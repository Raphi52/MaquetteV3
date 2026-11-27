-- Jeu de LecteurDatesTravail / EcrivainDatesTravail (« Dates de travail », PROC_GESTDATE), lot 14 du plan-676a4b19. La table aux types
-- de RIG_DEV (DATE_TRAVAIL.cs de RigBaseGreffe) ; aucun autre jeu ne la cree. Codes WINS et DATW (ceux que la V2 controle,
-- OPE_DATE_TRAVAIL.cs:50) et « L14... ». Les dates sont FIXES (2099 = futur, 2026 avant novembre = passe) : les messages des gestes, que le
-- banc lit tels quels, les citent.
IF OBJECT_ID(N'v2.DATE_TRAVAIL') IS NULL
    CREATE TABLE v2.DATE_TRAVAIL (DTTRV_CODE_DATE_TRAVAIL varchar(10) NOT NULL PRIMARY KEY, DTTRV_LIBELLE varchar(70) NULL,
        DTTRV_DATE_TRAVAIL datetime NOT NULL, DTTRV_ANCIENNE_DATE_TRAVA datetime NULL, DTTRV_DATE_FRAICHEUR datetime NULL,
        DTTRV_AUTOMATIQUE bit NOT NULL, DTTRV_ACTIF bit NOT NULL, DTTRV_AUTOMATIQUE_FRAICHEUR bit NOT NULL);

EXEC sp_executesql N'
INSERT INTO v2.DATE_TRAVAIL (DTTRV_CODE_DATE_TRAVAIL, DTTRV_LIBELLE, DTTRV_DATE_TRAVAIL, DTTRV_ANCIENNE_DATE_TRAVA, DTTRV_DATE_FRAICHEUR,
                             DTTRV_AUTOMATIQUE, DTTRV_ACTIF, DTTRV_AUTOMATIQUE_FRAICHEUR) VALUES
    -- WINS : fraicheur future -> « Fraîcheur à vérifier » ; DATW : date de travail future -> « Date à corriger ».
    (''WINS'', ''Inscriptions'', ''20260210'', NULL, ''20990101'', 0, 1, 0),
    (''DATW'', ''RCS'', ''20991231'', NULL, NULL, 0, 1, 0),
    -- L14F : des dates futures hors WINS / DATW ne sont pas controlees (jumeau de DATW) ; L14P : passee ; L14I : inactive, ecartee.
    (''L14F'', ''DATE BANC FUTURE'', ''20990630'', ''20260101'', ''20990701'', 1, 1, 0),
    (''L14P'', ''DATE BANC PASSEE'', ''20260105'', NULL, NULL, 0, 1, 0),
    (''L14I'', ''DATE BANC INACTIVE'', ''20260105'', NULL, NULL, 0, 0, 0);
';
