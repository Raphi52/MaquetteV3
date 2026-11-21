using RigV3.Serveur.Socle.Supervision;

namespace RigV3.Serveur.Recopie;

/// <summary>
/// La recopie dans la partie serveur, la nuit (lot 8 de la phase 2) : le controle des deux bases de chaque greffe
/// (REC-006, <see cref="ComparaisonNocturne"/>, planifie par le planificateur de nuit des clotures NF525) et les alertes
/// de la recopie (REC-005 CA05, REC-006 CA04, <see cref="AlertesRecopie"/>, lues par le surveillant). Aucune route : le
/// controle se lit dans la base V3 du greffe (rec.Comparaison) et par l'outil de l'exploitant (Outils\Comparaison).
/// </summary>
public sealed class ModuleRecopieNuit : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ComparaisonNocturne>();
        services.AddSingleton<ITraitementDeFond>(sp => sp.GetRequiredService<ComparaisonNocturne>());
        services.AddSingleton<ITachePlanifiee>(sp => sp.GetRequiredService<ComparaisonNocturne>());
        services.AddSingleton<ISourceAlertes, AlertesRecopie>();
    }

    public void Brancher(IEndpointRouteBuilder routes) { }
}
