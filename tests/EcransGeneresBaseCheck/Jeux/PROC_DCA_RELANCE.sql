-- Jeu de LecteurRelancesComptesAnnuels (« Relances comptes annuels », PROC_DCA_RELANCE), lot 1 du plan « brancher sur
-- la base les procedures encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11004xx (lot 1 : 1100000 a
-- 1199999). Ce qui est retenu ou ecarte, et pourquoi : PROC_DCA_RELANCE.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), seulement les
-- colonnes lues. Le banc les cree deja en partie : les colonnes qui leur manquent sont ajoutees ; RLCRC_TYPE_RELANCE et
-- RLCRC_FACTURE sont NOT NULL dans RIG_DEV, ajoutees NULL ici parce que la table a deja des lignes (celles des
-- radiations d'office du banc). Les lignes passent par sp_executesql : une colonne ajoutee dans ce meme lot
-- d'instructions n'existe pas encore quand SQL Server le compile.
-- Les trois demandes DCA de ce jeu (1100456 a 1100458) entrent aussi a l'ecran « Comptes annuels » (PROC_DCA) : un refus
-- de depot EST une demande DCA ; PROC_DCA n'a pas de .attendu, le banc ne controle ses lignes qu'avant les jeux.
-- fix-ok: 1100414 ajoute apres mesure sur la copie de dev (route PROC_DCA_RELANCE, 25/11/2026) : des diligences closes
-- sans ligne RELANCE_RCS (table tronquee a 200 lignes) sortaient « aucune relance envoyee » ; l'etat relance fait foi.
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
IF OBJECT_ID(N'v2.EXERCICE') IS NULL
    CREATE TABLE v2.EXERCICE (EXRCC_ID_EXRCC int NOT NULL PRIMARY KEY, EXRCC_DATE_CLOTURE datetime NOT NULL);
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DLGRC') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DLGRC int NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DMND_RECLA') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DMND_RECLA int NULL;
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant) et l'action de chaque diligence.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1100401, '511000401', '', N'RELANCE ALPHA SARL', 'C', 0), (1100402, '511000402', '', N'RELANCE BETA SAS', 'C', 0),
    (1100403, '511000403', '', N'RELANCE GAMMA SARL', 'C', 0), (1100404, '511000404', '', N'RELANCE DELTA SAS', 'C', 0),
    (1100405, '511000405', '', N'RELANCE EPSILON SARL', 'C', 0), (1100406, '511000406', '', N'RELANCE ZETA SAS', 'C', 0),
    (1100407, '511000407', '', N'RELANCE ETA SARL', 'C', 0), (1100408, '511000408', '', N'RELANCE THETA SAS', 'C', 0),
    (1100409, '511000409', '', N'RELANCE IOTA SARL', 'C', 0), (1100410, NULL, '', N'RELANCE KAPPA SAS', 'C', 0),
    (1100411, '511000411', '', N'RELANCE LAMBDA SARL', 'C', 0), (1100412, '511000412', '', N'RELANCE MU SAS', 'C', 0),
    (1100413, '511000413', '', N'RELANCE NU SARL', 'C', 0), (1100414, '511000414', '', N'RELANCE XI SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), v.Siren, '20180101', 'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1100401, '511000401'), (1100402, '511000402'), (1100403, '511000403'), (1100404, '511000404'),
             (1100405, '511000405'), (1100406, '511000406'), (1100407, '511000407'), (1100408, '511000408'),
             (1100409, '511000409'), (1100410, NULL), (1100411, '511000411'), (1100412, '511000412'),
             (1100413, '511000413'), (1100414, '511000414')) AS v (Id, Siren);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'SDCA', 'C', 'V', 0
FROM (VALUES (1100401), (1100402), (1100403), (1100404), (1100405), (1100406), (1100407), (1100408), (1100409),
             (1100410), (1100411), (1100412), (1100413), (1100414)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
-- Une diligence par dossier (meme identifiant que le dossier et son action).
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_DATE_REGULARISATION) VALUES
    (1100401, 1100401, ''SDCA'', ''1'', ''1'', NULL),   -- « A faire 1ere relance » : a relancer
    (1100402, 1100402, ''SDCA'', ''1'', ''2'', NULL),   -- relance 1 envoyee, delai en cours
    (1100403, 1100403, ''SDCA'', ''1'', ''2'', NULL),   -- relance 1 envoyee et facturee, delai expire : reste une relance
    (1100404, 1100404, ''SDCA'', ''1'', ''4'', NULL),   -- mise en demeure (relance 2, SMD), deux exercices
    (1100405, 1100405, ''SDCA'', ''1'', ''2'', NULL),   -- reclamation sur l''exercice relance
    (1100406, 1100406, ''SDCA'', ''1'', ''2'', NULL),   -- refus de depot APRES la relance
    (1100407, 1100407, ''SDCA'', ''1'', ''2'', NULL),   -- refus de depot AVANT la relance : pas un refus (jumeau de 406)
    (1100408, 1100408, ''SDCA'', ''9'', ''2'', DATEADD(DAY, -3, @aujourdhui)),  -- close regularisee : depot tardif
    (1100409, 1100409, ''SDCA'', ''8'', ''2'', NULL),   -- close non regularisee
    (1100410, 1100410, ''SDCA'', ''1'', ''0'', NULL),   -- ouverte, aucun courrier encore (sans SIREN : le numero de gestion)
    (1100411, 1100411, ''SDCA'', ''1'', ''4'', NULL),   -- passee a l''injonction (IDCA avec instance) : ecartee
    (1100412, 1100412, ''SDCA'', ''1'', ''4'', NULL),   -- relance IDCA SANS instance : reste une relance (jumeau de 411)
    (1100413, 1100413, ''SASSOSCI'', ''1'', ''1'', NULL), -- autre surveillance : ecartee (jumeau de 401)
    -- close regularisee, etat relance 6 (« 3eme phase faite ») mais AUCUNE ligne RELANCE_RCS : la relance est envoyee
    -- (l''etat de la diligence fait foi) -- jumeau 410 (etat 0, sans relance : en cours)
    (1100414, 1100414, ''SDCA'', ''9'', ''6'', DATEADD(DAY, -1, @aujourdhui));

INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE) VALUES
    (1100421, 1100402, 1, DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, 20, @aujourdhui), ''S'', NULL, 0),
    (1100422, 1100403, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 1),
    (1100423, 1100404, 1, DATEADD(DAY, -60, @aujourdhui), DATEADD(DAY, -30, @aujourdhui), ''S'', NULL, 0),
    (1100424, 1100404, 2, DATEADD(DAY, -20, @aujourdhui), DATEADD(DAY, -5, @aujourdhui), ''SMD'', NULL, 0),
    (1100425, 1100405, 1, DATEADD(DAY, -30, @aujourdhui), @aujourdhui, ''S'', NULL, 0),
    (1100426, 1100406, 1, DATEADD(DAY, -30, @aujourdhui), @aujourdhui, ''S'', NULL, 0),
    (1100427, 1100407, 1, DATEADD(DAY, -30, @aujourdhui), @aujourdhui, ''S'', NULL, 0),
    (1100428, 1100408, 1, DATEADD(DAY, -50, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), ''S'', NULL, 1),
    (1100429, 1100409, 1, DATEADD(DAY, -400, @aujourdhui), NULL, ''S'', NULL, 0),
    (1100430, 1100411, 1, DATEADD(DAY, -70, @aujourdhui), DATEADD(DAY, -40, @aujourdhui), ''S'', NULL, 0),
    (1100431, 1100411, 2, DATEADD(DAY, -15, @aujourdhui), NULL, ''IDCA'', 1100499, 0),
    (1100432, 1100412, 1, DATEADD(DAY, -70, @aujourdhui), DATEADD(DAY, -40, @aujourdhui), ''S'', NULL, 0),
    (1100433, 1100412, 2, DATEADD(DAY, -15, @aujourdhui), NULL, ''IDCA'', NULL, 0);

-- Les exercices relances (EXRCC_ID_DLGRC) ; 1100445 porte la demande de reclamation.
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DLGRC, EXRCC_ID_DMND_RECLA) VALUES
    (1100441, ''20241231'', 1100401, NULL), (1100442, ''20241231'', 1100402, NULL), (1100443, ''20241231'', 1100403, NULL),
    (1100444, ''20231231'', 1100404, NULL), (1100454, ''20241231'', 1100404, NULL), (1100445, ''20241231'', 1100405, 1100455),
    (1100446, ''20241231'', 1100406, NULL), (1100447, ''20241231'', 1100407, NULL), (1100448, ''20241231'', 1100408, NULL),
    (1100449, ''20221231'', 1100409, NULL), (1100450, ''20241231'', 1100410, NULL), (1100452, ''20241231'', 1100412, NULL),
    (1100453, ''20231231'', 1100414, NULL);

-- Les refus de depot (DCA, etat O) : 1100456 apres la relance de 406, 1100457 avant celle de 407. Et 1100458, un depot
-- recu apres la relance IDCA SANS instance de 412 : il n''entre PAS a l''ecran « Injonctions comptes annuels »
-- (jumeau des depots de PROC_DCA_INJ.sql, dont l''injonction porte une instance) ; un depot qualifie (Q) ne change pas
-- la relance de 412.
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION,
    DMND_DATE_VALIDATION) VALUES
    (1100456, 1100406, ''2601100456'', ''O'', ''DCA'', DATEADD(DAY, -5, @aujourdhui), DATEADD(DAY, -5, @aujourdhui)),
    (1100457, 1100407, ''2601100457'', ''O'', ''DCA'', DATEADD(DAY, -90, @aujourdhui), DATEADD(DAY, -90, @aujourdhui)),
    (1100458, 1100412, ''2601100458'', ''Q'', ''DCA'', DATEADD(DAY, -1, @aujourdhui), NULL);',
    N'@aujourdhui datetime', @aujourdhui;
