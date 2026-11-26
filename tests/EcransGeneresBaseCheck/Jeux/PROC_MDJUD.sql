-- Jeu de LecteurMotsDirecteurs et EcrivainMotsDirecteurs (« Mots directeurs », PROC_MDJUD), lot 3 du plan « RIG v3 100 %
-- fonctionnel » (plan-676a4b19). Identifiants 1 311 xxx (lot 3 : 1 300 000 a 1 399 999 ; 1 300 1xx-3xx sont au jeu RPJC).
-- Les tables V2 lues, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 26/11/2026), seulement les
-- colonnes lues ou ecrites ; creees si un jeu precedent ne les a pas posees, completees sinon (colonnes nullables : les
-- tables ont deja des lignes), et leurs lignes passent par sp_executesql (une colonne ajoutee dans ce meme lot
-- d'instructions n'existe pas encore quand SQL Server le compile).
-- Les instances de ce jeu sont des types JRPR / JRMC (la variante MDJUDPR) : hors de MJUD (PROC_MJUD.attendu inchange). Les
-- instances et PC des autres jeux (PROC_MJUD, PROC_VNOTIF, Program.cs) n'ont pas de mots directeurs : hors de la liste.
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_AFFAIRE varchar(10) NULL;
IF OBJECT_ID(N'v2.CODE_AFFAIRE') IS NULL
    CREATE TABLE v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEAFFAIR_LIBELLE_ECRAN varchar(100) NOT NULL,
        R_CODEAFFAIR_TYPE_NUM_ROLE varchar(5) NULL, R_CODEAFFAIR_TYPE_INSTANCE varchar(2) NOT NULL);
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NUM_PC') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NUM_PC varchar(10) NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_NOM_AFFAIRE') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_NOM_AFFAIRE varchar(max) NULL;
IF OBJECT_ID(N'v2.PREVENTION') IS NULL
    CREATE TABLE v2.PREVENTION (PREVE_ID_PREVE int NOT NULL PRIMARY KEY, PREVE_NUM_PRV varchar(10) NULL, PREVE_NOM_AFFAIRE varchar(250) NULL);
IF OBJECT_ID(N'v2.RIG_MOTS_DIRECTEUR_PCINST') IS NULL
    CREATE TABLE v2.RIG_MOTS_DIRECTEUR_PCINST (MDPCI_ID_MDPCI int NOT NULL PRIMARY KEY, MDPCI_ID_PRCLL int NULL, MDPCI_ID_INSTN int NULL,
        MDPCI_MOTS varchar(max) NULL, MDPCI_ID_PREVE int NULL);

-- Les references : deux types de la variante MDJUDPR (ETP_RECHERCHE_AFFAIRE.cs:30), et un type hors des deux variantes.
EXEC sp_executesql N'
INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_LIBELLE_ECRAN, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE) VALUES
    (''PR13'', ''Prévention du banc lot 3'', ''JRPR'', ''PR''), (''MC13'', ''Mandat ad hoc du banc lot 3'', ''JRMC'', ''MC''),
    (''XX13'', ''Hors MDJUD du banc lot 3'', ''JRXX'', ''XX'');

-- Les affaires. 1311001 (JRPR) et 1311002 (JRMC, chaine vide : « À compléter ») sont retenues ; 1311003 (JRPR) n''a pas de
-- mots directeurs : ecartee (la V3 ne cree pas l''enregistrement) -- jumeau retenu : 1311001 ; 1311004 (JRXX) a ses mots mais
-- un type hors des deux variantes : ecartee -- jumeau retenu : 1311001.
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_CODE_AFFAIRE) VALUES
    (1311001, ''2026P13101'', ''OMEGA MD INSTANCE'', ''PR13''), (1311002, ''2026M13102'', ''SIGMA MD INSTANCE'', ''MC13''),
    (1311003, ''2026P13103'', ''TAU SANS MD'', ''PR13''), (1311004, ''2026X13104'', ''UPSILON HORS MDJUD'', ''XX13'');
SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE ON;
INSERT INTO v2.PROCEDURE_COLLECTIVE (PRCLL_ID_PRCLL, PRCLL_NUM_PC, PRCLL_NOM_AFFAIRE) VALUES
    (1311101, ''2026P13111'', ''PC MD ALPHA''), (1311102, ''2026P13112'', ''PC MD BETA'');
SET IDENTITY_INSERT v2.PROCEDURE_COLLECTIVE OFF;
INSERT INTO v2.PREVENTION (PREVE_ID_PREVE, PREVE_NUM_PRV, PREVE_NOM_AFFAIRE) VALUES (1311201, ''2026V13121'', ''PRV MD GAMMA'');

-- Les chaines. 1311102 porte un mot deja enregistre au prefixe inconnu (« XYold ») : la V2 ne fait qu''avertir pour lui.
INSERT INTO v2.RIG_MOTS_DIRECTEUR_PCINST (MDPCI_ID_MDPCI, MDPCI_ID_INSTN, MDPCI_ID_PRCLL, MDPCI_ID_PREVE, MDPCI_MOTS) VALUES
    (1311501, 1311001, NULL, NULL, ''NRomega FNdupont''), (1311502, 1311002, NULL, NULL, ''''),
    (1311504, 1311004, NULL, NULL, ''NRupsilon''),
    (1311511, NULL, 1311101, NULL, ''SE513110001 NAalpha''), (1311512, NULL, 1311102, NULL, ''XYold''),
    (1311521, NULL, NULL, 1311201, ''NVgamma DEgamma'');';
