-- REC-001 : transferer chaque base de l'ancien RIG en une nuit, telle qu'elle est (carnet\recopie.json, REC-001, T01).
--
-- Le moteur est Serveur\Recopie\MoteurTransfert.cs ; l'outil de l'exploitant, Outils\Transfert ; le mode d'emploi et
-- l'architecture de la recopie pour les lots suivants, docs\reconstruction\recopie.md.
--
--   schema v2              les tables de la V2 recopiees TELLES QUELLES (memes colonnes, memes types, memes valeurs,
--                          meme cle primaire), en attendant que la story de leur domaine ecrive sa regle
--   rec.Transfert          le journal : un transfert d'une base V2 (instantane date, etat, motif d'arret, totaux, duree)
--   rec.TransfertTable     le rapport de fin de nuit, table par table (lues, ecrites, rejetees, liens casses, duree)
--   rec.TransfertEvenement ce que le transfert a imprime (schema, regles, reprises, arret), dans l'ordre
--   rec.Correspondance     identifiant V2 -> identifiant V3 de chaque ligne ecrite (T01), indexee dans les deux sens
--   rec.Rejet              chaque ligne lue et non ecrite : table, identifiant, motif, regle appliquee (CA03, CA04)
--   rec.LienCasse          chaque ligne ecrite dont un lien vise une ligne absente, valeur gardee telle quelle (CA04)
--
-- Volumes (RIG_DEV, lu le 2026-11-21 : 860 tables, 129,7 millions de lignes, 63,8 Go) : la correspondance porte une ligne
-- par ligne ecrite ; compression PAGE (toutes editions depuis SQL Server 2016 SP1) sur ses textes tres repetes.

IF SCHEMA_ID(N'v2') IS NULL EXEC (N'CREATE SCHEMA [v2]');

CREATE TABLE rec.Transfert (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Transfert PRIMARY KEY,
    BaseV2       sysname       NOT NULL,
    ServeurV2    sysname       NOT NULL,
    -- L'instantane de base (CREATE DATABASE ... AS SNAPSHOT OF) d'ou le transfert a tout lu (CA05) ; NULL si refuse avant.
    Instantane   sysname       NULL,
    InstantaneLe datetime2(3)  NULL,
    -- CHANGE_TRACKING_CURRENT_VERSION() lu DANS l'instantane : la recopie continue (REC-003) rejoue ce qui suit.
    -- NULL si le suivi des changements n'est pas active sur la base V2 (REC-003 T01).
    VersionSuivi bigint        NULL,
    Debut        datetime2(3)  NOT NULL CONSTRAINT DF_Transfert_Debut DEFAULT (SYSDATETIME()),
    Fin          datetime2(3)  NULL,
    DureeMs      bigint        NULL,
    -- en cours ; fini ; arrete (un ecart ou une erreur : la table en cours est annulee, la suite n'est pas faite) ;
    -- refuse (avant toute ecriture de donnees : table attendue absente, table sans regle...).
    Etat         varchar(10)   NOT NULL CONSTRAINT DF_Transfert_Etat DEFAULT ('en cours')
                 CONSTRAINT CK_Transfert_Etat CHECK (Etat IN ('en cours', 'fini', 'arrete', 'refuse')),
    Motif        nvarchar(max) NULL,
    Lues         bigint        NULL,
    Ecrites      bigint        NULL,
    Rejetees     bigint        NULL,
    LiensCasses  bigint        NULL,
    Compte       nvarchar(128) NOT NULL CONSTRAINT DF_Transfert_Compte DEFAULT (ORIGINAL_LOGIN()),
    Machine      nvarchar(128) NULL CONSTRAINT DF_Transfert_Machine DEFAULT (HOST_NAME())
);
CREATE INDEX IX_Transfert_Base ON rec.Transfert (BaseV2, Etat);

CREATE TABLE rec.TransfertTable (
    IdTransfert     int           NOT NULL CONSTRAINT FK_TransfertTable_Transfert REFERENCES rec.Transfert (Id),
    TableV2         sysname       NOT NULL,
    -- Rang d'ecriture, deduit des cles etrangeres (une table apres celles qu'elle vise).
    Ordre           int           NOT NULL,
    -- « telle quelle » ou « reprise » (une reprise de la phase 1, appelee et non refaite).
    -- fix-ok: en varchar(10), « telle quelle » (12) etait tronque en « telle quel » par le parametre (banc, 4e essai :
    -- ordre d'ecriture et duree vides) ; la compression en double de rec.Correspondance etait l'erreur du 3e essai.
    Regle           varchar(20)   NOT NULL,
    Story           varchar(20)   NOT NULL,
    TableV3         nvarchar(260) NULL,
    Lues            bigint        NULL,
    Ecrites         bigint        NULL,
    Rejetees        bigint        NULL,
    LiensCasses     bigint        NULL,
    Correspondances bigint        NULL,
    -- Place occupee dans la V3 (pages de la table, octets) : base de l'extrapolation de duree (CA02).
    Octets          bigint        NULL,
    Debut           datetime2(3)  NULL,
    Fin             datetime2(3)  NULL,
    DureeMs         bigint        NULL,
    Detail          nvarchar(max) NULL,
    CONSTRAINT PK_TransfertTable PRIMARY KEY (IdTransfert, TableV2)
);

CREATE TABLE rec.TransfertEvenement (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TransfertEvenement PRIMARY KEY,
    IdTransfert int           NOT NULL CONSTRAINT FK_TransfertEvenement_Transfert REFERENCES rec.Transfert (Id),
    Le          datetime2(3)  NOT NULL CONSTRAINT DF_TransfertEvenement_Le DEFAULT (SYSDATETIME()),
    Genre       varchar(12)   NOT NULL,
    Message     nvarchar(max) NOT NULL
);
CREATE INDEX IX_TransfertEvenement_Transfert ON rec.TransfertEvenement (IdTransfert, Id);

-- T01 : base V2, table V2, identifiant V2, identifiant V3, date. La table V3 en plus : un identifiant V3 n'a de sens
-- qu'avec sa table (une ligne d'UTILISATEUR devient un soc.Compte, une ligne d'AFFAIRE une ligne de v2.AFFAIRE).
-- Identifiant : la cle primaire en texte (colonnes jointes par « | » si elle en a plusieurs ; date en ISO 8601).
CREATE TABLE rec.Correspondance (
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    IdV2        nvarchar(200) NOT NULL,
    TableV3     nvarchar(260) NOT NULL,
    IdV3        nvarchar(200) NOT NULL,
    IdTransfert int           NOT NULL,
    Le          datetime2(0)  NOT NULL CONSTRAINT DF_Correspondance_Le DEFAULT (SYSDATETIME()),
    -- La cle primaire groupee EST la table : sa compression vaut pour les lignes (une seconde, sur la table, est refusee).
    CONSTRAINT PK_Correspondance PRIMARY KEY CLUSTERED (BaseV2, TableV2, IdV2) WITH (DATA_COMPRESSION = PAGE)
);
-- L'autre sens : de la ligne V3 a sa ligne V2 (la recopie V3 -> V2, REC-002). Non unique : une regle de fusion
-- (adresses partagees, REC-016) pourra faire d'une ligne V3 la correspondante de plusieurs lignes V2.
CREATE INDEX IX_Correspondance_V3 ON rec.Correspondance (TableV3, IdV3) INCLUDE (BaseV2, TableV2, IdV2)
    WITH (DATA_COMPRESSION = PAGE);

CREATE TABLE rec.Rejet (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Rejet PRIMARY KEY,
    IdTransfert int           NOT NULL CONSTRAINT FK_Rejet_Transfert REFERENCES rec.Transfert (Id),
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    IdV2        nvarchar(200) NOT NULL,
    Motif       nvarchar(1000) NOT NULL,
    -- La regle de la table qui a decide le rejet (ex. « lien casse : rejeter »).
    Regle       varchar(60)   NOT NULL,
    Le          datetime2(0)  NOT NULL CONSTRAINT DF_Rejet_Le DEFAULT (SYSDATETIME())
);
CREATE INDEX IX_Rejet_Ligne ON rec.Rejet (BaseV2, TableV2, IdV2);
CREATE INDEX IX_Rejet_Transfert ON rec.Rejet (IdTransfert, TableV2);

CREATE TABLE rec.LienCasse (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LienCasse PRIMARY KEY,
    IdTransfert int           NOT NULL CONSTRAINT FK_LienCasse_Transfert REFERENCES rec.Transfert (Id),
    BaseV2      sysname       NOT NULL,
    TableV2     sysname       NOT NULL,
    IdV2        nvarchar(200) NOT NULL,
    -- La cle etrangere de la V2 (son nom dans le catalogue de la base), ses colonnes, la valeur gardee telle quelle.
    Cle         sysname       NOT NULL,
    Colonnes    nvarchar(400) NOT NULL,
    Valeur      nvarchar(400) NOT NULL,
    TableVisee  sysname       NOT NULL,
    -- « absente de la V2 » : lien casse herite (C5) ; « rejetee par le transfert » : la ligne visee est dans rec.Rejet.
    Cause       varchar(30)   NOT NULL CONSTRAINT CK_LienCasse_Cause CHECK (Cause IN ('absente de la V2', 'rejetee par le transfert')),
    Le          datetime2(0)  NOT NULL CONSTRAINT DF_LienCasse_Le DEFAULT (SYSDATETIME())
);
CREATE INDEX IX_LienCasse_Ligne ON rec.LienCasse (BaseV2, TableV2, IdV2);
CREATE INDEX IX_LienCasse_Transfert ON rec.LienCasse (IdTransfert, TableV2);
