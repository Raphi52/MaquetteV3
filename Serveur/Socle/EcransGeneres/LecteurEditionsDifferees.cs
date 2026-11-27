using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Éditions différées » (PROC_EDT_DIFF) : les lots d'editions differees ouverts, en attente de cloture, et le DETAIL de chaque lot,
/// document par document. Mode d'emploi : LecteurEcran.cs ; les gestes : EcrivainEditionsDifferees.cs. Plan « RIG v3 100 %
/// fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_EDT_DIFF\ (proc_edt_diff.md, Detail OPE, qui fait foi).
///   - LES LOTS : MetierEdtDiff.GetLotEdtActifs (Metier\MetierEdtDiff.cs:19-24) -- LOT LEFT JOIN GROUPE_IMPRESSION (LOT_NOM =
///     GPIMP_NOM_LOT) LEFT JOIN DEMANDE_EDITION (DMNED_NUM_GROUPE = GPIMP_NUM_GROUPE), « where LOT_ACTIF=1 and not LOT_DIRECT=1 and
///     GPIMP_DATE_FIN is null and LOT_VISIBLE_EDTDIFF = 1 », groupe par lot et groupe d'impression, « having count(DMNED_ID_DMNED) > 0 »,
///     « order by LOT_NOM, LOT_NUM_ORDRE ». Une ligne = un lot ouvert (RIG v2 : une etape ETP_EDT_DIFF_LOT par lot, PROC_EDT_DIFF.cs:
///     31-35) : son nom, la date d'ouverture du groupe (GPIMP_DATE_DEBUT), l'imprimante (GPIMP_NOM_IMPRIMANTE), le nombre de documents
///     (le count de la requete) et le nombre de ceux marques « Ne pas imprimer » (DMNED_IMPRESSION = 0).
///   - LE DETAIL D'UN LOT (OPE_LISTE_LOT, la grille DataGridViewListeLot) : les demandes d'edition du groupe d'impression du lot,
///     « WHERE DMNED_NUM_GROUPE = <groupe> ORDER BY DMNED_NUM_ORDRE_GROUPE, DMNED_ID_DMNED » (GroupeImpression._loadDemandeEdition,
///     Outils\DLL\RigMetier\Edition\GroupeImpression.cs:102-107), une ligne par document, ses colonnes (_FillRow, :119-140) : le
///     libelle du document, le destinataire (DMNED_DESIGNATION_DESTINATAIRE), la date de creation, le nombre d'exemplaires, le numero
///     de dossier (DMND_NUM_INDICATIF de la demande), la maquette (DMNED_CODE_MAQUETTE), l'utilisateur de la demande ; un document
///     DMNED_IMPRESSION = 0 est barre et sa case « Ne pas imprimer » cochee (:132-139).
///
/// GROUPES : « Lots ouverts » (un lot clos disparait, GPIMP_DATE_FIN pose) et « Documents des lots » (le detail, lot par lot dans
/// l'ordre des lots). Le plafond vaut pour chaque groupe (TotalParGroupe) : un detail long ne cache pas un lot.
/// REGLE D'ETAT -- la cloture de RIG v2 exige une imprimante (OPE_EDT_DIFF_LOT.DoVerifier, :52-54), pas la case d'un document :
///   - un LOT : AFaire « À clôturer » si l'imprimante du groupe est choisie ; sinon EnAttente « Imprimante à choisir » (« Vous devez
///     choisir une imprimante pour pouvoir clôturer ») -- la forme File ne saisit pas l'imprimante : la choisir dans RIG v2 ;
///   - un DOCUMENT n'est jamais fige : RIG v2 le barre ou le retablit dans tout lot ouvert, imprimante choisie ou non
///     (OPE_LISTE_LOT._TraiterEditionsCochees, :53-78), et la forme File ote sa case a une ligne ni AFaire ni EnCours
///     (web/src/ecrans/formes/commun.tsx:78). A imprimer : AFaire ; barre (DMNED_IMPRESSION = 0, :132-139) : EnCours « Ne pas imprimer ».
///     (Avant le 27/11/2026 : Rejetee et EnAttente, figes : « Remettre à l'impression » etait injouable a l'ecran.)
/// Id = GPIMP_NUM_GROUPE pour un lot, « E-&lt;DMNED_ID_DMNED&gt; » pour un document.
///
/// Sans lot ouvert, RIG v2 affiche l'etape vide (ETP_EDT_DIFF_VIDE) : la liste est vide.
/// NON REPRIS (nomme) : « Visualiser » un document (Document.AffichageByIdDemandeEdition, :142-147 : le moteur de documents de RIG v2) ;
/// l'adresse du destinataire en info-bulle (infoDestinataire.AdresseBase) ; la liste « Imprimante » de la synthese (seule saisie de
/// RIG v2) : la forme File n'a pas de champ.
/// </summary>
internal sealed class LecteurEditionsDifferees : ILecteurEcran
{
    public string Procedure => "PROC_EDT_DIFF";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string LotsOuverts = "Lots ouverts", Documents = "Documents des lots";

    /// <summary>L'etat d'un document « Ne pas imprimer » (le descripteur, au mot pres).</summary>
    internal const string EtatNePasImprimer = "EnCours";

    /// <summary>Les lots ouverts de RIG v2 (MetierEdtDiff.cs:19-24), un lot ou tous ; « not LOT_DIRECT=1 » : un LOT_DIRECT NULL est
    /// ecarte, comme en SQL de RIG v2.</summary>
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

    /// <summary>Le detail des lots ouverts : leurs demandes d'edition (GroupeImpression._loadDemandeEdition, :102-107), un document ou
    /// tous (/*document*/). Un groupe ouvert d'un lot visible a forcement ses documents : le HAVING des lots n'a pas a etre repete.</summary>
    internal const string RequeteDocuments = """
        SELECT TOP (@plafond) l.LOT_NOM, g.GPIMP_NUM_GROUPE, e.DMNED_ID_DMNED, e.DMNED_LIBELLE_DOCUMENT,
               e.DMNED_DESIGNATION_DESTINATAIRE, e.DMNED_DATE_CREATION, e.DMNED_NOMBRE_EXEMPLAIRE, e.DMNED_CODE_MAQUETTE, e.DMNED_IMPRESSION,
               e.DMNED_PHASE_TRAITEMENT, d.DMND_NUM_INDICATIF, d.DMND_NOM_UTILISATEUR, COUNT(*) OVER () AS Total
        FROM v2.LOT AS l
        JOIN v2.GROUPE_IMPRESSION AS g ON l.LOT_NOM = g.GPIMP_NOM_LOT
        JOIN v2.DEMANDE_EDITION AS e ON e.DMNED_NUM_GROUPE = g.GPIMP_NUM_GROUPE
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = e.DMNED_ID_DMND
        WHERE l.LOT_ACTIF = 1 AND NOT l.LOT_DIRECT = 1 AND g.GPIMP_DATE_FIN IS NULL AND l.LOT_VISIBLE_EDTDIFF = 1 /*document*/
        ORDER BY l.LOT_NOM, l.LOT_NUM_ORDRE, g.GPIMP_NUM_GROUPE, e.DMNED_NUM_ORDRE_GROUPE, e.DMNED_ID_DMNED
        """;

    public async Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var lots = await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*groupe*/", "", StringComparison.Ordinal), Ligne, ct,
                                                new SqlParameter("@plafond", plafond));
        var documents = await LectureEcran.LireAsync(baseGreffe, RequeteDocuments.Replace("/*document*/", "", StringComparison.Ordinal), LigneDocument, ct,
                                                     new SqlParameter("@plafond", plafond));
        return new LignesLues([.. lots.Lignes, .. documents.Lignes], lots.Total + documents.Total)
        {
            TotalParGroupe = new Dictionary<string, int> { [LotsOuverts] = lots.Total, [Documents] = documents.Total },
        };
    }

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

    /// <summary>Un document du detail (les colonnes de DataGridViewListeLot._FillRow, :119-140).</summary>
    // fix-ok: lu sur les lignes des jeux PROC_SIGNJUD.sql et PROC_RELANCELOT.sql (27/11/2026) -- DMNED_NOMBRE_EXEMPLAIRE y est NULL, et le
    // premier jet l'affichait « 1 exemplaire » ; un nombre absent ne se dit plus.
    private static LigneLue LigneDocument(SqlDataReader r)
    {
        var barre = r["DMNED_IMPRESSION"] is bool b && !b;
        int? exemplaires = r["DMNED_NOMBRE_EXEMPLAIRE"] is DBNull ? null : Convert.ToInt32(r["DMNED_NOMBRE_EXEMPLAIRE"], CultureInfo.InvariantCulture);
        var creation = FormeLigne.DateDe(r, "DMNED_DATE_CREATION");
        var dossier = FormeLigne.Texte(r, "DMND_NUM_INDICATIF");
        var maquette = FormeLigne.Texte(r, "DMNED_CODE_MAQUETTE");
        var utilisateur = FormeLigne.Texte(r, "DMND_NOM_UTILISATEUR");
        var precisions = FormeLigne.Joindre($"Lot {FormeLigne.Texte(r, "LOT_NOM")}", FormeLigne.Texte(r, "DMNED_DESIGNATION_DESTINATAIRE") is { Length: > 0 } dest ? dest : null,
            creation is null ? null : $"créé le {FormeLigne.Date(creation)}", exemplaires is { } n ? $"{n} exemplaire{(n > 1 ? "s" : "")}" : null,
            dossier.Length > 0 ? $"dossier {dossier}" : null, maquette.Length > 0 ? $"maquette {maquette}" : null,
            utilisateur.Length > 0 ? $"par {utilisateur}" : null, barre ? "ne pas imprimer" : null);
        var id = FormeLigne.Texte(r, "DMNED_ID_DMNED");
        var libelle = FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT");
        var etat = barre ? EtatNePasImprimer : "AFaire";
        return new LigneLue(libelle.Length > 0 ? libelle : $"Document {id}", precisions, Documents, etat, []) { Id = "E-" + id };
    }
}
