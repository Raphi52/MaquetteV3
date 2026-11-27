-- Jeu de LecteurRetoursCfenet / EcrivainRetoursCfenet (« Retours CFE-NET », PROC_CFENET_RET), lot 15 du plan-676a4b19. Identifiants
-- 25003xx (retours, fichiers, actions, passages), dossiers 2500391-2500392. Ce qui est retenu ou ecarte, et pourquoi : .attendu, .gestes.
-- Types : ceux de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 27/11/2026), colonnes lues ; ACTION_RCS et DOSSIER_RCS sont
-- les fiches rec.ActionRegistre et rec.DossierRcs (REC-012). Aucun autre jeu ne cree ces tables v2.
IF OBJECT_ID(N'v2.CFE_LIASSE_XML') IS NULL
    CREATE TABLE v2.CFE_LIASSE_XML (LIAXML_ID_LIAXML int NOT NULL PRIMARY KEY, LIAXML_ID_LIAFILE int NULL, LIAXML_DATE_ENVOI datetime NULL,
        LIAXML_CFENETRET_DATE_RECEP_XML datetime NULL, LIAXML_CFENETRET_DATE_TRAITE datetime NULL, LIAXML_CFENETRET_ETAT varchar(20) NULL,
        LIAXML_CFENETRET_DETAIL varchar(max) NULL, LIAXML_CFENETRET_COR_GREF_DATE datetime NULL, LIAXML_CFENETRET_COR_GREF_ID_UTLST int NULL,
        LIAXML_CFENETRET_TRAITE_AMI_DATE datetime NULL, LIAXML_CFENETRET_TRAITE_AMI_ID_UTLST int NULL, LIAXML_CFENETRET_TRAITE_AMI_COM varchar(500) NULL);
IF OBJECT_ID(N'v2.CFE_LIASSE_XML_FILE') IS NULL
    CREATE TABLE v2.CFE_LIASSE_XML_FILE (LIAFILE_ID_LIAFILE int NOT NULL PRIMARY KEY, LIAFILE_ID_ACTRC int NULL);
IF OBJECT_ID(N'v2.PASSAGE_CFE') IS NULL
    CREATE TABLE v2.PASSAGE_CFE (PSSGC_ID_PSSCF int NOT NULL PRIMARY KEY, PSSGC_NUM_LIAISON varchar(14) NULL, PSSGC_DIRECT bit NULL,
        PSSGC_CCI bit NULL, PSSGC_VIDI_GUID_LIASSE1 uniqueidentifier NULL);
IF OBJECT_ID(N'v2.PASSAGE_CFE_COR') IS NULL
    CREATE TABLE v2.PASSAGE_CFE_COR (PSFCO_ID_PSFCO int NOT NULL PRIMARY KEY, PSFCO_ID_LIAFILE int NULL, PSFCO_NUM_LIAISON varchar(14) NULL);

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (2500391, '525003910', '', N'CFE NET ALPHA SARL', 'C', 0), (2500392, '525003920', '', N'CFE NET BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (2500391, 2500391, 'B1', '2026B25391', '525003910', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (2500392, 2500392, 'B1', '2026B25392', '525003920', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Un passage CFE, une action, un fichier XML par retour (meme numero). Le passage porte les drapeaux CCI / direct / deja numerise.
INSERT INTO v2.PASSAGE_CFE (PSSGC_ID_PSSCF, PSSGC_NUM_LIAISON, PSSGC_DIRECT, PSSGC_CCI, PSSGC_VIDI_GUID_LIASSE1) VALUES
    (2500301, 'C25030100001', 1, 1, NULL), (2500302, 'C25030200001', 1, 1, NULL), (2500303, 'C25030300001', 1, 1, NULL),
    (2500304, 'C25030400001', 1, 1, NULL), (2500305, 'C25030500001', 1, 1, '8E1C2D3F-0000-4000-8000-000000250305'),
    (2500306, 'C25030600001', 1, 1, NULL), (2500307, 'C25030700001', 1, 1, NULL), (2500308, 'C25030800001', 1, 1, NULL),
    (2500309, 'C25030900001', 1, 1, NULL),
    -- passage NON direct : hors du domaine de la V2 (PSSGC_DIRECT = 1, PROC_CFENET_RET.cs:74) -- jumeau 2500301
    (2500310, 'C25031000001', 0, 1, NULL), (2500311, 'C25031100001', 1, 1, NULL);
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdPassageCfe, CodeProcessus, EtatInfo, EtatAction, SurNotification)
SELECT n, CASE WHEN n % 2 = 1 THEN 2500391 ELSE 2500392 END, n, 'CFENET', 'C', 'V', 0
FROM (VALUES (2500301), (2500302), (2500303), (2500304), (2500305), (2500306), (2500307), (2500308), (2500309), (2500310), (2500311)) AS v (n);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
INSERT INTO v2.CFE_LIASSE_XML_FILE (LIAFILE_ID_LIAFILE, LIAFILE_ID_ACTRC)
SELECT n, n FROM (VALUES (2500301), (2500302), (2500303), (2500304), (2500305), (2500306), (2500307), (2500308), (2500309), (2500310), (2500311)) AS v (n);
-- 2500302 a une correspondance de passage : son numero de liaison est celui de la grille (CASE PSFCO, PROC_CFENET_RET.cs:47-50)
INSERT INTO v2.PASSAGE_CFE_COR (PSFCO_ID_PSFCO, PSFCO_ID_LIAFILE, PSFCO_NUM_LIAISON) VALUES (2500302, 2500302, 'C25030200099');

INSERT INTO v2.CFE_LIASSE_XML (LIAXML_ID_LIAXML, LIAXML_ID_LIAFILE, LIAXML_DATE_ENVOI, LIAXML_CFENETRET_DATE_RECEP_XML, LIAXML_CFENETRET_DATE_TRAITE,
    LIAXML_CFENETRET_ETAT, LIAXML_CFENETRET_DETAIL, LIAXML_CFENETRET_COR_GREF_DATE, LIAXML_CFENETRET_TRAITE_AMI_DATE) VALUES
    -- rejet definitif : retenu (« Erreurs » cochees a l'ouverture)
    (2500301, 2500301, DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -2, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), 'KoRejetDefinitif', 'Rejet definitif CCI', NULL, NULL),
    -- erreur fonctionnelle : retenue, liaison de la correspondance
    (2500302, 2500302, DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -2, @aujourdhui), 'KoFonctionnel', 'Code APE invalide', NULL, NULL),
    -- PDF absent, envoye il y a 3 jours (> 2, le defaut de CFENET_RET_NB_JOURS_PDF_ABSENT) : retenu
    (2500303, 2500303, DATEADD(DAY, -3, @aujourdhui), NULL, DATEADD(DAY, -3, @aujourdhui), 'KoPdfAbsent', NULL, NULL, NULL),
    -- ECARTE : PDF absent envoye il y a 2 jours (pas > 2) -- jumeau 2500303 (3 jours)
    (2500304, 2500304, DATEADD(DAY, -2, @aujourdhui), NULL, DATEADD(DAY, -2, @aujourdhui), 'KoPdfAbsent', NULL, NULL, NULL),
    -- ECARTE : PDF absent mais feuillet 1 deja numerise (« PDF deja numerise » decoche) -- jumeau 2500303
    (2500305, 2500305, DATEADD(DAY, -5, @aujourdhui), NULL, DATEADD(DAY, -5, @aujourdhui), 'KoPdfAbsent', NULL, NULL, NULL),
    -- ECARTE : traite auto par la CCI (« Traités » decoches) -- jumeau 2500301
    (2500306, 2500306, DATEADD(DAY, -10, @aujourdhui), NULL, DATEADD(DAY, -1, @aujourdhui), 'OkTraitementAuto', NULL, NULL, NULL),
    -- ECARTE : rejet doublon deja corrige par le greffe (masque a l'ouverture) -- jumeau 2500308 ; PROC_CFENET_RET.gestes le decorrige
    (2500307, 2500307, DATEADD(DAY, -20, @aujourdhui), NULL, DATEADD(DAY, -5, @aujourdhui), 'KoRejetDoublon', 'Doublon corrige', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- rejet doublon : retenu
    (2500308, 2500308, DATEADD(DAY, -20, @aujourdhui), NULL, DATEADD(DAY, -4, @aujourdhui), 'KoRejetDoublon', 'Liasse envoyée deux fois', NULL, NULL),
    -- ECARTE : traite par Amitel (masque a l'ouverture) -- jumeau 2500301
    (2500309, 2500309, DATEADD(DAY, -10, @aujourdhui), NULL, DATEADD(DAY, -1, @aujourdhui), 'KoRejetDefinitif', NULL, NULL, DATEADD(DAY, -1, @aujourdhui)),
    -- ECARTE : passage non direct (ci-dessus) -- jumeau 2500301
    (2500310, 2500310, DATEADD(DAY, -10, @aujourdhui), NULL, DATEADD(DAY, -1, @aujourdhui), 'KoRejetDefinitif', NULL, NULL, NULL),
    -- ECARTE : sans etat de retour -- jumeau 2500301
    (2500311, 2500311, DATEADD(DAY, -10, @aujourdhui), NULL, NULL, NULL, NULL, NULL, NULL);
