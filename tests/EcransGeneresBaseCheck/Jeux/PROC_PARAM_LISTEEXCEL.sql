-- Jeu de LecteurParametrageListesExcel (« Listes Excel », PROC_PARAM_LISTEEXCEL), lot 2 du plan « RIG v3 complet, mesure par le banc
-- du parcours complet » (plan-f4fb097f). Listes 3200101-3200109, codes de champ L2_*. Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_PARAM_LISTEEXCEL.attendu et .recherches.
-- Les tables, aux types de RIG_DEV (sys.columns de SQL-DEV\DEV, lu le 28/11/2026). PRMLS_ID_PRMLS est IDENTITY dans RIG_DEV : la table
-- du banc ne l'est pas, pour poser les identifiants du jeu (comme les autres jeux). PARAMETRAGE_LISTE_XLS est deja creee par
-- PROC_LISTEEXCEL.sql (joue avant) : meme definition. Ses types de liste (INS, IDJ, GIE, AFF, PC), que l'« Export Excel » lit, ne sont
-- pas ceux de ce jeu : ses lignes ne changent pas.
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE (PRMLS_ID_PRMLS int NOT NULL PRIMARY KEY, PRMLS_VALEUR varchar(50) NULL, PRMLS_SYNONYME varchar(20) NULL,
        PRMLS_LIBELLE varchar(100) NULL, PRMLS_REQUETE varchar(max) NULL, PRMLS_LISTE varchar(50) NULL);
IF OBJECT_ID(N'v2.PARAMETRAGE_LISTE_XLS') IS NULL
    CREATE TABLE v2.PARAMETRAGE_LISTE_XLS (PRMLX_TYPE_LISTE varchar(50) NOT NULL, PRMLX_TEXTE varchar(7000) NOT NULL, PRMLX_NUM_ORDRE int NULL,
        PRMLX_LIBELLE varchar(50) NOT NULL, PRMLX_TYPE_TEXTE varchar(10) NOT NULL, PRMLX_NUM_ORDRE_INIT int NOT NULL,
        PRMLX_CODE_CHAMP varchar(30) NOT NULL PRIMARY KEY);

-- Les listes : une par famille de l'export (IEF instances, MC mesures, PRV preventions, PCC procedures, JAL parutions, VR divers), un
-- type que l'export ne connait pas (L2X, « Divers »), et deux ecartees.
INSERT INTO v2.PARAMETRAGE_LISTE (PRMLS_ID_PRMLS, PRMLS_VALEUR, PRMLS_SYNONYME, PRMLS_LIBELLE, PRMLS_REQUETE, PRMLS_LISTE) VALUES
    (3200101, 'IEF', NULL, 'Liste des instances par évènement futur', NULL, 'LISTEXLS_IEF'),
    (3200102, 'MC', NULL, 'Liste des mesures en cours', NULL, 'LISTEXLS_MC'),
    -- aucun champ, aucune clause FROM : « Sans champ »
    (3200103, 'PRV', NULL, 'Liste des préventions', NULL, 'LISTEXLS_PRV'),
    (3200104, 'PCC', NULL, 'Liste des PC en cours', NULL, 'LISTEXLS_PCC'),
    (3200105, 'JAL', NULL, 'Liste des parutions au JAL', NULL, 'LISTEXLS_JAL'),
    -- sans libelle : son type le nomme
    (3200106, 'VR', NULL, NULL, NULL, 'LISTEXLS_VR'),
    -- un type que le switch de l'export ne nomme pas : « Divers », et la ligne le dit
    (3200107, 'L2X', NULL, 'Liste du banc lot 2', NULL, 'LISTEXLS_L2X'),
    -- ECARTEE : une autre liste de reference (PRMLS_LISTE sans « LISTEXLS ») -- jumelle 3200101
    (3200108, 'L2Y', NULL, 'Liste du banc lot 2 hors Excel', NULL, 'TYPE_L2Y'),
    -- ECARTEE : « LISTEXLS » sans caractere apres (le « _ » du LIKE de la V2 en exige un) -- jumelle 3200101 (« LISTEXLS_IEF »)
    (3200109, 'L2Z', NULL, 'Liste du banc lot 2 sans suffixe', NULL, 'LISTEXLS');

-- Les champs. Seuls les SELECT comptent (L2_IEFL, une ligne LISTE, ne compte pas -- jumeau L2_IEF1) ; le FROM est la clause de la
-- fiche (L2X n'en a pas). L2_IEF2 a l'ordre initial 1 : la fiche le montre avant L2_IEF1 (ordre 2).
INSERT INTO v2.PARAMETRAGE_LISTE_XLS (PRMLX_TYPE_LISTE, PRMLX_TEXTE, PRMLX_NUM_ORDRE, PRMLX_LIBELLE, PRMLX_TYPE_TEXTE, PRMLX_NUM_ORDRE_INIT, PRMLX_CODE_CHAMP) VALUES
    ('IEF', 'INSTN_NUM_ROLE AS [Num Rôle]', 2, 'Num Rôle', 'SELECT', 2, 'L2_IEF1'),
    ('IEF', 'EVNPR_DATE AS [Date évènement]', 1, 'Date évènement', 'SELECT', 1, 'L2_IEF2'),
    ('IEF', ' FROM INSTANCE INNER JOIN EVENEMENT_PROCEDURE ON EVNPR_ID_INSTN = INSTN_ID_INSTN', 1, 'FROM_IEF', 'FROM', 1, 'L2_IEFF'),
    ('IEF', '', 1, 'RIEN', 'LISTE', 1, 'L2_IEFL'),
    ('MC', 'MSR_NUMERO AS Numéro', 1, 'Numéro', 'SELECT', 1, 'L2_MC1'),
    ('MC', ' FROM MESURE', 1, 'FROM_MC', 'FROM', 1, 'L2_MCF'),
    ('PCC', 'PRCCL_NUMERO AS Numéro', 1, 'Numéro', 'SELECT', 1, 'L2_PCC1'),
    ('PCC', 'PRCCL_NOM AS Nom', 2, 'Nom', 'SELECT', 2, 'L2_PCC2'),
    ('PCC', 'PRCCL_DATE_OUVERTURE AS [Date ouverture]', NULL, 'Date ouverture', 'SELECT', 3, 'L2_PCC3'),
    ('PCC', ' FROM PROCEDURE_COLLECTIVE', 1, 'FROM_PCC', 'FROM', 1, 'L2_PCCF'),
    ('JAL', 'PRTJL_DATE AS [Date parution]', 1, 'Date parution', 'SELECT', 1, 'L2_JAL1'),
    ('JAL', 'PRTJL_JOURNAL AS Journal', 2, 'Journal', 'SELECT', 2, 'L2_JAL2'),
    ('JAL', ' FROM PARUTION_JAL', 1, 'FROM_JAL', 'FROM', 1, 'L2_JALF'),
    ('VR', 'APLAF_DATE AS [Date appel]', 1, 'Date appel', 'SELECT', 1, 'L2_VR1'),
    ('VR', 'INSTN_NUM_ROLE AS [Num Rôle]', 2, 'Num Rôle', 'SELECT', 2, 'L2_VR2'),
    ('VR', ' FROM APPEL_AFFAIRE', 1, 'FROM_VR', 'FROM', 1, 'L2_VRF'),
    ('L2X', 'INSTN_NOM_COURANT AS Nom', 1, 'Nom', 'SELECT', 1, 'L2_X1'),
    ('L2X', 'INSTN_NUM_ROLE AS [Num Rôle]', 2, 'Num Rôle', 'SELECT', 2, 'L2_X2'),
    -- les champs des listes ecartees ne sont lus par aucune ligne
    ('L2Y', 'INSTN_NUM_ROLE AS [Num Rôle]', 1, 'Num Rôle', 'SELECT', 1, 'L2_Y1');
