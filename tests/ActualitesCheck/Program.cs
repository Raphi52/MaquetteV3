using RigV3Desktop;

// Controle du widget ACTUALITES : des messages REDIGES (et non
// calcules) lus dans un fichier PARTAGE, avec repli hors ligne VISIBLE,
// une tuile sur l'accueil et un ecran de saisie qui publie vraiment.
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var vue = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));

// Bac a sable : JAMAIS l'etat reel de l'utilisateur.
var bac = Path.Combine(Path.GetTempPath(), "rigv3-actualites-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(bac);
var partage = Path.Combine(bac, "partage.json");
var cache = Path.Combine(bac, "cache.json");
var depot = new DepotActualites { CheminPartage = partage, CheminCache = cache };
var maintenant = new DateTime(2026, 9, 17, 10, 0, 0);

// --- 1. Les exemples dictes par le metier
var exemples = DepotActualites.Exemples(maintenant);
Exiger(exemples.Any(b => b.Titre.Contains("CHERIFI")), "l'exemple CNG (fraude CHERIFI) existe");
Exiger(exemples.Any(b => b.Message.Contains("DCADEMAT")), "l'exemple INPI (DCADEMAT suspendus) existe");
Exiger(exemples.All(b => Actualite.Statuts.Contains(b.Statut)),
    "chaque actualite porte un statut qui correspond a un filet existant");
Exiger(exemples.All(b => b.Actif(maintenant)), "les exemples sont actifs a l'heure du controle");

// --- 2. Lecture du fichier partage
Exiger(depot.Lire(maintenant).Source == SourceActualites.Aucune,
    "sans fichier ni cache, la lecture rend « aucune source » au lieu de lever");

Exiger(depot.Ecrire(exemples), "l'ecriture du fichier partage reussit");
var lu = depot.Lire(maintenant);
Exiger(lu.Source == SourceActualites.Partage, "la lecture prend le fichier partage quand il est la");
Exiger(lu.Actifs.Length == exemples.Length, "toutes les actualites actives sont rendues");
Exiger(lu.Actifs[0].Statut == "alerte", "le plus grave est affiche en tete");
Exiger(File.Exists(cache), "une lecture reussie recopie le contenu dans le cache local");

// --- 3. Repli HORS LIGNE : le reseau tombe, l'accueil tient, et ca se VOIT
File.Delete(partage);
var horsLigne = depot.Lire(maintenant);
Exiger(horsLigne.Source == SourceActualites.Cache,
    "source partagee injoignable : on se replie sur le dernier bon contenu");
Exiger(horsLigne.Actifs.Length == exemples.Length, "le repli montre les memes actualites");
Exiger(horsLigne.Etat.Contains("injoignable"),
    "l'etat degrade est ECRIT, pas avale silencieusement");

// Fichier illisible (partage corrompu) : meme repli, jamais d'exception.
File.WriteAllText(partage, "{ ceci n'est pas du json");
var corrompu = depot.Lire(maintenant);
Exiger(corrompu.Source == SourceActualites.Cache, "un fichier partage illisible se replie aussi");

// --- 3 bis. Les cinq TYPES : chacun a son filet, sa gravite et sa duree
var charte = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-charte.css"));
Exiger(TypeActualite.Tous.Length == 5, "il y a exactement cinq types d'actualite");
foreach (var t in TypeActualite.Tous)
{
    Exiger(charte.Contains(".rig-filet-v--" + t.Filet),
        "le type « " + t.Libelle + " » a son filet dans la charte");
    Exiger(charte.Contains(".rig-actualite__type--" + t.Filet),
        "le type « " + t.Libelle + " » a la couleur de son libelle");
}
Exiger(TypeActualite.Tous.Select(t => t.Gravite).Distinct().Count() == 5,
    "deux types ne peuvent pas peser la meme gravite, sinon le tri est arbitraire");
Exiger(TypeActualite.De("fraude").Gravite > TypeActualite.De("info").Gravite,
    "une fraude passe devant une info");
Exiger(TypeActualite.De("FRAUDE").Code == TypeActualite.Fraude,
    "le code du type est lu sans tenir compte de la casse");
Exiger(TypeActualite.De("type-inconnu").Code == TypeActualite.Info,
    "un type inconnu retombe sur « Info » au lieu de perdre le message");
Exiger(TypeActualite.De(null).Code == TypeActualite.Info,
    "un type absent retombe sur « Info »");
Exiger(TypeActualite.De("fraude").FinProposee(maintenant) is null,
    "une fraude est epinglee : aucune date de fin proposee");
Exiger(TypeActualite.De("info").FinProposee(maintenant) == maintenant.AddDays(7),
    "une info est proposee a 7 jours");
Exiger(TypeActualite.De("retabli").FinProposee(maintenant) == maintenant.AddDays(1),
    "un retabli ne reste qu'une journee");
Exiger(exemples.Any(b => b.Type == TypeActualite.Fraude), "l'exemple CNG est de type Fraude");
Exiger(exemples.Any(b => b.Type == TypeActualite.Panne), "l'exemple INPI est de type Panne");

// --- 4. Duree de vie
var perime = new Actualite("vieux", "Panne resolue", "Retabli", "ok", "SI",
                          maintenant.AddDays(-10), maintenant.AddDays(-3));
var futur = new Actualite("demain", "Maintenance", "Prevue", "attente", "SI",
                         maintenant.AddDays(1), null);
Exiger(!perime.Actif(maintenant), "une actualite expiree ne s'affiche plus");
Exiger(!futur.Actif(maintenant), "une actualite publiee plus tard ne s'affiche pas encore");
depot.Ecrire(exemples.Concat([perime, futur]));
var filtre = depot.Lire(maintenant);
Exiger(filtre.Actifs.Length == exemples.Length, "la tuile ne montre que les actualites actives");
Exiger(depot.Tous().Length == exemples.Length + 2,
    "l'ecran de saisie voit AUSSI les actualites expirees ou programmees");

// --- 5. La tuile est bien posee sur l'accueil
Exiger(vue.Contains("new(\"actualites\""), "la tuile « actualites » est declaree dans l'accueil");
Exiger(vue.Contains("case \"actualites\":"), "le corps de la tuile est aiguille dans le switch");
Exiger(vue.Contains("CorpsActualites"), "le corps de la tuile existe");
Exiger(vue.Contains("rig-filet-v--@b.Fiche.Filet") || vue.Contains("rig-filet-v--@(b.Fiche.Filet)"),
    "la couleur passe par le filet, comme la charte l'impose");
Exiger(vue.Contains("rig-actualite__type"), "le type est NOMME a cote de sa couleur");
Exiger(vue.Contains("EtatActualites"), "l'etat de la source est affiche dans la tuile");
Exiger(vue.Contains("PROC_ACTUALITES"), "la tuile mene a l'ecran de saisie");

// --- 6. L'ecran de saisie existe et est atteignable
var ecran = Path.Combine(racine, "Components/Ecrans/EcranActualites.razor");
Exiger(File.Exists(ecran), "l'ecran de saisie EcranActualites.razor existe");
if (File.Exists(ecran))
{
    var texte = File.ReadAllText(ecran);
    Exiger(texte.Contains("DepotActualites"), "l'ecran de saisie ecrit dans le depot partage");
    Exiger(texte.Contains("Publier") || texte.Contains("Enregistrer"),
        "l'ecran de saisie porte un geste de publication");
}
Exiger(vue.Contains("case \"PROC_ACTUALITES\":"), "l'ecran est branche dans l'aiguillage des procedures");
Exiger(vue.Contains("<EcranActualites />"), "l'ecran est rendu par l'accueil");
Exiger(vue.Contains("\"PROC_ACTUALITES\", \"Actualites\"")
       || vue.Contains("\"PROC_ACTUALITES\""), "l'ecran est nomme dans la table des procedures");

try { Directory.Delete(bac, true); } catch (Exception) { }

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Actualites : tout est vert."); return 0; }
Console.WriteLine($"Actualites : {echecs.Count} echec(s).");
return 1;
