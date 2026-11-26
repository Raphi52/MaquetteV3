-- « Relancer un fichier » (LecteurTraitementsEdition) : des files d'edition et leurs groupes (traitements d'edition) de la chaine
-- « lot 1 », aux colonnes de RIG_DEV (lu le 26/11/2026). Aucun autre jeu n'a ces tables. Les editions d'un groupe se comptent par
-- ses destinataires (INFO_DESTINATAIRE) et leurs lignes (LIGNE_EDITION) d'une DEMANDE : ce jeu ne pose aucune demande (les autres
-- ecrans lisent v2.DEMANDE) -- ses groupes ont 0 edition ; la regle « clos et vide » se controle donc sur des groupes vides.
IF OBJECT_ID(N'v2.FILE_EDITION') IS NULL
    CREATE TABLE v2.FILE_EDITION (FLEDT_ID_FLEDT int NOT NULL PRIMARY KEY, FLEDT_LIBELLE varchar(35) NULL, FLEDT_NUM_ORDRE int NULL,
        FLEDT_NOM_RIG varchar(30) NULL, FLEDT_ACTIVE bit NULL);
IF OBJECT_ID(N'v2.TRAITEMENT_EDITION') IS NULL
    CREATE TABLE v2.TRAITEMENT_EDITION (TRTED_ID_TRTED int NOT NULL PRIMARY KEY, TRTED_ID_FLEDT int NULL, TRTED_DATE_OUVERTURE datetime NULL,
        TRTED_CLOTURE bit NULL, TRTED_DATE_CLOTURE datetime NULL, TRTED_FIN_TRANSMISSION bit NULL, TRTED_IMPRIMANTE1 varchar(255) NULL);
IF OBJECT_ID(N'v2.INFO_DESTINATAIRE') IS NULL
    CREATE TABLE v2.INFO_DESTINATAIRE (INFDS_ID_INFDS int NOT NULL PRIMARY KEY, INFDS_ID_TRTED int NULL);
IF OBJECT_ID(N'v2.LIGNE_EDITION') IS NULL
    CREATE TABLE v2.LIGNE_EDITION (LGEDT_ID_LGEDT int NOT NULL PRIMARY KEY, LGEDT_ID_DMND int NULL, LGEDT_ID_INFDS int NULL);

-- Les files, dans l'ordre FLEDT_NUM_ORDRE : 1900901 « BANC9 RCS » (ordre 1), 1900902 sans nom RIG (nommee par son libelle, ordre
-- 2), 1900903 « AMIBANC9 », une file Amitel, ecartee pour un agent du greffe (OPE_RECHERCHELOT.cs:38-40) ; jumeau : 1900901.
INSERT INTO v2.FILE_EDITION (FLEDT_ID_FLEDT, FLEDT_LIBELLE, FLEDT_NUM_ORDRE, FLEDT_NOM_RIG, FLEDT_ACTIVE) VALUES
    (1900901, 'RCS du banc 9', 1, 'BANC9 RCS', 1), (1900902, 'Courriers du banc 9', 2, NULL, 1), (1900903, 'Amitel du banc 9', 0, 'AMIBANC9', 1);

-- Les groupes : 1900911 ouvert ; 1900912 clos (date de cloture ET TRTED_CLOTURE) et vide -- masque (OPE_LISTVIEWGRP.cs:152-155) ;
-- jumeaux acceptes : 1900913, cloture posee sans date (pas clos pour RIG v2, IsCloture) ; 1900911, ouvert et vide.
-- 1900914 : dans la file Amitel ; 1900915, 1900916 : la seconde file, l'ordre « TRTED_FIN_TRANSMISSION desc, TRTED_ID_TRTED ».
INSERT INTO v2.TRAITEMENT_EDITION (TRTED_ID_TRTED, TRTED_ID_FLEDT, TRTED_DATE_OUVERTURE, TRTED_CLOTURE, TRTED_DATE_CLOTURE, TRTED_FIN_TRANSMISSION, TRTED_IMPRIMANTE1) VALUES
    (1900911, 1900901, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, 'Imprimante 1er étage'),
    (1900912, 1900901, DATEADD(hour, 7, DATEADD(day, -3, @aujourdhui)), 1, DATEADD(hour, 18, DATEADD(day, -3, @aujourdhui)), 1, 'Imprimante 1er étage'),
    (1900913, 1900901, DATEADD(hour, 7, DATEADD(day, -2, @aujourdhui)), 1, NULL, 0, 'Imprimante 1er étage'),
    (1900914, 1900903, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, 'Imprimante Amitel'),
    (1900915, 1900902, DATEADD(hour, 7, DATEADD(day, -1, @aujourdhui)), 0, NULL, 0, NULL),
    (1900916, 1900902, DATEADD(hour, 7, DATEADD(day, -4, @aujourdhui)), 0, NULL, 1, NULL);
