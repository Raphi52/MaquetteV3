namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran « Tableau des editions » (web/src/ecrans/EcranTableauEditions.tsx).
///
/// Les lignes d'edition arrivent par les proprietes de l'ile (parametre
/// Editions de la coquille) ; seule la liste des types de papier, lue par le
/// detail du destinataire dans ArbreDemo, passe par ici. Metier/ n'est pas
/// modifie.
/// </summary>
public static class EcranTableauEditions
{
    /// <summary>Les types de papier proposes dans le detail (ArbreDemo.TypesPapier), dans leur ordre.</summary>
    [JSInvokable("EcranTableauEditions.TypesPapier")]
    public static string[] TypesPapier() => ArbreDemo.TypesPapier;
}
