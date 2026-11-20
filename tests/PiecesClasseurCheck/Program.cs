using System.IO;
using System.Text.Json;
using RigV3Desktop;
using RigV3Desktop.Pont;

// LES PIECES DU CLASSEUR JOUEES PAR LEURS GESTES (plan « Vue du dossier = coeur des
// actions », lot 4, 20/11/2026 ; RIG v2 : _ImporterPieceAutreSansBordereau, _Print
// recursif, _Copy, _EnvoiDossierParMail).
//
// Le banc joue les gestes comme la page les remonte (SessionPlanTravail.Agir) et lit
// la VUE rendue : un PDF joint a « Pieces jointes » s'y range, s'ouvre dans la colonne
// du document, compte dans sa carte et revient apres un redemarrage ; le menu du clic
// droit propose, selon la nature du noeud, d'imprimer ou d'envoyer une piece ou un
// dossier entier (la modale dit combien de pieces) et ce que « Copier » copie.
// « Ouvrir dans une fenetre » ouvre une fenetre WPF : prouve sur le bureau cache.

internal static class Programme
{
    private static readonly List<string> Echecs = new();

    private static void Exiger(bool ok, string quoi)
    {
        Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
        if (!ok) Echecs.Add(quoi);
    }

    private static JsonElement Valeur(object? v) => JsonSerializer.SerializeToElement(v);

    /// <summary>La ligne de l'arbre dont le texte est celui-ci, ou null.</summary>
    private static LigneArbreVue? Ligne(LigneArbreVue l, string texte)
        => l.Texte == texte ? l : l.Enfants.Select(e => Ligne(e, texte)).FirstOrDefault(x => x is not null);

    [STAThread]
    private static int Main()
    {
        // Bac a sable : JAMAIS l'etat reel de l'utilisateur, ni ses pieces jointes.
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var dossier = Path.Combine(Path.GetTempPath(), "rigv3-pieces-classeur-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dossier);
        Environment.SetEnvironmentVariable("RIGV3_ETAT", Path.Combine(dossier, "accueil.json"));
        try { Jouer(); }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { Directory.Delete(dossier, true); } catch (IOException) { }
        }

        Console.WriteLine();
        Console.WriteLine(Echecs.Count == 0 ? "PiecesClasseurCheck : vert." : $"PiecesClasseurCheck : {Echecs.Count} echec(s).");
        return Echecs.Count == 0 ? 0 : 1;
    }

    private static void Jouer()
    {
        var plan = new SessionPlanTravail(null, null);
        plan.Initialiser();
        void Agir(string geste, object? valeur = null) => plan.Agir(geste, Valeur(valeur));
        LigneArbreVue Arbre() => plan.Vue().Dossier!.Arbre;
        string Id(string texte) => (Ligne(Arbre(), texte) ?? throw new InvalidOperationException($"« {texte} » absent du classeur")).Id;
        void Menu(string texte) => Agir("arbreMenu", new { id = Id(texte), texte = (string?)null, clientX = 40.0, clientY = 80.0 });

        Agir("ouvrirNumero", "004176");
        var avant = plan.Vue().Dossier!;
        Exiger(Ligne(avant.Arbre, "Pieces jointes") is { Compte: 4 }, "004176 : la carte « Pieces jointes » annonce 4 pieces");

        // --- 1. JOINDRE UN FICHIER : un PDF depose sur la carte « Pieces jointes ».
        var pdf = "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n%%EOF\n"u8.ToArray();
        Agir("arbreJoindre", new { id = Id("Pieces jointes"), nom = "Attestation URSSAF.pdf", contenu = Convert.ToBase64String(pdf) });
        var d = plan.Vue().Dossier!;
        var apercu = d.Apercu?.Apercu;
        Exiger(apercu is { Libelle: "Attestation URSSAF", Pdf: true, Extension: "PDF", Pages: 1 }
               && apercu.Fichier.StartsWith("https://pieces.rigv3.example/004176/"),
            $"la piece jointe s'ouvre dans la colonne du document, par l'adresse de son fichier ({apercu?.Fichier})");
        var carte = Ligne(d.Arbre, "Pieces jointes");
        Exiger(carte is { Ouvert: true, Compte: 5 } && carte.Enfants.LastOrDefault() is { Texte: "Attestation URSSAF", Actif: true },
            "elle apparait dans la carte « Pieces jointes », ouverte, choisie, et la carte compte 5 pieces");
        Exiger(d.Pieces == avant.Pieces + 1 && d.Historique.Any(h => h.Libelle == "Attestation URSSAF" && h.Rubrique == "Pieces jointes"),
            "l'entete du classeur et l'historique la comptent");
        var fichier = Path.Combine(AccueilEtat.DossierPiecesJointes, "004176", apercu!.Fichier.Split('/')[^1]);
        Exiger(File.Exists(fichier) && File.ReadAllBytes(fichier).SequenceEqual(pdf),
            "son contenu est ecrit a cote du fichier d'etat de la session, dans le dossier de l'affaire");

        // Le jumeau refuse : la page ne l'envoie pas ; s'il arrivait, rien ne serait joint.
        var refus = "";
        try { Agir("arbreJoindre", new { id = Id("Pieces jointes"), nom = "notes.docx", contenu = Convert.ToBase64String(pdf) }); }
        catch (InvalidOperationException e) { refus = e.Message; }
        Exiger(refus.Contains("seuls les PDF et les images JPG ou PNG sont acceptes") && Ligne(Arbre(), "Pieces jointes")!.Compte == 5,
            "un type non accepte est refuse en clair et n'ajoute rien");

        // --- 2. Retenu par affaire et apres REDEMARRAGE.
        Agir("ouvrirNumero", "004151");
        Exiger(Ligne(Arbre(), "Attestation URSSAF") is null && Ligne(Arbre(), "Pieces jointes")?.Compte == 4,
            "une autre affaire (004151) n'a pas la piece jointe a 004176");
        var redemarre = new SessionPlanTravail(null, null);
        redemarre.Initialiser();
        redemarre.Agir("ouvrirNumero", Valeur("004176"));
        var r = redemarre.Vue().Dossier!;
        if (Ligne(r.Arbre, "Pieces jointes") is { Ouvert: false } fermee) redemarre.Agir("arbreRubrique", Valeur(fermee.Id));
        r = redemarre.Vue().Dossier!;
        var reprise = Ligne(r.Arbre, "Attestation URSSAF");
        Exiger(reprise is not null && Ligne(r.Arbre, "Pieces jointes") is { Compte: 5 },
            "apres redemarrage, la piece jointe est toujours dans « Pieces jointes » (5 pieces)");
        redemarre.Agir("arbreActiver", Valeur(reprise!.Id));
        Exiger(redemarre.Vue().Dossier!.Apercu?.Apercu is { Libelle: "Attestation URSSAF", Pdf: true } a2 && a2.Fichier == apercu.Fichier,
            "et elle se rouvre sur le meme fichier");
        redemarre.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // --- 3. LE MENU DU CLIC DROIT, selon la nature du noeud.
        Agir("ouvrirNumero", "004176");
        if (Ligne(Arbre(), "Depots d'actes") is { Ouvert: false }) Agir("arbreRubrique", Id("Depots d'actes"));
        Menu("Pieces jointes");
        Exiger(plan.Vue().MenuNoeud is { Titre: "Pieces jointes", EstPiece: false, Pieces: 5, Copie: "Pieces jointes", CopieNumero: false },
            "un dossier sans numero : ses 5 pieces, « Copier » copie son libelle");
        Agir("menuImprimer");
        var v = plan.Vue();
        Exiger(v.MenuNoeud is null && v.Impression?.Libelle == "Pieces jointes (5 pieces)",
            $"« Imprimer » un dossier : la modale d'impression annonce toutes ses pieces (lu : {v.Impression?.Libelle})");
        Agir("fermerImpression");

        Menu("Depots d'actes");
        Exiger(plan.Vue().MenuNoeud is { Pieces: 3, CopieNumero: false }, "« Depots d'actes » : 3 pieces sur deux niveaux de sous-dossiers");
        Agir("menuImprimer");
        Exiger(plan.Vue().Impression?.Libelle == "Depots d'actes (3 pieces)", "son impression est recursive : 3 pieces");
        Agir("fermerImpression");

        Menu("Depot n A2024/038654");
        Exiger(plan.Vue().MenuNoeud is { Pieces: 2, Copie: "A2024/038654", CopieNumero: true },
            "un depot numerote : « Copier le n° » copie A2024/038654, et il range 2 pieces");
        Agir("menuEnvoyer");
        v = plan.Vue();
        Exiger(v.MenuNoeud is null && v.EnvoiMail?.Libelle == "Depot n A2024/038654 (2 pieces)",
            $"« Envoyer par mail » un dossier : la modale d'envoi annonce ses pieces (lu : {v.EnvoiMail?.Libelle})");
        Agir("fermerMail");

        Menu("Dossier RCS");
        var rcs = plan.Vue().MenuNoeud;
        Exiger(rcs is { Pieces: 22, CopieNumero: false }, $"le domaine « Dossier RCS » : ses 21 pieces et la jointe ({rcs?.Pieces})");
        Agir("fermerMenu");

        Menu("Certificat de depot d'actes");
        Exiger(plan.Vue().MenuNoeud is { EstPiece: true, Pieces: 0, Copie: "Certificat de depot d'actes", CopieNumero: false },
            "une piece : « Copier le libelle »");
        Agir("menuImprimer");
        Exiger(plan.Vue().Impression?.Libelle == "Certificat de depot d'actes", "« Imprimer » une piece : la modale la nomme");
        Agir("fermerImpression");
        Menu("Certificat de depot d'actes");
        Agir("menuEnvoyer");
        Exiger(plan.Vue().EnvoiMail?.Libelle == "Certificat de depot d'actes", "« Envoyer par mail » une piece : la modale la nomme");
        Agir("fermerMail");

        Menu("Informations des depots");
        Exiger(plan.Vue().MenuNoeud is { EstPiece: false, Pieces: 0, Copie: "Informations des depots", CopieNumero: false },
            "une fiche : rien a imprimer ni envoyer, « Copier le libelle » seulement");
        Agir("fermerMenu");

        // --- 4. Les gestes existants tiennent sur une piece jointe : renommer, supprimer.
        // Une carte ouverte par domaine : « Depots d'actes » a referme « Pieces jointes ».
        if (Ligne(Arbre(), "Pieces jointes") is { Ouvert: false }) Agir("arbreRubrique", Id("Pieces jointes"));
        Menu("Attestation URSSAF");
        Agir("menuRenommer");
        Agir("arbreRenommer", new { id = Id("Attestation URSSAF"), texte = "Attestation de vigilance", clientX = 0.0, clientY = 0.0 });
        Exiger(Ligne(Arbre(), "Attestation de vigilance") is not null, "une piece jointe se renomme");
        Menu("Attestation de vigilance");
        Agir("menuImprimer");
        Exiger(plan.Vue().Impression?.Libelle == "Attestation de vigilance", "et s'imprime sous son nouveau nom");
        Agir("fermerImpression");
        Menu("Attestation de vigilance");
        Agir("menuSupprimer");
        Exiger(Ligne(Arbre(), "Attestation de vigilance") is null && Ligne(Arbre(), "Pieces jointes")?.Compte == 4
               && AccueilEtat.Charger().ArbreGedDe("004176")?.Ajouts.Values.SelectMany(x => x).Any() != true,
            "supprimee, elle quitte la carte (4 pieces) et l'arbre retenu");
    }
}
