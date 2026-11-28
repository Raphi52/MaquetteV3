namespace RigV3Desktop.Pont.EcransGeneres;

// LES FONCTIONS RETROUVEES (24/11/2026, conv-2142) : onze ecrans de RIG v2 qui n'avaient AUCUNE
// place dans la V3. Trouves en croisant l'inventaire des touches de la V2
// (Pont/InventaireTouchesV2.cs, 77 ecrans) et les fiches Documentation\reference\proc avec le menu :
// 22 ecrans de la V2 sans equivalent. Neuf etaient deja servis par un ecran de la V3 sous un autre
// code (Pont/EcranCorrespondances.cs, EcransRemplaces), trois n'etaient pas des ecrans de greffe
// (essais de developpeur, outil d'exploitation, menu configurable). Restent les onze ci-dessous.
//
// Consigne de l'utilisateur : « on ne reprend pas les vues telles quelles, on groupe ce qui peut
// l'etre ». Aucune n'a donc d'entree a elle seule dans le menu sauf les imprimantes : chacune entre
// dans un ECRAN REUNI (Pont/MenuAccueil.cs, EntreeMenu.Reunit) avec les procedures qui traitent
// le meme objet (les comptes annuels avec les depots au registre, les radiations d'office avec les
// relances, les actes web et les Kbis XML avec les actes rejetes...).
//
// Chaque descripteur est FONDE sur RIG v2, comme ceux des verbes : sa fiche (« Detail OPE » faisant
// foi) quand elle existe, sinon les libelles et messages de son formulaire (source citee en tete).
// Les lignes sont une demonstration ANONYME (noms fictifs).
public sealed class DomaineFonctionsRetrouvees : IDomaineEcrans
{
    public IEnumerable<DescripteurEcran> Ecrans() =>
    [
        // proc_dca.md (Detail OPE : ETP_EXERCICES, regles coeur 1 a 4, verification des pieces DCADEMAT).
        new DescripteurEcran
        {
            Code = "PROC_DCA", Titre = "Comptes annuels", Sujet = "dépôts de comptes annuels",
            Forme = FormeEcran.Saisie,
            Recherche = "Rechercher un dossier RCS, un numéro de gestion, un SIREN…",
            Criteres = ["Numéro de gestion ou SIREN", "Exercice", "Origine : guichet, INPI, CREFO"],
            Valider = "Valider le dépôt", // proc_dca.md, FORM_DCA : bouton VALIDER, état Validee (dépôt accepté).
            // « Valider le dépôt » au guichet écrit l'exercice, le dépôt (BILA, CONFI, RESUL), l'événement DCA et la demande validée
            // (ETP_EXERCICES.cs:365-615, DossierRCS.AddDCA) ; facture, éditions et BODACC restent à RIG v2 (lot 16 du plan « RIG v3 100 %
            // fonctionnel » : EcrivainComptesAnnuels.cs). Les deux confidentialités sont des cases de la grille V2 (ExerciceDca.cs:50-55) ; la
            // date d'arrivée est la saisie obligatoire de OPE_ACTES, dans l'étape des exercices (OPE_ACTES.Designer.cs:104, contrôles :153-163).
            Rang = 1,
            Etats = [.. EtatsEcran.Trois("À enregistrer", "En cours", "Déposé"),
                     new EtatEcran("EnAttente", "Réclamation", "en-attente"), new EtatEcran("Rejetee", "Refusé", "rejetee")],
            Groupes = ["Guichet", "Dématérialisé (INPI, CREFO)"],
            Marches =
            [
                // Seules les saisies que lit EcrivainComptesAnnuels.cs (SaisiesLues) : le SIREN et la dénomination de RIG v2 affichent
                // le dossier trouvé, l'« Exercice » est la ligne de la grille V2, que la date de clôture désigne ici ; une saisie non lue
                // fait refuser le dépôt (PROC_DCA.gestes bloc 10).
                // fix-ok: mesuré le 27/11/2026 (banc-r5-rouge.log, PROC_DCA.gestes bloc 10 « obtenu fait 200 ») -- l'écran offrait des champs
                // que l'écrivain ne lit pas et annonçait une grille d'exercices proposés que la V3 n'a pas : le dépôt s'écrivait en les ignorant.
                Marche("Dossier RCS", "Le dossier se retrouve par son numéro de gestion. En dépôt dématérialisé, le numéro arrive avec le flux et ne se modifie pas. La recherche par SIREN de RIG v2 n'est pas reprise par la V3.", true,
                    "Numéro de gestion"),
                Marche("Exercices", "L'exercice se désigne par sa date de clôture : l'exercice non déposé de cette date, sinon un exercice neuf. La date d'arrivée est obligatoire et jamais après le jour ; à plus de trois mois, elle est signalée. Un exercice sous injonction de déposer ne se dépose pas ici : RIG v2 y traite le retrait du rôle ou le dépôt hors délai, que la V3 ne reprend pas. Non repris par la V3, à saisir par l'agent : la liste des exercices non déposés, l'exercice coché d'office, la confidentialité du dernier dépôt et la date d'arrivée pré-remplie.", true,
                    "Date de clôture", Champ.Case("Bilan confidentiel"), Champ.Case("Résultat confidentiel"), "Date d'arrivée"),
                Marche("Pièces jointes", "En dépôt dématérialisé : le bilan toujours ; la déclaration de confidentialité, le compte de résultat confidentiel ou le bilan simplifié selon le niveau choisi. Une pièce absente bloque si le greffe l'a réglé ainsi, sinon elle est signalée. Le dépôt dématérialisé n'est pas repris par la V3 : une pièce saisie ici fait refuser le dépôt.", false,
                    "Bilan", "Déclaration de confidentialité", "Compte de résultat confidentiel", "Bilan simplifié"),
                // Comme « Facturation » de PROC_DAS (Ouvrir.cs) : l'étape de RIG v2 (etpFactuGestcom : articles, client d'expédition,
                // encaissement) n'est pas écrite par la V3 (EcrivainComptesAnnuels.cs, NON REPRIS) ; des champs saisissables y seraient
                // exigés par l'écran puis ignorés. Le tableau des articles la montre sans la faire saisir.
                Marche("Facturation", "Un dépôt accepté est toujours facturé ; une réclamation ou un refus ne l'est que si le greffe le prévoit. Un dépôt venu de l'INPI est toujours facturé. Client, client d'expédition et encaissement : non repris par la V3, le dépôt est validé sans facture.", true,
                    Champ.Tableau("Articles de facturation")),
                // Récapitulatif V2 : après Validee/Reclamee/Refusee, etpRcs et etpExercices passent en lecture seule
                // (PROC_DCA.cs:603-606, proc_dca.md:421) ; l'écran V3 n'a pas ce mode, il se vide après « Créer ».
                Marche("Réclamation ou refus", "Le motif et la date de réception. La réclamation et le refus ne sont pas repris par la V3 : un motif saisi ici fait refuser le dépôt, à traiter dans RIG v2. Le récapitulatif de RIG v2 (dossier et exercices en lecture seule après la validation) n'est pas repris non plus : la V3 revient à une saisie vide, et le dépôt enregistré se relit dans la liste.", false,
                    "Motif", "Date de réception"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · SIREN 900 300 001", "Exercice 2025 clos le 31/12/2025 · coché d'office", "Guichet", "AFaire"),
                Ligne("Bêta Industrie SAS · SIREN 900 300 002", "Exercice 2025 · facturation à faire", "Guichet", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · SIREN 900 300 003", "Exercice 2025 · déposé le 12/03/2026", "Guichet", "Validee", 0, 1, 3),
                Ligne("Delta Transports SAS · SIREN 900 300 004", "Exercices 2024 et 2025 · résultat confidentiel", "Guichet", "EnCours", 0),
                Ligne("Epsilon Habitat SARL · SIREN 900 300 005", "Flux INPI · bilan et déclaration de confidentialité reçus", "Dématérialisé (INPI, CREFO)", "AFaire"),
                Ligne("Zêta Commerce SAS · SIREN 900 300 006", "Flux INPI · bilan simplifié manquant", "Dématérialisé (INPI, CREFO)", "EnCours", 0, 1),
                Ligne("Êta Boulangerie SARL · SIREN 900 300 007", "Flux CREFO · déposé le 14/03/2026", "Dématérialisé (INPI, CREFO)", "Validee", 0, 1, 2, 3),
                Ligne("Thêta Import SAS · SIREN 900 300 008", "Réclamation reçue le 10/03/2026", "Guichet", "EnAttente", 0, 1, 4),
                Ligne("Iota Études SARL · SIREN 900 300 009", "Refusé : comptes non approuvés", "Dématérialisé (INPI, CREFO)", "Rejetee", 0, 1, 4),
            ],
        },

        // Pas de fiche : RCS\Exe\PROC_ROFSurveillances (FrmProcROFSurveillances : surveillances S123-124,
        // S123-131, SSAC2A ; relances dont la date limite est passee ; « Lettre au Procureur »).
        new DescripteurEcran
        {
            Code = "PROC_ROFSURVEILLANCES", Titre = "Radiations d'office", Sujet = "dossiers à radier d'office",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, un numéro de gestion, un SIREN…",
            Criteres = ["Surveillance", "Date d'expiration de la relance", "Exclure les dossiers provoquant une notification"],
            Valider = "Éditer la lettre au procureur", // FrmProcROFSurveillances.Designer.cs : « Lettre au Procureur ».
            // Le bouton RADIATION de la barre de la V2 (_radiationOffice, FrmProcROFSurveillances.cs:383-534) : la demande ROF du dossier,
            // posee interrompue, a reprendre dans RIG v2 (lot 16 du plan « RIG v3 100 % fonctionnel » : EcrivainRadiationsOffice.cs).
            // La lettre au procureur est une edition de cette radiation (l. 487-490) : elle part avec elle, dans RIG v2.
            Gestes = ["Radier d'office"],
            Rang = 5,
            Etats = EtatsEcran.Trois("À radier", "En cours", "Lettre éditée"),
            Groupes = ["Dissolution par survenance du terme", "Dissolution depuis plus de trois ans", "Sans activité"],
            Sections =
            [
                Marche("Surveillance", "La surveillance choisie liste les dossiers dont la relance a expiré, sans procédure collective ni cessation : dissolution par survenance du terme (S123-124), dissolution depuis plus de trois ans (S123-131), sans activité.", true,
                    "Surveillance", "Date d'expiration de la relance", "Ordre de tri"),
                Marche("Dossier", "Les informations du dossier et de ses relances. Un dossier modifié après la dernière relance est signalé : « Le dossier a été mis à jour après la dernière relance ».", true,
                    "Numéro de gestion", "SIREN", "Désignation", "Relances"),
                Marche("Radiation", "Les informations de la radiation ; on peut écarter les dossiers qui provoqueraient une notification.", false,
                    "Date de radiation", "Exclure les notifications"),
                Marche("Lettre au procureur", "L'impression de la lettre au procureur, avec la signature et l'imprimante.", true,
                    "Imprimante", "Signature"),
            ],
            Lignes =
            [
                Ligne("Kappa Négoce SAS · 2001 B 00411", "Terme survenu le 02/01/2026 · relance expirée", "Dissolution par survenance du terme", "AFaire"),
                Ligne("Lambda Bâtiment SARL · 1998 B 00217", "Terme survenu le 15/11/2025 · dossier vérifié", "Dissolution par survenance du terme", "EnCours", 0, 1),
                Ligne("Mu Textile SAS · 2012 B 00930", "Dissoute le 03/02/2022 · relance expirée", "Dissolution depuis plus de trois ans", "AFaire"),
                Ligne("Nu Distribution SARL · 2009 B 00128", "Dissoute le 20/01/2022 · lettre éditée le 11/03/2026", "Dissolution depuis plus de trois ans", "Validee", 0, 1, 3),
                Ligne("Xi Conseil SARL · 2016 B 00552", "Mis à jour après la dernière relance", "Dissolution depuis plus de trois ans", "EnCours", 0),
                Ligne("Omicron Services EURL · 2018 B 00710", "Sans activité · relance expirée", "Sans activité", "AFaire"),
                Ligne("Pi Logistique SAS · 2015 B 00309", "Sans activité · lettre éditée le 12/03/2026", "Sans activité", "Validee", 0, 1, 2, 3),
            ],
        },

        // proc_actrej_cmd_web.md (Detail OPE : OPE_RESULTATS, menu contextuel Copier / Envoyer au VGED /
        // Afficher le detail de la commande / Annuler ; K-bis ; actes sans GUID_VIDIGED).
        new DescripteurEcran
        {
            Code = "PROC_ACTREJ_CMD_WEB", Titre = "Actes web non transmis", Sujet = "actes commandés par le web",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, un SIREN, un numéro de dépôt…",
            Criteres = ["N° dossier", "Siren", "Date de la commande"],
            Valider = "Envoyer au VGED", // OPE_RES.Designer.cs : tsmiSendToVged « Envoyer au VGED ».
            // « Annuler » du menu de la ligne (_tsmiAnnuler_Click, OPE_RESULTATS.cs:84-117) : la commande web de l'acte (lot 16 :
            // EcrivainActesWebNonTransmis.cs). « Envoyer au VGED » ouvre la GED dans un autre processus : pas une ecriture, pas offert.
            Gestes = ["Annuler"],
            Rang = 5,
            Etats = [.. EtatsEcran.Trois("À transmettre", "En cours", "Transmis"), new EtatEcran("Rejetee", "Annulé", "rejetee")],
            Groupes = ["Commandes du jour", "Commandes plus anciennes"],
            Sections =
            [
                Marche("Acte", "L'acte est déposé mais pas encore dans la GED : la commande web l'attend. La liste montre le dossier, le SIREN, la dénomination, le dépôt et la date de la commande.", true,
                    "N° dossier", "Siren", "Dénomination", "N° dépôt"),
                Marche("Détail de la commande", "Un double-clic ouvre la commande reçue par EDI, avec son adresse de livraison.", false,
                    "Commande", "Adresse de livraison"),
                Marche("Kbis", "Le Kbis du dossier s'ouvre, mis à jour depuis son XML.", false,
                    "Extrait Kbis"),
                Marche("Envoi à la GED", "Sur une seule ligne choisie : la GED du dépôt ou celle du dossier s'ouvre pour y ranger l'acte.", true,
                    "GED du dépôt", "GED du dossier"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · 2019 B 00101", "Dépôt 4410 · commande du 24/03/2026", "Commandes du jour", "AFaire"),
                Ligne("Bêta Industrie SAS · 2020 B 00102", "Dépôt 4411 · commande du 24/03/2026", "Commandes du jour", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · 2017 B 00103", "Dépôt 4412 · rangé dans la GED", "Commandes du jour", "Validee", 0, 3),
                Ligne("Delta Transports SAS · 2015 B 00104", "Dépôt 4380 · commande du 18/03/2026", "Commandes plus anciennes", "AFaire"),
                Ligne("Epsilon Habitat SARL · 2021 B 00105", "Dépôt 4371 · Kbis consulté", "Commandes plus anciennes", "EnCours", 0, 2),
                Ligne("Zêta Commerce SAS · 2018 B 00106", "Dépôt 4365 · rangé dans la GED", "Commandes plus anciennes", "Validee", 0, 1, 3),
                Ligne("Êta Boulangerie SARL · 2022 B 00107", "Dépôt 4350 · commande annulée", "Commandes plus anciennes", "Rejetee", 0),
            ],
        },

        // Pas de fiche : RCS\Exe\PROC_RejetsKbisXml (FrmRejetsKbisXml : types d'erreur Bloquantes /
        // Avertissements / Informations, dossiers vivants ou radies, historiques, « Extrait Kbis »).
        new DescripteurEcran
        {
            Code = "PROC_REJETSKBISXML", Titre = "Kbis XML rejetes", Sujet = "Kbis XML en erreur",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un dossier, une erreur…",
            Criteres = ["Types d'erreur : bloquantes, avertissements, informations", "Dossiers : vivants, radiés", "Historiques"],
            Valider = "Extrait Kbis", // FrmRejetsKbisXml.Designer.cs : « Extrait &Kbis », « Afficher à l'écran l'extrait kbis du kbis sélectionné ».
            // « Annuler le rejet sélectionné... » du menu de la ligne, en mode Amitel (tsmiAnnulerRejet_Click, FrmRejetsKbisXml.cs:159-184 ;
            // lot 16 : EcrivainKbisXmlRejetes.cs). L'extrait Kbis s'affiche : pas une ecriture, pas offert.
            Gestes = ["Annuler le rejet"],
            Rang = 5,
            Etats = EtatsEcran.Trois("À examiner", "En cours", "Examiné"),
            Groupes = ["Bloquantes", "Avertissements", "Informations"],
            Sections =
            [
                Marche("Rejet", "Chaque Kbis XML transmis revient avec ses erreurs, classées bloquantes, avertissements ou informations, avec ses dates de transmission et de retour.", true,
                    "Gestion", "Date de transmission", "Date de retour", "Erreur"),
                Marche("Dossier", "Les dossiers vivants ou radiés ; un dossier radié montre sa date de radiation.", false,
                    "Vivants", "Radiés", "Date de radiation"),
                Marche("Historique", "Les transmissions précédentes du même Kbis.", false,
                    "Historique"),
                Marche("Extrait Kbis", "L'extrait Kbis du Kbis choisi s'affiche. Introuvable : « Le kbis est inaccessible ou inexistant. Merci de contacter AMITEL pour signaler le problème ».", true,
                    "Extrait Kbis"),
            ],
            Lignes =
            [
                Ligne("Kappa Négoce SAS · 2001 B 00411", "Transmis le 20/03/2026 · adresse du siège absente", "Bloquantes", "AFaire"),
                Ligne("Lambda Bâtiment SARL · 1998 B 00217", "Transmis le 20/03/2026 · capital incohérent", "Bloquantes", "EnCours", 0, 1),
                Ligne("Mu Textile SAS · 2012 B 00930", "Transmis le 19/03/2026 · corrigé", "Bloquantes", "Validee", 0, 3),
                Ligne("Nu Distribution SARL · 2009 B 00128", "Transmis le 19/03/2026 · activité tronquée", "Avertissements", "AFaire"),
                Ligne("Xi Conseil SARL · 2016 B 00552", "Transmis le 18/03/2026 · deux transmissions", "Avertissements", "EnCours", 0, 2),
                Ligne("Omicron Services EURL · 2018 B 00710", "Radié le 02/02/2026 · retour sans erreur", "Informations", "Validee", 0, 1, 3),
            ],
        },

        // Pas de fiche : Judiciaire\Exe\PROC_EDI_RPJC_INS (FORM_EDI_RPJC_INS : « Traiter la demande »,
        // « Rejeter la demande », creation de mandataire et de structure, verrou par utilisateur).
        new DescripteurEcran
        {
            Code = "PROC_EDI_RPJC_INS", Titre = "EDI RPJC inscriptions", Sujet = "demandes d'inscription",
            Forme = FormeEcran.File,
            Recherche = "Rechercher un avocat, un CNBF, une structure…",
            Criteres = ["État de la demande", "Cnbf", "Date du traitement"],
            Valider = "Traiter la demande", // FORM_EDI_RPJC_INS.Designer.cs : « &Traiter la demande ».
            // Les deux gestes de la demande cochee (frmTrt.cs:79-80, VALIDER et REFUS l. 893-897) et leurs saisies : les mandataires et
            // structures des grilles de frmTrt nommes par leur abrege, le motif de frmRejet (Designer l. 144) -- lot 16 :
            // EcrivainInscriptionsRpjc.cs.
            Gestes = ["Traiter la demande", "Rejeter la demande"],
            // Une inscription deja acceptee se retraite (frmChoixTrt.cs:270-272, PROC_EDI_RPJC_INS.gestes bloc 11) : « Traiter » vise
            // aussi la ligne « Traitée ». Refusee : la V2 refuse de la rouvrir (l. 281-283), elle reste figee.
            EtatsGestes = new Dictionary<string, string[]> { ["Traiter la demande"] = ["AFaire", "EnCours", "Validee"] },
            Parametres = ["Mandataires traités", "Mandataire principal", "Structures traitées", Champ.Zone("Motif du rejet")],
            Rang = 7,
            Etats = [.. EtatsEcran.Trois("En attente", "En cours", "Traitée"), new EtatEcran("Rejetee", "Rejetée", "rejetee")],
            Groupes = ["Avocat seul", "Avocat et structure"],
            Sections =
            [
                Marche("Demande", "Choisir la demande à traiter. Ouverte par un autre agent, elle est refusée : « Cette demande d'inscription est en cours de traitement par l'utilisateur … » ; traitée entre-temps, il faut recharger la liste.", true,
                    "Auteur de la demande", "Adresse de l'auteur", "Cnbf"),
                Marche("Structure", "Rattacher l'avocat à sa structure ou la créer (« Création structure en la personne de … »). Une adresse non conforme Médiapost est signalée.", false,
                    "Cnbf structure", "Abrégé structure", "Adresse structure"),
                Marche("Mandataire", "Créer le mandataire ou retrouver le sien. Rendu inactif à l'inscription, il peut être réactivé ; un mandataire principal ne se coche pas.", true,
                    "Abrégé mandataire", "Adresse mandataire", "Adresses e-mail", "Actif"),
                Marche("Rejet", "« Rejeter la demande » avec sa cause, que l'on peut copier pour l'avocat.", false,
                    "Cause du rejet", "Date du rejet"),
            ],
            Lignes =
            [
                Ligne("Me Irène Ardoise", "Cnbf 0001 · reçue le 21/03/2026", "Avocat seul", "AFaire"),
                Ligne("Me Simon Bélier", "Cnbf 0002 · mandataire à créer", "Avocat seul", "EnCours", 0),
                Ligne("Me Olga Chantelle", "Cnbf 0003 · inscrite le 18/03/2026", "Avocat seul", "Validee", 0, 2),
                Ligne("Me Théo Dorval · Cabinet Kappa Avocats", "Cnbf 0004 · structure à créer", "Avocat et structure", "EnCours", 0, 1),
                Ligne("Me Lina Écorce · Lambda Conseil", "Cnbf 0005 · reçue le 22/03/2026", "Avocat et structure", "AFaire"),
                Ligne("Me Paul Faïence · Mu Juris", "Cnbf 0006 · inscrite avec sa structure", "Avocat et structure", "Validee", 0, 1, 2),
                Ligne("Me Rose Grès", "Cnbf 0007 · Cnbf inconnu du barreau", "Avocat seul", "Rejetee", 0, 3),
            ],
        },

        // proc_edi_rpjc_ren.md (Detail OPE : etats 1 / 2 / 9, OPE_INFO_DEMANDE.Valider, OPE_AUDIENCE,
        // verrou RPJCR_USER_EN_COURS, CNBF introuvable, audience initiale a venir).
        new DescripteurEcran
        {
            Code = "PROC_EDI_RPJC_REN", Titre = "EDI RPJC renvois", Sujet = "demandes de renvoi",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une affaire, un avocat, une audience…",
            Criteres = ["En attente", "Traitées : audience initiale du … au …", "200 demandes au plus"],
            Valider = "Valider", // proc_edi_rpjc_ren.md : DoSauver -> OPE_INFO_DEMANDE.Valider().
            // Les deux cases exclusives de la decision (OPE_AUDIENCE.Designer.cs l. 83, 92), jouees sur la demande cochee, et leurs
            // saisies (l. 102, 126, 137) ; l'audience de renvoi par son numero (lot 16 : EcrivainRenvoisRpjc.cs).
            Gestes = ["Accepter", "Rejeter"],
            Parametres = ["Audience de renvoi", "Motif rejet", "Observation rejet", Champ.Date("Date de prononcé")],
            Rang = 7,
            Etats = [.. EtatsEcran.Trois("En attente", "En cours", "Acceptée"),
                     new EtatEcran("Rejetee", "Rejetée par le greffe", "rejetee"), new EtatEcran("EnAttente", "Rejet Extelia", "en-attente")],
            Groupes = ["À traiter", "Traitées", "Rejets Extelia"],
            Sections =
            [
                Marche("Demande", "La demande s'ouvre à votre nom : un autre agent qui la traite est nommé, et une demande traitée entre-temps demande de rafraîchir la recherche.", true,
                    "Affaire", "Parties représentées", "Motif de renvoi"),
                Marche("Avocat", "L'avocat est retrouvé par son CNBF ; introuvable, la demande ne s'ouvre pas (« Non trouvé » dans la liste).", true,
                    "CNBF", "Mandataire"),
                Marche("Audiences", "L'audience initiale et l'audience de renvoi côte à côte : date, heure, convocation, chambre, section, salle. Une demande dont l'audience initiale est encore à venir ne se traite pas.", true,
                    "Audience initiale", "Audience de renvoi"),
                Marche("Décision", "Accepter exige une audience de renvoi ; Rejeter exige un motif, et la date de prononcé si le motif l'impose.", true,
                    "Accepter", "Rejeter", "Motif", "Observation", "Date de prononcé"),
                Marche("Récapitulatif", "Le document Word de la demande : parties, motif, note, audiences.", false,
                    "Récapitulatif"),
            ],
            Lignes =
            [
                Ligne("Rôle 2026F00501 · Alpha Services SARL c/ Bêta Industrie SAS", "Me Irène Ardoise · audience initiale du 10/03/2026", "À traiter", "AFaire"),
                Ligne("Rôle 2026F00502 · Gamma Conseil SARL c/ Delta Transports SAS", "Me Simon Bélier · audience de renvoi à choisir", "À traiter", "EnCours", 0, 1, 2),
                Ligne("Rôle 2026F00503 · Epsilon Habitat SARL", "Me Olga Chantelle · CNBF vérifié", "À traiter", "EnCours", 0, 1),
                Ligne("Rôle 2026F00480 · Zêta Commerce SAS c/ Êta Boulangerie SARL", "Renvoi accepté au 07/04/2026", "Traitées", "Validee", 0, 1, 2, 3),
                Ligne("Rôle 2026F00481 · Thêta Import SAS", "Rejeté : motif sans date de prononcé", "Traitées", "Rejetee", 0, 1, 2, 3),
                Ligne("Rôle 2026F00470 · Iota Études SARL", "Rejet Extelia du 05/03/2026", "Rejets Extelia", "EnAttente"),
            ],
        },

        // proc_fnig.md (Detail OPE : la fiche rectifiee -- gestion des DEMANDES FNIG, pas consultation ;
        // OPE_EDITION, machine a etats des boutons, desactivation par clone, renvoi a l'identique).
        new DescripteurEcran
        {
            Code = "PROC_FNIG", Titre = "Demandes FNIG", Sujet = "demandes FNIG",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une personne, une entreprise, un jugement…",
            Criteres = ["État : à envoyer, envoyée, en erreur, à désactiver", "Date du jugement", "Demande d'une autre juridiction"],
            Valider = "Valider", // proc_fnig.md, OPE_EDITION : VALIDER, apres « Êtes vous sur de vouloir envoyer la demande au FNIG? ».
            // Les deux gestes de la barre de l'edition qui visent une demande de la liste (OPE_EDITION.cs:102-111) : « Désactiver » une
            // demande envoyee (une demande de desactivation neuve, a envoyer) et « Renvoyer la demande à l'identique » une demande en
            // erreur ; l'envoi au FNIG est le traitement de fond de RIG v2, jamais appele d'ici (lot 16 : EcrivainDemandesFnig.cs).
            // « Raison annulation » : le seul champ que la desactivation ouvre (OPE_EDITION_JUGEMENT.cs:126-138).
            Gestes = ["Désactiver", "Renvoyer la demande à l'identique"],
            // Les etats qui allument chaque bouton (_ChangerActivationBouttons, OPE_EDITION.cs:296-305) : « Désactiver » une demande
            // ENVOYEE (EstDesactivationPossible, l. 46-51), « Renvoyer » une demande EN ERREUR (EstCasierFnigEnErreur, l. 56-58) --
            // deux lignes que la file figerait sans cela (lot 16, decision du chef).
            EtatsGestes = new Dictionary<string, string[]> { ["Désactiver"] = ["Validee"], ["Renvoyer la demande à l'identique"] = ["Rejetee"] },
            Parametres = [Champ.Texte("Raison annulation")],
            Rang = 4,
            Etats = [.. EtatsEcran.Trois("Nouvelle demande", "En saisie", "Envoyée"),
                     new EtatEcran("Rejetee", "En erreur", "rejetee"), new EtatEcran("EnAttente", "À désactiver", "en-attente")],
            Groupes = ["Ajout", "Modification", "Désactivation"],
            Sections =
            [
                Marche("Personne", "La personne interdite de gérer : nom, prénoms, date et lieu de naissance.", true,
                    "Nom", "Prénoms", "Date de naissance", "Lieu de naissance"),
                Marche("Jugement", "La décision qui prononce l'interdiction.", true,
                    "Juridiction", "Date du jugement", "Durée"),
                Marche("Entreprises", "Les entreprises concernées ; une entreprise laissée vide est retirée à l'enregistrement.", false,
                    "SIREN", "Dénomination"),
                Marche("Envoi", "Valider pose la demande « à envoyer », le traitement de fond l'envoie au FNIG. On ne modifie qu'une demande envoyée ou en erreur ; on ne désactive qu'une demande envoyée, et la désactivation crée une demande à part. Une demande en erreur se renvoie à l'identique.", true,
                    "Utilisateur FNIG", "Type de demande"),
            ],
            Lignes =
            [
                Ligne("Personne 0201", "Jugement du 04/03/2026 · interdiction de 5 ans", "Ajout", "AFaire"),
                Ligne("Personne 0202", "Jugement du 06/03/2026 · entreprise à rattacher", "Ajout", "EnCours", 0, 1),
                Ligne("Personne 0203", "Envoyée le 09/03/2026", "Ajout", "Validee", 0, 1, 3),
                Ligne("Personne 0204", "Envoi refusé : date de naissance absente", "Ajout", "Rejetee", 0, 1, 3),
                Ligne("Personne 0205", "Durée corrigée · à renvoyer", "Modification", "EnCours", 0, 1),
                Ligne("Personne 0206", "Modification envoyée le 12/03/2026", "Modification", "Validee", 0, 1, 2, 3),
                Ligne("Personne 0207", "Interdiction levée · désactivation en attente d'envoi", "Désactivation", "EnAttente", 0, 1, 3),
            ],
        },

        // Pas de fiche : Judiciaire\Exe\PROC_GDEST_INST (frmListeDestInst : code, nom, adresse et type
        // obligatoires ; civilite, formule de politesse, courriel, coffre-fort ; types a creer).
        new DescripteurEcran
        {
            Code = "PROC_GDEST_INST", Titre = "Destinataires institutionnels", Sujet = "destinataires institutionnels",
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Nom", "Code", "Type", "Adresse", "Adresse courriel"],
            Recherche = "Rechercher un code, un nom, un type…",
            Criteres = ["Code", "Nom", "Type"],
            Valider = "Valider", // frmListeDestInst.cs : « Vous devez annuler ou valider les modifications ».
            // Ajouter, Modifier, Supprimer dans la grille : frmAddDestInst._Valider et le bouton SUPPRIMER de frmListeDestInst (lot 16 :
            // EcrivainDestinatairesInstitutionnels.cs) ; la grille saisit ses colonnes, les autres champs restent ceux de la base.
            Rang = 7,
            Etats = [.. EtatsEcran.Trois("À compléter", "En saisie", "Enregistré"), new EtatEcran("EnAttente", "Inactif", "en-attente")],
            Groupes = ["Parquet et juridictions", "Administrations", "Organismes"],
            Sections =
            [
                Marche("Identité", "Le code, le nom, l'adresse et le type sont obligatoires ; un code déjà pris est refusé : « Le code saisi existe déjà ».", true,
                    "Code", "Nom", "Type", "Adresse"),
                Marche("Courrier", "La civilité, la civilité développée et la formule de politesse des courriers.", false,
                    "Civilité", "Civilité développée", "Formule de politesse"),
                Marche("Courriel et coffre-fort", "L'adresse courriel ; le nom du coffre-fort et sa date de démarrage pour les envois dématérialisés.", false,
                    "Adresse courriel", "Nom du coffre-fort", "Date de démarrage"),
                Marche("Nouveau type", "Un type neuf demande un code et un libellé ; le code ne contient pas d'apostrophe et n'existe pas déjà.", false,
                    "Code du type", "Libellé du type"),
            ],
            Lignes =
            [
                Ligne("Parquet du tribunal judiciaire", "PARQ · courrier et coffre-fort", "Parquet et juridictions", "Validee", 0, 1, 2),
                Ligne("Cour d'appel", "CA · adresse à compléter", "Parquet et juridictions", "EnCours", 0),
                Ligne("Direction des finances publiques", "DGFIP · à créer", "Administrations", "AFaire"),
                Ligne("Urssaf", "URSSAF · coffre-fort depuis le 01/02/2026", "Administrations", "Validee", 0, 2),
                Ligne("Banque de France", "BDF · formule de politesse à saisir", "Organismes", "EnCours", 0, 1),
                Ligne("Chambre de commerce", "CCI · ancien destinataire", "Organismes", "EnAttente", 0),
            ],
        },

        // Pas de fiche : RIG\EXE\Accueil\PROC_PoolEdition (« Pool d'éditions » : etapes CRITERE, POOL,
        // DESTINATAIRE, EDITION, IMPRESSION, HISTO, REPRISE ; « Vous devez saisir des critères de recherche »).
        new DescripteurEcran
        {
            Code = "PROC_POOLEDITION", Titre = "File des editions", Sujet = "demandes d'édition",
            Forme = FormeEcran.File,
            Recherche = "Rechercher une demande, un dossier, un destinataire…",
            Criteres = ["Dossier (ou demande)", "Date de création", "Inclure les demandes supprimées"],
            Valider = "Imprimer", // PoolEdition, etape IMPRESSION : « Imprimer : ».
            // « Imprimer » = la fenetre « Propriétés du document » validee sans reprise (PoolEditionProprietes._Valider, l. 206-327) :
            // « Imprimer : » coche, l'imprimante et le nombre d'exemplaires saisis a cote de la liste (Designer l. 256, 506, 467). Lot 16 :
            // EcrivainFileEditions.cs ; la reprise d'une edition emise n'est pas reprise (nomme dans l'ecrivain).
            Gestes = ["Imprimer"],
            Parametres = [Champ.Texte("Imprimante"), Champ.Nombre("Nombre d'exemplaires")],
            Rang = 2,
            Etats = [.. EtatsEcran.Trois("À émettre", "En cours", "Émise"), new EtatEcran("Rejetee", "Supprimée", "rejetee")],
            Groupes = ["Courrier", "Courriel", "File différée"],
            Sections =
            [
                Marche("Demande", "La demande d'édition se retrouve par son dossier ou son numéro ; sans critère, la recherche est refusée : « Vous devez saisir des critères de recherche ».", true,
                    "Dossier (ou demande)", "Date de création", "Date d'émission"),
                Marche("Destinataires", "Le destinataire, son adresse postale ou son courriel, et la copie conforme.", true,
                    "Destinataire", "Adresse postale", "Adresse courriel", "Copie conforme"),
                Marche("Document", "Les propriétés du document : le modifier, ou régénérer ses données.", false,
                    "Libellé", "Corps", "Regénération des données"),
                Marche("Impression", "L'imprimante, le nombre d'exemplaires, le recto verso, l'agrafage ; afficher avant émission ou passer en file différée.", true,
                    "Imprimante", "Nombre d'exemplaires", "Recto/Verso", "Agrafage", "File différée"),
                Marche("Reprise", "Le niveau de reprise d'une émission, et son journal.", false,
                    "Niveau de reprise", "Journal"),
            ],
            Lignes =
            [
                Ligne("Alpha Services SARL · demande 88101", "Accusé de dépôt · 1 destinataire", "Courrier", "AFaire"),
                Ligne("Bêta Industrie SAS · demande 88102", "Kbis · imprimante à choisir", "Courrier", "EnCours", 0, 1),
                Ligne("Gamma Conseil SARL · demande 88103", "Certificat · émis le 21/03/2026", "Courrier", "Validee", 0, 1, 3),
                Ligne("Delta Transports SAS · demande 88104", "Courriel au déposant", "Courriel", "AFaire"),
                Ligne("Epsilon Habitat SARL · demande 88105", "Courriel · document régénéré", "Courriel", "EnCours", 0, 1, 2),
                Ligne("Zêta Commerce SAS · demande 88106", "File différée de 17 h", "File différée", "EnCours", 0),
                Ligne("Êta Boulangerie SARL · demande 88107", "Demande supprimée", "Courrier", "Rejetee", 0),
            ],
        },

        // Pas de fiche : RCS\Exe\PROC_TraitementNpai (« Outil de traitement des NPAI » : « Veuillez flasher
        // un code Data ou saisir un numéro de Demande », action, signature, imprimante, « Imprimer à Amitel »).
        new DescripteurEcran
        {
            Code = "PROC_TRAITEMENTNPAI", Titre = "Courriers revenus NPAI", Sujet = "courriers revenus",
            Forme = FormeEcran.File,
            Recherche = "Flasher un code Data ou saisir un numéro de demande…",
            Criteres = ["Code Data", "Numéro de demande"],
            Valider = "Valider", // FrmActionsNPAI.Designer.cs : « Valider ».
            // Le bouton « Annuler » de l'action ouverte (btAnnule, FrmProcTraitementNpai.cs:1101-1107) : la demande NPAI restee qualifiee
            // passe en X (lot 16 : EcrivainCourriersNpai.cs). « Valider » une action vise le courrier FLASHE, absent de la demande laissee
            // en cours : non repris (nomme dans l'ecrivain).
            Gestes = ["Annuler"],
            Rang = 2,
            Etats = EtatsEcran.Trois("À traiter", "En cours", "Traité"),
            Groupes = ["Formalités", "Relances de comptes annuels"],
            Sections =
            [
                Marche("Courrier", "Flasher le code Data du courrier revenu, ou saisir le numéro de sa demande.", true,
                    "Code Data", "Numéro de demande"),
                Marche("Société et formalité", "Les informations de la société (dirigeants, représentants) et de la formalité ou de la relance d'origine.", true,
                    "Société", "Dirigeants", "Formalité"),
                Marche("Action", "L'action à mener sur ce courrier revenu « n'habite pas à l'adresse indiquée ».", true,
                    "Action", "Choix optionnel"),
                Marche("Impression", "L'imprimante et la signature ; l'impression peut partir chez Amitel.", false,
                    "Imprimante", "Signature", "Imprimer à Amitel"),
            ],
            Lignes =
            [
                Ligne("Kappa Négoce SAS · demande 77201", "Courrier du 02/03/2026 revenu le 16/03/2026", "Formalités", "AFaire"),
                Ligne("Lambda Bâtiment SARL · demande 77202", "Dirigeant vérifié · action à choisir", "Formalités", "EnCours", 0, 1),
                Ligne("Mu Textile SAS · demande 77203", "Traité le 17/03/2026", "Formalités", "Validee", 0, 1, 2),
                Ligne("Nu Distribution SARL · relance 2025", "Relance revenue le 18/03/2026", "Relances de comptes annuels", "AFaire"),
                Ligne("Xi Conseil SARL · relance 2025", "Action choisie · impression à faire", "Relances de comptes annuels", "EnCours", 0, 1, 2),
                Ligne("Omicron Services EURL · relance 2024", "Traité, imprimé chez Amitel", "Relances de comptes annuels", "Validee", 0, 1, 2, 3),
            ],
        },

        // Outils\Impression\PROC_IMP_WIN (« Paramétrage des imprimantes greffe »). Lot 6 du plan « RIG v3 complet » (28/11/2026) :
        // l'ecran montre l'onglet « Lots » de RIG v2 -- les imprimantes LOGIQUES lot 1 (IMPRIMANTE_PS, ImprimanteLot1.cs:97) et lot 2
        // (IMPRIMANTE_EDITION, ImprimanteLot2.cs:262, types de papier dans TAG_POSTSCRIPT), tables de la BASE DU GREFFE, lues par
        // LecteurImprimantesGreffe.cs et ecrites par EcrivainImprimantesGreffe.cs (Ajouter, Modifier ; Supprimer reste une
        // demonstration : la V2 exige une imprimante de remplacement, OPE_IMP_LOT1.cs:208-226). Colonnes : les grilles et la saisie de
        // la V2 (OPE_IMP_LOT1.cs:77-101, OPE_IMP_LOT2.cs:79-96, OPE_SAISIE_IMP_LOT1.Designer.cs:94-120).
        // NON MONTRES (lot 16 du plan precedent, 27/11/2026) : les onglets des imprimantes Windows, de leurs bacs (« Bacs papier »)
        // et de leurs reglages nommes (DEVMOD) -- tables IMPRIMANTE_WINDOWS, IMPRIMANTE_BAC, IMPRIMANTE_DEVMOD et
        // RIG_IMPRIMANTE_WINDOWS de COMMUN_RIG (connexion CodeGreffeBaseCommmunRig, OPE_IMP_MANQUANTE.cs:147), par serveur
        // d'impression, absentes de la base V3 du greffe ; les pilotes viennent du serveur Windows (PrinterManagement,
        // OPE_IMP_MANQUANTE.cs:47). D'ou le retrait des marches « Bacs papier », « Réglages nommés », « Code OMR » et du groupe
        // « Autres imprimantes » : aucune donnee de la base du greffe ne les porte.
        new DescripteurEcran
        {
            Code = "PROC_IMP_WIN", Titre = "Imprimantes du greffe", Sujet = "imprimantes lot 1 et lot 2",
            Forme = FormeEcran.Referentiel,
            Colonnes = ["Nom", "Redirigée vers l'imprimante physique", Champ.Case("Utilisable pour les différés"), Champ.Case("Active"),
                        "Types de papier", Champ.Liste("Lot", "Lot 1", "Lot 2")],
            Recherche = "Rechercher une imprimante ou l'imprimante physique vers laquelle elle sort…",
            Criteres = ["Lot", "Imprimante physique"],
            Valider = "Enregistrer", // OPE_SAISIE_IMP_LOT1.cs:84-86 et LOT2.cs:93-95 : le bouton VALIDER de la saisie s'appelle « Enregistrer ».
            Rang = 7,
            Etats = EtatsEcran.Trois("À rediriger", "Inactive", "Active"),
            Groupes = ["Lot 1", "Lot 2"],
            Sections =
            [
                Marche("Imprimante", "Le nom et le lot ne changent plus une fois l'imprimante enregistrée. L'imprimante physique est obligatoire.", true,
                    "Nom", Champ.Liste("Lot", "Lot 1", "Lot 2"), "Redirigée vers l'imprimante physique"),
                Marche("Lot 1 : différés et activation", "Pour une imprimante lot 1 seulement.", false,
                    Champ.Case("Utilisable pour les différés"), Champ.Case("Active")),
                Marche("Lot 2 : types de papier", "Pour une imprimante lot 2 : les types séparés par des virgules (NORMAL, SECURISE, LRAR à l'ajout).", false,
                    "Types de papier"),
            ],
            // Les cellules, dans l'ordre des colonnes : des imprimantes de RIG_DEV (IMPRIMANTE_PS, IMPRIMANTE_EDITION), anonymes.
            Lignes =
            [
                new LigneEcran("Accueil", "vers PS · utilisable pour les différés", "Lot 1", "Validee", []) { Valeurs = ["Accueil", "PS", "oui", "oui", "", "Lot 1"] },
                new LigneEcran("Plieuse", "vers Plieuse 12 · inactive", "Lot 1", "EnCours", []) { Valeurs = ["Plieuse", "Plieuse 12", "", "", "", "Lot 1"] },
                new LigneEcran("Nouvelle copieuse", "sans imprimante physique", "Lot 1", "AFaire", []) { Valeurs = ["Nouvelle copieuse", "", "", "oui", "", "Lot 1"] },
                new LigneEcran("Greffe RCS", "vers RL · papier LRAR, NORMAL, SECURISE", "Lot 2", "Validee", []) { Valeurs = ["Greffe RCS", "RL", "", "", "LRAR, NORMAL, SECURISE", "Lot 2"] },
                new LigneEcran("Audiences", "vers RL · papier NORMAL", "Lot 2", "Validee", []) { Valeurs = ["Audiences", "RL", "", "", "NORMAL", "Lot 2"] },
                new LigneEcran("Archives", "sans imprimante physique · aucun type de papier", "Lot 2", "AFaire", []) { Valeurs = ["Archives", "", "", "", "", "Lot 2"] },
            ],
        },
    ];

    private static MarcheEcran Marche(string titre, string consigne, bool obligatoire, params ChampEcran[] champs) =>
        new(titre, consigne, obligatoire, champs);

    private static LigneEcran Ligne(string nom, string precisions, string groupe, string etat, params int[] faites) =>
        new(nom, precisions, groupe, etat, faites);
}
