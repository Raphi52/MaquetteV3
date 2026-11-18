namespace RigV3Desktop;

// ============================================================
// « Informations des Formalites » : TOUT ce que RIG v2 montre d'une
// formalite RCS dans le processus XACTION (« Corriger toute information
// sur une formalite du registre du commerce », MDPRC_ID_MDPRC = 1499).
//
// Liste GENEREE, meme methode que Metier/InformationsDossier.cs : lecture
// du 15/09/2026 de RIG_DEV@SQL-DEV\DEV, jointure MODELE_PROCESSUS >
// ADAPTATEUR_ETAPE > MODELE_ETAPE > ADAPTATEUR_OPERATION > ADAPTATEUR_ULT.
// Retenu : chaque ULT ACTIVE qui ecrit une colonne, dans les etapes
// XRACTIO (corriger l'action RCS), XBODRECT (rectifier ou annuler un
// Bodacc) et XBODPREC (consulter l'annonce precedente), dedoublonnee par
// table + colonne. Le troisieme champ porte les etapes qui le montrent.
// Ecarte : l'entree dans le processus (E_ENT_ACTR), la liberation des
// numeros (LIBNUM), l'emission CFE (9EMISCFEX), la post-validation
// (9INF_ACT), les pseudo-tables « # » et les cles techniques ID_*.
// ============================================================

public static class InformationsFormalite
{
    /// <summary>Libelle du noeud de l'arbre qui ouvre ce formulaire.</summary>
    public const string LibelleNoeud = "Informations des Formalites";

    /// <summary>Le processus v2 lu pour construire la liste.</summary>
    public static readonly string[] ProcessusSources = ["XACTION"];

    public static int NombreChamps => Sections.Sum(s => s.Champs.Length);

    public static readonly SectionInfoDossier[] Sections =
    [
        new("ACTION_RCS", "Action RCS (formalite)",
        [
            new("date arrivée", "DATE_ARRIVEE", "XRACTIO"),
            new("N° d'arrivée RCS", "NUM_ARRIVEE", "XRACTIO"),
            new("processus appliqué", "CODE_PROSS", "XRACTIO"),
        ]),

        new("BODACC", "Annonce Bodacc",
        [
            new("Type d'annonce", "TYPE_ANNONCE", "XBODPREC XBODRECT"),
            new("BODACC préalable ?", "BODACC_PREALABLE", "XBODPREC XBODRECT"),
            new("N° avis préalable", "NUM_AP", "XBODPREC XBODRECT"),
            new("AP sur NI ?", "AP_SUR_NI", "XBODPREC XBODRECT"),
            new("Type Bodacc XML", "TYPE_BODACC", "XBODPREC XBODRECT"),
            new("code tarif", "TARIF_BO_APPLIQUE", "XBODPREC XBODRECT"),
            new("montant", "COUT_BODACC", "XBODPREC XBODRECT"),
            new("monnaie", "MONNAIE", "XBODPREC XBODRECT"),
            new("état", "ETAT_BODACC", "XBODPREC XBODRECT"),
            new("N° bordereau", "NUM_BORDEREAU", "XBODPREC XBODRECT"),
            new("date création fichier", "DATE_ENVOI", "XBODPREC XBODRECT"),
            new("date transmission", "DATE_ENVOI_TE", "XBODPREC XBODRECT"),
            new("date publication", "DATE_PUBLICITE", "XBODPREC XBODRECT"),
            new("N° parution", "NUM_PARUTION", "XBODPREC XBODRECT"),
            new("N° annonce", "NUMERO_ANNONCE", "XBODPREC XBODRECT"),
            new("N° facture", "NUM_FACTURE", "XBODPREC XBODRECT"),
            new("motif rejet 1", "MOTIF_REJET_BODACC_1", "XBODPREC XBODRECT"),
            new("motif rejet 2", "MOTIF_REJET_BODACC_2", "XBODPREC XBODRECT"),
            new("commentaire rejet", "COMMENTAIRE", "XBODPREC XBODRECT"),
            new("Contrôle qualité", "CONTROLE_QUALITE", "XBODPREC XBODRECT"),
            new("Contrôle qualité vérifié ?", "CONTROLE_QUALITE_VERIFIE", "XBODPREC XBODRECT"),
        ]),

        new("BODACC_RCS_XML", "Bodacc RCS : contenu de l'annonce",
        [
            new("Statut RCS", "STATUT_RCS", "XBODPREC XBODRECT"),
            new("Code greffe", "CODE_GREFFE", "XBODPREC XBODRECT"),
            new("Dénomination", "DENOMINATION_USAGE", "XBODPREC XBODRECT"),
            new("Montant du capital", "MONTANT", "XBODPREC XBODRECT"),
            new("Monnaie", "MONNAIE", "XBODPREC XBODRECT"),
            new("Cap var.", "VARIABLE", "XBODPREC XBODRECT"),
            new("Patronyme", "PATRONYME", "XBODPREC XBODRECT"),
            new("Prénoms", "PRENOMS_ETAT_CIVIL", "XBODPREC XBODRECT"),
            new("Nom d'usage", "NOM_USAGE", "XBODPREC XBODRECT"),
            new("Pseudonyme", "PSEUDONYME", "XBODPREC XBODRECT"),
            new("Nom commercial", "NOM_COMMERCIAL", "XBODRECT"),
            new("Sigle", "SIGLE", "XBODPREC XBODRECT"),
            new("F. juridique", "FORME_JURIDIQUE_UTI", "XBODPREC XBODRECT"),
            new("Activité déclarée", "ACTIVITE_DECLAREE", "XBODPREC XBODRECT"),
            new("Qualité étab", "TYPE_ADRESSE", "XBODPREC XBODRECT"),
            new("Début d'act.", "DATE_DEBUT_ACTIVITE", "XBODPREC XBODRECT"),
            new("Effet", "DATE_EFFET", "XBODRECT"),
            new("Radiation ?", "RADIATION", "XBODRECT"),
            new("Cessation d'act.", "DATE_CESSATION_ACTIVITE", "XBODPREC XBODRECT"),
            new("Clôture", "DATE_CLOTURE", "XBODPREC XBODRECT"),
            new("Origine du fonds", "ORIGINE_FONDS", "XBODPREC XBODRECT"),
            new("Publication", "JOURNAL_DATE", "XBODPREC XBODRECT"),
            new("Journal de publication", "JOURNAL_TITRE", "XBODPREC XBODRECT"),
            new("Oppositions", "OPPOSITIONS", "XBODPREC XBODRECT"),
            new("Déclaration de créances", "DECLARATION_CREANCES", "XBODPREC XBODRECT"),
            new("Descriptif des modifications", "DESCRIPTIF", "XBODPREC XBODRECT"),
            new("Catégorie", "BODACC_CATEGORIE", "XBODPREC XBODRECT"),
        ]),

        new("BODACC_RCS_XML_DET", "Bodacc RCS : detail",
        [
            new("Type de détail", "TYPE_DETAIL", "XBODPREC XBODRECT"),
            new("Administration", "ADMINISTRATION", "XBODPREC XBODRECT"),
            new("NI ?", "NON_INSCRIT", "XBODPREC XBODRECT"),
            new("N° SIREN", "SIREN", "XBODPREC XBODRECT"),
            new("N° GESTION", "NUM_GESTION", "XBODRECT"),
            new("Code greffe", "CODE_GREFFE", "XBODPREC XBODRECT"),
            new("Statut", "STATUT_RCS", "XBODPREC XBODRECT"),
            new("PM ?", "PM", "XBODPREC XBODRECT"),
            new("Dénomination PM", "DENOMINATION_USAGE", "XBODPREC XBODRECT"),
            new("Patronyme PP", "PATRONYME", "XBODPREC XBODRECT"),
            new("Prénoms PP", "PRENOMS", "XBODPREC XBODRECT"),
            new("Nom d'usage PP", "NOM_USAGE", "XBODPREC XBODRECT"),
        ]),
    ];
}
