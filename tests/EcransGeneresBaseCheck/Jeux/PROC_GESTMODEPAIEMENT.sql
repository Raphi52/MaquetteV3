-- Jeu de LecteurModesPaiement / EcrivainModesPaiement (« Modes de paiement », PROC_GESTMODEPAIEMENT), lot 15 du plan-676a4b19.
-- Codes « L15.. » (modes), banques 25001xx, encaissement 25002xx. Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches,
-- .gestes.
-- Types : ceux de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 27/11/2026 : MODE_PAIEMENT, MODE_PAIEMENT_BANQUE, BANQUE,
-- ENCAISSEMENT ; defauts de RIG_DEV : UTILISABLE_SAISIE 0, GROUPE 1, REMIS_VISIBLE 1, TYPE 'CH'). Ce jeu est joue AVANT ceux qui creent
-- MODE_PAIEMENT et ENCAISSEMENT (PROC_JCAISSE, PROC_RECHENC, PROC_VLETTRAGE) : ENCAISSEMENT recoit la definition exacte de
-- PROC_JCAISSE.sql et PROC_RECHENC.sql (rien ne change pour eux) ; MODE_PAIEMENT ses colonnes de RIG_DEV, et des DEFAULT de banc sur
-- les colonnes NOT NULL que RIG_DEV laisse sans defaut (ISO, synonyme, date et auteur de creation) : PROC_JCAISSE et PROC_RECHENC y
-- inserent sans elles (leurs modes CH, ES, CB, VI, RBT, CHQ, VIR entrent donc dans cette liste : .attendu).
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_MODEPAIEME_ISO varchar(20) NOT NULL CONSTRAINT DF_L15_MODE_ISO DEFAULT (''),
        R_MODEPAIEME_SYNONYME varchar(16) NOT NULL CONSTRAINT DF_L15_MODE_SYN DEFAULT (''),
        R_MODEPAIEME_ABREGE_EDIT varchar(8) NULL, R_MODEPAIEME_SIMPLIFIE_EDIT varchar(35) NULL, R_MODEPAIEME_DEVELOPPE_EDIT varchar(70) NULL,
        R_MODEPAIEME_EXHAUSTIF_EDIT varchar(128) NULL,
        R_MODEPAIEME_DATE_CREATION datetime NOT NULL CONSTRAINT DF_L15_MODE_DATE DEFAULT ('20000101'),
        R_MODEPAIEME_AUTEUR_CREATION varchar(16) NOT NULL CONSTRAINT DF_L15_MODE_AUTEUR DEFAULT (''),
        R_MODEPAIEME_DATE_MODIF datetime NULL, R_MODEPAIEME_AUTEUR_MODIF varchar(16) NULL,
        R_MODEPAIEME_UTILISABLE_SAISIE bit NOT NULL CONSTRAINT DF_L15_MODE_ACTIF DEFAULT (0),
        R_MODEPAIEME_GROUPE bit NOT NULL CONSTRAINT DF_L15_MODE_GROUPE DEFAULT (1),
        R_MODEPAIEME_REMIS_VISIBLE bit NOT NULL CONSTRAINT DF_L15_MODE_REMIS DEFAULT (1),
        R_MODEPAIEME_TYPE varchar(3) NOT NULL CONSTRAINT DF_L15_MODE_TYPE DEFAULT ('CH'));
IF OBJECT_ID(N'v2.MODE_PAIEMENT_BANQUE') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT_BANQUE (MODBQ_MODE_PAIEMENT varchar(8) NOT NULL PRIMARY KEY, MODBQ_ID_BANQU int NOT NULL);
IF OBJECT_ID(N'v2.BANQUE') IS NULL
    CREATE TABLE v2.BANQUE (BANQU_ID_BANQU int NOT NULL PRIMARY KEY, BANQU_CODE varchar(10) NOT NULL, BANQU_NOM varchar(100) NOT NULL);
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);

INSERT INTO v2.BANQUE (BANQU_ID_BANQU, BANQU_CODE, BANQU_NOM) VALUES (2500101, 'L15LCL', 'BANQUE BANC LCL'), (2500102, 'L15CIC', 'BANQUE BANC CIC');
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_ISO, R_MODEPAIEME_SYNONYME, R_MODEPAIEME_DATE_CREATION,
    R_MODEPAIEME_AUTEUR_CREATION, R_MODEPAIEME_UTILISABLE_SAISIE, R_MODEPAIEME_GROUPE, R_MODEPAIEME_REMIS_VISIBLE, R_MODEPAIEME_TYPE) VALUES
    -- actif, banque associee, libelle de 6 caracteres et plus : « Enregistré » ; un encaissement le porte (ci-dessous) : sa suppression
    -- et le changement de son type sont refuses
    ('L15CH', 'Chèque banc', 'L15CH', 'L15CH', '20240101', 'BANC', 1, 1, 1, 'CH'),
    -- banque requise mais absente : « Banque à associer » -- jumeau : L15CH (banque associee)
    ('L15CB', 'Carte banc', 'L15CB', 'L15CB', '20240101', 'BANC', 1, 1, 1, 'CB'),
    -- code REG : sans banque (OPE_MODEPAIEMENTDETAIL.cs:70), « Enregistré » sans lien -- jumeau : L15CB (meme absence, banque requise)
    ('REG', 'Régularisation banc', 'REG', 'REG', '20240101', 'BANC', 1, 1, 1, 'REG'),
    -- libelle de 5 caracteres : « À compléter » (ControlsValidation, :299) -- jumeau : L15CB (10 caracteres)
    ('L15ES', 'Espèc', 'L15ES', 'L15ES', '20240101', 'BANC', 1, 1, 1, 'ES'),
    -- inactif : groupe « Inactifs » -- jumeau : L15CH (actif)
    ('L15VI', 'Virement banc', 'L15VI', 'L15VI', '20240101', 'BANC', 0, 0, 1, 'VI');
INSERT INTO v2.MODE_PAIEMENT_BANQUE (MODBQ_MODE_PAIEMENT, MODBQ_ID_BANQU) VALUES ('L15CH', 2500101), ('L15ES', 2500102), ('L15VI', 2500101);
-- l'encaissement qui porte L15CH, date fixe de 2019 (aucun autre ecran ne le lit : la caisse et les encaissements lisent le jour du
-- banc ou leurs periodes de 2024 et 2025)
INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_MODE_PAIEMENT) VALUES (2500201, 15, '20190115', 'L15CH');
