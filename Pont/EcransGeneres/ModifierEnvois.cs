namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE MODIFIER du menu, partie B (lot 4 du plan « les 125 ecrans manquants du menu »,
// 23/11/2026) : rediger, editer, envoyer. Dix-sept ecrans, ranges par groupe du menu :
// « Rediger et signer » (6), « Editer un courrier » (4) et « Envoyer » (7).
//
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md,
// « Detail OPE » faisant foi) : les etapes deviennent les marches, les regles les consignes,
// les controles de validation les messages. Les lignes sont une demonstration ANONYME.
//
// Ecarts a connaitre :
//   - Rang 2 = teinte du verbe Modifier (decision du chef, 23/11/2026). Sa classe
//     rig-tcw__rang-2 arrive par le lot 9 (wwwroot/rig-ecrans-generes.css) ; le banc lit
//     aujourd'hui rig-accueil.css seulement.
//   - Bordereaux LR : les marches « avec AR » et « sans AR » sont facultatives chacune, car
//     une ligne ne suit qu'une branche. Seules la periode et l'edition sont obligatoires.
//   - Bordereaux LR : la fiche fixe la limite a 1 000 plis (controle « >= 1000 » dans le code).
//   - File d'impression : la fiche signale qu'un echec du service d'impression reste
//     silencieux ; l'ecran ne le reproduit pas (a signaler, non corrige ici).
public sealed class DomaineModifierEnvois : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_REDA_MASSE", Titre = "Redaction en masse", Sujet = "décisions judiciaires",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une décision, un numéro de rôle, une partie…",
            Criteres = ["Numéro de rôle", "Type de décision", "État de rédaction"],
            Valider = "Valider le lot",
            Rang = 2,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Minute imprimée"),
            Groupes = ["Jugements", "Ordonnances"],
            Sections =
            [
                Marche("Dates d'audience", "Délibéré et prononcé renseignés : sans les deux, la décision ne se traite pas. Une date peut être calculée d'après les prochaines audiences ; l'annulation du calcul restaure la date d'origine.", true,
                    "Date de délibéré", "Date de prononcé"),
                Marche("Modèle", "Le modèle de document est obligatoire pour générer la décision.", true,
                    "Modèle de document"),
                Marche("Génération", "Génère le document Word ou RTF à partir du modèle. Une décision déjà générée (brouillon d'état 0 seulement) ne se régénère pas.", true,
                    "Génération de la décision"),
                Marche("Validation greffe", "Attribue le numéro de minute. Un échec est signalé dans le compte rendu du lot ; les autres décisions continuent.", true,
                    "Numéro de minute"),
                Marche("Impression de la minute", "Impression regroupée par instance. Sans case d'impression, la décision sort du lot d'impression.", true,
                    "Impression de la minute"),
                Marche("Dépens", "Génération des dépens, possible seulement une fois la minute imprimée.", false,
                    "Génération des dépens"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00101 · délibéré et prononcé le 04/03/2026", "Jugements", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00102 · date de prononcé à saisir", "Jugements", "EnCours", 0),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00103 · générée, minute à valider", "Jugements", "EnCours", 0, 1, 2),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026F00104 · minute imprimée", "Jugements", "Validee", 0, 1, 2, 3, 4),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026F00105 · minute imprimée, dépens générés", "Ordonnances", "Validee", 0, 1, 2, 3, 4, 5),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026F00106 · ordonnance du 09/03/2026", "Ordonnances", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026F00107 · modèle choisi, dates à saisir", "Ordonnances", "EnCours", 1),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026F00108 · révision demandée", "Ordonnances", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DEST_REDA", Titre = "Destinataires", Sujet = "destinataires",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un destinataire, une partie, un mandataire…",
            Criteres = ["Partie", "Mandataire", "Destinataire divers"],
            Valider = "Enregistrer les destinataires",
            Rang = 2,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Parties", "Mandataires", "Destinataires divers"],
            Sections =
            [
                Marche("Édition du jugement", "Chaque destinataire reçoit une édition : grosse (G), copie (C), expédition (E) ou lettre de notification (L). Sans aucune, la ligne est notée X.", true,
                    "Édition", Champ.Case("Grosse"), Champ.Case("Copie"), Champ.Case("Expédition"), Champ.Case("Lettre de notification")),
                Marche("Date de délivrance des grosses", "Obligatoire pour continuer : sans elle, l'enregistrement avertit, et les autres opérations du processus restent verrouillées.", true,
                    "Date de délivrance des grosses"),
                Marche("Suppléments", "Nombre de grosses, de copies et d'expéditions en plus, saisi à la main.", false,
                    Champ.Nombre("Grosses supplémentaires"), Champ.Nombre("Copies supplémentaires"), Champ.Nombre("Expéditions supplémentaires")),
                Marche("Destinataire divers", "Ajout manuel d'un destinataire. Seuls les destinataires divers se suppriment ; ceux de l'instance restent en lecture seule.", false,
                    "Destinataire divers", "Désignation"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Demandeur · aucune édition", "Parties", "AFaire"),
                Ligne("Bêta Industrie SAS", "Défendeur · grosse et copie · date à saisir", "Parties", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "Défendeur · expédition · grosses délivrées le 06/03/2026", "Parties", "Validee", 0, 1),
                Ligne("Cabinet Delta Avocats", "Mandataire postulant · lettre de notification", "Mandataires", "EnCours", 0),
                Ligne("Cabinet Epsilon Avocats", "Mandataire plaidant · grosse et copie · délivrance le 09/03/2026", "Mandataires", "Validee", 0, 1, 2),
                Ligne("Zêta Commerce SAS", "Destinataire divers · expédition", "Destinataires divers", "Validee", 0, 1, 3),
                Ligne("Thêta Import SAS", "Destinataire divers · aucune édition", "Destinataires divers", "AFaire"),
                Ligne("Iota Études SARL", "Partie · grosse · suppléments saisis", "Parties", "EnCours", 0, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SIGNJUD", Titre = "Signature electronique", Sujet = "décisions à signer",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une décision, un numéro de rôle, un juge…",
            Criteres = ["Juge", "Greffier", "État de signature"],
            Valider = "Lancer la signature",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À envoyer", "Envoyée au juge", "Signée"), new EtatEcran("EnAttente", "À corriger", "en-attente"), new EtatEcran("Rejetee", "En erreur", "rejetee")],
            Groupes = ["Pour le juge", "Pour le greffier"],
            Sections =
            [
                Marche("Contrôle de la décision", "Juge, greffier, une seule édition et date prévue renseignés, sans erreur bloquante. Une décision en erreur ne se coche pas.", true,
                    "Juge", "Greffier", "Édition", "Date prévue"),
                Marche("Envoi au juge", "Seules les décisions sans erreur se cochent. Le lot part au juge pour signature.", true,
                    "Décision cochée", "Envoi au juge"),
                Marche("Signature du juge", "Le juge signe en premier, puis le greffier contresigne. Si le juge demande des corrections, la décision revient « À corriger ».", true,
                    "Signature du juge"),
                Marche("Contresignature du greffier", "Le greffier contresigne les décisions signées par le juge. Chaque décision est mise en GED séparément : un échec n'arrête pas les autres.", true,
                    "Contresignature", "Mise en GED"),
                Marche("Correction", "Le menu « Corriger » renvoie une décision au juge, après confirmation. Tant que le juge n'a pas signé, tout le lot du même dossier repasse à corriger.", false,
                    "Passage à corriger"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Juge 1 · greffier 1 · à envoyer", "Pour le juge", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Juge 1 · envoyée au juge le 05/03/2026", "Pour le juge", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Juge 2 · corrections demandées", "Pour le juge", "EnAttente", 0, 1),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Juge 2 · signée, à contresigner", "Pour le greffier", "EnCours", 0, 1, 2),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Greffier 1 · contresignée, en GED", "Pour le greffier", "Validee", 0, 1, 2, 3),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Juge 1 · erreur de signature", "Pour le juge", "Rejetee", 0),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Juge 2 · à envoyer", "Pour le juge", "AFaire"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Greffier 2 · contresignée, en GED", "Pour le greffier", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ORDOSIGN", Titre = "Ordonnances a signer", Sujet = "ordonnances à signer",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une instance, un numéro de rôle, une affaire…",
            Criteres = ["Type de recherche", "Date de décision", "Copies à imprimer"],
            Valider = "Signer la sélection",
            Rang = 2,
            Etats = EtatsEcran.Trois("À dater", "En cours", "Signée"),
            Groupes = ["Opposition", "Injonctions", "Juge commis"],
            Sections =
            [
                Marche("Décision rendue", "Cocher la décision rendue : une décision non rendue reste dans la liste à signer.", true,
                    "Décision rendue"),
                Marche("Date de la décision", "Date entre la date de saisine du greffe et aujourd'hui, au format jj/mm/aaaa. Elle se valide en quittant la cellule.", true,
                    "Date de la décision"),
                Marche("Changement de juge commissaire", "Un seul changement en attente à la fois. Le juge à remplacer doit être présent avec la bonne fonction, et le nouveau juge absent de la procédure.", false,
                    "Juge sortant", "Nouveau juge"),
                Marche("Copies", "Nombre de copies par destinataire, réglé dans le paramétrage des copies.", false,
                    "Destinataires des copies", "Format d'impression"),
                Marche("Numéro de minute", "Attribué à la signature. Un numéro déjà attribué n'est pas recalculé.", true,
                    "Numéro de minute"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00201 · opposition · à dater", "Opposition", "AFaire"),
                Ligne("Gamma Conseil SARL", "Rôle 2026F00202 · décision cochée, date saisie", "Opposition", "EnCours", 0, 1),
                Ligne("Delta Transports SAS", "Rôle 2026F00203 · signée, minute attribuée", "Opposition", "Validee", 0, 1, 4),
                Ligne("Epsilon Habitat SARL", "Injonction de dépôt des comptes · à dater", "Injonctions", "AFaire"),
                Ligne("Zêta Commerce SAS", "Injonction de dépôt des comptes · décision cochée", "Injonctions", "EnCours", 0),
                Ligne("Êta Boulangerie SARL", "Injonction RBE · signée, minute attribuée", "Injonctions", "Validee", 0, 1, 4),
                Ligne("Thêta Import SAS", "Juge commis · changement de juge en attente", "Juge commis", "EnCours", 0, 2),
                Ligne("Iota Études SARL", "Juge commis · à dater", "Juge commis", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DJ_OCCULTATIONS", Titre = "Occultations", Sujet = "décisions",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une décision, un numéro de rôle…",
            Criteres = ["Catégories d'occultation", "Mots à masquer", "Mots à conserver"],
            Valider = "Enregistrer les occultations",
            Rang = 2,
            Etats = EtatsEcran.Trois("À occulter", "En cours", "Validées"),
            Groupes = ["Jugements", "Ordonnances"],
            Sections =
            [
                Marche("Décision", "La décision est chargée. Sans décision, l'écran affiche seulement un message : rien à saisir.", true,
                    "Numéro de rôle", "Type de décision"),
                Marche("Catégories", "Les cases d'occultation sont pré-cochées selon le paramétrage du greffe ; chaque case s'ajuste à la main.", true,
                    Champ.Tableau("Catégories d'occultation (12 cases)")),
                Marche("Mots à masquer", "Un mot ajouté est masqué. Unicité globale, sans tenir compte de la casse : un mot ne peut pas être à la fois à masquer et à conserver.", false,
                    "Mot à masquer"),
                Marche("Mots à conserver", "Un mot à conserver est gardé tel quel. Un mot retiré est barré et disparaît à l'enregistrement.", false,
                    "Mot à conserver"),
                Marche("Enregistrement", "Enregistre les occultations, la décision et la centralisation de l'instance. Un mot saisi sans clic sur Ajouter reste un avertissement, sans bloquer.", true),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00301 · jugement du 04/03/2026 · occultations non saisies", "Jugements", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00302 · cases pré-cochées, mots à saisir", "Jugements", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00303 · mots à masquer ajoutés", "Jugements", "EnCours", 0, 1, 2),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026F00304 · occultations enregistrées", "Jugements", "Validee", 0, 1, 4),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026F00305 · ordonnance du 05/03/2026", "Ordonnances", "AFaire"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026F00306 · mots à conserver saisis", "Ordonnances", "EnCours", 0, 3),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026F00307 · mots à masquer et à conserver", "Ordonnances", "Validee", 0, 1, 2, 3, 4),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026F00308 · ordonnance du 06/03/2026", "Ordonnances", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IMPLEG", Titre = "Impression legale", Sujet = "décisions d'une audience",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une décision, un numéro de rôle, un nom…",
            Criteres = ["Audience de cabinet", "Type de décision", "Numéro de minute"],
            Valider = "Imprimer la sélection",
            Rang = 2,
            Etats = EtatsEcran.Trois("À numéroter", "Numérotée", "Imprimée"),
            Groupes = ["Jugements d'expulsion", "Autres décisions"],
            Sections =
            [
                Marche("Numéro de minute", "Une décision sans numéro ne s'imprime pas : le bouton Générer les numéros attribue ceux qui manquent, dans la série des jugements d'expulsion.", true,
                    "Numéro de minute", "Numéro de répertoire"),
                Marche("Impression", "Une demande d'édition par décision, envoyée en lot à l'éditeur. L'utilisateur reçoit un retour sur les éditions générées. Tout sélectionner ou désélectionner d'un clic.", true,
                    "Imprimée"),
                Marche("Dépens", "Calcul des dépens en tâche de fond : les colonnes de dépens peuvent apparaître après un court délai.", false,
                    "Dépens"),
                Marche("Récapitulatif", "Liste des décisions numérotées de la séance. Sans aucune décision numérotée, le processus se ferme.", false,
                    "Récapitulatif des minutes"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Rôle 2026F00401 · sans numéro de minute", "Jugements d'expulsion", "AFaire"),
                Ligne("Bêta Industrie SAS", "Rôle 2026F00402 · numéro attribué, pas encore imprimée", "Jugements d'expulsion", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "Rôle 2026F00403 · imprimée, dépens calculés", "Jugements d'expulsion", "Validee", 0, 1, 2),
                Ligne("Delta Transports SAS", "Rôle 2026F00404 · imprimée", "Jugements d'expulsion", "Validee", 0, 1),
                Ligne("Epsilon Habitat SARL", "Rôle 2026F00405 · sans numéro de minute", "Autres décisions", "AFaire"),
                Ligne("Zêta Commerce SAS", "Rôle 2026F00406 · numérotée, à imprimer", "Autres décisions", "EnCours", 0),
                Ligne("Êta Boulangerie SARL", "Rôle 2026F00407 · imprimée", "Autres décisions", "Validee", 0, 1),
                Ligne("Thêta Import SAS", "Rôle 2026F00408 · sans numéro de minute", "Autres décisions", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_COURRIERM", Titre = "Courriers en masse", Sujet = "instances, procédures et mesures",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une instance, une procédure, un numéro…",
            Criteres = ["Instance", "Procédure", "Prévention", "Mesure"],
            Valider = "Générer les courriers",
            Rang = 2,
            Etats = EtatsEcran.Trois("À courrier", "En cours", "Générés"),
            Groupes = ["Instances", "Procédures", "Préventions", "Mesures"],
            Sections =
            [
                Marche("Volet", "Un seul volet à la fois : instance, procédure, prévention ou mesure. Le volet change la recherche et le résultat.", true,
                    "Volet"),
                Marche("Fiche", "Case « fiche » : édition principale de l'entité, au code fixe.", true,
                    "Fiche"),
                Marche("Deuxième édition", "Case « seconde édition » : code choisi dans la liste, hors fiche.", false,
                    "Seconde édition"),
                Marche("Génération", "Au moins une case cochée est exigée. Une demande par entité et une édition par case cochée, vers l'imprimante ou la file différée. Une entité déjà présente dans la liste n'est pas ajoutée deux fois.", true,
                    "Imprimante ou file différée"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00501 · aucune case cochée", "Instances", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00502 · fiche cochée", "Instances", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00503 · fiche et seconde édition générées", "Instances", "Validee", 0, 1, 2, 3),
                Ligne("Êta Boulangerie SARL", "Procédure PC 2026P00601 · fiche cochée", "Procédures", "EnCours", 0, 1),
                Ligne("Thêta Import SAS", "Procédure PC 2026P00602 · générée", "Procédures", "Validee", 0, 1, 3),
                Ligne("Iota Études SARL", "Prévention PRV 2026R00701 · aucune case cochée", "Préventions", "AFaire"),
                Ligne("Kappa Négoce SAS", "Prévention PRV 2026R00702 · générée", "Préventions", "Validee", 0, 1, 3),
                Ligne("Lambda Pêche SARL", "Mesure 2026M00801 · édition à générer", "Mesures", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDT_DIFF", Titre = "Editions differees", Sujet = "lots d'éditions",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un document, un destinataire, un utilisateur…",
            Criteres = ["Date d'ouverture du lot", "Imprimante", "Nombre de documents"],
            Valider = "Clôturer le lot",
            Rang = 2,
            Etats = EtatsEcran.Trois("À clôturer", "En préparation", "Clôturé"),
            Groupes = ["Agent A", "Agent B"],
            Sections =
            [
                Marche("Imprimante", "Imprimante obligatoire pour clôturer : sans elle, le lot ne se ferme pas. Une imprimante interne de test est signalée, sans blocage.", true,
                    "Imprimante"),
                Marche("Documents du lot", "Cocher « Ne pas imprimer » retire le document du lot : il reste listé, barré. Le nombre d'exemplaires est affiché.", false,
                    "Ne pas imprimer", "Exemplaires"),
                Marche("Clôture", "Enregistre chaque document et ferme le lot : il disparaît de la liste d'attente, et une trace de clôture est créée.", true,
                    "Date de clôture"),
            ],
            Lignes =
            [
                Ligne("Notification — Alpha Services SARL", "Destinataire : demandeur · 2 exemplaires · créée le 04/03/2026", "Agent A", "AFaire"),
                Ligne("Notification — Bêta Industrie SAS", "Destinataire : défendeur · 1 exemplaire · créée le 04/03/2026", "Agent A", "EnCours", 1),
                Ligne("Copie — Gamma Conseil SARL", "Destinataire : mandataire · barrée, ne pas imprimer", "Agent A", "EnCours", 1),
                Ligne("Notification — Delta Transports SAS", "Destinataire : défendeur · 1 exemplaire · créée le 05/03/2026", "Agent A", "Validee", 0, 1, 2),
                Ligne("Notification — Epsilon Habitat SARL", "Destinataire : partie · 2 exemplaires · créée le 05/03/2026", "Agent B", "AFaire"),
                Ligne("Copie — Zêta Commerce SAS", "Destinataire : partie · 1 exemplaire · créée le 06/03/2026", "Agent B", "EnCours", 0, 1),
                Ligne("Notification — Êta Boulangerie SARL", "Destinataire : mandataire · 1 exemplaire · créée le 06/03/2026", "Agent B", "Validee", 0, 1, 2),
                Ligne("Notification — Thêta Import SAS", "Destinataire : partie · 2 exemplaires · créée le 09/03/2026", "Agent B", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PDF_WRITER", Titre = "Pivoter des pages", Sujet = "pages",
            Forme = FormeEcran.Document,
            Recherche = "Rechercher une page, une plage (ex. 1-3, 5)…",
            Criteres = ["Pages cochées", "Plage de pages", "Sens de rotation"],
            Valider = "Enregistrer le PDF",
            Rang = 2,
            Etats = EtatsEcran.Trois("À pivoter", "Pivotée", "Enregistrée"),
            Groupes = ["Pages 1 à 4", "Pages 5 à 8"],
            Sections =
            [
                Marche("Fichier source", "Le fichier PDF source doit exister. Absent, l'écran reste ouvert mais inutilisable.", true,
                    "Fichier source"),
                Marche("Sélection", "Cocher des pages ou saisir une plage (ex. 1-3,5). La plage prime sur les cases cochées.", true,
                    "Pages cochées", "Plage de pages"),
                Marche("Rotation", "90° horaire, 90° anti-horaire ou 180°. Les rotations s'accumulent page par page ; le PDF n'est pas touché avant l'enregistrement.", true,
                    "Rotation"),
                Marche("Enregistrement", "Applique les rotations en une passe vers le fichier de sortie. Quitter sans enregistrer demande confirmation.", true,
                    "Fichier de sortie"),
            ],
            Lignes =
            [
                Ligne("Page 1", "Aucune rotation", "Pages 1 à 4", "AFaire"),
                Ligne("Page 2", "Rotation de 90° horaire, non enregistrée", "Pages 1 à 4", "EnCours", 0, 1, 2),
                Ligne("Page 3", "Rotation de 180°, non enregistrée", "Pages 1 à 4", "EnCours", 0, 1, 2),
                Ligne("Page 4", "Rotation de 90° anti-horaire, non enregistrée", "Pages 1 à 4", "EnCours", 0, 1, 2),
                Ligne("Page 5", "Aucune rotation", "Pages 5 à 8", "AFaire"),
                Ligne("Page 6", "Rotation de 90° horaire, enregistrée", "Pages 5 à 8", "Validee", 0, 1, 2, 3),
                Ligne("Page 7", "Aucune rotation, enregistrée", "Pages 5 à 8", "Validee", 0, 1, 2, 3),
                Ligne("Page 8", "Rotation de 180°, enregistrée", "Pages 5 à 8", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_NUM", Titre = "Documents ocerises", Sujet = "documents océrisés",
            Forme = FormeEcran.Document,
            Recherche = "Rechercher un document, un titre, une date…",
            Criteres = ["Format (PDF ou TIF)", "Titre", "Nombre de pages"],
            Valider = "Enregistrer une copie",
            Rang = 2,
            Etats = EtatsEcran.Trois("À vérifier", "En cours", "Enregistré"),
            Groupes = ["PDF", "TIF"],
            Sections =
            [
                Marche("Document", "Le document est ouvert ; son format, PDF ou TIF, décide de l'éditeur utilisé.", true,
                    "Format", "Nombre de pages"),
                Marche("Pages blanches", "Supprime les pages blanches du document, en une passe.", false,
                    "Pages blanches supprimées"),
                Marche("Modification", "PDF : éditeur de pages. TIF : éditeur natif, ou éditeur PDF si le paramètre du greffe le prévoit.", false,
                    "Éditeur"),
                Marche("Paramétrage", "Titre, type de document, confidentialité et métadonnées OCR.", true,
                    "Titre", "Type de document", "Confidentialité", "Métadonnées OCR"),
                Marche("Réservation pour fusion", "Réserve le document à votre compte pour une fusion, avant d'ouvrir la sélection des documents à fusionner.", false,
                    "Réservé pour fusion"),
                Marche("Enregistrement", "Copie le document vers un fichier PDF ou TIF choisi.", true,
                    "Fichier de sortie"),
            ],
            Lignes =
            [
                Ligne("Scan 2026-0301", "PDF · 12 pages · à vérifier", "PDF", "AFaire"),
                Ligne("Scan 2026-0302", "PDF · 4 pages · pages blanches supprimées", "PDF", "EnCours", 0, 1),
                Ligne("Scan 2026-0303", "PDF · 9 pages · titre saisi", "PDF", "EnCours", 0, 3),
                Ligne("Scan 2026-0304", "PDF · 6 pages · enregistré", "PDF", "Validee", 0, 3, 5),
                Ligne("Scan 2026-0305", "PDF · 2 pages · réservé pour fusion", "PDF", "EnCours", 0, 4),
                Ligne("Scan 2026-0306", "TIF · 3 pages · à vérifier", "TIF", "AFaire"),
                Ligne("Scan 2026-0307", "TIF · 5 pages · modifié, paramétrage à faire", "TIF", "EnCours", 0, 2),
                Ligne("Scan 2026-0308", "TIF · 1 page · enregistré", "TIF", "Validee", 0, 3, 5),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TRANSDOC", Titre = "Transmission", Sujet = "documents dématérialisés",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un document, un numéro de rôle, un SIREN…",
            Criteres = ["Tribunal digital", "Coffre-fort numérique", "Numéro d'affaire"],
            Valider = "Enregistrer les modifications",
            Rang = 2,
            Etats = EtatsEcran.Trois("À transmettre", "Numéro saisi", "Enregistré"),
            Groupes = ["Tribunal digital", "Coffre-fort numérique"],
            Sections =
            [
                Marche("Numéro d'affaire", "Saisi dans la colonne éditable. Actualiser enregistre les modifications avant de recharger la liste.", true,
                    "Numéro d'affaire"),
                Marche("Visualisation", "Voir le document. Une fois ouvert, il est marqué consulté.", false,
                    "Document ouvert"),
                Marche("Visualisation judiciaire", "Lance la visualisation judiciaire de l'instance ou de la procédure du document. Inactive si le document n'est rattaché à aucune entité.", false,
                    "Instance ou procédure"),
                Marche("Enregistrement", "Enregistre les numéros d'affaire. Quitter avec des numéros modifiés non enregistrés demande confirmation.", true,
                    "Enregistrement"),
            ],
            Lignes =
            [
                Ligne("Document DOC-2026-0101", "Dépôt du 02/03/2026 · KBIS · rôle 2026F00901 · SIREN 900 100 001", "Tribunal digital", "AFaire"),
                Ligne("Document DOC-2026-0102", "Dépôt du 03/03/2026 · acte · rôle 2026F00902", "Tribunal digital", "EnCours", 0),
                Ligne("Document DOC-2026-0103", "Dépôt du 03/03/2026 · acte · enregistré", "Tribunal digital", "Validee", 0, 3),
                Ligne("Document DOC-2026-0104", "Dépôt du 04/03/2026 · KBIS · consulté", "Tribunal digital", "EnCours", 1),
                Ligne("Document DOC-2026-0105", "Dépôt du 05/03/2026 · pièce · à transmettre", "Coffre-fort numérique", "AFaire"),
                Ligne("Document DOC-2026-0106", "Dépôt du 06/03/2026 · pièce · numéro saisi", "Coffre-fort numérique", "EnCours", 0),
                Ligne("Document DOC-2026-0107", "Dépôt du 09/03/2026 · pièce · enregistré", "Coffre-fort numérique", "Validee", 0, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BORDLR", Titre = "Bordereaux LR", Sujet = "lettres recommandées",
            Forme = FormeEcran.Etat,
            Parametres = [Champ.Periode("Période"), "Utilisateur", Champ.Case("Mes recommandés"), Champ.Liste("Avec ou sans AR", "Avec AR", "Sans AR")],
            Colonnes = ["Bordereau", Champ.Nombre("Nombre de plis")],
            Gestes = ["Éditer le bordereau"],
            Recherche = "Rechercher un bordereau, un utilisateur, une période…",
            Criteres = ["Période (dates et heures)", "Utilisateur", "Avec ou sans AR"],
            Valider = "Éditer le bordereau",
            Rang = 2,
            Etats = EtatsEcran.Trois("À préparer", "En cours", "Édité"),
            Groupes = ["Avec accusé de réception", "Sans accusé de réception"],
            Sections =
            [
                Marche("Période", "Date et heure de début, date et heure de fin. Sans heure, la plage couvre les journées entières. La date de début doit précéder la date de fin.", true,
                    "Date de début", "Heure de début", "Date de fin", "Heure de fin"),
                Marche("Utilisateur", "Tous les utilisateurs ayant émis, un utilisateur choisi, ou « mes recommandés » : la case fige le choix sur votre compte.", false,
                    "Utilisateur", "Mes recommandés"),
                Marche("Bordereau avec AR", "Lettres avec accusé de réception (LRAR) : la case AR est cochée et les coordonnées de retour du greffe sont imprimées. Un pli avec AR ne se mélange jamais avec un pli sans AR.", false,
                    "Case AR", "Coordonnées de retour"),
                Marche("Bordereau sans AR", "Lettres sans accusé de réception (LR) : bordereau simplifié. Une fois la numérotation LRN démarrée, ce bordereau passe en consultation.", false,
                    "Bordereau simplifié"),
                Marche("Édition", "Édition par le module d'impression. Aucun pli : « Validation inutile, vous n'avez rien à éditer. » À partir de 1 000 plis, l'édition est refusée : scinder le lot.", true,
                    "Nombre de plis", "Bordereau"),
            ],
            Lignes =
            [
                Ligne("Bordereau LRAR-1", "02/03/2026 · 14 plis · utilisateur 1", "Avec accusé de réception", "AFaire"),
                Ligne("Bordereau LRAR-2", "03/03/2026 · 9 plis · mes recommandés", "Avec accusé de réception", "EnCours", 0, 1),
                Ligne("Bordereau LRAR-3", "04/03/2026 · 22 plis · édité", "Avec accusé de réception", "Validee", 0, 2, 4),
                Ligne("Bordereau LRAR-4", "05/03/2026 · 6 plis · utilisateur 2", "Avec accusé de réception", "EnCours", 0, 2),
                Ligne("Bordereau LR-1", "02/03/2026 · 31 plis · utilisateur 1", "Sans accusé de réception", "AFaire"),
                Ligne("Bordereau LR-2", "03/03/2026 · 12 plis · utilisateur 3", "Sans accusé de réception", "EnCours", 0, 3),
                Ligne("Bordereau LR-3", "04/03/2026 · 18 plis · édité", "Sans accusé de réception", "Validee", 0, 3, 4),
                Ligne("Bordereau LR-4", "09/03/2026 · 1 002 plis · édition refusée", "Sans accusé de réception", "EnCours", 0, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RETOURLRAR", Titre = "Retours LRAR", Sujet = "retours de recommandés",
            Forme = FormeEcran.File,
            Recherche = "Scanner ou saisir le code-barre d'un pli…",
            Criteres = ["Code-barre du pli", "Numéro LRAR", "Date de réception"],
            Valider = "Enregistrer le retour",
            Rang = 2,
            Etats = EtatsEcran.Trois("À saisir", "En cours", "Retour enregistré"),
            Groupes = ["Reçus", "NPAI, refusés ou incomplets", "Non réclamés", "Non envoyés"],
            Sections =
            [
                Marche("Pli scanné", "Le code-barre retrouve l'événement et la demande d'origine. L'arborescence entité, événement, recommandé s'affiche.", true,
                    "Code-barre", "Numéro LRAR"),
                Marche("Modalité de retour", "Reçu, NPAI, non réclamé, refusé, incomplet ou non envoyé. C'est la modalité qui est enregistrée.", true,
                    "Modalité de retour"),
                Marche("Date de réception", "Date du retour. Modifiable ensuite : le changement demande confirmation, et peut prévenir le service judiciaire.", true,
                    "Date de réception"),
                Marche("Remise en émission", "Si la modalité n'est pas à gérer, les demandes d'édition liées repartent en file d'émission.", false,
                    "Demandes remises en file"),
                Marche("Événement lié", "Un événement futur lié passe en survenu. L'instance reste verrouillée pendant l'opération.", false,
                    "Événement futur"),
            ],
            Lignes =
            [
                Ligne("Pli 2026-LRAR-0101", "Demande 2026D0101 · aucune modalité saisie", "Reçus", "AFaire"),
                Ligne("Pli 2026-LRAR-0102", "Demande 2026D0102 · scanné, date à saisir", "Reçus", "EnCours", 0, 1),
                Ligne("Pli 2026-LRAR-0103", "Demande 2026D0103 · reçu le 04/03/2026", "Reçus", "Validee", 0, 1, 2),
                Ligne("Pli 2026-LRAR-0104", "Demande 2026D0104 · NPAI le 05/03/2026", "NPAI, refusés ou incomplets", "Validee", 0, 1, 2),
                Ligne("Pli 2026-LRAR-0105", "Demande 2026D0105 · refusé, remise en émission", "NPAI, refusés ou incomplets", "EnCours", 0, 1, 3),
                Ligne("Pli 2026-LRAR-0106", "Demande 2026D0106 · non réclamé", "Non réclamés", "EnCours", 0, 1),
                Ligne("Pli 2026-LRAR-0107", "Demande 2026D0107 · non envoyé", "Non envoyés", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ENV_JAL", Titre = "Envoi JAL / BODACC", Sujet = "annonces légales",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une annonce, un bordereau, une facture…",
            Criteres = ["Période d'envoi", "Destinataire (JAL)", "Numéro de bordereau"],
            Valider = "Générer le bordereau",
            Rang = 2,
            Etats = EtatsEcran.Trois("À envoyer", "Bordereau généré", "Envoyée"),
            Groupes = ["Procédures collectives", "Préventions"],
            Sections =
            [
                Marche("Période", "Dates pré-remplies d'après le paramétrage du greffe (JAL ou BODACC). Une recherche par numéro de bordereau ou de facture remplace les dates.", true,
                    "Date de début", "Date de fin"),
                Marche("Sélection", "Les annonces à envoyer, filtrées par procédure collective ou par prévention. L'ordre d'affichage suit le paramétrage du greffe.", true,
                    "Annonces cochées"),
                Marche("Génération", "Génère le bordereau et le fichier d'envoi. Annuler la génération est refusé si le bordereau a déjà été transmis.", true,
                    "Bordereau"),
                Marche("Envoi électronique", "Envoi EDI ou BODACC après génération. Une fois transmis, le bordereau ne s'annule plus.", false,
                    "Transmission"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Procédure PC 2026P00301 · publicité du 02/03/2026", "Procédures collectives", "AFaire"),
                Ligne("Bêta Industrie SAS", "Procédure PC 2026P00302 · publicité sélectionnée", "Procédures collectives", "EnCours", 1),
                Ligne("Gamma Conseil SARL", "Procédure PC 2026P00303 · bordereau 2026-BDX-01 généré", "Procédures collectives", "EnCours", 0, 1, 2),
                Ligne("Delta Transports SAS", "Procédure PC 2026P00304 · envoyée le 04/03/2026", "Procédures collectives", "Validee", 0, 1, 2, 3),
                Ligne("Epsilon Habitat SARL", "Prévention PRV 2026R00401 · publicité à envoyer", "Préventions", "AFaire"),
                Ligne("Zêta Commerce SAS", "Prévention PRV 2026R00402 · sélectionnée", "Préventions", "EnCours", 1),
                Ligne("Êta Boulangerie SARL", "Prévention PRV 2026R00403 · envoyée le 06/03/2026", "Préventions", "Validee", 0, 1, 2),
                Ligne("Thêta Import SAS", "Prévention PRV 2026R00404 · à envoyer", "Préventions", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTBOD", Titre = "Publications BODACC", Sujet = "annonces BODACC",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une annonce, un numéro de parution, une date…",
            Criteres = ["Date d'EP", "Date de parution", "Numéro de parution"],
            Valider = "Valider la sélection",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À traiter", "Vérifiée", "Certifiée"), new EtatEcran("EnAttente", "À renvoyer", "en-attente")],
            Groupes = ["Certificats de parution", "Rejets", "Contrôles non conformes"],
            Sections =
            [
                Marche("Mode et critère", "Mode : certificats, rejets, mise à jour des rejets ou contrôles non conformes. Critère de date : date d'EP, de parution ou d'envoi, ou numéro de parution. La date de fin ne peut pas précéder la date de début.", true,
                    "Mode", "Critère", "Date du", "Date au"),
                Marche("Vérification", "Cocher « vérifier » passe l'annonce à l'état vérifié. Une validation sans aucune ligne cochée est refusée.", true,
                    "Vérifier"),
                Marche("Édition", "Édition de la liste ou du certificat. Pour un certificat, l'annonce cochée passe à l'état certifié.", true,
                    "Édition"),
                Marche("Renvoi des rejets", "Pour la mise à jour des rejets : cocher « renvoyer » place l'annonce à renvoyer, et Valider enregistre ces renvois.", false,
                    "Renvoyer"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Parution n° 2026-041 · EP du 02/03/2026", "Certificats de parution", "AFaire"),
                Ligne("Bêta Industrie SAS", "Parution n° 2026-041 · vérifiée", "Certificats de parution", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL", "Parution n° 2026-042 · certifiée", "Certificats de parution", "Validee", 0, 1, 2),
                Ligne("Delta Transports SAS", "Parution n° 2026-042 · certifiée", "Certificats de parution", "Validee", 0, 1, 2),
                Ligne("Epsilon Habitat SARL", "Rejet du 05/03/2026 · à traiter", "Rejets", "AFaire"),
                Ligne("Zêta Commerce SAS", "Rejet du 06/03/2026 · renvoi coché", "Rejets", "EnAttente", 0, 3),
                Ligne("Êta Boulangerie SARL", "Contrôle de qualité · à traiter", "Contrôles non conformes", "AFaire"),
                Ligne("Thêta Import SAS", "Contrôle de qualité · vérifié", "Contrôles non conformes", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PAPERCUT", Titre = "File d'impression", Sujet = "travaux d'impression",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une imprimante…",
            Criteres = ["Imprimante", "Utilisateur", "Nombre de feuilles"],
            Valider = "Imprimer la file",
            Rang = 2,
            Etats = EtatsEcran.Trois("En attente", "En cours", "Imprimée"),
            Groupes = ["Greffe", "Accueil"],
            Sections =
            [
                Marche("Imprimante", "Une imprimante par ligne. Sans nom d'imprimante ou d'utilisateur, l'action est refusée.", true,
                    "Imprimante", "Utilisateur"),
                Marche("Impression", "Envoie les travaux au service d'impression. Un document déjà imprimé est redemandé : une confirmation est demandée avant.", true,
                    "Travaux envoyés"),
                Marche("Vidage", "Vider supprime les travaux en attente de l'imprimante sans les imprimer, puis rafraîchit la liste. Le nom « * » vide toutes les imprimantes de l'utilisateur.", false,
                    "Travaux supprimés"),
            ],
            Lignes =
            [
                Ligne("Imprimante Greffe 1", "3 travaux · 14 feuilles · utilisateur 1", "Greffe", "AFaire"),
                Ligne("Imprimante Greffe 2", "1 travail · 2 feuilles · utilisateur 1", "Greffe", "EnCours", 0),
                Ligne("Imprimante Greffe 3", "5 travaux · 30 feuilles · en impression", "Greffe", "EnCours", 0, 1),
                Ligne("Imprimante Accueil", "2 travaux · 6 feuilles · imprimée", "Accueil", "Validee", 0, 1),
                Ligne("Imprimante Salle A", "4 travaux · 22 feuilles · utilisateur 2", "Accueil", "AFaire"),
                Ligne("Imprimante Salle B", "1 travail · 1 feuille · imprimée", "Accueil", "Validee", 0, 1),
                Ligne("Imprimante Salle C", "8 travaux · 40 feuilles · à vider", "Accueil", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_LISTEEXCEL", Titre = "Export Excel", Sujet = "listes d'instances",
            Forme = FormeEcran.Etat,
            Parametres = ["Type de liste", Champ.Periode("Période"), "Code d'acte", "Nature d'affaire"],
            Colonnes = ["Liste", Champ.Nombre("Nombre de lignes")],
            Gestes = ["Lancer l'export Excel"],
            Recherche = "Rechercher un type de liste…",
            Criteres = ["Type de liste", "Période", "Colonnes à exporter"],
            Valider = "Lancer l'export Excel",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À compter", "Comptée", "Exportée"), new EtatEcran("EnAttente", "Bloquée : trop de lignes", "en-attente")],
            Groupes = ["Instances", "Groupements", "Affaires et citations"],
            Sections =
            [
                Marche("Type de liste", "Le type fixe les critères proposés : instances enrôlées, avec ou sans décision, avec événement passé ou futur, groupements, affaires sans décision définitive, citations signifiées.", true,
                    "Type de liste"),
                Marche("Critères", "Période et critères du type choisi. Un groupement se règle par code d'acte et nature d'affaire.", true,
                    "Période", "Code d'acte", "Nature d'affaire"),
                Marche("Comptage", "Le nombre de résultats est compté avant l'export. Au-delà de 65 536 lignes, l'export est bloqué : réduire la période ou affiner les critères.", true,
                    "Nombre de lignes"),
                Marche("Colonnes", "Colonnes incluses et leur ordre, à glisser-déposer. Enregistré pour l'utilisateur ou pour le greffe.", false,
                    "Colonnes incluses"),
                Marche("Export", "Le classeur est généré par l'utilitaire d'export. Le bouton reste inactif pendant l'opération.", true,
                    "Classeur"),
            ],
            Lignes =
            [
                Ligne("Instances enrôlées", "Mars 2026 · 1 423 lignes comptées", "Instances", "AFaire"),
                Ligne("Instances avec décision judiciaire", "Mars 2026 · 812 lignes · critères à régler", "Instances", "EnCours", 0),
                Ligne("Instances avec événement futur", "Mars 2026 · 390 lignes · comptées", "Instances", "EnCours", 0, 1, 2),
                Ligne("Instances avec événement passé", "Mars 2026 · 2 210 lignes · colonnes réglées", "Instances", "EnCours", 0, 1, 2, 3),
                Ligne("Instances sans décision judiciaire", "Mars 2026 · 97 lignes · exportée", "Instances", "Validee", 0, 1, 2, 4),
                Ligne("Groupements d'instances", "Code d'acte et nature d'affaire réglés", "Groupements", "EnCours", 0, 1),
                Ligne("Affaires sans décision définitive", "71 204 lignes : au-delà de 65 536, export bloqué", "Affaires et citations", "EnAttente", 0, 1, 2),
                Ligne("Citations signifiées", "Mars 2026 · 58 lignes · exportée", "Affaires et citations", "Validee", 0, 1, 2, 4),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
