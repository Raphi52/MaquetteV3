using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.Identite;

/// <summary>Les politiques d'acces du socle, a poser sur une route : <c>.RequireAuthorization(PolitiquesSocle.AgentAdmis)</c>.</summary>
public static class PolitiquesSocle
{
    /// <summary>L'appelant est un agent admis (SOC-001) : compte connu, avec un role, actif, avec un service.
    /// Sinon : 401 sans session Windows, 403 pour un compte refuse ; la route ne s'execute pas.
    /// Sur une route dont l'adresse porte <c>{greffe}</c> (<see cref="ParametreGreffe"/>), l'agent est juge
    /// dans la base de CE greffe (lot 8) : 404 si le greffe n'est pas au catalogue, 503 si sa base ne
    /// repond pas, sans repli ; sur une route sans greffe, dans la base commune.</summary>
    public const string AgentAdmis = "agent-admis";

    /// <summary>Le parametre d'adresse qui nomme le greffe ou l'agent travaille (<c>/numerotation/{greffe}/numeros</c>).
    /// Le <c>{code}</c> des routes du catalogue (/socle/greffes/{code}) n'en est pas un : il designe la fiche
    /// geree depuis la base commune, qui existe avant la base V3 du greffe (SOC-008).</summary>
    public const string ParametreGreffe = "greffe";
}

/// <summary>
/// SOC-001 : ouvrir RIG avec sa session Windows, sans mot de passe.
///
/// L'AUTHENTIFICATION du serveur entier s'inscrit ici (recette 2 de
/// docs/reconstruction/architecture.md) ; aucune route ne l'exige d'office :
/// /sante reste ouverte, une route de donnees pose <see cref="PolitiquesSocle.AgentAdmis"/>.
///
/// Le schema par defaut, « session », choisit a chaque requete :
///  - en PRODUCTION (et partout par defaut) : la session Windows, par l'authentification
///    integree (Negotiate : Kerberos ou NTLM, celle que la session RDS transmet) ;
///  - en DEVELOPPEMENT et en RECETTE seulement, une requete qui porte l'en-tete
///    <see cref="RoutesIdentite.EnTeteCompte"/> est l'appel de CE compte : le pendant du
///    parametre « utilisateur= » de la V2. En production l'en-tete est ignore (CA07).
/// </summary>
internal sealed class ModuleIdentite : IModuleServeur
{
    /// <summary>Le schema par defaut, qui aiguille vers l'un des deux suivants.</summary>
    internal const string SchemaSession = "session";

    /// <summary>Le changement de compte du developpement et de la recette.</summary>
    internal const string SchemaChangementDeCompte = "changement-de-compte";

    /// <summary>Nom de l'environnement de recette (ASPNETCORE_ENVIRONMENT=Recette).</summary>
    internal const string EnvironnementRecette = "Recette";

    public void Inscrire(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ServiceIdentite>();
        services.AddAuthentication(SchemaSession)
            .AddPolicyScheme(SchemaSession, "Session de l'agent", o => o.ForwardDefaultSelector =
                contexte => ChangementDeComptePermis(contexte) && contexte.Request.Headers.ContainsKey(RoutesIdentite.EnTeteCompte)
                    ? SchemaChangementDeCompte
                    : NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate()
            .AddScheme<AuthenticationSchemeOptions, GestionnaireChangementDeCompte>(SchemaChangementDeCompte, null);
        // Negotiate ne sait travailler que sur un serveur a connexions (Kestrel) ; sur le
        // serveur en memoire des bancs il leverait a CHAQUE requete, /sante comprise.
        services.Configure<AuthenticationOptions>(o =>
            o.SchemeMap[NegotiateDefaults.AuthenticationScheme].HandlerType = typeof(GestionnaireSessionWindows));

        services.AddAuthorization(o => o.AddPolicy(PolitiquesSocle.AgentAdmis,
            p => p.RequireAuthenticatedUser().AddRequirements(new ExigenceAgentAdmis())));
        services.AddSingleton<IAuthorizationHandler, ControleAgentAdmis>();
        // Le refus d'une route dont le greffe ne s'ouvre pas : 404 / 503 au lieu du 403 d'office.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ReponseGreffeNonOuvert>();
    }

    public void Brancher(IEndpointRouteBuilder routes)
    {
        // RequireAuthorization : sans session transmise, 401 « Negotiate » ; le client
        // de l'ecran (credentials de la session) refait alors la demande authentifiee.
        // Les deux adresses ont le meme corps : DeLAppelantAsync lit le {greffe} de l'adresse.
        routes.MapGet(RoutesIdentite.Identite, Identite).RequireAuthorization();
        routes.MapGet(RoutesIdentite.IdentiteDuGreffe, Identite).RequireAuthorization();
    }

    private static async Task<IResult> Identite(HttpContext contexte, BaseRig b, ServiceIdentite identites, CancellationToken ct)
    {
        try { return Results.Ok(await identites.DeLAppelantAsync(contexte, b, ct)); }
        catch (GreffeInconnu e) { return ReponseGreffeNonOuvert.Pour(e); }
        catch (BaseGreffeInjoignable e) { return ReponseGreffeNonOuvert.Pour(e); }
    }

    /// <summary>Le changement de compte n'existe qu'en developpement et en recette (CA07).</summary>
    internal static bool ChangementDeComptePermis(HttpContext contexte)
    {
        var environnement = contexte.RequestServices.GetRequiredService<IHostEnvironment>();
        return environnement.IsDevelopment() || environnement.IsEnvironment(EnvironnementRecette);
    }
}

/// <summary>
/// Negotiate, mais seulement la ou une session Windows peut arriver : sur une
/// connexion (Kestrel). Le serveur en memoire des bancs (TestServer) n'a pas de
/// connexion, donc pas de poignee de main Kerberos/NTLM possible : la requete y
/// reste anonyme, au lieu de lever « Negotiate authentication requires a server
/// that supports IConnectionItemsFeature » sur toutes les routes.
/// </summary>
internal sealed class GestionnaireSessionWindows(IOptionsMonitor<NegotiateOptions> options, ILoggerFactory journal, UrlEncoder encodeur)
    : NegotiateHandler(options, journal, encodeur), IAuthenticationRequestHandler
{
    private bool SansConnexion => Context.Features.Get<IConnectionItemsFeature>() is null;

    Task<bool> IAuthenticationRequestHandler.HandleRequestAsync()
        => SansConnexion ? Task.FromResult(false) : base.HandleRequestAsync();

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        => SansConnexion ? Task.FromResult(AuthenticateResult.NoResult()) : base.HandleAuthenticateAsync();
}

/// <summary>Le compte nomme par l'en-tete <see cref="RoutesIdentite.EnTeteCompte"/> ; rien en production.</summary>
internal sealed class GestionnaireChangementDeCompte(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory journal, UrlEncoder encodeur)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, journal, encodeur)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Deuxieme verrou, si un jour ce schema etait choisi autrement que par le selecteur.
        if (!ModuleIdentite.ChangementDeComptePermis(Context)) return Task.FromResult(AuthenticateResult.NoResult());
        var compte = Request.Headers[RoutesIdentite.EnTeteCompte].ToString().Trim();
        if (compte.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        var identite = new ClaimsIdentity([new Claim(ClaimTypes.Name, compte)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identite), Scheme.Name)));
    }
}

/// <summary>L'exigence de <see cref="PolitiquesSocle.AgentAdmis"/>.</summary>
internal sealed class ExigenceAgentAdmis : IAuthorizationRequirement;

/// <summary>Satisfait <see cref="ExigenceAgentAdmis"/> quand l'appelant passe les quatre controles de la V2,
/// dans la base du greffe de l'adresse (<see cref="ServiceIdentite.GreffeDe"/>) ou, sans greffe, dans la base commune.</summary>
internal sealed class ControleAgentAdmis(ServiceIdentite identites, BaseRig b) : AuthorizationHandler<ExigenceAgentAdmis>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext contexte, ExigenceAgentAdmis exigence)
    {
        if (contexte.Resource is not HttpContext http) return;
        IdentiteAgent? identite;
        try { identite = await identites.DeLAppelantAsync(http, b, http.RequestAborted); }
        catch (GreffeInconnu e) { contexte.Fail(new GreffeNonOuvert(this, ReponseGreffeNonOuvert.Pour(e))); return; }
        catch (BaseGreffeInjoignable e) { contexte.Fail(new GreffeNonOuvert(this, ReponseGreffeNonOuvert.Pour(e))); return; }
        if (identite is { Admis: true }) contexte.Succeed(exigence);
    }
}

/// <summary>Le refus porte par <see cref="ControleAgentAdmis"/> quand la base du greffe de l'adresse ne s'ouvre
/// pas : la reponse a rendre a la place du 403.</summary>
internal sealed class GreffeNonOuvert(IAuthorizationHandler controle, IResult reponse)
    : AuthorizationFailureReason(controle, "la base du greffe de l'adresse ne s'ouvre pas")
{
    public IResult Reponse { get; } = reponse;
}

/// <summary>
/// La reponse d'une route refusee. Un greffe absent du catalogue rend 404, une base de greffe muette 503,
/// avec leur cause et sans repli (decision du lot 4) ; tout autre refus suit ASP.NET Core (401 sans session,
/// 403 pour un compte refuse). Le serveur n'a qu'un traitement de ce type : celui-ci delegue le reste au
/// traitement d'origine (https://learn.microsoft.com/en-us/aspnet/core/security/authorization/customizingauthorizationmiddlewareresponse).
/// </summary>
internal sealed class ReponseGreffeNonOuvert : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler parDefaut = new();

    public Task HandleAsync(RequestDelegate suite, HttpContext contexte, AuthorizationPolicy politique, PolicyAuthorizationResult resultat)
        => resultat.AuthorizationFailure?.FailureReasons.OfType<GreffeNonOuvert>().FirstOrDefault() is { } refus
            ? refus.Reponse.ExecuteAsync(contexte)
            : parDefaut.HandleAsync(suite, contexte, politique, resultat);

    /// <summary>404 : le greffe de l'adresse n'est pas au catalogue commun.</summary>
    public static IResult Pour(GreffeInconnu e) => Results.Json(new RefusGreffe(e.Message), statusCode: StatusCodes.Status404NotFound);

    /// <summary>503 : la base V3 du greffe ne repond pas ; aucune autre base n'est lue a sa place.</summary>
    public static IResult Pour(BaseGreffeInjoignable e) => Results.Json(new RefusGreffe(e.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
}
