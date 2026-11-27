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
// Lot 15 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : bornes, modeles, sceaux, modes de paiement, Expresso, requetes, IA, wiki,
// page web, CFE-NET et EDI. fix-ok: mesure le 27/11/2026 en lisant le code V2 -- ces descripteurs portaient les groupes, colonnes et
// gestes des exemples du lot 7 (« Espèces et chèque » / « Virement et carte » absents de ModePaiementRigDataGridView.cs:15-40 ; les
// gestes « Traiter / Rejeter / Masquer » de CFE-NET absents d'OPE_RESULTAT.cs, qui n'a que la correction greffe et Amitel ; aucun geste
// « Refuser » sur EDI RPVA alors que DataProcDemandeInscr.Refuse l'ecrit) : une edition par descripteur, citee la ou elle porte. Les
// ecrans dont la V2 lit une base absente du greffe (COMMUN_RIG, base WIKI) ou un service externe restent en exemple, la cause nommee
// dans leur descripteur.
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
            // Lot 15 (27/11/2026) : RESTE EN EXEMPLE, cause nommee -- les articles sont dans la base « WIKI » du Support Desk d'Amitel
            // (RigGreffe.CodeGreffeWiki_Exploit = "WIKI", RigTools\TOOLS\RigGreffe.cs:44 ; tables WIKI_ARTICLE, WIKI_ARTICLE_CATEGORIE,
            // WIKI_ARTICLE_FILE, proc_wiki.md:125-131), absente de la base V3 du greffe. Aucun appel reseau externe.
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
            // Lot 15 (27/11/2026) : RESTE EN EXEMPLE, cause nommee -- la V2 ne lit aucune table : elle ouvre l'adresse recue en parametre
            // (ListeParametresInternes["url"], proc_web_browser.md DoChargerDonnees) dans un navigateur integre. Ouvrir une page est un
            // appel reseau, que la V3 ne fait pas ici : le geste reste une demonstration.
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
            // Lot 15 (27/11/2026) : RESTE EN EXEMPLE, cause nommee -- la V2 lit le journal des bornes dans COMMUN_RIG.BORNE_LOG_PARAMETRE
            // (proc_borne.md:43, :83-95 ; base commune absente de la base V3 du greffe) et pilote les bornes par le service SOAP ServiceKbisSoap
            // (service externe, jamais appele), sur les seules bases de developpement 9995 / 9999. Rien n'est ecrit dans RIG.
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
            // Lot 15 (27/11/2026) : les objets que la V2 administre (OPE_RECH_MODEL_MAQ.cs:84-85 modeles et maquettes REDA,
            // OPE_RECH_SECTION.cs:92 sections, OPE_RECH_GLOSSAIRE.cs:96 glossaire), lus par LecteurModelesRedaction.cs, un plafond par
            // groupe. Aucun geste branche (document Word en GED, sections reservees Amitel, glossaire au login de l'agent) : nomme dans le lecteur.
            Colonnes = ["Code", "Libellé", "Nature", "Utilisation"],
            Recherche = "Rechercher un modèle, une maquette, une section, un terme…",
            Criteres = ["Code", "Libellé"],
            // La V2 masque VALIDER (FORM_REDAA.cs:18-23) : chaque objet s'enregistre par la barre de sa fiche.
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Modèles et maquettes", "Sections et glossaire", "Jugements"],
            Sections =
            [
                new MarcheEcran("Modèle", "Un modèle de document compose des maquettes, dans l'ordre ; il ne se supprime pas tant qu'il en garde.", true,
                    ["Code", "Libellé"]),
                new MarcheEcran("Maquette", "Une maquette REDA est un document Word de la GED ; utilisée par un modèle, elle ne se supprime pas.", true,
                    ["Nature", "Utilisation"]),
                new MarcheEcran("Sections", "Les sections réutilisables de la décision (fusion, génération, occurrence, persistance) ; éditées par Amitel seulement.", false,
                    ["Sections"]),
                new MarcheEcran("Glossaire", "Les termes publics, et les termes privés de l'agent.", false,
                    ["Termes du glossaire"]),
                new MarcheEcran("Fonctions avancées", "Outils techniques de la rédaction, dans un onglet à part.", false,
                    ["Fonctions avancées"]),
            ],
            // Les cellules, dans l'ordre des colonnes : des objets anonymes du type de ceux de RIG_DEV.
            Lignes =
            [
                new LigneEcran("JGT_ORDINAIRE - Jugement ordinaire", "modèle · 3 maquette(s)", "Modèles et maquettes", "Validee", []) { Valeurs = ["JGT_ORDINAIRE", "Jugement ordinaire", "modèle", "3 maquette(s)"] },
                new LigneEcran("ORD_REFERE - Ordonnance de référé", "modèle · aucune maquette", "Modèles et maquettes", "AFaire", []) { Valeurs = ["ORD_REFERE", "Ordonnance de référé", "modèle", "aucune maquette"] },
                new LigneEcran("ENTETE_JGT - Entête de jugement", "maquette REDA · utilisée par 2 modèle(s)", "Modèles et maquettes", "Validee", []) { Valeurs = ["ENTETE_JGT", "Entête de jugement", "maquette REDA", "utilisée par 2 modèle(s)"] },
                new LigneEcran("DISPOSITIF - Dispositif", "maquette REDA · sans document Word", "Modèles et maquettes", "AFaire", []) { Valeurs = ["DISPOSITIF", "Dispositif", "maquette REDA", "sans document Word"] },
                new LigneEcran("MOTIFS - Motifs", "section", "Sections et glossaire", "Validee", []) { Valeurs = ["MOTIFS", "Motifs", "section", "—"] },
                new LigneEcran("PARTIES - Les parties", "glossaire public", "Sections et glossaire", "Validee", []) { Valeurs = ["PARTIES", "Les parties", "glossaire public", "—"] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SCEAU", Titre = "Sceaux", Sujet = "sceaux",
            Forme = FormeEcran.Referentiel,
            // Lot 15 (27/11/2026) : les colonnes de la liste de la V2 (Recherche\OPE_RECHERCHE.cs InitColumn : fichier, nom externe, hauteur,
            // largeur, rognage, date d'archivage), lues par LecteurSceaux.cs ; Supprimer seul est branche (EcrivainSceaux.cs) -- ajouter importe
            // l'image en GED, modifier la hauteur recalcule la largeur sur l'image : non repris, nommes dans l'ecrivain.
            Colonnes = ["Nom externe", "Fichier", Champ.Nombre("Hauteur max"), Champ.Nombre("Largeur max"), Champ.Nombre("Rogner"), Champ.Texte("Date archivage")],
            Recherche = "Rechercher un sceau, un fichier…",
            Criteres = ["Nom externe", "Fichier"],
            // La V2 masque VALIDER (FORM_SCEAU.cs:25-30) : la fiche du sceau a sa propre barre (Valider, Annuler, Supprimer).
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "Image absente de la GED", "Enregistré"),
            Groupes = ["Hauteur de 24 mm", "Autre hauteur"],
            Sections =
            [
                new MarcheEcran("Référence", "Le nom externe est la clé du sceau dans les maquettes : unique, il ne change plus une fois enregistré.", true,
                    ["Nom externe"]),
                new MarcheEcran("Dimensions", "Hauteur maximale en mm ; la largeur se recalcule sur l'image. « Dimensions du lot 1 » vaut une hauteur de 24 mm.", true,
                    [Champ.Nombre("Hauteur max"), Champ.Nombre("Largeur max"), Champ.Nombre("Rogner"), Champ.Case("Dimensions du lot 1")]),
                new MarcheEcran("Image", "L'image est un document de la GED du greffe ; supprimer le sceau la laisse dans la GED.", false,
                    ["Fichier", Champ.Texte("Date archivage")]),
            ],
            // Les cellules, dans l'ordre des colonnes : des elements de RIG_DEV (reference, hauteur, largeur, rognage), anonymes.
            Lignes =
            [
                new LigneEcran("SCEAU", "24 × 41 mm · sceau.gif · archivé le 12/03/2024", "Hauteur de 24 mm", "Validee", []) { Valeurs = ["SCEAU", "sceau.gif", "24", "41", "0", "12/03/2024"] },
                new LigneEcran("SCEAU_SEUL", "24 × 24 mm · sceau-seul.gif · archivé le 12/03/2024", "Hauteur de 24 mm", "Validee", []) { Valeurs = ["SCEAU_SEUL", "sceau-seul.gif", "24", "24", "0", "12/03/2024"] },
                new LigneEcran("SIGNATURE", "24 × 45 mm · image absente de la GED", "Hauteur de 24 mm", "EnCours", []) { Valeurs = ["SIGNATURE", "", "24", "45", "0", ""] },
                new LigneEcran("LOGO", "273 × 998 mm · logo.gif · archivé le 02/01/2023", "Autre hauteur", "Validee", []) { Valeurs = ["LOGO", "logo.gif", "273", "998", "0", "02/01/2023"] },
                new LigneEcran("SIGNJUD_IMAGE", "45 × 50 mm · sans image", "Autre hauteur", "AFaire", []) { Valeurs = ["SIGNJUD_IMAGE", "", "45", "50", "0", ""] },
                new LigneEcran("SCEAU_GPD", "24 × 0 mm · sceau-gpd.gif · archivé le 05/06/2024", "Hauteur de 24 mm", "Validee", []) { Valeurs = ["SCEAU_GPD", "sceau-gpd.gif", "24", "0", "0", "05/06/2024"] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTMODEPAIEMENT", Titre = "Modes de paiement", Sujet = "modes de paiement",
            Forme = FormeEcran.Referentiel,
            // Colonnes : la grille de la V2 (Code, Libelle, Actif : ModePaiementRigDataGridView.cs:15-40) et le detail qu'elle ouvre
            // (Type, Banque, edition du remis, groupe comptable : OPE_MODEPAIEMENTDETAIL.cs:65-82), modifiables en ligne. Le type est
            // un code de la combo de la V2 (ULT_COMBO_TYPE_MODE_PAIEMENT.cs:19-25). Lot 15 (27/11/2026) : realigne sur le code V2 que
            // lisent LecteurModesPaiement.cs et EcrivainModesPaiement.cs -- les groupes « Espèces et chèque » / « Virement et carte »
            // n'avaient pas de source ; la V2 a une liste unique, dont la colonne « Actif » fait ici les deux groupes.
            Colonnes = ["Code", "Libellé", Champ.Liste("Type", "CB", "CH", "DIV", "ES", "RBT", "REG", "VI"), "Banque", Champ.Case("Actif"),
                        Champ.Case("Édition du remis visible"), Champ.Case("Groupé pour le transfert en comptabilité")],
            Recherche = "Rechercher un mode de paiement, un code, un libellé…",
            Criteres = ["Code", "Libellé", "Type"],
            // La V2 masque VALIDER (FORM_GESTMODEPAIEMENT.cs:16-17) : chaque ligne s'enregistre par la barre de son detail.
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "Banque à associer", "Enregistré"),
            Groupes = ["Actifs", "Inactifs"],
            Sections =
            [
                new MarcheEcran("Code", "Obligatoire, 5 caractères au plus et unique. C'est l'identité du mode : il ne se change pas une fois enregistré.", true,
                    ["Code"]),
                new MarcheEcran("Libellé", "Obligatoire, six caractères au moins, et porté par aucun autre mode.", true,
                    ["Libellé"]),
                new MarcheEcran("Type et banque", "Le type est obligatoire ; il ne change plus dès qu'un encaissement porte le code, ni pour PV. La banque (ou « Aucune ») est demandée, sauf pour le code REG et le mode PV de type DIV.", true,
                    ["Type", "Banque"]),
                new MarcheEcran("Options", "Actif : utilisable en saisie (coché pour un nouveau mode). « Groupé » : décoché d'office pour un remboursement ou un virement.", false,
                    ["Actif", "Édition du remis visible", "Groupé pour le transfert en comptabilité"]),
                new MarcheEcran("Utilisation", "Un mode qu'un encaissement porte ne se supprime pas.", false,
                    ["Encaissements"]),
            ],
            // Les cellules, dans l'ordre des colonnes : des modes de RIG_DEV (code, libelle, type, banque, actif), anonymes.
            Lignes =
            [
                new LigneEcran("Carte bancaire", "code CB · type CB · banque LCL", "Actifs", "Validee", []) { Valeurs = ["CB", "Carte bancaire", "CB", "LCL", "oui", "oui", "oui"] },
                new LigneEcran("Chèque", "code CH · type CH · banque LCL", "Actifs", "Validee", []) { Valeurs = ["CH", "Chèque", "CH", "LCL", "oui", "oui", "oui"] },
                new LigneEcran("Chèque postal", "code CP · type CH · banque CIC · inactif", "Inactifs", "Validee", []) { Valeurs = ["CP", "Chèque postal", "CH", "CIC", "", "oui", "oui"] },
                new LigneEcran("Encaissement repris", "code PV · type DIV · sans banque", "Actifs", "Validee", []) { Valeurs = ["PV", "Encaissement repris", "DIV", "", "oui", "", "oui"] },
                new LigneEcran("Virement LCL", "code VI · type VI · banque LCL", "Actifs", "Validee", []) { Valeurs = ["VI", "Virement LCL", "VI", "LCL", "oui", "oui", ""] },
                new LigneEcran("Mandat", "code MA · type DIV · banque à associer", "Actifs", "EnCours", []) { Valeurs = ["MA", "Mandat", "DIV", "", "oui", "oui", "oui"] },
                new LigneEcran("Espèc", "code ES · type ES · banque ESPECE · libellé trop court", "Actifs", "AFaire", []) { Valeurs = ["ES", "Espèc", "ES", "ESPECE", "oui", "oui", "oui"] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARAMEXPRESSO", Titre = "Facturation Expresso", Sujet = "paramètres de facturation",
            Forme = FormeEcran.Fiche,
            // Lot 15 (27/11/2026) : un parametre par ligne, lu « PARAMETRAGE d'abord, sinon GREFFE_PARAMETRE » (MetierParametrage.cs:71-92,
            // repris dans soc.Reglage et soc.ParametreGreffe), puis la banque de chaque mode actif (MetierBanques.cs:38-57) :
            // LecteurFacturationExpresso.cs. Enregistrer n'est pas branche : ces reglages s'ecrivent par le module des reglages du greffe,
            // pas par un ecrivain d'ecran genere (v2.* et rec.* seulement) ; l'association mode / banque s'ecrit par « Modes de paiement ».
            Recherche = "Rechercher un paramètre de facturation…",
            Criteres = ["Paramètre", "Banque", "Mode de paiement"],
            // Bouton final cite par la fiche v2 : DoValider (PROC_PARAMEXPRESSO.cs:39-54), VERIFIER masque (FORM_PARAMEXPRESSO.cs:10)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À renseigner", "En cours", "Enregistré"),
            Groupes = ["Paramètres généraux", "Banques et modes", "Pied de page", "Comptabilité"],
            Sections =
            [
                new MarcheEcran("Paramètres généraux", "Rompu, seuils de relance et de remboursement, commentaires obligatoires, relevé client, éditions synthétiques, courriels des relevés.", true,
                    ["Paramètre", "Valeur"]),
                new MarcheEcran("Banques et modes", "Chaque mode de paiement actif est associé à sa banque, ou à aucune.", false,
                    ["Banque", "Mode de paiement"]),
                new MarcheEcran("Pied de page", "Le pied de page des factures : l'adresse vient du code greffe ou du paramètre GRPAR_GREFFE_ADR_COMPLETE.", false,
                    ["Ligne du pied de page", "Source de l'adresse"]),
                new MarcheEcran("Enregistrement", "Enregistrer écrit les seuls paramètres modifiés et laisse une trace.", true,
                    []),
            ],
            // Des cles de la V2, aux valeurs anonymes.
            Lignes =
            [
                new LigneEcran("GRPAR_ROMPU", "0.50 · paramètre du greffe", "Paramètres généraux", "Validee", []),
                new LigneEcran("EXPRESSO_RELANCE", "15.00 · réglage", "Paramètres généraux", "Validee", []),
                new LigneEcran("EXPRESSO_RELEVE_MAIL_ENVOI", "non renseigné", "Paramètres généraux", "AFaire", []),
                new LigneEcran("Mode CH", "banque du mode Chèque · banque LCL", "Banques et modes", "Validee", []),
                new LigneEcran("Mode REG", "banque du mode Régularisation · aucune banque", "Banques et modes", "AFaire", []),
                new LigneEcran("EXPRESSO_TYPE_PIED_PAGE", "CODE_GREFFE · réglage", "Pied de page", "Validee", []),
                new LigneEcran("GRPAR_COMPTA_TYPE_COMPTA", "BNC · paramètre du greffe", "Comptabilité", "Validee", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REQUETES", Titre = "Requetes metier", Sujet = "requêtes",
            // Lot 15 (27/11/2026) : RESTE EN EXEMPLE, cause nommee -- le catalogue des requetes est la table REQUETE de COMMUN_RIG (RigTools
            // SQL\COMMON\REQUETE.cs:13 ; Requete.Delete ecrit sur Common.CodeGreffeBaseCommmunRig, proc_requetes.md:231-232), absente de la
            // base V3 du greffe. L'execution (SELECT seul, #TOP# exige, RequetesHelper.cs:84-117) attend ce catalogue.
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
            // Lot 15 (27/11/2026) : la liasse INPI lue dans DEMAT_LIASSE_CFE et sa reponse DMTLCF_REPONSE_IA, LecteurAnalyseIa.cs ; groupes par
            // l'origine (INPI_FORMA / INPI_FORMA_MAJ) -- « Liasses CFE / DCA / DAS » n'avaient pas de source. Le lancement poste les pieces au
            // service externe (IA.cs:189-193) : jamais appele, le geste reste une demonstration.
            Groupes = ["Formalités INPI", "Régularisations INPI"],
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
                new MarcheEcran("Synthèse", "Indice de confiance global (faible sous 0,5, moyen sous 0,8, bon sous 0,9, très bon sous 1, excellent à 1) et commentaire de synthèse.", false,
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
                new LigneEcran("J00026001001", "formalité INPI · reçue le 10/03/2026 · analysée", "Formalités INPI", "Validee", []),
                new LigneEcran("J00026001002", "formalité INPI · reçue le 11/03/2026 · à analyser", "Formalités INPI", "AFaire", []),
                new LigneEcran("J00026001003", "formalité INPI · reçue le 12/03/2026 · à analyser", "Formalités INPI", "AFaire", []),
                new LigneEcran("J00026001004", "régularisation INPI · reçue le 13/03/2026 · à analyser", "Régularisations INPI", "AFaire", []),
                new LigneEcran("J00026001005", "formalité INPI · reçue le 14/03/2026 · analysée", "Formalités INPI", "Validee", []),
                new LigneEcran("J00026001006", "régularisation INPI · reçue le 15/03/2026 · analysée", "Régularisations INPI", "Validee", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CFENET_RET", Titre = "Retours CFE-NET", Sujet = "retours",
            Forme = FormeEcran.File,
            // Lot 15 (27/11/2026) : la grille de la V2 a ses criteres d'ouverture (erreurs, ni corrigees ni traitees par Amitel),
            // LecteurRetoursCfenet.cs ; les deux gestes de son menu de ligne (OPE_RESULTAT.cs:197-245), EcrivainRetoursCfenet.cs. Les gestes
            // « Traiter / Rejeter / Masquer » d'avant n'avaient pas de source V2.
            Parametres = [Champ.Zone("Commentaire Amitel")],
            Gestes = ["Correction greffe", "Correction Amitel"],
            Recherche = "Numéro de gestion, SIREN ou numéro de liaison…",
            Criteres = ["Numéro", "Erreurs", "Traités", "Corrections du greffe", "Date retour CCI"],
            // La V2 masque VALIDER et VERIFIER (FORM_CFENET_RET.cs:14-15) : tout passe par le menu de la ligne.
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Corrigé par le greffe"),
            Groupes = ["Rejet définitif", "Rejet doublon", "Erreur fonctionnelle", "PDF feuillet1 absent"],
            Sections =
            [
                new MarcheEcran("Recherche", "À l'ouverture : les retours en erreur (le PDF absent seulement après le délai du réglage CFENET_RET_NB_JOURS_PDF_ABSENT, 2 jours par défaut), ni corrigés par le greffe ni traités par Amitel. Le numéro cherche le dossier, le SIREN ou la liaison.", true,
                    ["Numéro", "Erreurs", "Date retour CCI"]),
                new MarcheEcran("Correction greffe", "Marque le retour comme traité par le greffe (date et agent) : il quitte la liste. Rejouée sur un retour corrigé, elle retire la correction.", true,
                    ["Correction greffe"]),
                new MarcheEcran("Correction Amitel", "Réservée à l'équipe Amitel, avec un commentaire obligatoire.", false,
                    ["Commentaire Amitel"]),
            ],
            Lignes =
            [
                new LigneEcran("2024 B 01011", "liaison C24010100001 · envoyée le 02/03/2026 · traité par la CCI le 05/03/2026 · Rejet définitif", "Rejet définitif", "AFaire", []),
                new LigneEcran("2024 B 01012", "liaison C24010200001 · envoyée le 03/03/2026 · traité par la CCI le 04/03/2026 · Liasse envoyée deux fois", "Rejet doublon", "AFaire", []),
                new LigneEcran("2024 B 01013", "liaison C24010300001 · envoyée le 01/03/2026 · Code APE invalide", "Erreur fonctionnelle", "AFaire", []),
                new LigneEcran("2024 B 01014", "liaison C24010400001 · envoyée le 25/02/2026", "PDF feuillet1 absent", "AFaire", []),
                new LigneEcran("2024 B 01015", "liaison C24010500001 · envoyée le 20/02/2026 · traité par la CCI le 22/02/2026 · Rejet définitif", "Rejet définitif", "AFaire", []),
                new LigneEcran("2024 B 01016", "liaison C24010600001 · envoyée le 18/02/2026 · Raison sociale absente", "Erreur fonctionnelle", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDI_RPVAINS", Titre = "EDI RPVA", Sujet = "demandes d'inscription",
            Forme = FormeEcran.File,
            // Lot 15 (27/11/2026) : le refus, seul geste de la V2 que la file porte (EcrivainInscriptionsRpva.cs) -- motif obligatoire
            // (OPE_SAISIE_REJET.Designer.cs:71), 300 caracteres (RPJCI_MOTIF_TRAITEMENT_RIG). « Traiter » (rattacher les mandataires
            // candidats) est un parcours en plusieurs fenetres de la V2, non repris : nomme dans l'ecrivain.
            Parametres = [Champ.Zone("Motif du refus")],
            Gestes = ["Refuser"],
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
                new MarcheEcran("Refus", "Le refus, motif saisi, met la demande à l'état refusée : elle quitte la liste. Une demande de modification, déjà refusée ou tenue par un agent ne se refuse pas.", false,
                    ["Motif du refus"]),
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
            // Lot 15 (27/11/2026) : lu par LecteurTraitementRpjc.cs ; AUCUN GESTE BRANCHE, cause nommee -- integrer ou refuser une
            // transmission n'est permis qu'apres avoir VISUALISE ses pieces (OPE_AFFAIRE_A_TRAITER.cs:687, une ligne non vue est sautee),
            // extraites du flux entrant de la base EDI (EDIGREFFE, l. 495-506), absente de la base du greffe ; integrer insere en GED.
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
