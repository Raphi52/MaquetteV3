-- Jeu de LecteurJournalCaisse (« Journal de caisse », PROC_JCAISSE), lot 13 du plan-676a4b19. Identifiants 23002xx. Ce qui est
-- retenu ou ecarte, et pourquoi : PROC_JCAISSE.attendu et .recherches.
-- Types : ceux de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le 27/11/2026 : ENCAISSEMENT, VENTILATION, MODE_PAIEMENT), colonnes
-- lues. ENCAISSEMENT et VENTILATION ont la definition de PROC_RECHENC.sql (joue apres) ; VENTI_ID_ENCMT reste nullable pour
-- PROC_RETAUD.sql, PROC_XPILOTAGE.sql et PROC_XXAFF.sql (des ventilations sans encaissement). MODE_PAIEMENT porte en plus le type
-- (R_MODEPAIEME_TYPE, NOT NULL DEFAULT 'CH' dans RIG_DEV), le groupe du journal. CLIENT est la fiche rec.Client (REC-013),
-- UTILISATEUR le compte soc.Compte (IdV2). AFFAIRE a la definition de PROC_CORDJRCS.sql (joue avant).
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL);
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_TYPE') IS NULL
    ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_TYPE varchar(3) NOT NULL CONSTRAINT DF_L13_MODE_TYPE DEFAULT ('CH');
IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_ID_UTLST_CREATION int NULL, AFFRE_ID_UTLST_EN_COURS int NULL,
        AFFRE_DATE_CREATION datetime NOT NULL, AFFRE_LIBELLE varchar(70) NULL);

-- Les modes de RIG_DEV (valeur, libelle, type), lus le 27/11/2026.
EXEC sp_executesql N'
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_TYPE)
SELECT v.c, v.l, v.t FROM (VALUES (''CH'', ''Chèque'', ''CH''), (''ES'', ''Espèce'', ''ES''), (''CB'', ''Carte bancaire'', ''CB''),
    (''VI'', ''Virement LCL'', ''VI''), (''RBT'', ''Remboursement par chèque'', ''RBT'')) AS v (c, l, t)
WHERE NOT EXISTS (SELECT 1 FROM v2.MODE_PAIEMENT AS m WHERE m.R_MODEPAIEME_VALEUR = v.c);';

INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, Desactive) VALUES
    (2300291, 'banc.l13.caisse1', 'CAISSE', 'Alice', 'L13C1', 'Mme', 0), (2300292, 'banc.l13.caisse2', 'GUICHET', 'Bruno', 'L13C2', 'M.', 0);
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES
    (2300271, 'L13CLIA', '411L13CLIA', 'CLIENT CAISSE A', 'C', 0, 0), (2300272, 'L13CLIB', '411L13CLIB', 'CLIENT CAISSE B', 'D', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION, AFFRE_LIBELLE) VALUES (2300281, '20250101', 'AFFAIRE CAISSE L13');

-- Le jour du banc : DEUX encaissements seulement -- ils entrent aussi dans la liste a l'ouverture des « Encaissements »
-- (PROC_RECHENC, les encaissements du jour), dont l'.attendu les porte. Les cas ecartes se jouent sur des dates fixes (fevrier 2024,
-- qu'aucun autre jeu n'emploie), par PROC_JCAISSE.recherches.
INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DESIGNATION_PAYEUR, ENCMT_NUMERO_TITRE,
    ENCMT_MODE_PAIEMENT, ENCMT_DESIGNATION_BANQUE, ENCMT_DATE_COMPTABILISATION, ENCMT_ID_UTLST, ENCMT_REMIS) VALUES
    -- aujourd'hui 9 h, un cheque ventile, ni remis ni comptabilise : « À comptabiliser »
    (2300201, 100.10, DATEADD(HOUR, 9, @aujourdhui), 'PAYEUR L13 A', 'L13CHQ1', 'CH', 'BANQUE L13', NULL, 2300291, NULL),
    -- aujourd'hui 0 h (borne basse retenue), remise -1 = « pas de remise » : « À comptabiliser » ; agent non repris (IdV2 inconnu) : garde
    (2300208, 7.77, @aujourdhui, 'PAYEUR L13 MINUIT', NULL, 'CB', NULL, NULL, 2300299, -1),
    -- ECARTE : la veille a 23 h -- jumeau retenu 2300208 (le jour a 0 h)
    (2300207, 8.00, DATEADD(HOUR, -1, @aujourdhui), 'PAYEUR L13 VEILLE', NULL, 'CH', NULL, NULL, 2300291, NULL),
    -- ECARTE : le lendemain a 0 h -- jumeau retenu 2300201 (le jour)
    (2300209, 9.00, DATEADD(DAY, 1, @aujourdhui), 'PAYEUR L13 DEMAIN', NULL, 'CH', NULL, NULL, 2300291, NULL),
    -- du 05 au 07/02/2024 (PROC_JCAISSE.recherches, periode 2024-02-05|2024-02-07) :
    -- especes ventilees DEUX fois : une seule ligne, 50,05 compte une fois ; comptabilise : « Comptabilisé »
    (2300202, 50.05, '20240205 10:00', 'PAYEUR L13 B', NULL, 'ES', NULL, '20240206', 2300291, NULL),
    -- un remboursement (type RBT) : le payeur est le client (CASE R_MODEPAIEME_TYPE WHEN 'RBT') ; remis : « Remis en banque »
    (2300203, -20.00, '20240205 11:00', 'PAYEUR IGNORE', 'L13RBT', 'RBT', NULL, NULL, 2300292, 5),
    -- un virement de 1 234,56 le 06/02/2024, comptabilise
    (2300210, 1234.56, '20240206 10:00', 'PAYEUR L13 MARS', 'L13VIR1', 'VI', 'BANQUE MARS', '20240207', 2300292, NULL),
    -- une carte le 07/02/2024 a 23 h 59 (borne haute retenue)
    (2300211, 12.34, '20240207 23:59', 'PAYEUR L13 CARTE', NULL, 'CB', NULL, NULL, 2300291, NULL),
    -- ECARTE : ventilation sans affaire (INNER JOIN AFFAIRE de la V2) -- jumeau retenu 2300210
    (2300204, 30.00, '20240206 12:00', 'PAYEUR L13 SANS AFFAIRE', NULL, 'CH', NULL, NULL, 2300291, NULL),
    -- ECARTE : aucune ventilation (INNER JOIN VENTILATION) -- jumeau retenu 2300210
    (2300205, 40.00, '20240206 13:00', 'PAYEUR L13 SANS VENTIL', NULL, 'CH', NULL, NULL, 2300291, NULL),
    -- ECARTE : mode absent de MODE_PAIEMENT (INNER JOIN MODE_PAIEMENT) -- jumeau retenu 2300210
    (2300206, 15.00, '20240206 14:00', 'PAYEUR L13 MODE', NULL, 'L13X', NULL, NULL, 2300291, NULL),
    -- ECARTE de la periode : le 08/02/2024 a 0 h -- jumeau retenu 2300211 (le 07 a 23 h 59)
    (2300212, 99.99, '20240208', 'PAYEUR L13 HORS', NULL, 'CH', NULL, NULL, 2300291, NULL);
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_ENCMT, VENTI_ID_AFFRE, VENTI_ID_CLIEN, VENTI_MONTANT, VENTI_DATE_VENTILATION) VALUES
    (2300221, 2300201, 2300281, 2300271, 100.10, @aujourdhui),
    (2300225, 2300208, 2300281, 2300271, 7.77, @aujourdhui),
    (2300228, 2300207, 2300281, 2300271, 8.00, @aujourdhui), (2300229, 2300209, 2300281, 2300271, 9.00, @aujourdhui),
    (2300222, 2300202, 2300281, 2300271, 30.05, '20240205'), (2300223, 2300202, 2300281, 2300272, 20.00, '20240205'),
    (2300224, 2300203, 2300281, 2300272, -20.00, '20240205'),
    (2300230, 2300210, 2300281, 2300271, 1234.56, '20240206'), (2300231, 2300211, 2300281, 2300272, 12.34, '20240207'),
    (2300226, 2300204, NULL, 2300271, 30.00, '20240206'),
    (2300227, 2300206, 2300281, 2300271, 15.00, '20240206'),
    (2300232, 2300212, 2300281, 2300271, 99.99, '20240208');
-- Lot 4 du plan-f4fb097f (28/11/2026), les gestes de PROC_JCAISSE : le droit d'executer le processus JCAISSE (FORM_ACCUEIL.cs:1614-1624,
-- soc.Droit), fonction du domaine Comptabilite (soc.Fonction de RIG_DEV : « Journal de caisse », DLL, Comptabilite = 1), sur le role BANC
-- de l'agent du banc ; et un agent SANS ce droit (banc.l4.sansdroit, role BANC_L4 sans fonction, admis comme lui), pour le refus. Les
-- colonnes de la file des editions que l'etat ecrit (EditionsEtatsV2.cs) et que les controles du .gestes relisent.
IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = 'JCAISSE')
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2, Comptabilite) VALUES ('JCAISSE', N'Journal de caisse', 'DLL', 1);
IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = 'BANC' AND Fonction = 'JCAISSE') INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'JCAISSE');
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
