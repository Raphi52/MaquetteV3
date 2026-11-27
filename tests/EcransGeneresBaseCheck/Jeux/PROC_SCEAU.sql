-- Jeu de LecteurSceaux / EcrivainSceaux (« Sceaux », PROC_SCEAU), lot 15 du plan-676a4b19. Identifiants 25005xx, references « L15_... ».
-- Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches, .gestes.
-- ELEMENT_FUSION est creee par PROC_RECHERCHE_GED.sql (joue avant) avec trois colonnes et UN element (1600301, L6_LETTRE_RAPPEL, dont le
-- document L6-GED-02 est dans GED_RIG) : il entre dans cette liste. Ce jeu ajoute les colonnes lues, aux types et defauts de RIG_DEV
-- (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le 27/11/2026 : HAUTEUR 45, LARGEUR 50, ROGNER 0) ; ELMFS_VERSION (NOT NULL sans defaut dans
-- RIG_DEV) recoit ' ', la version active que Valider ecrit (OPE_EDITION.cs:99). Un seul element du jeu a son image dans GED_RIG,
-- L15_TAMPON (L15-TAMPON-01) : Modifier n'enregistre qu'un sceau dont l'image est en GED (« Le fichier est obligatoire »,
-- OPE_EDITION.cs:163-166 ; jumeau refuse L15_SCEAU). Son document entre aussi dans « Voir une image » et « Documents »
-- (PROC_IMAGE.attendu, PROC_RECHERCHE_GED.attendu). Il porte l'extension « png » de son fichier, sans chemin virtuel, application ni
-- texte de recherche : Valider les reecrit (OPE_EDITION.cs:103-109), ce que PROC_SCEAU.gestes controle. GED_RIG est creee par
-- PROC_IMAGE.sql sans les trois colonnes que Valider ecrit en plus : ajoutees ici, aux types de la base V3 de dev (INFORMATION_SCHEMA,
-- lu le 27/11/2026).
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
IF COL_LENGTH('v2.GED_RIG', 'GDRG_CHAINE_RECHERCHEE') IS NULL ALTER TABLE v2.GED_RIG ADD GDRG_CHAINE_RECHERCHEE varchar(max) NULL;
IF COL_LENGTH('v2.GED_RIG', 'GDRG_APPLICATION_ARCHIVAGE') IS NULL ALTER TABLE v2.GED_RIG ADD GDRG_APPLICATION_ARCHIVAGE varchar(35) NULL;
IF COL_LENGTH('v2.GED_RIG', 'GDRG_PATH_VIRTUEL') IS NULL ALTER TABLE v2.GED_RIG ADD GDRG_PATH_VIRTUEL varchar(2000) NULL;
-- Les colonnes ajoutees ci-dessus ne sont connues qu'a l'execution : l'insertion passe par sp_executesql (comme PROC_GESTTIERSPAYEURS.sql).
EXEC sp_executesql N'
INSERT INTO v2.ELEMENT_FUSION (ELMFS_ID_ELMFS, ELMFS_REFERENCE, ELMFS_NUM_DOCUMENT, ELMFS_HAUTEUR_MAX_MM, ELMFS_LARGEUR_MAX_MM, ELMFS_ROGNER_MM,
    ELMFS_VERSION, ELMFS_PAR_DEFAUT) VALUES
    -- 24 mm (« dimensions sceau lot 1 »), rogne de 2 mm, document absent de la GED : « Image absente de la GED »
    (2500501, ''L15_SCEAU'', ''L15-SCEAU-01'', 24, 41, 2, '' '', 1),
    -- autre hauteur : groupe « Autre hauteur » -- jumeau 2500501 (24 mm)
    (2500502, ''L15_LOGO'', ''L15-LOGO-01'', 273, 998, 0, '' '', 1),
    -- sans numero de document : « À compléter » (« Le fichier est obligatoire ») -- jumeau 2500501 (un numero)
    (2500503, ''L15_SIGNATURE'', '''', 24, 45, 0, '' '', 1),
    -- son image dans la GED : « Enregistré » -- jumeau 2500501 (absente) ; modifiable (PROC_SCEAU.gestes)
    (2500504, ''L15_TAMPON'', ''L15-TAMPON-01'', 24, 30, 0, '' '', 1);
INSERT INTO v2.GED_RIG (GDRG_NUM_DOCUMENT, GDRG_EXTENSION, GDRG_FICHIER, GDRG_LOGIN_NT_ARCHIVAGE, GDRG_DATE_ARCHIVAGE, GDRG_REFERENCE,
    GDRG_NOM_FICHIER, GDRG_CHAINE_RECHERCHEE, GDRG_APPLICATION_ARCHIVAGE, GDRG_PATH_VIRTUEL) VALUES
    (''L15-TAMPON-01'', ''png'', 0x89504E47, ''banc'', ''20250315'', ''ELEMENT_FUSION.ELMFS_NUM_DOCUMENT'', ''tampon.png'', NULL, NULL, NULL);';
