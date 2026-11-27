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
// Lot 14 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : les descripteurs des acteurs et referentiels (Mandataires, Rapprocher
// des acteurs, Tiers payeurs, Natures de document, Codes chefs IP, Dates de travail...) sont realignes sur le code V2 que lisent
// leurs lecteurs et ecrivains (Serveur/Socle/EcransGeneres/). fix-ok: mesure le 27/11/2026 en lisant le code V2 -- ces descripteurs
// portaient les criteres, colonnes et sections des exemples du lot 7 (FormRecherche_MANDATAIRE.cs:179-198 cherche par nom, CNBF,
// CNBF de structure et SIREN, pas par abrege ; PROC_RAPPACTEUR n'a ni « Acteurs à supprimer » ni « Confirmation » saisis) : une
// edition par descripteur, chacune citee la ou elle porte.
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : les champs des sections ci-dessous (fiche, Identite, Abrege, Adresse, Qualification).
            Colonnes = ["Désignation", "Civilité", "Abrégé", "SIREN", "Courriel", "Qualification", Champ.Case("Actif")],
            Recherche = "Rechercher un mandataire, un abrégé, un SIREN…",
            // Criteres de la recherche de la V2 (FormRecherche_MANDATAIRE.cs:179-198) : nom, CNBF, CNBF de structure, SIREN.
            Criteres = ["Désignation", "Numéro CNBF", "CNBF de structure", "SIREN"],
            // Bouton final cite par la fiche v2 : DoSauve : Mandataire.Sauve (proc_mandataire.md, Detail OPE)
            Valider = "Enregistrer le mandataire",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Personne physique", "Personne morale"],
            Sections =
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
                    ["Qualification", "Infos complémentaires", Champ.Case("Actif")]),
                // Mandataire.GetLiaisonEntite (Mandataire.cs:477-490) : ce qui reference le mandataire. RIG v2 ne supprime un
                // mandataire qu'apres avoir repris ces liaisons sur un autre (PROC_RAPPACTEUR).
                new MarcheEcran("Utilisations", "Les affaires et pièces qui citent ce mandataire. Un mandataire utilisé ne se supprime pas : ses liaisons se reprennent d'abord sur un autre (« Rapprocher des acteurs »). Un mandataire actif qui a un numéro CNBF ne se supprime pas.", false,
                    [Champ.Tableau("Liaisons")]),
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
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un acteur à garder…",
            // La recherche de la V2 : le type, puis le nom (chaque mot dans le nom, le prenom et l'abrege, _Collate,
            // OPE_SELECTION_ACTEUR.cs:169). Lignes et apercu : LecteurRapprochementActeurs.cs ; fusion : EcrivainRapprochementActeurs.cs.
            Criteres = ["Type d'acteur", "Nom"],
            // Bouton final cite par la fiche v2 : _Valider, sauvegarde apres confirmation (proc_rappacteur.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            // L'etat dit si l'acteur peut etre coche « a supprimer » : un mandataire actif a numero CNBF ne se supprime pas
            // (Res.TEXTE_MANDATAIRE_ACTIF_AVEC_CNBF), sa ligne est figee.
            Etats = EtatsEcran.Trois("Actif", "Inactif", "Protégé (actif, n° CNBF)"),
            Groupes = ["Juges", "Mandataires", "Mandataires PC"],
            Sections =
            [
                new MarcheEcran("Type d'acteur", "Un seul type à la fois : juges, mandataires ou mandataires PC. Les acteurs cochés sont ceux à supprimer ; cochez-les dans la grille, ils sont tous du même type.", false,
                    [Champ.Liste("Type d'acteur", "Juges", "Mandataires", "Mandataires PC")]),
                new MarcheEcran("Acteur à garder", "Le numéro ou l'abrégé de l'acteur qui reprend les affaires : du même type, actif et répertorié (avec un abrégé), et non coché. Un mandataire actif qui a un numéro CNBF ne peut pas être supprimé.", true,
                    ["Acteur à garder"]),
                new MarcheEcran("Liaisons reprises", "Ouvrez la ligne d'un acteur coché : ses liaisons et les affaires touchées passeront à l'acteur gardé, dans une seule transaction, puis il sera supprimé. ATTENTION CETTE OPERATION EST IRREVERSIBLE.", false,
                    [Champ.Tableau("Liaisons"), Champ.Tableau("Affaires touchées")]),
                new MarcheEcran("Piste d'audit", "Chaque suppression laisse une trace datée (ACTEUR_SUPP) : l'auteur, l'acteur supprimé, l'acteur gardé et les reprises faites.", false,
                    []),
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
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un acteur à remplacer…",
            Criteres = ["Type d'acteur", "Acteur remplacé", "Remplaçant"],
            // Bouton final cite par la fiche v2 : bouton Valider (proc_supp_acteur_jud.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À choisir", "En cours", "Remplacé"),
            Groupes = ["Magistrats et greffe", "Mandataires PC", "Intervenants PC"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            // La grille et le detail de la V2 (TiersPayeursRigDataGridView.cs ; OPE_TIERSPAYEURSDETAIL.cs:57-66) : la liste ne montre que
            // les payeurs principaux (Payeur.GetPayeursPrincipaux), d'ou les groupes actifs / inactifs (PYEUR_IS_ACTIF), pas « autre payeur ».
            // Lignes : LecteurTiersPayeurs.cs ; gestes : EcrivainTiersPayeurs.cs.
            Colonnes = ["Abrégé", "Désignation", "Adresse", "Complément", "Code postal", "Ville", "Courriel", Champ.Case("Actif")],
            Recherche = "Rechercher un tiers payeur, un abrégé, une ville…",
            Criteres = ["Abrégé", "Désignation", "Ville"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gesttierspayeurs.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("Sans désignation", "Adresse à compléter", "Enregistré"),
            Groupes = ["Actifs", "Inactifs"],
            Sections =
            [
                new MarcheEcran("Abrégé", "Obligatoire, 10 caractères au plus, unique : « L'abrégé saisi existe déjà ». « DEBITEUR » est réservé : « L'abrégé débiteur n'est pas valable ». Les abrégés ATTENTE et TPG ne se modifient pas.", true,
                    ["Abrégé"]),
                new MarcheEcran("Identité", "La désignation est obligatoire. Le mandataire PC rattaché se lit ici.", true,
                    ["Désignation", "Mandataire rattaché"]),
                new MarcheEcran("Adresse", "L'adresse, son complément, le code postal (nettoyé de ses caractères parasites), la ville et le courriel.", false,
                    ["Adresse", "Complément", "Code postal", "Ville", "Courriel"]),
                new MarcheEcran("Enregistrement", "Supprimer n'est possible que si le tiers payeur n'est lié ni à une ventilation ni à une facture : « Suppression impossible ».", true,
                    [Champ.Case("Actif"), "Utilisations"]),
            ],
            Lignes =
            [
                new LigneEcran("Tiers Alpha", "abrégé ALPHA · 75001 Paris", "Actifs", "Validee", [0, 1, 3]),
                new LigneEcran("Tiers Bêta", "abrégé BETA · adresse à compléter", "Actifs", "EnCours", [0, 1]),
                new LigneEcran("Tiers Gamma", "abrégé GAMMA · 69001 Lyon", "Actifs", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Tiers Delta", "abrégé DELTA · adresse à compléter · inactif", "Inactifs", "EnCours", [0]),
                new LigneEcran("ÉPSILON", "abrégé ÉPSILON · sans désignation", "Actifs", "AFaire", []),
                new LigneEcran("Tiers Êta", "abrégé ETA · 13001 Marseille · inactif", "Inactifs", "Validee", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CNI", Titre = "Non-inscrits", Sujet = "non-inscrits",
            Forme = FormeEcran.Saisie,
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : la categorie (critere) et les champs des sections ci-dessous.
            Colonnes = ["Catégorie de date", "Date de travail", "Date de fraîcheur", Champ.Case("Mode automatique")],
            Recherche = "Rechercher une catégorie de date…",
            Criteres = ["Catégorie de date", "Date de travail", "Date de fraîcheur"],
            // Bouton final cite par la fiche v2 : bouton VALIDER (proc_gestdate.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À vérifier", "Modifiée", "Enregistrée"),
            Groupes = ["Dates d'endettement", "Autres catégories"],
            Sections =
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
            Forme = FormeEcran.Document,
            Recherche = "Rechercher un article, un mot-clé…",
            Criteres = ["Catégorie", "Auteur", "Titre"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_wiki.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À rédiger", "En rédaction", "Publié"),
            Groupes = ["Exploitation", "Développement", "Foire aux questions"],
            Sections =
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
            Forme = FormeEcran.Document,
            Recherche = "Rechercher une page, une adresse…",
            Criteres = ["Adresse (URL)", "Site interne ou externe", "Mode détaché"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_web_browser.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À ouvrir", "En cours", "Ouverte"),
            Groupes = ["Sites internes", "Sites externes"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : la valeur de la table, son code, sa table, son verrou (sections ci-dessous).
            Colonnes = ["Libellé", "Code", "Table", Champ.Case("Verrou")],
            Recherche = "Rechercher un code, un libellé, une table…",
            Criteres = ["Table", "Code", "Libellé"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gtabref.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Procédures collectives", "Actes et natures", "Annonces et juridictions"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Libellé", "Code"],
            Recherche = "Rechercher une nature de document…",
            Criteres = ["Libellé", "Code"],
            // Bouton final cite par la fiche v2 : post-validation (proc_ged_nature_doc.md, logique metier)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Décisions", "Pièces et actes"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : le cabinet (la salle) et les champs des sections ci-dessous.
            Colonnes = ["Cabinet", "Type de cabinet", "Code cabinet", "Date d'audience", "Heure d'audience", "Juge", "Greffier", Champ.Case("Classement admis")],
            Recherche = "Rechercher un cabinet, une date d'audience…",
            Criteres = ["Date d'audience", "Type de cabinet", "Juge"],
            // Bouton final cite par la fiche v2 : DoSauve(cnx, futurEtat) (proc_cabinet.md, Detail OPE)
            Valider = "Enregistrer le cabinet",
            Rang = 7,
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Enregistré"),
            Groupes = ["Salles fixes", "Salles ponctuelles"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : les criteres de la fiche (code chef, prefixe IP, ordre).
            Colonnes = ["Code chef", "Préfixe IP", Champ.Nombre("Ordre")],
            Recherche = "Rechercher un code chef…",
            Criteres = ["Code chef", "Préfixe IP", "Ordre"],
            // Bouton final cite par la fiche v2 : DoSauveAfterOperation (proc_param_ip.md, Detail OPE)
            Valider = "Enregistrer la liste",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Codes IP proposables", "Autres codes"],
            Sections =
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
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un essai, un article, une borne…",
            Criteres = ["Article", "Dossier de test", "Journal"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_borne.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À lancer", "En cours", "Terminé"),
            Groupes = ["Calcul de prix", "Commandes de test", "Journal"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Nom du modèle", "Maquette", "Sections"],
            Recherche = "Rechercher un modèle, une maquette, une section…",
            Criteres = ["Modèle", "Maquette", "Section"],
            // Bouton final cite par la fiche v2 : bouton VALIDER (proc_redaa.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Modèles et maquettes", "Sections et glossaire", "Jugements"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Référence", "Hauteur maximale", "Image", "Dernière version"],
            Recherche = "Rechercher un sceau, une référence…",
            Criteres = ["Référence", "Hauteur maximale", "Image"],
            // Bouton final cite par la fiche v2 : VALIDER, ANNULER_ROUGE (proc_sceau.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Hauteur de 24 mm", "Autre hauteur"],
            Sections =
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
            Forme = FormeEcran.Referentiel,
            // Colonnes : la grille de la V2 (Code, Libelle, Actif : proc_gestmodepaiement.md:244) et le detail
            // qu'elle ouvre (Type :173, Banque :183, edition du remis), modifiables en ligne.
            Colonnes = ["Code", "Libellé", "Type", "Banque", Champ.Case("Actif"), Champ.Case("Édition du remis visible")],
            Recherche = "Rechercher un mode de paiement, un code, une banque…",
            Criteres = ["Code", "Libellé", "Type"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gestmodepaiement.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"),
            Groupes = ["Espèces et chèque", "Virement et carte"],
            Sections =
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
            // Les cellules, dans l'ordre des colonnes : le code d'un mode n'est pas cite par la fiche (vide, « — ») ; le type
            // « VI » du virement l'est (:190) ; actif par defaut (:260). Le libelle est celui de la ligne.
            Lignes =
            [
                new LigneEcran("Espèces", "Espèces et chèque · enregistré", "Espèces et chèque", "Validee", [0, 1, 2, 3]) { Valeurs = ["", "Espèces", "", "", "oui", ""] },
                new LigneEcran("Chèque bancaire", "Espèces et chèque · enregistré", "Espèces et chèque", "Validee", [0, 1, 2]) { Valeurs = ["", "Chèque bancaire", "", "", "oui", ""] },
                new LigneEcran("Chèque de banque", "Espèces et chèque · libellé à saisir", "Espèces et chèque", "EnCours", [0]) { Valeurs = ["", "Chèque de banque", "", "", "oui", ""] },
                new LigneEcran("Mode déjà encaissé", "Espèces et chèque · code et type verrouillés", "Espèces et chèque", "EnCours", [0, 1, 2]) { Valeurs = ["", "Mode déjà encaissé", "", "", "oui", ""] },
                new LigneEcran("Virement", "Virement et carte · enregistré", "Virement et carte", "Validee", [0, 1, 2, 3]) { Valeurs = ["", "Virement", "VI", "", "oui", ""] },
                new LigneEcran("Carte bancaire", "Virement et carte · type à choisir", "Virement et carte", "EnCours", [0, 1]) { Valeurs = ["", "Carte bancaire", "", "", "oui", ""] },
                new LigneEcran("Prélèvement", "Virement et carte · à compléter", "Virement et carte", "AFaire", []) { Valeurs = ["", "Prélèvement", "", "", "oui", ""] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARAMEXPRESSO", Titre = "Facturation Expresso", Sujet = "paramètres de facturation",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un paramètre de facturation…",
            Criteres = ["Paramètre", "Banque", "Mode de paiement"],
            // Bouton final cite par la fiche v2 : DoValider (proc_paramexpresso.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À renseigner", "En cours", "Enregistré"),
            Groupes = ["Paramètres généraux", "Pied de page", "Comptabilité"],
            Sections =
            [
                new MarcheEcran("Paramètres généraux", "Les paramètres généraux de la facturation.", true,
                    ["Paramètre", "Valeur"]),
                new MarcheEcran("Banques et modes", "Chaque mode de paiement est associé à sa banque.", false,
                    ["Banque", "Mode de paiement"]),
                new MarcheEcran("Pied de page", "Le pied de page des factures : chaque ligne est un paramètre ; la source de l'adresse peut être basculée.", false,
                    ["Ligne du pied de page", "Source de l'adresse"]),
                new MarcheEcran("Enregistrement", "Enregistrer écrit tous les paramètres ensemble et laisse une trace.", true,
                    []),
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
            Forme = FormeEcran.Etat,
            Parametres = ["Requête", "Critères"],
            Colonnes = ["Requête"],
            Gestes = ["Exécuter"],
            Recherche = "Rechercher une requête préparée…",
            Criteres = ["Requête", "Critère", "Résultat"],
            // Bouton final cite par la fiche v2 : DoValider de la saisie de requete (proc_requetes.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À lancer", "En cours", "Exécutée"),
            Groupes = ["Requêtes préparées", "Requêtes à critères"],
            Sections =
            [
                new MarcheEcran("Requête", "La requête est choisie dans la liste des requêtes préparées ; sa description s'affiche.", true,
                    ["Requête"]),
                new MarcheEcran("Critères", "Les critères se saisissent dans un formulaire qui change selon la requête.", false,
                    ["Critères"]),
                new MarcheEcran("Exécution", "La requête est exécutée après le contrôle de sécurité du garde-fou SQL.", true,
                    ["Exécuter"]),
                // La grille de la requete executee, pas la liste des requetes que l'Etat affiche : titree pour ce qu'elle porte
                // (« Resultat » se lisait comme la liste de l'ecran ; controle final du lot 1, 25/11/2026).
                new MarcheEcran("Résultat de la requête", "Le résultat de la requête s'affiche en grille.", false,
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
            // OPERATION sur UNE liasse RECUE, pas une liste : RIG v2 charge PROC_IA dans la visionneuse des pieces d'une
            // liasse dematerialisee, qui lui passe le numero de la liasse affichee (OPE_DOC_DEMAT.cs:803-825, NumeroLiasse
            // :818 ; proc_ia.md:192). La V3 n'a pas encore cette visionneuse : le numero se saisit dans le seul critere.
            // Deux temps (proc_ia.md:151-156) : le lancement, confirme (OPE_IA.cs:66), puis la restitution (Resultat).
            // Une liasse deja analysee s'ouvre sur son resultat, sans bouton de lancement (PROC_IA.cs:60-64,
            // ETP_ENTETE.cs:36-37). Controle final du lot 1 (25/11/2026) : une file de liasses, puis une liste a cocher
            // dont le resultat et l'avis passaient AVANT le lancement.
            Forme = FormeEcran.Operation,
            ObjetUnique = true,
            Recherche = "Numéro de la liasse…",
            Criteres = ["Numéro de liasse"],
            // Le bouton de l'operation : « lancer » (proc_ia.md:280-281), apres confirmation (:207). FORM_IA n'a pas de
            // barre d'outils (:303) ; QUITTER et VALIDER n'y sont que desactives pendant l'analyse (:212, :287-288).
            Valider = "Lancer l'analyse",
            Rang = 7,
            // « En cours » : le temps de la reponse du service, dans la session (OPE_IA.cs:99-100) ; rien ne le garde en
            // base, ou DMTLCF_REPONSE_IA est vide (a analyser) ou remplie (analysee) -- aucune ligne n'y est donc.
            Etats = EtatsEcran.Trois("À analyser", "En cours", "Analysée"),
            Groupes = ["Liasses CFE", "Liasses DCA", "Liasses DAS"],
            // Ce que le lancement emporte et fait : ce qui se voit AVANT le bouton.
            Sections =
            [
                // Ce qui part : proc_ia.md:153 (JSON fonctionnel + PDF), :223-231 (regle de source : derniere regularisation,
                // sinon la liasse, flux EDI seulement si vide). Service externe : jamais appele (regle du plan).
                new MarcheEcran("Pièces envoyées", "Les données de la formalité (celles de sa dernière régularisation s'il y en a une, sinon celles de la liasse ; à défaut, celles du flux reçu) et tous ses PDF partent vers le service d'analyse externe. Ici, le service n'est jamais appelé.", false,
                    [Champ.Tableau("Pièces de la formalité")]),
                // proc_ia.md:287-288 (non bloquante, QUITTER/VALIDER desactives), :216-217 (reponse ecrite dans la liasse).
                new MarcheEcran("Analyse", "L'analyse tourne sans bloquer l'écran ; Quitter et Valider restent désactivés jusqu'à la réponse, enregistrée dans la liasse.", false,
                    []),
            ],
            // La restitution : ce que la reponse du service fait voir, APRES le lancement (proc_ia.md:155-156, toutes les
            // etapes se masquent tant qu'il n'y a pas de reponse, :315-316).
            Resultat =
            [
                // proc_ia.md:257-258 (resultats par champ, anomalies depliees d'office).
                new MarcheEcran("Champs vérifiés", "Chaque champ : correspondance, proche, pas de correspondance ou non disponible ; un champ en anomalie s'ouvre d'office.", false,
                    [Champ.Tableau("Champs vérifiés")]),
                // proc_ia.md:271-275 (synthese, commentaire), :296 (bandes de l'indice de confiance).
                new MarcheEcran("Synthèse", "Indice de confiance global (faible sous 0,5, moyen sous 0,8, bon sous 0,9, très bon au-delà) et commentaire de synthèse.", false,
                    [Champ.Tableau("Indice de confiance et commentaire")]),
                // OPE_AVIS_USER.cs:28-31 (« Votre avis nous est precieux », formulaire de retour), :36-44 (son adresse porte
                // la demande, la liasse et le greffe ; rien sans reponse). Ce n'est pas un choix validation / rejet comme
                // le dit la table de proc_ia.md:79 : le code gagne. Formulaire externe, jamais ouvert (regle du plan).
                new MarcheEcran("Votre avis", "Un bouton ouvre le formulaire de retour sur l'analyse, avec le numéro de la demande, celui de la liasse et le nom du greffe. Formulaire externe : jamais ouvert ici.", false,
                    []),
            ],
            // Le numero d'une liasse (le nom de la ligne) est ce qui se saisit dans le critere.
            Lignes =
            [
                new LigneEcran("2026-001", "Liasse CFE · analysée", "Liasses CFE", "Validee", [0, 1]),
                new LigneEcran("2026-002", "Liasse CFE · à analyser", "Liasses CFE", "AFaire", []),
                new LigneEcran("2026-003", "Liasse CFE · à analyser", "Liasses CFE", "AFaire", []),
                new LigneEcran("2026-004", "Liasse DCA · analysée", "Liasses DCA", "Validee", [0, 1]),
                new LigneEcran("2026-005", "Liasse DCA · à analyser", "Liasses DCA", "AFaire", []),
                new LigneEcran("2026-006", "Liasse DAS · à analyser", "Liasses DAS", "AFaire", []),
                new LigneEcran("2026-007", "Liasse DAS · analysée", "Liasses DAS", "Validee", [0, 1]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CFENET_RET", Titre = "Retours CFE-NET", Sujet = "retours",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, un numéro de retour…",
            Criteres = ["Période", "Rejets", "Corrections du greffe"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_cfenet_ret.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traité"),
            Groupes = ["Rejets", "Retours à traiter", "Retours traités"],
            Sections =
            [
                new MarcheEcran("Recherche", "La recherche se fait par période et par critères : rejet, traitement, masquage, date de retour.", true,
                    ["Période", "Rejets", "Date de retour"]),
                // Une section « Resultat » (la grille des retours) repetait la liste de la file, retiree (controle final du
                // lot 1, 25/11/2026) ; les gestes du menu de la ligne sont dans « Traitement ».
                new MarcheEcran("Corrections du greffe", "Les corrections déjà faites par le greffe s'affichent si la case est cochée.", false,
                    ["Corrections"]),
                new MarcheEcran("Traitement", "Traiter, rejeter ou masquer le retour ; le dossier part ensuite vers la GED.", true,
                    ["Traiter", "Rejeter", "Masquer"]),
            ],
            Lignes =
            [
                new LigneEcran("Retour 2026-0101", "Rejets · à traiter", "Rejets", "AFaire", []),
                new LigneEcran("Retour 2026-0102", "Rejets · rejeté", "Rejets", "Validee", [0, 1, 2]),
                new LigneEcran("Retour 2026-0103", "Retours à traiter · en cours", "Retours à traiter", "EnCours", [0]),
                new LigneEcran("Retour 2026-0104", "Retours à traiter · à traiter", "Retours à traiter", "AFaire", []),
                new LigneEcran("Retour 2026-0105", "Retours traités · traité", "Retours traités", "Validee", [0, 1, 2]),
                new LigneEcran("Retour 2026-0106", "Retours traités · corrigé par le greffe", "Retours traités", "Validee", [0, 1, 2]),
                new LigneEcran("Retour 2026-0107", "Retours à traiter · masqué", "Retours à traiter", "EnCours", [0]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDI_RPVAINS", Titre = "EDI RPVA", Sujet = "demandes d'inscription",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une demande, un numéro CNBF…",
            Criteres = ["Numéro CNBF", "Demandeur", "État de la demande"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_edi_rpvains.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("En attente", "En cours", "Traitée"),
            Groupes = ["Personnes physiques", "Structures"],
            Sections =
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
            Forme = FormeEcran.File,
            Recherche = "Rechercher une affaire, un numéro de dossier…",
            Criteres = ["Numéro d'affaire", "Mandataire", "État de traitement"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_edi_rpjc_trcnc.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            Groupes = ["À traiter", "En cours", "Historique"],
            Sections =
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
