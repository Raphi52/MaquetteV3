-- Jeu de LecteurPublicationsBodacc (« Publications BODACC », PROC_GESTBOD), lot 11 du plan « RIG v3 100 % fonctionnel »
-- (plan-676a4b19). Annonces 2110201-2110205. Ce qui est retenu ou ecarte, et pourquoi : PROC_GESTBOD.attendu.
-- v2.BODACC est creee par PROC_FIBEN.sql (sa cle de banc BDCCR_ID_BDCCR, recue ici la meme valeur que BDCCR_ID_BDCRC, la cle de
-- RIG_DEV) ; les colonnes lues lui sont ajoutees si elles manquent, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV,
-- lu le 27/11/2026). Aucune annonce n'a d'evenement de procedure : elles n'entrent dans aucun ecran des evenements, des procedures
-- collectives ou des preventions ; sans controle qualite, elles n'entrent pas dans PROC_SURVBOD.
IF OBJECT_ID(N'v2.BODACC') IS NULL
    CREATE TABLE v2.BODACC (BDCCR_ID_BDCCR int NOT NULL PRIMARY KEY, BDCCR_TYPE_BODACC varchar(5) NULL, BDCCR_DATE_PUBLICITE datetime NULL);
IF COL_LENGTH('v2.BODACC', 'BDCCR_ID_BDCRC') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_ID_BDCRC int NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_ID_EVNPR') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_ID_EVNPR int NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_DATE_ENVOI') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_DATE_ENVOI datetime NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_ETAT_BODACC') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_ETAT_BODACC varchar(2) NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_NUMERO_ANNONCE') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_NUMERO_ANNONCE varchar(15) NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_MOTIF_REJET_BODACC_1') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_MOTIF_REJET_BODACC_1 varchar(255) NULL;
IF OBJECT_ID(N'v2.EVENEMENT_PROCEDURE') IS NULL
    CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int NOT NULL PRIMARY KEY, EVNPR_ID_INSTN int NULL, EVNPR_NUM_ORDRE smallint NULL,
        EVNPR_CODE_EVE_PRO varchar(10) NOT NULL, EVNPR_DATE datetime NULL, EVNPR_SURVENU bit NULL, EVNPR_DELAI_EXPIRE bit NULL);
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PRCLL') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PRCLL int NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PREVE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PREVE int NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(11) NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_AFFAIRE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_AFFAIRE varchar(250) NULL;
IF OBJECT_ID(N'v2.PREVENTION') IS NULL
    CREATE TABLE v2.PREVENTION (PREVE_ID_PREVE int NOT NULL PRIMARY KEY, PREVE_NUM_PRV varchar(10) NULL, PREVE_NOM_AFFAIRE varchar(250) NULL);

EXEC sp_executesql N'
INSERT INTO v2.BODACC (BDCCR_ID_BDCCR, BDCCR_ID_BDCRC, BDCCR_TYPE_BODACC, BDCCR_ETAT_BODACC, BDCCR_DATE_ENVOI, BDCCR_NUMERO_ANNONCE,
                       BDCCR_MOTIF_REJET_BODACC_1) VALUES
    -- rejetee (9), publicite de procedure (P) : a traiter
    (2110201, 2110201, ''P1'', ''9'', ''20010402'', ''A2110201'', ''Adresse incomplète''),
    -- rejetee, sans motif ni numero : a traiter -- elle se renvoie (PROC_GESTBOD.gestes)
    (2110202, 2110202, ''P2'', ''9'', ''20010403'', NULL, NULL),
    -- deja remise a renvoyer (3) : a renvoyer, figee
    (2110203, 2110203, ''P1'', ''3'', ''20010404'', ''A2110203'', ''Nom tronqué''),
    -- rejetee mais d''un type hors publicite de procedure (N) : ecartee -- jumelle 2110201
    (2110204, 2110204, ''N1'', ''9'', ''20010402'', ''A2110204'', ''Adresse incomplète''),
    -- publiee a verifier (5) : un certificat, dont la liste exige une date -- ecartee, jumelle 2110201
    (2110205, 2110205, ''P1'', ''5'', ''20010402'', ''A2110205'', NULL);';
