using System.Reflection;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Versions;

/// <summary>
/// LES VERSIONS de la partie serveur (lot 12 du plan, 2026-11-21 ; docs/reconstruction/socle-exploitation.md).
/// <list type="bullet">
/// <item>GET /socle/version : la version du serveur et son site (SOC-010 : Amitel, Antilles, Reunion,
/// Polynesie). Le lanceur des serveurs RDS la compare a l'ecran qu'il va ouvrir (SOC-012 CA03), le releve
/// des versions la lit sur chaque site (CA06).</item>
/// <item>GET /socle/greffes/{greffe}/version : la version du serveur et celle que la base V3 du greffe porte
/// (rigv3.Livraison, ecrite par l'outil de livraison) ; si elles different, le refus a montrer, qui nomme
/// les deux (SOC-009 CA05).</item>
/// <item>POST /socle/version/erreurs-demarrage : un ecran qui n'a pas pu demarrer (composant WebView2 absent,
/// version perimee) le dit a l'exploitation, dans le journal du serveur (SOC-012 CA04, T04).</item>
/// </list>
/// Ces routes sont anonymes, comme /sante : le lanceur les appelle avant que l'agent soit juge, et elles ne
/// rendent que des numeros de version.
/// AU DEMARRAGE (SOC-009 T04, CA05) : la partie serveur refuse de demarrer si sa base commune porte une autre
/// version qu'elle, avec un message qui donne les deux (<see cref="ControleVersionAuDemarrage"/>).
/// </summary>
internal sealed class ModuleVersions : IModuleServeur
{
    /// <summary>La version de ce serveur : celle de Directory.Build.props, sans le suffixe de compilation.</summary>
    public static readonly string Version = VersionV3.Normaliser(
        typeof(ModuleVersions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ModuleVersions).Assembly.GetName().Version?.ToString() ?? "0.0.0");

    /// <summary>Le site du serveur (configuration « Site:Nom », posee par le paquet du site, SOC-010).</summary>
    public const string CleSite = "Site:Nom";

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
        => services.AddHostedService<ControleVersionAuDemarrage>();

    public void Brancher(IEndpointRouteBuilder routes)
    {
        routes.MapGet(RoutesVersions.Serveur, (IConfiguration configuration) =>
            Results.Ok(new VersionServeur(Version, Site(configuration))));

        routes.MapGet(RoutesVersions.Greffe, async (string greffe, CatalogueGreffes catalogue, CancellationToken ct) =>
        {
            try
            {
                await using var cn = await catalogue.OuvrirBaseV3Async(greffe, ct);
                var versionBase = await JournalLivraisons.VersionAsync(cn, ct);
                var concordent = versionBase is null || VersionV3.Egales(versionBase, Version);
                return Results.Ok(new VersionsGreffe(greffe, Version, versionBase, concordent,
                    concordent ? null : VersionV3.MessageBase("La partie serveur", Version, cn.Database, versionBase!)));
            }
            catch (GreffeInconnu e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status404NotFound); }
            catch (BaseGreffeInjoignable e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
        });

        routes.MapPost(RoutesVersions.ErreursDemarrage, (ErreurDemarrage erreur, ILogger<ModuleVersions> journal) =>
        {
            // Le journal du serveur est ce que l'exploitation surveille (SOC-011, lot 2) : niveau Erreur.
            journal.LogError("Ecran non demarre sur {Poste} pour {Compte} (greffe {Greffe}, ecran {VersionEcran}, serveur {VersionServeur}) : {Cause}",
                erreur.Poste, erreur.Compte, erreur.Greffe ?? "?", erreur.VersionEcran ?? "?", Version, erreur.Cause);
            return Results.Accepted(value: erreur);   // le corps renvoye dit au lanceur que la remontee est recue
        });
    }

    internal static string Site(IConfiguration configuration)
        => string.IsNullOrWhiteSpace(configuration[CleSite]) ? "developpement" : configuration[CleSite]!.Trim();
}

/// <summary>
/// SOC-009 CA05 : au demarrage, la version du serveur est comparee a celle de sa base commune. Differente :
/// le demarrage s'arrete sur un message qui donne les deux versions (au lieu d'une erreur SQL obscure a la
/// premiere requete). Une base sans livraison inscrite (developpement, bancs) ne bloque rien ; une base qui
/// ne repond pas non plus : le serveur demarre et /sante le dit (503), il repart des que la base revient.
/// </summary>
internal sealed class ControleVersionAuDemarrage(BaseRig commune, ILogger<ControleVersionAuDemarrage> journal) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (commune.Connexion is null) return;
        string? versionBase;
        string nomBase;
        try
        {
            await using var cn = await commune.OuvrirAsync(ct);
            nomBase = cn.Database;
            versionBase = await JournalLivraisons.VersionAsync(cn, ct);
        }
        catch (SqlException e)
        {
            journal.LogError(e, "Version de la base commune non controlee au demarrage : base injoignable ({Cause}).", e.Message);
            return;
        }
        if (versionBase is null)
        {
            journal.LogWarning("Base commune {Base} sans livraison inscrite : version non controlee (developpement).", nomBase);
            return;
        }
        if (!VersionV3.Egales(versionBase, ModuleVersions.Version))
            throw new InvalidOperationException(
                "Demarrage refuse (SOC-009 CA05) : " + VersionV3.MessageBase("La partie serveur", ModuleVersions.Version, nomBase, versionBase));
        journal.LogInformation("Partie serveur {Version}, base commune {Base} a la meme version.", ModuleVersions.Version, nomBase);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
