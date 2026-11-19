using RigV3Desktop;

// Controle de la tuile « Activite des utilisateurs » : le chef de greffe doit
// lire les gestes de son equipe du plus recent au plus ancien, pouvoir les
// filtrer par agent, et etre le SEUL a voir cette tuile. On verifie aussi que
// la tuile est reellement branchee dans l'accueil et que la version de
// disposition a ete montee -- sans quoi les postes deja ouverts une fois ne
// la verraient jamais apparaitre.
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
// PORTE EN REACT (lot 1 du plan « finir le passage a React », 19/11/2026) :
// la grille de l'accueil, ses poses et ses dispositions vivent dans la session
// de l'accueil (Pont/Accueil.cs, meme ecriture que dans Home.razor) ; la tuile
// est rendue par web/src/accueil/Grille.tsx et Tuiles.tsx (fonction Activite).
var vue = File.ReadAllText(Path.Combine(racine, "Pont/Accueil.cs"));
var home = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
var grilleTsx = File.ReadAllText(Path.Combine(racine, "web/src/accueil/Grille.tsx"));
var tuilesTsx = File.ReadAllText(Path.Combine(racine, "web/src/accueil/Tuiles.tsx"));
Exiger(!home.Contains("new(\"activite\","), "Home.razor ne tient plus de copie de la grille de l'accueil");

// --- 1. Le journal
var tout = JournalActions.Toutes;
Exiger(tout.Length > 0, "le journal rend au moins un geste");
Exiger(tout.All(a => !string.IsNullOrWhiteSpace(a.Verbe)
                     && !string.IsNullOrWhiteSpace(a.Objet)
                     && !string.IsNullOrWhiteSpace(a.Ecran)),
    "aucun geste vide (verbe, objet, ecran)");
Exiger(tout.All(a => JournalActions.Verbes.Contains(a.Verbe)),
    "chaque verbe appartient a la liste fermee des verbes metier");
Exiger(tout.All(a => JeuDemo.Comptes.Any(c => c.Login == a.Login)),
    "chaque geste est attribue a un compte qui existe");
Exiger(tout.All(a => a.Agent != a.Login), "chaque geste s'affiche au NOM de l'agent, pas a son login");

// L'ordre est rendu par le journal, pas par la tuile.
Exiger(tout.Zip(tout.Skip(1)).All(c => c.First.Quand >= c.Second.Quand),
    "les gestes sortent du plus recent au plus ancien");

Exiger(JournalActions.Dernieres(3).Length == 3, "« les 3 derniers » en rend exactement 3");
Exiger(JournalActions.Dernieres(3).SequenceEqual(tout.Take(3)),
    "« les derniers » sont bien le haut de la liste");
Exiger(JournalActions.Dernieres(0).Length == 0, "demander zero geste n'en rend aucun");

// --- 2. Le filtre par utilisateur
var unLogin = tout[0].Login;
var siennes = JournalActions.De(unLogin);
Exiger(siennes.Length > 0, $"le filtre rend les gestes de « {unLogin} »");
Exiger(siennes.All(a => a.Login == unLogin), "le filtre ne rend QUE les gestes de l'agent choisi");
Exiger(siennes.Length < tout.Length, "le filtre retire bien les gestes des autres agents");
Exiger(JournalActions.De("").Length == tout.Length, "un filtre vide ne cache rien");
Exiger(JournalActions.De("inconnu").Length == 0, "un login inconnu ne rend pas tout le journal");

// --- 3. Le compte du jour
var parAgent = JournalActions.ParAgentAujourdhui();
Exiger(parAgent.Length > 0, "le compte du jour rend au moins un agent");
Exiger(parAgent.Zip(parAgent.Skip(1)).All(c => c.First.Nombre >= c.Second.Nombre),
    "les agents sont classes du plus actif au moins actif");
Exiger(parAgent.Sum(x => x.Nombre) == tout.Count(a => a.Quand.Date == DateTime.Now.Date),
    "le total par agent egale le nombre de gestes du jour");
Exiger(JournalActions.Agents.Length == tout.Select(a => a.Login).Distinct().Count(),
    "la liste du filtre couvre tous les agents presents au journal");

// --- 4. QUI VOIT LA TUILE : le chef de greffe, et lui seul
var chefs = JeuDemo.Comptes.Where(JournalActions.VisiblePour).ToArray();
Exiger(chefs.Length > 0, "au moins un compte voit la tuile");
Exiger(chefs.All(c => c.PeutValider), "seuls les comptes qui VALIDENT voient la tuile");
Exiger(JeuDemo.Comptes.Where(c => !c.PeutValider).All(c => !JournalActions.VisiblePour(c)),
    "aucun redacteur ne voit le travail de ses collegues");

// --- 5. Le branchement dans l'accueil
Exiger(vue.Contains("\"activite\", \"Activite des utilisateurs\"")
       || vue.Contains("new(\"activite\","), "la tuile est declaree dans la grille de l'accueil");
Exiger(vue.Contains("corps == \"activite\" ? ActiviteTuile()")
       && grilleTsx.Contains("if (t.activite) return <Activite vue={t.activite}")
       && tuilesTsx.Contains("export function Activite("),
    "la tuile a un corps rendu dans l'accueil");
Exiger(vue.Contains("JournalActions.De(FiltreActivite)") && vue.Contains("case \"activite\": FiltreActivite =")
       && tuilesTsx.Contains("changer={(login) => geste('activite', login)}"),
    "le filtre par agent de la tuile passe par le journal");
Exiger(vue.Contains("JournalActions.VisiblePour(CompteCourant)"),
    "l'accueil filtre la tuile sur le compte connecte");
Exiger(vue.Contains("Definitions.Where(TuileAutorisee)"),
    "le panneau de choix des tuiles cache aussi la tuile non autorisee");

// La disposition par defaut change : sans montee de version, un accueil.json
// deja ecrit figerait l'ancienne grille et la tuile resterait invisible.
var version = System.Text.RegularExpressions.Regex.Match(
    vue, @"DispositionCourante\s*=\s*(\d+)");
Exiger(version.Success && int.Parse(version.Groups[1].Value) >= 3,
    "la version de disposition a ete montee (>= 3) pour reposer la grille");

// --- 6. LA GRILLE TIENT : aucune tuile n'en recouvre une autre, et tout
// entre dans l'ecran visible sans defilement. C'est le seul controle qu'une
// capture d'ecran aurait apporte ; il se calcule sur les poses declarees.
var poses = System.Text.RegularExpressions.Regex.Matches(vue,
    @"new\(""(?<id>[a-z-]+)"",\s*""[^""]*"",\s*new\(\)\s*\{\s*Colonne\s*=\s*(?<c>\d+),\s*Ligne\s*=\s*(?<l>\d+),\s*Largeur\s*=\s*(?<w>\d+),\s*Hauteur\s*=\s*(?<h>\d+)")
    .Select(m => (
        Id: m.Groups["id"].Value,
        C: int.Parse(m.Groups["c"].Value), L: int.Parse(m.Groups["l"].Value),
        W: int.Parse(m.Groups["w"].Value), H: int.Parse(m.Groups["h"].Value)))
    .ToArray();

Exiger(poses.Length >= 6, $"les 6 tuiles de l'accueil sont lues dans la grille (lu : {poses.Length})");
Exiger(poses.Any(t => t.Id == "activite"), "la pose de la tuile d'activite est declaree");
Exiger(poses.All(t => t.C >= 1 && t.C + t.W - 1 <= 12), "aucune tuile ne deborde des 12 colonnes");

// 17/09/2026 : les huit widgets METIER (un par categorie de la feuille
// « Alertes accueil v2 Rig ») n'entrent pas dans les 5 lignes visibles. La
// regle « sans defilement » est donc RESTREINTE au HAUT de l'accueil -- les
// tuiles historiques, qui ne bougent pas -- et les widgets metier se posent
// STRICTEMENT en dessous, sur des lignes >= 6. Rien n'est desserre : les deux
// moities sont controlees, et le non-chevauchement l'est sur l'ensemble.
// « A traiter BIS » (21/09/2026) est eteinte hors disposition « Bandeau » :
// sa pose par defaut est rangee sous les widgets metier.
var historiques = poses.Where(t => !t.Id.StartsWith("metier-") && t.Id != "alertes-bis").ToArray();
var metier = poses.Where(t => t.Id.StartsWith("metier-")).ToArray();
Exiger(historiques.All(t => t.L >= 1 && t.L + t.H - 1 <= 5),
    "les tuiles historiques tiennent dans les 5 lignes visibles, sans defilement");
Exiger(metier.Length == 8, $"un widget metier par categorie de la feuille (lu : {metier.Length})");
Exiger(metier.All(t => t.L >= 6), "les widgets metier se posent SOUS les tuiles historiques");

var chevauchements = new List<string>();
foreach (var a in poses)
{
    foreach (var b in poses)
    {
        if (ReferenceEquals(a, b) || string.CompareOrdinal(a.Id, b.Id) >= 0) continue;
        var seCroisent = a.C < b.C + b.W && b.C < a.C + a.W
                      && a.L < b.L + b.H && b.L < a.L + a.H;
        if (seCroisent) chevauchements.Add($"{a.Id}/{b.Id}");
    }
}
Exiger(chevauchements.Count == 0,
    "aucune tuile n'en recouvre une autre" + (chevauchements.Count > 0
        ? " (chevauchements : " + string.Join(", ", chevauchements) + ")" : ""));

// --- 7. LES DISPOSITIONS PREPAREES (21/09/2026) : dans chacune, les tuiles
// ALLUMEES ne se recouvrent pas et ne debordent pas des 12 colonnes.
var blocs = System.Text.RegularExpressions.Regex.Matches(vue,
    @"new\(""(?<id>bandeau|lateral|metiers)"",[^\[]*\[(?<eteintes>[^\]]*)\],\s*\[(?<poses>[^\]]*)\]\)");
Exiger(blocs.Count == 3, $"trois dispositions preparees declarees (lu : {blocs.Count})");
foreach (System.Text.RegularExpressions.Match bloc in blocs)
{
    var eteintes = bloc.Groups["eteintes"].Value;
    var propres = System.Text.RegularExpressions.Regex.Matches(bloc.Groups["poses"].Value,
        @"P\(""(?<id>[a-z-]+)"",\s*(?<c>\d+),\s*(?<l>\d+),\s*(?<w>\d+),\s*(?<h>\d+)\)")
        .ToDictionary(m => m.Groups["id"].Value, m => (C: int.Parse(m.Groups["c"].Value),
            L: int.Parse(m.Groups["l"].Value), W: int.Parse(m.Groups["w"].Value), H: int.Parse(m.Groups["h"].Value)));
    var allumees = poses
        .Where(t => !eteintes.Contains($"\"{t.Id}\"") && !(t.Id.StartsWith("metier-") && eteintes.Contains("TuilesMetier")))
        .Select(t => propres.TryGetValue(t.Id, out var p) ? (t.Id, p.C, p.L, p.W, p.H) : (t.Id, t.C, t.L, t.W, t.H))
        .ToArray();
    var nom = bloc.Groups["id"].Value;
    Exiger(allumees.All(t => t.C >= 1 && t.C + t.W - 1 <= 12), $"{nom} : aucune tuile ne deborde");
    var croises = new List<string>();
    foreach (var x in allumees) foreach (var y in allumees)
        if (string.CompareOrdinal(x.Id, y.Id) < 0 && x.C < y.C + y.W && y.C < x.C + x.W && x.L < y.L + y.H && y.L < x.L + x.H)
            croises.Add($"{x.Id}/{y.Id}");
    Exiger(croises.Count == 0, $"{nom} : aucune tuile allumee n'en recouvre une autre" +
        (croises.Count > 0 ? " (" + string.Join(", ", croises) + ")" : ""));
}
Exiger(vue.Contains("\"bandeau\"") && System.Text.RegularExpressions.Regex.IsMatch(vue, @"P\(""alertes-bis"",\s*1,\s*1,\s*12,"),
    "la disposition Bandeau pose A traiter BIS sur toute la largeur du haut");

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Banc ACTIVITE : VERT."); return 0; }
Console.WriteLine($"Banc ACTIVITE : {echecs.Count} ECHEC(S) -> {string.Join(" | ", echecs)}");
return 1;
