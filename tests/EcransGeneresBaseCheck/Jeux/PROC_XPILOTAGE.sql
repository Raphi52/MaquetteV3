-- « Pilotage » (LecteurPilotage) : la table PILOTAGE_REQUETES de RIG v2 (colonnes et types de RIG_DEV, lu le 26/11/2026) et
-- des requetes de controle, chacune pour une regle de OPE_GENERER.cs. Les chiffres comptent des groupes d'impression d'un lot
-- « BANC9_PILOTAGE » absent de la table LOT : « Relancer un lot » ne les voit pas (son lecteur part des lots).
IF OBJECT_ID(N'v2.PILOTAGE_REQUETES') IS NULL
    CREATE TABLE v2.PILOTAGE_REQUETES (PILOT_ID_PILOT int NOT NULL PRIMARY KEY, PILOT_SQL varchar(max) NULL, PILOT_COLONNE int NULL,
        PILOT_LIGNE int NULL, PILOT_LIBELLE varchar(255) NULL, PILOT_TRAITEMENT varchar(50) NULL, PILOT_FEUILLE varchar(10) NULL);
IF OBJECT_ID(N'v2.GROUPE_IMPRESSION') IS NULL
    CREATE TABLE v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE varchar(40) NOT NULL PRIMARY KEY, GPIMP_NOM_LOT varchar(255) NULL,
        GPIMP_DATE_DEBUT datetime NULL, GPIMP_DATE_FIN datetime NULL, GPIMP_NOM_IMPRIMANTE varchar(255) NULL, GPIMP_NB_TENTATIVES tinyint NULL,
        GPIMP_PHASE_TRAITEMENT varchar(10) NOT NULL);

-- La periode par defaut : le mois precedent (InitDate, OPE_GENERER.cs:123-143), fin a 23:59:59 (:253-254).
-- P01 le 10 du mois precedent et P02 son dernier jour a 23:00 : dans la periode (P02 : la fin portee a 23:59:59) ; jumeaux
-- refuses : P03 le 1er du mois courant a 0 h (apres la fin), P05 la veille du 1er du mois precedent (avant le debut). P04 : le 10
-- du meme mois un an plus tot, la colonne « un an plus tôt » (AddYears(-1), :170-171).
DECLARE @debut date = DATEADD(month, -1, DATEFROMPARTS(YEAR(@aujourdhui), MONTH(@aujourdhui), 1));
INSERT INTO v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE, GPIMP_NOM_LOT, GPIMP_DATE_DEBUT, GPIMP_PHASE_TRAITEMENT) VALUES
    ('BANC9-P01', 'BANC9_PILOTAGE', DATEADD(hour, 10, CAST(DATEADD(day, 9, @debut) AS datetime)), 'E'),
    ('BANC9-P02', 'BANC9_PILOTAGE', DATEADD(hour, 23, CAST(DATEADD(day, -1, DATEADD(month, 1, @debut)) AS datetime)), 'E'),
    ('BANC9-P03', 'BANC9_PILOTAGE', CAST(DATEADD(month, 1, @debut) AS datetime), 'E'),
    ('BANC9-P04', 'BANC9_PILOTAGE', DATEADD(hour, 10, CAST(DATEADD(day, 9, DATEADD(year, -1, @debut)) AS datetime)), 'E'),
    ('BANC9-P05', 'BANC9_PILOTAGE', DATEADD(hour, 12, CAST(DATEADD(day, -1, @debut) AS datetime)), 'E');

-- Les cellules. 1900606 (feuille « 3 ») n'est pas generee (garde :273) ; jumeau : 1900601, feuille 1. 1900607 (requete vide)
-- est ignoree (:282) ; jumeau : 1900602. Les autres disent leur valeur, ou pourquoi la V3 ne la calcule pas.
INSERT INTO v2.PILOTAGE_REQUETES (PILOT_ID_PILOT, PILOT_FEUILLE, PILOT_LIGNE, PILOT_COLONNE, PILOT_LIBELLE, PILOT_TRAITEMENT, PILOT_SQL) VALUES
    (1900601, '1', 7, 2, 'Groupes du banc', NULL,
     'SELECT COUNT(*) AS NB FROM GROUPE_IMPRESSION WHERE GPIMP_NOM_LOT = ''BANC9_PILOTAGE'' AND GPIMP_DATE_DEBUT BETWEEN @DateMin AND @DateMax'),
    (1900602, '2', 7, 2, 'Groupes du banc sans année précédente', 'PASANNEEPREC',
     'SELECT COUNT(*) as ''nb'' FROM [dbo].[GROUPE_IMPRESSION] WHERE GPIMP_NOM_LOT = ''BANC9_PILOTAGE'' AND GPIMP_DATE_DEBUT BETWEEN @DateMin AND @DateMax'),
    (1900603, '1', 8, 2, 'Immatriculations du banc', NULL,
     'SELECT COUNT(DSSRC_NUM_GESTION) AS NB FROM DOSSIER_RCS INNER JOIN ACTION_RCS ON DSSRC_ID_DSRCS = ACTRC_ID_DSRCS WHERE ACTRC_DATE_ARRIVEE BETWEEN @DateMin AND @DateMax'),
    (1900604, '1', 9, 6, 'Encaissements du banc', 'ENCAISS', 'SELECT 1 AS NB'),
    (1900605, '2', 8, 2, 'Requête déclarée du banc', NULL, 'declare @x int = 1 select @x as NB from PILOTAGE_REQUETES'),
    (1900606, '3', 7, 2, 'Feuille inconnue du banc', NULL, 'SELECT 1 AS NB'),
    (1900607, '2', 9, 2, 'Sans requête du banc', NULL, '   '),
    (1900608, '2', 10, 2, 'Table absente du banc', NULL, 'SELECT COUNT(*) AS NB FROM TABLE_ABSENTE_BANC9'),
    (1900609, '1', 10, 2, 'Division du banc', NULL, 'SELECT 1 / 0 AS NB FROM PILOTAGE_REQUETES'),
    (1900610, '1', 11, 2, 'Valeur nulle du banc', NULL, 'SELECT CAST(NULL AS int) AS NB');
