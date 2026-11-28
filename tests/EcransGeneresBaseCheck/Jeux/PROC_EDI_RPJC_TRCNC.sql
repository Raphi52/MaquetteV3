-- Jeu de LecteurTraitementRpjc (« EDI RPJC », PROC_EDI_RPJC_TRCNC), lot 3 du plan « brancher sur la base les procedures
-- encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 1 300 1xx a 1 300 3xx (lot 3 : 1 300 000 a 1 399 999),
-- CNBF 1300xx et roles 2026F13xxx : rien ne croise les lignes des jeux PROC_EDI_RPJC_REN.sql (CNBF 3000xx, roles
-- 2026F3000x). Ce qui est retenu ou ecarte, et pourquoi : PROC_EDI_RPJC_TRCNC.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues. v2.MANDATAIRE et v2.INSTANCE existent deja dans le banc (PROC_EDI_RPJC_REN.sql, joue
-- avant) : MANDATAIRE recoit les deux colonnes qui lui manquent (nullables : la table a deja des lignes), et ses lignes
-- passent par sp_executesql -- une colonne ajoutee dans ce meme lot d'instructions n'existe pas encore quand SQL Server
-- le compile (meme raison que PROC_TRAITEMENTNPAI.sql).
IF OBJECT_ID(N'v2.DEMAT_RPJC_TRCNC') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_TRCNC (RPJCC_ID_RPJCC int NOT NULL PRIMARY KEY, RPJCC_ETAT varchar(2) NULL,
        RPJCC_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCC_DATE_TRAITEMENT_RIG datetime NULL, RPJCC_MOTIF_TRAITEMENT_RIG varchar(max) NULL,
        RPJCC_DATE_RECEPTION_EXTELIA_AI datetime NULL, RPJCC_DATE_TRANSMISSION datetime NULL, RPJCC_CNBF_TRANSMISSION varchar(6) NULL,
        RPJCC_NUMERO_AFFAIRE varchar(11) NULL, RPJCC_USER_EN_COURS varchar(255) NULL, RPJCC_TYPE_DOCUMENT varchar(20) NULL,
        RPJCC_NB_PIECES int NULL);
IF OBJECT_ID(N'v2.EXPERIAN_CORRESP_EDI') IS NULL
    CREATE TABLE v2.EXPERIAN_CORRESP_EDI (EXPCE_TYPE varchar(20) NOT NULL, EXPCE_CODE varchar(20) NOT NULL,
        EXPCE_LIBELLE varchar(100) NOT NULL, EXPCE_UTILISABLE_SAISIE bit NOT NULL);
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL,
        MNDTR_NUMERO_CNBF varchar(6) NULL);
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ABREGE varchar(16) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_CIVILITE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_CIVILITE varchar(7) NULL;

-- Les mandataires par CNBF : 130001 actif avec abrege (retenu) ; 130002 inactif et 130003 sans abrege (ecartes des listes
-- a traiter / en cours) ; 130004 en a deux actifs avec abrege, le plus recent est affiche (ORDER BY MNDTR_ID_MNDTR DESC).
EXEC sp_executesql N'
INSERT INTO v2.MANDATAIRE (MNDTR_ID_MNDTR, MNDTR_ABREGE, MNDTR_CIVILITE, MNDTR_NOM, MNDTR_ACTIF, MNDTR_NUMERO_CNBF) VALUES
    (1300201, ''ALP13'', ''Me'', ''ALPHA Irène'', 1, ''130001''),
    (1300202, ''BET13'', ''Me'', ''BETA Simon'', 0, ''130002''),
    (1300203, NULL, ''Me'', ''GAMMA Olga'', 1, ''130003''),
    (1300204, ''DEL13A'', ''Me'', ''ANCIEN DELTA'', 1, ''130004''),
    (1300205, ''DEL13B'', ''Mme'', ''DELTA Lina'', 1, ''130004'');';

-- Les affaires : deux instances au role 2026F13001 (une seule ligne, le nom de la premiere) ; le role 2026F13009 n'a pas
-- d'instance.
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT) VALUES
    (1300301, '2026F13001', 'DELTA SARL c/ EPSILON SAS'),
    (1300302, '2026F13001', 'NOM PLUS RECENT A NE PAS PRENDRE'),
    (1300303, '2026F13003', 'ZETA CONSEIL SAS');

-- Les types de document connus ; INC13 n'y est pas.
INSERT INTO v2.EXPERIAN_CORRESP_EDI (EXPCE_TYPE, EXPCE_CODE, EXPCE_LIBELLE, EXPCE_UTILISABLE_SAISIE) VALUES
    ('RPJCC', 'CCL13', 'Conclusions', 1),
    ('RPJCC', 'PIE13', 'Pièces', 1);

INSERT INTO v2.DEMAT_RPJC_TRCNC (RPJCC_ID_RPJCC, RPJCC_ETAT, RPJCC_ETAT_TRAITEMENT_RIG, RPJCC_DATE_TRAITEMENT_RIG,
    RPJCC_MOTIF_TRAITEMENT_RIG, RPJCC_DATE_RECEPTION_EXTELIA_AI, RPJCC_DATE_TRANSMISSION, RPJCC_CNBF_TRANSMISSION,
    RPJCC_NUMERO_AFFAIRE, RPJCC_USER_EN_COURS, RPJCC_TYPE_DOCUMENT, RPJCC_NB_PIECES) VALUES
    -- A TRAITER (etat 1, personne) : mandataire actif avec abrege, role a deux instances : une ligne, AFaire
    (1300101, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -3, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1),
    -- etat 1, seul mandataire INACTIF : ecarte -- jumeau 1300101 (actif)
    (1300102, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -3, @aujourdhui), '130002', '2026F13001', NULL, 'CCL13', 1),
    -- etat 1, mandataire SANS ABREGE : ecarte -- jumeau 1300101 (avec abrege)
    (1300103, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -3, @aujourdhui), '130003', '2026F13001', NULL, 'CCL13', 1),
    -- etat 1, role SANS INSTANCE : ecarte -- jumeau 1300101 (role a instance)
    (1300104, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -3, @aujourdhui), '130001', '2026F13009', NULL, 'CCL13', 1),
    -- etat 1, trois pieces, sans type : AFaire, le plus recent des deux mandataires du CNBF
    (1300105, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -1, @aujourdhui), '130004', '2026F13003', NULL, NULL, 3),
    -- EN COURS (etat 1, un agent dessus) : EnCours, visualisation et verrou
    (1300106, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), '130001', '2026F13003', 'agent.trcnc', 'PIE13', 1),
    -- EN COURS, tenue par l'agent du banc (« banc.ecrans ») : celle que « Annuler le traitement » rend aux affaires a traiter
    -- (PROC_EDI_RPJC_TRCNC.gestes, jumeau accepte de 1300106, tenue par un autre agent ; lot 2 du plan-f4fb097f)
    (1300107, '1', NULL, NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), '130001', '2026F13001', 'banc.ecrans', 'CCL13', 1),
    -- HISTORIQUE : acceptee, traitee au jour moins 15 jours a 0 h (borne basse de la V2, incluse) : Validee
    (1300108, '2', 'A', DATEADD(DAY, -15, @aujourdhui), NULL, NULL, DATEADD(DAY, -20, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1),
    -- acceptee, traitee la veille de la borne basse a 23 h 59 : ecartee -- jumeau 1300108 (a la borne)
    (1300109, '4', 'A', DATEADD(MINUTE, -1, DATEADD(DAY, -15, @aujourdhui)), NULL, NULL, DATEADD(DAY, -20, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1),
    -- refusee par le greffe, avec son motif ; CNBF sans mandataire actif (l'historique ne l'exige pas) : Validee, refusee
    (1300110, '2', 'R', DATEADD(DAY, -2, @aujourdhui), 'Pièce illisible', NULL, DATEADD(DAY, -4, @aujourdhui), '130002', '2026F13003', NULL, 'PIE13', 1),
    -- acceptee puis reponse rejetee par Extelia (etat 9) ; role sans instance (l'historique ne l'exige pas) : Validee + Rejet
    (1300111, '9', 'A', DATEADD(DAY, -5, @aujourdhui), NULL, DATEADD(DAY, -4, @aujourdhui), DATEADD(DAY, -6, @aujourdhui), '130001', '2026F13009', NULL, 'CCL13', 1),
    -- acceptee dans la periode, type de document INCONNU : ecartee (jointure de la V2) -- jumeau 1300108 (type connu)
    (1300112, '2', 'A', DATEADD(DAY, -1, @aujourdhui), NULL, NULL, DATEADD(DAY, -2, @aujourdhui), '130001', '2026F13001', NULL, 'INC13', 1),
    -- acceptee aujourd'hui a 23 h (borne haute de la V2, le jour a 24 h) : retenue
    (1300113, '4', 'A', DATEADD(HOUR, 23, @aujourdhui), NULL, NULL, DATEADD(DAY, -1, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1),
    -- acceptee demain a 0 h : ecartee -- jumeau 1300113
    (1300114, '4', 'A', DATEADD(DAY, 1, @aujourdhui), NULL, NULL, DATEADD(DAY, -1, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1),
    -- etat 2 SANS decision, dans la periode : ecartee (cases « Acceptés » et « Rejetés » : A ou R) -- jumeau 1300108
    (1300115, '2', NULL, DATEADD(DAY, -1, @aujourdhui), NULL, NULL, DATEADD(DAY, -2, @aujourdhui), '130001', '2026F13001', NULL, 'CCL13', 1);
