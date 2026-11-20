// fix-ok: CS0246 HttpClient mesure par compiler.ps1 -- un projet WPF n'importe pas System.Net.Http d'office.
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RigV3.Contrats.Sante;

namespace RigV3Desktop.Pont;

// ============================================================
// LE CLIENT DU SERVEUR V3 (lot 1 du plan de reconstruction, 2026-11-20).
//
// L'ecran sait d'ou viennent ses donnees par UNE variable d'environnement :
//   - RIGV3_SERVEUR absente (cas d'aujourd'hui) : ClientServeur.Actif vaut null
//     et l'ecran garde ses donnees de demonstration (Metier/JeuDemo.cs) ; rien
//     ne change, aucune connexion n'est tentee ;
//   - RIGV3_SERVEUR=http://serveur:5080 : Actif appelle ce serveur (Serveur/).
//
// Le geste dans un pont d'ecran (Pont/EcranX.cs) :
//     if (ClientServeur.Actif is { } serveur)
//         return await serveur.LireAsync<ListeComptes>(RoutesComptes.Liste);
//     return JeuDemo.Comptes;
//
// Les types echanges viennent de Contrats/ (reference aussi par le serveur) ;
// le JSON suit les memes regles que le serveur et que le pont de la page
// (JsonSerializerDefaults.Web : camelCase a l'aller, sans casse au retour).
// Ce fichier ne depend que de Contrats/ : le banc tests/ServeurCheck le
// compile seul, contre le vrai serveur demarre en memoire.
// ============================================================

/// <summary>Une reponse du serveur en echec : le verbe, la route, le code HTTP et le debut du corps.</summary>
public sealed class ErreurServeur(string message, int codeHttp) : Exception(message)
{
    public int CodeHttp { get; } = codeHttp;
}

public sealed class ClientServeur
{
    /// <summary>La variable d'environnement qui branche l'ecran sur le serveur.</summary>
    public const string Variable = "RIGV3_SERVEUR";

    private static readonly Lazy<ClientServeur?> Courant =
        new(() => Depuis(Environment.GetEnvironmentVariable(Variable)));

    /// <summary>Le serveur de cette session, lu une fois dans <see cref="Variable"/> ;
    /// null quand elle n'est pas posee : l'ecran reste sur ses donnees de demonstration.</summary>
    public static ClientServeur? Actif => Courant.Value;

    /// <summary>
    /// Le client d'une adresse : null si elle est vide (pas de serveur). Une adresse
    /// posee mais illisible est une erreur de configuration, pas un retour discret
    /// aux donnees de demonstration : elle leve, en citant la variable.
    /// </summary>
    public static ClientServeur? Depuis(string? adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse)) return null;
        if (!Uri.TryCreate(adresse.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"{Variable} = « {adresse} » : adresse illisible. Attendu : http(s)://serveur:port (ex. http://127.0.0.1:5080).");
        return new ClientServeur(uri);
    }

    /// <summary>Les regles JSON du serveur (ASP.NET Core) et de Repartiteur.Json.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient Http;

    /// <param name="adresse">Racine du serveur.</param>
    /// <param name="gestionnaire">Transport HTTP ; null = le reseau. Un banc y passe
    /// le serveur en memoire (TestServer).</param>
    public ClientServeur(Uri adresse, HttpMessageHandler? gestionnaire = null)
    {
        // Une racine avec un chemin (http://h/rig) doit finir par « / », sinon
        // « sante » remplacerait « rig » au lieu de s'y ajouter.
        Adresse = adresse.AbsoluteUri.EndsWith('/') ? adresse : new Uri(adresse.AbsoluteUri + "/");
        Http = gestionnaire is null ? new HttpClient() : new HttpClient(gestionnaire, disposeHandler: false);
        Http.BaseAddress = Adresse;
        // Un agent attend devant son ecran : au-dela, mieux vaut un message qu'un sablier.
        Http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>Le serveur appele.</summary>
    public Uri Adresse { get; }

    /// <summary>GET <paramref name="route"/> ; leve <see cref="ErreurServeur"/> sur un code d'echec.</summary>
    public async Task<T> LireAsync<T>(string route, CancellationToken ct = default)
    {
        using var reponse = await Http.GetAsync(Relative(route), ct);
        return await Lire<T>(reponse, "GET", route, ct);
    }

    /// <summary>POST <paramref name="route"/> avec <paramref name="demande"/> en JSON ;
    /// leve <see cref="ErreurServeur"/> sur un code d'echec.</summary>
    public async Task<TReponse> EnvoyerAsync<TDemande, TReponse>(string route, TDemande demande, CancellationToken ct = default)
    {
        using var reponse = await Http.PostAsJsonAsync(Relative(route), demande, Json, ct);
        return await Lire<TReponse>(reponse, "POST", route, ct);
    }

    /// <summary>L'etat du serveur et de sa base. Un 503 n'est pas une exception ici :
    /// son corps dit la cause (base non configuree, injoignable), c'est la reponse.</summary>
    public async Task<EtatSante> SanteAsync(CancellationToken ct = default)
    {
        using var reponse = await Http.GetAsync(Relative(RoutesSante.Sante), ct);
        if (reponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            return await reponse.Content.ReadFromJsonAsync<EtatSante>(Json, ct)
                   ?? throw new ErreurServeur($"GET {RoutesSante.Sante} : 503 sans corps", 503);
        return await Lire<EtatSante>(reponse, "GET", RoutesSante.Sante, ct);
    }

    /// <summary>« /sante » sous la racine du serveur, meme si celle-ci a un chemin (http://h/rig/).</summary>
    private static string Relative(string route) => route.TrimStart('/');

    private static async Task<T> Lire<T>(HttpResponseMessage reponse, string verbe, string route, CancellationToken ct)
    {
        if (!reponse.IsSuccessStatusCode)
        {
            var corps = await reponse.Content.ReadAsStringAsync(ct);
            throw new ErreurServeur(
                $"{verbe} {route} : {(int)reponse.StatusCode} {reponse.ReasonPhrase} {corps[..Math.Min(corps.Length, 300)]}".TrimEnd(),
                (int)reponse.StatusCode);
        }
        try
        {
            return await reponse.Content.ReadFromJsonAsync<T>(Json, ct)
                   ?? throw new ErreurServeur($"{verbe} {route} : reponse vide", (int)reponse.StatusCode);
        }
        catch (JsonException e)
        {
            throw new ErreurServeur($"{verbe} {route} : reponse illisible ({e.Message})", (int)reponse.StatusCode);
        }
    }
}
