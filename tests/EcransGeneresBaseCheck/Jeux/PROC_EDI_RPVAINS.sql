-- Jeu de LecteurInscriptionsRpva (« EDI RPVA », PROC_EDI_RPVAINS), lot 3 du plan « brancher sur la base les procedures
-- encore en exemple des 5 ecrans » (plan-f7ba4de2). Ce qui est retenu ou ecarte, et pourquoi : PROC_EDI_RPVAINS.attendu.
--
-- Le LECTEUR est controle sur les demandes du jeu PROC_EDI_RPJC_INS.sql, joue avant celui-ci (ordre des noms) : « EDI RPVA » lit
-- la meme table que « EDI RPJC inscriptions » (v2.DEMAT_RPJC_INS, deux ecrans de la V2 sur les memes demandes), et ce jeu-la
-- porte deja chaque refus de ce filtre et son jumeau accepte (etat 1 contre etats 2, 4, 9 et sans etat ; Cnbf de structure ou non).
-- Les deux demandes 2500101-2500102 posees plus bas (lot 15, pour « Traiter la demande ») sont en attente : elles entrent dans
-- les deux listes, et leurs lignes sont dans PROC_EDI_RPVAINS.attendu ET PROC_EDI_RPJC_INS.attendu.
--
-- La table, si ce jeu passait seul : celle de PROC_EDI_RPJC_INS.sql, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de
-- SQL-DEV\DEV, lu le 25/11/2026).
IF OBJECT_ID(N'v2.DEMAT_RPJC_INS') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI int NOT NULL PRIMARY KEY, RPJCI_DATE_CREATION datetime NULL,
        RPJCI_ETAT varchar(2) NULL, RPJCI_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCI_DATE_TRAITEMENT_RIG datetime NULL,
        RPJCI_MOTIF_TRAITEMENT_RIG varchar(300) NULL, RPJCI_DATE_DEMANDE datetime NULL, RPJCI_TYPE_MAJ varchar(1) NULL,
        RPJCI_NUMERO_CNBF varchar(6) NULL, RPJCI_NOM varchar(50) NULL, RPJCI_PRENOM varchar(50) NULL,
        RPJCI_DENOMINATION varchar(200) NULL, RPJCI_USER_EN_COURS varchar(255) NULL, RPJCI_NUMERO_SIREN varchar(9) NULL,
        RPJCI_NUMERO_CNBF_STRUCTURE varchar(6) NULL);

-- Lot 15 du plan-676a4b19 : le REFUS (EcrivainInscriptionsRpva) ecrit aussi l'agent qui a traite (RPJCI_USER_TRAITEMENT_RIG,
-- DataProcDemandeInscr.cs:222), colonne de RIG_DEV absente de la table du jeu PROC_EDI_RPJC_INS.sql. Les demandes refusees par
-- PROC_EDI_RPVAINS.gestes sont celles de ce jeu (300101 a 300108).
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_USER_TRAITEMENT_RIG') IS NULL
    ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_USER_TRAITEMENT_RIG varchar(255) NULL;

-- Lot 15 du plan-676a4b19 : « Traiter la demande » (l'ACCEPTATION, EcrivainInscriptionsRpva). Ce qu'elle lit et ecrit, aux types de
-- la base V3 du greffe (INFORMATION_SCHEMA.COLUMNS de la copie de dev, lu le 27/11/2026) : les courriels, telephones et fax de la
-- demande, les mandataires (MANDATAIRE), la correspondance des CNBF de structure, les instances et leurs parties (AideInstances.cs).
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_MAIL') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_MAIL varchar(80) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_TELEPHONE') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_TELEPHONE varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_TELEPHONE2') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_TELEPHONE2 varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_TELEPHONE3') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_TELEPHONE3 varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_FAX') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_FAX varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_FAX2') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_FAX2 varchar(30) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_FAX3') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_FAX3 varchar(30) NULL;
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL,
        MNDTR_NUMERO_CNBF varchar(6) NULL);
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ABREGE varchar(16) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_NUMERO_CNBF_STRUCTURE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_NUMERO_CNBF_STRUCTURE varchar(6) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_SIREN') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_SIREN varchar(9) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ADRESSE_MAIL') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ADRESSE_MAIL varchar(70) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_TELEPHONE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_TELEPHONE varchar(15) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_FAX') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_FAX varchar(15) NULL;
IF OBJECT_ID(N'v2.CNBF_CORRESP_STRUCTURE') IS NULL
    CREATE TABLE v2.CNBF_CORRESP_STRUCTURE (CNBFC_ID_CNBFC int NOT NULL PRIMARY KEY, CNBFC_NUMERO_CNBF varchar(6) NULL,
        CNBFC_NUMERO_CNBF_STRUCTURE varchar(6) NULL, CNBFC_ID_RPJCI int NULL, CNBFC_SIREN varchar(9) NULL);
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF COL_LENGTH('v2.INSTANCE', 'INSTN_TOP_EXPERIAN') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TOP_EXPERIAN bit NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_MAJ_INFOG') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_MAJ_INFOG datetime NULL;
IF OBJECT_ID(N'v2.PARTIE') IS NULL
    CREATE TABLE v2.PARTIE (PRT_ID_PRT int NOT NULL PRIMARY KEY, PRT_ID_INSTN int NOT NULL, PRT_NUM_ORDRE smallint NOT NULL,
        PRT_CIVILITE varchar(7) NOT NULL, PRT_DESIGNATION varchar(512) NULL, PRT_QUALIF_PARTIE varchar(5) NOT NULL);
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_MNDTR_POS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_MNDTR_POS int NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_MNDTR_PLAI') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_MNDTR_PLAI int NULL;

-- (sp_executesql : un lot compile apres les ALTER ci-dessus, T-SQL verifiant les colonnes d'un lot avant de l'executer.)
-- 2500101 : la demande acceptee par PROC_EDI_RPVAINS.gestes -- en attente, une avocate et sa structure (SIREN et CNBF de
-- structure : la grille des structures existe), un courriel, un telephone, un fax. 2500102 : en attente, SIREN sans CNBF de
-- structure (la grille des structures n'existe pas), deux telephones (21 caracteres, plus que les 15 de MNDTR_TELEPHONE).
EXEC sp_executesql N'
INSERT INTO v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI, RPJCI_DATE_CREATION, RPJCI_ETAT, RPJCI_ETAT_TRAITEMENT_RIG, RPJCI_DATE_TRAITEMENT_RIG,
    RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_DATE_DEMANDE, RPJCI_TYPE_MAJ, RPJCI_NUMERO_CNBF, RPJCI_NOM, RPJCI_PRENOM, RPJCI_DENOMINATION,
    RPJCI_USER_EN_COURS, RPJCI_NUMERO_SIREN, RPJCI_NUMERO_CNBF_STRUCTURE, RPJCI_MAIL, RPJCI_TELEPHONE, RPJCI_TELEPHONE2,
    RPJCI_TELEPHONE3, RPJCI_FAX, RPJCI_FAX2, RPJCI_FAX3) VALUES
    (2500101, DATEADD(DAY, -1, @aujourdhui), ''1'', NULL, NULL, NULL, DATEADD(DAY, -1, @aujourdhui), ''I'', ''250001'', ''VERGER'',
     ''Anne'', ''CABINET VERGER'', NULL, ''512500101'', ''250901'', ''anne.verger@barreau.test'', ''0476250101'', NULL, NULL,
     ''0476250109'', NULL, NULL),
    (2500102, DATEADD(DAY, -1, @aujourdhui), ''1'', NULL, NULL, NULL, DATEADD(DAY, -1, @aujourdhui), ''I'', ''250002'', ''VIOLETTE'',
     ''Marc'', NULL, NULL, ''512500102'', NULL, NULL, ''0476250201'', ''0476250202'', NULL, NULL, NULL, NULL);',
    N'@aujourdhui datetime', @aujourdhui;

-- Les mandataires : 2500101 L15VER (le principal, actif, un AUTRE courriel : la liste des courriels a remplacer) ; 2500102 sans
-- abrege ni courriel (traite non principal : desactive, recoit le courriel du demandeur) ; 2500103 et 2500104 au meme abrege
-- L15DBL (2500103 inactif : pas principal ; 2500104 deja a un AUTRE CNBF : pas candidat) ; 2500105 L15STR, structure deja
-- principale (active, abrege, CNBF de structure) ; 2500106 la structure sans CNBF, candidate.
EXEC sp_executesql N'
INSERT INTO v2.MANDATAIRE (MNDTR_ID_MNDTR, MNDTR_NOM, MNDTR_ABREGE, MNDTR_ACTIF, MNDTR_NUMERO_CNBF, MNDTR_NUMERO_CNBF_STRUCTURE,
    MNDTR_SIREN, MNDTR_ADRESSE_MAIL, MNDTR_TELEPHONE, MNDTR_FAX) VALUES
    (2500101, ''VERGER Anne'', ''L15VER'', 1, NULL, NULL, NULL, ''verger@ancien.test'', NULL, NULL),
    (2500102, ''VERGER A.'', NULL, 1, NULL, NULL, NULL, NULL, NULL, NULL),
    (2500103, ''VERGER Anne-Marie'', ''L15DBL'', 0, NULL, NULL, NULL, NULL, NULL, NULL),
    (2500104, ''MARRON Paul'', ''L15DBL'', 1, ''250777'', NULL, NULL, NULL, NULL, NULL),
    (2500105, ''CABINET VERGER'', ''L15STR'', 1, NULL, ''250901'', ''512500101'', NULL, NULL, NULL),
    (2500106, ''CABINET VERGER SELARL'', NULL, 1, NULL, NULL, NULL, NULL, NULL, NULL);';

-- Les instances (AideInstances.cs:44-49) : 2500101 a une partie representee par 2500102 et une plaidee par 2500106, toutes deux
-- traitees -- elle repasse a recentraliser ; 2500102 a une partie representee par 2500104, jamais traite -- elle ne bouge pas.
EXEC sp_executesql N'
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_TOP_EXPERIAN, INSTN_DATE_MAJ_INFOG) VALUES
    (2500101, ''2026R25001'', ''VERGER RPVA'', 1, NULL),
    (2500102, ''2026R25002'', ''MARRON RPVA'', 1, NULL);
INSERT INTO v2.PARTIE (PRT_ID_PRT, PRT_ID_INSTN, PRT_NUM_ORDRE, PRT_CIVILITE, PRT_DESIGNATION, PRT_QUALIF_PARTIE, PRT_ID_MNDTR_POS,
    PRT_ID_MNDTR_PLAI) VALUES
    (2500101, 2500101, 1, ''PM'', ''L15 RPVA UN'', ''D'', 2500102, NULL),
    (2500102, 2500101, 2, ''PM'', ''L15 RPVA DEUX'', ''F'', NULL, 2500106),
    (2500103, 2500102, 1, ''PM'', ''L15 RPVA TROIS'', ''D'', 2500104, NULL);';
