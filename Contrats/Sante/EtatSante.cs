namespace RigV3.Contrats.Sante;

/// <summary>Les adresses du theme « sante » : le serveur les publie, l'ecran les appelle.</summary>
public static class RoutesSante
{
    /// <summary>GET : le serveur repond-il, et sa base aussi ?</summary>
    public const string Sante = "/sante";
}

/// <summary>Les valeurs possibles de <see cref="EtatSante.Etat"/>.</summary>
public static class EtatsSante
{
    /// <summary>Le serveur repond et sa base aussi (HTTP 200).</summary>
    public const string Ok = "ok";

    /// <summary>Aucune chaine de connexion n'est configuree (HTTP 503).</summary>
    public const string BaseNonConfiguree = "base non configuree";

    /// <summary>La base est configuree mais ne repond pas (HTTP 503).</summary>
    public const string BaseInjoignable = "base injoignable";
}

/// <summary>
/// La reponse de <see cref="RoutesSante.Sante"/>. Elle dit ce qu'un exploitant
/// demande en premier : le serveur vit-il, sur quelle base, et ou en sont les
/// migrations de cette base.
/// </summary>
/// <param name="Etat">Une valeur de <see cref="EtatsSante"/>.</param>
/// <param name="Version">Version du serveur (celle de son assemblage).</param>
/// <param name="Base">Nom de la base jointe ; null si elle n'est pas jointe.</param>
/// <param name="MigrationsJouees">Nombre de migrations inscrites au journal de la base.</param>
/// <param name="DerniereMigration">Identifiant de la derniere migration jouee ; null s'il n'y en a aucune.</param>
/// <param name="Detail">Cause d'un etat autre que <see cref="EtatsSante.Ok"/> ; null sinon.</param>
public sealed record EtatSante(
    string Etat,
    string Version,
    string? Base,
    int MigrationsJouees,
    string? DerniereMigration,
    string? Detail);
