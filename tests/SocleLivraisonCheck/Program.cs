using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Lanceur;
using RigV3.Livraison;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;

// ============================================================
// BANC DU LOT 12 : SOC-009 (livrer une version de la base en trois vagues), SOC-010 (la partie
// serveur chez Amitel et sur place outre-mer), SOC-012 (lancer la V3 depuis le serveur RDS, a la
// bonne version). Un controle par critere faisable sur ce poste, qui cite son id.
//
// Le decor : une base commune (catalogue) et SIX bases de greffe VIDES sur le SQL Server local,
// comme les 40 bases avant la premiere repetition generale (SOC-009 T05) :
//   beta : 2801 Chartres, 4301 Le Puy ; metropole : 0101 Bourg, 8002 Amiens ;
//   outre-mer : 9711 Basse-Terre (sa base porte « Panne » dans son nom : la migration d'essai
//   SOC-999 y echoue, et seulement la), 9871 Papeete.
// Trois paquets : P1 = Base\migrations du depot (2026.1.0, la version de Directory.Build.props),
// P2 = P1 + une table d'essai (2026.1.1, mineure), P3 = P2 + la panne simulee (2026.2.0, majeure).
// Mode d'emploi : docs/reconstruction/socle-exploitation.md.
// ============================================================

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrationsDepot = Path.Combine(racine, "Base", "migrations");
var temp = Path.Combine(Path.GetTempPath(), "SocleLivraisonCheck-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(temp);

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
static string Court(string? texte, int n) => texte is null ? "" : texte.Length <= n ? texte : texte[..n];
static async Task<Exception?> Lever(Func<Task> action)
{
    try { await action(); return null; }
    catch (Exception e) { return e; }
}
static async Task Executer(string connexion, string sql)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    foreach (var lot in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(l => l.Trim().Length > 0))
    {
        await using var cmd = new SqlCommand(lot, cn) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }
}
static async Task<object?> Scalaire(string connexion, string sql)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    var v = await cmd.ExecuteScalarAsync();
    return v is DBNull ? null : v;
}
static async Task<string?> VersionDe(string connexion)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    return await JournalLivraisons.VersionAsync(cn);
}
static async Task<IReadOnlyList<LivraisonInscrite>> JournalDe(string connexion)
{
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    return await JournalLivraisons.ListerAsync(cn);
}
static async Task<List<(short Code, string Libelle, string Detail)>> Evenements(string connexion)
{
    var liste = new List<(short, string, string)>();
    await using var cn = new SqlConnection(connexion);
    await cn.OpenAsync();
    await using var cmd = new SqlCommand(
        "IF OBJECT_ID(N'soc.EvenementTechnique') IS NOT NULL SELECT Code, Libelle, Detail FROM soc.EvenementTechnique WHERE Code IN (250, 255) ORDER BY Id;", cn);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) liste.Add((r.GetInt16(0), r.GetString(1), r.GetString(2)));
    return liste;
}
// La version de ce banc : celle de Directory.Build.props, comme l'ecran, le serveur et l'outil.
var versionDepot = VersionV3.Normaliser(typeof(Livreur).Assembly
    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion);

var serveurBancs = BaseJetable.ServeurDesBancs();
var instance = new SqlConnectionStringBuilder(serveurBancs).DataSource;
var authentification = new SqlConnectionStringBuilder(serveurBancs);
authentification.Remove("Data Source");
authentification.Remove("Initial Catalog");

await using var commune = await BaseJetable.CreerAsync("SLCommune");
await using var b2801 = await BaseJetable.CreerAsync("SLBeta2801");
await using var b4301 = await BaseJetable.CreerAsync("SLBeta4301");
await using var b0101 = await BaseJetable.CreerAsync("SLMetro0101");
await using var b8002 = await BaseJetable.CreerAsync("SLMetro8002");
await using var b9711 = await BaseJetable.CreerAsync("SLOutreMerPanne9711");
await using var b9871 = await BaseJetable.CreerAsync("SLOutreMer9871");
await using var communeDemarrage = await BaseJetable.CreerAsync("SLCommuneDemarrage");
await using var communV2 = await BaseJetable.CreerAsync("SLCommunV2");
await using var commune40 = await BaseJetable.CreerAsync("SLCommune40");
var bases = new Dictionary<string, BaseJetable>
{
    ["2801"] = b2801, ["4301"] = b4301, ["0101"] = b0101, ["8002"] = b8002, ["9711"] = b9711, ["9871"] = b9871,
};
try
{
    await Migrateur.AppliquerAsync(commune.Connexion, migrationsDepot);
    foreach (var (code, nom, beta, om, fuseau) in new[]
             {
                 ("2801", "Chartres", true, false, "Romance Standard Time"), ("4301", "Le Puy", true, false, "Romance Standard Time"),
                 ("0101", "Bourg en Bresse", false, false, "Romance Standard Time"), ("8002", "Amiens", false, false, "Romance Standard Time"),
                 ("9711", "Basse-Terre", false, true, "SA Western Standard Time"), ("9871", "Papeete", false, true, "Hawaiian Standard Time"),
             })
    {
        await using var cn = new SqlConnection(commune.Connexion);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, BaseV3CreeeLe, EstBeta, EstOutreMer, Fuseau, PassageLocal, ModifiePar, ModifieLe) " +
            "VALUES (@c, @n, @s, @v2, @v3, SYSUTCDATETIME(), @b, @o, @f, NULL, 'banc SocleLivraison', SYSUTCDATETIME());", cn);
        cmd.Parameters.AddWithValue("@c", code);
        cmd.Parameters.AddWithValue("@n", nom);
        cmd.Parameters.AddWithValue("@s", instance);
        cmd.Parameters.AddWithValue("@v2", "RIG_" + code);
        cmd.Parameters.AddWithValue("@v3", bases[code].Nom);
        cmd.Parameters.AddWithValue("@b", beta);
        cmd.Parameters.AddWithValue("@o", om);
        cmd.Parameters.AddWithValue("@f", fuseau);
        await cmd.ExecuteNonQueryAsync();
    }
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [CatalogueGreffes.CleAuthentification] = authentification.ConnectionString,
    }).Build();
    var catalogue = new CatalogueGreffes(new BaseRig(commune.Connexion), configuration);
    var fiches = await catalogue.ListerAsync();
    var livreur = new Livreur(catalogue);

    // --- Les trois paquets ---------------------------------------------------------------------
    string Dossier(string nom, params (string Fichier, string Texte)[] essais)
    {
        var d = Path.Combine(temp, nom);
        Directory.CreateDirectory(d);
        foreach (var f in Directory.GetFiles(migrationsDepot, "*.sql")) File.Copy(f, Path.Combine(d, Path.GetFileName(f)));
        foreach (var (fichier, texte) in essais) File.WriteAllText(Path.Combine(d, fichier), texte);
        return d;
    }
    var essai = ("SOC-998_essai-livraison.sql",
        "-- Migration d'ESSAI du banc SocleLivraisonCheck (jamais dans Base\\migrations) : une table de plus.\n-- apres: SOC-005\n" +
        "CREATE TABLE soc.EssaiLivraison (Id int NOT NULL CONSTRAINT PK_EssaiLivraison PRIMARY KEY);");
    var panne = ("SOC-999_panne-simulee.sql",
        "-- Migration d'ESSAI : echoue sur la seule base dont le nom porte « Panne » (SOC-009 CA04).\n-- apres: SOC-998\n" +
        "IF DB_NAME() LIKE N'%Panne%' THROW 50999, N'panne simulee par le banc SocleLivraisonCheck', 1;");
    var p1 = await PaquetLivraison.ConstruireAsync(migrationsDepot, Path.Combine(temp, "P1"), versionDepot);
    var p2 = await PaquetLivraison.ConstruireAsync(Dossier("src-P2", essai), Path.Combine(temp, "P2"), "2026.1.1");
    var p3 = await PaquetLivraison.ConstruireAsync(Dossier("src-P3", essai, panne), Path.Combine(temp, "P3"), "2026.2.0");

    // =====================================================================================
    // SOC-009 — livrer une nouvelle version de la base en trois vagues
    // =====================================================================================
    Console.WriteLine("--- SOC-009 : paquet et vagues");
    Exiger(versionDepot == "2026.1.0" && p1.Version == versionDepot,
        $"SOC-009 CA01 : le paquet prend la version de Directory.Build.props, la seule source ({p1.Version})");
    Exiger(PaquetLivraison.Lire(p1.Dossier).Empreinte == p1.Empreinte && p1.Manifeste.Migrations.Count == Directory.GetFiles(migrationsDepot, "*.sql").Length
           && p1.Manifeste.Schema.Count > 100,
        $"SOC-009 CA01 : le paquet relu porte toutes les migrations du depot ({p1.Manifeste.Migrations.Count}) et le schema mesure ({p1.Manifeste.Schema.Count} objets)");
    var p1bis = await PaquetLivraison.ConstruireAsync(migrationsDepot, Path.Combine(temp, "P1bis"), "2026.9.9");
    Exiger(p1bis.Empreinte == p1.Empreinte && p1bis.Manifeste.Schema.SequenceEqual(p1.Manifeste.Schema),
        "SOC-009 CA01 : la meme source donne le meme paquet (empreinte et schema identiques)");
    var altere = Path.Combine(temp, "P1-altere");
    Directory.CreateDirectory(Path.Combine(altere, PaquetLivraison.DossierScripts));
    File.Copy(Path.Combine(p1.Dossier, PaquetLivraison.NomManifeste), Path.Combine(altere, PaquetLivraison.NomManifeste));
    foreach (var f in Directory.GetFiles(p1.Migrations)) File.Copy(f, Path.Combine(altere, PaquetLivraison.DossierScripts, Path.GetFileName(f)));
    var retouche = Directory.GetFiles(Path.Combine(altere, PaquetLivraison.DossierScripts)).First();
    File.AppendAllText(retouche, "\n-- retouche a la main apres construction");
    var refusAltere = await Lever(() => Task.FromResult(PaquetLivraison.Lire(altere)));
    Exiger(refusAltere is InvalidOperationException e1 && e1.Message.Contains(Path.GetFileNameWithoutExtension(retouche)),
        $"SOC-009 CA01 : un script retouche apres coup fait refuser le paquet, en le nommant ({Court(refusAltere?.Message, 90)})");

    Exiger(Vagues.Greffes(Vague.Beta, fiches).Select(f => f.Code).SequenceEqual(["2801", "4301"])
           && Vagues.Greffes(Vague.Metropole, fiches).Select(f => f.Code).SequenceEqual(["0101", "8002"])
           && Vagues.Greffes(Vague.OutreMer, fiches).Select(f => f.Code).SequenceEqual(["9711", "9871"])
           && Vagues.Libelle(Vague.Metropole) == "Métropole (hors bêta-testeurs)",
        "SOC-009 CA02 : les trois vagues de la V2 (beta ; metropole hors beta ; outre-mer), par code, libelles de la V2");

    // Vague beta : seules ses deux bases recoivent la version.
    var rBeta = await livreur.LivrerAsync(p1, Vague.Beta);
    var versions = new Dictionary<string, string?>();
    foreach (var (code, b) in bases) versions[code] = await VersionDe(b.Connexion);
    Exiger(rBeta.Reussie && versions["2801"] == p1.Version && versions["4301"] == p1.Version
           && versions["0101"] is null && versions["8002"] is null && versions["9711"] is null && versions["9871"] is null,
        "SOC-009 CA02 : vague « beta » -> 2801 et 4301 en " + p1.Version + ", les quatre autres bases non touchees");
    Exiger(rBeta.Bases.All(b => b.Resultat == Resultats.Livree && b.VersionAvant is null && b.Jouees.Count == p1.Manifeste.Migrations.Count),
        "SOC-009 T05 : la premiere livraison cree tout dans des bases VIDES, par le meme outil");
    var rMetro = await livreur.LivrerAsync(p1, Vague.Metropole);
    Exiger(rMetro.Reussie && await VersionDe(b0101.Connexion) == p1.Version && await VersionDe(b8002.Connexion) == p1.Version
           && await VersionDe(b9711.Connexion) is null,
        "SOC-009 CA02 : vague « metropole » -> 0101 et 8002, l'outre-mer toujours non touche");
    var rOm = await livreur.LivrerAsync(p1, Vague.OutreMer);
    Exiger(rOm.Reussie && await VersionDe(b9711.Connexion) == p1.Version && await VersionDe(b9871.Connexion) == p1.Version,
        "SOC-009 CA02 : vague « outre-mer » -> 9711 et 9871 : les 6 bases portent " + p1.Version);
    var j4301 = await JournalDe(b4301.Connexion);
    Exiger(j4301 is [{ Resultat: JournalLivraisons.Livree, Vague: "beta" } l1] && l1.Version == p1.Version && l1.Paquet == p1.Empreinte,
        "SOC-009 CA01 : chaque base enregistre la version qu'elle porte (rigv3.Livraison : version, vague, paquet, resultat)");
    var conformes = 0;
    foreach (var b in bases.Values)
        if ((await Livreur.EcartsAsync(b.Connexion, p1.Manifeste.Schema, p1.Manifeste.Collation)).Count == 0) conformes++;
    Exiger(conformes == 6, $"SOC-009 CA03 : apres livraison, les 6 bases ont exactement le schema livre ({conformes}/6)");

    // CA06 : l'evenement d'installation, 250 a la premiere version.
    var ev = await Evenements(b9871.Connexion);
    Exiger(ev is [{ Code: 250 } e250] && e250.Libelle == "Installation d'une nouvelle version majeure du logiciel" && e250.Detail.Contains(p1.Version),
        $"SOC-009 CA06 : livraison reussie -> evenement 250 « {ev.FirstOrDefault().Libelle} » dans le journal de la base");

    // Version mineure sur la beta : evenement 255 ; rejouer le meme paquet ne touche a rien.
    var rBeta2 = await livreur.LivrerAsync(p2, Vague.Beta);
    var ev4301 = await Evenements(b4301.Connexion);
    Exiger(rBeta2.Reussie && ev4301.Select(e => e.Code).SequenceEqual(new short[] { 250, 255 })
           && ev4301[1].Libelle == "Installation d'une nouvelle version mineure du logiciel",
        "SOC-009 CA06 : 2026.1.0 -> 2026.1.1 (revision seule) -> evenement 255, version mineure");
    var rBeta2bis = await livreur.LivrerAsync(p2, Vague.Beta);
    Exiger(rBeta2bis.Reussie && rBeta2bis.Bases.All(b => b.Resultat == Resultats.DejaAJour) && (await Evenements(b4301.Connexion)).Count == 2,
        "SOC-009 CA02 (jumeau) : une base deja a la version (greffe beta ET outre-mer, livre deux fois) n'est pas retouchee");

    // CA03 : l'ecart avant livraison, base par base, et l'arret.
    Console.WriteLine("--- SOC-009 : controle d'ecart");
    await Executer(b0101.Connexion, "DROP INDEX IX_JournalDroits_Instant ON soc.JournalDroits;");
    await Executer(b8002.Connexion, "CREATE TABLE soc.Bricole (Id int NULL);");
    var controle = await livreur.LivrerAsync(p2, Vague.Metropole, controleSeul: true);
    Exiger(controle.ArretSur == "0101" && controle.Bases[0].Ecarts.Any(x => x.StartsWith("manquant : index soc.JournalDroits IX_JournalDroits_Instant"))
           && controle.Bases[1].Resultat == Resultats.NonTouchee,
        $"SOC-009 CA03 : controle seul -> 0101 en ecart, objet nomme (« {Court(controle.Bases[0].Ecarts.FirstOrDefault(), 60)}… »), arret avant 8002");
    var rEcart1 = await livreur.LivrerAsync(p2, Vague.Metropole);
    Exiger(rEcart1.ArretSur == "0101" && rEcart1.Bases[0].Resultat == Resultats.Ecart && rEcart1.Bases[1].Resultat == Resultats.NonTouchee
           && await VersionDe(b0101.Connexion) == p1.Version && await VersionDe(b8002.Connexion) == p1.Version
           && await Scalaire(b0101.Connexion, "SELECT OBJECT_ID(N'soc.EssaiLivraison')") is null,
        "SOC-009 CA03 : livraison -> arret sur 0101 (base non touchee, toujours en " + p1.Version + "), 8002 non touchee");
    Exiger((await JournalDe(b0101.Connexion)).Last() is { Resultat: JournalLivraisons.Ecart } je && je.Detail!.Contains("IX_JournalDroits_Instant"),
        "SOC-009 CA03 : l'ecart est inscrit au journal des livraisons de la base, objet par objet");
    await Executer(b0101.Connexion, "CREATE INDEX IX_JournalDroits_Instant ON soc.JournalDroits (Instant DESC);");
    var rEcart2 = await livreur.LivrerAsync(p2, Vague.Metropole);
    Exiger(rEcart2.ArretSur == "8002" && rEcart2.Bases[0].Resultat == Resultats.Livree
           && rEcart2.Bases[1].Ecarts.Any(x => x == "en trop : table soc.Bricole") && await VersionDe(b8002.Connexion) == p1.Version,
        "SOC-009 CA03 : base reparee -> 0101 livree ; 8002 porte un objet absent de la version (« en trop : table soc.Bricole ») -> arret");
    await Executer(b8002.Connexion, "DROP TABLE soc.Bricole;");
    var rEcart3 = await livreur.LivrerAsync(p2, Vague.Metropole);
    Exiger(rEcart3.Reussie && rEcart3.Bases[0].Resultat == Resultats.DejaAJour && rEcart3.Bases[1].Resultat == Resultats.Livree,
        "SOC-009 CA03 (jumeau) : sans ecart, la vague va au bout (0101 deja a jour, 8002 livree)");
    var aRebours = await livreur.LivrerAsync(p1, Vague.Metropole);
    Exiger(aRebours.ArretSur == "0101" && aRebours.Bases[0].Erreur!.Contains("plus recente"),
        "SOC-009 CA03 : un paquet plus ancien que la base est refuse (pas de livraison a rebours)");

    // CA04 : echec d'une mise a jour -> retour a la version precedente, les autres bases non touchees.
    Console.WriteLine("--- SOC-009 : echec et retour");
    var schemaAvant = await BaseGreffe.SchemaAsync(b9711.Connexion);
    var migrationsAvant = await Migrateur.JoueesAsync(b9711.Connexion);
    var rPanne = await livreur.LivrerAsync(p3, Vague.OutreMer);
    var etat9711 = rPanne.Bases[0];
    Exiger(rPanne.ArretSur == "9711" && etat9711.Resultat == Resultats.Echec && etat9711.Revenue
           && etat9711.Jouees.Contains("SOC-998_essai-livraison") && etat9711.Erreur!.Contains("panne simulee"),
        "SOC-009 CA04 : la panne est au rapport (9711 en echec, cause en clair, apres une migration deja validee)");
    Exiger(await VersionDe(b9711.Connexion) == p1.Version && await Scalaire(b9711.Connexion, "SELECT OBJECT_ID(N'soc.EssaiLivraison')") is null
           && (await BaseGreffe.SchemaAsync(b9711.Connexion)).SequenceEqual(schemaAvant)
           && (await Migrateur.JoueesAsync(b9711.Connexion)).SequenceEqual(migrationsAvant),
        "SOC-009 CA04 : 9711 est revenue a sa version precedente (" + p1.Version + ") : schema et journal des migrations d'avant, migration validee comprise");
    Exiger(rPanne.Bases[1].Resultat == Resultats.NonTouchee && await VersionDe(b9871.Connexion) == p1.Version
           && await Scalaire(b9871.Connexion, "SELECT OBJECT_ID(N'soc.EssaiLivraison')") is null,
        "SOC-009 CA04 : 9871, l'autre base de la vague, n'est pas touchee");
    Exiger((await JournalDe(b9711.Connexion)).Last() is { Resultat: JournalLivraisons.Echec, Version: "2026.2.0" } jp && jp.Detail!.Contains("panne simulee")
           && !(await Evenements(b9711.Connexion)).Any(e => e.Detail.Contains("2026.2.0"))
           && await Scalaire(commune.Connexion, $"SELECT COUNT(*) FROM sys.databases WHERE source_database_id = DB_ID(N'{b9711.Nom}')") is 0,
        "SOC-009 CA04 / CA06 (jumeau) : l'echec reste au journal de la base apres le retour, aucun evenement d'installation, aucun instantane laisse");
    var rP3Beta = await livreur.LivrerAsync(p3, Vague.Beta);
    var ev2801 = await Evenements(b2801.Connexion);
    Exiger(rP3Beta.Reussie && ev2801.Count == 3 && ev2801[2].Code == 250 && ev2801[2].Detail.Contains("2026.2.0"),
        "SOC-009 CA06 : 2026.1.1 -> 2026.2.0 (version qui change) -> evenement 250, version majeure ; P3 passe la ou la panne n'est pas");

    // =====================================================================================
    // SOC-009 CA05 / SOC-012 — la partie serveur, l'ecran et la base a la meme version
    // =====================================================================================
    Console.WriteLine("--- SOC-009 CA05 et SOC-012 : versions au demarrage");
    var journalServeur = new ConcurrentQueue<string>();
    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Logging.AddProvider(new JournalDuBanc(journalServeur));
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();
    var http = app.GetTestClient();
    var client = new ClientServeur(http.BaseAddress!, app.GetTestServer().CreateHandler());
    var vServeur = await client.LireAsync<VersionServeur>(RoutesVersions.Serveur);
    Exiger(vServeur.Version == versionDepot && vServeur.Site == "developpement",
        $"SOC-012 T04 : GET {RoutesVersions.Serveur} -> la version du serveur ({vServeur.Version}), celle de Directory.Build.props");
    var v9871 = await client.LireAsync<VersionsGreffe>(RoutesVersions.Pour("9871"));
    var v4301 = await client.LireAsync<VersionsGreffe>(RoutesVersions.Pour("4301"));
    Exiger(v9871.Concordent && v9871.Base == p1.Version && v9871.Message is null,
        "SOC-009 CA05 (jumeau) : base 9871 en " + p1.Version + " = serveur -> concordent");
    Exiger(!v4301.Concordent && v4301.Base == "2026.2.0" && v4301.Message!.Contains(versionDepot) && v4301.Message.Contains("2026.2.0"),
        $"SOC-009 CA05 : base 4301 en 2026.2.0, serveur en {versionDepot} -> refus qui donne les deux versions (« {Court(v4301.Message, 80)}… »)");
    var inconnu = await http.GetAsync(RoutesVersions.Pour("0000"));
    Exiger(inconnu.StatusCode == HttpStatusCode.NotFound, "SOC-012 T04 : greffe inconnu du catalogue -> 404, sans erreur de base");

    // Le serveur refuse de DEMARRER sur une base commune d'une autre version.
    await using (var cn = new SqlConnection(communeDemarrage.Connexion))
    {
        await cn.OpenAsync();
        await JournalLivraisons.CreerAsync(cn);
        await JournalLivraisons.InscrireAsync(cn, null, "2026.0.9", "commune", new string('0', 64), DateTime.UtcNow, JournalLivraisons.Livree, null);
    }
    WebApplication Serveur(string connexion) => ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Critical);
        s.Configuration["ConnectionStrings:RigV3"] = connexion;
    });
    var refusDemarrage = await Lever(async () => { await using var a = Serveur(communeDemarrage.Connexion); await a.StartAsync(); });
    Exiger(refusDemarrage is InvalidOperationException rd && rd.Message.Contains("2026.0.9") && rd.Message.Contains(versionDepot)
           && rd.Message.Contains(communeDemarrage.Nom) && !rd.Message.Contains("SqlException"),
        $"SOC-009 CA05 : base commune en 2026.0.9 -> la partie serveur refuse de demarrer, message clair (« {Court(refusDemarrage?.Message, 110)}… »)");
    await using (var cn = new SqlConnection(communeDemarrage.Connexion))
    {
        await cn.OpenAsync();
        await JournalLivraisons.InscrireAsync(cn, null, versionDepot, "commune", new string('0', 64), DateTime.UtcNow, JournalLivraisons.Livree, null);
    }
    var demarrageOk = await Lever(async () => { await using var a = Serveur(communeDemarrage.Connexion); await a.StartAsync(); await a.StopAsync(); });
    Exiger(demarrageOk is null, "SOC-009 CA05 (jumeau) : base commune livree en " + versionDepot + " -> la partie serveur demarre");

    // L'ecran : le lanceur juge la version installee sur le serveur RDS.
    string Installer(string nom, params string[] versionsCompletes)
    {
        var r = Path.Combine(temp, nom);
        foreach (var v in versionsCompletes)
        {
            var d = Path.Combine(r, Installation.DossierVersions, v);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, Installation.NomEcran), "ecran d'essai");
            File.WriteAllText(Path.Combine(d, Installation.NomMarque),
                JsonSerializer.Serialize(new MarqueInstallation(v, DateTime.UtcNow, "banc", "banc"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        return r;
    }
    var rdsPerime = Installer("rds-perime", "2026.0.9");
    var rdsAJour = Installer("rds-a-jour", "2026.0.9", versionDepot);
    string? AvecWebView2() => "131.0.2903.70";
    var perime = await Verification.JugerAsync(rdsPerime, AvecWebView2, client, [], null);
    Exiger(!perime.Ouvrir && perime.Ecart == "ecran" && perime.Message.Contains("2026.0.9") && perime.Message.Contains(versionDepot) && perime.Remonter,
        $"SOC-012 CA03 / SOC-009 CA05 : ecran 2026.0.9, serveur {versionDepot} -> le lanceur refuse avec un message (« {perime.Titre} : {Court(perime.Message, 90)}… »)");
    var aJour = await Verification.JugerAsync(rdsAJour, AvecWebView2, client, [], null);
    Exiger(aJour.Ouvrir && aJour.VersionEcran == versionDepot,
        "SOC-012 CA05 (jumeau) : la version la plus recente posee a cote (" + versionDepot + ") -> le lanceur ouvre celle-la");
    var baseAutre = await Verification.JugerAsync(rdsAJour, AvecWebView2, client, ["greffe=4301"], null);
    var baseMeme = await Verification.JugerAsync(rdsAJour, AvecWebView2, client, [], "9871");
    Exiger(!baseAutre.Ouvrir && baseAutre.Ecart == "base" && baseAutre.Message.Contains("2026.2.0") && baseMeme.Ouvrir && baseMeme.VersionBase == p1.Version,
        "SOC-009 CA05 : greffe 4301 (base en 2026.2.0) refuse a l'ouverture ; 9871 (seul greffe du poste, base a jour) ouvert");

    // SOC-012 CA04 : composant WebView2 absent -> message clair, et l'exploitation le sait.
    var sansComposant = await Verification.JugerAsync(rdsAJour, () => null, client, ["greffe=9871"], null);
    var remontee = await Verification.RemonterAsync(client, sansComposant);
    Exiger(!sansComposant.Ouvrir && sansComposant.Ecart == "composant" && sansComposant.Message.Contains("WebView2") && remontee is null
           && journalServeur.Any(l => l.Contains("Ecran non demarre") && l.Contains("WebView2") && l.Contains("9871")),
        "SOC-012 CA04 : composant absent -> message clair a l'agent, et l'erreur remonte au journal de la partie serveur");

    // SOC-012 CA03 : la veille, ecran deja ouvert.
    var veille = Verification.Veiller("2026.1.0", new VersionServeur("2026.1.1", "Amitel"), "8002");
    Exiger(veille is { Ouvrir: false } && veille.Message.Contains("obsolète (2026.1.0)") && veille.Message.Contains("redémarrer pour être à jour (2026.1.1)")
           && Verification.Veiller("2026.1.1", new VersionServeur("2026.1.1", "Amitel"), "8002") is null
           && Verification.Veiller("2026.10.0", new VersionServeur("2026.9.4", "Amitel"), null) is null,
        "SOC-012 CA03 : ecran ouvert plus ancien que le serveur -> l'avertissement de la V2 ; meme version ou 2026.10 > 2026.9 (#716) -> rien");

    // SOC-012 CA01 / CA05 : la pose a cote, sans fermer les sessions.
    Console.WriteLine("--- SOC-012 : installation sur le serveur RDS");
    var rds = Installer("rds-pose", "2026.0.9");
    Directory.CreateDirectory(Path.Combine(rds, Installation.DossierVersions, "2026.3.0"));   // pose interrompue : sans marque
    var source = Path.Combine(temp, "publication-ecran");
    Directory.CreateDirectory(Path.Combine(source, "wwwroot"));
    File.Copy(Environment.ProcessPath!, Path.Combine(source, Installation.NomEcran));        // un exe qui porte la version du depot
    File.WriteAllText(Path.Combine(source, "wwwroot", "index.html"), "<html></html>");
    VersionInstallee posee;
    await using (var enCours = new FileStream(Path.Combine(rds, Installation.DossierVersions, "2026.0.9", Installation.NomEcran),
                                              FileMode.Open, FileAccess.Read, FileShare.Read))   // une session l'execute
        posee = Installation.Poser(source, rds);
    Exiger(posee.Version == versionDepot && File.Exists(Path.Combine(posee.Dossier, "wwwroot", "index.html"))
           && Installation.Courante(rds)?.Version == versionDepot
           && File.Exists(Path.Combine(rds, Installation.DossierVersions, "2026.0.9", Installation.NomEcran)),
        "SOC-012 CA05 : la nouvelle version se pose A COTE pendant qu'une session tient l'ancienne ; elle sert au prochain lancement");
    Exiger(Installation.Lister(rds).Select(v => v.Version).SequenceEqual([versionDepot, "2026.0.9"]),
        "SOC-012 CA05 : une version sans marque (pose interrompue, 2026.3.0) n'est jamais ouverte");
    Exiger(await Lever(() => Task.FromResult(Installation.Poser(source, rds))) is InvalidOperationException,
        "SOC-012 T01 : une version deja posee ne se recopie pas par-dessus");
    var lanceurSource = Path.Combine(temp, "publication-lanceur");
    Directory.CreateDirectory(lanceurSource);
    File.WriteAllText(Path.Combine(lanceurSource, "RigV3Lanceur.dll"), "nouveau");
    File.WriteAllText(Path.Combine(rds, "RigV3Lanceur.dll"), "ancien");
    IReadOnlyList<string> misDeCote;
    await using (var tenu = new FileStream(Path.Combine(rds, "RigV3Lanceur.dll"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        misDeCote = Installation.PoserLanceur(lanceurSource, rds);
    Exiger(misDeCote.Count == 1 && File.ReadAllText(Path.Combine(rds, "RigV3Lanceur.dll")) == "nouveau" && File.ReadAllText(misDeCote[0]) == "ancien",
        "SOC-012 CA05 : le lanceur tenu par une session est renomme, le nouveau prend sa place (aucune session fermee)");

    // SOC-012 CA02 : les donnees du navigateur integre dans le profil de chaque agent.
    var pageWeb = File.ReadAllText(Path.Combine(racine, "Pont", "PageWeb.cs"));
    var programme = File.ReadAllText(Path.Combine(racine, "Lanceur", "Programme.cs"));
    Exiger(Regex.IsMatch(pageWeb, @"userDataFolder:\s*Path\.Combine\(\s*Environment\.GetFolderPath\(Environment\.SpecialFolder\.LocalApplicationData\)")
           && programme.Contains("SpecialFolder.LocalApplicationData") && !programme.Contains("AppContext.BaseDirectory, \"lanceur.log"),
        "SOC-012 CA02 : navigateur integre (Pont/PageWeb.cs) et journal du lanceur dans %LOCALAPPDATA% de l'agent : rien de partage entre sessions");

    // SOC-012 CA07 : une V3 par greffe et par session ; la relance ramene la fenetre.
    var ca07 = new List<(bool, string)>();
    var fil = new Thread(() =>
    {
        // Un verrou nomme appartient au fil qui le prend : tout le controle tourne sur ce fil.
        var prefixe = "RigV3Banc-" + Guid.NewGuid().ToString("N")[..8];
        using var signale = new ManualResetEventSlim();
        using var premier = InstanceUnique.Prendre("4301", signale.Set, prefixe)!;
        using var second = InstanceUnique.Prendre("4301", null, prefixe)!;
        using var autre = InstanceUnique.Prendre("2801", null, prefixe)!;
        ca07.Add((premier.Premier && !second.Premier && signale.Wait(TimeSpan.FromSeconds(5)),
            "SOC-012 CA07 : relance pour le meme greffe -> refusee, et la premiere ramene sa fenetre au premier plan"));
        ca07.Add((autre.Premier && InstanceUnique.Prendre(null, null, prefixe) is null,
            "SOC-012 CA07 (jumeau) : un autre greffe s'ouvre ; sans greffe connu au lancement, pas de verrou"));
    });
    fil.Start();
    fil.Join();
    foreach (var (ok, quoi) in ca07) Exiger(ok, quoi);

    // SOC-012 CA06 : le releve des versions de chaque site et de chaque serveur RDS.
    var sitesEssai = new Sites(new Ouverture([DayOfWeek.Monday], new TimeOnly(7, 0), new TimeOnly(20, 0)),
        [new Site("developpement", "banc", [instance], new BaseCommuneSite(instance, commune.Nom), http.BaseAddress!.ToString())]);
    var releve = await Releve.FaireAsync(sitesEssai,
        [new ServeurRds("RDS-A", "developpement", rdsAJour), new ServeurRds("RDS-B", "developpement", rdsPerime)],
        app.GetTestServer().CreateHandler());
    Exiger(releve.Count == 3 && releve[0] is { Piece: "serveur", Concorde: true } && releve.Single(l => l.Nom == "RDS-A").Concorde
           && releve.Single(l => l.Nom == "RDS-B") is { Concorde: false, Version: "2026.0.9" } rb && rb.Detail!.Contains(versionDepot),
        "SOC-012 CA06 : le releve compare chaque serveur RDS a la partie serveur de son site (RDS-B en 2026.0.9 -> ecart)");

    // =====================================================================================
    // SOC-010 — la partie serveur chez Amitel et sur place outre-mer
    // =====================================================================================
    Console.WriteLine("--- SOC-010 : sites de la partie serveur");
    var sites = Sites.Lire(Path.Combine(AppContext.BaseDirectory, Sites.NomFichier));
    await Migrateur.AppliquerAsync(commune40.Connexion, migrationsDepot);
    await Executer(communV2.Connexion, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "commun_rig_greffe.sql")));
    await using (var cnV2 = new SqlConnection(communV2.Connexion))
    await using (var cn40 = new SqlConnection(commune40.Connexion))
    {
        await cnV2.OpenAsync();
        await cn40.OpenAsync();
        await RepriseCatalogue.ReprendreAsync(cnV2, cn40, "banc SocleLivraison");
    }
    var les40 = await new CatalogueGreffes(new BaseRig(commune40.Connexion), configuration).ListerAsync();
    var repartition = sites.Repartir(les40);
    var parSite = repartition.GroupBy(a => a.Site ?? "?").ToDictionary(g => g.Key, g => g.Count());
    Exiger(les40.Count == 40 && repartition.All(a => a.Site is not null) && sites.Incoherences(les40).Count == 0,
        $"SOC-010 CA01 / CA02 : les 40 greffes reels ont chacun UN site, sans incoherence ({string.Join(", ", parSite.Select(p => $"{p.Key} {p.Value}"))})");
    Exiger(repartition.Where(a => !a.EstOutreMer).All(a => a.Site == "Amitel" && a.Instance == @"SQL-PROD\PROD"),
        @"SOC-010 CA01 : chaque greffe de metropole passe par la partie serveur d'Amitel, qui atteint sa base sur SQL-PROD\PROD");
    Exiger(repartition.Single(a => a.Greffe == "9871").Site == "Polynesie" && repartition.Single(a => a.Greffe == "9761").Site == "Reunion"
           && repartition.Where(a => a.EstOutreMer).All(a => a.Site != "Amitel"),
        "SOC-010 CA02 : chaque greffe d'outre-mer passe par la partie serveur de SON site (Papeete -> Polynesie, Mamoudzou -> Reunion), jamais par Amitel");
    var deplace = les40.Select(g => g.Code == "9871" ? g with { Serveur = @"SQL-PROD\PROD" } : g).ToList();
    Exiger(sites.Incoherences(deplace) is [var inco] && inco.Contains("9871") && inco.Contains("ocean"),
        "SOC-010 CA02 (contre-epreuve) : une base d'outre-mer posee sur SQL-PROD est signalee (elle serait atteinte a travers l'ocean)");

    // CA03 : le compte de l'agent et le greffe vise ; un greffe dont la base ne connait pas ce compte est refuse.
    await Executer(b0101.Connexion, """
        INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('0101', 'GREF', N'Greffe');
        INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleLivraison');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
        SELECT 'agent.bourg', 'BANC', 'Agent', 'ABOU', 'M.', '0101', s.Id, 0, 0, 0 FROM soc.Service s;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
        """);
    async Task<HttpStatusCode> Reglages(string greffe)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Reglages, greffe));
        req.Headers.Add(RoutesIdentite.EnTeteCompte, @"AMITEL\agent.bourg");
        return (await http.SendAsync(req)).StatusCode;
    }
    var chezLui = await Reglages("0101");
    var ailleurs = await Reglages("8002");
    Exiger(chezLui == HttpStatusCode.OK && ailleurs == HttpStatusCode.Forbidden,
        $"SOC-010 CA03 : la partie serveur juge le compte Windows dans la base du greffe vise : 0101 -> {(int)chezLui}, 8002 (compte inconnu) -> {(int)ailleurs}");

    // CA04 : arret et redemarrage ; la fenetre de mise a jour hors heures d'ouverture, heure locale.
    var libre = new TcpListener(IPAddress.Loopback, 0);
    libre.Start();
    var port = ((IPEndPoint)libre.LocalEndpoint).Port;
    libre.Stop();
    var adresse = $"http://127.0.0.1:{port}";
    WebApplication Kestrel() => ServeurRig.Construire(["--environment", "Development", "--urls", adresse], s =>
    {
        s.Logging.SetMinimumLevel(LogLevel.Critical);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    var ecranReseau = ClientServeur.Depuis(adresse)!;
    var avantArret = new List<VersionsGreffe>();
    await using (var k1 = Kestrel())
    {
        await k1.StartAsync();
        avantArret.Add(await ecranReseau.LireAsync<VersionsGreffe>(RoutesVersions.Pour("8002")));
        await k1.StopAsync();
    }
    var pendantArret = await Lever(() => ecranReseau.LireAsync<VersionServeur>(RoutesVersions.Serveur));
    VersionsGreffe apresRedemarrage;
    await using (var k2 = Kestrel())
    {
        await k2.StartAsync();
        apresRedemarrage = await ecranReseau.LireAsync<VersionsGreffe>(RoutesVersions.Pour("8002"));
        await k2.StopAsync();
    }
    Exiger(pendantArret is HttpRequestException && apresRedemarrage == avantArret[0] && apresRedemarrage.Base == "2026.1.1",
        "SOC-010 CA04 : partie serveur arretee puis redemarree -> le MEME client de l'ecran reprend sans etre rouvert, et relit a l'identique ce qui etait valide en base");
    var amitel = sites.Fuseaux(sites.Site("Amitel"), les40);
    var antilles = sites.Fuseaux(sites.Site("Antilles"), les40);
    var reunion = sites.Fuseaux(sites.Site("Reunion"), les40);
    var mardi = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
    Exiger(!sites.MiseAJourPermise(amitel, mardi.AddHours(10)) && sites.MiseAJourPermise(amitel, mardi.AddHours(22))
           && !sites.MiseAJourPermise(antilles, mardi.AddHours(22)) && sites.MiseAJourPermise(antilles, mardi.AddHours(3)),
        "SOC-010 CA04 : mise a jour permise hors ouverture A L'HEURE LOCALE : Amitel 23 h Paris oui, 11 h non ; Antilles 23 h locales (03 h UTC) oui, 18 h locales (22 h UTC) non");
    Exiger(reunion.Count == 2 && !sites.MiseAJourPermise(reunion, mardi.AddHours(16.5)) && sites.MiseAJourPermise(reunion, mardi.AddHours(17.5))
           && sites.ProchaineFenetre(reunion, mardi.AddHours(12)) is { } fr && fr.DebutUtc == mardi.AddHours(17),
        "SOC-010 CA04 : un site sert plusieurs fuseaux (Reunion UTC+4, Mayotte UTC+3) -> la fenetre attend que TOUS soient fermes (17 h UTC)");

    // CA06 : les traitements de fond tournent dans la partie serveur, jamais sur le poste de l'agent.
    var sourcesEcran = Directory.GetFiles(racine, "*.cs", SearchOption.TopDirectoryOnly)
        .Concat(Directory.GetFiles(Path.Combine(racine, "Pont"), "*.cs", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles(Path.Combine(racine, "Metier"), "*.cs", SearchOption.AllDirectories))
        .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\b(IHostedService|BackgroundService|AddHostedService)\b")).ToList();
    Exiger(sourcesEcran.Count == 0 && app.Services.GetServices<IHostedService>().Any(h => h.GetType().Name == "ControleVersionAuDemarrage"),
        "SOC-010 CA06 : aucun traitement de fond dans l'ecran (racine, Pont, Metier) ; la partie serveur heberge les siens (IHostedService)");

    // T02 : la configuration d'un site, sans mot de passe, lue par la partie serveur.
    var configAntilles = Sites.ConfigurationServeur(sites.Site("Antilles"));
    var lue = JsonDocument.Parse(configAntilles).RootElement;
    Exiger(lue.GetProperty("Site").GetProperty("Nom").GetString() == "Antilles"
           && lue.GetProperty("ConnectionStrings").GetProperty("RigV3").GetString()!.Contains("Server=RIGBD-ANTILLES;")
           && configAntilles.Contains("Integrated Security=True") && !configAntilles.Contains("Password", StringComparison.OrdinalIgnoreCase),
        "SOC-010 T02 : appsettings.Production.json du site Antilles : son nom, son instance, session Windows du service, aucun mot de passe");
    var dossierConfig = Path.Combine(temp, "config-antilles");
    Directory.CreateDirectory(dossierConfig);
    File.WriteAllText(Path.Combine(dossierConfig, "appsettings.json"), configAntilles);
    await using (var site = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Critical);
        s.Configuration.AddJsonFile(Path.Combine(dossierConfig, "appsettings.json"));
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
    }))
    {
        await site.StartAsync();
        var vSite = await site.GetTestClient().GetFromJsonAsync<VersionServeur>(RoutesVersions.Serveur);
        Exiger(vSite?.Site == "Antilles", "SOC-010 T02 : la partie serveur configuree pour les Antilles se presente comme telle (GET /socle/version)");
    }
    var programmeServeur = File.ReadAllText(Path.Combine(racine, "Serveur", "Program.cs"));
    Exiger(programmeServeur.Contains("UseWindowsService") && File.Exists(Path.Combine(AppContext.BaseDirectory, "installer-serveur.ps1"))
           && File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "installer-serveur.ps1")).Contains("sc.exe failure"),
        "SOC-010 T02 / CA04 : la partie serveur tourne en service Windows, redemarre seule apres une panne (sc.exe failure)");
}
finally
{
    try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "SocleLivraisonCheck : tout est vert." : $"SocleLivraisonCheck : {echecs.Count} controle(s) rouge(s).");
return echecs.Count == 0 ? 0 : 1;

/// <summary>Garde les lignes du journal du serveur (SOC-012 CA04 : l'erreur de demarrage remontee).</summary>
sealed class JournalDuBanc(ConcurrentQueue<string> lignes) : ILoggerProvider
{
    public ILogger CreateLogger(string categorie) => new Ecrivain(lignes);
    public void Dispose() { }

    private sealed class Ecrivain(ConcurrentQueue<string> lignes) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Warning;
        public void Log<TState>(LogLevel niveau, EventId id, TState state, Exception? e, Func<TState, Exception?, string> format)
            => lignes.Enqueue($"{niveau} {format(state, e)}");
    }
}
