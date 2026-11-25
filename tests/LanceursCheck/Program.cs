using System.Diagnostics;

// ============================================================
// LES LANCEURS SUR LES DONNEES DE DEVELOPPEMENT (2026-11-24, conv-2122 ; docs/reconstruction/architecture.md,
// « Travailler sur les données de développement »).
//  1. dev.ps1 et lancer.ps1 preparent les donnees de dev PAR DEFAUT, -Demo les retire (variables RIGV3_* comprises).
//  2. Ils le font AVANT de fermer l'instance ouverte : Docker arrete, l'utilisateur garde sa fenetre.
//  3. dev.ps1 lance comme l'utilisateur, Docker injoignable (contexte docker absent, pose pour CE processus seulement) :
//     code 1, la cause dite (« demarrer Docker Desktop »), le recours (-Demo), et rien de lance (ni Vite ni l'app).
// Contre-epreuves faites le 2026-11-24 : ordre d'avant remis (fermeture puis donnees) -> rouge ; appel de docker en
// 'Stop' remis dans donnees-dev.ps1 -> rouge (le texte brut de docker a la place du message).
// ============================================================

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

const string AppelDonnees = "'donnees-dev.ps1')";
const string Fermeture = "Get-Process -Name 'RigV3Desktop'";
foreach (var script in new[] { "dev.ps1", "lancer.ps1" })
{
    var texte = File.ReadAllText(Path.Combine(racine, script));
    var appel = texte.IndexOf(AppelDonnees, StringComparison.Ordinal);
    var fermeture = texte.IndexOf(Fermeture, StringComparison.Ordinal);
    Exiger(texte.Contains("param([switch]$Demo)") && texte.Contains("if (-not $Demo)") && appel > 0
           && texte.Contains("Remove-Item Env:RIGV3_SERVEUR, Env:RIGV3_COMPTE, Env:RIGV3_GREFFES"),
        $"{script} : les donnees de dev par defaut (donnees-dev.ps1 sauf -Demo), -Demo retire RIGV3_SERVEUR, RIGV3_COMPTE et RIGV3_GREFFES");
    Exiger(appel > 0 && fermeture > 0 && appel < fermeture,
        $"{script} : les donnees sont preparees AVANT de fermer l'instance ouverte (appel au caractere {appel}, fermeture au {fermeture}) -- " +
        "Docker arrete, l'utilisateur garde sa fenetre");
}

// Le contexte docker absent doit vraiment rendre docker injoignable : sinon dev.ps1 irait jusqu'au bout (serveur,
// fermeture de l'instance, Vite). Le banc s'arrete alors sans le lancer.
const string ContexteAbsent = "rigv3-banc-lanceurs-absent";
var (codeDocker, _) = await LancerAsync("docker", ["ps"], TimeSpan.FromSeconds(30));
var (codeAbsent, _) = await LancerAsync("docker", ["--context", ContexteAbsent, "ps"], TimeSpan.FromSeconds(30));
Exiger(codeAbsent != 0, $"docker --context {ContexteAbsent} ps echoue (code {codeAbsent} ; docker du poste : code {codeDocker}) : dev.ps1 peut etre lance sans risque");
if (codeAbsent != 0)
{
    var (code, sortie) = await LancerAsync("powershell.exe",
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(racine, "dev.ps1")], TimeSpan.FromSeconds(120),
        new() { ["DOCKER_CONTEXT"] = ContexteAbsent });
    Exiger(code == 1 && sortie.Contains("Donnees de developpement indisponibles") && sortie.Contains("demarrer Docker Desktop")
           && sortie.Contains(@".\dev.ps1 -Demo") && !sortie.Contains("Vite") && !sortie.Contains("mode developpement"),
        $"dev.ps1 sans option, Docker injoignable : code {code}, la cause et le recours dits, ni Vite ni l'app lances -- " +
        $"« {string.Join(" / ", sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4))} »");
}

// 4. Le serveur de dev suit le code (2026-11-25, conv-2155). Les deux fonctions sont lues DANS donnees-dev.ps1 (arbre
// syntaxique de PowerShell, rien d'autre du script ne tourne : ni Docker, ni le serveur partage du port 5080), puis
// jouees sur un depot git jetable dont chaque commit ne touche qu'un dossier.
var jetable = Path.Combine(Path.GetTempPath(), "rigv3-banc-lanceurs-" + Guid.NewGuid().ToString("N")[..8]);
var depot = Path.Combine(jetable, "depot");
try
{
    Directory.CreateDirectory(depot);
    var harnais = Path.Combine(jetable, "harnais.ps1");
    File.WriteAllText(harnais, """
        param([string]$Script, [string]$Depot, [string]$Versions)
        $arbre = [System.Management.Automation.Language.Parser]::ParseFile($Script, [ref]$null, [ref]$null)
        foreach ($f in $arbre.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true)) {
            if ($f.Name -in 'Get-SourcesServeur', 'Test-CodeServeur') { . ([scriptblock]::Create($f.Extent.Text)) }
        }
        foreach ($v in $Versions -split ';') { "$v=$((Test-CodeServeur $v $Depot).etat)" }
        """);
    // Serveur -> Contrats -> Base : la reference de Contrats vers Base n'est vue qu'en suivant les projets de proche en proche.
    void Poser(string chemin, string texte) { var f = Path.Combine(depot, chemin); Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, texte); }
    Poser("Serveur/RigV3.Serveur.csproj", """<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><ProjectReference Include="../Contrats/RigV3.Contrats.csproj" /></ItemGroup></Project>""");
    Poser("Contrats/RigV3.Contrats.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="..\Base\RigV3.Migrations.csproj" /></ItemGroup></Project>""");
    Poser("Base/RigV3.Migrations.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
    foreach (var f in new[] { "Serveur/Module.cs", "Contrats/Contrat.cs", "Base/migrations/m.sql", "web/src/Ecran.tsx", "Pont/Ecran.cs", "Directory.Build.props" }) Poser(f, "v1");
    async Task<string> Commit(string message)
    {
        await LancerAsync("git", ["-C", depot, "add", "-A"], TimeSpan.FromSeconds(30));
        await LancerAsync("git", ["-C", depot, "-c", "user.name=banc", "-c", "user.email=banc@rigv3.invalid", "commit", "-q", "-m", message], TimeSpan.FromSeconds(30));
        return await Tete();
    }
    async Task<string> Tete() => (await LancerAsync("git", ["-C", depot, "rev-parse", "HEAD"], TimeSpan.FromSeconds(30))).Sortie;
    async Task<Dictionary<string, string>> Etats(IEnumerable<string> versions, Dictionary<string, string>? env = null)
    {
        var (code, sortie) = await LancerAsync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", harnais,
            "-Script", Path.Combine(racine, "donnees-dev.ps1"), "-Depot", depot, "-Versions", string.Join(";", versions)], TimeSpan.FromSeconds(60), env);
        var etats = sortie.Split('\n', StringSplitOptions.TrimEntries).Where(l => l.Contains('=') && l.StartsWith("2026"))
            .Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        if (etats.Count == 0) Console.WriteLine($"      (harnais : code {code}, « {sortie.Split('\n').FirstOrDefault()?.Trim()} »)");
        return etats;
    }
    void Constater(Dictionary<string, string> etats, string version, string attendu, string quoi)
    {
        var lu = etats.GetValueOrDefault(version, "absent");
        Exiger(lu == attendu, $"donnees-dev.ps1, {quoi} : « {attendu} » (lu : {lu})");
    }

    await LancerAsync("git", ["-C", depot, "init", "-q"], TimeSpan.FromSeconds(30));
    var depart = await Commit("depart");
    var v = (string commit) => "2026.1.0+" + commit;

    // L'entree qui fait tomber une comparaison sur TOUT le depot : un commit qui ne touche que web\ et Pont\.
    Poser("web/src/Ecran.tsx", "v2"); Poser("Pont/Ecran.cs", "v2");
    var ecran = await Commit("ecran seul");
    var e = await Etats([v(ecran), v(depart)]);
    Constater(e, v(ecran), "a-jour", "serveur construit sur le commit en tete");
    Constater(e, v(depart), "a-jour", "seuls web\\ (React) et Pont\\ (l'ecran) ont change depuis le serveur : il n'est pas relance");

    // Chaque projet du serveur, seul : Serveur, Contrats (reference directe), Base (reference de Contrats), Directory.Build.props.
    // L'entree qui fait tomber une comparaison sur Serveur\ seul : le commit « Contrats ».
    foreach (var (fichier, quoi) in new[] {
        ("Serveur/Module.cs", "Serveur\\ a change"),
        ("Contrats/Contrat.cs", "Contrats\\ (projet reference par le serveur) a change"),
        ("Base/migrations/m.sql", "Base\\ (reference par Contrats, vu de proche en proche) a change"),
        ("Directory.Build.props", "Directory.Build.props (lu par MSBuild pour tous les projets) a change") })
    {
        var avant = await Tete();
        Poser(fichier, "v3-" + Guid.NewGuid());
        await Commit(fichier);
        Constater(await Etats([v(avant)]), v(avant), "perime", quoi + " depuis le serveur : il est relance");
    }

    // La version illisible garde le serveur tel quel (avertissement) : jumeau du « a-jour » ci-dessus, meme depot.
    var tete = await Tete();
    e = await Etats(["2026.1.0", "2026.1.0+0123456789abcdef0123456789abcdef01234567", v(tete)]);
    Constater(e, "2026.1.0", "illisible", "version sans « + » ni commit");
    Constater(e, "2026.1.0+0123456789abcdef0123456789abcdef01234567", "illisible", "commit inconnu du depot");
    Constater(e, v(tete), "a-jour", "commit en tete (jumeau des deux refus ci-dessus)");
    var sansGit = string.Join(";", Environment.SystemDirectory, Path.GetDirectoryName(Environment.SystemDirectory)!,
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0"));
    Constater(await Etats([v(tete)], new() { ["PATH"] = sansGit }), v(tete), "illisible", "git absent du PATH, commit en tete");
}
finally
{
    // Les objets d'un depot git sont en lecture seule sous Windows : Directory.Delete les refuse sans ce retrait.
    if (Directory.Exists(jetable))
    {
        foreach (var f in Directory.EnumerateFiles(jetable, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(jetable, recursive: true);
    }
}

// Reconstruire D'ABORD dans un dossier neuf (la dll du serveur en cours est verrouillee), arreter l'ancien ENSUITE,
// demarrer le neuf enfin : la coupure ne dure que le demarrage.
var texteDonnees = File.ReadAllText(Path.Combine(racine, "donnees-dev.ps1"));
var debutRelance = texteDonnees.IndexOf("function Start-ServeurDev", StringComparison.Ordinal);
var relance = debutRelance < 0 ? "" : texteDonnees[debutRelance..texteDonnees.IndexOf("\n}", debutRelance, StringComparison.Ordinal)];
var (construction, arret, demarrage) = (relance.IndexOf("& dotnet build", StringComparison.Ordinal),
    relance.IndexOf("Stop-ServeurDev", StringComparison.Ordinal), relance.IndexOf("Start-Process", StringComparison.Ordinal));
Exiger(construction > 0 && arret > construction && demarrage > arret,
    $"donnees-dev.ps1, Start-ServeurDev : construction dans un dossier neuf (caractere {construction}), PUIS arret de l'ancien ({arret}), PUIS demarrage ({demarrage})");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "LanceursCheck : VERT" : $"LanceursCheck : {echecs.Count} ECHEC(S)");
return echecs.Count == 0 ? 0 : 1;

static async Task<(int Code, string Sortie)> LancerAsync(string exe, string[] arguments, TimeSpan delai, Dictionary<string, string>? env = null)
{
    var d = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var a in arguments) d.ArgumentList.Add(a);
    foreach (var (k, v) in env ?? []) d.Environment[k] = v;
    using var p = Process.Start(d)!;
    var (sortie, erreur) = (p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
    using var fin = new CancellationTokenSource(delai);
    try { await p.WaitForExitAsync(fin.Token); }
    catch (OperationCanceledException) { p.Kill(entireProcessTree: true); return (-1, $"delai de {delai.TotalSeconds} s depasse"); }
    return (p.ExitCode, (await sortie + await erreur).Trim());
}
