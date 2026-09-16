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

var fichiers = Directory.GetFiles(Path.Combine(racine, "Components/Ecrans"), "*.razor").OrderBy(f => f).ToList();
Exiger(fichiers.Count >= 30, $"les ecrans sont trouves ({fichiers.Count})");

var muets = new List<string>();
var examines = 0;
foreach (var f in fichiers)
{
    var texte = File.ReadAllText(f);
    var nom = Path.GetFileName(f);
    foreach (Match b in Regex.Matches(texte, @"<button\b(?<attrs>(?:""[^""]*""|[^>""])*)>(?<corps>.*?)</button>", RegexOptions.Singleline))
    {
        examines++;
        var attrs = b.Groups["attrs"].Value;
        var corps = b.Groups["corps"].Value;

        // Texte lisible = ce qui reste une fois les balises, les commentaires
        // Razor et les expressions d'icone retires.
        var nu = Regex.Replace(corps, @"<[^>]*>", " ");
        nu = Regex.Replace(nu, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        nu = Regex.Replace(nu, @"&[a-zA-Z]+;|&#\d+;", " ");
        nu = Regex.Replace(nu, @"[\p{Cs}\p{So}]", " ");   // emojis et symboles
        nu = nu.Trim();

        var nomme = Regex.IsMatch(attrs, @"aria-label\s*=") || Regex.IsMatch(attrs, @"\btitle\s*=");
        if (nu.Length < 2 && !nomme)
        {
            var ligne = texte.Take(b.Index).Count(c => c == '\n') + 1;
            muets.Add($"{nom}:{ligne}");
        }
    }
}

Exiger(examines > 50, $"des boutons sont bien lus dans les ecrans ({examines})");
Exiger(muets.Count == 0,
    "aucun bouton-icone sans nom dans les ecrans"
    + (muets.Count == 0 ? "" : $" -- {muets.Count} muets : " + string.Join(", ", muets.Take(12))
        + (muets.Count > 12 ? ", ..." : "")));

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "AriaCheck : vert." : $"AriaCheck : {echecs.Count} echec(s).");
return echecs.Count;
