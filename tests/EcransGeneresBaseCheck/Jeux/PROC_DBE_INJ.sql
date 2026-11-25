-- Jeu de LecteurInjonctionsBeneficiaires (« Injonctions beneficiaires », PROC_DBE_INJ), lot 1 du plan « brancher sur
-- la base les procedures encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11001xx. Ce qui est retenu ou
-- ecarte, et pourquoi : PROC_DBE_INJ.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), seulement les
-- colonnes lues ; colonnes manquantes ajoutees (NULL : la table a deja des lignes), lignes par sp_executesql (une colonne
-- ajoutee dans ce lot d'instructions n'existe pas encore quand SQL Server le compile).
-- Les jumeaux qui restent des RELANCES (delai expirant aujourd'hui, dossier radie, relance IDBE sans instance) sont
-- dans PROC_DBE_RELANCE.sql (206, 207, 208) : un jeu ne pose pas de ligne dans l'ecran d'un autre.
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

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant), 106 radie, et l'action de chaque diligence.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1100101, N'ENJOINDRE ALPHA SARL'), (1100102, N'ENJOINDRE BETA SAS'), (1100103, N'ENJOINDRE GAMMA SARL'),
             (1100104, N'ENJOINDRE DELTA SAS'), (1100105, N'ENJOINDRE EPSILON SARL'), (1100106, N'ENJOINDRE ZETA SAS'),
             (1100107, N'ENJOINDRE ETA SARL')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, CASE WHEN v.Id = 1100106 THEN DATEADD(MONTH, -1, @aujourdhui) END
FROM (VALUES (1100101), (1100102), (1100103), (1100104), (1100105), (1100106), (1100107)) AS v (Id);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'RDBE', 'C', 'V', 0
FROM (VALUES (1100101), (1100102), (1100103), (1100104), (1100105), (1100106), (1100107)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_DATE_REGULARISATION) VALUES
    (1100101, 1100101, ''SDBE'', ''1'', ''2'', NULL),   -- relance 1 expiree hier : a enjoindre
    (1100102, 1100102, ''SDBE'', ''1'', ''2'', NULL),   -- relance 1 expiree depuis deux mois : a enjoindre
    (1100103, 1100103, ''SDBE'', ''1'', ''4'', NULL),   -- injonction emise (IDBE avec instance), en cours
    (1100104, 1100104, ''SDBE'', ''9'', ''4'', DATEADD(DAY, -3, @aujourdhui)),  -- injonction, close regularisee
    (1100105, 1100105, ''SDBE'', ''8'', ''4'', NULL),   -- injonction, close non regularisee
    (1100106, 1100106, ''SDBE'', ''1'', ''4'', NULL),   -- injonction sur un dossier RADIE : reste a suivre
    (1100107, 1100107, ''SASSOSCI'', ''1'', ''2'', NULL); -- autre surveillance, relance expiree : ecartee (jumeau de 101)
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE) VALUES
    (1100121, 1100101, 1, DATEADD(DAY, -31, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), ''S'', NULL, 0),
    (1100122, 1100102, 1, DATEADD(DAY, -90, @aujourdhui), DATEADD(DAY, -60, @aujourdhui), ''S'', NULL, 0),
    (1100123, 1100103, 1, DATEADD(DAY, -80, @aujourdhui), DATEADD(DAY, -50, @aujourdhui), ''S'', NULL, 0),
    (1100133, 1100103, 2, DATEADD(DAY, -20, @aujourdhui), NULL, ''IDBE'', 1100199, 0),
    (1100124, 1100104, 1, DATEADD(DAY, -160, @aujourdhui), DATEADD(DAY, -130, @aujourdhui), ''S'', NULL, 1),
    (1100134, 1100104, 2, DATEADD(DAY, -100, @aujourdhui), NULL, ''IDBE'', 1100198, 1),
    (1100125, 1100105, 1, DATEADD(DAY, -260, @aujourdhui), DATEADD(DAY, -230, @aujourdhui), ''S'', NULL, 0),
    (1100135, 1100105, 2, DATEADD(DAY, -200, @aujourdhui), NULL, ''IDBE'', 1100197, 0),
    (1100126, 1100106, 1, DATEADD(DAY, -70, @aujourdhui), DATEADD(DAY, -40, @aujourdhui), ''S'', NULL, 0),
    (1100136, 1100106, 2, DATEADD(DAY, -10, @aujourdhui), NULL, ''IDBE'', 1100196, 0),
    (1100127, 1100107, 1, DATEADD(DAY, -35, @aujourdhui), DATEADD(DAY, -5, @aujourdhui), ''S'', NULL, 0);',
    N'@aujourdhui datetime', @aujourdhui;
