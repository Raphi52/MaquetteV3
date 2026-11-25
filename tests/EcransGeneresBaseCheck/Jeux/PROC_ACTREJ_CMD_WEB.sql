-- Jeu de LecteurActesWebNonTransmis (« Actes web non transmis », PROC_ACTREJ_CMD_WEB), lot 2 du plan « ecrans retrouves
-- branches sur la base ». Identifiants 2001xx (lot 2 : a partir de 200000). Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_ACTREJ_CMD_WEB.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues (v2.DEPOT_RCS existe deja dans le banc).
IF OBJECT_ID(N'v2.DEPOT_RCS') IS NULL
    CREATE TABLE v2.DEPOT_RCS (DPTRC_ID_DPTRC int NOT NULL PRIMARY KEY, DPTRC_ID_EXRCC int NULL, DPTRC_NUM_DEPOT varchar(12) NOT NULL);
IF OBJECT_ID(N'v2.ACTE_DEPOSE') IS NULL
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL,
        ACTDP_DESCRIPTION varchar(256) NULL, ACTDP_CMDWEB_FLUX_ID int NULL, ACTDP_CMDWEB_DATE datetime NULL,
        ACTDP_GUID_VIDIGED uniqueidentifier NULL);
IF OBJECT_ID(N'v2.ABREGE_PIECE') IS NULL
    CREATE TABLE v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR varchar(16) NOT NULL PRIMARY KEY, R_ABREGEPIEC_DEVELOPPE_EDIT varchar(256) NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (200101, '512000101', '', N'ACTES WEB ALPHA SARL', 'C', 0),
    (200102, '512000102', '', N'ACTES WEB BETA SAS', 'C', 0),
    (200103, '512000103', '', N'ACTES WEB GAMMA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (200101, 200101, 'B1', '2020B10101', '512000101', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200102, 200102, 'B1', '2020B10102', '512000102', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200103, 200103, 'B1', '2020B10103', '512000103', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les depots et leurs actions (rec.ActionRegistre.IdDepot). Le depot 200122 a DEUX actions : une seule ligne par acte.
-- Le depot 200125 n'a aucune action : la V2 (INNER JOIN ACTION_RCS) ne le montre pas.
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_NUM_DEPOT) VALUES
    (200121, 'W200121'), (200122, 'W200122'), (200123, 'W200123'), (200125, 'W200125');
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (200131, 200101, 200121, 'DEPOT', 'C', 'V', 0),
    (200132, 200102, 200122, 'DEPOT', 'C', 'V', 0),
    (200133, 200102, 200122, 'MODIF', 'C', 'V', 0),
    (200134, 200103, 200123, 'DEPOT', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
INSERT INTO v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR, R_ABREGEPIEC_DEVELOPPE_EDIT)
SELECT 'PVBANC', N'Procès-verbal d''assemblée' WHERE NOT EXISTS (SELECT 1 FROM v2.ABREGE_PIECE WHERE R_ABREGEPIEC_VALEUR = 'PVBANC');

-- Les actes deposes.
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_DESCRIPTION, ACTDP_CMDWEB_FLUX_ID,
    ACTDP_CMDWEB_DATE, ACTDP_GUID_VIDIGED) VALUES
    -- commande d'aujourd'hui, pas dans la GED, description saisie : retenu, « Commandes du jour »
    (200111, 200121, 'STA', N'Statuts mis à jour', 5001, DATEADD(HOUR, 10, @aujourdhui), NULL),
    -- commande d'hier a 18 h (la borne du jour, autre cote), description NULL : celle de l'abrege ; retenu, plus ancienne
    (200112, 200122, 'PVBANC', NULL, 5002, DATEADD(HOUR, -6, @aujourdhui), NULL),
    -- deja range dans la GED (GUID pose) : ecarte -- son jumeau retenu est 200112, meme depot
    (200113, 200122, 'STA', N'Statuts déjà en GED', 5003, DATEADD(HOUR, -6, @aujourdhui), NEWID()),
    -- aucune commande web (flux NULL) : ecarte -- jumeau retenu 200111
    (200114, 200123, 'STA', N'Statuts non commandés', NULL, NULL, NULL),
    -- depot sans action, donc sans dossier : ecarte (INNER JOIN de la V2)
    (200115, 200125, 'STA', N'Acte d''un dépôt sans action', 5005, DATEADD(HOUR, 9, @aujourdhui), NULL);
