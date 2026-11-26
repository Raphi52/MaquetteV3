-- « Relancer un fichier » (LecteurTraitementsEdition) : des files d'edition et leurs groupes (traitements d'edition) de la chaine
-- « lot 1 », aux colonnes de RIG_DEV (lu le 26/11/2026). Aucun autre jeu n'a ces tables. Les editions d'un groupe se comptent par
-- ses destinataires (INFO_DESTINATAIRE) et leurs lignes (LIGNE_EDITION) d'une DEMANDE : ce jeu pose UNE demande (1900951), pour
-- les editions des groupes clos 1900917 et 1900918 que « Tout réimprimer » remet (PROC_RELANCEFILE.gestes) ; ses autres groupes ont
-- 0 edition, la regle « clos et vide » se controle sur eux.
IF OBJECT_ID(N'v2.FILE_EDITION') IS NULL
    CREATE TABLE v2.FILE_EDITION (FLEDT_ID_FLEDT int NOT NULL PRIMARY KEY, FLEDT_LIBELLE varchar(35) NULL, FLEDT_NUM_ORDRE int NULL,
        FLEDT_NOM_RIG varchar(30) NULL, FLEDT_ACTIVE bit NULL);
IF OBJECT_ID(N'v2.TRAITEMENT_EDITION') IS NULL
    CREATE TABLE v2.TRAITEMENT_EDITION (TRTED_ID_TRTED int NOT NULL PRIMARY KEY, TRTED_ID_FLEDT int NULL, TRTED_DATE_OUVERTURE datetime NULL,
        TRTED_CLOTURE bit NULL, TRTED_DATE_CLOTURE datetime NULL, TRTED_FIN_TRANSMISSION bit NULL, TRTED_IMPRIMANTE1 varchar(255) NULL);
IF OBJECT_ID(N'v2.INFO_DESTINATAIRE') IS NULL
    CREATE TABLE v2.INFO_DESTINATAIRE (INFDS_ID_INFDS int NOT NULL PRIMARY KEY, INFDS_ID_TRTED int NULL);
-- L'etat d'impression d'une edition aux types de RIG_DEV (INFORMATION_SCHEMA lu le 27/11/2026) : ce que « Tout réimprimer » ecrit.
-- Aucun autre jeu ni la base du banc ne creent cette table : toutes ses colonnes sont posees ici (un ALTER suivi d'un INSERT
-- dans le meme lot SQL ne compilerait pas).
IF OBJECT_ID(N'v2.LIGNE_EDITION') IS NULL
    CREATE TABLE v2.LIGNE_EDITION (LGEDT_ID_LGEDT int NOT NULL PRIMARY KEY, LGEDT_ID_DMND int NULL, LGEDT_ID_INFDS int NULL,
        LGEDT_IMPRIMER bit NOT NULL DEFAULT 0, LGEDT_ETAT_EDT varchar(1) NULL, LGEDT_ETAT varchar(1) NOT NULL DEFAULT '0',
        LGEDT_DATE_EDT datetime NULL, LGEDT_DIFFERE bit NOT NULL DEFAULT 0, LGEDT_DESTINATION_PHYSIQUE varchar(50) NULL);
IF OBJECT_ID(N'v2.DEMANDE') IS NULL
    CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_ID_DSRCS int NULL, DMND_ID_ACTRC int NULL,
        DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL, DMND_CODE_PROSS varchar(40) NULL,
        DMND_DATE_CREATION datetime NULL, DMND_DATE_VALIDATION datetime NULL);

-- Les files, dans l'ordre FLEDT_NUM_ORDRE : 1900901 « BANC9 RCS » (ordre 1), 1900902 sans nom RIG (nommee par son libelle, ordre
-- 2), 1900903 « AMIBANC9 », une file Amitel, ecartee pour un agent du greffe (OPE_RECHERCHELOT.cs:38-40) ; jumeau : 1900901.
INSERT INTO v2.FILE_EDITION (FLEDT_ID_FLEDT, FLEDT_LIBELLE, FLEDT_NUM_ORDRE, FLEDT_NOM_RIG, FLEDT_ACTIVE) VALUES
    (1900901, 'RCS du banc 9', 1, 'BANC9 RCS', 1), (1900902, 'Courriers du banc 9', 2, NULL, 1), (1900903, 'Amitel du banc 9', 0, 'AMIBANC9', 1);

-- Les groupes : 1900911 ouvert ; 1900912 clos (date de cloture ET TRTED_CLOTURE) et vide -- masque (OPE_LISTVIEWGRP.cs:152-155) ;
-- jumeaux acceptes : 1900913, cloture posee sans date (pas clos pour RIG v2, IsCloture) ; 1900911, ouvert et vide.
-- 1900914 : dans la file Amitel ; 1900915, 1900916 : la seconde file, l'ordre « TRTED_FIN_TRANSMISSION desc, TRTED_ID_TRTED ».
-- 1900917 : clos, transmission finie, 2 editions imprimees dont une que RIG v2 imprime (LGEDT_IMPRIMER) -- « Tout réimprimer » ;
-- 1900918 : clos, en transmission, 1 edition que RIG v2 n'imprime pas -- « Tout réimprimer » refuse (rien a remettre), jumeau 1900917.
INSERT INTO v2.TRAITEMENT_EDITION (TRTED_ID_TRTED, TRTED_ID_FLEDT, TRTED_DATE_OUVERTURE, TRTED_CLOTURE, TRTED_DATE_CLOTURE, TRTED_FIN_TRANSMISSION, TRTED_IMPRIMANTE1) VALUES
    (1900911, 1900901, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, 'Imprimante 1er étage'),
    (1900912, 1900901, DATEADD(hour, 7, DATEADD(day, -3, @aujourdhui)), 1, DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 1, 'Imprimante 1er étage'),
    (1900913, 1900901, DATEADD(hour, 7, DATEADD(day, -2, @aujourdhui)), 1, NULL, 0, 'Imprimante 1er étage'),
    (1900914, 1900903, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, 'Imprimante Amitel'),
    (1900915, 1900902, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, NULL),
    (1900916, 1900902, DATEADD(hour, 7, DATEADD(day, -4, @aujourdhui)), 0, NULL, 1, NULL),
    (1900917, 1900901, DATEADD(hour, 7, DATEADD(day, -3, @aujourdhui)), 1, DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 1, 'Imprimante 1er étage'),
    (1900918, 1900901, DATEADD(hour, 7, DATEADD(day, -2, @aujourdhui)), 1, DATEADD(hour, 18, DATEADD(day, -2, @aujourdhui)), 0, 'Imprimante 1er étage');

-- La demande des editions (validee, sans dossier ni affaire).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS) VALUES (1900951, 'D1900951', 'V', 'BANC9');
INSERT INTO v2.INFO_DESTINATAIRE (INFDS_ID_INFDS, INFDS_ID_TRTED) VALUES (1900961, 1900917), (1900962, 1900918);
-- 1900971 : imprimee (E), differee, que RIG v2 imprime ; 1900972 : imprimee, que RIG v2 n'imprime pas (LGEDT_IMPRIMER = 0) -- gardee
-- telle quelle par « Tout réimprimer » ; 1900973 : du groupe 1900918, que RIG v2 n'imprime pas.
INSERT INTO v2.LIGNE_EDITION (LGEDT_ID_LGEDT, LGEDT_ID_DMND, LGEDT_ID_INFDS, LGEDT_IMPRIMER, LGEDT_ETAT_EDT, LGEDT_ETAT, LGEDT_DATE_EDT, LGEDT_DIFFERE,
    LGEDT_DESTINATION_PHYSIQUE) VALUES
    (1900971, 1900951, 1900961, 1, 'E', '1', DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 1, 'Différée'),
    (1900972, 1900951, 1900961, 0, 'E', '1', DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 1, 'Différée'),
    (1900973, 1900951, 1900962, 0, 'E', '1', DATEADD(hour, 18, DATEADD(day, -2, @aujourdhui)), 0, 'Imprimante 1er étage');
