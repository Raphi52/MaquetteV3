using RigV3Desktop;

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

try { Directory.Delete(dossierTemp, true); } catch (Exception) { }

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Banc VERT."); return 0; }
Console.WriteLine($"Banc ROUGE : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine(" - " + e);
return 1;
