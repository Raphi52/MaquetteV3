-- Jeu de LecteurNouvelleInstance et EcrivainNouvelleInstance (« Nouvelle instance », PROC_XAFF), lot 5 du plan « RIG v3 100 %
-- fonctionnel » (plan-676a4b19). Joue en dernier (ordre des noms) : il n'ajoute AUCUNE instance -- tout type de numero de role
-- de XAFF est lu aussi par d'autres ecrans (JOIN, XXAFF, MJUD, mots directeurs) -- et lit celles des autres jeux (MJUD, JOIN,
-- XXAFF...), qu'il liste dans PROC_XAFF.attendu. Il ajoute seulement les colonnes que la fiche de XAFF lit (OPE_CORRECT_INSTANCE.cs:33-43),
-- aux types de RIG_DEV (copie de dev, INFORMATION_SCHEMA.COLUMNS, 26/11/2026).
IF COL_LENGTH('v2.INSTANCE', 'INSTN_PREMIERE_AUDIENCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_PREMIERE_AUDIENCE datetime NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_TEXTE_ACTE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TEXTE_ACTE varchar(255) NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_DELIVRANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DELIVRANCE varchar(5) NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_NAT_AFF_PART') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NAT_AFF_PART varchar(5) NULL;
IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_MAJ_INFOG') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_MAJ_INFOG datetime NULL;
