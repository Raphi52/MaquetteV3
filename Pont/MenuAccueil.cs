namespace RigV3Desktop.Pont;

// ============================================================
// LE MENU DE GAUCHE DE L'ACCUEIL : le catalogue des GESTES du greffe.
//
// Deplace TEL QUEL de Components/Pages/Home.razor (lot 1 du plan
// « finir le passage a React », 19/11/2026) : le menu est rendu par
// web/src/composants/Accueil.tsx, son etat vit dans Pont/Accueil.cs
// (SessionAccueil). Home.razor le relit encore pour ses aiguillages
// (motif d'un ecran a venir, tache voisine) par un champ qui pointe ici.
//
// Le tableau garde son indentation fixe (geste 8 espaces, verbe 12,
// entree 16) : tests/MenuGestesCheck le lit ligne a ligne.
// ============================================================

/// <summary>Une entree du panneau de gauche, nommee par le BESOIN et non par
/// l'outil. « Procedure » est un mot de l'architecture de RIG v2 : l'agent ne
/// cherche pas « PROC_ARRIVEE », il cherche le courrier arrive ce matin.
/// <paramref name="Detail"/> dit ce qu'on y trouve, pour qu'on n'ait pas a
/// ouvrir pour savoir.</summary>
public sealed record EntreeMenu(string Libelle, string Detail, string Code);

/// <summary><paramref name="Teinte"/> nomme la couleur du groupe (le CSS
/// porte les valeurs), <paramref name="Signe"/> le symbole de sa pastille.
/// La couleur n'est pas decorative : elle dit dans quel monde on est.</summary>
public sealed record MomentMenu(string Titre, string Teinte, string Signe, SousMenu[] Sous)
{
    /// <summary>Le compte affiche a droite du metier : le nombre d'ECRANS,
    /// pas de sous-menus — c'est ce que l'utilisateur va chercher.</summary>
    public int NombreEcrans => Sous.Sum(s => s.Entrees.Length);
}

/// <summary>Un sous-menu : un VERBE, et les ecrans qui le servent. Il coupe
/// un metier long (le judiciaire en compte neuf) en paquets de deux a
/// quatre lignes, la taille qu'un oeil lit sans balayer.</summary>
public sealed record SousMenu(string Titre, EntreeMenu[] Entrees);

public static class MenuAccueil
{
    /// <summary>Le panneau suit l'ORGANISATION du greffe : le RCS et le
    /// JUDICIAIRE sont deux metiers distincts, qu'un meme agent pratique
    /// rarement ensemble. Les ecrans transverses (documents, comptabilite)
    /// sortent des deux. Quelques ecrans apparaissent dans deux groupes :
    /// c'est VOULU, on les cherche des deux cotes. Chaque metier se coupe
    /// ensuite en VERBES : ce qu'on vient y FAIRE.</summary>
    public static readonly MomentMenu[] Moments =
    [
        new("Ouvrir", "rcs", "+",
        [
            new("Creer une affaire",
            [
                new("Nouvelle instance",      "saisir une affaire judiciaire",      "PROC_XAFF"),
                new("Nouvelle formalite",     "ouvrir une procedure collective",    "PROC_NF"),
                new("Nouvelle proc. collective", "PC ou prevention",                "PROC_NPC"),
                new("Injonction de payer",    "creer une IP",                       "PROC_IPE"),
                new("Enrolement judiciaire",  "mettre l'affaire au role",           "PROC_CJUD"),
                new("Mesure provisoire",      "inscrire une mesure en instance",    "PROC_MESIN"),
                new("Jonction d'instances",   "joindre deux affaires",              "PROC_JOIN"),
                new("Ouvrir un dossier",      "une societe arrive au registre",     "OUVERTURE_DOSSIER")
            ]),
            new("Recevoir",
            [
                new("Courrier du jour",       "le papier entre ce matin",           "PROC_ARRIVEE"),
                new("Demandes recues",        "ce qu'on attend de moi",             "PROC_DEMANDE"),
                new("Creation via Infogreffe","enrolement recu par RPJC",           "PROC_CJUDINFOG"),
                new("Ordonnance d'enrolement","ordonnance RCS",                     "PROC_CORDJRCS"),
                new("Requisitions",           "commander des documents",            "PROC_REQUI"),
                new("Commandes web",          "traiter les commandes recues",       "PROC_COMMANDES_WEB")
            ]),
            new("Enregistrer un depot",
            [
                new("Depots au tribunal",     "les depots judiciaires",             "PROC_DEPOTJUD"),
                new("Depots d'actes RCS",     "actes deposes au registre",          "PROC_DAS"),
                new("Depots d'evenement PC",  "evenement de PC ou prevention",      "PROC_DEP"),
                new("Depots PC en masse",     "traiter un lot de depots",           "PROC_DEPPCM"),
                new("Depots beneficiaires",   "actes BEN",                          "PROC_DASBEN")
            ])
        ]),
        new("Trouver", "jud", "?",
        [
            new("Une affaire",
            [
                new("Mes affaires",           "tout ce qui m'est confie",           "PROC_RECHAFF"),
                new("Dossier RCS",            "voir un dossier du registre",        "PROC_VDOSSIER"),
                new("Dossier judiciaire",     "instance, PC ou prevention",         "PROC_VJUD"),
                new("Registres judiciaires",  "les registres du greffe",            "PROC_REGJUD"),
                new("Navires",                "recherche de bateaux",               "PROC_RECHERCHE_BATEAU")
            ]),
            new("Les documents",
            [
                new("Documents",              "chercher dans la GED",               "PROC_RECHERCHE_GED"),
                new("Documents de l'affaire", "la GED de l'affaire",                "PROC_DOC_DEMAT"),
                new("Documents judiciaires",  "la GED du judiciaire",               "PROC_GEDJUD"),
                new("Actes numerises",        "ce que le scanner a produit",        "PROC_ACTES_NUM"),
                new("Controle des scans",     "verifier avant de valider",          "PROC_CONTROLE_VIDIGED"),
                new("Coffre-fort",            "les pieces mises a l'abri",          "PROC_COFFRE_FORT"),
                new("Voir un PDF",            "lire une piece",                     "PROC_PDF"),
                new("Voir une image",         "image ou PDF multi-pages",           "PROC_IMAGE")
            ]),
            new("Un chiffre, un reglement",
            [
                new("Chiffres d'un dossier",  "donnees financieres RCS",            "PROC_VCHIFFRES"),
                new("Encaissements",          "retrouver un reglement",             "PROC_RECHENC"),
                new("Tarifs",                 "articles tarifaires RCS",            "PROC_TARIFS"),
                new("Bilans en rejet",        "comptes annuels refuses",            "PROC_REJETS_BILANS")
            ]),
            new("Hors du greffe",
            [
                new("Interdits de gerer",     "consultation FNIG",                  "PROC_VFNIG"),
                new("FIBEN",                  "fichier bancaire des entreprises",   "PROC_FIBEN"),
                new("Notifications",          "echanges entre greffes",             "PROC_VNOTIF"),
                new("Tribunal digital",       "ce qui vient du portail",            "PROC_VTRID"),
                new("Rechercher un beneficiaire", "consultation BEN",               "PROC_BEN")
            ])
        ]),
        new("Modifier", "mod", "~",
        [
            new("L'affaire",
            [
                new("Instance judiciaire",    "corriger une instance",              "PROC_MJUD"),
                new("Procedure collective",   "corriger une PC ou prevention",      "PROC_MPC"),
                new("Mentions en marge",      "MMA du registre",                    "PROC_MMA"),
                new("Diligences RCS",         "les diligences d'un dossier",        "PROC_MDIL"),
                new("Mots directeurs",        "qualification judiciaire",           "PROC_MDJUD"),
                new("Repertoire",             "corriger le repertoire",             "PROC_MODIF_REPER"),
                new("Parties d'une instance", "qui s'oppose a qui",                 "PROC_PARTIES"),
                new("Beneficiaires d'un dossier", "les BE d'une societe",           "PROC_BE")
            ]),
            new("Defaire",
            [
                new("Liens entre PC",         "rattacher ou detacher",              "PROC_XLIEN"),
                new("Supprimer une instance", "et rendre les numeros",              "PROC_XXAFF"),
                new("Liberer une affaire",    "rendre la main sur un dossier",      "PROC_LIBAFFAIRE")
            ]),
            new("En masse",
            [
                new("Changer le juge-commissaire", "sur plusieurs PC",              "PROC_CHM"),
                new("Changer les organes",    "mandataires sur un lot de PC",       "PROC_CHORGANESENMASSE"),
                new("Debiteurs multiples",    "plusieurs debiteurs sur une PC",     "PROC_DEBITEURS_MULTIPLES")
            ]),
            new("Rediger et signer",
            [
                new("Redaction",              "ecrire une decision",                "PROC_REDA"),
                new("Redaction en masse",     "rediger un lot de decisions",        "PROC_REDA_MASSE"),
                new("Destinataires",          "a qui part la decision",             "PROC_DEST_REDA"),
                new("Signature electronique", "signer les decisions",               "PROC_SIGNJUD"),
                new("Ordonnances a signer",   "la pile a signer",                   "PROC_ORDOSIGN"),
                new("Occultations",           "masquer avant diffusion",            "PROC_DJ_OCCULTATIONS"),
                new("Impression legale",      "jugements en masse",                 "PROC_IMPLEG")
            ]),
            new("Editer un courrier",
            [
                new("Courriers divers",       "les lettres types du greffe",        "PROC_COURRIER_DIVERS"),
                new("Courriers en masse",     "un courrier pour un lot",            "PROC_COURRIERM"),
                new("Editions differees",     "ce qui partira plus tard",           "PROC_EDT_DIFF"),
                new("Pivoter des pages",      "redresser un scan",                  "PROC_PDF_WRITER"),
                new("Documents ocerises",     "numerotation des pages",             "PROC_NUM")
            ]),
            new("Envoyer",
            [
                new("Transmission",           "envoyer un document dematerialise",  "PROC_TRANSDOC"),
                new("Bordereaux LR",          "recommandes du jour",                "PROC_BORDLR"),
                new("Retours LRAR",           "accuses de reception",               "PROC_RETOURLRAR"),
                new("Envoi JAL / BODACC",     "ce qui part en publication",         "PROC_ENV_JAL"),
                new("Publications BODACC",    "gerer les annonces",                 "PROC_GESTBOD"),
                new("File d'impression",      "ce qui attend l'imprimante",         "PROC_PAPERCUT"),
                new("Export Excel",           "sortir une liste",                   "PROC_LISTEEXCEL")
            ])
        ]),
        new("Surveiller", "surv", "!",
        [
            new("Preparer les audiences",
            [
                new("Roles et cabinets",      "organiser les audiences a venir",    "PROC_GESTAUDS"),
                new("Composition",            "qui siege ce jour-la",               "PROC_COMPO"),
                new("Preparation d'audience", "plumitif avant l'audience",          "PROC_PREAUD"),
                new("Convocations",           "convoquer les parties",              "PROC_GCONV"),
                new("Mise au point chambres", "audiences de cabinet",               "PROC_MPCS")
            ]),
            new("Suivre les audiences",
            [
                new("Audiences",              "les dates qui approchent",           "PROC_AUDIENCE"),
                new("Retour d'audience",      "saisir ce qui a ete dit",            "PROC_RETAUD"),
                new("Deliberes",              "ce qui reste a rendre",              "PROC_LISTE_DELIBERES"),
                new("Taxes et tours de role", "editions PC d'audience",             "PROC_TAXESETTOURS")
            ]),
            new("Relancer",
            [
                new("Relances comptes annuels", "DCA en retard",                    "PROC_DCA_RELANCE"),
                new("Injonctions comptes annuels", "passer a l'injonction",         "PROC_DCA_INJ"),
                new("Relances beneficiaires", "DBE en retard",                      "PROC_DBE_RELANCE"),
                new("Injonctions beneficiaires", "injonction DBE",                  "PROC_DBE_INJ"),
                new("Rappels RBE",            "registre des beneficiaires",         "PROC_SRBE_RAPPEL"),
                new("Retention d'actes",      "actes retenus au depot",             "PROC_ACTRET"),
                new("Actes rejetes",          "rejets Infogreffe",                  "PROC_ACTREJ"),
                new("Divergences BEN",        "alertes de coherence",               "PROC_DBEN_ALERT")
            ]),
            new("Les instances",
            [
                new("Injonctions de payer",   "les IP en attente",                  "PROC_SURVIP"),
                new("Suivi des preventions",  "les IP de prevention",               "PROC_IPSUIVI"),
                new("Mesures",                "surveillance des mesures",           "PROC_SURVMES"),
                new("Lots",                   "les lots en cours",                  "PROC_GESTLOT")
            ]),
            new("Les envois et les editions",
            [
                new("Surveillance BODACC",    "rejets et annonces en attente",      "PROC_SURVBOD"),
                new("Suivi dematerialisation","ce qui est parti au JAL/BODACC",     "PROC_SDEMATJUD"),
                new("Suivi d'impression",     "ce qui est sorti",                   "PROC_SUIVI_IMPRESSION"),
                new("Relancer un fichier",    "edition bloquee",                    "PROC_RELANCEFILE"),
                new("Relancer un lot",        "lot d'impression bloque",            "PROC_RELANCELOT")
            ]),
            new("Chiffres",
            [
                new("Statistiques",           "les chiffres du greffe",             "PROC_STAT"),
                new("Statistiques cour d'appel", "ce qui remonte a la CA",          "PROC_STATCA"),
                new("Pilotage",               "tableaux de bord",                   "PROC_XPILOTAGE"),
                new("Numeros manquants",      "le registre chronologique",          "PROC_NUMEROS_MANQUANTS")
            ])
        ]),
        new("Terminer", "fin", "#",
        [
            new("",
            [
                new("Cloture",                "fermer une affaire terminee",        "PROC_CLOT"),
                new("Archivage",              "archiver ou radier",                 "PROC_ARCHIRAD"),
                new("Extinction RCS",         "eteindre un dossier du registre",    "PROC_XEX")
            ])
        ]),
        new("Facturer", "compta", "$",
        [
            new("Facturer",
            [
                new("Factures",               "ce qui est du",                      "PROC_FACTURE"),
                new("Refacturation",          "reediter une facture",               "PROC_FACTREEDIT"),
                new("Releve de compte",       "le compte d'un client",              "PROC_RELCLI"),
                new("Articles tarifaires",    "ce qu'on facture",                   "PROC_ARTICLE"),
                new("Reversements INPI",      "ce qui part a l'INPI",               "PROC_INPIPAY"),
                new("Formalites INPI",        "paiements en attente",               "PROC_GINPI")
            ]),
            new("Encaisser",
            [
                new("Encaissements",          "ce qui est rentre",                  "PROC_ENCAISS"),
                new("Rejets d'encaissement",  "un reglement revenu",                "PROC_REJET"),
                new("Remise en banque",       "porter les reglements en banque",    "PROC_REMBANQ"),
                new("Cheques emis",           "ce que le greffe a paye",            "PROC_EDCHQEMIS")
            ]),
            new("Tenir les comptes",
            [
                new("Journal de caisse",      "la caisse du jour",                  "PROC_JCAISSE"),
                new("Journal de vente",       "les ventes du greffe",               "PROC_JVENTE"),
                new("Livre de repertoire",    "le journal du repertoire",           "PROC_JLR"),
                new("Lettrage",               "solder les mouvements",              "PROC_VLETTRAGE"),
                new("Figer les ecritures",    "comptabiliser la journee",           "PROC_COMPTABILISATION")
            ]),
            new("Creances",
            [
                new("Creances",               "les creances d'une PC",              "PROC_CREANCE"),
                new("Ajouter une creance",    "declarer une creance",               "PROC_ADDCREANCE"),
                new("Modifier une creance",   "corriger une declaration",           "PROC_MODCREANCE"),
                new("Integrer des creances",  "reprise en lot",                     "PROC_INTEGCREANCE"),
                new("Representation",         "qui represente le creancier",        "PROC_CREANREP")
            ])
        ]),
        new("Configurer", "reg", "@",
        [
            new("Les acteurs",
            [
                new("Acteurs judiciaires",    "juges, parquet, intervenants",       "PROC_ACTEUR_JUD"),
                new("Experts",                "qui designer, qui a deja siege",     "PROC_EXPERT"),
                new("Mandataires",            "mandataires et praticiens",          "PROC_MANDATAIRE"),
                new("Rapprocher des acteurs", "fusionner deux fiches",              "PROC_RAPPACTEUR"),
                new("Remplacer un acteur",    "substituer dans les instances",      "PROC_SUPP_ACTEUR_JUD")
            ]),
            new("Les clients",
            [
                new("Clients",                "comptes et fiches",                  "PROC_GCLIENT"),
                new("Fiche client",           "le detail d'un client",              "PROC_FCLIENT"),
                new("Tiers payeurs",          "qui regle a la place du client",     "PROC_GESTTIERSPAYEURS"),
                new("Non-inscrits",           "creer une personne hors registre",   "PROC_CNI")
            ]),
            new("Mon poste",
            [
                new("Mon accueil",            "tuiles et alertes",                  "PROC_ACCUEIL_PARAM"),
                new("Actualites",             "les messages de l'accueil",          "PROC_ACTUALITES"),
                new("Dates de travail",       "la date du greffe",                  "PROC_GESTDATE"),
                new("Comptes des agents",     "qui ouvre RIG, et avec quels droits", "PROC_UTILISATEUR"),
                new("Aide du greffe",         "le wiki interne",                    "PROC_WIKI"),
                new("Page web interne",       "ouvrir un site dans RIG",            "PROC_WEB_BROWSER")
            ]),
            new("Referentiels",
            [
                new("Tables de reference",    "les codes du greffe",                "PROC_GTABREF"),
                new("Natures de document",    "typologie de la GED",                "PROC_GED_NATURE_DOC"),
                new("Cabinets",               "les salles d'audience",              "PROC_CABINET"),
                new("Codes chefs IP",         "parametrage des injonctions",        "PROC_PARAM_IP"),
                new("Bornes d'accueil",       "les kiosques KBIS",                  "PROC_BORNE"),
                new("Compteurs de numeros",   "rendre un numero donne par erreur",  "PROC_COMPTEURS_NUM"),
                new("Reglages du greffe",     "les parametres propres au greffe",   "PROC_REGLAGES_GREFFE")
            ]),
            new("Modeles et envois",
            [
                new("Modeles d'edition",      "la forme des courriers",             "PROC_EDIT_MAQUETTE"),
                new("Modeles de redaction",   "maquettes des decisions",            "PROC_REDAA"),
                new("Sceaux",                 "elements de fusion",                 "PROC_SCEAU"),
                new("Messagerie",             "les envois automatiques",            "PROC_GESTEMAIL"),
                new("Courriels envoyes",      "la trace de chaque envoi",           "PROC_MAIL_LOT1")
            ]),
            new("Argent et exports",
            [
                new("Banques",                "les comptes du greffe",              "PROC_GESTBANQ"),
                new("Modes de paiement",      "les moyens acceptes",                "PROC_GESTMODEPAIEMENT"),
                new("Facturation Expresso",   "parametrage de la facturation",      "PROC_PARAMEXPRESSO"),
                new("Listes Excel",           "parametrage des exports",            "PROC_PARAM_LISTEEXCEL")
            ]),
            new("Outils",
            [
                new("Requetes metier",        "executer une requete preparee",      "PROC_REQUETES"),
                new("Analyse IA",             "controle des formalites INPI",       "PROC_IA"),
                new("Retours CFE-NET",        "traitement des retours XML",         "PROC_CFENET_RET"),
                new("EDI RPVA",               "inscriptions mandataires",           "PROC_EDI_RPVAINS"),
                new("EDI RPJC",               "traitement / conciliation",          "PROC_EDI_RPJC_TRCNC")
            ])
        ])
    ];

    /// <summary>Le VERBE du menu (Ouvrir, Trouver...) qui range un ecran, ou null
    /// pour un ecran hors du menu (PROC_TACHES_LOT, ouvert par une alerte de lot).
    /// L'onglet de la vue en porte la pastille (19/11/2026) : on reconnait d'un
    /// coup d'oeil ce qu'on est venu y faire, aux couleurs du menu.</summary>
    public static MomentMenu? VerbeDe(string code)
        => Moments.FirstOrDefault(m => m.Sous.Any(s => s.Entrees.Any(e => e.Code == code)));
}
