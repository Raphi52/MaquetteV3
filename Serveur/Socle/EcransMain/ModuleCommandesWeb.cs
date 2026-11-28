using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Commandes web » (PROC_COMMANDES_WEB) : la route du geste « Mettre en attente » (EcritureCommandesWeb.cs). La lecture reste
/// RoutesCommandesWeb.Commandes (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
/// </summary>
internal sealed class ModuleCommandesWeb : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesTraitementCommandesWeb.MettreEnAttente, EcritureCommandesWeb.Ecran, nameof(EcritureCommandesWeb),
                                 EcritureCommandesWeb.Gestes, EcritureCommandesWeb.JouerAsync);
}
