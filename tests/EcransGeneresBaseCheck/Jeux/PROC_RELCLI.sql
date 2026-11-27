-- Jeu de LecteurRelevesClients (« Releve de compte », PROC_RELCLI), lot 12 du plan-676a4b19. Identifiants 22072xx (clients rec.Client),
-- 22073xx (ventilations) ; factures « F12R* ». Types : RIG_DEV (SSDT CLIENT_SOLDE.sql, FACTURE.sql, VENTILATION.sql) ; CLIENT est la
-- fiche rec.Client (REC-013). FACTURE a la forme minimale des autres jeux (voir PROC_GINPI.sql). Le rompu (GRPAR_ROMPU) n'est pas pose
-- dans soc.ParametreGreffe : le lecteur prend 1,52 €, le DEFAULT de RIG_DEV.
IF OBJECT_ID(N'v2.CLIENT_SOLDE') IS NULL
    CREATE TABLE v2.CLIENT_SOLDE (CLISO_ID_CLISO int IDENTITY PRIMARY KEY, CLISO_DATE_SOLDE datetime NOT NULL, CLISO_MONTANT money NOT NULL,
        CLISO_ID_CLIEN int NOT NULL, CLISO_ID_UTLST int NOT NULL);
IF OBJECT_ID(N'v2.FACTURE') IS NULL
    CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NOT NULL);
IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TTC') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TTC money NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_CREATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_CREATION datetime NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_ID_CLIEN') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_ID_CLIEN int NULL;
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);

EXEC sp_executesql N'
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, Statut, Groupe, ReleveFacture, EstComptePc, EstCompteWeb) VALUES
    -- compte standard, debiteur de 150 (solde -50, +100 de ventilations, -200 de factures, apres le dernier solde)
    (2207201, ''L12RELA'', ''411L12RELA'', ''CLI RELEVE A'', ''C'', ''A'', ''ST'', 1, 0, 0),
    -- compte greffe (groupe GR), debiteur de 10
    (2207202, ''L12RELG'', ''411L12RELG'', ''GREFFE RELEVE G'', ''C'', ''A'', ''GR'', 1, 0, 0),
    -- ECARTE : debiteur de 1 seulement, sous le rompu de 1,52 -- jumeau retenu 2207202 (10)
    (2207203, ''L12RELB'', ''411L12RELB'', ''CLI RELEVE SOUS ROMPU'', ''C'', ''A'', ''ST'', 1, 0, 0),
    -- ECARTE : sans releve de factures (CLIEN_RELEVE_FACTURE = 0) -- jumeau retenu 2207201
    (2207204, ''L12RELN'', ''411L12RELN'', ''CLI SANS RELEVE'', ''C'', ''A'', ''ST'', 0, 0, 0),
    -- ECARTE : en sommeil (statut S) -- jumeau retenu 2207201
    (2207205, ''L12RELS'', ''411L12RELS'', ''CLI RELEVE SOMMEIL'', ''C'', ''S'', ''ST'', 1, 0, 0),
    -- ECARTE : compte web (hors Standard et Greffes, les deux types de l ouverture) -- jumeau retenu 2207201
    (2207206, ''L12RELW'', ''411L12RELW'', ''CLI RELEVE WEB'', ''C'', ''A'', ''ST'', 1, 0, 1),
    -- ECARTE : aucun solde valide (CLIENT_SOLDE, INNER JOIN) -- jumeau retenu 2207201
    (2207207, ''L12RELX'', ''411L12RELX'', ''CLI SANS SOLDE'', ''C'', ''A'', ''ST'', 1, 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.CLIENT_SOLDE (CLISO_DATE_SOLDE, CLISO_MONTANT, CLISO_ID_CLIEN, CLISO_ID_UTLST) VALUES
    -- le plus ancien n est pas le dernier : il ne compte pas
    (DATEADD(DAY, -120, @aujourdhui), -999.00, 2207201, 1), (DATEADD(DAY, -60, @aujourdhui), -50.00, 2207201, 1),
    (DATEADD(DAY, -60, @aujourdhui), -10.00, 2207202, 1), (DATEADD(DAY, -60, @aujourdhui), -1.00, 2207203, 1),
    (DATEADD(DAY, -60, @aujourdhui), -80.00, 2207204, 1), (DATEADD(DAY, -60, @aujourdhui), -80.00, 2207205, 1),
    (DATEADD(DAY, -60, @aujourdhui), -80.00, 2207206, 1);
INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_MONTANT_TTC, FACTU_DATE_CREATION, FACTU_ID_CLIEN) VALUES
    (''F12R001'', ''D12REL001'', 200.00, DATEADD(DAY, -30, @aujourdhui), 2207201),
    -- avant le dernier solde : deja compte dans le solde, il ne compte pas
    (''F12R002'', ''D12REL002'', 500.00, DATEADD(DAY, -90, @aujourdhui), 2207201);
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_CLIEN, VENTI_MONTANT, VENTI_DATE_VENTILATION) VALUES
    (2207301, 2207201, 100.00, DATEADD(DAY, -20, @aujourdhui));
',
    N'@aujourdhui datetime', @aujourdhui;
