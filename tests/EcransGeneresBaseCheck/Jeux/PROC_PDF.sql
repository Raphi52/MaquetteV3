-- Jeu de LecteurPiecesPdf (« Voir un PDF », PROC_PDF), lot 6 du plan-676a4b19 : les PDF de la GED du greffe (GED_RIG).
-- Ce qui est retenu ou ecarte, et pourquoi : PROC_PDF.attendu. La table est creee par PROC_IMAGE.sql, joue avant.
IF OBJECT_ID(N'v2.GED_RIG') IS NULL
    CREATE TABLE v2.GED_RIG (GDRG_NUM_DOCUMENT varchar(40) NOT NULL PRIMARY KEY, GDRG_EXTENSION varchar(10) NOT NULL,
        GDRG_FICHIER varbinary(max) NULL, GDRG_FICHIER_PDF varbinary(max) NULL, GDRG_LOGIN_NT_ARCHIVAGE varchar(35) NOT NULL,
        GDRG_DATE_ARCHIVAGE datetime NOT NULL, GDRG_LOGIN_NT_EXTRACTION varchar(35) NULL, GDRG_DATE_EXTRACTION datetime NULL,
        GDRG_PATH_EXTRACTION varchar(2000) NULL, GDRG_REFERENCE varchar(200) NULL, GDRG_NOM_FICHIER varchar(160) NULL,
        GDRG_NB_PAGE int NULL);

INSERT INTO v2.GED_RIG (GDRG_NUM_DOCUMENT, GDRG_EXTENSION, GDRG_FICHIER, GDRG_FICHIER_PDF, GDRG_LOGIN_NT_ARCHIVAGE, GDRG_DATE_ARCHIVAGE,
    GDRG_LOGIN_NT_EXTRACTION, GDRG_DATE_EXTRACTION, GDRG_NOM_FICHIER, GDRG_NB_PAGE) VALUES
    -- un PDF de 12 pages, archive en dernier : en tete
    ('L6-PDF-01', 'pdf', 0x25504446, NULL, 'banc', '20250320', NULL, NULL, 'jugement-annexe.pdf', 12),
    -- un PDF dont seule la version PDF est en GED (GDRG_FICHIER vide) : Disponible
    ('L6-PDF-02', 'PDF', NULL, 0x25504446, 'banc', '20250318', NULL, NULL, 'statuts.pdf', 8),
    -- un PDF sans aucun octet : Fichier absent
    ('L6-PDF-03', 'pdf ', NULL, NULL, 'banc', '20250317', NULL, NULL, 'pv-assemblee.pdf', NULL);
