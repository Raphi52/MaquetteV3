using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Export Excel » (PROC_LISTEEXCEL, forme Etat) : les listes d'instances que l'ancien RIG exporte vers Excel, telles que le greffe
/// les a parametrees. Mode d'emploi des lecteurs : LecteurEcran.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026).
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_LISTEEXCEL (cite « OPE_RECHERCHE.cs », « ExcelInstance.cs ») ;
/// le parametrage : Outils\DLL\RigMetier\ParametrageListe\ (ParametrageListe.cs, ParametrageListeXls.cs, ParametrageListeXlsListe.cs).
/// Fiche : D:\RigApplication\Documentation\reference\proc\proc_listeexcel.md, « Parametres de lancement ».
///
/// LES TYPES : le menu lance PROC_LISTEEXCEL avec un type de liste (MetierParametres) ; ceux des instances sont INS, IDJ, IEF, IEP,
///   SDJ, GIE et AFF (OPE_RECHERCHE.cs:77-84, proc_listeexcel.md:24-33). Une ligne par type que la base declare
///   (PARAMETRAGE_LISTE_XLS_TYPE_LISTE), dans cet ordre, a son libelle ; un type d'un autre ecran (procedures, mesures, preventions,
///   JAL, BODACC) n'est pas lu ici. Le type « CitationAffaireSignifiee » de la fiche n'est declare dans aucune base lue (RIG_DEV).
/// CE QUI EST LU, par type :
///   - ses colonnes : les champs SELECT de PARAMETRAGE_LISTE_XLS, et parmi eux ceux de la liste par defaut, qui ont un ordre
///     (GetAllParametrageListeXlsByTypeAndNumOrdreNotNull, ParametrageListeXls.cs:64) ;
///   - ses LISTES PARAMETREES (PARAMETRAGE_LISTE_XLS_LISTE du type, GetAllParametrageListeXlsListeByType, ParametrageListeXlsListe.cs:57),
///     que la liste deroulante de la V2 propose (OPE_RECHERCHE.cs:476-482) ; et celle qu'elle choisit d'office : la ligne « LISTE » du
///     type nomme son code (GetListeParDefaut, ParametrageListeXlsListe.cs:62-73, « RIEN » dans RIG_DEV = aucune) ;
///   - puis une ligne par liste parametree, sous son type : son libelle (son code s'il est vide), ses colonnes
///     (PARAMETRAGE_LISTE_XLS_COMPOSITION, GetSqlOfListe, :78).
/// GROUPES (ceux du descripteur) : INS, IDJ, IEF, IEP, SDJ « Instances » ; GIE « Groupements » ; AFF « Affaires et citations ».
/// ETAT : « A compter » (AFaire) pour toute ligne -- voir NON REPRIS.
/// NON REPRIS (nomme) : le COMPTAGE sur une periode et l'EXPORT. La V2 compte en executant le texte SQL stocke dans la base
///   (PARAMETRAGE_LISTE.PRMLS_REQUETE, sa partie FROM, plus la clause de ExcelInstance.cs:39-63) et l'export lance EXECUTE_EXCEL avec
///   ce texte (OPE_RECHERCHE.cs:440-463), qui ouvre un classeur NON enregistre dans Excel (aucun dossier choisi). Ce texte nomme les
///   tables de la V2 sans schema, alors qu'elles sont sous v2 dans la base V3, et la V3 n'execute pas de SQL lu dans la base : ni
///   « Comptee », ni « Bloquee : trop de lignes » (plus de 65 536, OPE_RECHERCHE.cs:467-472), ni « Exportee » ne sont donc poses.
/// </summary>
internal sealed class LecteurListesExcel : ILecteurEcran
{
    public string Procedure => "PROC_LISTEEXCEL";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Instances = "Instances", Groupements = "Groupements", Affaires = "Affaires et citations";

    // Les types de l'ecran, dans leur ordre (une sous-requete : LectureV2 n'admet qu'un SELECT seul, sans WITH).
    // fix-ok: mesure au banc EcransGeneresBaseCheck (27/11/2026) -- InvalidOperationException « Lecture V2 refusee : seule une
    // requete SELECT seule est permise » sur la premiere version, ecrite avec WITH Types AS (...).
    private const string Types = """
        (SELECT t.PRMLLXTYP_CODE AS Type, t.PRMLLXCMP_LIBELLE AS LibelleType,
                CASE t.PRMLLXTYP_CODE WHEN 'INS' THEN 1 WHEN 'IDJ' THEN 2 WHEN 'IEF' THEN 3 WHEN 'IEP' THEN 4 WHEN 'SDJ' THEN 5
                                      WHEN 'GIE' THEN 6 ELSE 7 END AS Rang
         FROM v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE AS t
         WHERE t.PRMLLXTYP_CODE IN ('INS', 'IDJ', 'IEF', 'IEP', 'SDJ', 'GIE', 'AFF'))
        """;

    private const string Requete = $$"""
        SELECT TOP (@plafond) x.*, COUNT(*) OVER () AS Total
        FROM (SELECT ty.Rang, 0 AS Sorte, 0 AS IdListe, ty.Type, ty.LibelleType, ty.LibelleType AS Nom, CAST(NULL AS varchar(50)) AS Code,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS AS c
                      WHERE c.PRMLX_TYPE_LISTE = ty.Type AND c.PRMLX_TYPE_TEXTE = 'SELECT' AND c.PRMLX_NUM_ORDRE IS NOT NULL) AS ColonnesDefaut,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS AS c
                      WHERE c.PRMLX_TYPE_LISTE = ty.Type AND c.PRMLX_TYPE_TEXTE = 'SELECT') AS Colonnes,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l WHERE l.PRMLLXLIST_TYPE = ty.Type) AS Listes,
                     (SELECT TOP (1) ISNULL(NULLIF(l.PRMLLXLIST_LIBELLE, ''), l.PRMLLXLIST_CODE)
                      FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l
                      JOIN v2.PARAMETRAGE_LISTE_XLS AS d ON d.PRMLX_LIBELLE = l.PRMLLXLIST_CODE AND d.PRMLX_TYPE_LISTE = l.PRMLLXLIST_TYPE
                      WHERE d.PRMLX_TYPE_LISTE = ty.Type AND d.PRMLX_TYPE_TEXTE = 'LISTE'
                      ORDER BY l.PRMLLXLIST_ID_PRMLLXLIST) AS ParDefaut,
                     0 AS EstDefaut
              FROM {{Types}} AS ty
              UNION ALL
              SELECT ty.Rang, 1, l.PRMLLXLIST_ID_PRMLLXLIST, ty.Type, ty.LibelleType, ISNULL(NULLIF(l.PRMLLXLIST_LIBELLE, ''), l.PRMLLXLIST_CODE),
                     l.PRMLLXLIST_CODE,
                     (SELECT COUNT(*) FROM v2.PARAMETRAGE_LISTE_XLS_COMPOSITION AS c WHERE c.PRMLLXCMP_ID_LISTE = l.PRMLLXLIST_ID_PRMLLXLIST),
                     NULL, NULL, NULL,
                     CASE WHEN EXISTS (SELECT 1 FROM v2.PARAMETRAGE_LISTE_XLS AS d WHERE d.PRMLX_TYPE_LISTE = l.PRMLLXLIST_TYPE
                                       AND d.PRMLX_TYPE_TEXTE = 'LISTE' AND d.PRMLX_LIBELLE = l.PRMLLXLIST_CODE) THEN 1 ELSE 0 END
              FROM v2.PARAMETRAGE_LISTE_XLS_LISTE AS l
              JOIN {{Types}} AS ty ON ty.Type = l.PRMLLXLIST_TYPE) AS x
        ORDER BY x.Rang, x.Sorte, x.IdListe
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static string Nombre(int n, string un, string plusieurs, string aucun) =>
        n == 0 ? aucun : n == 1 ? $"1 {un}" : $"{n.ToString(CultureInfo.InvariantCulture)} {plusieurs}";

    private static LigneLue Ligne(SqlDataReader r)
    {
        var type = FormeLigne.Texte(r, "Type");
        var groupe = type switch { "GIE" => Groupements, "AFF" => Affaires, _ => Instances };
        var entier = (string colonne) => Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);
        string precisions;
        if (entier("Sorte") == 0)
        {
            var (colonnes, parDefaut) = (entier("Colonnes"), entier("ColonnesDefaut"));
            precisions = FormeLigne.Joindre(
                colonnes == 0 ? "aucune colonne paramétrée"
                              : $"{parDefaut.ToString(CultureInfo.InvariantCulture)} colonne{(parDefaut > 1 ? "s" : "")} par défaut sur {colonnes.ToString(CultureInfo.InvariantCulture)}",
                Nombre(entier("Listes"), "liste paramétrée", "listes paramétrées", "aucune liste paramétrée"),
                FormeLigne.Texte(r, "ParDefaut") is { Length: > 0 } defaut ? $"liste par défaut : {defaut}" : null);
        }
        else
            precisions = FormeLigne.Joindre(
                $"liste paramétrée {FormeLigne.Texte(r, "Code")}", FormeLigne.Texte(r, "LibelleType"),
                Nombre(entier("ColonnesDefaut"), "colonne", "colonnes", "aucune colonne"),
                entier("EstDefaut") == 1 ? "liste par défaut" : null);
        return new LigneLue(FormeLigne.Texte(r, "Nom"), precisions, groupe, "AFaire", []);
    }
}
