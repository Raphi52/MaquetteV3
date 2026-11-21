namespace RigV3.Contrats.Socle;

// Les versions de la V3 (lot 12 du plan de reconstruction, 2026-11-21 : SOC-009, SOC-010, SOC-012 ;
// mode d'emploi : docs/reconstruction/socle-exploitation.md). Le serveur les publie
// (Serveur/Socle/Versions/ModuleVersions.cs), le lanceur des serveurs RDS les lit (Lanceur/),
// l'outil de livraison les ecrit dans chaque base (Outils/Livraison).

/// <summary>
/// Les adresses des versions. Elles sont ANONYMES, comme /sante : le lanceur les appelle AVANT
/// que l'agent soit juge (un agent refuse doit pouvoir lire « version perimee » plutot qu'un 403),
/// et elles ne disent que des numeros de version.
/// </summary>
public static class RoutesVersions
{
    /// <summary>GET : la version de la partie serveur et son site (<see cref="VersionServeur"/>).</summary>
    public const string Serveur = "/socle/version";

    /// <summary>GET : la version du serveur et celle de la base V3 d'un greffe, et si elles concordent
    /// (<see cref="VersionsGreffe"/> ; 404 greffe inconnu, 503 base muette).</summary>
    public const string Greffe = "/socle/greffes/{greffe}/version";

    /// <summary>POST <see cref="ErreurDemarrage"/> : un ecran qui n'a pas pu demarrer le dit a l'exploitation
    /// (SOC-012 CA04, T04).</summary>
    public const string ErreursDemarrage = "/socle/version/erreurs-demarrage";

    /// <summary>L'adresse de <see cref="Greffe"/> pour un code greffe.</summary>
    public static string Pour(string greffe) => Greffe.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>La partie serveur qui repond.</summary>
/// <param name="Version">Sa version (<see cref="VersionV3"/>, sans le suffixe de compilation).</param>
/// <param name="Site">Le site ou elle tourne (configuration « Site:Nom » : Amitel, Antilles, Reunion,
/// Polynesie ; SOC-010) ; « developpement » sans configuration.</param>
public sealed record VersionServeur(string Version, string Site);

/// <summary>Les versions qu'un ecran doit croiser avant d'ouvrir un greffe (SOC-009 CA05).</summary>
/// <param name="Serveur">Version de la partie serveur.</param>
/// <param name="Base">Version livree dans la base V3 du greffe ; null si aucune livraison n'y est
/// enregistree (base de developpement ou creee hors de l'outil de livraison).</param>
/// <param name="Concordent">Vrai si la base porte la version du serveur, ou si elle n'en porte aucune.</param>
/// <param name="Message">Le refus a montrer quand elles ne concordent pas (les deux versions) ; null sinon.</param>
public sealed record VersionsGreffe(string Greffe, string Serveur, string? Base, bool Concordent, string? Message);

/// <summary>Ce qu'un ecran qui n'a pas pu demarrer remonte a l'exploitation (SOC-012 CA04).</summary>
/// <param name="Poste">Le serveur RDS (nom de machine).</param>
/// <param name="Compte">Le compte Windows de l'agent.</param>
/// <param name="Greffe">Le greffe vise, s'il est connu au lancement.</param>
/// <param name="VersionEcran">La version de l'ecran installee sur ce poste, si elle est connue.</param>
/// <param name="Cause">La cause, en clair (« composant WebView2 absent »).</param>
public sealed record ErreurDemarrage(string Poste, string Compte, string? Greffe, string? VersionEcran, string Cause);

/// <summary>
/// Les regles d'une version de la V3 : format annee.version.revision de la V2 (« 2026.5.5 »,
/// fiche team #681), comparaison numerique partie par partie (V2 : FORM_ACCUEIL._CompareVersion,
/// fiche #716 : « 2026.10.1 » est plus recent que « 2026.9.4 », ce qu'une comparaison de textes
/// rate), version majeure ou mineure pour l'evenement d'installation (SOC-009 CA06).
/// </summary>
public static class VersionV3
{
    /// <summary>Evenement « Installation d'une nouvelle version majeure du logiciel » (CODE_JET 250,
    /// RIG_DEV lu le 2026-11-21 : exigibilite X, non purgeable).</summary>
    public const short CodeMajeure = 250;

    /// <summary>Evenement « Installation d'une nouvelle version mineure du logiciel » (CODE_JET 255,
    /// RIG_DEV lu le 2026-11-21 : exigibilite C, purgeable).</summary>
    public const short CodeMineure = 255;

    /// <summary>Libelles de CODE_JET, recopies tels quels de RIG_DEV (2026-11-21).</summary>
    public const string LibelleMajeure = "Installation d'une nouvelle version majeure du logiciel";
    public const string LibelleMineure = "Installation d'une nouvelle version mineure du logiciel";

    /// <summary>
    /// La version sans ce que la compilation ajoute : « 2026.1.0+6b34db4… » (InformationalVersion du SDK
    /// .NET, qui y colle le commit) -> « 2026.1.0 » ; « 2026.1.0.0 » (version d'assemblage a 4 parties)
    /// -> « 2026.1.0 ». Le numero de reference est celui de Directory.Build.props, a trois parties.
    /// </summary>
    public static string Normaliser(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var v = version.Trim();
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        var parties = v.Split('.');
        if (parties.Length == 4 && parties[3] == "0") v = string.Join('.', parties[..3]);
        return v;
    }

    /// <summary>Comme la V2 (#716) : partie par partie, en nombres ; une partie absente vaut 0.
    /// Negatif si <paramref name="a"/> est plus ancienne que <paramref name="b"/>.</summary>
    public static int Comparer(string a, string b)
    {
        var pa = Parties(a);
        var pb = Parties(b);
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    /// <summary>Les deux numeros designent-ils la meme version ?</summary>
    public static bool Egales(string a, string b) => Comparer(a, b) == 0;

    /// <summary>
    /// Passer de <paramref name="avant"/> a <paramref name="apres"/> est-il une version MAJEURE
    /// (annee ou numero de version qui change, ou premiere version de la base) ? Sinon, seule la
    /// revision change : version mineure. Regle retenue « a valider » (la V2 n'ecrit pas ces
    /// evenements elle-meme : CODE_JET dit seulement « cree par le programme d'installation »).
    /// </summary>
    public static bool EstMajeure(string? avant, string apres)
    {
        if (string.IsNullOrWhiteSpace(avant)) return true;
        var pa = Parties(avant);
        var pb = Parties(apres);
        return pa.ElementAtOrDefault(0) != pb.ElementAtOrDefault(0) || pa.ElementAtOrDefault(1) != pb.ElementAtOrDefault(1);
    }

    /// <summary>Le refus d'une partie serveur ou d'un ecran face a une base d'une autre version (SOC-009 CA05) :
    /// il nomme les deux versions et ce qu'il faut faire, sans erreur de base obscure.</summary>
    public static string MessageBase(string piece, string versionPiece, string nomBase, string versionBase)
        => Comparer(versionPiece, versionBase) < 0
            ? $"{piece} est en version {versionPiece}, la base {nomBase} en version {versionBase} : " +
              $"{piece} est plus ancienne que la base. L'exploitation doit installer {piece} en version {versionBase}."
            : $"{piece} est en version {versionPiece}, la base {nomBase} en version {versionBase} : " +
              $"la base n'a pas encore recu cette version. L'exploitation doit la livrer (Outils\\Livraison) avant d'ouvrir ce greffe.";

    private static int[] Parties(string version)
        => [.. Normaliser(version).Split('.').Select(s => int.TryParse(s, out var n) ? n : 0)];
}
