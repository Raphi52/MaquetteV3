using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Clôture » (PROC_CLOT) : la route du geste « Courrier aux débiteurs » (EcritureCloture.cs). La lecture reste RoutesCloture.Affaires
/// (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026.
/// </summary>
internal sealed class ModuleCloture : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesCloture.CourrierDebiteurs, EcritureCloture.Ecran, nameof(EcritureCloture), EcritureCloture.Gestes,
                                 EcritureCloture.JouerAsync);
}
