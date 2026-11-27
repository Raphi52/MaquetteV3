-- Jeu de LecteurTiersPayeurs / EcrivainTiersPayeurs (« Tiers payeurs », PROC_GESTTIERSPAYEURS), lot 14 du plan-676a4b19. Identifiants
-- 24002xx (payeurs) et 24002 9x (ventilation), abreges « L14... ». Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches, .gestes.
-- Les tables aux types de RIG_DEV (PAYEUR.cs et VENTILATION.cs de RigBaseGreffe). Ce jeu est joue AVANT ceux qui creent PAYEUR
-- (PROC_RECHENC) et VENTILATION (PROC_JCAISSE, le premier) : PAYEUR recoit des DEFAULT sur ses colonnes NOT NULL (PROC_RECHENC y insere
-- sans elles), VENTILATION la definition exacte de PROC_JCAISSE.sql (rien ne change pour les jeux qui suivent). La colonne
-- FACTURE.FACTU_ID_PYEUR, que la suppression lit, est ajoutee par PROC_RAPPACTEUR.sql, joue apres PROC_JVENTE qui cree FACTURE.
IF OBJECT_ID(N'v2.PAYEUR') IS NULL
    CREATE TABLE v2.PAYEUR (PYEUR_ID_PYEUR int NOT NULL PRIMARY KEY, PYEUR_LIBELLE varchar(100) NULL, PYEUR_IS_PRINCIPAL bit NOT NULL);
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_ABREGE') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_ABREGE varchar(10) NOT NULL CONSTRAINT DF_L14_PYEUR_ABREGE DEFAULT ('');
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_LIGNE_ADRESSE') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_LIGNE_ADRESSE varchar(255) NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_SUITE_ADRESSE') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_SUITE_ADRESSE varchar(255) NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_CODE_POSTAL') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_CODE_POSTAL varchar(32) NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_LOCALITE_POSTALE') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_LOCALITE_POSTALE varchar(255) NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_EMAIL') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_EMAIL varchar(255) NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_ID_MNDPC') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_ID_MNDPC int NULL;
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_IS_ACTIF') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_IS_ACTIF bit NOT NULL CONSTRAINT DF_L14_PYEUR_ACTIF DEFAULT (1);
IF COL_LENGTH('v2.PAYEUR', 'PYEUR_DATE_CREATION') IS NULL ALTER TABLE v2.PAYEUR ADD PYEUR_DATE_CREATION datetime NOT NULL CONSTRAINT DF_L14_PYEUR_DATE DEFAULT ('20000101');
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);

EXEC sp_executesql N'
INSERT INTO v2.PAYEUR (PYEUR_ID_PYEUR, PYEUR_ABREGE, PYEUR_LIBELLE, PYEUR_LIGNE_ADRESSE, PYEUR_CODE_POSTAL, PYEUR_LOCALITE_POSTALE, PYEUR_EMAIL,
    PYEUR_IS_PRINCIPAL, PYEUR_IS_ACTIF, PYEUR_DATE_CREATION) VALUES
    -- principal, actif, adresse complete : « Enregistré »
    (2400201, ''L14A'', ''TIERS BANC ALPHA'', ''1 rue du Banc'', ''75001'', ''Paris'', ''alpha@exemple.test'', 1, 1, ''20240101''),
    -- ni code postal ni ville : « Adresse à compléter » ; une ventilation le cite (ci-dessous) : sa suppression est refusee
    (2400202, ''L14B'', ''TIERS BANC BETA'', NULL, NULL, NULL, NULL, 1, 1, ''20240101''),
    -- sans designation : « Sans désignation », l''abrege tient lieu de nom
    (2400203, ''L14C'', NULL, NULL, ''69001'', ''Lyon'', NULL, 1, 1, ''20240101''),
    -- inactif : groupe « Inactifs » -- jumeau : 2400201 (actif)
    (2400204, ''L14D'', ''TIERS BANC INACTIF'', NULL, ''13001'', ''Marseille'', NULL, 1, 0, ''20240101''),
    -- ECARTE : non principal (un payeur de procedure collective, Payeur.cs:191) -- jumeau retenu : 2400201
    (2400205, ''L14E'', ''TIERS BANC PC'', NULL, ''75001'', ''Paris'', NULL, 0, 1, ''20240101''),
    -- l''abrege reserve TPG : en lecture seule (OPE_TIERSPAYEURSDETAIL.cs:76)
    (2400206, ''TPG'', ''TRESOR PUBLIC BANC'', NULL, ''75001'', ''Paris'', NULL, 1, 1, ''20240101'');
-- la ventilation du payeur L14B, rattachee a aucun encaissement ni affaire : aucun autre ecran ne la lit (ils la cherchent par
-- encaissement ou par affaire)
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_PYEUR, VENTI_ID_ENCMT, VENTI_MONTANT) VALUES (2400291, 2400202, 2400999, 10);';
