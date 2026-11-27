-- Jeu de LecteurJournalVente (« Journal de vente », PROC_JVENTE), lot 13 du plan-676a4b19. Identifiants 23004xx, factures « L13F... ».
-- Ce qui est retenu ou ecarte, et pourquoi : PROC_JVENTE.attendu et .recherches.
-- Types : ceux de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV, lu le 27/11/2026 : ARTICLE_UTILISE, FACTURE), colonnes lues. Les deux
-- tables ont ici la definition de PROC_ORDOSIGN.sql, PROC_RECHERCHE_GED.sql, PROC_RETAUD.sql, PROC_XPILOTAGE.sql et PROC_XXAFF.sql
-- (joues apres), plus les colonnes que le journal lit -- nullables : ces jeux y inserent sans elles (une facture sans date n'entre
-- dans aucune periode). CLIENT est la fiche rec.Client (REC-013).
IF OBJECT_ID(N'v2.ARTICLE_UTILISE') IS NULL
    CREATE TABLE v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT int NOT NULL PRIMARY KEY, ARTUT_ID_AFFRE int NULL, ARTUT_MONTANT_TTC money NOT NULL,
        ARTUT_ID_INSTN int NULL, ARTUT_ID_CLIEN int NULL, ARTUT_CODE_ARTICLE varchar(32) NULL, ARTUT_PRIX_UHT money NULL,
        ARTUT_QUANTITE smallint NULL, ARTUT_MONTANT_INPI money NULL, ARTUT_MONTANT_BODACC money NULL, ARTUT_MONTANT_AUTRE money NULL,
        ARTUT_DATE_CREATION datetime NULL);
IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_NUMERO_FACTURE') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_NUMERO_FACTURE varchar(25) NULL;
IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_LIBELLE') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_LIBELLE varchar(512) NULL;
IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_ID_UTLST') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_ID_UTLST int NULL;
IF COL_LENGTH('v2.ARTICLE_UTILISE', 'ARTUT_TAUX_TVA') IS NULL ALTER TABLE v2.ARTICLE_UTILISE ADD ARTUT_TAUX_TVA real NULL;
IF OBJECT_ID(N'v2.FACTURE') IS NULL
    CREATE TABLE v2.FACTURE (FACTU_NUMERO_FACTURE varchar(25) NOT NULL PRIMARY KEY, FACTU_NUM_DEMANDE varchar(32) NOT NULL);
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_CREATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_CREATION datetime NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_DATE_COMPTABILISATION') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_DATE_COMPTABILISATION datetime NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_ID_CLIEN') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_ID_CLIEN int NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TTC') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TTC money NULL;
IF COL_LENGTH('v2.FACTURE', 'FACTU_MONTANT_TVA') IS NULL ALTER TABLE v2.FACTURE ADD FACTU_MONTANT_TVA money NULL;
IF OBJECT_ID(N'v2.ARTICLE') IS NULL
    CREATE TABLE v2.ARTICLE (ARTCL_ID_ARTCL int NOT NULL PRIMARY KEY, ARTCL_CODE_ARTICLE_RIG varchar(18) NOT NULL, ARTCL_LIBELLE varchar(512) NOT NULL,
        ARTCL_PRIX_UHT money NOT NULL, ARTCL_TYPE_ARTICLE varchar(10) NOT NULL, ARTCL_DATE_APPLICATION datetime NOT NULL, ARTCL_STATUT varchar(1) NULL,
        ARTCL_CODE_ARTICLE_GREFFE varchar(18) NOT NULL, ARTCL_ID_ARTCA int NULL);

INSERT INTO soc.Compte (IdV2, CompteWindows, Nom, Prenom, Abrege, Civilite, Desactive) VALUES
    (2300491, 'banc.l13.vente1', 'VENDEUR', 'Carla', 'L13V1', 'Mme', 0);
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES
    (2300471, 'L13VTA', '411L13VTA', 'CLIENT VENTE A', 'C', 0, 0), (2300472, 'L13VTB', '411L13VTB', 'CLIENT VENTE B', 'D', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;

EXEC sp_executesql N'
INSERT INTO v2.FACTURE (FACTU_NUMERO_FACTURE, FACTU_NUM_DEMANDE, FACTU_DATE_CREATION, FACTU_DATE_COMPTABILISATION, FACTU_ID_CLIEN, FACTU_MONTANT_TTC) VALUES
    (''L13F001'', ''L13DV001'', DATEADD(HOUR, 10, @aujourdhui), NULL, 2300471, 7.10),
    (''L13F002'', ''L13DV002'', DATEADD(HOUR, 11, @aujourdhui), @aujourdhui, 2300472, 12.00),
    (''L13F003'', ''L13DV003'', DATEADD(HOUR, -2, @aujourdhui), NULL, 2300471, 5.00),
    (''L13F010'', ''L13DV010'', ''20250310 09:00'', ''20250311'', 2300471, 39.00),
    (''L13F011'', ''L13DV011'', ''20250311 09:00'', NULL, 2300472, 14.30);
INSERT INTO v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT, ARTUT_ID_CLIEN, ARTUT_CODE_ARTICLE, ARTUT_LIBELLE, ARTUT_QUANTITE, ARTUT_PRIX_UHT, ARTUT_MONTANT_TTC,
    ARTUT_TAUX_TVA, ARTUT_DATE_CREATION, ARTUT_NUMERO_FACTURE, ARTUT_ID_UTLST) VALUES
    -- facture du jour, non comptabilisee : « Facturés au greffe », « Facturé » ; montant HT 2 x 2,96 = 5,92
    (2300401, 2300471, ''L13KBIS'', ''Extrait banc'', 2, 2.96, 7.10, 20, DATEADD(HOUR, 9, @aujourdhui), ''L13F001'', 2300491),
    -- facture du jour, comptabilisee, code WEB : « Web », « Comptabilisé »
    (2300402, 2300472, ''WEB'', ''Commande banc'', 1, 10, 12.00, 20, DATEADD(HOUR, 9, @aujourdhui), ''L13F002'', 2300491),
    -- ECARTE a l''ouverture : tarife (sans facture) -- la case « Tarifés » n''est pas cochee par defaut ; jumeau retenu 2300401 ;
    -- retrouve par « Type d''article = Tarifés »
    (2300403, 2300471, ''L13COPIE'', ''Copie banc'', 3, 0.78, 2.81, 20, DATEADD(HOUR, 8, @aujourdhui), NULL, 2300491),
    -- ECARTE : facture de la veille, meme si l''article est du jour (la periode porte sur la FACTURE) -- jumeau retenu 2300401
    (2300404, 2300471, ''L13KBIS'', ''Extrait banc veille'', 1, 2.96, 3.55, 20, DATEADD(HOUR, 9, @aujourdhui), ''L13F003'', 2300491),
    -- ECARTE : client absent de la base (INNER JOIN CLIENT) -- jumeau retenu 2300401
    (2300405, 2300499, ''L13KBIS'', ''Extrait banc orphelin'', 1, 2.96, 3.55, 20, DATEADD(HOUR, 9, @aujourdhui), ''L13F001'', 2300491),
    -- dates fixes, pour la recherche (PROC_JVENTE.recherches)
    (2300410, 2300471, ''L13DEP'', ''Depot banc'', 1, 32.50, 39.00, 20, ''20250309'', ''L13F010'', 2300491),
    (2300411, 2300472, ''L13RAD'', ''Radiation banc'', 1, 14.30, 14.30, 0, ''20250311'', ''L13F011'', NULL),
    -- un article tarife (sans facture) du 10/03/2025 : ecarte sans « Type d''article = Tarifés » -- jumeau retenu 2300410
    (2300412, 2300471, ''L13COPIE'', ''Copie banc tarif'', 2, 0.78, 1.87, 20, ''20250310 15:00'', NULL, 2300491);', N'@aujourdhui datetime', @aujourdhui;
