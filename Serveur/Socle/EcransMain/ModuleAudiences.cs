using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Créer une audience » (PROC_AUDIENCE) : la route du geste « Créer l'audience » (EcritureAudiences.cs). La lecture reste
/// RoutesAudiences.Programmees (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026.
/// </summary>
internal sealed class ModuleAudiences : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesCreationAudience.Creer, EcritureAudiences.Ecran, nameof(EcritureAudiences), EcritureAudiences.Gestes,
                                 EcritureAudiences.JouerAsync);
}
