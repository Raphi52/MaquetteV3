-- Jeu de LecteurDestinatairesInstitutionnels (« Destinataires institutionnels », PROC_GDEST_INST), lot 3 du plan « ecrans
-- retrouves branches sur la base ». Codes B3xxx (lot 3). Ce qui est retenu ou ecarte, et pourquoi : PROC_GDEST_INST.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues.
IF OBJECT_ID(N'v2.DESTINATAIRE_INSTITUTIONNEL') IS NULL
    CREATE TABLE v2.DESTINATAIRE_INSTITUTIONNEL (DTINS_CODE varchar(10) NOT NULL PRIMARY KEY, DTINS_LIBELLE varchar(100) NOT NULL,
        DTINS_BLOC_ADRESSE varchar(150) NULL, DTINS_TYPE varchar(10) NOT NULL, DTINS_CIVILITE_DEVELOPPEE varchar(500) NULL,
        DTINS_FORMULE_POLITESSE varchar(1000) NULL, DTINS_ADRESSE_MAIL varchar(1000) NULL, DTINS_NOM_COFFRE varchar(100) NULL,
        DTINS_DATE_DEMARRAGE_COFFRE datetime NULL, DTINS_ACTIF bit NOT NULL);
IF OBJECT_ID(N'v2.ENTITE') IS NULL
    CREATE TABLE v2.ENTITE (ENTT_CODE_ENTITE varchar(16) NOT NULL PRIMARY KEY, ENTT_LIBELLE varchar(32) NOT NULL);

-- Les types (ENTITE INST_<type>, libelles de RIG_DEV) ; le type ZZB3 n'en a pas : son code tient lieu de libelle.
INSERT INTO v2.ENTITE (ENTT_CODE_ENTITE, ENTT_LIBELLE)
SELECT c, l FROM (VALUES ('INST_PROC', 'Procureur'), ('INST_DDFP', 'Directeur départemental finances'),
                         ('INST_DESTPR', 'Enquête prévention'), ('INST_CC', 'Chambre de Commerce'), ('INST_BOD', 'Bodacc')) AS t(c, l)
WHERE NOT EXISTS (SELECT 1 FROM v2.ENTITE AS e WHERE e.ENTT_CODE_ENTITE = t.c);

INSERT INTO v2.DESTINATAIRE_INSTITUTIONNEL (DTINS_CODE, DTINS_LIBELLE, DTINS_BLOC_ADRESSE, DTINS_TYPE, DTINS_CIVILITE_DEVELOPPEE,
    DTINS_FORMULE_POLITESSE, DTINS_ADRESSE_MAIL, DTINS_NOM_COFFRE, DTINS_DATE_DEMARRAGE_COFFRE, DTINS_ACTIF) VALUES
    -- procureur complet : courrier et coffre-fort : Validee [0,1,2], « Parquet et juridictions »
    ('B3PARQ', 'Parquet du tribunal judiciaire', '1 place du Palais 38000 Grenoble', 'PROC', 'Monsieur le Procureur', NULL, NULL,
     'COFFRE_PARQUET', DATEADD(DAY, -100, @aujourdhui), 1),
    -- procureur SANS adresse, formule de politesse saisie : identite incomplete, une marche : EnCours [1]
    -- -- jumeau B3PARQ (adresse renseignee, Validee)
    ('B3NOADR', 'Cour d''appel', '', 'PROC', NULL, 'Veuillez agréer...', NULL, NULL, NULL, 1),
    -- finances publiques avec courriel : Validee [0,2], « Administrations »
    ('B3DDFP', 'Direction des finances publiques', '8 rue des Finances 38000 Grenoble', 'DDFP', NULL, NULL, 'ddfip@exemple.test',
     NULL, NULL, 1),
    -- destinataire de l'enquete de prevention, adresse seule : Validee [0], « Administrations »
    ('B3URSSAF', 'Urssaf', '3 avenue Sociale 38000 Grenoble', 'DESTPR', NULL, NULL, NULL, NULL, NULL, 1),
    -- chambre de commerce sans adresse (NULL) ni rien d'autre : AFaire [], « Organismes » -- jumeau B3NOADR (meme
    -- identite incomplete, une marche franchie : EnCours)
    ('B3VIDE', 'Chambre de commerce', NULL, 'CC', NULL, NULL, NULL, NULL, NULL, 1),
    -- inactif (DTINS_ACTIF = 0), complet : EnAttente [0] -- jumeau B3URSSAF (actif, Validee)
    ('B3INACT', 'Bodacc', '26 rue Desaix 75015 Paris', 'BOD', NULL, NULL, NULL, NULL, NULL, 0),
    -- type cree par le greffe, sans ENTITE : « Organismes », le code du type pour libelle, coffre-fort sans date
    ('B3AUTRE', 'Notaires du ressort', '5 rue des Notaires 38000 Grenoble', 'ZZB3', NULL, NULL, NULL, 'COFFRE_NOTAIRES', NULL, 1);

-- Lot 16 du plan « RIG v3 100 % fonctionnel » (EcrivainDestinatairesInstitutionnels, PROC_GDEST_INST.gestes) : l'historique que
-- la V2 ecrit a chaque ajout ou modification et efface avec le destinataire (DestinataireInstitutionnelHisto, cle DIHIS_ID_DIHIS
-- auto-incrementee : HaveAutoIdentity ; longueurs de BASE_GREFFE_DESTINATAIRE_INSTITUTIONNEL_HISTO : code 10, login 50, action
-- sans limite), et une ligne d'historique de B3VIDE, que le dernier geste supprime avec lui.
IF OBJECT_ID(N'v2.DESTINATAIRE_INSTITUTIONNEL_HISTO') IS NULL
    CREATE TABLE v2.DESTINATAIRE_INSTITUTIONNEL_HISTO (DIHIS_ID_DIHIS int IDENTITY(1, 1) NOT NULL PRIMARY KEY,
        DIHIS_CODE varchar(10) NOT NULL, DIHIS_LOGIN varchar(50) NULL, DIHIS_DATE datetime NULL, DIHIS_ACTION varchar(max) NULL);
INSERT INTO v2.DESTINATAIRE_INSTITUTIONNEL_HISTO (DIHIS_CODE, DIHIS_LOGIN, DIHIS_DATE, DIHIS_ACTION)
VALUES ('B3VIDE', 'ancien.agent', DATEADD(DAY, -30, @aujourdhui), 'Insertion');
