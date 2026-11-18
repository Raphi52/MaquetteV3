using RigV3Desktop;

// Garde-fou des FENETRES DETACHEES (demande du 14/09/2026) : plusieurs
// documents sortis en meme temps, et une place qui ne bouge pas d'une fois
// sur l'autre pour qui travaille sur plusieurs ecrans.
//
// Ici on EXECUTE le code reel, on ne relit pas son texte : les emplacements
// du passe-plat, l'aller-retour de la position dans le fichier de reglages,
// et le refus d'une position devenue hors ecran.
//
// Le fichier de reglages est redirige (RIGV3_ETAT) vers un fichier temporaire :
// le controle ne touche jamais l'etat reel de l'utilisateur.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var temporaire = Path.Combine(Path.GetTempPath(), "rigv3-fenetres-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", temporaire);

try
{
    // --- 1. PLUSIEURS documents en meme temps : un emplacement par rang.
    var partage = new DocumentPartage();
    var racine = ArbreDemo.Racine("2441", "AMIENS BILLARDS", "109678235");
    var feuilles = racine.Descendants().Where(n => n.EstPiece).ToList();
    Exiger(feuilles.Count >= 2, "le jeu de demonstration fournit au moins deux pieces");
    var a = feuilles[0];
    var b = feuilles[1];
    Exiger(!ReferenceEquals(a, b), "les deux pieces sont bien distinctes");

    partage.Publier(1, a, new List<(string, string)> { ("Nature", "Depot") });
    partage.Publier(2, b, new List<(string, string)> { ("Nature", "Radiation") });
    Exiger(ReferenceEquals(partage.Emplacement(1).Piece, a),
        "la fenetre 1 affiche SA piece");
    Exiger(ReferenceEquals(partage.Emplacement(2).Piece, b),
        "la fenetre 2 affiche une AUTRE piece (deux documents sortis a la fois)");
    Exiger(partage.Emplacement(1).Valeurs[0].Valeur == "Depot" &&
           partage.Emplacement(2).Valeurs[0].Valeur == "Radiation",
        "editer un formulaire ne repeint que la fenetre concernee");

    // Chaque emplacement previent SA fenetre, pas les autres.
    int vues1 = 0, vues2 = 0;
    partage.Emplacement(1).Change += () => vues1++;
    partage.Emplacement(2).Change += () => vues2++;
    partage.Publier(1, a, new List<(string, string)> { ("Nature", "Depot modifie") });
    Exiger(vues1 == 1 && vues2 == 0, "publier sur le rang 1 ne reveille pas la fenetre 2");

    // --- 2. Rangs : la place d'une fenetre fermee est reprise.
    Exiger(partage.RangLibre(new[] { 1, 2 }) == 3, "une 3e fenetre prend le rang 3");
    Exiger(partage.RangLibre(new[] { 2 }) == 1, "la fenetre 1 fermee, son rang est repris avant d'en creer un de plus");
    partage.Liberer(2);
    Exiger(partage.Emplacement(2).Piece is null, "fermer une fenetre vide son emplacement");

    // --- 3. POSITION : aller-retour reel par le fichier de reglages.
    Exiger(AccueilEtat.PoseDocument(1) is null, "aucune position retenue au depart");
    AccueilEtat.MemoriserDocument(1, new PoseFenetre { Gauche = 1930, Haut = 12, Largeur = 1200, Hauteur = 900 });
    AccueilEtat.MemoriserDocument(2, new PoseFenetre { Gauche = 100, Haut = 100, Largeur = 800, Hauteur = 600 });
    var relue = AccueilEtat.PoseDocument(1);
    Exiger(relue is not null && relue.Gauche == 1930 && relue.Haut == 12 &&
           relue.Largeur == 1200 && relue.Hauteur == 900,
        "la fenetre 1 revient EXACTEMENT ou elle a ete laissee (relu sur disque)");
    Exiger(AccueilEtat.PoseDocument(2)!.Gauche == 100, "chaque rang garde SA position, sans ecraser l'autre");
    Exiger(File.Exists(temporaire), "la position est bien ecrite dans le fichier de reglages existant");

    // Plafond annonce : au-dela de 4 fenetres, rien n'est retenu.
    AccueilEtat.MemoriserDocument(AccueilEtat.PlafondFenetresDocument + 1,
        new PoseFenetre { Gauche = 5, Haut = 5, Largeur = 800, Hauteur = 600 });
    Exiger(AccueilEtat.PoseDocument(AccueilEtat.PlafondFenetresDocument + 1) is null,
        "au-dela de 4 fenetres la position n'est plus retenue (fichier de reglages borne)");

    // --- 4. Une position devenue hors ecran est REFUSEE.
    var deuxEcrans = new[] { (0d, 0d, 1920d, 1040d), (1920d, 0d, 1920d, 1040d) };
    var unSeulEcran = new[] { (0d, 0d, 1920d, 1040d) };
    var surSecond = new PoseFenetre { Gauche = 1930, Haut = 12, Largeur = 1200, Hauteur = 900 };
    Exiger(AccueilEtat.PoseUtilisable(surSecond, deuxEcrans),
        "le dock branche : la position du second ecran est reprise");
    Exiger(!AccueilEtat.PoseUtilisable(surSecond, unSeulEcran),
        "portable debranche : la position hors ecran est refusee (fenetre jamais invisible)");
    Exiger(!AccueilEtat.PoseUtilisable(null, deuxEcrans), "rien de memorise : placement par defaut");
    Exiger(!AccueilEtat.PoseUtilisable(new PoseFenetre { Gauche = 10, Haut = 10, Largeur = 50, Hauteur = 40 }, deuxEcrans),
        "une fenetre minuscule retenue par accident est refusee");

    // --- 5. Placement par defaut : la branche « second ecran », impossible a
    // jouer sur un poste a un seul moniteur tant que le calcul vivait dans la
    // fenetre WPF. Depuis PlacementDocument, elle se rejoue ici.
    var gauche = new PlacementDocument.Zone(0, 0, 1920, 1040);
    var droite = new PlacementDocument.Zone(1920, 0, 1600, 900);
    var deux = new[] { gauche, droite };
    var un = new[] { gauche };

    var surAutre = PlacementDocument.Calculer(deux, 0, 1, 900, 800);
    Exiger(surAutre is not null && surAutre.Gauche >= 1920,
        "deux ecrans : le document part sur l'ecran QUI NE PORTE PAS l'affaire");
    Exiger(surAutre is not null && surAutre.Largeur == 1600 && surAutre.Hauteur == 900,
        "deux ecrans : le document prend toute la zone de travail du second");

    var principalADroite = PlacementDocument.Calculer(deux, 1, 1, 900, 800);
    Exiger(principalADroite is not null && principalADroite.Gauche < 1920,
        "l'ecran libre est choisi quel que soit celui qui porte l'affaire");

    var seconde = PlacementDocument.Calculer(deux, 0, 2, 900, 800);
    Exiger(seconde is not null && surAutre is not null
           && (seconde.Gauche != surAutre.Gauche || seconde.Haut != surAutre.Haut),
        "deux fenetres sorties ne se recouvrent pas exactement");

    var seul = PlacementDocument.Calculer(un, 0, 1, 900, 800);
    Exiger(seul is not null && seul.Largeur <= 960,
        "un seul ecran : le document prend au plus la moitie, l'affaire reste lisible");
    Exiger(seul is not null && seul.Gauche + seul.Largeur <= 1920.001,
        "un seul ecran : le document ne deborde pas de la zone de travail");

    Exiger(PlacementDocument.Calculer(Array.Empty<PlacementDocument.Zone>(), -1, 1, 900, 800) is null,
        "aucun ecran detecte : aucune place imposee, la fenetre garde la sienne");
    Exiger(PlacementDocument.Calculer(un, -1, 1, 900, 800) is not null,
        "ecran de l'affaire inconnu : une place est quand meme rendue");
}
finally
{
    try { if (File.Exists(temporaire)) File.Delete(temporaire); } catch { }
}

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("FenetresCheck : tout est vert."); return 0; }
Console.WriteLine("FenetresCheck : " + echecs.Count + " echec(s)");
foreach (var e in echecs) Console.WriteLine(" - " + e);
return 1;
