-- Jeu de LecteurReversementsInpi et EcrivainReversementsInpi (« Reversements INPI », PROC_INPIPAY), lot 12 du plan-676a4b19.
-- Identifiants 22060xx. Types : RIG_DEV (SSDT RigDatabase\dbo\Tables\INPI_REVERSEMENTS.sql), colonnes lues et ecrites.
IF OBJECT_ID(N'v2.INPI_REVERSEMENTS') IS NULL
    CREATE TABLE v2.INPI_REVERSEMENTS (INPIRV_ID_INPIRV int NOT NULL PRIMARY KEY, INPIRV_ID_PAYMENT varchar(50) NOT NULL, INPIRV_ID_ENCMT int NULL,
        INPIRV_ID_UTLST int NULL, INPIRV_ID_DMND int NULL, INPIRV_FICHIER_XML varchar(max) NOT NULL, INPIRV_DATE_CREATION datetime NOT NULL,
        INPIRV_DATE_EXTRACT_BEGIN datetime NOT NULL, INPIRV_DATE_EXTRACT_END datetime NOT NULL, INPIRV_NB_REVERSEMENTS int NULL,
        INPIRV_TOTAL_HANDLING_AMOUNT money NULL, INPIRV_ETAT_TRAITEMENT varchar(2) NOT NULL, INPIRV_DATE_TRAITEMENT datetime NULL,
        INPIRV_COMMENTAIRES varchar(1024) NULL);
INSERT INTO v2.INPI_REVERSEMENTS (INPIRV_ID_INPIRV, INPIRV_ID_PAYMENT, INPIRV_FICHIER_XML, INPIRV_DATE_CREATION, INPIRV_DATE_EXTRACT_BEGIN,
    INPIRV_DATE_EXTRACT_END, INPIRV_NB_REVERSEMENTS, INPIRV_TOTAL_HANDLING_AMOUNT, INPIRV_ETAT_TRAITEMENT) VALUES
    -- a valider (etat 1, le seul coche a l'ouverture) : retenus, par debut d'extraction
    (2206001, 'PAY12-001', '<feepaymentdata/>', DATEADD(DAY, -32, @aujourdhui), DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -33, @aujourdhui), 3, 245.50, '1'),
    (2206002, 'PAY12-002', '<feepaymentdata/>', DATEADD(DAY, -12, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), DATEADD(DAY, -13, @aujourdhui), 1, 80.00, '1'),
    -- ECARTES : valide (2) et rejete (3), non coches a l'ouverture -- jumeau retenu 2206002
    (2206003, 'PAY12-003', '<feepaymentdata/>', DATEADD(DAY, -11, @aujourdhui), DATEADD(DAY, -19, @aujourdhui), DATEADD(DAY, -12, @aujourdhui), 2, 50.00, '2'),
    (2206004, 'PAY12-004', '<feepaymentdata/>', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -18, @aujourdhui), DATEADD(DAY, -11, @aujourdhui), 1, 10.00, '3');
