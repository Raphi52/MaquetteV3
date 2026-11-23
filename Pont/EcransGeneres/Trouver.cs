namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE TROUVER du menu : les consultations et recherches (« Dossier RCS », « Dossier
// judiciaire », « Documents », « Tarifs », « Interdits de gerer »...). Lot 2 du plan « les 125
// ecrans manquants du menu ». Teinte 4 : la classe rig-tcw__rang-4 de rig-accueil.css.
//
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md, « Detail
// OPE » faisant foi) : les criteres de recherche et les colonnes deviennent les champs, les etapes
// deviennent les marches, l'action finale devient Valider. Les lignes sont une demonstration
// ANONYME (noms fictifs).
//
// Ce que la fiche ne dit pas, et que la maquette prend a sa charge :
//   - Valider est un geste de maquette quand la fiche n'a pas d'action finale (consultations en
//     lecture seule : Dossier judiciaire, Voir un PDF, Voir une image, Encaissements, Notifications,
//     Rechercher un beneficiaire). Il change l'etat de la ligne, rien n'est enregistre.
//   - Documents (PROC_RECHERCHE_GED) : sa fiche le decrit comme un outil de maintenance de la GED ;
//     ses marches le disent tel quel.
//   - FIBEN : sa fiche decrit un outil de facturation des transmissions (releve mensuel FIBEN et
//     releve trimestriel BODACC) ; le titre reste « FIBEN », libelle du menu.
//   - « Interdits de gerer » et « Rechercher un beneficiaire » sont repris tels quels du menu, sans
//     accent : tests/EcransGeneresCheck exige l'egalite du titre avec le libelle du menu.

// fix-ok: textes des marches verifies un a un contre les fiches proc_*.md (grep) : « expires » absent de
// PROC_VJUD, colonne « Nom le plus recent » liee a la case d'historique (PROC_RECHERCHE_BATEAU), aucune vue
// XML documentee dans PROC_VNOTIF, aucun refus de fichier documente dans PROC_IMAGE : corriges a la source.
public sealed class DomaineTrouver : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_VDOSSIER", Titre = "Dossier RCS", Sujet = "dossiers RCS", Rang = 4,
            Recherche = "Rechercher un dossier : numéro, SIREN, dépôt, inscription…",
            Criteres = ["Numéro : dossier, SIREN, dépôt, inscription, liaison, chrono ou non-inscrit", "Numéro d'inscription privilège"],
            Valider = "Valider le dossier",
            Etats = EtatsEcran.Trois("À consulter", "En consultation", "Consulté"),
            Groupes = ["Dossier RCS", "Non-inscrit", "Demande"],
            Marches =
            [
                Marche("Numéro", "Saisir un numéro de demande, un SIREN, un dossier, une liaison, un dépôt, une inscription, un chrono de formalité ou un non-inscrit. Un numéro de 9 caractères est traité comme un SIREN : la recherche d'entreprise s'ouvre.", true,
                    "Numéro", "Inscription privilège"),
                Marche("Choix du dossier", "Quand l'inscription a plusieurs dossiers, en choisir un dans la liste (double-clic ou Entrée). Annuler revient à la saisie.", false,
                    "Dossier retenu"),
                Marche("Identité", "La fiche du dossier : dénomination, SIREN, dates d'immatriculation et de radiation, adresse, code NAF, personne morale.", true,
                    "Dénomination", "SIREN", "Date d'immatriculation", "Date de radiation", "Adresse", "Code NAF", "Personne morale"),
                Marche("Documents", "L'arbre des documents, du plus récent au plus ancien : actes, bilans, formalités, signalements BE, inscriptions privilèges, surveillance, réquisition, pièces jointes.", true,
                    "Actes", "Bilans", "Formalités", "Signalements BE", "Inscriptions privilèges", "Surveillance", "Réquisition", "Pièces jointes"),
                Marche("Actions", "Ouvrir ou imprimer un document, importer une pièce jointe avec son bordereau, envoyer par courriel. Une pièce jointe ne se supprime qu'après confirmation.", false,
                    "Ouvrir", "Imprimer", "Importer", "Envoyer par courriel", "Supprimer une pièce jointe", "Kbis", "Kbis BE", "Kbis historique", "Synthèse RNE"),
            ],
            Lignes =
            [
                Ligne("Lambda Conseil SARL", "Dossier RCS 2026B00601 · inscrit le 02/03/2026", "Dossier RCS", "AFaire"),
                Ligne("Mu Logistique SAS", "Dossier RCS 2026B00602 · inscrit le 03/03/2026", "Dossier RCS", "EnCours", 0, 2),
                Ligne("Nu Services SARL", "Dossier RCS 2026B00603 · inscrit le 04/03/2026", "Dossier RCS", "Validee", 0, 2, 3),
                Ligne("Xi Entrepôts SAS", "Non-inscrit · dépôt du 05/03/2026", "Non-inscrit", "AFaire"),
                Ligne("Pi Études SARL", "Non-inscrit · dépôt du 06/03/2026", "Non-inscrit", "EnCours", 0),
                Ligne("Rho Habitat SAS", "Demande · bordereau de réquisition du 09/03/2026", "Demande", "Validee", 0, 2, 3, 4),
                Ligne("Sigma Transports SARL", "Dossier RCS 2026B00607 · deux dossiers pour cette inscription", "Dossier RCS", "AFaire"),
                Ligne("Tau Commerce SAS", "Dossier RCS 2026B00608 · inscrit le 12/03/2026", "Dossier RCS", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VJUD", Titre = "Dossier judiciaire", Sujet = "dossiers judiciaires", Rang = 4,
            Recherche = "Rechercher une instance, une procédure collective ou une prévention…",
            Criteres = ["Type d'entité : instance, procédure collective ou prévention", "Entité recherchée"],
            Valider = "Terminer la consultation",
            Etats = EtatsEcran.Trois("À consulter", "En consultation", "Consultée"),
            Groupes = ["Instance", "Procédure collective", "Prévention"],
            Marches =
            [
                Marche("Entité", "Choisir le type d'entité et la retrouver. Une entité déjà chargée s'affiche sans étape de recherche.", true,
                    "Type d'entité", "Entité recherchée"),
                Marche("Informations", "La fiche de l'entité : instance, procédure collective ou prévention. Lecture seule.", true,
                    "Informations principales"),
                Marche("Parties", "Les parties de l'entité : demandeurs, défendeurs, intervenants, débiteurs multiples.", true,
                    "Demandeurs", "Défendeurs", "Intervenants", "Débiteurs multiples"),
                Marche("Audiences et décisions", "Les audiences, les appels, les décisions judiciaires et le chef de demande.", false,
                    "Audiences", "Appels", "Décisions judiciaires", "Chef de demande"),
                Marche("Événements", "Les événements survenus, puis les événements futurs.", false,
                    "Événements survenus", "Événements futurs"),
            ],
            Lignes =
            [
                Ligne("Rôle 2026J00101 · Omega Conseil SAS c/ Kappa Négoce SARL", "Instance · mode référé", "Instance", "AFaire"),
                Ligne("Rôle 2026J00102 · Iota Bois SAS c/ Thêta Import SARL", "Instance · au fond", "Instance", "EnCours", 0),
                Ligne("Rôle 2026J00103 · Chi Pêche SARL c/ Psi Paiement SAS", "Instance · audiences à venir", "Instance", "Validee", 0, 1, 2, 3),
                Ligne("Lambda Conseil SARL", "Procédure collective · débiteur unique", "Procédure collective", "AFaire"),
                Ligne("Mu Logistique SAS", "Procédure collective · débiteurs multiples", "Procédure collective", "EnCours", 0, 1),
                Ligne("Nu Services SARL", "Prévention · mode référé", "Prévention", "Validee", 0, 1, 2),
                Ligne("Xi Entrepôts SAS", "Prévention · consultation seule", "Prévention", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REGJUD", Titre = "Registres judiciaires", Sujet = "lignes de registre", Rang = 4,
            Recherche = "Filtrer les lignes du registre : rôle, nature, chambre…",
            Criteres = ["Date d'acte", "Date de saisine", "Date d'enrôlement", "Type d'instance"],
            Valider = "Imprimer le registre",
            Etats = EtatsEcran.Trois("À filtrer", "Sélection en cours", "Imprimé"),
            Groupes = ["Répertoire général", "Injonctions de payer", "Ordonnances du président"],
            Marches =
            [
                Marche("Critères", "Saisir au moins un critère pour compter les lignes : dates d'acte, de saisine ou d'enrôlement, type d'instance.", true,
                    "Dates d'acte", "Dates de saisine", "Dates d'enrôlement", "Type d'instance"),
                Marche("Résultats", "Les lignes trouvées, avec leur compte. Colonnes : date d'acte, numéro au répertoire général, rôle, nature, chambre, date et type de décision, ressort.", true,
                    "Date d'acte", "Numéro au répertoire", "Rôle", "Nature", "Chambre", "Date de décision", "Type de décision", "Ressort"),
                Marche("Ventilation", "Ventiler les résultats par type d'instance, avec le nombre d'instances de chaque type.", false,
                    "Type d'instance", "Nombre d'instances"),
                Marche("Sélection", "Cocher les lignes à imprimer seulement, pour ne sortir qu'une partie du registre.", false,
                    "Lignes cochées"),
                Marche("Impression", "Imprimer le registre. Le bouton ne s'active qu'une fois au moins une ligne trouvée.", true,
                    "Aperçu", "Impression"),
            ],
            Lignes =
            [
                Ligne("Rôle 2026J00201 · Pi Études SARL c/ Rho Habitat SAS", "Acte du 03/03/2026 · n° 1201 · chambre commerciale", "Répertoire général", "AFaire"),
                Ligne("Rôle 2026J00202 · Sigma Transports SARL c/ Tau Commerce SAS", "Acte du 04/03/2026 · n° 1202", "Répertoire général", "EnCours", 0),
                Ligne("Rôle 2026J00203 · Upsilon Import SARL c/ Phi Bâtiment SAS", "Acte du 05/03/2026 · n° 1203", "Répertoire général", "Validee", 0, 1, 4),
                Ligne("Injonction 2026IP0301 · Chi Pêche SARL", "Décision du 06/03/2026", "Injonctions de payer", "AFaire"),
                Ligne("Injonction 2026IP0302 · Psi Paiement SAS", "Décision du 09/03/2026", "Injonctions de payer", "EnCours", 0, 1),
                Ligne("Ordonnance 2026OP0401 · Omega Conseil SAS", "Ordonnance du 10/03/2026", "Ordonnances du président", "Validee", 0, 1, 4),
                Ligne("Ordonnance 2026OP0402 · Kappa Négoce SARL", "Ordonnance du 11/03/2026", "Ordonnances du président", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHERCHE_BATEAU", Titre = "Navires", Sujet = "navires", Rang = 4,
            Recherche = "Rechercher un navire : nom ou numéro d'enregistrement…",
            Criteres = ["Nom du navire (recherche dans le texte)", "Numéro d'enregistrement (une partie suffit)", "Afficher l'historique des noms"],
            Valider = "Renvoyer la sélection",
            Etats = EtatsEcran.Trois("À chercher", "Sélection en cours", "Renvoyée"),
            Groupes = ["Nom actuel", "Ancien nom"],
            Marches =
            [
                Marche("Recherche", "Saisir le nom ou une partie du numéro d'enregistrement. Les deux critères se cumulent.", true,
                    "Nom du navire", "Numéro d'enregistrement"),
                Marche("Historique", "Afficher l'historique fait apparaître la colonne « Nom le plus récent ».", false,
                    "Afficher l'historique", "Nom le plus récent"),
                Marche("Détails", "Consulter le navire sélectionné : numéro d'enregistrement, nom et informations du référentiel bateau.", false,
                    "Numéro d'enregistrement", "Nom du navire", "Informations du référentiel"),
                Marche("Sélection", "Cocher les navires à retenir. Valider renvoie la liste à l'écran appelant.", true,
                    "Navires retenus"),
            ],
            Lignes =
            [
                Ligne("Le Pélican Alpha", "Numéro d'enregistrement 0001 · immatriculé en 2019", "Nom actuel", "AFaire"),
                Ligne("L'Étoile Bêta", "Numéro 0002 · immatriculé en 2020", "Nom actuel", "EnCours", 0),
                Ligne("La Mouette Gamma", "Numéro 0003 · ancien nom : Le Vent d'Ouest", "Ancien nom", "EnCours", 0, 1),
                Ligne("Le Phare Delta", "Numéro 0004 · immatriculé en 2016", "Nom actuel", "Validee", 0, 3),
                Ligne("Le Pélican Epsilon", "Numéro 0005 · immatriculé en 2011", "Nom actuel", "AFaire"),
                Ligne("Le Dauphin Zêta", "Numéro 0006 · ancien nom : Le Grand Large", "Ancien nom", "EnCours", 0, 2),
                Ligne("Le Hérisson Êta", "Numéro 0007 · immatriculé en 2008", "Nom actuel", "Validee", 0, 1, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHERCHE_GED", Titre = "Documents", Sujet = "documents", Rang = 4,
            Recherche = "Rechercher un document : numéro interne ou login d'extraction…",
            Criteres = ["Catégorie : maquettes, éléments de fusion, documents d'édition, factures, documents SIREN, orphelins", "Numéro interne du document", "Login d'extraction"],
            Valider = "Appliquer à la sélection",
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traité"),
            Groupes = ["Maquettes", "Éléments de fusion", "Documents d'édition", "Factures", "Documents SIREN", "Orphelins"],
            Marches =
            [
                Marche("Catégorie", "Choisir une catégorie, ou chercher par numéro interne ou login d'extraction. Le résultat est limité à 100 lignes, sans pagination.", true,
                    "Catégorie", "Numéro interne", "Login d'extraction"),
                Marche("Propriétés", "Consulter les propriétés du document sélectionné : catégorie, dernière version.", false,
                    "Numéro interne", "Catégorie", "Dernière version"),
                Marche("Extraction", "Récupérer la dernière version dans un répertoire, ou annuler une extraction.", false,
                    "Répertoire de destination", "Annuler l'extraction"),
                Marche("Archivage et suppression", "Archiver ou supprimer les documents sélectionnés, plusieurs à la fois.", true,
                    "Archiver", "Supprimer"),
            ],
            Lignes =
            [
                Ligne("Maquette de fiche client", "Document 0004521 · maquette HTML", "Maquettes", "AFaire"),
                Ligne("Élément de fusion : lettre de rappel", "Document 0004522", "Éléments de fusion", "EnCours", 0),
                Ligne("Attestation de dépôt 2026-0310", "Document 0004523 · extrait avec le login ext2026", "Documents d'édition", "Validee", 0, 1, 3),
                Ligne("Facture 2026-00815", "Document 0004524", "Factures", "EnCours", 0, 1),
                Ligne("Document SIREN 0004525", "Document rattaché à une entreprise", "Documents SIREN", "AFaire"),
                Ligne("Pièce orpheline 0004526", "Référence externe supprimée", "Orphelins", "EnCours", 0, 2),
                Ligne("Extrait d'édition 0004527", "Édition du 12/03/2026", "Documents d'édition", "Validee", 0, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PDF", Titre = "Voir un PDF", Sujet = "pièces PDF", Rang = 4,
            Recherche = "Rechercher une pièce à lire…",
            Criteres = ["Fichier PDF unique, ou dossier de PDF", "Pièces non lues affichées en gras", "Alerte à la fermeture si une pièce n'est pas lue"],
            Valider = "Marquer comme lu",
            Etats = EtatsEcran.Trois("Non lu", "En lecture", "Lu"),
            Groupes = ["Pièce unique", "Dossier de pièces"],
            Marches =
            [
                Marche("Ouverture", "Ouvrir le PDF. Un dossier de PDF liste ses fichiers ; s'il n'en contient qu'un, il s'ouvre directement.", true,
                    "Fichier", "Dossier"),
                Marche("Lecture", "Lire la pièce. Quitter une pièce non lue déclenche une alerte.", true,
                    "Pièce", "Lue"),
                Marche("Enregistrer ou imprimer", "Enregistrer une copie sous un autre nom, ou imprimer tout le dossier de pièces.", false,
                    "Enregistrer sous", "Imprimer tout"),
            ],
            Lignes =
            [
                Ligne("Courrier du greffe 2026-0412", "PDF · 4 pages", "Pièce unique", "AFaire"),
                Ligne("Jugement annexé 2026-0413", "PDF · 12 pages", "Pièce unique", "EnCours", 0),
                Ligne("Bilan 2025 (dossier de 3 PDF)", "Dossier · 3 fichiers, 1 lu", "Dossier de pièces", "EnCours", 0),
                Ligne("Statuts de la société Alpha", "PDF · 8 pages", "Pièce unique", "Validee", 0, 1),
                Ligne("Procès-verbal d'assemblée (dossier de 2 PDF)", "Dossier · 2 fichiers", "Dossier de pièces", "AFaire"),
                Ligne("Pouvoir de représentation 2026-0420", "PDF · 2 pages", "Pièce unique", "Validee", 0, 1, 2),
                Ligne("Attestation de régularité 2026-0421", "PDF · 1 page", "Pièce unique", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IMAGE", Titre = "Voir une image", Sujet = "images et PDF multi-pages", Rang = 4,
            Recherche = "Rechercher une pièce numérisée…",
            Criteres = ["Formats : PDF et TIFF multi-pages, JPEG, PNG, BMP", "Navigation entre les pages à partir de deux pages", "Zoom par clic ou à la molette"],
            Valider = "Terminer la lecture",
            Etats = EtatsEcran.Trois("Non vue", "En cours de lecture", "Vue"),
            Groupes = ["Image", "PDF ou TIFF multi-pages"],
            Marches =
            [
                Marche("Ouverture", "Ouvrir l'image ou le PDF, par son chemin complet.", true,
                    "Fichier", "Titre de la fenêtre"),
                Marche("Pages", "Parcourir toutes les pages. La navigation n'apparaît qu'à partir de deux pages.", true,
                    "Page précédente", "Page suivante"),
                Marche("Zoom", "Zoomer sur un détail, par clic ou à la molette.", false,
                    "Zoom par clic", "Zoom à la molette"),
            ],
            Lignes =
            [
                Ligne("Pièce numérisée 2026-0501", "JPEG · page unique", "Image", "AFaire"),
                Ligne("Pièce numérisée 2026-0502", "PNG · page unique", "Image", "Validee", 0, 1),
                Ligne("Pièce numérisée 2026-0503", "BMP · page unique", "Image", "EnCours", 0),
                Ligne("Acte numérisé 2026-0504", "TIFF · 6 pages", "PDF ou TIFF multi-pages", "EnCours", 0),
                Ligne("Jugement numérisé 2026-0505", "PDF · 14 pages", "PDF ou TIFF multi-pages", "AFaire"),
                Ligne("Bilan numérisé 2026-0506", "PDF · 9 pages", "PDF ou TIFF multi-pages", "Validee", 0, 1, 2),
                Ligne("Attestation numérisée 2026-0507", "JPEG · page unique", "Image", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VCHIFFRES", Titre = "Chiffres d'un dossier", Sujet = "exercices", Rang = 4,
            Recherche = "Rechercher un dossier RCS : numéro ou SIREN…",
            Criteres = ["Dossier RCS : numéro ou SIREN", "Exercices sans chiffres clés (réglage du greffe)"],
            Valider = "Générer le PDF",
            Etats = EtatsEcran.Trois("À consulter", "En cours", "PDF généré"),
            Groupes = ["Bilans déposés", "Exercices sans chiffres"],
            Marches =
            [
                Marche("Dossier", "Le dossier RCS, passé à l'écran ou recherché à la main. Sans dossier, l'écran s'ouvre en recherche manuelle.", true,
                    "Dossier RCS"),
                Marche("Chiffres seuils", "La grille des chiffres seuils par exercice, tirés des bilans déposés. Lecture seule.", true,
                    "Chiffre d'affaires", "Résultat", "Actif", "Capital", "Effectif"),
                Marche("Actes de l'exercice", "Les actes et dépôts de bilan rattachés à l'exercice choisi.", false,
                    "Actes déposés", "Dépôt du bilan"),
                Marche("Export PDF", "Générer le PDF des chiffres seuils, en brouillon.", true,
                    "Édition PDF"),
            ],
            Lignes =
            [
                Ligne("Exercice 2025", "Bilan déposé · clôture au 31/12/2025", "Bilans déposés", "AFaire"),
                Ligne("Exercice 2024", "Bilan déposé · clôture au 31/12/2024", "Bilans déposés", "EnCours", 0, 1),
                Ligne("Exercice 2023", "Bilan déposé · clôture au 31/12/2023", "Bilans déposés", "Validee", 0, 1, 3),
                Ligne("Exercice 2022", "Sans chiffres clés · clôture au 31/12/2022", "Exercices sans chiffres", "AFaire"),
                Ligne("Exercice 2021", "Sans chiffres clés · clôture au 31/12/2021", "Exercices sans chiffres", "EnCours", 0),
                Ligne("Exercice 2020", "Bilan déposé · clôture au 31/12/2020", "Bilans déposés", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHENC", Titre = "Encaissements", Sujet = "encaissements", Rang = 4,
            Recherche = "Rechercher un encaissement : client, remise, banque, commentaire…",
            Criteres = ["Dates de début et de fin (à cocher pour les activer)", "Mode de paiement", "Banque", "Payeur ou tiers payeur", "Numéro de remise", "Montant minimum et maximum", "Affaire"],
            Valider = "Marquer comme contrôlé",
            Etats = EtatsEcran.Trois("À contrôler", "En contrôle", "Contrôlé"),
            Groupes = ["Encaissement ventilé", "Encaissement sans ventilation"],
            Marches =
            [
                Marche("Critères", "Saisir les critères, qui se cumulent. Une date ne compte que si sa case est cochée.", true,
                    "Dates", "Mode de paiement", "Banque", "Payeur", "Numéro de remise", "Montant minimum", "Montant maximum", "Affaire"),
                Marche("Résultats", "Un encaissement par ligne, ses ventilations dessous. Un encaissement sans ventilation réelle s'affiche en rouge.", true,
                    "Encaissement", "Ventilation"),
                Marche("Affaire", "Double-clic sur une ventilation : ouvre son affaire.", false,
                    "Affaire ouverte"),
                Marche("Export", "Exporter les lignes à plat, une par ventilation, vers un tableur. L'export peut être interrompu.", false,
                    "Fichier du tableur"),
            ],
            Lignes =
            [
                Ligne("Encaissement 2026E0101 · Psi Paiement SAS", "Chèque · 1 200,00 € · remise du 03/03/2026", "Encaissement ventilé", "EnCours", 0),
                Ligne("Encaissement 2026E0102 · Chi Pêche SARL", "Virement · 850,00 € · sans ventilation", "Encaissement sans ventilation", "AFaire"),
                Ligne("Encaissement 2026E0103 · Sigma Transports SARL", "Prélèvement · 2 400,00 € · remise du 05/03/2026", "Encaissement ventilé", "Validee", 0, 1, 2),
                Ligne("Encaissement 2026E0104 · Tau Commerce SAS", "Carte bancaire · 310,00 € · sans ventilation", "Encaissement sans ventilation", "EnCours", 0),
                Ligne("Encaissement 2026E0105 · Upsilon Import SARL", "Chèque · 95,00 € · ventilé", "Encaissement ventilé", "Validee", 0, 1),
                Ligne("Encaissement 2026E0106 · Phi Bâtiment SAS", "Virement · 640,00 € · sans ventilation", "Encaissement sans ventilation", "AFaire"),
                Ligne("Encaissement 2026E0107 · Rho Habitat SAS", "Prélèvement · 1 050,00 € · remise du 09/03/2026", "Encaissement ventilé", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TARIFS", Titre = "Tarifs", Sujet = "articles tarifaires", Rang = 4,
            Recherche = "Filtrer les articles : code, libellé…",
            Criteres = ["Statut : actif, inactif ou tous", "Type d'article", "Période de tarification", "Catégorie d'article", "Code événement (affiche sa colonne)"],
            Valider = "Justifier l'incohérence",
            Etats = EtatsEcran.Trois("À justifier", "En cours", "Justifié"),
            Groupes = ["Article de base", "Article dérivé"],
            Marches =
            [
                Marche("Critères", "Saisir au moins un critère, qui se cumulent : statut, type, période de tarification, catégorie, code événement.", true,
                    "Statut", "Type d'article", "Période de tarification", "Catégorie", "Code événement"),
                Marche("Résultats", "Les articles : code, libellé, montant dans la devise du greffe, et « Cohérent ? ». Au-delà du seuil du greffe (100 par défaut), une alerte demande d'affiner.", true,
                    "Code", "Libellé", "Montant", "Cohérent ?"),
                Marche("Cohérence", "Justifier une incohérence, la dévalider, ou voir ses raisons. Une justification vaut pour toute la famille de l'article.", true,
                    "Raisons de l'incohérence", "Justification"),
                Marche("Dérivation", "Dupliquer un article en famille dérivée. Refusé pour un article déjà dérivé. Le nouveau code est distinct et tient en 18 caractères au plus.", false,
                    "Code de l'article dérivé"),
                Marche("Catégorie en masse", "Réaffecter une catégorie aux articles cochés, familles comprises, après confirmation.", false,
                    "Catégorie cible"),
            ],
            Lignes =
            [
                Ligne("Article 0101 · Inscription au greffe", "45,00 € · incohérent", "Article de base", "AFaire"),
                Ligne("Article 0102 · Modification", "60,00 € · incohérent, en examen", "Article de base", "EnCours", 0, 1),
                Ligne("Article 0103 · Extrait", "12,50 € · cohérent, rien à justifier", "Article de base", "Validee", 0, 1, 2),
                Ligne("Article 0201 · Copie dérivée", "Dérivé de l'article 0101 · 40,00 €", "Article dérivé", "AFaire"),
                Ligne("Article 0202 · Certificat", "Dérivé de l'article 0103 · 15,00 € · justifié", "Article dérivé", "Validee", 0, 1, 2, 3),
                Ligne("Article 0104 · Dépôt d'actes", "25,00 € · incohérent, en examen", "Article de base", "EnCours", 0),
                Ligne("Article 0105 · Recherche de pièce", "18,00 € · sans catégorie", "Article de base", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REJETS_BILANS", Titre = "Bilans en rejet", Sujet = "bilans en rejet", Rang = 4,
            Recherche = "Rechercher un bilan : SIREN, numéro de gestion ou numéro de dépôt…",
            Criteres = ["SIREN, numéro de gestion ou numéro de dépôt", "Codes motifs de rejet (plusieurs possibles)"],
            Valider = "Marquer comme corrigé",
            Etats = EtatsEcran.Trois("Rejeté", "En correction", "Corrigé"),
            Groupes = ["Rejet Infogreffe", "Rejet INPI"],
            Marches =
            [
                Marche("Recherche", "Saisir un SIREN, un numéro de gestion ou un numéro de dépôt, et choisir les codes motifs. Sans motif choisi, tous les motifs sont montrés.", true,
                    "Numéro", "Codes motifs de rejet"),
                Marche("Aperçu", "Lire l'aperçu PDF du bilan sélectionné.", true,
                    "Aperçu du bilan"),
                Marche("Correction", "Envoyer le bilan vers la correction (XDCA), ou éditer le bordereau de numérisation.", true,
                    "Envoi à la correction", "Bordereau de numérisation"),
                Marche("Dossier RCS", "Ouvrir le dossier RCS du dépôt (VGED), pour y retrouver le bilan.", false,
                    "Dossier RCS du dépôt"),
                Marche("Renvoi à Infogreffe", "Renvoyer le bilan corrigé à Infogreffe. Réservé aux utilisateurs autorisés, et seulement pour les motifs qui le permettent.", false,
                    "Renvoi à Infogreffe"),
            ],
            Lignes =
            [
                Ligne("Bilan 2025 · Omega Conseil SAS", "Dépôt 2026-0701 · motif A", "Rejet Infogreffe", "AFaire"),
                Ligne("Bilan 2025 · Iota Bois SAS", "Dépôt 2026-0702 · motif B", "Rejet Infogreffe", "EnCours", 0),
                Ligne("Bilan 2024 · Thêta Import SARL", "Dépôt 2026-0703 · motif C", "Rejet INPI", "Validee", 0, 1, 2, 4),
                Ligne("Bilan 2024 · Mu Logistique SAS", "Dépôt 2026-0704 · motif A", "Rejet INPI", "EnCours", 0, 1),
                Ligne("Bilan 2023 · Nu Services SARL", "Dépôt 2026-0705 · motif B", "Rejet Infogreffe", "AFaire"),
                Ligne("Bilan 2023 · Xi Entrepôts SAS", "Dépôt 2026-0706 · motif A", "Rejet INPI", "Validee", 0, 1, 2, 3),
                Ligne("Bilan 2025 · Tau Commerce SAS", "Dépôt 2026-0707 · motif C", "Rejet Infogreffe", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VFNIG", Titre = "Interdits de gerer", Sujet = "personnes", Rang = 4,
            Recherche = "Rechercher une personne : nom, prénom, date de naissance…",
            Criteres = ["Nom", "Prénom", "Date de naissance", "Pays de naissance (France par défaut)", "Commune de naissance (si France)"],
            Valider = "Valider la personne",
            Etats = EtatsEcran.Trois("À vérifier", "En cours", "Vérifiée"),
            Groupes = ["Personne trouvée", "Personne inexistante"],
            Marches =
            [
                Marche("Recherche", "Saisir le nom, le prénom et la date de naissance. Pour une naissance hors de France, la commune n'est pas demandée.", true,
                    "Nom", "Prénom", "Date de naissance", "Pays de naissance", "Commune de naissance"),
                Marche("Jugements", "Les jugements de la personne : les actifs d'abord, les plus récents en tête.", true,
                    "Jugements actifs", "Jugements anciens"),
                Marche("Confirmation", "Une personne absente du registre se confirme avant d'être ajoutée.", false,
                    "Confirmer l'ajout"),
                Marche("Détail", "Ouvrir le détail d'un jugement sélectionné.", false,
                    "Détail du jugement"),
                Marche("Document Word", "Exporter le résultat dans un document Word.", false,
                    "Document Word"),
            ],
            Lignes =
            [
                Ligne("Personne 0101", "Né(e) en 1970 · 2 jugements, dont 1 actif", "Personne trouvée", "AFaire"),
                Ligne("Personne 0102", "Né(e) en 1965 · 1 jugement actif", "Personne trouvée", "EnCours", 0),
                Ligne("Personne 0103", "Né(e) en 1981 · jugement ancien", "Personne trouvée", "Validee", 0, 1),
                Ligne("Personne 0104", "Absente du registre · confirmation requise", "Personne inexistante", "EnCours", 0, 2),
                Ligne("Personne 0105", "Né(e) à l'étranger en 1977 · 1 jugement actif", "Personne trouvée", "AFaire"),
                Ligne("Personne 0106", "Né(e) en 1959 · 3 jugements", "Personne trouvée", "Validee", 0, 1, 3),
                Ligne("Personne 0107", "Absente du registre · ajoutée après confirmation", "Personne inexistante", "Validee", 0, 1, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_FIBEN", Titre = "FIBEN", Sujet = "relevés", Rang = 4,
            Recherche = "Rechercher un relevé : mois ou trimestre…",
            Criteres = ["Mois (transmission FIBEN, mensuelle)", "Trimestre et année (relevé BODACC, trimestriel)", "Tarifs unitaires"],
            Valider = "Générer le relevé",
            Etats = EtatsEcran.Trois("À générer", "En cours", "Généré"),
            Groupes = ["Transmission FIBEN", "Relevé BODACC"],
            Marches =
            [
                Marche("Période", "Choisir le mois de la transmission FIBEN, ou le trimestre et l'année du relevé BODACC.", true,
                    "Mois", "Trimestre", "Année"),
                Marche("Tarifs", "Saisir les tarifs unitaires : quatre pour la transmission, six pour le relevé.", true,
                    "Tarifs de la transmission (4)", "Tarifs du relevé (6)"),
                Marche("Génération", "Générer le relevé à partir des volumes comptés pour chaque greffe en exploitation.", true,
                    "Volumes par greffe", "Relevé à générer"),
            ],
            Lignes =
            [
                Ligne("Transmission FIBEN · janvier 2026", "Avis RCS et PC · mensuelle", "Transmission FIBEN", "Validee", 0, 1, 2),
                Ligne("Transmission FIBEN · février 2026", "Avis RCS et PC · mensuelle", "Transmission FIBEN", "EnCours", 0),
                Ligne("Transmission FIBEN · mars 2026", "Avis RCS et PC · mensuelle", "Transmission FIBEN", "AFaire"),
                Ligne("Relevé BODACC · 4e trimestre 2025", "Annonces A, B, C et P · trimestriel", "Relevé BODACC", "Validee", 0, 1, 2),
                Ligne("Relevé BODACC · 1er trimestre 2026", "Annonces A, B, C et P · trimestriel", "Relevé BODACC", "EnCours", 0, 1),
                Ligne("Relevé BODACC · 2e trimestre 2026", "Annonces A, B, C et P · trimestriel", "Relevé BODACC", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VNOTIF", Titre = "Notifications", Sujet = "notifications", Rang = 4,
            Recherche = "Rechercher une notification : greffe, date, état…",
            Criteres = ["Dates", "États", "Greffes", "Origine (alerte)"],
            Valider = "Marquer comme traitée",
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            Groupes = ["Émises (RCS)", "Émises (judiciaire)", "Reçues"],
            Marches =
            [
                Marche("Recherche", "Choisir l'onglet (émises ou reçues) et les filtres : dates, états, greffes. Une alerte peut ouvrir cet écran directement.", true,
                    "Dates", "États", "Greffes", "Origine"),
                Marche("Contenu", "Lire la notification. Les colonnes changent selon le service : RCS ou judiciaire.", true,
                    "Identifiant", "Type", "Greffe", "Date d'envoi", "Date de réception"),
            ],
            Lignes =
            [
                Ligne("Notification 2026-0901 · vers un greffe voisin", "Émise le 02/03/2026", "Émises (RCS)", "AFaire"),
                Ligne("Notification 2026-0902", "Émise le 03/03/2026", "Émises (RCS)", "EnCours", 0),
                Ligne("Notification 2026-0903", "Émise le 04/03/2026", "Émises (judiciaire)", "Validee", 0, 1),
                Ligne("Notification 2026-0904 · d'un greffe voisin", "Reçue le 05/03/2026", "Reçues", "AFaire"),
                Ligne("Notification 2026-0905", "Reçue le 06/03/2026 · avec alerte", "Reçues", "EnCours", 0),
                Ligne("Notification 2026-0906", "Reçue le 09/03/2026", "Reçues", "Validee", 0, 1),
                Ligne("Notification 2026-0907", "Émise le 10/03/2026", "Émises (judiciaire)", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VTRID", Titre = "Tribunal digital", Sujet = "échanges avec le tribunal digital", Rang = 4,
            Recherche = "Rechercher un envoi ou une demande…",
            Criteres = ["Date (critère par défaut)", "Type d'entité : instance, procédure collective, mesure, prévention"],
            Valider = "Ouvrir dans le dossier judiciaire",
            Etats = EtatsEcran.Trois("Non traité", "En cours", "Traité"),
            Groupes = ["Documents transmis", "Demandes reçues"],
            Marches =
            [
                Marche("Critères", "Chercher par date (critère par défaut) ou par type d'entité. Une entité s'ouvre dans son formulaire de recherche.", true,
                    "Date", "Type d'entité"),
                Marche("Documents transmis", "Les documents envoyés au tribunal digital, avec leur numéro de rôle et leur compte rendu d'envoi.", true,
                    "Numéro de rôle", "Compte rendu d'envoi"),
                Marche("Demandes reçues", "Les demandes venues du portail, dans l'onglet à part.", false,
                    "Demande"),
                Marche("Preuve et dossier", "Télécharger la preuve de dépôt, si le compte rendu est OK, ou ouvrir l'affaire dans le dossier judiciaire.", false,
                    "Preuve de dépôt", "Dossier judiciaire"),
            ],
            Lignes =
            [
                Ligne("Envoi 2026-1001 · Lambda Conseil SARL", "Rôle 2026J00101 · envoyé le 02/03/2026", "Documents transmis", "AFaire"),
                Ligne("Envoi 2026-1002 · Mu Logistique SAS", "Rôle 2026J00102 · envoyé le 03/03/2026", "Documents transmis", "EnCours", 0),
                Ligne("Envoi 2026-1003 · Nu Services SARL", "Rôle 2026J00103 · compte rendu OK", "Documents transmis", "Validee", 0, 1, 3),
                Ligne("Demande 2026-1004 · Sigma Transports SARL", "Reçue le 04/03/2026 · instance", "Demandes reçues", "AFaire"),
                Ligne("Demande 2026-1005 · Pi Études SARL", "Reçue le 05/03/2026 · procédure collective", "Demandes reçues", "EnCours", 0),
                Ligne("Demande 2026-1006 · Rho Habitat SAS", "Reçue le 06/03/2026 · prévention", "Demandes reçues", "Validee", 0, 1, 2),
                Ligne("Envoi 2026-1007 · Tau Commerce SAS", "Rôle 2026J00107 · compte rendu en attente", "Documents transmis", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BEN", Titre = "Rechercher un beneficiaire", Sujet = "bénéficiaires effectifs", Rang = 4,
            Recherche = "Rechercher un bénéficiaire : nom, prénom, numéro de dossier…",
            Criteres = ["Numéro de dossier", "Nom d'usage", "Nom patronymique", "Prénoms", "Date de naissance", "Dossiers radiés", "Divergences seulement"],
            Valider = "Signaler une divergence",
            Etats = EtatsEcran.Trois("À examiner", "En cours", "Divergence signalée"),
            Groupes = ["Bénéficiaire en vigueur", "Dossier radié"],
            Marches =
            [
                Marche("Critères", "Saisir le numéro de dossier, ou le nom, les prénoms et la date de naissance. Les critères se cumulent ; Effacer critères vide la saisie.", true,
                    "Numéro de dossier", "Nom d'usage", "Nom patronymique", "Prénoms", "Date de naissance", "Dossiers radiés", "Divergences seulement"),
                Marche("Résultats", "La liste des bénéficiaires. Un double-clic sur un numéro de gestion relance la recherche sur ce dossier ; sur un nom, la recherche sur cette personne.", true,
                    "Numéro de gestion", "Nom", "Prénoms", "Date de naissance"),
                Marche("Divergence", "Signaler une divergence sur le bénéficiaire sélectionné, par le menu contextuel.", true,
                    "Identifiant de divergence"),
            ],
            Lignes =
            [
                Ligne("Bénéficiaire 0001 · dossier 2026B00601", "Né(e) en 1970 · en vigueur", "Bénéficiaire en vigueur", "AFaire"),
                Ligne("Bénéficiaire 0002 · dossier 2026B00602", "Né(e) en 1958 · en vigueur", "Bénéficiaire en vigueur", "EnCours", 0),
                Ligne("Bénéficiaire 0003 · dossier 2026B00603", "Né(e) en 1982 · divergence à signaler", "Bénéficiaire en vigueur", "Validee", 0, 1, 2),
                Ligne("Bénéficiaire 0004 · dossier 2026B00604", "Né(e) en 1975 · dossier radié", "Dossier radié", "AFaire"),
                Ligne("Bénéficiaire 0005 · dossier 2026B00605", "Né(e) en 1963 · dossier radié", "Dossier radié", "EnCours", 0, 1),
                Ligne("Bénéficiaire 0006 · dossier 2026B00606", "Né(e) en 1990 · en vigueur", "Bénéficiaire en vigueur", "EnCours", 0),
                Ligne("Bénéficiaire 0007 · dossier 2026B00607", "Né(e) en 1968 · dossier radié · divergence signalée", "Dossier radié", "Validee", 0, 1, 2),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params string[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
