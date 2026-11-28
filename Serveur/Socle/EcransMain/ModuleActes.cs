using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Actes » (PROC_ACTES_NUM) : la route du geste « Définir le document comme (non) numérisable » (EcritureActes.cs). La lecture reste
/// RoutesActes.Actes (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 10, 28/11/2026.
/// </summary>
internal sealed class ModuleActes : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesActes.SansDocument, EcritureActes.Ecran, nameof(EcritureActes), EcritureActes.Gestes,
                                 EcritureActes.JouerAsync);
}
