-- Jeu de LecteurSceaux / EcrivainSceaux (« Sceaux », PROC_SCEAU), lot 15 du plan-676a4b19. Identifiants 25005xx, references « L15_... ».
-- Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches, .gestes.
-- ELEMENT_FUSION est creee par PROC_RECHERCHE_GED.sql (joue avant) avec trois colonnes et UN element (1600301, L6_LETTRE_RAPPEL, dont le
-- document L6-GED-02 est dans GED_RIG) : il entre dans cette liste. Ce jeu ajoute les colonnes lues, aux types et defauts de RIG_DEV
-- (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le 27/11/2026 : HAUTEUR 45, LARGEUR 50, ROGNER 0) ; ELMFS_VERSION (NOT NULL sans defaut dans
-- RIG_DEV) recoit ' ', la version active que Valider ecrit (OPE_EDITION.cs:99). Les elements du jeu n'ont AUCUN document dans GED_RIG :
-- un document de plus entrerait dans « Voir une image » et « Rechercher en GED » (PROC_IMAGE, PROC_RECHERCHE_GED).
IF OBJECT_ID(N'v2.ELEMENT_FUSION') IS NULL
    CREATE TABLE v2.ELEMENT_FUSION (ELMFS_ID_ELMFS int NOT NULL PRIMARY KEY, ELMFS_REFERENCE varchar(32) NOT NULL, ELMFS_NUM_DOCUMENT varchar(40) NOT NULL);
IF COL_LENGTH('v2.ELEMENT_FUSION', 'ELMFS_HAUTEUR_MAX_MM') IS NULL
    ALTER TABLE v2.ELEMENT_FUSION ADD ELMFS_HAUTEUR_MAX_MM int NOT NULL CONSTRAINT DF_L15_ELMFS_HAUTEUR DEFAULT (45);
IF COL_LENGTH('v2.ELEMENT_FUSION', 'ELMFS_LARGEUR_MAX_MM') IS NULL
    ALTER TABLE v2.ELEMENT_FUSION ADD ELMFS_LARGEUR_MAX_MM int NOT NULL CONSTRAINT DF_L15_ELMFS_LARGEUR DEFAULT (50);
IF COL_LENGTH('v2.ELEMENT_FUSION', 'ELMFS_ROGNER_MM') IS NULL
    ALTER TABLE v2.ELEMENT_FUSION ADD ELMFS_ROGNER_MM int NOT NULL CONSTRAINT DF_L15_ELMFS_ROGNER DEFAULT (0);
IF COL_LENGTH('v2.ELEMENT_FUSION', 'ELMFS_VERSION') IS NULL
    ALTER TABLE v2.ELEMENT_FUSION ADD ELMFS_VERSION varchar(32) NOT NULL CONSTRAINT DF_L15_ELMFS_VERSION DEFAULT (' ');
IF COL_LENGTH('v2.ELEMENT_FUSION', 'ELMFS_PAR_DEFAUT') IS NULL
    ALTER TABLE v2.ELEMENT_FUSION ADD ELMFS_PAR_DEFAUT bit NULL;
IF OBJECT_ID(N'v2.GED_RIG') IS NULL
    CREATE TABLE v2.GED_RIG (GDRG_NUM_DOCUMENT varchar(40) NOT NULL PRIMARY KEY, GDRG_EXTENSION varchar(10) NOT NULL,
        GDRG_FICHIER varbinary(max) NULL, GDRG_FICHIER_PDF varbinary(max) NULL, GDRG_LOGIN_NT_ARCHIVAGE varchar(35) NOT NULL,
        GDRG_DATE_ARCHIVAGE datetime NOT NULL, GDRG_LOGIN_NT_EXTRACTION varchar(35) NULL, GDRG_DATE_EXTRACTION datetime NULL,
        GDRG_PATH_EXTRACTION varchar(2000) NULL, GDRG_REFERENCE varchar(200) NULL, GDRG_NOM_FICHIER varchar(160) NULL,
        GDRG_NB_PAGE int NULL);
-- Les colonnes ajoutees ci-dessus ne sont connues qu'a l'execution : l'insertion passe par sp_executesql (comme PROC_GESTTIERSPAYEURS.sql).
EXEC sp_executesql N'
INSERT INTO v2.ELEMENT_FUSION (ELMFS_ID_ELMFS, ELMFS_REFERENCE, ELMFS_NUM_DOCUMENT, ELMFS_HAUTEUR_MAX_MM, ELMFS_LARGEUR_MAX_MM, ELMFS_ROGNER_MM,
    ELMFS_VERSION, ELMFS_PAR_DEFAUT) VALUES
    -- 24 mm (« dimensions sceau lot 1 »), rogne de 2 mm, document absent de la GED : « Image absente de la GED »
    (2500501, ''L15_SCEAU'', ''L15-SCEAU-01'', 24, 41, 2, '' '', 1),
    -- autre hauteur : groupe « Autre hauteur » -- jumeau 2500501 (24 mm)
    (2500502, ''L15_LOGO'', ''L15-LOGO-01'', 273, 998, 0, '' '', 1),
    -- sans numero de document : « À compléter » (« Le fichier est obligatoire ») -- jumeau 2500501 (un numero)
    (2500503, ''L15_SIGNATURE'', '''', 24, 45, 0, '' '', 1);';
