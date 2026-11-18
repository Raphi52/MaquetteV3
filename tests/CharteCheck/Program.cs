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

// --- 6. « Commandes rondes » (18/11/2026, /draft conv-1979, proposition 5).
// La recherche de l'accueil est une pilule, le reglage et le profil sont des
// ronds a droite du titre, chaque tuile chiffree porte un grand rond a gauche
// de son chiffre. Le profil quitte la liste deroulante du bandeau pour le rond
// « RV » ; mais un ecran ouvert (.rig-proc) recouvre le titre, et le login doit
// rester atteignable depuis n'importe quel ecran (demande du 10/09/2026) : le
// meme rond passe alors dans le bandeau.
{
    var accueil = feuilles["rig-accueil.css"];
    var home = File.ReadAllText(Path.Combine(racine, "Components", "Pages", "Home.razor"));

    bool Rond(string selecteur, string rayon) =>
        Regex.IsMatch(accueil, Regex.Escape(selecteur) + @"\s*\{[^{}]*border-radius:\s*" + Regex.Escape(rayon));

    Exiger(Rond(".rig-recherche--fixe", "999px"), "la recherche de l'accueil est une pilule");
    Exiger(Rond(".rig-entete__action", "50%"), "le reglage a droite du titre est un rond");
    Exiger(Rond(".rig-profil", "50%"), "le profil est un rond");
    Exiger(Rond(".rig-chiffre__rond", "50%"), "le rond des tuiles chiffrees est un cercle");
    Exiger(Regex.Matches(home, @"class=""rig-chiffre__rond""").Count == 3,
        "chacune des 3 tuiles chiffrees porte son rond");

    Exiger(!home.Contains("<select class=\"rig-login\""),
        "le bandeau ne porte plus la liste deroulante du login");
    Exiger(Regex.IsMatch(home, @"rig-entete__outils[\s\S]*?@if \(!AccueilRecouvert\)\s*\{\s*@RondProfil\(dansLaBarre: false\)"),
        "sur l'accueil, le rond de profil est a droite du titre");
    Exiger(Regex.IsMatch(home, @"rig-barre__droite[\s\S]*?@if \(AccueilRecouvert\)\s*\{\s*@RondProfil\(dansLaBarre: true\)"),
        "un ecran ouvert garde le rond de profil, dans le bandeau");
    // Les DEUX ecrans pleins recouvrent le titre : l'ecran de procedure
    // (.rig-proc) ET l'affaire (.rig-vged). Oublier l'affaire perdait le login
    // la ou il sert le plus : il decide des branches visibles de son arbre.
    var recouvert = Regex.Match(home, @"bool AccueilRecouvert\s*=>([^;]*);");
    Exiger(recouvert.Success && recouvert.Groups[1].Value.Contains("ProcOuverte is not null")
                             && recouvert.Groups[1].Value.Contains("DossierOuvert is not null"),
        "l'accueil est recouvert par un ecran de procedure comme par une affaire ouverte");
    Exiger(Regex.IsMatch(home, @"@onclick=""\(\) => ChangerLogin\(compte\.Login\)"""),
        "le panneau du rond change bien de login");

    // Le fond de la fenetre (section 5) suit deja --rig-fond ; ici, la teinte
    // choisie : le clair gris bleute de la maquette, plus l'ivoire.
    Exiger(Regex.IsMatch(charte, @"\[data-rig-theme=""clair""\]\s*\{[^{}]*--rig-fond:\s*#EEF0F4", RegexOptions.IgnoreCase),
        "le mode clair est le gris bleute #EEF0F4 de la maquette");

    // Les documents sont du papier : BLANC PUR, hors charte (demande du
    // 18/11/2026 : le #fdfdfb chaud passait pour du creme sur le gris bleute).
    Exiger(Regex.IsMatch(accueil, @"\.rig-apercu__feuille\s*\{[^{}]*background:\s*#FFFFFF\s*;", RegexOptions.IgnoreCase),
        "la feuille des documents est blanc pur");
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
