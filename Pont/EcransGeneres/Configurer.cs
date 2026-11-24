namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE CONFIGURER du menu (lot 7 du plan « les 125 ecrans manquants du menu, au style
// Commandes web ») : le parametrage du greffe, ses acteurs, ses referentiels, ses modeles,
// ses exports et ses outils. Chaque descripteur reprend sa fiche RIG v2
// (Documentation\reference\proc\proc_<code>.md) : la section « Detail OPE » fait foi. Les
// etapes deviennent les marches, les regles les consignes, les messages les textes cites.
// Les lignes sont une demonstration anonyme (noms fictifs).
//
// Teinte du verbe : rang 7, classe rig-tcw__rang-7 dans wwwroot/rig-ecrans-generes.css (lot 9,
// deja sur main). rig-accueil.css n est pas modifie pour cela.
//
// Les procedures sans « Detail OPE » dans leur fiche (PROC_GED_NATURE_DOC) sont decrites d'apres
// leurs autres sections.
//
// PROC_PARAM_LISTEEXCEL n'a PAS de descripteur : sa fiche RIG v2 ne decrit aucune fonction
// (« Nature reelle du plugin : une coquille, pas une couche OPE »). Un ecran inventerait du
// contenu, ce que le plan interdit : il reste « a venir » dans le menu, a trancher par le chef.

public sealed class DomaineConfigurer : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_MANDATAIRE", Titre = "Mandataires", Sujet = "mandataires",
            Recherche = "Rechercher un mandataire, un abrégé, un SIREN…",
            Criteres = ["Désignation", "Abrégé", "SIREN"],
            // Bouton final cite par la fiche v2 : DoSauve : Mandataire.Sauve (proc_mandataire.md, Detail OPE)
            Valider = "Enregistrer le mandataire",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Personne physique", "Personne morale"],
            Marches =
            [
                new MarcheEcran("Identité", "La civilité et la désignation sont obligatoires. Un SIREN saisi interroge Infogreffe : la fiche se remplit si le service répond.", true,
                    ["Civilité", "Désignation", "SIREN"]),
                new MarcheEcran("Adresse et contacts", "L'adresse est un bloc unique. Un courriel mal formé est refusé : « Email invalide ».", false,
                    ["Adresse", "Téléphone", "Fax", "Courriel"]),
                new MarcheEcran("Abrégé", "Si le mandataire est répertorié, l'abrégé est obligatoire (16 caractères au plus) et unique : « L'abrégé ne peut être vide » ou « Abrege existant ». Décocher « Répertorier » efface l'abrégé.", false,
                    ["Répertorié", "Abrégé", "Numéro CNBF"]),
                new MarcheEcran("Client de facturation", "Le client est choisi parmi les comptes en compte du greffe ; la recherche client ouvre une fenêtre dédiée.", false,
                    ["Client de facturation", "Recherche client"]),
                new MarcheEcran("Qualification", "La qualification de la profession et les infos complémentaires. L'état « actif » n'apparaît qu'en modification.", false,
                    ["Qualification", "Infos complémentaires", "Actif"]),
            ],
            Lignes =
            [
                new LigneEcran("Me Irène Ardoise", "Personne physique · répertoriée", "Personne physique", "Validee", [0, 1, 2]),
                new LigneEcran("Me Simon Bélier", "Personne physique · non répertoriée", "Personne physique", "Validee", [0, 1]),
                new LigneEcran("Me Olga Chantelle", "Personne physique · abrégé à saisir", "Personne physique", "EnCours", [0]),
                new LigneEcran("Me Théo Dorval", "Personne physique · à compléter", "Personne physique", "AFaire", []),
                new LigneEcran("Cabinet Kappa Avocats SELARL", "Personne morale · répertoriée", "Personne morale", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Lambda Conseil SELARL", "Personne morale · SIREN recherché", "Personne morale", "EnCours", [0]),
                new LigneEcran("Mu Représentation SAS", "Personne morale · à compléter", "Personne morale", "AFaire", []),
                new LigneEcran("Nu Juris Services SAS", "Personne morale · qualification en cours", "Personne morale", "EnCours", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RAPPACTEUR", Titre = "Rapprocher des acteurs", Sujet = "acteurs à fusionner",
            Recherche = "Rechercher un acteur à garder…",
            Criteres = ["Type d'acteur", "Acteur à garder", "Acteurs à supprimer"],
            // Bouton final cite par la fiche v2 : _Valider, sauvegarde apres confirmation (proc_rappacteur.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À choisir", "En cours", "Fusionné"),
            Groupes = ["Juges", "Mandataires", "Mandataires PC"],
            Marches =
            [
                new MarcheEcran("Type d'acteur", "Un seul type à la fois : juges, mandataires ou mandataires PC. Changer de type vide la sélection.", true,
                    ["Type d'acteur"]),
                new MarcheEcran("Acteur à garder", "Seul un acteur actif peut être gardé. Le juge paramétré comme juge système ne peut pas être gardé.", true,
                    ["Acteur à garder", "Fiche de l'acteur gardé"]),
                new MarcheEcran("Acteurs à supprimer", "Au moins un acteur à supprimer est coché. Un mandataire actif qui a un numéro CNBF ne peut pas être supprimé.", true,
                    ["Acteurs à supprimer"]),
                new MarcheEcran("Liaisons reprises", "Toutes les liaisons des acteurs supprimés (instances, décisions, audiences, éditions) passent à l'acteur gardé, dans une seule transaction.", false,
                    ["Instances", "Décisions", "Audiences", "Éditions"]),
                new MarcheEcran("Confirmation", "Le nom de l'acteur gardé et la liste des acteurs supprimés sont affichés. Sans « Oui », rien n'est écrit.", true,
                    ["Confirmation"]),
                new MarcheEcran("Piste d'audit", "Chaque suppression laisse une trace horodatée : utilisateur, acteur supprimé, acteur gardé.", false,
                    ["Trace"]),
            ],
            Lignes =
            [
                new LigneEcran("Juge Alpha → Juge Bêta", "Juges · liaisons reprises, audit fait", "Juges", "Validee", [0, 1, 2, 3, 4, 5]),
                new LigneEcran("Juge Gamma → Juge Delta", "Juges · confirmation en attente", "Juges", "EnCours", [0, 1, 2]),
                new LigneEcran("Juge Épsilon", "Juges · acteur à garder à choisir", "Juges", "EnCours", [0]),
                new LigneEcran("Mandataire Zêta → Mandataire Êta", "Mandataires · fusion close", "Mandataires", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Mandataire Thêta", "Mandataires · aucun doublon", "Mandataires", "AFaire", []),
                new LigneEcran("Mandataire Iota → Mandataire Kappa", "Mandataires · CNBF bloquant", "Mandataires", "EnCours", [0, 1]),
                new LigneEcran("Mandataire PC Lambda → Mu", "Mandataires PC · doublon coché", "Mandataires PC", "EnCours", [0, 1, 2]),
                new LigneEcran("Mandataire PC Nu", "Mandataires PC · à choisir", "Mandataires PC", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SUPP_ACTEUR_JUD", Titre = "Remplacer un acteur", Sujet = "acteurs à remplacer",
            Recherche = "Rechercher un acteur à remplacer…",
            Criteres = ["Type d'acteur", "Acteur remplacé", "Remplaçant"],
            // Bouton final cite par la fiche v2 : bouton Valider (proc_supp_acteur_jud.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À choisir", "En cours", "Remplacé"),
            Groupes = ["Magistrats et greffe", "Mandataires PC", "Intervenants PC"],
            Marches =
            [
                new MarcheEcran("Type d'acteur", "Juge, parquetier, greffier assermenté, mandataire PC, intervenant PC ou expert : le type fixe les contrôles.", true,
                    ["Type d'acteur"]),
                new MarcheEcran("Acteur remplacé", "L'acteur à remplacer est pris dans la liste de son type. Le contrôle d'existence vérifie qu'il existe bien pour ce type.", true,
                    ["Acteur remplacé"]),
                new MarcheEcran("Remplaçant", "Le remplaçant doit être différent de l'acteur remplacé : sinon le message « même acteur » est affiché.", true,
                    ["Remplaçant"]),
                new MarcheEcran("Liaisons reprises", "Instances, décisions, audiences et composition passent au remplaçant, en une seule transaction. Une erreur annule toute la reprise.", false,
                    ["Instances", "Décisions", "Audiences", "Composition"]),
                new MarcheEcran("Clôture", "La demande RIG est clôturée après la reprise. Sans reprise complète, rien n'est écrit.", true,
                    ["Clôture de la demande"]),
            ],
            Lignes =
            [
                new LigneEcran("Juge Alpha → Juge Bêta", "Magistrats et greffe · reprise close", "Magistrats et greffe", "Validee", [0, 1, 2, 3, 4]),
                new LigneEcran("Parquetier Gamma → Parquetier Delta", "Magistrats et greffe · reprise à lancer", "Magistrats et greffe", "EnCours", [0, 1]),
                new LigneEcran("Greffier Épsilon", "Magistrats et greffe · remplaçant à choisir", "Magistrats et greffe", "EnCours", [0, 1]),
                new LigneEcran("Greffier Zêta", "Magistrats et greffe · à saisir", "Magistrats et greffe", "AFaire", []),
                new LigneEcran("Mandataire PC Êta → Thêta", "Mandataires PC · reprise close", "Mandataires PC", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Mandataire PC Iota", "Mandataires PC · remplaçant à choisir", "Mandataires PC", "EnCours", [0, 1]),
                new LigneEcran("Mandataire PC Kappa", "Mandataires PC · à saisir", "Mandataires PC", "AFaire", []),
                new LigneEcran("Intervenant Lambda → Mu", "Intervenants PC · même acteur à corriger", "Intervenants PC", "EnCours", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTTIERSPAYEURS", Titre = "Tiers payeurs", Sujet = "tiers payeurs",
            Recherche = "Rechercher un tiers payeur, un abrégé, une ville…",
            Criteres = ["Abrégé", "Désignation", "Ville"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gesttierspayeurs.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Payeur principal", "Autre payeur"],
            Marches =
            [
                new MarcheEcran("Abrégé", "Obligatoire, 10 caractères au plus, unique dans le greffe : « L'abrégé saisi existe déjà ». « DEBITEUR » est réservé : « L'abrégé débiteur n'est pas valable ». Les abrégés réservés par le greffe sont en lecture seule.", true,
                    ["Abrégé"]),
                new MarcheEcran("Identité", "La désignation est saisie ici. Un mandataire peut être proposé pour pré-remplir la fiche, sur confirmation.", true,
                    ["Désignation", "Mandataire rattaché"]),
                new MarcheEcran("Adresse", "Le code postal propose la ville, seulement si la ville est vide. La validation la recalcule si le code postal ou la ville a changé.", false,
                    ["Adresse", "Code postal", "Ville"]),
                new MarcheEcran("Enregistrement", "Après enregistrement, l'écran reste en saisie et la liste se rafraîchit. Supprimer n'est possible qu'en modification, après confirmation, et seulement si le tiers payeur n'est lié ni à une ventilation ni à une facture.", true,
                    ["Enregistrer", "Supprimer"]),
            ],
            Lignes =
            [
                new LigneEcran("Tiers Alpha", "Payeur principal · abrégé ALPHA", "Payeur principal", "Validee", [0, 1, 3]),
                new LigneEcran("Tiers Bêta", "Payeur principal · adresse à compléter", "Payeur principal", "EnCours", [0, 1]),
                new LigneEcran("Tiers Gamma", "Autre payeur · enregistré", "Autre payeur", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Tiers Delta", "Autre payeur · en saisie", "Autre payeur", "EnCours", [0]),
                new LigneEcran("Tiers Épsilon", "Autre payeur · à compléter", "Autre payeur", "AFaire", []),
                new LigneEcran("Tiers Zêta", "Payeur principal · à compléter", "Payeur principal", "AFaire", []),
                new LigneEcran("Tiers Êta", "Autre payeur · rattaché à une personne", "Autre payeur", "EnCours", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CNI", Titre = "Non-inscrits", Sujet = "non-inscrits",
            Recherche = "Rechercher une personne hors registre, un nom, un SIREN…",
            Criteres = ["Type de non-inscrit", "Personne physique ou morale", "SIREN"],
            // Bouton final cite par la fiche v2 : VALIDER, visible en phase Saisie (proc_cni.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Créé"),
            Groupes = ["Personne physique", "Personne morale"],
            Marches =
            [
                new MarcheEcran("Type et identité", "Le type (judiciaire, procédure collective, autre) et la civilité décident des champs : personne physique (nom de naissance, prénoms, nom d'usage) ou personne morale (dénomination, forme juridique).", true,
                    ["Type de non-inscrit", "Civilité", "Nom de naissance", "Prénoms", "Nom d'usage", "Dénomination", "Forme juridique", "SIREN"]),
                new MarcheEcran("Naissance", "Pour une personne physique : date, département, nationalité et pays de naissance. Le pays est la France par défaut.", false,
                    ["Date de naissance", "Département", "Nationalité", "Pays de naissance"]),
                new MarcheEcran("Adresse", "L'adresse est portée par l'établissement principal, créé si besoin. Le domicile se renseigne ici pour une personne physique.", true,
                    ["Adresse", "Adresse de domicile", "Greffe (hors ressort)"]),
                new MarcheEcran("Entreprise et établissements", "Une entreprise représentante est créée. Un second établissement est ajouté pour un type judiciaire ou une procédure collective.", false,
                    ["Entreprise représentante", "Établissement secondaire"]),
                new MarcheEcran("Enregistrement", "Le numéro de non-inscrit est attribué automatiquement pour l'année de travail. La demande prend le nom de la personne.", true,
                    ["Numéro de non-inscrit"]),
            ],
            Lignes =
            [
                new LigneEcran("Mu Dorval", "Personne physique · judiciaire · naissance renseignée", "Personne physique", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Nu Lacroix", "Personne physique · procédure collective", "Personne physique", "Validee", [0, 2, 3, 4]),
                new LigneEcran("Xi Pradel", "Personne physique · en saisie", "Personne physique", "EnCours", [0, 2]),
                new LigneEcran("Omicron Vidal", "Personne physique · à saisir", "Personne physique", "AFaire", []),
                new LigneEcran("Pi Services SARL", "Personne morale · créée", "Personne morale", "Validee", [0, 2, 3, 4]),
                new LigneEcran("Rho Logistique SAS", "Personne morale · adresse à saisir", "Personne morale", "EnCours", [0]),
                new LigneEcran("Sigma Transports", "Personne morale · à saisir", "Personne morale", "AFaire", []),
                new LigneEcran("Tau Habitat SARL", "Personne morale · en saisie", "Personne morale", "EnCours", [0, 2]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTDATE", Titre = "Dates de travail", Sujet = "dates de travail",
            Recherche = "Rechercher une catégorie de date…",
            Criteres = ["Catégorie de date", "Date de travail", "Date de fraîcheur"],
            // Bouton final cite par la fiche v2 : bouton VALIDER (proc_gestdate.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À vérifier", "Modifiée", "Enregistrée"),
            Groupes = ["Dates d'endettement", "Autres catégories"],
            Marches =
            [
                new MarcheEcran("Date de travail", "Une date postérieure à aujourd'hui est une erreur bloquante. Si la date change, l'ancienne est archivée.", true,
                    ["Date de travail", "Mode automatique"]),
                new MarcheEcran("Date de fraîcheur", "Une date postérieure à aujourd'hui donne un avertissement (« très dangereux ») : la saisie reste possible.", true,
                    ["Date de fraîcheur", "Mode automatique"]),
                new MarcheEcran("Commandes à éditer", "Pour l'état d'endettement seulement : visible s'il existe des commandes en attente. Le bouton Édition lance la génération des éditions.", false,
                    ["Commandes en attente", "Éditer les commandes"]),
                new MarcheEcran("Enregistrement", "Chaque changement est noté dans l'audit de la demande (« ancien => nouveau »). Une erreur de sauvegarde est signalée à l'utilisateur.", true,
                    ["Audit"]),
            ],
            Lignes =
            [
                new LigneEcran("État d'endettement", "Date de travail 12/03/2026 · 3 commandes à éditer", "Dates d'endettement", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Commandes d'endettement", "3 commandes en attente · fraîcheur à vérifier", "Dates d'endettement", "EnCours", [0]),
                new LigneEcran("Calendrier judiciaire", "Date de travail 13/03/2026", "Autres catégories", "Validee", [0, 1, 3]),
                new LigneEcran("Jours ouvrés", "Fraîcheur modifiée", "Autres catégories", "EnCours", [1]),
                new LigneEcran("Jours fériés", "Pas encore saisie", "Autres catégories", "AFaire", []),
                new LigneEcran("Éditions pluriannuelles", "Date de travail à saisir", "Autres catégories", "AFaire", []),
                new LigneEcran("Ancienne date archivée", "Dates d'endettement · mode automatique", "Dates d'endettement", "EnCours", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_WIKI", Titre = "Aide du greffe", Sujet = "articles d'aide",
            Recherche = "Rechercher un article, un mot-clé…",
            Criteres = ["Catégorie", "Auteur", "Titre"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_wiki.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À rédiger", "En rédaction", "Publié"),
            Groupes = ["Exploitation", "Développement", "Foire aux questions"],
            Marches =
            [
                new MarcheEcran("Titre et catégorie", "Le titre et la catégorie rangent l'article sous sa rubrique du wiki.", true,
                    ["Titre", "Catégorie"]),
                new MarcheEcran("Contenu", "Le texte est saisi dans l'éditeur, ou un document (RTF, PDF, DOC) est joint. Un fichier Word est copié localement avant édition : pas de lien direct vers le fichier source.", true,
                    ["Texte", "Document joint"]),
                new MarcheEcran("Auteur", "Le nom d'auteur s'affiche sous l'article, en forme abrégée (« X. EXEMPLE ») ou complète.", false,
                    ["Auteur"]),
                new MarcheEcran("Enregistrement et suppression", "Enregistrer écrit l'article. Supprimer demande une double confirmation ; un article verrouillé par un autre utilisateur ne peut pas être supprimé.", true,
                    ["Enregistrer", "Supprimer"]),
            ],
            Lignes =
            [
                new LigneEcran("Sauvegarde du poste", "Exploitation · publié", "Exploitation", "Validee", [0, 1, 3]),
                new LigneEcran("Reprise après incident", "Exploitation · texte à relire", "Exploitation", "EnCours", [0, 1]),
                new LigneEcran("Droits des utilisateurs", "Exploitation · titre saisi", "Exploitation", "EnCours", [0]),
                new LigneEcran("Nouveautés de la version", "Développement · publié", "Développement", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Notes de version", "Développement · à rédiger", "Développement", "AFaire", []),
                new LigneEcran("Comment ouvrir une affaire", "Foire aux questions · publié", "Foire aux questions", "Validee", [0, 1, 3]),
                new LigneEcran("Imprimer une attestation", "Foire aux questions · en rédaction", "Foire aux questions", "EnCours", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_WEB_BROWSER", Titre = "Page web interne", Sujet = "pages web",
            Recherche = "Rechercher une page, une adresse…",
            Criteres = ["Adresse (URL)", "Site interne ou externe", "Mode détaché"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_web_browser.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À ouvrir", "En cours", "Ouverte"),
            Groupes = ["Sites internes", "Sites externes"],
            Marches =
            [
                new MarcheEcran("Adresse", "L'adresse est le seul paramètre : une adresse vide n'ouvre rien.", true,
                    ["Adresse"]),
                new MarcheEcran("Affichage", "La page s'affiche en plein cadre, sans bordures, dans l'onglet de RIG.", true,
                    ["Page affichée"]),
                new MarcheEcran("Mode détaché", "Une fenêtre séparée n'est proposée que si la fonction de détachement est activée ; sinon la page reste intégrée.", false,
                    ["Détacher la page"]),
            ],
            Lignes =
            [
                new LigneEcran("Portail d'exploitation du greffe", "Site interne · ouverte", "Sites internes", "Validee", [0, 1]),
                new LigneEcran("Guide des procédures", "Site interne · ouverte en détaché", "Sites internes", "Validee", [0, 1, 2]),
                new LigneEcran("Intranet du greffe", "Site interne · adresse à saisir", "Sites internes", "EnCours", [0]),
                new LigneEcran("Tableau de bord de suivi", "Site interne · à ouvrir", "Sites internes", "AFaire", []),
                new LigneEcran("Site d'un prestataire", "Site externe · ouvert", "Sites externes", "Validee", [0, 1]),
                new LigneEcran("Page d'actualité externe", "Site externe · à ouvrir", "Sites externes", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GTABREF", Titre = "Tables de reference", Sujet = "codes de référence",
            Recherche = "Rechercher un code, un libellé, une table…",
            Criteres = ["Table", "Code", "Libellé"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gtabref.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Procédures collectives", "Actes et natures", "Annonces et juridictions"],
            Marches =
            [
                new MarcheEcran("Table", "Le code est choisi dans la table de référence voulue ; chaque table a ses propres champs.", true,
                    ["Table"]),
                new MarcheEcran("Code", "La clé (le code) ne peut pas être vide : elle sert de référence aux autres écrans.", true,
                    ["Code"]),
                new MarcheEcran("Libellé", "Le libellé et les champs propres à la table sont saisis ici.", true,
                    ["Libellé", "Champs de la table"]),
                new MarcheEcran("Copie et verrou", "Dupliquer recopie les champs, sauf ceux qui ne se dupliquent pas. Un enregistrement verrouillé par un autre poste ne se modifie pas.", false,
                    ["Dupliquer", "Verrou"]),
                new MarcheEcran("Enregistrement", "Enregistrer écrit le code ; la liste se rafraîchit.", true,
                    ["Enregistrer"]),
            ],
            Lignes =
            [
                new LigneEcran("État « ouvert »", "Procédures collectives · code 01", "Procédures collectives", "Validee", [0, 1, 2, 4]),
                new LigneEcran("État « clôturé »", "Procédures collectives · code 02", "Procédures collectives", "Validee", [0, 1, 2, 4]),
                new LigneEcran("État « en attente »", "Procédures collectives · libellé à saisir", "Procédures collectives", "EnCours", [0, 1]),
                new LigneEcran("Nature « jugement »", "Actes et natures · copie en cours", "Actes et natures", "EnCours", [0, 1, 2]),
                new LigneEcran("Nature « ordonnance »", "Actes et natures · à compléter", "Actes et natures", "AFaire", []),
                new LigneEcran("Journal d'annonces légales", "Annonces et juridictions · enregistré", "Annonces et juridictions", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Juridiction extérieure", "Annonces et juridictions · verrouillée par un autre poste", "Annonces et juridictions", "EnCours", [0]),
                new LigneEcran("Juridiction de référé", "Annonces et juridictions · à compléter", "Annonces et juridictions", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GED_NATURE_DOC", Titre = "Natures de document", Sujet = "natures de document",
            Recherche = "Rechercher une nature de document…",
            Criteres = ["Libellé", "Code"],
            // Bouton final cite par la fiche v2 : post-validation (proc_ged_nature_doc.md, logique metier)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Décisions", "Pièces et actes"],
            Marches =
            [
                new MarcheEcran("Libellé", "Le libellé est obligatoire : il sert à classer les documents de la GED.", true,
                    ["Libellé"]),
                new MarcheEcran("Code", "Le code identifie la nature dans le référentiel.", true,
                    ["Code"]),
                new MarcheEcran("Enregistrement", "Après validation, retour en lecture seule et rafraîchissement de la liste. Le bouton Nouveau ouvre la saisie.", true,
                    ["Enregistrer", "Nouveau"]),
            ],
            Lignes =
            [
                new LigneEcran("Jugement", "Décisions · enregistrée", "Décisions", "Validee", [0, 1, 2]),
                new LigneEcran("Ordonnance", "Décisions · enregistrée", "Décisions", "Validee", [0, 1, 2]),
                new LigneEcran("Jugement avant dire droit", "Décisions · code à saisir", "Décisions", "EnCours", [0]),
                new LigneEcran("Pièce d'identité", "Pièces et actes · en cours", "Pièces et actes", "EnCours", [0, 1]),
                new LigneEcran("Courrier reçu", "Pièces et actes · à compléter", "Pièces et actes", "AFaire", []),
                new LigneEcran("Procès-verbal", "Pièces et actes · à compléter", "Pièces et actes", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CABINET", Titre = "Cabinets", Sujet = "cabinets",
            Recherche = "Rechercher un cabinet, une date d'audience…",
            Criteres = ["Date d'audience", "Type de cabinet", "Juge"],
            // Bouton final cite par la fiche v2 : DoSauve(cnx, futurEtat) (proc_cabinet.md, Detail OPE)
            Valider = "Enregistrer le cabinet",
            Rang = 7,
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Enregistré"),
            Groupes = ["Salles fixes", "Salles ponctuelles"],
            Marches =
            [
                new MarcheEcran("Audience", "La date et l'heure d'audience sont obligatoires. Un doublon à la même date et à la même heure est refusé.", true,
                    ["Date d'audience", "Heure d'audience"]),
                new MarcheEcran("Type et code", "Le type de cabinet est obligatoire ; le code est exigé selon le type.", true,
                    ["Type de cabinet", "Code cabinet"]),
                new MarcheEcran("Juge et greffier", "Le juge et le greffier se choisissent dans les listes. En modification, le juge n'est plus modifiable quand il est renseigné.", false,
                    ["Juge", "Greffier"]),
                new MarcheEcran("Classement", "Le type de classement admis est écrit à l'enregistrement.", false,
                    ["Classement admis"]),
                new MarcheEcran("Enregistrement", "L'enregistrement met aussi à jour le nom de la demande.", true,
                    ["Enregistrer"]),
            ],
            Lignes =
            [
                new LigneEcran("Salle 1 · 14/03/2026 à 9 h", "Salles fixes · enregistrée", "Salles fixes", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Salle 2 · 14/03/2026 à 10 h", "Salles fixes · juge à renseigner", "Salles fixes", "EnCours", [0, 1]),
                new LigneEcran("Salle 3 · 15/03/2026 à 9 h", "Salles fixes · à saisir", "Salles fixes", "AFaire", []),
                new LigneEcran("Salle 4 · 19/03/2026 à 14 h", "Salles fixes · en saisie", "Salles fixes", "EnCours", [0, 1, 2]),
                new LigneEcran("Salle temporaire A · 16/03/2026", "Salles ponctuelles · enregistrée", "Salles ponctuelles", "Validee", [0, 1, 2, 3, 4]),
                new LigneEcran("Salle temporaire B · 17/03/2026", "Salles ponctuelles · type à choisir", "Salles ponctuelles", "EnCours", [0]),
                new LigneEcran("Salle temporaire C · 18/03/2026", "Salles ponctuelles · à saisir", "Salles ponctuelles", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARAM_IP", Titre = "Codes chefs IP", Sujet = "codes chefs",
            Recherche = "Rechercher un code chef…",
            Criteres = ["Code chef", "Préfixe IP", "Ordre"],
            // Bouton final cite par la fiche v2 : DoSauveAfterOperation (proc_param_ip.md, Detail OPE)
            Valider = "Enregistrer la liste",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Codes IP proposables", "Autres codes"],
            Marches =
            [
                new MarcheEcran("Ajout", "Seuls les codes chefs commençant par IP sont proposés. Un code déjà dans la liste est refusé ; un code qui ne se résout pas n'est pas ajouté.", true,
                    ["Code chef"]),
                new MarcheEcran("Ordre", "L'ordre choisi par l'utilisateur est conservé tel quel dans la liste.", false,
                    ["Ordre des codes"]),
                new MarcheEcran("Enregistrement", "La liste est réécrite en entier. Une liste vide est refusée. Un code introuvable au rechargement disparaît à la prochaine sauvegarde.", true,
                    ["Enregistrer"]),
            ],
            Lignes =
            [
                new LigneEcran("IP01", "Code proposable · premier de la liste", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("IP02", "Code proposable · deuxième de la liste", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("IP03", "Code proposable · à ajouter", "Codes IP proposables", "EnCours", [0]),
                new LigneEcran("IP04", "Code proposable · en attente", "Codes IP proposables", "AFaire", []),
                new LigneEcran("IP05", "Code proposable · à ajouter", "Codes IP proposables", "AFaire", []),
                new LigneEcran("Code hors IP", "Autre code résolu · conservé à l'ouverture", "Autres codes", "EnCours", [0, 1]),
                new LigneEcran("Code introuvable", "Ignoré au prochain enregistrement", "Autres codes", "EnCours", [0]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BORNE", Titre = "Bornes d'accueil", Sujet = "essais de bornes",
            Recherche = "Rechercher un essai, un article, une borne…",
            Criteres = ["Article", "Dossier de test", "Journal"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_borne.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À lancer", "En cours", "Terminé"),
            Groupes = ["Calcul de prix", "Commandes de test", "Journal"],
            Marches =
            [
                new MarcheEcran("Article", "Calcul du prix d'un article de la borne. Lecture seule : rien n'est écrit dans RIG.", true,
                    ["Article", "Prix calculé"]),
                new MarcheEcran("Sondes", "Lectures de contrôle de la borne, sans effet sur les dossiers.", false,
                    ["Sonde", "Résultat de lecture"]),
                new MarcheEcran("Commande", "Scénario complet de commande : il crée ou valide des demandes côté serveur de la borne, jamais dans RIG. Réservé aux bases de développement (codes greffe 9995 et 9999).", true,
                    ["Dossier", "Acte", "Montant"]),
                new MarcheEcran("Journal", "Visualiseur des journaux de la borne, en lecture seule.", false,
                    ["Journal"]),
            ],
            Lignes =
            [
                new LigneEcran("Extrait Kbis · article 1", "Calcul de prix · terminé", "Calcul de prix", "Validee", [0, 2]),
                new LigneEcran("Bilan · article 2", "Calcul de prix · à lancer", "Calcul de prix", "AFaire", []),
                new LigneEcran("Scénario de commande A", "Commandes de test · terminé", "Commandes de test", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Scénario de commande B", "Commandes de test · en cours", "Commandes de test", "EnCours", [0, 1]),
                new LigneEcran("Sonde de lecture C", "Commandes de test · à lancer", "Commandes de test", "AFaire", []),
                new LigneEcran("Journal du 12/03/2026", "Journal · lecture seule", "Journal", "EnCours", [3]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REDAA", Titre = "Modeles de redaction", Sujet = "modèles de décision",
            Recherche = "Rechercher un modèle, une maquette, une section…",
            Criteres = ["Modèle", "Maquette", "Section"],
            // Bouton final cite par la fiche v2 : bouton VALIDER (proc_redaa.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Modèles et maquettes", "Sections et glossaire", "Jugements"],
            Marches =
            [
                new MarcheEcran("Modèle", "Le modèle est recherché puis édité. Le mode d'ouverture choisit les onglets affichés.", true,
                    ["Nom du modèle"]),
                new MarcheEcran("Maquette", "La maquette du document est éditée. Une seule étape est visible à la fois.", true,
                    ["Maquette"]),
                new MarcheEcran("Sections", "Les sections réutilisables de la décision.", false,
                    ["Sections"]),
                new MarcheEcran("Glossaire", "Les termes du glossaire utilisés dans les modèles.", false,
                    ["Termes du glossaire"]),
                new MarcheEcran("Fonctions avancées", "Fonctions avancées de rédaction, dans un onglet à part.", false,
                    ["Fonctions avancées"]),
            ],
            Lignes =
            [
                new LigneEcran("Modèle de jugement ordinaire", "Modèles et maquettes · enregistré", "Modèles et maquettes", "Validee", [0, 1]),
                new LigneEcran("Modèle d'ordonnance", "Modèles et maquettes · maquette en cours", "Modèles et maquettes", "EnCours", [0]),
                new LigneEcran("Modèle de référé", "Modèles et maquettes · à compléter", "Modèles et maquettes", "AFaire", []),
                new LigneEcran("Section « motifs »", "Sections et glossaire · enregistrée", "Sections et glossaire", "Validee", [0, 1, 2]),
                new LigneEcran("Glossaire des parties", "Sections et glossaire · en cours", "Sections et glossaire", "EnCours", [0, 1]),
                new LigneEcran("Jugement · administration", "Jugements · à compléter", "Jugements", "AFaire", []),
                new LigneEcran("Jugement · dispositif", "Jugements · enregistré", "Jugements", "Validee", [0, 1, 3]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SCEAU", Titre = "Sceaux", Sujet = "sceaux",
            Recherche = "Rechercher un sceau, une référence…",
            Criteres = ["Référence", "Hauteur maximale", "Image"],
            // Bouton final cite par la fiche v2 : VALIDER, ANNULER_ROUGE (proc_sceau.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Hauteur de 24 mm", "Autre hauteur"],
            Marches =
            [
                new MarcheEcran("Référence", "La référence est unique : un doublon est refusé avant l'enregistrement.", true,
                    ["Référence"]),
                new MarcheEcran("Dimensions", "La hauteur maximale se saisit ; la largeur est fixe et jamais saisie. La case « dimensions du lot 1 » se coche d'elle-même pour une hauteur de 24 mm.", true,
                    ["Hauteur maximale", "Dimensions du lot 1"]),
                new MarcheEcran("Image", "L'image du sceau est archivée dans le dossier GED du sceau. « Récupérer la dernière version » copie la version de travail vers un dossier choisi.", false,
                    ["Image", "Dernière version"]),
                new MarcheEcran("Enregistrement", "Valider et Annuler ne s'activent que si la saisie a changé. Annuler rétablit les valeurs enregistrées.", true,
                    ["Valider", "Annuler"]),
            ],
            Lignes =
            [
                new LigneEcran("Sceau du greffe", "Hauteur de 24 mm · enregistré", "Hauteur de 24 mm", "Validee", [0, 1, 3]),
                new LigneEcran("Sceau du président", "Hauteur de 24 mm · image à archiver", "Hauteur de 24 mm", "EnCours", [0, 1]),
                new LigneEcran("Sceau de la chambre", "Hauteur de 24 mm · à compléter", "Hauteur de 24 mm", "AFaire", []),
                new LigneEcran("Sceau du tribunal", "Hauteur de 24 mm · en saisie", "Hauteur de 24 mm", "EnCours", [0, 1]),
                new LigneEcran("Sceau de la section", "Autre hauteur · enregistré", "Autre hauteur", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Sceau d'annexe", "Autre hauteur · référence à saisir", "Autre hauteur", "EnCours", [0]),
                new LigneEcran("Sceau de remplacement", "Autre hauteur · à compléter", "Autre hauteur", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTMODEPAIEMENT", Titre = "Modes de paiement", Sujet = "modes de paiement",
            Recherche = "Rechercher un mode de paiement, un code, une banque…",
            Criteres = ["Code", "Libellé", "Type"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gestmodepaiement.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Espèces et chèque", "Virement et carte"],
            Marches =
            [
                new MarcheEcran("Code", "Le code (5 caractères au plus) est obligatoire et unique. Il se verrouille dès qu'un encaissement ou un procès-verbal utilise le mode.", true,
                    ["Code"]),
                new MarcheEcran("Libellé", "Le libellé est obligatoire, six caractères au moins, et unique.", true,
                    ["Libellé"]),
                new MarcheEcran("Type et banque", "Le type est obligatoire et se verrouille comme le code. Une banque peut être associée selon le type.", true,
                    ["Type", "Banque"]),
                new MarcheEcran("Options", "Actif : utilisable en saisie. « Édition du remis visible » : affiché sur l'édition du remis.", false,
                    ["Actif", "Édition du remis visible"]),
                new MarcheEcran("Suppression", "Un mode qui a déjà servi à un encaissement ne peut pas être supprimé.", false,
                    ["Supprimer"]),
            ],
            Lignes =
            [
                new LigneEcran("Espèces", "Espèces et chèque · enregistré", "Espèces et chèque", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Chèque bancaire", "Espèces et chèque · enregistré", "Espèces et chèque", "Validee", [0, 1, 2]),
                new LigneEcran("Chèque de banque", "Espèces et chèque · libellé à saisir", "Espèces et chèque", "EnCours", [0]),
                new LigneEcran("Mode déjà encaissé", "Espèces et chèque · code et type verrouillés", "Espèces et chèque", "EnCours", [0, 1, 2]),
                new LigneEcran("Virement", "Virement et carte · enregistré", "Virement et carte", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Carte bancaire", "Virement et carte · type à choisir", "Virement et carte", "EnCours", [0, 1]),
                new LigneEcran("Prélèvement", "Virement et carte · à compléter", "Virement et carte", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARAMEXPRESSO", Titre = "Facturation Expresso", Sujet = "paramètres de facturation",
            Recherche = "Rechercher un paramètre de facturation…",
            Criteres = ["Paramètre", "Banque", "Mode de paiement"],
            // Bouton final cite par la fiche v2 : DoValider (proc_paramexpresso.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À renseigner", "En cours", "Enregistré"),
            Groupes = ["Paramètres généraux", "Pied de page", "Comptabilité"],
            Marches =
            [
                new MarcheEcran("Paramètres généraux", "Les paramètres généraux de la facturation.", true,
                    ["Paramètre", "Valeur"]),
                new MarcheEcran("Banques et modes", "Chaque mode de paiement est associé à sa banque.", false,
                    ["Banque", "Mode de paiement"]),
                new MarcheEcran("Pied de page", "Le pied de page des factures : chaque ligne est un paramètre ; la source de l'adresse peut être basculée.", false,
                    ["Ligne du pied de page", "Source de l'adresse"]),
                new MarcheEcran("Enregistrement", "Enregistrer écrit tous les paramètres ensemble et laisse une trace.", true,
                    ["Enregistrer"]),
            ],
            Lignes =
            [
                new LigneEcran("Taux de TVA par défaut", "Paramètres généraux · enregistré", "Paramètres généraux", "Validee", [0, 3]),
                new LigneEcran("Compte de vente", "Paramètres généraux · en saisie", "Paramètres généraux", "EnCours", [0]),
                new LigneEcran("Virement vers banque principale", "Paramètres généraux · association faite", "Paramètres généraux", "Validee", [0, 1, 3]),
                new LigneEcran("Mention de pied de page", "Pied de page · source d'adresse basculée", "Pied de page", "EnCours", [0, 2]),
                new LigneEcran("Pied de page des avoirs", "Pied de page · à compléter", "Pied de page", "AFaire", []),
                new LigneEcran("Grille de déversement", "Comptabilité · lecture seule", "Comptabilité", "Validee", [0, 3]),
                new LigneEcran("Écritures comptables", "Comptabilité · lecture seule", "Comptabilité", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REQUETES", Titre = "Requetes metier", Sujet = "requêtes",
            Recherche = "Rechercher une requête préparée…",
            Criteres = ["Requête", "Critère", "Résultat"],
            // Bouton final cite par la fiche v2 : DoValider de la saisie de requete (proc_requetes.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À lancer", "En cours", "Exécutée"),
            Groupes = ["Requêtes préparées", "Requêtes à critères"],
            Marches =
            [
                new MarcheEcran("Requête", "La requête est choisie dans la liste des requêtes préparées ; sa description s'affiche.", true,
                    ["Requête"]),
                new MarcheEcran("Critères", "Les critères se saisissent dans un formulaire qui change selon la requête.", false,
                    ["Critères"]),
                new MarcheEcran("Exécution", "La requête est exécutée après le contrôle de sécurité du garde-fou SQL.", true,
                    ["Exécuter"]),
                new MarcheEcran("Résultat", "Le résultat s'affiche en grille.", false,
                    ["Lignes du résultat"]),
            ],
            Lignes =
            [
                new LigneEcran("Dossiers ouverts par mois", "Requêtes préparées · exécutée", "Requêtes préparées", "Validee", [0, 2, 3]),
                new LigneEcran("Mandataires actifs", "Requêtes préparées · à lancer", "Requêtes préparées", "AFaire", []),
                new LigneEcran("Tiers payeurs actifs", "Requêtes préparées · critères à saisir", "Requêtes préparées", "EnCours", [0, 1]),
                new LigneEcran("Instances du trimestre", "Requêtes à critères · critères à saisir", "Requêtes à critères", "EnCours", [0]),
                new LigneEcran("Affaires par cabinet", "Requêtes à critères · exécution en cours", "Requêtes à critères", "EnCours", [0, 1]),
                new LigneEcran("Relances de paiement", "Requêtes à critères · exécutée", "Requêtes à critères", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Décisions par juge", "Requêtes à critères · à lancer", "Requêtes à critères", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IA", Titre = "Analyse IA", Sujet = "liasses analysées",
            Recherche = "Rechercher une liasse, un numéro de liasse…",
            Criteres = ["Numéro de liasse", "Type de liasse", "Indice de confiance"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_ia.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À analyser", "En cours", "Analysée"),
            Groupes = ["Liasses CFE", "Liasses DCA", "Liasses DAS"],
            Marches =
            [
                new MarcheEcran("Liasse", "Le numéro de liasse est requis pour lancer l'analyse. Le lancement n'est proposé que s'il n'existe pas encore de réponse.", true,
                    ["Numéro de liasse"]),
                new MarcheEcran("Analyse", "L'analyse tourne sans bloquer l'écran : pendant ce temps, Quitter est verrouillé.", true,
                    ["Lancer l'analyse"]),
                new MarcheEcran("Champs vérifiés", "Chaque champ est comparé : correspondance, non-correspondance, proche ou non disponible.", true,
                    ["Champs vérifiés", "Correspondances", "Non-correspondances", "Proches", "Non disponibles"]),
                new MarcheEcran("Synthèse", "L'indice de confiance global et le commentaire de synthèse. La réanalyse n'appelle pas le service : la réponse enregistrée est rechargée.", false,
                    ["Indice de confiance", "Commentaire"]),
            ],
            Lignes =
            [
                new LigneEcran("Liasse CFE 2026-001", "Liasses CFE · analysée", "Liasses CFE", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Liasse CFE 2026-002", "Liasses CFE · analyse en cours", "Liasses CFE", "EnCours", [0, 1]),
                new LigneEcran("Liasse CFE 2026-003", "Liasses CFE · à analyser", "Liasses CFE", "AFaire", []),
                new LigneEcran("Liasse DCA 2026-004", "Liasses DCA · analysée", "Liasses DCA", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Liasse DCA 2026-005", "Liasses DCA · numéro à saisir", "Liasses DCA", "EnCours", [0]),
                new LigneEcran("Liasse DAS 2026-006", "Liasses DAS · à analyser", "Liasses DAS", "AFaire", []),
                new LigneEcran("Liasse DAS 2026-007", "Liasses DAS · analysée", "Liasses DAS", "Validee", [0, 1, 2, 3]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CFENET_RET", Titre = "Retours CFE-NET", Sujet = "retours",
            Recherche = "Rechercher un dossier, un numéro de retour…",
            Criteres = ["Période", "Rejets", "Corrections du greffe"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_cfenet_ret.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traité"),
            Groupes = ["Rejets", "Retours à traiter", "Retours traités"],
            Marches =
            [
                new MarcheEcran("Recherche", "La recherche se fait par période et par critères : rejet, traitement, masquage, date de retour.", true,
                    ["Période", "Rejets", "Date de retour"]),
                new MarcheEcran("Résultat", "La grille liste les retours. Un menu contextuel donne les gestes sur la ligne.", true,
                    ["Liste des retours"]),
                new MarcheEcran("Corrections du greffe", "Les corrections déjà faites par le greffe s'affichent si la case est cochée.", false,
                    ["Corrections"]),
                new MarcheEcran("Traitement", "Traiter, rejeter ou masquer le retour ; le dossier part ensuite vers la GED.", true,
                    ["Traiter", "Rejeter", "Masquer"]),
            ],
            Lignes =
            [
                new LigneEcran("Retour 2026-0101", "Rejets · à traiter", "Rejets", "AFaire", []),
                new LigneEcran("Retour 2026-0102", "Rejets · rejeté", "Rejets", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Retour 2026-0103", "Retours à traiter · en cours", "Retours à traiter", "EnCours", [0, 1]),
                new LigneEcran("Retour 2026-0104", "Retours à traiter · à traiter", "Retours à traiter", "AFaire", []),
                new LigneEcran("Retour 2026-0105", "Retours traités · traité", "Retours traités", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Retour 2026-0106", "Retours traités · corrigé par le greffe", "Retours traités", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Retour 2026-0107", "Retours à traiter · masqué", "Retours à traiter", "EnCours", [0]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDI_RPVAINS", Titre = "EDI RPVA", Sujet = "demandes d'inscription",
            Recherche = "Rechercher une demande, un numéro CNBF…",
            Criteres = ["Numéro CNBF", "Demandeur", "État de la demande"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_edi_rpvains.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("En attente", "En cours", "Traitée"),
            Groupes = ["Personnes physiques", "Structures"],
            Marches =
            [
                new MarcheEcran("Demande", "Recherche des demandes d'inscription reçues par le réseau RPVA. Une demande déjà traitée ne se traite plus.", true,
                    ["Demandeur", "Numéro CNBF", "État"]),
                new MarcheEcran("Infos du demandeur", "Les informations du demandeur s'affichent.", true,
                    ["Identité", "Courriel du demandeur"]),
                new MarcheEcran("Mandataires candidats", "Les mandataires existants qui correspondent sont cherchés. Cocher « Traiter » leur affecte le numéro CNBF, le SIREN et le courriel du demandeur.", true,
                    ["Traiter", "Principal", "Désactiver"]),
                new MarcheEcran("Courriels", "Si des courriels alternatifs existent sur les lignes traitées, une fenêtre propose de les remplacer.", false,
                    ["Remplacement des courriels"]),
                new MarcheEcran("Refus", "Le refus met la demande à l'état refusée.", false,
                    ["Refuser"]),
            ],
            Lignes =
            [
                new LigneEcran("Demande RPVA 1", "Personnes physiques · traitée", "Personnes physiques", "Validee", [0, 1, 2]),
                new LigneEcran("Demande RPVA 2", "Personnes physiques · mandataire à rattacher", "Personnes physiques", "EnCours", [0, 1]),
                new LigneEcran("Demande RPVA 3", "Personnes physiques · en attente", "Personnes physiques", "AFaire", []),
                new LigneEcran("Demande RPVA 4", "Personnes physiques · courriels à remplacer", "Personnes physiques", "EnCours", [0, 1, 2]),
                new LigneEcran("Demande RPVA 5", "Structures · traitée", "Structures", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Demande RPVA 6", "Structures · SIREN à vérifier", "Structures", "EnCours", [0]),
                new LigneEcran("Demande RPVA 7", "Structures · en attente", "Structures", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDI_RPJC_TRCNC", Titre = "EDI RPJC", Sujet = "affaires RPJC",
            Recherche = "Rechercher une affaire, un numéro de dossier…",
            Criteres = ["Numéro d'affaire", "Mandataire", "État de traitement"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_edi_rpjc_trcnc.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            Groupes = ["À traiter", "En cours", "Historique"],
            Marches =
            [
                new MarcheEcran("Visualisation", "Les documents doivent être visualisés avant le traitement, sauf choix explicite de l'utilisateur sur les documents non vus.", true,
                    ["Documents", "Visualisation"]),
                new MarcheEcran("Verrouillage", "L'affaire est verrouillée au nom de l'utilisateur qui la traite : elle passe dans les affaires en cours.", true,
                    ["Verrou"]),
                new MarcheEcran("Traitement", "Le type de partie (demandeur ou défendeur) et la date de transmission déterminent l'événement créé.", true,
                    ["Type de partie", "Date de transmission"]),
                new MarcheEcran("Rejet", "Les rejets venus en amont sont visibles ici.", false,
                    ["Rejets amont"]),
                new MarcheEcran("Clôture", "Valider enregistre le traitement.", true,
                    ["Valider"]),
            ],
            Lignes =
            [
                new LigneEcran("Affaire RPJC 2026-01", "À traiter · documents à visualiser", "À traiter", "AFaire", []),
                new LigneEcran("Affaire RPJC 2026-02", "À traiter · documents à visualiser", "À traiter", "AFaire", []),
                new LigneEcran("Affaire RPJC 2026-03", "À traiter · rejet amont à lire", "À traiter", "EnCours", [0]),
                new LigneEcran("Affaire RPJC 2026-04", "En cours · verrouillée par vous", "En cours", "EnCours", [0, 1]),
                new LigneEcran("Affaire RPJC 2026-05", "En cours · type de partie à choisir", "En cours", "EnCours", [0, 1]),
                new LigneEcran("Affaire RPJC 2026-06", "Historique · traitée", "Historique", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Affaire RPJC 2026-07", "Historique · traitée", "Historique", "Validee", [0, 1, 2, 4]),
                new LigneEcran("Affaire RPJC 2026-08", "Historique · traitée après rejet", "Historique", "Validee", [0, 1, 2, 3, 4]),
            ],
        },
    ];
}
