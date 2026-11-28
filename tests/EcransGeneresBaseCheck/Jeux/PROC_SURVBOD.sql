-- « Surveillance BODACC » (LecteurRejetsBodacc) : des annonces BODACC et leurs types, aux colonnes de RIG_DEV (lu le 26/11/2026).
-- PROC_FIBEN.sql cree v2.BODACC avant ce jeu, sur la cle BDCCR_ID_BDCCR : la colonne de RIG_DEV, BDCCR_ID_BDCRC, est ajoutee, et
-- les deux recoivent l'identifiant. Ces annonces n'ont pas de cout ni de type d'annonce : FIBEN ne les compte pas.
-- Elles ne sont rattachees a aucune action RCS (rec.ActionRegistre) : en ajouter changerait les dossiers des autres ecrans ; elles
-- sont donc « Sans dossier RCS » (Xaction vide, OPE_REJETS_BODACC.cs:19). La branche « À corriger » (un dossier) se voit sur la base
-- de developpement.
IF OBJECT_ID(N'v2.BODACC') IS NULL
    CREATE TABLE v2.BODACC (BDCCR_ID_BDCCR int NOT NULL PRIMARY KEY, BDCCR_TYPE_BODACC varchar(5) NULL, BDCCR_DATE_PUBLICITE datetime NULL);
IF COL_LENGTH('v2.BODACC', 'BDCCR_ID_BDCRC') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_ID_BDCRC int NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_CONTROLE_QUALITE') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_CONTROLE_QUALITE varchar(255) NULL;
IF COL_LENGTH('v2.BODACC', 'BDCCR_CONTROLE_QUALITE_VERIFIE') IS NULL ALTER TABLE v2.BODACC ADD BDCCR_CONTROLE_QUALITE_VERIFIE bit NULL;
IF OBJECT_ID(N'v2.TYPE_BODACC') IS NULL
    CREATE TABLE v2.TYPE_BODACC (R_TYPEBODACC_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_TYPEBODACC_BORDEREAU varchar(3) NULL);
IF COL_LENGTH('v2.TYPE_BODACC', 'R_TYPEBODACC_BORDEREAU') IS NULL ALTER TABLE v2.TYPE_BODACC ADD R_TYPEBODACC_BORDEREAU varchar(3) NULL;
IF OBJECT_ID(N'v2.NON_INSCRIT') IS NULL CREATE TABLE v2.NON_INSCRIT (NINSC_ID_NINSC int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.NON_INSCRIT', 'NINSC_NUM_NON_INSCRIT') IS NULL ALTER TABLE v2.NON_INSCRIT ADD NINSC_NUM_NON_INSCRIT varchar(11) NULL;
IF OBJECT_ID(N'v2.INSCRIPTION_RCS') IS NULL CREATE TABLE v2.INSCRIPTION_RCS (INSRC_ID_INSRC int NOT NULL PRIMARY KEY);
IF COL_LENGTH('v2.INSCRIPTION_RCS', 'INSRC_NUM_CHRONO') IS NULL ALTER TABLE v2.INSCRIPTION_RCS ADD INSRC_NUM_CHRONO varchar(10) NULL;

-- B9A : bordereau AP (dans la liste de RIG v2) ; B9Z (R_TYPEBODACC_VALEUR est un varchar(5)) : sans bordereau (hors liste).
EXEC sp_executesql N'
INSERT INTO v2.TYPE_BODACC (R_TYPEBODACC_VALEUR, R_TYPEBODACC_BORDEREAU) VALUES (''B9A'', ''AP''), (''B9Z'', NULL);';

-- 1900801 : remarque non verifiee, publiee il y a dix jours (dans la liste). Refuses, chacun avec son jumeau accepte :
--   1900802 sans remarque (jumeau 1900801) ; 1900803 remarque deja verifiee (jumeau 1900801) ;
--   1900804 publiee le 31/12/2007 (avant la borne de 2008) -- jumeau 1900805, le 01/01/2008 ;
--   1900806 publiee demain -- jumeau 1900807, aujourd'hui a 15 h (avant aujourd'hui 23:59:59) ;
--   1900808 d'un type sans bordereau (jumeau 1900801).
EXEC sp_executesql N'
INSERT INTO v2.BODACC (BDCCR_ID_BDCCR, BDCCR_ID_BDCRC, BDCCR_TYPE_BODACC, BDCCR_DATE_PUBLICITE, BDCCR_CONTROLE_QUALITE, BDCCR_CONTROLE_QUALITE_VERIFIE) VALUES
    (1900801, 1900801, ''B9A'', DATEADD(day, -10, @aujourdhui), N''Adresse du siège incomplète'', 0),
    (1900802, 1900802, ''B9A'', DATEADD(day, -10, @aujourdhui), NULL, 0),
    (1900803, 1900803, ''B9A'', DATEADD(day, -10, @aujourdhui), N''Capital déjà vérifié'', 1),
    (1900804, 1900804, ''B9A'', ''20071231'', N''Annonce de 2007'', 0),
    (1900805, 1900805, ''B9A'', ''20080101'', N''Annonce de 2008'', 0),
    (1900806, 1900806, ''B9A'', DATEADD(day, 1, @aujourdhui), N''Annonce de demain'', 0),
    (1900807, 1900807, ''B9A'', DATEADD(hour, 15, @aujourdhui), N''Annonce du jour'', 0),
    (1900808, 1900808, ''B9Z'', DATEADD(day, -10, @aujourdhui), N''Type sans bordereau'', 0);',
    N'@aujourdhui datetime', @aujourdhui;

-- LES GESTES (EcrivainSurveillanceBodacc.cs, lot 1 du plan-f4fb097f, identifiants 31008xx) :
--   - « Word » et « Imprimer » : la liste Word du parametre LOT2_SURVBOD_LST_CQ (OPE_REJETS_BODACC.cs:158), aux valeurs de RIG_DEV
--     (libelle, maquette, FUSION_RIG, afficher avant impression, lu le 28/11/2026) ;
--   - « Modifier l'annonce » : l'annonce 3100811 est rattachee, par son action RCS 3100801, au dossier 3100801 (« À corriger ») --
--     le jumeau accepte des annonces sans dossier ci-dessus. Le dossier est RADIE et de type B2 (personne morale, sans BE ni comptes) :
--     il n'entre pas dans les files des autres ecrans (relances, injonctions, radiations d'office ne lisent que des dossiers non radies).
IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
    CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL,
        PRMED_RECTO_VERSO bit NOT NULL DEFAULT 0, PRMED_AFFICHER_AVANT_IMPRESSION bit NOT NULL DEFAULT 0, PRMED_CODE_CFECP varchar(50) NULL,
        PRMED_LISTE varchar(4000) NOT NULL DEFAULT '', PRMED_CODE_ENTITE_SOURCE varchar(16) NULL, PRMED_CODE_PROCESSUS varchar(40) NULL,
        PRMED_CODE_MAQUETTE varchar(40) NULL, PRMED_TYPE_GENERATION varchar(15) NULL, PRMED_NOMBRE_EXEMPLAIRES int NOT NULL DEFAULT 1,
        PRMED_READ_ONLY bit NOT NULL DEFAULT 0, PRMED_NUM_GROUPE_IMPRESSION varchar(30) NULL, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC varchar(50) NULL);
IF NOT EXISTS (SELECT 1 FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = 'LOT2_SURVBOD_LST_CQ')
    EXEC sp_executesql N'
    INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_CODE_MAQUETTE, PRMED_TYPE_GENERATION, PRMED_AFFICHER_AVANT_IMPRESSION,
        PRMED_RECTO_VERSO, PRMED_NOMBRE_EXEMPLAIRES)
    VALUES (''LOT2_SURVBOD_LST_CQ'', N''Liste des contrôles qualité'', ''LOT2_SURVBOD_LST_CQ'', ''FUSION_RIG'', 1, 0, 1);';

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire)
VALUES (3100801, '531008011', '', N'SURVBOD ALPHA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, DateRadiation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise)
VALUES (3100801, 3100801, 'B2', '2010B31081', '531008011', '20100315', '20150101', 'C', 0, 0, '20150101', 'R', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdAnnonceBodacc, CodeProcessus, EtatInfo, EtatAction, SurNotification)
VALUES (3100801, 3100801, 3100811, 'BANC31', 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;
EXEC sp_executesql N'
INSERT INTO v2.BODACC (BDCCR_ID_BDCCR, BDCCR_ID_BDCRC, BDCCR_TYPE_BODACC, BDCCR_DATE_PUBLICITE, BDCCR_CONTROLE_QUALITE, BDCCR_CONTROLE_QUALITE_VERIFIE)
VALUES (3100811, 3100811, ''B9A'', DATEADD(day, -5, @aujourdhui), N''Dirigeant sans date de naissance'', 0);',
    N'@aujourdhui datetime', @aujourdhui;
