-- Jeu de LecteurMandataires (« Mandataires », PROC_MANDATAIRE), lot 5 du plan « brancher sur la base les procedures encore
-- en exemple des 5 ecrans » (plan-f7ba4de2). Identifiants 1 500 1xx a 1 500 9xx (lot 5 : 1 500 000 a 1 599 999), CNBF 1500xx :
-- aucun ne croise les CNBF lus par les ecrans RPJC (3000xx, 1300xx). Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_MANDATAIRE.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues. v2.MANDATAIRE existe deja dans le banc (PROC_EDI_RPJC_REN.sql et PROC_EDI_RPJC_TRCNC.sql,
-- joues avant) : elle recoit les colonnes qui lui manquent (nullables : la table a deja des lignes), et ses lignes passent
-- par sp_executesql -- une colonne ajoutee dans ce meme lot d'instructions n'existe pas encore quand SQL Server le compile.
-- Les 8 mandataires de ces deux jeux entrent aussi dans cet ecran (la V2 liste tous les mandataires) : PROC_MANDATAIRE.attendu
-- les porte.
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL,
        MNDTR_NUMERO_CNBF varchar(6) NULL);
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ABREGE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ABREGE varchar(16) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_CIVILITE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_CIVILITE varchar(7) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ID_ADRBS') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ID_ADRBS int NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ID_CLIEN') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ID_CLIEN int NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_TEXTE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_TEXTE varchar(128) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_QUALIF_PROFESSION') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_QUALIF_PROFESSION varchar(5) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_ADRESSE_MAIL') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_ADRESSE_MAIL varchar(70) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_TELEPHONE') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_TELEPHONE varchar(15) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_FAX') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_FAX varchar(15) NULL;
IF COL_LENGTH('v2.MANDATAIRE', 'MNDTR_SIREN') IS NULL ALTER TABLE v2.MANDATAIRE ADD MNDTR_SIREN varchar(9) NULL;
IF OBJECT_ID(N'v2.CIVILITE') IS NULL
    CREATE TABLE v2.CIVILITE (R_CIVILITE_VALEUR varchar(7) NOT NULL PRIMARY KEY, R_CIVILITE_ABREGE_EDIT varchar(5) NOT NULL);

-- Les civilites de RIG_DEV et leur abrege d'edition (ND n'en a pas ; PM et ND ne precedent jamais le nom).
INSERT INTO v2.CIVILITE (R_CIVILITE_VALEUR, R_CIVILITE_ABREGE_EDIT)
SELECT v, a FROM (VALUES ('ME', 'Me'), ('MR', 'M.'), ('MME', 'Mme'), ('ND', ''), ('PM', 'PM')) AS t(v, a)
WHERE NOT EXISTS (SELECT 1 FROM v2.CIVILITE AS c WHERE c.R_CIVILITE_VALEUR = t.v);

EXEC sp_executesql N'
INSERT INTO v2.MANDATAIRE (MNDTR_ID_MNDTR, MNDTR_CIVILITE, MNDTR_NOM, MNDTR_ABREGE, MNDTR_SIREN, MNDTR_NUMERO_CNBF, MNDTR_ID_ADRBS,
    MNDTR_TELEPHONE, MNDTR_FAX, MNDTR_ADRESSE_MAIL, MNDTR_ID_CLIEN, MNDTR_QUALIF_PROFESSION, MNDTR_TEXTE, MNDTR_ACTIF) VALUES
    -- avocat complet : toutes les marches, Validee [0,1,2,3,4], « Personne physique », « Me » devant le nom
    (1500101, ''ME'', ''ARDOISE Irène'', ''ARD15'', ''900500001'', ''150001'', 1500901, NULL, NULL, ''ardoise@exemple.test'',
     1500911, ''AV'', NULL, 1),
    -- civilite et designation seules : Validee [0], non repertoriee
    (1500102, ''MME'', ''CHANTELLE Olga'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1),
    -- SANS civilite, un telephone : identite incomplete, une marche : EnCours [1] -- jumeau 1500102 (civilite, Validee)
    (1500103, '''', ''DORVAL Théo'', NULL, NULL, NULL, NULL, ''0476000000'', NULL, NULL, NULL, NULL, NULL, 1),
    -- sans civilite, client -1 (« aucun client » de la V2) : aucune marche, AFaire [] -- jumeau 1500103 (une marche) et
    -- 1500101 (client 1500911, marche 3 franchie)
    (1500104, '''', ''EPSILON Marc'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, -1, NULL, NULL, 1),
    -- civilite PM : « Personne morale », le nom sans « PM » ; abrege : Validee [0,2] -- jumeau 1500101 (ME, physique)
    (1500105, ''PM'', ''KAPPA AVOCATS SELARL'', ''KAPPA15'', ''900500002'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1),
    -- civilite ND : « Personne physique », le nom seul, « civilite non determinee » ; infos complementaires : Validee [0,4]
    (1500106, ''ND'', ''LAMBDA CONSEIL'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ''Huissier'', 1),
    -- INACTIF au nom qui trie en premier : apres TOUS les actifs (ordreSelect de la V2) ; Validee [0,2]
    (1500107, ''MR'', ''AARON Paul'', ''AAR15'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 0),
    -- designation VIDE, abrege : identite incomplete, EnCours [2], l''abrege tient lieu de nom ; le nom vide trie en premier
    -- -- jumeau 1500102 (designation saisie, Validee)
    (1500108, ''MR'', '''', ''ZZ15'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 1),
    -- personne morale, une adresse seule : Validee [0,1]
    (1500109, ''PM'', ''NU JURIS SAS'', NULL, NULL, NULL, 1500902, NULL, NULL, NULL, NULL, NULL, NULL, 1);';
