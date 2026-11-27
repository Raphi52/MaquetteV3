using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
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
using RigV3Desktop.Pont;

// Banc du LOT 4 du plan de reconstruction (2026-11-20) : SOC-003, SOC-007, SOC-008.
//
// Un controle par critere du carnet (C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\
// carnet\socle.json), qui cite son id ; tout refus a son jumeau accepte.
//
// OU IL ECRIT : le SQL Server LOCAL seulement (docker compose up -d) -- ses bases jetables
// (BaseJetable), les bases V3 qu'il cree a cote d'elles, quelques comptes d'essai -- et il
// supprime tout en sortie.
// OU IL LIT : RIG_DEV sur SQL-DEV\DEV, par LectureV2 (un SELECT seul, jamais d'ecriture),
// pour la reprise des reglages. Variable RIGV3_V2_REGLAGES pour une autre base de lecture
// (RIG_DEV ou RIG_RECETTE seulement).
// COMMUN_RIG n'est pas lu : sa copie d'essai (commun_rig_greffe.sql) porte les 40 greffes
// relus le 2026-11-20 (controles\bases-greffes.json).

// fix-ok: relecture avant le premier lancement -- connexion a la base de T002 laissee ouverte, historique compte sur T001 au lieu de T002, date formatee selon la langue du poste, non-lus attendus calcules de travers.
// Puis, mesure aux lancements : CS0136 (deux cnV2 imbriques) ; erreur 2627 (le banc faisait partager une base
// V3 a deux greffes, ce que le catalogue refuse : c'est devenu un controle) ; erreur 241 (litteral SQL
// '2099-06-01T08:00' sans secondes, refuse par SQL Server avant le declencheur) ; CA02 rouge sur main + ce lot
// (COLLATE ecrit en clair dans SOC-001 : chaque base se compare desormais a la version livree a son classement).
// Puis, objection du controle final : les routes du module etaient ouvertes sans PolitiquesSocle.AgentAdmis (decision
// du plan, lot 2) ; le banc passe par l'identite du lot 2 (comptes dans soc.Compte) et controle l'acces de chaque route.
// Lot 9 (raccord aux droits du lot 3) : modifier exige le droit de modifier le greffe (regle V2 OPE_EDITER_GREFFE.cs:30-38)
// et ecrit le geste au journal dans la transaction ; les routes des reglages jugent l'agent dans la base du greffe.
// fix-ok: banc.socle n'etait qu'un agent admis ; la V2 ne laisse modifier un greffe qu'a un administrateur (OPE_EDITER_GREFFE.cs:37), il le devient, et banc.agent garde le role d'agent sans ce droit.
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
// Les comptes du banc, inscrits dans soc.Compte (lot 2) : la politique AgentAdmis les juge.
const string Agent = @"AMITEL\banc.socle";      // admis, administrateur d'Amitel : il a le droit de modifier le greffe
const string Refuse = @"AMITEL\banc.refuse";    // desactive : refuse (403)
const string SansDroit = @"AMITEL\banc.agent";  // admis, ni administrateur ni nomme par la V2 : lecture seule (403 a la modification)
const string NommeV2 = @"AMITEL\benier";        // admis, non administrateur, mais nomme par la V2 (OPE_EDITER_GREFFE.cs:31-35)
// Les memes comptes, dans la base ou la politique les juge : la commune (catalogue), ou celle d'un greffe (reglages).
const string ComptesDuBanc = """
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'EXPL', N'Exploitation');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleGreffes');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
    SELECT v.Compte, 'BANC', 'Socle', v.Abrege, 'M.', @greffe, s.Id, v.Desactive, v.Admin, v.Admin
    FROM (VALUES ('banc.socle', 'BSOC', 0, 1), ('banc.refuse', 'BREF', 1, 0), ('banc.agent', 'BAGT', 0, 0), ('benier', 'BENI', 0, 0))
         v (Compte, Abrege, Desactive, Admin)
    CROSS JOIN soc.Service s;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
    """;

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

// --- La source V2 : lecture seule, RIG_DEV ou RIG_RECETTE et rien d'autre -----------
var sourceV2 = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RIGV3_V2_REGLAGES")
    ?? @"Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15")
{
    ApplicationIntent = ApplicationIntent.ReadOnly,
};
if (sourceV2.InitialCatalog is not ("RIG_DEV" or "RIG_RECETTE"))
{
    Exiger(false, $"source V2 refusee : {sourceV2.InitialCatalog} (le banc ne lit que RIG_DEV ou RIG_RECETTE)");
    return 1;
}

// --- Le serveur des bancs (local) et ce qu'on y cree hors BaseJetable -----------------
var serveurBancs = BaseJetable.ServeurDesBancs();
var instance = new SqlConnectionStringBuilder(serveurBancs).DataSource;     // « 127.0.0.1,14330 »
var authentification = new SqlConnectionStringBuilder(serveurBancs);
authentification.Remove("Data Source");
string Chaine(string nomBase, string? compte = null, string? mdp = null)
{
    var b = new SqlConnectionStringBuilder(serveurBancs) { InitialCatalog = nomBase };
    if (compte is not null) { b.UserID = compte; b.Password = mdp; }
    return b.ConnectionString;
}
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
var basesV3 = new List<string>();
var comptes = new List<string>();
var sauvegardes = new List<string>();
var jeton = Guid.NewGuid().ToString("N")[..8];
var mdpEssai = "Banc_" + Guid.NewGuid().ToString("N")[..12] + "!a1";

await using var commune = await BaseJetable.CreerAsync("SocleGreffesCheck");
await using var communV2 = await BaseJetable.CreerAsync("SGCommunV2");
await using var v2a = await BaseJetable.CreerAsync("SGV2A");
await using var v2b = await BaseJetable.CreerAsync("SGV2B");
await using var compat = await BaseJetable.CreerAsync("SGCompat2017");
try
{
    await Migrateur.AppliquerAsync(commune.Connexion, migrations);
    // Les comptes du banc, dans la base que la politique AgentAdmis interroge (ConnectionStrings:RigV3).
    // Le domaine « AMITEL\ » de l'en-tete est retire par ServiceIdentite.CompteDe, comme en production.
    async Task InscrireComptes(string nomBase, string greffe) => await Executer(nomBase, ComptesDuBanc.Replace("@greffe", $"'{greffe}'"));
    await InscrireComptes(commune.Nom, "T001");
    await Executer(communV2.Nom, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "commun_rig_greffe.sql")));

    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration["ConnectionStrings:RigV3"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();
    var http = app.GetTestClient();
    var catalogue = app.Services.GetRequiredService<CatalogueGreffes>();
    // L'appel de <compte> : l'en-tete du lot 2, lu en developpement seulement ; null = sans session.
    async Task<HttpResponseMessage> Envoyer(HttpMethod verbe, string adresse, object? corps = null, string? compte = Agent)
    {
        var req = new HttpRequestMessage(verbe, adresse);
        if (corps is not null) req.Content = JsonContent.Create(corps);
        if (compte is not null) req.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        return await http.SendAsync(req);
    }
    async Task<T> Lire<T>(string adresse) => (await (await Envoyer(HttpMethod.Get, adresse)).Content.ReadFromJsonAsync<T>())!;

    // =====================================================================================
    // SOC-007 — le catalogue commun des greffes
    // =====================================================================================
    Console.WriteLine("--- SOC-007 : catalogue commun");
    int repris;
    await using (var cnCommunV2 = new SqlConnection(Chaine(communV2.Nom)))
    await using (var cnCommune = new SqlConnection(commune.Connexion))
    {
        await cnCommunV2.OpenAsync();
        await cnCommune.OpenAsync();
        repris = await RepriseCatalogue.ReprendreAsync(cnCommunV2, cnCommune, "banc SocleGreffesCheck");

        // CA01 + SOC-003 CA01 (informations tenues en commun) : la comparaison V2 / V3 ne trouve rien...
        var ecarts = await RepriseCatalogue.ComparerAsync(cnCommunV2, cnCommune);
        Exiger(ecarts.Count == 0,
            $"SOC-007 CA01 : catalogue et fiche commune V2 identiques, 54 colonnes par greffe comprises ({ecarts.Count} ecart(s){(ecarts.Count > 0 ? ", ex. " + ecarts[0] : "")})");
        // ... et trouve l'ecart qu'on y glisse (jumeau).
        await Executer(commune.Nom, "UPDATE soc.GreffeInfoV2 SET Texte = Texte + N'!' WHERE CodeGreffe = '8002' AND Colonne = 'GRF_PARAM_TEST';");
        var avecEcart = await RepriseCatalogue.ComparerAsync(cnCommunV2, cnCommune);
        Exiger(avecEcart.Count == 1 && avecEcart[0].Cle == "8002" && avecEcart[0].Colonne == "GRF_PARAM_TEST",
            "SOC-007 CA01 (jumeau) : un texte change d'un caractere dans la V3 est trouve par la comparaison");
        await Executer(commune.Nom, "UPDATE soc.GreffeInfoV2 SET Texte = LEFT(Texte, LEN(Texte) - 1) WHERE CodeGreffe = '8002' AND Colonne = 'GRF_PARAM_TEST';");
        var longueur = Convert.ToInt32(await Scalaire(commune.Nom,
            "SELECT LEN(Texte) FROM soc.GreffeInfoV2 WHERE CodeGreffe = '8002' AND Colonne = 'GRF_PARAM_TEST';"));
        Exiger(longueur == 9000, $"SOC-003 CA01 : une colonne varchar(max) de la fiche commune est reprise entiere ({longueur} caracteres sur 9000)");
    }

    // L'oracle : la copie d'essai V2 relue par le banc lui-meme.
    var v2Greffes = new Dictionary<string, (string Base, string Serveur, bool? Beta, bool Drom)>();
    await using (var cn = new SqlConnection(Chaine(communV2.Nom)))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT GRF_CODE_GREFFE, GRF_NOMBASE_BD, GRF_SERVEUR_BD, GRF_IS_GREFFE_BETA, GRF_IS_DROM FROM dbo.GREFFE WHERE GRF_IS_EXPLOIT = 1;", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            v2Greffes[r.GetString(0)] = (r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetBoolean(3), r.GetBoolean(4));
    }
    var fiches = (await Lire<FicheGreffe[]>(RoutesGreffes.Liste)).ToDictionary(f => f.Code);
    Exiger(repris == 40 && fiches.Count == 40 && v2Greffes.Count == 40 && v2Greffes.Keys.All(fiches.ContainsKey),
        $"SOC-007 CA01 : les 40 greffes exploites sont au catalogue ({repris} repris, {fiches.Count} fiches)");
    Exiger(v2Greffes.All(g => fiches[g.Key].BaseV2 == g.Value.Base && fiches[g.Key].Serveur == g.Value.Serveur),
        "SOC-007 CA01 : chaque fiche porte la base V2 et le serveur de la V2");
    Exiger(v2Greffes.All(g => fiches[g.Key].BaseV3 == "RIGV3_" + g.Value.Base["RIG_".Length..]),
        "SOC-007 CA01 : chaque fiche porte sa base V3 (RIG_X -> RIGV3_X), sur le meme serveur (une seule colonne Serveur)");
    Exiger(fiches.Values.Count(f => f.Serveur == @"SQL-PROD\PROD") == 34
           && fiches.Values.Where(f => f.Serveur != @"SQL-PROD\PROD").All(f => f.Serveur is "RIGBD-ANTILLES" or "RIGBD-REUNION" or "RIGBD-POLYNESIE")
           && fiches.Values.Count(f => f.Serveur != @"SQL-PROD\PROD") == 6,
        "SOC-007 CA01 : 34 greffes sur SQL-PROD\\PROD, 6 sur RIGBD-ANTILLES / RIGBD-REUNION / RIGBD-POLYNESIE");
    Exiger(v2Greffes.All(g => fiches[g.Key].EstOutreMer == g.Value.Drom) && fiches.Values.Count(f => f.EstOutreMer) == 6,
        "SOC-007 CA01 : la marque outre-mer est celle de la V2 (6 greffes, Papeete compris)");
    Exiger(!fiches.ContainsKey("9998") && !fiches.ContainsKey("9999"),
        "SOC-007 CA01 (jumeau) : les greffes non exploites de la V2 restent hors du catalogue");

    // CA02 : marque beta vide = non beta.
    var betas = fiches.Values.Where(f => f.EstBeta).Select(f => f.Code).Order().ToArray();
    Exiger(betas.SequenceEqual(["2801", "3802", "4302"]),
        $"SOC-007 CA02 : seuls Le Puy, Chartres et Vienne sont beta ({string.Join(", ", betas.Select(c => fiches[c].Nom))})");
    var vides = v2Greffes.Where(g => g.Value.Beta is null).Select(g => g.Key).ToList();
    Exiger(vides.Count == 36 && vides.All(c => !fiches[c].EstBeta),
        $"SOC-007 CA02 : les {vides.Count} greffes a marque beta vide sont repris « non beta »");
    Exiger(v2Greffes["6601"].Beta == false && !fiches["6601"].EstBeta,
        "SOC-007 CA02 (jumeau) : Perpignan, a 0 dans la V2, reste non beta ; la marque 1 reste beta");

    // CA03 : fuseaux de l'outre-mer.
    var attendus = new Dictionary<string, int>
    {
        ["9711"] = -240, ["9721"] = -240, ["9712"] = -240,   // Basse-Terre, Fort-de-France, Pointe-a-Pitre : UTC-4
        ["9741"] = 240,                                      // Saint-Denis : UTC+4
        ["9761"] = 180,                                      // Mamoudzou : UTC+3
        ["9871"] = -600,                                     // Papeete : UTC-10
    };
    foreach (var (code, minutes) in attendus)
    {
        var f = await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, code));
        Exiger(f.DecalageUtcMinutes == minutes && f.Fuseau is not null,
            $"SOC-007 CA03 : {f.Nom} est a UTC{minutes / 60:+0;-0} ({f.Fuseau}, {f.DecalageUtcMinutes} min)");
    }
    Exiger(fiches["8002"].Fuseau == "Romance Standard Time" && fiches["8002"].DecalageUtcMinutes == 60,
        "SOC-007 CA03 (jumeau) : un greffe de metropole (Amiens) est a l'heure de Paris, UTC+1");

    // =====================================================================================
    // SOC-008 — la base V3 de chaque greffe, a cote de sa base V2
    // =====================================================================================
    Console.WriteLine("--- SOC-008 : base V3 a cote de la base V2");
    // Deux bases V2 jetables qui different comme deux greffes : classement et mode de recuperation.
    await Executer("master", $"ALTER DATABASE [{v2a.Nom}] SET RECOVERY SIMPLE; ALTER DATABASE [{v2b.Nom}] SET RECOVERY FULL; " +
                             $"ALTER DATABASE [{v2b.Nom}] COLLATE French_CI_AS;");
    // Les comptes : un agent (ouvert sur sa base V2, comme aujourd'hui), la partie serveur, la recopie, l'exploitation.
    string Compte(string role) { var c = $"rigv3_banc_{role}_{jeton}"; comptes.Add(c); return c; }
    var (agent, serveur, recopie, exploitation) = (Compte("agent"), Compte("serveur"), Compte("recopie"), Compte("exploit"));
    foreach (var c in new[] { agent, serveur, recopie, exploitation })
        await Executer("master", $"CREATE LOGIN [{c}] WITH PASSWORD = N'{mdpEssai}', CHECK_POLICY = OFF;");
    await Executer(v2a.Nom, $"CREATE TABLE dbo.DOSSIER (ID int); CREATE USER [{agent}] FROM LOGIN [{agent}]; " +
                            $"ALTER ROLE db_datareader ADD MEMBER [{agent}]; ALTER ROLE db_datawriter ADD MEMBER [{agent}];");
    foreach (var (code, v2) in new[] { ("T001", v2a.Nom), ("T002", v2b.Nom) })
        await Executer(commune.Nom,
            "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
            $"VALUES ('{code}', 'Greffe d''essai {code}', '{instance}', '{v2}', '{CatalogueGreffes.NomBaseV3(v2)}', 0, 0, " +
            "'Romance Standard Time', 'banc', SYSDATETIME());");

    var tailles = new TaillesBaseV3(DonneesMo: 16, JournalMo: 8, CroissanceDonneesMo: 16, CroissanceJournalMo: 8);
    var creeeA = await BaseGreffe.CreerPourGreffeAsync(catalogue, "T001", migrations,
        new AccesBaseV3([serveur], [recopie], [exploitation]), tailles, "banc SocleGreffesCheck");
    basesV3.Add(creeeA.Base);
    var creeeB = await BaseGreffe.CreerPourGreffeAsync(catalogue, "T002", migrations, AccesBaseV3.Aucun, tailles, "banc SocleGreffesCheck");
    basesV3.Add(creeeB.Base);
    var (v3a, v3b) = (creeeA.Base, creeeB.Base);

    // CA01
    var surLaMemeInstance = Convert.ToInt32(await Scalaire("master",
        $"SELECT COUNT(*) FROM sys.databases WHERE name IN (N'{v2a.Nom}', N'{v3a}', N'{v2b.Nom}', N'{v3b}');"));
    Exiger(v3a == v2a.Nom + "_V3" && v3b == v2b.Nom + "_V3" && surLaMemeInstance == 4,
        "SOC-008 CA01 : chaque base V3 est creee sur l'instance de sa base V2, sous un nom deduit d'elle");
    var ficheA = await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, "T001"));
    Exiger(ficheA.BaseV3 == v3a && ficheA.BaseV3CreeeLe is not null && ficheA.Serveur == instance,
        "SOC-008 CA01 : la base creee est inscrite au catalogue (nom et date de creation)");
    Exiger(fiches.Values.All(f => f.BaseV3 == CatalogueGreffes.NomBaseV3(f.BaseV2) && f.BaseV3.StartsWith("RIGV3_")),
        "SOC-008 CA01 : le nom deduit vaut pour les 40 bases reelles (RIG_AMIENS -> RIGV3_AMIENS...)");
    var collA = (string)(await Scalaire("master", $"SELECT CONVERT(sysname, DATABASEPROPERTYEX(N'{v3a}', 'Collation'));"))!;
    var collB = (string)(await Scalaire("master", $"SELECT CONVERT(sysname, DATABASEPROPERTYEX(N'{v3b}', 'Collation'));"))!;
    var recA = (string)(await Scalaire("master", $"SELECT recovery_model_desc FROM sys.databases WHERE name = N'{v3a}';"))!;
    var recB = (string)(await Scalaire("master", $"SELECT recovery_model_desc FROM sys.databases WHERE name = N'{v3b}';"))!;
    Exiger(collA == BaseJetable.Collation && collB == "French_CI_AS" && recA == "SIMPLE" && recB == "FULL",
        $"SOC-008 T01 : la base V3 prend le classement et le mode de recuperation de SA base V2 ({collA}/{recA}, {collB}/{recB})");
    var tailleMo = Convert.ToInt32(await Scalaire("master",
        $"SELECT size * 8 / 1024 FROM sys.master_files WHERE database_id = DB_ID(N'{v3a}') AND type = 0;"));
    Exiger(tailleMo == 16, $"SOC-008 T01 : fichier de donnees a la taille initiale demandee ({tailleMo} Mo)");
    var absente = $"rigv3_banc_absente_{jeton}";
    var refus = await Lever(() => BaseGreffe.CreerAsync(Chaine("master"), absente, migrations, AccesBaseV3.Aucun, tailles));
    Exiger(refus is InvalidOperationException && refus.Message.Contains("n'est pas sur cette instance")
           && await Scalaire("master", $"SELECT DB_ID(N'{absente}_V3');") is null,
        "SOC-008 CA01 (jumeau) : sans sa base V2 sur l'instance, aucune base V3 n'est creee");
    var ecrase = await Lever(() => BaseGreffe.CreerAsync(Chaine("master"), v2a.Nom, migrations, AccesBaseV3.Aucun, tailles));
    Exiger(ecrase is InvalidOperationException && ecrase.Message.Contains("existe deja"),
        "SOC-008 CA01 (jumeau) : une base V3 qui existe ne se recree pas par-dessus");

    // CA02 + T05. La version livree = les migrations du depot jouees sur une base neuve, au meme
    // classement : la base commune (SQL_Latin1_General_CP1_CI_AS) pour V3A, une reference French_CI_AS pour V3B.
    var livre = await BaseGreffe.SchemaAsync(commune.Connexion);
    await using var referenceFr = await BaseJetable.CreerAsync("SGLivreFr");
    await Executer("master", $"ALTER DATABASE [{referenceFr.Nom}] COLLATE French_CI_AS;");
    await Migrateur.AppliquerAsync(referenceFr.Connexion, migrations);
    var livreFr = await BaseGreffe.SchemaAsync(referenceFr.Connexion);
    var schemaA = await BaseGreffe.SchemaAsync(Chaine(v3a));
    var schemaB = await BaseGreffe.SchemaAsync(Chaine(v3b));
    Exiger(livre.Count > 50 && schemaA.SequenceEqual(livre) && schemaB.SequenceEqual(livreFr),
        $"SOC-008 CA02 : chaque base V3 creee a le schema livre, a l'identique, au classement de sa base V2 ({livre.Count} elements)");
    static List<string> SansClassement(IEnumerable<string> schema)
        => [.. schema.Where(l => !l.StartsWith("classement de la base ", StringComparison.Ordinal))
                     .Select(l => Regex.Replace(l, @" collate \S+", ""))
                     .Order(StringComparer.Ordinal)];
    Exiger(SansClassement(schemaA).SequenceEqual(SansClassement(schemaB)) && !schemaA.SequenceEqual(schemaB),
        "SOC-008 CA02 : entre deux greffes de classements differents, seul le classement differe (tables, colonnes, index, roles identiques)");
    var derniere = Migrateur.Lire(migrations)[^1].Id;
    // Les donnees de reference que les migrations posent elles-memes (SOC-002, SOC-016...) :
    // une base neuve a jour (referenceFr) les porte, une base V3 « vide » n'a rien de plus.
    var lignesLivrees = await BaseGreffe.LignesAsync(referenceFr.Connexion);
    var verifA = await BaseGreffe.VerifierAsync(Chaine(v3a), livre, lignesLivrees, derniere);
    Exiger(verifA.Prete && verifA.Vide && verifA.MigrationsJouees == Migrateur.Lire(migrations).Count,
        $"SOC-008 T05 : avant repetition, la base V3 existe, est vide et porte la version livree ({verifA.MigrationsJouees} migrations, {verifA.DerniereMigration})");
    var verifB = await BaseGreffe.VerifierAsync(Chaine(v3b), livre, lignesLivrees, derniere);
    Exiger(!verifB.Prete && verifB.Problemes.Any(p => p.Contains("schema different") && p.Contains("French_CI_AS")),
        "SOC-008 CA02 (jumeau) : comparee a la version livree d'un autre classement, la base est signalee, le classement nomme");
    await Executer(v3a, "ALTER TABLE soc.Reglage ADD Derive int NULL;");
    var verifDerive = await BaseGreffe.VerifierAsync(Chaine(v3a), livre, lignesLivrees, derniere);
    Exiger(!verifDerive.Prete && verifDerive.Problemes.Any(p => p.Contains("schema different") && p.Contains("Derive")),
        "SOC-008 CA02 (jumeau) : une base dont le schema a derive est signalee, l'ecart nomme");
    await Executer(v3a, "ALTER TABLE soc.Reglage DROP COLUMN Derive;");
    Exiger((await BaseGreffe.VerifierAsync(Chaine(v3a), livre, lignesLivrees, derniere)).Prete,
        "SOC-008 CA02 (jumeau) : la derive retiree, la base redevient conforme");
    await Executer(v3a, "INSERT INTO soc.Role (Nom, Libelle) VALUES ('BancDonnee', 'Donnee de banc');");
    var verifDonnee = await BaseGreffe.VerifierAsync(Chaine(v3a), livre, lignesLivrees, derniere);
    Exiger(!verifDonnee.Vide && verifDonnee.Problemes.Any(p => p.Contains("pas vide") && p.Contains("soc.Role")),
        "SOC-008 T05 (jumeau) : une ligne de plus que la version livree rend la base non vide, la table nommee");
    await Executer(v3a, "DELETE FROM soc.Role WHERE Nom = 'BancDonnee';");

    // CA03
    var agentV3 = await Lever(async () =>
    {
        await using var cn = new SqlConnection(Chaine(v3a, agent, mdpEssai));
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("INSERT INTO soc.ModeleCourriel (Code) VALUES ('AGENT');", cn);
        await cmd.ExecuteNonQueryAsync();
    });
    var agentV2 = await Lever(async () =>
    {
        await using var cn = new SqlConnection(Chaine(v2a.Nom, agent, mdpEssai));
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("INSERT INTO dbo.DOSSIER (ID) VALUES (1);", cn);
        await cmd.ExecuteNonQueryAsync();
    });
    Exiger(agentV3 is SqlException && agentV2 is null,
        $"SOC-008 CA03 : un compte d'agent qui ecrit dans sa base V2 est refuse dans la base V3 ({(agentV3 as SqlException)?.Number})");
    foreach (var (compte, role) in new[] { (serveur, "partie serveur"), (recopie, "recopie") })
    {
        var e = await Lever(async () =>
        {
            await using var cn = new SqlConnection(Chaine(v3a, compte, mdpEssai));
            await cn.OpenAsync();
            await using var cmd = new SqlCommand("INSERT INTO soc.ModeleCourriel (Code) VALUES ('ESSAI'); DELETE FROM soc.ModeleCourriel WHERE Code = 'ESSAI';", cn);
            await cmd.ExecuteNonQueryAsync();
        });
        Exiger(e is null, $"SOC-008 CA03 (jumeau) : la {role} ecrit dans la base V3{(e is null ? "" : " -- " + e.Message)}");
    }
    var exploit = await Lever(async () =>
    {
        await using var cn = new SqlConnection(Chaine(v3a, exploitation, mdpEssai));
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("CREATE TABLE dbo.EssaiExploitation (ID int); DROP TABLE dbo.EssaiExploitation;", cn);
        await cmd.ExecuteNonQueryAsync();
    });
    Exiger(exploit is null, $"SOC-008 CA03 (jumeau) : l'exploitation d'Amitel administre la base V3{(exploit is null ? "" : " -- " + exploit.Message)}");

    // CA04 : le banc joue le plan de sauvegarde (sauvegardes completes), puis le controle.
    var dossierDonnees = (string)(await Scalaire("master", "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultDataPath'));"))!;
    async Task Sauvegarder(string nomBase)
    {
        var f = $"{dossierDonnees}{nomBase}_{sauvegardes.Count}.bak";
        sauvegardes.Add(f);
        await Executer("master", $"BACKUP DATABASE [{nomBase}] TO DISK = N'{f}' WITH INIT;");
        await Task.Delay(1100); // dates de fin distinctes (backup_finish_date a la seconde pres)
    }
    await Sauvegarder(v2a.Nom);
    await Sauvegarder(v3a);
    var controle = await BaseGreffe.ControlerSauvegardesAsync(Chaine("master"), v2a.Nom, v3a);
    Exiger(controle.Conforme && controle.V2.ContainsKey("D") && controle.V3.ContainsKey("D"),
        "SOC-008 CA04 : la base V3 est sauvegardee au moins aussi recemment que sa base V2");
    await Sauvegarder(v2a.Nom);
    var enRetard = await BaseGreffe.ControlerSauvegardesAsync(Chaine("master"), v2a.Nom, v3a);
    Exiger(!enRetard.Conforme && enRetard.Retards.Single().Contains("complete"),
        "SOC-008 CA04 (jumeau) : une V2 sauvegardee apres sa V3 fait signaler la V3 en retard");
    var essai = $"{v3a}_essai";
    var fichierEssai = $"{dossierDonnees}{essai}.bak";
    sauvegardes.Add(fichierEssai);
    var restauration = await BaseGreffe.RestaurerEssaiAsync(Chaine("master"), v3a, Chaine("master"), essai, fichierEssai);
    Exiger(restauration.SchemaIdentique && await Scalaire("master", $"SELECT DB_ID(N'{essai}');") is null,
        $"SOC-008 CA04 : une restauration d'essai reussit, au schema identique, puis la base d'essai est supprimee ({restauration.Duree.TotalSeconds:0.0} s)");

    // CA05
    await Executer("master", $"ALTER DATABASE [{compat.Nom}] SET COMPATIBILITY_LEVEL = 140;");
    var en2017 = await Lever(() => Migrateur.AppliquerAsync(compat.Connexion, migrations));
    Exiger(en2017 is null, $"SOC-008 CA05 : le schema V3 se pose sur une base au niveau SQL Server 2017 (140){(en2017 is null ? "" : " -- " + en2017.Message)}");
    var fichiersSql = Directory.GetFiles(migrations, "*.sql");
    var trouves = fichiersSql.SelectMany(f => BaseGreffe.AbsentDeSqlServer2017(File.ReadAllText(f)).Select(a => $"{Path.GetFileName(f)} : {a}")).ToList();
    Exiger(trouves.Count == 0,
        $"SOC-008 CA05 : aucune des {fichiersSql.Length} migrations n'emploie une fonction absente de SQL Server 2017{(trouves.Count > 0 ? " -- " + string.Join(" ; ", trouves) : "")}");
    Exiger(BaseGreffe.AbsentDeSqlServer2017("SELECT GREATEST(a, b) FROM t WHERE x IS DISTINCT FROM y;").Count == 2
           && BaseGreffe.AbsentDeSqlServer2017("-- GREATEST(a, b) en commentaire\nSELECT STRING_AGG(x, ',') FROM t;").Count == 0,
        "SOC-008 CA05 (jumeau) : GREATEST et IS DISTINCT FROM (2022) sont trouves ; un commentaire et STRING_AGG (2017) passent");
    Exiger(creeeA.VersionInstance >= BaseGreffe.VersionMinimale,
        $"SOC-008 CA05 : la version de l'instance est lue a la creation ({creeeA.VersionInstance}, minimum {BaseGreffe.VersionMinimale})");

    // CA06
    var alerte = await BaseGreffe.PlaceDisqueAsync(Chaine("master"), [v2a.Nom, v3a], long.MaxValue);
    var calme = await BaseGreffe.PlaceDisqueAsync(Chaine("master"), [v2a.Nom, v3a], 0);
    Exiger(alerte.Count >= 1 && alerte.All(v => v.Alerte) && alerte.SelectMany(v => v.Bases).Distinct().Count() == 2,
        $"SOC-008 CA06 : sous le seuil, l'alerte nomme le volume des bases V2 et V3 ({string.Join(", ", alerte.Select(v => $"{v.Volume} {v.LibreOctets / 1_048_576} Mo libres"))})");
    Exiger(calme.Count >= 1 && calme.All(v => !v.Alerte),
        "SOC-008 CA06 (jumeau) : au-dessus du seuil, pas d'alerte");

    // =====================================================================================
    // SOC-003 — les reglages du greffe, repris de RIG_DEV a l'identique
    // =====================================================================================
    Console.WriteLine($"--- SOC-003 : reglages repris de {sourceV2.InitialCatalog} ({sourceV2.DataSource}), en lecture seule");
    Exiger(await Lever(() => { LectureV2.ExigerLecture("SELECT PARAM_CODE FROM dbo.PARAMETRAGE"); return Task.CompletedTask; }) is null
           && await Lever(() => { LectureV2.ExigerLecture("UPDATE dbo.PARAMETRAGE SET PARAM_VALEUR = '1'"); return Task.CompletedTask; }) is not null
           && await Lever(() => { LectureV2.ExigerLecture("SELECT 1; DELETE FROM dbo.PARAMETRAGE"); return Task.CompletedTask; }) is not null
           && await Lever(() => { LectureV2.ExigerLecture("SELECT * INTO dbo.X FROM dbo.PARAMETRAGE"); return Task.CompletedTask; }) is not null,
        "SOC-003 T04 : la porte vers la V2 laisse passer un SELECT et refuse UPDATE, DELETE et SELECT INTO");

    await using var cnV2 = new SqlConnection(sourceV2.ConnectionString);
    await cnV2.OpenAsync();
    await using var cnA = new SqlConnection(Chaine(v3a));
    await cnA.OpenAsync();
    var bilan = await RepriseReglages.ReprendreAsync(cnV2, cnA);
    var nbV2 = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2, "SELECT COUNT(*) FROM dbo.PARAMETRAGE", default));
    var nbParamV2 = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2,
        "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.GREFFE_PARAMETRE')", default));
    var nbModeleV2 = Convert.ToInt32(await LectureV2.ValeurAsync(cnV2, "SELECT COUNT(*) FROM dbo.MAIL_TEMPLATE", default));
    Exiger(bilan.Reglages == nbV2 && bilan.Parametres == nbParamV2 && bilan.Modeles == nbModeleV2,
        $"SOC-003 T04 : tout est repris -- {bilan.Reglages} reglages sur {nbV2}, {bilan.Parametres} parametres de facturation sur {nbParamV2}, {bilan.Modeles} modele(s) de courriel sur {nbModeleV2}");

    // CA01 : la comparaison du serveur...
    var ecartsReglages = await RepriseReglages.ComparerAsync(cnV2, cnA);
    Exiger(ecartsReglages.Count == 0,
        $"SOC-003 CA01 : la comparaison automatique V2 / V3 trouve 0 ecart ({ecartsReglages.Count}{(ecartsReglages.Count > 0 ? ", ex. " + ecartsReglages[0] : "")})");
    // ... et celle du banc, ecrite a part (oracle) : chaque colonne de chaque reglage, au caractere pres.
    var v2Lignes = new Dictionary<string, object?[]>(StringComparer.Ordinal);
    await using (var r = await LectureV2.LireAsync(cnV2,
        "SELECT PARAM_CODE, PARAM_LIBELLE, PARAM_VALEUR, PARAM_UTILISATION, PARAM_AUTEUR_CREATION, PARAM_DATE_CREATION, PARAM_AUTEUR_MODIF, PARAM_DATE_MODIF, PARAM_ID_PARAM FROM dbo.PARAMETRAGE", default))
        while (await r.ReadAsync()) { var v = new object?[9]; r.GetValues(v!); v2Lignes[r.GetString(0)] = v; }
    var differences = 0;
    var comparees = 0;
    await using (var cmd = new SqlCommand("SELECT Code, Libelle, Valeur, Utilisation, AuteurCreation, DateCreation, AuteurModif, DateModif, IdV2 FROM soc.Reglage;", cnA))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            var v3 = new object?[9];
            r.GetValues(v3!);
            if (!v2Lignes.TryGetValue(r.GetString(0), out var v2)) { differences++; continue; }
            // object.Equals : texte au caractere pres (ordinal), date et entier exacts, NULL = NULL.
            for (var i = 0; i < 9; i++, comparees++)
                if (!Equals(v2[i], v3[i])) differences++;
        }
    Exiger(differences == 0 && comparees == nbV2 * 9,
        $"SOC-003 CA01 : relu par le banc, chaque valeur V3 est celle de {sourceV2.InitialCatalog} ({comparees} valeurs comparees, {differences} difference(s))");
    var unCode = v2Lignes.Keys.Order(StringComparer.Ordinal).First();
    await Executer(v3a, $"UPDATE soc.Reglage SET Valeur = ISNULL(Valeur, '') + ' ', AuteurModif = 'banc', DateModif = GETDATE() WHERE Code = '{unCode.Replace("'", "''")}';");
    var avecEcarts = await RepriseReglages.ComparerAsync(cnV2, cnA);
    Exiger(avecEcarts.Count > 0 && avecEcarts.All(e => e.Cle == unCode) && avecEcarts.Any(e => e.Colonne == "PARAM_VALEUR"),
        $"SOC-003 CA01 (jumeau) : un espace ajoute a la valeur de {unCode} est trouve ({avecEcarts.Count} ecart(s) sur ce seul reglage)");
    var bilan2 = await RepriseReglages.ReprendreAsync(cnV2, cnA);
    Exiger(bilan2 == bilan && (await RepriseReglages.ComparerAsync(cnV2, cnA)).Count == 0,
        "SOC-003 T05 : une repetition de la reprise repart de la V2 et retrouve 0 ecart");

    // CA02 : les oui / non ecrits autrement que 0 / 1 gardent leur texte...
    var horsNorme = new List<(string Code, string Texte)>();
    await using (var r = await LectureV2.LireAsync(cnV2,
        "SELECT PARAM_CODE, PARAM_VALEUR FROM dbo.PARAMETRAGE WHERE UPPER(LTRIM(RTRIM(PARAM_VALEUR))) IN ('OUI', 'NON', 'O', 'N', 'TRUE', 'FALSE', 'VRAI', 'FAUX')", default))
        while (await r.ReadAsync()) horsNorme.Add((r.GetString(0), r.GetString(1)));
    var gardes = 0;
    foreach (var (code, texte) in horsNorme)
        if (await new LectureReglage(code, "?", "banc").LireTexteAsync(cnA) == texte) gardes++;
    Exiger(horsNorme.Count > 0 && gardes == horsNorme.Count,
        $"SOC-003 CA02 : les {horsNorme.Count} oui / non ecrits OUI, NON, O, N, True, False, Vrai, Faux gardent leur texte d'origine ({gardes} identiques)");
    // ... et chaque regle de lecture donne la reponse de la V2. Table ecrite a la main depuis le code V2 :
    // EgalUn (Parametrage.cs:164), EgalOUI (Message_Regent_V2016-02_Xml.cs:649), DBBool (DBObject.cs:444-475).
    var tableV2 = new (string Texte, bool EgalUn, bool EgalOUI, bool DBBool)[]
    {
        ("1", true, false, true), ("0", false, false, false),
        ("OUI", false, true, true), ("Oui", false, false, true), ("oui", false, false, false), ("O", false, false, true),
        ("NON", false, false, false), ("N", false, false, false),
        ("TRUE", false, false, true), ("True", false, false, true), ("true", false, false, true),
        ("FALSE", false, false, false), ("False", false, false, false), ("false", false, false, false),
        ("VRAI", false, false, false), ("Vrai", false, false, false), ("FAUX", false, false, false), ("Faux", false, false, false),
        ("", false, false, false), ("Y", false, false, true),
    };
    var conformes = tableV2.Count(t => LectureReglage.OuiNon(t.Texte, RegleOuiNon.EgalUn) == t.EgalUn
                                    && LectureReglage.OuiNon(t.Texte, RegleOuiNon.EgalOUI) == t.EgalOUI
                                    && LectureReglage.OuiNon(t.Texte, RegleOuiNon.ConversionDBBool) == t.DBBool);
    Exiger(conformes == tableV2.Length,
        $"SOC-003 CA02 : les trois facons de lire un oui / non de la V2 sont reproduites ({conformes}/{tableV2.Length} ecritures, « Vrai » = non en case a cocher, comme la V2)");
    // Sur les reglages reels de la base, par la fonction V2 qui les lit :
    var reelles = new (LectureReglage Lecture, RegleOuiNon Regle)[]
    {
        (new("NUMERISE_LIASSE1", "", @"RigMetier\Divers\Parametrage.cs:164"), RegleOuiNon.EgalUn),
        (new("EDI_CFE_EMI_SCHEMA_INVALIDE_BLOQUANT", "", @"Message_Regent_V2016-02_Xml.cs:649"), RegleOuiNon.EgalOUI),
        (new("IM_AFFICHER_LOT_SUIVANT", "NON", @"RCS\Exe\InscriptionsMasse\ListeLots.vb:634"), RegleOuiNon.EgalOUI),
        (new("IDCA_FORCER_DIFFERE", "", @"PROC_INJONCTION_DCA\Etapes\Parametrages\OPE_PARAMETRAGE.cs:202"), RegleOuiNon.ConversionDBBool),
    };
    foreach (var (lecture, regle) in reelles)
    {
        var texteV2 = (string?)await LectureV2.ValeurAsync(cnV2,
            $"SELECT PARAM_VALEUR FROM dbo.PARAMETRAGE WHERE PARAM_CODE = '{lecture.Code}'", default) ?? lecture.SiAbsent;
        var ligne = tableV2.FirstOrDefault(t => t.Texte == texteV2);
        var attenduV2 = regle switch { RegleOuiNon.EgalUn => ligne.EgalUn, RegleOuiNon.EgalOUI => ligne.EgalOUI, _ => ligne.DBBool };
        var lu = await lecture.LireOuiNonAsync(cnA, regle);
        Exiger(ligne.Texte is not null && lu == attenduV2,
            $"SOC-003 CA02 : {lecture.Code} = « {texteV2} » se lit {(lu ? "oui" : "non")} en V3, comme en V2 ({regle})");
    }

    // CA03 : reglage absent = la valeur par defaut de la fonction ; present mais vide = "" (DBString V2).
    await using var cnB = new SqlConnection(Chaine(v3b));   // la base V3 de T002 : aucun reglage repris
    await cnB.OpenAsync();
    var absentLu = await new LectureReglage("IM_AFFICHER_LOT_SUIVANT", "NON", "ListeLots.vb:634").LireTexteAsync(cnB);
    Exiger(absentLu == "NON" && !LectureReglage.OuiNon(absentLu, RegleOuiNon.EgalOUI),
        "SOC-003 CA03 : un reglage absent de la base du greffe prend la valeur par defaut de la fonction V2 (IM_AFFICHER_LOT_SUIVANT -> NON)");
    var codeNul = (string?)await LectureV2.ValeurAsync(cnV2, "SELECT MIN(PARAM_CODE) FROM dbo.PARAMETRAGE WHERE PARAM_VALEUR IS NULL", default);
    var nulLu = codeNul is null ? null : await new LectureReglage(codeNul, "DEFAUT", "banc").LireTexteAsync(cnA);
    Exiger(codeNul is not null && nulLu == "",
        $"SOC-003 CA03 (jumeau) : un reglage PRESENT mais vide ({codeNul}) donne \"\" et non la valeur par defaut, comme la V2");

    // CA04 et CA05 : la liste remise au referent Socle.
    await Executer(v3a, "INSERT INTO soc.Reglage (Code, Libelle, Valeur) VALUES ('BANC_CHEMIN_ABSENT', 'banc', '\\\\serveur-absent.invalid\\partage\\modele.txt');");
    var uncAvant = new Dictionary<string, string>();
    await using (var cmd = new SqlCommand(@"SELECT Code, Valeur FROM soc.Reglage WHERE Valeur LIKE '\\%';", cnA))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) uncAvant[r.GetString(0)] = r.GetString(1);
    var rapport = await Lire<RapportReprise>(RoutesGreffes.Pour(RoutesGreffes.Reprise, "T001"));
    var declares = LectureReglage.Toutes().Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var nbApres = Convert.ToInt32(await Scalaire(v3a, "SELECT COUNT(*) FROM soc.Reglage;"));
    var nonLusAttendus = v2Lignes.Keys.Append("BANC_CHEMIN_ABSENT").Where(c => !declares.Contains(c)).ToHashSet();
    Exiger(declares.Contains("SEND_EMAIL_TEMPLATE_PATH") && !rapport.NonLus.Contains("SEND_EMAIL_TEMPLATE_PATH")
           && rapport.NonLus.Contains("NUMERISE_LIASSE1") && nonLusAttendus.SetEquals(rapport.NonLus)
           && rapport.Reglages == nbV2 + 1 && nbApres == nbV2 + 1,
        $"SOC-003 CA04 : les reglages qu'aucune fonction V3 ne lit sont gardes et listes pour le referent ({rapport.NonLus.Count} sur {rapport.Reglages} ; lus : {string.Join(", ", declares)})");
    var injoignablesBanc = new HashSet<string>();
    foreach (var (code, chemin) in uncAvant)
        if (!(File.Exists(chemin) || Directory.Exists(chemin))) injoignablesBanc.Add(code);
    var listes = rapport.CheminsInjoignables.Select(c => c.Reglage).ToHashSet();
    Exiger(listes.Contains("BANC_CHEMIN_ABSENT") && listes.SetEquals(injoignablesBanc),
        $"SOC-003 CA05 : les chemins reseau injoignables d'ici sont listes ({string.Join(", ", listes.Order())})");
    Exiger(uncAvant.Keys.Any(c => !listes.Contains(c)),
        $"SOC-003 CA05 (jumeau) : un chemin joignable n'est pas liste ({uncAvant.Count - listes.Count} sur {uncAvant.Count})");
    var uncApres = new Dictionary<string, string>();
    await using (var cmd = new SqlCommand(@"SELECT Code, Valeur FROM soc.Reglage WHERE Valeur LIKE '\\%';", cnA))
    await using (var r = await cmd.ExecuteReaderAsync())
        while (await r.ReadAsync()) uncApres[r.GetString(0)] = r.GetString(1);
    Exiger(uncApres.Count == uncAvant.Count && uncAvant.All(p => uncApres[p.Key] == p.Value),
        "SOC-003 CA05 : la liste ne modifie aucun chemin");
    await Executer(v3a, "DELETE FROM soc.Reglage WHERE Code = 'BANC_CHEMIN_ABSENT';");

    // CA06 : l'habillage des courriels, base d'abord, fichier ensuite.
    var modeleV2 = (string?)await LectureV2.ValeurAsync(cnV2, "SELECT MAILT_CONTENU FROM dbo.MAIL_TEMPLATE WHERE MAILT_CODE = 'SHELL'", default);
    var modeleV3 = await ModeleCourriel.LireAvecRepliFichierAsync(cnA);
    Exiger(modeleV2 is { Length: > 0 } && modeleV3 == modeleV2,
        $"SOC-003 CA06 : le modele de courriel du greffe en base est repris tel quel et c'est lui qui habille ({modeleV3.Length} caracteres)");
    var fichierModele = Path.Combine(Path.GetTempPath(), $"rigv3-banc-modele-{jeton}.txt");
    await File.WriteAllTextAsync(fichierModele, "<MailTemplate><HtmlBody><![CDATA[{{MESSAGE_PERSONNALISE}} banc]]></HtmlBody></MailTemplate>");
    try
    {
        await Executer(v3a, "DELETE FROM soc.ModeleCourriel WHERE Code = 'SHELL';");
        await Executer(v3a, $"UPDATE soc.Reglage SET Valeur = N'{fichierModele}', AuteurModif = 'banc', DateModif = GETDATE() WHERE Code = 'SEND_EMAIL_TEMPLATE_PATH';");
        Exiger(await ModeleCourriel.LireAvecRepliFichierAsync(cnA) == await File.ReadAllTextAsync(fichierModele),
            "SOC-003 CA06 : sans modele en base, le fichier designe par SEND_EMAIL_TEMPLATE_PATH habille le courriel (repli de la V2)");
        await Executer(v3a, "INSERT INTO soc.ModeleCourriel (Code, Contenu) VALUES ('SHELL', N'<base/>');");
        Exiger(await ModeleCourriel.LireAvecRepliFichierAsync(cnA) == "<base/>",
            "SOC-003 CA06 (jumeau) : le modele en base passe avant le fichier");
        await Executer(v3a, "DELETE FROM soc.ModeleCourriel WHERE Code = 'SHELL';");
        await Executer(v3a, $"UPDATE soc.Reglage SET Valeur = N'{fichierModele}.absent', AuteurModif = 'banc', DateModif = GETDATE() WHERE Code = 'SEND_EMAIL_TEMPLATE_PATH';");
        Exiger(await ModeleCourriel.LireAvecRepliFichierAsync(cnA) == "",
            "SOC-003 CA06 (jumeau) : ni modele en base ni fichier lisible : pas d'habillage, le rendu historique (comme la V2)");
    }
    finally { File.Delete(fichierModele); }

    // Lot 9 : les routes des reglages jugent l'agent dans la base DU GREFFE (comme la V2 sous l'utilisateur du
    // greffe) : les comptes du banc y sont inscrits, comme dans la base commune -- et dans la base du controle
    // de compatibilite, que SOC-007 CA04 fait designer a T001 plus bas.
    await InscrireComptes(v3a, "T001");
    await InscrireComptes(v3b, "T002");
    await InscrireComptes(compat.Nom, "T001");
    async Task<(int A, int B, int Commune)> Journaux(string famille) => (
        Convert.ToInt32(await Scalaire(v3a, $"SELECT COUNT(*) FROM soc.Journal WHERE Famille = '{famille}';")),
        Convert.ToInt32(await Scalaire(v3b, $"SELECT COUNT(*) FROM soc.Journal WHERE Famille = '{famille}';")),
        Convert.ToInt32(await Scalaire(commune.Nom, $"SELECT COUNT(*) FROM soc.Journal WHERE Famille = '{famille}';")));

    // CA07 : modifier un reglage garde qui et quand, COMME LA V2 : l'auteur est le NOM de
    // l'agent (UTLST_NOM, Parametrage.cs:104-108), pas son compte Windows.
    var adresse = RoutesGreffes.Pour(RoutesGreffes.Reglage, "T001", "NUMERISE_LIASSE1");
    // Lot 14 du plan « RIG v3 100 % fonctionnel » (27/11/2026, refus du chef) : une valeur de PLUS de 1 000 caracteres est REFUSEE,
    // pas coupee. La V2 : PARAMETRAGE.Verifier (PARAMETRAGE.cs:242, PARAM_VALEUR varchar(1000)) leve TROPGRAND, « PARAMETRAGE.PARAM_VALEUR
    // est limité à 1000 caractères » (DBObject.cs:993-994). Avant : le parametre varchar(1000) coupait la valeur sans rien dire (200).
    // Jumeau accepte : 1 000 caracteres tout juste, relus ENTIERS. L'entree qui ferait tomber une borne fausse : 1 001 (« > 1000 »
    // oublie : coupee et acceptee) ; 1 000 (« >= 1000 » : refusee a tort). Joue AVANT la mesure CA07, qui compte le journal, et la
    // valeur d'origine remise.
    var valeurOrigine = (await Lire<ReglageGreffe>(adresse)).Valeur;
    var journalAvantTropLong = await Journaux("reglage");
    var tropLong = await Envoyer(HttpMethod.Put, adresse, new SaisieReglage(new string('7', 1001), "Reglages du greffe"));
    var motifTropLong = (await tropLong.Content.ReadFromJsonAsync<RefusGreffe>())?.Motif;
    var apresTropLong = await Lire<ReglageGreffe>(adresse);
    Exiger(tropLong.StatusCode == HttpStatusCode.BadRequest && motifTropLong == "PARAMETRAGE.PARAM_VALEUR est limité à 1000 caractères"
           && apresTropLong.Valeur == valeurOrigine && await Journaux("reglage") == journalAvantTropLong,
        $"Lot 14 : une valeur de 1 001 caracteres est refusee (400, message de la V2), ni coupee ni ecrite, journal inchange ({(int)tropLong.StatusCode} « {motifTropLong} », valeur {apresTropLong.Valeur?.Length} car.)");
    var pile1000 = await Envoyer(HttpMethod.Put, adresse, new SaisieReglage(new string('7', 1000), "Reglages du greffe"));
    var relu1000 = (await Lire<ReglageGreffe>(adresse)).Valeur;
    Exiger(pile1000.StatusCode == HttpStatusCode.OK && relu1000 == new string('7', 1000),
        $"Lot 14 (jumeau) : une valeur de 1 000 caracteres tout juste est ecrite et relue entiere ({(int)pile1000.StatusCode}, {relu1000?.Length} car.)");
    await Envoyer(HttpMethod.Put, adresse, new SaisieReglage(valeurOrigine, "Reglages du greffe"));
    var maintenantSql = (DateTime)(await Scalaire(v3a, "SELECT GETDATE();"))!;
    var nomAgent = (string)(await Scalaire(v3a, "SELECT Nom FROM soc.Compte WHERE CompteWindows = 'banc.socle';"))!;
    var valeurAvant = (string?)await Scalaire(v3a, "SELECT Valeur FROM soc.Reglage WHERE Code = 'NUMERISE_LIASSE1';");
    var reglagesAvant = await Journaux("reglage");
    var modif = await Envoyer(HttpMethod.Put, adresse, new SaisieReglage("0", "Reglages du greffe"));
    var apres = await Lire<ReglageGreffe>(adresse);
    Exiger(modif.StatusCode == HttpStatusCode.OK && apres.Valeur == "0" && apres.AuteurModif == nomAgent
           && apres.DateModif is { } d && Math.Abs((d - maintenantSql).TotalSeconds) < 120,
        $"SOC-003 CA07 : un reglage modifie montre qui l'a modifie et quand, le nom de l'agent comme la V2 ({apres.AuteurModif}, attendu {nomAgent}, {apres.DateModif:dd/MM/yyyy HH:mm:ss})");
    // Lot 9, SOC-016 CA01 : le geste est au journal de la base du greffe A, et de nulle autre.
    var reglagesApres = await Journaux("reglage");
    var ligneReglage = (string?)await Scalaire(v3a, """
        SELECT TOP 1 CONCAT(Agent, '|', Ecran, '|', Objet, '|', Verbe, '|', JSON_VALUE(Avant, '$.valeur'), '|', JSON_VALUE(Apres, '$.valeur'), '|',
                            CASE WHEN Instant > DATEADD(minute, -5, SYSUTCDATETIME()) THEN 'recent' ELSE 'ancien' END)
        FROM soc.Journal WHERE Famille = 'reglage' ORDER BY Id DESC;
        """);
    Exiger(reglagesApres == (reglagesAvant.A + 1, reglagesAvant.B, reglagesAvant.Commune)
           && ligneReglage == $"Socle BANC|Reglages du greffe|le reglage NUMERISE_LIASSE1|a modifie|{valeurAvant}|0|recent",
        $"Raccord (2), SOC-016 CA01 : la modification d'un reglage du greffe A s'ecrit au journal de la base de A seulement, avec l'agent, l'ecran, l'heure, l'avant et l'apres (journaux A/B/commune {reglagesAvant} -> {reglagesApres} ; « {ligneReglage} »)");
    // Le repli de la V2 : agent introuvable -> AmiEnvironment.UserName, le compte SANS domaine.
    // La politique AgentAdmis n'en laisse passer aucun par la route : la regle se juge seule.
    var sessionInconnue = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
        [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, @"AMITEL\banc.inconnu")], "Banc"));
    var agentDupont = new IdentiteAgent(StatutsIdentite.Admis, "jdupont",
        new AgentReconnu("jdupont", "DUPONT", "Jean", "JDU", "M.", "Greffe", false, false, false), null, null);
    Exiger(AuteurReglage.De(null, sessionInconnue) == "banc.inconnu"
           && AuteurReglage.De(new IdentiteAgent(StatutsIdentite.Inconnu, "banc.inconnu", null, "inconnu", null), sessionInconnue) == "banc.inconnu",
        $"SOC-003 CA07 (jumeau) : agent non reconnu, l'auteur est son compte Windows sans domaine, comme AmiEnvironment.UserName ({AuteurReglage.De(null, sessionInconnue)})");
    Exiger(AuteurReglage.De(agentDupont, sessionInconnue) == "DUPONT"
           && AuteurReglage.De(null, new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())) is null,
        "SOC-003 CA07 (jumeau) : agent reconnu, son nom prime sur le compte de la session ; sans session, aucun auteur (refus)");
    var sansAgent = await Envoyer(HttpMethod.Put, adresse, new SaisieReglage("1"), compte: null);
    Exiger(sansAgent.StatusCode == HttpStatusCode.Unauthorized && (await Lire<ReglageGreffe>(adresse)).Valeur == "0",
        "SOC-003 CA07 (jumeau) : sans identite, la modification est refusee (401) et la valeur ne bouge pas");
    var inexistant = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Reglage, "T001", "BANC_INEXISTANT"), new SaisieReglage("1"));
    Exiger(inexistant.StatusCode == HttpStatusCode.NotFound,
        "SOC-003 CA07 (jumeau) : un reglage absent ne se cree pas par une modification (404, comme UpdateValeurParametrage)");
    var sqlSansAuteur = await Lever(() => Executer(v3a, "UPDATE soc.Reglage SET Valeur = '1' WHERE Code = 'NUMERISE_LIASSE1';"));
    Exiger(sqlSansAuteur is SqlException { Number: 51731 },
        "SOC-003 CA07 : meme une requete SQL ecrite a la main ne change pas une valeur sans auteur ni date");

    // =====================================================================================
    // SOC-007 (suite) — la base d'un greffe vient du catalogue ; passage ; historique
    // =====================================================================================
    Console.WriteLine("--- SOC-007 : aiguillage, passage, historique");
    // CA04
    var etat = await Envoyer(HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Base, "T001"));
    var etatA = await etat.Content.ReadFromJsonAsync<EtatBaseGreffe>();
    var nbReglagesRoute = (await Lire<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"))).Length;
    Exiger(etat.StatusCode == HttpStatusCode.OK && etatA!.Joignable && etatA.Base == v3a && nbReglagesRoute == nbV2,
        $"SOC-007 CA04 : le serveur trouve la base du greffe par le catalogue ({etatA?.Base}, {nbReglagesRoute} reglages lus)");
    var partage = await Lever(() => Executer(commune.Nom,
        $"UPDATE soc.Greffe SET BaseV3 = N'{v3b}', ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = 'T001';"));
    Exiger(partage is SqlException { Number: 2627 },
        "SOC-007 CA04 : deux greffes ne peuvent pas designer la meme base (aucune ambiguite sur la base d'un greffe)");
    // Une base V3 hors catalogue, au schema livre et sans reglage : celle du controle de compatibilite.
    await Executer(commune.Nom, $"UPDATE soc.Greffe SET BaseV3 = N'{compat.Nom}', ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = 'T001';");
    var nbAilleurs = (await Lire<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"))).Length;
    await Executer(commune.Nom, $"UPDATE soc.Greffe SET BaseV3 = N'{v3a}', ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = 'T001';");
    Exiger(nbAilleurs == 0 && (await Lire<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"))).Length == nbV2,
        "SOC-007 CA04 : la base lue est celle que le catalogue nomme, et nulle autre (fiche changee -> base changee)");
    var injoignable = $"rigv3_banc_inexistante_{jeton}";
    await Executer(commune.Nom,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
        $"VALUES ('T003', 'Greffe sans base', '{instance}', '{injoignable}', '{injoignable}', 0, 0, 'Romance Standard Time', 'banc', SYSDATETIME());");
    // La route des reglages porte {greffe} (lot 9) : c'est la politique qui ouvre la base du greffe, et la nomme.
    var horsLigne = await Envoyer(HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Reglages, "T003"));
    var corpsHorsLigne = await horsLigne.Content.ReadFromJsonAsync<RefusGreffe>();
    var etatHorsLigne = await Envoyer(HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Base, "T003"));
    var corpsEtatHorsLigne = await etatHorsLigne.Content.ReadFromJsonAsync<EtatBaseGreffe>();
    Exiger(horsLigne.StatusCode == HttpStatusCode.ServiceUnavailable && corpsHorsLigne is { } refusHorsLigne && refusHorsLigne.Motif.Contains(injoignable)
           && etatHorsLigne.StatusCode == HttpStatusCode.ServiceUnavailable && corpsEtatHorsLigne is { Joignable: false } && corpsEtatHorsLigne.Base == injoignable,
        $"SOC-007 CA04 : une base qui ne repond pas est signalee (503 sur ses reglages et sur son etat, {corpsEtatHorsLigne?.Base}), sans repli sur une autre ({corpsHorsLigne?.Motif})");
    Exiger((await Envoyer(HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Base, "0000"))).StatusCode == HttpStatusCode.NotFound,
        "SOC-007 CA04 (jumeau) : un greffe absent du catalogue est refuse (404), sans base par defaut");

    // CA05
    var passage = RoutesGreffes.Pour(RoutesGreffes.Passage, "T001");
    var futur = await Envoyer(HttpMethod.Put, passage, new SaisiePassage("2099-01-01T08:00"));
    var ficheFutur = await futur.Content.ReadFromJsonAsync<FicheGreffe>();
    Exiger(futur.StatusCode == HttpStatusCode.OK && ficheFutur!.PassageLocal == new DateTime(2099, 1, 1, 8, 0, 0) && !ficheFutur.PassageFait,
        "SOC-007 CA05 : la date et l'heure de passage sont ecrites telles quelles, en heure locale du greffe");
    Exiger((await Envoyer(HttpMethod.Put, passage, new SaisiePassage("2099-01-01T08:00+02:00"))).StatusCode == HttpStatusCode.BadRequest,
        "SOC-007 CA05 (jumeau) : une heure avec decalage est refusee (seule l'heure locale du greffe fait foi)");
    var hier = Fuseaux.MaintenantLocal("Romance Standard Time").AddDays(-1);
    var fait = await Envoyer(HttpMethod.Put, passage,
        new SaisiePassage(hier.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)));
    var ficheFait = await fait.Content.ReadFromJsonAsync<FicheGreffe>();
    Exiger(fait.StatusCode == HttpStatusCode.OK && ficheFait!.PassageFait,
        "SOC-007 CA05 (jumeau) : avant le passage, la date se change encore (passage fait a l'heure saisie)");
    var fige = await Envoyer(HttpMethod.Put, passage, new SaisiePassage("2099-06-01T08:00"));
    var fuseauFige = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T001"), new SaisieFuseau("SA Western Standard Time"));
    var ficheApres = await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, "T001"));
    Exiger(fige.StatusCode == HttpStatusCode.Conflict && fuseauFige.StatusCode == HttpStatusCode.Conflict
           && ficheApres.PassageLocal == ficheFait?.PassageLocal && ficheApres.Fuseau == "Romance Standard Time",
        "SOC-007 CA05 : une fois le passage fait, sa date et son fuseau ne changent plus (409)");
    var sqlFige = await Lever(() => Executer(commune.Nom,
        "UPDATE soc.Greffe SET PassageLocal = '2099-06-01T08:00:00', ModifiePar = 'banc', ModifieLe = SYSDATETIME() WHERE CodeGreffe = 'T001';"));
    Exiger(sqlFige is SqlException { Number: 51703 },
        "SOC-007 CA05 : la base elle-meme refuse de changer une date de passage faite");
    await Executer(commune.Nom,
        "INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, ModifiePar, ModifieLe) " +
        $"VALUES ('T004', 'Greffe sans fuseau', '{instance}', 'RIG_T004', 'RIGV3_T004', 0, 1, 'banc', SYSDATETIME());");
    Exiger((await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Passage, "T004"), new SaisiePassage("2099-01-01T08:00"))).StatusCode == HttpStatusCode.Conflict,
        "SOC-007 CA05 : pas de date de passage sans fuseau (l'heure locale ne fonderait rien)");

    // CA06
    var maintenantCommune = (DateTime)(await Scalaire(commune.Nom, "SELECT SYSDATETIME();"))!;
    var historique = await Lire<ModificationGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Historique, "T001"));
    Exiger(historique.Length >= 3 && historique[0].ModifiePar == Agent && Math.Abs((historique[0].ModifieLe - maintenantCommune).TotalSeconds) < 120
           && historique[^1].Operation == "C",
        $"SOC-007 CA06 : chaque modification du catalogue garde son auteur et sa date ({historique.Length} entrees, derniere par {historique[0].ModifiePar})");
    var historiqueT002 = RoutesGreffes.Pour(RoutesGreffes.Historique, "T002");
    var avantRefus = (await Lire<ModificationGreffe[]>(historiqueT002)).Length;
    var anonyme = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("SA Western Standard Time"), compte: null);
    Exiger(anonyme.StatusCode == HttpStatusCode.Unauthorized
           && (await Lire<ModificationGreffe[]>(historiqueT002)).Length == avantRefus
           && (await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, "T002"))).Fuseau == "Romance Standard Time",
        "SOC-007 CA06 (jumeau) : une modification sans auteur est refusee (401) et rien ne change");
    var accepte = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("SA Western Standard Time"));
    Exiger(accepte.StatusCode == HttpStatusCode.OK
           && (await Lire<ModificationGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Historique, "T002")))[0] is { ModifiePar: Agent, Fuseau: "SA Western Standard Time" },
        "SOC-007 CA06 (jumeau) : la meme modification, avec son auteur, est acceptee et tracee");
    var sqlAnonyme = await Lever(() => Executer(commune.Nom, "UPDATE soc.Greffe SET Nom = 'x' WHERE CodeGreffe = 'T002';"));
    Exiger(sqlAnonyme is SqlException { Number: 51701 },
        "SOC-007 CA06 : meme une requete SQL ecrite a la main ne modifie pas le catalogue sans auteur ni date");

    // =====================================================================================
    // Lot 9 — le droit de modifier le greffe (V2 : OPE_EDITER_GREFFE.cs:30-38) et le journal des gestes
    // =====================================================================================
    Console.WriteLine("--- Lot 9 : droit de modifier, journal des gestes dans la transaction");
    // Les gestes du catalogue faits plus haut (passage de T001 deux fois, fuseau de T002) sont au journal de la
    // base COMMUNE, ou vit le catalogue ; les refus (400, 401, 409) n'y ont rien ecrit.
    var gestesFiche = new List<string>();
    await using (var cn = new SqlConnection(commune.Connexion))
    {
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT CONCAT(Agent, '|', Objet, '|', IdObjet, '|', Verbe, '|', COALESCE(JSON_VALUE(Avant, '$.passageLocal'), JSON_VALUE(Avant, '$.fuseau')), '|',
                          COALESCE(JSON_VALUE(Apres, '$.passageLocal'), JSON_VALUE(Apres, '$.fuseau')))
            FROM soc.Journal WHERE Famille = 'greffe' ORDER BY Id;
            """, cn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) gestesFiche.Add(r.GetString(0));
    }
    var hierJournal = hier.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture) + ":00";
    Exiger(gestesFiche.SequenceEqual([
               "Socle BANC|la fiche du greffe T001|T001|a modifie||2099-01-01T08:00:00",
               $"Socle BANC|la fiche du greffe T001|T001|a modifie|2099-01-01T08:00:00|{hierJournal}",
               "Socle BANC|la fiche du greffe T002|T002|a modifie|Romance Standard Time|SA Western Standard Time"])
           && await Journaux("greffe") == (0, 0, 3),
        $"Raccord (2), SOC-016 CA01 : chaque modification acceptee du catalogue (date et fuseau de passage) s'ecrit au journal de la base commune avec l'agent, l'avant et l'apres ; les refus n'y ecrivent rien ({string.Join(" ; ", gestesFiche)})");

    // Un agent admis SANS le droit : 403 sur les trois modifications, rien ne change, rien n'est journalise.
    var reglageT001Lot9 = RoutesGreffes.Pour(RoutesGreffes.Reglage, "T001", "NUMERISE_LIASSE1");
    async Task<string> Etat403() => string.Join("|",
        (await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, "T002"))) is var f2 ? $"{f2.Fuseau}/{f2.PassageLocal:o}/{f2.ModifiePar}" : "",
        (await Lire<ModificationGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Historique, "T002"))).Length,
        (await Lire<ReglageGreffe>(reglageT001Lot9)) is var r1 ? $"{r1.Valeur}/{r1.AuteurModif}/{r1.DateModif:o}" : "",
        await Journaux("greffe"), await Journaux("reglage"));
    var avant403 = await Etat403();
    var refus403 = new List<(HttpStatusCode Code, string Motif)>();
    foreach (var (adresse403, corps403) in new (string, object)[]
             {
                 (RoutesGreffes.Pour(RoutesGreffes.Passage, "T002"), new SaisiePassage("2099-01-01T08:00")),
                 (RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("Romance Standard Time")),
                 (reglageT001Lot9, new SaisieReglage("1")),
             })
    {
        var reponse = await Envoyer(HttpMethod.Put, adresse403, corps403, compte: SansDroit);
        refus403.Add((reponse.StatusCode, (await reponse.Content.ReadFromJsonAsync<RefusGreffe>())?.Motif ?? ""));
    }
    var apres403 = await Etat403();
    var lectureSansDroit = await Envoyer(HttpMethod.Get, reglageT001Lot9, compte: SansDroit);
    Exiger(refus403.All(r => r.Code == HttpStatusCode.Forbidden && r.Motif.Contains("banc.agent")) && apres403 == avant403,
        $"Raccord (2) : un agent admis sans le droit de modifier le greffe recoit 403 sur la date de passage, le fuseau et un reglage ; ni la fiche, ni son historique, ni le reglage, ni aucun journal ne changent ({string.Join(", ", refus403.Select(r => (int)r.Code))} ; {avant403} -> {apres403})");
    // Le motif du refus dit TOUTE la regle de la V2 (OPE_EDITER_GREFFE.cs:30-38) : l'agent refuse doit savoir qui
    // peut modifier. Les noms sont ecrits ici en clair, pas relus du serveur, pour que le controle ne se juge pas lui-meme.
    string[] reglePublique = ["administrateur d'Amitel", "thevenon", "benier", "jouguet"];
    Exiger(refus403.Count == 3 && refus403.All(r => reglePublique.All(r.Motif.Contains)),
        $"Raccord (2) : le motif du 403 nomme toute la regle de la V2, administrateur d'Amitel ou l'un des trois comptes nommes ({refus403.FirstOrDefault().Motif})");
    Exiger(lectureSansDroit.StatusCode == HttpStatusCode.OK,
        "Raccord (2) (jumeau) : le meme agent lit toujours le reglage (la V2 lui laisse la lecture : MetierReadOnly)");
    // Jumeau : un compte que la V2 nomme (sans etre administrateur) modifie, et son geste est journalise.
    var parNomme = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("Romance Standard Time"), compte: NommeV2);
    var reglageNomme = await Envoyer(HttpMethod.Put, reglageT001Lot9, new SaisieReglage("1"), compte: NommeV2);
    Exiger(parNomme.StatusCode == HttpStatusCode.OK && reglageNomme.StatusCode == HttpStatusCode.OK
           && (await Lire<ModificationGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Historique, "T002")))[0] is { ModifiePar: NommeV2, Fuseau: "Romance Standard Time" }
           && await Journaux("greffe") == (0, 0, 4) && (await Journaux("reglage")).A == reglagesApres.A + 1,
        $"Raccord (2) (jumeau) : un compte nomme par la V2 (OPE_EDITER_GREFFE.cs:31-35) modifie le fuseau et un reglage, et chaque geste est journalise ({(int)parNomme.StatusCode}, {(int)reglageNomme.StatusCode})");

    // LA MEME TRANSACTION : journal en panne -> la modification est annulee avec lui (tout ou rien).
    async Task<(HttpStatusCode? Statut, Exception? Erreur)> SousPanne(string nomBase, Func<Task<HttpResponseMessage>> appel)
    {
        await Executer(nomBase, "EXEC sp_rename 'soc.Journal', 'Journal_enPanne';");
        try
        {
            HttpStatusCode? statut = null;
            var erreur = await Lever(async () => statut = (await appel()).StatusCode);
            return (statut, erreur);
        }
        finally { await Executer(nomBase, "EXEC sp_rename 'soc.Journal_enPanne', 'Journal';"); }
    }
    var avantPanne = await Etat403();
    var panneFiche = await SousPanne(commune.Nom,
        () => Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("SA Western Standard Time")));
    var pannePassage = await SousPanne(commune.Nom,
        () => Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Passage, "T002"), new SaisiePassage("2099-01-01T08:00")));
    var panneReglage = await SousPanne(v3a, () => Envoyer(HttpMethod.Put, reglageT001Lot9, new SaisieReglage("0")));
    var apresPanne = await Etat403();
    Exiger(new[] { panneFiche, pannePassage, panneReglage }.All(p => p.Statut is not HttpStatusCode.OK) && apresPanne == avantPanne,
        $"Raccord (2) : le geste s'ecrit dans la MEME transaction que la modification -- journal en panne, la date de passage, le fuseau et le reglage restent tels quels ({panneFiche.Statut?.ToString() ?? panneFiche.Erreur?.GetType().Name}, {pannePassage.Statut?.ToString() ?? pannePassage.Erreur?.GetType().Name}, {panneReglage.Statut?.ToString() ?? panneReglage.Erreur?.GetType().Name} ; {avantPanne} -> {apresPanne})");
    var apresRetour = await Envoyer(HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("SA Western Standard Time"));
    Exiger(apresRetour.StatusCode == HttpStatusCode.OK && await Journaux("greffe") == (0, 0, 5),
        "Raccord (2) (jumeau) : le journal revenu, la meme modification passe et s'y ecrit");

    // =====================================================================================
    // L'ecran « Réglages du greffe » (Pont/EcranReglages.cs, SOC-003 T03 et CA07) sur le vrai serveur,
    // par le meme client que l'app (ClientServeur, compte pose par RIGV3_COMPTE a sa construction).
    // =====================================================================================
    Console.WriteLine("--- Ecran des reglages");
    ClientServeur ClientEcran(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    const string ReglageEcran = "NUMERISE_LIASSE1";
    var lusParRoute = await Lire<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"));
    var valeurAvantEcran = lusParRoute.Single(r => r.Code == ReglageEcran).Valeur;
    var vueEcran = await EcranReglages.OuvrirAsync(ClientEcran(Agent), "T001");
    Exiger(vueEcran is { Mode: "serveur", Greffe: "T001" } && vueEcran.Reglages.Length == lusParRoute.Length && vueEcran.Reglages.Any(r => r.Code == ReglageEcran),
        $"Ecran SOC-003 T03 : l'ecran lit les reglages du greffe T001 par la route ({vueEcran.Mode}, {vueEcran.Reglages.Length} reglages, route : {lusParRoute.Length})");
    var refusEcranReglage = await EcranReglages.ModifierAsync(ClientEcran(SansDroit), "T001", ReglageEcran, "valeur interdite");
    var apresRefusEcran = (await Lire<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"))).Single(r => r.Code == ReglageEcran).Valeur;
    Exiger(refusEcranReglage is { Echec: true } && refusEcranReglage.Message!.Contains("n'a pas le droit de modifier le greffe")
           && !refusEcranReglage.Message.Contains("{") && apresRefusEcran == valeurAvantEcran,
        $"Ecran : sans le droit, le motif du serveur s'affiche tel quel (« {refusEcranReglage.Message} ») et le reglage ne bouge pas ({apresRefusEcran})");
    var modifEcran = await EcranReglages.ModifierAsync(ClientEcran(Agent), "T001", ReglageEcran, "valeur ecran");
    var modifie = modifEcran.Reglages.SingleOrDefault(r => r.Code == ReglageEcran);
    Exiger(modifEcran is { Echec: false, Selection: ReglageEcran } && modifie is { Valeur: "valeur ecran", DateModif: not null }
           && modifie.AuteurModif is { Length: > 0 } && modifEcran.Message!.Contains("modifié"),
        $"Ecran SOC-003 CA07 : modifie depuis l'ecran, le reglage montre sa valeur, son auteur et sa date ({modifie?.Valeur}, {modifie?.AuteurModif}, {modifie?.DateModif:s}) -- « {modifEcran.Message} »");
    await EcranReglages.ModifierAsync(ClientEcran(Agent), "T001", ReglageEcran, valeurAvantEcran);   // remis comme avant
    var absentEcran = await EcranReglages.ModifierAsync(ClientEcran(Agent), "T001", "BANC_INEXISTANT", "1");
    Exiger(absentEcran is { Echec: true } && absentEcran.Message!.Contains("n'existe pas"),
        $"Ecran : un reglage absent est refuse avec le motif du serveur (« {absentEcran.Message} »)");
    var sansGreffeEcran = await EcranReglages.OuvrirAsync(ClientEcran(Agent), null);
    var demoReglages = await EcranReglages.OuvrirAsync(null, null);
    Exiger(sansGreffeEcran is { Mode: "sans-greffe", Reglages.Length: 0 } && demoReglages is { Mode: "demo" } && demoReglages.Reglages.Length > 0,
        $"Ecran : branche sans greffe ouvert, il le dit ({sansGreffeEcran.Mode}) ; sans serveur, {demoReglages.Reglages.Length} reglages de demonstration");

    // =====================================================================================
    // Acces — decision du plan (lot 2) : une route de donnees pose PolitiquesSocle.AgentAdmis
    // =====================================================================================
    Console.WriteLine("--- Acces aux routes du catalogue et des reglages");
    var reglageT001 = RoutesGreffes.Pour(RoutesGreffes.Reglage, "T001", "NUMERISE_LIASSE1");
    var appels = new (HttpMethod Verbe, string Adresse, object? Corps)[]
    {
        (HttpMethod.Get, RoutesGreffes.Liste, null),
        (HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Fiche, "T002"), null),
        (HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Historique, "T002"), null),
        (HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Passage, "T002"), new SaisiePassage("2099-01-01T08:00")),
        (HttpMethod.Put, RoutesGreffes.Pour(RoutesGreffes.Fuseau, "T002"), new SaisieFuseau("Romance Standard Time")),
        (HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Base, "T001"), null),
        (HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Reglages, "T001"), null),
        (HttpMethod.Get, reglageT001, null),
        (HttpMethod.Put, reglageT001, new SaisieReglage("1")),
        (HttpMethod.Get, RoutesGreffes.Pour(RoutesGreffes.Reprise, "T001"), null),
    };
    // Toutes les routes du module, lues dans le serveur lui-meme : une route ajoutee hors du groupe
    // protege, ou absente de la liste ci-dessus, fait tomber ce controle.
    var routesModule = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
        .Where(r => r.Metadata.GetMetadata<ModuleOrigine>()?.Nom == "RigV3.Serveur.Socle.Greffes.ModuleGreffes").ToList();
    var sansPolitique = routesModule
        .Where(r => !r.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == PolitiquesSocle.AgentAdmis))
        .Select(r => r.RoutePattern.RawText).ToList();
    Exiger(routesModule.Count == appels.Length && sansPolitique.Count == 0,
        $"Acces : les {routesModule.Count} routes du module portent PolitiquesSocle.AgentAdmis, toutes appelees ci-dessous"
        + (sansPolitique.Count > 0 ? $" (sans politique : {string.Join(", ", sansPolitique)})" : ""));

    // Ce que les modifications changeraient : un refus ne doit rien en bouger (la route n'a pas tourne).
    async Task<string> EtatModifiable()
    {
        var fiche = await Lire<FicheGreffe>(RoutesGreffes.Pour(RoutesGreffes.Fiche, "T002"));
        var traces = (await Lire<ModificationGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Historique, "T002"))).Length;
        return $"{fiche.Fuseau}|{fiche.PassageLocal:o}|{traces}|{(await Lire<ReglageGreffe>(reglageT001)).Valeur}";
    }
    var avantAppels = await EtatModifiable();
    var nonRefusesAnonyme = new List<string>();
    var nonRefusesRefuse = new List<string>();
    foreach (var (verbe, adresseAppel, corps) in appels)
    {
        var anonymeAppel = await Envoyer(verbe, adresseAppel, corps, compte: null);
        if (anonymeAppel.StatusCode != HttpStatusCode.Unauthorized) nonRefusesAnonyme.Add($"{verbe} {adresseAppel} -> {(int)anonymeAppel.StatusCode}");
        var refuseAppel = await Envoyer(verbe, adresseAppel, corps, compte: Refuse);
        if (refuseAppel.StatusCode != HttpStatusCode.Forbidden) nonRefusesRefuse.Add($"{verbe} {adresseAppel} -> {(int)refuseAppel.StatusCode}");
    }
    var apresAppels = await EtatModifiable();
    Exiger(nonRefusesAnonyme.Count == 0 && apresAppels == avantAppels,
        $"Acces : sans session Windows, chacune des {appels.Length} routes repond 401 et rien ne change"
        + (nonRefusesAnonyme.Count > 0 ? $" (passees : {string.Join(" ; ", nonRefusesAnonyme)})" : ""));
    Exiger(nonRefusesRefuse.Count == 0 && apresAppels == avantAppels,
        $"Acces : un compte refuse (desactive) recoit 403 sur chacune des {appels.Length} routes et rien ne change"
        + (nonRefusesRefuse.Count > 0 ? $" (passees : {string.Join(" ; ", nonRefusesRefuse)})" : ""));
    // Jumeau : l'agent admis lit par chacune des routes de lecture ; ses modifications sont acceptees
    // plus haut (SOC-003 CA07, SOC-007 CA05 et CA06).
    var lecturesAdmis = new List<string>();
    foreach (var (verbe, adresseAppel, _) in appels.Where(a => a.Verbe == HttpMethod.Get))
    {
        var admisAppel = await Envoyer(verbe, adresseAppel);
        if (admisAppel.StatusCode != HttpStatusCode.OK) lecturesAdmis.Add($"{adresseAppel} -> {(int)admisAppel.StatusCode}");
    }
    Exiger(lecturesAdmis.Count == 0,
        $"Acces (jumeau) : l'agent admis lit par les {appels.Count(a => a.Verbe == HttpMethod.Get)} routes de lecture (200)"
        + (lecturesAdmis.Count > 0 ? $" (refusees : {string.Join(" ; ", lecturesAdmis)})" : ""));

    await app.StopAsync();
}
catch (Exception e)
{
    // Pas avalee : le banc est rouge et la cause s'affiche.
    Exiger(false, $"le banc s'est arrete : {e.GetType().Name} : {e.Message}");
    Console.WriteLine(e);
}
finally
{
    // Ce que le banc a cree hors BaseJetable : bases V3, comptes, fichiers de sauvegarde (serveur local seulement).
    SqlConnection.ClearAllPools();
    foreach (var b in basesV3)
        await Executer("master", $"IF DB_ID(N'{b}') IS NOT NULL BEGIN ALTER DATABASE [{b}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{b}]; END");
    foreach (var c in comptes)
        await Executer("master", $"IF SUSER_ID(N'{c}') IS NOT NULL DROP LOGIN [{c}];");
    foreach (var f in sauvegardes)
    {
        // xp_delete_files : SQL Server 2022 (le serveur des bancs) ; un fichier qui resterait est dit, pas tu.
        var e = await Lever(() => Executer("master", $"EXEC master.sys.xp_delete_files N'{f}';"));
        if (e is not null) Console.WriteLine($"attention : sauvegarde d'essai non supprimee ({f}) : {e.Message}");
    }
}
foreach (var b in basesV3)
    Exiger(!await BaseJetable.ExisteAsync(b), $"nettoyage : la base V3 d'essai {b} est supprimee");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "SocleGreffesCheck : VERT -- SOC-003, SOC-007 et SOC-008, un controle par critere."
    : $"SocleGreffesCheck : ROUGE -- {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;
