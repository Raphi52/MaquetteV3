-- Jeu de LecteurInscriptionsRpjc (« EDI RPJC inscriptions », PROC_EDI_RPJC_INS), lot 3 du plan « ecrans retrouves
-- branches sur la base ». Identifiants 3001xx (lot 3 : a partir de 300000). Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_EDI_RPJC_INS.attendu.
-- La table V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues, plus RPJCI_NUMERO_SIREN : un SIREN seul ne fait pas une structure (300104).
IF OBJECT_ID(N'v2.DEMAT_RPJC_INS') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI int NOT NULL PRIMARY KEY, RPJCI_DATE_CREATION datetime NULL,
        RPJCI_ETAT varchar(2) NULL, RPJCI_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCI_DATE_TRAITEMENT_RIG datetime NULL,
        RPJCI_MOTIF_TRAITEMENT_RIG varchar(300) NULL, RPJCI_DATE_DEMANDE datetime NULL, RPJCI_TYPE_MAJ varchar(1) NULL,
        RPJCI_NUMERO_CNBF varchar(6) NULL, RPJCI_NOM varchar(50) NULL, RPJCI_PRENOM varchar(50) NULL,
        RPJCI_DENOMINATION varchar(200) NULL, RPJCI_USER_EN_COURS varchar(255) NULL, RPJCI_NUMERO_SIREN varchar(9) NULL,
        RPJCI_NUMERO_CNBF_STRUCTURE varchar(6) NULL);

INSERT INTO v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI, RPJCI_DATE_CREATION, RPJCI_ETAT, RPJCI_ETAT_TRAITEMENT_RIG,
    RPJCI_DATE_TRAITEMENT_RIG, RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_DATE_DEMANDE, RPJCI_TYPE_MAJ, RPJCI_NUMERO_CNBF, RPJCI_NOM,
    RPJCI_PRENOM, RPJCI_DENOMINATION, RPJCI_USER_EN_COURS, RPJCI_NUMERO_SIREN, RPJCI_NUMERO_CNBF_STRUCTURE) VALUES
    -- en attente (etat 1), personne ne l'a ouverte, avocat seul : AFaire, reçue a la date de la demande
    (300101, DATEADD(DAY, -3, @aujourdhui), '1', NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), 'I', '300001',
     'ARDOISE', 'Irène', '', NULL, '', ''),
    -- en attente, ouverte par un agent, structure par son Cnbf : EnCours, « Avocat et structure »
    (300102, DATEADD(DAY, -2, @aujourdhui), '1', NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), 'I', '300002',
     'BELIER', 'Simon', 'CABINET KAPPA AVOCATS', 'agent.banc', '', '300902'),
    -- acceptee (A, etat 2), structure par son Cnbf : Validee, demande, structure et mandataire
    (300103, DATEADD(DAY, -9, @aujourdhui), '2', 'A', DATEADD(HOUR, -14, @aujourdhui), NULL, NULL, 'I', '300003',
     'CHANTELLE', 'Olga', NULL, NULL, '512300103', '300903'),
    -- acceptee (A, etat 4 valide par Extelia), SIREN SANS Cnbf de structure, modification : « Avocat seul », Validee sans
    -- la structure -- jumeau 300103 (Cnbf de structure : « Avocat et structure »)
    (300104, DATEADD(DAY, -9, @aujourdhui), '4', 'A', DATEADD(DAY, -3, @aujourdhui), NULL, NULL, 'M', '300004',
     'DORVAL', 'Théo', 'LAMBDA CONSEIL', NULL, '512300104', NULL),
    -- rejetee par le greffe (R) avec son motif : Rejetee, demande et rejet
    (300105, DATEADD(DAY, -9, @aujourdhui), '2', 'R', DATEADD(DAY, -5, @aujourdhui), 'Cnbf inconnu du barreau', NULL, 'I',
     '300005', 'ECORCE', 'Lina', '', NULL, '', ''),
    -- SANS etat : ni « En attente » (= '1') ni « Traites » (<> '1') dans la V2 : ecarte -- jumeau retenu 300101 (etat 1)
    (300106, DATEADD(DAY, -1, @aujourdhui), NULL, NULL, NULL, NULL, DATEADD(DAY, -1, @aujourdhui), 'I', '300006',
     'FAIENCE', 'Paul', '', NULL, '', ''),
    -- acceptee puis reponse rejetee par Extelia (etat 9) : garde sa decision (Validee), precisions le disent
    (300107, DATEADD(DAY, -12, @aujourdhui), '9', 'A', DATEADD(DAY, -10, @aujourdhui), NULL, NULL, 'I', '300007',
     'GRES', 'Rose', NULL, NULL, NULL, NULL),
    -- en attente, une structure qui s'inscrit sans nom de personne : la denomination seule ; sans date de demande,
    -- la date de creation
    (300108, DATEADD(DAY, -1, @aujourdhui), '1', NULL, NULL, NULL, NULL, 'I', '300008',
     '', '', 'MU JURIS', NULL, '', '');

-- Lot 16 (plan-676a4b19) : les gestes « Traiter la demande » et « Rejeter la demande » (EcrivainInscriptionsRpjc.cs,
-- PROC_EDI_RPJC_INS.gestes). Identifiants 26002xx (lot 16 : a partir de 2600000). Les colonnes que les gestes lisent ou ecrivent, aux
-- types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de la copie de dev du greffe 3801, lu le 27/11/2026) ; PROC_EDI_RPVAINS.sql, joue
-- apres, ajoute les memes s'il ne les trouve pas.
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_USER_TRAITEMENT_RIG') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_USER_TRAITEMENT_RIG varchar(255) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_MAIL') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_MAIL varchar(80) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_MAIL2') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_MAIL2 varchar(80) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_INS', 'RPJCI_MAIL3') IS NULL ALTER TABLE v2.DEMAT_RPJC_INS ADD RPJCI_MAIL3 varchar(80) NULL;
-- Les demandes des gestes. Les mandataires : ceux d'autres jeux (aucun ajoute : les Mandataires et le rapprochement d'acteurs les
-- listent tous) -- 1300201 ALP13 actif au CNBF 130001 et 1300202 BET13 inactif (PROC_EDI_RPJC_TRCNC.sql), 2500104 actif et
-- 2500103 inactif, tous deux d'abrege L15DBL (PROC_EDI_RPVAINS.sql). Le traitement de 2600201 laisse ALP13 tel qu'il est lu
-- (meme CNBF, ni structure, ni SIREN, ni courriel a reprendre) : les jeux qui relisent les mandataires ne changent pas.
INSERT INTO v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI, RPJCI_DATE_CREATION, RPJCI_ETAT, RPJCI_ETAT_TRAITEMENT_RIG,
    RPJCI_DATE_TRAITEMENT_RIG, RPJCI_MOTIF_TRAITEMENT_RIG, RPJCI_DATE_DEMANDE, RPJCI_TYPE_MAJ, RPJCI_NUMERO_CNBF, RPJCI_NOM,
    RPJCI_PRENOM, RPJCI_DENOMINATION, RPJCI_USER_EN_COURS, RPJCI_NUMERO_SIREN, RPJCI_NUMERO_CNBF_STRUCTURE) VALUES
    -- en attente, inscription : traitee (« Traiter la demande », principal ALP13)
    (2600201, DATEADD(DAY, -2, @aujourdhui), '1', NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), 'I', '130001',
     'LYS', 'Irène', NULL, NULL, '', ''),
    -- en attente, inscription : rejetee (« Rejeter la demande »)
    (2600202, DATEADD(DAY, -2, @aujourdhui), '1', NULL, NULL, NULL, DATEADD(DAY, -2, @aujourdhui), 'I', '130009',
     'MAUVE', 'Paul', NULL, NULL, '', ''),
    -- acceptee (etat 4), tenue par un agent : refusee a l'ouverture (frmChoixTrt.cs:255-257) -- jumeau 2600201, que personne ne tient
    (2600203, DATEADD(DAY, -9, @aujourdhui), '4', 'A', DATEADD(DAY, -8, @aujourdhui), NULL, NULL, 'I', '130001',
     'NACRE', 'Léa', NULL, 'agent.l16', '', ''),
    -- modification acceptee (etat 4) : ne se retraite pas (l. 266-269) -- jumeau 2600201 (inscription)
    (2600204, DATEADD(DAY, -9, @aujourdhui), '4', 'A', DATEADD(DAY, -8, @aujourdhui), NULL, NULL, 'M', '130001',
     'OCRE', 'Jean', NULL, NULL, '', ''),
    -- refusee (etat 2, R) : ne se traite pas, la V2 donne son motif (l. 281-283) -- jumeau 2600201 (sans decision)
    (2600205, DATEADD(DAY, -9, @aujourdhui), '2', 'R', DATEADD(DAY, -8, @aujourdhui), 'Cnbf inconnu du barreau', NULL, 'I', '130009',
     'PERLE', 'Anne', NULL, NULL, '', ''),
    -- modification en erreur d'envoi (etat 8), sans decision : « pas encore gérée » (l. 298-302) -- jumeau 2600201 (inscription)
    (2600206, DATEADD(DAY, -9, @aujourdhui), '8', NULL, NULL, NULL, NULL, 'M', '130001',
     'QUARTZ', 'Marc', NULL, NULL, '', '');
