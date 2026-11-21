-- SOC-011 : surveiller la partie serveur et ses traitements depuis Amitel (carnet\socle.json, SOC-011, tache T01).
-- apres: SOC-017, SOC-016
--
-- La V2 : chaque service ecrit son etat dans COMMUN_RIG.dbo.ETAT_SERVICE toutes les N secondes (supervision.md:17-19 ;
-- 11 550 694 lignes le 2026-11-20, jamais purgees), une ligne « ETSVC_LAST = 1 » par programme et serveur, SANS le
-- greffe (supervision.md, pieges connus) ; ses messages attendent dans AMIMESSAGE (18 201 617 lignes) ; on demarre et
-- on arrete un service par l'outil MaintenanceServices, sur le serveur. Aucune alerte : on lit les tables a la main.
-- La V3 : l'etat courant (une ligne par serveur, traitement et greffe) a cote de son historique purge (CA01, CA05), la
-- file persistante des traitements (CA04 : une relance ne perd rien), l'etat de chaque base de greffe et les alertes
-- (CA02). Tout dans la base COMMUNE ; les tables existent vides dans chaque base de greffe (meme schema, SOC-008 CA02).

-- La file des traitements de fond : un element attend ici tant qu'il n'est pas fait. Un traitement arrete ou en erreur
-- garde sa file ; a la relance, il la reprend la ou elle en etait (CA04).
CREATE TABLE soc.FileTraitement (
    Id          bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_FileTraitement PRIMARY KEY,
    Traitement  varchar(100)   NOT NULL,
    CodeGreffe  varchar(20)    NULL,
    Charge      nvarchar(max)  NOT NULL,   -- ce que le traitement doit faire (JSON du traitement)
    DeposeLe    datetime2(3)   NOT NULL,
    Etat        varchar(10)    NOT NULL CONSTRAINT CK_FileTraitement_Etat CHECK (Etat IN ('en attente', 'en cours', 'fait')),
    Essais      int            NOT NULL CONSTRAINT DF_FileTraitement_Essais DEFAULT (0),
    Erreur      nvarchar(1000) NULL,       -- la derniere erreur : l'element reste en attente
    TraiteLe    datetime2(3)   NULL
);
CREATE INDEX IX_FileTraitement_File ON soc.FileTraitement (Traitement, Etat, Id);
GO

-- L'etat COURANT publie par chaque serveur pour chacun de ses traitements (CA01) : une ligne par serveur et traitement,
-- mise a jour a chaque publication (ce que la V2 obtient par ETSVC_LAST = 1). Un traitement sert les 40 greffes ;
-- CodeGreffe nomme celui de l'element en erreur, que l'alerte doit nommer (CA02) -- la V2 ne le sait pas.
CREATE TABLE soc.EtatTraitement (
    Serveur       varchar(120)   NOT NULL,
    Traitement    varchar(100)   NOT NULL,
    CodeGreffe    varchar(20)    NULL,
    Etat          varchar(20)    NOT NULL CONSTRAINT CK_EtatTraitement_Etat
                      CHECK (Etat IN ('en marche', 'travaux en cours', 'en attente', 'en erreur', 'arrete')),
    EnCours       int            NOT NULL,
    EnAttente     int            NOT NULL,
    EnErreur      int            NOT NULL,
    Message       nvarchar(1000) NULL,
    PublieLe      datetime2(3)   NOT NULL,
    -- La derniere fois que la file a baisse, ou etait vide : une file qui ne baisse plus depuis le delai regle
    -- (Supervision:DelaiFileBloqueeSecondes) leve l'alerte « file bloquee » (CA02).
    FileBaisseLe  datetime2(3)   NULL,
    CONSTRAINT PK_EtatTraitement PRIMARY KEY (Serveur, Traitement)
);
-- L'historique des etats publies : purge selon soc.ConservationTechnique (« etats », CA05).
CREATE TABLE soc.EtatTraitementHistorique (
    Id          bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_EtatTraitementHistorique PRIMARY KEY,
    Serveur     varchar(120)   NOT NULL,
    Traitement  varchar(100)   NOT NULL,
    CodeGreffe  varchar(20)    NULL,
    Etat        varchar(20)    NOT NULL,
    EnCours     int            NOT NULL,
    EnAttente   int            NOT NULL,
    EnErreur    int            NOT NULL,
    Message     nvarchar(1000) NULL,
    PublieLe    datetime2(3)   NOT NULL
);
CREATE INDEX IX_EtatTraitementHistorique_PublieLe ON soc.EtatTraitementHistorique (PublieLe);
GO

-- L'etat de la base V3 de chaque greffe et des echecs de numerotation venus de l'ancien RIG (NUM-007 CA05, lus par
-- Base\v2\echecs.sql sur l'instance du greffe) : une ligne par greffe, remplacee a chaque passage de la supervision.
CREATE TABLE soc.EtatGreffe (
    CodeGreffe         varchar(20)    NOT NULL CONSTRAINT PK_EtatGreffe PRIMARY KEY,
    Sante              varchar(22)    NOT NULL CONSTRAINT CK_EtatGreffe_Sante
                           CHECK (Sante IN ('ok', 'pas creee', 'injoignable', 'migrations en retard')),
    Detail             nvarchar(1000) NULL,
    MigrationsJouees   int            NULL,
    DerniereMigration  varchar(200)   NULL,
    Echecs             int            NULL,       -- null : pas lus (instance injoignable, session absente)
    PremierEchec       datetime2(3)   NULL,
    DernierEchec       datetime2(3)   NULL,
    EchecsDetail       nvarchar(500)  NULL,
    VerifieLe          datetime2(3)   NOT NULL
);
GO

-- Les alertes envoyees a l'exploitation d'Amitel (CA02). Une seule alerte OUVERTE par cause (regle, greffe,
-- traitement) : la premiere panne alerte, les passages suivants ne renvoient rien tant qu'elle dure ; la cause
-- disparue, l'alerte est fermee, et une nouvelle panne en ouvre une nouvelle.
CREATE TABLE soc.AlerteExploitation (
    Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_AlerteExploitation PRIMARY KEY,
    Cle         varchar(240)   NOT NULL,
    Regle       varchar(30)    NOT NULL CONSTRAINT CK_AlerteExploitation_Regle
                    CHECK (Regle IN ('traitement-en-erreur', 'file-bloquee', 'base-injoignable', 'migrations-en-retard', 'echec-numerotation')),
    CodeGreffe  varchar(20)    NULL,
    Traitement  varchar(100)   NOT NULL,
    Erreur      nvarchar(1000) NOT NULL,
    OuverteLe   datetime2(3)   NOT NULL,
    EnvoyeeLe   datetime2(3)   NULL,       -- null : le canal ne l'a pas encore prise, elle est renvoyee au passage suivant
    Canal       varchar(40)    NULL,
    EnvoiRate   nvarchar(500)  NULL,
    FermeeLe    datetime2(3)   NULL
);
CREATE UNIQUE INDEX UX_AlerteExploitation_Ouverte ON soc.AlerteExploitation (Cle) WHERE FermeeLe IS NULL;
GO

-- La relance d'un traitement est un geste de l'exploitant : elle s'ecrit au journal des gestes de la base commune,
-- avec son auteur (CA04, SOC-016).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('supervision', 'Supervision (relance d''un traitement)', 0, 6);

-- Durees de conservation de l'historique des etats, des alertes fermees et des elements faits de la file (CA05) :
-- a valider (la V2 garde ETAT_SERVICE sans fin, 11 millions de lignes).
INSERT INTO soc.ConservationTechnique (Journal, Niveau, Jours) VALUES
    ('etats',   '*',  31),
    ('alertes', '*', 365),
    ('file',    '*',  31);
