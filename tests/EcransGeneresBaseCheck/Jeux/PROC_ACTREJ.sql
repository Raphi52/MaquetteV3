-- Jeu de LecteurActesRejetes et EcrivainActesRejetes (« Actes rejetes », PROC_ACTREJ), lot 8 du plan « RIG v3 100 %
-- fonctionnel ». Identifiants 18012xx (lot 8 : a partir de 1800000). Ce qui est retenu ou ecarte : PROC_ACTREJ.attendu ; les
-- gestes : PROC_ACTREJ.gestes.
-- Les tables V2 lues, aux types de RIG_DEV (sys.columns de la copie de dev, 26/11/2026), seulement les colonnes lues. Ce jeu
-- passe AVANT PROC_ACTREJ_CMD_WEB.sql (ordre des noms) : s'il cree v2.ACTE_DEPOSE, il y met aussi les colonnes que ce jeu-la
-- insere sans les ajouter. v2.DEPOT_RCS existe deja dans le banc.
IF OBJECT_ID(N'v2.ACTE_DEPOSE') IS NULL
    CREATE TABLE v2.ACTE_DEPOSE (ACTDP_ID_ACDPS int NOT NULL PRIMARY KEY, ACTDP_ID_DPTRC int NULL, ACTDP_ABREGE_PIECE varchar(16) NOT NULL,
        ACTDP_DESCRIPTION varchar(256) NULL, ACTDP_CMDWEB_FLUX_ID int NULL, ACTDP_CMDWEB_DATE datetime NULL,
        ACTDP_GUID_VIDIGED uniqueidentifier NULL);
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_DESCRIPTION') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_DESCRIPTION varchar(256) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_GUID_VIDIGED') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_GUID_VIDIGED uniqueidentifier NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Etat_GEIDE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Etat_GEIDE varchar(2) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Date_Envoi_GEIDE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Date_Envoi_GEIDE datetime NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Date_Compte_Rendu_Integration_GEIDE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Date_Compte_Rendu_Integration_GEIDE datetime NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Motif_Rejet_GEIDE') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Motif_Rejet_GEIDE varchar(1000) NULL;
IF COL_LENGTH('v2.ACTE_DEPOSE', 'ACTDP_EDI_INFOGREFFE_Image_ATransferer') IS NULL ALTER TABLE v2.ACTE_DEPOSE ADD ACTDP_EDI_INFOGREFFE_Image_ATransferer bit NULL;
IF OBJECT_ID(N'v2.ABREGE_PIECE') IS NULL
    CREATE TABLE v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR varchar(16) NOT NULL PRIMARY KEY, R_ABREGEPIEC_DEVELOPPE_EDIT varchar(256) NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1801201, '518012010', '', N'ACTES REJETES ALPHA SARL', 'C', 0),
    (1801202, '518012020', '', N'ACTES REJETES BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1801201, 1801201, 'B1', '2026B81201', '518012010', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (1801202, 1801202, 'B1', '2026B81202', '518012020', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les depots et leurs actions. Le depot 1801213 n'a aucune action : la V2 (INNER JOIN ACTION_RCS) ne montre pas ses actes.
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_NUM_DEPOT) VALUES (1801211, 'R1801211'), (1801212, 'R1801212'), (1801213, 'R1801213');
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1801221, 1801201, 1801211, 'DEPOT', 'C', 'V', 0),
    (1801222, 1801202, 1801212, 'DEPOT', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
INSERT INTO v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR, R_ABREGEPIEC_DEVELOPPE_EDIT)
SELECT 'BILA18', N'Bilan des comptes annuels' WHERE NOT EXISTS (SELECT 1 FROM v2.ABREGE_PIECE WHERE R_ABREGEPIEC_VALEUR = 'BILA18');

-- Par sp_executesql : les colonnes EDI ajoutees ci-dessus n'existent pas encore quand SQL Server compile le lot.
EXEC sp_executesql N'
-- Les actes deposes. Seul l''etat GEIDE 9 (rejete par Infogreffe) est retenu (MetierActRejets.cs:81).
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_DESCRIPTION, ACTDP_EDI_INFOGREFFE_Etat_GEIDE,
    ACTDP_EDI_INFOGREFFE_Date_Envoi_GEIDE, ACTDP_EDI_INFOGREFFE_Date_Compte_Rendu_Integration_GEIDE, ACTDP_EDI_INFOGREFFE_Motif_Rejet_GEIDE,
    ACTDP_EDI_INFOGREFFE_Image_ATransferer) VALUES
    -- rejete il y a 5 jours, description vide : celle de l''abrege ; retenu, image non transferee (le renvoi la remet a 1)
    (1801231, 1801211, ''BILA18'', '''', ''9'', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -5, @aujourdhui), N''Format de fichier refusé'', 0),
    -- rejete il y a 2 jours : retenu, en tete (compte rendu le plus recent)
    (1801232, 1801212, ''STA'', N''Statuts mis à jour'', ''9'', DATEADD(DAY, -4, @aujourdhui), DATEADD(DAY, -2, @aujourdhui), N''Pièce illisible'', 1),
    -- deja renvoye (etat 1) : ecarte -- jumeau retenu 1801232, meme depot
    (1801233, 1801212, ''STA'', N''Statuts déjà renvoyés'', ''1'', DATEADD(DAY, -4, @aujourdhui), DATEADD(DAY, -3, @aujourdhui), NULL, 1),
    -- rejet annule par Amitel (etat 99) : ecarte -- jumeau retenu 1801231, meme depot
    (1801234, 1801211, ''STA'', N''Rejet annulé'', ''99'', DATEADD(DAY, -9, @aujourdhui), DATEADD(DAY, -6, @aujourdhui), NULL, 1),
    -- rejete il y a 20 jours, sans motif : retenu, en dernier ; aucun geste du banc ne le touche (la liste garde une ligne)
    (1801236, 1801211, ''PV'', N''Procès-verbal d''''assemblée'', ''9'', DATEADD(DAY, -21, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), NULL, 1),
    -- rejete mais d''un depot sans action, donc sans dossier : ecarte (INNER JOIN de la V2)
    (1801235, 1801213, ''STA'', N''Acte d''''un dépôt sans action'', ''9'', DATEADD(DAY, -4, @aujourdhui), DATEADD(DAY, -1, @aujourdhui), NULL, 1);
',
    N'@aujourdhui datetime', @aujourdhui;
