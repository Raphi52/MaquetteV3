using System.Data;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NumerotationV2Check;
using RigV3.Base;
using RigV3.Contrats.Numerotation;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Greffes;

// Banc du COTE V2 DE LA NUMEROTATION (lot 7 du plan de reconstruction, 2026-11-21).
// Stories NUM-002 (la V3 attribue les numeros que demande la V2), NUM-003 (le second programme aussi),
// NUM-006 (la V2 lit ses compteurs comme avant), NUM-007 (arret propre quand le compteur ne repond pas),
// NUM-022 (retour a la V2 pendant la beta) : un controle par critere, qui cite son id.
//
// Cinq bases jetables sur le SQL Server du poste, jamais sur une vraie base :
//  - « V3 »     : toutes les migrations du depot (le compteur du lot 5) ;
//  - « commune » : la base commune du serveur, ou son catalogue (soc.Greffe) mene le greffe 8002 a la
//                 base « V3 » : les routes du compteur portent le greffe et jugent l'agent dans SA base (lot 8) ;
//  - « V2 »     : la base V2 du greffe, reduite aux objets utiles (v2\), que Base\v2\bascule.sql bascule ;
//  - « temoin » : la meme base V2, jamais basculee : ses programmes d'origine donnent ce que la V2 aurait
//                 donne. Les memes gestes sont faits des deux cotes et compares (numeros, lectures) ;
//  - « D8 »     : une base V2 d'origine pour composer les 142 types de compteurs-d8.json (NUM-002 CA03).
// Les scripts de Base\v2 sont joues tels que les lancera l'exploitation (Sqlcmd.cs) ; les appels de la V2
// sont rejoues tels qu'ils sont ecrits (types, tailles, sens des parametres), sous un compte SQL d'agent
// qui n'a que les droits documentes (lecture-ecriture dans la V2, num_demandeur dans la V3).
//
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la racine du depot.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var dossierScripts = Path.Combine(racine, "Base", "v2");
var dossierBanc = Path.Combine(racine, "tests", "NumerotationV2Check");
const string CodeGreffe = "8002";
const string MessageAttribue = "Numérotation indisponible : la base de la V3 du greffe 8002 ne répond pas. Aucun numéro n'a été attribué. Réessayez ou appelez l'assistance d'Amitel.";
const string MessageRendu = "Numérotation indisponible : la base de la V3 du greffe 8002 ne répond pas. Aucun numéro n'a été rendu. Réessayez ou appelez l'assistance d'Amitel.";
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
// Ce qu'aucun banc de ce poste ne peut faire : dit, pas compte vert (docs/reconstruction/numerotation-v2.md).
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);

await using var v3 = await BaseJetable.CreerAsync("NumerotationV2CheckV3");
await using var v2 = await BaseJetable.CreerAsync("NumerotationV2CheckV2");
await using var temoin = await BaseJetable.CreerAsync("NumerotationV2CheckTemoin");
await using var d8 = await BaseJetable.CreerAsync("NumerotationV2CheckD8");
await using var commune = await BaseJetable.CreerAsync("NumerotationV2CheckCommune");

// Sans pool : une base mise hors ligne ou restauree ne doit pas laisser de connexion morte en reserve.
string SansPool(string c, string poste = "POSTE-BANC")
    => new SqlConnectionStringBuilder(c) { Pooling = false, WorkstationID = poste, ApplicationName = "NumerotationV2Check" }.ConnectionString;
var cxV3 = SansPool(v3.Connexion);
var cxV2 = SansPool(v2.Connexion);
var cxT = SansPool(temoin.Connexion);
var cxD8 = SansPool(d8.Connexion);
var cxMaster = SansPool(new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()) { InitialCatalog = "master" }.ConnectionString);

var agent = "rigv3_banc_agent_" + Guid.NewGuid().ToString("N")[..10];
var motDePasse = "Banc-" + Guid.NewGuid().ToString("N")[..16] + "!aA1";
string CommeAgent(string c) => new SqlConnectionStringBuilder(c)
{
    UserID = agent, Password = motDePasse, Pooling = false, WorkstationID = "POSTE-V2-AGENT", ApplicationName = "RIG V2"
}.ConnectionString;
var sessionEchecs = "rigv3_banc_echecs_" + Guid.NewGuid().ToString("N")[..8];
var tache = "rigv3_banc_releve_" + Guid.NewGuid().ToString("N")[..8];
string? sauvegarde = null;

async Task Executer(string cx, string sql)
{
    await using var cn = new SqlConnection(cx);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
    await cmd.ExecuteNonQueryAsync();
}
async Task<T> Valeur<T>(string cx, string sql)
{
    await using var cn = new SqlConnection(cx);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
    var v = await cmd.ExecuteScalarAsync();
    return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
}
// Les lignes d'une requete, une chaine par ligne (colonnes exclues retirees), triees : deux bases se comparent.
async Task<List<string>> Lignes(string cx, string sql, params string[] exclues)
{
    var lignes = new List<string>();
    await using var cn = new SqlConnection(cx);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
    await using var l = await cmd.ExecuteReaderAsync();
    while (await l.ReadAsync())
        lignes.Add(string.Join("|", Enumerable.Range(0, l.FieldCount)
            .Where(i => !exclues.Contains(l.GetName(i), StringComparer.OrdinalIgnoreCase))
            .Select(i => l.IsDBNull(i) ? "NULL" : Convert.ToString(l.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
    lignes.Sort(StringComparer.Ordinal);
    return lignes;
}
Dictionary<string, string> Variables(string? finBeta = null, string reprise = "faite") => new()
{
    ["BaseV2"] = v2.Nom, ["BaseV3"] = v3.Nom, ["CodeGreffe"] = CodeGreffe,
    ["FinBeta"] = finBeta ?? "non", ["SessionEchecs"] = sessionEchecs, ["Reprise"] = reprise,
};
async Task<Sqlcmd.Sortie> Script(string fichier, Dictionary<string, string> variables)
    => await Sqlcmd.JouerAsync(cxMaster, Path.Combine(dossierScripts, fichier), variables);
async Task<SqlException?> ScriptRefuse(string fichier, Dictionary<string, string> variables, System.Text.Encoding? encodage = null)
{
    try { await Sqlcmd.JouerAsync(cxMaster, Path.Combine(dossierScripts, fichier), variables, encodage); return null; }
    catch (SqlException e) { return e; }
}

// --- Les appels d'un programme V2, en recueillant tout ce que voit l'appelant -------------------------------
// FireInfoMessageEventOnUserErrors : une erreur levee arrive en message, le code rendu reste lisible.
async Task<Appel> Programme(string cx, string programme, Action<SqlParameterCollection> parametres,
                            SqlConnection? cnDonnee = null, SqlTransaction? tx = null)
{
    var cn = cnDonnee ?? new SqlConnection(cx);
    var erreurs = new List<Erreur>();
    void Recueillir(object _, SqlInfoMessageEventArgs e)
    {
        foreach (SqlError x in e.Errors) erreurs.Add(new Erreur(x.Number, x.State, x.Class, x.Message));
    }
    cn.InfoMessage += Recueillir;
    try
    {
        cn.FireInfoMessageEventOnUserErrors = true;
        if (cnDonnee is null) await cn.OpenAsync();
        var lignes = 0;
        await using var cmd = new SqlCommand(programme, cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
        cmd.StatementCompleted += (_, _) => lignes++;
        parametres(cmd.Parameters);
        var retour = cmd.Parameters.Add("@RETURN_VALUE", SqlDbType.Int);
        retour.Direction = ParameterDirection.ReturnValue;
        await cmd.ExecuteNonQueryAsync();
        var sorties = cmd.Parameters.Cast<SqlParameter>()
            .Where(p => p.Direction is ParameterDirection.Output or ParameterDirection.InputOutput)
            .ToDictionary(p => p.ParameterName, p => p.Value is DBNull ? null : p.Value);
        return new Appel(retour.Value is int c ? c : int.MinValue, sorties, erreurs, lignes);
    }
    finally
    {
        cn.InfoMessage -= Recueillir;
        if (cnDonnee is null) await cn.DisposeAsync();
    }
}
Task<Appel> Generer(string cx, string type, int annee = 0, int jour = 0, string? greffe = "9999",
                    SqlConnection? cn = null, SqlTransaction? tx = null)
    => Programme(cx, "GENERER_NUMERO", p =>
    {
        p.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = type;
        p.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40).Direction = ParameterDirection.Output;
        p.Add("@ANNEE", SqlDbType.Int).Value = annee;
        p.Add("@QUANTIEME", SqlDbType.Int).Value = jour;
        if (greffe is not null) p.Add("@CODE_GREFFE", SqlDbType.VarChar, 5).Value = greffe;
    }, cn, tx);
Task<Appel> Chercher(string cx, string type, int annee = 0, int jour = 0)
    => Programme(cx, "CHERCHER_NUMERO", p =>
    {
        p.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = type;
        p.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 20).Direction = ParameterDirection.Output;
        p.Add("@ANNEE", SqlDbType.Int).Value = annee;
        p.Add("@QUANTIEME", SqlDbType.Int).Value = jour;
    });
// Comme l'appel .NET central de la restitution (Numerotation.cs:191-205).
Task<Appel> Restituer(string cx, string type, string numero, int annee, string? raison)
    => Programme(cx, "RESTITUER_NUMERO", p =>
    {
        p.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = type;
        p.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 20).Value = numero;
        p.Add("@ANNEE", SqlDbType.Int).Value = annee;
        p.Add("@RAISON", SqlDbType.VarChar, 50).Value = (object?)raison ?? DBNull.Value;
        p.Add("@MonErreur", SqlDbType.Int).Direction = ParameterDirection.Output;
    });

// Ce que tiennent les deux bases du greffe : une restitution, une attribution ratee ne doivent rien y changer.
async Task<string> Etat() => await Valeur<string>(cxV3,
    $"SELECT CONCAT((SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM [{v2.Nom}].dbo.NUMERO_METIER), '/', " +
    $"(SELECT COUNT(*) FROM [{v2.Nom}].dbo.NUMERO_METIER), '/', (SELECT CHECKSUM_AGG(CHECKSUM(Type, Annee, Jour, Chrono)) FROM num.Compteur), " +
    "'/', (SELECT COUNT(*) FROM num.NumeroRendu), '/', (SELECT COUNT_BIG(*) FROM num.Journal));");
async Task<int?> ChronoV3(string type, int annee, int jour = 0) => await Valeur<int?>(cxV3,
    $"SELECT Chrono FROM num.Compteur WHERE Type = '{type}' AND Annee = {annee} AND Jour = {jour};");
async Task<int?> ChronoV2(string cx, string type, int annee) => await Valeur<int?>(cx,
    $"SELECT MAX(NMMTR_CHRONO) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = '{type}' AND NMMTR_ANNEE = {annee} AND NMMTR_ATTRIBUTION_ERRONEE = 0;");

// Le registre de la serie RGB 2026 cote greffe : chaque numero donne (V2, V3, V2 revenue), chaque numero rendu.
var enUsage = new HashSet<int>();
var rendus = new HashSet<int>();
var doublons = new List<int>();
void Donne(string? numero)
{
    if (numero is null || !numero.StartsWith("2026B")) return;
    var c = int.Parse(numero[5..]);
    if (!enUsage.Add(c)) doublons.Add(c);
    rendus.Remove(c);
}
void Rendu(string numero) { var c = int.Parse(numero[5..]); enUsage.Remove(c); rendus.Add(c); }

WebApplication? app = null;
try
{
    // =================================================================================================
    // Mise en place : la V3 (migrations), trois bases V2 d'origine, un agent SQL aux droits documentes
    // =================================================================================================
    var migrations = await Migrateur.AppliquerAsync(v3.Connexion, Path.Combine(racine, "Base", "migrations"));
    Exiger(migrations.Jouees.Contains("NUM-001_compteurs"),
        $"base V3 jetable : migrations du depot jouees, dont NUM-001_compteurs ({string.Join(", ", migrations.Jouees)})");

    // L'heure du SERVEUR fait foi (num.Attribuer et les programmes V2 prennent la sienne) : le conteneur
    // tourne en temps universel, le poste non -- autour de minuit, le jour de l'annee differe.
    var anneeCourante = await Valeur<int>(cxMaster, "SELECT YEAR(SYSDATETIME());");
    var jourCourant = await Valeur<int>(cxMaster, "SELECT DATEPART(dayofyear, SYSDATETIME());");
    var finBetaFuture = await Valeur<string>(cxMaster, "SELECT CONVERT(char(10), DATEADD(day, 30, SYSDATETIME()), 23);");
    var finBetaPassee = await Valeur<string>(cxMaster, "SELECT CONVERT(char(10), DATEADD(day, -1, SYSDATETIME()), 23);");

    async Task MonterV2(string cx)
    {
        await using var cn = new SqlConnection(cx);
        await cn.OpenAsync();
        async Task Jouer(string fichier, bool guillemetsOff = false, bool ansiNullsOff = false)
        {
            await using (var reglages = new SqlCommand(
                $"SET QUOTED_IDENTIFIER {(guillemetsOff ? "OFF" : "ON")}; SET ANSI_NULLS {(ansiNullsOff ? "OFF" : "ON")};", cn))
                await reglages.ExecuteNonQueryAsync();
            foreach (var lot in Sqlcmd.Lots(Path.Combine(dossierBanc, "v2", fichier), new Dictionary<string, string>()))
            {
                await using var cmd = new SqlCommand(lot, cn);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await Jouer("NUMERO_METIER.sql");
        await Jouer("reprise.sql");                                                     // ce que lit num.Reprendre (lot 6)
        await Jouer("nf525.sql");
        await Jouer("fn_CertifNF525Active.rigdev.sql");
        await Jouer("TR_NUMERO_METIER_JET.rigdev.sql");
        await Jouer("VerifTypeMetier.sql", guillemetsOff: true, ansiNullsOff: true);   // comme sur RIG_DEV
        await Jouer("GENERER_NUMERO.sql", guillemetsOff: true);                         // « "9999" »
        await Jouer("CHERCHER_NUMERO.sql", guillemetsOff: true);
        await Jouer("RESTITUER_NUMERO.sql", guillemetsOff: true);
        await using var fin = new SqlCommand(
            "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; " +
            "GRANT EXECUTE ON dbo.GENERER_NUMERO TO public; " +                         // comme sur RIG_DEV (lu le 2026-11-21)
            "INSERT dbo.PARAMETRAGE (PARAM_CODE, PARAM_VALEUR) VALUES ('ACTIVATION_CERTIF_NF525', '1'); " +
            "INSERT dbo.UTILISATEUR (UTLST_LOGIN) VALUES ('agent.dupont');", cn);
        await fin.ExecuteNonQueryAsync();
    }
    await MonterV2(cxV2);
    await MonterV2(cxT);
    await MonterV2(cxD8);

    // Les compteurs de depart, les memes dans la V2 et le temoin : reglages de RIG_DEV (lot 5).
    var depart = new List<Ligne>
    {
        new("RGB", 2026, 1, "ALC", 4, "B", null, 5, 1, 1234, true, Libelle: "RCS Numéro de gestion du dossier RCS, catégorie B"),
        new("RC1", 2026, 1, "LASC", 2, "F", "/", 6, 1, 9935, true),
        new("RC5", 2026, 1, "LASC", 2, "F", "/", 6, 1, 3, true, Greffe: CodeGreffe),
        new("JPC", 2026, 1, "ALC", 4, "RJ", null, 4, 1, 242, true),
        new("JGDJ", 2026, 58, "AQC", 2, null, null, 5, 1, 1, true, Greffe: "3801"),
        new("ARRD", 2026, 300, "LAQC", 2, "A", "", 5, 1, 115, true, Greffe: "9999"),
        new("ARRC", 2001, 1, "LAQC", 2, "C", null, 4, 1, 0, true, Redir: "ARRD"),
        new("DMD", 2026, 300, "LAQC", 2, "D", "", 5, 1, 122, false, Greffe: "9999"),
        new("FACTU", 2026, 1, "A-C", 2, "", "", 6, 1, 22768, false),
        new("LIAI", 2026, 1, "LGACK", 1, "G", null, 5, 1, 1782, false, Greffe: "3802"),
        new("JDSAN", 2026, 1, "LASC", 4, "SANC", "/", 5, 1, 12, true),
        new("PLEIN", 2026, 1, "ALC", 4, "P", null, 2, 1, 99, false),
    };
    foreach (var cx in new[] { cxV2, cxT })
        foreach (var f in depart)
            await Executer(cx,
                "INSERT dbo.NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_LIBELLE, NMMTR_STRUCTURE_NUMERO, NMMTR_CODE_GREFFE, NMMTR_ANNEE, " +
                "NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_TAILLE_CHRONO, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_DATE_ATTRIBUTION, " +
                "NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR, NMMTR_ATTRIBUTION_ERRONEE, NMMTR_FILE_REDIRECTION, NMMTR_SEPARATEUR) VALUES (" +
                $"'{f.Type}', {Sql(f.Libelle)}, '{f.Structure}', {Sql(f.Greffe)}, {f.Annee}, {f.Codage}, {Sql(f.Lettre)}, {f.Taille}, " +
                $"{f.Debut}, {f.Chrono}, '2026-11-01', {f.Quantieme?.ToString() ?? "NULL"}, {(f.Gestion ? 1 : 0)}, 0, {Sql(f.Redir)}, {Sql(f.Sep)});");
    // Ce que la reprise (num.Reprendre, num.ReleverHeritage) lit en plus de NUMERO_METIER, un cas de chaque :
    // ordre des mentions et des etablissements NQ (NUM-021 CA01), avis BDF de l'annee (CA04), code de liaison
    // (NUM-019 T03), numero manquant (NUM-013 CA02), sauvegarde d'un ancien transfert (NUM-005 CA06), role en
    // double (NUM-009 CA03), numero de gestion sans annee (NUM-012 CA02), facture hors format (NUM-020 CA04).
    foreach (var cx in new[] { cxV2, cxT })
        await Executer(cx,
            "INSERT dbo.DOSSIER_RCS (DSSRC_NUM_GESTION) VALUES ('2026B01234'), ('AP0042'); " +
            "INSERT dbo.MENTION_OBSERVATION (MNTOB_ID_DSRCS, MNTOB_NUM_ORDRE) VALUES (1, 1), (1, 2), (1, 3), (2, 1); " +
            "INSERT dbo.ETABLISSEMENT (ETBLS_ID_DSRCS, ETBLS_NUM_ORDRE, ETBLS_TYPE_ADRESSE, ETBLS_SUPPRIME) " +
            "VALUES (1, 2, 'NQ', 0), (1, 3, 'NQ', 0), (1, 9, 'NQ', 1), (2, 5, 'SI', 0); " +
            $"INSERT dbo.EDI_BDF (EDIBDF_NUM_GESTION) VALUES ('{anneeCourante}F00031'), ('{anneeCourante}F00032'), ('{anneeCourante - 1}F00090'); " +
            "INSERT dbo.GREFFE_NUMERO_LIAISON (GRNLN_CODE) VALUES ('G3802'); " +
            "INSERT dbo.NUMERO_CHRONO_MANQUANT (NMCHM_NUMERO_CHRONO, NMCHM_TYPE, NMCHM_ANNEE, NMCHM_DOMAINE, NMCHM_TYPE_METIER) " +
            "VALUES ('2026B01200', 'RGB', 2026, 'RCS', 'RGB'); " +
            "INSERT dbo.NUMERO_METIER_SAVE_CDU (NMMTR_TYPE_METIER, NMMTR_STRUCTURE_NUMERO, NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, " +
            "NMMTR_TAILLE_CHRONO, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_GESTION_NUM_ERR, NMMTR_ATTRIBUTION_ERRONEE) " +
            "VALUES ('RGB', 'ALC', 2019, 4, 'B', 5, 1, 999, 1, 0); " +
            "INSERT dbo.INSTANCE (INSTN_NUM_ROLE) VALUES ('2026IP00001'), ('2004IP00330'), ('2004IP00330'); " +
            "INSERT dbo.FACTURE (FACTU_NUMERO_FACTURE) VALUES ('26-022768'), ('ASTRA-AF1');");

    // L'agent : ce que la V2 a dans sa base (lecture, ecriture, execution), et num_demandeur dans la V3.
    await Executer(cxMaster, $"CREATE LOGIN [{agent}] WITH PASSWORD = N'{motDePasse}', CHECK_POLICY = OFF;");
    foreach (var cx in new[] { cxV2, cxT, cxD8 })
        await Executer(cx, $"CREATE USER [{agent}] FOR LOGIN [{agent}]; ALTER ROLE db_datareader ADD MEMBER [{agent}]; " +
                           $"ALTER ROLE db_datawriter ADD MEMBER [{agent}]; GRANT EXECUTE ON SCHEMA::dbo TO [{agent}];");
    await Executer(cxV3, $"CREATE USER [{agent}] FOR LOGIN [{agent}]; ALTER ROLE num_demandeur ADD MEMBER [{agent}];");
    var agentV2 = CommeAgent(cxV2);
    var agentT = CommeAgent(cxT);

    // =================================================================================================
    // La bascule : refusee tant que la V3 n'a pas les compteurs de la V2, puis faite
    // =================================================================================================
    var procsAvant = await Lignes(cxV2, "SELECT o.name, m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
                                        "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    // Lue dans la page de code du poste (lancement sans -f 65001) : refusee avant tout, les messages seraient abimes.
    var refusEncodage = await ScriptRefuse("bascule.sql", Variables(finBetaFuture), System.Text.Encoding.Latin1);
    Exiger(refusEncodage?.Number == 51020 && refusEncodage.Message.Contains("UTF-8")
           && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.schemas WHERE name = 'rigv3';") == 0,
        "bascule.sql lue dans une autre page de code que UTF-8 -> refusee (51020), rien n'est change");
    var refusVide = await ScriptRefuse("bascule.sql", Variables(finBetaFuture));
    var procsApresRefus = await Lignes(cxV2, "SELECT o.name, m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
                                             "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    Exiger(refusVide?.Number == 51020 && refusVide.Message.Contains("NUM-005") && procsAvant.SequenceEqual(procsApresRefus)
           && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.schemas WHERE name = 'rigv3';") == 0,
        $"bascule.sql refusee (51020) tant que la reprise NUM-005 n'a pas mis les compteurs dans la V3, et rien n'est change " +
        $"(« {refusVide?.Message} »)");

    // fix-ok: le banc jouait Reprise=faite apres une copie maison des compteurs ; le lot 6 est reuni (main 8251460),
    // la vraie reprise est jouee ici (controle final du 2026-11-21) et a fait apparaitre FACV3/BDFNI dans la V2.
    // NUM-002 T03 : la reprise des compteurs (num.Reprendre, NUM-005, lot 6) jouee DANS la transaction de la
    // bascule (Reprise=oui). Une bascule qui echoue au dernier programme annule donc aussi la reprise : la V3
    // reste sans compteur, la V2 garde ses programmes et sa table. L'echec est provoque par un declencheur de
    // base de la V2 qui refuse de remplacer RESTITUER_NUMERO, le dernier geste de la transaction.
    var tableAvantReprise = await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;");
    await Executer(cxV2, "CREATE TRIGGER banc_refus_restituer ON DATABASE FOR CREATE_PROCEDURE, ALTER_PROCEDURE AS " +
                         "IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]', 'sysname') = 'RESTITUER_NUMERO' " +
                         "THROW 50001, 'banc : RESTITUER_NUMERO ne se remplace pas', 1;");
    var refusFinDeBascule = await ScriptRefuse("bascule.sql", Variables(finBetaFuture, reprise: "oui"));
    await Executer(cxV2, "DROP TRIGGER banc_refus_restituer ON DATABASE;");
    var procsApresEchec = await Lignes(cxV2, "SELECT o.name, m.definition FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
                                             "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    var v3ApresEchec = await Valeur<string>(cxV3, "SELECT CONCAT((SELECT COUNT(*) FROM num.Compteur), '/', (SELECT COUNT(*) FROM num.OrdreFiche), " +
                                                  "'/', (SELECT COUNT(*) FROM num.Reprise));");
    Exiger(refusFinDeBascule?.Message.Contains("RESTITUER_NUMERO ne se remplace pas") == true && v3ApresEchec == "0/0/0"
           && procsAvant.SequenceEqual(procsApresEchec) && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.schemas WHERE name = 'rigv3';") == 0
           && (await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;")).SequenceEqual(tableAvantReprise),
        $"NUM-002 T03 : bascule.sql Reprise=oui qui echoue au dernier programme (« {refusFinDeBascule?.Message} ») annule aussi la reprise " +
        $"jouee dans sa transaction : V3 sans compteur ni reprise (compteurs/ordres/reprises : {v3ApresEchec}), V2 intacte (programmes, table, pas de pont)");

    // Les lectures de la table des compteurs par la V2 (NUM-006 CA02), avec les valeurs que la V2 y met.
    var lectures = new (string Source, string Sql, string[] Exclues)[]
    {
        ("VerifTypeMetier.sql:17-25", "SELECT dbo.VerifTypeMetier('RGB', 2026, '2026B01235', '8002'), dbo.VerifTypeMetier('JPC', 2026, '2026RJ0243', '8002'), " +
                                      "dbo.VerifTypeMetier('RC1', 2026, 'F26/009936', '8002'), dbo.VerifTypeMetier('RGB', 2027, '2027B00001', '8002');", []),
        ("FonctionMoteur.cls:423", "select NMMTR_STRUCTURE_NUMERO,NMMTR_LETTRE_CATEGORIE,NMMTR_CODAGE_ANNEE,NMMTR_SEPARATEUR,NMMTR_TAILLE_CHRONO from NUMERO_METIER where NMMTR_TYPE_METIER = 'RGB'", []),
        ("clsNumerotation.cls:46", "select distinct nmmtr_type_metier, nmmtr_structure_numero, nmmtr_codage_annee, nmmtr_lettre_categorie, nmmtr_taille_chrono, nmmtr_code_greffe, nmmtr_separateur from numero_metier WITH (nolock) order by nmmtr_type_metier desc", []),
        ("clsNumerotation.cls:121-124", "select distinct numero_metier.nmmtr_structure_numero as source_structure, numero_metier.nmmtr_lettre_categorie as source_lettre, numero_metier.nmmtr_taille_chrono as source_taille_chrono, numero_metier.nmmtr_codage_annee as source_taille_annee, numero_metier.nmmtr_separateur as source_separateur, redirection.nmmtr_type_metier as redirection_file, redirection.nmmtr_structure_numero as redirection_structure, redirection.nmmtr_lettre_categorie as redirection_lettre, redirection.nmmtr_taille_chrono as redirection_taille_chrono, redirection.nmmtr_codage_annee as redirection_taille_annee, redirection.nmmtr_separateur as redirection_separateur from numero_metier with (nolock) left outer join numero_metier as redirection with (nolock) on numero_metier.nmmtr_file_redirection = redirection.nmmtr_type_metier where numero_metier.nmmtr_type_metier = 'ARRC'", []),
        ("ULT_5NUM_MAN.cls:107-111", "SELECT NMMTR_STRUCTURE_NUMERO, NMMTR_LETTRE_CATEGORIE, NMMTR_CODAGE_ANNEE, NMMTR_TAILLE_CHRONO, NMMTR_SEPARATEUR, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_ANNEE, NMMTR_GESTION_NUM_ERR From NUMERO_METIER WHERE (NMMTR_TYPE_METIER IN ('RGB', 'JPC', 'JGDJ', 'RC1')) AND (NMMTR_ANNEE IN (2026, 2027))AND (NMMTR_ATTRIBUTION_ERRONEE = 0)", []),
        ("ULT_5NUM_MAN.cls:221-238", "SELECT NMMTR_TYPE_METIER, NMMTR_CHRONO From NUMERO_METIER WHERE (NMMTR_TYPE_METIER IN ('RGB', 'JPC', 'FACTU')) AND (NMMTR_ANNEE = 2026) AND (NMMTR_ATTRIBUTION_ERRONEE = 1)", []),
        ("MetierProcDemande.cs:260", "SELECT DISTINCT nmmtr_type_metier, nmmtr_structure_numero, nmmtr_codage_annee, nmmtr_lettre_categorie, nmmtr_taille_chrono, nmmtr_code_greffe, nmmtr_separateur FROM numero_metier WITH (NOLOCK) WHERE NMMTR_STRUCTURE_NUMERO = 'LASC' ORDER BY nmmtr_type_metier DESC", []),
        // SELECT * : l'identifiant et la date d'attribution (l'heure de chaque geste) different d'une base a l'autre.
        ("frmPrincipale.frm:101", "SELECT * From NUMERO_METIER WHERE NMMTR_TYPE_METIER LIKE 'JD%' AND NMMTR_ANNEE = '2026' AND NMMTR_ATTRIBUTION_ERRONEE = '0' order by NMMTR_TYPE_METIER",
            ["NMMTR_ID_NMMTR", "NMMTR_DATE_ATTRIBUTION"]),
    };
    async Task<List<List<string>>> Lire(string cx)
    {
        var r = new List<List<string>>();
        foreach (var (_, sql, exclues) in lectures) r.Add(await Lignes(cx, sql, exclues));
        return r;
    }
    List<string> Differences(List<List<string>> a, List<List<string>> b)
        => lectures.Select((l, i) => (l.Source, Pareil: a[i].SequenceEqual(b[i]))).Where(x => !x.Pareil).Select(x => x.Source).ToList();
    var lecturesAvant = await Lire(cxV2);
    var tableAvant = await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;");

    var bascule = await Script("bascule.sql", Variables(finBetaFuture, reprise: "oui"));
    var tableApres = await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;");
    // Ce que la reprise du lot 6 a mis dans la V3, dans la transaction de la bascule, puis le releve des valeurs
    // heritees joue apres le commit (le geste du passage de docs\reconstruction\numerotation-series.md).
    var rapportReprise = await Lignes(cxV3, "SELECT Code, LignesV2, Ecarts FROM num.Reprise;");
    var compteursRepris = await Valeur<int>(cxV3, "SELECT COUNT(*) FROM num.Compteur;");
    var seriesDeLaV3 = await Lignes(cxV3, "SELECT Type + ' ' + CAST(Annee AS varchar(6)) + ' ' + ISNULL(CAST(Chrono AS varchar(11)), 'NULL') " +
                                          "FROM num.Compteur WHERE Type IN ('FACV3', 'BDFNI');");
    var ordresRepris = await Lignes(cxV3, "SELECT Fiche + ' ' + CAST(Cle AS varchar(11)) + ' ' + CAST(Dernier AS varchar(11)) FROM num.OrdreFiche;");
    var autresRepris = await Valeur<string>(cxV3, "SELECT CONCAT((SELECT COUNT(*) FROM num.CodeLiaison WHERE Code = 'G3802'), '/', " +
                                                  "(SELECT COUNT(*) FROM num.Archive WHERE Chrono = 999), '/', " +
                                                  "(SELECT COUNT(*) FROM num.Journal WHERE Evenement = 'manquant' AND Numero = '2026B01200'));");
    var heritees = await Lignes(cxV3, "SELECT Fiche + ' ' + Valeur FROM num.ValeurHeritee;");
    Exiger(rapportReprise.SequenceEqual(["0|12|0"]) && compteursRepris == depart.Count + 2
           && seriesDeLaV3.SequenceEqual([$"BDFNI {anneeCourante} 32", $"FACV3 {anneeCourante} NULL"])
           && await ChronoV3("RGB", 2026) == 1234
           && ordresRepris.SequenceEqual(["ETABLISSEMENT 1 3", "MENTION_OBSERVATION 1 3", "MENTION_OBSERVATION 2 1"]) && autresRepris == "1/1/1"
           && heritees.SequenceEqual(["DOSSIER_RCS AP0042", "FACTURE ASTRA-AF1", "INSTANCE 2004IP00330"])
           && bascule.Messages.Any(m => m.StartsWith("Valeurs héritées relevées")),
        $"NUM-002 T03 / NUM-005 CA01 : bascule.sql Reprise=oui joue num.Reprendre dans sa transaction (rapport code|lignes V2|ecarts : " +
        $"{string.Join(", ", rapportReprise)}) : {compteursRepris} compteurs (les {depart.Count} de la V2, RGB 2026 a {await ChronoV3("RGB", 2026)}, " +
        $"et {string.Join(", ", seriesDeLaV3)}), ordres par fiche ({string.Join(", ", ordresRepris)}), liaison/archive/manquant {autresRepris} ; " +
        $"puis num.ReleverHeritage apres le commit ({string.Join(", ", heritees)})");
    var objetsV2 = await Lignes(cxV2, "SELECT SCHEMA_NAME(schema_id) + '.' + name FROM sys.objects WHERE schema_id = SCHEMA_ID('rigv3') " +
                                      "OR name IN ('TR_NUMERO_METIER_RIGV3');");
    var objetsV3 = await Lignes(cxV3, "SELECT name FROM sys.objects WHERE name IN ('TR_Compteur_RecopieV2', 'TR_NumeroRendu_RecopieV2', 'NumeroMetierV2', 'EcartsV2');");
    var corpsGardes = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.CorpsOrigine;");
    Exiger(bascule.Messages.Any(m => m.StartsWith("Bascule faite")) && objetsV3.Count == 4 && objetsV2.Count >= 12 && corpsGardes == 3,
        $"bascule.sql (variables BaseV2, BaseV3, CodeGreffe, FinBeta, SessionEchecs) : pont pose des deux cotes " +
        $"(V2 : {objetsV2.Count} objets, V3 : {string.Join(", ", objetsV3)}), corps d'origine des 3 programmes garde (beta)");
    Exiger(tableAvant.SequenceEqual(tableApres),
        $"NUM-006 T03 : la bascule laisse NUMERO_METIER en place et intacte ({tableApres.Count} lignes, aucune changee)");
    Exiger(Differences(lecturesAvant, await Lire(cxV2)) is { Count: 0 },
        $"NUM-006 CA02 : les {lectures.Length} lectures de la V2 rendent les memes resultats avant et apres le passage, sur la meme base");
    var definitions = await Lignes(cxV2, "SELECT o.name, m.uses_quoted_identifier FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id " +
                                         "WHERE o.name IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO') AND m.definition LIKE '%rigv3.%';");
    var parametres = await Lignes(cxV2, "SELECT OBJECT_NAME(object_id), parameter_id, name, TYPE_NAME(user_type_id), max_length, is_output, has_default_value " +
                                        "FROM sys.parameters WHERE OBJECT_NAME(object_id) IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    var parametresTemoin = await Lignes(cxT, "SELECT OBJECT_NAME(object_id), parameter_id, name, TYPE_NAME(user_type_id), max_length, is_output, has_default_value " +
                                             "FROM sys.parameters WHERE OBJECT_NAME(object_id) IN ('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    Exiger(definitions.Count == 3 && parametres.SequenceEqual(parametresTemoin),
        $"NUM-002 T01 / NUM-003 T01 : les 3 programmes remplaces gardent leur nom, leurs parametres, leurs types, leurs tailles et " +
        $"leurs valeurs par defaut ({parametres.Count} parametres identiques a ceux d'origine)");
    var synonymes = await Lignes(cxV2, "SELECT name + ' -> ' + base_object_name FROM sys.synonyms WHERE schema_id = SCHEMA_ID('rigv3');");
    Exiger(synonymes.Count == 2 && synonymes.All(s => s.Contains($"[{v3.Nom}].[num]")),
        $"NUM-002 T02 : un synonyme par programme vers la base V3 du greffe ({string.Join(" ; ", synonymes)}), seul endroit ou son nom est ecrit");

    // =================================================================================================
    // NUM-002 — Faire attribuer par la V3 les numeros que demande l'ancien logiciel
    // =================================================================================================

    // CA02 : le compteur RGB 2026 repris a 1234.
    var rgb = await Generer(agentV2, "RGB", 2026);
    var rgbT = await Generer(agentT, "RGB", 2026);
    Donne(rgb.Numero);
    Exiger(rgb.Code == 0 && rgb.Numero == "2026B01235" && await ChronoV3("RGB", 2026) == 1235 && rgbT.Numero == rgb.Numero,
        $"NUM-002 CA02 : RGB 2026 repris a 1234 -> la V2 recoit {rgb.Numero} (la V2 d'origine : {rgbT.Numero}) et le compteur de la V3 vaut {await ChronoV3("RGB", 2026)}");
    Exiger(await ChronoV2(cxV2, "RGB", 2026) == 1235,
        "NUM-006 CA01 : la ligne RGB 2026 de NUMERO_METIER suit, dans la meme transaction (chrono 1235)");

    // CA01 : les sept appels de la V2, tels qu'ils sont ecrits, sur la V2 basculee et sur le temoin.
    var appelants = new (string Nom, string Source, Func<string, Task<(string? Numero, int LignesAvant)>> Appeler)[]
    {
        (".NET central, sortie 40, code greffe absent", "RigMetier\\Numerotation\\Numerotation.cs:44-75", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("GENERER_NUMERO", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = "RGB";
            cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@ANNEE", SqlDbType.Int).Value = 2026;
            cmd.Parameters.Add("@QUANTIEME", SqlDbType.Int).Value = 0;
            await cmd.ExecuteNonQueryAsync();
            return (cmd.Parameters["@NUMERO_RETOURNE"].Value.ToString(), 0);
        }),
        ("VB6 central, sortie 40 en entree-sortie, code greffe vide", "RIG_CliSerL_Numerotation\\Numero.cls:278-300", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("GENERER_NUMERO", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = "RC1";
            var n = cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 40); n.Direction = ParameterDirection.InputOutput; n.Value = "";
            cmd.Parameters.Add("@ANNEE", SqlDbType.Int).Value = 2026;
            cmd.Parameters.Add("@QUANTIEME", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@CODE_GREFFE", SqlDbType.VarChar, 5).Value = "";
            await cmd.ExecuteNonQueryAsync();
            return (cmd.Parameters[1].Value.ToString(), 0);
        }),
        ("VB6 par lot SQL, sortie 20, code greffe vide", "RIG_CliSerL_Inscription\\modGeneral.bas:70-84", async cx =>
        {
            // ADO fait un jeu de resultats de chaque message « n lignes affectees » : le code saute le premier
            // (NextRecordset) et lit le numero dans le suivant. Il en faut donc exactement un avant le SELECT.
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            var lignes = 0;
            await using var cmd = new SqlCommand(
                "declare @vTYPE_NUMERO_METIER as varchar(5);\r\ndeclare @vNUMERO_RETOURNE as varchar(20);\r\ndeclare @vANNEE as integer;\r\n" +
                "declare @vQUANTIEME as integer;\r\ndeclare @vCODE_GREFFE as varchar(5);\r\ndeclare @vMONERREUR as integer;\r\n" +
                "set @vTYPE_NUMERO_METIER = 'RC5';\r\nset @vNUMERO_RETOURNE = '';\r\nset @vANNEE = 2026;\r\nset @vQUANTIEME = 0;\r\n" +
                "set @vCODE_GREFFE = '';\r\nset @vMONERREUR = -1;\r\n" +
                "execute dbo.GENERER_NUMERO @TYPE_NUMERO_METIER=@vTYPE_NUMERO_METIER, @NUMERO_RETOURNE=@vNUMERO_RETOURNE output, @ANNEE=@vANNEE, " +
                "@QUANTIEME=@vQUANTIEME, @CODE_GREFFE=@vCODE_GREFFE, @MONERREUR=@vMONERREUR;\r\nselect @vNUMERO_RETOURNE as Numero;", cn);
            cmd.StatementCompleted += (_, _) => lignes++;
            await using var l = await cmd.ExecuteReaderAsync();
            var avant = lignes;
            if (avant != 1) return (null, avant);
            return (await l.ReadAsync() ? l["Numero"] as string : null, avant);
        }),
        ("injecteur d'avis, type DMD, sortie 20, code greffe fourni", "EdiOld\\VB6\\InjecteurEdi_AVISIR\\TraitementAvis.cls:180-199", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("GENERER_NUMERO", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = "DMD";
            var n = cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 20); n.Direction = ParameterDirection.InputOutput; n.Value = "";
            cmd.Parameters.Add("@ANNEE", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@QUANTIEME", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@CODE_GREFFE", SqlDbType.VarChar, 5).Value = CodeGreffe;
            await cmd.ExecuteNonQueryAsync();
            return (cmd.Parameters["@NUMERO_RETOURNE"].Value.ToString(), 0);
        }),
        ("surveillance des inscriptions, sortie 20, ExecuteReader", "RCS\\Exe\\Surveillance des inscriptions\\frmDebiteurs.vb:673-688", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("GENERER_NUMERO", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddRange(new[]
            {
                new SqlParameter("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5),
                new SqlParameter("@NUMERO_RETOURNE", SqlDbType.VarChar, 20, ParameterDirection.InputOutput, false, 0, 0, "", DataRowVersion.Current, null),
                new SqlParameter("@ANNEE", SqlDbType.SmallInt, 2),
                new SqlParameter("@QUANTIEME", SqlDbType.SmallInt, 2),
                new SqlParameter("@CODE_GREFFE", SqlDbType.VarChar, 5),
                new SqlParameter("@MONERREUR", SqlDbType.Int, 4),
            });
            cmd.Parameters["@TYPE_NUMERO_METIER"].Value = "JPC";
            cmd.Parameters["@NUMERO_RETOURNE"].Value = DBNull.Value;
            cmd.Parameters["@CODE_GREFFE"].Value = CodeGreffe;
            await using var l = await cmd.ExecuteReaderAsync();
            await l.ReadAsync();
            var numero = cmd.Parameters["@NUMERO_RETOURNE"].Value.ToString();
            return (numero, 0);
        }),
        ("inscriptions en masse, sortie 20, code greffe fourni", "RCS\\Exe\\InscriptionsMasse\\DetailsDebiteur.vb:1598-1611", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("[GENERER_NUMERO]", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5).Value = "RC1";
            cmd.Parameters.Add("@NUMERO_RETOURNE", SqlDbType.VarChar, 20).Direction = ParameterDirection.Output;
            cmd.Parameters.Add("@ANNEE", SqlDbType.Int).Value = 2026;
            cmd.Parameters.Add("@QUANTIEME", SqlDbType.Int).Value = 0;
            cmd.Parameters.Add("@CODE_GREFFE", SqlDbType.VarChar, 5).Value = CodeGreffe;
            await using var l = await cmd.ExecuteReaderAsync();
            await l.ReadAsync();
            return (cmd.Parameters["@NUMERO_RETOURNE"].Value.ToString(), 0);
        }),
        ("inscriptions en masse, OutilRIG, sortie 20", "RCS\\Exe\\InscriptionsMasse\\OutilRIG.vb:129-160", async cx =>
        {
            await using var cn = new SqlConnection(cx); await cn.OpenAsync();
            await using var cmd = new SqlCommand("[GENERER_NUMERO]", cn) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.Add(new SqlParameter("@TYPE_NUMERO_METIER", SqlDbType.VarChar, 5) { Direction = ParameterDirection.Input, Value = "RC5" });
            cmd.Parameters.Add(new SqlParameter("@NUMERO_RETOURNE", SqlDbType.VarChar, 20) { Direction = ParameterDirection.Output });
            cmd.Parameters.Add(new SqlParameter("@ANNEE", SqlDbType.Int) { Direction = ParameterDirection.Input, Value = 2026 });
            cmd.Parameters.Add(new SqlParameter("@QUANTIEME", SqlDbType.Int) { Direction = ParameterDirection.Input, Value = 0 });
            cmd.Parameters.Add(new SqlParameter("@CODE_GREFFE", SqlDbType.VarChar, 5) { Direction = ParameterDirection.Input, Value = CodeGreffe });
            await using var l = await cmd.ExecuteReaderAsync();
            await l.ReadAsync();
            return (cmd.Parameters["@NUMERO_RETOURNE"].Value.ToString(), 0);
        }),
    };
    var numerosAppelants = new List<string?>();
    foreach (var (nom, source, appeler) in appelants)
    {
        var (n, avant) = await appeler(agentV2);
        var (nT, avantT) = await appeler(agentT);
        numerosAppelants.Add(n);
        Donne(n);
        Exiger(!string.IsNullOrEmpty(n) && n == nT && avant == avantT,
            $"NUM-002 CA01 : {nom} ({source}) recoit {n ?? "(rien)"} du compteur de la V3, comme la V2 d'origine ({nT ?? "(rien)"})" +
            (avant > 0 || avantT > 0 ? $", avec le meme message « 1 ligne affectee » avant le numero ({avant}/{avantT})" : ""));
    }
    Exiger(await Valeur<int>(cxV3, "SELECT COUNT(*) FROM num.Journal WHERE Logiciel = 'V2' AND Evenement = 'attribution';") == 1 + appelants.Length,
        $"NUM-002 CA01 : les {appelants.Length} appels et celui de CA02 sont au journal de la V3, logiciel « V2 »");

    // CA05 : un numero rendu, pas encore redonne, dans une serie a restitution active : il revient d'abord.
    var aRendre = numerosAppelants[0]!;                          // RGB, du premier appelant
    var rendu = await Restituer(agentV2, "RGB", aRendre, 0, "saisie annulee");
    var renduT = await Restituer(agentT, "RGB", aRendre, 0, "saisie annulee");
    Rendu(aRendre);
    var ligneRendue = await Valeur<int>(cxV2, $"SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'RGB' AND NMMTR_ATTRIBUTION_ERRONEE = 1 AND NMMTR_CHRONO = {int.Parse(aRendre[5..])};");
    var redonne = await Generer(agentV2, "RGB", 2026);
    var redonneT = await Generer(agentT, "RGB", 2026);
    Donne(redonne.Numero);
    Exiger(rendu.Code == 0 && Convert.ToInt32(rendu.Sorties["@MonErreur"]) == 0 && ligneRendue == 1 && rendu.Lignes == renduT.Lignes
           && redonne.Numero == aRendre && redonneT.Numero == aRendre
           && await Valeur<int>(cxV2, $"SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'RGB' AND NMMTR_ATTRIBUTION_ERRONEE = 1;") == 0,
        $"NUM-002 CA05 : {aRendre} rendu par RESTITUER_NUMERO (ligne « attribution erronee » recopiee dans la V2) puis redonne d'abord " +
        $"({redonne.Numero}, la V2 d'origine : {redonneT.Numero}) ; sa ligne disparait de la V2");

    // CA04 : sans annee, sans jour, type redirige (ARRC -> ARRD).
    var arrc = await Generer(agentV2, "ARRC", 0, 0);
    var arrcT = await Generer(agentT, "ARRC", 0, 0);
    var attenduArrc = $"A{anneeCourante % 100:00}{jourCourant:000}00116";
    Exiger(arrc.Code == 0 && arrc.Numero == arrcT.Numero && arrc.Numero is { Length: 11 } && arrc.Numero[..6] == attenduArrc[..6]
           && await ChronoV3("ARRD", anneeCourante, jourCourant) is not null,
        $"NUM-002 CA04 : ARRC sans annee ni jour -> serie ARRD, annee {anneeCourante}, jour {jourCourant} du serveur : {arrc.Numero} (V2 d'origine : {arrcT.Numero})");

    // CA06 : appel fait dans la transaction de la V2, transaction annulee.
    var etatAvantTx = await Etat();
    await using (var cn = new SqlConnection(agentV2))
    {
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var dansTx = await Generer(agentV2, "RGB", 2026, cn: cn, tx: tx);
        var vuDansTx = 0;
        await using (var lire = new SqlCommand($"SELECT MAX(Chrono) FROM [{v3.Nom}].num.Compteur WHERE Type = 'RGB' AND Annee = 2026;", cn, tx))
            vuDansTx = (int)(await lire.ExecuteScalarAsync())!;
        await tx.RollbackAsync();
        Exiger(dansTx.Code == 0 && vuDansTx == int.Parse(dansTx.Numero![5..]) && await Etat() == etatAvantTx,
            $"NUM-002 CA06 : {dansTx.Numero} pris dans la transaction de la V2 (compteur V3 a {vuDansTx} dans la transaction), transaction annulee " +
            "-> compteur V3, ligne V2 et journal reviennent en arriere ensemble (meme instance, meme transaction, sans coordinateur)");
    }

    // CA07 : 0 avec un numero, -2 sans rien ecrire, un code non nul avec un message ; jamais 0 avec un numero vide.
    var etatAvantInconnu = await Etat();
    var inconnu = await Generer(agentV2, "ZZZZ", 2026);
    var etatApresInconnu = await Etat();
    // fix-ok: au 4e passage, journal 11 -> 12 mesure entre les deux etats : c'etait le refus -5 de PLEIN (journalise
    // par num.Attribuer), pris apres le type inconnu ; de meme, au 5e, deux lignes DMD sous « select distinct » et
    // les contraintes des tables rigv3 comptees comme restes du pont -- trois erreurs du banc, pas du produit.
    var plein = await Generer(agentV2, "PLEIN", 2026);          // lui journalise son refus (lot 5) : apres la mesure
    Exiger(inconnu.Code == -2 && inconnu.Numero is null && inconnu.Leve is null && inconnu.Lignes == 0 && etatApresInconnu == etatAvantInconnu,
        $"NUM-002 CA07 : type inconnu -> {inconnu.Code}, sans message ({inconnu.Leve?.Message ?? "aucun"}), sans numero ({inconnu.Numero ?? "aucun"}), " +
        $"{inconnu.Lignes} message « n lignes », rien d'ecrit (V2, V3, journal : {etatAvantInconnu} -> {etatApresInconnu}), comme la V2 d'origine");
    Exiger(plein.Code == -5 && plein.Numero is null && plein.Leve?.Message.Contains("Série pleine") == true && plein.Leve.Gravite == 16,
        $"NUM-002 CA07 : serie pleine (PLEIN a 99 sur 2 chiffres ; la V2 d'origine bouclait sans fin) -> -5 et une erreur levee « {plein.Leve?.Message} »");

    // CA08 : un format qui porte le code greffe (LIAI, LGACK) ; vide, absent ou « 9999 » -> celui du compteur.
    var liaiVide = await Generer(agentV2, "LIAI", 2026, greffe: "");
    var liaiAbsent = await Generer(agentV2, "LIAI", 2026, greffe: null);
    var liai9999 = await Generer(agentV2, "LIAI", 2026, greffe: "9999");
    var liaiFourni = await Generer(agentV2, "LIAI", 2026, greffe: "3801");
    var liaiT = await Generer(agentT, "LIAI", 2026, greffe: null);
    foreach (var g in new string?[] { "", "9999", "3801" }) await Generer(agentT, "LIAI", 2026, greffe: g);   // le temoin suit
    Exiger(new[] { liaiVide, liaiAbsent, liai9999 }.All(a => a.Code == 0 && a.Numero!.StartsWith("G3802") && !a.Numero.Contains("9999"))
           && liaiFourni.Numero!.StartsWith("G3801"),
        $"NUM-002 CA08 : LIAI avec un code vide, absent ou « 9999 » -> code du compteur ({liaiVide.Numero}, {liaiAbsent.Numero}, {liai9999.Numero}), " +
        $"jamais « 9999 » ; un vrai code est garde ({liaiFourni.Numero}). La V2 d'origine, sans code : {liaiT.Numero} (ecart a valider)");

    // CA03 : les 142 types de compteurs-d8.json, composes par la V3 et par la V2 d'origine.
    var d8Json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dossierBanc, "compteurs-d8.json")));
    var hasard = new Random(20261121);
    var differents = new List<string>();
    var composes = 0;
    var types = 0;
    foreach (var c in d8Json.RootElement.GetProperty("compteurs").EnumerateArray())
    {
        types++;
        var type = c.GetProperty("type").GetString()!;
        foreach (var f in c.GetProperty("formats").EnumerateArray())
        {
            var structure = f.GetProperty("structure").GetString()!;
            var codage = f.GetProperty("codage_annee").GetInt32();
            var lettre = f.GetProperty("lettre").GetString() ?? "";
            var sep = f.GetProperty("separateur").GetString() ?? "";
            var taille = f.GetProperty("taille_chrono").GetInt32();
            var annee = hasard.Next(2001, 2100);
            var jour = hasard.Next(1, 367);
            var chrono = hasard.Next(2, (int)Math.Min(Math.Pow(10, taille) - 1, 999_999));
            await Executer(cxD8,
                "DELETE dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = '" + type.Replace("'", "''") + "'; " +
                "INSERT dbo.NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_STRUCTURE_NUMERO, NMMTR_CODE_GREFFE, NMMTR_ANNEE, NMMTR_CODAGE_ANNEE, " +
                "NMMTR_LETTRE_CATEGORIE, NMMTR_SEPARATEUR, NMMTR_TAILLE_CHRONO, NMMTR_CHRONO_DEBUT, NMMTR_CHRONO, NMMTR_QUANTIEME, NMMTR_GESTION_NUM_ERR) VALUES (" +
                $"'{type.Replace("'", "''")}', '{structure}', '3802', {annee}, {codage}, {Sql(lettre)}, {Sql(sep)}, {taille}, 1, {chrono - 1}, {jour}, 0);");
            var v2Compose = await Generer(cxD8, type, annee, jour, greffe: "3802");
            var v3Compose = await Valeur<string>(cxV3,
                $"SELECT num.Composer('{structure}', {codage}, {Sql(lettre)}, {Sql(sep)}, {taille}, '3802', {annee}, {jour}, {chrono});");
            composes++;
            if (v2Compose.Code != 0 || v2Compose.Numero != v3Compose)
                differents.Add($"{type} {structure} {annee}/{jour}/{chrono} : V2 {v2Compose.Numero ?? v2Compose.Code.ToString()} V3 {v3Compose}");
        }
    }
    Exiger(types == 142 && composes == 143 && differents.Count == 0,
        $"NUM-002 CA03 : {types} types, {composes} formats de compteurs-d8.json, annee, jour et chrono tires au hasard (graine 20261121) -> " +
        $"num.Composer ecrit le meme numero que GENERER_NUMERO.sql:245-288, caractere pour caractere" +
        (differents.Count > 0 ? " ; differents : " + string.Join(" ; ", differents.Take(5)) : ""));

    // =================================================================================================
    // NUM-003 — Remplacer aussi le second programme d'attribution
    // =================================================================================================

    // CA01 : type et annee -> le numero suivant du compteur de la V3, comme le premier programme (sortie 20).
    var avantChercher = (await ChronoV3("RGB", 2026))!.Value;
    var cherche = await Chercher(agentV2, "RGB", 2026);
    var chercheT = await Chercher(agentT, "RGB", 2026);
    var puisGenere = await Generer(agentV2, "RGB", 2026);
    var puisGenereT = await Generer(agentT, "RGB", 2026);
    Donne(cherche.Numero); Donne(puisGenere.Numero);
    Exiger(cherche.Code == 0 && cherche.Numero == $"2026B{avantChercher + 1:00000}" && puisGenere.Numero == $"2026B{avantChercher + 2:00000}"
           && cherche.Numero == chercheT.Numero && puisGenere.Numero == puisGenereT.Numero && cherche.Lignes == chercheT.Lignes,
        $"NUM-003 CA01 : CHERCHER_NUMERO RGB 2026 -> {cherche.Numero}, le suivant du compteur de la V3 ; GENERER_NUMERO enchaine " +
        $"({puisGenere.Numero}) : un seul compteur pour les deux programmes, comme la V2 d'origine ({chercheT.Numero}, {puisGenereT.Numero})");

    // CA04 : JDSAN, lettre « SANC » de quatre caracteres.
    var sanc = await Chercher(agentV2, "JDSAN", 2026);
    var sancT = await Chercher(agentT, "JDSAN", 2026);
    var sancGenere = await Generer(agentV2, "JDSAN", 2026);
    var sancGenereT = await Generer(agentT, "JDSAN", 2026);
    Exiger(sanc.Numero == "SANC2026/00013" && sancGenere.Numero == "SANC2026/00014",
        $"NUM-003 CA04 : JDSAN par le second programme -> {sanc.Numero}, « SANC » en entier comme le premier ({sancGenere.Numero}) ; " +
        $"la V2 d'origine ecrivait {sancT.Numero} (ecart a valider)");

    // CA03 : chaque appel journalise avec le compte et le poste appelants (l'agent que la V2 pose, sinon le compte SQL).
    await using (var cn = new SqlConnection(agentV2))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("EXEC sys.sp_set_session_context N'RigUserId', 1; DECLARE @n varchar(20); " +
                                             "EXEC dbo.CHERCHER_NUMERO 'RGB', @n OUTPUT, 2026; SELECT @n;", cn);
        var n = (string)(await cmd.ExecuteScalarAsync())!;
        Donne(n);
        await using var cmdT = new SqlCommand("DECLARE @n varchar(20); EXEC dbo.CHERCHER_NUMERO 'RGB', @n OUTPUT, 2026;", new SqlConnection(agentT));
        await cmdT.Connection.OpenAsync(); await cmdT.ExecuteNonQueryAsync(); await cmdT.Connection.DisposeAsync();
        var journal = await Lignes(cxV2, "SELECT Compte, Poste, Application, Type, Annee, Numero, Code FROM rigv3.AppelChercher;");
        var journalV3 = await Valeur<string>(cxV3, $"SELECT CONCAT(Compte, '|', Poste, '|', Logiciel) FROM num.Journal WHERE Numero = '{n}';");
        Exiger(journal.Count == 3 && journal.Contains($"agent.dupont|POSTE-V2-AGENT|RIG V2|RGB|2026|{n}|0")
               && journal.Count(j => j.StartsWith(agent + "|POSTE-V2-AGENT|RIG V2|")) == 2 && journalV3 == "agent.dupont|POSTE-V2-AGENT|V2",
            $"NUM-003 CA03 : les 3 appels de CHERCHER_NUMERO sont dans rigv3.AppelChercher (compte, poste, application, type, numero, code : " +
            $"{string.Join(" ; ", journal)}) ; " +
            $"l'agent pose par la V2 (RigUserId -> agent.dupont) plutot que le compte SQL, aussi au journal de la V3 ({journalV3})");
    }

    // CA02 : le releve des appelants (objets SQL et taches planifiees), refait base par base.
    var releveVide = await Sqlcmd.JouerAsync(cxV2, Path.Combine(dossierScripts, "releve-appelants.sql"), new Dictionary<string, string>());
    await Executer(cxV2, "CREATE PROCEDURE dbo.BANC_APPEL_OUBLIE AS BEGIN DECLARE @n varchar(20); EXEC dbo.CHERCHER_NUMERO 'RGB', @n OUTPUT; END;");
    await Executer(cxMaster, $"EXEC msdb.dbo.sp_add_job @job_name = N'{tache}'; EXEC msdb.dbo.sp_add_jobstep @job_name = N'{tache}', " +
                             $"@step_name = N'numeroter', @subsystem = N'TSQL', @database_name = N'{v2.Nom}', " +
                             "@command = N'DECLARE @n varchar(20); EXEC dbo.CHERCHER_NUMERO ''RGB'', @n OUTPUT;';");
    var releve = await Sqlcmd.JouerAsync(cxV2, Path.Combine(dossierScripts, "releve-appelants.sql"), new Dictionary<string, string>());
    var trouves = releve.Resultats.Single().Select(r => $"{r[1]} {r[2]}").ToList();
    Exiger(releveVide.Resultats.Single().Count(r => (string)r[1]! == "objet SQL") == 0
           && trouves.Contains("objet SQL dbo.BANC_APPEL_OUBLIE") && trouves.Any(t => t.StartsWith($"tâche planifiée {tache}")),
        $"NUM-003 CA02 : releve-appelants.sql ne trouve aucun appelant dans une base qui n'en a pas, et trouve un objet SQL et une tache " +
        $"planifiee poses expres ({string.Join(" ; ", trouves.Where(t => t.Contains("BANC") || t.Contains(tache)))})");
    await Executer(cxV2, "DROP PROCEDURE dbo.BANC_APPEL_OUBLIE;");
    await Executer(cxMaster, $"EXEC msdb.dbo.sp_delete_job @job_name = N'{tache}';");
    Reste("NUM-003 CA02 / T03 / T05 : jouer releve-appelants.sql sur les 40 bases (34 sur SQL-PROD, 6 outre-mer) et ecrire le resultat " +
          "dans numerotation-v2.md -- aucun acces a ces serveurs depuis ce poste ; RIG_DEV : objets SQL releves par le carnet (2026-11-20), " +
          "taches planifiees de SQL-DEV non relevees (voir le doc)");

    // =================================================================================================
    // NUM-006 — Laisser l'ancien logiciel lire ses compteurs comme avant
    // =================================================================================================

    // Les memes gestes des deux cotes : nouvelle annee, nouveau jour, redirection, restitution gardee, serie sans restitution.
    var gestes = new List<(string Quoi, string? V2, string? Temoin)>();
    async Task Geste(string quoi, Func<string, Task<Appel>> faire)
    {
        var a = await faire(agentV2);
        var t = await faire(agentT);
        gestes.Add((quoi, a.Numero ?? a.Code.ToString(), t.Numero ?? t.Code.ToString()));
    }
    await Geste("JPC 2026", cx => Generer(cx, "JPC", 2026));
    await Geste("JPC 2026 rendu", cx => Restituer(cx, "JPC", "2026RJ0244", 2026, "acte annule"));
    await Geste("RGB 2027 (nouvelle annee)", cx => Generer(cx, "RGB", 2027));
    await Geste("JGDJ jour 58", cx => Generer(cx, "JGDJ", 2026, 58));
    await Geste("JGDJ jour 59 (nouveau jour)", cx => Generer(cx, "JGDJ", 2026, 59));
    await Geste("ARRC jour 300 (redirige)", cx => Generer(cx, "ARRC", 2026, 300));
    await Geste("FACTU 2026", cx => Generer(cx, "FACTU", 2026));
    await Geste("FACTU rendu (sans restitution)", cx => Restituer(cx, "FACTU", "26-022769", 2026, "facture annulee"));
    await Geste("RC5 2026", cx => Generer(cx, "RC5", 2026));
    var gestesDifferents = gestes.Where(g => g.V2 != g.Temoin).ToList();
    Exiger(gestesDifferents.Count == 0,
        $"NUM-006 CA01 : {gestes.Count} gestes (nouvelle annee, nouveau jour, redirection, restitution, serie sans restitution) donnent le meme " +
        $"numero par la V3 que par la V2 d'origine ({string.Join(", ", gestes.Select(g => g.V2))})" +
        (gestesDifferents.Count > 0 ? " ; differents : " + string.Join(", ", gestesDifferents) : ""));

    // CA02 : apres les memes attributions, les lectures de la V2 basculee = celles de la V2 d'origine.
    var lecturesV2 = await Lire(cxV2);
    var lecturesT = await Lire(cxT);
    var lecturesDifferentes = Differences(lecturesV2, lecturesT);
    // Ecart a valider de NUM-002 CA08 : la V2 d'origine ecrit dans la ligne d'un jour qu'elle cree le code greffe
    // de l'appelant (« 9999 » par defaut), la V3 celui de son compteur. Seules ces lignes peuvent differer, et
    // seulement par ce code : les lectures sont refaites code greffe ecarte, et les lignes en cause comptees.
    var codesDifferents = await Lignes(cxV3,
        "SELECT v.NMMTR_TYPE_METIER + ' ' + CAST(v.NMMTR_ANNEE AS varchar(6)) + ' jour ' + CAST(v.NMMTR_QUANTIEME AS varchar(6)) + ' : V2 d''origine ' " +
        "+ ISNULL(t.NMMTR_CODE_GREFFE, 'NULL') + ', V3 ' + ISNULL(v.NMMTR_CODE_GREFFE, 'NULL') " +
        $"FROM [{v2.Nom}].dbo.NUMERO_METIER AS v JOIN [{temoin.Nom}].dbo.NUMERO_METIER AS t ON t.NMMTR_TYPE_METIER = v.NMMTR_TYPE_METIER " +
        "AND t.NMMTR_ANNEE = v.NMMTR_ANNEE AND ISNULL(t.NMMTR_QUANTIEME, -1) = ISNULL(v.NMMTR_QUANTIEME, -1) " +
        "AND t.NMMTR_ATTRIBUTION_ERRONEE = v.NMMTR_ATTRIBUTION_ERRONEE AND t.NMMTR_CHRONO = v.NMMTR_CHRONO " +
        "WHERE ISNULL(t.NMMTR_CODE_GREFFE, '') <> ISNULL(v.NMMTR_CODE_GREFFE, '');");
    var attendus = new List<string> { $"DMD {anneeCourante} jour {jourCourant} : V2 d'origine {CodeGreffe}, V3 9999", "JGDJ 2026 jour 59 : V2 d'origine 9999, V3 3801" };
    attendus.Sort(StringComparer.Ordinal);
    var memesSansCode = true;
    foreach (var (_, sql, exclues) in lectures)
    {
        var sans = exclues.Append("nmmtr_code_greffe").ToArray();
        // En ensembles : un « select distinct » garde deux lignes DMD dans la V2 d'origine (deux codes), une seule ici.
        memesSansCode &= (await Lignes(cxV2, sql, sans)).Distinct().SequenceEqual((await Lignes(cxT, sql, sans)).Distinct());
    }
    Exiger(memesSansCode && lecturesDifferentes.All(d => d is "clsNumerotation.cls:46" or "MetierProcDemande.cs:260")
           && codesDifferents.SequenceEqual(attendus),
        $"NUM-006 CA02 : apres {gestes.Count + appelants.Length + 12} attributions et restitutions faites par la V3, les {lectures.Length} lectures de la V2 " +
        $"({string.Join(", ", lectures.Select(l => l.Source))}) rendent ce que rendrait la V2 d'origine, au code greffe pres des {codesDifferents.Count} " +
        $"lignes de jour creees par un appel qui passait un autre code (ecart NUM-002 CA08 : {string.Join(" ; ", codesDifferents)})" +
        (lecturesDifferentes.Count > 0 ? $" -- lectures touchees : {string.Join(", ", lecturesDifferentes)}" : ""));
    var ecartsV2V3 = await Lignes(cxV3, "SELECT * FROM num.EcartsV2;");
    Exiger(ecartsV2V3.Count == 0, $"NUM-006 CA01 : num.EcartsV2 vide -- chaque compteur de la V3 est dans la V2, au meme chrono ({string.Join(" ; ", ecartsV2V3.Take(3))})");

    // CA01 : la V3 pour elle-meme (vrai serveur, en memoire) : attribution, restitution, nouvelle annee, nouveau jour.
    // Comme en production (lot 8) : le serveur lit son catalogue dans la base commune, qui mene le greffe 8002 a la
    // base V3 ; les routes portent le greffe (RoutesCompteur.Pour) et jugent l'agent dans la base V3 de CE greffe.
    // L'agent se designe par l'en-tete de developpement X-RigV3-Compte (SOC-001 CA07), d'ou « Development ».
    // Modele : tests\NumerotationCompteurCheck\Program.cs (lot 8).
    // fix-ok: sur main b3111fe (lot 8 reuni), POST /numerotation/numeros n'existe plus et la route par greffe rend un
    // refus sans corps faute de session : banc rouge mesure (JsonException apres 35 OK), d'ou ce raccord.
    await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));
    const string AgentV3 = "banc.numerotationv2";
    await Executer(cxV3, $"""
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('{CodeGreffe}', 'NUMV2', N'Numerotation');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANCNUMV2', 'Banc NumerotationV2');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT '{AgentV3}', 'BANC', 'NumerotationV2', 'BANC.NUMV2', 'M.', '{CodeGreffe}', Id, 0
        FROM soc.Service WHERE CodeGreffe = '{CodeGreffe}' AND Abrege = 'NUMV2';
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANCNUMV2' FROM soc.Compte WHERE CompteWindows = '{AgentV3}';
        -- La restitution par la route est reservee a XNUMERO (RoutesCompteur.FonctionReattribution, 21/11) : sans
        -- elle, 403 (« 200 403 200 200 » mesure). La reprise la donne aux roles qui l'ont dans la V2.
        IF NOT EXISTS (SELECT 1 FROM soc.Fonction WHERE Code = '{RoutesCompteur.FonctionReattribution}')
            INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('{RoutesCompteur.FonctionReattribution}', 'Reattribution de numero metier', 'PROCVB6');
        INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANCNUMV2', '{RoutesCompteur.FonctionReattribution}');
        """);
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    await Executer(SansPool(commune.Connexion),
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        $"VALUES ('{CodeGreffe}', 'Greffe {CodeGreffe} du banc', N'{serveurBancs.DataSource}', N'{v2.Nom}', N'{v3.Nom}', 1, 0, " +
        "'Romance Standard Time', 'banc', SYSDATETIME());");
    // Le serveur et la base viennent du catalogue ; sans pool, comme le reste du banc (la base V3 est mise hors
    // ligne plus loin : aucune connexion du serveur ne doit rester en reserve).
    var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString) { Pooling = false };
    authentification.Remove("Data Source");
    app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();
    var http = app.GetTestClient();
    http.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, AgentV3);
    var routeNumeros = RoutesCompteur.Pour(RoutesCompteur.Numeros, CodeGreffe);
    var routeRestitutions = RoutesCompteur.Pour(RoutesCompteur.Restitutions, CodeGreffe);
    var statutsV3 = new List<int>();
    // Un refus d'acces (401, 403, 404, 503 de la politique) n'a pas de corps : il se dit tel quel, pas en JsonException.
    async Task<T> LireReponse<T>(HttpResponseMessage r, string route)
    {
        statutsV3.Add((int)r.StatusCode);
        var corps = await r.Content.ReadAsStringAsync();
        if (corps.Length == 0)
            throw new InvalidOperationException($"POST {route} : {(int)r.StatusCode} {r.StatusCode} sans reponse (session, greffe ou base du greffe)");
        return JsonSerializer.Deserialize<T>(corps, json)!;
    }
    async Task<NumeroAttribue> V3(string type, int annee = 0, int jour = 0)
    {
        using var r = await http.PostAsJsonAsync(routeNumeros, new DemandeNumero(type, annee, jour), json);
        return await LireReponse<NumeroAttribue>(r, routeNumeros);
    }
    var v3Rgb = await V3("RGB", 2026);
    Donne(v3Rgb.Numero);
    var numerosV3 = new List<int> { v3Rgb.Chrono!.Value };
    var v2ApresV3 = await ChronoV2(cxV2, "RGB", 2026);
    using (var r = await http.PostAsJsonAsync(routeRestitutions, new DemandeRestitution("RGB", v3Rgb.Numero!, "acte V3 annule"), json))
    {
        var resultat = await LireReponse<ResultatRestitution>(r, routeRestitutions);
        if (resultat.Code == 0) Rendu(v3Rgb.Numero!);
    }
    var ligneV3Rendue = await Valeur<int>(cxV2, $"SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'RGB' AND NMMTR_ANNEE = 2026 " +
                                                $"AND NMMTR_ATTRIBUTION_ERRONEE = 1 AND NMMTR_CHRONO = {v3Rgb.Chrono} AND NMMTR_EXPLICATION = 'acte V3 annule';");
    var v3Annee = await V3("JPC", 2027);
    var v3Jour = await V3("JGDJ", 2026, 60);
    var lignesV3Nouvelles = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_ATTRIBUTION_ERRONEE = 0 AND " +
                                                    "((NMMTR_TYPE_METIER = 'JPC' AND NMMTR_ANNEE = 2027 AND NMMTR_CHRONO = 1) OR " +
                                                    "(NMMTR_TYPE_METIER = 'JGDJ' AND NMMTR_ANNEE = 2026 AND NMMTR_QUANTIEME = 60 AND NMMTR_CHRONO = 1));");
    Exiger(statutsV3.All(s => s == 200) && v3Rgb.Code == 0 && v2ApresV3 == v3Rgb.Chrono && ligneV3Rendue == 1 && v3Annee.Numero == "2027RJ0001"
           && v3Jour.Code == 0 && lignesV3Nouvelles == 2,
        $"NUM-006 CA01 : la V3 pour elle-meme (POST {routeNumeros}, agent {AgentV3} admis dans la base du greffe : {string.Join(" ", statutsV3)}) " +
        $"-> {v3Rgb.Numero} : chrono de la V2 a {v2ApresV3} ; restitution -> ligne " +
        $"« attribution erronee » dans la V2 ; nouvelle annee ({v3Annee.Numero}) et nouveau jour ({v3Jour.Numero}) -> leurs lignes dans la V2");
    // Une serie que seule la V3 tient (FACV3, ouverte par la reprise du lot 6) n'a pas de ligne correspondante
    // dans la V2 : elle n'en recoit pas. Sinon num.Reprendre refuserait toute nouvelle reprise de cette base
    // (« la V2 a deja un type FACV3 ou BDFNI », NUM-005_reprise.sql:631-632).
    var v3Facture = await V3("FACV3", anneeCourante);
    var lignesSeriesV3 = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER IN ('FACV3', 'BDFNI');");
    var ecartsApresFacture = await Lignes(cxV3, "SELECT * FROM num.EcartsV2;");
    Exiger(v3Facture.Code == 0 && v3Facture.Chrono == 1 && lignesSeriesV3 == 0 && ecartsApresFacture.Count == 0,
        $"NUM-006 CA01 : une serie de la V3 seule (FACV3) donne {v3Facture.Numero ?? v3Facture.Code.ToString()} -> aucune ligne dans la V2, qui n'en a pas " +
        $"de correspondante ({lignesSeriesV3} ligne FACV3/BDFNI) ; num.EcartsV2 reste vide ({string.Join(" ; ", ecartsApresFacture.Take(3))})");
    var distributeur = app.Services.GetRequiredService<DistributeurNumeros>();
    await using (var cn = new SqlConnection(cxV3))
    {
        await cn.OpenAsync();
        var avantTxV3 = await ChronoV2(cxV2, "RC1", 2026);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var n = await distributeur.AttribuerAsync(cn, tx, new DemandeNumero("RC1", 2026), "agent.v3", "POSTE-V3");
        int? vuV2;
        await using (var lire = new SqlCommand($"SELECT MAX(NMMTR_CHRONO) FROM [{v2.Nom}].dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'RC1' " +
                                               "AND NMMTR_ANNEE = 2026 AND NMMTR_ATTRIBUTION_ERRONEE = 0;", cn, tx))
            vuV2 = (int?)await lire.ExecuteScalarAsync();
        await tx.RollbackAsync();
        Exiger(n.Code == 0 && vuV2 == n.Chrono && await ChronoV2(cxV2, "RC1", 2026) == avantTxV3,
            $"NUM-006 CA01 : un acte de la V3 prend {n.Numero} dans sa transaction -> la V2 le voit dans la meme transaction ({vuV2}) ; " +
            $"annulee, la V2 revient a {avantTxV3}");
    }

    // CA03 : un changement de mode fait dans la V3, greffe certifie NF525 -> recopie, evenement dans le journal de la V2.
    var jetAvant = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM dbo.JET_NP WHERE JET_CODE_NP = '10';");
    await Executer(cxV3, "UPDATE num.Compteur SET GestionRendus = 0 WHERE Type = 'RC5' AND Annee = 2026;");
    var jetMode = await Lignes(cxV2, "SELECT JET_INFORMATIONS_NP FROM dbo.JET_NP WHERE JET_CODE_NP = '10';");
    var gestionV2 = await Valeur<bool>(cxV2, "SELECT NMMTR_GESTION_NUM_ERR FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'RC5' AND NMMTR_ANNEE = 2026 AND NMMTR_ATTRIBUTION_ERRONEE = 0;");
    var rc5 = await Generer(agentV2, "RC5", 2026);
    var jetApresChrono = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM dbo.JET_NP WHERE JET_CODE_NP = '10';");
    Exiger(jetAvant == 0 && jetMode.Count == 1 && jetMode[0].Contains("typemetier=RC5") && jetMode[0].Contains("gnum=0") && !gestionV2
           && rc5.Code == 0 && jetApresChrono == 1,
        $"NUM-006 CA03 : restitution de RC5 coupee dans la V3 -> recopiee dans la V2, son declencheur NF525 (TR_NUMERO_METIER_JET) ecrit " +
        $"l'evenement 10 « {jetMode.FirstOrDefault()} » ; l'avancement du chrono ({rc5.Numero}) n'emet rien");
    Reste("NUM-006 CA03 : « la V3 l'ecrit dans le sien » -- aucun programme de la V3 ne change encore le mode d'une serie et sa chaine " +
          "signee est SOC-018 ; le jour ou un ecran le fera, son evenement V3 s'ecrira a cote de la recopie prouvee ici");

    // CA04 : un autre programme que la recopie ecrit dans NUMERO_METIER -> refuse, avec un message qui renvoie a la V3.
    var tableAvantEcriture = await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;");
    var refusEcriture = new List<Erreur?>();
    foreach (var (cx, sql) in new[]
    {
        (agentV2, "INSERT dbo.NUMERO_METIER (NMMTR_TYPE_METIER, NMMTR_STRUCTURE_NUMERO, NMMTR_ANNEE) VALUES ('RGB', 'ALC', 2029);"),
        (agentV2, "UPDATE dbo.NUMERO_METIER SET NMMTR_CHRONO = NMMTR_CHRONO + 1 WHERE NMMTR_TYPE_METIER = 'RGB';"),
        (cxV2, "DELETE dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER = 'FACTU';"),                               // meme db_owner
    })
    {
        try { await Executer(cx, sql); refusEcriture.Add(null); }
        catch (SqlException e) { refusEcriture.Add(new Erreur(e.Number, e.State, e.Class, e.Message)); }
    }
    Exiger(refusEcriture.All(e => e?.Numero == 51006 && e.Message.Contains("compteur de la V3") && e.Message.Contains(v3.Nom))
           && (await Lignes(cxV2, "SELECT * FROM dbo.NUMERO_METIER;")).SequenceEqual(tableAvantEcriture),
        $"NUM-006 CA04 : INSERT, UPDATE et DELETE directs dans NUMERO_METIER (agent, puis proprietaire de la base) refuses (51006), table intacte : " +
        $"« {refusEcriture.FirstOrDefault()?.Message} »");

    // =================================================================================================
    // NUM-007 — Arreter proprement l'ancien logiciel quand le compteur ne repond pas
    // =================================================================================================
    var echecsProvoques = 0;

    // CA04 : la partie serveur arretee, la base V3 en service -> la V2 obtient son numero.
    await app.DisposeAsync();
    app = null;
    SqlConnection.ClearAllPools();
    var sansServeur = await Generer(agentV2, "RGB", 2026);
    Donne(sansServeur.Numero);
    Exiger(sansServeur.Code == 0 && sansServeur.Numero is not null,
        $"NUM-007 CA04 : partie serveur de la V3 arretee, base V3 en service -> la V2 obtient {sansServeur.Numero} : le compteur ne depend que de la base");

    // CA01 : la base V3 hors service, de quatre facons.
    async Task HorsService(string facon, Func<Task<IAsyncDisposable?>> couper, Func<Task> remettre)
    {
        var avant = await Etat();
        var tenue = await couper();
        Appel a;
        try { a = await Generer(agentV2, "RGB", 2026); }
        finally { if (tenue is not null) await tenue.DisposeAsync(); await remettre(); }
        echecsProvoques++;
        Exiger(a.Code == -9 && a.Numero is null && a.Leve?.Message == MessageAttribue && a.Leve.Gravite == 16 && a.Leve.Etat == 1
               && await Etat() == avant,
            $"NUM-007 CA01 : base V3 {facon} -> GENERER_NUMERO leve « {a.Leve?.Message} » et rend {a.Code} ; ni la V2 ni la V3 ne changent");
    }
    await HorsService("arretee (hors ligne)",
        async () => { await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;"); return null; },
        async () => await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET ONLINE;"));
    var dossierSauvegarde = await Valeur<string>(cxMaster, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260));");
    sauvegarde = dossierSauvegarde.TrimEnd('/', '\\') + (dossierSauvegarde.Contains('/') ? "/" : "\\") + v3.Nom + ".bak";
    await HorsService("en restauration",
        async () =>
        {
            await Executer(cxMaster, $"BACKUP DATABASE [{v3.Nom}] TO DISK = N'{sauvegarde}' WITH INIT, COPY_ONLY;");
            await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                                     $"RESTORE DATABASE [{v3.Nom}] FROM DISK = N'{sauvegarde}' WITH REPLACE, NORECOVERY;");
            return null;
        },
        async () => await Executer(cxMaster, $"RESTORE DATABASE [{v3.Nom}] WITH RECOVERY; ALTER DATABASE [{v3.Nom}] SET MULTI_USER;"));
    await HorsService("en utilisateur unique (pris par une autre session)",
        async () =>
        {
            var cn = new SqlConnection(cxV3);
            await cn.OpenAsync();
            await using (var cmd = new SqlCommand($"ALTER DATABASE [{v3.Nom}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;", cn)) await cmd.ExecuteNonQueryAsync();
            return new Tenue(cn, $"ALTER DATABASE [{v3.Nom}] SET MULTI_USER;");
        },
        () => Task.CompletedTask);
    await HorsService("sans droit pour le compte de la V2 (num_demandeur retire)",
        async () => { await Executer(cxV3, $"ALTER ROLE num_demandeur DROP MEMBER [{agent}];"); return null; },
        async () => await Executer(cxV3, $"ALTER ROLE num_demandeur ADD MEMBER [{agent}];"));

    // CA02 : un compteur tenu par une autre transaction au-dela de 5 secondes -> la meme erreur, rien n'est consomme.
    {
        var avant = await Etat();
        await using var tenant = new SqlConnection(cxV3);
        await tenant.OpenAsync();
        await using var tx = (SqlTransaction)await tenant.BeginTransactionAsync();
        await using (var verrou = new SqlCommand("EXEC sp_getapplock @Resource = N'num:RGB:2026:0', @LockMode = 'Exclusive', @LockOwner = 'Transaction';", tenant, tx))
            await verrou.ExecuteNonQueryAsync();
        var chrono = Stopwatch.StartNew();
        var bloque = await Generer(agentV2, "RGB", 2026);
        chrono.Stop();
        await tx.RollbackAsync();
        echecsProvoques++;
        Exiger(bloque.Code == -9 && bloque.Leve?.Message == MessageAttribue && bloque.Leve.Etat == 2
               && chrono.Elapsed.TotalSeconds is >= 4.5 and < 15 && await Etat() == avant,
            $"NUM-007 CA02 : compteur RGB 2026 tenu par une autre transaction -> apres {chrono.Elapsed.TotalSeconds:0.0} s (delai 5 s), la meme erreur " +
            $"(etat {bloque.Leve?.Etat} : compteur occupe) et rien n'est consomme");
    }

    // CA06 : base V3 hors service, la V2 rend un numero -> code non nul, message, rien d'ecrit.
    {
        var avant = await Etat();
        await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
        Appel r;
        try { r = await Restituer(agentV2, "RGB", "2026B01235", 2026, "essai"); }
        finally { await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET ONLINE;"); }
        echecsProvoques++;
        Exiger(r.Code == -9 && Convert.ToInt32(r.Sorties["@MonErreur"]) == -9 && r.Leve?.Message == MessageRendu && await Etat() == avant,
            $"NUM-007 CA06 : base V3 hors ligne, RESTITUER_NUMERO -> @MonErreur {r.Sorties["@MonErreur"]}, « {r.Leve?.Message} », rien d'ecrit");
    }

    // NUM-003 CA05 et NUM-007 CA03 : le second programme et les sept appels de la V2, base V3 hors service.
    {
        await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
        try
        {
            var avant = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.AppelChercher WHERE Code = -9;");
            var c = await Chercher(agentV2, "RGB", 2026);
            echecsProvoques++;
            Exiger(c.Code == -9 && c.Leve?.Message == MessageAttribue && c.Numero is null
                   && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.AppelChercher WHERE Code = -9;") == avant + 1,
                $"NUM-003 CA05 : CHERCHER_NUMERO en erreur -> {c.Code} et « {c.Leve?.Message} », jamais 0 (l'ancien rendait 0) ; l'echec est journalise");

            var gestes7 = new List<string>();
            foreach (var (nom, source, appeler) in appelants)
            {
                string geste;
                try
                {
                    var (n, _) = await appeler(agentV2);
                    geste = string.IsNullOrEmpty(n) ? "rien recu, sans erreur" : $"un numero ({n}) -- FAUX";
                }
                catch (SqlException e) when (e.Message.Contains(MessageAttribue)) { geste = "erreur SQL avec le message"; }
                catch (SqlException e) { geste = $"erreur SQL sans le message : {e.Message}"; }
                echecsProvoques++;
                gestes7.Add($"{nom} : {geste}");
            }
            Exiger(gestes7.All(g => g.EndsWith("erreur SQL avec le message")),
                $"NUM-007 CA03 : les {appelants.Length} appels de la V2 recoivent l'erreur et son message, aucun ne recoit de numero ({string.Join(" ; ", gestes7)})");
        }
        finally { await Executer(cxMaster, $"ALTER DATABASE [{v3.Nom}] SET ONLINE;"); }
    }
    Reste("NUM-007 CA03 : le geste de chaque appelant apres l'erreur (s'arreter ou enregistrer un acte sans numero) est releve dans son code " +
          "(liste dans numerotation-v2.md) ; le rejouer dans les ecrans VB6 et .NET de la V2 et remettre la liste au referent");

    // CA05 : chaque echec venu de la V2 est compte (session d'evenements de l'instance, echecs.sql).
    {
        var compte = 0;
        var attente = Stopwatch.StartNew();
        while (attente.Elapsed.TotalSeconds < 20)
        {
            var sortie = await Sqlcmd.JouerAsync(cxMaster, Path.Combine(dossierScripts, "echecs.sql"), new Dictionary<string, string> { ["SessionEchecs"] = sessionEchecs });
            compte = sortie.Resultats.Single().Where(r => (string?)r[0] == v2.Nom).Select(r => Convert.ToInt32(r[1])).FirstOrDefault();
            if (compte >= echecsProvoques) break;
            await Task.Delay(1000);
        }
        Exiger(compte == echecsProvoques,
            $"NUM-007 CA05 : les {echecsProvoques} echecs de numerotation provoques par ce banc sont comptes pour {v2.Nom} ({compte}), " +
            "meme quand l'appelant annule sa transaction (session d'evenements posee par bascule.sql, lue par echecs.sql)");
    }
    Reste("NUM-007 CA05 : l'alerte vers l'exploitation d'Amitel des le premier echec -- la supervision (SOC-011) n'existe pas encore ; " +
          "elle lira echecs.sql");

    // =================================================================================================
    // NUM-022 — Rendre la numerotation a l'ancien logiciel pendant la beta
    // =================================================================================================

    // CA01, une seule transaction : un retour qui echoue au dernier programme ne laisse rien a moitie rendu.
    var corpsRestituer = await Valeur<string>(cxV2, "SELECT Definition FROM rigv3.CorpsOrigine WHERE Objet = 'RESTITUER_NUMERO';");
    await Executer(cxV2, "UPDATE rigv3.CorpsOrigine SET Definition = N'CREATE PROCEDURE dbo.RESTITUER_NUMERO AS SELEC 1' WHERE Objet = 'RESTITUER_NUMERO';");
    var etatAvantEchec = await Etat();
    var retourCasse = await ScriptRefuse("retour.sql", Variables());
    var pontToujours = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.sql_modules WHERE definition LIKE '%rigv3.%' AND OBJECT_NAME(object_id) IN " +
                                               "('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    var recopieToujours = await Valeur<int>(cxV3, "SELECT COUNT(*) FROM sys.triggers WHERE name IN ('TR_Compteur_RecopieV2', 'TR_NumeroRendu_RecopieV2');");
    var fermeDeja = await Valeur<int>(cxV3, "SELECT COUNT(*) FROM sys.triggers WHERE name LIKE '%RenduALaV2';");
    Exiger(retourCasse?.Message.Contains("SELEC") == true && pontToujours == 3 && recopieToujours == 2 && fermeDeja == 0 && await Etat() == etatAvantEchec
           && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.Pont WHERE RetourLe IS NULL;") == 1,
        $"NUM-022 CA01 : un retour qui echoue sur le 3e programme (« {retourCasse?.Message} ») est annule en entier : les 3 programmes " +
        "demandent toujours a la V3, la recopie est en place, la V3 n'est pas fermee, aucun compteur n'a bouge");
    await using (var cn = new SqlConnection(cxV2))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE rigv3.CorpsOrigine SET Definition = @d WHERE Objet = 'RESTITUER_NUMERO';", cn);
        cmd.Parameters.Add("@d", SqlDbType.NVarChar, -1).Value = corpsRestituer;
        await cmd.ExecuteNonQueryAsync();
    }

    // CA01 : le vrai retour. La V2 recoit le chrono courant et les numeros rendus, puis ses trois programmes.
    var dernierV3 = (await ChronoV3("RGB", 2026))!.Value;
    var rendusV3EnAttente = await Lignes(cxV3, "SELECT Serie, Annee, Chrono FROM num.NumeroRendu;");
    var retour = await Script("retour.sql", Variables());
    var origines = await Lignes(cxV2, "SELECT o.name, CAST(CASE WHEN m.definition = c.Definition THEN 1 ELSE 0 END AS int), m.uses_quoted_identifier, m.uses_ansi_nulls " +
                                      "FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id JOIN rigv3.CorpsOrigine c ON c.Objet = o.name;");
    var droitPublic = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.database_permissions WHERE major_id = OBJECT_ID('dbo.GENERER_NUMERO') " +
                                              "AND grantee_principal_id = DATABASE_PRINCIPAL_ID('public') AND permission_name = 'EXECUTE';");
    // Les tables du pont restent (son histoire), avec leurs contraintes ; ni programme, ni fonction, ni synonyme, ni garde.
    var resteDuPont = await Lignes(cxV2, "SELECT name FROM sys.objects WHERE name = 'TR_NUMERO_METIER_RIGV3' " +
                                         "OR (schema_id = SCHEMA_ID('rigv3') AND type IN ('P', 'FN', 'IF', 'TF', 'SN', 'TR', 'V'));");
    var fermes = await Valeur<int>(cxV3, "SELECT COUNT(*) FROM sys.triggers WHERE name LIKE '%RenduALaV2';");
    var recopieRetiree = await Valeur<int>(cxV3, "SELECT COUNT(*) FROM sys.objects WHERE name IN ('TR_Compteur_RecopieV2', 'TR_NumeroRendu_RecopieV2', 'NumeroMetierV2', 'EcartsV2');");
    Exiger(retour.Messages.Any(m => m.StartsWith("Retour fait")) && origines.Count == 3 && origines.All(o => o.EndsWith("|1|False|True"))
           && droitPublic == 1 && resteDuPont.Count == 0 && fermes == 2 && recopieRetiree == 0,
        $"NUM-022 CA01 : retour.sql remet les 3 programmes d'origine tels quels (texte, QUOTED_IDENTIFIER OFF, ANSI_NULLS ON, " +
        $"GRANT EXECUTE a public), retire le pont de la V2 et la recopie de la V3, ferme la V3 ({string.Join(" ; ", origines)})");
    var chronoV2Revenue = await ChronoV2(cxV2, "RGB", 2026);
    var rendusV2 = await Lignes(cxV2, "SELECT NMMTR_TYPE_METIER, NMMTR_ANNEE, NMMTR_CHRONO FROM dbo.NUMERO_METIER WHERE NMMTR_ATTRIBUTION_ERRONEE = 1;");
    var premierRevenu = await Generer(agentV2, "RGB", 2026);
    var secondRevenu = await Generer(agentV2, "RGB", 2026);
    Donne(premierRevenu.Numero); Donne(secondRevenu.Numero);
    var plage = enUsage.Union(rendus).Order().ToList();
    Exiger(chronoV2Revenue == dernierV3 && rendusV2.SequenceEqual(rendusV3EnAttente) && rendusV3EnAttente.SequenceEqual(["JPC|2026|244"])
           && premierRevenu.Numero == $"2026B{dernierV3 + 1:00000}" && secondRevenu.Numero == $"2026B{dernierV3 + 2:00000}"
           && doublons.Count == 0 && rendus.Count == 0 && plage.SequenceEqual(Enumerable.Range(1235, plage.Count)),
        $"NUM-022 CA01 : la table de la V2 recoit le chrono courant de la V3 (RGB 2026 : {chronoV2Revenue}, V3 : {dernierV3}) et ses numeros rendus " +
        $"({string.Join(", ", rendusV2)}) ; la V2 revenue continue a {premierRevenu.Numero}, {secondRevenu.Numero} ; RGB 2026 de 1235 a {plage.LastOrDefault()} : " +
        $"{plage.Count} numeros donnes par la V2 basculee, la V3 et la V2 revenue, sans trou ni doublon" +
        $"{(doublons.Count > 0 ? " ; doublons : " + string.Join(", ", doublons) : "")}");
    var seriesV3DansV2 = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM dbo.NUMERO_METIER WHERE NMMTR_TYPE_METIER IN ('FACV3', 'BDFNI');");
    Exiger(seriesV3DansV2 == 0 && await Valeur<int>(cxV3, "SELECT COUNT(*) FROM num.Compteur WHERE Type IN ('FACV3', 'BDFNI');") == 2,
        $"NUM-022 CA01 : le retour ne met dans la V2 aucune ligne des series de la V3 seule ({seriesV3DansV2} ligne FACV3/BDFNI ; la V3 les garde) : " +
        "num.Reprendre pourra reprendre cette base de nouveau");

    // CA02 : les numeros donnes par la V3 a ses propres actes restent les leurs ; le compteur de la V2 est au-dela.
    var jpcV2 = await Generer(agentV2, "JPC", 2026);
    Exiger(numerosV3.All(n => n <= dernierV3) && (await ChronoV2(cxV2, "RGB", 2026))!.Value > numerosV3.Max()
           && await ChronoV2(cxV2, "JPC", 2027) == 1 && jpcV2.Numero == "2026RJ0244",
        $"NUM-022 CA02 : les numeros de la V3 (RGB {string.Join(", ", numerosV3)}, JPC 2027 n° 1) sont sous le compteur de la V2 revenue " +
        $"({await ChronoV2(cxV2, "RGB", 2026)}) ; le JPC rendu a la V3 (2026RJ0244) est redonne par la V2 ({jpcV2.Numero})");
    Reste("NUM-022 CA02 : recopier dans la V2 les actes de la V3 avec leurs numeros -- c'est REC-007 (phase de la recopie)");
    string? refusV3 = null;
    try { await Valeur<int>(cxV3, "DECLARE @n varchar(40); EXEC num.Attribuer @Type = 'RC1', @Numero = @n OUTPUT, @Annee = 2026; SELECT 1;"); }
    catch (SqlException e) { refusV3 = $"{e.Number} {e.Message}"; }
    Exiger(refusV3?.StartsWith("51023") == true,
        $"NUM-022 CA01 : apres le retour, la V3 ne donne plus de numero pour ce greffe (doublon evite) : « {refusV3} »");

    // CA03 : apres la fin de la beta, le retour refuse de tourner.
    var refusRebascule = await ScriptRefuse("bascule.sql", Variables(finBetaFuture));
    Exiger(refusRebascule?.Number == 51020 && refusRebascule.Message.Contains("chrono different RGB 2026"),
        $"bascule.sql refusee quand la V2 a numerote seule depuis le retour (V3 en retard) : « {refusRebascule?.Message[..Math.Min(160, refusRebascule.Message.Length)]} »");
    // Pour rebasculer, la V3 doit rattraper la V2 qui a numerote seule. num.Reprendre ne reprend qu'une base V3
    // sans compteur (-10) et heurterait num.OrdreFiche et num.CodeLiaison deja repris ; ce rattrapage n'existe
    // pas encore (docs\reconstruction\numerotation-v2.md, « Ce qui reste »). Le banc le fait a la main, pour
    // atteindre l'etat de NUM-022 CA03 : fermeture levee (message 51023), compteurs de la V2 recopies, puis
    // bascule Reprise=faite (la reprise jouee avant, la bascule verifie que les compteurs concordent).
    await Executer(cxV3, "DROP TRIGGER num.TR_Compteur_RenduALaV2; DROP TRIGGER num.TR_NumeroRendu_RenduALaV2; " +
        "DELETE num.NumeroRendu; DELETE num.Compteur WHERE Type NOT IN ('FACV3', 'BDFNI'); " +
        "INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, Debut, " +
        "Chrono, GestionRendus, Redirection, TableCible, ColonneCible, DerniereAttribution) " +
        "SELECT NMMTR_TYPE_METIER, NMMTR_ANNEE, CASE WHEN CHARINDEX('Q', NMMTR_STRUCTURE_NUMERO) > 0 THEN ISNULL(NMMTR_QUANTIEME, 0) ELSE 0 END, " +
        "NMMTR_LIBELLE, NMMTR_STRUCTURE_NUMERO, NMMTR_CODAGE_ANNEE, NMMTR_LETTRE_CATEGORIE, NMMTR_SEPARATEUR, NMMTR_TAILLE_CHRONO, " +
        "NMMTR_CODE_GREFFE, NMMTR_CHRONO_DEBUT, NULLIF(NMMTR_CHRONO, 0), NMMTR_GESTION_NUM_ERR, NMMTR_FILE_REDIRECTION, " +
        "NMMTR_TABLE_AFFECTEE, NMMTR_ATTRIBUT_AFFECTE, NMMTR_DATE_ATTRIBUTION " +
        $"FROM [{v2.Nom}].dbo.NUMERO_METIER WHERE NMMTR_ATTRIBUTION_ERRONEE = 0;");
    await Script("bascule.sql", Variables(finBetaPassee, reprise: "faite"));
    var refusFini = await ScriptRefuse("retour.sql", Variables());
    await Executer(cxV2, $"UPDATE rigv3.Pont SET FinBeta = '{finBetaFuture}';");
    var finDeBeta = await Script("fin-de-beta.sql", new Dictionary<string, string> { ["BaseV2"] = v2.Nom });
    var refusArchive1 = await ScriptRefuse("retour.sql", Variables());
    await Executer(cxV2, $"UPDATE rigv3.Pont SET FinBeta = '{finBetaFuture}';");     // meme une date repoussee ne rouvre rien
    var refusArchive2 = await ScriptRefuse("retour.sql", Variables());
    var toujoursPontee = await Valeur<int>(cxV2, "SELECT COUNT(*) FROM sys.sql_modules WHERE definition LIKE '%rigv3.%' AND OBJECT_NAME(object_id) IN " +
                                                "('GENERER_NUMERO', 'CHERCHER_NUMERO', 'RESTITUER_NUMERO');");
    Exiger(refusFini?.Number == 51022 && refusFini.Message.Contains("finie")
           && refusArchive1?.Number == 51022 && refusArchive2?.Number == 51022 && refusArchive2.Message.Contains("archivé")
           && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.CorpsOrigineArchive;") == 3
           && await Valeur<int>(cxV2, "SELECT COUNT(*) FROM rigv3.CorpsOrigine;") == 0
           && toujoursPontee == 3 && finDeBeta.Messages.Any(m => m.Contains("archivé")),
        $"NUM-022 CA03 : bete finie -> retour refuse (« {refusFini?.Message} ») ; fin-de-beta.sql archive le corps d'origine -> retour refuse, " +
        $"meme fin de beta repoussee (« {refusArchive2?.Message} ») ; la V2 continue de demander a la V3");
    Reste("NUM-022 T02 / NUM-002 T04 : jouer bascule.sql sur les 40 bases au passage de chacune, fin-de-beta.sql sur les 3 bases beta le jour J -- " +
          "hors poste (pas de droit d'ecriture sur les serveurs des greffes)");
}
catch (Exception e)
{
    // Un script refuse ou une erreur SQL inattendue : un echec lisible, pas un arret sans bilan.
    Exiger(false, $"le banc s'est arrete sur une erreur inattendue : {e.GetType().Name} : {e.Message}");
}
finally
{
    if (app is not null) await app.DisposeAsync();
    SqlConnection.ClearAllPools();
    // La base V3 remise en service quoi qu'il arrive : sinon elle ne se supprime pas.
    try
    {
        await Executer(cxMaster,
            $"IF DATABASEPROPERTYEX(N'{v3.Nom}', 'Status') = 'OFFLINE' ALTER DATABASE [{v3.Nom}] SET ONLINE; " +
            $"IF DATABASEPROPERTYEX(N'{v3.Nom}', 'Status') = 'RESTORING' RESTORE DATABASE [{v3.Nom}] WITH RECOVERY; " +
            $"IF DATABASEPROPERTYEX(N'{v3.Nom}', 'UserAccess') <> 'MULTI_USER' ALTER DATABASE [{v3.Nom}] SET MULTI_USER WITH ROLLBACK IMMEDIATE;");
    }
    catch (SqlException e) { Console.WriteLine("Nettoyage : base V3 non remise en service : " + e.Message); }
    foreach (var sql in new[]
    {
        $"IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'{sessionEchecs}') DROP EVENT SESSION [{sessionEchecs}] ON SERVER;",
        $"IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'{tache}') EXEC msdb.dbo.sp_delete_job @job_name = N'{tache}';",
        $"IF SUSER_ID(N'{agent}') IS NOT NULL DROP LOGIN [{agent}];",
        sauvegarde is null ? "" : $"EXEC master.sys.xp_delete_files N'{sauvegarde}';",
    })
    {
        if (sql.Length == 0) continue;
        try { await Executer(cxMaster, sql); }
        catch (SqlException e) { Console.WriteLine("Nettoyage : " + e.Message); }
    }
    // Rien ne reste sur le serveur du poste : les cinq bases, l'agent, la session d'evenements, la tache.
    foreach (var b in new[] { v3, v2, temoin, d8, commune }) await b.DisposeAsync();
    var restes = await Valeur<int>(cxMaster,
        $"SELECT (SELECT COUNT(*) FROM sys.databases WHERE name IN (N'{v3.Nom}', N'{v2.Nom}', N'{temoin.Nom}', N'{d8.Nom}', N'{commune.Nom}')) " +
        $"+ (SELECT COUNT(*) FROM sys.server_principals WHERE name = N'{agent}') " +
        $"+ (SELECT COUNT(*) FROM sys.server_event_sessions WHERE name = N'{sessionEchecs}') " +
        $"+ (SELECT COUNT(*) FROM msdb.dbo.sysjobs WHERE name = N'{tache}');");
    Exiger(restes == 0, "nettoyage : les 5 bases jetables, le compte SQL de l'agent, la session d'evenements et la tache planifiee sont supprimes du serveur du poste");
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "NumerotationV2Check : tout est VERT."
    : $"NumerotationV2Check : {echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

static string Sql(string? v) => v is null ? "NULL" : "'" + v.Replace("'", "''") + "'";

/// <summary>Un compteur de depart dans NUMERO_METIER (reglages de RIG_DEV, lot 5).</summary>
sealed record Ligne(string Type, int Annee, int? Quantieme, string Structure, byte Codage, string? Lettre, string? Sep, short Taille,
                    int Debut, int Chrono, bool Gestion, string? Redir = null, string? Greffe = null, string? Libelle = null);

/// <summary>Une erreur ou un message rendu a l'appelant.</summary>
sealed record Erreur(int Numero, byte Etat, byte Gravite, string Message);

/// <summary>Ce que voit l'appelant d'un programme V2 : code rendu, sorties, erreurs levees, messages « n lignes ».</summary>
sealed record Appel(int Code, IReadOnlyDictionary<string, object?> Sorties, IReadOnlyList<Erreur> Erreurs, int Lignes)
{
    public string? Numero => Sorties.TryGetValue("@NUMERO_RETOURNE", out var n) ? n as string : null;
    public Erreur? Leve => Erreurs.FirstOrDefault(e => e.Gravite >= 11);
}

/// <summary>Une session qui tient la base V3 en utilisateur unique ; la rend en multi-utilisateur en partant.</summary>
sealed class Tenue(SqlConnection cn, string rendre) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await using (var cmd = new SqlCommand(rendre, cn)) await cmd.ExecuteNonQueryAsync();
        await cn.DisposeAsync();
    }
}
