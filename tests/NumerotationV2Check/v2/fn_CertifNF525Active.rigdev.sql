/* Fonction canonique d'activation NF525 (nouvelle philosophie).
   Source de verite UNIQUE : la ligne PARAMETRAGE 'ACTIVATION_CERTIF_NF525' (PARAM_VALEUR='1' -> actif).
   Remplace les anciennes lectures directes de la colonne INFO_GREFFE.CERTIF_NF525 (supprimee) dans les triggers/SP (mutualisation :
   1 seul endroit a maintenir). PARAMETRAGE est global a la base (1 base = 1 greffe) -> pas de parametre greffe.
   Retourne bit : 1 = certif active, 0 = inactive (ou ligne absente). Deterministe, SANS effet de bord. */

CREATE   FUNCTION [dbo].[fn_CertifNF525Active]()
RETURNS bit
AS
BEGIN
    RETURN CAST(ISNULL((SELECT MAX(CASE WHEN LTRIM(RTRIM(PARAM_VALEUR)) = '1' THEN 1 ELSE 0 END)
                        FROM dbo.PARAMETRAGE WHERE PARAM_CODE = 'ACTIVATION_CERTIF_NF525'), 0) AS bit);
END


