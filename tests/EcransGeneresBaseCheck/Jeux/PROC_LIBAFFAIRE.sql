-- Jeu de LecteurAffairesVerrouillees et EcrivainAffairesVerrouillees (« Libérer une affaire », PROC_LIBAFFAIRE), lot 4 du plan
-- « RIG v3 100 % fonctionnel » (plan-676a4b19). Identifiants 14xx xxx. AFFAIRE aux types de RIG_DEV (lu le 26/11/2026).
-- L'utilisateur qui tient l'affaire est un compte de la V3 (UTILISATEUR -> soc.Compte, REC-017), retrouve par soc.Compte.IdV2.
--   1400851 LIB ALPHA : tenue par le compte IdV2 1400951, deux demandes -> « Avec une demande » ;
--   1400852 LIB BETA : tenue par le compte IdV2 1400952, sans demande -> « Sans demande » ;
--   1400853 LIB GAMMA : libre (AFFRE_ID_UTLST_EN_COURS NULL) -> ecartee (jumeau : 1400851) ; elle porte aussi les factures et la
--           provision de l'instance 1400353 de PROC_XXAFF.sql ;
--   1400854 LIB DELTA : tenue par un utilisateur que la V3 ne connait pas (IdV2 1400999) -> ecartee, comme l'INNER JOIN
--           UTILISATEUR de RIG v2 (OPE_LISTAFFAIRE.cs:43) ; jumeau : 1400852.
IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
    CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_ID_UTLST_CREATION int NULL, AFFRE_ID_UTLST_EN_COURS int NULL,
        AFFRE_DATE_CREATION datetime NOT NULL, AFFRE_LIBELLE varchar(70) NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, Desactive) VALUES
    (1400951, 'banc.lib.alpha', 'ALPHA', 'Lib', 'LIBALPHA', 'MR', 0),
    (1400952, 'banc.lib.beta', 'BETA', 'Lise', 'LIBBETA', 'MME', 0);
INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_ID_UTLST_EN_COURS, AFFRE_DATE_CREATION, AFFRE_LIBELLE) VALUES
    (1400851, 1400951, '20260301', 'LIB ALPHA'), (1400852, 1400952, '20260302', 'LIB BETA'),
    (1400853, NULL, '20260303', 'LIB GAMMA'), (1400854, 1400999, '20260304', 'LIB DELTA');
EXEC sp_executesql N'
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_ID_AFFRE) VALUES
    (1400861, ''26014000861'', ''V'', ''MJUD'', 1400851), (1400862, ''26014000862'', ''V'', ''MJUD'', 1400851);';
