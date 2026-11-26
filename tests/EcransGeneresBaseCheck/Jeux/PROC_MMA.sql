-- Jeu de LecteurMentionsEnMarge (« Mentions en marge », PROC_MMA), lot 3 du plan « RIG v3 100 % fonctionnel » (plan-676a4b19).
-- Identifiants 1 317 xxx. Tables aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 26/11/2026), creees si
-- absentes, completees sinon ; lignes par sp_executesql (voir PROC_MJUD.sql). Pas de jeu .gestes : MMA n'a pas d'ecrivain
-- (Valider et Refuser sont factures dans RIG v2, LecteurMentionsEnMarge.cs). L'inscription est la fiche reprise rec.Inscription
-- (migrations, Serveur/Recopie/FichesV3.cs:196) : la base V3 n'a pas de v2.INSCRIPTION. fix-ok: mesure le 26/11/2026 sur la
-- copie de dev (serveur 5231, journal) -- « Invalid object name 'v2.INSCRIPTION' » ; ce jeu creait la table que la V3 n'a pas.
IF OBJECT_ID(N'v2.MENTION_MARGINALE') IS NULL
    CREATE TABLE v2.MENTION_MARGINALE (MNMRG_ID_MNMRG int NOT NULL PRIMARY KEY, MNMRG_ID_INSCR int NULL, MNMRG_DATE datetime NULL,
        MNMRG_TEXTE_MENTION varchar(max) NULL);
IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_INSCR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_INSCR int NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
IF OBJECT_ID(N'v2.ETAT_DEMANDE') IS NULL
    CREATE TABLE v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATDEMAND_LIBELLE_ECRAN varchar(35) NOT NULL);

-- Les etats de demande lus, aux libelles de RIG_DEV (ETAT_DEMANDE lue le 26/11/2026), poses s'ils manquent.
EXEC sp_executesql N'
INSERT INTO v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR, R_ETATDEMAND_LIBELLE_ECRAN)
SELECT v.Valeur, v.Libelle FROM (VALUES (''A'', ''Arrivée''), (''Q'', ''Qualifiée''), (''V'', ''Validée''), (''N'', ''Réclamation''),
                                       (''O'', ''Refus''), (''W'', ''En attente''), (''T'', ''Terminée'')) AS v (Valeur, Libelle)
WHERE NOT EXISTS (SELECT 1 FROM v2.ETAT_DEMANDE AS e WHERE e.R_ETATDEMAND_VALEUR = v.Valeur);

-- Les inscriptions : 1317001 sans bateau, 1317002 maritime (IdBateau, l''INEDT_ID_BTEAU de la V2).
SET IDENTITY_INSERT rec.Inscription ON;
INSERT INTO rec.Inscription (Id, IdBateau, NumeroInscription, TypeInscription, ParTransfert, DateInscription, Renouvelable, EtatInfo, DateDiffusion) VALUES
    (1317001, NULL, ''2026-13701'', ''V'', 0, ''20260101'', 0, ''C'', ''20260101''),
    (1317002, 1317901, ''2026-13702'', ''N'', 0, ''20260101'', 0, ''C'', ''20260101'');
SET IDENTITY_INSERT rec.Inscription OFF;

-- Les demandes. MMA validee (V), refusee (O), en reclamation (N), arrivee (A), en cours de saisie (Q, en cours). Ecartees :
-- 1317106 (processus MJUD, meme inscription) -- jumeau retenu 1317101 ; 1317107 (MMA sans inscription) -- jumeau retenu 1317101.
-- 1317104 n''a pas de nom de demande : la ligne prend celui que PROC_MMA lui donne (PROC_MMA.cs:56).
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_EN_COURS, DMND_ID_INSCR, DMND_NOM, DMND_DATE_CREATION) VALUES
    (1317101, ''26017000001'', ''V'', ''MMA'', 0, 1317001, ''MMA sur l''''inscription 2026-13701 - Validée'', ''20260310''),
    (1317102, ''26017000002'', ''O'', ''MMA'', 0, 1317002, ''MMA sur l''''inscription 2026-13702'', ''20260309''),
    (1317103, ''26017000003'', ''N'', ''MMA'', 0, 1317001, ''MMA sur l''''inscription 2026-13701'', ''20260308''),
    (1317104, ''26017000004'', ''A'', ''MMA'', 0, 1317002, NULL, ''20260307''),
    (1317105, ''26017000005'', ''Q'', ''MMA'', 1, 1317001, ''MMA sur l''''inscription 2026-13701'', ''20260306''),
    (1317106, ''26017000006'', ''V'', ''MJUD'', 0, 1317001, ''Hors MMA'', ''20260305''),
    (1317107, ''26017000007'', ''V'', ''MMA'', 0, NULL, ''MMA sans inscription'', ''20260304'');

-- Les mentions de 1317001 : la plus recente est celle que la fiche lit.
INSERT INTO v2.MENTION_MARGINALE (MNMRG_ID_MNMRG, MNMRG_ID_INSCR, MNMRG_DATE, MNMRG_TEXTE_MENTION) VALUES
    (1317201, 1317001, ''20260310'', ''Mention du banc lot 3''), (1317202, 1317001, ''20250101'', ''Ancienne mention'');';
