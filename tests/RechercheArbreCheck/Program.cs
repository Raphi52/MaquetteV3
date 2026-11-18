using RigV3Desktop;

// Garde-fou de la recherche d'un ELEMENT dans l'arbre GED.
//
// Defaut a l'origine de ce test (17/09/2026) : taper un numero de depot
// (« 2024/0141 »), de formalite (« 2024/00218 ») ou une liasse dans la
// barre de recherche de l'accueil ne rendait RIEN — seules les affaires
// (numero, objet, siren) etaient cherchees. L'utilisateur attend d'etre
// emmene dans l'arbre, deplie sur l'element tape.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racine = ArbreDemo.Racine("2026B01067", "AMIENS BILLARDS", "109 678 235");

string[]? Cherche(string terme)
    => RechercheArbre.Chemins(racine, terme).FirstOrDefault();

// --- 1. Les separateurs ne comptent pas : un numero se tape comme on veut.
foreach (var frappe in new[] { "2024/0141", "2024 0141", "20240141" })
{
    var c = Cherche(frappe);
    Exiger(c is not null && c[^1].Contains("2024/0141"),
        $"« {frappe} » trouve le depot 2024/0141");
}

// --- 2. Formalite et liasse sont trouvees elles aussi.
var formalite = Cherche("2024/00218");
Exiger(formalite is not null && formalite[^1].Contains("Formalite"),
    "un numero de formalite trouve son sous-dossier");
Exiger(Cherche("J00092709138") is not null,
    "un numero de formalite judiciaire est trouve");
Exiger(RechercheArbre.Chemins(racine, "Liasse greffe").Count > 0,
    "une liasse greffe est trouvee");

// --- 3. Le chemin rendu part de la racine : il sert a DEPLIER l'arbre.
var depot = Cherche("2024/0141");
Exiger(depot is not null && depot.Length >= 2 && depot[0] == "Dossier RCS",
    "le chemin rendu commence a la premiere branche (Dossier RCS)");

// --- 4. Ce chemin se rejoue dans un arbre RECONSTRUIT (l'ouverture d'une
//        affaire rebatit l'arbre : une reference d'objet ne survivrait pas).
var neuf = ArbreDemo.Racine("2026B01067", "AMIENS BILLARDS", "109 678 235");
var suite = depot is null ? null : RechercheArbre.Suivre(neuf, depot);
Exiger(suite is not null && suite.Length == depot!.Length
       && suite[^1].Libelle == depot[^1],
    "le chemin se resout dans un arbre reconstruit (parents + element)");
Exiger(RechercheArbre.Suivre(neuf, ["Branche inexistante"]) is null,
    "un chemin disparu rend null au lieu de planter");

// --- 5. Une frappe trop courte ne deverse pas tout l'arbre.
Exiger(RechercheArbre.Chemins(racine, "a").Count == 0,
    "moins de 2 caracteres utiles ne rend aucun element");
Exiger(RechercheArbre.Chemins(racine, "e").Count == 0,
    "une seule lettre ne rend aucun element");

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("RechercheArbreCheck : VERT"); return 0; }
Console.WriteLine($"RechercheArbreCheck : {echecs.Count} echec(s)");
return 1;
