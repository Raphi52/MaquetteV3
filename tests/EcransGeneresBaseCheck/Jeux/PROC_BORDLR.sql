-- Jeu de LecteurBordereauxLr (« Bordereaux LR », PROC_BORDLR), lot 2 du plan « brancher sur la base les procedures
-- encore en exemple » (plan-f7ba4de2). Identifiants 12003xx. Ce qui est retenu ou ecarte, et pourquoi : PROC_BORDLR.attendu.
-- Aucune ligne n'entre dans un autre ecran branche : code processus BORDLR (et un voisin, BORDLRAR), sans dossier, et
-- AUCUNE ligne dans v2.DEMANDE_EDITION : elle entrerait dans PROC_POOLEDITION (PROC_POOLEDITION.sql l. 5-6). Ce jeu ne
-- couvre donc que les demandes sans edition ; le lecteur lit l'edition par une jointure, et v2.DEMANDE_EDITION est creee
-- par PROC_POOLEDITION.sql (joue avant toute lecture) : ce jeu ne la cree pas, pour ne pas lui imposer ses colonnes.
-- v2.DEMANDE existe deja dans le banc, sans DMND_NOM_UTILISATEUR : elle lui est ajoutee, au type de RIG_DEV
-- (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026). Les lignes passent par sp_executesql : une colonne
-- ajoutee dans ce meme lot d'instructions n'existe pas encore quand SQL Server le compile.
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;

EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION,
                        DMND_DATE_VALIDATION, DMND_NOM_UTILISATEUR) VALUES
    -- validee sans edition : en cours, la periode ; utilisateur ecrit « poste/utilisateur » comme la V2
    (1200301, ''26001200301'', ''V'', ''BORDLR'', DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), ''RIGAPP19/GESA''),
    -- qualifiee (Q), pas encore validee : a preparer ; « poste/utilisateur/MOTEUR » comme la V2 sous le moteur
    (1200302, ''26001200302'', ''Q'', ''BORDLR'', DATEADD(DAY, -1, @aujourdhui), NULL, ''RIGAPP2/lesniewski/MOTEUR''),
    -- interrompue (I) : a preparer ; sans utilisateur
    (1200303, ''26001200303'', ''I'', ''BORDLR'', DATEADD(DAY, -10, @aujourdhui), NULL, NULL),
    -- supprimee (X) : ecartee -- jumelle retenue 1200302
    (1200304, ''26001200304'', ''X'', ''BORDLR'', DATEADD(DAY, -2, @aujourdhui), NULL, ''RIGAPP2/lesniewski/MOTEUR''),
    -- code voisin BORDLRAR, validee : ecartee (egalite stricte du code) -- jumelle retenue 1200301
    (1200305, ''26001200305'', ''V'', ''BORDLRAR'', DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), ''RIGAPP19/GESA''),
    -- validee sans date de validation : la date de creation, pour la date affichee et pour l''ordre ; utilisateur d''un
    -- seul morceau : tel quel
    (1200306, ''26001200306'', ''V'', ''BORDLR'', DATEADD(DAY, -20, @aujourdhui), NULL, ''GPOURADIER''),
    -- validee le meme jour que 1200301 : la demande la plus recente (plus grand numero) d''abord
    (1200307, ''26001200307'', ''V'', ''BORDLR'', DATEADD(DAY, -4, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), ''RIGAPP19/GESA'');',
    N'@aujourdhui datetime', @aujourdhui;

-- LOT 11 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19) : les PLIS que le bordereau a editer compte, et les reglages
-- d'edition BORD_LRAR / BORD_LR. Identifiants 2110xxx, plis 1A21100xx, a des dates FIXES (le 15/03/2001) : les criteres d'un
-- .recherches s'ecrivent en dates, pas en {J-n}. Les tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le
-- 27/11/2026) ; v2.DEMANDE_EDITION et v2.LRAR_INFOS sont creees comme dans PROC_GCONV.sql si ce jeu passe le premier, et recoivent
-- les colonnes lues ou ecrites ici. Ces plis sont des demandes d'edition : ils entrent aussi dans la « File des editions »
-- (PROC_POOLEDITION.attendu, sans date de creation, par numero decroissant).
IF OBJECT_ID(N'v2.DEMANDE_EDITION') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_ID_DMND int NULL, DMNED_ID_DSRCS int NULL,
        DMNED_LOT varchar(255) NULL, DMNED_PHASE_TRAITEMENT varchar(10) NOT NULL, DMNED_DATE_PRE_EMISSION datetime NULL,
        DMNED_DESIGNATION_DESTINATAIRE varchar(255) NULL, DMNED_LIBELLE_DOCUMENT varchar(255) NOT NULL,
        DMNED_LIBELLE_ERREUR varchar(255) NULL, DMNED_DATE_CREATION datetime NULL, DMNED_ID_EMAIL_ENVOYE int NULL,
        DMNED_MODE_ENVOI int NULL);
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_NUM_LRAR') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_NUM_LRAR varchar(13) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_UTILISATEUR varchar(32) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_NUM_GROUPE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_NUM_GROUPE varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_PARAMETRE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_PARAMETRE_GENERATION varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_TYPE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_TYPE_GENERATION varchar(32) NULL;
IF OBJECT_ID(N'v2.LRAR_INFOS') IS NULL
    CREATE TABLE v2.LRAR_INFOS (LRARI_NUM_LRAR varchar(13) NOT NULL PRIMARY KEY, LRARI_DATE_RECEPTION datetime NULL,
        LRARI_TYPE varchar(15) NOT NULL, LRARI_TYPE_DATE varchar(20) NULL, LRARI_NUM_LRAR_RENVOYE varchar(13) NULL);
IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
    CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
        PRMED_RECTO_VERSO bit NOT NULL DEFAULT 0, PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL DEFAULT 0, PRMED_CODE_CFECP varchar(50) NULL,
        PRMED_LISTE varchar(4000) NOT NULL DEFAULT '', PRMED_CODE_ENTITE_SOURCE varchar(16) NULL, PRMED_CODE_PROCESSUS varchar(40) NULL,
        PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NOT NULL DEFAULT 1,
        PRMED_READ_ONLY bit NOT NULL DEFAULT 0, PRMED_NUM_GROUPE_IMPRESSION varchar(30) NULL, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC varchar(50) NULL);

EXEC sp_executesql N'
-- Les deux reglages de RIG_DEV (PARAMETRE_EDITION, lus le 27/11/2026), leur liste reduite aux attributs qui ne sont pas le SQL.
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_LISTE, PRMED_CODE_MAQUETTE, PRMED_TYPE_GENERATION)
SELECT v.Code, v.Libelle, v.Liste, ''BORDLRAR_LOT2'', ''FUSION_CRYSTAL'' FROM (VALUES
    (''BORD_LRAR'', ''BORDEREAU DE LETTRES RECOMMANDEES AVEC AR'',
     ''<PARAMETRES TYPE_GENERATION="FUSION_CRYSTAL" LIBELLE_DOCUMENT="bordereau LRAR" CODE_MAQUETTE="BORDLRAR_LOT2" SQL="SELECT 1" />''),
    (''BORD_LR'', ''BORDEREAU DE LETTRES RECOMMANDEES SANS AR'',
     ''<PARAMETRES TYPE_GENERATION="FUSION_CRYSTAL" LIBELLE_DOCUMENT="bordereau LR" CODE_MAQUETTE="BORDLRAR_LOT2" SQL="SELECT 1" />''))
    AS v (Code, Libelle, Liste)
WHERE NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION AS p WHERE p.PRMED_CODE = v.Code);
INSERT INTO v2.LRAR_INFOS (LRARI_NUM_LRAR, LRARI_TYPE, LRARI_TYPE_DATE, LRARI_DATE_RECEPTION) VALUES
    (''1A2110001'', ''LRAR'', NULL, NULL), (''1A2110002'', ''LRAR'', NULL, NULL), (''1A2110003'', ''LR'', NULL, NULL),
    (''1A2110004'', ''LRAR'', NULL, NULL), (''1A2110005'', ''CIPREMIUM_LRAR'', NULL, NULL), (''1A2110006'', ''LRAR'', NULL, NULL);
INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_DATE_PRE_EMISSION, DMNED_NUM_LRAR,
                                DMNED_UTILISATEUR) VALUES
    -- le 15/03/2001, LRAR, GESA : 10 h et 16 h ; sans AR (LR) a 11 h ; un autre utilisateur a 9 h
    (2110001, ''E'', ''Convocation du banc 11'', ''20010315 10:00'', ''1A2110001'', ''GESA''),
    (2110002, ''E'', ''Convocation du banc 11'', ''20010315 16:00'', ''1A2110002'', ''GESA''),
    (2110003, ''E'', ''Lettre simple du banc 11'', ''20010315 11:00'', ''1A2110003'', ''GESA''),
    (2110004, ''E'', ''Convocation du banc 11'', ''20010315 09:00'', ''1A2110004'', ''LESNIEWSKI''),
    -- lettre electronique (CIPREMIUM_LRAR) le meme jour : hors des deux types -- jumeau compte 2110001
    (2110005, ''E'', ''Lettre electronique du banc 11'', ''20010315 12:00'', ''1A2110005'', ''GESA''),
    -- le lendemain a 0 h : hors de la journee du 15 (borne 23:59:59.998) -- jumeau compte 2110002 (16 h le 15)
    (2110006, ''E'', ''Convocation du banc 11'', ''20010316 00:00'', ''1A2110006'', ''GESA'');';
