using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Arrivée du courrier » (PROC_ARRIVEE, Pont/EcranArrivee.cs + EcranArriveeBase.cs) : « Enregistrer une arrivée », la saisie
/// OPE_ARRIVEE de RIG v2 (Arrivee.CreateArrivee, Arrivee.cs:28-42 ; écriture : Serveur/Socle/EcransMain/EcritureArrivee.cs), jouée
/// par le pont puis relue par le pont : l'arrivée porte le numéro ARRD donné, son mode et la case « A ». Lot 7 du plan « RIG v3
/// complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU : MODE_ARRIVEE (absente : 500 au 28/11) et ARRIVEE créées si absentes, aux types de RIG_DEV (RigDatabase\dbo\Tables\
/// MODE_ARRIVEE.sql, ARRIVEE.sql : ARRV_ID_ARRV IDENTITY) ; les colonnes de DEMANDE, DEMANDE_EDITION, DEMAT_LIASSE_CFE et DEMAT_DAS
/// que la lecture joint (LectureArrivees.cs) ; deux modes utilisables en saisie (PTT, AUT) ; le compteur ARRD de RIG_DEV (structure
/// LAQC, année sur 2 chiffres, lettre A, chrono sur 5 : Pont/EcranCompteurs.cs) s'il manque.
/// </summary>
public sealed class SondeArrivee : ISondeEcran
{
    public string Code => "PROC_ARRIVEE";

    public string? Jeu => """
        IF OBJECT_ID('v2.MODE_ARRIVEE') IS NULL
            CREATE TABLE v2.MODE_ARRIVEE (R_MODEARRIVE_VALEUR varchar(5) NOT NULL PRIMARY KEY, R_MODEARRIVE_LIBELLE_ECRAN varchar(35) NOT NULL,
                R_MODEARRIVE_SYNONYME varchar(16) NOT NULL, R_MODEARRIVE_UTILISABLE_SAISIE bit NOT NULL);
        IF COL_LENGTH('v2.MODE_ARRIVEE', 'R_MODEARRIVE_UTILISABLE_SAISIE') IS NULL
            ALTER TABLE v2.MODE_ARRIVEE ADD R_MODEARRIVE_UTILISABLE_SAISIE bit NOT NULL DEFAULT (1);
        IF OBJECT_ID('v2.ARRIVEE') IS NULL
            CREATE TABLE v2.ARRIVEE (ARRV_NUM_ARRIVEE varchar(11) NULL, ARRV_MODE_ARRIVEE varchar(5) NOT NULL, ARRV_DATEHEURE datetime NULL,
                ARRV_NOM varchar(255) NULL, ARRV_REFERENCES varchar(35) NULL, ARRV_PAIEMENT_JOINT bit NOT NULL DEFAULT (0),
                ARRV_CASE_COURRIER varchar(10) NULL, ARRV_ID_ARRV int IDENTITY(3700001, 1) NOT NULL PRIMARY KEY);
        IF OBJECT_ID('v2.DEMANDE') IS NULL
            CREATE TABLE v2.DEMANDE (DMND_ID_DMND int NOT NULL PRIMARY KEY, DMND_NUM_DEMANDE varchar(11) NOT NULL, DMND_ETAT_DEMANDE varchar(1) NOT NULL);
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ARRV') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ARRV int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_DMND_MERE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_DMND_MERE int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_UTLST') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_UTLST int NULL;
        IF COL_LENGTH('v2.DEMANDE', 'DMND_LIBELLE_ECRAN') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LIBELLE_ECRAN varchar(35) NULL;
        IF OBJECT_ID('v2.DEMANDE_EDITION') IS NULL CREATE TABLE v2.DEMANDE_EDITION (DMNED_ID_DMNED int NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_VALEUR_ID_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_VALEUR_ID_SOURCE varchar(40) NULL;
        IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_MAQUETTE varchar(40) NULL;
        IF OBJECT_ID('v2.DEMAT_LIASSE_CFE') IS NULL
            CREATE TABLE v2.DEMAT_LIASSE_CFE (DMTLCF_ID_DMTLCF int NOT NULL PRIMARY KEY, DMTLCF_ID_DMND int NULL, DMTLCF_ARCHIVAGE_ETAT varchar(2) NULL);
        IF OBJECT_ID('v2.DEMAT_DAS') IS NULL
            CREATE TABLE v2.DEMAT_DAS (DMTDAS_ID_DMTDAS int NOT NULL PRIMARY KEY, DMTDAS_ID_DMND int NULL, DMTDAS_ARCHIVAGE_ETAT varchar(2) NULL);
        IF COL_LENGTH('v2.DEMAT_LIASSE_CFE', 'DMTLCF_ARCHIVAGE_ETAT') IS NULL ALTER TABLE v2.DEMAT_LIASSE_CFE ADD DMTLCF_ARCHIVAGE_ETAT varchar(2) NULL;
        IF COL_LENGTH('v2.DEMAT_DAS', 'DMTDAS_ARCHIVAGE_ETAT') IS NULL ALTER TABLE v2.DEMAT_DAS ADD DMTDAS_ARCHIVAGE_ETAT varchar(2) NULL;
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'ARRD')
            INSERT INTO num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, Debut)
            VALUES ('ARRD', YEAR(@aujourdhui), DATEPART(dayofyear, @aujourdhui), 'Numéro des arrivées, type Demande', 'LAQC', 2, 'A', '', 5, 1);
        EXEC sp_executesql N'
        IF NOT EXISTS (SELECT 1 FROM v2.MODE_ARRIVEE WHERE R_MODEARRIVE_VALEUR = ''PTT'')
            INSERT INTO v2.MODE_ARRIVEE (R_MODEARRIVE_VALEUR, R_MODEARRIVE_LIBELLE_ECRAN, R_MODEARRIVE_SYNONYME, R_MODEARRIVE_UTILISABLE_SAISIE)
            VALUES (''PTT'', ''Courrier'', ''PTT'', 1);
        IF NOT EXISTS (SELECT 1 FROM v2.MODE_ARRIVEE WHERE R_MODEARRIVE_VALEUR = ''AUT'')
            INSERT INTO v2.MODE_ARRIVEE (R_MODEARRIVE_VALEUR, R_MODEARRIVE_LIBELLE_ECRAN, R_MODEARRIVE_SYNONYME, R_MODEARRIVE_UTILISABLE_SAISIE)
            VALUES (''AUT'', ''Autre'', ''AUT'', 1);';
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var r = await EcranArrivee.EnregistrerAsync(c.Client, c.Greffe, "PTT", c.Jour.ToString("yyyy-MM-dd"));
        if (!r.Fait || r.Ids is not [var numero]) return new(false, false, $"Enregistrer une arrivée : refusé « {r.Message} »");

        var relue = await EcranArrivee.TrouverAsync(c.Client, c.Greffe,
            new CriteresArrivee(numero, "", "", "", "", null, null, false, false, 50));
        return relue is { Mode: "serveur", Lignes: [var a] } && a.Numero == numero && a.ModeLisible == "Courrier" && a.Case == "A"
            ? new(true, true, $"Enregistrer une arrivée fait (« {r.Message} »), relue par le pont : n°{a.Numero}, {a.ModeLisible}, case {a.Case}, {a.Horodatage}")
            : new(true, false, $"Enregistrer une arrivée rendu fait (n°{numero}), mais relu : {relue.Mode}, {relue.Lignes.Length} ligne(s)"
                               + (relue.Lignes.FirstOrDefault() is { } l ? $", mode « {l.ModeLisible} », case « {l.Case} »" : ""));
    }
}
