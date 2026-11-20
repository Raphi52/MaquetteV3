using System.Reflection;
using Microsoft.AspNetCore.Routing;

namespace RigV3.Serveur;

// ============================================================
// LE MONTAGE DU SERVEUR (lot 1 du plan de reconstruction, 2026-11-20).
//
// Program.cs ne fait que Construire(args).Run(). Le banc tests/ServeurCheck
// appelle Construire lui aussi, en memoire (TestServer) et sur sa base
// jetable : il essaie le serveur tel qu'il demarre en vrai.
//
// LA DECOUVERTE DES ROUTES. Les routes vivent dans des modules
// (IModuleServeur), un par fichier, ranges par theme du carnet :
// Sante/ModuleSante.cs, Socle/Identite/..., Numerotation/Compteur/...
// Construire les trouve par reflexion dans cet assemblage : deux lots qui
// ajoutent chacun leur module ne touchent aucun fichier commun. Le prix de
// cette liberte -- deux modules qui publieraient la meme route sans le savoir
// -- est paye ici : le demarrage s'arrete en nommant la route et les deux
// modules, au lieu d'une erreur « correspondance ambigue » a la premiere
// requete chez l'utilisateur.
// ============================================================

/// <summary>Le module d'origine d'une route (pour nommer un doublon).</summary>
public sealed record ModuleOrigine(string Nom);

public static class ServeurRig
{
    /// <summary>
    /// Le serveur pret a demarrer : configuration, services, puis les routes de
    /// chaque module.
    /// </summary>
    /// <param name="args">La ligne de commande (--urls, --ConnectionStrings:RigV3=...).</param>
    /// <param name="avant">Reglages du constructeur avant tout module (un banc y pose
    /// UseTestServer et sa base jetable).</param>
    /// <param name="modules">Les modules a monter ; null = ceux que <see cref="Decouvrir"/>
    /// trouve dans le serveur.</param>
    public static WebApplication Construire(string[] args,
                                            Action<WebApplicationBuilder>? avant = null,
                                            IEnumerable<IModuleServeur>? modules = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        avant?.Invoke(builder);

        var montes = (modules ?? Decouvrir(typeof(ServeurRig).Assembly)).ToList();
        builder.Services.AddSingleton(sp =>
            new BaseRig(sp.GetRequiredService<IConfiguration>().GetConnectionString(BaseRig.NomConnexion)));
        foreach (var module in montes) module.Inscrire(builder.Services, builder.Configuration);

        var app = builder.Build();
        foreach (var module in montes)
        {
            var nom = module.GetType().FullName ?? module.GetType().Name;
            module.Brancher(app.MapGroup(string.Empty).WithMetadata(new ModuleOrigine(nom)));
        }
        VerifierRoutesUniques(app);
        return app;
    }

    /// <summary>
    /// Les modules d'un assemblage : toute classe concrete qui implemente
    /// <see cref="IModuleServeur"/>, dans l'ordre de son nom complet (le meme a
    /// chaque demarrage).
    /// </summary>
    public static IReadOnlyList<IModuleServeur> Decouvrir(Assembly assemblage)
        => [.. assemblage.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModuleServeur).IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IModuleServeur)(Activator.CreateInstance(t, nonPublic: true)
                    ?? throw new InvalidOperationException($"Module {t.FullName} : impossible de le creer.")))];

    /// <summary>Leve si deux routes ont la meme adresse et le meme verbe.</summary>
    private static void VerifierRoutesUniques(IEndpointRouteBuilder app)
    {
        var routes = app.DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>();
        var doublons = routes
            .SelectMany(r => (r.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(verbe => (Cle: $"{verbe} /{(r.RoutePattern.RawText ?? "").Trim('/').ToLowerInvariant()}",
                                  Module: r.Metadata.GetMetadata<ModuleOrigine>()?.Nom ?? "(hors module)")))
            .GroupBy(x => x.Cle)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} publiee par {string.Join(" et ", g.Select(x => x.Module))}")
            .ToList();
        if (doublons.Count > 0)
            throw new InvalidOperationException("Routes en double : " + string.Join(" ; ", doublons) + ".");
    }
}
