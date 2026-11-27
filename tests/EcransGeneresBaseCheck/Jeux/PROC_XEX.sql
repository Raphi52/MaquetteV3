-- Jeu de LecteurKbisInterne / EcrivainKbisInterne (« Édition interne d'un Kbis », PROC_XEX), lot 13 du plan-676a4b19. Identifiants
-- 23006xx, numeros de gestion « 2013B6xxxx ». Ce qui est retenu ou ecarte, et pourquoi : PROC_XEX.attendu, .recherches et .gestes.
-- Les dossiers : rec.Entreprise et rec.DossierRcs (meme identifiant, la definition de PROC_ACTREJ.sql) ; la demande XEX : v2.DEMANDE du
-- banc, completee des colonnes que la V2 y ecrit (types de RIG_DEV, lus le 27/11/2026). Dates de mise a jour anciennes : aucun dossier
-- n'entre dans la liste du jour de « Dossier RCS » (PROC_VDOSSIER).
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (2300601, '523006010', '', N'KBIS INTERNE ALPHA SARL', 'C', 0), (2300602, '523006020', '', N'KBIS INTERNE BETA SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
    -- un dossier qui a deja un Kbis interne (la liste a l'ouverture)
    (2300601, 2300601, 'B1', '2013B60001', '523006010', '20130101', 'C', 0, 0, '20130101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, NULL),
    -- ECARTE de la liste a l'ouverture : aucun Kbis interne -- jumeau retenu 2300601 ; retrouve par son numero ; radie, admis par la
    -- V2 (MetierTestEntiteRadiee = false)
    (2300602, 2300602, 'B1', '2013B60002', '523006020', '20130101', 'C', 0, 0, '20130101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, '20200630');
SET IDENTITY_INSERT rec.DossierRcs OFF;
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_ID_DSRCS, DMND_DATE_CREATION,
    DMND_DATE_VALIDATION, DMND_NOM, DMND_NUM_INDICATIF) VALUES
    (2300611, ''L13X0001'', ''V'', ''XEX'', 2300601, ''20250115 10:00'', ''20250115 10:01'', ''KBIS INTERNE ALPHA SARL'', ''2013B60001''),
    -- ECARTE du compte : une demande d''un autre processus sur le dossier -- jumeau retenu 2300611 (XEX)
    (2300612, ''L13X0002'', ''V'', ''XEXX'', 2300602, ''20250116'', ''20250116'', ''KBIS INTERNE BETA SAS'', ''2013B60002'');';
