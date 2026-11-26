using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using static RigV3.Serveur.Socle.EcransMain.LectureDemandes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Créer une audience » (web/src/ecrans/EcranAudiences.tsx) : les listes de saisie de la creation et les audiences a venir, lues
/// dans la base V3 du greffe. LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026 (marche du lot 17).
///
/// SOURCE (ancien RIG) : PROC_AUDIENCE, Etapes\OPE_CREATION_AUDIENCE.cs -- ses listes sont celles des tables de reference liees a
/// l'audience (:30-36) : le type (AUDNC_TYPE_AUD_CAB -> TYPE_AUD_CAB), la chambre (CHAMBRE), la section (SECTION), et les types de
/// classement admis (OPE_TYPE_CLASSEMENT_ADMIS -> TYPE_CLASSEMENT) ; une liste de saisie ne propose que les valeurs utilisables en
/// saisie (*_UTILISABLE_SAISIE), un type d'AUDIENCE (R_TYPEAUDCAB_AUDIENCE = 1 : un cabinet n'est pas cree ici). Libelles a l'ecran.
/// LES AUDIENCES A VENIR : RIG v2 n'en montre pas a la creation ; l'ecran V3 les liste (il les declarait). Elles sont lues dans
/// AUDIENCE_CABINET comme la recherche d'audience (AudiencesV2.cs) : audiences (AUDNC_AUDIENCE = 1) du jour et apres, par date et
/// heure, avec le nombre d'affaires au role (APPEL_AFFAIRE). « Publiée sur le web » = non AUDNC_NON_DISPONIBLE_WEB (la case de RIG
/// v2 dit « Non disponible dans les choix d'Audience WEB », OPE_CREATION_AUDIENCE.Designer.cs:154).
/// NON FAIT : la creation elle-meme (aucune ecriture d'audience n'est branchee : l'ecran le dit, rien n'est enregistre).
/// </summary>
public static class LectureAudiences
{
    /// <summary>Le nombre d'audiences montrees.</summary>
    public const int Plafond = 200;

    private const string Requete = """
        SELECT TOP (@plafond) a.AUDNC_DATE, a.AUDNC_HEURE, COALESCE(t.R_TYPEAUDCAB_LIBELLE_ECRAN, a.AUDNC_TYPE_AUD_CAB) AS Type,
               COALESCE(ch.R_CHAMBRE_LIBELLE_ECRAN, a.AUDNC_CHAMBRE) AS Chambre, COALESCE(se.R_SECTION_LIBELLE_ECRAN, a.AUDNC_SECTION) AS Section,
               CONVERT(bit, ISNULL(a.AUDNC_PUBLIQUE, 0)) AS Publique, CONVERT(bit, ISNULL(a.AUDNC_NON_DISPONIBLE_WEB, 0)) AS NonDisponibleWeb,
               (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE AS aa WHERE aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC) AS Affaires, COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET AS a
        LEFT JOIN v2.TYPE_AUD_CAB AS t ON t.R_TYPEAUDCAB_VALEUR = a.AUDNC_TYPE_AUD_CAB
        LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = a.AUDNC_CHAMBRE
        LEFT JOIN v2.SECTION AS se ON se.R_SECTION_VALEUR = a.AUDNC_SECTION
        WHERE a.AUDNC_AUDIENCE = 1 AND a.AUDNC_DATE >= @jour
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC
        """;

    /// <summary>Les audiences a venir a partir de <paramref name="aujourdhui"/>, et les listes de la creation.</summary>
    public static async Task<AudiencesProgrammeesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<AudienceProgrammeeLue>();
        var total = 0;
        await using (var lu = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", SqlDbType.Int) { Value = plafond },
                         new SqlParameter("@jour", SqlDbType.DateTime) { Value = aujourdhui.Date }))
            while (await lu.ReadAsync(ct))
            {
                total = Convert.ToInt32(lu["Total"], CultureInfo.InvariantCulture);
                lignes.Add(new AudienceProgrammeeLue(lu["AUDNC_DATE"] as DateTime?, Chaine(lu, "AUDNC_HEURE"), Chaine(lu, "Type"), Chaine(lu, "Chambre"),
                    Chaine(lu, "Section"), lu["Publique"] is true, lu["NonDisponibleWeb"] is true,
                    Convert.ToInt32(lu["Affaires"], CultureInfo.InvariantCulture)));
            }
        var types = await ListeAsync(baseGreffe, """
            SELECT R_TYPEAUDCAB_LIBELLE_ECRAN AS L FROM v2.TYPE_AUD_CAB
            WHERE R_TYPEAUDCAB_AUDIENCE = 1 AND R_TYPEAUDCAB_UTILISABLE_SAISIE = 1 ORDER BY R_TYPEAUDCAB_LIBELLE_ECRAN
            """, ct);
        var chambres = await ListeAsync(baseGreffe,
            "SELECT R_CHAMBRE_LIBELLE_ECRAN AS L FROM v2.CHAMBRE WHERE R_CHAMBRE_UTILISABLE_SAISIE = 1 ORDER BY R_CHAMBRE_LIBELLE_ECRAN", ct);
        var sections = await ListeAsync(baseGreffe,
            "SELECT R_SECTION_LIBELLE_ECRAN AS L FROM v2.SECTION WHERE R_SECTION_UTILISABLE_SAISIE = 1 ORDER BY R_SECTION_LIBELLE_ECRAN", ct);
        var classements = await ListeAsync(baseGreffe,
            "SELECT R_TYPECLASSE_LIBELLE_ECRAN AS L FROM v2.TYPE_CLASSEMENT WHERE R_TYPECLASSE_UTILISABLE_SAISIE = 1 ORDER BY R_TYPECLASSE_LIBELLE_ECRAN", ct);
        return new AudiencesProgrammeesLues([.. lignes], total, types, chambres, sections, classements);
    }

    private static async Task<string[]> ListeAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        var valeurs = new List<string>();
        await using var lu = await LectureV2.LireAsync(cn, sql, ct);
        while (await lu.ReadAsync(ct)) if (Chaine(lu, "L") is { Length: > 0 } v && !valeurs.Contains(v)) valeurs.Add(v);
        return [.. valeurs];
    }
}
