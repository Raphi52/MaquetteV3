-- SOC-005 : garder la trace de chaque changement de droits (carnet\socle.json, SOC-005, tache T01).
-- apres: SOC-002
--
-- La V2 ne garde ni l'auteur ni la date d'un role attribue (HABILIT_UTILISATEUR_ROLE_USER.sql:1-6 :
-- le compte et le role, rien d'autre) ; son journal AUDIT ne couvre que FACTURE, ENCAISSEMENT,
-- VENTILATION et ARTICLE_UTILISE. Ecart « a valider » de la story, regle retenue : la V3 journalise
-- chaque ajout et chaque retrait de role (CA01), et ecrit l'evenement 130 quand le droit touche la
-- facturation ou l'encaissement (CA02).
-- Les deux journaux sont en AJOUT SEUL : une modification ou un effacement est refuse par la base
-- elle-meme, quel que soit le compte qui le tente (CA04).

-- Le journal des droits. Le compte VISE est garde par son nom (et son identifiant, sans cle) : un compte
-- jamais actif peut etre supprime (SOC-004 CA04) sans effacer la trace des roles qu'il a eus.
-- L'AUTEUR, lui, est une cle : un compte qui a donne un droit ne peut plus etre supprime.
CREATE TABLE soc.JournalDroits (
    Id            bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_JournalDroits PRIMARY KEY,
    Instant       datetime2(3)  NOT NULL CONSTRAINT DF_JournalDroits_Instant DEFAULT (SYSUTCDATETIME()),  -- UTC
    IdCompteVise  int           NOT NULL,
    CompteVise    varchar(35)   NOT NULL,
    Role          varchar(40)   NOT NULL,
    Sens          varchar(7)    NOT NULL CONSTRAINT CK_JournalDroits_Sens CHECK (Sens IN ('ajout', 'retrait')),
    IdAuteur      int           NOT NULL CONSTRAINT FK_JournalDroits_Auteur REFERENCES soc.Compte (Id),
    Auteur        varchar(80)   NOT NULL,  -- « Prenom Nom », tel qu'il etait au moment du geste
    Ecran         varchar(80)   NULL
);
CREATE INDEX IX_JournalDroits_Instant ON soc.JournalDroits (Instant DESC);

-- Le journal des evenements techniques (V2 : JET_P / JET_NP, codes de CODE_JET : 130 « Habilitation
-- d'utilisateur/Gestion des droits », 210 « Reprise de donnees d'autres logiciels suite a changement de
-- systemes », lus dans RIG_DEV le 2026-11-20). Sa SIGNATURE (chainage NF525, C19) arrive avec la
-- certification de la V3 (SOC-018) : cette table en est le contenu, en ajout seul des aujourd'hui.
CREATE TABLE soc.EvenementTechnique (
    Id        bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_EvenementTechnique PRIMARY KEY,
    Instant   datetime2(3)   NOT NULL CONSTRAINT DF_EvenementTechnique_Instant DEFAULT (SYSUTCDATETIME()),
    Code      smallint       NOT NULL,   -- CODE_JET
    Libelle   varchar(120)   NOT NULL,
    Detail    nvarchar(1000) NOT NULL,
    IdAuteur  int            NULL CONSTRAINT FK_EvenementTechnique_Auteur REFERENCES soc.Compte (Id),  -- null : la reprise
    Auteur    varchar(80)    NOT NULL
);
GO

CREATE TRIGGER soc.JournalDroits_AjoutSeul ON soc.JournalDroits AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51010, N'Le journal des droits est en ajout seul : une ligne ne se modifie ni ne s''efface (SOC-005 CA04).', 1;
END;
GO

CREATE TRIGGER soc.EvenementTechnique_AjoutSeul ON soc.EvenementTechnique AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51010, N'Le journal des evenements est en ajout seul : une ligne ne se modifie ni ne s''efface (SOC-005 CA04).', 1;
END;
