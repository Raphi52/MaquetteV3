using RigV3.Contrats.EcransMain;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Documents dématérialisés » (PROC_DOC_DEMAT) : la route des gestes « Supprimer » et « Remettre en vigueur » une pièce
/// (EcritureDocDemat.cs). La lecture reste RoutesDocDemat.Pieces (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du
/// parcours complet », lot 10, 28/11/2026.
/// </summary>
internal sealed class ModuleDocDemat : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => GesteFaitMain.Publier(routes, RoutesDocDemat.Etat, EcritureDocDemat.Ecran, nameof(EcritureDocDemat), EcritureDocDemat.Gestes,
                                 EcritureDocDemat.JouerAsync);
}
