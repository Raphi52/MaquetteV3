// fix-ok: le controle de nuit comparait d'une fiche recopiee des colonnes que la recopie ne garde pas egales : ses colonnes
// propres (etat de transmission), puis son identifiant et ses liens bruts -- mesure le 2026-11-21 par RecopieDeuxSensCheck
// (REC-006 sur les fiches recopiees, rouge : 5 faux ecarts, CLIENT 578727->578728 [CLIEN_ID_CLIEN] x2, DOSSIER_RCS
// [DSSRC_ID_DSRCS] x2, ACTION_RCS 1550628 [ACTRC_ID_DSRCS] vers le dossier V3 214713 = V2 214714). Il compare desormais les
// colonnes de la recopie elle-meme (FichesRecopiees.Construire sur les etats figes) et lit chaque lien de la V3 en
// identifiant V2 par rec.Correspondance, comme EcrituresRecopie.TraduireAsync l'ecrit.
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <param name="ConnexionV2">La base V2 du greffe : lue dans un instantane, et relue (SELECT seul) pour confirmer un ecart.</param>
/// <param name="ConnexionV3">Sa base V3, sur la meme instance (Q4) : le rapport y est ecrit (rec.Comparaison).</param>
public sealed record OptionsComparaison(string ConnexionV2, string ConnexionV3)
{
    /// <summary>Les tables V2 comparees ; null : les fiches partagees (<see cref="ComparaisonBases.TablesPartagees"/>).</summary>
    public IReadOnlyList<string>? Tables { get; init; }
    /// <summary>Ecarts detailles (colonnes, relecture) par table et par genre ; au-dela, ils sont comptes.</summary>
    public int DetailMax { get; init; } = 1000;
    /// <summary>Les rapports plus vieux sont purges au debut de chaque controle (CA04 : 90 jours, duree proposee).</summary>
    public int ConservationJours { get; init; } = 90;
    public Action<string>? Suivre { get; init; }
    /// <summary>Le journal technique (SOC-017) : un controle arrete y est ecrit.</summary>
    public ILogger? Journal { get; init; }
}

public sealed record TableComparee(string TableV2, string? TableV3, long LignesV2, long LignesV3, long Comparees, long Ecarts,
                                   long SansCorrespondanceV2, long SansCorrespondanceV3, long SansLigneV2, long SansLigneV3,
                                   long Rejetees, long Resorbes, int Colonnes, long DureeMs, string? Detail);

/// <param name="Genre">ecart ; sans correspondance V2 ; sans correspondance V3 ; correspondance sans ligne V2 ;
/// correspondance sans ligne V3 ; lien orphelin.</param>
/// <param name="Colonnes">Un ecart : les colonnes qui different ; un lien orphelin : la colonne du lien.</param>
public sealed record EcartComparaison(string Genre, string TableV2, string? IdV2, string? TableV3, string? IdV3, string? Colonnes, string? Detail);

public sealed record RapportComparaison(int Id, string Etat, string? Motif, string BaseV2, DateTime? EtatFigeLe, long DureeMs,
                                        IReadOnlyList<TableComparee> Tables, IReadOnlyList<EcartComparaison> Details, long Orphelins, int Purges)
{
    public long LignesComparees => Tables.Sum(t => t.Comparees);
    public long Ecarts => Tables.Sum(t => t.Ecarts);
    public long SansCorrespondance => Tables.Sum(t => t.SansCorrespondanceV2 + t.SansCorrespondanceV3 + t.SansLigneV2 + t.SansLigneV3);
    public long Resorbes => Tables.Sum(t => t.Resorbes);
    /// <summary>Ce qui alerte l'exploitation (CA04) : ecarts, liens orphelins, lignes sans correspondance.</summary>
    public long ATraiter => Ecarts + Orphelins + SansCorrespondance;
    /// <summary>0 : rien a traiter ; 5 : au moins un ecart ; 1 : le controle s'est arrete.</summary>
    public int CodeSortie => Etat != "fini" ? 1 : ATraiter > 0 ? 5 : 0;
}

/// <summary>
/// LE CONTROLE DE NUIT D'UN GREFFE : L'ANCIENNE ET LA NOUVELLE BASE DISENT-ELLES LA MEME CHOSE ? (REC-006)
///
///  1. Purge des rapports de plus de <see cref="OptionsComparaison.ConservationJours"/> jours (CA04).
///  2. Un ETAT FIGE de chaque base (instantane de base, comme le transfert) : la lecture ne pose aucun verrou sur les
///     tables vivantes, les agents ecrivent pendant le controle (CA05).
///  3. Pour chaque table partagee, par la table de correspondance (CA01) : le nombre de lignes de chaque cote, et une
///     EMPREINTE de chaque ligne (SHA-256 du texte de ses colonnes, calcule par SQL Server des deux cotes de la meme
///     facon ; une fiche recopiee, ReglesRecopie.cs, par les colonnes que la recopie recopie : ni son identifiant, ni ses
///     colonnes propres a chaque logiciel, et ses liens lus en identifiant V2), lues dans l'ordre
///     de l'identifiant V2 et fusionnees : ligne comparee, ecart, ligne sans correspondance (rejetee par le transfert :
///     comptee a part), correspondance sans sa ligne. Une table V3 qui n'est pas la table V2 telle quelle (une reprise :
///     UTILISATEUR -> soc.Compte) n'a que ses correspondances controlees : la conversion colonne par colonne est a
///     decrire par sa story (REC-006 T03).
///  4. Un ecart est DETAILLE (CA02) : les deux lignes relues colonne par colonne, les colonnes qui different. Puis il
///     est RELU dans les deux bases vivantes : disparu, c'etait une recopie en cours entre les deux etats figes
///     (« resorbe », compte, non alerte) ; present, il est au rapport.
///  4 bis. Les DROITS des comptes (fiche UTILISATEUR en service) : pas ligne a ligne, la REPONSE de la V2 contre celle de la V3 pour chaque
///     compte actif recopie et chaque fonction (DroitsRecopies.ComparerAsync) ; un ecart va au rapport comme les autres.
///  5. Les LIENS ORPHELINS (CA03) : une demande ou une affaire de la V3 qui vise une fiche partagee sans correspondance
///     (hors liens casses herites, deja notes par le transfert dans rec.LienCasse). Le lien est lu en identifiant de la V3
///     (sauf les colonnes RegleRecopie.NumerosV2) ; une ligne de la V3 qui vise une ligne que la V3 tient seule par regle
///     (affaire ou demande de la V3, type de la V3 seule) n'est pas orpheline.
///  6. Le rapport, ecrit dans la base V3 (rec.Comparaison, rec.ComparaisonTable, rec.ComparaisonEcart) : lignes
///     comparees, ecarts, lignes sans correspondance, orphelins. Ni valeur ni donnee personnelle : des noms de colonnes.
/// La base V2 n'est jamais ecrite (instantane et SELECT seuls, par LectureV2). Planifie chaque nuit par
/// ComparaisonNocturne ; alerte par AlertesRecopie (supervision d'Amitel). Mode d'emploi : docs/reconstruction/recopie.md.
/// </summary>
public static class ComparaisonBases
{
    /// <summary>Les fiches partagees de la V2 (REC-006 tables_v2, carnet\recopie.json).</summary>
    public static readonly IReadOnlyList<string> TablesPartagees =
        ["ACTION_RCS", "ADRESSE_BASE", "ARRIVEE", "CATEGORIE", "CLIENT", "DOSSIER_RCS", "INSCRIPTION", "UTILISATEUR", "ENTREPRISE", "AFFAIRE", "DEMANDE"];

    /// <summary>Les tables dont les liens vers les fiches partagees sont controles (CA03 : « 12 cles etrangeres partent de
    /// DEMANDE et AFFAIRE », sys.foreign_keys de RIG_DEV lu le 2026-11-20).</summary>
    public static readonly IReadOnlyList<string> TablesDesLiens = ["DEMANDE", "AFFAIRE"];

    public const string Usage = "rigv3cmp";

    public static async Task<RapportComparaison> ComparerAsync(OptionsComparaison o, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        var baseV2 = new SqlConnectionStringBuilder(o.ConnexionV2).InitialCatalog;
        var serveur = new SqlConnectionStringBuilder(o.ConnexionV2).DataSource;
        var demandees = o.Tables ?? TablesPartagees;

        int id, purges;
        await using (var v3 = new SqlConnection(o.ConnexionV3))
        {
            await v3.OpenAsync(ct);
            if (await Scalaire(v3, "SELECT OBJECT_ID(N'rec.Comparaison');", ct) is null)
                throw new InvalidOperationException("La base V3 n'a pas la migration REC-006_comparaison : lancer Base\\RigV3.Migrations sur elle d'abord.");
            await using (var cmd = new SqlCommand("DELETE FROM rec.Comparaison WHERE Debut < DATEADD(day, -@j, SYSDATETIME());", v3))
            {
                cmd.Parameters.Add(new SqlParameter("@j", SqlDbType.Int) { Value = o.ConservationJours });
                purges = await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var cmd = new SqlCommand("INSERT INTO rec.Comparaison (BaseV2, ServeurV2) OUTPUT INSERTED.Id VALUES (@b, @s);", v3))
            {
                cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.NVarChar, 128) { Value = baseV2 });
                cmd.Parameters.Add(new SqlParameter("@s", SqlDbType.NVarChar, 128) { Value = serveur });
                id = (int)(await cmd.ExecuteScalarAsync(ct))!;
            }
        }
        o.Suivre?.Invoke($"Controle {id} de {baseV2} : {purges} rapport(s) de plus de {o.ConservationJours} jours purge(s)");

        var tables = new List<TableComparee>();
        var details = new List<EcartComparaison>();
        Instantane? figeV3 = null, figeV2 = null;
        try
        {
            // 2. Les deux etats figes : la V3 d'abord (elle fait foi, Q7), puis la V2.
            figeV3 = await Instantane.CreerAsync(o.ConnexionV3, ct, Usage);
            figeV2 = await Instantane.CreerAsync(o.ConnexionV2, ct, Usage);
            o.Suivre?.Invoke($"Etats figes : {figeV3.Nom} et {figeV2.Nom} ({figeV2.Le:yyyy-MM-dd HH:mm:ss.fff})");
            await using var lv2 = new SqlConnection(figeV2.Connexion);
            await using var lv3 = new SqlConnection(figeV3.Connexion);
            await lv2.OpenAsync(ct);
            await lv3.OpenAsync(ct);
            var catV2 = await CatalogueV2.LireAsync(lv2, ct);
            var catV3 = await CatalogueV2.LireAsync(lv3, ct);
            // Les fiches recopiees telles que la recopie les voit (memes regles, memes catalogues) : ce qu'elle garde egal
            // dans les deux bases, et seulement cela, est compare.
            // fix-ok: comparaison faite avec toutes les regles du serveur au lieu de celles de la mise en service (512 faux ecarts CLIEN_ID_UTLST mesures).
            // Les regles sont celles de la MISE EN SERVICE de la base (rec.FicheEnService), comme pour la recopie : comparer
            // avec toutes les regles du serveur traduisait des liens que cette base ne recopie pas (512 faux ecarts
            // CLIENT [CLIEN_ID_UTLST] mesures par RecopieDeuxSensCheck, ou UTILISATEUR n'est pas mise en service).
            // Avant toute mise en service, une fiche transferee dans sa copie v2.X (REC-012 a REC-019) est comparee par la
            // forme telle quelle de sa regle : la table V3 qui compte est celle de ses correspondances.
            var (regles, _, misEnService) = await FichesRecopiees.ReglesEnServiceAsync(lv3, baseV2, ct);
            if (!misEnService)
            {
                var tablesV3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                await using (var l = await LireAsync(lv3, "SELECT TableV2, MAX(TableV3) FROM rec.Correspondance WHERE BaseV2 = @b GROUP BY TableV2", ct, P("@b", baseV2)))
                    while (await l.ReadAsync(ct)) tablesV3[l.GetString(0)] = l.GetString(1);
                regles = [.. regles.Select(r => tablesV3.TryGetValue(r.TableV2, out var ecrite)
                                                && string.Equals(ecrite, "v2." + r.TableV2, StringComparison.OrdinalIgnoreCase) ? r.EnTelleQuelle() : r)];
            }
            var recopiees = FichesRecopiees.Construire(baseV2, catV2, catV3, regles);

            // 3, 4. Table par table.
            foreach (var nom in demandees)
            {
                if (!catV2.Tables.TryGetValue(nom, out var t2)) { o.Suivre?.Invoke($"{nom} : absente de la base V2, non comparee"); continue; }
                var t = await ComparerTableAsync(o, baseV2, t2, catV3, lv2, lv3, details, recopiees.ParTableV2.GetValueOrDefault(nom), ct);
                tables.Add(t);
                o.Suivre?.Invoke($"{nom} -> {t.TableV3 ?? "aucune table V3"} : {t.LignesV2} lignes V2, {t.LignesV3} V3, {t.Comparees} comparees, {t.Ecarts} ecarts, " +
                                 $"{t.SansCorrespondanceV2} V2 et {t.SansCorrespondanceV3} V3 sans correspondance, {t.Rejetees} rejetees, {t.Resorbes} resorbes, {t.DureeMs} ms");
            }

            // 4 bis. Les droits des comptes (REC-006 CA01 pour UTILISATEUR, SOC-002 T06) : la partie jointe de la fiche compte, comparee par la
            // REPONSE (DroitsRecopies.ComparerAsync) des que la recopie de la fiche est en service -- avant, la V3 ne les ecrit pas dans la V2.
            if (misEnService && demandees.Contains("UTILISATEUR", StringComparer.OrdinalIgnoreCase)
                && regles.Any(r => string.Equals(r.TableV2, "UTILISATEUR", StringComparison.OrdinalIgnoreCase) && r.Jointe is DroitsRecopies)
                && catV2.Tables.ContainsKey("HABILIT_UTILISATEUR_ROLE_USER") && catV2.Tables.ContainsKey("PROCESSUS_ET_FONCTIONALITE")
                && await Scalaire(lv3, "SELECT OBJECT_ID(N'soc.Droit', N'V');", ct) is not null)
            {
                var t = await DroitsRecopies.ComparerAsync(o, baseV2, figeV2.Connexion, lv3, details, ct);
                tables.Add(t);
                o.Suivre?.Invoke($"{t.TableV2} -> {t.TableV3} : {t.Detail}, {t.Comparees} reponses comparees, {t.Ecarts} ecarts, {t.Resorbes} resorbes, {t.DureeMs} ms");
            }

            // 5. Les liens orphelins.
            long orphelins = 0;
            foreach (var nom in TablesDesLiens.Where(n => catV2.Tables.ContainsKey(n) && catV3.Tables.ContainsKey("v2." + n)))
                orphelins += await OrphelinsAsync(o, baseV2, catV2.Tables[nom], catV2, demandees, recopiees, lv3, details, ct);
            o.Suivre?.Invoke($"Liens orphelins : {orphelins}");

            var rapport = new RapportComparaison(id, "fini", null, baseV2, figeV2.Le, chrono.ElapsedMilliseconds, tables, details, orphelins, purges);
            await EcrireAsync(o.ConnexionV3, rapport, figeV2.Nom, figeV3.Nom, ct);
            return rapport;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var motif = "Controle arrete : " + e.Message;
            o.Journal?.LogError(e, "Controle {Id} de {BaseV2} arrete : {Cause}", id, baseV2, e.Message);
            await using var v3 = new SqlConnection(o.ConnexionV3);
            await v3.OpenAsync(CancellationToken.None);
            await using var cmd = new SqlCommand("UPDATE rec.Comparaison SET Etat = 'arrete', Motif = @m, Fin = SYSDATETIME(), DureeMs = @ms WHERE Id = @id;", v3);
            cmd.Parameters.Add(new SqlParameter("@m", SqlDbType.NVarChar, -1) { Value = motif });
            cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = chrono.ElapsedMilliseconds });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
            return new RapportComparaison(id, "arrete", motif, baseV2, figeV2?.Le, chrono.ElapsedMilliseconds, tables, details, 0, purges);
        }
        finally
        {
            if (figeV2 is not null) await figeV2.DisposeAsync();
            if (figeV3 is not null) await figeV3.DisposeAsync();
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Une table
    // -----------------------------------------------------------------------------------------------------------------

    private static async Task<TableComparee> ComparerTableAsync(OptionsComparaison o, string baseV2, TableV2 t2, CatalogueV2 catV3,
                                                                SqlConnection lv2, SqlConnection lv3, List<EcartComparaison> details,
                                                                TableRecopiee? fiche, CancellationToken ct)
    {
        var chrono = Stopwatch.StartNew();
        // La table V3 : celle de ses correspondances (v2.X pour une table telle quelle, soc.Compte pour UTILISATEUR...).
        var nomV3 = (string?)await Scalaire(lv3, "SELECT TOP (1) TableV3 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t GROUP BY TableV3 ORDER BY COUNT_BIG(*) DESC;", ct,
                                            P("@b", baseV2), P("@t", t2.Nom));
        nomV3 ??= catV3.Tables.ContainsKey("v2." + t2.Nom) ? "v2." + t2.Nom : null;
        var lignesV2 = Convert.ToInt64(await ValeurAsync(lv2, $"SELECT COUNT_BIG(*) FROM {t2.NomSql}", ct));
        if (nomV3 is null || !catV3.Tables.TryGetValue(nomV3, out var t3))
        {
            var sans = Math.Min(lignesV2, o.DetailMax);
            details.Add(new EcartComparaison("sans correspondance V2", t2.Nom, null, null, null, null,
                $"la table n'est pas dans la base V3 : ses {lignesV2} lignes n'ont aucune correspondance"));
            return new TableComparee(t2.Nom, null, lignesV2, 0, 0, 0, lignesV2, 0, 0, 0, 0, 0, 0, chrono.ElapsedMilliseconds,
                                     "table absente de la base V3 (pas transferee)");
        }
        var cle2 = t2.ClePrimaire.Select(t2.Colonne).ToList();
        var cle3 = t3.ClePrimaire.Select(t3.Colonne).ToList();
        if (cle2.Count == 0 || cle3.Count == 0)
            return new TableComparee(t2.Nom, nomV3, lignesV2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, chrono.ElapsedMilliseconds,
                                     "sans cle primaire : non comparee (le transfert la refuse aussi)");
        var id2 = Sql.Identifiant("c", cle2);
        var id3 = Sql.Identifiant("t", cle3);
        // Telle quelle : les memes colonnes des deux cotes. Une fiche recopiee (sa regle, ReglesRecopie.cs) : les colonnes que
        // la recopie recopie (FichesRecopiees), pas les autres -- son identifiant est propre a chaque base et apparie par la
        // correspondance (une fiche creee d'un cote recoit le numero que l'autre base lui donne, REC-002 CA02), ses colonnes
        // propres a chaque logiciel ne sont jamais recopiees (l'etat de transmission, REC-002 CA04) ; un lien vers une fiche
        // recopiee est compare en identifiant V2, la valeur de la V3 lue par la correspondance (Texte). Sinon : les
        // correspondances seulement.
        // Une fiche qui a sa table dans la V3 (REC-012 a REC-019) est comparee de la meme facon, chaque colonne V2 avec sa
        // colonne V3 (sa correspondance) ; une colonne traduite (un agent par son compte de session) ne l'est pas.
        var telleQuelle = string.Equals(nomV3, "v2." + t2.Nom, StringComparison.OrdinalIgnoreCase);
        var recopiee = fiche is not null && string.Equals(fiche.NomV3, nomV3, StringComparison.OrdinalIgnoreCase) ? fiche : null;
        var memes = recopiee is not null ? recopiee.Colonnes.Where(c => recopiee.TraductionDe(c.Nom) is null).ToList()
            : !telleQuelle ? []
            : t2.Colonnes.Where(c => t3.Colonnes.Any(x => string.Equals(x.Nom, c.Nom, StringComparison.OrdinalIgnoreCase))).ToList();
        var nonComparees = recopiee is null ? []
            : t2.Colonnes.Where(c => !memes.Any(m => string.Equals(m.Nom, c.Nom, StringComparison.OrdinalIgnoreCase)))
                         .Select(c => $"{c.Nom} ({(string.Equals(c.Nom, recopiee.Cle.Nom, StringComparison.OrdinalIgnoreCase) ? "identifiant propre a chaque base, apparie par rec.Correspondance"
                                                   : recopiee.Regle.ColonnesPropres.Contains(c.Nom, StringComparer.OrdinalIgnoreCase) ? "propre a chaque logiciel, jamais recopiee"
                                                   : recopiee.TraductionDe(c.Nom) is { } tr ? $"traduite par {tr.Par}"
                                                   : "hors de la recopie")})").ToList();
        var liens = memes.Where(c => recopiee is not null && recopiee.Liens.ContainsKey(c.Nom)).Select(c => $"{c.Nom} -> {recopiee!.Liens[c.Nom].NomV2}").ToList();
        var cols3 = memes.Select(c => recopiee is not null ? recopiee.ColonneV3(c) : t3.Colonne(c.Nom)).ToList();
        // fix-ok: les liens d'une fiche sont ranges sous leur nom V2 ; cherches sous le nom V3 (IdFamille), ils n'etaient pas
        // lus par la correspondance -- 873 faux ecarts CATEGORIE mesures par RecopieRepriseCheck (numeros V3 propres, REC-019).
        var textes3 = memes.Select((c, i) => Texte("t", cols3[i], recopiee is not null && recopiee.Liens.TryGetValue(c.Nom, out var visee) ? visee : null)).ToList();
        var lignesV3 = Convert.ToInt64(await ValeurAsync(lv3, $"SELECT COUNT_BIG(*) FROM {t3.NomSql}", ct));

        // Les lignes rejetees par le transfert : sans correspondance, mais expliquees (rec.Rejet).
        var rejets = new HashSet<string>(StringComparer.Ordinal);
        await using (var l = await LireAsync(lv3, "SELECT IdV2 FROM rec.Rejet WHERE BaseV2 = @b AND TableV2 = @t", ct, P("@b", baseV2), P("@t", t2.Nom)))
            while (await l.ReadAsync(ct)) rejets.Add(l.GetString(0));

        long comparees = 0, sansV2 = 0, sansLigneV2 = 0, sansLigneV3 = 0, rejetees = 0;
        var candidats = new List<(string IdV2, string IdV3)>();
        long ecartsAuDela = 0;
        var genres = new Dictionary<string, int>(StringComparer.Ordinal);
        void Noter(string genre, string? idV2, string? idV3)
        {
            genres[genre] = genres.GetValueOrDefault(genre) + 1;
            if (genres[genre] <= o.DetailMax) details.Add(new EcartComparaison(genre, t2.Nom, idV2, nomV3, idV3, null, null));
        }
        var empreinte2 = memes.Count == 0 ? "CAST(NULL AS varbinary(32))" : Empreinte("c", memes);
        var empreinte3 = memes.Count == 0 ? "CAST(NULL AS varbinary(32))" : Empreinte(textes3);
        await using (var s2 = await LireAsync(lv2, $"SELECT {id2}, {empreinte2} FROM {t2.NomSql} AS c ORDER BY {id2} COLLATE Latin1_General_BIN2", ct))
        await using (var sc = await LireAsync(lv3,
            $"SELECT c.IdV2, c.IdV3, CASE WHEN t.{Sql.Q(cle3[0].Nom)} IS NULL THEN 0 ELSE 1 END, {empreinte3} " +
            $"FROM rec.Correspondance AS c LEFT JOIN {t3.NomSql} AS t ON {id3} = c.IdV3 " +
            "WHERE c.BaseV2 = @b AND c.TableV2 = @t AND c.TableV3 = @t3 ORDER BY c.IdV2 COLLATE Latin1_General_BIN2", ct,
            P("@b", baseV2), P("@t", t2.Nom), P("@t3", nomV3, 260)))
        {
            // Les deux lecteurs avancent ensemble : fusion sur l'identifiant V2, dans l'ordre binaire des deux cotes.
            var a2 = await s2.ReadAsync(ct);
            var ac = await sc.ReadAsync(ct);
            while (a2 || ac)
            {
                var c = !a2 ? 1 : !ac ? -1 : string.CompareOrdinal(s2.GetString(0), sc.GetString(0));
                if (c < 0)
                {
                    var idV2 = s2.GetString(0);
                    if (rejets.Contains(idV2)) rejetees++;
                    else { sansV2++; Noter("sans correspondance V2", idV2, null); }
                    a2 = await s2.ReadAsync(ct);
                }
                else if (c > 0)
                {
                    sansLigneV2++;
                    Noter("correspondance sans ligne V2", sc.GetString(0), sc.GetString(1));
                    ac = await sc.ReadAsync(ct);
                }
                else
                {
                    if (sc.GetInt32(2) == 0) { sansLigneV3++; Noter("correspondance sans ligne V3", sc.GetString(0), sc.GetString(1)); }
                    else
                    {
                        comparees++;
                        if (memes.Count > 0 && !((byte[])s2.GetValue(1)).AsSpan().SequenceEqual((byte[])sc.GetValue(3)))
                        {
                            if (candidats.Count < o.DetailMax) candidats.Add((s2.GetString(0), sc.GetString(1)));
                            else ecartsAuDela++;
                        }
                    }
                    a2 = await s2.ReadAsync(ct);
                    ac = await sc.ReadAsync(ct);
                }
            }
        }

        // Les lignes V3 sans correspondance (creees dans la V3, jamais recopiees). Sauf pour une table qui ne fait que remonter
        // (les affaires et leurs demandes, REC-010) : une ligne creee dans la V3 y est une affaire de la V3, traitee dans la V3
        // seule (Q5, Q14) ; elle n'a de correspondance qu'au retour d'un greffe beta (REC-007) -- ce n'est pas un ecart. De meme
        // pour une fiche que la V3 ne cree jamais dans la V2 (SansCreationV2 : un type de demande de la V3 seule, REC-019 CA03 --
        // « la V3 le sait », l'applicateur ne la cree pas) ; le retour la liste (REC-007 CA06). Une ligne de la V2 qui perd sa
        // correspondance reste vue, du cote de la V2 (« sans correspondance V2 »).
        // fix-ok: un type de la V3 seule etait compte « sans correspondance V3 » a chaque nuit -- mesure le 2026-11-22 par
        // RecopieRetourCheck (REC-007 CA07 sur toutes les fiches partagees, rouge : CATEGORIE /893, le type cree par CA06).
        long sansV3 = 0;
        if (recopiee?.Regle is not ({ RemonteeSeule: true } or { SansCreationV2: true }))
        {
            await using var l = await LireAsync(lv3,
                $"SELECT {id3} FROM {t3.NomSql} AS t WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.TableV3 = @t3 AND c.IdV3 = {id3} AND c.BaseV2 = @b)", ct,
                P("@b", baseV2), P("@t3", nomV3, 260));
            while (await l.ReadAsync(ct)) { sansV3++; Noter("sans correspondance V3", null, l.GetString(0)); }
        }

        // 4. Les ecarts : detail des colonnes dans l'etat fige, puis relecture dans les bases vivantes.
        long resorbes = 0;
        var ecarts = new List<(string IdV2, string IdV3, List<string> Colonnes)>();
        if (candidats.Count > 0)
        {
            var figes = await ColonnesQuiDifferentAsync(lv2, lv3, t2, t3, memes, textes3, baseV2, candidats, ct);
            await using var vivV2 = new SqlConnection(new SqlConnectionStringBuilder(o.ConnexionV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString);
            await using var vivV3 = new SqlConnection(o.ConnexionV3);
            await vivV2.OpenAsync(ct);
            await vivV3.OpenAsync(ct);
            var vivantes = await ColonnesQuiDifferentAsync(vivV2, vivV3, t2, t3, memes, textes3, baseV2, candidats, ct);
            foreach (var (idV2, idV3) in candidats)
            {
                if (!vivantes.TryGetValue(idV2, out var vivant) || vivant.Count > 0)
                    ecarts.Add((idV2, idV3, figes.GetValueOrDefault(idV2) ?? vivant ?? []));
                else resorbes++;
            }
            foreach (var (idV2, idV3, colonnes) in ecarts)
                details.Add(new EcartComparaison("ecart", t2.Nom, idV2, nomV3, idV3, string.Join(", ", colonnes), null));
        }
        var nbEcarts = ecarts.Count + ecartsAuDela;
        var detail = memes.Count > 0
            ? $"{memes.Count} colonnes comparees (empreinte SHA-256 de chaque ligne, des deux cotes)"
              + (nonComparees.Count > 0 ? $" ; non comparees, la recopie ne les recopie pas : {string.Join(", ", nonComparees)}" : "")
              + (liens.Count > 0 ? $" ; liens compares en identifiant V2 par rec.Correspondance : {string.Join(", ", liens)}" : "")
              + (ecartsAuDela > 0 ? $" ; {ecartsAuDela} ecarts au-dela des {o.DetailMax} detailles" : "")
            : $"colonnes non comparees : {nomV3} n'est pas {t2.Nom} telle quelle et n'a pas de regle de recopie colonne par colonne (REC-006 T03) — correspondances controlees seulement";
        return new TableComparee(t2.Nom, nomV3, lignesV2, lignesV3, comparees, nbEcarts, sansV2, sansV3, sansLigneV2, sansLigneV3, rejetees, resorbes,
                                 memes.Count, chrono.ElapsedMilliseconds, detail);
    }

    /// <summary>Les colonnes qui different entre la ligne V2 et sa ligne V3, pour chaque identifiant V2 (absent du
    /// resultat : une des deux lignes n'existe pas).</summary>
    private static async Task<Dictionary<string, List<string>>> ColonnesQuiDifferentAsync(SqlConnection v2, SqlConnection v3, TableV2 t2, TableV2 t3,
        List<ColonneV2> cols2, IReadOnlyList<string> textes3, string baseV2, List<(string IdV2, string IdV3)> ids, CancellationToken ct)
    {
        var lignes2 = new Dictionary<string, string?[]>(StringComparer.Ordinal);
        var lignes3 = new Dictionary<string, string?[]>(StringComparer.Ordinal);
        var id2 = Sql.Identifiant("c", t2.ClePrimaire.Select(t2.Colonne));
        var id3 = Sql.Identifiant("t", t3.ClePrimaire.Select(t3.Colonne));
        foreach (var paquet in ids.Chunk(500))
        {
            var noms = paquet.Select((_, i) => $"@i{i}").ToList();
            var parametres2 = paquet.Select((x, i) => P($"@i{i}", x.IdV2)).ToArray();
            await using (var l = await LireAsync(v2, $"SELECT {id2}, {string.Join(", ", cols2.Select(c => Texte("c", c)))} FROM {t2.NomSql} AS c " +
                                                           $"WHERE {id2} IN ({string.Join(", ", noms)})", ct, parametres2))
                while (await l.ReadAsync(ct)) lignes2[l.GetString(0)] = [.. Enumerable.Range(1, cols2.Count).Select(i => l.IsDBNull(i) ? null : l.GetString(i))];
            var parametres3 = paquet.Select((x, i) => P($"@i{i}", x.IdV2)).Append(P("@b", baseV2)).Append(P("@t", t2.Nom)).ToArray();
            await using (var l = await LireAsync(v3, $"SELECT c.IdV2, {string.Join(", ", textes3)} FROM rec.Correspondance AS c " +
                                                           $"JOIN {t3.NomSql} AS t ON {id3} = c.IdV3 WHERE c.BaseV2 = @b AND c.TableV2 = @t AND c.IdV2 IN ({string.Join(", ", noms)})", ct, parametres3))
                while (await l.ReadAsync(ct)) lignes3[l.GetString(0)] = [.. Enumerable.Range(1, textes3.Count).Select(i => l.IsDBNull(i) ? null : l.GetString(i))];
        }
        var resultat = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (idV2, _) in ids)
            if (lignes2.TryGetValue(idV2, out var a) && lignes3.TryGetValue(idV2, out var b))
                resultat[idV2] = [.. cols2.Where((_, i) => !string.Equals(a[i], b[i], StringComparison.Ordinal)).Select(c => c.Nom)];
        return resultat;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Les liens orphelins (CA03)
    // -----------------------------------------------------------------------------------------------------------------

    // fix-ok: le lien d'une demande ou d'une affaire de la V3 etait cherche comme un identifiant V2 (c.IdV2 = valeur), alors que
    // v2.<SOURCE> de la V3 porte l'identifiant de la V3 de la fiche visee (egal a celui de la V2 pour une ligne transferee
    // seulement) -- mesure le 2026-11-22 par RecopieRetourCheck (REC-007 CA07, rouge) : apres le retour, 253 liens orphelins
    // (DMND_ID_AFFRE x248 vers des affaires rendues, V3 5502427 = V2 autre ; DMND_ID_ARRV x2, DMND_ID_DMND_MERE x2,
    // DMND_ID_DMND_INHIBITRICE x1) ; pendant la beta, 497 (dont DMND_ID_CTGR x247 : rec.TypeDemande 671 lu comme CATEGORIE 671).
    private static async Task<long> OrphelinsAsync(OptionsComparaison o, string baseV2, TableV2 source, CatalogueV2 catV2, IReadOnlyList<string> partagees,
                                                   FichesRecopiees recopiees, SqlConnection lv3, List<EcartComparaison> details, CancellationToken ct)
    {
        long total = 0;
        var idS = Sql.Identifiant("d", source.ClePrimaire.Select(source.Colonne));
        var regleSource = recopiees.ParTableV2.GetValueOrDefault(source.Nom)?.Regle;
        // La ligne source est dans la V2 : elle a sa correspondance (ancienne ligne, ou ligne de la V3 rendue au retour, REC-007).
        var dansLaV2 = $"EXISTS (SELECT 1 FROM rec.Correspondance AS s WHERE s.TableV3 = @s3 AND s.IdV3 = {idS} AND s.BaseV2 = @b AND s.TableV2 = @s)";
        foreach (var k in source.Cles.Where(k => partagees.Contains(k.TableVisee, StringComparer.OrdinalIgnoreCase) && catV2.Tables.ContainsKey(k.TableVisee)))
        {
            var visee = catV2.Tables[k.TableVisee];
            var regleVisee = recopiees.ParTableV2.GetValueOrDefault(visee.Nom)?.Regle;
            // La table V3 de la fiche visee : celle de ses correspondances (v2.X telle quelle, rec.Client, rec.TypeDemande...).
            var nomV3Visee = (string?)await Scalaire(lv3, "SELECT TOP (1) TableV3 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t GROUP BY TableV3 ORDER BY COUNT_BIG(*) DESC;", ct,
                                                     P("@b", baseV2), P("@t", visee.Nom)) ?? "v2." + visee.Nom;
            // La valeur du lien dans v2.<SOURCE> de la V3 est l'identifiant V3 de la fiche visee : la remontee le traduit
            // (EcrituresRecopie.TraduireAsync), une ligne creee dans la V3 le porte, et une ligne transferee porte celui de la
            // V2, egal a celui de la V3 (REC-001). Sauf une colonne qui garde le numero de la V2 (RegleRecopie.NumerosV2 : le
            // type, l'agent) : numero de la V2 dans une ligne qui est dans la V2, identifiant de la V3 dans une ligne de la V3.
            // Un lien casse herite (rec.LienCasse) est deja note : il n'est pas orphelin ici.
            var valeur = Sql.Identifiant("d", k.Colonnes.Select(source.Colonne));
            var renseigne = string.Join(" AND ", k.Colonnes.Select(c => $"d.{Sql.Q(c)} IS NOT NULL"));
            var parIdV3 = $"EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.TableV3 = @v3 AND c.IdV3 = {valeur} AND c.BaseV2 = @b AND c.TableV2 = @v)";
            var numeroV2 = k.Colonnes.Count == 1 && regleSource?.NumerosV2.Keys.Contains(k.Colonnes[0], StringComparer.OrdinalIgnoreCase) == true;
            var trouvee = numeroV2
                ? $"(({dansLaV2} AND EXISTS (SELECT 1 FROM rec.Correspondance AS c WHERE c.BaseV2 = @b AND c.TableV2 = @v AND c.IdV2 = {valeur})) OR (NOT {dansLaV2} AND {parIdV3}))"
                : parIdV3;
            // Une fiche visee dont la V3 tient des lignes seule, par regle -- les affaires et demandes de la V3 (RemonteeSeule,
            // REC-010 T02), un type cree dans la V3 seulement (SansCreationV2, REC-019 CA03) : une ligne de la V3 qui en vise une
            // relie deux lignes de la V3, ce n'est pas un lien orphelin. Une ligne qui est dans la V2 doit, elle, viser une fiche de la V2.
            var lignesV3Seules = regleVisee is { RemonteeSeule: true } or { SansCreationV2: true };
            var sql = $"SELECT {idS}, {valeur} FROM {Sql.Q("v2")}.{Sql.Q(source.Nom)} AS d WHERE {renseigne} AND NOT {trouvee} " +
                      (lignesV3Seules ? $"AND {dansLaV2} " : "") +
                      "AND NOT EXISTS (SELECT 1 FROM rec.LienCasse AS lc WHERE lc.BaseV2 = @b AND lc.TableV2 = @s AND lc.IdV2 = " + idS + " AND lc.Cle = @k)";
            long n = 0;
            await using var l = await LireAsync(lv3, sql, ct, P("@b", baseV2), P("@v", visee.Nom), P("@v3", nomV3Visee, 260), P("@s", source.Nom),
                                                P("@s3", "v2." + source.Nom, 260), P("@k", k.Nom));
            while (await l.ReadAsync(ct))
            {
                n++;
                if (n <= o.DetailMax)
                    details.Add(new EcartComparaison("lien orphelin", source.Nom, null, "v2." + source.Nom, l.GetString(0), string.Join(", ", k.Colonnes),
                        $"{k.Nom} vise {visee.Nom} {l.GetString(1)}, fiche partagee sans correspondance dans la V2"));
            }
            total += n;
        }
        return total;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // L'empreinte d'une ligne, et le texte d'une colonne
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Le texte d'une valeur, le meme pour la meme valeur des deux cotes : date en ISO 8601 (126), binaire et horodatage en
    /// hexadecimal (un timestamp de la V2 est un binary(8) dans la V3), flottant a 17 chiffres (3), monnaie a 4 decimales
    /// (2), le reste par la conversion de SQL Server. NULL se distingue de toute valeur (« ~ » contre « = valeur »).
    /// </summary>
    internal static string Texte(string alias, ColonneV2 c) => Texte(alias, c, null);

    /// <param name="visee">La fiche recopiee que vise cette colonne d'une ligne de la V3 : sa valeur, un identifiant V3, est
    /// lue en identifiant V2 dans rec.Correspondance (parametre @b de la requete ; une fiche V3 qui en a plusieurs, des types
    /// de demande fusionnes, suit celle qui n'est pas une fusion, comme EcrituresRecopie.IdV2Async), et telle quelle sans correspondance --
    /// exactement ce que la recopie ecrit dans l'autre base (EcrituresRecopie.TraduireAsync). Identifiants en texte des deux
    /// cotes par la meme conversion (Sql.EnTexte, celle du transfert).</param>
    private static string Texte(string alias, ColonneV2 c, TableRecopiee? visee)
    {
        var x = $"{alias}.{Sql.Q(c.Nom)}";
        var conv = c.TypeBase switch
        {
            "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" or "time" => $"CONVERT(nvarchar(40), {x}, 126)",
            "binary" or "varbinary" or "timestamp" or "image" => $"CONVERT(nvarchar(max), CONVERT(varbinary(max), {x}), 1)",
            "float" or "real" => $"CONVERT(nvarchar(40), {x}, 3)",
            "money" or "smallmoney" => $"CONVERT(nvarchar(40), {x}, 2)",
            _ => $"CONVERT(nvarchar(max), {x})",
        };
        if (visee is not null)
            conv = $"COALESCE((SELECT TOP (1) l.IdV2 FROM rec.Correspondance AS l WHERE l.TableV3 = {Sql.Texte(visee.NomV3)} AND l.IdV3 = {Sql.EnTexte(alias, c)} " +
                   $"AND l.BaseV2 = @b AND l.TableV2 = {Sql.Texte(visee.NomV2)} ORDER BY CASE WHEN l.Origine = 'fusion' THEN 1 ELSE 0 END, l.Le), {conv})";
        return $"CASE WHEN {x} IS NULL THEN N'~' ELSE N'=' + {conv} END";
    }

    /// <summary>SHA-256 du texte des colonnes, separees par un caractere de controle (NCHAR(30)).</summary>
    internal static string Empreinte(string alias, IReadOnlyList<ColonneV2> colonnes) => Empreinte(colonnes.Select(c => Texte(alias, c)));

    private static string Empreinte(IEnumerable<string> textes)
        => "HASHBYTES('SHA2_256', CAST(N'' AS nvarchar(max)) + " + string.Join(" + NCHAR(30) + ", textes) + ")";

    // -----------------------------------------------------------------------------------------------------------------
    // Le rapport
    // -----------------------------------------------------------------------------------------------------------------

    private static async Task EcrireAsync(string connexionV3, RapportComparaison r, string instantaneV2, string instantaneV3, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        var tables = new DataTable();
        foreach (var (nom, type) in new[] { ("IdComparaison", typeof(int)), ("TableV2", typeof(string)), ("TableV3", typeof(string)), ("LignesV2", typeof(long)),
                     ("LignesV3", typeof(long)), ("Comparees", typeof(long)), ("Ecarts", typeof(long)), ("SansCorrespondanceV2", typeof(long)),
                     ("SansCorrespondanceV3", typeof(long)), ("SansLigneV2", typeof(long)), ("SansLigneV3", typeof(long)), ("Rejetees", typeof(long)),
                     ("Resorbes", typeof(long)), ("Colonnes", typeof(int)), ("DureeMs", typeof(long)), ("Detail", typeof(string)) })
            tables.Columns.Add(nom, type);
        foreach (var t in r.Tables)
            tables.Rows.Add(r.Id, t.TableV2, (object?)t.TableV3 ?? DBNull.Value, t.LignesV2, t.LignesV3, t.Comparees, t.Ecarts, t.SansCorrespondanceV2,
                            t.SansCorrespondanceV3, t.SansLigneV2, t.SansLigneV3, t.Rejetees, t.Resorbes, t.Colonnes, t.DureeMs, (object?)t.Detail ?? DBNull.Value);
        var ecarts = new DataTable();
        foreach (var (nom, type) in new[] { ("IdComparaison", typeof(int)), ("Genre", typeof(string)), ("TableV2", typeof(string)), ("IdV2", typeof(string)),
                     ("TableV3", typeof(string)), ("IdV3", typeof(string)), ("Colonnes", typeof(string)), ("Detail", typeof(string)) })
            ecarts.Columns.Add(nom, type);
        foreach (var e in r.Details)
            ecarts.Rows.Add(r.Id, e.Genre, e.TableV2, (object?)e.IdV2 ?? DBNull.Value, (object?)e.TableV3 ?? DBNull.Value, (object?)e.IdV3 ?? DBNull.Value,
                            (object?)e.Colonnes ?? DBNull.Value, e.Detail is null ? DBNull.Value : e.Detail.Length > 1000 ? e.Detail[..999] + "…" : e.Detail);
        foreach (var (table, destination) in new[] { (tables, "rec.ComparaisonTable"), (ecarts, "rec.ComparaisonEcart") })
        {
            if (table.Rows.Count == 0) continue;
            using var copie = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = destination, BulkCopyTimeout = 0 };
            foreach (DataColumn c in table.Columns) copie.ColumnMappings.Add(c.ColumnName, c.ColumnName);
            await copie.WriteToServerAsync(table, ct);
        }
        await using (var cmd = new SqlCommand("""
            UPDATE rec.Comparaison SET Etat = 'fini', Fin = SYSDATETIME(), DureeMs = @ms, InstantaneV2 = @i2, InstantaneV3 = @i3, EtatFigeLe = @le,
                   Tables = @tables, LignesComparees = @comparees, Ecarts = @ecarts, Orphelins = @orphelins, SansCorrespondance = @sans, Resorbes = @resorbes
            WHERE Id = @id;
            """, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = r.DureeMs });
            cmd.Parameters.Add(new SqlParameter("@i2", SqlDbType.NVarChar, 128) { Value = instantaneV2 });
            cmd.Parameters.Add(new SqlParameter("@i3", SqlDbType.NVarChar, 128) { Value = instantaneV3 });
            cmd.Parameters.Add(new SqlParameter("@le", SqlDbType.DateTime2) { Value = (object?)r.EtatFigeLe ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@tables", SqlDbType.Int) { Value = r.Tables.Count });
            cmd.Parameters.Add(new SqlParameter("@comparees", SqlDbType.BigInt) { Value = r.LignesComparees });
            cmd.Parameters.Add(new SqlParameter("@ecarts", SqlDbType.BigInt) { Value = r.Ecarts });
            cmd.Parameters.Add(new SqlParameter("@orphelins", SqlDbType.BigInt) { Value = r.Orphelins });
            cmd.Parameters.Add(new SqlParameter("@sans", SqlDbType.BigInt) { Value = r.SansCorrespondance });
            cmd.Parameters.Add(new SqlParameter("@resorbes", SqlDbType.BigInt) { Value = r.Resorbes });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = r.Id });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Le rapport en clair (outil de l'exploitant, repetition).</summary>
    public static string Texte(RapportComparaison r)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"Controle {r.Id} de {r.BaseV2} : {r.Etat.ToUpperInvariant()} en {r.DureeMs / 1000.0:0.0} s" +
                     (r.EtatFigeLe is { } le ? $", etats figes du {le:yyyy-MM-dd HH:mm:ss}" : "") +
                     $" — {r.LignesComparees} lignes comparees, {r.Ecarts} ecarts, {r.SansCorrespondance} sans correspondance, {r.Orphelins} liens orphelins" +
                     (r.Resorbes > 0 ? $", {r.Resorbes} resorbes a la relecture" : "") + (r.Purges > 0 ? $" ; {r.Purges} rapports purges" : ""));
        if (r.Motif is not null) s.AppendLine("  " + r.Motif);
        s.AppendLine($"  {"table",-14} {"table V3",-16} {"V2",9} {"V3",9} {"comparees",9} {"ecarts",6} {"sansV2",6} {"sansV3",6} {"rejet.",6} {"ms",7}");
        foreach (var t in r.Tables)
            s.AppendLine($"  {t.TableV2,-14} {t.TableV3 ?? "-",-16} {t.LignesV2,9} {t.LignesV3,9} {t.Comparees,9} {t.Ecarts,6} {t.SansCorrespondanceV2,6} " +
                         $"{t.SansCorrespondanceV3,6} {t.Rejetees,6} {t.DureeMs,7}");
        foreach (var e in r.Details.Take(50))
            s.AppendLine($"  {e.Genre} : {e.TableV2} {e.IdV2 ?? ""} -> {e.TableV3 ?? ""} {e.IdV3 ?? ""}{(e.Colonnes is null ? "" : $" [{e.Colonnes}]")}{(e.Detail is null ? "" : $" ({e.Detail})")}");
        if (r.Details.Count > 50) s.AppendLine($"  ... {r.Details.Count - 50} autres lignes de detail dans rec.ComparaisonEcart (controle {r.Id})");
        return s.ToString();
    }

    /// <summary>Une lecture d'un etat fige ou d'une base vivante : un SELECT seul (LectureV2.ExigerLecture), SANS delai --
    /// l'empreinte triee d'une table de 4,5 millions de lignes (ARRIVEE de RIG_DEV) depasse les 120 s de LectureV2.</summary>
    private static async Task<SqlDataReader> LireAsync(SqlConnection cn, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        LectureV2.ExigerLecture(sql);
        var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        cmd.Parameters.AddRange(p);
        return await cmd.ExecuteReaderAsync(ct);
    }

    private static async Task<object?> ValeurAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        LectureV2.ExigerLecture(sql);
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static SqlParameter P(string nom, string valeur, int taille = 128) => new(nom, SqlDbType.NVarChar, Math.Max(taille, valeur.Length)) { Value = valeur };

    private static async Task<object?> Scalaire(SqlConnection cn, string sql, CancellationToken ct, params SqlParameter[] p)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        cmd.Parameters.AddRange(p);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }
}
