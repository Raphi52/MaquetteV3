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
    // Le plan de travail (lot 2 du passage a React) : Pont/PlanTravail.cs (le @code
    // de Home.razor, deplace tel quel) et son balisage web/src/plan/.
    var home = File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravail.cs")) + "\n" + string.Join("\n", Directory.GetFiles(Path.Combine(racine, "web/src/plan"), "*.tsx").Append(Path.Combine(racine, "web/src/composants/Application.tsx")).Where(f => !f.EndsWith(".test.tsx")).Select(File.ReadAllText));

    // Depuis le panneau flottant du clair (19/11/2026), la colonne s'ecrit
    // calc(<largeur du menu> + var(--rig-rail-marge)) : la largeur lue est celle du menu.
    const string colonneReduite = @"\.rig-accueil--menu-reduit[^{}]*\{[^{}]*grid-template-columns:\s*(?:calc\()?(\d+)px";
    Exiger(Regex.IsMatch(accueil, colonneReduite),
        "rig-accueil.css reduit la colonne du menu via .rig-accueil--menu-reduit");

    var m = Regex.Match(accueil, colonneReduite);
    Exiger(m.Success && int.Parse(m.Groups[1].Value) is > 0 and < 232,
        "la colonne reduite est plus etroite que 232px mais reste visible (> 0)");

    Exiger(!Regex.IsMatch(accueil, @"\.rig-accueil--menu-reduit\s+\.rig-menu\s*\{[^{}]*display:\s*none"),
        "le menu reduit n'est jamais masque (pas de display:none sur .rig-menu)");

    // Depuis le lot 1 du passage a React (19/11/2026), l'accueil est rendu par
    // web/src/composants/Accueil.tsx et web/src/accueil/Menu.tsx ; l'etat du
    // menu vit dans la session de l'accueil (Pont/Accueil.cs).
    var pont = File.ReadAllText(Path.Combine(racine, "Pont", "Accueil.cs"));
    var accueilTsx = File.ReadAllText(Path.Combine(racine, "web", "src", "composants", "Accueil.tsx"));
    var menuTsx = File.ReadAllText(Path.Combine(racine, "web", "src", "accueil", "Menu.tsx"));

    Exiger(pont.Contains("public bool MenuReduit") && pont.Contains("private void BasculerMenu() => MenuReduit = !MenuReduit;")
           && pont.Contains("case \"menu\": BasculerMenu();"),
        "la session de l'accueil porte l'etat MenuReduit et sa bascule BasculerMenu");

    Exiger(Regex.IsMatch(accueilTsx, @"'rig-accueil '\s*\+\s*\(vue\.menuReduit\s*\?\s*'rig-accueil--menu-reduit'")
           && pont.Contains("new VuePlanAccueil(MenuReduit,"),
        "l'accueil applique la classe de reduction sur .rig-accueil");

    Exiger(Regex.IsMatch(menuTsx, @"className=""rig-menu__replier""\s+onClick=\{\(\) => geste\('menu'\)\}"),
        "l'accueil expose un bouton de repli/deploiement du menu");
    Exiger(!home.Contains("rig-menu__replier") && !home.Contains("bool MenuReduit"),
        "le plan de travail ne tient pas de copie du menu de gauche");
}

// --- 5. Pas de cadre autour de la page (18/11/2026). Une marge de 6 px autour
// du WebView, peinte avec des teintes ecrites en dur dans FenetreInterop et
// restees sur l'ancienne charte (#D8DDE6 / #0C0F16), dessinait un gros cadre
// gris autour de la page ivoire. La marge est retiree, le redimensionnement
// passe par des bandes de bord, et le fond de fenetre suit --rig-fond.
{
    var fenetreXaml = File.ReadAllText(Path.Combine(racine, "MainWindow.xaml"));
    var interop = File.ReadAllText(Path.Combine(racine, "FenetreInterop.cs"));
    // Les bandes de bord de Main.razor sont en React depuis le retrait de Blazor
    // (19/11/2026) : web/src/fenetre/Bords.tsx les dessine, la racine C# de la
    // fenetre principale (Pont/Racines.cs) les pose et mene le geste.
    var bordsTsx = File.ReadAllText(Path.Combine(racine, "web", "src", "fenetre", "Bords.tsx"));
    var racines = File.ReadAllText(Path.Combine(racine, "Pont", "Racines.cs"));

    var vue = Regex.Match(fenetreXaml, @"<wv2:WebView2\b[^>]*>");
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

    // Chaque bande porte le NOM de BordFenetre (FenetreInterop.cs) et sa classe :
    // un nom qui ne serait plus celui de l'enum ferait echouer Enum.Parse cote C#.
    var bords = new[] { ("Haut", "h"), ("Bas", "b"), ("Gauche", "g"), ("Droite", "d"),
                        ("HautGauche", "hg"), ("HautDroite", "hd"), ("BasGauche", "bg"), ("BasDroite", "bd") };
    Exiger(bords.All(b => bordsTsx.Contains($"['{b.Item1}', '{b.Item2}']")
                          && Regex.IsMatch(interop, @"\b" + b.Item1 + @"\s*=\s*1\d\b")),
        "la page pose une bande de redimensionnement sur les 8 bords et coins (Bords.tsx, noms de BordFenetre)");
    Exiger(bordsTsx.Contains("onMouseDown={() => { void redimensionner(bord) }}")
           && racines.Contains("fenetre!.Redimensionner(Enum.Parse<BordFenetre>(valeur.GetString() ?? string.Empty));"),
        "les bandes de bord rendent le redimensionnement a Windows (rappel « bord » -> Redimensionner)");
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
    // Le plan de travail (lot 2) : son code dans Pont/PlanTravail.cs, son balisage dans web/src/plan/.
    var home = File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravail.cs"));
    var balisagePlan = string.Join("\n", Directory.GetFiles(Path.Combine(racine, "web/src/plan"), "*.tsx").Append(Path.Combine(racine, "web/src/composants/Application.tsx")).Where(f => !f.EndsWith(".test.tsx")).Select(File.ReadAllText));

    bool Rond(string selecteur, string rayon) =>
        Regex.IsMatch(accueil, Regex.Escape(selecteur) + @"\s*\{[^{}]*border-radius:\s*" + Regex.Escape(rayon));

    Exiger(Rond(".rig-recherche--fixe", "999px"), "la recherche de l'accueil est une pilule");
    Exiger(Rond(".rig-entete__action", "50%"), "le reglage a droite du titre est un rond");
    Exiger(Rond(".rig-profil", "50%"), "le profil est un rond");
    Exiger(Rond(".rig-chiffre__rond", "50%"), "le rond des tuiles chiffrees est un cercle");
    // Depuis le lot 1 du passage a React (19/11/2026) : la barre, l'entete, les
    // chiffres et le rond de profil sont rendus par web/src/. Le rond n'est plus
    // qu'a droite du titre de l'accueil : la barre du haut ne porte que les
    // onglets et les boutons de la fenetre (19/11/2026, « comme Brave »).
    string Web(params string[] chemin) => File.ReadAllText(Path.Combine(new[] { racine, "web", "src" }.Concat(chemin).ToArray()));
    var pont = File.ReadAllText(Path.Combine(racine, "Pont", "Accueil.cs"));
    var chiffresTsx = Web("accueil", "Chiffres.tsx");
    var enteteTsx = Web("accueil", "Entete.tsx");
    var barreTsx = Web("composants", "BarreRig.tsx");
    var rondTsx = Web("accueil", "RondProfil.tsx");

    Exiger(Regex.Matches(chiffresTsx, @"className=""rig-chiffre__rond""").Count == 3,
        "chacune des 3 tuiles chiffrees porte son rond");

    Exiger(!home.Contains("<select class=\"rig-login\"") && !balisagePlan.Contains("rig-login") && !barreTsx.Contains("rig-login"),
        "le bandeau ne porte plus la liste deroulante du login");
    Exiger(Regex.IsMatch(enteteTsx, @"rig-entete__outils[\s\S]*?\{entete\.profil && <RondProfil profil=\{entete\.profil\}")
           && pont.Contains("ctx.Recouvert ? null : Profil());"),
        "sur l'accueil, le rond de profil est a droite du titre");
    Exiger(!barreTsx.Contains("RondProfil") && !barreTsx.Contains("rig-barre__marque")
           && !barreTsx.Contains("<Liste") && !pont.Contains("PanneauBarre"),
        "la barre du haut ne porte ni profil, ni marque, ni reglages : seulement les onglets et la fenetre");
    // Depuis la maison (conv-2013, 19/11/2026) : elle ouvre la barre, avant les onglets.
    // Depuis le « + » (conv-2009, meme jour) : au bout des onglets, avant la fenetre.
    Exiger(Regex.IsMatch(barreTsx, @"<header className=""rig-barre""[^\n]*\n[\s\S]*?<button className=\{'rig-vonglet rig-vonglet--maison'[\s\S]*?</button>\s*\{onglets && <Onglets[^\n]*\n(\s*\{/\*[\s\S]*?\*/\})?\s*<button className=""rig-vonglet rig-vonglet--plus""[\s\S]*?</button>\s*<div className=""rig-fenetre"">"),
        "la barre du haut : la maison, les onglets, le « + », puis les boutons de la fenetre");
    Exiger(home.Contains("return new(AccueilRecouvert, ProcOuverte?.Code,"),
        "le plan de travail passe a l'accueil s'il est recouvert");
    // Les DEUX ecrans pleins recouvrent le titre : l'ecran de procedure
    // (.rig-proc) ET l'affaire (.rig-vged). Oublier l'affaire perdait le login
    // la ou il sert le plus : il decide des branches visibles de son arbre.
    var recouvert = Regex.Match(home, @"bool AccueilRecouvert\s*=>([^;]*);");
    Exiger(recouvert.Success && recouvert.Groups[1].Value.Contains("ProcOuverte is not null")
                             && recouvert.Groups[1].Value.Contains("DossierOuvert is not null"),
        "l'accueil est recouvert par un ecran de procedure comme par une affaire ouverte");
    Exiger(rondTsx.Contains("onClick={() => geste('login', compte.login)}")
           && pont.Contains("case \"login\": ChangerLogin(Exiger(valeur));"),
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
// - un theme a fond clair absent de la regle color-scheme garde des controles
//   natifs sombres (barres de defilement) ; les LISTES ouvertes, elles, sont
//   tenues par la section 8 ;
// - un code de ThemesRig sans bloc CSS donne une page sans couleurs.
{
    // La page entiere est rendue par l'application React (lot 2) : web/src/composants/Application.tsx ;
    // le theme est lu dans la session du plan de travail (Pont/PlanTravail.cs).
    var home = File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravail.cs"));
    var application = File.ReadAllText(Path.Combine(racine, "web", "src", "composants", "Application.tsx"));
    var balisagePlan = string.Join("\n", Directory.GetFiles(Path.Combine(racine, "web/src/plan"), "*.tsx").Append(Path.Combine(racine, "web/src/composants/Application.tsx")).Where(f => !f.EndsWith(".test.tsx")).Select(File.ReadAllText));
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

    // En clair, le rail de gauche est « FER DEGRADE » (/draft conv-2000, 19/11/2026,
    // proposition 6 ; avant : gris clair #D7DAE0 le matin meme, encre #15182A la
    // veille) : un degrade VERTICAL de gris fonces, une ombre douce vers la page
    // au lieu d'un bord, la pastille or du verbe ouvert inchangee. Gris = trois
    // canaux proches ; son texte clair tient 4,5:1 jusqu'au tertiaire sur l'arret
    // le plus CLAIR du degrade (regle « rien ne reste pale » du 10/09/2026) ; et
    // --rig-rail-melange eclaircit les teintes ecrites en texte.
    {
        var blocClair = BlocDuTheme("clair");
        string? Hexa(string jeton)
        {
            var m = Regex.Match(blocClair, Regex.Escape(jeton) + @":\s*(#[0-9A-Fa-f]{6})\s*;");
            return m.Success ? m.Groups[1].Value : null;
        }
        string? Valeur(string jeton)
        {
            var m = Regex.Match(blocClair, Regex.Escape(jeton) + @":\s*([^;]+);");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        double Luminance(string hexa)
        {
            var c = Convert.FromHexString(hexa.TrimStart('#'));
            double Lin(byte v) { var s = v / 255.0; return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(c[0]) + 0.7152 * Lin(c[1]) + 0.0722 * Lin(c[2]);
        }
        double Contraste(string a, string b)
        {
            var (x, y) = (Luminance(a), Luminance(b));
            return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
        }
        // Clarte percue L* (CIELAB, 0 = noir, 100 = blanc) : des pas egaux a
        // l'oeil, dans les noirs comme dans les blancs. Pour comparer deux
        // SURFACES ; Contraste reste la mesure des textes.
        double Clarte(string hexa)
        {
            var y = Luminance(hexa);
            return y > 216.0 / 24389 ? 116 * Math.Cbrt(y) - 16 : y * 24389.0 / 27;
        }

        var valeurRail = Valeur("--rig-rail") ?? "";
        var arrets = Regex.Matches(valeurRail, @"#[0-9A-Fa-f]{6}").Select(m => m.Value).ToList();
        var grisFonces = arrets.All(h =>
        {
            var o = Convert.FromHexString(h.TrimStart('#'));
            return o.Max() - o.Min() <= 16 && Luminance(h) < 0.1;
        });
        Exiger(valeurRail.StartsWith("linear-gradient(180deg") && arrets.Count >= 2 && grisFonces,
            $"« clair » : le rail de gauche est un degrade vertical de gris fonces ({(valeurRail.Length > 0 ? valeurRail : "absent")})");
        // Le texte se mesure sur l'arret le plus clair : c'est la que le contraste est le plus faible.
        var arretClair = arrets.Count > 0 ? arrets.OrderByDescending(Luminance).First() : null;
        Exiger(arretClair is not null && Contraste(arretClair, FondDuTheme("clair") ?? arretClair) >= 3,
            "« clair » : le rail fonce se detache du fond de page");
        foreach (var jeton in new[] { "--rig-rail-texte", "--rig-rail-texte-2", "--rig-rail-texte-3" })
        {
            var texte = Hexa(jeton);
            var ratio = arretClair is null || texte is null ? 0 : Contraste(arretClair, texte);
            Exiger(ratio >= 4.5, $"« clair » : {jeton} lisible sur le rail fonce ({ratio:0.0}:1, 4,5 exige)");
        }
        var melange = Hexa("--rig-rail-melange");
        Exiger(melange is not null && Luminance(melange) > 0.8,
            "« clair » : --rig-rail-melange eclaircit les teintes du menu sur le rail fonce");

        // L'ombre remplace le bord : un jeton du theme, lu par .rig-menu.
        var ombre = Valeur("--rig-rail-ombre");
        Exiger(ombre is not null && ombre != "none" && Valeur("--rig-rail-bord") == "transparent",
            $"« clair » : le rail se detache par une ombre, sans bord ({ombre ?? "ombre absente"})");
        Exiger(menu.Contains("box-shadow: var(--rig-rail-ombre)"),
            ".rig-menu porte l'ombre du theme (--rig-rail-ombre)");

        // Un PANNEAU flottant, pas une barre collee au bord (demande du 19/11/2026,
        // conv-2000) : decolle de la fenetre par --rig-rail-marge, coins arrondis
        // par --rig-rail-rayon. Les colonnes s'elargissent de la marge, pour que le
        // menu garde sa largeur (232 px deploye, 44 px reduit) et que ses libelles
        // ne se replient pas.
        Exiger(menu.Contains("border-radius: var(--rig-rail-rayon)")
               && Regex.IsMatch(menu, @"margin:\s*var\(--rig-rail-marge\)\s+0\s+var\(--rig-rail-marge\)\s+var\(--rig-rail-marge\)"),
            ".rig-menu lit la marge et l'arrondi du theme (haut, bas et gauche decolles)");
        foreach (var colonne in new[] { "232px", "44px", "196px" })
            Exiger(accueil.Contains($"grid-template-columns: calc({colonne} + var(--rig-rail-marge)) 1fr"),
                $"la colonne du menu ({colonne}) s'elargit de la marge du panneau");

        // Le MEME panneau dans les CINQ themes (demande du 19/11/2026, conv-2012 :
        // « le meme panel dans tous les themes »), chacun dans ses couleurs :
        // decolle et arrondi, detache par une ombre et non par un bord, fond en
        // degrade vertical, textes du rail a 4,5:1 sur CHAQUE arret du degrade
        // (le plus clair pour un panneau fonce, le plus fonce pour le blanc du violet).
        var blocRacine = Regex.Match(charte, @":root\s*\{([^{}]*)\}").Groups[1].Value;
        string? ValeurDuTheme(string code, string jeton)
        {
            foreach (var bloc in new[] { BlocDuTheme(code), BlocDuTheme("sombre"), blocRacine })
            {
                var m = Regex.Match(bloc, Regex.Escape(jeton) + @":\s*([^;]+);");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }
        int Pixels(string? valeur) => valeur is not null && Regex.Match(valeur, @"^(\d+)px$") is { Success: true } p
            ? int.Parse(p.Groups[1].Value) : 0;
        foreach (var code in themes.Keys)
        {
            var marge = Pixels(ValeurDuTheme(code, "--rig-rail-marge"));
            var rayon = Pixels(ValeurDuTheme(code, "--rig-rail-rayon"));
            Exiger(marge is >= 6 and <= 24 && rayon is >= 8 and <= 24,
                $"« {code} » : le menu est un panneau decolle ({marge}px) aux coins arrondis ({rayon}px)");
            var ombreTheme = ValeurDuTheme(code, "--rig-rail-ombre");
            Exiger(ombreTheme is not null && ombreTheme != "none" && ValeurDuTheme(code, "--rig-rail-bord") == "transparent",
                $"« {code} » : le panneau se detache par une ombre, sans bord ({ombreTheme ?? "ombre absente"})");
            var rail = ValeurDuTheme(code, "--rig-rail") ?? "";
            var arretsTheme = Regex.Matches(rail, @"#[0-9A-Fa-f]{6}").Select(m => m.Value).ToList();
            Exiger(rail.StartsWith("linear-gradient(180deg") && arretsTheme.Count >= 2,
                $"« {code} » : le fond du panneau est un degrade vertical ({(rail.Length > 0 ? rail : "absent")})");
            // Sur une page SOMBRE, l'ombre ne se voit pas : le panneau doit se
            // detacher par sa COULEUR, sur toute sa hauteur. Defaut a l'origine
            // (19/11/2026, conv-2012) : le degrade du sombre allait de #2A2A2A a
            // #111111 et CROISAIT la page #1E1E1E — mesure sur capture, #1C1C1C a
            // mi-hauteur contre #1A1A1A autour : le panneau disparaissait.
            // Donc : tous les arrets du MEME cote de la page (tous plus clairs ou
            // tous plus fonces), et un ecart de CLARTE PERCUE (L* CIELAB) d'au
            // moins 5 sur le pire arret, plus que les cartes (#282828 sur #1E1E1E :
            // 4,9). Pas le rapport de contraste du texte (WCAG) : sa marge de 0,05
            // ecrase les noirs — meme le noir pur n'y fait que 1,26:1 sur #1E1E1E —
            // et il interdisait tout panneau plus fonce que la page.
            var page = FondDuTheme(code);
            bool SeDetache(string fond, out string mesure)
            {
                var clartePage = Clarte(fond);
                var ecarts = arretsTheme.Select(a => Clarte(a) - clartePage).ToList();
                var pire = ecarts.Min(e => Math.Abs(e));
                var unSeulCote = ecarts.All(e => e > 0) || ecarts.All(e => e < 0);
                mesure = unSeulCote ? $"ecart de clarte {pire:0.0} au pire, 5 exige" : "le degrade CROISE la page";
                return unSeulCote && pire >= 5;
            }
            if (page is not null && Luminance(page) < 0.2 && arretsTheme.Count > 0)
            {
                Exiger(SeDetache(page, out var mesure),
                    $"« {code} » : sur la page sombre {page}, le panneau reste d'un seul cote de la page et s'en distingue sur toute sa hauteur ({mesure})");
            }
            // Le sombre a un panneau NOIR, de la famille du bandeau d'en-tete
            // (#080808), donc plus FONCE que la page sur toute sa hauteur. Demande
            // du 19/11/2026 (conv-2012) : le panneau acier #3A3C42 -> #2E2F34, plus
            // clair que les cartes, etait « trop clair ».
            if (code == "sombre" && page is not null && arretsTheme.Count > 0)
            {
                var plusClair = arretsTheme.MaxBy(Clarte)!;
                Exiger(Clarte(plusClair) < Clarte(page),
                    $"« sombre » : le panneau est plus fonce que la page {page} sur toute sa hauteur (arret le plus clair : {plusClair})");
            }
            // Sur TOUTE page, le bord du panneau doit etre net : soit sa couleur
            // se detache de la page (regle ci-dessus), soit un FILET INTERIEUR
            // (inset 0 0 0 1px, opacite >= .10) le dessine. Defaut a l'origine
            // (19/11/2026, conv-2012) : le panneau blanc du violet finissait en
            // #F5F3FE contre #EBEBF3 autour (1,08:1 mesure sur capture), et son
            // ombre portee vers le bas ne tracait rien a gauche.
            if (page is not null && arretsTheme.Count > 0)
            {
                var tranche = SeDetache(page, out _);
                var filet = Regex.Match(ombreTheme ?? "", @"inset\s+0\s+0\s+0\s+1px\s+rgba\(\s*\d+,\s*\d+,\s*\d+,\s*(0?\.\d+)\s*\)");
                var filetNet = filet.Success && double.Parse(filet.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 0.10;
                Exiger(tranche || filetNet,
                    $"« {code} » : le bord du panneau est net sur la page {page} (couleur qui tranche, ou filet interieur d'au moins .10)");
            }
            foreach (var jeton in new[] { "--rig-rail-texte", "--rig-rail-texte-2", "--rig-rail-texte-3" })
            {
                var texte = ValeurDuTheme(code, jeton);
                var ratio = texte is null || !Regex.IsMatch(texte, @"^#[0-9A-Fa-f]{6}$") || arretsTheme.Count == 0
                    ? 0 : arretsTheme.Min(a => Contraste(a, texte));
                Exiger(ratio >= 4.5, $"« {code} » : {jeton} lisible sur tout le panneau ({ratio:0.0}:1 au pire, 4,5 exige)");
            }
        }

        // Commandes web, commande ouverte : la liste devient la colonne de GAUCHE
        // et prend l'habit du menu de l'accueil, theme par theme (demande du
        // 19/11/2026, conv-2019 : « le panel de gauche de la meme couleur que le
        // menu de gauche de l'accueil en fonction des themes »). Memes jetons que
        // .rig-menu ; les teintes ecrites en texte (bouton, liens, « 2/3 ») se
        // melangent a --rig-rail-melange, comme les groupes du menu, et tiennent
        // 4,5:1 sur chaque arret du degrade. Sans melange, l'orange brule du
        // theme Noir et orange ne fait que 3,0:1 sur son panneau noir, l'alerte
        // du clair 1,8:1 sur son gris fonce.
        {
            var liste = Regex.Match(accueil, @"\n\.rig-tcw__scene--ouverte \.rig-tcw__liste\s*\{([^{}]*)\}").Groups[1].Value;
            Exiger(liste.Contains("background: var(--rig-rail)") && liste.Contains("box-shadow: var(--rig-rail-ombre)")
                   && liste.Contains("border-radius: var(--rig-rail-rayon)")
                   && liste.Contains("--rig-texte: var(--rig-rail-texte)") && liste.Contains("--rig-texte-2: var(--rig-rail-texte-2)")
                   && liste.Contains("--rig-texte-3: var(--rig-rail-texte-3)") && liste.Contains("--rig-bord-faible: var(--rig-rail-trait-faible)"),
                "Commandes web : la liste de gauche (commande ouverte) prend le fond, l'ombre et les textes du menu de l'accueil");

            var regles = string.Concat(Regex.Matches(accueil, @"\n\.rig-tcw__scene--ouverte \.rig-tcw__liste[^{}]*\{([^{}]*)\}")
                .Select(m => m.Groups[1].Value));
            var melanges = Regex.Matches(regles, @"color-mix\(in srgb, var\((--rig-[a-z-]+)\) (\d+)%, var\(--rig-rail-melange\)\)")
                .Select(m => (jeton: m.Groups[1].Value, part: int.Parse(m.Groups[2].Value) / 100.0)).ToList();
            Exiger(new[] { "--rig-or", "--rig-alerte", "--rig-palier-vide" }.All(j => melanges.Any(x => x.jeton == j)),
                $"Commandes web : la liste de gauche melange au rail l'accent, l'alerte et le « pret » ({string.Join(", ", melanges.Select(x => x.jeton))})");
            string Melange(string a, string b, double part)
            {
                var (x, y) = (Convert.FromHexString(a.TrimStart('#')), Convert.FromHexString(b.TrimStart('#')));
                return "#" + string.Concat(Enumerable.Range(0, 3).Select(i => ((int)Math.Round(x[i] * part + y[i] * (1 - part))).ToString("X2")));
            }
            foreach (var code in themes.Keys)
            {
                var arretsListe = Regex.Matches(ValeurDuTheme(code, "--rig-rail") ?? "", @"#[0-9A-Fa-f]{6}").Select(m => m.Value).ToList();
                var melangeRail = ValeurDuTheme(code, "--rig-rail-melange");
                foreach (var (jeton, part) in melanges)
                {
                    var teinte = ValeurDuTheme(code, jeton);
                    var lisible = teinte is not null && melangeRail is not null && arretsListe.Count > 0
                        && Regex.IsMatch(teinte, @"^#[0-9A-Fa-f]{6}$") && Regex.IsMatch(melangeRail, @"^#[0-9A-Fa-f]{6}$");
                    var ratio = lisible ? arretsListe.Min(a => Contraste(a, Melange(teinte!, melangeRail!, part))) : 0;
                    Exiger(ratio >= 4.5,
                        $"« {code} » : Commandes web, {jeton} melange a {part:P0} lisible sur la liste de gauche ({ratio:0.0}:1 au pire, 4,5 exige)");
                }
            }
        }
    }

    // La bascule a disparu au profit d'une liste nourrie par ThemesRig.
    // Depuis le 19/11/2026 la liste est en tete des reglages de l'accueil
    // (web/src/accueil/Entete.tsx) : la barre du haut ne porte plus que les
    // onglets. Le choix passe par la session de l'accueil (Pont/Accueil.cs),
    // <html> recoit le theme par ThemeRig.tsx.
    var reglagesTsx = File.ReadAllText(Path.Combine(racine, "web", "src", "accueil", "Entete.tsx"));
    var themeTsx = File.ReadAllText(Path.Combine(racine, "web", "src", "composants", "ThemeRig.tsx"));
    var pont = File.ReadAllText(Path.Combine(racine, "Pont", "Accueil.cs"));
    Exiger(!home.Contains("BasculerTheme") && !balisagePlan.Contains("Mode sombre")
           && !Regex.IsMatch(reglagesTsx, @">\s*Mode sombre\s*<") && !pont.Contains("BasculerTheme"),
        "la bascule « Mode sombre » a disparu des reglages");
    Exiger(Regex.IsMatch(reglagesTsx, @"<Liste[^>]*id=""rig-choix-theme""[^>]*changer=\{\(code\) => geste\('theme', code\)\}")
           && reglagesTsx.Contains("panneau.themes.map((t) => <option key={t.code} value={t.code}>{t.libelle}</option>)")
           && pont.Contains("ThemesRig.Tous.Select(t => new ThemeVue(t.Code, t.Libelle))")
           && pont.Contains("case \"theme\": ChoisirTheme(Texte(valeur));"),
        "les reglages de l'accueil proposent une liste deroulante des themes de ThemesRig");
    Exiger(application.Contains("<div data-rig-theme={v.theme}") && home.Contains("private string Theme => Accueil.Theme;")
           && File.ReadAllText(Path.Combine(racine, "Pont", "PlanTravailVue.cs")).Contains("return new VueApplication(\r\n            Theme,".Replace("\r\n", Environment.NewLine)),
        "la page porte le code du theme choisi");
    Exiger(themeTsx.Contains("document.documentElement.setAttribute('data-rig-theme', theme)")
           && application.Contains("<ThemeRig theme={v.theme} />"),
        "<html> porte aussi le theme, dans toutes les fenetres (ile ThemeRig)");
}

// --- 8. Listes deroulantes lisibles dans TOUS les themes. La liste ouverte
// d'un <select> est une fenetre a part qui ne rend pas la transparence : une
// ligne sans fond OPAQUE pose le voile translucide du champ sur du blanc, sous
// le texte clair du theme. Vu le 10/09/2026 (liste des logins, corrigee pour
// elle seule) puis le 18/11/2026 (liste des themes) : la regle doit etre
// GLOBALE, et son jeton opaque dans chaque theme, y compris un theme ajoute.
{
    var regle = Regex.Match(charte, @"(?m)^select\s+option\s*\{([^{}]*)\}");
    Exiger(regle.Success
           && regle.Groups[1].Value.Contains("background-color: var(--rig-champ)")
           && regle.Groups[1].Value.Contains("color: var(--rig-texte)"),
        "rig-charte.css donne un fond et un texte de theme a TOUTES les lignes de liste (select option)");

    foreach (var code in themes.Keys)
    {
        var champ = Regex.Match(BlocDuTheme(code), @"--rig-champ:\s*([^;]+);");
        Exiger(champ.Success && Regex.IsMatch(champ.Groups[1].Value.Trim(), @"^#[0-9A-Fa-f]{6}$"),
            $"« {code} » : --rig-champ est opaque ({(champ.Success ? champ.Groups[1].Value.Trim() : "absent")}), fond des listes ouvertes");
    }

    // Une regle plus precise qui redonnerait aux lignes un fond translucide
    // ou transparent defait la regle globale.
    foreach (var (nom, texte) in feuilles)
    {
        var sansCommentaires = Regex.Replace(texte, @"/\*.*?\*/", "", RegexOptions.Singleline);
        foreach (Match bloc in Regex.Matches(sansCommentaires, @"([^{}]*\boption\b[^{}]*)\{([^{}]*)\}"))
        {
            var fond = Regex.Match(bloc.Groups[2].Value, @"background(?:-color)?:\s*([^;]+);");
            if (!fond.Success) continue;
            var valeur = fond.Groups[1].Value.Trim();
            Exiger(!Regex.IsMatch(valeur, @"rgba|transparent|--rig-fond-2|--rig-zebre|--rig-survol"),
                $"{nom} : « {bloc.Groups[1].Value.Trim()} » garde un fond opaque aux lignes de liste ({valeur})");
        }
    }
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
