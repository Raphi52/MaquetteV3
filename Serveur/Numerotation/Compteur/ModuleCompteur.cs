using RigV3.Contrats.Numerotation;

namespace RigV3.Serveur.Numerotation.Compteur;

/// <summary>
/// Le compteur central des numeros (NUM-001, NUM-004, NUM-008) : le service
/// <see cref="DistributeurNumeros"/> pour les actes de la V3, et trois routes pour l'ecran
/// (liste des compteurs, numero suivant, restitution). Toutes passent par les programmes
/// num.Attribuer et num.Restituer de la base : aucun second chemin d'attribution.
/// </summary>
internal sealed class ModuleCompteur : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
        => services.AddSingleton(sp => new DistributeurNumeros(
            sp.GetRequiredService<IConfiguration>().GetValue(DistributeurNumeros.Reglage, DistributeurNumeros.DelaiParDefautMs)));

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesCompteur.Compteurs, async (string? type, BaseRig b, CancellationToken ct) =>
        {
            await using var cn = await b.OuvrirAsync(ct);
            return Results.Ok(await DistributeurNumeros.ListerAsync(cn, string.IsNullOrWhiteSpace(type) ? null : type.Trim(), ct));
        });

        routes.MapPost(RoutesCompteur.Numeros, async (DemandeNumero demande, HttpContext http, BaseRig b,
                                                      DistributeurNumeros numeros, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(demande.Type))
                return Results.BadRequest(new NumeroAttribue(CodesNumerotation.NonJouee, null, null, demande.Annee,
                    demande.Jour, null, false, "Le type de numero est obligatoire."));
            await using var cn = await b.OuvrirAsync(ct);
            try
            {
                var (compte, poste) = Demandeur(http);
                return Results.Ok(await numeros.AttribuerAsync(cn, null, demande with { Type = demande.Type.Trim() }, compte, poste, ct));
            }
            catch (CompteurOccupeException e)
            {
                return Results.Json(new NumeroAttribue(CodesNumerotation.CompteurOccupe, null, null, demande.Annee, demande.Jour,
                    null, false, e.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        routes.MapPost(RoutesCompteur.Restitutions, async (DemandeRestitution demande, HttpContext http, BaseRig b,
                                                           DistributeurNumeros numeros, CancellationToken ct) =>
        {
            // L'ecran de reattribution exige l'explication (NUM-004 T04) ; le programme, lui, accepte
            // une restitution sans explication venue de la V2 et le note (NUM-004 CA08).
            if (string.IsNullOrWhiteSpace(demande.Type) || string.IsNullOrWhiteSpace(demande.Numero)
                || string.IsNullOrWhiteSpace(demande.Explication))
                return Results.BadRequest(new ResultatRestitution(CodesNumerotation.NonJouee, null, null,
                    "Le type, le numero et l'explication sont obligatoires."));
            await using var cn = await b.OuvrirAsync(ct);
            try
            {
                var (compte, poste) = Demandeur(http);
                return Results.Ok(await numeros.RestituerAsync(cn, null, demande with { Type = demande.Type.Trim() }, compte, poste, ct));
            }
            catch (CompteurOccupeException e)
            {
                return Results.Json(new ResultatRestitution(CodesNumerotation.CompteurOccupe, null, null, e.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    /// <summary>
    /// Qui demande, pour le journal (NUM-001 CA06). Le compte vient de l'authentification du serveur
    /// (session Windows, lot 2) ; tant qu'elle n'est pas branchee, le journal l'ecrit « non identifie »
    /// plutot que le compte SQL du serveur, qui serait faux.
    /// </summary>
    private static (string Compte, string Poste) Demandeur(HttpContext http)
        => (http.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } nom } ? nom : "(non identifie)",
            http.Connection.RemoteIpAddress?.ToString() ?? "(poste inconnu)");
}
