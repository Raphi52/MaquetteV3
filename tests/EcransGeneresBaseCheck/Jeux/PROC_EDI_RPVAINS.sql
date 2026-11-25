-- Jeu de LecteurInscriptionsRpva (« EDI RPVA », PROC_EDI_RPVAINS), lot 3 du plan « brancher sur la base les procedures
-- encore en exemple des 5 ecrans » (plan-f7ba4de2). Ce qui est retenu ou ecarte, et pourquoi : PROC_EDI_RPVAINS.attendu.
--
-- AUCUNE LIGNE POSEE ICI, et c'est voulu : « EDI RPVA » lit la meme table que « EDI RPJC inscriptions »
-- (v2.DEMAT_RPJC_INS, deux ecrans de la V2 sur les memes demandes). Toute demande en attente (etat 1) posee ici entrerait
-- aussi dans l'ecran de LecteurInscriptionsRpjc et changerait les lignes de PROC_EDI_RPJC_INS.attendu. Le lecteur est donc
-- controle sur les demandes du jeu PROC_EDI_RPJC_INS.sql, joue avant celui-ci (ordre des noms), qui portent deja chaque
-- refus de ce filtre et son jumeau accepte (etat 1 contre etats 2, 4, 9 et sans etat ; Cnbf de structure ou non).
--
-- La table, si ce jeu passait seul : celle de PROC_EDI_RPJC_INS.sql, aux types de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de
-- SQL-DEV\DEV, lu le 25/11/2026).
IF OBJECT_ID(N'v2.DEMAT_RPJC_INS') IS NULL
    CREATE TABLE v2.DEMAT_RPJC_INS (RPJCI_ID_RPJCI int NOT NULL PRIMARY KEY, RPJCI_DATE_CREATION datetime NULL,
        RPJCI_ETAT varchar(2) NULL, RPJCI_ETAT_TRAITEMENT_RIG varchar(2) NULL, RPJCI_DATE_TRAITEMENT_RIG datetime NULL,
        RPJCI_MOTIF_TRAITEMENT_RIG varchar(300) NULL, RPJCI_DATE_DEMANDE datetime NULL, RPJCI_TYPE_MAJ varchar(1) NULL,
        RPJCI_NUMERO_CNBF varchar(6) NULL, RPJCI_NOM varchar(50) NULL, RPJCI_PRENOM varchar(50) NULL,
        RPJCI_DENOMINATION varchar(200) NULL, RPJCI_USER_EN_COURS varchar(255) NULL, RPJCI_NUMERO_SIREN varchar(9) NULL,
        RPJCI_NUMERO_CNBF_STRUCTURE varchar(6) NULL);
