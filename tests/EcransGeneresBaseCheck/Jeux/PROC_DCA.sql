-- Jeu de EcrivainComptesAnnuels (« Comptes annuels », PROC_DCA : « Valider le dépôt »), lot 16 du plan « RIG v3 100 % fonctionnel »
-- (plan-676a4b19). Identifiants 2 601 xxx (lot 16 : a partir de 2 600 000). Les gestes : PROC_DCA.gestes ; les lignes de l'ecran :
-- PROC_DCA.attendu (ce jeu n'ajoute AUCUNE demande DCA : les lignes sont celles du banc, section 1a, et des jeux PROC_DCA_*).
-- Les tables, aux types de RIG v2 (Source\BaseDeDonnées\RigDatabase\dbo\Tables\EXERCICE.sql, DEPOT_RCS.sql, ACTE_DEPOSE.sql,
-- DILIGENCE_RCS.sql, RELANCE_RCS.sql, INSCRIT_PM.sql, FORME_JURIDIQUE_UTI.sql) : le banc les cree en partie (section 1a), les jeux
-- PROC_DAS, PROC_BE et PROC_DCA_RELANCE leur ajoutent des colonnes ; celles de ce lot sont ajoutees ici, nullables (des lignes
-- d'autres jeux n'en ont pas). Les lignes passent par sp_executesql : une colonne ajoutee dans ce lot d'instructions n'existe pas
-- encore quand SQL Server le compile.
-- Hors des autres ecrans : limite de depot nulle (Relances comptes annuels lit « limite < aujourd'hui »), aucune relance d'ordre 1
-- (Injonctions comptes annuels), la relance IDCA sans instance (la liste des injonctions lit RLCRC_ID_INSTN non nul), diligences
-- SDCA (pas celles des radiations d'office), aucune forme juridique a DCA obligatoire.
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DSRCS') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DSRCS int NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_NUM_ORDRE') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_NUM_ORDRE smallint NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_TYPE_EXERCICE') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_TYPE_EXERCICE varchar(1) NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_DATE_LIMITE_AGO') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_DATE_LIMITE_AGO datetime NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_LIMITE_DEPOT') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_LIMITE_DEPOT datetime NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_DEPOSE') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_DEPOSE bit NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ETAT_DCA') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ETAT_DCA varchar(2) NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ETAT_INFO') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ETAT_INFO varchar(1) NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DLGRC') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DLGRC int NULL;
IF COL_LENGTH('v2.EXERCICE', 'EXRCC_ID_DMND_RECLA') IS NULL ALTER TABLE v2.EXERCICE ADD EXRCC_ID_DMND_RECLA int NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE_MAJ_INTERG') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE_MAJ_INTERG datetime NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE_MAJ_INFOG') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE_MAJ_INFOG datetime NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE_MODIF_FLAG_TRANS') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE_MODIF_FLAG_TRANS datetime NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_NUM_DEPOT_ANNULE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_NUM_DEPOT_ANNULE varchar(12) NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_NUM_DEPOT_REMPLACE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_NUM_DEPOT_REMPLACE varchar(12) NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DIFFUSABILITE_CONFIDENTIELLE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DIFFUSABILITE_CONFIDENTIELLE bit NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_RESULTAT_CONFIDENTIEL') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_RESULTAT_CONFIDENTIEL bit NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_NOMBRE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_NOMBRE varchar(1) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_COMMANDABLE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_COMMANDABLE bit NULL;
IF COL_LENGTH('v2.DILIGENCE_RCS', 'DLGRC_DATE_REGULARISATION') IS NULL ALTER TABLE v2.DILIGENCE_RCS ADD DLGRC_DATE_REGULARISATION datetime NULL;
IF COL_LENGTH('v2.RELANCE_RCS', 'RLCRC_TYPE_RELANCE') IS NULL ALTER TABLE v2.RELANCE_RCS ADD RLCRC_TYPE_RELANCE varchar(16) NULL;
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_FORME_JURIDIQUE_UTI') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_FORME_JURIDIQUE_UTI varchar(10) NULL;
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_DELAI_TENUE_AGO') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_DELAI_TENUE_AGO smallint NULL;
IF OBJECT_ID(N'v2.FORME_JURIDIQUE_UTI') IS NULL
    CREATE TABLE v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_FORMEJURID_TYPE_BEN int NULL);
IF COL_LENGTH('v2.FORME_JURIDIQUE_UTI', 'R_FORMEJURID_DELAI_TENUE_AGO_VARIABLE') IS NULL
    ALTER TABLE v2.FORME_JURIDIQUE_UTI ADD R_FORMEJURID_DELAI_TENUE_AGO_VARIABLE bit NULL;

-- Les compteurs (num.Compteur, NUM-001) s'ils manquent : RDC, la serie des depots de comptes d'un dossier B1 a D2 (ActionRcs.cs:194-206),
-- lettre C pour la distinguer dans les messages (B est celle de RDB, PROC_DASBEN.sql) ; RA1 et DMD (PROC_DAS.sql les pose deja).
EXEC sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''RDC'')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES (''RDC'', 2026, ''ALC'', 4, ''C'', 6, 1);
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''RA1'')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES (''RA1'', 2026, ''AC'', 4, 6, 1);
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''DMD'')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES (''DMD'', 2026, ''LAQC'', 2, ''D'', 5, 1);';

-- Les cinq dossiers B1 (personnes morales), leur entreprise (meme identifiant) :
--   2601001 ALPHA   : exercice 2601101 clos le 31/12/2025 NON depose, sa diligence SDCA 2601201 (sans injonction), et un ancien DCA
--                     2601301 (2025B26301) que le nouveau depot annule ; exercice 2601102 clos le 31/01/2026 non depose (a +/- 2 mois) ;
--   2601002 BETA    : exercice 2601111 clos le 31/12/2024 DEPOSE, dernier DCA 2601302 ni confidentiel ni a resultat confidentiel ;
--   2601003 GAMMA   : exercice 2601121 clos le 31/12/2024 DEPOSE, dernier DCA 2601303 a bilan confidentiel ;
--   2601004 DELTA   : exercice 2601131 clos le 31/12/2025 non depose, sous injonction (relance IDCA 2601251 de sa diligence 2601202) ;
--   2601005 EPSILON : aucun exercice ; inscrit PM de forme juridique SAS26 a delai de tenue variable, delai 9 mois (type BE nul :
--                     hors des ecrans des beneficiaires, qui lisent R_FORMEJURID_TYPE_BEN <> 0).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (2601001, '526010001', '', N'DCA26 ALPHA SARL', 'C', 0), (2601002, '526010002', '', N'DCA26 BETA SAS', 'C', 0),
    (2601003, '526010003', '', N'DCA26 GAMMA SAS', 'C', 0), (2601004, '526010004', '', N'DCA26 DELTA SARL', 'C', 0),
    (2601005, '526010005', '', N'DCA26 EPSILON SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2001B26' + RIGHT(CAST(v.Id AS varchar(7)), 3), v.Siren, '20010101', 'C', 0, 0, '20010101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (2601001, '526010001'), (2601002, '526010002'), (2601003, '526010003'), (2601004, '526010004'),
             (2601005, '526010005')) AS v (Id, Siren);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, NumeroArrivee, DateArrivee, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (2601401, 2601001, 2601301, '2025000401', '20250301', 'DCA', '2', '2', 0),
    (2601402, 2601002, 2601302, '2025000402', '20250601', 'DCA', '2', '2', 0),
    (2601403, 2601003, 2601303, '2025000403', '20250601', 'DCA', '2', '2', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
EXEC sp_executesql N'
INSERT INTO v2.DILIGENCE_RCS (DLGRC_ID_DLGRC, DLGRC_ID_ACTRC, DLGRC_NATURE_DILIGENCE, DLGRC_ETAT_DILIGENCE, DLGRC_ETAT_RELANCE) VALUES
    (2601201, NULL, ''SDCA'', ''1'', ''1''), (2601202, NULL, ''SDCA'', ''1'', ''3'');
INSERT INTO v2.RELANCE_RCS (RLCRC_ID_RLCRC, RLCRC_ID_DLGRC, RLCRC_NUM_ORDRE, RLCRC_DATE_RELANCE, RLCRC_DATE_LIMITE, RLCRC_TYPE_RELANCE) VALUES
    (2601251, 2601202, 3, ''20260601'', NULL, ''IDCA'');
INSERT INTO v2.EXERCICE (EXRCC_ID_EXRCC, EXRCC_DATE_CLOTURE, EXRCC_ID_DSRCS, EXRCC_NUM_ORDRE, EXRCC_TYPE_EXERCICE, EXRCC_DEPOSE, EXRCC_ETAT_DCA,
    EXRCC_ETAT_INFO, EXRCC_ID_DLGRC, EXRCC_ID_DMND_RECLA) VALUES
    (2601101, ''20251231'', 2601001, 1, ''2'', 0, ''00'', ''1'', 2601201, NULL),
    (2601102, ''20260131'', 2601001, 1, ''2'', 0, ''00'', ''1'', NULL, NULL),
    (2601111, ''20241231'', 2601002, 1, ''1'', 1, ''00'', ''1'', NULL, NULL),
    (2601121, ''20241231'', 2601003, 1, ''1'', 1, ''00'', ''1'', NULL, NULL),
    (2601131, ''20251231'', 2601004, 1, ''2'', 0, ''00'', ''1'', 2601202, NULL);
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_ID_EXRCC, DPTRC_NUM_DEPOT, DPTRC_DATE, DPTRC_CATEGORIE_DEPOT, DPTRC_DIFFUSABILITE_CONFIDENTIELLE,
    DPTRC_RESULTAT_CONFIDENTIEL) VALUES
    (2601301, 2601101, ''2025B26301'', ''20250301'', ''B'', 0, 0),
    (2601302, 2601111, ''2025B26302'', ''20250601'', ''B'', 0, 0),
    (2601303, 2601121, ''2025B26303'', ''20250601'', ''B'', 1, 0);
INSERT INTO v2.EVENEMENT_RCS (EVNRC_ID_EVNRC, EVNRC_ID_ACTRC, EVNRC_NUM_ORDRE, EVNRC_CODE_EVENEMENT_RCS, EVNRC_OBJET_DEPOT) VALUES
    (2601501, 2601401, 1, ''DCA'', 1), (2601502, 2601402, 1, ''DCA'', 1), (2601503, 2601403, 1, ''DCA'', 1);
IF NOT EXISTS (SELECT 1 FROM v2.FORME_JURIDIQUE_UTI WHERE R_FORMEJURID_VALEUR = ''SAS26'')
    INSERT INTO v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR, R_FORMEJURID_TYPE_BEN, R_FORMEJURID_DELAI_TENUE_AGO_VARIABLE) VALUES (''SAS26'', NULL, 1);
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI, INSPM_DELAI_TENUE_AGO) VALUES (2601005, ''SAS26'', 9);';
