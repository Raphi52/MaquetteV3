using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;
using RigV3.Serveur.Socle.Supervision;
using RigV3Desktop.Pont;

// Banc du LOT 2 de la phase 2 du plan de reconstruction (2026-11-21) : SOC-017 (retrouver une erreur technique sans
// aller sur les serveurs) et SOC-011 (surveiller la partie serveur et ses traitements depuis Amitel).
//
// Un controle par critere du carnet (C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json), qui
// cite son id ; tout refus a son jumeau accepte. Le VRAI serveur (ServeurRig.Construire), en memoire, sur ses bases
// jetables du SQL Server LOCAL (docker compose up -d) :
//   - la base commune, avec les 40 greffes exploites de la copie d'essai de COMMUN_RIG.dbo.GREFFE (celle du banc
//     SocleGreffesCheck), tous poses sur l'instance locale ;
//   - trois bases V3 de greffe (Amiens 8002 et Vienne 3802 a jour, Saint-Denis de la Reunion 9741 sans migration),
//     une base « V2 » pour Le Puy 4302 (echecs de numerotation) ;
//   - la session d'evenements des echecs, creee par le bloc MEME de Base\v2\bascule.sql (nom propre au banc) ;
//   - une seconde base commune pour un serveur en production qui tourne seul (publication a intervalle regulier).
// Il supprime tout en sortie (bases, session). Rien n'est lu ni ecrit sur SQL-DEV ni SQL-PROD.
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
const string Amitel = @"AMITEL\banc.amitel";   // admis, marque Amitel : l'exploitation
const string Agent = @"AMITEL\banc.agent";     // admis, agent de greffe : n'a que l'identifiant de suivi
const string ComptesDuBanc = """
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('9999', 'EXPL', N'Exploitation');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleSupervision');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
    SELECT v.Compte, v.Nom, 'Banc', v.Abrege, 'M.', '9999', s.Id, 0, v.Amitel, 0
    FROM (VALUES ('banc.amitel', 'EXPLOITANT', 'BEXP', 1), ('banc.agent', 'AGENT', 'BAGT', 0)) v (Compte, Nom, Abrege, Amitel)
    CROSS JOIN soc.Service s;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
    """;

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reste(string quoi) => Console.WriteLine("RESTE : " + quoi);
async Task<T> Attendre<T>(Func<Task<T>> lire, Func<T, bool> fini, double secondes = 15)
{
    var chrono = Stopwatch.StartNew();
    while (true)
    {
        var valeur = await lire();
        if (fini(valeur) || chrono.Elapsed.TotalSeconds > secondes) return valeur;
        await Task.Delay(250);
    }
}

// --- Le serveur des bancs (local) ----------------------------------------------------------------------
var serveurBancs = BaseJetable.ServeurDesBancs();
var instance = new SqlConnectionStringBuilder(serveurBancs).DataSource;
var authentification = new SqlConnectionStringBuilder(serveurBancs);
authentification.Remove("Data Source");
string Chaine(string nomBase) => new SqlConnectionStringBuilder(serveurBancs) { InitialCatalog = nomBase }.ConnectionString;
async Task Executer(string nomBase, string sql)
{
    await using var cn = new SqlConnection(Chaine(nomBase));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    await cmd.ExecuteNonQueryAsync();
}
async Task<object?> Scalaire(string nomBase, string sql)
{
    await using var cn = new SqlConnection(Chaine(nomBase));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    var v = await cmd.ExecuteScalarAsync();
    return v is DBNull ? null : v;
}
async Task<List<object?[]>> Lignes(string nomBase, string sql)
{
    await using var cn = new SqlConnection(Chaine(nomBase));
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    await using var r = await cmd.ExecuteReaderAsync();
    var lignes = new List<object?[]>();
    while (await r.ReadAsync())
    {
        var v = new object?[r.FieldCount];
        r.GetValues(v!);
        for (var i = 0; i < v.Length; i++) if (v[i] is DBNull) v[i] = null;
        lignes.Add(v);
    }
    return lignes;
}

var jeton = Guid.NewGuid().ToString("N")[..8];
var session = $"rigv3_banc_supervision_{jeton}";

await using var commune = await BaseJetable.CreerAsync("SocleSupervisionCheck");
await using var communeAuto = await BaseJetable.CreerAsync("SSAuto");
await using var communV2 = await BaseJetable.CreerAsync("SSCommunV2");
await using var v2Puy = await BaseJetable.CreerAsync("SSV2Puy");
await using var v3Amiens = await BaseJetable.CreerAsync("SSV3Amiens");
await using var v3Vienne = await BaseJetable.CreerAsync("SSV3Vienne");
await using var v3Reunion = await BaseJetable.CreerAsync("SSV3Reunion");
try
{
    Console.WriteLine("--- Mise en place");
    foreach (var b in new[] { commune, communeAuto, v3Amiens, v3Vienne })
        await Migrateur.AppliquerAsync(b.Connexion, migrations);
    await Executer(commune.Nom, ComptesDuBanc);
    await Executer(communeAuto.Nom, ComptesDuBanc);
    await Executer(communV2.Nom, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "commun_rig_greffe.sql")));
    await using (var cnV2 = new SqlConnection(communV2.Connexion))
    await using (var cnC = new SqlConnection(commune.Connexion))
    {
        await cnV2.OpenAsync();
        await cnC.OpenAsync();
        var repris = await RepriseCatalogue.ReprendreAsync(cnV2, cnC, "banc SocleSupervisionCheck");
        Exiger(repris == 40, $"mise en place : les 40 greffes exploites de la copie de COMMUN_RIG sont au catalogue ({repris})");
    }
    // Toutes les bases des greffes sur l'instance du banc ; Le Puy a pour base V2 celle ou le banc provoque des echecs.
    await Executer(commune.Nom,
        $"UPDATE soc.Greffe SET Serveur = N'{instance}', ModifiePar = 'banc', ModifieLe = SYSDATETIME(); " +
        $"UPDATE soc.Greffe SET BaseV2 = N'{v2Puy.Nom}', ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = '4302';");

    // La session des echecs de numerotation : le bloc de Base\v2\bascule.sql lui-meme, pas une copie.
    var bascule = File.ReadAllText(Path.Combine(racine, "Base", "v2", "bascule.sql"));
    var debutSession = bascule.IndexOf("IF NOT EXISTS (SELECT 1 FROM sys.server_event_sessions", StringComparison.Ordinal);
    var finSession = bascule.IndexOf("\nGO", debutSession, StringComparison.Ordinal);
    await Executer("master", bascule[debutSession..finSession].Replace("$(SessionEchecs)", session));

    var traitement = new TraitementDuBanc();
    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        // La console du banc ne recopie pas les pannes qu'il provoque expres (elles vont au journal technique, qu'il lit).
        s.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Critical);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
        s.Configuration["Supervision:Automatique"] = "false";            // le banc joue chaque passage lui-meme
        s.Configuration["Supervision:Site"] = "BANC-APP";
        s.Configuration["Supervision:SessionEchecs"] = session;
        s.Configuration["Supervision:DelaiFileBloqueeSecondes"] = "2";
        s.Services.AddSingleton<ITraitementDeFond>(traitement);
    }, [.. ServeurRig.Decouvrir(typeof(ServeurRig).Assembly), new ModuleBanc()]);
    await app.StartAsync();
    var http = app.GetTestClient();
    var catalogue = app.Services.GetRequiredService<CatalogueGreffes>();
    var surveillant = app.Services.GetRequiredService<Surveillant>();
    var moteur = app.Services.GetRequiredService<MoteurTraitements>();
    var canal = (CanalAlerteSimule)app.Services.GetRequiredService<ICanalAlerte>();
    await catalogue.InscrireBaseV3Async("8002", v3Amiens.Nom, "banc");
    await catalogue.InscrireBaseV3Async("3802", v3Vienne.Nom, "banc");
    await catalogue.InscrireBaseV3Async("9741", v3Reunion.Nom, "banc");

    async Task<HttpResponseMessage> Envoyer(HttpMethod verbe, string adresse, object? corps = null, string? compte = Amitel)
    {
        var req = new HttpRequestMessage(verbe, adresse);
        if (corps is not null) req.Content = JsonContent.Create(corps);
        if (compte is not null) req.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        return await http.SendAsync(req);
    }
    async Task<T> Lire<T>(string adresse, string compte = Amitel)
    {
        var r = await Envoyer(HttpMethod.Get, adresse, null, compte);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }
    ClientServeur ClientDe(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    var clientAgent = ClientDe(Agent);
    var clientAmitel = ClientDe(Amitel);
    async Task<ErreurTechnique[]> Parcours(string suivi) => await Lire<ErreurTechnique[]>(RoutesSupervision.PourParcours(suivi));

    // Acces : toutes les routes du module portent une politique, lue dans le serveur lui-meme.
    var routesModule = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
        .Where(r => r.Metadata.GetMetadata<ModuleOrigine>()?.Nom == "RigV3.Serveur.Socle.Supervision.ModuleSupervision").ToList();
    var reservees = routesModule.Where(r => r.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
        .Any(a => a.Policy == PolitiquesSupervision.ExploitationAmitel)).Select(r => r.RoutePattern.RawText).ToList();
    var sansPolitique = routesModule.Where(r => !r.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any())
        .Select(r => r.RoutePattern.RawText).ToList();
    Exiger(routesModule.Count == 5 && reservees.Count == 4 && sansPolitique.Count == 0,
        $"Acces : les {routesModule.Count} routes de la supervision exigent une session, {reservees.Count} sont reservees a Amitel " +
        $"({string.Join(", ", reservees)}) ; seule la remontee des erreurs de l'ecran est ouverte a tout agent");

    // =====================================================================================================
    // SOC-017 — Retrouver une erreur technique sans aller sur les serveurs
    // =====================================================================================================
    Console.WriteLine("--- SOC-017 : journal technique");

    // CA01 : une erreur non prevue de la partie serveur, et ce que l'agent en recoit.
    var reponse = await Envoyer(HttpMethod.Get, "/banc/8002/panne", compte: Agent);
    var corpsAgent = await reponse.Content.ReadAsStringAsync();
    var suiviAgent = (await reponse.Content.ReadFromJsonAsync<SuiviErreur>())!;
    var enTete = reponse.Headers.TryGetValues(RoutesSupervision.EnTeteSuivi, out var vs) ? vs.First() : null;
    Exiger(reponse.StatusCode == HttpStatusCode.InternalServerError && Suivis.Normaliser(suiviAgent.Suivi) == suiviAgent.Suivi
           && enTete == suiviAgent.Suivi && suiviAgent.Message.Contains(suiviAgent.Suivi)
           && !corpsAgent.Contains("Panne du banc") && !corpsAgent.Contains("InvalidOperationException") && !corpsAgent.Contains(" at "),
        $"SOC-017 CA01 : l'agent recoit un 500 qui ne porte que l'identifiant de suivi ({suiviAgent.Suivi}, aussi en en-tete), " +
        "ni le message ni le type ni la pile");
    var lignesPanne = await Attendre(() => Parcours(suiviAgent.Suivi), l => l.Length >= 2);
    var grave = lignesPanne.FirstOrDefault(l => l.Niveau == NiveauxErreur.Grave);
    Exiger(grave is not null && grave.CodeGreffe == "8002" && grave.Compte == "banc.agent" && grave.Serveur == "BANC-APP"
           && grave.Programme == "GET /banc/{greffe}/panne" && grave.Origine == OriginesErreur.Serveur
           && grave.Message.Contains("Panne du banc pour le greffe 8002") && grave.Pile is { } pile && pile.Contains("ModuleBanc")
           && grave.Type == typeof(InvalidOperationException).FullName && (DateTime.UtcNow - grave.Instant).TotalMinutes < 5,
        $"SOC-017 CA01 : l'erreur est en base commune avec la date, le greffe, le compte, le serveur, le programme, le message, " +
        $"la pile et l'identifiant ({grave?.Instant:O} {grave?.CodeGreffe} {grave?.Compte} {grave?.Serveur} « {grave?.Programme} »)");

    // CA01 cote ecran (T03) : une methode du pont qui leve part au journal ; l'agent recoit l'identifiant.
    var suiviEcran = await EcranSupervision.SignalerAsync(clientAgent, "EcranBanc.Ouvrir",
        new NullReferenceException("Object reference not set to an instance of an object."), "9871");
    var ligneEcran = suiviEcran is null ? null : (await Parcours(suiviEcran.Suivi)).SingleOrDefault();
    Exiger(suiviEcran is not null && suiviEcran.Message.Contains(suiviEcran.Suivi) && ligneEcran is { Origine: OriginesErreur.Ecran, Niveau: NiveauxErreur.Grave }
           && ligneEcran.Compte == "banc.agent" && ligneEcran.CodeGreffe == "9871" && ligneEcran.Programme == "EcranBanc.Ouvrir"
           && ligneEcran.Serveur == Environment.MachineName && ligneEcran.Type == typeof(NullReferenceException).FullName,
        $"SOC-017 CA01 : une erreur de l'ecran (Papeete) est journalisee avec le compte de la SESSION et le poste ; l'agent lit « {suiviEcran?.Message} »");

    // CA02 : tous les messages du meme parcours. La 500 recue par l'ecran porte son identifiant (ErreurServeur.Suivi) ;
    // l'erreur que l'ecran signale ensuite le reprend : le serveur et l'ecran, au meme endroit.
    ErreurServeur? recue = null;
    try { await clientAgent.LireAsync<object>("/banc/8002/panne"); }
    catch (ErreurServeur e) { recue = e; }
    var suiviParcours = recue?.Suivi;
    if (recue is not null) await EcranSupervision.SignalerAsync(clientAgent, "EcranBanc.Rendre", recue, "8002");
    var parcours = suiviParcours is null ? [] : await Attendre(() => Parcours(suiviParcours), l => l.Length >= 3);
    Exiger(recue is { CodeHttp: 500 } && parcours.Length == 3
           && parcours.Select(l => (l.Origine, l.Niveau)).SequenceEqual([(OriginesErreur.Serveur, NiveauxErreur.Erreur),
                                                                        (OriginesErreur.Serveur, NiveauxErreur.Grave),
                                                                        (OriginesErreur.Ecran, NiveauxErreur.Grave)]),
        $"SOC-017 CA02 : l'identifiant {suiviParcours} ramene tout le parcours, dans l'ordre : l'erreur interceptee, la panne du serveur, " +
        $"puis l'erreur de l'ecran ({string.Join(" ; ", parcours.Select(l => $"{l.Origine}/{l.Niveau} {l.Programme}"))})");
    var vueParcours = await EcranSupervision.RechercherAsync(clientAmitel, new RechercheErreurs(suiviParcours?.ToLowerInvariant(), null, null, null, null));
    Exiger(vueParcours.Mode == "serveur" && vueParcours.Erreurs.Length == 3 && vueParcours.Message!.Contains("3 message(s)"),
        $"SOC-017 CA02 : l'ecran « Supervision » retrouve le parcours par l'identifiant dicte en minuscules ({vueParcours.Message})");

    // Le detail est reserve a Amitel (controle cle, contre-epreuve faite : docs\reconstruction\socle-supervision.md).
    var refusAgent = await Envoyer(HttpMethod.Get, RoutesSupervision.PourParcours(suiviAgent.Suivi), compte: Agent);
    var corpsRefus = await refusAgent.Content.ReadAsStringAsync();
    var refusListe = await Envoyer(HttpMethod.Get, RoutesSupervision.Erreurs + "?greffe=8002", compte: Agent);
    var sansSession = await Envoyer(HttpMethod.Get, RoutesSupervision.PourParcours(suiviAgent.Suivi), compte: null);
    var accepte = await Envoyer(HttpMethod.Get, RoutesSupervision.PourParcours(suiviAgent.Suivi), compte: Amitel);
    Exiger(refusAgent.StatusCode == HttpStatusCode.Forbidden && refusListe.StatusCode == HttpStatusCode.Forbidden
           && sansSession.StatusCode == HttpStatusCode.Unauthorized && !corpsRefus.Contains("Panne du banc")
           && accepte.StatusCode == HttpStatusCode.OK && (await accepte.Content.ReadAsStringAsync()).Contains("Panne du banc"),
        $"SOC-017 CA02 : le detail d'une erreur est refuse hors Amitel (agent de greffe {(int)refusAgent.StatusCode} et {(int)refusListe.StatusCode}, " +
        $"sans session {(int)sansSession.StatusCode}) ; jumeau : l'exploitation d'Amitel le lit ({(int)accepte.StatusCode})");
    var vueRefus = await EcranSupervision.OuvrirAsync(clientAgent);
    Exiger(vueRefus.Mode == "refuse" && vueRefus.Etat is null && vueRefus.Erreurs.Length == 0,
        $"SOC-017 CA02 : l'ecran « Supervision » d'un agent de greffe ne montre rien ({vueRefus.Mode} : {vueRefus.Message})");

    // CA03 : jamais avalee en silence. (a) A l'execution : l'erreur interceptee par le code de la route (catch + journal)
    // est dans le parcours (ci-dessus, niveau « erreur »). (b) Dans le code : aucun « catch » qui attrape tout sans relancer
    // ni journaliser, dans la partie serveur et a l'entree des ecrans (Pont/Repartiteur.cs).
    var interceptee = lignesPanne.FirstOrDefault(l => l.Niveau == NiveauxErreur.Erreur);
    Exiger(interceptee is not null && interceptee.Message.Contains("Date illisible") && interceptee.Suivi == suiviAgent.Suivi
           && interceptee.Programme.StartsWith("GET /banc/{greffe}/panne", StringComparison.Ordinal),
        $"SOC-017 CA03 : une erreur interceptee et journalisee par le code va au journal technique, dans le parcours de la requete ({interceptee?.Programme})");
    var avalees = Avalement.Chercher(Path.Combine(racine, "Serveur"), racine)
        .Concat(Avalement.Chercher(Path.Combine(racine, "Pont", "Repartiteur.cs"), racine)).ToList();
    var temoinFaute = Avalement.Dans("x.cs", "try { F(); } catch (Exception) { return null; }").Count;
    var temoinBon = Avalement.Dans("x.cs", "try { F(); } catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, \"x\"); }").Count
                  + Avalement.Dans("x.cs", "try { F(); } catch (IOException) { return null; }").Count;
    Exiger(avalees.Count == 0 && temoinFaute == 1 && temoinBon == 0,
        $"SOC-017 CA03 : recherche statique -- aucune exception avalee dans Serveur\\ ni Pont\\Repartiteur.cs " +
        $"({(avalees.Count == 0 ? "aucune" : string.Join(", ", avalees))}) ; la recherche trouve un avalement ecrit expres et laisse passer un catch journalise ou type");

    // CA04 : purge selon des durees ecrites, les graves gardees plus longtemps.
    var durees = (await Lignes(commune.Nom, "SELECT Niveau, Jours FROM soc.ConservationTechnique WHERE Journal = 'erreurs';"))
        .ToDictionary(l => (string)l[0]!, l => (int)l[1]!);
    await Executer(commune.Nom, """
        INSERT INTO soc.ErreurTechnique (Suivi, Instant, Niveau, Origine, Serveur, Programme, Message) VALUES
            ('PRG0-0001', DATEADD(day, -40,  SYSUTCDATETIME()), 'avertissement', 'serveur', 'BANC', 'purge', N'trace de 40 jours'),
            ('PRG0-0002', DATEADD(day, -40,  SYSUTCDATETIME()), 'grave',         'serveur', 'BANC', 'purge', N'grave de 40 jours'),
            ('PRG0-0003', DATEADD(day, -200, SYSUTCDATETIME()), 'erreur',        'serveur', 'BANC', 'purge', N'erreur de 200 jours'),
            ('PRG0-0004', DATEADD(day, -100, SYSUTCDATETIME()), 'erreur',        'serveur', 'BANC', 'purge', N'erreur de 100 jours'),
            ('PRG0-0005', DATEADD(day, -800, SYSUTCDATETIME()), 'grave',         'serveur', 'BANC', 'purge', N'grave de 800 jours'),
            ('PRG0-0006', DATEADD(day, -10,  SYSUTCDATETIME()), 'avertissement', 'serveur', 'BANC', 'purge', N'trace de 10 jours');
        """);
    var purge = await surveillant.PurgerAsync(DateTime.UtcNow);
    var restent = (await Lignes(commune.Nom, "SELECT Suivi FROM soc.ErreurTechnique WHERE Programme = 'purge' ORDER BY Suivi;"))
        .Select(l => (string)l[0]!).ToList();
    Exiger(durees.Count == 3 && durees["grave"] > durees["erreur"] && durees["erreur"] > durees["avertissement"]
           && purge.Erreurs == 3 && restent.SequenceEqual(["PRG0-0002", "PRG0-0004", "PRG0-0006"]),
        $"SOC-017 CA04 : durees ecrites (graves {durees.GetValueOrDefault("grave")} j, erreurs {durees.GetValueOrDefault("erreur")} j, " +
        $"traces {durees.GetValueOrDefault("avertissement")} j) ; la purge retire la trace de 40 j, l'erreur de 200 j et la grave de 800 j, " +
        $"garde la grave de 40 j, l'erreur de 100 j et la trace de 10 j (restent : {string.Join(", ", restent)})");

    // CA05 : ni mot de passe ni contenu de piece ; le compte seul comme donnee personnelle.
    var colonnes = (await Lignes(commune.Nom,
        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'soc' AND TABLE_NAME = 'ErreurTechnique' ORDER BY ORDINAL_POSITION;"))
        .Select(l => (string)l[0]!).ToList();
    var texteGrave = (grave?.Message ?? "") + (grave?.Pile ?? "");
    Exiger(grave is not null && !texteGrave.Contains(ModuleBanc.MotDePasse) && !texteGrave.Contains("Hunter2")
           && !texteGrave.Contains(ModuleBanc.Jeton) && !texteGrave.Contains(ModuleBanc.Piece[..60])
           && texteGrave.Contains("Password=***") && texteGrave.Contains("[contenu retiré : 400 caractères]")
           && colonnes.SequenceEqual(["Id", "Suivi", "Instant", "Niveau", "Origine", "CodeGreffe", "Compte", "Serveur", "Programme", "Type", "Message", "Pile"])
           && grave.Compte == "banc.agent",
        "SOC-017 CA05 : la trace ne garde ni le mot de passe, ni le jeton, ni le contenu de la piece (masques), aucune colonne ne recoit " +
        "le corps d'une requete, et le compte est le seul identifiant de personne (sans nom ni domaine)");
    Exiger(grave is not null && grave.Message.Contains("Panne du banc pour le greffe 8002 : Server=x;User ID=sa;"),
        "SOC-017 CA05 (jumeau) : le reste du message, utile au diagnostic, est garde tel quel");

    // CA06 + T05 : les 40 greffes, outre-mer compris, au meme endroit.
    var codes = (await catalogue.ListerAsync()).Select(f => f.Code).ToList();
    var suivisParGreffe = new Dictionary<string, string>();
    foreach (var code in codes)
    {
        var r = await Envoyer(HttpMethod.Get, $"/banc/{code}/panne", compte: Agent);
        suivisParGreffe[code] = (await r.Content.ReadFromJsonAsync<SuiviErreur>())!.Suivi;
    }
    var retrouves = 0;
    foreach (var code in codes)
        if ((await Lire<ErreurTechnique[]>($"{RoutesSupervision.Erreurs}?greffe={code}")).Any(l => l.Suivi == suivisParGreffe[code] && l.CodeGreffe == code))
            retrouves++;
    var tout = await Lire<ErreurTechnique[]>(RoutesSupervision.Erreurs);
    var outreMer = (await catalogue.ListerAsync()).Where(f => f.EstOutreMer).Select(f => f.Code).ToList();
    Exiger(codes.Count == 40 && retrouves == 40 && outreMer.Count == 6
           && outreMer.All(c => tout.Any(l => l.CodeGreffe == c)) && tout.Any(l => l.CodeGreffe == "8002"),
        $"SOC-017 CA06 : une erreur de chacun des {codes.Count} greffes se retrouve par son greffe ({retrouves}), et celles des 6 greffes " +
        $"d'outre-mer ({string.Join(", ", outreMer)}) sont dans la meme liste que celles de la metropole, sans passer par leur serveur");
    Reste("SOC-017 CA06 : sur les vrais serveurs, verifier qu'un ecran d'un RDS d'outre-mer joint le serveur d'Amitel (reseau des territoires) -- hors de ce poste");

    // =====================================================================================================
    // SOC-011 — Surveiller la partie serveur et ses traitements depuis Amitel
    // =====================================================================================================
    Console.WriteLine("--- SOC-011 : supervision");

    // CA01 : l'etat publie, et les etats d'un traitement.
    async Task<string> EtatDe(string nom) => (await moteur.EtatsAsync()).Single(e => e.Traitement == nom).Etat;
    await surveillant.PasserAsync();
    var publies = await Lignes(commune.Nom, "SELECT Traitement, Etat FROM soc.EtatTraitement WHERE Serveur = 'BANC-APP' ORDER BY Traitement;");
    var historique1 = Convert.ToInt32(await Scalaire(commune.Nom, "SELECT COUNT(*) FROM soc.EtatTraitementHistorique WHERE Serveur = 'BANC-APP';"));
    Exiger(publies.Count == 2 && publies.Any(l => (string)l[0]! == TraitementsSupervision.PartieServeur && (string)l[1]! == EtatsTraitement.EnMarche)
           && publies.Any(l => (string)l[0]! == "Banc") && historique1 == 2,
        $"SOC-011 CA01 : la partie serveur publie son etat et celui de chacun de ses traitements, et l'historique le garde " +
        $"({string.Join(", ", publies.Select(l => $"{l[0]} : {l[1]}"))})");

    var etatsVus = new List<string> { await EtatDe("Banc") };
    traitement.Bloquer = new TaskCompletionSource();
    await moteur.DeposerAsync("Banc", "8002", "ok-1");
    etatsVus.Add(await EtatDe("Banc"));
    var passage = moteur.PasserAsync("Banc");
    etatsVus.Add(await Attendre(() => EtatDe("Banc"), e => e == EtatsTraitement.TravauxEnCours, 10));
    traitement.Bloquer.SetResult();
    traitement.Bloquer = null;
    await passage;
    etatsVus.Add(await EtatDe("Banc"));
    Exiger(etatsVus.SequenceEqual([EtatsTraitement.EnMarche, EtatsTraitement.EnAttente, EtatsTraitement.TravauxEnCours, EtatsTraitement.EnMarche]),
        $"SOC-011 CA01 : un traitement passe par « en marche », « en attente », « travaux en cours » ({string.Join(" -> ", etatsVus)}) ; " +
        "« en erreur » et « arrete » sont prouves par CA02 et CA04");

    // CA01 : a intervalle regulier, sans le banc -- un serveur en PRODUCTION qui tourne seul (1 s), sur sa propre base commune.
    {
        await using var auto = ServeurRig.Construire(["--environment", "Production"], s =>
        {
            s.WebHost.UseTestServer();
            s.Logging.SetMinimumLevel(LogLevel.Warning);
            s.Logging.AddFilter<Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider>(null, LogLevel.Critical);
            s.Configuration["ConnectionStrings:RigV3"] = communeAuto.Connexion;
            s.Configuration["Supervision:Site"] = "BANC-AUTO";
            s.Configuration["Supervision:IntervalleSecondes"] = "1";
        }, [.. ServeurRig.Decouvrir(typeof(ServeurRig).Assembly), new ModuleBanc()]);
        await auto.StartAsync();
        var instants = await Attendre(async () => (await Lignes(communeAuto.Nom,
                "SELECT PublieLe FROM soc.EtatTraitementHistorique WHERE Serveur = 'BANC-AUTO' ORDER BY PublieLe;"))
                .Select(l => (DateTime)l[0]!).ToList(), l => l.Count >= 3, 12);
        var ecarts = instants.Zip(instants.Skip(1), (a, b) => (b - a).TotalSeconds).ToList();
        Exiger(instants.Count >= 3 && ecarts.All(e => e is > 0.5 and < 3),
            $"SOC-011 CA01 : un serveur laisse a lui-meme publie son etat a intervalle regulier ({instants.Count} publications, " +
            $"ecarts {string.Join(" ; ", ecarts.Select(e => e.ToString("0.0")))} s pour 1 s regle)");

        // En PRODUCTION, c'est l'intercepteur (pas la page de developpement) qui repond : meme 500, meme journal.
        var prod = await auto.GetTestClient().GetAsync("/banc/0101/panne-anonyme");
        var suiviProd = (await prod.Content.ReadFromJsonAsync<SuiviErreur>())!.Suivi;
        var ligneProd = await Scalaire(communeAuto.Nom, $"SELECT Programme FROM soc.ErreurTechnique WHERE Suivi = '{suiviProd}' AND Niveau = 'grave';");
        Exiger(prod.StatusCode == HttpStatusCode.InternalServerError && (string?)ligneProd == "GET /banc/{greffe}/panne-anonyme",
            $"SOC-017 CA01 : en production aussi, la panne d'une route est journalisee ({suiviProd}) et l'appelant ne recoit que l'identifiant");
        await auto.StopAsync();
    }

    // CA02 (a) : la panne d'une base de greffe, vue et alertee une fois ; jumeau : une base qui repond n'alerte pas.
    int AlertesRecues(string regle, string greffe) => canal.Recues.Count(a => a.Regle == regle && a.CodeGreffe == greffe);
    await surveillant.PasserAsync();
    var avant = await Lire<EtatSupervision>(RoutesSupervision.Etat);
    Exiger(avant.Greffes.Single(g => g.Code == "3802").Sante == SantesGreffe.Ok && avant.Greffes.Single(g => g.Code == "8002").Sante == SantesGreffe.Ok
           && AlertesRecues(ReglesAlerte.BaseInjoignable, "3802") == 0 && !canal.Recues.Any(a => a.CodeGreffe == "8002"),
        "SOC-011 CA02 (jumeau) : les bases de Vienne et d'Amiens repondent : aucune alerte");
    await Executer("master", $"ALTER DATABASE [{v3Vienne.Nom}] SET OFFLINE WITH ROLLBACK IMMEDIATE;");
    SqlConnection.ClearAllPools();
    var chrono = Stopwatch.StartNew();
    await surveillant.PasserAsync();
    await surveillant.PasserAsync();
    var pendant = await Lire<EtatSupervision>(RoutesSupervision.Etat);
    var lecture = chrono.Elapsed;
    var vienne = pendant.Greffes.Single(g => g.Code == "3802");
    var alerteVienne = canal.Recues.LastOrDefault(a => a.Regle == ReglesAlerte.BaseInjoignable && a.CodeGreffe == "3802");
    Exiger(vienne.Sante == SantesGreffe.Injoignable && AlertesRecues(ReglesAlerte.BaseInjoignable, "3802") == 1
           && alerteVienne is not null && alerteVienne.Traitement == TraitementsSupervision.BaseDuGreffe && alerteVienne.Erreur.Contains(v3Vienne.Nom)
           && pendant.Alertes.Any(a => a.Regle == ReglesAlerte.BaseInjoignable && a.CodeGreffe == "3802" && a.EnvoyeeLe is not null && a.FermeeLe is null),
        $"SOC-011 CA02 : la base de Vienne arretee est vue injoignable et alertee UNE fois en deux passages, l'alerte nomme le greffe, " +
        $"le traitement et l'erreur (« {alerteVienne?.Erreur} »)");
    await Executer("master", $"ALTER DATABASE [{v3Vienne.Nom}] SET ONLINE;");
    SqlConnection.ClearAllPools();
    await surveillant.PasserAsync();
    var apres = await Lire<EtatSupervision>(RoutesSupervision.Etat);
    Exiger(apres.Greffes.Single(g => g.Code == "3802").Sante == SantesGreffe.Ok
           && apres.Alertes.Any(a => a.Regle == ReglesAlerte.BaseInjoignable && a.CodeGreffe == "3802" && a.FermeeLe is not null)
           && !apres.Alertes.Any(a => a.Regle == ReglesAlerte.BaseInjoignable && a.CodeGreffe == "3802" && a.FermeeLe is null),
        "SOC-011 CA02 : la base revenue, l'alerte de Vienne est fermee ; une nouvelle panne en ouvrira une nouvelle");
    var reunion = apres.Greffes.Single(g => g.Code == "9741");
    Exiger(reunion.Sante == SantesGreffe.MigrationsEnRetard && AlertesRecues(ReglesAlerte.MigrationsEnRetard, "9741") == 1,
        $"SOC-011 CA02 : la base V3 de Saint-Denis de la Reunion sans migration est vue en retard et alertee ({reunion.Detail})");

    // CA02 (b) : les echecs de numerotation de l'ancien RIG (NUM-007 CA05), lus par Base\v2\echecs.sql, alertent des le premier.
    Exiger(apres.Greffes.Single(g => g.Code == "4302").Echecs == 0 && AlertesRecues(ReglesAlerte.EchecNumerotation, "4302") == 0,
        "SOC-011 CA02 (jumeau) : Le Puy sans echec de numerotation : 0 compte, aucune alerte");
    try { await Executer(v2Puy.Nom, "RAISERROR(N'Numérotation indisponible : la base de la V3 du greffe 4302 ne répond pas (banc).', 16, 1);"); }
    catch (SqlException) { /* l'erreur que la V2 recoit : c'est elle que la session compte */ }
    var puy = await Attendre(async () =>
    {
        await surveillant.PasserAsync();
        return (await Lire<EtatSupervision>(RoutesSupervision.Etat)).Greffes.Single(g => g.Code == "4302");
    }, g => g.Echecs >= 1, 20);
    var alertePuy = canal.Recues.LastOrDefault(a => a.Regle == ReglesAlerte.EchecNumerotation && a.CodeGreffe == "4302");
    Exiger(puy.Echecs == 1 && AlertesRecues(ReglesAlerte.EchecNumerotation, "4302") == 1 && alertePuy!.Traitement == TraitementsSupervision.Numerotation
           && alertePuy.Erreur.StartsWith("1 échec(s) de numérotation", StringComparison.Ordinal)
           && canal.Recues.Count(a => a.Regle == ReglesAlerte.EchecNumerotation) == 1,
        $"SOC-011 CA02 : le premier echec de numerotation de l'ancien RIG a Le Puy est compte par echecs.sql et alerte (« {alertePuy?.Erreur} »)");

    // CA02 (c) : un traitement en erreur, alerte avec le greffe de l'element ; (d) une file qui ne baisse plus.
    traitement.Panne = true;
    await moteur.DeposerAsync("Banc", "8002", "ok-2");
    await moteur.DeposerAsync("Banc", "9741", "panne");
    await moteur.DeposerAsync("Banc", "8002", "ok-3");
    await surveillant.PasserAsync();                                   // file a 3 : FileBaisseLe pose maintenant
    Exiger(!canal.Recues.Any(a => a.Regle == ReglesAlerte.FileBloquee),
        "SOC-011 CA02 (jumeau) : une file qui vient de se remplir n'alerte pas avant le delai regle (2 s)");
    var faits = await moteur.PasserAsync("Banc");
    var etatErreur = await EtatDe("Banc");
    await surveillant.PasserAsync();
    var alerteTraitement = canal.Recues.LastOrDefault(a => a.Regle == ReglesAlerte.TraitementEnErreur);
    var erreurTraitement = (await Lire<ErreurTechnique[]>($"{RoutesSupervision.Erreurs}?greffe=9741"))
        .FirstOrDefault(l => l.Origine == OriginesErreur.Traitement);
    Exiger(faits == 1 && etatErreur == EtatsTraitement.EnErreur && alerteTraitement is not null && alerteTraitement.CodeGreffe == "9741"
           && alerteTraitement.Traitement == "Banc (BANC-APP)" && alerteTraitement.Erreur.Contains("refuse par le banc")
           && erreurTraitement is not null && erreurTraitement.Programme == "traitement Banc" && alerteTraitement.Erreur.Contains(erreurTraitement.Suivi),
        $"SOC-011 CA02 : le traitement arrete en erreur alerte l'exploitation en nommant le greffe, le traitement et l'erreur " +
        $"(« {alerteTraitement?.Erreur} ») ; l'erreur est au journal technique avec son identifiant (SOC-017 CA03)");
    Exiger(!canal.Recues.Any(a => a.Regle == ReglesAlerte.FileBloquee),
        "SOC-011 CA02 (jumeau) : la file a baisse (3 -> 2) : pas d'alerte de file bloquee");
    await Task.Delay(TimeSpan.FromSeconds(2.5));
    await surveillant.PasserAsync();
    var alerteFile = canal.Recues.LastOrDefault(a => a.Regle == ReglesAlerte.FileBloquee);
    Exiger(alerteFile is not null && alerteFile.Traitement == "Banc (BANC-APP)" && alerteFile.Erreur.Contains("2 élément(s)")
           && alerteFile.Erreur.Contains("8002, 9741"),
        $"SOC-011 CA02 : une file qui ne baisse plus depuis le delai regle alerte (« {alerteFile?.Erreur} »)");

    // CA03 : les 40 greffes, outre-mer compris, lus dans la base commune seule.
    var vue = await Lire<EtatSupervision>(RoutesSupervision.Etat);
    var refusEtat = await Envoyer(HttpMethod.Get, RoutesSupervision.Etat, compte: Agent);
    Exiger(vue.Greffes.Length == 40 && vue.Greffes.Count(g => g.EstOutreMer) == 6 && vue.Greffes.All(g => g.Sante != SantesGreffe.NonVerifiee)
           && vue.Greffes.Count(g => g.Sante == SantesGreffe.PasCreee) == 37 && lecture.TotalSeconds < 10,
        $"SOC-011 CA03 : l'exploitant voit les 40 greffes dont 6 d'outre-mer, chacun verifie ({vue.Greffes.Count(g => g.Sante == SantesGreffe.Ok)} " +
        $"bases qui repondent, {vue.Greffes.Count(g => g.Sante == SantesGreffe.PasCreee)} pas encore creees), meme quand une base est arretee " +
        $"(deux passages et la lecture en {lecture.TotalSeconds:0.0} s)");
    Exiger(refusEtat.StatusCode == HttpStatusCode.Forbidden,
        $"SOC-011 CA03 : l'etat des greffes est reserve a Amitel (agent de greffe : {(int)refusEtat.StatusCode})");
    var vueEcran = await EcranSupervision.OuvrirAsync(clientAmitel);
    Exiger(vueEcran.Mode == "serveur" && vueEcran.Etat!.Greffes.Length == 40 && vueEcran.Etat.Alertes.Any(a => a.FermeeLe is null)
           && vueEcran.Erreurs.Length > 0,
        $"SOC-011 CA03 : l'ecran « Supervision » d'Amitel montre les 40 greffes, les alertes ouvertes et les erreurs ({vueEcran.Erreurs.Length})");
    Reste("SOC-011 CA03 : la vue des 40 VRAIES bases (instances SQL-PROD, RIGBD-ANTILLES, RIGBD-REUNION, RIGBD-POLYNESIE) se verifiera au " +
          "deploiement du serveur chez Amitel -- ce poste n'a ni ces instances ni le droit de les joindre");

    // CA04 : la relance, refusee hors Amitel, tracee avec son auteur, sans perte de file.
    var journalAvant = Convert.ToInt32(await Scalaire(commune.Nom, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'supervision';"));
    var relanceAgent = await Envoyer(HttpMethod.Post, RoutesSupervision.Relancer, new DemandeRelance("Banc"), Agent);
    Exiger(relanceAgent.StatusCode == HttpStatusCode.Forbidden && await EtatDe("Banc") == EtatsTraitement.EnErreur
           && Convert.ToInt32(await Scalaire(commune.Nom, "SELECT COUNT(*) FROM soc.Journal WHERE Famille = 'supervision';")) == journalAvant,
        $"SOC-011 CA04 : un agent de greffe ne relance pas ({(int)relanceAgent.StatusCode}) : le traitement reste arrete, rien n'est trace");
    traitement.Panne = false;
    var relance = await Envoyer(HttpMethod.Post, RoutesSupervision.Relancer, new DemandeRelance("Banc"));
    var resultat = relance.IsSuccessStatusCode ? await relance.Content.ReadFromJsonAsync<ResultatRelance>() : null;
    var trace = await Lignes(commune.Nom, """
        SELECT j.Agent, j.Objet, j.Verbe, j.Avant, c.CompteWindows FROM soc.Journal j JOIN soc.Compte c ON c.Id = j.IdAgent
        WHERE j.Famille = 'supervision';
        """);
    var refaits = await moteur.PasserAsync("Banc");
    var file = await Lignes(commune.Nom, "SELECT Charge, Etat FROM soc.FileTraitement WHERE Traitement = 'Banc' ORDER BY Id;");
    Exiger(resultat is { EtatAvant: EtatsTraitement.EnErreur, EtatApres: EtatsTraitement.EnMarche, EnFile: 2 } && refaits == 2
           && traitement.Faits.OrderBy(f => f).SequenceEqual(["ok-1", "ok-2", "ok-3", "panne"])
           && file.All(l => (string)l[1]! == "fait") && file.Count == 4,
        $"SOC-011 CA04 : relance par l'exploitant : les {resultat?.EnFile} elements en file sont repris, chacun fait une fois " +
        $"({string.Join(", ", traitement.Faits)}), rien n'est perdu");
    Exiger(trace.Count == 1 && (string)trace[0][4]! == "banc.amitel" && (string)trace[0][1]! == "le traitement Banc"
           && ((string)trace[0][3]!).Contains("en erreur"),
        $"SOC-011 CA04 : la relance est tracee au journal des gestes avec son auteur ({trace.FirstOrDefault()?[0]}, {trace.FirstOrDefault()?[4]})");
    var deuxFois = await Envoyer(HttpMethod.Post, RoutesSupervision.Relancer, new DemandeRelance("Banc"));
    var inconnu = await Envoyer(HttpMethod.Post, RoutesSupervision.Relancer, new DemandeRelance("Inconnu"));
    Exiger(deuxFois.StatusCode == HttpStatusCode.Conflict && inconnu.StatusCode == HttpStatusCode.NotFound,
        $"SOC-011 CA04 : un traitement en marche ne se relance pas ({(int)deuxFois.StatusCode}), un traitement inconnu non plus ({(int)inconnu.StatusCode})");
    moteur.Arreter("Banc");
    var etatArrete = await EtatDe("Banc");
    var vueRelance = await EcranSupervision.RelancerAsync(clientAmitel, "Banc");
    Exiger(etatArrete == EtatsTraitement.Arrete && !vueRelance.Echec && vueRelance.Message!.Contains("arrete → en marche")
           && await EtatDe("Banc") == EtatsTraitement.EnMarche,
        $"SOC-011 CA04 : un traitement « arrete » se relance depuis l'ecran « Supervision » ({vueRelance.Message})");

    // CA05 : purge de l'historique des etats (et des alertes fermees, des elements faits) selon les durees ecrites.
    await Executer(commune.Nom, """
        INSERT INTO soc.EtatTraitementHistorique (Serveur, Traitement, CodeGreffe, Etat, EnCours, EnAttente, EnErreur, PublieLe) VALUES
            ('VIEUX', 'Banc', NULL, 'en marche', 0, 0, 0, DATEADD(day, -40, SYSUTCDATETIME())),
            ('RECENT', 'Banc', NULL, 'en marche', 0, 0, 0, DATEADD(day, -1, SYSUTCDATETIME()));
        INSERT INTO soc.AlerteExploitation (Cle, Regle, Traitement, Erreur, OuverteLe, FermeeLe) VALUES
            ('vieille', 'file-bloquee', 'Banc', N'fermee il y a 400 jours', DATEADD(day, -401, SYSUTCDATETIME()), DATEADD(day, -400, SYSUTCDATETIME())),
            ('ouverte', 'file-bloquee', 'Banc', N'ouverte il y a 400 jours', DATEADD(day, -400, SYSUTCDATETIME()), NULL);
        INSERT INTO soc.FileTraitement (Traitement, Charge, DeposeLe, Etat, TraiteLe) VALUES
            ('Vieux', N'{}', DATEADD(day, -41, SYSUTCDATETIME()), 'fait', DATEADD(day, -40, SYSUTCDATETIME()));
        """);
    var purgeEtats = await surveillant.PurgerAsync(DateTime.UtcNow);
    var restantsEtats = await Lignes(commune.Nom, """
        SELECT (SELECT COUNT(*) FROM soc.EtatTraitementHistorique WHERE Serveur = 'VIEUX'), (SELECT COUNT(*) FROM soc.EtatTraitementHistorique WHERE Serveur = 'RECENT'),
               (SELECT COUNT(*) FROM soc.AlerteExploitation WHERE Cle = 'vieille'), (SELECT COUNT(*) FROM soc.AlerteExploitation WHERE Cle = 'ouverte'),
               (SELECT COUNT(*) FROM soc.FileTraitement WHERE Traitement = 'Vieux'), (SELECT Jours FROM soc.ConservationTechnique WHERE Journal = 'etats');
        """);
    var (vieux, recent, vieille, ouverte, vieuxFile, joursEtats) = ((int)restantsEtats[0][0]!, (int)restantsEtats[0][1]!, (int)restantsEtats[0][2]!,
        (int)restantsEtats[0][3]!, (int)restantsEtats[0][4]!, (int)restantsEtats[0][5]!);
    Exiger(vieux == 0 && recent == 1 && vieille == 0 && ouverte == 1 && vieuxFile == 0 && purgeEtats.Etats == 1 && purgeEtats.Alertes == 1 && purgeEtats.File == 1,
        $"SOC-011 CA05 : l'historique des etats au-dela de {joursEtats} jours est purge (l'etat d'hier reste), comme l'alerte fermee " +
        "il y a 400 jours et l'element fait il y a 40 jours ; une alerte encore ouverte n'est jamais purgee");
    Reste("SOC-011 CA02 : le canal reel vers l'exploitation (courriel, outil d'astreinte) est a choisir par l'exploitation d'Amitel ; " +
          "le developpement utilise le canal simule (Supervision:DossierAlertes)");
}
finally
{
    // La base arretee d'un banc rouge doit revenir en ligne pour etre supprimee ; la session d'evenements n'est pas une base.
    try { await Executer("master", $"IF DB_ID(N'{v3Vienne.Nom}') IS NOT NULL ALTER DATABASE [{v3Vienne.Nom}] SET ONLINE;"); }
    catch (SqlException e) { Console.WriteLine($"(remise en ligne de {v3Vienne.Nom} : {e.Message})"); }
    try { await Executer("master", $"IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'{session}') DROP EVENT SESSION [{session}] ON SERVER;"); }
    catch (SqlException e) { Console.WriteLine($"(session {session} non supprimee : {e.Message})"); echecs.Add("session d'evenements du banc laissee sur l'instance"); }
    SqlConnection.ClearAllPools();
}

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("VERT : SOC-017 et SOC-011 tiennent leurs criteres sur ce poste.");
    return 0;
}
Console.WriteLine($"ROUGE : {echecs.Count} controle(s) en echec.");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;

/// <summary>Les routes du banc : une panne non prevue, precedee d'une erreur interceptee et journalisee par le code.</summary>
public sealed class ModuleBanc : IModuleServeur
{
    public const string MotDePasse = "Secret123";
    public const string Jeton = "eyJhbGciOiJIUzI1NiJ9abcdef";

    /// <summary>Le contenu d'une piece (300 octets, 400 caracteres en base64) qui ne doit jamais atteindre le journal.</summary>
    public static readonly string Piece = Convert.ToBase64String([.. Enumerable.Range(0, 300).Select(i => (byte)(i * 7 + 3))]);

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/banc/{greffe}/panne", (string greffe, ILoggerFactory journaux) => Panne(greffe, journaux)).RequireAuthorization();
        routes.MapGet("/banc/{greffe}/panne-anonyme", (string greffe, ILoggerFactory journaux) => Panne(greffe, journaux));
    }

    private static IResult Panne(string greffe, ILoggerFactory journaux)
    {
        var log = journaux.CreateLogger("RigV3.Serveur.Banc.ModuleBanc");
        try { throw new FormatException("Date illisible dans le dossier du banc"); }
        catch (FormatException e) { log.LogError(e, "Erreur interceptee par le code du banc : {Cause}", e.Message); }
        throw new InvalidOperationException(
            $"Panne du banc pour le greffe {greffe} : Server=x;User ID=sa;Password={MotDePasse}; pwd: Hunter2 ; Authorization: Bearer {Jeton} ; piece {Piece}");
    }
}

/// <summary>Un traitement de fond du banc : refuse l'element « panne » tant que <see cref="Panne"/> est vrai.</summary>
public sealed class TraitementDuBanc : ITraitementDeFond
{
    public string Nom => "Banc";
    public volatile bool Panne = true;
    public TaskCompletionSource? Bloquer;
    public readonly ConcurrentQueue<string> Faits = new();

    public async Task TraiterAsync(ElementFile element, CancellationToken ct)
    {
        if (Bloquer is { } b) await b.Task.WaitAsync(ct);
        if (Panne && element.Charge == "panne")
            throw new InvalidOperationException($"Element {element.Id} du greffe {element.CodeGreffe} refuse par le banc");
        Faits.Enqueue(element.Charge);
    }
}

/// <summary>
/// SOC-017 CA03, recherche statique : un « catch » qui attrape TOUT (sans type, Exception, ou un filtre « is not ») doit
/// relancer, journaliser (Log…, EcrireAsync) ou repondre a l'appelant avec l'identifiant (RepondreAsync, Signaler…).
/// Un catch d'un type precis est une decision du code (format illisible -> 400), pas un avalement.
/// </summary>
public static class Avalement
{
    private static readonly Regex Entete = new(
        @"\bcatch\b\s*(\((?<type>[^)]*)\))?\s*(when\s*\((?<quand>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\))?\s*\{",
        RegexOptions.CultureInvariant);
    private static readonly Regex Traite = new(
        @"\bthrow\b|\.Log(Error|Critical|Warning)\s*\(|\bEcrireAsync\s*\(|\bRepondreAsync\s*\(|\bSignaler\w*Async\s*\(", RegexOptions.CultureInvariant);

    public static List<string> Chercher(string chemin, string racine)
    {
        var fichiers = File.Exists(chemin) ? [chemin]
            : Directory.EnumerateFiles(chemin, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin")).ToList();
        return [.. fichiers.SelectMany(f => Dans(Path.GetRelativePath(racine, f), File.ReadAllText(f)))];
    }

    public static List<string> Dans(string nom, string texte)
    {
        var fautes = new List<string>();
        foreach (Match m in Entete.Matches(texte))
        {
            var type = m.Groups["type"].Success ? m.Groups["type"].Value.Trim() : "";
            var quand = m.Groups["quand"].Success ? m.Groups["quand"].Value : null;
            var attrapeTout = (type.Length == 0 || Regex.IsMatch(type, @"^(System\.)?Exception\b"))
                              && (quand is null || quand.Contains(" is not ") || !quand.Contains(" is "));
            if (!attrapeTout) continue;
            var debut = m.Index + m.Length - 1;
            int profondeur = 0, fin = debut;
            for (var i = debut; i < texte.Length; i++)
            {
                if (texte[i] == '{') profondeur++;
                else if (texte[i] == '}' && --profondeur == 0) { fin = i; break; }
            }
            if (!Traite.IsMatch(texte[debut..(fin + 1)]))
                fautes.Add($"{nom}:{texte[..m.Index].Count(c => c == '\n') + 1}");
        }
        return fautes;
    }
}
