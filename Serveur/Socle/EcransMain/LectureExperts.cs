using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Experts » (PROC_EXPERT, web/src/ecrans/EcranExperts.tsx) : les experts du greffe et les mesures où ils participent, lus dans la base
/// V3 du greffe. LECTURE SEULE (LectureV2) : l'écran V3 n'édite pas. Plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EXPERT\PROC_EXPERT.
///   - LA LISTE : Recherche\OPE_RECHERCHE_EXPERT -- tous les experts (table EXPERT, RigBaseGreffe\GREFFE\EXPERT.cs), filtres a l'ecran
///     (plein texte, actifs seulement), tries par abrege comme la liste des acteurs (OPE_RECHERCHE_ACTEUR.cs:706-714, memes colonnes).
///   - LA FICHE : Edition\OPE_EDITION_EXPERT -- civilite, nom, abrege, complement, naissance, adresse (ADRESSE_BASE recopiee : rec.Adresse),
///     telephone, fax, courriel, qualification, actif.
///   - LES MESURES : Edition\OPE_MESURE_EXPERT.cs:83 -- Participant.GetListeParticipantJoinMesureByExpert (RigMetier) : PARTICIPANT joint
///     a MESURE (PRTCP_ID_MSINS = MSINS_ID_MSINS) ou PRTCP_ID_EXPRT = l'expert ; « actives » = PRTCP_ACTIF = 1 et MSINS_DATE_CLOTURE nulle,
///     sinon « inactives ». Colonnes de ULT_LISTVIEW_MESURES_EXPERT : numero de mesure, civilite, nom, complement et qualification du
///     participant, adresse.
/// </summary>
public static class LectureExperts
{
    public const int Plafond = 500;

    private const string AdresseSql = """
        (SELECT TOP (1) CONCAT_WS(' ', NULLIF(CONCAT_WS(' ', ad.NumeroVoie, ad.TypeVoie, ad.NomVoie), ''), NULLIF(CONCAT_WS(' ', ad.CodePostal, ad.Localite), ''))
         FROM rec.Adresse AS ad WHERE ad.Id = {0})
        """;

    private static readonly string RequeteExperts = $"""
        SELECT TOP (@plafond) e.EXPRT_ID_EXPRT, e.EXPRT_ABREGE, e.EXPRT_CIVILITE, e.EXPRT_NOM, e.EXPRT_COMPLEMENT_NOM, e.EXPRT_DATE_NAISSANCE,
               {string.Format(CultureInfo.InvariantCulture, AdresseSql, "e.EXPRT_ID_ADRBS")} AS Adresse, e.EXPRT_TELEPHONE, e.EXPRT_FAX,
               e.EXPRT_ADRESSE_MAIL, e.EXPRT_QUALIF_PROFESSION, CAST(e.EXPRT_EST_ACTIF AS bit) AS Actif, COUNT(*) OVER () AS Total
        FROM v2.EXPERT AS e
        ORDER BY e.EXPRT_ABREGE, e.EXPRT_ID_EXPRT
        """;

    private static readonly string RequeteMesures = $"""
        SELECT p.PRTCP_ID_EXPRT, m.MSINS_NUM_MESURE, p.PRTCP_CIVILITE, p.PRTCP_NOM, p.PRTCP_COMPLEMENT_NOM, p.PRTCP_QUALIF_PROFESSION,
               {string.Format(CultureInfo.InvariantCulture, AdresseSql, "p.PRTCP_ID_ADRBS")} AS Adresse,
               CAST(CASE WHEN p.PRTCP_ACTIF = 1 AND m.MSINS_DATE_CLOTURE IS NULL THEN 1 ELSE 0 END AS bit) AS EnCours
        FROM v2.PARTICIPANT AS p INNER JOIN v2.MESURE AS m ON p.PRTCP_ID_MSINS = m.MSINS_ID_MSINS
        WHERE p.PRTCP_ID_EXPRT IN (SELECT TOP (@plafond) e.EXPRT_ID_EXPRT FROM v2.EXPERT AS e ORDER BY e.EXPRT_ABREGE, e.EXPRT_ID_EXPRT)
        ORDER BY p.PRTCP_ID_EXPRT, m.MSINS_NUM_MESURE, p.PRTCP_ID_PRTCP
        """;

    private static async Task<bool> TableAsync(SqlConnection baseGreffe, string table, CancellationToken ct)
    {
        await using var o = await LectureV2.LireAsync(baseGreffe, "SELECT OBJECT_ID(@t)", ct, new SqlParameter("@t", table));
        return await o.ReadAsync(ct) && !o.IsDBNull(0);
    }

    /// <summary>Les experts et leurs mesures ; sans table EXPERT, aucun expert ; sans PARTICIPANT ou MESURE, aucune mesure.</summary>
    public static async Task<ExpertsLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        if (!await TableAsync(baseGreffe, "v2.EXPERT", ct)) return new ExpertsLus([], [], 0);
        static string T(SqlDataReader r, string c) => r[c] is DBNull or null ? "" : Convert.ToString(r[c], CultureInfo.InvariantCulture)!.Trim();
        var experts = new List<ExpertLu>();
        var total = 0;
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteExperts, ct, new SqlParameter("@plafond", plafond)))
            while (await r.ReadAsync(ct))
            {
                total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
                experts.Add(new ExpertLu(Convert.ToInt32(r["EXPRT_ID_EXPRT"], CultureInfo.InvariantCulture), T(r, "EXPRT_ABREGE"), T(r, "EXPRT_CIVILITE"),
                    T(r, "EXPRT_NOM"), T(r, "EXPRT_COMPLEMENT_NOM"),
                    r["EXPRT_DATE_NAISSANCE"] is DateTime n ? n.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "", T(r, "Adresse"),
                    T(r, "EXPRT_TELEPHONE"), T(r, "EXPRT_FAX"), T(r, "EXPRT_ADRESSE_MAIL"), T(r, "EXPRT_QUALIF_PROFESSION"), r["Actif"] is true));
            }
        var mesures = new List<MesureExpertLue>();
        if (experts.Count > 0 && await TableAsync(baseGreffe, "v2.PARTICIPANT", ct) && await TableAsync(baseGreffe, "v2.MESURE", ct))
            await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteMesures, ct, new SqlParameter("@plafond", plafond)))
                while (await r.ReadAsync(ct))
                    mesures.Add(new MesureExpertLue(Convert.ToInt32(r["PRTCP_ID_EXPRT"], CultureInfo.InvariantCulture), T(r, "MSINS_NUM_MESURE"),
                        T(r, "PRTCP_CIVILITE"), T(r, "PRTCP_NOM"), T(r, "PRTCP_COMPLEMENT_NOM"), T(r, "PRTCP_QUALIF_PROFESSION"), T(r, "Adresse"),
                        r["EnCours"] is true));
        return new ExpertsLus([.. experts], [.. mesures], total);
    }
}
