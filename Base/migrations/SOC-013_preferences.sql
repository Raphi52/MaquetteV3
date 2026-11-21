-- SOC-013 : les preferences de chaque agent et ses documents a afficher, dans la base V3 du greffe
-- (carnet\socle.json, SOC-013, tache T01 ; lot 4 du plan « Reconstruction de RIG — phase 2 »).
-- apres: SOC-001
--
-- Pourquoi en base et non dans un fichier du poste : la maquette rangeait les reglages de l'accueil dans
-- %APPDATA%\RigV3\accueil.json (AccueilEtat.cs) ; sur une ferme RDS sans profil itinerant, l'agent les perd
-- des que la ferme l'envoie sur un autre serveur (notes de SOC-013). La V2 les garde en base :
-- dbo.PARAMETRE_UTILISATEUR (nom et valeur par agent, 40 470 lignes sur RIG_DEV), et dans dbo.UTILISATEUR
-- l'imprimante par defaut, le dernier serveur et la derniere session (UTILISATEUR.sql:17-21).
-- Lecture et ecriture : Serveur\Socle\Preferences (ServicePreferences.cs, DocumentsAgents.cs, ReprisePreferences.cs).

-- Les preferences d'un agent (V2 : PARAMETRE_UTILISATEUR, et UTLST_IMPRIMANTE_DEFAUT pour l'imprimante, CA05).
-- Une ligne par preference : son nom (« Theme », « Imprimante », « OngletsVue »...) et sa valeur en JSON,
-- telle que l'ecran l'ecrit. Le serveur ne l'interprete pas : la V3 peut ajouter une preference sans
-- migration. ModifieSur : le serveur RDS qui l'a ecrite en dernier (diagnostic d'un « mes reglages ont change »).
CREATE TABLE soc.Preference (
    IdCompte    int            NOT NULL CONSTRAINT FK_Preference_Compte REFERENCES soc.Compte (Id) ON DELETE CASCADE,
    Nom         varchar(60)    NOT NULL,
    Valeur      nvarchar(max)  NOT NULL,
    ModifieLe   datetime2(0)   NOT NULL CONSTRAINT DF_Preference_ModifieLe DEFAULT (SYSDATETIME()),
    ModifieSur  varchar(64)    NULL,
    CONSTRAINT PK_Preference PRIMARY KEY (IdCompte, Nom)
);
GO

-- La session ou l'agent travaille (V2 : UTLST_LAST_SERVER et UTLST_LAST_SESSIONID, ecrits a l'ouverture,
-- ThreadAccueil.cs:89-94). Une seule par agent : la derniere ouverte. Un document lui est remis LA, et
-- nulle part ailleurs (CA03, LigneEdition.cs:810-820 : la V2 envoie l'affichage au serveur et a la session notes).
CREATE TABLE soc.SessionAgent (
    IdCompte   int           NOT NULL CONSTRAINT PK_SessionAgent PRIMARY KEY
                             CONSTRAINT FK_SessionAgent_Compte REFERENCES soc.Compte (Id) ON DELETE CASCADE,
    Serveur    varchar(64)   NOT NULL,   -- le nom du serveur RDS (V2 : AmiEnvironment.MachineName)
    Session    varchar(64)   NOT NULL,   -- la session Windows sur ce serveur, et l'instance de la V3 qui s'y est ouverte
    OuverteLe  datetime2(0)  NOT NULL CONSTRAINT DF_SessionAgent_OuverteLe DEFAULT (SYSDATETIME())
);
GO

-- Les documents que la partie serveur prepare pour un agent (edition a afficher ou a relire, CA03). Un document
-- non remis attend l'agent (CA04) : il part a la prochaine session qui s'ouvre, jamais a une autre personne.
-- Remis* dit ou et quand il a ete remis : une fois remis, il ne repart plus.
CREATE TABLE soc.DocumentAgent (
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DocumentAgent PRIMARY KEY,
    IdCompte      int             NOT NULL CONSTRAINT FK_DocumentAgent_Compte REFERENCES soc.Compte (Id) ON DELETE CASCADE,
    Nature        varchar(10)     NOT NULL CONSTRAINT CK_DocumentAgent_Nature CHECK (Nature IN ('afficher', 'relire')),
    Titre         nvarchar(200)   NOT NULL,
    Fichier       nvarchar(400)   NOT NULL,   -- ou le serveur l'a pose (partage des editions)
    DeposeLe      datetime2(0)    NOT NULL CONSTRAINT DF_DocumentAgent_DeposeLe DEFAULT (SYSDATETIME()),
    DeposePar     varchar(80)     NOT NULL,   -- le traitement serveur qui l'a prepare
    RemisLe       datetime2(0)    NULL,
    RemisServeur  varchar(64)     NULL,
    RemisSession  varchar(64)     NULL
);
GO
CREATE INDEX IX_DocumentAgent_Attente ON soc.DocumentAgent (IdCompte) WHERE RemisLe IS NULL;
