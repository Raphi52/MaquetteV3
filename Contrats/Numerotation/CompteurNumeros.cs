namespace RigV3.Contrats.Numerotation;

/// <summary>
/// Un compteur de la base V3 (<see cref="RoutesCompteur.Compteurs"/>) : l'equivalent d'une ligne
/// « compteur » de NUMERO_METIER de la V2 (NUM-001 CA01).
/// </summary>
/// <param name="Type">NMMTR_TYPE_METIER.</param>
/// <param name="Annee">NMMTR_ANNEE.</param>
/// <param name="Jour">Le quantieme d'une serie numerotee par jour (« Q » dans sa structure) ; 0 sinon.</param>
/// <param name="Libelle">NMMTR_LIBELLE.</param>
/// <param name="Structure">Le format : A annee, L lettre, S separateur, C chrono, Q jour, G code greffe, K « * ».</param>
/// <param name="CodageAnnee">Chiffres de l'annee dans le numero : 0, 1, 2 ou 4.</param>
/// <param name="Lettre">NMMTR_LETTRE_CATEGORIE.</param>
/// <param name="Separateur">NMMTR_SEPARATEUR.</param>
/// <param name="TailleChrono">Chiffres du chrono (complete de zeros).</param>
/// <param name="CodeGreffe">NMMTR_CODE_GREFFE.</param>
/// <param name="Debut">La valeur de depart (NMMTR_CHRONO_DEBUT).</param>
/// <param name="Chrono">Le dernier chrono donne ; null = aucun encore.</param>
/// <param name="GestionRendus">Le reglage de restitution (NMMTR_GESTION_NUM_ERR) : un numero rendu est-il redonne ?</param>
/// <param name="Redirection">Le type qui sert a sa place (NMMTR_FILE_REDIRECTION) ; null sinon.</param>
/// <param name="TableCible">La table ou le numero est range (NMMTR_TABLE_AFFECTEE).</param>
/// <param name="ColonneCible">La colonne ou le numero est range (NMMTR_ATTRIBUT_AFFECTE).</param>
/// <param name="DerniereAttribution">Quand le dernier numero a ete donne.</param>
/// <param name="NumerosRendus">Combien de numeros rendus attendent d'etre redonnes.</param>
public sealed record CompteurNumeros(
    string Type,
    int Annee,
    int Jour,
    string? Libelle,
    string Structure,
    int CodageAnnee,
    string? Lettre,
    string? Separateur,
    int TailleChrono,
    string? CodeGreffe,
    int Debut,
    int? Chrono,
    bool GestionRendus,
    string? Redirection,
    string? TableCible,
    string? ColonneCible,
    DateTime? DerniereAttribution,
    int NumerosRendus);
