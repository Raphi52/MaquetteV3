using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Arrivée du courrier » (PROC_ARRIVEE) : la route du geste « Enregistrer une arrivée » (EcritureArrivee.cs). La lecture reste
/// RoutesEcransMain.Arrivees (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
/// </summary>
internal sealed class ModuleArrivee : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesArrivee.Enregistrer, EcritureArrivee.Ecran, nameof(EcritureArrivee), EcritureArrivee.Gestes,
                                 EcritureArrivee.JouerAsync);
}
