/* ============================================================
   Trigger : NUMERO_METIER  (PK = NMMTR_ID_NMMTR)
   ------------------------------------------------------------
   Emission JET NF525 code 10 (changement du mode de gestion de la sequence continue) :
   des qu'un mode de numerotation change (STRUCTURE_NUMERO, CODAGE_ANNEE, GESTION_NUM_ERR),
   pour un greffe certifie (dbo.fn_CertifNF525Active() = 1, PARAMETRAGE.ACTIVATION_CERTIF_NF525). Ne trace PAS le simple avancement du chrono.
   ------------------------------------------------------------
   ATOMICITE (decision A, 2026-07-13) : cf. TR_INFO_GREFFE. Une anomalie d'emission JET annule la
   modif metier declencheuse (trace-ou-rien). Le TRY/CATCH ne sert qu'au nettoyage du curseur +
   remontee de l'erreur ; il ne masque aucun echec d'emission.
   ============================================================ */

CREATE   TRIGGER [dbo].[TR_NUMERO_METIER_JET]
ON [dbo].[NUMERO_METIER]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Garde d'entree : uniquement les colonnes de MODE de sequence (pas le chrono/attribution).
    IF NOT (UPDATE(NMMTR_STRUCTURE_NUMERO) OR UPDATE(NMMTR_CODAGE_ANNEE) OR UPDATE(NMMTR_GESTION_NUM_ERR))
        RETURN;

    BEGIN TRY
        DECLARE @uid int, @login nvarchar(256), @poste nvarchar(15),
                @cg varchar(20), @type varchar(50), @certif bit, @info nvarchar(200),
                @structN varchar(255), @structO varchar(255), @codN tinyint, @codO tinyint, @gestN bit, @gestO bit;

        -- Identite (robustesse, PAS une emission JET : fallback SUSER_SNAME() (identite SQL reelle), ne fait jamais echouer le metier).
        BEGIN TRY SET @uid = CONVERT(int, SESSION_CONTEXT(N'RigUserId')); END TRY BEGIN CATCH SET @uid = NULL; END CATCH
        SET @login = ISNULL((SELECT UTLST_LOGIN FROM dbo.UTILISATEUR WHERE UTLST_ID_UTLST = @uid), SUSER_SNAME());
        SET @poste = CONVERT(nvarchar(15), HOST_NAME());

        DECLARE cur CURSOR LOCAL STATIC FOR
            SELECT i.NMMTR_CODE_GREFFE, i.NMMTR_TYPE_METIER,
                   i.NMMTR_STRUCTURE_NUMERO, d.NMMTR_STRUCTURE_NUMERO,
                   i.NMMTR_CODAGE_ANNEE, d.NMMTR_CODAGE_ANNEE,
                   i.NMMTR_GESTION_NUM_ERR, d.NMMTR_GESTION_NUM_ERR
            FROM inserted i
            JOIN deleted  d ON d.NMMTR_ID_NMMTR = i.NMMTR_ID_NMMTR
            WHERE (i.NMMTR_STRUCTURE_NUMERO <> d.NMMTR_STRUCTURE_NUMERO)
               OR (i.NMMTR_STRUCTURE_NUMERO IS NULL AND d.NMMTR_STRUCTURE_NUMERO IS NOT NULL)
               OR (i.NMMTR_STRUCTURE_NUMERO IS NOT NULL AND d.NMMTR_STRUCTURE_NUMERO IS NULL)
               OR (i.NMMTR_CODAGE_ANNEE <> d.NMMTR_CODAGE_ANNEE)
               OR (i.NMMTR_CODAGE_ANNEE IS NULL AND d.NMMTR_CODAGE_ANNEE IS NOT NULL)
               OR (i.NMMTR_CODAGE_ANNEE IS NOT NULL AND d.NMMTR_CODAGE_ANNEE IS NULL)
               OR (i.NMMTR_GESTION_NUM_ERR <> d.NMMTR_GESTION_NUM_ERR)
               OR (i.NMMTR_GESTION_NUM_ERR IS NULL AND d.NMMTR_GESTION_NUM_ERR IS NOT NULL)
               OR (i.NMMTR_GESTION_NUM_ERR IS NOT NULL AND d.NMMTR_GESTION_NUM_ERR IS NULL);
        OPEN cur;
        FETCH NEXT FROM cur INTO @cg, @type, @structN, @structO, @codN, @codO, @gestN, @gestO;
        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- greffe NULL (sequences 'nationales' sans greffe) = HORS perimetre certif par greffe -> ignore.
            IF @cg IS NOT NULL
            BEGIN
                IF dbo.fn_CertifNF525Active() = 1
                BEGIN
                    SET @info = LEFT(N'Chgt mode sequence continue;g=' + @cg + N';typemetier=' + ISNULL(@type,N'?') + N';struct=' + ISNULL(@structN,N'?') + N';gnum=' + ISNULL(CONVERT(nvarchar(1), @gestN),N'?'), 200);
                    EXEC dbo.SP_INSERT_JET @JetCode=N'10', @JetCodeOp=@login, @JetInfo=@info, @JetCodeCaiss=@poste;
                END
            END
            FETCH NEXT FROM cur INTO @cg, @type, @structN, @structO, @codN, @codO, @gestN, @gestO;
        END
        CLOSE cur; DEALLOCATE cur;
    END TRY
    BEGIN CATCH
        IF CURSOR_STATUS('local','cur') >= -1 BEGIN CLOSE cur; DEALLOCATE cur; END;
        THROW;  -- l'instruction precedente DOIT finir par ';' (regle T-SQL du THROW)
    END CATCH
END



