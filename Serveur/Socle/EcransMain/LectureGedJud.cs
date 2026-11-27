using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « GED judiciaire » (PROC_GEDJUD, web/src/ecrans/EcranGedJud.tsx) : les documents de la GED judiciaire du greffe, lus dans la
/// base V3 (v2.VIDIGED_INDEX, v2.VIDIGED_PARAMETRAGE, v2.VIDIGED_DJ, v2.DECISION_JUDICIAIRE et les entités recopiées). LECTURE
/// SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : PROC_GEDJUD, OPE_RECHERCHE_DOC.Rechercher (D:\RigApplication\Source\RIG\DLL\Processus\PROC_GEDJUD\
/// ETP_RECHERCHE_DOC\OPE_RECHERCHE_DOC.cs ; le Détail OPE de proc_gedjud.md fait foi) :
///   - la requête : « select ... from VIDIGED_INDEX LEFT OUTER JOIN VIDIGED_PARAMETRAGE ON VGIDX_NATURE_DOC = VGPRM_CODE and
///     VGPRM_TYPE = 'NATURE_DOC' » (:259-260) ; la nature affichée est ce libellé ;
///   - « VGIDX_ID_ENTITE is not null » (:375) : un document sans entité n'est pas montré ;
///   - « VGIDX_ETAT &lt;&gt; 'R' » sauf « Afficher tout » (:376) : la V3 lit les rejetés aussi et l'ÉCRAN applique la case (le
///     même document reste lu quand on la coche) ;
///   - l'état : son libellé est celui de VIDIGED_PARAMETRAGE de type « ETAT » (la combo de l'état, ULT_COMBO_ETAT.cs:17-25 :
///     V Validé, VR Validé avec réserve, A En attente, R Rejeté dans RIG_DEV) ;
///   - les décisions liées : VIDIGED_DJ par le GUID du document (GetNbDecisionsJudiciairesLiees,
///     OPE_GED_MODIF_DOCUMENT.cs:80-91, montré dans le libellé du nœud, OPE_RECHERCHE_DOC.cs:651-656) ; la date rendue et prévue
///     sont celles de la première décision liée (VGDDJ_NUM_ORDRE, puis l'identifiant) ;
///   - l'entité : son type (VGIDX_TYPE_ENTITE, enumEntite :26) et son numéro, lu dans la table de son type comme le fait le
///     rechargement de l'entité (:417-487) : INSTANCE.INSTN_NUM_ROLE, PROCEDURE_COLLECTIVE.PRCLL_NUM_PC, PREVENTION.PREVE_NUM_PRV,
///     MESURE.MSINS_NUM_MESURE ; une audience (ou une entité dont la ligne manque dans la copie) garde son identifiant.
/// ÉCART NOMMÉ : RIG v2 exige un critère avant de chercher (VerifSaisie, :251-254, :671-688) et trie par entité (:382) ; l'écran
/// V3 s'ouvre sur les documents les plus récemment reçus (au plus le plafond), que ses critères filtrent ensuite.
/// NON REPRIS : l'élargissement aux entités rattachées (:274-339, il part d'une entité saisie), le filtre « Audience / cabinet »
/// (un écran de recherche OCX, :331-333, :802-934) et les minutes non numérisées (:529-596, elles viennent des décisions et non
/// de VIDIGED_INDEX).
/// </summary>
public static class LectureGedJud
{
    /// <summary>Au plus autant de documents (le plafond des écrans faits main).</summary>
    public const int Plafond = 500;

    private const string Requete = """
        SELECT TOP (@plafond) x.VGIDX_ID_VGIDX, x.VGIDX_LIBELLE, x.VGIDX_TYPE_ENTITE, x.VGIDX_ID_ENTITE, x.VGIDX_NATURE_DOC,
               n.VGPRM_LIBELLE AS Nature, x.VGIDX_ETAT, e.VGPRM_LIBELLE AS Etat, x.VGIDX_DATE_RECEPTION, x.VGIDX_COMMENTAIRE,
               x.VGIDX_MOTIF_REJET,
               CASE x.VGIDX_TYPE_ENTITE WHEN 'INSTANCE' THEN (SELECT i.INSTN_NUM_ROLE FROM v2.INSTANCE AS i WHERE i.INSTN_ID_INSTN = x.VGIDX_ID_ENTITE)
                    WHEN 'PROCEDURE_COLLECTIVE' THEN (SELECT p.PRCLL_NUM_PC FROM v2.PROCEDURE_COLLECTIVE AS p WHERE p.PRCLL_ID_PRCLL = x.VGIDX_ID_ENTITE)
                    WHEN 'PREVENTION' THEN (SELECT p.PREVE_NUM_PRV FROM v2.PREVENTION AS p WHERE p.PREVE_ID_PREVE = x.VGIDX_ID_ENTITE)
                    WHEN 'MESURE' THEN (SELECT m.MSINS_NUM_MESURE FROM v2.MESURE AS m WHERE m.MSINS_ID_MSINS = x.VGIDX_ID_ENTITE) END AS Numero,
               (SELECT COUNT(*) FROM v2.VIDIGED_DJ AS l WHERE l.VGDDJ_GUID_DOCUMENT = x.VGIDX_GUID_DOCUMENT) AS DecisionsLiees,
               dj.DCJDC_DATE, dj.DCJDC_DATE_PREVUE, COUNT(*) OVER () AS Total
        FROM v2.VIDIGED_INDEX AS x
        OUTER APPLY (SELECT TOP (1) p.VGPRM_LIBELLE FROM v2.VIDIGED_PARAMETRAGE AS p
                     WHERE p.VGPRM_CODE = x.VGIDX_NATURE_DOC AND p.VGPRM_TYPE = 'NATURE_DOC' ORDER BY p.VGPRM_ID_VGPRM) AS n
        OUTER APPLY (SELECT TOP (1) p.VGPRM_LIBELLE FROM v2.VIDIGED_PARAMETRAGE AS p
                     WHERE p.VGPRM_CODE = x.VGIDX_ETAT AND p.VGPRM_TYPE = 'ETAT' ORDER BY p.VGPRM_ID_VGPRM) AS e
        OUTER APPLY (SELECT TOP (1) d.DCJDC_DATE, d.DCJDC_DATE_PREVUE FROM v2.VIDIGED_DJ AS l
                     JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_DCJDC = l.VGDDJ_ID_DCJDC
                     WHERE l.VGDDJ_GUID_DOCUMENT = x.VGIDX_GUID_DOCUMENT ORDER BY l.VGDDJ_NUM_ORDRE, l.VGDDJ_ID_VGDDJ) AS dj
        WHERE x.VGIDX_ID_ENTITE IS NOT NULL
        ORDER BY x.VGIDX_DATE_RECEPTION DESC, x.VGIDX_ID_VGIDX DESC
        """;

    /// <summary>Le libellé d'un type d'entité de VIDIGED_INDEX (enumEntite, OPE_RECHERCHE_DOC.cs:26).</summary>
    internal static string TypeEntite(string code) => code.Trim().ToUpperInvariant() switch
    {
        "INSTANCE" => "Instance",
        "MESURE" => "Mesure",
        "PROCEDURE_COLLECTIVE" => "Procédure collective",
        "PREVENTION" => "Prévention",
        "AUDIENCE" => "Audience",
        "CABINET" => "Cabinet",
        _ => code.Trim(),
    };

    /// <summary>Les documents de la GED judiciaire, au plus <paramref name="plafond"/>.</summary>
    public static async Task<DocumentsGedLus> LireAsync(SqlConnection baseGreffe, CancellationToken ct, int plafond = Plafond)
    {
        var lignes = new List<DocumentGedLu>();
        var total = 0;
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@plafond", plafond));
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], System.Globalization.CultureInfo.InvariantCulture);
            var type = FormeLigne.Texte(r, "VGIDX_TYPE_ENTITE");
            var numero = FormeLigne.Texte(r, "Numero").Trim();
            var codeEtat = FormeLigne.Texte(r, "VGIDX_ETAT").Trim();
            var nature = FormeLigne.Texte(r, "Nature").Trim();
            var etat = FormeLigne.Texte(r, "Etat").Trim();
            lignes.Add(new DocumentGedLu(
                Convert.ToInt32(r["VGIDX_ID_VGIDX"], System.Globalization.CultureInfo.InvariantCulture),
                FormeLigne.Texte(r, "VGIDX_LIBELLE").Trim(),
                TypeEntite(type),
                numero.Length > 0 ? numero : $"{TypeEntite(type)} {r["VGIDX_ID_ENTITE"]}",
                nature.Length > 0 ? nature : FormeLigne.Texte(r, "VGIDX_NATURE_DOC").Trim(),
                codeEtat,
                etat.Length > 0 ? etat : codeEtat,
                FormeLigne.DateDe(r, "VGIDX_DATE_RECEPTION"),
                FormeLigne.Texte(r, "VGIDX_COMMENTAIRE").Trim(),
                FormeLigne.Texte(r, "VGIDX_MOTIF_REJET").Trim(),
                Convert.ToInt32(r["DecisionsLiees"], System.Globalization.CultureInfo.InvariantCulture),
                FormeLigne.DateDe(r, "DCJDC_DATE"),
                FormeLigne.DateDe(r, "DCJDC_DATE_PREVUE")));
        }
        return new DocumentsGedLus([.. lignes], total);
    }
}
