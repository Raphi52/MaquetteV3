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
//  3. hors de l'AIGUILLEUR -- le seul fichier ou les codes servent de cle de
//     navigation -- aucune chaine litterale ne contient PROC_. Depuis le lot 2
//     du passage a React (19/11/2026), l'aiguilleur est web/src/plan/Aiguillage.tsx
//     (le @switch de Home.razor) ; Home.razor, devenu coquille, perd son exemption.
// Les commentaires (/* */ et //) sont exclus : la tracabilite vers les
// sources v2 est utile et invisible a l'utilisateur.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// RETRAIT DE BLAZOR (lot 3 du plan « finir le passage a React », 19/11/2026) :
// plus aucun ecran .razor. Les trois regles se tiennent sur les ecrans React
// ci-dessous ; on s'assure qu'aucun .razor n'est revenu, qui y echapperait.
Exiger(!Directory.EnumerateFiles(racine, "*.razor", SearchOption.AllDirectories)
            .Any(f => !Regex.IsMatch(f, @"[\\/](bin|obj|node_modules|binverif)[\\/]")),
    "plus aucun ecran .razor : les regles se lisent sur les ecrans React");

// --- Les ecrans PORTES EN REACT (web/src, 18/11/2026) : memes trois regles.
// Un seul fichier de web/src aiguille par code : web/src/plan/Aiguillage.tsx
// (lot 2) ; la regle 3 vaut partout ailleurs. Commentaires exclus
// (/* */, {/* */} et //) : la tracabilite vers PROC_xxx de RIG v2 y reste utile.
static string SansCommentairesTs(string texte)
{
    var t = Regex.Replace(texte, @"/\*.*?\*/", " ", RegexOptions.Singleline);
    t = Regex.Replace(t, @"(?m)^\s*//.*$", " ");
    return t;
}

var racineWeb = Path.Combine(racine, "web/src");
var fichiersWeb = Directory.Exists(racineWeb)
    ? Directory.GetFiles(racineWeb, "*.ts*", SearchOption.AllDirectories)
               .Where(f => f.EndsWith(".ts") || f.EndsWith(".tsx"))
               .Where(f => !Regex.IsMatch(f, @"\.test\.tsx?$"))
               .ToArray()
    : [];
Exiger(fichiersWeb.Any(f => f.EndsWith(".tsx")), $"des ecrans React .tsx sont trouves ({fichiersWeb.Length} fichiers lus)");
var aiguilleur = fichiersWeb.SingleOrDefault(f => Path.GetFileName(f) == "Aiguillage.tsx"
                                                && Path.GetFileName(Path.GetDirectoryName(f)) == "plan");
Exiger(aiguilleur is not null && Regex.Matches(File.ReadAllText(aiguilleur), @"case '(PROC_[A-Z_]+|BODACC|OUVERTURE_DOSSIER)':").Count > 20,
    "l'aiguillage des ecrans est web/src/plan/Aiguillage.tsx, et il aiguille par code");

foreach (var chemin in fichiersWeb)
{
    var nom = Path.GetFileName(chemin);
    var texte = SansCommentairesTs(File.ReadAllText(chemin));

    // 1. Attributs de texte lus par l'utilisateur : "..." ou {'...'} / {`...`}.
    foreach (Match m in Regex.Matches(texte,
                 @"(title|placeholder|alt|aria-label)\s*=\s*(?:""(?<v>[^""]*)""|\{\s*(?<q>['`])(?<v>[^'`]*)\k<q>\s*\})",
                 RegexOptions.IgnoreCase))
    {
        Exiger(!m.Groups["v"].Value.Contains("PROC_"),
            $"{nom} : l'attribut {m.Groups[1].Value} « {m.Groups["v"].Value} » ne montre pas de code interne");
    }

    // 2. Texte rendu entre deux balises (un texte sur plusieurs lignes ou avec « ; » est du code).
    foreach (Match m in Regex.Matches(texte, @">([^<>{}]*)<"))
    {
        var rendu = m.Groups[1].Value;
        if (!rendu.Contains("PROC_")) continue;
        if (rendu.Contains("\n") || rendu.Contains(";")) continue;
        Exiger(false, $"{nom} : le texte affiche « {rendu.Trim()} » ne montre pas de code interne");
    }

    // 3. Aucune chaine litterale ("...", '...' ou `...`) ne porte de code interne,
    // sauf dans l'aiguilleur, ou les codes sont les cles de navigation.
    if (chemin == aiguilleur) continue;
    foreach (Match m in Regex.Matches(texte, @"([""'`])(?<v>[^""'`\r\n]*PROC_[^""'`\r\n]*)\1"))
    {
        Exiger(false,
            $"{nom} : la chaine « {m.Groups["v"].Value} » porte un code interne ; " +
            "seul l'aiguillage (web/src/plan/Aiguillage.tsx) aiguille par code");
    }
}

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "TOUT VERT : aucun code interne de procedure n'atteint l'ecran."
    : $"{echecs.Count} ECHEC(S).");
return echecs.Count == 0 ? 0 : 1;
