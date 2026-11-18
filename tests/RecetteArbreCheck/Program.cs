using RigV3Desktop;

// Banc de controle du MODE TACHE vu depuis l'arbre (demande du 17/09/2026,
// affinements A/B/D/E/F). La preuve tenue ici est la PRESENCE DE LA PIECE
// dans l'arbre : une piece que la recette nomme reste visible, une piece
// qu'elle ne nomme pas se replie, et « Afficher tout le dossier » la rend.
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racineSrc = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var vue = File.ReadAllText(Path.Combine(racineSrc, "Components/Pages/Home.razor"));
var noeudVue = File.ReadAllText(Path.Combine(racineSrc, "Components/ArbreGedNoeud.razor"));
var css = File.ReadAllText(Path.Combine(racineSrc, "wwwroot/rig-accueil.css"));

var deno = RecettesTaches.Deno;
var arbre = ArbreDemo.Racine("2026B01067", "AMIENS BILLARDS", "109 678 235");
string[] domaines = ["RCS", "JUD"];

// Parcours de l'arbre AVEC la recette donnee : ce que l'ecran montrerait.
IEnumerable<NoeudGed> Visible(RecetteTache? recette)
{
    var pile = new Stack<NoeudGed>();
    pile.Push(arbre);
    while (pile.Count > 0)
    {
        var n = pile.Pop();
        foreach (var e in AccesGed.EnfantsVisibles(n, domaines, null, null, recette))
        {
            yield return e;
            pile.Push(e);
        }
    }
}
bool Presente(RecetteTache? r, string libelle)
    => Visible(r).Any(n => n.Libelle.Contains(libelle, StringComparison.OrdinalIgnoreCase));

// --- A. Le filtre descend A LA PIECE
Exiger(deno.PiecesAttendues.Length > 0, "A : la recette DENO nomme les pieces qu'elle garde");
Exiger(Presente(deno, "Avis BODACC"), "A : une piece NOMMEE par la recette reste dans l'arbre");
Exiger(!Presente(deno, "Piece d'identite du president"),
    "A : une piece NON nommee se replie (avant, tout le dossier restait visible)");
Exiger(!Presente(deno, "Certificat d'inscription"),
    "A : une piece non nommee d'une branche GARDEE se replie aussi");
Exiger(Presente(deno, "Extrait Kbis"), "A : la piece de reference du dossier reste visible");
Exiger(Presente(deno, InformationsDossier.LibelleNoeud),
    "A : la fiche du dossier -- qui porte les champs -- n'est jamais repliee");
Exiger(Presente(null, "Piece d'identite du president"),
    "A : « Afficher tout le dossier » (recette nulle) rend la piece repliee");
Exiger(deno.GardePiece("Avis BODACC - immatriculation") && !deno.GardePiece("Document numerise"),
    "A : GardePiece tranche sur le libelle de la piece");
// 17/09/2026 : la recette Dirigeant NOMME desormais ses pieces (elle etait la
// seule a laisser tout le dossier a l'ecran). Le comportement de repli « sans
// pieces nommees » reste couvert, mais sur une recette CONSTRUITE ici : le
// controle ne depend plus du fait qu'une recette reelle soit restee incomplete.
Exiger(RecettesTaches.Dirigeant.PiecesAttendues.Length > 0,
    "A : la recette Dirigeant nomme aussi les pieces qu'elle garde");
var sansPieces = RecettesTaches.Dirigeant with { PiecesGardees = null };
Exiger(sansPieces.PiecesGardees is null && sansPieces.GardePiece("n'importe quelle piece"),
    "A : une recette qui ne nomme aucune piece garde le comportement d'avant");

// --- E. Cochage DERIVE d'une preuve : la presence de la piece
var effets = deno.EffetsObligatoires;
Exiger(effets.Count(e => e.Piece.Length > 0) >= 3,
    "E : les effets publies declarent la piece qui les atteste");
bool PiecePresente(string motif) => Presente(null, motif);
Exiger(deno.EffetAtteste("bodacc", PiecePresente),
    "E : l'effet BODACC se coche seul -- son avis est depose dans l'arbre");
Exiger(!deno.EffetAtteste("inpi", PiecePresente),
    "E : l'effet INPI reste decoche tant qu'aucun accuse n'est depose");
Exiger(!deno.EffetAtteste("notif-secondaire", PiecePresente),
    "E : un effet sans piece attestante ne se coche jamais tout seul");
Exiger(vue.Contains("EffetAtteste"), "E : la vue lit le cochage derive, pas seulement l'etat clique");
Exiger(vue.Contains("disabled=\"@atteste\"") || vue.Contains("readonly"),
    "E : une case prouvee n'est plus cochable a la main");

// --- F. Une etape de la checklist PILOTE le filtre
var cible = deno.PourEffet("inpi");
Exiger(cible.GardePiece("Accuse INPI du 17/09/2026") && !cible.GardePiece("Avis BODACC - immatriculation"),
    "F : cliquer un effet restreint l'arbre aux pieces de CET effet");
Exiger(ReferenceEquals(deno.PourEffet("notif-secondaire"), deno),
    "F : un effet sans piece ne change rien au filtre");
Exiger(vue.Contains("EffetCible"), "F : la vue porte l'effet choisi comme filtre de l'arbre");

// --- D. Les effets passent EN PIED : l'arbre ne descend plus
var posEffets = vue.IndexOf("rig-tache__effets", StringComparison.Ordinal);
var posArbre = vue.IndexOf("<div class=\"rig-arbre\">", StringComparison.Ordinal);
Exiger(posArbre > 0 && posEffets > posArbre,
    "D : le bloc des effets est rendu APRES l'arbre (il ne le pousse plus vers le bas)");
Exiger(css.Contains("rig-tache__effets"), "D : le bloc des effets a son ancrage en pied de colonne");

// --- B. Chemin or : le noeud courant et ses parents
Exiger(noeudVue.Contains("rig-arbre__ligne--chemin"),
    "B : la ligne d'un parent du noeud courant porte la marque du chemin");
Exiger(css.Contains("rig-arbre__ligne--chemin"), "B : le filet or du chemin existe dans la charte");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "VERT : mode tache -- filtre a la piece, chemin, effets en pied, cochage prouve."
    : $"ROUGE : {echecs.Count} point(s) en echec.");
return echecs.Count == 0 ? 0 : 1;
