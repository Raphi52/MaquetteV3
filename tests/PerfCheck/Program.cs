using System.Text.RegularExpressions;
using RigV3Desktop;

// Verifie que le widget performance affiche des mesures VIVANTES,
// et que la vue est bien branchee sur la sonde (pas de valeurs en dur).
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// --- 1. La sonde rend six mesures completes
var sonde = new MesuresPerf();
var a = sonde.Relever();
Exiger(a.Length == 6, "la sonde rend 6 mesures");
Exiger(a.All(m => !string.IsNullOrWhiteSpace(m.Valeur) && !string.IsNullOrWhiteSpace(m.Libelle)),
    "aucune mesure vide");
Exiger(a.All(m => m.Part >= 0 && m.Part <= 100), "chaque jauge reste entre 0 et 100 %");
Exiger(a.All(m => m.Statut is "ok" or "attente" or "action" or "alerte"),
    "chaque statut correspond a un degrade existant");

// --- 2. Les valeurs EVOLUENT d'un releve a l'autre (le defaut corrige)
var travail = 0;
for (var i = 0; i < 200; i++)
{
    ThreadPool.QueueUserWorkItem(_ => Interlocked.Increment(ref travail));
}
Thread.Sleep(120);
var b = sonde.Relever();
var change = a.Zip(b).Count(c => c.First.Valeur != c.Second.Valeur);
Console.WriteLine($"       valeurs modifiees entre deux releves : {change}/6");
Exiger(change > 0, "au moins une valeur change entre deux releves");

// --- 3. La vue est branchee sur la sonde, sans tableau ecrit en dur
var vue = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Components/Pages/Home.razor"));
Exiger(Regex.IsMatch(vue, @"MesuresPerf\s+Sonde\s*=\s*new"), "Home.razor instancie la sonde");
Exiger(Regex.IsMatch(vue, @"MinuteurPerf\s*=\s*new Timer"), "Home.razor relance le releve par minuteur");
Exiger(vue.Contains("RelevePerf();"), "Home.razor appelle le releve");
Exiger(!Regex.IsMatch(vue, @"new\(""Threads actifs"""), "plus de mesures ecrites en dur dans la vue");
Exiger(Regex.IsMatch(vue, @"MinuteurPerf.*DisposeAsync", RegexOptions.Singleline), "le minuteur est arrete a la fermeture");

Console.WriteLine();
if (echecs.Count > 0)
{
    Console.WriteLine($"{echecs.Count} controle(s) en echec.");
    return 1;
}
Console.WriteLine("Tous les controles passent.");
return 0;
