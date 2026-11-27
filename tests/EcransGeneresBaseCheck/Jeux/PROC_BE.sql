-- Jeu de LecteurBeneficiairesDossier et EcrivainBeneficiairesDossier (« Bénéficiaires d'un dossier », PROC_BE), lot 3 du plan
-- « RIG v3 100 % fonctionnel » (plan-676a4b19). Identifiants 1 314 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS
-- de SQL-DEV\DEV, lu le 26/11/2026), creees si absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql).
IF OBJECT_ID(N'v2.BENEFICIAIRE_EFFECTIF') IS NULL
    CREATE TABLE v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF int NOT NULL PRIMARY KEY, BENEF_ID_DSRCS int NOT NULL, BENEF_SUPPRIME bit NOT NULL,
        BENEF_CIVILITE varchar(10) NULL, BENEF_PATRONYME varchar(100) NULL, BENEF_PRENOM_ETAT_CIVIL varchar(100) NULL,
        BENEF_NOM_USAGE varchar(100) NULL, BENEF_DATE_NAISSANCE datetime NULL, BENEF_DEPARTEMENT_NAISS varchar(3) NULL, BENEF_PAYS varchar(50) NULL);
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_LIEU_NAISSANCE') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_LIEU_NAISSANCE varchar(50) NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_NATIONALITE') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_NATIONALITE varchar(50) NULL;
IF COL_LENGTH('v2.BENEFICIAIRE_EFFECTIF', 'BENEF_ID_ADRBS') IS NULL ALTER TABLE v2.BENEFICIAIRE_EFFECTIF ADD BENEF_ID_ADRBS int NULL;
IF OBJECT_ID(N'v2.NATIONALITE') IS NULL
    CREATE TABLE v2.NATIONALITE (R_NATIONALIT_VALEUR varchar(35) NOT NULL PRIMARY KEY, R_NATIONALIT_UTILISABLE_SAISIE bit NULL);
IF OBJECT_ID(N'v2.COMMUNE') IS NULL
    CREATE TABLE v2.COMMUNE (R_COMMUNE_ID_COMMN int NOT NULL PRIMARY KEY, R_COMMUNE_COMMUNE varchar(50) NULL, R_COMMUNE_CODE_POSTAL varchar(6) NOT NULL,
        R_COMMUNE_DEPARTEMENT varchar(3) NULL);
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

-- Les communes, comme RIG_DEV les ecrit (R_COMMUNE_COMMUNE en majuscules, sans accent ni tiret, ULT_COMBO_COMMUNE_NAISSANCE.cs:86-88) :
-- NICE (06) ; deux SAINT DENIS, en Seine-Saint-Denis (93) et a La Reunion (974), que seul le departement departage.
INSERT INTO v2.COMMUNE (R_COMMUNE_ID_COMMN, R_COMMUNE_COMMUNE, R_COMMUNE_CODE_POSTAL, R_COMMUNE_DEPARTEMENT) VALUES
    (1314901, 'NICE', '06000', '06'), (1314902, 'SAINT DENIS', '93200', '93'), (1314903, 'SAINT DENIS', '97400', '974');

-- Les nationalites, aux valeurs de RIG_DEV (NATIONALITE lue le 26/11/2026) : FRA et ITA utilisables en saisie, ZZZ « autre »
-- qui ne l'est plus (R_NATIONALIT_UTILISABLE_SAISIE = 0). Le Répertoire s'en sert aussi (PROC_MODIF_REPER.sql, joue apres).
INSERT INTO v2.NATIONALITE (R_NATIONALIT_VALEUR, R_NATIONALIT_UTILISABLE_SAISIE) VALUES ('FRA', 1), ('ITA', 1), ('ZZZ', 0);

-- Les beneficiaires. 1314101 ne en France (75, a PARIS : absente de COMMUNE, aucun controle de compatibilite), francais, a
-- l'adresse rec.Adresse 1312501 (posee par PROC_PARTIES.sql, joue apres ce jeu, avant les gestes) ; 1314102 sans pays
-- (la V2 l'ouvre sur la France, OPE_IDENTITE.cs:62-64), sans departement ni lieu ; 1314103 ne en Italie, a Milan. 1314104 est
-- partant (BENEF_SUPPRIME) : ecarte -- jumeau retenu : 1314101.
EXEC sp_executesql N'
INSERT INTO v2.INSCRIT_PM (INSPM_ID_DSRCS, INSPM_FORME_JURIDIQUE_UTI) VALUES (1314001, ''SAS13''), (1314002, ''SIC13'');
INSERT INTO v2.BENEFICIAIRE_EFFECTIF (BENEF_ID_BENEF, BENEF_ID_DSRCS, BENEF_SUPPRIME, BENEF_CIVILITE, BENEF_PATRONYME, BENEF_PRENOM_ETAT_CIVIL,
    BENEF_NOM_USAGE, BENEF_DATE_NAISSANCE, BENEF_DEPARTEMENT_NAISS, BENEF_PAYS, BENEF_LIEU_NAISSANCE, BENEF_NATIONALITE, BENEF_ID_ADRBS) VALUES
    (1314101, 1314001, 0, ''M.'', ''Dupont'', ''jean marc'', NULL, ''19800201'', ''75'', ''FRA'', ''PARIS'', ''FRA'', 1312501),
    (1314102, 1314001, 0, ''Mme'', ''MARTIN'', ''Anne'', NULL, ''19751103'', NULL, NULL, NULL, NULL, NULL),
    (1314103, 1314002, 0, ''M.'', ''ROSSI'', ''Luca'', NULL, ''19900615'', NULL, ''ITA'', ''Milan'', ''ITA'', NULL),
    (1314104, 1314001, 1, ''M.'', ''PARTANT'', ''Paul'', NULL, ''19600101'', ''69'', ''FRA'', ''LYON'', ''FRA'', NULL);';
