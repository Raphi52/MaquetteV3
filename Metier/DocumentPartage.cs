namespace RigV3Desktop;

/// <summary>Ce qu'une fenetre detachee affiche : sa piece et ce que le
/// formulaire a renseigne dessus. Un emplacement par fenetre ouverte.</summary>
public sealed class DocumentDetache
{
    public NoeudGed? Piece { get; internal set; }

    public IReadOnlyList<(string Libelle, string Valeur)> Valeurs { get; internal set; }
        = new List<(string, string)>();

    public event Action? Change;

    internal void Prevenir() => Change?.Invoke();
}

/// <summary>
/// Passe-plat entre la vue de l'affaire et les FENETRES DETACHEES du document.
///
/// Chaque fenetre porte son propre arbre de composants Blazor : elles ne
/// partagent AUCUN etat de page. Ce service, enregistre une seule fois pour
/// l'application, garde UN EMPLACEMENT PAR FENETRE (clef = rang d'ouverture :
/// 1re fenetre, 2e...) afin que PLUSIEURS documents puissent etre sortis en
/// meme temps -- avant le 14/09/2026 il n'y avait qu'une piece pour tout le
/// monde, donc N fenetres montraient N fois le meme document.
/// </summary>
public sealed class DocumentPartage
{
    private readonly Dictionary<int, DocumentDetache> _emplacements = new();

    /// <summary>L'emplacement de la fenetre de ce rang, cree au besoin.</summary>
    public DocumentDetache Emplacement(int rang)
    {
        if (!_emplacements.TryGetValue(rang, out var e))
        {
            e = new DocumentDetache();
            _emplacements[rang] = e;
        }
        return e;
    }

    /// <summary>Le plus petit rang libre : la 1re fenetre fermee est reprise
    /// avant d'en ouvrir une de plus, sinon la position memorisee du rang 1
    /// ne resservirait jamais.</summary>
    public int RangLibre(IEnumerable<int> occupes)
    {
        var pris = new HashSet<int>(occupes);
        var rang = 1;
        while (pris.Contains(rang)) rang++;
        return rang;
    }

    public void Publier(int rang, NoeudGed? piece, IReadOnlyList<(string Libelle, string Valeur)> valeurs)
    {
        var e = Emplacement(rang);
        e.Piece = piece;
        e.Valeurs = valeurs;
        e.Prevenir();
    }

    public void Liberer(int rang) => _emplacements.Remove(rang);

    /// <summary>
    /// Le contenu d'une page de l'apercu. La maquette n'a pas de fichier PDF :
    /// le texte de demonstration de la piece est decoupe en pages. Vit ICI
    /// parce que DEUX vues l'affichent : la 3e zone de VDossier et la fenetre
    /// detachee -- une seconde copie aurait derive.
    /// </summary>
    public static string TextePage(NoeudGed n, int feuille)
    {
        if (n.Apercu.Length == 0)
            return n.Libelle + "\n\n(page " + feuille + " - document numerise, " + n.Origine + ")";
        var lignes = n.Apercu.Replace("\r\n", "\n").Split('\n');
        var debut = (feuille - 1) * LignesParPage;
        if (debut >= lignes.Length)
            return "(page " + feuille + " - suite du document)";
        return string.Join("\n", lignes.Skip(debut).Take(LignesParPage));
    }

    /// <summary>Les lignes d'une page de l'apercu texte.</summary>
    public const int LignesParPage = 18;

    /// <summary>Les pages qu'un texte d'apercu occupe : celles que <see cref="TextePage"/> remplit.</summary>
    public static int PagesDuTexte(string apercu)
        => Math.Max(1, (apercu.Replace("\r\n", "\n").Split('\n').Length + LignesParPage - 1) / LignesParPage);
}
