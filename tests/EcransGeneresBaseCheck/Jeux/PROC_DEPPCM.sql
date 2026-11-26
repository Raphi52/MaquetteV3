-- Jeu de LecteurDepotsPcEnMasse et EcrivainDepotsPcEnMasse (« Dépôts PC en masse », PROC_DEPPCM), lot 5 du plan « RIG v3
-- 100 % fonctionnel » (plan-676a4b19). Identifiants 1 510 4xx. Ce qui est retenu ou ecarte : PROC_DEPPCM.attendu.
-- Les tables et references sont celles de PROC_DEP.sql (joue juste avant, ordre des noms : code D15SAI, serie JD15S) ; les PC
-- sont celles de PROC_CHM.sql (CHM BETA 1400102 au forfait, CHM DELTA 1400104), pour la meme raison que PROC_DEP.sql.
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_MERE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_MERE int NULL;

-- Un lot valide (1510420, sans PC : pas une ligne) et ses dossiers : CHM BETA valide, CHM DELTA qualifie (Q, ne tient pas la
-- PC : VerrouDemandeV2) ; ecarte : un dossier supprime (X).
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_EN_COURS, DMND_NOM, DMND_NUM_INDICATIF,
    DMND_ID_DMND_MERE) VALUES
    (1510420, ''D1515100420'', ''V'', ''DEPPCM'', 0, NULL, NULL, NULL),
    (1510421, ''D1515100421'', ''V'', ''DEPPCM'', 0, ''CHM BETA'', ''1990P14102'', 1510420),
    (1510422, ''D1515100422'', ''Q'', ''DEPPCM'', 0, ''CHM DELTA'', ''1990P14104'', 1510420),
    (1510423, ''D1515100423'', ''X'', ''DEPPCM'', 0, ''CHM DELTA'', ''1990P14104'', 1510420);
INSERT INTO v2.DOSSIER_DEMANDE (DSSDM_ID_DSSDM, DSSDM_ID_DMND, DSSDM_CODE_SEQUENCEUR, DSSDM_VALEUR_SEQUENCEUR) VALUES
    (1510431, 1510421, ''2P'', ''1400102''), (1510432, 1510422, ''2P'', ''1400104''), (1510433, 1510423, ''2P'', ''1400104'');';
