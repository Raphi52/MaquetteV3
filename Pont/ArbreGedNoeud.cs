using System.Runtime.CompilerServices;

namespace RigV3Desktop.Pont;

/// <summary>
/// Une ligne de l'arbre GED telle que web/src/composants/ArbreGedNoeud.tsx la
/// rend, avec ses enfants deja calcules (vides tant que la branche est repliee).
/// </summary>
/// <param name="Id">Identifiant STABLE du noeud (son identite, pas son libelle) : il revient avec chaque geste.</param>
/// <param name="Depliable">Le noeud a des enfants affiches : chevron plein, sinon chevron vide.</param>
/// <param name="Compte">Le nombre de pieces entre parentheses, ou null quand il ne s'affiche pas.</param>
/// <param name="Vide">« aucune piece » s'affiche sous la ligne (dossier ouvert sans enfant).</param>
public sealed record LigneArbreVue(string Id, int Niveau, string Texte, string Icone,
                                   bool Actif, bool Chemin, bool Depliable, bool Ouvert,
                                   bool Renomme, int? Compte, LigneArbreVue[] Enfants, bool Vide);

/// <summary>
/// Pont du composant RECURSIF de l'arbre GED (web/src/composants/ArbreGedNoeud.tsx).
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
    /// </summary>
    public static LigneArbreVue Vue(NoeudGed noeud, int niveau, IReadOnlySet<NoeudGed> ouverts,
                                    NoeudGed? selection, string[] domaines,
                                    IReadOnlyDictionary<NoeudGed, List<NoeudGed>>? ajouts,
                                    IReadOnlyCollection<NoeudGed>? masques,
                                    IReadOnlyDictionary<NoeudGed, string>? libelles,
                                    RecetteTache? recette, NoeudGed? renomme, string? filtre,
                                    IDictionary<string, NoeudGed> index)
    {
        var id = Identifiant(noeud);
        index[id] = noeud;

        var visibles = EnfantsVisibles(noeud, domaines, ajouts, masques, recette);
        var affiches = EnfantsAffiches(visibles, domaines, ajouts, masques, libelles, recette, filtre);
        // Pendant une recherche, les branches qui menent a un resultat s'ouvrent
        // seules : un resultat cache sous un dossier replie n'est pas trouve.
        var ouvert = ouverts.Contains(noeud)
                     || (!string.IsNullOrWhiteSpace(filtre) && affiches.Count > 0);

        var enfants = ouvert
            ? affiches.Select(e => Vue(e, niveau + 1, ouverts, selection, domaines, ajouts, masques,
                                        libelles, recette, renomme, filtre, index)).ToArray()
            : [];

        return new LigneArbreVue(
            id, niveau, Texte(noeud, libelles), AccesGed.CodeIcone(noeud),
            Actif: ReferenceEquals(selection, noeud),
            Chemin: SurLeChemin(noeud, selection),
            Depliable: affiches.Count > 0,
            Ouvert: ouvert,
            Renomme: ReferenceEquals(renomme, noeud),
            Compte: visibles.Count > 0 && noeud.NbPieces > 0 ? noeud.NbPieces : null,
            Enfants: enfants,
            Vide: ouvert && visibles.Count == 0 && string.IsNullOrWhiteSpace(filtre)
                  && !noeud.EstPiece && !noeud.EstFiche);
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
