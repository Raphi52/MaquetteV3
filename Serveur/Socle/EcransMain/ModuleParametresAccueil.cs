using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Mon accueil » (PROC_ACCUEIL_PARAM) : la route du geste « Enregistrer mes réglages d'accueil » (EcritureParametresAccueil.cs). La
/// lecture reste RoutesParametresAccueil.Parametres (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours
/// complet », lot 13, 28/11/2026.
/// </summary>
internal sealed class ModuleParametresAccueil : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesParametresAccueil.Enregistrer, EcritureParametresAccueil.Ecran, nameof(EcritureParametresAccueil),
                                 EcritureParametresAccueil.Gestes, EcritureParametresAccueil.JouerAsync);
}
