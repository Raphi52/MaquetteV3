using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Les courriels du greffe (SOC-006) : le service d'envoi (<see cref="ServiceCourriels"/>) pour les actes de la V3, le
/// menage quotidien des traces (<see cref="MenageCourriels"/>), six routes de lecture pour les ecrans « Courriels
/// envoyés » (les envois, un envoi) et « Messagerie » (les modeles, un modele et son apercu, les listes de distribution, les boites
/// du greffe par leur adresse), et six routes d'ecriture pour la Messagerie (creer, modifier, activer ou desactiver un modele et une liste : <see cref="GestionCourriels"/>).
///
/// Les routes portent le greffe dans leur adresse et <see cref="PolitiquesSocle.AgentAdmis"/>, posee une fois sur leur
/// groupe : l'agent est juge dans la base de CE greffe (401, 403, 404, 503 par la politique). La liste des envois et le
/// detail d'un envoi exigent en plus la fonction MAIL_LOT1, celle de l'ecran « Mails envoyés » de la V2 : sans elle,
/// 403 et rien n'est lu. Les routes d'ecriture exigent la fonction GESTEMAIL, celle de l'ecran V2 « Modele de mail » : sans
/// elle, 403 et rien n'est ecrit. La base du greffe s'ouvre par le catalogue commun, jamais autrement.
/// </summary>
internal sealed class ModuleCourriels : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        // Lue ici : une configuration incoherente (production sans serveur de messagerie...) arrete le demarrage.
        var options = OptionsCourriels.Lire(configuration);
        services.AddSingleton(options);
        services.AddSingleton<ServiceCourriels>();
        services.AddSingleton<MenageCourriels>();
        services.AddHostedService(sp => sp.GetRequiredService<MenageCourriels>());
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var courriels = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        courriels.MapGet(RoutesCourriels.Envois, async (string greffe, DateOnly? du, DateOnly? au, string? destinataire, string? modele,
                                                        string? etat, int? max, HttpContext http, CatalogueGreffes c,
                                                        ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (await SansLaFonction(http, droits, commune, greffe, ct) is { } refus) return refus;
            if (!string.IsNullOrWhiteSpace(etat) && !EtatsCourriel.Tous.Contains(etat.Trim()))
                return Refus(StatusCodes.Status400BadRequest, $"Etat « {etat} » inconnu : {string.Join(", ", EtatsCourriel.Tous)}.");
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
                Results.Ok(await TracesCourriels.ListerAsync(cn, du, au, destinataire, modele, etat, max, ct)));
        });

        courriels.MapGet(RoutesCourriels.Envoi, async (string greffe, Guid suivi, HttpContext http, CatalogueGreffes c,
                                                       ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (await SansLaFonction(http, droits, commune, greffe, ct) is { } refus) return refus;
            return await SurBaseDuGreffe(c, greffe, ct, async cn =>
                await TracesCourriels.DetailAsync(cn, suivi, ct) is { } detail
                    ? Results.Ok(detail)
                    : Refus(StatusCodes.Status404NotFound, $"Aucun courriel de numero de suivi {suivi} dans le greffe {greffe}."));
        });

        // CA05 : un agent du greffe ne voit pas un modele reserve a Amitel ; un agent d'Amitel les voit tous.
        courriels.MapGet(RoutesCourriels.Modeles, async (string greffe, HttpContext http, CatalogueGreffes c,
                                                         ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async cn =>
                await droits.AppelantAsync(http, commune, cn, ct) is { } moi
                    ? Results.Ok(await ServiceCourriels.ModelesVisiblesAsync(cn, moi.EstAmitel, ct))
                    : NonAdmis()));

        // CA05 encore : un modele reserve a Amitel n'est pas plus lisible par son nom que par la liste (404, comme inconnu).
        courriels.MapGet(RoutesCourriels.Modele, async (string greffe, string type, HttpContext http, CatalogueGreffes c,
                                                        ServiceDroits droits, BaseRig commune, ServiceCourriels service, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async cn =>
                await droits.AppelantAsync(http, commune, cn, ct) is not { } moi ? NonAdmis()
                : await service.ApercuAsync(greffe, cn, type, moi.EstAmitel, moi.CompteWindows, ct) is { } apercu ? Results.Ok(apercu)
                : Refus(StatusCodes.Status404NotFound, $"Le modèle de courriel « {type} » n'est pas dans le greffe {greffe}.")));

        courriels.MapGet(RoutesCourriels.ListesDiffusion, async (string greffe, HttpContext http, CatalogueGreffes c,
                                                                 ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async cn =>
                await droits.AppelantAsync(http, commune, cn, ct) is { } moi
                    ? Results.Ok(await ServiceCourriels.ListesVisiblesAsync(cn, moi.CompteWindows, ct))
                    : NonAdmis()));

        // Le formulaire d'un modele (lot 16) : les boites du greffe par leur adresse (INFO_GREFFE) et l'etat de son habillage.
        courriels.MapGet(RoutesCourriels.Boites, async (string greffe, HttpContext http, CatalogueGreffes c,
                                                        ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            await SurBaseDuGreffe(c, greffe, ct, async cn =>
                await droits.AppelantAsync(http, commune, cn, ct) is null
                    ? NonAdmis()
                    : Results.Ok(await GestionCourriels.BoitesAsync(greffe, cn, c, commune, ct))));

        // --- La gestion (ecran « Messagerie », procedure V2 PROC_GESTEMAIL) : chaque geste dans sa transaction, avec sa ligne au
        // --- journal des gestes ; un modele reserve a Amitel n'existe pas pour un agent du greffe (404, CA05).
        courriels.MapPost(RoutesCourriels.Modeles, (string greffe, SaisieModele saisie, HttpContext http, CatalogueGreffes c,
                                                    ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Json(await GestionCourriels.CreerModeleAsync(cn, greffe, moi, saisie, ct), statusCode: StatusCodes.Status201Created)));

        courriels.MapPut(RoutesCourriels.Modele, (string greffe, string type, SaisieModele saisie, HttpContext http, CatalogueGreffes c,
                                                  ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Ok(await GestionCourriels.ModifierModeleAsync(cn, greffe, moi, type, saisie, ct))));

        courriels.MapPut(RoutesCourriels.ActivationModele, (string greffe, string type, SaisieActivation saisie, HttpContext http,
                                                            CatalogueGreffes c, ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Ok(await GestionCourriels.ActiverModeleAsync(cn, greffe, moi, type, saisie, ct))));

        courriels.MapPost(RoutesCourriels.ListesDiffusion, (string greffe, SaisieListe saisie, HttpContext http, CatalogueGreffes c,
                                                            ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Json(await GestionCourriels.CreerListeAsync(cn, moi, saisie, ct), statusCode: StatusCodes.Status201Created)));

        courriels.MapPut(RoutesCourriels.ListeDiffusion, (string greffe, int id, SaisieListe saisie, HttpContext http, CatalogueGreffes c,
                                                          ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Ok(await GestionCourriels.ModifierListeAsync(cn, greffe, moi, id, saisie, ct))));

        courriels.MapPut(RoutesCourriels.ActivationListe, (string greffe, int id, SaisieActivation saisie, HttpContext http,
                                                           CatalogueGreffes c, ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
            Gerer(http, c, droits, commune, greffe, ct, async (cn, moi) =>
                Results.Ok(await GestionCourriels.ActiverListeAsync(cn, greffe, moi, id, saisie, ct))));
    }

    /// <summary>
    /// Un geste de gestion : l'appelant admis, qui a la fonction GESTEMAIL dans la base de CE greffe -- sinon 403 et rien n'est
    /// ecrit (une panne de la verification est un refus, SOC-002 CA06) ; un geste refuse rend son statut et son motif.
    /// </summary>
    private static Task<IResult> Gerer(HttpContext http, CatalogueGreffes c, ServiceDroits droits, BaseRig commune, string greffe,
                                       CancellationToken ct, Func<SqlConnection, Appelant, Task<IResult>> geste)
        => SurBaseDuGreffe(c, greffe, ct, async cn =>
        {
            if (await droits.AppelantAsync(http, commune, cn, ct) is not { } moi) return NonAdmis();
            if (await droits.VerifierAsync(http, commune, moi, RoutesCourriels.FonctionGestion, ct) is not { Autorise: true })
                return Refus(StatusCodes.Status403Forbidden,
                    $"Refusé : le compte {moi.CompteWindows} n'a pas la fonction {RoutesCourriels.FonctionGestion} (« Gestion modèles emails ») " +
                    $"dans le greffe {greffe} : il lit les modèles et les listes, il ne les modifie pas.");
            try { return await geste(cn, moi); }
            catch (RefusGestionCourriels e) { return Refus(e.Statut, e.Message); }
        });

    private static IResult NonAdmis() => Refus(StatusCodes.Status403Forbidden, "Refuse : l'agent n'est pas admis dans ce greffe.");

    /// <summary>403 si l'agent n'a pas la fonction MAIL_LOT1 dans la base de CE greffe ; null s'il l'a. Une panne de la
    /// verification est un refus (SOC-002 CA06, trace par ServiceDroits).</summary>
    private static async Task<IResult?> SansLaFonction(HttpContext http, ServiceDroits droits, BaseRig commune, string greffe, CancellationToken ct)
    {
        var moi = await droits.AppelantAsync(http, commune, ct);
        var droit = moi is null ? null : await droits.VerifierAsync(http, commune, moi, RoutesCourriels.FonctionCourrielsEnvoyes, ct);
        if (droit is { Autorise: true }) return null;
        return Refus(StatusCodes.Status403Forbidden,
            $"Refuse : le compte {moi?.CompteWindows ?? http.User.Identity?.Name} n'a pas la fonction {RoutesCourriels.FonctionCourrielsEnvoyes} " +
            $"(« Mails envoyés ») dans le greffe {greffe}.");
    }

    /// <summary>Le travail d'une route sur la base V3 du greffe, ouverte par le catalogue ; si elle ne s'ouvre plus, la meme
    /// reponse que la politique (404 / 503), jamais une autre base.</summary>
    private static async Task<IResult> SurBaseDuGreffe(CatalogueGreffes c, string greffe, CancellationToken ct, Func<SqlConnection, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn) return await travail(cn);
    }

    private static IResult Refus(int statut, string motif) => Results.Json(new RefusGreffe(motif), statusCode: statut);
}
