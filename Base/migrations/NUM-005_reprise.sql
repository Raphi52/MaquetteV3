-- apres: NUM-001
-- NUM-005 : reprendre chaque compteur de la V2 à sa valeur du jour du passage, et ce que les séries
-- NUM-009 à NUM-021 demandent au compteur central en plus de NUM-001 (carnet\numerotation.json) :
--
--   num.Reprendre        copie NUMERO_METIER de la base V2 dans le compteur, dans UNE transaction qui
--                        bloque la table de la V2, puis rapproche ; un écart annule tout (NUM-005 CA01-CA06)
--   num.ReleverHeritage  relève dans la base V2, en lecture, les valeurs héritées à accepter (rôles en double,
--                        numéros hors format) et les numéros portés deux fois ; les signale au référent
--   num.Reprise          le rapport de chaque passage (durée du blocage, comptes, écarts : NUM-005 T03)
--   num.Archive          la copie de sauvegarde d'un ancien transfert (NUMERO_METIER_SAVE_CDU), en lecture seule
--   num.Signalement      ce qui est listé au référent (doublons, années futures, hors format, formes partagées)
--   num.ValeurHeritee    les valeurs que la V3 accepte parce qu'elles viennent de la V2, et seulement elles
--   num.CodeLiaison      les codes de liaison du greffe (GREFFE_NUMERO_LIAISON, NUM-019 T03)
--   num.OrdreFiche       les numéros tenus par fiche, hors du compteur commun (NUM-021 CA01)
--   num.Attribuer        redéfini : refuse une année au-delà de la suivante (NUM-005 CA04, T05) et un numéro
--                        au-delà de la plage de La Poste (NUM-017 CA02)
--   num.DeclarerManquant un numéro donné puis non utilisé, avec son motif (NUM-013 CA03)
--
-- Mode d'emploi, règles et écarts avec la V2 : docs\reconstruction\numerotation-series.md.

-- NUM-017 CA02 : la fin de la plage attribuée par La Poste, à saisir ; NULL = la série n'a pas de plage.
ALTER TABLE num.Compteur ADD
    FinPlage    int NULL,
    -- Prévenir quand il reste au plus ce nombre de numéros ; NULL = un dixième de la plage.
    AlertePlage int NULL;
GO
ALTER TABLE num.Compteur ADD CONSTRAINT CK_Compteur_Plage CHECK (FinPlage IS NULL OR FinPlage >= Debut);

-- NUM-013 CA02, CA03 : « manquant » = un numéro donné qui ne sera jamais utilisé, avec son motif.
ALTER TABLE num.Journal DROP CONSTRAINT CK_Journal_Evenement;
ALTER TABLE num.Journal ADD CONSTRAINT CK_Journal_Evenement CHECK (Evenement IN ('attribution', 'reattribution',
    'restitution', 'restitution non redonnable', 'restitution en double', 'refus', 'reprise', 'manquant'));

CREATE TABLE num.Reprise (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reprise PRIMARY KEY,
    BaseV2       sysname       NOT NULL,
    Debut        datetime2(3)  NOT NULL,
    Fin          datetime2(3)  NULL,
    -- Du blocage de la table V2 à la fin du rapprochement (NUM-005 CA01 : moins d'une minute).
    DureeMs      int           NULL,
    -- NULL en cours ; 0 validée ; -1 écart au rapprochement (annulée) ; -10 déjà reprise ;
    -- -11 base V2 introuvable ; -99 erreur SQL (annulée).
    Code         int           NULL,
    LignesV2     int           NULL,
    Compteurs    int           NULL,
    Rendus       int           NULL,
    Journalisees int           NULL,
    Archivees    int           NULL,
    Manquants    int           NULL,
    CodesLiaison int           NULL,
    OrdresFiche  int           NULL,
    Ecarts       int           NULL,
    Compte       nvarchar(128) NOT NULL CONSTRAINT DF_Reprise_Compte DEFAULT (ORIGINAL_LOGIN()),
    Message      nvarchar(400) NULL
);

CREATE TABLE num.RepriseEcart (
    IdReprise int          NOT NULL CONSTRAINT FK_RepriseEcart_Reprise REFERENCES num.Reprise (Id),
    Type      varchar(5)   NULL,
    Annee     int          NULL,
    Jour      int          NULL,
    Quoi      varchar(40)  NOT NULL,
    V2        nvarchar(400) NULL,
    V3        nvarchar(400) NULL
);

CREATE TABLE num.Signalement (
    Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Signalement PRIMARY KEY,
    IdReprise int           NULL,
    Sujet     varchar(40)   NOT NULL,
    Referent  varchar(30)   NOT NULL,          -- Socle, Judiciaire, RCS, Facturation
    Serie     varchar(5)    NULL,
    Annee     int           NULL,
    Jour      int           NULL,
    Valeur    varchar(60)   NULL,
    Detail    nvarchar(400) NOT NULL,
    Le        datetime2(3)  NOT NULL CONSTRAINT DF_Signalement_Le DEFAULT (SYSDATETIME()),
    CONSTRAINT CK_Signalement_Sujet CHECK (Sujet IN ('compteur en double', 'numero porte deux fois', 'annee future',
        'numero hors format', 'doublon herite', 'forme partagee'))
);

-- NUM-009 CA03, NUM-012 CA02, NUM-020 CA04 : une valeur que la V2 a écrite hors de toute règle de la V3
-- (numéro de rôle porté par deux instances, numéro de gestion « AP… » sans année, facture « ASTRA-AF… »).
-- Les vérifications de la V3 l'acceptent parce qu'elle est ici, et seulement elle : num.EstHeritee.
CREATE TABLE num.ValeurHeritee (
    Fiche     varchar(40) NOT NULL,              -- la table de la V2 : INSTANCE, DOSSIER_RCS, FACTURE
    Colonne   varchar(40) NOT NULL,              -- sans son préfixe : NUM_ROLE, NUM_GESTION, NUMERO_FACTURE
    Valeur    varchar(60) NOT NULL,
    Motif     varchar(40) NOT NULL,              -- 'doublon herite', 'hors format'
    Porteurs  int         NOT NULL,              -- combien de fiches de la V2 la portent
    IdReprise int         NULL,
    CONSTRAINT PK_ValeurHeritee PRIMARY KEY (Fiche, Colonne, Valeur)
);

-- NUM-005 CA06 : la copie de sauvegarde laissée par un ancien transfert de compteurs. Elle ne sert jamais
-- de compteur (num.Attribuer ne la lit pas) et ne se modifie plus une fois reprise.
CREATE TABLE num.Archive (
    Id              int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Archive PRIMARY KEY,
    Source          sysname       NOT NULL,      -- la table de la V2 : NUMERO_METIER_SAVE_CDU
    IdReprise       int           NULL,
    IdV2            int           NULL,
    Type            varchar(5)    NULL,
    Libelle         varchar(50)   NULL,
    Structure       varchar(16)   NULL,
    CodeGreffe      varchar(5)    NULL,
    Annee           int           NULL,
    CodageAnnee     tinyint       NULL,
    Lettre          varchar(16)   NULL,
    TailleChrono    smallint      NULL,
    Debut           int           NULL,
    Chrono          int           NULL,
    DateAttribution smalldatetime NULL,
    Quantieme       smallint      NULL,
    GestionRendus   bit           NULL,
    Erronee         bit           NULL,
    Explication     varchar(50)   NULL,
    Redirection     varchar(5)    NULL,
    Separateur      varchar(5)    NULL,
    TableCible      varchar(32)   NULL,
    ColonneCible    varchar(32)   NULL
);
GO
CREATE TRIGGER num.Archive_LectureSeule ON num.Archive INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51002, N'num.Archive est en lecture seule : la copie de sauvegarde de la V2 ne se modifie pas (NUM-005 CA06).', 1;
END;
GO

-- NUM-019 T03 : les codes de liaison du greffe (lettre + code greffe : « G3802 »). Le numéro de liaison
-- (série LIAI) les porte à la place de « G », jamais le « 9999 » par défaut de GENERER_NUMERO (CA02).
CREATE TABLE num.CodeLiaison (
    Code      varchar(5) NOT NULL CONSTRAINT PK_CodeLiaison PRIMARY KEY,
    Lettre    AS CAST(LEFT(Code, 1) AS char(1)),
    Greffe    AS CAST(SUBSTRING(Code, 2, 4) AS varchar(4)),
    IdReprise int        NULL
);

-- NUM-021 CA01 : un numéro tenu par fiche (ex. l'ordre des mentions d'un dossier : la V2 le recalcule
-- à partir de 1 à chaque enregistrement, DossierRCS.cs:2136-2143). La V3 le tient ici, repris au
-- passage au plus grand de chaque fiche ; num.AttribuerOrdre donne le suivant, à la V2 comme à la V3.
CREATE TABLE num.OrdreFiche (
    Fiche   varchar(40) NOT NULL,                -- MENTION_OBSERVATION
    Cle     int         NOT NULL,                -- l'identifiant de la fiche mère (le dossier)
    Dernier int         NOT NULL,
    CONSTRAINT PK_OrdreFiche PRIMARY KEY (Fiche, Cle),
    CONSTRAINT CK_OrdreFiche_Dernier CHECK (Dernier >= 0)
);
GO

-- Le motif LIKE de tous les numéros qu'un format peut écrire (num.Composer) : sert à reconnaître, vite
-- et sur des tables entières, un numéro qu'aucun compteur n'a pu produire (NUM-020 CA04).
CREATE FUNCTION num.MotifLike
(
    @Structure varchar(16), @CodageAnnee tinyint, @Lettre varchar(16), @Separateur varchar(5),
    @TailleChrono smallint, @LongueurGreffe int
)
RETURNS varchar(400)
WITH SCHEMABINDING
AS
BEGIN
    DECLARE @Motif varchar(400) = '', @i int = 1, @c char(1), @Texte varchar(16), @j int;
    WHILE @i <= LEN(@Structure)
    BEGIN
        SET @c = SUBSTRING(@Structure, @i, 1);
        SET @Texte = CASE @c WHEN 'L' THEN ISNULL(@Lettre, '') WHEN 'S' THEN ISNULL(@Separateur, '')
                             WHEN 'K' THEN '*' ELSE @c END;
        IF @c = 'A' SET @Motif += REPLICATE('[0-9]', CASE WHEN @CodageAnnee IN (0, 1, 2, 4) THEN @CodageAnnee ELSE 0 END)
                                + CASE WHEN @CodageAnnee IN (0, 1, 2, 4) THEN '' ELSE 'XXXX' END;
        ELSE IF @c = 'C' SET @Motif += REPLICATE('[0-9]', @TailleChrono);
        ELSE IF @c = 'Q' SET @Motif += '[0-9][0-9][0-9]';
        ELSE IF @c = 'G' SET @Motif += REPLICATE('_', ISNULL(@LongueurGreffe, 0));
        ELSE
        BEGIN
            SET @j = 1;
            WHILE @j <= DATALENGTH(@Texte)
            BEGIN
                SET @Motif += CASE SUBSTRING(@Texte, @j, 1) WHEN '[' THEN '[[]' WHEN '%' THEN '[%]' WHEN '_' THEN '[_]'
                                   ELSE SUBSTRING(@Texte, @j, 1) END;
                SET @j += 1;
            END;
        END;
        SET @i += 1;
    END;
    RETURN @Motif;
END;
GO

CREATE FUNCTION num.EstHeritee (@Fiche varchar(40), @Colonne varchar(40), @Valeur varchar(60))
RETURNS bit
AS
BEGIN
    RETURN CASE WHEN EXISTS (SELECT 1 FROM num.ValeurHeritee
                             WHERE Fiche = @Fiche AND Colonne = @Colonne AND Valeur = @Valeur) THEN 1 ELSE 0 END;
END;
GO

-- NUM-009 CA02, NUM-010 CA02, NUM-011 CA02, NUM-013 CA04, NUM-015 CA02, NUM-018 CA02 : deux séries dont
-- les formats écrivent les mêmes numéros (JGDJ / JGJC / JGOP, JDETM / JDSAN « SANC… », RC1 / RC5 / RC6
-- « F… », ECN / ENP « …E… », LTIA / LTIC / LTIF, PREV / RGCH). Un numéro n'y est identifié que par sa série
-- et sa valeur ; NumerosCommuns compte ceux que les deux compteurs du même jour ont déjà donnés.
CREATE VIEW num.FormePartagee
AS
WITH F AS (
    SELECT Type, Annee, Jour, Debut, Chrono,
           Forme = CONCAT(Structure, '|',
                          CASE WHEN CHARINDEX('A', Structure) > 0 THEN CodageAnnee END, '|',
                          CASE WHEN CHARINDEX('L', Structure) > 0 THEN Lettre END, '|',
                          CASE WHEN CHARINDEX('S', Structure) > 0 THEN Separateur END, '|',
                          CASE WHEN CHARINDEX('C', Structure) > 0 THEN TailleChrono END, '|',
                          CASE WHEN CHARINDEX('G', Structure) > 0 THEN CodeGreffe END)
    FROM num.Compteur)
SELECT a.Type AS SerieA, b.Type AS SerieB, a.Annee, a.Jour, a.Forme, a.Chrono AS ChronoA, b.Chrono AS ChronoB,
       NumerosCommuns = CASE WHEN a.Chrono IS NULL OR b.Chrono IS NULL THEN 0
                             WHEN IIF(a.Chrono < b.Chrono, a.Chrono, b.Chrono) < IIF(a.Debut > b.Debut, a.Debut, b.Debut) THEN 0
                             ELSE IIF(a.Chrono < b.Chrono, a.Chrono, b.Chrono) - IIF(a.Debut > b.Debut, a.Debut, b.Debut) + 1 END
FROM F AS a
JOIN F AS b ON b.Forme = a.Forme AND b.Annee = a.Annee AND b.Jour = a.Jour AND b.Type > a.Type;
GO

-- NUM-017 CA02 : les séries à plage (lettres recommandées) qui approchent de la fin de leur plage.
CREATE VIEW num.PlageAEpuiser
AS
SELECT Type, Annee, Jour, Debut, Chrono, FinPlage,
       Restants = FinPlage - ISNULL(Chrono, Debut - 1),
       Seuil = ISNULL(AlertePlage, (FinPlage - Debut + 1) / 10)
FROM num.Compteur
WHERE FinPlage IS NOT NULL
  AND FinPlage - ISNULL(Chrono, Debut - 1) <= ISNULL(AlertePlage, (FinPlage - Debut + 1) / 10);
GO

-- NUM-013 CA02, CA03 : la liste des numéros manquants du registre lit le journal. Un numéro y figure avec
-- son dernier motif (repris de la V2 ou déclaré dans la V3), sauf s'il a été rendu (NUM-004).
CREATE VIEW num.NumeroManquant
AS
WITH M AS (
    SELECT j.Serie, j.Annee, j.Jour, j.Chrono, j.Numero, j.Explication AS Motif, j.Logiciel, j.Le,
           Rang = ROW_NUMBER() OVER (PARTITION BY j.Serie, j.Numero ORDER BY j.Id DESC)
    FROM num.Journal AS j
    WHERE j.Evenement = 'manquant')
SELECT Serie, Annee, Jour, Chrono, Numero, Motif, Logiciel, Le AS DeclareLe
FROM M
WHERE Rang = 1
  AND NOT EXISTS (SELECT 1 FROM num.Journal AS r
                  WHERE r.Evenement = 'restitution' AND r.Serie = M.Serie AND r.Numero = M.Numero);
GO

-- ============================================================================
-- num.Attribuer, redéfini (même signature, mêmes codes que NUM-001_compteurs.sql, deux refus de plus) :
--   -6 = année au-delà de l'année suivante (NUM-005 CA04, T05) : les compteurs d'années futures repris de
--        la V2 (JDCIA 2048, JDRFM 2109, RA1 2044 et 2188 dans RIG_DEV) ne servent plus, sauf réouverture ;
--   -5 = aussi « plage épuisée » : le chrono suivant dépasse FinPlage (NUM-017 CA02).
-- Tout le reste est le programme du lot 5, inchangé : voir NUM-001_compteurs.sql et
-- docs\reconstruction\numerotation-compteur.md.
-- ============================================================================
CREATE OR ALTER PROCEDURE num.Attribuer
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

    -- NUM-005 CA04, T05 : pas d'année au-delà de la suivante (la V2 en ouvrait : 2048, 2109, 2188).
    IF @Annee > YEAR(@Maintenant) + 1
    BEGIN
        SET @Message = N'Année refusée : ' + CAST(@Annee AS nvarchar(6)) + N' est au-delà de l''année suivante ('
                     + CAST(YEAR(@Maintenant) + 1 AS nvarchar(6)) + N') ; aucun numéro ' + @Serie
                     + N' n''a été donné. Un compteur d''année future repris de la V2 ne sert que si le référent le rouvre.';
        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Code, Logiciel, Compte, Poste, Explication)
        VALUES ('refus', @Type, @Serie, @Annee, NULL, -6, @Logiciel, @Compte, @Poste, @Message);
        RETURN -6;
    END;

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
            @Separateur varchar(5), @Taille smallint, @GreffeCompteur varchar(5), @FinPlage int, @Cree bit = 0;
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
               @GreffeCompteur = CodeGreffe, @FinPlage = FinPlage
        FROM num.Compteur WITH (UPDLOCK, ROWLOCK)
        WHERE Type = @Serie AND Annee = @Annee AND Jour = @Cle;

        IF @Id IS NULL
        BEGIN
            -- Nouvelle année ou nouveau jour : les réglages de la référence, et la valeur de départ.
            INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur,
                                 TailleChrono, CodeGreffe, Debut, Chrono, GestionRendus, Redirection,
                                 TableCible, ColonneCible, FinPlage, AlertePlage)
            SELECT TOP (1) Type, @Annee, @Cle, Libelle, Structure, CodageAnnee, Lettre, Separateur,
                   TailleChrono, CodeGreffe, Debut, NULL, GestionRendus, Redirection, TableCible, ColonneCible,
                   FinPlage, AlertePlage
            FROM num.Compteur WITH (NOLOCK)
            WHERE Type = @Serie
            ORDER BY CASE WHEN Annee = @Annee THEN 0 ELSE 1 END, Annee DESC, Jour DESC;
            SET @Cree = 1;
            SELECT @Id = Id, @Dernier = Chrono, @Debut = Debut, @Gestion = GestionRendus, @Structure = Structure,
                   @Codage = CodageAnnee, @Lettre = Lettre, @Separateur = Separateur, @Taille = TailleChrono,
                   @GreffeCompteur = CodeGreffe, @FinPlage = FinPlage
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
               OR (@FinPlage IS NOT NULL AND ISNULL(@Dernier + 1, @Debut) > @FinPlage)
            BEGIN
                IF @Entree = 0 ROLLBACK TRANSACTION; ELSE ROLLBACK TRANSACTION NumAttribuer;
                SET @Message = CASE WHEN LEN(CAST(ISNULL(@Dernier + 1, @Debut) AS varchar(11))) > @Taille
                    THEN N'Série pleine : le compteur ' + @Libelle + N' est à ' + CAST(@Dernier AS nvarchar(11))
                         + N', le suivant dépasse les ' + CAST(@Taille AS nvarchar(6)) + N' chiffres de son format.'
                    ELSE N'Plage épuisée : le compteur ' + @Libelle + N' est à ' + ISNULL(CAST(@Dernier AS nvarchar(11)), N'son début')
                         + N', sa plage s''arrête à ' + CAST(@FinPlage AS nvarchar(11))
                         + N' (NUM-017) ; saisir la nouvelle plage obtenue de La Poste.' END;
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
-- num.AttribuerOrdre : le numéro suivant d'une fiche (NUM-021 CA01) — l'ordre des mentions d'un dossier
-- que la V2 recalculait à partir de 1 (DossierRCS.cs:2136-2143, fiche #2440 : deux mentions « 1 »).
-- La V2 (programme remplacé, lot 7) et la V3 l'appellent : deux mentions du même dossier ont deux ordres.
-- Même règle pour l'ordre des établissements NQ d'un dossier (fiche « ETABLISSEMENT »), que la V2 calcule
-- par ISNULL(MAX(ETBLS_NUM_ORDRE), 1) + 1 (frmREQUI.cs:3140-3145) : son premier vaut 2 (@Premier).
-- ============================================================================
CREATE PROCEDURE num.AttribuerOrdre
    @Fiche   varchar(40),
    @Cle     int,
    @Ordre   int = NULL OUTPUT,
    @Premier int = 1                   -- l'ordre d'une fiche qui n'en a encore aucun
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Entree int = @@TRANCOUNT;
    DECLARE @Pris TABLE (Ordre int NOT NULL);
    SET @Ordre = NULL;
    IF @Fiche IS NULL OR @Cle IS NULL THROW 51000, N'num.AttribuerOrdre : @Fiche et @Cle sont obligatoires.', 1;
    BEGIN TRY
        IF @Entree = 0 BEGIN TRANSACTION; ELSE SAVE TRANSACTION NumOrdre;
        -- HOLDLOCK : la plage de la clé reste tenue jusqu'à la fin, une première demande n'est créée qu'une fois.
        UPDATE num.OrdreFiche WITH (UPDLOCK, HOLDLOCK)
        SET Dernier = Dernier + 1
        OUTPUT inserted.Dernier INTO @Pris (Ordre)
        WHERE Fiche = @Fiche AND Cle = @Cle;
        IF NOT EXISTS (SELECT 1 FROM @Pris)
            INSERT num.OrdreFiche (Fiche, Cle, Dernier) OUTPUT inserted.Dernier INTO @Pris (Ordre) VALUES (@Fiche, @Cle, ISNULL(@Premier, 1));
        SELECT @Ordre = Ordre FROM @Pris;
        IF @Entree = 0 COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @Entree = 0 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @Entree > 0 AND XACT_STATE() = 1 ROLLBACK TRANSACTION NumOrdre;
        SET @Ordre = NULL;
        THROW;
    END CATCH;
END;
GO

-- ============================================================================
-- num.DeclarerManquant : un numéro donné qui ne sera pas utilisé (acte abandonné…), avec son motif
-- (NUM-013 CA03). Il entre dans la liste des numéros manquants (num.NumeroManquant), sauf s'il est rendu.
-- Codes : 0 déclaré ; -2 type inconnu ; -3 illisible ; -4 jamais donné. Motif obligatoire (51000).
-- ============================================================================
CREATE PROCEDURE num.DeclarerManquant
    @Type     varchar(5),
    @Numero   varchar(40),
    @Motif    nvarchar(max),
    @Logiciel char(2)       = 'V3',
    @Compte   nvarchar(128) = NULL,
    @Poste    nvarchar(128) = NULL,
    @Message  nvarchar(400) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Message = NULL;
    SET @Motif = NULLIF(LTRIM(RTRIM(@Motif)), N'');
    IF @Motif IS NULL THROW 51000, N'num.DeclarerManquant : le motif est obligatoire (NUM-013).', 1;
    IF @Logiciel IS NULL OR @Logiciel NOT IN ('V2', 'V3') THROW 51000, N'num.DeclarerManquant : @Logiciel attend « V2 » ou « V3 ».', 1;
    SET @Numero = LTRIM(RTRIM(ISNULL(@Numero, '')));
    DECLARE @Serie varchar(5), @Redirection varchar(5);
    SELECT TOP (1) @Serie = Type, @Redirection = Redirection FROM num.Compteur WHERE Type = @Type ORDER BY Annee DESC, Jour DESC;
    IF @Serie IS NULL BEGIN SET @Message = N'Type de numéro inconnu : « ' + ISNULL(@Type, N'') + N' ».'; RETURN -2; END;
    IF ISNULL(@Redirection, '') <> '' SET @Serie = @Redirection;

    -- Le compteur qui l'a donné : même format, l'année lue dans le numéro, le jour d'une série par jour.
    DECLARE @Annee smallint, @Jour smallint, @Chrono int, @Dernier int, @Debut int;
    SELECT TOP (1) @Annee = c.Annee, @Jour = c.Jour, @Chrono = l.Chrono, @Dernier = c.Chrono, @Debut = c.Debut
    FROM num.Compteur AS c
    CROSS APPLY num.LireNumero(@Numero, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono,
                               DATALENGTH(ISNULL(c.CodeGreffe, ''))) AS l
    WHERE c.Type = @Serie
      AND c.Jour = CASE WHEN CHARINDEX('Q', c.Structure) > 0 THEN l.Jour ELSE 0 END
      AND (l.CodeAnnee = '' OR RIGHT(CAST(c.Annee AS varchar(5)), LEN(l.CodeAnnee)) = l.CodeAnnee)
    ORDER BY c.Annee DESC;
    IF @Chrono IS NULL
    BEGIN
        SET @Message = N'Numéro illisible : « ' + @Numero + N' » ne suit le format d''aucun compteur de la série ' + @Serie + N'.';
        RETURN -3;
    END;
    IF @Dernier IS NULL OR @Chrono > @Dernier OR @Chrono < @Debut
    BEGIN
        SET @Message = N'« ' + @Numero + N' » n''a jamais été donné : il ne peut pas manquer.';
        RETURN -4;
    END;
    INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Compte, Poste, Explication)
    VALUES ('manquant', @Type, @Serie, @Annee, @Jour, @Chrono, @Numero, 0, @Logiciel,
            ISNULL(@Compte, ORIGINAL_LOGIN()), ISNULL(@Poste, HOST_NAME()), @Motif);
    RETURN 0;
END;
GO

-- ============================================================================
-- num.Reprendre : le passage d'une base (NUM-005 T01-T03). Recopie la table des compteurs de la V2
-- (NUMERO_METIER) dans le compteur de la V3, et ce qui en dépend, dans UNE transaction :
--
--   1. la table de la V2 est bloquée (TABLOCKX, HOLDLOCK) jusqu'à la fin : GENERER_NUMERO et
--      RESTITUER_NUMERO attendent ; aucun numéro ne peut être donné entre la copie et le branchement
--      (NUM-005 CA01). Appelé dans la transaction du passage (lot 7 : remplacement des trois programmes),
--      il y travaille (point de sauvegarde) et le blocage dure jusqu'à SON commit ;
--   2. les compteurs : une ligne par (type, année, jour) ; deux lignes pour une même clé (ARRD 2014 jour 282,
--      ARRD 2018 jour 137, JGOP 2004 jour 1 dans RIG_DEV) : le plus grand chrono est gardé, l'autre ligne
--      va au journal et au référent (CA03) ; chrono 0 (« rien donné » de la V2) devient NULL ;
--      années futures copiées telles quelles et signalées (CA04) ;
--   3. les lignes « attribution erronée » : lisible, sous le compteur, série qui redonne -> num.NumeroRendu,
--      une fois même en double, « sans explication (reprise de la V2) » si elle n'en a pas ; illisible
--      (chrono ou jour à 0), au-delà du compteur, sans compteur, série qui ne redonne pas, en double ->
--      journal seulement (CA05) ;
--   4. NUMERO_METIER_SAVE_CDU -> num.Archive (CA06) ; NUMERO_CHRONO_MANQUANT -> journal « manquant »
--      (NUM-013 CA02) ; GREFFE_NUMERO_LIAISON -> num.CodeLiaison (NUM-019 T03) ; le plus grand ordre des
--      mentions de chaque dossier -> num.OrdreFiche (NUM-021 CA01) ;
--   5. le rapprochement (CA02) : pour chaque (type, année, jour) de la V2, même chrono, même format, même
--      réglage, même nombre de lignes et de lignes rendues ; mêmes comptes pour le reste. Un écart :
--      tout est annulé, code -1, écarts dans num.RepriseEcart ;
--   6. ouvre ce que la V3 tient en plus : la série des factures de la V3 (FACV3, préfixe propre, NUM-020
--      CA02) et le compteur des non-inscrits de la Banque de France (BDFNI, au plus grand numéro de
--      l'année, NUM-021 CA04).
--
-- Codes : 0 validée ; -1 écart (annulée) ; -10 la base V3 a déjà des compteurs ; -11 base V2 introuvable.
-- Une erreur SQL annule et LÈVE. Le rapport : num.Reprise (@IdReprise), num.RepriseEcart, num.Signalement.
-- ============================================================================
CREATE PROCEDURE num.Reprendre
    @BaseV2            sysname,
    -- NUM-020 CA02 : le préfixe de la série des factures de la V3. Il doit porter un caractère qui n'est pas
    -- un chiffre : la série de la V2 (« 26-022768 ») ne peut alors jamais l'écrire. Choix du référent
    -- Facturation, provisoire ici.
    @PrefixeFacturesV3 varchar(16) = 'V3-',
    @IdReprise         int           = NULL OUTPUT,
    @Message           nvarchar(400) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Debut datetime2(3) = SYSDATETIME(), @Entree int = @@TRANCOUNT,
            @AnneePassage smallint = YEAR(SYSDATETIME()), @Base nvarchar(300) = QUOTENAME(@BaseV2),
            @sql nvarchar(max), @JournalAvant bigint, @Code int;
    SELECT @IdReprise = NULL, @Message = NULL;

    IF @PrefixeFacturesV3 IS NULL OR @PrefixeFacturesV3 COLLATE Latin1_General_BIN NOT LIKE '%[^0-9]%'
        THROW 51000, N'num.Reprendre : le préfixe des factures de la V3 doit porter au moins un caractère qui n''est pas un chiffre (NUM-020 CA02).', 1;
    IF DB_ID(@BaseV2) IS NULL OR OBJECT_ID(@Base + N'.dbo.NUMERO_METIER') IS NULL
    BEGIN
        SET @Message = N'Base V2 introuvable, ou sans table NUMERO_METIER : « ' + ISNULL(@BaseV2, N'') + N' ».';
        INSERT num.Reprise (BaseV2, Debut, Fin, Code, Message) VALUES (ISNULL(@BaseV2, N''), @Debut, SYSDATETIME(), -11, @Message);
        SET @IdReprise = SCOPE_IDENTITY();
        RETURN -11;
    END;
    IF EXISTS (SELECT 1 FROM num.Compteur)
    BEGIN
        SET @Message = N'La base V3 a déjà des compteurs : une base ne se reprend qu''une fois (NUM-005). Rien n''a été fait.';
        INSERT num.Reprise (BaseV2, Debut, Fin, Code, Message) VALUES (@BaseV2, @Debut, SYSDATETIME(), -10, @Message);
        SET @IdReprise = SCOPE_IDENTITY();
        RETURN -10;
    END;

    -- La copie de la table de la V2 (même colonnes, collation de cette base pour les comparaisons).
    CREATE TABLE #V2 (
        Id int NOT NULL, Type varchar(5) COLLATE DATABASE_DEFAULT NOT NULL, Libelle varchar(50) COLLATE DATABASE_DEFAULT NULL,
        Structure varchar(16) COLLATE DATABASE_DEFAULT NOT NULL, CodeGreffe varchar(5) COLLATE DATABASE_DEFAULT NULL,
        Annee int NOT NULL, CodageAnnee tinyint NOT NULL, Lettre varchar(16) COLLATE DATABASE_DEFAULT NULL,
        TailleChrono smallint NOT NULL, Debut int NOT NULL, Chrono int NOT NULL, DateAttribution smalldatetime NULL,
        Quantieme smallint NULL, Gestion bit NOT NULL, Erronee bit NOT NULL, Explication varchar(50) COLLATE DATABASE_DEFAULT NULL,
        Redirection varchar(5) COLLATE DATABASE_DEFAULT NULL, Separateur varchar(5) COLLATE DATABASE_DEFAULT NULL,
        TableCible varchar(32) COLLATE DATABASE_DEFAULT NULL, ColonneCible varchar(32) COLLATE DATABASE_DEFAULT NULL,
        -- La clé du compteur dans la V3 : le jour pour une série dont le format porte « Q », sinon 0.
        JourCle AS (CASE WHEN CHARINDEX('Q', Structure) > 0 THEN ISNULL(CAST(Quantieme AS int), 0) ELSE 0 END)
    );
    DECLARE @Ecarts TABLE (Type varchar(5), Annee int, Jour int, Quoi varchar(40), V2 nvarchar(400), V3 nvarchar(400));
    DECLARE @Lignes int, @Compteurs int, @Rendus int, @Journalisees int, @Archivees int = 0, @Manquants int = 0,
            @Codes int = 0, @Ordres int = 0, @Attendu int;

    BEGIN TRY
        IF @Entree = 0 BEGIN TRANSACTION; ELSE SAVE TRANSACTION NumReprendre;
        INSERT num.Reprise (BaseV2, Debut) VALUES (@BaseV2, @Debut);
        SET @IdReprise = SCOPE_IDENTITY();
        SELECT @JournalAvant = ISNULL(MAX(Id), 0) FROM num.Journal;

        -- 1. Le blocage et la copie (CA01).
        SET @sql = N'INSERT #V2 (Id, Type, Libelle, Structure, CodeGreffe, Annee, CodageAnnee, Lettre, TailleChrono, Debut, Chrono,
                         DateAttribution, Quantieme, Gestion, Erronee, Explication, Redirection, Separateur, TableCible, ColonneCible)
                     SELECT NMMTR_ID_NMMTR, NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_STRUCTURE_NUMERO, NMMTR_CODE_GREFFE, NMMTR_ANNEE,
                         NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_TAILLE_CHRONO, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO,
                         NMMTR_DATE_ATTRIBUTION, NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_EXPLICATION,
                         NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR, NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE
                     FROM ' + @Base + N'.dbo.NUMERO_METIER WITH (TABLOCKX, HOLDLOCK);';
        EXEC sys.sp_executesql @sql;
        SET @Lignes = @@ROWCOUNT;
        IF EXISTS (SELECT 1 FROM #V2 WHERE Type IN ('FACV3', 'BDFNI'))
            THROW 51000, N'num.Reprendre : la V2 a déjà un type FACV3 ou BDFNI, que la V3 réserve à ses propres séries.', 1;

        -- 2. Les compteurs : une ligne par clé, le plus grand chrono (CA03).
        SELECT *, Rang = ROW_NUMBER() OVER (PARTITION BY Type, Annee, JourCle ORDER BY Chrono DESC, Id DESC)
        INTO #C FROM #V2 WHERE Erronee = 0;

        INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono,
                             CodeGreffe, Debut, Chrono, GestionRendus, Redirection, TableCible, ColonneCible,
                             DerniereAttribution, IdV2)
        SELECT Type, Annee, JourCle, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono,
               CodeGreffe, Debut, NULLIF(Chrono, 0), Gestion, Redirection, TableCible, ColonneCible,
               DateAttribution, Id
        FROM #C WHERE Rang = 1;
        SET @Compteurs = @@ROWCOUNT;

        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Code, Logiciel, Compte, Poste, Explication)
        SELECT 'reprise', d.Type, d.Type, d.Annee, d.JourCle, d.Chrono, 0, 'V2', ORIGINAL_LOGIN(), HOST_NAME(),
               CONCAT(N'compteur en double : la ligne V2 ', d.Id, N' (chrono ', d.Chrono, N') est écartée, la V3 garde la ligne ',
                      k.Id, N' (chrono ', k.Chrono, N'), le plus grand (NUM-005 CA03).')
        FROM #C AS d JOIN #C AS k ON k.Type = d.Type AND k.Annee = d.Annee AND k.JourCle = d.JourCle AND k.Rang = 1
        WHERE d.Rang > 1;

        INSERT num.Signalement (IdReprise, Sujet, Referent, Serie, Annee, Jour, Detail)
        SELECT @IdReprise, 'compteur en double', 'Socle', d.Type, d.Annee, d.JourCle,
               CONCAT(N'Deux compteurs ', d.Type, N' ', d.Annee, CASE WHEN d.JourCle > 0 THEN CONCAT(N' jour ', d.JourCle) END,
                      N' dans la V2 (lignes ', k.Id, N' à ', k.Chrono, N' et ', d.Id, N' à ', d.Chrono,
                      N') : la V3 garde ', k.Chrono, N'. Les numéros donnés deux fois sont relevés par num.ReleverHeritage.')
        FROM #C AS d JOIN #C AS k ON k.Type = d.Type AND k.Annee = d.Annee AND k.JourCle = d.JourCle AND k.Rang = 1
        WHERE d.Rang > 1;

        -- Années au-delà de l'année suivante (CA04) : copiées telles quelles, listées au référent.
        INSERT num.Signalement (IdReprise, Sujet, Referent, Serie, Annee, Jour, Valeur, Detail)
        SELECT @IdReprise, 'annee future', 'Socle', Type, Annee, JourCle, CAST(Chrono AS varchar(60)),
               CONCAT(N'Ligne V2 ', Id, CASE WHEN Erronee = 1 THEN N' (attribution erronée)' END, N' : ', Type, N' ', Annee,
                      N', chrono ', Chrono, N'. Reprise telle quelle ; la V3 refuse toute demande au-delà de ',
                      @AnneePassage + 1, N' (données d''essai possibles, H6).')
        FROM #V2 WHERE Annee > @AnneePassage + 1;

        -- 3. Les lignes « attribution erronée » (CA05).
        SELECT e.Id, e.Type, e.Annee, e.JourCle, e.Quantieme, e.Chrono, e.Explication,
               c.Id AS IdCompteur, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, c.CodeGreffe,
               Motif = CASE
                   WHEN e.Chrono <= 0 OR (CHARINDEX('Q', e.Structure) > 0 AND (e.Quantieme IS NULL OR e.Quantieme NOT BETWEEN 1 AND 366))
                       THEN N'illisible (chrono ou jour à 0), la V2 ne le redonnait jamais'
                   WHEN c.Id IS NULL THEN N'aucun compteur pour son année ou son jour'
                   WHEN c.Chrono IS NULL OR e.Chrono > c.Chrono OR e.Chrono < c.Debut THEN N'au-delà du compteur : jamais donné (NUM-004 CA04)'
                   WHEN CHARINDEX('Q', c.Structure) = 0 AND c.GestionRendus = 0 THEN N'série qui ne redonne pas ses numéros'
                   END,
               Rang = ROW_NUMBER() OVER (PARTITION BY e.Type, e.Annee, e.JourCle, e.Chrono ORDER BY e.Id)
        INTO #R
        FROM #V2 AS e
        LEFT JOIN num.Compteur AS c ON c.Type = e.Type AND c.Annee = e.Annee AND c.Jour = e.JourCle
        WHERE e.Erronee = 1;

        INSERT num.NumeroRendu (Serie, Annee, Jour, Chrono, Numero, Explication, Logiciel, IdV2)
        SELECT Type, Annee, JourCle, Chrono,
               num.Composer(Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, Annee, JourCle, Chrono),
               ISNULL(NULLIF(LTRIM(RTRIM(Explication)), ''), 'sans explication (reprise de la V2)'), 'V2', Id
        FROM #R WHERE Motif IS NULL AND Rang = 1;
        SET @Rendus = @@ROWCOUNT;

        INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Compte, Poste, Explication)
        SELECT 'reprise', Type, Type, Annee, JourCle, Chrono,
               CASE WHEN IdCompteur IS NOT NULL AND Chrono > 0
                    THEN num.Composer(Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, Annee, JourCle, Chrono) END,
               0, 'V2', ORIGINAL_LOGIN(), HOST_NAME(),
               CONCAT(N'rendu non redonnable : ligne « attribution erronée » V2 ', Id, N', ',
                      ISNULL(Motif, N'déjà rendue par une autre ligne (une seule fois, NUM-004 CA05)'),
                      N'. Explication V2 : ', ISNULL(NULLIF(LTRIM(RTRIM(Explication)), ''), N'(aucune)'), N'.')
        FROM #R WHERE Motif IS NOT NULL OR Rang > 1;
        SET @Journalisees = @@ROWCOUNT;

        -- 4. Ce qui accompagne les compteurs.
        IF OBJECT_ID(@Base + N'.dbo.NUMERO_METIER_SAVE_CDU') IS NOT NULL
        BEGIN
            SET @sql = N'INSERT num.Archive (Source, IdReprise, IdV2, Type, Libelle, Structure, CodeGreffe, Annee, CodageAnnee, Lettre,
                             TailleChrono, Debut, Chrono, DateAttribution, Quantieme, GestionRendus, Erronee, Explication,
                             Redirection, Separateur, TableCible, ColonneCible)
                         SELECT ''NUMERO_METIER_SAVE_CDU'', @Id, NMMTR_ID_NMMTR, NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_STRUCTURE_NUMERO,
                             NMMTR_CODE_GREFFE, NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_TAILLE_CHRONO,
                             NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_DATE_ATTRIBUTION, NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR,
                             NMMTR_ATTRIBUTION_ERRONEE, NMMTR_EXPLICATION, NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR,
                             NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE
                         FROM ' + @Base + N'.dbo.NUMERO_METIER_SAVE_CDU;
                         SET @n = @@ROWCOUNT;';
            EXEC sys.sp_executesql @sql, N'@Id int, @n int OUTPUT', @IdReprise, @Archivees OUTPUT;
        END;

        -- fix-ok: créée même sans table V2 à lire, car le rapprochement la compte : une base V2 sans
        -- NUMERO_CHRONO_MANQUANT faisait échouer la reprise sur « Invalid object name '#M' » (mesuré le 2026-11-21,
        -- banc NumerotationSeriesCheck, base V2 réduite à NUMERO_METIER).
        CREATE TABLE #M (Id int, Type varchar(5) COLLATE DATABASE_DEFAULT, Annee int, Numero varchar(40) COLLATE DATABASE_DEFAULT);
        IF OBJECT_ID(@Base + N'.dbo.NUMERO_CHRONO_MANQUANT') IS NOT NULL
        BEGIN
            SET @sql = N'INSERT #M SELECT NMCHM_ID_NMCHM, COALESCE(NULLIF(NMCHM_TYPE_METIER, ''''), NMCHM_TYPE), NMCHM_ANNEE,
                             LTRIM(RTRIM(NMCHM_NUMERO_CHRONO))
                         FROM ' + @Base + N'.dbo.NUMERO_CHRONO_MANQUANT;';
            EXEC sys.sp_executesql @sql;
            -- NUM-013 CA02 : au journal, jamais redonné (un registre chronologique ne redonne pas un numéro
            -- après les suivants). Lu avec le format du compteur de son année.
            INSERT num.Journal (Evenement, TypeDemande, Serie, Annee, Jour, Chrono, Numero, Code, Logiciel, Explication)
            SELECT 'manquant', ISNULL(m.Type, '?'), ISNULL(m.Type, '?'), m.Annee, l.Jour, l.Chrono, m.Numero, 0, 'V2',
                   N'manquant constaté par la V2, motif inconnu'
            FROM #M AS m
            OUTER APPLY (SELECT TOP (1) x.Jour, x.Chrono
                         FROM num.Compteur AS c
                         CROSS APPLY num.LireNumero(m.Numero, c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono,
                                                    DATALENGTH(ISNULL(c.CodeGreffe, ''))) AS x
                         WHERE c.Type = m.Type AND c.Annee = m.Annee) AS l;
            SET @Manquants = @@ROWCOUNT;
        END;

        IF OBJECT_ID(@Base + N'.dbo.GREFFE_NUMERO_LIAISON') IS NOT NULL
        BEGIN
            SET @sql = N'INSERT num.CodeLiaison (Code, IdReprise)
                         SELECT DISTINCT LTRIM(RTRIM(GRNLN_CODE)), @Id FROM ' + @Base + N'.dbo.GREFFE_NUMERO_LIAISON
                         WHERE LTRIM(RTRIM(GRNLN_CODE)) <> '''';
                         SET @n = @@ROWCOUNT;';
            EXEC sys.sp_executesql @sql, N'@Id int, @n int OUTPUT', @IdReprise, @Codes OUTPUT;
        END;

        IF OBJECT_ID(@Base + N'.dbo.MENTION_OBSERVATION') IS NOT NULL
        BEGIN
            SET @sql = N'INSERT num.OrdreFiche (Fiche, Cle, Dernier)
                         SELECT ''MENTION_OBSERVATION'', MNTOB_ID_DSRCS, MAX(ISNULL(MNTOB_NUM_ORDRE, 0))
                         FROM ' + @Base + N'.dbo.MENTION_OBSERVATION WHERE MNTOB_ID_DSRCS IS NOT NULL
                         GROUP BY MNTOB_ID_DSRCS;
                         SET @n = @@ROWCOUNT;';
            EXEC sys.sp_executesql @sql, N'@n int OUTPUT', @Ordres OUTPUT;
        END;
        IF COL_LENGTH(@Base + N'.dbo.ETABLISSEMENT', 'ETBLS_NUM_ORDRE') IS NOT NULL
        BEGIN
            -- Le périmètre de la V2 : établissements NQ non supprimés (frmREQUI.cs:3140-3142).
            DECLARE @OrdresEtab int = 0;
            SET @sql = N'INSERT num.OrdreFiche (Fiche, Cle, Dernier)
                         SELECT ''ETABLISSEMENT'', ETBLS_ID_DSRCS, MAX(ISNULL(ETBLS_NUM_ORDRE, 0))
                         FROM ' + @Base + N'.dbo.ETABLISSEMENT
                         WHERE ETBLS_ID_DSRCS IS NOT NULL AND ETBLS_SUPPRIME = 0 AND ETBLS_TYPE_ADRESSE = ''NQ''
                         GROUP BY ETBLS_ID_DSRCS;
                         SET @n = @@ROWCOUNT;';
            EXEC sys.sp_executesql @sql, N'@n int OUTPUT', @OrdresEtab OUTPUT;
            SET @Ordres += @OrdresEtab;
        END;

        -- 5. Le rapprochement (CA02), relu dans les tables de la V3.
        --    a. Chaque clé de la V2 a son compteur : le plus grand chrono, le format et le réglage de sa ligne.
        INSERT @Ecarts (Type, Annee, Jour, Quoi, V2, V3)
        SELECT k.Type, k.Annee, k.JourCle, 'compteur',
               CONCAT_WS('|', k.Chrono, k.Structure, k.CodageAnnee, k.Lettre, k.Separateur, k.TailleChrono, k.CodeGreffe,
                         k.Debut, k.Gestion, k.Redirection, k.TableCible, k.ColonneCible),
               CONCAT_WS('|', ISNULL(c.Chrono, 0), c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, c.CodeGreffe,
                         c.Debut, c.GestionRendus, c.Redirection, c.TableCible, c.ColonneCible)
        FROM #C AS k
        LEFT JOIN num.Compteur AS c ON c.Type = k.Type AND c.Annee = k.Annee AND c.Jour = k.JourCle
        WHERE k.Rang = 1
          AND (c.Id IS NULL
               OR CAST(CONCAT_WS('|', k.Chrono, k.Structure, k.CodageAnnee, k.Lettre, k.Separateur, k.TailleChrono, k.CodeGreffe,
                                 k.Debut, k.Gestion, k.Redirection, k.TableCible, k.ColonneCible) AS varbinary(400))
                  <> CAST(CONCAT_WS('|', ISNULL(c.Chrono, 0), c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono,
                                    c.CodeGreffe, c.Debut, c.GestionRendus, c.Redirection, c.TableCible, c.ColonneCible) AS varbinary(400)));

        --    b. Même nombre de lignes de compteur par clé : 1 gardée + les doublons au journal.
        INSERT @Ecarts (Type, Annee, Jour, Quoi, V2, V3)
        SELECT v.Type, v.Annee, v.JourCle, 'lignes de compteur', CAST(v.n AS nvarchar(12)), CAST(ISNULL(w.n, 0) AS nvarchar(12))
        FROM (SELECT Type, Annee, JourCle, COUNT(*) AS n FROM #C GROUP BY Type, Annee, JourCle) AS v
        LEFT JOIN (SELECT c.Type, c.Annee, c.Jour, 1 + (SELECT COUNT(*) FROM num.Journal AS j
                                                         WHERE j.Id > @JournalAvant AND j.Evenement = 'reprise'
                                                           AND j.Explication LIKE N'compteur en double%'
                                                           AND j.Serie = c.Type AND j.Annee = c.Annee AND j.Jour = c.Jour) AS n
                   FROM num.Compteur AS c) AS w ON w.Type = v.Type AND w.Annee = v.Annee AND w.Jour = v.JourCle
        WHERE ISNULL(w.n, 0) <> v.n;

        --    c. Même nombre de lignes rendues par clé : redonnables + journalisées.
        INSERT @Ecarts (Type, Annee, Jour, Quoi, V2, V3)
        SELECT v.Type, v.Annee, v.JourCle, 'lignes rendues', CAST(v.n AS nvarchar(12)),
               CAST(ISNULL(r.n, 0) + ISNULL(j.n, 0) AS nvarchar(12))
        FROM (SELECT Type, Annee, JourCle, COUNT(*) AS n FROM #V2 WHERE Erronee = 1 GROUP BY Type, Annee, JourCle) AS v
        LEFT JOIN (SELECT Serie, Annee, Jour, COUNT(*) AS n FROM num.NumeroRendu WHERE IdV2 IS NOT NULL GROUP BY Serie, Annee, Jour) AS r
               ON r.Serie = v.Type AND r.Annee = v.Annee AND r.Jour = v.JourCle
        LEFT JOIN (SELECT Serie, Annee, Jour, COUNT(*) AS n FROM num.Journal
                   WHERE Id > @JournalAvant AND Evenement = 'reprise' AND Explication LIKE N'rendu non redonnable%'
                   GROUP BY Serie, Annee, Jour) AS j
               ON j.Serie = v.Type AND j.Annee = v.Annee AND j.Jour = v.JourCle
        WHERE ISNULL(r.n, 0) + ISNULL(j.n, 0) <> v.n;

        --    d. Aucun compteur de la V3 sans ligne de la V2, et les totaux.
        INSERT @Ecarts (Type, Annee, Jour, Quoi, V2, V3)
        SELECT c.Type, c.Annee, c.Jour, 'compteur sans ligne V2', NULL, CAST(ISNULL(c.Chrono, 0) AS nvarchar(12))
        FROM num.Compteur AS c
        WHERE NOT EXISTS (SELECT 1 FROM #V2 AS v WHERE v.Erronee = 0 AND v.Type = c.Type AND v.Annee = c.Annee AND v.JourCle = c.Jour);

        SELECT @Attendu = (SELECT COUNT(*) FROM num.Compteur)
                        + (SELECT COUNT(*) FROM num.NumeroRendu WHERE IdV2 IS NOT NULL)
                        + (SELECT COUNT(*) FROM num.Journal WHERE Id > @JournalAvant AND Evenement = 'reprise');
        IF @Attendu <> @Lignes
            INSERT @Ecarts (Quoi, V2, V3) VALUES ('lignes au total', CAST(@Lignes AS nvarchar(12)), CAST(@Attendu AS nvarchar(12)));

        IF OBJECT_ID(@Base + N'.dbo.NUMERO_METIER_SAVE_CDU') IS NOT NULL
        BEGIN
            SET @sql = N'SELECT @n = COUNT(*) FROM ' + @Base + N'.dbo.NUMERO_METIER_SAVE_CDU;';
            EXEC sys.sp_executesql @sql, N'@n int OUTPUT', @Attendu OUTPUT;
            IF @Attendu <> (SELECT COUNT(*) FROM num.Archive WHERE IdReprise = @IdReprise)
                INSERT @Ecarts (Quoi, V2, V3) VALUES ('archive', CAST(@Attendu AS nvarchar(12)), CAST(@Archivees AS nvarchar(12)));
        END;
        IF (SELECT COUNT(*) FROM #M) <> (SELECT COUNT(*) FROM num.Journal WHERE Id > @JournalAvant AND Evenement = 'manquant')
            INSERT @Ecarts (Quoi, V2, V3)
            VALUES ('numeros manquants', (SELECT CAST(COUNT(*) AS nvarchar(12)) FROM #M), CAST(@Manquants AS nvarchar(12)));

        IF EXISTS (SELECT 1 FROM @Ecarts)
        BEGIN
            -- Tout est annulé : la base reste sur ses anciens programmes (CA02).
            IF @Entree = 0 ROLLBACK TRANSACTION; ELSE ROLLBACK TRANSACTION NumReprendre;
            SET @Message = CONCAT(N'Reprise annulée : ', (SELECT COUNT(*) FROM @Ecarts), N' écart(s) au rapprochement avec la V2 ; ',
                                  N'la base V2 garde ses compteurs et ses programmes. Détail : num.RepriseEcart.');
            INSERT num.Reprise (BaseV2, Debut, Fin, DureeMs, Code, LignesV2, Ecarts, Message)
            VALUES (@BaseV2, @Debut, SYSDATETIME(), DATEDIFF(millisecond, @Debut, SYSDATETIME()), -1, @Lignes,
                    (SELECT COUNT(*) FROM @Ecarts), @Message);
            SET @IdReprise = SCOPE_IDENTITY();
            INSERT num.RepriseEcart (IdReprise, Type, Annee, Jour, Quoi, V2, V3)
            SELECT @IdReprise, Type, Annee, Jour, Quoi, V2, V3 FROM @Ecarts;
            RETURN -1;
        END;

        -- 6. Ce que la V3 tient en plus de la V2, ouvert pour l'année du passage.
        INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono,
                             CodeGreffe, Debut, Chrono, GestionRendus, Redirection, TableCible, ColonneCible)
        VALUES ('FACV3', @AnneePassage, 0, 'Factures de la V3 (NUM-020)', 'LA-C', 2, @PrefixeFacturesV3, NULL, 6,
                NULL, 1, NULL, 0, NULL, 'FACTURE', 'NUMERO_FACTURE');

        -- NUM-021 CA04 : la V2 calculait « <année>F » + 5 chiffres par le plus grand numéro de l'année dans
        -- ses avis à la Banque de France (EdiBanqueFranceAvisJUDEdiSortant.cs:105-114) ; la V3 le tient.
        -- fix-ok: sp_executesql refuse une expression en paramètre (« Incorrect syntax near '@AnneePassage' », lot 13,
        -- mesuré au premier passage du migrateur le 2026-11-21) : le motif passe par une variable. Même passage :
        -- un DECLARE mêlant scalaire et table (@Pris de num.AttribuerOrdre) refusé, séparé en deux.
        DECLARE @MaxBdf varchar(60) = NULL, @ChronoBdf int = NULL, @MotifBdf varchar(10) = CONCAT(@AnneePassage, 'F%');
        IF OBJECT_ID(@Base + N'.dbo.EDI_BDF') IS NOT NULL
        BEGIN
            SET @sql = N'SELECT @m = MAX(EDIBDF_NUM_GESTION) FROM ' + @Base + N'.dbo.EDI_BDF WHERE EDIBDF_NUM_GESTION LIKE @motif;';
            EXEC sys.sp_executesql @sql, N'@m varchar(60) OUTPUT, @motif varchar(10)', @MaxBdf OUTPUT, @MotifBdf;
            SET @ChronoBdf = TRY_CAST(SUBSTRING(@MaxBdf, 6, 5) AS int);
            IF @MaxBdf IS NOT NULL AND @ChronoBdf IS NULL
                THROW 51000, N'num.Reprendre : le plus grand numéro de gestion des non-inscrits de l''année (EDI_BDF) ne se lit pas comme « <année>F » + 5 chiffres.', 1;
        END;
        INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono,
                             CodeGreffe, Debut, Chrono, GestionRendus, Redirection, TableCible, ColonneCible)
        VALUES ('BDFNI', @AnneePassage, 0, 'Banque de France : non-inscrits (NUM-021)', 'ALC', 4, 'F', NULL, 5,
                NULL, 1, NULLIF(@ChronoBdf, 0), 0, NULL, 'EDI_BDF', 'NUM_GESTION');

        UPDATE num.Reprise
        SET Fin = SYSDATETIME(), DureeMs = DATEDIFF(millisecond, @Debut, SYSDATETIME()), Code = 0, LignesV2 = @Lignes,
            Compteurs = @Compteurs, Rendus = @Rendus, Journalisees = @Journalisees, Archivees = @Archivees,
            Manquants = @Manquants, CodesLiaison = @Codes, OrdresFiche = @Ordres, Ecarts = 0,
            Message = CONCAT(N'Reprise validée : ', @Lignes, N' lignes de la V2, ', @Compteurs, N' compteurs, ', @Rendus,
                             N' numéros rendus redonnables, ', @Journalisees, N' lignes au journal seulement, zéro écart.')
        WHERE Id = @IdReprise;
        SELECT @Message = Message FROM num.Reprise WHERE Id = @IdReprise;

        IF @Entree = 0 COMMIT TRANSACTION;
        RETURN 0;
    END TRY
    BEGIN CATCH
        DECLARE @Erreur nvarchar(400) = LEFT(ERROR_MESSAGE(), 400);
        IF @Entree = 0 AND XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        ELSE IF @Entree > 0 AND XACT_STATE() = 1 ROLLBACK TRANSACTION NumReprendre;
        IF XACT_STATE() <> -1
        BEGIN
            INSERT num.Reprise (BaseV2, Debut, Fin, Code, LignesV2, Message)
            VALUES (@BaseV2, @Debut, SYSDATETIME(), -99, @Lignes, N'Reprise annulée sur erreur : ' + @Erreur);
            SET @IdReprise = SCOPE_IDENTITY();
        END;
        THROW;
    END CATCH;
END;
GO

-- ============================================================================
-- num.ReleverHeritage : après num.Reprendre, en LECTURE dans la base V2 (hors du blocage), les valeurs
-- héritées que la V3 doit accepter et ce que le référent doit voir :
--   - rôles portés par plusieurs instances (NUM-009 CA03 : 2004IP00330, 2009JC13469 dans RIG_DEV) ;
--   - numéros de gestion sans année (NUM-012 CA02 : AP… ; tous les formats RG* commencent par l'année) ;
--   - factures qu'aucun format de la série FACTU ne peut écrire (NUM-020 CA04 : ASTRA-AF…) ;
--   - numéros d'un jour aux compteurs en double, portés deux fois dans leur table (NUM-005 CA03) ;
--   - paires de séries de même forme qui ont déjà donné les mêmes numéros (formes partagées).
-- Une table ou une colonne absente de la base V2 est sautée. Rend le nombre de signalements.
-- ============================================================================
CREATE PROCEDURE num.ReleverHeritage
    @BaseV2    sysname,
    @IdReprise int = NULL,
    @Signales  int = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Base nvarchar(300) = QUOTENAME(@BaseV2), @sql nvarchar(max), @Avant int;
    SELECT @Avant = ISNULL(MAX(Id), 0) FROM num.Signalement;
    IF DB_ID(@BaseV2) IS NULL THROW 51000, N'num.ReleverHeritage : base V2 introuvable.', 1;
    CREATE TABLE #H (Fiche varchar(40) COLLATE DATABASE_DEFAULT, Colonne varchar(40) COLLATE DATABASE_DEFAULT,
                     Valeur varchar(60) COLLATE DATABASE_DEFAULT, Porteurs int);

    -- Rôles en double (NUM-009 CA03).
    IF COL_LENGTH(@Base + N'.dbo.INSTANCE', 'INSTN_NUM_ROLE') IS NOT NULL
    BEGIN
        SET @sql = N'INSERT #H SELECT ''INSTANCE'', ''NUM_ROLE'', INSTN_NUM_ROLE, COUNT(*) FROM ' + @Base + N'.dbo.INSTANCE
                     WHERE INSTN_NUM_ROLE IS NOT NULL AND INSTN_NUM_ROLE <> '''' GROUP BY INSTN_NUM_ROLE HAVING COUNT(*) > 1;';
        EXEC sys.sp_executesql @sql;
        INSERT num.ValeurHeritee (Fiche, Colonne, Valeur, Motif, Porteurs, IdReprise)
        SELECT h.Fiche, h.Colonne, h.Valeur, 'doublon herite', h.Porteurs, @IdReprise FROM #H AS h
        WHERE h.Fiche = 'INSTANCE' AND NOT EXISTS (SELECT 1 FROM num.ValeurHeritee AS v
                                                   WHERE v.Fiche = h.Fiche AND v.Colonne = h.Colonne AND v.Valeur = h.Valeur);
        INSERT num.Signalement (IdReprise, Sujet, Referent, Valeur, Detail)
        SELECT @IdReprise, 'doublon herite', 'Judiciaire', Valeur,
               CONCAT(N'Numéro de rôle porté par ', Porteurs, N' instances dans la V2 : repris tel quel, accepté comme doublon hérité ; ',
                      N'le compteur, déjà au-delà, ne le redonne jamais.')
        FROM #H WHERE Fiche = 'INSTANCE';
    END;

    -- Numéros de gestion sans année (NUM-012 CA02).
    IF COL_LENGTH(@Base + N'.dbo.DOSSIER_RCS', 'DSSRC_NUM_GESTION') IS NOT NULL
    BEGIN
        SET @sql = N'INSERT #H SELECT ''DOSSIER_RCS'', ''NUM_GESTION'', DSSRC_NUM_GESTION, COUNT(*) FROM ' + @Base + N'.dbo.DOSSIER_RCS
                     WHERE DSSRC_NUM_GESTION <> '''' AND DSSRC_NUM_GESTION NOT LIKE ''[12][0-9][0-9][0-9]%'' GROUP BY DSSRC_NUM_GESTION;';
        EXEC sys.sp_executesql @sql;
        INSERT num.ValeurHeritee (Fiche, Colonne, Valeur, Motif, Porteurs, IdReprise)
        SELECT h.Fiche, h.Colonne, h.Valeur, 'hors format', h.Porteurs, @IdReprise FROM #H AS h
        WHERE h.Fiche = 'DOSSIER_RCS' AND NOT EXISTS (SELECT 1 FROM num.ValeurHeritee AS v
                                                      WHERE v.Fiche = h.Fiche AND v.Colonne = h.Colonne AND v.Valeur = h.Valeur);
        INSERT num.Signalement (IdReprise, Sujet, Referent, Valeur, Detail)
        SELECT @IdReprise, 'numero hors format', 'RCS', Valeur,
               N'Numéro de gestion sans année : aucun compteur ne l''a produit, aucune série n''est touchée ; la V3 l''accepte comme valeur héritée et ne compose jamais ce format.'
        FROM #H WHERE Fiche = 'DOSSIER_RCS';
    END;

    -- Factures hors du format de la série FACTU (NUM-020 CA04).
    IF COL_LENGTH(@Base + N'.dbo.FACTURE', 'FACTU_NUMERO_FACTURE') IS NOT NULL AND EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'FACTU')
    BEGIN
        DECLARE @Formats nvarchar(max) = (
            SELECT STRING_AGG(N'FACTU_NUMERO_FACTURE NOT LIKE ''' + REPLACE(m.Motif, '''', '''''') + N'''', N' AND ')
            FROM (SELECT DISTINCT num.MotifLike(Structure, CodageAnnee, Lettre, Separateur, TailleChrono,
                                                DATALENGTH(ISNULL(CodeGreffe, ''))) AS Motif
                  FROM num.Compteur WHERE Type = 'FACTU') AS m);
        SET @sql = N'INSERT #H SELECT ''FACTURE'', ''NUMERO_FACTURE'', FACTU_NUMERO_FACTURE, COUNT(*) FROM ' + @Base + N'.dbo.FACTURE
                     WHERE FACTU_NUMERO_FACTURE <> '''' AND ' + @Formats + N' GROUP BY FACTU_NUMERO_FACTURE;';
        EXEC sys.sp_executesql @sql;
        INSERT num.ValeurHeritee (Fiche, Colonne, Valeur, Motif, Porteurs, IdReprise)
        SELECT h.Fiche, h.Colonne, h.Valeur, 'hors format', h.Porteurs, @IdReprise FROM #H AS h
        WHERE h.Fiche = 'FACTURE' AND NOT EXISTS (SELECT 1 FROM num.ValeurHeritee AS v
                                                  WHERE v.Fiche = h.Fiche AND v.Colonne = h.Colonne AND v.Valeur = h.Valeur);
        INSERT num.Signalement (IdReprise, Sujet, Referent, Serie, Valeur, Detail)
        SELECT @IdReprise, 'numero hors format', 'Facturation', 'FACTU', Valeur,
               N'Facture hors du format de la série FACTU : reprise telle quelle, elle n''entre dans la continuité d''aucune série ; sa chaîne signée reste dans la V2 (Q6).'
        FROM #H WHERE Fiche = 'FACTURE';
    END;

    -- Compteurs en double (NUM-005 CA03) : les numéros que les deux lignes ont donnés (de Debut au plus
    -- petit des deux chronos), cherchés dans la table qu'ils numérotent. La V2 ne renseigne pas toujours sa
    -- table cible : ARRD numérote ARRIVEE.ARRV_NUM_ARRIVEE (Arrivee.cs:34).
    DECLARE @Serie varchar(5), @Annee int, @Jour int, @Haut int, @Table sysname, @Colonne sysname, @Vrai sysname;
    DECLARE doublons CURSOR LOCAL FAST_FORWARD FOR
        SELECT c.Type, c.Annee, c.Jour, IIF(ISNULL(c.Chrono, 0) < ISNULL(j.Chrono, 0), ISNULL(c.Chrono, 0), ISNULL(j.Chrono, 0)),
               COALESCE(c.TableCible, CASE c.Type WHEN 'ARRD' THEN 'ARRIVEE' END),
               COALESCE(c.ColonneCible, CASE c.Type WHEN 'ARRD' THEN 'NUM_ARRIVEE' END)
        FROM num.Journal AS j
        JOIN num.Compteur AS c ON c.Type = j.Serie AND c.Annee = j.Annee AND c.Jour = j.Jour
        WHERE j.Evenement = 'reprise' AND j.Explication LIKE N'compteur en double%';
    OPEN doublons;
    FETCH NEXT FROM doublons INTO @Serie, @Annee, @Jour, @Haut, @Table, @Colonne;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- La colonne réelle : celle de la table dont le nom finit par « _<attribut> » (INSTN_NUM_ROLE).
        SET @Vrai = NULL;
        IF @Table IS NOT NULL AND @Colonne IS NOT NULL AND OBJECT_ID(@Base + N'.dbo.' + QUOTENAME(@Table)) IS NOT NULL
        BEGIN
            SET @sql = N'SELECT TOP (1) @v = name FROM ' + @Base + N'.sys.columns
                         WHERE object_id = OBJECT_ID(@t) AND (name = @c OR name LIKE ''%[_]'' + @c) ORDER BY LEN(name);';
            DECLARE @NomComplet nvarchar(600) = @Base + N'.dbo.' + QUOTENAME(@Table);
            EXEC sys.sp_executesql @sql, N'@v sysname OUTPUT, @t nvarchar(600), @c sysname', @Vrai OUTPUT, @NomComplet, @Colonne;
        END;
        IF @Vrai IS NOT NULL AND @Haut > 0
        BEGIN
            CREATE TABLE #N (Numero varchar(40) COLLATE DATABASE_DEFAULT PRIMARY KEY);
            INSERT #N (Numero)
            SELECT DISTINCT num.Composer(c.Structure, c.CodageAnnee, c.Lettre, c.Separateur, c.TailleChrono, c.CodeGreffe, c.Annee, c.Jour, n.Chrono)
            FROM num.Compteur AS c
            CROSS APPLY (SELECT TOP (@Haut - c.Debut + 1) c.Debut - 1 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS Chrono
                         FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS n
            WHERE c.Type = @Serie AND c.Annee = @Annee AND c.Jour = @Jour AND @Haut >= c.Debut;
            SET @sql = N'INSERT num.Signalement (IdReprise, Sujet, Referent, Serie, Annee, Jour, Valeur, Detail)
                         SELECT @Id, ''numero porte deux fois'', ''Socle'', @s, @a, @j, t.' + QUOTENAME(@Vrai) + N',
                                CONCAT(N''Porté par '', COUNT(*), N'' fiches de '', @tab, N'' : donné par les deux compteurs '', @s, N'' du même jour dans la V2.'')
                         FROM ' + @Base + N'.dbo.' + QUOTENAME(@Table) + N' AS t
                         JOIN #N AS n ON n.Numero = t.' + QUOTENAME(@Vrai) + N'
                         GROUP BY t.' + QUOTENAME(@Vrai) + N' HAVING COUNT(*) > 1;';
            EXEC sys.sp_executesql @sql, N'@Id int, @s varchar(5), @a int, @j int, @tab sysname', @IdReprise, @Serie, @Annee, @Jour, @Table;
            DROP TABLE #N;
        END;
        FETCH NEXT FROM doublons INTO @Serie, @Annee, @Jour, @Haut, @Table, @Colonne;
    END;
    CLOSE doublons;
    DEALLOCATE doublons;

    -- Formes partagées : une ligne par paire de séries qui a déjà donné des numéros identiques.
    INSERT num.Signalement (IdReprise, Sujet, Referent, Serie, Valeur, Detail)
    SELECT @IdReprise, 'forme partagee', 'Socle', SerieA, SerieB,
           CONCAT(SerieA, N' et ', SerieB, N' écrivent la même forme de numéro : ', SUM(NumerosCommuns),
                  N' numéros identiques déjà donnés par les deux (', COUNT(*), N' compteurs). La V3 identifie un numéro par sa série et sa valeur.')
    FROM num.FormePartagee
    WHERE NumerosCommuns > 0
    GROUP BY SerieA, SerieB;

    SELECT @Signales = COUNT(*) FROM num.Signalement WHERE Id > @Avant;
    RETURN 0;
END;
GO

-- Les droits (NUM-001 CA01) : les logiciels qui numérotent lisent ces tables et appellent les deux
-- nouveaux programmes ; ils n'écrivent rien en direct. La reprise et le relevé sont des gestes du
-- passage (équipe d'Amitel), pas des logiciels : aucun droit à num_demandeur.
GRANT EXECUTE ON OBJECT::num.AttribuerOrdre TO num_demandeur;
GRANT EXECUTE ON OBJECT::num.DeclarerManquant TO num_demandeur;
GRANT EXECUTE ON OBJECT::num.EstHeritee TO num_demandeur;
GRANT SELECT ON OBJECT::num.Reprise TO num_demandeur;
GRANT SELECT ON OBJECT::num.Signalement TO num_demandeur;
GRANT SELECT ON OBJECT::num.ValeurHeritee TO num_demandeur;
GRANT SELECT ON OBJECT::num.Archive TO num_demandeur;
GRANT SELECT ON OBJECT::num.CodeLiaison TO num_demandeur;
GRANT SELECT ON OBJECT::num.OrdreFiche TO num_demandeur;
GRANT SELECT ON OBJECT::num.FormePartagee TO num_demandeur;
GRANT SELECT ON OBJECT::num.PlageAEpuiser TO num_demandeur;
GRANT SELECT ON OBJECT::num.NumeroManquant TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.Reprise TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.RepriseEcart TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.Signalement TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.ValeurHeritee TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.Archive TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.CodeLiaison TO num_demandeur;
DENY INSERT, UPDATE, DELETE ON OBJECT::num.OrdreFiche TO num_demandeur;
