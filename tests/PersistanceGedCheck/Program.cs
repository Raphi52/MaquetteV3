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

// ---------- Lot 4 du plan « Vue du dossier » (20/11/2026) : une PIECE JOINTE ----------
// Un PDF depose sur la carte « Pieces jointes » : son contenu est ecrit a cote du fichier
// d'etat, la piece rejoint les ajouts de l'arbre, et tout revient apres un redemarrage.
string[] tousDomaines = [ArbreDemo.DomaineRcs, ArbreDemo.DomaineJudiciaire];
var dossierPieces = AccueilEtat.DossierPiecesJointes;
Exiger(dossierPieces == Path.Combine(dossierTemp, "accueil-pieces"),
    $"les pieces jointes vont a cote du fichier d'etat de RIGV3_ETAT, jamais dans celui de l'utilisateur ({dossierPieces})");
// Un vrai PDF de deux pages (deux objets /Type /Page sous un /Type /Pages).
var pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >> endobj\n3 0 obj << /Type /Page /Parent 2 0 R >> endobj\n4 0 obj << /Type /Page /Parent 2 0 R >> endobj\n%%EOF\n"u8.ToArray();
var arbreJ = ArbreDemo.Racine("004176", "SCI DU PARC", "812 445 901");
var pjJ = arbreJ.Descendants().First(n => n.Libelle == "Pieces jointes");
var jointe = PiecesJointes.Ranger(dossierPieces, "004176", "Attestation URSSAF.pdf", pdf, new DateTime(2026, 11, 20));
Exiger(jointe is { Libelle: "Attestation URSSAF", Extension: "PDF", Date: "20/11/2026", Pages: 2, EstPiece: true, APdf: true }
       && jointe.Origine == PiecesJointes.Origine
       && jointe.Fichier.StartsWith("https://pieces.rigv3.example/004176/") && jointe.Fichier.EndsWith(".pdf"),
    $"la piece jointe porte le nom du fichier, la date du jour, ses 2 pages et l'adresse de son fichier ({jointe.Fichier})");
var surDisque = Path.Combine(dossierPieces, "004176", jointe.Fichier.Split('/')[^1]);
Exiger(File.Exists(surDisque) && File.ReadAllBytes(surDisque).SequenceEqual(pdf),
    "le contenu est REELLEMENT ecrit, octet pour octet, dans le dossier de l'affaire");
var deuxieme = PiecesJointes.Ranger(dossierPieces, "004176", "Attestation URSSAF.pdf", pdf, new DateTime(2026, 11, 20));
Exiger(deuxieme.Fichier != jointe.Fichier && File.Exists(surDisque),
    "deposer deux fois le meme fichier n'ecrase pas le premier");
var ajoutsJ = AccesGed.NouveauxAjouts();
ajoutsJ[pjJ] = [jointe];
AccueilEtat.Charger().EnregistrerArbreGed("004176",
    PersistanceGed.Capturer(arbreJ, ajoutsJ, AccesGed.NouveauxMasques(), AccesGed.NouveauxLibelles()));

// L'application redemarre : arbre neuf, etat relu.
var arbreJ2 = ArbreDemo.Racine("004176", "SCI DU PARC", "812 445 901");
var ajoutsJ2 = AccesGed.NouveauxAjouts();
var masquesJ2 = AccesGed.NouveauxMasques();
PersistanceGed.Appliquer(arbreJ2, AccueilEtat.Charger().ArbreGedDe("004176"), ajoutsJ2, masquesJ2, AccesGed.NouveauxLibelles());
var pjJ2 = arbreJ2.Descendants().First(n => n.Libelle == "Pieces jointes");
var reposee = AccesGed.EnfantsVisibles(pjJ2, tousDomaines, ajoutsJ2, masquesJ2).LastOrDefault();
Exiger(reposee is { Libelle: "Attestation URSSAF", Pages: 2, APdf: true } && reposee.Fichier == jointe.Fichier
       && reposee.Origine == PiecesJointes.Origine && File.Exists(surDisque),
    "apres redemarrage : la piece jointe est toujours dans « Pieces jointes », avec son fichier sur disque");

// IMPRIMER UN DOSSIER ENTIER (_Print recursif de RIG v2) : toutes ses pieces, sous-dossiers compris.
string[] PiecesDe(NoeudGed n) => [.. AccesGed.PiecesSous(n, tousDomaines, ajoutsJ2, masquesJ2).Select(p => p.Libelle)];
Exiger(PiecesDe(pjJ2).Length == 5 && PiecesDe(pjJ2)[^1] == "Attestation URSSAF",
    $"imprimer « Pieces jointes » : ses 4 pieces et la piece jointe ({PiecesDe(pjJ2).Length})");
var actesJ2 = arbreJ2.Descendants().First(n => n.Libelle == "Depots d'actes");
Exiger(PiecesDe(actesJ2).SequenceEqual(["Bail commercial", "Extrait de proces-verbal du conseil de surveillance du 03/10/2024",
                                         "Certificat de depot d'actes"]),
    "imprimer « Depots d'actes » : ses 3 pieces sur deux niveaux de sous-dossiers, dans l'ordre de l'arbre, sans la fiche");
var rcsJ2 = arbreJ2.Enfants.First(e => e.Libelle == "Dossier RCS");
Exiger(PiecesDe(rcsJ2).Length == rcsJ2.NbPieces + 1, $"imprimer le domaine « Dossier RCS » : ses {rcsJ2.NbPieces} pieces et la jointe");
masquesJ2.Add(actesJ2.Descendants().First(n => n.Libelle == "Bail commercial"));
Exiger(PiecesDe(actesJ2).Length == 2, "une piece supprimee n'est plus imprimee avec son dossier");
// Les droits se lisent sur le domaine, cache a son parent : depuis la racine de l'affaire, un
// login judiciaire n'a que les pieces de « Judiciaire ».
var judJ2 = arbreJ2.Enfants.First(e => e.Libelle == "Judiciaire");
Exiger(AccesGed.PiecesSous(arbreJ2, [ArbreDemo.DomaineJudiciaire], ajoutsJ2, masquesJ2).Count() == judJ2.NbPieces
       && AccesGed.PiecesSous(jointe, tousDomaines).Count() == 0,
    "un login sans le domaine RCS n'imprime aucune de ses pieces ; une piece ne range aucune piece");

// Les REFUS, et le jumeau accepte de chacun (meme regle que refusFichier de ClasseurArbre.tsx).
Exiger(PiecesJointes.Refus("notes.docx", pdf) == "« notes.docx » n'est pas joint : seuls les PDF et les images JPG ou PNG sont acceptes."
       && PiecesJointes.Refus("notes.pdf", pdf) is null,
    "un type non accepte est refuse en clair ; le meme contenu en .pdf passe");
Exiger(PiecesJointes.Refus("faux.pdf", "<html></html>"u8.ToArray()) == "« faux.pdf » n'est pas un vrai PDF : il n'est pas joint."
       && PiecesJointes.Refus("photo.JPEG", [0xFF, 0xD8, 0xFF, 0xE0]) is null
       && PiecesJointes.Refus("photo.jpeg", "%PDF"u8.ToArray()) == "« photo.jpeg » n'est pas un vrai JPG : il n'est pas joint."
       && PiecesJointes.Refus("scan.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]) is null,
    "un fichier dont le contenu n'est pas du type annonce est refuse ; JPG (en majuscules) et PNG vrais passent");
Exiger(PiecesJointes.Refus("vide.pdf", []) == "« vide.pdf » est vide : il n'est pas joint.", "un fichier vide est refuse");
var avantRefus = Directory.GetFiles(Path.Combine(dossierPieces, "004176")).Length;
var refuse = false;
try { PiecesJointes.Ranger(dossierPieces, "004176", "faux.pdf", "<html></html>"u8.ToArray(), DateTime.Today); }
catch (InvalidOperationException) { refuse = true; }
Exiger(refuse && Directory.GetFiles(Path.Combine(dossierPieces, "004176")).Length == avantRefus,
    "un fichier refuse n'est jamais ecrit");
var image = PiecesJointes.Ranger(dossierPieces, "004176", @"C:\scans\Carte d'identite.png",
                                 [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], DateTime.Today);
Exiger(image is { Libelle: "Carte d'identite", Extension: "PNG", Pages: 1 } && AccesGed.CodeIcone(image) == "jpg",
    "une image jointe : son nom sans chemin, une page, l'icone d'image");

// « Copier le n° » d'un dossier : le numero que porte son libelle.
Exiger(AccesGed.NumeroDe("Depot n A2024/038654") == "A2024/038654"
       && AccesGed.NumeroDe("Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC") == "2022J00114"
       && AccesGed.NumeroDe("Formalite n J00092709138") == "J00092709138"
       && AccesGed.NumeroDe("Pieces jointes") is null && AccesGed.NumeroDe("Jonctions et disjonctions") is null,
    "le numero d'un dossier numerote se lit dans son libelle ; un dossier sans numero n'en a pas");

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
// Lot 2 du passage a React (19/11/2026) : le @code de Home.razor vit dans Pont/PlanTravail.cs.
var vue = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
Exiger(vue.Contains("PersistanceGed.Appliquer("),
    "le plan de travail repose l'arbre conserve a l'ouverture d'une affaire");
Exiger(vue.Contains("PersistanceGed.Capturer("),
    "le plan de travail retient l'arbre apres un geste de l'utilisateur");
foreach (var appel in new[] { "MemoriserArbre();", "PoserLibelle(" })
    Exiger(vue.Contains(appel), $"le plan de travail appelle {appel}");

// ---------- Kbis : greffe, denomination et adresse du siege VIENNENT DU FORMULAIRE ----------
// PORTE EN REACT (lot 6, 19/11/2026) : Components/ApercuKbis.razor n'est plus
// qu'une coquille. Les valeurs de l'extrait se calculent dans Pont/ApercuKbis.cs
// (lu ET execute ci-dessous), la mise en page dans web/src/composants/ApercuKbis.tsx.
// Blazor retire (19/11/2026) : plus de coquille, c'est le plan de travail qui passe
// la lecture du formulaire au pont (Pont/PlanTravailVue.cs, depuis le lot 2) et
// l'apercu de la piece qui monte l'extrait (web/src/plan/Apercu.tsx).
var planVue = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravailVue.cs"));
var apercu = File.ReadAllText(Path.Combine(racine, "web/src/plan/Apercu.tsx"));
var tsxKbis = File.ReadAllText(Path.Combine(racine, "web/src/composants/ApercuKbis.tsx"));
var kbis = File.ReadAllText(Path.Combine(racine, "Pont/ApercuKbis.cs"));
Exiger(apercu.Contains("<ApercuKbis vue={a.kbis} />")
       && planVue.Contains("kbis = ApercuKbis.Vue(d.Numero, d.Siren, DenominationAffaire(), SommaireDu(d).FormeJuridique, LireSaisie);")
       && tsxKbis.Contains("className=\"rig-kbis\"")
       && !File.Exists(Path.Combine(racine, "Components/ApercuKbis.razor")),
    "le plan de travail passe la lecture du formulaire au pont du Kbis et monte l'extrait React");
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
Exiger(!(kbis + tsxKbis + apercu).Contains("Greffe du Tribunal de Commerce d'Amiens"),
    "le greffe n'est plus ecrit en dur dans l'apercu Kbis");

// L'extrait calcule par le pont, sur un formulaire saisi.
var saisieKbis = new Dictionary<(string, string), string>
{
    [("SIEGE", "ETBLS_CODE_GREFFE")] = "7501 - PARIS",
    [("IDENTITE", "INSPM_DENOMINATION_STATUT")] = "NOUVELLE SAS",
    [("IDENTITE", "DSSRC_DATE_IMMAT")] = "2014-03-12",
    [("SIEGE", "ETBLS_ID_ADRBS")] = "3 rue de Rivoli 75001 PARIS",
    [("CAPITAL", "CPTL_MONTANT")] = "10000",
    // Le type de dossier n'est pas une forme juridique : le Kbis ne doit pas l'imprimer.
    [("IDENTITE", "DSSRC_TYPE_RCS")] = "Personne morale",
};
string LireKbis(string etape, string colonne) => saisieKbis.TryGetValue((etape, colonne), out var v) ? v : "";
var extrait = RigV3Desktop.Pont.ApercuKbis.Vue("004176", "812 445 906", "SCI DU PARC", "Societe civile immobiliere", LireKbis);
string? Ligne(string titre) => extrait.Sections.SelectMany(x => x.Lignes).FirstOrDefault(l => l.Titre == titre)?.Libelle;
Exiger(extrait.GreffeLibelle == Greffes.Trouver("7501 - PARIS").Libelle && extrait.VilleRcs == "PARIS"
       && Ligne("Immatriculation au RCS, numero") == "812 445 906 R.C.S. PARIS",
    "le Kbis calcule prend l'entete et la ville du greffe SAISI (execute)");
Exiger(Ligne("Denomination ou raison sociale") == "NOUVELLE SAS"
       && Ligne("Adresse du siege") == "3 rue de Rivoli 75001 PARIS"
       && Ligne("Date d'immatriculation") == "12/03/2014",
    "le Kbis calcule imprime la denomination, l'adresse et la date saisies (execute)");
Exiger(Ligne("Forme juridique") == "Societe civile immobiliere",
    $"le Kbis imprime la forme juridique de l'affaire, pas le type de dossier saisi (execute ; lu : « {Ligne("Forme juridique")} »)");
Exiger(Ligne("Capital social") == 10000m.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " EUR"
       && extrait.Gestion == "—"
       && extrait.Sections.Length == 1,
    "le capital en N2 fr-FR, la gestion par defaut, et les sections vides masquees comme la XSL (execute)");
var sansSaisie = RigV3Desktop.Pont.ApercuKbis.Vue("004176", "812 445 906", "SCI DU PARC", "", (_, _) => "");
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
