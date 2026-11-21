-- NUM-002 : le nouveau corps de GENERER_NUMERO, dans chaque base V2 (lot 7, 2026-11-21).
-- La V2 demande ses numéros comme avant ; c'est le compteur de la V3 (num.Attribuer de la base V3 du
-- greffe, même instance, même transaction) qui les donne. Même nom, mêmes paramètres, mêmes valeurs par
-- défaut que l'ancien (D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Stored Procedures\
-- GENERER_NUMERO.sql:2-9) : aucun appelant de la V2 n'est recompilé.
--
-- Joué par bascule.sql (base V2 courante), jamais seul : il appelle rigv3.DemanderNumero (pont-v2.sql).
--
-- Ce qui est rendu :
--    0  numéro donné, dans @NUMERO_RETOURNE ;
--   -2  type inconnu, rien n'est écrit, sans message (comme l'ancien, GENERER_NUMERO.sql:51-55) ;
--   -5, -6  refus du compteur : série pleine ou plage épuisée (l'ancien bouclait sans fin), année
--       au-delà de la suivante (lot 6) ; -9 numérotation indisponible (NUM-007) : une erreur est levée
--       avec le message, et rien n'est consommé.
--       fix-ok: le code -6 vient de num.Attribuer redéfini par le lot 6 (NUM-005_reprise.sql, lu le 2026-11-21).
-- Jamais 0 avec un numéro vide (NUM-002 CA07).
CREATE OR ALTER PROCEDURE dbo.GENERER_NUMERO
        @TYPE_NUMERO_METIER varchar(5),
        @NUMERO_RETOURNE varchar(40) OUTPUT,
        @ANNEE smallint = 0,
        @QUANTIEME smallint = 0,
        @CODE_GREFFE varchar(5) = '9999',
        @MONERREUR int = 0
AS
BEGIN
    -- La forme de la réponse est celle de l'ancien : UN message « 1 ligne affectée » quand le numéro est
    -- donné (son UPDATE ou INSERT de NUMERO_METIER), aucun sinon -- mesuré le 2026-11-21 sur l'ancien corps.
    -- L'appel VB6 par lot SQL en dépend : il saute ce premier résultat avant de lire le numéro
    -- (RIG_CliSerL_Inscription\modGeneral.bas:85, « Set rst = rst.NextRecordset »).
    DECLARE @CompterLignes bit = CASE WHEN @@OPTIONS & 512 = 0 THEN 1 ELSE 0 END;   -- 512 = NOCOUNT de l'appelant
    SET NOCOUNT ON;
    DECLARE @rc int, @Numero varchar(40), @Message nvarchar(400), @Etat tinyint;
    DECLARE @UneLigne TABLE (x bit);

    EXEC @rc = rigv3.DemanderNumero @Programme = N'GENERER_NUMERO', @Type = @TYPE_NUMERO_METIER,
         @Annee = @ANNEE, @Jour = @QUANTIEME, @CodeGreffe = @CODE_GREFFE,
         @Numero = @Numero OUTPUT, @Message = @Message OUTPUT, @Etat = @Etat OUTPUT;

    IF @rc = 0
    BEGIN
        SET @NUMERO_RETOURNE = @Numero;
        IF @CompterLignes = 1
        BEGIN
            SET NOCOUNT OFF;
            INSERT @UneLigne (x) VALUES (1);
        END;
        RETURN 0;
    END;
    IF @rc = -2 RETURN -2;

    -- RAISERROR et non THROW : il ne coupe pas le lot de l'appelant, qui lit donc aussi le code rendu
    -- (NUM-007 CA01 : une erreur ET un code non nul). L'état dit la cause : 1 base V3 hors service,
    -- 2 compteur tenu au-delà du délai, 3 erreur pendant l'appel, 5 refus du compteur.
    RAISERROR (N'%s', 16, @Etat, @Message);
    RETURN @rc;
END;
