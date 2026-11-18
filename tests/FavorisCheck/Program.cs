using RigV3Desktop;

// Garde-fou des FAVORIS du menu de gauche : une etoile allumee doit survivre
// a la fermeture de l'application, et un accueil.json ecrit AVANT l'arrivee
// des favoris doit encore se lire.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// On n'ecrit JAMAIS dans l'etat reel de l'utilisateur.
var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-favoris-check.json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
if (File.Exists(etatBanc)) File.Delete(etatBanc);

// --- 1. Un profil neuf n'a aucun favori
var etat = AccueilEtat.Charger();
Exiger(etat.VuesFavorites.Count == 0, "profil neuf : aucun favori");
Exiger(!etat.EstFavori("PROC_XAFF"), "aucun ecran n'est etoile au depart");

// --- 2. Allumer l'etoile
Exiger(etat.BasculerFavori("PROC_XAFF"), "le premier clic allume l'etoile");
Exiger(etat.EstFavori("PROC_XAFF"), "l'ecran est desormais favori");

// --- 3. Le choix survit a la fermeture (relecture du fichier)
var relu = AccueilEtat.Charger();
Exiger(relu.EstFavori("PROC_XAFF"), "le favori est retrouve a la session suivante");

// --- 4. L'ordre des etoiles est conserve
relu.BasculerFavori("PROC_NF");
var relu2 = AccueilEtat.Charger();
Exiger(relu2.VuesFavorites.SequenceEqual(new[] { "PROC_XAFF", "PROC_NF" }),
    "les favoris gardent l'ordre ou ils ont ete etoiles");

// --- 5. Re-cliquer eteint l'etoile, et seulement celle-la
Exiger(!relu2.BasculerFavori("PROC_XAFF"), "le second clic eteint l'etoile");
var relu3 = AccueilEtat.Charger();
Exiger(!relu3.EstFavori("PROC_XAFF"), "l'ecran n'est plus favori apres relecture");
Exiger(relu3.EstFavori("PROC_NF"), "l'autre favori n'a pas ete touche");

// --- 6. Un meme ecran ne peut pas etre etoile deux fois
relu3.BasculerFavori("PROC_NF");
relu3.BasculerFavori("PROC_NF");
Exiger(AccueilEtat.Charger().VuesFavorites.Count(c => c == "PROC_NF") == 1,
    "pas de doublon dans la liste des favoris");

// --- 7. Un accueil.json d'AVANT les favoris se lit encore
File.WriteAllText(etatBanc, "{ \"ThemeSombre\": true }");
var ancien = AccueilEtat.Charger();
Exiger(ancien.ThemeSombre, "un ancien fichier de reglages se lit toujours");
Exiger(ancien.VuesFavorites is { Count: 0 }, "sans le champ, la liste part vide (jamais nulle)");

// --- 8. Un code vide n'entre pas dans la liste
Exiger(!ancien.BasculerFavori("  "), "un code vide n'est pas etoile");
Exiger(ancien.VuesFavorites.Count == 0, "la liste reste vide");

// --- 9. Deux fenetres = deux copies : l'etoile d'une vue detachee atteint la principale
var principale = AccueilEtat.Charger();
var detachee = AccueilEtat.Charger();
var recus = 0;
Action<AccueilEtat, List<string>> abonne = (src, liste) =>
{   // meme regle que Home.SurFavorisChanges
    if (ReferenceEquals(src, principale)) return;
    principale.VuesFavorites = liste; recus++;
};
AccueilEtat.FavorisChanges += abonne;
detachee.BasculerFavori("PROC_IPE");
Exiger(recus == 1, "la fenetre principale recoit le signal");
Exiger(principale.EstFavori("PROC_IPE"), "la principale voit le favori pose dans la detachee");
principale.Enregistrer();
Exiger(AccueilEtat.Charger().EstFavori("PROC_IPE"), "la principale n'ecrase plus le favori en enregistrant");
principale.BasculerFavori("PROC_IPE");
Exiger(recus == 1, "la principale ne se notifie pas elle-meme");
detachee.VuesFavorites.Add("X"); detachee.VuesFavorites.Remove("X");
Exiger(!principale.EstFavori("PROC_IPE"), "la liste recue est une copie, pas partagee");
AccueilEtat.FavorisChanges -= abonne;

if (File.Exists(etatBanc)) File.Delete(etatBanc);

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "VERT : les favoris du menu tiennent d'une session a l'autre."
    : $"ROUGE : {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;
