using System.Text.Json;
using System.Text.RegularExpressions;
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
// Lot 2 du passage a React (19/11/2026) : l'aiguillage des ecrans est
// web/src/plan/Aiguillage.tsx, la table des procedures est dans Pont/PlanTravail.cs.
var aiguillage = File.ReadAllText(Path.Combine(racine, "web/src/plan/Aiguillage.tsx"));
// Le catalogue des ecrans vit dans Pont/CatalogueEcrans.cs depuis le 20/11/2026 (sorti de
// PlanTravail.cs, lot 6 du plan « Vue du dossier ») : il est lu avec lui.
var vue = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs")) + Environment.NewLine + File.ReadAllText(Path.Combine(racine, "Pont/CatalogueEcrans.cs"));

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
// PORTE EN REACT (lot 1 du plan « finir le passage a React », 19/11/2026) :
// la grille de l'accueil est rendue par web/src/accueil/Grille.tsx, le corps de
// la tuile par Tuiles.tsx (fonction Actualites) ; la releve, la vue et le geste
// « Rediger » vivent dans la session de l'accueil (Pont/Accueil.cs). Memes
// regles, lues dans ces fichiers.
var pontAccueil = File.ReadAllText(Path.Combine(racine, "Pont/Accueil.cs"));
var grilleTsx = File.ReadAllText(Path.Combine(racine, "web/src/accueil/Grille.tsx"));
var tuilesTsx = File.ReadAllText(Path.Combine(racine, "web/src/accueil/Tuiles.tsx"));
Exiger(pontAccueil.Contains("new(\"actualites\""), "la tuile « actualites » est declaree dans l'accueil");
Exiger(pontAccueil.Contains("corps == \"actualites\" ? ActualitesTuile()")
       && grilleTsx.Contains("if (t.actualites) return <Actualites vue={t.actualites}"),
    "le corps de la tuile est aiguille dans le switch");
Exiger(tuilesTsx.Contains("export function Actualites("), "le corps de la tuile existe");
Exiger(tuilesTsx.Contains("className={'rig-filet-v rig-filet-v--' + b.filet}")
       && pontAccueil.Contains("new ActualiteVue(b.Fiche.Filet,"),
    "la couleur passe par le filet, comme la charte l'impose");
Exiger(tuilesTsx.Contains("'rig-actualite__type rig-actualite__type--' + b.filet}>{b.type}"),
    "le type est NOMME a cote de sa couleur");
Exiger(pontAccueil.Contains("EtatActualites") && tuilesTsx.Contains("{vue.etat}"),
    "l'etat de la source est affiche dans la tuile");
Exiger(pontAccueil.Contains("case \"rediger\": return new(DemandeAccueil.OuvrirCode, \"PROC_ACTUALITES\");")
       && tuilesTsx.Contains("onClick={() => geste('rediger')}"),
    "la tuile mene a l'ecran de saisie");
Exiger(pontAccueil.Contains("Actualites.Lire(maintenant)") && pontAccueil.Contains("DepotActualites.Exemples(maintenant)"),
    "la tuile lit le depot partage, et l'ensemence au premier demarrage");

// --- 6. L'ecran de saisie existe et est atteignable.
// PORTE EN REACT (lot 3, 19/11/2026) : EcranActualites.razor n'est plus qu'une
// coquille qui monte web/src/ecrans/EcranActualites.tsx ; la liste et le depot
// partage vivent dans Pont/EcranActualites.cs (ecran a session). Ce que ce banc
// lisait dans le .razor -- l'ecran ecrit dans le depot partage, il porte un
// geste de publication -- il le tient sur la chaine reelle, et l'EXECUTE.
// RETRAIT DE BLAZOR (lot 3 du plan « finir le passage a React », 19/11/2026) :
// la coquille .razor n'existe plus, l'aiguillage React monte l'ecran directement.
Exiger(!File.Exists(Path.Combine(racine, "Components/Ecrans/EcranActualites.razor")) && aiguillage.Contains("<EcranActualites"),
    "l'ecran de saisie est atteignable : l'aiguillage React le monte (plus de coquille .razor)");
Exiger(aiguillage.Contains("<EcranActualites />"),
    "l'aiguillage monte l'ecran React du meme nom, sans parametre comme avant");
var cheminTsx = Path.Combine(racine, "web/src/ecrans/EcranActualites.tsx");
Exiger(File.Exists(cheminTsx), "l'ecran React de saisie existe (web/src/ecrans/EcranActualites.tsx)");
var tsx = File.Exists(cheminTsx) ? File.ReadAllText(cheminTsx) : string.Empty;
var pontActu = File.ReadAllText(Path.Combine(racine, "Pont/EcranActualites.cs"));
Exiger(pontActu.Contains("DepotActualites"), "l'ecran de saisie ecrit dans le depot partage");
Exiger(tsx.Contains("geste('Publier')") && tsx.Contains("Publier l'actualite"),
    "l'ecran de saisie porte un geste de publication");
Exiger(tsx.Contains("'EcranActualites.Ouvrir'") && tsx.Contains("'EcranActualites.Agir'")
       && tsx.Contains("'EcranActualites.Fermer'"),
    "l'ecran React ouvre, pilote et ferme sa session par le pont");

// Le geste reel, sur un depot JETABLE : publier ecrit le fichier partage.
JsonElement Valeur(object? v) => JsonSerializer.SerializeToElement(v);
var depotEcran = new DepotActualites { CheminPartage = Path.Combine(bac, "ecran-partage.json"),
                                       CheminCache = Path.Combine(bac, "ecran-cache.json") };
var v0 = RigV3Desktop.Pont.EcranActualites.Ouvrir(depotEcran);
Exiger(v0.Liste.Length == 2 && v0.Liste.Any(l => l.Titre.Contains("CHERIFI")),
    "depot vierge : l'ecran part des deux exemples du metier");
Exiger(!v0.PeutPublier, "sans titre, « Publier » est grise");
Exiger(v0.Types.Length == TypeActualite.Tous.Length && v0.Types.All(t => t.Aide.Length > 0),
    "chaque type a son bouton et son infobulle");
var s = v0.Session;
RigV3Desktop.Pont.EcranActualites.Agir(s, "Titre", Valeur("Coupure INPI"));
var v1 = RigV3Desktop.Pont.EcranActualites.Agir(s, "ChoisirType", Valeur(TypeActualite.Panne));
Exiger(v1.Type == TypeActualite.Panne && v1.Jours == "30" && v1.PeutPublier,
    "choisir un type repositionne la duree proposee (Panne : 30 jours)");
var v2 = RigV3Desktop.Pont.EcranActualites.Agir(s, "Jours", Valeur("12a"));
Exiger(v2.Jours == "30", "une duree illisible garde la derniere valeur lisible, comme le @bind d'un int");
var v3 = RigV3Desktop.Pont.EcranActualites.Agir(s, "Publier", Valeur(null));
var ecrites = depotEcran.Tous();
Exiger(ecrites.Length == 3 && ecrites[0].Titre == "Coupure INPI" && ecrites[0].Type == TypeActualite.Panne
       && ecrites[0].Statut == "alerte",
    "« Publier » ECRIT le fichier partage, en tete, avec le type et le statut choisis");
Exiger(v3.CompteRendu.StartsWith("Actualite publiee") && v3.StatutRendu == "ok" && v3.Titre == "",
    "la publication est confirmee et le titre est vide pour la suivante");
var v4 = RigV3Desktop.Pont.EcranActualites.Agir(s, "Retirer", Valeur(0));
Exiger(v4.Liste.Length == 2 && depotEcran.Tous().Length == 2 && v4.CompteRendu == "Actualite retiree de l'accueil.",
    "« Retirer » enleve l'actualite du fichier partage");
RigV3Desktop.Pont.EcranActualites.Fermer(s);

// Un depot qu'on ne peut pas ecrire ne se tait pas.
var bloque = Path.Combine(bac, "fichier-bloquant");
File.WriteAllText(bloque, "pas un dossier");
var depotBloque = new DepotActualites { CheminPartage = Path.Combine(bloque, "sous", "partage.json"),
                                        CheminCache = Path.Combine(bloque, "sous", "cache.json") };
var sb = RigV3Desktop.Pont.EcranActualites.Ouvrir(depotBloque).Session;
RigV3Desktop.Pont.EcranActualites.Agir(sb, "Titre", Valeur("Rien ne part"));
var vb = RigV3Desktop.Pont.EcranActualites.Agir(sb, "Publier", Valeur(null));
Exiger(vb.StatutRendu == "alerte" && vb.CompteRendu.StartsWith("ECHEC"),
    "un echec d'ecriture est DIT au redacteur : rien n'est parti");
RigV3Desktop.Pont.EcranActualites.Fermer(sb);
Exiger(aiguillage.Contains("case 'PROC_ACTUALITES':"), "l'ecran est branche dans l'aiguillage des procedures");
Exiger(Regex.IsMatch(aiguillage, @"case 'PROC_ACTUALITES':\s*return <EcranActualites />"), "l'ecran est rendu par le plan de travail");
Exiger(vue.Contains("\"PROC_ACTUALITES\", \"Actualites\"")
       || vue.Contains("\"PROC_ACTUALITES\""), "l'ecran est nomme dans la table des procedures");

try { Directory.Delete(bac, true); } catch (Exception) { }

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Actualites : tout est vert."); return 0; }
Console.WriteLine($"Actualites : {echecs.Count} echec(s).");
return 1;
