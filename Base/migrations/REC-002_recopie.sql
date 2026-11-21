-- apres: REC-001, SOC-002, SOC-016
-- REC-002, REC-003, REC-004 : la recopie CONTINUE entre la base V2 d'un greffe et sa base V3 (carnet\recopie.json), lot 9
-- du plan de reconstruction, phase 2 (2026-11-21).
--
--   rec.FileRecopie   la file V3 -> V2 (REC-002 T01) : une ligne par changement valide d'une fiche partagee, ecrite par un
--                     declencheur de la table V3 DANS la transaction du changement ; l'applicateur la lit dans l'ordre
--                     (Serveur\Recopie\Applicateur.cs). La V2 indisponible ne bloque pas la V3 : les lignes attendent (CA06).
--   rec.EtatLigne     l'etat commun des deux bases pour une fiche, apres son dernier passage (dans un sens ou dans l'autre) :
--                     la remontee y reconnait une ecriture de la recopie (REC-002 CA05, REC-003 CA05) et l'applicateur un
--                     changement de la V2 depuis le dernier passage, c'est-a-dire un conflit (REC-004 T02).
--   rec.Remontee      le point de reprise de la remontee V2 -> V3 d'une base (REC-003 T02) : la version du suivi des
--                     changements de SQL Server deja remontee, ecrite dans la transaction de chaque lot.
--   rec.PassageRecopie le journal des passages (ce qui a ete applique, remonte, ignore, en conflit, ou l'erreur).
--   rec.Ecart, rec.EcartChamp  les fiches modifiees des deux cotes (REC-004 T01) : champ, valeur V2 perdue, valeur V3
--                     gardee, dates, auteurs, traitement ; rec.EcartsParJour, le compte du controle quotidien (CA05).
--
-- Mode d'emploi et raisons : docs\reconstruction\recopie.md, section « La recopie continue ».
-- fix-ok: fichier NEUF du lot 9, complete avant tout essai : soc.Journal exige une famille de soc.FamilleJournal (cle
-- etrangere FK_Journal_Famille, SOC-016_gestes.sql:49) -- d'ou les deux familles ci-dessous et « apres: SOC-016 ».

-- La correspondance sait desormais qui l'a posee : NULL, le transfert initial (REC-001) ; « recopie » : une fiche creee
-- dans la V3 et recue par la V2 (REC-002 CA02) ; « remontee » : une fiche creee dans la V2 et recue par la V3 (REC-003 CA03).
ALTER TABLE rec.Correspondance ADD Origine varchar(10) NULL
    CONSTRAINT CK_Correspondance_Origine CHECK (Origine IN ('recopie', 'remontee'));

CREATE TABLE rec.FileRecopie (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FileRecopie PRIMARY KEY,
    -- La fiche : sa table V3 (« v2.CLIENT ») et sa cle primaire en texte (meme ecriture que rec.Correspondance.IdV3).
    TableV3     nvarchar(260) NOT NULL,
    IdV3        nvarchar(200) NOT NULL,
    -- C creation, M modification, S suppression.
    Genre       char(1)       NOT NULL CONSTRAINT CK_FileRecopie_Genre CHECK (Genre IN ('C', 'M', 'S')),
    -- Les colonnes changees (separees par des virgules) ; NULL pour une creation ou une suppression.
    Colonnes    nvarchar(max) NULL,
    -- La ligne V3 AVANT le changement (JSON) : l'etat commun des deux bases quand la fiche n'a encore jamais ete recopiee
    -- (REC-004 : c'est contre lui qu'un changement de la V2 se mesure). NULL pour une creation.
    Avant       nvarchar(max) NULL,
    Le          datetime2(3)  NOT NULL CONSTRAINT DF_FileRecopie_Le DEFAULT (SYSDATETIME()),
    -- L'agent de la V3 (SESSION_CONTEXT rigv3.agent pose par le serveur), sinon le compte SQL.
    Auteur      nvarchar(128) NOT NULL,
    -- en attente ; appliquee (la V2 a la valeur de la V3, ou n'avait rien a recevoir).
    Etat        varchar(12)   NOT NULL CONSTRAINT DF_FileRecopie_Etat DEFAULT ('en attente')
                CONSTRAINT CK_FileRecopie_Etat CHECK (Etat IN ('en attente', 'appliquee')),
    AppliqueeLe datetime2(3)  NULL,
    BaseV2      sysname       NULL,
    IdV2        nvarchar(200) NULL,
    -- Les passages qui ont echoue sur cette ligne (base V2 muette, refus de la V2) et le dernier motif : la ligne reste
    -- en attente, rien de ce qui la suit n'est applique avant elle.
    Essais      int           NOT NULL CONSTRAINT DF_FileRecopie_Essais DEFAULT (0),
    Erreur      nvarchar(2000) NULL,
    IdEcart     bigint        NULL
);
CREATE INDEX IX_FileRecopie_Attente ON rec.FileRecopie (Id) INCLUDE (TableV3, IdV3, Le) WHERE Etat = 'en attente';
CREATE INDEX IX_FileRecopie_Fiche ON rec.FileRecopie (TableV3, IdV3, Id);

CREATE TABLE rec.EtatLigne (
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    IdV2        nvarchar(200) NOT NULL,
    -- La ligne V2 (colonnes recopiees, en JSON de SQL Server) telle que les deux bases l'ont en commun apres le passage.
    Ligne       nvarchar(max) NOT NULL,
    Le          datetime2(3)  NOT NULL CONSTRAINT DF_EtatLigne_Le DEFAULT (SYSDATETIME()),
    Sens        varchar(10)   NOT NULL CONSTRAINT CK_EtatLigne_Sens CHECK (Sens IN ('V3 vers V2', 'V2 vers V3')),
    CONSTRAINT PK_EtatLigne PRIMARY KEY (BaseV2, TableV2, IdV2)
);

CREATE TABLE rec.Remontee (
    BaseV2         sysname      NOT NULL CONSTRAINT PK_Remontee PRIMARY KEY,
    -- CHANGE_TRACKING_CURRENT_VERSION() de la V2 jusqu'ou tout est remonte ; au depart, celle lue dans l'instantane du
    -- transfert (rec.Transfert.VersionSuivi) : la remontee rejoue exactement ce que le transfert n'a pas pris.
    Version        bigint       NOT NULL,
    IdTransfert    int          NOT NULL,
    MiseEnServiceLe datetime2(3) NOT NULL CONSTRAINT DF_Remontee_MiseEnService DEFAULT (SYSDATETIME()),
    DernierPassage datetime2(3) NULL
);

CREATE TABLE rec.PassageRecopie (
    Id           bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PassageRecopie PRIMARY KEY,
    BaseV2       sysname       NOT NULL,
    Debut        datetime2(3)  NOT NULL,
    Fin          datetime2(3)  NOT NULL,
    Appliquees   int           NOT NULL,
    Remontees    int           NOT NULL,
    -- Changements de la V2 reconnus comme des ecritures de la recopie (REC-003 CA05) : ignores.
    Echos        int           NOT NULL,
    Ecarts       int           NOT NULL,
    VersionV2    bigint        NULL,
    Erreur       nvarchar(2000) NULL
);
CREATE INDEX IX_PassageRecopie_Base ON rec.PassageRecopie (BaseV2, Id);

CREATE TABLE rec.Ecart (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Ecart PRIMARY KEY,
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    IdV2        nvarchar(200) NOT NULL,
    TableV3     nvarchar(260) NOT NULL,
    IdV3        nvarchar(200) NOT NULL,
    -- Ce que l'agent lit (« client 3001 — DUPONT ») et ou le clic l'emmene (« client:3001 », « dossier:2026B00012 »).
    Fiche       nvarchar(300) NOT NULL,
    Cible       nvarchar(200) NULL,
    DetecteLe   datetime2(3)  NOT NULL CONSTRAINT DF_Ecart_DetecteLe DEFAULT (SYSDATETIME()),
    -- Le cote V2 : la date de mise a jour de la fiche quand la table en a une (REC-004 T04), sinon le moment ou la
    -- recopie l'a vu ; l'auteur quand la V2 le note, sinon NULL (« non note par la V2 »).
    DateV2      datetime2(3)  NOT NULL,
    AuteurV2    nvarchar(128) NULL,
    -- Le cote V3 : le premier changement en attente de la fiche et son agent.
    DateV3      datetime2(3)  NOT NULL,
    AuteurV3    nvarchar(128) NOT NULL,
    TraiteLe    datetime2(3)  NULL,
    TraitePar   nvarchar(128) NULL
);
CREATE INDEX IX_Ecart_NonTraites ON rec.Ecart (DetecteLe) INCLUDE (TableV2) WHERE TraiteLe IS NULL;
CREATE INDEX IX_Ecart_Jour ON rec.Ecart (DetecteLe, BaseV2, TableV2);

CREATE TABLE rec.EcartChamp (
    IdEcart     bigint        NOT NULL CONSTRAINT FK_EcartChamp_Ecart REFERENCES rec.Ecart (Id),
    Champ       sysname       NOT NULL,
    -- Les valeurs en texte JSON de SQL Server (une chaine garde ses guillemets, NULL s'ecrit null).
    ValeurV2    nvarchar(max) NULL,
    ValeurV3    nvarchar(max) NULL,
    CONSTRAINT PK_EcartChamp PRIMARY KEY (IdEcart, Champ)
);
GO

-- REC-004 CA05 : le nombre d'ecarts de chaque jour, par base et par table, que lit le controle quotidien (REC-006).
CREATE VIEW rec.EcartsParJour
AS
SELECT CAST(DetecteLe AS date) AS Jour, BaseV2, TableV2, COUNT_BIG(*) AS Ecarts,
       SUM(CASE WHEN TraiteLe IS NULL THEN 1 ELSE 0 END) AS NonTraites
FROM rec.Ecart
GROUP BY CAST(DetecteLe AS date), BaseV2, TableV2;
GO

-- REC-004 T03 : suivre les fiches modifiees des deux cotes est une fonction de la V3 (la V2 n'a pas de recopie). Le
-- greffe la donne a l'agent qu'il designe par la gestion des droits ; le role qui gere les droits l'a d'office.
INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('RECOPIE_ECARTS', 'Suivre les fiches modifiees des deux cotes (recopie V2-V3)', 'V3');
INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('GestionDesHabilitations', 'RECOPIE_ECARTS');

-- Le journal des gestes (SOC-016) : une fiche partagee ecrite dans la V3 par la remontee, au nom de la recopie (CA06),
-- et un ecart marque traite par l'agent designe. Conservation : 6 ans, la duree par defaut a valider (SOC-016-T05).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('fiche-partagee', 'Fiche partagee V2-V3 (recopie)', 0, 6),
    ('ecart-recopie',  'Fiche modifiee des deux cotes',  0, 6);
