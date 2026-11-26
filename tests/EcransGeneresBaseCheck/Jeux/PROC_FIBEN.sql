-- Jeu de LecteurReleveFiben (« FIBEN », PROC_FIBEN), lot 6 du plan-676a4b19. Identifiants 16002xx. Ce qui est compte ou ecarte,
-- et pourquoi : PROC_FIBEN.attendu. Aucun autre jeu ne touche ces trois tables.
-- Types : ceux de RIG_DEV (SSDT, RigDatabase\dbo\Tables\EDI_BDF_RCS.sql, EDI_BDF.sql, BODACC.sql), colonnes lues.
IF OBJECT_ID(N'v2.EDI_BDF_RCS') IS NULL
    CREATE TABLE v2.EDI_BDF_RCS (DBDRC_ID_DBDRC int NOT NULL PRIMARY KEY, DBDRC_ID_TRANSMISSION int NULL, DBDRC_AVIS varchar(max) NULL,
        DBDRC_DATE_ENVOI datetime NULL);
IF OBJECT_ID(N'v2.EDI_BDF') IS NULL
    CREATE TABLE v2.EDI_BDF (EDIBDF_ID_EDIBDF int NOT NULL PRIMARY KEY, EDIBDF_NUMFICHIER bigint NULL, EDIBDF_DATE_INSERTION_DANS_FICHIER datetime NULL,
        EDIBDF_CODE_ACTION varchar(1) NOT NULL);
IF OBJECT_ID(N'v2.BODACC') IS NULL
    CREATE TABLE v2.BODACC (BDCCR_ID_BDCCR int NOT NULL PRIMARY KEY, BDCCR_TYPE_BODACC varchar(5) NULL, BDCCR_DATE_PUBLICITE datetime NULL,
        BDCCR_COUT_BODACC float NULL, BDCCR_TYPE_ANNONCE varchar(1) NULL);

-- FIBEN, mars 2025 : 2 avis RCS comptes (le « REGUL » et le sans avis ecartes), 2 PC crees, 1 annule.
INSERT INTO v2.EDI_BDF_RCS (DBDRC_ID_DBDRC, DBDRC_ID_TRANSMISSION, DBDRC_AVIS, DBDRC_DATE_ENVOI) VALUES
    (1600201, 41, 'AVIS RCS 1', '20250303 10:00'),
    (1600202, 42, 'AVIS RCS 2', '20250331 23:00'),
    -- ECARTE du compte : regularisation (« not like '%  REGUL  %' ») -- jumeau compte 1600201
    (1600203, 43, 'AVIS  REGUL  3', '20250310'),
    -- ECARTE du compte : sans avis (NOT LIKE sur NULL) -- jumeau compte 1600201
    (1600204, 44, NULL, '20250311'),
    -- avril 2025 : un mois a lui seul
    (1600205, 45, 'AVIS RCS 5', '20250401'),
    -- ECARTE : jamais envoye (DBDRC_DATE_ENVOI vide) -- jumeau 1600205
    (1600206, 0, 'AVIS RCS 6', NULL);
INSERT INTO v2.EDI_BDF (EDIBDF_ID_EDIBDF, EDIBDF_NUMFICHIER, EDIBDF_DATE_INSERTION_DANS_FICHIER, EDIBDF_CODE_ACTION) VALUES
    (1600211, 7, '20250305', 'C'), (1600212, 7, '20250320', 'C'), (1600213, 8, '20250321', 'A'),
    -- ni creee ni annulee : comptee dans aucune colonne, le mois existe deja
    (1600214, 8, '20250322', 'M');
-- BODACC, 1er trimestre 2025 : A 2, B 1, C 0, P 1 ; 2e trimestre : B 1.
INSERT INTO v2.BODACC (BDCCR_ID_BDCCR, BDCCR_TYPE_BODACC, BDCCR_DATE_PUBLICITE, BDCCR_COUT_BODACC, BDCCR_TYPE_ANNONCE) VALUES
    (1600221, 'A', '20250105', 50, 'N'), (1600222, 'a1', '20250331 22:00', 0, 'C'), (1600223, 'B', '20250210', 20, 'N'),
    (1600224, 'P', '20250215', 10, 'N'), (1600225, 'B', '20250402', 20, 'N'),
    -- ECARTEES : type, cout ou type d'annonce vides (« is not null » de ReleveBODACC.cs:265) -- jumelle comptee 1600221
    (1600226, NULL, '20250106', 50, 'N'), (1600227, 'A', '20250107', NULL, 'N'), (1600228, 'A', '20250108', 50, NULL);
