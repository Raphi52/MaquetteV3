<!-- FICHIER GENERE par tests/SoclePassageCheck (SupportsFormation.cs) a partir des ecrans de la V3 : ne pas modifier a la main. Regenerer : RIGV3_ECRIRE_FORMATION=1 .\tests.ps1 SoclePassage -->

# Les écrans de la V3, métier par métier (SOC-026)

Ce que l'agent trouve dans le menu de gauche de la V3, rangé comme le menu (geste › verbe), pour son métier.
« dans la V3 » : l'écran est construit ; « a venir » : l'entrée est au menu, son écran arrive avec son domaine.
Le code entre guillemets inversés est celui de la procédure de RIG V2 : c'est le même écran, l'agent le reconnaît.

## RCS — 24 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Nouvelle formalite | ouvrir une procedure collective | `PROC_NF` | a venir |
| Ouvrir > Creer une affaire | Ouvrir un dossier | une societe arrive au registre | `OUVERTURE_DOSSIER` | dans la V3 |
| Ouvrir > Recevoir | Ordonnance d'enrolement | ordonnance RCS | `PROC_CORDJRCS` | a venir |
| Ouvrir > Recevoir | Commandes web | traiter les commandes recues | `PROC_COMMANDES_WEB` | dans la V3 |
| Ouvrir > Enregistrer un depot | Depots au registre | actes, comptes annuels, beneficiaires — réunit : Depots d'actes RCS (`PROC_DAS`), Comptes annuels (`PROC_DCA`), Depots beneficiaires (`PROC_DASBEN`) | — | a venir |
| Trouver > Une affaire | Dossier RCS | voir un dossier du registre | `PROC_VDOSSIER` | a venir |
| Trouver > Chiffres et fichiers exterieurs | Chiffres du registre | chiffres, tarifs, bilans en rejet — réunit : Chiffres d'un dossier (`PROC_VCHIFFRES`), Tarifs (`PROC_TARIFS`), Bilans en rejet (`PROC_REJETS_BILANS`) | — | a venir |
| Trouver > Chiffres et fichiers exterieurs | Rechercher un beneficiaire | consultation BEN | `PROC_BEN` | a venir |
| Modifier > L'affaire | Corriger un dossier RCS | mentions en marge, diligences, beneficiaires — réunit : Mentions en marge (`PROC_MMA`), Diligences RCS (`PROC_MDIL`), Beneficiaires d'un dossier (`PROC_BE`) | — | a venir |
| Surveiller > Relancer | Relances et injonctions | comptes annuels, beneficiaires, RBE, radiations d'office — réunit : Relances comptes annuels (`PROC_DCA_RELANCE`), Injonctions comptes annuels (`PROC_DCA_INJ`), Relances beneficiaires (`PROC_DBE_RELANCE`), Injonctions beneficiaires (`PROC_DBE_INJ`), Rappels RBE (`PROC_SRBE_RAPPEL`), Radiations d'office (`PROC_ROFSURVEILLANCES`) | — | a venir |
| Surveiller > Relancer | Actes retenus et rejetes | retenus, rejets Infogreffe, actes web, Kbis XML — réunit : Retention d'actes (`PROC_ACTRET`), Actes rejetes (`PROC_ACTREJ`), Actes web non transmis (`PROC_ACTREJ_CMD_WEB`), Kbis XML rejetes (`PROC_REJETSKBISXML`) | — | a venir |
| Surveiller > Relancer | Divergences BEN | alertes de coherence | `PROC_DBEN_ALERT` | a venir |
| Terminer >  | Édition interne d'un Kbis | extrait Kbis a usage interne | `PROC_XEX` | a venir |
| Facturer > Facturer | Paiements INPI | reversements, formalites en attente — réunit : Reversements INPI (`PROC_INPIPAY`), Formalites INPI (`PROC_GINPI`) | — | a venir |
| Configurer > Les clients | Non-inscrits | creer une personne hors registre | `PROC_CNI` | a venir |
| Configurer > Referentiels | Tables et natures de document | codes du greffe, typologie de la GED — réunit : Tables de reference (`PROC_GTABREF`), Natures de document (`PROC_GED_NATURE_DOC`) | — | a venir |
| Configurer > Referentiels | Bornes d'accueil | les kiosques KBIS | `PROC_BORNE` | a venir |
| Configurer > Modeles et envois | Messagerie | les envois automatiques | `PROC_GESTEMAIL` | dans la V3 |
| Configurer > Argent et exports | Banques | les comptes du greffe | `PROC_GESTBANQ` | dans la V3 |
| Configurer > Argent et exports | Facturation Expresso | parametrage de la facturation | `PROC_PARAMEXPRESSO` | a venir |
| Configurer > Argent et exports | Listes Excel | parametrage des exports | `PROC_PARAM_LISTEEXCEL` | a venir |
| Configurer > Outils | Requetes metier | executer une requete preparee | `PROC_REQUETES` | a venir |
| Configurer > Outils | Formalites dematerialisees | analyse IA, retours CFE-NET — réunit : Analyse IA (`PROC_IA`), Retours CFE-NET (`PROC_CFENET_RET`) | — | a venir |
| Configurer > Outils | Imprimantes du greffe | lots, bacs et pilotes | `PROC_IMP_WIN` | a venir |

## Judiciaire — 37 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Nouvelle affaire judiciaire | instance, PC, mesure, jonction — réunit : Nouvelle instance (`PROC_XAFF`), Nouvelle proc. collective (`PROC_NPC`), Mesure provisoire (`PROC_MESIN`), Jonction d'instances (`PROC_JOIN`) | — | a venir |
| Ouvrir > Creer une affaire | Injonction de payer | creer une IP | `PROC_IPE` | a venir |
| Ouvrir > Creer une affaire | Enrolement judiciaire | mettre l'affaire au role | `PROC_CJUD` | dans la V3 |
| Ouvrir > Enregistrer un depot | Depots au tribunal | les depots judiciaires | `PROC_DEPOTJUD` | dans la V3 |
| Ouvrir > Enregistrer un depot | Depots de procedure collective | un evenement ou un lot de PC — réunit : Depots d'evenement PC (`PROC_DEP`), Depots PC en masse (`PROC_DEPPCM`) | — | a venir |
| Trouver > Une affaire | Dossiers et registres judiciaires | instance, PC, registres, navires — réunit : Dossier judiciaire (`PROC_VJUD`), Registres judiciaires (`PROC_REGJUD`), Navires (`PROC_RECHERCHE_BATEAU`) | — | a venir |
| Trouver > Les documents | Documents judiciaires | la GED du judiciaire | `PROC_GEDJUD` | dans la V3 |
| Modifier > L'affaire | Corriger une affaire judiciaire | instance, PC, mots directeurs, repertoire, parties — réunit : Instance judiciaire (`PROC_MJUD`), Procedure collective (`PROC_MPC`), Mots directeurs (`PROC_MDJUD`), Repertoire (`PROC_MODIF_REPER`), Parties d'une instance (`PROC_PARTIES`) | — | a venir |
| Modifier > L'affaire | Defaire une affaire | liens entre PC, suppression, liberation — réunit : Liens entre PC (`PROC_XLIEN`), Supprimer une instance (`PROC_XXAFF`), Liberer une affaire (`PROC_LIBAFFAIRE`) | — | a venir |
| Modifier > L'affaire | Modifier un lot de PC | juge-commissaire, organes, debiteurs — réunit : Changer le juge-commissaire (`PROC_CHM`), Changer les organes (`PROC_CHORGANESENMASSE`), Debiteurs multiples (`PROC_DEBITEURS_MULTIPLES`) | — | a venir |
| Modifier > Rediger et signer | Redaction | ecrire une decision | `PROC_REDA` | dans la V3 |
| Modifier > Rediger et signer | Redaction en masse | rediger un lot de decisions | `PROC_REDA_MASSE` | a venir |
| Modifier > Rediger et signer | Signer et diffuser les decisions | destinataires, signature, occultations, impression — réunit : Destinataires (`PROC_DEST_REDA`), Signature electronique (`PROC_SIGNJUD`), Ordonnances a signer (`PROC_ORDOSIGN`), Occultations (`PROC_DJ_OCCULTATIONS`), Impression legale (`PROC_IMPLEG`) | — | a venir |
| Surveiller > Les audiences | Roles et cabinets | organiser les audiences a venir | `PROC_GESTAUDS` | dans la V3 |
| Surveiller > Les audiences | Audiences | les dates qui approchent | `PROC_AUDIENCE` | dans la V3 |
| Surveiller > Les audiences | Preparer l'audience | composition, plumitif, convocations, chambres — réunit : Composition (`PROC_COMPO`), Preparation d'audience (`PROC_PREAUD`), Convocations (`PROC_GCONV`), Mise au point chambres (`PROC_MPCS`) | — | a venir |
| Surveiller > Les audiences | Apres l'audience | retour, deliberes, taxes et tours — réunit : Retour d'audience (`PROC_RETAUD`), Deliberes (`PROC_LISTE_DELIBERES`), Taxes et tours de role (`PROC_TAXESETTOURS`) | — | a venir |
| Surveiller > Les instances | Suivi des injonctions de payer | IP en attente, preventions — réunit : Injonctions de payer (`PROC_SURVIP`), Suivi des preventions (`PROC_IPSUIVI`) | — | a venir |
| Surveiller > Les instances | Mesures | surveillance des mesures | `PROC_SURVMES` | a venir |
| Surveiller > Les echanges et l'ancien RIG | Suivi dematerialisation | ce qui est parti au JAL/BODACC | `PROC_SDEMATJUD` | a venir |
| Surveiller > Chiffres | Statistiques cour d'appel | ce qui remonte a la CA | `PROC_STATCA` | a venir |
| Facturer > Facturer | Creances | declarer, corriger, integrer, representer — réunit : Ajouter une creance (`PROC_ADDCREANCE`), Modifier une creance (`PROC_MODCREANCE`), Integrer des creances (`PROC_INTEGCREANCE`), Representation (`PROC_CREANREP`) | — | a venir |
| Configurer > Les acteurs | Acteurs judiciaires | juges, parquet, intervenants | `PROC_ACTEUR_JUD` | dans la V3 |
| Configurer > Les acteurs | Experts | qui designer, qui a deja siege | `PROC_EXPERT` | dans la V3 |
| Configurer > Les acteurs | Mandataires et destinataires | mandataires, destinataires institutionnels, rapprocher, remplacer — réunit : Mandataires (`PROC_MANDATAIRE`), Destinataires institutionnels (`PROC_GDEST_INST`), Rapprocher des acteurs (`PROC_RAPPACTEUR`), Remplacer un acteur (`PROC_SUPP_ACTEUR_JUD`) | — | a venir |
| Configurer > Referentiels | Tables et natures de document | codes du greffe, typologie de la GED — réunit : Tables de reference (`PROC_GTABREF`), Natures de document (`PROC_GED_NATURE_DOC`) | — | a venir |
| Configurer > Referentiels | Cabinets | les salles d'audience | `PROC_CABINET` | a venir |
| Configurer > Referentiels | Codes chefs IP | parametrage des injonctions | `PROC_PARAM_IP` | a venir |
| Configurer > Referentiels | Bornes d'accueil | les kiosques KBIS | `PROC_BORNE` | a venir |
| Configurer > Modeles et envois | Modeles de redaction | maquettes des decisions | `PROC_REDAA` | a venir |
| Configurer > Modeles et envois | Messagerie | les envois automatiques | `PROC_GESTEMAIL` | dans la V3 |
| Configurer > Argent et exports | Banques | les comptes du greffe | `PROC_GESTBANQ` | dans la V3 |
| Configurer > Argent et exports | Facturation Expresso | parametrage de la facturation | `PROC_PARAMEXPRESSO` | a venir |
| Configurer > Argent et exports | Listes Excel | parametrage des exports | `PROC_PARAM_LISTEEXCEL` | a venir |
| Configurer > Outils | Requetes metier | executer une requete preparee | `PROC_REQUETES` | a venir |
| Configurer > Outils | Echanges avec les avocats | RPVA et RPJC : inscriptions, renvois, conciliation — réunit : EDI RPVA (`PROC_EDI_RPVAINS`), EDI RPJC (`PROC_EDI_RPJC_TRCNC`), EDI RPJC inscriptions (`PROC_EDI_RPJC_INS`), EDI RPJC renvois (`PROC_EDI_RPJC_REN`) | — | a venir |
| Configurer > Outils | Imprimantes du greffe | lots, bacs et pilotes | `PROC_IMP_WIN` | a venir |

## Endettement (injonctions, creances) — 6 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Injonction de payer | creer une IP | `PROC_IPE` | a venir |
| Ouvrir > Recevoir | Commandes web | traiter les commandes recues | `PROC_COMMANDES_WEB` | dans la V3 |
| Surveiller > Relancer | Relances et injonctions | comptes annuels, beneficiaires, RBE, radiations d'office — réunit : Relances comptes annuels (`PROC_DCA_RELANCE`), Injonctions comptes annuels (`PROC_DCA_INJ`), Relances beneficiaires (`PROC_DBE_RELANCE`), Injonctions beneficiaires (`PROC_DBE_INJ`), Rappels RBE (`PROC_SRBE_RAPPEL`), Radiations d'office (`PROC_ROFSURVEILLANCES`) | — | a venir |
| Surveiller > Relancer | Actes retenus et rejetes | retenus, rejets Infogreffe, actes web, Kbis XML — réunit : Retention d'actes (`PROC_ACTRET`), Actes rejetes (`PROC_ACTREJ`), Actes web non transmis (`PROC_ACTREJ_CMD_WEB`), Kbis XML rejetes (`PROC_REJETSKBISXML`) | — | a venir |
| Surveiller > Les instances | Suivi des injonctions de payer | IP en attente, preventions — réunit : Injonctions de payer (`PROC_SURVIP`), Suivi des preventions (`PROC_IPSUIVI`) | — | a venir |
| Facturer > Facturer | Creances | declarer, corriger, integrer, representer — réunit : Ajouter une creance (`PROC_ADDCREANCE`), Modifier une creance (`PROC_MODCREANCE`), Integrer des creances (`PROC_INTEGCREANCE`), Representation (`PROC_CREANREP`) | — | a venir |

## Tous les métiers — 54 entrée(s) communes (courrier, documents, comptabilité, réglages)

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Recevoir | Courrier du jour | le papier entre ce matin | `PROC_ARRIVEE` | dans la V3 |
| Ouvrir > Recevoir | Demandes recues | ce qu'on attend de moi | `PROC_DEMANDE` | dans la V3 |
| Ouvrir > Recevoir | Requisitions | commander des documents | `PROC_REQUI` | dans la V3 |
| Trouver > Une affaire | Mes affaires | tout ce qui m'est confie | `PROC_RECHAFF` | dans la V3 |
| Trouver > Les documents | Chercher et lire une piece | la GED, un PDF, une image — réunit : Documents (`PROC_RECHERCHE_GED`), Voir un PDF (`PROC_PDF`), Voir une image (`PROC_IMAGE`) | — | a venir |
| Trouver > Les documents | Documents de l'affaire | la GED de l'affaire | `PROC_DOC_DEMAT` | dans la V3 |
| Trouver > Les documents | Actes numerises | ce que le scanner a produit | `PROC_ACTES_NUM` | dans la V3 |
| Trouver > Les documents | Controle des scans | verifier avant de valider | `PROC_CONTROLE_VIDIGED` | dans la V3 |
| Trouver > Les documents | Coffre-fort | les pieces mises a l'abri | `PROC_COFFRE_FORT` | dans la V3 |
| Trouver > Chiffres et fichiers exterieurs | Encaissements | retrouver un reglement | `PROC_RECHENC` | a venir |
| Trouver > Chiffres et fichiers exterieurs | Fichiers et echanges exterieurs | FNIG, FIBEN, notifications, tribunal digital — réunit : Interdits de gerer (`PROC_VFNIG`), Demandes FNIG (`PROC_FNIG`), FIBEN (`PROC_FIBEN`), Notifications (`PROC_VNOTIF`), Tribunal digital (`PROC_VTRID`) | — | a venir |
| Modifier > Editer un courrier | Courriers divers | les lettres types du greffe | `PROC_COURRIER_DIVERS` | dans la V3 |
| Modifier > Editer un courrier | Courriers en masse | un courrier pour un lot | `PROC_COURRIERM` | a venir |
| Modifier > Editer un courrier | Retoucher un document | pivoter, numeroter les pages — réunit : Pivoter des pages (`PROC_PDF_WRITER`), Documents ocerises (`PROC_NUM`) | — | a venir |
| Modifier > Envoyer | Transmission | envoyer un document dematerialise | `PROC_TRANSDOC` | a venir |
| Modifier > Envoyer | Recommandes et retours | bordereaux, accuses, courriers NPAI — réunit : Bordereaux LR (`PROC_BORDLR`), Retours LRAR (`PROC_RETOURLRAR`), Courriers revenus NPAI (`PROC_TRAITEMENTNPAI`) | — | a venir |
| Modifier > Envoyer | Publications JAL et BODACC | envois, annonces, rejets — réunit : Envoi JAL / BODACC (`PROC_ENV_JAL`), Publications BODACC (`PROC_GESTBOD`), Surveillance BODACC (`PROC_SURVBOD`) | — | a venir |
| Modifier > Envoyer | Impressions et editions | files, editions differees, relances, suivi — réunit : File d'impression (`PROC_PAPERCUT`), File des editions (`PROC_POOLEDITION`), Editions differees (`PROC_EDT_DIFF`), Suivi d'impression (`PROC_SUIVI_IMPRESSION`), Relancer un fichier (`PROC_RELANCEFILE`), Relancer un lot (`PROC_RELANCELOT`) | — | a venir |
| Modifier > Envoyer | Export Excel | sortir une liste | `PROC_LISTEEXCEL` | a venir |
| Surveiller > Les instances | Lots | les lots en cours | `PROC_GESTLOT` | a venir |
| Surveiller > Les echanges et l'ancien RIG | Fiches en conflit | modifiees dans les deux RIG | `PROC_FICHES_CONFLIT` | dans la V3 |
| Surveiller > Les echanges et l'ancien RIG | Historique de l'ancien RIG | affaires des deux RIG, anciens numeros | `PROC_HISTORIQUE_ANCIEN_RIG` | dans la V3 |
| Surveiller > Chiffres | Statistiques | les chiffres du greffe | `PROC_STAT` | dans la V3 |
| Surveiller > Chiffres | Pilotage | tableaux de bord | `PROC_XPILOTAGE` | a venir |
| Surveiller > Chiffres | Numeros manquants | le registre chronologique | `PROC_NUMEROS_MANQUANTS` | dans la V3 |
| Terminer >  | Cloture | fermer une affaire terminee | `PROC_CLOT` | dans la V3 |
| Terminer >  | Archivage | archiver ou radier | `PROC_ARCHIRAD` | dans la V3 |
| Facturer > Facturer | Factures | ce qui est du | `PROC_FACTURE` | dans la V3 |
| Facturer > Facturer | Refacturation et releves | reediter une facture, le compte d'un client — réunit : Refacturation (`PROC_FACTREEDIT`), Releve de compte (`PROC_RELCLI`) | — | a venir |
| Facturer > Facturer | Articles tarifaires | ce qu'on facture | `PROC_ARTICLE` | a venir |
| Facturer > Encaisser et tenir les comptes | Encaissements | ce qui est rentre | `PROC_ENCAISS` | dans la V3 |
| Facturer > Encaisser et tenir les comptes | Banque, rejets et cheques | remises en banque, reglements revenus, cheques emis — réunit : Remise en banque (`PROC_REMBANQ`), Rejets d'encaissement (`PROC_REJET`), Cheques emis (`PROC_EDCHQEMIS`) | — | a venir |
| Facturer > Encaisser et tenir les comptes | Journaux et lettrage | caisse, ventes, repertoire, lettrage — réunit : Journal de caisse (`PROC_JCAISSE`), Journal de vente (`PROC_JVENTE`), Livre de repertoire (`PROC_JLR`), Lettrage (`PROC_VLETTRAGE`) | — | a venir |
| Facturer > Encaisser et tenir les comptes | Figer les ecritures | comptabiliser la journee | `PROC_COMPTABILISATION` | dans la V3 |
| Facturer > Encaisser et tenir les comptes | Certification NF525 | journal et grands totaux signes | `PROC_CERTIF` | dans la V3 |
| Configurer > Les clients | Clients | comptes et fiches | `PROC_GCLIENT` | dans la V3 |
| Configurer > Les clients | Fiche client | le detail d'un client | `PROC_FCLIENT` | dans la V3 |
| Configurer > Les clients | Tiers payeurs | qui regle a la place du client | `PROC_GESTTIERSPAYEURS` | a venir |
| Configurer > Mon poste | Mon accueil | tuiles et alertes | `PROC_ACCUEIL_PARAM` | dans la V3 |
| Configurer > Mon poste | Ancienne touche, nouvelle touche | les raccourcis de l'ancien RIG | `PROC_TOUCHES` | dans la V3 |
| Configurer > Mon poste | Actualites | les messages de l'accueil | `PROC_ACTUALITES` | dans la V3 |
| Configurer > Mon poste | Dates de travail | la date du greffe | `PROC_GESTDATE` | a venir |
| Configurer > Mon poste | Comptes des agents | qui ouvre RIG, et avec quels droits | `PROC_UTILISATEUR` | dans la V3 |
| Configurer > Mon poste | Pages internes | aide du greffe, site web dans RIG — réunit : Aide du greffe (`PROC_WIKI`), Page web interne (`PROC_WEB_BROWSER`) | — | a venir |
| Configurer > Prendre en main la V3 | Premiers pas | les gestes principaux de la V3 | `PROC_PREMIERS_PAS` | dans la V3 |
| Configurer > Prendre en main la V3 | Ancienne touche, nouvelle touche | les raccourcis de l'ancien RIG | `PROC_TOUCHES` | dans la V3 |
| Configurer > Prendre en main la V3 | Certificat NF525 de la V3 | organisme, numero, validite | `PROC_CERTIFICAT` | dans la V3 |
| Configurer > Referentiels | Compteurs de numeros | rendre un numero donne par erreur | `PROC_COMPTEURS_NUM` | dans la V3 |
| Configurer > Referentiels | Reglages du greffe | les parametres propres au greffe | `PROC_REGLAGES_GREFFE` | dans la V3 |
| Configurer > Modeles et envois | Modeles d'edition | la forme des courriers | `PROC_EDIT_MAQUETTE` | dans la V3 |
| Configurer > Modeles et envois | Sceaux | elements de fusion | `PROC_SCEAU` | a venir |
| Configurer > Modeles et envois | Courriels envoyes | la trace de chaque envoi | `PROC_MAIL_LOT1` | dans la V3 |
| Configurer > Argent et exports | Modes de paiement | les moyens acceptes | `PROC_GESTMODEPAIEMENT` | a venir |
| Configurer > Outils | Supervision | greffes, traitements, erreurs (Amitel) | `PROC_SUPERVISION` | dans la V3 |
