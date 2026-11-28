using RigV3Desktop.Pont;

namespace ParcoursCompletCheck;

/// <summary>
/// « Dépôts judiciaires » (PROC_DEPOTJUD, Pont/EcranDepotsJud*.cs) : « Nouvel événement survenu » sur une instance JROP et son dépôt
/// numéroté (RIG v2 : ETP_EVENEMENT_SURVENU et OPE_DEPOT ; écriture : Serveur/Socle/EcransMain/EcritureDepotsJud.cs), joué par le pont
/// puis relu par le pont : la ligne de l'instance montre l'événement déposé, le dépôt porte le numéro de la série. Lot 8 du plan
/// « RIG v3 complet, mesuré par le banc du parcours complet » (28/11/2026).
///
/// LE JEU (identifiants 38005xx) : une instance JROP (code affaire OP38, type RP = « OP » pour la compatibilité du code) dans son
/// affaire, sans client : le dépôt s'écrit facturation décochée, comme RIG v2 le valide, et le message le dit ; le code DJ38 (avec
/// dépôt d'instance, compatible OP, catégorie S38, série JD38S « LC »). Tables et colonnes créées si absentes, aux types de RIG_DEV
/// (RigDatabase\dbo\Tables\CODE_EVE_PRO.sql, EVENEMENT_PROCEDURE.sql, DEPOT_PC.sql).
/// fix-ok: mesure le 28/11/2026 (capture lot8, donnees de dev) -- les 200 instances JROP sont « à facturer » (aucun client) ; le refus
/// « sans client » etait plus strict que RIG v2, qui valide facturation decochee (OPE_FACTU_GESTCOM.cs:947-956) : client retire du jeu.
/// </summary>
public sealed class SondeDepotsJud : ISondeEcran
{
    private const string Role = "2026R38005", CodeEvenement = "DJ38";

    public string Code => "PROC_DEPOTJUD";

    public string? Jeu => """
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_ID_AFFRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_ID_AFFRE int NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_CODE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_CODE_AFFAIRE varchar(10) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_TYPE_INSTANCE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_TYPE_INSTANCE varchar(2) NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_DATE_ENROLEMENT') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_DATE_ENROLEMENT datetime NULL;
        IF COL_LENGTH('v2.INSTANCE', 'INSTN_NATURE_AFFAIRE') IS NULL ALTER TABLE v2.INSTANCE ADD INSTN_NATURE_AFFAIRE varchar(10) NULL;
        IF OBJECT_ID(N'v2.CODE_AFFAIRE') IS NULL CREATE TABLE v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR varchar(10) NOT NULL PRIMARY KEY);
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_LIBELLE_ECRAN') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_LIBELLE_ECRAN varchar(100) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_TYPE_NUM_ROLE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_TYPE_NUM_ROLE varchar(5) NULL;
        IF COL_LENGTH('v2.CODE_AFFAIRE', 'R_CODEAFFAIR_TYPE_INSTANCE') IS NULL ALTER TABLE v2.CODE_AFFAIRE ADD R_CODEAFFAIR_TYPE_INSTANCE varchar(2) NULL;
        IF OBJECT_ID(N'v2.NATURE_AFFAIRE') IS NULL
            CREATE TABLE v2.NATURE_AFFAIRE (R_NATUREAFFA_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_NATUREAFFA_LIBELLE_ECRAN varchar(100) NULL);
        IF OBJECT_ID(N'v2.AFFAIRE') IS NULL CREATE TABLE v2.AFFAIRE (AFFRE_ID_AFFRE int NOT NULL PRIMARY KEY, AFFRE_DATE_CREATION datetime NULL);
        IF OBJECT_ID(N'v2.CODE_EVE_PRO') IS NULL
            CREATE TABLE v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR varchar(10) NOT NULL PRIMARY KEY, R_CODEEVEPRO_LIBELLE_ECRAN varchar(35) NOT NULL);
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
        IF OBJECT_ID(N'v2.CATEGORIE_DEPOT') IS NULL
            CREATE TABLE v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR varchar(3) NOT NULL PRIMARY KEY, R_CATEGORIED_TYPE_NUM_DEPOT varchar(5) NULL);
        IF OBJECT_ID(N'v2.EVENEMENT_PROCEDURE') IS NULL
            CREATE TABLE v2.EVENEMENT_PROCEDURE (EVNPR_ID_EVNPR int NOT NULL PRIMARY KEY, EVNPR_ID_INSTN int NULL, EVNPR_NUM_ORDRE smallint NULL,
                EVNPR_CODE_EVE_PRO varchar(10) NOT NULL, EVNPR_DATE datetime NULL, EVNPR_SURVENU bit NULL, EVNPR_DELAI_EXPIRE bit NULL);
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
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'JD38S')
            INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('JD38S', YEAR(@aujourdhui), 'LC', 4, 'DP38', 5, 1);
        IF NOT EXISTS (SELECT 1 FROM num.Compteur WHERE Type = 'DMD')
            INSERT INTO num.Compteur (Type, Annee, Structure, CodageAnnee, Lettre, TailleChrono, Debut) VALUES ('DMD', YEAR(@aujourdhui), 'LAQC', 2, 'D', 5, 1);
        EXEC sp_executesql N'
        INSERT INTO v2.CODE_AFFAIRE (R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_LIBELLE_ECRAN, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE)
            VALUES (''OP38'', ''Requête de la sonde lot 8'', ''JROP'', ''RP'');
        INSERT INTO v2.CODE_EVE_PRO (R_CODEEVEPRO_VALEUR, R_CODEEVEPRO_LIBELLE_ECRAN, R_CODEEVEPRO_TYPE_EVE_PRO, R_CODEEVEPRO_UTILISABLE_SAISIE,
                R_CODEEVEPRO_AVEC_DEPOT, R_CODEEVEPRO_CATEGORIE_DEPOT_INS, R_CODEEVEPRO_COMPATIBLE_OP)
            VALUES (''DJ38'', ''Dépôt de requête (sonde)'', ''1'', 1, 1, ''S38'', 1);
        INSERT INTO v2.CATEGORIE_DEPOT (R_CATEGORIED_VALEUR, R_CATEGORIED_TYPE_NUM_DEPOT) VALUES (''S38'', ''JD38S'');
        INSERT INTO v2.AFFAIRE (AFFRE_ID_AFFRE, AFFRE_DATE_CREATION) VALUES (3800511, @aujourdhui);
        INSERT INTO v2.INSTANCE (INSTN_ID_INSTN, INSTN_NUM_ROLE, INSTN_NOM_COURANT, INSTN_ID_AFFRE, INSTN_CODE_AFFAIRE, INSTN_TYPE_INSTANCE, INSTN_DATE_ENROLEMENT)
            VALUES (3800501, ''2026R38005'', ''SONDE DEPOT JUDICIAIRE'', 3800511, ''OP38'', ''RP'', @aujourdhui);
        ', N'@aujourdhui datetime', @aujourdhui;
        """;

    public async Task<PreuveSonde> GesteAsync(ContexteSonde c)
    {
        var date = c.Jour.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var codes = await EcranDepotsJud.CodesAsync(c.Client, c.Greffe, Role);
        if (codes.Mode != "serveur" || codes.Codes.All(x => x.Code != CodeEvenement))
            return new(false, false, $"Les codes de l'instance {Role} : {codes.Mode} « {codes.Message} », {codes.Codes.Length} code(s), {CodeEvenement} absent");
        var r = await EcranDepotsJud.EnregistrerAsync(c.Client, c.Greffe, Role, CodeEvenement, date);
        if (!r.Fait) return new(false, false, $"Déposer sur {Role} : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var vue = await EcranDepotsJud.VueAsync(c.Client, c.Greffe);
        var ligne = vue.Lignes.FirstOrDefault(l => l.Numero == Role);
        var attendu = $"Dépôt de requête (sonde) ({c.Jour:dd/MM/yyyy})";
        var depot = await c.Valeur("""
            SELECT TOP (1) d.DPTPC_NUM_DEPOT FROM v2.DEPOT_PC AS d JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_EVNPR = d.DPTPC_ID_EVNPR
            WHERE e.EVNPR_ID_INSTN = 3800501 AND e.EVNPR_CODE_EVE_PRO = 'DJ38' AND d.DPTPC_CATEGORIE_DEPOT = 'S38'
            """);
        return vue.Mode == "serveur" && ligne?.DernierEvenement == attendu && depot.StartsWith("DP38", StringComparison.Ordinal) && r.Message.Contains(depot, StringComparison.Ordinal)
               && r.Message.Contains("sans facture", StringComparison.Ordinal)
            ? new(true, true, $"Déposer fait (« {r.Message} »), relu par le pont : dernier événement « {ligne.DernierEvenement} », dépôt {depot}")
            : new(true, false, $"Déposer rendu fait, mais relu {vue.Mode} « {ligne?.DernierEvenement ?? "instance absente"} » (attendu « {attendu} »), dépôt « {depot} »");
    }
}
