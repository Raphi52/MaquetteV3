namespace RigV3Desktop.Pont;

// ============================================================
// LE CATALOGUE DES ECRANS PORTES DEPUIS RIG v2 : le code de chaque ecran et le nom
// que montre son onglet, par famille. Il n'est pas affiche tel quel : la session de
// la page (SessionPlanTravail.Groupes) s'en sert d'aiguillage, d'inventaire et de
// source des noms ; la palette et l'ecran Demandes y lisent le nom d'un ecran.
//
// Sorti de Pont/PlanTravail.cs le 20/11/2026 (plan « Vue du dossier », lot 6) : l'ecran
// Demandes nomme son traitement par l'ecran (« Depots judiciaires », pas PROC_DEPOTJUD),
// et lire ce nom dans la session faisait dependre Pont/EcranDemandes.cs de toute la
// session -- SaisieCheck et SimuEdiCheck, qui le compilent seul, ne compilaient plus.
// Ce fichier ne depend que du menu de l'accueil (Pont/MenuAccueil.cs).
// ============================================================

/// <summary>Un ecran du catalogue : son code et le nom de son onglet.</summary>
public sealed record EcranPorte(string Code, string Libelle);

/// <summary>Une famille d'ecrans (RCS, Judiciaire...) et son statut de portage.</summary>
public sealed record FamilleEcrans(string Code, string Libelle, string Statut, EcranPorte[] Ecrans);

public static class CatalogueEcrans
{
    public static readonly FamilleEcrans[] Familles =
    [
        new("RCS", "RCS", "action",
        [
            new("OUVERTURE_DOSSIER", "Ouverture d'un dossier RCS"),
            new("PROC_RECHAFF",   "Recherche d'affaires"),
            new("PROC_DEMANDE",   "Demandes"),
            new("PROC_REQUI",     "Requisitions - commande de documents"),
            new("PROC_COMMANDES_WEB", "Commandes web"),
            new("PROC_ARRIVEE",   "Arrivee du courrier"),
            new("PROC_ACTES_NUM", "Actes numerises"),
            new("PROC_CLOT",      "Cloture"),
            new("PROC_ARCHIRAD",  "Archivage / radiation"),
            new("BODACC",         "BODACC")
        ]),
        new("JUD", "Judiciaire", "attente",
        [
            new("PROC_DEPOTJUD",   "Depots judiciaires"),
            new("PROC_ACTEUR_JUD", "Acteurs"),
            new("PROC_CJUD",       "Consultation judiciaire"),
            new("PROC_AUDIENCE",   "Audiences"),
            new("PROC_GESTAUDS",   "Gestion des audiences"),
            new("PROC_EXPERT",     "Experts"),
            new("PROC_GEDJUD",     "GED judiciaire"),
            new("PROC_REDA",       "Redaction d'une decision")
        ]),
        new("GED", "Documents", "ok",
        [
            new("PROC_DOC_DEMAT",        "Documents demateralises"),
            new("PROC_CONTROLE_VIDIGED", "Controle VidiGED"),
            new("PROC_COFFRE_FORT",      "Coffre-fort"),
            new("PROC_TELETRANS",        "Teletransmissions (EDI)")
        ]),
        new("FAC", "Facturation", "ok",
        [
            new("PROC_FACTURE", "Factures"),
            new("PROC_ENCAISS", "Encaissements"),
            new("PROC_CREANCE", "Creances"),
            new("PROC_FCLIENT", "Fiches clients"),
            new("PROC_GCLIENT", "Gestion clients"),
            new("PROC_COMPTABILISATION", "Comptabilisation")
        ]),
        new("ADM", "Administration", "attente",
        [
            new("PROC_ACCUEIL_PARAM", "Parametres d'accueil"),
            new("PROC_TOUCHES",       "Ancienne touche, nouvelle touche"),
            new("PROC_ACTUALITES", "Actualites"),
            new("PROC_UTILISATEUR",   "Comptes des agents"),
            new("PROC_COMPTEURS_NUM", "Compteurs de numeros"),
            new("PROC_REGLAGES_GREFFE", "Reglages du greffe"),
            new("PROC_NUMEROS_MANQUANTS", "Numeros manquants"),
            new("PROC_SUPERVISION",   "Supervision"),
            new("PROC_TACHES_LOT",    "Taches en lot"),
            new("PROC_VERROUS",       "Dossiers verrouilles"),
            new("PROC_STAT",       "Statistiques"),
            new("PROC_GESTEMAIL",     "Messagerie"),
            new("PROC_MAIL_LOT1",     "Courriels envoyes"),
            new("PROC_GESTBANQ",      "Banques"),
            new("PROC_EDIT_MAQUETTE", "Maquettes d'edition"),
            new("PROC_COURRIER_DIVERS", "Courriers divers")
        ])
    ];

    /// <summary>Le nom d'un ecran tel que son onglet l'affiche, sinon tel que le menu de
    /// l'accueil le nomme ; vide s'il n'est ni l'un ni l'autre. La palette et l'ecran
    /// Demandes lisent cette meme regle : jamais le code a l'ecran.</summary>
    public static string Nom(string code)
        => code.Length == 0 ? ""
         : Familles.SelectMany(f => f.Ecrans).FirstOrDefault(e => e.Code == code)?.Libelle
           ?? MenuAccueil.Moments.SelectMany(m => m.Sous).SelectMany(s => s.Entrees)
                  .FirstOrDefault(x => x.Code == code)?.Libelle
           ?? "";
}
