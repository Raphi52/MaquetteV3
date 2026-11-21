-- SOC-007 CA06 et SOC-016 CA01 : une modification du catalogue commun des greffes (date et fuseau de
-- passage) s'ecrit au journal des gestes de la base commune, dans sa transaction (lot 9 du plan,
-- Serveur\Socle\Greffes\ModuleGreffes.cs). Sa famille, a cote de « reglage » (SOC-016_gestes.sql) qui
-- recoit les modifications d'un reglage dans la base du greffe.
-- apres: SOC-016, SOC-007_catalogue
--
-- Duree de conservation : 6 ans par defaut, comme les autres familles hors facturation (a fixer par
-- le referent Socle et le delegue a la protection des donnees, SOC-016-T05).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('greffe', 'Fiche du greffe (catalogue commun)', 0, 6);
