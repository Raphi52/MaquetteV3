-- « Pilotage » (LecteurPilotage) : la table PILOTAGE_REQUETES de RIG v2 (colonnes et types de RIG_DEV, lu le 26/11/2026) et
-- des requetes de controle, chacune pour une regle de OPE_GENERER.cs. Les chiffres comptent des groupes d'impression d'un lot
-- « BANC9_PILOTAGE » absent de la table LOT : « Relancer un lot » ne les voit pas (son lecteur part des lots).
IF OBJECT_ID(N'v2.PILOTAGE_REQUETES') IS NULL
    CREATE TABLE v2.PILOTAGE_REQUETES (PILOT_ID_PILOT int NOT NULL PRIMARY KEY, PILOT_SQL varchar(max) NULL, PILOT_COLONNE int NULL,
        PILOT_LIGNE int NULL, PILOT_LIBELLE varchar(255) NULL, PILOT_TRAITEMENT varchar(50) NULL, PILOT_FEUILLE varchar(10) NULL);
IF OBJECT_ID(N'v2.GROUPE_IMPRESSION') IS NULL
    CREATE TABLE v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE varchar(40) NOT NULL PRIMARY KEY, GPIMP_NOM_LOT varchar(255) NULL,
        GPIMP_DATE_DEBUT datetime NULL, GPIMP_DATE_FIN datetime NULL, GPIMP_NOM_IMPRIMANTE varchar(255) NULL, GPIMP_NB_TENTATIVES tinyint NULL,
        GPIMP_PHASE_TRAITEMENT varchar(10) NOT NULL);

-- La periode par defaut : le mois precedent (InitDate, OPE_GENERER.cs:123-143), fin a 23:59:59 (:253-254).
-- P01 le 10 du mois precedent et P02 son dernier jour a 23:00 : dans la periode (P02 : la fin portee a 23:59:59) ; jumeaux
-- refuses : P03 le 1er du mois courant a 0 h (apres la fin), P05 la veille du 1er du mois precedent (avant le debut). P04 : le 10
-- du meme mois un an plus tot, la colonne « un an plus tôt » (AddYears(-1), :170-171).
DECLARE @debut date = DATEADD(month, -1, DATEFROMPARTS(YEAR(@aujourdhui), MONTH(@aujourdhui), 1));
INSERT INTO v2.GROUPE_IMPRESSION (GPIMP_NUM_GROUPE, GPIMP_NOM_LOT, GPIMP_DATE_DEBUT, GPIMP_PHASE_TRAITEMENT) VALUES
    ('BANC9-P01', 'BANC9_PILOTAGE', DATEADD(hour, 10, CAST(DATEADD(day, 9, @debut) AS datetime)), 'E'),
    ('BANC9-P02', 'BANC9_PILOTAGE', DATEADD(hour, 23, CAST(DATEADD(day, -1, DATEADD(month, 1, @debut)) AS datetime)), 'E'),
    ('BANC9-P03', 'BANC9_PILOTAGE', CAST(DATEADD(month, 1, @debut) AS datetime), 'E'),
    ('BANC9-P04', 'BANC9_PILOTAGE', DATEADD(hour, 10, CAST(DATEADD(day, 9, DATEADD(year, -1, @debut)) AS datetime)), 'E'),
    ('BANC9-P05', 'BANC9_PILOTAGE', DATEADD(hour, 12, CAST(DATEADD(day, -1, @debut) AS datetime)), 'E');

-- La feuille RCS lit des fiches rec.* SOUS LEURS NOMS V2 (Serveur/Recopie/FichesV3.cs) : DOSSIER_RCS, ACTION_RCS, CLIENT. Deux
-- dossiers, quatre actions « BANC9 » : A01 (le 10) et A02 (le dernier jour a 23 h) du dossier D01 dans le mois precedent ; A03 (le
-- 1er du mois courant, hors periode) et A04 (un an plus tot) du dossier D02.
SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1900601, NULL, '', 'BANC9 PILOTAGE UN', 'C', 0), (1900602, NULL, '', 'BANC9 PILOTAGE DEUX', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1900601, 1900601, 'B1', '1971B19601', NULL, '19710101', 'C', 0, 0, '19710101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0),
    (1900602, 1900602, 'B1', '1971B19602', NULL, '19710101', 'C', 0, 0, '19710101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, CodeProcessus, DateArrivee, EtatInfo, EtatAction, SurNotification) VALUES
    (1900601, 1900601, 'BANC9', DATEADD(hour, 10, CAST(DATEADD(day, 9, @debut) AS datetime)), 'C', 'V', 0),
    (1900602, 1900601, 'BANC9', DATEADD(hour, 23, CAST(DATEADD(day, -1, DATEADD(month, 1, @debut)) AS datetime)), 'C', 'V', 0),
    (1900603, 1900602, 'BANC9', CAST(DATEADD(month, 1, @debut) AS datetime), 'C', 'V', 0),
    (1900604, 1900602, 'BANC9', DATEADD(hour, 10, CAST(DATEADD(day, 9, DATEADD(year, -1, @debut)) AS datetime)), 'C', 'V', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- ENCAISS (TraiterEncaiss, OPE_GENERER.cs:365-407) : ARTICLE_UTILISE et VENTILATION sont des tables V2 (colonnes de RIG_DEV, lu le
-- 27/11/2026 ; ARTUT_CODE_ARTICLE et ARTUT_QUANTITE y sont NOT NULL, laissees nulles ici pour les jeux qui creent la table sans
-- elles). Un client « C » compte son emolument : 40 x 2 + 20 = 100. Les clients divers, le reste de leur ventilation apres leurs
-- debours, au plus l'emolument : C02 ventile 80, debours 5 + 3 + 2 -> 70, plafonne a 50 ; C03 ventile 30, debours 10 -> 20 ; C04
-- ventile 10, debours 15 -> rien (reste negatif). Periode : 100 + 50 + 20 + 0 = 170. Jumeaux : 1 000 le 1er du mois courant (hors
-- periode) ; un an plus tot, le client C : 7. Les debours d'un client divers sont a 0, jamais NULL : la somme de RIG v2
-- (artut_montant_inpi + artut_montant_bodacc + artut_montant_autre, :386) vaut NULL des qu'un montant l'est (mesure au banc : 190).
IF OBJECT_ID(N'v2.ARTICLE_UTILISE') IS NULL
    CREATE TABLE v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT int NOT NULL PRIMARY KEY, ARTUT_ID_AFFRE int NULL, ARTUT_MONTANT_TTC money NOT NULL,
        ARTUT_ID_INSTN int NULL, ARTUT_ID_CLIEN int NULL, ARTUT_CODE_ARTICLE varchar(32) NULL, ARTUT_PRIX_UHT money NULL,
        ARTUT_QUANTITE smallint NULL, ARTUT_MONTANT_INPI money NULL, ARTUT_MONTANT_BODACC money NULL, ARTUT_MONTANT_AUTRE money NULL,
        ARTUT_DATE_CREATION datetime NULL);
IF OBJECT_ID(N'v2.VENTILATION') IS NULL
    CREATE TABLE v2.VENTILATION (VENTI_ID_VENTI int NOT NULL PRIMARY KEY, VENTI_ID_PYEUR int NULL, VENTI_ID_ENCMT int NULL,
        VENTI_ID_AFFRE int NULL, VENTI_ID_CLIEN int NULL, VENTI_MONTANT money NOT NULL, VENTI_DATE_VENTILATION datetime NULL);
SET IDENTITY_INSERT rec.Client ON;
INSERT INTO rec.Client (Id, Abrege, NumeroCompte, Nom, Type, EstComptePc, EstCompteWeb) VALUES
    (1900601, 'B9COMPTE', '411B9COMPTE', 'BANC9 COMPTE', 'C', 0, 0), (1900602, 'B9DIV1', '411B9DIV1', 'BANC9 DIVERS UN', 'D', 0, 0),
    (1900603, 'B9DIV2', '411B9DIV2', 'BANC9 DIVERS DEUX', 'D', 0, 0), (1900604, 'B9DIV3', '411B9DIV3', 'BANC9 DIVERS TROIS', 'D', 0, 0);
SET IDENTITY_INSERT rec.Client OFF;
DECLARE @dixieme datetime = DATEADD(hour, 10, CAST(DATEADD(day, 9, @debut) AS datetime));
INSERT INTO v2.ARTICLE_UTILISE (ARTUT_ID_ARTUT, ARTUT_ID_AFFRE, ARTUT_MONTANT_TTC, ARTUT_ID_CLIEN, ARTUT_CODE_ARTICLE, ARTUT_PRIX_UHT,
    ARTUT_QUANTITE, ARTUT_MONTANT_INPI, ARTUT_MONTANT_BODACC, ARTUT_MONTANT_AUTRE, ARTUT_DATE_CREATION) VALUES
    (1900601, 1900611, 96, 1900601, 'BANC9', 40, 2, NULL, NULL, NULL, @dixieme),
    (1900602, 1900611, 24, 1900601, 'BANC9', 20, 1, NULL, NULL, NULL, @dixieme),
    (1900603, 1900612, 60, 1900602, 'BANC9', 50, 1, 5, 3, 2, @dixieme),
    (1900604, 1900613, 60, 1900603, 'BANC9', 50, 1, 10, 0, 0, @dixieme),
    (1900605, 1900614, 48, 1900604, 'BANC9', 40, 1, 0, 0, 15, @dixieme),
    (1900606, 1900611, 1200, 1900601, 'BANC9', 1000, 1, NULL, NULL, NULL, CAST(DATEADD(month, 1, @debut) AS datetime)),
    (1900607, 1900611, 8.4, 1900601, 'BANC9', 7, 1, NULL, NULL, NULL, DATEADD(year, -1, @dixieme));
INSERT INTO v2.VENTILATION (VENTI_ID_VENTI, VENTI_ID_AFFRE, VENTI_ID_CLIEN, VENTI_MONTANT) VALUES
    (1900601, 1900612, 1900602, 80), (1900602, 1900613, 1900603, 30), (1900603, 1900614, 1900604, 10);

-- Les cellules. 1900603, 1900604, 1900611 : des fiches rec.* lues sous leurs noms V2 (1900611 : avec alias) ; 1900612 : une
-- colonne propre a la V2 (DSSRC_ETAT_TRANSMISSION, FichesV3.cs : ColonnesPropres), nommee ; 1900613 : ENCAISS sans ligne, la
-- cellule reste vide (:289). 1900606 (feuille « 3 ») n'est pas generee (garde :273) ; jumeau : 1900601, feuille 1. 1900607 (requete vide)
-- est ignoree (:282) ; jumeau : 1900602. Les autres disent leur valeur, ou pourquoi la V3 ne la calcule pas.
INSERT INTO v2.PILOTAGE_REQUETES (PILOT_ID_PILOT, PILOT_FEUILLE, PILOT_LIGNE, PILOT_COLONNE, PILOT_LIBELLE, PILOT_TRAITEMENT, PILOT_SQL) VALUES
    (1900601, '1', 7, 2, 'Groupes du banc', NULL,
     'SELECT COUNT(*) AS NB FROM GROUPE_IMPRESSION WHERE GPIMP_NOM_LOT = ''BANC9_PILOTAGE'' AND GPIMP_DATE_DEBUT BETWEEN @DateMin AND @DateMax'),
    (1900602, '2', 7, 2, 'Groupes du banc sans année précédente', 'PASANNEEPREC',
     'SELECT COUNT(*) as ''nb'' FROM [dbo].[GROUPE_IMPRESSION] WHERE GPIMP_NOM_LOT = ''BANC9_PILOTAGE'' AND GPIMP_DATE_DEBUT BETWEEN @DateMin AND @DateMax'),
    (1900603, '1', 8, 2, 'Immatriculations du banc', NULL,
     'SELECT COUNT(DSSRC_NUM_GESTION) AS NB FROM DOSSIER_RCS INNER JOIN ACTION_RCS ON DSSRC_ID_DSRCS = ACTRC_ID_DSRCS WHERE ACTRC_CODE_PROSS = ''BANC9'' AND ACTRC_DATE_ARRIVEE BETWEEN @DateMin AND @DateMax'),
    (1900604, '1', 9, 6, 'Encaissements du banc', 'ENCAISS',
     'SELECT case WHEN clien_type = ''C'' THEN -1 ELSE ARTUT_ID_AFFRE END as IdAffre, sum(artut_prix_uht * artut_quantite) as MontantEmol, case WHEN clien_type = ''C'' THEN -1 ELSE ARTUT_ID_CLIEN END as IdClient, clien_type FROM ARTICLE_UTILISE WITH (nolock) inner join client WITH (nolock) on clien_id_clien = artut_id_clien WHERE ARTUT_CODE_ARTICLE = ''BANC9'' AND ARTUT_DATE_CREATION BETWEEN @DateMin AND @DateMax group by clien_type, ARTUT_ID_AFFRE, artut_id_clien order by IdAffre, clien_type, IdClient'),
    (1900611, '1', 12, 2, 'Dossiers du banc par alias', NULL,
     'SELECT COUNT(DISTINCT d.DSSRC_ID_DSRCS) AS NB FROM DOSSIER_RCS d INNER JOIN ACTION_RCS AS a ON d.DSSRC_ID_DSRCS = a.ACTRC_ID_DSRCS WHERE a.ACTRC_CODE_PROSS = ''BANC9'' AND a.ACTRC_DATE_ARRIVEE BETWEEN @DateMin AND @DateMax'),
    (1900612, '1', 13, 2, 'Transmissions du banc', NULL,
     'SELECT COUNT(*) AS NB FROM DOSSIER_RCS WHERE DSSRC_ETAT_TRANSMISSION = ''M'' AND DSSRC_ID_DSRCS > 1900600'),
    (1900613, '1', 14, 6, 'Encaissements du banc sans ligne', 'ENCAISS',
     'SELECT -1 as IdAffre, sum(artut_prix_uht) as MontantEmol, -1 as IdClient, clien_type FROM ARTICLE_UTILISE inner join CLIENT on clien_id_clien = artut_id_clien WHERE ARTUT_CODE_ARTICLE = ''BANC9_AUCUN'' AND ARTUT_DATE_CREATION BETWEEN @DateMin AND @DateMax group by clien_type'),
    (1900605, '2', 8, 2, 'Requête déclarée du banc', NULL, 'declare @x int = 1 select @x as NB from PILOTAGE_REQUETES'),
    (1900606, '3', 7, 2, 'Feuille inconnue du banc', NULL, 'SELECT 1 AS NB'),
    (1900607, '2', 9, 2, 'Sans requête du banc', NULL, '   '),
    (1900608, '2', 10, 2, 'Table absente du banc', NULL, 'SELECT COUNT(*) AS NB FROM TABLE_ABSENTE_BANC9'),
    (1900609, '1', 10, 2, 'Division du banc', NULL, 'SELECT 1 / 0 AS NB FROM PILOTAGE_REQUETES'),
    (1900610, '1', 11, 2, 'Valeur nulle du banc', NULL, 'SELECT CAST(NULL AS int) AS NB');
