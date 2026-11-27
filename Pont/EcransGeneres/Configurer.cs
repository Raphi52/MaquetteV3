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
// Lot 14 du plan « RIG v3 100 % fonctionnel » (27/11/2026) : chaque descripteur d'acteur ou de referentiel qui a son lecteur et son
// ecrivain dans Serveur/Socle/EcransGeneres/ (Lecteur<Sujet>.cs, Ecrivain<Sujet>.cs, cites dans le descripteur) est realigne sur le
// code V2 qu'ils lisent : colonnes, criteres, groupes, sections. Un descripteur sans lecteur garde ses lignes d'exemple. fix-ok: mesure
// le 27/11/2026 en lisant le code V2, puis par le controle final du lot 14 -- ces descripteurs portaient les criteres, colonnes et
// groupes des exemples du lot 7 (FormRecherche_MANDATAIRE.cs:179-198 cherche par nom, CNBF, CNBF de structure et SIREN, pas par abrege ;
// OPE_GED_NATURE_DOC_READONLY.cs:44-46 montre code, libelle et type de document, pas des groupes « Décisions ») ; et cette note citait
// des ecrans pas encore branches : elle ne nomme plus que la regle, chaque descripteur cite son lecteur.
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
                new MarcheEcran("Acteur à garder", "Le numéro ou l'abrégé de l'acteur qui reprend les affaires : du même type, répertorié (avec un abrégé), et non coché. Un mandataire actif qui a un numéro CNBF ne peut pas être supprimé, ni le juge par défaut des injonctions de payer.", true,
                    ["Acteur à garder"]),
                new MarcheEcran("Liaisons reprises", "Ouvrez la ligne d'un acteur coché : ses liaisons et les affaires touchées passeront à l'acteur gardé, dans une seule transaction, puis il sera supprimé. ATTENTION CETTE OPERATION EST IRREVERSIBLE.", false,
                    [Champ.Tableau("Liaisons"), Champ.Tableau("Affaires touchées")]),
                new MarcheEcran("Piste d'audit", "Chaque suppression laisse une trace datée (ACTEUR_SUPP) : l'auteur, l'acteur supprimé, l'acteur gardé et les reprises faites.", false,
                    []),
            ],
            Lignes =
            [
                // Hors Saisie, Faites reste vide (decision du lot 1) : l'etat dit seul si l'acteur peut etre coche.
                new LigneEcran("ALPHA Jean (JAL)", "Juges · actif", "Juges", "AFaire", []),
                new LigneEcran("BETA Anne (JBE)", "Juges · inactif", "Juges", "EnCours", []),
                new LigneEcran("Me Zêta (ZET)", "Mandataires · actif", "Mandataires", "AFaire", []),
                new LigneEcran("Me Iota (IOT)", "Mandataires · actif, n° CNBF 12345", "Mandataires", "Validee", []),
                new LigneEcran("Me Thêta", "Mandataires · inactif", "Mandataires", "EnCours", []),
                new LigneEcran("SELARL Lambda (LAM)", "Mandataires PC · actif", "Mandataires PC", "AFaire", []),
                new LigneEcran("SCP Nu", "Mandataires PC · inactif", "Mandataires PC", "EnCours", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SUPP_ACTEUR_JUD", Titre = "Remplacer un acteur", Sujet = "acteurs à remplacer",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher un acteur à remplacer…",
            // Les acteurs lies (HaveLiaisonEntite : la V2 n'ouvre cet ecran que pour eux, OPE_EDITION_GGREFFIER.cs:113-114) et les
            // remplacements faits (ACTEUR_SUPP). Lignes et apercu : LecteurRemplacementActeur.cs ; reprise : EcrivainRemplacementActeur.cs.
            Criteres = ["Type d'acteur", "Acteur remplacé"],
            // Bouton final cite par la fiche v2 : bouton Valider (proc_supp_acteur_jud.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À choisir", "En cours", "Remplacé"),
            Groupes = ["Magistrats et greffe", "Mandataires PC", "Intervenants PC"],
            Sections =
            [
                new MarcheEcran("Acteur remplacé", "Cochez UN acteur : juge, parquetier, greffier assermenté, mandataire PC ou intervenant PC. L'expert a son propre écran.", true,
                    ["Type d'acteur", "Acteur remplacé"]),
                new MarcheEcran("Liaisons reprises", "Ouvrez la ligne : la liste des entités liées et les affaires touchées passeront au remplaçant, dans une seule transaction, puis l'acteur sera supprimé. Une trace datée est gardée.", false,
                    [Champ.Tableau("Liaisons"), Champ.Tableau("Affaires touchées")]),
                new MarcheEcran("Remplaçant", "Le numéro ou l'abrégé d'un acteur ACTIF du même type, différent de l'acteur remplacé.", true,
                    ["Remplaçant"]),
            ],
            Lignes =
            [
                // Hors Saisie, Faites reste vide (decision du lot 1).
                new LigneEcran("ALPHA Jean (JAL)", "Juge · actif", "Magistrats et greffe", "AFaire", []),
                new LigneEcran("GAMMA Luc (PGA)", "Parquetier · inactif", "Magistrats et greffe", "AFaire", []),
                new LigneEcran("EPSILON Anne (GEP)", "Greffier assermenté · actif", "Magistrats et greffe", "AFaire", []),
                new LigneEcran("BETA Paul (JBE) → DELTA Marc (JDE)", "Juge · remplacé le 12/03/2026", "Magistrats et greffe", "Validee", []),
                new LigneEcran("SELARL Iota (MIO)", "Mandataire PC · actif", "Mandataires PC", "AFaire", []),
                new LigneEcran("SCP Kappa (MKA) → SELARL Thêta (MTH)", "Mandataire PC · remplacé le 02/02/2026", "Mandataires PC", "Validee", []),
                new LigneEcran("LAMBDA Conseil (ILA)", "Intervenant PC · actif", "Intervenants PC", "AFaire", []),
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
            // Une ligne = une demande CNI (RIG v2 n'a pas de liste : LecteurNonInscrits.cs) ; « Valider » cree le non-inscrit, son
            // entreprise, son etablissement et la demande, comme OPE_CNI.DoSauve (EcrivainNonInscrits.cs). Les champs et leurs obligations :
            // OPE_CNI.Designer.cs:97-202 et _Rafraichir (OPE_CNI.cs:253-287).
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Créé"),
            Groupes = ["Personne physique", "Personne morale"],
            Marches =
            [
                new MarcheEcran("Type et identité", "Le type et la civilité sont obligatoires ; la civilité décide de la nature. Personne physique : nom de naissance, prénoms et forme juridique obligatoires, et le nom d'usage pour « MME ». Personne morale : la dénomination. Un SIREN déjà connu du greffe refuse la création.", true,
                    ["Type de non-inscrit", "Civilité", "Nom de naissance", "Prénoms", "Nom d'usage", "Dénomination", "Forme juridique", "SIREN"]),
                new MarcheEcran("Naissance", "Pour une personne physique : date, département, nationalité et pays de naissance. Le pays est la France (FRA) par défaut.", false,
                    [Champ.Date("Date de naissance"), "Département", "Nationalité", "Pays de naissance"]),
                new MarcheEcran("Adresse", "L'adresse de l'établissement est obligatoire. Une entreprise représentante et l'établissement principal sont créés ; un type judiciaire (JUD) ou procédure collective (PCOL) a aussi un établissement « NQS ». Le numéro RGNI est donné pour l'année de la date de travail des non-inscrits.", true,
                    ["Adresse", "Code postal", "Ville"]),
            ],
            Lignes =
            [
                new LigneEcran("MR DORVAL Mu", "demande 2026DMD000101 · du 03/11/2026 · non-inscrit créé", "Personne physique", "Validee", [0, 2]),
                new LigneEcran("MME LACROIX Nu", "demande 2026DMD000102 · du 05/11/2026 · non-inscrit créé", "Personne physique", "Validee", [0, 2]),
                new LigneEcran("MR PRADEL Xi", "demande 2026DMD000103 · du 06/11/2026 · saisie interrompue", "Personne physique", "EnCours", [0]),
                new LigneEcran("PI SERVICES", "demande 2026DMD000104 · du 07/11/2026 · non-inscrit créé", "Personne morale", "Validee", [0, 2]),
                new LigneEcran("RHO LOGISTIQUE", "demande 2026DMD000105 · du 10/11/2026 · saisie interrompue", "Personne morale", "EnCours", [0]),
                new LigneEcran("TAU HABITAT", "demande 2026DMD000106 · du 12/11/2026 · non-inscrit créé", "Personne morale", "Validee", [0, 2]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTDATE", Titre = "Dates de travail", Sujet = "dates de travail",
            Forme = FormeEcran.Referentiel, Ajout = false, Suppression = false,
            // Les dates ACTIVES de DATE_TRAVAIL (DateTravail.cs:48) et les champs de leur bloc V2 (OPE_DATE_TRAVAIL.cs:40-44). Lignes :
            // LecteurDatesTravail.cs ; gestes (Modifier seul, la V2 n'ajoute ni ne supprime) : EcrivainDatesTravail.cs. Groupes et etats :
            // le controle de la V2, pour WINS et DATW seulement (DoVerifier, l. 49-70).
            Colonnes = ["Code", "Libellé", Champ.Date("Date de travail"), Champ.Date("Date de fraîcheur"), Champ.Case("Date de travail automatique"),
                        Champ.Case("Date de fraîcheur automatique"), Champ.Date("Ancienne date de travail")],
            Recherche = "Rechercher une date de travail…",
            Criteres = ["Code", "Libellé"],
            // Bouton final cite par la fiche v2 : bouton VALIDER (proc_gestdate.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("Date à corriger", "Fraîcheur à vérifier", "À jour"),
            Groupes = ["RCS et endettement", "Autres dates"],
            Sections =
            [
                new MarcheEcran("Date de travail", "Pour le RCS et l'endettement, une date postérieure à aujourd'hui est une erreur bloquante. Si la date change, l'ancienne est archivée. « Automatique » : la date suit chaque jour la date du jour.", true,
                    ["Code", "Libellé", Champ.Date("Date de travail"), Champ.Case("Date de travail automatique"), Champ.Date("Ancienne date de travail")]),
                new MarcheEcran("Date de fraîcheur", "Une date postérieure à aujourd'hui donne un avertissement (« très dangereux ») : l'enregistrement reste possible. « Automatique » : elle suit la date de travail.", true,
                    [Champ.Date("Date de fraîcheur"), Champ.Case("Date de fraîcheur automatique")]),
            ],
            Lignes =
            [
                new LigneEcran("Inscriptions", "code WINS · travail 12/03/2026 · fraîcheur 11/03/2026", "RCS et endettement", "Validee", []),
                new LigneEcran("RCS", "code DATW · travail 13/03/2026 · fraîcheur 20/03/2026", "RCS et endettement", "EnCours", []),
                new LigneEcran("Judiciaire", "code DATJ · travail 13/03/2026 · fraîcheur non renseignée", "Autres dates", "Validee", []),
                new LigneEcran("Bilans", "code DATB · travail 12/03/2026 · fraîcheur 11/03/2026 · automatique", "Autres dates", "Validee", []),
                new LigneEcran("Registre des sûretés", "code DATS · travail 11/03/2026 · fraîcheur 10/03/2026", "Autres dates", "Validee", []),
                new LigneEcran("RCS (saisie future)", "code DATW · travail 31/12/2026 · fraîcheur non renseignée", "RCS et endettement", "AFaire", []),
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
                new LigneEcran("Sauvegarde du poste", "Exploitation · publié · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Exploitation", "Validee", [0, 1, 3]),
                new LigneEcran("Reprise après incident", "Exploitation · texte à relire · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Exploitation", "EnCours", [0, 1]),
                new LigneEcran("Droits des utilisateurs", "Exploitation · titre saisi · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Exploitation", "EnCours", [0]),
                new LigneEcran("Nouveautés de la version", "Développement · publié · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Développement", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Notes de version", "Développement · à rédiger · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Développement", "AFaire", []),
                new LigneEcran("Comment ouvrir une affaire", "Foire aux questions · publié · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Foire aux questions", "Validee", [0, 1, 3]),
                new LigneEcran("Imprimer une attestation", "Foire aux questions · en rédaction · exemple : articles de la base WIKI du support Amitel, absente de la base du greffe", "Foire aux questions", "EnCours", [0, 1]),
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
                new LigneEcran("Portail d'exploitation du greffe", "Site interne · ouverte · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites internes", "Validee", [0, 1]),
                new LigneEcran("Guide des procédures", "Site interne · ouverte en détaché · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites internes", "Validee", [0, 1, 2]),
                new LigneEcran("Intranet du greffe", "Site interne · adresse à saisir · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites internes", "EnCours", [0]),
                new LigneEcran("Tableau de bord de suivi", "Site interne · à ouvrir · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites internes", "AFaire", []),
                new LigneEcran("Site d'un prestataire", "Site externe · ouvert · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites externes", "Validee", [0, 1]),
                new LigneEcran("Page d'actualité externe", "Site externe · à ouvrir · exemple : RIG v2 ouvre l'adresse reçue, sans table ; aucune page externe n'est ouverte ici", "Sites externes", "AFaire", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GTABREF", Titre = "Tables de reference", Sujet = "codes de référence",
            Forme = FormeEcran.Referentiel,
            // Les trois tables que RIG v2 ouvre (OPE_SELECTION_TABLE.cs:17-27 ; NATURE_AFFAIRE, CODE_ACTE_PROCE, JOURNAL_ANNONCES et
            // JURIDICTION_EXTERIEURE y sont en commentaire), une par groupe, sous la description de leur CBO_TableRef (FactoryHelper.cs:66-109).
            // Colonnes : la liste de la V2 (code, libelle, « (! Non saisissable) » : FactoryHelper.cs:4-9) et la table. Lignes :
            // LecteurTablesReference.cs ; gestes (Modifier ; Ajouter un etat de PC, ou une zone avec ses champs ; Dupliquer un etat ou
            // une zone ; Supprimer, refuse quand le code est cite) : EcrivainTablesReference.cs.
            // fix-ok: mesure au banc le 27/11/2026 -- EcransGeneresBaseCheck « fautifs : Champs de la table > Régime… » (la fiche montrait
            // des champs que les sections ne declaraient pas) et EcransGeneresCheck « 4 lignes de demonstration (6 a 15 attendues) » :
            // une section par table, six exemples.
            // fix-ok: mesure par le controle final du lot 14 (27/11/2026, reprise) -- la grille n'offrait ni « Ajouter une zone » ni
            // « Dupliquer » (une ligne neuve n'envoyait que ses colonnes) : Duplication, SectionsAjout, et l'historique d'une zone a part.
            // fix-ok: controle final du lot 14 (27/11/2026, 2e reprise) -- la fiche d'une zone montrait les sections des deux autres
            // tables « Non lu », Modifier d'une zone ne prenait que libelle et case, et un type AJMJ offrait Supprimer / Dupliquer :
            // SectionsParGroupe (le formulaire de chaque table) et GroupesModificationSeule (OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:31-33).
            // fix-ok: controle final du lot 14 (27/11/2026, 3e reprise) -- un etat de PC refusait tous ses champs hors libelle et case,
            // alors que seules les modalites AJ / PA recalculent (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:112-114) : section « Champs d'un état
            // de PC » typee (liste, cases) et saisie, ses regles dans EcrivainTablesReference.EtatAsync (banc PROC_GTABREF.gestes).
            Colonnes = ["Code", "Libellé", "Table", Champ.Case("Utilisable en saisie")],
            Recherche = "Rechercher un code, un libellé, une table…",
            Criteres = ["Table", "Code", "Libellé"],
            // Bouton final cite par la fiche v2 : VALIDER (proc_gtabref.md, Detail OPE)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("Libellé à saisir", "Non saisissable", "Utilisable en saisie"),
            Groupes = ["État de la procédure collective", "Zones à afficher selon l’état de la procédure collective", "Types des dépôts envoyés par les AJMJ"],
            // « Dupliquer » (OPE_TABLE_REF_BUTTONS_BAR.cs:335, 355-370 ; SQLBase_Handler.Copie) et, pour une zone créée ou copiée
            // dans son onglet, ses champs obligatoires (OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.Designer.cs:78-96).
            Duplication = true,
            SectionsAjout = new Dictionary<string, string[]> { ["Zones à afficher selon l’état de la procédure collective"] = ["Champs d'une zone"] },
            // Le formulaire de chaque table sous sa ligne ouverte (« Enregistrement » est dans les colonnes) : ses champs modifiables
            // dans RIG v2 s'y saisissent (une zone : tous, _PC_ETAT_ZONE.cs:75-95 ; un type AJMJ : l'evenement de procedure, :46).
            SectionsParGroupe = new Dictionary<string, string[]>
            {
                ["État de la procédure collective"] = ["Champs d'un état de PC"],
                ["Zones à afficher selon l’état de la procédure collective"] = ["Champs d'une zone", "Historique d'une zone"],
                ["Types des dépôts envoyés par les AJMJ"] = ["Champs d'un type de dépôt"],
            },
            // RIG v2 cache NOUVEAU, SUPPRIMER et DUPLIQUER pour un type de depot AJMJ (OPE_AFFICHE_REF_CFEC_AJMJ_TYPE_DEPOT.cs:31-33).
            GroupesModificationSeule = ["Types des dépôts envoyés par les AJMJ"],
            Sections =
            [
                new MarcheEcran("Enregistrement", "Le code ne se renomme pas (en saisir un autre ouvre son enregistrement dans RIG v2) ; le libellé et « Utilisable en saisie » se modifient (le libellé d'un type de dépôt AJMJ, par l'équipe Amitel seule). Un état de procédure collective se crée ici, une zone dans son onglet avec ses champs ; « Dupliquer » copie un état ou une zone sous un autre code ; un code cité ailleurs ne se supprime pas. Un type de dépôt AJMJ ne se crée, ne se copie ni ne se supprime : RIG v2 ne fait que le modifier.", true,
                    ["Table", "Code", "Libellé", Champ.Case("Utilisable en saisie")]),
                // Le formulaire d'un etat de PC (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:64-82), sous sa ligne ouverte : la fiche dit quels champs se
                // saisissent, EcrivainTablesReference.EtatAsync joue les regles de la V2 a chaque enregistrement (cases exclusives :89-108 ;
                // resolution du plan et code cloture :135-161 ; « A sauver » :116-119 ; regime : la liste eTableRefModaliteRegime, :39-44).
                // En lecture : les modalites AJ et PA (les changer recalcule les decisions et la saisie rattachees : Modalite_Changed,
                // :111-114 -> _PC_ETAT_DJ_EP.cs:393-401, _PC_ETAT_SAISIE.cs:706-714, que la grille ne porte pas), le regime des statistiques
                // (equipe Amitel, :52-55) et le code processus (liste PROCESSUS_ET_FONCTIONALITE absente de la base V3).
                new MarcheEcran("Champs d'un état de PC", "Le formulaire d'un état de procédure collective (OPE_AFFICHE_SQLBASE_PC_ETAT.cs:64-82), avec ses règles : ouverture, clôture, conversion et extension s'excluent (en cocher une décoche les autres) ; « Résolution du plan » ne se montre que sur un état d'ouverture et se décoche avec elle ; le code clôture ne se montre que sur résolution du plan, y est obligatoire et se vide sans elle ; « À sauver » est bloqué quand « Mise à jour de la PC » est cochée. En lecture : les modalités (les changer recalcule dans RIG v2 les décisions et la saisie rattachées à l'état), le régime des statistiques (équipe Amitel) et le code procédure (sa liste n'est pas dans la base V3).", false,
                    [Champ.Liste("Régime", "Général", "Simplifié facultatif", "Simplifié obligatoire"), "Modalité PA", "Modalité AJ",
                     // Les quatre cases s'excluent AU CLIC, comme dans la V2 (:89-108) : chacune decoche les trois autres.
                     Champ.Case("Ouverture") with { Exclut = ["Clôture", "Conversion LJ", "Extension"] },
                     Champ.Case("Clôture") with { Exclut = ["Ouverture", "Conversion LJ", "Extension"] },
                     Champ.Case("Conversion LJ") with { Exclut = ["Ouverture", "Clôture", "Extension"] },
                     Champ.Case("Extension") with { Exclut = ["Ouverture", "Clôture", "Conversion LJ"] },
                     // Montres et bloques comme dans la V2 : « Résolution du plan » avec « Ouverture » (:135-142), « Code clôture » avec
                     // « Résolution du plan » (:149-156), « À sauver » bloque sous « Mise à jour de la PC » (:116-119).
                     Champ.Case("Résolution du plan") with { VisibleSi = "Ouverture" }, Champ.Texte("Code clôture") with { VisibleSi = "Résolution du plan" },
                     Champ.Case("Mise à jour de la PC"), Champ.Case("À sauver") with { BloqueSi = "Mise à jour de la PC" }, Champ.Case("Audience future"), "Régime PC (statistiques)", "Code procédure"]),
                // Saisis a la creation et a la copie d'une zone (SectionsAjout), lus ensuite : synonyme, ISO, simplifie et developpe,
                // table, attribut, type et etat sont obligatoires ; le type et l'etat, les listes de RIG v2 (_ChargerComboTypes,
                // OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:48-55 ; _ChargerComboEtats, SousOPE_HISTORIQUE.cs:39-47).
                new MarcheEcran("Champs d'une zone", "Le formulaire d'une zone (OPE_AFFICHE_SQLBASE_PC_ETAT_ZONE.cs:75-95) : saisi quand la zone se crée, se copie ou se modifie (synonyme, ISO, textes simplifié et développé, table, attribut, type et état obligatoires ; une table et un attribut déjà portés par une autre zone sont refusés).", false,
                    ["Synonyme", "ISO", "Abrégé pour édition", "Simplifié pour édition", Champ.Zone("Développé pour édition"), Champ.Zone("Exhaustif pour édition"),
                     "Table de la zone", "Attribut", Champ.Liste("Type", "DATE", "DATETIME", "HEURE", "INT", "TEXT", "MULTILIGNE", "MONTANT"),
                     Champ.Liste("État", "En test", "En vigueur pour tout", "En vigueur si maj après création", "Applicable si maj avant archivage", "Inactif"),
                     Champ.Date("Date de désactivation")]),
                new MarcheEcran("Historique d'une zone", "La création et la dernière modification d'une zone (SousOPE_HISTORIQUE), posées par RIG.", false,
                    ["Création", "Modification"]),
                // L'evenement de procedure se modifie (lie l. 46), facultatif (MetierObligatoire force a faux, l. 27-28), parmi les codes
                // avec depot PC (MetierDepotPcObligatoire, .Designer.cs:98 -> ULT_COMBO_CODE_EVE_PRO.cs:79) ; creation et modification
                // se lisent.
                new MarcheEcran("Champs d'un type de dépôt", "Le formulaire d'un type de dépôt AJMJ (OPE_AFFICHE_SQLBASE_DEMAT_CFEC_AJMJ_TYPE_DEPOT.cs:44-52) : l'évènement de procédure se modifie (facultatif, un code avec dépôt PC) ; la création et la modification se lisent.", false,
                    ["Évènement de procédure", "Création", "Modification"]),
            ],
            Lignes =
            [
                new LigneEcran("Ouverture redressement judiciaire", "code ORJ · PC_ETAT", "État de la procédure collective", "Validee", []) { Valeurs = ["ORJ", "Ouverture redressement judiciaire", "PC_ETAT", "oui"] },
                new LigneEcran("Liquidation judiciaire", "code LJ · PC_ETAT", "État de la procédure collective", "Validee", []) { Valeurs = ["LJ", "Liquidation judiciaire", "PC_ETAT", "oui"] },
                new LigneEcran("(! Non saisissable) Ancien état", "code ANC · PC_ETAT · non saisissable", "État de la procédure collective", "EnCours", []) { Valeurs = ["ANC", "Ancien état", "PC_ETAT", "non"] },
                new LigneEcran("Date du jugement", "code DJ · PC_ETAT_ZONE", "Zones à afficher selon l’état de la procédure collective", "Validee", []) { Valeurs = ["DJ", "Date du jugement", "PC_ETAT_ZONE", "oui"] },
                new LigneEcran("Date de cessation des paiements", "code DCP · PC_ETAT_ZONE", "Zones à afficher selon l’état de la procédure collective", "Validee", []) { Valeurs = ["DCP", "Date de cessation des paiements", "PC_ETAT_ZONE", "oui"] },
                new LigneEcran("Rapport du mandataire", "code RAP · TYPE_DEPOT_AJMJ", "Types des dépôts envoyés par les AJMJ", "Validee", []) { Valeurs = ["RAP", "Rapport du mandataire", "TYPE_DEPOT_AJMJ", "oui"] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GED_NATURE_DOC", Titre = "Natures de document", Sujet = "natures de document",
            Forme = FormeEcran.Referentiel,
            // Colonnes de la liste V2 (OPE_GED_NATURE_DOC_READONLY.cs:44-46). Lignes : LecteurNaturesDocument.cs ; gestes :
            // EcrivainNaturesDocument.cs. Les groupes disent d'avance si la suppression sera refusee (OPE_GED_NATURE_DOC_EDIT.cs:116-119).
            Colonnes = ["Code", "Libellé", "Type de document"],
            Recherche = "Rechercher une nature de document…",
            Criteres = ["Code", "Libellé"],
            // Bouton final cite par la fiche v2 : post-validation (proc_ged_nature_doc.md, logique metier)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Associées à un document", "Sans document"],
            Sections =
            [
                new MarcheEcran("Nature", "Le code et le libellé sont obligatoires ; le code est unique, sans quote, et ne se modifie plus une fois créé. Le type de document se choisit parmi les regroupements web.", true,
                    ["Code", "Libellé", "Type de document"]),
                new MarcheEcran("Paramétrage GED", "Les réglages de dépôt de la nature (Infogreffe, coffre-fort, GEIDE, copie conforme, Tribunal digital, type de déposant) : en lecture.", false,
                    ["Nature Infogreffe", "Événement de dépôt (demande)", "Événement de dépôt (défaut)", "Dépôt au coffre-fort", "Centralisation GEIDE",
                     "Copie conforme", "Édition supplémentaire au coffre-fort", "Nature Tribunal digital", "Type de déposant GED"]),
                new MarcheEcran("Utilisations", "Une nature déjà associée à un document, ou utilisée comme valeur par défaut, ne se supprime pas.", false,
                    ["Documents GED"]),
            ],
            Lignes =
            [
                new LigneEcran("Jugement", "code JUG · Décisions · 12 document(s)", "Associées à un document", "Validee", [0, 1, 2]),
                new LigneEcran("Ordonnance", "code ORD · Décisions · 4 document(s)", "Associées à un document", "Validee", [0, 1, 2]),
                new LigneEcran("Pièce d'identité", "code PID · Pièces · 1 document(s)", "Associées à un document", "Validee", [0, 1]),
                new LigneEcran("Courrier reçu", "code COU · aucun document", "Sans document", "Validee", []),
                new LigneEcran("PV", "code PV · aucun document", "Sans document", "AFaire", []),
                new LigneEcran("Requête", "code REQ · Pièces · 7 document(s)", "Associées à un document", "Validee", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_CABINET", Titre = "Cabinets", Sujet = "cabinets",
            Forme = FormeEcran.Referentiel, Suppression = false,
            // Les cabinets : AUDIENCE_CABINET hors audiences (AUDNC_AUDIENCE = 0, AudienceCabinet.cs:1280) et les champs de la saisie V2
            // (OPE_CREATION_CABINET.cs:25-30). Lignes : LecteurCabinets.cs ; gestes (Ajouter, Modifier ; la V2 ne supprime pas de cabinet) :
            // EcrivainCabinets.cs. Groupes : la date du cabinet contre le jour (la frontiere des invitations, l. 148).
            Colonnes = [Champ.Date("Date d'audience"), "Heure d'audience", "Type de cabinet", "Code cabinet", "Juge", "Greffier"],
            Recherche = "Rechercher un cabinet, un type, un juge…",
            Criteres = ["Date d'audience", "Type de cabinet", "Juge"],
            // Bouton final cite par la fiche v2 : DoSauve(cnx, futurEtat) (proc_cabinet.md, Detail OPE)
            Valider = "Enregistrer le cabinet",
            Rang = 7,
            Etats = EtatsEcran.Trois("À saisir", "En saisie", "Enregistré"),
            Groupes = ["À venir", "Passés"],
            Sections =
            [
                new MarcheEcran("Audience", "La date et l'heure d'audience (HH:MM) sont obligatoires. Un autre cabinet au même code, à la même date, à la même heure et au même juge est refusé.", true,
                    [Champ.Date("Date d'audience"), "Heure d'audience"]),
                new MarcheEcran("Type et code", "Le type de cabinet est obligatoire ; le code est exigé quand le type le demande. Un cabinet qui a des affaires appelées garde sa date, son type et son code.", true,
                    ["Type de cabinet", "Libellé du type", "Code cabinet", "Libellé du code"]),
                new MarcheEcran("Juge et greffier", "Le juge et le greffier se choisissent par leur abrégé. Un cabinet qui a des affaires appelées garde son juge s'il est actif.", false,
                    ["Juge", "Greffier"]),
                new MarcheEcran("Classement", "Le type de classement admis : en lecture.", false,
                    ["Classement admis"]),
            ],
            Lignes =
            [
                new LigneEcran("Cabinet 1 · 14/03/2026 à 09:00", "Cabinet de juge commissaire · juge JCA", "Passés", "Validee", []),
                new LigneEcran("Cabinet 2 · 14/03/2026 à 10:00", "Cabinet de juge commissaire · juge à renseigner", "Passés", "EnCours", []),
                new LigneEcran("Cabinet 3 · 15/12/2026 à 09:30", "Cabinet du président · juge PRE", "À venir", "Validee", []),
                new LigneEcran("Cabinet 1 · 16/12/2026 à 14:00", "Cabinet de juge commissaire · juge JCA · greffier GRA", "À venir", "Validee", []),
                new LigneEcran("Cabinet du président · 17/12/2026", "Cabinet du président · juge à renseigner", "À venir", "AFaire", []),
                new LigneEcran("Cabinet 2 · 10/02/2026 à 09:00", "Cabinet de juge commissaire · juge JCB", "Passés", "Validee", []),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARAM_IP", Titre = "Codes chefs IP", Sujet = "codes chefs",
            Forme = FormeEcran.Referentiel,
            // Colonnes de la liste V2 (OPE_PARAM_IP.cs:27-34) et le rang du code (RigListViewUpDown). Lignes : LecteurCodesChefsIp.cs
            // (le reglage IP_CODE_CHEF de PARAMETRAGE, repris dans soc.Reglage : ReprisesPhase1.cs:86). Gestes : EcrivainCodesChefsIp.cs --
            // Ajouter (en fin de liste), Supprimer (le retrait de la liste) et, au menu de la ligne, Monter / Descendre d'un rang
            // (RigListViewUpDown._MoveItem) ; chacun reecrit le reglage par l'ecriture des reglages du greffe (EcritureReglage.cs).
            // « Modifier » n'existe pas en V2 : il reste « Demonstration ».
            Colonnes = ["Code chef", "Libellé", Champ.Nombre("Ordre")],
            Gestes = ["Monter", "Descendre"],
            Recherche = "Rechercher un code chef…",
            Criteres = ["Code chef", "Libellé"],
            // Bouton final cite par la fiche v2 : DoSauveAfterOperation (proc_param_ip.md, Detail OPE)
            Valider = "Enregistrer la liste",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Codes IP proposables", "Autres codes"],
            Sections =
            [
                new MarcheEcran("Ajout", "Seuls les codes chefs commençant par IP sont proposés. Un code déjà dans la liste est refusé ; un code qui ne se résout pas n'est pas ajouté. Il va en fin de liste.", true,
                    ["Code chef", "Libellé"]),
                new MarcheEcran("Ordre", "L'ordre choisi par l'utilisateur est conservé tel quel dans la liste : « Monter » et « Descendre », au menu de la ligne, déplacent un code d'un rang.", false,
                    ["Ordre", "Liste enregistrée"]),
                new MarcheEcran("Enregistrement", "La liste est réécrite en entier. Une liste vide est refusée. Un code introuvable au rechargement disparaît à la prochaine sauvegarde.", true,
                    ["Enregistrer"]),
            ],
            Lignes =
            [
                new LigneEcran("IP01", "Injonction de payer · rang 1", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("IP02", "Injonction de payer européenne · rang 2", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("IP03", "Opposition · rang 3", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("RF01", "Code hors IP conservé à l'ouverture · rang 4", "Autres codes", "Validee", [0, 1, 2]),
                new LigneEcran("IP04", "Injonction de payer (petits litiges) · rang 5", "Codes IP proposables", "Validee", [0, 1, 2]),
                new LigneEcran("IP05", "Injonction de faire · rang 6", "Codes IP proposables", "Validee", [0, 1, 2]),
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
                new LigneEcran("Extrait Kbis · article 1", "Calcul de prix · terminé · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Calcul de prix", "Validee", [0, 2]),
                new LigneEcran("Bilan · article 2", "Calcul de prix · à lancer · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Calcul de prix", "AFaire", []),
                new LigneEcran("Scénario de commande A", "Commandes de test · terminé · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Commandes de test", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Scénario de commande B", "Commandes de test · en cours · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Commandes de test", "EnCours", [0, 1]),
                new LigneEcran("Sonde de lecture C", "Commandes de test · à lancer · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Commandes de test", "AFaire", []),
                new LigneEcran("Journal du 12/03/2026", "Journal · lecture seule · exemple : journal BORNE_LOG_PARAMETRE de COMMUN_RIG, absent de la base du greffe ; bornes pilotées par un service externe", "Journal", "EnCours", [3]),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REDAA", Titre = "Modeles de redaction", Sujet = "modèles de décision",
            Forme = FormeEcran.Referentiel,
            // Lot 15 (27/11/2026) : les objets que la V2 administre (OPE_RECH_MODEL_MAQ.cs:84-85 modeles et maquettes REDA,
            // OPE_RECH_SECTION.cs:92 sections, OPE_RECH_GLOSSAIRE.cs:96 glossaire : deux onglets, ETP_SECTION et ETP_GLOSSAIRE), lus par
            // LecteurModelesRedaction.cs, un plafond par groupe. Le GLOSSAIRE s'ajoute, se modifie et se supprime (EcrivainModelesRedaction.cs,
            // OPE_EDT_GLOSSAIRE.cs:66-91) ; modeles et maquettes passent par leur document Word en GED, les sections par l'equipe Amitel
            // (OPE_EDT_SECTION.cs:64-72) : refus nommes par l'ecrivain.
            Colonnes = ["Code", "Libellé", "Nature", "Utilisation"],
            Recherche = "Rechercher un modèle, une maquette, une section, un terme…",
            Criteres = ["Code", "Libellé"],
            // La V2 masque VALIDER (FORM_REDAA.cs:18-23) : chaque objet s'enregistre par la barre de sa fiche.
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Modèles et maquettes", "Sections", "Glossaire", "Jugements"],
            // Sous une ligne ouverte, le formulaire de son onglet de la V2 ; une ligne neuve du glossaire saisit son texte (obligatoire,
            // OPE_EDT_GLOSSAIRE.Designer.cs:98) et son choix public / prive (l. 118-120).
            SectionsParGroupe = new Dictionary<string, string[]>
            {
                ["Modèles et maquettes"] = ["Modèle ou maquette"], ["Sections"] = ["Section"], ["Glossaire"] = ["Entrée du glossaire"],
            },
            SectionsAjout = new Dictionary<string, string[]> { ["Glossaire"] = ["Entrée du glossaire"] },
            Sections =
            [
                new MarcheEcran("Modèle ou maquette", "Un modèle de document compose des maquettes, dans l'ordre ; il ne se supprime pas tant qu'il en garde. Une maquette REDA est un document Word de la GED ; utilisée par un modèle, elle ne se supprime pas. Les deux se créent, se modifient et se suppriment avec leur document Word, dans RIG v2.", true,
                    ["Code", "Libellé", "Utilisation"]),
                new MarcheEcran("Section", "Les sections réutilisables de la décision (requête SQL, fusion, génération, occurrence, persistance) ; éditées par l'équipe Amitel seulement, dans RIG v2.", false,
                    ["Code", "Libellé"]),
                new MarcheEcran("Entrée du glossaire", "Le code (fixé une fois enregistré) et le texte associé sont obligatoires, la description facultative. Une entrée privée n'appartient qu'à vous ; une entrée publique ne redevient pas privée. La liste montre le glossaire public.", false,
                    ["Code", "Libellé", Champ.Zone("Texte associé"), Champ.Case("Privé")]),
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
                new LigneEcran("MOTIFS - Motifs", "section", "Sections", "Validee", []) { Valeurs = ["MOTIFS", "Motifs", "section", "—"] },
                new LigneEcran("PARTIES - Les parties", "glossaire public", "Glossaire", "Validee", []) { Valeurs = ["PARTIES", "Les parties", "glossaire public", "—"] },
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
            // repris dans soc.Reglage et soc.ParametreGreffe) -- les 38 cles que lisent les operations de la V2, un groupe par operation --,
            // puis la banque de chaque mode actif (MetierBanques.cs:38-57) : LecteurFacturationExpresso.cs. La fiche d'un parametre ecrit
            // sa valeur (EcrivainFacturationExpresso.cs, MetierParametrage.cs:42-47) pour les seules cles PARAMETRAGE que la V2 ecrit, par
            // l'ecriture partagee d'un reglage (decision du chef, 27/11 ; lot 14, 5e8d50d). Les GRPAR_* (GREFFE_PARAMETRE) se lisent
            // seulement ; l'association mode / banque s'ecrit par « Modes de paiement ».
            Recherche = "Rechercher un paramètre de facturation…",
            Criteres = ["Paramètre", "Banque", "Mode de paiement"],
            // Bouton final cite par la fiche v2 : DoValider (PROC_PARAMEXPRESSO.cs:39-54), VERIFIER masque (FORM_PARAMEXPRESSO.cs:10)
            Valider = "Valider",
            Rang = 7,
            Etats = EtatsEcran.Trois("À renseigner", "En cours", "Enregistré"),
            // Une operation de la V2 par groupe (OPE_FACTURATION, OPE_CORPS_FACTURE, OPE_PIED_PAGE, OPE_LETTRES_CHEQUES,
            // OPE_SPECIFICITE_JUDICIAIRE, OPE_COMPTABILITE_PARAMETRES_GENERAUX, OPE_ASSOCIATION_BANQUES).
            Groupes = ["Paramètres généraux", "Corps de facture", "Pied de page", "Lettres chèques", "Spécificités judiciaires", "Comptabilité", "Banques et modes"],
            // La fiche d'une ligne (LecteurFacturationExpresso.LireFicheAsync) : un parametre, ou la banque d'un mode.
            Sections =
            [
                new MarcheEcran("Paramètre", "La clé, sa valeur et d'où elle vient : un réglage (PARAMETRAGE), que la valeur s'enregistre, ou un paramètre du greffe (GREFFE_PARAMETRE), lu seulement.", true,
                    ["Paramètre", "Valeur", "Source"]),
                new MarcheEcran("Banque du mode", "Chaque mode de paiement actif est associé à sa banque, ou à aucune ; l'association s'enregistre dans « Modes de paiement ».", false,
                    ["Mode de paiement", "Banque"]),
                new MarcheEcran("Enregistrement", "Enregistrer écrit la valeur modifiée, avec l'auteur et la date, et laisse une trace.", true,
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
                new LigneEcran("Dossiers ouverts par mois", "Requêtes préparées · exécutée · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes préparées", "Validee", [0, 2, 3]),
                new LigneEcran("Mandataires actifs", "Requêtes préparées · à lancer · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes préparées", "AFaire", []),
                new LigneEcran("Tiers payeurs actifs", "Requêtes préparées · critères à saisir · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes préparées", "EnCours", [0, 1]),
                new LigneEcran("Instances du trimestre", "Requêtes à critères · critères à saisir · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes à critères", "EnCours", [0]),
                new LigneEcran("Affaires par cabinet", "Requêtes à critères · exécution en cours · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes à critères", "EnCours", [0, 1]),
                new LigneEcran("Relances de paiement", "Requêtes à critères · exécutée · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes à critères", "Validee", [0, 1, 2, 3]),
                new LigneEcran("Décisions par juge", "Requêtes à critères · à lancer · exemple : catalogue REQUETE de COMMUN_RIG, absent de la base du greffe", "Requêtes à critères", "AFaire", []),
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
            // Lot 15 (27/11/2026) : les deux gestes de la V2 (EcrivainInscriptionsRpva.cs). « Traiter la demande » (VALIDER renomme,
            // FORM_TRAITEMENT.cs:37) rattache la demande aux mandataires nommes par leur abrege (les cases « Traiter », « Principal »
            // et « TraiterStruct » des deux grilles, RigDataGridViewTraitements*.cs) ; « Courriels remplaces » tient la liste
            // FORM_REMPLACER_EMAILS. « Refuser » : motif obligatoire (OPE_SAISIE_REJET.Designer.cs:71), 300 caracteres.
            Parametres =
            [
                Champ.Texte("Mandataires traités"), Champ.Texte("Mandataire principal"), Champ.Texte("Structures traitées"),
                Champ.Texte("Courriels remplacés"), Champ.Zone("Motif du refus"),
            ],
            Gestes = ["Traiter la demande", "Refuser"],
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
                new MarcheEcran("Mandataires traités", "Nommez par leur abrégé (ou leur numéro) les mandataires à rattacher, séparés par des virgules : sans numéro CNBF, ou déjà au numéro CNBF de la demande. Ils reçoivent le numéro CNBF, le SIREN, le courriel, les téléphones et fax du demandeur ; un seul principal, actif et avec abrégé, les autres sont désactivés. Une structure ne se rattache que si la demande a un SIREN et un CNBF de structure.", true,
                    ["Mandataires traités", "Mandataire principal", "Structures traitées"]),
                new MarcheEcran("Courriels", "Si des mandataires traités ont un autre courriel que le demandeur, nommez ceux à remplacer, ou « aucun » : un courriel vide reçoit toujours celui du demandeur.", false,
                    ["Courriels remplacés"]),
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
