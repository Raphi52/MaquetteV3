using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ParcoursCompletCheck;
using RigV3.Base;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using RigV3Desktop.Pont;
using RigV3Desktop.Pont.EcransGeneres;

// Banc du PARCOURS COMPLET du RIG v3 (28/11/2026, conv-2206) : LE thermometre de « RIG v3 complet, mesure, pas declare ».
//
// Il ouvre CHAQUE entree du menu de l'accueil (Pont/MenuAccueil.cs), les procedures d'un ecran reuni une par une, par le
// PONT de l'application (le code que la fenetre appelle) contre le vrai serveur en memoire, et note pour chacune :
//   - FORME    : la forme du descripteur (Pont/EcransGeneres/, FormeEcran) d'un ecran genere ; « fait main » et son
//                composant (web/src/plan/Aiguillage.tsx) ; « a venir » (EcranNonMaquette) sinon ;
//   - DONNEES  : « base » quand le pont rend des lignes lues dans la base du greffe ; sinon l'exemple et sa cause, mesuree
//                (aucun lecteur au serveur, erreur du serveur et son motif, pont qui n'appelle pas le serveur) ;
//   - GESTE    : un geste JOUE par le pont (fait, et trace au journal des gestes) puis RELU par le pont : les lignes de
//                l'ecran ont change, ou la fiche / le controle SQL que son jeu declare se relit. Le CONTENU exact de chaque
//                geste est prouve par EcransGeneresBaseCheck, qui joue tous les blocs dans SON ordre (alphabetique) : ici,
//                dans l'ordre du menu, les lignes attendues d'un bloc (qui portent les gestes des fichiers joues avant lui)
//                ne se comparent pas mot pour mot. Une forme sans geste d'ecriture (Consultation, Etat, Document sans geste
//                nomme) prouve sa recherche (bloc 1 de son .recherches) ou la lecture d'une fiche.
// Une ligne n'est VERTE qu'avec « base + geste prouve » ; le banc est ROUGE tant qu'une ligne ne l'est pas.
//
// LES DONNEES DU BANC : celles des jeux de tests/EcransGeneresBaseCheck/Jeux (un .sql par procedure, ses .attendu,
// .gestes, .recherches). Le banc ne les recopie pas : il lance EcransGeneresBaseCheck en mode RIGV3_BANC_GARDER, qui cree
// ses bases jetables, y joue tous les jeux (sections 0 a 3c de son Program.cs), PAS encore les gestes, puis attend ; ce banc
// sert ces bases par son propre serveur en memoire, joue SON parcours, et rend les bases en effacant le fichier de garde.
// Chaque procedure a donc son etat « jeux charges, aucun geste joue » ; le PREMIER bloc « resultat: fait » de son .gestes
// y est rejoue par le pont (les refus qui le precedent n'ecrivent rien). Les gestes se jouent dans l'ordre du menu.
//
// LES ECRANS FAITS MAIN (web/src/ecrans/Ecran*.tsx) : leurs donnees se mesurent seules -- toute methode publique de leur
// pont qui lit (premier parametre ClientServeur, puis le code du greffe ; les autres parametres recoivent une valeur vide :
// criteres par defaut, aucune affaire) est appelee, la session de l'application ouverte sur le greffe des jeux
// (SessionAgent.PreparerAsync), et son « Mode » dit d'ou viennent les lignes (« serveur » = la base). Elles sont lues au nom
// d'un agent qui a TOUTES les fonctions du greffe (et Amitel) : les droits sont prouves par leurs bancs (SocleDroitsCheck),
// ici on mesure d'ou viennent les lignes. Une erreur du serveur est rendue avec l'exception qu'il a levee. Leur GESTE, lui, ne se devine pas : il se prouve par une
// SONDE, un fichier par ecran dans tests/ParcoursCompletCheck/Sondes/ (ISondeEcran, mode d'emploi dans Sondes/Sonde.cs),
// trouvee par reflexion -- aucune ligne d'ici a toucher. Sans sonde, la ligne est rouge « geste non prouve ».
//
// RENDRE UNE LIGNE VERTE (lots du plan) :
//   - ecran genere : un lecteur et un ecrivain au serveur (Serveur/Socle/EcransGeneres/, mode d'emploi en tete de
//     LecteurEcran.cs et EcrivainEcran.cs), ses jeux .sql / .attendu / .gestes (un bloc « fait » au moins, qui declare ce
//     qu'il relit) dans tests/EcransGeneresBaseCheck/Jeux ;
//   - ecran fait main : sa lecture au serveur (Serveur/Socle/EcransMain/, sur le modele du lot 17) et sa sonde.
//
// LANCER : .\tests.ps1 Parcours (le tableau est ecrit a cote de l'exe, parcours-complet.md, et dans RIGV3_PARCOURS_TABLEAU
// si elle est posee). RIGV3_PARCOURS = « PROC_MJUD,PROC_MPC » ou « Surveiller.cs » : seules ces lignes (codes, ou fichier de
// pont) decident du code de sortie -- un lot prouve SES lignes ; toutes restent mesurees et affichees.
// Il lui faut le SQL Server de developpement : « docker compose up -d » a la racine du depot.

Console.OutputEncoding = Encoding.UTF8;
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var jour = DateTime.Today;
var dossierJeux = Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "Jeux");
var filtre = (Environment.GetEnvironmentVariable("RIGV3_PARCOURS") ?? "")
    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// ============================================================ 1. LES BASES DES JEUX (EcransGeneresBaseCheck, mode garde)

var projetJeux = Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "EcransGeneresBaseCheck.csproj");
// Le meme dossier que tests.ps1 (cinq niveaux sous la racine : le banc y relit ses jeux par « ../../../../../ »).
var sortieJeux = Path.Combine(racine, "tests", "EcransGeneresBaseCheck", "binverif", "Debug", "net10.0-windows") + Path.DirectorySeparatorChar;
Console.WriteLine("Construction de EcransGeneresBaseCheck (les jeux des ecrans generes)...");
var (codeConstruction, sortieConstruction) = await Executer("dotnet", ["build", projetJeux, "--nologo", "-v:q", $"-p:OutputPath={sortieJeux}"]);
if (codeConstruction != 0)
{
    Console.WriteLine(sortieConstruction);
    Console.WriteLine("ECHEC : EcransGeneresBaseCheck ne se construit pas : le parcours n'a pas de donnees.");
    return 2;
}

var garde = Path.Combine(Path.GetTempPath(), $"rigv3-parcours-{Guid.NewGuid():N}.txt");
var infoJeux = new ProcessStartInfo(Path.Combine(sortieJeux, "EcransGeneresBaseCheck.exe"))
{
    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
};
infoJeux.Environment["RIGV3_BANC_GARDER"] = garde;
var journalJeux = new StringBuilder();
using var bancJeux = new Process { StartInfo = infoJeux };
bancJeux.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (journalJeux) journalJeux.AppendLine(e.Data); };
bancJeux.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (journalJeux) journalJeux.AppendLine(e.Data); };
bancJeux.Start();
bancJeux.BeginOutputReadLine();
bancJeux.BeginErrorReadLine();
Dictionary<string, string>? cles = null;
var limite = DateTime.Now.AddMinutes(20);
while (cles is null && !bancJeux.HasExited && DateTime.Now < limite)
{
    await Task.Delay(500);
    if (!File.Exists(garde)) continue;
    try
    {
        var lu = File.ReadAllLines(garde).Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (lu.ContainsKey("agent")) cles = lu;
    }
    catch (IOException) { /* le fichier s'ecrit encore : relu au tour suivant */ }
}
if (cles is null)
{
    if (!bancJeux.HasExited) bancJeux.Kill();
    lock (journalJeux) Console.WriteLine(journalJeux.ToString());
    Console.WriteLine("ECHEC : EcransGeneresBaseCheck n'a pas garde ses bases (sortie ci-dessus) : le parcours n'a pas de donnees.");
    return 2;
}
int echecsAvantGestes;
lock (journalJeux) echecsAvantGestes = Regex.Matches(journalJeux.ToString(), "^ECHEC", RegexOptions.Multiline).Count;
Console.WriteLine($"Bases des jeux gardees : greffe {cles["greffe"]}, base {cles["base"]} ({echecsAvantGestes} echec(s) du banc des jeux avant ses gestes).");

var lignes = new List<LigneParcours>();
try
{
    // ============================================================ 2. LE SERVEUR EN MEMOIRE SUR CES BASES, LE PONT

    var serveurBancs = new SqlConnectionStringBuilder(BaseJetable.ServeurDesBancs());
    string Connexion(string nom) => new SqlConnectionStringBuilder(serveurBancs.ConnectionString) { InitialCatalog = nom }.ConnectionString;
    var connexionGreffe = Connexion(cles["base"]);
    var greffe = cles["greffe"];
    var authentification = new SqlConnectionStringBuilder(serveurBancs.ConnectionString);
    authentification.Remove("Data Source");   // le serveur et la base viennent du catalogue de la base commune
    var erreursServeur = new CaptureErreurs();
    await using var app = ServeurRig.Construire(["--environment", "Development"], s =>
    {
        s.WebHost.UseTestServer();
        s.Logging.ClearProviders();
        s.Logging.AddProvider(erreursServeur);   // la cause d'un 500, rendue dans le tableau
        s.Logging.SetMinimumLevel(LogLevel.Error);
        s.Configuration["ConnectionStrings:RigV3"] = Connexion(cles["commune"]);
        s.Configuration[CatalogueGreffes.CleAuthentification] = authentification.ConnectionString;
    });
    await app.StartAsync();

    // Le client du pont, au nom d'un agent (en-tete X-RigV3-Compte, pose par ClientServeur depuis RIGV3_COMPTE).
    var clients = new Dictionary<string, ClientServeur>(StringComparer.OrdinalIgnoreCase);
    ClientServeur ClientDe(string? agent)
    {
        agent ??= cles["agent"];
        if (clients.TryGetValue(agent, out var c)) return c;
        Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, agent);
        try { c = new ClientServeur(new Uri("http://localhost/"), app.GetTestServer().CreateHandler()); }
        finally { Environment.SetEnvironmentVariable(ClientServeur.VariableCompte, null); }
        return clients[agent] = c;
    }
    var client = ClientDe(null);

    async Task<string> Valeur(string sql)
    {
        await using var cn = new SqlConnection(connexionGreffe);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        return Convert.ToString(await cmd.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
    async Task Sql(string sql)
    {
        await using var cn = new SqlConnection(connexionGreffe);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@aujourdhui", jour);
        await cmd.ExecuteNonQueryAsync();
    }
    Task<string> Journal() => Valeur("SELECT COUNT(*) FROM soc.Journal");

    // L'agent qui LIT les ecrans faits main : toutes les fonctions du greffe, Amitel et administrateur (en tete). Dans la base
    // du greffe et dans la base commune (les ecrans sans greffe, comme la supervision, jugent l'agent dans la commune).
    const string Lecteur = "parcours.lecteur";
    const string AgentLecteur = $"""
        IF NOT EXISTS (SELECT 1 FROM soc.Service) INSERT INTO soc.Service (CodeGreffe, Abrege, Nom) VALUES (@greffe, 'PARC', N'Parcours complet');
        IF NOT EXISTS (SELECT 1 FROM soc.Role WHERE Nom = 'PARCOURS') INSERT INTO soc.Role (Nom, Libelle) VALUES ('PARCOURS', 'Banc ParcoursComplet');
        INSERT INTO soc.RoleFonction (Role, Fonction) SELECT 'PARCOURS', Code FROM soc.Fonction
            WHERE Code NOT IN (SELECT Fonction FROM soc.RoleFonction WHERE Role = 'PARCOURS');
        INSERT INTO soc.Compte (CompteWindows, Nom, Prenom, Abrege, Civilite, CodeGreffe, IdService, Desactive, EstAmitel, EstAdministrateur)
        SELECT TOP 1 '{Lecteur}', 'PARCOURS', 'Lecteur', 'PARCLECT', 'M.', @greffe, Id, 0, 1, 1 FROM soc.Service ORDER BY Id;
        INSERT INTO soc.CompteRole (IdCompte, Role) SELECT Id, 'PARCOURS' FROM soc.Compte WHERE CompteWindows = '{Lecteur}';
        """;
    foreach (var baseLecteur in new[] { connexionGreffe, Connexion(cles["commune"]) })
    {
        await using var cn = new SqlConnection(baseLecteur);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(AgentLecteur, cn);
        cmd.Parameters.AddWithValue("@greffe", greffe);
        await cmd.ExecuteNonQueryAsync();
    }
    var lecteur = ClientDe(Lecteur);
    // La session de la fenetre, ouverte comme au lancement de RIG sur ce greffe : les ponts qui lisent SessionAgent.CodeGreffe
    // (EcranComptes...) voient le greffe des jeux.
    var session = await SessionAgent.PreparerAsync(() => lecteur, [$"greffe={greffe}"], greffe);
    Console.WriteLine($"Session de l'application : greffe {SessionAgent.CodeGreffe ?? "aucun"} ({session}).");

    // ============================================================ 3. CE QUE L'APPLICATION SAIT DE CHAQUE ECRAN

    var lecteurs = LecteursEcrans.Decouvrir(typeof(ServeurRig).Assembly);
    var ecrivains = EcrivainsEcrans.Decouvrir(typeof(ServeurRig).Assembly);
    var appli = typeof(MenuAccueil).Assembly;
    // Le ou les fichiers de Pont/ qui declarent une classe (une classe « partial » : tous) -- le fichier qu'un lot possede.
    var sources = Directory.GetFiles(Path.Combine(racine, "Pont"), "*.cs", SearchOption.AllDirectories)
        .Select(f => (Chemin: Path.GetRelativePath(racine, f).Replace('\\', '/'), Texte: File.ReadAllText(f))).ToArray();
    string FichiersDe(string classe)
        => sources.Where(x => Regex.IsMatch(x.Texte, $@"\bclass {classe}\b")).Select(x => x.Chemin).ToArray() is { Length: > 0 } f
            ? string.Join(" + ", f) : $"Pont/{classe}.cs (absent)";
    // Le fichier de Pont/EcransGeneres/ qui decrit chaque ecran genere (un domaine = un fichier, Descripteur.cs).
    var domaineDe = appli.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomaineEcrans).IsAssignableFrom(t))
        .SelectMany(t => ((IDomaineEcrans)Activator.CreateInstance(t)!).Ecrans().Select(e => (e.Code, t.Name)))
        .ToDictionary(x => x.Code, x => FichiersDe(x.Name));
    // Le composant React de chaque ecran fait main : le seul aiguillage de l'application (web/src/plan/Aiguillage.tsx).
    var composants = new Dictionary<string, string>(StringComparer.Ordinal);
    var enAttente = new List<string>();
    foreach (var l in File.ReadLines(Path.Combine(racine, "web", "src", "plan", "Aiguillage.tsx")))
    {
        if (Regex.Match(l, @"^\s*case '([A-Z0-9_]+)':") is { Success: true } cas) { enAttente.Add(cas.Groups[1].Value); continue; }
        if (Regex.Match(l, @"return\s+(?:[\w.]+\s*\?\s*)?<(Ecran\w+)") is { Success: true } rendu && enAttente.Count > 0)
        {
            foreach (var code in enAttente) composants[code] = rendu.Groups[1].Value;
            enAttente.Clear();
        }
    }
    var sondes = typeof(ISondeEcran).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISondeEcran).IsAssignableFrom(t))
        .Select(t => (ISondeEcran)Activator.CreateInstance(t)!)
        .ToDictionary(s => s.Code, StringComparer.Ordinal);
    var contexte = new ContexteSonde(client, greffe, jour, Valeur, Sql, Journal, ClientDe);
    foreach (var s in sondes.Values.Where(s => s.Jeu is not null)) await Sql(s.Jeu!);

    // ============================================================ 4. LE PARCOURS

    var vuesReunies = new Dictionary<string, VueLignesEcran>(StringComparer.Ordinal);
    var nullabilite = new NullabilityInfoContext();   // les parametres « string? » d'une lecture d'ecran fait main (LecturesDuPont)
    var parCode = new Dictionary<string, LigneParcours>(StringComparer.Ordinal);
    foreach (var moment in MenuAccueil.Moments)
    foreach (var sous in moment.Sous)
    foreach (var entree in sous.Entrees)
    {
        var chemin = string.Join(" › ", new[] { moment.Titre, sous.Titre, entree.Libelle }.Where(x => x.Length > 0));
        if (entree.Membres.Length == 0) { await Mesurer(entree.Code, chemin, null); continue; }
        foreach (var membre in entree.Membres) await Mesurer(membre.Code, $"{chemin} › {membre.Libelle}", entree);
    }

    async Task Mesurer(string code, string chemin, EntreeMenu? reunion)
    {
        // Un ecran range dans deux groupes du menu (c'est voulu, MenuAccueil.cs) n'est mesure qu'une fois : son geste ecrit.
        if (parCode.TryGetValue(code, out var deja)) { deja.Aussi.Add(chemin); return; }
        LigneParcours ligne;
        try
        {
            var d = CatalogueGenere.Descripteur(code);
            if (d is { Membres.Length: 0 }) ligne = await Genere(code, chemin, reunion, d);
            else if (composants.TryGetValue(code, out var composant)) ligne = await FaitMain(code, chemin, composant);
            else ligne = new LigneParcours(chemin, code, "web/src/plan/Aiguillage.tsx", "à venir (EcranNonMaquette)",
                false, "aucun écran : ni descripteur (Pont/EcransGeneres/), ni composant dans Aiguillage.tsx", false, "aucun écran");
        }
        catch (Exception e)
        {
            ligne = new LigneParcours(chemin, code, "?", "?", false, $"le banc a levé {e.GetType().Name} : {e.Message}", false, "non mesuré");
        }
        parCode[code] = ligne;
        lignes.Add(ligne);
    }

    // ---- Un ecran genere : ouvert par Pont/EcranGenere.cs, comme EcranGenere.tsx (l'ecran reuni s'il y en a un).
    async Task<LigneParcours> Genere(string code, string chemin, EntreeMenu? reunion, DescripteurEcran d)
    {
        var fichier = domaineDe.GetValueOrDefault(code, "Pont/EcransGeneres/?");
        var forme = d.Forme.ToString();
        // L'ecran que la fenetre ouvre : l'ecran reuni s'il a un descripteur (liste commune), sinon la procedure seule.
        var ecran = reunion is not null && CatalogueGenere.Descripteur(reunion.Code) is { Membres.Length: > 0 } ? reunion.Code : code;
        if (!vuesReunies.TryGetValue(ecran, out var vue)) vuesReunies[ecran] = vue = await EcranGenere.LignesAsync(client, greffe, ecran);
        // Seule, la procedure est en exemple quand la vue n'est pas « base » ; reunie, quand son titre est dans Exemples. Branchee
        // sans ligne : son titre est dans Vides (Pont/EcranGenere.cs, LignesAsync).
        var seule = ecran == code;
        var enExemple = seule ? vue.Mode != "base" : vue.Exemples.Contains(d.Titre);
        var lues = seule ? vue.Lignes.Length : vue.Lignes.Count(l => l.Groupe == d.Titre);
        var (donneesOk, donnees) = vue.Mode == "erreur" ? (false, $"erreur du serveur : {vue.Message}")
            : enExemple ? (false, $"exemple : aucun lecteur au serveur pour {code} (Serveur/Socle/EcransGeneres/)")
            : vue.Vides.Contains(d.Titre) ? (true, "base (0 ligne dans les jeux du banc)")
            : (true, $"base ({lues} ligne{(lues > 1 ? "s" : "")})");
        var (gesteOk, geste) = await GesteGenere(ecran, d);
        return new LigneParcours(chemin, code, fichier, forme, donneesOk, donnees, gesteOk, geste);
    }

    // Le geste d'un ecran genere, joue par le pont (Pont/EcranGenere.cs, GesteAsync) puis relu.
    async Task<(bool, string)> GesteGenere(string ecran, DescripteurEcran d)
    {
        var ecritAttendu = d.Forme is FormeEcran.Fiche or FormeEcran.Saisie or FormeEcran.Referentiel or FormeEcran.Operation
                                   or FormeEcran.File or FormeEcran.Audience || d.Gestes.Length > 0;
        if (!ecrivains.ParProcedure.ContainsKey(d.Code))
        {
            if (ecritAttendu)
                return (false, $"aucun écrivain au serveur : les gestes de l'écran ({Gestes(d)}) répondent « Démonstration »");
            return await LectureSeule(d);
        }
        var jeu = Path.Combine(dossierJeux, d.Code + ".gestes");
        if (!File.Exists(jeu)) return (false, $"un écrivain, mais aucun jeu {d.Code}.gestes : aucun geste prouvé");
        var bloc = Blocs.Lire(File.ReadAllLines(jeu)).FirstOrDefault(b => b.Resultat == "fait" && b.Geste is not null);
        if (bloc is null) return (false, $"{d.Code}.gestes n'a aucun bloc « resultat: fait » : aucun geste écrit n'est prouvé");
        var quoi = $"{bloc.Geste!.Type}{(bloc.Geste.Nom is { } nom ? " « " + nom + " »" : "")}";

        var avant = await Lignes(d.Code);
        var journalAvant = await Journal();
        var r = await EcranGenere.GesteAsync(ClientDe(bloc.Agent), greffe, ecran, d.Titre, bloc.Geste);
        if (!r.Fait) return (false, $"{quoi} joué par le pont : {(r.SansEcrivain ? "démonstration" : "refusé")} « {r.Message} »");
        var journalApres = await Journal();
        if (journalApres == journalAvant) return (false, $"{quoi} rendu « fait » sans trace au journal des gestes : rien d'écrit");

        var relus = new List<string>();
        var ecarts = new List<string>();
        if (bloc.Lignes is { } attendues)
        {
            var apres = await Lignes(d.Code);
            var voulues = attendues.Select(a => Blocs.Dates(a, jour)).ToArray();
            var obtenues = apres.Select((l, k) => k < voulues.Length && voulues[k].Contains(" | valeurs: ")
                ? l.Texte + " | valeurs: " + string.Join(" ; ", l.Valeurs) : l.Texte).ToArray();
            if (obtenues.SequenceEqual(voulues)) relus.Add($"les {voulues.Length} lignes du jeu");
            else ecarts.Add($"lignes ≠ jeu ({obtenues.Length} contre {voulues.Length} ; 1re différence : « {Premiere(obtenues, voulues)} »)");
        }
        if (bloc.Fiche is { } id)
        {
            var fiche = await EcranGenere.FicheAsync(client, greffe, d.Code, d.Titre, id);
            var champs = fiche.Fiche?.Sections.SelectMany(s => s.Champs.SelectMany(c => c.Lignes
                    .Select((l, i) => (Cle: $"{GesteEcran.Cle(s.Titre, c.Libelle)} › {i + 1}", Valeur: string.Join(" | ", l)))
                    .Prepend((Cle: GesteEcran.Cle(s.Titre, c.Libelle), c.Valeur))))
                .ToArray() ?? [];
            var faux = bloc.ValeursFiche.Where(v => champs.FirstOrDefault(c => c.Cle == v.Cle).Valeur != v.Valeur).ToArray();
            if (fiche.Mode == "base" && faux.Length == 0) relus.Add($"fiche {id}");
            else ecarts.Add($"fiche {id} relue ({fiche.Mode}) : {string.Join(", ", faux.Select(f => $"« {f.Cle} » ≠ « {f.Valeur} »"))}");
        }
        foreach (var (sql, attendu) in bloc.Controles)
        {
            var lu = await Valeur(sql);
            if (lu == attendu) relus.Add("contrôle SQL");
            else ecarts.Add($"contrôle « {sql} » = {lu}, attendu {attendu}");
        }
        // Relu : les lignes de l'ecran ont change, ou la fiche / un controle declares se relisent (en tete : le contenu exact est
        // celui d'EcransGeneresBaseCheck, dont l'ordre de jeu differe).
        var relues = await Lignes(d.Code);
        if (!relues.Select(l => l.Texte).SequenceEqual(avant.Select(l => l.Texte))) relus.Insert(0, "les lignes de l'écran ont changé");
        if (relus.Count > 0) return (true, $"{quoi} fait, relu ({string.Join(", ", relus.Distinct())})");
        return ecarts.Count > 0
            ? (false, $"{quoi} fait, mais rien de relu : lignes inchangées à l'écran, et {string.Join(" ; ", ecarts)}")
            : (false, $"{quoi} fait, mais rien de relu : le bloc ne déclare ni « lignes: », ni « fiche: », ni « controle: », et les lignes n'ont pas changé");
    }

    // Une forme sans geste d'ecriture : sa recherche (bloc 1 du .recherches) ou une fiche, lue par le pont.
    async Task<(bool, string)> LectureSeule(DescripteurEcran d)
    {
        var lecteur = lecteurs.ParProcedure.GetValueOrDefault(d.Code);
        var jeu = Path.Combine(dossierJeux, d.Code + ".recherches");
        if (lecteur is IRechercheEcran && File.Exists(jeu))
        {
            var bloc = Blocs.Lire(File.ReadAllLines(jeu)).First();
            var vue = await EcranGenere.LignesAsync(client, greffe, d.Code, new RechercheEcran(bloc.Texte, bloc.Criteres));
            var voulues = (bloc.Lignes ?? []).Select(a => Blocs.Dates(a, jour))
                .Select(a => a.Contains(" | valeurs: ") ? a[..a.IndexOf(" | valeurs: ", StringComparison.Ordinal)] : a).ToArray();
            var obtenues = vue.Lignes.Select(l => Blocs.Texte(l.Nom, l.Precisions, l.Groupe, l.Etat, l.Faites)).ToArray();
            return vue.Mode == "base" && obtenues.SequenceEqual(voulues)
                ? (true, $"lecture seule : recherche « {bloc.Texte} » jouée, {voulues.Length} ligne(s) relue(s)")
                : (false, $"lecture seule : recherche « {bloc.Texte} » relue fausse ({vue.Mode}, {obtenues.Length} contre {voulues.Length})");
        }
        if (lecteur is IFicheEcran)
        {
            var premiere = (await Lignes(d.Code)).FirstOrDefault(l => l.Id is not null);
            if (premiere is null) return (false, "lecture seule : aucune ligne avec un identifiant pour ouvrir une fiche");
            var fiche = await EcranGenere.FicheAsync(client, greffe, d.Code, d.Titre, premiere.Id);
            return fiche is { Mode: "base", Fiche: not null }
                ? (true, $"lecture seule : fiche {premiere.Id} ouverte et lue")
                : (false, $"lecture seule : la fiche {premiere.Id} ne se lit pas ({fiche.Mode} : {fiche.Message})");
        }
        return lecteur is null
            ? (false, "lecture seule, sans lecteur au serveur : rien à chercher ni à ouvrir")
            : (false, $"lecture seule : ni recherche prouvée ({d.Code}.recherches), ni fiche à ouvrir");
    }

    async Task<LigneLueParPont[]> Lignes(string procedure)
        => [.. (await EcranGenere.LignesAsync(client, greffe, procedure)).Lignes
               .Select(l => new LigneLueParPont(Blocs.Texte(l.Nom, l.Precisions, l.Groupe, l.Etat, l.Faites), l.Valeurs, l.Id))];

    // ---- Un ecran fait main : les lectures de son pont, puis sa sonde.
    async Task<LigneParcours> FaitMain(string code, string chemin, string composant)
    {
        var fichier = FichiersDe(composant);
        var forme = $"fait main ({composant})";
        var pont = appli.GetType($"RigV3Desktop.Pont.{composant}");
        (bool, string) donnees;
        if (sondes.TryGetValue(code, out var sondeDonnees) && sondeDonnees.DonneesAsync(contexte) is { } parSonde)
            donnees = await parSonde is var pd && pd.Fait ? (true, pd.Detail) : (false, $"sonde : {pd.Detail}");
        else if (pont is null) donnees = (false, $"aucun pont RigV3Desktop.Pont.{composant} : l'écran ne parle pas au C#");
        else donnees = await LecturesDuPont(pont);
        (bool, string) geste = sondes.TryGetValue(code, out var sonde)
            ? await sonde.GesteAsync(contexte) is var p && p.Fait && p.Relu ? (true, p.Detail) : (false, $"sonde : {p.Detail}")
            : (false, $"geste non prouvé : aucune sonde tests/ParcoursCompletCheck/Sondes/ pour {code}");
        return new LigneParcours(chemin, code, fichier, forme, donnees.Item1, donnees.Item2, geste.Item1, geste.Item2);
    }

    // Toute methode publique du pont qui LIT : (ClientServeur[, greffe]) puis seulement des parametres facultatifs ou qui
    // acceptent null (« sans affaire », « sans critere » : l'ecran ouvert depuis le menu), au nom qui ne dit pas une ecriture.
    // Son resultat porte un « Mode » : « serveur » (ou « base ») = les lignes viennent de la base.
    bool Facultatif(ParameterInfo p) => p.IsOptional || Nullable.GetUnderlyingType(p.ParameterType) is not null
        || (!p.ParameterType.IsValueType && nullabilite.Create(p).WriteState == NullabilityState.Nullable);
    async Task<(bool, string)> LecturesDuPont(Type pont)
    {
        var ecritures = new Regex("^(Modifier|Creer|Supprimer|Purger|Rendre|Traiter|Relancer|Enregistrer|Activer|Desactiver|Envoyer|Valider|Controler|Agir|Ouvrir$)");
        var lectures = pont.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !ecritures.IsMatch(m.Name))
            .Where(m => m.GetParameters() is { Length: > 0 } ps && ps[0].ParameterType == typeof(ClientServeur)
                        && ps.All(p => p.ParameterType != typeof(System.Text.Json.JsonElement)))
            .ToArray();
        if (lectures.Length == 0)
        {
            // Un pont qui ne parle au serveur QUE par ses gestes (EcranRequisitions.AgirAsync : la recherche est un geste de la
            // session) : ses donnees se prouvent par sa sonde (DonneesAsync), le banc ne les devine pas.
            var parGestes = pont.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetParameters() is { Length: > 0 } ps && ps[0].ParameterType == typeof(ClientServeur)).Select(m => m.Name).ToArray();
            return (false, parGestes.Length > 0
                ? $"non mesuré : {pont.Name} n'appelle le serveur que par ses gestes ({string.Join(", ", parGestes.Distinct())}) — à prouver par sa sonde"
                : $"exemple : {pont.Name} n'a aucune lecture qui appelle le serveur (rien de la base)");
        }
        var vus = new List<string>();
        foreach (var m in lectures)
        {
            var ps = m.GetParameters();
            object? resultat;
            erreursServeur.Derniere = null;
            try
            {
                var args = ps.Select((p, i) => i == 0 ? lecteur : i == 1 && p.ParameterType == typeof(string) ? greffe
                                               : p.IsOptional ? p.DefaultValue : Facultatif(p) ? null : Defaut(p.ParameterType, p.Name, 0)).ToArray();
                resultat = m.Invoke(null, args);
                if (resultat is Task t)
                {
                    if (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60))) != t) { vus.Add($"{m.Name} : pas de réponse en 60 s"); continue; }
                    await t;
                    resultat = t.GetType().GetProperty("Result")?.GetValue(t);
                }
            }
            catch (Exception e)
            {
                var cause = e is TargetInvocationException { InnerException: { } i } ? i : e;
                vus.Add($"{m.Name} lève {cause.GetType().Name} : {cause.Message}");
                continue;
            }
            var mode = resultat?.GetType().GetProperty("Mode")?.GetValue(resultat) as string;
            var message = resultat?.GetType().GetProperty("Message")?.GetValue(resultat) as string;
            if (mode is "serveur" or "base") return (true, $"base (pont {pont.Name}.{m.Name})");
            var dite = erreursServeur.Derniere is { } ex ? $" [serveur : {ex}]" : "";
            vus.Add(mode is null ? $"{m.Name} : sa vue n'a pas de « Mode », base ou exemple non mesurable"
                                 : $"{m.Name} : {mode}{(message is null ? "" : " — " + message)}{dite}");
        }
        return (false, $"exemple : {string.Join(" ; ", vus)}");
    }
}
finally
{
    // Rendre les bases : EcransGeneresBaseCheck s'arrete (code 3) et les supprime.
    try { File.Delete(garde); } catch (IOException) { }
    if (!bancJeux.WaitForExit(120_000)) bancJeux.Kill();
}

// ============================================================ 5. LE TABLEAU

// Une valeur « vide » pour un parametre obligatoire d'une lecture : l'ecran ouvert depuis le menu, sans critere ni affaire.
// Une date (« du », « debut ») : le 1er janvier ; une fin (« au », « fin ») : aujourd'hui.
static object? Defaut(Type t, string? nom, int profondeur)
{
    var n = (nom ?? "").ToLowerInvariant();
    if (t == typeof(string))
        return n is "du" or "debut" or "datedebut" ? $"{DateTime.Today.Year}-01-01"
             : n is "au" or "fin" or "datefin" ? DateTime.Today.ToString("yyyy-MM-dd") : "";
    if (t == typeof(DateTime)) return DateTime.Today;
    if (t == typeof(DateOnly)) return DateOnly.FromDateTime(DateTime.Today);
    if (t.IsArray) return Array.CreateInstance(t.GetElementType()!, 0);
    if (t.IsValueType) return Activator.CreateInstance(t);
    if (t.IsInterface || t.IsAbstract || profondeur > 3 || typeof(Delegate).IsAssignableFrom(t)) return null;
    var ctor = t.GetConstructors().OrderBy(c => c.GetParameters().Length).FirstOrDefault();
    if (ctor is null) return null;
    return ctor.Invoke([.. ctor.GetParameters().Select(p => p.IsOptional ? p.DefaultValue : Defaut(p.ParameterType, p.Name, profondeur + 1))]);
}

// Les gestes qu'un ecran genere montre : ceux que sa fiche nomme, sinon ceux de sa forme.
static string Gestes(DescripteurEcran d) => d.Gestes.Length > 0 ? string.Join(", ", d.Gestes) : $"ceux de la forme {d.Forme}";

static string Premiere(string[] obtenues, string[] voulues)
{
    for (var k = 0; k < Math.Max(obtenues.Length, voulues.Length); k++)
    {
        var o = k < obtenues.Length ? obtenues[k] : "(aucune)";
        var v = k < voulues.Length ? voulues[k] : "(aucune)";
        if (o != v) return $"{o} » au lieu de « {v}";
    }
    return "";
}

bool Retenue(LigneParcours l) => filtre.Length == 0
    || filtre.Any(f => string.Equals(f, l.Code, StringComparison.Ordinal) || l.Fichier.Contains(f, StringComparison.OrdinalIgnoreCase));

foreach (var l in lignes)
    Console.WriteLine($"{(l.Verte ? "OK   " : "ECHEC")} : {l.Code} [{l.Fichier}] {l.Forme} — données : {l.Donnees} — geste : {l.Geste}");

var md = new StringBuilder();
md.AppendLine($"# Parcours complet du RIG v3 — {DateTime.Now:dd/MM/yyyy HH:mm}");
md.AppendLine();
md.AppendLine($"{lignes.Count(l => l.Verte)} lignes vertes sur {lignes.Count} (verte = données de la base + geste joué puis relu).");
md.AppendLine();
md.AppendLine("| Verdict | Entrée du menu | Code | Pont | Forme | Données | Geste |");
md.AppendLine("|---|---|---|---|---|---|---|");
string Cellule(string s) => s.Replace("|", "\\|").Replace("\n", " ");
foreach (var l in lignes)
    md.AppendLine($"| {(l.Verte ? "VERT" : "ROUGE")} | {Cellule(l.Chemin + (l.Aussi.Count > 0 ? " (aussi : " + string.Join(" ; ", l.Aussi) + ")" : ""))} | {l.Code} | {l.Fichier} " +
                  $"| {Cellule(l.Forme)} | {(l.DonneesOk ? "✔ " : "✘ ")}{Cellule(l.Donnees)} | {(l.GesteOk ? "✔ " : "✘ ")}{Cellule(l.Geste)} |");
md.AppendLine();
md.AppendLine("## Rouges par fichier de pont");
md.AppendLine();
foreach (var g in lignes.Where(l => !l.Verte).GroupBy(l => l.Fichier).OrderByDescending(g => g.Count()))
    md.AppendLine($"- {g.Key} : {g.Count()} — {string.Join(", ", g.Select(l => l.Code))}");
var tableau = Path.Combine(AppContext.BaseDirectory, "parcours-complet.md");
File.WriteAllText(tableau, md.ToString());
if (Environment.GetEnvironmentVariable("RIGV3_PARCOURS_TABLEAU") is { Length: > 0 } copie) File.WriteAllText(copie, md.ToString());

var retenues = lignes.Where(Retenue).ToArray();
Console.WriteLine();
Console.WriteLine($"Tableau : {tableau}");
foreach (var g in lignes.Where(l => !l.Verte).GroupBy(l => l.Fichier).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  rouges {g.Key} : {g.Count()}");
Console.WriteLine($"Parcours : {retenues.Count(l => l.Verte)}/{retenues.Length} lignes vertes{(filtre.Length > 0 ? $" (RIGV3_PARCOURS = {string.Join(",", filtre)})" : "")}, " +
                  $"{lignes.Count(l => l.Verte)}/{lignes.Count} au total.");
return retenues.Length > 0 && retenues.All(l => l.Verte) ? 0 : 1;

static async Task<(int Code, string Sortie)> Executer(string programme, string[] arguments)
{
    var info = new ProcessStartInfo(programme) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!;
    var sortie = p.StandardOutput.ReadToEndAsync();
    var erreurs = p.StandardError.ReadToEndAsync();
    await p.WaitForExitAsync();
    return (p.ExitCode, await sortie + await erreurs);
}

/// <summary>Une ligne du parcours : une entree du menu (ou une procedure d'un ecran reuni) et ce que le banc en a mesure.</summary>
sealed record LigneParcours(string Chemin, string Code, string Fichier, string Forme, bool DonneesOk, string Donnees, bool GesteOk, string Geste)
{
    public bool Verte => DonneesOk && GesteOk;
    /// <summary>Les autres places de cet ecran dans le menu (un ecran range dans deux groupes, MenuAccueil.cs).</summary>
    public List<string> Aussi { get; } = [];
}

/// <summary>La derniere exception que le serveur en memoire a journalisee (un 500 et sa cause : « Nom d'objet non valide »).</summary>
sealed class CaptureErreurs : ILoggerProvider, ILogger
{
    public string? Derniere { get; set; }
    public ILogger CreateLogger(string categorie) => this;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Error;
    public void Log<TState>(LogLevel niveau, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> format)
    {
        if (exception is null || niveau < LogLevel.Error) return;
        var racine = exception;
        while (racine.InnerException is not null) racine = racine.InnerException;
        Derniere = $"{racine.GetType().Name} : {racine.Message.Split('\n')[0].Trim()}";
    }
    public void Dispose() { }
}

/// <summary>Une ligne lue par le pont, au format des jeux (Blocs.Texte), ses cellules et son identifiant.</summary>
sealed record LigneLueParPont(string Texte, string[] Valeurs, string? Id);
