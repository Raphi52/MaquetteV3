using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// Les routes propres a « GED judiciaire » (PROC_GEDJUD) apres le lot 20 : le geste « modifier un document » (EcritureGedJud.cs).
/// Sa lecture reste dans ModuleEcransMain (lot 11). Plan « parcours complet », lot 8, 28/11/2026.
/// </summary>
internal sealed class ModuleGedJud : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(ModificationDocumentGed.Etats, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await EcritureGedJud.EtatsAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        GesteFaitMain.Publier(routes, ModificationDocumentGed.Modifier, EcritureGedJud.Ecran, nameof(EcritureGedJud), EcritureGedJud.Gestes, EcritureGedJud.JouerAsync);
    }
}
