-- Jeu de LecteurDocumentsOcerises (« Documents océrisés », PROC_NUM), lot 11 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19).
-- Documents 2110301-2110304. Ce qui est retenu ou ecarte, et pourquoi : PROC_NUM.attendu. La table aux types de RIG_DEV
-- (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 27/11/2026), colonnes lues ; aucun autre ecran ne la lit.
IF OBJECT_ID(N'v2.DOCUMENT_OCERISE') IS NULL
    CREATE TABLE v2.DOCUMENT_OCERISE (DOCOC_ID_DOCOC int NOT NULL PRIMARY KEY, DOCOC_NOM_FICHIER varchar(255) NULL,
        DOCOC_DATE_CREATION datetime NULL, DOCOC_ID_UTLST int NULL, DOCOC_NB_PAGES int NULL, DOCOC_TAILLE int NULL,
        DOCOC_NUM_JUD varchar(20) NULL, DOCOC_CODE_PROCESSUS varchar(40) NULL, DOCOC_ID_UTLST_EN_COURS int NULL,
        DOCOC_CODE_PROC_EN_COURS varchar(40) NULL, DOCOC_DATE_EN_COURS datetime NULL, DOCOC_IS_LIE_OCR_DIFFERE bit NULL);
INSERT INTO v2.DOCUMENT_OCERISE (DOCOC_ID_DOCOC, DOCOC_NOM_FICHIER, DOCOC_DATE_CREATION, DOCOC_NB_PAGES, DOCOC_TAILLE, DOCOC_NUM_JUD,
                                 DOCOC_ID_UTLST_EN_COURS, DOCOC_CODE_PROC_EN_COURS, DOCOC_DATE_EN_COURS, DOCOC_IS_LIE_OCR_DIFFERE) VALUES
    -- un PDF disponible, rattache a une affaire
    (2110301, 'courrier du banc 11.pdf', '20010502', 3, 2048, '2001J00011', NULL, NULL, NULL, 0),
    -- un TIF qu'un processus tient en cours
    (2110302, 'scan du banc 11.tif', '20010503', 1, 500, NULL, 7, 'NUM', '20010504', 0),
    -- lie a l'OCR differe (OPE_PIECENUM.cs:93-96) : ecarte -- jumeau 2110301
    (2110303, 'differe du banc 11.pdf', '20010504', 2, 1024, NULL, NULL, NULL, NULL, 1),
    -- un .TIFF sans date ni pages, sans drapeau d'OCR differe (NULL) : retenu, en dernier
    (2110304, 'ancien du banc 11.TIFF', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);
