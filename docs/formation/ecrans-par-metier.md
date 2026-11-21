<!-- FICHIER GENERE par tests/SoclePassageCheck (SupportsFormation.cs) a partir des ecrans de la V3 : ne pas modifier a la main. Regenerer : RIGV3_ECRIRE_FORMATION=1 .\tests.ps1 SoclePassage -->

# Les écrans de la V3, métier par métier (SOC-026)

Ce que l'agent trouve dans le menu de gauche de la V3, rangé comme le menu (geste › verbe), pour son métier.
« dans la V3 » : l'écran est construit ; « a venir » : l'entrée est au menu, son écran arrive avec son domaine.
Le code entre guillemets inversés est celui de la procédure de RIG V2 : c'est le même écran, l'agent le reconnaît.

## RCS — 36 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Nouvelle formalite | ouvrir une procedure collective | `PROC_NF` | a venir |
| Ouvrir > Creer une affaire | Ouvrir un dossier | une societe arrive au registre | `OUVERTURE_DOSSIER` | dans la V3 |
| Ouvrir > Recevoir | Ordonnance d'enrolement | ordonnance RCS | `PROC_CORDJRCS` | a venir |
| Ouvrir > Recevoir | Commandes web | traiter les commandes recues | `PROC_COMMANDES_WEB` | dans la V3 |
| Ouvrir > Enregistrer un depot | Depots d'actes RCS | actes deposes au registre | `PROC_DAS` | a venir |
| Ouvrir > Enregistrer un depot | Depots beneficiaires | actes BEN | `PROC_DASBEN` | a venir |
| Trouver > Une affaire | Dossier RCS | voir un dossier du registre | `PROC_VDOSSIER` | a venir |
| Trouver > Un chiffre, un reglement | Chiffres d'un dossier | donnees financieres RCS | `PROC_VCHIFFRES` | a venir |
| Trouver > Un chiffre, un reglement | Tarifs | articles tarifaires RCS | `PROC_TARIFS` | a venir |
| Trouver > Un chiffre, un reglement | Bilans en rejet | comptes annuels refuses | `PROC_REJETS_BILANS` | a venir |
| Trouver > Hors du greffe | Rechercher un beneficiaire | consultation BEN | `PROC_BEN` | a venir |
| Modifier > L'affaire | Mentions en marge | MMA du registre | `PROC_MMA` | a venir |
| Modifier > L'affaire | Diligences RCS | les diligences d'un dossier | `PROC_MDIL` | a venir |
| Modifier > L'affaire | Beneficiaires d'un dossier | les BE d'une societe | `PROC_BE` | a venir |
| Surveiller > Relancer | Relances comptes annuels | DCA en retard | `PROC_DCA_RELANCE` | a venir |
| Surveiller > Relancer | Injonctions comptes annuels | passer a l'injonction | `PROC_DCA_INJ` | a venir |
| Surveiller > Relancer | Relances beneficiaires | DBE en retard | `PROC_DBE_RELANCE` | a venir |
| Surveiller > Relancer | Injonctions beneficiaires | injonction DBE | `PROC_DBE_INJ` | a venir |
| Surveiller > Relancer | Rappels RBE | registre des beneficiaires | `PROC_SRBE_RAPPEL` | a venir |
| Surveiller > Relancer | Retention d'actes | actes retenus au depot | `PROC_ACTRET` | a venir |
| Surveiller > Relancer | Actes rejetes | rejets Infogreffe | `PROC_ACTREJ` | a venir |
| Surveiller > Relancer | Divergences BEN | alertes de coherence | `PROC_DBEN_ALERT` | a venir |
| Terminer >  | Extinction RCS | eteindre un dossier du registre | `PROC_XEX` | a venir |
| Facturer > Facturer | Reversements INPI | ce qui part a l'INPI | `PROC_INPIPAY` | a venir |
| Facturer > Facturer | Formalites INPI | paiements en attente | `PROC_GINPI` | a venir |
| Configurer > Les clients | Non-inscrits | creer une personne hors registre | `PROC_CNI` | a venir |
| Configurer > Referentiels | Tables de reference | les codes du greffe | `PROC_GTABREF` | a venir |
| Configurer > Referentiels | Natures de document | typologie de la GED | `PROC_GED_NATURE_DOC` | a venir |
| Configurer > Referentiels | Bornes d'accueil | les kiosques KBIS | `PROC_BORNE` | a venir |
| Configurer > Modeles et envois | Messagerie | les envois automatiques | `PROC_GESTEMAIL` | dans la V3 |
| Configurer > Argent et exports | Banques | les comptes du greffe | `PROC_GESTBANQ` | dans la V3 |
| Configurer > Argent et exports | Facturation Expresso | parametrage de la facturation | `PROC_PARAMEXPRESSO` | a venir |
| Configurer > Argent et exports | Listes Excel | parametrage des exports | `PROC_PARAM_LISTEEXCEL` | a venir |
| Configurer > Outils | Requetes metier | executer une requete preparee | `PROC_REQUETES` | a venir |
| Configurer > Outils | Analyse IA | controle des formalites INPI | `PROC_IA` | a venir |
| Configurer > Outils | Retours CFE-NET | traitement des retours XML | `PROC_CFENET_RET` | a venir |

## Judiciaire — 69 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Nouvelle instance | saisir une affaire judiciaire | `PROC_XAFF` | a venir |
| Ouvrir > Creer une affaire | Nouvelle proc. collective | PC ou prevention | `PROC_NPC` | a venir |
| Ouvrir > Creer une affaire | Injonction de payer | creer une IP | `PROC_IPE` | a venir |
| Ouvrir > Creer une affaire | Enrolement judiciaire | mettre l'affaire au role | `PROC_CJUD` | dans la V3 |
| Ouvrir > Creer une affaire | Mesure provisoire | inscrire une mesure en instance | `PROC_MESIN` | a venir |
| Ouvrir > Creer une affaire | Jonction d'instances | joindre deux affaires | `PROC_JOIN` | a venir |
| Ouvrir > Recevoir | Creation via Infogreffe | enrolement recu par RPJC | `PROC_CJUDINFOG` | a venir |
| Ouvrir > Enregistrer un depot | Depots au tribunal | les depots judiciaires | `PROC_DEPOTJUD` | dans la V3 |
| Ouvrir > Enregistrer un depot | Depots d'evenement PC | evenement de PC ou prevention | `PROC_DEP` | a venir |
| Ouvrir > Enregistrer un depot | Depots PC en masse | traiter un lot de depots | `PROC_DEPPCM` | a venir |
| Trouver > Une affaire | Dossier judiciaire | instance, PC ou prevention | `PROC_VJUD` | a venir |
| Trouver > Une affaire | Registres judiciaires | les registres du greffe | `PROC_REGJUD` | a venir |
| Trouver > Une affaire | Navires | recherche de bateaux | `PROC_RECHERCHE_BATEAU` | a venir |
| Trouver > Les documents | Documents judiciaires | la GED du judiciaire | `PROC_GEDJUD` | dans la V3 |
| Modifier > L'affaire | Instance judiciaire | corriger une instance | `PROC_MJUD` | a venir |
| Modifier > L'affaire | Procedure collective | corriger une PC ou prevention | `PROC_MPC` | a venir |
| Modifier > L'affaire | Mots directeurs | qualification judiciaire | `PROC_MDJUD` | a venir |
| Modifier > L'affaire | Repertoire | corriger le repertoire | `PROC_MODIF_REPER` | a venir |
| Modifier > L'affaire | Parties d'une instance | qui s'oppose a qui | `PROC_PARTIES` | a venir |
| Modifier > Defaire | Liens entre PC | rattacher ou detacher | `PROC_XLIEN` | a venir |
| Modifier > Defaire | Supprimer une instance | et rendre les numeros | `PROC_XXAFF` | a venir |
| Modifier > Defaire | Liberer une affaire | rendre la main sur un dossier | `PROC_LIBAFFAIRE` | a venir |
| Modifier > En masse | Changer le juge-commissaire | sur plusieurs PC | `PROC_CHM` | a venir |
| Modifier > En masse | Changer les organes | mandataires sur un lot de PC | `PROC_CHORGANESENMASSE` | a venir |
| Modifier > En masse | Debiteurs multiples | plusieurs debiteurs sur une PC | `PROC_DEBITEURS_MULTIPLES` | a venir |
| Modifier > Rediger et signer | Redaction | ecrire une decision | `PROC_REDA` | dans la V3 |
| Modifier > Rediger et signer | Redaction en masse | rediger un lot de decisions | `PROC_REDA_MASSE` | a venir |
| Modifier > Rediger et signer | Destinataires | a qui part la decision | `PROC_DEST_REDA` | a venir |
| Modifier > Rediger et signer | Signature electronique | signer les decisions | `PROC_SIGNJUD` | a venir |
| Modifier > Rediger et signer | Ordonnances a signer | la pile a signer | `PROC_ORDOSIGN` | a venir |
| Modifier > Rediger et signer | Occultations | masquer avant diffusion | `PROC_DJ_OCCULTATIONS` | a venir |
| Modifier > Rediger et signer | Impression legale | jugements en masse | `PROC_IMPLEG` | a venir |
| Surveiller > Preparer les audiences | Roles et cabinets | organiser les audiences a venir | `PROC_GESTAUDS` | dans la V3 |
| Surveiller > Preparer les audiences | Composition | qui siege ce jour-la | `PROC_COMPO` | a venir |
| Surveiller > Preparer les audiences | Preparation d'audience | plumitif avant l'audience | `PROC_PREAUD` | a venir |
| Surveiller > Preparer les audiences | Convocations | convoquer les parties | `PROC_GCONV` | a venir |
| Surveiller > Preparer les audiences | Mise au point chambres | audiences de cabinet | `PROC_MPCS` | a venir |
| Surveiller > Suivre les audiences | Audiences | les dates qui approchent | `PROC_AUDIENCE` | dans la V3 |
| Surveiller > Suivre les audiences | Retour d'audience | saisir ce qui a ete dit | `PROC_RETAUD` | a venir |
| Surveiller > Suivre les audiences | Deliberes | ce qui reste a rendre | `PROC_LISTE_DELIBERES` | a venir |
| Surveiller > Suivre les audiences | Taxes et tours de role | editions PC d'audience | `PROC_TAXESETTOURS` | a venir |
| Surveiller > Les instances | Injonctions de payer | les IP en attente | `PROC_SURVIP` | a venir |
| Surveiller > Les instances | Suivi des preventions | les IP de prevention | `PROC_IPSUIVI` | a venir |
| Surveiller > Les instances | Mesures | surveillance des mesures | `PROC_SURVMES` | a venir |
| Surveiller > Les envois et les editions | Suivi dematerialisation | ce qui est parti au JAL/BODACC | `PROC_SDEMATJUD` | a venir |
| Surveiller > Chiffres | Statistiques cour d'appel | ce qui remonte a la CA | `PROC_STATCA` | a venir |
| Facturer > Creances | Creances | les creances d'une PC | `PROC_CREANCE` | dans la V3 |
| Facturer > Creances | Ajouter une creance | declarer une creance | `PROC_ADDCREANCE` | a venir |
| Facturer > Creances | Modifier une creance | corriger une declaration | `PROC_MODCREANCE` | a venir |
| Facturer > Creances | Integrer des creances | reprise en lot | `PROC_INTEGCREANCE` | a venir |
| Facturer > Creances | Representation | qui represente le creancier | `PROC_CREANREP` | a venir |
| Configurer > Les acteurs | Acteurs judiciaires | juges, parquet, intervenants | `PROC_ACTEUR_JUD` | dans la V3 |
| Configurer > Les acteurs | Experts | qui designer, qui a deja siege | `PROC_EXPERT` | dans la V3 |
| Configurer > Les acteurs | Mandataires | mandataires et praticiens | `PROC_MANDATAIRE` | a venir |
| Configurer > Les acteurs | Rapprocher des acteurs | fusionner deux fiches | `PROC_RAPPACTEUR` | a venir |
| Configurer > Les acteurs | Remplacer un acteur | substituer dans les instances | `PROC_SUPP_ACTEUR_JUD` | a venir |
| Configurer > Referentiels | Tables de reference | les codes du greffe | `PROC_GTABREF` | a venir |
| Configurer > Referentiels | Natures de document | typologie de la GED | `PROC_GED_NATURE_DOC` | a venir |
| Configurer > Referentiels | Cabinets | les salles d'audience | `PROC_CABINET` | a venir |
| Configurer > Referentiels | Codes chefs IP | parametrage des injonctions | `PROC_PARAM_IP` | a venir |
| Configurer > Referentiels | Bornes d'accueil | les kiosques KBIS | `PROC_BORNE` | a venir |
| Configurer > Modeles et envois | Modeles de redaction | maquettes des decisions | `PROC_REDAA` | a venir |
| Configurer > Modeles et envois | Messagerie | les envois automatiques | `PROC_GESTEMAIL` | dans la V3 |
| Configurer > Argent et exports | Banques | les comptes du greffe | `PROC_GESTBANQ` | dans la V3 |
| Configurer > Argent et exports | Facturation Expresso | parametrage de la facturation | `PROC_PARAMEXPRESSO` | a venir |
| Configurer > Argent et exports | Listes Excel | parametrage des exports | `PROC_PARAM_LISTEEXCEL` | a venir |
| Configurer > Outils | Requetes metier | executer une requete preparee | `PROC_REQUETES` | a venir |
| Configurer > Outils | EDI RPVA | inscriptions mandataires | `PROC_EDI_RPVAINS` | a venir |
| Configurer > Outils | EDI RPJC | traitement / conciliation | `PROC_EDI_RPJC_TRCNC` | a venir |

## Endettement (injonctions, creances) — 16 entrée(s) propres au métier

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Creer une affaire | Injonction de payer | creer une IP | `PROC_IPE` | a venir |
| Ouvrir > Recevoir | Commandes web | traiter les commandes recues | `PROC_COMMANDES_WEB` | dans la V3 |
| Surveiller > Relancer | Relances comptes annuels | DCA en retard | `PROC_DCA_RELANCE` | a venir |
| Surveiller > Relancer | Injonctions comptes annuels | passer a l'injonction | `PROC_DCA_INJ` | a venir |
| Surveiller > Relancer | Relances beneficiaires | DBE en retard | `PROC_DBE_RELANCE` | a venir |
| Surveiller > Relancer | Injonctions beneficiaires | injonction DBE | `PROC_DBE_INJ` | a venir |
| Surveiller > Relancer | Rappels RBE | registre des beneficiaires | `PROC_SRBE_RAPPEL` | a venir |
| Surveiller > Relancer | Retention d'actes | actes retenus au depot | `PROC_ACTRET` | a venir |
| Surveiller > Relancer | Actes rejetes | rejets Infogreffe | `PROC_ACTREJ` | a venir |
| Surveiller > Les instances | Injonctions de payer | les IP en attente | `PROC_SURVIP` | a venir |
| Surveiller > Les instances | Suivi des preventions | les IP de prevention | `PROC_IPSUIVI` | a venir |
| Facturer > Creances | Creances | les creances d'une PC | `PROC_CREANCE` | dans la V3 |
| Facturer > Creances | Ajouter une creance | declarer une creance | `PROC_ADDCREANCE` | a venir |
| Facturer > Creances | Modifier une creance | corriger une declaration | `PROC_MODCREANCE` | a venir |
| Facturer > Creances | Integrer des creances | reprise en lot | `PROC_INTEGCREANCE` | a venir |
| Facturer > Creances | Representation | qui represente le creancier | `PROC_CREANREP` | a venir |

## Tous les métiers — 69 entrée(s) communes (courrier, documents, comptabilité, réglages)

| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |
|---|---|---|---|---|
| Ouvrir > Recevoir | Courrier du jour | le papier entre ce matin | `PROC_ARRIVEE` | dans la V3 |
| Ouvrir > Recevoir | Demandes recues | ce qu'on attend de moi | `PROC_DEMANDE` | dans la V3 |
| Ouvrir > Recevoir | Requisitions | commander des documents | `PROC_REQUI` | dans la V3 |
| Trouver > Une affaire | Mes affaires | tout ce qui m'est confie | `PROC_RECHAFF` | dans la V3 |
| Trouver > Les documents | Documents | chercher dans la GED | `PROC_RECHERCHE_GED` | a venir |
| Trouver > Les documents | Documents de l'affaire | la GED de l'affaire | `PROC_DOC_DEMAT` | dans la V3 |
| Trouver > Les documents | Actes numerises | ce que le scanner a produit | `PROC_ACTES_NUM` | dans la V3 |
| Trouver > Les documents | Controle des scans | verifier avant de valider | `PROC_CONTROLE_VIDIGED` | dans la V3 |
| Trouver > Les documents | Coffre-fort | les pieces mises a l'abri | `PROC_COFFRE_FORT` | dans la V3 |
| Trouver > Les documents | Voir un PDF | lire une piece | `PROC_PDF` | a venir |
| Trouver > Les documents | Voir une image | image ou PDF multi-pages | `PROC_IMAGE` | a venir |
| Trouver > Un chiffre, un reglement | Encaissements | retrouver un reglement | `PROC_RECHENC` | a venir |
| Trouver > Hors du greffe | Interdits de gerer | consultation FNIG | `PROC_VFNIG` | a venir |
| Trouver > Hors du greffe | FIBEN | fichier bancaire des entreprises | `PROC_FIBEN` | a venir |
| Trouver > Hors du greffe | Notifications | echanges entre greffes | `PROC_VNOTIF` | a venir |
| Trouver > Hors du greffe | Tribunal digital | ce qui vient du portail | `PROC_VTRID` | a venir |
| Modifier > Editer un courrier | Courriers divers | les lettres types du greffe | `PROC_COURRIER_DIVERS` | dans la V3 |
| Modifier > Editer un courrier | Courriers en masse | un courrier pour un lot | `PROC_COURRIERM` | a venir |
| Modifier > Editer un courrier | Editions differees | ce qui partira plus tard | `PROC_EDT_DIFF` | a venir |
| Modifier > Editer un courrier | Pivoter des pages | redresser un scan | `PROC_PDF_WRITER` | a venir |
| Modifier > Editer un courrier | Documents ocerises | numerotation des pages | `PROC_NUM` | a venir |
| Modifier > Envoyer | Transmission | envoyer un document dematerialise | `PROC_TRANSDOC` | a venir |
| Modifier > Envoyer | Bordereaux LR | recommandes du jour | `PROC_BORDLR` | a venir |
| Modifier > Envoyer | Retours LRAR | accuses de reception | `PROC_RETOURLRAR` | a venir |
| Modifier > Envoyer | Envoi JAL / BODACC | ce qui part en publication | `PROC_ENV_JAL` | a venir |
| Modifier > Envoyer | Publications BODACC | gerer les annonces | `PROC_GESTBOD` | a venir |
| Modifier > Envoyer | File d'impression | ce qui attend l'imprimante | `PROC_PAPERCUT` | a venir |
| Modifier > Envoyer | Export Excel | sortir une liste | `PROC_LISTEEXCEL` | a venir |
| Surveiller > Les instances | Lots | les lots en cours | `PROC_GESTLOT` | a venir |
| Surveiller > Les envois et les editions | Surveillance BODACC | rejets et annonces en attente | `PROC_SURVBOD` | a venir |
| Surveiller > Les envois et les editions | Suivi d'impression | ce qui est sorti | `PROC_SUIVI_IMPRESSION` | a venir |
| Surveiller > Les envois et les editions | Relancer un fichier | edition bloquee | `PROC_RELANCEFILE` | a venir |
| Surveiller > Les envois et les editions | Relancer un lot | lot d'impression bloque | `PROC_RELANCELOT` | a venir |
| Surveiller > Chiffres | Statistiques | les chiffres du greffe | `PROC_STAT` | dans la V3 |
| Surveiller > Chiffres | Pilotage | tableaux de bord | `PROC_XPILOTAGE` | a venir |
| Surveiller > Chiffres | Numeros manquants | le registre chronologique | `PROC_NUMEROS_MANQUANTS` | dans la V3 |
| Terminer >  | Cloture | fermer une affaire terminee | `PROC_CLOT` | dans la V3 |
| Terminer >  | Archivage | archiver ou radier | `PROC_ARCHIRAD` | dans la V3 |
| Facturer > Facturer | Factures | ce qui est du | `PROC_FACTURE` | dans la V3 |
| Facturer > Facturer | Refacturation | reediter une facture | `PROC_FACTREEDIT` | a venir |
| Facturer > Facturer | Releve de compte | le compte d'un client | `PROC_RELCLI` | a venir |
| Facturer > Facturer | Articles tarifaires | ce qu'on facture | `PROC_ARTICLE` | a venir |
| Facturer > Encaisser | Encaissements | ce qui est rentre | `PROC_ENCAISS` | dans la V3 |
| Facturer > Encaisser | Rejets d'encaissement | un reglement revenu | `PROC_REJET` | a venir |
| Facturer > Encaisser | Remise en banque | porter les reglements en banque | `PROC_REMBANQ` | a venir |
| Facturer > Encaisser | Cheques emis | ce que le greffe a paye | `PROC_EDCHQEMIS` | a venir |
| Facturer > Tenir les comptes | Journal de caisse | la caisse du jour | `PROC_JCAISSE` | a venir |
| Facturer > Tenir les comptes | Journal de vente | les ventes du greffe | `PROC_JVENTE` | a venir |
| Facturer > Tenir les comptes | Livre de repertoire | le journal du repertoire | `PROC_JLR` | a venir |
| Facturer > Tenir les comptes | Lettrage | solder les mouvements | `PROC_VLETTRAGE` | a venir |
| Facturer > Tenir les comptes | Figer les ecritures | comptabiliser la journee | `PROC_COMPTABILISATION` | dans la V3 |
| Facturer > Tenir les comptes | Certification NF525 | journal et grands totaux signes | `PROC_CERTIF` | dans la V3 |
| Configurer > Les clients | Clients | comptes et fiches | `PROC_GCLIENT` | dans la V3 |
| Configurer > Les clients | Fiche client | le detail d'un client | `PROC_FCLIENT` | dans la V3 |
| Configurer > Les clients | Tiers payeurs | qui regle a la place du client | `PROC_GESTTIERSPAYEURS` | a venir |
| Configurer > Mon poste | Mon accueil | tuiles et alertes | `PROC_ACCUEIL_PARAM` | dans la V3 |
| Configurer > Mon poste | Ancienne touche, nouvelle touche | les raccourcis de l'ancien RIG | `PROC_TOUCHES` | dans la V3 |
| Configurer > Mon poste | Actualites | les messages de l'accueil | `PROC_ACTUALITES` | dans la V3 |
| Configurer > Mon poste | Dates de travail | la date du greffe | `PROC_GESTDATE` | a venir |
| Configurer > Mon poste | Comptes des agents | qui ouvre RIG, et avec quels droits | `PROC_UTILISATEUR` | dans la V3 |
| Configurer > Mon poste | Aide du greffe | le wiki interne | `PROC_WIKI` | a venir |
| Configurer > Mon poste | Page web interne | ouvrir un site dans RIG | `PROC_WEB_BROWSER` | a venir |
| Configurer > Referentiels | Compteurs de numeros | rendre un numero donne par erreur | `PROC_COMPTEURS_NUM` | dans la V3 |
| Configurer > Referentiels | Reglages du greffe | les parametres propres au greffe | `PROC_REGLAGES_GREFFE` | dans la V3 |
| Configurer > Modeles et envois | Modeles d'edition | la forme des courriers | `PROC_EDIT_MAQUETTE` | dans la V3 |
| Configurer > Modeles et envois | Sceaux | elements de fusion | `PROC_SCEAU` | a venir |
| Configurer > Modeles et envois | Courriels envoyes | la trace de chaque envoi | `PROC_MAIL_LOT1` | dans la V3 |
| Configurer > Argent et exports | Modes de paiement | les moyens acceptes | `PROC_GESTMODEPAIEMENT` | a venir |
| Configurer > Outils | Supervision | greffes, traitements, erreurs (Amitel) | `PROC_SUPERVISION` | dans la V3 |
