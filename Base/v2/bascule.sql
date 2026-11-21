-- ============================================================================================
-- BASCULE DE LA NUMÉROTATION D'UN GREFFE : la V2 demande ses numéros au compteur de la V3
-- (lot 7 ; NUM-002, NUM-003, NUM-006, NUM-007, NUM-022 ; 2026-11-21).
--
--   cd Base\v2
--   sqlcmd -S <instance> -E -f 65001 -b -i bascule.sql ^
--          -v BaseV2=RIG_AMIENS BaseV3=RIGV3_AMIENS CodeGreffe=8002 Reprise=oui FinBeta=2027-06-30 ^
--             SessionEchecs=rigv3_numerotation_echecs
--
-- Variables (les noms viennent du catalogue des greffes, soc.Greffe, SOC-007) :
--   BaseV2, BaseV3   les deux bases du greffe, sur la MÊME instance (Q4)
--   CodeGreffe       écrit dans les messages d'erreur (NUM-007)
--   Reprise          « oui » : la reprise des compteurs (num.Reprendre, NUM-005) est jouée dans la transaction
--                    de la bascule -- personne ne numérote entre les deux ; « faite » : jouée avant
--                    (la bascule vérifie alors que rien n'a bougé depuis)
--   FinBeta          AAAA-MM-JJ[THH:MI:SS] : jusque-là le retour arrière est possible et le corps
--                    d'origine des trois programmes est gardé (NUM-022) ; « non » : greffe hors bêta,
--                    rien n'est gardé, pas de retour
--   SessionEchecs    la session d'événements de l'instance qui compte les échecs (NUM-007 CA05)
-- « :r » lit les fichiers par rapport au dossier courant : lancer depuis Base\v2.
--
-- À jouer la V2 du greffe fermée aux saisies. Dans UNE transaction, NUMERO_METIER et les compteurs de la
-- V3 verrouillés en exclusif :
--   1. contrôles : deux bases sur l'instance, même collation, compteur de la V3 en place, programmes de
--      la V2 d'origine ; avec Reprise=oui, la reprise des compteurs (num.Reprendre, NUM-005) dans cette
--      transaction (NUM-002 T03) ; puis chaque compteur de la V2 égal à celui de la V3 (num.EcartsV2) --
--      sinon REFUS, erreur 51020, rien n'est changé, reprise comprise ;
--   2. côté V3 : la recopie vers NUMERO_METIER (pont-v3.sql) ;
--   3. côté V2 : schéma rigv3 (pont-v2.sql), fiche du pont, corps d'origine gardé si bêta, puis les
--      trois programmes remplacés (GENERER_NUMERO.sql, CHERCHER_NUMERO.sql, RESTITUER_NUMERO.sql).
-- Puis, hors transaction : la session d'événements qui compte les échecs de numérotation de l'instance et,
-- avec Reprise=oui, le relevé des valeurs héritées (num.ReleverHeritage, lot 6).
-- NUMERO_METIER n'est pas touchée : elle reste en place et intacte (NUM-006 T03).
-- Retour arrière pendant la bêta : retour.sql. Mode d'emploi : docs\reconstruction\numerotation-v2.md.
-- ============================================================================================
:on error exit
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

-- 1. Contrôles qui ne demandent aucun verrou.
-- Le fichier (et ceux qu'il inclut) doit avoir été lu en UTF-8 : sinon les messages des programmes, dont
-- celui de NUM-007 qui doit être exact, seraient écrits abîmés. sqlcmd lit un fichier non Unicode dans la
-- page de code du poste si on ne lui dit pas « -f 65001 » (doc Microsoft de sqlcmd, option -f).
IF UNICODE(N'é') <> 233
    THROW 51020, N'Bascule refusée : le script n''a pas été lu en UTF-8 ; relancer sqlcmd avec -f 65001.', 1;
IF DB_ID(N'$(BaseV2)') IS NULL OR DB_ID(N'$(BaseV3)') IS NULL
    THROW 51020, N'Bascule refusée : la base V2 « $(BaseV2) » ou la base V3 « $(BaseV3) » n''est pas sur cette instance (les deux bases d''un greffe sont sur la même, Q4).', 1;
IF CAST(DATABASEPROPERTYEX(N'$(BaseV2)', 'Collation') AS sysname) <> CAST(DATABASEPROPERTYEX(N'$(BaseV3)', 'Collation') AS sysname)
    THROW 51020, N'Bascule refusée : $(BaseV2) et $(BaseV3) n''ont pas la même collation ; la recopie compare leurs textes.', 1;
IF LTRIM(N'$(CodeGreffe)') = N''
    THROW 51020, N'Bascule refusée : CodeGreffe est vide.', 1;
IF N'$(FinBeta)' <> N'non' AND TRY_CONVERT(datetime2(0), N'$(FinBeta)', 126) IS NULL
    THROW 51020, N'Bascule refusée : FinBeta « $(FinBeta) » n''est ni une date AAAA-MM-JJ[THH:MI:SS] ni « non ».', 1;
GO

BEGIN TRANSACTION;
GO

USE [$(BaseV2)];
DECLARE @n int;
SELECT @n = COUNT(*) FROM dbo.NUMERO_METIER WITH (TABLOCKX, HOLDLOCK);
IF OBJECT_ID(N'dbo.GENERER_NUMERO', N'P') IS NULL OR OBJECT_ID(N'dbo.CHERCHER_NUMERO', N'P') IS NULL
   OR OBJECT_ID(N'dbo.RESTITUER_NUMERO', N'P') IS NULL
    THROW 51020, N'Bascule refusée : GENERER_NUMERO, CHERCHER_NUMERO ou RESTITUER_NUMERO manque dans $(BaseV2).', 1;
IF OBJECT_DEFINITION(OBJECT_ID(N'dbo.GENERER_NUMERO')) LIKE N'%rigv3.%'
   OR OBJECT_DEFINITION(OBJECT_ID(N'dbo.CHERCHER_NUMERO')) LIKE N'%rigv3.%'
   OR OBJECT_DEFINITION(OBJECT_ID(N'dbo.RESTITUER_NUMERO')) LIKE N'%rigv3.%'
    THROW 51020, N'Bascule refusée : les programmes de $(BaseV2) demandent déjà leurs numéros à la V3.', 1;
GO

USE [$(BaseV3)];
DECLARE @n int, @rc int, @IdReprise int, @MessageReprise nvarchar(400), @m nvarchar(2048);
IF OBJECT_ID(N'num.Attribuer', N'P') IS NULL OR OBJECT_ID(N'num.Restituer', N'P') IS NULL
    THROW 51020, N'Bascule refusée : num.Attribuer ou num.Restituer manque dans $(BaseV3) (migration NUM-001 non jouée).', 1;
SELECT @n = COUNT(*) FROM num.Compteur WITH (TABLOCKX, HOLDLOCK);
-- La reprise des compteurs dans cette même transaction (NUM-002 T03) : num.Reprendre du lot 6 (NUM-005)
-- travaille dans la transaction de l'appelant. « faite » : jouée avant, la V2 fermée aux saisies depuis.
IF N'$(Reprise)' = N'oui'
BEGIN
    IF OBJECT_ID(N'num.Reprendre', N'P') IS NULL
        THROW 51020, N'Bascule refusée : Reprise=oui, mais num.Reprendre manque dans $(BaseV3) (migration NUM-005 non jouée).', 1;
    EXEC @rc = num.Reprendre @BaseV2 = N'$(BaseV2)', @IdReprise = @IdReprise OUTPUT, @Message = @MessageReprise OUTPUT;
    IF @rc <> 0
    BEGIN
        SET @m = LEFT(CONCAT(N'Bascule refusée : la reprise NUM-005 a rendu ', @rc, N' : ', @MessageReprise), 2048);
        THROW 51020, @m, 1;
    END;
    SELECT @n = COUNT(*) FROM num.Compteur;
END
ELSE IF N'$(Reprise)' <> N'faite'
    THROW 51020, N'Bascule refusée : Reprise attend « oui » (num.Reprendre dans cette transaction) ou « faite ».', 1;
IF @n = 0
    THROW 51020, N'Bascule refusée : aucun compteur dans $(BaseV3) ; la reprise de NUMERO_METIER (NUM-005) n''a pas été jouée.', 1;
SELECT @n = COUNT(*) FROM num.NumeroRendu WITH (TABLOCKX, HOLDLOCK);
GO

-- 2. Côté V3 : la recopie vers la V2, puis le rapprochement (dans la transaction : un refus la défait).
:r pont-v3.sql

DECLARE @Ecarts nvarchar(max), @m nvarchar(2048), @SeulementV3 int;
SELECT @Ecarts = STRING_AGG(CAST(e.Ecart + N' ' + e.Type + N' ' + CAST(e.Annee AS nvarchar(6))
                                 + CASE WHEN e.Jour > 0 THEN N' jour ' + CAST(e.Jour AS nvarchar(3)) ELSE N'' END
                                 + N' (V2 ' + ISNULL(CAST(e.ChronoV2 AS nvarchar(11)), N'-') + N', V3 '
                                 + ISNULL(CAST(e.ChronoV3 AS nvarchar(11)), N'-') + N')' AS nvarchar(max)), N' ; ')
FROM (SELECT TOP (15) * FROM num.EcartsV2 WHERE Ecart <> 'compteur V3 absent de la V2' ORDER BY Type, Annee, Jour) AS e;
IF @Ecarts IS NOT NULL
BEGIN
    SET @m = LEFT(N'Bascule refusée : les compteurs de $(BaseV3) ne sont pas ceux de $(BaseV2) ; rejouer la reprise (NUM-005), '
                + N'V2 fermée aux saisies. ' + @Ecarts, 2048);
    THROW 51020, @m, 1;
END;
-- Une année ou un jour d'une série de la V2 que seule la V3 a ouvert : permis ; la recopie l'écrira dans la
-- V2 à sa première attribution. Les séries de la V3 seule (FACV3, BDFNI de la reprise) ne sont pas des
-- écarts : la V2 n'en a pas de ligne et n'en reçoit jamais (pont-v3.sql).
SELECT @SeulementV3 = COUNT(*) FROM num.EcartsV2 WHERE Ecart = 'compteur V3 absent de la V2';
IF @SeulementV3 > 0 PRINT CONCAT(N'Compteurs que seule la V3 connaît : ', @SeulementV3, N'.');
GO

-- 3. Côté V2 : le pont, sa fiche, le corps d'origine, puis les trois programmes remplacés.
USE [$(BaseV2)];
GO
:r pont-v2.sql

DECLARE @FinBeta datetime2(0) = CASE WHEN N'$(FinBeta)' = N'non' THEN NULL ELSE TRY_CONVERT(datetime2(0), N'$(FinBeta)', 126) END;
MERGE rigv3.Pont AS p
USING (SELECT CAST(1 AS tinyint) AS Id) AS s ON p.Id = s.Id
WHEN MATCHED THEN UPDATE SET CodeGreffe = N'$(CodeGreffe)', BaseV3 = N'$(BaseV3)', BasculeLe = SYSDATETIME(),
                             BasculePar = ORIGINAL_LOGIN(), FinBeta = @FinBeta, RetourLe = NULL, RetourPar = NULL
WHEN NOT MATCHED THEN INSERT (Id, CodeGreffe, BaseV3, BasculeLe, BasculePar, FinBeta)
                      VALUES (1, N'$(CodeGreffe)', N'$(BaseV3)', SYSDATETIME(), ORIGINAL_LOGIN(), @FinBeta);

-- Le texte exact des programmes en service et les droits posés dessus (le retour les recrée).
-- Hors bêta, rien n'est gardé : le retour n'existe pas (NUM-022 CA03).
DELETE rigv3.CorpsOrigine;
IF @FinBeta IS NOT NULL
    INSERT rigv3.CorpsOrigine (Objet, Definition, QuotedIdentifier, AnsiNulls, Droits, GardeLe)
    SELECT o.name, m.definition, m.uses_quoted_identifier, m.uses_ansi_nulls,
           -- Les vues du catalogue n'ont pas toutes la collation de la base : elle est posée ici.
           -- fix-ok: erreur 4191 (collation de permission_name ≠ celle de la base) puis 8124 (o.name, colonne
           -- extérieure, dans l'agrégat), mesurées au banc NumerotationV2Check le 2026-11-21 : collation posée, nom lu par OBJECT_NAME.
           (SELECT STRING_AGG(CAST(CASE p.state WHEN 'D' THEN N'DENY ' ELSE N'GRANT ' END
                                   + p.permission_name COLLATE DATABASE_DEFAULT
                                   + N' ON OBJECT::dbo.' + QUOTENAME(OBJECT_NAME(p.major_id) COLLATE DATABASE_DEFAULT)
                                   + N' TO ' + QUOTENAME(pr.name COLLATE DATABASE_DEFAULT)
                                   + CASE p.state WHEN 'W' THEN N' WITH GRANT OPTION' ELSE N'' END AS nvarchar(max)), N'; ')
            FROM sys.database_permissions AS p
            JOIN sys.database_principals AS pr ON pr.principal_id = p.grantee_principal_id
            WHERE p.class = 1 AND p.major_id = o.object_id),
           SYSDATETIME()
    FROM sys.sql_modules AS m
    JOIN sys.objects AS o ON o.object_id = m.object_id
    WHERE o.schema_id = SCHEMA_ID(N'dbo') AND o.name IN (N'GENERER_NUMERO', N'CHERCHER_NUMERO', N'RESTITUER_NUMERO');
GO
:r GENERER_NUMERO.sql
GO
:r CHERCHER_NUMERO.sql
GO
:r RESTITUER_NUMERO.sql
GO

COMMIT TRANSACTION;
GO

-- 4. Les échecs de numérotation de toute l'instance, comptés (NUM-007 CA05) : chaque « Numérotation
--    indisponible » levé par un programme remplacé, avec sa base, son poste et son compte. Une session
--    par instance, partagée par ses bases V2 ; lue par echecs.sql et par la supervision (SOC-011).
IF NOT EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'$(SessionEchecs)')
    CREATE EVENT SESSION [$(SessionEchecs)] ON SERVER
    ADD EVENT sqlserver.error_reported (
        ACTION (sqlserver.database_name, sqlserver.client_hostname, sqlserver.client_app_name,
                sqlserver.server_principal_name, sqlserver.session_id)
        WHERE ([severity] = (16) AND [error_number] = (50000)
               AND [sqlserver].[like_i_sql_unicode_string]([message], N'Num_rotation indisponible :%')))
    ADD TARGET package0.ring_buffer (SET max_events_limit = (10000))
    WITH (MAX_DISPATCH_LATENCY = 1 SECONDS, STARTUP_STATE = ON);
IF NOT EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'$(SessionEchecs)')
    ALTER EVENT SESSION [$(SessionEchecs)] ON SERVER STATE = START;
GO

-- 5. Reprise=oui : le relevé des valeurs héritées de la V2 (num.ReleverHeritage, lot 6 -- rôles en double,
--    numéros hors format, numéros portés deux fois), en lecture, hors du blocage, comme le geste du passage
--    de docs\reconstruction\numerotation-series.md. La bascule est déjà validée : en cas d'erreur ici, le
--    rejouer seul (EXEC num.ReleverHeritage @BaseV2, @IdReprise).
-- fix-ok: Reprise=oui jouée au banc le 2026-11-21 avec la vraie reprise : num.ValeurHeritee restait vide (rôle
-- en double, numéro sans année, facture hors format non relevés) -- la bascule remplaçait le geste du passage
-- du lot 6 sans son relevé ; ajouté ici, banc vert ensuite.
USE [$(BaseV3)];
IF N'$(Reprise)' = N'oui'
BEGIN
    DECLARE @IdReprise int, @Signales int;
    SELECT @IdReprise = MAX(Id) FROM num.Reprise WHERE BaseV2 = N'$(BaseV2)' AND Code = 0;
    EXEC num.ReleverHeritage @BaseV2 = N'$(BaseV2)', @IdReprise = @IdReprise, @Signales = @Signales OUTPUT;
    PRINT CONCAT(N'Valeurs héritées relevées (num.ReleverHeritage, reprise ', @IdReprise, N') : ', @Signales,
                 N' signalement(s) pour le référent, dans num.Signalement.');
END;
GO

PRINT N'Bascule faite : $(BaseV2) demande ses numéros à $(BaseV3) (greffe $(CodeGreffe), fin de bêta : $(FinBeta)).';
GO
