using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// La route d'un GESTE d'écran fait main, commune aux modules Module&lt;Ecran&gt;.cs (plan « RIG v3 complet, mesuré par le banc du
/// parcours complet », lot 7, 28/11/2026) : un type de geste que l'écriture ne connaît pas = « Démonstration », rien d'écrit ; sinon
/// l'agent est identifié dans la base du greffe (403 inconnu), et le geste est joué dans la transaction et le journal des gestes du
/// socle (EcrivainsEcrans.JouerAsync, sa version pour un écran fait main). Un refus de RIG v2 = 422, comme la route des gestes
/// générés (ModuleEcransGeneres) et celle des paramètres d'édition (ModuleEcransMain, RoutesMaquettesEdition.Parametre).
/// </summary>
internal static class GesteFaitMain
{
    /// <summary>Publie POST <paramref name="route"/> : le geste <paramref name="jouer"/> de l'écran <paramref name="ecran"/>.</summary>
    internal static void Publier(IEndpointRouteBuilder routes, string route, string ecran, string auteur, IReadOnlyCollection<TypeGeste> gestes,
                                 Func<GesteEnCours, CancellationToken, Task<IssueGeste>> jouer)
        => routes.MapPost(route, (string greffe, GesteEcran geste, HttpContext http, CatalogueGreffes c, ServiceDroits droits, BaseRig commune,
                                  DistributeurNumeros numeros, CancellationToken ct)
            => !gestes.Contains(geste.Type)
                ? Task.FromResult(Results.Ok(new ResultatGeste(false, $"{ecran} n'écrit pas le geste {geste.Type} : rien n'est enregistré.") { SansEcrivain = true }))
                : ModuleEcransMain.SurLaBase(c, greffe, ct, async cn =>
                {
                    if (await droits.AppelantAsync(http, commune, cn, ct) is not { } moi)
                        return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                    var resultat = await EcrivainsEcrans.JouerAsync(ecran, auteur, jouer, cn, geste, moi, DateTime.Now, numeros, ct);
                    return resultat.Fait ? Results.Ok(resultat) : Results.Json(resultat, statusCode: StatusCodes.Status422UnprocessableEntity);
                }))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
}
