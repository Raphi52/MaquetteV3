-- Jeu de LecteurImagesGed (« Voir une image », PROC_IMAGE), lot 6 du plan-676a4b19 : les images de la GED du greffe (GED_RIG).
-- Ce qui est retenu ou ecarte, et pourquoi : PROC_IMAGE.attendu. Les documents de PROC_PDF.sql et PROC_RECHERCHE_GED.sql sont dans
-- la meme table : ils n'entrent pas ici (autres extensions). Numeros de document « L6-IMG-... ».
-- Types : ceux de RIG_DEV (SSDT, RigDatabase\dbo\Tables\GED_RIG.sql ; GDRG_FICHIER y est un IMAGE, varbinary(max) ici).
IF OBJECT_ID(N'v2.GED_RIG') IS NULL
    CREATE TABLE v2.GED_RIG (GDRG_NUM_DOCUMENT varchar(40) NOT NULL PRIMARY KEY, GDRG_EXTENSION varchar(10) NOT NULL,
        GDRG_FICHIER varbinary(max) NULL, GDRG_FICHIER_PDF varbinary(max) NULL, GDRG_LOGIN_NT_ARCHIVAGE varchar(35) NOT NULL,
        GDRG_DATE_ARCHIVAGE datetime NOT NULL, GDRG_LOGIN_NT_EXTRACTION varchar(35) NULL, GDRG_DATE_EXTRACTION datetime NULL,
        GDRG_PATH_EXTRACTION varchar(2000) NULL, GDRG_REFERENCE varchar(200) NULL, GDRG_NOM_FICHIER varchar(160) NULL,
        GDRG_NB_PAGE int NULL);

INSERT INTO v2.GED_RIG (GDRG_NUM_DOCUMENT, GDRG_EXTENSION, GDRG_FICHIER, GDRG_FICHIER_PDF, GDRG_LOGIN_NT_ARCHIVAGE, GDRG_DATE_ARCHIVAGE,
    GDRG_LOGIN_NT_EXTRACTION, GDRG_DATE_EXTRACTION, GDRG_NOM_FICHIER, GDRG_NB_PAGE) VALUES
    -- un TIFF de 3 pages, 2 048 octets : Disponible
    ('L6-IMG-01', 'TIF', CAST(REPLICATE(CAST('x' AS varchar(max)), 2048) AS varbinary(max)), NULL, 'banc', '20250310', NULL, NULL, 'bilan-numerise.tif', 3),
    -- une image PNG extraite par un agent : Extrait
    ('L6-IMG-02', 'png', 0x89504E47, NULL, 'banc', '20250311', 'agent.banc', '20250312', 'courrier-scanne.png', NULL),
    -- une image sans aucun octet dans la GED : Fichier absent
    ('L6-IMG-03', 'JPEG', NULL, NULL, 'banc', '20250309', NULL, NULL, NULL, 1),
    -- ECARTE : un document d'une autre extension (doc) -- jumeau retenu L6-IMG-02 (png)
    ('L6-IMG-04', 'doc', 0x01, NULL, 'banc', '20250313', NULL, NULL, 'lettre.doc', NULL);
