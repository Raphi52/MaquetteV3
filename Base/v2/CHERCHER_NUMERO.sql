-- NUM-003 : le nouveau corps de CHERCHER_NUMERO, le second programme d'attribution de la V2 (lot 7,
-- 2026-11-21). Malgré son nom, l'ancien n'est pas une lecture : il donne un numéro, efface des numéros
-- rendus et crée des compteurs (CHERCHER_NUMERO.sql:92-94, 106-110, 123-133). Il passe donc, lui aussi,
-- par le compteur de la V3 : aucun chemin de la V2 n'avance plus un compteur à côté de la V3.
--
-- Signature d'aujourd'hui (CHERCHER_NUMERO.sql:1-7) : sortie sur 20 caractères, pas de code greffe
-- (l'ancien prenait celui du compteur, CHERCHER_NUMERO.sql:62 : c'est ce que fait num.Attribuer sans code).
-- Le numéro est écrit comme par GENERER_NUMERO -- lettre entière (« SANC » de JDSAN, pas « SAN »),
-- caractères fixes recopiés : écarts « à valider » de NUM-003, docs\reconstruction\numerotation-v2.md.
--
-- Aucun appelant n'est connu (code de la V2 et RIG_DEV, 2026-11-20) : chaque appel est journalisé dans
-- rigv3.AppelChercher avec le compte, le poste et l'application, pour mesurer son usage réel (NUM-003
-- CA03, Q11). Mêmes codes que GENERER_NUMERO ; une erreur rend un code non nul et un message, jamais 0
-- (NUM-003 CA05 -- l'ancien sortait sans valeur en erreur, l'appelant lisait 0).
--
-- Joué par bascule.sql (base V2 courante), jamais seul.
CREATE OR ALTER PROCEDURE dbo.CHERCHER_NUMERO
        @TYPE_NUMERO_METIER varchar(5),
        @NUMERO_RETOURNE varchar(20) OUTPUT,
        @ANNEE smallint = 0,
        @QUANTIEME smallint = 0,
        @MONERREUR int = 0
AS
BEGIN
    -- Même forme de réponse que l'ancien et que GENERER_NUMERO : un message « 1 ligne affectée » si
    -- le numéro est donné, aucun sinon.
    DECLARE @CompterLignes bit = CASE WHEN @@OPTIONS & 512 = 0 THEN 1 ELSE 0 END;
    SET NOCOUNT ON;
    DECLARE @rc int, @Numero varchar(40), @Message nvarchar(400), @Etat tinyint;
    DECLARE @UneLigne TABLE (x bit);

    EXEC @rc = rigv3.DemanderNumero @Programme = N'CHERCHER_NUMERO', @Type = @TYPE_NUMERO_METIER,
         @Annee = @ANNEE, @Jour = @QUANTIEME, @CodeGreffe = NULL,
         @Numero = @Numero OUTPUT, @Message = @Message OUTPUT, @Etat = @Etat OUTPUT;

    INSERT rigv3.AppelChercher (Compte, Poste, Application, Type, Annee, Jour, Numero, Code)
    VALUES (rigv3.CompteAppelant(), HOST_NAME(), APP_NAME(), @TYPE_NUMERO_METIER, @ANNEE, @QUANTIEME, @Numero, @rc);

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

    RAISERROR (N'%s', 16, @Etat, @Message);
    RETURN @rc;
END;
