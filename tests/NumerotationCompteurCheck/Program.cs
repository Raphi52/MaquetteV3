using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Numerotation;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3Desktop.Pont;

// Banc du COMPTEUR CENTRAL DES NUMEROS (lot 5 du plan de reconstruction, 2026-11-20).
// Stories NUM-001 (tenir les compteurs dans la base V3), NUM-004 (rendre un numero pour qu'il soit
// redonne) et NUM-008 (aucun doublon quand la V2 et la V3 numerotent ensemble) : un controle par
// critere, qui cite son id.
//
// Sur une base jetable ou TOUTES les migrations du depot sont jouees :
//  - les compteurs de depart sont ceux de RIG_DEV (lus le 2026-11-20 sur SQL-DEV\DEV, en lecture
//    seule) ; la reprise reelle de NUMERO_METIER est la story NUM-005 (lot 6) ;
//  - une session « V3 » passe par le vrai serveur, demarre en memoire (routes, ou le service
//    DistributeurNumeros dans la transaction d'un acte) ;
//  - une session « V2 » est une connexion SQL qui appelle num.Attribuer / num.Restituer avec
//    @Logiciel = 'V2', exactement ce que feront GENERER_NUMERO et RESTITUER_NUMERO remplaces (lot 7).
//
// Raccord des lots 2, 4 et 5 (lot 8, 2026-11-21) : la base ci-dessous est celle du GREFFE A. Le serveur
// lit le catalogue dans une base commune qui n'a aucun compte d'agent, et y trouve deux greffes, A et B,
// chacun avec sa base jetable. Les routes portent le greffe (RoutesCompteur.Pour) et exigent la session :
// l'agent du banc (en-tete de developpement X-RigV3-Compte) n'est connu que de la base du greffe A.
//
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la racine du depot.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
const int DelaiServeurMs = 3000;          // attente d'un compteur tenu, cote serveur (NUM-008 CA05)
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var rapport = new Dictionary<string, object>();

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
// Ce qu'aucun banc de ce poste ne peut faire : dit, pas compte vert (docs/reconstruction/numerotation-compteur.md).
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);

await using var b = await BaseJetable.CreerAsync("NumerotationCompteurCheck");
var migrations = await Migrateur.AppliquerAsync(b.Connexion, Path.Combine(racine, "Base", "migrations"));
Exiger(migrations.Jouees.Contains("NUM-001_compteurs"),
    $"migration NUM-001_compteurs jouee sur une base neuve, avec les autres du depot ({string.Join(", ", migrations.Jouees)})");
await using var commune = await BaseJetable.CreerAsync("NumCompteurCommune");
await using var baseB = await BaseJetable.CreerAsync("NumCompteurGreffeB");
await Migrateur.AppliquerAsync(commune.Connexion, Path.Combine(racine, "Base", "migrations"));
await Migrateur.AppliquerAsync(baseB.Connexion, Path.Combine(racine, "Base", "migrations"));
const string GreffeA = "9101", GreffeB = "9102", GreffeMuet = "9103", GreffeAbsent = "9199";
const string AgentA = "banc.numeros", RefuseA = "banc.refuse", AgentB = "banc.greffeb";
const string SansXnumeroA = "banc.lecture";   // admis dans A, sans la fonction XNUMERO (NUM-004 T08)

async Task<SqlConnection> Ouvrir()
{
    var cn = new SqlConnection(b.Connexion);
    await cn.OpenAsync();
    return cn;
}
async Task<T> Valeur<T>(string sql, params (string Nom, object? Valeur)[] p)
    => await ValeurSur<T>(b.Connexion, sql, p);
async Task<T> ValeurSur<T>(string connexion, string sql, params (string Nom, object? Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
    var v = await cmd.ExecuteScalarAsync();
    return v is null or DBNull ? default! : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
}
async Task Executer(string sql) => await ExecuterSur(b.Connexion, sql);
async Task ExecuterSur(string connexion, string sql, params (string Nom, object Valeur)[] p)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (nom, valeur) in p) cmd.Parameters.AddWithValue(nom, valeur);
    await cmd.ExecuteNonQueryAsync();
}
async Task<int?> Dernier(string type, int annee, int jour = 0)
    => await Valeur<int?>("SELECT Chrono FROM num.Compteur WHERE Type = @t AND Annee = @a AND Jour = @j;",
                          ("@t", type), ("@a", annee), ("@j", jour));
async Task<int> Rendus(string serie, int annee, int jour = 0)
    => await Valeur<int>("SELECT COUNT(*) FROM num.NumeroRendu WHERE Serie = @s AND Annee = @a AND Jour = @j;",
                         ("@s", serie), ("@a", annee), ("@j", jour));
async Task<string> Etat() => await EtatSur(b.Connexion);
async Task<string> EtatSur(string connexion)
    => await ValeurSur<string>(connexion, "SELECT CONCAT((SELECT COUNT(*) FROM num.Compteur), '/', (SELECT COUNT(*) FROM num.NumeroRendu), '/', " +
                            "(SELECT COUNT_BIG(*) FROM num.Journal), '/', (SELECT CHECKSUM_AGG(CHECKSUM(Type, Annee, Jour, Chrono)) FROM num.Compteur));");

// --- Les sessions « V2 » : les programmes, appeles en SQL comme le feront ceux de la V2 remplaces ----
async Task<NumeroAttribue> V2Attribuer(SqlConnection cn, SqlTransaction? tx, string type, int annee = 0, int jour = 0,
                                       int delaiMs = 10000)
{
    await using var cmd = new SqlCommand("num.Attribuer", cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
    cmd.Parameters.Add("@Type", SqlDbType.VarChar, 5).Value = type;
    cmd.Parameters.Add("@Annee", SqlDbType.SmallInt).Value = annee;
    cmd.Parameters.Add("@Jour", SqlDbType.SmallInt).Value = jour;
    cmd.Parameters.Add("@Logiciel", SqlDbType.Char, 2).Value = "V2";
    cmd.Parameters.Add("@Compte", SqlDbType.NVarChar, 128).Value = "agent.v2";
    cmd.Parameters.Add("@Poste", SqlDbType.NVarChar, 128).Value = "POSTE-V2";
    cmd.Parameters.Add("@DelaiVerrouMs", SqlDbType.Int).Value = delaiMs;
    var numero = cmd.Parameters.Add("@Numero", SqlDbType.VarChar, 40);
    var serie = cmd.Parameters.Add("@Serie", SqlDbType.VarChar, 5);
    var chrono = cmd.Parameters.Add("@Chrono", SqlDbType.Int);
    var reattribue = cmd.Parameters.Add("@Reattribue", SqlDbType.Bit);
    var message = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 400);
    foreach (var p in new[] { numero, serie, chrono, reattribue, message }) p.Direction = ParameterDirection.Output;
    var retour = cmd.Parameters.Add("@r", SqlDbType.Int);
    retour.Direction = ParameterDirection.ReturnValue;
    await cmd.ExecuteNonQueryAsync();
    return new NumeroAttribue((int)retour.Value, serie.Value as string, numero.Value as string, annee, jour,
                              chrono.Value as int?, reattribue.Value is true, message.Value as string);
}
async Task<NumeroAttribue> V2(string type, int annee = 0, int jour = 0)
{
    await using var cn = await Ouvrir();
    return await V2Attribuer(cn, null, type, annee, jour);
}
async Task<ResultatRestitution> V2Restituer(SqlConnection? cnDonnee, SqlTransaction? tx, string type, string numero,
                                            string? explication, int annee = 0, int delaiMs = 10000)
{
    var cn = cnDonnee ?? await Ouvrir();
    try
    {
        await using var cmd = new SqlCommand("num.Restituer", cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 120 };
        cmd.Parameters.Add("@Type", SqlDbType.VarChar, 5).Value = type;
        cmd.Parameters.Add("@Numero", SqlDbType.VarChar, 40).Value = numero;
        cmd.Parameters.Add("@Annee", SqlDbType.SmallInt).Value = annee;
        cmd.Parameters.Add("@Explication", SqlDbType.NVarChar, -1).Value = (object?)explication ?? DBNull.Value;
        cmd.Parameters.Add("@Logiciel", SqlDbType.Char, 2).Value = "V2";
        cmd.Parameters.Add("@Compte", SqlDbType.NVarChar, 128).Value = "agent.v2";
        cmd.Parameters.Add("@Poste", SqlDbType.NVarChar, 128).Value = "POSTE-V2";
        cmd.Parameters.Add("@DelaiVerrouMs", SqlDbType.Int).Value = delaiMs;
        var serie = cmd.Parameters.Add("@Serie", SqlDbType.VarChar, 5);
        var issue = cmd.Parameters.Add("@Issue", SqlDbType.VarChar, 20);
        var message = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 400);
        foreach (var p in new[] { serie, issue, message }) p.Direction = ParameterDirection.Output;
        var retour = cmd.Parameters.Add("@r", SqlDbType.Int);
        retour.Direction = ParameterDirection.ReturnValue;
        await cmd.ExecuteNonQueryAsync();
        return new ResultatRestitution((int)retour.Value, issue.Value as string, serie.Value as string, message.Value as string);
    }
    finally
    {
        if (cnDonnee is null) await cn.DisposeAsync();
    }
}

// --- Les compteurs de depart : RIG_DEV.dbo.NUMERO_METIER, lu le 2026-11-20 ------------------------
var anneeCourante = DateTime.Now.Year;
var depart = new List<Fixture>
{
    new("RGB", 2025, 0, "ALC", 4, "B", null, 5, 1, 3229, true, Libelle: "RCS Numéro de gestion du dossier RCS, catégorie B"),
    new("RGB", 2026, 0, "ALC", 4, "B", null, 5, 1, 890, true, Libelle: "RCS Numéro de gestion du dossier RCS, catégorie B"),
    new("JPC", 2025, 0, "ALC", 4, "RJ", null, 4, 1, 777, true, Table: "PROCEDURE_COLLECTIVE", Colonne: "NUM_PC"),
    new("JPC", 2026, 0, "ALC", 4, "RJ", null, 4, 1, 242, true, Table: "PROCEDURE_COLLECTIVE", Colonne: "NUM_PC"),
    new("JGDJ", 2026, 58, "AQC", 2, null, null, 5, 1, 1, true, Greffe: "3801", Table: "DECISION_JUDICIAIRE", Colonne: "NUM_REPERTOIRE"),
    new("ARRD", 2026, 324, "LAQC", 2, "A", "", 5, 1, 115, true, Greffe: "9999"),
    new("ARRC", 2001, 1, "LAQC", 2, "C", null, 4, 1, null, true, Redir: "ARRD"),
    new("RC1", 2024, 0, "LASC", 2, "F", "/", 6, 1, 100, true),
    new("RC5", 2024, 0, "LASC", 2, "F", "/", 6, 1, 100, true),
    new("RC1", 2026, 0, "LASC", 2, "F", "/", 6, 1, 9935, true),
    new("RC5", 2026, 0, "LASC", 2, "F", "/", 6, 1, 3, true),
    new("RC6", 2026, 0, "LASC", 2, "F", "/", 6, 1, 179, true),
    // Les 10 series a restitution inactive de NUM-004 CA03.
    new("FACTU", 2026, 0, "A-C", 2, null, null, 6, 1, 22768, false),
    new("DMD", 2026, 324, "LAQC", 2, "D", "", 5, 1, 122, false, Greffe: "9999"),
    new("RAP", 2026, 0, "ALC", 2, "AP", null, 5, 1, 43, false, Greffe: "3801"),
    new("ARRHF", 2001, 0, "LAC", 2, "HF", "", 7, 1, null, false, Greffe: "9999"),
    new("ARRNQ", 2001, 0, "LAC", 2, "NQ", "", 7, 1, null, false, Greffe: "9999"),
    new("RA1", 2188, 0, "ALC", 2, "r", "", 6, 1, 1, false),
    new("EAR", 2026, 0, "ALC", 2, "e", "", 7, 1, 1985, false),
    new("LIAI", 2026, 0, "LGACK", 1, "G", null, 5, 1, 1782, false, Greffe: "3802"),
    new("SIREN", 2000, 0, "C", 0, null, null, 9, 0, null, false),
    new("HF", 2001, 0, "LAC", 1, "HF", null, 7, 1, null, false, Greffe: "9999"),
    // Une serie dont le format ne porte pas l'annee (NUM-004 CA07), l'annee en cours et la precedente.
    new("LTIA", (short)(anneeCourante - 1), 0, "C", 4, "ACT", "/", 5, 1, 2, true),
    new("LTIA", (short)anneeCourante, 0, "C", 4, "ACT", "/", 5, 1, 2, true),
};
await using (var cn = await Ouvrir())
{
    foreach (var f in depart)
    {
        await using var cmd = new SqlCommand(
            "INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, " +
            "Debut, Chrono, GestionRendus, Redirection, TableCible, ColonneCible) VALUES (@t, @a, @j, @lib, @s, @ca, @l, @sep, @tc, @g, " +
            "@d, @c, @gr, @r, @ta, @co);", cn);
        cmd.Parameters.AddWithValue("@t", f.Type);
        cmd.Parameters.AddWithValue("@a", f.Annee);
        cmd.Parameters.AddWithValue("@j", f.Jour);
        cmd.Parameters.AddWithValue("@lib", (object?)f.Libelle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@s", f.Structure);
        cmd.Parameters.AddWithValue("@ca", f.Codage);
        cmd.Parameters.AddWithValue("@l", (object?)f.Lettre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sep", (object?)f.Sep ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tc", f.Taille);
        cmd.Parameters.AddWithValue("@g", (object?)f.Greffe ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", f.Debut);
        cmd.Parameters.AddWithValue("@c", (object?)f.Chrono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gr", f.Gestion);
        cmd.Parameters.AddWithValue("@r", (object?)f.Redir ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ta", (object?)f.Table ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@co", (object?)f.Colonne ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

// --- Le greffe B (sa base, ses compteurs, son agent), le greffe A (ses agents), le catalogue commun -----
await ExecuterSur(baseB.Connexion,
    "INSERT num.Compteur (Type, Annee, Jour, Libelle, Structure, CodageAnnee, Lettre, Separateur, TailleChrono, CodeGreffe, Debut, Chrono, GestionRendus) " +
    "VALUES ('RGB', 2026, 0, NULL, 'ALC', 4, 'B', NULL, 5, NULL, 1, 4242, 1), ('JPC', 2026, 0, NULL, 'ALC', 4, 'RJ', NULL, 4, NULL, 1, 10, 1);");
const string ComptesDuGreffe = """
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'NUM', N'Numerotation');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc NumerotationCompteur');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT v.Compte, 'BANC', 'Numeros', LEFT(v.Compte, 16), 'M.', @greffe, s.Id, v.Desactive
    FROM (VALUES (@admis, 0), (@refuse, 1)) v (Compte, Desactive) CROSS JOIN soc.Service s WHERE v.Compte <> '';
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
    -- La reattribution est reservee a XNUMERO (procedure de la V2, RoutesCompteur.FonctionReattribution).
    INSERT INTO soc.Fonction (Code, Libelle, TypeV2) VALUES ('XNUMERO', 'Reattribution de numero metier', 'PROCVB6');
    INSERT INTO soc.RoleFonction (Role, Fonction) VALUES ('BANC', 'XNUMERO');
    """;
await ExecuterSur(b.Connexion, ComptesDuGreffe, ("@greffe", GreffeA), ("@admis", AgentA), ("@refuse", RefuseA));
await ExecuterSur(baseB.Connexion, ComptesDuGreffe, ("@greffe", GreffeB), ("@admis", AgentB), ("@refuse", ""));
await ExecuterSur(b.Connexion, """
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC_LECTURE', 'Banc : admis sans XNUMERO');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
    SELECT @compte, 'BANC', 'Lecture', LEFT(@compte, 16), 'M.', @greffe, MIN(Id), 0 FROM soc.Service;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC_LECTURE' FROM soc.Compte WHERE CompteWindows = @compte;
    """, ("@greffe", GreffeA), ("@compte", SansXnumeroA));
var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
var instance = serveurBancs.DataSource;                       // « 127.0.0.1,14330 »
var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
authentification.Remove("Data Source");                       // le serveur et la base viennent du catalogue
var baseMuette = $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}";   // jamais creee
foreach (var (code, nomBase) in new[] { (GreffeA, b.Nom), (GreffeB, baseB.Nom), (GreffeMuet, baseMuette) })
    await ExecuterSur(commune.Connexion,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        "VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());",
        ("@c", code), ("@n", $"Greffe {code} du banc"), ("@s", instance), ("@v2", "RIG_BANC_" + code), ("@v3", nomBase));

// --- Le vrai serveur, en memoire, sur la base commune (catalogue) ; environnement de developpement : -----
// --- l'agent se designe par l'en-tete X-RigV3-Compte, que la production ignore (SOC-001 CA07) ----------
await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    s.Configuration[DistributeurNumeros.Reglage] = DelaiServeurMs.ToString();
});
await app.StartAsync();
var http = app.GetTestClient();
http.Timeout = TimeSpan.FromSeconds(120);
http.DefaultRequestHeaders.Add(RoutesIdentite.EnTeteCompte, AgentA);   // l'agent du greffe A, pour tout le banc
var sansEnTete = app.GetTestClient();                                   // chaque appel y dit son compte (ou aucun)
var distributeur = app.Services.GetRequiredService<DistributeurNumeros>();
var compteursA = RoutesCompteur.Pour(RoutesCompteur.Compteurs, GreffeA);
var numerosA = RoutesCompteur.Pour(RoutesCompteur.Numeros, GreffeA);
var restitutionsA = RoutesCompteur.Pour(RoutesCompteur.Restitutions, GreffeA);

async Task<NumeroAttribue> V3(string type, int annee = 0, int jour = 0)
{
    using var r = await http.PostAsJsonAsync(numerosA, new DemandeNumero(type, annee, jour), json);
    return (await r.Content.ReadFromJsonAsync<NumeroAttribue>(json))!;
}
async Task<(HttpStatusCode Statut, ResultatRestitution Resultat)> V3Restituer(string type, string numero, string? explication, int annee = 0)
{
    using var r = await http.PostAsJsonAsync(restitutionsA, new DemandeRestitution(type, numero, explication, annee), json);
    return (r.StatusCode, (await r.Content.ReadFromJsonAsync<ResultatRestitution>(json))!);
}

// =====================================================================================================
// NUM-001 — Tenir tous les compteurs de numeros dans la base de la V3
// =====================================================================================================

// CA01 : chaque ligne a son equivalent (type, annee, jour, chrono, format, reglage, redirection, cible)...
var liste = await http.GetFromJsonAsync<CompteurNumeros[]>(compteursA, json) ?? [];
var manquants = depart.Where(f => !liste.Any(c =>
        c.Type == f.Type && c.Annee == f.Annee && c.Jour == f.Jour && c.Chrono == f.Chrono && c.Debut == f.Debut
        && c.Structure == f.Structure && c.CodageAnnee == f.Codage && c.Lettre == f.Lettre && c.Separateur == f.Sep
        && c.TailleChrono == f.Taille && c.GestionRendus == f.Gestion && c.Redirection == f.Redir && c.CodeGreffe == f.Greffe
        && c.TableCible == f.Table && c.ColonneCible == f.Colonne && c.Libelle == f.Libelle && c.NumerosRendus == 0))
    .Select(f => $"{f.Type} {f.Annee}").ToList();
Exiger(liste.Length == depart.Count && manquants.Count == 0,
    $"NUM-001 CA01 : GET {RoutesCompteur.Compteurs} rend chaque compteur avec type, annee, jour, chrono, format, reglage de restitution, " +
    $"redirection et cible ({liste.Length}/{depart.Count}{(manquants.Count > 0 ? " ; differents : " + string.Join(", ", manquants) : "")})");
var unType = await http.GetFromJsonAsync<CompteurNumeros[]>(compteursA + "?type=JPC", json) ?? [];
Exiger(unType.Length == 2 && unType.All(c => c.Type == "JPC"), $"NUM-001 CA01 : ?type=JPC ne rend que les compteurs JPC ({unType.Length})");

// ... et seul le programme d'attribution peut les modifier (droits SQL).
await Executer("CREATE USER banc_demandeur WITHOUT LOGIN; ALTER ROLE num_demandeur ADD MEMBER banc_demandeur; " +
               "ALTER ROLE db_datawriter ADD MEMBER banc_demandeur; ALTER ROLE db_datareader ADD MEMBER banc_demandeur;");
async Task<int> CommeDemandeur(string sql)
    => await Valeur<int>("DECLARE @e int = 0; EXECUTE AS USER = N'banc_demandeur'; " +
                         $"BEGIN TRY {sql}; END TRY BEGIN CATCH SET @e = ERROR_NUMBER(); END CATCH; REVERT; SELECT @e;");
var refusUpdate = await CommeDemandeur("UPDATE num.Compteur SET Chrono = Chrono + 1 WHERE Type = 'RC6'");
var refusInsert = await CommeDemandeur("INSERT num.NumeroRendu (Serie, Annee, Jour, Chrono, Numero, Logiciel) VALUES ('RC6', 2026, 0, 1, 'x', 'V2')");
var refusDelete = await CommeDemandeur("DELETE FROM num.Journal");
Exiger(refusUpdate == 229 && refusInsert == 229 && refusDelete == 229,
    $"NUM-001 CA01 : un logiciel demandeur (num_demandeur, meme db_datawriter) ne peut ni modifier un compteur, ni ajouter un numero rendu, " +
    $"ni effacer le journal (erreurs {refusUpdate}, {refusInsert}, {refusDelete} ; 229 = permission refusee)");
var accepteExec = await CommeDemandeur(
    "DECLARE @rc int, @n varchar(40); EXEC @rc = num.Attribuer @Type = 'RC6', @Annee = 2026, @Numero = @n OUTPUT, @Logiciel = 'V2'; " +
    "IF @rc <> 0 OR @n IS NULL SET @e = 1000 - @rc; DECLARE @k int; SELECT @k = COUNT(*) FROM num.Compteur");
Exiger(accepteExec == 0 && await Dernier("RC6", 2026) == 180,
    $"NUM-001 CA01 (jumeau) : le meme compte obtient un numero par num.Attribuer et lit les compteurs ({accepteExec}, RC6 2026 a {await Dernier("RC6", 2026)})");

// CA05 : un type inconnu rend -2 comme la V2, et rien n'est ecrit.
var avantInconnu = await Etat();
var inconnuV2 = await V2("ZZZZZ", 2026);
var inconnuV3 = await V3("ZZZZZ", 2026);
Exiger(inconnuV2.Code == CodesNumerotation.TypeInconnu && inconnuV3.Code == CodesNumerotation.TypeInconnu
       && inconnuV2.Numero is null && await Etat() == avantInconnu,
    $"NUM-001 CA05 : type inconnu -> -2 (V2 et V3), aucun numero, rien d'ecrit ({inconnuV2.Code}, {inconnuV3.Code}, « {inconnuV3.Message} »)");
var connu = await V2("RC6", 2026);
Exiger(connu.Code == 0 && connu.Numero == "F26/000181", $"NUM-001 CA05 (jumeau) : un type connu rend 0 et son numero ({connu.Code}, {connu.Numero})");
var redirige = await V3("ARRC", 2026, 324);
Exiger(redirige.Code == 0 && redirige.Serie == "ARRD" && redirige.Numero == "A2632400116",
    $"NUM-001 CA05 : un type redirige (ARRC -> ARRD, GENERER_NUMERO.sql:45-49) prend le numero de sa cible ({redirige.Serie}, {redirige.Numero})");

// CA02 : V2 et V3 au meme instant -> deux numeros differents, le compteur avance de deux.
var avantPaires = (await Dernier("RC6", 2026))!.Value;
var pairesFausses = 0;
for (var tour = 0; tour < 100; tour++)
{
    var top = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var cnV2 = await Ouvrir();
    var tV2 = Task.Run(async () => { await top.Task; return await V2Attribuer(cnV2, null, "RC6", 2026); });
    var tV3 = Task.Run(async () => { await top.Task; return await V3("RC6", 2026); });
    top.SetResult();
    var (a, c) = (await tV2, await tV3);
    if (a.Code != 0 || c.Code != 0 || a.Chrono == c.Chrono || a.Numero == c.Numero) pairesFausses++;
}
var apresPaires = (await Dernier("RC6", 2026))!.Value;
var cleUnique = await Valeur<int>(
    "SELECT COUNT(*) FROM sys.indexes i WHERE i.object_id = OBJECT_ID('num.Compteur') AND i.is_unique = 1 AND " +
    "(SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c " +
    " ON c.object_id = ic.object_id AND c.column_id = ic.column_id WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id " +
    " AND ic.key_ordinal > 0) = 'Type,Annee,Jour';");
Exiger(pairesFausses == 0 && apresPaires - avantPaires == 200 && cleUnique == 1,
    $"NUM-001 CA02 : 100 fois une demande V2 et une V3 au meme instant -> toujours deux numeros differents, le compteur avance de deux " +
    $"(RC6 {avantPaires} -> {apresPaires}, {pairesFausses} paire(s) fausse(s)) ; cle UNIQUE (Type, Annee, Jour) : {cleUnique}");

// CA03 : une attribution dans une transaction annulee ne consomme rien.
var avantAnnul = await Etat();
await using (var cn = await Ouvrir())
{
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    var dansTx = await V2Attribuer(cn, tx, "JPC", 2025);
    var parV3 = await distributeur.AttribuerAsync(cn, tx, new DemandeNumero("JPC", 2025), "agent.v3", "POSTE-V3");
    await tx.RollbackAsync();
    Exiger(dansTx.Numero == "2025RJ0778" && parV3.Numero == "2025RJ0779" && await Etat() == avantAnnul && await Dernier("JPC", 2025) == 777,
        $"NUM-001 CA03 : deux numeros pris dans une transaction annulee (V2 {dansTx.Numero}, V3 {parV3.Numero}) -> compteur revenu a 777, journal inchange");
}
var apresAnnul = await V3("JPC", 2025);
Exiger(apresAnnul.Numero == "2025RJ0778", $"NUM-001 CA03 : le numero suivant est celui qui avait ete annule ({apresAnnul.Numero})");
var rendu500 = await V2Restituer(null, null, "JPC", "2025RJ0500", "essai d'annulation");
await using (var cn = await Ouvrir())
{
    await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
    var reprisDansTx = await V2Attribuer(cn, tx, "JPC", 2025);
    await tx.RollbackAsync();
    Exiger(rendu500.Code == 0 && reprisDansTx.Reattribue && reprisDansTx.Numero == "2025RJ0500" && await Rendus("JPC", 2025) == 1,
        $"NUM-001 CA03 : un numero rendu redonne dans une transaction annulee redevient a redonner ({reprisDansTx.Numero}, rendus : {await Rendus("JPC", 2025)})");
}
var redonne500 = await V2("JPC", 2025);
Exiger(redonne500.Numero == "2025RJ0500" && redonne500.Reattribue && await Rendus("JPC", 2025) == 0,
    $"NUM-001 CA03 : ... et il est redonne a la demande suivante ({redonne500.Numero})");

// CA04 : annee (ou jour) sans compteur, demandee plusieurs fois au meme instant -> un seul compteur,
// avec les reglages de la derniere annee connue, qui commence a sa valeur de depart.
async Task<List<NumeroAttribue>> EnMemeTemps(int nombre, Func<int, Task<NumeroAttribue>> demande)
{
    var top = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var taches = Enumerable.Range(0, nombre).Select(i => Task.Run(async () => { await top.Task; return await demande(i); })).ToList();
    top.SetResult();
    return [.. await Task.WhenAll(taches)];
}
var annee2027 = await EnMemeTemps(12, i => i % 2 == 0 ? V2("RGB", 2027) : V3("RGB", 2027));
var compteurs2027 = await Valeur<int>("SELECT COUNT(*) FROM num.Compteur WHERE Type = 'RGB' AND Annee = 2027;");
var reglages2027 = (await http.GetFromJsonAsync<CompteurNumeros[]>(compteursA + "?type=RGB", json))!;
var r2026 = reglages2027.Single(c => c.Annee == 2026);
var r2027 = reglages2027.Single(c => c.Annee == 2027);
Exiger(compteurs2027 == 1 && annee2027.All(n => n.Code == 0)
       && annee2027.Select(n => n.Chrono!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 12))
       && r2027 with { Annee = 2026, Chrono = r2026.Chrono, DerniereAttribution = null } == r2026 with { DerniereAttribution = null },
    $"NUM-001 CA04 : 12 demandes RGB 2027 au meme instant (sans compteur) -> {compteurs2027} compteur cree, reglages de 2026, numeros 1 a 12 " +
    $"({string.Join(" ", annee2027.Select(n => n.Numero).Order().Take(3))}...)");
var jour200 = await EnMemeTemps(6, i => i % 2 == 0 ? V2("JGDJ", 2026, 200) : V3("JGDJ", 2026, 200));
var compteursJour = await Valeur<int>("SELECT COUNT(*) FROM num.Compteur WHERE Type = 'JGDJ' AND Annee = 2026 AND Jour = 200;");
Exiger(compteursJour == 1 && jour200.Select(n => n.Chrono!.Value).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 6))
       && jour200.Any(n => n.Numero == "2620000001"),
    $"NUM-001 CA04 : 6 demandes JGDJ du jour 200 au meme instant -> {compteursJour} compteur du jour, numeros 1 a 6 (2620000001...)");

// CA07 : deux types qui composent la meme forme : un numero est identifie par sa serie ET sa valeur.
var rc1 = await V2("RC1", 2024);
var rc5 = await V3("RC5", 2024);
var rendRc5 = await V2Restituer(null, null, "RC5", rc5.Numero!, "essai de forme partagee");
var rc1Suivant = await V2("RC1", 2024);
var rc5Suivant = await V3("RC5", 2024);
var journalForme = await Valeur<int>("SELECT COUNT(DISTINCT Serie) FROM num.Journal WHERE Numero = 'F24/000101' AND Evenement = 'attribution';");
Exiger(rc1.Numero == "F24/000101" && rc5.Numero == rc1.Numero && rendRc5.Code == 0 && rc1Suivant.Numero == "F24/000102"
       && rc5Suivant.Numero == "F24/000101" && rc5Suivant.Reattribue && journalForme == 2,
    $"NUM-001 CA07 : RC1 et RC5 donnent tous deux {rc1.Numero} ; le rendre en RC5 ne touche pas RC1 (RC1 suivant {rc1Suivant.Numero}, " +
    $"RC5 suivant {rc5Suivant.Numero}) ; le journal les distingue par serie ({journalForme} series)");

// =====================================================================================================
// NUM-004 — Rendre un numero donne par erreur pour qu'il soit redonne
// =====================================================================================================

// CA01 : 2026RJ0012 (JPC, restitution active) rendu -> compteur intact, ligne « attribution erronee »
// avec l'explication, et la prochaine demande, V2 ou V3, le recoit. (Le carnet ecrit « 2026RJ00012 » :
// le format de JPC dans RIG_DEV a 4 chiffres, le numero reel est 2026RJ0012.)
var rendu12 = await V2Restituer(null, null, "JPC", "2026RJ0012", "procédure supprimée");
var ligne12 = await Valeur<string>("SELECT Explication FROM num.NumeroRendu WHERE Serie = 'JPC' AND Annee = 2026 AND Chrono = 12;");
var compteurApres12 = await Dernier("JPC", 2026);
var redonneV2 = await V2("JPC", 2026);
var rendu12V3 = await V3Restituer("JPC", "2026RJ0012", "procédure supprimée (V3)");
var redonneV3 = await V3("JPC", 2026);
var suivant243 = await V2("JPC", 2026);
Exiger(rendu12.Code == 0 && rendu12.Issue == IssuesRestitution.Rendu && ligne12 == "procédure supprimée" && compteurApres12 == 242
       && redonneV2.Numero == "2026RJ0012" && redonneV2.Reattribue && rendu12V3.Resultat.Code == 0
       && redonneV3.Numero == "2026RJ0012" && redonneV3.Reattribue && suivant243.Numero == "2026RJ0243",
    $"NUM-004 CA01 : 2026RJ0012 rendu (« {ligne12} ») -> compteur toujours a {compteurApres12} ; redonne a la V2 ({redonneV2.Numero}), " +
    $"rendu encore et redonne a la V3 ({redonneV3.Numero}) ; puis {suivant243.Numero}");

// CA02 : le plus petit d'abord, chacun une seule fois.
foreach (var n in new[] { "2026RJ0100", "2026RJ0050", "2026RJ0075" }) await V2Restituer(null, null, "JPC", n, "annulation groupée");
var ordre = new List<string?> { (await V2("JPC", 2026)).Numero, (await V3("JPC", 2026)).Numero, (await V2("JPC", 2026)).Numero, (await V3("JPC", 2026)).Numero };
Exiger(ordre.SequenceEqual(["2026RJ0050", "2026RJ0075", "2026RJ0100", "2026RJ0244"]) && await Rendus("JPC", 2026) == 0,
    $"NUM-004 CA02 : 0100, 0050 et 0075 rendus -> redonnes 0050, 0075, 0100, une fois chacun, puis le suivant ({string.Join(", ", ordre)})");

// CA03 : series a restitution inactive : pas redonne, comme la V2, mais la demande est journalisee.
var inactives = new (string Type, int Annee, int Jour, string Attendu, int AnneeRendu)[]
{
    ("FACTU", 2026, 0, "26-022769", 0), ("DMD", 2026, 324, "D2632400123", 0), ("RAP", 2026, 0, "26AP00044", 0),
    ("ARRHF", 2026, 0, "HF260000001", 0), ("ARRNQ", 2026, 0, "NQ260000001", 0), ("RA1", 2026, 0, "26r000001", 0),
    ("EAR", 2026, 0, "26e0001986", 0), ("LIAI", 2026, 0, "G3802601783*", 0), ("SIREN", 2000, 0, "000000000", 2000),
    ("HF", 2026, 0, "HF60000001", 0),
};
var inactivesFausses = new List<string>();
foreach (var s in inactives)
{
    var donne = await V2(s.Type, s.Annee, s.Jour);
    var explication = $"essai {s.Type} : acte annulé";
    var rendu = await V2Restituer(null, null, s.Type, donne.Numero ?? "", explication, s.AnneeRendu);
    var journalise = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = @s AND Numero = @n AND Evenement = 'restitution non redonnable' " +
                                       "AND Explication = @e AND Code = 0;", ("@s", s.Type), ("@n", donne.Numero), ("@e", explication));
    var suivant = await V2(s.Type, s.Annee, s.Jour);
    if (donne.Numero != s.Attendu || rendu.Code != 0 || rendu.Issue != IssuesRestitution.NonRedonnable || journalise != 1
        || await Rendus(s.Type, s.Annee, s.Jour) != 0 || suivant.Reattribue || suivant.Chrono != donne.Chrono + 1)
        inactivesFausses.Add($"{s.Type} ({donne.Numero}, code {rendu.Code}, {rendu.Issue}, journal {journalise}, suivant {suivant.Numero})");
}
Exiger(inactivesFausses.Count == 0,
    $"NUM-004 CA03 : les 10 series a restitution inactive (FACTU, DMD, RAP, ARRHF, ARRNQ, RA1, EAR, LIAI, SIREN, HF) ne redonnent pas, " +
    $"rendent 0 et journalisent la demande avec son explication ; leurs numeros s'ecrivent comme la V2 " +
    $"(26-022769, D2632400123, G3802601783*...){(inactivesFausses.Count > 0 ? " ; faux : " + string.Join(" ; ", inactivesFausses) : "")}");

// CA04 : un numero jamais donne (au-dela du compteur) est refuse.
var compteurAvant9999 = await Dernier("JPC", 2026);
var refus9999 = await V2Restituer(null, null, "JPC", "2026RJ9999", "XXPC: suppression procedure collective 2026RJ9999");
var refus9999Journal = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = 'JPC' AND Chrono = 9999 AND Evenement = 'refus' AND Code = -4;");
Exiger(refus9999.Code == CodesNumerotation.JamaisDonne && refus9999.Code < 0 && !string.IsNullOrEmpty(refus9999.Message)
       && await Rendus("JPC", 2026) == 0 && refus9999Journal == 1,
    $"NUM-004 CA04 : JPC 2026 a {compteurAvant9999}, 2026RJ9999 rendu -> refuse ({refus9999.Code}, « {refus9999.Message} »), rien de redonnable");
var rendu244 = await V2Restituer(null, null, "JPC", "2026RJ0244", "dernier numéro donné, annulé");
var redonne244 = await V3("JPC", 2026);
Exiger(rendu244.Code == 0 && rendu244.Issue == IssuesRestitution.Rendu && redonne244.Numero == "2026RJ0244",
    $"NUM-004 CA04 (jumeau) : le dernier numero donne (2026RJ0244, la borne) est accepte et redonne ({redonne244.Numero})");

// CA05 : rendu deux fois -> une seule entree redonnable, la seconde demande est journalisee.
var premier200 = await V2Restituer(null, null, "JPC", "2026RJ0200", "doublon de saisie");
var second200 = await V2Restituer(null, null, "JPC", "2026RJ0200", "doublon de saisie, encore");
var lignes200 = await Valeur<int>("SELECT COUNT(*) FROM num.NumeroRendu WHERE Serie = 'JPC' AND Annee = 2026 AND Chrono = 200;");
var double200 = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = 'JPC' AND Chrono = 200 AND Evenement = 'restitution en double';");
var redonne200 = await V2("JPC", 2026);
var apres200 = await V2("JPC", 2026);
Exiger(premier200.Issue == IssuesRestitution.Rendu && second200.Code == 0 && second200.Issue == IssuesRestitution.DejaRendu
       && lignes200 == 1 && double200 == 1 && redonne200.Numero == "2026RJ0200" && apres200.Numero == "2026RJ0245",
    $"NUM-004 CA05 : 2026RJ0200 rendu deux fois -> {lignes200} entree redonnable, seconde demande journalisee ({second200.Issue}) ; " +
    $"redonne une fois ({redonne200.Numero}), puis {apres200.Numero}");

// CA06 : un numero qui ne suit pas le format de sa serie est refuse, et rien n'est ecrit.
var avantIllisibles = await Etat();
var illisibles = new[] { "2026RJ00A2", "2026RX0012", "2026RJ012", "2026RJ00123", "26RJ0012", "2026-RJ0012", "" };
var codesIllisibles = new List<int>();
foreach (var n in illisibles) codesIllisibles.Add((await V2Restituer(null, null, "JPC", n, "illisible")).Code);
var jourIllisible = await V2Restituer(null, null, "JGDJ", "2640000001", "jour 400");   // quantieme hors 1..366
Exiger(codesIllisibles.All(c => c == CodesNumerotation.NumeroIllisible) && jourIllisible.Code == CodesNumerotation.NumeroIllisible
       && await Etat() == avantIllisibles,
    $"NUM-004 CA06 : chrono ou jour illisible ({string.Join(", ", illisibles.Select(n => n == "" ? "« »" : n))}, JGDJ jour 400) -> refuse " +
    $"({string.Join(" ", codesIllisibles)} {jourIllisible.Code}), rien d'ecrit");
var lisible = await V2Restituer(null, null, "JPC", "2026RJ0013", "lisible");
var redonne13 = await V3("JPC", 2026);
var jourLisible = await V2Restituer(null, null, "JGDJ", "2620000003", "jour 200 lisible");
var redonneJour = await V2("JGDJ", 2026, 200);
Exiger(lisible.Code == 0 && redonne13.Numero == "2026RJ0013" && jourLisible.Code == 0 && redonneJour.Numero == "2620000003",
    $"NUM-004 CA06 (jumeau) : des numeros bien formes sont acceptes et redonnes ({redonne13.Numero}, {redonneJour.Numero})");

// CA07 : l'annee est lue dans le numero quand son format la porte ; sinon l'annee demandee, ou en cours.
var renduSansAnnee = await V2Restituer(null, null, "JPC", "2025RJ0100", "rendu sans annee", annee: 0);
var renduRange = await Rendus("JPC", 2025);
var redonne2025 = await V3("JPC", 2025);
var ltiaCourant = await V2Restituer(null, null, "LTIA", "00002", "lot annulé");
var ltiaPrecedent = await V2Restituer(null, null, "LTIA", "00002", "lot annulé l'an dernier", annee: anneeCourante - 1);
var ltiaRendus = (await Rendus("LTIA", anneeCourante), await Rendus("LTIA", anneeCourante - 1));
Exiger(renduSansAnnee.Code == 0 && renduRange == 1 && redonne2025.Numero == "2025RJ0100" && redonne2025.Reattribue
       && ltiaCourant.Code == 0 && ltiaPrecedent.Code == 0 && ltiaRendus == (1, 1),
    $"NUM-004 CA07 : 2025RJ0100 rendu sans annee -> range en 2025, lue dans le numero (redonne en 2025 : {redonne2025.Numero}) ; " +
    $"LTIA (format sans annee) : l'annee en cours sans annee donnee, {anneeCourante - 1} quand elle est donnee (rendus {ltiaRendus})");
await V2("LTIA", anneeCourante);
await V2("LTIA", anneeCourante - 1);

// CA08 : sans explication -> le journal le dit, avec compte et poste ; au-dela de 50 caracteres -> texte
// entier au journal, 50 premiers sur la ligne de la V2 (num.NumeroRendu).
var longue = "Minute annulée par jugement du tribunal de commerce, rectification d'erreur matérielle n° 42";
var sansExplication = await V2Restituer(null, null, "JPC", "2026RJ0014", null);
var avecLongue = await V2Restituer(null, null, "JPC", "2026RJ0015", longue);
await using (var cn = await Ouvrir())
await using (var cmd = new SqlCommand(
    "SELECT j.Explication, j.Compte, j.Poste, r.Explication FROM num.Journal j JOIN num.NumeroRendu r ON r.Serie = j.Serie AND r.Annee = j.Annee " +
    "AND r.Jour = j.Jour AND r.Chrono = j.Chrono WHERE j.Serie = 'JPC' AND j.Annee = 2026 AND j.Chrono IN (14, 15) AND j.Evenement = 'restitution' ORDER BY j.Chrono;", cn))
await using (var l = await cmd.ExecuteReaderAsync())
{
    await l.ReadAsync();
    var (j14, compte14, poste14, r14) = (l.GetString(0), l.GetString(1), l.GetString(2), l.GetString(3));
    await l.ReadAsync();
    var (j15, r15) = (l.GetString(0), l.GetString(3));
    Exiger(sansExplication.Code == 0 && j14 == "sans explication (demande de la V2)" && compte14 == "agent.v2" && poste14 == "POSTE-V2"
           && r14 == j14,
        $"NUM-004 CA08 : sans explication -> journal « {j14} », compte {compte14}, poste {poste14}");
    Exiger(avecLongue.Code == 0 && longue.Length > 50 && j15 == longue && r15 == longue[..50],
        $"NUM-004 CA08 : explication de {longue.Length} caracteres -> entiere au journal, 50 premiers sur la ligne rendue (« {r15} »)");
}
await V2("JPC", 2026);
await V2("JPC", 2026);

// CA09 : un code non nul (negatif pour un refus) ; 0 seulement quand le numero est rendu ou journalise.
var codesRefus = new[] { inconnuV2.Code, refus9999.Code, codesIllisibles[0], jourIllisible.Code };
var codesFaits = new[] { rendu12.Code, rendu244.Code, second200.Code, lisible.Code, sansExplication.Code };
var inconnuRendu = await V2Restituer(null, null, "ZZZZZ", "2026RJ0001", "type inconnu");
Exception? erreurVerrou = null;
var rendusAvantVerrou = await Rendus("JPC", 2026);
await using (var bloqueur = await Ouvrir())
{
    await using var tx = (SqlTransaction)await bloqueur.BeginTransactionAsync();
    await V2Attribuer(bloqueur, tx, "JPC", 2026);                       // tient le compteur JPC 2026
    try { await V2Restituer(null, null, "JPC", "2026RJ0016", "pendant un verrou", delaiMs: 1000); }
    catch (SqlException e) { erreurVerrou = e; }
    await tx.RollbackAsync();
}
Exiger(codesRefus.All(c => c < 0) && inconnuRendu.Code == -2 && inconnuRendu.Issue == IssuesRestitution.TypeInconnu
       && codesFaits.All(c => c == 0)
       && erreurVerrou is SqlException { Number: DistributeurNumeros.ErreurCompteurOccupe } && await Rendus("JPC", 2026) == rendusAvantVerrou,
    $"NUM-004 CA09 : refus negatifs ({string.Join(" ", codesRefus)}, restitution d'un type inconnu {inconnuRendu.Code}), 0 pour rendu ou journalise ; " +
    $"une restitution en erreur (compteur tenu) leve l'erreur {(erreurVerrou as SqlException)?.Number} et ne rend rien ({erreurVerrou?.Message})");

// CA10 : la route de l'ecran de reattribution applique les memes regles qu'un programme.
var ecranRefus = await V3Restituer("JPC", "2026RJ9999", "essai depuis l'ecran");
var ecranSansExplication = await V3Restituer("JPC", "2026RJ0017", "  ");
var ecranRendu = await V3Restituer("JPC", "2026RJ0017", "acte supprimé depuis l'écran");
var ecranJournal = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = 'JPC' AND Chrono = 17 AND Evenement = 'restitution' AND Logiciel = 'V3';");
var ecranRedonne = await V2("JPC", 2026);
Exiger(ecranRefus.Resultat.Code == refus9999.Code && ecranRefus.Resultat.Issue == refus9999.Issue
       && ecranSansExplication.Statut == HttpStatusCode.BadRequest && ecranRendu.Resultat.Code == 0 && ecranJournal == 1
       && ecranRedonne.Numero == "2026RJ0017",
    $"NUM-004 CA10 (regles) : POST {RoutesCompteur.Restitutions} refuse 2026RJ9999 comme le programme ({ecranRefus.Resultat.Code}), " +
    $"exige l'explication ({(int)ecranSansExplication.Statut}), rend 2026RJ0017 (journal V3) qui est redonne ({ecranRedonne.Numero})");
Reste("NUM-004 CA10 (ecran) : l'ecran de reattribution reserve au responsable -- il attend les droits (SOC-002, lot 3) ; la route qu'il appellera est prouvee ci-dessus");

// =====================================================================================================
// NUM-001 CA06 — le journal rend compte de chaque numero (vu sur les gestes faits plus haut)
// =====================================================================================================
await using (var cn = await Ouvrir())
await using (var cmd = new SqlCommand(
    "SELECT Evenement, Logiciel, COUNT(*) FROM num.Journal WHERE Serie = 'JPC' AND Annee = 2026 AND Chrono = 12 " +
    "AND Serie IS NOT NULL AND Numero IS NOT NULL AND Compte IS NOT NULL AND Poste IS NOT NULL AND Le IS NOT NULL " +
    "GROUP BY Evenement, Logiciel;", cn))
await using (var l = await cmd.ExecuteReaderAsync())
{
    var vus = new Dictionary<string, int>();
    while (await l.ReadAsync()) vus[$"{l.GetString(0)} {l.GetString(1)}"] = l.GetInt32(2);
    var explique = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = 'JPC' AND Chrono = 12 AND Evenement = 'restitution' AND Explication LIKE 'procédure supprimée%';");
    var attributions = await Valeur<int>("SELECT COUNT(*) FROM num.Journal WHERE Serie = 'RC6' AND Annee = 2026 AND Evenement = 'attribution';");
    Exiger(vus.GetValueOrDefault("restitution V2") == 1 && vus.GetValueOrDefault("restitution V3") == 1
           && vus.GetValueOrDefault("reattribution V2") == 1 && vus.GetValueOrDefault("reattribution V3") == 1
           && explique == 2 && attributions == 202,
        $"NUM-001 CA06 : 2026RJ0012 au journal -- {string.Join(", ", vus.Select(v => $"{v.Key} x{v.Value}"))} -- avec serie, numero, logiciel, " +
        $"compte, poste, date et explication ; RC6 2026 : {attributions} attributions pour 202 numeros donnes");
}

// =====================================================================================================
// NUM-008 — Aucun numero donne deux fois quand les deux logiciels numerotent ensemble
// =====================================================================================================

// CA01 (et critere de reception du lot) : 4 sessions V2 + 4 sessions V3, 1 250 numeros chacune,
// en meme temps sur RGB 2026 : 10 000 numeros tous differents et consecutifs.
var debutRgb = (await Dernier("RGB", 2026))!.Value;
var servis = new ConcurrentBag<(string Logiciel, int Chrono, string Numero)>();
var erreursCharge = new ConcurrentBag<string>();
var topCharge = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var montre = Stopwatch.StartNew();
var sessions = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
{
    await using var cn = i < 4 ? await Ouvrir() : null;
    await topCharge.Task;
    for (var k = 0; k < 1250; k++)
    {
        var n = cn is not null ? await V2Attribuer(cn, null, "RGB", 2026) : await V3("RGB", 2026);
        if (n.Code != 0 || n.Chrono is null) erreursCharge.Add($"{n.Code} {n.Message}");
        else servis.Add((cn is not null ? "V2" : "V3", n.Chrono.Value, n.Numero!));
    }
})).ToList();
montre.Restart();
topCharge.SetResult();
await Task.WhenAll(sessions);
var dureeCharge = montre.Elapsed;
var finRgb = (await Dernier("RGB", 2026))!.Value;
var chronos = servis.Select(s => s.Chrono).OrderBy(x => x).ToList();
var distincts = servis.Select(s => s.Numero).Distinct().Count();
var consecutifs = chronos.Count > 0 && chronos.SequenceEqual(Enumerable.Range(debutRgb + 1, chronos.Count));
Exiger(erreursCharge.IsEmpty && servis.Count == 10_000 && distincts == 10_000 && consecutifs && finRgb - debutRgb == 10_000
       && servis.Count(s => s.Logiciel == "V2") == 5000,
    $"NUM-008 CA01 : 4 sessions V2 + 4 sessions V3 tirent 10 000 numeros RGB 2026 en meme temps -> {distincts} differents, consecutifs " +
    $"({debutRgb + 1} a {finRgb}), compteur +{finRgb - debutRgb}, {erreursCharge.Count} erreur(s), {dureeCharge.TotalSeconds:0.0} s " +
    $"({10_000 / Math.Max(dureeCharge.TotalSeconds, 0.001):0} numeros/s)");
rapport["NUM-008 CA01"] = new { sessionsV2 = 4, sessionsV3 = 4, numeros = servis.Count, distincts, consecutifs, debut = debutRgb + 1, fin = finRgb,
                                 erreurs = erreursCharge.Count, secondes = Math.Round(dureeCharge.TotalSeconds, 1) };

// CA02 : premiere demande du jour, V2 et V3 au meme instant : un seul compteur du jour, deux numeros qui se suivent.
var joursFaux = new List<int>();
for (var jour = 100; jour < 120; jour++)
{
    var d = jour;
    var deux = await EnMemeTemps(2, i => i == 0 ? V2("JGDJ", 2026, d) : V3("JGDJ", 2026, d));
    var nb = await Valeur<int>("SELECT COUNT(*) FROM num.Compteur WHERE Type = 'JGDJ' AND Annee = 2026 AND Jour = @j;", ("@j", d));
    if (nb != 1 || !deux.Select(n => n.Chrono ?? 0).Order().SequenceEqual([1, 2])) joursFaux.Add(d);
}
Exiger(joursFaux.Count == 0,
    $"NUM-008 CA02 : 20 premiers appels du jour JGDJ (jours 100 a 119), V2 et V3 au meme instant -> un compteur par jour, numeros 1 et 2" +
    (joursFaux.Count > 0 ? $" ; faux : {string.Join(", ", joursFaux)}" : ""));
rapport["NUM-008 CA02"] = new { jours = 20, faux = joursFaux };

// CA03 : 100 numeros rendus pendant que les deux logiciels demandent : chacun redonne exactement une fois.
var debutRc1 = (await Dernier("RC1", 2026))!.Value;
var aRendre = Enumerable.Range(5001, 100).ToList();
var servisRc1 = new ConcurrentBag<int>();
var erreursRc1 = new ConcurrentBag<string>();
var topRc1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var demandeurs = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
{
    await topRc1.Task;
    for (var k = 0; k < 40; k++)
    {
        var n = i < 4 ? await V2("RC1", 2026) : await V3("RC1", 2026);
        if (n.Code != 0) erreursRc1.Add(n.Message ?? "?"); else servisRc1.Add(n.Chrono!.Value);
    }
})).ToList();
var rendeur = Task.Run(async () =>
{
    await topRc1.Task;
    foreach (var c in aRendre)
    {
        var numero = $"F26/{c:000000}";
        var r = c % 2 == 0 ? await V2Restituer(null, null, "RC1", numero, "charge : acte annulé")
                           : (await V3Restituer("RC1", numero, "charge : acte annulé (V3)")).Resultat;
        if (r.Code != 0 || r.Issue != IssuesRestitution.Rendu) erreursRc1.Add($"rendu {numero} : {r.Code} {r.Issue}");
    }
});
topRc1.SetResult();
await Task.WhenAll([.. demandeurs, rendeur]);
var restaient = await Rendus("RC1", 2026);
for (var k = 0; k < restaient; k++) servisRc1.Add((await V2("RC1", 2026)).Chrono!.Value);   // les derniers rendus
var redonnes = servisRc1.Where(aRendre.Contains).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
var neufs = servisRc1.Where(c => !aRendre.Contains(c)).Order().ToList();
var finRc1 = (await Dernier("RC1", 2026))!.Value;
Exiger(erreursRc1.IsEmpty && aRendre.All(c => redonnes.GetValueOrDefault(c) == 1) && await Rendus("RC1", 2026) == 0
       && neufs.SequenceEqual(Enumerable.Range(debutRc1 + 1, finRc1 - debutRc1)) && servisRc1.Count == servisRc1.Distinct().Count(),
    $"NUM-008 CA03 : 100 numeros RC1 rendus (V2 et V3) pendant 320 demandes simultanees -> chacun redonne exactement une fois " +
    $"({redonnes.Count}/100, dont {restaient} apres la charge), les autres numeros neufs et consecutifs ({debutRc1 + 1} a {finRc1}), aucun doublon " +
    $"({erreursRc1.Count} erreur(s){(erreursRc1.IsEmpty ? "" : " : " + erreursRc1.First())})");
rapport["NUM-008 CA03"] = new { rendus = 100, redonnesUneFois = redonnes.Count(r => r.Value == 1), apresCharge = restaient, neufs = neufs.Count, erreurs = erreursRc1.Count };

// CA04 : a minuit le 31 decembre : des demandes de l'annee qui finit et de la nouvelle, en meme temps.
// (L'horloge du serveur ne se deplace pas : l'annee est passee explicitement, comme le permet GENERER_NUMERO.)
var debutJpc2026 = (await Dernier("JPC", 2026))!.Value;
var minuit = await EnMemeTemps(40, i => (i % 4) switch
{
    0 => V2("JPC", 2026), 1 => V3("JPC", 2026), 2 => V2("JPC", 2027), _ => V3("JPC", 2027),
});
var compteurs2027Jpc = await Valeur<int>("SELECT COUNT(*) FROM num.Compteur WHERE Type = 'JPC' AND Annee = 2027;");
var nouvelle = minuit.Where(n => n.Annee == 2027).Select(n => n.Chrono!.Value).Order().ToList();
var ancienne = minuit.Where(n => n.Annee == 2026).Select(n => n.Chrono!.Value).Order().ToList();
Exiger(minuit.All(n => n.Code == 0) && compteurs2027Jpc == 1 && nouvelle.SequenceEqual(Enumerable.Range(1, 20))
       && ancienne.SequenceEqual(Enumerable.Range(debutJpc2026 + 1, 20)) && minuit.Any(n => n.Numero == "2027RJ0001"),
    $"NUM-008 CA04 : 20 demandes JPC 2026 et 20 JPC 2027 (sans compteur) au meme instant -> {compteurs2027Jpc} compteur 2027, " +
    $"qui commence a sa valeur de depart (2027RJ0001 a 2027RJ0020), 2026 continue ({debutJpc2026 + 1} a {debutJpc2026 + 20})");
rapport["NUM-008 CA04"] = new { compteurs2027 = compteurs2027Jpc, numeros2027 = nouvelle.Count, numeros2026 = ancienne.Count };

// CA05 : une transaction de la V2 garde le compteur ; la V3 recoit une erreur claire apres le delai,
// sans blocage reste ouvert ni numero perdu.
NumeroAttribue? numeroBloqueur;
HttpStatusCode statutAttente;
NumeroAttribue? reponseAttente;
TimeSpan attente;
await using (var bloqueur = await Ouvrir())
{
    await using var tx = (SqlTransaction)await bloqueur.BeginTransactionAsync();
    numeroBloqueur = await V2Attribuer(bloqueur, tx, "RGB", 2026);     // la V2 tient RGB 2026 (jusqu'a 30 s dans le scenario)
    var chronoAttente = Stopwatch.StartNew();
    using (var r = await http.PostAsJsonAsync(numerosA, new DemandeNumero("RGB", 2026), json))
    {
        attente = chronoAttente.Elapsed;
        statutAttente = r.StatusCode;
        reponseAttente = await r.Content.ReadFromJsonAsync<NumeroAttribue>(json);
    }
    await tx.CommitAsync();
}
var apresAttente = await V3("RGB", 2026);
var verrousRestants = await Valeur<int>(
    "SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID() AND request_session_id <> @@SPID AND resource_type <> 'DATABASE';");
Exiger(statutAttente == HttpStatusCode.ServiceUnavailable && reponseAttente?.Code == CodesNumerotation.CompteurOccupe
       && reponseAttente.Message!.Contains("tenu par une autre transaction") && reponseAttente.Numero is null
       && attente >= TimeSpan.FromMilliseconds(DelaiServeurMs * 0.9) && attente < TimeSpan.FromSeconds(30),
    $"NUM-008 CA05 : la V2 tient RGB 2026 ; la V3 recoit {(int)statutAttente} apres {attente.TotalSeconds:0.0} s " +
    $"(delai {DelaiServeurMs} ms, bien avant 30 s) : « {reponseAttente?.Message} »");
Exiger(apresAttente.Chrono == numeroBloqueur.Chrono + 1 && verrousRestants == 0,
    $"NUM-008 CA05 : aucun numero perdu (V2 {numeroBloqueur.Numero}, puis V3 {apresAttente.Numero}) et aucun verrou reste ouvert ({verrousRestants})");
rapport["NUM-008 CA05"] = new { statut = (int)statutAttente, secondes = Math.Round(attente.TotalSeconds, 2), message = reponseAttente?.Message, verrousRestants };

// CA06 : des transactions annulees au hasard des deux cotes pendant la charge : les numeros servis aux
// actes valides forment une suite sans trou ni doublon, et le journal explique chacun.
var debut2025 = (await Dernier("RGB", 2025))!.Value;
var valides = new ConcurrentBag<int>();
var annulees = 0;
var erreursAnnul = new ConcurrentBag<string>();
var topAnnul = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var actes = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
{
    var hasard = new Random(1000 + i);
    await using var cn = await Ouvrir();
    await topAnnul.Task;
    for (var k = 0; k < 300; k++)
    {
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var n = i < 4 ? await V2Attribuer(cn, tx, "RGB", 2025)
                      : await distributeur.AttribuerAsync(cn, tx, new DemandeNumero("RGB", 2025), $"agent.v3.{i}", "POSTE-V3");
        if (n.Code != 0) { erreursAnnul.Add(n.Message ?? "?"); await tx.RollbackAsync(); continue; }
        if (hasard.Next(4) == 0) { await tx.RollbackAsync(); Interlocked.Increment(ref annulees); }
        else { await tx.CommitAsync(); valides.Add(n.Chrono!.Value); }
    }
})).ToList();
topAnnul.SetResult();
await Task.WhenAll(actes);
var fin2025 = (await Dernier("RGB", 2025))!.Value;
var suiteValide = valides.Order().ToList();
var journal2025 = new List<int>();
await using (var cn = await Ouvrir())
await using (var cmd = new SqlCommand("SELECT Chrono FROM num.Journal WHERE Serie = 'RGB' AND Annee = 2025 AND Evenement = 'attribution' ORDER BY Chrono;", cn))
await using (var l = await cmd.ExecuteReaderAsync())
    while (await l.ReadAsync()) journal2025.Add(l.GetInt32(0));
Exiger(erreursAnnul.IsEmpty && annulees > 0 && suiteValide.SequenceEqual(Enumerable.Range(debut2025 + 1, fin2025 - debut2025))
       && journal2025.SequenceEqual(suiteValide),
    $"NUM-008 CA06 : 2 400 actes (V2 et V3) dont {annulees} annules au hasard -> les {suiteValide.Count} numeros des actes valides se suivent " +
    $"sans trou ni doublon ({debut2025 + 1} a {fin2025}) et le journal en a exactement autant ({journal2025.Count})");
rapport["NUM-008 CA06"] = new { actes = 2400, annulees, valides = suiteValide.Count, debut = debut2025 + 1, fin = fin2025, journal = journal2025.Count };

// =====================================================================================================
// Raccord des lots 2, 4 et 5 (lot 8) : session Windows, base DU GREFFE par le catalogue, agent du greffe
// =====================================================================================================
Console.WriteLine("--- Raccord : session Windows, base du greffe, agent du greffe (lot 8)");
async Task<HttpResponseMessage> Appeler(HttpMethod verbe, string adresse, object? corps, string? compte)
{
    var demande = new HttpRequestMessage(verbe, adresse);
    if (compte is not null) demande.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
    if (corps is not null) demande.Content = JsonContent.Create(corps, options: json);
    return await sansEnTete.SendAsync(demande);
}
// Les trois routes, pour un greffe. La restitution vise un numero jamais donne (-4, journalise) : servie,
// elle ne rend rien qui serait redonne ensuite.
(HttpMethod Verbe, string Adresse, object? Corps)[] TroisRoutes(string greffe) =>
[
    (HttpMethod.Get, RoutesCompteur.Pour(RoutesCompteur.Compteurs, greffe), null),
    (HttpMethod.Post, RoutesCompteur.Pour(RoutesCompteur.Numeros, greffe), new DemandeNumero("RGB", 2026)),
    (HttpMethod.Post, RoutesCompteur.Pour(RoutesCompteur.Restitutions, greffe), new DemandeRestitution("RGB", "2026B99999", "essai du raccord (lot 8)", 2026)),
];
async Task<(int[] Codes, string[] Corps)> Jouer(string greffe, string? compte)
{
    var codes = new List<int>();
    var corps = new List<string>();
    foreach (var (verbe, adresse, contenu) in TroisRoutes(greffe))
        using (var r = await Appeler(verbe, adresse, contenu, compte))
        {
            codes.Add((int)r.StatusCode);
            corps.Add(await r.Content.ReadAsStringAsync());
        }
    return ([.. codes], [.. corps]);
}
string Codes(int[] codes) => string.Join(" ", codes);
async Task<string> EtatDesTrois() => $"A {await Etat()} | B {await EtatSur(baseB.Connexion)} | commune {await EtatSur(commune.Connexion)}";

// (1) Les trois routes exigent la session (lot 2) : posee sur leur groupe, lue dans le serveur lui-meme.
var routesDuCompteur = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
    .Where(r => r.Metadata.GetMetadata<ModuleOrigine>()?.Nom == "RigV3.Serveur.Numerotation.Compteur.ModuleCompteur").ToList();
var malProtegees = routesDuCompteur
    .Where(r => !r.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == PolitiquesSocle.AgentAdmis)
                || r.RoutePattern.GetParameter(PolitiquesSocle.ParametreGreffe) is null)
    .Select(r => r.RoutePattern.RawText).ToList();
Exiger(routesDuCompteur.Count == 3 && malProtegees.Count == 0,
    $"Raccord (1) : les {routesDuCompteur.Count} routes du compteur portent PolitiquesSocle.AgentAdmis et le greffe dans leur adresse " +
    $"({string.Join(", ", routesDuCompteur.Select(r => r.RoutePattern.RawText))}){(malProtegees.Count > 0 ? " ; fautives : " + string.Join(", ", malProtegees) : "")}");
var etatAvantRefus = await EtatDesTrois();
var anonyme = await Jouer(GreffeA, null);
var desactive = await Jouer(GreffeA, RefuseA);
var etatApresRefus = await EtatDesTrois();
Exiger(anonyme.Codes.All(c => c == 401),
    $"Raccord (1) : sans session Windows, les 3 routes du compteur du greffe A repondent 401 ({Codes(anonyme.Codes)})");
Exiger(desactive.Codes.All(c => c == 403) && etatApresRefus == etatAvantRefus,
    $"Raccord (1) : un compte desactive dans le greffe A recoit 403 sur les 3 routes ; ni ces refus ni les 401 n'ont fait tourner une route " +
    $"(aucun compteur, numero rendu ni journal ne bouge) ({Codes(desactive.Codes)})");
var admisA = await Jouer(GreffeA, AgentA);
var compteJournalise = await Valeur<string>("SELECT TOP 1 Compte FROM num.Journal WHERE Logiciel = 'V3' AND Evenement = 'attribution' ORDER BY Id DESC;");
Exiger(admisA.Codes.All(c => c == 200) && compteJournalise == AgentA,
    $"Raccord (1) (jumeau) : l'agent admis du greffe A est servi par les 3 routes ({Codes(admisA.Codes)}) ; le journal porte le compte " +
    $"de sa session (« {compteJournalise} »), plus « (non identifie) » (NUM-001 CA06)");

// (2) Base du greffe demande, par le catalogue (lot 4) : 404 inconnu, 503 injoignable sans repli, A et B separes.
var absent = await Jouer(GreffeAbsent, AgentA);
Exiger(absent.Codes.All(c => c == 404) && absent.Corps.All(c => c.Contains(GreffeAbsent)),
    $"Raccord (2) : un greffe absent du catalogue ({GreffeAbsent}) -> 404 sur les 3 routes, qui le nomment ; aucune base par defaut ({Codes(absent.Codes)})");
var etatAvantMuet = await EtatDesTrois();
var muet = await Jouer(GreffeMuet, AgentA);
Exiger(muet.Codes.All(c => c == 503) && muet.Corps.All(c => c.Contains(baseMuette)) && await EtatDesTrois() == etatAvantMuet,
    $"Raccord (2) : la base du greffe {GreffeMuet} ne repond pas -> 503 sur les 3 routes, la base nommee ; sans repli : ni la base commune, " +
    $"ni A, ni B ne bougent ({Codes(muet.Codes)})");
var rgbA = (await Dernier("RGB", 2026))!.Value;
var rgbB = await ValeurSur<int>(baseB.Connexion, "SELECT Chrono FROM num.Compteur WHERE Type = 'RGB' AND Annee = 2026;");
var etatB = await EtatSur(baseB.Connexion);
var tireA = await V3("RGB", 2026);
Exiger(tireA.Code == 0 && tireA.Chrono == rgbA + 1 && await Dernier("RGB", 2026) == rgbA + 1 && await EtatSur(baseB.Connexion) == etatB,
    $"Raccord (2) : un numero RGB 2026 tire pour le greffe A ({tireA.Numero}) avance le compteur de A ({rgbA} -> {rgbA + 1}) et laisse " +
    $"celui de B intact ({rgbB}, ni compteur, ni rendu, ni journal ne bouge dans B)");
var etatA = await Etat();
using (var r = await Appeler(HttpMethod.Post, RoutesCompteur.Pour(RoutesCompteur.Numeros, GreffeB), new DemandeNumero("RGB", 2026), AgentB))
{
    var tireB = await r.Content.ReadFromJsonAsync<NumeroAttribue>(json);
    var rgbBApres = await ValeurSur<int>(baseB.Connexion, "SELECT Chrono FROM num.Compteur WHERE Type = 'RGB' AND Annee = 2026;");
    Exiger(r.StatusCode == HttpStatusCode.OK && tireB is { Code: 0 } && tireB.Chrono == rgbB + 1 && rgbBApres == rgbB + 1 && await Etat() == etatA,
        $"Raccord (2) (jumeau) : tire pour le greffe B par son agent, le numero vient de B ({tireB?.Numero}, {rgbB} -> {rgbBApres}) et A ne bouge pas");
}
using (var r = await Appeler(HttpMethod.Get, RoutesCompteur.Pour(RoutesCompteur.Compteurs, GreffeB), null, AgentB))
{
    var listeB = await r.Content.ReadFromJsonAsync<CompteurNumeros[]>(json) ?? [];
    Exiger(listeB.Length == 2 && listeB.Any(c => c.Type == "RGB" && c.Chrono == rgbB + 1) && listeB.Any(c => c.Type == "JPC" && c.Chrono == 10),
        $"Raccord (2) : la liste du greffe B est celle de SA base, lue par le catalogue ({listeB.Length} compteurs : " +
        $"{string.Join(", ", listeB.Select(c => $"{c.Type} {c.Chrono}"))}) ; celle de A en a {liste.Length} et plus");
}
// Ce qui est interdit, c'est d'OUVRIR une base autrement que par le catalogue : BaseRig.OuvrirAsync (la base
// unique) ou une chaine de connexion. Le module peut NOMMER BaseRig pour le passer a ServiceDroits (droit
// XNUMERO de la restitution, 21/11) : sur une adresse de greffe, l'identite et les droits n'ouvrent que la
// base du greffe (ServiceIdentite.DeLAppelantAsync, ServiceDroits.OuvrirBaseAsync). Le mot seul n'ouvre rien.
var chaineEnDur = new Regex(@"\.OuvrirAsync\(|ConnectionStrings|Data\s*Source\s*=|Server\s*=|Initial\s*Catalog|Database\s*=", RegexOptions.IgnoreCase);
var sourceModule = File.ReadAllText(Path.Combine(racine, "Serveur", "Numerotation", "Compteur", "ModuleCompteur.cs"));
Exiger(chaineEnDur.IsMatch("await using var cn = await b.OuvrirAsync(ct);") && chaineEnDur.IsMatch("Server=SQL-PROD;Database=RIGV3_AMIENS")
       && !chaineEnDur.IsMatch("await c.OuvrirBaseV3Async(greffe, ct)"),
    "Raccord (2) (jumeau) : le controle de la source reconnait l'ouverture de la base unique (BaseRig.OuvrirAsync) et une chaine de connexion, pas le catalogue");
Exiger(!chaineEnDur.IsMatch(sourceModule) && sourceModule.Contains("OuvrirBaseV3Async"),
    "Raccord (2) : ModuleCompteur.cs n'ouvre aucune base par une chaine ni par la base unique : seulement par le catalogue (OuvrirBaseV3Async)");

// (3) L'agent est cherche dans la base du greffe choisi, comme la V2 : connu de A seulement -> refuse sur B.
var comptesCommune = await ValeurSur<int>(commune.Connexion, "SELECT COUNT(*) FROM soc.Compte;");
var etatAvantCroise = await EtatDesTrois();
var aSurB = await Jouer(GreffeB, AgentA);
var bSurA = await Jouer(GreffeA, AgentB);
Exiger(aSurB.Codes.All(c => c == 403) && await EtatDesTrois() == etatAvantCroise,
    $"Raccord (3) : l'agent {AgentA}, connu du greffe A seulement, est refuse sur les 3 routes du greffe B (403) ; rien ne bouge ({Codes(aSurB.Codes)})");
Exiger(bSurA.Codes.All(c => c == 403),
    $"Raccord (3) : symetrique -- l'agent {AgentB}, connu du greffe B seulement, est refuse sur le greffe A (403) ({Codes(bSurA.Codes)})");
var bSurB = await Jouer(GreffeB, AgentB);
Exiger(bSurB.Codes.All(c => c == 200) && comptesCommune == 0,
    $"Raccord (3) (jumeau) : {AgentB} est servi sur SON greffe ({Codes(bSurB.Codes)}) ; la base commune n'a aucun compte d'agent " +
    $"({comptesCommune}) : chaque admission vient de la base du greffe de l'adresse");

// --- L'ecran « Compteurs de numéros » (Pont/EcranCompteurs.cs, NUM-004 CA10) sur le vrai serveur : -------
// --- le meme client que l'app (ClientServeur, en-tete RIGV3_COMPTE pose a sa construction). ----------------
ClientServeur ClientEcran(string compte)
{
    Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
    try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
    finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
}
var ecranA = ClientEcran(AgentA);
var ecranLus = await http.GetFromJsonAsync<CompteurNumeros[]>(compteursA, json) ?? [];
var vueA = await EcranCompteurs.OuvrirAsync(ecranA, GreffeA);
Exiger(vueA is { Mode: "serveur", Greffe: GreffeA } && vueA.Compteurs.Length == ecranLus.Length && vueA.Compteurs.Length > 0,
    $"Ecran NUM-001 CA01 : l'ecran lit les compteurs du greffe {GreffeA} par la route ({vueA.Mode}, {vueA.Compteurs.Length} compteurs, route : {ecranLus.Length})");
var sansGreffe = await EcranCompteurs.OuvrirAsync(ecranA, null);
Exiger(sansGreffe is { Mode: "sans-greffe", Echec: true, Compteurs.Length: 0 },
    $"Ecran : branche au serveur sans greffe ouvert, il le dit et ne devine aucun greffe ({sansGreffe.Mode} : {sansGreffe.Message})");
var ecranDonne = await V3("JPC", 2026);
var jpcAvant = vueA.Compteurs.Where(c => c.Type == "JPC" && c.Annee == 2026).Sum(c => c.NumerosRendus);
var apresRendu = await EcranCompteurs.RendreAsync(ecranA, GreffeA, new DemandeRestitution("JPC", ecranDonne.Numero!, "acte annulé (écran des compteurs)"));
var jpcApres = apresRendu.Compteurs.Where(c => c.Type == "JPC" && c.Annee == 2026).Sum(c => c.NumerosRendus);
Exiger(apresRendu is { Mode: "serveur", Echec: false } && apresRendu.Message!.Contains(ecranDonne.Numero!) && apresRendu.Message.Contains("sera redonné") && jpcApres == jpcAvant + 1,
    $"Ecran NUM-004 CA10 : {ecranDonne.Numero} rendu depuis l'ecran -> « {apresRendu.Message} », numeros rendus JPC 2026 {jpcAvant} -> {jpcApres}");
var ecranRedonneApres = await V3("JPC", 2026);
Exiger(ecranRedonneApres.Numero == ecranDonne.Numero,
    $"Ecran NUM-004 CA10 : memes regles qu'un programme -- le numero rendu par l'ecran est redonne au prochain acte ({ecranRedonneApres.Numero})");
var ecranJamais = ecranDonne.Numero![..^4] + ((ecranDonne.Chrono ?? 0) + 500).ToString("D4");
var refusEcran = await EcranCompteurs.RendreAsync(ecranA, GreffeA, new DemandeRestitution("JPC", ecranJamais, "essai"));
Exiger(refusEcran is { Echec: true } && refusEcran.Message!.Contains("refusée") && !refusEcran.Message.Contains("{"),
    $"Ecran NUM-004 CA09 : {ecranJamais}, jamais donne, est refuse et l'ecran le montre en refus (« {refusEcran.Message} »)");
var vueBSurA = await EcranCompteurs.OuvrirAsync(ClientEcran(AgentB), GreffeA);
Exiger(vueBSurA is { Mode: "refuse", Compteurs.Length: 0 },
    $"Ecran : l'agent du greffe B sur le greffe A recoit le refus, sans compteur ({vueBSurA.Mode} : {vueBSurA.Message})");
var ecranAbsent = await EcranCompteurs.OuvrirAsync(ecranA, GreffeAbsent);
var ecranMuet = await EcranCompteurs.OuvrirAsync(ecranA, GreffeMuet);
Exiger(ecranAbsent is { Mode: "erreur" } && ecranAbsent.Message!.Contains("pas au catalogue") && ecranMuet is { Mode: "erreur" } && ecranMuet.Message!.Contains("ne répond pas") && !ecranMuet.Message.Contains("{"),
    $"Ecran : greffe absent du catalogue -> « {ecranAbsent.Message} » ; base muette -> « {ecranMuet.Message} »");
// NUM-004 T08 : la reattribution avec un agent sans XNUMERO -- 403, et rien ne bouge (compteurs, rendus, journal).
var etatAvantSansDroit = await Etat();
var jpcDonnePourRefus = await V3("JPC", 2026);
var etatApresTirage = await Etat();
using (var r = await Appeler(HttpMethod.Post, restitutionsA, new DemandeRestitution("JPC", jpcDonnePourRefus.Numero!, "essai sans le droit"), SansXnumeroA))
{
    var corps = await r.Content.ReadAsStringAsync();
    Exiger(r.StatusCode == HttpStatusCode.Forbidden && corps.Contains(RoutesCompteur.FonctionReattribution) && await Etat() == etatApresTirage,
        $"NUM-004 T08 / CA10 : {SansXnumeroA}, admis dans le greffe A sans la fonction XNUMERO, ne rend pas {jpcDonnePourRefus.Numero} " +
        $"({(int)r.StatusCode}) et rien ne bouge (etat {etatApresTirage} -> {await Etat()})");
}
using (var r = await Appeler(HttpMethod.Get, compteursA, null, SansXnumeroA))
    Exiger(r.StatusCode == HttpStatusCode.OK,
        $"NUM-004 T08 (jumeau) : le meme agent lit les compteurs ({(int)r.StatusCode}) : seule la reattribution est reservee");
var refusDroitEcran = await EcranCompteurs.RendreAsync(ClientEcran(SansXnumeroA), GreffeA,
    new DemandeRestitution("JPC", jpcDonnePourRefus.Numero!, "essai sans le droit (ecran)"));
Exiger(refusDroitEcran is { Mode: "serveur", Echec: true } && refusDroitEcran.Message!.Contains("XNUMERO") && !refusDroitEcran.Message.Contains("{")
       && await Etat() == etatApresTirage,
    $"Ecran NUM-004 T08 : sans XNUMERO, l'ecran dit pourquoi (« {refusDroitEcran.Message} ») et rien n'est rendu");
var renduParResponsable = await EcranCompteurs.RendreAsync(ecranA, GreffeA, new DemandeRestitution("JPC", jpcDonnePourRefus.Numero!, "rendu par le responsable"));
Exiger(renduParResponsable is { Echec: false } && etatAvantSansDroit != etatApresTirage,
    $"Ecran NUM-004 T08 (jumeau) : le meme numero, rendu par {AgentA} qui a XNUMERO, passe (« {renduParResponsable.Message} »)");

var ecranDemo = await EcranCompteurs.OuvrirAsync(null, null);
var ecranDemoRendu = await EcranCompteurs.RendreAsync(null, null, new DemandeRestitution("JPC", "2026RJ0012", "essai"));
Exiger(ecranDemo is { Mode: "demo", Greffe: null } && ecranDemo.Compteurs.Length > 0 && ecranDemoRendu is { Mode: "demo", Echec: true },
    $"Ecran : sans RIGV3_SERVEUR, des compteurs de demonstration ({ecranDemo.Compteurs.Length}) en lecture seule (« {ecranDemoRendu.Message} »)");

// CA07 : copie anonymisee d'une vraie base de greffe, resultats archives avec le rapport de la beta.
var cheminRapport = Path.Combine(AppContext.BaseDirectory, "numerotation-charge.json");
rapport["base"] = b.Nom;
rapport["le"] = DateTime.Now.ToString("s");
await File.WriteAllTextAsync(cheminRapport, JsonSerializer.Serialize(rapport, new JsonSerializerOptions { WriteIndented = true }));
Exiger(File.Exists(cheminRapport), $"NUM-008 CA07 (releve) : les resultats de la charge sont ecrits dans {cheminRapport}");
Reste("NUM-008 CA07 : rejouer cette charge sur une copie anonymisee d'une base de greffe et l'archiver avec le rapport de la beta (SOC-022) -- " +
      "aucune copie anonymisee n'existe sur ce poste");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "NumerotationCompteurCheck : tout est VERT."
    : $"NumerotationCompteurCheck : {echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Un compteur de depart, recopie de RIG_DEV.dbo.NUMERO_METIER (2026-11-20).</summary>
sealed record Fixture(string Type, short Annee, short Jour, string Structure, byte Codage, string? Lettre, string? Sep, short Taille,
                      int Debut, int? Chrono, bool Gestion, string? Redir = null, string? Greffe = null, string? Table = null,
                      string? Colonne = null, string? Libelle = null);
