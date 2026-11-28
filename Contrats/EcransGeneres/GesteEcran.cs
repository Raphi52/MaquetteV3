using System.Text.Json.Serialization;

namespace RigV3.Contrats.EcransGeneres;

// LES GESTES d'un ecran genere (plan « RIG v3 100 % fonctionnel », lot 2, 26/11/2026). Un geste part de l'ecran (une
// forme, web/src/ecrans/formes/), passe par le pont (Pont/EcranGenere.cs) et arrive a l'ecrivain de SA procedure, cote
// serveur (Serveur/Socle/EcransGeneres/Ecrivain<Sujet>.cs, mode d'emploi en tete d'EcrivainEcran.cs), qui l'ecrit dans
// la base V3 du greffe en une transaction, apres les controles de RIG v2. Refuse : rien n'est ecrit, le motif est le
// message de RIG v2. Sans ecrivain : rien n'est ecrit, et la reponse le dit (« Demonstration »).
// fix-ok: mesure (26/11/2026) -- la grille d'un Referentiel lit maintenant la fiche de sa ligne ; elle n'envoie a Modifier
// que les colonnes changees (FormeReferentiel.tsx, test « Modifier n'envoie que la colonne changee ») : documente ici.

/// <summary>Le type d'un geste : un par forme d'ecran qui ecrit.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TypeGeste>))]
public enum TypeGeste
{
    /// <summary>Fiche : enregistrer les champs modifies d'UN objet (Ids = [son id]).</summary>
    Enregistrer = 1,
    /// <summary>Saisie : creer un objet (Ids vide), les champs de ses etapes.</summary>
    Creer,
    /// <summary>Referentiel : ajouter une ligne a la table (Ids vide), ses cellules par colonne.</summary>
    Ajouter,
    /// <summary>Referentiel : modifier une ligne (Ids = [son id]), ses cellules CHANGEES par colonne (une colonne absente
    /// garde la valeur de la base, comme <see cref="Enregistrer"/>).</summary>
    Modifier,
    /// <summary>Referentiel : supprimer une ligne (Ids = [son id]).</summary>
    Supprimer,
    /// <summary>File : un geste nomme (<see cref="GesteEcran.Nom"/>) sur les lignes cochees (Ids).</summary>
    Agir,
    /// <summary>Operation : confirmer le geste sur les objets choisis (Ids), les champs de ses sections.</summary>
    Confirmer,
    /// <summary>Audience : une decision nommee (<see cref="GesteEcran.Nom"/>) sur les affaires cochees (Ids), les
    /// parametres de l'audience (date, chambre) dans Valeurs.</summary>
    Decider,
}

/// <summary>Un geste demande (corps de <see cref="RoutesEcransGeneres.Gestes"/>).</summary>
/// <param name="Type">Ce que la forme fait.</param>
/// <param name="Ids">Les objets vises (<see cref="LigneLue.Id"/>) ; vide pour une creation ou un ajout.</param>
/// <param name="Valeurs">Les valeurs SAISIES, au format de FicheEcran.cs : cle « Section › Libelle » pour un champ de
/// section (Fiche, Saisie, Operation), le libelle de la colonne (Referentiel) ou du parametre (Audience) ; une Operation
/// sur une liste y joint aussi les criteres remplis de sa recherche, sous leur libelle (lot 11 : un geste RIG v2 qui porte sur
/// toute la liste recherchee, les listes BODACC d'EcrivainPublicationsBodacc). Une Fiche
/// (Enregistrer) et un Referentiel (Modifier) n'envoient que les champs modifies : un champ absent garde la valeur de la
/// base.</param>
public sealed record GesteEcran(TypeGeste Type, string[] Ids, IReadOnlyDictionary<string, string> Valeurs)
{
    /// <summary>Le geste que la fiche RIG v2 nomme (« Relancer », « Renvoyer ») : Agir, Decider, Confirmer ; null sinon.</summary>
    public string? Nom { get; init; }

    /// <summary>La cle d'un champ de section dans <see cref="Valeurs"/> : « Section › Libelle » (celle de l'ecran,
    /// web/src/ecrans/formes/commun.tsx, cleChamp).</summary>
    public static string Cle(string section, string libelle) => $"{section} › {libelle}";
}

/// <summary>Ce que le geste a donne.</summary>
/// <param name="Fait">Vrai : ecrit dans la base du greffe (et trace au journal des gestes).</param>
/// <param name="Message">Ce que l'ecran dit : ce qui est fait, ou pourquoi rien ne l'est (le message de RIG v2).</param>
public sealed record ResultatGeste(bool Fait, string Message)
{
    /// <summary>Vrai : la procedure n'a pas d'ecrivain pour ce geste ; rien n'est ecrit (l'ecran dit « Demonstration »).</summary>
    public bool SansEcrivain { get; init; }

    /// <summary>Les objets ecrits (un objet cree porte ici son nouvel identifiant).</summary>
    public string[] Ids { get; init; } = [];

    /// <summary>Le fichier que le geste produit pour le POSTE, comme RIG v2 l'ecrit dans un dossier choisi (un export) ; l'ecran
    /// l'enregistre (web/src/ecrans/formes/commun.tsx, useGeste). Null : aucun fichier. Lot 16 du plan-f4fb097f (28/11/2026).</summary>
    public FichierGeste? Fichier { get; init; }
}

/// <summary>Un fichier rendu par un geste : son nom (celui de RIG v2) et son texte entier.</summary>
public sealed record FichierGeste(string Nom, string Contenu);
