-- Jeu de LecteurRefacturation (« Refacturation », PROC_FACTREEDIT), lot 12 du plan-676a4b19. Identifiants 22082xx (client rec.Client),
-- 22084xx (demandes), 22085xx (demandes d'edition) ; factures « F12E* ». Types : RIG_DEV (SSDT FACTURE.sql, DEMANDE.sql,
-- DEMANDE_EDITION.sql) ; FACTURE a la forme minimale des autres jeux (voir PROC_GINPI.sql), DEMANDE_EDITION celle de PROC_COURRIERM.sql.
-- Les demandes d'edition d'ici entrent aussi dans « File des éditions » (toutes les demandes d'edition) : son .attendu les porte.
IF OBJECT_ID(N'v2.FACTURE') IS NULL
    CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NOT NULL);
IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TTC') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TTC money NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_CREATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_CREATION datetime NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_ID_CLIEN') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_ID_CLIEN int NULL;
IF OBJECT_ID(N'v2.DEMANDE_EDITION') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_ID_DMND int NULL, DMNED_ID_DSRCS int NULL,
        DMNED_LOT varchar(255) NULL, DMNED_PHASE_TRAITEMENT varchar(10) NOT NULL, DMNED_DATE_PRE_EMISSION datetime NULL,
        DMNED_DESIGNATION_DESTINATAIRE varchar(255) NULL, DMNED_LIBELLE_DOCUMENT varchar(255) NOT NULL,
        DMNED_LIBELLE_ERREUR varchar(255) NULL, DMNED_DATE_CREATION datetime NULL, DMNED_ID_EMAIL_ENVOYE int NULL,
        DMNED_MODE_ENVOI int NULL);
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_PROCESSUS') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_PROCESSUS varchar(40) NULL;

EXEC sp_executesql N'
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES (2208201, ''L12FACT'', ''411L12FACT'', ''CLI REFACTURATION'', ''C'', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION) VALUES
    (2208401, ''D12FACT001'', ''V'', ''IMM'', DATEADD(DAY, -10, @aujourdhui)), (2208402, ''D12FACT002'', ''V'', ''IMM'', DATEADD(DAY, -5, @aujourdhui)),
    (2208403, ''D12FACT003'', ''V'', ''IMM'', DATEADD(DAY, -3, @aujourdhui)), (2208404, ''D12FACT004'', ''V'', ''IMM'', DATEADD(DAY, -2, @aujourdhui)),
    (2208405, ''D12FACT005'', ''V'', ''IMM'', DATEADD(DAY, 1, @aujourdhui));
INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_MONTANT_TTC, FACTU_DATE_CREATION, FACTU_ID_CLIEN) VALUES
    -- editee (FACT, phase E) : retenue
    (''F12E001'', ''D12FACT001'', 125.00, DATEADD(DAY, -10, @aujourdhui), 2208201),
    -- deux editions FACT (GT et P) : retenue UNE fois
    (''F12E002'', ''D12FACT002'', 80.40, DATEADD(DAY, -5, @aujourdhui), 2208201),
    -- ECARTEE : son edition est encore a la phase A (pas aboutie, OPE_REVIEW.cs:82) -- jumeau retenu F12E001 (phase E)
    (''F12E003'', ''D12FACT003'', 10.00, DATEADD(DAY, -3, @aujourdhui), 2208201),
    -- ECARTEE : son edition n est pas du processus FACT (:81) -- jumeau retenu F12E001
    (''F12E004'', ''D12FACT004'', 15.00, DATEADD(DAY, -2, @aujourdhui), 2208201),
    -- ECARTEE : datee de demain, apres la fin de periode (maintenant, :80) -- jumeau retenu F12E002
    (''F12E005'', ''D12FACT005'', 20.00, DATEADD(DAY, 1, @aujourdhui), 2208201);
INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_ID_DMND, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_DATE_CREATION, DMNED_CODE_PROCESSUS) VALUES
    (2208501, 2208401, ''E'', ''Facture F12E001'', ''20000101'', ''FACT''),
    (2208502, 2208402, ''GT'', ''Facture F12E002'', ''20000101'', ''FACT''), (2208503, 2208402, ''P'', ''Facture F12E002 (copie)'', ''20000101'', ''FACT''),
    (2208504, 2208403, ''A'', ''Facture F12E003'', ''20000101'', ''FACT''),
    (2208505, 2208404, ''E'', ''Courrier F12E004'', ''20000101'', ''COUR''),
    (2208506, 2208405, ''E'', ''Facture F12E005'', ''20000101'', ''FACT'');
',
    N'@aujourdhui datetime', @aujourdhui;
