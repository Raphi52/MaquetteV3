-- Jeu de LecteurRelancesBeneficiaires (« Relances beneficiaires », PROC_DBE_RELANCE), lot 1 du plan « brancher sur la
-- base les procedures encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11002xx. Ce qui est retenu ou
-- ecarte, et pourquoi : PROC_DBE_RELANCE.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), seulement les
-- colonnes lues ; colonnes manquantes ajoutees (NULL : la table a deja des lignes), lignes par sp_executesql (une colonne
-- ajoutee dans ce lot d'instructions n'existe pas encore quand SQL Server le compile).
-- Les diligences que ce lecteur ECARTE parce qu'elles sont a l'ecran « Injonctions beneficiaires » (injonction IDBE
-- avec instance, relance restee sans effet) sont dans PROC_DBE_INJ.sql : un jeu ne pose pas de ligne dans l'ecran d'un
-- autre. Ici, leurs jumeaux qui restent des relances : 206 (delai qui expire aujourd'hui), 207 (dossier radie), 208
-- (relance IDBE sans instance).
IF OBJECT_ID(N'v2.DILIGENCE_RCS') IS NULL
    CREATE TABLE v2.DILIGENCE_RCS (DLGRC_ID_DLGRC int NOT NULL PRIMARY KEY, DLGRC_ID_ACTRC int NULL,
        DLGRC_NATURE_DILIGENCE varchar(10) NOT NULL, DLGRC_ETAT_DILIGENCE varchar(1) NULL, DLGRC_ETAT_RELANCE varchar(1) NULL);
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_DATE_REGULARISATION') IS NULL
    ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_DATE_REGULARISATION datetime NULL;
IF OBJECT_ID(N'v2.RELANCE_RCS') IS NULL
    CREATE TABLE v2.RELANCE_RCS (RLCRC_ID_RLCRC int NOT NULL PRIMARY KEY, RLCRC_ID_DLGRC int NULL, RLCRC_NUM_ORDRE smallint NOT NULL,
        RLCRC_DATE_RELANCE datetime NOT NULL, RLCRC_DATE_LIMITE datetime NULL);
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_TYPE_RELANCE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_TYPE_RELANCE varchar(16) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_ID_INSTN') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_ID_INSTN int NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_FACTURE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_FACTURE bit NULL;

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant), 207 radie, et l'action de chaque diligence.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1100201, N'BENEF ALPHA SARL'), (1100202, N'BENEF BETA SAS'), (1100203, N'BENEF GAMMA SARL'),
             (1100204, N'BENEF DELTA SAS'), (1100205, N'BENEF EPSILON SARL'), (1100206, N'BENEF ZETA SAS'),
             (1100207, N'BENEF ETA SARL'), (1100208, N'BENEF THETA SAS'), (1100209, N'BENEF IOTA SARL'),
             (1100210, N'BENEF KAPPA SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20180101',
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, CASE WHEN v.Id = 1100207 THEN DATEADD(MONTH, -1, @aujourdhui) END
FROM (VALUES (1100201), (1100202), (1100203), (1100204), (1100205), (1100206), (1100207), (1100208), (1100209), (1100210)) AS v (Id);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT v.Id, v.Id, NULL, 'RDBE', 'C', 'V', 0
FROM (VALUES (1100201), (1100202), (1100203), (1100204), (1100205), (1100206), (1100207), (1100208), (1100209), (1100210)) AS v (Id);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_DATE_REGULARISATION) VALUES
    (1100201, 1100201, ''SDBE'', ''1'', ''1'', NULL),   -- « A faire 1ere relance » : a relancer
    (1100202, 1100202, ''SDBE'', ''1'', ''2'', NULL),   -- relance 1 envoyee, delai en cours
    (1100203, 1100203, ''SDBE'', ''1'', ''0'', NULL),   -- ouverte, aucun courrier encore
    (1100204, 1100204, ''SDBE'', ''9'', ''2'', DATEADD(DAY, -2, @aujourdhui)),  -- close regularisee
    (1100205, 1100205, ''SDBE'', ''8'', ''2'', NULL),   -- close non regularisee
    (1100206, 1100206, ''SDBE'', ''1'', ''2'', NULL),   -- delai qui expire AUJOURD''HUI : pas encore sans effet
    (1100207, 1100207, ''SDBE'', ''1'', ''2'', NULL),   -- delai expire mais dossier RADIE : reste une relance
    (1100208, 1100208, ''SDBE'', ''1'', ''4'', NULL),   -- relance IDBE SANS instance : reste une relance
    (1100209, 1100209, ''SASSOSCI'', ''1'', ''1'', NULL), -- autre surveillance : ecartee (jumeau de 201)
    -- etat relance 2 (« 1ere relance faite ») sans ligne RELANCE_RCS : relancee (l''etat fait foi) ; sans relance n° 1
    -- datee, elle n''est pas « sans effet » -- jumeau 203 (etat 0 : en cours)
    (1100210, 1100210, ''SDBE'', ''1'', ''2'', NULL);
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE,
    RLCRC_TYPE_RELANCE, RLCRC_ID_INSTN, RLCRC_FACTURE) VALUES
    (1100222, 1100202, 1, DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, 20, @aujourdhui), ''S'', NULL, 0),
    (1100224, 1100204, 1, DATEADD(DAY, -40, @aujourdhui), DATEADD(DAY, -10, @aujourdhui), ''S'', NULL, 1),
    (1100225, 1100205, 1, DATEADD(DAY, -300, @aujourdhui), DATEADD(DAY, -270, @aujourdhui), ''S'', NULL, 0),
    (1100226, 1100206, 1, DATEADD(DAY, -30, @aujourdhui), @aujourdhui, ''S'', NULL, 0),
    (1100227, 1100207, 1, DATEADD(DAY, -31, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), ''S'', NULL, 0),
    (1100228, 1100208, 1, DATEADD(DAY, -60, @aujourdhui), DATEADD(DAY, -30, @aujourdhui), ''S'', NULL, 0),
    (1100238, 1100208, 2, DATEADD(DAY, -5, @aujourdhui), NULL, ''IDBE'', NULL, 0);',
    N'@aujourdhui datetime', @aujourdhui;

-- ===== Lot 8 du plan « RIG v3 100 % fonctionnel » (26/11/2026) : LES DOSSIERS A RELANCER (relance 1 des beneficiaires,
-- LecteurRelancesBeneficiaires.DossierARelancerSql, EcrivainRelancesBeneficiaires). Identifiants 18002xx (lot 8). Colonnes et
-- types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 26/11/2026), seulement celles que la requete lit. Les tables
-- qu'un jeu PLUS LOIN dans l'ordre cree aussi le sont ici A L'IDENTIQUE de ce jeu (NATURE_DILIGENCE de PROC_MDIL.sql,
-- NATURE_DILIGENCE_PARAM et PARAMETRE_EDITION de PROC_DCA_RELANCE.sql) : leur IF OBJECT_ID les saute, leurs INSERT passent.
-- Les conditions dont un jumeau ecrirait dans une table que d'autres ecrans lisent SANS filtre (PARTIE : PROC_PARTIES ;
-- PROCEDURE_COLLECTIVE : PROC_MPC ; DEMANDE SRBE : PROC_SRBE_RAPPEL ; DEPOT_RCS : les ecrans de depots) ne sont pas posees ici :
-- la requete les lit, ce jeu ne les fait pas varier.
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_FORME_JURIDIQUE_UTI') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_FORME_JURIDIQUE_UTI varchar(10) NULL;
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_SOCI_COTEE') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_SOCI_COTEE bit NULL;
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_DATE_DISSOLUTION') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_DATE_DISSOLUTION datetime NULL;
IF OBJECT_ID(N'v2.FORME_JURIDIQUE_UTI') IS NULL
    CREATE TABLE v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_FORMEJURID_TYPE_BEN int NULL);
IF OBJECT_ID(N'v2.PAYS') IS NULL CREATE TABLE v2.PAYS (R_PAYS_VALEUR varchar(3) NOT NULL PRIMARY KEY, R_PAYS_CEE bit NULL);
IF OBJECT_ID(N'v2.ETABLISSEMENT') IS NULL
    CREATE TABLE v2.ETABLISSEMENT (ETBLS_ID_ETBLS int NOT NULL PRIMARY KEY, ETBLS_ID_ADRBS int NULL, ETBLS_ID_DSRCS int NULL,
        ETBLS_TYPE_ADRESSE varchar(10) NULL);
IF OBJECT_ID(N'v2.INSCRIPTION_RCS') IS NULL CREATE TABLE v2.INSCRIPTION_RCS (INSRC_ID_INSRC int NOT NULL PRIMARY KEY);
IF OBJECT_ID(N'v2.CODE_MENTION') IS NULL
    CREATE TABLE v2.CODE_MENTION (R_CODEMENTIO_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_CODEMENTIO_TYPE_MENTION varchar(1) NOT NULL);
IF OBJECT_ID(N'v2.DILIGENCE_RCS_EXCLUE') IS NULL
    CREATE TABLE v2.DILIGENCE_RCS_EXCLUE (DLGEX_ID_DLGEX int NOT NULL PRIMARY KEY, DLGEX_ID_DSRCS int NOT NULL,
        DLGEX_NATURE_DILIGENCE varchar(10) NOT NULL);
IF COL_LENGTH('v2.CODE_EVENEMENT_RCS', 'R_CODEEVENEM_ISBENEF') IS NULL ALTER TABLE v2.CODE_EVENEMENT_RCS ADD R_CODEEVENEM_ISBENEF bit NULL;
IF COL_LENGTH('v2.PARTIE', 'PRT_ID_DSRCS') IS NULL ALTER TABLE v2.PARTIE ADD PRT_ID_DSRCS int NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_CATEGORIE_DEPOT') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_CATEGORIE_DEPOT varchar(1) NULL;
-- Ce que l'ecrivain ecrit (diligence, relance, demandes) : les colonnes de RIG_DEV que le banc n'a pas encore a ce point.
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_ORIGINE_INFO') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_ORIGINE_INFO varchar(3) NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_MOTIF_DILIGENCE') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_MOTIF_DILIGENCE varchar(5) NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_ETAT_EDI') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_ETAT_EDI varchar(2) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_AUTEUR') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_AUTEUR varchar(1) NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_INTERVENANT') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_INTERVENANT varchar(1) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_LES_NUMEROS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LES_NUMEROS varchar(128) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_PARAMETRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_PARAMETRE varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_MERE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_MERE int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
-- Reglages, parametre d'edition et groupe de la nature (a l'identique de PROC_DCA_RELANCE.sql et PROC_MDIL.sql).
IF OBJECT_ID(N'v2.NATURE_DILIGENCE_PARAM') IS NULL
    CREATE TABLE v2.NATURE_DILIGENCE_PARAM (NADIP_ID_NADIP int NOT NULL PRIMARY KEY, NADIP_NATURE_DILIGENCE varchar(10) NOT NULL,
        NADIP_CODE_PROCESSUS varchar(64) NULL, NADIP_NUM_ORDRE int NULL, NADIP_CODE_PARAM varchar(128) NOT NULL,
        NADIP_VALEUR_PARAM varchar(max) NOT NULL);
IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
    CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
        PRMED_RECTO_VERSO bit NOT NULL DEFAULT 0, PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL DEFAULT 0, PRMED_CODE_CFECP varchar(50) NULL,
        PRMED_LISTE varchar(4000) NOT NULL DEFAULT '', PRMED_CODE_ENTITE_SOURCE varchar(16) NULL, PRMED_CODE_PROCESSUS varchar(40) NULL,
        PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NOT NULL DEFAULT 1,
        PRMED_READ_ONLY bit NOT NULL DEFAULT 0, PRMED_NUM_GROUPE_IMPRESSION varchar(30) NULL, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC varchar(50) NULL);
IF OBJECT_ID(N'v2.NATURE_DILIGENCE') IS NULL
    CREATE TABLE v2.NATURE_DILIGENCE (R_NATUREDILI_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_NATUREDILI_LIBELLE_ECRAN varchar(100) NULL);
IF COL_LENGTH('v2.NATURE_DILIGENCE', 'R_NATUREDILI_ID_NDILGRP') IS NULL ALTER TABLE v2.NATURE_DILIGENCE ADD R_NATUREDILI_ID_NDILGRP int NULL;
IF OBJECT_ID(N'v2.NATURE_DILIGENCE_GROUPE') IS NULL
    CREATE TABLE v2.NATURE_DILIGENCE_GROUPE (NDILGRP_ID_NDILGRP int NOT NULL PRIMARY KEY, NDILGRP_LIBELLE varchar(128) NULL);
-- Le code du groupe (NDILGRP_CODE varchar(50) dans RIG_DEV, lu le 27/11/2026 : 1 = « President ») : PROC_ORDOSIGN.sql, joue apres, l'ecrit.
IF COL_LENGTH('v2.NATURE_DILIGENCE_GROUPE', 'NDILGRP_CODE') IS NULL ALTER TABLE v2.NATURE_DILIGENCE_GROUPE ADD NDILGRP_CODE varchar(50) NULL;
-- Les compteurs RA1 (arrivee) et DMD (demandes) s'ils manquent (NUM-001), comme PROC_DAS.sql.
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'RA1')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES ('RA1', 2026, 'AC', 4, 6, 1);

-- Les dossiers. 201 (B1) et 202 (C1) sont a relancer. Chaque dossier ecarte a son jumeau retenu :
--   203 type A1 (TypeDossier « B,C,D ») -- jumeau 201 ; 204 forme juridique « a ne pas traiter » (TYPE_BEN 0) -- jumeau 201 ;
--   205 societe cotee -- jumeau 201 ; 206 siege dans l'Union europeenne (BEL) -- jumeau 207, siege hors Union (CHE), retenu ;
--   208 demande MC1 en attente de qualification (Q) -- jumeau 209, la meme validee (V), retenu ; 210 dissoute -- jumeau 201 ;
--   211 exclu de la nature SDBE (DILIGENCE_RCS_EXCLUE) -- jumeau 212, exclu de SDCA seulement, retenu ;
--   213 a une diligence SDBE en cours d'etat relance 1 (> 0) -- jumeau 214, etat relance 0, retenu (et sa diligence est listee) ;
--   215 radie, 216 EIRL, 217 en cessation -- jumeau 201.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '518002' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1800201, N'RBE UN SAS'), (1800202, N'RBE DEUX SARL'), (1800203, N'RBE TROIS SAS'), (1800204, N'RBE QUATRE SCI'),
             (1800205, N'RBE CINQ SA'), (1800206, N'RBE SIX SAS'), (1800207, N'RBE SEPT SAS'), (1800208, N'RBE HUIT SAS'),
             (1800209, N'RBE NEUF SAS'), (1800210, N'RBE DIX SAS'), (1800211, N'RBE ONZE SAS'), (1800212, N'RBE DOUZE SAS'),
             (1800213, N'RBE TREIZE SAS'), (1800214, N'RBE QUATORZE SAS'), (1800215, N'RBE QUINZE SAS'), (1800216, N'RBE SEIZE SAS'),
             (1800217, N'RBE DIX-SEPT SAS')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation, DateCessation)
SELECT v.Id, v.Id, v.TypeRcs, '2026B18' + RIGHT(CAST(v.Id AS varchar(7)), 3), '518002' + RIGHT(CAST(v.Id AS varchar(7)), 3), '20200101',
       'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, v.Eirl, 0,
       CASE WHEN v.Id = 1800215 THEN DATEADD(MONTH, -1, @aujourdhui) END, CASE WHEN v.Id = 1800217 THEN DATEADD(MONTH, -2, @aujourdhui) END
FROM (VALUES (1800201, 'B1', 0), (1800202, 'C1', 0), (1800203, 'A1', 0), (1800204, 'B1', 0), (1800205, 'B1', 0), (1800206, 'B1', 0),
             (1800207, 'B1', 0), (1800208, 'B1', 0), (1800209, 'B1', 0), (1800210, 'B1', 0), (1800211, 'B1', 0), (1800212, 'B1', 0),
             (1800213, 'B1', 0), (1800214, 'B1', 0), (1800215, 'B1', 0), (1800216, 'B1', 1), (1800217, 'B1', 0)) AS v (Id, TypeRcs, Eirl);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.Adresse ON;
INSERT INTO rec.Adresse (Id, NumeroVoie, ComplementNumero, TypeVoie, NomVoie, CodePostal, Localite, Pays, BoitePostale, Cedex, EtatInfo) VALUES
    (1800206, '1', NULL, NULL, 'Rue du Banc', '1000', 'BRUXELLES', 'BEL', NULL, NULL, 'C'),
    (1800207, '2', NULL, NULL, 'Rue du Banc', '1200', 'GENEVE', 'CHE', NULL, NULL, 'C');
SET IDENTITY_INSERT rec.Adresse OFF;
-- Les actions des deux diligences SDBE (213, 214).
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1800213, 1800213, NULL, 'SDBE', '1', '2', 0), (1800214, 1800214, NULL, 'SDBE', '1', '2', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

EXEC sp_executesql N'
-- Formes juridiques (TYPE_BEN : 1 societe, 0 a ne pas traiter) et pays (R_PAYS_CEE : 1 Union europeenne).
INSERT INTO v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR, R_FORMEJURID_TYPE_BEN) VALUES (''SAS18'', 1), (''SCI18'', 0);
INSERT INTO v2.PAYS (R_PAYS_VALEUR, R_PAYS_CEE)
SELECT v.p, v.c FROM (VALUES (''BEL'', 1), (''CHE'', 0)) AS v (p, c) WHERE NOT EXISTS (SELECT 1 FROM v2.PAYS WHERE R_PAYS_VALEUR = v.p);
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI, INSPM_SOCI_COTEE, INSPM_DATE_DISSOLUTION)
SELECT v.Id, CASE WHEN v.Id = 1800204 THEN ''SCI18'' ELSE ''SAS18'' END, CASE WHEN v.Id = 1800205 THEN 1 ELSE 0 END,
       CASE WHEN v.Id = 1800210 THEN DATEADD(MONTH, -3, @aujourdhui) END
FROM (VALUES (1800201), (1800202), (1800203), (1800204), (1800205), (1800206), (1800207), (1800208), (1800209), (1800210),
             (1800211), (1800212), (1800213), (1800214), (1800215), (1800216), (1800217)) AS v (Id);
INSERT INTO v2.ETABLISSEMENT (ETBLS_ID_ETBLS, ETBLS_ID_ADRBS, ETBLS_ID_DSRCS, ETBLS_TYPE_ADRESSE) VALUES
    (1800206, 1800206, 1800206, ''SPM''), (1800207, 1800207, 1800207, ''SPM'');
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION) VALUES
    (1800258, 1800208, ''2601800258'', ''Q'', ''MC1'', DATEADD(DAY, -3, @aujourdhui)),
    (1800259, 1800209, ''2601800259'', ''V'', ''MC1'', DATEADD(DAY, -3, @aujourdhui));
INSERT INTO v2.DILIGENCE_RCS_EXCLUE (DLGEX_ID_DLGEX, DLGEX_ID_DSRCS, DLGEX_NATURE_DILIGENCE) VALUES
    (1800261, 1800211, ''SDBE''), (1800262, 1800212, ''SDCA'');
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_ORIGINE_INFO, DLGRC_MOTIF_DILIGENCE) VALUES
    (1800213, 1800213, ''SDBE'', ''1'', ''1'', ''G'', ''NP''), (1800214, 1800214, ''SDBE'', ''1'', ''0'', ''G'', ''NP'');
-- Les reglages de RIG_DEV de la relance 1 SDBE (NATURE_DILIGENCE_PARAM ids 54, 55, 58 ; EnvoiEdi 0) et son parametre d''edition
-- (PARAMETRE_EDITION LOT2_REL_RDBE « Relance Greffe DBE », recto verso, FUSION_RIG), lus le 26/11/2026 ; le groupe de la nature
-- (NATURE_DILIGENCE.R_NATUREDILI_ID_NDILGRP 1, NATURE_DILIGENCE_GROUPE 1 « Surveillances Président »). Sans libelle de nature :
-- l''ecran « Diligences » (PROC_MDIL) montre alors le code, comme pour les autres SDBE du banc.
INSERT INTO v2.NATURE_DILIGENCE_PARAM (NADIP_ID_NADIP, NADIP_NATURE_DILIGENCE, NADIP_CODE_PROCESSUS, NADIP_NUM_ORDRE,
    NADIP_CODE_PARAM, NADIP_VALEUR_PARAM) VALUES
    (1800251, ''SDBE'', ''RSURV'', 1, ''CodeParamEdition'', ''LOT2_REL_RDBE''), (1800252, ''SDBE'', ''RSURV'', 1, ''DelaiRelance'', ''Mois=1''),
    (1800253, ''SDBE'', ''RSURV'', 1, ''FileImpression'', ''AMIRCSRBE''), (1800254, ''SDBE'', ''RSURV'', NULL, ''EnvoiEdi'', ''0'');
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_RECTO_VERSO, PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_CFECP,
    PRMED_TYPE_GENERATION) VALUES (''LOT2_REL_RDBE'', ''Relance Greffe DBE'', 1, 0, NULL, ''FUSION_RIG'');
INSERT INTO v2.NATURE_DILIGENCE (R_NATUREDILI_VALEUR, R_NATUREDILI_LIBELLE_ECRAN, R_NATUREDILI_ID_NDILGRP)
SELECT ''SDBE'', NULL, 1 WHERE NOT EXISTS (SELECT 1 FROM v2.NATURE_DILIGENCE WHERE R_NATUREDILI_VALEUR = ''SDBE'');
INSERT INTO v2.NATURE_DILIGENCE_GROUPE (NDILGRP_ID_NDILGRP, NDILGRP_CODE, NDILGRP_LIBELLE)
SELECT 1, ''President'', ''Surveillances Président'' WHERE NOT EXISTS (SELECT 1 FROM v2.NATURE_DILIGENCE_GROUPE WHERE NDILGRP_ID_NDILGRP = 1);',
    N'@aujourdhui datetime', @aujourdhui;
