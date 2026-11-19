using System.Text.RegularExpressions;
using RigV3Desktop;

// Garde-fou des CONTROLES DE SAISIE.
// Un controle qui accepte tout ne se voit pas a l'ecran : il faut lui
// soumettre des valeurs FAUSSES et exiger qu'il les refuse.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var ajd = new DateTime(2026, 9, 9);
string? R(string controle, string valeur) => ValiditeSaisie.Refus(controle, valeur, ajd);

// --- 1. SIREN : cle de Luhn, algorithme repris de RIG v2
// 552 100 554 (Renault) et 404 833 048 (Google France) sont des SIREN
// reels dont la cle est juste.
Exiger(ValiditeSaisie.LuhnValide("552100554"), "un SIREN reel passe la cle (552 100 554)");
Exiger(ValiditeSaisie.LuhnValide("404833048"), "un second SIREN reel passe la cle (404 833 048)");
Exiger(!ValiditeSaisie.LuhnValide("552100555"), "un chiffre modifie casse la cle");
Exiger(!ValiditeSaisie.LuhnValide("55210055A"), "une lettre est refusee");

Exiger(R("siren", "552 100 554") is null, "les espaces de presentation sont tolerés");
Exiger(R("siren", "55210055") == "le SIREN compte 9 chiffres", "8 chiffres : refuse pour la longueur");
Exiger(R("siren", "123456789") == "cle de controle du SIREN incorrecte", "9 chiffres mais cle fausse : refuse");
Exiger(R("siren", "") is null, "un champ vide n'est pas une valeur refusee");

// --- 2. Dates
Exiger(R("date-passee", "2024-03-12") is null, "une date passee est acceptee");
Exiger(R("date-passee", "2030-01-01") == "date posterieure a aujourd'hui", "une date future est refusee");
Exiger(R("date-passee", "1500-01-01") == "date anterieure au registre", "une date absurde est refusee");
Exiger(R("date-passee", "pas une date") == "date illisible", "un texte quelconque est refuse");

// --- 3. Coherence entre deux dates
Exiger(ValiditeSaisie.RefusOrdre("2024-03-12", "2024-03-01", "la date d'immatriculation") is not null,
    "une date d'effet anterieure a l'immatriculation est refusee");
Exiger(ValiditeSaisie.RefusOrdre("2024-03-12", "2024-04-01", "la date d'immatriculation") is null,
    "une date d'effet posterieure passe");
Exiger(ValiditeSaisie.RefusOrdre("2024-03-12", "", "x") is null,
    "tant que la seconde date est vide, on ne reproche rien");

// --- 4. Code NAF
Exiger(R("naf", "6202A") is null, "6202A est un code bien forme");
Exiger(R("naf", "620A2") is not null, "les chiffres et la lettre ne sont pas interchangeables");
Exiger(R("naf", "6202") is not null, "il manque la lettre : refuse");

// --- 5. Montants et taux
Exiger(R("montant", "10000") is null, "un capital positif passe");
Exiger(R("montant", "-5") is not null, "un capital negatif est refuse");
Exiger(R("taux", "100") is null, "un taux de 100 % passe");
Exiger(R("taux", "140") is not null, "un taux au-dela de 100 % est refuse");

// --- 6. Les controles sont bien BRANCHES sur les champs de la vue
// Depuis le lot 2 du passage a React (19/11/2026) : les etapes, leurs champs et
// le controle final vivent dans Pont/PlanTravail.cs (le @code de Home.razor,
// deplace tel quel) ; la vue en est calculee par Pont/PlanTravailVue.cs et
// rendue par web/src/plan/Etapes.tsx.
var racineSrc = Path.Combine(AppContext.BaseDirectory, "../../../../../");
var vue = File.ReadAllText(Path.Combine(racineSrc, "Pont/PlanTravail.cs"));
var calculVue = File.ReadAllText(Path.Combine(racineSrc, "Pont/PlanTravailVue.cs"));
var etapesTsx = File.ReadAllText(Path.Combine(racineSrc, "web/src/plan/Etapes.tsx"));
Exiger(Regex.IsMatch(vue, @"""DSSRC_SIREN""[^)]*Controle:\s*""siren"""),
    "le SIREN du dossier porte le controle siren");
Exiger(Regex.IsMatch(vue, @"""ACTVT_CODE_NAF""[^)]*Controle:\s*""naf"""),
    "le code NAF porte son controle");
Exiger(Regex.IsMatch(vue, @"""DRGNT_DATE_ENTREE""[^)]*Controle:\s*""date-passee"""),
    "la date d'entree d'un dirigeant ne peut pas etre future");
Exiger(calculVue.Contains("var (manquants, refuses) = Defauts(etape);")
       && calculVue.Contains("refuses, manquants, remplis, total);"),
    "la frise interroge bien les defauts de chaque etape");
Exiger(etapesTsx.Contains("rig-frise__requis") && etapesTsx.Contains("rig-frise__defaut")
       && Regex.IsMatch(etapesTsx, @"p\.refuses > 0\s*\?\s*<span className=""rig-frise__defaut"">[\s\S]{0,200}?: p\.manquants > 0\s*\?\s*<span className=""rig-frise__requis"">"),
    "la frise distingue requis manquants et valeurs refusees");

// --- 7. Le controle final : recapitulatif ET verrou de validation
Exiger(vue.Contains("private List<Defaut> TousLesDefauts()")
       && Regex.IsMatch(calculVue, @"var defauts = TousLesDefauts\(\);\s*return new RecapVue\(false, defauts\.Count,"),
    "le controle final rassemble les defauts des 7 etapes");
Exiger(Regex.IsMatch(etapesTsx, @"const n = recap\.nombre[\s\S]{0,1500}?disabled=\{n > 0\}\s*onClick=\{\(\) => plan\('valider'\)\}"),
    "le bouton Valider est eteint tant qu il reste un defaut");
// Le bouton desactive ne suffit pas : la methode doit refuser d elle-meme.
Exiger(Regex.IsMatch(vue, @"ValiderDossier\(\)\s*\{\s*(//[^\r\n]*\r?\n\s*)*if \(TousLesDefauts\(\)\.Count > 0\) return;"),
    "ValiderDossier refuse aussi de lui-meme, sans dependre du bouton");
Exiger(vue.Contains("Etat.Valides.Add(CleAffaire)") && vue.Contains("Etat.Enregistrer();"),
    "une validation est conservee, pas seulement affichee");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("Controles de saisie : tout est vert.");
    return 0;
}
Console.WriteLine($"Controles de saisie : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
