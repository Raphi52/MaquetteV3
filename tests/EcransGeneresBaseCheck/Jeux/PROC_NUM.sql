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
-- Pour les gestes (PROC_NUM.gestes, EcrivainDocumentsOcerises) : un document en cours de numerisation (2110305, son nom n'est pas
-- enregistre : OPE_PIECENUM_1.cs:352-354), les tags OCR de 2110301 (supprimes avec lui, DocumentOcerise.cs:384-390) et 2110302 que
-- l'OCR utilise (VIDIGED_INDEX_TMP_OCR, sa suppression est refusee : OPE_PIECENUM_1.cs:270-273). Tables aux types de RIG_DEV.
INSERT INTO v2.DOCUMENT_OCERISE (DOCOC_ID_DOCOC, DOCOC_NOM_FICHIER, DOCOC_DATE_CREATION, DOCOC_NB_PAGES, DOCOC_TAILLE, DOCOC_IS_LIE_OCR_DIFFERE) VALUES
    (2110305, 'en cours de numérisation', '20010501', NULL, NULL, 0);
IF OBJECT_ID(N'v2.DOCUMENT_OCERISE_TAGS') IS NULL
    CREATE TABLE v2.DOCUMENT_OCERISE_TAGS (DOCOT_ID_DOCOT int NOT NULL PRIMARY KEY, DOCOT_ID_DOCOC int NULL,
        DOCOT_DONNEES_OCERISEES varchar(max) NULL, DOCOT_NUM_PAGE int NULL, DOCOT_TYPE varchar(50) NULL);
INSERT INTO v2.DOCUMENT_OCERISE_TAGS (DOCOT_ID_DOCOT, DOCOT_ID_DOCOC, DOCOT_DONNEES_OCERISEES, DOCOT_NUM_PAGE, DOCOT_TYPE) VALUES
    (2110311, 2110301, 'texte de la page 1', 1, 'TEXTE'), (2110312, 2110301, 'texte de la page 2', 2, 'TEXTE'),
    (2110313, 2110302, 'texte du scan', 1, 'TEXTE');
IF OBJECT_ID(N'v2.VIDIGED_INDEX_TMP_OCR') IS NULL
    CREATE TABLE v2.VIDIGED_INDEX_TMP_OCR (VITOC_ID_VITOC int NOT NULL PRIMARY KEY, VITOC_NUM_DOCUMENT_GED_RIG varchar(50) NULL,
        VITOC_ID_DOC_OCR int NULL, VITOC_LIBELLE varchar(255) NULL, VITOC_OCR bit NULL, VITOC_DATE_CREATION datetime NULL,
        VITOC_DATE_TRAITEMENT datetime NULL);
INSERT INTO v2.VIDIGED_INDEX_TMP_OCR (VITOC_ID_VITOC, VITOC_ID_DOC_OCR, VITOC_LIBELLE, VITOC_OCR, VITOC_DATE_CREATION) VALUES
    (2110321, 2110302, 'scan du banc 11', 1, '20010504');
