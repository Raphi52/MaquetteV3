-- Jeu de LecteurDemandesFnig (« Demandes FNIG », PROC_FNIG), lot 3 du plan « ecrans retrouves branches sur la base ».
-- Identifiants 3003xx (lot 3 : a partir de 300000). Ce qui est retenu ou ecarte, et pourquoi : PROC_FNIG.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues.
IF OBJECT_ID(N'v2.CASIER_JUDICIAIRE_FNIG') IS NULL
    CREATE TABLE v2.CASIER_JUDICIAIRE_FNIG (CFNIG_ID_CFNIG int NOT NULL PRIMARY KEY, CFNIG_DATE_JUGEMENT datetime NULL,
        CFNIG_DATE_FIN_INTERDICTION datetime NULL, CFNIG_NOM varchar(35) NULL, CFNIG_PRENOM varchar(62) NULL,
        CFNIG_DATE_ENVOI datetime NULL, CFNIG_ETAT_FNIG varchar(32) NULL, CFNIG_ERREUR_ENVOI_FNIG varchar(max) NULL,
        CFNIG_TYPE_DEMANDE_FNIG varchar(32) NULL);
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
    (300307, DATEADD(DAY, -15, @aujourdhui), NULL, 'FNIGBANC', 'Gilles', NULL, 'Erreur', '', 'Modification');

INSERT INTO v2.CASIER_JUDICIAIRE_FNIG_ENTREPRISE (CFNET_ID_CFNET, CFNET_ID_CFNIG, CFNET_DENOMINATION, CFNET_SIREN) VALUES
    (300311, 300301, 'FNIG ALPHA SARL', '512300311'),
    (300312, 300301, 'FNIG BETA SAS', NULL),
    (300313, 300304, '', NULL),
    (300314, 300305, NULL, '512300314');
