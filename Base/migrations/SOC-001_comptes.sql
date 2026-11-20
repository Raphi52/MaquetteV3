-- SOC-001 : les comptes des agents, reconnus par leur session Windows (carnet\socle.json, SOC-001, tache T01).
-- Reprend de la V2 dbo.SERVICE, dbo.UTILISATEUR, dbo.HABILIT_ROLE_USER et dbo.HABILIT_UTILISATEUR_ROLE_USER
-- (D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Tables), juste ce qu'il faut pour les quatre
-- controles de l'ouverture (RigConsoleAccueil.cs:113-133) : compte connu, au moins un role, actif, un service.
-- SANS la colonne de mot de passe (UTLST_PASSWORD) : la V3 n'en a aucun (CA08).
-- Les lots suivants COMPLETENT ces tables par leurs propres migrations (« -- apres: SOC-001 ») :
-- distribution des services et creation des comptes (SOC-004), fonctions et origine des droits (SOC-002).

-- Les services du greffe (V2 : SERVICE). Un compte sans service n'ouvre pas la V3 (CA06).
CREATE TABLE soc.Service (
    Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Service PRIMARY KEY,
    IdV2        int          NULL,      -- SRVC_ID_SRVC, pour la recopie V2 <-> V3
    CodeGreffe  varchar(4)   NOT NULL,  -- SRVC_CODE_GREFFE
    Abrege      varchar(16)  NOT NULL,  -- SRVC_ABREGE
    Nom         varchar(70)  NULL       -- SRVC_NOM
);
GO

-- Les comptes (V2 : UTILISATEUR). Le compte Windows est unique et se compare SANS tenir compte des
-- majuscules, comme la V2 (Utilisateur.cs:428 et 530, ToUpper) : la collation CI de la colonne le
-- garantit pour la recherche ET pour l'unicite, quelle que soit la collation de la base (CA02).
CREATE TABLE soc.Compte (
    Id                 int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Compte PRIMARY KEY,
    IdV2               int          NULL,      -- UTLST_ID_UTLST, pour la recopie V2 <-> V3
    CompteWindows      varchar(35)  COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL,   -- UTLST_LOGIN
    Nom                varchar(35)  NOT NULL,  -- UTLST_NOM
    Prenom             varchar(35)  NOT NULL,  -- UTLST_PRENOM
    Abrege             varchar(16)  NOT NULL,  -- UTLST_ABREGE
    Civilite           varchar(7)   NOT NULL,  -- UTLST_CIVILITE
    CodeGreffe         varchar(4)   NULL,      -- UTLST_CODE_GREFFE (9999 : cree par Amitel, SOC-004)
    IdService          int          NULL CONSTRAINT FK_Compte_Service REFERENCES soc.Service (Id),  -- UTLST_ID_SRVC
    Desactive          bit          NOT NULL CONSTRAINT DF_Compte_Desactive DEFAULT (0),          -- UTLST_DESACTIVE
    EstAmitel          bit          NOT NULL CONSTRAINT DF_Compte_EstAmitel DEFAULT (0),          -- UTLST_ISAMITEL
    EstAdministrateur  bit          NOT NULL CONSTRAINT DF_Compte_EstAdministrateur DEFAULT (0),  -- UTLST_ISADMIN
    EstAssistance      bit          NOT NULL CONSTRAINT DF_Compte_EstAssistance DEFAULT (0),      -- UTLST_IS_HOTLINE
    CONSTRAINT UQ_Compte_CompteWindows UNIQUE (CompteWindows)
);
GO

-- Les roles (V2 : HABILIT_ROLE_USER), avec leurs reserves a Amitel ou aux administrateurs.
CREATE TABLE soc.Role (
    Nom                    varchar(40)  NOT NULL CONSTRAINT PK_Role PRIMARY KEY,  -- HROLE_NOM_ROLE
    Libelle                varchar(80)  NOT NULL,                                  -- HROLE_LIBELLE
    ReserveAmitel          bit          NOT NULL CONSTRAINT DF_Role_ReserveAmitel DEFAULT (0),          -- HROLE_ONLYAMITEL
    ReserveAdministrateur  bit          NOT NULL CONSTRAINT DF_Role_ReserveAdministrateur DEFAULT (0)   -- HROLE_ONLYADMIN
);
GO

-- Les roles de chaque compte (V2 : HABILIT_UTILISATEUR_ROLE_USER). Aucun role = aucune habilitation :
-- l'ouverture est refusee (CA05).
CREATE TABLE soc.CompteRole (
    IdCompte  int          NOT NULL CONSTRAINT FK_CompteRole_Compte REFERENCES soc.Compte (Id),
    Role      varchar(40)  NOT NULL CONSTRAINT FK_CompteRole_Role REFERENCES soc.Role (Nom),
    CONSTRAINT PK_CompteRole PRIMARY KEY (IdCompte, Role)
);
