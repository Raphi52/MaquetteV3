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
using RigV3.Serveur.Socle.Greffes;
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
//  - le raccord avec le catalogue des greffes (lot 8) : deux bases de greffe jetables, A et B, au
//    catalogue de la base commune ; un agent n'est connu que de la base de SON greffe.
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
string[] nomsGreffes;
await using (var b = await BaseJetable.CreerAsync("SocleIdentiteCheck"))
await using (var baseA = await BaseJetable.CreerAsync("SocleIdentiteGreffeA"))
await using (var baseB = await BaseJetable.CreerAsync("SocleIdentiteGreffeB"))
{
    nomBase = b.Nom;
    nomsGreffes = [baseA.Nom, baseB.Nom];
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
        => await ExecuterSur(b.Connexion, sql, parametres);
    async Task ExecuterSur(string connexion, string sql, params (string Nom, object Valeur)[] parametres)
    {
        await using var cn = new SqlConnection(connexion);
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

    // --- Raccord (lot 8) : deux greffes au catalogue de la base commune (b), chacun avec SA base ------
    // Un agent par greffe, connu de sa base seulement ; un compte desactive dans A. La base commune garde
    // les comptes ci-dessus (JDUPONT...) : ceux des routes communes, inconnus des deux greffes.
    const string GreffeA = "9201", GreffeB = "9202", GreffeMuet = "9203", GreffeAbsent = "9299";
    foreach (var cs in new[] { baseA.Connexion, baseB.Connexion })
        await Migrateur.AppliquerAsync(cs, migrations);
    const string ComptesDuGreffe = """
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'GRF', N'Service du greffe');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('GREFFIER', 'Greffier');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT v.Compte, @nom, 'Agent', LEFT(v.Compte, 16), 'M.', @greffe, s.Id, v.Desactive
        FROM (VALUES (@admis, 0), (@desactive, 1)) v (Compte, Desactive) CROSS JOIN soc.Service s WHERE v.Compte <> '';
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'GREFFIER' FROM soc.Compte;
        """;
    await ExecuterSur(baseA.Connexion, ComptesDuGreffe, ("@greffe", GreffeA), ("@nom", "GreffeA"), ("@admis", "AGENT.A"), ("@desactive", "DESACTIVE.A"));
    await ExecuterSur(baseB.Connexion, ComptesDuGreffe, ("@greffe", GreffeB), ("@nom", "GreffeB"), ("@admis", "AGENT.B"), ("@desactive", ""));
    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    var authentificationGreffes = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentificationGreffes.Remove("Data Source");             // le serveur et la base viennent du catalogue
    var baseMuette = $"{BaseJetable.Prefixe}inexistante_{Guid.NewGuid():N}";   // jamais creee
    foreach (var (code, nom) in new[] { (GreffeA, baseA.Nom), (GreffeB, baseB.Nom), (GreffeMuet, baseMuette) })
        await Executer("""
            INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe)
            VALUES (@c, @n, @s, @v2, @v3, 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());
            """, ("@c", code), ("@n", $"Greffe {code} du banc"), ("@s", serveurBancs.DataSource), ("@v2", "RIG_BANC_" + code), ("@v3", nom));

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
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentificationGreffes.ConnectionString;
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
            var identite = reponse.IsSuccessStatusCode && route.StartsWith(RoutesIdentite.Identite, StringComparison.Ordinal)
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

        // --- Raccord (lot 8) : le compte se cherche dans la base DU GREFFE choisi, comme la V2 ------------
        // (Utilisateur.GetUtilisateurCourant(CodeGreffeRig), Utilisateur.cs:424-432).
        var aSurA = await Ouvrir("agent.a", RoutesIdentite.Pour(GreffeA));
        var aSurB = await Ouvrir("agent.a", RoutesIdentite.Pour(GreffeB));
        var bSurB = await Ouvrir("agent.b", RoutesIdentite.Pour(GreffeB));
        var bSurA = await Ouvrir("agent.b", RoutesIdentite.Pour(GreffeA));
        Exiger(aSurA.Identite is { Admis: true, Agent: { CompteWindows: "AGENT.A", Nom: "GreffeA", Service: "Service du greffe" } },
            $"Raccord (3) : l'agent du greffe A ouvre le greffe A, reconnu dans la base de A (nom « {aSurA.Identite?.Agent?.Nom} ») ({aSurA.Brut})");
        Exiger(aSurB.Code == HttpStatusCode.OK && aSurB.Identite is { Admis: false, Statut: StatutsIdentite.Inconnu, Agent: null }
               && aSurB.Identite.Message == "L'utilisateur 'agent.a' n'a pas été défini dans RIG.",
            $"Raccord (3) : l'agent connu du greffe A seulement est refuse sur le greffe B, comme un compte inconnu de la V2 ({aSurB.Brut})");
        Exiger(bSurB.Identite is { Admis: true, Agent.Nom: "GreffeB" } && bSurA.Identite is { Statut: StatutsIdentite.Inconnu },
            $"Raccord (3) (jumeau) : l'agent du greffe B ouvre B ({bSurB.Identite?.Agent?.Nom}) et il est inconnu de A ({bSurA.Identite?.Statut})");
        var communSurA = await Ouvrir("jdupont", RoutesIdentite.Pour(GreffeA));
        Exiger(communSurA.Identite is { Statut: StatutsIdentite.Inconnu } && jdupont.Identite is { Admis: true },
            $"Raccord (3) : JDUPONT, inscrit dans la base commune seulement, est inconnu du greffe A : la base commune ne sert plus " +
            $"a juger l'agent d'un greffe ({communSurA.Identite?.Statut})");
        var desactiveA = await Ouvrir("desactive.a", RoutesIdentite.Pour(GreffeA));
        Exiger(desactiveA.Identite is { Statut: StatutsIdentite.Desactive, Message: "L'utilisateur 'desactive.a' a été désactivé." },
            $"Raccord (3) : les controles de la V2 jouent sur la base du greffe -- un compte desactive dans A y est refuse pour ce motif ({desactiveA.Brut})");
        var absentId = await Ouvrir("agent.a", RoutesIdentite.Pour(GreffeAbsent));
        var muetId = await Ouvrir("agent.a", RoutesIdentite.Pour(GreffeMuet));
        var anonymeId = await Ouvrir(null, RoutesIdentite.Pour(GreffeA));
        Exiger(absentId.Code == HttpStatusCode.NotFound && absentId.Brut.Contains(GreffeAbsent)
               && muetId.Code == HttpStatusCode.ServiceUnavailable && muetId.Brut.Contains(baseMuette)
               && anonymeId.Code == HttpStatusCode.Unauthorized,
            $"Raccord (2) : identite sur un greffe absent du catalogue -> 404, sur un greffe dont la base ne repond pas -> 503 (la base nommee, " +
            $"aucun repli sur la base commune), sans session -> 401 ({(int)absentId.Code}, {(int)muetId.Code}, {(int)anonymeId.Code})");

        // La politique AgentAdmis sur une route de donnees d'un greffe : elle juge l'agent dans CE greffe.
        var routeA = ModuleDonneesDeBanc.Pour(GreffeA);
        var avantGreffe = appels.Nombre;
        var donneesA = await Ouvrir("agent.a", routeA);
        var apresAdmis = appels.Nombre;
        var donneesAsurB = await Ouvrir("agent.a", ModuleDonneesDeBanc.Pour(GreffeB));
        var donneesJdupontA = await Ouvrir("jdupont", routeA);
        var donneesAnonymeA = await Ouvrir(null, routeA);
        var donneesAbsent = await Ouvrir("agent.a", ModuleDonneesDeBanc.Pour(GreffeAbsent));
        var donneesMuet = await Ouvrir("agent.a", ModuleDonneesDeBanc.Pour(GreffeMuet));
        Exiger(donneesA.Code == HttpStatusCode.OK && apresAdmis == avantGreffe + 1,
            $"Raccord (3) (jumeau) : une route {ModuleDonneesDeBanc.RouteDuGreffe} sert l'agent admis de son greffe ({(int)donneesA.Code})");
        Exiger(donneesAsurB.Code == HttpStatusCode.Forbidden && donneesJdupontA.Code == HttpStatusCode.Forbidden
               && donneesAnonymeA.Code == HttpStatusCode.Unauthorized && appels.Nombre == apresAdmis,
            $"Raccord (3) : la meme route refuse l'agent de A sur le greffe B (403) et le compte de la base commune sur A (403), " +
            $"demande la session (401) ; elle n'a pas tourne ({(int)donneesAsurB.Code}, {(int)donneesJdupontA.Code}, {(int)donneesAnonymeA.Code})");
        Exiger(donneesAbsent.Code == HttpStatusCode.NotFound && donneesMuet.Code == HttpStatusCode.ServiceUnavailable
               && donneesMuet.Brut.Contains(baseMuette) && appels.Nombre == apresAdmis,
            $"Raccord (2) : la politique rend 404 pour un greffe absent du catalogue et 503 pour une base de greffe muette, " +
            $"au lieu d'un 403 ; la route n'a pas tourne ({(int)donneesAbsent.Code}, {(int)donneesMuet.Code})");
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
    // L'ecran ouvre un greffe (lot 10) : la session est inscrite dans la base du greffe A, le seul du poste.
    // fix-ok: lot 10 -- SessionAgent.OuvrirAsync(Func<ClientServeur?>) demandait l'identite SANS greffe (route commune) ; elle
    // est remplacee par PreparerAsync, qui choisit le greffe d'abord : les quatre appels de ce banc suivent (CA01, CA03, demo, panne).
    await ExecuterSur(baseA.Connexion, """
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive)
        SELECT @compte, 'Windows', 'Session', LEFT(@compte, 16), 'M.', @greffe, s.Id, 0 FROM soc.Service s;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'GREFFIER' FROM soc.Compte WHERE CompteWindows = @compte;
        """, ("@compte", sessionInscrite), ("@greffe", GreffeA));
    await using (var app = ServeurRig.Construire(["--urls", "http://127.0.0.1:0"], s =>
    {
        Silence(s);
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = b.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentificationGreffes.ConnectionString;
    }))
    {
        await app.StartAsync();
        var adresse = new Uri(app.Urls.First());

        using (var sansSession = new HttpClient { BaseAddress = adresse })
        using (var reponse = await sansSession.GetAsync(RoutesIdentite.Identite.TrimStart('/')))
            Exiger(reponse.StatusCode == HttpStatusCode.Unauthorized && reponse.Headers.WwwAuthenticate.Any(a => a.Scheme == "Negotiate"),
                $"SOC-001 T02 : sans session Windows transmise, le serveur la demande (401 Negotiate) ({reponse.StatusCode})");

        // Le vrai chemin de l'ecran : SessionAgent -> ClientServeur (credentials de la session), sur le seul
        // greffe du poste, choisi d'office (lot 10 ; le choix lui-meme est prouve par tests/OuvertureGreffeCheck).
        var client = new ClientServeur(adresse);
        var moi = (await SessionAgent.PreparerAsync(() => client, [], GreffeA)).Identite;
        Exiger(moi is { Admis: true, Agent: { Nom: "Windows", Prenom: "Session" } } && moi.Agent.CompteWindows == sessionInscrite
               && string.Equals(moi.Compte, session, StringComparison.OrdinalIgnoreCase)
               && SessionAgent.NomAgent == "Session Windows" && SessionAgent.CodeGreffe == GreffeA,
            $"SOC-001 CA01 : la session Windows « {session} » ouvre RIG sans aucune saisie, l'entete recoit « {SessionAgent.NomAgent} » (compte inscrit « {sessionInscrite} » dans la base du greffe {SessionAgent.CodeGreffe}) ({JsonSerializer.Serialize(moi, json)})");

        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, "jdupont");
        var clientParametre = new ClientServeur(adresse);
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null);
        var ignore = await clientParametre.LireAsync<IdentiteAgent>(RoutesIdentite.Identite);
        Exiger(ignore is { Admis: true } && ignore.Agent!.CompteWindows == sessionInscrite,
            $"SOC-001 CA07 : en production, RIGV3_COMPTE=jdupont est ignore ; RIG s'ouvre sous la session « {session} » ({ignore.Agent?.CompteWindows})");

        await ExecuterSur(baseA.Connexion,
            "DELETE FROM soc.CompteRole WHERE IdCompte = (SELECT Id FROM soc.Compte WHERE CompteWindows = @c); DELETE FROM soc.Compte WHERE CompteWindows = @c;",
            ("@c", sessionInscrite));
        // Lancee sur ce greffe (« /g ») : la liste du poste ne le proposerait plus, la V2 non plus.
        var refuse = (await SessionAgent.PreparerAsync(() => new ClientServeur(adresse), ["/g" + GreffeA], GreffeA)).Identite;
        Exiger(refuse is { Admis: false, Statut: StatutsIdentite.Inconnu, Agent: null } && refuse.Message!.Contains($"'{session}'")
               && SessionAgent.NomAgent is null,
            $"SOC-001 CA03 : la meme session, retiree de la base du greffe, est refusee en la nommant ; l'entete ne recoit aucun nom ({refuse?.Message})");
        await app.StopAsync();
    }

    // --- L'ecran sans serveur, ou avec un serveur qui ne repond pas ---------------------------------
    var demo = await SessionAgent.PreparerAsync(() => null, [], GreffeA);
    Exiger(demo is { Demonstration: true, Identite: null } && SessionAgent.NomAgent is null,
        "sans RIGV3_SERVEUR, rien n'est demande : l'accueil reste sur ses comptes de demonstration");
    var panne = (await SessionAgent.PreparerAsync(() => new ClientServeur(new Uri("http://127.0.0.1:1/")), [], GreffeA)).Identite;
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

Exiger(!await BaseJetable.ExisteAsync(nomBase) && !await BaseJetable.ExisteAsync(nomsGreffes[0]) && !await BaseJetable.ExisteAsync(nomsGreffes[1]),
    $"bases jetables supprimees : {nomBase}, {string.Join(", ", nomsGreffes)}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : SOC-001 CA01 a CA08 prouves (session Windows reelle, refus de la V2 dans leur ordre, aucun mot de passe)."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Combien de fois la route de donnees du banc a tourne.</summary>
sealed class CompteurDeBanc { public int Nombre; }

/// <summary>Des routes de donnees comme celles des lots suivants : reservees a l'agent admis, commune et d'un greffe.</summary>
sealed class ModuleDonneesDeBanc(CompteurDeBanc appels) : IModuleServeur
{
    public const string Route = "/banc/donnees";

    /// <summary>Une route d'un greffe (lot 8) : son adresse porte {greffe}.</summary>
    public const string RouteDuGreffe = "/banc/{greffe}/donnees";

    public static string Pour(string greffe) => RouteDuGreffe.Replace("{greffe}", greffe);

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(Route, () => { Interlocked.Increment(ref appels.Nombre); return "donnees du greffe"; })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        routes.MapGet(RouteDuGreffe, (string greffe) => { Interlocked.Increment(ref appels.Nombre); return $"donnees du greffe {greffe}"; })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }
}
