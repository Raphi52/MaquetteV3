-- Jeu de LecteurEncaissements (« Encaissements », PROC_RECHENC), lot 6 du plan-676a4b19. Identifiants 16004xx. Ce qui est
-- retenu ou ecarte, et pourquoi : PROC_RECHENC.attendu et PROC_RECHENC.recherches. Aucun autre jeu ne touche ces tables.
-- Types : ceux de RIG_DEV (SSDT, RigDatabase\dbo\Tables\ENCAISSEMENT.sql, VENTILATION.sql, PAYEUR.sql, MODE_PAIEMENT.sql),
-- colonnes lues ; CLIENT est la fiche rec.Client (REC-013).
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
-- VENTI_ID_ENCMT est NOT NULL dans RIG_DEV ; nullable ici, a la forme de PROC_XXAFF.sql (joue apres), qui pose une ventilation
-- sans encaissement (une provision de l'affaire) : elle n'entre pas dans cet ecran, qui part des encaissements.
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);
IF OBJECT_ID(N'v2.PAYEUR') IS NULL
    CREATE TABLE v2.PAYEUR (PYEUR_ID_PYEUR int NOT NULL PRIMARY KEY, PYEUR_LIBELLE varchar(100) NULL, PYEUR_IS_PRINCIPAL bit NOT NULL);
IF OBJECT_ID(N'v2.MODE_PAIEMENT') IS NULL
    CREATE TABLE v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR varchar(8) NOT NULL PRIMARY KEY, R_MODEPAIEME_LIBELLE_ECRAN varchar(35) NOT NULL);

INSERT INTO v2.MODE_PAIEMENT (R_MODEPAIEME_VALEUR, R_MODEPAIEME_LIBELLE_ECRAN)
SELECT v.c, v.l FROM (VALUES ('CHQ', 'Chèque'), ('VIR', 'Virement')) AS v (c, l)
WHERE NOT EXISTS (SELECT 1 FROM v2.MODE_PAIEMENT AS m WHERE m.R_MODEPAIEME_VALEUR = v.c);
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES
    (1600421, 'L6ALPHA', '411L6ALPHA', 'CLI ALPHA', 'CLIENT', 0, 0), (1600422, 'L6GAMMA', '411L6GAMMA', 'CLI GAMMA', 'CLIENT', 0, 0),
    (1600423, 'L6DELTA', '411L6DELTA', 'CLI DELTA', 'CLIENT', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.PAYEUR (PYEUR_ID_PYEUR, PYEUR_LIBELLE, PYEUR_IS_PRINCIPAL) VALUES (1600441, 'TIERS PAYEUR BANC', 0);

INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DESIGNATION_PAYEUR, ENCMT_NUMERO_TITRE,
    ENCMT_MODE_PAIEMENT, ENCMT_DESIGNATION_BANQUE, ENCMT_DATE_COMPTABILISATION, ENCMT_REMIS, ENCMT_COMMENTAIRE) VALUES
    -- aujourd'hui a 10 h, deux ventilations (une ligne chacune) : Validee
    (1600401, 1200, DATEADD(HOUR, 10, @aujourdhui), 'PAYEUR ALPHA', 'L6CHQ001', 'CHQ', 'BANQUE BANC', '18000101', 12, 'reglement du jour'),
    -- aujourd'hui a 9 h, sans ventilation : AFaire, « sans ventilation »
    (1600402, 850, DATEADD(HOUR, 9, @aujourdhui), 'PAYEUR BETA', 'L6VIR002', 'VIR', NULL, NULL, NULL, NULL),
    -- aujourd'hui a 0 h (borne basse retenue), une ventilation a 0 : AFaire (la V2 la met en rouge) ; pas de remise (-1)
    (1600403, 50, @aujourdhui, NULL, NULL, 'ESP', NULL, NULL, -1, NULL),
    -- ECARTE de l'ouverture : la veille a 23 h -- jumeau retenu 1600403 (le jour a 0 h)
    (1600406, 70, DATEADD(HOUR, -1, @aujourdhui), 'PAYEUR VEILLE', 'L6VEILLE', 'CHQ', NULL, NULL, NULL, NULL),
    -- ECARTE de l'ouverture : le lendemain a 0 h -- jumeau retenu 1600401 (le jour a 10 h)
    (1600407, 80, DATEADD(DAY, 1, @aujourdhui), 'PAYEUR DEMAIN', 'L6DEMAIN', 'CHQ', NULL, NULL, NULL, NULL),
    -- dates fixes, pour la recherche (PROC_RECHENC.recherches) : le 10/03/2025 ventile a CLI DELTA, le 11/03/2025 sans ventilation
    (1600404, 300, '20250310 14:00', 'PAYEUR DELTA', 'L6CHQ004', 'CHQ', 'BANQUE DELTA', NULL, 7, 'acompte'),
    (1600405, 1500, '20250311', 'PAYEUR OMEGA', 'L6REM005', 'VIR', NULL, NULL, NULL, NULL);
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_PYEUR, VENTI_ID_ENCMT, VENTI_ID_AFFRE, VENTI_ID_CLIEN, VENTI_MONTANT, VENTI_DATE_VENTILATION) VALUES
    (1600411, NULL, 1600401, 1600431, 1600421, 1000, @aujourdhui),
    (1600412, 1600441, 1600401, NULL, NULL, 200, @aujourdhui),
    (1600413, NULL, 1600403, NULL, 1600422, 0, @aujourdhui),
    (1600414, NULL, 1600404, 1600432, 1600423, 300, '20250310');
