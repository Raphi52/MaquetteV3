using System.Globalization;
using System.Runtime.CompilerServices;

namespace RigV3Desktop.Pont;

/// <summary>
/// Une ligne de l'arbre GED telle que web/src/composants/ClasseurArbre.tsx la
/// rend, avec ses enfants deja calcules (vides tant que la branche est repliee).
/// </summary>
/// <param name="Id">Identifiant STABLE du noeud (son identite, pas son libelle) : il revient avec chaque geste.</param>
/// <param name="Depliable">Le noeud a des enfants affiches : chevron plein, sinon chevron vide.</param>
/// <param name="Compte">Le nombre de pieces entre parentheses, ou null quand il ne s'affiche pas.</param>
/// <param name="Vide">« aucune piece » s'affiche sous la ligne (dossier ouvert sans enfant).</param>
/// <param name="Groupe">La teinte du LISERE (1 a 8) de la rubrique qui range la ligne, 0 hors rubrique :
/// tout ce qui va ensemble porte le meme filet de couleur (demande du 19/11/2026).</param>
/// <param name="NiveauGroupe">Le niveau de la tete de la rubrique : le lisere se pose a son retrait.</param>
/// <param name="Date">Une PIECE : sa date de depot (jj/mm/aaaa), vide sinon. Le classeur de
/// l'arbre (19/11/2026, /draft « Chronologie ») la montre et range ses pieces par date.</param>
/// <param name="Pages">Une PIECE : son nombre de pages, 0 sinon.</param>
/// <param name="Derniere">Un DOSSIER : la date de sa piece la plus recente (jj/mm/aaaa), vide
/// s'il n'en a aucune de datee -- la carte fermee d'une rubrique l'annonce.</param>
/// <param name="Replie">Un SOUS-DOSSIER replie dans le classeur (« Par dossier ») : son intertitre
/// reste, ce qu'il range se cache. Ses enfants sont rendus quand meme : « Par date » les montre.
/// Jamais pendant une recherche, qui montre ce qu'elle trouve.</param>
public sealed record LigneArbreVue(string Id, int Niveau, string Texte, string Icone,
                                   bool Actif, bool Chemin, bool Depliable, bool Ouvert,
                                   bool Renomme, int? Compte, LigneArbreVue[] Enfants, bool Vide,
                                   int Groupe = 0, int NiveauGroupe = 0,
                                   string Date = "", int Pages = 0, string Derniere = "",
                                   bool Replie = false);

/// <summary>
/// Pont du composant RECURSIF de l'arbre GED (web/src/composants/ClasseurArbre.tsx).
///
/// Les regles du @code de Components/ArbreGedNoeud.razor sont DEPLACEES ici telles
/// quelles (enfants visibles, filtre, branches ouvertes, chemin, repli au clic) :
/// la coquille les appelle avec ses parametres d'origine -- des objets VIVANTS de
/// l'accueil (HashSet des noeuds ouverts, ajouts, masques...) qui ne traversent pas
/// vers JavaScript -- et passe la vue a React. Metier/ n'est pas modifie.
///
/// Pourquoi un identifiant par IDENTITE : deux dossiers peuvent porter le meme
/// libelle a deux endroits de l'arbre, et NoeudGed est un record (egalite par
/// valeur). Le .razor comparait par ReferenceEquals ; la table ci-dessous fait de
/// meme, sans retenir un noeud que l'accueil a oublie.
/// </summary>
public static class ArbreGedNoeud
{
    private static readonly ConditionalWeakTable<NoeudGed, string> Identifiants = new();
    private static long Dernier;

    /// <summary>L'identifiant stable de ce noeud (le meme a chaque rendu).</summary>
    public static string Identifiant(NoeudGed noeud)
        => Identifiants.GetValue(noeud, _ => "n" + Interlocked.Increment(ref Dernier));

    /// <summary>
    /// La ligne de ce noeud et, si elle est ouverte, tout ce qui se rend dessous.
    /// <paramref name="index"/> recoit chaque noeud rendu sous son identifiant :
    /// c'est par lui que la coquille retrouve le noeud d'un geste.
    /// <paramref name="groupes"/> donne la teinte de chaque TETE de rubrique ; ses
    /// descendants la portent aussi (<paramref name="groupe"/>, <paramref name="niveauGroupe"/>).
    /// </summary>
    public static LigneArbreVue Vue(NoeudGed noeud, int niveau, IReadOnlySet<NoeudGed> ouverts,
                                    NoeudGed? selection, string[] domaines,
                                    IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
                                    IReadOnlyCollection<NoeudGed>? masques,
                                    IReadOnlyDictionary<NoeudGed, string>? libelles,
                                    RecetteTache? recette, NoeudGed? renomme, string? filtre,
                                    IDictionary<string, NoeudGed> index,
                                    IReadOnlyDictionary<NoeudGed, int>? groupes = null,
                                    int groupe = 0, int niveauGroupe = 0,
                                    IReadOnlySet<NoeudGed>? replies = null)
    {
        var id = Identifiant(noeud);
        index[id] = noeud;
        if (groupes is not null && groupes.TryGetValue(noeud, out var teinte))
        {
            groupe = teinte;
            niveauGroupe = niveau;
        }

        var visibles = EnfantsVisibles(noeud, domaines, ajouts, masques, recette);
        var affiches = EnfantsAffiches(visibles, domaines, ajouts, masques, libelles, recette, filtre);
        // Pendant une recherche, les branches qui menent a un resultat s'ouvrent
        // seules : un resultat cache sous un dossier replie n'est pas trouve.
        var ouvert = ouverts.Contains(noeud)
                     || (!string.IsNullOrWhiteSpace(filtre) && affiches.Count > 0);

        var enfants = ouvert
            ? affiches.Select(e => Vue(e, niveau + 1, ouverts, selection, domaines, ajouts, masques,
                                        libelles, recette, renomme, filtre, index,
                                        groupes, groupe, niveauGroupe, replies)).ToArray()
            : [];

        return new LigneArbreVue(
            id, niveau, Texte(noeud, libelles), AccesGed.CodeIcone(noeud),
            Actif: ReferenceEquals(selection, noeud),
            Chemin: SurLeChemin(noeud, selection),
            Depliable: affiches.Count > 0,
            Ouvert: ouvert,
            Renomme: ReferenceEquals(renomme, noeud),
            // Les pieces qu'il range pour ce login, ajouts compris, suppressions exclues (lot 4,
            // 20/11/2026) : NbPieces, le compte fige du jeu de demonstration, ignorait les pieces
            // jointes et supprimees (GedCheck, « une piece jointe compte dans sa carte »).
            Compte: visibles.Count > 0 && AccesGed.PiecesSous(noeud, domaines, ajouts, masques).Count() is > 0 and var n
                ? n : null,
            Enfants: enfants,
            Vide: ouvert && visibles.Count == 0 && string.IsNullOrWhiteSpace(filtre)
                  && !noeud.EstPiece && !noeud.EstFiche,
            Groupe: groupe,
            NiveauGroupe: niveauGroupe,
            Date: noeud.EstPiece ? noeud.Date : "",
            Pages: noeud.EstPiece ? noeud.Pages : 0,
            Derniere: noeud.EstPiece || noeud.EstFiche
                ? ""
                : DerniereDate(noeud, domaines, ajouts, masques, recette)?.ToString(FormatDate, CultureInfo.InvariantCulture) ?? "",
            Replie: replies is not null && replies.Contains(noeud) && string.IsNullOrWhiteSpace(filtre));
    }

    /// <summary>Le format des dates de l'arbre (NoeudGed.Date).</summary>
    private const string FormatDate = "dd/MM/yyyy";

    /// <summary>
    /// La date de la piece la plus recente sous ce dossier, parmi les pieces VISIBLES
    /// (droits du login, ajouts, masques, mode tache) -- y compris sous une branche
    /// repliee : la carte fermee d'une rubrique l'annonce sans etre ouverte.
    /// </summary>
    private static DateTime? DerniereDate(NoeudGed dossier, string[] domaines,
                                          IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
                                          IReadOnlyCollection<NoeudGed>? masques, RecetteTache? recette)
    {
        DateTime? plusRecente = null;
        foreach (var e in EnfantsVisibles(dossier, domaines, ajouts, masques, recette))
        {
            var date = e.EstPiece
                ? DateTime.TryParseExact(e.Date, FormatDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null
                : e.EstFiche ? null : DerniereDate(e, domaines, ajouts, masques, recette);
            if (date is { } x && (plusRecente is null || x > plusRecente)) plusRecente = x;
        }
        return plusRecente;
    }

    /// <summary>
    /// Le clic sur une ligne (Activer du .razor) : un dossier se replie/se deplie ;
    /// une piece se selectionne. Un dossier vide reste depliable : c'est ainsi que
    /// v2 montre « aucune piece » plutot que de cacher le dossier. La coquille leve
    /// ensuite Selectionner, comme le .razor.
    /// </summary>
    public static void Activer(NoeudGed noeud, HashSet<NoeudGed> ouverts, string[] domaines,
                               IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
                               IReadOnlyCollection<NoeudGed>? masques, RecetteTache? recette)
    {
        if (!noeud.EstFiche && (EnfantsVisibles(noeud, domaines, ajouts, masques, recette).Count > 0 || !noeud.EstPiece))
        {
            if (!ouverts.Add(noeud)) ouverts.Remove(noeud);
        }
    }

    private static string Texte(NoeudGed n, IReadOnlyDictionary<NoeudGed, string>? libelles)
        => libelles is not null && libelles.TryGetValue(n, out var l) ? l : n.Libelle;

    private static IReadOnlyList<NoeudGed> EnfantsVisibles(
        NoeudGed n, string[] domaines, IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
        IReadOnlyCollection<NoeudGed>? masques, RecetteTache? recette)
        => AccesGed.EnfantsVisibles(n, domaines, ajouts, masques, recette);

    /// <summary>Les enfants rendus : les visibles, restreints par le filtre de la barre
    /// de recherche (seules les branches dont un element porte ce texte restent).</summary>
    private static IReadOnlyList<NoeudGed> EnfantsAffiches(
        IReadOnlyList<NoeudGed> visibles, string[] domaines,
        IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts, IReadOnlyCollection<NoeudGed>? masques,
        IReadOnlyDictionary<NoeudGed, string>? libelles, RecetteTache? recette, string? filtre)
        => string.IsNullOrWhiteSpace(filtre)
            ? visibles
            : visibles.Where(n => Correspond(n, FiltreArbre.Normaliser(filtre), domaines, ajouts, masques,
                                             libelles, recette)).ToList();

    /// <summary>Le noeud porte le texte cherche, ou l'un de ses descendants visibles.</summary>
    private static bool Correspond(NoeudGed n, string cle, string[] domaines,
                                   IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
                                   IReadOnlyCollection<NoeudGed>? masques,
                                   IReadOnlyDictionary<NoeudGed, string>? libelles, RecetteTache? recette)
    {
        if (FiltreArbre.Normaliser(Texte(n, libelles)).Contains(cle)) return true;
        return EnfantsVisibles(n, domaines, ajouts, masques, recette)
            .Any(e => Correspond(e, cle, domaines, ajouts, masques, libelles, recette));
    }

    /// <summary>
    /// Ce noeud est-il un PARENT du noeud selectionne ? La comparaison porte sur
    /// l'identite du noeud, pas sur son libelle : deux dossiers peuvent porter le
    /// meme nom a deux endroits de l'arbre.
    /// </summary>
    private static bool SurLeChemin(NoeudGed noeud, NoeudGed? selection)
        => selection is not null
           && !ReferenceEquals(selection, noeud)
           && noeud.Descendants().Any(d => ReferenceEquals(d, selection));
}
