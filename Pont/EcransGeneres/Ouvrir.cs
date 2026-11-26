namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE OUVRIR du menu : « Creer une affaire », « Recevoir » et « Enregistrer un depot ».
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md) :
// les etapes deviennent les marches, les regles les consignes, les controles de validation
// les messages. Les lignes sont une demonstration anonyme (noms fictifs).
//
// PROC_CJUDINFOG (« Creation via Infogreffe ») n'a PAS de descripteur : sa fiche le dit
// obsolete (supprime le 22/11/2018, factorise dans PROC_CJUD). Son entree a ete retiree du
// menu (decision utilisateur du 2026-11-23) : l'enrolement passe par PROC_CJUD.
public sealed class DomaineOuvrir : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        // PROC_XAFF (lot 5 du plan « RIG v3 100 % fonctionnel », 26/11/2026) : FICHE, pas Saisie. Malgre son libelle de menu,
        // RIG v2 n'y cree pas d'instance : l'etape d'entree retrouve une instance EXISTANTE par son numero de role
        // (ETP_INSTANCE_RECHERCHE, MetierParamNumMetiers « Instance=JROP, JRIP, JRCT, JRPC, JRREF, JRJC, JRMDE, JRORC »,
        // PROC_XAFF.Designer.cs), la seule etape propre au PROC la corrige (ETP_CORRECT_INSTANCE, obligatoire), et
        // MetierCreateAffaire = false. La creation numerotee est dans PROC_IPE et PROC_CORDJRCS. LecteurNouvelleInstance.cs,
        // EcrivainNouvelleInstance.cs. fix-ok: mesure le 26/11/2026 (lot 5) -- OPE_INSTANCE_RECHERCHE.cs:155-180 ne fait que
        // charger une instance existante (txtNumeroMetier -> Instance.GetInstancesByNumRole) : la forme Saisie promettait une
        // creation que RIG v2 n'a pas ; IPE et CORDJRCS, eux, creent par l'etape Instance (OPE_INSTANCE.CallPrepSauve).
        new DescripteurEcran
        {
            Code = "PROC_XAFF", Titre = "Nouvelle instance", Sujet = "instances judiciaires",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une instance : numéro de rôle, nom courant…",
            // La recherche d'instance de RIG v2 (FormRechercheInstance.cs:425-426).
            Criteres = ["Numéro de rôle", "Nom courant"],
            Valider = "Valider",
            Etats = [.. EtatsEcran.Trois("À corriger", "En cours", "Corrigée"), new EtatEcran("EnAttente", "Verrouillée", "en-attente")],
            Groupes = ["Référé", "Fond"],
            Sections =
            [
                // OPE_CORRECT_INSTANCE.cs:33-43, titres et obligatoires de son Designer (:91-185). Code affaire, natures et acte se
                // lisent : leur revalidation contre le code affaire (:46-93) n'est pas reprise par la V3.
                Marche("Corriger l'instance", "Date d'enrôlement et date de saisine du tribunal obligatoires. Le code affaire, les natures et l'acte de saisine se lisent : RIG v2 les revalide contre les listes du code affaire, règle que la V3 ne reprend pas encore.", true,
                    "Numéro de rôle", "Code affaire", "Nature de l'affaire", "Nature particulière de l'affaire", "Acte de saisine",
                    Champ.Date("Date d'enrôlement"), Champ.Date("Date saisine tribunal"), Champ.Date("Date première audience"), "Nom courant",
                    Champ.Date("Date acte introductif"), "Délivrance de l'acte", Champ.Zone("Texte sur l'acte introductif")),
                // Les sous-listes que SetPhase charge (PROC_XAFF.cs:76-155) ; pas d'intervenant pour une IP (:97-103).
                Marche("Parties", "Demandeurs, défendeurs et intervenants de l'instance (pas d'intervenant pour une injonction de payer).", false,
                    Champ.Tableau("Demandeurs"), Champ.Tableau("Défendeurs"), Champ.Tableau("Intervenants")),
                Marche("Appels de l'affaire", "Les appels de l'affaire, par date d'audience.", false,
                    Champ.Tableau("Appels de l'affaire")),
                Marche("Décisions judiciaires", "Les décisions rendues ou prévues.", false,
                    Champ.Tableau("Décisions judiciaires")),
                Marche("Événements", "Événements survenus, puis futurs et expirés dans la même liste.", false,
                    Champ.Tableau("Événements survenus"), Champ.Tableau("Événements futurs et expirés")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL # Bêta Industrie SAS", "Rôle 2026J00101 · enrôlée le 03/03/2026", "Fond", "AFaire"),
                Ligne("Gamma Conseil SARL # Delta Transports SAS", "Rôle 2026J00102 · enrôlée le 04/03/2026", "Fond", "Validee"),
                Ligne("Epsilon Habitat SARL # Zêta Commerce SAS", "Rôle 2026R00103 · bloquée par une autre demande", "Référé", "EnAttente"),
                Ligne("Êta Boulangerie SARL # Thêta Import SAS", "Rôle 2026R00104 · enrôlée le 06/03/2026", "Référé", "AFaire"),
                Ligne("Iota Études SARL # Kappa Négoce SAS", "Rôle 2026IP00105 · enrôlée le 07/03/2026", "Fond", "AFaire"),
                Ligne("Lambda Pêche SARL # Mu Logistique SAS", "Rôle 2026J00106 · enrôlée le 09/03/2026", "Fond", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_NF", Titre = "Nouvelle formalite", Sujet = "procédures collectives",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un débiteur, un SIREN, une procédure…",
            Criteres = ["SIREN du débiteur", "Type de procédure", "Date du jugement"],
            Valider = "Ouvrir la procédure",
            Etats = EtatsEcran.Trois("À ouvrir", "En cours", "Ouverte"),
            Groupes = ["Redressement et liquidation (RC)", "Prévention (IP)", "Autres procédures"],
            Marches =
            [
                Marche("Débiteur", "Le premier débiteur fournit la procédure : son dossier RCS, ou son non-inscrit à défaut. Au moins un débiteur est requis ; sans l'un ni l'autre, la création échoue.", true,
                    "Premier débiteur", "Dossier RCS ou non-inscrit"),
                Marche("Instance", "Parties, appels, décision et événements. Une prévention n'a ni intervenants, ni appels, ni chef de demande.", true,
                    "Demandeurs", "Défendeurs", "Décision", "Événements"),
                Marche("Procédure collective", "Organes de procédure, dirigeants, établissements et chiffres.", true,
                    "Organes de procédure", "Dirigeants", "Établissements", "Chiffres"),
                Marche("Note", "N'apparaît que si la procédure a une note.", false,
                    "Note sur la procédure"),
                Marche("Facturation", "Forfait, ou articles ligne par ligne : le paramètre de la procédure décide.", true,
                    "Articles"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "SIREN 900 100 001 · redressement judiciaire · jugement du 02/03/2026", "Redressement et liquidation (RC)", "AFaire"),
                Ligne("Bêta Industrie SAS", "SIREN 900 100 002 · liquidation judiciaire · jugement du 05/03/2026", "Redressement et liquidation (RC)", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Non-inscrit · redressement judiciaire · jugement du 06/03/2026", "Redressement et liquidation (RC)", "EnCours", 0),
                Ligne("Delta Transports SAS", "SIREN 900 100 004 · sauvegarde · jugement du 10/03/2026", "Autres procédures", "Validee", 0, 1, 2, 4),
                Ligne("Epsilon Habitat SARL", "SIREN 900 100 005 · prévention", "Prévention (IP)", "AFaire"),
                Ligne("Zêta Commerce SAS", "SIREN 900 100 006 · prévention", "Prévention (IP)", "EnCours", 0, 1),
                Ligne("Êta Boulangerie SARL", "SIREN 900 100 007 · redressement judiciaire · jugement du 13/03/2026", "Autres procédures", "AFaire"),
                Ligne("Thêta Import SAS", "SIREN 900 100 008 · liquidation judiciaire · jugement du 16/03/2026", "Redressement et liquidation (RC)", "Validee", 0, 1, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_NPC", Titre = "Nouvelle proc. collective", Sujet = "procédures et préventions",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher une procédure, un débiteur, une notification…",
            Criteres = ["Mode (PC ou prévention)", "Notification CFEC", "Débiteur"],
            Valider = "Enregistrer la procédure",
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Enregistrée"), new EtatEcran("Rejetee", "Refusée (CFEC)", "rejetee")],
            Groupes = ["Procédure collective (PC)", "Prévention (PRV)"],
            Marches =
            [
                Marche("Procédure", "Saisie de la procédure, marquée extérieure. En mode prévention, c'est l'étape prévention qui s'ouvre.", true,
                    "Nature", "Date du jugement", "Débiteur"),
                Marche("Établissement", "Établissement de la procédure.", false,
                    "Établissement principal"),
                Marche("Organes", "Organes de la procédure.", true,
                    "Administrateur", "Mandataire", "Juge-commissaire"),
                Marche("Dirigeants", "Dirigeants de la procédure.", false,
                    "Dirigeant", "Qualité"),
                Marche("Chiffres", "Chiffres économiques de la procédure.", false,
                    "Chiffre d'affaires", "Effectif"),
                Marche("Événements", "Événements survenus, puis événements à venir.", false,
                    "Événements survenus", "Événements à venir"),
                Marche("Notification CFEC", "Notification dématérialisée. Acceptée, elle ouvre la suite ; refusée, elle fait refuser la demande et seule cette étape reste visible.", true,
                    "Accepter", "Refuser"),
                Marche("Facturation", "Facturation de la procédure, selon son paramètre.", true,
                    "Articles"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Notification CFEC reçue le 02/03/2026", "Procédure collective (PC)", "AFaire"),
                Ligne("Bêta Industrie SAS", "Notification CFEC reçue le 03/03/2026 · acceptée", "Procédure collective (PC)", "EnCours", 0, 6),
                Ligne("Gamma Conseil SARL", "Notification CFEC refusée le 04/03/2026", "Procédure collective (PC)", "Rejetee"),
                Ligne("Delta Transports SAS", "Procédure ouverte le 06/03/2026", "Procédure collective (PC)", "Validee", 0, 2, 6, 7),
                Ligne("Epsilon Habitat SARL", "Prévention · saisie du 09/03/2026", "Prévention (PRV)", "AFaire"),
                Ligne("Zêta Commerce SAS", "Prévention · saisie du 10/03/2026", "Prévention (PRV)", "EnCours", 0, 2),
                Ligne("Êta Boulangerie SARL", "Notification CFEC reçue le 12/03/2026", "Procédure collective (PC)", "EnCours", 0, 2, 6),
                Ligne("Thêta Import SAS", "Prévention · enregistrée le 13/03/2026", "Prévention (PRV)", "Validee", 0, 2, 6, 7),
            ],
        },

        // PROC_IPE (lot 5 du plan « RIG v3 100 % fonctionnel », 26/11/2026) : les etapes de PROC_IPE.Designer.cs:26-37, leurs
        // champs obligatoires lus dans leurs Designer (OPE_INSTANCE, OPE_PARTIE : MetierObligatoire). Creer ecrit l'instance
        // NUMEROTEE, ses deux parties et sa demande, laissee interrompue : decision, facture et editions restent a RIG v2
        // (EcrivainInjonctionsDePayer.cs, qui les nomme). LecteurInjonctionsDePayer.cs lit les saisies.
        new DescripteurEcran
        {
            Code = "PROC_IPE", Titre = "Injonction de payer", Sujet = "injonctions de payer",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher une demande, un demandeur, un défendeur…",
            Criteres = ["Demandeur", "Défendeur", "Date de la demande"],
            Valider = "Enregistrer l'injonction",
            // Refus (O) : le chemin refus de PROC_IPE (PROC_IPE.cs:261-277), l'etat « Refus » de ETAT_DEMANDE.
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Enregistrée"), new EtatEcran("EnAttente", "En attente du tribunal", "en-attente"),
                     new EtatEcran("Rejetee", "Refusée", "rejetee")],
            Groupes = ["Saisie directe", "Tribunal digital"],
            Marches =
            [
                // ETP_INSTANCE obligatoire (PROC_IPE.Designer.cs:111-120) ; codes affaire de type IP (OPE_INSTANCE.cs:1663-1664) ;
                // enrolement = maintenant (PROC_IPE.cs:155).
                Marche("Instance", "Codes affaire d'injonction de payer (type IP). La date d'enrôlement est celle du jour ; le numéro de rôle et le répertoire général sont attribués à la création. La date de l'acte n'est pas saisie pour une assignation.", true,
                    "Code affaire", "Nature affaire", "Nature particulière de l'affaire", "Acte de saisine", Champ.Date("Date de l'acte"),
                    Champ.Date("Saisine tribunal"), "Note sur l'affaire"),
                // ETP_DEMANDEUR / ETP_DEFENDEUR obligatoires (:87-107), un de chaque a la creation (PROC_IPE.cs:158-168).
                Marche("Demandeur", "Un demandeur à la création ; civilité et désignation obligatoires.", true,
                    "Civilité", "Désignation", "N° de Siren"),
                Marche("Défendeur", "Un défendeur à la création ; civilité et désignation obligatoires.", true,
                    "Civilité", "Désignation", "N° de Siren"),
                // ETP_PREP_DECISION obligatoire (:77-83) : non ecrite par la V3, la demande reste interrompue.
                Marche("Décision", "Décision préparée dès la saisie dans RIG v2 (type, avant dire droit, ressort, contradictoire, exécution provisoire). Non reprise par la V3 : la demande reste interrompue, à reprendre dans RIG v2.", true,
                    Champ.Tableau("Décision préparée")),
                // Etape unique (PROC_IPE.Designer.cs:63-72).
                Marche("Chef de demande", "Étape unique : un seul chef de demande.", false,
                    Champ.Tableau("Chef de demande")),
                Marche("Événements survenus", "Événements de l'instance.", false,
                    Champ.Tableau("Événements survenus")),
                Marche("Facturation", "IPREQ, puis IPREQSUP par partie facturée au-delà de la première. Non reprise par la V3.", false,
                    Champ.Tableau("Articles IPREQ et IPREQSUP")),
                Marche("Réclamation ou refus", "IPREQREFUS pour une ordonnance de refus, JRECLAM par réclamation attendue.", false,
                    Champ.Tableau("IPREQREFUS et JRECLAM")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL # Bêta Industrie SAS", "Demande D2600000101 · saisine du 02/03/2026", "Saisie directe", "AFaire"),
                Ligne("Gamma Conseil SARL # Delta Transports SAS", "Rôle 2026IP00102 · saisine du 03/03/2026 · interrompue", "Saisie directe", "EnCours", 0, 1, 2),
                Ligne("Epsilon Habitat SARL # Zêta Commerce SAS", "Rôle 2026IP00103 · saisine du 04/03/2026", "Saisie directe", "Validee", 0, 1, 2, 3),
                Ligne("Êta Boulangerie SARL # Thêta Import SAS", "Tribunal digital · flux du 05/03/2026", "Tribunal digital", "EnAttente"),
                Ligne("Iota Études SARL # Kappa Négoce SAS", "Rôle 2026IP00105 · flux du 06/03/2026", "Tribunal digital", "Validee", 0, 1, 2, 3),
                Ligne("Lambda Pêche SARL # Mu Logistique SAS", "Tribunal digital · flux du 09/03/2026 · refusée", "Tribunal digital", "Rejetee"),
                Ligne("Nu Paiement SAS # Xi Entrepôts SARL", "Rôle 2026IP00107 · saisine du 10/03/2026 · interrompue", "Saisie directe", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MESIN", Titre = "Mesure provisoire", Sujet = "mesures provisoires",
            // FICHE, pas Saisie : PROC_MESIN modifie une mesure qui EXISTE (proc_mesin.md:19 « ID de la mesure a
            // modifier », :177 verrou avant toute modification, :211 MetierCreateAffaire = false ; titre de la fenetre
            // « Modification de mesure », :256). On la retrouve, on la verrouille, on passe en Saisie (:221-226).
            // Controle final du lot 1, 25/11/2026 : elle etait rangee en Saisie d'apres le menu, pas d'apres la fiche.
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une mesure ou une instance…",
            // Le seul critere de l'etape de recherche V2 : OPE_MESURE_RECHERCHE.Designer.cs:81.
            Criteres = ["N° de la mesure ou instance"],
            // Valider : le bouton du moteur (Processus), la fiche n'en nomme pas d'autre (:209 aucun DoVerifier ni DoSauve).
            Valider = "Valider",
            Etats = [.. EtatsEcran.Trois("À modifier", "En cours", "Modifiée"), new EtatEcran("EnAttente", "Verrouillée", "en-attente")],
            Groupes = ["Mise en état", "Référé"],
            Sections =
            [
                // Les champs de l'etape « Mesure » (ETP_MESURE) : OPE_MESURE.Designer.cs:92-208. N° de mesure et
                // Ouverture y sont en lecture seule (:91, :112) ; Type de mesure obligatoire (:100), liste des types du
                // greffe (OPE_MESURE.cs:49, MSINS_TYPE_MESURE).
                // Verrou a l'ouverture ; s'il echoue, la mesure reste en lecture seule (proc_mesin.md:225, :228).
                Marche("Mesure", "Verrouillée à l'ouverture : tenue par un autre utilisateur, elle reste en lecture seule. N° de mesure et date d'ouverture ne se modifient pas ; le type de mesure est obligatoire.", true,
                    "Type de mesure", Champ.Date("Classement sans suite"), Champ.Date("Caducité"),
                    Champ.Date("Limite de dépôt du rapport"), Champ.Date("Dépôt du rapport"), Champ.Date("Prévision de dépôt du rapport"),
                    Champ.Montant("Coût prévu de l'expertise"),
                    Champ.Date("Limite de dépôt de l'état des diligences"), Champ.Date("Dépôt de l'état des diligences"),
                    Champ.Zone("Informations")),
                // Parties : celles de l'instance de la mesure (proc_mesin.md:243-245). Demandeurs et defendeurs
                // obligatoires (Designer.cs:112 et :101) ; sans demandeur, une ligne vide est creee.
                Marche("Demandeurs", "Les demandeurs de l'instance de la mesure ; sans demandeur, une ligne vide est créée.", true,
                    Champ.Tableau("Demandeurs")),
                Marche("Défendeurs", "Les défendeurs de l'instance ; la liste reste vide s'il n'y en a pas.", true,
                    Champ.Tableau("Défendeurs")),
                Marche("Intervenants", "Les intervenants de l'instance ; la liste reste vide s'il n'y en a pas.", false,
                    Champ.Tableau("Intervenants")),
                // Deux etapes d'evenements, pas trois : les expires sont affiches avec les futurs (proc_mesin.md:247).
                Marche("Événements survenus", "Événements de procédure déjà survenus ; une ligne vide est offerte s'il n'y en a aucun.", false,
                    Champ.Tableau("Événements survenus")),
                Marche("Événements futurs et expirés", "Événements à venir, et ceux dont le délai est dépassé, dans la même liste.", false,
                    Champ.Tableau("Événements futurs et expirés")),
                // Le type de facturation MESIN, fixe dans le code (proc_mesin.md:248).
                Marche("Facturation", "Les articles des mesures provisoires (type de facturation MESIN).", false,
                    Champ.Tableau("Articles MESIN")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Mesure du 02/03/2026 · instance 2026J00201", "Mise en état", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Mesure du 03/03/2026 · instance 2026J00202", "Mise en état", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Mesure du 05/03/2026 · instance 2026J00203", "Mise en état", "Validee", 0, 1, 2, 4),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Mesure en référé du 06/03/2026", "Référé", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Mesure en référé du 09/03/2026 · verrouillée par un autre utilisateur", "Référé", "EnAttente"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Mesure en référé du 10/03/2026", "Référé", "Validee", 0, 1, 2, 6),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Mesure du 12/03/2026 · instance 2026J00207", "Mise en état", "EnCours", 0),
            ],
        },

        // PROC_JOIN (lot 4 du plan « RIG v3 100 % fonctionnel », 26/11/2026) : aligne sur le « Detail OPE » de proc_join.md, qui
        // fait foi -- l'objet est la MERE (PROC_JOIN.cs:13), la fille se designe par son numero de role, UNE a la fois ; le lien
        // est porte par la fille (INSTN_ID_INSTN_INSTANCE_PRINCIPA, date et type de jonction). Il n'y a ni evenement JONCTION ni
        // controle de greffe (la fiche d'origine les inventait). LecteurJonctions.cs, EcrivainJonctions.cs.
        new DescripteurEcran
        {
            Code = "PROC_JOIN", Titre = "Jonction d'instances", Sujet = "jonctions d'instances",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher l'instance mère : numéro de rôle, nom courant…",
            // La recherche d'instance de RIG v2 (FormRechercheInstance.cs:425-426).
            Criteres = ["Numéro de rôle", "Nom courant"],
            Valider = "Enregistrer la jonction",
            // Une mere tenue par une autre demande n'entre pas en saisie (instance.Lock, PROC_JOIN.cs:49). « En cours » et « Jointe »
            // n'ont pas de ligne : le lien est porte par la fille, la mere reste « Libre ».
            Etats = [.. EtatsEcran.Trois("Libre", "En cours", "Jointe"), new EtatEcran("EnAttente", "Verrouillée", "en-attente")],
            Groupes = ["Instance principale", "Instance jointe", "Sans jonction"],
            Sections =
            [
                // OPE_RECAP_INST.cs:27-39.
                Marche("Instance mère", "L'instance cochée est la mère : elle n'est jamais modifiée, seule la fille porte le lien.", false,
                    Champ.Tableau("Récapitulatif de la mère")),
                // OPE_JONCTION_FILLES.cs:53-77 (la fille par son numero), :84-116 (deja jointe, boucle), :163-195 (les dates) ;
                // types : table TYPE_JONCTION (RIG_DEV, ULT_COMBO_TYPE_JONCTION).
                Marche("Instance fille", "La fille se désigne par son numéro de rôle. La date de jonction est obligatoire, jamais avant la date de l'acte de la fille ni de la mère. Une instance ne se joint ni à elle-même, ni deux fois, ni en boucle.", true,
                    "Numéro de rôle de la fille",
                    Champ.Liste("Type de jonction", "Jonction complète", "Jonction double enrolement", "Jonction informations et documents",
                        "Jonction LOIP", "dupliquer les parties sans joindre", "Jonction de rapprochement", "Jonction de rapprochement plumitif"),
                    Champ.Date("Date de jonction")),
                // BtnDisjoindre, OPE_JONCTION_FILLES.cs:141-146, :156-159.
                Marche("Disjonction", "Détache de la mère la fille dont le numéro est saisi : son lien, sa date et son type de jonction sont effacés.", false,
                    Champ.Case("Disjoindre la fille")),
                // Les filles que SetPhase pre-charge (PROC_JOIN.cs:71-72).
                Marche("Instances jointes", "Les filles déjà jointes à cette mère, avec leur type et leur date de jonction.", false,
                    Champ.Tableau("Filles jointes")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00301 · 2 instances jointes", "Instance principale", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00311 · jointe à 2026J00301", "Instance jointe", "AFaire"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00312 · jointe à 2026J00301", "Instance jointe", "AFaire"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J00321", "Sans jonction", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J00322 · bloquée par une autre demande", "Sans jonction", "EnAttente"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026J00331 · 1 instance jointe", "Instance principale", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J00332 · jointe à 2026J00331", "Instance jointe", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CORDJRCS", Titre = "Ordonnance d'enrolement", Sujet = "ordonnances d'enrôlement",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un dossier RCS, un non-inscrit, un code affaire…",
            Criteres = ["Dossier RCS ou non-inscrit", "Code affaire", "Date de saisine"],
            Valider = "Enregistrer l'ordonnance",
            Etats = EtatsEcran.Trois("À rédiger", "En cours", "Enregistrée"),
            Groupes = ["Enrôlement sur requête", "Refus", "Réclamation"],
            // Lot 5 du plan « RIG v3 100 % fonctionnel » (26/11/2026) : les etapes de PROC_CORDJRCS.DoInitObject (fiche
            // proc_cordjrcs.md, Detail OPE :254-279) et les obligatoires de leurs Designer (OPE_INSTANCE, OPE_PARTIE). Creer ecrit
            // l'instance NUMEROTEE, ses deux parties et sa demande, laissee interrompue (EcrivainOrdonnancesEnrolement.cs).
            Marches =
            [
                // Codes affaire de type RC (OPE_INSTANCE.cs:1649-1650) ; enrolement = maintenant (PROC_CORDJRCS.cs:214) ; troncature
                // automatique des champs trop longs (Instance.TronquageAuto, :183).
                Marche("Instance", "Codes affaire des ordonnances RCS (type RC). La date d'enrôlement est celle du jour ; le numéro de rôle est attribué à la création. Les champs trop longs sont tronqués.", true,
                    "Code affaire", "Nature affaire", "Nature particulière de l'affaire", "Acte de saisine", Champ.Date("Date de l'acte"),
                    Champ.Date("Saisine tribunal"), "Note sur l'affaire"),
                Marche("Demandeur", "Partie créée à l'ouverture ; civilité et désignation obligatoires.", true,
                    "Civilité", "Désignation", "N° de Siren"),
                Marche("Défendeur", "Partie créée à l'ouverture ; civilité et désignation obligatoires.", true,
                    "Civilité", "Désignation", "N° de Siren"),
                Marche("Décision", "La décision judiciaire est créée à la validation ; le dossier RCS ou le non-inscrit est mis à jour. Non reprise par la V3 : la demande reste interrompue, à reprendre dans RIG v2.", true,
                    Champ.Tableau("Décision")),
                Marche("Chef de demande", "Qualification juridique de la demande.", false,
                    Champ.Tableau("Chef de demande")),
                Marche("Facturation", "Préfixe d'émolument selon la date de saisine (origine, 16-, 18-). Les articles 801 et 806 sont des frais postaux, pas des émoluments. Non reprise par la V3.", false,
                    Champ.Tableau("Émoluments et frais postaux")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL # Bêta Industrie SAS", "Demande D2600000401 · saisine du 02/03/2026", "Enrôlement sur requête", "AFaire"),
                Ligne("Gamma Conseil SARL # Delta Transports SAS", "Rôle 2026RC00402 · saisine du 04/03/2026 · interrompue", "Enrôlement sur requête", "EnCours", 0, 1, 2),
                Ligne("Epsilon Habitat SARL # Zêta Commerce SAS", "Rôle 2026RC00403 · saisine du 05/03/2026", "Enrôlement sur requête", "Validee", 0, 1, 2, 3),
                Ligne("Êta Boulangerie SARL # Thêta Import SAS", "Rôle 2026RC00404 · saisine du 06/03/2026", "Refus", "Validee", 0, 1, 2, 3),
                Ligne("Iota Études SARL # Kappa Négoce SAS", "Rôle 2026RC00405 · saisine du 09/03/2026", "Réclamation", "EnCours", 0, 1),
                Ligne("Lambda Pêche SARL # Mu Logistique SAS", "Rôle 2026RC00406 · saisine du 10/03/2026", "Enrôlement sur requête", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DAS", Titre = "Depots d'actes RCS", Sujet = "dépôts d'actes",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un acte…",
            Criteres = ["Dossier RCS ou non-inscrit", "Date de dépôt", "Type d'acte"],
            Valider = "Enregistrer le dépôt",
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Enregistré"), new EtatEcran("Rejetee", "Refusé", "rejetee")],
            Groupes = ["Dossier RCS", "Non-inscrit"],
            Marches =
            [
                Marche("Dépôt", "Dates de dépôt et d'arrivée, dépôt gratuit INPI. Le dépôt est vérifié en entier avant validation.", true,
                    "Date de dépôt", "Date d'arrivée", "Dépôt gratuit INPI"),
                Marche("Évènement RCS", "Code de l'évènement obligatoire. Changer de code supprime les actes déposés de l'ancien code.", true,
                    "Code de l'évènement", "Date d'effet", "Texte"),
                Marche("Actes déposés", "Pièce obligatoire et date d'acte obligatoire ; la description se calcule seule.", true,
                    "Pièce", "Date de l'acte", "Description"),
                Marche("Comptes annuels", "Selon l'évènement : la case de non-approbation des comptes doit être cochée, ou la date de clôture modifiée.", false,
                    "Non-approbation des comptes", "Date de clôture"),
                Marche("Facturation", "Article 21-RCDASRET si le dépôt est retenu, 21-RCDASREJ s'il est rejeté.", true,
                    "Article de facturation"),
                Marche("Refus ou réclamation", "La lettre de refus ou la lettre de réclamation est éditée selon le cas.", false,
                    "Lettre de refus", "Lettre de réclamation"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Dossier RCS 2026B00501 · dépôt du 02/03/2026", "Dossier RCS", "AFaire"),
                Ligne("Bêta Industrie SAS", "Dossier RCS 2026B00502 · dépôt du 03/03/2026", "Dossier RCS", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Dossier RCS 2026B00503 · dépôt du 04/03/2026", "Dossier RCS", "Validee", 0, 1, 2, 4),
                Ligne("Delta Transports SAS", "Dossier RCS 2026B00504 · refusé", "Dossier RCS", "Rejetee"),
                Ligne("Epsilon Habitat SARL", "Dossier RCS 2026B00505 · dépôt du 06/03/2026", "Dossier RCS", "EnCours", 0, 1, 2),
                Ligne("Zêta Commerce SAS", "Non-inscrit · dépôt du 09/03/2026", "Non-inscrit", "AFaire"),
                Ligne("Êta Boulangerie SARL", "Non-inscrit · dépôt du 10/03/2026", "Non-inscrit", "EnCours", 0, 1, 2, 4),
                Ligne("Thêta Import SAS", "Non-inscrit · dépôt du 11/03/2026", "Non-inscrit", "Validee", 0, 1, 2, 4),
                Ligne("Iota Études SARL", "Dossier RCS 2026B00510 · dépôt du 12/03/2026", "Dossier RCS", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DEP", Titre = "Depots d'evenement PC", Sujet = "dépôts d'événement",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher une procédure collective ou une prévention…",
            Criteres = ["Numéro de procédure", "Débiteur", "Date de l'événement"],
            Valider = "Enregistrer le dépôt",
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Enregistré"), new EtatEcran("EnAttente", "Verrouillée", "en-attente")],
            Groupes = ["Procédure collective", "Prévention"],
            Marches =
            [
                Marche("Procédure", "Verrouillée pour la saisie : si un autre utilisateur la tient, elle s'ouvre en lecture seule.", true,
                    "Numéro de procédure"),
                Marche("Organes et dirigeants", "Organes de la procédure et dirigeants.", false,
                    "Organes", "Dirigeants"),
                Marche("Événement survenu", "Un dépôt PC est obligatoire sur chaque événement survenu.", true,
                    "Code de l'événement", "Date", "Dépôt PC"),
                Marche("Événement à venir", "Ce qui doit encore se passer, selon le calendrier.", false,
                    "Événement à venir"),
                Marche("Facturation", "Forfait selon le paramètre de la procédure. Une prévention est toujours gratuite.", true,
                    "Articles"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Procédure 2026P00101 · dépôt du 02/03/2026", "Procédure collective", "AFaire"),
                Ligne("Bêta Industrie SAS", "Procédure 2026P00102 · dépôt du 03/03/2026", "Procédure collective", "EnCours", 0, 2),
                Ligne("Gamma Conseil SARL", "Procédure 2026P00103 · verrouillée par un autre utilisateur", "Procédure collective", "EnAttente"),
                Ligne("Delta Transports SAS", "Procédure 2026P00104 · enregistré le 05/03/2026", "Procédure collective", "Validee", 0, 2, 4),
                Ligne("Epsilon Habitat SARL", "Prévention 2026P00201 · dépôt du 06/03/2026", "Prévention", "AFaire"),
                Ligne("Zêta Commerce SAS", "Prévention 2026P00202 · dépôt du 09/03/2026", "Prévention", "EnCours", 0, 2),
                Ligne("Êta Boulangerie SARL", "Prévention 2026P00203 · enregistré le 10/03/2026", "Prévention", "Validee", 0, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DEPPCM", Titre = "Depots PC en masse", Sujet = "numéros de PC du lot",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un numéro de PC dans le lot…",
            Criteres = ["Numéro de PC", "Code événement", "Date de l'événement"],
            Valider = "Valider le dépôt",
            Etats = EtatsEcran.Trois("À saisir", "En cours", "Dépôt créé"),
            Groupes = ["Comptes annuels"],
            Marches =
            [
                Marche("Numéro de PC", "Obligatoire : une étape sans numéro bloque la validation (« PC manquante »).", true,
                    "Numéro de PC"),
                Marche("Événement", "Code de l'événement et date.", true,
                    "Code événement", "Date"),
                Marche("Doublon", "Même numéro, même code et même date : un avertissement, non bloquant.", false,
                    "Contrôle de doublon"),
                Marche("Dépôt", "À la validation, la demande fille est créée et rattachée à la demande du lot.", true,
                    "Dépôt RCS créé"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "PC 2026P00301 · comptes annuels 2025 · 02/03/2026", "Comptes annuels", "AFaire"),
                Ligne("Bêta Industrie SAS", "PC 2026P00302 · comptes annuels 2025 · 02/03/2026", "Comptes annuels", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "PC 2026P00303 · comptes annuels 2025 · 03/03/2026", "Comptes annuels", "Validee", 0, 1, 3),
                Ligne("Delta Transports SAS", "PC 2026P00304 · comptes annuels 2025 · 03/03/2026", "Comptes annuels", "AFaire"),
                Ligne("Epsilon Habitat SARL", "PC 2026P00305 · comptes annuels 2025 · 04/03/2026", "Comptes annuels", "EnCours", 0),
                Ligne("Zêta Commerce SAS", "PC 2026P00306 · comptes annuels 2025 · 04/03/2026", "Comptes annuels", "Validee", 0, 1, 3),
                Ligne("Êta Boulangerie SARL", "PC 2026P00307 · comptes annuels 2025 · 05/03/2026", "Comptes annuels", "AFaire"),
                Ligne("Thêta Import SAS", "PC 2026P00308 · comptes annuels 2025 · 05/03/2026", "Comptes annuels", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DASBEN", Titre = "Depots beneficiaires", Sujet = "dépôts de bénéficiaires effectifs",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher une entreprise, un numéro de gestion…",
            Criteres = ["Entreprise (RCS ou non-inscrit)", "Date de dépôt", "Code événement"],
            Valider = "Enregistrer le dépôt",
            Etats = EtatsEcran.Trois("À saisir", "En cours", "Enregistré"),
            Groupes = ["Dossier RCS", "Non-inscrit"],
            Marches =
            [
                Marche("Entreprise", "Recherche de l'entreprise : elle doit être éligible (dossier RCS ou non-inscrit). Sa fiche s'affiche ensuite en lecture seule.", true,
                    "Entreprise", "Mode"),
                Marche("Fiche entreprise", "Identité et adresse, en lecture seule.", false,
                    "Identité", "Adresse"),
                Marche("Actes", "Dates, code événement et montants. Les codes proposés sont ceux qui vont avec l'historique BEN du dossier.", true,
                    "Date d'arrivée", "Date de dépôt", "Code événement", "Montant"),
                Marche("Acte déposé", "L'acte et sa date.", true,
                    "Acte", "Date de l'acte"),
                Marche("Pièces", "Pièces dématérialisées, rattachées au dépôt.", false,
                    "Pièces"),
                Marche("Numérotation", "Numéro attribué à la validation ; la saisie repart ensuite pour un autre dépôt.", false,
                    "Numéro"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Dossier RCS 2026B00601 · dépôt du 02/03/2026", "Dossier RCS", "AFaire"),
                Ligne("Bêta Industrie SAS", "Dossier RCS 2026B00602 · dépôt du 03/03/2026", "Dossier RCS", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Dossier RCS 2026B00603 · dépôt du 04/03/2026", "Dossier RCS", "Validee", 0, 1, 2, 3, 5),
                Ligne("Delta Transports SAS", "Non-inscrit · dépôt du 05/03/2026", "Non-inscrit", "AFaire"),
                Ligne("Epsilon Habitat SARL", "Non-inscrit · dépôt du 06/03/2026", "Non-inscrit", "EnCours", 0, 1, 2),
                Ligne("Zêta Commerce SAS", "Non-inscrit · dépôt du 09/03/2026", "Non-inscrit", "Validee", 0, 1, 2, 3),
                Ligne("Êta Boulangerie SARL", "Dossier RCS 2026B00607 · dépôt du 10/03/2026", "Dossier RCS", "AFaire"),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
