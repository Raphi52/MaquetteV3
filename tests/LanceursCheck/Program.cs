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
