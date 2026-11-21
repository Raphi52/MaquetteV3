using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Outils.Anonymisation;
using RigV3.Serveur.Recopie;

namespace RigV3.Repetition;

/// <summary>Ce que la repetition fait. Une base a repeter vient d'une SOURCE (la base V2 d'un greffe, anonymisee ici
/// avant tout le reste) ou d'une COPIE deja anonymisee sur ce poste.</summary>
public sealed record OptionsRepetition
{
    public IReadOnlyList<string> Sources { get; init; } = [];
    public IReadOnlyList<string> Copies { get; init; } = [];
    /// <summary>Une source hors de ce poste : son nom de base, l'accord donne pour CETTE base (SOC-021 CA07).</summary>
    public string? Accord { get; init; }
    public required string DossierRapport { get; init; }
    public required string DossierMigrations { get; init; }
    /// <summary>Lignes copiees par table au plus (copie reduite : une base ENTIERE en tables, pas en volume).</summary>
    public long? MaxLignes { get; init; }
    /// <summary>Valeurs personnelles tirees de la source et cherchees dans la copie (REC-008 T05 : 1 000).</summary>
    public int Echantillon { get; init; } = 1000;
    public int TailleLot { get; init; } = 50_000;
    /// <summary>Garder la copie et la base V3 apres la repetition (sinon supprimees : elles ne servent qu'a mesurer).</summary>
    public bool Garder { get; init; }
    /// <summary>Valeurs fictives reproductibles : source sur ce poste seulement (refusee sinon par l'anonymisation).</summary>
    public string? Graine { get; init; }
    public TextWriter? Sortie { get; init; }
}

/// <summary>Les lignes rejetees d'une table, par regle (REC-008 CA02), et la regle de reprise ecrite pour elles (CA03).</summary>
public sealed record RejetsTable(string Table, string Regle, long Lignes, string Exemple, string? RepriseDesRejets);

/// <summary>Une base repetee et ses mesures (REC-008 CA02).</summary>
/// <param name="Etat">fini ; ou l'etape qui a arrete la base (anonymisation, controle de la copie, copie non anonymisee,
/// transfert, controle des bases).</param>
public sealed record BaseRepetee(
    string Base, string Source, string? Copie, string? BaseV3, string Etat, string? Motif,
    bool CopieAnonymisee, string? ControleCopie,
    long? DureeAnonymisationMs, long? DureeControleCopieMs, long? DureeTransfertMs, long? DureeComparaisonMs,
    string? EtatTransfert, int? IdTransfert, long? Lignes, long? Octets, int Tables, int Provisoires,
    IReadOnlyList<RejetsTable> Rejets,
    long? RetardMaxRecopieMs, string RetardRecopie,
    int? IdComparaison, long? LignesComparees, long? Ecarts, long? Orphelins, long? SansCorrespondance)
{
    /// <summary>Copie reduite (--max-lignes) : toutes les tables, pas tout le volume. Le feu vert la refuse.</summary>
    public long? MaxLignes { get; init; }
}

public sealed record RapportRepetition(DateTime Debut, DateTime Fin, string Poste, IReadOnlyList<BaseRepetee> Bases, IReadOnlyList<string> Journal)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

/// <summary>
/// LA REPETITION DU TRANSFERT SUR DES COPIES ANONYMISEES (REC-008), base par base :
///  1. ANONYMISATION (CA01, CA05) : la source est LUE (SELECT seul) et la copie ecrite deja anonymisee sur ce poste
///     (Outils\Anonymisation : aucune valeur reelle n'y est jamais ecrite) ; puis son controle -- un echantillon de
///     <see cref="OptionsRepetition.Echantillon"/> valeurs personnelles de la source cherchees dans la copie, volumes et
///     numeros gardes -- doit etre vert. Le journal de preparation dit, pour chaque base, d'ou elle a ete lue et ou la
///     copie anonymisee a ete ecrite : aucune copie en clair n'existe, rien n'est deplace avant l'anonymisation.
///  2. GARDE : le transfert ne lit qu'une copie MARQUEE anonymisee (anonymisation.Copie) ; une copie en clair est
///     refusee et le journal le dit (CA05).
///  3. TRANSFERT (REC-001, REC-005) dans une base V3 neuve, regles PROVISOIRES pour les tables sans regle (marquees,
///     comptees) : duree, lignes, rejets par table et regle.
///  4. CONTROLE des deux bases (REC-006) : ecarts, liens orphelins, lignes sans correspondance.
///  5. MESURES et rapport consolide (JSON et Markdown) : par base, la duree du transfert, les rejets par table et motif,
///     le retard maximal de recopie, les ecarts du controle (CA02). Le feu vert du jour J se juge sur ce rapport (Feu).
/// La recopie continue d'une journee simulee (REC-008 T04) n'est pas encore jouable : la recopie (REC-002/003) est le
/// lot 9. Son retard est donc ecrit « non mesure », et le feu vert le refuse.
/// </summary>
public static class Repetition
{
    public const string RetardNonMesure = "non mesure : la recopie continue (REC-002, REC-003 : lot 9) et le simulateur d'une journee d'ecritures " +
                                          "(REC-008 T04) ne sont pas encore dans ce depot";

    public static async Task<RapportRepetition> RepeterAsync(OptionsRepetition o, CancellationToken ct = default)
    {
        var debut = DateTime.Now;
        var journal = new List<string>();
        void Noter(string ligne)
        {
            var l = $"{DateTime.Now:HH:mm:ss} {ligne}";
            journal.Add(l);
            o.Sortie?.WriteLine(l);
        }
        Directory.CreateDirectory(o.DossierRapport);
        var serveurLocal = BaseJetable.ServeurDesBancs();
        var bases = new List<BaseRepetee>();
        var reglesServeur = ReglesTransfert.Decouvrir().Regles.ToDictionary(r => r.Table, StringComparer.OrdinalIgnoreCase);
        var etiquette = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        foreach (var (chaine, estCopie) in o.Sources.Select(s => (s, false)).Concat(o.Copies.Select(c => (c, true))))
        {
            var src = new SqlConnectionStringBuilder(chaine);
            var nomBase = src.InitialCatalog;
            var propre = new string([.. nomBase.Where(char.IsAsciiLetterOrDigit).Take(40)]);
            string? copie = null, v3 = null, controle = null;
            long? dAnon = null, dControle = null;
            var marquee = false;
            BaseRepetee Fin(string etat, string? motif, long? dTransfert = null, RapportTransfert? rt = null, RapportComparaison? rc = null,
                            long? octets = null, IReadOnlyList<RejetsTable>? rejets = null)
                => new(nomBase, $"{src.DataSource} / {nomBase}", copie is null ? null : new SqlConnectionStringBuilder(copie).InitialCatalog,
                       v3 is null ? null : new SqlConnectionStringBuilder(v3).InitialCatalog, etat, motif, marquee, controle,
                       dAnon, dControle, dTransfert, rc?.DureeMs, rt?.Etat, rt?.IdTransfert,
                       rt?.Tables.Where(t => t.Ecrites is not null).Sum(t => t.Lues ?? 0), octets, rt?.Tables.Count ?? 0, rt?.Provisoires ?? 0,
                       rejets ?? [], null, RetardNonMesure, rc?.Id, rc?.LignesComparees, rc?.Ecarts, rc?.Orphelins, rc?.SansCorrespondance)
                   { MaxLignes = o.MaxLignes };
            try
            {
                // 1. L'anonymisation, avant tout deplacement.
                if (!estCopie)
                {
                    copie = new SqlConnectionStringBuilder(serveurLocal) { InitialCatalog = $"rigv3_rep_{propre}_{etiquette}" }.ConnectionString;
                    Noter($"{nomBase} : source {src.DataSource} lue en lecture seule (SELECT seul) par l'anonymisation ; la copie s'ecrit DEJA anonymisee " +
                          $"dans {new SqlConnectionStringBuilder(copie).InitialCatalog} ({new SqlConnectionStringBuilder(copie).DataSource}) — aucune copie en clair n'est ecrite ni deplacee");
                    var chrono = Stopwatch.StartNew();
                    var anon = await CopieAnonymisee.CopierAsync(new OptionsCopie
                    {
                        Source = chaine,
                        Cible = copie,
                        Accord = o.Accord,
                        MaxLignes = o.MaxLignes,
                        Graine = o.Graine,
                        FichierRapport = Path.Combine(o.DossierRapport, $"{propre}-anonymisation.md"),
                    }, ct);
                    dAnon = chrono.ElapsedMilliseconds;
                    Noter($"{nomBase} : copie anonymisee en {dAnon / 1000.0:0.0} s — {anon.Volumes.Count} tables, {anon.Colonnes.Count} colonnes traitees, " +
                          $"{anon.Anomalies.Count} anomalie(s)");
                    if (anon.Anomalies.Count > 0)
                    {
                        bases.Add(Fin("anonymisation", "anomalies de la copie : " + string.Join(" ; ", anon.Anomalies.Take(5))));
                        continue;
                    }
                    chrono.Restart();
                    var r = await ControleCopie.ControlerAsync(chaine, copie, o.Accord, o.Echantillon, ct);
                    dControle = chrono.ElapsedMilliseconds;
                    var (vert, reduite) = JugerControle(r, o.MaxLignes);
                    controle = $"{(vert ? "vert" : "ROUGE")} : {r.ColonnesControlees} colonnes personnelles, {r.EchantillonValeurs} valeurs et {r.EchantillonMots} mots " +
                               $"tires de la source, retrouves dans la copie : {(r.Fuites.Count == 0 ? "aucun" : string.Join(", ", r.Fuites.Take(10)))} ; " +
                               $"{r.TablesComparees} tables, " + (reduite ??
                               $"volumes differents : {(r.VolumesDifferents.Count == 0 ? "aucun" : string.Join(", ", r.VolumesDifferents))} ; " +
                               $"cles et numeros gardes changes : {(r.NumerosChanges.Count == 0 ? "aucun" : string.Join(", ", r.NumerosChanges))}");
                    Noter($"{nomBase} : controle de la copie en {dControle / 1000.0:0.0} s — {controle}");
                    if (!vert)
                    {
                        bases.Add(Fin("controle de la copie", controle));
                        continue;
                    }
                }
                else copie = chaine;

                // 2. La garde : une copie en clair ne sert jamais de jeu de repetition (C17, CA05).
                BaseJetable.ExigerServeurLocal(copie);
                marquee = await EstAnonymiseeAsync(copie, ct);
                if (!marquee)
                {
                    Noter($"{nomBase} : REFUS — {new SqlConnectionStringBuilder(copie).InitialCatalog} n'a pas la marque d'anonymisation (anonymisation.Copie) : " +
                          "une copie en clair ne sert jamais de jeu de repetition ; elle n'est pas lue");
                    bases.Add(Fin("copie non anonymisee", "la copie n'a pas la marque d'anonymisation (Outils\\Anonymisation) : refusee avant toute lecture"));
                    continue;
                }
                Noter($"{nomBase} : copie {new SqlConnectionStringBuilder(copie).InitialCatalog} marquee anonymisee : elle sert de jeu de repetition");

                // 3. Le transfert, dans une base V3 neuve.
                v3 = new SqlConnectionStringBuilder(serveurLocal) { InitialCatalog = $"rigv3_rep_{propre}_v3_{etiquette}" }.ConnectionString;
                await CreerBaseAsync(v3, ct);
                await Migrateur.AppliquerAsync(v3, o.DossierMigrations, ct: ct);
                var rt = await MoteurTransfert.TransfererAsync(new OptionsTransfert(copie, v3) { ReglesProvisoires = true, TailleLot = o.TailleLot }, ct);
                Noter($"{nomBase} : transfert {rt.IdTransfert} {rt.Etat.ToUpperInvariant()} en {rt.DureeMs / 1000.0:0.0} s — {rt.Tables.Count} tables, " +
                      $"{rt.Provisoires} par une regle PROVISOIRE{(rt.Motif is null ? "" : " — " + rt.Motif)}");
                var (octets, rejets) = await MesuresAsync(v3, rt.IdTransfert, reglesServeur, ct);
                if (rt.Etat != "fini")
                {
                    bases.Add(Fin("transfert", rt.Motif, rt.DureeMs, rt, null, octets, rejets));
                    continue;
                }

                // 4. Le controle des deux bases.
                var rc = await ComparaisonBases.ComparerAsync(new OptionsComparaison(copie, v3), ct);
                Noter($"{nomBase} : controle {rc.Id} {rc.Etat.ToUpperInvariant()} en {rc.DureeMs / 1000.0:0.0} s — {rc.LignesComparees} lignes comparees, " +
                      $"{rc.Ecarts} ecarts, {rc.Orphelins} liens orphelins, {rc.SansCorrespondance} sans correspondance");
                bases.Add(Fin(rc.Etat == "fini" ? "fini" : "controle des bases", rc.Motif, rt.DureeMs, rt, rc, octets, rejets));
            }
            catch (Exception e) when (e is RefusAnonymisation or InvalidOperationException or SqlException or IOException)
            {
                Noter($"{nomBase} : ARRET — {e.Message}");
                bases.Add(Fin("erreur", e.Message));
            }
            finally
            {
                if (!o.Garder)
                {
                    if (v3 is not null) await SupprimerAsync(v3);
                    if (copie is not null && !estCopie) await SupprimerAsync(copie);
                }
            }
        }

        var rapport = new RapportRepetition(debut, DateTime.Now, Environment.MachineName, bases, journal);
        var nom = Path.Combine(o.DossierRapport, $"repetition-{debut:yyyyMMdd-HHmmss}");
        await File.WriteAllTextAsync(nom + ".json", JsonSerializer.Serialize(rapport, RapportRepetition.Json), ct);
        await File.WriteAllTextAsync(nom + ".md", EnMarkdown(rapport), ct);
        return rapport;
    }

    /// <summary>
    /// Le verdict du controle de la copie. Copie COMPLETE : celui de l'outil (ResultatControle.Vert). Copie REDUITE
    /// (--max-lignes) : volumes et lignes gardees y different par construction (docs\reconstruction\socle-anonymisation.md,
    /// « Volumes et lignes gardees different par construction ») ; on exige alors qu'aucune valeur reelle ne soit retrouvee,
    /// que les colonnes a vider le soient, que la marque soit la, qu'aucune colonne gardee ne porte de prenoms, et que chaque
    /// volume soit celui de la reduction (min(source, N)) ; les numeros gardes ne sont pas juges. Limite ecrite au rapport :
    /// dans une copie reduite, la plupart des valeurs tirees n'y sont pas -- l'absence de fuite se prouve sur une copie complete.
    /// </summary>
    internal static (bool Vert, string? Reduite) JugerControle(ResultatControle r, long? maxLignes)
    {
        if (maxLignes is not { } max) return (r.Vert, null);
        var horsReduction = r.VolumesDifferents.Where(v => Regex.Match(v, @"\((\d+) dans la source, (\d+) dans la copie\)$") is not { Success: true } m
                                                           || long.Parse(m.Groups[2].Value) != Math.Min(long.Parse(m.Groups[1].Value), max)).ToList();
        var vert = r.Fuites.Count == 0 && r.NonVidees.Count == 0 && r.Marquee && !r.Gardees.Any(g => g.Prenoms) && !r.GardeesNonJugees && horsReduction.Count == 0;
        return (vert, $"copie REDUITE a {max} lignes par table : {r.VolumesDifferents.Count - horsReduction.Count} volumes reduits comme demande" +
                      (horsReduction.Count > 0 ? $", HORS REDUCTION : {string.Join(", ", horsReduction)}" : "") +
                      " ; numeros gardes non juges (les lignes tirees de la source n'y sont pas toutes) ; l'absence de fuite se prouve sur une copie complete");
    }

    /// <summary>La copie porte-t-elle la marque de l'outil d'anonymisation (schema anonymisation, table Copie) ?</summary>
    public static async Task<bool> EstAnonymiseeAsync(string copie, CancellationToken ct)
    {
        await using var cn = new SqlConnection(copie);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM sys.tables WHERE name = N'Copie' AND SCHEMA_NAME(schema_id) = N'{CopieAnonymisee.SchemaMarque}';", cn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    private static async Task<(long Octets, List<RejetsTable> Rejets)> MesuresAsync(string v3, int idTransfert, Dictionary<string, RegleTable> regles, CancellationToken ct)
    {
        await using var cn = new SqlConnection(v3);
        await cn.OpenAsync(ct);
        long octets;
        await using (var cmd = new SqlCommand("SELECT ISNULL(SUM(Octets), 0) FROM rec.TransfertTable WHERE IdTransfert = @id;", cn))
        {
            cmd.Parameters.AddWithValue("@id", idTransfert);
            octets = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        var rejets = new List<RejetsTable>();
        await using (var cmd = new SqlCommand("SELECT TableV2, Regle, COUNT_BIG(*), MIN(Motif) FROM rec.Rejet WHERE IdTransfert = @id GROUP BY TableV2, Regle ORDER BY TableV2, Regle;", cn))
        {
            cmd.Parameters.AddWithValue("@id", idTransfert);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
                rejets.Add(new RejetsTable(l.GetString(0), l.GetString(1), l.GetInt64(2), l.GetString(3),
                                           regles.TryGetValue(l.GetString(0), out var r) ? r.RepriseDesRejets : null));
        }
        return (octets, rejets);
    }

    private static async Task CreerBaseAsync(string chaine, CancellationToken ct)
    {
        BaseJetable.ExigerServeurLocal(chaine);
        var nom = new SqlConnectionStringBuilder(chaine).InitialCatalog;
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(chaine) { InitialCatalog = "master" }.ConnectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"CREATE DATABASE [{nom.Replace("]", "]]")}] COLLATE {BaseJetable.Collation};", cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Supprime une base de la repetition (sur ce poste seulement) et ce qui reste de ses instantanes.</summary>
    private static async Task SupprimerAsync(string chaine)
    {
        BaseJetable.ExigerServeurLocal(chaine);
        var nom = new SqlConnectionStringBuilder(chaine).InitialCatalog;
        using (var c = new SqlConnection(chaine)) SqlConnection.ClearPool(c);
        await using var cn = new SqlConnection(new SqlConnectionStringBuilder(chaine) { InitialCatalog = "master" }.ConnectionString);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            DECLARE @s nvarchar(max) = N'';
            SELECT @s += N'DROP DATABASE ' + QUOTENAME(name) + N'; ' FROM sys.databases WHERE source_database_id = DB_ID(@n);
            EXEC (@s);
            IF DB_ID(@n) IS NOT NULL
            BEGIN
                SET @s = N'ALTER DATABASE ' + QUOTENAME(@n) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@n) + N';';
                EXEC (@s);
            END
            """, cn);
        cmd.Parameters.AddWithValue("@n", nom);
        await cmd.ExecuteNonQueryAsync();
    }

    public static string EnMarkdown(RapportRepetition r)
    {
        var s = new StringBuilder();
        s.AppendLine($"# Répétition du transfert — {r.Debut:yyyy-MM-dd HH:mm} → {r.Fin:HH:mm} ({r.Poste})").AppendLine();
        s.AppendLine($"{r.Bases.Count(b => b.Etat == "fini")} base(s) répétée(s) jusqu'au bout sur {r.Bases.Count}. Feu vert du jour J : `RigV3.Repetition feu`.").AppendLine();
        s.AppendLine("| Base | État | Copie anonymisée | Transfert | Lignes | Tables (provisoires) | Rejets | Retard de recopie | Contrôle : écarts / orphelins / sans corresp. |");
        s.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var b in r.Bases)
            s.AppendLine($"| {b.Base} | {b.Etat} | {(b.CopieAnonymisee ? "oui" : "non")} | {(b.DureeTransfertMs is { } d ? $"{d / 1000.0:0.0} s" : "—")} | {b.Lignes?.ToString() ?? "—"} | " +
                         $"{b.Tables} ({b.Provisoires}) | {b.Rejets.Sum(x => x.Lignes)} | {(b.RetardMaxRecopieMs is { } rr ? $"{rr} ms" : "non mesuré")} | " +
                         $"{b.Ecarts?.ToString() ?? "—"} / {b.Orphelins?.ToString() ?? "—"} / {b.SansCorrespondance?.ToString() ?? "—"} |");
        foreach (var b in r.Bases)
        {
            s.AppendLine().AppendLine($"## {b.Base}").AppendLine();
            s.AppendLine($"- Source : {b.Source} ; copie : {b.Copie ?? "—"} ; base V3 : {b.BaseV3 ?? "—"}");
            if (b.Motif is not null) s.AppendLine($"- Arrêt : {b.Motif}");
            if (b.ControleCopie is not null) s.AppendLine($"- Contrôle de la copie : {b.ControleCopie}");
            s.AppendLine($"- Durées : anonymisation {Ms(b.DureeAnonymisationMs)}, contrôle de la copie {Ms(b.DureeControleCopieMs)}, transfert {Ms(b.DureeTransfertMs)}, " +
                         $"contrôle des bases {Ms(b.DureeComparaisonMs)} ; {b.Octets?.ToString() ?? "—"} octets écrits");
            s.AppendLine($"- Retard maximal de recopie : {(b.RetardMaxRecopieMs is { } rr ? $"{rr} ms" : b.RetardRecopie)}");
            foreach (var x in b.Rejets)
                s.AppendLine($"- Rejets {x.Table} ({x.Regle}) : {x.Lignes} ligne(s), ex. « {x.Exemple} » ; règle de reprise : {x.RepriseDesRejets ?? "AUCUNE ÉCRITE"}");
        }
        s.AppendLine().AppendLine("## Journal de préparation").AppendLine();
        foreach (var l in r.Journal) s.AppendLine($"- {l}");
        return s.ToString();
    }

    private static string Ms(long? ms) => ms is { } v ? $"{v / 1000.0:0.0} s" : "—";
}
