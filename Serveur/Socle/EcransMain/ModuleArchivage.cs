using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Archivage / radiation » (PROC_ARCHIRAD) : la route du geste « Préparer le bordereau » (EcritureArchivage.cs). La lecture reste
/// RoutesArchivage.Dossiers (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026.
/// </summary>
internal sealed class ModuleArchivage : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesArchivage.Bordereau, EcritureArchivage.Ecran, nameof(EcritureArchivage), EcritureArchivage.Gestes,
                                 EcritureArchivage.JouerAsync);
}
