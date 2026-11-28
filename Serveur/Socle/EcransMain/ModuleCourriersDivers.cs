using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Courriers divers » (PROC_COURRIER_DIVERS) : le catalogue des modeles (LectureCourriersDivers.cs, lecture seule) et les gestes de la
/// fiche, valider et supprimer (EcritureCourriersDivers.cs). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 8,
/// 28/11/2026.
/// </summary>
internal sealed class ModuleCourriersDivers : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesCourriersDivers.Catalogue, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => ModuleEcransMain.SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureCourriersDivers.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
        GesteFaitMain.Publier(routes, RoutesCourriersDivers.Geste, EcritureCourriersDivers.Ecran, nameof(EcritureCourriersDivers),
                              EcritureCourriersDivers.Gestes, EcritureCourriersDivers.JouerAsync);
    }
}
