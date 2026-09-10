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
// Le tableau des editions a son propre ecran depuis 09/2026 : les colonnes et
// le detail du destinataire s'y trouvent, plus dans Home.razor.
var ecranEditions = Path.Combine(racine, "Components/Ecrans/EcranTableauEditions.razor");
var editions = File.Exists(ecranEditions) ? File.ReadAllText(ecranEditions) : string.Empty;

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
foreach (var colonne in new[] { "Liste des pieces" })
{
    Exiger(vue.Contains(colonne), $"l'entete de l'arbre porte la colonne « {colonne} »");
}
// Demande du 10/09/2026 : la colonne « Nb pages » de v2 est RETIREE de
// l'arbre (le nombre de pages reste dans l'apercu de la piece).
Exiger(!vue.Contains("Nb pages") && !rendu.Contains("rig-arbre__pages"),
    "la colonne « Nb pages » n'est plus dans l'arbre");
// Demande du 10/09/2026 : la colonne « Date » saute aussi ; la date de
// depot reste dans l'apercu de la piece, a droite de l'arbre.
Exiger(!rendu.Contains("rig-arbre__date"),
    "la colonne « Date » n'est plus dans l'arbre");

// --- 4. Le tableau des editions, colonnes de TreeviewEditions.cs (v2).
Exiger(vue.Contains("Tableau des editions"), "la sidebar porte le « Tableau des editions »");
foreach (var colonne in new[] { "Destinataire", "Nom du document", "Ex.", "Imprimante",
                                "File ou imprimante", "File differee" })
{
    Exiger(vue.Contains(colonne) || editions.Contains(colonne),
        $"le tableau des editions porte la colonne v2 « {colonne} »");
}

// --- 5. Le detail de l'edition selectionnee, champs d'OPE_DESTINATAIRE (v2).
Exiger(editions.Contains("Detail de l'edition selectionnee") || vue.Contains("Detail de l'edition selectionnee"),
    "le bloc de detail est present");
foreach (var champ in new[] { "Designation", "A l'attention de", "Adresse postale",
                              "Type de papier", "Telephone", "Siren",
                              "Coffre-fort : nature du document", "Coffre-fort",
                              "Tribunal digital : nature du document", "Tribunal digital",
                              "Tribunal digital : Erreur", "Courriel", "Observation" })
{
    Exiger(vue.Contains(champ) || editions.Contains(champ),
        $"le detail porte le champ v2 « {champ} »");
}

// --- 6. La mise en page : arbre + sidebar d'edition.
Exiger(Regex.IsMatch(css, @"\.rig-vged__editions\b"), "rig-accueil.css style la sidebar d'edition");

// --- 7. Cliquer une piece ouvre l'apercu du document, avec ses trois
//        actions, et « Imprimer » ouvre le choix de l'imprimante.
//        Defaut a l'origine (10/09/2026) : le clic n'affichait que quatre
//        metadonnees, aucun apercu, aucune action.
Exiger(Regex.IsMatch(vue, @"class=""rig-apercu"""), "un apercu du document existe (rig-apercu)");
foreach (var action in new[] { "Imprimer", "Sauvegarder", "Transferer" })
{
    Exiger(Regex.IsMatch(vue, @"rig-apercu__actions.*?>\s*" + action, RegexOptions.Singleline),
        $"l'apercu porte le bouton « {action} »");
}
Exiger(vue.Contains("OuvrirApercu"), "le clic sur une piece appelle OuvrirApercu");
Exiger(Regex.IsMatch(vue, @"Selectionner=""OuvrirApercu""|Selectionner=""@?OuvrirApercu"""),
    "l'arbre branche le clic d'une piece sur OuvrirApercu");
Exiger(vue.Contains("rig-impression"), "la boite de choix de l'imprimante existe (rig-impression)");
Exiger(Regex.IsMatch(vue, @"ArbreDemo\.Imprimantes"),
    "la boite d'impression propose les imprimantes v2");
foreach (var champ in new[] { "File ou imprimante", "Type de papier", "Recto-verso", "Exemplaires" })
{
    Exiger(Regex.IsMatch(vue, @"rig-impression.*?" + Regex.Escape(champ), RegexOptions.Singleline),
        $"la boite d'impression porte le champ v2 « {champ} »");
}
Exiger(vue.Contains("OuvrirImpression"), "le bouton Imprimer ouvre la boite (OuvrirImpression)");
Exiger(Regex.IsMatch(css, @"\.rig-apercu\s*\{") && Regex.IsMatch(css, @"\.rig-impression\s*\{"),
    "rig-accueil.css style l'apercu et la boite d'impression");

// --- 8. Visibilite des branches selon les DOMAINES du login.
//        Demande du 10/09/2026 : « dans les niveaux il y a RCS et
//        Judiciaire et ca affecte les elements qu'on voit dans la
//        treeview ». v2 ne construit l'arbre qu'en deux branches
//        (PROC_VGED/ETP_VGED/OPE_VGED.cs, _InitTreeview) : Dossier RCS et
//        Judiciaire. Sans ce test, une branche redevient visible pour tout
//        le monde, ou l'arbre se remet a porter des dossiers inventes.
Exiger(RigV3Desktop.JeuDemo.Comptes.Length >= 2, "plusieurs logins de demo existent");
Exiger(RigV3Desktop.JeuDemo.Comptes.Select(c => c.DomainesLisibles).Distinct().Count() >= 2,
    "les logins n'ont pas tous les memes domaines");
Exiger(RigV3Desktop.JeuDemo.Compte("inconnu").Domaines.Length
       == RigV3Desktop.JeuDemo.Comptes.Min(c => c.Domaines.Length),
    "un login inconnu retombe sur le compte le moins dote");

var racineGed = RigV3Desktop.ArbreDemo.Racine("0000", "AMITEL SAS", "903 118 776");
string[] tous = [RigV3Desktop.ArbreDemo.DomaineRcs, RigV3Desktop.ArbreDemo.DomaineJudiciaire];
var vuTout = RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, tous);
var vuRcs = RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, [RigV3Desktop.ArbreDemo.DomaineRcs]);
var vuJud = RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, [RigV3Desktop.ArbreDemo.DomaineJudiciaire]);
var vuRien = RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, System.Array.Empty<string>());

// La structure de v2 : DEUX branches a la racine, pas une liste plate.
Exiger(vuTout.Count == 2, "l'arbre n'a que deux branches : Dossier RCS et Judiciaire");
Exiger(vuTout.Any(d => d.Libelle == "Dossier RCS") && vuTout.Any(d => d.Libelle == "Judiciaire"),
    "les deux branches sont bien « Dossier RCS » et « Judiciaire »");
Exiger(vuRcs.Count == 1 && vuRcs[0].Libelle == "Dossier RCS",
    "un login RCS seul ne voit que la branche Dossier RCS");
Exiger(vuJud.Count == 1 && vuJud[0].Libelle == "Judiciaire",
    "un login judiciaire seul ne voit que la branche Judiciaire");
Exiger(vuRien.Count == 0, "un login sans domaine ne voit aucune branche");

// Les sous-dossiers de la branche RCS sont ceux de v2, et rien de plus.
var rcs = vuTout.First(d => d.Libelle == "Dossier RCS");
foreach (var attendu in new[] { "Pieces jointes", "Depots d'actes",
                                "Depots des comptes annuels", "Formalites",
                                "Inscriptions privileges" })
{
    Exiger(rcs.Enfants.Any(e => e.Libelle == attendu),
        $"la branche RCS porte le sous-dossier v2 « {attendu} »");
}
// Ces dossiers n'existent PAS dans l'arbre de v2 : « Non inscrits » et
// « Bordereau de requisition » s'ouvrent dans un arbre separe, les autres
// sont des inventions de la maquette.
var libelles = racineGed.Descendants().Select(n => n.Libelle).ToArray();
foreach (var interdit in new[] { "Signalements de divergences BE", "Divers",
                                 "Non inscrits", "Bordereau mandataire",
                                 "Bordereaux de requisition",
                                 "Ordonnances de prorogation" })
{
    Exiger(!libelles.Contains(interdit),
        $"l'arbre ne porte plus le dossier inexistant en v2 « {interdit} »");
}

Exiger(rendu.Contains("Domaines") && vue.Contains("Domaines=\"Domaines\""),
    "l'arbre rendu recoit les domaines du login courant");
Exiger(vue.Contains("JeuDemo.Comptes") && vue.Contains("ChangerLogin"),
    "l'entete permet de changer de login");

// --- 9. Bouton « Nouveau » de l'arbre : popup, puis ajout dans la liste.
Exiger(vue.Contains("rig-arbre__nouveau") && vue.Contains("OuvrirNouveau"),
    "l'entete de l'arbre porte un bouton « Nouveau » qui ouvre la popup");
Exiger(vue.Contains("NouveauOuvert") && vue.Contains("AjouterElement"),
    "la popup de creation existe et ajoute l'element");
Exiger(Regex.IsMatch(css, @"\.rig-arbre__nouveau\b"), "rig-accueil.css style le bouton « Nouveau »");

var ajouts = RigV3Desktop.AccesGed.NouveauxAjouts();
var cible = racineGed.Enfants.First(d => d.Libelle == "Dossier RCS");
var avant = RigV3Desktop.AccesGed.EnfantsVisibles(cible, tous, ajouts).Count;
ajouts[cible] = [RigV3Desktop.AccesGed.NouveauDossier("Depot n 2026/0042")];
var apres = RigV3Desktop.AccesGed.EnfantsVisibles(cible, tous, ajouts);
Exiger(apres.Count == avant + 1 && apres.Any(n => n.Libelle == "Depot n 2026/0042"),
    "un element cree apparait dans la liste du dossier cible");
var voisin = cible.Enfants.First(d => d.Libelle == "Formalites");
Exiger(RigV3Desktop.AccesGed.EnfantsVisibles(voisin, tous, ajouts).Count
       == voisin.Enfants.Length,
    "l'ajout ne touche que le dossier cible");
Exiger(RigV3Desktop.AccesGed.NouvellePiece("Kbis", "PDF", "10/09/2026").EstPiece,
    "une piece creee est bien une piece (elle s'ouvre dans l'apercu)");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("GED : tout est vert.");
    return 0;
}
Console.WriteLine($"GED : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
