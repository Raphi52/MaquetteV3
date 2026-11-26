-- « Mesures » (LecteurSurveillanceMesures) : des evenements de procedure de la mesure 1500902 de PROC_MESIN.sql (joue avant
-- celui-ci, ordre des noms). Ses mesures sont toutes listees par PROC_MESIN : ce jeu n'en ajoute pas, il rattache ses evenements
-- a une mesure existante, active (ni rapport, ni sans suite, ni caducite). Les evenements de PROC_MESIN n'ont ni date maximale, ni
-- minimale, ni fixe : aucune echeance, ils ne sont pas dans cette file. Types de RIG_DEV (lu le 26/11/2026). Les insertions passent
-- par sp_executesql : elles lisent des colonnes que ce meme jeu ajoute.
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_ID_MSINS') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_ID_MSINS int NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MAXI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MAXI datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_MINI') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_MINI datetime NULL;
IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DATE_FIXE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DATE_FIXE datetime NULL;
IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE varchar(10) NULL;
IF COL_LENGTH('v2.MESURE', 'MSINS_DATE_DEPOT_RAPPORT') IS NULL ALTER TABLE v2.MESURE ADD MSINS_DATE_DEPOT_RAPPORT datetime NULL;
IF COL_LENGTH('v2.MESURE', 'MSINS_DATE_SANS_SUITE') IS NULL ALTER TABLE v2.MESURE ADD MSINS_DATE_SANS_SUITE datetime NULL;
IF COL_LENGTH('v2.MESURE', 'MSINS_DATE_CADUCITE') IS NULL ALTER TABLE v2.MESURE ADD MSINS_DATE_CADUCITE datetime NULL;

-- Deux codes : BANC9EXP a un code « expire » (BANC9X) -- il se traite ; BANC9OBS n'en a pas -- grise, en rouge
-- (DataGridMesures.cs:240-245).
EXEC sp_executesql N'
INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE) VALUES
    (''BANC9EXP'', N''Échéance du banc 9'', ''BANC9X''), (''BANC9OBS'', N''Observation du banc 9'', NULL),
    (''BANC9X'', N''Expiration du banc 9'', NULL);';

-- L'echeance = ISNULL(MAXI, ISNULL(MINI, FIXE)) au plus aujourd'hui (OPE_RECHERCHE_MES.cs:51) :
--   1900701 MAXI hier (dans la file) ; 1900702 MINI avant-hier, sans MAXI (dans la file, sans code d'expiration) ;
--   1900703 FIXE aujourd'hui (dans la file : « <= » aujourd'hui) ; jumeau refuse 1900704, MAXI demain ;
--   1900705 survenu (EVNPR_SURVENU = 1) et 1900706 delai expire (EVNPR_DELAI_EXPIRE = 1) : hors file ; jumeau accepte 1900701 ;
--   1900707 : MAXI demain mais MINI hier -- MAXI l'emporte (priorite MAXI, MINI, FIXE) : hors file ; jumeau accepte 1900702.
EXEC sp_executesql N'
INSERT INTO v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR, EVNPR_ID_MSINS, EVNPR_NUM_ORDRE, EVNPR_CODE_EVE_PRO, EVNPR_SURVENU, EVNPR_DELAI_EXPIRE,
                                    EVNPR_DATE_MAXI, EVNPR_DATE_MINI, EVNPR_DATE_FIXE) VALUES
    (1900701, 1500902, 11, ''BANC9EXP'', 0, 0, DATEADD(day, -1, @aujourdhui), NULL, NULL),
    (1900702, 1500902, 12, ''BANC9OBS'', 0, 0, NULL, DATEADD(day, -2, @aujourdhui), NULL),
    (1900703, 1500902, 13, ''BANC9EXP'', 0, 0, NULL, NULL, @aujourdhui),
    (1900704, 1500902, 14, ''BANC9EXP'', 0, 0, DATEADD(day, 1, @aujourdhui), NULL, NULL),
    (1900705, 1500902, 15, ''BANC9EXP'', 1, 0, DATEADD(day, -1, @aujourdhui), NULL, NULL),
    (1900706, 1500902, 16, ''BANC9EXP'', 0, 1, DATEADD(day, -1, @aujourdhui), NULL, NULL),
    (1900707, 1500902, 17, ''BANC9OBS'', 0, 0, DATEADD(day, 1, @aujourdhui), DATEADD(day, -1, @aujourdhui), NULL);',
    N'@aujourdhui datetime', @aujourdhui;
