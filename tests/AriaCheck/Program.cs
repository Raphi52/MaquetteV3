using System.Text.RegularExpressions;

// Garde-fou du BOUTON MUET (16/09/2026).
//
// Constat mesure avant ce banc : aria-label present 0 fois dans les 35 fichiers
// de Components/Ecrans/ (seul Home.razor en portait). Un bouton qui ne contient
// qu'une icone n'a alors AUCUN nom : ni pour un lecteur d'ecran, ni pour un test
// qui cherche un bouton par son nom.
//
// La regle : tout <button> dont le contenu ne porte pas de texte lisible doit
// declarer aria-label (et title, qui l'affiche a la souris).

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// RETRAIT DE BLAZOR (19/11/2026) : plus aucun ecran .razor. La regle se tient
// sur les .tsx ci-dessous, et le seuil « au moins 30 ecrans » sur web/src/ecrans.
Exiger(!Directory.EnumerateFiles(racine, "*.razor", SearchOption.AllDirectories)
            .Any(f => !Regex.IsMatch(f, @"[\\/](bin|obj|node_modules|binverif)[\\/]")),
    "plus aucun ecran .razor : la regle se lit sur les ecrans React");
var ecransTsx = Directory.GetFiles(Path.Combine(racine, "web/src/ecrans"), "Ecran*.tsx")
                         .Count(f => !f.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase));
Exiger(ecransTsx >= 30, $"les ecrans sont trouves ({ecransTsx})");

var muets = new List<string>();
var examines = 0;

// --- Les ecrans PORTES EN REACT (web/src, 18/11/2026) : meme regle sur le .tsx.
// Le balisage change de syntaxe, pas de sens : un attribut s'ecrit aussi
// entre accolades (onClick={() => ...}, dont le « => » n'est pas une fin de
// balise), et un commentaire s'ecrit {/* */} au lieu de @* *@.
var racineWeb = Path.Combine(racine, "web/src");
var fichiersTsx = Directory.Exists(racineWeb)
    ? Directory.GetFiles(racineWeb, "*.tsx", SearchOption.AllDirectories)
               .Where(f => !f.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase))
               .OrderBy(f => f).ToList()
    : [];
Exiger(fichiersTsx.Count >= 3, $"les ecrans React sont trouves ({fichiersTsx.Count})");
var examinesTsx = 0;
foreach (var f in fichiersTsx)
{
    var texte = File.ReadAllText(f);
    var nom = Path.GetFileName(f);
    // Attribut = chaine "..." ou '...', expression {...} (une imbrication
    // d'accolades : style={{ ... }}), ou tout autre caractere hors > " ' {.
    foreach (Match b in Regex.Matches(texte,
                 @"<button\b(?<attrs>(?:""[^""]*""|'[^']*'|\{(?:[^{}]|\{[^{}]*\})*\}|[^>""'{])*)>(?<corps>.*?)</button>",
                 RegexOptions.Singleline))
    {
        examinesTsx++;
        var attrs = b.Groups["attrs"].Value;
        var corps = b.Groups["corps"].Value;

        var nu = Regex.Replace(corps, @"\{/\*.*?\*/\}", " ", RegexOptions.Singleline);
        // Une expression qui n'est qu'un texte litteral ({'☆'}) compte pour son texte.
        nu = Regex.Replace(nu, @"\{\s*(['""`])(?<t>[^'""`]*)\1\s*\}", "${t}");
        nu = Regex.Replace(nu, @"<[^>]*>", " ");
        nu = Regex.Replace(nu, @"&[a-zA-Z]+;|&#\d+;", " ");
        nu = Regex.Replace(nu, @"[\p{Cs}\p{So}]", " ");
        nu = nu.Trim();

        var nomme = Regex.IsMatch(attrs, @"aria-label\s*=") || Regex.IsMatch(attrs, @"\btitle\s*=");
        if (nu.Length < 2 && !nomme)
        {
            var ligne = texte.Take(b.Index).Count(c => c == '\n') + 1;
            muets.Add($"{nom}:{ligne}");
        }
    }
}
Exiger(fichiersTsx.Count == 0 || examinesTsx > 0, $"des boutons sont bien lus dans les ecrans React ({examinesTsx})");
examines += examinesTsx;

Exiger(examines > 50, $"des boutons sont bien lus dans les ecrans ({examines})");
Exiger(muets.Count == 0,
    "aucun bouton-icone sans nom dans les ecrans"
    + (muets.Count == 0 ? "" : $" -- {muets.Count} muets : " + string.Join(", ", muets.Take(12))
        + (muets.Count > 12 ? ", ..." : "")));

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "AriaCheck : vert." : $"AriaCheck : {echecs.Count} echec(s).");
return echecs.Count;
