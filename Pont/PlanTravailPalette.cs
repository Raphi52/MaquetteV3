using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// LA PALETTE Ctrl K dans le plan de travail (web/src/composants/Palette.tsx).
//
// La recherche vit dans Metier/RecherchePalette.cs ; ce fichier tient ce que
// seule la session sait : la palette ouverte, le texte tape, le filtre, le
// login de l'accueil, l'affaire ouverte et son arbre, les dossiers valides
// au controle final, et les ouvertures (OuvrirParNumero, OuvrirElementTrouve).
// Les gestes arrivent par Agir (PlanTravailVue.cs), nommes « palette… ».
// ============================================================

/// <summary>La palette ouverte, telle que la page la dessine.</summary>
/// <param name="Filtre">Code du filtre de « Mon travail » choisi (RecherchePalette.Filtres).</param>
/// <param name="Travail">Le travail trouve, le plus urgent d'abord (au plus RecherchePalette.MaxTravail).</param>
/// <param name="TravailTrouve">Combien de lignes repondent au filtre, montrees ou non.</param>
/// <param name="DossierDesPieces">L'affaire dont l'arbre est cherche (« 2026B01067 AMIENS BILLARDS »).</param>
/// <param name="SirenInconnu">Le SIREN tape quand rien ne lui repond : la palette propose d'ouvrir un dossier.</param>
/// <param name="Ecrans">Les ecrans du menu dont le nom porte ce qui est tape (« facture », « trans »).</param>
/// <param name="Elements">Les elements de l'arbre dont le LIBELLE porte ce qui est tape (n° de depot, de formalite).</param>
/// <param name="Ancree">Ouverte SUR L'ACCUEIL (sa barre de recherche, ou Ctrl K quand rien ne le
/// recouvre) : elle pend SOUS la barre, en liste deroulante. Sur un ecran ou une affaire, elle
/// flotte au milieu.</param>
public sealed record PaletteVue(string Texte, string Filtre, FiltrePaletteVue[] Filtres, AgentPaletteVue Moi,
                                AgentPaletteVue[] Agents, TravailPaletteVue[] Travail, int TravailTrouve,
                                PiecePaletteVue[] Pieces, string DossierDesPieces, string NumeroDesPieces,
                                HorsGreffePaletteVue[] HorsGreffe,
                                BilanPaletteVue Bilan, string? SirenInconnu,
                                EcranPaletteVue[] Ecrans, ElementPaletteVue[] Elements, bool Ancree);
/// <summary>Un ecran du menu ; le geste « paletteAller » (nature « ecran ») l'ouvre par son code.</summary>
public sealed record EcranPaletteVue(string Code, string Libelle);
/// <param name="Chemin">Libelles depuis la racine : « palettePiece » deplie l'arbre jusqu'a l'element.</param>
/// <param name="Piece">Une piece (sinon un dossier de l'arbre : un depot, une formalite).</param>
public sealed record ElementPaletteVue(string Libelle, string[] Chemin, string Lieu, bool Piece);
public sealed record FiltrePaletteVue(string Code, string Libelle, int Nombre);
/// <param name="Signature">Ce qu'inscrit la colonne « Utilisateur » (« C. BENIER »).</param>
/// <param name="Nom">Le prenom du compte quand il existe, sinon la signature.</param>
public sealed record AgentPaletteVue(string Signature, string Initiales, string Nom);
/// <param name="EnCours">Encore a faire : une demande en cours se confie a un agent.</param>
/// <param name="Attribuable">Une demande en cours qui n'est pas encore au login courant.</param>
/// <param name="EcranLibelle">Le nom de l'ecran du traitement, lu dans le catalogue du menu (jamais son code).</param>
public sealed record TravailPaletteVue(string Nature, string Numero, string Cle, string Ecran, string EcranLibelle, string Titre,
                                       string Detail, string Etat, string Ton, string Filet, AgentPaletteVue? Qui,
                                       bool AMoi, bool EnCours, bool Attribuable, string? Delai, string Siren,
                                       ActivitePaletteVue[] Activite);
public sealed record ActivitePaletteVue(string Agent, string Verbe, string Objet, string Depuis);
/// <param name="Chemin">Libelles depuis la racine : le geste « palettePiece » deplie l'arbre jusqu'a la piece.</param>
/// <param name="Annonce">Une annonce (BODACC) : rangee a part dans la palette.</param>
/// <param name="Texte">Le texte entier de la piece : la liseuse de la page « Recherche » l'affiche.</param>
/// <param name="Positions">Le debut de chaque passage trouve dans <paramref name="Texte"/>.</param>
/// <param name="Longueur">La longueur de chaque passage (celle de ce qui est tape).</param>
public sealed record PiecePaletteVue(string Libelle, string[] Chemin, string Lieu, string Avant, string Trouve,
                                     string Apres, int Passages, int Pages, string Date, string Origine, bool Annonce,
                                     string Texte, int[] Positions, int Longueur);
/// <param name="Sujet">Ce qui serait consulte (« TRANSNORD SA · 318 552 004 »).</param>
public sealed record HorsGreffePaletteVue(string Code, string Source, string Objet, string Sujet);
/// <param name="PlusAncienne">Jours depuis la plus ancienne demande en cours confiee au login.</param>
public sealed record BilanPaletteVue(string Jour, int HorsDelai, int ArriveesDuJour, int MAttendent, int PlusAncienne);

public sealed partial class SessionPlanTravail
{
    private bool PaletteOuverte;

    /// <summary>Le code de la page « Recherche » (proposition 4 « Lecteur » du /draft,
    /// conv-2018) : la palette etalee dans un onglet. Seul l'aiguillage le lit
    /// (web/src/plan/Aiguillage.tsx).</summary>
    private const string CodePageRecherche = "RECHERCHE";

    /// <summary>Ce qui est cherche dans la page « Recherche » (sa propre barre).</summary>
    private string PageRechercheTexte = string.Empty;
    private bool PaletteAncree;
    private string PaletteTexte = string.Empty;
    private string PaletteFiltre = "tout";

    private sealed record GestePaletteAller(string Nature, string Cle);
    private sealed record GestePaletteAttribuer(string Numero, string? Signature);

    /// <summary>Ctrl K, ou la barre de recherche de l'accueil : elle passe ce qui y
    /// a deja ete tape (la premiere lettre), la palette continue la saisie.</summary>
    /// <param name="ancree">Depuis la barre de l'accueil : la palette pend sous elle.</param>
    private void OuvrirPalette(string texte = "", bool ancree = false)
    {
        PaletteOuverte = !ModeFenetreVue;
        PaletteTexte = texte;
        PaletteAncree = ancree;
    }

    /// <summary>Echap ferme d'abord la palette, la couche la plus haute.</summary>
    private bool FermerPalette()
    {
        if (!PaletteOuverte) return false;
        PaletteOuverte = false;
        return true;
    }

    /// <summary>Un geste « palette… » de la page.</summary>
    private void AgirPalette(string geste, JsonElement valeur)
    {
        switch (geste)
        {
            // Chaque ouverture repart d'une saisie vide : Ctrl K est un nouveau
            // « je cherche », pas la reprise de la recherche d'avant.
            // Sur l'accueil, Ctrl K deroule la palette sous SA barre de recherche,
            // comme un clic dans la barre ; par-dessus un ecran, elle flotte.
            case "paletteOuvrir": OuvrirPalette(ancree: !AccueilRecouvert); break;
            case "paletteFermer": PaletteOuverte = false; break;
            case "paletteTexte": PaletteTexte = Texte(valeur); break;
            // « Ouvrir en page » : la meme recherche, dans un onglet, avec la liseuse.
            case "paletteEnPage":
                PageRechercheTexte = PaletteTexte;
                PaletteOuverte = false;
                OuvrirPageRecherche();
                break;
            case "rechercheTexte": PageRechercheTexte = Texte(valeur); break;
            case "paletteFiltre":
            {
                var code = Texte(valeur);
                PaletteFiltre = RecherchePalette.Filtres.Any(f => f.Code == code) ? code : "tout";
                break;
            }
            case "paletteAller":
            {
                var g = Lire<GestePaletteAller>(valeur);
                PaletteOuverte = false;
                switch (g.Nature)
                {
                    case "ecran": OuvrirCode(g.Cle); break;
                    case "siren": OuvrirUnDossierPour(g.Cle); break;
                    default: OuvrirParNumero(g.Cle); break;
                }
                break;
            }
            case "palettePiece":
            {
                var chemin = Lire<string[]>(valeur);
                PaletteOuverte = false;
                OuvrirElementTrouve(new ElementTrouve(AffaireCherchee, chemin));
                break;
            }
            case "paletteAttribuer":
            {
                var g = Lire<GestePaletteAttribuer>(valeur);
                var moi = Accueil.CompteCourant.Signature;
                // Seuls les comptes connus recoivent une demande : une signature
                // tapee a la main ne devient pas un agent du greffe.
                var a = g.Signature is { Length: > 0 } s ? s : moi;
                if (JeuDemo.Comptes.Any(c => c.Signature == a)
                    && JeuDemo.Demandes.Any(d => d.Numero == g.Numero))
                    JeuDemo.Attribuer(g.Numero, a);
                break;
            }
            default: throw new InvalidOperationException($"Geste inconnu de la palette : {geste}.");
        }
    }

    /// <summary>La palette, si elle est ouverte (jamais dans la fenetre d'un onglet sorti).</summary>
    private PaletteVue? VuePalette()
        => !PaletteOuverte || ModeFenetreVue ? null : Recherche(PaletteTexte, PaletteFiltre, PaletteAncree);

    /// <summary>La page « Recherche » : le MEME calcul que la palette, sur le texte de sa barre.</summary>
    private PaletteVue VuePageRecherche() => Recherche(PageRechercheTexte, "tout", ancree: false);

    /// <summary>Ouvre (ou ramene) l'onglet de la page « Recherche ».</summary>
    private void OuvrirPageRecherche() => OuvrirProc(new Proc(CodePageRecherche, "Recherche"));

    /// <summary>La recherche de la palette et de la page : le travail, les ecrans, l'arbre,
    /// le texte des pieces, hors du greffe.</summary>
    private PaletteVue Recherche(string texteTape, string filtre, bool ancree)
    {
        var compte = Accueil.CompteCourant;
        var moi = compte.Signature;
        var texte = texteTape.Trim();

        var trouve = RecherchePalette.Travail(texte,
            Fonds.Select(d => new AffairePalette(d.Numero, d.Objet, d.Siren, d.Type, d.Statut)), EstValide);
        var filtres = RecherchePalette.Filtres
            .Select(f => new FiltrePaletteVue(f.Code, f.Libelle, RecherchePalette.Filtrer(trouve, f.Code, moi).Count()))
            .ToArray();
        var gardees = RecherchePalette.Filtrer(trouve, filtre, moi).ToArray();

        // L'arbre se cherche tel qu'on l'OUVRIRAIT : celui de l'affaire ouverte
        // (avec ce que l'agent y a ajoute ou masque), sinon celui de la premiere
        // affaire du registre. Ce que le login ne voit pas ne se cherche pas.
        var affaire = AffaireCherchee;
        var racine = ArbreCherche();
        IEnumerable<NoeudGed> Visibles(NoeudGed n) => AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre);
        // Par le LIBELLE (un n° de depot, de formalite, une liasse) : « 2024/0141 »,
        // « 2024 0141 » et « 20240141 » sont le meme depot (RechercheArbre).
        // Par le TEXTE de la piece, avec le passage trouve.
        var pieces = RecherchePalette.Passages(racine, Visibles, texte);
        // Une piece trouvee des deux facons ne s'affiche qu'une fois, avec son
        // passage, qui en dit plus que son libelle (et range un avis BODACC
        // parmi les annonces).
        var elements = RechercheArbre.Chemins(racine, texte)
            .Where(c => !pieces.Any(p => p.Chemin.SequenceEqual(c)))
            .Select(c => (Chemin: c, Suite: SuiteVisible(racine, c, Visibles)))
            .Where(e => e.Suite is not null)
            .ToArray();
        var ecrans = texte.Length == 0 ? [] : TraitementsPour(texte);

        var siren = RecherchePalette.SirenTape(texte);
        // Hors du greffe : pour un SIREN tape, ou pour la societe que designe
        // la premiere ligne trouvee (son SIREN est connu).
        var societe = siren is not null
            ? (Nom: "", Siren: siren)
            : trouve.FirstOrDefault(l => l.Siren.Length > 0) is { } l
                ? (Nom: Denomination(l.Titre), Siren: l.Siren)
                : (Nom: "", Siren: "");
        var horsGreffe = texte.Length == 0 || societe.Siren.Length == 0
            ? []
            : RecherchePalette.HorsGreffe
                .Select(s => new HorsGreffePaletteVue(s.Code, s.Nom, s.Objet,
                    societe.Nom.Length > 0 ? $"{societe.Nom} · {societe.Siren}" : societe.Siren))
                .ToArray();

        var enCoursAMoi = JeuDemo.Demandes.Where(d => d.EnCours && !EstValide(d.Dossier) && d.Utilisateur == moi).ToArray();
        var bilan = new BilanPaletteVue(
            RecherchePalette.JourLisible(JeuDemo.Aujourdhui),
            JeuDemo.DemandesHorsDelai().Count(d => !EstValide(d.Dossier)),
            JeuDemo.Arrivees.Count(a => a.Instant.Date == JeuDemo.Aujourdhui.Date),
            enCoursAMoi.Length,
            enCoursAMoi.Length == 0 ? 0 : enCoursAMoi.Max(JeuDemo.JoursDepuis));

        return new PaletteVue(
            texteTape, filtre, filtres, Agent(moi),
            [.. JeuDemo.Comptes.Select(c => Agent(c.Signature))],
            [.. gardees.Take(RecherchePalette.MaxTravail).Select(t => new TravailPaletteVue(
                t.Nature, t.Numero, t.Cle, t.Ecran, LibelleEcran(t.Ecran), t.Titre, t.Detail, t.Etat, t.Ton, t.Filet,
                t.Qui.Length > 0 ? Agent(t.Qui) : null,
                t.Qui == moi,
                t.EnCours,
                t.Nature == "dem" && t.EnCours && t.Qui != moi,
                t.Delai, t.Siren,
                [.. RecherchePalette.Activite(t.Cle, compte)
                    .Select(a => new ActivitePaletteVue(a.Agent, a.Verbe, a.Objet, a.Depuis))]))],
            gardees.Length,
            [.. pieces.Select(p => new PiecePaletteVue(p.Piece.Libelle, p.Chemin, RecherchePalette.Lieu(p.Chemin),
                p.Avant, p.Trouve, p.Apres, p.Passages, p.Piece.Pages, p.Piece.Date, p.Piece.Origine,
                RecherchePalette.EstAnnonce(p.Piece), p.Piece.Apercu, p.Positions, p.Longueur))],
            $"{affaire.Numero} {Denomination(affaire.Objet)}",
            affaire.Numero,
            horsGreffe,
            bilan,
            siren is not null && trouve.Length == 0 ? siren : null,
            [.. ecrans.Select(p => new EcranPaletteVue(p.Code, p.Libelle))],
            [.. elements.Select(e => new ElementPaletteVue(e.Chemin[^1], e.Chemin, RecherchePalette.Lieu(e.Chemin),
                                                           e.Suite![^1].EstPiece))],
            ancree);
    }

    /// <summary>Les noeuds d'un chemin, s'il passe tout entier par des noeuds
    /// VISIBLES pour ce login ; null sinon (branche d'un autre domaine, masquee).</summary>
    private static NoeudGed[]? SuiteVisible(NoeudGed racine, string[] chemin, Func<NoeudGed, IEnumerable<NoeudGed>> visibles)
    {
        var suite = new List<NoeudGed>();
        var courant = racine;
        foreach (var libelle in chemin)
        {
            var enfant = visibles(courant).FirstOrDefault(e => e.Libelle == libelle);
            if (enfant is null) return null;
            suite.Add(enfant);
            courant = enfant;
        }
        return suite.Count > 0 ? [.. suite] : null;
    }

    /// <summary>Le libelle d'un ecran dans le catalogue du menu ; vide s'il n'y est pas.</summary>
    private string LibelleEcran(string code)
        => code.Length == 0 ? ""
         : Groupes.SelectMany(g => g.Procs).FirstOrDefault(p => p.Code == code)?.Libelle
           ?? EntreeDeCode(code)?.Libelle ?? "";

    /// <summary>« Depot de bilan - TRANSNORD SA » nomme la societe apres le tiret.</summary>
    private static string Denomination(string objet)
        => objet.Contains(" - ") ? objet[(objet.IndexOf(" - ", StringComparison.Ordinal) + 3)..] : objet;

    /// <summary>Un agent : son prenom quand un compte porte cette signature.</summary>
    private static AgentPaletteVue Agent(string signature)
        => new(signature, RecherchePalette.Initiales(signature),
               JeuDemo.Comptes.FirstOrDefault(c => c.Signature == signature)?.Nom ?? signature);
}
