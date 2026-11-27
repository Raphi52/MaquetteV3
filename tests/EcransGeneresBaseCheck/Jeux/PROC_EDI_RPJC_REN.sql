-- Jeu de LecteurRenvoisRpjc (« EDI RPJC renvois », PROC_EDI_RPJC_REN), lot 3 du plan « ecrans retrouves branches sur
-- la base ». Identifiants 3002xx (lot 3 : a partir de 300000). Ce qui est retenu ou ecarte, et pourquoi :
-- PROC_EDI_RPJC_REN.attendu.
-- Les tables V2 que le lecteur lit, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 25/11/2026),
-- seulement les colonnes lues.
IF OBJECT_ID(N'v2.DEMAT_RPJC_REN') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_REN (RPJCR_ID_RPJCR int NOT NULL PRIMARY KEY, RPJCR_ETAT varchar(2) NULL,
        RPJCR_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCR_DATE_TRAITEMENT_RIG datetime NULL, RPJCR_NUMERO_AFFAIRE varchar(11) NULL,
        RPJCR_CNBF_DEMANDE varchar(6) NULL, RPJCR_DATE_AUDIENCE_INITIALE datetime NULL, RPJCR_DATE_RENVOI_DEMANDEE datetime NULL,
        RPJCR_USER_EN_COURS varchar(255) NULL, RPJCR_MOTIF_REJET_RIG varchar(max) NULL, RPJCR_DATE_RECEPTION_EXTELIA_CT datetime NULL);
IF OBJECT_ID(N'v2.INSTANCE') IS NULL
    CREATE TABLE v2.INSTANCE (INSTN_ID_INSTN int NOT NULL PRIMARY KEY, INSTN_NUM_ROLE varchar(11) NULL, INSTN_NOM_COURANT varchar(250) NULL);
IF OBJECT_ID(N'v2.MANDATAIRE') IS NULL
    CREATE TABLE v2.MANDATAIRE (MNDTR_ID_MNDTR int NOT NULL PRIMARY KEY, MNDTR_NOM varchar(128) NOT NULL, MNDTR_ACTIF bit NOT NULL,
        MNDTR_NUMERO_CNBF varchar(6) NULL);

-- Les affaires : deux instances au meme role (300261 et 300262), la premiere par son numero donne le nom (la V2 lit
-- INSTANCE sans ORDER BY, par sa cle) ; le role 2026F30002 n'a pas d'instance : le role seul.
INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT) VALUES
    (300261, '2026F30001', 'ALPHA SERVICES SARL c/ BETA INDUSTRIE SAS'),
    (300262, '2026F30001', 'NOM PLUS RECENT A NE PAS PRENDRE'),
    (300263, '2026F30003', 'GAMMA CONSEIL SARL');
-- Les avocats par CNBF : 300013 en a deux, l'actif passe d'abord (Mandataire.cs:277, ORDER BY MNDTR_ACTIF DESC) ;
-- 300099 n'en a aucun (« Non trouvé » de la V2).
INSERT INTO v2.MANDATAIRE (MNDTR_ID_MNDTR, MNDTR_NOM, MNDTR_ACTIF, MNDTR_NUMERO_CNBF) VALUES
    (300251, 'ALPHA Irène', 1, '300011'),
    (300253, 'ANCIEN BETA', 0, '300013'),
    (300254, 'BETA Simon', 1, '300013');

INSERT INTO v2.DEMAT_RPJC_REN (RPJCR_ID_RPJCR, RPJCR_ETAT, RPJCR_ETAT_TRAITEMENT_RIG, RPJCR_DATE_TRAITEMENT_RIG,
    RPJCR_NUMERO_AFFAIRE, RPJCR_CNBF_DEMANDE, RPJCR_DATE_AUDIENCE_INITIALE, RPJCR_DATE_RENVOI_DEMANDEE, RPJCR_USER_EN_COURS,
    RPJCR_MOTIF_REJET_RIG, RPJCR_DATE_RECEPTION_EXTELIA_CT) VALUES
    -- en attente (etat 1), personne : AFaire, quelle que soit son audience initiale (a venir)
    (300201, '1', NULL, NULL, '2026F30001', '300011', DATEADD(DAY, 10, @aujourdhui), DATEADD(DAY, 40, @aujourdhui), NULL, NULL, NULL),
    -- en attente, ouverte par un agent, avocat introuvable : EnCours, demande seule
    (300202, '1', NULL, NULL, '2026F30002', '300099', DATEADD(DAY, 12, @aujourdhui), NULL, 'agent.r', NULL, NULL),
    -- en attente, ouverte, avocat retrouve (l'actif des deux) : EnCours, demande et avocat
    (300203, '1', NULL, NULL, '2026F30003', '300013', DATEADD(DAY, 5, @aujourdhui), NULL, 'agent.s', NULL, NULL),
    -- acceptee, audience initiale au jour moins un mois (borne basse de la V2, incluse) : retenue, Validee
    (300204, '2', 'A', DATEADD(DAY, -40, @aujourdhui), '2026F30001', '300011', DATEADD(MONTH, -1, @aujourdhui), NULL, NULL, NULL, NULL),
    -- acceptee, audience initiale la veille de la borne basse : ecartee -- jumeau retenu 300204
    (300205, '4', 'A', DATEADD(DAY, -40, @aujourdhui), '2026F30001', '300011', DATEADD(DAY, -1, DATEADD(MONTH, -1, @aujourdhui)), NULL, NULL, NULL, NULL),
    -- rejetee par le greffe, audience initiale aujourd'hui a 23 h (borne haute, 24 h, incluse) : retenue, Rejetee
    (300206, '2', 'R', DATEADD(DAY, -2, @aujourdhui), '2026F30003', '300011', DATEADD(HOUR, 23, @aujourdhui), NULL, NULL,
     'Motif sans date de prononcé', NULL),
    -- acceptee, audience initiale demain a 0 h : ecartee -- jumeau retenu 300206
    (300207, '4', 'A', DATEADD(DAY, -2, @aujourdhui), '2026F30003', '300011', DATEADD(DAY, 1, @aujourdhui), NULL, NULL, NULL, NULL),
    -- rejetee par Extelia (etat 9), audience initiale il y a six mois (hors du mois) : retenue, le bouton « REJET
    -- DOCAPOST » de la V2 les montre toutes
    (300208, '9', 'A', DATEADD(MONTH, -6, @aujourdhui), '2026F30001', '300011', DATEADD(MONTH, -6, @aujourdhui), NULL, NULL, NULL,
     DATEADD(DAY, -20, @aujourdhui)),
    -- SANS etat, audience dans le mois : ni = '1' ni <> '1' en SQL : ecartee -- jumeau retenu 300210 (meme audience)
    (300209, NULL, 'A', DATEADD(DAY, -4, @aujourdhui), '2026F30001', '300011', DATEADD(DAY, -3, @aujourdhui), NULL, NULL, NULL, NULL),
    -- erreur d'envoi (etat 8) apres acceptation, audience dans le mois : retenue, Validee
    (300210, '8', 'A', DATEADD(DAY, -4, @aujourdhui), '2026F30001', '300011', DATEADD(DAY, -3, @aujourdhui), NULL, NULL, NULL, NULL);

-- Lot 16 (plan-676a4b19) : les gestes « Accepter » et « Rejeter » (EcrivainRenvoisRpjc.cs, PROC_EDI_RPJC_REN.gestes). Identifiants
-- 26001xx (lot 16 : a partir de 2600000). Les colonnes que les gestes ecrivent, et la table des motifs, aux types de RIG_DEV
-- (INFORMATION_SCHEMA.COLUMNS de la copie de dev du greffe 3801, lu le 27/11/2026).
IF COL_LENGTH('v2.DEMAT_RPJC_REN', 'RPJCR_USER_TRAITEMENT_RIG') IS NULL ALTER TABLE v2.DEMAT_RPJC_REN ADD RPJCR_USER_TRAITEMENT_RIG varchar(255) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_REN', 'RPJCR_ID_AUDIENCE_CHOISIE') IS NULL ALTER TABLE v2.DEMAT_RPJC_REN ADD RPJCR_ID_AUDIENCE_CHOISIE int NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_REN', 'RPJCR_CODE_REJET_RIG') IS NULL ALTER TABLE v2.DEMAT_RPJC_REN ADD RPJCR_CODE_REJET_RIG varchar(100) NULL;
IF COL_LENGTH('v2.DEMAT_RPJC_REN', 'RPJCR_DATE_PRONONCE_REJET_RIG') IS NULL ALTER TABLE v2.DEMAT_RPJC_REN ADD RPJCR_DATE_PRONONCE_REJET_RIG datetime NULL;
IF OBJECT_ID(N'v2.DEMAT_RPJC_REN_MOTIF_RENVOI') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_REN_MOTIF_RENVOI (RPJCRMR_CODE varchar(50) NOT NULL PRIMARY KEY, RPJCRMR_LIBELLE varchar(100) NULL,
        RPJCRMR_UTILISABLE_SAISIE bit NOT NULL, RPJCRMR_DATE_OBLIGATOIRE bit NOT NULL);
-- Deux motifs de RIG_DEV : MISEDELIBERE impose la date de prononce, DIVERS non.
INSERT INTO v2.DEMAT_RPJC_REN_MOTIF_RENVOI (RPJCRMR_CODE, RPJCRMR_LIBELLE, RPJCRMR_UTILISABLE_SAISIE, RPJCRMR_DATE_OBLIGATOIRE) VALUES
    ('DIVERS', 'Voir commentaire', 1, 0),
    ('MISEDELIBERE', 'Mise en délibéré du dossier', 1, 1);
-- Les avocats : ceux de PROC_EDI_RPJC_TRCNC.sql (meme table, aucun mandataire ajoute : les Mandataires et le rapprochement
-- d'acteurs les listent tous) -- CNBF 130001, 1300201 actif avec abrege ALP13 (retenu par la V2) ; CNBF 130002, 1300202 inactif
-- (ecarte). L'audience de renvoi : 2400401 de PROC_CABINET.sql (10/03/2099 a 09:00).
INSERT INTO v2.DEMAT_RPJC_REN (RPJCR_ID_RPJCR, RPJCR_ETAT, RPJCR_ETAT_TRAITEMENT_RIG, RPJCR_DATE_TRAITEMENT_RIG,
    RPJCR_NUMERO_AFFAIRE, RPJCR_CNBF_DEMANDE, RPJCR_DATE_AUDIENCE_INITIALE, RPJCR_DATE_RENVOI_DEMANDEE, RPJCR_USER_EN_COURS,
    RPJCR_MOTIF_REJET_RIG, RPJCR_DATE_RECEPTION_EXTELIA_CT) VALUES
    -- en attente, avocat actif avec abrege, audience initiale passee : accepte (geste « Accepter »)
    (2600101, '1', NULL, NULL, '2026F30001', '130001', DATEADD(DAY, -2, @aujourdhui), NULL, NULL, NULL, NULL),
    -- en attente, audience initiale a venir : refusee (OPE_INFO_DEMANDE.cs:458-460) -- jumeau 2600101
    (2600102, '1', NULL, NULL, '2026F30001', '130001', DATEADD(DAY, 3, @aujourdhui), NULL, NULL, NULL, NULL),
    -- en attente, l'avocat du CNBF est inactif : refusee (l. 103-108) -- jumeau 2600101
    (2600103, '1', NULL, NULL, '2026F30001', '130002', DATEADD(DAY, -2, @aujourdhui), NULL, NULL, NULL, NULL),
    -- en attente, tenue par un agent : refusee (OPE_RESULTAT_RECHERCHE.cs:386-387) -- jumeau 2600101
    (2600104, '1', NULL, NULL, '2026F30001', '130001', DATEADD(DAY, -2, @aujourdhui), NULL, 'agent.l16', NULL, NULL),
    -- en attente : rejetee avec MISEDELIBERE et sa date de prononce
    (2600105, '1', NULL, NULL, '2026F30001', '130001', DATEADD(DAY, -2, @aujourdhui), NULL, NULL, NULL, NULL),
    -- en attente : rejetee avec DIVERS, sans date
    (2600106, '1', NULL, NULL, '2026F30001', '130001', DATEADD(DAY, -2, @aujourdhui), NULL, NULL, NULL, NULL),
    -- en attente mais deja rejetee (R) : un nouveau rejet ne change rien (l. 532-535) -- jumeau 2600106
    (2600107, '1', 'R', DATEADD(DAY, -1, @aujourdhui), '2026F30001', '130001', DATEADD(DAY, -2, @aujourdhui), NULL, NULL, NULL, NULL);
