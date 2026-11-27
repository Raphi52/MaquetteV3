-- Jeu de LecteurChiffresDossier (« Chiffres d'un dossier », PROC_VCHIFFRES), lot 6 du plan-676a4b19. Identifiants 16009xx. Ce
-- qui est retenu ou ecarte, et pourquoi : PROC_VCHIFFRES.attendu et .recherches. Aucun autre jeu ne pose de chiffres seuils ;
-- EXERCICE, DEPOT_RCS et ACTE_DEPOSE sont celles du banc et des jeux precedents, leurs colonnes lues ajoutees si besoin.
-- Types : ceux de RIG_DEV (SSDT, RigDatabase\dbo\Tables\CHIFFRES_SEUILS.sql, EXERCICE.sql).
IF OBJECT_ID(N'v2.CHIFFRES_SEUILS') IS NULL
    CREATE TABLE v2.CHIFFRES_SEUILS (CHPSL_ID_CHPSL int NOT NULL PRIMARY KEY, CHPSL_DATE_PROFIL datetime NOT NULL, CHPSL_DUREE varchar(2) NOT NULL,
        CHPSL_MONNAIE varchar(3) NOT NULL, CHPSL_ACTIF money NULL, CHPSL_CHIFFRE_AFFAIRE money NULL, CHPSL_RESULTAT money NULL,
        CHPSL_N_SALARIES int NULL, CHPSL_TOTAL_CAPITAUX_PROPRES money NULL, CHPSL_CAPITAL_SOCIAL money NULL);
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DSRCS') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DSRCS int NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_CHPSL') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_CHPSL int NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE datetime NULL;
IF OBJECT_ID(N'v2.ACTE_DEPOSE') IS NULL
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL);
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_NUM_ORDRE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_NUM_ORDRE int NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SAISIE_BILAN_CodeRejet') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SAISIE_BILAN_CodeRejet varchar(2) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SAISIE_BILAN_MotifRejet') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SAISIE_BILAN_MotifRejet varchar(255) NULL;

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1600901, '516009001', '', N'CHIFFRES ALPHA SARL', 'C', 0), (1600902, '516009002', '', N'CHIFFRES SANS SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1600901, 1600901, 'B1', '2016B60901', '516009001', '20160101', 'C', 0, 0, '20160101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (1600902, 1600902, 'B1', '2016B60902', '516009002', '20160101', 'C', 0, 0, '20160101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;
INSERT INTO v2.CHIFFRES_SEUILS (CHPSL_ID_CHPSL, CHPSL_DATE_PROFIL, CHPSL_DUREE, CHPSL_MONNAIE, CHPSL_ACTIF, CHPSL_CHIFFRE_AFFAIRE, CHPSL_RESULTAT,
    CHPSL_N_SALARIES, CHPSL_TOTAL_CAPITAUX_PROPRES, CHPSL_CAPITAL_SOCIAL) VALUES
    (1600921, '20241231', '12', 'EUR', 80000, 120000, 15000, 4, 40000, 10000),
    (1600922, '20231231', '12', 'EUR', 70000, 98000, -2500, 1, 25000, 10000);

-- Les colonnes viennent peut-etre d'etre ajoutees : insertion compilee a part.
EXEC sp_executesql N'
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DSRCS, EXRCC_ID_CHPSL) VALUES
    -- 2024, chiffre : Validee
    (1600911, ''20241231'', 1600901, 1600921),
    -- 2023, chiffre, son bilan rejete (une piece du depot de l''exercice porte un code de rejet) : EnCours
    (1600912, ''20231231'', 1600901, 1600922),
    -- 2022, sans chiffres seuils : la ligne « - » de la V2, AFaire
    (1600913, ''20221231'', 1600901, NULL),
    -- un dossier sans aucun exercice chiffre : retrouve par sa recherche (PROC_VCHIFFRES.recherches)
    (1600914, ''20241231'', 1600902, NULL);
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_ID_EXRCC, DPTRC_NUM_DEPOT, DPTRC_DATE) VALUES (1600931, 1600912, ''L6D0931'', ''20240615'');
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_NUM_ORDRE, ACTDP_SAISIE_BILAN_CodeRejet,
    ACTDP_SAISIE_BILAN_MotifRejet) VALUES (1600941, 1600931, ''BILAN'', 1, ''3'', ''Bilan incomplet'');';
