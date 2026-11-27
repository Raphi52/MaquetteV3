-- Jeu de LecteurFacturationExpresso (« Facturation Expresso », PROC_PARAMEXPRESSO), lot 15 du plan-676a4b19. Ce qui est retenu, et
-- pourquoi : .attendu. Un parametre dans chacune des deux sources de la V3 (SOC-003) : EXPRESSO_RELANCE dans soc.Reglage (la table
-- PARAMETRAGE de la V2), GRPAR_ROMPU dans soc.ParametreGreffe (une colonne de GREFFE_PARAMETRE, valeur typee money). Aucun autre jeu
-- ne pose ces cles. Les modes de paiement et leurs banques sont ceux de PROC_GESTMODEPAIEMENT.sql (joue avant).
IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'EXPRESSO_RELANCE')
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_RELANCE', 'Banc EcransGeneresBase lot 15', '15.00');
IF NOT EXISTS (SELECT 1 FROM soc.ParametreGreffe WHERE Colonne = 'GRPAR_ROMPU')
    INSERT INTO soc.ParametreGreffe (Colonne, Valeur, TypeV2) VALUES ('GRPAR_ROMPU', CAST(0.50 AS money), 'money');
