-- SOC-008 CA03 : qui ecrit dans une base V3 (carnet\socle.json, SOC-008, lot 4).
--
-- Seuls la partie serveur de la V3, la recopie (V2 <-> V3) et l'exploitation d'Amitel
-- y ecrivent. Un compte d'agent n'y a AUCUN acces direct : il n'y est pas declare, il ne
-- peut donc ni l'ouvrir ni y ecrire depuis son poste ; il passe par la partie serveur.
--
-- Les roles font partie du schema : toutes les bases V3 les ont, a l'identique
-- (SOC-008 CA02). Qui en est membre se decide a la creation de la base
-- (Serveur\Socle\Greffes\BaseGreffe.cs, CreerAsync : comptes donnes par l'exploitation).
-- Droits poses au niveau de la base : ils valent pour les schemas a venir (num, rec...).
CREATE ROLE rigv3_serveur;
CREATE ROLE rigv3_recopie;
CREATE ROLE rigv3_exploitation;
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE TO rigv3_serveur;
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE TO rigv3_recopie;
ALTER ROLE db_owner ADD MEMBER rigv3_exploitation;
