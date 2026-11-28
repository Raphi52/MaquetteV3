-- Jeu de LecteurChequesEmis (« Cheques emis », PROC_EDCHQEMIS), lot 12 du plan-676a4b19. Identifiants 22007xx (encaissements),
-- 22008xx (ventilations), 22092xx (clients rec.Client), 22093xx (affaires), 22094xx (demandes). Types : RIG_DEV (SSDT ENCAISSEMENT.sql,
-- VENTILATION.sql, MODE_PAIEMENT.sql) ; tables partagees avec PROC_RECHENC.sql (colonnes ajoutees NULLables). Les deux
-- remboursements du jour entrent aussi dans « Encaissements » (PROC_RECHENC.attendu les porte) ; leur mode n'est pas utilisable en
-- saisie (pas dans « Remise en banque ») et n'est pas un cheque (pas dans « Rejets »).
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_ORIGINE') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_ORIGINE varchar(3) NULL;
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL);
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_UTILISABLE_SAISIE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_UTILISABLE_SAISIE bit NULL;
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_TYPE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_TYPE varchar(3) NULL;
IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_ID_UTLST_CREATION int NULL, AFFRE_ID_UTLST_EN_COURS int NULL,
        AFFRE_DATE_CREATION datetime NOT NULL, AFFRE_LIBELLE varchar(70) NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;

EXEC sp_executesql N'
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_UTILISABLE_SAISIE, R_MODEPAIEME_TYPE) VALUES
    (''L12RBT'', ''Remboursement (banc 12)'', 0, ''RBT''),
    -- pas un remboursement : son encaissement 2200704 n entre pas
    (''L12AUT'', ''Autre (banc 12)'', 0, NULL);
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES
    (2209201, ''L12RBTC'', ''411L12RBTC'', ''CLI REMBOURSE'', ''C'', 0, 0),
    -- le compte technique des notifications : le beneficiaire est le commentaire de l encaissement (EmisTropPercus.cs:137-141)
    (2209202, ''DGREFFE'', ''411DGREFFE'', ''DIVERS GREFFE'', ''D'', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION, AFFRE_LIBELLE) VALUES (2209301, ''20250601'', ''AFF REMBOURSEMENT 12'');
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION, DMND_ID_AFFRE) VALUES
    (2209401, ''D12RBT001'', ''V'', ''IMM'', ''20250601'', 2209301);
INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DESIGNATION_PAYEUR, ENCMT_NUMERO_TITRE, ENCMT_MODE_PAIEMENT,
    ENCMT_ORIGINE, ENCMT_COMMENTAIRE) VALUES
    -- du jour : un trop-percu (origine vide) et une notification (NTF) du compte DGREFFE
    (2200701, 45.00, DATEADD(HOUR, 9, @aujourdhui), ''GREFFE'', ''RBT12001'', ''L12RBT'', NULL, NULL),
    (2200702, 30.00, DATEADD(HOUR, 10, @aujourdhui), ''GREFFE'', ''RBT12002'', ''L12RBT'', ''NTF'', ''BENEFICIAIRE NOTIF''),
    -- en juin 2025 : retrouves par PROC_EDCHQEMIS.recherches (trop-percu, notification) ; 2200704 n est pas un remboursement
    (2200703, 20.00, ''20250610 11:00'', ''GREFFE'', ''RBT12003'', ''L12RBT'', NULL, NULL),
    (2200704, 15.00, ''20250610 11:00'', ''GREFFE'', ''AUT12004'', ''L12AUT'', NULL, NULL),
    (2200705, 25.00, ''20250612 11:00'', ''GREFFE'', ''RBT12005'', ''L12RBT'', ''NTF'', ''BENEFICIAIRE JUIN'');
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_ENCMT, VENTI_ID_AFFRE, VENTI_ID_CLIEN, VENTI_MONTANT, VENTI_DATE_VENTILATION) VALUES
    (2200801, 2200701, 2209301, 2209201, 45.00, @aujourdhui), (2200802, 2200702, 2209301, 2209202, 30.00, @aujourdhui),
    (2200803, 2200703, 2209301, 2209201, 20.00, ''20250610''), (2200804, 2200704, 2209301, 2209201, 15.00, ''20250610''),
    (2200805, 2200705, 2209301, 2209202, 25.00, ''20250612'');
',
    N'@aujourdhui datetime', @aujourdhui;
-- Lot 4 du plan-f4fb097f (28/11/2026), les gestes de PROC_EDCHQEMIS : le droit d'executer le processus EDCHQEMIS (FORM_ACCUEIL.cs:1614-1624,
-- soc.Droit), fonction du domaine Comptabilite (soc.Fonction de RIG_DEV : « Liste des chèques émis », EXE, Comptabilite = 1), sur le role BANC
-- de l'agent du banc ; et un agent SANS ce droit (banc.l4.sansdroit, role BANC_L4 sans fonction, admis comme lui), pour le refus. Les
-- colonnes de la file des editions que l'etat ecrit (EditionsEtatsV2.cs) et que les controles du .gestes relisent.
IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = 'EDCHQEMIS')
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2, Comptabilite) VALUES ('EDCHQEMIS', N'Liste des chèques émis', 'EXE', 1);
IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = 'BANC' AND Fonction = 'EDCHQEMIS') INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'EDCHQEMIS');
IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = 'BANC_L4') INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC_L4', 'Banc lot 4, sans droit');
IF NOT EXISTS (SELECT 1 FROM soc.Compte WHERE CompteWindows = 'banc.l4.sansdroit')
BEGIN
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT 'banc.l4.sansdroit', 'SANSDROIT', 'Banc', 'BANCL4', 'M.', CodeGreffe, IdService, 0 FROM soc.Compte WHERE CompteWindows = 'banc.ecrans';
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC_L4' FROM soc.Compte WHERE CompteWindows = 'banc.l4.sansdroit';
END
IF OBJECT_ID(N'v2.DEMANDE_EDITION') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_ID_DMND int NULL, DMNED_ID_DSRCS int NULL,
        DMNED_LOT varchar(255) NULL, DMNED_PHASE_TRAITEMENT varchar(10) NOT NULL, DMNED_DATE_PRE_EMISSION datetime NULL,
        DMNED_DESIGNATION_DESTINATAIRE varchar(255) NULL, DMNED_LIBELLE_DOCUMENT varchar(255) NOT NULL,
        DMNED_LIBELLE_ERREUR varchar(255) NULL, DMNED_DATE_CREATION datetime NULL, DMNED_ID_EMAIL_ENVOYE int NULL,
        DMNED_MODE_ENVOI int NULL);
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_PROCESSUS varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_TYPE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_TYPE_GENERATION varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_PARAMETRE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_PARAMETRE_GENERATION varchar(max) NULL;
