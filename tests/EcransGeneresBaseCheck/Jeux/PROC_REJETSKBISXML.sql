-- Jeu de LecteurKbisXmlRejetes (« Kbis XML rejetes », PROC_REJETSKBISXML), lot 2 du plan « ecrans retrouves branches
-- sur la base ». Identifiants 2002xx. Ce qui est retenu ou ecarte, et pourquoi : PROC_REJETSKBISXML.attendu.
-- v2.KBIS_XML aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
IF OBJECT_ID(N'v2.KBIS_XML') IS NULL
    CREATE TABLE v2.KBIS_XML (KBIS_ID_KBIS int NOT NULL PRIMARY KEY, KBIS_ID_DSRCS int NOT NULL, KBIS_ID_DMND int NULL,
        KBIS_LIBELLE_ERREUR varchar(max) NULL, KBIS_CATEGORIE_ERREUR varchar(2) NULL, KBIS_IS_LAST bit NOT NULL,
        KBIS_DATE_TRANSMISSION datetime NULL, KBIS_DATE_RETOUR datetime NULL);

-- Les cases a l'ouverture, par les reglages de la V2 lus « == "1" » (FrmRejetsKbisXml.cs:76-104) :
--   KBISXML_ERR_BLOQ_DEFAULT   absent -> la V2 coche « Bloquantes » ;
--   KBISXML_ERR_NONBLOQ_DEFAULT '1'   -> « Avertissements » cochee ;
--   KBISXML_RADIES_DEFAULT     'OUI'  -> pas « 1 » : « Radies » decochee (la regle EgalUn, pas une conversion) ;
--   KBISXML_VIVANTS_DEFAULT    absent -> « Vivants » cochee.
INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES
    ('KBISXML_ERR_NONBLOQ_DEFAULT', 'Banc EcransGeneresBase lot 2', '1'),
    ('KBISXML_RADIES_DEFAULT', 'Banc EcransGeneresBase lot 2', 'OUI');

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant) ; 200203 est radie.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (200201, '512000201', '', N'KBIS ALPHA SARL', 'C', 0),
    (200202, '512000202', '', N'KBIS BETA SAS', 'C', 0),
    (200203, '512000203', '', N'KBIS GAMMA SARL', 'C', 0),
    (200204, '512000204', '', N'KBIS DELTA SAS', 'C', 0),
    (200205, '512000205', '', N'KBIS EPSILON SARL', 'C', 0),
    (200206, '512000206', '', N'KBIS ZETA SAS', 'C', 0),
    (200207, '512000207', '', N'KBIS ETA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
    (200201, 200201, 'B1', '2020B20201', '512000201', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (200202, 200202, 'B1', '2020B20202', '512000202', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (200203, 200203, 'B1', '2020B20203', '512000203', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, DATEADD(MONTH, -2, @aujourdhui)),
    (200204, 200204, 'B1', '2020B20204', '512000204', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (200205, 200205, 'B1', '2020B20205', '512000205', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (200206, 200206, 'B1', '2020B20206', '512000206', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (200207, 200207, 'B1', '2020B20207', '512000207', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les Kbis XML : transmis a 20 h, retour a 23 h (les heures de RIG_DEV).
INSERT INTO v2.KBIS_XML (KBIS_ID_KBIS, KBIS_ID_DSRCS, KBIS_LIBELLE_ERREUR, KBIS_CATEGORIE_ERREUR, KBIS_IS_LAST,
    KBIS_DATE_TRANSMISSION, KBIS_DATE_RETOUR) VALUES
    -- bloquante, dernier Kbis, dossier vivant : retenu ; libelle sur deux lignes comme dans RIG_DEV (la 1re est gardee)
    (200211, 200201, N'La date doit-être obligatoirement inférieure à la date du jour.' + CHAR(13) + CHAR(10)
        + N'(Bloquant : La date du dépôt d''actes numéro A2026/000211 est invalide)', 'B', 1,
        DATEADD(HOUR, -100, @aujourdhui), DATEADD(HOUR, -97, @aujourdhui)),
    -- le Kbis precedent du meme dossier (KBIS_IS_LAST = 0) : ecarte -- jumeau retenu 200211
    (200213, 200201, N'Ancien rejet du même dossier', 'B', 0, DATEADD(DAY, -30, @aujourdhui), DATEADD(DAY, -30, @aujourdhui)),
    -- avertissement (reglage a « 1 ») : retenu
    (200212, 200202, N'Le NIC existe déjà sur un autre établissement de ce dossier.', 'A', 1,
        DATEADD(HOUR, -52, @aujourdhui), DATEADD(HOUR, -49, @aujourdhui)),
    -- bloquante d'un dossier RADIE (reglage « OUI ») : ecarte -- jumeau retenu 200211 (vivant)
    (200214, 200203, N'Rejet d''un dossier radié', 'B', 1, DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui)),
    -- information et historique : jamais a l'ouverture -- jumeau retenu 200212 (avertissement)
    (200215, 200204, N'Information seulement', 'I', 1, DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui)),
    (200216, 200205, N'Historique seulement', 'H', 1, DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui)),
    -- rejet annule (categorie vide, libelle NULL : tsmiAnnulerRejet_Click) : ecarte
    (200217, 200206, NULL, '', 1, DATEADD(DAY, -3, @aujourdhui), DATEADD(DAY, -3, @aujourdhui)),
    -- bloquante revenue hier : avant 200211 (retour plus recent)
    (200218, 200207, N'Le numéro SIRET(NIC) de l''établissement n''est pas valide.', 'B', 1,
        DATEADD(HOUR, -28, @aujourdhui), DATEADD(HOUR, -25, @aujourdhui));
