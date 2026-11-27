-- Jeu de LecteurRetoursLrar (« Retours LRAR », PROC_RETOURLRAR), lot 2 du plan « brancher sur la base les procedures
-- encore en exemple » (plan-f7ba4de2). Plis 1A12000xx. Ce qui est retenu ou ecarte, et pourquoi : PROC_RETOURLRAR.attendu.
-- Aucune ligne n'entre dans un autre ecran branche : LRAR_INFOS et PARAM_LRAR_INFOS ne sont lues par aucun autre lecteur,
-- et aucune demande d'edition n'est posee (elle entrerait dans PROC_POOLEDITION).
-- Les tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026), colonnes lues.
IF OBJECT_ID(N'v2.LRAR_INFOS') IS NULL
    CREATE TABLE v2.LRAR_INFOS (LRARI_NUM_LRAR varchar(13) NOT NULL PRIMARY KEY, LRARI_DATE_RECEPTION datetime NULL,
        LRARI_TYPE varchar(15) NOT NULL, LRARI_TYPE_DATE varchar(20) NULL, LRARI_NUM_LRAR_RENVOYE varchar(13) NULL);
-- Les modalites de retour : la table de RIG_DEV telle quelle (sept lignes, lues le 25/11/2026).
IF OBJECT_ID(N'v2.PARAM_LRAR_INFOS') IS NULL
    CREATE TABLE v2.PARAM_LRAR_INFOS (PRMLI_CODE varchar(20) NOT NULL PRIMARY KEY, PRMLI_LIBELLE varchar(50) NULL,
        PRMLI_A_GERER bit NOT NULL);
INSERT INTO v2.PARAM_LRAR_INFOS (PRMLI_CODE, PRMLI_LIBELLE, PRMLI_A_GERER)
SELECT v.Code, v.Libelle, v.AGerer FROM (VALUES ('INCOMPLET', N'Incomplet reçu le', 1), ('NON_RECLAME', N'Non réclamé reçu le', 1),
    ('NPAI', N'NPAI reçu le', 1), ('PAS_ENVOYE', N'Non envoyé le', 0), ('PLI_PERDU', N'pli réclamé déclaré perdu par la poste', 1),
    ('RECEPTION', N'Reçu le', 1), ('REFUSE', N'Refusé reçu le', 1)) AS v (Code, Libelle, AGerer)
WHERE NOT EXISTS (SELECT 1 FROM v2.PARAM_LRAR_INFOS AS p WHERE p.PRMLI_CODE = v.Code);

INSERT INTO v2.LRAR_INFOS (LRARI_NUM_LRAR, LRARI_TYPE, LRARI_TYPE_DATE, LRARI_DATE_RECEPTION, LRARI_NUM_LRAR_RENVOYE) VALUES
    -- accuse recu, date saisie : Recus, retour enregistre
    ('1A1200001', 'LRAR', 'RECEPTION', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- NPAI, renvoye sous un autre pli
    ('1A1200002', 'LRAR', 'NPAI', DATEADD(DAY, -2, @aujourdhui), '1A1200011'),
    -- refuse
    ('1A1200003', 'LRAR', 'REFUSE', DATEADD(DAY, -3, @aujourdhui), NULL),
    -- incomplet, case « date non renseignee » : date a saisir, en cours, en tete
    ('1A1200004', 'LRAR', 'INCOMPLET', NULL, NULL),
    -- non reclame
    ('1A1200005', 'LRAR', 'NON_RECLAME', DATEADD(DAY, -4, @aujourdhui), NULL),
    -- LR non envoye : modalite « pas a gerer » (PRMLI_A_GERER = 0), remise en emission
    ('1A1200006', 'LR', 'PAS_ENVOYE', DATEADD(DAY, -5, @aujourdhui), NULL),
    -- pli dont l'accuse n'est pas revenu (aucune modalite) : ecarte -- jumeau retenu 1A1200001
    ('1A1200007', 'LRAR', NULL, NULL, NULL),
    -- lettre recommandee electronique, retour ecrit par le flux de La Poste : ecartee -- jumeau retenu 1A1200001
    ('1A1200008', 'CIPREMIUM_LRAR', 'RECEPTION', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- modalite vide (la V2 la tient pour « pas encore recu », l. 707) : ecarte -- jumeau retenu 1A1200001
    ('1A1200009', 'LRAR', '', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- modalite sans parametrage (INCONNU de eTypeReceptionLrar) : retours anormaux, son code dans les precisions
    ('1A1200010', 'LRAR', 'INCONNU', DATEADD(DAY, -6, @aujourdhui), NULL);

-- LOT 11 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19) : des plis a SCANNER (sans retour), pour la recherche par code-barre
-- et le geste « Enregistrer le retour ». Plis 1A21101xx, editions 21101xx, emis le 20/03/2001 (dates fixes). Les demandes d'edition
-- entrent aussi dans la « File des editions » (PROC_POOLEDITION.attendu, sans date de creation). La colonne DMNED_LOT (vide a la
-- remise en emission) est ajoutee si ce jeu la trouve absente, au type de RIG_DEV.
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_NUM_LRAR') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_NUM_LRAR varchar(13) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_NUM_GROUPE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_NUM_GROUPE varchar(40) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_LOT') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_LOT varchar(255) NULL;
EXEC sp_executesql N'
INSERT INTO v2.LRAR_INFOS (LRARI_NUM_LRAR, LRARI_TYPE, LRARI_TYPE_DATE, LRARI_DATE_RECEPTION) VALUES
    -- pas encore revenu, emis par une convocation : le retour se saisit (bloc 5 du .gestes)
    (''1A2110101'', ''LRAR'', NULL, NULL),
    -- pas encore revenu, dans une file differee : « Pli non envoye » le remet en emission (bloc 6)
    (''1A2110102'', ''LRAR'', NULL, NULL),
    -- pas encore revenu, mais aucune demande d''edition ne l''a emis : la V2 ne le reconnait pas -- jumeau 1A2110101
    (''1A2110103'', ''LRAR'', NULL, NULL),
    -- lettre electronique, son retour vient de La Poste : ni cherchee ni saisie ici -- jumeau 1A2110101
    (''1A2110105'', ''CIPREMIUM_LRAR'', NULL, NULL);
-- Les demandes d''edition, en table temporaire : copiees ensuite avec les quatre cases NOT NULL sans defaut que PROC_REGJUD.sql pose
-- comme dans la base V3 (DMNED_HAS_NUMLRAR...) a 0, la valeur par defaut de RIG_DEV, si elles sont la (PROC_RELANCELOT.sql).
SELECT * INTO #Editions FROM (VALUES
    (2110101, ''E'', ''Convocation du banc 11'', ''RETOUR ALPHA SARL'', CONVERT(datetime, ''20010320 10:00''), ''1A2110101'', CAST(NULL AS varchar(255))),
    (2110102, ''C'', ''Convocation du banc 11'', ''RETOUR BETA SAS'', CONVERT(datetime, ''20010320 11:00''), ''1A2110102'', ''LRAR''),
    (2110105, ''E'', ''Lettre electronique du banc 11'', ''RETOUR GAMMA SARL'', CONVERT(datetime, ''20010320 12:00''), ''1A2110105'', NULL),
    -- l''edition qui a emis 1A1200004 (incomplet, sans date) : sa date se corrige (bloc 7)
    (2110104, ''E'', ''Convocation du banc 11'', ''RETOUR DELTA SAS'', CONVERT(datetime, ''20010320 09:00''), ''1A1200004'', NULL))
    AS e (Id, Phase, Libelle, Destinataire, PreEmission, Lrar, Lot);
IF COL_LENGTH(''v2.DEMANDE_EDITION'', ''DMNED_MASQUER_COFFRE_FORT'') IS NOT NULL
    EXEC (''INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_DESIGNATION_DESTINATAIRE,
            DMNED_DATE_PRE_EMISSION, DMNED_NUM_LRAR, DMNED_LOT, DMNED_HAS_NUMLRAR, DMNED_IS_NON_SUPPRESSIBLE, DMNED_HAS_NUMLR, DMNED_MASQUER_COFFRE_FORT)
          SELECT Id, Phase, Libelle, Destinataire, PreEmission, Lrar, Lot, 0, 0, 0, 0 FROM #Editions'');
ELSE
    INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_DESIGNATION_DESTINATAIRE,
                                    DMNED_DATE_PRE_EMISSION, DMNED_NUM_LRAR, DMNED_LOT)
    SELECT Id, Phase, Libelle, Destinataire, PreEmission, Lrar, Lot FROM #Editions;';
