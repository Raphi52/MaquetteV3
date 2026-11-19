using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RigV3Desktop;

// ============================================================
// LA PALETTE DE RECHERCHE (Ctrl K, depuis n'importe quel ecran).
//
// /draft « Page Recherche » du 19/11/2026 (conv-2018) : sur le socle de la
// palette blanche « Empilee », deux familles d'elements retenues par
// l'utilisateur (tour 3, propositions 3 et 6) :
//   3 · MON TRAVAIL : le plus urgent d'abord, qui s'en occupe, delai legal
//       depasse, activite recente, filtre « A moi », m'attribuer ;
//   6 · PIECES : la recherche dans le TEXTE des pieces de l'arbre, les
//       fichiers hors du greffe, les annonces.
//
// Ce module est la RECHERCHE seule, sans etat d'ecran : la session du plan
// de travail (Pont/PlanTravailPalette.cs) lui passe ce qu'elle seule sait
// (le login, l'arbre ouvert, les dossiers valides au controle final) et en
// fait la vue. Les donnees sont celles du jeu de demonstration : la meme
// file de demandes que l'ecran Demandes, le meme courrier que l'Arrivee, les
// memes actes que les Actes numerises, le meme journal que la tuile Activite.
// ============================================================

/// <summary>
/// Une ligne de « Mon travail » : une demande, une affaire, un courrier ou un
/// acte numerise, avec ce qui dit son urgence.
/// </summary>
/// <param name="Nature">« dem » demande, « aff » affaire, « cou » courrier, « doc » acte numerise.</param>
/// <param name="Numero">Le numero affiche en tete de ligne.</param>
/// <param name="Cle">Le dossier qu'ouvre la ligne ; vide = l'ecran <paramref name="Ecran"/>.</param>
/// <param name="Ton">Couleur de la pastille d'etat : « al » (alerte), « ok », ou vide.</param>
/// <param name="Filet">Statut du filet de gauche (classes rig-filet-v--*).</param>
/// <param name="Qui">L'agent qui s'en occupe (« C. BENIER ») ; vide = personne.</param>
/// <param name="Urgence">0 hors delai, 1 a traiter, 2 en attente, 3 termine : l'ordre de la liste.</param>
/// <param name="Delai">Ou en est le delai legal, pour une demande en cours ; null sinon.</param>
public sealed record LigneTravail(string Nature, string Numero, string Cle, string Ecran,
                                  string Titre, string Detail, string Etat, string Ton, string Filet,
                                  string Qui, DateTime Date, int Urgence, int Jours, string? Delai,
                                  string Siren, bool EnCours);

/// <summary>Une affaire du fonds interrogeable (le plan de travail tient la liste).</summary>
public sealed record AffairePalette(string Numero, string Objet, string Siren, string Type, string Statut);

/// <summary>Un passage trouve dans le TEXTE d'une piece de l'arbre.</summary>
/// <param name="Chemin">Les libelles depuis la racine (exclue) : il deplie l'arbre jusqu'a la piece.</param>
/// <param name="Avant">Le texte qui precede le premier passage trouve.</param>
/// <param name="Trouve">Le passage lui-meme, tel qu'il est ecrit dans la piece.</param>
/// <param name="Passages">Combien de fois la piece porte ce qui est tape.</param>
/// <param name="Positions">Ou commence chaque passage dans le texte de la piece (Apercu), dans
/// l'ordre ; tous font <paramref name="Longueur"/> caracteres.</param>
public sealed record PassagePiece(string[] Chemin, NoeudGed Piece, string Avant, string Trouve,
                                  string Apres, int Passages, int[] Positions, int Longueur);

/// <summary>Un fichier consulte HORS du greffe. Aucun n'est branche dans la maquette.</summary>
public sealed record SourceExterne(string Code, string Nom, string Objet);

public static class RecherchePalette
{
    /// <summary>Les filtres de « Mon travail », dans l'ordre de la barre (Tab passe au suivant).</summary>
    public static readonly (string Code, string Libelle)[] Filtres =
    [
        ("tout", "Tout"), ("moi", "A moi"), ("horsDelai", "Hors delai"), ("semaine", "Cette semaine")
    ];

    /// <summary>Les fichiers hors du greffe que la palette propose pour une societe.</summary>
    public static readonly SourceExterne[] HorsGreffe =
    [
        new("FIBEN", "FIBEN", "cotation et incidents de paiement, Banque de France"),
        new("IDG", "Interdits de gerer", "fichier national des interdits de gerer"),
        new("RBE", "Beneficiaires effectifs", "registre des beneficiaires effectifs")
    ];

    /// <summary>Nombre de lignes de travail montrees ; le compte total reste donne.</summary>
    public const int MaxTravail = 8;

    // ---------------------------------------------------------------- comparer

    /// <summary>
    /// Comme un humain lit un numero ou un nom : sans accents, sans casse, sans
    /// separateurs. « 318 552 004 », « 318.552.004 » et « 318552004 » sont le
    /// meme SIREN ; « Depot » trouve « Dépôt ».
    /// </summary>
    public static string Plier(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Vrai si CHAQUE mot tape se retrouve dans l'un des champs. « transnord
    /// bilan » trouve « Depot de bilan - TRANSNORD SA » ; un mot de moins de
    /// deux caracteres utiles ne suffit pas a chercher.
    /// </summary>
    public static bool Correspond(string texte, params string[] champs)
    {
        if (Plier(texte).Length < 2) return false;
        var tout = Plier(string.Join(" ", champs));
        return Mots(texte).All(m => tout.Contains(m, StringComparison.Ordinal));
    }

    /// <summary>
    /// Les mots tapes, plies. Des groupes de chiffres qui se suivent forment UN
    /// numero : « 318 552 004 » est un SIREN, pas trois nombres qu'on
    /// trouverait chacun ailleurs (« 552 » est aussi dans un autre SIREN).
    /// </summary>
    private static List<string> Mots(string texte)
    {
        var mots = new List<string>();
        foreach (var m in texte.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Plier).Where(m => m.Length > 0))
        {
            if (mots.Count > 0 && m.All(char.IsDigit) && mots[^1].All(char.IsDigit)) mots[^1] += m;
            else mots.Add(m);
        }
        return mots;
    }

    /// <summary>« C. BENIER » donne « CB », « R. VILAIN » donne « RV ».</summary>
    public static string Initiales(string signature)
    {
        var mots = signature.Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        return mots.Length == 0 ? ""
             : mots.Length == 1 ? mots[0][..Math.Min(2, mots[0].Length)].ToUpperInvariant()
             : $"{char.ToUpperInvariant(mots[0][0])}{char.ToUpperInvariant(mots[^1][0])}";
    }

    /// <summary>Un SIREN tape en entier (9 chiffres, separateurs ignores), ou null.</summary>
    public static string? SirenTape(string texte)
    {
        var p = Plier(texte);
        return p.Length == 9 && p.All(char.IsDigit) ? $"{p[..3]} {p[3..6]} {p[6..]}" : null;
    }

    // ---------------------------------------------------------------- mon travail

    /// <summary>
    /// Le travail qui porte ce qui est tape, LE PLUS URGENT D'ABORD : hors delai
    /// (le plus ancien en tete), a traiter, en attente, termine. Texte vide : le
    /// travail encore ouvert (demandes en cours, courrier a rattacher).
    /// </summary>
    /// <param name="estValide">Le controle final du plan de travail : une demande
    /// validee y est terminee, quel que soit l'etat qu'elle porte.</param>
    public static LigneTravail[] Travail(string texte, IEnumerable<AffairePalette> affaires,
                                         Func<string, bool> estValide)
    {
        var demandes = JeuDemo.Demandes.Select(d => DeDemande(d, estValide(d.Dossier))).ToList();
        // Une affaire qui est aussi une demande de la file n'apparait qu'une
        // fois : la demande dit deja qui s'en occupe et ou en est le delai.
        var numerosDemandes = demandes.Select(l => l.Numero).ToHashSet(StringComparer.Ordinal);
        var toutes = demandes
            .Concat(affaires.Where(a => !numerosDemandes.Contains(a.Numero)).Select(DeAffaire))
            .Concat(JeuDemo.Arrivees.Select(DuCourrier))
            .Concat(JeuDemo.Actes.Select(DeLActe));

        var gardees = string.IsNullOrWhiteSpace(texte)
            ? toutes.Where(l => l.EnCours)
            : toutes.Where(l => Correspond(texte, l.Numero, l.Titre, l.Siren, l.Detail, l.Qui));

        return [.. gardees.OrderBy(l => l.Urgence)
                          .ThenByDescending(l => l.Jours)
                          .ThenBy(l => l.Numero, StringComparer.Ordinal)];
    }

    /// <summary>Les lignes que garde un filtre de « Mon travail ».</summary>
    public static IEnumerable<LigneTravail> Filtrer(IEnumerable<LigneTravail> lignes, string filtre, string moi)
        => filtre switch
        {
            "moi" => lignes.Where(l => l.Qui == moi),
            "horsDelai" => lignes.Where(l => l.Urgence == 0),
            "semaine" => lignes.Where(l => l.Jours is >= 0 and < 7),
            _ => lignes
        };

    private static LigneTravail DeDemande(DemandeRcs d, bool valide)
    {
        var jours = JeuDemo.JoursDepuis(d);
        var detail = $"Demande du {d.Creation} · {d.Client}".TrimEnd(' ', '·');
        if (valide)
            return new("dem", d.Numero, d.Dossier, "PROC_DEMANDE", d.Nom, detail, "Validee", "ok", "ok",
                       d.Utilisateur, d.Creation2, 3, jours, null, d.Siren, false);
        if (!d.EnCours)
            return new("dem", d.Numero, d.Dossier, "PROC_DEMANDE", d.Nom, detail, d.Etat,
                       d.Etat == "Validee" ? "ok" : "", d.Statut, d.Utilisateur, d.Creation2, 3, jours, null,
                       d.Siren, false);

        var reste = JeuDemo.DelaiLegalJours - jours;
        return reste < 0
            ? new("dem", d.Numero, d.Dossier, "PROC_DEMANDE", d.Nom, detail, $"J+{jours}", "al", "alerte",
                  d.Utilisateur, d.Creation2, 0, jours,
                  $"Delai legal de {JeuDemo.DelaiLegalJours} jours depasse de {Jours(-reste)}", d.Siren, true)
            : new("dem", d.Numero, d.Dossier, "PROC_DEMANDE", d.Nom, detail, d.Etat, "", "action",
                  d.Utilisateur, d.Creation2, 1, jours,
                  reste == 0 ? "Dernier jour du delai legal" : $"Reste {Jours(reste)} sur le delai legal",
                  d.Siren, true);
    }

    private static LigneTravail DeAffaire(AffairePalette a)
    {
        var urgence = a.Statut is "alerte" or "action" ? 1 : 2;
        return new("aff", a.Numero, a.Numero, "", a.Objet, a.Siren.Length > 0 ? $"Affaire {a.Type} · SIREN {a.Siren}" : $"Affaire {a.Type}",
                   // La pastille dit le TYPE de l'affaire ; le filet, lui, porte l'alerte.
                   a.Type, "", a.Statut, "", JeuDemo.Aujourdhui, urgence, -1, null,
                   a.Siren, false);
    }

    private static LigneTravail DuCourrier(Arrivee a)
    {
        var jours = (int)(JeuDemo.Aujourdhui.Date - a.Instant.Date).TotalDays;
        var titre = $"{a.TypeDemande} - {a.Nom}";
        var detail = $"Courrier · {a.ModeLisible} · {a.Horodatage}";
        if (a.NumDemande.Length == 0)
            return new("cou", a.Numero, "", "PROC_ARRIVEE", titre, detail, "A rattacher", "", "action",
                       a.Utilisateur, a.Instant, 1, jours, null, "", true);
        return !a.Numerise
            ? new("cou", a.Numero, a.NumDemande, "PROC_ARRIVEE", titre, detail, "A numeriser", "", "attente",
                  a.Utilisateur, a.Instant, 2, jours, null, "", false)
            : new("cou", a.Numero, a.NumDemande, "PROC_ARRIVEE", titre, detail, $"Rattache a {a.NumDemande}", "ok", "ok",
                  a.Utilisateur, a.Instant, 3, jours, null, "", false);
    }

    private static LigneTravail DeLActe(ActeNum a)
    {
        var jours = (int)(JeuDemo.Aujourdhui.Date - a.DateDepot.Date).TotalDays;
        var titre = $"{a.Description} - {a.Denomination}";
        var detail = $"{a.CategorieLisible} · depose le {a.Depose}" + (a.Confidentiel ? " · confidentiel" : "");
        if (!a.Numerisable)
            return new("doc", a.Depot, a.Dossier, "PROC_ACTES_NUM", titre, detail, "Papier", "", "neutre",
                       "", a.DateDepot, 3, jours, null, "", false);
        if (a.Anomalie.Length > 0 && a.Demat)
            return new("doc", a.Depot, a.Dossier, "PROC_ACTES_NUM", titre, detail, a.Anomalie, "al", "alerte",
                       "", a.DateDepot, 1, jours, null, "", false);
        return a.Demat
            ? new("doc", a.Depot, a.Dossier, "PROC_ACTES_NUM", titre, detail, "Numerise", "ok", "ok",
                  "", a.DateDepot, 3, jours, null, "", false)
            : new("doc", a.Depot, a.Dossier, "PROC_ACTES_NUM", titre, detail, "A numeriser", "", "attente",
                  "", a.DateDepot, 2, jours, null, "", false);
    }

    private static string Jours(int n) => n == 1 ? "1 jour" : $"{n} jours";

    /// <summary>
    /// Ce qui s'est passe sur un dossier, du plus recent au plus ancien. Le
    /// journal des AUTRES n'est montre qu'au compte qui valide (meme regle que
    /// la tuile Activite, <see cref="JournalActions.VisiblePour"/>) : un
    /// redacteur ne voit que ses propres gestes.
    /// </summary>
    public static ActionAgent[] Activite(string dossier, CompteRig compte, int combien = 3)
    {
        if (dossier.Length == 0) return [];
        var mot = new Regex($@"(?<![\w-]){Regex.Escape(dossier)}(?![\w-])");
        var source = JournalActions.VisiblePour(compte) ? JournalActions.Toutes : JournalActions.De(compte.Login);
        return [.. source.Where(a => mot.IsMatch(a.Objet)).Take(combien)];
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>
    /// Les pieces de l'arbre dont le TEXTE porte ce qui est tape (accents et
    /// casse ignores), avec le premier passage et le nombre de passages. Trois
    /// caracteres au moins : en dessous, presque toutes les pieces repondraient.
    /// </summary>
    /// <param name="enfants">Les enfants VISIBLES d'un noeud pour ce login (domaines,
    /// pieces masquees) : une piece qu'on ne peut pas ouvrir ne se cherche pas.</param>
    public static PassagePiece[] Passages(NoeudGed racine, Func<NoeudGed, IEnumerable<NoeudGed>> enfants,
                                          string texte, int max = 6)
    {
        var cherche = Regex.Replace(texte.Trim(), @"\s+", " ");
        if (Plier(cherche).Length < 3) return [];
        var plie = PlierAPlat(cherche);

        var trouves = new List<PassagePiece>();
        void Descendre(NoeudGed noeud, string[] chemin)
        {
            foreach (var e in enfants(noeud))
            {
                if (trouves.Count >= max) return;
                string[] ici = [.. chemin, e.Libelle];
                if (e.EstPiece && e.Apercu.Length > 0 && Passage(e.Apercu, plie) is { } p)
                    trouves.Add(new PassagePiece(ici, e, p.Avant, p.Trouve, p.Apres, p.Positions.Length,
                                                 p.Positions, plie.Length));
                Descendre(e, ici);
            }
        }
        Descendre(racine, []);
        return [.. trouves];
    }

    private static (string Avant, string Trouve, string Apres, int[] Positions)? Passage(string texte, string cherchePlie)
    {
        var plie = PlierAPlat(texte);
        var i = plie.IndexOf(cherchePlie, StringComparison.Ordinal);
        if (i < 0) return null;

        var positions = new List<int>();
        for (var j = i; j >= 0; j = plie.IndexOf(cherchePlie, j + cherchePlie.Length, StringComparison.Ordinal))
            positions.Add(j);

        var debut = Math.Max(0, i - 48);
        var fin = Math.Min(texte.Length, i + cherchePlie.Length + 72);
        // Couper sur un blanc, pas au milieu d'un mot.
        if (debut > 0 && texte.IndexOf(' ', debut, i - debut) is var b and >= 0) debut = b + 1;
        if (fin < texte.Length && texte.LastIndexOf(' ', fin - 1, fin - (i + cherchePlie.Length)) is var e and >= 0) fin = e;

        return ((debut > 0 ? "…" : "") + Lisible(texte[debut..i]),
                texte.Substring(i, cherchePlie.Length),
                Lisible(texte[(i + cherchePlie.Length)..fin]) + (fin < texte.Length ? "…" : ""), [.. positions]);
    }

    /// <summary>Sans accents, sans casse, MEME LONGUEUR que le texte : une position
    /// trouvee dans le pli est la position dans le texte d'origine.</summary>
    private static string PlierAPlat(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (var c in texte)
        {
            var base_ = c.ToString().Normalize(NormalizationForm.FormD)[0];
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToUpperInvariant(base_));
        }
        return sb.ToString();
    }

    /// <summary>Une piece s'ecrit sur plusieurs lignes ; l'extrait, sur une seule.</summary>
    private static string Lisible(string morceau) => Regex.Replace(morceau, @"[ \t]*\n[\s]*", " · ");

    /// <summary>Le libelle d'un dossier, en mots (« Depots d'actes > Depot n 2024/0141 »).</summary>
    public static string Lieu(string[] chemin)
        => string.Join(" > ", chemin[..^1].Skip(chemin.Length > 2 ? 1 : 0));

    /// <summary>Une annonce, dans les pieces : son libelle le dit.</summary>
    public static bool EstAnnonce(NoeudGed piece)
        => piece.Libelle.Contains("BODACC", StringComparison.OrdinalIgnoreCase);

    /// <summary>« mercredi 9 septembre », pour le bilan du jour.</summary>
    public static string JourLisible(DateTime d)
        => d.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("fr-FR"));
}
