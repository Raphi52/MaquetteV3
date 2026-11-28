using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Gestion des audiences » (PROC_GESTAUDS) : la route de sa RECHERCHE, les critères de RIG v2 appliqués par le serveur
/// (LectureGestionAudiences.RechercherAsync). Aucune route d'écriture : PROC_GESTAUDS est une consultation dans RIG v2 (en tête de
/// LectureGestionAudiences.cs). La lecture d'ouverture reste RoutesGestionAudiences.Supprimees (ModuleEcransMain). Plan « RIG v3 complet,
/// mesuré par le banc du parcours complet », lot 9, 28/11/2026.
/// </summary>
internal sealed class ModuleGestionAudiences : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
        => routes.MapPost(RoutesRechercheAudiencesSupprimees.Rechercher,
                (string greffe, RechercheAudiencesSupprimees recherche, CatalogueGreffes c, CancellationToken ct)
                    => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureGestionAudiences.RechercherAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
}
