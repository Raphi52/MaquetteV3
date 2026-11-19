using RigV3Desktop;

// Banc de l'arbre GED CONSERVE d'une session a l'autre.
//
// Defaut a l'origine de ce test (16/09/2026) : tout ce que l'utilisateur
// faisait dans l'arbre d'une affaire — creer un dossier ou une piece
// (bouton « Nouveau »), renommer, supprimer (= masquer) — ne vivait que
// dans trois collections de Home.razor indexees par REFERENCE d'objet.
// L'arbre etant reconstruit a neuf a chaque ouverture (ArbreDemo.Racine),
// ces gestes disparaissaient a la fermeture de l'application.
//
// Ce banc ECRIT REELLEMENT le fichier d'etat (RIGV3_ETAT le detourne vers
// un dossier temporaire, jamais l'etat de l'utilisateur), le RELIT, et
// repose l'etat sur un arbre neuf : c'est l'aller-retour complet, pas une
// lecture de texte.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var dossierTemp = Path.Combine(Path.GetTempPath(), "rigv3-persistance-ged-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dossierTemp);
var fichier = Path.Combine(dossierTemp, "accueil.json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", fichier);

const string affaire = "2026B01067";

// ---------- Session 1 : l'utilisateur travaille dans l'arbre ----------
var arbre1 = ArbreDemo.Racine(affaire, "AMIENS BILLARDS", "109 678 235");
var ajouts = AccesGed.NouveauxAjouts();
var masques = AccesGed.NouveauxMasques();
var libelles = AccesGed.NouveauxLibelles();

var dossierRcs = arbre1.Enfants.First(e => e.Libelle.Contains("Dossier RCS"));
var aMasquer = dossierRcs.Descendants().First(d => d.EstPiece);
var aRenommer = arbre1.Enfants.First();

// creation d'un dossier, puis d'une piece SOUS ce dossier (imbrication)
var nouveauDossier = AccesGed.NouveauDossier("Pieces du greffier");
ajouts[dossierRcs] = new List<NoeudGed> { nouveauDossier };
var nouvellePiece = AccesGed.NouvellePiece("Note interne", "pdf", "16/09/2026");
ajouts[nouveauDossier] = new List<NoeudGed> { nouvellePiece };
masques.Add(aMasquer);
libelles[aRenommer] = "Dossier RCS (renomme)";

var etat1 = AccueilEtat.Charger();
etat1.EnregistrerArbreGed(affaire, PersistanceGed.Capturer(arbre1, ajouts, masques, libelles));

Exiger(File.Exists(fichier), "l'arbre est REELLEMENT ecrit sur disque");
Exiger(File.ReadAllText(fichier).Contains("Pieces du greffier"),
    "le fichier d'etat porte l'element cree a la main");

// ---------- Session 2 : l'application redemarre, arbre NEUF ----------
var arbre2 = ArbreDemo.Racine(affaire, "AMIENS BILLARDS", "109 678 235");
var ajouts2 = AccesGed.NouveauxAjouts();
var masques2 = AccesGed.NouveauxMasques();
var libelles2 = AccesGed.NouveauxLibelles();
var etat2 = AccueilEtat.Charger();

Exiger(!ReferenceEquals(arbre1, arbre2), "l'arbre de la session 2 est un objet NEUF");
Exiger(etat2.ArbreGedDe(affaire) is not null, "l'etat relu porte l'arbre de l'affaire");

PersistanceGed.Appliquer(arbre2, etat2.ArbreGedDe(affaire), ajouts2, masques2, libelles2);

var dossierRcs2 = arbre2.Enfants.First(e => e.Libelle.Contains("Dossier RCS"));
var repose = ajouts2.TryGetValue(dossierRcs2, out var listeRepose) ? listeRepose : new List<NoeudGed>();
Exiger(repose.Any(n => n.Libelle == "Pieces du greffier"),
    "le dossier cree revient SOUS le meme dossier de l'arbre neuf");

var dossierRepose = repose.FirstOrDefault(n => n.Libelle == "Pieces du greffier");
Exiger(dossierRepose is not null
       && ajouts2.TryGetValue(dossierRepose, out var sous)
       && sous.Any(n => n.Libelle == "Note interne" && n.Extension == "pdf"),
    "la piece posee SOUS l'element cree revient elle aussi (imbrication)");

var visibles = AccesGed.EnfantsVisibles(dossierRcs2, ["RCS", "JUD"], ajouts2, masques2);
Exiger(visibles.Any(n => n.Libelle == "Pieces du greffier"),
    "l'element cree est VISIBLE dans l'arbre de la session 2");

var masqueRepose = masques2.FirstOrDefault(n => n.Libelle == aMasquer.Libelle);
Exiger(masqueRepose is not null, "l'element supprime reste masque apres relecture");
Exiger(arbre2.Descendants().Any(d => d.Libelle == aMasquer.Libelle),
    "masquer n'est PAS supprimer : le jeu de demonstration est intact");

var renommeRepose = arbre2.Enfants.First();
Exiger(libelles2.TryGetValue(renommeRepose, out var nom) && nom == "Dossier RCS (renomme)",
    "le renommage revient sur le bon noeud");

// ---------- L'arbre revenu a l'etat d'origine n'encombre pas le fichier ----------
var etat3 = AccueilEtat.Charger();
etat3.EnregistrerArbreGed(affaire, new ArbreGedEnregistre());
Exiger(AccueilEtat.Charger().ArbreGedDe(affaire) is null,
    "un arbre sans aucun changement est retire de l'etat");

// ---------- Un etat illisible ne fait pas tomber l'ecran ----------
var avant = ajouts2.Count;
PersistanceGed.Appliquer(arbre2, null, ajouts2, masques2, libelles2);
Exiger(ajouts2.Count == 0 && masques2.Count == 0 && libelles2.Count == 0 && avant > 0,
    "sans etat conserve, l'arbre repart propre (aucune exception)");

// ---------- L'ecran appelle bien ces deux points ----------
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var vue = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
Exiger(vue.Contains("PersistanceGed.Appliquer("),
    "Home.razor repose l'arbre conserve a l'ouverture d'une affaire");
Exiger(vue.Contains("PersistanceGed.Capturer("),
    "Home.razor retient l'arbre apres un geste de l'utilisateur");
foreach (var appel in new[] { "MemoriserArbre();", "PoserLibelle(" })
    Exiger(vue.Contains(appel), $"Home.razor appelle {appel}");

// ---------- Kbis : greffe, denomination et adresse du siege VIENNENT DU FORMULAIRE ----------
// PORTE EN REACT (lot 6, 19/11/2026) : Components/ApercuKbis.razor n'est plus
// qu'une coquille. Les valeurs de l'extrait se calculent dans Pont/ApercuKbis.cs
// (lu ET execute ci-dessous), la mise en page dans web/src/composants/ApercuKbis.tsx.
var coquilleKbis = File.ReadAllText(Path.Combine(racine, "Components/ApercuKbis.razor"));
var tsxKbis = File.ReadAllText(Path.Combine(racine, "web/src/composants/ApercuKbis.tsx"));
var kbis = File.ReadAllText(Path.Combine(racine, "Pont/ApercuKbis.cs"));
Exiger(coquilleKbis.Contains("<IleReact Composant=\"ApercuKbis\"")
       && coquilleKbis.Contains("ApercuKbis.Vue(Numero, Siren, Denomination, Lire)")
       && tsxKbis.Contains("className=\"rig-kbis\""),
    "la coquille du Kbis passe la lecture du formulaire au pont et monte l'extrait React");
Exiger(vue.Contains("\"INSPM_DENOMINATION_STATUT\""),
    "le formulaire porte la denomination statutaire (INSCRIT_PM.DENOMINATION_STATUT)");
Exiger(vue.Contains("\"ETBLS_ID_ADRBS\""),
    "le formulaire porte l'adresse du siege (ETABLISSEMENT.ID_ADRBS)");
Exiger(vue.Contains("\"ETBLS_CODE_GREFFE\""),
    "le formulaire porte le code greffe (ETABLISSEMENT.CODE_GREFFE)");
Exiger(kbis.Contains("Val(\"IDENTITE\", \"INSPM_DENOMINATION_STATUT\""),
    "le Kbis imprime la denomination SAISIE");
Exiger(kbis.Contains("(\"Adresse du siege\", Val(\"SIEGE\", \"ETBLS_ID_ADRBS\"))"),
    "le Kbis imprime l'adresse du siege, et non le TYPE d'adresse");
Exiger(!(kbis + tsxKbis + coquilleKbis).Contains("Greffe du Tribunal de Commerce d'Amiens"),
    "le greffe n'est plus ecrit en dur dans l'apercu Kbis");

// L'extrait calcule par le pont, sur un formulaire saisi.
var saisieKbis = new Dictionary<(string, string), string>
{
    [("SIEGE", "ETBLS_CODE_GREFFE")] = "7501 - PARIS",
    [("IDENTITE", "INSPM_DENOMINATION_STATUT")] = "NOUVELLE SAS",
    [("IDENTITE", "DSSRC_DATE_IMMAT")] = "2014-03-12",
    [("SIEGE", "ETBLS_ID_ADRBS")] = "3 rue de Rivoli 75001 PARIS",
    [("CAPITAL", "CPTL_MONTANT")] = "10000",
};
string LireKbis(string etape, string colonne) => saisieKbis.TryGetValue((etape, colonne), out var v) ? v : "";
var extrait = RigV3Desktop.Pont.ApercuKbis.Vue("004176", "812 445 901", "SCI DU PARC", LireKbis);
string? Ligne(string titre) => extrait.Sections.SelectMany(x => x.Lignes).FirstOrDefault(l => l.Titre == titre)?.Libelle;
Exiger(extrait.GreffeLibelle == Greffes.Trouver("7501 - PARIS").Libelle && extrait.VilleRcs == "PARIS"
       && Ligne("Immatriculation au RCS, numero") == "812 445 901 R.C.S. PARIS",
    "le Kbis calcule prend l'entete et la ville du greffe SAISI (execute)");
Exiger(Ligne("Denomination ou raison sociale") == "NOUVELLE SAS"
       && Ligne("Adresse du siege") == "3 rue de Rivoli 75001 PARIS"
       && Ligne("Date d'immatriculation") == "12/03/2014",
    "le Kbis calcule imprime la denomination, l'adresse et la date saisies (execute)");
Exiger(Ligne("Capital social") == 10000m.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " EUR"
       && extrait.Gestion == "—"
       && extrait.Sections.Length == 1,
    "le capital en N2 fr-FR, la gestion par defaut, et les sections vides masquees comme la XSL (execute)");
var sansSaisie = RigV3Desktop.Pont.ApercuKbis.Vue("004176", "812 445 901", "SCI DU PARC", (_, _) => "");
Exiger(sansSaisie.GreffeLibelle == Greffes.Defaut.Libelle
       && sansSaisie.Sections.Single().Lignes.Single(l => l.Titre == "Denomination ou raison sociale").Libelle == "SCI DU PARC",
    "sans saisie, le greffe par defaut et la denomination de l'affaire (execute)");
Exiger(Greffes.Trouver("8001 - AMIENS").Ville == "AMIENS", "le code greffe saisi retrouve son greffe");
Exiger(Greffes.Trouver("7501 - PARIS").Libelle.Contains("Paris"), "un autre greffe change l'entete");
Exiger(Greffes.Trouver("").Code == Greffes.Defaut.Code, "sans saisie, l'entete garde le greffe par defaut");
Exiger(Greffes.Choix.Length == Greffes.Tous.Length, "le formulaire propose tous les greffes");

try { Directory.Delete(dossierTemp, true); } catch { }

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine($"PersistanceGedCheck : VERT ({echecs.Count} echec)");
    return 0;
}
Console.WriteLine($"PersistanceGedCheck : ROUGE, {echecs.Count} echec(s)");
return 1;
