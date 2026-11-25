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
