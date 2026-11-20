namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Demander le numero suivant d'une serie (<see cref="RoutesCompteur.Numeros"/>) ; les memes
/// parametres que GENERER_NUMERO de la V2.
/// </summary>
/// <param name="Type">Le type de numero (NMMTR_TYPE_METIER : « RGB », « JPC », « JGDJ »...).</param>
/// <param name="Annee">L'annee du compteur ; 0 = l'annee en cours.</param>
/// <param name="Jour">Le quantieme (1..366) d'une serie numerotee par jour ; 0 = aujourd'hui.</param>
/// <param name="CodeGreffe">Le code greffe ecrit a la place de « G » ; null = celui du compteur.</param>
public sealed record DemandeNumero(string Type, int Annee = 0, int Jour = 0, string? CodeGreffe = null);

/// <summary>La reponse a une <see cref="DemandeNumero"/>.</summary>
/// <param name="Code">Une valeur de <see cref="CodesNumerotation"/> : 0 = numero donne.</param>
/// <param name="Serie">La serie qui a servi (apres redirection, ex. ARRC -> ARRD) ; null si le type est inconnu.</param>
/// <param name="Numero">Le numero, ecrit au format de sa serie (ex. « 2026B00891 ») ; null si refus.</param>
/// <param name="Annee">L'annee du compteur.</param>
/// <param name="Jour">Le jour du compteur (serie par jour) ; 0 pour une serie par annee.</param>
/// <param name="Chrono">Le chrono donne ; null si refus.</param>
/// <param name="Reattribue">Vrai si c'est un numero rendu (NUM-004), redonne.</param>
/// <param name="Message">Le motif d'un refus ; null sinon.</param>
public sealed record NumeroAttribue(
    int Code,
    string? Serie,
    string? Numero,
    int Annee,
    int Jour,
    int? Chrono,
    bool Reattribue,
    string? Message);
