using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// Les routes propres a « Rédaction d'une décision » (PROC_REDA) apres le lot 20 : le geste « valider la décision »
/// (EcritureRedaction.cs) et les valeurs de ses listes. Sa lecture reste dans ModuleEcransMain (lot 10). Plan « parcours complet », lot 8, 28/11/2026.
/// </summary>
internal sealed class ModuleRedaction : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(ValidationDecision.Listes, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await EcritureRedaction.ListesAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        GesteFaitMain.Publier(routes, ValidationDecision.Valider, EcritureRedaction.Ecran, nameof(EcritureRedaction), EcritureRedaction.Gestes, EcritureRedaction.JouerAsync);
    }
}
