-- Jeu de LecteurDepotsBeneficiaires et EcrivainDepotsBeneficiaires (« Dépôts bénéficiaires », PROC_DASBEN), lot 5 du plan
-- « RIG v3 100 % fonctionnel » (plan-676a4b19). Identifiants 1 510 2xx. Ce qui est retenu ou ecarte : PROC_DASBEN.attendu.
-- Les tables sont celles de PROC_DAS.sql (joue juste avant, ordre des noms) ; ce jeu ajoute ses lignes et ses references.
-- Le dossier de ce jeu A DEJA un BEN depose : seul BENM lui est propose (OPE_ACTES.cs:33-45) ; le non-inscrit GAMMA n'en a
-- pas : les codes « 1099 » (:52-55).

EXEC sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''RA1'')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, TailleChrono, Debut) VALUES (''RA1'', 2026, ''AC'', 4, 6, 1);
IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = ''RDB'')
    INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES (''RDB'', 2026, ''ALC'', 4, ''B'', 6, 1);
INSERT INTO v2.CODE_EVENEMENT_RCS (R_CODEEVENEM_VALEUR, R_CODEEVENEM_AMITEL)
SELECT v.c, ''1099'' FROM (VALUES (''BEN''), (''BENH''), (''BENM'')) AS v(c) WHERE NOT EXISTS (SELECT 1 FROM v2.CODE_EVENEMENT_RCS WHERE R_CODEEVENEM_VALEUR = v.c);
IF NOT EXISTS (SELECT 1 FROM v2.ABREGE_PIECE WHERE R_ABREGEPIEC_VALEUR = ''BEN'')
    INSERT INTO v2.ABREGE_PIECE (R_ABREGEPIEC_VALEUR, R_ABREGEPIEC_DEVELOPPE_EDIT) VALUES (''BEN'', ''Document relatif au bénéficiaire effectif'');';

SET IDENTITY_INSERT rec.Entreprise ON;
INSERT INTO rec.Entreprise (Id, Siren, Civilite, Denomination, EtatInfo, FicheSommaire) VALUES
    (1510201, '515102010', '', N'DASBEN ALPHA SARL', 'C', 0);
SET IDENTITY_INSERT rec.Entreprise OFF;
SET IDENTITY_INSERT rec.DossierRcs ON;
INSERT INTO rec.DossierRcs (Id, IdEntreprise, TypeRcs, NumeroGestion, Siren, DateImmatriculation, MotifImmatriculation,
    ProvisoirementSans, CompteurMaj, DateMaj, EtatDossier, EtatInfo, InscritRegistreMetiers, ModificationInterne,
    SirenProvisoire, Accre, AutoEntrepreneur, Eirl, AutoEntrepreneurRegularise) VALUES
    (1510201, 1510201, 'B1', '2010B15201', '515102010', '20100101', 'C', 0, 0, '20100101', 'A', 'C', 0, 0, 0, 0, 0, 0, 0);
SET IDENTITY_INSERT rec.DossierRcs OFF;
SET IDENTITY_INSERT rec.ActionRegistre ON;
INSERT INTO rec.ActionRegistre (Id, IdDossier, IdDepot, NumeroArrivee, DateArrivee, CodeProcessus, EtatInfo, EtatAction, SurNotification) VALUES
    (1510231, 1510201, 1510241, '2019000231', '20190601', 'DASBEN', '2', '2', 0);
SET IDENTITY_INSERT rec.ActionRegistre OFF;

-- Les saisies : une validee sur le dossier (son depot BEN est l'historique du dossier) ; une interrompue sur le non-inscrit
-- GAMMA ; ecartee : une supprimee (X).
EXEC sp_executesql N'
INSERT INTO v2.NON_INSCRIT (NINSC_ID_NINSC, NINSC_NUM_NON_INSCRIT, NINSC_TYPE_RCS, NINSC_PM, NINSC_DENOMINATION, NINSC_CIVILITE) VALUES
    (1510212, ''NI15212'', ''B1'', 1, ''DASBEN NI GAMMA SAS'', '''');
INSERT INTO v2.DEPOT_RCS (DPTRC_ID_DPTRC, DPTRC_NUM_DEPOT, DPTRC_DATE, DPTRC_CATEGORIE_DEPOT) VALUES (1510241, ''2019B000001'', ''20190601'', ''N'');
INSERT INTO v2.EVENEMENT_RCS (EVNRC_ID_EVNRC, EVNRC_ID_ACTRC, EVNRC_NUM_ORDRE, EVNRC_CODE_EVENEMENT_RCS, EVNRC_OBJET_DEPOT) VALUES (1510251, 1510231, 1, ''BEN'', 1);
INSERT INTO v2.ACTE_DEPOSE (ACTDP_ID_ACDPS, ACTDP_ID_DPTRC, ACTDP_ABREGE_PIECE, ACTDP_NUM_ORDRE, ACTDP_DATE) VALUES (1510261, 1510241, ''BEN'', 1, ''20190530'');
INSERT INTO v2.DEMANDE (DMND_ID_DMND, DMND_NUM_DEMANDE, DMND_ETAT_DEMANDE, DMND_CODE_PROSS, DMND_EN_COURS, DMND_NOM, DMND_NUM_INDICATIF,
    DMND_ID_DSRCS, DMND_ID_AUTRE, DMND_ID_ACTRC) VALUES
    (1510221, ''D1515100221'', ''V'', ''DASBEN'', 0, ''DASBEN ALPHA SARL'', ''2010B15201'', 1510201, NULL, 1510231),
    (1510222, ''D1515100222'', ''I'', ''DASBEN'', 0, ''DASBEN NI GAMMA SAS'', ''NI15212'', NULL, 1510212, NULL),
    (1510223, ''D1515100223'', ''X'', ''DASBEN'', 0, ''DASBEN SUPPRIMEE'', ''2010B15201'', 1510201, NULL, NULL);';
