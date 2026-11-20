-- NUM-001 : le compteur central des numéros, dans la base V3 du greffe (carnet\numerotation.json :
-- NUM-001 tenir les compteurs, NUM-004 rendre un numéro, NUM-008 aucun doublon quand V2 et V3 numérotent).
-- Décision Q16 : la V3 distribue TOUS les numéros ; GENERER_NUMERO, CHERCHER_NUMERO et RESTITUER_NUMERO
-- de la V2 appelleront num.Attribuer et num.Restituer (lot 7), sur la même instance, dans leur transaction.
--
--   num.Compteur     une ligne « compteur » de NUMERO_METIER (V2) : clé UNIQUE (Type, Annee, Jour)
--   num.NumeroRendu  une ligne « attribution erronée » (NMMTR_ATTRIBUTION_ERRONEE = 1) : un numéro à redonner
--   num.Journal      chaque attribution, réattribution, restitution et refus : qui, quand, pourquoi
--   num.Attribuer    le programme d'attribution (GENERER_NUMERO.sql), seul à écrire dans ces tables
--   num.Restituer    la restitution (RESTITUER_NUMERO.sql)
--   num_demandeur    le rôle des logiciels qui demandent : EXECUTE sur les deux programmes, lecture seule ailleurs
--
-- Mode d'emploi, règles et écarts avec la V2 : docs\reconstruction\numerotation-compteur.md.

CREATE TABLE num.Compteur (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Compteur PRIMARY KEY NONCLUSTERED,
    Type                varchar(5)    NOT NULL,                                       -- NMMTR_TYPE_METIER
    Annee               smallint      NOT NULL,                                       -- NMMTR_ANNEE
    -- Le jour (quantième 1..366) pour une série dont le format porte « Q » (un compteur par jour,
    -- ex. JGDJ) ; 0 pour les autres : un seul compteur par année.
    Jour                smallint      NOT NULL CONSTRAINT DF_Compteur_Jour DEFAULT (0),
    Libelle             varchar(50)   NULL,
    Structure           varchar(16)   NOT NULL,                                       -- A L S C Q G K, ou caractère recopié
    CodageAnnee         tinyint       NOT NULL CONSTRAINT DF_Compteur_CodageAnnee DEFAULT (0),   -- 0, 1, 2 ou 4 chiffres
    Lettre              varchar(16)   NULL,
    Separateur          varchar(5)    NULL,
    TailleChrono        smallint      NOT NULL CONSTRAINT DF_Compteur_TailleChrono DEFAULT (1),
    CodeGreffe          varchar(5)    NULL,
    Debut               int           NOT NULL CONSTRAINT DF_Compteur_Debut DEFAULT (0),   -- NMMTR_CHRONO_DEBUT
    -- Le dernier chrono donné ; NULL = aucun encore (le prochain sera Debut). La V2 note « rien »
    -- par 0, ce qui redonne 0 sans fin quand Debut vaut 0 : la reprise (NUM-005) traduit 0 en NULL.
    Chrono              int           NULL,
    GestionRendus       bit           NOT NULL CONSTRAINT DF_Compteur_GestionRendus DEFAULT (0), -- NMMTR_GESTION_NUM_ERR
    Redirection         varchar(5)    NULL,                                           -- NMMTR_FILE_REDIRECTION
    TableCible          varchar(32)   NULL,                                           -- NMMTR_TABLE_AFFECTEE
    ColonneCible        varchar(32)   NULL,                                           -- NMMTR_ATTRIBUT_AFFECTE
    DerniereAttribution datetime2(3)  NULL,
    CreeLe              datetime2(3)  NOT NULL CONSTRAINT DF_Compteur_CreeLe DEFAULT (SYSDATETIME()),
    IdV2                int           NULL,                                           -- NMMTR_ID_NMMTR repris (NUM-005)
    -- L'index de la V2 sur (type, année, quantième) n'est pas unique : deux compteurs ARRD pour un
    -- même jour existent dans RIG_DEV (2014, 2018). Ici la clé est UNIQUE (NUM-001 CA02).
    CONSTRAINT UQ_Compteur_Cle UNIQUE CLUSTERED (Type, Annee, Jour),
    CONSTRAINT CK_Compteur_Jour CHECK (Jour BETWEEN 0 AND 366),
    CONSTRAINT CK_Compteur_Chrono CHECK (Chrono IS NULL OR Chrono >= Debut)
);

CREATE TABLE num.NumeroRendu (
    Serie       varchar(5)    NOT NULL,
    Annee       smallint      NOT NULL,
    Jour        smallint      NOT NULL,
    Chrono      int           NOT NULL,
    Numero      varchar(40)   NOT NULL,
    -- Les 50 premiers caractères, comme NMMTR_EXPLICATION ; le texte entier est dans num.Journal.
    Explication varchar(50)   NULL,
    RenduLe     datetime2(3)  NOT NULL CONSTRAINT DF_NumeroRendu_RenduLe DEFAULT (SYSDATETIME()),
    Logiciel    char(2)       NOT NULL,
    Compte      nvarchar(128) NULL,
    Poste       nvarchar(128) NULL,
    IdV2        int           NULL,
    -- Une seule entrée redonnable par numéro (NUM-004 CA05), identifié par sa série (NUM-001 CA07).
    CONSTRAINT PK_NumeroRendu PRIMARY KEY CLUSTERED (Serie, Annee, Jour, Chrono),
    CONSTRAINT FK_NumeroRendu_Compteur FOREIGN KEY (Serie, Annee, Jour) REFERENCES num.Compteur (Type, Annee, Jour),
    CONSTRAINT CK_NumeroRendu_Logiciel CHECK (Logiciel IN ('V2', 'V3'))
);

CREATE TABLE num.Journal (
    Id          bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Journal PRIMARY KEY CLUSTERED,
    Le          datetime2(3)  NOT NULL CONSTRAINT DF_Journal_Le DEFAULT (SYSDATETIME()),
    Evenement   varchar(30)   NOT NULL,
    TypeDemande varchar(5)    NOT NULL,          -- le type demandé, avant redirection
    Serie       varchar(5)    NOT NULL,          -- la série qui a servi
    Annee       smallint      NULL,
    Jour        smallint      NULL,
    Chrono      int           NULL,
    Numero      varchar(40)   NULL,
    Code        int           NOT NULL,          -- le code rendu au demandeur
    Logiciel    char(2)       NOT NULL,
    Compte      nvarchar(128) NULL,
    Poste       nvarchar(128) NULL,
    Explication nvarchar(max) NULL,
    -- « reprise » : réservé à la reprise de la V2 (NUM-005, lot 6).
    CONSTRAINT CK_Journal_Evenement CHECK (Evenement IN ('attribution', 'reattribution', 'restitution',
        'restitution non redonnable', 'restitution en double', 'refus', 'reprise')),
    CONSTRAINT CK_Journal_Logiciel CHECK (Logiciel IN ('V2', 'V3'))
);
CREATE INDEX IX_Journal_Numero ON num.Journal (Serie, Annee, Jour, Chrono);
GO

-- Le numéro écrit selon le format d'un compteur, comme GENERER_NUMERO.sql:245-288 :
-- A année (CodageAnnee chiffres ; un codage inconnu donne « XXXX »), L lettre, S séparateur,
-- C chrono complété de zéros à TailleChrono, Q jour sur 3 chiffres, G code greffe, K « * » ;
-- tout autre caractère est recopié (« A-C » de FACTU donne « 26-022768 »).
CREATE FUNCTION num.Composer
(
    @Structure varchar(16), @CodageAnnee tinyint, @Lettre varchar(16), @Separateur varchar(5),
    @TailleChrono smallint, @CodeGreffe varchar(5), @Annee smallint, @Jour smallint, @Chrono int
)
RETURNS varchar(40)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @CodeAnnee varchar(4) = CASE @CodageAnnee
            WHEN 0 THEN ''
            WHEN 1 THEN RIGHT(CAST(@Annee AS varchar(5)), 1)
            WHEN 2 THEN RIGHT(CAST(@Annee AS varchar(5)), 2)
            WHEN 4 THEN CAST(@Annee AS varchar(5))
            ELSE 'XXXX' END;
    DECLARE @CodeJour varchar(3) = RIGHT('00' + CAST(@Jour AS varchar(3)), 3);
    DECLARE @CodeChrono varchar(11) = CAST(@Chrono AS varchar(11));
    IF LEN(@CodeChrono) < @TailleChrono
        SET @CodeChrono = REPLICATE('0', @TailleChrono - LEN(@CodeChrono)) + @CodeChrono;

    DECLARE @Numero varchar(40) = '', @i int = 1, @c char(1);
    WHILE @i <= LEN(@Structure)
    BEGIN
        SET @c = SUBSTRING(@Structure, @i, 1);
        SET @Numero += CASE @c
            WHEN 'A' THEN @CodeAnnee
            WHEN 'S' THEN ISNULL(@Separateur, '')
            WHEN 'C' THEN @CodeChrono
            WHEN 'L' THEN ISNULL(@Lettre, '')
            WHEN 'Q' THEN @CodeJour
            WHEN 'G' THEN ISNULL(@CodeGreffe, '')
            WHEN 'K' THEN '*'
            ELSE @c END;
        SET @i += 1;
    END;
    RETURN @Numero;
END;
GO

-- La lecture inverse : ce que dit un numéro écrit au format donné (code de l'année, jour, chrono).
-- Aucune ligne si le numéro ne suit pas ce format à la lettre : longueur, chiffres, lettre,
-- séparateur, « * » et caractères recopiés. Plus stricte que RESTITUER_NUMERO.sql:76-105, qui
-- retranche des longueurs sans rien vérifier (NUM-004 CA06).
CREATE FUNCTION num.LireNumero
(
    @Numero varchar(40), @Structure varchar(16), @CodageAnnee tinyint, @Lettre varchar(16),
    @Separateur varchar(5), @TailleChrono smallint, @LongueurGreffe int
)
RETURNS @Lu TABLE (CodeAnnee varchar(4) NOT NULL, Jour smallint NOT NULL, Chrono int NOT NULL)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @i int = 1, @p int = 1, @c char(1), @n int, @m varchar(40),
            @CodeAnnee varchar(4) = '', @Jour smallint = 0, @Chrono int = NULL,
            @Longueur int = ISNULL(DATALENGTH(@Numero), 0);
    SET @Lettre = ISNULL(@Lettre, '');
    SET @Separateur = ISNULL(@Separateur, '');
    IF CHARINDEX('A', @Structure) > 0 AND @CodageAnnee NOT IN (0, 1, 2, 4) RETURN;

    WHILE @i <= LEN(@Structure)
    BEGIN
        SET @c = SUBSTRING(@Structure, @i, 1);
        SET @n = CASE @c
            WHEN 'A' THEN @CodageAnnee
            WHEN 'L' THEN DATALENGTH(@Lettre)
            WHEN 'S' THEN DATALENGTH(@Separateur)
            WHEN 'Q' THEN 3
            WHEN 'G' THEN ISNULL(@LongueurGreffe, 0)
            WHEN 'C' THEN @TailleChrono
            ELSE 1 END;
        IF @p + @n - 1 > @Longueur RETURN;                                   -- trop court
        SET @m = SUBSTRING(@Numero, @p, @n);
        IF @c IN ('A', 'Q', 'C') AND @m COLLATE Latin1_General_BIN LIKE '%[^0-9]%' RETURN;
        IF @c = 'A' SET @CodeAnnee = @m;
        IF @c = 'Q' SET @Jour = TRY_CAST(@m AS smallint);
        IF @c = 'C' SET @Chrono = TRY_CAST(@m AS int);
        IF @c IN ('Q', 'C') AND (@m = '' OR TRY_CAST(@m AS int) IS NULL) RETURN;
        IF @c = 'L' AND @m <> @Lettre RETURN;
        IF @c = 'S' AND @m <> @Separateur RETURN;
        IF @c = 'K' AND @m <> '*' RETURN;
        IF @c NOT IN ('A', 'Q', 'C', 'L', 'S', 'K', 'G') AND @m <> @c RETURN; -- caractère recopié ; G : tel quel
        SET @p += @n;
        SET @i += 1;
    END;
    IF @p <= @Longueur RETURN;                                               -- trop long
    IF @Chrono IS NULL RETURN;
    IF CHARINDEX('Q', @Structure) > 0 AND (@Jour < 1 OR @Jour > 366) RETURN;
    INSERT @Lu (CodeAnnee, Jour, Chrono) VALUES (@CodeAnnee, @Jour, @Chrono);
    RETURN;
END;
GO

-- ============================================================================
-- num.Attribuer : donner le numéro suivant d'une série (remplace GENERER_NUMERO et CHERCHER_NUMERO).
--
-- Codes rendus (RETURN) : 0 = numéro donné ; -2 = type inconnu (comme la V2, rien n'est écrit) ;
-- -5 = le chrono suivant dépasse la taille du format (la V2 bouclait sans fin, GENERER_NUMERO.sql:261-266).
-- Une erreur LÈVE (THROW) : 51001 = compteur tenu par une autre transaction au-delà de @DelaiVerrouMs ;
-- 51000 = paramètre faux ; toute autre erreur SQL telle quelle. Rien n'est alors consommé.
--
-- Transaction : appelé dans une transaction, il y travaille (point de sauvegarde) et ses écritures
-- en suivent le sort -- annulée, aucun numéro n'est consommé (NUM-001 CA03) ; sinon il ouvre la sienne.
--
-- Simultanéité (NUM-001 CA02, NUM-008) : chaque compteur (Serie, Annee, Jour), qu'il existe ou non, a
-- son verrou applicatif « num:<serie>:<annee>:<jour> » (sp_getapplock), pris jusqu'à la fin de la
-- transaction par num.Attribuer comme par num.Restituer : deux demandes au même instant, de la V2 ou
-- de la V3, passent l'une après l'autre, et un compteur manquant n'est créé qu'une fois. Ce verrou ne
-- bloque que SA clé (pas de verrou de plage qui gênerait le jour d'à côté) et son attente est bornée
-- par @DelaiVerrouMs. Le chrono suivant est lu et écrit dans UNE instruction (UPDATE ... OUTPUT).
-- fix-ok: SET LOCK_TIMEOUT refuse une variable -- erreur SQL 102 « Incorrect syntax near 'LOCK_TIMEOUT' »
-- mesurée au premier passage du banc (2026-11-20) ; le délai passe donc par sp_getapplock @LockTimeout.
-- Les réglages (redirection, format) se lisent sans verrou, comme la V2 (GENERER_NUMERO.sql:42,57,73) :
-- une attribution ne les change jamais ; le chrono, lui, ne se lit que sous verrou.
-- ============================================================================
CREATE PROCEDURE num.Attribuer
    @Type          varchar(5),
    @Numero        varchar(40)   = NULL OUTPUT,
    @Annee         smallint      = 0,          -- 0 : l'année en cours (GENERER_NUMERO.sql:35-36)
    @Jour          smallint      = 0,          -- quantième ; 0 : aujourd'hui (GENERER_NUMERO.sql:38-39)
    @CodeGreffe    varchar(5)    = NULL,       -- pour « G » ; NULL : celui du compteur
    @Logiciel      char(2)       = 'V3',       -- 'V2' ou 'V3' : qui demande
    @Compte        nvarchar(128) = NULL,       -- NULL : le compte SQL de la session
    @Poste         nvarchar(128) = NULL,       -- NULL : le poste de la session
    @DelaiVerrouMs int           = 10000,      -- attente maximale du compteur ; -1 = sans limite
    @Serie         varchar(5)    = NULL OUTPUT,-- la série qui a servi (après redirection)
    @Chrono        int           = NULL OUTPUT,
    @Reattribue    bit           = NULL OUTPUT,-- 1 : c'est un numéro rendu, redonné
    @Message       nvarchar(400) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @DelaiVerrouMs = ISNULL(@DelaiVerrouMs, 10000);

    DECLARE @Maintenant datetime2(3) = SYSDATETIME(), @Entree int = @@TRANCOUNT;
    SELECT @Numero = NULL, @Serie = NULL, @Chrono = NULL, @Reattribue = 0, @Message = NULL;

    IF @Logiciel IS NULL OR @Logiciel NOT IN ('V2', 'V3')
        THROW 51000, N'num.Attribuer : @Logiciel attend « V2 » ou « V3 ».', 1;
    IF ISNULL(@Annee, 0) = 0 SET @Annee = YEAR(@Maintenant);
    IF ISNULL(@Jour, 0) = 0 SET @Jour = DATEPART(dayofyear, @Maintenant);
    IF @Annee < 1 OR @Jour NOT BETWEEN 1 AND 366
        THROW 51000, N'num.Attribuer : @Annee doit être positive et @Jour entre 1 et 366.', 1;
    SET @Compte = ISNULL(@Compte, ORIGINAL_LOGIN());
    SET @Poste = ISNULL(@Poste, HOST_NAME());

    -- 1. La série : le type demandé, ou celui où il est redirigé (GENERER_NUMERO.sql:42-55).
    DECLARE @Redirection varchar(5);
    SELECT TOP (1) @Serie = Type, @Redirection = Redirection
    FROM num.Compteur WITH (NOLOCK)
    WHERE Type = @Type
    ORDER BY Annee DESC, Jour DESC;
    IF @Serie IS NULL
    BEGIN
        SET @Message = N'Type de numéro inconnu : « ' + ISNULL(@Type, N'') + N' ».';
        RETURN -2;
    END;
    IF ISNULL(@Redirection, '') <> '' SET @Serie = @Redirection;

    -- 2. Le format de référence : celui de l'année demandée si elle a un compteur, sinon celui de la
    --    dernière année connue de la série (GENERER_NUMERO.sql:191-233). Il dit si la série a un
    --    compteur par jour (« Q ») ou par année.
    DECLARE @Structure varchar(16), @AnneeReference smallint;
    SELECT TOP (1) @Structure = Structure, @AnneeReference = Annee
    FROM num.Compteur WITH (NOLOCK)
    WHERE Type = @Serie
    ORDER BY CASE WHEN Annee = @Annee THEN 0 ELSE 1 END, Annee DESC, Jour DESC;
    IF @Structure IS NULL
    BEGIN
        SET @Message = N'Type de numéro inconnu : « ' + @Type + N' » est redirigé vers « ' + @Serie + N' », qui n''existe pas.';
        RETURN -2;
    END;
    DECLARE @Cle smallint = CASE WHEN CHARINDEX('Q', @Structure) > 0 THEN @Jour ELSE 0 END;
    DECLARE @Libelle nvarchar(60) = @Serie + N' ' + CAST(@Annee AS nvarchar(6))
                                    + CASE WHEN @Cle > 0 THEN N' jour ' + CAST(@Cle AS nvarchar(3)) ELSE N'' END;

    DECLARE @Id int, @Dernier int, @Debut int, @Gestion bit, @Codage tinyint, @Lettre varchar(16),
            @Separateur varchar(5), @Taille smallint, @GreffeCompteur varchar(5), @Cree bit = 0;
    DECLARE @Pris TABLE (Chrono int NOT NULL);
    DECLARE @Ressource nvarchar(255) = N'num:' + @Serie + N':' + CAST(@Annee AS nvarchar(6)) + N':' + CAST(@Cle AS nvarchar(3)),
            @Verrou int;

    BEGIN TRY
        IF @Entree = 0 BEGIN TRANSACTION; ELSE SAVE TRANSACTION NumAttribuer;

        -- 3. Le compteur est à nous jusqu'à la fin de la transaction (qu'il existe ou non), puis on le lit.
        EXEC @Verrou = sp_getapplock @Resource = @Ressource, @LockMode = 'Exclusive', @LockOwner = 'Transaction',
                                     @LockTimeout = @DelaiVerrouMs;
        IF @Verrou < 0
        BEGIN
            SET @Message = N'Numérotation : le compteur ' + @Libelle + N' est tenu par une autre transaction depuis plus de '
                         + CAST(@DelaiVerrouMs AS nvarchar(12)) + N' ms ; aucun numéro n''a été donné. Réessayer.'
                         + CASE WHEN @Verrou = -3 THEN N' (interblocage)' ELSE N'' END;
            THROW 51001, @Message, 1;
        END;
        SELECT @Id = Id, @Dernier = Chrono, @Debut = Debut, @Gestion = GestionRendus, @Structure = Structure,
               @Codage = CodageAnnee, @Lettre = Lettre, @Separateur = Separateur, @Taille = TailleChrono,
               @GreffeCompteur = CodeGreffe
        FROM num.Compteur WITH (UPDLOCK, ROWLOCK)
        WHERE Type = @Serie AND Annee = @Annee AND Jour = @Cle;

        IF @Id IS NULL
        BEGIN
            -- Nouvelle année ou nouveau jour : les réglages de la référence, et la valeur de départ.
            INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur,
                                 TailleChrono, CodeGreffe, Debut, Chrono, GestionRendus, Redirection,
                                 TableCible, ColonneCible)
            SELECT TOP (1) Type, @Annee, @Cle, Libelle, Structure, CodageAnnee, Lettre, Separateur,
                   TailleChrono, CodeGreffe, Debut, NULL, GestionRendus, Redirection, TableCible, ColonneCible
            FROM num.Compteur WITH (NOLOCK)
            WHERE Type = @Serie
            ORDER BY CASE WHEN Annee = @Annee THEN 0 ELSE 1 END, Annee DESC, Jour DESC;
            SET @Cree = 1;
            SELECT @Id = Id, @Dernier = Chrono, @Debut = Debut, @Gestion = GestionRendus, @Structure = Structure,
                   @Codage = CodageAnnee, @Lettre = Lettre, @Separateur = Separateur, @Taille = TailleChrono,
                   @GreffeCompteur = CodeGreffe
            FROM num.Compteur
            WHERE Type = @Serie AND Annee = @Annee AND Jour = @Cle;
        END;

        -- 4. Un numéro rendu d'abord, le plus petit, une seule fois (GENERER_NUMERO.sql:85-105, 150-168).
        --    Comme la V2, une série par jour redonne sans regarder le réglage (écart « à valider » de NUM-004).
        IF @Gestion = 1 OR CHARINDEX('Q', @Structure) > 0
        BEGIN
            ;WITH Rendu AS (
                SELECT TOP (1) Chrono FROM num.NumeroRendu
                WHERE Serie = @Serie AND Annee = @Annee AND Jour = @Cle
                ORDER BY Chrono)
            DELETE FROM Rendu OUTPUT deleted.Chrono INTO @Pris (Chrono);
        END;
        SELECT @Chrono = Chrono FROM @Pris;

        IF @Chrono IS NOT NULL
            SET @Reattribue = 1;
        ELSE
        BEGIN
            IF LEN(CAST(ISNULL(@Dernier + 1, @Debut) AS varchar(11))) > @Taille
            BEGIN
                IF @Entree = 0 ROLLBACK TRANSACTION; ELSE ROLLBACK TRANSACTION NumAttribuer;
                SET @Message = N'Série pleine : le compteur ' + @Libelle + N' est à ' + CAST(@Dernier AS nvarchar(11))
                             + N', le suivant dépasse les ' + CAST(@Taille AS nvarchar(6)) + N' chiffres de son format.';
                INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Code, Logiciel, Compte, Poste, Explication)
                VALUES ('refus', @Type, @Serie, @Annee, @Cle, @Dernier, -5, @Logiciel, @Compte, @Poste, @Message);
                RETURN -5;
            END;
            -- 5. Le suivant : lu et écrit dans une seule instruction.
            UPDATE num.Compteur
            SET Chrono = CASE WHEN Chrono IS NULL THEN Debut ELSE Chrono + 1 END,
                DerniereAttribution = @Maintenant
            OUTPUT inserted.Chrono INTO @Pris (Chrono)
            WHERE Id = @Id;
            SELECT @Chrono = Chrono FROM @Pris;
        END;

        -- 6. Le numéro écrit. « G » : le code greffe demandé, sauf au premier numéro d'une année
        --    nouvelle où la V2 prend celui du compteur (GENERER_NUMERO.sql:202).
        SET @Numero = num.Composer(@Structure, @Codage, @Lettre, @Separateur, @Taille,
            CASE WHEN @Cree = 1 AND @AnneeReference <> @Annee THEN @GreffeCompteur
                 ELSE ISNULL(@CodeGreffe, @GreffeCompteur) END,
            @Annee, @Jour, @Chrono);

        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Compte, Poste)
        VALUES (CASE WHEN @Reattribue = 1 THEN 'reattribution' ELSE 'attribution' END,
                @Type, @Serie, @Annee, @Cle, @Chrono, @Numero, 0, @Logiciel, @Compte, @Poste);

        IF @Entree = 0 COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        DECLARE @Erreur int = ERROR_NUMBER();
        -- Sa propre transaction : annulée. Celle de l'appelant : ramenée au point de sauvegarde si elle
        -- est encore valide ; condamnée (XACT_ABORT, interblocage), c'est à l'appelant de l'annuler.
        IF @Entree = 0 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @Entree > 0 AND XACT_STATE() = 1 ROLLBACK TRANSACTION NumAttribuer;
        SELECT @Numero = NULL, @Chrono = NULL, @Reattribue = 0;
        IF @Erreur = 1222
        BEGIN
            SET @Message = N'Numérotation : le compteur ' + @Libelle + N' est tenu par une autre transaction depuis plus de '
                         + CAST(@DelaiVerrouMs AS nvarchar(12)) + N' ms ; aucun numéro n''a été donné. Réessayer.';
            THROW 51001, @Message, 1;
        END;
        THROW;
    END CATCH;
END;
GO

-- ============================================================================
-- num.Restituer : rendre un numéro donné par erreur, pour qu'il soit redonné (remplace RESTITUER_NUMERO).
--
-- Codes rendus (RETURN, NUM-004 CA09) et @Issue :
--    0  'rendu'           le numéro sera redonné au prochain demandeur de sa série ;
--    0  'non redonnable'  série à restitution inactive : rien n'est redonné (comme la V2), la demande est journalisée ;
--    0  'deja rendu'      il attendait déjà d'être redonné : la demande est journalisée, rien de plus ;
--   -2  'type inconnu'    comme la V2 ; rien n'est écrit ;
--   -3  'illisible'       le numéro ne suit le format d'aucun compteur de la série ; rien n'est écrit ;
--   -4  'jamais donne'    lisible, mais au-delà du compteur ou sans compteur : refus journalisé.
-- Une erreur LÈVE, comme num.Attribuer (51001 : compteur tenu au-delà de @DelaiVerrouMs).
-- Le compteur n'est jamais diminué ni effacé (RESTITUER_NUMERO.sql:114-123, note Brain
-- rig-v3-deux-lignes-numero-metier-apres-restitution).
-- ============================================================================
CREATE PROCEDURE num.Restituer
    @Type          varchar(5),
    @Numero        varchar(40),
    @Annee         smallint      = 0,          -- utile seulement si le format ne porte pas l'année (NUM-004 CA07)
    @Explication   nvarchar(max) = NULL,
    @Logiciel      char(2)       = 'V3',
    @Compte        nvarchar(128) = NULL,
    @Poste         nvarchar(128) = NULL,
    @DelaiVerrouMs int           = 10000,
    @Serie         varchar(5)    = NULL OUTPUT,
    @Issue         varchar(20)   = NULL OUTPUT,
    @Message       nvarchar(400) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @DelaiVerrouMs = ISNULL(@DelaiVerrouMs, 10000);

    DECLARE @Entree int = @@TRANCOUNT, @AnneeCourante smallint = YEAR(SYSDATETIME());
    SELECT @Serie = NULL, @Issue = NULL, @Message = NULL;

    IF @Logiciel IS NULL OR @Logiciel NOT IN ('V2', 'V3')
        THROW 51000, N'num.Restituer : @Logiciel attend « V2 » ou « V3 ».', 1;
    SET @Numero = LTRIM(RTRIM(ISNULL(@Numero, '')));
    SET @Compte = ISNULL(@Compte, ORIGINAL_LOGIN());
    SET @Poste = ISNULL(@Poste, HOST_NAME());
    -- NUM-004 CA08 : sans explication, le journal le dit ; avec, il garde le texte entier.
    SET @Explication = NULLIF(LTRIM(RTRIM(@Explication)), N'');
    IF @Explication IS NULL
        SET @Explication = N'sans explication (demande de la ' + @Logiciel + N')';

    -- 1. La série (RESTITUER_NUMERO.sql:29-42).
    DECLARE @Redirection varchar(5);
    SELECT TOP (1) @Serie = Type, @Redirection = Redirection
    FROM num.Compteur WITH (NOLOCK)
    WHERE Type = @Type
    ORDER BY Annee DESC, Jour DESC;
    IF @Serie IS NULL
    BEGIN
        SELECT @Issue = 'type inconnu', @Message = N'Type de numéro inconnu : « ' + ISNULL(@Type, N'') + N' ».';
        RETURN -2;
    END;
    IF ISNULL(@Redirection, '') <> '' SET @Serie = @Redirection;

    -- 2. Lire le numéro avec chaque format de la série : année, jour, chrono.
    DECLARE @Lectures TABLE (Structure varchar(16), CodageAnnee tinyint, Lettre varchar(16), Separateur varchar(5),
                             TailleChrono smallint, LongueurGreffe int, CodeAnnee varchar(4), Jour smallint, Chrono int);
    INSERT @Lectures
    SELECT f.Structure, f.CodageAnnee, f.Lettre, f.Separateur, f.TailleChrono, f.LongueurGreffe, l.CodeAnnee, l.Jour, l.Chrono
    FROM (SELECT DISTINCT Structure, CodageAnnee, ISNULL(Lettre, '') AS Lettre, ISNULL(Separateur, '') AS Separateur,
                 TailleChrono, DATALENGTH(ISNULL(CodeGreffe, '')) AS LongueurGreffe
          FROM num.Compteur WITH (NOLOCK)
          WHERE Type = @Serie) AS f
    CROSS APPLY num.LireNumero(@Numero, f.Structure, f.CodageAnnee, f.Lettre, f.Separateur, f.TailleChrono, f.LongueurGreffe) AS l;
    IF NOT EXISTS (SELECT 1 FROM @Lectures)
    BEGIN
        SELECT @Issue = 'illisible',
               @Message = N'Numéro illisible : « ' + @Numero + N' » ne suit le format d''aucun compteur de la série ' + @Serie + N'.';
        RETURN -3;
    END;

    -- 3. Le compteur qui l'a donné : même format, même jour, et l'année lue dans le numéro ; si le format
    --    ne porte pas l'année, @Annee, sinon l'année en cours (NUM-004 CA07). Un code d'année court
    --    (1 ou 2 chiffres) désigne l'année demandée si elle concorde, sinon la plus récente qui concorde.
    DECLARE @AnneeParDefaut smallint = CASE WHEN ISNULL(@Annee, 0) > 0 THEN @Annee ELSE @AnneeCourante END;
    DECLARE @AnneeLue smallint, @JourLu smallint, @ChronoLu int;
    SELECT TOP (1) @AnneeLue = c.Annee, @JourLu = c.Jour, @ChronoLu = l.Chrono
    FROM @Lectures AS l
    JOIN num.Compteur AS c WITH (NOLOCK)
      ON c.Type = @Serie
     AND c.Structure = l.Structure AND c.CodageAnnee = l.CodageAnnee AND c.TailleChrono = l.TailleChrono
     AND ISNULL(c.Lettre, '') = l.Lettre AND ISNULL(c.Separateur, '') = l.Separateur
     AND DATALENGTH(ISNULL(c.CodeGreffe, '')) = l.LongueurGreffe
     AND c.Jour = CASE WHEN CHARINDEX('Q', l.Structure) > 0 THEN l.Jour ELSE 0 END
     AND (   (l.CodeAnnee = '' AND c.Annee = @AnneeParDefaut)
          OR (l.CodeAnnee <> '' AND RIGHT(CAST(c.Annee AS varchar(5)), LEN(l.CodeAnnee)) = l.CodeAnnee))
    ORDER BY CASE WHEN c.Annee = @Annee THEN 0 ELSE 1 END, c.Annee DESC;

    IF @AnneeLue IS NULL
    BEGIN
        -- Lisible, mais aucun compteur pour cette année ou ce jour : jamais donné.
        SELECT TOP (1) @JourLu = l.Jour, @ChronoLu = l.Chrono,
               @AnneeLue = CASE WHEN l.CodeAnnee = '' THEN @AnneeParDefaut
                                WHEN LEN(l.CodeAnnee) = 4 THEN CAST(l.CodeAnnee AS smallint) END
        FROM @Lectures AS l;
        SELECT @Issue = 'jamais donne',
               @Message = N'Restitution refusée : « ' + @Numero + N' » n''a jamais été donné (aucun compteur '
                        + @Serie + N' pour son année ou son jour).';
        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Compte, Poste, Explication)
        VALUES ('refus', @Type, @Serie, @AnneeLue, @JourLu, @ChronoLu, @Numero, -4, @Logiciel, @Compte, @Poste,
                @Message + N' Explication donnée : ' + @Explication);
        RETURN -4;
    END;

    DECLARE @Dernier int, @Debut int, @Gestion bit, @Verrou int,
            @Ressource nvarchar(255) = N'num:' + @Serie + N':' + CAST(@AnneeLue AS nvarchar(6)) + N':' + CAST(@JourLu AS nvarchar(3));
    BEGIN TRY
        IF @Entree = 0 BEGIN TRANSACTION; ELSE SAVE TRANSACTION NumRestituer;

        -- 4. Le compteur, avec le même verrou qu'une attribution : une restitution et une demande du même
        --    compteur passent l'une après l'autre.
        EXEC @Verrou = sp_getapplock @Resource = @Ressource, @LockMode = 'Exclusive', @LockOwner = 'Transaction',
                                     @LockTimeout = @DelaiVerrouMs;
        IF @Verrou < 0
        BEGIN
            SET @Message = N'Numérotation : le compteur ' + @Serie + N' ' + CAST(@AnneeLue AS nvarchar(6))
                         + N' est tenu par une autre transaction depuis plus de ' + CAST(@DelaiVerrouMs AS nvarchar(12))
                         + N' ms ; le numéro n''a pas été rendu. Réessayer.'
                         + CASE WHEN @Verrou = -3 THEN N' (interblocage)' ELSE N'' END;
            THROW 51001, @Message, 1;
        END;
        SELECT @Dernier = Chrono, @Debut = Debut, @Gestion = GestionRendus
        FROM num.Compteur WITH (UPDLOCK, ROWLOCK)
        WHERE Type = @Serie AND Annee = @AnneeLue AND Jour = @JourLu;

        DECLARE @Code int = 0, @Evenement varchar(30);
        IF @Dernier IS NULL OR @ChronoLu > @Dernier OR @ChronoLu < @Debut
        BEGIN
            -- NUM-004 CA04 : la V2 l'accepterait et le redonnerait avant le suivant (la série sauterait).
            SELECT @Code = -4, @Evenement = 'refus', @Issue = 'jamais donne',
                   @Message = N'Restitution refusée : « ' + @Numero + N' » n''a jamais été donné (compteur ' + @Serie + N' '
                            + CAST(@AnneeLue AS nvarchar(6)) + CASE WHEN @JourLu > 0 THEN N' jour ' + CAST(@JourLu AS nvarchar(3)) ELSE N'' END
                            + N' : ' + ISNULL(N'dernier numéro donné ' + CAST(@Dernier AS nvarchar(11)), N'aucun numéro donné') + N').';
        END
        ELSE IF @Gestion = 0
            -- NUM-004 CA03 : comme la V2, rien n'est redonné (RESTITUER_NUMERO.sql:63-67) ; le trou est expliqué.
            SELECT @Evenement = 'restitution non redonnable', @Issue = 'non redonnable',
                   @Message = N'La série ' + @Serie + N' ne redonne pas ses numéros : « ' + @Numero + N' » reste un trou, expliqué au journal.';
        ELSE IF EXISTS (SELECT 1 FROM num.NumeroRendu
                        WHERE Serie = @Serie AND Annee = @AnneeLue AND Jour = @JourLu AND Chrono = @ChronoLu)
            -- NUM-004 CA05 : la V2 ajouterait une ligne de plus (jusqu'à 157 pour un même numéro).
            SELECT @Evenement = 'restitution en double', @Issue = 'deja rendu',
                   @Message = N'« ' + @Numero + N' » attend déjà d''être redonné : demande journalisée, rien de plus.';
        ELSE
        BEGIN
            INSERT num.NumeroRendu (Serie, Annee, Jour, Chrono, Numero, Explication, Logiciel, Compte, Poste)
            VALUES (@Serie, @AnneeLue, @JourLu, @ChronoLu, @Numero, LEFT(@Explication, 50), @Logiciel, @Compte, @Poste);
            SELECT @Evenement = 'restitution', @Issue = 'rendu',
                   @Message = N'« ' + @Numero + N' » sera redonné au prochain numéro demandé dans la série ' + @Serie + N'.';
        END;

        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Compte, Poste, Explication)
        VALUES (@Evenement, @Type, @Serie, @AnneeLue, @JourLu, @ChronoLu, @Numero, @Code, @Logiciel, @Compte, @Poste, @Explication);

        IF @Entree = 0 COMMIT TRANSACTION;
        RETURN @Code;
    END TRY
    BEGIN CATCH
        DECLARE @Erreur int = ERROR_NUMBER();
        IF @Entree = 0 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @Entree > 0 AND XACT_STATE() = 1 ROLLBACK TRANSACTION NumRestituer;
        SET @Issue = NULL;
        IF @Erreur = 1222
        BEGIN
            SET @Message = N'Numérotation : le compteur ' + @Serie + N' ' + CAST(@AnneeLue AS nvarchar(6))
                         + N' est tenu par une autre transaction depuis plus de ' + CAST(@DelaiVerrouMs AS nvarchar(12))
                         + N' ms ; le numéro n''a pas été rendu. Réessayer.';
            THROW 51001, @Message, 1;
        END;
        THROW;
    END CATCH;
END;
GO

-- Les droits (NUM-001 CA01) : un logiciel qui demande des numéros (le serveur de la V3, la V2 par ses
-- programmes remplacés) est membre de num_demandeur. Il exécute les deux programmes et lit les tables ;
-- il ne peut ni écrire ni effacer dedans, même s'il est aussi db_datawriter (DENY l'emporte).
-- Les programmes, eux, écrivent par le chaînage de propriété (même propriétaire, dbo).
-- Un membre de db_owner passe outre : les comptes applicatifs ne doivent pas l'être sur la base V3.
CREATE ROLE num_demandeur;
GRANT EXECUTE ON OBJECT::num.Attribuer TO num_demandeur;
GRANT EXECUTE ON OBJECT::num.Restituer TO num_demandeur;
GRANT SELECT ON OBJECT::num.Compteur TO num_demandeur;
GRANT SELECT ON OBJECT::num.NumeroRendu TO num_demandeur;
GRANT SELECT ON OBJECT::num.Journal TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.Compteur TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.NumeroRendu TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.Journal TO num_demandeur;
