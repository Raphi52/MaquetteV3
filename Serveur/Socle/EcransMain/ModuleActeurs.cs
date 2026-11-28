using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Acteurs » (PROC_ACTEUR_JUD) : la route du geste « Enregistrer les coordonnées » d'un acteur (EcritureActeurs.cs). La lecture reste
/// RoutesActeurs.Acteurs (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026.
/// </summary>
internal sealed class ModuleActeurs : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesCoordonneesActeur.Enregistrer, EcritureActeurs.Ecran, nameof(EcritureActeurs), EcritureActeurs.Gestes,
                                 EcritureActeurs.JouerAsync);
}
