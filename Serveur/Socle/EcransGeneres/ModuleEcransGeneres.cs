using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Numerotation.Compteur;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// Les routes des ecrans generes : les LIGNES (cherchees ou non), UNE FICHE, et les GESTES (modes d'emploi :
/// LecteurEcran.cs, EcrivainEcran.cs). La base est la base V3 du greffe de l'adresse, ouverte par le catalogue commun
/// (aucune chaine ici, aucun repli sur une autre base) ; l'agent y est admis (PolitiquesSocle.AgentAdmis). Une procedure
/// sans lecteur rend Branchee = faux (l'ecran garde ses exemples) ; sans ecrivain, un geste rend SansEcrivain (rien
/// d'ecrit, l'ecran dit « Demonstration »).
/// </summary>
internal sealed class ModuleEcransGeneres : IModuleServeur
{
    /// <summary>Le nombre maximal de lignes rendues : une liste de travail, pas une extraction.</summary>
    public const int Plafond = 200;

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(LecteursEcrans.Decouvrir(typeof(ModuleEcransGeneres).Assembly));
        services.AddSingleton(EcrivainsEcrans.Decouvrir(typeof(ModuleEcransGeneres).Assembly));
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesEcransGeneres.Lignes, async (string greffe, string procedure, string? texte, string[]? critere, CatalogueGreffes c,
                                                         LecteursEcrans lecteurs, CancellationToken ct) =>
        {
            if (!lecteurs.ParProcedure.TryGetValue(procedure, out var lecteur))
                return Results.Ok(new LignesEcran(procedure, false, [], 0, Plafond));
            var recherche = RechercheEcran.Lire(texte, critere);
            return await SurLaBase(c, greffe, ct, async cn =>
            {
                // Une recherche ne va dans la base que si le lecteur sait chercher ; sinon la liste de travail, et l'ecran
                // filtre ce qu'il a (Cherchee = faux).
                var cherche = !recherche.Vide && lecteur is IRechercheEcran;
                var lues = cherche
                    ? await ((IRechercheEcran)lecteur).ChercherAsync(cn, recherche, DateTime.Now, Plafond, ct)
                    : await lecteur.LireAsync(cn, DateTime.Now, Plafond, ct);
                return Results.Ok(new LignesEcran(procedure, true, [.. lues.Lignes], lues.Total, Plafond)
                                  { Cherchee = cherche, Chercheur = lecteur is IRechercheEcran });
            });
        }).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapGet(RoutesEcransGeneres.Fiche, async (string greffe, string procedure, string id, CatalogueGreffes c, LecteursEcrans lecteurs,
                                                        CancellationToken ct) =>
        {
            if (!lecteurs.ParProcedure.TryGetValue(procedure, out var lecteur) || lecteur is not IFicheEcran fiches)
                return Results.NotFound(new { motif = $"La procédure {procedure} ne lit pas encore de fiche dans la base du greffe." });
            return await SurLaBase(c, greffe, ct, async cn => await fiches.LireFicheAsync(cn, id, DateTime.Now, ct) is { } fiche
                ? Results.Ok(fiche)
                : Results.NotFound(new { motif = $"Rien ne porte l'identifiant {id} dans la base du greffe {greffe}." }));
        }).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        routes.MapPost(RoutesEcransGeneres.Gestes, async (string greffe, string procedure, GesteEcran geste, HttpContext http, CatalogueGreffes c,
                                                          EcrivainsEcrans ecrivains, ServiceDroits droits, BaseRig commune,
                                                          DistributeurNumeros numeros, CancellationToken ct) =>
        {
            if (!ecrivains.ParProcedure.TryGetValue(procedure, out var ecrivain) || !ecrivain.Gestes.Contains(geste.Type))
                return Results.Ok(new ResultatGeste(false, ecrivain is null
                    ? $"Aucun écrivain pour {procedure} : rien n'est enregistré."
                    : $"{ecrivain.Ecran} n'écrit pas encore le geste {geste.Type} : rien n'est enregistré.") { SansEcrivain = true });
            return await SurLaBase(c, greffe, ct, async cn =>
            {
                var moi = await droits.AppelantAsync(http, commune, cn, ct);
                if (moi is null) return Results.Json(new { motif = "Agent inconnu dans la base de ce greffe." }, statusCode: StatusCodes.Status403Forbidden);
                var resultat = await EcrivainsEcrans.JouerAsync(ecrivain, cn, geste, moi, DateTime.Now, numeros, ct);
                return resultat.Fait ? Results.Ok(resultat) : Results.Json(resultat, statusCode: StatusCodes.Status422UnprocessableEntity);
            });
        }).RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }

    /// <summary>
    /// <paramref name="faire"/> sur la base V3 du greffe <paramref name="greffe"/>. La politique AgentAdmis vient d'ouvrir la
    /// meme base pour juger l'agent ; si elle ne s'ouvre plus entre-temps, la reponse est celle de la politique (404 / 503),
    /// jamais une autre base.
    /// </summary>
    private static async Task<IResult> SurLaBase(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, Task<IResult>> faire)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await faire(cn);
    }
}
