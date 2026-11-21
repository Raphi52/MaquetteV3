namespace RigV3.Serveur.Recopie;

/// <summary>
/// LES TABLES DE REC-001 (tables_v2 de la story, carnet\recopie.json), recopiees TELLES QUELLES dans le schema v2 de la
/// base V3 en attendant la regle colonne par colonne de la story qui en repond (en commentaire) : celle-ci REMPLACE la
/// ligne ci-dessous, elle ne s'y ajoute pas. Depuis le lot 10, ACTION_RCS, ADRESSE_BASE, CATEGORIE, CLIENT, DOSSIER_RCS et
/// INSCRIPTION ont leur table dans la V3 (REC-012 a REC-019) : leur regle est dans FichesV3.cs. UTILISATEUR, aussi dans
/// tables_v2, est porte par la reprise des comptes
/// (ReprisesPhase1.cs). Colonnes attendues relevees dans RIG_DEV (SQL-DEV\DEV) le 2026-11-21 par
/// « RigV3.Transfert inventaire » ; un lien casse herite est marque, jamais corrige (CA04, regle « a valider »).
/// </summary>
internal sealed class ReglesRec001 : IReglesTransfert
{
    public IEnumerable<RegleTable> Regles =>
    [
        // REC-010 (historique), domaine des affaires
        RegleTable.TelleQuelle("AFFAIRE", "REC-001",
            "AFFRE_ID_UTLST_CREATION", "AFFRE_ID_UTLST_EN_COURS", "AFFRE_DATE_CREATION", "AFFRE_LIBELLE", "AFFRE_ID_AFFRE",
            "AFFRE_NOTE_INTERNE", "AFFRE_INPI_MONTANT_TTC", "AFFRE_WALLI_MONTANT_TTC", "AFFRE_ID_CLIEN"),
        // REC-011 (demandes liees), domaine des demandes
        RegleTable.TelleQuelle("DEMANDE", "REC-001",
            "DMND_ID_INSCR", "DMND_ID_CTGR", "DMND_ID_UTLST", "DMND_ID_ACTRC", "DMND_ID_ARRV", "DMND_ID_DSRCS",
            "DMND_ID_AUTRE", "DMND_ID_FMEDT", "DMND_ID_DMND_INHIBITRICE", "DMND_ID_AFFRE", "DMND_NUM_DEMANDE", "DMND_NOM",
            "DMND_NUM_INDICATIF", "DMND_ETAT_DEMANDE", "DMND_EN_COURS", "DMND_CODE_PROSS", "DMND_LIBELLE",
            "DMND_LIBELLE_ECRAN", "DMND_FOCUS", "DMND_ETAT_PROSS", "DMND_ARRIVEE_SAISIE", "DMND_EDITION",
            "DMND_NUM_LIAISON", "DMND_NOM_UTILISATEUR", "DMND_LES_NUMEROS", "DMND_INHIBEE", "DMND_TEXTE_INHIBITEUR",
            "DMND_FACTURE", "DMND_CODE_PARTENAIRE", "DMND_ID_DMND", "DMND_TEXTE_PARAMETRE", "DMND_ID_ADRBS",
            "DMND_DATE_VALIDATION", "DMND_DATE_CREATION", "DMND_DATE_MODIFICATION", "DMND_INTERROMPU",
            "DMND_ID_CLIENT_EXPEDITION", "DMND_ID_DMND_MERE", "DMND_ID_ACTION_2"),
        // REC-018
        RegleTable.TelleQuelle("ARRIVEE", "REC-001",
            "ARRV_NUM_ARRIVEE", "ARRV_MODE_ARRIVEE", "ARRV_DATEHEURE", "ARRV_NOM", "ARRV_REFERENCES",
            "ARRV_PAIEMENT_JOINT", "ARRV_CASE_COURRIER", "ARRV_ID_ARRV"),
    ];
}
