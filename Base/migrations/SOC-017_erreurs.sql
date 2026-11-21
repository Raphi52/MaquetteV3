-- SOC-017 : retrouver une erreur technique sans aller sur les serveurs (carnet\socle.json, SOC-017, tache T01).
--
-- La V2 ecrit ses journaux en FICHIERS sur le serveur ou tourne l'agent (C:\rig\data\log\<greffe>\...,
-- logs-application.md:25-29), purges en quelques jours (:60-63) ; outre-mer, il faut passer par le partage du
-- serveur du territoire (:48-58). Seules les erreurs graves (eLog9Crash) vont en base commune, dans
-- COMMUN_RIG.dbo.LOG_RIG_ERREUR (:190-200 ; 2 482 lignes le 2026-11-20), sans identifiant que l'agent puisse donner.
-- La V3 ecrit TOUTE erreur de l'ecran, de la partie serveur et des traitements ici, dans la base COMMUNE (une seule
-- pour les 40 greffes, outre-mer compris, CA06), avec l'identifiant de suivi montre a l'agent (CA01, CA02).
-- Ecrit par Serveur\Socle\Supervision\JournalErreurs.cs, seul : aucune autre ecriture.
--
-- CA05 : ni mot de passe ni contenu de piece. Aucune colonne ne recoit le corps d'une requete ; le message et la pile
-- sont nettoyes (secrets masques) et bornes par le serveur ; la seule donnee personnelle est le compte Windows.
CREATE TABLE soc.ErreurTechnique (
    Id          bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ErreurTechnique PRIMARY KEY,
    -- L'identifiant de suivi du PARCOURS (« 7KQ2-M9XP ») : toutes les lignes d'une meme requete, ou d'un meme geste
    -- de l'ecran, le partagent (CA02). Montre a l'agent, dicte au telephone : 8 signes sans I, L, O ni U.
    Suivi       char(9)        NOT NULL,
    Instant     datetime2(3)   NOT NULL,   -- UTC
    Niveau      varchar(13)    NOT NULL CONSTRAINT CK_ErreurTechnique_Niveau CHECK (Niveau IN ('grave', 'erreur', 'avertissement')),
    Origine     varchar(10)    NOT NULL CONSTRAINT CK_ErreurTechnique_Origine CHECK (Origine IN ('ecran', 'serveur', 'traitement')),
    CodeGreffe  varchar(20)    NULL,
    Compte      varchar(35)    NULL,       -- le compte Windows, sans domaine : la seule donnee personnelle (CA05)
    Serveur     varchar(120)   NOT NULL,   -- la machine : le serveur d'Amitel, ou le serveur RDS de l'ecran
    Programme   varchar(200)   NOT NULL,   -- « GET /numerotation/{greffe}/numeros », « EcranCompteurs.Rendre », un traitement
    Type        varchar(200)   NULL,       -- le type de l'exception
    Message     nvarchar(2000) NOT NULL,
    Pile        nvarchar(max)  NULL
);
CREATE INDEX IX_ErreurTechnique_Suivi ON soc.ErreurTechnique (Suivi);
CREATE INDEX IX_ErreurTechnique_Instant ON soc.ErreurTechnique (Instant DESC) INCLUDE (CodeGreffe, Compte, Niveau);
GO

-- Les durees de conservation ECRITES (SOC-017 CA04, SOC-011 CA05), lues par la purge du serveur
-- (Serveur\Socle\Supervision\Surveillant.cs). Une ligne par journal et par niveau ; « * » vaut pour tout le journal.
-- Valeurs a valider par le referent Socle et le delegue a la protection des donnees (docs\reconstruction\
-- socle-supervision.md) : les erreurs graves 2 ans (la V2 garde LOG_RIG_ERREUR sans purge), les erreurs 6 mois,
-- les traces detaillees 30 jours (la V2 : quelques jours de fichiers ; EMAIL_ENVOI_TRACE 90 jours).
CREATE TABLE soc.ConservationTechnique (
    Journal  varchar(20)  NOT NULL,
    Niveau   varchar(20)  NOT NULL,
    Jours    int          NOT NULL CONSTRAINT CK_ConservationTechnique_Jours CHECK (Jours > 0),
    CONSTRAINT PK_ConservationTechnique PRIMARY KEY (Journal, Niveau)
);
INSERT INTO soc.ConservationTechnique (Journal, Niveau, Jours) VALUES
    ('erreurs', 'grave',         730),
    ('erreurs', 'erreur',        180),
    ('erreurs', 'avertissement',  30);
