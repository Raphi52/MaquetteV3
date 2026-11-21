-- NUM-002 / NUM-004 T02 : le nouveau corps de RESTITUER_NUMERO, dans chaque base V2 (lot 7, 2026-11-21).
-- Un numéro rendu par la V2 l'est au compteur de la V3 (num.Restituer), qui le redonnera au prochain
-- demandeur de sa série, V2 ou V3. Signature d'aujourd'hui (RESTITUER_NUMERO.sql:1-6).
--
-- Le code est rendu dans @MonErreur ET par RETURN : l'appel .NET central lit @MonErreur
-- (RigMetier\Numerotation\Numerotation.cs:205). Codes de num.Restituer (NUM-004 CA09) :
--    0  rendu / série sans restitution (rien n'est redonné, la demande est journalisée) / déjà rendu ;
--   -2  type inconnu ; -3 numéro illisible ; -4 jamais donné : rien n'est rendu ;
--   -9  numérotation indisponible (NUM-007 CA06) : une erreur est levée avec le message, rien n'est écrit.
-- L'ancien laissait @MonErreur à 0 sur un type inconnu et sortait sans code en erreur
-- (RESTITUER_NUMERO.sql:26, 41, 58-62) : écarts « à valider » de NUM-004, ici codés selon ses critères.
--
-- Joué par bascule.sql (base V2 courante), jamais seul.
CREATE OR ALTER PROCEDURE dbo.RESTITUER_NUMERO
        @TYPE_NUMERO_METIER varchar(5),
        @NUMERO_RETOURNE varchar(20),
        @ANNEE smallint = 0,
        @RAISON varchar(50) = '',
        @MonErreur int = 0 OUTPUT
AS
BEGIN
    -- Même forme de réponse que l'ancien : un message « 1 ligne affectée » quand il ajoutait sa ligne
    -- « attribution erronée » (RESTITUER_NUMERO.sql:114-123), aucun pour une série sans restitution.
    DECLARE @CompterLignes bit = CASE WHEN @@OPTIONS & 512 = 0 THEN 1 ELSE 0 END;
    SET NOCOUNT ON;
    DECLARE @rc int, @Issue varchar(20), @Message nvarchar(400), @Etat tinyint;
    DECLARE @UneLigne TABLE (x bit);

    EXEC @rc = rigv3.RendreNumero @Type = @TYPE_NUMERO_METIER, @Numero = @NUMERO_RETOURNE, @Annee = @ANNEE,
         @Explication = @RAISON, @Issue = @Issue OUTPUT, @Message = @Message OUTPUT, @Etat = @Etat OUTPUT;
    SET @MonErreur = @rc;

    IF @rc = 0 AND @Issue IN ('rendu', 'deja rendu') AND @CompterLignes = 1
    BEGIN
        SET NOCOUNT OFF;
        INSERT @UneLigne (x) VALUES (1);
    END;
    IF @rc = -9 RAISERROR (N'%s', 16, @Etat, @Message);
    RETURN @rc;
END;
