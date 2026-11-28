using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Coffre-fort » (PROC_COFFRE_FORT) : la route du geste « Re-transmettre » (EcritureCoffreFort.cs). La lecture reste
/// RoutesCoffreFort.Depots (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 10, 28/11/2026.
/// </summary>
internal sealed class ModuleCoffreFort : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesCoffreFort.Retransmission, EcritureCoffreFort.Ecran, nameof(EcritureCoffreFort),
                                 EcritureCoffreFort.Gestes, EcritureCoffreFort.JouerAsync);
}
