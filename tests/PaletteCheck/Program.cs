using System.IO;
using System.Text.Json;
using RigV3Desktop;
using RigV3Desktop.Pont;

// LA PALETTE Ctrl K JOUEE PAR SES GESTES (19/11/2026, /draft « Page Recherche »,
// conv-2018, propositions 3 « Mon travail » et 6 « Pieces » du tour 3).
//
// Le banc joue les gestes comme la page les remonte (SessionPlanTravail.Agir)
// et lit la VUE rendue (Vue().Palette) : le plus urgent d'abord, qui s'en
// occupe, le delai, l'activite, le filtre « A moi », « Me l'attribuer » (vu
// aussi par l'ecran Demandes), la recherche dans le texte des pieces (accents
// ignores, domaines du login respectes), les annonces, hors du greffe, le
// SIREN inconnu, Echap, et les ouvertures.

internal static class Programme
{
    private static readonly List<string> Echecs = new();

    private static void Exiger(bool ok, string quoi)
    {
        Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
        if (!ok) Echecs.Add(quoi);
    }

    private static JsonElement Valeur(object? v) => JsonSerializer.SerializeToElement(v);

    private static int Main()
    {
        // Bac a sable : JAMAIS l'etat reel de l'utilisateur.
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-palette-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
        try { Jouer(); }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatBanc); } catch (IOException) { }
        }

        Console.WriteLine();
        if (Echecs.Count == 0) { Console.WriteLine("PaletteCheck : tout est vert."); return 0; }
        Console.WriteLine($"PaletteCheck : {Echecs.Count} echec(s).");
        return 1;
    }

    private static void Jouer()
    {
        // --- 0. Les briques de comparaison.
        Exiger(RecherchePalette.Plier("Dépôt 318.552-004") == "DEPOT318552004",
            "le pli retire accents, casse et separateurs");
        Exiger(RecherchePalette.Correspond("transnord bilan", "Depot de bilan - TRANSNORD SA")
               && !RecherchePalette.Correspond("transnord actes", "Depot de bilan - TRANSNORD SA"),
            "chaque mot tape doit se retrouver dans la ligne");
        Exiger(RecherchePalette.Correspond("318 552 004", "318 552 004")
               && !RecherchePalette.Correspond("552 318", "318 552 004"),
            "des groupes de chiffres tapes a la suite forment un seul numero");
        Exiger(RecherchePalette.Initiales("C. BENIER") == "CB" && RecherchePalette.Initiales("R. VILAIN") == "RV",
            "les initiales d'un agent se lisent sur sa signature");
        Exiger(JeuDemo.Compte("rvilain").Signature == "R. VILAIN",
            "la signature d'un compte est celle de la colonne « Utilisateur » des demandes");
        Exiger(RecherchePalette.SirenTape("552.100.554") == "552 100 554" && RecherchePalette.SirenTape("55210055") is null,
            "un SIREN tape en entier est reconnu, separateurs ignores");

        var plan = new SessionPlanTravail(null, null);
        plan.Initialiser();
        void Agir(string geste, object? valeur = null) => plan.Agir(geste, Valeur(valeur));
        PaletteVue P() => plan.Vue().Palette ?? throw new InvalidOperationException("palette fermee");

        // --- 1. Fermee par defaut ; Ctrl K l'ouvre sur le travail encore ouvert.
        Exiger(plan.Vue().Palette is null, "la palette est fermee a l'ouverture de l'application");
        Agir("paletteOuvrir");
        var p = P();
        Exiger(p.Texte == "" && p.Travail.Length > 0 && p.Travail.All(t => t.Nature is "dem" or "cou"),
            "avant de taper : le travail encore ouvert (demandes en cours, courrier a rattacher)");
        Exiger(p.Travail[0] is { Numero: "004151", Etat: "J+11", Ton: "al", Filet: "alerte" }
               && p.Travail[0].Delai == "Delai legal de 7 jours depasse de 4 jours",
            $"le plus urgent d'abord : 004151 hors delai J+11 en tete ({p.Travail[0].Numero} {p.Travail[0].Etat} · {p.Travail[0].Delai})");
        Exiger(p.Travail[0].EcranLibelle == "Demandes" && !p.Travail.Any(t => t.EcranLibelle.Contains("PROC_")),
            "l'ecran du traitement est nomme par le menu, jamais par son code");
        var urgences = p.Travail.Select(t => t.Ton == "al" ? 0 : 1).ToArray();
        Exiger(urgences.SequenceEqual(urgences.Order()), "les lignes hors delai passent avant les autres");
        Exiger(p.Travail[0].Qui is { Signature: "C. BENIER", Initiales: "CB" } && !p.Travail[0].AMoi && p.Travail[0].Attribuable,
            "chaque ligne dit qui s'en occupe ; une demande d'un autre peut m'etre attribuee");
        Exiger(p.Moi is { Signature: "R. VILAIN", Initiales: "RV", Nom: "Raphael" },
            "la palette sait qui est connecte (le login de l'accueil)");
        var miens = JeuDemo.Demandes.Count(d => d.EnCours && d.Utilisateur == "R. VILAIN");
        Exiger(p.Bilan.HorsDelai == JeuDemo.DemandesHorsDelai().Length && p.Bilan.MAttendent == miens && miens > 0
               && p.Bilan.ArriveesDuJour == JeuDemo.Arrivees.Count(a => a.Instant.Date == JeuDemo.Aujourdhui.Date),
            $"le bilan du jour reprend les memes sources que l'accueil ({p.Bilan.HorsDelai} hors delai, {p.Bilan.MAttendent} m'attendent)");

        // --- 2. « transnord » : tout ce qui touche la societe, l'urgent en tete.
        Agir("paletteTexte", "transnord");
        p = P();
        Exiger(p.Travail.Length >= 4 && p.Travail.All(t => (t.Titre + t.Detail).Contains("TRANSNORD", StringComparison.OrdinalIgnoreCase)),
            $"« transnord » ne rend que ce qui touche TRANSNORD ({p.Travail.Length} lignes)");
        Exiger(p.Travail[0].Numero == "004151" && p.Travail.Select(t => t.Nature).Distinct().Count() >= 3,
            "demandes, affaires, courrier et actes se melangent, le plus urgent d'abord");
        Exiger(p.Travail.Count(t => t.Numero == "J-00931") == 1 && p.Travail.Count(t => t.Numero == "004155") == 1,
            "une affaire qui est aussi une demande n'apparait qu'une fois");
        var activite = p.Travail[0].Activite;
        Exiger(activite.Length == 3 && activite[0] is { Agent: "Raphael", Verbe: "a valide" } && activite.All(a => a.Objet.Contains("004151")),
            "l'activite recente du dossier, la plus recente d'abord");
        Exiger(p.HorsGreffe.Length == 3 && p.HorsGreffe.All(h => h.Sujet == "TRANSNORD SA · 318 552 004"),
            "hors du greffe : FIBEN, interdits de gerer et beneficiaires sont proposes pour la societe");

        // --- 3. Les filtres de « Mon travail ».
        int Nombre(string code) => P().Filtres.Single(f => f.Code == code).Nombre;
        Exiger(Nombre("tout") == P().TravailTrouve && Nombre("moi") == 0 && Nombre("horsDelai") == 1,
            "les filtres comptent ce qu'ils garderaient (Tout, A moi = 0, Hors delai = 1)");
        Agir("paletteFiltre", "horsDelai");
        Exiger(P().Travail.Length == 1 && P().Travail[0].Numero == "004151", "« Hors delai » ne garde que 004151");
        Agir("paletteFiltre", "nimportequoi");
        Exiger(P().Filtre == "tout", "un filtre inconnu retombe sur « Tout »");

        // --- 4. Me l'attribuer : la palette ET l'ecran Demandes le voient.
        Agir("paletteAttribuer", new { numero = "004151", signature = (string?)null });
        p = P();
        var l = p.Travail.Single(t => t.Numero == "004151");
        Exiger(l is { AMoi: true, Attribuable: false, Qui.Initiales: "RV" } && Nombre("moi") == 1,
            "« Me l'attribuer » confie la demande au login courant ; « A moi » la compte");
        var ecran = EcranDemandes.Trouver(new CriteresDemandes("004151", "", "", null, null, [], false, false, 100), []);
        Exiger(ecran.Lignes.Single().Utilisateur == "R. VILAIN",
            "l'ecran Demandes montre le nouvel agent : une seule liste de demandes");
        Agir("paletteAttribuer", new { numero = "004151", signature = "X. INCONNU" });
        Exiger(JeuDemo.Demandes.Single(d => d.Numero == "004151").Utilisateur == "R. VILAIN",
            "une signature qui n'est pas un compte du greffe est refusee");
        Agir("paletteAttribuer", new { numero = "004151", signature = "C. MARTIN" });
        Exiger(P().Travail.Single(t => t.Numero == "004151").Qui is { Nom: "Claire" },
            "« Attribuer a… » confie la demande a un autre compte");

        // --- 5. Le texte des pieces.
        Agir("paletteTexte", "soussigné dupont");
        p = P();
        Exiger(p.Pieces.Length == 1 && p.Pieces[0] is { Libelle: "Declaration de non-condamnation", Trouve: "soussigne DUPONT" }
               && p.Pieces[0].Avant.EndsWith("NON-CONDAMNATION · Je ") && p.Pieces[0].Chemin[0] == "Dossier RCS",
            $"le texte des pieces se cherche, accents ignores ({string.Join(", ", p.Pieces.Select(x => x.Libelle))})");
        Agir("paletteTexte", "dupont");
        p = P();
        Exiger(p.Pieces.Any(x => x.Libelle == "Liste des souscripteurs") && p.Pieces.All(x => x.Trouve.Equals("DUPONT", StringComparison.OrdinalIgnoreCase)),
            "chaque piece montre le passage trouve, ecrit comme dans la piece");
        Exiger(p.DossierDesPieces == "2026B01067 AMIENS BILLARDS",
            $"la palette dit dans quel dossier elle a cherche ({p.DossierDesPieces})");
        Agir("paletteTexte", "bodacc");
        Exiger(P().Pieces.Any(x => x is { Libelle: "Avis BODACC - immatriculation", Annonce: true }),
            "un avis BODACC est reconnu comme une annonce");
        Agir("paletteTexte", "du");
        Exiger(P().Pieces.Length == 0, "moins de trois caracteres : le texte des pieces n'est pas cherche");

        // --- 6. Les domaines du login : une piece qu'on ne peut pas ouvrir ne se cherche pas.
        plan.SurGesteAccueil(new GesteIle("login", Valeur("pjudic")));
        Agir("paletteTexte", "dupont");
        Exiger(P().Pieces.Length == 0 && P().Moi.Signature == "P. JUDIC",
            "un login sans le domaine RCS ne trouve pas le texte des pieces du RCS");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("cmartin")));
        Agir("paletteTexte", "004151");
        var aCmartin = P().Travail.First(t => t.Numero == "004151");
        Exiger(aCmartin.AMoi && aCmartin.Activite.All(a => a.Agent == "Claire"),
            "un redacteur ne voit que ses propres gestes dans l'activite (comme la tuile Activite)");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));

        // --- 7. SIREN inconnu : ouvrir un dossier, chercher hors du greffe.
        Agir("paletteTexte", "552 100 554");
        p = P();
        Exiger(p.Travail.Length == 0 && p.SirenInconnu == "552 100 554" && p.HorsGreffe.All(h => h.Sujet == "552 100 554"),
            "un SIREN sans resultat propose d'ouvrir un dossier et de chercher hors du greffe");
        Agir("paletteAller", new { nature = "siren", cle = "552 100 554" });
        var vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Proc is { Code: "OUVERTURE_DOSSIER" } && vue.Proc.Ecran.SirenDemande == "552 100 554",
            "« Ouvrir un dossier pour ce SIREN » ferme la palette et ouvre l'ecran, SIREN rempli");

        // --- 8. Ouvrir une piece : l'affaire s'ouvre, depliee sur la piece.
        Agir("paletteOuvrir");
        Exiger(P().Texte == "", "Ctrl K repart d'une saisie vide");
        Agir("paletteTexte", "souscripteurs");
        var piece = P().Pieces.First();
        Agir("palettePiece", piece.Chemin);
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Dossier is { Numero: "2026B01067" } && vue.Dossier.Apercu?.Apercu?.Libelle == piece.Libelle,
            "ouvrir une piece ouvre son affaire avec la piece a l'apercu");

        // --- 8 bis. La pastille « Ctrl K » de la recherche de l'accueil ouvre la palette.
        plan.SurGesteAccueil(new GesteIle("palette", Valeur(null)));
        Exiger(plan.Vue().Palette is { Texte: "", Ancree: true },
            "la barre de l'accueil ouvre la palette ACCROCHEE : sa liste deroulante");
        Agir("paletteFermer");
        Exiger(plan.Vue().Palette is null, "la palette se ferme");
        Agir("paletteOuvrir");
        Exiger(plan.Vue() is { Palette.Ancree: false, Dossier: not null },
            "Ctrl K par-dessus une affaire ouverte : la palette flotte au milieu");
        Agir("paletteFermer");

        // --- 9. Echap ferme la palette d'abord, l'affaire ensuite.
        Agir("paletteOuvrir");
        Agir("echap");
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Dossier is not null, "Echap ferme la palette sans fermer l'affaire dessous");
        Agir("echap");
        Exiger(plan.Vue().Dossier is null, "un second Echap ferme l'affaire");

        // --- 10. Ouvrir une demande depuis la palette.
        Agir("paletteOuvrir");
        Agir("paletteAller", new { nature = "dem", cle = "004176" });
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Dossier is { Numero: "004176" }, "Entree sur une demande ouvre son dossier");
        Agir("paletteOuvrir");
        Agir("paletteAller", new { nature = "ecran", cle = "BODACC" });
        Exiger(plan.Vue().Proc is { Code: "BODACC" }, "une annonce ouvre l'ecran BODACC");

        // --- 11. La BARRE de l'accueil est la porte de la palette (19/11/2026) :
        // la lettre tapee y part, et la palette trouve ce que l'ancienne liste
        // trouvait -- les ecrans du menu, les elements de l'arbre.
        plan.SurGesteAccueil(new GesteIle("palette", Valeur("trans")));
        p = P();
        Exiger(p.Texte == "trans" && p.Ecrans.Any(e => e is { Code: "PROC_TELETRANS", Libelle: "Teletransmissions (EDI)" })
               && p.Travail.Any(t => t.Numero == "004151"),
            "une frappe dans la barre de l'accueil ouvre la palette avec ce texte : ecrans du menu et travail");
        Agir("paletteTexte", "2024 0141");
        p = P();
        var depot = p.Elements.FirstOrDefault(e => e.Libelle == "Depot n 2024/0141");
        Exiger(depot is { Piece: false, Lieu: "Depots d'actes" } && depot.Chemin[0] == "Dossier RCS",
            $"un n° de depot trouve son dossier dans l'arbre, separateurs ignores ({string.Join(", ", p.Elements.Select(e => e.Libelle))})");
        Agir("paletteTexte", "bail commercial");
        p = P();
        Exiger(p.Pieces.Any(x => x.Libelle == "Bail commercial") && p.Elements.All(e => e.Libelle != "Bail commercial"),
            "une piece trouvee par son libelle ET son texte ne s'affiche qu'une fois, avec son passage");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("pjudic")));
        Agir("paletteTexte", "2024 0141");
        Exiger(P().Elements.Length == 0, "un login sans le domaine RCS ne trouve pas les elements de la branche RCS");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Agir("paletteTexte", "2024 0141");
        Agir("palettePiece", P().Elements.First(e => e.Libelle == "Depot n 2024/0141").Chemin);
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Dossier is { Numero: "2026B01067" },
            "Entree sur un element de l'arbre ouvre son affaire");
        Agir("echap");
        Agir("paletteOuvrir");
        Agir("paletteTexte", "facture");
        Agir("paletteAller", new { nature = "ecran", cle = P().Ecrans.First().Code });
        Exiger(plan.Vue().Proc is { Libelle: var lib } && lib.Contains("Facture", StringComparison.OrdinalIgnoreCase),
            "Entree sur un ecran du menu l'ouvre");

        // --- 12. Ctrl K SUR L'ACCUEIL : la liste deroulante de sa barre ; sur un
        // ecran, la palette flotte au milieu.
        Agir("accueil");
        Exiger(plan.Vue() is { Proc: null, Dossier: null, Accueil: not null }, "la maison ramene a l'accueil");
        Agir("paletteOuvrir");
        Exiger(plan.Vue().Palette is { Ancree: true, Texte: "" },
            "Ctrl K sur l'accueil deroule la palette sous la barre, comme un clic dans la barre");
        Agir("paletteFermer");
        Agir("ouvrirCode", "BODACC");
        Agir("paletteOuvrir");
        Exiger(plan.Vue() is { Palette.Ancree: false, Proc.Code: "BODACC" },
            "Ctrl K sur un ecran ouvre la palette au milieu");
        Agir("paletteFermer");

        plan.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
