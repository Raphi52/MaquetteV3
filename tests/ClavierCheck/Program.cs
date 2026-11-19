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
// Depuis le lot 2 du passage a React (19/11/2026), il est dans
// web/src/plan/clavier.ts (ex-activerClavier de wwwroot/rig-accueil.js, retire),
// branche par l'application (web/src/composants/Application.tsx) ; vitest le
// joue en plus (web/src/plan/clavier.test.ts).
var js = File.ReadAllText(Path.Combine(racine, "web/src/plan/clavier.ts"));
var application = File.ReadAllText(Path.Combine(racine, "web/src/composants/Application.tsx"));
// fix-ok: le regex acceptait tout suffixe (XXaddEventListener matchait) ; ancre sur l'ecoute du DOCUMENT
Exiger(Regex.IsMatch(js, @"\bdoc\.addEventListener\(\s*'keydown'") && js.Contains("doc: Document = document"),
    "clavier.ts branche un ecouteur keydown sur le document");
Exiger(Regex.IsMatch(js, @"export function ecouterEchap\("),
    "clavier.ts expose ecouterEchap(echap) pour le brancher");
Exiger(js.Contains("'Escape'"),
    "l'ecouteur reconnait la touche Echap");
Exiger(js.Contains("return () => doc.removeEventListener('keydown', surTouche)")
       && application.Contains("useEffect(() => ecouterEchap(() => gestePlan('echap')), [gestePlan])"),
    "l'ecouteur clavier est debranche quand la page s'en va");

// --- 2. Echap arrive jusqu'a C#, et ferme ce qui est ouvert.
// La regle vit dans la session du plan de travail (Pont/PlanTravail.cs, le @code
// de Home.razor deplace tel quel) ; le geste « echap » y mene (Pont/PlanTravailVue.cs).
var plan = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
var gestes = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravailVue.cs"));
Exiger(gestes.Contains("case \"echap\": ToucheEchap(); break;"),
    "le plan de travail recoit Echap de la page (geste « echap »)");
var corps = Regex.Match(plan, @"private void ToucheEchap\(\)(?<c>.*?)\n    \}", RegexOptions.Singleline);
Exiger(corps.Success && corps.Groups["c"].Value.Contains("FermerProc"),
    "Echap ferme l'ecran de procedure ouvert");
// Depuis le 19/11/2026 l'affaire a son onglet : Echap la ferme AVEC lui (FermerAffaire).
Exiger(corps.Success && corps.Groups["c"].Value.Contains("FermerAffaire"),
    "Echap ferme l'affaire ouverte");
Exiger(Regex.IsMatch(corps.Groups["c"].Value, @"if \(DossierOuvert is not null\) FermerAffaire\(\);\s*else if \(ProcOuverte is not null\) FermerProc\(\);"),
    "Echap ferme l'affaire d'abord, l'ecran ensuite : jamais deux choses a la fois");

// --- 3. Entree lance la recherche partout ou une recherche existe.
// Depuis le retrait de Blazor (lot 3 du plan « finir le passage a React »,
// 19/11/2026), il n'y a plus d'ecran .razor : la regle se tient sur la seule
// famille React (section 3 bis), et on s'assure qu'aucun .razor n'est revenu
// en douce (il echapperait alors a ce controle).
Exiger(!Directory.EnumerateFiles(racine, "*.razor", SearchOption.AllDirectories)
            .Any(f => !Regex.IsMatch(f, @"[\\/](bin|obj|node_modules|binverif)[\\/]")),
    "plus aucun ecran .razor : les ecrans de recherche se lisent en React");

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
Exiger(recherchesTsx.Count >= 2,
    $"les ecrans de recherche restent trouves ({recherchesTsx.Count})");
// Plus strict que l'ancien seuil (>= 2 sur les seuls .razor) : les TROIS ecrans
// de recherche connus le 19/11/2026 sont nommes, et chacun doit etre trouve
// parmi les ecrans React -- un ecran qui perdrait son bouton Rechercher
// en passant en React ne sortirait plus du controle sans bruit.
var nomsRecherche = recherchesTsx.Select(Path.GetFileNameWithoutExtension).ToHashSet();
Exiger(new[] { "EcranRechAffaires", "EcranRequisitions", "EcranCommandesWeb" }.All(nomsRecherche.Contains),
    $"les ecrans de recherche sont trouves ({string.Join(", ", nomsRecherche.Order())})");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "ClavierCheck : vert." : $"ClavierCheck : {echecs.Count} echec(s).");
return echecs.Count;
