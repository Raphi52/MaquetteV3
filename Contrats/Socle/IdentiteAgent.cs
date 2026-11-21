namespace RigV3.Contrats.Socle;

/// <summary>Les adresses de l'identite de l'agent (SOC-001) : le serveur les publie, l'ecran les appelle.</summary>
public static class RoutesIdentite
{
    /// <summary>GET : qui est l'appelant (sa session Windows) et peut-il ouvrir RIG ?
    /// 200 avec une <see cref="IdentiteAgent"/> admise ou refusee ; 401 si aucune session n'est transmise.
    /// Sans greffe, le compte est cherche dans la base commune (routes communes, comme le catalogue) ;
    /// pour ouvrir un greffe : <see cref="IdentiteDuGreffe"/>.</summary>
    public const string Identite = "/socle/identite";

    /// <summary>GET : la meme question pour le greffe <c>{greffe}</c> (son code au catalogue) : le compte est
    /// cherche dans la base V3 de CE greffe, comme la V2 (Utilisateur.GetUtilisateurCourant(CodeGreffeRig)).
    /// 404 si le greffe n'est pas au catalogue, 503 si sa base ne repond pas. Adresse : <see cref="Pour"/>.</summary>
    public const string IdentiteDuGreffe = "/socle/identite/{greffe}";

    /// <summary>L'adresse de <see cref="IdentiteDuGreffe"/> pour le greffe <paramref name="greffe"/>.</summary>
    public static string Pour(string greffe) => IdentiteDuGreffe.Replace("{greffe}", Uri.EscapeDataString(greffe));

    /// <summary>
    /// En-tete qui DESIGNE UN AUTRE COMPTE que la session Windows, le pendant du parametre
    /// « utilisateur= » de la V2 (RigConsoleAccueil.cs:76-77). Le serveur ne l'ecoute qu'en
    /// developpement et en recette ; en production il l'ignore (SOC-001 CA07).
    /// </summary>
    public const string EnTeteCompte = "X-RigV3-Compte";
}

/// <summary>Les valeurs de <see cref="IdentiteAgent.Statut"/>, dans l'ordre des controles de la V2.</summary>
public static class StatutsIdentite
{
    /// <summary>Compte connu, avec au moins un role, actif et rattache a un service : l'accueil s'ouvre.</summary>
    public const string Admis = "admis";

    /// <summary>1er refus de la V2 : le compte Windows n'est pas declare dans la base du greffe (CA03) ;
    /// un agent declare seulement dans un autre greffe y est donc inconnu.</summary>
    public const string Inconnu = "inconnu";

    /// <summary>2e refus : le compte n'a aucun role (CA05 ; la V2 le prevoit sans le faire).</summary>
    public const string SansDroit = "sans-droit";

    /// <summary>3e refus : le compte est desactive (CA04).</summary>
    public const string Desactive = "desactive";

    /// <summary>4e refus : aucun service n'est rattache au compte (CA06).</summary>
    public const string SansService = "sans-service";
}

/// <summary>
/// La reponse de <see cref="RoutesIdentite.Identite"/>. Un compte refuse ne recoit QUE son compte
/// Windows, la raison et le contact de l'assistance : aucune donnee du greffe (CA03).
/// </summary>
/// <param name="Statut">Une valeur de <see cref="StatutsIdentite"/>.</param>
/// <param name="Compte">Le compte Windows de l'appelant, tel que sa session l'a transmis (sans domaine).</param>
/// <param name="Agent">L'agent reconnu ; null pour un refus.</param>
/// <param name="Message">La raison du refus, qui nomme le compte (textes de la V2) ; null si admis.</param>
/// <param name="Assistance">Vers qui se tourner ; null si admis.</param>
public sealed record IdentiteAgent(
    string Statut,
    string Compte,
    AgentReconnu? Agent,
    string? Message,
    string? Assistance)
{
    /// <summary>Vrai quand l'accueil peut s'ouvrir.</summary>
    public bool Admis => Statut == StatutsIdentite.Admis;
}

/// <summary>L'agent admis, tel que la table soc.Compte le decrit (aucun mot de passe : CA08).</summary>
/// <param name="CompteWindows">Le compte tel qu'il est enregistre (ses majuscules peuvent differer de la session : CA02).</param>
/// <param name="Service">Le nom du service de rattachement (son abrege s'il n'a pas de nom).</param>
public sealed record AgentReconnu(
    string CompteWindows,
    string Nom,
    string Prenom,
    string Abrege,
    string Civilite,
    string Service,
    bool EstAmitel,
    bool EstAdministrateur,
    bool EstAssistance)
{
    /// <summary>Ce que l'entete de l'accueil affiche : « Prenom Nom ».</summary>
    public string NomAffiche => $"{Prenom} {Nom}".Trim();
}

// fix-ok: pas un correctif -- lot 8 ajoute IdentiteDuGreffe et Pour (identite jugee dans la base du greffe), puis deux retouches de redaction du commentaire de StatutsIdentite.Inconnu.
