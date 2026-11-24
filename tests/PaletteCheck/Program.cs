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
        Exiger(p.Actions.Length == 0, "hors affaire : pas de section « Sur ce dossier »");
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
        Exiger(p.HorsGreffe.Length == 3 && p.HorsGreffe.All(h => h.Sujet == "TRANSNORD SA · 318 552 007"),
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

        // --- 10 bis. « SUR CE DOSSIER » (plan « Vue du dossier = coeur des actions », lot 5) :
        // sur l'affaire ouverte, ses actions viennent en tete, filtrees par ce qui est tape,
        // et Entree en joue une par le geste de la barre (« actionDossier »).
        var barre = vue.Dossier!.Actions.SelectMany(v => v.Actions).Select(a => a.Code).ToArray();
        Agir("paletteOuvrir");
        p = P();
        Exiger(barre.Length > 0 && p.Actions.Select(a => a.Code).SequenceEqual(barre),
            $"rien de tape : toutes les actions de la barre de 004176, dans son ordre ({string.Join(", ", p.Actions.Select(a => a.Code))})");
        Agir("paletteTexte", "fac");
        p = P();
        Exiger(p.Actions.Length > 0 && p.Actions[0] is { Code: "factures", Libelle: "Factures", Verbe: "Facturer", Touche: "F", Ecran: true, Copie: null },
            $"« fac » : Factures en tete, avec son verbe et sa lettre ({string.Join(", ", p.Actions.Select(a => a.Libelle))})");
        Exiger(p.Actions.Select(a => a.Code).SequenceEqual(["factures", "encaissements"]),
            "le libelle passe avant le verbe : Encaissements suit, trouve par « Facturer »");
        Agir("paletteTexte", "kbis");
        Exiger(P().Actions.Select(a => a.Code).SequenceEqual(["kbis", "imprimerKbis"]),
            "« kbis » : les deux actions du Kbis, dans l'ordre de la barre");
        Agir("paletteTexte", "copier siren");
        Exiger(P().Actions is [{ Code: "copierSiren", Copie: "812 445 906", Ecran: false }],
            "une action de copie porte le texte a copier (jouee par la page)");
        Agir("paletteTexte", "zzz");
        Exiger(P().Actions.Length == 0, "rien ne repond : pas de section « Sur ce dossier »");
        Agir("paletteTexte", "fac");
        Agir("actionDossier", P().Actions[0].Code);
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Proc is { Code: "PROC_FACTURE" } && vue.Proc.Ecran.Dossier is { Numero: "004176" },
            "Entree sur « Factures » : la palette se ferme, l'ecran Factures s'ouvre pour 004176");
        Agir("paletteOuvrir");
        Exiger(P().Actions.Length == 0 && P().Travail.Length > 0,
            "sur un ecran, meme ouvert depuis l'affaire : la palette d'avant, sans « Sur ce dossier »");
        Agir("paletteFermer");
        Agir("retourDossier");
        Exiger(plan.Vue().Dossier is { Numero: "004176" }, "la puce « ← Dossier » ramene a l'affaire");
        Agir("paletteOuvrir");
        Agir("paletteTexte", "kbis");
        Agir("actionDossier", "kbis");
        vue = plan.Vue();
        Exiger(vue.Palette is null && vue.Dossier?.Apercu?.Apercu?.Libelle == ArbreDemo.LibelleKbisAJour,
            "une action locale jouee depuis la palette la ferme : le Kbis a jour s'affiche dans la page");

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

        // --- 12 bis. Ctrl K trouve TOUT le menu par son libelle, pas seulement les ecrans du
        // plan de travail (Groupes). « Mandataires » et « Tables de reference » ne sont qu'au
        // menu. Le filtre de profil vaut aussi pour le menu : PROC_MANDATAIRE est reserve au
        // judiciaire, donc un login RCS seul ne le trouve pas. Un ecran present dans les deux
        // sources ne sort qu'une fois (« Commandes web »).
        Agir("paletteOuvrir");
        Agir("paletteTexte", "Mandataires");
        Exiger(P().Ecrans.Any(e => e is { Code: "PROC_MANDATAIRE", Libelle: "Mandataires" }),
            "Ctrl K « Mandataires » trouve l'ecran du menu par son libelle");
        Agir("paletteTexte", "Tables de reference");
        Exiger(P().Ecrans.Any(e => e.Code == "PROC_GTABREF"),
            "Ctrl K « Tables de reference » trouve l'ecran du menu par son libelle");
        Agir("paletteTexte", "Commandes web");
        Exiger(P().Ecrans.Count(e => e.Code == "PROC_COMMANDES_WEB") == 1,
            "Ctrl K « Commandes web » reste trouve, et une seule fois");
        Agir("paletteFermer");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("cmartin")));
        Agir("paletteOuvrir");
        Agir("paletteTexte", "Mandataires");
        Exiger(!P().Ecrans.Any(e => e.Code == "PROC_MANDATAIRE"),
            "un login RCS seul ne trouve pas un ecran reserve au judiciaire : le filtre du profil vaut pour le menu");
        Agir("paletteFermer");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Agir("paletteOuvrir");
        Agir("paletteTexte", "Mandataires");
        Agir("paletteAller", new { nature = "ecran", cle = "PROC_MANDATAIRE" });
        // Depuis le 24/11/2026, les mandataires sont REUNIS avec les destinataires institutionnels et les
        // rapprochements d'acteurs (Pont/MenuAccueil.cs) : la palette trouve « Mandataires » par son nom, et
        // l'ouvre dans l'ecran qui le reunit -- un onglet, pas un de plus par procedure.
        Exiger(plan.Vue().Proc is { Code: "SYNTHESE_FICHES_ACTEURS", Libelle: "Mandataires et destinataires" },
            $"Entree sur « Mandataires » l'ouvre dans un onglet, celui de l'ecran qui le reunit ({plan.Vue().Proc?.Code})");

        // --- 12 ter. Ctrl K trouve le menu par son libelle, et Entree ouvre le premier ecran.
        // fix-ok: Pont/PlanTravail.cs TraitementsPour : les Groupes passaient avant le menu, sans
        // rang, puis Take(4). Mesure par ce banc avant correction : 5 libelles du menu n'etaient pas
        // en tete (Requisitions, Redaction, Archivage, Clients, Fiche client) et « EDI » ne faisait
        // pas figurer les deux ecrans du menu qui commencent par ce mot.
        // Mesure sur TOUTES les entrees visibles du menu, pas sur un echantillon.
        Agir("accueil");
        Agir("paletteOuvrir");
        var sansLibelle = new List<string>();
        var pasEnTete = new List<string>();
        foreach (var entree in MenuAccueil.Moments.SelectMany(m => m.Sous).SelectMany(s => s.Entrees))
        {
            if (!PerimetreProfil.Visible(entree.Code, JeuDemo.Compte("rvilain").Domaines)) continue;
            Agir("paletteTexte", entree.Libelle);
            var liste = P().Ecrans;
            if (!liste.Any(e => e.Code == entree.Code)) sansLibelle.Add($"{entree.Libelle} ({entree.Code})");
            // Deux entrees du menu peuvent porter le meme libelle (« Encaissements ») : la premiere
            // ligne doit porter le libelle tape, pas forcement ce code-la.
            else if (RecherchePalette.Plier(liste[0].Libelle) != RecherchePalette.Plier(entree.Libelle))
                pasEnTete.Add($"{entree.Libelle} (d'abord : {liste[0].Libelle})");
        }
        Exiger(sansLibelle.Count == 0,
            $"chaque entree du menu visible se trouve par son libelle ({sansLibelle.Count} absente(s) : {string.Join(", ", sansLibelle)})");
        Exiger(pasEnTete.Count == 0,
            $"tape en entier, un libelle du menu sort en tete : Entree l'ouvre ({pasEnTete.Count} hors tete : {string.Join(", ", pasEnTete)})");

        // Un mot tape qui COMMENCE deux libelles du menu les fait figurer tous les deux (la limite
        // de 4 ne coupe qu'au-dela) : « EDI » commence « EDI RPVA » et « EDI RPJC ».
        Agir("paletteTexte", "EDI");
        Exiger(P().Ecrans.Any(e => e.Code == "PROC_EDI_RPVAINS") && P().Ecrans.Any(e => e.Code == "PROC_EDI_RPJC_TRCNC"),
            "« EDI » fait figurer les deux libelles du menu qui commencent par ce mot, pas seulement les ecrans du plan qui le contiennent");
        Agir("paletteFermer");

        // --- 12. Ctrl K SUR L'ACCUEIL : la liste deroulante de sa barre ; sur un
        // ecran, la palette flotte au milieu.
        Agir("accueil");
        Exiger(plan.Vue() is { Proc: null, Dossier: null, Accueil: not null }, "la maison ramene a l'accueil");
        Agir("paletteOuvrir");
        Exiger(plan.Vue().Palette is { Ancree: true, Texte: "", Actions.Length: 0 },
            "Ctrl K sur l'accueil deroule la palette sous la barre, comme un clic dans la barre (sans « Sur ce dossier »)");
        Agir("paletteFermer");
        Agir("ouvrirCode", "BODACC");
        Agir("paletteOuvrir");
        Exiger(plan.Vue() is { Palette.Ancree: false, Proc.Code: "BODACC" },
            "Ctrl K sur un ecran ouvre la palette au milieu");
        Agir("paletteFermer");

        // --- 13. « Ouvrir en page » : la meme recherche dans un onglet, avec la liseuse
        // (/draft tour 4, proposition 4 « Lecteur »).
        Agir("accueil");
        Agir("paletteOuvrir");
        Agir("paletteTexte", "dupont");
        Agir("paletteEnPage");
        vue = plan.Vue();
        var page = vue.Proc?.Ecran.Recherche;
        Exiger(vue.Palette is null && vue.Proc is { Code: "RECHERCHE", Libelle: "Recherche" } && page is { Texte: "dupont" },
            "« Ouvrir en page » ferme la palette et ouvre l'onglet Recherche sur le meme texte");
        Exiger(page!.Pieces.Length >= 2 && page.Pieces.All(x => x.Positions.Length == x.Passages && x.Longueurs.All(l => l == 6)
               && x.Positions.Select((i, k) => x.Texte.Substring(i, x.Longueurs[k])).All(t => t.Equals("DUPONT", StringComparison.OrdinalIgnoreCase))),
            "chaque piece porte son texte entier et la place exacte de CHAQUE passage, pour la liseuse");
        var decl = page.Pieces.First(x => x.Libelle == "Declaration de non-condamnation");
        Agir("rechercheTexte", "soussigné dupont");
        page = plan.Vue().Proc!.Ecran.Recherche!;
        var d2 = page.Pieces.Single(x => x.Libelle == decl.Libelle);
        Exiger(page.Texte == "soussigné dupont" && d2.Texte.Substring(d2.Positions[0], d2.Longueurs[0]) == "soussigne DUPONT",
            "la barre de la page cherche a son tour, accents ignores, passage exact");
        Agir("palettePiece", d2.Chemin);
        vue = plan.Vue();
        Exiger(vue.Dossier is { Numero: "2026B01067" } && vue.Dossier.Apercu?.Apercu?.Libelle == decl.Libelle,
            "« Ouvrir la piece » ouvre son affaire avec la piece a l'apercu");
        Exiger(plan.Vue().Proc is null || plan.Vue().Proc!.Code != "RECHERCHE" || plan.Vue().Dossier is not null,
            "l'affaire passe devant la page");
        Agir("echap");
        Agir("ongletActiver", "RECHERCHE");
        Exiger(plan.Vue().Proc?.Ecran.Recherche is { Texte: "soussigné dupont" },
            "revenu sur l'onglet, la page a garde sa recherche");

        // --- 14. Les VRAIS documents (PDF) se cherchent par leur texte, page par page
        // (wwwroot/documents/*.pdf.txt, tires par extraire-texte-pdf.ps1).
        Agir("rechercheTexte", "commandite");
        page = plan.Vue().Proc!.Ecran.Recherche!;
        var synthese = page.Pieces.FirstOrDefault(x => x.Libelle == "Synthese de depot au Guichet unique");
        Exiger(synthese is { Fichier: "documents/formalite-synthese-guichet-unique.pdf", NbPages: 13 }
               && synthese.PagesDesPassages.SequenceEqual([1, 2, 4]) && synthese.Texte.Split('\f').Length == 13,
            $"un vrai PDF se trouve par son texte, chaque passage avec sa page ({string.Join(", ", page.Pieces.Select(x => x.Libelle))})");
        Exiger(synthese!.Positions.Select((i, k) => synthese.Texte.Substring(i, synthese.Longueurs[k]))
               .All(t => RecherchePalette.Plier(t) == "COMMANDITE"),
            "les passages d'un PDF sont a la bonne place dans son texte");
        Agir("paletteOuvrir");
        Agir("paletteTexte", "commandite");
        Exiger(P().Pieces.Any(x => x.Libelle == synthese.Libelle && x.Texte == "" && x.PagesDesPassages.SequenceEqual([1, 2, 4])),
            "la palette trouve le PDF aussi, sans transporter son texte entier");
        Agir("paletteFermer");
        Agir("rechercheTexte", "guichet unique formalité J00092709138");
        page = plan.Vue().Proc!.Ecran.Recherche!;
        var aCheval = page.Pieces.FirstOrDefault(x => x.Libelle == synthese.Libelle);
        Exiger(aCheval is not null && aCheval.Texte.Substring(aCheval.Positions[0], aCheval.Longueurs[0]).Contains('\n'),
            "un passage a cheval sur deux lignes du PDF est trouve : les retours a la ligne comptent pour un blanc");
        // Un PDF qui n'est qu'une IMAGE numerisee : son texte, lu a l'image par la
        // reconnaissance de caracteres de Windows (extraire-texte-pdf.ps1), se cherche aussi.
        Agir("rechercheTexte", "Guillaume BRUN");
        var ordonnance = plan.Vue().Proc!.Ecran.Recherche!.Pieces.FirstOrDefault(x => x.Fichier == "documents/ordonnance-president.pdf");
        Exiger(ordonnance is { NbPages: 3 } && ordonnance.PagesDesPassages.SequenceEqual([3]),
            $"une ordonnance numerisee se trouve par son texte lu a l'image, a sa page ({ordonnance?.Libelle}, pages {string.Join(",", ordonnance?.PagesDesPassages ?? [])})");
        Agir("rechercheTexte", "avoir de la facture");
        var numerise = plan.Vue().Proc!.Ecran.Recherche!.Pieces.FirstOrDefault(x => x.Fichier == "documents/document-numerise.pdf");
        Exiger(numerise is { NbPages: 5 } && numerise.PagesDesPassages.Contains(2),
            $"un document numerise (bordereau, avoir, courriel) se trouve aussi, a la page de l'avoir ({numerise?.Libelle}, pages {string.Join(",", numerise?.PagesDesPassages ?? [])})");
        // Le nombre de pages AFFICHE (« 4 p. » dans la liste, l'apercu) est celui que l'arbre de
        // demonstration declare : il doit etre celui du PDF, sinon la liste dit « 4 p. » a cote
        // de « page 3 sur 3 » dans la liseuse.
        static IEnumerable<NoeudGed> Tous(NoeudGed n) => n.Enfants.SelectMany(Tous).Prepend(n);
        var ecarts = Tous(ArbreDemo.Racine("0000", "DEMO", ""))
            .Where(n => n.APdf)
            .Select(n => (n.Libelle, Declare: n.Pages, Reel: TextePieces.Pages(n, PageWeb.DossierWwwroot).Length))
            .Where(p => p.Reel > 0 && p.Reel != p.Declare)
            .Select(p => $"{p.Libelle} : {p.Declare} declarees, {p.Reel} dans le PDF")
            .ToArray();
        Exiger(ecarts.Length == 0,
            $"chaque PDF de la demonstration declare son vrai nombre de pages ({(ecarts.Length == 0 ? "aucun ecart" : string.Join(" ; ", ecarts))})");

        // --- 15. « Joindre a ce depot » : une copie de la piece dans un depot de l'affaire.
        Agir("rechercheTexte", "commandite");
        page = plan.Vue().Proc!.Ecran.Recherche!;
        var depotActes = page.Depots.FirstOrDefault(d => d.Libelle == "Depot n 2024/0141");
        Exiger(page.Depots.Length >= 3 && depotActes is { Lieu: "Depots d'actes" } && !depotActes.Pieces.Contains(synthese.Libelle),
            $"la liseuse connait les depots de l'affaire ({string.Join(", ", page.Depots.Select(d => d.Libelle))})");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("astagiaire")));
        Agir("rechercheJoindre", new { piece = synthese.Chemin, depot = depotActes!.Chemin });
        page = plan.Vue().Proc!.Ecran.Recherche!;
        Exiger(page.Avis!.StartsWith("Envoye en validation") && !page.Depots.Single(d => d.Libelle == depotActes.Libelle).Pieces.Contains(synthese.Libelle),
            "un login soumis a validation : la piece n'est jointe qu'une fois la demande validee");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Agir("rechercheJoindre", new { piece = synthese.Chemin, depot = depotActes.Chemin });
        page = plan.Vue().Proc!.Ecran.Recherche!;
        Exiger(page.Avis == $"« {synthese.Libelle} » jointe a {depotActes.Libelle}." && page.Depots.Single(d => d.Libelle == depotActes.Libelle).Pieces.Contains(synthese.Libelle),
            "« Joindre a ce depot » pose la piece dans le depot, et le dit");
        Agir("rechercheJoindre", new { piece = synthese.Chemin, depot = depotActes.Chemin });
        Exiger(plan.Vue().Proc!.Ecran.Recherche!.Avis!.Contains("est deja dans"), "une piece deja dans le depot n'y est pas posee deux fois");
        var copie = plan.Vue().Proc!.Ecran.Recherche!.Pieces.FirstOrDefault(x => x.Chemin.Contains("Depot n 2024/0141"));
        Exiger(copie is { Fichier: "documents/formalite-synthese-guichet-unique.pdf", NbPages: 13 },
            "la piece jointe garde son PDF et son texte (enregistres avec l'arbre de l'affaire)");
        Agir("ouvrirNumero", "2026B01067");
        Agir("ongletActiver", "RECHERCHE");
        Exiger(plan.Vue().Proc?.Ecran.Recherche?.Depots.Single(d => d.Libelle == depotActes.Libelle).Pieces.Contains(synthese.Libelle) == true,
            "rouverte, l'affaire a garde la piece jointe dans son depot");

        // --- 16. « Ouvrir la piece » de la liseuse : le PDF a la page du passage lu.
        Agir("rechercheTexte", "commandite");
        Agir("rechercheOuvrir", new { chemin = synthese.Chemin, page = 4 });
        var ouvert = plan.Vue().Dossier?.Apercu?.Apercu;
        Exiger(ouvert is { Libelle: "Synthese de depot au Guichet unique", Pdf: true, Page: 4 },
            $"« Ouvrir la piece » ouvre le PDF a la page du passage lu ({ouvert?.Libelle}, page {ouvert?.Page})");
        Agir("ongletActiver", "RECHERCHE");
        Agir("rechercheOuvrir", new { chemin = synthese.Chemin, page = 99 });
        Exiger(plan.Vue().Dossier?.Apercu?.Apercu?.Page == 13, "une page au-dela du document s'arrete a sa derniere (13)");
        Agir("palettePiece", synthese.Chemin);
        Exiger(plan.Vue().Dossier?.Apercu?.Apercu?.Page == 1, "ouverte autrement (palette, arbre), la piece repart de sa page 1");
        // Une piece d'un autre classeur (Judiciaire), lue a l'image : meme chose.
        Agir("ongletActiver", "RECHERCHE");
        Agir("rechercheTexte", "Guillaume BRUN");
        Agir("rechercheOuvrir", new { chemin = ordonnance!.Chemin, page = 3 });
        var ordo = plan.Vue().Dossier?.Apercu?.Apercu;
        Exiger(ordo is { Pdf: true, Page: 3, Fichier: "documents/ordonnance-president.pdf" },
            $"l'ordonnance numerisee s'ouvre a la page du passage lu ({ordo?.Libelle}, page {ordo?.Page})");

        plan.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
