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
public sealed record PaletteVue(string Texte, string Filtre, FiltrePaletteVue[] Filtres, AgentPaletteVue Moi,
                                AgentPaletteVue[] Agents, TravailPaletteVue[] Travail, int TravailTrouve,
                                PiecePaletteVue[] Pieces, string DossierDesPieces, string NumeroDesPieces,
                                HorsGreffePaletteVue[] HorsGreffe,
                                BilanPaletteVue Bilan, string? SirenInconnu);
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
public sealed record PiecePaletteVue(string Libelle, string[] Chemin, string Lieu, string Avant, string Trouve,
                                     string Apres, int Passages, int Pages, string Date, string Origine, bool Annonce);
/// <param name="Sujet">Ce qui serait consulte (« TRANSNORD SA · 318 552 004 »).</param>
public sealed record HorsGreffePaletteVue(string Code, string Source, string Objet, string Sujet);
/// <param name="PlusAncienne">Jours depuis la plus ancienne demande en cours confiee au login.</param>
public sealed record BilanPaletteVue(string Jour, int HorsDelai, int ArriveesDuJour, int MAttendent, int PlusAncienne);

public sealed partial class SessionPlanTravail
{
    private bool PaletteOuverte;
    private string PaletteTexte = string.Empty;
    private string PaletteFiltre = "tout";

    private sealed record GestePaletteAller(string Nature, string Cle);
    private sealed record GestePaletteAttribuer(string Numero, string? Signature);

    /// <summary>Ctrl K, ou la pastille « Ctrl K » de la recherche de l'accueil.</summary>
    private void OuvrirPalette()
    {
        PaletteOuverte = !ModeFenetreVue;
        PaletteTexte = string.Empty;
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
            case "paletteOuvrir": OuvrirPalette(); break;
            case "paletteFermer": PaletteOuverte = false; break;
            case "paletteTexte": PaletteTexte = Texte(valeur); break;
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
    {
        if (!PaletteOuverte || ModeFenetreVue) return null;

        var compte = Accueil.CompteCourant;
        var moi = compte.Signature;
        var texte = PaletteTexte.Trim();

        var trouve = RecherchePalette.Travail(texte,
            Fonds.Select(d => new AffairePalette(d.Numero, d.Objet, d.Siren, d.Type, d.Statut)), EstValide);
        var filtres = RecherchePalette.Filtres
            .Select(f => new FiltrePaletteVue(f.Code, f.Libelle, RecherchePalette.Filtrer(trouve, f.Code, moi).Count()))
            .ToArray();
        var gardees = RecherchePalette.Filtrer(trouve, PaletteFiltre, moi).ToArray();

        // Les pieces se cherchent dans l'arbre que l'on OUVRIRAIT : celui de
        // l'affaire ouverte (avec ce que l'agent y a ajoute ou masque), sinon
        // celui de la premiere affaire du registre -- le meme que la recherche
        // d'elements de l'accueil (ElementsTrouves).
        var affaire = AffaireCherchee;
        var racine = ReferenceEquals(affaire, DossierOuvert)
            ? Arbre
            : ArbreDemo.Racine(affaire.Numero, affaire.Objet, affaire.Siren, avecDossierRcs: !affaire.SansDossierRcs);
        var pieces = RecherchePalette.Passages(racine,
            n => AccesGed.EnfantsVisibles(n, Domaines, AjoutsArbre, MasquesArbre), texte);

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
            PaletteTexte, PaletteFiltre, filtres, Agent(moi),
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
                RecherchePalette.EstAnnonce(p.Piece)))],
            $"{affaire.Numero} {Denomination(affaire.Objet)}",
            affaire.Numero,
            horsGreffe,
            bilan,
            siren is not null && trouve.Length == 0 ? siren : null);
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
