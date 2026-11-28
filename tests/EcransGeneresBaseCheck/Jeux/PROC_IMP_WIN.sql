-- Jeu de LecteurImprimantesGreffe (« Imprimantes du greffe », PROC_IMP_WIN), lot 6 du plan « RIG v3 complet » (plan-f4fb097f,
-- 28/11/2026). Les imprimantes logiques de l'onglet « Lots » de RIG v2 : lot 1 (IMPRIMANTE_PS) et lot 2 (IMPRIMANTE_EDITION, ses
-- types de papier dans TAG_POSTSCRIPT), tables de la BASE DU GREFFE. Noms « Banc lot 6 ... » : aucun autre jeu ne les porte.
-- Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 28/11/2026 ; cles PK_IMPRIMANTE_PS,
-- PK_IMPRIMANTE_EDITION, PK_TAG_POSTSCRIPT et valeurs par defaut de RIG_DEV), creees si absentes, completees sinon (PROC_COURRIERM
-- cree IMPRIMANTE_EDITION avant ce jeu). Lignes par sp_executesql (voir PROC_MJUD.sql).
IF OBJECT_ID(N'v2.IMPRIMANTE_PS') IS NULL
    CREATE TABLE v2.IMPRIMANTE_PS (R_IMPRIMANTE_VALEUR varchar(50) NOT NULL PRIMARY KEY, R_IMPRIMANTE_LIBELLE_ECRAN varchar(50) NULL,
        R_IMPRIMANTE_SYNONYME varchar(50) NULL, R_IMPRIMANTE_ETAT varchar(1) NOT NULL, R_IMPRIMANTE_UTILISABLE_SAISIE bit NOT NULL DEFAULT 0,
        R_IMPRIMANTE_CONFIG_TYPE bit NOT NULL DEFAULT 0, R_IMPRIMANTE_EMPLACEMENT_IMP varchar(100) NULL DEFAULT '',
        R_IMPRIMANTE_DIFFEREE bit NOT NULL DEFAULT 0, R_IMPRIMANTE_ACTIVATION bit NOT NULL DEFAULT 0);
IF OBJECT_ID(N'v2.IMPRIMANTE_EDITION') IS NULL
    CREATE TABLE v2.IMPRIMANTE_EDITION (IMPED_NOM_LOGIQUE varchar(255) NOT NULL PRIMARY KEY, IMPED_NOM_PHYSIQUE varchar(255) NULL,
        IMPED_ONLY_AMITEL bit NOT NULL DEFAULT 0);
IF OBJECT_ID(N'v2.TAG_POSTSCRIPT') IS NULL
    CREATE TABLE v2.TAG_POSTSCRIPT (TAGPS_NOM_TAG varchar(255) NOT NULL, TAGPS_NOM_IMPRIMANTE varchar(255) NOT NULL,
        TAGPS_CHAINE_POSTSCRIPT varchar(max) NULL, TAGPS_BAC_MANUEL bit NOT NULL DEFAULT 0, PRIMARY KEY (TAGPS_NOM_TAG, TAGPS_NOM_IMPRIMANTE));

EXEC sp_executesql N'
-- Lot 1 : redirigee et active (Validee), sans imprimante physique (AFaire, la valeur par defaut de RIG_DEV est « »), inactive (EnCours).
INSERT INTO v2.IMPRIMANTE_PS (R_IMPRIMANTE_VALEUR, R_IMPRIMANTE_LIBELLE_ECRAN, R_IMPRIMANTE_SYNONYME, R_IMPRIMANTE_ETAT,
    R_IMPRIMANTE_EMPLACEMENT_IMP, R_IMPRIMANTE_DIFFEREE, R_IMPRIMANTE_ACTIVATION) VALUES
    (''Banc lot 6 Accueil'', ''Banc lot 6 Accueil'', ''Banc lot 6 Accueil'', ''0'', ''PS'', 1, 1),
    (''Banc lot 6 Copieuse'', ''Banc lot 6 Copieuse'', ''Banc lot 6 Copieuse'', ''0'', '''', 0, 1),
    (''Banc lot 6 Plieuse'', ''Banc lot 6 Plieuse'', ''Banc lot 6 Plieuse'', ''0'', ''Plieuse 12'', 0, 0);

-- Lot 2 : avec ses trois types de papier et un tag qui n''est pas un bac (AGRAFE : il reste quand les types changent), et sans
-- imprimante physique ni type.
INSERT INTO v2.IMPRIMANTE_EDITION (IMPED_NOM_LOGIQUE, IMPED_NOM_PHYSIQUE, IMPED_ONLY_AMITEL) VALUES
    (''Banc lot 6 Greffe'', ''RL'', 0), (''Banc lot 6 Archives'', NULL, 0);
INSERT INTO v2.TAG_POSTSCRIPT (TAGPS_NOM_TAG, TAGPS_NOM_IMPRIMANTE, TAGPS_CHAINE_POSTSCRIPT, TAGPS_BAC_MANUEL) VALUES
    (''BAC_NORMAL'', ''Banc lot 6 Greffe'', '''', 0), (''BAC_SECURISE'', ''Banc lot 6 Greffe'', '''', 0),
    (''BAC_LRAR'', ''Banc lot 6 Greffe'', '''', 0), (''AGRAFE'', ''Banc lot 6 Greffe'', ''%%AGRAFE'', 0);';
