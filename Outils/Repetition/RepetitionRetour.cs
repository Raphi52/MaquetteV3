using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Recopie;
using RigV3.Passage;
using RigV3.Serveur.Recopie;

namespace RigV3.Repetition;

/// <summary>Ce que la repetition du retour fait : une SOURCE (la base V2 d'un greffe beta, anonymisee ici avant tout le
/// reste) ou une COPIE deja anonymisee sur ce poste.</summary>
public sealed record OptionsRetour
{
    public string? Source { get; init; }
    public string? Copie { get; init; }
    /// <summary>Une source hors de ce poste : son nom de base, l'accord donne pour CETTE base (SOC-021 CA07).</summary>
    public string? Accord { get; init; }
    public required string DossierRapport { get; init; }
    public required string DossierMigrations { get; init; }
    /// <summary>Base\v2 : recopie-v2.sql et pont-v2.sql, les scripts memes que l'exploitation joue.</summary>
    public required string DossierScriptsV2 { get; init; }
    /// <summary>Copie reduite (--max-lignes) : toutes les tables, au plus N lignes chacune, fermee sur ses cles.</summary>
    public long? MaxLignes { get; init; }
    /// <summary>Demandes du mois simule ; null : le mois le plus charge des 36 derniers de la SOURCE (lu par un SELECT).</summary>
    public int? DemandesDuMois { get; init; }
    /// <summary>La fenetre de nuit (CA07 : « il se termine en une nuit ») : 20 h -> 6 h.</summary>
    public double FenetreHeures { get; init; } = 10;
    public string? Greffe { get; init; }
    public bool Garder { get; init; }
    public string? Graine { get; init; }
    public TextWriter? Sortie { get; init; }
}

/// <summary>Un controle de nuit, resume pour le rapport.</summary>
public sealed record ControleNuit(string Etat, int CodeSortie, int Tables, long LignesComparees, long Ecarts, long Orphelins, long SansCorrespondance,
                                  long ATraiter, long DureeMs, string? AuDetail);

/// <param name="Vert">CA07 tenu : le retour du mois est fait, dans la fenetre de nuit, le controle de nuit ne trouve ensuite
/// rien a traiter, et sa contre-epreuve voit le defaut qu'on lui montre.</param>
public sealed record RapportRetour(
    DateTime Debut, DateTime Fin, string Poste, string Source, string? Copie, string? BaseV3, long? MaxLignes,
    string? Repetition, string MoisSimule, int AffairesSimulees, int DemandesSimulees, long? DureeSimulationMs,
    string? DeclencheursValidationV2, ControleNuit? PendantBeta, IReadOnlyList<string> Refus,
    string? EtatRetour, string? DetailRetour, int AffairesRendues, int DemandesRendues, long? DureeRetourMs,
    ControleNuit? ApresRetour, string? ContreEpreuve, bool Vert, string Verdict, IReadOnlyList<string> Journal);

/// <summary>
/// LA REPETITION DU RETOUR A L'ANCIEN RIG (REC-007 CA07) : « etant donne la copie anonymisee d'un greffe beta avec un
/// mois d'affaires V3 simulees, quand on repete le retour, il se termine en une nuit et le controle quotidien (REC-006)
/// ne trouve aucun ecart ensuite ». Dans l'ordre :
///  1. La repetition du transfert (<see cref="Repetition.RepeterAsync"/>, REC-008), gardee : copie anonymisee (SOC-021,
///     la source est LUE), son controle, puis Base\v2\recopie-v2.sql sur la copie AVANT le transfert, le transfert dans une
///     base V3 neuve et le controle des deux bases.
///  2. Le greffe mis en beta comme la bascule le laisse : la recopie mise en service (garde des affaires, PontRecopie),
///     Base\v2\pont-v2.sql, la fiche du pont (fin de beta dans 30 jours) et le corps d'origine des trois programmes de
///     numerotation garde (NUM-022 : sans lui, le greffe n'est pas en beta) ; la personne designee pour le retour (CA08).
///  3. Un MOIS d'affaires creees dans la V3 : autant de demandes que le mois le plus charge de la source, deux par affaire
///     (l'une validee a une date du mois, l'autre en cours), copiees d'affaires transferees, agents et types en
///     identifiants de la V3 -- comme le banc RecopieRetourCheck, au volume reel.
///  4. Un passage de la recopie et le controle de nuit pendant la beta ; le retour, chronometre ; un passage et le controle
///     de nuit complet ; sa contre-epreuve : la correspondance d'une affaire rendue mise de cote, le controle doit voir ses
///     deux demandes orphelines (puis elle est remise).
///  5. Le rapport retour-&lt;date&gt;.json et .md ; copie et base V3 supprimees sauf --garder.
/// Les identifiants que la V2 donne sont decales de 100 000 avant le mois (comme apres des mois ou chacun cree ses lignes) :
/// un identifiant de la V3 ecrit tel quel dans la V2 y viserait une autre ligne, et le controle le verrait.
/// Ce qui n'est PAS joue : bascule.sql (les programmes de numerotation remplaces) et son retour (retour.sql, NUM-022, prouve
/// par SoclePassageCheck) -- ce qui se mesure ici est le retour des affaires (Serveur\Recopie\RetourBeta.cs).
/// </summary>
public static class RepetitionRetour
{
    private static readonly string[] Programmes = ["GENERER_NUMERO", "CHERCHER_NUMERO", "RESTITUER_NUMERO"];
    private const int TailleLot = 5000;

    public static async Task<RapportRetour> RepeterAsync(OptionsRetour o, CancellationToken ct = default)
    {
        if ((o.Source is null) == (o.Copie is null)) throw new ArgumentException("une source OU une copie anonymisee");
        if (o.Source is null && o.DemandesDuMois is null)
            throw new ArgumentException("--mois est exige avec --copie : une copie reduite n'a pas le volume de la source");
        var debut = DateTime.Now;
        var journal = new List<string>();
        void Noter(string ligne)
        {
            var l = $"{DateTime.Now:HH:mm:ss} {ligne}";
            journal.Add(l);
            o.Sortie?.WriteLine(l);
        }
        var serveur = BaseJetable.ServeurDesBancs();
        var nomSource = new SqlConnectionStringBuilder(o.Source ?? o.Copie).InitialCatalog;
        var greffe = o.Greffe ?? new string([.. nomSource.Take(20)]);
        var responsable = $@"{Environment.UserDomainName}\{Environment.UserName}";
        string? copie = null, v3 = null, repetition = null, declencheurs = null, etatRetour = null, detailRetour = null, contreEpreuve = null;
        string moisSimule = "";
        int affaires = 0, demandes = 0, affairesRendues = 0, demandesRendues = 0;
        long? dureeSimulation = null, dureeRetour = null;
        ControleNuit? pendantBeta = null, apres = null;
        var refus = new List<string>();

        RapportRetour Rapport(bool vert, string verdict) => new(debut, DateTime.Now, Environment.MachineName, nomSource,
            Nom(copie), Nom(v3), o.MaxLignes, repetition, moisSimule, affaires, demandes, dureeSimulation, declencheurs, pendantBeta, refus,
            etatRetour, detailRetour, affairesRendues, demandesRendues, dureeRetour, apres, contreEpreuve, vert, verdict, journal);

        RapportRetour rapport;
        try { rapport = await DeroulerAsync(); }
        catch (Exception e) when (e is InvalidOperationException or SqlException or IOException or ArgumentException)
        {
            Noter($"ARRET -- {e.Message}");
            rapport = Rapport(false, $"ROUGE : arret -- {e.Message}");
        }
        finally
        {
            if (!o.Garder)
            {
                if (v3 is not null) await Repetition.SupprimerAsync(v3);
                if (copie is not null && o.Copie is null) await Repetition.SupprimerAsync(copie);
            }
        }
        Directory.CreateDirectory(o.DossierRapport);
        var fichier = Path.Combine(o.DossierRapport, $"retour-{debut:yyyyMMdd-HHmmss}");
        await File.WriteAllTextAsync(fichier + ".json", JsonSerializer.Serialize(rapport, RapportRepetition.Json), ct);
        await File.WriteAllTextAsync(fichier + ".md", EnMarkdown(rapport), ct);
        return rapport;

        // Chaque etape qui echoue rend un rapport ROUGE qui dit laquelle ; le rapport est ecrit dans tous les cas.
        async Task<RapportRetour> DeroulerAsync()
        {
            // 0. Le volume d'un mois : celui de la source, lu par un SELECT (aucune ecriture).
            if (o.DemandesDuMois is { } donne) (demandes, moisSimule) = (donne, $"{donne} demandes (donne par --mois)");
            else
            {
                var t = await LireAsync(o.Source!, """
                    SELECT TOP (1) YEAR(DMND_DATE_CREATION) * 100 + MONTH(DMND_DATE_CREATION), COUNT(*) FROM dbo.DEMANDE
                    WHERE DMND_DATE_CREATION >= DATEADD(month, -36, GETDATE())
                    GROUP BY YEAR(DMND_DATE_CREATION) * 100 + MONTH(DMND_DATE_CREATION) ORDER BY 2 DESC;
                    """, ct);
                if (t.Rows.Count == 0) throw new InvalidOperationException($"{nomSource} n'a aucune demande creee dans les 36 derniers mois : passer --mois N");
                var mois = Convert.ToInt32(t.Rows[0][0], CultureInfo.InvariantCulture);
                demandes = Convert.ToInt32(t.Rows[0][1], CultureInfo.InvariantCulture);
                moisSimule = $"{demandes} demandes : le mois le plus charge des 36 derniers de {nomSource} ({mois % 100:00}/{mois / 100})";
            }
            affaires = (demandes + 1) / 2;
            demandes = affaires * 2;
            Noter($"mois simule : {affaires} affaires de la V3, deux demandes chacune ({demandes}) -- {moisSimule}");

            // 1. La repetition du transfert, gardee ; le suivi des changements pose sur la copie AVANT le transfert.
            var reglesRecopie = ReglesRecopie.Decouvrir();
            var rep = await Repetition.RepeterAsync(new OptionsRepetition
            {
                Sources = o.Source is null ? [] : [o.Source],
                Copies = o.Copie is null ? [] : [o.Copie],
                Accord = o.Accord,
                DossierRapport = o.DossierRapport,
                DossierMigrations = o.DossierMigrations,
                MaxLignes = o.MaxLignes,
                Garder = true,
                Graine = o.Graine,
                Sortie = o.Sortie,
                AvantTransfert = async (c, jeton) =>
                {
                    var m = await ScriptSqlcmd.JouerAsync(c, Path.Combine(o.DossierScriptsV2, "recopie-v2.sql"), new Dictionary<string, string>
                    {
                        ["BaseV2"] = Nom(c)!, ["RetentionJours"] = "7", ["Tables"] = string.Join(",", reglesRecopie.Select(r => r.TableV2)),
                    }, jeton);
                    return $"Base\\v2\\recopie-v2.sql joue sur la copie avant le transfert ({reglesRecopie.Count} tables partagees suivies){(m.Count > 0 ? " : " + m[^1] : "")}";
                },
            }, ct);
            var b = rep.Bases.Single();
            copie = b.Copie is null ? null : Chaine(serveur, b.Copie);
            v3 = b.BaseV3 is null ? null : Chaine(serveur, b.BaseV3);
            journal.AddRange(rep.Journal);
            repetition = $"{b.Etat} -- copie anonymisee {Ms(b.DureeAnonymisationMs)}, son controle {Ms(b.DureeControleCopieMs)} ({b.ControleCopie}) ; " +
                         $"transfert {Ms(b.DureeTransfertMs)} ({b.Tables} tables, {b.Provisoires} par une regle provisoire, {b.Lignes} lignes, " +
                         $"{b.Rejets.Sum(r => r.Lignes)} rejet(s)) ; controle des deux bases {Ms(b.DureeComparaisonMs)} : {b.Ecarts} ecart(s), " +
                         $"{b.Orphelins} lien(s) orphelin(s), {b.SansCorrespondance} sans correspondance";
            if (b.Etat != "fini" || copie is null || v3 is null)
                return Rapport(false, $"ROUGE : la repetition du transfert s'est arretee ({b.Etat}) : {b.Motif}");
            var baseV2 = b.Copie!;

            // 2. Le greffe en beta, comme la bascule le laisse.
            var mes = await PontRecopie.MettreEnServiceAsync(v3, baseV2, ct: ct);
            Noter($"mise en service de la recopie : {(mes.Faite ? "faite" : "REFUSEE")} -- {string.Join(", ", mes.Declencheurs.Where(d => d.Contains("Garde")))}" +
                  $"{(mes.Refus.Count > 0 ? " ; refus : " + string.Join(" ; ", mes.Refus) : "")}");
            if (!mes.Faite) return Rapport(false, "ROUGE : la mise en service de la recopie est refusee : " + string.Join(" ; ", mes.Refus));
            var t2 = await LireAsync(copie, "SELECT name, is_disabled FROM sys.triggers WHERE parent_id = OBJECT_ID(N'dbo.DEMANDE') AND name IN (N'T_DEMANDE_VALIDATION', N'T_DEMANDE_VALIDATION_INS') ORDER BY name;", ct);
            declencheurs = t2.Rows.Count == 0 ? "aucun dans la copie" : string.Join(", ", t2.Rows.Cast<DataRow>().Select(r => $"{r[0]} {(Convert.ToBoolean(r[1], CultureInfo.InvariantCulture) ? "DESACTIVE" : "actif")}"));
            Noter($"declencheurs de date de validation de la V2 (CA02) : {declencheurs}");
            foreach (var (table, cle) in new[] { ("AFFAIRE", "AFFRE_ID_AFFRE"), ("DEMANDE", "DMND_ID_DMND") })
                await ExecuterAsync(copie, $"DECLARE @m int = ISNULL((SELECT MAX({cle}) FROM dbo.{table}), 0) + 100000; DBCC CHECKIDENT ('dbo.{table}', RESEED, @m) WITH NO_INFOMSGS;", ct);
            await ScriptSqlcmd.JouerAsync(copie, Path.Combine(o.DossierScriptsV2, "pont-v2.sql"), new Dictionary<string, string> { ["BaseV3"] = Nom(v3)! }, ct);
            // La copie d'un greffe deja bascule porte SA fiche du pont (vers sa base V3 de production) et ses corps d'origine :
            // la fiche est re-pointee vers la V3 de la repetition, beta ouverte ; les corps gardes par la bascule restent.
            var pont = await LireAsync(copie, $"""
                DECLARE @existait bit = CASE WHEN EXISTS (SELECT 1 FROM rigv3.Pont WHERE Id = 1) THEN 1 ELSE 0 END;
                IF @existait = 1
                    UPDATE rigv3.Pont SET BaseV3 = N'{Nom(v3)}', FinBeta = DATEADD(day, 30, SYSDATETIME()), RetourLe = NULL, RetourPar = NULL WHERE Id = 1;
                ELSE
                    INSERT INTO rigv3.Pont (Id, CodeGreffe, BaseV3, BasculeLe, BasculePar, FinBeta)
                    VALUES (1, N'{greffe.Replace("'", "''")}', N'{Nom(v3)}', DATEADD(day, -31, SYSDATETIME()), N'{responsable.Replace("'", "''")}', DATEADD(day, 30, SYSDATETIME()));
                INSERT INTO rigv3.CorpsOrigine (Objet, Definition, QuotedIdentifier, AnsiNulls, Droits, GardeLe)
                SELECT o.name, m.definition, m.uses_quoted_identifier, m.uses_ansi_nulls, NULL, SYSDATETIME()
                FROM sys.sql_modules AS m JOIN sys.objects AS o ON o.object_id = m.object_id
                WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.name IN ({string.Join(", ", Programmes.Select(p => $"N'{p}'"))}) AND m.definition IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM rigv3.CorpsOrigine AS c WHERE c.Objet = o.name);
                SELECT @existait, (SELECT COUNT(*) FROM rigv3.CorpsOrigine), (SELECT CodeGreffe FROM rigv3.Pont WHERE Id = 1);
                """, ct);
            greffe = (string)pont.Rows[0][2];
            Noter($"Base\\v2\\pont-v2.sql joue ; fiche du pont {(Convert.ToBoolean(pont.Rows[0][0], CultureInfo.InvariantCulture) ? "deja la (greffe bascule) : re-pointee vers la V3 de la repetition" : "posee : bascule il y a 31 jours")}, " +
                  $"greffe {greffe}, fin de beta dans 30 jours ; corps d'origine gardes : {pont.Rows[0][1]} (attendu : {Programmes.Length})");
            await using (var cn = new SqlConnection(v3))
            {
                await cn.OpenAsync(ct);
                await RetourBeta.DesignerAsync(cn, null, baseV2, responsable, "repetition du retour", ct);
            }

            // 3. Le mois d'affaires de la V3.
            var chrono = Stopwatch.StartNew();
            var modeles = await SimulerMoisAsync(v3, baseV2, affaires, ct);
            dureeSimulation = chrono.ElapsedMilliseconds;
            Noter($"mois simule ecrit dans la V3 en {Ms(dureeSimulation)} : {affaires} affaires et {demandes} demandes, copiees de {modeles} affaires transferees");

            // 4. Pendant la beta, le retour, apres.
            var passage = await ServiceRecopie.PasserAsync(v3, baseV2, ct: ct);
            pendantBeta = Resumer(await ComparaisonBases.ComparerAsync(new OptionsComparaison(copie, v3), ct));
            Noter($"controle de nuit pendant la beta : {Texte(pendantBeta)}{(passage.Erreur is null ? "" : " ; passage : " + passage.Erreur)}");
            PreparationRetour prep;
            await using (var cn = new SqlConnection(v3))
            {
                await cn.OpenAsync(ct);
                prep = await RetourBeta.PreparerAsync(cn, baseV2, greffe, ct);
            }
            refus.AddRange(prep.Refus);
            Noter($"avant le retour : {prep.Affaires} affaires et {prep.Demandes} demandes de la V3 ; refus : {(prep.Refus.Length == 0 ? "aucun" : string.Join(" ; ", prep.Refus))}");

            chrono.Restart();
            var retour = await RetourBeta.RevenirAsync(v3, baseV2, greffe, responsable, "repetition du retour a l'ancien RIG (REC-007 CA07)", ct: ct);
            dureeRetour = chrono.ElapsedMilliseconds;
            (etatRetour, detailRetour, affairesRendues, demandesRendues) = (retour.Etat, retour.Detail, retour.Affaires, retour.Demandes);
            Noter($"RETOUR {retour.Etat.ToUpperInvariant()} en {Ms(dureeRetour)} : {retour.Affaires} affaires, {retour.Demandes} demandes, {retour.Renvois} renvoi(s) -- {retour.Detail}");
            if (retour.Etat != "faite") return Rapport(false, $"ROUGE : le retour n'est pas fait ({retour.Etat}) : {retour.Detail}");

            var passageApres = await ServiceRecopie.PasserAsync(v3, baseV2, ct: ct);
            apres = Resumer(await ComparaisonBases.ComparerAsync(new OptionsComparaison(copie, v3), ct));
            Noter($"controle de nuit apres le retour : {Texte(apres)}{(passageApres.Erreur is null ? "" : " ; passage : " + passageApres.Erreur)}");

            // La contre-epreuve : le controle voit-il un retour casse ? La correspondance d'une affaire rendue mise de cote,
            // ses deux demandes (dans la V2) visent une affaire sans equivalent : deux liens orphelins, et seulement elles.
            var temoin = (await LireAsync(v3, """
                SELECT TOP (1) CONVERT(nvarchar(200), a.AFFRE_ID_AFFRE) FROM v2.AFFAIRE AS a
                WHERE a.AFFRE_LIBELLE LIKE 'REPETITION RETOUR %' ORDER BY a.AFFRE_ID_AFFRE;
                """, ct)).Rows[0][0].ToString()!;
            var demandesTemoin = (await LireAsync(v3, $"SELECT CONVERT(nvarchar(200), DMND_ID_DMND) FROM v2.DEMANDE WHERE DMND_ID_AFFRE = {temoin};", ct))
                .Rows.Cast<DataRow>().Select(r => r[0].ToString()!).ToHashSet();
            await ExecuterAsync(v3, $"UPDATE rec.Correspondance SET TableV2 = N'AFFAIRE_TEMOIN' WHERE BaseV2 = N'{baseV2}' AND TableV2 = N'AFFAIRE' AND TableV3 = N'v2.AFFAIRE' AND IdV3 = N'{temoin}';", ct);
            RapportComparaison vu;
            try { vu = await ComparaisonBases.ComparerAsync(new OptionsComparaison(copie, v3) { Tables = ["AFFAIRE", "DEMANDE"] }, ct); }
            finally { await ExecuterAsync(v3, $"UPDATE rec.Correspondance SET TableV2 = N'AFFAIRE' WHERE BaseV2 = N'{baseV2}' AND TableV2 = N'AFFAIRE_TEMOIN' AND IdV3 = N'{temoin}';", ct); }
            var orphelins = vu.Details.Where(d => d.Genre == "lien orphelin").ToList();
            var contreOk = vu.Orphelins == 2 && demandesTemoin.Count == 2 && orphelins.All(d => d.Colonnes == "DMND_ID_AFFRE" && demandesTemoin.Contains(d.IdV3!)) && vu.CodeSortie == 5;
            contreEpreuve = $"{(contreOk ? "vue" : "NON VUE")} : la correspondance de l'affaire rendue {temoin} mise de cote, le controle liste {vu.Orphelins} lien(s) orphelin(s) " +
                            $"(attendu : ses demandes {string.Join(" et ", demandesTemoin)}), code {vu.CodeSortie} ; correspondance remise";
            Noter($"contre-epreuve : {contreEpreuve}");

            var fenetre = TimeSpan.FromHours(o.FenetreHeures);
            var nuit = TimeSpan.FromMilliseconds(dureeRetour.Value + apres.DureeMs);
            var manques = new List<string>();
            if (affairesRendues != affaires || demandesRendues != demandes) manques.Add($"rendu {affairesRendues} affaires / {demandesRendues} demandes pour {affaires} / {demandes} simulees");
            if (nuit >= fenetre) manques.Add($"retour et controle en {nuit:hh\\:mm\\:ss}, au-dela de la fenetre de nuit ({o.FenetreHeures} h)");
            if (pendantBeta.CodeSortie != 0) manques.Add($"le controle de nuit pendant la beta a {pendantBeta.ATraiter} ligne(s) a traiter (code {pendantBeta.CodeSortie})");
            if (apres.CodeSortie != 0) manques.Add($"le controle de nuit apres le retour a {apres.ATraiter} ligne(s) a traiter (code {apres.CodeSortie})");
            if (!contreOk) manques.Add("la contre-epreuve n'a pas ete vue par le controle");
            var parLigne = (double)dureeRetour.Value / Math.Max(1, affairesRendues + demandesRendues);
            return Rapport(manques.Count == 0, manques.Count == 0
                ? $"VERT : {affairesRendues} affaires et {demandesRendues} demandes rendues a l'ancien RIG en {TimeSpan.FromMilliseconds(dureeRetour.Value):hh\\:mm\\:ss} " +
                  $"({parLigne:0.0} ms par ligne), controle de nuit compris {nuit:hh\\:mm\\:ss}, dans la fenetre de {o.FenetreHeures} h ; le controle de nuit ensuite : rien a traiter"
                : "ROUGE : " + string.Join(" ; ", manques));
        }
    }

    /// <summary>
    /// Ecrit dans la V3 <paramref name="affaires"/> affaires de deux demandes, reparties sur les 30 derniers jours : chacune
    /// copiee d'une affaire transferee (et de sa premiere demande) qui a un dossier et un type connus de la correspondance ;
    /// agents et type en identifiants de la V3 (RegleRecopie.NumerosV2 : une fiche de la V3 les porte ainsi), numero de demande
    /// « V3 » + rang, l'une validee deux heures apres sa creation, l'autre en cours. Par lots de <see cref="TailleLot"/>
    /// affaires : les declencheurs de garde de la V3 (GardeAffaires) voient des lots, comme plusieurs agents. Rend le nombre
    /// d'affaires modeles.
    /// </summary>
    private static async Task<int> SimulerMoisAsync(string v3, string baseV2, int affaires, CancellationToken ct)
    {
        await using var cn = new SqlConnection(v3);
        await cn.OpenAsync(ct);
        async Task<DataTable> Lire(string sql)
        {
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
            var t = new DataTable();
            await using var l = await cmd.ExecuteReaderAsync(ct);
            t.Load(l);
            return t;
        }
        async Task Executer(string sql, params (string Nom, object Valeur)[] p)
        {
            await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        async Task<List<string>> Colonnes(string table)
            => (await Lire($"SELECT c.name FROM sys.columns AS c WHERE c.object_id = OBJECT_ID(N'{table}') AND c.is_identity = 0 AND c.is_computed = 0 " +
                           "AND TYPE_NAME(c.system_type_id) <> 'timestamp' ORDER BY c.column_id;")).Rows.Cast<DataRow>().Select(r => (string)r[0]).ToList();
        string Selection(List<string> colonnes, string table, Dictionary<string, string> remplace)
        {
            var inconnues = remplace.Keys.Where(k => !colonnes.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
            if (inconnues.Count > 0) throw new InvalidOperationException($"{table} : colonnes inconnues {string.Join(", ", inconnues)}");
            return string.Join(", ", colonnes.Select(c => remplace.TryGetValue(c, out var v) ? v : $"m.[{c}]"));
        }
        var b = baseV2.Replace("'", "''");
        string V3De(string tableV2, string colonne) =>
            $"(SELECT TOP (1) TRY_CONVERT(int, k.IdV3) FROM rec.Correspondance AS k WHERE k.BaseV2 = N'{b}' AND k.TableV2 = N'{tableV2}' AND k.IdV2 = CONVERT(nvarchar(200), m.[{colonne}]))";

        // Un lot SANS parametre : avec des parametres, SqlClient passe par sp_executesql, et les tables temporaires creees
        // dans ce lot disparaitraient a sa fin. Le nombre d'affaires est un entier, ecrit tel quel.
        await Executer($"""
            CREATE TABLE #modele (K int NOT NULL PRIMARY KEY, Affaire int NOT NULL, Demande int NOT NULL);
            INSERT INTO #modele (K, Affaire, Demande)
            SELECT ROW_NUMBER() OVER (ORDER BY x.Affaire DESC) - 1, x.Affaire, x.Demande FROM (
                SELECT TOP (200) d.DMND_ID_AFFRE AS Affaire, MIN(d.DMND_ID_DMND) AS Demande FROM v2.DEMANDE AS d
                JOIN rec.Correspondance AS c ON c.BaseV2 = N'{b}' AND c.TableV3 = N'v2.DEMANDE' AND c.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_DMND)
                WHERE d.DMND_ID_DSRCS IS NOT NULL AND d.DMND_ID_CTGR IS NOT NULL AND d.DMND_ID_AFFRE IS NOT NULL
                  AND EXISTS (SELECT 1 FROM rec.Correspondance AS k WHERE k.BaseV2 = N'{b}' AND k.TableV2 = N'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), d.DMND_ID_CTGR))
                GROUP BY d.DMND_ID_AFFRE ORDER BY d.DMND_ID_AFFRE DESC) AS x;
            DECLARE @k int = (SELECT COUNT(*) FROM #modele);
            IF @k = 0 THROW 51099, N'Aucune affaire transferee avec un dossier et un type connus : pas de modele pour le mois simule.', 1;
            CREATE TABLE #plan (I int NOT NULL PRIMARY KEY, Affaire int NOT NULL, Demande int NOT NULL, Libelle varchar(70) NOT NULL, Jour datetime NOT NULL, Nouvelle int NULL);
            DECLARE @debut datetime = DATEADD(day, -30, CONVERT(datetime, CONVERT(date, GETDATE())));
            WITH n AS (SELECT TOP ({affaires}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS I FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS c)
            INSERT INTO #plan (I, Affaire, Demande, Libelle, Jour)
            SELECT n.I, m.Affaire, m.Demande, 'REPETITION RETOUR ' + RIGHT('000000' + CONVERT(varchar(6), n.I), 6),
                   DATEADD(second, CONVERT(int, (n.I - 1) * (2592000.0 / {affaires})), @debut)
            FROM n JOIN #modele AS m ON m.K = (n.I - 1) % @k;
            """);
        var modeles = Convert.ToInt32((await Lire("SELECT COUNT(*) FROM #modele;")).Rows[0][0], CultureInfo.InvariantCulture);

        var colsAffaire = await Colonnes("v2.AFFAIRE");
        var selAffaire = Selection(colsAffaire, "v2.AFFAIRE", new()
        {
            ["AFFRE_LIBELLE"] = "p.Libelle", ["AFFRE_DATE_CREATION"] = "p.Jour",
            ["AFFRE_ID_UTLST_CREATION"] = V3De("UTILISATEUR", "AFFRE_ID_UTLST_CREATION"), ["AFFRE_ID_UTLST_EN_COURS"] = V3De("UTILISATEUR", "AFFRE_ID_UTLST_EN_COURS"),
        });
        var colsDemande = await Colonnes("v2.DEMANDE");
        var selDemande = Selection(colsDemande, "v2.DEMANDE", new()
        {
            ["DMND_ID_AFFRE"] = "p.Nouvelle", ["DMND_NUM_DEMANDE"] = "'V3' + RIGHT('00000000' + CONVERT(varchar(8), p.I * 2 - 2 + k.N), 8)",
            ["DMND_ETAT_DEMANDE"] = "k.Etat", ["DMND_DATE_VALIDATION"] = "CASE k.N WHEN 1 THEN DATEADD(hour, 2, p.Jour) END",
            ["DMND_ID_DMND_MERE"] = "NULL", ["DMND_ID_DMND_INHIBITRICE"] = "NULL", ["DMND_INHIBEE"] = "0", ["DMND_TEXTE_INHIBITEUR"] = "NULL",
            ["DMND_ID_ACTRC"] = "NULL", ["DMND_ID_INSCR"] = "NULL", ["DMND_ID_ADRBS"] = "NULL", ["DMND_ID_CLIENT_EXPEDITION"] = "NULL",
            ["DMND_ID_CTGR"] = V3De("CATEGORIE", "DMND_ID_CTGR"), ["DMND_ID_UTLST"] = V3De("UTILISATEUR", "DMND_ID_UTLST"),
            ["DMND_DATE_CREATION"] = "p.Jour", ["DMND_DATE_MODIFICATION"] = "p.Jour",
        });
        // Le libelle relie chaque ligne du plan a son affaire ecrite. Les deux cotes n'ont pas la meme collation : #plan celle de
        // tempdb, v2.AFFAIRE celle de la colonne source que le transfert a reprise (French_CI_AS pour RIG_DEV) -- d'ou un conflit
        // (mesure le 2026-11-23). La comparaison se fait donc en binaire, des deux cotes : une egalite exacte, quelle que soit la base.
        for (var a = 1; a <= affaires; a += TailleLot)
        {
            var z = Math.Min(affaires, a + TailleLot - 1);
            await Executer($"""
                INSERT INTO v2.AFFAIRE ({string.Join(", ", colsAffaire.Select(c => $"[{c}]"))})
                SELECT {selAffaire} FROM #plan AS p JOIN v2.AFFAIRE AS m ON m.AFFRE_ID_AFFRE = p.Affaire WHERE p.I BETWEEN @a AND @z ORDER BY p.I;
                UPDATE p SET Nouvelle = x.AFFRE_ID_AFFRE FROM #plan AS p
                JOIN v2.AFFAIRE AS x ON x.AFFRE_LIBELLE COLLATE Latin1_General_BIN2 = p.Libelle COLLATE Latin1_General_BIN2 WHERE p.I BETWEEN @a AND @z;
                IF EXISTS (SELECT 1 FROM #plan WHERE I BETWEEN @a AND @z AND Nouvelle IS NULL) THROW 51099, N'Une affaire du mois simule n''est pas retrouvee par son libelle.', 1;
                INSERT INTO v2.DEMANDE ({string.Join(", ", colsDemande.Select(c => $"[{c}]"))})
                SELECT {selDemande} FROM #plan AS p JOIN v2.DEMANDE AS m ON m.DMND_ID_DMND = p.Demande CROSS JOIN (VALUES (1, 'V'), (2, 'Q')) AS k (N, Etat)
                WHERE p.I BETWEEN @a AND @z ORDER BY p.I, k.N;
                """, ("@a", a), ("@z", z));
        }
        return modeles;
    }

    public static string EnMarkdown(RapportRetour r)
    {
        var s = new StringBuilder();
        s.AppendLine($"# Répétition du retour à l'ancien RIG (REC-007 CA07) — {r.Debut:yyyy-MM-dd HH:mm} → {r.Fin:HH:mm} ({r.Poste})").AppendLine();
        s.AppendLine($"**{r.Verdict}**").AppendLine();
        s.AppendLine($"- Source : {r.Source} ; copie anonymisée : {r.Copie ?? "—"}{(r.MaxLignes is { } n ? $" (réduite à {n} lignes par table, fermée sur ses clés)" : " (complète)")} ; base V3 : {r.BaseV3 ?? "—"}");
        s.AppendLine($"- Répétition du transfert : {r.Repetition ?? "—"}");
        s.AppendLine($"- Mois simulé : {r.AffairesSimulees} affaires et {r.DemandesSimulees} demandes de la V3 — {r.MoisSimule} ; écrit en {Ms(r.DureeSimulationMs)}");
        s.AppendLine($"- Déclencheurs de date de validation de la V2 : {r.DeclencheursValidationV2 ?? "—"}");
        s.AppendLine($"- Contrôle de nuit pendant la bêta : {(r.PendantBeta is null ? "—" : Texte(r.PendantBeta))}");
        s.AppendLine($"- Refus avant le retour : {(r.Refus.Count == 0 ? "aucun" : string.Join(" ; ", r.Refus))}");
        s.AppendLine($"- Retour : {r.EtatRetour ?? "—"} en {Ms(r.DureeRetourMs)} — {r.AffairesRendues} affaires, {r.DemandesRendues} demandes{(r.DetailRetour is null ? "" : $" ({r.DetailRetour})")}");
        s.AppendLine($"- Contrôle de nuit après le retour : {(r.ApresRetour is null ? "—" : Texte(r.ApresRetour))}");
        s.AppendLine($"- Contre-épreuve : {r.ContreEpreuve ?? "—"}");
        s.AppendLine().AppendLine("## Journal").AppendLine();
        foreach (var l in r.Journal) s.AppendLine($"- {l}");
        return s.ToString();
    }

    private static ControleNuit Resumer(RapportComparaison r) => new(r.Etat, r.CodeSortie, r.Tables.Count, r.LignesComparees, r.Ecarts, r.Orphelins,
        r.SansCorrespondance, r.ATraiter, r.DureeMs, r.Details.Count == 0 ? r.Motif : string.Join(" ; ", r.Details
            .GroupBy(d => $"{d.Genre} {d.TableV2}{(d.Genre == "lien orphelin" ? "." + d.Colonnes : "")}")
            .Select(g => $"{g.Key} x{g.Count()} ({string.Join(", ", g.Take(3).Select(d => d.Genre == "lien orphelin" ? $"{d.IdV3} : {d.Detail}" : $"{d.IdV2}/{d.IdV3} {d.Colonnes}"))})")));

    private static string Texte(ControleNuit c) => $"{c.Etat}, code {c.CodeSortie}, {c.Tables} tables, {c.LignesComparees} lignes comparées en {Ms(c.DureeMs)} : " +
                                                   $"{c.Ecarts} écart(s), {c.Orphelins} lien(s) orphelin(s), {c.SansCorrespondance} sans correspondance" +
                                                   (c.AuDetail is null ? "" : $" — {c.AuDetail}");

    private static string Chaine(string serveur, string nom) => new SqlConnectionStringBuilder(serveur) { InitialCatalog = nom }.ConnectionString;
    private static string? Nom(string? chaine) => chaine is null ? null : new SqlConnectionStringBuilder(chaine).InitialCatalog;
    private static string Ms(long? ms) => ms is { } v ? $"{v / 1000.0:0.0} s" : "—";

    private static async Task<DataTable> LireAsync(string chaine, string sql, CancellationToken ct)
    {
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 1800 };
        var t = new DataTable();
        await using var l = await cmd.ExecuteReaderAsync(ct);
        t.Load(l);
        return t;
    }

    private static async Task ExecuterAsync(string chaine, string sql, CancellationToken ct)
    {
        await using var cn = new SqlConnection(chaine);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 1800 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
