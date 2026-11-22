using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;

// Banc du LOT 3 de la phase 2 du plan de reconstruction (2026-11-21) : SOC-014 et SOC-015.
//
// Un controle par critere du carnet (C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json),
// qui cite son id ; tout refus a son jumeau accepte.
//
// OU IL ECRIT : le SQL Server LOCAL seulement (docker compose up -d) -- sa base jetable (BaseJetable), supprimee en
// sortie -- et un dossier temporaire pour la fausse V2.
// OU IL LIT : les sources de la V2 (D:\RigApplication\Source, variable RIGV3_V2_SOURCES), pour l'inventaire des chemins
// de creation d'affaire (SOC-014 CA04). Il ne lance JAMAIS la vraie V2 : il se relance lui-meme en « fausse V2 », une
// fenetre hors ecran, sans bouton de barre des taches ni activation, titree comme la console de la V2, qui note ses
// arguments et les messages WM_COPYDATA recus. Le premier plan et le presse-papiers de la personne qui lance le banc
// ne sont pas touches (PosteBanc les note sans les faire).
//
// Source V2 relue avant de coder : RigConsoleAccueil.cs:52-91 (lancement, instance unique par greffe, titre :43),
// FormRigClientAccueil.cs:96-100 et :421-445 (WM_COPYDATA, EXECUTE_PROCESSUS), AmiCommunication.cs:36-47 et :78-95
// (format du message), ListeParametre.cs:153-262 (lecture des arguments), RigFormLogin.cs:282-319 (« /g3093 » :311),
// PROC_RECHAFF.cs (NoAffaire : modification NON livree de D:\RigApplication, non commitee), Affaire.cs:122-144.
// fix-ok: premier lancement rouge sur CS0103 (File, Path, HttpRequestMessage) -- un projet UseWPF retire System.IO et System.Net.Http des using implicites ; ajoutes en tete.

const string VariableFausseV2 = "RIGV3_BANC_FAUSSE_V2";
if (Environment.GetEnvironmentVariable(VariableFausseV2) is { Length: > 0 } dossierFausseV2)
    return FausseV2.Jouer(dossierFausseV2, args);

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var migrations = Path.Combine(racine, "Base", "migrations");
const string Agent = @"AMITEL\banc.agent";     // admis
const string Refuse = @"AMITEL\banc.refuse";   // desactive : 403
const string ComptesDuBanc = """
    INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES ('T001', 'EXPL', N'Exploitation');
    INSERT INTO soc.Role (Nom, Libelle) VALUES ('BANC', 'Banc SocleAiguillage');
    INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
    SELECT v.Compte, 'BANC', 'Aiguillage', v.Abrege, 'M.', 'T001', s.Id, v.Desactive, 0, 0
    FROM (VALUES ('banc.agent', 'BAGT', 0), ('banc.refuse', 'BREF', 1)) v (Compte, Abrege, Desactive)
    CROSS JOIN soc.Service s;
    INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'BANC' FROM soc.Compte;
    """;

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
static DateTime D(string iso) => DateTime.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

// Les greffes du catalogue d'essai.
const string Passe = "T001", Futur = "T002", SansPassage = "T003", OutreMerAvant = "T004", OutreMerApres = "T005";
// Deux greffes beta passes (REC-007 T02, lot 11) : l'un revenu a l'ancien RIG -- sa base V3 est la base jetable du banc (elle a
// toutes les migrations, rec.EtatRetour compris) --, l'autre non (une base V3 vide). L'aiguillage les lit pour eux seuls.
const string BetaRevenu = "T006", BetaEnV3 = "T007";
var passageT001 = D("2026-08-01T08:00:00");
const string Hawai = "Hawaiian Standard Time";   // UTC-10 : 11 h de moins que Paris en hiver
var maintenantHawai = Fuseaux.MaintenantLocal(Hawai);
var passageT004 = maintenantHawai.AddHours(3);    // pas encore atteint A HAWAI, deja depasse a l'heure de Paris
var passageT005 = maintenantHawai.AddHours(-3);   // atteint a Hawai

await using var commune = await BaseJetable.CreerAsync("SocleAiguillageCheck");
// La base V3 du greffe beta sans retour (une base V3 par greffe, UQ_Greffe_BaseV3) : vide, comme une base d'avant la migration REC-007.
await using var v3SansRetour = await BaseJetable.CreerAsync("SocleAiguillageV3");
var dossierTemp = Directory.CreateTempSubdirectory("rigv3-banc-aiguillage-");
var fausseV2Lancees = new List<Process>();
try
{
    await Migrateur.AppliquerAsync(commune.Connexion, migrations);
    await using (var cn = new SqlConnection(commune.Connexion))
    {
        await cn.OpenAsync();
        await using (var cmd = new SqlCommand(ComptesDuBanc, cn)) await cmd.ExecuteNonQueryAsync();
        foreach (var (code, fuseau, passage) in new (string, string, DateTime?)[]
                 {
                     (Passe, Fuseaux.Metropole, passageT001), (Futur, Fuseaux.Metropole, D("2099-06-01T08:00:00")),
                     (SansPassage, Fuseaux.Metropole, null), (OutreMerAvant, Hawai, passageT004), (OutreMerApres, Hawai, passageT005),
                 })
        {
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, PassageLocal, ModifiePar, ModifieLe)
                VALUES (@c, @n, 'SQL-BANC', @v2, @v3, 0, @om, @f, @p, 'banc', SYSDATETIME());
                """, cn);
            cmd.Parameters.AddWithValue("@c", code);
            cmd.Parameters.AddWithValue("@n", $"Greffe {code} (banc)");
            cmd.Parameters.AddWithValue("@v2", "RIG_BANC_" + code);
            cmd.Parameters.AddWithValue("@v3", "RIGV3_BANC_" + code);
            cmd.Parameters.AddWithValue("@om", fuseau != Fuseaux.Metropole);
            cmd.Parameters.AddWithValue("@f", fuseau);
            cmd.Parameters.Add(new SqlParameter("@p", System.Data.SqlDbType.DateTime2) { Value = (object?)passage ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
        }
        var instance = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs()).DataSource;
        await using (var cmd = new SqlCommand($"""
            INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, PassageLocal, ModifiePar, ModifieLe)
            VALUES ('{BetaRevenu}', N'Greffe beta revenu (banc)', @i, 'RIG_BANC_{BetaRevenu}', @v3, 1, 0, @f, @p, 'banc', SYSDATETIME()),
                   ('{BetaEnV3}', N'Greffe beta en V3 (banc)', @i, 'RIG_BANC_{BetaEnV3}', @v3b, 1, 0, @f, @p, 'banc', SYSDATETIME());
            INSERT INTO rec.DecisionRetour (BaseV2, Auteur, Motif, Etat) VALUES ('RIG_BANC_{BetaRevenu}', N'banc.responsable', N'banc', 'faite');
            INSERT INTO rec.EtatRetour (BaseV2, Etat, Depuis, IdDecision) VALUES ('RIG_BANC_{BetaRevenu}', 'revenu en V2', '2026-11-20T21:30:00', SCOPE_IDENTITY());
            """, cn))
        {
            cmd.Parameters.AddWithValue("@i", instance);
            cmd.Parameters.AddWithValue("@v3", commune.Nom);
            cmd.Parameters.AddWithValue("@v3b", v3SansRetour.Nom);
            cmd.Parameters.AddWithValue("@f", Fuseaux.Metropole);
            cmd.Parameters.Add(new SqlParameter("@p", System.Data.SqlDbType.DateTime2) { Value = passageT001 });
            await cmd.ExecuteNonQueryAsync();
        }
    }
    var authentification = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    authentification.Remove("Data Source");

    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.SetMinimumLevel(LogLevel.Warning);
        s.Configuration[$"ConnectionStrings:{BaseRig.NomConnexion}"] = commune.Connexion;
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();
    var http = app.GetTestClient();

    async Task<HttpResponseMessage> Poser(string code, object? corps, string? compte = Agent)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, RoutesAiguillage.Pour(RoutesAiguillage.Aiguiller, code))
        {
            Content = corps is string brut ? new StringContent(brut, Encoding.UTF8, "application/json") : JsonContent.Create(corps),
        };
        if (compte is not null) req.Headers.Add(RoutesIdentite.EnTeteCompte, compte);
        return await http.SendAsync(req);
    }
    async Task<ReponseAiguillage> Aiguiller(string code, params AffaireAAiguiller[] affaires)
    {
        var r = await Poser(code, new QuestionAiguillage(affaires));
        if (r.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException($"{code} : {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<ReponseAiguillage>())!;
    }
    static string Logiciel(ReponseAiguillage r, string numero) => r.Affaires.Single(a => a.Numero == numero).Logiciel;

    // =====================================================================================
    // SOC-014 — chaque affaire au bon logiciel selon sa date de creation
    // =====================================================================================
    Console.WriteLine("--- SOC-014 : aiguillage par affaire (serveur)");

    // CA01 : greffe passe, nouvelle affaire -> V3 ; jumeaux : passage futur ou non fixe -> V2.
    var nouvelle = new AffaireAAiguiller("nouvelle", null);
    var t001 = await Aiguiller(Passe, nouvelle);
    Exiger(Logiciel(t001, "nouvelle") == Logiciels.V3 && t001.PassageLocal == passageT001,
        $"SOC-014 CA01 : greffe passe le 01/08/2026 08:00 (catalogue), une nouvelle affaire est creee dans la V3 ({t001.Affaires[0].Motif})");
    Exiger(Logiciel(await Aiguiller(Futur, nouvelle), "nouvelle") == Logiciels.V2
           && Logiciel(await Aiguiller(SansPassage, nouvelle), "nouvelle") == Logiciels.V2,
        "SOC-014 CA01 (jumeau) : greffe au passage a venir, ou sans date de passage, une nouvelle affaire reste dans la V2");
    // L'heure du greffe, pas celle du serveur : a Hawai, 3 h avant le passage, alors qu'a Paris il est deja depasse.
    var t004 = await Aiguiller(OutreMerAvant, nouvelle);
    var t005 = await Aiguiller(OutreMerApres, nouvelle);
    Exiger(Logiciel(t004, "nouvelle") == Logiciels.V2 && DateTime.Now > passageT004 && Logiciel(t005, "nouvelle") == Logiciels.V3
           && t004.MaintenantLocal is { } mh && Math.Abs((mh - maintenantHawai).TotalMinutes) < 5,
        $"SOC-014 CA01 : un greffe d'outre-mer passe a SON heure -- passage {passageT004:dd/MM HH:mm} heure de Hawai pas encore atteint : V2 ; " +
        $"3 h apres : V3 (heure du greffe rendue : {t004.MaintenantLocal:dd/MM HH:mm})");

    // CA02 : une affaire creee avant le passage -> V2, une demande la concernant suit l'affaire ; la borne.
    var t001Dates = await Aiguiller(Passe,
        new("004168", D("2026-07-30T00:00:00")), new("a-0759", D("2026-08-01T07:59:00")),
        new("a-0800", D("2026-08-01T08:00:00")), new("004176", D("2026-08-24T00:00:00")));
    Exiger(Logiciel(t001Dates, "004168") == Logiciels.V2 && Logiciel(t001Dates, "a-0759") == Logiciels.V2,
        "SOC-014 CA02 : une affaire creee avant le passage (30/07, et 01/08 a 07:59) se traite dans la V2, demandes comprises");
    Exiger(Logiciel(t001Dates, "a-0800") == Logiciels.V3 && Logiciel(t001Dates, "004176") == Logiciels.V3,
        "SOC-014 CA02 (jumeau) : creee a l'heure du passage (01/08 08:00, la borne de PassageFait) ou apres : V3");

    // CA03 : nouvelle formalite sur une entreprise immatriculee avant le passage = nouvelle affaire = V3.
    var formalite = await Aiguiller(Passe,
        new("affaire de l'immatriculation 2019 de 109 678 235", D("2019-03-12T10:00:00")),
        new("nouvelle formalite sur 109 678 235", null));
    Exiger(Logiciel(formalite, "affaire de l'immatriculation 2019 de 109 678 235") == Logiciels.V2
           && Logiciel(formalite, "nouvelle formalite sur 109 678 235") == Logiciels.V3,
        "SOC-014 CA03 : la nouvelle formalite d'une entreprise immatriculee en 2019 ouvre une affaire dans la V3 ; l'affaire de 2019 reste dans la V2");

    // CA04 (partiel) : la V2 refuse de creer apres le passage. Ce poste ne joue pas la V2 d'un greffe passe ; il tient
    // l'inventaire des chemins de creation a fermer (SOC-014 T03), compare au code de la V2, et l'aiguillage donne a
    // la V3 toute creation posterieure au passage.
    var sourcesV2 = Environment.GetEnvironmentVariable("RIGV3_V2_SOURCES") ?? @"D:\RigApplication\Source";
    var doc = File.ReadAllText(Path.Combine(racine, "docs", "reconstruction", "socle-aiguillage.md"));
    var inventaire = Regex.Matches(doc, @"^\| `([^`]+\.cs)` \| (\d+) \|", RegexOptions.Multiline)
        .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
    var creation = new Regex(@"Affaire\.CreateAffaire(SansSauver)?\(");
    var horsInventaire = new Regex(@"^Migration|/Moulinettes/|BatchOneShot");   // outils de reprise jouables une fois
    var trouves = Directory.Exists(sourcesV2)
        ? Directory.EnumerateFiles(sourcesV2, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(sourcesV2, f).Replace('\\', '/'), Fichier: f))
            .Where(f => !f.Rel.Contains("/obj/") && !horsInventaire.IsMatch(f.Rel))
            .Select(f => (f.Rel, Nb: creation.Matches(File.ReadAllText(f.Fichier)).Count))
            .Where(f => f.Nb > 0)
            .ToDictionary(f => f.Rel, f => f.Nb)
        : [];
    var manquants = trouves.Where(t => !inventaire.TryGetValue(t.Key, out var n) || n != t.Value).Select(t => $"{t.Key} ({t.Value})").ToList();
    var perimes = inventaire.Keys.Where(k => !trouves.ContainsKey(k)).ToList();
    Exiger(trouves.Count > 0 && manquants.Count == 0 && perimes.Count == 0,
        $"SOC-014 CA04 (partiel, T03) : l'inventaire des chemins de creation d'affaire de la V2 (socle-aiguillage.md) est celui du code -- " +
        $"{trouves.Count} fichiers, {trouves.Values.Sum()} appels" +
        (manquants.Count + perimes.Count > 0 ? $" ; absents de l'inventaire : {string.Join(", ", manquants.Take(5))} ; perimes : {string.Join(", ", perimes.Take(5))}" : ""));
    Exiger(Logiciel(await Aiguiller(Passe, new AffaireAAiguiller("creation tentee dans la V2", null)), "creation tentee dans la V2") == Logiciels.V3,
        "SOC-014 CA04 (partiel) : apres le passage, toute creation d'affaire est donnee a la V3 -- les chemins de l'inventaire sont a fermer dans la V2");

    // CA05 : une arrivee suit l'affaire visee ; sans affaire, la V3.
    var arrivees = await Aiguiller(Passe, new("depot sur l'affaire 004168", D("2026-07-30T00:00:00")), new("echange sans affaire", null));
    Exiger(Logiciel(arrivees, "depot sur l'affaire 004168") == Logiciels.V2 && Logiciel(arrivees, "echange sans affaire") == Logiciels.V3,
        "SOC-014 CA05 : une demande d'un echange ou d'un depot va au logiciel de l'affaire visee (V2) ; si elle n'en vise aucune, a la V3");

    // CA06 : la seule comparaison des deux dates, sans regle par domaine (Q18).
    var champs = typeof(AffaireAAiguiller).GetProperties().Select(p => p.Name).Order().ToArray();
    var avecDomaines = await Poser(Passe, """
        {"affaires":[{"numero":"RCS-avant","creeeLe":"2026-07-30T00:00:00","domaine":"RCS"},
                     {"numero":"JUD-avant","creeeLe":"2026-07-30T00:00:00","domaine":"JUD","nature":"collective"},
                     {"numero":"RCS-apres","creeeLe":"2026-08-24T00:00:00","domaine":"RCS"},
                     {"numero":"JUD-apres","creeeLe":"2026-08-24T00:00:00","domaine":"JUD","nature":"mesure"}]}
        """);
    var domaines = (await avecDomaines.Content.ReadFromJsonAsync<ReponseAiguillage>())!;
    Exiger(champs.SequenceEqual(["CreeeLe", "Numero"])
           && Logiciel(domaines, "RCS-avant") == Logiciels.V2 && Logiciel(domaines, "JUD-avant") == Logiciels.V2
           && Logiciel(domaines, "RCS-apres") == Logiciels.V3 && Logiciel(domaines, "JUD-apres") == Logiciels.V3,
        "SOC-014 CA06 : l'aiguillage repond V2 ou V3 d'apres la seule date de creation -- la question n'a que numero et date, " +
        "un domaine ou une nature glisses dans la demande ne changent rien (RCS et JUD du meme jour : meme reponse)");
    Exiger(domaines.Affaires.All(a => a.Logiciel is Logiciels.V2 or Logiciels.V3),
        "SOC-014 CA06 : la reponse ne prend que les valeurs « V2 » ou « V3 »");

    // REC-007 T02 (lot 11) : un greffe beta revenu a l'ancien RIG -- passage atteint, qui ne change plus (SOC-007 CA05) --
    // donne toute affaire a la V2, nouvelle comme creee apres le passage : la creation est rouverte dans la V2, la V3 la
    // refuse (REC-007 CA03, GardeAffaires). Jumeau : le meme greffe beta passe, sans retour, garde la regle des dates.
    var revenu = await Aiguiller(BetaRevenu, nouvelle, new("creee apres le passage", D("2026-08-24T00:00:00")));
    var enV3 = await Aiguiller(BetaEnV3, nouvelle, new("creee apres le passage", D("2026-08-24T00:00:00")), new("creee avant", D("2026-07-30T00:00:00")));
    Exiger(revenu.Affaires.All(a => a.Logiciel == Logiciels.V2 && a.Motif.Contains("revenu a l'ancien RIG le 20/11/2026 21:30")) && revenu.PassageLocal == passageT001,
        $"REC-007 T02 : greffe beta revenu a l'ancien RIG (rec.EtatRetour de sa base V3), passage du 01/08 atteint : une nouvelle affaire et une affaire " +
        $"creee apres le passage vont a la V2 ({revenu.Affaires[0].Motif})");
    Exiger(Logiciel(enV3, "nouvelle") == Logiciels.V3 && Logiciel(enV3, "creee apres le passage") == Logiciels.V3 && Logiciel(enV3, "creee avant") == Logiciels.V2,
        "REC-007 T02 (jumeau) : un greffe beta passe sans retour garde la regle des dates (nouvelle et posterieure : V3, anterieure : V2)");

    // Acces et refus de la route (chacun a son jumeau accepte plus haut : agent admis, greffe connu, liste lisible).
    Exiger((await Poser(Passe, new QuestionAiguillage([nouvelle]), compte: null)).StatusCode == HttpStatusCode.Unauthorized
           && (await Poser(Passe, new QuestionAiguillage([nouvelle]), compte: Refuse)).StatusCode == HttpStatusCode.Forbidden,
        "SOC-014 (acces) : sans session Windows 401, compte desactive 403 ; l'agent admis est servi");
    Exiger((await Poser("T999", new QuestionAiguillage([nouvelle]))).StatusCode == HttpStatusCode.NotFound,
        "SOC-014 (refus) : greffe absent du catalogue : 404, sans repli sur un autre greffe");
    var mille = Enumerable.Range(0, RoutesAiguillage.AffairesParQuestion).Select(i => new AffaireAAiguiller($"n{i}", null)).ToArray();
    Exiger((await Poser(Passe, """{"affaires":null}""")).StatusCode == HttpStatusCode.BadRequest
           && (await Poser(Passe, new QuestionAiguillage([.. mille, nouvelle]))).StatusCode == HttpStatusCode.BadRequest
           && (await Poser(Passe, new QuestionAiguillage(mille))).StatusCode == HttpStatusCode.OK,
        $"SOC-014 (refus) : liste absente ou de plus de {RoutesAiguillage.AffairesParQuestion} affaires : 400 ; {RoutesAiguillage.AffairesParQuestion} tout juste : servie");

    // =====================================================================================
    // SOC-015 — ouvrir une ancienne affaire dans la V2 depuis la V3
    // =====================================================================================
    Console.WriteLine("--- SOC-015 : l'ancien RIG depuis la recherche d'affaires (pont)");
    ClientServeur ClientEcran(string compte)
    {
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, compte);
        try { return new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
    }
    var client = ClientEcran(Agent);
    string[] lignesRecherche = ["004168", "004176", "2026B01067", "inconnu-du-fonds"];

    // CA01 : la recherche marque les anciennes affaires (branchee au serveur, et en demonstration, meme regle).
    var vueServeur = await AncienRig.AiguillerAsync(client, Passe, lignesRecherche, EcranRechAffaires.CreationDe);
    string? De(VueAiguillage v, string n) => v.Affaires.FirstOrDefault(a => a.Numero == n)?.Logiciel;
    Exiger(vueServeur is { Mode: "serveur", Passage: "01/08/2026 08:00" } && De(vueServeur, "004168") == "V2"
           && De(vueServeur, "004176") == "V3" && De(vueServeur, "2026B01067") == "V3" && De(vueServeur, "inconnu-du-fonds") is null,
        "SOC-015 CA01 : branchee au serveur, la recherche marque « traitee dans la V2 » l'affaire 004168 (creee le 30/07, passage du greffe le 01/08) ; " +
        "004176 et 2026B01067 restent V3 ; un numero hors du fonds n'est pas aiguille (une date absente vaudrait « nouvelle affaire »)");
    var vueDemo = await AncienRig.AiguillerAsync(null, null, lignesRecherche, EcranRechAffaires.CreationDe);
    Exiger(vueDemo is { Mode: "demo", Passage: "01/08/2026 08:00" } && De(vueDemo, "004168") == "V2" && De(vueDemo, "2026B01067") == "V3"
           && vueDemo.Affaires.All(a => a.Logiciel == RegleAiguillage.Aiguiller(a.Numero, AncienRig.PassageDemo, EcranRechAffaires.CreationDe(a.Numero), null).Logiciel),
        "SOC-015 CA01 : sans serveur, la demonstration fixe le passage au 01/08/2026 08:00 et joue la MEME regle que le serveur");
    var vueRefus = await AncienRig.AiguillerAsync(ClientEcran(Refuse), Passe, lignesRecherche, EcranRechAffaires.CreationDe);
    var vueInconnu = await AncienRig.AiguillerAsync(client, "T999", lignesRecherche, EcranRechAffaires.CreationDe);
    var vueSansGreffe = await AncienRig.AiguillerAsync(client, null, lignesRecherche, EcranRechAffaires.CreationDe);
    Exiger(vueRefus is { Mode: "refuse", Affaires.Length: 0 } && vueInconnu is { Mode: "erreur", Affaires.Length: 0 }
           && vueSansGreffe is { Mode: "sans-greffe", Affaires.Length: 0 },
        "SOC-015 CA01 (jumeau) : aiguillage refuse (403), greffe inconnu, ou aucun greffe ouvert : aucune mention, l'ecran reste celui d'avant");

    // CA01 + CA04 a l'ecran : la mention, le bloc « Ancien RIG », la suppression refusee. Ce sont les tests de
    // l'ecran (vitest), JOUES ici pour que ce banc couvre chaque critere.
    var vitest = Jouer("cmd.exe", "/c npx vitest run src/ecrans/EcranRechAffaires.test.tsx -t SOC-015", Path.Combine(racine, "web"));
    Exiger(vitest.Code == 0 && Regex.IsMatch(vitest.Sortie, @"Tests\s+3 passed"),
        "SOC-015 CA01, CA02, CA04 (ecran, vitest « SOC-015 ») : mention « traitee dans la V2 », « Ouvrir dans l'ancien RIG » sur l'affaire choisie, " +
        $"suppression refusee sur une ancienne affaire et simulee sur une affaire de la V3 (code {vitest.Code})"
        + (vitest.Code == 0 ? "" : " -- " + string.Join(" | ", vitest.Sortie.Split('\n').Where(l => l.Contains("FAIL") || l.Contains("Error")).Take(4))));

    // CA02 (gestes) : V2 deja ouverte -> ramenee, sans second lancement ; affaire de la V3 -> refus.
    var poste = new PosteFactice { Fenetre = (IntPtr)0x4242 };
    var ramenee = await AncienRig.OuvrirAsync("004168", client, Passe, EcranRechAffaires.CreationDe, poste, @"C:\RIG\exe\RigClientAccueil.exe",
                                              TimeSpan.FromSeconds(5), attendreLaV2: true);
    Exiger(ramenee is { Fait: true, Geste: "ramene" } && poste.Lancements.Count == 0
           && poste.Journal.SequenceEqual(["copier 004168", "chercher RigClientAccueil T001", "ramener 16962",
                                            "envoyer 16962 EXECUTE_PROCESSUS|RECHAFF|NoAffaire=004168"]),
        $"SOC-015 CA02 : la V2 du greffe deja ouverte dans la session est ramenee au premier plan, sans second lancement, et ouvre sa recherche d'affaires ({string.Join(" ; ", poste.Journal)})");
    var posteV3 = new PosteFactice();
    var refusV3 = await AncienRig.OuvrirAsync("004176", client, Passe, EcranRechAffaires.CreationDe, posteV3, @"C:\RIG\exe\RigClientAccueil.exe",
                                              TimeSpan.FromSeconds(5), attendreLaV2: true);
    var refusNumero = await AncienRig.OuvrirAsync("0041|68", client, Passe, EcranRechAffaires.CreationDe, posteV3, @"C:\RIG\exe\RigClientAccueil.exe",
                                                  TimeSpan.FromSeconds(5), attendreLaV2: true);
    var refusAbsente = await AncienRig.OuvrirAsync("004168", client, Passe, EcranRechAffaires.CreationDe, new PosteFactice { ExeLa = false },
                                                   @"C:\RIG\exe\RigClientAccueil.exe", TimeSpan.FromSeconds(5), attendreLaV2: true);
    Exiger(refusV3 is { Fait: false, Geste: "refuse" } && refusV3.Message.Contains("se traite dans la V3") && posteV3.Journal.Count == 0
           && refusNumero is { Fait: false } && refusAbsente is { Fait: false } && refusAbsente.Message.Contains("pas installe"),
        "SOC-015 CA02 (jumeaux) : une affaire de la V3 n'ouvre pas l'ancien RIG (rien copie, rien lance) ; un numero portant « | » est refuse ; " +
        "une V2 absente du poste est dite, sans lancement");

    // CA02, CA03, CA05 de bout en bout : la V3 LANCE la « V2 » (ce banc, relance en fausse V2), lui parle quand sa
    // fenetre est prete, puis la RAMENE au second geste.
    Environment.SetEnvironmentVariable(VariableFausseV2, dossierTemp.FullName);
    var exeFausse = Environment.ProcessPath!;
    var posteReel = new PosteBanc(fausseV2Lancees);
    var lancee = await AncienRig.OuvrirAsync("004168", client, Passe, EcranRechAffaires.CreationDe, posteReel, exeFausse,
                                             TimeSpan.FromSeconds(60), attendreLaV2: true);
    var argsRecus = File.Exists(Path.Combine(dossierTemp.FullName, "args.txt")) ? File.ReadAllLines(Path.Combine(dossierTemp.FullName, "args.txt")) : [];
    var messages = LireMessages(dossierTemp.FullName);
    Exiger(lancee is { Fait: true, Geste: "lance" } && lancee.LigneDeCommande == $"\"{exeFausse}\" /gT001"
           && argsRecus.SequenceEqual(["/gT001"]) && SessionAgent.GreffeDeLancement(argsRecus) == Passe,
        $"SOC-015 CA02 : V2 fermee, la V3 la lance comme son raccourci Windows (« RigClientAccueil.exe /g<code> », RigFormLogin.cs:311) : " +
        $"la V2 recoit [{string.Join(", ", argsRecus)}] et y lit le greffe {SessionAgent.GreffeDeLancement(argsRecus)} (lecture de ListeParametre)");
    var cles = CleV2(string.Join(' ', argsRecus));
    Exiger(cles.Keys.SequenceEqual(["greffe"]) && !argsRecus.Any(a => a.Contains("utilisateur", StringComparison.OrdinalIgnoreCase)),
        "SOC-015 CA05 : aucun identifiant passe a la V2 (ni « utilisateur= » ni mot de passe) : seule la cle « greffe » ; la V2 reconnait l'agent a sa session Windows");
    var premier = messages.FirstOrDefault();
    var champsMessage = premier?.Texte.Split('|') ?? [];
    Exiger(messages.Count == 1 && premier is { DwData: 1 } && premier.CbData == (premier.Texte.Length + 1) * 2
           && champsMessage is ["EXECUTE_PROCESSUS", "RECHAFF", _] && CleV2(champsMessage[2]).GetValueOrDefault("NoAffaire") == "004168",
        $"SOC-015 CA02 : des sa fenetre prete, la V2 recoit par WM_COPYDATA le message de ses raccourcis « Envoyer vers » " +
        $"(dwData 1, UTF-16 termine par zero) : « {premier?.Texte} » -> processus RECHAFF, NoAffaire=004168");
    Exiger(posteReel.Copies.SequenceEqual(["004168"]) && posteReel.Ordre.IndexOf("copier") < posteReel.Ordre.IndexOf("lancer")
           && lancee.Message.Contains("Le numero 004168 est copie"),
        $"SOC-015 CA03 : le numero 004168 est copie AVANT que la V2 s'affiche, et dit a l'agent (« {lancee.Message} »)");

    var processusAvant = fausseV2Lancees.Count;
    var ramene2 = await AncienRig.OuvrirAsync("004168", client, Passe, EcranRechAffaires.CreationDe, posteReel, exeFausse,
                                              TimeSpan.FromSeconds(30), attendreLaV2: true);
    var messages2 = LireMessages(dossierTemp.FullName);
    var fenetreFausse = fausseV2Lancees.FirstOrDefault()?.MainWindowHandle ?? IntPtr.Zero;
    Exiger(ramene2 is { Fait: true, Geste: "ramene" } && fausseV2Lancees.Count == processusAvant && processusAvant == 1
           && posteReel.Ramenees.Count == 1 && posteReel.Ramenees[0] == fenetreFausse && messages2.Count == 2,
        "SOC-015 CA02 : au second geste, la V2 deja ouverte est retrouvee par sa fenetre (titre « Console d'accueil de RIG (T001 - … ») et ramenee, " +
        $"sans second processus ; elle recoit a nouveau la recherche ({messages2.Count} messages)");
    Exiger(new PosteWindows().FenetreV2(Path.GetFileNameWithoutExtension(exeFausse), OutreMerAvant) is null,
        "SOC-015 CA02 (jumeau) : la console d'un AUTRE greffe (T004) n'est pas prise pour celle de T001");
    Environment.SetEnvironmentVariable(VariableFausseV2, null);

    // Le serveur du banc s'arrete avant d'etre detruit (decision du lot 1 de la phase 2, controle « Arret » de
    // tests/SocleCourrielsCheck) : detruit sans StopAsync, l'hote annule ses services de fond sans s'etre arrete et le
    // Surveillant (lot 2) sort en « BackgroundService failed » au journal, une fausse panne apres un banc vert.
    await app.StopAsync();
}
finally
{
    foreach (var p in fausseV2Lancees)
    {
        // Les seuls processus que ce banc arrete : les fausses V2 qu'il a lancees lui-meme.
        try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { }
        p.Dispose();
    }
    try { dossierTemp.Delete(recursive: true); } catch (IOException) { }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "Bilan : SocleAiguillage vert." : $"Bilan : {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;

// La lecture de la V2 : un texte « p1=v1;p2=v2 » (ListeParametre.cs:216-224, SetParametreUnique :226-249).
static Dictionary<string, string> CleV2(string texte)
{
    var cles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var morceau in texte.Split(';').Select(m => m.Trim()).Where(m => m.Length > 0))
    {
        var egal = morceau.Split('=', 2);
        if (egal.Length == 2) { cles[egal[0]] = egal[1]; continue; }
        var vieux = morceau.Split(' ');
        if (vieux.Length == 1 && morceau.Length > 2) vieux = [morceau[..2], morceau[2..]];
        if (vieux[0] == "/g" && vieux.Length > 1) cles["greffe"] = vieux[1].Trim();
        else if (vieux.Length == 2) cles[vieux[0]] = vieux[1];
    }
    return cles;
}

static List<MessageRecu> LireMessages(string dossier)
{
    var f = Path.Combine(dossier, "messages.txt");
    return File.Exists(f)
        ? File.ReadAllLines(f).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<MessageRecu>(l)!).ToList()
        : [];
}

static (int Code, string Sortie) Jouer(string exe, string arguments, string dossier)
{
    var psi = new ProcessStartInfo(exe, arguments)
    {
        WorkingDirectory = dossier, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
    };
    using var p = Process.Start(psi)!;
    var sortie = p.StandardOutput.ReadToEndAsync();
    var erreurs = p.StandardError.ReadToEndAsync();
    p.WaitForExit();
    return (p.ExitCode, sortie.Result + erreurs.Result);
}

/// <summary>Un message recu par la fausse V2, tel que AmiCommunication.ReadMessage le lirait.</summary>
sealed record MessageRecu(long DwData, int CbData, string Texte);

/// <summary>Un poste entierement simule : il note chaque geste dans l'ordre.</summary>
sealed class PosteFactice : IPosteWindows
{
    public IntPtr? Fenetre { get; init; }
    public bool ExeLa { get; init; } = true;
    public List<string> Journal { get; } = [];
    public List<string> Lancements { get; } = [];
    public IntPtr? FenetreV2(string nomProcessus, string? greffe) { Journal.Add($"chercher {nomProcessus} {greffe}"); return Fenetre; }
    public bool ExisteExe(string chemin) => ExeLa;
    public Process? Lancer(string exe, string arguments) { Lancements.Add($"{exe} {arguments}"); Journal.Add("lancer"); return null; }
    public void RamenerAuPremierPlan(IntPtr fenetre) => Journal.Add($"ramener {fenetre}");
    public bool Envoyer(IntPtr fenetre, string message) { lock (Journal) Journal.Add($"envoyer {fenetre} {message}"); return true; }
    public bool Copier(string texte) { Journal.Add($"copier {texte}"); return true; }
}

/// <summary>Le poste REEL (processus, fenetres, lancement, WM_COPYDATA), sauf le premier plan et le presse-papiers de la
/// personne qui lance le banc, notes sans etre faits.</summary>
sealed class PosteBanc(List<Process> lancees) : PosteWindows
{
    public List<IntPtr> Ramenees { get; } = [];
    public List<string> Copies { get; } = [];
    public List<string> Ordre { get; } = [];
    public override Process? Lancer(string exe, string arguments)
    {
        Ordre.Add("lancer");
        var p = base.Lancer(exe, arguments);
        if (p is not null) lancees.Add(p);
        return p;
    }
    public override void RamenerAuPremierPlan(IntPtr fenetre) { Ordre.Add("ramener"); Ramenees.Add(fenetre); }
    public override bool Copier(string texte) { Ordre.Add("copier"); Copies.Add(texte); return true; }
}

/// <summary>
/// La « fausse V2 » : ce banc relance avec la variable RIGV3_BANC_FAUSSE_V2. Il note ses arguments, lit le greffe comme
/// la V2 (SessionAgent.GreffeDeLancement, la lecture de ListeParametre portee au lot 10), ouvre une fenetre titree
/// comme la console de la V2 (« Console d'accueil de RIG (&lt;code&gt; - &lt;serveur&gt; / &lt;base&gt;) »,
/// RigGreffe.cs:594), hors ecran, sans activation ni bouton de barre des taches, et note chaque WM_COPYDATA recu.
/// </summary>
static class FausseV2
{
    private const int WM_COPYDATA = 0x004A;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    public static int Jouer(string dossier, string[] args)
    {
        File.WriteAllLines(Path.Combine(dossier, "args.txt"), args);
        var titre = $"{AncienRig.TitreConsoleV2} ({SessionAgent.GreffeDeLancement(args)} - SQL-BANC / RIG_BANC)";
        var journal = Path.Combine(dossier, "messages.txt");
        var fil = new Thread(() =>
        {
            var fenetre = new Window
            {
                Title = titre, Width = 260, Height = 90, Left = -32000, Top = -32000,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, WindowStyle = WindowStyle.ToolWindow,
            };
            fenetre.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(fenetre).Handle)!.AddHook(
                (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool traite) =>
                {
                    if (msg != WM_COPYDATA) return IntPtr.Zero;
                    var cds = Marshal.PtrToStructure<CopyData>(lParam);
                    var texte = Marshal.PtrToStringUni(cds.lpData) ?? "";   // AmiCommunication.ReadMessage
                    File.AppendAllText(journal, JsonSerializer.Serialize(new MessageRecu(cds.dwData.ToInt64(), cds.cbData, texte)) + "\n");
                    traite = true;
                    return (IntPtr)1;
                });
            fenetre.Show();
            // Filet : la fausse V2 s'arrete seule si le banc qui l'a lancee disparait sans l'arreter.
            var minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
            minuterie.Tick += (_, _) => Dispatcher.CurrentDispatcher.InvokeShutdown();
            minuterie.Start();
            Dispatcher.Run();
        });
        fil.SetApartmentState(ApartmentState.STA);
        fil.Start();
        fil.Join();
        return 0;
    }
}
