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

// --- 1 bis. Chaque mesure porte son OBJECTIF, et la couleur suit l ECART a
// cet objectif -- pas la hauteur de la jauge.
Exiger(a.All(m => m.Objectif >= 0 && m.Objectif <= 100),
    "le repere d objectif reste sur l echelle de la jauge");
Exiger(a.All(m => !string.IsNullOrWhiteSpace(m.ObjectifTexte) && m.ObjectifTexte.Contains("obj.")),
    "chaque mesure annonce sa cible en clair");
Exiger(a.All(m => m.Atteinte >= 0), "l atteinte de l objectif est un pourcentage positif");
Exiger(a.All(m => (m.Atteinte >= 100) == (m.Statut == "ok")),
    "le vert est donne quand l objectif est tenu, et seulement alors");
// Le hors-delai a pour cible zero : une jauge haute y est MAUVAISE, une jauge
// a zero y est verte. C est ce renversement que le statut doit porter.
var hd = a.First(m => m.Libelle == "Hors delai legal");
Exiger(hd.Objectif == 0, "l objectif du hors-delai est zero");
Exiger((hd.Valeur == "0") == (hd.Statut == "ok"),
    "le hors-delai n est vert que s il est nul");

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

// --- 3. La vue est branchee sur la sonde, sans tableau ecrit en dur.
// PORTE EN REACT (lot 1 du plan « finir le passage a React », 19/11/2026) : la
// sonde et son minuteur vivent dans la session de l'accueil (Pont/Accueil.cs),
// la tuile est dessinee par web/src/accueil/Tuiles.tsx (fonction Performance) ;
// Home.razor demarre et arrete la session, et la fait relire apres une tache.
var racinePerf = Path.Combine(AppContext.BaseDirectory, "../../../../../");
// Lot 2 du passage a React (19/11/2026) : le @code de Home.razor vit dans
// Pont/PlanTravail.cs ; sa session demarre et arrete celle de l'accueil.
var vue = File.ReadAllText(Path.Combine(racinePerf, "Pont/PlanTravail.cs"));
var pont = File.ReadAllText(Path.Combine(racinePerf, "Pont/Accueil.cs"));
var tuiles = File.ReadAllText(Path.Combine(racinePerf, "web/src/accueil/Tuiles.tsx"));
Exiger(Regex.IsMatch(pont, @"MesuresPerf\s+Sonde\s*=\s*new"), "la session de l'accueil instancie la sonde");
Exiger(Regex.IsMatch(pont, @"MinuteurPerf\s*\?\?=\s*new Timer"), "la session relance le releve par minuteur");
Exiger(Regex.IsMatch(pont, @"new Timer\(_ =>\s*\{\s*RelevePerf\(\);\s*Change\?\.Invoke\(\);"),
    "chaque releve du minuteur fait redessiner l'accueil");
Exiger(Regex.IsMatch(vue, @"public void Demarrer\(\)[\s\S]{0,700}?Accueil\.Demarrer\(\);") && vue.Contains("private void SurChangeAccueil() => Change?.Invoke();")
       && vue.Contains("Accueil.Change += SurChangeAccueil;"),
    "le plan de travail demarre les minuteurs au premier rendu et redessine a chaque releve");
Exiger(pont.Contains("RelevePerf();") && vue.Contains("Accueil.RelevePerf();"), "la session et le plan de travail appellent le releve");
Exiger(!Regex.IsMatch(vue + pont + tuiles, @"new\(""Threads actifs""|'Threads actifs'|""Threads actifs"""),
    "plus de mesures ecrites en dur dans la vue");
Exiger(Regex.IsMatch(pont, @"MinuteurPerf.*DisposeAsync", RegexOptions.Singleline)
       && vue.Contains("await Accueil.DisposeAsync();"), "le minuteur est arrete a la fermeture");
Exiger(tuiles.Contains("className=\"rig-perf__repere\" style={{ left: m.objectif + '%' }}"),
    "la vue dessine le repere d objectif sur la jauge");
Exiger(tuiles.Contains("{m.objectifTexte}") && pont.Contains("m.ObjectifTexte"),
    "la vue affiche la cible chiffree a cote de la valeur");
var styles = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../wwwroot/rig-accueil.css"));
Exiger(styles.Contains(".rig-perf__repere"), "le repere d objectif a bien un style");

Console.WriteLine();
if (echecs.Count > 0)
{
    Console.WriteLine($"{echecs.Count} controle(s) en echec.");
    return 1;
}
Console.WriteLine("Tous les controles passent.");
return 0;
