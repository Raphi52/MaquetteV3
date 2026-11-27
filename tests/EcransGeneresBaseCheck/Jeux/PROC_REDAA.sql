-- Jeu de LecteurModelesRedaction (« Modèles de rédaction », PROC_REDAA), lot 15 du plan-676a4b19. Codes « L15_... ».
-- Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches. Types : ceux de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le
-- 27/11/2026), colonnes lues. MAQUETTE est creee par PROC_RECHERCHE_GED.sql (joue avant) sans libelle, avec la maquette L6_FICHE_CLIENT
-- de type « RIG » (ecartee : seul le type « REDA » est lu) ; ce jeu lui ajoute MQTT_LIBELLE. Aucun autre jeu ne cree les autres tables.
IF OBJECT_ID(N'v2.MAQUETTE') IS NULL
    CREATE TABLE v2.MAQUETTE (MQTT_CODE_MAQUETTE varchar(40) NOT NULL PRIMARY KEY, MQTT_NUM_DOCUMENT varchar(40) NULL, MQTT_TYPE_MAQUETTE varchar(30) NULL);
IF COL_LENGTH('v2.MAQUETTE', 'MQTT_LIBELLE') IS NULL ALTER TABLE v2.MAQUETTE ADD MQTT_LIBELLE varchar(32) NULL;
IF OBJECT_ID(N'v2.MODELE_DOCUMENT') IS NULL
    CREATE TABLE v2.MODELE_DOCUMENT (MDDCM_CODE_MODELE_DOCUMENT varchar(30) NOT NULL PRIMARY KEY, MDDCM_LIBELLE varchar(70) NULL,
        MDDCM_TYPE_MODELE_DOCUMENT varchar(10) NULL, MDDCM_ACTIF bit NULL);
IF OBJECT_ID(N'v2.COMPOSITION_MODELE') IS NULL
    CREATE TABLE v2.COMPOSITION_MODELE (X_CMPMD_ID_CMPMD int NOT NULL PRIMARY KEY, X_CMPMD_CODE_MODELE_DOCUMENT varchar(30) NOT NULL,
        X_CMPMD_CODE_MAQUETTE varchar(40) NOT NULL, X_CMPMD_INDEX smallint NULL);
IF OBJECT_ID(N'v2.SECTION_MAQUETTE') IS NULL
    CREATE TABLE v2.SECTION_MAQUETTE (SCTMQ_CODE_SECTION_MAQUETTE varchar(32) NOT NULL PRIMARY KEY, SCTMQ_LIBELLE varchar(70) NULL);
IF OBJECT_ID(N'v2.GLOSSAIRE') IS NULL
    CREATE TABLE v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE varchar(20) NOT NULL PRIMARY KEY, GLOSS_LIBELLE varchar(70) NULL, GLOSS_TEXTE varchar(8000) NULL,
        GLOSS_PROPRIETAIRE varchar(35) NULL);

INSERT INTO v2.MODELE_DOCUMENT (MDDCM_CODE_MODELE_DOCUMENT, MDDCM_LIBELLE, MDDCM_TYPE_MODELE_DOCUMENT, MDDCM_ACTIF) VALUES
    -- deux maquettes : « Enregistré » ; sans maquette : « À compléter » (jumeau)
    ('L15_JGT', 'Jugement banc', 'REDA', 1), ('L15_ORD', 'Ordonnance banc', 'REDA', 1);
INSERT INTO v2.COMPOSITION_MODELE (X_CMPMD_ID_CMPMD, X_CMPMD_CODE_MODELE_DOCUMENT, X_CMPMD_CODE_MAQUETTE, X_CMPMD_INDEX) VALUES
    (2500601, 'L15_JGT', 'L15_ENTETE', 1), (2500602, 'L15_JGT', 'L15_DISPO', 2);
-- Les colonnes ajoutees ci-dessus ne sont connues qu'a l'execution : l'insertion des maquettes passe par sp_executesql.
EXEC sp_executesql N'
INSERT INTO v2.MAQUETTE (MQTT_CODE_MAQUETTE, MQTT_NUM_DOCUMENT, MQTT_TYPE_MAQUETTE, MQTT_LIBELLE) VALUES
    -- maquette REDA utilisee par L15_JGT, avec son document Word : « Enregistré »
    (''L15_ENTETE'', ''L15-DOC-1'', ''REDA'', ''Entete banc''),
    -- sans document Word : « À compléter » -- jumeau L15_ENTETE
    (''L15_DISPO'', NULL, ''REDA'', ''Dispositif banc''),
    -- utilisee par aucun modele : supprimable dans la V2 (OPE_EDT_MAQ.cs:153), « Enregistré »
    (''L15_LIBRE'', ''L15-DOC-3'', ''REDA'', ''Libre banc''),
    -- ECARTEE : maquette d''un autre usage (type RIG) -- jumeau L15_LIBRE
    (''L15_RIG'', ''L15-DOC-4'', ''RIG'', ''Courrier banc'');';
INSERT INTO v2.SECTION_MAQUETTE (SCTMQ_CODE_SECTION_MAQUETTE, SCTMQ_LIBELLE) VALUES ('L15_MOTIFS', 'Motifs banc');
INSERT INTO v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE) VALUES
    -- public ('*') : retenu
    ('L15_ART', 'Article banc', 'Texte du glossaire', '*'),
    -- ECARTE : prive d'un agent (la V2 ne le montre qu'a lui) -- jumeau L15_ART
    ('L15_PRIV', 'Prive banc', 'Texte prive', 'BANC');
