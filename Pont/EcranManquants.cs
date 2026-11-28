using System.Net.Http;
using System.Text.Json;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Contrats.Numerotation;

namespace RigV3Desktop.Pont;

/// <summary>Ce que l'ecran « Numéros manquants » affiche.</summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403 : pas LNUMCM) ou « erreur ».</param>
/// <param name="Message">La raison d'un refus ou d'une panne ; null sinon.</param>
/// <param name="Greffe">Le code du greffe dont la liste est montree ; null en demonstration.</param>
public sealed record VueEcranManquants(
    string Mode,
    string? Message,
    string? Greffe,
    NumeroManquant[] Manquants);

/// <summary>
/// Pont de l'ecran « Numéros manquants » (web/src/ecrans/EcranManquants.tsx), NUM-013 T04 : l'edition de la liste des
/// numeros manquants du registre, avec le motif de chacun. Reprend la procedure V2 LNUMCM (« Liste des numeros chrono
/// manquants », roles RCSNIV01 et RCSNIV05) ; la route exige la meme fonction. La liste vient de la vue
/// num.NumeroManquant (lot 6) : les manquants repris de la V2 et ceux declares par la V3, sans ceux rendus depuis.
/// Le greffe : celui de la session (<see cref="SessionAgent.CodeGreffe"/>, lot 10).
///
/// LE GESTE (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026) : « Éditer la liste », l'etape ALNUMCM
/// de LNUMCM -- <see cref="CreerEditionAsync"/>, ecrit par le serveur (Serveur/Socle/EcransMain/EcritureManquants.cs : la demande LNUMCM
/// et sa ligne d'edition a imprimer), reserve a la fonction LNUMCM. Sans serveur, rien n'est ecrit (« Démonstration »).
/// </summary>
public static class EcranManquants
{
    public const string NomEcran = "Numéros manquants";

    [JSInvokable("EcranManquants.Editer")]
    public static Task<ResultatGeste> Editer() => CreerEditionAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>La liste a imprimer, sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration » ;
    /// sans la fonction LNUMCM, le refus du serveur. Le resultat porte la demande creee (Ids).</summary>
    public static async Task<ResultatGeste> CreerEditionAsync(ClientServeur? serveur, string? greffe)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        try
        {
            return await serveur.EnvoyerAsync<GesteEcran, ResultatGeste>(RoutesEcransMain.Pour(RoutesManquants.Editer, greffe),
                new GesteEcran(TypeGeste.Agir, [], new Dictionary<string, string>()) { Nom = "Éditer la liste" });
        }
        catch (ErreurServeur e) when (e.CodeHttp is 403 or 422 && LireResultat(e.Corps) is { } refus)
        {
            return refus;
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new ResultatGeste(false, $"Rien n'est enregistré : le serveur de RIG ne répond pas ({serveur.Adresse}, " +
                                            $"{(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).");
        }
    }

    private static ResultatGeste? LireResultat(string? corps)
    {
        if (string.IsNullOrWhiteSpace(corps)) return null;
        try { return JsonSerializer.Deserialize<ResultatGeste>(corps, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return null; }
    }

    [JSInvokable("EcranManquants.Ouvrir")]
    public static Task<VueEcranManquants> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>La liste du greffe <paramref name="greffe"/> lue sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranManquants> OuvrirAsync(ClientServeur? serveur, string? greffe)
    {
        if (serveur is null) return Demonstration();
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : la liste se lit dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var liste = await serveur.LireAsync<NumeroManquant[]>(RoutesCompteur.Pour(RoutesCompteur.Manquants, greffe));
            return new VueEcranManquants("serveur", null, greffe, liste);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return Vide("refuse", greffe, Motif(e) ??
                $"Vous n'avez pas accès à la liste des numéros manquants : il faut la fonction {RoutesCompteur.FonctionNumerosManquants}.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Vide("erreur", greffe, e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return Vide("erreur", greffe, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).");
        }
    }

    /// <summary>Le motif d'un refus du serveur (le Message de son ResultatRestitution) ; null si le corps n'en porte pas.</summary>
    private static string? Motif(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<ResultatRestitution>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Message; }
        catch (JsonException) { return null; }
    }

    private static VueEcranManquants Vide(string mode, string? greffe, string message) => new(mode, message, greffe, []);

    /// <summary>Sans serveur : les 7 numeros de RIG_DEV.dbo.NUMERO_CHRONO_MANQUANT (lus le 21/11/2026), tels que la reprise les ecrit.</summary>
    private static VueEcranManquants Demonstration()
        => new("demo", null, null,
        [
            Demo(2024, 8948), Demo(2024, 8951), Demo(2024, 8955), Demo(2024, 8956), Demo(2024, 8957),
            Demo(2025, 14518), Demo(2025, 18415),
        ]);

    private static NumeroManquant Demo(int annee, int chrono)
        => new("RC1", annee, 0, chrono, $"F{annee % 100:00}/{chrono:000000}", "manquant constaté par la V2, motif inconnu", "V2",
               new DateTime(2026, 11, 20, 18, 0, 0));
}
