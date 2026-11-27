-- Jeu de LecteurModelesRedaction / EcrivainModelesRedaction (« Modèles de rédaction », PROC_REDAA), lot 15 du plan-676a4b19. Codes
-- « L15_... ». Ce qui est retenu ou ecarte, et pourquoi : .attendu, .recherches ; les gestes : .gestes. Types : ceux de la base V3 de dev
-- (INFORMATION_SCHEMA, recopie de RIG_DEV, lue le 27/11/2026), colonnes lues. MAQUETTE est creee par PROC_RECHERCHE_GED.sql (joue avant)
-- sans libelle, avec la maquette L6_FICHE_CLIENT de type « RIG » (ecartee : seul le type « REDA » est lu) ; ce jeu lui ajoute
-- MQTT_LIBELLE. Aucun autre jeu ne cree les autres tables. COMPOSITION_MODELE sans IDENTITY (la base de dev en a une) : l'ecrivain
-- passe par GesteEnCours.InsererAsync, qui prend l'une ou MAX + 1.
IF OBJECT_ID(N'v2.MAQUETTE') IS NULL
    CREATE TABLE v2.MAQUETTE (MQTT_CODE_MAQUETTE varchar(40) NOT NULL PRIMARY KEY, MQTT_NUM_DOCUMENT varchar(40) NULL, MQTT_TYPE_MAQUETTE varchar(30) NULL);
IF COL_LENGTH('v2.MAQUETTE', 'MQTT_LIBELLE') IS NULL ALTER TABLE v2.MAQUETTE ADD MQTT_LIBELLE varchar(32) NULL;
IF OBJECT_ID(N'v2.MODELE_DOCUMENT') IS NULL
    CREATE TABLE v2.MODELE_DOCUMENT (MDDCM_CODE_MODELE_DOCUMENT varchar(30) NOT NULL PRIMARY KEY, MDDCM_LIBELLE varchar(70) NOT NULL,
        MDDCM_TYPE_MODELE_DOCUMENT varchar(10) NULL, MDDCM_ACTIF bit NOT NULL);
IF OBJECT_ID(N'v2.COMPOSITION_MODELE') IS NULL
    CREATE TABLE v2.COMPOSITION_MODELE (X_CMPMD_ID_CMPMD int NOT NULL PRIMARY KEY, X_CMPMD_CODE_MODELE_DOCUMENT varchar(30) NOT NULL,
        X_CMPMD_CODE_MAQUETTE varchar(40) NOT NULL, X_CMPMD_INDEX smallint NOT NULL);
IF OBJECT_ID(N'v2.SECTION_MAQUETTE') IS NULL
    CREATE TABLE v2.SECTION_MAQUETTE (SCTMQ_CODE_SECTION_MAQUETTE varchar(32) NOT NULL PRIMARY KEY, SCTMQ_LIBELLE varchar(70) NOT NULL,
        SCTMQ_REQUETE_SQL varchar(8000) NOT NULL, SCTMQ_NIVEAU_OCCURRENCE int NOT NULL, SCTMQ_NIVEAU_FUSION int NOT NULL,
        SCTMQ_NIVEAU_GENERATION int NOT NULL, SCTMQ_NIVEAU_PERSISTANCE int NOT NULL);
-- La liste des types d'un modele (ULT_COMBO_TYPE_MODELE_DOCUMENT : TYPE_MODELE_DOCUMENT), ses colonnes lues seulement.
IF OBJECT_ID(N'v2.TYPE_MODELE_DOCUMENT') IS NULL
    CREATE TABLE v2.TYPE_MODELE_DOCUMENT (R_TYPEMODELE_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_TYPEMODELE_LIBELLE_ECRAN varchar(35) NOT NULL,
        R_TYPEMODELE_UTILISABLE_SAISIE bit NOT NULL);
IF OBJECT_ID(N'v2.GLOSSAIRE') IS NULL
    CREATE TABLE v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE varchar(20) NOT NULL PRIMARY KEY, GLOSS_LIBELLE varchar(70) NULL, GLOSS_TEXTE varchar(8000) NULL,
        GLOSS_PROPRIETAIRE varchar(35) NULL);

-- Les trois types de la base de dev (CX, PC, PR).
INSERT INTO v2.TYPE_MODELE_DOCUMENT (R_TYPEMODELE_VALEUR, R_TYPEMODELE_LIBELLE_ECRAN, R_TYPEMODELE_UTILISABLE_SAISIE) VALUES
    ('CX', 'Jugement de contentieux', 1), ('PC', 'Jugement de procédure collective', 1), ('PR', 'Jugement de prévention', 1);
INSERT INTO v2.MODELE_DOCUMENT (MDDCM_CODE_MODELE_DOCUMENT, MDDCM_LIBELLE, MDDCM_TYPE_MODELE_DOCUMENT, MDDCM_ACTIF) VALUES
    -- deux maquettes : « Enregistré » ; sans maquette : « À compléter » (jumeau)
    ('L15_JGT', 'Jugement banc', 'PC', 1), ('L15_ORD', 'Ordonnance banc', 'CX', 1);
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
-- Le document Word de L15_LIBRE dans GED_RIG (creee par PROC_IMAGE.sql, joue avant) : supprimer la maquette supprime aussi cette
-- ligne (OPE_EDT_MAQ.cs:157-159), ce que le dernier bloc de PROC_REDAA.gestes controle. Il entre dans « Documents », categorie
-- « Maquettes » (PROC_RECHERCHE_GED.attendu). Les autres maquettes du jeu n'ont pas de document en GED.
INSERT INTO v2.GED_RIG (GDRG_NUM_DOCUMENT, GDRG_EXTENSION, GDRG_FICHIER, GDRG_LOGIN_NT_ARCHIVAGE, GDRG_DATE_ARCHIVAGE, GDRG_REFERENCE, GDRG_NOM_FICHIER)
VALUES ('L15-DOC-3', 'doc', 0xD0CF11E0, 'banc', '20250314', 'MAQUETTE.MQTT_NUM_DOCUMENT', '$maq_L15_LIBRE.doc');
INSERT INTO v2.SECTION_MAQUETTE (SCTMQ_CODE_SECTION_MAQUETTE, SCTMQ_LIBELLE, SCTMQ_REQUETE_SQL, SCTMQ_NIVEAU_OCCURRENCE, SCTMQ_NIVEAU_FUSION,
                                 SCTMQ_NIVEAU_GENERATION, SCTMQ_NIVEAU_PERSISTANCE) VALUES
    ('L15_MOTIFS', 'Motifs banc', 'SELECT 1 AS MOTIF', 1, 1, 0, 0);
INSERT INTO v2.GLOSSAIRE (GLOSS_CODE_GLOSSAIRE, GLOSS_LIBELLE, GLOSS_TEXTE, GLOSS_PROPRIETAIRE) VALUES
    -- public ('*') : retenu
    ('L15_ART', 'Article banc', 'Texte du glossaire', '*'),
    -- ECARTE : prive d'un agent (la V2 ne le montre qu'a lui) -- jumeau L15_ART
    ('L15_PRIV', 'Prive banc', 'Texte prive', 'BANC');

-- L'agent qui ECRIT les sections (PROC_REDAA.gestes, blocs « agent: banc.amitel.l15 ») : la V2 les reserve a l'equipe Amitel
-- (OPE_EDT_SECTION.cs:64-72), ce que l'agent du banc n'est pas -- ses refus sont les blocs joues sans « agent: » -- et que le rendre
-- changerait l'agent de tous les autres jeux : un second compte, Amitel (pas administrateur), admis comme lui (meme service, role
-- BANC), comme l'etait banc.admin.l15. Sans IdV2 : aucun lecteur ne le montre.
IF NOT EXISTS (SELECT 1 FROM soc.Compte WHERE CompteWindows = 'banc.amitel.l15')
BEGIN
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
    SELECT 'banc.amitel.l15', 'AMITELL15', 'Banc', 'BANCA15', 'M.', CodeGreffe, IdService, 0, 1, 0 FROM soc.Compte WHERE CompteWindows = 'banc.ecrans';
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte WHERE CompteWindows = 'banc.amitel.l15';
END
