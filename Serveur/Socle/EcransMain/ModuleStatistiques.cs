using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Statistiques » (PROC_STAT) : la route du geste « Excel » (EcritureStatistiques.cs). Le calcul reste RoutesStatistiques.Statistiques
/// (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026.
/// </summary>
internal sealed class ModuleStatistiques : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesStatistiques.Excel, EcritureStatistiques.Ecran, nameof(EcritureStatistiques), EcritureStatistiques.Gestes,
                                 EcritureStatistiques.JouerAsync);
}
