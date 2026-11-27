-- Jeu de LecteurArticles / EcrivainArticles (« Articles tarifaires », PROC_ARTICLE), lot 13 du plan-676a4b19. Identifiants 23001xx,
-- codes « L13... ». Ce qui est retenu ou ecarte, et pourquoi : PROC_ARTICLE.attendu, .recherches et .gestes.
-- Types : ceux de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le 27/11/2026 : ARTICLE, ARTICLE_GROUPE, ARTICLE_TVA), colonnes lues
-- ou ecrites. v2.ARTICLE a la definition de PROC_TARIFS.sql et PROC_RETAUD.sql (joues apres), plus les colonnes que la creation ecrit
-- (nullables ou a DEFAULT : les autres jeux y inserent sans elles) ; les lignes passent par sp_executesql (une colonne ajoutee dans ce
-- lot n'est pas connue a la compilation du lot).
-- La grille lit TOUS les articles PR et TI actifs de la base : ceux de PROC_RETAUD.sql et PROC_TARIFS.sql entrent dans son .attendu ;
-- les principaux actifs de ce jeu entrent dans le bloc « Type d'article = PR » de PROC_TARIFS.recherches.
-- Pas de titre (TI) ici : un titre actif entrerait dans la liste par defaut des Tarifs. Les titres de la grille sont ceux de
-- PROC_TARIFS.sql (L6TI01, compose de ses principaux : 25,50).
IF OBJECT_ID(N'v2.ARTICLE') IS NULL
    CREATE TABLE v2.ARTICLE (ARTCL_ID_ARTCL int NOT NULL PRIMARY KEY, ARTCL_CODE_ARTICLE_RIG varchar(18) NOT NULL, ARTCL_LIBELLE varchar(512) NOT NULL,
        ARTCL_PRIX_UHT money NOT NULL, ARTCL_TYPE_ARTICLE varchar(10) NOT NULL, ARTCL_DATE_APPLICATION datetime NOT NULL, ARTCL_STATUT varchar(1) NULL,
        ARTCL_CODE_ARTICLE_GREFFE varchar(18) NOT NULL, ARTCL_ID_ARTCA int NULL);
IF COL_LENGTH('v2.ARTICLE', 'ARTCL_DATE_CREATION') IS NULL ALTER TABLE v2.ARTICLE ADD ARTCL_DATE_CREATION datetime NULL;
IF COL_LENGTH('v2.ARTICLE', 'ARTCL_ID_UTLST') IS NULL ALTER TABLE v2.ARTICLE ADD ARTCL_ID_UTLST int NULL;
IF COL_LENGTH('v2.ARTICLE', 'ARTCL_ID_ARTGR') IS NULL ALTER TABLE v2.ARTICLE ADD ARTCL_ID_ARTGR int NULL;
IF COL_LENGTH('v2.ARTICLE', 'ARTCL_HUISSIER') IS NULL ALTER TABLE v2.ARTICLE ADD ARTCL_HUISSIER bit NOT NULL CONSTRAINT DF_L13_HUISSIER DEFAULT (0);
IF COL_LENGTH('v2.ARTICLE', 'ARTCL_REGROUPE_FACTURE') IS NULL ALTER TABLE v2.ARTICLE ADD ARTCL_REGROUPE_FACTURE bit NOT NULL CONSTRAINT DF_L13_REGROUPE DEFAULT (0);
IF OBJECT_ID(N'v2.ARTICLE_COMPOSITION') IS NULL
    CREATE TABLE v2.ARTICLE_COMPOSITION (X_ARTCP_ID_ARTCL_PERE int NOT NULL, X_ARTCP_ID_ARTCL_FILS int NOT NULL, X_ARTCP_QUANTITE int NULL,
        X_ARTCP_CLASSEMENT int NULL);
IF OBJECT_ID(N'v2.ARTICLE_TVA') IS NULL
    CREATE TABLE v2.ARTICLE_TVA (ARTTV_ID_ARTTV int NOT NULL PRIMARY KEY, ARTTV_CODE varchar(10) NOT NULL, ARTTV_TAUX_TVA real NULL,
        ARTTV_DATE_APPLICATION datetime NULL);
IF OBJECT_ID(N'v2.ARTICLE_GROUPE') IS NULL
    CREATE TABLE v2.ARTICLE_GROUPE (ARTGR_ID_ARTGR int NOT NULL PRIMARY KEY, ARTGR_TYPE_ARTICLE varchar(10) NOT NULL, ARTGR_ID_ARTTV int NOT NULL);

-- Trois taux, trois groupes : la creation prend le groupe PR du taux le plus recent (2300191, PROC_ARTICLE.cs:115-117) ; le groupe
-- TI 2300193 a un taux plus recent encore, mais il n'est pas un groupe d'articles principaux -- jumeau ecarte.
INSERT INTO v2.ARTICLE_TVA (ARTTV_ID_ARTTV, ARTTV_CODE, ARTTV_TAUX_TVA, ARTTV_DATE_APPLICATION) VALUES
    (2300181, 'L13T19', 19.6, '20000101'), (2300182, 'L13T20', 20, '20140101'), (2300183, 'L13T21', 21, '20300101');
INSERT INTO v2.ARTICLE_GROUPE (ARTGR_ID_ARTGR, ARTGR_TYPE_ARTICLE, ARTGR_ID_ARTTV) VALUES
    (2300190, 'PR', 2300181), (2300191, 'PR', 2300182), (2300193, 'TI', 2300183);

EXEC sp_executesql N'
INSERT INTO v2.ARTICLE (ARTCL_ID_ARTCL, ARTCL_CODE_ARTICLE_RIG, ARTCL_LIBELLE, ARTCL_PRIX_UHT, ARTCL_TYPE_ARTICLE, ARTCL_DATE_APPLICATION,
    ARTCL_STATUT, ARTCL_CODE_ARTICLE_GREFFE, ARTCL_DATE_CREATION, ARTCL_ID_UTLST, ARTCL_ID_ARTGR) VALUES
    -- un principal en vigueur, deux versions : la plus recente (2300102, 2025) est la ligne -- jumeau ecarte 2300101 (2024)
    (2300101, ''L13PR01'', ''Copie banc 2024'', 1.50, ''PR'', ''20240101'', ''A'', ''L13PR01'', ''20240101'', 7, 2300191),
    (2300102, ''L13PR01'', ''Copie banc'', 2.25, ''PR'', ''20250101'', ''A'', ''L13PR01'', ''20250101'', 7, 2300191),
    -- un principal applicable dans un an : « À venir »
    (2300103, ''L13PR03'', ''Extrait banc futur'', 3.10, ''PR'', DATEADD(YEAR, 1, @aujourdhui), ''A'', ''L13PR03'', ''20250101'', 7, 2300191),
    -- ECARTE a l''ouverture : en sommeil (S) -- jumeau retenu 2300102 (A) ; retrouve par « Statut = tous »
    (2300104, ''L13PR04'', ''Ancien tarif banc'', 4.00, ''PR'', ''20200101'', ''S'', ''L13PR04'', ''20200101'', 7, 2300191),
    -- ECARTE de la grille : une charge (BO) -- jumeau retenu 2300102 (PR) ; la V2 ne l''ouvre pas comme article
    (2300106, ''L13BO06'', ''Frais banc'', 1.00, ''BO'', ''20250101'', ''A'', ''L13BO06'', ''20250101'', 7, 2300191);', N'@aujourdhui datetime', @aujourdhui;
