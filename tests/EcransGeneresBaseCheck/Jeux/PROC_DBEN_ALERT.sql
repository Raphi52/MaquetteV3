-- Jeu de LecteurDivergencesBen (« Divergences BEN », PROC_DBEN_ALERT), lot 8 du plan « RIG v3 100 % fonctionnel ». Identifiants
-- 18013xx (lot 8 : a partir de 1800000). Ce qui est retenu ou ecarte, et pourquoi : PROC_DBEN_ALERT.attendu.
-- Les tables V2 lues, aux types de RIG_DEV (sys.columns de la copie de dev, 26/11/2026), seulement les colonnes lues.
-- PROC_BEN.sql, joue avant (ordre des noms), cree deja DIVERGENCE et DIVERGENCE_DETAIL aux types de RIG_DEV : memes definitions
-- ici ; sa divergence ouverte 1600131 entre aussi a cet ecran (PROC_DBEN_ALERT.attendu).
IF OBJECT_ID(N'v2.DIVERGENCE') IS NULL
    CREATE TABLE v2.DIVERGENCE (DIVER_ID_DIVER int NOT NULL PRIMARY KEY, DIVER_ID_ACTRC int NOT NULL, DIVER_ID_DSRCS int NOT NULL,
        DIVER_DEMANDEUR varchar(max) NOT NULL, DIVER_DATE_CREATION datetime NULL, DIVER_STATUS int NULL);
IF OBJECT_ID(N'v2.DIVERGENCE_REFERENCE') IS NULL
    CREATE TABLE v2.DIVERGENCE_REFERENCE (DIVREF_ID_DIVREF int NOT NULL PRIMARY KEY, DIVREF_CODE_REFERENCE varchar(20) NULL,
        DIVREF_LIBELLE varchar(500) NULL);
IF OBJECT_ID(N'v2.DIVERGENCE_DETAIL') IS NULL
    CREATE TABLE v2.DIVERGENCE_DETAIL (DIVDET_ID_DIVDET int NOT NULL PRIMARY KEY, DIVDET_ID_BENEF int NULL, DIVDET_ID_DIVER int NOT NULL,
        DIVDET_ID_DIVREF int NOT NULL);

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1801301, '518013010', '', N'DIVERGENCES ALPHA SARL', 'C', 0),
    (1801302, '518013020', '', N'DIVERGENCES BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
-- 1801302 est radie il y a 30 jours : « Dossiers radies » (le critere « Exclure les dossiers radies » de la V2).
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
    (1801301, 1801301, 'B1', '2026B81301', '518013010', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    (1801302, 1801302, 'B1', '2026B81302', '518013020', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, DATEADD(DAY, -30, @aujourdhui));
SET IDENTITY_INSERT rec.DossierRcs OFF;

INSERT INTO v2.DIVERGENCE_REFERENCE (DIVREF_ID_DIVREF, DIVREF_CODE_REFERENCE, DIVREF_LIBELLE) VALUES
    (1801321, 'DIV18A', N'Bénéficiaire absent'), (1801322, 'DIV18B', N'Date de naissance différente');
-- DIVER_ID_ACTRC : l'action qui porte la divergence (NOT NULL dans RIG_DEV) ; l'ecran ne la lit pas.
INSERT INTO v2.DIVERGENCE (DIVER_ID_DIVER, DIVER_ID_ACTRC, DIVER_ID_DSRCS, DIVER_DEMANDEUR, DIVER_DATE_CREATION, DIVER_STATUS) VALUES
    -- ouverte, dossier actif, deux references : retenue (la plus ancienne des miennes)
    (1801311, 1801341, 1801301, 'GREFFE DE TEST', DATEADD(DAY, -10, @aujourdhui), 0),
    -- ouverte, dossier radie : retenue, « Dossiers radies »
    (1801312, 1801342, 1801302, 'INPI', DATEADD(DAY, -5, @aujourdhui), 0),
    -- traitee (statut 1) : ecartee -- jumeau retenu 1801311, meme dossier
    (1801313, 1801341, 1801301, 'INPI', DATEADD(DAY, -8, @aujourdhui), 1),
    -- ouverte, mais d'un dossier absent : ecartee (INNER JOIN DOSSIER_RCS de la V2)
    (1801314, 1801343, 1801399, 'INPI', DATEADD(DAY, -7, @aujourdhui), 0);
INSERT INTO v2.DIVERGENCE_DETAIL (DIVDET_ID_DIVDET, DIVDET_ID_DIVER, DIVDET_ID_DIVREF) VALUES
    (1801331, 1801311, 1801321), (1801332, 1801311, 1801322), (1801333, 1801312, 1801322), (1801334, 1801313, 1801321);
