using System.Net.Http;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.EcransMain;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc du LOT 16 du plan « RIG v3 complet, mesure par le banc du parcours complet » (28/11/2026) : le CERTIFICAT DE DEPOT de
// « Dépôts judiciaires » (EcritureDepotsJud.cs, OPE_DEPOT.cs:234-288 de RIG v2), joue par le pont (EcranDepotsJud.EnregistrerAsync)
// a la fin de Program.cs, sur la meme base jetable et le meme serveur en memoire. Ses donnees restent dans la base gardee
// (RIGV3_BANC_GARDER) : la capture du lot 16 y joue le geste a l'ecran.
//   - REFUS : la case « Editer un certificat de dépôt » cochee sans le parametre d'edition DEPOT_INS -- RIG v2 echoue sur sa demande
//     nulle (OPE_DEPOT.cs:283) ; rien d'ecrit (aucun depot, journal inchange) ;
//   - JUMEAU ACCEPTE (meme instance, meme code) : la case decochee, son defaut dans RIG v2 (OPE_EVENEMENT.Designer.cs:571) -- le depot
//     s'ecrit, aucun certificat ;
//   - FAIT : le parametre DEPOT_INS pose (sa liste de RIG_DEV), la case cochee -- « Certificat de dépôt - <numero> » dans la file des
//     editions, rattache a la demande DEPOTJUD, source DEPOT_PC = le numero source du depot, envoi au coffre, la liste du parametre et
//     LISTE_ENTITE_DESTINATION dans DMNED_PARAMETRE_GENERATION.
// L'ENTREE QUI REND LA SECTION ROUGE si l'ecriture etait fausse : la case cochee (sans elle, aucun certificat : vu rouge sur le code
// d'avant le lot 16, « certificat dans la file : 0 »). Identifiants : a partir de 4 600 000 ((30 + 16) x 100 000), codes « …46 ».
// Les tables deja creees par Program.cs (INSTANCE, CODE_AFFAIRE, CODE_EVE_PRO, CATEGORIE_DEPOT, EVENEMENT_PROCEDURE, DEMANDE,
// DEMANDE_EDITION) recoivent les colonnes que le geste lit et ecrit, aux types de RIG_DEV (INFORMATION_SCHEMA de SQL-DEV\DEV).

/// <summary>Ce que Program.cs prete au banc du lot 16.</summary>
internal sealed record BancParcoursLot16(Func<string, (string Nom, object? Valeur)[], Task> ExecuterSql, Action<bool, string> Verifier,
                                         string Connexion, ClientServeur Client, string Greffe, DateTime Jour)
{
    internal const string Role = "2026R46001", CodeEvenement = "DJ46";

    private Task Sql(string sql) => ExecuterSql(sql, [("@aujourdhui", Jour)]);

    private async Task<string> Valeur(string sql)
    {
        await using var cn = new SqlConnection(Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    public async Task JouerAsync()
    {
        await Sql("""
            IF COL_LENGTH('v2.INSTANCE', 'INSTN_TYPE_INSTANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TYPE_INSTANCE varchar(2) NULL;
            IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_PREVE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_PREVE int NULL;
            IF COL_LENGTH('v2.INSTANCE', 'INSTN_NUMERO_SOURCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NUMERO_SOURCE varchar(36) NULL;
            IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_TYPE_INSTANCE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_TYPE_INSTANCE varchar(2) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_TYPE_EVE_PRO varchar(1) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_UTILISABLE_SAISIE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_UTILISABLE_SAISIE bit NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_AVEC_DEPOT') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_AVEC_DEPOT bit NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_CATEGORIE_DEPOT_INS') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_CATEGORIE_DEPOT_INS varchar(3) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_COMPATIBLE_OP') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_COMPATIBLE_OP bit NOT NULL DEFAULT (0);
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_AVEC_PIECE_NUMERISE_INSMES bit NOT NULL DEFAULT (0);
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_NATURE_DOC_GED') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_NATURE_DOC_GED varchar(50) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_TYPE_DEPOSANT_GED') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_TYPE_DEPOSANT_GED varchar(50) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_CODE_EVE_PRO_FUTUR') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_CODE_EVE_PRO_FUTUR varchar(10) NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_FIXE tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_FIXE tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_FIXE') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_FIXE tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_MAXI tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_MAXI tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_MAXI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_MAXI tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_MOIS_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_MOIS_DATE_MINI tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_JOURS_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_JOURS_DATE_MINI tinyint NULL;
            IF COL_LENGTH('v2.CODE_EVE_PRO', 'R_CODEEVEPRO_SEMAINES_DATE_MINI') IS NULL ALTER TABLE v2.CODE_EVE_PRO ADD R_CODEEVEPRO_SEMAINES_DATE_MINI tinyint NULL;
            IF COL_LENGTH('v2.CATEGORIE_DEPOT', 'R_CATEGORIED_TYPE_NUM_DEPOT') IS NULL ALTER TABLE v2.CATEGORIE_DEPOT ADD R_CATEGORIED_TYPE_NUM_DEPOT varchar(5) NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUM_ORDRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUM_ORDRE smallint NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_DELAI_EXPIRE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_DELAI_EXPIRE bit NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NUMERO_SOURCE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NUMERO_SOURCE varchar(36) NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_TYPE_EVE_PRO') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_TYPE_EVE_PRO varchar(1) NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_AVEC_PIECE_NUMERISE') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_AVEC_PIECE_NUMERISE bit NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_NATURE_DOC_GED') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_NATURE_DOC_GED varchar(50) NULL;
            IF COL_LENGTH('v2.EVENEMENT_PROCEDURE', 'EVNPR_TYPE_DEPOSANT_GED') IS NULL ALTER TABLE v2.EVENEMENT_PROCEDURE ADD EVNPR_TYPE_DEPOSANT_GED varchar(50) NULL;
            IF OBJECT_ID(N'v2.DEPOT_PC') IS NULL CREATE TABLE v2.DEPOT_PC (DPTPC_ID_DPTPC int NOT NULL PRIMARY KEY, DPTPC_ID_EVNPR int NOT NULL);
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_NUM_DEPOT') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_NUM_DEPOT varchar(15) NULL;
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_DATE') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_DATE datetime NULL;
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_CATEGORIE_DEPOT') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_CATEGORIE_DEPOT varchar(3) NULL;
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_ETAT_INFO') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_ETAT_INFO varchar(1) NULL;
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_NUMERO_SOURCE') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_NUMERO_SOURCE varchar(36) NULL;
            IF COL_LENGTH('v2.DEPOT_PC', 'DPTPC_TOP_EXPERIAN') IS NULL ALTER TABLE v2.DEPOT_PC ADD DPTPC_TOP_EXPERIAN bit NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_AFFRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_AFFRE int NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_ID_ACTRC') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_ID_ACTRC int NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_NOM_UTILISATEUR') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_NOM_UTILISATEUR varchar(255) NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_LES_NUMEROS') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_LES_NUMEROS varchar(128) NULL;
            IF COL_LENGTH('v2.DEMANDE', 'DMND_TEXTE_PARAMETRE') IS NULL ALTER TABLE v2.DEMANDE ADD DMND_TEXTE_PARAMETRE varchar(max) NULL;
            IF OBJECT_ID(N'v2.DOSSIER_DEMANDE') IS NULL
                CREATE TABLE v2.DOSSIER_DEMANDE (DSSDM_ID_DSSDM int NOT NULL PRIMARY KEY, DSSDM_ID_DMND int NOT NULL,
                    DSSDM_CODE_SEQUENCEUR varchar(20) NOT NULL, DSSDM_VALEUR_SEQUENCEUR varchar(100) NULL);
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_ID_DMND') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_ID_DMND int NULL;
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_LIBELLE_DOCUMENT') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_LIBELLE_DOCUMENT varchar(255) NULL;
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_PHASE_TRAITEMENT') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_PHASE_TRAITEMENT varchar(10) NULL;
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_TYPE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_TYPE_GENERATION varchar(32) NULL;
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_CODE_ENTITE_SOURCE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_CODE_ENTITE_SOURCE varchar(16) NULL;
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_ENVOI_AU_COFFRE') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_ENVOI_AU_COFFRE bit NULL DEFAULT (0);
            IF COL_LENGTH('v2.DEMANDE_EDITION', 'DMNED_PARAMETRE_GENERATION') IS NULL ALTER TABLE v2.DEMANDE_EDITION ADD DMNED_PARAMETRE_GENERATION varchar(max) NULL;
            IF OBJECT_ID(N'v2.CEP_EDITION') IS NULL
                CREATE TABLE v2.CEP_EDITION (CEPED_ID_CEPED int NOT NULL PRIMARY KEY, CEPED_CODE_EVE_PRO varchar(10) NOT NULL, CEPED_TYPE_EP varchar(1) NOT NULL,
                    CEPED_CODE_EDITION varchar(32) NOT NULL, CEPED_DESTI_MODE_ENVOI varchar(max) NULL, CEPED_CONDITION varchar(250) NULL,
                    CEPED_AUTRE_PARAMETRE varchar(100) NULL);
            IF OBJECT_ID(N'v2.DESTINATAIRE_INSTITUTIONNEL') IS NULL
                CREATE TABLE v2.DESTINATAIRE_INSTITUTIONNEL (DTINS_CODE varchar(10) NOT NULL PRIMARY KEY, DTINS_LIBELLE varchar(100) NOT NULL,
                    DTINS_BLOC_ADRESSE varchar(150) NULL, DTINS_TYPE varchar(10) NOT NULL, DTINS_ADRESSE_MAIL varchar(1000) NULL,
                    DTINS_NOM_COFFRE varchar(100) NULL, DTINS_DATE_DEMARRAGE_COFFRE datetime NULL, DTINS_ACTIF bit NOT NULL);
            IF COL_LENGTH('v2.CEP_EDITION', 'CEPED_DESTI_MODE_ENVOI') IS NULL ALTER TABLE v2.CEP_EDITION ADD CEPED_DESTI_MODE_ENVOI varchar(max) NULL;
            IF COL_LENGTH('v2.CEP_EDITION', 'CEPED_CONDITION') IS NULL ALTER TABLE v2.CEP_EDITION ADD CEPED_CONDITION varchar(250) NULL;
            IF COL_LENGTH('v2.CEP_EDITION', 'CEPED_AUTRE_PARAMETRE') IS NULL ALTER TABLE v2.CEP_EDITION ADD CEPED_AUTRE_PARAMETRE varchar(100) NULL;
            IF COL_LENGTH('v2.DESTINATAIRE_INSTITUTIONNEL', 'DTINS_BLOC_ADRESSE') IS NULL ALTER TABLE v2.DESTINATAIRE_INSTITUTIONNEL ADD DTINS_BLOC_ADRESSE varchar(150) NULL;
            IF COL_LENGTH('v2.DESTINATAIRE_INSTITUTIONNEL', 'DTINS_ADRESSE_MAIL') IS NULL ALTER TABLE v2.DESTINATAIRE_INSTITUTIONNEL ADD DTINS_ADRESSE_MAIL varchar(1000) NULL;
            IF COL_LENGTH('v2.DESTINATAIRE_INSTITUTIONNEL', 'DTINS_NOM_COFFRE') IS NULL ALTER TABLE v2.DESTINATAIRE_INSTITUTIONNEL ADD DTINS_NOM_COFFRE varchar(100) NULL;
            IF COL_LENGTH('v2.DESTINATAIRE_INSTITUTIONNEL', 'DTINS_DATE_DEMARRAGE_COFFRE') IS NULL ALTER TABLE v2.DESTINATAIRE_INSTITUTIONNEL ADD DTINS_DATE_DEMARRAGE_COFFRE datetime NULL;
            IF COL_LENGTH('v2.DESTINATAIRE_INSTITUTIONNEL', 'DTINS_ACTIF') IS NULL ALTER TABLE v2.DESTINATAIRE_INSTITUTIONNEL ADD DTINS_ACTIF bit NOT NULL DEFAULT (1);
            IF OBJECT_ID(N'v2.PARAMETRE_EDITION') IS NULL
                CREATE TABLE v2.PARAMETRE_EDITION (PRMED_CODE varchar(40) NOT NULL PRIMARY KEY, PRMED_LIBELLE varchar(100) NOT NULL);
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_LISTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_LISTE varchar(4000) NOT NULL DEFAULT ('');
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_TYPE_GENERATION') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_TYPE_GENERATION varchar(15) NULL;
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_MAQUETTE') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_MAQUETTE varchar(40) NULL;
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_CODE_DOCUMENT') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_CODE_DOCUMENT varchar(20) NULL;
            IF COL_LENGTH('v2.PARAMETRE_EDITION', 'PRMED_IMPRESSION') IS NULL ALTER TABLE v2.PARAMETRE_EDITION ADD PRMED_IMPRESSION bit NOT NULL DEFAULT (0);
            IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'JD46S')
                INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('JD46S', YEAR(@aujourdhui), 'LC', 4, 'DP46', 5, 1);
            IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'DMD')
                INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('DMD', YEAR(@aujourdhui), 'LAQC', 2, 'D', 5, 1);
            EXEC sp_executesql N'
            INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE) VALUES (''OP46'', ''JROP'', ''RP'');
            INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_TYPE_EVE_PRO, R_CODEEVEPRO_UTILISABLE_SAISIE,
                    R_CODEEVEPRO_AVEC_DEPOT, R_CODEEVEPRO_CATEGORIE_DEPOT_INS, R_CODEEVEPRO_COMPATIBLE_OP)
                VALUES (''DJ46'', ''Dépôt de requête (lot 16)'', ''1'', 1, 1, ''S46'', 1);
            INSERT INTO v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR, R_CATEGORIED_LIBELLE_ECRAN, R_CATEGORIED_TYPE_NUM_DEPOT) VALUES (''S46'', ''Requêtes lot 16'', ''JD46S'');
            INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION) VALUES (4600011, @aujourdhui);
            INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_ID_AFFRE, INSTN_CODE_AFFAIRE, INSTN_TYPE_INSTANCE, INSTN_DATE_ENROLEMENT)
                VALUES (4600001, ''2026R46001'', ''DEPOT CERTIFICAT LOT 16'', 4600011, ''OP46'', ''RP'', @aujourdhui);
            ', N'@aujourdhui datetime', @aujourdhui;
            """);
        var date = Jour.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string Depots() => "SELECT COUNT(*) FROM v2.DEPOT_PC AS d JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = d.DPTPC_ID_EVNPR WHERE e.EVNPR_ID_INSTN = 4600001";
        const string Certificats = "SELECT COUNT(*) FROM v2.DEMANDE_EDITION WHERE DMNED_LIBELLE_DOCUMENT LIKE N'Certificat de dépôt - DP46%'";

        // REFUS : la case cochee, sans le parametre DEPOT_INS.
        await Sql("DELETE FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = 'DEPOT_INS'");
        var journal = await Valeur("SELECT COUNT(*) FROM soc.Journal");
        var refus = await EcranDepotsJud.EnregistrerAsync(Client, Greffe, Role, CodeEvenement, date, certificat: true);
        Verifier(!refus.Fait && refus.Message.StartsWith("Le paramètre d'édition « DEPOT_INS » n'existe pas dans la base du greffe", StringComparison.Ordinal)
                 && await Valeur(Depots()) == "0" && await Valeur("SELECT COUNT(*) FROM soc.Journal") == journal,
            $"depots judiciaires (lot 16) : certificat demande sans le parametre DEPOT_INS -- refuse, rien d'ecrit (« {refus.Message} »)");

        // JUMEAU ACCEPTE : la case decochee (son defaut dans RIG v2) -- le depot, aucun certificat.
        var sans = await EcranDepotsJud.EnregistrerAsync(Client, Greffe, Role, CodeEvenement, date);
        Verifier(sans.Fait && await Valeur(Depots()) == "1" && await Valeur(Certificats) == "0" && !sans.Message.Contains("Certificat", StringComparison.Ordinal),
            $"depots judiciaires (lot 16) : case decochee -- le depot s'ecrit, aucun certificat (« {sans.Message} »)");

        // FAIT : le parametre de RIG_DEV (sa liste), la case cochee -- le certificat dans la file des editions, relu.
        await Sql("""
            EXEC sp_executesql N'
            INSERT INTO v2.PARAMETRE_EDITION (PRMED_CODE, PRMED_LIBELLE, PRMED_LISTE, PRMED_TYPE_GENERATION, PRMED_CODE_MAQUETTE)
            VALUES (''DEPOT_INS'', ''Certificat de dépot INS'', ''<PARAMETRES CODE_ENTITE_SOURCE="DEPOT_PC" CODE_MAQUETTE="3801_DEPOT_INS" CODE_PROCESSUS="EDIT_DEPOT" IMPRESSION="1" LIBELLE_DOCUMENT="Certificat de dépot INS" TYPE_GENERATION="FUSION_RIG" />'',
                    ''FUSION_RIG'', ''3801_DEPOT_INS'');';
            """);
        var avec = await EcranDepotsJud.EnregistrerAsync(Client, Greffe, Role, CodeEvenement, date, certificat: true);
        var numero = await Valeur("""
            SELECT TOP (1) d.DPTPC_NUM_DEPOT FROM v2.DEPOT_PC AS d JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = d.DPTPC_ID_EVNPR
            WHERE e.EVNPR_ID_INSTN = 4600001 ORDER BY d.DPTPC_ID_DPTPC DESC
            """);
        var relu = await Valeur($"""
            SELECT CONCAT(de.DMNED_CODE_ENTITE_SOURCE, '|', CAST(de.DMNED_ENVOI_AU_COFFRE AS int), '|', dm.DMND_CODE_PROSS, '|', de.DMNED_PHASE_TRAITEMENT, '|',
                          CAST(de.DMNED_PARAMETRE_GENERATION AS xml).value('(/PARAMETRES/@CODE_MAQUETTE)[1]', 'varchar(40)'), '|',
                          CAST(de.DMNED_PARAMETRE_GENERATION AS xml).exist('/PARAMETRES/@LISTE_ENTITE_DESTINATION'))
            FROM v2.DEMANDE_EDITION AS de JOIN v2.DEMANDE AS dm ON dm.DMND_ID_DMND = de.DMNED_ID_DMND
            JOIN v2.DEPOT_PC AS d ON d.DPTPC_NUMERO_SOURCE = de.DMNED_VALEUR_ID_SOURCE
            WHERE de.DMNED_LIBELLE_DOCUMENT = N'Certificat de dépôt - {numero}' AND d.DPTPC_NUM_DEPOT = N'{numero}'
            """);
        Verifier(avec.Fait && numero.StartsWith("DP46", StringComparison.Ordinal) && await Valeur(Certificats) == "1"
                 && avec.Message.Contains($"« Certificat de dépôt - {numero} » placé dans la file des éditions", StringComparison.Ordinal)
                 && relu == "DEPOT_PC|1|DEPOTJUD|C|3801_DEPOT_INS|1",
            $"depots judiciaires (lot 16) : case cochee -- « Certificat de dépôt - {numero} » relu dans la file (source, coffre, demande, phase, maquette, "
            + $"destinataires : {relu}) (« {avec.Message} »)");
    }
}
