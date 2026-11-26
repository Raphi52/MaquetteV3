-- Jeu de LecteurTaxesEtTours (« Taxes et tours de rôle », PROC_TAXESETTOURS), lot 7 du plan « RIG v3 100 % fonctionnel »
-- (plan-676a4b19). Identifiants 1 707 xxx. Tables aux types de RIG_DEV ; une table deja creee par un autre jeu recoit seulement les
-- colonnes lues ici, nullables. PAS d'IDENTITY. La periode est celle de RIG v2 par defaut : du 1er janvier de l'annee du banc a son
-- jour (OPE_PERIODE.cs:31-32) ; les lignes datees se posent par rapport a @aujourdhui.
-- AUCUNE procedure collective neuve (PROC_MPC lit toutes les PC) : les designations et les chiffres se posent sur les PC 1316104,
-- 1316105, 1316106 de PROC_MPC.sql (non exterieures) ; la date de designation (GRRPC_DATE_DEBUT) prime sur celle de la PC.
-- Ce qui est retenu ou ecarte, et pourquoi : PROC_TAXESETTOURS.attendu.
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_OUVERTURE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_OUVERTURE datetime NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_DATE_SAUVEGARDE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_DATE_SAUVEGARDE datetime NULL;
IF OBJECT_ID(N'v2.MANDATAIRE_PC') IS NULL
    CREATE TABLE v2.MANDATAIRE_PC (MNDPC_ID_MNDPC int NOT NULL PRIMARY KEY, MNDPC_CIVILITE varchar(7) NOT NULL, MNDPC_NOM varchar(100) NOT NULL,
        MNDPC_COMPLEMENT_NOM varchar(100) NULL);
IF COL_LENGTH('v2.MANDATAIRE_PC', 'MNDPC_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE_PC ADD MNDPC_ABREGE varchar(16) NULL;
IF OBJECT_ID(N'v2.GERER_PC') IS NULL
    CREATE TABLE v2.GERER_PC (GRRPC_ID_GRRPC int NOT NULL PRIMARY KEY, GRRPC_ID_MNDPC int NOT NULL, GRRPC_ID_PRCLL int NULL,
        GRRPC_NUM_ORDRE smallint NOT NULL, GRRPC_FONCTION_PC varchar(5) NOT NULL, GRRPC_FIN_MISSION bit NOT NULL, GRRPC_ID_PREVE int NULL);
IF COL_LENGTH('v2.GERER_PC', 'GRRPC_DATE_DEBUT') IS NULL ALTER TABLE v2.GERER_PC ADD GRRPC_DATE_DEBUT datetime NULL;
IF OBJECT_ID(N'v2.CHIFFRES_PC') IS NULL
    CREATE TABLE v2.CHIFFRES_PC (CHFPC_ID_CHFPC int NOT NULL PRIMARY KEY, CHFPC_ID_PRCLL int NOT NULL, CHFPC_CHIFFRE_AFFAIRE money NULL,
        CHFPC_SALARIES_OUVERTURE int NULL);
IF OBJECT_ID(N'v2.CODE_CHIFFRE') IS NULL
    CREATE TABLE v2.CODE_CHIFFRE (R_CODECHIFFR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODECHIFFR_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_CODECHIFFR_TAXE bit NULL, R_CODECHIFFR_ACOMPTE bit NULL);
IF OBJECT_ID(N'v2.AUTRE_CHIFFRE') IS NULL
    CREATE TABLE v2.AUTRE_CHIFFRE (ACHFF_ID_ACHFF int NOT NULL PRIMARY KEY, ACHFF_CODE_CHIFFRE varchar(10) NULL, ACHFF_ABREGE_ORGANE varchar(16) NULL,
        ACHFF_FONCTION_PC varchar(5) NULL, ACHFF_DATE datetime NULL, ACHFF_MONTANT money NULL);

-- Les conditions « tour » : celles de la copie de dev (soc.Reglage, reprise de PARAMETRAGE), telles quelles.
-- Mandataires : MA17 « ALPHA MANDATAIRE SELARL », AD17 « BETA ADMINISTRATEUR SCP ».
-- Designations : MA17 mandataire (MDJ) de 1316104 le jour du banc ; MA17 liquidateur (LIQ) de 1316105 le 1er janvier (borne retenue) ;
-- MA17 MDJ de 1316106 le 31 decembre d'avant (borne ecartee) ; AD17 administrateur (ADJ) de 1316104 le jour du banc ; AD17 commissaire
-- a l'execution du plan (CEP) de 1316105 : fonction hors des tours (Tours.cs:158, ecartee ; jumeau : ADJ).
-- Chiffres : 1316104 CA 300, 10 salaries (tour 1 MAND et tour 1 ADJ) ; 1316105 CA 5 000, 20 salaries (tour 4 MAND) ; 1316106 CA 0.
-- Taxes et acomptes : codes TX17 (taxe), AC17 (acompte), NX17 (ni l'un ni l'autre, ecarte). MA17 : MDJ 100 + 20 (taxe, le jour),
-- LIQ 30 (taxe, le 1er janvier, retenue), LIQ 999 (taxe, le 31 decembre d'avant, ecartee), MDJ 500 (NX17, ecartee). AD17 : ADJ 75,25
-- (acompte), CEP 10 (taxe).
EXEC sp_executesql N'
DECLARE @an1 datetime = DATEFROMPARTS(YEAR(@aujourdhui), 1, 1);
DECLARE @avant datetime = DATEFROMPARTS(YEAR(@aujourdhui) - 1, 12, 31);
INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES
    (''4D_TOUR_MAND_N'', ''Tour N (banc)'', ''CHFPC_SALARIES_OUVERTURE = 0 OR CHFPC_SALARIES_OUVERTURE is null''),
    (''4D_TOUR_MAND_1'', ''Tour 1 (banc)'', ''(CHFPC_SALARIES_OUVERTURE > 0 AND CHFPC_SALARIES_OUVERTURE <=15) AND CHFPC_CHIFFRE_AFFAIRE <= 400''),
    (''4D_TOUR_MAND_2'', ''Tour 2 (banc)'', ''(CHFPC_SALARIES_OUVERTURE > 0 AND CHFPC_SALARIES_OUVERTURE <=15) AND CHFPC_CHIFFRE_AFFAIRE > 400''),
    (''4D_TOUR_MAND_3'', ''Tour 3 (banc)'', ''CHFPC_SALARIES_OUVERTURE > 15 AND CHFPC_CHIFFRE_AFFAIRE <= 400''),
    (''4D_TOUR_MAND_4'', ''Tour 4 (banc)'', ''CHFPC_SALARIES_OUVERTURE > 15 AND CHFPC_CHIFFRE_AFFAIRE > 400''),
    (''4D_TOUR_ADJ_N'', ''Tour N (banc)'', ''CHFPC_CHIFFRE_AFFAIRE =0 OR CHFPC_CHIFFRE_AFFAIRE is null''),
    (''4D_TOUR_ADJ_1'', ''Tour 1 (banc)'', ''CHFPC_CHIFFRE_AFFAIRE > 0 and CHFPC_CHIFFRE_AFFAIRE <= 800''),
    (''4D_TOUR_ADJ_2'', ''Tour 2 (banc)'', ''CHFPC_CHIFFRE_AFFAIRE > 800 and CHFPC_CHIFFRE_AFFAIRE <= 3000''),
    (''4D_TOUR_ADJ_3'', ''Tour 3 (banc)'', ''CHFPC_CHIFFRE_AFFAIRE > 3000 and CHFPC_CHIFFRE_AFFAIRE <= 12000''),
    (''4D_TOUR_ADJ_4'', ''Tour 4 (banc)'', ''CHFPC_CHIFFRE_AFFAIRE > 12000'');
INSERT INTO v2.MANDATAIRE_PC (MNDPC_ID_MNDPC, MNDPC_CIVILITE, MNDPC_NOM, MNDPC_COMPLEMENT_NOM, MNDPC_ABREGE) VALUES
    (1707501, ''ME'', ''ALPHA MANDATAIRE'', ''SELARL'', ''MA17''), (1707502, ''ME'', ''BETA ADMINISTRATEUR'', ''SCP'', ''AD17'');
INSERT INTO v2.GERER_PC (GRRPC_ID_GRRPC, GRRPC_ID_MNDPC, GRRPC_ID_PRCLL, GRRPC_NUM_ORDRE, GRRPC_FONCTION_PC, GRRPC_FIN_MISSION, GRRPC_DATE_DEBUT) VALUES
    (1707601, 1707501, 1316104, 17, ''MDJ'', 0, @aujourdhui), (1707602, 1707501, 1316105, 17, ''LIQ'', 0, @an1),
    (1707603, 1707501, 1316106, 17, ''MDJ'', 0, @avant), (1707604, 1707502, 1316104, 18, ''ADJ'', 0, @aujourdhui),
    (1707605, 1707502, 1316105, 18, ''CEP'', 0, @aujourdhui);
INSERT INTO v2.CHIFFRES_PC (CHFPC_ID_CHFPC, CHFPC_ID_PRCLL, CHFPC_CHIFFRE_AFFAIRE, CHFPC_SALARIES_OUVERTURE) VALUES
    (1707701, 1316104, 300, 10), (1707702, 1316105, 5000, 20), (1707703, 1316106, 0, 0);
INSERT INTO v2.CODE_CHIFFRE (R_CODECHIFFR_VALEUR, R_CODECHIFFR_LIBELLE_ECRAN, R_CODECHIFFR_TAXE, R_CODECHIFFR_ACOMPTE) VALUES
    (''TX17'', ''Taxe du banc'', 1, 0), (''AC17'', ''Acompte du banc'', 0, 1), (''NX17'', ''Autre chiffre du banc'', 0, 0);
INSERT INTO v2.AUTRE_CHIFFRE (ACHFF_ID_ACHFF, ACHFF_CODE_CHIFFRE, ACHFF_ABREGE_ORGANE, ACHFF_FONCTION_PC, ACHFF_DATE, ACHFF_MONTANT) VALUES
    (1707801, ''TX17'', ''MA17'', ''MDJ'', @aujourdhui, 100), (1707802, ''TX17'', ''MA17'', ''MDJ'', @aujourdhui, 20),
    (1707803, ''TX17'', ''MA17'', ''LIQ'', @an1, 30), (1707804, ''TX17'', ''MA17'', ''LIQ'', @avant, 999),
    (1707805, ''NX17'', ''MA17'', ''MDJ'', @aujourdhui, 500), (1707806, ''AC17'', ''AD17'', ''ADJ'', @aujourdhui, 75.25),
    (1707807, ''TX17'', ''AD17'', ''CEP'', @aujourdhui, 10);', N'@aujourdhui datetime', @aujourdhui;
