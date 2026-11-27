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

-- Pour les CERTIFICATS DE PARUTION et les CONTROLES NON CONFORMES (lot 11, reprise : PROC_GESTBOD.recherches et .gestes) : des
-- annonces de 2001 (PROC_SURVBOD ne lit que les parutions depuis 2008), dont deux sur l'evenement 2110251 (BANC11B, du 01/04/2001,
-- sans instance, PC ni prevention). Colonnes ajoutees si un jeu precedent ne les a pas posees, aux types de RIG_DEV.
IF COL_LENGTH('v2.BODACC', 'BDCCR_NUM_PARUTION') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_NUM_PARUTION varchar(10) NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_CONTROLE_QUALITE') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_CONTROLE_QUALITE nvarchar(max) NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_DATE_EP_SUPPR') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_DATE_EP_SUPPR datetime NULL;
EXEC sp_executesql N'
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_SURVENU, EVNPR_DELAI_EXPIRE) VALUES
    (2110251, ''BANC11B'', ''20010401'', 1, 0);
UPDATE v2.BODACC SET BDCCR_NUMERO_ANNONCE = ''A2110205'' WHERE BDCCR_ID_BDCRC = 2110205;
INSERT INTO v2.BODACC (BDCCR_ID_BDCCR, BDCCR_ID_BDCRC, BDCCR_TYPE_BODACC, BDCCR_ETAT_BODACC, BDCCR_DATE_ENVOI, BDCCR_DATE_PUBLICITE,
                       BDCCR_NUM_PARUTION, BDCCR_NUMERO_ANNONCE, BDCCR_ID_EVNPR, BDCCR_CONTROLE_QUALITE, BDCCR_DATE_EP_SUPPR) VALUES
    -- parue le 10/04 (parution P2110) et deja verifiee (6)
    (2110206, 2110206, ''P1'', ''6'', ''20010402'', ''20010410'', ''P2110'', ''A2110206'', NULL, NULL, NULL),
    -- rectifiee, a verifier (10), sur l''evenement 2110251, avec un controle qualite : un certificat ET un controle non conforme
    (2110207, 2110207, ''P2'', ''10'', ''20010406'', ''20010410'', ''P2110'', ''A2110207'', 2110251, N''Capital manquant'', NULL),
    -- type N (hors P%) avec un controle qualite : un controle non conforme seulement (le controle ne filtre pas le type)
    (2110208, 2110208, ''N1'', ''5'', ''20010406'', NULL, NULL, ''A2110208'', 2110251, N''Siège absent'', NULL),
    -- a verifier, son evenement supprime le 01/04 (BDCCR_DATE_EP_SUPPR) : la date d''EP la trouve
    (2110209, 2110209, ''P1'', ''5'', NULL, NULL, NULL, ''A2110209'', NULL, NULL, ''20010401'');';
