-- Jeu de LecteurLettresRecommandees / EcrivainLettresRecommandees (« Livre de répertoire », PROC_JLR), lot 13 du plan-676a4b19.
-- Ce qui est retenu ou ecarte, et pourquoi : PROC_JLR.attendu et .gestes.
-- AUCUNE demande d'edition posee ici : toute demande d'edition entre dans la « File des éditions » (LecteurFileEditions, sans filtre),
-- dont PROC_POOLEDITION.attendu fixe les lignes. La liste du banc part donc vide ; les lettres sont creees par le geste
-- (PROC_JLR.gestes, joue apres tous les .attendu) puis relues.
-- Les tables : DEMANDE (le banc, colonnes de PROC_COURRIERM.sql et des autres jeux), DEMANDE_EDITION et PARAMETRE_EDITION (definition de
-- PROC_BORDLR.sql, joue avant), completees ici de ce que la lettre ecrit (types de RIG_DEV, lus le 27/11/2026).
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NULL;
IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_BLOC_ADRESSE_DESTINATAIRE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_BLOC_ADRESSE_DESTINATAIRE varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_LIBELLE_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_LIBELLE_SOURCE varchar(max) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_ENTITE_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_ENTITE_SOURCE varchar(16) NULL;
IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_VALEUR_ID_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_VALEUR_ID_SOURCE varchar(255) NULL;
-- Le parametre d'edition JLR de RIG_DEV (PARAMETRE_EDITION, lu le 27/11/2026) : « Imprimé LR La Poste », FUSION_RIG. JLRAR (« Imprimé
-- LRAR La Poste ») est pose par PROC_SURVIP.sql, a la liste de RIG_DEV : joue apres celui-ci, avant les gestes, il ne se double pas ici.
EXEC sp_executesql N'
INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_TYPE_GENERATION)
SELECT ''JLR'', ''Imprimé LR La Poste'', ''FUSION_RIG'' WHERE NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION AS p WHERE p.PRMED_CODE = ''JLR'');';
