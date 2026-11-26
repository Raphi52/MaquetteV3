namespace RigV3.Contrats.EcransGeneres;

// Les lignes d'un ECRAN GENERE lues dans la base du greffe (plan « ecrans retrouves branches sur la base », lot 1,
// 24/11/2026). L'ecran est decrit par un descripteur (Pont/EcransGeneres/) ; ses lignes viennent du lecteur de SA
// procedure, cote serveur (Serveur/Socle/EcransGeneres/, un fichier par procedure : mode d'emploi en tete de
// LecteurEcran.cs). L'ecran les recoit par Pont/EcranGenere.cs.
//
// Depuis le socle fonctionnel (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) la meme adresse CHERCHE par criteres
// (texte et criteres de la fiche RIG v2, en parametres d'adresse), une fiche se lit en entier (FicheEcran.cs) et les
// gestes s'ecrivent (GesteEcran.cs).

/// <summary>
/// Les adresses d'un ecran genere. {greffe} = le code du greffe ouvert : l'agent y est juge dans la base V3 de CE
/// greffe, qui est aussi la base lue et ecrite (401 sans session, 403 compte refuse, 404 greffe inconnu, 503 base muette,
/// sans repli). {procedure} = le code de la procedure du menu (« PROC_DCA »).
/// </summary>
public static class RoutesEcransGeneres
{
    /// <summary>GET : les lignes de la procedure (<see cref="LignesEcran"/>). Parametres d'adresse facultatifs :
    /// <see cref="ParamTexte"/> (le champ de recherche) et <see cref="ParamCritere"/>, repete, « Libelle=valeur » (un critere
    /// de la fiche RIG v2, au libelle du descripteur).</summary>
    public const string Lignes = "/ecrans-generes/{greffe}/{procedure}/lignes";

    /// <summary>GET : UNE fiche de la procedure (<see cref="FicheEcran"/>), {id} = <see cref="LigneLue.Id"/>. 404 : la
    /// procedure ne lit pas de fiche, ou rien ne porte cet identifiant.</summary>
    public const string Fiche = "/ecrans-generes/{greffe}/{procedure}/fiches/{id}";

    /// <summary>POST <see cref="GesteEcran"/> : un geste, ecrit dans la base du greffe en une transaction
    /// (<see cref="ResultatGeste"/>). 200 fait ou sans ecrivain (Demonstration), 422 refuse par un controle de RIG v2
    /// (rien n'est ecrit).</summary>
    public const string Gestes = "/ecrans-generes/{greffe}/{procedure}/gestes";

    /// <summary>Le nom du parametre d'adresse du champ de recherche.</summary>
    public const string ParamTexte = "texte";

    /// <summary>Le nom du parametre d'adresse d'un critere, repete : « Libelle=valeur ».</summary>
    public const string ParamCritere = "critere";

    /// <summary>L'adresse des lignes du greffe <paramref name="greffe"/> et de la procedure <paramref name="procedure"/>,
    /// cherchees par <paramref name="recherche"/> s'il y en a une.</summary>
    public static string Pour(string greffe, string procedure, RechercheEcran? recherche = null)
    {
        var adresse = Remplir(Lignes, greffe, procedure);
        if (recherche is null || recherche.Vide) return adresse;
        var parametres = new List<string>();
        if (!string.IsNullOrWhiteSpace(recherche.Texte)) parametres.Add($"{ParamTexte}={Uri.EscapeDataString(recherche.Texte.Trim())}");
        foreach (var (libelle, valeur) in recherche.Criteres.Where(c => !string.IsNullOrWhiteSpace(c.Value)).OrderBy(c => c.Key, StringComparer.Ordinal))
            parametres.Add($"{ParamCritere}={Uri.EscapeDataString($"{libelle}={valeur.Trim()}")}");
        return $"{adresse}?{string.Join("&", parametres)}";
    }

    /// <summary>L'adresse de la fiche <paramref name="id"/>.</summary>
    public static string FichePour(string greffe, string procedure, string id)
        => Remplir(Fiche, greffe, procedure).Replace("{id}", Uri.EscapeDataString(id));

    /// <summary>L'adresse des gestes de la procedure.</summary>
    public static string GestesPour(string greffe, string procedure) => Remplir(Gestes, greffe, procedure);

    private static string Remplir(string route, string greffe, string procedure)
        => route.Replace("{greffe}", Uri.EscapeDataString(greffe)).Replace("{procedure}", Uri.EscapeDataString(procedure));
}

/// <summary>Une recherche : le texte du champ de recherche et les criteres remplis (au libelle du descripteur).</summary>
public sealed record RechercheEcran(string? Texte, IReadOnlyDictionary<string, string> Criteres)
{
    /// <summary>Aucun texte, aucun critere rempli : la liste de travail sans filtre.</summary>
    public bool Vide => string.IsNullOrWhiteSpace(Texte) && Criteres.Values.All(string.IsNullOrWhiteSpace);

    /// <summary>La valeur du critere <paramref name="libelle"/>, sans blancs ; null s'il n'est pas rempli.</summary>
    public string? Critere(string libelle)
        => Criteres.TryGetValue(libelle, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    /// <summary>La recherche des parametres d'adresse (<see cref="RoutesEcransGeneres.ParamTexte"/>,
    /// <see cref="RoutesEcransGeneres.ParamCritere"/> « Libelle=valeur »). Un critere sans « = » est ignore.</summary>
    public static RechercheEcran Lire(string? texte, IEnumerable<string>? criteres)
    {
        var parLibelle = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in criteres ?? [])
        {
            var i = c.IndexOf('=', StringComparison.Ordinal);
            if (i > 0) parLibelle[c[..i]] = c[(i + 1)..];
        }
        return new RechercheEcran(texte, parLibelle);
    }
}

/// <summary>Une ligne de l'ecran, telle que la mise en page l'affiche (les noms de LigneEcran du descripteur).</summary>
/// <param name="Nom">Ce qui la nomme : la denomination et le SIREN ou le numero de gestion.</param>
/// <param name="Precisions">Ce qui la distingue (exercice, dates, origine...).</param>
/// <param name="Groupe">Un des groupes du descripteur de la procedure, au mot pres.</param>
/// <param name="Etat">Un des etats du descripteur (AFaire, EnCours, Validee, EnAttente, Rejetee...).</param>
/// <param name="Faites">Les rangs des marches deja franchies (a partir de 0), selon la regle de l'etat.</param>
public sealed record LigneLue(string Nom, string Precisions, string Groupe, string Etat, int[] Faites)
{
    /// <summary>L'identifiant de l'objet de la ligne dans la base du greffe (l'id V2 : « 4711 ») : la cle de sa fiche
    /// (<see cref="RoutesEcransGeneres.Fiche"/>) et des gestes qui le visent. Null : la ligne ne s'ouvre pas et ne porte
    /// aucun geste ecrit (un lecteur d'avant le socle fonctionnel).</summary>
    public string? Id { get; init; }

    /// <summary>Les cellules, dans l'ordre des colonnes du descripteur (Referentiel, Etat) ; vide sinon.</summary>
    public string[] Valeurs { get; init; } = [];
}

/// <summary>La reponse de <see cref="RoutesEcransGeneres.Lignes"/>.</summary>
/// <param name="Procedure">Le code demande.</param>
/// <param name="Branchee">Faux : aucun lecteur pour cette procedure (l'ecran garde ses lignes d'exemple).</param>
/// <param name="Lignes">Au plus <see cref="Plafond"/> lignes, dans l'ordre d'affichage.</param>
/// <param name="Total">Le nombre de lignes de la base qui repondent aux filtres (au-dela du plafond compris).</param>
/// <param name="Plafond">Le nombre maximal de lignes rendues.</param>
public sealed record LignesEcran(string Procedure, bool Branchee, LigneLue[] Lignes, int Total, int Plafond)
{
    /// <summary>Vrai : la recherche demandee a ete appliquee DANS la base (le lecteur cherche par criteres) ; l'ecran ne
    /// refiltre pas ces lignes. Faux : lignes sans recherche, l'ecran filtre ce qu'il a.</summary>
    public bool Cherchee { get; init; }

    /// <summary>Vrai : le lecteur de la procedure sait chercher dans la base (IRechercheEcran) ; l'ecran y envoie sa
    /// recherche (<see cref="RoutesEcransGeneres.ParamTexte"/>, <see cref="RoutesEcransGeneres.ParamCritere"/>).</summary>
    public bool Chercheur { get; init; }
}
