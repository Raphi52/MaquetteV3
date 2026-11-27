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
    -- a valider (etat 1, le seul coche a l'ouverture) : retenus, par debut d'extraction.
    -- PAY12-001 est le fichier que « Valider » ACCEPTE (PROC_INPIPAY.gestes) : quatre reversements -- ENT12INPI0001 deux fois (commandes
    -- 501 et 502 : UNE ventilation de 145,50), A12INPI0002 (commande 503 : 100,00, ecrit « 100,00 » : la V2 lit la virgule comme un
    -- point, fee-payment-data.cs:9-14), ENT12INPI0404 sans montant (0 : ignore, jamais cherche, OPE_VALIDATION.cs:117). Total 245,50.
    (2206001, 'PAY12-001', '<fee-payment-data xsd-version="1.1" id-payment="PAY12-001" date-extract-begin="2026-10-01" date-extract-end="2026-10-31" date-produced="2026-11-02T10:00:00" total-handling-amount="245.50" total-handling-fee="245.50"><executor-office office-code="L12" total-fee="245.50">'
        + '<fee-pay applicant-number="ENT12INPI0001" command-number="501"><basic-fee-data total-fee="100.00"/></fee-pay>'
        + '<fee-pay applicant-number="A12INPI0002" command-number="503"><basic-fee-data total-fee="100,00"/></fee-pay>'
        + '<fee-pay applicant-number="ENT12INPI0001" command-number="502"><basic-fee-data total-fee="45.50"/></fee-pay>'
        + '<fee-pay applicant-number="ENT12INPI0404" command-number="509"><basic-fee-data total-fee=""/></fee-pay></executor-office></fee-payment-data>',
     DATEADD(DAY, -32, @aujourdhui), DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -33, @aujourdhui), 4, 245.50, '1'),
    (2206002, 'PAY12-002', '<feepaymentdata/>', DATEADD(DAY, -12, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), DATEADD(DAY, -13, @aujourdhui), 1, 80.00, '1'),
    -- ECARTES : valide (2) et rejete (3), non coches a l'ouverture -- jumeau retenu 2206002
    (2206003, 'PAY12-003', '<feepaymentdata/>', DATEADD(DAY, -11, @aujourdhui), DATEADD(DAY, -19, @aujourdhui), DATEADD(DAY, -12, @aujourdhui), 2, 50.00, '2'),
    (2206004, 'PAY12-004', '<feepaymentdata/>', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -18, @aujourdhui), DATEADD(DAY, -11, @aujourdhui), 1, 10.00, '3'),
    -- Les fichiers que « Valider » REFUSE, chacun pour un controle de la V2 (jumeau accepte : PAY12-001, ou le meme controle passe) :
    --   PAY12-005 : son seul numero national n'a aucune liasse -> « L'id d'arrivee n'a pas pu être déterminé. » (_GetIdArrivee, :187-205) ;
    --   PAY12-006 : la demande de J12INPI0006 n'a pas de facture (:146-149) ;
    --   PAY12-007 : le total du fichier (50,00) n'egale pas ses reversements (60,00) (:174-177) ;
    --   PAY12-008 : l'arrivee se trouve (ENT12INPI0001), mais A12INPI0404, premier dans l'ordre des numeros, n'a aucun DAS (Demat.cs:232-238).
    (2206005, 'PAY12-005', '<fee-payment-data id-payment="PAY12-005" date-extract-begin="2026-10-01" date-extract-end="2026-10-31"><executor-office total-fee="30.00">'
        + '<fee-pay applicant-number="ENT12INPI0404" command-number="551"><basic-fee-data total-fee="30.00"/></fee-pay></executor-office></fee-payment-data>',
     DATEADD(DAY, -9, @aujourdhui), DATEADD(DAY, -9, @aujourdhui), DATEADD(DAY, -8, @aujourdhui), 1, 30.00, '1'),
    (2206006, 'PAY12-006', '<fee-payment-data id-payment="PAY12-006" date-extract-begin="2026-10-01" date-extract-end="2026-10-31"><executor-office total-fee="25.00">'
        + '<fee-pay applicant-number="J12INPI0006" command-number="561"><basic-fee-data total-fee="25.00"/></fee-pay></executor-office></fee-payment-data>',
     DATEADD(DAY, -8, @aujourdhui), DATEADD(DAY, -8, @aujourdhui), DATEADD(DAY, -7, @aujourdhui), 1, 25.00, '1'),
    (2206007, 'PAY12-007', '<fee-payment-data id-payment="PAY12-007" date-extract-begin="2026-10-01" date-extract-end="2026-10-31"><executor-office total-fee="50.00">'
        + '<fee-pay applicant-number="ENT12INPI0001" command-number="571"><basic-fee-data total-fee="60.00"/></fee-pay></executor-office></fee-payment-data>',
     DATEADD(DAY, -7, @aujourdhui), DATEADD(DAY, -7, @aujourdhui), DATEADD(DAY, -6, @aujourdhui), 1, 50.00, '1'),
    (2206008, 'PAY12-008', '<fee-payment-data id-payment="PAY12-008" date-extract-begin="2026-10-01" date-extract-end="2026-10-31"><executor-office total-fee="15.00">'
        + '<fee-pay applicant-number="ENT12INPI0001" command-number="581"><basic-fee-data total-fee="10.00"/></fee-pay>'
        + '<fee-pay applicant-number="A12INPI0404" command-number="582"><basic-fee-data total-fee="5.00"/></fee-pay></executor-office></fee-payment-data>',
     DATEADD(DAY, -6, @aujourdhui), DATEADD(DAY, -6, @aujourdhui), DATEADD(DAY, -5, @aujourdhui), 2, 15.00, '1');

-- « Valider » (EcrivainReversementsInpi, OPE_VALIDATION.cs:67-205) : les colonnes que le geste lit et ecrit, aux types de RIG_DEV
-- (SSDT INPI_REVERSEMENTS.sql, ENCAISSEMENT.sql, AFFAIRE.sql, DEMANDE.sql, DEMAT_LIASSE_CFE.sql, DEMAT_DAS.sql, DEMAT_DCA.sql) ; tables
-- partagees, colonnes ajoutees NULLables. Identifiants 22061xx (affaires), 22062xx (demandes), 22065xx (liasses, DAS) ; factures F12P*.
-- Clients : ceux de PROC_REJET.sql (2204201 actif, 2204202 en sommeil -- la V2 ne regarde pas le statut ici).
IF COL_LENGTH('v2.INPI_REVERSEMENTS', 'INPIRV_TOTAL_HANDLING_FEE') IS NULL ALTER TABLE v2.INPI_REVERSEMENTS ADD INPIRV_TOTAL_HANDLING_FEE money NULL;
IF COL_LENGTH('v2.INPI_REVERSEMENTS', 'INPIRV_MONTANT_VALIDATION') IS NULL ALTER TABLE v2.INPI_REVERSEMENTS ADD INPIRV_MONTANT_VALIDATION money NULL;
IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_ID_UTLST_CREATION int NULL, AFFRE_ID_UTLST_EN_COURS int NULL,
        AFFRE_DATE_CREATION datetime NOT NULL, AFFRE_LIBELLE varchar(70) NULL);
IF COL_LENGTH('v2.AFFAIRE', 'AFFRE_NOTE_INTERNE') IS NULL ALTER TABLE v2.AFFAIRE ADD AFFRE_NOTE_INTERNE varchar(200) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ARRV') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ARRV int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_LIAISON') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_LIAISON varchar(14) NULL;
IF OBJECT_ID(N'v2.FACTURE') IS NULL
    CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NOT NULL);
IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TTC') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TTC money NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_CREATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_CREATION datetime NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_ID_CLIEN') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_ID_CLIEN int NULL;
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_ID_ARRV') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_ID_ARRV int NULL;
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_GUID') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_GUID varchar(36) NULL;
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_EXT_SOURCE') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_EXT_SOURCE varchar(5) NULL;
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_EXT_ID') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_EXT_ID varchar(32) NULL;
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_EXT_DATE') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_EXT_DATE datetime NULL;
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL);
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_TYPE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_TYPE varchar(3) NULL;
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_UTILISABLE_SAISIE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_UTILISABLE_SAISIE bit NULL;
IF OBJECT_ID(N'v2.DEMAT_LIASSE_CFE') IS NULL
    CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY, DMTLCF_ID_DMND int NULL, DMTLCF_NUMERO_NATIONAL_INPI varchar(50) NULL);
IF OBJECT_ID(N'v2.DEMAT_DAS') IS NULL
    CREATE TABLE v2.DEMAT_DAS (DMTDAS_ID_DMTDAS int NOT NULL PRIMARY KEY, DMTDAS_ID_DMND int NULL, DMTDAS_NUMERO_NATIONAL_INPI varchar(50) NULL);
IF COL_LENGTH('v2.DEMAT_DCA', 'DMTDCA_NUMERO_NATIONAL_INPI') IS NULL ALTER TABLE v2.DEMAT_DCA ADD DMTDCA_NUMERO_NATIONAL_INPI varchar(50) NULL;

EXEC sp_executesql N'
UPDATE v2.INPI_REVERSEMENTS SET INPIRV_TOTAL_HANDLING_FEE = INPIRV_TOTAL_HANDLING_AMOUNT WHERE INPIRV_ID_INPIRV BETWEEN 2206001 AND 2206008;
-- Deux modes : un virement utilisable en saisie (le mode du geste accepte) et un mode de type REG, exclu de la liste de la V2.
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_TYPE, R_MODEPAIEME_UTILISABLE_SAISIE) VALUES
    (''L12VICB'', ''Virement INPI (banc 12)'', ''VIR'', 1), (''L12REGI'', ''Régularisation (banc 12)'', ''REG'', 1);
-- Les affaires (creees en 2001 : hors de la periode des Formalites INPI ; apres la piece bloquante F12V005 du 14/02/2000 de
-- PROC_RELCLI.sql, que des factures non comptabilisees de 2000 remplaceraient), leurs demandes et factures. 2206102 a deux demandes : la
-- derniere facture de l affaire (F12P003, client 2204202) donne le client de la ventilation, pas celle de la demande du numero national.
INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION, AFFRE_LIBELLE, AFFRE_NOTE_INTERNE) VALUES
    (2206101, ''20010110'', ''INPIPAY LIASSE'', NULL), (2206102, ''20010110'', ''INPIPAY DAS'', ''NOTE INPI''), (2206106, ''20010110'', ''INPIPAY SANS FACTURE'', NULL);
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION, DMND_ID_AFFRE, DMND_ID_ARRV) VALUES
    (2206201, ''D12INPI0001'', ''V'', ''IMM'', ''20010115'', 2206101, 2206190), (2206202, ''D12INPI0002'', ''V'', ''IMM'', ''20010115'', 2206102, 2206191),
    (2206203, ''D12INPI0003'', ''V'', ''IMM'', ''20010215'', 2206102, NULL), (2206206, ''D12INPI0006'', ''V'', ''IMM'', ''20010115'', 2206106, 2206192);
INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_MONTANT_TTC, FACTU_DATE_CREATION, FACTU_NUM_DEMANDE, FACTU_ID_CLIEN) VALUES
    (''F12P001'', 145.50, ''20010120'', ''D12INPI0001'', 2204201), (''F12P002'', 60.00, ''20010120'', ''D12INPI0002'', 2204201),
    (''F12P003'', 40.00, ''20010220'', ''D12INPI0003'', 2204202);
INSERT INTO v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF, DMTLCF_ID_DMND, DMTLCF_NUMERO_NATIONAL_INPI) VALUES
    (2206501, 2206201, ''ENT12INPI0001''), (2206506, 2206206, ''J12INPI0006'');
INSERT INTO v2.DEMAT_DAS (DMTDAS_ID_DMTDAS, DMTDAS_ID_DMND, DMTDAS_NUMERO_NATIONAL_INPI) VALUES (2206502, 2206202, ''A12INPI0002'');
',
    N'@aujourdhui datetime', @aujourdhui;
