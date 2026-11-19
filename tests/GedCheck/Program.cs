using System.Text.Json;
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

// Bac a sable (19/11/2026) : les AccueilEtat de ce banc ENREGISTRENT a chaque
// geste (BasculerEffet, NoterModification, CloreTache...). Sans RIGV3_ETAT, ils
// ecrivaient dans %APPDATA%\RigV3\accueil.json -- l'etat REEL de l'utilisateur,
// remplace par un etat vierge a chaque passage du banc. Efface a la fin.
var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-gedcheck-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);

// PORTE EN REACT (lot 2 du plan « finir le passage a React », 19/11/2026) : le
// plan de travail de Home.razor (visionneuse, arbre, saisie guidee, modales) est
// rendu par web/src/plan/*.tsx et web/src/composants/Application.tsx ; son @code
// est deplace tel quel dans Pont/PlanTravail.cs, sa vue et ses gestes dans
// Pont/PlanTravailVue.cs. « vue » designe donc ces sources ensemble : ce que
// Home.razor contenait a lui seul. Une regle de balisage se lit dans le .tsx
// (« balisage »), une regle de code dans le C# (« codePlan »).
var codePlan = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"))
               + Environment.NewLine + File.ReadAllText(Path.Combine(racine, "Pont/PlanTravailVue.cs"));
var balisage = string.Join(Environment.NewLine,
    Directory.GetFiles(Path.Combine(racine, "web/src/plan"), "*.tsx")
             .Append(Path.Combine(racine, "web/src/composants/Application.tsx"))
             .Where(f => !f.EndsWith(".test.tsx")).OrderBy(f => f).Select(File.ReadAllText));
var vue = codePlan + Environment.NewLine + balisage;
var arbreModele = Path.Combine(racine, "Metier/ArbreGed.cs");
// Depuis le 19/11/2026, l'arbre se rend en CLASSEUR (/draft « Chronologie », conv-2009) :
// web/src/composants/ClasseurArbre.tsx remplace ArbreGedNoeud.tsx (retire). Les regles
// qui suivent se lisent dans le nouveau composant ; leur intention ne change pas.
var noeud = Path.Combine(racine, "web/src/composants/ClasseurArbre.tsx");
// PORTE EN REACT (lot 6, 19/11/2026) : ArbreGedNoeud, IconeGed, DocumentDetacheVue
// et FicheInformationsDossier ne sont plus que des COQUILLES. Leur balisage se lit
// dans web/src/composants/<Nom>.tsx, leurs regles dans Pont/<Nom>.cs ; chaque regle
// ci-dessous se lit a son nouvel endroit ET les ponts sont EXECUTES (section
// « Ponts du lot 6 », en fin de fichier). Depuis le retrait de Blazor (lot 3 du
// plan « finir le passage a React », 19/11/2026), il n'y a plus de coquilles :
// ce qu'elles faisaient (monter le composant, lui passer l'etat vivant, ouvrir et
// fermer une session) se lit la ou le plan de travail le fait desormais --
// web/src/plan/*.tsx (« balisage ») et Pont/PlanTravail*.cs (« codePlan »).
string LireSource(string relatif)
{
    var chemin = Path.Combine(racine, relatif);
    return File.Exists(chemin) ? File.ReadAllText(chemin) : string.Empty;
}
var tsxArbre = LireSource("web/src/composants/ClasseurArbre.tsx");
var pontArbre = LireSource("Pont/ArbreGedNoeud.cs");
var tsxFiche = LireSource("web/src/composants/FicheInformationsDossier.tsx");
var pontFiche = LireSource("Pont/FicheInformationsDossier.cs");
var css = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-accueil.css"));
// Le tableau des editions a son propre ecran depuis 09/2026 : les colonnes et
// le detail du destinataire s'y trouvent, plus dans Home.razor.
// PORTE EN REACT (lot 3, 19/11/2026) : le .razor n'est plus qu'une coquille ;
// colonnes et detail se lisent dans web/src/ecrans/EcranTableauEditions.tsx.
var tsxEditions = Path.Combine(racine, "web/src/ecrans/EcranTableauEditions.tsx");
var editions = File.Exists(tsxEditions) ? File.ReadAllText(tsxEditions) : string.Empty;

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
Exiger(File.Exists(noeud) && !File.Exists(Path.Combine(racine, "Components/ArbreGedNoeud.razor"))
       && !File.Exists(Path.Combine(racine, "web/src/composants/ArbreGedNoeud.tsx")),
    "web/src/composants/ClasseurArbre.tsx existe (plus de coquille .razor, plus d'ancien arbre en retrait)");
Exiger(balisage.Contains("<ClasseurArbre arbre={d.arbre}"),
    "la visionneuse du plan de travail monte le classeur React, depuis la racine de l'arbre");
// La profondeur (Judiciaire > Ordonnances > Demandeur > piece) se rend sans plafond :
// le contenu d'une carte s'appelle lui-meme pour chaque sous-dossier.
Exiger(Regex.IsMatch(tsxArbre, @"function Contenu\([\s\S]*?<Contenu key=\{l\.id \+ ':c'\}"),
    "ClasseurArbre.tsx rend les sous-dossiers par un composant qui s'appelle lui-meme (recursion)");
// Renomme le 2026-09-14 (demande Leslie) : l entete de l arbre annonce
// desormais la vue entiere, pas seulement sa colonne.
foreach (var colonne in new[] { "Visualisation du dossier" })
{
    Exiger(vue.Contains(colonne), $"l'entete de l'arbre porte la colonne « {colonne} »");
}
// Demande du 10/09/2026 : les colonnes « Nb pages » et « Date » de v2 avaient ete
// RETIREES de l'arbre. REVUE le 19/11/2026 : l'utilisateur a choisi le classeur
// « Chronologie » (/draft, conv-2009), qui range les pieces PAR DATE et montre leurs
// pages. Ce ne sont plus des colonnes de tableau, et ces valeurs viennent du metier.
Exiger(!vue.Contains("Nb pages")
       && pontArbre.Contains("Date: noeud.EstPiece ? noeud.Date") && pontArbre.Contains("Pages: noeud.EstPiece ? noeud.Pages")
       && tsxArbre.Contains("rig-classeur__date") && tsxArbre.Contains("rig-classeur__pages"),
    "la date et les pages d'une piece viennent du metier et s'affichent sur sa ligne, pas en colonne « Nb pages »");

// --- 4. Le tableau des editions, colonnes de TreeviewEditions.cs (v2).
// Le bouton d acces a quitte la visionneuse (barre du haut retiree le 15/09/2026) ;
// l ecran lui-meme est conserve.
Exiger(editions.Contains("Tableau des editions"), "l ecran « Tableau des editions » est conserve");
Exiger(editions.Contains("editions?: LigneEdition[] | null") && editions.Contains("affaire?: string | null")
       && editions.Contains("fermer: () => void")
       && editions.Contains("export function EcranTableauEditions({ editions, affaire, fermer }")
       && !File.Exists(Path.Combine(racine, "Components/Ecrans/EcranTableauEditions.razor")),
    "l'ecran React du tableau des editions garde Editions, Affaire et Fermer (plus de coquille .razor)");
Exiger(editions.Contains("appeler<string[]>('EcranTableauEditions.TypesPapier')")
       && RigV3Desktop.Pont.EcranTableauEditions.TypesPapier().SequenceEqual(RigV3Desktop.ArbreDemo.TypesPapier),
    "les types de papier du detail viennent d'ArbreDemo par le pont, dans leur ordre");
// --- Les etapes du dossier : un accordeon, l'en-tete en bas (proposition 6 du
// /draft du 19/11/2026, conv-2022), champs charges a la demande.
Exiger(vue.Contains("rig-accordeon__etapes") && vue.Contains("rig-guide__controle")
       && !vue.Contains("Controle du dossier</button>"),
    "les etapes vivent dans la colonne du milieu, en accordeon, le controle du dossier en pied, sans onglet a part");
Exiger(Regex.IsMatch(codePlan, @"var ouverte = EtapesOuvertes\.Contains\(i\);")
       && Regex.IsMatch(balisage, @"page\.ouverte && <Corps "),
    "seules les etapes depliees portent leurs champs (chargement a la demande)");
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
Exiger(Regex.IsMatch(balisage, @"className=""rig-apercu"""), "un apercu du document existe (rig-apercu)");
foreach (var action in new[] { "Imprimer", "Sauvegarder", "Envoyer par mail" })
{
    Exiger(Regex.IsMatch(vue, @"rig-apercu__actions[\s\S]{0,3000}?aria-label=""" + action + @"""", RegexOptions.Singleline),
        $"l'apercu porte le bouton « {action} »");
}
Exiger(vue.Contains("EnvoiMailOuvert") && vue.Contains("MailMessagesDemo"), "« Envoyer par mail » ouvre une popup destinataire + message");
Exiger(vue.Contains("OuvrirApercu"), "le clic sur une piece appelle OuvrirApercu");
// Demande du 15/09/2026 : un noeud sans formulaire ferme la colonne des
// donnees, un noeud sans apercu ferme la colonne du document.
Exiger(Regex.IsMatch(vue, @"if \(PlacerSurInformations\(n\)\) \{ GuideVisible = false; return; \}"),
    "clic sur une fiche (pas d'apercu) : la colonne du document se ferme");
Exiger(Regex.IsMatch(vue, @"if \(!n\.EstPiece\) \{ DonneesVisible = false; GuideVisible = false; return; \}"),
    "clic sur un dossier (ni formulaire ni apercu) : les deux colonnes se ferment");

// --- 7 bis. TROIS zones cote a cote (demande du 14/09/2026) : arbre,
// donnees, document -- et le document sort dans SA fenetre.
Exiger(Regex.IsMatch(vue, @"rig-vged__donnees"), "la colonne des DONNEES existe (zone 2)");
Exiger(Regex.IsMatch(css, @"\.rig-vged__donnees\s*\{"), "rig-accueil.css style la colonne des donnees");
Exiger(Regex.IsMatch(vue, @"DebutRedim2"),
    "une SECONDE poignee regle la largeur des donnees (3 colonnes reglables)");
// Depuis le 19/11/2026 (conv-2009), le bloc pose aussi la poignee seulement si
// l'arbre n'est pas replie ; le calcul colonne par colonne est joue par PlanTravailCheck.
Exiger(Regex.IsMatch(vue, @"if \(DonneesVisible\)\s*\{\s*(if \(!ArbreReplie\) )?morceaux\.Add"),
    "les colonnes se recalculent quand une zone se ferme");
// Borne courte : sans elle, le motif traversait tout le fichier et trouvait
// la METHODE DetacherDocument, bouton supprime ou non (mesure du 14/09).
Exiger(Regex.IsMatch(balisage, @"rig-apercu__actions[\s\S]{0,3000}?aria-label=""Detacher"" onClick=\{\(\) => plan\('detacher'\)\}")
       && codePlan.Contains("case \"detacher\": DetacherDocument(); break;"),
    "le document porte le bouton « Detacher »");
// Demande du 15/09/2026 : detacher ferme la colonne de droite, fermer la
// fenetre y renvoie le document.
var corpsDetacher = Regex.Match(vue, @"private void DetacherDocument\(\)[\s\S]*?\n    \}").Value;
Exiger(Regex.IsMatch(corpsDetacher, @"fenetre\.Show\(\);[\s\S]*GuideVisible = false;"),
    "detacher ferme la colonne de droite (pas de doublon)");
Exiger(Regex.IsMatch(corpsDetacher, @"Closed \+=[\s\S]*ApercuNoeud = piece;[\s\S]*GuideVisible = true;"),
    "fermer la fenetre detachee renvoie le document dans la colonne de droite");
Exiger(File.Exists(Path.Combine(racine, "FenetreDocument.xaml")) &&
       File.Exists(Path.Combine(racine, "FenetreDocument.xaml.cs")),
    "la fenetre detachee du document existe");
var fenDoc = File.Exists(Path.Combine(racine, "FenetreDocument.xaml.cs"))
    ? File.ReadAllText(Path.Combine(racine, "FenetreDocument.xaml.cs")) : string.Empty;
// Le CHOIX du moniteur ne se lit plus ici : il a ete sorti dans
// Metier/PlacementDocument.cs pour etre rejoue avec deux ecrans simules
// (FenetresCheck), ce qu'un poste a un seul moniteur ne permettait pas.
// Ce controle verifie donc le CABLAGE ; la regle elle-meme est prouvee a
// l'execution par FenetresCheck.
Exiger(Regex.IsMatch(fenDoc, @"EnumDisplayMonitors") && Regex.IsMatch(fenDoc, @"PlacementDocument\.Calculer"),
    "la fenetre detachee demande sa place PAR DEFAUT a PlacementDocument (choix du moniteur)");
Exiger(File.Exists(Path.Combine(racine, "Metier", "PlacementDocument.cs")),
    "la regle de placement vit dans un fichier verifiable, pas dans la fenetre WPF");
// 14/09/2026 : la position PRIME sur le placement par defaut -- elle doit etre
// la meme d'une fois sur l'autre (travail sur plusieurs ecrans).
Exiger(Regex.IsMatch(fenDoc, @"if \(!RestaurerPose\(\)\) PoserSurAutreEcran\(\)"),
    "la position memorisee passe AVANT le placement par defaut");
Exiger(Regex.IsMatch(fenDoc, @"AccueilEtat\.MemoriserDocument\(Rang, pose\)"),
    "la place laissee est memorisee, par rang de fenetre");
var etatAccueil = File.ReadAllText(Path.Combine(racine, "AccueilEtat.cs"));
Exiger(Regex.IsMatch(etatAccueil, @"Dictionary<string, PoseFenetre> FenetresDocument"),
    "la position des fenetres detachees est rangee dans accueil.json (pas de nouveau format)");
// Le verdict lui-meme est verifie A L'EXECUTION par tests/FenetresCheck
// (AccueilEtat.PoseUtilisable) ; ici on exige seulement que la fenetre s'en
// serve au lieu de refaire le calcul dans son coin.
Exiger(Regex.IsMatch(fenDoc, @"ZonesDeTravail\(\)") && Regex.IsMatch(fenDoc, @"AccueilEtat\.PoseUtilisable\(pose, zones\)"),
    "une position hors des ecrans presents est refusee (fenetre jamais invisible)");
// PLUSIEURS documents externalisables : un emplacement par rang, pas une piece
// unique partagee (sinon N fenetres montrent N fois le meme document).
var partage = File.ReadAllText(Path.Combine(racine, "Metier/DocumentPartage.cs"));
Exiger(Regex.IsMatch(partage, @"Dictionary<int, DocumentDetache> _emplacements")
       && Regex.IsMatch(partage, @"public DocumentDetache Emplacement\(int rang\)"),
    "le passe-plat porte UN emplacement PAR fenetre detachee");
Exiger(Regex.IsMatch(vue, @"List<FenetreDocument> FenetresDetachees")
       && !Regex.IsMatch(vue, @"private bool DocumentDetache;"),
    "VDossier garde une LISTE de fenetres detachees (plusieurs documents a la fois)");
Exiger(Regex.IsMatch(vue, @"RangLibre\(FenetresDetachees"),
    "chaque detachement prend le plus petit rang libre");

Exiger(!Regex.IsMatch(fenDoc, @"Application\.Current\.MainWindow\s*\.\s*(Width|Height|Left|Top)"),
    "la fenetre detachee ne redimensionne pas la fenetre principale");
// Depuis le retrait de Blazor (19/11/2026), la racine de FenetreDocument est
// RacineDocumentDetache (Pont/Racines.cs), plus la coquille .razor.
var racinesFenetres = LireSource("Pont/Racines.cs");
var racineDetachee = Regex.Match(racinesFenetres, @"public sealed class RacineDocumentDetache[\s\S]*$").Value;
Exiger(racineDetachee.Contains("public override string Composant => \"DocumentDetacheVue\";")
       && fenDoc.Contains("Racine = new Pont.RacineDocumentDetache(App.Partage, rang);"),
    "la fenetre detachee monte la vue React du document (RacineDocumentDetache)");
// L'objectif (zoom) du document detache doit etre MODULABLE et INDIVIDUEL :
// boutons +/- dans la vue detachee, applique au document, et etat garde DANS la
// vue (jamais dans DocumentPartage, sinon les fenetres zoomeraient ensemble).
// PORTE EN REACT (lot 6) : la coquille reste la racine de FenetreDocument ; la
// vue est web/src/composants/DocumentDetacheVue.tsx, la piece vient de
// Pont/DocumentDetacheVue.cs. L'objectif est un etat React de CETTE fenetre.
var tsxDetachee = LireSource("web/src/composants/DocumentDetacheVue.tsx");
var pontDetachee = LireSource("Pont/DocumentDetacheVue.cs");
var texteDetachee = racineDetachee + Environment.NewLine + tsxDetachee + Environment.NewLine + pontDetachee;
Exiger(racineDetachee.Contains("Emplacement.Change += Changer")
       && racineDetachee.Contains("DocumentDetacheVue.Vue(Emplacement)"),
    "la racine de la fenetre detachee suit SON emplacement et re-rend la vue React");
Exiger(Regex.IsMatch(tsxDetachee, @"onClick=\{\(\) => setZoom\(zoomPlus\)\}")
       && Regex.IsMatch(tsxDetachee, @"onClick=\{\(\) => setZoom\(zoomMoins\)\}"),
    "la fenetre detachee porte les boutons d'objectif (zoom + et -)");
Exiger(Regex.IsMatch(tsxDetachee, @"rig-apercu__pages""[^>]{0,120}?zoom"),
    "l'objectif s'applique aux pages du document detache");
Exiger(racineDetachee.Contains("public int Rang => rang;")
       && racineDetachee.Contains("Emplacement = partage.Emplacement(rang);"),
    "la vue detachee sait QUEL document elle affiche (son rang)");
Exiger(Regex.IsMatch(tsxDetachee, @"const \[zoom, setZoom\] = useState\(1\.0\)")
       && !Regex.IsMatch(pontDetachee + racineDetachee, @"Zoom")
       && !Regex.IsMatch(File.ReadAllText(Path.Combine(racine, "Metier/DocumentPartage.cs")), @"Zoom"),
    "l'objectif est propre a CHAQUE fenetre detachee (etat local, pas partage)");
Exiger(Regex.Matches(File.ReadAllText(Path.Combine(racine, "Metier/DocumentPartage.cs")),
        @"public static string TextePage").Count == 1 &&
       !Regex.IsMatch(texteDetachee, @"parPage") && pontDetachee.Contains("DocumentPartage.TextePage(piece, f)"),
    "la decoupe en pages n existe qu UNE fois (pas de copie dans la fenetre detachee)");
Exiger(Regex.IsMatch(codePlan, @"ArbreGedNoeud\.Activer\(active[\s\S]{0,200}?OuvrirApercu\(active\);")
       && balisage.Contains("activer={(id) => plan('arbreActiver', id)}"),
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
// « Informations des Formalites » (demande du 15/09/2026) : premiere entree de
// la categorie Formalites, formulaire des champs du processus v2 XACTION, et
// deplier Formalites place le bloc du milieu dessus.
var formalites = rcs.Enfants.First(e => e.Libelle == "Formalites");
var ficheFormalites = formalites.Enfants.FirstOrDefault();
Exiger(ficheFormalites is { EstFiche: true, Libelle: RigV3Desktop.InformationsFormalite.LibelleNoeud },
    "la categorie Formalites commence par la fiche « Informations des Formalites »");
Exiger(RigV3Desktop.InformationsFormalite.ProcessusSources.SequenceEqual(new[] { "XACTION" })
       && RigV3Desktop.InformationsFormalite.NombreChamps == 63
       && RigV3Desktop.InformationsFormalite.Sections.Select(s => s.Table).SequenceEqual(
              new[] { "ACTION_RCS", "BODACC", "BODACC_RCS_XML", "BODACC_RCS_XML_DET" }),
    "la fiche des formalites porte les 63 champs XACTION lus en base");
Exiger(ReferenceEquals(RigV3Desktop.ArbreDemo.FicheADeplier(formalites), ficheFormalites)
       && RigV3Desktop.ArbreDemo.FicheADeplier(rcs) is { Libelle: RigV3Desktop.InformationsDossier.LibelleNoeud }
       && RigV3Desktop.ArbreDemo.FicheADeplier(formalites.Enfants[1]) is null,
    "deplier Formalites (ou Dossier RCS) designe sa fiche d'informations");
// « Documents generes lors de la formalite » (demande du 15/09/2026) : pendant
// v3 des EDITIONS de v2 (OPE_VGED.cs _RechercherFormalites, lignes d'edition
// R_CODEMODELE_VDAS rangees dans chaque « Formalite n° »).
var editionsFormalite = formalites.Enfants[1].Enfants
    .FirstOrDefault(e => e.Libelle == RigV3Desktop.ArbreDemo.LibelleDocumentsGeneres);
Exiger(editionsFormalite is { EstPiece: false, EstFiche: false } && editionsFormalite.NbPieces > 0
       && editionsFormalite.Enfants.All(e => e.Origine == "Edition greffe"),
    "chaque formalite porte la categorie « Documents generes lors de la formalite » (EDITIONS v2)");
Exiger(Regex.IsMatch(vue, @"ArbreDemo.FicheADeplier\(") && vue.Contains("InformationsFormalite.Sections"),
    "la vue utilise la regle de depliage et affiche la fiche des formalites");
// « Informations des depots » (demande du 15/09/2026) : premiere entree de
// la categorie Depots d'actes, formulaire des champs du processus v2 XDAS, et
// deplier Depots d'actes place le bloc du milieu dessus.
var depots = rcs.Enfants.First(e => e.Libelle == "Depots d'actes");
var ficheDepots = depots.Enfants.FirstOrDefault();
Exiger(ficheDepots is { EstFiche: true, Libelle: RigV3Desktop.InformationsDepots.LibelleNoeud },
    "la categorie Depots d'actes commence par la fiche « Informations des depots »");
Exiger(RigV3Desktop.InformationsDepots.ProcessusSources.SequenceEqual(new[] { "XDAS" })
       && RigV3Desktop.InformationsDepots.NombreChamps == 77
       && RigV3Desktop.InformationsDepots.Sections.Select(s => s.Table).SequenceEqual(
              new[] { "DEPOT_RCS", "EVENEMENT_RCS", "ACTE_DEPOSE", "OBJET_ACTE",
                      "INPI_TRANS_DEPOT_IMR", "DOSSIER_RCS", "BODACC", "BODACC_RCS_XML" }),
    "la fiche des depots porte les 77 champs XDAS lus en base");
Exiger(ReferenceEquals(RigV3Desktop.ArbreDemo.FicheADeplier(depots), ficheDepots)
       && RigV3Desktop.ArbreDemo.FicheADeplier(depots.Enfants[1]) is null,
    "deplier Depots d'actes designe sa fiche d'informations, pas un depot");
Exiger(vue.Contains("InformationsDepots.Sections"),
    "la vue affiche la fiche des depots");

// --- 8 bis. La branche Judiciaire porte les objets des processus v2 de
//     CREATION et de MODIFICATION du judiciaire (demande du 16/09/2026).
//     Libelles des processus lus en base (RIG_AMIENS@SQL-PROD\PROD,
//     PROCESSUS_ET_FONCTIONALITE) : CJUD/MJUD -> Instances, JOIN ->
//     Jonctions et disjonctions, MESIN -> Mesures d'instruction,
//     SIGNJUD/ORDOSIGN -> Decisions judiciaires, NPC/NF/REPRIPCH/MPC/EF ->
//     Procedures collectives, DEPPC -> Depots sur la procedure, EJC ->
//     Instances juge-commissaire, CLOT -> Cloture, IPE/IPSUIVI ->
//     Injonctions de payer, OPE -> Ordonnances. Sans ce test, la branche
//     Judiciaire retombe a la seule categorie « Ordonnances ».
var jud = vuTout.First(d => d.Libelle == "Judiciaire");
foreach (var attendu in new[] { "Instances", "Procedures collectives",
                                "Injonctions de payer", "Ordonnances" })
{
    Exiger(jud.Enfants.Any(e => e.Libelle == attendu),
        $"la branche Judiciaire porte la categorie « {attendu} »");
}
var sousJud = jud.Descendants().Select(n => n.Libelle).ToArray();
foreach (var attendu in new[] { "Jonctions et disjonctions", "Mesures d instruction",
                                "Decisions judiciaires", "Depots sur la procedure",
                                "Instances juge-commissaire", "Cloture" })
{
    Exiger(sousJud.Contains(attendu),
        $"la branche Judiciaire porte le sous-dossier « {attendu} »");
}
Exiger(jud.NbPieces >= 15, "la branche Judiciaire porte au moins 15 pieces");

// --- 8 ter. Affaires PUREMENT judiciaires (demande du 16/09/2026) : une
//     affaire sans dossier RCS (partie non immatriculee, personne physique,
//     societe d un autre greffe) n ouvre QUE la branche « Judiciaire ».
//     v2 fait exactement cela : OPE_VGED.cs, _InitTreeview() n ajoute
//     « Dossier RCS » que si l affaire A un dossier RCS. Sans ce test,
//     toute affaire ouvre un dossier RCS vide qui n existe pas.
var jud2 = RigV3Desktop.ArbreDemo.RacineJudiciaire("2026J00087", "DUPONT Jean");
var brJud = RigV3Desktop.AccesGed.EnfantsVisibles(jud2, tous);
Exiger(brJud.Count == 1 && brJud[0].Libelle == "Judiciaire",
    "une affaire purement judiciaire n'ouvre que la branche Judiciaire");
Exiger(!jud2.Descendants().Any(n => n.Libelle == "Dossier RCS"),
    "une affaire purement judiciaire ne porte aucun dossier RCS");
Exiger(brJud[0].NbPieces == jud.NbPieces,
    "la branche Judiciaire est la meme, avec ou sans dossier RCS");
Exiger(RigV3Desktop.AccesGed.EnfantsVisibles(
           RigV3Desktop.ArbreDemo.Racine("0000", "AMITEL SAS", "903 118 776",
                                         avecJudiciaire: false), tous)
       .Count == 1,
    "une affaire sans volet judiciaire n'ouvre que la branche Dossier RCS");
// La liste des affaires et le fonds de la page portent ces affaires sans Siren.
// La recherche d'affaires est PORTEE EN REACT (lot 3, 19/11/2026) : son fonds
// vit dans Pont/EcranRechAffaires.cs, et la recherche y est EXECUTEE.
var rechAffaires = File.ReadAllText(Path.Combine(racine, "Pont/EcranRechAffaires.cs"));
foreach (var numero in new[] { "2026J00087", "2026P00031", "2026IP00214",
                               "2026M00045", "2026C00059" })
{
    var trouvee = RigV3Desktop.Pont.EcranRechAffaires.Trouver(
        new(numero, "", "", "", "", "", "", "", "", null, null), null, true, null);
    Exiger(rechAffaires.Contains(numero) && vue.Contains(numero)
           && trouvee.Lignes.Any(l => l.Numero == numero && l.Siren == "" && l.Type == "JUD"),
        $"l'affaire purement judiciaire « {numero} » est cherchable et ouvrable");
}
Exiger(vue.Contains("SansDossierRcs") && vue.Contains("avecDossierRcs: !d.SansDossierRcs"),
    "ouvrir une affaire purement judiciaire coupe la branche Dossier RCS");

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

Exiger(codePlan.Contains("private string[] Domaines => Accueil.Domaines;")
       && pontArbre.Contains("AccesGed.EnfantsVisibles(n, domaines, ajouts, masques, recette)")
       // Les branches rendues sont celles du classeur (OuvertsDuClasseur, 19/11/2026) ;
       // les domaines du login, eux, passent toujours.
       && codePlan.Contains("ArbreGedNoeud.Vue(Arbre, 0, OuvertsDuClasseur(), NoeudSelectionne, Domaines,"),
    "l'arbre rendu recoit les domaines du login courant");
// Le rond de profil est rendu par React depuis le lot 1 du passage a React
// (19/11/2026) : la liste des comptes et le changement de login vivent dans la
// session de l'accueil (Pont/Accueil.cs), le clic dans web/src/accueil/RondProfil.tsx,
// et Home.razor replie l'arbre sur ce que le nouveau login voit.
var pontAccueil = File.ReadAllText(Path.Combine(racine, "Pont/Accueil.cs"));
var rondProfil = File.ReadAllText(Path.Combine(racine, "web/src/accueil/RondProfil.tsx"));
Exiger(pontAccueil.Contains("JeuDemo.Comptes") && pontAccueil.Contains("ChangerLogin")
       && rondProfil.Contains("geste('login', compte.login)"),
    "l'entete permet de changer de login");
Exiger(vue.Contains("case Pont.DemandeAccueil.LoginChange: ApresChangementLogin();")
       && vue.Contains("private string[] Domaines => Accueil.Domaines;"),
    "un changement de login replie l'arbre et change ses domaines visibles");

// --- 9. Bouton « Nouveau » de l'arbre : popup, puis ajout dans la liste.
Exiger(vue.Contains("rig-arbre__nouveau") && vue.Contains("OuvrirNouveau"),
    "l'entete de l'arbre porte un bouton « Nouveau » qui ouvre la popup");
Exiger(vue.Contains("NouveauOuvert") && vue.Contains("AjouterElement"),
    "la popup de creation existe et ajoute l'element");
Exiger(Regex.IsMatch(css, @"\.rig-arbre__nouveau\b"), "rig-accueil.css style le bouton « Nouveau »");

// Les fenetres modales s'ouvrent DEPUIS un ecran plein (`.rig-proc`, z-index 88) :
// sous ce niveau elles se rendent derriere lui, invisibles (constate le 10/09/2026).
var zVoile = Regex.Match(css, @"\.rig-voile \{[^}]*z-index:\s*(\d+)");
var zHaut = Regex.Match(css, @"\.rig-voile--haut \{[^}]*z-index:\s*(\d+)");
Exiger(zVoile.Success && int.Parse(zVoile.Groups[1].Value) > 88,
    "le voile des fenetres passe au-dessus des ecrans pleins");
Exiger(zHaut.Success && int.Parse(zHaut.Groups[1].Value) > 88,
    "le voile haut (creation) passe au-dessus des ecrans pleins");
Exiger(!Regex.IsMatch(css, @"\.rig-(impression|apercu) \{[^}]*background:\s*var\(--rig-fond-2"),
    "les fenetres ont un fond OPAQUE (--rig-fond-2 est translucide)");

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


// --- 10. Creation par ICONES, nommage SUR PLACE, menu du clic droit.
Exiger(vue.Contains("rig-natures") && vue.Contains("AccesGed.Natures")
       && !vue.Contains("NouveauLibelle"),
    "la fenetre « Nouveau » propose des icones de nature, sans champ Libelle");
Exiger(RigV3Desktop.AccesGed.Natures.Length >= 10,
    "la liste des natures proposees est enrichie");
Exiger(Regex.IsMatch(css, @"\.rig-tuile-nature \{[^}]*border:[^;]*dashed")
       && Regex.IsMatch(css, @"\.rig-tuile-nature \{[^}]*aspect-ratio:\s*1\s*/\s*1")
       && Regex.IsMatch(css, @"\.rig-tuile-nature \{[^}]*border-radius")
       // La grille GED ne doit JAMAIS reutiliser `.rig-nature` : cette classe est
       // le badge de la colonne Nature du tableau des affaires (rig-charte.css).
       // Collision constatee le 11/09/2026 : les lignes du tableau prenaient
       // aspect-ratio 1/1 et devenaient de gros carres.
       && !Regex.IsMatch(css, @"\.rig-nature \{"),
    "les tuiles de nature sont carrees, arrondies et en pointilles");
var arbreVue = tsxArbre;
// Le clic droit n'annoncait RIEN a l'ecran : remplace le 17/09/2026 par un
// bouton « trois points » a droite de la ligne (meme geste que la liste des
// conversations d'Autowin OS). Le clic droit ne doit plus etre le seul acces.
Exiger(arbreVue.Contains("rig-arbre__actions")
       && !(arbreVue + Regex.Match(balisage, @"<ClasseurArbre arbre=\{d\.arbre\}[\s\S]*?/>").Value).Contains("oncontextmenu", StringComparison.OrdinalIgnoreCase)
       && arbreVue.Contains("e.stopPropagation(); menuContextuel(")
       && vue.Contains("rig-ctx__item")
       && vue.Contains("SupprimerNoeud") && vue.Contains("DemarrerRenommage"),
    "les trois points a droite d'une ligne ouvrent le menu renommer/supprimer");
Exiger(Regex.IsMatch(css, @"\.rig-arbre__actions \{[^}]*opacity:\s*0")
       && css.Contains(".rig-arbre__ligne:hover .rig-arbre__actions"),
    "les trois points restent discrets et se revelent au survol de la ligne");
Exiger(arbreVue.Contains("rig-arbre__saisie") && vue.Contains("ValiderRenommage")
       && vue.Contains("NoeudRenomme = element"),
    "un element cree se nomme SUR PLACE dans l'arbre");
// Le menu de GAUCHE de l'accueil s'appelle deja `.rig-menu` : une seconde
// declaration de ce nom (menu contextuel) l'avait sorti du flux en
// `position: fixed` et tassait tout l'accueil a gauche (10/09/2026).
Exiger(!Regex.IsMatch(css, @"\.rig-menu \{[^}]*position:\s*fixed"),
    "le menu de gauche de l'accueil n'est pas mis en position fixe");
Exiger(Regex.IsMatch(css, @"\.rig-ctx \{[^}]*position:\s*fixed"),
    "le menu du clic droit a son propre nom de classe (.rig-ctx)");

// Chaque nature doit avoir SON dessin : sans cela les tuiles se
// ressemblent toutes et le choix ne se fait plus a l'oeil.
// PORTE EN REACT (lot 6) : les dessins vivent dans web/src/composants/IconeGed.tsx ;
// la fenetre de creation (web/src/plan/Modales.tsx) le pose directement depuis le
// lot 2 ; sa coquille IconeGed.razor est retiree avec Blazor (19/11/2026).
var icones = LireSource("web/src/composants/IconeGed.tsx");
Exiger(balisage.Contains("<IconeGed nature={n.code} classe=\"rig-nature__icone\" />") && !vue.Contains("rig-nature__icone rig-arbre__icone")
       && Regex.IsMatch(icones, @"export function IconeGed\(\{ nature, classe\b")
       && !File.Exists(Path.Combine(racine, "Components/IconeGed.razor")),
    "les tuiles de nature portent une vraie icone dessinee, pas une pastille");
foreach (var n in RigV3Desktop.AccesGed.Natures)
{
    if (n.Code == "pdf") continue; // la feuille est le dessin par defaut
    Exiger(icones.Contains($"case '{n.Code}':"), $"la nature « {n.Titre} » a son propre dessin");
}
Exiger(icones.Contains("stroke=\"currentColor\"") && !icones.Contains("http"),
    "les icones sont tracees au trait, sans image ni ressource externe");

// L'arbre pose la MEME icone que la fenetre de creation, plus de pastille.
var arbreIcone = tsxArbre;
Exiger(arbreIcone.Contains("<IconeGed nature={ligne.icone} classe=\"rig-arbre__icone\"")
       && pontArbre.Contains("AccesGed.CodeIcone(noeud)") && !arbreIcone.Contains("rig-arbre__icone--"),
    "les lignes de l'arbre portent une icone dessinee, plus une pastille carree");
Exiger(!Regex.IsMatch(css, @"\.rig-arbre__icone \{[^}]*background"),
    "l'icone de l'arbre est un trait, pas un aplat de couleur");
Exiger(RigV3Desktop.AccesGed.CodeIcone(RigV3Desktop.AccesGed.NouveauDossier("X")) == "dossier",
    "un dossier prend le dessin de la chemise");
Exiger(RigV3Desktop.AccesGed.CodeIcone(
        RigV3Desktop.AccesGed.NouvellePiece("X", "JPG", "10/09/2026")) == "jpg",
    "une piece JPG prend le dessin de l'image");
Exiger(RigV3Desktop.AccesGed.CodeIcone(
        RigV3Desktop.AccesGed.NouveauSelonNature("ordonnance", "10/09/2026")) == "ordonnance",
    "une ordonnance creee garde SON dessin dans l'arbre");
Exiger(RigV3Desktop.AccesGed.CodeIcone(
        racineGed.Descendants().First(n => n.EstPiece)).Length > 0,
    "une piece du jeu de demonstration retombe sur un dessin connu");

// La barre du haut (onglets, reduire/fermer, deplacement de la fenetre) doit
// rester visible DANS la vue d'une affaire : la visionneuse commence sous le
// bandeau et passe sous la barre.
var vged = Regex.Match(css, @"\.rig-vged \{(?:[^{}]|/\*.*?\*/)*?\}", RegexOptions.Singleline);
Exiger(vged.Success && vged.Value.Contains("--rig-bandeau-h")
       && !Regex.IsMatch(vged.Value, @"inset:\s*0\s*;"),
    "la vue d'une affaire commence SOUS la barre du haut");
var zVged = Regex.Match(vged.Value, @"z-index:\s*(\d+)");
var zBarre = Regex.Match(css, @"\.rig-barre \{(?:[^{}]|/\*.*?\*/)*?z-index:\s*(\d+)",
                         RegexOptions.Singleline);
Exiger(zVged.Success && zBarre.Success
       && int.Parse(zVged.Groups[1].Value) < int.Parse(zBarre.Groups[1].Value),
    "la vue d'une affaire passe SOUS la barre du haut, jamais par-dessus");

var masques = RigV3Desktop.AccesGed.NouveauxMasques();
masques.Add(cible.Enfants[0]);
Exiger(RigV3Desktop.AccesGed.EnfantsVisibles(cible, tous, null, masques).Count
       == cible.Enfants.Length - 1,
    "un noeud supprime disparait de la liste du dossier");

// La fenetre detachee porte la barre de titre native de Windows : elle ne doit
// PAS piloter FenetreInterop, dont les commandes visent Application.Current.MainWindow
// -- reduire/fermer depuis le document frapperaient alors la fenetre principale.
Exiger(!Regex.IsMatch(texteDetachee, @"FenetreInterop"),
    "la vue detachee ne pilote pas les commandes de la fenetre principale");

// --- MODE TACHE (task driven) : une recette replie l'arbre sur ce qu'elle
// nomme, sans rien supprimer, et le bouton « Afficher tout le dossier »
// remet la vue entiere. Sans ce test, la vue focalisee peut se transformer
// en suppression silencieuse de branches.
var deno = RigV3Desktop.RecettesTaches.Deno;
Exiger(deno.Champs.Length == 2
       && deno.Champs.Any(c => c.Table == "INSCRIT_PM" && c.Colonne == "DENOMINATION_USAGE")
       && deno.Champs.Any(c => c.Table == "ETABLISSEMENT" && c.Colonne == "NOM_DOMAINE"),
    "la recette DENO declare la denomination d'usage ET le nom de domaine");
Exiger(deno.Champs.Any(c => !c.Declare),
    "la recette distingue un champ NON declare en base (le nom de domaine)");

var infos = RigV3Desktop.InformationsDossier.Sections;
Exiger(infos.Any(s => s.Table == "INSCRIT_PM" && s.Champs.Any(c => c.Colonne == "DENOMINATION_USAGE"))
       && infos.Any(s => s.Table == "ETABLISSEMENT" && s.Champs.Any(c => c.Colonne == "NOM_DOMAINE")),
    "les deux champs de la recette existent bien dans la fiche du dossier");
Exiger(infos.Sum(s => s.Champs.Length) > 400
       && infos.Count(s => deno.GardeTable(s.Table)) == 2,
    "la recette ne retient que 2 sections de la fiche sur les 31");

var dossierRcs = cible;
{
    var complet = RigV3Desktop.AccesGed.EnfantsVisibles(dossierRcs, tous);
    var focalise = RigV3Desktop.AccesGed.EnfantsVisibles(dossierRcs, tous, null, null, deno);
    Exiger(focalise.Count < complet.Count, "la tache DENO replie une partie du dossier RCS");
    Exiger(focalise.Any(n => n.EstFiche),
        "la tache DENO garde la fiche « Informations du dossier »");
    Exiger(focalise.Any(n => n.Libelle == "Pieces jointes") && focalise.Any(n => n.Libelle == "Formalites"),
        "la tache DENO garde les branches ou deposer la preuve");
    Exiger(!focalise.Any(n => n.Libelle == "Depots des comptes annuels"),
        "la tache DENO replie les branches qu'elle ne nomme pas");
    // Le REPLI n'est pas une suppression : sans recette, tout revient.
    Exiger(RigV3Desktop.AccesGed.EnfantsVisibles(dossierRcs, tous, null, null, null).Count == complet.Count,
        "« Afficher tout le dossier » remet toutes les branches (repli, pas suppression)");
}
Exiger(!RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, tous, null, null, deno)
        .Any(n => n.Libelle == "Judiciaire"),
    "la tache DENO ne montre pas la branche Judiciaire");

var vueTache = vue;
Exiger(vueTache.Contains("Afficher tout le dossier"),
    "la vue porte le bouton « Afficher tout le dossier »");
Exiger(vueTache.Contains("BranchesMasquees") && vueTache.Contains("PiecesMasquees"),
    "le bouton annonce COMBIEN il revele (branches et pieces masquees)");
Exiger(tsxFiche.Contains("Afficher tous les champs") && balisage.Contains("toutAffiche={() => plan('toutAffiche')}")
       && codePlan.Contains("case \"toutAffiche\": CompterToutAffiche(); break;")
       && pontFiche.Contains("if (TousChamps) Rappels.Add(\"toutAffiche\")"),
    "la fiche porte le meme bouton et COMPTE les clics");

// fix-ok: depuis le 17/09 Relever() ne rend QUE le catalogue metier de la feuille 2 ; les constats calcules (lots, taches) sont dans ReleverCalculees().
var alertesTache = new RigV3Desktop.AlertesGreffe().ReleverCalculees();
var taches = alertesTache.Where(a => a.Tache is not null).ToArray();
// Depuis le regroupement (16/09) les denominations arrivent en LOT, plus
// une par dossier : la file mene toujours a la tache, par le tableau.
Exiger(alertesTache.Any(a => a.Lot == RigV3Desktop.RecettesTaches.CodeDeno),
    "l'accueil mene a la tache DENO, desormais par son entree groupee");
Exiger(taches.Any(a => a.Tache!.Value.Code == "DPER")
       && taches.Any(a => a.Tache!.Value.Code == "DSTE"),
    "les alertes de changement de dirigeant arrivent sous leurs DEUX codes");
Exiger(taches.All(a => RigV3Desktop.RecettesTaches.ParCode(a.Tache!.Value.Code) is not null),
    "aucune alerte n'ouvre une tache sans recette");

// --- CLOTURE de la tache : elle ne se ferme que si SES champs sont ecrits,
// la trace dit QUI a ecrit quoi, et l'alerte ne revient plus ensuite.
var ecrit = new Dictionary<string, string>();
Exiger(!deno.EstComplete(c => ecrit.TryGetValue(c, out var v) ? v : null),
    "une tache sans aucun champ ecrit n'est PAS complete");
ecrit["INSCRIT_PM.DENOMINATION_USAGE"] = "AMIENS BILLARDS 2";
string? Ecrit(string c) => ecrit.TryGetValue(c, out var v) ? v : null;

// NOM DE DOMAINE : exige UNIQUEMENT si le dossier en porte deja un
// (decide le 16/09/2026 -- 1 072 etablissements sur 239 564 en ont un).
// Dossier SANS domaine d'origine : la denomination suffit a clore.
Exiger(deno.EstComplete(Ecrit),
    "sans domaine d'origine, renommer suffit a clore (99,5 % des dossiers)");
// Dossier AVEC un domaine d'origine : il DOIT etre repris.
string? Origine(string c) => c == "ETABLISSEMENT.NOM_DOMAINE" ? "ancien-nom.fr" : null;
Exiger(!deno.EstComplete(Ecrit, 1, Origine),
    "un dossier qui A deja un domaine ne se clot pas sans l'avoir repris");
Exiger(deno.Manquants(Ecrit, 1, Origine).Single().Colonne == "NOM_DOMAINE",
    "le champ manquant est NOMME, pas un refus muet");
ecrit["ETABLISSEMENT.NOM_DOMAINE"] = "amiens-billards.fr";
Exiger(deno.EstComplete(Ecrit, 1, Origine),
    "le domaine repris, la tache se clot");
ecrit["ETABLISSEMENT.NOM_DOMAINE"] = "   ";
Exiger(!deno.EstComplete(Ecrit, 1, Origine),
    "une valeur effacee redevient manquante (pas de faux vert)");
Exiger(deno.Champs.Single(c => c.Colonne == "NOM_DOMAINE").ObligatoireSiRenseigne
       && !deno.Champs.Single(c => c.Colonne == "NOM_DOMAINE").Obligatoire,
    "le nom de domaine est conditionnel : ni exige partout, ni abandonne");
Exiger(deno.ChampsExiges(null).Length == 1 && deno.ChampsExiges(Origine).Length == 2,
    "1 champ exige sans domaine d'origine, 2 avec");

// La PREUVE de completude est la trace du dossier, avec son auteur.
var etatTest = new RigV3Desktop.AccueilEtat();
Exiger(etatTest.DerniereValeur("2026B01067", "INSCRIT_PM.DENOMINATION_USAGE") is null,
    "aucune valeur n'est prouvee tant que rien n'a ete ecrit");
etatTest.Modifications["2026B01067"] =
[
    new(DateTime.Now, "R. VILAIN", "Informations du dossier > Identite de la personne morale",
        "Denomination d'usage", "INSCRIT_PM.DENOMINATION_USAGE", "AMIENS BILLARDS", "AMIENS BILLARDS 2"),
];
Exiger(etatTest.DerniereValeur("2026B01067", "INSCRIT_PM.DENOMINATION_USAGE") == "AMIENS BILLARDS 2",
    "la trace du dossier dit la derniere valeur ecrite (preuve de completude)");
Exiger(etatTest.Modifications["2026B01067"][0].Auteur.Length > 0,
    "la trace dit QUI a modifie le champ");

Exiger(!etatTest.TacheFaite(RigV3Desktop.RecettesTaches.CodeDeno, "2026B01067"),
    "une tache non close n'est pas marquee faite");
etatTest.CloreTache(RigV3Desktop.RecettesTaches.CodeDeno, "2026B01067", "R. VILAIN");
Exiger(etatTest.TacheFaite(RigV3Desktop.RecettesTaches.CodeDeno, "2026B01067"),
    "une tache close est retenue (code + affaire + auteur + date)");
Exiger(!etatTest.TacheFaite(RigV3Desktop.RecettesTaches.CodeDeno, "0000"),
    "la cloture ne vaut que pour l'affaire nommee, pas pour les autres");

// --- PROPAGATION : ce que l'evenement entraine AILLEURS que dans les champs.
// Les canaux et leurs codes viennent du referentiel CODE_EVENEMENT_RCS : si
// quelqu'un les reecrit a la main, ce test le voit.
Exiger(deno.Effets.Length == 5 && deno.EffetsObligatoires.Length == 4,
    "DENO declare 5 effets, dont 4 exiges avant cloture");
foreach (var (canal, code) in new[]
         { ("bodacc", "B0111 / B0112 / B0113"), ("inpi", "0F"), ("infogreffe", "4028") })
{
    var eff = deno.Effets.FirstOrDefault(x => x.Canal == canal);
    Exiger(eff is not null && eff.Code == code,
        $"l'effet « {canal} » porte le code lu en base ({code})");
}
Exiger(deno.Effets.All(x => x.Source.StartsWith("CODE_EVENEMENT_RCS.")),
    "chaque effet nomme la colonne de base qui le declare");
Exiger(deno.Effets.Single(x => x.Canal == "notif-principal").Obligatoire == false
       && deno.Effets.Single(x => x.Canal == "notif-secondaire").Obligatoire,
    "la base dit non pour le principal et oui pour le secondaire : le test le fige");

var coches = new HashSet<string>();
Exiger(deno.EffetsRestants(coches.Contains).Length == 4,
    "aucun effet coche : les 4 obligatoires restent a traiter");
coches.Add("bodacc"); coches.Add("inpi"); coches.Add("infogreffe");
Exiger(deno.EffetsRestants(coches.Contains).Single().Canal == "notif-secondaire",
    "il reste la notification aux etablissements secondaires, nommee");
coches.Add("notif-secondaire");
Exiger(deno.EffetsRestants(coches.Contains).Length == 0,
    "les 4 effets obligatoires coches, plus rien ne bloque la cloture");
Exiger(!coches.Contains("notif-principal"),
    "un effet NON exige ne doit pas etre coche pour pouvoir clore");

var etatEffets = new RigV3Desktop.AccueilEtat();
Exiger(!etatEffets.EffetTraite("DENO", "2026B01067", "bodacc"),
    "aucun effet n'est traite au depart");
etatEffets.BasculerEffet("DENO", "2026B01067", "bodacc");
Exiger(etatEffets.EffetTraite("DENO", "2026B01067", "bodacc"),
    "cocher un effet le retient (une tache interrompue se retrouve a l'identique)");
etatEffets.BasculerEffet("DENO", "2026B01067", "bodacc");
Exiger(!etatEffets.EffetTraite("DENO", "2026B01067", "bodacc"),
    "decocher un effet est possible : le geste est reversible");
Exiger(!etatEffets.EffetTraite("DENO", "0000", "bodacc"),
    "les effets coches valent pour UNE affaire, pas pour toutes");

// --- 2e RECETTE : changement de dirigeant. C'est elle qui dit si le modele
// tient sans etre reecrit -- et ce qu'il a fallu lui ajouter.
var dir = RigV3Desktop.RecettesTaches.Dirigeant;
Exiger(dir.Evenements.Length == 2 && dir.Evenements.Contains("DSTE")
       && dir.Evenements.Contains("DPER"),
    "UNE tache « changer un dirigeant » couvre DEUX codes RCS (DSTE et DPER)");
Exiger(RigV3Desktop.RecettesTaches.ParCode("DPER") is { } rd && rd.Code == dir.Code,
    "une alerte DPER trouve la meme recette qu'une alerte DSTE");
Exiger(RigV3Desktop.RecettesTaches.ParCode("NGER") is null,
    "NGER n'a PAS de recette : la base dit qu'il ne sert qu'aux depots d'actes");
Exiger(deno.Evenements.Single() == "DENO",
    "une recette a un seul code garde le sien (rien n'a casse pour DENO)");

Exiger(dir.Champs.Length == 5 && dir.ChampsObligatoires.Length == 4,
    "5 champs dont 4 exiges : la date de fin du sortant reste facultative");
Exiger(dir.Champs.Single(c => c.Colonne == "SUPPRIM_DATE_FIN").Obligatoire == false,
    "une nomination SANS depart reste un changement de dirigeant valide");
Exiger(dir.Champs.All(c => !c.Declare),
    "aucun champ dirigeant n'est declare en base : la recette le dit au lieu de faire semblant");
Exiger(dir.Champs.All(c => c.Repetable) && deno.Champs.All(c => !c.Repetable),
    "un dirigeant est une LIGNE repetable, une denomination non");
Exiger(deno.Champs.Length == 2,
    "la 1re recette porte toujours ses 2 champs : le domaine n'a pas ete retire");

var ecritDir = new Dictionary<string, string>
{
    ["DIRIGEANT_PP[1].PATRONYME"] = "MARTIN",
    ["DIRIGEANT_PP[1].PRENOMS_ETAT_CIVIL"] = "Claire",
    ["DIRIGEANT[1].FONCTION_DIRIGEANT"] = "Gerant",
    ["DIRIGEANT[1].DATE_ENTREE"] = "01/09/2026",
};
Exiger(dir.EstComplete(c => ecritDir.TryGetValue(c, out var v) ? v : null),
    "les 4 champs exiges suffisent : le champ facultatif ne bloque pas la cloture");

Exiger(dir.Effets.Single(x => x.Canal == "infogreffe").Code == "4206"
       && deno.Effets.Single(x => x.Canal == "infogreffe").Code == "4028",
    "chaque evenement a SON code Infogreffe (4206 dirigeant, 4028 denomination)");
Exiger(dir.EffetsObligatoires.Length == 3
       && dir.Effets.Single(x => x.Canal == "notif").Obligatoire == false,
    "le dirigeant ne notifie personne : 3 effets exiges, pas 4");
Exiger(dir.Branches.Contains("Depots d'actes") && !deno.Branches.Contains("Depots d'actes"),
    "la tache dirigeant ouvre en plus les depots d'actes (l'acte de nomination)");

// --- PLUSIEURS LIGNES : trois dirigeants changes le meme jour font UNE
// tache, pas trois. Chaque ligne a sa propre cle de trace.
Exiger(dir.ARepetition && !deno.ARepetition,
    "la tache dirigeant se saisit en plusieurs lignes, pas la denomination");
var champFonction = dir.Champs.Single(c => c.Colonne == "FONCTION_DIRIGEANT");
Exiger(RigV3Desktop.RecetteTache.Cle(champFonction, 2) == "DIRIGEANT[2].FONCTION_DIRIGEANT",
    "un champ repetable porte son rang de ligne dans la trace");
Exiger(RigV3Desktop.RecetteTache.Cle(champFonction, 1)
       != RigV3Desktop.RecetteTache.Cle(champFonction, 2),
    "le 2e dirigeant n'ECRASE pas le 1er dans la trace");
Exiger(RigV3Desktop.RecetteTache.Cle(deno.Champs[0], 2) == "INSCRIT_PM.DENOMINATION_USAGE",
    "un champ NON repetable garde sa cle, quel que soit le rang");

var l1 = new Dictionary<string, string>
{
    ["DIRIGEANT_PP[1].PATRONYME"] = "MARTIN",
    ["DIRIGEANT_PP[1].PRENOMS_ETAT_CIVIL"] = "Claire",
    ["DIRIGEANT[1].FONCTION_DIRIGEANT"] = "Gerant",
    ["DIRIGEANT[1].DATE_ENTREE"] = "01/09/2026",
};
string? Lire(string c) => l1.TryGetValue(c, out var v) ? v : null;
Exiger(dir.EstComplete(Lire, 1), "une seule ligne renseignee : la tache est complete");
Exiger(!dir.EstComplete(Lire, 2),
    "ouvrir un 2e dirigeant ROUVRE la tache (une ligne vide ne se clot pas en silence)");
Exiger(dir.ManquantsParLigne(Lire, 2).All(x => x.Ligne == 2)
       && dir.ManquantsParLigne(Lire, 2).Length == 4,
    "les 4 champs manquants sont nommes SUR LA LIGNE 2, pas en vrac");
l1["DIRIGEANT_PP[2].PATRONYME"] = "DUPONT";
l1["DIRIGEANT_PP[2].PRENOMS_ETAT_CIVIL"] = "Jean";
l1["DIRIGEANT[2].FONCTION_DIRIGEANT"] = "President";
l1["DIRIGEANT[2].DATE_ENTREE"] = "02/09/2026";
Exiger(dir.EstComplete(Lire, 2), "les deux dirigeants renseignes, la tache se clot");
Exiger(dir.Manquants(Lire, 3).Length == 4,
    "un 3e dirigeant ajoute redemande ses champs");

var etatLignes = new RigV3Desktop.AccueilEtat();
Exiger(etatLignes.NbLignesTache("DSTE", "004182") == 1,
    "une tache commence avec une seule ligne");
Exiger(etatLignes.AjouterLigneTache("DSTE", "004182") == 2
       && etatLignes.NbLignesTache("DSTE", "004182") == 2,
    "ajouter une ligne est retenu (une tache interrompue garde ses lignes)");
Exiger(etatLignes.NbLignesTache("DSTE", "0000") == 1,
    "les lignes valent pour UNE affaire, pas pour toutes");
Exiger(etatLignes.RetirerLigneTache("DSTE", "004182") == 1
       && etatLignes.RetirerLigneTache("DSTE", "004182") == 1,
    "retirer une ligne est possible, et ne descend jamais sous une ligne");

Exiger(vueTache.Contains("AjouterLigneTache") && vueTache.Contains("TrousDeLaLigne("),
    "la vue sait ajouter une ligne et dire laquelle est incomplete");
var ficheLignes = pontFiche;
Exiger(ficheLignes.Contains("LigneCourante") && ficheLignes.Contains("CleDuChamp("),
    "la fiche ecrit sous la cle de la ligne saisie");
Exiger(ficheLignes.Contains("Recette.LibelleLigne"),
    "la trace nomme la ligne : trois nominations donnent trois lignes distinctes");

// --- TRAITEMENT EN LOT : la meme recette sur plusieurs affaires.
// PORTE EN REACT (lot 3, 19/11/2026) : le .razor est une coquille qui ouvre la
// session avec l'etat de l'accueil ; les regles vivent dans
// Pont/EcranTachesLot.cs et le balisage dans web/src/ecrans/EcranTachesLot.tsx.
// Les regles lues ci-dessous se lisent donc dans ces deux fichiers, et la
// session est EXECUTEE plus bas sur un etat jetable.
// Blazor retire (19/11/2026) : plus de coquille, le plan de travail ouvre et
// ferme la session du lot (Pont/PlanTravailVue.cs, depuis le lot 2).
Exiger(File.Exists(Path.Combine(racine, "web/src/ecrans/EcranTachesLot.tsx"))
       && !File.Exists(Path.Combine(racine, "Components/Ecrans/EcranTachesLot.razor")),
    "l'ecran de traitement en lot existe (en React)");
var lot = File.ReadAllText(Path.Combine(racine, "Pont/EcranTachesLot.cs")) + Environment.NewLine
        + File.ReadAllText(Path.Combine(racine, "web/src/ecrans/EcranTachesLot.tsx"));
Exiger(balisage.Contains("<EcranTachesLot key={ecran.lot.session} session={ecran.lot.session} version={ecran.lot.version} />")
       && codePlan.Contains("SessionLot = EcranTachesLot.Ouvrir(Etat, Utilisateur, LotDemande);")
       && codePlan.Contains("EcranTachesLot.Parametrer(SessionLot, Etat, Utilisateur, LotDemande)")
       && codePlan.Contains("EcranTachesLot.Fermer(SessionLot);"),
    "le plan de travail monte le lot, ouvre sa session sur l'etat VIVANT de l'accueil et la ferme");
Exiger(vueTache.Contains("case 'PROC_TACHES_LOT':") && vueTache.Contains("<EcranTachesLot"),
    "l'ecran en lot est aiguille dans la vue");
Exiger(Regex.IsMatch(vueTache, @"new\(""PROC_TACHES_LOT"",\s*"""),
    "l'ecran en lot figure au catalogue des traitements");
Exiger(lot.Contains("ChampsObligatoires") && lot.Contains("<th"),
    "le lot montre les champs de la recette EN COLONNES");
Exiger(lot.Contains("ARepetition"),
    "une tache qui se saisit en plusieurs lignes est ECARTEE du lot, avec son motif");
Exiger(lot.Contains("private void Terminer(DossierDuLot d)")
       && !Regex.IsMatch(lot, @"Tout\s*terminer|TerminerTout"),
    "la cloture est PAR DOSSIER : aucun bouton ne clot le lot entier");
Exiger(lot.Contains("Restants(d.Dossier) > 0 || EffetsRestants(d.Dossier) > 0"),
    "un dossier incomplet ne se clot pas, meme au milieu d'un lot");
Exiger(lot.Contains("Etat.NoterModification(d.Dossier"),
    "la saisie en lot ecrit dans la trace DU DOSSIER concerne, pas dans un ailleurs");
Exiger(lot.Contains("Etat.TacheFaite("),
    "un dossier deja termine sort du lot");

// Le lot n'est pas qu'un ecran : il faut qu'il y ait REELLEMENT plusieurs
// affaires a traiter ensemble, sinon la vue tableau ne sert a rien.
var parRecette = RigV3Desktop.JeuDemo.EvenementsAttendus
    .Select(e => (e, r: RigV3Desktop.RecettesTaches.ParCode(e.Code)))
    .Where(x => x.r is not null)
    .GroupBy(x => x.r!.Code)
    .ToDictionary(g => g.Key, g => g.Count());
Exiger(parRecette.TryGetValue("DENO", out var nDeno) && nDeno >= 2,
    "au moins deux affaires attendent la meme recette (le lot a un sens)");

// La file d'alertes MENE au lot : plusieurs dossiers pour une meme recette
// font UNE entree, pas une par dossier.
var alertesLot = new RigV3Desktop.AlertesGreffe().ReleverCalculees();
var entreeLot = alertesLot.FirstOrDefault(x => x.Lot is not null);
Exiger(entreeLot is not null && entreeLot.Lot == "DENO",
    "la file propose UNE entree groupee pour les denominations");
Exiger(entreeLot is not null && entreeLot.Titre.StartsWith("2 dossiers"),
    "l'entree groupee annonce COMBIEN de dossiers elle porte");
// L ARRIERE part en lot, mais ce qui est signale AUJOURD HUI reste une alerte
// NOMMEE : AlertesGreffe.Relever() le dit explicitement (« ce qui arrive
// aujourd hui ne part pas en lot »), sinon le flux EDI entre ce matin serait
// noye dans un compteur et le greffe perdrait le seul cas ou il peut repondre
// le jour meme. Le controle porte donc sur l ARRIERE, pas sur le jour.
var denoIndividuelles = alertesLot
    .Where(x => x.Tache is { } t2 && t2.Code == "DENO")
    .ToArray();
Exiger(denoIndividuelles.All(x => x.Detail.Contains("signale aujourd hui")),
    "aucune denomination de l ARRIERE ne reste en alerte individuelle : le lot les absorbe");
Exiger(alertesLot.Any(x => x.Tache is { } t3 && (t3.Code == "DPER" || t3.Code == "DSTE")),
    "le changement de dirigeant reste dossier par dossier (saisie ligne par ligne)");
Exiger(lot.Contains("CodeRecette"),
    "l'ecran s'ouvre sur la recette que l'alerte nomme");

// Le LOT JOUE par sa session, sur un etat jetable (RIGV3_ETAT ci-dessus).
JsonElement Geste(object? v) => JsonSerializer.SerializeToElement(v);
var etatLot = new RigV3Desktop.AccueilEtat();
var attendusDeno = RigV3Desktop.JeuDemo.EvenementsAttendus
    .Where(e => e.Code == "DENO" && RigV3Desktop.JeuDemo.Demandes.Any(d => d.Dossier == e.Dossier))
    .OrderBy(e => e.Signale).Select(e => e.Dossier).ToArray();
var sLot = RigV3Desktop.Pont.EcranTachesLot.Ouvrir(etatLot, "R. VILAIN", "");
var vLot = RigV3Desktop.Pont.EcranTachesLot.Vue(sLot);
Exiger(vLot.Recette?.Code == "DENO" && vLot.Dossiers.Select(d => d.Dossier).SequenceEqual(attendusDeno),
    "sans recette demandee, l'ecran s'ouvre sur le plus gros lot, ses dossiers du plus ancien au plus recent");
Exiger(vLot.Lots.Any(l => l.ARepetition) && vLot.Lots.Single(l => l.Actif).Code == "DENO",
    "les taches a repetition restent listees a gauche, la tache ouverte est marquee");
var cibleLot = vLot.Dossiers[0];
Exiger(!cibleLot.Pret && cibleLot.Restants > 0, "un dossier sans saisie n'est pas pret a terminer");
RigV3Desktop.Pont.EcranTachesLot.Agir(sLot, "Terminer", Geste(cibleLot.Dossier));
Exiger(!etatLot.TacheFaite("DENO", cibleLot.Dossier),
    "« Terminer » sur un dossier incomplet ne clot rien");
for (var j = 0; j < vLot.Recette!.Champs.Length; j++)
    RigV3Desktop.Pont.EcranTachesLot.Agir(sLot, "Ecrire", Geste(new { dossier = cibleLot.Dossier, champ = j, valeur = "NOUVELLE DENO " + j }));
Exiger(etatLot.Modifications.TryGetValue(cibleLot.Dossier, out var tracesLot)
       && tracesLot.Count == vLot.Recette.Champs.Length
       && tracesLot.All(m => m.Element.StartsWith("Traitement en lot > ") && m.Auteur == "R. VILAIN"),
    "chaque saisie du tableau est notee dans la trace DU dossier, avec son auteur");
var vPropage = RigV3Desktop.Pont.EcranTachesLot.Agir(sLot, "ToutPropager", Geste(cibleLot.Dossier));
Exiger(vPropage.Dossiers.First(d => d.Dossier == cibleLot.Dossier) is { Pret: true, EffetsRestants: 0 },
    "« tout » coche les publications : le dossier devient pret");
var vClos = RigV3Desktop.Pont.EcranTachesLot.Agir(sLot, "Terminer", Geste(cibleLot.Dossier));
Exiger(etatLot.TacheFaite("DENO", cibleLot.Dossier) && vClos.Dossiers.All(d => d.Dossier != cibleLot.Dossier)
       && vClos.Dossiers.Length == attendusDeno.Length - 1
       && vClos.DernierMessage == cibleLot.Dossier + " termine. Son alerte ne reviendra plus.",
    "la cloture vaut pour CE dossier seul, qui sort du lot ; les autres restent");
Exiger(RigV3Desktop.Pont.EcranTachesLot.Parametrer(sLot, etatLot, "R. VILAIN", "DSTE")
       && RigV3Desktop.Pont.EcranTachesLot.Vue(sLot).Recette is { ARepetition: true, LibelleLigne: "dirigeant" },
    "rouvrir l'ecran sur une autre recette change la vue ; une tache a repetition y est refusee, motif nomme");
RigV3Desktop.Pont.EcranTachesLot.Fermer(sLot);

// --- RELECTURE EN BASE des recettes sigle / forme juridique / nom d'usage
// (ULT_OP_ET_EVENEMENT_RCS et CODE_EVENEMENT_RCS, RIG_AMIENS, 16/09/2026).
// Ces valeurs viennent d'une lecture, pas d'une deduction : si quelqu'un les
// reecrit de memoire, ce test le voit.
var sigl = RigV3Desktop.RecettesTaches.ParCode("SIGL")!;
var form = RigV3Desktop.RecettesTaches.ParCode("FORM")!;
var nusa = RigV3Desktop.RecettesTaches.ParCode("NUSA")!;

Exiger(sigl.Champs.Length == 1 && sigl.Champs[0].Colonne == "SIGLE"
       && sigl.Champs[0].Obligatoire,
    "SIGL : un seul champ declare en base (INSCRIT_PM.SIGLE), obligatoire");

foreach (var col in new[] { "FORME_JURIDIQUE_UTI", "TEXTE_FORME", "ASSOCIE_UNIQUE", "BODACC_OUI" })
{
    Exiger(form.Champs.Any(c => c.Table == "INSCRIT_PM" && c.Colonne == col),
        $"FORM porte le champ declare en base « {col} »");
}
Exiger(form.Champs.Length == 4,
    "FORM : les QUATRE champs declares en base, pas seulement la forme juridique");
Exiger(form.ChampsObligatoires.Length == 1
       && form.ChampsObligatoires[0].Colonne == "FORME_JURIDIQUE_UTI",
    "FORM : seule la forme juridique est exigee (choix produit assume, la base n'exige rien)");

Exiger(nusa.Champs.Length == 2
       && nusa.Champs.Any(c => c.Colonne == "NOM_USAGE" && c.Obligatoire)
       && nusa.Champs.Any(c => c.Colonne == "CIVILITE" && !c.Obligatoire),
    "NUSA : nom d'usage exige, civilite proposee -- comme la base le declare");

// Les codes Infogreffe sont DIFFERENTS par evenement : c'est la valeur la
// plus facile a recopier de travers.
foreach (var (code, infog) in new[] { ("SIGL", "4011"), ("FORM", "4012"), ("NUSA", "4035"),
                                      ("DENO", "4028"), ("DSTE", "4206") })
{
    var r = RigV3Desktop.RecettesTaches.ParCode(code)!;
    Exiger(r.Effets.Single(e => e.Canal == "infogreffe").Code == infog,
        $"{code} : code Infogreffe {infog}, tel que la base le declare");
}
Exiger(!nusa.Effets.Any(e => e.Canal == "bodacc"),
    "NUSA n'a AUCUNE publication BODACC : la base laisse les trois colonnes vides");
Exiger(form.Effets.Any(e => e.Canal == "bodacc") && sigl.Effets.Any(e => e.Canal == "bodacc"),
    "SIGL et FORM publient au BODACC (B0111/B0112/B0113)");
Exiger(RigV3Desktop.RecettesTaches.Liste.All(
        r => r.Effets.All(e => e.Source.StartsWith("CODE_EVENEMENT_RCS."))),
    "CHAQUE effet de CHAQUE recette nomme la colonne de base qui le declare");

// --- REFERENTIEL -> COLONNE REELLE. C'est cette regle qui decidera si le
// branchement sur la base trouve les champs ou echoue EN SILENCE : une
// colonne introuvable ne leve rien, elle rend du vide.
var R = (Func<string, string, RigV3Desktop.CibleColonne>)RigV3Desktop.ColonnesRig.Resoudre;

// Les prefixes sont LUS en base, pas deduits du nom : DIRIGEANT donne
// DRGNT, DOSSIER_RCS donne DSSRC. Une regle mecanique se tromperait.
foreach (var (tbl, attr, attendu) in new[]
{
    ("INSCRIT_PP",   "PATRONYME",          "INSPP_PATRONYME"),
    ("INSCRIT_PM",   "DENOMINATION_USAGE", "INSPM_DENOMINATION_USAGE"),
    ("ETABLISSEMENT","NOM_DOMAINE",        "ETBLS_NOM_DOMAINE"),
    ("DOSSIER_RCS",  "DATE_CESSATION",     "DSSRC_DATE_CESSATION"),
    ("DIRIGEANT",    "FONCTION_DIRIGEANT", "DRGNT_FONCTION_DIRIGEANT"),
    ("DIRIGEANT_PP", "PATRONYME",          "DRGPP_PATRONYME"),
    ("ADRESSE_BASE", "ID_ADRBS",           "ADRBS_ID_ADRBS"),
})
{
    var cibleCol = R(tbl, attr);
    Exiger(cibleCol.Resolue && cibleCol.Colonne == attendu,
        $"{tbl}.{attr} se lit « {attendu} » en base");
}

// Le « X » de tete est un prefixe de PROCESSUS : la table est reelle.
Exiger(RigV3Desktop.ColonnesRig.TableReelle("XINSCRIT_PP") == "INSCRIT_PP",
    "XINSCRIT_PP designe la vraie table INSCRIT_PP (X = prefixe de processus)");
Exiger(R("XINSCRIT_PP", "INSAI_AUTRES_BIENS_LIEU").Colonne == "INSPP_INSAI_AUTRES_BIENS_LIEU",
    "un champ vu sous XINSCRIT_PP se resout dans INSCRIT_PP");
Exiger(RigV3Desktop.ColonnesRig.TableReelle("XMACHIN") == "XMACHIN",
    "une table inconnue commencant par X n'est PAS amputee de son X");

// Le « # » est un titre de bloc : il ne doit JAMAIS produire une colonne.
Exiger(!R("#TITRE", "#PRESENCE_SALARIES").Resolue
       && !R("INSCRIT_PP", "#INSAISISSABILITE").Resolue,
    "un nom prefixe « # » ne produit aucune colonne : c'est un bloc d'ecran");

// ECHEC BRUYANT : une table dont le prefixe n'a pas ete lu est REFUSEE,
// jamais devinee -- sinon le branchement chercherait une colonne inventee.
var inconnue = R("TABLE_JAMAIS_LUE", "CHAMP");
Exiger(!inconnue.Resolue && inconnue.Motif.Contains("inconnue"),
    "une table au prefixe non lu est refusee avec son motif, pas devinee");
Exiger(inconnue.Colonne.Length == 0,
    "un echec ne rend AUCUN nom de colonne plausible");
Exiger(R("INSCRIT_PP", "INSPP_PATRONYME").Colonne == "INSPP_PATRONYME",
    "un attribut deja prefixe n'est pas prefixe deux fois");

// LE GARDE-FOU : tous les champs de toutes les recettes doivent se
// resoudre. Si quelqu'un ajoute demain une recette sur une table dont le
// prefixe n'a pas ete lu, ce test devient rouge AVANT le branchement.
var orphelins = RigV3Desktop.ColonnesRig.NonResolus(RigV3Desktop.RecettesTaches.Liste);
Exiger(orphelins.Length == 0,
    orphelins.Length == 0
        ? "les champs des 12 recettes se resolvent tous en colonne reelle"
        : $"champs non resolus : {string.Join(" | ", orphelins.Select(o => $"{o.Recette.Code} {o.Champ.Colonne} -> {o.Motif}"))}");

// L'EPREUVE en base doit rester tracee : sans elle, ces noms ne sont que
// plausibles. Le fichier porte les chiffres mesures, avec leur date.
var colonnesRig = File.ReadAllText(Path.Combine(racine, "Metier/ColonnesRig.cs"));
Exiger(colonnesRig.Contains("EPREUVE EN CONDITIONS REELLES")
       && colonnesRig.Contains("RIG_AMIENS@SQL-PROD"),
    "la regle porte la trace de son epreuve sur la base de production");
foreach (var col in new[] { "ETBLS_NOM_DOMAINE", "INSPP_PATRONYME", "DRGNT_FONCTION_DIRIGEANT",
                            "DRGPP_PATRONYME", "ADRBS_ID_ADRBS", "DSSRC_DATE_CESSATION",
                            "INSPM_DENOMINATION_USAGE" })
{
    Exiger(colonnesRig.Contains(col),
        $"l'epreuve nomme la colonne reellement lue « {col} »");
}
// Chaque table declaree doit avoir ete eprouvee : un prefixe ajoute sans
// epreuve rendrait des noms plausibles mais non verifies.
foreach (var t in RigV3Desktop.ColonnesRig.Prefixes)
{
    Exiger(colonnesRig.Contains(t.Value + "_"),
        $"le prefixe « {t.Value} » ({t.Key}) apparait dans l'epreuve");
}

// La regle ne doit pas rester du code mort : la vue montre la colonne
// REELLE, et seulement quand elle est resolue (sinon 500 champs hors
// recette afficheraient un motif d'echec inutile).
var ficheCol = pontFiche;
Exiger(ficheCol.Contains("ColonnesRig.Resoudre(") && ficheCol.Contains("reelle.Resolue"),
    "la fiche montre la colonne reelle, et seulement si elle est resolue");
Exiger(lot.Contains("ColonnesRig.Resoudre("),
    "le tableau du lot montre aussi la colonne reelle en infobulle");

// --- RELECTURE EN BASE des SEPT dernieres recettes (16/09/2026, memes
// tables). Les canaux etaient tous exacts ; les CHAMPS manquaient.
foreach (var (code, cols) in new (string, string[])[]
{
    ("NOPA",  ["PATRONYME"]),
    ("PRENO", ["PRENOMS_ETAT_CIVIL", "PRENOM_USAGE"]),
    ("PSEU",  ["PSEUDONYME", "PRENOM_PSEUDO", "SURNOM"]),
    ("COLL",  ["CONJOINT_COLLABO", "CONJOINT_SALARIE", "CODE_SITUATION"]),
    ("CESM",  ["DATE_CESSATION"]),
    ("RADI",  ["DATE_CESSATION", "CESS_DECES", "DATE_CES_EMPLOI_SALARIE"]),
})
{
    var r = RigV3Desktop.RecettesTaches.ParCode(code)!;
    foreach (var col in cols)
        Exiger(r.Champs.Any(c => c.Colonne == col),
            $"{code} porte le champ declare en base « {col} »");
    Exiger(r.Champs.Length == cols.Length,
        $"{code} porte EXACTEMENT les {cols.Length} champ(s) que la base declare");
}
var diss = RigV3Desktop.RecettesTaches.ParCode("DISS")!;
foreach (var col in new[] { "DATE_DISSOLUTION", "JOURNAL_ANNONCES", "DISSO_JAL_DATE",
                            "LIEU_LIQUIDATION_CFE", "ID_ADRBS" })
{
    Exiger(diss.Champs.Any(c => c.Colonne == col),
        $"DISS porte le champ declare en base « {col} »");
}
Exiger(diss.ChampsObligatoires.Length == 1
       && diss.ChampsObligatoires[0].Colonne == "DATE_DISSOLUTION",
    "DISS : seule la date est exigee -- une parution au JAL pas encore connue ne bloque pas");

// Les codes INPI ne sont PAS tous « 0F » : c'est l'autre valeur facile a
// recopier de travers d'une recette a l'autre.
foreach (var (code, inpi) in new[] { ("NOPA", "10P"), ("PSEU", "15P"), ("COLL", "30P"),
                                     ("RADI", "41P"), ("PRENO", "0F"), ("DISS", "0F") })
{
    var r = RigV3Desktop.RecettesTaches.ParCode(code)!;
    Exiger(r.Effets.Single(e => e.Canal == "inpi").Code == inpi,
        $"{code} : code INPI {inpi}, tel que la base le declare");
}
// BODACC : seuls DISS et CESM publient parmi ces sept.
foreach (var code in new[] { "NOPA", "PRENO", "PSEU", "COLL", "RADI" })
{
    Exiger(!RigV3Desktop.RecettesTaches.ParCode(code)!.Effets.Any(e => e.Canal == "bodacc"),
        $"{code} ne publie PAS au BODACC (les trois colonnes sont vides en base)");
}
foreach (var code in new[] { "DISS", "CESM" })
{
    Exiger(RigV3Desktop.RecettesTaches.ParCode(code)!.Effets.Any(e => e.Canal == "bodacc"),
        $"{code} publie au BODACC (B0111 / B0112 / B0113)");
}
// La notification aux secondaires n'est PAS uniforme : non pour CESM et COLL.
foreach (var code in new[] { "CESM", "COLL" })
{
    Exiger(RigV3Desktop.RecettesTaches.ParCode(code)!
            .Effets.All(e => !e.Canal.StartsWith("notif") || !e.Obligatoire),
        $"{code} ne notifie aucun etablissement : la base dit non aux deux");
}

// Les releves d'alertes ont suivi l'accueil dans sa session (Pont/Accueil.cs,
// lot 1 du passage a React) : le filtre des taches closes s'y lit.
Exiger(pontAccueil.Contains("TacheDejaFaite(") && pontAccueil.Contains("RecettesTaches.ParCode(code) is { } r"),
    "l'alerte DPER s'eteint quand la tache a ete close sous le code DSTE");

Exiger(vueTache.Contains("TachePeutSeClore()") && vueTache.Contains("EffetsRestants()"),
    "la cloture exige AUSSI que la propagation soit traitee");
Exiger(vueTache.Contains("Ce que ce changement entraine"),
    "la tache montre les endroits impactes");
Exiger(vueTache.Contains("RaisonBlocage()"),
    "le bouton dit CE QUI manque, il ne se contente pas d'etre grise");

Exiger(vueTache.Contains("Terminer la tache") && vueTache.Contains("ChampsManquants()"),
    "la vue porte le bouton « Terminer la tache » et son controle de completude");
// Depuis le 19/11/2026 le plan lit les TACHES ouvertes (TachesOuvertes) : la file
// d'alertes (AlertesOuvertes) ne porte plus aucune tache depuis le 17/09/2026. Les
// deux appliquent le meme filtre des taches terminees (TacheDejaFaite).
Exiger(vueTache.Contains("Accueil.TachesOuvertes()")
       && !Regex.IsMatch(vueTache, @"SondeAlertes\.(Relever|ReleverCalculees)\(\)"),
    "la file d'alertes EXCLUT les taches deja terminees");
Exiger(Regex.IsMatch(pontAccueil, @"Alertes = AlertesOuvertes\(\);\s*Taches = TachesOuvertes\(\);")
       && !Regex.IsMatch(pontAccueil, @"Alertes\s*=\s*SondeAlertes\.Relever\(\)")
       && vue.Contains("Accueil.ReleverAlertes();"),
    "la session de l'accueil releve sa file sans les taches terminees, et Home la fait relire a la cloture");

// --- PONTS DU LOT 6, EXECUTES (19/11/2026). Les regles de l'arbre, de la fiche
// et de la fenetre detachee ont quitte les .razor pour Pont/ : on les joue ici
// comme la coquille et React les jouent, sur le jeu de demonstration.
{
    var ouverts = RigV3Desktop.AccesGed.NouveauxMasques();
    ouverts.Add(racineGed);
    var index = new Dictionary<string, RigV3Desktop.NoeudGed>();
    RigV3Desktop.Pont.LigneArbreVue VueArbre(RigV3Desktop.NoeudGed? selection = null, string[]? domaines = null,
                                             string? filtre = null, RigV3Desktop.NoeudGed? racineVue = null)
    {
        index.Clear();
        return RigV3Desktop.Pont.ArbreGedNoeud.Vue(racineVue ?? racineGed, 0, ouverts, selection, domaines ?? tous,
                                                   null, null, null, null, null, filtre, index);
    }
    IEnumerable<RigV3Desktop.Pont.LigneArbreVue> Lignes(RigV3Desktop.Pont.LigneArbreVue l)
        => new[] { l }.Concat(l.Enfants.SelectMany(Lignes));

    var arbreVu = VueArbre();
    Exiger(arbreVu.Ouvert && arbreVu.Enfants.Length == RigV3Desktop.AccesGed.EnfantsVisibles(racineGed, tous).Count
           && arbreVu.Enfants.All(e => !e.Ouvert && e.Enfants.Length == 0),
        "pont de l'arbre : la racine ouverte rend ses enfants visibles, une branche fermee ne rend rien dessous");
    Exiger(index.Count == Lignes(arbreVu).Count()
           && Lignes(arbreVu).All(l => index.ContainsKey(l.Id))
           && index[arbreVu.Id] is { } r0 && ReferenceEquals(r0, racineGed)
           && VueArbre().Enfants.Select(e => e.Id).SequenceEqual(arbreVu.Enfants.Select(e => e.Id)),
        "pont de l'arbre : chaque ligne rendue garde un identifiant STABLE qui retrouve SON noeud");
    Exiger(!VueArbre(domaines: [RigV3Desktop.ArbreDemo.DomaineRcs]).Enfants.Any(e => e.Texte == "Judiciaire")
           && arbreVu.Enfants.Any(e => e.Texte == "Judiciaire"),
        "pont de l'arbre : un dossier reserve a un autre domaine ne se rend pas");

    var brancheRcs = racineGed.Enfants.First(e => e.Libelle == "Dossier RCS");
    RigV3Desktop.Pont.ArbreGedNoeud.Activer(brancheRcs, ouverts, tous, null, null, null);
    var deplie = VueArbre().Enfants.First(e => e.Texte == "Dossier RCS");
    RigV3Desktop.Pont.ArbreGedNoeud.Activer(brancheRcs, ouverts, tous, null, null, null);
    var replie = VueArbre().Enfants.First(e => e.Texte == "Dossier RCS");
    Exiger(deplie.Ouvert && deplie.Enfants.Length > 0 && deplie.Depliable && !replie.Ouvert && replie.Enfants.Length == 0,
        "pont de l'arbre : un clic deplie un dossier, un second le replie");
    var pieceRcs = brancheRcs.Enfants.First(e => e.EstPiece);
    var avantClic = ouverts.Count;
    RigV3Desktop.Pont.ArbreGedNoeud.Activer(pieceRcs, ouverts, tous, null, null, null);
    Exiger(ouverts.Count == avantClic, "pont de l'arbre : un clic sur une piece ne deplie rien (elle se selectionne)");

    ouverts.Add(brancheRcs);
    var avecSelection = VueArbre(selection: pieceRcs);
    var lignePiece = Lignes(avecSelection).First(l => index[l.Id] == pieceRcs);
    Exiger(avecSelection.Chemin && avecSelection.Enfants.First(e => e.Texte == "Dossier RCS").Chemin
           && lignePiece.Actif && !lignePiece.Chemin,
        "pont de l'arbre : la piece choisie est active, TOUS ses parents portent le chemin");
    ouverts.Remove(brancheRcs);

    var filtreArbre = VueArbre(filtre: "statuts");
    Exiger(Lignes(filtreArbre).Any(l => l.Texte.Contains("Statuts", StringComparison.OrdinalIgnoreCase))
           && filtreArbre.Enfants.All(e => e.Ouvert)
           && !filtreArbre.Enfants.Any(e => e.Texte == "Judiciaire"),
        "pont de l'arbre : le filtre ouvre les branches qui menent a un resultat et cache les autres");

    var dossierVide = RigV3Desktop.AccesGed.NouveauDossier("Vide");
    ouverts.Add(dossierVide);
    var vueVide = VueArbre(racineVue: dossierVide);
    Exiger(vueVide.Vide && !vueVide.Depliable && vueVide.Compte is null && vueVide.Enfants.Length == 0,
        "pont de l'arbre : un dossier ouvert sans piece annonce « aucune piece »");
    Exiger(arbreVu.Compte == racineGed.NbPieces && arbreVu.Icone == RigV3Desktop.AccesGed.CodeIcone(racineGed),
        "pont de l'arbre : le compte de pieces et l'icone viennent du metier");

    // Fenetre detachee : la piece et sa decoupe en pages, par le passe-plat.
    var partageDoc = new RigV3Desktop.DocumentPartage();
    var pieceDoc = racineGed.Descendants().First(n => n.EstPiece && !n.APdf);
    Exiger(RigV3Desktop.Pont.DocumentDetacheVue.Vue(partageDoc.Emplacement(1)) is null,
        "pont de la fenetre detachee : sans piece ouverte, la vue le dit (null)");
    partageDoc.Publier(2, pieceDoc, [("SIREN", "903 118 776")]);
    var vueDoc = RigV3Desktop.Pont.DocumentDetacheVue.Vue(partageDoc.Emplacement(2))!;
    Exiger(vueDoc.Libelle == pieceDoc.Libelle && vueDoc.Feuilles.Length == Math.Max(1, pieceDoc.Pages)
           && vueDoc.Feuilles.Select(f => f.Texte).SequenceEqual(
                  Enumerable.Range(1, vueDoc.Feuilles.Length).Select(f => RigV3Desktop.DocumentPartage.TextePage(pieceDoc, f)))
           && vueDoc.Valeurs.Single() is { Libelle: "SIREN", Valeur: "903 118 776" },
        "pont de la fenetre detachee : la piece de SON rang, decoupee par DocumentPartage.TextePage");
    Exiger(RigV3Desktop.Pont.DocumentDetacheVue.Vue(partageDoc.Emplacement(1)) is null,
        "pont de la fenetre detachee : chaque rang garde son document (le rang 1 reste vide)");

    // Fiche : la session de Pont/FicheInformationsDossier.cs, comme la coquille l'ouvre.
    var etatFiche = new RigV3Desktop.AccueilEtat();
    var ficheOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    System.Text.Json.JsonElement J(object? o) => System.Text.Json.JsonSerializer.SerializeToElement(o, ficheOpts);
    RigV3Desktop.Pont.ParametresFiche P(RigV3Desktop.RecetteTache? r, int ligne = 1)
        => new("004182", "Dossier RCS", RigV3Desktop.InformationsDossier.LibelleNoeud,
               RigV3Desktop.InformationsDossier.Sections, RigV3Desktop.InformationsDossier.ProcessusSources,
               r, ligne, etatFiche, r is null);

    var sDeno = RigV3Desktop.Pont.FicheInformationsDossier.Ouvrir(P(deno));
    var vDeno = RigV3Desktop.Pont.FicheInformationsDossier.Vue(sDeno);
    var champsDeno = vDeno.Sections.SelectMany(x => x.Champs).ToArray();
    Exiger(vDeno.Tache is { Code: "DENO", TousChamps: false } && vDeno.Frise is null && vDeno.Remplissage is null
           && champsDeno.Length == RigV3Desktop.InformationsDossier.Sections
                  .Sum(x => x.Champs.Count(c => deno.GardeChamp(x.Table, c.Colonne))),
        "pont de la fiche : en mode tache, la fiche ne montre QUE les champs de la recette");
    var ouvreTout = RigV3Desktop.Pont.FicheInformationsDossier.Agir(sDeno, "tousChamps", J(null));
    var refermeTout = RigV3Desktop.Pont.FicheInformationsDossier.Agir(sDeno, "tousChamps", J(null));
    Exiger(ouvreTout.Rappels.SequenceEqual(["toutAffiche"]) && ouvreTout.Tache!.TousChamps
           && ouvreTout.Remplissage is not null && refermeTout.Rappels.Length == 0,
        "pont de la fiche : « Afficher tous les champs » rouvre la fiche ET leve ToutAffiche (le retour ne compte pas)");
    Exiger(champsDeno.Any(c => c.TitreColonne.StartsWith("En base : "))
           && champsDeno.All(c => c.TitreColonne.StartsWith("En base : ") || c.TitreColonne == c.Cle),
        "pont de la fiche : l'infobulle montre la colonne reelle quand elle est resolue, la cle sinon");
    RigV3Desktop.Pont.FicheInformationsDossier.Fermer(sDeno);

    var repetable = dir.Champs.FirstOrDefault(c => c.Repetable && RigV3Desktop.InformationsDossier.Sections
        .Any(x => x.Table == c.Table && x.Champs.Any(ch => ch.Colonne == c.Colonne)));
    Exiger(repetable is not null, "pont de la fiche : la recette dirigeant a un champ repetable dans la fiche");
    if (repetable is not null)
    {
        var sDir = RigV3Desktop.Pont.FicheInformationsDossier.Ouvrir(P(dir, ligne: 2));
        var bloc = Array.FindIndex(RigV3Desktop.InformationsDossier.Sections, x => x.Table == repetable.Table
                                   && x.Champs.Any(ch => ch.Colonne == repetable.Colonne));
        var rang = Array.FindIndex(RigV3Desktop.InformationsDossier.Sections[bloc].Champs, ch => ch.Colonne == repetable.Colonne);
        var apresSaisie = RigV3Desktop.Pont.FicheInformationsDossier.Agir(sDir, "saisir", J(new { s = bloc, c = rang, valeur = "MARTIN" }));
        var ecriture = etatFiche.Modifications["004182"].Last();
        Exiger(ecriture.Colonne == RigV3Desktop.RecetteTache.Cle(repetable, 2) && ecriture.Colonne.Contains("[2]")
               && ecriture.Element.EndsWith($"({dir.LibelleLigne} 2)") && ecriture.Apres == "MARTIN"
               && apresSaisie.Rappels.SequenceEqual(["champModifie"]),
            "pont de la fiche : un champ repetable s'ecrit sous la cle de SA ligne, la trace nomme la ligne");
        RigV3Desktop.Pont.FicheInformationsDossier.Fermer(sDir);
    }
}

try { File.Delete(etatBanc); } catch (IOException) { }

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("GED : tout est vert.");
    return 0;
}
Console.WriteLine($"GED : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
