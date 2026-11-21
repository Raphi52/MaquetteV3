-- ============================================================================================
-- LA BASE V2 D'UN GREFFE PREPAREE POUR LA RECOPIE CONTINUE V2 <-> V3 (lot 9 ; REC-003 T01 et CA02 ; 2026-11-21).
--
--   cd Base\v2
--   sqlcmd -S <instance> -E -f 65001 -b -i recopie-v2.sql ^
--          -v BaseV2=RIG_AMIENS RetentionJours=7 ^
--             Tables="ACTION_RCS,ADRESSE_BASE,ARRIVEE,CATEGORIE,CLIENT,DOSSIER_RCS,INSCRIPTION"
--
-- A jouer AVANT le transfert de la base (REC-001) : l'instantane du transfert lit alors la version du suivi d'ou la
-- remontee repart (rec.Transfert.VersionSuivi), et rien de ce que la V2 ecrit pendant la nuit n'est perdu pour la V3.
-- Les tables : celles des regles de recopie du serveur (Serveur\Recopie\ReglesRecopie.cs, une ligne par table partagee).
--
-- CE QUE LE SCRIPT FAIT, ET SEULEMENT CELA. La remontee capte les ecritures de la V2 par la base elle-meme, le suivi
-- des changements de SQL Server (CHANGE_TRACKING) : AUCUN programme de l'application V2 n'est modifie (REC-003 CA02) --
-- ni table, ni colonne, ni vue, ni procedure, ni declencheur n'est cree ou change ; l'application V2 ne voit rien.
--   1. le suivi de la base, retention RetentionJours jours, nettoyage automatique ; deja actif : la retention est
--      portee a RetentionJours si elle est plus courte (jamais raccourcie) ;
--   2. le suivi de chaque table partagee (sa cle primaire est exigee par SQL Server) ;
--   3. le compte rendu : tables suivies, version courante, retention.
-- Retention proposee : 7 jours (REC-003 CA04 demande une heure d'arret de la V3 ; 7 jours couvrent un serveur V3 arrete
-- un week-end prolonge). Au-dela, la remontee s'arrete en le disant, et la base est a comparer (REC-006).
-- Cout : SQL Server ecrit une ligne par ligne changee dans ses tables internes, dans la transaction de l'agent, sans
-- attendre la V3 ; la mesure sur la copie de la plus grosse base reste a faire (REC-003 T01 ; voir recopie.md).
-- Rejouer le script est sans risque. Le retirer : ALTER TABLE ... DISABLE CHANGE_TRACKING, puis ALTER DATABASE ... SET
-- CHANGE_TRACKING = OFF (la recopie de ce greffe s'arrete alors : retour V2, REC-007).
-- ============================================================================================
:on error exit
SET NOCOUNT ON;
GO

IF DB_ID(N'$(BaseV2)') IS NULL
    THROW 51030, N'Recopie refusee : la base « $(BaseV2) » n''est pas sur cette instance.', 1;
IF TRY_CONVERT(int, N'$(RetentionJours)') IS NULL OR TRY_CONVERT(int, N'$(RetentionJours)') < 1
    THROW 51030, N'Recopie refusee : RetentionJours « $(RetentionJours) » n''est pas un nombre de jours.', 1;
IF LTRIM(RTRIM(N'$(Tables)')) = N''
    THROW 51030, N'Recopie refusee : Tables est vide (les tables partagees, separees par des virgules).', 1;
GO

-- 1. La base.
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_databases WHERE database_id = DB_ID(N'$(BaseV2)'))
    ALTER DATABASE [$(BaseV2)] SET CHANGE_TRACKING = ON (CHANGE_RETENTION = $(RetentionJours) DAYS, AUTO_CLEANUP = ON);
ELSE IF EXISTS (SELECT 1 FROM sys.change_tracking_databases
                WHERE database_id = DB_ID(N'$(BaseV2)')
                  AND retention_period * CASE retention_period_units WHEN 1 THEN 1 WHEN 2 THEN 60 ELSE 1440 END < $(RetentionJours) * 1440)
    ALTER DATABASE [$(BaseV2)] SET CHANGE_TRACKING (CHANGE_RETENTION = $(RetentionJours) DAYS);
GO

USE [$(BaseV2)];
GO

-- 2. Les tables partagees.
-- fix-ok: « EXEC (N'...' + QUOTENAME(@t) + N'...') » est refuse par SQL Server (« Incorrect syntax near 'QUOTENAME' »,
-- banc RecopieDeuxSensCheck, 1er essai du 2026-11-21) : EXEC (...) n'admet que des chaines et des variables -> @sql.
DECLARE @liste nvarchar(max) = N'$(Tables)' + N',', @t sysname, @i int, @m nvarchar(400), @sql nvarchar(600);
WHILE LEN(@liste) > 0
BEGIN
    SET @i = CHARINDEX(N',', @liste);
    SET @t = LTRIM(RTRIM(LEFT(@liste, @i - 1)));
    SET @liste = STUFF(@liste, 1, @i, N'');
    IF @t = N'' CONTINUE;
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@t), N'U') IS NULL
    BEGIN
        SET @m = N'Recopie refusee : la table dbo.' + @t + N' n''existe pas dans $(BaseV2).';
        THROW 51030, @m, 1;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@t)) AND is_primary_key = 1)
    BEGIN
        SET @m = N'Recopie refusee : la table dbo.' + @t + N' n''a pas de cle primaire (le suivi des changements l''exige).';
        THROW 51030, @m, 1;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables WHERE object_id = OBJECT_ID(N'dbo.' + QUOTENAME(@t)))
    BEGIN
        SET @sql = N'ALTER TABLE dbo.' + QUOTENAME(@t) + N' ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);';
        EXEC sys.sp_executesql @sql;
    END;
END;
GO

-- 3. Le compte rendu.
SELECT t.name AS TableSuivie, CHANGE_TRACKING_MIN_VALID_VERSION(c.object_id) AS VersionMinimale
FROM sys.change_tracking_tables AS c JOIN sys.tables AS t ON t.object_id = c.object_id
ORDER BY t.name;
SELECT CHANGE_TRACKING_CURRENT_VERSION() AS VersionCourante, d.retention_period AS Retention, d.retention_period_units_desc AS Unite,
       d.is_auto_cleanup_on AS NettoyageAutomatique
FROM sys.change_tracking_databases AS d WHERE d.database_id = DB_ID();
GO
