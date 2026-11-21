-- ============================================================================================
-- LES ÉCHECS DE NUMÉROTATION COMPTÉS sur une instance (lot 7, NUM-007 CA05 ; 2026-11-21). Lecture seule.
--
--   sqlcmd -S <instance> -E -f 65001 -i echecs.sql -v SessionEchecs=rigv3_numerotation_echecs -W -s ";"
--
-- Chaque « Numérotation indisponible » levé par GENERER_NUMERO, CHERCHER_NUMERO ou RESTITUER_NUMERO
-- remplacés est gardé par la session d'événements que pose bascule.sql (en mémoire : 10 000 derniers,
-- jusqu'au redémarrage de l'instance), même si l'appelant annule sa transaction. Une ligne par base V2 :
-- combien, le premier, le dernier, et la cause du dernier (état de l'erreur : 1 base V3 hors service,
-- 2 compteur tenu au-delà de 5 s, 3 erreur pendant l'appel). La supervision d'Amitel (SOC-011) lit
-- cette requête pour alerter dès le premier échec. Lire la session demande VIEW SERVER STATE.
-- fix-ok: option -f 65001 retirée puis remise : le client sqlcmd en Go de ce poste la refuse (« 'f': Unknown Option », mesuré le 2026-11-21) ; la doc Microsoft dit qu'un fichier non Unicode est lu dans la page de code du poste sans elle -- d'où la commande avec -f et le refus si le texte n'est pas lu en UTF-8.
-- ============================================================================================
SET NOCOUNT ON;

WITH Evenements AS (
    SELECT e.n.value('(action[@name="database_name"]/value)[1]', 'sysname')      AS Base,
           e.n.value('@timestamp', 'datetime2(3)')                                 AS Le,
           e.n.value('(data[@name="state"]/value)[1]', 'int')                      AS Etat,
           e.n.value('(action[@name="client_hostname"]/value)[1]', 'nvarchar(128)') AS Poste,
           e.n.value('(action[@name="server_principal_name"]/value)[1]', 'nvarchar(128)') AS Compte,
           e.n.value('(data[@name="message"]/value)[1]', 'nvarchar(400)')          AS Message
    FROM (SELECT CAST(t.target_data AS xml) AS x
          FROM sys.dm_xe_session_targets AS t
          JOIN sys.dm_xe_sessions AS s ON s.address = t.event_session_address
          WHERE s.name = N'$(SessionEchecs)' AND t.target_name = N'ring_buffer') AS r
    CROSS APPLY r.x.nodes('/RingBufferTarget/event') AS e(n))
SELECT Base, COUNT(*) AS Echecs, MIN(Le) AS Premier, MAX(Le) AS Dernier,
       (SELECT TOP (1) CONCAT(N'état ', d.Etat, N', ', d.Poste, N', ', d.Compte) FROM Evenements AS d
        WHERE d.Base = g.Base ORDER BY d.Le DESC) AS DerniereCause
FROM Evenements AS g
GROUP BY Base
ORDER BY Base;
