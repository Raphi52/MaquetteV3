using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Dépôts judiciaires » (PROC_DEPOTJUD) : les codes d'evenement avec depot d'une instance (lecture seule) et le geste « nouvel
/// evenement survenu » et son depot (EcritureDepotsJud.cs). La liste des instances reste RoutesDepotsJud.Instances (ModuleEcransMain).
/// Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8, 28/11/2026.
/// </summary>
internal sealed class ModuleDepotsJud : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesDepotsJud.Codes, (string greffe, string role, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await EcritureDepotsJud.CodesAsync(cn, role, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        GesteFaitMain.Publier(routes, RoutesDepotsJud.Deposer, EcritureDepotsJud.Ecran, nameof(EcritureDepotsJud), EcritureDepotsJud.Gestes,
                              EcritureDepotsJud.JouerAsync);
    }
}
