using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
// fix-ok: CS1061 SetMinimumLevel mesure par tests.ps1 -- l'extension vit dans Microsoft.Extensions.Logging.
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Sante;
using RigV3.Serveur;
using RigV3Desktop.Pont;

// Banc de l'ARCHITECTURE V3 (lot 1 du plan de reconstruction, 2026-11-20).
//
// Ce qu'il prouve, sur le vrai code et une vraie base :
//  1. le garde-fou : une base jetable ne se cree que sur le serveur local
//     (jamais SQL-DEV ni SQL-PROD) ;
//  2. la base jetable est creee, a la collation de RIG_DEV ;
//  3. le migrateur : ordre par « -- apres: » (pas par l'alphabet), lots GO,
//     schema du theme, journal, rejeu sans effet, refus d'une migration jouee
//     puis modifiee, annulation complete d'une migration en echec, refus d'une
//     dependance inconnue ou circulaire et d'un nom hors forme ;
//  4. toutes les migrations du depot (Base/migrations) passent sur une base neuve ;
//  5. le serveur demarre en memoire sur cette base : /sante repond 200 et voit
//     les migrations jouees ; le client de l'ecran (Pont/ClientServeur.cs) lit
//     la meme chose ;
//  6. les modules sont decouverts sans liste ; une route en double arrete le
//     demarrage ; base absente ou injoignable : /sante repond 503 ;
//  7. sans RIGV3_SERVEUR, l'ecran reste sur ses donnees de demonstration ;
//  8. la base jetable est supprimee.
//
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la
// racine du depot (docker-compose.yml). Sans lui, il est ROUGE, avec ce conseil.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var dossierReel = Path.Combine(racine, "Base", "migrations");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
static async Task<Exception?> Lever(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception e) { return e; }
}
static Exception? LeverSync(Action action)
{
    try { action(); return null; }
    catch (Exception e) { return e; }
}
// Le vrai serveur, en memoire ; ses journaux d'information (une ligne par requete)
// noieraient les lignes OK / ECHEC du banc : seuls les avertissements restent.
static void EnMemoire(WebApplicationBuilder s)
{
    s.WebHost.UseTestServer();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
}

// --- 1. Garde-fou du serveur des bancs ----------------------------------------
Exiger(!BaseJetable.EstServeurLocal(@"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True"),
    "garde-fou : SQL-DEV\\DEV est refuse");
Exiger(!BaseJetable.EstServeurLocal(@"Server=SQL-PROD\PROD;Database=RIG_AMIENS;Integrated Security=True"),
    "garde-fou : SQL-PROD\\PROD est refuse");
Exiger(!BaseJetable.EstServeurLocal("Server=tcp:RIGBD-REUNION,1433;Integrated Security=True"),
    "garde-fou : un serveur de greffe (RIGBD-REUNION) est refuse");
Exiger(BaseJetable.EstServeurLocal(BaseJetable.ServeurDeveloppement),
    "garde-fou (jumeau) : le serveur de docker-compose.yml (127.0.0.1,14330) est accepte");
Exiger(BaseJetable.EstServeurLocal(@"Server=localhost\SQLEXPRESS;Integrated Security=True")
       && BaseJetable.EstServeurLocal("Server=tcp:localhost,1433;Integrated Security=True")
       && BaseJetable.EstServeurLocal($"Server={Environment.MachineName};Integrated Security=True"),
    "garde-fou (jumeau) : localhost\\instance, tcp:localhost,port et le nom du poste sont acceptes");
// De bout en bout : RIGV3_SQL vers un serveur distant fait refuser la creation.
// Le nom vise est en « .invalid » (jamais resolu, RFC 2606) et non SQL-DEV lui-meme :
// si ce garde-fou venait a manquer, le banc echouerait sans jamais joindre SQL-DEV.
// Le message exige est celui du garde-fou (« Serveur refuse »), pas celui d'une
// connexion ratee : contre-epreuve du 2026-11-20, garde-fou retire, l'ancien
// controle restait vert sur le message « injoignable (SQL-DEV\DEV) ».
var variableAvant = Environment.GetEnvironmentVariable(BaseJetable.VariableServeur);
Environment.SetEnvironmentVariable(BaseJetable.VariableServeur, @"Server=sql-dev.invalid\DEV;Integrated Security=True;Connect Timeout=1");
var refusDev = await Lever(() => BaseJetable.CreerAsync("ServeurCheck"));
Environment.SetEnvironmentVariable(BaseJetable.VariableServeur, variableAvant);
Exiger(refusDev is InvalidOperationException && refusDev.Message.StartsWith(@"Serveur refuse : « sql-dev.invalid\DEV »", StringComparison.Ordinal),
    $"garde-fou : RIGV3_SQL vers un serveur distant fait refuser la creation, avant toute connexion ({refusDev?.Message})");

// --- 2. Base jetable --------------------------------------------------------------
string nomBase;
await using (var b = await BaseJetable.CreerAsync("ServeurCheck"))
{
    nomBase = b.Nom;
    Exiger(b.Nom.StartsWith(BaseJetable.Prefixe + "ServeurCheck_", StringComparison.Ordinal) && await BaseJetable.ExisteAsync(b.Nom),
        $"base jetable creee : {b.Nom}");
    await using (var cn = new SqlConnection(b.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));", cn);
        var collation = (string?)await cmd.ExecuteScalarAsync();
        Exiger(collation == BaseJetable.Collation, $"collation de la base = celle de RIG_DEV ({collation})");
    }

    // --- 3. Le migrateur, sur des scripts d'essai ------------------------------------
    var essai = Directory.CreateTempSubdirectory("rigv3-serveurcheck-").FullName;
    try
    {
        void Ecrire(string nom, string texte) => File.WriteAllText(Path.Combine(essai, nom), texte, new UTF8Encoding(false));
        Ecrire("TST-001_table-a.sql",
            "-- Story d'essai du banc.\nCREATE TABLE tst.A (Id int NOT NULL PRIMARY KEY, Libelle nvarchar(50) NOT NULL);\n" +
            "INSERT INTO tst.A (Id, Libelle) VALUES (1, N'été');\n");
        Ecrire("TST-002_vue-b.sql",
            "-- Passe apres TST-003 malgre son nom : la vue lit tst.C.\n-- apres: TST-003\n" +
            "CREATE VIEW tst.VueB AS SELECT c.Id, c.Total, a.Libelle FROM tst.C c JOIN tst.A a ON a.Id = c.Id;\n");
        Ecrire("TST-003_table-c.sql",
            "-- apres: TST-001_table-a\nCREATE TABLE tst.C (Id int NOT NULL, Total int NOT NULL);\nGO\n" +
            "CREATE PROCEDURE tst.Compter AS SELECT COUNT(*) FROM tst.A;\ngo  -- fin du lot\nINSERT INTO tst.C (Id, Total) VALUES (1, 10);\n");

        var ordre = Migrateur.Lire(essai).Select(m => m.Id).ToList();
        Exiger(ordre.SequenceEqual(["TST-001_table-a", "TST-003_table-c", "TST-002_vue-b"]),
            $"ordre : « -- apres: » passe avant l'alphabet ({string.Join(", ", ordre)})");

        var premier = await Migrateur.AppliquerAsync(b.Connexion, essai);
        var journal = await Migrateur.JoueesAsync(b.Connexion);
        Exiger(premier.Jouees.SequenceEqual(ordre) && journal.SequenceEqual(ordre),
            $"migration jouee : 3 scripts joues et inscrits au journal dans cet ordre ({string.Join(", ", journal)})");

        await using (var cn = new SqlConnection(b.Connexion))
        {
            await cn.OpenAsync();
            await using var vue = new SqlCommand("SELECT Libelle FROM tst.VueB WHERE Total = 10;", cn);
            var libelle = (string?)await vue.ExecuteScalarAsync();
            await using var proc = new SqlCommand("SELECT OBJECT_ID(N'tst.Compter', N'P');", cn);
            var procedure = await proc.ExecuteScalarAsync();
            Exiger(libelle == "été" && procedure is int,
                "lots GO : la procedure (lot a part) et la vue existent, le schema tst a ete cree, l'accent est garde");
        }

        var second = await Migrateur.AppliquerAsync(b.Connexion, essai);
        Exiger(second.Jouees.Count == 0 && second.DejaJouees.Count == 3,
            "rejeu : rien n'est rejoue, les 3 sont reconnues deja jouees");

        var cheminA = Path.Combine(essai, "TST-001_table-a.sql");
        var texteA = File.ReadAllText(cheminA);
        File.WriteAllText(cheminA, texteA.Replace("\n", "\r\n"), new UTF8Encoding(true));
        var crlf = await Lever(() => Migrateur.AppliquerAsync(b.Connexion, essai));
        Exiger(crlf is null, "empreinte (jumeau) : le meme script en CRLF et avec BOM est reconnu (reglage git du poste)");

        File.WriteAllText(cheminA, texteA + "-- retouche apres coup\n");
        var modifiee = await Lever(() => Migrateur.AppliquerAsync(b.Connexion, essai));
        Exiger(modifiee is ErreurMigration && modifiee.Message.Contains("TST-001_table-a") && modifiee.Message.Contains("ne se modifie plus"),
            $"refus : une migration jouee puis modifiee arrete l'outil ({modifiee?.Message})");
        File.WriteAllText(cheminA, texteA);

        Ecrire("TST-004_echec.sql", "-- apres: TST-002\nCREATE TABLE tst.D (Id int NOT NULL);\nGO\nSELECT * FROM tst.Inexistante;\n");
        var echec = await Lever(() => Migrateur.AppliquerAsync(b.Connexion, essai));
        bool tableD;
        await using (var cn = new SqlConnection(b.Connexion))
        {
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(N'tst.D', N'U') IS NULL THEN 0 ELSE 1 END;", cn);
            tableD = (int)(await cmd.ExecuteScalarAsync())! == 1;
        }
        var apresEchec = await Migrateur.JoueesAsync(b.Connexion);
        Exiger(echec is ErreurMigration && echec.Message.Contains("TST-004_echec.sql, lot 2/2 (ligne 4)") && !tableD
               && !apresEchec.Contains("TST-004_echec"),
            $"echec : la migration est annulee en entier (ni tst.D, ni ligne au journal) et nommee ({echec?.Message})");
        File.Delete(Path.Combine(essai, "TST-004_echec.sql"));

        Ecrire("TST-005_orpheline.sql", "-- apres: TST-999\nSELECT 1;\n");
        var inconnue = LeverSync(() => Migrateur.Lire(essai));
        Exiger(inconnue is ErreurMigration && inconnue.Message.Contains("TST-999"),
            $"refus : une dependance inconnue arrete tout avant de jouer ({inconnue?.Message})");
        File.Delete(Path.Combine(essai, "TST-005_orpheline.sql"));

        Ecrire("TST-006_poule.sql", "-- apres: TST-007\nSELECT 1;\n");
        Ecrire("TST-007_oeuf.sql", "-- apres: TST-006\nSELECT 1;\n");
        var cycle = LeverSync(() => Migrateur.Lire(essai));
        Exiger(cycle is ErreurMigration && cycle.Message.Contains("circulaires"),
            $"refus : deux migrations qui s'attendent l'une l'autre ({cycle?.Message})");
        File.Delete(Path.Combine(essai, "TST-006_poule.sql"));
        File.Delete(Path.Combine(essai, "TST-007_oeuf.sql"));

        foreach (var mauvais in new[] { "comptes.sql", "soc-001_comptes.sql", "SOC-1_comptes.sql", "SOC-001_Comptes.sql" })
        {
            Ecrire(mauvais, "SELECT 1;\n");
            var nom = LeverSync(() => Migrateur.Lire(essai));
            Exiger(nom is ErreurMigration && nom.Message.Contains(mauvais) && nom.Message.Contains("SOC-001_comptes.sql"),
                $"refus : le nom « {mauvais} » n'est pas « <STORY>_<libelle>.sql »");
            File.Delete(Path.Combine(essai, mauvais));
        }
        var noms = Path.Combine(essai, "noms");
        Directory.CreateDirectory(noms);
        foreach (var bon in new[] { "SOC-001_comptes.sql", "NUM-005_reprise.sql", "REC-019_types_de-demande2.sql" })
            File.WriteAllText(Path.Combine(noms, bon), "SELECT 1;\n");
        var acceptes = LeverSync(() => Migrateur.Lire(noms));
        Exiger(acceptes is null, "noms (jumeau) : SOC-001_comptes, NUM-005_reprise, REC-019_types_de-demande2 sont acceptes");
    }
    finally
    {
        Directory.Delete(essai, recursive: true);
    }

    // --- 4. Les migrations du depot -----------------------------------------------------
    var reelles = Migrateur.Lire(dossierReel);
    var rapportReel = await Migrateur.AppliquerAsync(b.Connexion, dossierReel);
    Exiger(rapportReel.Jouees.Count == reelles.Count && rapportReel.Absentes.Count == 3,
        $"Base/migrations : les {reelles.Count} migration(s) du depot passent sur une base neuve" +
        (reelles.Count == 0 ? " (aucune encore : le lot 1 ne pose que l'outil)" : $" ({string.Join(", ", rapportReel.Jouees)})") +
        $" ; les 3 d'essai sont signalees absentes du dossier, sans bloquer");
    var journalComplet = await Migrateur.JoueesAsync(b.Connexion);

    // --- 5. Le serveur en memoire, sur la base jetable ----------------------------------
    await using (var app = ServeurRig.Construire([], s =>
    {
        EnMemoire(s);
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = b.Connexion;
    }))
    {
        await app.StartAsync();
        var http = app.GetTestClient();
        using var reponse = await http.GetAsync(RoutesSante.Sante);
        var brut = await reponse.Content.ReadAsStringAsync();
        var etat = System.Text.Json.JsonSerializer.Deserialize<EtatSante>(brut, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Exiger(reponse.StatusCode == HttpStatusCode.OK && etat is { Etat: EtatsSante.Ok } && etat.Base == b.Nom,
            $"/sante repond 200, base jointe = la base jetable ({brut})");
        Exiger(etat is not null && etat.MigrationsJouees == journalComplet.Count && etat.DerniereMigration == journalComplet[^1],
            $"/sante voit le journal : {etat?.MigrationsJouees} migration(s), derniere {etat?.DerniereMigration}");
        Exiger(brut.Contains("\"migrationsJouees\":") && brut.Contains("\"derniereMigration\":"),
            "le JSON est en camelCase, comme le pont de la page");

        var client = new ClientServeur(new Uri("http://localhost"), app.GetTestServer().CreateHandler());
        var parClient = await client.SanteAsync();
        var parLecture = await client.LireAsync<EtatSante>(RoutesSante.Sante);
        Exiger(parClient == etat && parLecture == etat,
            "Pont/ClientServeur.cs lit la meme reponse (SanteAsync et LireAsync<EtatSante>)");
        var inconnue = await Lever(() => client.LireAsync<EtatSante>("/route-qui-n-existe-pas"));
        Exiger(inconnue is ErreurServeur { CodeHttp: 404 } && inconnue.Message.Contains("/route-qui-n-existe-pas"),
            $"ClientServeur : une route inconnue leve ErreurServeur 404 en la nommant ({inconnue?.Message})");
        await app.StopAsync();
    }
}

// --- 6. Les modules -----------------------------------------------------------------------
var decouverts = ServeurRig.Decouvrir(typeof(ServeurRig).Assembly);
Exiger(decouverts.Any(m => m.GetType().Name == "ModuleSante"),
    $"decouverte : les modules du serveur sont trouves sans liste ({string.Join(", ", decouverts.Select(m => m.GetType().Name))})");

await using (var app = ServeurRig.Construire([], EnMemoire, [.. decouverts, new ModuleEcho()]))
{
    await app.StartAsync();
    var http = app.GetTestClient();
    using var echo = await http.GetAsync("/banc/echo");
    using var sante = await http.GetAsync(RoutesSante.Sante);
    var etat = await sante.Content.ReadAsStringAsync();
    Exiger(echo.StatusCode == HttpStatusCode.OK && await echo.Content.ReadAsStringAsync() == "echo",
        "un module de plus (un fichier) publie sa route, sans rien toucher d'autre");
    Exiger(sante.StatusCode == HttpStatusCode.ServiceUnavailable && etat.Contains(EtatsSante.BaseNonConfiguree),
        $"/sante sans base configuree : 503 « {EtatsSante.BaseNonConfiguree} »");
    await app.StopAsync();
}

var doublon = LeverSync(() => ServeurRig.Construire([], EnMemoire, [.. decouverts, new ModuleDoublon()]));
Exiger(doublon is InvalidOperationException && doublon.Message.Contains("GET /sante")
       && doublon.Message.Contains("ModuleSante") && doublon.Message.Contains("ModuleDoublon"),
    $"route en double : le demarrage s'arrete en nommant la route et les deux modules ({doublon?.Message})");

await using (var app = ServeurRig.Construire([], s =>
{
    EnMemoire(s);
    s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] =
        "Server=127.0.0.1,1;Database=rigv3_absente;User ID=sa;Password=x;Connect Timeout=3;Encrypt=False";
}))
{
    await app.StartAsync();
    var client = new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler());
    var etat = await client.SanteAsync();
    Exiger(etat.Etat == EtatsSante.BaseInjoignable && etat.Base is null && !string.IsNullOrEmpty(etat.Detail),
        $"/sante sur une base injoignable : 503 « {etat.Etat} », avec la cause");
    await app.StopAsync();
}

// --- 7. Sans RIGV3_SERVEUR : les donnees de demonstration ---------------------------------
Environment.SetEnvironmentVariable(ClientServeur.Variable, null);
Exiger(ClientServeur.Actif is null, "sans RIGV3_SERVEUR, ClientServeur.Actif est null : l'ecran garde JeuDemo");
Exiger(ClientServeur.Depuis(null) is null && ClientServeur.Depuis("   ") is null,
    "RIGV3_SERVEUR vide = pas de serveur");
Exiger(ClientServeur.Depuis("http://127.0.0.1:5080")?.Adresse.AbsoluteUri == "http://127.0.0.1:5080/"
       && ClientServeur.Depuis("https://rig.amitel.fr/v3")?.Adresse.AbsoluteUri == "https://rig.amitel.fr/v3/",
    "RIGV3_SERVEUR=http(s)://... branche l'ecran sur ce serveur (racine avec chemin comprise)");
foreach (var illisible in new[] { "pas une adresse", "ftp://serveur/", "127.0.0.1:5080" })
{
    var refus = LeverSync(() => ClientServeur.Depuis(illisible));
    Exiger(refus is InvalidOperationException && refus.Message.Contains(ClientServeur.Variable),
        $"RIGV3_SERVEUR=« {illisible} » est une erreur nommee, pas un retour discret a la demonstration");
}

// --- 8. Apres le banc : plus de base --------------------------------------------------------
Exiger(!await BaseJetable.ExisteAsync(nomBase), $"base jetable supprimee : {nomBase}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : base jetable creee, migrations jouees, /sante repond, base supprimee."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Un module du banc : prouve qu'un fichier de plus publie sa route.</summary>
sealed class ModuleEcho : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes) => routes.MapGet("/banc/echo", () => "echo");
}

/// <summary>Un module du banc qui reprend /sante : le demarrage doit le refuser.</summary>
sealed class ModuleDoublon : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes) => routes.MapGet("/sante/", () => Results.Ok());
}
