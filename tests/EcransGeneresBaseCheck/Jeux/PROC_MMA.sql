-- Jeu de LecteurMentionsEnMarge (« Mentions en marge », PROC_MMA), lot 3 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19).
-- Identifiants 1 317 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 26/11/2026), creees si
-- absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql). Les gestes : PROC_MMA.gestes (EcrivainMentionsEnMarge,
-- lot 6 du plan « RIG v3 complet », 28/11/2026 ; ses tables en fin de fichier, identifiants 3 600 xxx). L'inscription est la fiche reprise rec.Inscription
-- (migrations, Serveur/Recopie/FichesV3.cs:196) : la base V3 n'a pas de v2.INSCRIPTION. fix-ok: mesure le 26/11/2026 sur la
-- copie de dev (serveur 5231, journal) -- « Invalid object name 'v2.INSCRIPTION' » ; ce jeu creait la table que la V3 n'a pas.
IF OBJECT_ID(N'v2.MENTION_MARGINALE') IS NULL
    CREATE TABLE v2.MENTION_MARGINALE (MNMRG_ID_MNMRG int NOT NULL PRIMARY KEY, MNMRG_ID_INSCR int NULL, MNMRG_DATE datetime NULL,
        MNMRG_TEXTE_MENTION varchar(max) NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_INSCR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_INSCR int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
IF OBJECT_ID(N'v2.ETAT_DEMANDE') IS NULL
    CREATE TABLE v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATDEMAND_LIBELLE_ECRAN varchar(35) NOT NULL);

-- Les etats de demande lus, aux libelles de RIG_DEV (ETAT_DEMANDE lue le 26/11/2026), poses s'ils manquent.
EXEC sp_executesql N'
INSERT INTO v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR, R_ETATDEMAND_LIBELLE_ECRAN)
SELECT v.Valeur, v.Libelle FROM (VALUES (''A'', ''Arrivée''), (''Q'', ''Qualifiée''), (''V'', ''Validée''), (''N'', ''Réclamation''),
                                       (''O'', ''Refus''), (''W'', ''En attente''), (''T'', ''Terminée'')) AS v (Valeur, Libelle)
WHERE NOT EXISTS (SELECT 1 FROM v2.ETAT_DEMANDE AS e WHERE e.R_ETATDEMAND_VALEUR = v.Valeur);

-- Les inscriptions : 1317001 sans bateau, 1317002 maritime (IdBateau, l''INEDT_ID_BTEAU de la V2).
SET IDENTITY_INSERT rec.Inscription ON;
INSERT INTO rec.Inscription (Id, IdBateau, NumeroInscription, TypeInscription, ParTransfert, DateInscription, Renouvelable, EtatInfo, DateDiffusion) VALUES
    (1317001, NULL, ''2026-13701'', ''V'', 0, ''20260101'', 0, ''C'', ''20260101''),
    (1317002, 1317901, ''2026-13702'', ''N'', 0, ''20260101'', 0, ''C'', ''20260101'');
SET IDENTITY_INSERT rec.Inscription OFF;

-- Les demandes. MMA validee (V), refusee (O), en reclamation (N), arrivee (A), en cours de saisie (Q, en cours). Ecartees :
-- 1317106 (processus MJUD, meme inscription) -- jumeau retenu 1317101 ; 1317107 (MMA sans inscription) -- jumeau retenu 1317101.
-- 1317104 n''a pas de nom de demande : la ligne prend celui que PROC_MMA lui donne (PROC_MMA.cs:56).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_EN_COURS, DMND_ID_INSCR, DMND_NOM, DMND_DATE_CREATION) VALUES
    (1317101, ''26017000001'', ''V'', ''MMA'', 0, 1317001, ''MMA sur l''''inscription 2026-13701 - Validée'', ''20260310''),
    (1317102, ''26017000002'', ''O'', ''MMA'', 0, 1317002, ''MMA sur l''''inscription 2026-13702'', ''20260309''),
    (1317103, ''26017000003'', ''N'', ''MMA'', 0, 1317001, ''MMA sur l''''inscription 2026-13701'', ''20260308''),
    (1317104, ''26017000004'', ''A'', ''MMA'', 0, 1317002, NULL, ''20260307''),
    (1317105, ''26017000005'', ''Q'', ''MMA'', 1, 1317001, ''MMA sur l''''inscription 2026-13701'', ''20260306''),
    (1317106, ''26017000006'', ''V'', ''MJUD'', 0, 1317001, ''Hors MMA'', ''20260305''),
    (1317107, ''26017000007'', ''V'', ''MMA'', 0, NULL, ''MMA sans inscription'', ''20260304'');

-- Les mentions de 1317001 : la plus recente est celle que la fiche lit.
INSERT INTO v2.MENTION_MARGINALE (MNMRG_ID_MNMRG, MNMRG_ID_INSCR, MNMRG_DATE, MNMRG_TEXTE_MENTION) VALUES
    (1317201, 1317001, ''20260310'', ''Mention du banc lot 3''), (1317202, 1317001, ''20250101'', ''Ancienne mention'');';

-- ============================================================ LES GESTES (EcrivainMentionsEnMarge, lot 6 du plan « RIG v3 complet »)
-- Ce que Valider lit et ecrit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV et ses valeurs par defaut, lus le
-- 28/11/2026) : la mention (type, numero de registre, « ne pas editer dans le certificat »), sa formalite, les types de mention, les
-- debiteurs de l'inscription, la demande (sa formalite, ses numeros, sa date de validation).
IF COL_LENGTH('v2.MENTION_MARGINALE', 'MNMRG_TYPE_MMA') IS NULL ALTER TABLE v2.MENTION_MARGINALE ADD MNMRG_TYPE_MMA varchar(5) NULL;
IF COL_LENGTH('v2.MENTION_MARGINALE', 'MNMRG_NUM_REGISTRE') IS NULL ALTER TABLE v2.MENTION_MARGINALE ADD MNMRG_NUM_REGISTRE varchar(10) NULL;
IF COL_LENGTH('v2.MENTION_MARGINALE', 'MNMRG_ETAT_EDIT_C') IS NULL ALTER TABLE v2.MENTION_MARGINALE ADD MNMRG_ETAT_EDIT_C bit NOT NULL DEFAULT 0;
IF OBJECT_ID(N'v2.FORMALITE_ENDETTEMENT') IS NULL
    CREATE TABLE v2.FORMALITE_ENDETTEMENT (FMEDT_ID_FMEDT int NOT NULL PRIMARY KEY, FMEDT_ID_INSCR int NULL, FMEDT_ID_MNMRG int NULL,
        FMEDT_DATE_FORMALITE datetime NOT NULL, FMEDT_NUM_REGISTRE varchar(10) NULL, FMEDT_TRAITEMENT_SURV bit NOT NULL DEFAULT 0,
        FMEDT_IS_DEMAT bit NOT NULL DEFAULT 0, FMEDT_VIDI_ARCHIVAGE_ETAT varchar(2) NOT NULL DEFAULT 0);
IF OBJECT_ID(N'v2.TYPE_MMA') IS NULL
    CREATE TABLE v2.TYPE_MMA (R_TYPEMMA_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPEMMA_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_TYPEMMA_ISO varchar(20) NOT NULL, R_TYPEMMA_SYNONYME varchar(16) NOT NULL);
-- DEBITEUR_END est partagee : PROC_VDOSSIER.sql (joue apres ce jeu) la cree aussi, avec DBEND_ID_RPRTR, et ne complete pas une
-- table deja la. Ce jeu la cree donc avec les colonnes des deux, et ajoute les siennes a une table creee par un autre. fix-ok: mesure
-- le 28/11/2026 (EcransGeneresBaseCheck) -- creee ici sans DBEND_ID_RPRTR, « jeu PROC_VDOSSIER.sql : Invalid column name 'DBEND_ID_RPRTR' ».
IF OBJECT_ID(N'v2.DEBITEUR_END') IS NULL
    CREATE TABLE v2.DEBITEUR_END (DBEND_ID_DBEND int NOT NULL PRIMARY KEY, DBEND_ID_RPRTR int NULL, DBEND_ID_INSCR int NULL);
IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_NUM_ORDRE') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_NUM_ORDRE smallint NOT NULL DEFAULT 1;
IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_DESIGNATION') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_DESIGNATION varchar(128) NULL;
IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_DENOMINATION') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_DENOMINATION varchar(max) NULL;
IF COL_LENGTH('v2.DEBITEUR_END', 'DBEND_NUM_SIREN') IS NULL ALTER TABLE v2.DEBITEUR_END ADD DBEND_NUM_SIREN varchar(10) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_FMEDT') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_FMEDT int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_LES_NUMEROS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LES_NUMEROS varchar(128) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_VALIDATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_VALIDATION datetime NULL;

EXEC sp_executesql N'
-- Deux types de mention de RIG_DEV (TYPE_MMA lue le 28/11/2026).
INSERT INTO v2.TYPE_MMA (R_TYPEMMA_VALEUR, R_TYPEMMA_LIBELLE_ECRAN, R_TYPEMMA_ISO, R_TYPEMMA_SYNONYME)
SELECT v.Valeur, v.Libelle, v.Valeur, v.Valeur FROM (VALUES (''ADR'', ''Changement d''''adresse''), (''CONS'', ''Mention de contestation (simple)''))
    AS v (Valeur, Libelle)
WHERE NOT EXISTS (SELECT 1 FROM v2.TYPE_MMA AS t WHERE t.R_TYPEMMA_VALEUR = v.Valeur);

-- Les debiteurs de l''inscription 1317001 : une societe avec son SIREN (le premier : ses numeros vont a la demande), une personne
-- sans denomination ni SIREN (sa designation).
INSERT INTO v2.DEBITEUR_END (DBEND_ID_DBEND, DBEND_ID_INSCR, DBEND_NUM_ORDRE, DBEND_DESIGNATION, DBEND_DENOMINATION, DBEND_NUM_SIREN) VALUES
    (3600101, 1317001, 1, ''ALPHA BANC'', ''Alpha Banc SARL'', ''123456789''), (3600102, 1317001, 2, ''Paul MARTIN'', NULL, NULL);

-- La serie du numero de registre des mentions (EAR) en 2026, a la structure de RIG_DEV (num.Compteur de la copie de dev, lu le
-- 28/11/2026 : ALC, annee sur 2, lettre « e », 7 chiffres).
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''EAR'' AND Annee = 2026)
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES (''EAR'', 2026, ''ALC'', 2, ''e'', 7, 1);';

-- ============================================================ LE GESTE DE « CHANGER LE JUGE-COMMISSAIRE » (PROC_CHM.gestes, lot 6)
-- Le parametrage et les references qu'EcrivainChangementJuge lit, poses ICI et non dans PROC_CHM.sql : les jeux sont joues par ordre
-- de nom, et CIVILITE (PROC_CNI.sql), CODE_AFFAIRE et CODE_ACTE_PROCE (PROC_CORDJRCS.sql), NATURE_AFFAIRE (PROC_JOIN.sql),
-- TYPE_DECISION (PROC_DBE_INJ.sql) sont creees par des jeux qui passent APRES PROC_CHM.sql et avant celui-ci ; les creer avant eux
-- leur enleverait leurs colonnes (le piege de DEBITEUR_END, plus haut). Colonnes ajoutees si absentes, aux types de RIG_DEV (lus le
-- 28/11/2026) ; CIVILITE : DEVELOPPE et SIMPLIFIE en NULL (NOT NULL dans RIG_DEV pour SIMPLIFIE : la table a deja des lignes).
IF COL_LENGTH('v2.CIVILITE', 'R_CIVILITE_DEVELOPPE_EDIT') IS NULL ALTER TABLE v2.CIVILITE ADD R_CIVILITE_DEVELOPPE_EDIT varchar(70) NULL;
IF COL_LENGTH('v2.CIVILITE', 'R_CIVILITE_SIMPLIFIE_EDIT') IS NULL ALTER TABLE v2.CIVILITE ADD R_CIVILITE_SIMPLIFIE_EDIT varchar(35) NULL;
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NATURE_AFF') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NATURE_AFF varchar(10) NULL;
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_CODE_ACTE_PRO') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_CODE_ACTE_PRO varchar(3) NULL;
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_CODE_EVE_PRO_CREA') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_CODE_EVE_PRO_CREA varchar(255) NULL;
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NUM_REPERTOIRE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NUM_REPERTOIRE bit NULL;
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_NAT_AFF_PART') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_NAT_AFF_PART varchar(5) NULL;
IF COL_LENGTH('v2.TYPE_DECISION', 'R_TYPEDECISI_NUM_REPERTOIRE') IS NULL ALTER TABLE v2.TYPE_DECISION ADD R_TYPEDECISI_NUM_REPERTOIRE varchar(5) NULL;
IF COL_LENGTH('v2.TYPE_DECISION', 'R_TYPEDECISI_PARAMETRE_EDITION') IS NULL ALTER TABLE v2.TYPE_DECISION ADD R_TYPEDECISI_PARAMETRE_EDITION varchar(20) NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_DSRCS') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_DSRCS int NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_ID_NINSC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_ID_NINSC int NULL;

EXEC sp_executesql N'
-- Le parametrage du processus (PARAMETRAGE, repris dans soc.Reglage ; valeurs de RIG_DEV, lues le 28/11/2026) : le demandeur en
-- saisine d''office et le mode facture. CHJCM_CODE_AFFAIRE : OPCHJ20, la valeur que PROC_ORDOSIGN.sql pose aussi (joue apres).
INSERT INTO soc.Reglage (Code, Libelle, Valeur)
SELECT c, ''Banc EcransGeneresBase lot 6'', v FROM (VALUES (''DEMANDEUR_SO_CIVILITE'', ''PM''), (''DEMANDEUR_SO_DESIGNATION'', ''Saisine d''''office''),
    (''CHJCM_MODE_FACTU'', ''F''), (''CHJCM_CODE_AFFAIRE'', ''OPCHJ20'')) AS r (c, v)
WHERE NOT EXISTS (SELECT 1 FROM soc.Reglage AS x WHERE x.Code = r.c);
IF NOT EXISTS (SELECT 1 FROM v2.CIVILITE WHERE R_CIVILITE_VALEUR = ''PM'')
    INSERT INTO v2.CIVILITE (R_CIVILITE_VALEUR, R_CIVILITE_ABREGE_EDIT) VALUES (''PM'', '''');
-- Le code affaire de l''ordonnance (OPCHJCM de RIG_DEV : ordonnance du president RP, repertoire general, nature OPCHJC, saisine
-- d''office ; sans evenement de creation, comme RIG_DEV ou son NJCCHJC n''est pas dans CODE_EVE_PRO), son acte, sa nature. Son role :
-- JROP6, une serie du banc a la structure de JROP, et non JROP (celle de RIG_DEV) : OPCHJ20 est aussi le code de l''instance ORDO G
-- de PROC_ORDOSIGN.sql, et un type JROP la mettrait -- avec l''ordonnance du geste -- dans les listes JOIN, XAFF, MJUD, XLIEN, XXAFF
-- (CODE_AFFAIRE.R_CODEAFFAIR_TYPE_NUM_ROLE, LecteurJonctions.TypesMere...), et prendrait un numero JROP aux oppositions des jeux IP.
IF NOT EXISTS (SELECT 1 FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = ''OPCHJ20'')
    INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_LIBELLE_ECRAN, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE,
        R_CODEAFFAIR_NUM_REPERTOIRE, R_CODEAFFAIR_NATURE_AFF, R_CODEAFFAIR_CODE_ACTE_PRO, R_CODEAFFAIR_NAT_AFF_PART, R_CODEAFFAIR_CODE_EVE_PRO_CREA)
    VALUES (''OPCHJ20'', ''Changement de juge-commissaire du banc'', ''JROP6'', ''RP'', 1, ''OPCHJC6'', ''SO6'', ''0A'', NULL);
IF NOT EXISTS (SELECT 1 FROM v2.CODE_ACTE_PROCE WHERE R_CODEACTEPR_VALEUR = ''SO6'')
    INSERT INTO v2.CODE_ACTE_PROCE (R_CODEACTEPR_VALEUR, R_CODEACTEPR_LIBELLE_ECRAN, R_CODEACTEPR_ISO) VALUES (''SO6'', ''Saisine d''''office du banc'', ''20'');
IF NOT EXISTS (SELECT 1 FROM v2.NATURE_AFFAIRE WHERE R_NATUREAFFA_VALEUR = ''OPCHJC6'')
    INSERT INTO v2.NATURE_AFFAIRE (R_NATUREAFFA_VALEUR, R_NATUREAFFA_LIBELLE_ECRAN) VALUES (''OPCHJC6'', ''Changement de juge-commissaire du banc'');
-- Les types de decision : celui de l''ordonnance (minutes JGOP, son edition) et un type a la serie de minutes fausse (le refus).
INSERT INTO v2.TYPE_DECISION (R_TYPEDECISI_VALEUR, R_TYPEDECISI_LIBELLE_ECRAN, R_TYPEDECISI_NUM_REPERTOIRE, R_TYPEDECISI_PARAMETRE_EDITION) VALUES
    (''OPCHJ6'', ''Ordonnance de changement de juge-commissaire'', ''JGOP'', ''ORDO_CHJCM6''),
    (''OPCHJ6X'', ''Ordonnance du banc mal numerotee'', ''JGAFF'', ''ORDO_CHJCM6'');
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_TYPE_GENERATION) VALUES
    (''ORDO_CHJCM6'', ''Ordonnance de changement de juge-commissaire'', ''FUSION_RIG'');
-- Les series de l''ordonnance, a la structure de RIG_DEV (num.Compteur de la copie de dev, lu le 28/11/2026) : role JROP6 (celle de
-- JROP : annee sur 4, « OP », 5 chiffres), repertoire general JGAFF (annee sur 4, 6 chiffres). Les numeros sont ceux de l''annee de
-- saisine (CreationV2.CreerInstanceAsync) : le geste (PROC_CHM.gestes) saisit un acte de 2022, annee qu''aucun autre jeu ne numerote,
-- pour ne pas decaler les repertoires 2026 qu''attendent PROC_IPE, PROC_IPSUIVI et PROC_SURVIP. JGAFF 2026 : la definition de
-- PROC_DBE_INJ.sql et PROC_IPE.sql, dont le IF NOT EXISTS (tout JGAFF) saute ensuite.
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''JROP6'' AND Annee = 2022)
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES (''JROP6'', 2022, ''ALC'', 4, ''OP'', 5, 1);
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''JGAFF'' AND Annee = 2022)
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES (''JGAFF'', 2022, ''AC'', 4, 6, 1);
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''JGAFF'' AND Annee = 2026)
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES (''JGAFF'', 2026, ''AC'', 4, 6, 1);';
