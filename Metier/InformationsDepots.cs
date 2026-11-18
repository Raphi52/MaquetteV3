namespace RigV3Desktop;

// ============================================================
// « Informations des depots » : TOUT ce que RIG v2 montre d'un depot
// d'acte RCS dans le processus XDAS (« Correction interne d'un depot
// d'acte », MDPRC_ID_MDPRC = 1503).
//
// Liste GENEREE, meme methode que Metier/InformationsDossier.cs : lecture
// du 15/09/2026 de RIG_DEV@SQL-DEV\DEV, jointure MODELE_PROCESSUS >
// ADAPTATEUR_ETAPE > MODELE_ETAPE > ADAPTATEUR_OPERATION > ADAPTATEUR_ULT
// (ADPOP_ID_MDLTP = ADPET_ID_MDLTP, ADPUL_ID_MDOPR = ADPOP_ID_MDOPR).
// Retenu : chaque ULT ACTIVE qui ecrit une colonne, dans les etapes
// XEVRCS, XRDAS, XACT, XRACT_NS, XRACT_S et BODDAS, dedoublonnee par
// table + colonne. Le troisieme champ porte les etapes qui le montrent.
// Libelles = ADPUL_LIBELLE_ECRAN tels quels ; pour INPI_DATE_REJET, v2
// porte deux ULT (condition + saisie) : le libelle de saisie est garde.
// Ecarte : l'entree (E_ACTRC), la demande (1GDMNX), le client (1CLIENTN),
// les finitions (4RXDAS), l'historisation (9HISTODEP), la post-validation
// (9INF_XDP), la sauvegarde / verification du client (9VALCLI, 9VERCLIN),
// la facturation (XDASBOD), les pseudo-tables « # », les colonnes NULL,
// ETAT_INFO et les cles techniques ID_*. Les deux adresses du Bodacc
// (ID_ADRBS_SP, ID_ADRBS_EP) sont gardees : ce sont des champs « Adresse ».
// ============================================================

public static class InformationsDepots
{
    /// <summary>Libelle du noeud de l'arbre qui ouvre ce formulaire.</summary>
    public const string LibelleNoeud = "Informations des depots";

    /// <summary>Le processus v2 lu pour construire la liste.</summary>
    public static readonly string[] ProcessusSources = ["XDAS"];

    public static int NombreChamps => Sections.Sum(s => s.Champs.Length);

    public static readonly SectionInfoDossier[] Sections =
    [
        new("DEPOT_RCS", "Depot RCS",
        [
            new("date de dépôt", "DATE", "XRDAS"),
            new("N° de dépôt", "NUM_DEPOT", "XRDAS"),
            new("catégorie de dépôt", "CATEGORIE_DEPOT", "XRDAS"),
            new("numérisé", "NUMERISE", "XRDAS"),
            new("Dépôt INPI Gratuit ?", "GRATUIT", "XRDAS"),
            new("non transmission a l'inpi", "ENVOI_INPI", "XRDAS"),
            new("mode saisie simplifiée", "SAISIE_SIMPLIFIEE", "XACT"),
        ]),

        new("EVENEMENT_RCS", "Evenements RCS",
        [
            new("N° d'ordre", "NUM_ORDRE", "XEVRCS"),
            new("code évènement RCS", "CODE_EVENEMENT_RCS", "XEVRCS"),
            new("date d'effet", "DATE_EFFET", "XEVRCS"),
            new("fait partie de l'objet du dépôt", "OBJET_DEPOT", "XEVRCS"),
            new("code précision événement", "CODE_PRECISION_EVENEMENT", "XEVRCS"),
            new("texte événement", "TEXTE_EVENEMENT", "XEVRCS"),
        ]),

        new("ACTE_DEPOSE", "Actes deposes",
        [
            new("N° d'ordre", "NUM_ORDRE", "XRACT_NS XRACT_S"),
            new("abrégé de la pièce", "ABREGE_PIECE", "XRACT_NS XRACT_S"),
            new("nb exemplaires", "NOMBRE", "XRACT_NS XRACT_S"),
            new("forme de la pièce", "FORME_PIECE", "XRACT_NS XRACT_S"),
            new("nature de l'acte", "NATURE_ACTE", "XRACT_NS XRACT_S"),
            new("date de la pièce", "DATE", "XRACT_NS XRACT_S"),
            new("description", "DESCRIPTION", "XRACT_NS XRACT_S"),
            new("nb pages", "NBR_PAGES", "XRACT_NS XRACT_S"),
            new("commandable", "COMMANDABLE", "XRACT_NS XRACT_S"),
            new("état pièce", "ETAT_PIECE", "XRACT_NS XRACT_S"),
            new("état Inpi", "INPI_ETAT", "XRACT_NS XRACT_S"),
            new("Inpi Etat Data", "INPI_ETAT_DATA", "XRACT_NS XRACT_S"),
        ]),

        new("OBJET_ACTE", "Objets de l'acte",
        [
            new("numéro d'ordre", "NUM_ORDRE", "XRACT_NS"),
            new("évènement RCS", "CODE_EVENEMENT_RCS", "XRACT_NS"),
            new("Précision évènement", "CODE_PRECISION_EVENEMENT", "XRACT_NS"),
            new("date d'éffet", "DATE_EFFET", "XRACT_NS"),
            new("texte évènement", "TEXTE_EVENEMENT", "XRACT_NS"),
        ]),

        new("INPI_TRANS_DEPOT_IMR", "Transmission INPI",
        [
            new("Envoyé le", "DATE_ENVOI", "XRACT_S"),
            new("AR Docaposte", "DATE_AR", "XRACT_S"),
            new("reçu Inpi", "INPI_DATE_RECU", "XRACT_S"),
            new("Intégré Inpi", "INPI_DATE_INTGR", "XRACT_S"),
            new("date de rejet", "INPI_DATE_REJET", "XRACT_S"),
            new("motif du rejet", "MOTIF_REJET", "XRACT_S"),
        ]),

        new("DOSSIER_RCS", "Dossier RCS (Bodacc)",
        [
            new("siren", "SIREN", "BODDAS"),
            new("n° de gestion", "NUM_GESTION", "BODDAS"),
        ]),

        new("BODACC", "Annonce Bodacc",
        [
            new("type d'annonce", "TYPE_ANNONCE", "BODDAS"),
            new("Type de Bodacc XML", "TYPE_BODACC", "BODDAS"),
            new("code tarif", "TARIF_BO_APPLIQUE", "BODDAS"),
            new("montant", "COUT_BODACC", "BODDAS"),
            new("monnaie", "MONNAIE", "BODDAS"),
            new("état", "ETAT_BODACC", "BODDAS"),
            new("N° bordereau", "NUM_BORDEREAU", "BODDAS"),
            new("date bordereau", "DATE_ENVOI", "BODDAS"),
            new("date transmission", "DATE_ENVOI_TE", "BODDAS"),
            new("N° annonce", "NUMERO_ANNONCE", "BODDAS"),
        ]),

        new("BODACC_RCS_XML", "Annonce Bodacc RCS (XML)",
        [
            new("Statut RCS", "STATUT_RCS", "BODDAS"),
            new("Code greffe", "CODE_GREFFE", "BODDAS"),
            new("Dénomination", "DENOMINATION_USAGE", "BODDAS"),
            new("Montant du capital", "MONTANT", "BODDAS"),
            new("Monnaie", "MONNAIE", "BODDAS"),
            new("Cap var.", "VARIABLE", "BODDAS"),
            new("Patronyme", "PATRONYME", "BODDAS"),
            new("Prénoms", "PRENOMS_ETAT_CIVIL", "BODDAS"),
            new("Nom d'usage", "NOM_USAGE", "BODDAS"),
            new("Pseudonyme", "PSEUDONYME", "BODDAS"),
            new("Nom commercial", "NOM_COMMERCIAL", "BODDAS"),
            new("Sigle", "SIGLE", "BODDAS"),
            new("F. juridique", "FORME_JURIDIQUE_UTI", "BODDAS"),
            new("Activité déclarée", "ACTIVITE_DECLAREE", "BODDAS"),
            new("Adresse du siège", "ID_ADRBS_SP", "BODDAS"),
            new("Adresse de l'établissement", "ID_ADRBS_EP", "BODDAS"),
            new("Qualité étab", "TYPE_ADRESSE", "BODDAS"),
            new("Début d'act.", "DATE_DEBUT_ACTIVITE", "BODDAS"),
            new("Effet", "DATE_EFFET", "BODDAS"),
            new("Radiation ?", "RADIATION", "BODDAS"),
            new("Cessation d'act.", "DATE_CESSATION_ACTIVITE", "BODDAS"),
            new("Clôture", "DATE_CLOTURE", "BODDAS"),
            new("Origine du fonds", "ORIGINE_FONDS", "BODDAS"),
            new("Publication", "JOURNAL_DATE", "BODDAS"),
            new("Journal de publication", "JOURNAL_TITRE", "BODDAS"),
            new("Oppositions", "OPPOSITIONS", "BODDAS"),
            new("Déclaration de créances", "DECLARATION_CREANCES", "BODDAS"),
            new("Descriptif des modifications", "DESCRIPTIF", "BODDAS"),
            new("Catégorie", "BODACC_CATEGORIE", "BODDAS"),
        ]),
    ];
}
