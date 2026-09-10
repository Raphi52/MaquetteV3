using System.Text.RegularExpressions;
using RigV3Desktop;

// Verifie que le widget performance affiche l ACTIVITE DES DOSSIERS
// (traites, en cours, hors delai...) et non la sante technique du process,
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

// --- 2. Les mesures portent bien sur les DOSSIERS, pas sur le process
var libelles = a.Select(m => m.Libelle).ToArray();
Exiger(libelles.Contains("Dossiers traites"), "la premiere mesure est le nombre de dossiers traites");
Exiger(!libelles.Any(l => l.Contains("Thread") || l.Contains("Memoire") || l.Contains("pool")),
    "aucune mesure technique du process ne subsiste");

var attenduTraites = JeuDemo.Demandes.Count(d => d.Etat == "Validee");
var attenduEnCours = JeuDemo.Demandes.Count(d => d.EnCours);
Exiger(a.First(m => m.Libelle == "Dossiers traites").Valeur
        == $"{attenduTraites} / {JeuDemo.Demandes.Length}",
    "le compte des dossiers traites est CALCULE sur les dossiers connus");
Exiger(a.First(m => m.Libelle == "Dossiers en cours").Valeur == attenduEnCours.ToString("N0"),
    "le compte des dossiers en cours est calcule, pas ecrit en dur");
Exiger(a.First(m => m.Libelle == "Hors delai legal").Valeur != "0"
        || !JeuDemo.Demandes.Any(d => d.EnCours && (JeuDemo.Aujourdhui - d.Creation2).TotalDays > 7),
    "le hors-delai suit le delai legal de 7 jours");

// Deux releves sur les memes dossiers doivent donner le MEME resultat :
// une mesure d activite ne clignote pas.
var b = sonde.Relever();
Exiger(a.Zip(b).All(c => c.First.Valeur == c.Second.Valeur),
    "deux releves consecutifs donnent le meme etat des dossiers");

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
