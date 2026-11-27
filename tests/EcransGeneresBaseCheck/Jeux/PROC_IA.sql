-- Jeu de LecteurAnalyseIa (« Analyse IA », PROC_IA), lot 15 du plan-676a4b19. Identifiants 25004xx. Ce qui est retenu ou ecarte, et
-- pourquoi : .attendu, .recherches. Types : ceux de RIG_DEV (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 27/11/2026), colonnes
-- lues. La reponse du service est un JSON au format de IAHelper.ReponseJson (IAHelper.cs:310-345). Aucun autre jeu ne cree cette table.
IF OBJECT_ID(N'v2.DEMAT_LIASSE_CFE') IS NULL
    CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY, DMTLCF_DATE_CREATION datetime NOT NULL,
        DMTLCF_NUM_GESTION varchar(10) NULL, DMTLCF_ORIGINE varchar(16) NULL, DMTLCF_NUMERO_NATIONAL_INPI varchar(50) NULL,
        DMTLCF_REPONSE_IA varchar(max) NULL);

INSERT INTO v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF, DMTLCF_DATE_CREATION, DMTLCF_NUM_GESTION, DMTLCF_ORIGINE, DMTLCF_NUMERO_NATIONAL_INPI, DMTLCF_REPONSE_IA) VALUES
    -- formalite INPI deja analysee : « Analysée », sa fiche porte le resultat
    (2500401, '20260310', '2026B25401', 'INPI_FORMA', 'J00025040100', N'{"verificationsChamps":[{"champ":"DENOMINATION","resultat":"CORRESPOND","explication":"Identique aux statuts","indiceConfiance":"0.95"},{"champ":"MONTANT_CAPITAL","resultat":"PAS DE CORRESPONDANCE","explication":"1000 dans les statuts, 1500 saisi","indiceConfiance":"0.42"}],"verificationGlobale":{"nombreChampsVerifies":2,"nombreCorrespondances":1,"nombrePasCorrespondances":1,"nombreProches":0,"nombreNonDisponibles":0,"indiceConfiance":"0.7","commentaire":"Capital a verifier"}}'),
    -- formalite INPI pas encore analysee (reponse vide) : « À analyser » -- jumeau 2500401
    (2500402, '20260311', NULL, 'INPI_FORMA', 'J00025040200', NULL),
    -- regularisation INPI, reponse blanche : « À analyser », groupe « Régularisations INPI »
    (2500403, '20260312', NULL, 'INPI_FORMA_MAJ', 'J00025040300', '  '),
    -- ECARTEE : liasse CFE sans numero national INPI (la V2 ne l'analyse pas) -- jumeau 2500402
    (2500404, '20260313', NULL, 'CREFO_FORMA', NULL, NULL);
