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
