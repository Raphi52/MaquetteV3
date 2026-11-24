namespace RigV3Desktop.Pont.EcransGeneres;

// LE CATALOGUE DES ECRANS GENERIQUES : tous les domaines (IDomaineEcrans) range dans
// Pont/EcransGeneres/, trouves par reflexion. Ajouter un verbe = ajouter son fichier : ce
// catalogue ne se modifie pas. Une procedure decrite deux fois est une erreur de
// construction, levee au premier acces (on ne garde pas un doublon en silence).
public static class CatalogueGenere
{
    private static readonly Dictionary<string, DescripteurEcran> _parCode = Charger();

    /// <summary>Tous les descripteurs, dans l'ordre de decouverte.</summary>
    public static IReadOnlyCollection<DescripteurEcran> Tous => _parCode.Values;

    /// <summary>Le descripteur d'une procedure, ou nul : son ecran reste « a venir ».</summary>
    public static DescripteurEcran? Descripteur(string code) => _parCode.GetValueOrDefault(code);

    private static Dictionary<string, DescripteurEcran> Charger()
    {
        var parCode = new Dictionary<string, DescripteurEcran>();
        var domaines = typeof(CatalogueGenere).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, Namespace: "RigV3Desktop.Pont.EcransGeneres" }
                        && typeof(IDomaineEcrans).IsAssignableFrom(t));
        foreach (var type in domaines)
        {
            var domaine = (IDomaineEcrans)Activator.CreateInstance(type)!;
            foreach (var ecran in domaine.Ecrans())
            {
                if (!parCode.TryAdd(ecran.Code, ecran))
                    throw new InvalidOperationException($"{ecran.Code} est decrit deux fois (domaine {type.Name}).");
            }
        }
        return parCode;
    }
}
