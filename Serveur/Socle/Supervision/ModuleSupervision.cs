using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Supervision;

/// <summary>Les politiques d'acces de la supervision, a poser apres <see cref="PolitiquesSocle.AgentAdmis"/>.</summary>
public static class PolitiquesSupervision
{
    /// <summary>L'appelant est un agent admis de la base commune, marque Amitel (soc.Compte.EstAmitel, la marque
    /// UTLST_IS_AMITEL de la V2) : l'exploitation et l'assistance d'Amitel. Sinon 403, la route ne s'execute pas.</summary>
    public const string ExploitationAmitel = "exploitation-amitel";
}

/// <summary>
/// SOC-017 et SOC-011 : le journal technique, l'etat des greffes et des traitements, les alertes et la relance.
///
/// Les routes de LECTURE et la relance sont reservees a l'exploitation d'Amitel (<see cref="PolitiquesSupervision.ExploitationAmitel"/>) :
/// un agent de greffe recoit d'une erreur son identifiant de suivi, jamais le detail (message, pile, compte, serveur).
/// La route qui recoit les erreurs de l'ecran est ouverte a toute session Windows : l'erreur peut survenir avant que
/// l'agent soit admis. Toutes sont communes (sans {greffe}) : une seule base, pour les 40 greffes (CA06, CA03).
/// </summary>
internal sealed class ModuleSupervision : IModuleServeur
{
    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(ReglagesSupervision.Depuis(configuration));
        services.AddSingleton<JournalErreurs>();
        services.AddSingleton<FileJournal>();
        services.AddSingleton<ILoggerProvider, FournisseurJournalTechnique>();
        services.AddHostedService<EcrivainJournal>();
        services.AddSingleton<IStartupFilter, FiltreErreurs>();
        services.AddSingleton<IDeveloperPageExceptionFilter, FiltreErreursDeveloppement>();

        services.AddSingleton<MoteurTraitements>();
        services.AddHostedService(sp => sp.GetRequiredService<MoteurTraitements>());
        services.AddSingleton<Surveillant>();
        services.AddHostedService(sp => sp.GetRequiredService<Surveillant>());
        // Le canal reel, quand il existera, s'inscrit par son module : TryAdd laisse sa place au premier inscrit.
        services.TryAddSingleton<ICanalAlerte, CanalAlerteSimule>();

        services.AddAuthorization(o => o.AddPolicy(PolitiquesSupervision.ExploitationAmitel,
            p => p.RequireAuthenticatedUser().AddRequirements(new ExigenceExploitationAmitel())));
        services.AddSingleton<IAuthorizationHandler, ControleExploitationAmitel>();
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var amitel = new[] { PolitiquesSocle.AgentAdmis, PolitiquesSupervision.ExploitationAmitel };

        // SOC-017 T03 : l'erreur de l'ecran. Le compte vient de la session, jamais du corps ; le parcours est celui que
        // l'ecran donne (erreur du serveur deja recue), sinon celui de cette requete.
        routes.MapPost(RoutesSupervision.ErreursEcran, async (ErreurEcran e, HttpContext h, JournalErreurs journal, CancellationToken ct) =>
            {
                var suivi = Suivis.Normaliser(e.Suivi) ?? Parcours.Courant?.Suivi ?? Suivis.Nouveau();
                await journal.EcrireAsync(new ErreurAEcrire(suivi, NiveauxErreur.Grave, OriginesErreur.Ecran, e.CodeGreffe,
                    ServiceIdentite.CompteDe(h.User), string.IsNullOrWhiteSpace(e.Poste) ? "(poste inconnu)" : e.Poste,
                    string.IsNullOrWhiteSpace(e.Programme) ? "(ecran)" : e.Programme, e.Type, e.Message ?? string.Empty, e.Pile,
                    DateTime.UtcNow), ct);
                return Results.Ok(new SuiviErreur(suivi, JournalErreurs.PhraseAgent(suivi)));
            })
            .RequireAuthorization();

        routes.MapGet(RoutesSupervision.Erreurs, async (string? suivi, string? greffe, string? compte, string? programme, string? du, string? au,
                                                        JournalErreurs journal, CancellationToken ct) =>
            {
                if (!Date(du, out var debut) || !Date(au, out var fin))
                    return Results.BadRequest(new RefusGreffe("Dates attendues au format yyyy-MM-dd."));
                return Results.Ok(await journal.RechercherAsync(suivi, greffe, compte, programme, debut, fin, ct));
            })
            .RequireAuthorization(amitel);

        // SOC-017 CA02 : tout le parcours d'un identifiant de suivi, dans l'ordre ou il s'est deroule.
        routes.MapGet(RoutesSupervision.Parcours, async (string suivi, JournalErreurs journal, CancellationToken ct) =>
            {
                if (Suivis.Normaliser(suivi) is null)
                    return Results.BadRequest(new RefusGreffe($"« {suivi} » n'est pas un identifiant de suivi (forme attendue : 7KQ2-M9XP)."));
                var lignes = await journal.RechercherAsync(suivi, null, null, null, null, null, ct);
                return Results.Ok(lignes.OrderBy(l => l.Instant).ThenBy(l => l.Id).ToArray());
            })
            .RequireAuthorization(amitel);

        routes.MapGet(RoutesSupervision.Etat, async (Surveillant surveillant, CancellationToken ct) => Results.Ok(await surveillant.LireAsync(ct)))
            .RequireAuthorization(amitel);

        // SOC-011 CA04 : la relance, tracee au journal des gestes de la base commune avec son auteur, dans la transaction
        // qui remet en attente ce que le traitement laissait « en cours ».
        routes.MapPost(RoutesSupervision.Relancer, async (DemandeRelance d, HttpContext h, BaseRig b, ServiceDroits droits,
                                                          MoteurTraitements moteur, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(d.Traitement) || !moteur.Connait(d.Traitement))
                    return Results.NotFound(new RefusGreffe($"Le traitement « {d.Traitement} » ne tourne pas sur ce serveur."));
                var auteur = (await droits.AppelantAsync(h, b, ct))!;
                var resultat = await moteur.RelancerAsync(d.Traitement, (cn, tx, r) => JournalGestes.EcrireAsync(cn, tx, auteur, "Supervision",
                    "supervision", $"le traitement {r.Traitement}", null, "a modifie",
                    new { etat = r.EtatAvant, enFile = r.EnFile }, new { etat = r.EtatApres, relance = true }, ct), ct);
                return resultat is null
                    ? Results.Conflict(new RefusGreffe($"Le traitement « {d.Traitement} » n'est pas arrêté : rien à relancer."))
                    : Results.Ok(resultat);
            })
            .RequireAuthorization(amitel);
    }

    private static bool Date(string? texte, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(texte)) return true;
        if (!DateOnly.TryParseExact(texte.Trim(), "yyyy-MM-dd", out var d)) return false;
        date = d;
        return true;
    }
}

/// <summary>L'exigence de <see cref="PolitiquesSupervision.ExploitationAmitel"/>.</summary>
internal sealed class ExigenceExploitationAmitel : IAuthorizationRequirement;

/// <summary>Satisfait <see cref="ExigenceExploitationAmitel"/> quand l'appelant admis (base commune) est marque Amitel.</summary>
internal sealed class ControleExploitationAmitel(ServiceDroits droits, BaseRig b) : AuthorizationHandler<ExigenceExploitationAmitel>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexte, ExigenceExploitationAmitel exigence)
    {
        if (contexte.Resource is not HttpContext http) return;
        Appelant? appelant;
        // Routes communes : la base est la commune ; un greffe inconnu ou muet ne peut pas se presenter ici.
        try { appelant = await droits.AppelantAsync(http, b, http.RequestAborted); }
        catch (GreffeInconnu) { return; }
        catch (BaseGreffeInjoignable) { return; }
        if (appelant is { EstAmitel: true }) contexte.Succeed(exigence);
    }
}
