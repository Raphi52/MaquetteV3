-- Jeu de LecteurRechercheBeneficiaires (« Rechercher un beneficiaire », PROC_BEN), lot 6 du plan-676a4b19. Identifiants 16001xx
-- (lot 6 : a partir de 1 600 000). Ce qui est retenu ou ecarte, et pourquoi : PROC_BEN.attendu et PROC_BEN.recherches.
-- « Bénéficiaires d'un dossier » (PROC_BE) liste TOUT beneficiaire non partant : ce jeu s'appuie donc sur ceux de PROC_BE.sql,
-- joue avant lui (dossiers 1314001 et 1314002, non radies ; 1314104 partant), et n'en ajoute qu'UN, celui d'un dossier radie,
-- ajoute aussi a PROC_BE.attendu, PROC_BE.recherches et PROC_BE.gestes (regle du plan : un jeu qui ajoute une ligne a un
-- lecteur sans filtre l'ajoute a ses attendus). La divergence en attente vise 1314102 (MARTIN Anne, PROC_BE.sql).
-- Types : ceux de RIG_DEV (SSDT, RigDatabase\dbo\Tables\BENEFICIAIRE_EFFECTIF.sql, DIVERGENCE.sql, DIVERGENCE_DETAIL.sql).
IF OBJECT_ID(N'v2.BENEFICIAIRE_EFFECTIF') IS NULL
    CREATE TABLE v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF int NOT NULL PRIMARY KEY, BENEF_ID_DSRCS int NOT NULL, BENEF_SUPPRIME bit NOT NULL);
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_PATRONYME') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_PATRONYME varchar(100) NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_PRENOM_ETAT_CIVIL') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_PRENOM_ETAT_CIVIL varchar(100) NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_NOM_USAGE') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_NOM_USAGE varchar(100) NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_DATE_NAISSANCE') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_DATE_NAISSANCE datetime NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_DATE_EFFET') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_DATE_EFFET datetime NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_DEPART_DATE') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_DEPART_DATE datetime NULL;
IF OBJECT_ID(N'v2.DIVERGENCE') IS NULL
    CREATE TABLE v2.DIVERGENCE (DIVER_ID_DIVER int NOT NULL PRIMARY KEY, DIVER_ID_ACTRC int NOT NULL, DIVER_ID_DSRCS int NOT NULL,
        DIVER_DEMANDEUR varchar(max) NOT NULL, DIVER_DATE_CREATION datetime NULL, DIVER_STATUS int NULL);
IF OBJECT_ID(N'v2.DIVERGENCE_DETAIL') IS NULL
    CREATE TABLE v2.DIVERGENCE_DETAIL (DIVDET_ID_DIVDET int NOT NULL PRIMARY KEY, DIVDET_ID_BENEF int NULL, DIVDET_ID_DIVER int NOT NULL,
        DIVDET_ID_DIVREF int NOT NULL);

-- Un dossier radie (ecarte par defaut, la case « Dossiers radiés » decochee).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES (1600102, '516001002', '', N'BEN RADIEE SAS', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise, DateRadiation) VALUES
    (1600102, 1600102, 'B1', '2016B60102', '516001002', '20160101', 'C', 0, 0, '20160101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0, '20250630');
SET IDENTITY_INSERT rec.DossierRcs OFF;
-- L'action RCS du dossier 1314001 qui porte la divergence (DIVER_ID_ACTRC), code processus hors de ceux des autres lecteurs.
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1600121, 1314001, 'BANCBEN', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- Le beneficiaire du dossier radie, les divergences ; les colonnes viennent peut-etre d'etre ajoutees : insertion compilee a part.
EXEC sp_executesql N'
INSERT INTO v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF, BENEF_ID_DSRCS, BENEF_SUPPRIME, BENEF_PATRONYME, BENEF_PRENOM_ETAT_CIVIL,
    BENEF_NOM_USAGE, BENEF_DATE_NAISSANCE, BENEF_DATE_EFFET) VALUES
    -- ECARTE par defaut : dossier radie -- jumeau retenu 1314101 (dossier en vigueur) ; retrouve avec « Dossiers radiés = oui »
    (1600114, 1600102, 0, ''BENRADIE'', ''Anne'', NULL, ''19750615'', ''20230101'');
INSERT INTO v2.DIVERGENCE (DIVER_ID_DIVER, DIVER_ID_ACTRC, DIVER_ID_DSRCS, DIVER_DEMANDEUR, DIVER_DATE_CREATION, DIVER_STATUS) VALUES
    -- en attente de la societe (0) : la ligne de divergence de 1314102
    (1600131, 1600121, 1314001, ''Banque Banc'', ''20261101'', 0),
    -- ECARTEE : traitee (1) -- jumelle retenue 1600131
    (1600132, 1600121, 1314001, ''Notaire Banc'', ''20260901'', 1);
INSERT INTO v2.DIVERGENCE_DETAIL (DIVDET_ID_DIVDET, DIVDET_ID_BENEF, DIVDET_ID_DIVER, DIVDET_ID_DIVREF) VALUES
    (1600141, 1314102, 1600131, 1), (1600142, 1314102, 1600132, 1);';
