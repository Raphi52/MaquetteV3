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

// Les themes de la liste des reglages, tels que les declare ThemesRig
// (AccueilEtat.cs) : code -> fond de fenetre.
var themes = Regex.Matches(File.ReadAllText(Path.Combine(racine, "AccueilEtat.cs")),
        @"new\(""([a-z]+)"",\s*""[^""]+"",\s*""(#[0-9A-Fa-f]{6})""\)")
    .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());

// Corps du bloc [data-rig-theme="code"] de la charte (vide s'il manque).
string BlocDuTheme(string code)
    => Regex.Match(charte, @"\[data-rig-theme=""" + code + @"""\]\s*\{([^{}]*)\}").Groups[1].Value;
string? FondDuTheme(string code)
{
    var fond = Regex.Match(BlocDuTheme(code), @"--rig-fond:\s*(#[0-9A-Fa-f]{6})");
    return fond.Success ? fond.Groups[1].Value.ToUpperInvariant() : null;
}
HashSet<string> Jetons(string corps)
    => Regex.Matches(corps, @"(--rig-[a-z0-9-]+)\s*:").Select(m => m.Groups[1].Value).ToHashSet();

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

    // Depuis le 18/11/2026 les fonds vivent dans ThemesRig (AccueilEtat.cs),
    // une entree par theme de la liste des reglages : chacun doit suivre le
    // --rig-fond de SON bloc de rig-charte.css.
    foreach (var (code, fondDeclare) in themes)
    {
        var fond = FondDuTheme(code);
        Exiger(fond is not null && fond == fondDeclare,
            $"ThemesRig « {code} » : le fond de fenetre {fondDeclare} suit --rig-fond de la feuille ({fond})");
    }
    Exiger(Regex.IsMatch(interop, @"ThemesRig\.De\(theme\)\.Fond"),
        "FenetreInterop.AppliquerFond peint le fond du theme lu dans ThemesRig");
    Exiger(!Regex.IsMatch(interop, @"Color\.FromRgb\(|""#[0-9A-Fa-f]{6}"""),
        "FenetreInterop ne peint aucune teinte en dur");

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

// --- 7. Themes au choix (18/11/2026). La bascule « Mode sombre » devient une
// liste de cinq themes. Trois pieges, chacun silencieux a la compilation :
// - un jeton oublie dans un bloc retombe sur la valeur du SOMBRE (:root) :
//   texte creme sur carte blanche, illisible ;
// - un theme a fond clair absent de la regle color-scheme garde des listes
//   deroulantes systeme sombres (constat du 10/09/2026, en sens inverse) ;
// - un code de ThemesRig sans bloc CSS donne une page sans couleurs.
{
    var home = File.ReadAllText(Path.Combine(racine, "Components", "Pages", "Home.razor"));
    var accueil = feuilles["rig-accueil.css"];

    Exiger(themes.Count >= 5 && new[] { "clair", "sombre", "violet", "orange", "argile" }.All(themes.ContainsKey),
        $"ThemesRig declare les cinq themes ({string.Join(", ", themes.Keys)})");

    // Le contrat : tout ce que le clair redefinit, plus les jetons du rail.
    var contrat = Jetons(BlocDuTheme("clair"));
    contrat.UnionWith(new[] { "--rig-or-halo", "--rig-rail-texte", "--rig-rail-texte-2", "--rig-rail-texte-3",
        "--rig-rail-voile", "--rig-rail-trait", "--rig-rail-trait-faible", "--rig-rail-melange" });
    var parDefaut = Jetons(Regex.Match(charte, @":root\s*\{([^{}]*)\}").Groups[1].Value);
    parDefaut.UnionWith(Jetons(BlocDuTheme("sombre")));
    var sansDefaut = contrat.Except(parDefaut).ToList();
    Exiger(sansDefaut.Count == 0,
        "chaque jeton du contrat a une valeur par defaut (:root ou bloc sombre)"
        + (sansDefaut.Count > 0 ? " — manque : " + string.Join(", ", sansDefaut) : ""));

    foreach (var code in themes.Keys.Where(c => c is not ("clair" or "sombre")))
    {
        var manquants = contrat.Except(Jetons(BlocDuTheme(code))).ToList();
        Exiger(manquants.Count == 0,
            $"le theme « {code} » redefinit tous les jetons du clair et du rail"
            + (manquants.Count > 0 ? " — manque : " + string.Join(", ", manquants) : ""));
    }

    foreach (var (code, fond) in themes)
    {
        var rvb = Convert.FromHexString(fond.TrimStart('#'));
        var clairOuSombre = (0.2126 * rvb[0] + 0.7152 * rvb[1] + 0.0722 * rvb[2]) / 255 > 0.5;
        var declareClair = charte.Contains(@"html:has([data-rig-theme=""" + code + @"""])");
        Exiger(clairOuSombre == declareClair,
            $"« {code} » : color-scheme {(clairOuSombre ? "light" : "dark")} pour un fond {fond}");
    }

    // Le rail lit ses couleurs dans les jetons du theme, plus en dur.
    var menu = Regex.Match(accueil, @"\n\.rig-menu\s*\{([^{}]*)\}").Groups[1].Value;
    Exiger(menu.Contains("--rig-texte: var(--rig-rail-texte)") && !Regex.IsMatch(menu, @"--rig-texte[^:]*:\s*#"),
        "le rail de gauche prend ses couleurs de texte dans les jetons --rig-rail-* du theme");

    // La bascule a disparu au profit d'une liste nourrie par ThemesRig.
    Exiger(!home.Contains("BasculerTheme") && !home.Contains("<span>Mode sombre</span>"),
        "la bascule « Mode sombre » a disparu des reglages");
    Exiger(Regex.IsMatch(home, @"<select[^>]*id=""rig-choix-theme""[^>]*@onchange=""ChoisirTheme""")
           && home.Contains("@foreach (var t in ThemesRig.Tous)"),
        "Reglages > Affichage propose une liste deroulante des themes de ThemesRig");
    Exiger(home.Contains(@"data-rig-theme=""@Theme"""),
        "la page porte le code du theme choisi");
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
