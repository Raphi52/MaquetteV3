using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Comptes;
using SocleDroitsCheck;

// Banc du lot 3 du plan de reconstruction (2026-11-20) : SOC-002, SOC-004, SOC-005, SOC-016
// (carnet\socle.json). Un controle par critere, qui cite son id.
//
//  1. Une petite base V2 jetable (BaseV2Jetable) : chaque origine de droits de la V2 a son cas limite.
//     Elle est reprise dans une base V3 jetable, puis le VRAI serveur (en memoire, en developpement :
//     chaque requete choisit son compte par l'en-tete X-RigV3-Compte) est essaye critere par critere.
//  2. RIG_DEV (SQL-DEV\DEV), LU en lecture seule, repris dans une autre base V3 jetable : pour chaque
//     compte actif et chaque fonction du catalogue, la reponse de la V3 est comparee a celle de la V2,
//     recalculee par OracleDroitsV2 (portage de Utilisateur.cs:298-347 et GestionDroitRIG.cls).
// Il lui faut « docker compose up -d » ; RIGV3_V2 remplace la base V2 lue (defaut : RIG_DEV).

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
const string Ecran = "Comptes des agents";
const string CodeGreffe = "3801";

var echecs = new List<string>();
var reportes = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Reporter(string quoi)
{
    Console.WriteLine("REPORTE : " + quoi);
    reportes.Add(quoi);
}

var noms = new List<string>();
var journalServeur = new JournalCapture();

async Task<T> Scalaire<T>(string cs, string sql)
{
    await using var cn = new SqlConnection(cs);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
}
async Task<Exception?> Lever(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception e) { return e; }
}
async Task Executer(string cs, string sql)
{
    await using var cn = new SqlConnection(cs);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    await cmd.ExecuteNonQueryAsync();
}

WebApplication Serveur(string cs) => ServeurRig.Construire(["--environment", "Development"], s =>
{
    s.Logging.ClearProviders();
    s.Logging.SetMinimumLevel(LogLevel.Warning);
    s.Logging.AddProvider(journalServeur);
    s.WebHost.UseTestServer();
    s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = cs;
    s.Configuration[ServiceComptes.CleCodeGreffe] = CodeGreffe;
});

// =====================================================================================================
// 1. LE JEU V2 JETABLE, REPRIS, PUIS LE SERVEUR
// =====================================================================================================
await using (var v2 = await BaseJetable.CreerAsync("SocleDroitsCheckV2"))
await using (var v3 = await BaseJetable.CreerAsync("SocleDroitsCheck"))
{
    noms.Add(v2.Nom); noms.Add(v3.Nom);
    await BaseV2Jetable.CreerAsync(v2.Connexion);
    var rapport = await Migrateur.AppliquerAsync(v3.Connexion, migrations);
    Exiger(new[] { "SOC-002_droits", "SOC-004_services", "SOC-005_journal", "SOC-016_gestes" }.All(rapport.Jouees.Contains),
        $"les migrations du lot passent sur une base neuve, apres SOC-001 ({string.Join(", ", rapport.Jouees)})");

    var bilan = await RepriseDroits.ReprendreAsync(v2.Connexion, v3.Connexion);
    Exiger(bilan is { Comptes: 8, Services: 2, Roles: 6, Fonctions: 8, RolesAttribues: 10 },
        $"reprise du jeu V2 : 8 comptes, 2 services, 6 roles, 8 fonctions, 10 roles attribues ({bilan})");

    // ---------------- SOC-004 CA08 : services et rattachements repris a l'identique -------------------
    var services = new List<string>();
    await using (var cn = new SqlConnection(v3.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT CONCAT(IdV2, '|', Abrege, '|', Nom, '|', CodeDirection, '|', CodeGreffe, '|', DistributionSpecifique, '|', DistributionAuto, '|',
                          CONVERT(varchar(19), HeureDistribution, 126)) FROM soc.Service ORDER BY IdV2;
            """, cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync()) services.Add(l.GetString(0));
    }
    Exiger(services.SequenceEqual(["5|RCS|Registre du Commerce et des sociétés|9999|9999|1|0|1899-12-30T10:00:00", "7|JUD|Service Judiciaire|9999|9999|0|0|"]),
        $"SOC-004 CA08 : les services repris ont le meme abrege, nom, direction, distribution automatique et heure ({string.Join(" ; ", services)})");
    var rattaches = await Scalaire<int>(v3.Connexion, """
        SELECT COUNT(*) FROM soc.Compte c JOIN soc.Service s ON s.Id = c.IdService
        WHERE (c.IdV2 IN (104, 108) AND s.IdV2 = 7) OR (c.IdV2 NOT IN (104, 108) AND s.IdV2 = 5);
        """);
    Exiger(rattaches == 8, $"SOC-004 CA08 : chaque compte reste rattache au meme service ({rattaches}/8)");
    Exiger(await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Compte WHERE CompteWindows = 'ddepart' AND Desactive = 1") == 1,
        "SOC-004 T04 : un compte desactive dans la V2 est repris desactive");

    // ---------------- SOC-005 CA03, SOC-016 CA05 : la reprise ecrit UN evenement et ouvre le journal ----
    var evenements = await Scalaire<int>(v3.Connexion, $"SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = {RepriseDroits.CodeEvenementReprise}");
    var detailReprise = await Scalaire<string>(v3.Connexion, "SELECT TOP 1 Detail FROM soc.EvenementTechnique WHERE Code = 210");
    var lignesDroits = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.JournalDroits");
    Exiger(evenements == 1 && detailReprise.Contains("8 comptes") && detailReprise.Contains($"{bilan.Droits} droits repris")
           && lignesDroits == 0 && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique") == 1,
        $"SOC-005 CA03 : la reprise ecrit UN evenement 210 avec le nombre de comptes et de droits, aucun par droit ({evenements} evenement, {lignesDroits} ligne de droit : « {detailReprise} »)");
    var deuxieme = await Lever(() => RepriseDroits.ReprendreAsync(v2.Connexion, v3.Connexion));
    Exiger(deuxieme is InvalidOperationException && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 210") == 1,
        $"SOC-005 CA03 : une seconde reprise du meme greffe est refusee, toujours un seul evenement ({deuxieme?.Message})");
    var ouverture = await Scalaire<string>(v3.Connexion, "SELECT CONCAT(COUNT(*), '|', MIN(Source), '|', MIN(Verbe), '|', MIN(Apres)) FROM soc.Journal");
    Exiger(ouverture.StartsWith("1|passage|a ouvert|") && ouverture.Contains("reste dans la V2"),
        $"SOC-016 CA05 : le journal de la V3 commence au passage par une ligne qui dit que celui de la V2 y reste (journal V2 non copie) ({ouverture})");

    // ---------------- Le serveur --------------------------------------------------------------------
    await using var app = Serveur(v3.Connexion);
    await app.StartAsync();
    var http = app.GetTestClient();

    async Task<(HttpStatusCode Code, T? Corps, string Brut)> Get<T>(string compte, string route)
    {
        using var d = new HttpRequestMessage(HttpMethod.Get, route);
        d.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        using var r = await http.SendAsync(d);
        var brut = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, r.IsSuccessStatusCode ? JsonSerializer.Deserialize<T>(brut, json) : default, brut);
    }
    async Task<(HttpStatusCode Code, ResultatCompte? Corps, string Brut)> Post<TD>(string compte, string route, TD demande)
    {
        using var d = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(demande, options: json) };
        d.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        using var r = await http.SendAsync(d);
        var brut = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, r.IsSuccessStatusCode ? JsonSerializer.Deserialize<ResultatCompte>(brut, json) : null, brut);
    }
    async Task<VerificationDroit> Verifier(string compte, string fonction)
        => (await Get<VerificationDroit>(compte, $"{RoutesDroits.Verifier}?fonction={Uri.EscapeDataString(fonction)}")).Corps
           ?? new VerificationDroit(fonction, false, "http-en-echec", null);

    // ---------------- SOC-002 CA02 : les trois origines de la V2, un seul modele ----------------------
    var parRole = await Verifier("jagent", "RCS_CONSULT");
    var sansRole = await Verifier("jagent", "FACT");
    Exiger(parRole is { Autorise: true, Origine: OriginesDroit.Role, Detail: "RCSNIV01" } && sansRole is { Autorise: false, Origine: OriginesDroit.Aucune },
        $"SOC-002 CA02 : un droit qui vient d'un role est accorde (RCS_CONSULT par RCSNIV01), une fonction d'aucun role refusee ({parRole.Origine}, {sansRole.Origine})");
    var a3 = await Verifier("pancien", "ENCUPDDEL");
    var a2 = await Verifier("qancien", "ENCUPDDEL");
    Exiger(a3 is { Autorise: true, Origine: OriginesDroit.Ancien } && a2 is { Autorise: false },
        $"SOC-002 CA02 : ancien systeme, degre 3 accorde et degre 2 refuse (superieur a 2, Habilitation.cs:12) ({a3.Detail} ; {a2.Origine})");
    var activite3 = await Verifier("pancien", "ACTJUD");
    var activite2 = await Verifier("qancien", "ACTJUD");
    var processus3 = await Verifier("pancien", "PRCJUD");
    var processus2 = await Verifier("qancien", "PRCJUD");
    Exiger(activite3.Autorise && !activite2.Autorise && processus3.Autorise && !processus2.Autorise,
        $"SOC-002 CA02 : ancien systeme, le degre du domaine prime pour une activite (3 accorde, 2 refuse) et borne un processus de type OLD (3/3 accorde, domaine 2 refuse) ({activite3.Autorise}, {activite2.Autorise}, {processus3.Autorise}, {processus2.Autorise})");
    var menu = await Verifier("ccompta", "FACT_EXP");
    var ancienNom = await Verifier("ccompta", "GESTDROITSEXP");
    var menuSurVb6 = await Verifier("ccompta", "MENUVB6");
    Exiger(menu is { Autorise: true, Origine: OriginesDroit.Comptabilite } && ancienNom is { Autorise: true, Origine: OriginesDroit.Comptabilite }
           && ancienNom.Detail!.Contains("DROITS") && menuSurVb6 is { Autorise: false },
        $"SOC-002 CA02 : un menu de comptabilite est accorde, y compris sous son ancien nom (DROITS -> GESTDROITSEXP) ; sur une fonction FONCVB6, la V2 ne lit pas les menus : refuse ({menu.Detail} ; {ancienNom.Detail} ; {menuSurVb6.Origine})");
    var oracleJeu = await OracleDroitsV2.LireAsync(v2.Connexion);
    var ecartsJeu = new List<string>();
    await using (var cn = new SqlConnection(v3.Connexion))
    {
        await cn.OpenAsync();
        var idsV3 = new Dictionary<int, int>();
        await using (var cmd = new SqlCommand("SELECT IdV2, Id FROM soc.Compte", cn))
        await using (var l = await cmd.ExecuteReaderAsync())
            while (await l.ReadAsync()) idsV3[l.GetInt32(0)] = l.GetInt32(1);
        foreach (var (id, login) in oracleJeu.ComptesActifs)
        {
            var v3Droits = await ServiceDroits.FonctionsAutoriseesAsync(cn, idsV3[id], default);
            foreach (var f in oracleJeu.Fonctions)
                if (oracleJeu.ALaFonctionnalite(id, f) != v3Droits.Contains(f)) ecartsJeu.Add($"{login}/{f}");
        }
    }
    Exiger(ecartsJeu.Count == 0 && oracleJeu.ComptesActifs.Count == 7,
        $"SOC-002 CA02 : sur le jeu, la V3 et l'algorithme de la V2 repondent pareil pour 7 comptes actifs x 8 fonctions ({ecartsJeu.Count} ecart(s) {string.Join(", ", ecartsJeu)})");

    // ---------------- SOC-002 CA05 : fonction inconnue -----------------------------------------------
    async Task<long> LignesDeLaBase() => await Scalaire<long>(v3.Connexion, """
        DECLARE @sql nvarchar(max) = N'SELECT CAST(0 AS bigint)';
        SELECT @sql += N' + (SELECT COUNT_BIG(*) FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N')'
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id;
        EXEC sp_executesql @sql;
        """);
    var avantInconnue = (await LignesDeLaBase(), await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Fonction"));
    var inconnue = await Verifier("jagent", "FONCTION_JAMAIS_VUE");
    var tropLongue = await Verifier("jagent", new string('R', 41));
    var apresInconnue = (await LignesDeLaBase(), await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Fonction"));
    Exiger(inconnue is { Autorise: false, Origine: OriginesDroit.FonctionInconnue } && tropLongue is { Autorise: false, Origine: OriginesDroit.FonctionInconnue }
           && avantInconnue == apresInconnue,
        $"SOC-002 CA05 : une fonction inconnue est refusee sans rien creer ni modifier (catalogue {avantInconnue.Item2} -> {apresInconnue.Item2}, base {avantInconnue.Item1} -> {apresInconnue.Item1} lignes)");
    var majuscules = await Verifier("jagent", "rcs_consult");
    Exiger(majuscules is { Autorise: true, Fonction: "RCS_CONSULT" },
        "SOC-002 CA05 (jumeau) : une fonction connue, ecrite avec d'autres majuscules, est trouvee comme dans la V2 (ProcessusEtFonctionalite.cs:327)");

    // ---------------- SOC-002 CA06 : panne pendant la verification -----------------------------------
    journalServeur.Vider();
    await Executer(v3.Connexion, "EXEC sp_rename 'soc.CompteFonction', 'CompteFonction_enPanne';");
    var panne = await Verifier("pancien", "ENCUPDDEL");
    await Executer(v3.Connexion, "EXEC sp_rename 'soc.CompteFonction_enPanne', 'CompteFonction';");
    var trace = journalServeur.Lignes.FirstOrDefault(x => x.Id == ServiceDroits.EvenementPanne.Id);
    Exiger(panne is { Autorise: false, Origine: OriginesDroit.Panne } && trace is not null
           && trace.Texte.Contains("compte pancien") && trace.Texte.Contains("fonction ENCUPDDEL")
           && System.Text.RegularExpressions.Regex.IsMatch(trace.Texte, @"a \d{4}-\d\d-\d\dT\d\d:\d\d:\d\d"),
        $"SOC-002 CA06 : une panne de la verification refuse la fonction et la trace avec le compte, la fonction et l'heure (« {trace?.Texte} »)");
    Exiger((await Verifier("pancien", "ENCUPDDEL")).Autorise,
        "SOC-002 CA06 (jumeau) : la base revenue, la meme verification accorde a nouveau");

    // ---------------- SOC-002 CA04 : seul qui gere les droits ouvre la gestion des comptes ----------
    var refusVue = await Get<VueComptes>("jagent", RoutesComptes.Comptes);
    var refusCreer = await Post("jagent", RoutesComptes.Creer, new DemandeCompte(null, "intrus", "Intrus", "Ivan", "M.", null, null, Ecran));
    var refusJournal = await Get<LigneJournalDroits[]>("jagent", RoutesDroits.Journal);
    var vueResp = await Get<VueComptes>("rresp", RoutesComptes.Comptes);
    Exiger(refusVue.Code == HttpStatusCode.Forbidden && refusCreer.Code == HttpStatusCode.Forbidden && refusJournal.Code == HttpStatusCode.Forbidden
           && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Compte WHERE CompteWindows = 'intrus'") == 0,
        $"SOC-002 CA04 : un agent sans le droit de gerer les droits est refuse a l'ouverture des comptes, a la creation et au journal des droits ({refusVue.Code}, {refusCreer.Code}, {refusJournal.Code})");
    Exiger(vueResp.Code == HttpStatusCode.OK && vueResp.Corps is { Comptes.Length: > 0 },
        $"SOC-002 CA04 (jumeau) : le responsable qui a le role {RoutesDroits.RoleGestionDesDroits} ouvre la gestion des comptes ({vueResp.Code})");

    // ---------------- SOC-002 CA03 : roles reserves ---------------------------------------------------
    var rolesResp = vueResp.Corps!.Roles.Select(r => r.Nom).ToList();
    var rolesAmitel = (await Get<VueComptes>("aamitel", RoutesComptes.Comptes)).Corps!.Roles.Select(r => r.Nom).ToList();
    var rolesAdmin = (await Get<VueComptes>("aadmin", RoutesComptes.Comptes)).Corps!.Roles.Select(r => r.Nom).ToList();
    var idAgent = vueResp.Corps.Comptes.First(c => c.CompteWindows == "jagent").Id;
    var attribuerReserve = await Post("rresp", RoutesComptes.Role, new DemandeRole(idAgent, "ADMINAMITEL", true, Ecran));
    var attribuerAdmin = await Post("rresp", RoutesComptes.Role, new DemandeRole(idAgent, "ADMINRIG", true, Ecran));
    Exiger(!rolesResp.Contains("ADMINAMITEL") && !rolesResp.Contains("ADMINRIG") && rolesResp.Contains("GREFFIER")
           && attribuerReserve.Corps is { Fait: false } && attribuerAdmin.Corps is { Fait: false }
           && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.CompteRole WHERE Role IN ('ADMINAMITEL', 'ADMINRIG') AND IdCompte = " + idAgent) == 0,
        $"SOC-002 CA03 : un responsable du greffe ne voit pas les roles reserves a Amitel ou aux administrateurs et ne peut pas les attribuer ({string.Join(", ", rolesResp)} ; « {attribuerReserve.Corps?.Message} »)");
    var adminDonne = await Post("aadmin", RoutesComptes.Role, new DemandeRole(idAgent, "ADMINRIG", true, Ecran));
    Exiger(rolesAmitel.Contains("ADMINAMITEL") && !rolesAmitel.Contains("ADMINRIG") && rolesAdmin.Contains("ADMINRIG") && rolesAdmin.Contains("ADMINAMITEL")
           && adminDonne.Corps is { Fait: true },
        $"SOC-002 CA03 (jumeau) : un agent d'Amitel voit les roles Amitel, un administrateur voit et attribue aussi les roles administrateur ({adminDonne.Corps?.Message})");
    await Post("aadmin", RoutesComptes.Role, new DemandeRole(idAgent, "ADMINRIG", false, Ecran));

    // ---------------- SOC-004 CA01, CA07 : creer un compte ------------------------------------------
    var idRcs = vueResp.Corps.Services.First(s => s.Abrege == "RCS").Id;
    var long21 = "nouvel.agent.lot3abcd";   // 21 caracteres
    var cree = await Post("rresp", RoutesComptes.Creer, new DemandeCompte(null, long21, "Martin", "Lucie", "Mme", idRcs, ["GREFFIER"], Ecran));
    var identiteNouveau = await Get<IdentiteAgent>(long21, RoutesIdentite.Identite);
    Exiger(cree.Corps is { Fait: true, Compte: { Desactive: false, Abrege: "nouvel.agent.lot", CodeGreffe: CodeGreffe } }
           && identiteNouveau.Corps is { Admis: true, Agent.Nom: "Martin" },
        $"SOC-004 CA01 : le compte est cree actif, abrege tire du compte (16 caracteres : « {cree.Corps?.Compte?.Abrege} »), et l'agent ouvre la V3 aussitot ({identiteNouveau.Corps?.Statut})");
    var court = await Post("rresp", RoutesComptes.Creer, new DemandeCompte(null, "lcourt", "Court", "Leo", "M.", idRcs, ["GREFFIER"], Ecran));
    Exiger(court.Corps is { Fait: true, Compte.Abrege: "lcourt" },
        "SOC-004 CA01 (jumeau) : un compte de 15 caracteres ou moins est son propre abrege, comme la V2");
    var parAmitel = await Post("aamitel", RoutesComptes.Creer, new DemandeCompte(null, "cree.par.amitel", "Par", "Amitel", "M.", idRcs, ["GREFFIER"], Ecran));
    Exiger(parAmitel.Corps is { Fait: true, Compte.CodeGreffe: ServiceComptes.CodeGreffeAmitel } && cree.Corps.Compte.CodeGreffe == CodeGreffe,
        $"SOC-004 CA07 : un compte cree par un agent d'Amitel recoit le code greffe 9999, un compte cree par le greffe celui du greffe ({parAmitel.Corps?.Compte?.CodeGreffe} / {cree.Corps.Compte.CodeGreffe})");

    // ---------------- SOC-004 CA02 : doublon a d'autres majuscules ----------------------------------
    var doublon = await Post("rresp", RoutesComptes.Creer, new DemandeCompte(null, long21.ToUpperInvariant(), "Autre", "Agent", "M.", idRcs, ["GREFFIER"], Ecran));
    var c0 = court.Corps!.Compte!;
    var renommage = await Post("rresp", RoutesComptes.Modifier, new DemandeCompte(c0.Id, "JAGENT", c0.Nom, c0.Prenom, c0.Civilite, c0.IdService, null, Ecran));
    Exiger(doublon.Corps is { Fait: false, Message: var m1 } && m1 == $"Ce login existe déjà : {long21.ToUpperInvariant()}"
           && renommage.Corps is { Fait: false } && renommage.Corps.Message!.StartsWith("Ce login existe déjà")
           && await Scalaire<int>(v3.Connexion, $"SELECT COUNT(*) FROM soc.Compte WHERE CompteWindows = '{long21}'") == 1,
        $"SOC-004 CA02 : un compte deja enregistre, ecrit avec d'autres majuscules, est refuse avec le message de la V2 (« {doublon.Corps?.Message} »), a la creation comme au renommage");

    // ---------------- SOC-004 CA05, SOC-016 CA01 : modifier, journal avec avant/apres --------------
    var nouveau = cree.Corps.Compte!;
    var modif = await Post("rresp", RoutesComptes.Modifier, new DemandeCompte(nouveau.Id, long21, "Martin-Durand", "Lucie", "Mme", idRcs, null, Ecran));
    Exiger(modif.Corps is { Fait: true, Compte.Nom: "Martin-Durand" }, $"SOC-004 : le responsable corrige le nom d'un compte ({modif.Corps?.Message})");
    var ligneModif = await Scalaire<string>(v3.Connexion, $"""
        SELECT CONCAT(Agent, '|', Ecran, '|', Famille, '|', Objet, '|', Verbe, '|', JSON_VALUE(Avant, '$.nom'), '|', JSON_VALUE(Apres, '$.nom'), '|',
                      CASE WHEN Instant > DATEADD(minute, -5, SYSUTCDATETIME()) THEN 'recent' ELSE 'ancien' END)
        FROM soc.Journal WHERE IdObjet = '{nouveau.Id}' AND Verbe = 'a modifie' AND Objet LIKE 'le compte %';
        """);
    Exiger(ligneModif == $"Rose Responsable|{Ecran}|compte|le compte {long21}|a modifie|Martin|Martin-Durand|recent",
        $"SOC-016 CA01 : une modification garde l'agent, la date et l'heure, l'ecran, l'objet et les valeurs avant et apres ({ligneModif})");

    // ---------------- SOC-004 CA03 : l'agent agit, puis part ----------------------------------------
    await Post("rresp", RoutesComptes.Role, new DemandeRole(nouveau.Id, RoutesDroits.RoleGestionDesDroits, true, Ecran));
    var gesteDuPartant = await Post(long21, RoutesComptes.Modifier, new DemandeCompte(court.Corps.Compte!.Id, "lcourt", "Court", "Leon", "M.", idRcs, null, Ecran));
    var desactive = await Post("rresp", RoutesComptes.Desactiver, new DemandeSurCompte(nouveau.Id, Ecran));
    var identiteParti = await Get<IdentiteAgent>(long21, RoutesIdentite.Identite);
    var vueApresDepart = await Get<VueJournal>("rresp", $"{RoutesJournal.Gestes}?objet=lcourt");
    Exiger(gesteDuPartant.Corps is { Fait: true } && desactive.Corps is { Fait: true, Compte.Desactive: true }
           && identiteParti.Corps is { Admis: false, Statut: StatutsIdentite.Desactive }
           && vueApresDepart.Corps!.Lignes.Any(l => l.Qui == "Lucie Martin-Durand" && l.Verbe == "a modifié"),
        $"SOC-004 CA03 : desactive, l'agent n'ouvre plus la V3 ({identiteParti.Corps?.Statut}) et son geste garde son nom comme auteur ({string.Join(" / ", vueApresDepart.Corps.Lignes.Select(l => l.Phrase))})");
    Reporter("SOC-004 CA03, moitie V2 : « il ne peut plus ouvrir la V2 » suppose la recopie du compte vers la V2 (REC-017 CA02, phase 2) ; la desactivation est journalisee dans la transaction du geste, prete pour elle");

    // ---------------- SOC-004 CA04 : suppression ------------------------------------------------------
    var suppRefusee = await Post("rresp", RoutesComptes.Supprimer, new DemandeSurCompte(nouveau.Id, Ecran));
    var reprisV2 = vueResp.Corps.Comptes.First(c => c.CompteWindows == "jagent");
    var suppRepris = await Post("rresp", RoutesComptes.Supprimer, new DemandeSurCompte(reprisV2.Id, Ecran));
    var parSql = await Lever(() => Executer(v3.Connexion, $"SET XACT_ABORT ON; BEGIN TRAN; DELETE FROM soc.CompteRole WHERE IdCompte = {nouveau.Id}; DELETE FROM soc.Compte WHERE Id = {nouveau.Id}; COMMIT;"));
    Exiger(suppRefusee.Corps is { Fait: false, ProposerDesactivation: true } && suppRepris.Corps is { Fait: false, ProposerDesactivation: true }
           && parSql is SqlException { Number: 547 }
           && await Scalaire<int>(v3.Connexion, $"SELECT COUNT(*) FROM soc.Compte WHERE Id IN ({nouveau.Id}, {reprisV2.Id})") == 2,
        $"SOC-004 CA04 : la suppression d'un compte qui a agi (ou repris de la V2) est refusee et propose la desactivation ; la base elle-meme la refuse ({suppRefusee.Corps?.Message} ; SQL {(parSql as SqlException)?.Number})");
    var jamais = await Post("rresp", RoutesComptes.Creer, new DemandeCompte(null, "jamais.agi", "Jamais", "Agi", "M.", idRcs, ["GREFFIER"], Ecran));
    var suppOk = await Post("rresp", RoutesComptes.Supprimer, new DemandeSurCompte(jamais.Corps!.Compte!.Id, Ecran));
    Exiger(suppOk.Corps is { Fait: true } && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Compte WHERE CompteWindows = 'jamais.agi'") == 0
           && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Objet = 'le compte jamais.agi' AND Verbe = 'a supprime'") == 1,
        "SOC-004 CA04 (jumeau) : un compte qui n'a jamais agi est supprime, et la suppression est journalisee");

    // ---------------- SOC-004 CA05 : le journal du compte ----------------------------------------------
    var journalCompte = await Get<VueJournal>("rresp", $"{RoutesJournal.Gestes}?objet={Uri.EscapeDataString("le compte " + long21)}");
    var verbes = journalCompte.Corps!.Lignes.Where(l => l.Objet == $"le compte {long21}").Select(l => l.Verbe).ToList();
    Exiger(verbes.SequenceEqual(["a désactivé", "a modifié", "a créé"])
           && journalCompte.Corps.Lignes.All(l => l.Qui == "Rose Responsable" && l.Instant > DateTime.UtcNow.AddMinutes(-5)),
        $"SOC-004 CA05 : creation, modification et desactivation figurent au journal avec l'auteur, la date et le compte vise ({string.Join(" / ", journalCompte.Corps.Lignes.Select(l => l.Phrase))})");
    Reporter("SOC-004 CA06 : la V2 reconnait un compte cree ou change dans la V3 -- recopie V3 -> V2 (REC-002, REC-017, phase 2). Le compte garde les cles qu'elle lira : compte Windows, service (IdV2 du service), etat");

    // ---------------- SOC-005 CA01, CA02 : journal des droits et evenement 130 -----------------------
    var avant130 = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 130");
    var avantDroits = await Scalaire<long>(v3.Connexion, "SELECT ISNULL(MAX(Id), 0) FROM soc.JournalDroits");
    await Post("rresp", RoutesComptes.Role, new DemandeRole(idAgent, "GREFFIER", true, Ecran));
    var apres130SansCompta = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 130");
    await Post("rresp", RoutesComptes.Role, new DemandeRole(idAgent, "FACT : STANDARD", true, Ecran));
    await Post("rresp", RoutesComptes.Role, new DemandeRole(idAgent, "GREFFIER", false, Ecran));
    var lignes = new List<string>();
    await using (var cn = new SqlConnection(v3.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT CONCAT(CompteVise, '|', Role, '|', Sens, '|', Auteur, '|', CASE WHEN Instant > DATEADD(minute, -5, SYSUTCDATETIME()) THEN 'date' END) FROM soc.JournalDroits WHERE Id > {avantDroits} ORDER BY Id", cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync()) lignes.Add(l.GetString(0));
    }
    Exiger(lignes.SequenceEqual(["jagent|GREFFIER|ajout|Rose Responsable|date", "jagent|FACT : STANDARD|ajout|Rose Responsable|date", "jagent|GREFFIER|retrait|Rose Responsable|date"]),
        $"SOC-005 CA01 : chaque ajout et retrait de role garde le compte vise, le role, l'auteur, la date et l'heure ({string.Join(" ; ", lignes)})");
    var apres130 = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.EvenementTechnique WHERE Code = 130");
    var detail130 = await Scalaire<string>(v3.Connexion, "SELECT TOP 1 CONCAT(Libelle, ' : ', Detail) FROM soc.EvenementTechnique WHERE Code = 130 ORDER BY Id DESC");
    Exiger(apres130 == avant130 + 1 && apres130SansCompta == avant130 && detail130.Contains("FACT : STANDARD") && detail130.Contains("FACT"),
        $"SOC-005 CA02 : un role qui ouvre une fonction de facturation ecrit l'evenement 130 ; un role sans facturation n'en ecrit pas ({avant130} -> {apres130SansCompta} -> {apres130} : « {detail130} »)");

    // ---------------- SOC-005 CA04, SOC-016 CA04 : ajout seul ---------------------------------------
    var majDroits = await Lever(() => Executer(v3.Connexion, "UPDATE soc.JournalDroits SET Auteur = 'Faussaire';"));
    var effDroits = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.JournalDroits;"));
    var effEvt = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.EvenementTechnique WHERE Code = 130;"));
    Exiger(majDroits is SqlException { Number: 51010 } && effDroits is SqlException { Number: 51010 } && effEvt is SqlException { Number: 51010 }
           && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.JournalDroits WHERE Auteur = 'Faussaire'") == 0,
        "SOC-005 CA04 : modifier ou effacer une ligne du journal des droits ou des evenements est refuse par la base, meme au compte le plus puissant (sa)");
    var routesEcriture = new List<HttpStatusCode>();
    foreach (var verbe in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Post })
    {
        using var d = new HttpRequestMessage(verbe, RoutesDroits.Journal);
        d.Headers.Add(RoutesIdentite.EnTeteCompte, "aadmin");
        using var r = await http.SendAsync(d);
        routesEcriture.Add(r.StatusCode);
    }
    Exiger(routesEcriture.All(c => c is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound),
        $"SOC-005 CA04 : aucune route ne modifie le journal, meme pour un administrateur ({string.Join(", ", routesEcriture)})");

    var majGestes = await Lever(() => Executer(v3.Connexion, "UPDATE soc.Journal SET Agent = 'Faussaire';"));
    var effRecent = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.Journal;"));
    await Executer(v3.Connexion, """
        INSERT INTO soc.Journal (Instant, Source, CompteV2, Famille, Objet, Verbe) VALUES
            (DATEADD(year, -7, SYSUTCDATETIME()), 'recopie-v2', 'ancien', 'facture', 'la facture F-SEPT-ANS', 'a modifie'),
            (DATEADD(year, -5, SYSUTCDATETIME()), 'recopie-v2', 'ancien', 'facture', 'la facture F-CINQ-ANS', 'a modifie');
        """);
    var effHorsPurge = await Lever(() => Executer(v3.Connexion, "DELETE FROM soc.Journal WHERE Objet = 'la facture F-SEPT-ANS';"));
    var avantPurge = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Journal");
    var retirees = await Scalaire<int>(v3.Connexion, "EXEC soc.PurgerJournal;");
    var restent = await Scalaire<string>(v3.Connexion, "SELECT CONCAT(COUNT(*), '|', SUM(CASE WHEN Objet = 'la facture F-CINQ-ANS' THEN 1 ELSE 0 END), '|', SUM(CASE WHEN Objet = 'la facture F-SEPT-ANS' THEN 1 ELSE 0 END)) FROM soc.Journal");
    var sixAns = await Lever(() => Executer(v3.Connexion, "UPDATE soc.FamilleJournal SET ConservationAns = 5 WHERE Famille = 'facture';"));
    Exiger(majGestes is SqlException { Number: 51010 } && effRecent is SqlException { Number: 51011 } && effHorsPurge is SqlException { Number: 51011 }
           && retirees == 1 && restent == $"{avantPurge - 1}|1|0" && sixAns is SqlException { Number: 547 },
        $"SOC-016 CA04 : une ligne du journal ne se modifie ni ne s'efface ; seule la purge par duree retire la ligne de facturation de 7 ans et garde celle de 5 ans (au moins 6 ans) ({retirees} retiree, reste {restent})");

    // ---------------- SOC-005 CA05 : filtres du journal des droits -----------------------------------
    var aujourdhui = DateTime.UtcNow.ToString("yyyy-MM-dd");
    var parCompte = (await Get<LigneJournalDroits[]>("rresp", $"{RoutesDroits.Journal}?compte=jagent")).Corps!;
    var parRoleJ = (await Get<LigneJournalDroits[]>("rresp", $"{RoutesDroits.Journal}?role=GREFFIER")).Corps!;
    var parPeriode = (await Get<LigneJournalDroits[]>("rresp", $"{RoutesDroits.Journal}?du={aujourdhui}&au={aujourdhui}")).Corps!;
    var passe = (await Get<LigneJournalDroits[]>("rresp", $"{RoutesDroits.Journal}?du=2020-01-01&au=2020-12-31")).Corps!;
    var mauvaiseDate = await Get<LigneJournalDroits[]>("rresp", $"{RoutesDroits.Journal}?du=20/11/2026");
    Exiger(parCompte.Length >= 3 && parCompte.All(l => l.CompteVise == "jagent") && parCompte.Zip(parCompte.Skip(1)).All(p => p.First.Instant >= p.Second.Instant && p.First.Id > p.Second.Id)
           && parRoleJ.Length >= 2 && parRoleJ.All(l => l.Role == "GREFFIER") && parRoleJ[0].Sens == "retrait"
           && parPeriode.Length >= parCompte.Length && passe.Length == 0 && mauvaiseDate.Code == HttpStatusCode.BadRequest,
        $"SOC-005 CA05 : le journal des droits se filtre par compte, par role et par periode, du plus recent au plus ancien ({parCompte.Length}, {parRoleJ.Length}, {parPeriode.Length}, periode passee {passe.Length})");

    // ---------------- SOC-016 CA02 : quatre familles de la V2, l'agent nomme -----------------------
    var familles = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.FamilleJournal WHERE Famille IN ('facture', 'encaissement', 'ventilation', 'article-utilise') AND Facturation = 1 AND ConservationAns >= 6");
    var sansAgent = await Lever(() => Executer(v3.Connexion, "INSERT INTO soc.Journal (Source, Famille, Objet, Verbe) VALUES ('v3', 'facture', 'la facture F1', 'a modifie');"));
    var agentFantome = await Lever(() => Executer(v3.Connexion, "INSERT INTO soc.Journal (Source, IdAgent, Agent, Famille, Objet, Verbe) VALUES ('v3', 999999, 'sa', 'facture', 'la facture F1', 'a modifie');"));
    var agentsV3 = await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Journal j WHERE j.Source = 'v3' AND NOT EXISTS (SELECT 1 FROM soc.Compte c WHERE c.Id = j.IdAgent)");
    Exiger(familles == 4 && sansAgent is SqlException { Number: 547 } && agentFantome is SqlException { Number: 547 } && agentsV3 == 0,
        $"SOC-016 CA02 : le journal couvre les quatre familles de la V2 (facture, encaissement, ventilation, article utilise) et nomme l'agent lui-meme : une ligne sans agent, ou sous un compte qui n'est pas un agent, est refusee ({familles} familles)");

    // ---------------- SOC-016 CA03 : le chef de greffe voit tout, un agent ses seuls gestes -----------
    await using (var cn = new SqlConnection(v3.Connexion))
    {
        await cn.OpenAsync();
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();
        var agentJules = new Appelant(idAgent, "jagent", "Jules Agent", false, false, false);
        await JournalGestes.EcrireAsync(cn, tx, agentJules, "Demandes", "demande", "la demande D-1", "D-1", "a valide", null, new { etat = "validee" }, default);
        await tx.CommitAsync();
    }
    var chef = (await Get<VueJournal>("rresp", RoutesJournal.Gestes)).Corps!;
    var jules = (await Get<VueJournal>("jagent", RoutesJournal.Gestes)).Corps!;
    var filtreAgent = (await Get<VueJournal>("rresp", $"{RoutesJournal.Gestes}?agent=Jules")).Corps!;
    var filtrePasse = (await Get<VueJournal>("rresp", $"{RoutesJournal.Gestes}?du=2020-01-01&au=2020-12-31")).Corps!;
    Exiger(chef.ToutLeGreffe && chef.Lignes.Length > 5 && chef.Lignes.Zip(chef.Lignes.Skip(1)).All(p => p.First.Instant >= p.Second.Instant)
           && chef.Lignes.Any(l => l.Phrase == $"Rose Responsable a modifié le compte {long21}")
           && filtreAgent.Lignes.All(l => l.Qui == "Jules Agent") && filtreAgent.Lignes.Length == 1 && filtrePasse.Lignes.Length == 0,
        $"SOC-016 CA03 : le chef de greffe voit les gestes du plus recent au plus ancien, en phrases, filtrables par agent, objet et periode ({chef.Lignes.Length} lignes ; « {chef.Lignes[0].Phrase} »)");
    Exiger(!jules.ToutLeGreffe && jules.Lignes.Length == 1 && jules.Lignes[0].Phrase == "Jules Agent a validé la demande D-1",
        $"SOC-016 CA03 : un autre agent ne voit pas l'activite de ses collegues, seulement ses propres gestes ({jules.Lignes.Length} ligne : {string.Join(" / ", jules.Lignes.Select(l => l.Phrase))})");

    // ---------------- SOC-016 CA06 : la recopie V2 -> V3 est attribuee a la recopie --------------
    await using (var cn = new SqlConnection(v3.Connexion))
    {
        await cn.OpenAsync();
        await JournalGestes.EcrireRecopieAsync(cn, null, "jdupont_v2", "client", "le client C-42", "C-42", "a modifie", new { nom = "A" }, new { nom = "B" }, default);
    }
    var recopie = (await Get<VueJournal>("rresp", $"{RoutesJournal.Gestes}?objet=C-42")).Corps!.Lignes.Single();
    var recopieAvecAgent = await Lever(() => Executer(v3.Connexion, $"INSERT INTO soc.Journal (Source, IdAgent, Agent, CompteV2, Famille, Objet, Verbe) VALUES ('recopie-v2', {idAgent}, 'Jules Agent', 'jdupont_v2', 'client', 'le client C-43', 'a modifie');"));
    Exiger(recopie is { Source: "recopie-v2", Phrase: "La recopie de la V2 (compte V2 jdupont_v2) a modifié le client C-42" }
           && await Scalaire<int>(v3.Connexion, "SELECT COUNT(*) FROM soc.Journal WHERE Objet = 'le client C-42' AND IdAgent IS NULL AND CompteV2 = 'jdupont_v2'") == 1
           && recopieAvecAgent is SqlException { Number: 547 },
        $"SOC-016 CA06 : une ecriture de la recopie est attribuee a la recopie et au compte V2 d'origine, jamais a un agent de la V3 (« {recopie.Phrase} »)");

    Reporter("SOC-002 CA07 : la V2 applique un droit change dans la V3 -- recopie V3 -> V2 des roles (REC-002, phase 2). Chaque changement est journalise (compte Windows et nom de role de la V2) dans la transaction du geste");
    await app.StopAsync();
}

// =====================================================================================================
// 2. RIG_DEV : LA REPRISE COMPAREE A LA V2, COMPTE PAR COMPTE, FONCTION PAR FONCTION (SOC-002 CA01)
// =====================================================================================================
var csV2 = Environment.GetEnvironmentVariable("RIGV3_V2")
           ?? @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15";
var nomV2 = new SqlConnectionStringBuilder(csV2).InitialCatalog;
OracleDroitsV2? oracle = null;
try { oracle = await OracleDroitsV2.LireAsync(csV2); }
catch (Exception e) { Exiger(false, $"SOC-002 CA01 : la base V2 {nomV2} se lit en lecture seule (poser RIGV3_V2 pour une autre base) -- {e.Message}"); }

if (oracle is not null)
{
    await using var rig = await BaseJetable.CreerAsync("SocleDroitsCheckRig");
    noms.Add(rig.Nom);
    await Migrateur.AppliquerAsync(rig.Connexion, migrations);
    var debut = DateTime.UtcNow;
    var bilan = await RepriseDroits.ReprendreAsync(csV2, rig.Connexion);
    var duree = DateTime.UtcNow - debut;
    Console.WriteLine($"       reprise de {nomV2} : {bilan} en {duree.TotalSeconds:0.0} s");

    var idsV3 = new Dictionary<int, int>();
    var comparaisons = 0;
    var ecarts = new List<string>();
    var accordes = 0;
    await using (var cn = new SqlConnection(rig.Connexion))
    {
        await cn.OpenAsync();
        await using (var cmd = new SqlCommand("SELECT IdV2, Id FROM soc.Compte", cn))
        await using (var l = await cmd.ExecuteReaderAsync())
            while (await l.ReadAsync()) idsV3[l.GetInt32(0)] = l.GetInt32(1);
        foreach (var (id, login) in oracle.ComptesActifs)
        {
            var v3Droits = await ServiceDroits.FonctionsAutoriseesAsync(cn, idsV3[id], default);
            foreach (var f in oracle.Fonctions)
            {
                comparaisons++;
                var v2Dit = oracle.ALaFonctionnalite(id, f);
                if (v2Dit) accordes++;
                if (v2Dit != v3Droits.Contains(f)) ecarts.Add($"{login}/{f} (V2 {(v2Dit ? "autorise" : "refuse")})");
            }
        }
    }
    Exiger(ecarts.Count == 0 && oracle.ComptesActifs.Count > 50 && oracle.Fonctions.Count > 500,
        $"SOC-002 CA01 : {nomV2} repris -- pour chacun des {oracle.ComptesActifs.Count} comptes actifs et chacune des {oracle.Fonctions.Count} fonctions, la V3 repond comme la V2 : {ecarts.Count} ecart sur {comparaisons} comparaisons ({accordes} autorisations){(ecarts.Count > 0 ? " -- " + string.Join(", ", ecarts.Take(10)) : "")}");
    Exiger(bilan is { DroitsAncien: > 0, DroitsComptabilite: > 0, RolesAttribues: > 0 },
        $"SOC-002 CA02 : dans {nomV2}, les trois origines de droits sont reprises dans le modele unique ({bilan.RolesAttribues} roles attribues, {bilan.DroitsAncien} droits de l'ancien systeme, {bilan.DroitsComptabilite} de la comptabilite)");

    // Le VRAI chemin d'un agent : la route du serveur, compte par compte, pour un echantillon de comptes admis.
    await using (var app = Serveur(rig.Connexion))
    {
        await app.StartAsync();
        var http = app.GetTestClient();
        var admis = new List<(int Id, string Login)>();
        foreach (var c in oracle.ComptesActifs)
        {
            using var d = new HttpRequestMessage(HttpMethod.Get, RoutesIdentite.Identite);
            d.Headers.Add(RoutesIdentite.EnTeteCompte, c.Login);
            using var r = await http.SendAsync(d);
            if ((await r.Content.ReadFromJsonAsync<IdentiteAgent>(json))?.Admis == true) admis.Add(c);
            if (admis.Count == 3) break;
        }
        var ecartsRoute = new ConcurrentBag<string>();
        var appels = 0;
        foreach (var (id, login) in admis)
            await Parallel.ForEachAsync(oracle.Fonctions, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (f, ct) =>
            {
                using var d = new HttpRequestMessage(HttpMethod.Get, $"{RoutesDroits.Verifier}?fonction={Uri.EscapeDataString(f)}");
                d.Headers.Add(RoutesIdentite.EnTeteCompte, login);
                using var r = await http.SendAsync(d, ct);
                var v = await r.Content.ReadFromJsonAsync<VerificationDroit>(json, ct);
                Interlocked.Increment(ref appels);
                if (v is null || v.Autorise != oracle.ALaFonctionnalite(id, f)) ecartsRoute.Add($"{login}/{f}");
            });
        Exiger(admis.Count == 3 && ecartsRoute.IsEmpty && appels == 3 * oracle.Fonctions.Count,
            $"SOC-002 CA01 : par la route de l'agent (GET {RoutesDroits.Verifier}), 3 comptes admis de {nomV2} x {oracle.Fonctions.Count} fonctions = la V2 ({ecartsRoute.Count} ecart sur {appels} appels : {string.Join(", ", admis.Select(a => a.Login))})");
        await app.StopAsync();
    }

    // Le comparateur n'est pas aveugle : un droit retire en V3 est vu.
    await Executer(rig.Connexion, "DELETE TOP (1) FROM soc.CompteFonction WHERE Origine = 'comptabilite' AND IdCompte IN (SELECT Id FROM soc.Compte WHERE Desactive = 0) AND NOT EXISTS (SELECT 1 FROM soc.CompteRole r JOIN soc.RoleFonction rf ON rf.Role = r.Role WHERE r.IdCompte = soc.CompteFonction.IdCompte AND rf.Fonction = soc.CompteFonction.Fonction);");
    var vus = 0;
    await using (var cn = new SqlConnection(rig.Connexion))
    {
        await cn.OpenAsync();
        foreach (var (id, _) in oracle.ComptesActifs)
        {
            var v3Droits = await ServiceDroits.FonctionsAutoriseesAsync(cn, idsV3[id], default);
            vus += oracle.Fonctions.Count(f => oracle.ALaFonctionnalite(id, f) != v3Droits.Contains(f));
        }
    }
    Exiger(vus == 1, $"SOC-002 CA01 (jumeau) : un seul droit retire de la V3 fait 1 ecart au comparateur ({vus})");

    var evenement = await Scalaire<string>(rig.Connexion, "SELECT CONCAT(COUNT(*), '|', MAX(Detail)) FROM soc.EvenementTechnique WHERE Code = 210");
    var journal = await Scalaire<int>(rig.Connexion, "SELECT COUNT(*) FROM soc.Journal");
    Exiger(evenement.StartsWith("1|") && evenement.Contains($"{bilan.Comptes} comptes") && journal == 1,
        $"SOC-005 CA03, SOC-016 CA05 : la reprise de {nomV2} ecrit un seul evenement ({evenement}) et une seule ligne de journal ; l'AUDIT de la V2 n'est pas copie");

    // SOC-004 CA08 sur la vraie base : les services et le rattachement de chaque compte.
    var servicesV2 = new List<string>();
    await using (var cn = new SqlConnection(new SqlConnectionStringBuilder(csV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT CONCAT(SRVC_ID_SRVC, '|', SRVC_ABREGE, '|', SRVC_NOM, '|', SRVC_CODE_DIRECTION, '|', SRVC_DISTRI_SPECIFIQUE, '|', SRVC_DISTRIBUTION_AUTO, '|', CONVERT(varchar(19), SRVC_HEURE_DISTRIBUTION, 126)) FROM SERVICE ORDER BY SRVC_ID_SRVC", cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync()) servicesV2.Add(l.GetString(0));
    }
    var servicesV3 = new List<string>();
    await using (var cn = new SqlConnection(rig.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT CONCAT(IdV2, '|', Abrege, '|', Nom, '|', CodeDirection, '|', CAST(DistributionSpecifique AS int), '|', CAST(DistributionAuto AS int), '|', CONVERT(varchar(19), HeureDistribution, 126)) FROM soc.Service ORDER BY IdV2", cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync()) servicesV3.Add(l.GetString(0));
    }
    var rattachementsV2 = new Dictionary<int, int?>();
    await using (var cn = new SqlConnection(new SqlConnectionStringBuilder(csV2) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT UTLST_ID_UTLST, UTLST_ID_SRVC FROM UTILISATEUR", cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync()) rattachementsV2[l.GetInt32(0)] = l.IsDBNull(1) ? null : l.GetInt32(1);
    }
    var differents = 0;
    await using (var cn = new SqlConnection(rig.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT c.IdV2, s.IdV2 FROM soc.Compte c LEFT JOIN soc.Service s ON s.Id = c.IdService", cn);
        await using var l = await cmd.ExecuteReaderAsync();
        while (await l.ReadAsync())
            if (rattachementsV2.GetValueOrDefault(l.GetInt32(0)) != (l.IsDBNull(1) ? null : l.GetInt32(1))) differents++;
    }
    Exiger(servicesV2.Count > 0 && servicesV2.SequenceEqual(servicesV3) && rattachementsV2.Count == bilan.Comptes && differents == 0,
        $"SOC-004 CA08 : {nomV2} -- {servicesV3.Count} services repris a l'identique (abrege, nom, direction, distribution, heure) et {rattachementsV2.Count} comptes rattaches au meme service ({differents} different)");
    Reporter("SOC-002 CA01, « sur les 40 bases » : seule RIG_DEV est lisible d'ici ; le meme banc se rejoue base par base (RIGV3_V2) sur les copies anonymisees des 40 greffes (REC-008)");
}

foreach (var nom in noms)
    Exiger(!await BaseJetable.ExisteAsync(nom), $"base jetable supprimee : {nom}");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? $"TOUT VERT : SOC-002, SOC-004, SOC-005, SOC-016 -- chaque critere a son controle ; {reportes.Count} partie(s) REPORTEE(S), nommee(s) ci-dessus (recopie de la phase 2, 40 bases)."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Les lignes de journal du serveur, pour lire la trace d'une panne (SOC-002 CA06).</summary>
sealed class JournalCapture : ILoggerProvider
{
    public sealed record Ligne(int Id, LogLevel Niveau, string Texte);
    private readonly ConcurrentQueue<Ligne> _lignes = new();
    public IReadOnlyList<Ligne> Lignes => [.. _lignes];
    public void Vider() => _lignes.Clear();
    public ILogger CreateLogger(string categorie) => new Ecrivain(this);
    public void Dispose() { }

    private sealed class Ecrivain(JournalCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Warning;
        public void Log<TState>(LogLevel niveau, EventId id, TState etat, Exception? e, Func<TState, Exception?, string> format)
            => capture._lignes.Enqueue(new Ligne(id.Id, niveau, format(etat, e)));
    }
}
