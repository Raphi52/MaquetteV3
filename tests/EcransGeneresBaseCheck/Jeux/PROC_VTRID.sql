-- Jeu de LecteurTribunalDigital (« Tribunal digital », PROC_VTRID), lot 4 du plan-f7ba4de2. Identifiants 14004xx et
-- 14005xx (lot 4 : 1 400 000 a 1 499 999). Ce qui est retenu ou ecarte, et pourquoi : PROC_VTRID.attendu.
-- Un envoi n'est une ligne qu'avec une demande d'edition (la V2 joint DEMANDE_EDITION). Toute ligne de v2.DEMANDE_EDITION
-- entre dans « File des editions » (PROC_POOLEDITION.sql l. 5-6) : ce jeu n'en cree aucune et ne cree pas la table ; ses
-- envois visent les editions de PROC_POOLEDITION.sql, joue avant lui (ordre alphabetique) -- 200412 « Certificat de
-- non-faillite » (Maître Banc, creee il y a 14 h), 200413 « Lettre de relance » (38 h), 200414 « Extrait Kbis » (62 h),
-- 200415 « Extrait Kbis » (Maître Banc, 86 h), 200416 « Accusé de dépôt » (110 h), 200419 « Certificat » (EDITION BETA
-- SAS, 182 h), 200411 (+10 h) et 200420 (206 h).
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
IF OBJECT_ID(N'v2.TRIBUNAL_DIGITAL_ENVOI') IS NULL
    CREATE TABLE v2.TRIBUNAL_DIGITAL_ENVOI (TDE_ID_TDE int NOT NULL PRIMARY KEY, TDE_NUM_AFFAIRE varchar(20) NULL,
        TDE_NUMEROS_ENVOI varchar(30) NULL, TDE_DATE_ENVOI datetime NULL, TDE_DATE_SES_AR datetime NULL, TDE_DATE_CR datetime NULL,
        TDE_ETAT varchar(20) NULL, TDE_ERREUR_SES varchar(max) NULL);
IF OBJECT_ID(N'v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL (DETD_ID_DETD int NOT NULL PRIMARY KEY, DETD_ID_DMNED int NULL, DETD_ID_TDE int NULL);
IF OBJECT_ID(N'v2.DEMAT_TRIBUNAL_DIGITAL_CREFO') IS NULL
    CREATE TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO int NOT NULL PRIMARY KEY, DTDCREFO_DATE_CREATION datetime NOT NULL,
        DTDCREFO_ETAT_TRAITEMENT_RIG varchar(2) NULL, DTDCREFO_TYPE_FORMALITE varchar(20) NOT NULL, DTDCREFO_NUM_CREFO varchar(30) NOT NULL,
        DTDCREFO_NUM_AFFAIRE varchar(20) NULL, DTDCREFO_ERREUR_SES varchar(max) NULL);

-- Les envois. La date filtree est l'envoi ; l'ordre, la creation de la premiere edition de l'envoi, la plus recente d'abord.
INSERT INTO v2.TRIBUNAL_DIGITAL_ENVOI (TDE_ID_TDE, TDE_NUM_AFFAIRE, TDE_NUMEROS_ENVOI, TDE_DATE_ENVOI, TDE_DATE_SES_AR, TDE_DATE_CR, TDE_ETAT,
    TDE_ERREUR_SES) VALUES
    -- compte rendu OK (4) : Validee, preuve de depot ; envoi GROUPE de deux editions -> une ligne, la premiere (200414)
    (1400401, '2026J00401', 'E1400401', DATEADD(HOUR, -39, @aujourdhui), DATEADD(HOUR, -38, @aujourdhui), DATEADD(HOUR, -20, @aujourdhui), '4', NULL),
    -- envoye (3), accuse de reception, compte rendu attendu : EnCours
    (1400402, '2026J00402', 'E1400402', DATEADD(HOUR, -15, @aujourdhui), DATEADD(HOUR, -14, @aujourdhui), NULL, '3', NULL),
    -- compte rendu KO (9), erreur sur deux lignes : AFaire, la premiere ligne de l'erreur
    (1400403, '2026J00403', 'E1400403', DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), DATEADD(HOUR, -20, @aujourdhui), '9',
        N'Erreur SES : numéro de rôle inconnu' + CHAR(13) + CHAR(10) + N'Détail technique'),
    -- erreur d'envoi (8) sans texte, sans numero d'envoi, envoye le jour moins 7 a 0 h (borne basse retenue) : AFaire
    (1400404, '2026J00404', NULL, DATEADD(DAY, -7, @aujourdhui), NULL, NULL, '8', NULL),
    -- envoye le jour meme a 23 h (borne haute retenue), ni accuse ni compte rendu : EnCours
    (1400405, '2026J00405', 'E1400405', DATEADD(HOUR, 23, @aujourdhui), NULL, NULL, '3', NULL),
    -- compte rendu recu, etat ni 4 ni 9 ni 8 : Validee sans la preuve
    (1400406, '2026J00406', 'E1400406', DATEADD(DAY, -4, @aujourdhui), NULL, DATEADD(HOUR, -20, @aujourdhui), NULL, NULL),
    -- ECARTE : envoye le jour moins 8 a 23 h -- jumeau retenu 1400404 (jour moins 7 a 0 h)
    (1400407, '2026J00407', 'E1400407', DATEADD(HOUR, -169, @aujourdhui), NULL, NULL, '3', NULL),
    -- ECARTE : envoye le lendemain a 0 h -- jumeau retenu 1400405 (le jour meme a 23 h)
    (1400408, '2026J00408', 'E1400408', DATEADD(DAY, 1, @aujourdhui), NULL, NULL, '3', NULL),
    -- ECARTE : aucune edition -- jumeau retenu 1400402
    (1400409, '2026J00409', 'E1400409', DATEADD(HOUR, -15, @aujourdhui), NULL, NULL, '3', NULL),
    -- ECARTE : son edition n'existe pas dans DEMANDE_EDITION -- jumeau retenu 1400402
    (1400410, '2026J00410', 'E1400410', DATEADD(HOUR, -15, @aujourdhui), NULL, NULL, '3', NULL),
    -- ECARTE : pas encore envoye (2, sans date d'envoi) -- jumeau retenu 1400402
    (1400411, '2026J00411', NULL, NULL, NULL, NULL, '2', NULL);

INSERT INTO v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL (DETD_ID_DETD, DETD_ID_DMNED, DETD_ID_TDE) VALUES
    (1400421, 200414, 1400401), (1400422, 200411, 1400401),
    (1400423, 200412, 1400402), (1400424, 200413, 1400403), (1400425, 200415, 1400404), (1400426, 200416, 1400405),
    (1400427, 200419, 1400406), (1400428, 200420, 1400407), (1400429, 200420, 1400408), (1400430, 1499999, 1400410),
    (1400431, 200420, 1400411),
    -- une edition prete, pas encore envoyee (DETD_ID_TDE NULL) : le compteur de la V2, pas une ligne
    (1400432, 200410, NULL);

-- Les demandes recues du portail : la V2 ne coche que « En attente » (etat de traitement RIG vide), sans date ni type.
INSERT INTO v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO, DTDCREFO_DATE_CREATION, DTDCREFO_ETAT_TRAITEMENT_RIG,
    DTDCREFO_TYPE_FORMALITE, DTDCREFO_NUM_CREFO, DTDCREFO_NUM_AFFAIRE, DTDCREFO_ERREUR_SES) VALUES
    -- en attente, requete au juge commissaire, sans affaire : AFaire
    (1400501, DATEADD(HOUR, -14, @aujourdhui), NULL, 'TD_RQT_JUGE_COMM', '26001400501', NULL, NULL),
    -- en attente depuis plus d'un an (aucune date a l'ouverture), assignation avec son role et une erreur SES : AFaire
    (1400502, DATEADD(DAY, -400, @aujourdhui), NULL, 'TD_ASSIGNATION', '25001400502', '2025J00502', N'Erreur SES : pièce illisible'),
    -- en attente, type inconnu de la V2 : le code tel quel
    (1400505, DATEADD(DAY, -5, @aujourdhui), NULL, 'TD_NOUVEAU', '26001400505', NULL, NULL),
    -- ECARTEES : acceptee (A), refusee (R) -- jumeau retenu 1400501 (en attente)
    (1400503, DATEADD(HOUR, -14, @aujourdhui), 'A', 'TD_RQT_JUGE_COMM', '26001400503', NULL, NULL),
    (1400504, DATEADD(HOUR, -14, @aujourdhui), 'R', 'TD_RQT_JUGE_COMM', '26001400504', NULL, NULL);
