// fix-ok: fichier NEUF du lot 11, ecrit puis relu avant tout essai : le critere « client » renommait sa colonne par un
// Replace sur le texte SQL ; l'alias DMND_ID_AFFRE est desormais ecrit dans la requete elle-meme. Pas un correctif d'un defaut mesure.
using System.Data;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Recopie;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// L'HISTORIQUE DES AFFAIRES DES DEUX LOGICIELS, LU DANS LA BASE V3 DU GREFFE (REC-010 T03, REC-011 T04, REC-018 T03).
/// Les anciennes affaires y sont tenues a jour par la remontee (GardeAffaires.cs) : tout se lit ici, sans attendre la V2 ;
/// une demande validee dans l'ancien RIG se relit validee au passage suivant de la recopie (5 s, REC-010 CA02). Le client
/// d'une affaire est la fiche de la V3 (rec.Client, la V3 fait foi : CA05), jamais une copie figee. Les echanges avec les
/// partenaires sont lus dans la base des echanges (EDI_GREFFE), par code greffe et identifiant V2 de la demande, celui que
/// rec.Correspondance donne (CA04). Tout est en lecture : aucune route ne modifie une ancienne affaire (CA03 : la garde le
/// refuse dans la base).
/// </summary>
public static class HistoriqueAffaires
{
    /// <summary>Au plus tant d'affaires par historique (une entreprise tres ancienne en a des centaines) ; la note le dit.</summary>
    public const int AffairesAuPlus = 200;

    /// <summary>Le libelle d'un etat de demande de la V2 (RigMetier, Demande.eEtatDemande).</summary>
    public static string EtatLibelle(string? etat) => etat switch
    {
        "Q" => "qualifiée", "V" => "validée", "W" => "en attente", "X" => "supprimée", "A" => "arrivée", "N" => "en réclamation",
        "R" => "réservée", "I" => "interrompue", "T" => "terminée", "O" => "refusée", null => "?", _ => etat,
    };

    private static string Origine(string table, string alias) =>
        $"(SELECT TOP (1) c.IdV2 FROM rec.Correspondance AS c WHERE c.TableV3 = N'v2.{table}' AND c.IdV3 = CONVERT(nvarchar(200), {alias}) " +
        $"AND c.BaseV2 = @b AND c.TableV2 = N'{table}')";

    /// <summary>L'historique d'une entreprise (son SIREN), d'un dossier (son numero de gestion), d'un client (son code abrege)
    /// ou d'une arrivee (son numero).</summary>
    public static async Task<VueHistorique> LireAsync(SqlConnection v3, string baseV2, string codeGreffe, string critere, string valeur,
                                                      string? connexionEchanges, CancellationToken ct)
    {
        valeur = valeur.Trim();
        var (selection, titre) = critere switch
        {
            "siren" => ("SELECT DISTINCT d.DMND_ID_AFFRE FROM v2.DEMANDE AS d JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS " +
                        "LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE (s.Siren = @v OR e.Siren = @v) AND d.DMND_ID_AFFRE IS NOT NULL",
                        "SELECT TOP (1) CONCAT(e.Denomination, N' — SIREN ', e.Siren) FROM rec.Entreprise AS e WHERE e.Siren = @v ORDER BY e.Id"),
            "gestion" => ("SELECT DISTINCT d.DMND_ID_AFFRE FROM v2.DEMANDE AS d JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS " +
                          "WHERE s.NumeroGestion = @v AND d.DMND_ID_AFFRE IS NOT NULL",
                          "SELECT TOP (1) CONCAT(N'Dossier ', s.NumeroGestion, CASE WHEN e.Denomination IS NULL THEN N'' ELSE N' — ' + e.Denomination END) " +
                          "FROM rec.DossierRcs AS s LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise WHERE s.NumeroGestion = @v ORDER BY s.Id"),
            "client" => ("SELECT a.AFFRE_ID_AFFRE AS DMND_ID_AFFRE FROM v2.AFFAIRE AS a JOIN rec.Client AS c ON c.Id = a.AFFRE_ID_CLIEN WHERE c.Abrege = @v",
                         "SELECT TOP (1) CONCAT(N'Client ', c.Abrege, N' — ', c.Nom, CASE WHEN c.Prenom IS NULL THEN N'' ELSE N' ' + c.Prenom END) " +
                         "FROM rec.Client AS c WHERE c.Abrege = @v ORDER BY c.Id"),
            "arrivee" => ("SELECT DISTINCT d.DMND_ID_AFFRE FROM v2.DEMANDE AS d JOIN v2.ARRIVEE AS r ON r.ARRV_ID_ARRV = d.DMND_ID_ARRV " +
                          "WHERE r.ARRV_NUM_ARRIVEE = @v AND d.DMND_ID_AFFRE IS NOT NULL",
                          "SELECT TOP (1) CONCAT(N'Arrivée ', r.ARRV_NUM_ARRIVEE, N' du ', CONVERT(nvarchar(10), r.ARRV_DATEHEURE, 103)) FROM v2.ARRIVEE AS r " +
                          "WHERE r.ARRV_NUM_ARRIVEE = @v ORDER BY r.ARRV_ID_ARRV"),
            _ => throw new ArgumentException($"Critere « {critere} » inconnu : siren, gestion, client ou arrivee.", nameof(critere)),
        };

        var ids = new List<int>();
        await using (var cmd = new SqlCommand($"SELECT TOP ({AffairesAuPlus + 1}) x.DMND_ID_AFFRE FROM ({selection}) AS x ORDER BY 1 DESC;", v3))
        {
            cmd.Parameters.Add(new SqlParameter("@v", SqlDbType.NVarChar, 200) { Value = valeur });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) ids.Add(l.GetInt32(0));
        }
        string? leTitre;
        await using (var cmd = new SqlCommand(titre, v3))
        {
            cmd.Parameters.Add(new SqlParameter("@v", SqlDbType.NVarChar, 200) { Value = valeur });
            leTitre = await cmd.ExecuteScalarAsync(ct) as string;
        }
        var note = ids.Count > AffairesAuPlus ? $"Les {AffairesAuPlus} affaires les plus récentes sont affichées." : null;
        if (ids.Count > AffairesAuPlus) ids.RemoveAt(ids.Count - 1);
        var affaires = await AffairesAsync(v3, baseV2, codeGreffe, ids, connexionEchanges, ct);
        if (affaires.Length == 0 && leTitre is null) note = "Aucune fiche ne correspond : vérifiez le numéro.";
        return new VueHistorique(critere, valeur, leTitre, affaires, affaires.Count(a => a.Logiciel == Origines.V2), affaires.Count(a => a.Logiciel == Origines.V3), note);
    }

    /// <summary>Des affaires par leur ligne de la V3, avec leurs demandes, liens, et echanges (les plus recentes d'abord).</summary>
    internal static async Task<AffaireHistorique[]> AffairesAsync(SqlConnection v3, string baseV2, string codeGreffe, IReadOnlyList<int> ids,
                                                                  string? connexionEchanges, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var liste = string.Join(",", ids);
        var entetes = new List<(int Id, string? IdV2, string? Libelle, DateTime? Le, string? Client, string? Code)>();
        await using (var cmd = new SqlCommand($"""
            SELECT a.AFFRE_ID_AFFRE, {Origine("AFFAIRE", "a.AFFRE_ID_AFFRE")}, a.AFFRE_LIBELLE, a.AFFRE_DATE_CREATION,
                   CASE WHEN c.Id IS NULL THEN NULL ELSE CONCAT(c.Nom, CASE WHEN c.Prenom IS NULL THEN N'' ELSE N' ' + c.Prenom END) END, c.Abrege
            FROM v2.AFFAIRE AS a LEFT JOIN rec.Client AS c ON c.Id = a.AFFRE_ID_CLIEN
            WHERE a.AFFRE_ID_AFFRE IN ({liste}) ORDER BY a.AFFRE_ID_AFFRE DESC;
            """, v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
                entetes.Add((l.GetInt32(0), l.IsDBNull(1) ? null : l.GetString(1), l.IsDBNull(2) ? null : l.GetValue(2).ToString(),
                             l.IsDBNull(3) ? null : l.GetDateTime(3), l.IsDBNull(4) ? null : l.GetString(4), l.IsDBNull(5) ? null : l.GetValue(5).ToString()));
        }
        var demandes = await DemandesAsync(v3, baseV2, $"d.DMND_ID_AFFRE IN ({liste})", null, ct);
        var dossiers = new Dictionary<int, List<string>>();
        await using (var cmd = new SqlCommand($"SELECT DISTINCT d.DMND_ID_AFFRE, s.NumeroGestion FROM v2.DEMANDE AS d JOIN rec.DossierRcs AS s ON s.Id = d.DMND_ID_DSRCS " +
                                              $"WHERE d.DMND_ID_AFFRE IN ({liste}) AND s.NumeroGestion IS NOT NULL;", v3))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct))
            {
                if (!dossiers.TryGetValue(l.GetInt32(0), out var d)) dossiers[l.GetInt32(0)] = d = [];
                d.Add(l.GetValue(1).ToString()!);
            }
        var (echanges, note) = await EchangesAsync(connexionEchanges, codeGreffe, demandes.SelectMany(d => d.Value).Where(d => d.IdV2 is not null).ToList(), ct);
        return [.. entetes.Select(a =>
        {
            var ds = demandes.GetValueOrDefault(a.Id) ?? [];
            var ancienne = a.IdV2 is not null;
            var parIdV2 = ds.Where(d => d.IdV2 is not null).ToDictionary(d => d.IdV2!, d => d.Numero);
            return new AffaireHistorique(a.Id, a.IdV2, ancienne ? Origines.V2 : Origines.V3, ancienne ? Origines.MarqueAncienne : Origines.MarqueNouvelle,
                ds.Any(d => !GardeAffaires.EtatsFinis.Contains(d.Etat)) ? "en cours" : "terminée", a.Libelle, a.Le, a.Client, a.Code,
                [.. (dossiers.GetValueOrDefault(a.Id) ?? []).Order(StringComparer.Ordinal)], [.. ds],
                [.. echanges.Where(e => e.Demande is not null && parIdV2.ContainsKey(e.Demande)).Select(e => e with { Demande = parIdV2[e.Demande!] ?? e.Demande })
                            .OrderByDescending(e => e.Le)],
                ancienne || ds.Any(d => d.IdV2 is not null) ? note : null);
        })];
    }

    /// <summary>Des demandes (filtre sur l'alias « d » de v2.DEMANDE), par affaire, avec leurs liens.</summary>
    internal static async Task<Dictionary<int, List<DemandeHistorique>>> DemandesAsync(SqlConnection v3, string baseV2, string filtre,
                                                                                       (string Nom, object Valeur)? parametre, CancellationToken ct)
    {
        var lues = new List<(int Affaire, DemandeHistorique D)>();
        await using (var cmd = new SqlCommand($"""
            SELECT ISNULL(d.DMND_ID_AFFRE, 0), d.DMND_ID_DMND, o.IdV2, d.DMND_NUM_DEMANDE, ISNULL(d.DMND_LIBELLE_ECRAN, d.DMND_LIBELLE), d.DMND_ETAT_DEMANDE,
                   d.DMND_DATE_CREATION, d.DMND_DATE_VALIDATION, ISNULL(t.LibelleEcran, t.Libelle), r.ARRV_NUM_ARRIVEE, d.DMND_INHIBEE, d.DMND_TEXTE_INHIBITEUR
            FROM v2.DEMANDE AS d
            OUTER APPLY (SELECT {Origine("DEMANDE", "d.DMND_ID_DMND")} AS IdV2) AS o
            -- Le type : un numero de CATEGORIE pour une ancienne demande (copiee telle quelle), l'identifiant de rec.TypeDemande
            -- pour une demande de la V3 (RegleRecopie.NumerosV2).
            OUTER APPLY (SELECT TOP (1) ty.Libelle, ty.LibelleEcran FROM rec.TypeDemande AS ty
                         WHERE (o.IdV2 IS NULL AND ty.Id = d.DMND_ID_CTGR)
                            OR (o.IdV2 IS NOT NULL AND ty.Id = (SELECT TOP (1) TRY_CONVERT(int, k.IdV3) FROM rec.Correspondance AS k
                                                                WHERE k.BaseV2 = @b AND k.TableV2 = N'CATEGORIE' AND k.IdV2 = CONVERT(nvarchar(200), d.DMND_ID_CTGR)))) AS t
            LEFT JOIN v2.ARRIVEE AS r ON r.ARRV_ID_ARRV = d.DMND_ID_ARRV
            WHERE {filtre}
            ORDER BY d.DMND_ID_DMND;
            """, v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            if (parametre is { } p) cmd.Parameters.AddWithValue(p.Nom, p.Valeur);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct))
            {
                var idV2 = l.IsDBNull(2) ? null : l.GetString(2);
                var etat = l.IsDBNull(5) ? "?" : l.GetString(5);
                lues.Add((l.GetInt32(0), new DemandeHistorique(l.GetInt32(1), idV2, idV2 is null ? Origines.V3 : Origines.V2,
                    l.IsDBNull(3) ? null : l.GetString(3), l.IsDBNull(4) ? null : l.GetString(4), etat, EtatLibelle(etat),
                    l.IsDBNull(6) ? null : l.GetDateTime(6), l.IsDBNull(7) ? null : l.GetDateTime(7), l.IsDBNull(8) ? null : l.GetString(8),
                    l.IsDBNull(9) ? null : l.GetString(9), !l.IsDBNull(10) && l.GetBoolean(10), l.IsDBNull(11) ? null : l.GetString(11), [])));
            }
        }
        var liens = await LiensAsync(v3, baseV2, lues.Select(x => x.D.Id).ToList(), ct);
        var r = new Dictionary<int, List<DemandeHistorique>>();
        foreach (var (affaire, d) in lues)
        {
            if (!r.TryGetValue(affaire, out var l)) r[affaire] = l = [];
            l.Add(d with { Liens = [.. liens.GetValueOrDefault(d.Id) ?? []] });
        }
        return r;
    }

    /// <summary>
    /// Les liens de demandes (REC-011) : ceux que la demande porte dans ses colonnes (DMND_ID_DMND_INHIBITRICE, DMND_ID_DMND_MERE
    /// vers une ligne de la V3, ancienne ou non), ceux qu'on lui porte, et ceux de rec.LienDemande (le blocage pose puis leve
    /// par la regle de la V2, CA02 ; le lien qu'une ancienne demande porte vers une demande de la V3, CA04).
    /// </summary>
    internal static async Task<Dictionary<int, List<LienDemandeVue>>> LiensAsync(SqlConnection v3, string baseV2, IReadOnlyList<int> ids, CancellationToken ct)
    {
        var r = new Dictionary<int, List<LienDemandeVue>>();
        if (ids.Count == 0) return r;
        foreach (var paquet in ids.Chunk(1000))
        {
            var liste = string.Join(",", paquet);
            await using var cmd = new SqlCommand($"""
                WITH l (Demande, Lien, Autre, Leve, LeveLe) AS (
                    SELECT d.DMND_ID_DMND, N'bloquée par', d.DMND_ID_DMND_INHIBITRICE, 0, NULL FROM v2.DEMANDE AS d
                    WHERE d.DMND_ID_DMND IN ({liste}) AND d.DMND_ID_DMND_INHIBITRICE IS NOT NULL
                    UNION ALL
                    SELECT d.DMND_ID_DMND, N'née de', d.DMND_ID_DMND_MERE, 0, NULL FROM v2.DEMANDE AS d
                    WHERE d.DMND_ID_DMND IN ({liste}) AND d.DMND_ID_DMND_MERE IS NOT NULL
                    UNION ALL
                    SELECT TOP (2000) f.DMND_ID_DMND_INHIBITRICE, N'bloque', f.DMND_ID_DMND, 0, NULL FROM v2.DEMANDE AS f
                    WHERE f.DMND_ID_DMND_INHIBITRICE IN ({liste})
                    UNION ALL
                    SELECT TOP (2000) f.DMND_ID_DMND_MERE, N'a engendré', f.DMND_ID_DMND, 0, NULL FROM v2.DEMANDE AS f
                    WHERE f.DMND_ID_DMND_MERE IN ({liste})
                    UNION ALL
                    -- Un blocage pose par la regle de la V2 puis leve : la colonne est videe, la trace reste.
                    SELECT k.IdDemande, N'bloquée par', k.IdVisee, 1, k.LeveLe FROM rec.LienDemande AS k
                    WHERE k.Porteuse = 'V3' AND k.LeveLe IS NOT NULL AND k.IdDemande IN ({liste})
                    UNION ALL
                    SELECT k.IdVisee, N'a été bloquée par', k.IdDemande, 1, k.LeveLe FROM rec.LienDemande AS k
                    WHERE k.Porteuse = 'V3' AND k.LeveLe IS NOT NULL AND k.IdVisee IN ({liste})
                    UNION ALL
                    -- Le lien qu'une ancienne demande porte vers une demande de la V3, et son pendant.
                    SELECT k.IdDemande, CASE k.Lien WHEN 'inhibitrice' THEN N'bloquée par' ELSE N'née de' END, k.IdVisee, 0, NULL FROM rec.LienDemande AS k
                    WHERE k.Porteuse = 'V2' AND k.IdDemande IN ({liste})
                    UNION ALL
                    SELECT k.IdVisee, CASE k.Lien WHEN 'inhibitrice' THEN N'bloque' ELSE N'a engendré' END, k.IdDemande, 0, NULL FROM rec.LienDemande AS k
                    WHERE k.Porteuse = 'V2' AND k.IdVisee IN ({liste})
                )
                SELECT l.Demande, l.Lien, l.Autre, a.DMND_NUM_DEMANDE, CASE WHEN {Origine("DEMANDE", "l.Autre")} IS NULL THEN 0 ELSE 1 END,
                       a.DMND_ETAT_DEMANDE, l.Leve, l.LeveLe
                FROM l LEFT JOIN v2.DEMANDE AS a ON a.DMND_ID_DMND = l.Autre
                ORDER BY l.Demande, l.Lien, l.Autre;
                """, v3);
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var lec = await cmd.ExecuteReaderAsync(ct);
            while (await lec.ReadAsync(ct))
            {
                if (!r.TryGetValue(lec.GetInt32(0), out var liens)) r[lec.GetInt32(0)] = liens = [];
                var etat = lec.IsDBNull(5) ? "?" : lec.GetString(5);
                liens.Add(new LienDemandeVue(lec.GetString(1), lec.GetInt32(2), lec.IsDBNull(3) ? null : lec.GetString(3),
                    lec.GetInt32(4) == 1 ? Origines.V2 : Origines.V3, etat, EtatLibelle(etat), lec.GetInt32(6) == 1, lec.IsDBNull(7) ? null : lec.GetDateTime(7)));
            }
        }
        return r;
    }

    /// <summary>
    /// Pose le lien qu'une ANCIENNE demande porte vers une demande de la V3 (REC-011 T01, CA04) : elle est bloquee par elle
    /// (« inhibitrice ») ou nee d'elle (« mere »). La ligne de l'ancienne demande est celle de la V2 (la remontee la reecrit) :
    /// le lien est garde dans rec.LienDemande et ecrit dans la V2 au retour (RetourBeta), quand la demande de la V3 y a une
    /// ligne. Le sens inverse (une demande de la V3 qui vise une ancienne) n'a pas besoin de cette table : ses colonnes
    /// DMND_ID_DMND_MERE et DMND_ID_DMND_INHIBITRICE visent la ligne de l'ancienne dans la V3.
    /// </summary>
    public static async Task<int> LierAncienneAsync(SqlConnection v3, SqlTransaction? tx, string baseV2, int idAncienne, string lien, int idDemandeV3,
                                                    string par, CancellationToken ct)
    {
        if (lien is not ("inhibitrice" or "mere")) throw new ArgumentException("Lien « inhibitrice » ou « mere ».", nameof(lien));
        async Task<bool> Ancienne(int id)
        {
            await using var cmd = new SqlCommand($"SELECT CASE WHEN {Origine("DEMANDE", "@id")} IS NULL THEN 0 ELSE 1 END;", v3, tx);
            cmd.Parameters.AddWithValue("@b", baseV2);
            cmd.Parameters.AddWithValue("@id", id);
            return (int)(await cmd.ExecuteScalarAsync(ct))! == 1;
        }
        if (!await Ancienne(idAncienne)) throw new InvalidOperationException($"La demande {idAncienne} n'est pas une demande de l'ancien RIG.");
        if (await Ancienne(idDemandeV3)) throw new InvalidOperationException($"La demande {idDemandeV3} n'est pas une demande de la V3.");
        await using var ins = new SqlCommand("INSERT INTO rec.LienDemande (Porteuse, IdDemande, Lien, IdVisee, PosePar) OUTPUT inserted.Id VALUES ('V2', @a, @l, @v, @p);", v3, tx);
        ins.Parameters.AddWithValue("@a", idAncienne);
        ins.Parameters.AddWithValue("@l", lien);
        ins.Parameters.AddWithValue("@v", idDemandeV3);
        ins.Parameters.AddWithValue("@p", par);
        return (int)(await ins.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Une demande par sa ligne de la V3, ses liens compris (REC-011 CA03 : le lien vers la demande d'origine s'ouvre
    /// en lecture) ; null si elle n'existe pas.</summary>
    public static async Task<DemandeHistorique?> DemandeAsync(SqlConnection v3, string baseV2, int id, CancellationToken ct)
        => (await DemandesAsync(v3, baseV2, "d.DMND_ID_DMND = @id", ("@id", id), ct)).Values.SelectMany(d => d).FirstOrDefault();

    /// <summary>Une arrivee par son numero et ses demandes des deux logiciels, celles de l'ancien RIG marquees (REC-018 CA03) ;
    /// null si le numero n'existe pas dans la V3.</summary>
    public static async Task<ArriveeDeuxLogiciels?> ArriveeAsync(SqlConnection v3, string baseV2, string numero, CancellationToken ct)
    {
        int id; string? idV2, num, nom, refs; string mode; DateTime? le;
        await using (var cmd = new SqlCommand($"""
            SELECT TOP (1) a.ARRV_ID_ARRV, (SELECT TOP (1) c.IdV2 FROM rec.Correspondance AS c WHERE c.TableV3 = N'v2.ARRIVEE'
                                           AND c.IdV3 = CONVERT(nvarchar(200), a.ARRV_ID_ARRV) AND c.BaseV2 = @b AND c.TableV2 = N'ARRIVEE'),
                   a.ARRV_NUM_ARRIVEE, a.ARRV_MODE_ARRIVEE, a.ARRV_DATEHEURE, a.ARRV_NOM, a.ARRV_REFERENCES
            FROM v2.ARRIVEE AS a WHERE a.ARRV_NUM_ARRIVEE = @n ORDER BY a.ARRV_ID_ARRV DESC;
            """, v3))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.VarChar, 11) { Value = numero.Trim() });
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (!await l.ReadAsync(ct)) return null;
            id = l.GetInt32(0); idV2 = l.IsDBNull(1) ? null : l.GetString(1); num = l.IsDBNull(2) ? null : l.GetString(2);
            mode = l.GetString(3); le = l.IsDBNull(4) ? null : l.GetDateTime(4); nom = l.IsDBNull(5) ? null : l.GetString(5);
            refs = l.IsDBNull(6) ? null : l.GetString(6);
        }
        var demandes = (await DemandesAsync(v3, baseV2, "d.DMND_ID_ARRV = @a", ("@a", id), ct)).Values.SelectMany(d => d).OrderBy(d => d.Id).ToArray();
        return new ArriveeDeuxLogiciels(id, idV2, num, mode, le, nom, refs, demandes, demandes.Count(d => d.Logiciel == Origines.V2));
    }

    /// <summary>
    /// Les envois et receptions de la base des echanges (EDI_GREFFE) de ces demandes : FLUX_SORTANT / FLUX_ENTRANT par code greffe
    /// et identifiant V2 (FLUSOR_RIG_ID, FLUENT_RIG_ID). Lecture seule (LectureV2). Seuls les flux dont l'identifiant RIG est
    /// celui d'une demande sont relies : c'est le cas courant des receptions (DMND_ID_DMND, 14 affectations dans Source\Edi) ;
    /// les autres (dossier, tables de dematerialisation) le seront par la story Echanges de leur flux.
    /// </summary>
    internal static async Task<(List<EchangeHistorique> Echanges, string? Note)> EchangesAsync(string? connexionEchanges, string codeGreffe,
                                                                                            IReadOnlyList<DemandeHistorique> demandes, CancellationToken ct)
    {
        if (demandes.Count == 0) return ([], null);
        if (string.IsNullOrWhiteSpace(connexionEchanges))
            return ([], "Échanges non listés : la base des échanges n'est pas réglée sur ce serveur (Recopie:BaseEchanges).");
        var ids = string.Join(",", demandes.Select(d => int.Parse(d.IdV2!, System.Globalization.CultureInfo.InvariantCulture)).Distinct());
        var r = new List<EchangeHistorique>();
        try
        {
            await using var cn = new SqlConnection(new SqlConnectionStringBuilder(connexionEchanges) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString);
            await cn.OpenAsync(ct);
            await using var l = await LectureV2.LireAsync(cn,
                "SELECT N'envoi', FLUSOR_ID_FLUSOR, FLUSOR_CODE_EDI, FLUSOR_ETAT, FLUSOR_DATE_CREATION, FLUSOR_REFERENCE, FLUSOR_ERREUR, FLUSOR_RIG_ID " +
                $"FROM dbo.FLUX_SORTANT WHERE FLUSOR_CODE_GREFFE = @g AND FLUSOR_RIG_ID IN ({ids}) " +
                "UNION ALL SELECT N'réception', FLUENT_ID_FLUENT, FLUENT_CODE_EDI, FLUENT_ETAT, FLUENT_DATE_CREATION, FLUENT_REFERENCE, FLUENT_ERREUR, FLUENT_RIG_ID " +
                $"FROM dbo.FLUX_ENTRANT WHERE FLUENT_CODE_GREFFE = @g AND FLUENT_RIG_ID IN ({ids})", ct,
                new SqlParameter("@g", SqlDbType.Char, 4) { Value = codeGreffe });
            while (await l.ReadAsync(ct))
                r.Add(new EchangeHistorique(l.GetString(0), l.GetInt32(1), l.GetString(2), l.GetInt32(3), l.GetDateTime(4),
                                            l.IsDBNull(5) ? null : l.GetString(5), l.IsDBNull(6) ? null : l.GetString(6),
                                            l.IsDBNull(7) ? null : l.GetInt32(7).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (SqlException e)
        {
            return ([], $"Échanges non listés : la base des échanges ne répond pas ({e.Number}).");
        }
        return (r, null);
    }
}
