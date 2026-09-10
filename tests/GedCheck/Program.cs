using System.Text.RegularExpressions;

// Garde-fou de l'arbre GED et du tableau des editions.
//
// Defaut a l'origine de ce test (10/09/2026) : ouvrir l'affaire 0000
// affichait une liste PLATE de categories (un seul niveau, aucune
// colonne Date / Nb pages) et, a droite, quatre lignes de metadonnees
// de la piece. RIG v2 montre un ARBRE (PROC_VDOSSIER/Control/
// TreeviewGed.Designer.cs : colonnes Documents / Date / Nb Pages) et un
// TABLEAU DES EDITIONS avec le detail du destinataire selectionne
// (PROC_TABLEAU_LOT3/Processus/TreeviewEditions.cs et
//  OPE_DESTINATAIRE.Designer.cs). Sans ce test, la liste plate repasse.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var vue = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
var arbreModele = Path.Combine(racine, "Metier/ArbreGed.cs");
var noeud = Path.Combine(racine, "Components/ArbreGedNoeud.razor");
var css = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-accueil.css"));

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// --- 1. Le modele d'arbre existe et il est RECURSIF.
Exiger(File.Exists(arbreModele), "Metier/ArbreGed.cs existe");
var modele = File.Exists(arbreModele) ? File.ReadAllText(arbreModele) : string.Empty;
Exiger(Regex.IsMatch(modele, @"record NoeudGed\([^)]*NoeudGed\[\]\s+Enfants",
        RegexOptions.Singleline),
    "NoeudGed porte ses propres enfants (arbre, pas liste plate)");

// --- 2. Les dossiers de l'arbre VGED v2 sont la, imbriques.
foreach (var dossier in new[] { "Dossier RCS", "Depots d'actes", "Depots des comptes annuels",
                                "Formalites", "Inscriptions privileges", "Judiciaire",
                                "Ordonnances", "Demandeur", "Defendeur" })
{
    Exiger(modele.Contains(dossier), $"l'arbre porte le dossier v2 « {dossier} »");
}
Exiger(Regex.IsMatch(modele, @"Judiciaire.*Ordonnances.*Demandeur", RegexOptions.Singleline),
    "Judiciaire contient Ordonnances qui contient Demandeur (profondeur >= 3)");

// --- 3. L'arbre se rend par un composant RECURSIF, avec ses colonnes v2.
Exiger(File.Exists(noeud), "Components/ArbreGedNoeud.razor existe");
var rendu = File.Exists(noeud) ? File.ReadAllText(noeud) : string.Empty;
Exiger(rendu.Contains("<ArbreGedNoeud"), "ArbreGedNoeud.razor s'appelle lui-meme (recursion)");
foreach (var colonne in new[] { "Liste des pieces", "Date", "Nb pages" })
{
    Exiger(vue.Contains(colonne), $"l'entete de l'arbre porte la colonne « {colonne} »");
}

// --- 4. Le tableau des editions, colonnes de TreeviewEditions.cs (v2).
Exiger(vue.Contains("Tableau des editions"), "la sidebar porte le « Tableau des editions »");
foreach (var colonne in new[] { "Destinataire", "Nom du document", "Ex.", "Imprimante",
                                "File ou imprimante", "File differee" })
{
    Exiger(vue.Contains(colonne), $"le tableau des editions porte la colonne v2 « {colonne} »");
}

// --- 5. Le detail de l'edition selectionnee, champs d'OPE_DESTINATAIRE (v2).
Exiger(vue.Contains("Detail de l'edition selectionnee"), "le bloc de detail est present");
foreach (var champ in new[] { "Designation", "A l'attention de", "Adresse postale",
                              "Type de papier", "Telephone", "Siren",
                              "Coffre-fort : nature du document", "Coffre-fort",
                              "Tribunal digital : nature du document", "Tribunal digital",
                              "Tribunal digital : Erreur", "Courriel", "Observation" })
{
    Exiger(vue.Contains(champ), $"le detail porte le champ v2 « {champ} »");
}

// --- 6. La mise en page : arbre + sidebar d'edition.
Exiger(Regex.IsMatch(css, @"\.rig-vged__editions\b"), "rig-accueil.css style la sidebar d'edition");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("GED : tout est vert.");
    return 0;
}
Console.WriteLine($"GED : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
