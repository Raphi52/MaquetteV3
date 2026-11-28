using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Recherche d'affaires » (PROC_RECHAFF) : la route du geste « rattacher une demande a l'affaire choisie » (EcritureRechAffaires.cs).
/// La recherche reste RoutesAffaires.Affaires (ModuleEcransMain). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8,
/// 28/11/2026.
/// </summary>
internal sealed class ModuleRechAffaires : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesAffaires.Rattacher, EcritureRechAffaires.Ecran, nameof(EcritureRechAffaires), EcritureRechAffaires.Gestes,
                                 EcritureRechAffaires.JouerAsync);
}
