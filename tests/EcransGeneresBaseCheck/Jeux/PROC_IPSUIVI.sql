-- Jeu de LecteurSuiviIp (« Suivi des preventions », PROC_IPSUIVI : le suivi d'UNE injonction de payer), lot 8 du plan « RIG v3
-- 100 % fonctionnel ». Identifiants 18015xx (lot 8 : a partir de 1800000). La liste montre TOUTES les instances de type IP de la
-- base (la V2 ouvre n'importe quelle IP par son numero) : celles de PROC_SURVIP.sql et des autres jeux entrent aussi a
-- PROC_IPSUIVI.attendu. Ce jeu passe avant PROC_SURVIP.sql (ordre des noms) : il cree ou complete lui-meme les tables qu'il lit,
-- aux types de RIG_DEV, et ses lignes passent par sp_executesql (une colonne ajoutee ici n'existe pas encore a la compilation).
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
-- Trois IP : rien de signifie (1801501), signifiee (1801502), close par un desistement (1801503) ; une instance J (1801504),
-- ecartee : la V2 n''ouvre qu''une IP (OPE_IPSUIVI.cs:135-143).
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TYPE_INSTANCE, INSTN_IP_EUROPEENNE) VALUES
    (1801501, ''2026IP81501'', N''SUIVI IP OMEGA'', ''IP'', 0), (1801502, ''2026IP81502'', N''SUIVI IP SIGMA'', ''IP'', 0),
    (1801503, ''2026IP81503'', N''SUIVI IP TAU'', ''IP'', 0), (1801504, ''2026J81504'', N''SUIVI IP AU FOND'', ''J'', 0);
-- 1801501 : ordonnance prevue (sans date rendue) ; 1801502 : rendue il y a 50 jours.
INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_NUM_ORDRE, DCJDC_DATE_PREVUE, DCJDC_DATE) VALUES
    (1801551, 1801501, 1, DATEADD(DAY, -7, @aujourdhui), NULL), (1801552, 1801502, 1, NULL, DATEADD(DAY, -50, @aujourdhui));
-- Pas de partie ici : celles de 1801401 (PROC_SURVIP.sql) montrent « demandeur c/ defendeur » ; une partie de plus entrerait
-- aussi a l''ecran « Parties » (PROC_PARTIES lit toutes les parties).
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_INSTN, EVNPR_NUM_ORDRE, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU,
    EVNPR_DELAI_EXPIRE, EVNPR_DATE_MAXI, EVNPR_SIGNIF_A_PERSONNE) VALUES
    -- 1801501 : signification seulement ATTENDUE (future) : rien de signifie
    (1801571, 1801501, 1, ''IPSIGNIF'', NULL, 0, 0, DATEADD(DAY, 20, @aujourdhui), NULL),
    -- 1801502 : signifiee a personne il y a 40 jours ; l''opposition attendue a EXPIRE (sa date maxi s''affiche) ; provision recue
    (1801572, 1801502, 1, ''IPSIGNIF'', DATEADD(DAY, -40, @aujourdhui), 1, 0, NULL, 1),
    (1801573, 1801502, 2, ''IPOPOS'', NULL, 0, 1, DATEADD(DAY, -10, @aujourdhui), NULL),
    (1801574, 1801502, 3, ''IPCONSOPOS'', DATEADD(DAY, -8, @aujourdhui), 1, 0, NULL, NULL),
    -- 1801503 : signifiee, puis desistement survenu : close
    (1801575, 1801503, 1, ''ISIGNI'', DATEADD(DAY, -90, @aujourdhui), 1, 0, NULL, 0),
    (1801576, 1801503, 2, ''IPDESIN'', DATEADD(DAY, -30, @aujourdhui), 1, 0, NULL, NULL);
',
    N'@aujourdhui datetime', @aujourdhui;

-- LE DESISTEMENT (EcrivainSuiviIp, « Valider », lot 8, 27/11/2026) : cinq IP de plus (18016xx), une par regle de Remplir_IP
-- (OPE_IPSUIVI.cs:976-1055). Leur ordonnance porte un type de decision : IPACC18 accepte, IPREJ18 rejete (R_TYPEDECISI_ACCEPTE).
-- Aucune partie (elles entreraient a l'ecran « Parties ») ; aucune n'a d'evenement futur echu (elles n'entrent pas a PROC_SURVIP).
--   1801601 : ordonnance rendue et acceptee, une signification attendue dans 20 jours : le desistement est offert -- jumeau de tous ;
--   1801602 : la meme, mais IP europeenne : refusee ;
--   1801603 : ordonnance de type rejete : « IP rejetée » ;
--   1801604 : ordonnance acceptee, sans aucun evenement futur : la case n'est pas offerte ;
--   1801605 : ordonnance acceptee prevue, SANS date rendue : seul le desistement est offert (meme sans evenement futur).
-- 1801503 (deja desistee, plus haut) et 1801501 (ordonnance sans type : IsAccepter faux) sont refusees aussi.
IF COL_LENGTH('v2.INSTANCE', 'INSTN_NOTE_AUDIENCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NOTE_AUDIENCE varchar(max) NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_AFFRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_AFFRE int NULL;
IF OBJECT_ID(N'v2.TYPE_DECISION') IS NULL
    CREATE TABLE v2.TYPE_DECISION (R_TYPEDECISI_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_TYPEDECISI_LIBELLE_ECRAN varchar(100) NOT NULL);
IF COL_LENGTH('v2.TYPE_DECISION', 'R_TYPEDECISI_ACCEPTE') IS NULL ALTER TABLE v2.TYPE_DECISION ADD R_TYPEDECISI_ACCEPTE bit NULL;

EXEC sp_executesql N'
INSERT INTO v2.TYPE_DECISION (R_TYPEDECISI_VALEUR, R_TYPEDECISI_LIBELLE_ECRAN, R_TYPEDECISI_ACCEPTE) VALUES
    (''IPACC18'', ''Ordonnance IP acceptee lot 8'', 1), (''IPREJ18'', ''Ordonnance IP rejetee lot 8'', 0);
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TYPE_INSTANCE, INSTN_IP_EUROPEENNE, INSTN_NOTE_AUDIENCE) VALUES
    (1801601, ''2026IP81601'', N''DESISTER OK'', ''IP'', 0, N''Relance faite''), (1801602, ''2026IP81602'', N''DESISTER EUROPE'', ''IP'', 1, NULL),
    (1801603, ''2026IP81603'', N''DESISTER REJETEE'', ''IP'', 0, NULL), (1801604, ''2026IP81604'', N''DESISTER SANS FUTUR'', ''IP'', 0, NULL),
    (1801605, ''2026IP81605'', N''DESISTER NON SIGNEE'', ''IP'', 0, NULL);
INSERT INTO v2.DECISION_JUDICIAIRE (DCJDC_ID_DCJDC, DCJDC_ID_INSTN, DCJDC_NUM_ORDRE, DCJDC_DATE_PREVUE, DCJDC_DATE, DCJDC_TYPE_DECISION) VALUES
    (1801651, 1801601, 1, NULL, DATEADD(DAY, -20, @aujourdhui), ''IPACC18''), (1801652, 1801602, 1, NULL, DATEADD(DAY, -20, @aujourdhui), ''IPACC18''),
    (1801653, 1801603, 1, NULL, DATEADD(DAY, -20, @aujourdhui), ''IPREJ18''), (1801654, 1801604, 1, NULL, DATEADD(DAY, -20, @aujourdhui), ''IPACC18''),
    (1801655, 1801605, 1, DATEADD(DAY, -5, @aujourdhui), NULL, ''IPACC18'');
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_INSTN, EVNPR_NUM_ORDRE, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU,
    EVNPR_DELAI_EXPIRE, EVNPR_DATE_MAXI, EVNPR_SIGNIF_A_PERSONNE) VALUES
    (1801671, 1801601, 1, ''IPSIGNIF'', NULL, 0, 0, DATEADD(DAY, 20, @aujourdhui), NULL),
    (1801672, 1801602, 1, ''IPSIGNIF'', NULL, 0, 0, DATEADD(DAY, 20, @aujourdhui), NULL),
    (1801673, 1801603, 1, ''IPSIGNIF'', NULL, 0, 0, DATEADD(DAY, 20, @aujourdhui), NULL);
',
    N'@aujourdhui datetime', @aujourdhui;
