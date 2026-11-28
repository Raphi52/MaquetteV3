-- Jeu de LecteurBilansRejetes (« Bilans en rejet », PROC_REJETS_BILANS), lot 6 du plan-676a4b19. Identifiants 16007xx. Ce qui
-- est retenu ou ecarte, et pourquoi : PROC_REJETS_BILANS.attendu.
-- DEPOT_RCS et EXERCICE sont creees par le banc (Program.cs), ACTE_DEPOSE par PROC_ACTREJ_CMD_WEB.sql : les colonnes lues ici
-- leur sont ajoutees, aux types de RIG_DEV (SSDT, ACTE_DEPOSE.sql, DEPOT_RCS.sql, EXERCICE.sql). Ces actes
-- n'ont pas de flux de commande web (ACTDP_CMDWEB_FLUX_ID) : ils n'entrent pas dans « Actes web non transmis ».
IF OBJECT_ID(N'v2.ACTE_DEPOSE') IS NULL
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL);
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_NUM_ORDRE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_NUM_ORDRE int NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SAISIE_BILAN_CodeRejet') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SAISIE_BILAN_CodeRejet varchar(2) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SAISIE_BILAN_MotifRejet') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SAISIE_BILAN_MotifRejet varchar(255) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_SAISIE_BILAN_DateSaisieTerminee') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_SAISIE_BILAN_DateSaisieTerminee datetime NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE datetime NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DSRCS') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DSRCS int NULL;
-- Ce que « Envoyer vers XDCA » ecrit (EcrivainBilansRejetes, lot 3 du plan-f4fb097f) : l'affaire et la demande XDCA, aux types de
-- RIG_DEV (ceux de PROC_CORDJRCS.sql et PROC_DBE_RELANCE.sql), et le compteur des demandes (DMD) s'il manque.
IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_ID_UTLST_CREATION int NULL, AFFRE_ID_UTLST_EN_COURS int NULL,
        AFFRE_DATE_CREATION datetime NOT NULL, AFFRE_LIBELLE varchar(70) NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_PARAMETRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_PARAMETRE varchar(max) NULL;
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'DMD')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('DMD', 2026, 'LAQC', 2, 'D', 5, 1);

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1600701, '516007001', '', N'REJ ALPHA SAS', 'C', 0), (1600702, '516007002', '', N'REJ BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1600701, 1600701, 'B1', '2016B60701', '516007001', '20160101', 'C', 0, 0, '20160101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (1600702, 1600702, 'B1', '2016B60702', '516007002', '20160101', 'C', 0, 0, '20160101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1600711, 1600701, 1600721, 'BANCREJ', 'C', 'V', 0), (1600712, 1600702, 1600722, 'BANCREJ', 'C', 'V', 0),
    (1600713, 1600701, 1600723, 'BANCREJ', 'C', 'V', 0), (1600714, 1600702, 1600724, 'BANCREJ', 'C', 'V', 0),
    (1600715, 1600701, 1600725, 'BANCREJ', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
-- La correction XDCA : en cours sur l'action 1600712 (EnCours), validee sur 1600713 (jumelle : AFaire).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_ID_ACTRC, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS) VALUES
    (1600751, 1600702, 1600712, '26001600751', 'E', 'XDCA'), (1600752, 1600701, 1600713, '26001600752', 'V', 'XDCA');
-- La date de debut du greffe : les bilans dont la saisie s'est terminee avant le 01/03/2025 sont ignores. Le PARAMETRAGE de la
-- V2 est repris dans soc.Reglage (SOC-003, RepriseReglages.cs) : la base V3 n'a pas de v2.PARAMETRAGE.
-- fix-ok: mesure le 26/11/2026 -- lu dans v2.PARAMETRAGE, l'ecran tombait en erreur sur la copie de dev (« Invalid object name
-- 'v2.PARAMETRAGE' », journal du serveur 5096) alors que ce banc, qui creait la table, restait vert.
INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('VDCAREJ_DATE_DEBUT', 'Date de debut des bilans en rejet', '01/03/2025');

-- Les colonnes viennent peut-etre d'etre ajoutees : insertion compilee a part.
EXEC sp_executesql N'
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DSRCS) VALUES
    (1600731, ''20241231'', 1600701), (1600732, ''20241231'', 1600702), (1600733, ''20231231'', 1600701), (1600734, ''20221231'', 1600702);
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_ID_EXRCC, DPTRC_NUM_DEPOT, DPTRC_DATE) VALUES
    (1600721, 1600731, ''L6D0721'', ''20250402''), (1600722, 1600732, ''L6D0722'', ''20250405''), (1600723, 1600733, ''L6D0723'', ''20250301''),
    (1600724, 1600734, ''L6D0724'', ''20250201''),
    -- un depot sans exercice (des actes, pas un bilan)
    (1600725, NULL, ''L6D0725'', ''20250410'');
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_NUM_ORDRE, ACTDP_SAISIE_BILAN_CodeRejet,
    ACTDP_SAISIE_BILAN_MotifRejet, ACTDP_SAISIE_BILAN_DateSaisieTerminee) VALUES
    -- motif 3 (bilan incomplet) : « Renvoi possible », AFaire
    (1600741, 1600721, ''BILAN'', 1, ''3'', ''Bilan incomplet'', ''20250403''),
    -- motif 10, correction XDCA en cours : « Rejet de fond », EnCours
    (1600742, 1600722, ''BILAN'', 1, ''10'', ''Actif nul'', ''20250406''),
    -- ECARTE : motif 1 (incoherences a la source, pas un rejet : GetCodesMotifsRejet l''ecarte) -- jumeau 1600741 (motif 3)
    (1600743, 1600721, ''ANNEXE'', 2, ''1'', ''Incoherences'', ''20250403''),
    -- motif C7, saisie terminee le 01/03/2025 (borne de VDCAREJ_DATE_DEBUT retenue), correction validee : AFaire
    (1600744, 1600723, ''BILAN'', 1, ''C7'', ''Pages illisibles'', ''20250301''),
    -- ECARTE : saisie terminee le 28/02/2025, avant VDCAREJ_DATE_DEBUT -- jumeau 1600744 (le 01/03/2025)
    (1600746, 1600724, ''BILAN'', 1, ''16'', ''Bilan illisible'', ''20250228''),
    -- ECARTE : un depot sans exercice (INNER JOIN EXERCICE) -- jumeau 1600741
    (1600745, 1600725, ''ACTE'', 1, ''7'', ''Pages manquantes'', ''20250411'');';
