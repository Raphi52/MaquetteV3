namespace ParcoursCompletCheck;

/// <summary>
/// Le Jeu d'une sonde en DEUX TEMPS : la structure (tables créées si absentes, colonnes ajoutées si manquantes), puis les données. SQL Server
/// compile un lot entier avant de le jouer : un INSERT qui nomme une colonne qu'un ALTER du même lot vient d'ajouter est refusé (erreur 207,
/// mesuré par les jeux de tests/EcransGeneresBaseCheck, d'où leurs « EXEC sp_executesql »). Les données passent donc par sp_executesql, qui
/// les compile APRÈS la structure, avec le paramètre @aujourdhui du banc ; leurs apostrophes sont doublées ici, pas à la main.
/// Lot 11 du plan « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
/// </summary>
internal static class JeuSonde
{
    /// <summary>Le lot <paramref name="structure"/>, puis <paramref name="donnees"/> jouées par sp_executesql (paramètre @aujourdhui).</summary>
    internal static string EnDeuxTemps(string structure, string donnees)
        => $"{structure}\nEXEC sp_executesql N'{donnees.Replace("'", "''")}', N'@aujourdhui datetime', @aujourdhui = @aujourdhui;";

    /// <summary>Les colonnes de v2.DEMANDE qu'écrivent les demandes de RIG v2 (EditionsV2, CreationV2), aux types de RIG_DEV
    /// (INFORMATION_SCHEMA.COLUMNS de SQL-DEV\DEV, lu le 28/11/2026), et les tables de son verrou (VerrouDemandeV2).</summary>
    internal const string Demandes = """
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NUM_INDICATIF') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NUM_INDICATIF varchar(14) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_LES_NUMEROS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LES_NUMEROS varchar(128) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_EN_COURS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_EN_COURS bit NOT NULL DEFAULT (0);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_MERE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_MERE int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_CREATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_CREATION datetime NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_DATE_VALIDATION') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_DATE_VALIDATION datetime NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ACTRC') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ACTRC int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DSRCS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DSRCS int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_CODE_PROSS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_CODE_PROSS varchar(40) NULL;
        IF OBJECT_ID(N'v2.AFFAIRE') IS NULL
            CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_DATE_CREATION datetime NOT NULL);
        IF OBJECT_ID(N'v2.DOSSIER_DEMANDE') IS NULL
            CREATE TABLE v2.DOSSIER_DEMANDE (DSSDM_ID_DSSDM int NOT NULL PRIMARY KEY, DSSDM_ID_DMND int NOT NULL,
                DSSDM_CODE_SEQUENCEUR varchar(20) NOT NULL, DSSDM_VALEUR_SEQUENCEUR varchar(100) NULL);
        IF OBJECT_ID(N'v2.ETAT_DEMANDE') IS NULL
            CREATE TABLE v2.ETAT_DEMANDE (R_ETATDEMAND_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_ETATDEMAND_LIBELLE_ECRAN varchar(35) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'DMD')
            INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('DMD', YEAR(@aujourdhui), 'LAQC', 2, 'D', 5, 1);
        """;

    /// <summary>v2.LIGNE_EDITION (les éditions « lot un » : Serveur/Socle/EcransMain/LignesEditionV2.cs), aux types de RIG_DEV. Celle du
    /// banc, créée par un autre jeu, n'a pas LGEDT_CODE_MODELE_DOC (mesuré le 28/11/2026, lot 7) : ajoutée NULL, la table ayant des lignes.</summary>
    internal const string LignesEdition = """
        IF OBJECT_ID(N'v2.LIGNE_EDITION') IS NULL
            CREATE TABLE v2.LIGNE_EDITION (LGEDT_ID_LGEDT int IDENTITY(4100001, 1) NOT NULL PRIMARY KEY, LGEDT_ID_DMND int NULL,
                LGEDT_CODE_MODELE_DOC varchar(50) NOT NULL);
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ID_DMND') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ID_DMND int NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_CODE_MODELE_DOC') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_CODE_MODELE_DOC varchar(50) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ETAT') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ETAT varchar(1) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_TRANSMISSION') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_TRANSMISSION varchar(20) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_NUM_ORDRE') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_NUM_ORDRE smallint NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_NOMBRE') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_NOMBRE smallint NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ETAT_EDT') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ETAT_EDT varchar(1) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_ETAT_TRS') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_ETAT_TRS varchar(1) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_SCEAU') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_SCEAU varchar(255) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_IMP_BAC') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_IMP_BAC varchar(10) NULL;
        IF COL_LENGTH('v2.LIGNE_EDITION', 'LGEDT_IMPRIMER') IS NULL ALTER TABLE v2.LIGNE_EDITION ADD LGEDT_IMPRIMER bit NULL;
        """;
}
