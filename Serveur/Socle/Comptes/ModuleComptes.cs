using Microsoft.AspNetCore.Authorization;
using RigV3.Contrats.Socle;
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
/// La base est celle du serveur (<see cref="BaseRig"/>), la meme que celle qui juge l'identite (lot 2) ;
/// le raccord a la base de CHAQUE greffe (lot 4, CatalogueGreffes) est decrit dans
/// docs\reconstruction\socle-droits.md.
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

        routes.MapGet(RoutesComptes.Comptes, async (HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.VueAsync(b, (await droits.AppelantAsync(h, b, ct))!, ct)))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Creer, async (DemandeCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.CreerAsync(b, (await droits.AppelantAsync(h, b, ct))!, d with { Id = null }, ct)))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Modifier, async (DemandeCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.ModifierAsync(b, (await droits.AppelantAsync(h, b, ct))!, d, ct)))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Desactiver, async (DemandeSurCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.DesactiverAsync(b, (await droits.AppelantAsync(h, b, ct))!, d, ct)))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Supprimer, async (DemandeSurCompte d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.SupprimerAsync(b, (await droits.AppelantAsync(h, b, ct))!, d, ct)))
            .RequireAuthorization(gestion);

        routes.MapPost(RoutesComptes.Role, async (DemandeRole d, HttpContext h, BaseRig b, ServiceDroits droits, ServiceComptes comptes, CancellationToken ct)
                => Results.Ok(await comptes.RoleAsync(b, (await droits.AppelantAsync(h, b, ct))!, d, ct)))
            .RequireAuthorization(gestion);

        routes.MapGet(RoutesDroits.Journal, async (string? compte, string? role, string? du, string? au, BaseRig b, CancellationToken ct) =>
            {
                if (!Dates(du, au, out var debut, out var fin)) return Results.BadRequest("Dates attendues au format yyyy-MM-dd.");
                return Results.Ok(await JournalGestes.LireDroitsAsync(b, compte, role, debut, fin, ct));
            })
            .RequireAuthorization(gestion);

        // Chaque agent admis verifie SES droits : la reponse ne dit rien d'un autre compte.
        routes.MapGet(RoutesDroits.Verifier, async (string? fonction, HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct) =>
            {
                var moi = (await droits.AppelantAsync(h, b, ct))!;
                return Results.Ok(await droits.VerifierAsync(b, moi.Id, moi.CompteWindows, fonction ?? string.Empty, ct));
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);

        // Le journal des gestes : tout le greffe pour qui a la fonction JOURNAL_GESTES, ses propres gestes sinon (SOC-016 CA03).
        routes.MapGet(RoutesJournal.Gestes, async (string? agent, string? objet, string? du, string? au, HttpContext h, BaseRig b, ServiceDroits droits, CancellationToken ct) =>
            {
                if (!Dates(du, au, out var debut, out var fin)) return Results.BadRequest("Dates attendues au format yyyy-MM-dd.");
                var moi = (await droits.AppelantAsync(h, b, ct))!;
                var tout = await droits.VerifierAsync(b, moi.Id, moi.CompteWindows, RoutesJournal.FonctionJournalDuGreffe, ct);
                return Results.Ok(await JournalGestes.LireAsync(b, moi, tout.Autorise, agent, objet, debut, fin, ct));
            })
            .RequireAuthorization(PolitiquesSocle.AgentAdmis);
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

/// <summary>Satisfait <see cref="ExigenceGestionDesDroits"/> quand l'appelant admis a le role de gestion des droits.</summary>
internal sealed class ControleGestionDesDroits(ServiceDroits droits, BaseRig b) : AuthorizationHandler<ExigenceGestionDesDroits>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexte, ExigenceGestionDesDroits exigence)
    {
        if (contexte.Resource is not HttpContext http) return;
        if (await droits.AppelantAsync(http, b, http.RequestAborted) is { GereLesDroits: true }) contexte.Succeed(exigence);
    }
}
