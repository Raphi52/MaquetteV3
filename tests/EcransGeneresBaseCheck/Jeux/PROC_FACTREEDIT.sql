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
-- Lot 4 du plan-f4fb097f (28/11/2026), les gestes de PROC_FACTREEDIT : le droit d'executer le processus FACTREEDIT (FORM_ACCUEIL.cs:1614-1624,
-- soc.Droit), fonction du domaine Comptabilite (soc.Fonction de RIG_DEV : « Réédition des factures pour un client et une période donnée », DLL, Comptabilite = 1), sur le role BANC
-- de l'agent du banc ; et un agent SANS ce droit (banc.l4.sansdroit, role BANC_L4 sans fonction, admis comme lui), pour le refus. Les
-- colonnes de la file des editions que l'etat ecrit (EditionsEtatsV2.cs) et que les controles du .gestes relisent.
IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = 'FACTREEDIT')
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2, Comptabilite) VALUES ('FACTREEDIT', N'Réédition des factures pour un client et une période donnée', 'DLL', 1);
IF NOT EXISTS (SELECT 1 FROM soc.RoleFonction WHERE Role = 'BANC' AND Fonction = 'FACTREEDIT') INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'FACTREEDIT');
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
-- La reedition (EcrivainRefacturation.cs) choisit l'edition de FACTURE par sa maquette (DemandeEdition.isFacture, FACT_[0-9]*_IMP,
-- DemandeEdition.cs:2008) : les editions FACT du jeu portent celle de RIG_DEV (FACT_3_IMP, 91 editions lues le 28/11/2026), le courrier
-- de F12E004 une maquette de courrier (sa facture n'a donc AUCUNE edition de facture : le refus de PROC_FACTREEDIT.gestes) ; la copie
-- porte l'edition d'origine dans DMNED_ID_DMNED_REEDITEE (int NULL dans RIG_DEV).
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_ID_DMNED_REEDITEE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_ID_DMNED_REEDITEE int NULL;
EXEC sp_executesql N'UPDATE v2.DEMANDE_EDITION SET DMNED_CODE_MAQUETTE = CASE DMNED_CODE_PROCESSUS WHEN ''FACT'' THEN ''FACT_3_IMP'' ELSE ''COURRIER_LIBRE'' END
    WHERE DMNED_ID_DMNED BETWEEN 2208501 AND 2208506;';
