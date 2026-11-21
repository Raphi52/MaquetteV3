-- SOC-006 T04 (suite) : creer, modifier, activer ou desactiver un modele de courriel et une liste de
-- distribution depuis l'ecran « Messagerie » (procedure V2 PROC_GESTEMAIL, fonction GESTEMAIL ; lot 15 du
-- plan « Reconstruction de RIG -- phase 2 », Serveur\Socle\Courriels\GestionCourriels.cs).
-- Chaque geste s'ecrit au journal des gestes de la base du greffe, dans sa transaction (SOC-016) : sa famille,
-- a cote de « reglage » (SOC-016_gestes.sql). La V2 en gardait la trace par une demande creee deja validee
-- (« Désactivation du modèle mail : … », OPE_EDIT_EMAILMODEL.cs:171-176).
-- apres: SOC-006_courriels, SOC-016
--
-- Duree de conservation : 6 ans par defaut, comme les autres familles hors facturation (a fixer par le referent
-- Socle et le delegue a la protection des donnees, SOC-016-T05).
INSERT INTO soc.FamilleJournal (Famille, Libelle, Facturation, ConservationAns) VALUES
    ('courriel', 'Modele de courriel ou liste de distribution', 0, 6);
