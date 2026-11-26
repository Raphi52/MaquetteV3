-- « Relancer un lot » (LecteurGroupesImpression, EcrivainGroupesImpression) : des groupes d'impression des lots de
-- PROC_GESTLOT.sql (joue avant celui-ci), une phase par etat de RIG v2 (DemandeEditionPourServices.cs : I, P, PT, PZ, IZ, E).
-- Types de RIG_DEV (lu le 26/11/2026) ; les tables sont creees ici aussi si ce jeu est seul.
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
IF OBJECT_ID(N'v2.DEMANDE_EDITION') IS NULL
    CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY, DMNED_PHASE_TRAITEMENT varchar(10) NOT NULL,
        DMNED_LIBELLE_DOCUMENT varchar(255) NOT NULL);
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_NUM_GROUPE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_NUM_GROUPE varchar(40) NULL;

-- G02 (emis, AUCUNE edition) n'est pas affiche (OPE_LISTVIEWGRP.cs:154-155) ; jumeau : G01, emis avec une edition. G03 (initial,
-- aucune edition) l'est : la regle ne vise que l'emis. G07 est dans la file Amitel AMI_BANC9 : ecarte (OPE_RECHERCHELOT.cs:38-39) ;
-- jumeau : G01. G09 est d'un lot absent de la table LOT : RIG v2 ne l'atteint par aucun lot ; jumeau : G01.
INSERT INTO v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE, GPIMP_NOM_LOT, GPIMP_DATE_DEBUT, GPIMP_DATE_FIN, GPIMP_NOM_IMPRIMANTE, GPIMP_PHASE_TRAITEMENT) VALUES
    ('BANC9-G01', 'BANC9_COURRIERS', DATEADD(hour, 10, DATEADD(day, -3, @aujourdhui)), DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 'Imprimante 1er étage', 'E'),
    ('BANC9-G02', 'BANC9_COURRIERS', DATEADD(hour, 9, DATEADD(day, -3, @aujourdhui)), DATEADD(hour, 17, DATEADD(day, -3, @aujourdhui)), 'Imprimante 1er étage', 'E'),
    ('BANC9-G03', 'BANC9_COURRIERS', DATEADD(hour, 10, DATEADD(day, -1, @aujourdhui)), NULL, 'Imprimante 1er étage', 'I'),
    ('BANC9-G04', 'BANC9_RELANCES', DATEADD(hour, 10, DATEADD(day, -2, @aujourdhui)), NULL, 'Imprimante 2e étage', 'PZ'),
    ('BANC9-G05', 'BANC9_RELANCES', DATEADD(hour, 10, DATEADD(day, -4, @aujourdhui)), NULL, 'Imprimante 2e étage', 'P'),
    ('BANC9-G06', 'BANC9_DIRECT', DATEADD(hour, 10, DATEADD(day, -5, @aujourdhui)), NULL, 'Imprimante accueil', 'PT'),
    ('BANC9-G07', 'AMI_BANC9', DATEADD(hour, 10, DATEADD(day, -1, @aujourdhui)), NULL, 'Imprimante Amitel', 'E'),
    ('BANC9-G08', 'BANC9_DIRECT', DATEADD(hour, 10, DATEADD(day, -6, @aujourdhui)), NULL, 'Imprimante accueil', 'IZ'),
    ('BANC9-G09', 'BANC9_SANS_LOT', DATEADD(hour, 10, DATEADD(day, -1, @aujourdhui)), NULL, 'Imprimante 1er étage', 'E');

-- Les editions des groupes (DMNED_NUM_GROUPE) : une pour G01, deux pour G04 (« Tout réimprimer » les remet a imprimer), une pour
-- G07 et une pour G09, qui restent ecartes. Elles entrent aussi dans « File des éditions » (PROC_POOLEDITION.attendu).
-- (sp_executesql : DMNED_NUM_GROUPE peut etre ajoutee par ce meme jeu, ci-dessus.) Les quatre cases NOT NULL sans defaut que
-- PROC_REGJUD.sql pose comme dans la base V3 (DMNED_HAS_NUMLRAR...) recoivent 0, la valeur par defaut de RIG_DEV.
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_MASQUER_COFFRE_FORT') IS NOT NULL
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_NUM_GROUPE, DMNED_HAS_NUMLRAR,
                                DMNED_IS_NON_SUPPRESSIBLE, DMNED_HAS_NUMLR, DMNED_MASQUER_COFFRE_FORT) VALUES
    (1900401, ''E'', ''Courrier du banc 9'', ''BANC9-G01'', 0, 0, 0, 0),
    (1900402, ''PZ'', ''Relance du banc 9'', ''BANC9-G04'', 0, 0, 0, 0),
    (1900403, ''E'', ''Relance du banc 9'', ''BANC9-G04'', 0, 0, 0, 0),
    (1900404, ''E'', ''Courrier Amitel du banc 9'', ''BANC9-G07'', 0, 0, 0, 0),
    (1900405, ''E'', ''Courrier sans lot du banc 9'', ''BANC9-G09'', 0, 0, 0, 0);';
ELSE
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE_EDITION (DMNED_ID_DMNED, DMNED_PHASE_TRAITEMENT, DMNED_LIBELLE_DOCUMENT, DMNED_NUM_GROUPE) VALUES
    (1900401, ''E'', ''Courrier du banc 9'', ''BANC9-G01''),
    (1900402, ''PZ'', ''Relance du banc 9'', ''BANC9-G04''),
    (1900403, ''E'', ''Relance du banc 9'', ''BANC9-G04''),
    (1900404, ''E'', ''Courrier Amitel du banc 9'', ''BANC9-G07''),
    (1900405, ''E'', ''Courrier sans lot du banc 9'', ''BANC9-G09'');';
