using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Réquisitions » (PROC_REQUI) : le geste « Terminer » la commande de documents (EcritureRequisitions.cs) et la relecture de la
/// commande terminée (LectureCommandesRequi.cs). La recherche des entreprises reste RoutesRequisitions.Entreprises (ModuleEcransMain).
/// Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
/// </summary>
internal sealed class ModuleRequisitions : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        GesteFaitMain.Publier(routes, RoutesTerminerRequisitions.Terminer, EcritureRequisitions.Ecran, nameof(EcritureRequisitions),
                              EcritureRequisitions.Gestes, EcritureRequisitions.JouerAsync);
        routes.MapPost(RoutesTerminerRequisitions.Commande, (string greffe, CommandeRequiDemandee demandee, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureCommandesRequi.LireAsync(cn, demandee, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }
}
