-- Jeu de LecteurPartiesInstance et EcrivainPartiesInstance (« Parties d'une instance », PROC_PARTIES), lot 3 du plan « RIG v3
-- 100 % fonctionnel » (plan-676a4b19). Identifiants 1 312 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de
-- SQL-DEV\DEV, lu le 26/11/2026), creees si absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql).
-- Les cases d'aide juridictionnelle sont NOT NULL dans RIG_DEV : ajoutees NOT NULL DEFAULT 0, les parties deja posees
-- (PROC_MJUD.sql) valent « sans AJ ».
-- L'instance 1312001 n'a pas de code affaire : hors de MJUD, de MDJUD et de MPC. Les parties de PROC_MJUD.sql (instances
-- 1200001 et 1200007) sont dans la liste : PROC_PARTIES ne filtre pas le type de l'instance (PROC_PARTIES.attendu le dit).
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF COL_LENGTH('v2.INSTANCE', 'INSTN_TOP_EXPERIAN') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TOP_EXPERIAN bit NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_MAJ_INFOG') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_MAJ_INFOG datetime NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_NUMERO_SOURCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NUMERO_SOURCE varchar(36) NULL;
IF OBJECT_ID(N'v2.PARTIE') IS NULL
    CREATE TABLE v2.PARTIE (PRT_ID_PRT int NOT NULL PRIMARY KEY, PRT_ID_INSTN int NOT NULL, PRT_NUM_ORDRE smallint NOT NULL,
        PRT_CIVILITE varchar(7) NOT NULL, PRT_DESIGNATION varchar(512) NULL, PRT_NOM varchar(512) NULL, PRT_SIREN varchar(10) NULL,
        PRT_MODE_PRESENTATION varchar(3) NULL, PRT_QUALIF_PARTIE varchar(5) NOT NULL, PRT_DATE_ACTE datetime NULL);
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_PRTRP') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_PRTRP int NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_ADRBS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_ADRBS int NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_MNDTR_POS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_MNDTR_POS int NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_NUMERO_SOURCE') IS NULL ALTER TABLE v2.PARTIE ADD PRT_NUMERO_SOURCE varchar(36) NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_AIDE_JURIDIC_TOTALE') IS NULL
    ALTER TABLE v2.PARTIE ADD PRT_AIDE_JURIDIC_TOTALE bit NOT NULL CONSTRAINT DF_PARTIE_AJ_TOTALE DEFAULT 0;
IF COL_LENGTH('v2.PARTIE', 'PRT_AIDE_JURIDIC_PARTIELLE') IS NULL
    ALTER TABLE v2.PARTIE ADD PRT_AIDE_JURIDIC_PARTIELLE bit NOT NULL CONSTRAINT DF_PARTIE_AJ_PARTIELLE DEFAULT 0;
IF COL_LENGTH('v2.PARTIE', 'PRT_AIDE_JURIDIC_NUM') IS NULL ALTER TABLE v2.PARTIE ADD PRT_AIDE_JURIDIC_NUM varchar(30) NULL;
-- L'adresse est la fiche reprise rec.Adresse (l'ADRESSE_BASE de la V2, Serveur/Recopie/FichesV3.cs). fix-ok: mesure le
-- 26/11/2026 -- la fiche d'une partie de la copie de dev tombait en erreur : la base V3 n'a pas de v2.ADRESSE_BASE.
SET IDENTITY_INSERT rec.Adresse ON;
INSERT INTO rec.Adresse (Id, NumeroVoie, ComplementNumero, TypeVoie, NomVoie, CodePostal, Localite, EtatInfo)
    VALUES (1312501, '12', NULL, 'rue', 'du Banc', '75001', 'PARIS', 'C');
SET IDENTITY_INSERT rec.Adresse OFF;
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL);
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ABREGE varchar(16) NULL;

-- L'instance et ses parties. Retenues : 1312101, 1312102 (AJ totale, repertoriee : le repertorie 1315001 de
-- PROC_MODIF_REPER.sql), 1312106 (qualite « DOIP » : la premiere lettre la range chez les demandeurs, Instance.cs:1264-1282),
-- 1312103 (AJ partielle), 1312104 (intervenant). Aucun mandataire : v2.MANDATAIRE est la liste de PROC_MANDATAIRE (une ligne
-- ajoutee ici entrerait dans son ecran) ; le champ « Mandataire » se lit vide. Ecartees (Instance.ListeParties, Instance.cs:1253) : 1312105 sans
-- designation -- jumeau retenu 1312103 ; 1312107 qualite vide -- jumeau retenu 1312101.
-- Le nom courant de l'instance est celui que MAJNomInstance calcule (« demandeur # defendeur », Instance.cs:931-962) :
-- renommer le premier demandeur le recalcule (PROC_PARTIES.gestes).
EXEC sp_executesql N'
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TOP_EXPERIAN, INSTN_DATE_MAJ_INFOG, INSTN_NUMERO_SOURCE) VALUES
    (1312001, ''2026Z13201'', ''ALPHA PARTIES SARL # GAMMA Lucie'', 0, ''20200101'', NULL);
INSERT INTO v2.PARTIE (PRT_ID_PRT, PRT_ID_INSTN, PRT_NUM_ORDRE, PRT_CIVILITE, PRT_DESIGNATION, PRT_NOM, PRT_MODE_PRESENTATION, PRT_QUALIF_PARTIE,
    PRT_ID_PRTRP, PRT_ID_ADRBS, PRT_ID_MNDTR_POS, PRT_NUMERO_SOURCE, PRT_AIDE_JURIDIC_TOTALE, PRT_AIDE_JURIDIC_PARTIELLE, PRT_AIDE_JURIDIC_NUM) VALUES
    (1312101, 1312001, 1, ''PM'', ''ALPHA PARTIES SARL'', NULL, NULL, ''D'', NULL, 1312501, NULL, NULL, 0, 0, NULL),
    (1312102, 1312001, 2, ''M.'', ''BETA Paul'', NULL, ''RA'', ''D'', 1315001, NULL, NULL, ''SOURCE-1312102'', 1, 0, ''AJ-2026-0001''),
    (1312106, 1312001, 3, ''PM'', ''EPSILON IP'', NULL, NULL, ''DOIP'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312103, 1312001, 1, ''Mme'', ''GAMMA Lucie'', NULL, NULL, ''F'', NULL, NULL, NULL, NULL, 0, 1, ''AJ-2026-0003''),
    (1312104, 1312001, 1, ''PM'', ''DELTA INTERVENANT SAS'', NULL, NULL, ''I'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312105, 1312001, 2, ''M.'', NULL, NULL, NULL, ''F'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312107, 1312001, 4, ''PM'', ''ZETA VIDE'', NULL, NULL, '''', NULL, NULL, NULL, NULL, 0, 0, NULL);';

-- Quatre instances au nom RETOUCHE (different du calcul « demandeur # defendeur ») : renommer leur partie passe par la fin de
-- RefreshNomInstance (Instance.cs:903-920), ou seul l'ancien nom de la partie est remplace dans le nom de l'instance. Sur
-- RIG_DEV (lu le 26/11/2026), 213 720 instances sur 493 768 sont dans ce cas : 187 546 sans « # » (ex. 2026JC01285 « LOCAM »,
-- l'injonction nommee par son seul demandeur), 26 173 avec un « # » (ex. 2025R00344 « ... SISTEL "CHATEAU PIMO" »), 1 avec
-- plusieurs. Roles en « 2026Y » : hors du critere « 2026 Z 132 » de PROC_PARTIES.recherches. Sans code affaire : hors de MJUD,
-- de MDJUD et de MPC.
--   1312002 sans « # » : « Societe » ajoute devant le demandeur.
--   1312003 avec un « # » : « Ste » devant le demandeur, « et autres » apres le defendeur.
--   1312004 avec deux « # » : RIG v2 ne touche plus au nom (Instance.cs:917-918).
--   1312005 raccourci a « OMICRON » : l'ancien nom est coupe a la longueur du morceau avant d'etre cherche (Left, Instance.cs:908).
EXEC sp_executesql N'
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TOP_EXPERIAN, INSTN_DATE_MAJ_INFOG, INSTN_NUMERO_SOURCE) VALUES
    (1312002, ''2026Y13202'', ''Société LAMBDA BAIL'', 0, NULL, NULL),
    (1312003, ''2026Y13203'', ''Sté PSI CONSEIL # TAU Anne et autres'', 0, NULL, NULL),
    (1312004, ''2026Y13204'', ''CHI SA # PHI SARL # RHO'', 0, NULL, NULL),
    (1312005, ''2026Y13205'', ''OMICRON'', 0, NULL, NULL);
INSERT INTO v2.PARTIE (PRT_ID_PRT, PRT_ID_INSTN, PRT_NUM_ORDRE, PRT_CIVILITE, PRT_DESIGNATION, PRT_NOM, PRT_MODE_PRESENTATION, PRT_QUALIF_PARTIE,
    PRT_ID_PRTRP, PRT_ID_ADRBS, PRT_ID_MNDTR_POS, PRT_NUMERO_SOURCE, PRT_AIDE_JURIDIC_TOTALE, PRT_AIDE_JURIDIC_PARTIELLE, PRT_AIDE_JURIDIC_NUM) VALUES
    (1312111, 1312002, 1, ''PM'', ''LAMBDA BAIL'', NULL, NULL, ''D'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312112, 1312002, 1, ''PM'', ''MU BOULANGERIE'', NULL, NULL, ''F'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312121, 1312003, 1, ''PM'', ''PSI CONSEIL'', NULL, NULL, ''D'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312122, 1312003, 1, ''Mme'', ''TAU Anne'', NULL, NULL, ''F'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312131, 1312004, 1, ''PM'', ''CHI SA'', NULL, NULL, ''D'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312132, 1312004, 1, ''PM'', ''PHI SARL'', NULL, NULL, ''F'', NULL, NULL, NULL, NULL, 0, 0, NULL),
    (1312141, 1312005, 1, ''PM'', ''OMICRON DISTRIBUTION'', NULL, NULL, ''D'', NULL, NULL, NULL, NULL, 0, 0, NULL);';
