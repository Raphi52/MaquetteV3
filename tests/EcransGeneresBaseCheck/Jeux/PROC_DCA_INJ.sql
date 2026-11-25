-- Jeu de LecteurInjonctionsComptesAnnuels (« Injonctions comptes annuels », PROC_DCA_INJ), lot 1 du plan « brancher
-- sur la base les procedures encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11003xx. Ce qui est retenu
-- ou ecarte, et pourquoi : PROC_DCA_INJ.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), seulement les
-- colonnes lues ; colonnes manquantes ajoutees (NULL : la table a deja des lignes), lignes par sp_executesql (une colonne
-- ajoutee dans ce lot d'instructions n'existe pas encore quand SQL Server le compile).
-- Les demandes DCA / DCADEMAT de ce jeu entrent aussi a l'ecran « Comptes annuels » (PROC_DCA) : un depot apres
-- injonction EST un depot ; PROC_DCA n'a pas de .attendu, le banc ne controle ses lignes qu'avant les jeux. Le jumeau
-- « relance IDCA SANS instance » est dans PROC_DCA_RELANCE.sql (dossier 1100412, demande 1100458) : sa diligence est
-- une relance, elle appartient a cet ecran-la.
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
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant) et l'action de chaque diligence.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1100301, N'INJONCTION ALPHA SARL'), (1100302, N'INJONCTION BETA SAS'), (1100303, N'INJONCTION GAMMA SARL'),
             (1100304, N'INJONCTION DELTA SAS'), (1100305, N'INJONCTION EPSILON SARL'), (1100306, N'INJONCTION ZETA SAS'),
             (1100307, N'INJONCTION ETA SARL'), (1100310, N'INJONCTION KAPPA SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1100301), (1100302), (1100303), (1100304), (1100305), (1100306), (1100307), (1100310)) AS v (Id);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'SDCA', 'C', 'V', 0
FROM (VALUES (1100301), (1100302), (1100303), (1100304), (1100305), (1100306), (1100307), (1100310)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
-- Chaque dossier est sous injonction depuis J-30 : diligence SDCA, relance 1, puis relance IDCA portant une instance.
-- 1100310 : la meme injonction, mais sur une diligence SASSOSCI (pas une injonction DCA).
-- 1100304 : le depot 1100314 valide (J-8) a clos la diligence, regularisee a la meme date.
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_DATE_REGULARISATION)
SELECT v.Id, v.Id, v.Nature, CASE WHEN v.Id = 1100304 THEN ''9'' ELSE ''1'' END, ''4'',
       CASE WHEN v.Id = 1100304 THEN DATEADD(DAY, -8, @aujourdhui) END
FROM (VALUES (1100301, ''SDCA''), (1100302, ''SDCA''), (1100303, ''SDCA''), (1100304, ''SDCA''), (1100305, ''SDCA''),
             (1100306, ''SDCA''), (1100307, ''SDCA''), (1100310, ''SASSOSCI'')) AS v (Id, Nature);
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE)
SELECT v.Id + 20, v.Id, 1, DATEADD(DAY, -90, @aujourdhui), DATEADD(DAY, -60, @aujourdhui), ''S'', NULL, 0
FROM (VALUES (1100301), (1100302), (1100303), (1100304), (1100305), (1100306), (1100307), (1100310)) AS v (Id)
UNION ALL
SELECT v.Id + 40, v.Id, 2, DATEADD(DAY, -30, @aujourdhui), NULL, ''IDCA'', 1100399, 0
FROM (VALUES (1100301), (1100302), (1100303), (1100304), (1100305), (1100306), (1100307), (1100310)) AS v (Id);

-- Les demandes de depot.
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION,
    DMND_DATE_VALIDATION) VALUES
    (1100311, 1100301, ''2601100311'', ''Q'', ''DCADEMAT'', DATEADD(DAY, -2, @aujourdhui), NULL),   -- demat qualifiee : a traiter
    (1100312, 1100302, ''2601100312'', ''A'', ''DCA'', DATEADD(DAY, -1, @aujourdhui), NULL),        -- papier arrive : a traiter
    (1100313, 1100303, ''2601100313'', ''I'', ''DCA'', DATEADD(DAY, -5, @aujourdhui), NULL),        -- interrompue : en cours
    (1100314, 1100304, ''2601100314'', ''V'', ''DCADEMAT'', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -8, @aujourdhui)),
    (1100315, 1100305, ''2601100315'', ''T'', ''DCA'', DATEADD(DAY, -12, @aujourdhui), DATEADD(DAY, -11, @aujourdhui)),
    (1100316, 1100306, ''2601100316'', ''N'', ''DCA'', DATEADD(DAY, -15, @aujourdhui), DATEADD(DAY, -14, @aujourdhui)),
    -- refus, cree LE JOUR de l''injonction : retenu (borne incluse) -- jumeau ecarte 1100318 (la veille)
    (1100317, 1100307, ''2601100317'', ''O'', ''DCADEMAT'', DATEADD(DAY, -30, @aujourdhui), NULL),
    -- depot la VEILLE de l''injonction : ecarte -- jumeau retenu 1100317
    (1100318, 1100301, ''2601100318'', ''Q'', ''DCA'', DATEADD(DAY, -31, @aujourdhui), NULL),
    -- supprime (X) apres l''injonction : ecarte -- jumeau retenu 1100312 (A)
    (1100319, 1100302, ''2601100319'', ''X'', ''DCA'', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- autre processus (DAS) apres l''injonction : ecarte -- jumeau retenu 1100311
    (1100320, 1100301, ''2601100320'', ''Q'', ''DAS'', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- dossier dont l''« injonction » est sur une diligence SASSOSCI : ecarte -- jumeau retenu 1100312
    (1100321, 1100310, ''2601100321'', ''Q'', ''DCA'', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- depot APRES la regularisation de l''injonction de 1100304 (exercice suivant) : ecarte -- jumeau retenu 1100314
    -- (cree avant, valide le jour de la regularisation)
    (1100322, 1100304, ''2601100322'', ''Q'', ''DCA'', DATEADD(DAY, -3, @aujourdhui), NULL);',
    N'@aujourdhui datetime', @aujourdhui;
