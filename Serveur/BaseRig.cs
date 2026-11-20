using Microsoft.Data.SqlClient;

namespace RigV3.Serveur;

/// <summary>
/// La base V3 que sert ce serveur. Sa chaine vient de la configuration
/// « ConnectionStrings:RigV3 » (variable d'environnement ConnectionStrings__RigV3,
/// appsettings.json ou ligne de commande). Une route la recoit en parametre :
/// <c>async (BaseRig b, CancellationToken ct) => { await using var cn = await b.OuvrirAsync(ct); ... }</c>.
/// </summary>
public sealed class BaseRig(string? connexion)
{
    /// <summary>Nom de la chaine dans la configuration.</summary>
    public const string NomConnexion = "RigV3";

    /// <summary>La chaine de connexion ; null si le serveur n'en a pas recu.</summary>
    public string? Connexion { get; } = string.IsNullOrWhiteSpace(connexion) ? null : connexion;

    /// <summary>Ouvre une connexion a la base (une par requete : le pool de SqlClient les recycle).</summary>
    /// <exception cref="InvalidOperationException">Aucune chaine n'est configuree.</exception>
    public async Task<SqlConnection> OuvrirAsync(CancellationToken ct = default)
    {
        if (Connexion is null)
            throw new InvalidOperationException(
                $"Base non configuree : poser ConnectionStrings:{NomConnexion} (variable ConnectionStrings__{NomConnexion}).");
        var cn = new SqlConnection(Connexion);
        try
        {
            await cn.OpenAsync(ct);
            return cn;
        }
        catch
        {
            await cn.DisposeAsync();
            throw;
        }
    }
}
