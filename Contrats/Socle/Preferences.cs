namespace RigV3.Contrats.Socle;

/// <summary>
/// Les adresses des preferences de l'agent et de ses documents (SOC-013) : le serveur les publie
/// (Serveur/Socle/Preferences/ModulePreferences.cs), l'ecran les appelle (Pont/PreferencesServeur.cs).
///
/// Chaque adresse porte le greffe (<c>{greffe}</c>) : les preferences vivent dans la base V3 de CE greffe,
/// comme PARAMETRE_UTILISATEUR dans la base V2 du greffe. L'agent est celui de la session Windows (SOC-001) :
/// on ne lit et n'ecrit que SES preferences, on ne recoit que SES documents — aucune adresse ne nomme un autre
/// agent. 401 sans session, 403 compte refuse dans ce greffe, 404 greffe absent du catalogue, 503 base muette.
/// </summary>
public static class RoutesPreferences
{
    /// <summary>GET : les preferences de l'agent (<see cref="PreferencesAgent"/>) ; PUT <see cref="PreferencesAgent"/> :
    /// les remplace par celles envoyees (une preference absente de l'envoi n'est pas touchee).</summary>
    public const string Preferences = "/socle/{greffe}/preferences";

    /// <summary>POST <see cref="OuvertureSessionAgent"/> : la V3 de l'agent vient de s'ouvrir sur ce serveur RDS, dans
    /// cette session ; rend les documents qui l'attendaient (<see cref="DocumentRecu"/>[], CA04).</summary>
    public const string Session = "/socle/{greffe}/session";

    /// <summary>GET « ?serveur=…&amp;session=… » : les documents prepares pour l'agent depuis, s'il travaille bien dans
    /// cette session (<see cref="DocumentRecu"/>[], CA03) ; vide pour toute autre session.</summary>
    public const string Documents = "/socle/{greffe}/documents";

    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Les noms de preference que la V3 connait, et ce qu'ils portent (valeur en JSON).</summary>
public static class NomsPreferences
{
    /// <summary>Le theme d'affichage (code de ThemesRig : « clair », « sombre »...). V2 : ConsoleAccueil_Theme.</summary>
    public const string Theme = "Theme";

    /// <summary>L'imprimante par defaut de l'agent (CA05). V2 : UTILISATEUR.UTLST_IMPRIMANTE_DEFAUT.</summary>
    public const string Imprimante = "Imprimante";

    /// <summary>Les ecrans en favori, par code. V2 : ConsoleAccueil_Fav_NN (« P;ARRIVEE;ARRIVEE »).</summary>
    public const string VuesFavorites = "VuesFavorites";

    /// <summary>Les onglets ouverts, dans leur ordre, et l'onglet montre au demarrage. V2 : ConsoleAccueil_StartProcessus.</summary>
    public const string OngletsVue = "OngletsVue";
    public const string OngletActif = "OngletActif";
}

/// <summary>Les preferences d'un agent : nom -> valeur JSON, telle que l'ecran l'a ecrite.</summary>
/// <param name="Valeurs">Les preferences (<see cref="NomsPreferences"/> et les autres reglages de l'accueil).</param>
/// <param name="Serveur">Le serveur RDS qui les envoie (PUT) ; celui qui les a ecrites en dernier (GET), null s'il n'y en a pas.</param>
public sealed record PreferencesAgent(IReadOnlyDictionary<string, string> Valeurs, string? Serveur);

/// <summary>Ou la V3 de l'agent vient de s'ouvrir.</summary>
/// <param name="Serveur">Le serveur RDS (nom de la machine).</param>
/// <param name="Session">La session Windows sur ce serveur et l'instance de la V3 (« 3:7f1c… ») : deux V3 ouvertes
/// l'une apres l'autre dans la meme session sont deux sessions.</param>
public sealed record OuvertureSessionAgent(string Serveur, string Session);

/// <summary>Un document que la partie serveur a prepare pour l'agent (edition a afficher ou a relire).</summary>
public sealed record DocumentRecu(int Id, string Nature, string Titre, string Fichier, DateTime DeposeLe, string DeposePar);

/// <summary>Les natures d'un document pour l'agent (contrainte CK_DocumentAgent_Nature).</summary>
public static class NaturesDocument
{
    public const string Afficher = "afficher";
    public const string Relire = "relire";
}
