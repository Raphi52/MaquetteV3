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

-- L'agent qui ECRIT le reglage (PROC_PARAM_IP.gestes, blocs « agent: banc.admin.l14 ») : l'ecriture d'un reglage exige le droit de
-- modifier le greffe (ServiceDroits.PeutModifierLeGreffe : administrateur = Amitel ET marque administrateur, OPE_EDITER_GREFFE.cs:30-38).
-- L'agent du banc (banc.ecrans) ne l'a pas -- son refus est le bloc 1 -- et le lui donner changerait l'agent de tous les autres jeux :
-- un second compte, administrateur, admis comme lui (meme service, role BANC). Sans IdV2 : aucun lecteur ne le montre.
INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
SELECT 'banc.admin.l14', 'ADMINL14', 'Banc', 'BANCL14', 'M.', CodeGreffe, IdService, 0, 1, 1 FROM soc.Compte WHERE CompteWindows = 'banc.ecrans';
INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = 'banc.admin.l14';
