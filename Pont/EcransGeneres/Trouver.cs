namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE TROUVER du menu : les consultations et recherches (« Dossier RCS », « Dossier
// judiciaire », « Documents », « Tarifs », « Interdits de gerer »...). Lot 2 du plan « les 125
// ecrans manquants du menu ». Teinte 4 : la classe rig-tcw__rang-4 de rig-accueil.css.
//
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md, « Detail
// OPE » faisant foi) : les criteres de recherche et les colonnes deviennent les champs, les etapes
// deviennent les marches. Les lignes sont une demonstration ANONYME (noms fictifs).
//
// Le bouton final (Valider) est toujours un libelle que la fiche cite : un bouton visible de sa
// barre d'outils, ou une action de son menu. La ligne de la fiche est donnee au-dessus du Valider de
// chaque descripteur. Une fiche sans validation (« Processus sans validation », proc_image.md:85) prend
// pour bouton final une action qu'elle cite, a confirmer : c'est le cas de Voir une image (APERCU).
//
// Ce que la fiche ne dit pas, et que la maquette prend a sa charge :
//   - Documents (PROC_RECHERCHE_GED) : sa fiche le decrit comme un outil de maintenance de la GED ;
//     ses marches le disent tel quel.
//   - FIBEN : sa fiche decrit un outil de facturation des transmissions (releve mensuel FIBEN et
//     releve trimestriel BODACC) ; le titre reste « FIBEN », libelle du menu.
//   - « Interdits de gerer » et « Rechercher un beneficiaire » sont repris tels quels du menu, sans
//     accent : tests/EcransGeneresCheck exige l'egalite du titre avec le libelle du menu.
//   - Voir une image (PROC_IMAGE) : la fiche decrit un visualiseur passif, sans etat ni validation
//     (proc_image.md:101-105). Son bouton final est APERCU, a confirmer.
//
// BRANCHES SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026) : quinze de ces seize ecrans ont leur
// lecteur (Serveur/Socle/EcransGeneres/Lecteur*.cs, nomme dans un commentaire de chaque descripteur) ; leurs criteres, leurs
// sections, leurs groupes et leurs etats sont ceux que ce lecteur lit et cherche, chacun cite sur sa ligne de fiche V2. Les
// lignes ci-dessous restent la demonstration, montree sans serveur. « Interdits de gerer » n'a pas de lecteur : RIG v2
// interroge le service web du FNIG, pas la base du greffe.

// fix-ok: cause mesurée — 14 des 16 « Valider » de la version committée (HEAD d242adc) n'existaient pas dans leur fiche (gestes de maquette) ; chacun est cité sur sa ligne de fiche. PROC_IMAGE, lui, restait sans écran (banc EcransGeneres -Detail, liste « sans ecran ni descripteur ») : descripteur ajouté (contrôle : 16/16).
public sealed class DomaineTrouver : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_VDOSSIER", Titre = "Dossier RCS", Sujet = "dossiers RCS", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurDossierRcs.cs (lot 6, plan-676a4b19). Critères : l'entrée par numéro
            // (proc_vdossier.md:314-347), la recherche d'entreprise par nom (OPE_ENTREE_VGED_NUMERO.cs:76-107) et l'entrée par
            // inscription de privilège (proc_vdossier.md:349-369).
            Recherche = "Rechercher un dossier : numéro, SIREN, dépôt, demande, non-inscrit ou nom…",
            Criteres = ["Numéro", "Dénomination", "Inscription privilège"],
            // Valider : proc_vdossier.md:378 (barre « Valider/Annuler » de l'étape de choix du dossier) et :397 (VALIDER → _Valider).
            Valider = "Valider",
            // États : les colonnes de DOSSIER_RCS (radiation, provisoirement sans activité, cessation) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Actif", "Sans activité", "Radié"),
            Groupes = ["Dossier RCS", "Non-inscrit", "Demande"],
            Sections =
            [
                // Identité : les colonnes de la grille de choix du dossier (OPE_SELECT_DOSSIERRCS, proc_vdossier.md:383-387).
                Marche("Identité", "La fiche du dossier : dénomination, numéro de gestion, SIREN, dates d'immatriculation et de radiation, adresse, code NAF, personne morale.", true,
                    "Dénomination", "Numéro de gestion", "SIREN", "Date d'immatriculation", "Date de radiation", "Adresse", "Code NAF", Champ.Case("Personne morale")),
                // Documents : l'arbre de OPE_VGED, pour ce que la V3 en lit (actes, bilans, formalités, inscriptions, bénéficiaires).
                Marche("Documents", "Les documents du dossier, du plus récent au plus ancien : actes, bilans, formalités, inscriptions de privilège, bénéficiaires effectifs déclarés.", true,
                    Champ.Tableau("Actes"), Champ.Tableau("Bilans"), Champ.Tableau("Formalités"), Champ.Tableau("Inscriptions privilèges"), Champ.Tableau("Signalements BE")),
            ],
            Lignes =
            [
                Ligne("Lambda Conseil SARL", "Dossier RCS 2026 B 00601 · immatriculé le 02/03/2026", "Dossier RCS", "AFaire"),
                Ligne("Mu Logistique SAS", "Dossier RCS 2026 B 00602 · immatriculé le 03/03/2026 · sans activité", "Dossier RCS", "EnCours"),
                Ligne("Nu Services SARL", "Dossier RCS 2026 B 00603 · radié le 04/03/2026", "Dossier RCS", "Validee"),
                Ligne("Xi Entrepôts SAS", "Non-inscrit N2026000005", "Non-inscrit", "AFaire"),
                Ligne("Pi Études SARL", "Non-inscrit N2026000006 · radié le 06/03/2026", "Non-inscrit", "Validee"),
                Ligne("Rho Habitat SAS", "Demande 26000000709 · Dossier RCS 2026 B 00609", "Demande", "AFaire"),
                Ligne("Sigma Transports SARL", "Dossier RCS 2026 B 00607 · SIREN 900 300 007", "Dossier RCS", "AFaire"),
                Ligne("Tau Commerce SAS", "Dossier RCS 2026 B 00608 · immatriculé le 12/03/2026", "Dossier RCS", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VJUD", Titre = "Dossier judiciaire", Sujet = "dossiers judiciaires", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurDossierJudiciaire.cs (lot 6). Critères : la recherche d'entité judiciaire
            // (ETP_ENTITE_JUD_RECHERCHE, proc_vjud.md:111-119).
            Recherche = "Rechercher une instance, une procédure collective ou une prévention : numéro ou nom…",
            Criteres = ["Type d'entité", "Numéro", "Nom"],
            // Valider : proc_vjud.md:172 et :176 (bouton MODIFIER ; visible avec POOL_EDIT si le dossier est une entité judiciaire).
            Valider = "Modifier",
            // États : les événements de l'affaire (étapes « Événements survenus / futurs ») -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans événement", "Événement à venir", "Événements survenus"),
            Groupes = ["Instance", "Procédure collective", "Prévention"],
            Sections =
            [
                Marche("Informations", "La fiche de l'entité : numéro, nom de l'affaire, code affaire, dates. Lecture seule.", true,
                    "Numéro", "Nom de l'affaire", "Code affaire", "Date d'ouverture", "Date de l'acte", "Procédure collective rattachée"),
                Marche("Parties", "Les parties de l'instance : leur qualité et leur désignation.", false,
                    Champ.Tableau("Parties de l'affaire")),
                Marche("Audiences et décisions", "Les décisions judiciaires de l'instance, rendues ou prévues.", false,
                    Champ.Tableau("Décisions judiciaires")),
                Marche("Événements", "Les événements survenus, puis les événements futurs.", false,
                    Champ.Tableau("Événements survenus"), Champ.Tableau("Événements futurs")),
            ],
            Lignes =
            [
                Ligne("Rôle 2026J00101 · Omega Conseil SAS c/ Kappa Négoce SARL", "Instance · enrôlée le 02/03/2026 · aucun événement", "Instance", "AFaire"),
                Ligne("Rôle 2026J00102 · Iota Bois SAS c/ Thêta Import SARL", "Instance · 1 événement à venir", "Instance", "EnCours"),
                Ligne("Rôle 2026J00103 · Chi Pêche SARL c/ Psi Paiement SAS", "Instance · 3 événements survenus", "Instance", "Validee"),
                Ligne("PC 2026P00104 · Lambda Conseil SARL", "Procédure collective · ouverte le 05/03/2026 · aucun événement", "Procédure collective", "AFaire"),
                Ligne("PC 2026P00105 · Mu Logistique SAS", "Procédure collective · 2 événements à venir", "Procédure collective", "EnCours"),
                Ligne("Prévention 2026R00106 · Nu Services SARL", "Prévention · 1 événement survenu", "Prévention", "Validee"),
                Ligne("Prévention 2026R00107 · Xi Entrepôts SAS", "Prévention · aucun événement", "Prévention", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REGJUD", Titre = "Registres judiciaires", Sujet = "lignes de registre", Rang = 4,
            Forme = FormeEcran.Etat,
            // Parametres : les criteres de la V2, cherches DANS la base (LecteurRegistresJudiciaires.cs) ; les types d'instance
            // sont ceux que le repertoire general propose (OPE_REGISTRE_GENERAL.cs:41-56), un a la fois en V3.
            Parametres = [Champ.Periode("Dates d'acte"), Champ.Periode("Dates de saisine"), Champ.Periode("Dates d'enrôlement"),
                Champ.Liste("Type d'instance", "J", "F", "R", "JC", "OP", "IP", "LS", "PP", "DD", "MD", "RC", "RA")],
            // Colonnes : les champs de la grille V2. Ils formaient une section « Resultats », retiree : elle repetait le
            // tableau que l'Etat affiche (controle final du lot 1, 25/11/2026). Lecteur : LecteurRegistresJudiciaires.cs (lot 6).
            Colonnes = ["Rôle", Champ.Date("Date d'acte"), "Numéro au répertoire", "Nature", "Chambre", Champ.Date("Date de décision"), "Type de décision", "Ressort"],
            // « Impression » = VALIDER de RIG v2 : une demande d'édition par registre dans la file des éditions
            // (EcrivainRegistresJudiciaires.cs, OPE_REGISTRE_ABSTRACT.cs:113-126) ; « Aperçu » (l'état Crystal) est refusé, dit.
            Gestes = ["Aperçu", "Impression"],
            Recherche = "Chercher dans le registre : rôle ou nom de l'affaire…",
            // Les criteres sont les Parametres, au libelle pres : c'est ce que l'Etat envoie a la base.
            Criteres = ["Dates d'acte", "Dates de saisine", "Dates d'enrôlement", "Type d'instance"],
            // Valider : proc_regjud.md:271 et :347 (bouton VALIDER de la barre : édition du registre, actif s'il y a des lignes).
            Valider = "Valider",
            // États : la décision de la ligne (DECISION_JUDICIAIRE) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans décision", "Décision prévue", "Décision rendue"),
            Groupes = ["Répertoire général", "Injonctions de payer", "Ordonnances du président"],
            Sections =
            [
                Marche("Critères", "Saisir au moins un critère pour compter les lignes : dates d'acte, de saisine ou d'enrôlement, type d'instance.", true,
                    "Dates d'acte", "Dates de saisine", "Dates d'enrôlement", "Type d'instance"),
                Marche("Ventilation", "Ventiler les résultats par type d'instance, avec le nombre d'instances de chaque type.", false,
                    "Type d'instance", "Nombre d'instances"),
                Marche("Sélection", "Cocher les lignes à imprimer seulement, pour ne sortir qu'une partie du registre.", false,
                    "Lignes cochées"),
                Marche("Impression", "Imprimer le registre. Le bouton ne s'active qu'une fois au moins une ligne trouvée.", true,
                    "Aperçu", "Impression"),
            ],
            Lignes =
            [
                Ligne("Rôle 2026J00201 · Pi Études SARL c/ Rho Habitat SAS", "acte du 03/03/2026 · n° 1201 · chambre 1 · sans décision", "Répertoire général", "AFaire"),
                Ligne("Rôle 2026J00202 · Sigma Transports SARL c/ Tau Commerce SAS", "acte du 04/03/2026 · n° 1202 · décision prévue le 30/03/2026", "Répertoire général", "EnCours"),
                Ligne("Rôle 2026J00203 · Upsilon Import SARL c/ Phi Bâtiment SAS", "acte du 05/03/2026 · n° 1203 · décision du 20/03/2026", "Répertoire général", "Validee"),
                Ligne("Rôle 2026IP0301 · Chi Pêche SARL", "minute 301 · décision du 06/03/2026", "Injonctions de payer", "Validee"),
                Ligne("Rôle 2026IP0302 · Psi Paiement SAS", "minute 302 · décision prévue le 09/03/2026", "Injonctions de payer", "EnCours"),
                Ligne("Rôle 2026OP0401 · Omega Conseil SAS", "acte du 10/03/2026 · décision du 10/03/2026", "Ordonnances du président", "Validee"),
                Ligne("Rôle 2026OP0402 · Kappa Négoce SARL", "acte du 11/03/2026 · sans décision", "Ordonnances du président", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHERCHE_BATEAU", Titre = "Navires", Sujet = "navires", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurNavires.cs (lot 6). Critères : OPE_RECHERCHE_BATEAU.Rechercher
            // (proc_recherche_bateau.md:158-198) -- le nom, le numéro d'enregistrement, la case « Afficher l'historique » (oui).
            Recherche = "Rechercher un navire : nom ou numéro d'enregistrement…",
            Criteres = ["Nom du navire", "Numéro d'enregistrement", "Afficher l'historique"],
            // Valider : proc_recherche_bateau.md:64 et :148 (VALIDER toujours visible) ; renvoie la liste à l'écran appelant (:72).
            Valider = "Valider",
            // États : les inscriptions du navire (la colonne NbInscriptions de la V2) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans inscription", "Inscrit sous un ancien nom", "Inscrit"),
            Groupes = ["Nom actuel", "Ancien nom"],
            Sections =
            [
                Marche("Détails", "Le navire : numéro d'enregistrement, nom, nom le plus récent, port, immatriculation, propriétaire et caractéristiques du référentiel bateau.", true,
                    "Numéro d'enregistrement", "Nom du navire", "Nom le plus récent", "Port (quartier maritime)", "Date d'immatriculation",
                    "Lieu d'immatriculation", "Propriétaire", "Numéro OMI", "Longueur", "Jauge", "Nationalité"),
                Marche("Inscriptions", "Les inscriptions du navire, la plus récente en tête (l'info-bulle de la V2).", false,
                    Champ.Tableau("Inscriptions du navire")),
            ],
            Lignes =
            [
                Ligne("Le Pélican Alpha", "Numéro d'enregistrement 0001 · port 01 · aucune inscription", "Nom actuel", "AFaire"),
                Ligne("L'Étoile Bêta", "Numéro d'enregistrement 0002 · port 02 · 2 inscriptions", "Nom actuel", "Validee"),
                Ligne("La Mouette Gamma", "Numéro d'enregistrement 0003 · nom le plus récent : Le Vent d'Ouest · 1 inscription", "Ancien nom", "EnCours"),
                Ligne("Le Phare Delta", "Numéro d'enregistrement 0004 · 1 inscription", "Nom actuel", "Validee"),
                Ligne("Le Pélican Epsilon", "Numéro d'enregistrement 0005 · aucune inscription", "Nom actuel", "AFaire"),
                Ligne("Le Dauphin Zêta", "Numéro d'enregistrement 0006 · nom le plus récent : Le Grand Large · aucune inscription", "Ancien nom", "AFaire"),
                Ligne("Le Hérisson Êta", "Numéro d'enregistrement 0007 · 3 inscriptions", "Nom actuel", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHERCHE_GED", Titre = "Documents", Sujet = "documents", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurDocumentsGed.cs, catégories DocumentsGed.cs (lot 6). Critères : la
            // catégorie de l'arbre (OPE_GED.cs:48-72), le numéro GED (ult_TbxNumGed, :446) et le login d'extraction (:456).
            Recherche = "Rechercher un document : numéro, nom de fichier ou libellé…",
            Criteres = ["Catégorie", "Numéro du document", "Login d'extraction"],
            // Valider : proc_recherche_ged.md:115 (« Archiver » : IMPORT (Archiver) → _Archiver(), section « Actions toolbar »).
            Valider = "Archiver",
            // États : la référence et l'extraction du document (GED_RIG) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Orphelin", "Extrait", "Archivé"),
            Groupes = ["Sans référence", "Éléments de fusion", "Maquettes", "Documents d'édition", "Documents REDA", "Factures", "Documents SIREN", "Orphelins", "Autres documents"],
            Sections =
            [
                Marche("Propriétés", "Le document : numéro, catégorie, libellé, fichier, référence, archivage, taille et pages. Le résultat est limité à 100 lignes dans RIG v2, sans pagination.", true,
                    "Numéro du document", "Catégorie", "Libellé", "Nom du fichier", "Extension", "Référence", "Date d'archivage", "Archivé par", "Taille", "Nombre de pages"),
                Marche("Extraction", "Qui a extrait le document, quand, et dans quel répertoire. Un document extrait est réservé jusqu'à son archivage.", false,
                    "Login d'extraction", "Date d'extraction", "Répertoire d'extraction"),
            ],
            Lignes =
            [
                Ligne("FICHE_CLIENT (RIG)", "Document 0004521 · HTML · archivé le 02/03/2026", "Maquettes", "Validee"),
                Ligne("LETTRE_RAPPEL", "Document 0004522 · DOCX · archivé le 03/03/2026 · extrait par ext2026", "Éléments de fusion", "EnCours"),
                Ligne("Attestation de dépôt", "Document 0004523 · PDF · archivé le 04/03/2026", "Documents d'édition", "Validee"),
                Ligne("Facture 2026-00815", "Document 0004524 · XML · archivé le 05/03/2026", "Factures", "Validee"),
                Ligne("Liaison 26000000525", "Document 0004525 · XML · archivé le 06/03/2026", "Documents SIREN", "Validee"),
                Ligne("piece-0004526.pdf", "Document 0004526 · PDF · référence supprimée (MAQUETTE.MQTT_NUM_DOCUMENT)", "Orphelins", "AFaire"),
                Ligne("import-0004527.zip", "Document 0004527 · ZIP · archivé le 12/03/2026", "Sans référence", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PDF", Titre = "Voir un PDF", Sujet = "pièces PDF", Rang = 4,
            Forme = FormeEcran.Document,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurPiecesGed.cs (lot 6) : les PDF de la GED du greffe (GED_RIG). RIG v2
            // reçoit un chemin de fichier (proc_pdf.md:31-40) ; le document réel d'un greffe est dans sa GED, octets compris.
            Recherche = "Rechercher une pièce à lire…",
            Criteres = ["Fichier PDF unique, ou dossier de PDF", "Pièces non lues affichées en gras", "Alerte à la fermeture si une pièce n'est pas lue"],
            // Valider : proc_pdf.md:110 (« Imprimer tout », impression de masse du mode dossier, :203) ; VALIDER (ligne 106) n'est jamais visible.
            Valider = "Imprimer tout",
            // États : le fichier dans la GED (octets, extraction) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Fichier absent", "Extrait", "Disponible"),
            Groupes = ["PDF archivé"],
            Sections =
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
                Ligne("courrier-2026-0412.pdf", "Document 0004412 · PDF · 4 pages · archivé le 02/03/2026", "PDF archivé", "Validee"),
                Ligne("jugement-2026-0413.pdf", "Document 0004413 · PDF · 12 pages · archivé le 03/03/2026", "PDF archivé", "Validee"),
                Ligne("bilan-2025.pdf", "Document 0004414 · PDF · 3 pages · extrait par ext2026", "PDF archivé", "EnCours"),
                Ligne("statuts-alpha.pdf", "Document 0004415 · PDF · 8 pages · archivé le 05/03/2026", "PDF archivé", "Validee"),
                Ligne("pv-assemblee.pdf", "Document 0004416 · PDF · aucun fichier dans la GED", "PDF archivé", "AFaire"),
                Ligne("pouvoir-2026-0420.pdf", "Document 0004420 · PDF · 2 pages · archivé le 09/03/2026", "PDF archivé", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IMAGE", Titre = "Voir une image", Sujet = "fichiers à afficher", Rang = 4,
            Forme = FormeEcran.Document,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurPiecesGed.cs (lot 6) : les images de la GED du greffe (GED_RIG ; tif,
            // tiff, png, jpg, jpeg, bmp, gif). RIG v2 reçoit un chemin de fichier (proc_image.md:24-28).
            Recherche = "Rechercher un fichier à afficher…",
            Criteres = ["Fichier : chemin absolu d'une image ou d'un PDF (paramètre Filename)", "Titre de la fenêtre, facultatif (paramètre Titre)"],
            // Voir une image n'a pas de VALIDER (proc_image.md:85, :105) : son bouton final est l'action APERCU, à confirmer.
            // La table :59 dit « miniatures » ; le Détail OPE (:21, :158) corrige : ajuster à la fenêtre, sans miniatures.
            // Valider : proc_image.md:158 (« ajuster à la fenêtre », action APERCU ; :156 « réajuste l'image »).
            Valider = "Ajuster à la fenêtre",
            // États : le fichier dans la GED (octets, extraction) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Fichier absent", "Extrait", "Disponible"),
            Groupes = ["TIFF", "Image"],
            Sections =
            [
                Marche("Ouverture", "Charger le fichier passé en paramètre : une image ou un PDF, chemin absolu. Sans fichier, la fenêtre reste vide ; sans titre, elle prend « Aperçu du fichier » et le nom du fichier.", true,
                    "Fichier", "Titre"),
                Marche("Pages", "Visible seulement si le document a plusieurs pages (PDF ou TIFF) : première, précédente, suivante, dernière. Une image à page unique n'affiche aucune navigation.", false,
                    "Pages"),
                Marche("Ajustement", "Le bouton APERCU ajuste l'image à la fenêtre : ce n'est pas une vue de miniatures. Le zoom se fait aussi au clic ou à la molette.", true,
                    "Zoom au clic", "Zoom à la molette"),
            ],
            Lignes =
            [
                Ligne("courrier-scanne-0501.png", "Document 0004501 · PNG · archivé le 02/03/2026", "Image", "Validee"),
                Ligne("plan-situation-0502.jpg", "Document 0004502 · JPG · archivé le 03/03/2026", "Image", "Validee"),
                Ligne("bilan-2025-numerise.tif", "Document 0004503 · TIF · 6 pages · archivé le 04/03/2026", "TIFF", "Validee"),
                Ligne("pv-0504.tiff", "Document 0004504 · TIFF · 3 pages · extrait par ext2026", "TIFF", "EnCours"),
                Ligne("attestation-0505.bmp", "Document 0004505 · BMP · aucun fichier dans la GED", "Image", "AFaire"),
                Ligne("logo-greffe.gif", "Document 0004506 · GIF · archivé le 09/03/2026", "Image", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VCHIFFRES", Titre = "Chiffres d'un dossier", Sujet = "exercices", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurChiffresDossier.cs (lot 6). Critère : le dossier RCS passé à l'écran
            // (idDossier, proc_vchiffres.md:5), retrouvé par son numéro de gestion, son SIREN ou sa dénomination.
            Recherche = "Rechercher un dossier RCS : numéro, SIREN ou dénomination…",
            Criteres = ["Dossier RCS", "Dénomination"],
            // Valider : proc_vchiffres.md:254 (bouton EXPORT « Exporter le contenu des chiffres seuils au format PDF », visible en phase Saisie).
            Valider = "Exporter",
            // États : les chiffres seuils de l'exercice et le rejet de son bilan (proc_vchiffres.md:206) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans chiffres", "Bilan rejeté", "Chiffres connus"),
            Groupes = ["Bilans déposés", "Exercices sans chiffres"],
            Sections =
            [
                Marche("Dossier", "Le dossier RCS de l'exercice.", false,
                    "Dossier RCS", "Dénomination"),
                Marche("Chiffres seuils", "Les chiffres seuils de l'exercice, tirés du bilan déposé. Lecture seule.", true,
                    "Date de clôture", "Durée (mois)", "Monnaie", "Chiffre d'affaires", "Résultat", "Actif", "Capital", "Capitaux propres", Champ.Nombre("Effectif")),
                Marche("Actes de l'exercice", "Les pièces des dépôts de bilan rattachés à l'exercice, et leur motif de rejet.", false,
                    Champ.Tableau("Actes déposés")),
            ],
            Lignes =
            [
                Ligne("Exercice clos le 31/12/2025 · Lambda Conseil SARL", "dossier 2026 B 00601 · CA 120 000,00 € · 4 salariés", "Bilans déposés", "Validee"),
                Ligne("Exercice clos le 31/12/2024 · Lambda Conseil SARL", "dossier 2026 B 00601 · CA 98 000,00 € · bilan rejeté", "Bilans déposés", "EnCours"),
                Ligne("Exercice clos le 31/12/2023 · Lambda Conseil SARL", "dossier 2026 B 00601 · sans chiffres clés", "Exercices sans chiffres", "AFaire"),
                Ligne("Exercice clos le 31/12/2025 · Mu Logistique SAS", "dossier 2026 B 00602 · CA 1 200 000,00 € · 18 salariés", "Bilans déposés", "Validee"),
                Ligne("Exercice clos le 31/12/2024 · Mu Logistique SAS", "dossier 2026 B 00602 · sans chiffres clés", "Exercices sans chiffres", "AFaire"),
                Ligne("Exercice clos le 30/06/2025 · Nu Services SARL", "dossier 2026 B 00603 · CA 45 000,00 € · 1 salarié", "Bilans déposés", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RECHENC", Titre = "Encaissements", Sujet = "encaissements", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurEncaissements.cs (lot 6). Critères : OPE_CRITERES.GetValues (tableau de
            // proc_rechenc.md:278-294) ; les dates se tapent « 31/12/2025 », les montants « 1 200,50 ».
            Recherche = "Rechercher un encaissement : titre, payeur, banque, commentaire, client…",
            Criteres = ["Date de début", "Date de fin", "Numéro de titre", "Mode de paiement", "Banque", "Payeur", "Numéro de remise", "Commentaire",
                        "Abrégé client", "Nom du client", "Montant minimum", "Montant maximum", "Affaire"],
            // Valider : proc_rechenc.md:351 (« EXPORT → export Excel », action nommée « Export Excel » en :209) ; bouton toujours visible (:346).
            Valider = "Export Excel",
            // États : la mise en rouge d'un encaissement non ventilé (OPE_RESULTAT, proc_rechenc.md:327) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans ventilation", "En cours", "Ventilé"),
            Groupes = ["Encaissement ventilé", "Encaissement sans ventilation"],
            Sections =
            [
                Marche("Encaissement", "L'encaissement : titre, date, montant, mode de paiement, banque, payeur, remise, commentaire, agent, comptabilisation.", true,
                    "Numéro de titre", "Date d'encaissement", "Montant", "Mode de paiement", "Banque", "Payeur", "Numéro de remise", "Commentaire",
                    "Saisi par", "Date de comptabilisation"),
                Marche("Ventilations", "Ses ventilations : client, tiers payeur, affaire, montant ventilé. Sans ventilation réelle, la V2 l'affiche en rouge.", true,
                    Champ.Tableau("Ventilations")),
            ],
            Lignes =
            [
                Ligne("Encaissement 2026E0101 · Psi Paiement SAS", "Chèque · 1 200,00 € · le 03/03/2026 · ventilé 1 200,00 €", "Encaissement ventilé", "Validee"),
                Ligne("Encaissement 2026E0102 · Chi Pêche SARL", "Virement · 850,00 € · le 04/03/2026 · sans ventilation", "Encaissement sans ventilation", "AFaire"),
                Ligne("Encaissement 2026E0103 · Sigma Transports SARL", "Prélèvement · 2 400,00 € · le 05/03/2026 · ventilé 2 400,00 €", "Encaissement ventilé", "Validee"),
                Ligne("Encaissement 2026E0104 · Tau Commerce SAS", "Carte bancaire · 310,00 € · le 06/03/2026 · sans ventilation", "Encaissement sans ventilation", "AFaire"),
                Ligne("Encaissement 2026E0105 · Upsilon Import SARL", "Chèque · 95,00 € · le 09/03/2026 · ventilé 95,00 €", "Encaissement ventilé", "Validee"),
                Ligne("Encaissement 2026E0106 · Phi Bâtiment SAS", "Virement · 640,00 € · le 10/03/2026 · sans ventilation", "Encaissement sans ventilation", "AFaire"),
                Ligne("Encaissement 2026E0107 · Rho Habitat SAS", "Prélèvement · 1 050,00 € · le 11/03/2026 · ventilé 1 050,00 €", "Encaissement ventilé", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TARIFS", Titre = "Tarifs", Sujet = "articles tarifaires", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurTarifs.cs (lot 6). Critères : BuildWhereClause
            // (PROC_TARIFS\DataAccess\ArticleSearchQueries.cs:91-134) ; statut et type gardent leur défaut V2 (actif, titre)
            // tant qu'ils ne sont pas saisis, « tous » lève le filtre ; « * » en code événement : tout article qui en a un.
            Recherche = "Filtrer les articles : code, libellé…",
            Criteres = ["Statut", "Type d'article", "Catégorie d'article", "Code événement"],
            // Valider : proc_tarifs.md:186 (« Justifier » du menu contextuel d'une ligne incohérente) ; VALIDER masqué (:215).
            Valider = "Justifier",
            // États : le statut de l'article et la justification de sa famille -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Actif", "En sommeil", "Incohérence justifiée"),
            Groupes = ["Article de base", "Article dérivé"],
            Sections =
            [
                // L'article ouvert (la section s'appelait « Resultats » : elle repetait la liste ; controle final du lot 1).
                Marche("Article", "Code, libellé, montant hors taxe dans la devise du greffe (la somme de ses composants pour un titre), type, statut, catégorie, codes événement.", true,
                    "Code", "Libellé", "Montant HT", "Type", "Statut", "Catégorie", "Date d'application", "Codes événement"),
                Marche("Composition", "Les articles principaux qui composent un titre, avec leur quantité et leur prix.", false,
                    Champ.Tableau("Articles composants")),
                Marche("Cohérence", "La famille de l'article et la date de sa justification. Une justification vaut pour toute la famille de l'article.", false,
                    "Famille", "Date de justification"),
            ],
            Lignes =
            [
                Ligne("Article 0101 · Inscription au greffe", "45,00 € HT · type TI · catégorie Aucune", "Article de base", "AFaire"),
                Ligne("Article 0102 · Modification", "60,00 € HT · type TI · catégorie RCS · incohérence justifiée", "Article de base", "Validee"),
                Ligne("Article 0103 · Extrait", "12,50 € HT · type TI · en sommeil", "Article de base", "EnCours"),
                Ligne("Article 0201 · Copie dérivée", "40,00 € HT · type TI · catégorie RCS", "Article dérivé", "AFaire"),
                Ligne("Article 0202 · Certificat", "15,00 € HT · type TI · incohérence justifiée", "Article dérivé", "Validee"),
                Ligne("Article 0104 · Dépôt d'actes", "25,00 € HT · type TI · codes événement 01, 02", "Article de base", "AFaire"),
                Ligne("Article 0105 · Recherche de pièce", "18,00 € HT · type TI · catégorie Aucune", "Article de base", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_REJETS_BILANS", Titre = "Bilans en rejet", Sujet = "bilans en rejet", Rang = 4,
            Forme = FormeEcran.File,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurBilansRejetes.cs (lot 6) : la requête de OPE_REJETS_BILANS
            // (proc_rejets_bilans.md:80-96) et ses codes de rejet par défaut (ChiffresSeuils.GetCodesMotifsRejet).
            Recherche = "Rechercher un bilan : SIREN, numéro de gestion ou numéro de dépôt…",
            Criteres = ["SIREN, numéro de gestion ou numéro de dépôt", "Codes motifs de rejet (plusieurs possibles)"],
            // Valider : proc_rejets_bilans.md:208 (bouton « Envoyer vers XDCA », actif si la grille a des lignes, :398).
            Valider = "Envoyer vers XDCA",
            // États : une correction XDCA en cours sur l'action du bilan -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Rejeté", "En correction", "Corrigé"),
            // Groupes : ChiffresSeuils.IsRenvoiToInfogreffeAllowed (ChiffresSeuils.cs:226-237).
            Groupes = ["Renvoi possible", "Rejet de fond"],
            Sections =
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
                Ligne("Bilan 2025 · Omega Conseil SAS", "dossier 2026 B 00701 · dépôt 2026-0701 · motif 3 · bilan incomplet", "Renvoi possible", "AFaire"),
                Ligne("Bilan 2025 · Iota Bois SAS", "dossier 2026 B 00702 · dépôt 2026-0702 · motif 7 · correction XDCA en cours", "Renvoi possible", "EnCours"),
                Ligne("Bilan 2024 · Thêta Import SARL", "dossier 2026 B 00703 · dépôt 2026-0703 · motif 10", "Rejet de fond", "AFaire"),
                Ligne("Bilan 2024 · Mu Logistique SAS", "dossier 2026 B 00704 · dépôt 2026-0704 · motif 16 · correction XDCA en cours", "Rejet de fond", "EnCours"),
                Ligne("Bilan 2023 · Nu Services SARL", "dossier 2026 B 00705 · dépôt 2026-0705 · motif C7", "Renvoi possible", "AFaire"),
                Ligne("Bilan 2023 · Xi Entrepôts SAS", "dossier 2026 B 00706 · dépôt 2026-0706 · motif 17", "Rejet de fond", "AFaire"),
                Ligne("Bilan 2025 · Tau Commerce SAS", "dossier 2026 B 00707 · dépôt 2026-0707 · motif E7", "Renvoi possible", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VFNIG", Titre = "Interdits de gerer", Sujet = "personnes", Rang = 4,
            Forme = FormeEcran.Consultation,
            Recherche = "Rechercher une personne : nom, prénom, date de naissance…",
            Criteres = ["Nom", "Prénom", "Date de naissance", "Pays de naissance (France par défaut)", "Commune de naissance (si France)"],
            // Sans lecteur (lot 6, plan-676a4b19) : RIG v2 n'interroge pas la base du greffe mais le service web du FNIG
            // (FnigService.GetPersonneJugementsJud, adresse GRF_URL_FNIG du greffe : proc_vfnig.md:5-15), qu'aucun écran de la V3
            // n'appelle. L'écran garde ses lignes d'exemple, étiquetées comme telles.
            // Valider : proc_vfnig.md:32 et :449 (bouton « Editer les résultats », actif dès qu'une ligne est sélectionnée, :171).
            Valider = "Editer les résultats",
            Etats = EtatsEcran.Trois("À vérifier", "En cours", "Vérifiée"),
            Groupes = ["Personne trouvée", "Personne inexistante"],
            Sections =
            [
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
                Ligne("Personne 0102", "Né(e) en 1965 · 1 jugement actif", "Personne trouvée", "EnCours"),
                Ligne("Personne 0103", "Né(e) en 1981 · jugement ancien", "Personne trouvée", "Validee", 0),
                Ligne("Personne 0104", "Absente du registre · confirmation requise", "Personne inexistante", "EnCours", 1),
                Ligne("Personne 0105", "Né(e) à l'étranger en 1977 · 1 jugement actif", "Personne trouvée", "AFaire"),
                Ligne("Personne 0106", "Né(e) en 1959 · 3 jugements", "Personne trouvée", "Validee", 0, 2),
                Ligne("Personne 0107", "Absente du registre · ajoutée après confirmation", "Personne inexistante", "Validee", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_FIBEN", Titre = "FIBEN", Sujet = "relevés", Rang = 4,
            Forme = FormeEcran.Etat,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurReleveFiben.cs (lot 6) : les volumes transmis par le greffe de la base,
            // un mois (FIBEN) ou un trimestre (BODACC) par ligne (ReleveFIBEN.cs:181-210, ReleveBODACC.cs:244-267). La période de
            // la V2 (un mois ; un trimestre et une année) filtre ici les lignes par leur début.
            Parametres = [Champ.Periode("Période")],
            Colonnes = [Champ.Date("Début de période"), "Relevé", Champ.Nombre("Avis RCS"), Champ.Nombre("Avis PC créés"), Champ.Nombre("Avis PC annulés"),
                        Champ.Nombre("Annonces A"), Champ.Nombre("Annonces B"), Champ.Nombre("Annonces C"), Champ.Nombre("Annonces P")],
            Gestes = ["Générer"],
            Recherche = "Rechercher un relevé : mois ou trimestre…",
            Criteres = ["Mois (transmission FIBEN, mensuelle)", "Trimestre et année (relevé BODACC, trimestriel)", "Tarifs unitaires"],
            // Valider : proc_fiben.md:10-11 et :60-61 (bouton « Générer » de chaque opération).
            Valider = "Générer",
            // États : la période et aujourd'hui -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("À venir", "Période en cours", "Période close"),
            Groupes = ["Transmission FIBEN", "Relevé BODACC"],
            Sections =
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
                Ligne("Transmission FIBEN · janvier 2026", "12 avis RCS · 3 avis PC créés · 0 annulés", "Transmission FIBEN", "Validee"),
                Ligne("Transmission FIBEN · février 2026", "9 avis RCS · 1 avis PC créés · 1 annulés", "Transmission FIBEN", "Validee"),
                Ligne("Transmission FIBEN · mars 2026", "4 avis RCS · 0 avis PC créés · 0 annulés", "Transmission FIBEN", "EnCours"),
                Ligne("Relevé BODACC · 4e trimestre 2025", "A 14 · B 22 · C 5 · P 2", "Relevé BODACC", "Validee"),
                Ligne("Relevé BODACC · 1er trimestre 2026", "A 6 · B 11 · C 2 · P 0", "Relevé BODACC", "EnCours"),
                Ligne("Relevé BODACC · 3e trimestre 2025", "A 10 · B 18 · C 4 · P 1", "Relevé BODACC", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VNOTIF", Titre = "Notifications", Sujet = "notifications", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurNotifications.cs ; recherche et fiche ajoutées au lot 6. Critères :
            // Commun\OPE_DATES, OPE_ETAT, OPE_GREFFES de la V2 (les dates se tapent « 31/12/2025 », l'état par son libellé).
            Recherche = "Rechercher une notification : numéro, dénomination, numéro de gestion…",
            Criteres = ["Date de début", "Date de fin", "État", "Greffe"],
            // Valider : proc_vnotif.md:249 et :262 (barre « Rechercher / Effacer », bouton « Rechercher »).
            Valider = "Rechercher",
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            Groupes = ["Émises (RCS)", "Émises (judiciaire)", "Reçues"],
            Sections =
            [
                Marche("Contenu", "Lire la notification : son greffe, ses dates, le dossier qu'elle vise, son état et le motif d'un rejet.", true,
                    "Identifiant", "Type", "Greffe", "Date d'envoi", "Date de réception", "Dossier", "État", "Motif de rejet"),
            ],
            Lignes =
            [
                Ligne("Notification 2026-0901 · vers un greffe voisin", "Émise le 02/03/2026", "Émises (RCS)", "AFaire"),
                Ligne("Notification 2026-0902", "Émise le 03/03/2026", "Émises (RCS)", "EnCours"),
                Ligne("Notification 2026-0903", "Émise le 04/03/2026", "Émises (judiciaire)", "Validee", 0),
                Ligne("Notification 2026-0904 · d'un greffe voisin", "Reçue le 05/03/2026", "Reçues", "AFaire"),
                Ligne("Notification 2026-0905", "Reçue le 06/03/2026 · avec alerte", "Reçues", "EnCours"),
                Ligne("Notification 2026-0906", "Reçue le 09/03/2026", "Reçues", "Validee", 0),
                Ligne("Notification 2026-0907", "Émise le 10/03/2026", "Émises (judiciaire)", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_VTRID", Titre = "Tribunal digital", Sujet = "échanges avec le tribunal digital", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurTribunalDigital.cs ; recherche et fiche ajoutées au lot 6. Critères :
            // ceux des deux onglets de la V2 (OPE_RECHERCHE_DOCUMENTS_TRANSMIS, OPE_RECHERCHE_DEMANDES_RECUES).
            Recherche = "Rechercher un envoi ou une demande : numéro, rôle, document…",
            Criteres = ["Date de début", "Date de fin", "Numéro de rôle", "Demandes traitées"],
            // Valider : proc_vtrid.md:234 (bouton « Envoyer vers VJUD », actif selon la sélection).
            Valider = "Envoyer vers VJUD",
            Etats = EtatsEcran.Trois("Non traité", "En cours", "Traité"),
            Groupes = ["Documents transmis", "Demandes reçues"],
            Sections =
            [
                Marche("Documents transmis", "Le document envoyé au tribunal digital, avec son numéro de rôle et son compte rendu d'envoi.", true,
                    "Numéro de l'envoi", "Numéro de rôle", "Document", "Destinataire", "Date d'envoi", "Compte rendu d'envoi"),
                Marche("Demandes reçues", "La demande venue du portail, dans l'onglet à part.", false,
                    "Demande", "Numéro de rôle", "Type de formalité", Champ.Date("Reçue le"), "Erreur"),
                Marche("Preuve et dossier", "Télécharger la preuve de dépôt, si le compte rendu est OK, ou ouvrir l'affaire dans le dossier judiciaire.", false,
                    "Preuve de dépôt", "Dossier judiciaire"),
            ],
            Lignes =
            [
                Ligne("Envoi 2026-1001 · Lambda Conseil SARL", "Rôle 2026J00101 · envoyé le 02/03/2026", "Documents transmis", "AFaire"),
                Ligne("Envoi 2026-1002 · Mu Logistique SAS", "Rôle 2026J00102 · envoyé le 03/03/2026", "Documents transmis", "EnCours"),
                Ligne("Envoi 2026-1003 · Nu Services SARL", "Rôle 2026J00103 · compte rendu OK", "Documents transmis", "Validee", 0, 2),
                Ligne("Demande 2026-1004 · Sigma Transports SARL", "Reçue le 04/03/2026 · instance", "Demandes reçues", "AFaire"),
                Ligne("Demande 2026-1005 · Pi Études SARL", "Reçue le 05/03/2026 · procédure collective", "Demandes reçues", "EnCours"),
                Ligne("Demande 2026-1006 · Rho Habitat SAS", "Reçue le 06/03/2026 · prévention", "Demandes reçues", "Validee", 0, 1),
                Ligne("Envoi 2026-1007 · Tau Commerce SAS", "Rôle 2026J00107 · compte rendu en attente", "Documents transmis", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BEN", Titre = "Rechercher un beneficiaire", Sujet = "bénéficiaires effectifs", Rang = 4,
            Forme = FormeEcran.Consultation,
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurRechercheBeneficiaires.cs (lot 6). Critères :
            // OPE_RESULTATS_BEN._BuildRequete (OPE_RESULTATS_BEN.cs:140-163) ; « Dossiers radiés » : oui pour les montrer.
            // Non repris : « Divergences seulement », une case que la V2 ne lit jamais (proc_ben.md:315-317).
            Recherche = "Rechercher un bénéficiaire : nom, prénom, numéro de dossier…",
            Criteres = ["Numéro de dossier", "Nom d'usage", "Nom patronymique", "Prénoms", "Date de naissance", "Dossiers radiés"],
            // Valider : proc_ben.md:321 (bouton de colonne « Signaler », « Visualiser » si une divergence existe).
            Valider = "Signaler",
            // États : le libellé du bouton de la ligne (Search(), OPE_RESULTATS_BEN.cs:198-203) -- règle en tête du lecteur.
            Etats = EtatsEcran.Trois("Sans divergence", "En cours", "Divergence en attente"),
            Groupes = ["Bénéficiaire en vigueur", "Dossier radié"],
            Sections =
            [
                Marche("Bénéficiaire", "Numéro de gestion du dossier, nom, prénoms, dates de naissance et d'effet.", true,
                    "Numéro de gestion", "SIREN", "Nom", "Prénoms", "Date de naissance", "Date d'effet"),
                Marche("Divergence", "Les divergences en attente de la société sur ce bénéficiaire. Signaler une divergence ouvre, dans RIG v2, l'écran de déclaration des divergences.", false,
                    Champ.Tableau("Divergences en attente")),
            ],
            Lignes =
            [
                Ligne("DUPONT Jean", "dossier 2026 B 00601 · né(e) le 01/02/1970", "Bénéficiaire en vigueur", "AFaire"),
                Ligne("MARTIN Anne", "dossier 2026 B 00602 · né(e) le 03/11/1958", "Bénéficiaire en vigueur", "AFaire"),
                Ligne("MARTIN Anne", "dossier 2026 B 00602 · divergence du 05/03/2026 en attente de la société", "Bénéficiaire en vigueur", "Validee"),
                Ligne("ROSSI Luca", "dossier 2026 B 00604 · né(e) le 15/06/1975 · dossier radié", "Dossier radié", "AFaire"),
                Ligne("BERNARD Paul", "dossier 2026 B 00605 · né(e) le 21/09/1963 · dossier radié", "Dossier radié", "AFaire"),
                Ligne("PETIT Claire", "dossier 2026 B 00606 · né(e) le 30/04/1990", "Bénéficiaire en vigueur", "AFaire"),
                Ligne("PETIT Claire", "dossier 2026 B 00606 · divergence du 08/03/2026 en attente de la société", "Bénéficiaire en vigueur", "Validee"),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
