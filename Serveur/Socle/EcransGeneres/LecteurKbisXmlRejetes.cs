using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Kbis XML rejetes » (PROC_REJETSKBISXML) : les Kbis XML transmis a Infogreffe revenus avec une erreur. Mode
/// d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RCS\Exe\PROC_RejetsKbisXml\FrmRejetsKbisXml.cs.
/// _ConstruireRequete (l. 299-338) : KBIS_XML JOIN DOSSIER_RCS (KBIS_ID_DSRCS), ou KBIS_LIBELLE_ERREUR IS NOT NULL
/// AND KBIS_IS_LAST = 1 (seul le dernier Kbis du dossier compte), puis les cases de l'ecran :
///   « Vivants » / « Radies » : DSSRC_DATE_RADIATION nulle ou non (les deux cochees : tous) ;
///   « Bloquantes » B, « Avertissements » A, « Informations » I, « Historiques » H : KBIS_CATEGORIE_ERREUR.
/// A l'ouverture (FrmRejetsKbisXml_Load, l. 64-114), les cases viennent de quatre reglages lus « == "1" », avec la
/// valeur de la V2 quand le reglage manque : KBISXML_ERR_BLOQ_DEFAULT (oui), KBISXML_ERR_NONBLOQ_DEFAULT (non),
/// KBISXML_RADIES_DEFAULT (non), KBISXML_VIVANTS_DEFAULT (oui) ; ni vivants ni radies -> vivants ; ni bloquantes ni
/// avertissements -> bloquantes ; « Informations » et « Historiques » toujours decochees (l. 109-110). RIG_DEV (lu le
/// 25/11/2026) les a toutes les quatre a « 1 ».
/// Tri de la V2 (l. 335) : dossiers vivants d'abord, bloquantes d'abord, puis le nom du dernier agent de la demande
/// (KbisUtilisateur, par UTILISATEUR et LIGNE_RAPP_GENE), puis le retour le plus recent. Ce tri par agent n'est pas
/// repris (les comptes sont des fiches soc.Compte, hors des tables V2) : vivants, bloquantes, retour le plus recent.
/// Dans la base V3, DOSSIER_RCS est rec.DossierRcs ; KBIS_XML la table V2 telle quelle (v2.KBIS_XML).
///
/// GROUPES : la categorie de l'erreur (B « Bloquantes », A « Avertissements », I « Informations »).
///
/// REGLE D'ETAT. Toute ligne est « A examiner » (AFaire, aucune marche) : la liste ne garde que les rejets encore
/// poses sur le dernier Kbis. « Annuler le rejet » vide la categorie et le libelle (tsmiAnnulerRejet_Click, l. 159-184)
/// et un Kbis plus recent fait tomber KBIS_IS_LAST : dans les deux cas la ligne sort de la liste. Aucune colonne de
/// la V2 ne dit qu'un rejet est en cours d'examen.
/// </summary>
internal sealed class LecteurKbisXmlRejetes : ILecteurEcran
{
    public string Procedure => "PROC_REJETSKBISXML";

    /// <summary>Les groupes du descripteur, au mot pres, par categorie d'erreur de la V2.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Groupes = new Dictionary<string, string>
    {
        ["B"] = "Bloquantes",
        ["A"] = "Avertissements",
        ["I"] = "Informations",
    };

    // Les cases a l'ouverture (FrmRejetsKbisXml.cs:76-104) : absent -> la valeur que la V2 coche alors.
    internal static readonly LectureReglage Bloquantes = new("KBISXML_ERR_BLOQ_DEFAULT", "1", "FrmRejetsKbisXml.cs:76");
    internal static readonly LectureReglage Avertissements = new("KBISXML_ERR_NONBLOQ_DEFAULT", "0", "FrmRejetsKbisXml.cs:83");
    internal static readonly LectureReglage Radies = new("KBISXML_RADIES_DEFAULT", "0", "FrmRejetsKbisXml.cs:90");
    internal static readonly LectureReglage Vivants = new("KBISXML_VIVANTS_DEFAULT", "1", "FrmRejetsKbisXml.cs:97");

    private static string Requete(bool vivants, bool radies, IEnumerable<string> categories) => $"""
        SELECT TOP (@plafond) k.KBIS_ID_KBIS, k.KBIS_CATEGORIE_ERREUR, k.KBIS_LIBELLE_ERREUR, k.KBIS_DATE_TRANSMISSION, k.KBIS_DATE_RETOUR,
               s.NumeroGestion, s.DateRadiation, e.Denomination, COUNT(*) OVER () AS Total
        FROM v2.KBIS_XML AS k
        JOIN rec.DossierRcs AS s ON s.Id = k.KBIS_ID_DSRCS
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        WHERE k.KBIS_LIBELLE_ERREUR IS NOT NULL AND k.KBIS_IS_LAST = 1
          {(vivants && radies ? "" : vivants ? "AND s.DateRadiation IS NULL" : "AND s.DateRadiation IS NOT NULL")}
          AND k.KBIS_CATEGORIE_ERREUR IN ({string.Join(", ", categories.Select(c => $"'{c}'"))})
        ORDER BY CASE WHEN s.DateRadiation IS NULL THEN 0 ELSE 1 END,
                 CASE WHEN k.KBIS_CATEGORIE_ERREUR = 'B' THEN 0 ELSE 1 END,
                 k.KBIS_DATE_RETOUR DESC, k.KBIS_ID_KBIS DESC
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (vivants, radies, categories) = Cases(
            await Bloquantes.LireOuiNonAsync(baseGreffe, RegleOuiNon.EgalUn, ct),
            await Avertissements.LireOuiNonAsync(baseGreffe, RegleOuiNon.EgalUn, ct),
            await Radies.LireOuiNonAsync(baseGreffe, RegleOuiNon.EgalUn, ct),
            await Vivants.LireOuiNonAsync(baseGreffe, RegleOuiNon.EgalUn, ct));
        return await LectureEcran.LireAsync(baseGreffe, Requete(vivants, radies, categories), Ligne, ct, new SqlParameter("@plafond", plafond));
    }

    /// <summary>Les cases cochees a l'ouverture de la V2 (FrmRejetsKbisXml.cs:107-110) : au moins vivants ou radies, au
    /// moins bloquantes ou avertissements ; jamais les informations ni les historiques.</summary>
    internal static (bool Vivants, bool Radies, string[] Categories) Cases(bool bloquantes, bool avertissements, bool radies, bool vivants)
    {
        if (!vivants && !radies) vivants = true;
        if (!bloquantes && !avertissements) bloquantes = true;
        return (vivants, radies, [.. bloquantes ? new[] { "B" } : [], .. avertissements ? new[] { "A" } : []]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var radiation = FormeLigne.DateDe(r, "DateRadiation");
        var transmis = FormeLigne.DateDe(r, "KBIS_DATE_TRANSMISSION");
        var retour = FormeLigne.DateDe(r, "KBIS_DATE_RETOUR");
        var precisions = FormeLigne.Joindre(
            radiation is null ? null : $"Radié le {FormeLigne.Date(radiation)}",
            transmis is null ? null : $"Transmis le {FormeLigne.Date(transmis)}",
            retour is null ? null : $"retour du {FormeLigne.Date(retour)}",
            PremiereLigne(FormeLigne.Texte(r, "KBIS_LIBELLE_ERREUR")));
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")));
        // La regle d'etat (en tete du fichier) : tout rejet de la liste est a examiner, aucune marche franchie. L'id : le Kbis
        // (KBIS_ID_KBIS, colIdKbis de la V2), que « Annuler le rejet » vise (EcrivainKbisXmlRejetes.cs, lot 16).
        return new LigneLue(nom, precisions, Groupes[FormeLigne.Texte(r, "KBIS_CATEGORIE_ERREUR")], "AFaire", [])
        {
            Id = Convert.ToInt32(r["KBIS_ID_KBIS"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Le libelle d'erreur de la V2 tient sur plusieurs lignes (l'erreur, puis son detail entre parentheses,
    /// 313 caracteres en moyenne dans RIG_DEV) : la ligne de l'ecran en garde la premiere, l'erreur elle-meme.</summary>
    private static string PremiereLigne(string libelle)
        => libelle.Split(['\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries) is [var premiere, ..] ? premiere.Trim() : libelle;
}
