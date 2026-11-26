-- Jeu de LecteurNotifications (« Notifications », PROC_VNOTIF), lot 4 du plan-f7ba4de2. Identifiants 14001xx a 14003xx
-- (lot 4 : 1 400 000 a 1 499 999). Ce qui est retenu ou ecarte, et pourquoi : PROC_VNOTIF.attendu.
-- Pour ne poser aucune ligne qui entrerait dans un autre ecran, ce jeu ne cree ni dossier ni demande : il s'appuie sur
-- ceux de PROC_POOLEDITION.sql, joue avant lui (ordre alphabetique) -- dossiers 200401 « EDITION ALPHA SARL » (2020B40401)
-- et 200402 « EDITION BETA SAS » (2020B40402), demande 200421 (dossier 200401). Ses actions (rec.ActionRegistre) n'ont
-- ni depot ni demande qui les vise ; ses procedures collectives n'ont pas de dossier (PRCLL_ID_DSRCS NULL).
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
IF OBJECT_ID(N'v2.ETAT_TRAITMENT') IS NULL
    CREATE TABLE v2.ETAT_TRAITMENT (R_ETATTRAITM_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATTRAITM_LIBELLE_ECRAN varchar(128) NOT NULL);
IF OBJECT_ID(N'v2.ETAT_DEMAT_NOTIF') IS NULL
    CREATE TABLE v2.ETAT_DEMAT_NOTIF (R_ETATDEMATN_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATDEMATN_LIBELLE_ECRAN varchar(128) NOT NULL);
IF OBJECT_ID(N'v2.NOTIFICATION_RCS_XML') IS NULL
    CREATE TABLE v2.NOTIFICATION_RCS_XML (NTFXML_ID_NTFXML int NOT NULL PRIMARY KEY, NTFXML_ID_ACTRC int NULL,
        NTFXML_TYPE_NOTIFICATION varchar(3) NOT NULL, NTFXML_CODE_GREFFE varchar(4) NOT NULL, NTFXML_ETAT_TRAITMENT varchar(2) NULL,
        NTFXML_DATE_GENERATION datetime NULL, NTFXML_DATE_RECEPTION datetime NULL, NTFXML_DATE_ENVOI datetime NULL,
        NTFXML_LIBELLE_REJET varchar(200) NULL);
IF OBJECT_ID(N'v2.NOTIFICATION_JUD_XML') IS NULL
    CREATE TABLE v2.NOTIFICATION_JUD_XML (NTFJUDXML_ID_NTFJUDXML int NOT NULL PRIMARY KEY, NTFJUDXML_ID_PRCLL int NOT NULL,
        NTFJUDXML_ID_DSRCS int NULL, NTFJUDXML_ID_NINSC int NULL, NTFJUDXML_CODE_GREFFE varchar(4) NOT NULL,
        NTFJUDXML_TYPE_NOTIFICATION varchar(3) NOT NULL, NTFJUDXML_ETAT_TRAITMENT varchar(5) NOT NULL,
        NTFJUDXML_DATE_GENERATION datetime NOT NULL, NTFJUDXML_DATE_ENVOI datetime NULL, NTFJUDXML_DATE_RECEPTION datetime NULL,
        NTFJUDXML_ERREUR varchar(200) NULL);
IF OBJECT_ID(N'v2.DEMAT_NOTIF') IS NULL
    CREATE TABLE v2.DEMAT_NOTIF (DMTNTF_ID_DMTNTF int NOT NULL PRIMARY KEY, DMTNTF_DATE_CREATION datetime NOT NULL,
        DMTNTF_ETAT_TRAITEMENT varchar(2) NULL, DMTNTF_DATE_RECEPTION_AT datetime NULL, DMTNTF_NUM_GESTION varchar(10) NULL,
        DMTNTF_ID_DMND int NULL, DMTNTF_CODE_GREFFE_EMETTEUR varchar(4) NULL, DMTNTF_TYPE_NOTIF varchar(5) NULL);
IF OBJECT_ID(N'v2.NON_INSCRIT') IS NULL
    CREATE TABLE v2.NON_INSCRIT (NINSC_ID_NINSC int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NUM_NON_INSCRIT') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NUM_NON_INSCRIT varchar(11) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_DENOMINATION') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_DENOMINATION varchar(max) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NOM_USAGE') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NOM_USAGE varchar(70) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_PRENOMS') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_PRENOMS varchar(62) NULL;
-- v2.PROCEDURE_COLLECTIVE est creee par le banc (Program.cs, section 1b), identifiant en IDENTITY : meme forme ici.
IF OBJECT_ID(N'v2.PROCEDURE_COLLECTIVE') IS NULL
    CREATE TABLE v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL int IDENTITY PRIMARY KEY, PRCLL_ID_DSRCS int NULL);
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(10) NULL;

-- Les etats, libelles de RIG_DEV.
INSERT INTO v2.ETAT_TRAITMENT (R_ETATTRAITM_VALEUR, R_ETATTRAITM_LIBELLE_ECRAN)
SELECT v.c, v.l FROM (VALUES ('2', 'A envoyer'), ('3', 'Envoyé'), ('4', 'Réceptionné'), ('5', 'Refusé Greffe'), ('6', 'Validé Greffe'),
    ('8', 'Erreur envoi'), ('9', 'Rejet Docapost'), ('10', 'Clôturé')) AS v (c, l)
WHERE NOT EXISTS (SELECT 1 FROM v2.ETAT_TRAITMENT AS t WHERE t.R_ETATTRAITM_VALEUR = v.c);
INSERT INTO v2.ETAT_DEMAT_NOTIF (R_ETATDEMATN_VALEUR, R_ETATDEMATN_LIBELLE_ECRAN)
SELECT v.c, v.l FROM (VALUES ('1', 'En Attente'), ('2', 'Traité Greffe'), ('3', 'Envoyé'), ('4', 'Réceptionné'), ('8', 'Erreur envoi'),
    ('9', 'Rejet Docapost')) AS v (c, l)
WHERE NOT EXISTS (SELECT 1 FROM v2.ETAT_DEMAT_NOTIF AS t WHERE t.R_ETATDEMATN_VALEUR = v.c);

-- Les actions qui portent les notifications RCS (code processus hors de ceux des autres lecteurs du banc).
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1400111, 200401, 'BANCNOTIF', 'C', 'V', 1),
    (1400112, 200402, 'BANCNOTIF', 'C', 'V', 1);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- Emises RCS. La date filtree est la generation ; l'ordre, la date de reception (NULL d'abord), puis le numero.
INSERT INTO v2.NOTIFICATION_RCS_XML (NTFXML_ID_NTFXML, NTFXML_ID_ACTRC, NTFXML_TYPE_NOTIFICATION, NTFXML_CODE_GREFFE, NTFXML_ETAT_TRAITMENT,
    NTFXML_DATE_GENERATION, NTFXML_DATE_RECEPTION, NTFXML_DATE_ENVOI, NTFXML_LIBELLE_REJET) VALUES
    -- erreur d'envoi (8), avec son motif : AFaire
    (1400101, 1400111, 'MTS', '2602', '8', DATEADD(HOUR, -14, @aujourdhui), NULL, NULL, 'Greffe destinataire injoignable'),
    -- a envoyer (2), generee le jour meme a 23 h (borne haute retenue) : EnCours
    (1400102, 1400111, 'MR2', '3802', '2', DATEADD(HOUR, 23, @aujourdhui), NULL, NULL, NULL),
    -- refusee par le greffe (5), generee le jour moins 30 a 0 h (borne basse retenue), envoyee puis recue : AFaire
    (1400103, 1400112, 'MTS', '6901', '5', DATEADD(DAY, -30, @aujourdhui), DATEADD(DAY, -28, @aujourdhui), DATEADD(DAY, -29, @aujourdhui), NULL),
    -- ECARTEES : envoyee (3) -- jumeau retenu 1400101 (8) ; validee (6) -- jumeau 1400103 (5)
    (1400104, 1400111, 'MTS', '2602', '3', DATEADD(DAY, -2, @aujourdhui), NULL, DATEADD(DAY, -2, @aujourdhui), NULL),
    (1400106, 1400111, 'MTS', '2602', '6', DATEADD(DAY, -2, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), DATEADD(DAY, -2, @aujourdhui), NULL),
    -- ECARTEE : generee le jour moins 31 a 23 h -- jumeau retenu 1400103 (jour moins 30 a 0 h)
    (1400105, 1400111, 'MTS', '2602', '2', DATEADD(HOUR, -721, @aujourdhui), NULL, NULL, NULL),
    -- ECARTEE : sans action (la V2 joint ACTION_RCS) -- jumeau retenu 1400101
    (1400107, NULL, 'MTS', '2602', '8', DATEADD(DAY, -1, @aujourdhui), NULL, NULL, NULL),
    -- ECARTEE : generee le lendemain a 0 h -- jumeau retenu 1400102 (le jour meme a 23 h)
    (1400109, 1400111, 'MR2', '3802', '2', DATEADD(DAY, 1, @aujourdhui), NULL, NULL, NULL),
    -- lot 6 (plan-676a4b19), pour la RECHERCHE (PROC_VNOTIF.recherches) : envoyee (3) le 10/03/2025, hors de l'ouverture
    (1606110, 1400111, 'MTS', '2602', '3', '20250310', NULL, '20250310', NULL);

-- Les procedures collectives et les non-inscrits des notifications judiciaires. Leurs colonnes viennent peut-etre d'etre
-- ajoutees ci-dessus : l'insertion est compilee a part (sp_executesql), comme PROC_EDI_RPJC_TRCNC.sql.
-- fix-ok: le banc envoie tout le fichier en UN lot (Program.cs:379, une SqlCommand) : pas de GO, et une colonne ajoutee
-- par ALTER ne peut pas etre lue dans le meme lot si la table existait deja.
EXEC sp_executesql N'
SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE ON;
INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_ID_DSRCS, PRCLL_NUM_PC) VALUES
    (1400211, NULL, ''2026P00211''), (1400212, NULL, ''2026P00212''), (1400213, NULL, ''2026P00213'');
SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE OFF;
INSERT INTO v2.NON_INSCRIT (NINSC_ID_NINSC, NINSC_NUM_NON_INSCRIT, NINSC_DENOMINATION, NINSC_NOM_USAGE, NINSC_PRENOMS) VALUES
    (1400221, ''N2026000221'', ''NOTIF NON INSCRIT SARL'', NULL, NULL),
    (1400222, ''N2026000222'', NULL, ''NOTIFBANC'', ''Paul'');';

-- Emises judiciaires.
INSERT INTO v2.NOTIFICATION_JUD_XML (NTFJUDXML_ID_NTFJUDXML, NTFJUDXML_ID_PRCLL, NTFJUDXML_ID_DSRCS, NTFJUDXML_ID_NINSC, NTFJUDXML_CODE_GREFFE,
    NTFJUDXML_TYPE_NOTIFICATION, NTFJUDXML_ETAT_TRAITMENT, NTFJUDXML_DATE_GENERATION, NTFJUDXML_DATE_ENVOI, NTFJUDXML_DATE_RECEPTION,
    NTFJUDXML_ERREUR) VALUES
    -- erreur d'envoi, sur un dossier du registre : AFaire, la designation de l'entreprise
    (1400201, 1400211, 200402, NULL, '5602', 'PCL', '8', DATEADD(DAY, -3, @aujourdhui), NULL, NULL, 'Code greffe inconnu'),
    -- a envoyer, sur un non-inscrit personne morale : EnCours, sa denomination et son numero
    (1400202, 1400212, NULL, 1400221, '3402', 'PCL', '2', DATEADD(DAY, -5, @aujourdhui), NULL, NULL, NULL),
    -- refusee par le greffe, sur un non-inscrit personne physique (nom et prenoms), envoyee puis recue : AFaire
    (1400203, 1400213, NULL, 1400222, '3402', 'PCS', '5', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -9, @aujourdhui), NULL),
    -- ECARTEES : cloturee (10) -- jumeau retenu 1400201 (8) ; generee le jour moins 40 -- jumeau retenu 1400202
    (1400204, 1400211, 200402, NULL, '5602', 'PCL', '10', DATEADD(DAY, -3, @aujourdhui), NULL, NULL, NULL),
    (1400205, 1400212, NULL, 1400221, '3402', 'PCL', '2', DATEADD(DAY, -40, @aujourdhui), NULL, NULL, NULL);

-- Recues. La date filtree et l'ordre : la creation (« reception » dans la V2), croissante.
INSERT INTO v2.DEMAT_NOTIF (DMTNTF_ID_DMTNTF, DMTNTF_DATE_CREATION, DMTNTF_ETAT_TRAITEMENT, DMTNTF_DATE_RECEPTION_AT, DMTNTF_NUM_GESTION,
    DMTNTF_ID_DMND, DMTNTF_CODE_GREFFE_EMETTEUR, DMTNTF_TYPE_NOTIF) VALUES
    -- en attente, liee a une demande : le dossier de la demande, son numero de gestion l'emporte sur celui de la notification
    (1400301, DATEADD(HOUR, -40, @aujourdhui), '1', DATEADD(HOUR, -41, @aujourdhui), '1999B00001', 200421, '6901', 'RCS'),
    -- erreur d'envoi, sans demande : le numero de gestion de la notification
    (1400302, DATEADD(HOUR, -14, @aujourdhui), '8', NULL, '2025B00999', NULL, '3802', 'PCL'),
    -- en attente, creee le jour moins 30 a 0 h (borne basse retenue), sans rien d'autre
    (1400305, DATEADD(DAY, -30, @aujourdhui), '1', NULL, NULL, NULL, NULL, NULL),
    -- ECARTEES : traitee par le greffe (2) -- jumeau retenu 1400301 (1) ; creee le jour moins 31 a 23 h -- jumeau 1400305
    (1400303, DATEADD(HOUR, -40, @aujourdhui), '2', NULL, '2025B00303', NULL, '6901', 'RCS'),
    (1400304, DATEADD(HOUR, -721, @aujourdhui), '1', NULL, '2025B00304', NULL, '6901', 'RCS'),
    -- lot 6, pour la RECHERCHE : traitee par le greffe (2) le 15/03/2025, hors de l'ouverture
    (1606306, '20250315', '2', NULL, '2025B00306', NULL, '6901', 'RCS');
