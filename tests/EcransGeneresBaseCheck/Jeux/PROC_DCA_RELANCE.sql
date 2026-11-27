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

-- ---------------------------------------------------------------- LA RELANCE DUE ET SON GESTE (plan « RIG v3 100 %
-- fonctionnel », lot 8, 26/11/2026 ; identifiants 18004xx, lot 8 : 1800000 a 1899999). La regle « A relancer » de
-- LecteurRelancesComptesAnnuels (requetes SDCA / RSURV / 2 et 3 de RIG_DEV) lit les colonnes d'exercice ci-dessous ; le
-- geste (EcrivainRelancesComptesAnnuels, PROC_DCA_RELANCE.gestes) ecrit aussi l'etat EDI de la diligence, l'auteur et
-- l'intervenant de la relance, la demande RSURV et son courrier. Types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de
-- SQL-DEV\DEV, lu le 26/11/2026), ajoutes NULL ici parce que les tables ont deja des lignes d'autres jeux.
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_DEPOSE') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_DEPOSE bit NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_NON_APPROBATION_COMPTES') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_NON_APPROBATION_COMPTES bit NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_LIMITE_DEPOT') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_LIMITE_DEPOT datetime NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ETAT_DCA') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ETAT_DCA varchar(2) NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_DATE_RELANCE') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_DATE_RELANCE datetime NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_ETAT_EDI') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_ETAT_EDI varchar(2) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_AUTEUR') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_AUTEUR varchar(1) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_INTERVENANT') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_INTERVENANT varchar(1) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_LES_NUMEROS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LES_NUMEROS varchar(128) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_PARAMETRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_PARAMETRE varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_MERE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_MERE int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
-- Les reglages de relance (NATURE_DILIGENCE_PARAM) et les parametres d'edition (PARAMETRE_EDITION, cle PRMED_CODE) : les
-- colonnes lues, aux types de RIG_DEV.
IF OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM') IS NULL
    CREATE TABLE v2.NATURE_DILIGENCE_PARAM (NADIP_ID_NADIP int NOT NULL PRIMARY KEY, NADIP_NATURE_DILIGENCE varchar(10) NOT NULL,
        NADIP_CODE_PROCESSUS varchar(64) NULL, NADIP_NUM_ORDRE int NULL, NADIP_CODE_PARAM varchar(128) NOT NULL,
        NADIP_VALEUR_PARAM varchar(max) NOT NULL);
-- fix-ok: mesure le 26/11/2026 (banc reuni avec le lot 6 de main) -- ce jeu passe AVANT PROC_REGJUD.sql (ordre des noms) et creait
-- PARAMETRE_EDITION sans les colonnes que REGJUD insere (PRMED_LISTE...) : « Invalid column name ». Il la cree maintenant avec
-- les colonnes des deux jeux, aux types de RIG_DEV ; celles qu'un jeu ne remplit pas ont le defaut de RIG_DEV ou sont NULL.
IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
    CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
        PRMED_RECTO_VERSO bit NOT NULL DEFAULT 0, PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL DEFAULT 0, PRMED_CODE_CFECP varchar(50) NULL,
        PRMED_LISTE varchar(4000) NOT NULL DEFAULT '', PRMED_CODE_ENTITE_SOURCE varchar(16) NULL, PRMED_CODE_PROCESSUS varchar(40) NULL,
        PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NOT NULL DEFAULT 1,
        PRMED_READ_ONLY bit NOT NULL DEFAULT 0, PRMED_NUM_GROUPE_IMPRESSION varchar(30) NULL, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC varchar(50) NULL);
IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_RECTO_VERSO') IS NULL
    ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_RECTO_VERSO bit NOT NULL CONSTRAINT DF_BancRelance_Recto DEFAULT 0 WITH VALUES;
IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_AFFICHER_AVANT_IMPRESSION') IS NULL
    ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL CONSTRAINT DF_BancRelance_Afficher DEFAULT 0 WITH VALUES;

-- Les dossiers des jumeaux de la regle « A relancer » (lot 8) : 1800402 est radie.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1800401, '518004401', '', N'RELANCE OMICRON SAS', 'C', 0), (1800402, '518004402', '', N'RELANCE PI SARL', 'C', 0),
    (1800403, '518004403', '', N'RELANCE RHO SAS', 'C', 0), (1800404, '518004404', '', N'RELANCE SIGMA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation)
SELECT v.Id, v.Id, 'B1', '2026B18' + RIGHT(CAST(v.Id AS varchar(7)), 3), v.Siren, '20180101', 'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, v.Radiation
FROM (VALUES (1800401, '518004401', CAST(NULL AS datetime)), (1800402, '518004402', CAST('20250601' AS datetime)),
             (1800403, '518004403', CAST(NULL AS datetime)), (1800404, '518004404', CAST(NULL AS datetime))) AS v (Id, Siren, Radiation);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'SDCA', 'C', 'V', 0 FROM (VALUES (1800401), (1800402), (1800403), (1800404)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
-- Les exercices des diligences existantes : 403 et 404 sont DUS (relance 1, resp. 2, echue ; exercice non depose, limite
-- de depot passee, etat DCA de la relance faite). 402 (limite de la relance 1 a {J+20}) et 407 (limite le jour meme : la
-- requete dit « < aujourd''hui ») ont le meme exercice mais ne sont pas dus -- jumeaux de 403. 454, le second exercice de
-- 404, est depose : 404 reste due par 444, et le geste ne relance que 444.
UPDATE v2.EXERCICE SET EXRCC_DEPOSE = 0, EXRCC_NON_APPROBATION_COMPTES = 0, EXRCC_LIMITE_DEPOT = DATEADD(DAY, -200, @aujourdhui),
    EXRCC_ETAT_DCA = ''22'' WHERE EXRCC_ID_EXRCC IN (1100442, 1100443, 1100447);
UPDATE v2.EXERCICE SET EXRCC_DEPOSE = 0, EXRCC_NON_APPROBATION_COMPTES = 0, EXRCC_LIMITE_DEPOT = DATEADD(DAY, -500, @aujourdhui),
    EXRCC_ETAT_DCA = ''44'' WHERE EXRCC_ID_EXRCC = 1100444;
UPDATE v2.EXERCICE SET EXRCC_DEPOSE = 1, EXRCC_NON_APPROBATION_COMPTES = 0, EXRCC_LIMITE_DEPOT = DATEADD(DAY, -200, @aujourdhui),
    EXRCC_ETAT_DCA = ''44'' WHERE EXRCC_ID_EXRCC = 1100454;

-- Les jumeaux de 403 (relance 1 echue le {J-10}, exercice a relancer) : 1800401 a une demande DCA en attente (W), 1800402
-- est radie, 1800403 a son exercice depose -- aucun n''est du. 1800404 est du (etat relance 2, relance 1 echue) mais porte
-- deja une relance 2 : le geste le refuse au message de RIG v2.
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE) VALUES
    (1800401, 1800401, ''SDCA'', ''1'', ''2''), (1800402, 1800402, ''SDCA'', ''1'', ''2''),
    (1800403, 1800403, ''SDCA'', ''1'', ''2''), (1800404, 1800404, ''SDCA'', ''1'', ''2'');
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE, RLCRC_AUTEUR, RLCRC_INTERVENANT) VALUES
    (1800421, 1800401, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 0, ''3'', ''1''),
    (1800422, 1800402, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 0, ''3'', ''1''),
    (1800423, 1800403, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 0, ''3'', ''1''),
    (1800424, 1800404, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 0, ''3'', ''1''),
    (1800425, 1800404, 2, DATEADD(DAY, -1, @aujourdhui), DATEADD(DAY, 14, @aujourdhui), ''S'', NULL, 0, ''4'', ''1'');
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DLGRC, EXRCC_ID_DMND_RECLA, EXRCC_DEPOSE,
    EXRCC_NON_APPROBATION_COMPTES, EXRCC_LIMITE_DEPOT, EXRCC_ETAT_DCA) VALUES
    (1800441, ''20241231'', 1800401, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''22''),
    (1800442, ''20241231'', 1800402, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''22''),
    (1800443, ''20241231'', 1800403, NULL, 1, 0, DATEADD(DAY, -200, @aujourdhui), ''22''),
    (1800444, ''20241231'', 1800404, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''22'');
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION) VALUES
    (1800456, 1800401, ''2601800456'', ''W'', ''DCA'', DATEADD(DAY, -2, @aujourdhui));

-- Les reglages de RIG_DEV pour les relances 2 et 3 des SDCA (NATURE_DILIGENCE_PARAM, ids 5, 7, 13, 9, 10, 14, lus le
-- 26/11/2026) et leurs parametres d''edition (PARAMETRE_EDITION LOT2_MDDCA, LOT2_APDCA, meme jour).
INSERT INTO v2.NATURE_DILIGENCE_PARAM (NADIP_ID_NADIP, NADIP_NATURE_DILIGENCE, NADIP_CODE_PROCESSUS, NADIP_NUM_ORDRE,
    NADIP_CODE_PARAM, NADIP_VALEUR_PARAM) VALUES
    (1800451, ''SDCA'', ''RSURV'', 2, ''CodeParamEdition'', ''LOT2_MDDCA''), (1800452, ''SDCA'', ''RSURV'', 2, ''DelaiRelance'', ''Jour=15''),
    (1800453, ''SDCA'', ''RSURV'', 2, ''FileImpression'', ''AMIRCSRBE''), (1800454, ''SDCA'', ''RSURV'', 3, ''CodeParamEdition'', ''LOT2_APDCA''),
    (1800455, ''SDCA'', ''RSURV'', 3, ''DelaiRelance'', ''Jour=15''), (1800457, ''SDCA'', ''RSURV'', 3, ''FileImpression'', ''AMIRCSRBE'');
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_RECTO_VERSO, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_CFECP) VALUES
    (''LOT2_MDDCA'', ''Relance Greffe MD DCA'', 1, 0, NULL), (''LOT2_APDCA'', ''Relance Greffe AP DCA'', 1, 0, NULL);',
    N'@aujourdhui datetime', @aujourdhui;
-- ---------------------------------------------------------------- LA RELANCE 1 (lot 8, 26/11/2026 ; identifiants 18005xx) :
-- les exercices JAMAIS relances, recherche SDCA / RSURV / 1 de RIG_DEV et enveloppe du moteur aux criteres par defaut
-- (LecteurRelancesComptesAnnuels.ExerciceRelance1Sql). Colonnes et types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV,
-- lu le 26/11/2026), seulement celles que la requete lit ; NULL quand la table a deja des lignes d'autres jeux. EXRCC_ID_DSRCS :
-- a l'identique de PROC_MDIL.sql (qui passe apres ce jeu). Les tables INSCRIT_PM, FORME_JURIDIQUE_UTI, CODE_MENTION et
-- DILIGENCE_RCS_EXCLUE sont deja creees par PROC_BE.sql et PROC_DBE_RELANCE.sql (avant ce jeu, ordre des noms).
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DSRCS') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DSRCS int NULL;
IF COL_LENGTH('v2.FORME_JURIDIQUE_UTI', 'R_FORMEJURID_DCA_OBLIGATOIRE') IS NULL
    ALTER TABLE v2.FORME_JURIDIQUE_UTI ADD R_FORMEJURID_DCA_OBLIGATOIRE varchar(1) NULL;
IF COL_LENGTH('v2.CODE_SOLUTION', 'R_CODESOLUTI_SURVEILLANCE_DCA') IS NULL ALTER TABLE v2.CODE_SOLUTION ADD R_CODESOLUTI_SURVEILLANCE_DCA bit NULL;
IF COL_LENGTH('v2.CODE_CLOTURE', 'R_CODECLOTUR_SURVEILLANCE_DCA') IS NULL ALTER TABLE v2.CODE_CLOTURE ADD R_CODECLOTUR_SURVEILLANCE_DCA bit NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS_EXCLUE', 'DLGEX_ID_EXRCC') IS NULL ALTER TABLE v2.DILIGENCE_RCS_EXCLUE ADD DLGEX_ID_EXRCC int NULL;
IF OBJECT_ID(N'v2.DIRIGEANT') IS NULL
    CREATE TABLE v2.DIRIGEANT (DRGNT_ID_DRGNT int NOT NULL PRIMARY KEY, DRGNT_ID_DSRCS int NULL, DRGNT_NUM_ORDRE int NOT NULL,
        DRGNT_SUPPRIME bit NOT NULL, DRGNT_FONCTION_DIRIGEANT varchar(5) NOT NULL);
IF OBJECT_ID(N'v2.DIRIGEANT_PP') IS NULL CREATE TABLE v2.DIRIGEANT_PP (DRGPP_ID_DRGPP int NOT NULL PRIMARY KEY, DRGPP_ID_DRGNT int NULL);
IF OBJECT_ID(N'v2.DIRIGEANT_PM') IS NULL
    CREATE TABLE v2.DIRIGEANT_PM (DRGPM_ID_DRGPM int NOT NULL PRIMARY KEY, DRGPM_ID_DRGNT int NULL, DRGPM_FORME_JURIDIQUE_UTI varchar(10) NULL);

-- Les dossiers. Retenus : 501 (B1, forme juridique a depot obligatoire « 1 »), 502 (D1, forme « 3 » dont le seul associe
-- ASNC est une personne morale de forme « 1 » ; deux exercices, celui de 2023 deja lie a une diligence close : RIG v2
-- l'avertit), 511 (deux exercices, celui de 2024 exclu de la nature SDCA : seul celui de 2023 reste). Ecartes, chacun avec
-- son jumeau retenu :
--   503 forme « 3 » avec un associe ASNC personne physique -- jumeau 502 ; 504 forme « 0 » (jamais) -- jumeau 501 ;
--   505 type C1 (TypeDossier « B,D ») -- jumeaux 501 (B1) et 502 (D1) ; 506 exercice depose -- jumeau 501 ;
--   507 limite de depot le jour meme (« < aujourd'hui ») -- jumeau 501 (limite passee) ; 508 etat DCA « 22 » (deja relance)
--   -- jumeau 501 (« 00 ») ; 509 demande DCA en attente (W) -- jumeau 501 ; 510 exercice porte par une diligence SDCA en cours
--   deja relancee (etat relance 2) -- jumeau 502 (diligence close) ; 512 societe dissoute -- jumeau 501.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '518005' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1800501, N'R1 ALPHA SAS'), (1800502, N'R1 BETA SNC'), (1800503, N'R1 GAMMA SNC'), (1800504, N'R1 DELTA SCI'),
             (1800505, N'R1 EPSILON SAS'), (1800506, N'R1 ZETA SAS'), (1800507, N'R1 ETA SAS'), (1800508, N'R1 THETA SAS'),
             (1800509, N'R1 IOTA SAS'), (1800510, N'R1 KAPPA SAS'), (1800511, N'R1 LAMBDA SAS'), (1800512, N'R1 MU SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, v.TypeRcs, '2026B18' + RIGHT(CAST(v.Id AS varchar(7)), 3), '518005' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1800501, 'B1'), (1800502, 'D1'), (1800503, 'B1'), (1800504, 'B1'), (1800505, 'C1'), (1800506, 'B1'), (1800507, 'B1'),
             (1800508, 'B1'), (1800509, 'B1'), (1800510, 'B1'), (1800511, 'B1'), (1800512, 'B1')) AS v (Id, TypeRcs);
SET IDENTITY_INSERT rec.DossierRcs OFF;

EXEC sp_executesql N'
-- Formes juridiques (R_FORMEJURID_DCA_OBLIGATOIRE : 0 jamais, 1 toujours, 3 selon les associes) ; TYPE_BEN NULL : ces dossiers
-- n''entrent pas dans la relance des beneficiaires (qui exige TYPE_BEN <> 0).
INSERT INTO v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR, R_FORMEJURID_TYPE_BEN, R_FORMEJURID_DCA_OBLIGATOIRE) VALUES
    (''D18A'', NULL, ''1''), (''D18S'', NULL, ''3''), (''D18Z'', NULL, ''0'');
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI, INSPM_DATE_DISSOLUTION)
SELECT v.Id, v.Fj, CASE WHEN v.Id = 1800512 THEN DATEADD(MONTH, -3, @aujourdhui) END
FROM (VALUES (1800501, ''D18A''), (1800502, ''D18S''), (1800503, ''D18S''), (1800504, ''D18Z''), (1800505, ''D18A''), (1800506, ''D18A''),
             (1800507, ''D18A''), (1800508, ''D18A''), (1800509, ''D18A''), (1800510, ''D18A''), (1800511, ''D18A''), (1800512, ''D18A'')) AS v (Id, Fj);
-- Les associes : 502 a un ASNC personne morale de forme « 1 » (relancable), 503 un ASNC personne physique (pas de relance).
INSERT INTO v2.DIRIGEANT (DRGNT_ID_DRGNT, DRGNT_ID_DSRCS, DRGNT_NUM_ORDRE, DRGNT_SUPPRIME, DRGNT_FONCTION_DIRIGEANT) VALUES
    (1800582, 1800502, 1, 0, ''ASNC''), (1800583, 1800503, 1, 0, ''ASNC'');
INSERT INTO v2.DIRIGEANT_PM (DRGPM_ID_DRGPM, DRGPM_ID_DRGNT, DRGPM_FORME_JURIDIQUE_UTI) VALUES (1800582, 1800582, ''D18A'');
INSERT INTO v2.DIRIGEANT_PP (DRGPP_ID_DRGPP, DRGPP_ID_DRGNT) VALUES (1800583, 1800583);
-- La diligence close de l''exercice 2023 de 502 (etat 8) et la diligence en cours deja relancee de 510 (etat relance 2), sans action :
-- elles ne sont pas des lignes de cet ecran (il lit le dossier par l''action), l''ecran « Diligences » les lit par l''exercice.
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE) VALUES
    (1800562, NULL, ''SDCA'', ''8'', ''2''), (1800560, NULL, ''SDCA'', ''1'', ''2'');
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DSRCS, EXRCC_ID_DLGRC, EXRCC_ID_DMND_RECLA, EXRCC_DEPOSE,
    EXRCC_NON_APPROBATION_COMPTES, EXRCC_LIMITE_DEPOT, EXRCC_ETAT_DCA) VALUES
    (1800541, ''20241231'', 1800501, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800542, ''20231231'', 1800502, 1800562, NULL, 0, 0, DATEADD(DAY, -560, @aujourdhui), ''00''),
    (1800552, ''20241231'', 1800502, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800543, ''20241231'', 1800503, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800544, ''20241231'', 1800504, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800545, ''20241231'', 1800505, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800546, ''20241231'', 1800506, NULL, NULL, 1, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800547, ''20241231'', 1800507, NULL, NULL, 0, 0, @aujourdhui, ''00''),
    (1800548, ''20241231'', 1800508, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''22''),
    (1800549, ''20241231'', 1800509, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800550, ''20241231'', 1800510, 1800560, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800551, ''20241231'', 1800511, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00''),
    (1800553, ''20231231'', 1800511, NULL, NULL, 0, 0, DATEADD(DAY, -560, @aujourdhui), ''00''),
    (1800554, ''20241231'', 1800512, NULL, NULL, 0, 0, DATEADD(DAY, -200, @aujourdhui), ''00'');
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION) VALUES
    (1800559, 1800509, ''2601800559'', ''W'', ''DCA'', DATEADD(DAY, -2, @aujourdhui));
INSERT INTO v2.DILIGENCE_RCS_EXCLUE (DLGEX_ID_DLGEX, DLGEX_ID_DSRCS, DLGEX_NATURE_DILIGENCE, DLGEX_ID_EXRCC) VALUES
    (1800561, 1800511, ''SDCA'', 1800551);
-- Les reglages de RIG_DEV de la relance 1 SDCA (NATURE_DILIGENCE_PARAM ids 2, 3, 12) et son parametre d''edition (PARAMETRE_EDITION
-- LOT2_RGDCA « Relance Greffe RG DCA », recto verso, FUSION_RIG), lus le 26/11/2026.
INSERT INTO v2.NATURE_DILIGENCE_PARAM (NADIP_ID_NADIP, NADIP_NATURE_DILIGENCE, NADIP_CODE_PROCESSUS, NADIP_NUM_ORDRE,
    NADIP_CODE_PARAM, NADIP_VALEUR_PARAM) VALUES
    (1800571, ''SDCA'', ''RSURV'', 1, ''CodeParamEdition'', ''LOT2_RGDCA''), (1800572, ''SDCA'', ''RSURV'', 1, ''DelaiRelance'', ''Mois=1''),
    (1800573, ''SDCA'', ''RSURV'', 1, ''FileImpression'', ''AMIRCSRBE'');
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_RECTO_VERSO, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_CFECP,
    PRMED_TYPE_GENERATION) VALUES (''LOT2_RGDCA'', ''Relance Greffe RG DCA'', 1, 0, NULL, ''FUSION_RIG'');',
    N'@aujourdhui datetime', @aujourdhui;

-- ============================================================ LES DOSSIERS A ENJOINDRE (lot 8, 26/11/2026)
-- « Injonctions comptes annuels » (LecteurInjonctionsComptesAnnuels, Rang 0) lit la MEME recherche SDCA que la relance 1
-- (ExerciceRechercheSql : l'injonction ISURV de SDCA n'a pas de requete a elle, NatureDiligence.parametre.cs:620-626) : ses dossiers
-- sont poses ici, avec la structure de cette recherche (formes juridiques, INSCRIT_PM, exercices...). Le cabinet, les codes et les
-- reglages ISURV sont dans PROC_DBE_INJ.sql. Ces dossiers sont AUSSI « A relancer » (relance 2 due) a l'ecran des relances, comme
-- dans RIG v2 : les deux moteurs les proposent ; l'injonction les sort des relances (filtre IDCA). Leurs lignes :
-- PROC_DCA_INJ.attendu et PROC_DCA_RELANCE.attendu ; leurs diligences : PROC_MDIL.attendu (toutes les diligences).
--   601 a enjoindre : exercice 2023 non depose, etat DCA « 22 », diligence SDCA en cours, relance 1 faite (etat relance 2) echue
--       hier, gerant representant legal ;
--   602 a enjoindre, DEUX exercices (2022 et 2023) sur la meme diligence : deux injonctions (une par exercice, PROC_ISURV.cs:224-233) ;
--   603 relance 1 echeant AUJOURD'HUI (« < aujourd'hui ») -- ecarte, jumeau 601 ;
--   604 a enjoindre, mais son seul dirigeant est commissaire aux comptes (pas representant legal) : le geste est refuse -- jumeau 601.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '518006' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1800601, N'INJ ALPHA SAS'), (1800602, N'INJ BETA SAS'), (1800603, N'INJ GAMMA SAS'), (1800604, N'INJ DELTA SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2026B18' + RIGHT(CAST(v.Id AS varchar(7)), 3), '518006' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1800601), (1800602), (1800603), (1800604)) AS v (Id);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'SDCA', '1', '2', 0 FROM (VALUES (1800601), (1800602), (1800603), (1800604)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI, INSPM_DATE_DISSOLUTION)
SELECT v.Id, ''D18A'', NULL FROM (VALUES (1800601), (1800602), (1800603), (1800604)) AS v (Id);
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE)
SELECT v.Id + 60, v.Id, ''SDCA'', ''1'', ''2'' FROM (VALUES (1800601), (1800602), (1800603), (1800604)) AS v (Id);
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE, RLCRC_TYPE_RELANCE,
    RLCRC_ID_INSTN, RLCRC_FACTURE, RLCRC_AUTEUR, RLCRC_INTERVENANT)
SELECT v.Id + 80, v.Id + 60, 1, DATEADD(DAY, -31, @aujourdhui), CASE WHEN v.Id = 1800603 THEN @aujourdhui ELSE DATEADD(DAY, -1, @aujourdhui) END,
       ''S'', NULL, 0, ''3'', ''1''
FROM (VALUES (1800601), (1800602), (1800603), (1800604)) AS v (Id);
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DSRCS, EXRCC_ID_DLGRC, EXRCC_ID_DMND_RECLA, EXRCC_DEPOSE,
    EXRCC_NON_APPROBATION_COMPTES, EXRCC_LIMITE_DEPOT, EXRCC_ETAT_DCA) VALUES
    (1800641, ''20231231'', 1800601, 1800661, NULL, 0, 0, DATEADD(DAY, -500, @aujourdhui), ''22''),
    (1800642, ''20221231'', 1800602, 1800662, NULL, 0, 0, DATEADD(DAY, -860, @aujourdhui), ''22''),
    (1800652, ''20231231'', 1800602, 1800662, NULL, 0, 0, DATEADD(DAY, -500, @aujourdhui), ''22''),
    (1800643, ''20231231'', 1800603, 1800663, NULL, 0, 0, DATEADD(DAY, -500, @aujourdhui), ''22''),
    (1800644, ''20231231'', 1800604, 1800664, NULL, 0, 0, DATEADD(DAY, -500, @aujourdhui), ''22'');
INSERT INTO v2.DIRIGEANT (DRGNT_ID_DRGNT, DRGNT_ID_DSRCS, DRGNT_NUM_ORDRE, DRGNT_SUPPRIME, DRGNT_FONCTION_DIRIGEANT) VALUES
    (1800681, 1800601, 1, 0, ''GER18''), (1800682, 1800602, 1, 0, ''GER18''), (1800683, 1800603, 1, 0, ''GER18''),
    (1800684, 1800604, 1, 0, ''CAC18'');
INSERT INTO v2.DIRIGEANT_PP (DRGPP_ID_DRGPP, DRGPP_ID_DRGNT, DRGPP_CIVILITE, DRGPP_PATRONYME, DRGPP_PRENOM_USAGE) VALUES
    (1800681, 1800681, ''MR'', ''ALPHAGERANT'', ''Leo''), (1800682, 1800682, ''MME'', ''BETAGERANTE'', ''Ana''),
    (1800683, 1800683, ''MR'', ''GAMMAGERANT'', ''Max''), (1800684, 1800684, ''MR'', ''DELTAAUDIT'', ''Tom'');',
    N'@aujourdhui datetime', @aujourdhui;
