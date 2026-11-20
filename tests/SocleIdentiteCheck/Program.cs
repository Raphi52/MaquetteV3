using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Sante;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Identite;
using RigV3Desktop.Pont;

// Banc de SOC-001 « Ouvrir RIG avec sa session Windows, sans mot de passe »
// (carnet\socle.json ; lot 2 du plan de reconstruction, 2026-11-20).
//
// Sur le vrai serveur, sa vraie base (jetable) et le vrai client de l'ecran :
//  - CA01 et CA07 par la VRAIE session Windows de qui lance le banc : le serveur
//    ecoute sur 127.0.0.1 (Kestrel), le client lui transmet la session
//    (Kerberos / NTLM), le compte est inscrit dans la base en MAJUSCULES ;
//  - CA02 a CA06 et l'ordre des refus par le changement de compte, qui n'existe
//    qu'en developpement (serveur en memoire, --environment Development) ;
//  - CA08 sur toute la base V3 (migrations du depot) et sur les sources des ecrans.
// Chaque controle cite son critere. Il lui faut « docker compose up -d ».

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// Le vrai serveur ; ses journaux d'information (une ligne par requete) noieraient le banc.
static void Silence(WebApplicationBuilder s) => s.Logging.SetMinimumLevel(LogLevel.Warning);

// La session Windows qui lance le banc, et ce qu'en retient le serveur (sans domaine).
var session = Environment.UserName;
var sessionInscrite = session.ToUpperInvariant();

string nomBase;
await using (var b = await BaseJetable.CreerAsync("SocleIdentiteCheck"))
{
    nomBase = b.Nom;
    var rapport = await Migrateur.AppliquerAsync(b.Connexion, migrations);
    Exiger(rapport.Jouees.Contains("SOC-001_comptes"),
        $"SOC-001 T01 : la migration des comptes passe sur une base neuve ({string.Join(", ", rapport.Jouees)})");

    async Task<T> Scalaire<T>(string sql)
    {
        await using var cn = new SqlConnection(b.Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
    async Task Executer(string sql, params (string Nom, object Valeur)[] parametres)
    {
        await using var cn = new SqlConnection(b.Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (nom, valeur) in parametres) cmd.Parameters.AddWithValue(nom, valeur);
        await cmd.ExecuteNonQueryAsync();
    }
    async Task<Exception?> Lever(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception e) { return e; }
    }

    // --- CA08 (base) : aucune colonne de mot de passe dans TOUTE la base V3 ----------------------
    const string ColonnesMotDePasse = """
        SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
        WHERE LOWER(c.name) LIKE '%password%' OR LOWER(c.name) LIKE '%passwd%' OR LOWER(c.name) LIKE '%pwd%'
           OR LOWER(c.name) LIKE '%motdepasse%' OR LOWER(c.name) LIKE '%mot[_]de[_]passe%' OR LOWER(c.name) LIKE '%mdp%';
        """;
    Exiger(await Scalaire<int>(ColonnesMotDePasse) == 0,
        "SOC-001 CA08 : aucune table de la V3 (toutes les migrations du depot) n'a de colonne de mot de passe");
    await Executer("CREATE TABLE dbo.PiegeDuBanc (Id int NOT NULL, UTLST_PASSWORD varchar(20) NULL, MotDePasse varchar(20) NULL);");
    var piege = await Scalaire<int>(ColonnesMotDePasse);
    await Executer("DROP TABLE dbo.PiegeDuBanc;");
    Exiger(piege == 2, $"SOC-001 CA08 (jumeau) : le controle voit une colonne UTLST_PASSWORD ou MotDePasse ({piege} trouvee(s) sur 2)");

    // --- CA02 (base) : le compte Windows est unique sans tenir compte des majuscules -------------
    await Executer("""
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('0101', 'RCS', N'Registre du commerce');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('GREFFIER', 'Greffier'), ('ACCUEIL', 'Accueil');
        """);
    var idService = await Scalaire<int>("SELECT Id FROM soc.Service WHERE Abrege = 'RCS';");
    async Task Compte(string compte, bool actif, bool role, bool service, string nom = "Agent", string prenom = "Banc")
    {
        await Executer("""
            INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
            VALUES (@compte, @nom, @prenom, LEFT(@compte, 16), 'M.', '0101', @service, @desactive);
            """, ("@compte", compte), ("@nom", nom), ("@prenom", prenom),
            ("@service", service ? idService : (object)DBNull.Value), ("@desactive", !actif));
        if (role)
            await Executer("INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'GREFFIER' FROM soc.Compte WHERE CompteWindows = @compte;",
                ("@compte", compte));
    }
    await Compte(sessionInscrite, actif: true, role: true, service: true, nom: "Windows", prenom: "Session");
    await Compte("JDUPONT", actif: true, role: true, service: true, nom: "Dupont", prenom: "Jean");
    await Compte("DESACTIVE", actif: false, role: true, service: true);
    await Compte("SANSDROIT", actif: true, role: false, service: true);
    await Compte("SANSSERVICE", actif: true, role: true, service: false);
    // L'ORDRE des refus de la V2 : chaque compte echoue a DEUX controles ; le premier doit gagner.
    await Compte("DESACT_SANSDROIT", actif: false, role: false, service: true);
    await Compte("DESACT_SANSSERV", actif: false, role: true, service: false);
    await Compte("SANSDROIT_SANSSERV", actif: true, role: false, service: false);

    var doublon = await Lever(() => Compte("jdupont", actif: true, role: false, service: true));
    Exiger(doublon is SqlException { Number: 2627 or 2601 },
        $"SOC-001 CA02 : « jdupont » est refuse en base quand « JDUPONT » existe (unicite sans majuscules) ({doublon?.Message})");

    async Task<long> LignesDeLaBase() => await Scalaire<long>("""
        DECLARE @sql nvarchar(max) = N'SELECT CAST(0 AS bigint)';
        SELECT @sql += N' + (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N')'
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id;
        EXEC sp_executesql @sql;
        """);

    // --- Le serveur en DEVELOPPEMENT, en memoire : le changement de compte y est permis ------------
    var appels = new CompteurDeBanc();
    await using (var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        Silence(s);
        s.WebHost.UseTestServer();
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = b.Connexion;
    }, [.. ServeurRig.Decouvrir(typeof(ServeurRig).Assembly), new ModuleDonneesDeBanc(appels)]))
    {
        await app.StartAsync();
        var http = app.GetTestClient();

        async Task<(HttpStatusCode Code, IdentiteAgent? Identite, string Brut)> Ouvrir(string? compte, string route = RoutesIdentite.Identite)
        {
            using var demande = new HttpRequestMessage(HttpMethod.Get, route);
            if (compte is not null) demande.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
            using var reponse = await http.SendAsync(demande);
            var brut = await reponse.Content.ReadAsStringAsync();
            var identite = reponse.IsSuccessStatusCode && route == RoutesIdentite.Identite
                ? JsonSerializer.Deserialize<IdentiteAgent>(brut, json) : null;
            return (reponse.StatusCode, identite, brut);
        }

        using (var sante = await http.GetAsync(RoutesSante.Sante))
            Exiger(sante.StatusCode == HttpStatusCode.OK,
                "l'authentification Windows du serveur ne casse pas les routes ouvertes : /sante repond 200 sur le serveur en memoire");

        // CA02 : session « jdupont », compte « JDUPONT ».
        var jdupont = await Ouvrir("jdupont");
        Exiger(jdupont.Code == HttpStatusCode.OK && jdupont.Identite is { Admis: true, Agent: { CompteWindows: "JDUPONT", Nom: "Dupont", Prenom: "Jean" } }
               && jdupont.Identite.Compte == "jdupont",
            $"SOC-001 CA02 : la session « jdupont » est reconnue sur le compte « JDUPONT », comme la V2 ({jdupont.Brut})");
        var domaine = await Ouvrir(@"AMITEL\JDupont");
        Exiger(domaine.Identite is { Admis: true, Agent.CompteWindows: "JDUPONT", Compte: "JDupont" },
            $"SOC-001 CA02 : « AMITEL\\JDupont » est reconnu sans son domaine, comme Environment.UserName dans la V2 ({domaine.Brut})");
        Exiger(jdupont.Identite is { Agent: { Service: "Registre du commerce", EstAmitel: false } } && jdupont.Identite.Message is null
               && !jdupont.Brut.Contains("passw", StringComparison.OrdinalIgnoreCase) && !jdupont.Brut.Contains("motDePasse", StringComparison.OrdinalIgnoreCase),
            "SOC-001 CA01 : l'agent admis recoit son nom et son service, aucun mot de passe");

        // CA03 : compte inconnu ; rien n'est ecrit.
        var avant = await LignesDeLaBase();
        var inconnu = await Ouvrir("personne");
        var long36 = await Ouvrir(new string('J', 36));
        var apres = await LignesDeLaBase();
        Exiger(inconnu.Code == HttpStatusCode.OK && inconnu.Identite is { Admis: false, Statut: StatutsIdentite.Inconnu, Agent: null }
               && inconnu.Identite.Message == "L'utilisateur 'personne' n'a pas été défini dans RIG."
               && inconnu.Identite.Assistance == "Merci de contacter la hotline d'Amitel pour signaler le problème.",
            $"SOC-001 CA03 : un compte inconnu est refuse, le message nomme le compte et renvoie vers l'assistance d'Amitel ({inconnu.Brut})");
        Exiger(long36.Identite is { Statut: StatutsIdentite.Inconnu },
            "SOC-001 CA03 : un compte plus long que la colonne (36 caracteres) est inconnu, jamais tronque sur un autre");
        Exiger(avant == apres, $"SOC-001 CA03 : aucune donnee n'est ecrite par un refus ({avant} lignes avant, {apres} apres)");

        // CA03 (suite) : une route de donnees protegee ne s'execute pas pour un compte refuse.
        var donneesAdmis = await Ouvrir("jdupont", ModuleDonneesDeBanc.Route);
        var avantRefus = appels.Nombre;
        var donneesInconnu = await Ouvrir("personne", ModuleDonneesDeBanc.Route);
        var donneesDesactive = await Ouvrir("desactive", ModuleDonneesDeBanc.Route);
        var donneesAnonyme = await Ouvrir(null, ModuleDonneesDeBanc.Route);
        Exiger(donneesAdmis.Code == HttpStatusCode.OK && avantRefus == 1,
            $"SOC-001 CA03 (jumeau) : une route .RequireAuthorization(PolitiquesSocle.AgentAdmis) sert l'agent admis ({donneesAdmis.Code})");
        Exiger(donneesInconnu.Code == HttpStatusCode.Forbidden && donneesDesactive.Code == HttpStatusCode.Forbidden
               && donneesAnonyme.Code == HttpStatusCode.Unauthorized && appels.Nombre == avantRefus,
            $"SOC-001 CA03 : un compte refuse n'obtient aucune donnee (403 inconnu, 403 desactive, 401 sans session ; la route n'a pas tourne) ({donneesInconnu.Code}, {donneesDesactive.Code}, {donneesAnonyme.Code})");

        // CA04, CA05, CA06 : chaque refus et son message (textes de la V2).
        var desactive = await Ouvrir("desactive");
        Exiger(desactive.Identite is { Statut: StatutsIdentite.Desactive, Agent: null, Message: "L'utilisateur 'desactive' a été désactivé." },
            $"SOC-001 CA04 : un compte desactive est refuse avec un message qui le dit ({desactive.Brut})");
        var sansDroit = await Ouvrir("sansdroit");
        Exiger(sansDroit.Identite is { Statut: StatutsIdentite.SansDroit, Agent: null, Message: "Aucune habilitation n'a été définie pour l'utilisateur 'sansdroit'." },
            $"SOC-001 CA05 : un compte actif sans aucun role est refuse, « aucune habilitation » ({sansDroit.Brut})");
        var sansService = await Ouvrir("sansservice");
        Exiger(sansService.Identite is { Statut: StatutsIdentite.SansService, Agent: null, Message: "Aucun service n'est rattaché à l'utilisateur 'sansservice'.",
                                         Assistance: "Merci de signaler le problème à la hotline d'AMITEL." },
            $"SOC-001 CA06 : un compte actif sans service est refuse, « aucun service rattache » ({sansService.Brut})");

        // L'ordre de la V2 : inconnu, sans droit, desactive, sans service (RigConsoleAccueil.cs:113-133).
        var ordre = new[] { "DESACT_SANSDROIT", "DESACT_SANSSERV", "SANSDROIT_SANSSERV" };
        var statuts = new List<string>();
        foreach (var c in ordre) statuts.Add((await Ouvrir(c)).Identite?.Statut ?? "?");
        Exiger(statuts.SequenceEqual([StatutsIdentite.SansDroit, StatutsIdentite.Desactive, StatutsIdentite.SansDroit]),
            $"SOC-001 T02 : les refus suivent l'ordre de la V2 -- desactive+sans role = sans droit, desactive+sans service = desactive, sans role+sans service = sans droit ({string.Join(", ", statuts)})");

        // CA07 (jumeau) : en developpement, le parametre du client designe bien un autre compte.
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, "jdupont");
        var clientDev = new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler());
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null);
        var parClientDev = await clientDev.LireAsync<IdentiteAgent>(RoutesIdentite.Identite);
        Exiger(parClientDev is { Admis: true, Agent.CompteWindows: "JDUPONT" },
            "SOC-001 CA07 (jumeau) : en developpement, RIGV3_COMPTE=jdupont ouvre RIG sous ce compte (le « utilisateur= » de la V2)");
        await app.StopAsync();
    }

    // --- Le serveur de PRODUCTION (environnement par defaut), en memoire ----------------------------
    await using (var app = ServeurRig.Construire([], s =>
    {
        Silence(s);
        s.WebHost.UseTestServer();
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = b.Connexion;
    }))
    {
        await app.StartAsync();
        using var demande = new HttpRequestMessage(HttpMethod.Get, RoutesIdentite.Identite);
        demande.Headers.Add(RoutesIdentite.EnTeteCompte, "jdupont");
        using var reponse = await app.GetTestClient().SendAsync(demande);
        Exiger(reponse.StatusCode == HttpStatusCode.Unauthorized
               && reponse.Headers.WwwAuthenticate.Any(a => a.Scheme == "Negotiate"),
            $"SOC-001 CA07 : en production, l'en-tete {RoutesIdentite.EnTeteCompte}: jdupont n'ouvre rien ; seule une session Windows est demandee (401 Negotiate, {reponse.StatusCode})");
        await app.StopAsync();
    }

    // --- La VRAIE session Windows : serveur de production sur 127.0.0.1 (Kestrel) -------------------
    await using (var app = ServeurRig.Construire(["--urls", "http://127.0.0.1:0"], s =>
    {
        Silence(s);
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = b.Connexion;
    }))
    {
        await app.StartAsync();
        var adresse = new Uri(app.Urls.First());

        using (var sansSession = new HttpClient { BaseAddress = adresse })
        using (var reponse = await sansSession.GetAsync(RoutesIdentite.Identite.TrimStart('/')))
            Exiger(reponse.StatusCode == HttpStatusCode.Unauthorized && reponse.Headers.WwwAuthenticate.Any(a => a.Scheme == "Negotiate"),
                $"SOC-001 T02 : sans session Windows transmise, le serveur la demande (401 Negotiate) ({reponse.StatusCode})");

        // Le vrai chemin de l'ecran : SessionAgent -> ClientServeur (credentials de la session).
        var client = new ClientServeur(adresse);
        var moi = await SessionAgent.OuvrirAsync(() => client);
        Exiger(moi is { Admis: true, Agent: { Nom: "Windows", Prenom: "Session" } } && moi.Agent.CompteWindows == sessionInscrite
               && string.Equals(moi.Compte, session, StringComparison.OrdinalIgnoreCase)
               && SessionAgent.NomAgent == "Session Windows",
            $"SOC-001 CA01 : la session Windows « {session} » ouvre RIG sans aucune saisie, l'entete recoit « {SessionAgent.NomAgent} » (compte inscrit « {sessionInscrite} ») ({JsonSerializer.Serialize(moi, json)})");

        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, "jdupont");
        var clientParametre = new ClientServeur(adresse);
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null);
        var ignore = await clientParametre.LireAsync<IdentiteAgent>(RoutesIdentite.Identite);
        Exiger(ignore is { Admis: true } && ignore.Agent!.CompteWindows == sessionInscrite,
            $"SOC-001 CA07 : en production, RIGV3_COMPTE=jdupont est ignore ; RIG s'ouvre sous la session « {session} » ({ignore.Agent?.CompteWindows})");

        await Executer("DELETE FROM soc.CompteRole WHERE IdCompte = (SELECT Id FROM soc.Compte WHERE CompteWindows = @c); DELETE FROM soc.Compte WHERE CompteWindows = @c;",
            ("@c", sessionInscrite));
        var refuse = await SessionAgent.OuvrirAsync(() => new ClientServeur(adresse));
        Exiger(refuse is { Admis: false, Statut: StatutsIdentite.Inconnu, Agent: null } && refuse.Message!.Contains($"'{session}'")
               && SessionAgent.NomAgent is null,
            $"SOC-001 CA03 : la meme session, retiree de la base, est refusee en la nommant ; l'entete ne recoit aucun nom ({refuse?.Message})");
        await app.StopAsync();
    }

    // --- L'ecran sans serveur, ou avec un serveur qui ne repond pas ---------------------------------
    var demo = await SessionAgent.OuvrirAsync(() => null);
    Exiger(demo is null && SessionAgent.NomAgent is null,
        "sans RIGV3_SERVEUR, rien n'est demande : l'accueil reste sur ses comptes de demonstration");
    var panne = await SessionAgent.OuvrirAsync(() => new ClientServeur(new Uri("http://127.0.0.1:1/")));
    Exiger(panne is { Admis: false, Statut: SessionAgent.StatutServeurInjoignable } && panne.Message!.Contains("127.0.0.1:1"),
        $"un serveur qui ne repond pas est un refus d'ouvrir qui dit la cause, pas un retour discret a la demonstration ({panne?.Message})");
}

// --- CA08 (ecrans) : aucun ecran ne demande de mot de passe --------------------------------------------
var champMotDePasse = new Regex("""type\s*=\s*\{?\s*["'`]password["'`]|<PasswordBox\b""", RegexOptions.IgnoreCase);
Exiger(champMotDePasse.IsMatch("""<input type="password" />""") && champMotDePasse.IsMatch("<input type={'password'} />")
       && champMotDePasse.IsMatch("<PasswordBox x:Name=\"Mdp\"/>") && !champMotDePasse.IsMatch("""<input type="text" />"""),
    "SOC-001 CA08 (jumeau) : le controle reconnait un champ de mot de passe HTML, React ou WPF");
var sources = Directory.EnumerateFiles(Path.Combine(racine, "web", "src"), "*.*", SearchOption.AllDirectories)
    .Where(f => (f.EndsWith(".tsx") || f.EndsWith(".ts")) && !f.Contains(".test."))
    .Concat(Directory.EnumerateFiles(racine, "*.xaml", SearchOption.TopDirectoryOnly))
    .Concat(Directory.EnumerateFiles(Path.Combine(racine, "wwwroot"), "*.html", SearchOption.TopDirectoryOnly))
    .ToList();
var fautifs = sources.Where(f => champMotDePasse.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(racine, f)).ToList();
Exiger(sources.Count > 50 && fautifs.Count == 0,
    $"SOC-001 CA08 : aucun ecran ne demande de mot de passe ({sources.Count} sources d'ecran lues{(fautifs.Count > 0 ? " ; fautifs : " + string.Join(", ", fautifs) : "")})");

Exiger(!await BaseJetable.ExisteAsync(nomBase), $"base jetable supprimee : {nomBase}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : SOC-001 CA01 a CA08 prouves (session Windows reelle, refus de la V2 dans leur ordre, aucun mot de passe)."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Combien de fois la route de donnees du banc a tourne.</summary>
sealed class CompteurDeBanc { public int Nombre; }

/// <summary>Une route de donnees comme celles des lots suivants : reservee a l'agent admis.</summary>
sealed class ModuleDonneesDeBanc(CompteurDeBanc appels) : IModuleServeur
{
    public const string Route = "/banc/donnees";

    public void Brancher(IEndpointRouteBuilder routes)
        => routes.MapGet(Route, () => { Interlocked.Increment(ref appels.Nombre); return "donnees du greffe"; })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
}
