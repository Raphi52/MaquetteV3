using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « EDI RPVA » (PROC_EDI_RPVAINS) : les demandes d'inscription des avocats recues par le reseau RPVA, en attente du greffe.
/// Mode d'emploi des lecteurs : LecteurEcran.cs.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_EDI_RPVAINS, fiche Brain proc_edi_rpvains.md, sur
/// la base du greffe. A l'ouverture, la recherche part seule (FormRechercheDemandes\PROC_RECHERCHE_EDI_RPVAINS.cs:29-30) avec
/// les criteres remis a zero (OPE_RECHERCHE_EDI_RPVAINS.cs:58-74) : « En attente » cochee, « Traites » decochee, ni CNBF ni
/// designation, 200 resultats. La requete (DataProcDemandeInscr.cs:103-137) devient alors :
///   SELECT TOP 200 * FROM DEMAT_RPJC_INS WHERE (RPJCI_ETAT = '1') ORDER BY RPJCI_DATE_TRAITEMENT_RIG DESC.
/// L'ecran V3 lit cette liste-la, et elle seule : une demande traitee ou refusee n'y est pas (le descripteur n'a d'ailleurs
/// pas d'etat « Rejetee »). Ecart assume : a date de traitement egale (toutes vides pour une demande en attente), le numero
/// de la demande departage, pour un ordre stable.
/// MEME TABLE que « EDI RPJC inscriptions » (LecteurInscriptionsRpjc, PROC_EDI_RPJC_INS) : la V2 avait deux ecrans sur
/// DEMAT_RPJC_INS ; une demande en attente est donc dans les deux membres de l'ecran reuni « Echanges avec les avocats ».
///
/// GROUPES : « Structures » = la demande porte le Cnbf d'une structure (RPJCI_NUMERO_CNBF_STRUCTURE), sinon « Personnes
/// physiques ». La V2 montre ce Cnbf dans sa liste (RigDataGridViewDemandes.cs:30) et ne remplit la grille des structures
/// qu'avec lui (FormTraitement\OPE_TRAITEMENT_RECHERCHE.cs:97-115). RPJCI_PP ne distingue rien : il vaut 1 sur les 3 028
/// demandes de RIG_DEV (lu le 25/11/2026), structure ou non. Le SIREN seul ne fait pas une structure (meme regle que
/// LecteurInscriptionsRpjc).
///
/// REGLE D'ETAT (marches du descripteur : 0 Demande, 1 Infos du demandeur, 2 Mandataires candidats, 3 Courriels, 4 Refus) :
///   RPJCI_USER_EN_COURS renseigne -> EnCours « En cours » : un agent a ouvert la demande (OPE_RESULTAT_EDI_RPVAINS.cs:91-99
///                                    pose son nom et refuse l'ouverture aux autres) ; la fenetre de traitement charge
///                                    aussitot les infos du demandeur (FormTraitement\PROC_TRAITEMENT.cs:111-112, Recharger) :
///                                    demande et infos (0, 1) ;
///   personne                       -> AFaire « En attente » : aucune marche.
/// « Traitee » (Validee) n'arrive pas ici : accepter ou refuser fait sortir la demande de l'etat 1
/// (DataProcDemandeInscr.cs:215-218), donc de la liste.
/// </summary>
internal sealed class LecteurInscriptionsRpva : ILecteurEcran
{
    public string Procedure => "PROC_EDI_RPVAINS";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string PersonnesPhysiques = "Personnes physiques", Structures = "Structures";

    private const string Requete = """
        SELECT TOP (@plafond) d.RPJCI_ID_RPJCI, d.RPJCI_DATE_DEMANDE, d.RPJCI_TYPE_MAJ, d.RPJCI_NUMERO_CNBF, d.RPJCI_NOM,
               d.RPJCI_PRENOM, d.RPJCI_DENOMINATION, d.RPJCI_USER_EN_COURS, d.RPJCI_NUMERO_SIREN, d.RPJCI_NUMERO_CNBF_STRUCTURE,
               COUNT(*) OVER () AS Total
        FROM v2.DEMAT_RPJC_INS AS d
        WHERE d.RPJCI_ETAT = '1'
        ORDER BY d.RPJCI_DATE_TRAITEMENT_RIG DESC, d.RPJCI_ID_RPJCI
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete, Ligne, ct, new SqlParameter("@plafond", plafond));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var cnbfStructure = FormeLigne.Texte(r, "RPJCI_NUMERO_CNBF_STRUCTURE");
        var structure = cnbfStructure.Length > 0;
        var ouvertePar = FormeLigne.Texte(r, "RPJCI_USER_EN_COURS");
        var recue = FormeLigne.Date(FormeLigne.DateDe(r, "RPJCI_DATE_DEMANDE"));

        // La regle d'etat (en tete du fichier).
        (string Etat, int[] Faites, string? Statut) regle = ouvertePar.Length > 0
            ? ("EnCours", [0, 1], $"ouverte par {ouvertePar}")
            : ("AFaire", [], recue.Length > 0 ? $"reçue le {recue}" : null);

        // La designation de la V2 : « NOM Prenom » (RigDataGridViewDemandes.cs:31) ; sans nom, la denomination.
        var nom = $"{FormeLigne.Texte(r, "RPJCI_NOM")} {FormeLigne.Texte(r, "RPJCI_PRENOM")}".Trim();
        if (nom.Length == 0) nom = FormeLigne.Texte(r, "RPJCI_DENOMINATION");
        if (nom.Length == 0) nom = $"Demande n° {FormeLigne.Texte(r, "RPJCI_ID_RPJCI")}";
        var siren = FormeLigne.Texte(r, "RPJCI_NUMERO_SIREN");
        var precisions = FormeLigne.Joindre(
            $"Cnbf {FormeLigne.Texte(r, "RPJCI_NUMERO_CNBF")}",
            structure ? $"structure Cnbf {cnbfStructure}" : null,
            siren.Length > 0 ? $"SIREN {FormeLigne.Siren(siren)}" : null,
            FormeLigne.Texte(r, "RPJCI_TYPE_MAJ") == "M" ? "modification" : null,
            regle.Statut);
        return new LigneLue(nom, precisions, structure ? Structures : PersonnesPhysiques, regle.Etat, regle.Faites);
    }
}
