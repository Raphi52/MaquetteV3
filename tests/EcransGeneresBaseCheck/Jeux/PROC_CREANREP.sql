-- Jeu de LecteurRepertoireCreanciers et EcrivainRepertoireCreanciers (« Representation », PROC_CREANREP : le repertoire des
-- creanciers), lot 12 du plan-676a4b19. Identifiants 22030xx (creanciers), 22031xx (adresses rec.Adresse). Types : RIG_DEV (SSDT
-- RigDatabase\dbo\Tables\CREANCIER_REPERTO.sql) ; l'adresse est la fiche reprise rec.Adresse (migration REC-012).
IF OBJECT_ID(N'v2.CREANCIER_REPERTO') IS NULL
    CREATE TABLE v2.CREANCIER_REPERTO (CRRPR_ID_CRRPR int NOT NULL PRIMARY KEY, CRRPR_ID_ADRBS int NULL, CRRPR_ABREGE varchar(16) NOT NULL,
        CRRPR_CIVILITE varchar(7) NOT NULL, CRRPR_NOM varchar(512) NULL, CRRPR_TEXTE_CREANCIER varchar(max) NULL, CRRPR_SUPPRIME bit NOT NULL,
        CRRPR_FORME_JURIDIQUE_UTI varchar(10) NULL, CRRPR_NUM_SIREN char(10) NULL, CRRPR_RCS_VILLE varchar(4) NULL, CRRPR_PRENOMS varchar(70) NULL,
        CRRPR_NOM_USAGE varchar(70) NULL);
SET IDENTITY_INSERT rec.Adresse ON;
INSERT INTO rec.Adresse (Id, NomVoie, CodePostal, Localite, EtatInfo) VALUES
    (2203101, '1 rue du Banc', '75001', 'PARIS', '1'), (2203102, '3 rue Haute', '21000', 'DIJON', '1');
SET IDENTITY_INSERT rec.Adresse OFF;
INSERT INTO v2.CREANCIER_REPERTO (CRRPR_ID_CRRPR, CRRPR_ID_ADRBS, CRRPR_ABREGE, CRRPR_CIVILITE, CRRPR_NOM, CRRPR_SUPPRIME, CRRPR_NUM_SIREN, CRRPR_RCS_VILLE,
    CRRPR_PRENOMS) VALUES
    -- personne morale, active, avec son adresse
    (2203001, 2203101, 'L12BQA', 'PM', 'BANQUE BANC ALPHA', 0, '912003001', '7501', NULL),
    -- personne physique DESACTIVEE : listee (desactives compris, OPE_SELECTION_CREANREP.cs:11), EnCours (elle se reactive)
    (2203002, 2203102, 'L12MART', 'M.', 'MARTIN BANC', 1, NULL, NULL, 'Paul'),
    -- personne morale sans adresse
    (2203003, NULL, 'L12CRB', 'PM', 'CREDIT BANC BETA', 0, NULL, NULL, NULL);
