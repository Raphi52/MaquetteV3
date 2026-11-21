// fix-ok: fichier NEUF du lot 11, relu avant tout essai : (1) le chargement des fiches prenait cn.ConnectionString, qui perd
// le mot de passe d'une connexion SQL ouverte (Persist Security Info=False) -- la chaine d'origine est passee ; (2) les tables
// de facturation a renvoyer viennent de la preparation (un INSERT vers rigv3.RenvoiFactureV3 echoue si la table manque, meme
// sans ligne) au lieu d'une seconde lecture approximative.
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
using static RigV3.Serveur.Recopie.EcrituresRecopie;

namespace RigV3.Serveur.Recopie;

/// <summary>Le retour s'arrete : la cause, en clair (rien n'est ecrit dans la V2, la transaction est annulee).</summary>
public sealed class RetourImpossible(string message) : Exception(message);

/// <summary>
/// LE RETOUR D'UN GREFFE BETA A L'ANCIEN RIG AVEC LES AFFAIRES CREEES DANS LE NOUVEAU (REC-007 ; lot 11, 2026-11-21). Il
/// prolonge le retour de la numerotation (NUM-022, Base\v2\retour.sql, lot 7), qui rend les compteurs a la V2 : ici, les
/// affaires et les demandes creees dans la V3 depuis le passage sont ecrites dans la V2, pour que le greffe y continue sans
/// rien ressaisir.
///
///   1. La decision (CA08) : seule la personne designee chez Amitel (rec.ResponsableRetour) la prend ; toute decision, prise
///      ou refusee, est enregistree avec son auteur, sa date et son motif (rec.DecisionRetour).
///   2. Les refus avant toute ecriture (<see cref="PreparerAsync"/>) : greffe hors beta ou jour J des autres greffes passe
///      (CA05 : la fin de beta de rigv3.Pont, celle de NUM-022) ; un type de demande de la V3 sans equivalent dans la V2 et
///      sans regle choisie (CA06) ; une table qui pointe vers une affaire ou une demande de la V3 sans regle de retour (T03 :
///      par renvoi a la story de son domaine) ; un compte ou une fiche liee sans equivalent dans la V2.
///   3. La V3 se ferme (rec.EtatRetour « en retour ») : plus aucune creation pour ce greffe (CA03, GardeAffaires).
///   4. Dans UNE transaction, sous le verrou de la recopie de la base : chaque affaire de la V3, puis chaque demande UNE PAR
///      UNE dans l'ordre de creation -- les declencheurs de la V2 ne lisent qu'une ligne ; la date de validation de la V3 est
///      ecrite, le declencheur ne la remplace pas (CA02, ecart code-metier de la story, « a valider ») --, liens traduits par
///      la correspondance (client, dossier, arrivee, type, agent...), numeros gardes tels quels (gestion, inscription, demande :
///      CA01) ; les liens entre demandes de la V3 ensuite (une mere creee apres sa fille) ; les liens qu'une ancienne demande
///      porte vers une demande de la V3 (REC-011 CA04) ; la facturation de la V3 reste dans la V3 avec son renvoi dans la V2
///      (rigv3.RenvoiFactureV3) : rien n'entre dans les chaines signees de la V2 (CA04, regle proposee, a valider par le
///      referent de la Facturation). Chaque ligne recoit sa correspondance (origine « retour ») et son etat commun : la
///      remontee ne la renvoie pas, le controle de nuit (REC-006) la compare (CA07).
///   5. Le greffe est « revenu en V2 ». Une erreur annule tout et rouvre la V3 : la decision est « arretee » avec la cause.
/// La numerotation (retour.sql) se joue ensuite, par l'outil du passage (RigV3.Passage retour).
/// Mode d'emploi : docs/reconstruction/recopie.md, section « Revenir a l'ancien RIG pendant la beta (REC-007) ».
/// </summary>
public static class RetourBeta
{
    /// <summary>Les tables de la facturation qui pointent vers une affaire ou une demande : elles restent dans la V3 (C19, chaque
    /// logiciel garde ses chaines signees) et la V2 recoit un renvoi (CA04). Noms de RIG_DEV (sys.foreign_keys lu le 2026-11-20 :
    /// articles utilises, lettrages, ventilations pointent vers une affaire) et tables_v2 de la story.</summary>
    public static readonly IReadOnlySet<string> TablesFacturation = new HashSet<string>(
        ["ARTICLE_UTILISE", "LETTRAGE", "VENTILATION", "FACTURE", "FACTURE_DUPLICATA", "ENCAISSEMENT", "JET_NP"], StringComparer.OrdinalIgnoreCase);

    public const string SortRenvoi = "reste dans la V3, renvoi dans la V2 (C19)";
    public const string SortSansRegle = "sans règle de retour : à écrire par la story de son domaine";

    private static string V2(string baseV2) => Sql.Q(baseV2) + ".dbo";

    // ---------------------------------------------------------------------------------------------------------------
    // La personne designee et les regles de type
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Designe la personne qui decide le retour de ce greffe (CA08) ; une seule par greffe, la derniere designee.</summary>
    public static async Task DesignerAsync(SqlConnection v3, SqlTransaction? tx, string baseV2, string compte, string par, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(compte)) throw new ArgumentException("Le compte de la personne designee est obligatoire.", nameof(compte));
        await ExecuterAsync(v3, tx, """
            MERGE rec.ResponsableRetour AS r USING (SELECT @b AS BaseV2) AS s ON r.BaseV2 = s.BaseV2
            WHEN MATCHED THEN UPDATE SET Compte = @c, DesignePar = @p, DesigneLe = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (BaseV2, Compte, DesignePar) VALUES (@b, @c, @p);
            """, ct, ("@b", baseV2), ("@c", compte.Trim()), ("@p", par));
    }

    /// <summary>Choisit le type de la V2 sous lequel iront les demandes d'un type cree dans la V3 seule (CA06). Refus si le type
    /// de la V3 a un equivalent dans la V2 (il n'a pas besoin de regle) ou si le type de la V2 n'existe pas dans sa base.</summary>
    public static async Task ChoisirTypeAsync(SqlConnection v3, SqlTransaction? tx, string baseV2, int idTypeV3, int idTypeV2, string par, CancellationToken ct)
    {
        if (await ScalaireAsync(v3, tx, "SELECT CONVERT(nvarchar(20), Id) FROM rec.TypeDemande WHERE Id = @t;", ct, ("@t", idTypeV3)) is null)
            throw new RetourImpossible($"Le type {idTypeV3} n'existe pas dans la V3.");
        if (await ScalaireAsync(v3, tx, "SELECT IdV2 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = N'CATEGORIE' AND TableV3 = N'rec.TypeDemande' AND IdV3 = @t;",
                                ct, ("@b", baseV2), ("@t", idTypeV3.ToString(System.Globalization.CultureInfo.InvariantCulture))) is { } numero)
            throw new RetourImpossible($"Le type {idTypeV3} de la V3 est le type {numero} de l'ancien RIG : ses demandes y vont sous ce type, sans règle à choisir.");
        if (await ScalaireAsync(v3, tx, $"SELECT CONVERT(nvarchar(20), CTGR_ID_CTGR) FROM {V2(baseV2)}.CATEGORIE WHERE CTGR_ID_CTGR = @t;", ct, ("@t", idTypeV2)) is null)
            throw new RetourImpossible($"Le type {idTypeV2} n'existe pas dans l'ancien RIG ({baseV2}).");
        await ExecuterAsync(v3, tx, """
            MERGE rec.RegleTypeRetour AS r USING (SELECT @b AS BaseV2, @t3 AS IdTypeV3) AS s ON r.BaseV2 = s.BaseV2 AND r.IdTypeV3 = s.IdTypeV3
            WHEN MATCHED THEN UPDATE SET IdTypeV2 = @t2, ChoisiPar = @p, ChoisiLe = SYSDATETIME()
            WHEN NOT MATCHED THEN INSERT (BaseV2, IdTypeV3, IdTypeV2, ChoisiPar) VALUES (@b, @t3, @t2, @p);
            """, ct, ("@b", baseV2), ("@t3", idTypeV3), ("@t2", idTypeV2), ("@p", par));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // La preparation : ce que le retour ferait, ce qui l'empeche
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Les affaires creees dans la V3 (sans correspondance V2), en texte pour un IN.</summary>
    private const string AffairesV3 = "SELECT a.AFFRE_ID_AFFRE FROM v2.AFFAIRE AS a WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance AS c " +
                                      "WHERE c.TableV3 = N'v2.AFFAIRE' AND c.IdV3 = CONVERT(nvarchar(200), a.AFFRE_ID_AFFRE) AND c.BaseV2 = @b AND c.TableV2 = N'AFFAIRE')";
    private const string DemandesV3 = "SELECT d.DMND_ID_DMND FROM v2.DEMANDE AS d WHERE NOT EXISTS (SELECT 1 FROM rec.Correspondance AS c " +
                                      "WHERE c.TableV3 = N'v2.DEMANDE' AND c.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_DMND) AND c.BaseV2 = @b AND c.TableV2 = N'DEMANDE')";

    public static async Task<PreparationRetour> PreparerAsync(SqlConnection v3, string baseV2, string greffe, CancellationToken ct)
    {
        var refus = new List<string>();
        var etat = await ScalaireAsync(v3, null, "SELECT Etat + N'|' + CONVERT(nvarchar(16), Depuis, 120) FROM rec.EtatRetour WHERE BaseV2 = @b;", ct, ("@b", baseV2));
        if (etat is not null) refus.Add($"le greffe est déjà « {etat.Split('|')[0]} » depuis le {etat.Split('|')[1]}");

        // CA05 : la beta, telle que la bascule l'a posee dans la V2 (rigv3.Pont, NUM-022) ; l'heure est celle du serveur SQL.
        bool enBeta = false;
        DateTime? finBeta = null;
        var pont = await ScalaireAsync(v3, null, $"SELECT CONVERT(nvarchar(1), CASE WHEN OBJECT_ID(N'{baseV2.Replace("'", "''")}.rigv3.Pont', N'U') IS NULL THEN 0 ELSE 1 END);", ct);
        if (pont != "1") refus.Add($"{baseV2} n'a pas été basculée vers la V3 (rigv3.Pont absent) : le retour ne vaut que pour un greffe bêta passé");
        else
        {
            await using var cmd = new SqlCommand($"""
                SELECT p.FinBeta, CASE WHEN p.FinBeta IS NOT NULL AND SYSDATETIME() < p.FinBeta THEN 1 ELSE 0 END,
                       (SELECT COUNT(*) FROM {Sql.Q(baseV2)}.rigv3.CorpsOrigine)
                FROM {Sql.Q(baseV2)}.rigv3.Pont AS p WHERE p.Id = 1;
                """, v3);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct))
            {
                finBeta = l.IsDBNull(0) ? null : l.GetDateTime(0);
                enBeta = l.GetInt32(1) == 1 && l.GetInt32(2) > 0;
            }
            if (finBeta is null) refus.Add("le greffe n'est pas un greffe bêta : le retour à l'ancien RIG n'existe que pendant la bêta (Q8)");
            else if (!enBeta)
                refus.Add($"le jour J des autres greffes est passé (fin de la bêta le {finBeta:dd/MM/yyyy HH:mm}) : après le jour J, on corrige la V3 sur place (CA05)");
        }

        var responsable = await ScalaireAsync(v3, null, "SELECT Compte FROM rec.ResponsableRetour WHERE BaseV2 = @b;", ct, ("@b", baseV2));
        if (responsable is null) refus.Add("personne n'est désignée pour décider le retour de ce greffe (rec.ResponsableRetour, CA08)");

        var affaires = int.Parse((await ScalaireAsync(v3, null, $"SELECT CONVERT(nvarchar(20), COUNT(*)) FROM ({AffairesV3}) AS x;", ct, ("@b", baseV2)))!);
        var demandes = int.Parse((await ScalaireAsync(v3, null, $"SELECT CONVERT(nvarchar(20), COUNT(*)) FROM ({DemandesV3}) AS x;", ct, ("@b", baseV2)))!);

        // CA06 : les types de la V3 seule, et la regle choisie pour chacun.
        var types = new List<TypeAuRetour>();
        await using (var cmd = new SqlCommand($"""
            SELECT d.DMND_ID_CTGR, t.Reference, ISNULL(t.LibelleEcran, t.Libelle), COUNT(*), MAX(r.IdTypeV2), MAX(CASE WHEN t.Id IS NULL THEN 1 ELSE 0 END)
            FROM v2.DEMANDE AS d JOIN ({DemandesV3}) AS n ON n.DMND_ID_DMND = d.DMND_ID_DMND
            LEFT JOIN rec.TypeDemande AS t ON t.Id = d.DMND_ID_CTGR
            LEFT JOIN rec.RegleTypeRetour AS r ON r.BaseV2 = @b AND r.IdTypeV3 = d.DMND_ID_CTGR
            WHERE d.DMND_ID_CTGR IS NOT NULL AND NOT EXISTS (SELECT 1 FROM rec.Correspondance AS k WHERE k.BaseV2 = @b AND k.TableV2 = N'CATEGORIE'
                                                             AND k.TableV3 = N'rec.TypeDemande' AND k.IdV3 = CONVERT(nvarchar(200), d.DMND_ID_CTGR))
            GROUP BY d.DMND_ID_CTGR, t.Reference, t.LibelleEcran, t.Libelle ORDER BY d.DMND_ID_CTGR;
            """, v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
            {
                types.Add(new TypeAuRetour(l.GetInt32(0), l.IsDBNull(1) ? null : l.GetValue(1).ToString(), l.IsDBNull(2) ? null : l.GetValue(2).ToString(),
                                                 l.GetInt32(3), l.IsDBNull(4) ? null : l.GetInt32(4)));
                if (l.GetInt32(5) == 1) refus.Add($"type {l.GetInt32(0)} de {l.GetInt32(3)} demande(s) de la V3 inconnu de rec.TypeDemande");
            }
        }
        foreach (var t in types.Where(t => t.IdTypeV2 is null))
            refus.Add($"le type « {t.Libelle ?? t.Reference ?? t.IdTypeV3.ToString(System.Globalization.CultureInfo.InvariantCulture)} » ({t.Demandes} demande(s)) n'existe pas dans l'ancien RIG : " +
                      "choisir le type de l'ancien RIG sous lequel ses demandes iront (CA06)");

        // Les agents de la V3 sans compte dans la V2 (la recopie des comptes, REC-017, les y a crees).
        await using (var cmd = new SqlCommand($"""
            SELECT DISTINCT x.Compte FROM (
                SELECT d.DMND_ID_UTLST AS Compte FROM v2.DEMANDE AS d JOIN ({DemandesV3}) AS n ON n.DMND_ID_DMND = d.DMND_ID_DMND
                UNION ALL SELECT a.AFFRE_ID_UTLST_CREATION FROM v2.AFFAIRE AS a JOIN ({AffairesV3}) AS n ON n.AFFRE_ID_AFFRE = a.AFFRE_ID_AFFRE
                UNION ALL SELECT a.AFFRE_ID_UTLST_EN_COURS FROM v2.AFFAIRE AS a JOIN ({AffairesV3}) AS n ON n.AFFRE_ID_AFFRE = a.AFFRE_ID_AFFRE) AS x
            WHERE x.Compte IS NOT NULL AND NOT EXISTS (SELECT 1 FROM rec.Correspondance AS k WHERE k.BaseV2 = @b AND k.TableV2 = N'UTILISATEUR'
                                                       AND k.TableV3 = N'soc.Compte' AND k.IdV3 = CONVERT(nvarchar(200), x.Compte));
            """, v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) refus.Add($"le compte {l.GetInt32(0)} de la V3 n'a pas d'équivalent dans l'ancien RIG (recopie des comptes, REC-017)");
        }

        // T03 : les tables qui pointent vers une affaire ou une demande de la V3.
        var dependantes = await DependantesAsync(v3, baseV2, ct);
        foreach (var d in dependantes.Where(d => d.Sort == SortSansRegle))
            refus.Add($"{d.TableV2} : {d.Lignes} ligne(s) rattachée(s) à des affaires ou demandes de la V3, sans règle de retour (story de son domaine, T03)");
        if (dependantes.Any(d => d.Sort == SortRenvoi) && pont == "1"
            && await ScalaireAsync(v3, null, $"SELECT CONVERT(nvarchar(1), CASE WHEN OBJECT_ID(N'{baseV2.Replace("'", "''")}.rigv3.RenvoiFactureV3', N'U') IS NULL THEN 0 ELSE 1 END);", ct) != "1")
            refus.Add($"{baseV2} n'a pas la table des renvois vers les factures de la V3 (rigv3.RenvoiFactureV3) : rejouer Base\\v2\\pont-v2.sql (lot 11)");

        return new PreparationRetour(greffe, baseV2, etat?.Split('|')[0] ?? "en V3", enBeta, finBeta, responsable, affaires, demandes, [.. types],
                                     [.. dependantes], [.. refus]);
    }

    /// <summary>Les tables de la base V3 (copies v2.*) qui pointent vers AFFAIRE ou DEMANDE dans le catalogue de la V2, et leurs
    /// lignes rattachees a une affaire ou une demande de la V3.</summary>
    private static async Task<List<LignesDependantes>> DependantesAsync(SqlConnection v3, string baseV2, CancellationToken ct)
    {
        var cles = new List<(string Table, string Colonne, string Visee)>();
        await using (var cmd = new SqlCommand($"""
            SELECT DISTINCT tp.name, cp.name, tr.name
            FROM {Sql.Q(baseV2)}.sys.foreign_keys AS fk
            JOIN {Sql.Q(baseV2)}.sys.foreign_key_columns AS fc ON fc.constraint_object_id = fk.object_id
            JOIN {Sql.Q(baseV2)}.sys.tables AS tp ON tp.object_id = fk.parent_object_id
            JOIN {Sql.Q(baseV2)}.sys.tables AS tr ON tr.object_id = fk.referenced_object_id
            JOIN {Sql.Q(baseV2)}.sys.columns AS cp ON cp.object_id = fc.parent_object_id AND cp.column_id = fc.parent_column_id
            WHERE tr.name IN (N'AFFAIRE', N'DEMANDE') AND tp.name NOT IN (N'AFFAIRE', N'DEMANDE')
              AND (SELECT COUNT(*) FROM {Sql.Q(baseV2)}.sys.foreign_key_columns AS n WHERE n.constraint_object_id = fk.object_id) = 1;
            """, v3))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) cles.Add((l.GetString(0), l.GetString(1), l.GetString(2)));
        var r = new List<LignesDependantes>();
        foreach (var g in cles.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase))
        {
            if (await ScalaireAsync(v3, null, "SELECT CONVERT(nvarchar(1), 1) WHERE OBJECT_ID(@t, N'U') IS NOT NULL;", ct, ("@t", "v2." + g.Key)) is null) continue;
            var conditions = g.Select(c => $"x.{Sql.Q(c.Colonne)} IN ({(c.Visee.Equals("AFFAIRE", StringComparison.OrdinalIgnoreCase) ? AffairesV3 : DemandesV3)})");
            var n = long.Parse((await ScalaireAsync(v3, null, $"SELECT CONVERT(nvarchar(20), COUNT_BIG(*)) FROM v2.{Sql.Q(g.Key)} AS x WHERE {string.Join(" OR ", conditions)};",
                                                    ct, ("@b", baseV2)))!, System.Globalization.CultureInfo.InvariantCulture);
            if (n > 0) r.Add(new LignesDependantes(g.Key, n, TablesFacturation.Contains(g.Key) ? SortRenvoi : SortSansRegle));
        }
        return r;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Le retour
    // ---------------------------------------------------------------------------------------------------------------

    public static async Task<BilanRetour> RevenirAsync(string connexionV3, string baseV2, string greffe, string auteur, string motif,
                                                       ILogger? log = null, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);

        async Task<BilanRetour> Decision(string etatDecision, string? detail, int affaires = 0, int demandes = 0, int renvois = 0, int? id = null)
        {
            if (id is null)
                id = int.Parse((await ScalaireAsync(cn, null, """
                    INSERT INTO rec.DecisionRetour (BaseV2, Auteur, Motif, Etat, Detail, FiniLe, DureeMs) OUTPUT CONVERT(nvarchar(20), inserted.Id)
                    VALUES (@b, @a, @m, @e, @d, SYSDATETIME(), @ms);
                    """, ct, ("@b", baseV2), ("@a", auteur), ("@m", string.IsNullOrWhiteSpace(motif) ? "(aucun motif)" : motif.Trim()), ("@e", etatDecision),
                    ("@d", (object?)detail ?? DBNull.Value), ("@ms", chrono.ElapsedMilliseconds)))!);
            else
                await ExecuterAsync(cn, null, """
                    UPDATE rec.DecisionRetour SET Etat = @e, Detail = @d, FiniLe = SYSDATETIME(), Affaires = @af, Demandes = @de, Renvois = @r, DureeMs = @ms WHERE Id = @id;
                    """, ct, ("@e", etatDecision), ("@d", (object?)detail ?? DBNull.Value), ("@af", affaires), ("@de", demandes), ("@r", renvois),
                    ("@ms", chrono.ElapsedMilliseconds), ("@id", id.Value));
            await using var cmd = new SqlCommand("SELECT Le FROM rec.DecisionRetour WHERE Id = @id;", cn);
            cmd.Parameters.AddWithValue("@id", id.Value);
            var le = (DateTime)(await cmd.ExecuteScalarAsync(ct))!;
            return new BilanRetour(id.Value, etatDecision, auteur, le, motif, detail, affaires, demandes, renvois, chrono.ElapsedMilliseconds);
        }

        // 1. CA08 : seule la personne designee decide ; un motif est obligatoire.
        var responsable = await ScalaireAsync(cn, null, "SELECT Compte FROM rec.ResponsableRetour WHERE BaseV2 = @b;", ct, ("@b", baseV2));
        if (responsable is null || !string.Equals(responsable, auteur.Trim(), StringComparison.OrdinalIgnoreCase))
            return await Decision("refusee", responsable is null
                ? "personne n'est désignée pour décider le retour de ce greffe : rien n'est fait (CA08)"
                : $"seule la personne désignée ({responsable}) décide le retour de ce greffe ; demandé par {auteur} : refusé (CA08)");
        if (string.IsNullOrWhiteSpace(motif)) return await Decision("refusee", "le motif du retour est obligatoire (CA08)");

        // 2. Les refus avant toute ecriture.
        var preparation = await PreparerAsync(cn, baseV2, greffe, ct);
        if (preparation.Refus.Length > 0) return await Decision("refusee", "retour refusé avant toute écriture : " + string.Join(" ; ", preparation.Refus));

        // Un passage de la recopie a la fois : le retour en est un (Serveur\Recopie\ServiceRecopie.cs, meme verrou).
        await using (var verrou = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @n, @LockMode = 'Exclusive', @LockOwner = 'Session', " +
                                                 "@LockTimeout = 60000; SELECT @r;", cn))
        {
            verrou.Parameters.AddWithValue("@n", "rigv3.recopie." + baseV2);
            if ((int)(await verrou.ExecuteScalarAsync(ct))! < 0)
                return await Decision("refusee", "un passage de la recopie de cette base ne se termine pas : réessayer");
        }
        // 3. La V3 se ferme pour ce greffe (CA03).
        var decision = await Decision("en cours", null);
        await ExecuterAsync(cn, null, "INSERT INTO rec.EtatRetour (BaseV2, Etat, IdDecision) VALUES (@b, 'en retour', @id);", ct, ("@b", baseV2), ("@id", decision.IdDecision));
        try
        {
            var (affaires, demandes, renvois) = await EcrireAsync(cn, connexionV3, baseV2,
                [.. preparation.Dependantes.Where(d => d.Sort == SortRenvoi).Select(d => d.TableV2)], ct);
            await ExecuterAsync(cn, null, "UPDATE rec.EtatRetour SET Etat = 'revenu en V2', Depuis = SYSDATETIME() WHERE BaseV2 = @b;", ct, ("@b", baseV2));
            return await Decision("faite", $"{affaires} affaire(s) et {demandes} demande(s) créées dans la V3 écrites dans l'ancien RIG, numéros gardés ; " +
                                           $"{renvois} ligne(s) de facturation restées dans la V3 avec leur renvoi (rigv3.RenvoiFactureV3) ; la V3 refuse désormais " +
                                           "toute création pour ce greffe", affaires, demandes, renvois, decision.IdDecision);
        }
        catch (Exception e) when (e is SqlException or RetourImpossible or InvalidOperationException or FormatException)
        {
            log?.LogError(e, "Retour du greffe {Greffe} ({BaseV2}) a l'ancien RIG arrete : {Cause}", greffe, baseV2, e.Message);
            await RetirerMarqueAsync(cn);
            await ExecuterAsync(cn, null, "DELETE FROM rec.EtatRetour WHERE BaseV2 = @b;", CancellationToken.None, ("@b", baseV2));
            return await Decision("arretee", $"retour arrêté, rien n'est écrit dans l'ancien RIG et la V3 est rouverte : {e.Message}", id: decision.IdDecision);
        }
        finally
        {
            if (cn.State == ConnectionState.Open)
                await ExecuterAsync(cn, null, "EXEC sp_releaseapplock @Resource = @n, @LockOwner = 'Session';", CancellationToken.None, ("@n", "rigv3.recopie." + baseV2));
        }
    }

    /// <summary>L'ecriture dans la V2, en une transaction (etape 4 de l'en-tete).</summary>
    /// <param name="aRenvoyer">Les tables de facturation qui ont des lignes rattachees a la V3 (la preparation les a comptees).</param>
    private static async Task<(int Affaires, int Demandes, int Renvois)> EcrireAsync(SqlConnection cn, string connexionV3, string baseV2,
                                                                                     IReadOnlyList<string> aRenvoyer, CancellationToken ct)
    {
        // La chaine d'origine, pas cn.ConnectionString : une connexion ouverte n'y garde pas le mot de passe.
        var fiches = await FichesRecopiees.ChargerAsync(connexionV3, baseV2, null, ct);
        if (!fiches.ParTableV2.TryGetValue("AFFAIRE", out var tA) || !fiches.ParTableV2.TryGetValue("DEMANDE", out var tD) || !tA.Regle.RemonteeSeule || !tD.Regle.RemonteeSeule)
            throw new RetourImpossible("les affaires et les demandes ne sont pas dans la recopie de ce greffe (mise en service d'avant le lot 11) : refaire sa mise en service");
        var comptes = fiches.ParTableV2.GetValueOrDefault("UTILISATEUR");
        var idTransfert = int.Parse((await ScalaireAsync(cn, null, "SELECT CONVERT(nvarchar(20), IdTransfert) FROM rec.Remontee WHERE BaseV2 = @b;", ct, ("@b", baseV2)))
                                    ?? throw new RetourImpossible("la recopie de ce greffe n'est pas en service (rec.Remontee)"));

        async Task<List<string>> Ids(string sql)
        {
            var r = new List<string>();
            await using var cmd = new SqlCommand(sql + " ORDER BY 1;", cn);
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) r.Add(Convert.ToString(l.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
            return r;
        }
        var affairesV3 = await Ids(AffairesV3);
        var demandesV3 = await Ids(DemandesV3);
        var nouvelles = new HashSet<string>(demandesV3, StringComparer.Ordinal);

        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Un numero de la V3 (compte, type) en numero de la V2 (RegleRecopie.NumerosV2).
            async Task<string> NumeroV2(string table, string valeur, string quoi)
            {
                if (table.Equals("UTILISATEUR", StringComparison.OrdinalIgnoreCase))
                    return await ScalaireAsync(cn, tx, "SELECT IdV2 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = N'UTILISATEUR' AND TableV3 = N'soc.Compte' AND IdV3 = @v;",
                                               ct, ("@b", baseV2), ("@v", valeur))
                           ?? throw new RetourImpossible($"{quoi} : le compte {valeur} de la V3 n'a pas d'équivalent dans l'ancien RIG");
                if (table.Equals("CATEGORIE", StringComparison.OrdinalIgnoreCase))
                    return await ScalaireAsync(cn, tx, "SELECT IdV2 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = N'CATEGORIE' AND TableV3 = N'rec.TypeDemande' AND IdV3 = @v;",
                                               ct, ("@b", baseV2), ("@v", valeur))
                           ?? await ScalaireAsync(cn, tx, "SELECT CONVERT(nvarchar(20), IdTypeV2) FROM rec.RegleTypeRetour WHERE BaseV2 = @b AND IdTypeV3 = CONVERT(int, @v);",
                                                  ct, ("@b", baseV2), ("@v", valeur))
                           ?? throw new RetourImpossible($"{quoi} : le type {valeur} de la V3 n'a ni équivalent ni règle de retour (CA06)");
                throw new RetourImpossible($"{quoi} : aucune traduction connue vers {table}");
            }
            async Task<LigneJson> PourLaV2(TableRecopiee t, string idV3, LigneJson ligne, ISet<string> sansLien, string quoi)
            {
                var r = ligne.Copie();
                foreach (var (colonne, visee) in t.Regle.NumerosV2)
                    if (r.TryGetValue(colonne, out var brut) && TableRecopiee.TexteDe(brut) is { } valeur)
                        r[colonne] = TableRecopiee.Brut(await NumeroV2(visee, valeur, $"{quoi} ({colonne})"), t.V2.Colonne(colonne));
                foreach (var c in sansLien) r[c] = "null";
                var enV2 = await TraduireAsync(cn, tx, t, r, versV2: true, ct);
                // Un lien qui reste sans equivalent dans la V2 serait un identifiant de la V3 ecrit dans la V2 : arret.
                foreach (var (colonne, visee) in t.Liens)
                    if (!sansLien.Contains(colonne) && enV2.TryGetValue(colonne, out var b) && TableRecopiee.TexteDe(b) is { } v && TableRecopiee.TexteDe(ligne[colonne]) is { } v3
                        && await IdV2Async(cn, tx, visee, v3, ct) is null)
                        throw new RetourImpossible($"{quoi} : {colonne} vise {visee.Regle.Libelle} {v} de la V3, sans équivalent dans l'ancien RIG");
                return enV2;
            }

            // Les affaires.
            foreach (var id in affairesV3)
            {
                var ligne = await LireV3Async(cn, tx, tA, id, true, ct, avecPropres: true) ?? throw new RetourImpossible($"affaire {id} disparue de la V3");
                var enV2 = await PourLaV2(tA, id, ligne, new HashSet<string>(), $"affaire {id}");
                var idV2 = await CreerV2Async(cn, tx, tA, id, enV2, ct);
                await CorrespondreAsync(cn, tx, tA, idV2, id, idTransfert, "retour", ct);
            }

            // Les demandes, une par une ; un lien vers une demande de la V3 pas encore ecrite attend la fin (une mere creee apres).
            var suivre = new List<(string IdV3, string IdV2, string[] Colonnes)>();
            var liensDemandes = tD.Liens.Where(l => l.Value == tD).Select(l => l.Key).ToList();
            var ecrites = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in demandesV3)
            {
                var ligne = await LireV3Async(cn, tx, tD, id, true, ct, avecPropres: true) ?? throw new RetourImpossible($"demande {id} disparue de la V3");
                var plusTard = liensDemandes.Where(c => ligne.TryGetValue(c, out var b) && TableRecopiee.TexteDe(b) is { } v && nouvelles.Contains(v) && !ecrites.Contains(v))
                                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var enV2 = await PourLaV2(tD, id, ligne, plusTard, $"demande {id}");
                var idV2 = await CreerV2Async(cn, tx, tD, id, enV2, ct);
                await CorrespondreAsync(cn, tx, tD, idV2, id, idTransfert, "retour", ct);
                ecrites.Add(id);
                if (plusTard.Count > 0) suivre.Add((id, idV2, [.. plusTard]));
            }
            foreach (var (idV3, idV2, colonnes) in suivre)
            {
                var ligne = (await LireV3Async(cn, tx, tD, idV3, false, ct))!;
                var enV2 = await TraduireAsync(cn, tx, tD, ligne, versV2: true, ct);
                await ModifierV2Async(cn, tx, tD, idV2, enV2, colonnes, ct);
            }

            // REC-011 CA04 : les liens qu'une ancienne demande porte vers une demande de la V3, ecrits dans la V2.
            var liensV2 = new List<(int Id, int Ancienne, string Lien, int Visee)>();
            await using (var cmd = new SqlCommand("SELECT Id, IdDemande, Lien, IdVisee FROM rec.LienDemande WHERE Porteuse = 'V2' AND EcritV2Le IS NULL ORDER BY Id;", cn, tx))
            await using (var l = await cmd.ExecuteReaderAsync(ct))
                while (await l.ReadAsync(ct)) liensV2.Add((l.GetInt32(0), l.GetInt32(1), l.GetString(2), l.GetInt32(3)));
            foreach (var (idLien, ancienne, lien, visee) in liensV2)
            {
                var ancienneV2 = await IdV2Async(cn, tx, tD, ancienne.ToString(System.Globalization.CultureInfo.InvariantCulture), ct)
                                 ?? throw new RetourImpossible($"lien {idLien} : la demande {ancienne} n'est pas une demande de l'ancien RIG");
                var viseeV2 = await IdV2Async(cn, tx, tD, visee.ToString(System.Globalization.CultureInfo.InvariantCulture), ct)
                              ?? throw new RetourImpossible($"lien {idLien} : la demande {visee} de la V3 n'a pas été écrite dans l'ancien RIG");
                var numero = await ScalaireAsync(cn, tx, "SELECT DMND_NUM_DEMANDE FROM v2.DEMANDE WHERE DMND_ID_DMND = @v;", ct, ("@v", visee));
                await ExecuterAsync(cn, tx, lien == "inhibitrice"
                    ? $"WITH CHANGE_TRACKING_CONTEXT (@marque) UPDATE {V2(baseV2)}.DEMANDE SET DMND_ID_DMND_INHIBITRICE = @v, DMND_INHIBEE = 1, " +
                      "DMND_TEXTE_INHIBITEUR = LEFT('Bloquée par la demande ' + ISNULL(@n, '?'), 255) WHERE DMND_ID_DMND = @a;"
                    : $"WITH CHANGE_TRACKING_CONTEXT (@marque) UPDATE {V2(baseV2)}.DEMANDE SET DMND_ID_DMND_MERE = @v WHERE DMND_ID_DMND = @a;",
                    ct, ("@marque", PontRecopie.MarqueRecopie), ("@v", int.Parse(viseeV2)), ("@a", int.Parse(ancienneV2)), ("@n", (object?)numero ?? DBNull.Value));
                await ExecuterAsync(cn, tx, "UPDATE rec.LienDemande SET EcritV2Le = SYSDATETIME() WHERE Id = @id;", ct, ("@id", idLien));
            }

            // CA04 : la facturation de la V3 reste dans la V3 ; la V2 recoit le renvoi, par affaire.
            var renvois = 0;
            foreach (var table in aRenvoyer) renvois += await RenvoyerAsync(cn, tx, baseV2, table, ct);

            // La copie de la V3 devient le miroir de la V2 : ses numeros de la V2 (type, agent) remplacent ceux de la V3, sous la
            // marque de la remontee (la garde ne refuse pas), puis l'etat commun de chaque ligne.
            await ExecuterAsync(cn, tx, $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = 1;", ct);
            foreach (var (t, ids) in new[] { (tA, affairesV3), (tD, demandesV3) })
                foreach (var id in ids)
                {
                    var idV2 = (await IdV2Async(cn, tx, t, id, ct))!;
                    var ligneV2 = (await LireV2Async(cn, tx, t, idV2, false, ct))!;
                    foreach (var colonne in t.Regle.NumerosV2.Keys.Where(c => t.Colonnes.Any(x => x.Nom.Equals(c, StringComparison.OrdinalIgnoreCase))))
                        await ExecuterAsync(cn, tx, $"UPDATE {t.NomV3Sql} SET {Sql.Q(colonne)} = (SELECT x.{Sql.Q(colonne)} FROM {t.NomV2Sql} AS x WHERE {t.CleEgale("x", "@id2")}) " +
                                                    $"WHERE {t.CleEgaleV3(t.NomV3Sql, "@id3")};", ct, ("@id2", idV2), ("@id3", id));
                    await NoterEtatAsync(cn, tx, t, idV2, ligneV2, "V3 vers V2", ct);
                }
            await ExecuterAsync(cn, tx, $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = NULL;", ct);
            await tx.CommitAsync(ct);
            return (affairesV3.Count, demandesV3.Count, renvois);
        }
        catch
        {
            if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>Les lignes d'une table de facturation de la V3 rattachees a une affaire (ou une demande) creee dans la V3 et
    /// ecrite dans la V2 au retour : un renvoi par ligne dans rigv3.RenvoiFactureV3 de la V2, l'affaire en identifiant V2.</summary>
    private static async Task<int> RenvoyerAsync(SqlConnection cn, SqlTransaction tx, string baseV2, string table, CancellationToken ct)
    {
        var cles = new List<(string Colonne, string Visee)>();
        await using (var cmd = new SqlCommand($"""
            SELECT cp.name, tr.name FROM {Sql.Q(baseV2)}.sys.foreign_keys AS fk
            JOIN {Sql.Q(baseV2)}.sys.foreign_key_columns AS fc ON fc.constraint_object_id = fk.object_id
            JOIN {Sql.Q(baseV2)}.sys.tables AS tp ON tp.object_id = fk.parent_object_id
            JOIN {Sql.Q(baseV2)}.sys.tables AS tr ON tr.object_id = fk.referenced_object_id
            JOIN {Sql.Q(baseV2)}.sys.columns AS cp ON cp.object_id = fc.parent_object_id AND cp.column_id = fc.parent_column_id
            WHERE tp.name = @t AND tr.name IN (N'AFFAIRE', N'DEMANDE');
            """, cn, tx))
        {
            cmd.Parameters.AddWithValue("@t", table);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) cles.Add((l.GetString(0), l.GetString(1)));
        }
        if (cles.Count == 0) return 0;
        var pk = await ScalaireAsync(cn, tx, """
            SELECT TOP (1) c.name FROM sys.indexes AS i JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@t) AND i.is_primary_key = 1 ORDER BY ic.key_ordinal;
            """, ct, ("@t", "v2." + table));
        if (pk is null) return 0;
        var total = 0;
        foreach (var (colonne, visee) in cles)
        {
            // L'affaire de la ligne : directe, ou celle de sa demande ; seulement les affaires que ce retour vient d'ecrire.
            var affaire = visee.Equals("AFFAIRE", StringComparison.OrdinalIgnoreCase) ? $"x.{Sql.Q(colonne)}"
                : $"(SELECT d.DMND_ID_AFFRE FROM v2.DEMANDE AS d WHERE d.DMND_ID_DMND = x.{Sql.Q(colonne)})";
            await using var cmd = new SqlCommand($"""
                INSERT INTO {Sql.Q(baseV2)}.rigv3.RenvoiFactureV3 (IdAffaireV2, TableV3, IdV3, BaseV3)
                SELECT DISTINCT TRY_CONVERT(int, c.IdV2), N'v2.{table}', CONVERT(nvarchar(200), x.{Sql.Q(pk)}), DB_NAME()
                FROM v2.{Sql.Q(table)} AS x
                JOIN rec.Correspondance AS c ON c.BaseV2 = @b AND c.TableV2 = N'AFFAIRE' AND c.TableV3 = N'v2.AFFAIRE' AND c.Origine = 'retour'
                                            AND c.IdV3 = CONVERT(nvarchar(200), {affaire})
                WHERE NOT EXISTS (SELECT 1 FROM {Sql.Q(baseV2)}.rigv3.RenvoiFactureV3 AS r WHERE r.TableV3 = N'v2.{table}' AND r.IdV3 = CONVERT(nvarchar(200), x.{Sql.Q(pk)}));
                """, cn, tx);
            cmd.Parameters.AddWithValue("@b", baseV2);
            total += await cmd.ExecuteNonQueryAsync(ct);
        }
        return total;
    }
}
