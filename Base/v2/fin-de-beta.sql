-- ============================================================================================
-- FIN DE LA BÊTA d'un greffe bêta (lot 7, NUM-022 T02 et CA03 ; 2026-11-21) : le corps d'origine des
-- trois programmes, gardé par bascule.sql, est archivé. Le retour arrière (retour.sql) refuse ensuite
-- de tourner : il n'y a plus de retour à la V2.
--
--   cd Base\v2
--   sqlcmd -S <instance> -E -f 65001 -b -i fin-de-beta.sql -v BaseV2=RIG_AMIENS
--
-- À jouer pour chaque greffe bêta le jour J des autres greffes. Rien d'autre ne change : la V2 continue
-- de demander ses numéros à la V3.
-- fix-ok: option -f 65001 retirée puis remise : le client sqlcmd en Go de ce poste la refuse (« 'f': Unknown Option », mesuré le 2026-11-21) ; la doc Microsoft dit qu'un fichier non Unicode est lu dans la page de code du poste sans elle -- d'où la commande avec -f et le refus si le texte n'est pas lu en UTF-8.
-- ============================================================================================
:on error exit
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

-- Lu en UTF-8, sinon ses messages seraient écrits abîmés (sqlcmd -f 65001 ; voir bascule.sql).
IF UNICODE(N'é') <> 233
    THROW 51022, N'Refusé : le script n''a pas été lu en UTF-8 ; relancer sqlcmd avec -f 65001.', 1;
USE [$(BaseV2)];
IF OBJECT_ID(N'rigv3.CorpsOrigine', N'U') IS NULL
    THROW 51022, N'Fin de bêta : $(BaseV2) n''a jamais été basculée vers la V3.', 1;
GO
BEGIN TRANSACTION;
DECLARE @Archives int;
INSERT rigv3.CorpsOrigineArchive (Objet, Definition, QuotedIdentifier, AnsiNulls, Droits, GardeLe, ArchiveLe, ArchivePar)
SELECT Objet, Definition, QuotedIdentifier, AnsiNulls, Droits, GardeLe, SYSDATETIME(), ORIGINAL_LOGIN()
FROM rigv3.CorpsOrigine WITH (TABLOCKX, HOLDLOCK);
SET @Archives = @@ROWCOUNT;
DELETE rigv3.CorpsOrigine;
UPDATE rigv3.Pont SET FinBeta = SYSDATETIME() WHERE Id = 1 AND FinBeta > SYSDATETIME();
COMMIT TRANSACTION;
PRINT CONCAT(N'Fin de bêta : ', @Archives, N' corps d''origine archivé(s) dans $(BaseV2).rigv3.CorpsOrigineArchive ; le retour arrière n''est plus possible.');
GO
