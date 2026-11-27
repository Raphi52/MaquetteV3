using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « File des editions » (PROC_POOLEDITION) : les demandes d'edition du greffe, de la creation a l'emission. Mode
/// d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\EXE\Accueil\PROC_PoolEdition\PoolEdition.cs. _GetRequete
/// (l. 345-519) lit DEMANDE_EDITION (« demande_edition_source ») LEFT JOIN DEMANDE (DMNED_ID_DMND), avec les criteres
/// saisis ; a l'ouverture ils sont tous vides (_ResetRecherche, l. 323-343 : dossier, destinataire, libelle, dates
/// decochees, « voir aussi les supprimees » decochee) et la recherche par defaut (Entree, F5) n'est pas celle du spool
/// (_spool = false, l. 232-243) : toutes les demandes d'edition, les plus recentes d'abord (« SELECT TOP n ... order by
/// dmned_date_creation desc », l. 838). Les demandes supprimees sont dans DEMANDE_EDITION_SUPPRIMEE, lue seulement
/// case cochee : pas ici. Dans la base V3, DEMANDE_EDITION et DEMANDE sont les tables V2 telles quelles (v2.*),
/// DOSSIER_RCS rec.DossierRcs, ENTREPRISE rec.Entreprise. Le dossier d'une edition : DMNED_ID_DSRCS, sinon celui de sa
/// demande.
/// Le total est le nombre de lignes de v2.DEMANDE_EDITION (sans critere, toutes repondent ; aucune jointure ne double
/// une ligne) : un COUNT(*) OVER () trierait toute la table jointe a chaque ouverture (4,3 millions de lignes dans
/// RIG_DEV).
///
/// GROUPES : « Courriel » quand l'edition part par courriel -- son mode d'envoi porte le drapeau Courriel (512,
/// TYPE_MODE_ENVOI, DemandeEditionPourServices.cs:89-97), ou le courriel est deja envoye (DMNED_ID_EMAIL_ENVOYE : la
/// colonne « Envoi par mail » de la grille V2, l. 1483 ; les editions anterieures au mode d'envoi n'ont que lui) ;
/// sinon « File differee » quand elle est rangee dans une file (DMNED_LOT : le critere « File differee » filtre cette
/// colonne, l. 614-617) ; sinon « Courrier ».
///
/// REGLE D'ETAT (DMNED_PHASE_TRAITEMENT, phases de RIG.METIER.DemandeEdition,
/// Outils\DLL\RigMetier\Edition\DemandeEditionPourServices.cs:16-33 ; suffixe T = en traitement, Z = en erreur) :
///   I, IT  initiale                  -> AFaire  « A emettre » : aucune marche ;
///   C, CT  creee                     -> EnCours « En cours » : demande et destinataires (0, 1) ;
///   G, GT  generee ; P, PT prete     -> EnCours : demande, destinataires, document (0, 1, 2) ;
///   E      emise                     -> Validee « Emise » : demande, destinataires, document, impression (0 a 3) ;
///   IZ, CZ, GZ, PZ, EZ en erreur     -> EnCours : les marches de la phase atteinte (au moins la demande, 0), et le
///                                       libelle d'erreur (DMNED_LIBELLE_ERREUR) dans les precisions -- la V2 ecrit ces
///                                       lignes en rouge (l. 855) ;
///   tout autre code                  -> EnCours, demande (0), avec le code de l'ancien RIG dans les precisions.
/// « Supprimee » (Rejetee) n'a pas de ligne : les supprimees ne sont pas lues (ci-dessus).
/// </summary>
internal sealed class LecteurFileEditions : ILecteurEcran
{
    public string Procedure => "PROC_POOLEDITION";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Courrier = "Courrier", Courriel = "Courriel", FileDifferee = "File différée";

    private const string Requete = """
        SELECT TOP (@plafond) de.DMNED_ID_DMNED, de.DMNED_PHASE_TRAITEMENT, de.DMNED_LIBELLE_DOCUMENT, de.DMNED_LIBELLE_ERREUR,
               de.DMNED_DESIGNATION_DESTINATAIRE, de.DMNED_LOT, de.DMNED_DATE_CREATION,
               CASE WHEN de.DMNED_ID_EMAIL_ENVOYE IS NOT NULL OR (ISNULL(de.DMNED_MODE_ENVOI, 0) & 512) <> 0 THEN 1 ELSE 0 END AS ParCourriel,
               de.DMNED_DATE_PRE_EMISSION, de.DMNED_NOMBRE_EXEMPLAIRE, d.DMND_NUM_DEMANDE, s.NumeroGestion, e.Denomination,
               (SELECT COUNT(*) FROM v2.DEMANDE_EDITION) AS Total
        FROM v2.DEMANDE_EDITION AS de
        LEFT JOIN v2.DEMANDE AS d ON d.DMND_ID_DMND = de.DMNED_ID_DMND
        LEFT JOIN rec.DossierRcs AS s ON s.Id = COALESCE(de.DMNED_ID_DSRCS, d.DMND_ID_DSRCS)
        LEFT JOIN rec.Entreprise AS e ON e.Id = s.IdEntreprise
        ORDER BY de.DMNED_DATE_CREATION DESC, de.DMNED_ID_DMNED DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var phase = FormeLigne.Texte(r, "DMNED_PHASE_TRAITEMENT");
        var (etat, faites) = Etat(phase);
        var lot = FormeLigne.Texte(r, "DMNED_LOT");
        var groupe = Convert.ToInt32(r["ParCourriel"]) == 1 ? Courriel : lot.Length > 0 ? FileDifferee : Courrier;
        var destinataire = FormeLigne.Texte(r, "DMNED_DESIGNATION_DESTINATAIRE");
        // DMNED_DATE_CREATION peut manquer (6 576 demandes d'edition dans RIG_DEV, lu le 25/11/2026) : pas de date, pas de mention.
        var date = etat == "Validee"
            ? FormeLigne.DateDe(r, "DMNED_DATE_PRE_EMISSION") ?? FormeLigne.DateDe(r, "DMNED_DATE_CREATION")
            : FormeLigne.DateDe(r, "DMNED_DATE_CREATION");
        var quand = date is null ? null : etat == "Validee" ? $"émise le {FormeLigne.Date(date)}" : $"créée le {FormeLigne.Date(date)}";
        var erreur = phase.EndsWith('Z') ? $"en erreur : {FormeLigne.Texte(r, "DMNED_LIBELLE_ERREUR")}".TrimEnd(' ', ':') : null;
        // Le nombre d'exemplaires : la colonne « Ex. » de la liste V2 (PoolEdition.cs:279, lu l. 1461, un negatif montre 0 l. 1505-1507),
        // apres le destinataire comme dans la V2 ; « Imprimer » le reecrit (EcrivainFileEditions.cs, lot 16). Sans valeur : pas de mention.
        var exemplaires = r["DMNED_NOMBRE_EXEMPLAIRE"] is DBNull ? (int?)null : Math.Max(0, Convert.ToInt32(r["DMNED_NOMBRE_EXEMPLAIRE"]));
        var precisions = FormeLigne.Joindre(FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT"),
                                            destinataire.Length > 0 ? $"à {destinataire}" : null,
                                            exemplaires is { } ex ? $"{ex} ex." : null, quand,
                                            lot.Length > 0 ? $"file « {lot} »" : null, erreur,
                                            Connue(phase) ? null : $"phase « {phase} » de l'ancien RIG");
        var demande = FormeLigne.Texte(r, "DMND_NUM_DEMANDE");
        var nom = FormeLigne.Joindre(FormeLigne.Texte(r, "Denomination"), FormeLigne.NumeroGestion(FormeLigne.Texte(r, "NumeroGestion")),
                                     demande.Length > 0 ? $"demande {demande}" : $"édition n° {FormeLigne.Texte(r, "DMNED_ID_DMNED")}");
        // L'id : la demande d'edition (DMNED_ID_DMNED), que « Imprimer » vise (EcrivainFileEditions.cs, lot 16).
        return new LigneLue(nom, precisions, groupe, etat, faites)
        {
            Id = Convert.ToInt32(r["DMNED_ID_DMNED"]).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>La regle d'etat (en tete du fichier) : etat du descripteur et marches franchies, par phase.</summary>
    internal static (string Etat, int[] Faites) Etat(string phase) => phase switch
    {
        "I" or "IT" => ("AFaire", []),
        "C" or "CT" or "CZ" => ("EnCours", [0, 1]),
        "G" or "GT" or "GZ" or "P" or "PT" or "PZ" or "EZ" => ("EnCours", [0, 1, 2]),
        "E" => ("Validee", [0, 1, 2, 3]),
        _ => ("EnCours", [0]),   // IZ (erreur a la phase initiale) et tout code inconnu : la demande existe
    };

    private static bool Connue(string phase)
        => phase is "I" or "IT" or "IZ" or "C" or "CT" or "CZ" or "G" or "GT" or "GZ" or "P" or "PT" or "PZ" or "E" or "EZ";
}
