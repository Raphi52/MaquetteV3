using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Experts » (PROC_EXPERT) : la route du geste « Enregistrer la fiche » d'un expert (EcritureExperts.cs). La lecture reste
/// RoutesExperts.Experts (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026.
/// </summary>
internal sealed class ModuleExperts : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesFicheExpert.Enregistrer, EcritureExperts.Ecran, nameof(EcritureExperts), EcritureExperts.Gestes,
                                 EcritureExperts.JouerAsync);
}
