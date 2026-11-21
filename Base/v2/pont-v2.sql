-- Le pont côté V2 : ce que la bascule pose dans la base V2 d'un greffe, dans le schéma rigv3
-- (lot 7, NUM-002, 003, 006, 007, 022 ; 2026-11-21). Joué par bascule.sql, base V2 courante, dans sa
-- transaction ; variable sqlcmd $(BaseV3).
--
--   rigv3.Pont                 une ligne : le greffe, sa base V3, la bascule, la fin de la bêta, le retour
--   rigv3.CorpsOrigine         le texte exact des trois programmes d'origine, gardé pendant la bêta (NUM-022)
--   rigv3.CorpsOrigineArchive  ce même texte après la fin de la bêta : plus déployable (NUM-022 CA03)
--   rigv3.AppelChercher        chaque appel de CHERCHER_NUMERO : qui, d'où, quoi (NUM-003 CA03)
--   rigv3.Attribuer            synonymes vers num.Attribuer / num.Restituer de la base V3 : seul endroit
--   rigv3.Restituer              où son nom est écrit ; le corps des programmes est le même dans les 40 bases
--   rigv3.DemanderNumero       l'appel au compteur, commun à GENERER_NUMERO et CHERCHER_NUMERO
--   rigv3.RendreNumero         l'appel à la restitution, pour RESTITUER_NUMERO
--   dbo.TR_NUMERO_METIER_RIGV3 la garde de NUMERO_METIER : seule la recopie de la V3 y écrit (NUM-006 CA04)
--
-- Les tables restent après un retour arrière (l'histoire du pont) ; le reste est retiré par retour.sql.

IF SCHEMA_ID(N'rigv3') IS NULL EXEC (N'CREATE SCHEMA rigv3 AUTHORIZATION dbo');
GO

IF OBJECT_ID(N'rigv3.Pont', N'U') IS NULL
CREATE TABLE rigv3.Pont (
    Id          tinyint       NOT NULL CONSTRAINT PK_Pont PRIMARY KEY CONSTRAINT CK_Pont_UneLigne CHECK (Id = 1),
    CodeGreffe  varchar(20)   NOT NULL,          -- soc.Greffe.CodeGreffe (SOC-007) : il est dans les messages
    BaseV3      sysname       NOT NULL,          -- soc.Greffe.BaseV3, même instance (Q4)
    BasculeLe   datetime2(0)  NOT NULL,
    BasculePar  nvarchar(128) NOT NULL,
    -- Fin de la bêta : au-delà, le retour arrière refuse de tourner (NUM-022 CA03). NULL = greffe hors
    -- bêta : aucun corps d'origine n'est gardé, le retour n'existe pas pour lui.
    FinBeta     datetime2(0)  NULL,
    RetourLe    datetime2(0)  NULL,              -- NULL = le pont est en service
    RetourPar   nvarchar(128) NULL
);

IF OBJECT_ID(N'rigv3.CorpsOrigine', N'U') IS NULL
CREATE TABLE rigv3.CorpsOrigine (
    Objet            sysname       NOT NULL CONSTRAINT PK_CorpsOrigine PRIMARY KEY,
    Definition       nvarchar(max) NOT NULL,     -- sys.sql_modules.definition, tel quel
    -- Les réglages de sa création : les anciens programmes écrivent « "9999" » et « <>"" », qui ne
    -- se compilent qu'avec QUOTED_IDENTIFIER OFF (mesuré le 2026-11-21 : erreur 1038 sinon).
    QuotedIdentifier bit           NOT NULL,
    AnsiNulls        bit           NOT NULL,
    -- Les GRANT / DENY posés sur le programme : le retour le recrée (DROP puis CREATE) et les repose.
    Droits           nvarchar(max) NULL,
    GardeLe          datetime2(0)  NOT NULL
);

IF OBJECT_ID(N'rigv3.CorpsOrigineArchive', N'U') IS NULL
CREATE TABLE rigv3.CorpsOrigineArchive (
    Objet            sysname       NOT NULL,
    Definition       nvarchar(max) NOT NULL,
    QuotedIdentifier bit           NOT NULL,
    AnsiNulls        bit           NOT NULL,
    Droits           nvarchar(max) NULL,
    GardeLe          datetime2(0)  NOT NULL,
    ArchiveLe        datetime2(0)  NOT NULL,
    ArchivePar       nvarchar(128) NOT NULL
);

IF OBJECT_ID(N'rigv3.AppelChercher', N'U') IS NULL
CREATE TABLE rigv3.AppelChercher (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppelChercher PRIMARY KEY,
    Le          datetime2(3)  NOT NULL CONSTRAINT DF_AppelChercher_Le DEFAULT (SYSDATETIME()),
    Compte      nvarchar(128) NULL,
    Poste       nvarchar(128) NULL,
    Application nvarchar(128) NULL,
    Type        varchar(5)    NULL,
    Annee       smallint      NULL,
    Jour        smallint      NULL,
    Numero      varchar(40)   NULL,
    Code        int           NULL
);
GO

IF OBJECT_ID(N'rigv3.Attribuer', N'SN') IS NOT NULL DROP SYNONYM rigv3.Attribuer;
IF OBJECT_ID(N'rigv3.Restituer', N'SN') IS NOT NULL DROP SYNONYM rigv3.Restituer;
CREATE SYNONYM rigv3.Attribuer FOR [$(BaseV3)].num.Attribuer;
CREATE SYNONYM rigv3.Restituer FOR [$(BaseV3)].num.Restituer;
GO

-- NULL si la base V3 du greffe peut répondre à @Programme ; sinon pourquoi elle ne le peut pas.
-- Regardé AVANT l'appel (NUM-007 T01) : une base hors ligne, en restauration, en utilisateur unique ou
-- restreint, ou un droit retiré, se voit ici sans attendre ni rien écrire.
CREATE OR ALTER FUNCTION rigv3.BaseV3Injoignable (@Programme sysname)
RETURNS nvarchar(200)
AS
BEGIN
    DECLARE @Base sysname = (SELECT BaseV3 FROM rigv3.Pont WHERE Id = 1 AND RetourLe IS NULL);
    IF @Base IS NULL RETURN N'aucun pont V2 -> V3 en service';
    DECLARE @Etat nvarchar(60) = CAST(DATABASEPROPERTYEX(@Base, 'Status') AS nvarchar(60)),
            @Acces nvarchar(60) = CAST(DATABASEPROPERTYEX(@Base, 'UserAccess') AS nvarchar(60));
    IF @Etat IS NULL RETURN N'base ' + @Base + N' introuvable';
    IF @Etat <> N'ONLINE' RETURN N'base ' + @Base + N' ' + @Etat;
    IF @Acces <> N'MULTI_USER' RETURN N'base ' + @Base + N' en ' + @Acces;
    IF HAS_DBACCESS(@Base) <> 1 RETURN N'aucun accès à la base ' + @Base;
    IF ISNULL(HAS_PERMS_BY_NAME(QUOTENAME(@Base) + N'.' + @Programme, N'OBJECT', N'EXECUTE'), 0) <> 1
        RETURN N'droit EXECUTE sur ' + @Programme + N' absent dans ' + @Base;
    RETURN NULL;
END;
GO

-- Le message de NUM-007 CA01, mot pour mot ; @Geste = N'attribué' ou N'rendu'.
CREATE OR ALTER FUNCTION rigv3.MessageIndisponible (@Geste nvarchar(20))
RETURNS nvarchar(400)
AS
BEGIN
    RETURN N'Numérotation indisponible : la base de la V3 du greffe '
         + ISNULL((SELECT CodeGreffe FROM rigv3.Pont WHERE Id = 1), N'?')
         + N' ne répond pas. Aucun numéro n''a été ' + @Geste + N'. Réessayez ou appelez l''assistance d''Amitel.';
END;
GO

-- Qui demande, pour le journal de la V3 : l'agent que l'application V2 a posé dans SESSION_CONTEXT
-- (RigUserId), sinon le compte SQL -- la même règle que le déclencheur NF525 de NUMERO_METIER
-- (RIG_DEV.dbo.TR_NUMERO_METIER_JET, lu le 2026-11-21).
CREATE OR ALTER FUNCTION rigv3.CompteAppelant ()
RETURNS nvarchar(128)
AS
BEGIN
    DECLARE @Id int = TRY_CONVERT(int, SESSION_CONTEXT(N'RigUserId'));
    RETURN ISNULL((SELECT CAST(UTLST_LOGIN AS nvarchar(128)) FROM dbo.UTILISATEUR WHERE UTLST_ID_UTLST = @Id),
                  ORIGINAL_LOGIN());
END;
GO

-- L'appel au compteur de la V3, commun aux deux programmes d'attribution (NUM-002 T01, NUM-007 T01).
-- Rend le code de num.Attribuer -- 0, -2 type inconnu, ou un refus du compteur avec son message (-5 série
-- pleine ou plage épuisée, -6 année au-delà de la suivante : lot 6) -- ou -9 si la numérotation est
-- indisponible ; @Etat est l'état de l'erreur que lèvera l'appelant (1, 2, 3 : panne ; 5 : refus).
--   * Code greffe (NUM-002 CA08) : celui de l'appelant s'il n'est ni vide ni « 9999 », sinon NULL --
--     num.Attribuer prend alors celui du compteur : « 9999 » n'est jamais écrit dans un numéro.
--   * Attente d'un compteur tenu par une autre transaction : 5 secondes (NUM-007 CA02), puis -9.
--   * Rien n'est consommé en cas d'échec : num.Attribuer travaille dans un point de sauvegarde de la
--     transaction de l'appelant, ou dans la sienne, et l'annule en erreur.
CREATE OR ALTER PROCEDURE rigv3.DemanderNumero
    @Programme  sysname,
    @Type       varchar(5),
    @Annee      smallint,
    @Jour       smallint,
    @CodeGreffe varchar(5),
    @Numero     varchar(40)   OUTPUT,
    @Message    nvarchar(400) OUTPUT,
    @Etat       tinyint       OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @rc int, @Compte nvarchar(128) = rigv3.CompteAppelant(), @Poste nvarchar(128) = HOST_NAME(),
            @Code varchar(5) = NULLIF(NULLIF(LTRIM(RTRIM(@CodeGreffe)), ''), '9999'),
            @Panne nvarchar(200) = rigv3.BaseV3Injoignable(N'num.Attribuer');
    SELECT @Numero = NULL, @Message = NULL, @Etat = NULL;

    IF @Panne IS NULL
    BEGIN
        BEGIN TRY
            EXEC @rc = rigv3.Attribuer @Type = @Type, @Numero = @Numero OUTPUT, @Annee = @Annee, @Jour = @Jour,
                 @CodeGreffe = @Code, @Logiciel = 'V2', @Compte = @Compte, @Poste = @Poste,
                 @DelaiVerrouMs = 5000, @Message = @Message OUTPUT;
            IF @rc = 0 AND ISNULL(@Numero, '') <> '' RETURN 0;
            IF @rc = -2 RETURN -2;
            -- fix-ok: num.Attribuer redéfini par le lot 6 (Base\migrations\NUM-005_reprise.sql, lu dans sa copie le
            -- 2026-11-21) rend aussi -6 ; un refus du compteur passe tel quel avec son message, ce n'est pas une panne.
            IF @rc < 0 AND @rc <> -9 BEGIN SET @Etat = 5; RETURN @rc; END;
            -- 0 sans numéro : impossible avec les formats connus (tous portent « C ») ; jamais rendu 0 pour autant.
            SET @Panne = N'num.Attribuer a rendu ' + CAST(@rc AS nvarchar(11)) + N' sans numéro';
            SET @Etat = 3;
        END TRY
        BEGIN CATCH
            SELECT @Panne = ERROR_MESSAGE(), @Etat = CASE WHEN ERROR_NUMBER() = 51001 THEN 2 ELSE 3 END;
        END CATCH;
    END
    ELSE SET @Etat = 1;

    SELECT @Numero = NULL, @Message = rigv3.MessageIndisponible(N'attribué');
    RETURN -9;
END;
GO

-- L'appel à la restitution de la V3, pour RESTITUER_NUMERO (NUM-002, NUM-007 CA06).
CREATE OR ALTER PROCEDURE rigv3.RendreNumero
    @Type        varchar(5),
    @Numero      varchar(20),
    @Annee       smallint,
    @Explication varchar(50),
    @Issue       varchar(20)   OUTPUT,
    @Message     nvarchar(400) OUTPUT,
    @Etat        tinyint       OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @rc int, @Compte nvarchar(128) = rigv3.CompteAppelant(), @Poste nvarchar(128) = HOST_NAME(),
            @Texte nvarchar(max) = @Explication,
            @Panne nvarchar(200) = rigv3.BaseV3Injoignable(N'num.Restituer');
    SELECT @Issue = NULL, @Message = NULL, @Etat = NULL;

    IF @Panne IS NULL
    BEGIN
        BEGIN TRY
            EXEC @rc = rigv3.Restituer @Type = @Type, @Numero = @Numero, @Annee = @Annee, @Explication = @Texte,
                 @Logiciel = 'V2', @Compte = @Compte, @Poste = @Poste, @DelaiVerrouMs = 5000,
                 @Issue = @Issue OUTPUT, @Message = @Message OUTPUT;
            RETURN @rc;
        END TRY
        BEGIN CATCH
            SELECT @Panne = ERROR_MESSAGE(), @Etat = CASE WHEN ERROR_NUMBER() = 51001 THEN 2 ELSE 3 END;
        END CATCH;
    END
    ELSE SET @Etat = 1;

    SELECT @Issue = NULL, @Message = rigv3.MessageIndisponible(N'rendu');
    RETURN -9;
END;
GO

-- NUM-006 CA04 : après le passage, NUMERO_METIER est la copie que tient la V3. Une écriture qui ne
-- vient pas de sa recopie (un ancien outil de reprise, MigrationNumerosMetier.cs:72-77 ; un ancien
-- programme d'attribution resté en cours pendant la bascule) est refusée, et toute son instruction
-- annulée. La recopie se signale par SESSION_CONTEXT 'rigv3.recopie' = 1 (pont-v3.sql) : une garde
-- contre l'erreur, pas contre un programme qui voudrait la tromper.
CREATE OR ALTER TRIGGER dbo.TR_NUMERO_METIER_RIGV3 ON dbo.NUMERO_METIER
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF TRY_CONVERT(int, SESSION_CONTEXT(N'rigv3.recopie')) = 1 RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    DECLARE @m nvarchar(2048) =
        N'Écriture refusée dans NUMERO_METIER : depuis le passage du greffe '
        + ISNULL((SELECT CodeGreffe FROM rigv3.Pont WHERE Id = 1), N'?')
        + N' à la V3, ses compteurs sont tenus par le compteur de la V3 (base '
        + ISNULL((SELECT BaseV3 FROM rigv3.Pont WHERE Id = 1), N'?')
        + N', num.Attribuer et num.Restituer) et seulement recopiés ici. Demander un numéro par GENERER_NUMERO, '
        + N'le rendre par RESTITUER_NUMERO (NUM-006).';
    THROW 51006, @m, 1;
END;
GO
