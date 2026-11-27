-- Jeu de LecteurCodesChefsIp et EcrivainCodesChefsIp (« Codes chefs IP », PROC_PARAM_IP), lot 14 du plan-676a4b19 : la lecture ici,
-- les gestes dans PROC_PARAM_IP.gestes (ils reecrivent soc.Reglage par EcritureReglage.cs). Codes « ...L14... ». Le reglage IP_CODE_CHEF est dans
-- soc.Reglage (la reprise de PARAMETRAGE, SOC-003) ; CODE_CHEF est creee par PROC_DBE_INJ.sql (joue avant) avec deux colonnes : ce jeu
-- lui ajoute le libelle ecran (varchar(84) NOT NULL dans RIG_DEV, CODE_CHEF.cs de RigBaseGreffe : DEFAULT '' pour les lignes des autres).
IF OBJECT_ID(N'v2.CODE_CHEF') IS NULL
    CREATE TABLE v2.CODE_CHEF (R_CODECHEF_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODECHEF_TEXTE_DEMANDE varchar(max) NULL);
IF COL_LENGTH('v2.CODE_CHEF', 'R_CODECHEF_LIBELLE_ECRAN') IS NULL
    ALTER TABLE v2.CODE_CHEF ADD R_CODECHEF_LIBELLE_ECRAN varchar(84) NOT NULL CONSTRAINT DF_L14_CODECHEF_LIBELLE DEFAULT ('');

EXEC sp_executesql N'
-- IPL14B existe mais n''est pas range dans la liste (jumeau ecarte de IPL14A) ; IPL14Z est range mais n''existe pas : la V2 ne le
-- montre pas (ETP_PARAM_IP.cs:20).
INSERT INTO v2.CODE_CHEF (R_CODECHEF_VALEUR, R_CODECHEF_LIBELLE_ECRAN) VALUES
    (''IPL14A'', ''INJONCTION BANC A''), (''IPL14B'', ''INJONCTION BANC B''), (''IPL14C'', ''INJONCTION BANC C''), (''RFL14'', ''REFERE BANC'');
IF EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = ''IP_CODE_CHEF'')
    UPDATE soc.Reglage SET Valeur = ''RFL14;IPL14A;IPL14Z;IPL14C'' WHERE Code = ''IP_CODE_CHEF'';
ELSE
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES (''IP_CODE_CHEF'', ''Banc EcransGeneresBase lot 14'', ''RFL14;IPL14A;IPL14Z;IPL14C'');
';
