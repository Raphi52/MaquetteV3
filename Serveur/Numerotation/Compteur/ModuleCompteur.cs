using Microsoft.Data.SqlClient;
using RigV3.Contrats.Numerotation;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Numerotation.Compteur;

/// <summary>
/// Le compteur central des numeros (NUM-001, NUM-004, NUM-008) : le service
/// <see cref="DistributeurNumeros"/> pour les actes de la V3, et trois routes pour l'ecran
/// (liste des compteurs, numero suivant, restitution). Toutes passent par les programmes
/// num.Attribuer et num.Restituer de la base : aucun second chemin d'attribution.
///
/// RACCORD (lot 8). Les trois routes portent le greffe dans leur adresse (<c>{greffe}</c>) et
/// <see cref="PolitiquesSocle.AgentAdmis"/>, posee une fois sur leur groupe : l'agent est juge dans
/// la base de CE greffe avant que la route ne tourne (401 sans session, 403 compte refuse dans ce
/// greffe, 404 greffe absent du catalogue, 503 base muette). La route ouvre ensuite la meme base par
/// le catalogue commun (<see cref="CatalogueGreffes.OuvrirBaseV3Async"/>) : aucune chaine de connexion
/// ici, aucun repli sur la base commune ni sur un autre greffe.
/// </summary>
internal sealed class ModuleCompteur : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
        => services.AddSingleton(sp => new DistributeurNumeros(
            sp.GetRequiredService<IConfiguration>().GetValue(DistributeurNumeros.Reglage, DistributeurNumeros.DelaiParDefautMs)));

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var compteur = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        compteur.MapGet(RoutesCompteur.Compteurs, async (string greffe, string? type, CatalogueGreffes c, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async cn =>
                Results.Ok(await DistributeurNumeros.ListerAsync(cn, string.IsNullOrWhiteSpace(type) ? null : type.Trim(), ct))));

        compteur.MapPost(RoutesCompteur.Numeros, async (string greffe, DemandeNumero demande, HttpContext http, CatalogueGreffes c,
                                                        DistributeurNumeros numeros, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(demande.Type))
                return Results.BadRequest(new NumeroAttribue(CodesNumerotation.NonJouee, null, null, demande.Annee,
                    demande.Jour, null, false, "Le type de numero est obligatoire."));
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
            {
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
        });

        compteur.MapPost(RoutesCompteur.Restitutions, async (string greffe, DemandeRestitution demande, HttpContext http, CatalogueGreffes c,
                                                             DistributeurNumeros numeros, ServiceDroits droits, BaseRig commune,
                                                             CancellationToken ct) =>
        {
            // L'ecran de reattribution exige l'explication (NUM-004 T04) ; le programme, lui, accepte
            // une restitution sans explication venue de la V2 et le note (NUM-004 CA08).
            if (string.IsNullOrWhiteSpace(demande.Type) || string.IsNullOrWhiteSpace(demande.Numero)
                || string.IsNullOrWhiteSpace(demande.Explication))
                return Results.BadRequest(new ResultatRestitution(CodesNumerotation.NonJouee, null, null,
                    "Le type, le numero et l'explication sont obligatoires."));
            // Reservee aux detenteurs de XNUMERO, comme la procedure de la V2 (NUM-004 T04, T08), jugee dans la
            // base de CE greffe (lot 9). Un refus ne touche a rien : ni compteur, ni numero rendu, ni journal.
            // Une panne de la verification est un refus (SOC-002 CA06, trace par ServiceDroits).
            var moi = await droits.AppelantAsync(http, commune, ct);
            var droit = moi is null ? null : await droits.VerifierAsync(http, commune, moi, RoutesCompteur.FonctionReattribution, ct);
            if (droit is not { Autorise: true })
                return Results.Json(new ResultatRestitution(CodesNumerotation.NonJouee, null, null,
                    $"Restitution refusee : le compte {moi?.CompteWindows ?? http.User.Identity?.Name} n'a pas la fonction " +
                    $"{RoutesCompteur.FonctionReattribution} (Reattribution de numero metier) dans le greffe {greffe}."),
                    statusCode: StatusCodes.Status403Forbidden);
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
            {
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
        });
    }

    /// <summary>
    /// Le travail d'une route sur la base V3 du greffe <paramref name="greffe"/>, ouverte par le catalogue.
    /// La politique vient de l'ouvrir pour juger l'agent ; si elle ne s'ouvre plus entre-temps, la reponse
    /// est la meme que celle de la politique (404 / 503), jamais une autre base.
    /// </summary>
    private static async Task<IResult> SurBaseDuGreffe(CatalogueGreffes c, string greffe, CancellationToken ct,
                                                       Func<SqlConnection, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await travail(cn);
    }

    /// <summary>
    /// Qui demande, pour le journal (NUM-001 CA06) : le compte de la session Windows que la politique
    /// <see cref="PolitiquesSocle.AgentAdmis"/> vient d'admettre (lot 2) ; la route ne tourne jamais sans lui.
    /// </summary>
    private static (string Compte, string Poste) Demandeur(HttpContext http)
        => (http.User.Identity?.Name ?? throw new InvalidOperationException("Route du compteur appelee sans session : la politique AgentAdmis manque."),
            http.Connection.RemoteIpAddress?.ToString() ?? "(poste inconnu)");
}
