namespace RigV3Desktop.Pont.EcransGeneres;

// LE VERBE SURVEILLER du menu (lot 5 du plan « les 125 ecrans manquants du menu, au style
// Commandes web »). Chaque descripteur reprend sa fiche RIG v2 (Documentation\reference\proc\
// proc_<code>.md, « Detail OPE » faisant foi) : les etapes deviennent les marches, les regles
// les consignes, les controles de validation les messages. Les lignes sont une demonstration
// ANONYME (noms fictifs). Rang 5 = la teinte du verbe Surveiller (rig-tcw__rang-5).
// Libellé du bouton Valider : un geste cité par la fiche ou le formulaire V2 (ligne en commentaire), jamais inventé.
// fix-ok: PROC_DBEN_ALERT rétabli (décision chef msg-8810ae9e, 14:01 : fiche sans étape mais formulaire présent, fondé sur la source) ; mesuré : 26 descripteurs, 26 codes, Valider cité par la source (OPE_DIVERGENCES.Designer.cs:97) ; libellés Valider cités (msg-ce7b47f9), en-tête « ANONYME » restauré.
//
// Cas a connaitre, dits ici plutot que tus :
//   - PROC_LISTE_DELIBERES n'a pas de fiche : appuye sur PROC_RETAUD (ce qui reste a rendre
//     apres l'audience) et PROC_MPCS (decision DELIBERE). A confirmer par le chef.
//   - PROC_SUIVI_IMPRESSION n'a pas de fiche : appuye sur PROC_PAPERCUT (file d'impression),
//     seule procedure voisine documentee. A confirmer par le chef.
//   - PROC_DBE_RELANCE et PROC_DBE_INJ ont une fiche courte (« source non trouvee ») : leurs
//     etapes sont calquees sur la relance DCA (PROC_DCA_RELANCE). A confirmer.
//   - PROC_DBEN_ALERT : fiche sans étape (lecture seule), fondé sur le formulaire FORM_DBEN_ALERT.cs
//     (VALIDER, VERIFIER, INTERROMPRE masqués, lignes 12-14) : une seule marche, « Visualiser le
//     signalement » (OPE_DIVERGENCES.Designer.cs:97). La tuile « En cours » reste vide : la source
//     n'a pas d'état intermédiaire. A confirmer par le chef.
//   - PROC_STATCA : la fiche decrit un tableau a lignes fixes ; les lignes de demonstration
//     sont des affaires classees par motif de role (J, F, R, IP), comme le fait le calcul.
//   - PROC_SURVBOD : la recherche est un stub dans la fiche ; les criteres affiches sont les
//     colonnes de la grille.
//   - PROC_XPILOTAGE : les lignes sont les cellules pilotees, par feuille (RCS, judiciaire).
//   - PROC_RELANCELOT : le bouton Réinitialiser n'est jamais activable (TODO de la fiche,
//     OPE_EDITGRP.cs:120) : il n'est pas offert ici.
//   - PROC_IPSUIVI : dix etats dans le code (TypeTraitement), cinq ici ; « Suspendue » et
//     « transférée » partagent l'etat figé EnAttente, « Rejetée » est l'etat Rejetee.
public sealed class DomaineSurveiller : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        new DescripteurEcran
        {
            Code = "PROC_COMPO", Titre = "Composition", Sujet = "audiences et cabinets",
            Recherche = "Rechercher une audience, un cabinet, une date…",
            Criteres = ["Audience ou cabinet", "Date de l'audience", "Mode référé"],
            Valider = "Valider", // Bouton VALIDER de la barre d'automate (proc_sdematjud.md, « Etat des boutons ») ; controles : proc_compo.md, ETP_COMPO_AUDIENCE.
            Rang = 5,
            Etats = EtatsEcran.Trois("À composer", "En cours", "Composée"),
            Groupes = ["Audiences", "Cabinets"],
            Marches =
            [
                Marche("Audience", "Une audience ou un cabinet est choisi avant toute saisie ; sans audience, le sélecteur s'ouvre. Une composition liée à des appels d'affaire ne se supprime pas.", true,
                    "Audience ou cabinet", "Date de l'audience"),
                Marche("Juges", "Au moins un juge, 99 au plus. Un même juge ne figure pas deux fois dans une composition.", true,
                    "Juge 1", "Juges suivants"),
                Marche("Greffier", "Un seul greffier, obligatoire.", true, "Greffier"),
                Marche("Parquetier", "Zéro ou un parquetier.", false, "Parquetier"),
                Marche("Invitations", "Un changement de composition envoie les invitations calendrier, si l'envoi est paramétré.", false,
                    "Invitation aux juges", "Invitation au greffier"),
            ],
            Lignes =
            [
                Ligne("Audience du 10/03/2026 · 1re chambre", "Trois juges proposés d'office · greffier à désigner", "Audiences", "AFaire"),
                Ligne("Audience du 10/03/2026 · 2e chambre", "Composition saisie · invitations à envoyer", "Audiences", "EnCours", 0, 1),
                Ligne("Audience du 11/03/2026 · référé", "Composition enregistrée", "Audiences", "Validee", 0, 1, 2),
                Ligne("Cabinet du 12/03/2026 · juge des référés", "Composition à compléter", "Cabinets", "EnCours", 0),
                Ligne("Cabinet du 13/03/2026 · juge commis", "Aucune composition", "Cabinets", "AFaire"),
                Ligne("Audience du 16/03/2026 · 3e chambre", "Composition enregistrée · invitations envoyées", "Audiences", "Validee", 0, 1, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PREAUD", Titre = "Preparation d'audience", Sujet = "plumitifs",
            Recherche = "Rechercher une audience ou un cabinet…",
            Criteres = ["Audience ou cabinet", "Dates de l'audience", "Mode : audience, procédures collectives ou cabinet"],
            Valider = "Générer le plumitif", // proc_preaud.md, Logique métier : « Génération du plumitif (LancerPlumitif) ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À préparer", "En préparation", "Plumitif prêt"),
            Groupes = ["Audiences", "Procédures collectives", "Cabinets"],
            Marches =
            [
                Marche("Audience", "Une ou plusieurs audiences se choisissent. Une audience déjà chargée n'est pas ajoutée une seconde fois.", true,
                    "Audience", "Cabinet"),
                Marche("Ordre des affaires", "Les numéros d'appel se calculent par affaire ; ils se reconstruisent si l'un manque.", true,
                    "Numéro d'appel", "Type de classement"),
                Marche("Options", "Plumitif simplifié, notes d'audience, composition : trois cases à cocher au besoin.", false,
                    "Plumitif simplifié", "Notes de l'audience", "Avec la composition"),
                Marche("Destinataires", "Envoi par courriel ou dépôt dans le coffre. Sans destinataire coché, rien n'est envoyé.", false,
                    "Juges", "Mandataires", "Procureurs et avocats", "Saisie libre"),
                Marche("Génération", "Le plumitif PDF se génère, s'imprime ou s'envoie. Un type de classement inutilisable bloque la génération.", true,
                    "Aperçu", "Impression", "Envoi"),
            ],
            Lignes =
            [
                Ligne("Audience du 09/03/2026 · 1re chambre", "Plumitif généré · 42 affaires", "Audiences", "Validee", 0, 1, 4),
                Ligne("Audience du 10/03/2026 · 2e chambre", "Ordre des affaires à revoir", "Audiences", "EnCours", 0),
                Ligne("Audience du 11/03/2026 · 3e chambre", "Destinataires à choisir", "Audiences", "EnCours", 0, 1),
                Ligne("Audience du 13/03/2026 · procédures collectives", "Ordre des affaires à établir", "Procédures collectives", "AFaire"),
                Ligne("Audience du 16/03/2026 · procédures collectives", "Ordre validé · génération à lancer", "Procédures collectives", "EnCours", 0, 1),
                Ligne("Cabinet du 12/03/2026 · juge des référés", "Plumitif à générer", "Cabinets", "AFaire"),
                Ligne("Cabinet du 13/03/2026 · juge commis", "Envoyé aux juges", "Cabinets", "Validee", 0, 1, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GCONV", Titre = "Convocations", Sujet = "instances à citer",
            Recherche = "Rechercher une audience cabinet, une instance, un huissier…",
            Criteres = ["Audience cabinet", "Instance ou numéro de rôle", "Huissier", "État de la LRAR"],
            Valider = "Citer par huissier", // proc_gconv.md, Rôle : « Gérer les citations et significations par huissier (LRAR) ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À citer", "En cours", "Citée"), new EtatEcran("Rejetee", "NPAI", "rejetee")],
            Groupes = ["Citations à envoyer", "NPAI à reprendre"],
            Marches =
            [
                Marche("Audience cabinet", "Une seule audience cabinet à la fois, choisie par le sélecteur. Une audience déjà chargée est refusée.", true,
                    "Audience cabinet", "Date de l'audience"),
                Marche("Instances", "Une instance est cochée d'office si sa LRAR porte une solution de citation et qu'aucune citation n'existe. Une réception ou un NPAI est grisé.", true,
                    "Case cochée", "Date de LRAR", "Type de date"),
                Marche("Huissier", "Chaque instance cochée a son huissier : sans lui, l'envoi est refusé. Le choix sur la ligne racine remplit les lignes vides.", true,
                    "Huissier"),
                Marche("Copie de l'ordonnance", "Sans copie rattachée à la première décision, un avertissement s'affiche, sans bloquer l'envoi.", false,
                    "Copie conforme de l'ordonnance"),
                Marche("Éditions", "Citation, copie conforme de l'ordonnance et KBIS si coché : une demande d'édition par instance cochée.", true,
                    "Citation", "Copie conforme", "KBIS"),
                Marche("Facturation", "Facturée si la diligence n'est pas tarifée. Une erreur de facturation n'arrête pas l'envoi : l'équipe est prévenue par courriel.", false,
                    "Facture"),
            ],
            Lignes =
            [
                Ligne("Instance 2026J00201 · Alpha Services SARL", "LRAR avec solution de citation · pas encore citée", "Citations à envoyer", "AFaire"),
                Ligne("Instance 2026J00202 · Bêta Industrie SAS", "Huissier choisi · édition à lancer", "Citations à envoyer", "EnCours", 0, 1, 2),
                Ligne("Instance 2026J00203 · Gamma Conseil SARL", "Instance cochée · huissier à choisir", "Citations à envoyer", "EnCours", 0, 1),
                Ligne("Instance 2026J00204 · Delta Transports SAS", "Citation envoyée le 04/03/2026", "Citations à envoyer", "Validee", 0, 1, 2, 4),
                Ligne("Instance 2026J00205 · Epsilon Habitat SARL", "LRAR revenue NPAI le 05/03/2026", "NPAI à reprendre", "Rejetee"),
                Ligne("Instance 2026J00206 · Êta Boulangerie SARL", "Citation à relancer", "Citations à envoyer", "AFaire"),
                Ligne("Instance 2026J00207 · Thêta Import SAS", "LRAR NPAI le 06/03/2026 · à reprendre", "NPAI à reprendre", "Rejetee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MPCS", Titre = "Mise au point chambres", Sujet = "affaires d'audience",
            Recherche = "Rechercher une affaire, un appel, un numéro de rôle…",
            Criteres = ["Appels d'affaires", "Audience cabinet", "Numéro de rôle"],
            Valider = "Valider la décision", // proc_mpcs.md : « ValidationSauver — Clôture et génération finale ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À juger", "En cours", "Décision validée"),
            Groupes = ["Affaires en lot", "Affaire unique"],
            Marches =
            [
                Marche("Affaires", "En lot, les affaires cochées se traitent ensemble. Les instances se verrouillent à l'ouverture et se libèrent à la sortie, même annulée.", true,
                    "Affaires sélectionnées", "Numéro de minute"),
                Marche("Instance", "En affaire unique seulement : données de l'instance.", false,
                    "Numéro de rôle", "Date d'enrôlement"),
                Marche("Parties", "Demandeur, défendeur et intervenants.", false,
                    "Demandeurs", "Défendeurs", "Intervenants"),
                Marche("Décision judiciaire", "Si une décision existe déjà pour l'appel, il faut choisir : la réutiliser, ou en créer une nouvelle.", true,
                    "Décision existante", "Nouvelle décision", "Type de décision"),
                Marche("Événements", "Événements de procédure de l'affaire : survenus, puis à venir.", false,
                    "Événements survenus", "Événements à venir"),
                Marche("Facturation", "Facturation si l'affaire est facturable. En lot, aucune facturation.", false,
                    "Articles"),
                Marche("Prochain appel", "Passage à l'affaire suivante.", false,
                    "Prochain appel"),
            ],
            Lignes =
            [
                Ligne("Affaire 2026J00301 · Alpha Services SARL c/ Bêta Industrie SAS", "Lot · décision réutilisée", "Affaires en lot", "Validee", 0, 3),
                Ligne("Affaire 2026J00302 · Gamma Conseil SARL c/ Delta Transports SAS", "Lot · nouvelle décision à créer", "Affaires en lot", "EnCours", 0),
                Ligne("Affaire 2026J00303 · Epsilon Habitat SARL c/ Zêta Commerce SAS", "Lot · à juger", "Affaires en lot", "AFaire"),
                Ligne("Affaire 2026J00304 · Êta Boulangerie SARL", "Affaire unique · instance ouverte", "Affaire unique", "EnCours", 0, 1),
                Ligne("Affaire 2026J00305 · Thêta Import SAS", "Affaire unique · décision validée", "Affaire unique", "Validee", 0, 1, 3, 4),
                Ligne("Affaire 2026J00306 · Iota Études SARL", "Affaire unique · à juger", "Affaire unique", "AFaire"),
                Ligne("Affaire 2026J00307 · Kappa Négoce SAS", "Affaire unique · décision à saisir", "Affaire unique", "EnCours", 0, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RETAUD", Titre = "Retour d'audience", Sujet = "affaires d'audience",
            Recherche = "Rechercher une affaire, un numéro de rôle, une partie…",
            Criteres = ["Audience ou cabinet", "Affaires à traiter", "Prononcés à traiter", "Publicités en attente"],
            Valider = "Saisir les décisions", // proc_retaud.md, Rôle : « saisir les décisions par affaire ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Résultat saisi"), new EtatEcran("EnAttente", "Publicité en attente", "en-attente")],
            Groupes = ["Affaires à traiter", "Prononcés à traiter", "Publicités en attente"],
            Marches =
            [
                Marche("Audience", "Une audience ou un cabinet se choisit ; la liste de ses affaires s'affiche.", true,
                    "Audience", "Cabinet"),
                Marche("Résultat par affaire", "Le résultat de chaque appel est saisi : décision, prochaine audience, modèle de jugement, minute. Une couleur signale un juge interdit ou un calendrier.", true,
                    "Résultat", "Prochaine audience", "Modèle de jugement", "Numéro de minute"),
                Marche("Décision", "Depuis le menu de la ligne : décision judiciaire, procédure collective, modification des parties.", false,
                    "Décision judiciaire", "Procédure collective", "Modification des parties"),
                Marche("Facturation", "Tarif et solde de l'affaire, depuis la grille.", false,
                    "Tarif", "Solde"),
                Marche("Publicités", "Publicités et impressions légales en attente, à traiter avant de clore l'audience.", false,
                    "Publicité", "Impression légale"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026J00401 · appel n° 1", "Affaires à traiter", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026J00402 · appel n° 2 · résultat commencé", "Affaires à traiter", "EnCours", 0),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026J00403 · appel n° 3 · résultat complet", "Affaires à traiter", "Validee", 0, 1),
                Ligne("Lambda Pêche SARL", "Rôle 2026J00404 · prononcé à saisir", "Prononcés à traiter", "AFaire"),
                Ligne("Thêta Import SAS", "Rôle 2026J00405 · prononcé commencé", "Prononcés à traiter", "EnCours", 0),
                Ligne("Iota Études SARL", "Rôle 2026J00406 · publicité légale en attente", "Publicités en attente", "EnAttente"),
                Ligne("Kappa Négoce SAS", "Rôle 2026J00407 · impression légale en attente", "Publicités en attente", "EnAttente"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_LISTE_DELIBERES", Titre = "Deliberes", Sujet = "délibérés",
            Recherche = "Rechercher un délibéré, une affaire, un numéro de rôle…",
            Criteres = ["Date de l'audience de délibéré", "Chambre ou section", "Numéro de rôle"],
            Valider = "Saisir les décisions", // Pas de fiche : libellé de PROC_RETAUD (proc_retaud.md, Rôle), procédure voisine.
            Rang = 5,
            Etats = EtatsEcran.Trois("À rendre", "En cours", "Rendu"),
            Groupes = ["Délibérés à rendre", "Délibérés depuis plus de deux mois"],
            Marches =
            [
                Marche("Affaire", "L'affaire se retrouve par son numéro de rôle, comme au retour d'audience.", true,
                    "Numéro de rôle", "Parties"),
                Marche("Date du délibéré", "La date de l'audience de délibéré est reprise ; l'écart avec le prononcé se calcule.", true,
                    "Audience de délibéré", "Date de prononcé prévue"),
                Marche("Prononcé", "Le prononcé se saisit comme au retour d'audience : décision, date et minute.", true,
                    "Décision", "Date de prononcé", "Numéro de minute"),
                Marche("Rédaction", "Pour une décision qui se rédige, un brouillon de rédaction est créé.", false,
                    "Modèle de rédaction", "Brouillon"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Délibéré du 02/02/2026 · prononcé à saisir", "Délibérés à rendre", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Délibéré du 05/02/2026 · date de prononcé fixée", "Délibérés à rendre", "EnCours", 0, 1),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Délibéré du 09/02/2026 · prononcé saisi", "Délibérés à rendre", "Validee", 0, 1, 2),
                Ligne("Kappa Négoce SAS", "Délibéré du 20/02/2026 · rédaction à lancer", "Délibérés à rendre", "EnCours", 0, 1, 2),
                Ligne("Êta Boulangerie SARL", "Délibéré du 12/01/2026 · plus de deux mois", "Délibérés depuis plus de deux mois", "AFaire"),
                Ligne("Thêta Import SAS", "Délibéré du 08/01/2026 · plus de deux mois", "Délibérés depuis plus de deux mois", "EnCours", 0),
                Ligne("Iota Études SARL", "Délibéré du 15/01/2026 · rendu", "Délibérés depuis plus de deux mois", "Validee", 0, 1, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TAXESETTOURS", Titre = "Taxes et tours de role", Sujet = "éditions",
            Recherche = "Rechercher une édition, une période…",
            Criteres = ["Date de début", "Date de fin", "Type d'édition : tours, acomptes, taxes"],
            Valider = "Éditer", // proc_taxesettours.md, Détail OPE : « l'unique action est Éditer ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À éditer", "En cours", "Éditée"),
            Groupes = ["Tours de rôle", "Acomptes", "Taxes"],
            Marches =
            [
                Marche("Période", "Date de début et date de fin. Sans date de début, l'édition est refusée. Par défaut : du 1er janvier de l'année en cours à aujourd'hui.", true,
                    "Date de début", "Date de fin"),
                Marche("Type d'édition", "Au moins une case : tours, acomptes ou taxes. Sans case cochée, le bouton Éditer reste désactivé.", true,
                    "Tours", "Acomptes", "Taxes"),
                Marche("Édition", "Un état pour les tours de rôle, un état pour les acomptes et taxes, pour le greffe de la session.", true,
                    "Tours de rôle", "Acomptes et taxes"),
            ],
            Lignes =
            [
                Ligne("Tours de rôle · janvier 2026", "Du 01/01/2026 au 31/01/2026", "Tours de rôle", "Validee", 0, 1, 2),
                Ligne("Tours de rôle · février 2026", "Période à compléter", "Tours de rôle", "AFaire"),
                Ligne("Acomptes · janvier 2026", "Du 01/01/2026 au 31/01/2026 · édition en cours", "Acomptes", "EnCours", 0, 1),
                Ligne("Acomptes · février 2026", "Période à compléter", "Acomptes", "AFaire"),
                Ligne("Taxes · janvier 2026", "Du 01/01/2026 au 31/01/2026", "Taxes", "Validee", 0, 1, 2),
                Ligne("Taxes · février 2026", "Édition en cours", "Taxes", "EnCours", 0),
            ],
        },

        // ---- Relancer
        new DescripteurEcran
        {
            Code = "PROC_DCA_RELANCE", Titre = "Relances comptes annuels", Sujet = "dossiers RCS en retard",
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Exercices non déposés", "Réponse : réclamation, refus, dépôt tardif"],
            Valider = "Valider", // proc_dca_relance.md, Cycle de vie : « Validation -> PreparerDemandes() -> éditions courrier relance ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À relancer", "En cours", "Relancé"),
            Groupes = ["Sans réponse", "Réclamation", "Refus", "Dépôt tardif"],
            Marches =
            [
                Marche("Dossier RCS", "Le dossier se retrouve par son numéro de gestion ou par son SIREN.", true,
                    "Numéro de gestion", "SIREN"),
                Marche("Exercices", "Les exercices non déposés sont proposés, à cocher pour la relance. Un seul exercice de moins de trois ans est coché d'office ; plusieurs : le plus récent de plus de six mois.", true,
                    "Exercice", "Date de clôture", "Déposé ou non"),
                Marche("Facturation", "Si la relance est facturée, les frais de relance s'ajoutent à la facturation.", false,
                    "Articles de relance"),
                Marche("Courrier", "La validation prépare les courriers de relance des exercices cochés, en lettre recommandée ou en démat.", true,
                    "Courrier de relance", "Mode d'envoi"),
                Marche("Réponse", "Réclamation : la saisie s'ouvre. Refus : des frais de refus sont facturés si le paramètre le prévoit. Dépôt tardif : enregistré.", false,
                    "Réclamation", "Refus", "Dépôt tardif"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · SIREN 900 200 001", "Exercice 2024 non déposé · aucune réponse", "Sans réponse", "AFaire"),
                Ligne("Bêta Industrie SAS · SIREN 900 200 002", "Exercice 2024 · relance à préparer", "Sans réponse", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · SIREN 900 200 003", "Relance envoyée le 10/03/2026", "Sans réponse", "Validee", 0, 1, 3),
                Ligne("Delta Transports SAS · SIREN 900 200 004", "Réclamation reçue le 12/03/2026", "Réclamation", "EnCours", 0, 1, 4),
                Ligne("Epsilon Habitat SARL · SIREN 900 200 005", "Refus reçu le 13/03/2026 · frais de refus facturés", "Refus", "Validee", 0, 1, 2, 3, 4),
                Ligne("Zêta Commerce SAS · SIREN 900 200 006", "Dépôt tardif enregistré le 14/03/2026", "Dépôt tardif", "Validee", 0, 1, 3),
                Ligne("Êta Boulangerie SARL · SIREN 900 200 007", "Exercice 2023 · relance à préparer", "Sans réponse", "AFaire"),
                Ligne("Thêta Import SAS · SIREN 900 200 008", "Exercice 2025 · à cocher", "Sans réponse", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DCA_INJ", Titre = "Injonctions comptes annuels", Sujet = "dépôts de comptes annuels",
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Exercices non déposés", "Origine : papier ou dématérialisé"],
            Valider = "Valider le dépôt", // proc_dca_inj.md, Rôle : « valide le dépôt ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À traiter", "En cours", "Déposée"), new EtatEcran("Rejetee", "Refusée", "rejetee")],
            Groupes = ["Dépôts dématérialisés", "Dépôts papier", "Réclamations et refus"],
            Marches =
            [
                Marche("Dossier RCS", "Le dossier se charge en premier, par numéro de gestion ou par SIREN.", true,
                    "Numéro de gestion", "SIREN"),
                Marche("Exercices", "Les exercices non déposés sont proposés ; le dépôt porte sur ceux cochés. Un seul exercice de moins de trois ans est coché d'office.", true,
                    "Exercice non déposé", "Date de clôture", "Date de réception"),
                Marche("Facturation", "Client, encaissement et frais. Les articles changent selon le canal : démat ou papier.", false,
                    "Client", "Encaissement", "Frais"),
                Marche("Réclamation ou refus", "Un motif est saisi pour une réclamation ou un refus.", false,
                    "Motif", "Date de réception"),
                Marche("Validation", "Valider le dépôt : la demande passe en récapitulatif, en lecture seule.", true,
                    "Récapitulatif"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · SIREN 900 210 001", "Dépôt démat reçu · exercice 2025", "Dépôts dématérialisés", "AFaire"),
                Ligne("Bêta Industrie SAS · SIREN 900 210 002", "Dépôt démat · exercice coché", "Dépôts dématérialisés", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · SIREN 900 210 003", "Dépôt démat validé le 11/03/2026", "Dépôts dématérialisés", "Validee", 0, 1, 2, 4),
                Ligne("Delta Transports SAS · SIREN 900 210 004", "Dépôt papier · exercice à cocher", "Dépôts papier", "EnCours", 0),
                Ligne("Epsilon Habitat SARL · SIREN 900 210 005", "Dépôt papier validé le 12/03/2026", "Dépôts papier", "Validee", 0, 1, 4),
                Ligne("Zêta Commerce SAS · SIREN 900 210 006", "Réclamation reçue le 13/03/2026", "Réclamations et refus", "EnCours", 0, 1, 3),
                Ligne("Êta Boulangerie SARL · SIREN 900 210 007", "Refus reçu le 14/03/2026", "Réclamations et refus", "Rejetee"),
                Ligne("Thêta Import SAS · SIREN 900 210 008", "Dépôt démat à valider", "Dépôts dématérialisés", "AFaire"),
            ],
        },

        // DBE : fiche courte (« source non trouvée »). Étapes calquées sur la relance DCA :
        // à confirmer par le chef avant la livraison.
        new DescripteurEcran
        {
            Code = "PROC_DBE_RELANCE", Titre = "Relances beneficiaires", Sujet = "entreprises en retard de déclaration",
            Recherche = "Rechercher une entreprise, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Déclaration DBE (bénéficiaires effectifs) non faite ou incomplète", "Mode d'envoi : LRAR ou notification électronique"],
            Valider = "Générer les courriers de relance", // proc_dbe_relance.md, Contexte : « Il peut générer des courriers de relance LRAR ou des notifications électroniques ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À relancer", "En cours", "Relancée"),
            Groupes = ["DBE non déposée", "DBE incomplète"],
            Marches =
            [
                Marche("Entreprise", "Le dossier RCS se retrouve par son numéro de gestion ou son SIREN.", true,
                    "Numéro de gestion", "SIREN"),
                Marche("Déclaration", "Entreprise sans déclaration DBE, ou dont la déclaration est incomplète.", true,
                    "Déclaration DBE", "Date limite"),
                Marche("Courrier", "Relance par lettre recommandée (LRAR) ou notification électronique.", true,
                    "Mode d'envoi", "Courrier de relance"),
                Marche("Suivi", "Si la relance reste sans effet, l'injonction DBE prend la suite.", false,
                    "Réponse", "Injonction DBE"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · SIREN 900 220 001", "DBE non déposée · relance à envoyer", "DBE non déposée", "AFaire"),
                Ligne("Bêta Industrie SAS · SIREN 900 220 002", "DBE non déposée · courrier préparé", "DBE non déposée", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · SIREN 900 220 003", "Relance LRAR envoyée", "DBE non déposée", "Validee", 0, 1, 2),
                Ligne("Delta Transports SAS · SIREN 900 220 004", "DBE incomplète · relance à envoyer", "DBE incomplète", "AFaire"),
                Ligne("Epsilon Habitat SARL · SIREN 900 220 005", "DBE incomplète · notification envoyée", "DBE incomplète", "Validee", 0, 1, 2, 3),
                Ligne("Zêta Commerce SAS · SIREN 900 220 006", "DBE incomplète · relance en cours", "DBE incomplète", "EnCours", 0),
                Ligne("Êta Boulangerie SARL · SIREN 900 220 007", "DBE non déposée · déjà relancée", "DBE non déposée", "Validee", 0, 1, 2),
            ],
        },

        // DBE : fiche courte (« source non trouvée »). Injonction : à confirmer par le chef.
        new DescripteurEcran
        {
            Code = "PROC_DBE_INJ", Titre = "Injonctions beneficiaires", Sujet = "injonctions DBE",
            Recherche = "Rechercher une entreprise, une relance, un numéro de gestion…",
            Criteres = ["Numéro de gestion ou SIREN", "Relance DBE restée sans effet"],
            Valider = "Générer les injonctions", // proc_dbe_inj.md, Contexte : « le suivi et la génération de ces injonctions ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À enjoindre", "En cours", "Injonction émise"),
            Groupes = ["Relance restée sans effet", "Injonction à suivre"],
            Marches =
            [
                Marche("Entreprise", "Le dossier RCS se retrouve par son numéro de gestion ou son SIREN.", true,
                    "Numéro de gestion", "SIREN"),
                Marche("Relance", "Une relance DBE doit être restée sans effet dans les délais.", true,
                    "Relance DBE", "Date de la relance"),
                Marche("Injonction", "L'injonction de déclarer les bénéficiaires effectifs se prépare, puis se génère.", true,
                    "Texte de l'injonction", "Date d'émission"),
                Marche("Suivi", "Le suivi de l'injonction reste ouvert tant que la déclaration n'est pas faite.", false,
                    "Suivi"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · SIREN 900 230 001", "Relance restée sans effet", "Relance restée sans effet", "AFaire"),
                Ligne("Bêta Industrie SAS · SIREN 900 230 002", "Injonction à générer", "Relance restée sans effet", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · SIREN 900 230 003", "Injonction émise le 09/03/2026", "Injonction à suivre", "Validee", 0, 1, 2),
                Ligne("Delta Transports SAS · SIREN 900 230 004", "Relance à reprendre", "Relance restée sans effet", "AFaire"),
                Ligne("Epsilon Habitat SARL · SIREN 900 230 005", "Injonction en préparation", "Injonction à suivre", "EnCours", 0, 1),
                Ligne("Zêta Commerce SAS · SIREN 900 230 006", "Injonction émise le 12/03/2026", "Injonction à suivre", "Validee", 0, 1, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SRBE_RAPPEL", Titre = "Rappels RBE", Sujet = "dossiers RCS",
            Recherche = "Rechercher un dossier, une date d'immatriculation, un numéro de gestion…",
            Criteres = ["Date d'immatriculation : avant, à partir de, ou intervalle", "Numéros de gestion, de à"],
            Valider = "Éditer les courriers", // proc_srbe_rappel.md, FORM_SRBE_RAPPEL : « AddButton(EDITION) — le bouton ÉDITION (générer les courriers) ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À envoyer", "En cours", "Courrier généré"),
            Groupes = ["Rappel", "Relance 1"],
            Marches =
            [
                Marche("Critères", "Date d'immatriculation (avant, à partir de, ou intervalle) et plage de numéros de gestion. Entrée ou F7 lancent la recherche.", true,
                    "Date d'immatriculation", "Numéros de gestion"),
                Marche("Dossiers", "Les cases « avec activité » et « sans activité » cochent les dossiers en masse. Un dossier provisoirement sans activité est signalé en orange.", true,
                    "Sélection", "Dossier sans activité"),
                Marche("Courrier", "Immatriculé avant le 1er août 2017 : rappel. Sinon : relance 1. Au moins un dossier coché est requis.", true,
                    "Rappel", "Relance 1"),
                Marche("Éditions", "Lettre et liste récapitulative, produites en Word.", false,
                    "Lettre", "Liste récapitulative"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · 2015 B 0001", "Immatriculée en 2015 · rappel", "Rappel", "AFaire"),
                Ligne("Bêta Industrie SAS · 2016 B 0002", "Immatriculée en 2016 · à cocher", "Rappel", "EnCours", 0),
                Ligne("Gamma Conseil SARL · 2016 B 0003", "Rappel généré", "Rappel", "Validee", 0, 1, 2),
                Ligne("Delta Transports SAS · 2018 B 0004", "Immatriculée en 2018 · relance 1", "Relance 1", "AFaire"),
                Ligne("Epsilon Habitat SARL · 2019 B 0005", "Sans activité · relance 1 à cocher", "Relance 1", "EnCours", 0, 1),
                Ligne("Zêta Commerce SAS · 2019 B 0006", "Relance 1 générée", "Relance 1", "Validee", 0, 1, 2),
                Ligne("Êta Boulangerie SARL · 2020 B 0007", "Relance 1 à générer", "Relance 1", "AFaire"),
                Ligne("Thêta Import SAS · 2014 B 0008", "Sans activité · rappel à cocher", "Rappel", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ACTRET", Titre = "Retention d'actes", Sujet = "dépôts",
            Recherche = "Rechercher un dépôt ou un numéro de dossier…",
            Criteres = ["Numéro de dépôt", "Numéro de dossier", "Anomalies sur les pièces"],
            Valider = "Renvoyer à Infogreffe", // proc_actret.md, OPE_RESULTATS, menu contextuel : « Renvoyer à Infogreffe ».
            Rang = 5,
            Etats = EtatsEcran.Trois("En rétention", "Prêt à renvoyer", "Renvoyé"),
            Groupes = ["Comptes annuels", "Pièce à ne pas envoyer"],
            Marches =
            [
                Marche("Recherche", "Numéro de dépôt, numéro de dossier, anomalies sur les pièces. Entrée, F5 ou F7 lancent la recherche.", true,
                    "Numéro de dépôt", "Numéro de dossier", "Anomalies"),
                Marche("Pièces", "Bilan (BILA), déclaration de confidentialité (CONFI), compte de résultat (RESUL) : un dépôt est complet quand ses pièces attendues sont numérisées.", true,
                    "Bilan", "Déclaration de confidentialité", "Compte de résultat"),
                Marche("Renvoi", "Renvoi à Infogreffe depuis la grille, pour les actes sélectionnés. L'acte quitte la rétention.", true,
                    "Actes à renvoyer", "Confirmation"),
                Marche("Consultation", "Voir le dépôt ou le dossier dans la GED depuis le menu de la ligne.", false,
                    "Dépôt", "Dossier"),
            ],
            Lignes =
            [
                Ligne("Dépôt 2026-0101 · Alpha Services SARL", "Bilan non numérisé · en rétention", "Comptes annuels", "AFaire"),
                Ligne("Dépôt 2026-0102 · Bêta Industrie SAS", "Bilan numérisé · compte de résultat à compléter", "Comptes annuels", "EnCours", 0, 1),
                Ligne("Dépôt 2026-0103 · Gamma Conseil SARL", "Pièces complètes · renvoi à faire", "Comptes annuels", "EnCours", 0, 1),
                Ligne("Dépôt 2026-0104 · Delta Transports SAS", "Renvoyé le 10/03/2026", "Comptes annuels", "Validee", 0, 1, 2),
                Ligne("Dépôt 2026-0105 · Epsilon Habitat SARL", "Pièce marquée « à ne pas envoyer »", "Pièce à ne pas envoyer", "AFaire"),
                Ligne("Dépôt 2026-0106 · Zêta Commerce SAS", "Pièce absente · en rétention", "Comptes annuels", "AFaire"),
                Ligne("Dépôt 2026-0107 · Êta Boulangerie SARL", "Renvoyé le 11/03/2026", "Comptes annuels", "Validee", 0, 1, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ACTREJ", Titre = "Actes rejetes", Sujet = "actes rejetés",
            Recherche = "Rechercher un acte rejeté, un dépôt, un numéro de dossier…",
            Criteres = ["Numéro de dossier", "Numéro de dépôt", "Plage de dates et type de date", "Nombre de résultats (200 par défaut)"],
            Valider = "Renvoyer à Infogreffe", // proc_actrej.md, OPE_RESULTATS, menu contextuel : « Renvoyer à Infogreffe ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("Rejeté", "Renvoi lancé", "Renvoyé"), new EtatEcran("Rejetee", "Annulé (Amitel)", "rejetee")],
            Groupes = ["Rejets Infogreffe"],
            Marches =
            [
                Marche("Recherche", "Les rejets se filtrent par numéro de dossier, numéro de dépôt, plage de dates et type de date. À l'ouverture, la liste se remplit seule sur les critères par défaut (200 résultats).", true,
                    "Numéro de dossier", "Numéro de dépôt", "Date du", "Date au", "Type de date"),
                Marche("Résultats", "Grille des rejets. Le menu de la ligne renvoie l'acte à Infogreffe, copie la ligne ou l'ouvre dans la GED.", true,
                    "Renvoyer à Infogreffe", "Copier", "Voir dans la GED"),
                Marche("Renvoi", "Le renvoi remet l'acte en file d'envoi : il quitte la liste au prochain rafraîchissement.", true,
                    "Acte renvoyé", "Confirmation"),
                Marche("Annulation", "Annuler un rejet est réservé à l'équipe Amitel.", false,
                    "Annulation du rejet"),
            ],
            Lignes =
            [
                Ligne("Acte 2026-0501 · Alpha Services SARL", "Rejet du 02/03/2026 · à renvoyer", "Rejets Infogreffe", "AFaire"),
                Ligne("Acte 2026-0502 · Bêta Industrie SAS", "Rejet du 03/03/2026 · renvoi lancé", "Rejets Infogreffe", "EnCours", 0),
                Ligne("Acte 2026-0503 · Gamma Conseil SARL", "Rejet du 04/03/2026 · recherche faite", "Rejets Infogreffe", "EnCours", 0, 1),
                Ligne("Acte 2026-0504 · Delta Transports SAS", "Renvoyé le 05/03/2026", "Rejets Infogreffe", "Validee", 0, 1, 2),
                Ligne("Acte 2026-0505 · Epsilon Habitat SARL", "Rejet du 06/03/2026 · à renvoyer", "Rejets Infogreffe", "AFaire"),
                Ligne("Acte 2026-0506 · Zêta Commerce SAS", "Renvoyé le 09/03/2026", "Rejets Infogreffe", "Validee", 0, 1, 2),
                Ligne("Acte 2026-0507 · Êta Boulangerie SARL", "Rejet annulé par l'équipe Amitel", "Rejets Infogreffe", "Rejetee"),
                Ligne("Acte 2026-0508 · Thêta Import SAS", "Rejet du 10/03/2026 · à renvoyer", "Rejets Infogreffe", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            // Fondé sur la source (formulaire FORM_DBEN_ALERT.cs) : la fiche proc_dben_alert.md n'a
            // pas d'étape, elle est en lecture seule. Les lignes sont une démonstration anonyme.
            Code = "PROC_DBEN_ALERT", Titre = "Divergences BEN", Sujet = "divergences BEN ouvertes",
            Recherche = "Rechercher un dossier, un demandeur…",
            Criteres = ["Numéro de dossier", "Demandeur", "Date de début", "Date de fin", "Exclure les dossiers radiés"],
            Valider = "Visualiser le signalement", // OPE_DIVERGENCES.Designer.cs:97 ; même action au double-clic (OPE_DIVERGENCES.cs:115-117). FORM_DBEN_ALERT.cs:13 masque VALIDER.
            Rang = 5,
            Etats = EtatsEcran.Trois("À visualiser", "En cours", "Visualisée"),
            Groupes = ["Dossiers actifs", "Dossiers radiés"],
            Marches =
            [
                Marche("Signalement", "Visualiser le signalement ouvre la divergence dans l'écran de dépôt des bénéficiaires effectifs. Ce tableau de bord ne modifie rien : il est en lecture seule.", true,
                    "Numéro de gestion", "Demandeur", "Détail des divergences"),
            ],
            Lignes =
            [
                Ligne("Dossier 2026-0412 · divergence du 02/03/2026", "Demandeur A · 2 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2026-0415 · divergence du 03/03/2026", "Demandeur B · 1 libellé de divergence", "Dossiers actifs", "Validee", 0),
                Ligne("Dossier 2026-0419 · divergence du 05/03/2026", "Demandeur C · 3 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2026-0422 · divergence du 06/03/2026", "Demandeur A · 1 libellé de divergence", "Dossiers actifs", "Validee", 0),
                Ligne("Dossier 2026-0430 · divergence du 09/03/2026", "Demandeur D · 2 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2025-1187 · divergence du 12/11/2025", "Demandeur E · 1 libellé de divergence", "Dossiers radiés", "AFaire"),
                Ligne("Dossier 2025-1190 · divergence du 14/11/2025", "Demandeur F · 2 libellés de divergence", "Dossiers radiés", "Validee", 0),
                Ligne("Dossier 2025-1203 · divergence du 20/11/2025", "Demandeur B · 1 libellé de divergence", "Dossiers radiés", "Validee", 0),
            ],
        },

        // ---- Les instances
        new DescripteurEcran
        {
            Code = "PROC_SURVIP", Titre = "Injonctions de payer", Sujet = "injonctions de payer",
            Recherche = "Rechercher une injonction, un numéro de rôle, un demandeur…",
            Criteres = ["Juge", "Greffier", "Titre exécutoire", "Non avenue", "Caducité", "Certificat de non-opposition"],
            Valider = "Appliquer les traitements", // proc_survip.md, Rôle : « Appliquer les traitements : Titre exécutoire, Caducité, IP Non avenue, Certificat de non-opposition ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            Groupes = ["Titre exécutoire", "Caducité", "Non avenue", "Certificat de non-opposition"],
            Marches =
            [
                Marche("Recherche", "Au moins un type de traitement coché. Seules les injonctions non européennes sont retenues. Pour le titre exécutoire, seules les décisions antérieures au 1er mars 2022 le sont.", true,
                    "Juge", "Greffier", "Types de traitement"),
                Marche("Délai", "Le délai d'opposition ou de consignation est dépassé. Un délai supplémentaire peut être ajouté, type par type.", false,
                    "Délai supplémentaire", "Date prévue"),
                Marche("Traitement", "Le traitement attribue les numéros d'inscription, de répertoire et de dossier judiciaire.", true,
                    "Numéro d'inscription", "Numéro de répertoire", "Numéro de dossier judiciaire"),
                Marche("Éditions", "Les éditions de l'injonction ou du titre sont produites pour chaque traitement.", false,
                    "Éditions", "Ordonnance numérisée"),
                Marche("Suivi", "Une injonction à suivre s'ouvre dans le suivi des préventions, en fenêtre modale.", false,
                    "Suivi"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Titre exécutoire · opposition échue le 01/03/2026", "Titre exécutoire", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Titre exécutoire · numéros attribués", "Titre exécutoire", "EnCours", 0, 2),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Titre exécutoire · traité", "Titre exécutoire", "Validee", 0, 2, 3),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Caducité · consignation échue", "Caducité", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Non avenue · signification échue", "Non avenue", "EnCours", 0),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Certificat de non-opposition · échéance passée", "Certificat de non-opposition", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Caducité · traitée", "Caducité", "Validee", 0, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IPSUIVI", Titre = "Suivi des preventions", Sujet = "instances de prévention",
            Recherche = "Rechercher une prévention, un débiteur…",
            Criteres = ["Débiteur", "Numéro de gestion", "État de traitement"],
            Valider = "Clôturer", // proc_ipsuivi.md, ConfigurerBoutonsTransition : « [Cloturer] -> Cloture ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("Non traitée", "En suivi", "Clôturée"),
                     new EtatEcran("Rejetee", "Rejetée", "rejetee"),
                     new EtatEcran("EnAttente", "Suspendue ou transférée", "en-attente")],
            Groupes = ["À prendre en charge", "En suivi", "Suspendues ou transférées", "Clôturées ou rejetées"],
            Marches =
            [
                Marche("Prise en charge", "Une prévention non traitée passe en suivi. Un rejet se fait par « Rejeter », avec un commentaire obligatoire.", true,
                    "Prise en charge", "Commentaire"),
                Marche("Convocation et contact", "Le débiteur est convoqué, puis le contact est confirmé.", false,
                    "Convocation", "Contact confirmé"),
                Marche("Mandat ou régularisation", "Un mandataire est désigné, ou la situation est régularisée.", true,
                    "Mandataire", "Régularisation"),
                Marche("Clôture", "La clôture est un état final : plus aucune transition après.", true,
                    "Clôture"),
                Marche("Historique", "Chaque changement d'état est gardé dans l'historique, qui ne se modifie pas.", false,
                    "Historique"),
            ],
            Lignes =
            [
                Ligne("Débiteur 2026 P 0101 · Alpha Services SARL", "Prévention non traitée", "À prendre en charge", "AFaire"),
                Ligne("Débiteur 2026 P 0102 · Bêta Industrie SAS", "Convoquée le 05/03/2026", "En suivi", "EnCours", 0, 1),
                Ligne("Débiteur 2026 P 0103 · Gamma Conseil SARL", "Contactée · mandataire à désigner", "En suivi", "EnCours", 0, 1),
                Ligne("Débiteur 2026 P 0104 · Delta Transports SAS", "Mandataire désigné", "En suivi", "EnCours", 0, 1, 2),
                Ligne("Débiteur 2026 P 0105 · Epsilon Habitat SARL", "Suspendue le 03/03/2026", "Suspendues ou transférées", "EnAttente", 0),
                Ligne("Débiteur 2026 P 0106 · Zêta Commerce SAS", "Clôturée le 09/03/2026", "Clôturées ou rejetées", "Validee", 0, 2, 3),
                Ligne("Débiteur 2026 P 0107 · Êta Boulangerie SARL", "Rejetée sans suite", "Clôturées ou rejetées", "Rejetee"),
                Ligne("Débiteur 2026 P 0108 · Thêta Import SAS", "Transférée à un autre greffe", "Suspendues ou transférées", "EnAttente"),
                Ligne("Débiteur 2026 P 0109 · Iota Études SARL", "Nouvelle prévention reçue", "À prendre en charge", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SURVMES", Titre = "Mesures", Sujet = "mesures",
            Recherche = "Rechercher une mesure, un type d'événement, un numéro de dossier…",
            Criteres = ["Type d'affaire", "État de la mesure", "Type d'événement de procédure"],
            Valider = "Éditer les documents Word", // proc_survmes.md, Rôle : « Éditer des documents Word (AmiOffice2010) sur les mesures cochées ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À traiter", "En cours", "Traitée"), new EtatEcran("Rejetee", "Code expiré", "rejetee")],
            Groupes = ["Mesures à traiter", "Codes expirés"],
            Marches =
            [
                Marche("Recherche", "Les mesures se cherchent selon les critères. Une mesure dont le code d'événement est expiré est décochée, grisée et en rouge.", true,
                    "Type d'affaire", "État"),
                Marche("Événements de procédure", "Cocher un code d'événement coche toutes les mesures correspondantes.", false,
                    "Code d'événement"),
                Marche("Cochage", "Cocher une mesure la recharge, pose son verrou métier et met à jour le compteur « cochés ».", true,
                    "Mesure cochée", "Compteur"),
                Marche("Éditions Word", "Documents Word édités sur les mesures cochées. Quitter le tableau de vérification annule les demandes.", true,
                    "Document Word", "Annulation"),
            ],
            Lignes =
            [
                Ligne("Mesure 2026 M 0101 · Alpha Services SARL", "Événement à venir · à cocher", "Mesures à traiter", "AFaire"),
                Ligne("Mesure 2026 M 0102 · Bêta Industrie SAS", "Mesure cochée · compteur à jour", "Mesures à traiter", "EnCours", 0, 2),
                Ligne("Mesure 2026 M 0103 · Gamma Conseil SARL", "Éditée en Word", "Mesures à traiter", "Validee", 0, 2, 3),
                Ligne("Mesure 2026 M 0104 · Delta Transports SAS", "Code d'événement à cocher", "Mesures à traiter", "EnCours", 0, 1),
                Ligne("Mesure 2026 M 0105 · Epsilon Habitat SARL", "Code d'événement expiré", "Codes expirés", "Rejetee"),
                Ligne("Mesure 2026 M 0106 · Zêta Commerce SAS", "Code d'événement expiré", "Codes expirés", "Rejetee"),
                Ligne("Mesure 2026 M 0107 · Êta Boulangerie SARL", "Mesure à cocher", "Mesures à traiter", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTLOT", Titre = "Lots", Sujet = "lots",
            Recherche = "Rechercher un lot par son nom…",
            Criteres = ["Nom du lot", "Lot actif"],
            Valider = "Valider le lot", // proc_gestlot.md, OPE_AJOUT : « _valider() », séquence transactionnelle.
            Rang = 5,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Lots actifs", "Lots inactifs"],
            Marches =
            [
                Marche("Nom", "Nom obligatoire et unique : un nom déjà pris est refusé. Renommer reporte le nouveau nom sur les groupes d'impression.", true,
                    "Nom du lot"),
                Marche("Imprimante", "Imprimante du lot. Si elle change, le groupe d'impression en cours peut reprendre cette imprimante.", false,
                    "Imprimante", "Impression supplémentaire"),
                Marche("Impression", "Impression automatique à l'heure choisie, et tri automatique. Un seul lot peut être direct.", false,
                    "Impression automatique", "Heure d'impression", "Tri automatique"),
                Marche("Activation", "Un lot créé est actif d'office. Un lot direct est unique : l'activer désactive l'autre lot direct.", true,
                    "Lot actif", "Lot direct"),
                Marche("Enregistrement", "Enregistrer écrit le lot ; une création ou un renommage actualise la liste.", true,
                    "Enregistrer"),
            ],
            Lignes =
            [
                Ligne("Lot relances mars", "Nouveau lot · nom à saisir", "Lots actifs", "AFaire"),
                Ligne("Lot édition audiences", "Imprimante choisie · impression auto", "Lots actifs", "EnCours", 0, 1),
                Ligne("Lot impression directe", "Lot direct · enregistré", "Lots actifs", "Validee", 0, 3, 4),
                Ligne("Lot relances DBE", "Nom saisi · activation à confirmer", "Lots actifs", "EnCours", 0),
                Ligne("Lot archives 2025", "Lot inactif · enregistré", "Lots inactifs", "Validee", 0, 3, 4),
                Ligne("Lot tri mensuel", "Nouveau lot · impression auto à régler", "Lots actifs", "AFaire"),
                Ligne("Lot relances LRAR", "Lot actif · tri auto", "Lots actifs", "EnCours", 0, 3),
            ],
        },

        // ---- Les envois et les éditions
        new DescripteurEcran
        {
            Code = "PROC_SURVBOD", Titre = "Surveillance BODACC", Sujet = "annonces",
            Recherche = "Rechercher une annonce, un numéro de gestion…",
            // La fiche décrit la recherche comme un stub : les critères affichés sont les colonnes de la grille.
            Criteres = ["Numéro de gestion RCS", "Type d'annonce (A, B ou C)", "Date de publication prévue"],
            Valider = "Modifier l'annonce", // proc_survbod.md, Colonnes / Boutons : « MODIFIER (XACTION) » lance le processus XACTION pour l'annonce sélectionnée.
            Rang = 5,
            Etats = EtatsEcran.Trois("À corriger", "En cours", "Corrigée"),
            Groupes = ["Annonces A", "Annonces B", "Annonces C"],
            Marches =
            [
                Marche("Rejet", "Les rejets de publication s'affichent par numéro de gestion RCS et numéro chronologique du dépôt.", true,
                    "Numéro de gestion", "Numéro chronologique du dépôt"),
                Marche("Correction", "« Modifier » lance la correction de l'annonce sélectionnée. Une annonce non inscrite ne se sélectionne pas.", true,
                    "Annonce", "Action corrective liée"),
                Marche("Envoi", "Export Word des rejets sélectionnés, ou impression.", false,
                    "Export Word", "Impression"),
            ],
            Lignes =
            [
                Ligne("Annonce 2026 B 0201 · Alpha Services SARL", "Rejet du 02/03/2026", "Annonces A", "AFaire"),
                Ligne("Annonce 2026 B 0202 · Bêta Industrie SAS", "Rejet repris · correction à lancer", "Annonces B", "EnCours", 0),
                Ligne("Annonce 2026 B 0203 · Gamma Conseil SARL", "Correction lancée", "Annonces C", "EnCours", 0, 1),
                Ligne("Annonce 2026 B 0204 · Delta Transports SAS", "Corrigée et exportée", "Annonces A", "Validee", 0, 1, 2),
                Ligne("Annonce 2026 B 0205 · Epsilon Habitat SARL", "Non inscrite · non sélectionnable", "Annonces B", "AFaire"),
                Ligne("Annonce 2026 B 0206 · Zêta Commerce SAS", "Corrigée", "Annonces C", "Validee", 0, 1),
                Ligne("Annonce 2026 B 0207 · Êta Boulangerie SARL", "Rejet à corriger", "Annonces A", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SDEMATJUD", Titre = "Suivi dematerialisation", Sujet = "décisions transmises",
            Recherche = "Rechercher une décision, un numéro d'affaire…",
            Criteres = ["Type de date : décision, envoi EDI ou parution", "Dates du et au (par défaut : le dernier mois)", "Journal destinataire (JAL)", "Numéro d'affaire"],
            Valider = "Valider", // proc_sdematjud.md, Etat des boutons par onglet : « VALIDER » (onglet JAL).
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À envoyer", "En préparation", "Envoyée"), new EtatEcran("EnAttente", "En attente de parution", "en-attente")],
            Groupes = ["JAL (journal d'annonces légales)", "BODACC"],
            Marches =
            [
                Marche("Critères", "Type de date (décision, envoi EDI ou parution) et plage de dates. Par défaut : les décisions du dernier mois.", true,
                    "Type de date", "Date du", "Date au"),
                Marche("Journal", "Pour le JAL : le journal destinataire. Le BODACC n'a pas de journal.", false,
                    "Journal destinataire"),
                Marche("Options avancées", "Parution sans retour, numéro d'affaire.", false,
                    "Parution sans retour", "Numéro d'affaire"),
                Marche("Validation", "Valider et vérifier la sélection, éditer les détails. Sur l'onglet BODACC, ces actions sont désactivées : la liste se consulte seulement.", true,
                    "Valider", "Vérifier", "Éditer les détails"),
            ],
            Lignes =
            [
                Ligne("Décision 2026 J 0301 · Alpha Services SARL", "JAL · décision du 02/03/2026", "JAL (journal d'annonces légales)", "AFaire"),
                Ligne("Décision 2026 J 0302 · Bêta Industrie SAS", "JAL · envoi EDI en préparation", "JAL (journal d'annonces légales)", "EnCours", 0, 1),
                Ligne("Décision 2026 J 0303 · Gamma Conseil SARL", "JAL · envoyée le 05/03/2026", "JAL (journal d'annonces légales)", "Validee", 0, 1, 3),
                Ligne("Décision 2026 J 0304 · Delta Transports SAS", "JAL · sans retour de parution", "JAL (journal d'annonces légales)", "EnCours", 0),
                Ligne("Annonce 2026 B 0305 · Epsilon Habitat SARL", "BODACC · parution attendue", "BODACC", "EnAttente"),
                Ligne("Annonce 2026 B 0306 · Zêta Commerce SAS", "BODACC · parution attendue", "BODACC", "EnAttente"),
                Ligne("Décision 2026 J 0307 · Êta Boulangerie SARL", "JAL · décision du 09/03/2026", "JAL (journal d'annonces légales)", "AFaire"),
            ],
        },

        // PROC_SUIVI_IMPRESSION n'a pas de fiche RIG v2 : appuyé sur PROC_PAPERCUT (file
        // d'impression, fiche présente). « Ce qui est sorti » = les travaux imprimés. À confirmer.
        new DescripteurEcran
        {
            Code = "PROC_SUIVI_IMPRESSION", Titre = "Suivi d'impression", Sujet = "travaux d'impression",
            Recherche = "Rechercher un travail, un lot, une imprimante…",
            Criteres = ["Imprimante", "Titre du lot ou du document", "Période"],
            Valider = "Imprimer", // Pas de fiche : libellé de PROC_PAPERCUT (proc_papercut.md, actions de la vue globale : « Imprimer »).
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("En attente", "En cours", "Imprimé"), new EtatEcran("Rejetee", "Vidé de la file", "rejetee")],
            Groupes = ["En attente d'impression", "Imprimés"],
            Marches =
            [
                Marche("Imprimante", "La vue se fait par imprimante : travaux en attente et feuilles.", true,
                    "Imprimante", "Nombre de travaux", "Nombre de feuilles"),
                Marche("Travaux", "Les travaux s'affichent avec leur ordre, leur date de création, leur lot et leur titre.", true,
                    "Ordre d'impression", "Titre du lot", "Titre du document", "Nombre de documents"),
                Marche("Impression", "Imprimer envoie le travail au service d'impression ; le travail quitte la vue.", true,
                    "Imprimer", "Réimprimer"),
                Marche("Vider la file", "Vider supprime les travaux en attente de l'imprimante, pour l'utilisateur courant.", false,
                    "Vider"),
            ],
            Lignes =
            [
                Ligne("Travail 2026 P 0401 · Lot audiences de mars", "Imprimante du 2e étage · en attente", "En attente d'impression", "AFaire"),
                Ligne("Travail 2026 P 0402 · Courriers DCA", "Imprimante du 2e étage · en cours", "En attente d'impression", "EnCours", 0, 1),
                Ligne("Travail 2026 P 0403 · Plumitif de la 1re chambre", "Imprimé le 10/03/2026", "Imprimés", "Validee", 0, 1, 2),
                Ligne("Travail 2026 P 0404 · Convocations", "Imprimé le 11/03/2026", "Imprimés", "Validee", 0, 1, 2),
                Ligne("Travail 2026 P 0405 · Relance LRAR", "Vidé de la file", "En attente d'impression", "Rejetee"),
                Ligne("Travail 2026 P 0406 · Composition", "Imprimante du rez-de-chaussée · en attente", "En attente d'impression", "AFaire"),
                Ligne("Travail 2026 P 0407 · Statistiques", "Imprimé le 12/03/2026", "Imprimés", "Validee", 0, 1, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RELANCEFILE", Titre = "Relancer un fichier", Sujet = "groupes d'édition",
            Recherche = "Rechercher une file, un groupe, une date…",
            Criteres = ["File d'édition", "Date de début (par défaut : aujourd'hui à 7 h)", "Date de fin", "Numéro de groupe"],
            Valider = "Relancer", // proc_relancefile.md, OPE_EDITGRP : « édition + RELANCE ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À relancer", "En cours", "Relancé"),
            Groupes = ["Groupes en erreur", "Groupes en traitement", "Groupes émis"],
            Marches =
            [
                Marche("File", "Une file d'édition se choisit ; ses groupes s'affichent pour la période.", true,
                    "File d'édition"),
                Marche("Période", "Date de début et date de fin. La fin ne peut pas précéder le début.", true,
                    "Date de début", "Date de fin"),
                Marche("Groupe", "Le groupe choisi montre sa date d'ouverture et sa date de clôture. Une date d'ouverture vide prend l'instant présent.", true,
                    "Numéro de groupe", "Date d'ouverture", "Date de clôture"),
                Marche("Éditions", "Les éditions du groupe se listent, avec leur état.", false,
                    "Édition", "État"),
                Marche("Relance", "Relancer les éditions en attente du groupe. Le journal des traitements suit l'avancement.", true,
                    "Relancer", "Journal"),
            ],
            Lignes =
            [
                Ligne("Groupe 2026 0501 · file des convocations", "Erreur d'édition · 3 éditions", "Groupes en erreur", "AFaire"),
                Ligne("Groupe 2026 0502 · file des plumitifs", "Erreur · relance à lancer", "Groupes en erreur", "EnCours", 0, 1),
                Ligne("Groupe 2026 0503 · file des courriers", "Erreur relancée", "Groupes en erreur", "Validee", 0, 1, 2, 4),
                Ligne("Groupe 2026 0504 · file des LRAR", "En traitement depuis 7 h", "Groupes en traitement", "EnCours", 0, 1, 2),
                Ligne("Groupe 2026 0505 · file des bordereaux", "En traitement · éditions listées", "Groupes en traitement", "EnCours", 0),
                Ligne("Groupe 2026 0506 · file des statistiques", "Émis le 10/03/2026", "Groupes émis", "AFaire"),
                Ligne("Groupe 2026 0507 · file des ordonnances", "Émis · relance à lancer", "Groupes émis", "EnCours", 0, 1),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RELANCELOT", Titre = "Relancer un lot", Sujet = "lots d'impression",
            Recherche = "Rechercher un lot, un groupe, une édition…",
            Criteres = ["Nom du lot", "Numéro de groupe", "Édition"],
            Valider = "Valider", // proc_relancelot.md, OPE_EDITGRP : boutons « Valider / Annuler » (actifs si modifié).
            Rang = 5,
            Etats = EtatsEcran.Trois("À imprimer", "En cours", "Imprimé"),
            Groupes = ["Groupes à imprimer", "Groupes en cours", "Groupes figés"],
            Marches =
            [
                Marche("Lot", "Le lot se choisit par son nom ; ses groupes d'impression se listent.", true,
                    "Nom du lot"),
                Marche("Groupe", "Le groupe porte son numéro, ses dates d'ouverture et de fin, et sa file. Un groupe figé ne se modifie plus.", true,
                    "Numéro de groupe", "Date d'ouverture", "Date de fin", "File"),
                Marche("Éditions", "La liste des éditions du groupe s'affiche avec son état.", false,
                    "Édition", "État"),
                Marche("Impression", "Imprimer, ou tout réimprimer selon l'état du groupe. Relancer les éditions manquantes lance un traitement en arrière-plan.", true,
                    "Imprimer", "Tout réimprimer", "Relancer les éditions manquantes"),
                Marche("Figer", "Figer clôt le groupe, possible seulement avant son impression. Pour un utilisateur Amitel, un choix sur les demandes d'édition du groupe est proposé.", false,
                    "Figer le groupe"),
            ],
            Lignes =
            [
                Ligne("Lot relances mars · groupe 2026 0601", "Groupe émis · rien à imprimer", "Groupes à imprimer", "AFaire"),
                Ligne("Lot relances mars · groupe 2026 0602", "Éditions manquantes à relancer", "Groupes à imprimer", "EnCours", 0, 1),
                Ligne("Lot audiences · groupe 2026 0603", "Imprimé", "Groupes à imprimer", "Validee", 0, 1, 3),
                Ligne("Lot audiences · groupe 2026 0604", "Impression en cours", "Groupes en cours", "EnCours", 0, 1),
                Ligne("Lot courriers · groupe 2026 0605", "Groupe figé", "Groupes figés", "Validee", 0, 1, 3, 4),
                Ligne("Lot courriers · groupe 2026 0606", "Groupe figé · éditions listées", "Groupes figés", "Validee", 0, 1, 3),
                Ligne("Lot divers · groupe 2026 0607", "Nouveau groupe à ouvrir", "Groupes à imprimer", "AFaire"),
            ],
        },

        // ---- Les chiffres
        new DescripteurEcran
        {
            Code = "PROC_STATCA", Titre = "Statistiques cour d'appel", Sujet = "affaires",
            Recherche = "Rechercher une affaire, un rôle…",
            Criteres = ["Période du", "Période au (par défaut : le mois précédent)", "Catégorie : contentieux, sauvegarde, référés, injonctions"],
            Valider = "Générer", // proc_statca.md, Particularités de configuration : le bouton APERCU est renommé « Générer ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À générer", "En cours", "Généré"),
            Groupes = ["Contentieux", "Sauvegarde et redressement", "Référés commerciaux", "Injonctions de payer"],
            Marches =
            [
                Marche("Période", "Par défaut : le mois précédent. La fin ne peut pas précéder le début.", true,
                    "Période du", "Période au"),
                Marche("Affaires enrôlées", "Affaires en cours au début de la période, et affaires nouvelles enrôlées pendant la période.", true,
                    "En cours au début", "Nouvelles"),
                Marche("Décisions", "Affaires terminées hors avant-dire-droit, et jugements avant-dire-droit (expertise, sursis à statuer).", true,
                    "Terminées hors avant-dire-droit", "Jugements avant-dire-droit"),
                Marche("Stock et délibérés", "Stock de fin de mois : en cours et nouvelles, moins terminées et avant-dire-droit. Délibérés de plus de deux mois.", false,
                    "Stock de fin de mois", "Délibérés de plus de deux mois"),
                Marche("Décisions à délivrer", "Affaires au plumitif « décision » sans décision saisie.", false,
                    "Décisions à délivrer"),
                Marche("Export Word", "Export Word du tableau généré.", false,
                    "Word"),
            ],
            Lignes =
            [
                Ligne("Rôle 2026J00701 · Alpha Services SARL", "Contentieux · nouvelle affaire", "Contentieux", "AFaire"),
                Ligne("Rôle 2026J00702 · Bêta Industrie SAS", "Contentieux · décision terminante", "Contentieux", "Validee", 0, 1, 2),
                Ligne("Rôle 2026F00703 · Gamma Conseil SARL", "Sauvegarde · en cours", "Sauvegarde et redressement", "EnCours", 0),
                Ligne("Rôle 2026R00704 · Delta Transports SAS", "Référé · avant-dire-droit", "Référés commerciaux", "EnCours", 0, 1),
                Ligne("Rôle 2026IP00705 · Epsilon Habitat SARL", "Injonction de payer · décision à délivrer", "Injonctions de payer", "AFaire"),
                Ligne("Rôle 2026J00706 · Zêta Commerce SAS", "Contentieux · délibéré de plus de deux mois", "Contentieux", "Validee", 0, 1, 2, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_XPILOTAGE", Titre = "Pilotage", Sujet = "tableaux de bord",
            Recherche = "Rechercher un indicateur, une feuille…",
            Criteres = ["Date minimale", "Date maximale", "Feuille : RCS ou judiciaire"],
            Valider = "Générer le tableau Excel", // proc_xpilotage.md, Rôle réel : « générateur de tableau de pilotage statistique Excel ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À générer", "En cours", "Généré"),
            Groupes = ["Feuille RCS", "Feuille judiciaire"],
            Marches =
            [
                Marche("Période", "Les dates minimale et maximale sont substituées dans chaque requête.", true,
                    "Date minimale", "Date maximale"),
                Marche("Requêtes", "Chaque requête de pilotage écrit son résultat dans la cellule qui lui est assignée, sur sa feuille.", true,
                    "Requête", "Cellule"),
                Marche("Encaissements", "Les requêtes d'encaissement se calculent ligne par ligne.", false,
                    "Encaissements"),
                Marche("Archives", "Le bouton Archives ouvre les tableaux générés précédemment.", false,
                    "Archives"),
            ],
            Lignes =
            [
                Ligne("Feuille RCS · ligne 3 · colonne 2", "Requête à lancer", "Feuille RCS", "AFaire"),
                Ligne("Feuille RCS · ligne 4 · colonne 2", "Résultat écrit", "Feuille RCS", "Validee", 0, 1),
                Ligne("Feuille RCS · ligne 5 · colonne 2", "Encaissement calculé", "Feuille RCS", "EnCours", 0, 2),
                Ligne("Feuille RCS · ligne 6 · colonne 2", "Requête à lancer", "Feuille RCS", "AFaire"),
                Ligne("Feuille judiciaire · ligne 3 · colonne 2", "Résultat écrit", "Feuille judiciaire", "Validee", 0, 1),
                Ligne("Feuille judiciaire · ligne 4 · colonne 2", "Requête en cours", "Feuille judiciaire", "EnCours", 0),
                Ligne("Feuille judiciaire · ligne 5 · colonne 2", "Requête à lancer", "Feuille judiciaire", "AFaire"),
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params string[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
