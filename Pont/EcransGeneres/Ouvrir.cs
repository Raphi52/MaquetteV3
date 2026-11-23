namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE OUVRIR du menu : « Creer une affaire », « Recevoir » et « Enregistrer un depot ».
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md) :
// les etapes deviennent les marches, les regles les consignes, les controles de validation
// les messages. Les lignes sont une demonstration anonyme (noms fictifs).
//
// PROC_CJUDINFOG (« Creation via Infogreffe ») n'a PAS de descripteur : sa fiche le dit
// obsolete (supprime le 22/11/2018, factorise dans PROC_CJUD). Il reste « a venir » dans le
// menu ; le retirer ou le router vers PROC_CJUD est une decision du chef.
public sealed class DomaineOuvrir : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_XAFF", Titre = "Nouvelle instance", Sujet = "instances judiciaires",
            Recherche = "Rechercher une instance, un numéro de rôle, une partie…",
            Criteres = ["Numéro de rôle", "Type d'instance", "Mode référé (PR)"],
            Valider = "Enregistrer l'instance",
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Enregistrée"),
            Groupes = ["Référé", "Fond"],
            Marches =
            [
                Marche("Instance", "Le code affaire revalide la nature et l'acte de saisine : une valeur devenue incompatible est effacée, et l'utilisateur est prévenu.", true,
                    "Code affaire", "Nature affaire", "Acte de saisine", "Date d'enrôlement", "Date de saisine du tribunal"),
                Marche("Parties", "Au moins un demandeur et un défendeur : une ligne vierge est créée si la liste est vide. Pas d'intervenant pour une injonction de payer.", true,
                    "Demandeurs", "Défendeurs", "Intervenants"),
                Marche("Calendrier et appels", "Calendrier des audiences, appels passés et appels à venir.", false,
                    "Calendrier", "Appels passés", "Appels à venir"),
                Marche("Décision et chef de demande", "Décisions judiciaires rendues. Le chef de demande reste unique tant qu'aucune décision ne lui est rattachée.", false,
                    "Décisions judiciaires", "Chef de demande"),
                Marche("Événements", "Événements survenus, puis événements futurs ; les expirés y figurent aussi.", false,
                    "Événements survenus", "Événements futurs"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00101 · enrôlée le 03/03/2026", "Fond", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00102 · enrôlée le 04/03/2026", "Fond", "EnCours", 0),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00103 · mode référé", "Référé", "EnCours", 0, 1),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J00104 · mode référé", "Référé", "Validee", 0, 1, 3),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J00105 · enrôlée le 07/03/2026", "Fond", "Validee", 0, 1, 3, 4),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026J00106 · enrôlée le 09/03/2026", "Fond", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J00107 · mode référé", "Référé", "EnCours", 0),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026J00108 · enrôlée le 12/03/2026", "Fond", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_NF", Titre = "Nouvelle formalite", Sujet = "procédures collectives",
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

        new DescripteurEcran
        {
            Code = "PROC_IPE", Titre = "Injonction de payer", Sujet = "injonctions de payer",
            Recherche = "Rechercher une demande, un demandeur, un défendeur…",
            Criteres = ["Demandeur", "Défendeur", "Date de la demande"],
            Valider = "Enregistrer l'injonction",
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Enregistrée"), new EtatEcran("EnAttente", "En attente du tribunal", "en-attente")],
            Groupes = ["Saisie directe", "Tribunal digital"],
            Marches =
            [
                Marche("Demande", "En-tête de la demande, masquable. En circuit dématérialisé, le tribunal digital s'affiche une fois la formalité créée.", false,
                    "Date de la demande", "Tribunal"),
                Marche("Instance", "Créée vide dès l'ouverture : une instance quittée sans validation reste orpheline, à nettoyer.", true,
                    "Code affaire", "Date d'enrôlement"),
                Marche("Demandeur", "Au moins un demandeur, jusqu'à 250.", true,
                    "Demandeur"),
                Marche("Défendeur", "Au moins un défendeur. Un KBis ou une attestation de non-inscription est édité selon qu'il est inscrit.", true,
                    "Défendeur", "Inscription au registre"),
                Marche("Décision", "Décision préparée dès la saisie.", true,
                    "Décision préparée", "Chef de demande"),
                Marche("Facturation", "IPREQ, puis IPREQSUP par partie au-delà de la première.", true,
                    "IPREQ", "IPREQSUP"),
                Marche("Réclamation ou refus", "IPREQREFUS pour une ordonnance de refus, JRECLAM pour une réclamation.", false,
                    "IPREQREFUS", "JRECLAM"),
                Marche("Note", "Note libre sur la demande.", false,
                    "Note"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Demande reçue le 02/03/2026", "Saisie directe", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Demande du 03/03/2026 · deux défendeurs", "Saisie directe", "EnCours", 0, 1, 2),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Demande du 04/03/2026", "Saisie directe", "Validee", 0, 1, 2, 3, 4, 5),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Tribunal digital · flux du 05/03/2026", "Tribunal digital", "EnAttente"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Tribunal digital · flux du 06/03/2026", "Tribunal digital", "EnCours", 0, 1, 2),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Demande du 09/03/2026", "Saisie directe", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Demande du 10/03/2026 · réclamation", "Saisie directe", "EnCours", 0, 1, 2, 3, 4, 5, 6),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Tribunal digital · flux du 11/03/2026", "Tribunal digital", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MESIN", Titre = "Mesure provisoire", Sujet = "mesures provisoires",
            Recherche = "Rechercher une mesure, une instance, une partie…",
            Criteres = ["Instance", "Partie", "Date de la mesure"],
            Valider = "Enregistrer la mesure",
            Etats = EtatsEcran.Trois("À saisir", "En cours", "Enregistrée"),
            Groupes = ["Mise en état", "Référé"],
            Marches =
            [
                Marche("Mesure", "Verrouillée avant toute modification : si un autre utilisateur la tient, un message s'affiche et la saisie ne s'ouvre pas.", true,
                    "Nature de la mesure", "Date"),
                Marche("Parties", "Demandeur, défendeur et intervenants de la mesure.", true,
                    "Demandeur", "Défendeur", "Intervenants"),
                Marche("Événements survenus", "Événements de procédure déjà survenus.", false,
                    "Événements survenus"),
                Marche("Événements futurs", "Événements planifiés.", false,
                    "Événements futurs"),
                Marche("Événements expirés", "Délais dépassés sans réalisation.", false,
                    "Événements expirés"),
                Marche("Facturation", "Articles du code MESIN, fixé dans le plugin.", true,
                    "Articles MESIN"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Mesure du 02/03/2026 · instance 2026J00201", "Mise en état", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Mesure du 03/03/2026 · instance 2026J00202", "Mise en état", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Mesure du 05/03/2026 · instance 2026J00203", "Mise en état", "Validee", 0, 1, 2, 5),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Mesure en référé du 06/03/2026", "Référé", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Mesure en référé du 09/03/2026", "Référé", "EnCours", 0),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Mesure en référé du 10/03/2026", "Référé", "Validee", 0, 1, 5),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Mesure du 12/03/2026 · instance 2026J00207", "Mise en état", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_JOIN", Titre = "Jonction d'instances", Sujet = "jonctions d'instances",
            Recherche = "Rechercher l'instance mère : numéro de rôle, parties…",
            Criteres = ["Numéro de rôle", "Parties", "Date d'enrôlement"],
            Valider = "Enregistrer la jonction",
            Etats = EtatsEcran.Trois("À joindre", "En cours", "Jointe"),
            Groupes = ["Mère 2026J00301", "Mère 2026J00302", "Mère 2026J00303"],
            Marches =
            [
                Marche("Instance mère", "Recherche de la mère. Mère et filles sont verrouillées avant la jonction, pour éviter les modifications concurrentes.", true,
                    "Numéro de rôle"),
                Marche("Instances filles", "Les filles sont choisies une à une, puis contrôlées.", true,
                    "Instances à joindre"),
                Marche("Déjà jointe", "Une instance déjà jointe ne se rejoint pas : la détacher de sa mère d'abord.", true,
                    "Contrôle de jonction"),
                Marche("Boucle", "Une instance ne se joint pas à elle-même, ni à l'une de ses ancêtres : la jonction circulaire est interdite.", true,
                    "Contrôle de boucle"),
                Marche("Même greffe", "Les instances de greffes différents ne se joignent pas.", true,
                    "Contrôle de greffe"),
                Marche("Événement de jonction", "La jonction laisse un événement JONCTION dans l'historique de la mère.", false,
                    "Événement JONCTION"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00311 · à joindre", "Mère 2026J00301", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00312 · en contrôle", "Mère 2026J00301", "EnCours", 0, 1, 2),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00313 · jointe le 04/03/2026", "Mère 2026J00301", "Validee", 0, 1, 2, 3, 4, 5),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J00321 · à joindre", "Mère 2026J00302", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J00322 · en contrôle", "Mère 2026J00302", "EnCours", 0, 1),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026J00331 · à joindre", "Mère 2026J00303", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J00332 · jointe le 09/03/2026", "Mère 2026J00303", "Validee", 0, 1, 2, 3, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CORDJRCS", Titre = "Ordonnance d'enrolement", Sujet = "ordonnances d'enrôlement",
            Recherche = "Rechercher un dossier RCS, un non-inscrit, un code affaire…",
            Criteres = ["Dossier RCS ou non-inscrit", "Code affaire", "Date de saisine"],
            Valider = "Enregistrer l'ordonnance",
            Etats = EtatsEcran.Trois("À rédiger", "En cours", "Enregistrée"),
            Groupes = ["Enrôlement sur requête", "Refus", "Réclamation"],
            Marches =
            [
                Marche("Facturation", "Préfixe d'émolument selon la date de saisine (origine, 16-, 18-). Les articles 801 et 806 sont des frais postaux, pas des émoluments.", true,
                    "Émoluments", "Frais postaux"),
                Marche("Instance", "Instance créée vide : les champs trop longs sont tronqués automatiquement.", true,
                    "Code affaire", "Date de saisine"),
                Marche("Demandeur", "Partie créée à l'ouverture.", true,
                    "Demandeur"),
                Marche("Défendeur", "Partie créée à l'ouverture.", true,
                    "Défendeur"),
                Marche("Décision", "La décision judiciaire est créée à la validation ; le dossier RCS ou le non-inscrit est mis à jour.", true,
                    "Date de l'ordonnance", "Nature de la décision"),
                Marche("Chef de demande", "Qualification juridique de la demande.", false,
                    "Chef de demande"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Dossier RCS 2026B00401 · saisine du 02/03/2026", "Enrôlement sur requête", "AFaire"),
                Ligne("Bêta Industrie SAS", "Dossier RCS 2026B00402 · saisine du 04/03/2026", "Enrôlement sur requête", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Non-inscrit · saisine du 05/03/2026", "Enrôlement sur requête", "Validee", 0, 1, 2, 3, 4),
                Ligne("Delta Transports SAS", "Dossier RCS 2026B00405 · refus", "Refus", "AFaire"),
                Ligne("Epsilon Habitat SARL", "Dossier RCS 2026B00406 · refus", "Refus", "EnCours", 0, 1, 2),
                Ligne("Zêta Commerce SAS", "Dossier RCS 2026B00407 · refus", "Refus", "Validee", 0, 1, 2, 3, 4),
                Ligne("Êta Boulangerie SARL", "Non-inscrit · réclamation du 09/03/2026", "Réclamation", "EnCours", 0, 1, 2, 3),
                Ligne("Thêta Import SAS", "Dossier RCS 2026B00409 · réclamation", "Réclamation", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DAS", Titre = "Depots d'actes RCS", Sujet = "dépôts d'actes",
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

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params string[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
