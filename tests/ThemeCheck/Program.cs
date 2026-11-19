using System.IO;
using System.Text.Json;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc du THEME D'AFFICHAGE retenu d'une session a l'autre.
//
// Defaut a l'origine de ce test (17/09/2026) : le mode etait un simple
// champ de Home.razor (« ThemeSombre = true »), donc l'application
// reouvrait TOUJOURS en sombre, quel que soit le choix de l'utilisateur.
// Deux exigences : le CLAIR est le defaut d'un profil neuf, et le choix
// fait est relu au demarrage suivant.
//
// 18/11/2026 : le oui/non « ThemeSombre » devient un code de theme parmi
// cinq (ThemesRig). Deux exigences de plus : un poste reste en sombre apres
// la mise a jour (son fichier n'a que l'ancien champ), et un code inconnu
// retombe sur le clair au lieu de laisser la page sans couleurs.
//
// Le banc ecrit REELLEMENT le fichier d'etat (RIGV3_ETAT le detourne vers
// un dossier temporaire) et le relit : c'est l'aller-retour complet.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var dossierTemp = Path.Combine(Path.GetTempPath(), "rigv3-theme-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dossierTemp);
var fichier = Path.Combine(dossierTemp, "accueil.json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", fichier);

// ---------- Profil neuf : le CLAIR est le defaut ----------
Exiger(new AccueilEtat().Theme == "clair", "un etat neuf est en theme CLAIR");
Exiger(AccueilEtat.Charger().Theme == "clair",
    "sans fichier d'etat, l'application ouvre en theme CLAIR");

// ---------- La liste des reglages : cinq themes, codes uniques ----------
Exiger(ThemesRig.Tous.Select(t => t.Code).SequenceEqual(new[] { "clair", "sombre", "violet", "orange", "argile" }),
    "la liste propose clair, sombre, violet, orange et argile, dans cet ordre");
Exiger(ThemesRig.Tous.All(t => !string.IsNullOrWhiteSpace(t.Libelle)),
    "chaque theme a un libelle lisible dans la liste");

// ---------- Chaque theme choisi est retrouve au redemarrage ----------
foreach (var theme in ThemesRig.Tous)
{
    var etat = AccueilEtat.Charger();
    etat.Theme = theme.Code;
    etat.Enregistrer();
    Exiger(AccueilEtat.Charger().Theme == theme.Code,
        $"le theme « {theme.Libelle} » choisi est retrouve au redemarrage");
}
Exiger(File.ReadAllText(fichier).Contains("\"Theme\""),
    "le fichier d'etat porte le theme d'affichage");
Exiger(!File.ReadAllText(fichier).Contains("ThemeSombre"),
    "l'ancien champ ThemeSombre n'est plus ecrit");

// ---------- Retour au clair, retenu aussi ----------
var etat3 = AccueilEtat.Charger();
etat3.Theme = "clair";
etat3.Enregistrer();
Exiger(AccueilEtat.Charger().Theme == "clair", "le retour au theme CLAIR est retenu");

// ---------- Un poste d'avant la mise a jour garde son mode ----------
File.WriteAllText(fichier, "{ \"ThemeSombre\": true }");
Exiger(AccueilEtat.Charger().Theme == "sombre",
    "un ancien fichier « ThemeSombre: true » ouvre en SOMBRE");
File.WriteAllText(fichier, "{ \"ThemeSombre\": false }");
Exiger(AccueilEtat.Charger().Theme == "clair",
    "un ancien fichier « ThemeSombre: false » ouvre en CLAIR");
File.WriteAllText(fichier, "{ \"ThemeSombre\": true }");
AccueilEtat.Charger().Enregistrer();
Exiger(!File.ReadAllText(fichier).Contains("ThemeSombre") && AccueilEtat.Charger().Theme == "sombre",
    "apres reecriture, l'ancien champ disparait et le sombre reste");

// ---------- Un code inconnu ou un etat illisible retombe sur le clair ----------
File.WriteAllText(fichier, "{ \"Theme\": \"fuchsia\" }");
Exiger(AccueilEtat.Charger().Theme == "clair",
    "un code de theme inconnu retombe sur le CLAIR");
var etat4 = new AccueilEtat { Theme = "" };
Exiger(etat4.Theme == "clair", "un code vide retombe sur le CLAIR");
File.WriteAllText(fichier, "{ ceci n'est pas du json");
Exiger(AccueilEtat.Charger().Theme == "clair",
    "un etat illisible retombe sur le theme CLAIR par defaut");

// ---------- Le choix fait dans la BARRE (React) : l'aller-retour complet ----------
// 19/11/2026, lot 1 du passage a React : la liste des themes est rendue par
// web/src/composants/BarreRig.tsx ; son choix remonte a la session de l'accueil
// (Pont/Accueil.cs, geste « theme ») qui l'ecrit par AccueilEtat. On joue ce
// geste comme React, puis on rouvre une fenetre comme au redemarrage.
static JsonElement Valeur(string json) => JsonDocument.Parse(json).RootElement.Clone();
var contexte = new ContexteAccueil(false, null, RechercheTrouvee.Rien);
File.Delete(fichier);
var session = new SessionAccueil(AccueilEtat.Charger());
Exiger(session.Vue(contexte).Barre.Themes.Select(t => t.Code).SequenceEqual(ThemesRig.Tous.Select(t => t.Code)),
    "la liste de la barre propose les themes de ThemesRig, dans leur ordre");
Exiger(session.Theme == "clair" && session.Vue(contexte).Theme == "clair",
    "une premiere fenetre s'ouvre en CLAIR, et sa vue le dit");
var demande = session.Agir("theme", Valeur("\"violet\""));
Exiger(demande is { Quoi: DemandeAccueil.ThemeChange, Valeur: "violet" },
    "choisir un theme demande a la fenetre de repeindre son fond (FenetreInterop.AppliquerFond)");
var vueTheme = session.Vue(contexte);
Exiger(vueTheme.Theme == "violet" && vueTheme.Barre.Theme == "violet",
    "la vue rendue a React porte le theme choisi (liste de la barre et data-rig-theme)");
Exiger(AccueilEtat.Charger().Theme == "violet", "le theme choisi dans la barre est ecrit dans accueil.json");
Exiger(new SessionAccueil(AccueilEtat.Charger()).Theme == "violet",
    "une nouvelle fenetre (redemarrage) rouvre dans le theme choisi");
session.Agir("theme", Valeur("\"fuchsia\""));
Exiger(session.Theme == "clair" && AccueilEtat.Charger().Theme == "clair",
    "un code inconnu venu de la liste retombe sur le CLAIR, et c'est le clair qui est retenu");
session.Agir("theme", Valeur("null"));
Exiger(session.Theme == "clair", "un choix vide retombe sur le CLAIR");

// ---------- Ce que React fait du theme ----------
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var themeTsx = File.ReadAllText(Path.Combine(racine, "web/src/composants/ThemeRig.tsx"));
// Lot 2 du passage a React (19/11/2026) : la page entiere est l'application React
// (web/src/composants/Application.tsx), le fond WPF suit la session du plan de
// travail (Pont/PlanTravail.cs, le @code de Home.razor deplace tel quel).
var home = File.ReadAllText(Path.Combine(racine, "web/src/composants/Application.tsx"));
var plan = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
Exiger(themeTsx.Contains("document.documentElement.setAttribute('data-rig-theme', theme)"),
    "<html> recoit le theme (sinon le fond de la page hote cerne le clair d'un cadre sombre)");
// L'ile du theme n'est sous AUCUNE condition : la ligne qui la precede n'ouvre pas de bloc.
var lignes = home.Split('\n').Select(l => l.Trim()).ToList();
var ileTheme = lignes.FindIndex(l => l.StartsWith("<ThemeRig theme="));
Exiger(ileTheme > 0 && lignes[ileTheme] == "<ThemeRig theme={v.theme} />"
       && !lignes[ileTheme - 1].EndsWith("&&") && !lignes[ileTheme - 1].EndsWith("(") && !lignes[ileTheme - 1].StartsWith("{"),
    "le theme est pose dans TOUTES les fenetres, y compris celle d'un onglet sorti");
Exiger(plan.Contains("case Pont.DemandeAccueil.ThemeChange: Fenetre?.AppliquerFond(Theme);")
       && System.Text.RegularExpressions.Regex.Matches(plan, @"Fenetre\?\.AppliquerFond\(Theme\);").Count >= 2,
    "le fond de la fenetre WPF suit le theme au demarrage et a chaque choix");

try { Directory.Delete(dossierTemp, true); } catch (Exception) { }

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Banc VERT."); return 0; }
Console.WriteLine($"Banc ROUGE : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine(" - " + e);
return 1;
