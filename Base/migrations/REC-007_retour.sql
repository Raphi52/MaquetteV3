-- apres: REC-001, REC-002, REC-012
-- REC-007 : revenir a l'ancien RIG pendant la beta avec les affaires creees dans le nouveau (carnet\recopie.json), lot 11 du
-- plan de reconstruction, phase 2 (2026-11-21). Le code : Serveur\Recopie\RetourBeta.cs ; mode d'emploi :
-- docs\reconstruction\recopie.md, section « Revenir a l'ancien RIG pendant la beta (REC-007) ».
--
--   rec.ResponsableRetour  la personne designee chez Amitel, seule a pouvoir decider le retour d'un greffe (CA08)
--   rec.DecisionRetour     chaque decision de retour : auteur, date, motif, issue (refusee, en cours, faite, arretee) (CA08)
--   rec.RegleTypeRetour    pour un type de demande cree dans la V3 seule, le type de la V2 choisi avant le retour (CA06)
--   rec.EtatRetour         l'etat du greffe (T01) : sans ligne, il travaille dans la V3 ; « en retour » puis « revenu en
--                          V2 » -- la V3 refuse alors toute creation d'affaire pour lui (CA03, declencheur de garde pose a
--                          la mise en service : Serveur\Recopie\GardeAffaires.cs)
--
-- Une affaire creee dans la V3 pendant la beta (T01) se reconnait sans marque a part : c'est une ligne de v2.AFFAIRE sans
-- correspondance dans la V2 (rec.Correspondance) ; les anciennes affaires (transferees ou remontees de la V2) en ont une. Le
-- retour donne la sienne a chaque affaire et demande recopiee, avec l'origine « retour ».

CREATE TABLE rec.ResponsableRetour (
    BaseV2      sysname       NOT NULL CONSTRAINT PK_ResponsableRetour PRIMARY KEY,
    Compte      nvarchar(128) NOT NULL,      -- le compte de session de la personne designee (soc.Compte.CompteWindows)
    DesignePar  nvarchar(128) NOT NULL,
    DesigneLe   datetime2(0)  NOT NULL CONSTRAINT DF_ResponsableRetour_Le DEFAULT (SYSDATETIME())
);

CREATE TABLE rec.DecisionRetour (
    Id          int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DecisionRetour PRIMARY KEY,
    BaseV2      sysname        NOT NULL,
    Auteur      nvarchar(128)  NOT NULL,
    Le          datetime2(0)   NOT NULL CONSTRAINT DF_DecisionRetour_Le DEFAULT (SYSDATETIME()),
    Motif       nvarchar(1000) NOT NULL,
    Etat        varchar(10)    NOT NULL CONSTRAINT CK_DecisionRetour_Etat CHECK (Etat IN ('refusee', 'en cours', 'faite', 'arretee')),
    -- Pourquoi refusee ou arretee ; le compte rendu d'un retour fait.
    Detail      nvarchar(max)  NULL,
    FiniLe      datetime2(0)   NULL,
    Affaires    int            NULL,
    Demandes    int            NULL,
    Renvois     int            NULL,     -- lignes de facturation restees dans la V3, avec leur renvoi dans la V2 (CA04)
    DureeMs     bigint         NULL
);
CREATE INDEX IX_DecisionRetour_Base ON rec.DecisionRetour (BaseV2, Le);

CREATE TABLE rec.RegleTypeRetour (
    BaseV2      sysname       NOT NULL,
    IdTypeV3    int           NOT NULL,      -- rec.TypeDemande.Id, type cree dans la V3 seule (REC-019 CA03)
    IdTypeV2    int           NOT NULL,      -- CATEGORIE.CTGR_ID_CTGR de la base V2, sous lequel ses demandes y vont
    ChoisiPar   nvarchar(128) NOT NULL,
    ChoisiLe    datetime2(0)  NOT NULL CONSTRAINT DF_RegleTypeRetour_Le DEFAULT (SYSDATETIME()),
    CONSTRAINT PK_RegleTypeRetour PRIMARY KEY (BaseV2, IdTypeV3)
);

CREATE TABLE rec.EtatRetour (
    BaseV2      sysname      NOT NULL CONSTRAINT PK_EtatRetour PRIMARY KEY,
    Etat        varchar(14)  NOT NULL CONSTRAINT CK_EtatRetour_Etat CHECK (Etat IN ('en retour', 'revenu en V2')),
    Depuis      datetime2(0) NOT NULL CONSTRAINT DF_EtatRetour_Depuis DEFAULT (SYSDATETIME()),
    IdDecision  int          NOT NULL CONSTRAINT FK_EtatRetour_Decision REFERENCES rec.DecisionRetour (Id)
);

-- L'origine « retour » : la correspondance d'une ligne creee dans la V3 et recopiee dans la V2 au retour (liste complete).
ALTER TABLE rec.Correspondance DROP CONSTRAINT CK_Correspondance_Origine;
ALTER TABLE rec.Correspondance ADD CONSTRAINT CK_Correspondance_Origine CHECK (Origine IN ('recopie', 'remontee', 'fusion', 'retour'));
