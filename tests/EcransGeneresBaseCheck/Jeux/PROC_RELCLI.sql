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

-- « Valider les soldes » (EcrivainRelevesClients, PROC_RELCLI.gestes). Des comptes aux dates de 1999-2000 : la validation porte sur TOUS
-- les comptes actifs du greffe (GetClientsPourValidationPeriode), ceux des autres jeux compris ; a la fin de periode du banc
-- (31/03/2000), ceux-ci n'ont rien a valider (leurs ecritures et soldes sont d'aujourd'hui). Identifiants 22072xx (clients),
-- 22074xx (ventilations), 220090x (encaissements : sous les numeros du jeu des rejets, qui attend MAX + 1 = 2204007) ; factures « F12V* ».
--   2207211 : dernier solde 01/11/1999 -50,00, factures et ventilations de nov. 1999 a fev. 2000 -> 3 soldes valides (01/12, 01/01, 01/02),
--             hors releve de factures : la validation ne filtre pas CLIEN_RELEVE_FACTURE (proc_relcli.md:159) ;
--   2207212 : coche, solde le plus ancien 01/03/2000 100,00 et des ecritures de dec. 1999 a fev. 2000 -> completion a rebours (3 soldes) ;
--   2207213 : la facture NON comptabilisee F12V005 du 14/02/2000, la piece bloquante -> limite 01/02/2000 ; sans solde, sa premiere
--             ecriture (F12V006 du 02/01/2000) le fait partir du 01/01/2000 : un solde au 01/02/2000, -1,00 ;
--   2207217 : ni solde, ni ecriture, modifie le 15/12/1999 -> depart 01/12/1999, soldes 0,00 (PROC_RELCLI.cs:325) ;
--   2207218 : ni solde, une ventilation et une facture de dec. 1999 -> depart 01/12/1999 (premiere ecriture), solde depuis 1900 ;
--   ECARTES de la validation (jumeau retenu 2207211, meme solde et memes dates) : 2207214 en sommeil, 2207215 compte PC, 2207216 type D.
-- Jumeaux des pieces bloquantes : F12V006 du 02/01/2000 comptabilisee, l'encaissement 2200902 du 03/01/2000 deja remis (ENCMT_REMIS),
-- l'encaissement 2200901 du 10/03/2000 non comptabilise mais posterieur a la facture bloquante.
-- Le droit comptable : la fonction EXPCOMPTA sur le role de l'agent du banc (PROC_RELCLI.cs:72, roles seuls) ; son utilisateur V2
-- (CLISO_ID_UTLST) : 2000901, le meme que PROC_SIGNJUD.sql.
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_COMPTABILISATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_COMPTABILISATION datetime NULL;
IF OBJECT_ID(N'v2.ENCAISSEMENT') IS NULL
    CREATE TABLE v2.ENCAISSEMENT (ENCMT_ID_ENCMT int NOT NULL PRIMARY KEY, ENCMT_MONTANT money NOT NULL, ENCMT_DATE_ENCAISSEMENT datetime NOT NULL,
        ENCMT_DESIGNATION_PAYEUR varchar(100) NULL, ENCMT_NUMERO_TITRE varchar(32) NULL, ENCMT_MODE_PAIEMENT varchar(8) NULL,
        ENCMT_DESIGNATION_BANQUE varchar(100) NULL, ENCMT_DATE_COMPTABILISATION datetime NULL, ENCMT_ID_UTLST int NULL,
        ENCMT_REMIS int NULL, ENCMT_COMMENTAIRE varchar(255) NULL);
EXEC sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = ''EXPCOMPTA'')
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES (''EXPCOMPTA'', ''Expert comptable'', ''V2'');
IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = ''BANC'' AND Fonction = ''EXPCOMPTA'')
    INSERT INTO soc.RoleFonction (Role, Fonction) VALUES (''BANC'', ''EXPCOMPTA'');
UPDATE soc.Compte SET IdV2 = 2000901 WHERE CompteWindows = ''banc.ecrans'' AND IdV2 IS NULL;
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, Statut, Groupe, ReleveFacture, EstComptePc, EstCompteWeb, DateDerniereModification) VALUES
    (2207211, ''L12VALA'', ''411L12VALA'', ''CLI VALIDATION A'', ''C'', ''A'', ''ST'', 0, 0, 0, ''19991105''),
    (2207212, ''L12VALB'', ''411L12VALB'', ''CLI COMPLETION B'', ''C'', ''A'', ''ST'', 1, 0, 0, ''19991105''),
    (2207213, ''L12VALF'', ''411L12VALF'', ''CLI PIECE BLOQUANTE'', ''C'', ''A'', ''ST'', 1, 0, 0, ''20000214''),
    (2207214, ''L12VALS'', ''411L12VALS'', ''CLI VALIDATION SOMMEIL'', ''C'', ''S'', ''ST'', 1, 0, 0, ''19991105''),
    (2207215, ''L12VALP'', ''411L12VALP'', ''CLI VALIDATION COMPTE PC'', ''C'', ''A'', ''ST'', 1, 1, 0, ''19991105''),
    (2207216, ''L12VALD'', ''411L12VALD'', ''CLI VALIDATION DIVERS'', ''D'', ''A'', ''ST'', 1, 0, 0, ''19991105''),
    (2207217, ''L12VALM'', ''411L12VALM'', ''CLI SANS ECRITURE'', ''C'', ''A'', ''ST'', 1, 0, 0, ''19991215''),
    (2207218, ''L12VALN'', ''411L12VALN'', ''CLI SANS SOLDE'', ''C'', ''A'', ''ST'', 1, 0, 0, ''19991105'');
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.CLIENT_SOLDE (CLISO_DATE_SOLDE, CLISO_MONTANT, CLISO_ID_CLIEN, CLISO_ID_UTLST) VALUES
    (''19991101'', -50.00, 2207211, 1), (''20000301'', 100.00, 2207212, 1),
    (''19991101'', -50.00, 2207214, 1), (''19991101'', -50.00, 2207215, 1), (''19991101'', -50.00, 2207216, 1);
INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_MONTANT_TTC, FACTU_DATE_CREATION, FACTU_ID_CLIEN, FACTU_DATE_COMPTABILISATION) VALUES
    (''F12V001'', ''D12VAL001'', 100.00, ''19991110'', 2207211, ''19991231''), (''F12V002'', ''D12VAL002'', 30.25, ''19991205'', 2207211, ''19991231''),
    (''F12V003'', ''D12VAL003'', 10.00, ''20000120'', 2207211, ''20000131''),
    -- apres la limite (01/02/2000) : ne compte pas
    (''F12V004'', ''D12VAL004'', 999.00, ''20000203'', 2207211, ''20000229''),
    (''F12V005'', ''D12VAL005'', 15.00, ''20000214'', 2207213, NULL),
    (''F12V006'', ''D12VAL006'', 1.00, ''20000102'', 2207213, ''20000131''),
    (''F12V007'', ''D12VAL007'', 20.00, ''20000110'', 2207212, ''20000131''), (''F12V008'', ''D12VAL008'', 5.55, ''20000215'', 2207212, ''20000229''),
    (''F12V009'', ''D12VAL009'', 20.00, ''19991220'', 2207218, ''19991231'');
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_CLIEN, VENTI_MONTANT, VENTI_DATE_VENTILATION) VALUES
    (2207401, 2207211, 40.00, ''19991115''), (2207402, 2207211, 60.50, ''20000105''),
    (2207403, 2207212, 30.00, ''19991220''), (2207404, 2207212, 12.00, ''20000201''),
    (2207405, 2207218, 50.00, ''19991210'');
INSERT INTO v2.ENCAISSEMENT (ENCMT_ID_ENCMT, ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_DESIGNATION_PAYEUR, ENCMT_MODE_PAIEMENT, ENCMT_REMIS) VALUES
    (2200901, 12.00, ''20000310'', ''PAYEUR VALIDATION'', ''L12VE'', NULL),
    (2200902, 8.00, ''20000103'', ''PAYEUR VALIDATION'', ''L12VE'', 7);
',
    N'@aujourdhui datetime', @aujourdhui;
