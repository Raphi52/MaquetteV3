-- Jeu de LecteurBeneficiairesDossier et EcrivainBeneficiairesDossier (« Bénéficiaires d'un dossier », PROC_BE), lot 3 du plan
-- « RIG v3 100 % fonctionnel » (plan-676a4b19). Identifiants 1 314 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS
-- de SQL-DEV\DEV, lu le 26/11/2026), creees si absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql).
IF OBJECT_ID(N'v2.BENEFICIAIRE_EFFECTIF') IS NULL
    CREATE TABLE v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF int NOT NULL PRIMARY KEY, BENEF_ID_DSRCS int NOT NULL, BENEF_SUPPRIME bit NOT NULL,
        BENEF_CIVILITE varchar(10) NULL, BENEF_PATRONYME varchar(100) NULL, BENEF_PRENOM_ETAT_CIVIL varchar(100) NULL,
        BENEF_NOM_USAGE varchar(100) NULL, BENEF_DATE_NAISSANCE datetime NULL, BENEF_DEPARTEMENT_NAISS varchar(3) NULL, BENEF_PAYS varchar(50) NULL);
IF COL_LENGTH('v2.INSCRIT_PM', 'INSPM_FORME_JURIDIQUE_UTI') IS NULL ALTER TABLE v2.INSCRIT_PM ADD INSPM_FORME_JURIDIQUE_UTI varchar(10) NULL;
IF OBJECT_ID(N'v2.FORME_JURIDIQUE_UTI') IS NULL
    CREATE TABLE v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_FORMEJURID_TYPE_BEN int NULL);

-- Les formes juridiques : type 1 (societe) et 3 (placement collectif, FORME_JURIDIQUE_UTI.GetTypeBe).
INSERT INTO v2.FORME_JURIDIQUE_UTI (R_FORMEJURID_VALEUR, R_FORMEJURID_TYPE_BEN) VALUES ('SAS13', 1), ('SIC13', 3);

-- Les dossiers : 1314001 une societe (« Déclaration standard »), 1314002 un placement collectif.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1314001, '513140001', '', N'BE ALPHA SAS', 'C', 0), (1314002, '513140002', '', N'BE PLACEMENT SICAV', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2027B' + RIGHT(CAST(v.Id AS varchar(7)), 5), v.Siren, '20180101', 'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1314001, '513140001'), (1314002, '513140002')) AS v (Id, Siren);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les beneficiaires. 1314101 ne en France (75) ; 1314102 sans pays (la V2 l'ouvre sur la France, OPE_IDENTITE.cs:62-64) et sans
-- departement ; 1314103 ne en Italie. 1314104 est partant (BENEF_SUPPRIME) : ecarte -- jumeau retenu : 1314101.
EXEC sp_executesql N'
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI) VALUES (1314001, ''SAS13''), (1314002, ''SIC13'');
INSERT INTO v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF, BENEF_ID_DSRCS, BENEF_SUPPRIME, BENEF_CIVILITE, BENEF_PATRONYME, BENEF_PRENOM_ETAT_CIVIL,
    BENEF_NOM_USAGE, BENEF_DATE_NAISSANCE, BENEF_DEPARTEMENT_NAISS, BENEF_PAYS) VALUES
    (1314101, 1314001, 0, ''M.'', ''Dupont'', ''jean marc'', NULL, ''19800201'', ''75'', ''FRA''),
    (1314102, 1314001, 0, ''Mme'', ''MARTIN'', ''Anne'', NULL, ''19751103'', NULL, NULL),
    (1314103, 1314002, 0, ''M.'', ''ROSSI'', ''Luca'', NULL, ''19900615'', NULL, ''ITA''),
    (1314104, 1314001, 1, ''M.'', ''PARTANT'', ''Paul'', NULL, ''19600101'', ''69'', ''FRA'');';
