namespace RigV3Desktop.Pont.EcransGeneres;

// fix-ok: libelles de bouton final non cites par leur fiche (grep des 14, mesure : « Enregistrer » absent de PARTIES, MDJUD, XLIEN ; « Voir le recapitulatif » absent de CHORGANESENMASSE ; « Valider » absent de MDIL) : remplaces par le libelle cite, ligne donnee en commentaire ; decision chef 13:18.
// LE VERBE MODIFIER, blocs « L'affaire », « Defaire » et « En masse » du menu (14 ecrans) :
// Instance judiciaire, Procedure collective, Mentions en marge, Diligences RCS, Mots
// directeurs, Repertoire, Parties d'une instance, Beneficiaires d'un dossier, Liens entre
// PC, Supprimer une instance, Liberer une affaire, Changer le juge-commissaire, Changer les
// organes, Debiteurs multiples.
//
// Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\proc_<code>.md).
// Quand la section « Detail OPE » contredit le reste de la fiche, elle fait foi (le code lu
// le confirme) : PROC_MDJUD (une chaine de mots, pas une grille), PROC_XXAFF (le role reel
// est la suppression d'une instance), PROC_LIBAFFAIRE (ni verification ni filtre texte dans
// le code), PROC_CHORGANESENMASSE (un ecran de recherche par juge-commissaire, qui
// n'enregistre aucun changement : la description du menu parle de mandataires, on suit la fiche).
//
// Le rang 2 est la teinte du verbe Modifier (decision du chef) : sa classe rig-tcw__rang-2
// vit dans wwwroot/rig-ecrans-generes.css (lot 9), que tests/EcransGeneresCheck lit aussi.
//
// Les lignes sont une demonstration ANONYME (noms fictifs).
//
// fix-ok: lot 3 du plan « RIG v3 100 % fonctionnel » (26/11/2026), cinq descripteurs alignes sur le code V2 lu, chacun a sa
// ligne citee la ou il change : un champ que la fiche V2 porte et que le descripteur oubliait (numero d'AJ, pays de
// naissance, case Actif), un type mal pose (dates, zones), des groupes sans source (Repertoire).
public sealed class DomaineModifierAffaire : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_MJUD", Titre = "Instance judiciaire", Sujet = "instances judiciaires",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une instance, un numéro de rôle, un nom…",
            Criteres = ["Numéro de rôle", "Nom courant"],
            // Valider : la fiche decrit une etape de validation (l.31 « Afficher post-validation », l.209 « après validation ») sans nommer le bouton ; boutons cites l.298-305 : GED, LANCER REDA, POOL_EDIT, INTERROMPRE. A confirmer.
            Valider = "Valider",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À modifier", "En cours", "Modifiée"), new EtatEcran("EnAttente", "Verrouillée", "en-attente")],
            Groupes = ["Au fond", "En référé"],
            Sections =
            [
                // L'etape « Instance » de MJUD (OPE_INSTANCE, PROC_MJUD.Designer.cs:235-242), ses champs aux titres V2
                // (OPE_INSTANCE.Designer.cs:149-461). Lue et enregistree dans la base (plan « RIG v3 100 % fonctionnel », lot 2) :
                // ce qui se modifie et ses controles, Serveur/Socle/EcransGeneres/EcrivainInstanceJudiciaire.cs.
                Marche("Instance", "Code affaire, nature et acte de saisine ne se modifient pas (zones grisées en modification) ; la saisine du tribunal et la date de l'acte ne dépassent pas l'enrôlement.", false,
                    Champ.Texte("Numéro de rôle"), Champ.Texte("Nom courant"), Champ.Texte("Code affaire"), Champ.Texte("Nature affaire"),
                    Champ.Texte("Acte de saisine"), Champ.Date("Date de l'acte"), Champ.Date("Saisine tribunal"), Champ.Date("Enrôlement"),
                    Champ.Texte("Délivrance de l'acte de saisine"), Champ.Case("IP Européenne"), Champ.Zone("Note sur l'affaire")),
                Marche("Parties", "Demandeurs, défendeurs et intervenants, chacun dans sa liste.", false,
                    Champ.Tableau("Demandeurs"), Champ.Tableau("Défendeurs"), Champ.Tableau("Intervenants")),
                Marche("Appels d'affaire", "Dernier appel passé, prochain appel et liste complète.", false,
                    Champ.Date("Dernier appel"), Champ.Date("Prochain appel"), Champ.Tableau("Liste des appels")),
                Marche("Décision et chef de demande", "Décisions judiciaires rendues, chef de demande et réponse du défendeur.", false,
                    Champ.Tableau("Décisions judiciaires"), "Chef de demande", Champ.Zone("Réponse du défendeur")),
                Marche("Événements", "Événements survenus, puis événements futurs.", false,
                    Champ.Tableau("Événements survenus"), Champ.Tableau("Événements futurs")),
                Marche("Facturation", "Ne facture pas par défaut : la section n'apparaît que si le paramétrage le demande.", false,
                    Champ.Tableau("Articles")),
                Marche("Notes et calendrier", "Note sur l'instance, calendrier de la procédure, coffre-fort et Tribunal digital.", false,
                    "Note sur l'instance", Champ.Tableau("Calendrier de procédure"), Champ.Tableau("Coffre-fort")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00201 · modification de la partie", "Au fond", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00202 · appels à compléter", "Au fond", "EnCours", 0),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00203 · référé en cours", "En référé", "EnCours"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J00204 · référé enregistré", "En référé", "Validee", 1, 2),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J00205 · décision rendue", "Au fond", "Validee", 1, 3, 5),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026J00206 · verrouillée par un autre utilisateur", "Au fond", "EnAttente"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J00207 · référé", "En référé", "AFaire"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026J00208 · événements futurs", "Au fond", "EnCours", 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MPC", Titre = "Procedure collective", Sujet = "procédures collectives et préventions",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une PC, un débiteur, un numéro de prévention…",
            Criteres = ["Numéro de PC ou de prévention", "Instance liée", "Débiteur"],
            // Valider : fiche l.127, « Le bouton VALIDER est surchargé » (DoValider, l.130-136).
            Valider = "Valider",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À modifier", "En cours", "Modifiée"),
                     new EtatEcran("EnAttente", "Verrouillée", "en-attente"),
                     new EtatEcran("Rejetee", "Notification refusée", "rejetee")],
            Groupes = ["Procédure collective (PC)", "Prévention (PRV)"],
            Sections =
            [
                Marche("Entité judiciaire", "Recherche de la PC, ou de l'instance qui y est liée. Verrouillée par un autre utilisateur, l'entité reste en lecture seule.", true,
                    "Numéro de PC", "Instance liée"),
                // La date d'ouverture (ultDateOuverture : OPE_PROCEDURE_COLLECTIVE.cs:196, OPE_PREVENTION.cs:86) et celle de
                // cessation des paiements (ultDateCessationPaiement, OPE_PROCEDURE_COLLECTIVE.cs:202) ont leur champ ; lues et
                // enregistrees sans facture (lot 3) : LecteurProceduresCollectives.cs, EcrivainProceduresCollectives.cs.
                Marche("Procédure", "Données de la PC, ou de la prévention selon le mode d'entrée. Les dates clés se renseignent ici.", true,
                    Champ.Tableau("Dates clés"), Champ.Date("Date d'ouverture"), Champ.Date("Date de cessation des paiements"),
                    Champ.Liste("Mode (PC ou prévention)", "PC", "Prévention")),
                Marche("Organes", "Organes de la PC ou de la prévention, et les organes en fin de mission.", false,
                    Champ.Tableau("Organes de la procédure"), Champ.Tableau("Fin de mission")),
                Marche("Parties et appels", "Parties de l'instance liée, appels d'affaire, décision judiciaire, chef de demande et réponse du défendeur.", false,
                    Champ.Tableau("Parties"), Champ.Tableau("Appels d'affaire"), Champ.Tableau("Décision judiciaire"), "Chef de demande", Champ.Zone("Réponse du défendeur")),
                Marche("Événements", "Événements de l'instance et de la PC ou de la prévention.", false,
                    Champ.Tableau("Événements de l'instance"), Champ.Tableau("Événements de la procédure")),
                Marche("Établissement et chiffres", "Établissement, dirigeants, chiffres économiques et éléments actifs (mode PC seulement).", false,
                    "Établissement", Champ.Tableau("Dirigeants"), Champ.Tableau("Chiffres économiques"), Champ.Tableau("Éléments actifs")),
                Marche("Notification", "Notification dématérialisée : elle s'accepte ou se refuse, sans mise en attente. Refusée, la demande est refusée.", false,
                    Champ.Liste("Notification", "Accepter", "Refuser")),
                Marche("Facturation", "Au forfait, pas de facturation ligne à ligne. Sinon, les articles suivent le paramétrage de la PC.", false,
                    Champ.Tableau("Articles")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "PC 2026P00301 · sauvegarde", "Procédure collective (PC)", "AFaire"),
                Ligne("Bêta Industrie SAS", "PC 2026P00302 · redressement judiciaire", "Procédure collective (PC)", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "PC 2026P00303 · notification à accepter", "Procédure collective (PC)", "EnCours", 0, 1, 2),
                Ligne("Delta Transports SAS", "PC 2026P00304 · liquidation judiciaire", "Procédure collective (PC)", "Validee", 0, 1, 2, 5, 7),
                Ligne("Epsilon Habitat SARL", "Prévention 2026V00305 · saisie du 09/03/2026", "Prévention (PRV)", "AFaire"),
                Ligne("Zêta Commerce SAS", "Prévention 2026V00306 · verrouillée par un autre utilisateur", "Prévention (PRV)", "EnAttente"),
                Ligne("Êta Boulangerie SARL", "PC 2026P00307 · notification refusée le 12/03/2026", "Procédure collective (PC)", "Rejetee"),
                Ligne("Thêta Import SAS", "Prévention 2026V00308 · prévention en cours", "Prévention (PRV)", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MMA", Titre = "Mentions en marge", Sujet = "mentions en marge d'inscriptions",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une inscription, un débiteur, un créancier…",
            Criteres = ["Numéro d'inscription", "Débiteur", "Créancier"],
            // Valider : fiche l.4 (« Validée (acceptation) ») et l.195 (« on ne peut pas valider un MMA sans saisir une mention ») ; Réclamer, Refuser, Attendre : l.22-23 et l.189.
            Valider = "Valider",
            Rang = 2,
            // La fiche range « Reclamee » et « En Attente » parmi les issues ; l'ecran les met
            // toutes deux en « En attente » : figees, sans geste, comme le maintien en attente.
            Etats = [.. EtatsEcran.Trois("À traiter", "En cours", "Validée"),
                     new EtatEcran("EnAttente", "En attente", "en-attente"),
                     new EtatEcran("Rejetee", "Refusée", "rejetee")],
            Groupes = ["Inscriptions maritimes", "Autres inscriptions"],
            Sections =
            [
                Marche("Mention en marge", "Obligatoire pour valider. Si l'inscription n'a pas encore de mention, une mention vierge est proposée pour guider la saisie.", true,
                    Champ.Zone("Mention marginale"), "Date de la mention"),
                Marche("Bateau", "Données du bateau, pour une inscription maritime seulement.", false,
                    Champ.Tableau("Données du bateau")),
                Marche("Débiteurs et biens", "Débiteurs et biens concernés par l'inscription.", false,
                    Champ.Tableau("Débiteurs"), Champ.Tableau("Biens concernés")),
                Marche("Créance et facturation", "Créance ou endettement. Le certificat est facturé automatiquement pour certains types d'inscription ; les frais postaux s'affichent en saisie.", false,
                    "Créance ou endettement", Champ.Tableau("Facturation"), Champ.Montant("Frais postaux")),
                Marche("Décision", "Valider accepte la mention. Réclamer demande une correction au déposant. Refuser rejette la demande. Attendre la met en attente.", true,
                    Champ.Liste("Décision", "Valider", "Réclamer", "Refuser", "Attendre")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Inscription 2026-0401 · modification de mention", "Autres inscriptions", "AFaire"),
                Ligne("Bêta Navigation SAS", "Inscription 2026-0402 · bateau", "Inscriptions maritimes", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "Inscription 2026-0403 · réclamée le 05/03/2026", "Autres inscriptions", "EnAttente"),
                Ligne("Delta Transports SAS", "Inscription 2026-0404 · validée le 06/03/2026", "Autres inscriptions", "Validee", 0, 2, 4),
                Ligne("Epsilon Habitat SARL", "Inscription 2026-0405 · refusée le 09/03/2026", "Autres inscriptions", "Rejetee"),
                Ligne("Zêta Pêche SAS", "Inscription 2026-0406 · bateau de pêche", "Inscriptions maritimes", "AFaire"),
                Ligne("Êta Boulangerie SARL", "Inscription 2026-0407 · en attente de la décision", "Autres inscriptions", "EnCours"),
                Ligne("Thêta Import SAS", "Inscription 2026-0408 · mise en attente le 10/03/2026", "Autres inscriptions", "EnAttente"),
                Ligne("Iota Yachting SAS", "Inscription 2026-0409 · bateau · validée", "Inscriptions maritimes", "Validee", 0, 1, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MDIL", Titre = "Diligences RCS", Sujet = "diligences de dossiers RCS",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un dossier RCS, une diligence, une relance…",
            Criteres = ["Numéro de dossier RCS", "Nature de la diligence", "État de la relance"],
            // Modifier : la fiche ne nomme ni validation ni sauvegarde ; l'action citee est « modifier » (l.1, l.133 « seulement modifier les existantes »). A confirmer.
            Valider = "Modifier",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À modifier", "En cours", "Modifiée"), new EtatEcran("EnAttente", "Sans diligence", "en-attente")],
            Groupes = ["Avec relance", "Sans relance"],
            Sections =
            [
                Marche("Diligence", "Modification seulement : aucune diligence ne se crée. Un dossier sans diligence reste en lecture seule, avec un avertissement.", true,
                    "Origine de l'information", "Nature de la diligence", "Motif de la diligence"),
                Marche("État", "État courant de la diligence et de sa relance.", true,
                    "État de la diligence", "État de la relance"),
                // Lue et enregistree dans la base (plan « RIG v3 100 % fonctionnel », lot 3) : Serveur/Socle/EcransGeneres/
                // LecteurDiligencesRcs.cs et EcrivainDiligencesRcs.cs. Date et zone : les controles de OPE_DILIGENCE_RCS
                // (ultDateRegularisation, ultValeurAttendue texte libre, OPE_DILIGENCE_RCS.Designer.cs:134, :166).
                Marche("Régularisation", "Date prévue ou effective de régularisation, et valeur attendue : montant, document, etc.", false,
                    Champ.Date("Date de régularisation"), Champ.Zone("Valeur attendue")),
                Marche("Paramètres", "Visible seulement si la nature de la diligence a ses propres paramètres.", false,
                    Champ.Tableau("Paramètres de la nature")),
                Marche("Relances", "Chaque diligence peut avoir plusieurs relances, gérées une à une.", false,
                    Champ.Tableau("Relances de la diligence")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Dossier RCS 2026B00701 · demande de pièces · non relancée", "Sans relance", "AFaire"),
                Ligne("Bêta Industrie SAS", "Dossier RCS 2026B00702 · régularisation attendue · relance du 04/03/2026", "Avec relance", "EnCours", 0),
                Ligne("Gamma Conseil SARL", "Dossier RCS 2026B00703 · relance du 05/03/2026", "Avec relance", "EnCours", 0, 1),
                Ligne("Delta Transports SAS", "Dossier RCS 2026B00704 · régularisée le 06/03/2026", "Sans relance", "Validee", 0, 1, 2),
                Ligne("Epsilon Habitat SARL", "Dossier RCS 2026B00705 · aucune diligence", "Sans relance", "EnAttente"),
                Ligne("Zêta Commerce SAS", "Dossier RCS 2026B00706 · relances multiples", "Avec relance", "Validee", 0, 1, 2, 4),
                Ligne("Êta Boulangerie SARL", "Dossier RCS 2026B00707 · paramètres de nature", "Sans relance", "EnCours", 0, 1, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MDJUD", Titre = "Mots directeurs", Sujet = "affaires judiciaires",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une affaire, un numéro de rôle, un mot directeur…",
            Criteres = ["Numéro de rôle, de PC ou de prévention", "Nom courant", "Famille d'affaire"],
            // Valider : la fiche decrit une validation avant sauvegarde (l.4 « valide leur format avant sauvegarde », l.210 « APRES la validation (DoVerifier) ») ; aucun libelle de bouton nomme.
            Valider = "Valider",
            Rang = 2,
            // La fiche decrit une grille de mots ; le Detail OPE fait foi : c'est UNE chaine de
            // mots, normalisee a l'enregistrement. La grille de codes n'est qu'une legende.
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrés"),
            Groupes = ["Instances", "Procédures collectives", "Préventions"],
            Sections =
            [
                Marche("Affaire", "Recherche de l'instance, de la PC ou de la prévention. Le numéro et le nom courant s'affichent en lecture.", true,
                    "Numéro de l'affaire", "Nom courant"),
                // La chaine est une zone de plusieurs lignes (_ultTextMotsDirecteurs, Ult_TextMultiLine, OPE_MD.Designer.cs:29) ;
                // lue et enregistree dans la base (lot 3) : LecteurMotsDirecteurs.cs, EcrivainMotsDirecteurs.cs.
                Marche("Mots directeurs", "Une seule chaîne de mots, séparés par des espaces. Chaque mot commence par un code de deux lettres propre à la famille, et compte au moins trois caractères.", true,
                    Champ.Zone("Mots directeurs")),
                Marche("Contrôle", "Un mot nouveau au préfixe inconnu bloque l'enregistrement. Un mot déjà enregistré au préfixe inconnu ne donne qu'un avertissement.", true,
                    Champ.Tableau("Préfixes reconnus")),
                Marche("Enregistrement", "Espaces réduits ; chaque mot purement alphabétique de plus de deux lettres : deux premières lettres en majuscules, le reste en minuscules.", true,
                    "Chaîne normalisée"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Instance · rôle 2026J00401", "Instances", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Instance · rôle 2026J00402", "Instances", "EnCours", 0),
                Ligne("Epsilon Habitat SARL", "PC 2026P00403", "Procédures collectives", "EnCours", 0, 1),
                Ligne("Zêta Commerce SAS", "PC 2026P00404", "Procédures collectives", "Validee", 0, 1, 2, 3),
                Ligne("Êta Boulangerie SARL", "Prévention 2026V00405", "Préventions", "AFaire"),
                Ligne("Thêta Import SAS", "Prévention 2026V00406", "Préventions", "Validee", 0, 1, 2, 3),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Instance · rôle 2026J00407", "Instances", "Validee", 0, 1, 2, 3),
                Ligne("Lambda Pêche SARL", "PC 2026P00408", "Procédures collectives", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MODIF_REPER", Titre = "Repertoire", Sujet = "répertoriés",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un répertorié, un abrégé, une désignation…",
            Criteres = ["Abrégé", "Désignation", "Répertoriés actifs seulement"],
            // Valider : fiche l.260 (« Yes ⇒ Valider() »), l.287 (cascade transactionnelle), l.322 (civilité des parties cochées) ; le VALIDER global est désarmé (l.244).
            Valider = "Valider",
            Rang = 2,
            Etats = EtatsEcran.Trois("À corriger", "En cours", "Enregistré"),
            // Forme revue au lot 3 (note du chef) : elle reste Fiche -- la V2 modifie UN repertorie dans sa fiche sous la liste
            // (OPE_EDITION_MODIF_REPER, proc_modif_reper.md:238), et « Corriger une affaire judiciaire » est une liste commune
            // de Fiches. Groupes alignes sur la V2 : la liste ne distingue que les actifs (case « actifs uniquement »,
            // OPE_RECHERCHE_MODIF_REPER.cs:278-281) ; « Mandataires / Avocats / Représentants » n'avait pas de source.
            // Lu et enregistre dans la base : LecteurRepertoire.cs, EcrivainRepertoire.cs.
            Groupes = ["Actifs", "Inactifs"],
            Sections =
            [
                // Obligatoires et controle cites par le « Detail OPE » : abrege et civilite (MetierObligatoire), abrege unique
                // (DoVerifier :66-79) ; pas de controle de la designation ni du format de l'adresse mail dans le code.
                Marche("Fiche", "Abrégé et civilité obligatoires ; l'abrégé est unique dans le greffe. Un abrégé changé est reporté sur les parties liées et sur les codes affaire qui le portent.", true,
                    "Abrégé", "Désignation", "Adresse mail", "Civilité", Champ.Case("Actif")),
                Marche("Civilité des parties", "Applique la civilité du répertorié aux parties cochées.", false,
                    Champ.Tableau("Parties liées"), "Civilité"),
            ],
            Lignes =
            [
                Ligne("MAND-ALPHA", "Alpha Mandats SELARL · actif", "Actifs", "AFaire"),
                Ligne("MAND-BETA", "Bêta Administrateurs SARL · actif", "Actifs", "EnCours"),
                Ligne("AVOC-GAMMA", "Cabinet Gamma Avocats · actif", "Actifs", "EnCours", 0),
                Ligne("AVOC-DELTA", "Cabinet Delta Avocats · inactif", "Inactifs", "AFaire"),
                Ligne("REPR-EPSILON", "Représentant Epsilon · actif", "Actifs", "Validee", 0, 1),
                Ligne("REPR-ZETA", "Représentant Zêta · actif", "Actifs", "Validee", 0),
                Ligne("MAND-ETA", "Mandataire Êta · inactif", "Inactifs", "AFaire"),
                Ligne("AVOC-THETA", "Cabinet Thêta Avocats · actif", "Actifs", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PARTIES", Titre = "Parties d'une instance", Sujet = "parties d'instances",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une partie, une instance, un répertorié…",
            Criteres = ["Numéro de rôle de l'instance", "Qualité de la partie", "Aide juridictionnelle"],
            // Valider : fiche l.133 (« Regles de validation (DoVerifier) ») et l.174 (« declencher la sauvegarde de l'instance ») ; aucun libelle de bouton nomme.
            Valider = "Valider",
            Rang = 2,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistrée"),
            Groupes = ["Demandeurs", "Défendeurs", "Intervenants"],
            Sections =
            [
                Marche("Identité", "Nom et adresse de la partie. Une partie vide est créée si la liste est vide : une ligne est toujours à remplir.", true,
                    "Nom", "Adresse"),
                Marche("Mandataire et répertorié", "Mandataire, et répertorié qui le désigne si la partie figure au répertoire.", false,
                    "Mandataire", Champ.Case("Répertorié")),
                // Le numero d'AJ (ultTextNumeroAJ -> PRT_AIDE_JURIDIC_NUM, OPE_PARTIE.cs:390), affiche avec les deux cases quand la
                // case AJ est cochee (:1540-1550). Lu et enregistre dans la base (lot 3) : LecteurPartiesInstance.cs,
                // EcrivainPartiesInstance.cs.
                Marche("Aide juridictionnelle", "Case AJ cochée : il faut choisir Partielle ou Totale, sinon l'enregistrement est refusé.", false,
                    Champ.Case("Aide juridictionnelle"), Champ.Case("Partielle"), Champ.Case("Totale"), Champ.Texte("Numéro d'AJ")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Demandeur · instance 2026J00501", "Demandeurs", "AFaire"),
                Ligne("Bêta Industrie SAS", "Demandeur · instance 2026J00501 · AJ partielle", "Demandeurs", "Validee", 0, 2),
                Ligne("Gamma Conseil SARL", "Demandeur · instance 2026J00502", "Demandeurs", "EnCours", 0),
                Ligne("Delta Transports SAS", "Défendeur · instance 2026J00502", "Défendeurs", "Validee", 0, 1),
                Ligne("Epsilon Habitat SARL", "Défendeur · instance 2026J00503", "Défendeurs", "AFaire"),
                Ligne("Zêta Commerce SAS", "Défendeur · instance 2026J00503 · AJ totale", "Défendeurs", "EnCours", 0, 2),
                Ligne("Êta Boulangerie SARL", "Intervenant · instance 2026J00504", "Intervenants", "EnCours", 0),
                Ligne("Thêta Import SAS", "Intervenant · instance 2026J00504", "Intervenants", "AFaire"),
                Ligne("Iota Études SARL", "Intervenant · instance 2026J00505 · répertorié", "Intervenants", "Validee", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BE", Titre = "Beneficiaires d'un dossier", Sujet = "bénéficiaires effectifs",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un bénéficiaire, un dossier RCS…",
            Criteres = ["Dossier RCS", "Nom du bénéficiaire", "Type de déclaration"],
            // Valider : fiche l.430-431, bouton Valider (masqué en consultation) ; DoSauve l.425.
            Valider = "Valider",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À déclarer", "En cours", "Déclaré"), new EtatEcran("EnAttente", "Consultation seule", "en-attente")],
            Groupes = ["Déclaration standard", "Placement collectif"],
            Sections =
            [
                // Le pays de naissance (ultPaysNaissance, obligatoire, OPE_IDENTITE.Designer.cs:144) : les controles du departement
                // le lisent (OPE_IDENTITE.cs:71-93). Lu et enregistre dans la base (lot 3) : LecteurBeneficiairesDossier.cs,
                // EcrivainBeneficiairesDossier.cs.
                Marche("Identité", "Patronyme, prénoms de l'état civil, date et pays de naissance sont obligatoires ; né en France, le département l'est aussi, et seulement dans ce cas.", true,
                    "Civilité", "Patronyme", "Prénoms (état civil)", "Nom d'usage", Champ.Date("Date de naissance"), "Département de naissance",
                    "Pays de naissance"),
                Marche("Modalités de contrôle", "Les champs changent selon le type de déclaration du dossier : le placement collectif en ajoute sept.", true,
                    Champ.Tableau("Modalités selon la déclaration")),
                Marche("Acte déposé", "Visualise l'acte déposé, et les données brutes renvoyées par le service de déclaration.", false,
                    "Acte déposé", Champ.Tableau("Données de déclaration")),
            ],
            Lignes =
            [
                Ligne("Bénéficiaire Alpha", "Dossier RCS 2026B00801 · déclaration standard", "Déclaration standard", "AFaire"),
                Ligne("Bénéficiaire Bêta", "Dossier RCS 2026B00801 · déclaration standard", "Déclaration standard", "EnCours", 0),
                Ligne("Bénéficiaire Gamma", "Dossier RCS 2026B00801 · déclaration standard", "Déclaration standard", "Validee", 0, 1, 2),
                Ligne("Bénéficiaire Delta", "Placement collectif · acte à visualiser", "Placement collectif", "EnCours", 0, 1),
                Ligne("Bénéficiaire Epsilon", "Placement collectif", "Placement collectif", "Validee", 0, 1),
                Ligne("Bénéficiaire Zêta", "Dossier en consultation seule", "Déclaration standard", "EnAttente"),
                Ligne("Bénéficiaire Êta", "Placement collectif · consultation seule", "Placement collectif", "EnAttente"),
                Ligne("Bénéficiaire Thêta", "Dossier RCS 2026B00801 · déclaration standard", "Déclaration standard", "AFaire"),
            ],
        },

        // Lot 4 du plan « RIG v3 100 % fonctionnel » (26/11/2026) : les sept gestes ponctuels sur les affaires, alignes sur le
        // « Detail OPE » de leur fiche V2 (il fait foi) et branches sur la base (Serveur\Socle\EcransGeneres\Lecteur*.cs et
        // Ecrivain*.cs, cites dans chaque descripteur). Les consignes parlent a l'agent ; les sources sont ici et dans le C#.

        // PROC_XLIEN : deux processus jumeaux (XLIENPC, XLIENPR), une instance a la fois ; le lien est UNE colonne de l'instance
        // (INSTN_ID_PRCLL ou INSTN_ID_PREVE). LecteurLiensPc.cs, EcrivainLiensPc.cs. Une ligne « Rattachée » reste cochable
        // (EnCours) : on y change de PC ou on la detache.
        new DescripteurEcran
        {
            Code = "PROC_XLIEN", Titre = "Liens entre PC", Sujet = "instances à relier",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher une instance, un numéro de rôle, une PC…",
            // FormRechercheInstance.cs:425-426 (role, nom courant) ; le numero de la PC ou de la prevention deja liee.
            Criteres = ["Numéro de rôle", "Nom courant", "Numéro de PC ou de prévention"],
            // Valider : fiche l.93 (« se ferme automatiquement après validation ») ; l.135 : seule l'Instance est sauvée.
            Valider = "Valider",
            Rang = 2,
            // « Enregistrée » (Validee) n'a pas de ligne : une ligne validee se fige, et une instance rattachee doit rester
            // cochable pour changer de PC ou se detacher (« Rattachée », EnCours).
            Etats =
            [
                .. EtatsEcran.Trois("À rattacher", "Rattachée", "Enregistrée"),
                new EtatEcran("EnAttente", "Verrouillée", "en-attente"), new EtatEcran("Rejetee", "Liée à l'autre type", "rejetee"),
            ],
            Groupes = ["Vers une PC", "Vers une prévention"],
            Sections =
            [
                // OPE_XLIENPC_INSTANCE.cs:21-28 (le numero metier designe une seule entite), :122-130 (il remplace le lien).
                Marche("Lien", "Le numéro de la PC (instance « Vers une PC ») ou de la prévention (« Vers une prévention ») à rattacher : il doit désigner une seule entité, qui remplace le lien existant. Seule l'instance est enregistrée, jamais la PC ni la prévention.", false,
                    "Procédure collective", "Prévention"),
                // SupprimerLienPc, OPE_XLIENPC_INSTANCE.cs:140-149 ; exclusivite PC / prevention, PROC_XLIENPC.cs:46-48.
                Marche("Détachement", "Détache l'instance de sa PC ou de sa prévention. Une instance liée à l'autre type se détache dans l'autre groupe.", false,
                    Champ.Case("Détacher le lien")),
                // Le volet d'information OPE_XLIENPC_INFOPC / OPE_XLIENPR_INFOPR.
                Marche("Entité liée", "La PC ou la prévention liée aujourd'hui : celle que le geste remplace ou détache.", false,
                    Champ.Tableau("PC ou prévention liée")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00901 · sans PC", "Vers une PC", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00902 · PC 2026P00902", "Vers une PC", "EnCours"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00903 · sans prévention", "Vers une prévention", "AFaire"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J00904 · prévention 2026V00904", "Vers une prévention", "EnCours"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J00905 · bloquée par une autre demande", "Vers une PC", "EnAttente"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026J00906 · prévention 2026V00906", "Vers une PC", "Rejetee"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J00907 · sans prévention", "Vers une prévention", "AFaire"),
            ],
        },

        // PROC_XXAFF : la suppression PHYSIQUE d'une instance (Instance.CanDeleteInstance puis DeleteCascade). Une instance
        // supprimee sort de la base, donc de la liste. LecteurSuppressionInstance.cs, EcrivainSuppressionInstance.cs.
        new DescripteurEcran
        {
            Code = "PROC_XXAFF", Titre = "Supprimer une instance", Sujet = "instances à supprimer",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher une instance, un numéro d'instance…",
            Criteres = ["Numéro d'instance", "Nom courant", "Date de saisine"],
            // Valider : fiche l.126, OnButtonClick ne traite que VALIDER ; l.155, avertissement « Corriger / Valider ».
            Valider = "Valider",
            Rang = 2,
            // Le Detail OPE fait foi : le role reel est la suppression d'une instance, avec
            // restitution des numeros metiers qu'elle consommait (la fiche disait « saisie rapide »).
            // « En cours » et « Supprimée » n'ont pas de ligne : une instance supprimee sort de la base.
            Etats =
            [
                .. EtatsEcran.Trois("À supprimer", "En cours", "Supprimée"),
                new EtatEcran("EnAttente", "Verrouillée", "en-attente"), new EtatEcran("Rejetee", "Bloquée", "rejetee"),
            ],
            // Le classement de « Instance judiciaire » : JRREF en référé, les autres au fond.
            Groupes = ["Au fond", "En référé"],
            Sections =
            [
                Marche("Résumé", "L'instance choisie, en lecture : rien n'est supprimé avant la validation.", false,
                    Champ.Tableau("Résumé de l'instance")),
                // OPE_INSTANCE_LIBERER_NUM_METIER.cs:44-65 (la liste), :96-98 (l'avertissement), :101-113 (la restitution).
                Marche("Numéros à libérer", "RIG v2 rend les numéros de l'instance quand la case est cochée (elle l'est par défaut). La V3 aussi, et dit ce qu'il advient de chacun. Case décochée, l'instance est supprimée sans rendre ses numéros, après l'avertissement de RIG v2.", false,
                    Champ.Case("Libérer le numéro d'instance"), Champ.Tableau("Numéros de l'instance")),
                // Instance.CanDeleteInstance, Instance.cs:4136-4229.
                Marche("Contrôles", "Ce qui empêche la suppression dans RIG v2 : document en GED, décision judiciaire, dépôt, jonction, facture, provision, mesure, créance PC, numéro non conforme. Une seule raison suffit à la bloquer.", false,
                    Champ.Tableau("Raisons de RIG v2")),
                // Instance.DeleteCascade, Instance.cs:4237-4670 ; SendMail, OPE_INSTANCE_LIBERER_NUM_METIER.cs:112.
                Marche("Suppression", "Ce que la validation efface avec l'instance, table par table, comme RIG v2 : parties, calendrier, appels, décisions, événements, notes, juges… et son dossier judiciaire s'il n'a plus d'autre instance. RIG v2 envoie un courriel à chaque suppression : la V3 ne l'envoie pas.", false,
                    Champ.Tableau("Lignes effacées")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J01001 · saisi le 02/03/2026", "Au fond", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J01002 · 1 raison de RIG v2 l'empêche", "Au fond", "Rejetee"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026R01003 · saisi le 04/03/2026", "En référé", "AFaire"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026J01004 · bloquée par une autre demande", "Au fond", "EnAttente"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026J01005 · 2 raisons de RIG v2 l'empêchent", "Au fond", "Rejetee"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026R01006 · saisi le 06/03/2026", "En référé", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026J01007 · saisi le 09/03/2026", "Au fond", "AFaire"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026J01008 · saisi le 10/03/2026", "Au fond", "AFaire"),
            ],
        },

        // PROC_LIBAFFAIRE : la liste des affaires verrouillees (AFFRE_ID_UTLST_EN_COURS) et TRAITER qui libere les cochees
        // (OPE_LISTAFFAIRE.cs:41-44, :74-86). Ni recherche ni controle dans RIG v2 : les criteres filtrent la liste lue.
        // LecteurAffairesVerrouillees.cs, EcrivainAffairesVerrouillees.cs.
        new DescripteurEcran
        {
            Code = "PROC_LIBAFFAIRE", Titre = "Liberer une affaire", Sujet = "affaires verrouillées",
            Forme = FormeEcran.Operation,
            // La fiche cite un filtre texte que le code n'a pas : la pilule ne filtre ici que la
            // liste affichée, comme sur tous les écrans du plan.
            Recherche = "Rechercher une affaire ou un utilisateur…",
            // Les colonnes de la grille de RIG v2 (RigDataGridViewAffaire.cs:27-37) : numero, utilisateur, demandes.
            Criteres = ["Numéro d'affaire", "Utilisateur", "Demande"],
            // Valider : fiche l.202, bouton générique TRAITER (déclenche la libération ; le libellé « LIBERER » de la fiche n'existe pas).
            Valider = "Traiter",
            Rang = 2,
            // Une affaire liberee sort de la liste (rechargee apres TRAITER, OPE_LISTAFFAIRE.cs:85) : seul « Verrouillée » a des lignes.
            Etats = EtatsEcran.Trois("Verrouillée", "Sélectionnée", "Libérée"),
            Groupes = ["Avec une demande", "Sans demande"],
            Sections =
            [
                Marche("Libération", "Le verrou de l'utilisateur saute tout de suite, sans retour arrière : ses modifications non enregistrées sont perdues. Rien d'autre ne change dans l'affaire.", false),
            ],
            Lignes =
            [
                Ligne("Affaire n° 11101", "Alpha Services SARL · verrouillée par M. Alpha Utilisateur · demandes 26000011101", "Avec une demande", "AFaire"),
                Ligne("Affaire n° 11102", "Bêta Industrie SAS · verrouillée par Mme Bêta Utilisatrice", "Sans demande", "AFaire"),
                Ligne("Affaire n° 11103", "Gamma Conseil SARL · verrouillée par M. Gamma Utilisateur · demandes 26000011103, 26000011113", "Avec une demande", "AFaire"),
                Ligne("Affaire n° 11104", "Delta Transports SAS · verrouillée par M. Delta Utilisateur", "Sans demande", "AFaire"),
                Ligne("Affaire n° 11105", "Epsilon Habitat SARL · verrouillée par Mme Epsilon Utilisatrice · demandes 26000011105", "Avec une demande", "AFaire"),
                Ligne("Affaire n° 11106", "Zêta Commerce SAS · verrouillée par M. Zêta Utilisateur", "Sans demande", "AFaire"),
            ],
        },

        // PROC_CHM (PROC_CHJCM) : la liste et ses controles sont lus (LecteurChangementJuge.cs) ; VALIDER fabrique dans RIG v2
        // une ordonnance numerotee par PC, une facture et un changement en attente : non ecrit par la V3, raison dans le lecteur.
        new DescripteurEcran
        {
            Code = "PROC_CHM", Titre = "Changer le juge-commissaire", Sujet = "procédures collectives",
            Forme = FormeEcran.Operation,
            Recherche = "Rechercher une PC, un juge, un numéro de PC…",
            // OPE_CRITERE (juge, fonction) et OPE_AUTRES_CRITERE (le juge de l'autre fonction), MetierChangementJuge.cs:183-215.
            Criteres = ["Juge à remplacer", "Statut titulaire ou suppléant", "Juge de l'autre fonction"],
            // Valider : fiche l.141, barre Modifier / Annuler / Valider ; l.40, DoSauver.
            Valider = "Valider",
            Rang = 2,
            // ChangementJugeCommissairePc.UpdateErreursMetier (:191-232) : une ligne en erreur ne se coche pas.
            // « En cours » et « Changée » n'ont pas de ligne : le geste n'est pas ecrit par la V3 (LecteurChangementJuge.cs).
            Etats =
            [
                .. EtatsEcran.Trois("À traiter", "En cours", "Changée"),
                new EtatEcran("EnAttente", "Changement en attente", "en-attente"), new EtatEcran("Rejetee", "Bloquée", "rejetee"),
            ],
            Groupes = ["Au forfait", "Hors forfait"],
            Sections =
            [
                Marche("Paramètres", "Lecture seule par défaut. Seuls le code affaire et le mode de facturation sont enregistrés.", true,
                    "Code affaire", "Mode de facturation"),
                Marche("Nouveau juge", "Date de l'acte jamais postérieure à aujourd'hui. Le nouveau juge doit être différent de l'ancien, et pas déjà juge de la PC.", true,
                    "Nouveau juge-commissaire", "Date de l'acte"),
                Marche("Décision", "Types proposés selon le code affaire ; s'il n'y en a qu'un, il est choisi seul.", false,
                    "Type de décision"),
                Marche("Ordonnances et résumé", "Une ordonnance du président par PC, puis un rapport de suivi. Une facture en échec n'arrête pas le traitement.", false,
                    Champ.Tableau("Ordonnances du président"), Champ.Tableau("Rapport de suivi")),
                Marche("Juges et mandataires", "Les juges-commissaires et les mandataires en mission dans la PC.", false,
                    Champ.Tableau("Organes de la PC")),
                Marche("Contrôles", "Ce qui met la PC à part dans RIG v2 : juge à remplacer présent deux fois, PC extérieure, client d'une PC hors forfait inactif, pas en compte ou sans adresse, changement déjà en attente.", false,
                    Champ.Tableau("Erreurs de RIG v2")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "PC 2026P01201 · titulaire Juge A", "Hors forfait", "AFaire"),
                Ligne("Bêta Industrie SAS", "PC 2026P01202 · titulaire Juge A", "Au forfait", "AFaire"),
                Ligne("Gamma Conseil SARL", "PC 2026P01203 · titulaire Juge B · changement de juge déjà en attente", "Hors forfait", "EnAttente"),
                Ligne("Delta Transports SAS", "PC 2026P01204 · titulaire Juge A", "Hors forfait", "AFaire"),
                Ligne("Epsilon Habitat SARL", "PC 2026P01205 · titulaire Juge C", "Au forfait", "AFaire"),
                Ligne("Zêta Commerce SAS", "PC 2026P01206 · Le juge à remplacer est présent plusieurs fois dans la PC", "Hors forfait", "Rejetee"),
                Ligne("Êta Boulangerie SARL", "PC 2026P01207 · La PC est extérieure", "Hors forfait", "Rejetee"),
                Ligne("Thêta Import SAS", "PC 2026P01208 · titulaire Juge B", "Au forfait", "AFaire"),
                Ligne("Iota Études SARL", "PC 2026P01209 · Le client de la PC est en sommeil", "Hors forfait", "Rejetee"),
            ],
        },

        // PROC_CHORGANESENMASSE : le Detail OPE fait foi -- une RECHERCHE de PC par juge-commissaire qui n'enregistre rien
        // (« Changer le juge » n'affiche qu'un recapitulatif, OPE_CHANGEMENT.cs:62 ; VALIDER masque). Forme Consultation (decision
        // du chef : forme a revoir sur le role reel, :98). LecteurOrganesPc.cs ; aucun ecrivain.
        new DescripteurEcran
        {
            Code = "PROC_CHORGANESENMASSE", Titre = "Changer les organes", Sujet = "procédures collectives par juge",
            Forme = FormeEcran.Consultation,
            Recherche = "Rechercher une PC par numéro ou par nom…",
            // OPE_RECHERCHE.cs:26-27 (statut T, comparaison E par defaut), :66-70 (les deux juges).
            Criteres = ["Juge principal", "Statut recherché", "Juge secondaire", "Comparaison"],
            // Le geste de RIG v2 est la recherche (barre interne de l'operation, OPE_RECHERCHE.cs:32-43) : rien d'autre a valider.
            Valider = "Rechercher",
            Rang = 2,
            // La recherche ne rend que des PC non cloturees (« PRCLL_CODE_CLOTURE Is Null », OPE_RECHERCHE.cs:88) : toutes « À
            // examiner » ; « En cours » et « Récapitulée » n'ont pas de ligne (RIG v2 n'enregistre rien).
            Etats = EtatsEcran.Trois("À examiner", "En cours", "Récapitulée"),
            Groupes = ["Juge titulaire", "Juge suppléant"],
            Sections =
            [
                // La grille de RIG v2, OPE_RESULTAT.cs:34-42.
                Marche("Procédure collective", "La PC trouvée : numéro, nom, solution et forfait.", false,
                    "Numéro de PC", "Nom de l'affaire", "Code solution", Champ.Date("Date de solution"), Champ.Case("Forfait facturé")),
                Marche("Organes", "Les juges-commissaires et les mandataires en mission. RIG v2 n'enregistre aucun changement depuis cet écran : « Changer le juge » n'y affiche qu'un récapitulatif.", false,
                    "Juge-commissaire titulaire", "Juge-commissaire suppléant", "Mandataire judiciaire"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "PC 2026P01301 · titulaire Juge A", "Juge titulaire", "AFaire"),
                Ligne("Bêta Industrie SAS", "PC 2026P01302 · titulaire Juge B", "Juge titulaire", "AFaire"),
                Ligne("Gamma Conseil SARL", "PC 2026P01303 · suppléant Juge C", "Juge suppléant", "AFaire"),
                Ligne("Delta Transports SAS", "PC 2026P01304 · suppléant Juge D", "Juge suppléant", "AFaire"),
                Ligne("Epsilon Habitat SARL", "PC 2026P01305 · titulaire Juge A", "Juge titulaire", "AFaire"),
                Ligne("Zêta Commerce SAS", "PC 2026P01306 · titulaire Juge E", "Juge titulaire", "AFaire"),
                Ligne("Êta Boulangerie SARL", "PC 2026P01307 · suppléant Juge B", "Juge suppléant", "AFaire"),
                Ligne("Thêta Import SAS", "PC 2026P01308 · suppléant Juge C", "Juge suppléant", "AFaire"),
            ],
        },

        // PROC_DEBITEURS_MULTIPLES : la grappe des debiteurs d'une PC (le principal, porte par la PC, et les extensions,
        // DEBITEUR_AUTRE). Champs aux titres de OPE_DEBITEUR (OPE_DEBITEUR.Designer.cs:93-194). LecteurDebiteursMultiples.cs,
        // EcrivainDebiteursMultiples.cs.
        new DescripteurEcran
        {
            Code = "PROC_DEBITEURS_MULTIPLES", Titre = "Debiteurs multiples", Sujet = "débiteurs de procédures collectives",
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une procédure, un débiteur…",
            // RIG v2 recoit la PC (IdPC) : les criteres de la recherche de PC de MPC, d'ou il part.
            Criteres = ["Numéro de PC", "Débiteur"],
            // Valider : fiche l.176 (« 9. Validation ») et l.131 (« se ferme après validation »).
            Valider = "Valider",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À compléter", "En cours", "Enregistré"), new EtatEcran("EnAttente", "Principal (lecture)", "en-attente")],
            Groupes = ["Débiteur principal", "Débiteurs additionnels"],
            Sections =
            [
                Marche("Entreprise", "L'entreprise du débiteur : son dossier RCS ou son numéro de non-inscrit. Elle se choisit à la création d'une extension, que cet écran ne fait pas.", false,
                    "Entreprise reliée à la procédure collective"),
                // Obligatoires : OPE_DEBITEUR.Designer.cs:126 (date), :148 (lien) ; valeurs du lien : table LIEN_PRINCIPALE (RIG_DEV).
                Marche("Identité", "Nom et adresse viennent de l'entreprise. Pour une extension, la date d'extension et le lien avec le débiteur principal sont obligatoires.", true,
                    "Nom du débiteur", "Adresse", Champ.Date("Date d'extension"),
                    Champ.Liste("Lien débiteur principal", "article 181", "article 182", "associé de fait", "associé en nom", "associé unipersonnel",
                        "commandité de SCS", "conjoint(e)", "Extension", "filiale", "société du même groupe", "holding", "membre de GIE",
                        "société \"mère\"", "Resolution du plan", "lien sans qualification explicite", "société civile immobilière", "société \"soeur\"")),
                // MAJAffichage, OPE_DEBITEUR.cs:151-153 ; departement obligatoire, :184.
                Marche("Registre des métiers", "Le numéro, le département (obligatoire) et le registre dépendant ne valent que si la case est cochée.", false,
                    Champ.Case("Inscrit au RM"), "N° au registre des métiers", "Département immat. RM", "RM dépendant"),
                // DoPreparationObjetsBeforeEtape, PROC_DEBITEURS_MULTIPLES.cs:65-70.
                Marche("Enregistrement", "À la validation, RIG v2 recalcule le nom de l'affaire, les dirigeants et les établissements de la PC quand un nom, une entreprise ou un débiteur change ; une date, un lien ou le registre des métiers ne les changent pas.", false,
                    "Nom de l'affaire", Champ.Tableau("Débiteurs de la procédure")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "PC 2026P01401 · débiteur principal · 2 extensions", "Débiteur principal", "EnAttente"),
                Ligne("Bêta Industrie SAS", "PC 2026P01401 · Extension · étendue le 03/03/2026", "Débiteurs additionnels", "Validee"),
                Ligne("Gamma Conseil SARL", "PC 2026P01401 · société du même groupe · étendue le 04/03/2026", "Débiteurs additionnels", "Validee"),
                Ligne("Delta Transports SAS", "PC 2026P01402 · débiteur principal · sans extension", "Débiteur principal", "EnAttente"),
                Ligne("Epsilon Habitat SARL", "PC 2026P01403 · débiteur principal · 1 extension", "Débiteur principal", "EnAttente"),
                Ligne("Zêta Commerce SAS", "PC 2026P01403 · société civile immobilière · étendue le 06/03/2026", "Débiteurs additionnels", "Validee"),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
