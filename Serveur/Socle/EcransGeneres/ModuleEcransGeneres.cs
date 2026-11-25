using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// La route des lignes des ecrans generes (mode d'emploi : LecteurEcran.cs). Lecture seule : la base V3 du greffe
/// de l'adresse, ouverte par le catalogue commun (aucune chaine ici, aucun repli sur une autre base), lue par le
/// lecteur de la procedure. Une procedure sans lecteur rend Branchee = faux : l'ecran garde ses exemples.
/// </summary>
internal sealed class ModuleEcransGeneres : IModuleServeur
{
    /// <summary>Le nombre maximal de lignes rendues : une liste de travail, pas une extraction.</summary>
    public const int Plafond = 200;

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
        => services.AddSingleton(LecteursEcrans.Decouvrir(typeof(ModuleEcransGeneres).Assembly));

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesEcransGeneres.Lignes, async (string greffe, string procedure, CatalogueGreffes c, LecteursEcrans lecteurs,
                                                         CancellationToken ct) =>
        {
            if (!lecteurs.ParProcedure.TryGetValue(procedure, out var lecteur))
                return Results.Ok(new LignesEcran(procedure, false, [], 0, Plafond));
            // La politique AgentAdmis vient d'ouvrir la meme base pour juger l'agent ; si elle ne s'ouvre plus
            // entre-temps, la reponse est celle de la politique (404 / 503), jamais une autre base.
            SqlConnection cn;
            try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
            catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
            catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
            await using (cn)
            {
                var lues = await lecteur.LireAsync(cn, DateTime.Now, Plafond, ct);
                return Results.Ok(new LignesEcran(procedure, true, [.. lues.Lignes], lues.Total, Plafond));
            }
        }).RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }
}
