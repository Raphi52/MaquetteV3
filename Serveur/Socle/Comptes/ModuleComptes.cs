using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Comptes;

/// <summary>Les politiques d'acces des comptes et des droits, a poser apres <see cref="PolitiquesSocle.AgentAdmis"/>.</summary>
public static class PolitiquesComptes
{
    /// <summary>L'appelant a le droit de gerer les comptes et les droits : le role
    /// <see cref="RoutesDroits.RoleGestionDesDroits"/> (SOC-002 CA04). Sinon 403, la route ne s'execute pas.</summary>
    public const string GestionDesDroits = "gestion-des-droits";
}

/// <summary>
/// SOC-002, SOC-004, SOC-005, SOC-016 : les comptes des agents, leurs droits et les journaux.
///
/// Toutes les routes exigent un agent admis (SOC-001). La gestion des comptes, des roles et le journal
/// des droits exigent EN PLUS le role de gestion des droits : la V2 le prevoyait pour ses ecrans
/// GESTUTIL et GESTROLE, mais l'a laisse en commentaire (FORM_UTILISATEUR.cs:20-24, FORM_ROLE.cs:20-24) ;
/// la V3 le tient ici, cote serveur (CA04, regle « a valider » de la story).
///
/// LA BASE (lot 9). Chaque adresse porte le code du greffe, <c>{greffe}</c> : comme la V2
/// (Utilisateur.GetUtilisateurCourant(CodeGreffeRig)), les comptes, les droits et les journaux sont ceux de la
/// base V3 de CE greffe, ouverte par le catalogue commun (<see cref="ServiceDroits.OuvrirBaseAsync"/>). La
/// politique AgentAdmis y juge l'agent : 404 si le greffe n'est pas au catalogue, 503 si sa base ne repond pas,
/// sans repli sur une autre ; un agent declare dans un seul greffe est inconnu des autres. Le code greffe d'un
/// compte cree par le greffe est celui de l'adresse (le RIG.Common.CodeGreffeRig de la V2).
/// </summary>
internal sealed class ModuleComptes : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ServiceDroits>();
        services.AddSingleton<ServiceComptes>();
        services.AddAuthorization(o => o.AddPolicy(PolitiquesComptes.GestionDesDroits,
            p => p.RequireAuthenticatedUser().AddRequirements(new ExigenceGestionDesDroits())));
        services.AddSingleton<IAuthorizationHandler, ControleGestionDesDroits>();
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var gestion = new[] { PolitiquesSocle.AgentAdmis, PolitiquesComptes.GestionDesDroits };

        routes.MapGet(RoutesComptes.Comptes, (HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.VueAsync(cn, moi, ct))))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Creer, (string greffe, DemandeCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.CreerAsync(cn, moi, greffe, d with { Id = null }, ct))))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Modifier, (DemandeCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.ModifierAsync(cn, moi, d, ct))))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Desactiver, (DemandeSurCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.DesactiverAsync(cn, moi, d, ct))))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Supprimer, (DemandeSurCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.SupprimerAsync(cn, moi, d, ct))))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Role, (DemandeRole d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (cn, moi) => Results.Ok(await comptes.RoleAsync(cn, moi, d, ct))))
            .RequireAuthorization(gestion);

        routes.MapGet(RoutesDroits.Journal, (string? compte, string? role, string? du, string? au, HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct) =>
            {
                if (!Dates(du, au, out var debut, out var fin)) return Task.FromResult(Results.BadRequest("Dates attendues au format yyyy-MM-dd."));
                return SurLaBase(h, b, droits, ct, async (cn, _) => Results.Ok(await JournalGestes.LireDroitsAsync(cn, compte, role, debut, fin, ct)));
            })
            .RequireAuthorization(gestion);

        // Chaque agent admis verifie SES droits : la reponse ne dit rien d'un autre compte.
        routes.MapGet(RoutesDroits.Verifier, (string? fonction, HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct)
                => SurLaBase(h, b, droits, ct, async (_, moi) => Results.Ok(await droits.VerifierAsync(h, b, moi, fonction ?? string.Empty, ct))))
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Le journal des gestes : tout le greffe pour qui a la fonction JOURNAL_GESTES, ses propres gestes sinon (SOC-016 CA03).
        routes.MapGet(RoutesJournal.Gestes, (string? agent, string? objet, string? du, string? au, HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct) =>
            {
                if (!Dates(du, au, out var debut, out var fin)) return Task.FromResult(Results.BadRequest("Dates attendues au format yyyy-MM-dd."));
                return SurLaBase(h, b, droits, ct, async (cn, moi) =>
                {
                    var tout = await droits.VerifierAsync(h, b, moi, RoutesJournal.FonctionJournalDuGreffe, ct);
                    return Results.Ok(await JournalGestes.LireAsync(cn, moi, tout.Autorise, agent, objet, debut, fin, ct));
                });
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
    }

    /// <summary>
    /// Le travail d'une route sur la base de SON greffe, avec l'appelant que la politique vient d'admettre.
    /// La politique a deja ouvert cette base ; si elle cesse de repondre entre-temps (ou si la fiche du greffe
    /// disparait), la reponse est la meme que celle de la politique : 503 ou 404, jamais une autre base.
    /// </summary>
    private static async Task<IResult> SurLaBase(HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct,
                                                 Func<SqlConnection, Appelant, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await droits.OuvrirBaseAsync(h, b, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn)
            return await travail(cn, (await droits.AppelantAsync(h, b, cn, ct))!);
    }

    private static bool Dates(string? du, string? au, out DateOnly? debut, out DateOnly? fin)
    {
        debut = fin = null;
        try
        {
            debut = JournalGestes.Date(du);
            fin = JournalGestes.Date(au);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>L'exigence de <see cref="PolitiquesComptes.GestionDesDroits"/>.</summary>
internal sealed class ExigenceGestionDesDroits : IAuthorizationRequirement;

/// <summary>Satisfait <see cref="ExigenceGestionDesDroits"/> quand l'appelant admis a le role de gestion des droits,
/// dans la base du greffe de l'adresse. Un greffe qui ne s'ouvre pas laisse l'exigence non satisfaite : la reponse
/// (404 ou 503) est celle que la politique AgentAdmis a deja posee pour la meme requete.</summary>
internal sealed class ControleGestionDesDroits(ServiceDroits droits, BaseRig b) : AuthorizationHandler<ExigenceGestionDesDroits>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexte, ExigenceGestionDesDroits exigence)
    {
        if (contexte.Resource is not HttpContext http) return;
        Appelant? appelant;
        try { appelant = await droits.AppelantAsync(http, b, http.RequestAborted); }
        catch (GreffeInconnu) { return; }
        catch (BaseGreffeInjoignable) { return; }
        if (appelant is { GereLesDroits: true }) contexte.Succeed(exigence);
    }
}
