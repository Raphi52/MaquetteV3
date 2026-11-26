using System.Net.Http;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>
/// La lecture d'un ECRAN FAIT MAIN dans la base du greffe (plan « RIG v3 100 % fonctionnel », lot 17) : la meme pour
/// Demandes, Arrivee du courrier, Teletransmissions, BODACC et les dossiers deja ouverts, sur le modele de
/// Pont/EcranManquants.cs et Pont/EcranGenere.cs.
///
/// Le mode que l'ecran recoit :
///   « serveur »     les lignes viennent de la base V3 du greffe de la session ;
///   « demo »        sans serveur (pas de RIGV3_SERVEUR) : le pont rend la demonstration (Metier/JeuDemo.cs), que l'ecran
///                   marque « exemple » ;
///   « sans-greffe » un serveur, mais aucun greffe ouvert dans la session ;
///   « refuse »      le serveur refuse l'agent dans ce greffe (401 / 403) ;
///   « erreur »      greffe hors catalogue, base muette, serveur absent ou d'avant cette route : le motif est dit.
/// Hors « serveur » et « demo », l'ecran montre le motif et AUCUNE ligne : jamais la demonstration a la place de la base.
/// </summary>
public sealed record LectureEcranMain<T>(string Mode, string? Message, string? Greffe, T? Valeur) where T : class
{
    /// <summary>Vrai quand les lignes viennent de la base du greffe.</summary>
    public bool DuServeur => Mode == "serveur" && Valeur is not null;
}

/// <summary>Lire une vue d'ecran fait main sur le serveur, avec le mode et le motif d'une panne.</summary>
public static class LectureEcransMain
{
    /// <summary>Le mode « demo » : aucun serveur, la demonstration s'affiche, marquee « exemple ».</summary>
    public const string Demo = "demo";

    /// <summary>
    /// <paramref name="lire"/> sur <paramref name="serveur"/> pour le greffe <paramref name="greffe"/> ; null serveur =
    /// « demo ». <paramref name="quoi"/> nomme la liste dans les messages (« les demandes »).
    /// </summary>
    public static async Task<LectureEcranMain<T>> LireAsync<T>(ClientServeur? serveur, string? greffe, string quoi,
                                                               Func<ClientServeur, string, Task<T>> lire) where T : class
    {
        if (serveur is null) return new(Demo, null, null, null);
        if (string.IsNullOrWhiteSpace(greffe))
            return new("sans-greffe",
                $"Aucun greffe n'est ouvert dans cette session : {quoi} se lisent dans la base du greffe choisi à l'ouverture de RIG.",
                null, null);
        try
        {
            return new("serveur", null, greffe, await lire(serveur, greffe));
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return new("refuse", MotifDuRefus(e) ?? $"Vous n'avez pas accès aux données du greffe {greffe}.", greffe, null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new("erreur", Motif(e, serveur, greffe, quoi), greffe, null);
        }
    }

    /// <summary>Pourquoi le serveur n'a pas repondu, en mots d'agent (memes phrases que Pont/EcranGenere.cs).</summary>
    private static string Motif(Exception e, ClientServeur serveur, string greffe, string quoi)
    {
        if (e is not ErreurServeur s)
            return $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {e.GetType().Name}).";
        return s.CodeHttp switch
        {
            // 404 : le greffe absent du catalogue (le corps le dit : RefusGreffe), ou un serveur d'avant cette route.
            404 => MotifDuRefus(s) ?? $"Le serveur de RIG ne sait pas encore lire {quoi} : il date d'avant cette fonction, il faut le relancer.",
            503 => MotifDuRefus(s) ?? $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.",
            _ => $"Le serveur de RIG n'a pas rendu {quoi} ({s.CodeHttp}).",
        };
    }

    /// <summary>Le motif d'un refus (corps { motif : ... } : greffe inconnu, base muette) ; null sinon.</summary>
    private static string? MotifDuRefus(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }
}
