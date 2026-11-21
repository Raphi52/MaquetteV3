using RigV3.Contrats.Socle;

namespace RigV3.Livraison;

/// <summary>
/// Les trois vagues de livraison de la V2 (SOC-009 CA02), reprises a l'identique :
/// D:\RigApplication\Source\RIG\EXE\Accueil\PROC_RigClientNewsRig\PROC_SAISIE_NEWS\OPE_LOT_LIVRAISON.cs:16-25
/// (libelles) et :148-166 (qui en fait partie), D:\RigApplication\Source\Outils\DLL\RigTools\TOOLS\RigGreffe.cs:1930-1998.
/// <list type="bullet">
/// <item>« Bêta-testeurs » : les greffes marques beta (GetGreffesBeta) ;</item>
/// <item>« Métropole (hors bêta-testeurs) » : ni beta ni outre-mer (production moins les deux autres) ;</item>
/// <item>« Outre-Mer » : les greffes marques outre-mer (GetGreffesOutreMer, Papeete compris).</item>
/// </list>
/// Comme la V2, un greffe a la fois beta et outre-mer serait dans DEUX vagues ; la seconde le trouve
/// deja a jour et n'y touche pas (Livreur). Le catalogue V3 ne contient que des greffes exploites
/// (reprise SOC-007) : le filtre IsExploit de la V2 y est deja applique.
/// </summary>
public enum Vague { Beta, Metropole, OutreMer }

public static class Vagues
{
    /// <summary>Le code d'une vague en ligne de commande et dans le journal des livraisons.</summary>
    public static string Code(Vague v) => v switch
    {
        Vague.Beta => "beta",
        Vague.Metropole => "metropole",
        Vague.OutreMer => "outremer",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>Le libelle de la V2 (eGreffesToUpdate).</summary>
    public static string Libelle(Vague v) => v switch
    {
        Vague.Beta => "Bêta-testeurs",
        Vague.Metropole => "Métropole (hors bêta-testeurs)",
        Vague.OutreMer => "Outre-Mer",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    /// <summary>« beta », « metropole », « outremer » (ou « outre-mer ») -> la vague ; leve sinon.</summary>
    public static Vague Lire(string code) => code.Trim().ToLowerInvariant().Replace("-", "") switch
    {
        "beta" => Vague.Beta,
        "metropole" => Vague.Metropole,
        "outremer" => Vague.OutreMer,
        _ => throw new ArgumentException($"Vague inconnue « {code} » : beta, metropole ou outremer."),
    };

    /// <summary>Le greffe est-il de la vague ?</summary>
    public static bool Contient(Vague v, FicheGreffe g) => v switch
    {
        Vague.Beta => g.EstBeta,
        Vague.OutreMer => g.EstOutreMer,
        Vague.Metropole => !g.EstBeta && !g.EstOutreMer,
        _ => false,
    };

    /// <summary>Les greffes de la vague, par code (comme la V2 : OrderBy CodeGreffeReel).</summary>
    public static IReadOnlyList<FicheGreffe> Greffes(Vague v, IEnumerable<FicheGreffe> catalogue)
        => [.. catalogue.Where(g => Contient(v, g)).OrderBy(g => g.Code, StringComparer.Ordinal)];
}
