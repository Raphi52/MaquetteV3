-- Jeu de LecteurDossierRcs (« Dossier RCS », PROC_VDOSSIER), lot 6 du plan-676a4b19. Identifiants 16010xx. Ce qui est retenu ou
-- ecarte, et pourquoi : PROC_VDOSSIER.attendu et .recherches.
-- La liste a l'ouverture : les dossiers mis a jour le jour du banc (DSSRC_DATE_MAJ). Les autres jeux posent leurs dossiers a des
-- dates passees (« 20160101 », « 20180101 »...) : ils n'y entrent pas. Ces dossiers n'ont ni relance, ni diligence, ni date
-- d'expiration de personne morale : aucune surveillance (LecteurRadiationsOffice) ne les retient.
-- Tables : rec.* des migrations ; DEPOT_RCS, EXERCICE, ACTE_DEPOSE, NON_INSCRIT, INSCRIT_PM, DEMANDE du banc et des jeux
-- precedents, leurs colonnes lues ajoutees si besoin ; DEBITEUR_END creee ici (SSDT, RigDatabase\dbo\Tables\DEBITEUR_END.sql).
IF OBJECT_ID(N'v2.DEBITEUR_END') IS NULL
    CREATE TABLE v2.DEBITEUR_END (DBEND_ID_DBEND int NOT NULL PRIMARY KEY, DBEND_ID_RPRTR int NULL, DBEND_ID_INSCR int NULL);
IF OBJECT_ID(N'v2.NON_INSCRIT') IS NULL
    CREATE TABLE v2.NON_INSCRIT (NINSC_ID_NINSC int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NUM_NON_INSCRIT') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NUM_NON_INSCRIT varchar(11) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NUM_SIREN') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NUM_SIREN varchar(9) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_DENOMINATION') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_DENOMINATION varchar(max) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NOM_USAGE') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NOM_USAGE varchar(70) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_PATRONYME') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_PATRONYME varchar(70) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_PRENOMS') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_PRENOMS varchar(62) NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_DATE_RADIATION') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_DATE_RADIATION datetime NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_ID_RPRTR') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_ID_RPRTR int NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_ID_ADRBS') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_ID_ADRBS int NULL;
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_PM') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_PM bit NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE datetime NULL;
IF OBJECT_ID(N'v2.ACTE_DEPOSE') IS NULL
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL);
IF OBJECT_ID(N'v2.BENEFICIAIRE_EFFECTIF') IS NULL
    CREATE TABLE v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF int NOT NULL PRIMARY KEY, BENEF_ID_DSRCS int NOT NULL, BENEF_SUPPRIME bit NOT NULL);

-- Les dossiers. 1601001-1601003 mis a jour le jour du banc ; 1601004 la veille a 23 h et 1601005 le lendemain a 0 h, ecartes.
SET IDENTITY_INSERT rec.Adresse ON;
INSERT INTO rec.Adresse (Id, NumeroVoie, TypeVoie, NomVoie, CodePostal, Localite, EtatInfo) VALUES (1601081, '12', 'RUE', 'DU BANC', '75001', 'PARIS', 'C');
SET IDENTITY_INSERT rec.Adresse OFF;
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, IdAdresse, Siren, Civilite, Denomination, EtatInfo, FicheSommaire, CodeNaf) VALUES
    (1601001, 1601081, '516010001', '', N'VDOSSIER DU JOUR SARL', 'C', 0, '6201Z'),
    (1601002, NULL, '516010002', '', N'VDOSSIER EN SOMMEIL SAS', 'C', 0, NULL),
    (1601003, NULL, '516010003', '', N'VDOSSIER RADIE SARL', 'C', 0, NULL),
    (1601004, NULL, '516010004', '', N'VDOSSIER VEILLE SARL', 'C', 0, NULL),
    (1601005, NULL, '516010005', '', N'VDOSSIER DEMAIN SARL', 'C', 0, NULL);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
    -- actif, mis a jour aujourd'hui a 10 h : AFaire « Actif »
    (1601001, 1601001, 'B1', '2016B61001', '516010001', '20160301', 'C', 0, 0, DATEADD(HOUR, 10, @aujourdhui), 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    -- provisoirement sans activite, mis a jour aujourd'hui a 0 h (borne basse retenue) : EnCours « Sans activité »
    (1601002, 1601002, 'B1', '2016B61002', '516010002', '20160302', 'C', 1, 0, @aujourdhui, 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    -- radie : Validee « Radié »
    (1601003, 1601003, 'B1', '2016B61003', '516010003', '20160303', 'C', 0, 0, DATEADD(HOUR, 8, @aujourdhui), 'A', 'C', 0, 0, 0, 0, 0, 0, 0, '20260101'),
    -- ECARTES de l'ouverture : la veille a 23 h -- jumeau 1601002 (le jour a 0 h) ; le lendemain a 0 h -- jumeau 1601001
    (1601004, 1601004, 'B1', '2016B61004', '516010004', '20160304', 'C', 0, 0, DATEADD(HOUR, -1, @aujourdhui), 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (1601005, 1601005, 'B1', '2016B61005', '516010005', '20160305', 'C', 0, 0, DATEADD(DAY, 1, @aujourdhui), 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL);
SET IDENTITY_INSERT rec.DossierRcs OFF;
-- Les documents de 1601001 : un depot d'actes (sans exercice) et un depot de bilan (exercice), par ses actions ; une inscription
-- de privilege sur son entreprise (DEBITEUR_END).
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification, NumeroArrivee, DateArrivee) VALUES
    (1601011, 1601001, 1601021, 'BANCVD', 'C', 'V', 0, 'A1601011', '20250310'),
    (1601012, 1601001, 1601022, 'BANCVD', 'C', 'V', 0, 'A1601012', '20250412');
SET IDENTITY_INSERT rec.ActionRegistre OFF;
SET IDENTITY_INSERT rec.Inscription ON;
INSERT INTO rec.Inscription (Id, NumeroInscription, TypeInscription, ParTransfert, DateInscription, Renouvelable, EtatInfo, DateDiffusion) VALUES
    (1601041, '2025N01041', 'NAN', 0, '20250501', 0, 'C', '20250501');
SET IDENTITY_INSERT rec.Inscription OFF;
INSERT INTO v2.DEBITEUR_END (DBEND_ID_DBEND, DBEND_ID_RPRTR, DBEND_ID_INSCR) VALUES (1601051, 1601001, 1601041);
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS) VALUES (1601001);
-- Une demande sur le dossier 1601001 (recherche par numero de demande : groupe « Demande »).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_ID_ACTRC, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS) VALUES
    (1601071, 1601001, NULL, '26001601071', 'E', 'BANCVD');

-- Les colonnes viennent peut-etre d'etre ajoutees : insertion compilee a part.
EXEC sp_executesql N'
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE) VALUES (1601031, ''20241231'');
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_ID_EXRCC, DPTRC_NUM_DEPOT, DPTRC_DATE) VALUES
    (1601021, NULL, ''L6D1021'', ''20250310''), (1601022, 1601031, ''L6D1022'', ''20250412'');
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE) VALUES
    (1601091, 1601021, ''STATUTS''), (1601092, 1601021, ''PV''), (1601093, 1601022, ''BILAN'');
-- Un non-inscrit (recherche par numero, ou par nom : groupe « Non-inscrit »).
INSERT INTO v2.NON_INSCRIT (NINSC_ID_NINSC, NINSC_NUM_NON_INSCRIT, NINSC_NUM_SIREN, NINSC_DENOMINATION, NINSC_PM) VALUES
    (1601061, ''N2016001061'', ''516010061'', ''VDOSSIER NON INSCRIT SARL'', 1);';
