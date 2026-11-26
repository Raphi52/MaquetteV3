namespace RigV3.Contrats.EcransGeneres;

// UNE FICHE d'un ecran genere, lue dans la base du greffe (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026) :
// l'objet d'une ligne (LigneLue.Id) avec la valeur de chacun de ses champs, section par section. Les sections et les
// champs portent les TITRES ET LIBELLES DU DESCRIPTEUR de la procedure (Pont/EcransGeneres/), au mot pres : l'ecran y
// prend le type du champ (date, liste, case...), la fiche n'apporte que la valeur et le droit de la changer. Le banc
// tests/EcransGeneresBaseCheck le verifie pour tout lecteur de fiche.
//
// Format des valeurs, le meme pour lire et pour ecrire (GesteEcran.Valeurs) : une date « 2026-03-31 » (celui d'un
// champ date de l'ecran), une case « oui » ou « », un nombre ou un montant en ecriture invariante (« 180.00 »), une
// liste le texte de l'option, un texte tel quel. Une sous-liste (Tableau) se lit en colonnes et lignes.

/// <summary>La reponse de <see cref="RoutesEcransGeneres.Fiche"/>.</summary>
/// <param name="Procedure">Le code de la procedure.</param>
/// <param name="Id">L'identifiant demande (<see cref="LigneLue.Id"/>).</param>
/// <param name="Nom">Ce qui nomme l'objet (le nom de sa ligne).</param>
/// <param name="Sections">Les sections lues, aux titres du descripteur ; une section absente n'est pas lue par la V3.</param>
public sealed record FicheEcran(string Procedure, string Id, string Nom, SectionFiche[] Sections)
{
    /// <summary>Vrai : la fiche s'ouvre en lecture seule, aucun geste ne l'ecrit ; <see cref="Motif"/> dit pourquoi
    /// (l'objet verrouille par un autre utilisateur, au message de RIG v2).</summary>
    public bool LectureSeule { get; init; }

    /// <summary>Pourquoi la fiche est en lecture seule ; null sinon.</summary>
    public string? Motif { get; init; }
}

/// <summary>Une section de la fiche : son titre (celui du descripteur) et ses champs lus.</summary>
public sealed record SectionFiche(string Titre, ChampFiche[] Champs);

/// <summary>Un champ lu : son libelle (celui du descripteur) et sa valeur dans la base.</summary>
public sealed record ChampFiche(string Libelle, string Valeur)
{
    /// <summary>Vrai : l'ecrivain de la procedure sait l'enregistrer (geste Enregistrer, cle « Section › Libelle »).
    /// Faux : lu seulement.</summary>
    public bool Modifiable { get; init; }

    /// <summary>Une sous-liste (Tableau) : les titres de ses colonnes.</summary>
    public string[] Colonnes { get; init; } = [];

    /// <summary>Une sous-liste (Tableau) : ses lignes, une cellule par colonne.</summary>
    public string[][] Lignes { get; init; } = [];
}
