-- Jeu de LecteurSurveillanceIp (« Injonctions de payer », PROC_SURVIP), lot 8 du plan « RIG v3 100 % fonctionnel ».
-- Identifiants 18014xx (lot 8 : a partir de 1800000). Ce qui est retenu ou ecarte, et pourquoi : PROC_SURVIP.attendu. Les
-- instances de ce jeu sont des IP : elles entrent aussi a l'ecran « Suivi des preventions » (PROC_IPSUIVI.attendu).
-- Les tables V2 lues, aux types de RIG_DEV (sys.columns de la copie de dev, 26/11/2026), seulement les colonnes lues ; d'autres
-- jeux les creent deja en partie (PROC_MESIN.sql, PROC_MJUD.sql...) : les colonnes manquantes sont ajoutees, et les lignes
-- passent par sp_executesql (une colonne ajoutee ici n'existe pas encore quand SQL Server compile le lot).
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF COL_LENGTH('v2.INSTANCE', 'INSTN_TYPE_INSTANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TYPE_INSTANCE varchar(2) NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_IP_EUROPEENNE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_IP_EUROPEENNE bit NULL;
IF OBJECT_ID(N'v2.EVENEMENT_PROCEDURE') IS NULL
    CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int NOT NULL PRIMARY KEY, EVNPR_ID_INSTN int NULL, EVNPR_NUM_ORDRE smallint NULL,
        EVNPR_CODE_EVE_PRO varchar(10) NOT NULL, EVNPR_DATE datetime NULL, EVNPR_SURVENU bit NULL, EVNPR_DELAI_EXPIRE bit NULL);
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MAXI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MAXI datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MINI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MINI datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_FIXE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_FIXE datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_SIGNIF_A_PERSONNE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_SIGNIF_A_PERSONNE bit NULL;
IF OBJECT_ID(N'v2.DECISION_JUDICIAIRE') IS NULL
    CREATE TABLE v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC int NOT NULL PRIMARY KEY, DCJDC_ID_INSTN int NOT NULL, DCJDC_NUM_ORDRE smallint NOT NULL,
        DCJDC_DATE_PREVUE datetime NULL, DCJDC_DATE datetime NULL, DCJDC_TYPE_DECISION varchar(10) NULL);
IF OBJECT_ID(N'v2.PARTIE') IS NULL
    CREATE TABLE v2.PARTIE (PRT_ID_PRT int NOT NULL PRIMARY KEY, PRT_ID_INSTN int NOT NULL, PRT_NUM_ORDRE smallint NOT NULL,
        PRT_CIVILITE varchar(7) NOT NULL, PRT_DESIGNATION varchar(512) NULL, PRT_NOM varchar(512) NULL, PRT_SIREN varchar(10) NULL,
        PRT_MODE_PRESENTATION varchar(3) NULL, PRT_QUALIF_PARTIE varchar(5) NOT NULL, PRT_DATE_ACTE datetime NULL);

EXEC sp_executesql N'
-- Les injonctions de payer (type IP ; 1801408 europeenne).
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TYPE_INSTANCE, INSTN_IP_EUROPEENNE) VALUES
    (1801401, ''2026IP81401'', N''IP TITRE ALPHA'', ''IP'', 0), (1801402, ''2026IP81402'', N''IP TITRE APRES 2022'', ''IP'', 0),
    (1801403, ''2026IP81403'', N''IP NON AVENUE GAMMA'', ''IP'', 0), (1801404, ''2026IP81404'', N''IP CONSIGNEE'', ''IP'', 0),
    (1801405, ''2026IP81405'', N''IP CADUCITE EPSILON'', ''IP'', 0), (1801406, ''2026IP81406'', N''IP CERTIFICAT A VENIR'', ''IP'', 0),
    (1801407, ''2026IP81407'', N''IP CERTIFICAT ZETA'', ''IP'', 0), (1801408, ''2026IP81408'', N''IP EUROPEENNE'', ''IP'', 1),
    (1801409, ''2026IP81409'', N''IP SIGNIFICATION EXPIREE'', ''IP'', 0);

-- Les ordonnances : 1801401 rendue avant la loi du 1er mars 2022, 1801402 apres ; 1801403 il y a 60 jours.
INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_NUM_ORDRE, DCJDC_DATE) VALUES
    (1801451, 1801401, 1, ''20210601''), (1801452, 1801402, 1, ''20230601''), (1801453, 1801403, 1, DATEADD(DAY, -60, @aujourdhui));

-- Les parties de 1801401 (demandeur D, defendeur F) ; les autres IP s''affichent par leur nom courant. Ces deux parties entrent
-- aussi a l''ecran « Parties » (PROC_PARTIES lit toutes les parties) : PROC_PARTIES.attendu et .gestes les portent.
INSERT INTO v2.PARTIE (PRT_ID_PRT, PRT_ID_INSTN, PRT_NUM_ORDRE, PRT_CIVILITE, PRT_DESIGNATION, PRT_QUALIF_PARTIE) VALUES
    (1801461, 1801401, 1, ''PM'', N''CREANCIER ALPHA'', ''D''), (1801462, 1801401, 2, ''PM'', N''DEBITEUR BETA'', ''F'');

-- Les evenements. Un evenement FUTUR : ni survenu ni expire ; echu si sa date maxi, fixe ou mini est passee.
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_INSTN, EVNPR_NUM_ORDRE, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU,
    EVNPR_DELAI_EXPIRE, EVNPR_DATE_MAXI, EVNPR_DATE_FIXE, EVNPR_SIGNIF_A_PERSONNE) VALUES
    -- 1801401 : opposition attendue (IOIP), echue il y a 5 jours, ordonnance de 2021 : titre executoire, retenue ; signifiee
    (1801471, 1801401, 1, ''ISIGNI'', DATEADD(DAY, -40, @aujourdhui), 1, 0, NULL, NULL, 1),
    (1801472, 1801401, 2, ''IOIP'', NULL, 0, 0, DATEADD(DAY, -5, @aujourdhui), NULL, NULL),
    -- 1801402 : la meme, mais ordonnance de 2023 : ecartee (plus d''executoire apres la loi de 2022) -- jumeau 1801401
    (1801473, 1801402, 1, ''IOIP'', NULL, 0, 0, DATEADD(DAY, -5, @aujourdhui), NULL, NULL),
    -- 1801403 : signification attendue (ISIGNI) a date fixe passee : non avenue, retenue
    (1801474, 1801403, 1, ''ISIGNI'', NULL, 0, 0, NULL, DATEADD(DAY, -3, @aujourdhui), NULL),
    -- 1801404 : consignation attendue echue, mais une consignation est SURVENUE : ecartee (elle clot la caducite) -- jumeau 1801405
    (1801475, 1801404, 1, ''ICONSOIP'', NULL, 0, 0, DATEADD(DAY, -1, @aujourdhui), NULL, NULL),
    (1801476, 1801404, 2, ''ICONSOIP'', DATEADD(DAY, -2, @aujourdhui), 1, 0, NULL, NULL, NULL),
    -- 1801405 : consignation attendue echue il y a 2 jours : caducite, retenue
    (1801477, 1801405, 1, ''IPCONSOPOS'', NULL, 0, 0, DATEADD(DAY, -2, @aujourdhui), NULL, NULL),
    -- 1801406 : certificat attendu dans 3 jours : ecarte (pas echu) -- jumeau 1801407
    (1801478, 1801406, 1, ''IPCERTNOPP'', NULL, 0, 0, DATEADD(DAY, 3, @aujourdhui), NULL, NULL),
    -- 1801407 : certificat attendu echu il y a 4 jours : retenu
    (1801479, 1801407, 1, ''IPCERTNOPP'', NULL, 0, 0, DATEADD(DAY, -4, @aujourdhui), NULL, NULL),
    -- 1801408 : IP europeenne, opposition echue : ecartee (INSTN_IP_EUROPEENNE)
    (1801480, 1801408, 1, ''IOIP'', NULL, 0, 0, DATEADD(DAY, -5, @aujourdhui), NULL, NULL),
    -- 1801409 : signification attendue mais deja EXPIREE : ecartee (EVNPR_DELAI_EXPIRE) -- jumeau 1801403
    (1801481, 1801409, 1, ''ISIGNI'', NULL, 0, 1, DATEADD(DAY, -3, @aujourdhui), NULL, NULL);
',
    N'@aujourdhui datetime', @aujourdhui;
