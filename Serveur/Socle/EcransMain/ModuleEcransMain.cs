using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// Les routes des ECRANS FAITS MAIN qui lisaient la demonstration (plan « RIG v3 100 % fonctionnel », lot 17) : Demandes,
/// Arrivee du courrier et Teletransmissions, BODACC, dossiers deja ouverts. Chaque lecture est dans son fichier (Lecture*.cs),
/// sources V2 citees. La base est la base V3 du greffe de l'adresse, ouverte par le catalogue commun (aucune chaine ici, aucun
/// repli sur une autre base) ; l'agent y est admis (PolitiquesSocle.AgentAdmis), comme pour les ecrans generes
/// (ModuleEcransGeneres) : le menu filtre deja les ecrans par profil.
/// </summary>
internal sealed class ModuleEcransMain : IModuleServeur
{
    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapPost(RoutesEcransMain.Demandes, (string greffe, RechercheDemandes recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDemandes.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapPost(RoutesEcransMain.Arrivees, (string greffe, RechercheArrivees recherche, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureArrivees.LireAsync(cn, recherche, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapGet(RoutesEcransMain.Bodacc, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureBodacc.LireAsync(cn, DateTime.Now, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapGet(RoutesEcransMain.DossiersOuverts, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDossiersOuverts.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Lot 5 : « Dépôts judiciaires » (LectureDepotsJud.cs).
        routes.MapGet(RoutesDepotsJud.Instances, (string greffe, CatalogueGreffes c, CancellationToken ct)
            => SurLaBase(c, greffe, ct, async cn => Results.Ok(await LectureDepotsJud.LireAsync(cn, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }

    /// <summary><paramref name="faire"/> sur la base V3 du greffe ; 404 / 503 de la politique si elle ne s'ouvre plus.</summary>
    private static async Task<IResult> SurLaBase(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, Task<IResult>> faire)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await faire(cn);
    }
}
