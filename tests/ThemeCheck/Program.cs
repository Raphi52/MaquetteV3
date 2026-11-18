using RigV3Desktop;

// Banc du MODE D'AFFICHAGE retenu d'une session a l'autre.
//
// Defaut a l'origine de ce test (17/09/2026) : le mode etait un simple
// champ de Home.razor (« ThemeSombre = true »), donc l'application
// reouvrait TOUJOURS en sombre, quel que soit le choix de l'utilisateur.
// Deux exigences : le CLAIR est le defaut d'un profil neuf, et le choix
// fait est relu au demarrage suivant.
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
Exiger(!new AccueilEtat().ThemeSombre, "un etat neuf est en mode CLAIR");
Exiger(!AccueilEtat.Charger().ThemeSombre,
    "sans fichier d'etat, l'application ouvre en mode CLAIR");

// ---------- Session 1 : l'utilisateur bascule en sombre ----------
var etat1 = AccueilEtat.Charger();
etat1.ThemeSombre = true;
etat1.Enregistrer();

Exiger(File.Exists(fichier), "le choix est REELLEMENT ecrit sur disque");
Exiger(File.ReadAllText(fichier).Contains("ThemeSombre"),
    "le fichier d'etat porte le mode d'affichage");

// ---------- Session 2 : l'application redemarre ----------
Exiger(AccueilEtat.Charger().ThemeSombre, "le mode SOMBRE choisi est retrouve au redemarrage");

// ---------- Session 3 : retour au clair, retenu aussi ----------
var etat3 = AccueilEtat.Charger();
etat3.ThemeSombre = false;
etat3.Enregistrer();
Exiger(!AccueilEtat.Charger().ThemeSombre, "le retour au mode CLAIR est retenu");

// ---------- Un etat illisible ne doit pas forcer le sombre ----------
File.WriteAllText(fichier, "{ ceci n'est pas du json");
Exiger(!AccueilEtat.Charger().ThemeSombre,
    "un etat illisible retombe sur le mode CLAIR par defaut");

try { Directory.Delete(dossierTemp, true); } catch (Exception) { }

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Banc VERT."); return 0; }
Console.WriteLine($"Banc ROUGE : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine(" - " + e);
return 1;
