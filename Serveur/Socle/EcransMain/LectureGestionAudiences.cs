using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Gestion des audiences » (web/src/ecrans/EcranGestionAudiences.tsx) : les audiences et cabinets SUPPRIMES, lus dans la base V3
/// du greffe. LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_GESTAUDS, Opérations\OPE_RECHERCHE_GESTAUDS.cs -- ses deux requetes lisent AUDIENCE_CABINET_SUPPRIMEE :
///   - les CABINETS (ChargementSQLCabinet, :279-323) : AUDNCS_AUDIENCE = 0, le code cabinet « code - libelle » (CODE_CABINET), le
///     juge et le greffier « abrege - civilite nom prenom » (JUGE, GREFFIER_ASSERMENTE ; vides sans nom) ;
///   - les AUDIENCES (ChargementSQLAudience, :325-390) : AUDNCS_AUDIENCE = 1, la chambre « code - libelle » (CHAMBRE), la section ;
///   - pour toutes : date, heure, type (AUDNCS_TYPE_AUD_CAB), types de classement admis, publique, auteur et date de la suppression,
///     date de diffusion (« Envoi suppression Infogreffe »), non disponible sur le web ;
///   - ORDRE : date, heure (strQueryOrder, :170).
/// La V3 lit les deux modes d'un coup (l'ecran bascule de l'un a l'autre) ; les criteres de RIG v2 (dates, type, chambre, section,
/// juge, code cabinet, type de classement, nombre maximum) filtrent a l'ecran. NON LUS : les compteurs d'appels et de lectures
/// (NOMBRE_APPELS / NOMBRE_LECTURES, :288-289, que la liste n'affiche pas) et l'infobulle de la composition (:394-410).
/// </summary>
public static class LectureGestionAudiences
{
    /// <summary>Le nombre de lignes montrees.</summary>
    public const int Plafond = 500;

    private const string Requete = """
        SELECT TOP (@plafond) CONVERT(bit, CASE WHEN s.AUDNCS_AUDIENCE = 0 THEN 1 ELSE 0 END) AS Cabinet, s.AUDNCS_DATE, s.AUDNCS_HEURE,
               s.AUDNCS_TYPE_AUD_CAB,
               CASE WHEN ISNULL(s.AUDNCS_CODE_CABINET, '') = '' THEN '' ELSE s.AUDNCS_CODE_CABINET + ' - ' + ISNULL(cc.R_CODECABINE_LIBELLE_ECRAN, '') END AS CodeCabinet,
               CASE WHEN ISNULL(j.JUG_NOM, '') = '' THEN ''
                    ELSE ISNULL(j.JUG_ABREGE, '') + ' - ' + ISNULL(j.JUG_CIVILITE, '') + ' ' + j.JUG_NOM + ' ' + ISNULL(j.JUG_PRENOM, '') END AS Juge,
               CASE WHEN ISNULL(g.GRASS_NOM, '') = '' THEN ''
                    ELSE ISNULL(g.GRASS_ABREGE, '') + ' - ' + ISNULL(g.GRASS_CIVILITE, '') + ' ' + g.GRASS_NOM + ' ' + ISNULL(g.GRASS_PRENOM, '') END AS Greffier,
               CASE WHEN ISNULL(s.AUDNCS_CHAMBRE, '') = '' THEN '' ELSE s.AUDNCS_CHAMBRE + ' - ' + ISNULL(ch.R_CHAMBRE_LIBELLE_ECRAN, '') END AS Chambre,
               s.AUDNCS_SECTION, s.AUDNCS_TYPE_CLASSEMENT_ADMIS, CONVERT(bit, ISNULL(s.AUDNCS_PUBLIQUE, 0)) AS Publique, s.AUDNCS_AUTEUR_SUPPRESSION,
               s.AUDNCS_DATE_SUPPRESSION, s.AUDNCS_DATE_DIFFUSION, CONVERT(bit, ISNULL(s.AUDNCS_NON_DISPONIBLE_WEB, 0)) AS NonDisponibleWeb,
               COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET_SUPPRIMEE AS s
        LEFT JOIN v2.CODE_CABINET AS cc ON cc.R_CODECABINE_VALEUR = s.AUDNCS_CODE_CABINET
        LEFT JOIN v2.JUGE AS j ON j.JUG_ID_JUG = s.AUDNCS_ID_JUG
        LEFT JOIN v2.GREFFIER_ASSERMENTE AS g ON g.GRASS_ID_GRASS = s.AUDNCS_ID_GRASS
        LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = s.AUDNCS_CHAMBRE
        ORDER BY s.AUDNCS_DATE, s.AUDNCS_HEURE, s.AUDNCS_ID_AUDNCS
        """;

    /// <summary>Les audiences et cabinets supprimes, au plus <paramref name="plafond"/>.</summary>
    public static async Task<AudiencesSupprimeesLues> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<AudienceSupprimeeLue>();
        var total = 0;
        await using var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond });
        while (await lu.ReadAsync(ct))
        {
            total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
            lignes.Add(new AudienceSupprimeeLue(lu["Cabinet"] is true, lu["AUDNCS_DATE"] as DateTime?, Chaine(lu, "AUDNCS_HEURE"),
                Chaine(lu, "AUDNCS_TYPE_AUD_CAB"), Chaine(lu, "CodeCabinet"), Chaine(lu, "Juge"), Chaine(lu, "Greffier"), Chaine(lu, "Chambre"),
                Chaine(lu, "AUDNCS_SECTION"), Chaine(lu, "AUDNCS_TYPE_CLASSEMENT_ADMIS"), lu["Publique"] is true, Chaine(lu, "AUDNCS_AUTEUR_SUPPRESSION"),
                lu["AUDNCS_DATE_SUPPRESSION"] as DateTime?, lu["AUDNCS_DATE_DIFFUSION"] as DateTime?, lu["NonDisponibleWeb"] is true));
        }
        return new AudiencesSupprimeesLues([.. lignes], total);
    }
}
