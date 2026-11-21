-- Le pont côté V3 : ce que la bascule pose dans la base V3 du greffe (lot 7, NUM-006 ; 2026-11-21).
-- Joué par bascule.sql, base V3 courante, dans sa transaction ; variables sqlcmd $(BaseV2), $(CodeGreffe).
--
-- NUM-006 CA01 : toute attribution, restitution, création d'année ou de jour faite dans num.Compteur et
-- num.NumeroRendu -- pour la V2 par ses programmes remplacés, ou pour la V3 elle-même -- est recopiée
-- dans NUMERO_METIER de la V2, DANS LA MÊME TRANSACTION (déclencheurs) : les écrans et traitements de
-- la V2 qui lisent cette table y trouvent les valeurs de la V3. Une transaction annulée l'est des deux côtés.
--
--   num.NumeroMetierV2          synonyme vers [$(BaseV2)].dbo.NUMERO_METIER (même instance, Q4)
--   num.EcartsV2                ce qui diffère entre les deux : la bascule et le retour s'y arrêtent
--   num.TR_Compteur_RecopieV2   la ligne « compteur » de la V2 : chrono, date, réglages, nouvelle année ou jour
--   num.TR_NumeroRendu_RecopieV2 les lignes « attribution erronée » de la V2 : ajoutées, effacées quand redonnées
--
-- Correspondance (celle de docs\reconstruction\numerotation-compteur.md, dans l'autre sens) :
--   num.Compteur (Type, Annee, Jour)  = lignes NMMTR_ATTRIBUTION_ERRONEE = 0 de même type et année, et de
--                                       même quantième pour une série par jour (« Q » dans la structure) ;
--   Chrono NULL (rien donné)          = NMMTR_CHRONO 0, la « sentinelle » de la V2 ;
--   num.NumeroRendu                   = ligne NMMTR_ATTRIBUTION_ERRONEE = 1, quantième NULL hors série par
--                                       jour (comme RESTITUER_NUMERO.sql:114-123).
--   série que la V2 ne connaît pas    = aucune ligne : c'est une série de la V3 seule (FACV3, BDFNI, que
--     (aucune ligne compteur du type)   num.Reprendre ouvre au passage) ; elle n'a pas de « ligne
--                                       correspondante » dans la V2 (NUM-006 CA01), et num.Reprendre refuse
--                                       une base V2 qui porte ces types (NUM-005_reprise.sql:631-632).
-- fix-ok: la recopie écrivait FACV3 et BDFNI dans NUMERO_METIER (première facture de la V3, puis retour :
-- 1 puis 2 lignes, mesuré au banc NumerotationV2Check le 2026-11-21 avec la vraie reprise) ; la base V2 ne
-- pouvait plus être reprise. Le type inconnu de la V2 est désormais écarté de la recopie et de num.EcartsV2.
--
-- Écrire dans la V2 demande au compte qui numérote (celui de la V2, celui du serveur V3) le droit
-- d'écrire NUMERO_METIER dans la base V2 : la chaîne de propriété ne passe pas d'une base à l'autre.

IF OBJECT_ID(N'num.TR_Compteur_RenduALaV2', N'TR') IS NOT NULL DROP TRIGGER num.TR_Compteur_RenduALaV2;
IF OBJECT_ID(N'num.TR_NumeroRendu_RenduALaV2', N'TR') IS NOT NULL DROP TRIGGER num.TR_NumeroRendu_RenduALaV2;
IF OBJECT_ID(N'num.NumeroMetierV2', N'SN') IS NOT NULL DROP SYNONYM num.NumeroMetierV2;
CREATE SYNONYM num.NumeroMetierV2 FOR [$(BaseV2)].dbo.NUMERO_METIER;
GO

-- Les écarts entre les compteurs de la V3 et la table de la V2, avec la clé de la reprise (NUM-005, lot 6) :
-- une ligne V2 d'une série par jour sans quantième (ARRC 2001 dans RIG_DEV, ligne de réglage qui porte la
-- redirection) est le compteur Jour 0 ; deux lignes V2 pour une même clé (ARRD 2014, 2018) : leur plus
-- grand chrono (réponse du lot 6, msg-600d98e6, 2026-11-21).
-- fix-ok: la reprise du lot 6 met ARRC 2001 (quantième NULL) au Jour 0 ; la première version de la vue
-- l'écartait, le retour l'aurait compté « compteur V3 absent de la V2 » et refusé.
CREATE OR ALTER VIEW num.EcartsV2
AS
WITH V2 AS (
    SELECT NMMTR_TYPE_METIER AS Type, NMMTR_ANNEE AS Annee,
           CASE WHEN CHARINDEX('Q', NMMTR_STRUCTURE_NUMERO) > 0 THEN ISNULL(NMMTR_QUANTIEME, 0) ELSE 0 END AS Jour,
           MAX(NMMTR_CHRONO) AS Chrono
    FROM num.NumeroMetierV2
    WHERE NMMTR_ATTRIBUTION_ERRONEE = 0
    GROUP BY NMMTR_TYPE_METIER, NMMTR_ANNEE,
             CASE WHEN CHARINDEX('Q', NMMTR_STRUCTURE_NUMERO) > 0 THEN ISNULL(NMMTR_QUANTIEME, 0) ELSE 0 END)
SELECT CAST(CASE WHEN c.Type IS NULL THEN 'compteur V2 absent de la V3'
                 WHEN v.Type IS NULL THEN 'compteur V3 absent de la V2'
                 ELSE 'chrono different' END AS varchar(30)) AS Ecart,
       ISNULL(c.Type, v.Type) AS Type, ISNULL(c.Annee, v.Annee) AS Annee, ISNULL(c.Jour, v.Jour) AS Jour,
       v.Chrono AS ChronoV2, c.Chrono AS ChronoV3
FROM V2 AS v
FULL JOIN num.Compteur AS c ON c.Type = v.Type AND c.Annee = v.Annee AND c.Jour = v.Jour
WHERE c.Type IS NULL OR v.Chrono <> ISNULL(c.Chrono, 0)
   OR (v.Type IS NULL AND EXISTS (SELECT 1 FROM V2 AS k WHERE k.Type = c.Type))     -- une série de la V3 seule n'est pas un écart
UNION ALL
SELECT CAST('rendu V3 absent de la V2' AS varchar(30)), r.Serie, r.Annee, r.Jour, NULL, r.Chrono
FROM num.NumeroRendu AS r
WHERE EXISTS (SELECT 1 FROM V2 AS k WHERE k.Type = r.Serie)
  AND NOT EXISTS (SELECT 1 FROM num.NumeroMetierV2 AS v
                  WHERE v.NMMTR_ATTRIBUTION_ERRONEE = 1 AND v.NMMTR_TYPE_METIER = r.Serie AND v.NMMTR_ANNEE = r.Annee
                    AND v.NMMTR_CHRONO = r.Chrono AND (r.Jour = 0 OR v.NMMTR_QUANTIEME = r.Jour));
GO

CREATE OR ALTER TRIGGER num.TR_Compteur_RecopieV2 ON num.Compteur
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;
    -- La garde de la V2 (dbo.TR_NUMERO_METIER_RIGV3) laisse passer la recopie, et elle seule.
    DECLARE @Avant sql_variant = SESSION_CONTEXT(N'rigv3.recopie');
    EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = 1;
    BEGIN TRY
        -- 1. Le chrono et la date : ce que fait chaque attribution, sans toucher au mode de la série --
        --    le déclencheur NF525 de la V2 n'a donc rien à écrire (NUM-006 CA03).
        IF UPDATE(Chrono) OR UPDATE(DerniereAttribution)
            UPDATE v
            SET NMMTR_CHRONO = ISNULL(i.Chrono, 0),
                NMMTR_DATE_ATTRIBUTION = ISNULL(CAST(i.DerniereAttribution AS smalldatetime), v.NMMTR_DATE_ATTRIBUTION)
            FROM num.NumeroMetierV2 AS v
            JOIN inserted AS i
              ON v.NMMTR_TYPE_METIER = i.Type AND v.NMMTR_ANNEE = i.Annee AND v.NMMTR_ATTRIBUTION_ERRONEE = 0
             AND (i.Jour = 0 OR v.NMMTR_QUANTIEME = i.Jour)
            WHERE v.NMMTR_CHRONO <> ISNULL(i.Chrono, 0)
               OR (i.DerniereAttribution IS NOT NULL
                   AND ISNULL(v.NMMTR_DATE_ATTRIBUTION, '19000101') <> CAST(i.DerniereAttribution AS smalldatetime));

        -- 2. Le mode et les réglages de la série, seulement là où ils changent : le déclencheur NF525 de
        --    la V2 écrit alors son événement (code 10) pour un greffe certifié (NUM-006 CA03).
        IF UPDATE(Structure) OR UPDATE(CodageAnnee) OR UPDATE(Lettre) OR UPDATE(Separateur) OR UPDATE(TailleChrono)
           OR UPDATE(CodeGreffe) OR UPDATE(Debut) OR UPDATE(GestionRendus) OR UPDATE(Redirection)
           OR UPDATE(TableCible) OR UPDATE(ColonneCible) OR UPDATE(Libelle)
            UPDATE v
            SET NMMTR_STRUCTURE_NUMERO = i.Structure, NMMTR_CODAGE_ANNEE = i.CodageAnnee,
                NMMTR_LETTRE_CATEGORIE = i.Lettre, NMMTR_SEPARATEUR = i.Separateur, NMMTR_TAILLE_CHRONO = i.TailleChrono,
                NMMTR_CODE_GREFFE = i.CodeGreffe, NMMTR_CHRONO_DEBUT = i.Debut, NMMTR_GESTION_NUM_ERR = i.GestionRendus,
                NMMTR_FILE_REDIRECTION = i.Redirection, NMMTR_TABLE_AFFECTEE = i.TableCible,
                NMMTR_ATTRIBUT_AFFECTE = i.ColonneCible, NMMTR_LIBELLE = i.Libelle
            FROM num.NumeroMetierV2 AS v
            JOIN inserted AS i
              ON v.NMMTR_TYPE_METIER = i.Type AND v.NMMTR_ANNEE = i.Annee AND v.NMMTR_ATTRIBUTION_ERRONEE = 0
             AND (i.Jour = 0 OR v.NMMTR_QUANTIEME = i.Jour)
            WHERE EXISTS (SELECT v.NMMTR_STRUCTURE_NUMERO, v.NMMTR_CODAGE_ANNEE, v.NMMTR_LETTRE_CATEGORIE, v.NMMTR_SEPARATEUR,
                                 v.NMMTR_TAILLE_CHRONO, v.NMMTR_CODE_GREFFE, v.NMMTR_CHRONO_DEBUT, v.NMMTR_GESTION_NUM_ERR,
                                 v.NMMTR_FILE_REDIRECTION, v.NMMTR_TABLE_AFFECTEE, v.NMMTR_ATTRIBUT_AFFECTE, v.NMMTR_LIBELLE
                          EXCEPT
                          SELECT i.Structure, i.CodageAnnee, i.Lettre, i.Separateur, i.TailleChrono, i.CodeGreffe, i.Debut,
                                 i.GestionRendus, i.Redirection, i.TableCible, i.ColonneCible, i.Libelle);

        -- 3. Un compteur que la V2 n'a pas d'une série qu'elle connaît (nouvelle année, nouveau jour) : sa
        --    ligne, comme l'écrivait GENERER_NUMERO.sql:128-137 et 216-224. Hors série par jour, la V2 y
        --    notait le quantième du jour de création : c'est aussi ce qui est écrit ici. Une série de la V3
        --    seule (aucune ligne compteur de ce type dans la V2 : FACV3, BDFNI) n'y est pas écrite.
        INSERT num.NumeroMetierV2 (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_STRUCTURE_NUMERO, NMMTR_CODE_GREFFE, NMMTR_ANNEE,
               NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_TAILLE_CHRONO, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO,
               NMMTR_DATE_ATTRIBUTION, NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_ATTRIBUTION_ERRONEE,
               NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR, NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE)
        SELECT i.Type, i.Libelle, i.Structure, i.CodeGreffe, i.Annee, i.CodageAnnee, i.Lettre, i.TailleChrono, i.Debut,
               ISNULL(i.Chrono, 0), CAST(ISNULL(i.DerniereAttribution, SYSDATETIME()) AS smalldatetime),
               CASE WHEN i.Jour > 0 THEN i.Jour ELSE DATEPART(dayofyear, SYSDATETIME()) END,
               i.GestionRendus, 0, i.Redirection, i.Separateur, i.TableCible, i.ColonneCible
        FROM inserted AS i
        WHERE NOT EXISTS (SELECT 1 FROM num.NumeroMetierV2 AS v
                          WHERE v.NMMTR_TYPE_METIER = i.Type AND v.NMMTR_ANNEE = i.Annee AND v.NMMTR_ATTRIBUTION_ERRONEE = 0
                            AND (i.Jour = 0 OR v.NMMTR_QUANTIEME = i.Jour))
          AND EXISTS (SELECT 1 FROM num.NumeroMetierV2 AS k WHERE k.NMMTR_TYPE_METIER = i.Type AND k.NMMTR_ATTRIBUTION_ERRONEE = 0);
    END TRY
    BEGIN CATCH
        EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = @Avant;
        THROW;
    END CATCH;
    EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = @Avant;
END;
GO

CREATE OR ALTER TRIGGER num.TR_NumeroRendu_RecopieV2 ON num.NumeroRendu
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    DECLARE @Avant sql_variant = SESSION_CONTEXT(N'rigv3.recopie');
    EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = 1;
    BEGIN TRY
        -- 1. Un numéro redonné : ses lignes « attribution erronée » disparaissent de la V2, toutes, comme
        --    l'effaçait GENERER_NUMERO.sql:95-97 et 159-160.
        DELETE v
        FROM num.NumeroMetierV2 AS v
        JOIN deleted AS r
          ON v.NMMTR_ATTRIBUTION_ERRONEE = 1 AND v.NMMTR_TYPE_METIER = r.Serie AND v.NMMTR_ANNEE = r.Annee
         AND v.NMMTR_CHRONO = r.Chrono AND (r.Jour = 0 OR v.NMMTR_QUANTIEME = r.Jour)
        WHERE NOT EXISTS (SELECT 1 FROM inserted AS i
                          WHERE i.Serie = r.Serie AND i.Annee = r.Annee AND i.Jour = r.Jour AND i.Chrono = r.Chrono);

        -- 2. Un numéro rendu : sa ligne dans la V2, avec les réglages de son compteur, comme l'écrivait
        --    RESTITUER_NUMERO.sql:114-123 (une ligne déjà là n'est pas doublée) ; rien pour une série de la
        --    V3 seule, qui n'a pas de ligne dans la V2.
        INSERT num.NumeroMetierV2 (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_CODE_GREFFE, NMMTR_ANNEE, NMMTR_CODAGE_ANNEE,
               NMMTR_LETTRE_CATEGORIE, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_TAILLE_CHRONO, NMMTR_STRUCTURE_NUMERO,
               NMMTR_ATTRIBUTION_ERRONEE, NMMTR_DATE_ATTRIBUTION, NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR,
               NMMTR_EXPLICATION, NMMTR_SEPARATEUR, NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE)
        SELECT r.Serie, c.Libelle, c.CodeGreffe, r.Annee, c.CodageAnnee, c.Lettre, c.Debut, r.Chrono, c.TailleChrono,
               c.Structure, 1, CAST(r.RenduLe AS smalldatetime), NULLIF(r.Jour, 0), c.GestionRendus,
               r.Explication, c.Separateur, c.TableCible, c.ColonneCible
        FROM inserted AS r
        JOIN num.Compteur AS c ON c.Type = r.Serie AND c.Annee = r.Annee AND c.Jour = r.Jour
        WHERE NOT EXISTS (SELECT 1 FROM num.NumeroMetierV2 AS v
                          WHERE v.NMMTR_ATTRIBUTION_ERRONEE = 1 AND v.NMMTR_TYPE_METIER = r.Serie AND v.NMMTR_ANNEE = r.Annee
                            AND v.NMMTR_CHRONO = r.Chrono AND (r.Jour = 0 OR v.NMMTR_QUANTIEME = r.Jour))
          AND EXISTS (SELECT 1 FROM num.NumeroMetierV2 AS k WHERE k.NMMTR_TYPE_METIER = r.Serie AND k.NMMTR_ATTRIBUTION_ERRONEE = 0);
    END TRY
    BEGIN CATCH
        EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = @Avant;
        THROW;
    END CATCH;
    EXEC sys.sp_set_session_context @key = N'rigv3.recopie', @value = @Avant;
END;
GO
