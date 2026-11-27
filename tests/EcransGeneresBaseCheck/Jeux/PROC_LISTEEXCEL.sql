-- Jeu de LecteurListesExcel (« Export Excel », PROC_LISTEEXCEL), lot 11 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19).
-- Listes parametrees 2110501-2110503, codes de champ BANC11_*. Ce qui est retenu ou ecarte, et pourquoi : PROC_LISTEEXCEL.attendu.
-- Les tables du parametrage des listes Excel, aux types de RIG_DEV (sys.columns de SQL-DEV\DEV, lu le 27/11/2026) ; aucun autre jeu
-- ni ecran ne les lit. Leur cle (RIG_DEV n'en declare pas sur PARAMETRAGE_LISTE_XLS) : le code du champ, unique dans RIG_DEV.
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE (PRMLLXTYP_CODE varchar(50) NOT NULL PRIMARY KEY, PRMLLXCMP_LIBELLE varchar(50) NOT NULL);
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE_XLS') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE_XLS (PRMLX_TYPE_LISTE varchar(50) NOT NULL, PRMLX_TEXTE varchar(7000) NOT NULL, PRMLX_NUM_ORDRE int NULL,
        PRMLX_LIBELLE varchar(50) NOT NULL, PRMLX_TYPE_TEXTE varchar(10) NOT NULL, PRMLX_NUM_ORDRE_INIT int NOT NULL,
        PRMLX_CODE_CHAMP varchar(30) NOT NULL PRIMARY KEY);
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE_XLS_LISTE') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE_XLS_LISTE (PRMLLXLIST_ID_PRMLLXLIST int NOT NULL PRIMARY KEY, PRMLLXLIST_CODE varchar(50) NOT NULL,
        PRMLLXLIST_LIBELLE varchar(100) NULL, PRMLLXLIST_TYPE varchar(50) NOT NULL);
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE_XLS_COMPOSITION') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE_XLS_COMPOSITION (PRMLLXCMP_ID_PRMLLXCMP int NOT NULL PRIMARY KEY, PRMLLXCMP_ID_LISTE int NOT NULL,
        PRMLLXCMP_CODE_CHAMPS varchar(30) NOT NULL, PRMLLXCMP_NUM_ORDRE int NOT NULL);

-- Les types (libelles de RIG_DEV) : quatre de l'ecran, et PC, les procedures, d'un autre ecran (ecarte -- jumeau INS).
INSERT INTO v2.PARAMETRAGE_LISTE_XLS_TYPE_LISTE (PRMLLXTYP_CODE, PRMLLXCMP_LIBELLE) VALUES
    ('AFF', 'Liste affaires en cours'), ('GIE', 'Liste citations/affaires signifiées'), ('IDJ', 'Liste des instances par date DJ'),
    ('INS', 'Liste des instances'), ('PC', 'Liste procédures');
-- Les champs : INS a trois SELECT dont deux ordonnes (la liste par defaut), son FROM (ecarte du compte des colonnes -- jumeau les
-- SELECT) et sa ligne LISTE qui nomme la liste par defaut BANC11DEF ; IDJ un SELECT ordonne ; AFF sa ligne LISTE « RIEN », comme
-- RIG_DEV (aucune liste de ce code : pas de liste par defaut) ; GIE aucun champ.
INSERT INTO v2.PARAMETRAGE_LISTE_XLS (PRMLX_TYPE_LISTE, PRMLX_TEXTE, PRMLX_NUM_ORDRE, PRMLX_LIBELLE, PRMLX_TYPE_TEXTE, PRMLX_NUM_ORDRE_INIT, PRMLX_CODE_CHAMP) VALUES
    ('INS', 'INSTN_NUM_ROLE AS [Num Rôle]', 1, 'Num Rôle', 'SELECT', 1, 'BANC11_C1'),
    ('INS', 'INSTN_DATE_ENROLEMENT AS [Date enrôl.]', 2, 'Date enrôl.', 'SELECT', 2, 'BANC11_C2'),
    ('INS', 'INSTN_NOM_COURANT AS Nom', NULL, 'Nom', 'SELECT', 3, 'BANC11_C3'),
    ('INS', ' FROM INSTANCE', 1, 'FROM', 'FROM', 1, 'BANC11_FROM'),
    ('INS', '', 1, 'BANC11DEF', 'LISTE', 1, 'BANC11_LISTE'),
    ('IDJ', 'DCJDC_DATE AS [Date DJ]', 1, 'Date DJ', 'SELECT', 1, 'BANC11_IDJ1'),
    ('AFF', '', 1, 'RIEN', 'LISTE', 1, 'BANC11_AFFL');
-- Les listes parametrees : BANC11DEF (la liste par defaut d'INS, deux colonnes), BANC11B sans libelle (son code tient lieu de nom,
-- aucune colonne), BANC11PC d'un type d'un autre ecran (ecartee -- jumelle BANC11DEF).
INSERT INTO v2.PARAMETRAGE_LISTE_XLS_LISTE (PRMLLXLIST_ID_PRMLLXLIST, PRMLLXLIST_CODE, PRMLLXLIST_LIBELLE, PRMLLXLIST_TYPE) VALUES
    (2110501, 'BANC11DEF', 'Liste du banc 11', 'INS'), (2110502, 'BANC11B', NULL, 'INS'), (2110503, 'BANC11PC', 'Procédures du banc 11', 'PC');
INSERT INTO v2.PARAMETRAGE_LISTE_XLS_COMPOSITION (PRMLLXCMP_ID_PRMLLXCMP, PRMLLXCMP_ID_LISTE, PRMLLXCMP_CODE_CHAMPS, PRMLLXCMP_NUM_ORDRE) VALUES
    (2110511, 2110501, 'BANC11_C1', 1), (2110512, 2110501, 'BANC11_C3', 2), (2110513, 2110503, 'BANC11_C1', 1);
