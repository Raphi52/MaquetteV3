using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Clôture » (PROC_CLOT) : les routes de ses gestes (EcritureCloture.cs) -- l'« Enrôlement sur saisine d'office », le geste principal, et
/// le « Courrier aux débiteurs » -- et la relecture des enrôlements (LectureCloture.EnrolementsAsync, l'onglet « Enrôlement / requête »).
/// La lecture de la grille reste RoutesCloture.Affaires (ModuleEcransMain). Plan « RIG v3 complet, mesuré par le banc du parcours
/// complet », lot 11, 28/11/2026.
/// </summary>
internal sealed class ModuleCloture : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        GesteFaitMain.Publier(routes, RoutesCloture.EnrolementSo, EcritureCloture.Ecran, nameof(EcritureCloture), EcritureCloture.GestesEnrolement,
                              EcritureCloture.EnrolerAsync);
        GesteFaitMain.Publier(routes, RoutesCloture.CourrierDebiteurs, EcritureCloture.Ecran, nameof(EcritureCloture), EcritureCloture.Gestes,
                              EcritureCloture.JouerAsync);
        routes.MapPost(RoutesCloture.Enrolements, (string greffe, RechercheCloture recherche, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureCloture.EnrolementsAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }
}
