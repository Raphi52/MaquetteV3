-- ============================================================================================
-- RETOUR ARRIÈRE PENDANT LA BÊTA : rendre la numérotation à la V2 d'un greffe (lot 7, NUM-022 ;
-- 2026-11-21). L'inverse de bascule.sql.
--
--   cd Base\v2
--   sqlcmd -S <instance> -E -f 65001 -b -i retour.sql -v BaseV2=RIG_AMIENS BaseV3=RIGV3_AMIENS CodeGreffe=8002
--
-- Dans UNE seule transaction (NUM-022 CA01), NUMERO_METIER et les compteurs de la V3 verrouillés en
-- exclusif -- la V2 comme la V3 attendent, personne ne numérote :
--   1. refus si le pont n'est pas en service, si le greffe est hors bêta, si la bêta est finie ou si le
--      corps d'origine a été archivé (fin-de-beta.sql) : il n'est plus déployable (NUM-022 CA03) ;
--   2. la table de la V2 reçoit le chrono courant de chaque compteur de la V3 et ses numéros rendus :
--      chaque ligne repasse par la recopie (pont-v3.sql), puis num.EcartsV2 doit être vide, sinon refus ;
--   3. la V3 ne numérote plus pour ce greffe : recopie retirée, toute écriture dans ses compteurs refusée
--      (erreur 51023) -- un numéro qu'elle donnerait encore serait un doublon de la V2 ;
--   4. les trois programmes d'origine sont remis, tels quels, avec leurs réglages et leurs droits ;
--   5. le pont est retiré de la V2 (garde, synonymes, procédures) ; ses tables restent, la fiche dit le retour.
-- Toute erreur annule tout : rien n'est à moitié rendu. La V2 reprend alors au chrono où la V3 s'est
-- arrêtée, sans trou ni doublon : les actes numérotés par la V3 gardent leurs numéros (REC-007).
-- fix-ok: option -f 65001 retirée puis remise : le client sqlcmd en Go de ce poste la refuse (« 'f': Unknown Option », mesuré le 2026-11-21) ; la doc Microsoft dit qu'un fichier non Unicode est lu dans la page de code du poste sans elle -- d'où la commande avec -f et le refus si le texte n'est pas lu en UTF-8.
-- fix-ok: le bloc TRY/CATCH du 4 : une erreur de compilation d'un corps gardé n'annule pas la transaction d'elle-même (XACT_ABORT ne couvre pas la compilation) ; constaté au banc sur un corps corrompu (« Incorrect syntax near 'SELEC' »), désormais rattrapé et annulé tout de suite.
-- ============================================================================================
:on error exit
SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

-- Lu en UTF-8, sinon ses messages seraient écrits abîmés (sqlcmd -f 65001 ; voir bascule.sql).
IF UNICODE(N'é') <> 233
    THROW 51022, N'Refusé : le script n''a pas été lu en UTF-8 ; relancer sqlcmd avec -f 65001.', 1;
GO

BEGIN TRANSACTION;
GO

-- 1. Le retour est-il encore permis ?
USE [$(BaseV2)];
IF OBJECT_ID(N'rigv3.Pont', N'U') IS NULL
    THROW 51022, N'Retour à la V2 refusé : $(BaseV2) n''a jamais été basculée vers la V3.', 1;
GO
DECLARE @FinBeta datetime2(0), @Base sysname, @Corps int, @n int, @m nvarchar(2048);
SELECT @FinBeta = FinBeta, @Base = BaseV3 FROM rigv3.Pont WITH (UPDLOCK, HOLDLOCK) WHERE Id = 1 AND RetourLe IS NULL;
IF @Base IS NULL
    THROW 51022, N'Retour à la V2 refusé : le pont de $(BaseV2) n''est pas en service (déjà rendu, ou jamais posé).', 1;
IF @Base <> N'$(BaseV3)'
BEGIN
    SET @m = N'Retour à la V2 refusé : $(BaseV2) demande ses numéros à ' + @Base + N', pas à $(BaseV3).';
    THROW 51022, @m, 1;
END;
IF @FinBeta IS NULL
    THROW 51022, N'Retour à la V2 refusé : le greffe $(CodeGreffe) n''est pas un greffe bêta ; le retour ne vaut que pendant la bêta (NUM-022 CA03).', 1;
IF SYSDATETIME() >= @FinBeta
BEGIN
    SET @m = N'Retour à la V2 refusé : la bêta du greffe $(CodeGreffe) est finie depuis le ' + CONVERT(nvarchar(19), @FinBeta, 120)
           + N' ; le corps d''origine des programmes n''est plus déployable (NUM-022 CA03).';
    THROW 51022, @m, 1;
END;
SELECT @Corps = COUNT(*) FROM rigv3.CorpsOrigine WHERE Objet IN (N'GENERER_NUMERO', N'CHERCHER_NUMERO', N'RESTITUER_NUMERO');
IF @Corps <> 3
    THROW 51022, N'Retour à la V2 refusé : le corps d''origine des programmes de $(BaseV2) a été archivé (fin de la bêta) ; il n''est plus déployable (NUM-022 CA03).', 1;
SELECT @n = COUNT(*) FROM dbo.NUMERO_METIER WITH (TABLOCKX, HOLDLOCK);
GO

-- 2 et 3. Côté V3 : tout ce qu'elle tient passe dans la V2, puis elle se ferme.
USE [$(BaseV3)];
DECLARE @n int, @Ecarts nvarchar(max), @m nvarchar(2048), @Inconnues int;
SELECT @n = COUNT(*) FROM num.Compteur WITH (TABLOCKX, HOLDLOCK);
SELECT @n = COUNT(*) FROM num.NumeroRendu WITH (TABLOCKX, HOLDLOCK);
-- Chaque ligne repasse par la recopie : la correspondance V3 -> V2 n'est écrite qu'à un endroit, dans les
-- déclencheurs de pont-v3.sql (chrono courant, compteur absent de la V2, numéro rendu absent de la V2) ;
-- les séries de la V3 seule (FACV3, BDFNI) restent dans la V3 : la V2 n'en a pas de ligne.
-- fix-ok: avec la vraie reprise (Reprise=oui), ce retour écrivait 2 lignes FACV3/BDFNI dans NUMERO_METIER (banc
-- NumerotationV2Check, 2026-11-21) et num.Reprendre aurait refusé de reprendre la base ; corrigé dans pont-v3.sql.
UPDATE num.Compteur SET Chrono = Chrono;
UPDATE num.NumeroRendu SET Chrono = Chrono;
SELECT @Ecarts = STRING_AGG(CAST(e.Ecart + N' ' + e.Type + N' ' + CAST(e.Annee AS nvarchar(6))
                                 + N' (V2 ' + ISNULL(CAST(e.ChronoV2 AS nvarchar(11)), N'-') + N', V3 '
                                 + ISNULL(CAST(e.ChronoV3 AS nvarchar(11)), N'-') + N')' AS nvarchar(max)), N' ; ')
FROM (SELECT TOP (15) * FROM num.EcartsV2 ORDER BY Type, Annee, Jour) AS e;
IF @Ecarts IS NOT NULL
BEGIN
    SET @m = LEFT(N'Retour à la V2 refusé : après recopie, la V2 diffère encore de la V3 : ' + @Ecarts, 2048);
    THROW 51022, @m, 1;
END;
-- Pour l'équipe de passage : les lignes « attribution erronée » d'avant la bascule que la reprise n'a pas
-- gardées (au-delà du compteur, en double) ; la V2 revenue les redonnera comme avant le passage.
SELECT @Inconnues = COUNT(*)
FROM num.NumeroMetierV2 AS v
WHERE v.NMMTR_ATTRIBUTION_ERRONEE = 1 AND v.NMMTR_GESTION_NUM_ERR = 1
  AND NOT EXISTS (SELECT 1 FROM num.NumeroRendu AS r
                  WHERE r.Serie = v.NMMTR_TYPE_METIER AND r.Annee = v.NMMTR_ANNEE AND r.Chrono = v.NMMTR_CHRONO
                    AND (r.Jour = 0 OR r.Jour = v.NMMTR_QUANTIEME));
IF @Inconnues > 0
    PRINT CONCAT(N'Attention : ', @Inconnues, N' ligne(s) « attribution erronée » de la V2 que la V3 ne redonnait pas ',
                 N'seront redonnées par la V2 (voir num.Journal, événement « reprise »).');
DROP TRIGGER num.TR_Compteur_RecopieV2;
DROP TRIGGER num.TR_NumeroRendu_RecopieV2;
DROP VIEW num.EcartsV2;
DROP SYNONYM num.NumeroMetierV2;
GO
CREATE TRIGGER num.TR_Compteur_RenduALaV2 ON num.Compteur AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51023, N'Numérotation rendue à la V2 (NUM-022) : le greffe $(CodeGreffe) numérote dans sa base $(BaseV2) ; la V3 ne donne plus de numéro. Une nouvelle reprise (NUM-005) commence par supprimer num.TR_Compteur_RenduALaV2 et num.TR_NumeroRendu_RenduALaV2.', 1;
END;
GO
CREATE TRIGGER num.TR_NumeroRendu_RenduALaV2 ON num.NumeroRendu AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    THROW 51023, N'Numérotation rendue à la V2 (NUM-022) : le greffe $(CodeGreffe) numérote dans sa base $(BaseV2) ; la V3 ne rend plus de numéro. Une nouvelle reprise (NUM-005) commence par supprimer num.TR_Compteur_RenduALaV2 et num.TR_NumeroRendu_RenduALaV2.', 1;
END;
GO

-- 4. Les trois programmes d'origine, tels qu'ils étaient : texte, QUOTED_IDENTIFIER / ANSI_NULLS de leur
--    création (sans quoi « "9999" » ne se compile plus), droits.
USE [$(BaseV2)];
DECLARE @Objet sysname, @Definition nvarchar(max), @QI bit, @AN bit, @Droits nvarchar(max), @Lot nvarchar(max);
DECLARE corps CURSOR LOCAL FAST_FORWARD FOR
    SELECT Objet, Definition, QuotedIdentifier, AnsiNulls, Droits FROM rigv3.CorpsOrigine
    WHERE Objet IN (N'GENERER_NUMERO', N'CHERCHER_NUMERO', N'RESTITUER_NUMERO') ORDER BY Objet;
OPEN corps;
FETCH NEXT FROM corps INTO @Objet, @Definition, @QI, @AN, @Droits;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- Une erreur de compilation d'un texte gardé n'annule pas la transaction d'elle-même (XACT_ABORT ne
    -- touche pas les erreurs de compilation) : elle est rattrapée ici, tout est annulé, rien n'est à moitié rendu.
    BEGIN TRY
        SET @Lot = N'DROP PROCEDURE dbo.' + QUOTENAME(@Objet) + N';';
        EXEC sys.sp_executesql @Lot;
        -- Les réglages valent pour le lot qui compile le texte : posés dans un lot englobant, puis le texte.
        SET @Lot = N'SET QUOTED_IDENTIFIER ' + CASE WHEN @QI = 1 THEN N'ON' ELSE N'OFF' END
                 + N'; SET ANSI_NULLS ' + CASE WHEN @AN = 1 THEN N'ON' ELSE N'OFF' END
                 + N'; EXEC sys.sp_executesql @Texte;';
        EXEC sys.sp_executesql @Lot, N'@Texte nvarchar(max)', @Texte = @Definition;
        IF @Droits IS NOT NULL EXEC sys.sp_executesql @Droits;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
    FETCH NEXT FROM corps INTO @Objet, @Definition, @QI, @AN, @Droits;
END;
CLOSE corps;
DEALLOCATE corps;

-- 5. Le pont quitte la V2 ; sa fiche dit le retour.
DROP TRIGGER dbo.TR_NUMERO_METIER_RIGV3;
DROP PROCEDURE rigv3.DemanderNumero;
DROP PROCEDURE rigv3.RendreNumero;
DROP FUNCTION rigv3.BaseV3Injoignable;
DROP FUNCTION rigv3.MessageIndisponible;
DROP FUNCTION rigv3.CompteAppelant;
DROP SYNONYM rigv3.Attribuer;
DROP SYNONYM rigv3.Restituer;
UPDATE rigv3.Pont SET RetourLe = SYSDATETIME(), RetourPar = ORIGINAL_LOGIN() WHERE Id = 1;
GO

COMMIT TRANSACTION;
GO

PRINT N'Retour fait : $(BaseV2) numérote de nouveau seule (greffe $(CodeGreffe)) ; $(BaseV3) ne donne plus de numéro.';
GO
