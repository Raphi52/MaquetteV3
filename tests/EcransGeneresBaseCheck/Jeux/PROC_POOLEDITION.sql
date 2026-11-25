-- Jeu de LecteurFileEditions (« File des editions », PROC_POOLEDITION), lot 2 du plan « ecrans retrouves branches sur
-- la base ». Identifiants 2004xx. Les lignes attendues et pourquoi : PROC_POOLEDITION.attendu.
-- A l'ouverture, la V2 n'a aucun critere : toute demande d'edition est une ligne. Ce jeu couvre donc chaque phase,
-- chaque groupe et chaque facon de trouver le dossier, plutot que des refus (il n'y en a pas, hors des supprimees
-- qui sont dans une autre table, DEMANDE_EDITION_SUPPRIMEE, non lue). Un jeu d'un autre lot ne doit PAS ajouter de
-- ligne a v2.DEMANDE_EDITION : elle entrerait dans cet ecran.
-- v2.DEMANDE_EDITION aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
IF OBJECT_ID(N'v2.DEMANDE_EDITION') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_ID_DMND int NULL, DMNED_ID_DSRCS int NULL,
        DMNED_LOT varchar(255) NULL, DMNED_PHASE_TRAITEMENT varchar(10) NOT NULL, DMNED_DATE_PRE_EMISSION datetime NULL,
        DMNED_DESIGNATION_DESTINATAIRE varchar(255) NULL, DMNED_LIBELLE_DOCUMENT varchar(255) NOT NULL,
        DMNED_LIBELLE_ERREUR varchar(255) NULL, DMNED_DATE_CREATION datetime NULL, DMNED_ID_EMAIL_ENVOYE int NULL,
        DMNED_MODE_ENVOI int NULL);
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (200401, '512000401', '', N'EDITION ALPHA SARL', 'C', 0),
    (200402, '512000402', '', N'EDITION BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (200401, 200401, 'B1', '2020B40401', '512000401', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200402, 200402, 'B1', '2020B40402', '512000402', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les demandes qui portent les editions (code processus hors de ceux des autres lecteurs du banc).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION) VALUES
    (200421, 200401, '26000200421', 'V', 'BANCEDIT', DATEADD(DAY, -10, @aujourdhui)),
    (200422, 200402, '26000200422', 'V', 'BANCEDIT', DATEADD(DAY, -10, @aujourdhui));

-- Les demandes d'edition, une par jour en reculant (l'ordre de la V2 : la plus recente creee en tete).
INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_ID_DMND, DMNED_ID_DSRCS, DMNED_LOT, DMNED_PHASE_TRAITEMENT,
    DMNED_DATE_PRE_EMISSION, DMNED_DESIGNATION_DESTINATAIRE, DMNED_LIBELLE_DOCUMENT, DMNED_LIBELLE_ERREUR,
    DMNED_DATE_CREATION, DMNED_ID_EMAIL_ENVOYE, DMNED_MODE_ENVOI) VALUES
    -- phase initiale I : a emettre ; dossier par DMNED_ID_DSRCS ; courrier
    (200411, 200421, 200401, NULL, 'I', NULL, N'EDITION ALPHA SARL', N'Accusé de dépôt', NULL, DATEADD(HOUR, 10, @aujourdhui), NULL, 8),
    -- creee C, mode d'envoi Courriel (512) mais pas encore envoyee : courriel
    (200412, 200421, 200401, NULL, 'C', NULL, N'Maître Banc', N'Certificat de non-faillite', NULL, DATEADD(HOUR, -14, @aujourdhui), NULL, 512),
    -- generee, en traitement GT, rangee dans la file « LRAR » : file differee
    (200413, 200421, 200401, 'LRAR', 'GT', NULL, N'EDITION ALPHA SARL', N'Lettre de relance', NULL, DATEADD(HOUR, -38, @aujourdhui), NULL, 64),
    -- prete a imprimer P : courrier
    (200414, 200421, 200401, NULL, 'P', NULL, N'EDITION ALPHA SARL', N'Extrait Kbis', NULL, DATEADD(HOUR, -62, @aujourdhui), NULL, 8),
    -- emise E, courriel deja envoye (DMNED_ID_EMAIL_ENVOYE), mode d'envoi d'avant le drapeau (0) : courriel
    (200415, 200421, 200401, NULL, 'E', DATEADD(HOUR, -81, @aujourdhui), N'Maître Banc', N'Extrait Kbis', NULL, DATEADD(HOUR, -86, @aujourdhui), 77, 0),
    -- erreur a la creation CZ, avec son libelle
    (200416, 200421, 200401, NULL, 'CZ', NULL, N'EDITION ALPHA SARL', N'Accusé de dépôt', N'Adresse du destinataire absente', DATEADD(HOUR, -110, @aujourdhui), NULL, 8),
    -- erreur a la phase initiale IZ, sans libelle
    (200417, 200421, 200401, NULL, 'IZ', NULL, N'EDITION ALPHA SARL', N'Accusé de dépôt', NULL, DATEADD(HOUR, -134, @aujourdhui), NULL, 8),
    -- code de phase inconnu de l'ecran
    (200418, 200421, 200401, NULL, 'Q', NULL, N'EDITION ALPHA SARL', N'Accusé de dépôt', NULL, DATEADD(HOUR, -158, @aujourdhui), NULL, 8),
    -- emise E par lettre recommandee (64) ; dossier par sa demande (DMNED_ID_DSRCS vide)
    (200419, 200422, NULL, NULL, 'E', DATEADD(HOUR, -180, @aujourdhui), N'EDITION BETA SAS', N'Certificat', NULL, DATEADD(HOUR, -182, @aujourdhui), NULL, 64),
    -- emise, sans demande ni dossier, sans destinataire, sans date d'emission
    (200420, NULL, NULL, NULL, 'E', NULL, NULL, N'Statistique nantissement', NULL, DATEADD(HOUR, -206, @aujourdhui), NULL, NULL),
    -- creee, SANS date de creation (6 576 dans RIG_DEV) : en dernier (le NULL d'un tri decroissant), sans « creee le »
    (200410, NULL, NULL, NULL, 'C', NULL, N'EDITION SANS DATE', N'Courrier libre', NULL, NULL, NULL, 8);
