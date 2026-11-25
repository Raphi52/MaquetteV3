-- Jeu de LecteurTransmissionDocuments (« Transmission », PROC_TRANSDOC), lot 2 du plan « brancher sur la base les
-- procedures encore en exemple » (plan-f7ba4de2). Identifiants 12000xx. Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_TRANSDOC.attendu. Aucune ligne n'entre dans un autre ecran branche : codes processus DOC_TRIDIG et DCOFFRE seuls,
-- sans dossier ni parametre, et aucune ligne dans v2.DEMANDE_EDITION (elle entrerait dans PROC_POOLEDITION).
-- v2.DEMANDE existe deja dans le banc, sans DMND_NOM (le libelle de la grille V2) : il lui est ajoute, au type de RIG_DEV.
-- Les deux tables des documents, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- colonnes lues. Les lignes passent par sp_executesql : une colonne ajoutee dans ce meme lot d'instructions n'existe pas
-- encore quand SQL Server le compile.
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF OBJECT_ID(N'v2.DEMAT_TRIBUNAL_DIGITAL_CREFO') IS NULL
    CREATE TABLE v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO int NOT NULL PRIMARY KEY,
        DTDCREFO_DATE_CREATION datetime NOT NULL, DTDCREFO_ID_DMND int NULL, DTDCREFO_NUM_AFFAIRE varchar(20) NULL);
IF OBJECT_ID(N'v2.DEMAT_CFEC_AJMJ') IS NULL
    CREATE TABLE v2.DEMAT_CFEC_AJMJ (CFECAJ_ID_CFECAJ int NOT NULL PRIMARY KEY, CFECAJ_DATE_CREATION datetime NOT NULL,
        CFECAJ_NUMERO_AFF varchar(255) NULL, CFECAJ_NUM_SIREN varchar(255) NULL, CFECAJ_TYPE_DOC varchar(255) NULL,
        CFECAJ_ID_DMND int NOT NULL);

EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_DATE_CREATION, DMND_NOM) VALUES
    -- TRIBUNAL DIGITAL
    -- qualifiee, sans numero d''affaire : a transmettre
    (1200011, ''26001200011'', ''Q'', ''DOC_TRIDIG'', DATEADD(DAY, -6, @aujourdhui), ''Requête TRIDIG ALPHA''),
    -- qualifiee, numero d''affaire saisi : en cours
    (1200012, ''26001200012'', ''Q'', ''DOC_TRIDIG'', DATEADD(DAY, -4, @aujourdhui), ''Requête TRIDIG BETA''),
    -- integree (V) : ecartee -- jumelle retenue 1200012
    (1200013, ''26001200013'', ''V'', ''DOC_TRIDIG'', DATEADD(DAY, -4, @aujourdhui), ''Requête TRIDIG GAMMA''),
    -- qualifiee mais sans fiche de document : ecartee (OPE_LISTE_FLUX_DOC.cs:674) -- jumelle retenue 1200011
    (1200014, ''26001200014'', ''Q'', ''DOC_TRIDIG'', DATEADD(DAY, -6, @aujourdhui), ''Requête TRIDIG SANS FICHE''),
    -- qualifiee, numero d''affaire blanc : a transmettre ; sans libelle : son numero de demande
    (1200015, ''26001200015'', ''Q'', ''DOC_TRIDIG'', DATEADD(DAY, -1, @aujourdhui), NULL),
    -- COFFRE-FORT
    -- qualifiee, numero d''affaire et SIREN : en cours
    (1200021, ''26001200021'', ''Q'', ''DCOFFRE'', DATEADD(DAY, -5, @aujourdhui), ''2024RJ0020 réception dépôt coffre : bord.pdf, COFFRE ALPHA SASU''),
    -- qualifiee, sans numero d''affaire ni SIREN : a transmettre
    (1200022, ''26001200022'', ''Q'', ''DCOFFRE'', DATEADD(DAY, -2, @aujourdhui), '' réception dépôt coffre : test.pdf,''),
    -- abandonnee (X) puis en traitement (T) : ecartees -- jumelle retenue 1200021
    (1200023, ''26001200023'', ''X'', ''DCOFFRE'', DATEADD(DAY, -5, @aujourdhui), ''COFFRE ABANDONNE''),
    (1200024, ''26001200024'', ''T'', ''DCOFFRE'', DATEADD(DAY, -5, @aujourdhui), ''COFFRE EN TRAITEMENT''),
    -- qualifiee DCOFFRE dont la seule fiche est une fiche du tribunal digital : la V2 lit DEMAT_CFEC_AJMJ pour DCOFFRE
    -- (l. 693) : ecartee -- jumelle retenue 1200022
    (1200025, ''26001200025'', ''Q'', ''DCOFFRE'', DATEADD(DAY, -2, @aujourdhui), ''COFFRE FICHE TRIDIG''),
    -- autre processus (le traitement d''un document, DCOFFRE_GED) avec une fiche : ecartee -- jumelle retenue 1200021
    (1200026, ''26001200026'', ''Q'', ''DCOFFRE_GED'', DATEADD(DAY, -5, @aujourdhui), ''COFFRE GED''),
    -- qualifiee avec deux fiches : une ligne, la premiere fiche (plus petite cle), qui porte le numero
    (1200027, ''26001200027'', ''Q'', ''DCOFFRE'', DATEADD(DAY, -3, @aujourdhui), ''2019RJ477 réception dépôt coffre : crfm.pdf, COFFRE DEUX FICHES'');
INSERT INTO v2.DEMAT_TRIBUNAL_DIGITAL_CREFO (DTDCREFO_ID_DTDCREFO, DTDCREFO_DATE_CREATION, DTDCREFO_ID_DMND, DTDCREFO_NUM_AFFAIRE) VALUES
    (1200111, DATEADD(HOUR, -150, @aujourdhui), 1200011, NULL),
    (1200112, DATEADD(HOUR, -100, @aujourdhui), 1200012, ''2026F00012''),
    (1200113, DATEADD(HOUR, -100, @aujourdhui), 1200013, ''2026F00013''),
    (1200115, DATEADD(HOUR, -20, @aujourdhui), 1200015, ''   ''),
    (1200125, DATEADD(HOUR, -40, @aujourdhui), 1200025, ''2026F00025'');
INSERT INTO v2.DEMAT_CFEC_AJMJ (CFECAJ_ID_CFECAJ, CFECAJ_DATE_CREATION, CFECAJ_NUMERO_AFF, CFECAJ_NUM_SIREN, CFECAJ_TYPE_DOC, CFECAJ_ID_DMND) VALUES
    (1200221, DATEADD(HOUR, -120, @aujourdhui), ''2024RJ0020'', ''852198969'', ''Dépôt en matière de procédure collective'', 1200021),
    (1200222, DATEADD(HOUR, -44, @aujourdhui), NULL, NULL, ''Avis (courrier)'', 1200022),
    (1200223, DATEADD(HOUR, -120, @aujourdhui), ''2024RJ0023'', ''852198923'', ''Avis (courrier)'', 1200023),
    (1200224, DATEADD(HOUR, -120, @aujourdhui), ''2024RJ0024'', ''852198924'', ''Avis (courrier)'', 1200024),
    (1200226, DATEADD(HOUR, -120, @aujourdhui), ''2024RJ0026'', ''852198926'', ''Avis (courrier)'', 1200026),
    (1200227, DATEADD(HOUR, -70, @aujourdhui), ''2019RJ477'', ''842 322 810'', ''Dépôt en matière de procédure collective'', 1200027),
    (1200228, DATEADD(HOUR, -60, @aujourdhui), NULL, NULL, ''Avis (courrier)'', 1200027);',
    N'@aujourdhui datetime', @aujourdhui;
