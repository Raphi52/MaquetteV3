-- Jeu de LecteurDemandesFnig (« Demandes FNIG », PROC_FNIG), lot 3 du plan « ecrans retrouves branches sur la base ».
-- Identifiants 3003xx (lot 3 : a partir de 300000). Ce qui est retenu ou ecarte, et pourquoi : PROC_FNIG.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues -- sauf CASIER_JUDICIAIRE_FNIG : lot 16 du plan « RIG v3 100 % fonctionnel » (EcrivainDemandesFnig,
-- PROC_FNIG.gestes), « Désactiver » COPIE toutes les colonnes de la demande (CASIER_JUDICIAIRE_FNIG.cs:465-472). Elle a donc toutes
-- celles de RIG v2, aux types de sa definition (D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\Tables\
-- CASIER_JUDICIAIRE_FNIG.sql), sans IDENTITY (les ids du jeu ; une desactivation prend le suivant).
IF OBJECT_ID(N'v2.CASIER_JUDICIAIRE_FNIG') IS NULL
    CREATE TABLE v2.CASIER_JUDICIAIRE_FNIG (CFNIG_ID_CFNIG int NOT NULL PRIMARY KEY, CFNIG_ID_CSRJD int NULL,
        CFNIG_DATE_CREATION datetime NULL, CFNIG_DATE_JUGEMENT datetime NULL, CFNIG_TYPE_PROHIBITION varchar(2) NULL,
        CFNIG_DATE_DEBUT_INTERDICTION datetime NULL, CFNIG_DATE_FIN_INTERDICTION datetime NULL, CFNIG_SANCTION varchar(2000) NULL,
        CFNIG_RAISON_ANNULATION varchar(max) NULL, CFNIG_NOM varchar(35) NULL, CFNIG_NOM_USAGE varchar(35) NULL,
        CFNIG_PRENOM varchar(62) NULL, CFNIG_DATE_NAISSANCE datetime NULL, CFNIG_LIEU_NAISSANCE varchar(50) NULL,
        CFNIG_PAYS_NAISSANCE varchar(3) NULL, CFNIG_NATIONALITE varchar(35) NULL, CFNIG_DATE_ENVOI datetime NULL,
        CFNIG_ID_FNIG varchar(16) NULL, CFNIG_ETAT_FNIG varchar(32) NULL, CFNIG_UTILISATEUR varchar(255) NULL,
        CFNIG_ERREUR_ENVOI_FNIG varchar(max) NULL, CFNIG_TYPE_DEMANDE_FNIG varchar(32) NULL, CFNIG_CIVILITE varchar(7) NULL,
        CFNIG_ADRESSE varchar(255) NULL, CFNIG_SUITE_ADRESSE varchar(255) NULL, CFNIG_CODE_POSTAL varchar(10) NULL,
        CFNIG_VILLE varchar(70) NULL, CFNIG_PAYS varchar(2) NULL, CFNIG_JURIDICTION varchar(1000) NULL, CFNIG_ID_UTLST int NULL);
IF OBJECT_ID(N'v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE') IS NULL
    CREATE TABLE v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE (CFNET_ID_CFNET int NOT NULL PRIMARY KEY, CFNET_ID_CFNIG int NOT NULL,
        CFNET_DENOMINATION varchar(255) NULL, CFNET_SIREN varchar(255) NULL);

INSERT INTO v2.CASIER_JUDICIAIRE_FNIG (CFNIG_ID_CFNIG, CFNIG_DATE_JUGEMENT, CFNIG_DATE_FIN_INTERDICTION, CFNIG_NOM, CFNIG_PRENOM,
    CFNIG_DATE_ENVOI, CFNIG_ETAT_FNIG, CFNIG_ERREUR_ENVOI_FNIG, CFNIG_TYPE_DEMANDE_FNIG) VALUES
    -- ajout envoye, deux entreprises renseignees : Validee, entreprises franchies
    (300301, DATEADD(DAY, -30, @aujourdhui), DATEADD(YEAR, 5, @aujourdhui), 'FNIGBANC', 'Alice', DATEADD(DAY, -3, @aujourdhui), 'Envoye', NULL, 'Ajout'),
    -- ajout envoye, sans entreprise : Validee sans la marche des entreprises
    (300302, DATEADD(DAY, -28, @aujourdhui), NULL, 'FNIGBANC', 'Bruno', DATEADD(DAY, -2, @aujourdhui), 'Envoye', NULL, 'Ajout'),
    -- ajout en erreur, avec son message : Rejetee
    (300303, DATEADD(DAY, -25, @aujourdhui), NULL, 'FNIGBANC', 'Chloé', NULL, 'Erreur', 'Date de naissance absente', 'Ajout'),
    -- modification a envoyer, une entreprise laissee VIDE (ni denomination ni SIREN) : EnCours, entreprises NON franchies
    -- -- jumeau retenu 300301 (entreprises renseignees)
    (300304, DATEADD(DAY, -20, @aujourdhui), NULL, 'FNIGBANC', 'David', NULL, 'AEnvoyer', NULL, 'Modification'),
    -- desactivation a envoyer, une entreprise par son SIREN seul : EnAttente, entreprises franchies
    (300305, DATEADD(DAY, -400, @aujourdhui), NULL, 'FNIGBANC', 'Emma', NULL, 'ADesactiver', NULL, 'Desactivation'),
    -- ni etat ni type (jamais validee) : AFaire, groupe « Ajout »
    (300306, NULL, NULL, 'FNIGBANC', 'Fanny', NULL, NULL, NULL, NULL),
    -- modification envoyee, en erreur sans message : Rejetee, « envoi refusé »
    (300307, DATEADD(DAY, -15, @aujourdhui), NULL, 'FNIGBANC', 'Gilles', NULL, 'Erreur', '', 'Modification'),
    -- lot 16 : ajout envoye dont l'interdiction debute APRES sa fin (controle 4 d'EcrivainDemandesFnig) : Validee, sa fin affichee
    (300308, DATEADD(DAY, -12, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), 'FNIGBANC', 'Hugo', DATEADD(DAY, -1, @aujourdhui), 'Envoye', NULL, 'Ajout'),
    -- lot 16 : la meme chose pour une demande d'une autre juridiction (sans casier du greffe) : Validee
    (300309, DATEADD(DAY, -12, @aujourdhui), DATEADD(DAY, -20, @aujourdhui), 'FNIGBANC', 'Inès', DATEADD(DAY, -1, @aujourdhui), 'Envoye', NULL, 'Ajout');

-- Lot 16 : ce que les gestes lisent et copient (PROC_FNIG.gestes), sans effet sur la liste (le lecteur ne le lit pas).
--   300301 : rattachee a un casier du greffe ; debut = jugement (borne acceptee de « debut avant le jugement »), debut avant la
--            fin (borne acceptee de « debut apres la fin »), naissance avant le jugement ; numero FNIG et auteur d'origine.
--   300302 : debut la VEILLE du jugement -> « Désactiver » refuse (jumeau accepte : 300301).
--   300303 : naissance avant le jugement -> « Renvoyer » accepte.
--   300307 : naissance APRES le jugement -> « Renvoyer » refuse (jumeau accepte : 300303).
--   300308 : rattachee a un casier, debut apres la fin -> « Désactiver » refuse (jumeau accepte : 300301, debut avant la fin).
--   300309 : SANS casier (autre juridiction : la V2 n'affiche pas la fin, OPE_EDITION_JUGEMENT.cs:99), debut apres la fin ->
--            « Désactiver » accepte (jumeau refuse : 300308).
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ID_CSRJD = 300391, CFNIG_DATE_CREATION = DATEADD(DAY, -30, @aujourdhui),
    CFNIG_DATE_DEBUT_INTERDICTION = DATEADD(DAY, -30, @aujourdhui), CFNIG_DATE_NAISSANCE = '19700501', CFNIG_ID_FNIG = 'FNIG0300301',
    CFNIG_UTILISATEUR = 'banc.origine : ORIGINE Agent', CFNIG_ID_UTLST = 2000902, CFNIG_SANCTION = 'Interdiction de gerer'
WHERE CFNIG_ID_CFNIG = 300301;
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ID_CSRJD = 300392, CFNIG_DATE_DEBUT_INTERDICTION = DATEADD(DAY, -29, @aujourdhui)
WHERE CFNIG_ID_CFNIG = 300302;
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ID_CSRJD = 300393, CFNIG_DATE_NAISSANCE = '19800101' WHERE CFNIG_ID_CFNIG = 300303;
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ID_CSRJD = 300397, CFNIG_DATE_NAISSANCE = DATEADD(DAY, -10, @aujourdhui)
WHERE CFNIG_ID_CFNIG = 300307;
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_ID_CSRJD = 300398, CFNIG_DATE_DEBUT_INTERDICTION = DATEADD(DAY, -10, @aujourdhui)
WHERE CFNIG_ID_CFNIG = 300308;
UPDATE v2.CASIER_JUDICIAIRE_FNIG SET CFNIG_DATE_DEBUT_INTERDICTION = DATEADD(DAY, -10, @aujourdhui) WHERE CFNIG_ID_CFNIG = 300309;
-- L'agent du banc porte un utilisateur RIG v2, l'auteur d'une desactivation (OPE_EDITION.cs:237-240), comme dans PROC_RELCLI.sql.
UPDATE soc.Compte SET IdV2 = 2000901 WHERE CompteWindows = 'banc.ecrans' AND IdV2 IS NULL;

INSERT INTO v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE (CFNET_ID_CFNET, CFNET_ID_CFNIG, CFNET_DENOMINATION, CFNET_SIREN) VALUES
    (300311, 300301, 'FNIG ALPHA SARL', '512300311'),
    (300312, 300301, 'FNIG BETA SAS', NULL),
    (300313, 300304, '', NULL),
    (300314, 300305, NULL, '512300314');
