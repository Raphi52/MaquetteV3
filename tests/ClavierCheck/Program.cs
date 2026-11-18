using System.Text.RegularExpressions;

// Garde-fou du CLAVIER (16/09/2026).
//
// Constat mesure avant ce banc : dans tout Components/, wwwroot/ et Main.razor,
// DEUX occurrences de clavier en tout (un autofocus et un onkeydown dans
// l'arbre GED). Aucun ecouteur keydown au niveau du document, aucun @onkeydown
// dans les 35 ecrans. RIG v2 est une application WinForms conduite au clavier :
// a la souris seule, la maquette ne ressemble pas au metier.
//
// Premier pas borne, celui que ce banc exige :
//   - Echap ferme l'ecran ouvert (branche depuis le document) ;
//   - Entree lance la recherche sur les ecrans qui ONT une action Rechercher.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// --- 1. L'ecouteur clavier existe vraiment cote navigateur.
var js = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-accueil.js"));
// fix-ok: le regex acceptait tout suffixe (XXaddEventListener matchait) ; ancre sur document.addEventListener
Exiger(Regex.IsMatch(js, @"document\.addEventListener\(\s*""keydown"""),
    "rig-accueil.js branche un ecouteur keydown");
Exiger(Regex.IsMatch(js, @"export function activerClavier\("),
    "rig-accueil.js expose activerClavier(dotnet) pour le brancher");
Exiger(js.Contains("Escape"),
    "l'ecouteur reconnait la touche Echap");
Exiger(Regex.IsMatch(js, @"export function arreterClavier\(") && js.Contains(@"removeEventListener(""keydown"""),
    "l'ecouteur clavier est debranche quand la page s'en va");

// --- 2. Echap arrive jusqu'a C#, et ferme ce qui est ouvert.
var home = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
Exiger(Regex.IsMatch(home, @"\[JSInvokable\][^\}]*ToucheEchap", RegexOptions.Singleline),
    "Home.razor expose ToucheEchap au navigateur");
var corps = Regex.Match(home, @"public void ToucheEchap\(\)(?<c>.*?)\n    \}", RegexOptions.Singleline);
Exiger(corps.Success && corps.Groups["c"].Value.Contains("FermerProc"),
    "Echap ferme l'ecran de procedure ouvert");
Exiger(corps.Success && corps.Groups["c"].Value.Contains("FermerDossier"),
    "Echap ferme l'affaire ouverte");
Exiger(home.Contains(@"InvokeVoidAsync(""activerClavier"""),
    "Home branche l'ecouteur clavier au premier rendu");

// --- 3. Entree lance la recherche partout ou une recherche existe.
var ecrans = Directory.GetFiles(Path.Combine(racine, "Components/Ecrans"), "*.razor");
var recherches = ecrans.Where(f => File.ReadAllText(f).Contains(@"@onclick=""Rechercher""")).ToList();
Exiger(recherches.Count >= 2, $"les ecrans de recherche sont trouves ({recherches.Count})");
foreach (var f in recherches)
{
    var t = File.ReadAllText(f);
    var nom = Path.GetFileName(f);
    Exiger(t.Contains("@onkeydown"), $"{nom} ecoute le clavier");
    Exiger(Regex.IsMatch(t, @"Enter""\s*\)\s*Rechercher\(\)|e\.Key == ""Enter"""),
        $"{nom} lance la recherche sur Entree");
}

// --- 3 bis. Meme regle sur les ecrans PORTES EN REACT (web/src, 18/11/2026) :
// un ecran dont un bouton lance rechercher() ecoute le clavier (onKeyDown) et
// lance la recherche sur Entree. Un ecran de recherche porte reste donc compte
// ici, et le seuil ci-dessus se lit sur les DEUX familles.
var racineWeb = Path.Combine(racine, "web/src/ecrans");
var ecransTsx = Directory.Exists(racineWeb)
    ? Directory.GetFiles(racineWeb, "*.tsx").Where(f => !f.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase)).ToList()
    : [];
var recherchesTsx = ecransTsx
    .Where(f => Regex.IsMatch(File.ReadAllText(f), @"onClick=\{\s*(?:\(\)\s*=>\s*)?rechercher(?:\(\))?\s*\}"))
    .ToList();
foreach (var f in recherchesTsx)
{
    var t = File.ReadAllText(f);
    var nom = Path.GetFileName(f);
    Exiger(t.Contains("onKeyDown"), $"{nom} ecoute le clavier");
    Exiger(Regex.IsMatch(t, @"\.key\s*===?\s*['""]Enter['""][\s\S]{0,80}?rechercher\(\)"),
        $"{nom} lance la recherche sur Entree");
}
Exiger(recherches.Count + recherchesTsx.Count >= 2,
    $"les ecrans de recherche restent trouves, Blazor et React confondus ({recherches.Count} + {recherchesTsx.Count})");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "ClavierCheck : vert." : $"ClavierCheck : {echecs.Count} echec(s).");
return echecs.Count;
