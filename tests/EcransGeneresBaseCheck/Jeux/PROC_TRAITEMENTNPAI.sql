-- Jeu de LecteurCourriersNpai (« Courriers revenus NPAI », PROC_TRAITEMENTNPAI), lot 2 du plan « ecrans retrouves
-- branches sur la base ». Identifiants 2003xx. Ce qui est retenu ou ecarte, et pourquoi : PROC_TRAITEMENTNPAI.attendu.
-- v2.DEMANDE existe deja dans le banc, sans les deux colonnes lues ici : elles lui sont ajoutees, aux types de RIG_DEV
-- (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026). Les lignes passent par sp_executesql : une colonne
-- ajoutee dans ce meme lot d'instructions n'existe pas encore quand SQL Server le compile.
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_PARAMETRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_PARAMETRE varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
-- Le libelle du processus du courrier d'origine (MODELE_PROCESSUS de RIG_DEV : cle MDPRC_ID_MDPRC, libelles tels quels).
IF OBJECT_ID(N'v2.MODELE_PROCESSUS') IS NULL
    CREATE TABLE v2.MODELE_PROCESSUS (MDPRC_ID_MDPRC int NOT NULL PRIMARY KEY, MDPRC_TYPE_MODELE varchar(3) NOT NULL,
        MDPRC_CODE_PROSS varchar(40) NOT NULL, MDPRC_LIBELLE varchar(70) NULL);
INSERT INTO v2.MODELE_PROCESSUS (MDPRC_ID_MDPRC, MDPRC_TYPE_MODELE, MDPRC_CODE_PROSS, MDPRC_LIBELLE)
SELECT v.Id, 'P', v.Code, v.Libelle FROM (VALUES (200391, 'SDCA', N'Surveillances des dépôts de comptes annuels'),
                                                 (200392, 'SASSOSCI', N'Surveillance SCI sans Associés')) AS v (Id, Code, Libelle)
WHERE NOT EXISTS (SELECT 1 FROM v2.MODELE_PROCESSUS AS m WHERE m.MDPRC_CODE_PROSS = v.Code);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (200301, '512000301', '', N'NPAI ALPHA SARL', 'C', 0),
    (200302, '512000302', '', N'NPAI BETA SAS', 'C', 0),
    (200303, '512000303', '', N'NPAI GAMMA SARL', 'C', 0),
    (200304, '512000304', '', N'NPAI DELTA SAS', 'C', 0),
    (200305, '512000305', '', N'NPAI EPSILON SARL', 'C', 0),
    (200307, '512000307', '', N'NPAI ETA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (200301, 200301, 'B1', '2020B30301', '512000301', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200302, 200302, 'B1', '2020B30302', '512000302', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200303, 200303, 'B1', '2020B30303', '512000303', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200304, 200304, 'B1', '2020B30304', '512000304', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200305, 200305, 'B1', '2020B30305', '512000305', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (200307, 200307, 'B1', '2020B30307', '512000307', '20200101', 'C', 0, 0, '20200101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;

-- Les demandes : celles du processus NPAI (une par courrier revenu traite) et leurs jumelles ecartees.
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION,
    DMND_DATE_VALIDATION, DMND_TEXTE_PARAMETRE, DMND_NOM) VALUES
    -- traitee : LRAR au siege (une action qui imprime) ; formalite
    (200311, 200301, ''26000200311'', ''V'', ''NPAI'', DATEADD(HOUR, -231, @aujourdhui), DATEADD(HOUR, -231, @aujourdhui),
        ''typeNPAI=SiegeLR'', ''NPAI - LRAR - NPAI ALPHA SARL''),
    -- traitee : enregistrement seul (pas d''impression) ; relance de comptes annuels (LstIdExer)
    (200312, 200302, ''26000200312'', ''V'', ''NPAI'', DATEADD(HOUR, -40, @aujourdhui), DATEADD(HOUR, -40, @aujourdhui),
        ''typeNPAI=Enreg;LstIdExer=4521,4522'', ''NPAI - Enregistrement - NPAI BETA SAS''),
    -- action ouverte, pas encore validee (Q) : en cours, en tete ; le courrier revenu est celui de 200318
    (200313, 200303, ''26000200313'', ''Q'', ''NPAI'', DATEADD(HOUR, 8, @aujourdhui), NULL,
        ''typeNPAI=SiegeLR'', ''NPAI - LRAR - NPAI GAMMA SARL''),
    -- action abandonnee (X) : ecartee -- jumelle retenue 200313
    (200314, 200304, ''26000200314'', ''X'', ''NPAI'', DATEADD(HOUR, 7, @aujourdhui), NULL,
        ''typeNPAI=DiriLS'', ''NPAI - lettre simple au dirigeant - NPAI DELTA SAS''),
    -- autre processus (LRAR) portant pourtant typeNPAI : ecartee -- jumelle retenue 200311
    (200315, 200305, ''26000200315'', ''V'', ''LRAR'', DATEADD(HOUR, -20, @aujourdhui), DATEADD(HOUR, -20, @aujourdhui),
        ''typeNPAI=SiegeLR'', ''LRAR - NPAI EPSILON SARL''),
    -- code d''etat hors de ceux de l''ecran, sans dossier : le nom de la demande
    (200316, NULL, ''26000200316'', ''A'', ''NPAI'', DATEADD(HOUR, -86, @aujourdhui), NULL,
        ''typeNPAI=SiegeMof'', ''NPAI - MOF - SANS DOSSIER''),
    -- traitee, lettre au dirigeant, exercice vide : relance de comptes annuels par son courrier d''origine (200327, SDCA)
    (200317, 200307, ''26000200317'', ''V'', ''NPAI'', DATEADD(HOUR, -134, @aujourdhui), DATEADD(HOUR, -134, @aujourdhui),
        ''typeNPAI=DiriLS;LstIdExer='', ''NPAI - lettre simple au dirigeant - NPAI ETA SARL''),
    -- traitee, lettre au dirigeant, revenue A SON TOUR (note SiegeLR=200313) : courrier d''origine 200328, SDCA
    (200318, 200303, ''26000200318'', ''V'', ''NPAI'', DATEADD(HOUR, -62, @aujourdhui), DATEADD(HOUR, -62, @aujourdhui),
        ''typeNPAI=DiriLS;SiegeLR=200313'', ''NPAI - lettre simple au dirigeant - NPAI GAMMA SARL''),
    -- LES COURRIERS D''ORIGINE : la demande flashee porte « <action>=<n° de la demande NPAI> » (notifierAncienneDemande).
    -- 200311 vient d''une surveillance SASSOSCI : « Formalites », jumelle de 200317 (SDCA)
    (200321, 200301, ''26000200321'', ''V'', ''SASSOSCI'', DATEADD(DAY, -40, @aujourdhui), NULL,
        ''NatureDiligence=SASSOSCI;SiegeLR=200311'', ''Surveillance SCI''),
    (200327, 200307, ''26000200327'', ''V'', ''SDCA'', DATEADD(DAY, -40, @aujourdhui), NULL, ''DiriLS=200317'', ''Relance DCA''),
    -- 200313 -> 200318 (NPAI) -> 200328 (SDCA) : deux crans
    (200328, 200303, ''26000200328'', ''V'', ''SDCA'', DATEADD(DAY, -40, @aujourdhui), NULL, ''DiriLS=200318'', ''Relance DCA''),
    -- leurre : « SiegeLR=2003110 » commence comme 200311 mais n''est pas sa note (valeur exacte) -- 200311 reste SASSOSCI
    (200331, 200301, ''26000200331'', ''V'', ''SDCA'', DATEADD(DAY, -50, @aujourdhui), NULL, ''SiegeLR=2003110'', ''Relance DCA'');',
    N'@aujourdhui datetime', @aujourdhui;
