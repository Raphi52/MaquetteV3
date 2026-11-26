-- Jeu de LecteurRetentionActes (« Retention d'actes », PROC_ACTRET), lot 8 du plan « RIG v3 100 % fonctionnel ». Identifiants
-- 18011xx (lot 8 : a partir de 1800000). Ce qui est retenu ou ecarte, et pourquoi : PROC_ACTRET.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (sys.columns de la copie de dev, 26/11/2026), seulement les colonnes lues ; v2.DEPOT_RCS
-- existe deja dans le banc, v2.ACTE_DEPOSE est cree par PROC_ACTREJ.sql ou PROC_ACTREJ_CMD_WEB.sql (avant ce jeu). Si ce jeu cree
-- v2.CATEGORIE_DEPOT, il y met aussi la colonne que PROC_DASBEN.sql insere sans l'ajouter.
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DATE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DATE datetime NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_CATEGORIE_DEPOT') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_CATEGORIE_DEPOT varchar(1) NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_DIFFUSABILITE_CONFIDENTIELLE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_DIFFUSABILITE_CONFIDENTIELLE bit NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_RESULTAT_CONFIDENTIEL') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_RESULTAT_CONFIDENTIEL bit NULL;
IF COL_LENGTH('v2.DEPOT_RCS', 'DPTRC_NUM_DEPOT_REMPLACE') IS NULL ALTER TABLE v2.DEPOT_RCS ADD DPTRC_NUM_DEPOT_REMPLACE varchar(12) NULL;
IF OBJECT_ID(N'v2.CATEGORIE_DEPOT') IS NULL
    CREATE TABLE v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR varchar(3) NOT NULL PRIMARY KEY, R_CATEGORIED_TYPE_NUM_DEPOT varchar(5) NULL);
IF COL_LENGTH('v2.CATEGORIE_DEPOT', 'R_CATEGORIED_COMPTES_ANNUEL') IS NULL ALTER TABLE v2.CATEGORIE_DEPOT ADD R_CATEGORIED_COMPTES_ANNUEL bit NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_GUID_VIDIGED') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_GUID_VIDIGED uniqueidentifier NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Etat_GEIDE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Etat_GEIDE varchar(2) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Image_ATransferer') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Image_ATransferer bit NULL;
-- Les lignes passent par sp_executesql : une colonne ajoutee ci-dessus n'existe pas encore quand SQL Server compile le lot.
EXEC sp_executesql N'
-- Deux categories : « K », comptes annuels (comme B, G, J, K de RIG_DEV) ; « Y », un depot hors comptes annuels (un acte BEN, que
-- la V2 ne met pas en retention, Metier.cs:96).
INSERT INTO v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR, R_CATEGORIED_COMPTES_ANNUEL)
SELECT ''K'', 1 WHERE NOT EXISTS (SELECT 1 FROM v2.CATEGORIE_DEPOT WHERE R_CATEGORIED_VALEUR = ''K'');
INSERT INTO v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR, R_CATEGORIED_COMPTES_ANNUEL)
SELECT ''Y'', 0 WHERE NOT EXISTS (SELECT 1 FROM v2.CATEGORIE_DEPOT WHERE R_CATEGORIED_VALEUR = ''Y'');

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1801101, ''518011010'', '''', N''RETENTION ALPHA SARL'', ''C'', 0),
    (1801102, ''518011020'', '''', N''RETENTION BETA SAS'', ''C'', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1801101, 1801101, ''B1'', ''2026B81101'', ''518011010'', ''20200101'', ''C'', 0, 0, ''20200101'', ''A'', ''C'', 0, 0, 0, 0, 0, 0, 0),
    (1801102, 1801102, ''B1'', ''2026B81102'', ''518011020'', ''20200101'', ''C'', 0, 0, ''20200101'', ''A'', ''C'', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les depots (numero, date, categorie, bilan confidentiel, resultat confidentiel, depot remplace).
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_NUM_DEPOT, DPTRC_DATE, DPTRC_CATEGORIE_DEPOT, DPTRC_DIFFUSABILITE_CONFIDENTIELLE,
    DPTRC_RESULTAT_CONFIDENTIEL, DPTRC_NUM_DEPOT_REMPLACE) VALUES
    (1801111, ''T1801111'', DATEADD(DAY, -1, @aujourdhui), ''K'', 1, 0, NULL),
    (1801112, ''T1801112'', DATEADD(DAY, -2, @aujourdhui), ''K'', 1, 0, NULL),
    (1801113, ''T1801113'', DATEADD(DAY, -3, @aujourdhui), ''K'', 1, 0, NULL),
    (1801114, ''T1801114'', DATEADD(DAY, -4, @aujourdhui), ''K'', 1, 0, NULL),
    (1801115, ''T1801115'', DATEADD(DAY, -5, @aujourdhui), ''K'', 0, 1, NULL),
    (1801116, ''T1801116'', DATEADD(DAY, -6, @aujourdhui), ''K'', 1, 0, ''T1801199''),
    (1801117, ''T1801117'', DATEADD(DAY, -7, @aujourdhui), ''Y'', 1, 0, NULL),
    (1801118, ''T1801118'', DATEADD(DAY, -8, @aujourdhui), ''K'', 1, 0, NULL),
    (1801119, ''T1801119'', DATEADD(DAY, -9, @aujourdhui), ''K'', 0, 0, NULL);
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1801121, 1801101, 1801111, ''DEPOT'', ''C'', ''V'', 0), (1801122, 1801101, 1801112, ''DEPOT'', ''C'', ''V'', 0),
    (1801123, 1801101, 1801113, ''DEPOT'', ''C'', ''V'', 0), (1801124, 1801101, 1801114, ''DEPOT'', ''C'', ''V'', 0),
    (1801125, 1801102, 1801115, ''DEPOT'', ''C'', ''V'', 0), (1801126, 1801101, 1801116, ''DEPOT'', ''C'', ''V'', 0),
    (1801127, 1801101, 1801117, ''DEPOT'', ''C'', ''V'', 0), (1801128, 1801101, 1801118, ''DEPOT'', ''C'', ''V'', 0),
    (1801129, 1801101, 1801119, ''DEPOT'', ''C'', ''V'', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- Les pieces. Une piece PRETE : etat GEIDE 1, numerisee (GUID VIDIGED), image a transferer (Metier.cs:129-132).
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_GUID_VIDIGED, ACTDP_EDI_INFOGREFFE_Etat_GEIDE,
    ACTDP_EDI_INFOGREFFE_Image_ATransferer) VALUES
    -- 1801111, bilan confidentiel : bilan pret, declaration de confidentialite ABSENTE (1 piece prete sur 2) : retenu
    (1801131, 1801111, ''BILA'', NEWID(), ''1'', 1),
    -- 1801112 : bilan pret, declaration presente mais NON NUMERISEE : retenu
    (1801132, 1801112, ''BILA'', NEWID(), ''1'', 1), (1801133, 1801112, ''CONFI'', NULL, ''1'', 1),
    -- 1801113 : bilan pret, declaration numerisee mais etat GEIDE 0 (« marquee a ne pas envoyer ») : retenu
    (1801134, 1801113, ''BILA'', NEWID(), ''1'', 1), (1801135, 1801113, ''CONFI'', NEWID(), ''0'', 1),
    -- 1801114 : bilan et declaration prets (2 sur 2) : ecarte, il n''est plus retenu -- jumeau retenu 1801111
    (1801136, 1801114, ''BILA'', NEWID(), ''1'', 1), (1801137, 1801114, ''CONFI'', NEWID(), ''1'', 1),
    -- 1801115, compte de resultat confidentiel : bilan et declaration prets, compte de resultat ABSENT (2 sur 3) : retenu
    (1801138, 1801115, ''BILA'', NEWID(), ''1'', 1), (1801139, 1801115, ''CONFI'', NEWID(), ''1'', 1),
    -- 1801116 : comme 1801111, mais depot remplace : ecarte (DPTRC_NUM_DEPOT_REMPLACE)
    (1801140, 1801116, ''BILA'', NEWID(), ''1'', 1),
    -- 1801117 : comme 1801111, mais categorie hors comptes annuels : ecarte (R_CATEGORIED_COMPTES_ANNUEL)
    (1801141, 1801117, ''BILA'', NEWID(), ''1'', 1),
    -- 1801118 : aucune piece prete (bilan non numerise) : ecarte (le EXISTS de la V2) -- jumeau retenu 1801112
    (1801142, 1801118, ''BILA'', NULL, ''1'', 1),
    -- 1801119 : ni bilan ni resultat confidentiels : ecarte, jamais retenu
    (1801143, 1801119, ''BILA'', NEWID(), ''1'', 1);
',
    N'@aujourdhui datetime', @aujourdhui;
