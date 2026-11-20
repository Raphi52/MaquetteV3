namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Rendre un numero donne par erreur, pour qu'il soit redonne au prochain acte de sa serie
/// (NUM-004, <see cref="RoutesCompteur.Restitutions"/>) ; les parametres de RESTITUER_NUMERO de la V2.
/// </summary>
/// <param name="Type">Le type de numero (« JPC »...).</param>
/// <param name="Numero">Le numero tel qu'il a ete donne (« 2026RJ0012 »).</param>
/// <param name="Explication">Pourquoi il est rendu (obligatoire par cette route : l'ecran de
/// reattribution l'exige) ; garde en entier au journal, 50 caracteres sur la ligne de la V2.</param>
/// <param name="Annee">Seulement si le format de la serie ne porte pas l'annee ; 0 = l'annee en cours.
/// Si le format la porte, elle est lue dans le numero (NUM-004 CA07).</param>
public sealed record DemandeRestitution(string Type, string Numero, string? Explication, int Annee = 0);

/// <summary>Les issues d'une restitution (<see cref="ResultatRestitution.Issue"/>).</summary>
public static class IssuesRestitution
{
    /// <summary>Code 0 : il sera redonne au prochain demandeur de sa serie.</summary>
    public const string Rendu = "rendu";

    /// <summary>Code 0 : serie a restitution inactive ; rien n'est redonne (comme la V2), le trou est explique au journal.</summary>
    public const string NonRedonnable = "non redonnable";

    /// <summary>Code 0 : il attendait deja d'etre redonne ; la demande est journalisee, rien de plus.</summary>
    public const string DejaRendu = "deja rendu";

    /// <summary>Code -2 : type inconnu ; rien n'est ecrit.</summary>
    public const string TypeInconnu = "type inconnu";

    /// <summary>Code -3 : le numero ne suit pas le format de sa serie ; rien n'est ecrit.</summary>
    public const string Illisible = "illisible";

    /// <summary>Code -4 : jamais donne (au-dela du compteur) ; refus journalise.</summary>
    public const string JamaisDonne = "jamais donne";
}

/// <summary>La reponse a une <see cref="DemandeRestitution"/>.</summary>
/// <param name="Code">Une valeur de <see cref="CodesNumerotation"/> : 0 = rendu ou journalise, negatif = refus.</param>
/// <param name="Issue">Une valeur de <see cref="IssuesRestitution"/> ; null si le compteur etait occupe.</param>
/// <param name="Serie">La serie (apres redirection) ; null si le type est inconnu.</param>
/// <param name="Message">Ce qui s'est passe, en clair.</param>
public sealed record ResultatRestitution(int Code, string? Issue, string? Serie, string? Message);
