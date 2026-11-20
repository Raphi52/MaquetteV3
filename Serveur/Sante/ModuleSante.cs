using System.Reflection;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Sante;

namespace RigV3.Serveur.Sante;

/// <summary>
/// GET /sante : le serveur repond-il, et sa base aussi ? C'est la premiere
/// question d'un exploitant (et d'un repartiteur de charge) : 200 quand la base
/// repond, 503 sinon, avec la cause. Le module modele pour tous les autres :
/// un fichier, une classe, ses routes.
/// </summary>
internal sealed class ModuleSante : IModuleServeur
{
    private static readonly string Version =
        typeof(ModuleSante).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ModuleSante).Assembly.GetName().Version?.ToString() ?? "?";

    public void Brancher(IEndpointRouteBuilder routes)
        => routes.MapGet(RoutesSante.Sante, async (BaseRig b, CancellationToken ct) =>
        {
            if (b.Connexion is null)
                return Results.Json(new EtatSante(EtatsSante.BaseNonConfiguree, Version, null, 0, null,
                        $"poser ConnectionStrings:{BaseRig.NomConnexion}"),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            try
            {
                await using var cn = await b.OuvrirAsync(ct);
                var (nombre, derniere) = await Migrateur.EtatJournalAsync(cn, ct);
                return Results.Json(new EtatSante(EtatsSante.Ok, Version, cn.Database, nombre, derniere, null));
            }
            catch (SqlException e)
            {
                // Pas avalee : la cause part dans la reponse, que l'exploitant lit.
                return Results.Json(new EtatSante(EtatsSante.BaseInjoignable, Version, null, 0, null, e.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
}
