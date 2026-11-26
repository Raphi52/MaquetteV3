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
//   - PROC_LISTE_DELIBERES n'a pas de fiche, mais son code V2 existe (Source\Judiciaire\Exe\PROC_LISTE_DELIBERES\
//     frmListeDelibere.cs) : un Etat en lecture seule, aligne dessus au lot 7 (plan-676a4b19).
//   - PROC_SUIVI_IMPRESSION n'a pas de fiche, mais son code existe (Source\Outils\Exe\Processus\
//     PROC_SUIVI_IMPRESSION) : il lit TRAVAIL_IMPRESSION, de la base commune du service d'impression, absente
//     de la base V3 du greffe -- l'ecran reste en exemple (lot 9). Libelles appuyes sur PROC_PAPERCUT.
//   - PROC_DBE_RELANCE et PROC_DBE_INJ ont une fiche courte (« source non trouvee ») : leurs
//     etapes sont calquees sur la relance DCA (PROC_DCA_RELANCE). A confirmer.
//   - PROC_DBEN_ALERT : fiche sans étape (lecture seule), fondé sur le formulaire FORM_DBEN_ALERT.cs
//     (VALIDER, VERIFIER, INTERROMPRE masqués, lignes 12-14) : une seule marche, « Visualiser le
//     signalement » (OPE_DIVERGENCES.Designer.cs:97). La tuile « En cours » reste vide : la source
//     n'a pas d'état intermédiaire. A confirmer par le chef.
//   - PROC_STATCA : le tableau a lignes fixes de la fiche -- une ligne par categorie de role (J, F, R,
//     IP), ses sept colonnes calculees dans la base (lot 9, LecteurStatistiquesCourAppel.cs).
//   - PROC_SURVBOD : la « requete principale » du haut de la fiche est inventee (le Detail OPE le dit) ;
//     la vraie (OPE_REJETS_BODACC.cs:12-32) est celle de LecteurRejetsBodacc.cs (lot 9).
//   - PROC_XPILOTAGE : les lignes sont les cellules pilotees (table PILOTAGE_REQUETES), par feuille.
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
            Forme = FormeEcran.Audience,
            // L'audience : la recherche de RIG v2, type audience, [J+1 ; J+15] (PROC_COMPO.cs:134-149) ; un cabinet n'a pas de
            // composition. Lecteur : Serveur/Socle/EcransGeneres/LecteurComposition.cs (lot 7). « Valider » enregistre la
            // composition de l'audience cochée : juges (abrégés, le premier préside), greffier, parquetier, tels que les listes
            // ULT_COMBO_* de RIG v2 les nomment (EcrivainComposition.cs ; ETP_COMPO_AUDIENCE.cs:100-141, :240-257).
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", "Juges", "Greffier", "Parquetier"],
            Recherche = "Rechercher une audience, une chambre…",
            Criteres = ["Date de l'audience", "Chambre"],
            Valider = "Valider", // Bouton VALIDER de la barre d'automate (proc_sdematjud.md, « Etat des boutons ») ; controles : proc_compo.md, ETP_COMPO_AUDIENCE.
            Rang = 5,
            Etats = EtatsEcran.Trois("À composer", "En cours", "Composée"),
            Groupes = ["Audiences"],
            Sections =
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
                Ligne("Audience du 12/03/2026 · juge des référés", "Composition à compléter", "Audiences", "EnCours", 0),
                Ligne("Audience du 13/03/2026 · 4e chambre", "Aucune composition", "Audiences", "AFaire"),
                Ligne("Audience du 16/03/2026 · 3e chambre", "Composition enregistrée · invitations envoyées", "Audiences", "Validee", 0, 1, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PREAUD", Titre = "Preparation d'audience", Sujet = "plumitifs",
            Forme = FormeEcran.Audience,
            // Le mode A / P / C (proc_preaud.md:21-23 ; A et P cherchent des audiences, C des cabinets, ETP.cs:54-62) et la date.
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurPreparationAudience.cs (lot 7). « Valider » attribue les numéros d'appel des
            // audiences cochées (EcrivainPreparationAudience.cs) ; le plumitif (état Crystal, file des éditions) n'est pas généré.
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", Champ.Liste("Mode", "Audience", "Procédures collectives", "Cabinet"),
                          Champ.Case("Recalculer les n° d'appel")],
            Recherche = "Rechercher une audience ou un cabinet…",
            Criteres = ["Date de l'audience", "Chambre", "Mode"],
            // Le VALIDER de la barre d'automate (FORM_PREAUD ne masque qu'INTERROMPRE) : « Le calcul sera exécuté automatiquement à la
            // validation » (OPE_RECHERCHE_PLUMITIF.cs:39).
            Valider = "Valider",
            Rang = 5,
            Etats = EtatsEcran.Trois("À préparer", "En préparation", "Plumitif prêt"),
            // Le mode P (procédures collectives) est un mode de l'écran, pas une sorte d'audience : ses audiences sont des audiences.
            Groupes = ["Audiences", "Cabinets"],
            Sections =
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
                Ligne("Audience du 13/03/2026 · procédures collectives", "Ordre des affaires à établir", "Audiences", "AFaire"),
                Ligne("Audience du 16/03/2026 · procédures collectives", "Ordre validé · génération à lancer", "Audiences", "EnCours", 0, 1),
                Ligne("Cabinet du 12/03/2026 · juge des référés", "Plumitif à générer", "Cabinets", "AFaire"),
                Ligne("Cabinet du 13/03/2026 · juge commis", "Envoyé aux juges", "Cabinets", "Validee", 0, 1, 2, 4),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GCONV", Titre = "Convocations", Sujet = "instances à citer",
            Forme = FormeEcran.Audience,
            // Le cabinet : la recherche de RIG v2, type cabinet, fenêtre INTERV_AUD_DATE_*_PREAUD (OPE_CITATIONS.cs:157-167) ; ses
            // instances et leur LRAR (:46-95). Lecteur : Serveur/Socle/EcransGeneres/LecteurConvocations.cs (lot 7). « Citer par
            // huissier » enregistre la citation des instances cochées (EcrivainConvocations.cs ; OPE_CITATIONS.cs:241-332) ; ses
            // éditions (citation, copie conforme, KBIS) et sa facture ne sont pas faites.
            Parametres = [Champ.Date("Date de l'audience"), "Huissier"],
            Recherche = "Rechercher une instance, un numéro de rôle, un dossier, une LRAR…",
            Criteres = ["Date de l'audience"],
            Valider = "Citer par huissier", // proc_gconv.md, Rôle : « Gérer les citations et significations par huissier (LRAR) ».
            Rang = 5,
            // Les nœuds de l'arbre V2 (OPE_CITATIONS.cs:91-95) : coché d'office, grisé (réception, NPAI), déjà cité, sans retour.
            Etats = [.. EtatsEcran.Trois("À citer", "Sans retour", "Citée"), new EtatEcran("Rejetee", "Reçue ou NPAI", "rejetee")],
            Groupes = ["Citations à envoyer", "Citées", "LRAR sans retour", "Reçues ou NPAI"],
            Sections =
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
                Ligne("Instance 2026J00202 · Bêta Industrie SAS", "LRAR avec solution de citation · pas encore citée", "Citations à envoyer", "AFaire"),
                Ligne("Instance 2026J00203 · Gamma Conseil SARL", "LRAR envoyée le 02/03/2026 · pas de retour", "LRAR sans retour", "EnCours"),
                Ligne("Instance 2026J00204 · Delta Transports SAS", "Citée le 04/03/2026", "Citées", "Validee"),
                Ligne("Instance 2026J00205 · Epsilon Habitat SARL", "LRAR revenue NPAI le 05/03/2026", "Reçues ou NPAI", "Rejetee"),
                Ligne("Instance 2026J00206 · Êta Boulangerie SARL", "LRAR reçue le 05/03/2026", "Reçues ou NPAI", "Rejetee"),
                Ligne("Instance 2026J00207 · Thêta Import SAS", "LRAR envoyée le 03/03/2026 · pas de retour", "LRAR sans retour", "EnCours"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_MPCS", Titre = "Mise au point chambres", Sujet = "affaires d'audience",
            Forme = FormeEcran.Audience,
            // RIG v2 ouvre MPCS du retour d'audience sur les affaires PC cochées (OPE_LIST_APPEL_AFFAIRE.cs:1409-1412) : même audience,
            // mêmes paramètres. Lecteur : Serveur/Socle/EcransGeneres/LecteurMiseAuPointChambres.cs (lot 7). « Valider la décision »
            // valide en lot (mode MultiInstance), dans l'ordre de ApplyModifications (PROC_MPCS.cs:1204-1251) : la composition (son
            // numéro dans l'audience), le résultat, le prochain appel (audience du jour saisi, type de classement), une décision du
            // type saisi et sa mesure liée (EcrivainMiseAuPointChambres.cs).
            // fix-ok: second contrôle du lot 7 (26/11/2026) -- ces descripteurs n'avaient pas les paramètres des écritures et recherches
            // de RIG v2 désormais branchées (composition et prochain appel, PROC_MPCS.cs:1204-1238 ; cases des types, Designer :491-522) ;
            // une cause, portée par un remplacement par descripteur (MPCS, Délibérés, Taxes), aucun autre descripteur touché.
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", Champ.Nombre("Composition"), "Résultat", Champ.Date("Prochaine audience"),
                          "Type de classement", "Type de décision", Champ.Date("Date prévue")],
            Recherche = "Rechercher une affaire, un appel, un numéro de rôle…",
            Criteres = ["Date de l'audience", "Chambre"],
            Valider = "Valider la décision", // proc_mpcs.md : « ValidationSauver — Clôture et génération finale ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À juger", "En cours", "Décision validée"),
            Groupes = ["Procédures collectives à l'audience"],
            Sections =
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
                Ligne("Affaire 2026J00301 · Alpha Services SARL c/ Bêta Industrie SAS", "Lot · décision réutilisée", "Procédures collectives à l'audience", "Validee", 0, 3),
                Ligne("Affaire 2026J00302 · Gamma Conseil SARL c/ Delta Transports SAS", "Lot · nouvelle décision à créer", "Procédures collectives à l'audience", "EnCours", 0),
                Ligne("Affaire 2026J00303 · Epsilon Habitat SARL c/ Zêta Commerce SAS", "Lot · à juger", "Procédures collectives à l'audience", "AFaire"),
                Ligne("Affaire 2026J00304 · Êta Boulangerie SARL", "Affaire unique · instance ouverte", "Procédures collectives à l'audience", "EnCours", 0, 1),
                Ligne("Affaire 2026J00305 · Thêta Import SAS", "Affaire unique · décision validée", "Procédures collectives à l'audience", "Validee", 0, 1, 3, 4),
                Ligne("Affaire 2026J00306 · Iota Études SARL", "Affaire unique · à juger", "Procédures collectives à l'audience", "AFaire"),
                Ligne("Affaire 2026J00307 · Kappa Négoce SAS", "Affaire unique · décision à saisir", "Procédures collectives à l'audience", "EnCours", 0, 3),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RETAUD", Titre = "Retour d'audience", Sujet = "affaires d'audience",
            Forme = FormeEcran.Audience,
            // L'audience : la recherche de RIG v2 (date, chambre, mode « C » = cabinet ; OPE_INFORMATIONS_AUDIENCE.cs:99-141,
            // PROC_RETAUD.cs:85-87). Le résultat et la prochaine audience : ceux du renvoi saisi en masse (PROC_MJUDS,
            // OPE_RESULTAT.cs:448-700) ; le type de décision et sa date prévue : l'étape décision du même renvoi, ouverte pour un code
            // prononcé ou délibéré (OPE_RESULTAT.cs:472-473, OPE_DECISION_JUDICIAIRE_MJUDS). Lecteur et gestes :
            // Serveur/Socle/EcransGeneres/LecteurRetourAudience.cs, EcrivainRetourAudience.cs.
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", Champ.Liste("Mode", "Audience", "Cabinet"), "Résultat", Champ.Date("Prochaine audience"),
                          "Type de décision", Champ.Date("Date prévue")],
            Recherche = "Rechercher une affaire, un numéro de rôle, un nom…",
            Criteres = ["Date de l'audience", "Chambre", "Mode"],
            Valider = "Saisir les décisions", // proc_retaud.md, Rôle : « saisir les décisions par affaire ».
            // Le menu de la grille (OPE_LIST_APPEL_AFFAIRE.cs:380, « Marquer les décisions comme rendues ») et le renvoi (PROC_MJUDS).
            Gestes = ["Marquer les décisions comme rendues", "Renvoyer"],
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À saisir", "En cours", "Résultat saisi"), new EtatEcran("EnAttente", "Publicité en attente", "en-attente")],
            // Les filtres de la grille (FiltreAffichage, OPE_LIST_APPEL_AFFAIRE.cs:553-598) ; « tous » montre aussi les traitées.
            Groupes = ["Affaires à traiter", "Prononcés à traiter", "Affaires traitées", "Publicités en attente"],
            Sections =
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
            // Un ÉTAT, pas une audience : RIG v2 n'a pas de fiche, mais son code (Source\Judiciaire\Exe\PROC_LISTE_DELIBERES\
            // frmListeDelibere.cs) est une liste en lecture seule, éditée en Word, sans aucune écriture (lot 7). Lecteur, qui cherche
            // dans la base avec ces paramètres : Serveur/Socle/EcransGeneres/LecteurListeDeliberes.cs.
            // fix-ok: lot 7, mesure par grep -- l'edition scriptee de ce descripteur avait colle deux lignes de commentaire (une
            // barre oblique inverse en fin de ligne lue comme une continuation) ; les deux lignes sont separees, rien d'autre ne change.
            Forme = FormeEcran.Etat,
            // Les critères de recherche de frmListeDelibere.Designer.cs (:400-577) : les types d'instance sont trois cases, comme
            // chkbContentieux, chkbProcedureCollective et chkbRefere (:491-522).
            Parametres = [Champ.Date("Délibérés au"), Champ.Nombre("Durée du délibéré en jours"),
                          Champ.Case("Contentieux"), Champ.Case("Procédures Collectives"), Champ.Case("Référés"), "Juge de l'instance",
                          Champ.Date("Date d'enrôlement min")],
            // Les colonnes de la grille (frmListeDelibere.Designer.cs:126-200).
            Colonnes = ["N° rôle", "N° joints", "Nom de l'affaire", Champ.Date("Date du délibéré"), "Code plumitif", "Durée du délibéré",
                        "Type de décision prévue", Champ.Date("Date prévue"), "Juge", "Localisation"],
            // La barre d'outils (frmListeDelibere.cs:55-61) : « Editer le résultat » (Word), « Délibérés repoussés non rendus » (Excel).
            Gestes = ["Editer le résultat", "Délibérés repoussés non rendus"],
            Recherche = "Rechercher un délibéré, une affaire, un numéro de rôle…",
            Criteres = ["Délibérés au", "Durée du délibéré en jours", "Contentieux", "Procédures Collectives", "Référés", "Juge de l'instance",
                        "Date d'enrôlement min"],
            Valider = "Editer le résultat", // frmListeDelibere.cs:57 : TypeButtonRigToolBar.EDITER, texte « Editer le résultat ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À rendre", "En cours", "Rendu"),
            Groupes = ["Délibérés non rendus"],
            Sections =
            [
                Marche("Recherche", "Les affaires en délibéré depuis au moins la durée (60 jours par défaut), à la date de référence (le dernier jour du mois précédent par défaut), sans décision depuis le délibéré. Sans type coché, le contentieux (la case cochée par défaut de RIG v2).", true,
                    "Délibérés au", "Durée du délibéré en jours", "Contentieux", "Procédures Collectives", "Référés"),
                Marche("Édition", "Le résultat s'édite en Word, colonnes au choix ; les délibérés repoussés non rendus s'exportent en Excel.", false,
                    "Editer le résultat", "Délibérés repoussés non rendus"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Délibéré du 02/02/2026 · 89 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2026J00101", "", "Alpha Services SARL c/ Bêta Industrie SAS", "2026-02-02", "DEL", "89", "JGT", "2026-03-02", "DUPONT Jean", ""] },
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Délibéré du 05/02/2026 · 86 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2026J00102", "2026J00103", "Gamma Conseil SARL c/ Delta Transports SAS", "2026-02-05", "DEL", "86", "", "2026-02-05", "MARTIN Paul", ""] },
                Ligne("Kappa Négoce SAS", "Délibéré du 12/01/2026 · 110 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2025J00950", "", "Kappa Négoce SAS", "2026-01-12", "DEL", "110", "ORD", "2026-02-09", "DUPONT Jean", "Cabinet 2"] },
                Ligne("Êta Boulangerie SARL", "Délibéré du 08/01/2026 · 114 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2025J00930", "", "Êta Boulangerie SARL", "2026-01-08", "DEL", "114", "", "2026-01-08", "", ""] },
                Ligne("Thêta Import SAS", "Délibéré du 15/12/2025 · 138 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2025J00880", "", "Thêta Import SAS", "2025-12-15", "DEL", "138", "JGT", "2026-01-15", "MARTIN Paul", ""] },
                Ligne("Iota Études SARL", "Délibéré du 01/12/2025 · 152 jours", "Délibérés non rendus", "AFaire") with
                    { Valeurs = ["2025J00860", "", "Iota Études SARL", "2025-12-01", "DEL", "152", "", "2025-12-01", "", ""] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TAXESETTOURS", Titre = "Taxes et tours de role", Sujet = "éditions",
            // Une ÉDITION pure : RIG v2 n'écrit rien, il produit trois états Crystal en lecture seule (proc_taxesettours.md:112-118,
            // le Détail OPE fait foi). Le tableau EST leur contenu, lu dans la base : les tours de rôle des mandataires (4D_TOURS.rpt)
            // et leurs acomptes et taxes (4D_TAXES_ACOMPTES.rpt), sur la période par défaut de RIG v2 (du 1er janvier à aujourd'hui,
            // OPE_PERIODE.cs:31-32), ou celle des paramètres saisis, que le lecteur cherche dans la base avec les cases cochées.
            // Lecteur : Serveur/Socle/EcransGeneres/LecteurTaxesEtTours.cs (lot 7).
            Forme = FormeEcran.Etat,
            // OPE_PERIODE (deux dates) et OPE_TYPE_EDITION (trois cases), proc_taxesettours.md:144-150.
            Parametres = [Champ.Date("Date de début"), Champ.Date("Date de fin"), Champ.Case("Tours"), Champ.Case("Acomptes"), Champ.Case("Taxes")],
            // Les champs des deux états : TOURS (FONCTION, ABREGE_ORGANE, TOUR_N à TOUR_4, PlumitifPcTours.cs:39-57) et TAXES_ACOMPTES
            // (NOM, MONTANT_ADJ, _CEP, _LIQ, _MDJ, PlumitifPcTaxes.cs:43-52).
            Colonnes = ["Mandataire", "Fonction", Champ.Nombre("Tour N"), Champ.Nombre("Tour 1"), Champ.Nombre("Tour 2"), Champ.Nombre("Tour 3"),
                        Champ.Nombre("Tour 4"), Champ.Montant("ADJ"), Champ.Montant("CEP"), Champ.Montant("LIQ"), Champ.Montant("MDJ")],
            Gestes = ["Éditer"],
            Recherche = "Rechercher un mandataire…",
            Criteres = ["Date de début", "Date de fin", "Tours", "Acomptes", "Taxes"],
            Valider = "Éditer", // proc_taxesettours.md, Détail OPE : « l'unique action est Éditer » (OPE_RECHERCHE.Designer.cs:77-78).
            Rang = 5,
            // Aucun état par ligne dans RIG v2 (édition pure) : ces trois états sont ceux que tout descripteur porte
            // (EcransGeneresCheck/Program.cs:150-152), la forme Etat ne les affiche pas (FormeEtat.tsx) et chaque ligne lue est
            // « Validee » : le contenu de l'édition. Les Lignes ci-dessous ne s'affichent que sans serveur, marquées « exemple ».
            Etats = EtatsEcran.Trois("À éditer", "En cours", "Éditée"),
            Groupes = ["Tours de rôle", "Acomptes", "Taxes"],
            Sections =
            [
                Marche("Période", "Du 1er janvier de l'année en cours à aujourd'hui par défaut. Sans date de début, l'édition est refusée ; la date de fin est facultative (le jour de début seul) ; le début ne dépasse pas la fin.", true,
                    "Date de début", "Date de fin"),
                Marche("Type d'édition", "Au moins une case : tours, acomptes ou taxes ; un état par case cochée. Sans case cochée, Éditer reste désactivé.", true,
                    "Tours", "Acomptes", "Taxes"),
                Marche("Tours de rôle", "Par mandataire et fonction (administrateur, liquidateur, mandataire judiciaire), le nombre de PC désignées dans la période pour chaque tour, selon les conditions du greffe.", false,
                    "Tour N", "Tour 1", "Tour 2", "Tour 3", "Tour 4"),
                Marche("Acomptes et taxes", "Par mandataire, la somme des montants de la période, par fonction.", false,
                    "ADJ", "CEP", "LIQ", "MDJ"),
            ],
            Lignes =
            [
                Ligne("DURAND MANDATAIRE SELARL (MD01)", "MDJ · tour 1 : 2 · tour 4 : 1", "Tours de rôle", "Validee") with
                    { Valeurs = ["DURAND MANDATAIRE SELARL (MD01)", "MDJ", "0", "2", "0", "0", "1", "", "", "", ""] },
                Ligne("DURAND MANDATAIRE SELARL (MD01)", "LIQ · tour 3 : 1", "Tours de rôle", "Validee") with
                    { Valeurs = ["DURAND MANDATAIRE SELARL (MD01)", "LIQ", "0", "0", "0", "1", "0", "", "", "", ""] },
                Ligne("MARTIN ADMINISTRATEUR SCP (AJ02)", "ADJ · tour 2 : 1", "Tours de rôle", "Validee") with
                    { Valeurs = ["MARTIN ADMINISTRATEUR SCP (AJ02)", "ADJ", "0", "0", "1", "0", "0", "", "", "", ""] },
                Ligne("MARTIN ADMINISTRATEUR SCP", "ADJ 1 200,00 €", "Acomptes", "Validee") with
                    { Valeurs = ["MARTIN ADMINISTRATEUR SCP", "", "", "", "", "", "", "1200.00", "", "", ""] },
                Ligne("DURAND MANDATAIRE SELARL", "LIQ 350,00 € · MDJ 820,00 €", "Taxes", "Validee") with
                    { Valeurs = ["DURAND MANDATAIRE SELARL", "", "", "", "", "", "", "", "", "350.00", "820.00"] },
                Ligne("MARTIN ADMINISTRATEUR SCP", "CEP 140,00 €", "Taxes", "Validee") with
                    { Valeurs = ["MARTIN ADMINISTRATEUR SCP", "", "", "", "", "", "", "", "140.00", "", ""] },
            ],
        },

        // ---- Relancer
        new DescripteurEcran
        {
            // Lot 8 (26/11/2026) : lue et relancee dans la base -- LecteurRelancesComptesAnnuels.cs, EcrivainRelancesComptesAnnuels.cs
            // (« Valider » : la relance suivante des diligences dont la relance precedente est echue, moteur PROC_RSURV de RIG v2).
            Code = "PROC_DCA_RELANCE", Titre = "Relances comptes annuels", Sujet = "dossiers RCS en retard",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Exercices non déposés", "Réponse : réclamation, refus, dépôt tardif"],
            Valider = "Valider", // proc_dca_relance.md, Cycle de vie : « Validation -> PreparerDemandes() -> éditions courrier relance ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À relancer", "En cours", "Relancé"),
            Groupes = ["Sans réponse", "Réclamation", "Refus", "Dépôt tardif"],
            Sections =
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
            // Lot 8 (26/11/2026) : lue dans la base (LecteurInjonctionsComptesAnnuels.cs) ; le geste N'EST PAS BRANCHE. L'injonction de RIG v2 passe par
            // le moteur PROC_ISURV (SURVEILLANCES\PROC_ISURV\PROC_ISURV.cs) : instance creee (l. 252), evenement futur « date limite »
            // (l. 423), editions (l. 437, 468), numeros de role et de repertoire JGAFF (l. 494, 499), restitues en cas d'erreur
            // (l. 739-781) -- une transaction sur plusieurs objets, a faire dans un lot a elle. L'ecran dit « Demonstration ».
            Code = "PROC_DCA_INJ", Titre = "Injonctions comptes annuels", Sujet = "dépôts de comptes annuels",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Exercices non déposés", "Origine : papier ou dématérialisé"],
            Valider = "Valider le dépôt", // proc_dca_inj.md, Rôle : « valide le dépôt ».
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À traiter", "En cours", "Déposée"), new EtatEcran("Rejetee", "Refusée", "rejetee")],
            Groupes = ["Dépôts dématérialisés", "Dépôts papier", "Réclamations et refus"],
            Sections =
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
            // Lot 8 (26/11/2026) : lue dans la base (LecteurRelancesBeneficiaires.cs) ; le geste N'EST PAS BRANCHE. RIG v2 relance
            // les dossiers SANS diligence choisis par la requete de reglage NATURE_DILIGENCE_PARAM SDBE / RSURV / 1 (SqlSelect de
            // RIG_DEV, lue le 26/11/2026) et leur CREE une diligence (moteur PROC_RSURV, relance 1). Toutes les tables qu'elle lit sont
            // dans la base V3 (INSCRIT_PM, FORME_JURIDIQUE_UTI, ETABLISSEMENT, PAYS, EVENEMENT_RCS... : verifie le meme jour) : le
            // geste est faisable, il reste a faire, avec la relance 1 des comptes annuels (meme moteur).
            Code = "PROC_DBE_RELANCE", Titre = "Relances beneficiaires", Sujet = "entreprises en retard de déclaration",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une entreprise, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Déclaration DBE (bénéficiaires effectifs) non faite ou incomplète", "Mode d'envoi : LRAR ou notification électronique"],
            Valider = "Générer les courriers de relance", // proc_dbe_relance.md, Contexte : « Il peut générer des courriers de relance LRAR ou des notifications électroniques ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À relancer", "En cours", "Relancée"),
            Groupes = ["DBE non déposée", "DBE incomplète"],
            Sections =
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
            // Lot 8 (26/11/2026) : lue dans la base (LecteurInjonctionsBeneficiaires.cs) ; le geste N'EST PAS BRANCHE. L'injonction de RIG v2 passe par
            // le moteur PROC_ISURV (SURVEILLANCES\PROC_ISURV\PROC_ISURV.cs) : instance creee (l. 252), evenement futur « date limite »
            // (l. 423), editions (l. 437, 468), numeros de role et de repertoire JGAFF (l. 494, 499), restitues en cas d'erreur
            // (l. 739-781) -- une transaction sur plusieurs objets, a faire dans un lot a elle. L'ecran dit « Demonstration ».
            Code = "PROC_DBE_INJ", Titre = "Injonctions beneficiaires", Sujet = "injonctions DBE",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une entreprise, une relance, un numéro de gestion…",
            Criteres = ["Numéro de gestion ou SIREN", "Relance DBE restée sans effet"],
            Valider = "Générer les injonctions", // proc_dbe_inj.md, Contexte : « le suivi et la génération de ces injonctions ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À enjoindre", "En cours", "Injonction émise"),
            Groupes = ["Relance restée sans effet", "Injonction à suivre"],
            Sections =
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
            // Lot 8 (26/11/2026) : lue dans la base (LecteurRappelsRbe.cs) ; le geste N'EST PAS BRANCHE : la recherche des dossiers a
            // rappeler de RIG v2 ne compile plus (Source\CompilationFull\PowerShelluildErrors.txt:110), et le courrier « Relance RBE »
            // passe par le moteur PROC_ISURV (PROC_ISURV.cs:685), comme les injonctions.
            Code = "PROC_SRBE_RAPPEL", Titre = "Rappels RBE", Sujet = "dossiers RCS",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, une date d'immatriculation, un numéro de gestion…",
            Criteres = ["Date d'immatriculation : avant, à partir de, ou intervalle", "Numéros de gestion, de à"],
            Valider = "Éditer les courriers", // proc_srbe_rappel.md, FORM_SRBE_RAPPEL : « AddButton(EDITION) — le bouton ÉDITION (générer les courriers) ».
            Rang = 5,
            Etats = EtatsEcran.Trois("À envoyer", "En cours", "Courrier généré"),
            Groupes = ["Rappel", "Relance 1"],
            Sections =
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
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dépôt ou un numéro de dossier…",
            Criteres = ["Numéro de dépôt", "Numéro de dossier", "Anomalies sur les pièces"],
            // Le menu de la ligne de la V2 offre « Renvoyer à Infogreffe », mais son geste ne fait RIEN : le corps de
            // _RenvoyerAInfogreffe est en commentaire (PROC_ACTRET\RESULTATS\OPE_RESULTATS.cs:89-97). Aucun écrivain, donc :
            // l'écran le dit (« Démonstration »). Lot 8 (26/11/2026) : lu dans la base -- LecteurRetentionActes.cs.
            Valider = "Renvoyer à Infogreffe",
            Rang = 5,
            // Les trois anomalies que la recherche V2 filtre (Metier.cs:191-205) : piece absente ou non numerisee, piece marquee
            // a ne pas envoyer ; « Pieces completes » ne sort jamais de la liste V2 (tuile a 0).
            Etats = EtatsEcran.Trois("Pièce manquante", "Pièce à ne pas envoyer", "Pièces complètes"),
            // La confidentialite du depot, premiere colonne et cle de tri de la grille V2 (Metier.cs:8-14, :109).
            Groupes = ["Bilan", "Compte de résultat"],
            Sections =
            [
                Marche("Recherche", "Numéro de dépôt, numéro de dossier, anomalies sur les pièces. Entrée, F5 ou F7 lancent la recherche.", true,
                    "Numéro de dépôt", "Numéro de dossier", "Anomalies"),
                Marche("Pièces", "Bilan (BILA), déclaration de confidentialité (CONFI), compte de résultat (RESUL) : un dépôt est complet quand ses pièces attendues sont numérisées.", true,
                    "Bilan", "Déclaration de confidentialité", "Compte de résultat"),
                Marche("Renvoi", "Le renvoi à Infogreffe du menu de la ligne ne fait rien dans RIG v2 (son code est en commentaire) : un dépôt quitte la rétention quand ses pièces attendues sont prêtes.", true,
                    "Actes à renvoyer", "Confirmation"),
                Marche("Consultation", "Voir le dépôt ou le dossier dans la GED depuis le menu de la ligne.", false,
                    "Dépôt", "Dossier"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · 2024 B 00101", "Dépôt 2026-0101 · Bilan : Non numérisé · Déclaration de confidentialité : Numérisée · Compte de résultat : NC", "Bilan", "AFaire"),
                Ligne("Bêta Industrie SAS · 2024 B 00102", "Dépôt 2026-0102 · Bilan : Numérisé · Déclaration de confidentialité : Absente du dépôt · Compte de résultat : NC", "Bilan", "AFaire"),
                Ligne("Gamma Conseil SARL · 2024 B 00103", "Dépôt 2026-0103 · Bilan : Numérisé  (marqué à ne pas envoyer) · Déclaration de confidentialité : Numérisée", "Bilan", "EnCours"),
                Ligne("Delta Transports SAS · 2024 B 00104", "Dépôt 2026-0104 · Bilan : Numérisé · Compte de résultat : Non numérisé", "Compte de résultat", "AFaire"),
                Ligne("Epsilon Habitat SARL · 2024 B 00105", "Dépôt 2026-0105 · Compte de résultat : Numérisé  (marqué à ne pas envoyer)", "Compte de résultat", "EnCours"),
                Ligne("Zêta Commerce SAS · 2024 B 00106", "Dépôt 2026-0106 · Compte de résultat : Absent du dépôt", "Compte de résultat", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ACTREJ", Titre = "Actes rejetes", Sujet = "actes rejetés",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un acte rejeté, un dépôt, un numéro de dossier…",
            Criteres = ["Numéro de dossier", "Numéro de dépôt", "Plage de dates et type de date", "Nombre de résultats (200 par défaut)"],
            Valider = "Renvoyer à Infogreffe", // proc_actrej.md, OPE_RESULTATS, menu contextuel : « Renvoyer à Infogreffe ».
            // Les deux gestes du menu de la ligne (PROC_ACTREJ\RESULTATS\OPE_RESULTATS.cs:68-89) ; l'annulation n'est offerte
            // qu'a un utilisateur Amitel (l. 38-41). Lot 8 (26/11/2026) : LecteurActesRejetes.cs, EcrivainActesRejetes.cs.
            Gestes = ["Renvoyer à Infogreffe", "Annuler le rejet"],
            Rang = 5,
            // La liste V2 ne montre que l'etat GEIDE 9 (MetierActRejets.cs:81) : toute ligne est « Rejete » ; les deux gestes
            // la font sortir de la liste (etat 1 renvoye, 99 annule). Les autres tuiles restent a 0.
            Etats = [.. EtatsEcran.Trois("Rejeté", "Renvoi lancé", "Renvoyé"), new EtatEcran("Rejetee", "Annulé (Amitel)", "rejetee")],
            Groupes = ["Rejets Infogreffe"],
            Sections =
            [
                Marche("Recherche", "Les rejets se filtrent par numéro de dossier, numéro de dépôt, plage de dates et type de date. À l'ouverture, la liste se remplit seule sur les critères par défaut (200 résultats).", true,
                    "Numéro de dossier", "Numéro de dépôt", "Date du", "Date au", "Type de date"),
                // Le menu de la ligne (proc_actrej.md, OPE_RESULTATS). La section s'appelait « Resultats » et repetait la grille
                // que la file affiche deja (controle final du lot 1, 25/11/2026) : elle ne garde que ce menu.
                Marche("Menu de la ligne", "Le menu de la ligne renvoie l'acte à Infogreffe, copie la ligne ou l'ouvre dans la GED.", true,
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
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, un demandeur…",
            Criteres = ["Numéro de dossier", "Demandeur", "Date de début", "Date de fin", "Exclure les dossiers radiés"],
            Valider = "Visualiser le signalement", // OPE_DIVERGENCES.Designer.cs:97 ; même action au double-clic (OPE_DIVERGENCES.cs:115-117). FORM_DBEN_ALERT.cs:13 masque VALIDER.
            Rang = 5,
            // La V2 ne liste QUE les divergences ouvertes (DIVER_STATUS = 0, PROC_DBEN_ALERT.cs:60) et n'a pas d'etat
            // « visualisee » : toute ligne est « Ouverte ». Les deux autres tuiles restent a 0 (une divergence traitee sort de
            // la liste). Lot 8 (26/11/2026) : lu dans la base -- LecteurDivergencesBen.cs ; « Visualiser » ouvre un autre
            // ecran (PROC_DBEN) et n'ecrit rien : pas d'ecrivain.
            Etats = EtatsEcran.Trois("Ouverte", "En cours", "Traitée"),
            Groupes = ["Dossiers actifs", "Dossiers radiés"],
            Sections =
            [
                Marche("Signalement", "Visualiser le signalement ouvre la divergence dans l'écran de dépôt des bénéficiaires effectifs. Ce tableau de bord ne modifie rien : il est en lecture seule.", true,
                    "Numéro de gestion", "Demandeur", "Détail des divergences"),
            ],
            Lignes =
            [
                Ligne("Dossier 2026-0412 · divergence du 02/03/2026", "Demandeur A · 2 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2026-0415 · divergence du 03/03/2026", "Demandeur B · 1 libellé de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2026-0419 · divergence du 05/03/2026", "Demandeur C · 3 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2026-0430 · divergence du 09/03/2026", "Demandeur D · 2 libellés de divergence", "Dossiers actifs", "AFaire"),
                Ligne("Dossier 2025-1187 · divergence du 12/11/2025", "Demandeur E · 1 libellé de divergence", "Dossiers radiés", "AFaire"),
                Ligne("Dossier 2025-1190 · divergence du 14/11/2025", "Demandeur F · 2 libellés de divergence", "Dossiers radiés", "AFaire"),
            ],
        },

        // ---- Les instances
        new DescripteurEcran
        {
            Code = "PROC_SURVIP", Titre = "Injonctions de payer", Sujet = "injonctions de payer",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une injonction, un numéro de rôle, un demandeur…",
            Criteres = ["Juge", "Greffier", "Titre exécutoire", "Non avenue", "Caducité", "Certificat de non-opposition"],
            Valider = "Appliquer les traitements", // proc_survip.md, Rôle : « Appliquer les traitements : Titre exécutoire, Caducité, IP Non avenue, Certificat de non-opposition ».
            Rang = 5,
            // La V2 ne liste que les echeances a traiter : toute ligne est « A traiter » ; une IP traitee sort de la liste
            // (son evenement futur expire). Lot 8 (26/11/2026) : lu dans la base -- LecteurSurveillanceIp.cs ; le traitement
            // (OPE_TRAITEMENTS.PreparationTraitements) n'est pas encore branche : l'ecran le dit (« Demonstration »).
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traitée"),
            // Les TypeTrt de la V2 au mot pres (PROC_SURVIP.cs:18-23), dans l'ordre de chargementTraitements
            // (OPE_RECHERCHE.cs:278-306).
            Groupes = ["Titre exécutoire", "IP Non avenue", "Caducité", "Certif. non opposition"],
            Sections =
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
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Non avenue · signification échue", "IP Non avenue", "EnCours", 0),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Certificat de non-opposition · échéance passée", "Certif. non opposition", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Caducité · traitée", "Caducité", "Validee", 0, 2),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IPSUIVI", Titre = "Suivi des preventions", Sujet = "injonctions de payer",
            // FICHE, pas File (decision du chef, formes a revoir : proc_ipsuivi.md:173). Le « Detail OPE » fait foi : IPSUIVI
            // est le suivi d'UNE injonction de payer apres son ordonnance, ouverte par son numero (PROC_IPSUIVI.cs:20-26,
            // ETP_IPSUIVI.cs:14-15) ; la partie haute de la fiche (dix etats NonTraite... Transfere, IP_SUIVI) ne correspond
            // pas au code (proc_ipsuivi.md:165-171). Lot 8 (26/11/2026) : lue dans la base -- LecteurSuiviIp.cs ; la saisie
            // des dates (Valider : evenements, instance J ou OP, facture, editions) n'est pas branchee, la fiche est en lecture.
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une injonction de payer par son numéro…",
            Criteres = ["N° IP"], // le champ de saisie de la V2 (ETP_IPSUIVI.cs:14-15, GetUltByMetierTitre("N° IP"))
            Valider = "Valider", // la barre de l'OPE (OPE_IPSUIVI.cs:493-516, bouton VALIDER)
            Rang = 5,
            // Les evenements survenus de l'IP (EvenementProcedure.cs:585-604) : rien de signifie, signifiee, close (fin,
            // executoire, non avenue ou caducite).
            Etats = EtatsEcran.Trois("À signifier", "Signifiée", "Close"),
            Groupes = ["Injonctions de payer"],
            Sections =
            [
                // L'IP chargee (OPE_IPSUIVI.cs:845-858) : numero, date de l'ordonnance (1re decision, ou sa date prevue), parties.
                Marche("Injonction de payer", "L'injonction s'ouvre par son numéro. La date IP est celle de l'ordonnance (ou sa date prévue).", true,
                    "N° IP", Champ.Date("Date IP"), "Demandeur", "Défendeur"),
                // Les dates des actes qui suivent l'ordonnance (OPE_IPSUIVI.cs:879-944) : l'evenement survenu, sinon la date
                // d'expiration de l'evenement expire (signification, opposition, provision).
                Marche("Actes après l'ordonnance", "Chaque date vient de l'événement de procédure survenu ; à défaut, de la date d'expiration de l'événement expiré. Dans RIG v2, saisir une date fait survenir l'événement : la V3 ne le reprend pas encore, la fiche se lit.", true,
                    Champ.Date("Signification"), Champ.Case("À personne"), Champ.Date("Opposition"), Champ.Date("Réception LRAR"),
                    Champ.Date("Provision"), Champ.Date("Certificat de non-opposition")),
            ],
            Lignes =
            [
                Ligne("IP 2026 IP 0101 · Alpha Services SARL c/ Bêta Industrie SAS", "ordonnance du 02/03/2026", "Injonctions de payer", "AFaire"),
                Ligne("IP 2026 IP 0102 · Gamma Conseil SARL c/ Delta Transports SAS", "ordonnance du 03/02/2026 · signification 20/02/2026", "Injonctions de payer", "EnCours"),
                Ligne("IP 2026 IP 0103 · Epsilon Habitat SARL c/ Zêta Commerce SAS", "ordonnance du 05/01/2026 · signification 12/01/2026 · opposition 30/01/2026", "Injonctions de payer", "EnCours"),
                Ligne("IP 2025 IP 0904 · Êta Boulangerie SARL c/ Thêta Import SAS", "ordonnance du 10/09/2025 · signification 18/09/2025", "Injonctions de payer", "Validee"),
                Ligne("IP 2026 IP 0105 · Iota Études SARL c/ Kappa Négoce SAS", "ordonnance du 09/03/2026", "Injonctions de payer", "AFaire"),
                Ligne("IP 2025 IP 0806 · Lambda Pêche SARL c/ Mu Logistique SAS", "ordonnance du 01/08/2025 · signification 08/08/2025 · provision 01/09/2025", "Injonctions de payer", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SURVMES", Titre = "Mesures", Sujet = "mesures",
            Forme = FormeEcran.File,
            // La file des échéances de mesures (proc_survmes.md, Détail OPE) : lue dans la base par
            // Serveur/Socle/EcransGeneres/LecteurSurveillanceMesures.cs (lot 9). Les boutons de FORM_SURVMES.cs:20-29 :
            // VALIDER (l'expiration des événements, OPE_AFFICHAGE_MES.cs:83-136), « Editer les lignes cochées », « Editer tout ».
            // Sans écrivain : le moteur des événements de procédure de RIG v2 n'est pas dans la V3 (LecteurSurveillanceMesures.cs).
            Recherche = "Rechercher une mesure, un rôle, un code d'événement…",
            Criteres = ["Délai supplémentaire (jours)", "Événements expirables seulement"], // OPE_RECHERCHE_MES.cs:49-55.
            Valider = "Valider", // FORM_SURVMES.cs : le bouton VALIDER du processus (DoVerifier puis expiration, OPE_AFFICHAGE_MES.cs:83).
            Gestes = ["Valider", "Editer les lignes cochées", "Editer tout"], // FORM_SURVMES.cs:23-29.
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À traiter", "En cours", "Traitée"), new EtatEcran("Rejetee", "Sans code d'expiration", "rejetee")],
            Groupes = ["Mesures à traiter", "Sans code d'expiration"],
            Sections =
            [
                Marche("Recherche", "Les événements ni survenus ni expirés des mesures actives (ni rapport déposé, ni sans suite, ni caduques), arrivés à échéance : date maximale, sinon minimale, sinon fixe, au plus aujourd'hui moins le délai supplémentaire.", true,
                    "Délai supplémentaire (jours)", Champ.Case("Événements expirables seulement")),
                Marche("Événements de procédure", "Cocher un code d'événement coche toutes les mesures qui le portent. Un code sans code d'expiration est décoché, grisé et en rouge.", false,
                    "Code d'événement"),
                Marche("Cochage", "Cocher une mesure la recharge ; une mesure déjà verrouillée par une autre demande se décoche.", true,
                    "Mesure cochée", "Compteur"),
                Marche("Validation", "Valider fait expirer l'événement de chaque ligne cochée et crée l'événement survenu, sous une demande par mesure.", true,
                    "Valider"),
                Marche("Éditions Word", "Un aperçu Word des lignes cochées, ou de toutes les lignes affichées.", false,
                    "Editer les lignes cochées", "Editer tout"),
            ],
            Lignes =
            [
                Ligne("Mesure 2026M00107 · Alpha Services SARL", "rôle 2026J00101 · RAPP Dépôt du rapport · échéance 02/03/2026", "Mesures à traiter", "AFaire"),
                Ligne("Mesure 2026M00106 · Bêta Industrie SAS", "rôle 2026J00102 · CONS Consignation · échéance 05/03/2026", "Mesures à traiter", "AFaire"),
                Ligne("Mesure 2026M00105 · Gamma Conseil SARL", "rôle 2026J00103 · RAPP Dépôt du rapport · échéance 09/03/2026", "Mesures à traiter", "AFaire"),
                Ligne("Mesure 2026M00104 · Delta Transports SAS", "rôle 2026J00104 · OBS Observations · échéance 10/03/2026 · sans code d'expiration", "Sans code d'expiration", "Rejetee"),
                Ligne("Mesure 2026M00103 · Epsilon Habitat SARL", "rôle 2026J00105 · CONS Consignation · échéance 11/03/2026", "Mesures à traiter", "AFaire"),
                Ligne("Mesure 2026M00102 · Zêta Commerce SAS", "rôle 2026J00106 · OBS Observations · échéance 12/03/2026 · sans code d'expiration", "Sans code d'expiration", "Rejetee"),
                Ligne("Mesure 2026M00101 · Êta Boulangerie SARL", "rôle 2026J00107 · RAPP Dépôt du rapport · échéance 13/03/2026", "Mesures à traiter", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTLOT", Titre = "Lots", Sujet = "lots",
            Forme = FormeEcran.Referentiel,
            // La table des lots d'impression (proc_gestlot.md, Détail OPE ; OPE_AJOUT.cs) : lue par
            // Serveur/Socle/EcransGeneres/LecteurLots.cs, écrite par EcrivainLots.cs (lot 9) -- Ajouter, Modifier (renommer
            // compris : les groupes d'impression suivent), Supprimer (un agent du greffe désactive le lot).
            Colonnes = ["Nom du lot", "Imprimante", Champ.Case("Impression automatique"), "Heure d'impression", Champ.Case("Lot actif"), Champ.Case("Lot direct")],
            Recherche = "Rechercher un lot par son nom ou son imprimante…", // OPE_RECHERCHE.cs:62-64 : nom + imprimante.
            Criteres = ["Nom du lot", "Lot actif"],
            Valider = "Valider le lot", // proc_gestlot.md, OPE_AJOUT : « _valider() », séquence transactionnelle.
            Rang = 5,
            Etats = EtatsEcran.Trois("À compléter", "En cours", "Enregistré"),
            Groupes = ["Lots actifs", "Lots inactifs"],
            Sections =
            [
                Marche("Nom", "Nom obligatoire et unique : un nom déjà pris est refusé. Renommer reporte le nouveau nom sur les groupes d'impression de l'ancien lot.", true,
                    "Nom du lot"),
                Marche("Imprimante", "Imprimante du lot. RIG v2 propose alors de l'associer au groupe d'impression actif ; RIG v3 ne pose pas la question.", false,
                    "Imprimante"),
                Marche("Impression", "Impression automatique à l'heure choisie.", false,
                    Champ.Case("Impression automatique"), "Heure d'impression"),
                Marche("Activation", "Un lot créé est actif d'office. La case « Lot direct » ne se saisit pas : un seul lot est direct, et l'enregistrer retire « direct » aux autres.", true,
                    Champ.Case("Lot actif"), Champ.Case("Lot direct")),
                Marche("Suppression", "Supprimer un lot le désactive pour un agent du greffe ; la suppression définitive, avec ses groupes, est réservée à Amitel.", false,
                    "Supprimer"),
            ],
            Lignes =
            [
                Ligne("Lot relances mars", "imprimante Imprimante du 2e étage · actif", "Lots actifs", "Validee"),
                Ligne("Lot édition audiences", "imprimante Imprimante du 1er étage · impression automatique à 08:30 · actif", "Lots actifs", "Validee"),
                Ligne("Lot impression directe", "imprimante Imprimante de l'accueil · actif · lot direct", "Lots actifs", "Validee"),
                Ligne("Lot relances DBE", "imprimante Imprimante du 2e étage · actif", "Lots actifs", "Validee"),
                Ligne("Lot archives 2025", "imprimante Imprimante du 2e étage · inactif", "Lots inactifs", "Validee"),
                Ligne("Lot tri mensuel", "sans imprimante · actif", "Lots actifs", "Validee"),
                Ligne("Lot relances LRAR", "imprimante Imprimante du 1er étage · impression automatique à 18:00 · actif", "Lots actifs", "Validee"),
            ],
        },

        // ---- Les envois et les éditions
        new DescripteurEcran
        {
            Code = "PROC_SURVBOD", Titre = "Surveillance BODACC", Sujet = "annonces",
            Forme = FormeEcran.File,
            // Le contrôle qualité des annonces BODACC (proc_survbod.md, Détail OPE : la vraie requête est
            // OPE_REJETS_BODACC.cs:12-32) : lu par Serveur/Socle/EcransGeneres/LecteurRejetsBodacc.cs (lot 9). Sans écrivain :
            // « Modifier » ouvre le processus VB6 XACTION, Word et Imprimer produisent un document (LecteurRejetsBodacc.cs).
            Recherche = "Rechercher une annonce, un numéro de gestion…",
            Criteres = ["Numéro de gestion RCS", "Numéro chronologique", "Date de publication", "Type d'annonce"], // colonnes de la grille, DataGridViewResultats.cs.
            Valider = "Modifier l'annonce", // Le bouton « M » de la ligne (colXaction) et le double clic : LaunchXAction, OPE_REJETS_BODACC.cs:82-95.
            Gestes = ["Modifier l'annonce", "Word", "Imprimer"], // FORM_SURVBOD._InitToolbar : WORD, IMPRIMER (MODIFIER y est masqué).
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À corriger", "En cours", "Corrigée"), new EtatEcran("Rejetee", "Sans dossier RCS", "rejetee")],
            Groupes = ["Rejets du contrôle qualité"],
            Sections =
            [
                Marche("Contrôle qualité", "Les annonces publiées depuis 2008, d'un bordereau AP, ANU, AU, B, BR ou C, dont le contrôle qualité porte une remarque non vérifiée.", true,
                    "Numéro de gestion RCS", "Numéro chronologique", "Remarque du contrôle qualité"),
                Marche("Correction", "« Modifier » lance la correction de l'annonce d'un dossier RCS. Une annonce sans dossier RCS (un non-inscrit) ne se corrige pas d'ici.", true,
                    "Annonce", "Action corrective liée"),
                Marche("Envoi", "Export Word des annonces cochées, ou impression.", false,
                    "Word", "Imprimer"),
            ],
            Lignes =
            [
                Ligne("Annonce A0001 du 02/03/2026 · 2026 B 00201", "chrono 2026000201 · Adresse du siège incomplète", "Rejets du contrôle qualité", "AFaire"),
                Ligne("Annonce A0003 du 03/03/2026 · 2026 B 00202", "chrono 2026000202 · Capital non renseigné", "Rejets du contrôle qualité", "AFaire"),
                Ligne("Annonce A0010 du 04/03/2026 · 2026 B 00203", "chrono 2026000203 · Activité absente", "Rejets du contrôle qualité", "AFaire"),
                Ligne("Annonce A0002 du 05/03/2026 · non-inscrit 2026N00004", "chrono D2026000204 · Nom commercial incomplet · sans dossier RCS : pas de correction", "Rejets du contrôle qualité", "Rejetee"),
                Ligne("Annonce A0001 du 06/03/2026 · 2026 B 00205", "chrono 2026000205 · Date d'effet absente", "Rejets du contrôle qualité", "AFaire"),
                Ligne("Annonce A0020 du 09/03/2026 · 2026 B 00206", "chrono 2026000206 · Dirigeant sans date de naissance", "Rejets du contrôle qualité", "AFaire"),
                Ligne("Annonce A0004 du 10/03/2026 · non-inscrit 2026N00007", "chrono D2026000207 · Enseigne incomplète · sans dossier RCS : pas de correction", "Rejets du contrôle qualité", "Rejetee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SDEMATJUD", Titre = "Suivi dematerialisation", Sujet = "décisions transmises",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une décision, un numéro d'affaire…",
            Criteres = ["Type de date : décision, envoi EDI ou parution", "Dates du et au (par défaut : le dernier mois)", "Journal destinataire (JAL)", "Numéro d'affaire"],
            Valider = "Valider", // proc_sdematjud.md, Etat des boutons par onglet : « VALIDER » (onglet JAL).
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À envoyer", "En préparation", "Envoyée"), new EtatEcran("EnAttente", "En attente de parution", "en-attente")],
            Groupes = ["JAL (journal d'annonces légales)", "BODACC"],
            Sections =
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

        // PROC_SUIVI_IMPRESSION n'a pas de fiche RIG v2, mais son code existe (Source\Outils\Exe\Processus\PROC_SUIVI_IMPRESSION :
        // OPE_RECHERCHE.cs:160-207 lit TRAVAIL_IMPRESSION, BASE_COMMON_TRAVAIL_IMPRESSION de la base COMMUNE du service
        // d'impression). Cette table n'est pas dans la base du greffe (absente de RIG_DEV, lu le 26/11/2026) ni dans la base V3 :
        // l'écran reste sur ses lignes d'exemple, étiquetées (lot 9). Libellés appuyés sur PROC_PAPERCUT (file d'impression).
        new DescripteurEcran
        {
            Code = "PROC_SUIVI_IMPRESSION", Titre = "Suivi d'impression", Sujet = "travaux d'impression",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un travail, un lot, une imprimante…",
            Criteres = ["Imprimante", "Titre du lot ou du document", "Période"],
            Valider = "Imprimer", // Pas de fiche : libellé de PROC_PAPERCUT (proc_papercut.md, actions de la vue globale : « Imprimer »).
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("En attente", "En cours", "Imprimé"), new EtatEcran("Rejetee", "Vidé de la file", "rejetee")],
            Groupes = ["En attente d'impression", "Imprimés"],
            Sections =
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
            Forme = FormeEcran.File,
            // Les groupes (traitements d'édition) des files d'édition (proc_relancefile.md, Détail OPE) : lus par
            // Serveur/Socle/EcransGeneres/LecteurTraitementsEdition.cs (lot 9). Les boutons d'OPE_EDITGRP.cs:44-48 : RELANCER
            // « Relancer via editrcs », FIGER « Cloturer le groupe », IMPRIMER_AUTRE « Tout réimprimer » : joués par
            // EcrivainTraitementsEdition.cs -- « Tout réimprimer » remet les éditions du groupe à imprimer dans la base,
            // « Cloturer le groupe » clôt un groupe ouvert et ouvre le suivant de sa file (TraitementEdition.Cloturer) ;
            // « Relancer via editrcs » est refusé (un programme externe, jamais lancé).
            Recherche = "Rechercher une file, un groupe, une date…",
            Criteres = ["File d'édition", "Date de début (par défaut : aujourd'hui à 7 h)", "Date de fin", "Numéro de groupe"],
            Valider = "Relancer via editrcs", // OPE_EDITGRP.cs:44-48, _relancer (:237-240).
            Gestes = ["Relancer via editrcs", "Cloturer le groupe", "Tout réimprimer"],
            Rang = 5,
            Etats = EtatsEcran.Trois("Ouvert", "Clôturé", "Clôturé sans édition"), // un groupe clos se réimprime, transmis ou non (OPE_EDITGRP.cs:205)
            Groupes = ["Groupes ouverts", "Groupes clôturés"],
            Sections =
            [
                Marche("File", "Une file d'édition se choisit ; ses groupes s'affichent pour la période. Les files Amitel ne se montrent qu'à Amitel.", true,
                    "File d'édition"),
                Marche("Période", "Date de début et date de fin. La fin ne peut pas précéder le début.", true,
                    "Date de début", "Date de fin"),
                Marche("Groupe", "Le groupe montre sa date d'ouverture et sa date de clôture. Un groupe clôturé et vide n'est pas affiché.", true,
                    "Numéro de groupe", "Date d'ouverture", "Date de clôture"),
                Marche("Éditions", "Les éditions du groupe se listent, avec leur état.", false,
                    "Édition", "État"),
                Marche("Relance", "Un groupe ouvert se clôture (une imprimante est obligatoire) ou se relance ; un groupe clôturé se réimprime.", true,
                    "Relancer via editrcs", "Cloturer le groupe", "Tout réimprimer"),
            ],
            Lignes =
            [
                Ligne("File RCS interne · groupe 501", "3 éditions · ouvert le 10/03/2026 · Imprimante du 2e étage", "Groupes ouverts", "AFaire"),
                Ligne("File RCS interne · groupe 502", "12 éditions · ouvert le 09/03/2026 · clôturé le 09/03/2026 · transmission finie", "Groupes clôturés", "EnCours"),
                Ligne("File RCS autre · groupe 503", "1 édition · ouvert le 10/03/2026 · Imprimante du 1er étage", "Groupes ouverts", "AFaire"),
                Ligne("File RCS autre · groupe 504", "4 éditions · ouvert le 08/03/2026 · clôturé le 08/03/2026 · en transmission", "Groupes clôturés", "EnCours"),
                Ligne("File Info légal · groupe 505", "0 édition · ouvert le 10/03/2026", "Groupes ouverts", "AFaire"),
                Ligne("File Info légal · groupe 506", "7 éditions · ouvert le 06/03/2026 · clôturé le 07/03/2026 · transmission finie", "Groupes clôturés", "EnCours"),
                Ligne("File RCS toque · groupe 507", "2 éditions · ouvert le 05/03/2026 · clôturé le 05/03/2026 · en transmission", "Groupes clôturés", "EnCours"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RELANCELOT", Titre = "Relancer un lot", Sujet = "lots d'impression",
            Forme = FormeEcran.File,
            // Les groupes d'impression des lots (proc_relancelot.md, Détail OPE) : lus par
            // Serveur/Socle/EcransGeneres/LecteurGroupesImpression.cs, relancés par EcrivainGroupesImpression.cs (lot 9) -- les
            // boutons d'OPE_EDITGRP.cs:109-128, actifs selon la phase : IMPRIMER, IMPRIMER_AUTRE (« Tout réimprimer »), FIGER.
            // L'état du groupe change dans la base ; le message au service d'impression n'est pas envoyé.
            Recherche = "Rechercher un lot, un groupe…",
            Criteres = ["Nom du lot", "Numéro de groupe ou nom de l'imprimante", "Date de début", "Date de fin"], // OPE_RECHERCHE.Designer.cs:90-197.
            Valider = "Imprimer", // OPE_EDITGRP.cs:144-146 : IMPRIMER -> _basculerEtatP(false).
            Gestes = ["Imprimer", "Tout réimprimer", "Figer le groupe"], // OPE_EDITGRP.cs:141-149.
            Rang = 5,
            Etats =
            [
                new EtatEcran("AFaire", "À réimprimer", "a-traiter"), new EtatEcran("EnCours", "À figer", "saisie-manuelle"),
                new EtatEcran("Validee", "Imprimé", "validee"), new EtatEcran("EnAttente", "Prêt à l'émission", "en-attente"),
                new EtatEcran("Rejetee", "Erreur sans reprise", "rejetee"),
            ],
            Groupes = ["Groupes émis", "Groupes intermédiaires", "Groupes en traitement", "Groupes en erreur"],
            Sections =
            [
                Marche("Lot", "Le lot se choisit par son nom ; ses groupes d'impression se listent. Un groupe émis sans édition n'est pas affiché.", true,
                    "Nom du lot"),
                Marche("Groupe", "Le groupe porte son numéro, ses dates de début et de fin, son imprimante et sa phase. La phase ne se saisit pas : seuls les boutons la changent.", true,
                    "Numéro de groupe", "Date de début", "Date de fin", "Imprimante"),
                Marche("Éditions", "La liste des éditions du groupe s'affiche avec son état.", false,
                    "Édition", "État"),
                Marche("Impression", "Un groupe émis ou en erreur se remet à imprimer : « Imprimer » le groupe seul, « Tout réimprimer » avec toutes ses éditions.", true,
                    "Imprimer", "Tout réimprimer"),
                Marche("Figer", "Figer clôt un groupe initial : sa date de fin est posée et il passe prêt à l'émission pour l'impression finale.", false,
                    "Figer le groupe"),
            ],
            Lignes =
            [
                Ligne("Lot relances mars · groupe 2026-0601", "Emis · 3 éditions · ouvert le 09/03/2026 · fini le 09/03/2026 · Imprimante du 2e étage", "Groupes émis", "AFaire"),
                Ligne("Lot relances mars · groupe 2026-0602", "Initial · 2 éditions · ouvert le 10/03/2026 · Imprimante du 2e étage", "Groupes intermédiaires", "EnCours"),
                Ligne("Lot audiences · groupe 2026-0603", "Prêt à l'émission · 5 éditions · ouvert le 10/03/2026 · Imprimante du 1er étage", "Groupes intermédiaires", "EnAttente"),
                Ligne("Lot audiences · groupe 2026-0604", "En cours d'émission · 4 éditions · ouvert le 10/03/2026 · Imprimante du 1er étage", "Groupes en traitement", "EnAttente"),
                Ligne("Lot courriers · groupe 2026-0605", "Erreur · 1 édition · ouvert le 08/03/2026 · Imprimante de l'accueil", "Groupes en erreur", "AFaire"),
                Ligne("Lot courriers · groupe 2026-0606", "Emis · 6 éditions · ouvert le 07/03/2026 · fini le 07/03/2026 · Imprimante de l'accueil", "Groupes émis", "AFaire"),
                Ligne("Lot divers · groupe 2026-0607", "Initial · 0 édition · ouvert le 10/03/2026", "Groupes intermédiaires", "EnCours"),
            ],
        },

        // ---- Les chiffres
        new DescripteurEcran
        {
            Code = "PROC_STATCA", Titre = "Statistiques cour d'appel", Sujet = "affaires",
            Forme = FormeEcran.Etat,
            // Le tableau « Activité commerciale » de RIG v2 (proc_statca.md, Détail OPE ; OPE_GENERER.cs:132-593), calculé dans
            // la base par Serveur/Socle/EcransGeneres/LecteurStatistiquesCourAppel.cs (lot 9) : quatre catégories (le numéro de
            // rôle J, F, R, IP), sept colonnes aux en-têtes de la grille V2 (:48-81) ; le pied totalise, comme la ligne « totaux ».
            // La période cherche dans la base ; « Word » (PrintWord) n'a pas d'édition dans la V3.
            Parametres = [Champ.Periode("Période")],
            Colonnes =
            [
                "Catégorie", Champ.Nombre("affaires en cours en début de période"), Champ.Nombre("affaires nouvelles"),
                Champ.Nombre("affaires terminées"), Champ.Nombre("jugements ADD"), Champ.Nombre("dossier en cours en fin de mois - stock"),
                Champ.Nombre("indicateurs spécifiques délibérés de plus de 2 mois (pour les décisions rendues dans la période)"),
                Champ.Nombre("indicateurs spécifiques décision à délivrer ou à mettre en forme"),
            ],
            Gestes = ["Word"], // OPE_RECHERCHE.cs:32-34 : WORD ; « Générer » (APERCU) est ici le calcul de la période.
            Recherche = "Rechercher une catégorie…",
            Criteres = ["Période"], // OPE_RECHERCHE : la période du et au, par défaut le mois précédent (InitDate, :37-64).
            Valider = "Générer", // proc_statca.md : le bouton APERCU est renommé « Générer » (OPE_RECHERCHE.cs:33).
            Rang = 5,
            Etats = EtatsEcran.Trois("À générer", "En cours", "Généré"),
            Groupes = ["Activité commerciale"],
            Sections =
            [
                Marche("Période", "Par défaut : le mois précédent. Le début ne peut pas suivre la fin, ni l'un ni l'autre dépasser aujourd'hui.", true,
                    "Période"),
                Marche("Affaires enrôlées", "Affaires en cours au début de la période (sans décision terminante), et affaires nouvelles enrôlées pendant la période.", true,
                    "En cours au début", "Nouvelles"),
                Marche("Décisions", "Affaires terminées hors avant-dire-droit, et jugements avant-dire-droit (expertise, sursis à statuer).", true,
                    "Terminées hors avant-dire-droit", "Jugements avant-dire-droit"),
                Marche("Stock et délibérés", "Stock de fin de mois : en cours et nouvelles, moins terminées et avant-dire-droit. Délibérés de plus de deux mois, sauf injonctions de payer.", false,
                    "Stock de fin de mois", "Délibérés de plus de deux mois"),
                Marche("Procédures collectives", "Redressements, liquidations, clôtures pour insuffisance d'actif et pour extinction du passif de la période.", false,
                    "Nb redressement judiciaire", "Nb liquidations (LIJI ou LJ)", "Nb clôtures pour insuffisance d'actif", "Nb clôtures pour extinction du passif"),
                Marche("Export Word", "Export Word du tableau généré.", false,
                    "Word"),
            ],
            Lignes =
            [
                Ligne("contentieux général", "Activité commerciale du 01/02/2026 au 28/02/2026", "Activité commerciale", "Validee") with { Valeurs = ["contentieux général", "182", "14", "11", "3", "182", "1", "2"] },
                Ligne("affaires de procédure collective", "Activité commerciale du 01/02/2026 au 28/02/2026", "Activité commerciale", "Validee") with { Valeurs = ["affaires de procédure collective", "96", "9", "6", "1", "98", "0", "1"] },
                Ligne("référés commerciaux", "Activité commerciale du 01/02/2026 au 28/02/2026", "Activité commerciale", "Validee") with { Valeurs = ["référés commerciaux", "12", "7", "8", "0", "11", "0", "0"] },
                Ligne("injonctions de payer", "Activité commerciale du 01/02/2026 au 28/02/2026", "Activité commerciale", "Validee") with { Valeurs = ["injonctions de payer", "40", "22", "19", "0", "43", "0", "0"] },
                Ligne("contentieux général", "Activité commerciale du 01/01/2026 au 31/01/2026", "Activité commerciale", "Validee") with { Valeurs = ["contentieux général", "179", "16", "13", "0", "182", "2", "1"] },
                Ligne("affaires de procédure collective", "Activité commerciale du 01/01/2026 au 31/01/2026", "Activité commerciale", "Validee") with { Valeurs = ["affaires de procédure collective", "94", "8", "5", "1", "96", "0", "0"] },
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_XPILOTAGE", Titre = "Pilotage", Sujet = "tableaux de bord",
            Forme = FormeEcran.Etat,
            // Le tableau de pilotage de RIG v2 (proc_xpilotage.md, Détail OPE ; OPE_GENERER.cs) : une ligne par cellule de la table
            // PILOTAGE_REQUETES, sa requête calculée dans la base sur la période et un an plus tôt, par
            // Serveur/Socle/EcransGeneres/LecteurPilotage.cs (lot 9). Une requête qui lit une table reprise en fiche, ou un
            // calcul d'encaissement, est « Non calculé » avec sa cause. Le classeur Excel et les archives n'ont pas d'édition V3.
            Parametres = [Champ.Date("Date minimale"), Champ.Date("Date maximale"), Champ.Liste("Feuille", "RCS", "Judiciaire")],
            Colonnes = ["Indicateur", "Cellule", "Valeur sur la période", "Valeur un an plus tôt", "Calcul"],
            Gestes = ["Visualiser", "Archives"], // OPE_GENERER.cs:74 : APERCU renommé « Visualiser » (le classeur Excel) ; ARCHIVES (:416-425).
            Recherche = "Rechercher un indicateur…",
            Criteres = ["Date minimale", "Date maximale", "Feuille"], // OPE_GENERER : la période (par défaut le mois précédent, :123-143), les cases RCS et JUD.
            Valider = "Visualiser", // OPE_GENERER.cs:74.
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À calculer", "En cours", "Calculé"), new EtatEcran("Rejetee", "Non calculé", "rejetee")],
            Groupes = ["Feuille RCS", "Feuille judiciaire"],
            Sections =
            [
                Marche("Période", "Les dates minimale et maximale remplacent @DateMin et @DateMax dans chaque requête ; la même période un an plus tôt donne la colonne voisine. Par défaut : le mois précédent.", true,
                    "Date minimale", "Date maximale"),
                Marche("Requêtes", "Chaque requête de pilotage écrit son résultat dans la cellule qui lui est assignée, sur sa feuille. Une requête en erreur n'arrête pas le tableau.", true,
                    "Requête", "Cellule"),
                Marche("Encaissements", "Les requêtes d'encaissement se calculent ligne par ligne, plafonnées à l'encaissé net des débours.", false,
                    "Encaissements"),
                Marche("Archives", "Le bouton Archives ouvre les tableaux générés précédemment.", false,
                    "Archives"),
            ],
            Lignes =
            [
                Ligne("Immat PP", "calculé du 01/02/2026 au 28/02/2026", "Feuille RCS", "Validee") with { Valeurs = ["Immat PP", "ligne 7 · colonne 2", "41", "38", "calculé du 01/02/2026 au 28/02/2026"] },
                Ligne("Immat PM Com", "calculé du 01/02/2026 au 28/02/2026", "Feuille RCS", "Validee") with { Valeurs = ["Immat PM Com", "ligne 8 · colonne 2", "57", "61", "calculé du 01/02/2026 au 28/02/2026"] },
                Ligne("Délai moyen CCI", "calculé du 01/02/2026 au 28/02/2026", "Feuille RCS", "Validee") with { Valeurs = ["Délai moyen CCI", "ligne 7 · colonne 6", "2,5", "3,1", "calculé du 01/02/2026 au 28/02/2026"] },
                Ligne("Encaissements inscriptions", "non calculé : calcul d'encaissement ligne par ligne (TraiterEncaiss) non repris", "Feuille RCS", "Rejetee") with { Valeurs = ["Encaissements inscriptions", "ligne 28 · colonne 6", "", "", "non calculé : calcul d'encaissement ligne par ligne (TraiterEncaiss) non repris"] },
                Ligne("Ordonnances IP", "calculé du 01/02/2026 au 28/02/2026", "Feuille judiciaire", "Validee") with { Valeurs = ["Ordonnances IP", "ligne 7 · colonne 2", "14", "12", "calculé du 01/02/2026 au 28/02/2026"] },
                Ligne("Oppositions - IP", "calculé du 01/02/2026 au 28/02/2026", "Feuille judiciaire", "Validee") with { Valeurs = ["Oppositions - IP", "ligne 8 · colonne 2", "11", "15", "calculé du 01/02/2026 au 28/02/2026"] },
                Ligne("RJ en LJ - Conversions", "calculé du 01/02/2026 au 28/02/2026", "Feuille judiciaire", "Validee") with { Valeurs = ["RJ en LJ - Conversions", "ligne 8 · colonne 10", "9", "7", "calculé du 01/02/2026 au 28/02/2026"] },
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
