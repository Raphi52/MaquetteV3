-- « Lots » (LecteurLots, EcrivainLots) : la table des lots d'impression. Les tables v2.LOT et v2.GROUPE_IMPRESSION aux colonnes
-- et aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 26/11/2026). Les lots des groupes de
-- PROC_RELANCELOT.sql sont poses ICI : RIG v2 liste TOUS les lots (Lot.GetListeLot), ils sont donc dans cet ecran.
IF OBJECT_ID(N'v2.LOT') IS NULL
    CREATE TABLE v2.LOT (LOT_NOM varchar(255) NOT NULL PRIMARY KEY, LOT_NUM_ORDRE tinyint NULL, LOT_ACTIF bit NULL,
        LOT_IMPRESSION_AUTO bit NULL, LOT_HEURE_IMPRESSION_AUTO datetime NULL, LOT_NOM_IMPRIMANTE varchar(255) NULL, LOT_DIRECT bit NULL,
        LOT_PERIODE_AFFICHAGE tinyint NULL DEFAULT 0, LOT_CODE_PARAMETRE_EDITION varchar(20) NULL, LOT_TAILLE_POSTSCRIPT int NULL,
        LOT_TRI_AUTO bit NULL, LOT_VISIBLE_EDTDIFF bit NOT NULL DEFAULT 1, LOT_IS_IMPRESSION_SUPPLEMENTAIRE_ACTIVE bit NOT NULL DEFAULT 0,
        LOT_IMPRIMANTE_SUPPLEMENTAIRE varchar(255) NULL);
IF OBJECT_ID(N'v2.GROUPE_IMPRESSION') IS NULL
    CREATE TABLE v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE varchar(40) NOT NULL PRIMARY KEY, GPIMP_NOM_LOT varchar(255) NULL,
        GPIMP_DATE_DEBUT datetime NULL, GPIMP_DATE_FIN datetime NULL, GPIMP_NOM_IMPRIMANTE varchar(255) NULL, GPIMP_NB_TENTATIVES tinyint NULL,
        GPIMP_PHASE_TRAITEMENT varchar(10) NOT NULL);

-- AMI_BANC9 : une file Amitel (le nom commence par « AMI ») -- listee ici comme dans RIG v2 (GESTLOT n'ecarte pas les files
-- Amitel), ecartee de « Relancer un lot ». BANC9_ANCIEN_DIRECT : un second lot direct (inactif), que l'enregistrement du lot
-- direct BANC9_DIRECT doit retirer (PROC_GESTLOT.gestes).
INSERT INTO v2.LOT (LOT_NOM, LOT_NUM_ORDRE, LOT_ACTIF, LOT_IMPRESSION_AUTO, LOT_HEURE_IMPRESSION_AUTO, LOT_NOM_IMPRIMANTE, LOT_DIRECT,
                    LOT_CODE_PARAMETRE_EDITION, LOT_TRI_AUTO, LOT_VISIBLE_EDTDIFF, LOT_IS_IMPRESSION_SUPPLEMENTAIRE_ACTIVE) VALUES
    ('AMI_BANC9', 0, 1, 0, NULL, 'Imprimante Amitel', 0, '', 0, 0, 0),
    ('BANC9_ANCIEN_DIRECT', 0, 0, 0, NULL, 'Imprimante accueil', 1, '', 0, 0, 0),
    ('BANC9_COURRIERS', 0, 1, 1, '20160101 08:30', 'Imprimante 1er étage', 0, '', 0, 0, 0),
    ('BANC9_DIRECT', 0, 1, 0, NULL, 'Imprimante accueil', 1, '', 0, 0, 0),
    ('BANC9_INACTIF', 0, 0, 0, NULL, NULL, 0, '', 0, 0, 0),
    ('BANC9_RELANCES', 0, 1, 0, '20160101 00:00', 'Imprimante 2e étage', 0, '', 0, 0, 0);
