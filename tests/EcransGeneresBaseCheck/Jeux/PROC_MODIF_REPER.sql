-- Jeu de LecteurRepertoire et EcrivainRepertoire (« Répertoire », PROC_MODIF_REPER), lot 3 du plan « RIG v3 100 % fonctionnel »
-- (plan-676a4b19). Identifiants 1 315 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le
-- 26/11/2026), creees si absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql).
-- Aucun mandataire postulant : v2.MANDATAIRE est la liste de PROC_MANDATAIRE (une ligne ajoutee ici entrerait dans son ecran).
-- La partie liee au repertorie 1315001 est posee par PROC_PARTIES.sql (1312102, joue apres ce jeu, avant les gestes).
-- Le client, le mandataire postulant et l'adresse des gestes sont ceux d'autres jeux, joues avant les gestes : le client
-- 1316601 MPCDIV (PROC_MPC.sql), le mandataire 1500101 ARD15 (PROC_MANDATAIRE.sql), l'adresse 1312501 (PROC_PARTIES.sql) --
-- une ligne ajoutee ici a rec.Client ou v2.MANDATAIRE entrerait dans les ecrans qui les listent.
IF OBJECT_ID(N'v2.REPERTORIE') IS NULL
    CREATE TABLE v2.REPERTORIE (PRTRP_ID_PRTRP int NOT NULL PRIMARY KEY, PRTRP_ABREGE varchar(16) NOT NULL, PRTRP_CIVILITE varchar(7) NOT NULL,
        PRTRP_DESIGNATION varchar(512) NULL, PRTRP_ADRESSE_MAIL varchar(70) NULL, PRTRP_ACTIF bit NOT NULL, PRTRP_ID_CLIEN int NULL,
        PRTRP_ID_MNDTR_POS int NULL, PRTRP_MODE_PRESENTATION varchar(3) NULL, PRTRP_TEXTE_ASSISTANCE nvarchar(max) NULL);
IF COL_LENGTH('v2.REPERTORIE', 'PRTRP_ID_ADRBS') IS NULL ALTER TABLE v2.REPERTORIE ADD PRTRP_ID_ADRBS int NULL;
IF OBJECT_ID(N'v2.MODE_PRESENTATION') IS NULL
    CREATE TABLE v2.MODE_PRESENTATION (R_MODEPRESEN_VALEUR varchar(3) NOT NULL PRIMARY KEY, R_MODEPRESEN_SAISIE_REPRESENTANT bit NULL,
        R_MODEPRESEN_SAISIE_ASSISTANT bit NULL);
IF COL_LENGTH('v2.MODE_PRESENTATION', 'R_MODEPRESEN_UTILISABLE_SAISIE') IS NULL ALTER TABLE v2.MODE_PRESENTATION ADD R_MODEPRESEN_UTILISABLE_SAISIE bit NULL;
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL);
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ABREGE varchar(16) NULL;
IF OBJECT_ID(N'v2.CODE_AFFAIRE') IS NULL
    CREATE TABLE v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEAFFAIR_LIBELLE_ECRAN varchar(100) NOT NULL,
        R_CODEAFFAIR_TYPE_NUM_ROLE varchar(5) NULL, R_CODEAFFAIR_TYPE_INSTANCE varchar(2) NOT NULL);
IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_ABREGE_DEMANDEUR') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_ABREGE_DEMANDEUR varchar(16) NULL;
IF OBJECT_ID(N'v2.PARTIE') IS NULL
    CREATE TABLE v2.PARTIE (PRT_ID_PRT int NOT NULL PRIMARY KEY, PRT_ID_INSTN int NOT NULL, PRT_NUM_ORDRE smallint NOT NULL,
        PRT_CIVILITE varchar(7) NOT NULL, PRT_DESIGNATION varchar(512) NULL, PRT_NOM varchar(512) NULL, PRT_SIREN varchar(10) NULL,
        PRT_MODE_PRESENTATION varchar(3) NULL, PRT_QUALIF_PARTIE varchar(5) NOT NULL, PRT_DATE_ACTE datetime NULL);
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_PRTRP') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_PRTRP int NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_ABREGE') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ABREGE varchar(16) NULL;

-- Les modes de presentation, aux valeurs de RIG_DEV (MODE_PRESENTATION lue le 26/11/2026) : RA represente par avocat (saisie du
-- representant), EA en personne et assiste (saisie de l'assistant), EP present en personne (ni l'un ni l'autre : le client), R
-- « Représenté(e) par », le seul mode de RIG_DEV qui n'est plus utilisable en saisie (R_MODEPRESEN_UTILISABLE_SAISIE = 0).
EXEC sp_executesql N'
INSERT INTO v2.MODE_PRESENTATION (R_MODEPRESEN_VALEUR, R_MODEPRESEN_SAISIE_REPRESENTANT, R_MODEPRESEN_SAISIE_ASSISTANT, R_MODEPRESEN_UTILISABLE_SAISIE)
    VALUES (''RA'', 1, 0, 1), (''EA'', 0, 1, 1), (''EP'', 0, 0, 1), (''R'', 1, 0, 0);';

-- Les repertories. 1315001 represente par avocat (RA) : un client pose a tort, que Valider efface (OPE_EDITION_MODIF_REPER.cs:215-218),
-- et un texte d'assistance que RA ne demande pas. 1315002 inactif. 1315003 sans mode : client et texte effaces a la sauvegarde.
-- 1315004 en personne et assiste (EA) : son client (1316601) et son texte d'assistance restent ; son adresse 1312501.
-- Le code affaire RP13 a 1315001 pour demandeur ; il n'a pas de type de numero de role : aucune instance n'en entre dans MJUD.
EXEC sp_executesql N'
INSERT INTO v2.REPERTORIE (PRTRP_ID_PRTRP, PRTRP_ABREGE, PRTRP_CIVILITE, PRTRP_DESIGNATION, PRTRP_ADRESSE_MAIL, PRTRP_ACTIF, PRTRP_ID_CLIEN,
    PRTRP_ID_MNDTR_POS, PRTRP_MODE_PRESENTATION, PRTRP_TEXTE_ASSISTANCE, PRTRP_ID_ADRBS) VALUES
    (1315001, ''REP-ALPHA'', ''Me'', ''Cabinet Alpha'', ''alpha@banc.fr'', 1, 99, NULL, ''RA'', N''assiste par X'', NULL),
    (1315002, ''REP-BETA'', ''M.'', ''Beta Conseil'', NULL, 0, NULL, NULL, NULL, NULL, NULL),
    (1315003, ''REP-GAMMA'', ''Mme'', ''Gamma'', NULL, 1, 77, NULL, NULL, N''assistee par Y'', NULL),
    (1315004, ''REP-DELTA'', ''M.'', ''Delta Assiste'', NULL, 1, 1316601, NULL, ''EA'', N''assiste par Z'', 1312501);
INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_LIBELLE_ECRAN, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE, R_CODEAFFAIR_ABREGE_DEMANDEUR)
    VALUES (''RP13'', ''Code du repertorie du banc lot 3'', NULL, ''07'', ''REP-ALPHA'');';
