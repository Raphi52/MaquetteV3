using System.Text.RegularExpressions;

// Garde-fou du NOM METIER.
//
// Defaut a l'origine de ce test (16/09/2026) : le code interne des
// procedures de RIG v2 (PROC_xxx) fuyait vers l'ecran. Trois endroits
// mesures : l'infobulle « Ouvrir la recherche d'affaires (PROC_RECHAFF) »
// dans Home.razor, le message « Voir TODO #275 dans PROC_REQUI » des
// requisitions, et la colonne « Processus » des modeles d'edition, qui
// affichait PROC_DEMANDE, PROC_ARRIVEE... en clair dans un tableau.
// Home.razor le disait pourtant deja en commentaire : « Le code technique
// n'est JAMAIS affiche ». Un commentaire ne retient rien ; une regle si.
//
// Ce que ce test exige :
//  1. aucun attribut de texte (title, placeholder, alt) ne contient PROC_ ;
//  2. aucun texte rendu (entre deux balises) ne contient PROC_ ;
//  3. hors Home.razor -- le seul aiguilleur, ou les codes servent de cle de
//     navigation -- aucune chaine litterale ne contient PROC_.
// Les commentaires (@* *@ et //) sont exclus : la tracabilite vers les
// sources v2 est utile et invisible a l'utilisateur.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var composants = Path.Combine(racine, "Components");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// Retire les commentaires Razor @* ... *@ et les commentaires C# de ligne.
static string SansCommentaires(string texte)
{
    var t = Regex.Replace(texte, @"@\*.*?\*@", " ", RegexOptions.Singleline);
    t = Regex.Replace(t, @"(?m)^\s*//.*$", " ");
    t = Regex.Replace(t, @"(?m)^\s*///.*$", " ");
    return t;
}

var fichiers = Directory.GetFiles(composants, "*.razor", SearchOption.AllDirectories);
Exiger(fichiers.Length > 0, "des ecrans .razor sont trouves");

foreach (var chemin in fichiers)
{
    var nom = Path.GetFileName(chemin);
    var texte = SansCommentaires(File.ReadAllText(chemin));

    // Le balisage (ce qui devient de l'ecran) s'arrete au bloc @code.
    var debutCode = Regex.Match(texte, @"(?m)^\s*@code");
    var balisage = debutCode.Success ? texte[..debutCode.Index] : texte;
    var codeCs = debutCode.Success ? texte[debutCode.Index..] : string.Empty;

    // 1. Attributs de texte lus par l'utilisateur.
    foreach (Match m in Regex.Matches(balisage, @"(title|placeholder|alt)\s*=\s*""([^""]*)""",
                                      RegexOptions.IgnoreCase))
    {
        Exiger(!m.Groups[2].Value.Contains("PROC_"),
            $"{nom} : l'attribut {m.Groups[1].Value} « {m.Groups[2].Value} » ne montre pas de code interne");
    }

    // 2. Texte rendu entre deux balises.
    foreach (Match m in Regex.Matches(balisage, @">([^<>]*)<"))
    {
        var rendu = m.Groups[1].Value;
        if (!rendu.Contains("PROC_")) continue;
        // Un texte affiche tient sur une ligne et n'est pas du C# : ce filtre
        // ecarte les blocs @switch d'aiguillage, qui vivent dans le balisage.
        if (rendu.Contains("\n") || rendu.Contains(";")) continue;
        Exiger(false, $"{nom} : le texte affiche « {rendu.Trim()} » ne montre pas de code interne");
    }

    // 3. Hors aiguilleur : aucun code interne, meme en donnee.
    if (nom.Equals("Home.razor", StringComparison.OrdinalIgnoreCase)) continue;
    foreach (Match m in Regex.Matches(texte, @"""([^""\r\n]*PROC_[^""\r\n]*)"""))
    {
        Exiger(false,
            $"{nom} : la chaine « {m.Groups[1].Value} » porte un code interne ; " +
            "seul Home.razor aiguille par code");
    }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : aucun code interne de procedure n'atteint l'ecran."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;
