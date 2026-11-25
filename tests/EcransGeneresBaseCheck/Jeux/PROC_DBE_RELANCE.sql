-- Jeu de LecteurRelancesBeneficiaires (« Relances beneficiaires », PROC_DBE_RELANCE), lot 1 du plan « brancher sur la
-- base les procedures encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11002xx. Ce qui est retenu ou
-- ecarte, et pourquoi : PROC_DBE_RELANCE.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), seulement les
-- colonnes lues ; colonnes manquantes ajoutees (NULL : la table a deja des lignes), lignes par sp_executesql (une colonne
-- ajoutee dans ce lot d'instructions n'existe pas encore quand SQL Server le compile).
-- Les diligences que ce lecteur ECARTE parce qu'elles sont a l'ecran « Injonctions beneficiaires » (injonction IDBE
-- avec instance, relance restee sans effet) sont dans PROC_DBE_INJ.sql : un jeu ne pose pas de ligne dans l'ecran d'un
-- autre. Ici, leurs jumeaux qui restent des relances : 206 (delai qui expire aujourd'hui), 207 (dossier radie), 208
-- (relance IDBE sans instance).
IF OBJECT_ID(N'v2.DILIGENCE_RCS') IS NULL
    CREATE TABLE v2.DILIGENCE_RCS (DLGRC_ID_DLGRC int NOT NULL PRIMARY KEY, DLGRC_ID_ACTRC int NULL,
        DLGRC_NATURE_DILIGENCE varchar(10) NOT NULL, DLGRC_ETAT_DILIGENCE varchar(1) NULL, DLGRC_ETAT_RELANCE varchar(1) NULL);
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_DATE_REGULARISATION') IS NULL
    ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_DATE_REGULARISATION datetime NULL;
IF OBJECT_ID(N'v2.RELANCE_RCS') IS NULL
    CREATE TABLE v2.RELANCE_RCS (RLCRC_ID_RLCRC int NOT NULL PRIMARY KEY, RLCRC_ID_DLGRC int NULL, RLCRC_NUM_ORDRE smallint NOT NULL,
        RLCRC_DATE_RELANCE datetime NOT NULL, RLCRC_DATE_LIMITE datetime NULL);
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_TYPE_RELANCE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_TYPE_RELANCE varchar(16) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_ID_INSTN') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_ID_INSTN int NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_FACTURE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_FACTURE bit NULL;

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant), 207 radie, et l'action de chaque diligence.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1100201, N'BENEF ALPHA SARL'), (1100202, N'BENEF BETA SAS'), (1100203, N'BENEF GAMMA SARL'),
             (1100204, N'BENEF DELTA SAS'), (1100205, N'BENEF EPSILON SARL'), (1100206, N'BENEF ZETA SAS'),
             (1100207, N'BENEF ETA SARL'), (1100208, N'BENEF THETA SAS'), (1100209, N'BENEF IOTA SARL'),
             (1100210, N'BENEF KAPPA SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, CASE WHEN v.Id = 1100207 THEN DATEADD(MONTH, -1, @aujourdhui) END
FROM (VALUES (1100201), (1100202), (1100203), (1100204), (1100205), (1100206), (1100207), (1100208), (1100209), (1100210)) AS v (Id);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'RDBE', 'C', 'V', 0
FROM (VALUES (1100201), (1100202), (1100203), (1100204), (1100205), (1100206), (1100207), (1100208), (1100209), (1100210)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_DATE_REGULARISATION) VALUES
    (1100201, 1100201, ''SDBE'', ''1'', ''1'', NULL),   -- « A faire 1ere relance » : a relancer
    (1100202, 1100202, ''SDBE'', ''1'', ''2'', NULL),   -- relance 1 envoyee, delai en cours
    (1100203, 1100203, ''SDBE'', ''1'', ''0'', NULL),   -- ouverte, aucun courrier encore
    (1100204, 1100204, ''SDBE'', ''9'', ''2'', DATEADD(DAY, -2, @aujourdhui)),  -- close regularisee
    (1100205, 1100205, ''SDBE'', ''8'', ''2'', NULL),   -- close non regularisee
    (1100206, 1100206, ''SDBE'', ''1'', ''2'', NULL),   -- delai qui expire AUJOURD''HUI : pas encore sans effet
    (1100207, 1100207, ''SDBE'', ''1'', ''2'', NULL),   -- delai expire mais dossier RADIE : reste une relance
    (1100208, 1100208, ''SDBE'', ''1'', ''4'', NULL),   -- relance IDBE SANS instance : reste une relance
    (1100209, 1100209, ''SASSOSCI'', ''1'', ''1'', NULL), -- autre surveillance : ecartee (jumeau de 201)
    -- etat relance 2 (« 1ere relance faite ») sans ligne RELANCE_RCS : relancee (l''etat fait foi) ; sans relance n° 1
    -- datee, elle n''est pas « sans effet » -- jumeau 203 (etat 0 : en cours)
    (1100210, 1100210, ''SDBE'', ''1'', ''2'', NULL);
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE) VALUES
    (1100222, 1100202, 1, DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, 20, @aujourdhui), ''S'', NULL, 0),
    (1100224, 1100204, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 1),
    (1100225, 1100205, 1, DATEADD(DAY, -300, @aujourdhui), DATEADD(DAY, -270, @aujourdhui), ''S'', NULL, 0),
    (1100226, 1100206, 1, DATEADD(DAY, -30, @aujourdhui), @aujourdhui, ''S'', NULL, 0),
    (1100227, 1100207, 1, DATEADD(DAY, -31, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), ''S'', NULL, 0),
    (1100228, 1100208, 1, DATEADD(DAY, -60, @aujourdhui), DATEADD(DAY, -30, @aujourdhui), ''S'', NULL, 0),
    (1100238, 1100208, 2, DATEADD(DAY, -5, @aujourdhui), NULL, ''IDBE'', NULL, 0);',
    N'@aujourdhui datetime', @aujourdhui;
