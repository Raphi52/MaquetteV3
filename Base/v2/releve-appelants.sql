-- ============================================================================================
-- RELEVÉ DES APPELANTS DE CHERCHER_NUMERO dans une base V2 (lot 7, NUM-003 CA02 et T03 ; 2026-11-21).
-- Lecture seule : à jouer sur chacune des 40 bases avant le passage, puis à écrire dans
-- docs\reconstruction\numerotation-v2.md.
--
--   sqlcmd -S <instance> -E -f 65001 -d RIG_AMIENS -i releve-appelants.sql -W -s ";"
--
-- Une ligne par appelant : les objets SQL de la base (procédures, fonctions, vues, déclencheurs) dont le
-- texte cite CHERCHER_NUMERO, et les étapes des tâches planifiées de l'instance (msdb) qui le citent.
-- Aucune ligne = aucun appelant. Les tâches planifiées demandent le droit de lire msdb : sans lui, une
-- ligne « tâches planifiées non lues » le dit.
-- fix-ok: option -f 65001 retirée puis remise : le client sqlcmd en Go de ce poste la refuse (« 'f': Unknown Option », mesuré le 2026-11-21) ; la doc Microsoft dit qu'un fichier non Unicode est lu dans la page de code du poste sans elle -- d'où la commande avec -f et le refus si le texte n'est pas lu en UTF-8.
-- fix-ok: le schéma rigv3 est écarté : au 3e passage du banc, rigv3.DemanderNumero sortait comme appelant (son commentaire cite CHERCHER_NUMERO).
-- ============================================================================================
SET NOCOUNT ON;

DECLARE @Releve TABLE (Base sysname, Genre nvarchar(30), Appelant nvarchar(400), Detail nvarchar(400));

INSERT @Releve (Base, Genre, Appelant, Detail)
SELECT DB_NAME(), N'objet SQL', SCHEMA_NAME(o.schema_id) + N'.' + o.name, o.type_desc
FROM sys.sql_modules AS m
JOIN sys.objects AS o ON o.object_id = m.object_id
WHERE m.definition LIKE N'%CHERCHER[_]NUMERO%'
  AND NOT (o.name = N'CHERCHER_NUMERO' AND o.schema_id = SCHEMA_ID(N'dbo'))
  AND o.schema_id <> ISNULL(SCHEMA_ID(N'rigv3'), -1);          -- le pont lui-même (pont-v2.sql) n'est pas un appelant

IF HAS_PERMS_BY_NAME(N'msdb.dbo.sysjobsteps', N'OBJECT', N'SELECT') = 1
   AND HAS_PERMS_BY_NAME(N'msdb.dbo.sysjobs', N'OBJECT', N'SELECT') = 1
    INSERT @Releve (Base, Genre, Appelant, Detail)
    SELECT DB_NAME(), N'tâche planifiée', j.name + N' / ' + s.step_name, N'base de l''étape : ' + ISNULL(s.database_name, N'?')
    FROM msdb.dbo.sysjobsteps AS s
    JOIN msdb.dbo.sysjobs AS j ON j.job_id = s.job_id
    WHERE s.command LIKE N'%CHERCHER[_]NUMERO%';
ELSE
    INSERT @Releve (Base, Genre, Appelant, Detail)
    VALUES (DB_NAME(), N'tâches planifiées non lues', N'', N'droit de lecture sur msdb.dbo.sysjobsteps absent');

SELECT Base, Genre, Appelant, Detail FROM @Releve ORDER BY Genre, Appelant;
