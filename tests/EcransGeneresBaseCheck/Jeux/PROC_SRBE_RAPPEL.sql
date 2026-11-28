-- Jeu de LecteurRappelsRbe (« Rappels RBE », PROC_SRBE_RAPPEL), lot 1 du plan « brancher sur la base les procedures
-- encore en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 11005xx. Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_SRBE_RAPPEL.attendu. v2.DEMANDE existe deja dans le banc avec les colonnes lues (types de RIG_DEV).
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);

-- Les dossiers (rec.Entreprise et rec.DossierRcs : meme identifiant), autour du 01/08/2017 (PROC_SRBE_RAPPEL.cs:20).
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
SELECT v.Id, '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), '', v.Nom, 'C', 0
FROM (VALUES (1100501, N'RBE ALPHA SARL'), (1100502, N'RBE BETA SAS'), (1100503, N'RBE GAMMA SARL'), (1100504, N'RBE DELTA SAS'),
             (1100505, N'RBE EPSILON SARL'), (1100506, N'RBE ZETA SAS'), (1100507, N'RBE ETA SARL')) AS v (Id, Nom);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
SELECT v.Id, v.Id, 'B1', '2018B' + RIGHT(CAST(v.Id AS varchar(7)), 5), '511000' + RIGHT(CAST(v.Id AS varchar(7)), 3), v.Immat,
       'C', 0, 0, '20180101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0
FROM (VALUES (1100501, '20160510'), (1100502, '20170801'), (1100503, '20170731'), (1100504, '20190315'),
             (1100505, '20200101'), (1100506, '20160101'), (1100507, '20160101')) AS v (Id, Immat);
SET IDENTITY_INSERT rec.DossierRcs OFF;

INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_ID_DSRCS, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION,
    DMND_DATE_VALIDATION) VALUES
    -- immatricule avant le 01/08/2017 : rappel genere
    (1100511, 1100501, '2601100511', 'V', 'SRBE', DATEADD(DAY, -400, @aujourdhui), DATEADD(DAY, -400, @aujourdhui)),
    -- immatricule LE 01/08/2017 : relance 1 (« avant » exclut la date) -- jumeau 1100513 (la veille : rappel)
    (1100512, 1100502, '2601100512', 'V', 'SRBE', DATEADD(DAY, -380, @aujourdhui), DATEADD(DAY, -380, @aujourdhui)),
    -- la veille du seuil, demande qualifiee (code SRBE_RAPPEL) : rappel a envoyer
    (1100513, 1100503, '2601100513', 'Q', 'SRBE_RAPPEL', DATEADD(DAY, -2, @aujourdhui), NULL),
    -- interrompue (code SRBE_OLD) : relance 1 commencee
    (1100514, 1100504, '2601100514', 'I', 'SRBE_OLD', DATEADD(DAY, -5, @aujourdhui), NULL),
    -- terminee (T) : relance 1 generee a sa date de validation
    (1100515, 1100505, '2601100515', 'T', 'SRBE', DATEADD(DAY, -500, @aujourdhui), DATEADD(DAY, -499, @aujourdhui)),
    -- supprimee (X) : ecartee -- jumeau retenu 1100513 (Q)
    (1100516, 1100506, '2601100516', 'X', 'SRBE', DATEADD(DAY, -3, @aujourdhui), NULL),
    -- relance de la nouvelle surveillance (RDBE, ecran « Relances beneficiaires » par sa diligence) : ecartee --
    -- jumeau retenu 1100511 (SRBE)
    (1100517, 1100507, '2601100517', 'V', 'RDBE', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -10, @aujourdhui)),
    -- sans dossier : ecartee -- jumeau retenu 1100511
    (1100518, NULL, '2601100518', 'V', 'SRBE', DATEADD(DAY, -10, @aujourdhui), DATEADD(DAY, -10, @aujourdhui));

-- LES GESTES (EcrivainRappelsRbe.cs, lot 1 du plan-f4fb097f) : « Éditer les courriers » edite par dossier le parametre LOT1_LRNRBE_3_IMP
-- (rappel) ou LOT1_LRNRBE_1_IMP (relance 1), puis la liste des dossiers LRNRBE_1_LIST_IMP (PROC_SRBE_RAPPEL.cs:123-126, :234), aux
-- valeurs de RIG_DEV (libelle, maquette, type, recto verso, affichage avant impression, entite source ; lu le 28/11/2026). Aucun reglage
-- SRBE_EDITION_* dans le banc : les defauts de ParametrageRBE.cs (rappels chez Amitel, sans file, liste editee).
IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
    CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
        PRMED_RECTO_VERSO bit NOT NULL DEFAULT 0, PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL DEFAULT 0, PRMED_CODE_CFECP varchar(50) NULL,
        PRMED_LISTE varchar(4000) NOT NULL DEFAULT '', PRMED_CODE_ENTITE_SOURCE varchar(16) NULL, PRMED_CODE_PROCESSUS varchar(40) NULL,
        PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NOT NULL DEFAULT 1,
        PRMED_READ_ONLY bit NOT NULL DEFAULT 0, PRMED_NUM_GROUPE_IMPRESSION varchar(30) NULL, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC varchar(50) NULL);
EXEC sp_executesql N'
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_MAQUETTE, PRMED_TYPE_GENERATION, PRMED_RECTO_VERSO,
    PRMED_AFFICHER_AVANT_IMPRESSION, PRMED_CODE_ENTITE_SOURCE, PRMED_CODE_PROCESSUS, PRMED_NOMBRE_EXEMPLAIRES)
SELECT v.Code, v.Libelle, v.Maquette, v.TypeGen, v.Recto, 0, v.Entite, v.Processus, 1
FROM (VALUES (''LOT1_LRNRBE_3_IMP'', N''Courrier de rappel pour dépôt RBE'', ''LRNRBE_3_IMP'', ''RIG_LOT_UN'', 1, ''DOSSIER_RCS'', ''LRNRBE''),
             (''LOT1_LRNRBE_1_IMP'', N''Lettre de relance pour non dépôt RBE'', ''LRNRBE_1_IMP'', ''RIG_LOT_UN'', 0, NULL, ''LRNRBE''),
             (''LRNRBE_1_LIST_IMP'', N''Liste des dossiers pour relance pour non dépôt RBE'', ''LOT2_SRBE_1_LST_DOSS'', ''FUSION_ASPOSE'', 0, NULL, NULL))
     AS v (Code, Libelle, Maquette, TypeGen, Recto, Entite, Processus)
WHERE v.Code NOT IN (SELECT PRMED_CODE FROM v2.PARAMETRE_EDITION);';
