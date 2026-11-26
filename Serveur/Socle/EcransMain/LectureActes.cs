using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Actes » (web/src/ecrans/EcranActes.tsx) : les actes deposes non encore ranges dans la GED, lus dans la base V3 du greffe.
/// LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_ACTES_NUM, mode Acte (Processus\PROC_ACTES_NUM\PROC_ACTES_NUM\RESULTAT\OPE_RESULTAT.cs ; fiche
/// proc_actes_num.md:360-412). La requete (_SqlBase, OPE_RESULTAT.cs:393-431) : DEPOT_RCS JOIN CATEGORIE_DEPOT hors comptes
/// annuels (ISNULL(R_CATEGORIED_COMPTES_ANNUEL, 0) = 0) JOIN ACTE_DEPOSE JOIN ACTION_RCS LEFT JOIN DOSSIER_RCS / INSCRIT_PM /
/// INSCRIT_PP JOIN ABREGE_PIECE, WHERE DPTRC_DATE BETWEEN '2014-01-01' AND aujourd'hui - 2 jours AND ACTDP_GUID_VIDIGED IS NULL
/// (pas encore de document dans la GED). Colonnes : numero de gestion, designation (denomination, sinon patronyme et prenom),
/// numero et date de depot, categorie, abrege (R_ABREGEPIEC_SYNONYME), description (ACTDP_DESCRIPTION, sinon le developpe de
/// l'abrege), date de l'acte, « demat » (une demande de l'action porte un flux DEMAT_DAS, DEMAT_DCA ou DEMAT_LIASSE_CFE).
/// Criteres (ChargerDonnees, :450-541) :
///   - la periode de depot (CONVERT(date, DPTRC_DATE)), bornes incluses ;
///   - le numero : la V2 le classe (Numero.GetTypeNumero) puis cherche « LIKE %n% » dans DSSRC_NUM_GESTION OU dans
///     DPTRC_NUM_DEPOT ; RIG v3 cherche dans les deux colonnes. Un numero saisi leve le plancher de 2014 a 1954 (:500-501) ;
///   - ACTDP_SANS_DOCUMENT = 0, ou = 1 case « Lister actes non numerisables » cochee (:504-508) : la case REMPLACE la liste ;
///   - les abreges coches : R_ABREGEPIEC_SYNONYME IN (...) (:526-539).
/// AJOUT DE LA V3, nomme : la categorie de depot choisie filtre DPTRC_CATEGORIE_DEPOT (colonne « catego » de la requete V2,
/// :401) ; la V2 n'a pas ce critere (son « Type de depot » choisit le mode Acte ou Bilan).
/// NON LU, nomme : le mode Bilan (vue serveur ViewDcaEnAnomalie, :342-391), ses anomalies et sa confidentialite -- la vue n'est
/// pas dans la base V3. ORDRE : la V2 n'en pose pas en mode Acte ; RIG v3 range par date de depot, depot, ordre de l'acte.
/// </summary>
public static class LectureActes
{
    /// <summary>Le nombre maximal d'actes montres (le « Nombre maxi. » de l'ecran est ramene ici).</summary>
    public const int PlafondMax = 500;

    private const string Requete = """
        SELECT TOP (@plafond) ad.ACTDP_ID_ACDPS, s.NumeroGestion, p.DPTRC_NUM_DEPOT,
               COALESCE(NULLIF(LTRIM(RTRIM(e.Denomination)), ''), LTRIM(RTRIM(CONCAT(e.Patronyme, ' ', e.Prenom)))) AS Designation,
               p.DPTRC_CATEGORIE_DEPOT, cd.R_CATEGORIED_LIBELLE_ECRAN, ap.R_ABREGEPIEC_SYNONYME,
               CASE WHEN ISNULL(ad.ACTDP_DESCRIPTION, '') <> '' THEN ad.ACTDP_DESCRIPTION ELSE ap.R_ABREGEPIEC_DEVELOPPE_EDIT END AS Description,
               p.DPTRC_DATE, ad.ACTDP_DATE, ad.ACTDP_SANS_DOCUMENT,
               CASE WHEN EXISTS (SELECT 1 FROM v2.DEMANDE AS d WHERE d.DMND_ID_ACTRC = a.Id
                                   AND (EXISTS (SELECT 1 FROM v2.DEMAT_DAS AS x WHERE x.DMTDAS_ID_DMND = d.DMND_ID_DMND)
                                        OR EXISTS (SELECT 1 FROM v2.DEMAT_DCA AS x WHERE x.DMTDCA_ID_DMND = d.DMND_ID_DMND)
                                        OR EXISTS (SELECT 1 FROM v2.DEMAT_LIASSE_CFE AS x WHERE x.DMTLCF_ID_DMND = d.DMND_ID_DMND)))
                    THEN 1 ELSE 0 END AS Demat,
               COUNT(*) OVER () AS Total
        FROM v2.DEPOT_RCS AS p
        JOIN v2.CATEGORIE_DEPOT AS cd ON cd.R_CATEGORIED_VALEUR = p.DPTRC_CATEGORIE_DEPOT AND ISNULL(cd.R_CATEGORIED_COMPTES_ANNUEL, 0) = 0
        JOIN v2.ACTE_DEPOSE AS ad ON ad.ACTDP_ID_DPTRC = p.DPTRC_ID_DPTRC
        JOIN rec.ActionRegistre AS a ON a.IdDepot = p.DPTRC_ID_DPTRC
        LEFT JOIN rec.DossierRcs AS s ON s.Id = a.IdDossier
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        JOIN v2.ABREGE_PIECE AS ap ON ap.R_ABREGEPIEC_VALEUR = ad.ACTDP_ABREGE_PIECE
        WHERE p.DPTRC_DATE BETWEEN @plancher AND @borne AND ad.ACTDP_GUID_VIDIGED IS NULL
          AND (@du IS NULL OR CONVERT(date, p.DPTRC_DATE) >= @du) AND (@au IS NULL OR CONVERT(date, p.DPTRC_DATE) <= @au)
          AND (@numero = '' OR s.NumeroGestion LIKE '%' + @numero + '%' OR p.DPTRC_NUM_DEPOT LIKE '%' + @numero + '%')
          AND ad.ACTDP_SANS_DOCUMENT = @sansDocument
          AND (@categorie = '' OR p.DPTRC_CATEGORIE_DEPOT = @categorie)
          {abreges}
        ORDER BY p.DPTRC_DATE, p.DPTRC_NUM_DEPOT, ad.ACTDP_NUM_ORDRE, ad.ACTDP_ID_ACDPS
        """;

    private const string Categories = """
        SELECT cd.R_CATEGORIED_VALEUR, cd.R_CATEGORIED_LIBELLE_ECRAN FROM v2.CATEGORIE_DEPOT AS cd
        WHERE ISNULL(cd.R_CATEGORIED_COMPTES_ANNUEL, 0) = 0
        ORDER BY cd.R_CATEGORIED_LIBELLE_ECRAN, cd.R_CATEGORIED_VALEUR
        """;

    /// <summary>Les actes de <paramref name="recherche"/> au jour <paramref name="aujourdhui"/> (la borne « - 2 jours »).</summary>
    public static async Task<ActesLus> LireAsync(SqlConnection baseGreffe, RechercheActes recherche, DateTime aujourdhui, CancellationToken ct)
    {
        var numero = (recherche.Numero ?? "").Trim();
        var abreges = (recherche.Abreges ?? []).Select(a => a.Trim()).Where(a => a.Length > 0).Distinct().ToArray();
        var parametres = new List<SqlParameter>
        {
            new("@plafond", SqlDbType.Int) { Value = Math.Clamp(recherche.Plafond, 1, PlafondMax) },
            // « si le numero de depot est renseigne, on ne se limite pas a 2014 » (OPE_RESULTAT.cs:500-501).
            new("@plancher", SqlDbType.DateTime) { Value = numero.Length > 0 ? new DateTime(1954, 1, 1) : new DateTime(2014, 1, 1) },
            new("@borne", SqlDbType.DateTime) { Value = aujourdhui.Date.AddDays(-2) },
            new("@du", SqlDbType.Date) { Value = (object?)recherche.Du?.Date ?? DBNull.Value },
            new("@au", SqlDbType.Date) { Value = (object?)recherche.Au?.Date ?? DBNull.Value },
            new("@numero", SqlDbType.VarChar, 40) { Value = numero },
            new("@sansDocument", SqlDbType.Bit) { Value = recherche.SansDocument },
            new("@categorie", SqlDbType.VarChar, 16) { Value = (recherche.Categorie ?? "").Trim() },
        };
        parametres.AddRange(abreges.Select((a, i) => new SqlParameter($"@abrege{i}", SqlDbType.VarChar, 16) { Value = a }));
        var sql = Requete.Replace("{abreges}", abreges.Length == 0 ? ""
            : $"AND ap.R_ABREGEPIEC_SYNONYME IN ({string.Join(", ", abreges.Select((_, i) => $"@abrege{i}"))})");

        var lignes = new List<ActeLu>();
        var total = 0;
        await using (var lu = await LectureV2.LireAsync(baseGreffe, sql, ct, [.. parametres]))
        {
            while (await lu.ReadAsync(ct))
            {
                total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
                lignes.Add(new ActeLu(Convert.ToInt32(lu["ACTDP_ID_ACDPS"], CultureInfo.InvariantCulture), Chaine(lu, "NumeroGestion"),
                    Chaine(lu, "DPTRC_NUM_DEPOT"), Chaine(lu, "Designation"), Chaine(lu, "DPTRC_CATEGORIE_DEPOT"), Chaine(lu, "R_CATEGORIED_LIBELLE_ECRAN"),
                    Chaine(lu, "R_ABREGEPIEC_SYNONYME"), Chaine(lu, "Description"), lu["DPTRC_DATE"] as DateTime?, lu["ACTDP_DATE"] as DateTime?,
                    Convert.ToInt32(lu["Demat"], CultureInfo.InvariantCulture) == 1, lu["ACTDP_SANS_DOCUMENT"] is true));
            }
        }
        var categories = new List<CategorieDepotLue>();
        await using (var lu = await LectureV2.LireAsync(baseGreffe, Categories, ct))
            while (await lu.ReadAsync(ct))
                categories.Add(new CategorieDepotLue(Chaine(lu, "R_CATEGORIED_VALEUR"), Chaine(lu, "R_CATEGORIED_LIBELLE_ECRAN")));
        return new ActesLus([.. lignes], total, [.. categories]);
    }
}
