-- fix-ok: lot 6 du plan-676a4b19 -- trois lignes ajoutees pour la RECHERCHE (envoi 1606412 et son edition, demande acceptee
-- 1606506), a dates fixes hors de la liste d'ouverture : PROC_VTRID.attendu inchange, vert au banc EcransGeneresBase (26/11/2026).
-- Jeu de LecteurTribunalDigital (« Tribunal digital », PROC_VTRID), lot 4 du plan-f7ba4de2. Identifiants 14004xx et
-- 14005xx (lot 4 : 1 400 000 a 1 499 999). Ce qui est retenu ou ecarte, et pourquoi : PROC_VTRID.attendu.
-- Un envoi n'est une ligne qu'avec une demande d'edition (la V2 joint DEMANDE_EDITION). Toute ligne de v2.DEMANDE_EDITION
-- entre dans « File des editions » (PROC_POOLEDITION.sql l. 5-6) : ce jeu n'en cree aucune et ne cree pas la table ; ses
-- envois visent les editions de PROC_POOLEDITION.sql, joue avant lui (ordre alphabetique) -- 200412 « Certificat de
-- non-faillite » (Maître Banc, creee il y a 14 h), 200413 « Lettre de relance » (38 h), 200414 « Extrait Kbis » (62 h),
-- 200415 « Extrait Kbis » (Maître Banc, 86 h), 200416 « Accusé de dépôt » (110 h), 200419 « Certificat » (EDITION BETA
-- SAS, 182 h), 200411 (+10 h) et 200420 (206 h).
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
-- Un autre jeu peut avoir cree une de ces tables avant celui-ci avec moins de colonnes : v2.DEMAT_TRIBUNAL_DIGITAL_CREFO
-- l'est par PROC_TRANSDOC.sql (ID, DATE_CREATION, ID_DMND, NUM_AFFAIRE). Chaque table est donc creee si elle manque,
-- puis recoit chaque colonne qui lui manque ; les lignes passent par sp_executesql (une colonne ajoutee dans ce meme lot
-- d'instructions n'existe pas encore quand SQL Server le compile). Les colonnes ajoutees a une table qui a deja des
-- lignes sont nullables, meme DTDCREFO_TYPE_FORMALITE et DTDCREFO_NUM_CREFO, NOT NULL dans RIG_DEV.
IF OBJECT_ID(N'v2.TRIBUNAL_DIGITAL_ENVOI') IS NULL
    CREATE TABLE v2.TRIBUNAL_DIGITAL_ENVOI (TDE_ID_TDE int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_NUM_AFFAIRE') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_NUM_AFFAIRE varchar(20) NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_NUMEROS_ENVOI') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_NUMEROS_ENVOI varchar(30) NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_DATE_ENVOI') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_DATE_ENVOI datetime NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_DATE_SES_AR') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_DATE_SES_AR datetime NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_DATE_CR') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_DATE_CR datetime NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_ETAT') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_ETAT varchar(20) NULL;
IF COL_LENGTH('v2.TRIBUNAL_DIGITAL_ENVOI', 'TDE_ERREUR_SES') IS NULL ALTER TABLE v2.TRIBUNAL_DIGITAL_ENVOI ADD TDE_ERREUR_SES varchar(max) NULL;

IF OBJECT_ID(N'v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL (DETD_ID_DETD int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL', 'DETD_ID_DMNED') IS NULL ALTER TABLE v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL ADD DETD_ID_DMNED int NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL', 'DETD_ID_TDE') IS NULL ALTER TABLE v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL ADD DETD_ID_TDE int NULL;

IF OBJECT_ID(N'v2.DEMAT_TRIBUNAL_DIGITAL_CREFO') IS NULL
    CREATE TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO int NOT NULL PRIMARY KEY, DTDCREFO_DATE_CREATION datetime NOT NULL);
-- La liste « Demandes reçues » prend toute demande dont DTDCREFO_ETAT_TRAITEMENT_RIG est vide (la V2 ne filtre que sur
-- cette colonne a l'ouverture : DataGridViewDemandesRecus.cs:283-284, OPE_RECHERCHE_DEMANDES_RECUES.cs:76-81). Si la
-- table a deja les lignes d'un autre jeu qui ignore cette colonne, ce jeu la leur ajoute en premier et les dit acceptees
-- ('A') : sinon elles entreraient dans cet ecran (PROC_TRANSDOC.sql, lignes 1200111-1200125 ; regle du banc, en tete du
-- Program.cs). Dans la V2, une demande recue en attente va avec une demande encore qualifiee (RIG_DEV : EJC_TRIDIG Q),
-- une acceptee avec une demande integree (DOC_TRIDIG V : 120) : les deux ecrans peuvent montrer le meme document.
IF COL_LENGTH('v2.DEMAT_TRIBUNAL_DIGITAL_CREFO', 'DTDCREFO_ETAT_TRAITEMENT_RIG') IS NULL
BEGIN
    ALTER TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO ADD DTDCREFO_ETAT_TRAITEMENT_RIG varchar(2) NULL;
    EXEC sp_executesql N'UPDATE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO SET DTDCREFO_ETAT_TRAITEMENT_RIG = ''A'';';
END
IF COL_LENGTH('v2.DEMAT_TRIBUNAL_DIGITAL_CREFO', 'DTDCREFO_TYPE_FORMALITE') IS NULL ALTER TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO ADD DTDCREFO_TYPE_FORMALITE varchar(20) NULL;
IF COL_LENGTH('v2.DEMAT_TRIBUNAL_DIGITAL_CREFO', 'DTDCREFO_NUM_CREFO') IS NULL ALTER TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO ADD DTDCREFO_NUM_CREFO varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_TRIBUNAL_DIGITAL_CREFO', 'DTDCREFO_NUM_AFFAIRE') IS NULL ALTER TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO ADD DTDCREFO_NUM_AFFAIRE varchar(20) NULL;
IF COL_LENGTH('v2.DEMAT_TRIBUNAL_DIGITAL_CREFO', 'DTDCREFO_ERREUR_SES') IS NULL ALTER TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO ADD DTDCREFO_ERREUR_SES varchar(max) NULL;

-- Les envois. La date filtree est l'envoi ; l'ordre, la creation de la premiere edition de l'envoi, la plus recente d'abord.
EXEC sp_executesql N'
INSERT INTO v2.TRIBUNAL_DIGITAL_ENVOI (TDE_ID_TDE, TDE_NUM_AFFAIRE, TDE_NUMEROS_ENVOI, TDE_DATE_ENVOI, TDE_DATE_SES_AR, TDE_DATE_CR, TDE_ETAT,
    TDE_ERREUR_SES) VALUES
    -- compte rendu OK (4) : Validee, preuve de depot ; envoi GROUPE de deux editions -> une ligne, la premiere (200414)
    (1400401, ''2026J00401'', ''E1400401'', DATEADD(HOUR, -39, @aujourdhui), DATEADD(HOUR, -38, @aujourdhui), DATEADD(HOUR, -20, @aujourdhui), ''4'', NULL),
    -- envoye (3), accuse de reception, compte rendu attendu : EnCours
    (1400402, ''2026J00402'', ''E1400402'', DATEADD(HOUR, -15, @aujourdhui), DATEADD(HOUR, -14, @aujourdhui), NULL, ''3'', NULL),
    -- compte rendu KO (9), erreur sur deux lignes : AFaire, la premiere ligne de l''erreur
    (1400403, ''2026J00403'', ''E1400403'', DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), DATEADD(HOUR, -20, @aujourdhui), ''9'',
        N''Erreur SES : numéro de rôle inconnu'' + CHAR(13) + CHAR(10) + N''Détail technique''),
    -- erreur d''envoi (8) sans texte, sans numero d''envoi, envoye le jour moins 7 a 0 h (borne basse retenue) : AFaire
    (1400404, ''2026J00404'', NULL, DATEADD(DAY, -7, @aujourdhui), NULL, NULL, ''8'', NULL),
    -- envoye le jour meme a 23 h (borne haute retenue), ni accuse ni compte rendu : EnCours
    (1400405, ''2026J00405'', ''E1400405'', DATEADD(HOUR, 23, @aujourdhui), NULL, NULL, ''3'', NULL),
    -- compte rendu recu, etat ni 4 ni 9 ni 8 : Validee sans la preuve
    (1400406, ''2026J00406'', ''E1400406'', DATEADD(DAY, -4, @aujourdhui), NULL, DATEADD(HOUR, -20, @aujourdhui), NULL, NULL),
    -- ECARTE : envoye le jour moins 8 a 23 h -- jumeau retenu 1400404 (jour moins 7 a 0 h)
    (1400407, ''2026J00407'', ''E1400407'', DATEADD(HOUR, -169, @aujourdhui), NULL, NULL, ''3'', NULL),
    -- ECARTE : envoye le lendemain a 0 h -- jumeau retenu 1400405 (le jour meme a 23 h)
    (1400408, ''2026J00408'', ''E1400408'', DATEADD(DAY, 1, @aujourdhui), NULL, NULL, ''3'', NULL),
    -- ECARTE : aucune edition -- jumeau retenu 1400402
    (1400409, ''2026J00409'', ''E1400409'', DATEADD(HOUR, -15, @aujourdhui), NULL, NULL, ''3'', NULL),
    -- ECARTE : son edition n''existe pas dans DEMANDE_EDITION -- jumeau retenu 1400402
    (1400410, ''2026J00410'', ''E1400410'', DATEADD(HOUR, -15, @aujourdhui), NULL, NULL, ''3'', NULL),
    -- ECARTE : pas encore envoye (2, sans date d''envoi) -- jumeau retenu 1400402
    (1400411, ''2026J00411'', NULL, NULL, NULL, NULL, ''2'', NULL),
    -- lot 6 (plan-676a4b19), pour la RECHERCHE (PROC_VTRID.recherches) : envoye le 12/03/2025, hors de la liste d''ouverture
    (1606412, ''2025J00412'', ''E1606412'', ''20250312 10:00'', NULL, ''20250313'', ''4'', NULL);

INSERT INTO v2.DEMANDE_EDITION_TRIBUNAL_DIGITAL (DETD_ID_DETD, DETD_ID_DMNED, DETD_ID_TDE) VALUES
    (1400421, 200414, 1400401), (1400422, 200411, 1400401),
    (1400423, 200412, 1400402), (1400424, 200413, 1400403), (1400425, 200415, 1400404), (1400426, 200416, 1400405),
    (1400427, 200419, 1400406), (1400428, 200420, 1400407), (1400429, 200420, 1400408), (1400430, 1499999, 1400410),
    (1400431, 200420, 1400411),
    -- une edition prete, pas encore envoyee (DETD_ID_TDE NULL) : le compteur de la V2, pas une ligne
    (1400432, 200410, NULL),
    -- lot 6 : l''edition de l''envoi 1606412
    (1606433, 200419, 1606412);

-- Les demandes recues du portail : la V2 ne coche que « En attente » (etat de traitement RIG vide), sans date ni type.
-- Aucune n''a de demande liee (DTDCREFO_ID_DMND) : elles n''entrent pas dans « Transmission » (LecteurTransmissionDocuments
-- part de v2.DEMANDE).
INSERT INTO v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO, DTDCREFO_DATE_CREATION, DTDCREFO_ETAT_TRAITEMENT_RIG,
    DTDCREFO_TYPE_FORMALITE, DTDCREFO_NUM_CREFO, DTDCREFO_NUM_AFFAIRE, DTDCREFO_ERREUR_SES) VALUES
    -- en attente, requete au juge commissaire, sans affaire : AFaire
    (1400501, DATEADD(HOUR, -14, @aujourdhui), NULL, ''TD_RQT_JUGE_COMM'', ''26001400501'', NULL, NULL),
    -- en attente depuis plus d''un an (aucune date a l''ouverture), assignation avec son role et une erreur SES : AFaire
    (1400502, DATEADD(DAY, -400, @aujourdhui), NULL, ''TD_ASSIGNATION'', ''25001400502'', ''2025J00502'', N''Erreur SES : pièce illisible''),
    -- en attente, type inconnu de la V2 : le code tel quel
    (1400505, DATEADD(DAY, -5, @aujourdhui), NULL, ''TD_NOUVEAU'', ''26001400505'', NULL, NULL),
    -- ECARTEES : acceptee (A), refusee (R) -- jumeau retenu 1400501 (en attente)
    (1400503, DATEADD(HOUR, -14, @aujourdhui), ''A'', ''TD_RQT_JUGE_COMM'', ''26001400503'', NULL, NULL),
    (1400504, DATEADD(HOUR, -14, @aujourdhui), ''R'', ''TD_RQT_JUGE_COMM'', ''26001400504'', NULL, NULL),
    -- lot 6, pour la RECHERCHE : une demande acceptee a date fixe (ecartee a l''ouverture, comme 1400503)
    (1606506, ''20250311'', ''A'', ''TD_ASSIGNATION'', ''25001606506'', ''2025J00506'', NULL);',
    N'@aujourdhui datetime', @aujourdhui;
