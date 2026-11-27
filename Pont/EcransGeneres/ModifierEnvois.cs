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
            // Forme Referentiel (lot 10) : RIG v2 tient les destinataires d'une décision rédigée dans une GRILLE (RedaDestinatairesDataGridView)
            // dont l'édition se change ligne par ligne, avec « Ajouter » et « Supprimer » (OPE_DEST_REDA.cs:103-109 ; proc_dest_reda.md,
            // Détail OPE). Une ligne par destinataire (partie, mandataire, destinataire divers) ; la date de délivrance des grosses et les
            // suppléments sont ceux du document de la décision. Lecteur et gestes : LecteurDestinatairesRedaction.cs,
            // EcrivainDestinatairesRedaction.cs (lot 10).
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Destinataire", "Numéro de rôle", "Qualité", "Adresse", "Édition", Champ.Date("Date de délivrance des grosses"),
                        Champ.Nombre("Grosses supplémentaires"), Champ.Nombre("Copies supplémentaires"), Champ.Nombre("Expéditions supplémentaires")],
            Recherche = "Rechercher un destinataire, un numéro de rôle, une affaire…",
            Criteres = ["Numéro de rôle"],
            Valider = "Enregistrer les destinataires",
            Rang = 2,
            Etats = EtatsEcran.Trois("Date de délivrance à saisir", "Délivrance datée", "Enregistré"),
            Groupes = ["Parties", "Mandataires", "Destinataires divers"],
            Sections =
            [
                Marche("Destinataire", "Les parties de l'instance (demandeur, défendeur, intervenant), leurs mandataires et les destinataires divers de la décision. « Ajouter » saisit un destinataire divers : son libellé, son adresse et le rôle de la décision ; seul un destinataire divers se supprime.", true,
                    "Destinataire", "Numéro de rôle", "Qualité", "Type de décision", "Adresse"),
                Marche("Édition du jugement", "Chaque destinataire reçoit une édition : grosse (G), copie (C), expédition (E) ou lettre de notification (L), dans cet ordre. Sans aucune, X ; D : l'édition par défaut de RIG v2.", true,
                    "Édition"),
                Marche("Date de délivrance des grosses", "Obligatoire pour continuer : sans elle, l'enregistrement avertit, et les autres opérations du processus restent verrouillées. Elle vaut pour toute la décision.", true,
                    Champ.Date("Date de délivrance des grosses")),
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
            // éditions ; le service d'édition de RIG v2 les produit. Préventions et mesures : trouvées par leur numéro, comme la zone de
            // numéro de leur volet dans RIG v2 (OPE_RECHERCHE_COURRIERM_PREVENTION.cs:74-85) ; une mesure n'a que la seconde édition.
            Forme = FormeEcran.Audience,
            Parametres = [Champ.Date("Date de l'audience"), "Chambre", Champ.Liste("Mode", "Audience", "Cabinet"), "N° de prévention", "N° de mesure",
                          "File différée ou imprimante", "Seconde édition"],
            Recherche = "Rechercher une instance, une procédure, un numéro…",
            Criteres = ["Date de l'audience", "Chambre", "Mode", "N° de prévention", "N° de mesure"],
            Valider = "Générer la fiche",
            Gestes = ["Générer la fiche", "Générer la seconde édition"], // Les deux cases de la grille (OPE_RESULTAT_COURRIERM_INSTANCE.cs:153-190).
            Rang = 2,
            Etats = EtatsEcran.Trois("Sans courrier du jour", "Courrier du jour généré", "Générés"),
            Groupes = ["Instances", "Procédures", "Préventions", "Mesures"],
            Sections =
            [
                Marche("Volet", "Instances ou procédures collectives de l'audience choisie ; une prévention ou une mesure par son numéro. Une entité déjà dans la liste n'y entre pas deux fois.", true,
                    "Volet"),
                Marche("Fiche", "« Générer la fiche » : l'édition principale de l'entité (fiche instance, fiche procédure, fiche prévention). Une mesure n'a pas de fiche.", true,
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
            // fix-ok: constate par le chef du plan sur 8bc0a99 (27/11/2026) -- les descripteurs du lot 10 omettaient des fonctions de RIG v2 :
            // le detail d'un lot ici, les volets Préventions et Mesures des courriers, l'ajout et la suppression d'un destinataire divers.
            // Une ligne par lot d'éditions différées ouvert (MetierEdtDiff.GetLotEdtActifs, :19-24) et, dans « Documents des lots », le
            // détail de chaque lot document par document avec sa case « Ne pas imprimer » (OPE_LISTE_LOT, DataGridViewListeLot ;
            // proc_edt_diff.md, Détail OPE). Lecteur et gestes : LecteurEditionsDifferees.cs, EcrivainEditionsDifferees.cs (lot 10).
            Forme = FormeEcran.File,
            Recherche = "Rechercher un lot, un document, un destinataire…",
            Criteres = ["Date d'ouverture du lot", "Imprimante", "Nombre de documents"],
            Valider = "Clôturer le lot",
            // Le bouton VALIDER relibellé « Clôturer » (OPE_EDT_DIFF_LOT.cs:26-27) ; la case « Ne pas imprimer » de la grille, cochée ou
            // décochée puis TRAITER (OPE_LISTE_LOT._TraiterEditionsCochees, :53-78).
            Gestes = ["Clôturer le lot", "Ne pas imprimer", "Remettre à l'impression"],
            Rang = 2,
            // Un document barré reste dans son lot ouvert et se rétablit (OPE_LISTE_LOT._TraiterEditionsCochees, :53-78) : « Ne pas
            // imprimer » est donc EnCours, cochable, et non un état figé (commun.tsx:78) qui ôterait sa case à l'écran.
            Etats =
            [
                .. EtatsEcran.Trois("À clôturer", "Ne pas imprimer", "Clôturé"), new EtatEcran("EnAttente", "Imprimante à choisir", "en-attente"),
            ],
            Groupes = ["Lots ouverts", "Documents des lots"],
            Sections =
            [
                Marche("Imprimante", "Imprimante obligatoire pour clôturer : sans elle, le lot ne se ferme pas. Une imprimante interne d'Amitel est signalée, sans blocage.", true,
                    "Imprimante"),
                Marche("Documents du lot", "Chaque document du lot, avec son destinataire, ses exemplaires, son dossier et sa maquette. « Ne pas imprimer » le barre ; « Remettre à l'impression » le rétablit.", false,
                    "Ne pas imprimer", "Exemplaires"),
                Marche("Clôture", "Se joue sur la ligne du lot : les documents barrés quittent le lot, qui passe prêt à imprimer et disparaît de la liste d'attente.", true,
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
                Ligne("Notification de jugement", "Lot NOTIFICATIONS · Alpha Services SARL · créé le 04/03/2026 · 1 exemplaire · dossier 2026F00101 · ne pas imprimer", "Documents des lots", "EnCours"),
                Ligne("Convocation à l'audience", "Lot CONVOCATIONS · Bêta Industrie SAS · créé le 05/03/2026 · 2 exemplaires · dossier 2026F00102", "Documents des lots", "AFaire"),
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
            // Les documents numérisés du greffe (DOCUMENT_OCERISE, hors OCR différé : OPE_PIECENUM.cs:85-107), lus par
            // LecteurDocumentsOcerises.cs (lot 11). Forme Référentiel, pas Document (lot 11, reprise) : la liste de RIG v2 est une
            // grille dont le nom se saisit dans la ligne puis s'enregistre (TreeViewPiecesNum.NodeNom_ChangesApplied, :346-365 ;
            // OPE_PIECENUM_1.Modifier, :341-377), et dont une ligne se supprime (menu Suppression, :269-283) : les deux gestes qui
            // écrivent la table, EcrivainDocumentsOcerises.cs ; et « Dupliquer le document sélectionné » (FORM_NUM.cs:99-100,
            // PROC_NUM_PROC.Dupliquer :388-423), une copie en base, au menu de la ligne (Gestes, lot 11). Ajouter (scanner ou importer
            // un fichier) et les traitements de fichier (export sur disque, PDF_WRITER, pages blanches, fusion Aspose) ne sont pas
            // repris (proc_num.md, Détail OPE).
            Forme = FormeEcran.Referentiel,
            // Le nom sans extension (OPE_PIECENUM_10.cs:105), le format de l'édition (PROC_NUM_PROC._Editer), pages, taille, date,
            // affaire : ce que la ligne V2 montre (OPE_PIECENUM_1.cs:121-136).
            Colonnes = ["Nom", "Format", Champ.Nombre("Pages"), "Taille", Champ.Date("Créé le"), "Affaire"],
            Recherche = "Rechercher un document, un titre, une date…",
            Criteres = ["Format (PDF ou TIF)", "Titre", "Nombre de pages"],
            Valider = "Enregistrer les noms de fichiers",
            Gestes = ["Dupliquer"],
            Rang = 2,
            // En cours : le document qu'un processus tient (DOCOC_ID_UTLST_EN_COURS) ; la V2 n'a pas d'état « enregistré ».
            Etats = EtatsEcran.Trois("Disponible", "En cours", "Enregistré"),
            Groupes = ["PDF", "TIF"],
            Sections =
            [
                Marche("Document", "Le document de la liste ; seul son nom se saisit, sans extension : RIG v2 remet celle du document.", true,
                    "Nom", "Format", Champ.Nombre("Pages"), "Taille", Champ.Date("Créé le"), "Affaire"),
                Marche("Traitements du fichier", "Ouvrir, modifier dans l'éditeur PDF ou TIF, supprimer les pages blanches, fusionner, enregistrer sous : des traitements du fichier lui-même, faits sur le poste par RIG v2. Dupliquer, au menu de la ligne, crée la copie « <nom>-copie » dans la liste.", false),
            ],
            Lignes =
            [
                Ligne("Scan 2026-0301", "PDF · 12 pages · créé le 02/03/2026", "PDF", "AFaire"),
                Ligne("Scan 2026-0302", "PDF · 4 pages · en cours · processus NUM", "PDF", "EnCours"),
                Ligne("Scan 2026-0303", "PDF · 9 pages · affaire 2026J00012", "PDF", "AFaire"),
                Ligne("Scan 2026-0305", "PDF · 2 pages · en cours · processus FUSION", "PDF", "EnCours"),
                Ligne("Scan 2026-0306", "TIF · 3 pages · créé le 06/03/2026", "TIF", "AFaire"),
                Ligne("Scan 2026-0307", "TIF · 5 pages · en cours · processus NUM", "TIF", "EnCours"),
                Ligne("Scan 2026-0308", "TIF · 1 page · créé le 09/03/2026", "TIF", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_TRANSDOC", Titre = "Transmission", Sujet = "documents dématérialisés",
            // Forme Référentiel, pas File (lot 11) : la V2 est une grille dont le numéro d'affaire se saisit dans la colonne, puis
            // Actualiser l'enregistre (OPE_LISTE_FLUX_DOC.SauveNumAffaire, :586-622 ; proc_transdoc.md, Détail OPE). Rien d'autre ne
            // s'écrit : ni ajout, ni suppression. Lecteur et geste : LecteurTransmissionDocuments.cs, EcrivainTransmissionDocuments.cs.
            Forme = FormeEcran.Referentiel,
            // Les colonnes de la grille V2 (_InitDGV : colDate, colDemande, colLibelle, colNumRole, colNumeroSIREN, colTypeDocument).
            Colonnes = ["Document", Champ.Date("Reçu le"), "Type de document", "Numéro d'affaire", "SIREN", "Demande"],
            Recherche = "Rechercher un document, un numéro d'affaire, un SIREN…",
            Criteres = ["Tribunal digital", "Coffre-fort numérique", "Numéro d'affaire"],
            Valider = "Enregistrer les modifications",
            Rang = 2,
            Etats = EtatsEcran.Trois("À transmettre", "Numéro saisi", "Enregistré"),
            Groupes = ["Tribunal digital", "Coffre-fort numérique"],
            Sections =
            [
                Marche("Document", "Le document reçu et sa demande ; seul le numéro d'affaire se saisit (la colonne éditable de RIG v2).", true,
                    "Document", Champ.Date("Reçu le"), "Type de document", "Numéro d'affaire", "SIREN", "Demande"),
                Marche("Visualisation", "Voir le document, ou lancer la visualisation judiciaire de son instance ou de sa procédure : RIG v2 n'écrit rien.", false),
            ],
            Lignes =
            [
                Ligne("Document DOC-2026-0101", "Dépôt du 02/03/2026 · KBIS · rôle 2026F00901 · SIREN 900 100 001", "Tribunal digital", "AFaire"),
                Ligne("Document DOC-2026-0102", "Dépôt du 03/03/2026 · acte · rôle 2026F00902", "Tribunal digital", "EnCours"),
                Ligne("Document DOC-2026-0104", "Dépôt du 04/03/2026 · KBIS · numéro à saisir", "Tribunal digital", "AFaire"),
                Ligne("Document DOC-2026-0105", "Dépôt du 05/03/2026 · pièce · à transmettre", "Coffre-fort numérique", "AFaire"),
                Ligne("Document DOC-2026-0106", "Dépôt du 06/03/2026 · pièce · numéro saisi", "Coffre-fort numérique", "EnCours"),
                Ligne("Document DOC-2026-0107", "Dépôt du 09/03/2026 · pièce · à transmettre", "Coffre-fort numérique", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_BORDLR", Titre = "Bordereaux LR", Sujet = "lettres recommandées",
            // Une ligne par bordereau. Sans période : les bordereaux déjà édités (demandes BORDLR de RIG v2, éditions
            // « Bordereau LRAR-n » / « Bordereau LR-n »). Avec une période : le bordereau à éditer de chaque type et le nombre
            // de plis que RIG v2 recompte à chaque saisie (MetierProcBordlr.GetCountDemandeEdition, :86-92). Lecteur et geste :
            // LecteurBordereauxLr.cs, EcrivainBordereauxLr.cs (lot 11). La case « Mes recommandés » de la V2 n'est pas reprise :
            // la lecture ne connaît pas l'agent ; son identifiant se saisit dans « Utilisateur ».
            Forme = FormeEcran.Etat,
            // Les champs des deux OPE de saisie (OPE_BORDLR_AVEC_AR.cs / OPE_BORDLR_SANS_AR.cs : dates, heures, utilisateur) ;
            // « avec ou sans AR » est l'étape de la V2 : ici, le groupe de la ligne.
            Parametres = [Champ.Periode("Période"), "Heure de début", "Heure de fin", "Utilisateur"],
            Colonnes = ["Bordereau", "Période", "Utilisateur", Champ.Nombre("Nombre de plis"), "Édition"],
            // VALIDER de RIG v2 : une demande d'édition BORD_LRAR ou BORD_LR par type (DoGenererLesDemandesEditions).
            Gestes = ["Éditer le bordereau"],
            Recherche = "Rechercher un bordereau, un utilisateur…",
            // Les critères sont les Paramètres, au libellé près : c'est ce que l'État envoie à la base.
            Criteres = ["Période", "Heure de début", "Heure de fin", "Utilisateur"],
            Valider = "Éditer le bordereau",
            Rang = 2,
            Etats = EtatsEcran.Trois("À éditer", "En cours", "Édité"),
            Groupes = ["Avec accusé de réception", "Sans accusé de réception"],
            Sections =
            [
                Marche("Période", "Date et heure de début, date et heure de fin. Sans heure, la plage couvre les journées entières. La date de début doit précéder la date de fin.", true,
                    "Date de début", "Heure de début", "Date de fin", "Heure de fin"),
                Marche("Utilisateur", "Tous les utilisateurs ayant émis, ou l'identifiant de l'un d'eux.", false,
                    "Utilisateur"),
                Marche("Bordereau avec AR", "Lettres avec accusé de réception (LRAR). Un pli avec AR ne se mélange jamais avec un pli sans AR.", false,
                    "Case AR", "Coordonnées de retour"),
                Marche("Bordereau sans AR", "Lettres sans accusé de réception (LR). Une fois la numérotation LRN démarrée, ce bordereau passe en consultation.", false,
                    "Bordereau simplifié"),
                Marche("Édition", "Édition par le service d'édition de RIG v2. Aucun pli : « Validation inutile vous n'avez rien a éditer. » À partir de 1 000 plis, l'édition est refusée : scinder la période.", true,
                    "Nombre de plis", "Bordereau"),
            ],
            Lignes =
            [
                Ligne("Bordereau LRAR-1 à éditer", "du 02/03/2026 au 02/03/2026 · 14 plis · tous les utilisateurs", "Avec accusé de réception", "AFaire"),
                Ligne("Bordereau LRAR-1", "créé le 03/03/2026 · 9 plis · en file d'édition", "Avec accusé de réception", "EnCours"),
                Ligne("Bordereau LRAR-1", "créé le 04/03/2026 · 22 plis · édité", "Avec accusé de réception", "Validee"),
                Ligne("Bordereau LR-1 à éditer", "du 02/03/2026 au 02/03/2026 · 31 plis · utilisateur 1", "Sans accusé de réception", "AFaire"),
                Ligne("Bordereau LR-1", "créé le 04/03/2026 · 18 plis · édité", "Sans accusé de réception", "Validee"),
                Ligne("Bordereau LR-1", "créé le 05/03/2026 · 4 plis · en file d'édition", "Sans accusé de réception", "EnCours"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_RETOURLRAR", Titre = "Retours LRAR", Sujet = "retours de recommandés",
            // Forme Opération, pas File (lot 11) : l'écran V2 n'a pas de liste, on scanne UN pli, on choisit sa modalité et sa date,
            // puis l'écran les écrit (ETP_RETOURLRAR_RECEPTION\OPE_RETOURLRAR_RECEPTION.cs, recept :105-157, Reception :705-748).
            // Choisir (le pli, cherché par son code-barre dans la base), Conséquences (la modalité et la date), Confirmer.
            // Lecteur et geste : LecteurRetoursLrar.cs, EcrivainRetoursLrar.cs (lot 11).
            Forme = FormeEcran.Operation,
            Recherche = "Scanner ou saisir le code-barre d'un pli…",
            Criteres = ["Code-barre du pli"],
            Valider = "Enregistrer le retour",
            Rang = 2,
            Etats = EtatsEcran.Trois("À saisir", "Date à saisir", "Retour enregistré"),
            Groupes = ["Reçus", "NPAI, refusés ou incomplets", "Non réclamés", "Non envoyés", "Sans retour"],
            Sections =
            [
                // Les boutons radio de la V2, à leurs libellés (eTypeReceptionLrar, Description), « Réception » par défaut (:56).
                Marche("Retour", "La modalité et la date du retour. Sans date, cocher « Date non renseignée » : la V2 refuse sinon.", true,
                    Champ.Liste("Modalité de retour", "Réception", "NPAI", "Non réclamé", "Pli non envoyé", "Refusé", "Incomplet"),
                    Champ.Date("Date de réception"), Champ.Case("Date non renseignée")),
                Marche("Retour déjà saisi", "Un pli dont le retour est saisi sans date se corrige ici ; la V2 demande alors de confirmer l'écrasement des anciennes données.", false),
                Marche("Remise en émission", "Si la modalité n'est pas à gérer (« Non envoyé »), les demandes d'édition du pli repartent en file d'émission.", false),
            ],
            Lignes =
            [
                Ligne("Pli 2026-LRAR-0101", "Convocation · émis le 02/03/2026 · aucun retour saisi", "Sans retour", "AFaire"),
                Ligne("Pli 2026-LRAR-0102", "Incomplet reçu · date non précisée sur l'imprimé", "NPAI, refusés ou incomplets", "EnCours"),
                Ligne("Pli 2026-LRAR-0103", "Reçu le 04/03/2026", "Reçus", "Validee"),
                Ligne("Pli 2026-LRAR-0104", "NPAI reçu le 05/03/2026", "NPAI, refusés ou incomplets", "Validee"),
                Ligne("Pli 2026-LRAR-0106", "Non réclamé reçu le 06/03/2026", "Non réclamés", "Validee"),
                Ligne("Pli 2026-LRAR-0107", "Non envoyé le 06/03/2026 · demandes d'édition remises en file d'émission", "Non envoyés", "Validee"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_ENV_JAL", Titre = "Envoi JAL / BODACC", Sujet = "annonces légales",
            // Forme Opération, pas File (lot 11) : la V2 cherche d'abord (date de début obligatoire, OPE_RECH_ANNONCES.cs:331-341),
            // puis coche des annonces et génère ; une File n'envoie pas de critère au serveur. Le menu l'ouvre en mode JAL
            // (PROC_ENV_JAL.cs:52-69). Lecteur : LecteurEnvoiJal.cs (lot 11), à envoyer puis déjà envoyées. « Générer le bordereau » :
            // EcrivainEnvoiJal.cs, le mode EDI de la V2 (JAL_ENVOIE_EDI = 1 dans RIG_DEV) en un geste -- Générer puis Envoyer
            // (OPE_A_ENVOYER.cs:189-334, :1071-1170) : bordereau RBOE, publicités envoyées ; le fichier XML n'est pas produit (simulé,
            // dit à l'écran). Le document Word hors EDI (:493-597) n'est pas repris. « Supprimer le bordereau », le bouton des
            // annonces déjà envoyées (OPE_DEJA_ENVOYER.cs:69-84, AnnulerBordereaux_JAL :305-376) : le second geste, offert au temps
            // Confirmer (Gestes, lot 11) -- les annonces du bordereau redeviennent à envoyer, le bordereau est supprimé.
            Forme = FormeEcran.Operation,
            Recherche = "Numéro de bordereau…",
            Criteres = ["Date de début", "Date de fin", "Destinataire (JAL)", "Numéro de PC ou PRV", "Numéro de bordereau"],
            Valider = "Générer le bordereau",
            Gestes = ["Générer le bordereau", "Supprimer le bordereau"],
            Rang = 2,
            Etats = EtatsEcran.Trois("À envoyer", "Bordereau généré", "Envoyée"),
            Groupes = ["Procédures collectives", "Préventions"],
            Sections =
            [
                // La période de la recherche est dans les critères ; celle du BORDEREAU (BRDRJ_DATE_MIN / MAX, et les publicités
                // générées : GetPubtoEdit, LigneJal.cs:298) se saisit ici, car une Opération n'envoie que ses sections au geste.
                // fix-ok: mesuré dans web/src/ecrans/formes/FormeOperation.tsx:57-59 -- le geste Confirmer porte `remplies` (les
                // champs des sections) et jamais les critères de recherche : sans cette section, l'écrivain ignorait la période.
                // Vides : les dates que la V2 pré-remplit (JAL_DATE_DEBUT_ENVOI, puis aujourd'hui, OPE_RECH_ANNONCES.cs:275-293).
                Marche("Période du bordereau", "Les annonces générées sont celles des évènements cochés, encore à envoyer, dont l'envoi est prévu dans cette période (la fin est comprise à 0 h, comme RIG v2). Vide : la période que RIG v2 pré-remplit.", false,
                    Champ.Date("Date de début"), Champ.Date("Date de fin")),
                Marche("Envoi électronique", "Un seul journal par envoi. Le bordereau est numéroté et les annonces passent à l'état envoyé ; le fichier XML EDI n'est pas produit par RIG v3 et rien n'est transmis (simulé).", false),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Procédure PC 2026P00301 · publicité du 02/03/2026", "Procédures collectives", "AFaire"),
                Ligne("Bêta Industrie SAS", "Procédure PC 2026P00302 · publicité sélectionnée", "Procédures collectives", "EnCours"),
                Ligne("Gamma Conseil SARL", "Procédure PC 2026P00303 · bordereau 2026-BDX-01 généré", "Procédures collectives", "EnCours"),
                Ligne("Delta Transports SAS", "Procédure PC 2026P00304 · envoyée le 04/03/2026", "Procédures collectives", "Validee"),
                Ligne("Epsilon Habitat SARL", "Prévention PRV 2026R00401 · publicité à envoyer", "Préventions", "AFaire"),
                Ligne("Zêta Commerce SAS", "Prévention PRV 2026R00402 · sélectionnée", "Préventions", "EnCours"),
                Ligne("Êta Boulangerie SARL", "Prévention PRV 2026R00403 · envoyée le 06/03/2026", "Préventions", "Validee"),
                Ligne("Thêta Import SAS", "Prévention PRV 2026R00404 · à envoyer", "Préventions", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_GESTBOD", Titre = "Publications BODACC", Sujet = "annonces BODACC",
            // Une ligne par annonce BODACC. Sans critère : les rejets (état 9, rectifier) et ceux remis à renvoyer (état 3), la liste
            // que RIG v2 ouvre sans date (OPE_LST_REJETS.MajSelectionBodacc, :55-90 ; proc_gestbod.md, Détail OPE). Avec une date (d'EP,
            // de parution ou d'envoi) ou un numéro de parution : aussi les certificats de parution (OPE_CERTIF_BOD.cs:80-117) et les
            // contrôles non conformes (OPE_LST_CTRLNC.cs:50-81). Forme Opération (lot 11, reprise), comme « Envoi JAL / BODACC » : la
            // seule qui envoie des critères ET des lignes cochées. Lecteur et geste : LecteurPublicationsBodacc.cs,
            // EcrivainPublicationsBodacc.cs.
            Forme = FormeEcran.Operation,
            Recherche = "Numéro de parution…",
            // Les boutons radio GestionBodacc (OPE_BASE_BOD.cs:12, :44-47) et les deux dates « du » / « au » de la V2.
            Criteres = ["Critère de date", "Date du", "Date au", "Numéro de parution"],
            // Les deux colonnes à cocher de RIG v2, offertes au temps Confirmer (gestes nommés, lot 11) : « Valider » -- un rejet
            // passe à renvoyer (état 3, OPE_LST_LISTE_REJ.DoSauver, :51-57), un certificat est vérifié (état 6, OPE_LST_CERTIF_BOD,
            // :57-59) ; « Éditer » -- les éditions de VALIDER (DoGenererLesEditions) : la liste des rejets (LISTE_REJETS_BODACC,
            // OPE_LST_LISTE_REJ.cs:62-120), la liste des contrôles non conformes (LISTE_BODACC_QUALITE, OPE_LST_CTRLNC_BOD.cs:49-103),
            // le certificat de parution par destinataire du réglage CERTIF_BOD_DEST (CERTIF_BOD / CERTIF_BODPR, OPE_LST_CERTIF_BOD.cs:61-101).
            // Écrivain : EcrivainPublicationsBodacc.cs.
            Valider = "Valider",
            Gestes = ["Valider", "Éditer"],
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À traiter", "Vérifiée", "Certifiée"), new EtatEcran("EnAttente", "À renvoyer", "en-attente")],
            Groupes = ["Certificats de parution", "Rejets", "Contrôles non conformes"],
            Sections =
            [
                Marche("Renvoi des rejets", "Un rejet coché passe à renvoyer (état 3) : le prochain envoi BODACC le reprend.", false),
                Marche("Vérification des parutions", "Un certificat de parution coché passe à l'état vérifié (6). Un contrôle non conforme ne se valide pas : RIG v2 n'en édite que la liste. Une validation sans ligne cochée est refusée.", false),
                Marche("Éditions", "« Éditer » place dans la file des éditions la liste des rejets ou celle des contrôles non conformes — toute la liste de la recherche, sa période dans le titre, comme RIG v2 ; cocher une de ses annonces la choisit —, ou le certificat de parution de chaque annonce cochée, un par destinataire du réglage CERTIF_BOD_DEST (mandataires de la procédure, document interne). L'impression et l'envoi au coffre-fort sont faits par le service d'édition de RIG v2 (simulés ici).", false),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL", "Parution n° 2026-041 · EP du 02/03/2026", "Certificats de parution", "AFaire"),
                Ligne("Gamma Conseil SARL", "Parution n° 2026-042 · parution vérifiée", "Certificats de parution", "EnCours"),
                Ligne("Epsilon Habitat SARL", "Rejet du 05/03/2026 · à traiter", "Rejets", "AFaire"),
                Ligne("Zêta Commerce SAS", "Rejet du 06/03/2026 · à renvoyer au prochain envoi", "Rejets", "EnAttente"),
                Ligne("Êta Boulangerie SARL", "Contrôle de qualité · à traiter", "Contrôles non conformes", "AFaire"),
                Ligne("Thêta Import SAS", "Rejet du 07/03/2026 · à traiter", "Rejets", "AFaire"),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_PAPERCUT", Titre = "File d'impression", Sujet = "travaux d'impression",
            // Reste en EXEMPLE (lot 11) : la file vit dans la table PAPERCUT de la base commune COMMUN_RIG
            // ([COMMUN_RIG].[dbo].[PAPERCUT], proc_papercut.md, Détail OPE : vue globale et vidage), absente de la base V3 du greffe
            // (et de RIG_DEV : aucune table PAPERCUT, lu le 27/11/2026) ; « Imprimer » appelle le service WCF d'impression PaperCut.
            // L'écran le DIT : chaque ligne d'exemple nomme la table absente (le descripteur n'a pas d'autre texte affiché au-dessus
            // de la liste, et la mention « exemple » seule ne disait pas pourquoi).
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
                Ligne("Imprimante Greffe 1", "3 travaux · 14 feuilles · utilisateur 1 · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Greffe", "AFaire"),
                Ligne("Imprimante Greffe 2", "1 travail · 2 feuilles · utilisateur 1 · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Greffe", "EnCours", 0),
                Ligne("Imprimante Greffe 3", "5 travaux · 30 feuilles · en impression · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Greffe", "EnCours", 0, 1),
                Ligne("Imprimante Accueil", "2 travaux · 6 feuilles · imprimée · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Accueil", "Validee", 0, 1),
                Ligne("Imprimante Salle A", "4 travaux · 22 feuilles · utilisateur 2 · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Accueil", "AFaire"),
                Ligne("Imprimante Salle B", "1 travail · 1 feuille · imprimée · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Accueil", "Validee", 0, 1),
                Ligne("Imprimante Salle C", "8 travaux · 40 feuilles · à vider · exemple : table PAPERCUT de COMMUN_RIG, absente de la base du greffe", "Accueil", "EnCours", 0),
            ],
        },

        new DescripteurEcran
        {
            Code = "PROC_LISTEEXCEL", Titre = "Export Excel", Sujet = "listes d'instances",
            // Lecteur : LecteurListesExcel.cs (lot 11) -- les types de liste d'instances du greffe, leurs colonnes et leurs listes
            // paramétrées (PARAMETRAGE_LISTE_XLS*) ; avec une date de début, le COMPTAGE de RIG v2 par type (ExcelInstance.cs:9-104,
            // la requête de PARAMETRAGE_LISTE réécrite sous v2) et le blocage au-delà de 65 536 lignes (OPE_RECHERCHE.cs:466-472).
            // L'export reste en démonstration : EXECUTE_EXCEL ouvre un classeur non enregistré, sans dossier choisi (Excel.cs:551),
            // avec des colonnes qui sont des expressions SQL stockées dans la base.
            Forme = FormeEcran.Etat,
            // Les critères des listes d'instances (OPE_INSTANCE, OPE_TYPE_INSTANCE, OPE_CODE_EVE_PRO ; OPE_RECHERCHE.cs:78-99).
            // Code d'acte et nature d'affaire du groupement ne se saisissent pas : ce sont deux réglages du greffe (OPE_INSTANCE.cs:78-79).
            Parametres = ["Type de liste", Champ.Periode("Période"), "Types d'instance", "Codes d'événement",
                          Champ.Case("Sans sursis à statuer"), Champ.Case("Sans ADD expertise"), Champ.Case("Sans ADD")],
            Colonnes = ["Liste", Champ.Nombre("Nombre de lignes")],
            Gestes = ["Lancer l'export Excel"],
            Recherche = "Rechercher un type de liste…",
            // Les critères sont les Paramètres, au libellé près : c'est ce que l'État envoie à la base.
            Criteres = ["Type de liste", "Période", "Types d'instance", "Codes d'événement", "Sans sursis à statuer", "Sans ADD expertise", "Sans ADD"],
            Valider = "Lancer l'export Excel",
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À compter", "Comptée", "Exportée"), new EtatEcran("EnAttente", "Bloquée : trop de lignes", "en-attente")],
            Groupes = ["Instances", "Groupements", "Affaires et citations"],
            Sections =
            [
                Marche("Type de liste", "Le type fixe les critères proposés : instances enrôlées, avec ou sans décision, avec événement passé ou futur, groupements, affaires sans décision définitive, citations signifiées.", true,
                    "Type de liste"),
                Marche("Critères", "Période et critères du type choisi : types d'instance (RA, RC, MD…), débuts de codes d'événement séparés par « ; » pour les événements, cases sans sursis et sans ADD pour les décisions. Un groupement se règle par deux réglages du greffe.", true,
                    "Période", "Types d'instance", "Codes d'événement"),
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
