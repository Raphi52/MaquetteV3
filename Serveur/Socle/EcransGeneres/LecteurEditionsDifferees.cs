using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Éditions différées » (PROC_EDT_DIFF) : les lots d'editions differees ouverts, en attente de cloture. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainEditionsDifferees.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_EDT_DIFF\Metier\MetierEdtDiff.cs:19-24 (proc_edt_diff.md, Detail OPE, qui fait
/// foi) -- GetLotEdtActifs : LOT LEFT JOIN GROUPE_IMPRESSION (LOT_NOM = GPIMP_NOM_LOT) LEFT JOIN DEMANDE_EDITION (DMNED_NUM_GROUPE =
/// GPIMP_NUM_GROUPE), « where LOT_ACTIF=1 and not LOT_DIRECT=1 and GPIMP_DATE_FIN is null and LOT_VISIBLE_EDTDIFF = 1 », groupe par
/// lot et groupe d'impression, « having count(DMNED_ID_DMNED) > 0 », « order by LOT_NOM, LOT_NUM_ORDRE ». Une ligne = un lot ouvert
/// (RIG v2 : une occurrence de l'etape ETP_EDT_DIFF_LOT par lot, PROC_EDT_DIFF.cs:31-35) : son nom, la date d'ouverture du groupe
/// (GPIMP_DATE_DEBUT), l'imprimante (GPIMP_NOM_IMPRIMANTE), le nombre de documents (le count de la requete) ; et le nombre de ceux
/// marques « Ne pas imprimer » (DMNED_IMPRESSION = 0 : la ligne barree de DataGridViewListeLot._FillRow, :133-138).
/// Sans lot ouvert, RIG v2 affiche l'etape vide (ETP_EDT_DIFF_VIDE) : la liste est vide.
///
/// GROUPE : « Lots ouverts » (le seul : un lot clos disparait, GPIMP_DATE_FIN pose).
/// REGLE D'ETAT -- la cloture de RIG v2 exige une imprimante (OPE_EDT_DIFF_LOT.DoVerifier, :52-54) :
///   - AFaire « À clôturer » : l'imprimante du groupe est choisie ;
///   - EnAttente « Imprimante à choisir » : aucune (« Vous devez choisir une imprimante pour pouvoir clôturer ») -- la forme File
///     ne saisit pas l'imprimante : la choisir dans RIG v2.
/// Id = GPIMP_NUM_GROUPE.
///
/// NON REPRIS (nomme) : le detail d'un lot (OPE_LISTE_LOT, les documents un a un, « Visualiser ») et sa case « Ne pas imprimer »
/// (_TraiterEditionsCochees, :53-78) : une ligne de la file est un lot, pas un document -- le nombre de documents barres est donne ;
/// la liste « Imprimante » de la synthese (seule saisie de RIG v2) : la forme File n'a pas de champ.
/// </summary>
internal sealed class LecteurEditionsDifferees : ILecteurEcran
{
    public string Procedure => "PROC_EDT_DIFF";

    /// <summary>Le groupe du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string LotsOuverts = "Lots ouverts";

    /// <summary>La requete de RIG v2 (MetierEdtDiff.cs:19-24), un lot ou tous ; « not LOT_DIRECT=1 » : un LOT_DIRECT NULL est ecarte,
    /// comme en SQL de RIG v2.</summary>
    internal const string Requete = """
        SELECT TOP (@plafond) l.LOT_NOM, l.LOT_NUM_ORDRE, g.GPIMP_NOM_IMPRIMANTE, g.GPIMP_DATE_DEBUT, g.GPIMP_NUM_GROUPE, g.GPIMP_PHASE_TRAITEMENT,
               COUNT(e.DMNED_ID_DMNED) AS NB, SUM(CASE WHEN e.DMNED_IMPRESSION = 0 THEN 1 ELSE 0 END) AS NonImprimes,
               COUNT(*) OVER () AS Total
        FROM v2.LOT AS l
        LEFT OUTER JOIN v2.GROUPE_IMPRESSION AS g ON l.LOT_NOM = g.GPIMP_NOM_LOT
        LEFT OUTER JOIN v2.DEMANDE_EDITION AS e ON e.DMNED_NUM_GROUPE = g.GPIMP_NUM_GROUPE
        WHERE l.LOT_ACTIF = 1 AND NOT l.LOT_DIRECT = 1 AND g.GPIMP_DATE_FIN IS NULL AND l.LOT_VISIBLE_EDTDIFF = 1 /*groupe*/
        GROUP BY l.LOT_NUM_ORDRE, l.LOT_NOM, g.GPIMP_NOM_IMPRIMANTE, g.GPIMP_DATE_DEBUT, g.GPIMP_DATE_FIN, g.GPIMP_NUM_GROUPE, g.GPIMP_PHASE_TRAITEMENT
        HAVING COUNT(e.DMNED_ID_DMNED) > 0
        ORDER BY l.LOT_NOM, l.LOT_NUM_ORDRE, g.GPIMP_NUM_GROUPE
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*groupe*/", "", StringComparison.Ordinal), Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var documents = Convert.ToInt32(r["NB"], CultureInfo.InvariantCulture);
        var barres = r["NonImprimes"] is DBNull ? 0 : Convert.ToInt32(r["NonImprimes"], CultureInfo.InvariantCulture);
        var imprimante = FormeLigne.Texte(r, "GPIMP_NOM_IMPRIMANTE");
        var debut = FormeLigne.DateDe(r, "GPIMP_DATE_DEBUT");
        var precisions = FormeLigne.Joindre(documents > 1 ? $"{documents} documents" : "1 document",
            barres == 0 ? null : barres > 1 ? $"{barres} à ne pas imprimer" : "1 à ne pas imprimer",
            debut is null ? null : $"ouvert le {FormeLigne.Date(debut)}",
            imprimante.Length > 0 ? $"imprimante « {imprimante} »" : "aucune imprimante choisie",
            $"groupe {FormeLigne.Texte(r, "GPIMP_NUM_GROUPE")}");
        return new LigneLue($"Lot {FormeLigne.Texte(r, "LOT_NOM")}", precisions, LotsOuverts, imprimante.Length > 0 ? "AFaire" : "EnAttente", [])
        {
            Id = FormeLigne.Texte(r, "GPIMP_NUM_GROUPE"),
        };
    }
}
