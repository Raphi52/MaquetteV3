using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPJC inscriptions » (PROC_EDI_RPJC_INS) : les demandes d'inscription des avocats recues par le flux RPJC.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\PROC_EDI_RPJC_INS. L'ecran d'entree est frmChoixTrt
/// (GestionForms.PremierChargementForms), sur la base du greffe : SELECT TOP 100 * FROM DEMAT_RPJC_INS, avec deux cases
/// (frmChoixTrt.cs:102-152) : « En attente » (RPJCI_ETAT = '1', cochee a l'ouverture) OU « Traites » (RPJCI_ETAT <> '1',
/// date du traitement facultative, aucune par defaut), les traites du plus recent au plus ancien
/// (ORDER BY RPJCI_DATE_TRAITEMENT_RIG DESC). L'ecran V3 a les etats des deux listes (descripteur : En attente, En cours,
/// Traitee, Rejetee) : il lit la liste des deux cases cochees, sans date -- une demande sans etat n'est dans aucune.
/// Ecart assume : les demandes en attente d'abord (dans l'ordre de leur numero, celui de la liste « En attente » de la
/// V2), puis les traitees du plus recent au plus ancien ; le plafond coupe donc les plus anciennes traitees.
/// RPJCI_ETAT suit eDematEtat (Outils\DLL\RigMetier\JUDICIAIRE\DematEtat.cs) : 1 integre (attend le greffe), 2 traite par
/// le greffe, 3 envoye, 4 valide par Extelia, 8 erreur d'envoi, 9 rejete par Extelia.
///
/// GROUPES : « Avocat et structure » = la demande porte le Cnbf d'une structure (RPJCI_NUMERO_CNBF_STRUCTURE) ; sinon
/// « Avocat seul ». La V2 ne traite la structure que par ce Cnbf (frmTrt.cs:380-421 et 547 ; sans lui, le cadre structure
/// est replie, l. 163-167). Le SIREN seul n'en fait pas une : la plupart des avocats en ont un a titre individuel.
/// fix-ok: RIG_DEV (25/11/2026) : 3 016 demandes sur 3 028 ont un SIREN, dont 1 288 sans Cnbf de structure ; compter le
/// SIREN mettait 99,6 % des demandes dans « Avocat et structure » (route du serveur de dev : 200 sur 200).
///
/// REGLE D'ETAT (marches du descripteur : 0 Demande, 1 Structure, 2 Mandataire, 3 Rejet) :
///   RPJCI_ETAT_TRAITEMENT_RIG = 'A'        -> Validee « Traitee » : frmTrt.cs:605-607 pose A en creant ou retrouvant le
///                                            mandataire : demande et mandataire (0, 2), plus la structure (1) d'une
///                                            demande qui en porte une ;
///   RPJCI_ETAT_TRAITEMENT_RIG = 'R'        -> Rejetee « Rejetee » : frmTrt.cs:684-687 (motif RPJCI_MOTIF_TRAITEMENT_RIG) :
///                                            demande et rejet (0, 3) ;
///   pas de decision, RPJCI_USER_EN_COURS   -> EnCours « En cours » : un agent l'a ouverte (frmChoixTrt.cs:255-260 pose
///                                            son nom et refuse l'ouverture aux autres) : demande choisie (0) ;
///   pas de decision, personne              -> AFaire « En attente » : aucune marche.
/// Une demande traitee puis rejetee par Extelia (RPJCI_ETAT = '9') garde sa decision ; ses precisions le disent.
/// </summary>
internal sealed class LecteurInscriptionsRpjc : ILecteurEcran
{
    public string Procedure => "PROC_EDI_RPJC_INS";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string AvocatSeul = "Avocat seul", AvecStructure = "Avocat et structure";

    private const string Requete = """
        SELECT TOP (@plafond) i.RPJCI_ID_RPJCI, i.RPJCI_ETAT, i.RPJCI_ETAT_TRAITEMENT_RIG, i.RPJCI_DATE_TRAITEMENT_RIG,
               i.RPJCI_MOTIF_TRAITEMENT_RIG, COALESCE(i.RPJCI_DATE_DEMANDE, i.RPJCI_DATE_CREATION) AS DateRecue,
               i.RPJCI_TYPE_MAJ, i.RPJCI_NUMERO_CNBF, i.RPJCI_NOM, i.RPJCI_PRENOM, i.RPJCI_DENOMINATION,
               i.RPJCI_USER_EN_COURS, i.RPJCI_NUMERO_CNBF_STRUCTURE,
               COUNT(*) OVER () AS Total
        FROM v2.DEMAT_RPJC_INS AS i
        WHERE i.RPJCI_ETAT = '1' OR i.RPJCI_ETAT <> '1'
        ORDER BY CASE WHEN i.RPJCI_ETAT = '1' THEN 0 ELSE 1 END,
                 CASE WHEN i.RPJCI_ETAT = '1' THEN i.RPJCI_ID_RPJCI END,
                 i.RPJCI_DATE_TRAITEMENT_RIG DESC, i.RPJCI_ID_RPJCI DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var cnbfStructure = FormeLigne.Texte(r, "RPJCI_NUMERO_CNBF_STRUCTURE");
        var structure = cnbfStructure.Length > 0;
        var decision = FormeLigne.Texte(r, "RPJCI_ETAT_TRAITEMENT_RIG");
        var ouvertePar = FormeLigne.Texte(r, "RPJCI_USER_EN_COURS");
        var traitee = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCI_DATE_TRAITEMENT_RIG"));
        var recue = FormeLigne.Date(FormeLigne.DateDe(r, "DateRecue"));

        // La regle d'etat (en tete du fichier).
        (string Etat, int[] Faites, string Statut) regle = decision switch
        {
            "A" => ("Validee", structure ? [0, 1, 2] : [0, 2], $"inscrite le {traitee}"),
            "R" => ("Rejetee", [0, 3], FormeLigne.Joindre($"rejetée le {traitee}", FormeLigne.Texte(r, "RPJCI_MOTIF_TRAITEMENT_RIG"))),
            _ when ouvertePar.Length > 0 => ("EnCours", [0], $"ouverte par {ouvertePar}"),
            _ => ("AFaire", [], recue.Length > 0 ? $"reçue le {recue}" : ""),
        };

        // Le demandeur : la V2 montre la denomination, sinon « NOM PRENOM » (frmChoixTrt.cs:186-190) ; ici les deux.
        var nom = FormeLigne.Joindre($"{FormeLigne.Texte(r, "RPJCI_NOM")} {FormeLigne.Texte(r, "RPJCI_PRENOM")}".Trim(),
                                     FormeLigne.Texte(r, "RPJCI_DENOMINATION"));
        if (nom.Length == 0) nom = $"Demande n° {FormeLigne.Texte(r, "RPJCI_ID_RPJCI")}";
        var precisions = FormeLigne.Joindre(
            $"Cnbf {FormeLigne.Texte(r, "RPJCI_NUMERO_CNBF")}",
            structure ? $"structure Cnbf {cnbfStructure}" : null,
            FormeLigne.Texte(r, "RPJCI_TYPE_MAJ") == "M" ? "modification" : null,
            regle.Statut,
            FormeLigne.Texte(r, "RPJCI_ETAT") == "9" ? "réponse rejetée par Extelia" : null);
        // L'id : la demande (RPJCI_ID_RPJCI), que « Traiter la demande » et « Rejeter la demande » visent (EcrivainInscriptionsRpjc.cs, lot 16).
        return new LigneLue(nom, precisions, structure ? AvecStructure : AvocatSeul, regle.Etat, regle.Faites)
        {
            Id = Convert.ToInt32(r["RPJCI_ID_RPJCI"], System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
