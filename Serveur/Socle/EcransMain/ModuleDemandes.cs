using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Demandes » (PROC_DEMANDE) : la route du geste « Supprimer l'état en cours » (EcritureDemandes.cs). La lecture reste
/// RoutesEcransMain.Demandes (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
/// </summary>
internal sealed class ModuleDemandes : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesDemandes.SupprimerEtatEnCours, EcritureDemandes.Ecran, nameof(EcritureDemandes),
                                 EcritureDemandes.Gestes, EcritureDemandes.JouerAsync);
}
