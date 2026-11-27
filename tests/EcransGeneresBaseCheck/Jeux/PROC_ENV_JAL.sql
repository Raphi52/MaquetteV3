-- Jeu de LecteurEnvoiJal (« Envoi JAL / BODACC », PROC_ENV_JAL), lot 11 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19).
-- Publicites 2110311-2110334, evenements 2110301-2110302. Ce qui est retenu ou ecarte, et pourquoi : PROC_ENV_JAL.attendu.
-- Les publicites sont posees sur des affaires d'autres jeux, comme PROC_SDEMATJUD.sql : la PC 1400103 « CHM GAMMA » (exterieure)
-- et la prevention 1400201 « XLIEN PRV ALPHA » (PROC_CHM.sql), qu'aucun jeu n'ouvre le jour du banc. Aucune procedure n'est donc
-- ajoutee (les ecrans des procedures collectives et des preventions ne bougent pas). Les evenements ont un code absent de
-- CODE_EVE_PRO (« BANC11JAL ») : « Suivi dematerialisation » (jointure sur CODE_EVE_PRO) ne les voit pas, et ils n'ont pas
-- d'instance (aucun ecran des instances ne les compte).
-- PUBLICITE et BORDEREAU_JAL sont creees par PROC_SDEMATJUD.sql, joue apres ce jeu : elles le sont ici A L'IDENTIQUE de son
-- CREATE, puis les colonnes lues ici leur sont ajoutees, aux types de RIG_DEV (sys.columns de la copie de dev, lu le 27/11/2026).
IF OBJECT_ID(N'v2.PUBLICITE') IS NULL
    CREATE TABLE v2.PUBLICITE (PBLCT_ID_PBLCT int NOT NULL PRIMARY KEY, PBLCT_ID_EVNPR int NULL, PBLCT_DATE_REALISATION datetime NULL,
        PBLCT_DATE_ENVOI_ELECTRONIQUE datetime NULL, PBLCT_NUM_PARUTION varchar(20) NULL, PBLCT_NUM_BORDEREAU varchar(10) NULL,
        PBLCT_DESTINATAIRE varchar(35) NULL);
IF COL_LENGTH('v2.PUBLICITE', 'PBLCT_DATE_ENVOI') IS NULL ALTER TABLE v2.PUBLICITE ADD PBLCT_DATE_ENVOI datetime NULL;
IF COL_LENGTH('v2.PUBLICITE', 'PBLCT_ID_PREC_JAL') IS NULL ALTER TABLE v2.PUBLICITE ADD PBLCT_ID_PREC_JAL int NULL;
IF OBJECT_ID(N'v2.BORDEREAU_JAL') IS NULL
    CREATE TABLE v2.BORDEREAU_JAL (BRDRJ_ID_BRDRJ int NOT NULL PRIMARY KEY, BRDRJ_NUM_BORDEREAU varchar(10) NULL, BRDRJ_DATE_CREA_XML datetime NULL);
IF COL_LENGTH('v2.BORDEREAU_JAL', 'BRDRJ_ETAT_XML') IS NULL ALTER TABLE v2.BORDEREAU_JAL ADD BRDRJ_ETAT_XML varchar(2) NULL;
IF COL_LENGTH('v2.PROCEDURE_COLLECTIVE', 'PRCLL_EXTERIEUR') IS NULL ALTER TABLE v2.PROCEDURE_COLLECTIVE ADD PRCLL_EXTERIEUR bit NULL;
-- Les colonnes d'EVENEMENT_PROCEDURE qui rattachent l'evenement a sa PC ou a sa prevention, comme PROC_GESTBOD.sql (joue apres).
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PRCLL') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PRCLL int NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_PREVE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_PREVE int NULL;

-- Les reglages que la V2 lit a l'ouverture (OPE_RECH_ANNONCES.cs:275-293, :512) : le debut de periode pre-rempli, ecrit en texte
-- jj/mm/aaaa comme dans RIG_DEV (« 18/03/2026 »), ici 10 jours avant le banc ; l'ordre « a envoyer », la valeur de RIG_DEV.
DECLARE @debut varchar(10) = CONVERT(varchar(10), DATEADD(DAY, -10, @aujourdhui), 103);
IF EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'JAL_DATE_DEBUT_ENVOI')
    UPDATE soc.Reglage SET Valeur = @debut WHERE Code = 'JAL_DATE_DEBUT_ENVOI';
ELSE
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('JAL_DATE_DEBUT_ENVOI', 'Banc EcransGeneresBase lot 11', @debut);
IF EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'JAL_ORDER_BY')
    UPDATE soc.Reglage SET Valeur = 'EXTERIEUR,EVNPR_CODE_EVE_PRO,EVNPR_DATE ASC,NUM' WHERE Code = 'JAL_ORDER_BY';
ELSE
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('JAL_ORDER_BY', 'Banc EcransGeneresBase lot 11', 'EXTERIEUR,EVNPR_CODE_EVE_PRO,EVNPR_DATE ASC,NUM');

EXEC sp_executesql N'
-- 2110301 un evenement de la PC 1400103 (exterieure), 2110302 un evenement de la prevention 1400201.
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_CODE_EVE_PRO, EVNPR_DATE, EVNPR_ID_PRCLL, EVNPR_ID_PREVE) VALUES
    (2110301, ''BANC11JAL'', DATEADD(DAY, -20, @aujourdhui), 1400103, NULL),
    (2110302, ''BANC11JAL'', DATEADD(DAY, -15, @aujourdhui), NULL, 1400201);
-- Deux bordereaux : B11J01 genere, pas encore transmis (annulable) ; B11J02 transmis par EDI (etat XML 3, plus annulable).
INSERT INTO v2.BORDEREAU_JAL (BRDRJ_ID_BRDRJ, BRDRJ_NUM_BORDEREAU, BRDRJ_DATE_CREA_XML, BRDRJ_ETAT_XML) VALUES
    (2110341, ''B11J01'', DATEADD(DAY, -4, @aujourdhui), ''2''), (2110342, ''B11J02'', DATEADD(DAY, -4, @aujourdhui), ''3'');
INSERT INTO v2.PUBLICITE (PBLCT_ID_PBLCT, PBLCT_ID_EVNPR, PBLCT_DATE_ENVOI, PBLCT_DATE_ENVOI_ELECTRONIQUE, PBLCT_NUM_BORDEREAU,
    PBLCT_DESTINATAIRE, PBLCT_ID_PREC_JAL) VALUES
    -- a envoyer (pas de date d envoi electronique), dans la periode par defaut [J-10 ; aujourd hui]
    (2110311, 2110301, DATEADD(DAY, -2, @aujourdhui), NULL, NULL, ''BANC11J'', NULL),
    (2110312, 2110302, DATEADD(DAY, -3, @aujourdhui), NULL, NULL, ''BANC11K'', NULL),
    -- bornes de la periode : J-30 avant le debut (ecartee) et J-10 a 0 h, le debut (retenue) ; demain (ecartee) et aujourd hui
    -- a 18 h, la fin a 24 h (retenue)
    (2110313, 2110301, DATEADD(DAY, -30, @aujourdhui), NULL, NULL, ''BANC11J'', NULL),
    (2110315, 2110301, DATEADD(DAY, -10, @aujourdhui), NULL, NULL, ''BANC11J'', NULL),
    (2110314, 2110301, DATEADD(DAY, 1, @aujourdhui), NULL, NULL, ''BANC11J'', NULL),
    (2110316, 2110301, DATEADD(HOUR, 18, @aujourdhui), NULL, NULL, ''BANC11J'', NULL),
    -- deja envoyees : sur B11J01 (annulable), sur B11J02 (transmis), sans bordereau
    (2110321, 2110301, DATEADD(DAY, -5, @aujourdhui), DATEADD(DAY, -4, @aujourdhui), ''B11J01'', ''BANC11J'', NULL),
    (2110322, 2110302, DATEADD(DAY, -5, @aujourdhui), DATEADD(DAY, -4, @aujourdhui), ''B11J02'', ''BANC11J'', NULL),
    (2110323, 2110301, DATEADD(DAY, -6, @aujourdhui), DATEADD(DAY, -5, @aujourdhui), NULL, ''BANC11J'', NULL),
    -- evenement supprime (PBLCT_ID_EVNPR NULL) : 2110331 dans la periode, retrouvee par 2110332 qui la suit (PBLCT_ID_PREC_JAL) ;
    -- 2110333 hors periode (J-30), retrouvee par 2110334. 2110332 et 2110334, a J+40, sont hors periode elles-memes.
    (2110331, NULL, DATEADD(DAY, -7, @aujourdhui), DATEADD(DAY, -6, @aujourdhui), NULL, ''BANC11J'', NULL),
    (2110332, 2110301, DATEADD(DAY, 40, @aujourdhui), NULL, NULL, ''BANC11J'', 2110331),
    (2110333, NULL, DATEADD(DAY, -30, @aujourdhui), DATEADD(DAY, -29, @aujourdhui), NULL, ''BANC11J'', NULL),
    (2110334, 2110301, DATEADD(DAY, 40, @aujourdhui), NULL, NULL, ''BANC11J'', 2110333);', N'@aujourdhui datetime', @aujourdhui;
