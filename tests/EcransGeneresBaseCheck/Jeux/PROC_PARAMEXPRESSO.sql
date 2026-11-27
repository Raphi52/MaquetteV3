-- Jeu de LecteurFacturationExpresso (« Facturation Expresso », PROC_PARAMEXPRESSO), lot 15 du plan-676a4b19. Ce qui est retenu, et
-- pourquoi : .attendu. Un parametre dans chacune des deux sources de la V3 (SOC-003) : EXPRESSO_RELANCE dans soc.Reglage (la table
-- PARAMETRAGE de la V2), GRPAR_ROMPU dans soc.ParametreGreffe (une colonne de GREFFE_PARAMETRE, valeur typee money). Aucun autre jeu
-- ne pose ces cles ; GRPAR_ROMPU vaut le DEFAULT de RIG_DEV (1,52, DF_GREFFE_PARAMETRE_GRPAR_ROMPU) : le releve client (PROC_RELCLI,
-- joue apres) le lit aussi et prend 1,52 sans lui. Les modes de paiement et leurs banques sont ceux de PROC_GESTMODEPAIEMENT.sql
-- (joue avant) et des jeux du lot 12.
-- Les reglages que .gestes enregistre (EcrivainFacturationExpresso) : EXPRESSO_RELANCE au format des montants de RIG_DEV (« 1,30 » :
-- decimal.ToString() en culture francaise), la case EXPRESSO_EDITIONS_SYNTHETIQUES a « 0 », EXPRESSO_MODE_PAIEMENT_PROFIT vide, et le
-- texte EXPRESSO_RELEVE_MAIL_COPIE vide (la borne des 1 000 caracteres de l'ecriture des reglages, blocs 12 a 14 ; lot 14).
-- EXPRESSO_MODE_PAIEMENT_PERTE n'y est pas : la V3 ne cree pas de reglage (refus du bloc 7 de .gestes).
IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'EXPRESSO_RELANCE')
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_RELANCE', 'Banc EcransGeneresBase lot 15', '15,00');
IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'EXPRESSO_EDITIONS_SYNTHETIQUES')
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_EDITIONS_SYNTHETIQUES', 'Banc EcransGeneresBase lot 15', '0');
IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'EXPRESSO_MODE_PAIEMENT_PROFIT')
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_MODE_PAIEMENT_PROFIT', 'Banc EcransGeneresBase lot 15', '');
IF NOT EXISTS (SELECT 1 FROM soc.Reglage WHERE Code = 'EXPRESSO_RELEVE_MAIL_COPIE')
    INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('EXPRESSO_RELEVE_MAIL_COPIE', 'Banc EcransGeneresBase lot 14', '');
IF NOT EXISTS (SELECT 1 FROM soc.ParametreGreffe WHERE Colonne = 'GRPAR_ROMPU')
    INSERT INTO soc.ParametreGreffe (Colonne, Valeur, TypeV2) VALUES ('GRPAR_ROMPU', CAST(1.52 AS money), 'money');
