using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Comptes;
using RigV3.Serveur.Socle.Greffes;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Preferences;

/// <summary>
/// SOC-013 : retrouver ses preferences et ses documents sur n'importe quel serveur RDS. Cinq routes, toutes sur la
/// base V3 du greffe de l'adresse et pour l'agent de la session Windows seulement (<see cref="PolitiquesSocle.AgentAdmis"/>,
/// posee une fois sur leur groupe : 401, 403, 404, 503 avant que la route ne tourne).
///
///  - GET / PUT <see cref="RoutesPreferences.Preferences"/> : ses preferences (CA01, CA05) ;
///  - POST <see cref="RoutesPreferences.Session"/> : sa V3 s'ouvre ici, les documents qui l'attendaient lui sont remis (CA04) ;
///  - GET <see cref="RoutesPreferences.Documents"/> : la releve de sa session (CA03) ;
///  - GET <see cref="RoutesPreferences.Document"/> : le contenu d'un de SES documents, que l'ecran ouvre (T03) : le fichier
///    est sur le serveur, pas sur le serveur RDS.
///
/// Aucun parametre ne designe un autre agent : l'identifiant du compte est celui que la base du greffe donne a la
/// session Windows (<see cref="ServiceDroits.AppelantAsync(HttpContext, BaseRig, SqlConnection, CancellationToken)"/>).
/// Le depot d'un document n'est pas une route : c'est la partie serveur qui le fait (<see cref="DocumentsAgents.DeposerAsync"/>),
/// comme le moteur d'editions de la V2 ; aucun agent ne pose de document chez un autre.
/// </summary>
internal sealed class ModulePreferences : IModuleServeur
{
    /// <summary>Longueur des colonnes Serveur et Session (soc.SessionAgent).</summary>
    private const int LongueurSession = 64;

    public void Brancher(IEndpointRouteBuilder routes)
    {
        var groupe = routes.MapGroup(string.Empty).RequireAuthorization(PolitiquesSocle.AgentAdmis);

        groupe.MapGet(RoutesPreferences.Preferences, (string greffe, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                      BaseRig commune, CancellationToken ct) =>
            PourLAgent(http, c, droits, commune, greffe, ct, async (cn, id) =>
            {
                var (valeurs, serveur) = await ServicePreferences.LireAsync(cn, id, ct);
                return Results.Ok(new PreferencesAgent(valeurs, serveur));
            }));

        groupe.MapPut(RoutesPreferences.Preferences, (string greffe, PreferencesAgent envoi, HttpContext http, CatalogueGreffes c,
                                                      ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (ServicePreferences.Refus(envoi?.Valeurs) is { } motif)
                return Task.FromResult(Results.BadRequest(new RefusGreffe(motif)));
            return PourLAgent(http, c, droits, commune, greffe, ct, async (cn, id) =>
            {
                await ServicePreferences.EcrireAsync(cn, id, envoi!.Valeurs, envoi.Serveur, ct);
                var (valeurs, serveur) = await ServicePreferences.LireAsync(cn, id, ct);
                return Results.Ok(new PreferencesAgent(valeurs, serveur));
            });
        });

        groupe.MapPost(RoutesPreferences.Session, (string greffe, OuvertureSessionAgent ouverture, HttpContext http, CatalogueGreffes c,
                                                   ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (RefusSession(ouverture?.Serveur, ouverture?.Session) is { } motif)
                return Task.FromResult(Results.BadRequest(new RefusGreffe(motif)));
            return PourLAgent(http, c, droits, commune, greffe, ct, async (cn, id) =>
                Results.Ok(await DocumentsAgents.OuvrirSessionAsync(cn, id, ouverture! with
                {
                    Serveur = ouverture.Serveur.Trim(), Session = ouverture.Session.Trim(),
                }, ct)));
        });

        groupe.MapGet(RoutesPreferences.Documents, (string greffe, string? serveur, string? session, HttpContext http, CatalogueGreffes c,
                                                    ServiceDroits droits, BaseRig commune, CancellationToken ct) =>
        {
            if (RefusSession(serveur, session) is { } motif)
                return Task.FromResult(Results.BadRequest(new RefusGreffe(motif)));
            return PourLAgent(http, c, droits, commune, greffe, ct, async (cn, id) =>
                Results.Ok(await DocumentsAgents.ReleverAsync(cn, id, serveur!.Trim(), session!.Trim(), ct)));
        });

        groupe.MapGet(RoutesPreferences.Document, (string greffe, int id, HttpContext http, CatalogueGreffes c, ServiceDroits droits,
                                                   BaseRig commune, CancellationToken ct) =>
            PourLAgent(http, c, droits, commune, greffe, ct, async (cn, moi) =>
            {
                // Le document d'un autre agent n'existe pas pour lui : la meme reponse qu'un numero inconnu.
                if (await DocumentsAgents.FichierAsync(cn, moi, id, ct) is not { } fichier)
                    return Results.NotFound(new RefusGreffe($"Aucun document n° {id} pour vous dans ce greffe."));
                if (!File.Exists(fichier))
                    return Results.NotFound(new RefusGreffe($"Le fichier du document n° {id} n'est plus sur le serveur."));
                return Results.Ok(new ContenuDocument(id, Path.GetFileName(fichier), await File.ReadAllBytesAsync(fichier, ct)));
            }));
    }

    private static string? RefusSession(string? serveur, string? session)
        => string.IsNullOrWhiteSpace(serveur) || string.IsNullOrWhiteSpace(session)
            ? "Le serveur RDS et la session sont obligatoires."
            : serveur.Trim().Length > LongueurSession || session.Trim().Length > LongueurSession
                ? $"Le serveur RDS et la session tiennent en {LongueurSession} caracteres."
                : null;

    /// <summary>
    /// Le travail d'une route, sur la base V3 du greffe <paramref name="greffe"/>, pour le compte de la session Windows
    /// dans CETTE base. Si la base ne s'ouvre plus entre la politique et la route : la meme reponse que la politique
    /// (404 / 503), jamais une autre base.
    /// </summary>
    private static async Task<IResult> PourLAgent(HttpContext http, CatalogueGreffes c, ServiceDroits droits, BaseRig commune, string greffe,
                                                  CancellationToken ct, Func<SqlConnection, int, Task<IResult>> travail)
    {
        SqlConnection cn;
        try { cn = await c.OuvrirBaseV3Async(greffe, ct); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
        await using (cn)
        {
            // La politique AgentAdmis vient d'admettre la session dans cette base : un appelant absent ici est une
            // incoherence (compte supprime entre-temps), refusee comme un compte inconnu.
            if (await droits.AppelantAsync(http, commune, cn, ct) is not { } moi) return Results.Forbid();
            return await travail(cn, moi.Id);
        }
    }
}
