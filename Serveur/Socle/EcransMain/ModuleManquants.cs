using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Contrats.Numerotation;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Numéros manquants » (PROC_NUMEROS_MANQUANTS) : la route du geste « Éditer la liste » (EcritureManquants.cs), RÉSERVÉE à la fonction
/// LNUMCM comme la lecture (RoutesCompteur.Manquants, ModuleCompteur : 403 et le même motif sans elle). Le reste est la route d'un geste
/// fait main (GesteFaitMain.Publier, recopiée ici parce qu'elle n'a pas de contrôle de fonction : un type de geste inconnu = « Démonstration »,
/// l'agent identifié dans la base du greffe, le geste dans la transaction et le journal du socle, 422 = refus). Plan « RIG v3 complet, mesuré
/// par le banc du parcours complet », lot 11, 28/11/2026.
/// </summary>
internal sealed class ModuleManquants : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => routes.MapPost(RoutesManquants.Editer, async (string greffe, GesteEcran geste, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                         BaseRig commune, DistributeurNumeros numeros, CancellationToken ct) =>
            {
                if (!EcritureManquants.Gestes.Contains(geste.Type))
                    return Results.Ok(new ResultatGeste(false, $"{EcritureManquants.Ecran} n'écrit pas le geste {geste.Type} : rien n'est enregistré.")
                    { SansEcrivain = true });
                var appelant = await droits.AppelantAsync(http, commune, ct);
                var droit = appelant is null ? null : await droits.VerifierAsync(http, commune, appelant, RoutesCompteur.FonctionNumerosManquants, ct);
                if (droit is not { Autorise: true })
                    return Results.Json(new ResultatGeste(false,
                            $"Refuse : le compte {appelant?.CompteWindows ?? http.User.Identity?.Name} n'a pas la fonction " +
                            $"{RoutesCompteur.FonctionNumerosManquants} (Liste des numeros chrono manquants) dans le greffe {greffe}."),
                        statusCode: StatusCodes.Status403Forbidden);
                return await ModuleEcransMain.SurLaBase(c, greffe, ct, async cn =>
                {
                    if (await droits.AppelantAsync(http, commune, cn, ct) is not { } moi)
                        return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                    var resultat = await EcrivainsEcrans.JouerAsync(EcritureManquants.Ecran, nameof(EcritureManquants), EcritureManquants.JouerAsync, cn,
                                                                     geste, moi, DateTime.Now, numeros, ct);
                    return resultat.Fait ? Results.Ok(resultat) : Results.Json(resultat, statusCode: StatusCodes.Status422UnprocessableEntity);
                });
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
}
