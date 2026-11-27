-- Jeu de LecteurRemiseEnBanque et EcrivainRemiseEnBanque (« Remise en banque », PROC_REMBANQ), lot 12 du plan-676a4b19. Identifiants
-- 22010xx (encaissements), 22019xx (banque). Ce qui est retenu ou ecarte, et pourquoi : PROC_REMBANQ.attendu ; les gestes :
-- PROC_REMBANQ.gestes. Types : ceux de RIG_DEV (SSDT RigDatabase\dbo\Tables\ENCAISSEMENT.sql, MODE_PAIEMENT.sql,
-- MODE_PAIEMENT_BANQUE.sql, BANQUE.sql). Les tables ENCAISSEMENT et MODE_PAIEMENT sont partagees avec PROC_RECHENC.sql (lot 6) :
-- meme forme (cle sans IDENTITY), colonnes ajoutees NULLables (RIG_DEV les a NOT NULL a DEFAULT : R_MODEPAIEME_UTILISABLE_SAISIE 0,
-- R_MODEPAIEME_TYPE 'CH', R_MODEPAIEME_REMIS_VISIBLE 1) -- les modes CHQ et VIR de PROC_RECHENC n'ont donc ni saisie ni type, et
-- n'entrent ni dans cet ecran ni dans PROC_REJET (cheques). Les encaissements du jour d'ici entrent dans « Encaissements »
-- (PROC_RECHENC.attendu les porte).
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
IF COL_LENGTH('v2.ENCAISSEMENT', 'ENCMT_ID_BANQU') IS NULL ALTER TABLE v2.ENCAISSEMENT ADD ENCMT_ID_BANQU int NULL;
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL);
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_UTILISABLE_SAISIE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_UTILISABLE_SAISIE bit NULL;
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_TYPE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_TYPE varchar(3) NULL;
IF COL_LENGTH('v2.MODE_PAIEMENT', 'R_MODEPAIEME_REMIS_VISIBLE') IS NULL ALTER TABLE v2.MODE_PAIEMENT ADD R_MODEPAIEME_REMIS_VISIBLE bit NULL;
IF OBJECT_ID(N'v2.BANQUE') IS NULL
    CREATE TABLE v2.BANQUE (BANQU_ID_BANQU int NOT NULL PRIMARY KEY, BANQU_CODE varchar(10) NOT NULL, BANQU_NOM varchar(100) NOT NULL);
IF OBJECT_ID(N'v2.MODE_PAIEMENT_BANQUE') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT_BANQUE (MODBQ_MODE_PAIEMENT varchar(8) NOT NULL, MODBQ_ID_BANQU int NOT NULL);

-- Par sp_executesql : les colonnes ajoutees ci-dessus n'existent pas encore quand SQL Server compile le lot.
EXEC sp_executesql N'
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_UTILISABLE_SAISIE, R_MODEPAIEME_TYPE, R_MODEPAIEME_REMIS_VISIBLE) VALUES
    (''L12ESP'', ''Espèces (banc 12)'', 1, NULL, 1),
    (''L12CB'', ''Carte (banc 12)'', 1, NULL, 1),
    -- utilisable en saisie, SANS banque (aucune ligne MODE_PAIEMENT_BANQUE) : listee, laissee a remettre par le bordereau
    (''L12TPE'', ''Terminal sans banque (banc 12)'', 1, NULL, 1),
    -- ECARTE : non utilisable en saisie (OPE_MODEPAIEMENT.cs:43) -- jumeau retenu L12ESP
    (''L12NS'', ''Non saisissable (banc 12)'', 0, NULL, 1);
-- ECARTE : PV, hors remise en banque meme utilisable en saisie (OPE_MODEPAIEMENT.cs:43) -- jumeau retenu L12ESP
INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN, R_MODEPAIEME_UTILISABLE_SAISIE)
SELECT ''PV'', ''Provision'', 1 WHERE NOT EXISTS (SELECT 1 FROM v2.MODE_PAIEMENT WHERE R_MODEPAIEME_VALEUR = ''PV'');
INSERT INTO v2.BANQUE (BANQU_ID_BANQU, BANQU_CODE, BANQU_NOM) VALUES (2201901, ''BQ12'', ''BANQUE DU BANC 12'');
INSERT INTO v2.MODE_PAIEMENT_BANQUE (MODBQ_MODE_PAIEMENT, MODBQ_ID_BANQU) VALUES (''L12ESP'', 2201901), (''L12CB'', 2201901);

INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DESIGNATION_PAYEUR, ENCMT_NUMERO_TITRE,
    ENCMT_MODE_PAIEMENT, ENCMT_DATE_COMPTABILISATION, ENCMT_REMIS) VALUES
    -- a remettre, du jour : AFaire
    (2201001, 120.50, DATEADD(HOUR, 8, @aujourdhui), ''PAYEUR R12A'', ''R12ESP01'', ''L12ESP'', NULL, NULL),
    (2201002, 79.50, DATEADD(HOUR, 9, @aujourdhui), ''PAYEUR R12B'', ''R12ESP02'', ''L12ESP'', NULL, NULL),
    (2201003, 45.00, DATEADD(MINUTE, 630, @aujourdhui), ''PAYEUR R12C'', ''R12CB03'', ''L12CB'', NULL, NULL),
    (2201004, 33.33, DATEADD(HOUR, 11, @aujourdhui), ''PAYEUR R12D'', ''R12TPE04'', ''L12TPE'', NULL, NULL),
    -- ECARTES : mode non saisissable, PV -- jumeau retenu 2201001
    (2201005, 10.00, DATEADD(HOUR, 8, @aujourdhui), ''PAYEUR R12E'', ''R12NS05'', ''L12NS'', NULL, NULL),
    (2201006, 15.00, DATEADD(HOUR, 8, @aujourdhui), ''PAYEUR R12F'', ''R12PV06'', ''PV'', NULL, NULL),
    -- ECARTES : la veille a 23 h 30, le lendemain a 0 h -- jumeaux retenus 2201001 (le jour a 8 h) et 2201004 (le jour a 11 h)
    (2201007, 60.00, DATEADD(MINUTE, -30, @aujourdhui), ''PAYEUR R12G'', ''R12VEIL07'', ''L12ESP'', NULL, NULL),
    (2201008, 70.00, DATEADD(DAY, 1, @aujourdhui), ''PAYEUR R12H'', ''R12DEM08'', ''L12ESP'', NULL, NULL),
    -- remis 41, non comptabilise : EnCours (annulable) ; remis 42, comptabilise : Validee (fige)
    (2201009, 200.00, DATEADD(HOUR, 12, @aujourdhui), ''PAYEUR R12I'', ''R12CB09'', ''L12CB'', NULL, 41),
    (2201010, 300.00, DATEADD(MINUTE, 750, @aujourdhui), ''PAYEUR R12J'', ''R12CB10'', ''L12CB'', DATEADD(HOUR, 18, @aujourdhui), 42),
    -- ECARTE : un remis negatif (brouillon, jamais ecrit par la V2) -- jumeau retenu 2201009 (remis 41)
    (2201011, 25.00, DATEADD(HOUR, 13, @aujourdhui), ''PAYEUR R12K'', ''R12NEG11'', ''L12ESP'', NULL, -3),
    -- hors liste (le mois precedent), a remettre : sert au refus « mois differents » du bordereau (PROC_REMBANQ.gestes, bloc 2)
    (2201013, 5.00, DATEADD(HOUR, 8, DATEADD(MONTH, -1, @aujourdhui)), ''PAYEUR R12M'', ''R12MOIS13'', ''L12ESP'', NULL, NULL);
-- Les encaissements du remis 41 et 42 ont leur banque (le bordereau la pose).
UPDATE v2.ENCAISSEMENT SET ENCMT_ID_BANQU = 2201901 WHERE ENCMT_ID_ENCMT IN (2201009, 2201010);
',
    N'@aujourdhui datetime', @aujourdhui;
