-- Jeu de LecteurDiligencesRcs et EcrivainDiligencesRcs (« Diligences RCS », PROC_MDIL), lot 3 du plan « RIG v3 100 %
-- fonctionnel » (plan-676a4b19). Identifiants 1 313 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de
-- SQL-DEV\DEV, lu le 26/11/2026), creees si absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql).
-- MDIL lit TOUTES les diligences d'un dossier (DiligenceRcs.GetDiligencesRcsByDossier, sans filtre) : les diligences des jeux
-- des relances et injonctions (PROC_DBE_*, PROC_DCA_*) et de Program.cs sont aussi dans sa liste (PROC_MDIL.attendu les cite).
-- Les dossiers de ce jeu ont des numeros de gestion 2027B13xxx : ils passent en tete (numero decroissant).
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_ORIGINE_INFO') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_ORIGINE_INFO varchar(3) NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_MOTIF_DILIGENCE') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_MOTIF_DILIGENCE varchar(5) NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_DATE_REGULARISATION') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_DATE_REGULARISATION datetime NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_VALEUR_ATTENDUE') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_VALEUR_ATTENDUE varchar(max) NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_ID_DLGPA') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_ID_DLGPA int NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_TYPE_RELANCE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_TYPE_RELANCE varchar(16) NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DLGRC') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DLGRC int NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DSRCS') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DSRCS int NULL;
IF OBJECT_ID(N'v2.NATURE_DILIGENCE') IS NULL
    CREATE TABLE v2.NATURE_DILIGENCE (R_NATUREDILI_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_NATUREDILI_LIBELLE_ECRAN varchar(100) NULL);
IF OBJECT_ID(N'v2.ETAT_DILIGENCE') IS NULL
    CREATE TABLE v2.ETAT_DILIGENCE (R_ETATDILIGE_VALEUR varchar(1) NOT NULL PRIMARY KEY, R_ETATDILIGE_LIBELLE_ECRAN varchar(50) NULL);
IF OBJECT_ID(N'v2.ETAT_RELANCE') IS NULL
    CREATE TABLE v2.ETAT_RELANCE (R_ETATRELANC_VALEUR varchar(1) NOT NULL PRIMARY KEY, R_ETATRELANC_LIBELLE_ECRAN varchar(50) NULL);
IF OBJECT_ID(N'v2.DILIGENCE_RCS_PARAMETRE') IS NULL
    CREATE TABLE v2.DILIGENCE_RCS_PARAMETRE (DLGPA_ID_DLGPA int NOT NULL PRIMARY KEY, DLGPA_EDITIONS_DIFFEREES bit NULL, DLGPA_NON_DISTRIBUE bit NULL,
        DLGPA_ASTREINTE bit NULL, DLGPA_TAUX_ASTREINTE decimal(9, 2) NULL, DLGPA_NPAI bit NULL);

-- Les references, aux libelles de RIG_DEV pour les natures (NATURE_DILIGENCE lue le 26/11/2026). Les codes d'etat « Z » et
-- « R » sont propres a ce jeu : les diligences des autres jeux n'en prennent pas le libelle.
INSERT INTO v2.NATURE_DILIGENCE (R_NATUREDILI_VALEUR, R_NATUREDILI_LIBELLE_ECRAN) VALUES
    ('AIMPA', 'Alerte immatriculation A principale'), ('JMOAP', 'Injonction modification A principal'),
    ('RIMBS', 'Relance immatriculation B second');
INSERT INTO v2.ETAT_DILIGENCE (R_ETATDILIGE_VALEUR, R_ETATDILIGE_LIBELLE_ECRAN) VALUES ('Z', 'En cours du banc lot 3');
INSERT INTO v2.ETAT_RELANCE (R_ETATRELANC_VALEUR, R_ETATRELANC_LIBELLE_ECRAN) VALUES ('R', 'Relance du banc lot 3');
INSERT INTO v2.DILIGENCE_RCS_PARAMETRE (DLGPA_ID_DLGPA, DLGPA_EDITIONS_DIFFEREES, DLGPA_NON_DISTRIBUE, DLGPA_ASTREINTE, DLGPA_TAUX_ASTREINTE, DLGPA_NPAI)
    VALUES (1313301, 0, 1, 1, 12.50, 0);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant) et l'action de la diligence 1313101 ; l'action 1313003
-- n'a pas de dossier.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1313001, '513130001', '', N'MDIL ALPHA SARL', 'C', 0), (1313002, '513130002', '', N'MDIL BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2027B' + RIGHT(CAST(v.Id AS varchar(7)), 5), v.Siren, '20180101', 'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1313001, '513130001'), (1313002, '513130002')) AS v (Id, Siren);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1313001, 1313001, NULL, 'AIMPA', 'C', 'V', 0), (1313003, NULL, NULL, 'AIMPA', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- Les diligences. 1313101 par son action (dossier 1313001), avec ses parametres et deux relances : « Avec relance ».
-- 1313102 sans action, par un exercice (dossier 1313002, la branche EXERCICE de GetDiligencesRcsByDossier) : « Sans relance ».
-- 1313103 : son action n'a pas de dossier, aucun exercice : ecartee -- jumeau retenu : 1313101.
EXEC sp_executesql N'
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE,
    DLGRC_ORIGINE_INFO, DLGRC_MOTIF_DILIGENCE, DLGRC_DATE_REGULARISATION, DLGRC_VALEUR_ATTENDUE, DLGRC_ID_DLGPA) VALUES
    (1313101, 1313001, ''AIMPA'', ''Z'', ''R'', ''GRF'', ''M1'', NULL, ''Un extrait'', 1313301),
    (1313102, NULL, ''JMOAP'', ''Z'', NULL, NULL, NULL, ''20260115'', NULL, NULL),
    (1313103, 1313003, ''AIMPA'', ''Z'', NULL, NULL, NULL, NULL, NULL, NULL);
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DLGRC, EXRCC_ID_DSRCS) VALUES (1313401, ''20251231'', 1313102, 1313002);
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE, RLCRC_TYPE_RELANCE) VALUES
    (1313201, 1313101, 1, ''20260201'', ''20260301'', ''RIMM''), (1313202, 1313101, 2, ''20260305'', NULL, ''RIMM'');';
