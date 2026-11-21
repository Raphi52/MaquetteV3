-- SOC-022 : les agents des greffes beta signalent un defaut depuis la V3, en un geste, avec l'identifiant de suivi
-- (carnet\socle.json, SOC-022, CA06, tache T03 ; lot 17 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
-- apres: SOC-017
--
-- La V2 n'a pas de signalement : un agent appelle l'assistance ou ecrit un courriel, et le defaut n'a ni identifiant ni
-- ecran. La V3 garde ici, dans la base COMMUNE (une seule pour les 40 greffes, comme soc.ErreurTechnique), chaque defaut
-- signale avec ce que l'ecran a pris sans saisie (ecran ouvert, greffe, compte de la session, version, heure du poste,
-- derniere erreur suivie) et la phrase de l'agent. L'identifiant de suivi est celui du parcours de la requete, donne par
-- le meme mecanisme que les erreurs (en-tete X-Rig-Suivi, Serveur\Socle\Supervision\JournalErreurs.cs) : l'agent le lit a
-- l'ecran, Amitel le retrouve dans l'ecran Supervision et « RigV3.Passage beta defauts » le classe (CA03).
-- Une table a part du journal technique : un defaut de la beta se garde jusqu'a la fin de la beta et au-dela, il ne suit
-- pas les durees de purge des traces techniques (soc.ConservationTechnique). Ecrit par Serveur\Socle\Supervision\
-- DefautsSignales.cs, seul. La seule donnee personnelle est le compte Windows ; la phrase est bornee a 1000 signes.
CREATE TABLE soc.DefautSignale (
    Id              bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DefautSignale PRIMARY KEY,
    Suivi           char(9)        NOT NULL CONSTRAINT UX_DefautSignale_Suivi UNIQUE,
    Instant         datetime2(3)   NOT NULL,   -- UTC, l'heure du serveur
    CodeGreffe      varchar(20)    NULL,
    Compte          varchar(35)    NULL,       -- le compte Windows de la session, sans domaine
    Poste           varchar(120)   NOT NULL,   -- le serveur RDS de l'ecran
    Ecran           nvarchar(200)  NOT NULL,   -- le nom de l'ecran ouvert, tel que l'agent le voit
    CodeEcran       varchar(60)    NULL,       -- son code dans la V3 (PROC_...) ; null pour l'accueil
    Version         varchar(30)    NULL,       -- la version de l'ecran
    HeurePoste      datetimeoffset(0) NULL,    -- l'heure du poste, avec son decalage (outre-mer)
    DerniereErreur  char(9)        NULL,       -- l'identifiant de suivi de la derniere erreur montree a l'agent
    Phrase          nvarchar(1000) NOT NULL CONSTRAINT CK_DefautSignale_Phrase CHECK (LEN(Phrase) > 0)
);
CREATE INDEX IX_DefautSignale_Instant ON soc.DefautSignale (Instant DESC) INCLUDE (CodeGreffe);
