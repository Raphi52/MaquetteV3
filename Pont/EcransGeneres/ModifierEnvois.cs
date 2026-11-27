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
            // Forme Audience (lot 10, plan-676a4b19) : RIG v2 lance la rédaction en masse depuis le retour d'UNE audience, avec ses
            // décisions (OPE_INFORMATIONS_AUDIENCE.GetDJForRedaMasse, PROC_RETAUD, :586-608 ; proc_reda_masse.md, Détail OPE). Lecteur et
            // geste : Serveur/Socle/EcransGeneres/LecteurRedactionEnMasse.cs, EcrivainRedactionEnMasse.cs (la validation greffe : la
            // minute) ; la génération Word, l'impression de la minute et les dépens (moteur REDA) ne sont pas repris.
            Forme = FormeEcran.Audience,
            Parametres = [Champ.Date("Date de l'audience"), "Chambre"],
            Recherche = "Rechercher une décision, un numéro de rôle, une partie…",
            Criteres = ["Date de l'audience", "Chambre"],
            Valider = "Valider au greffe",
            Gestes = ["Valider au greffe"], // La case « Validation greffe » de la grille (OPE_LISTE_DJ_REDA_MASSE.cs:120-123).
            Rang = 2,
            Etats = EtatsEcran.Trois("À générer", "Brouillon ou révision", "Minute validée"), // États de rédaction 0, 1-2, 3 (:853-875).
            Groupes = ["Jugements", "Ordonnances"],
            Sections =
            [
                Marche("Dates d'audience", "Délibéré et prononcé renseignés : sans les deux, la décision ne se valide pas. La date de la décision devient celle de l'audience du prononcé.", true,
                    "Date de délibéré", "Date de prononcé"),
                Marche("Modèle", "Le modèle du document de la décision est obligatoire pour la valider.", true,
                    "Modèle de document"),
                Marche("Génération", "Génère le document à partir du modèle, pour une décision encore à générer. Une décision déjà générée ne se régénère pas. La génération se fait dans RIG v2.", true,
                    "Génération de la décision"),
                Marche("Validation greffe", "Attribue le numéro de minute d'une décision en brouillon ou en révision, puis la passe « projet révisé à rendre ». Un échec est signalé ; les autres décisions continuent.", true,
                    "Numéro de minute"),
                Marche("Impression de la minute", "Impression regroupée par instance, faite par RIG v2.", true,
                    "Impression de la minute"),
                Marche("Dépens", "Génération des dépens, possible seulement une fois la minute imprimée.", false,
                    "Génération des dépens"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00101 · jugement · à créer", "Jugements", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00102 · jugement · brouillon", "Jugements", "EnCours"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00103 · jugement · projet en révision", "Jugements", "EnCours"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026F00104 · projet révisé à rendre · minute 2606600001", "Jugements", "Validee"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026F00105 · ordonnance · projet révisé à rendre · minute 2606600002", "Ordonnances", "Validee"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026F00106 · ordonnance · à créer", "Ordonnances", "AFaire"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026F00107 · ordonnance · brouillon", "Ordonnances", "EnCours"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026F00108 · ordonnance · projet en révision", "Ordonnances", "EnCours"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DEST_REDA", Titre = "Destinataires", Sujet = "destinataires",
            // Une ligne par destinataire (partie, mandataire, destinataire divers) des décisions qui ont un document REDA ; la fiche porte
            // son édition et, sur le document de la décision, la date de délivrance des grosses et les suppléments (proc_dest_reda.md,
            // Détail OPE). Lecteur et geste : LecteurDestinatairesRedaction.cs, EcrivainDestinatairesRedaction.cs (lot 10). L'ajout et la
            // suppression d'un destinataire divers (FormAjouterDestinataire, menu « Supprimer ») ne sont pas repris.
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher un destinataire, un numéro de rôle, une affaire…",
            Criteres = ["Numéro de rôle"],
            Valider = "Enregistrer les destinataires",
            Rang = 2,
            Etats = EtatsEcran.Trois("Date de délivrance à saisir", "Délivrance datée", "Enregistré"),
            Groupes = ["Parties", "Mandataires", "Destinataires divers"],
            Sections =
            [
                Marche("Décision", "La décision dont le destinataire reçoit une édition.", true,
                    "Numéro de rôle", "Type de décision"),
                Marche("Édition du jugement", "Chaque destinataire reçoit une édition : grosse (G), copie (C), expédition (E) ou lettre de notification (L), dans cet ordre. Sans aucune, X ; D : l'édition par défaut de RIG v2.", true,
                    "Édition", Champ.Case("Grosse"), Champ.Case("Copie"), Champ.Case("Expédition"), Champ.Case("Lettre de notification")),
                Marche("Date de délivrance des grosses", "Obligatoire pour continuer : sans elle, l'enregistrement avertit, et les autres opérations du processus restent verrouillées.", true,
                    "Date de délivrance des grosses"),
                Marche("Suppléments", "Nombre de grosses, de copies et d'expéditions en plus, saisi à la main, pour toute la décision.", false,
                    Champ.Nombre("Grosses supplémentaires"), Champ.Nombre("Copies supplémentaires"), Champ.Nombre("Expéditions supplémentaires")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Rôle 2026F00101 · partie · aucune édition · date de délivrance à saisir", "Parties", "AFaire"),
                Ligne("Bêta Industrie SAS", "Rôle 2026F00101 · partie · grosse + copie · date de délivrance à saisir", "Parties", "AFaire"),
                Ligne("Gamma Conseil SARL", "Rôle 2026F00102 · partie · expédition · grosses délivrées le 06/03/2026", "Parties", "EnCours"),
                Ligne("Cabinet Delta Avocats", "Rôle 2026F00102 · mandataire postulant · lettre de notification · grosses délivrées le 06/03/2026", "Mandataires", "EnCours"),
                Ligne("Cabinet Epsilon Avocats", "Rôle 2026F00103 · mandataire plaidant · grosse + copie · grosses délivrées le 09/03/2026", "Mandataires", "EnCours"),
                Ligne("Zêta Commerce SAS", "Rôle 2026F00103 · destinataire divers · expédition · grosses délivrées le 09/03/2026", "Destinataires divers", "EnCours"),
                Ligne("Thêta Import SAS", "Rôle 2026F00104 · destinataire divers · aucune édition · date de délivrance à saisir", "Destinataires divers", "AFaire"),
                Ligne("Iota Études SARL", "Rôle 2026F00104 · partie · édition par défaut · date de délivrance à saisir", "Parties", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_SIGNJUD", Titre = "Signature electronique", Sujet = "décisions à signer",
            // Les deux onglets de RIG v2 (juge, greffier) en deux groupes, aux états Oodrive (proc_signjud.md, Détail OPE). Lecteur et
            // gestes : LecteurSignatureElectronique.cs, EcrivainSignatureElectronique.cs (lot 10) -- Oodrive et CertSign ne sont jamais
            // appelés : leur partie est simulée et le dit, l'état de RIG v2 est écrit.
            Forme = FormeEcran.File,
            Recherche = "Rechercher une décision, un numéro de rôle, un juge…",
            Criteres = ["Juge", "Greffier", "État de signature"],
            Valider = "Envoyer au juge",
            Gestes = ["Envoyer au juge", "Signer les décisions", "Corriger"], // OPE_RESULTAT_SIGNJUD.cs:64-73 ; le menu « Corriger » (:292-349).
            Rang = 2,
            Etats =
            [
                new EtatEcran("AFaire", "À traiter", "a-traiter"), new EtatEcran("EnCours", "À corriger", "saisie-manuelle"),
                new EtatEcran("Validee", "Signée", "validee"), new EtatEcran("EnAttente", "Chez le juge", "en-attente"),
                new EtatEcran("Rejetee", "En erreur", "rejetee"),
            ],
            Groupes = ["Pour le juge", "Pour le greffier"],
            Sections =
            [
                Marche("Contrôle de la décision", "Juge, greffier, une seule édition et date prévue renseignés, sans erreur bloquante. Une décision en erreur ne se coche pas.", true,
                    "Juge", "Greffier", "Édition", "Date prévue"),
                Marche("Envoi au juge", "Seules les décisions à envoyer ou à corriger partent au juge, par le parapheur Oodrive (simulé par la V3 : rien n'est envoyé).", true,
                    "Décision cochée", "Envoi au juge"),
                Marche("Signature du juge", "Le juge signe en premier dans Oodrive ; s'il demande des corrections, la décision revient « À corriger ».", true,
                    "Signature du juge"),
                Marche("Contresignature du greffier", "Le greffier signataire contresigne les décisions signées par le juge (CertSign, simulé par la V3 : rien n'est signé ni mis en GED).", true,
                    "Contresignature", "Mise en GED"),
                Marche("Correction", "« Corriger » renvoie une décision au juge. Tant que le juge n'a pas signé, tout le lot du même dossier repasse à corriger.", false,
                    "Passage à corriger"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00101 · juge 1 · greffier 1 · à envoyer au juge", "Pour le juge", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00102 · juge 1 · Attente de signature", "Pour le juge", "EnAttente"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00103 · juge 2 · Le document doit être corrigée", "Pour le juge", "EnCours"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026F00104 · juge 2 · Signé par le juge", "Pour le greffier", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026F00105 · greffier 1 · Signé par le greffier", "Pour le greffier", "Validee"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026F00106 · juge 1 · Erreur de signature juge", "Pour le juge", "Rejetee"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026F00107 · juge 2 · à envoyer au juge", "Pour le juge", "AFaire"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026F00108 · greffier 2 · Signé par le juge", "Pour le greffier", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ORDOSIGN", Titre = "Ordonnances a signer", Sujet = "ordonnances à signer",
            // Les cases de la recherche de RIG v2 (OPE_RECHERCHE.Designer.cs, MetierTitre), une par groupe ; « Signer la sélection »
            // rend les décisions cochées à la date du jour et attribue leur minute (OPE_LISTE_ORDONNANCES.cs, proc_ordosign.md Détail
            // OPE). Lecteur et geste : LecteurOrdonnancesASigner.cs, EcrivainOrdonnancesASigner.cs (lot 10) ; les éditions et courriels de
            // la signature ne sont pas produits.
            Forme = FormeEcran.File,
            Recherche = "Rechercher une instance, un numéro de rôle, une affaire…",
            Criteres = ["Type ordonnance", "N° rôle d'une ordonnance à rééditer"],
            Valider = "Signer la sélection",
            Gestes = ["Signer la sélection"],
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À signer", "En cours", "Signée"), new EtatEcran("EnAttente", "Demande en cours", "en-attente")],
            Groupes = ["OP", "OP (liée à une PC)", "OP (changement de juge)", "Prévention", "IP", "JC", "Juge RCS", "Injonction DCA", "Injonction RBE", "Injonctions juge commis"],
            Sections =
            [
                Marche("Décision rendue", "Cocher la décision rendue : une décision non rendue reste dans la liste à signer. Une instance tenue par une demande en cours ne se coche pas.", true,
                    "Décision rendue"),
                Marche("Date de la décision", "La date du jour, entre la date de saisine du greffe et aujourd'hui. Une minute déjà attribuée impose le jour qu'elle porte.", true,
                    "Date de la décision"),
                Marche("Changement de juge commissaire", "Un seul changement en attente à la fois, traité dans RIG v2.", false,
                    "Juge sortant", "Nouveau juge"),
                Marche("Copies", "Nombre de copies par destinataire, réglé dans le paramétrage des copies de RIG v2.", false,
                    "Destinataires des copies", "Format d'impression"),
                Marche("Numéro de minute", "Attribué à la signature, dans la série du type de décision. Un numéro déjà attribué n'est pas recalculé.", true,
                    "Numéro de minute"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026OP00201 · ordonnance · prévue le 04/03/2026", "OP", "AFaire"),
                Ligne("Gamma Conseil SARL", "Rôle 2026OP00202 · ordonnance · signée le 05/03/2026 · minute 2606400001", "OP", "Validee"),
                Ligne("Delta Transports SAS", "Rôle 2026OP00203 · Instance bloquée, demande \"En cours\"", "OP", "EnAttente"),
                Ligne("Epsilon Habitat SARL", "Rôle 2026OP00204 · procédure collective · prévue le 06/03/2026", "OP (liée à une PC)", "AFaire"),
                Ligne("Zêta Commerce SAS", "Rôle 2026OP00205 · injonction de dépôt des comptes · prévue le 06/03/2026", "Injonction DCA", "AFaire"),
                Ligne("Êta Boulangerie SARL", "Rôle 2026OP00206 · injonction RBE · signée le 06/03/2026", "Injonction RBE", "Validee"),
                Ligne("Thêta Import SAS", "Rôle 2026IP00207 · injonction de payer · prévue le 09/03/2026", "IP", "AFaire"),
                Ligne("Iota Études SARL", "Rôle 2026JC00208 · juge commis · prévue le 09/03/2026", "JC", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_DJ_OCCULTATIONS", Titre = "Occultations", Sujet = "décisions",
            // Les consignes d'occultation d'une décision pour l'open data : 12 cases et deux listes de mots (proc_dj_occultations.md,
            // Détail OPE). RIG v2 n'appelle aucun service ici. Lecteur et geste : LecteurOccultations.cs, EcrivainOccultations.cs (lot 10).
            Forme = FormeEcran.Fiche,
            Recherche = "Rechercher une décision, un numéro de rôle…",
            Criteres = ["Numéro de rôle"],
            Valider = "Enregistrer les occultations",
            Rang = 2,
            Etats = EtatsEcran.Trois("À occulter", "Occultations saisies", "Validées"),
            Groupes = ["Jugements", "Ordonnances"],
            Sections =
            [
                Marche("Décision", "La décision ouverte. Sans décision, l'écran affiche seulement un message : rien à saisir.", true,
                    "Numéro de rôle", "Type de décision"),
                Marche("Catégories d'occultation", "Les cases sont pré-cochées selon le paramétrage du greffe pour une décision sans consignes ; chaque case s'ajuste à la main.", true,
                    Champ.Case("Intérêt particulier"), Champ.Case("Personne Morale"), Champ.Case("Personne physico-morale/géo-morale"), Champ.Case("Adresse"),
                    Champ.Case("Dates civiles (naissance , mariage, décès)"), Champ.Case("Plaque d'immatriculation"), Champ.Case("Cadastre"),
                    Champ.Case("Chaine de numéro identifiante"), Champ.Case("Coordonnées électroniques"),
                    Champ.Case("Occultations spéciales soumises à décision du président du tribunal de commerce (Nom Prénom des juges et des Greffiers)"),
                    Champ.Case("Occultations des motifs (débats en chambre du conseil protection de la vie privé et ou sécurité physique des parties)"),
                    Champ.Case("Occultations des motifs (Secret des affaires)")),
                Marche("Mots à masquer", "Un mot par ligne. Un mot ne peut pas être à la fois à masquer et à conserver (sans tenir compte de la casse).", false,
                    Champ.Zone("Mots-clés à masquer")),
                Marche("Mots à conserver", "Un mot par ligne. Un mot retiré de la liste disparaît à l'enregistrement.", false,
                    Champ.Zone("Mots-clés à conserver")),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00301 · jugement du 04/03/2026 · occultations non saisies", "Jugements", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00302 · jugement · cases enregistrées, aucun mot", "Jugements", "EnCours"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00303 · jugement · 2 mots à masquer", "Jugements", "EnCours"),
                Ligne("Êta Boulangerie SARL c/ Thêta Import SAS", "Rôle 2026F00304 · jugement · occultations non saisies", "Jugements", "AFaire"),
                Ligne("Iota Études SARL c/ Kappa Négoce SAS", "Rôle 2026F00305 · ordonnance du 05/03/2026 · occultations non saisies", "Ordonnances", "AFaire"),
                Ligne("Lambda Pêche SARL c/ Mu Logistique SAS", "Rôle 2026F00306 · ordonnance · 1 mot à conserver", "Ordonnances", "EnCours"),
                Ligne("Nu Paiement SAS c/ Xi Entrepôts SARL", "Rôle 2026F00307 · ordonnance · 3 mots à masquer · 1 mot à conserver", "Ordonnances", "EnCours"),
                Ligne("Omicron Bâtiment SARL c/ Pi Services SAS", "Rôle 2026F00308 · ordonnance du 06/03/2026 · occultations non saisies", "Ordonnances", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_IMPLEG", Titre = "Impression legale", Sujet = "décisions d'une audience",
            // Forme Audience (lot 10) : le périmètre de RIG v2 est UNE audience de cabinet (« IdAudience », ou la recherche d'audience) et
            // ses décisions à la minute éditée (OPE_IMPLEG.RequeteSQL_DataDJ, :52-82 ; proc_impleg.md, Détail OPE). Lecteur et geste :
            // LecteurImpressionLegale.cs, EcrivainImpressionLegale.cs -- l'état « rendu avant signature » et l'expertise numérotée sont
            // écrits ; les documents REDA (moteur de RIG v2) ne sont pas produits.
            Forme = FormeEcran.Audience,
            Parametres = [Champ.Date("Date de l'audience"), "Chambre"],
            Recherche = "Rechercher une décision, un numéro de rôle, un nom…",
            Criteres = ["Date de l'audience", "Chambre"],
            Valider = "Imprimer la sélection",
            Gestes = ["Imprimer la sélection"],
            Rang = 2,
            Etats = EtatsEcran.Trois("Minute éditée", "Impression légale effectuée", "Décision figée"), // RequeteSQL_DataDJ, :56-66.
            Groupes = ["Décisions de l'audience"],
            Sections =
            [
                Marche("Décisions", "Les décisions prononcées à l'audience dont la minute est éditée (état 3) ou l'impression légale déjà faite (état 4). « Non diffusable » est signalé.", true,
                    "Numéro de minute", "Numéro de répertoire"),
                Marche("Impression", "Une demande d'édition par décision, envoyée en lot à l'éditeur par RIG v2. La décision « révisé à rendre » passe « rendu avant signature ».", true,
                    "Imprimée"),
                Marche("Expertise", "Une décision dont le type porte une mesure liée reçoit son numéro d'expertise (JMEXP) à l'impression.", false,
                    "Numéro d'expertise"),
                Marche("Dépens", "Calcul des dépens en option, fait par RIG v2.", false,
                    "Dépens"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Rôle 2026F00401 · expulsion · minute 2606600011 · Minute éditée", "Décisions de l'audience", "AFaire"),
                Ligne("Bêta Industrie SAS", "Rôle 2026F00402 · expulsion · minute 2606600012 · Impression légale effectuée", "Décisions de l'audience", "EnCours"),
                Ligne("Gamma Conseil SARL", "Rôle 2026F00403 · expertise · minute 2606600013 · Minute éditée", "Décisions de l'audience", "AFaire"),
                Ligne("Delta Transports SAS", "Rôle 2026F00404 · minute 2606600014 · Impression légale effectuée", "Décisions de l'audience", "EnCours"),
                Ligne("Epsilon Habitat SARL", "Rôle 2026F00405 · minute 2606600015 · Minute éditée - ATTENTION - NON DIFFUSABLE", "Décisions de l'audience", "AFaire"),
                Ligne("Zêta Commerce SAS", "Rôle 2026F00406 · minute 2606600016 · Minute éditée", "Décisions de l'audience", "AFaire"),
                Ligne("Êta Boulangerie SARL", "Rôle 2026F00407 · minute 2606600017 · Impression légale effectuée", "Décisions de l'audience", "EnCours"),
                Ligne("Thêta Import SAS", "Rôle 2026F00408 · minute 2606600018 · Minute éditée", "Décisions de l'audience", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_COURRIERM", Titre = "Courriers en masse", Sujet = "instances, procédures et mesures",
            // Forme Audience (lot 10) : RIG v2 part d'UNE audience ou d'un cabinet (date, chambre ou section), charge ses instances ou ses
            // procédures, coche « fiche » ou « seconde édition » et génère (proc_courrierm.md, Détail OPE). Lecteur et gestes :
            // LecteurCourriersEnMasse.cs, EcrivainCourriersEnMasse.cs -- la demande et la demande d'édition sont posées dans la file des
            // éditions ; le service d'édition de RIG v2 les produit. Préventions et mesures : trouvées par leur seul numéro dans RIG v2,
            // non reprises (groupes vides).
            Forme = FormeEcran.Audience,
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", Champ.Liste("Mode", "Audience", "Cabinet"), "File différée ou imprimante", "Seconde édition"],
            Recherche = "Rechercher une instance, une procédure, un numéro…",
            Criteres = ["Date de l'audience", "Chambre", "Mode"],
            Valider = "Générer la fiche",
            Gestes = ["Générer la fiche", "Générer la seconde édition"], // Les deux cases de la grille (OPE_RESULTAT_COURRIERM_INSTANCE.cs:153-190).
            Rang = 2,
            Etats = EtatsEcran.Trois("Sans courrier du jour", "Courrier du jour généré", "Générés"),
            Groupes = ["Instances", "Procédures", "Préventions", "Mesures"],
            Sections =
            [
                Marche("Volet", "Instances ou procédures collectives de l'audience choisie. Une entité déjà dans la liste n'y entre pas deux fois.", true,
                    "Volet"),
                Marche("Fiche", "« Générer la fiche » : l'édition principale de l'entité (fiche instance, fiche procédure).", true,
                    "Fiche"),
                Marche("Deuxième édition", "« Générer la seconde édition » : le code choisi (par défaut celui du paramétrage du greffe), hors fiche.", false,
                    "Seconde édition"),
                Marche("Génération", "Au moins une entité cochée. Une demande par entité et une édition par geste, vers la file différée ou l'imprimante choisie.", true,
                    "File différée ou imprimante"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL c/ Bêta Industrie SAS", "Rôle 2026F00501 · audience du 04/03/2026 · appel 1", "Instances", "AFaire"),
                Ligne("Gamma Conseil SARL c/ Delta Transports SAS", "Rôle 2026F00502 · appel 2 · courrier du jour « fiche instance »", "Instances", "EnCours"),
                Ligne("Epsilon Habitat SARL c/ Zêta Commerce SAS", "Rôle 2026F00503 · appel 3", "Instances", "AFaire"),
                Ligne("Êta Boulangerie SARL", "PC 2026P00601 · ouverte le 02/02/2026 · appel 4", "Procédures", "AFaire"),
                Ligne("Thêta Import SAS", "PC 2026P00602 · courrier du jour « fiche procédure » · file « Courriers »", "Procédures", "EnCours"),
                Ligne("Iota Études SARL", "Rôle 2026F00504 · appel 5", "Instances", "AFaire"),
                Ligne("Kappa Négoce SAS", "Rôle 2026F00505 · appel 6 · courrier du jour « convocation »", "Instances", "EnCours"),
                Ligne("Lambda Pêche SARL", "PC 2026P00603 · ouverte le 12/01/2026 · appel 7", "Procédures", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_EDT_DIFF", Titre = "Editions differees", Sujet = "lots d'éditions",
            // Une ligne par lot d'éditions différées ouvert (MetierEdtDiff.GetLotEdtActifs, :19-24 ; proc_edt_diff.md, Détail OPE).
            // Lecteur et geste : LecteurEditionsDifferees.cs, EcrivainEditionsDifferees.cs (lot 10) ; le détail d'un lot document par
            // document et sa case « Ne pas imprimer » ne sont pas repris.
            Forme = FormeEcran.File,
            Recherche = "Rechercher un lot, une imprimante…",
            Criteres = ["Date d'ouverture du lot", "Imprimante", "Nombre de documents"],
            Valider = "Clôturer le lot",
            Gestes = ["Clôturer le lot"], // Le bouton VALIDER relibellé « Clôturer » (OPE_EDT_DIFF_LOT.cs:26-27).
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À clôturer", "En préparation", "Clôturé"), new EtatEcran("EnAttente", "Imprimante à choisir", "en-attente")],
            Groupes = ["Lots ouverts"],
            Sections =
            [
                Marche("Imprimante", "Imprimante obligatoire pour clôturer : sans elle, le lot ne se ferme pas. Une imprimante interne d'Amitel est signalée, sans blocage.", true,
                    "Imprimante"),
                Marche("Documents du lot", "Les documents du lot et ceux marqués « Ne pas imprimer », barrés. Le détail se règle dans RIG v2.", false,
                    "Ne pas imprimer", "Exemplaires"),
                Marche("Clôture", "Ferme le lot : il passe prêt à imprimer et disparaît de la liste d'attente.", true,
                    "Date de clôture"),
            ],
            Lignes =
            [
                Ligne("Lot COURRIERS", "12 documents · ouvert le 04/03/2026 · imprimante « Imprimante du 1er étage » · groupe 2026-0101", "Lots ouverts", "AFaire"),
                Ligne("Lot NOTIFICATIONS", "5 documents · 1 à ne pas imprimer · ouvert le 04/03/2026 · imprimante « Imprimante du 2e étage » · groupe 2026-0102", "Lots ouverts", "AFaire"),
                Ligne("Lot RELANCES", "3 documents · ouvert le 05/03/2026 · aucune imprimante choisie · groupe 2026-0103", "Lots ouverts", "EnAttente"),
                Ligne("Lot CONVOCATIONS", "8 documents · ouvert le 05/03/2026 · imprimante « Imprimante de l'accueil » · groupe 2026-0104", "Lots ouverts", "AFaire"),
                Ligne("Lot COPIES", "2 documents · ouvert le 06/03/2026 · aucune imprimante choisie · groupe 2026-0105", "Lots ouverts", "EnAttente"),
                Ligne("Lot GROSSES", "4 documents · ouvert le 06/03/2026 · imprimante « Imprimante du 1er étage » · groupe 2026-0106", "Lots ouverts", "AFaire"),
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
