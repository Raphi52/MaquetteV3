-- SOC-016 : savoir qui a modifie quoi, quand et depuis quel ecran (carnet\socle.json, SOC-016, tache T01).
-- apres: SOC-002
--
-- La V2 (AUDIT.sql:1-11) ne journalise que quatre tables -- FACTURE, ENCAISSEMENT, VENTILATION,
-- ARTICLE_UTILISE -- par des declencheurs, sous le nom du PROGRAMME et du compte de CONNEXION SQL
-- (FACTURE.sql:81-94). La V3 ecrit son journal depuis la partie serveur, au nom de l'AGENT (CA02),
-- avec l'ecran, l'objet et les valeurs avant et apres (CA01).
-- Ajout seul (CA04) : une modification est toujours refusee ; un effacement ne passe que par la purge
-- reglee par duree (soc.PurgerJournal), et seulement pour une ligne echue -- au moins 6 ans pour ce qui
-- touche la facturation (contrainte CK_FamilleJournal_Facturation).
-- Le journal de la V2 reste dans la V2 (CA05) : la V3 commence le sien, au passage, par une ligne
-- « passage » qui le dit (Serveur\Socle\Comptes\RepriseDroits.cs).

-- Les familles d'objets journalises et leur duree de conservation. Les durees autres que celles de la
-- facturation (6 ans, donnees de reglement) sont a fixer par le referent Socle et le delegue a la
-- protection des donnees (SOC-016-T05) : 6 ans par defaut, a valider.
CREATE TABLE soc.FamilleJournal (
    Famille          varchar(30)  NOT NULL CONSTRAINT PK_FamilleJournal PRIMARY KEY,
    Libelle          varchar(60)  NOT NULL,
    Facturation      bit          NOT NULL,
    ConservationAns  int          NOT NULL CONSTRAINT CK_FamilleJournal_Conservation CHECK (ConservationAns > 0),
    CONSTRAINT CK_FamilleJournal_Facturation CHECK (Facturation = 0 OR ConservationAns >= 6)
);
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('facture',         'Facture',                    1, 6),   -- V2 : AUDIT sur FACTURE
    ('encaissement',    'Encaissement',               1, 6),   -- V2 : AUDIT sur ENCAISSEMENT
    ('ventilation',     'Ventilation',                1, 6),   -- V2 : AUDIT sur VENTILATION
    ('article-utilise', 'Article utilise',            1, 6),   -- V2 : AUDIT sur ARTICLE_UTILISE
    ('client',          'Client',                     1, 6),
    ('dossier',         'Dossier',                    0, 6),
    ('affaire',         'Affaire',                    0, 6),
    ('demande',         'Demande',                    0, 6),
    ('reglage',         'Reglage du greffe',          0, 6),
    ('compte',          'Compte d''agent',            0, 6),
    ('journal',         'Journal',                    0, 6);

-- Le journal des gestes. Qui a agi, selon la source :
--   v3         : un agent de la V3 (IdAgent, jamais un compte technique) ;
--   recopie-v2 : la recopie V2 -> V3, au nom du compte V2 d'origine (CA06) ;
--   passage    : la ligne d'ouverture du journal (CA05).
CREATE TABLE soc.Journal (
    Id        bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Journal PRIMARY KEY,
    Instant   datetime2(3)   NOT NULL CONSTRAINT DF_Journal_Instant DEFAULT (SYSUTCDATETIME()),  -- UTC
    Source    varchar(10)    NOT NULL CONSTRAINT CK_Journal_Source CHECK (Source IN ('v3', 'recopie-v2', 'passage')),
    IdAgent   int            NULL CONSTRAINT FK_Journal_Agent REFERENCES soc.Compte (Id),
    Agent     varchar(80)    NULL,   -- « Prenom Nom » au moment du geste : le nom reste apres un depart
    CompteV2  varchar(35)    NULL,   -- le compte V2 d'origine d'une recopie
    Ecran     varchar(80)    NULL,
    Famille   varchar(30)    NOT NULL CONSTRAINT FK_Journal_Famille REFERENCES soc.FamilleJournal (Famille),
    Objet     varchar(120)   NOT NULL,   -- ce qui a ete touche, lisible (« le compte jdupont »)
    IdObjet   varchar(40)    NULL,
    Verbe     varchar(20)    NOT NULL CONSTRAINT CK_Journal_Verbe
                  CHECK (Verbe IN ('a cree', 'a modifie', 'a desactive', 'a supprime', 'a valide', 'a ouvert')),
    Avant     nvarchar(max)  NULL,   -- JSON des valeurs avant
    Apres     nvarchar(max)  NULL,   -- JSON des valeurs apres
    CONSTRAINT CK_Journal_Auteur CHECK (
           (Source = 'v3'         AND IdAgent IS NOT NULL AND Agent IS NOT NULL AND CompteV2 IS NULL)
        OR (Source = 'recopie-v2' AND IdAgent IS NULL AND CompteV2 IS NOT NULL)
        OR (Source = 'passage'    AND IdAgent IS NULL AND CompteV2 IS NULL))
);
CREATE INDEX IX_Journal_Instant ON soc.Journal (Instant DESC);
GO

CREATE TRIGGER soc.Journal_SansModification ON soc.Journal AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51010, N'Le journal des gestes est en ajout seul : une ligne ne se modifie pas (SOC-016 CA04).', 1;
END;
GO

-- Un effacement ne passe que DANS la purge (contexte de session pose par soc.PurgerJournal) ET pour une
-- ligne echue. Meme la purge ne peut donc pas retirer une ligne recente.
CREATE TRIGGER soc.Journal_PurgeSeulement ON soc.Journal AFTER DELETE AS
BEGIN
    IF EXISTS (SELECT 1
               FROM deleted d
               JOIN soc.FamilleJournal f ON f.Famille = d.Famille
               WHERE ISNULL(CAST(SESSION_CONTEXT(N'rigv3.purge-journal') AS int), 0) <> 1
                  OR d.Instant >= DATEADD(year, -f.ConservationAns, SYSUTCDATETIME()))
        THROW 51011, N'Le journal des gestes ne s''efface pas : seule la purge par duree retire une ligne echue (SOC-016 CA04).', 1;
END;
GO

-- La purge reglee par duree (SOC-016-T05) : retire les lignes plus vieilles que la conservation de leur
-- famille, rien d'autre. Rend le nombre de lignes retirees. Sa planification est un geste d'exploitation.
CREATE PROCEDURE soc.PurgerJournal
AS
BEGIN
    SET NOCOUNT ON;
    EXEC sys.sp_set_session_context @key = N'rigv3.purge-journal', @value = 1;
    BEGIN TRY
        DELETE j
        FROM soc.Journal j
        JOIN soc.FamilleJournal f ON f.Famille = j.Famille
        WHERE j.Instant < DATEADD(year, -f.ConservationAns, SYSUTCDATETIME());
        DECLARE @retirees int = @@ROWCOUNT;
        EXEC sys.sp_set_session_context @key = N'rigv3.purge-journal', @value = NULL;
        SELECT @retirees AS Retirees;
    END TRY
    BEGIN CATCH
        EXEC sys.sp_set_session_context @key = N'rigv3.purge-journal', @value = NULL;
        THROW;
    END CATCH
END;
GO

-- Consulter le journal des gestes de TOUT le greffe est une fonction de la V3 (la V2 n'a pas ce journal) :
-- le chef de greffe, et quiconque le greffe lui assimile par un role (CA03). Le role qui gere les droits
-- l'a d'office ; le greffe la donne a d'autres roles par la gestion des droits. Les autres agents ne
-- voient que leurs propres gestes.
INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('JOURNAL_GESTES', 'Consulter le journal des gestes du greffe', 'V3');
INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('GestionDesHabilitations', 'JOURNAL_GESTES');
