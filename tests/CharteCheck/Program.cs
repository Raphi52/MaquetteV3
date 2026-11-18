using System.Text.RegularExpressions;

// Garde-fou de mise en page.
//
// Defaut a l'origine de ce test (09/09/2026) : aucune feuille ne posait
// box-sizing: border-box. Une largeur de 100 % s'ajoutait alors au
// remplissage et a la bordure, et TOUS les champs de la saisie guidee
// depassaient de leur colonne. Le defaut etait visible sur une capture
// que j'avais deja regardee : l'oeil ne suffit pas, il faut une regle.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var wwwroot = Path.Combine(racine, "wwwroot");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var charte = File.ReadAllText(Path.Combine(wwwroot, "rig-charte.css"));
var feuilles = Directory.GetFiles(wwwroot, "*.css")
    .ToDictionary(f => Path.GetFileName(f)!, File.ReadAllText);

// --- 1. Le reset existe et porte sur TOUS les elements.
Exiger(
    Regex.IsMatch(charte, @"\*[^{}]*\{[^{}]*box-sizing:\s*border-box", RegexOptions.Singleline),
    "rig-charte.css pose box-sizing: border-box sur *");

// --- 2. Personne ne le reintroduit a l'envers.
foreach (var (nom, texte) in feuilles)
{
    Exiger(!texte.Contains("box-sizing: content-box"),
        $"{nom} ne remet pas box-sizing en content-box");
}

// --- 3. Toute saisie large est bien contrainte a sa colonne.
foreach (var (nom, texte) in feuilles)
{
    foreach (Match bloc in Regex.Matches(texte, @"([^{}]+)\{([^{}]*)\}"))
    {
        var corps = bloc.Groups[2].Value;
        if (!Regex.IsMatch(corps, @"width:\s*100%")) continue;
        if (!Regex.IsMatch(corps, @"padding:\s*[^0;]")) continue;
        var selecteur = bloc.Groups[1].Value.Trim().Replace("\n", " ");
        // Sans le reset general, ce couple deborde toujours : on exige
        // que le reset soit present, sinon on nomme le coupable.
        Exiger(Regex.IsMatch(charte, @"\*[^{}]*\{[^{}]*box-sizing:\s*border-box", RegexOptions.Singleline),
            $"{nom} : « {selecteur} » melange width:100% et padding, le reset est indispensable");
    }
}

// --- 4. Le menu de gauche se reduit et se redeploie (11/09/2026).
// Defaut a l'origine : le menu occupait une colonne fixe de 232px, sans
// aucun moyen de la replier. Le repli responsive (<1100px) n'est pas un
// repli choisi par l'utilisateur : il faut un etat commande, et il doit
// rester visible sous forme de rail (jamais display:none).
{
    var accueil = feuilles["rig-accueil.css"];
    var home = File.ReadAllText(Path.Combine(racine, "Components", "Pages", "Home.razor"));

    Exiger(Regex.IsMatch(accueil, @"\.rig-accueil--menu-reduit[^{}]*\{[^{}]*grid-template-columns:\s*(\d+)px"),
        "rig-accueil.css reduit la colonne du menu via .rig-accueil--menu-reduit");

    var m = Regex.Match(accueil, @"\.rig-accueil--menu-reduit[^{}]*\{[^{}]*grid-template-columns:\s*(\d+)px");
    Exiger(m.Success && int.Parse(m.Groups[1].Value) is > 0 and < 232,
        "la colonne reduite est plus etroite que 232px mais reste visible (> 0)");

    Exiger(!Regex.IsMatch(accueil, @"\.rig-accueil--menu-reduit\s+\.rig-menu\s*\{[^{}]*display:\s*none"),
        "le menu reduit n'est jamais masque (pas de display:none sur .rig-menu)");

    Exiger(home.Contains("MenuReduit") && home.Contains("BasculerMenu"),
        "Home.razor porte l'etat MenuReduit et sa bascule BasculerMenu");

    Exiger(Regex.IsMatch(home, @"rig-accueil\s+@\(MenuReduit\s*\?\s*""rig-accueil--menu-reduit"""),
        "Home.razor applique la classe de reduction sur .rig-accueil");

    Exiger(Regex.IsMatch(home, @"rig-menu__replier[^>]*@onclick=""BasculerMenu"""),
        "Home.razor expose un bouton de repli/deploiement du menu");
}

// --- 5. Pas de cadre autour de la page (18/11/2026). Une marge de 6 px autour
// du WebView, peinte avec des teintes ecrites en dur dans FenetreInterop et
// restees sur l'ancienne charte (#D8DDE6 / #0C0F16), dessinait un gros cadre
// gris autour de la page ivoire. La marge est retiree, le redimensionnement
// passe par des bandes de bord, et le fond de fenetre suit --rig-fond.
{
    var fenetreXaml = File.ReadAllText(Path.Combine(racine, "MainWindow.xaml"));
    var interop = File.ReadAllText(Path.Combine(racine, "FenetreInterop.cs"));
    var main = File.ReadAllText(Path.Combine(racine, "Main.razor"));

    var vue = Regex.Match(fenetreXaml, @"<blazor:BlazorWebView\b[^>]*>");
    Exiger(vue.Success && !Regex.IsMatch(vue.Value, @"\bMargin="),
        "MainWindow.xaml : le WebView n'a pas de marge (aucun cadre autour de la page)");

    string? FondDuTheme(string theme)
    {
        var bloc = Regex.Match(charte,
            @"\[data-rig-theme=""" + theme + @"""\]\s*\{([^{}]*)\}");
        var fond = Regex.Match(bloc.Groups[1].Value, @"--rig-fond:\s*(#[0-9A-Fa-f]{6})");
        return fond.Success ? fond.Groups[1].Value.ToUpperInvariant() : null;
    }
    string? Constante(string nom)
    {
        var m = Regex.Match(interop, nom + @"\s*=\s*""(#[0-9A-Fa-f]{6})""");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }
    var clair = FondDuTheme("clair");
    var sombre = FondDuTheme("sombre");
    Exiger(clair is not null && Constante("FondClair") == clair,
        $"FenetreInterop.FondClair suit --rig-fond du theme clair ({clair})");
    Exiger(sombre is not null && Constante("FondSombre") == sombre,
        $"FenetreInterop.FondSombre suit --rig-fond du theme sombre ({sombre})");
    Exiger(!Regex.IsMatch(interop, @"Color\.FromRgb\("),
        "FenetreInterop ne peint aucune teinte en dur hors des deux constantes");

    var bords = new[] { "Haut", "Bas", "Gauche", "Droite", "HautGauche", "HautDroite", "BasGauche", "BasDroite" };
    Exiger(bords.All(b => main.Contains("BordFenetre." + b + ",")),
        "Main.razor pose une bande de redimensionnement sur les 8 bords et coins");
    Exiger(main.Contains("Fenetre.Redimensionner(bord)"),
        "les bandes de bord rendent le redimensionnement a Windows (Redimensionner)");
    Exiger(new[] { "h", "b", "g", "d", "hg", "hd", "bg", "bd" }
            .All(c => Regex.IsMatch(charte, @"\.rig-bord--" + c + @"\s*\{[^{}]*cursor:\s*[a-z]+-resize")),
        "rig-charte.css donne un curseur de redimensionnement a chaque bande");
}

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("Charte : tout est vert.");
    return 0;
}
Console.WriteLine($"Charte : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
