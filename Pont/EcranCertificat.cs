using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Ce que l'ecran « Certificat NF525 de la V3 » affiche.</summary>
/// <param name="Mode">« serveur » (le certificat enregistre par Amitel), « demo » (sans RIGV3_SERVEUR), « sans-greffe »
/// (aucun greffe ouvert par la session), « refuse » (403) ou « erreur » (serveur ou base injoignable).</param>
/// <param name="Message">La raison d'un refus ou d'une panne ; en demonstration, ce que l'ecran ne peut pas dire.</param>
/// <param name="Etat">Le certificat et ce qu'il vaut aujourd'hui ; null en cas de refus ou de panne.</param>
public sealed record VueEcranCertificat(string Mode, string? Message, EtatCertificat? Etat);

/// <summary>
/// Pont de l'ecran « Certificat NF525 de la V3 » (web/src/ecrans/EcranCertificat.tsx), SOC-019 CA04 et T04 : ecran NEUF,
/// « a maquetter » dans le carnet (la V2 n'en a pas : son certificat circule en piece jointe, un greffe le demande a
/// l'assistance). Un greffe passe a la V3 y retrouve le certificat de la V3 -- organisme accredite, accreditation, numero,
/// referentiel, version couverte, dates de delivrance et de validite -- et ce qu'il vaut aujourd'hui pour la version en
/// service, sans passer par l'assistance. Tout vient du serveur (GET RoutesCertification.Certificat) : le certificat est
/// enregistre une fois par Amitel dans la base commune, la regle de validite est celle du demarrage de la beta.
/// Sans certificat enregistre, l'ecran dit « certification en cours » ; il n'en montre JAMAIS un faux, pas meme en
/// demonstration (sans RIGV3_SERVEUR : la meme phrase, et pourquoi l'ecran ne peut pas en dire plus).
/// </summary>
public static class EcranCertificat
{
    /// <summary>Le nom de l'ecran (menu, catalogue des ecrans).</summary>
    public const string NomEcran = "Certificat NF525 de la V3";

    /// <summary>Le code de l'ecran au menu (sans fiche V2 : MenuGestesCheck le range dans ses ecrans neufs).</summary>
    public const string Code = "PROC_CERTIFICAT";

    [JSInvokable("EcranCertificat.Ouvrir")]
    public static Task<VueEcranCertificat> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Le certificat de la V3, lu sur <paramref name="serveur"/> pour le greffe <paramref name="greffe"/>.</summary>
    public static async Task<VueEcranCertificat> OuvrirAsync(ClientServeur? serveur, string? greffe)
    {
        if (serveur is null) return Demonstration();
        if (string.IsNullOrWhiteSpace(greffe))
            return new("sans-greffe", "Aucun greffe n'est ouvert dans cette session : le certificat se lit pour le greffe choisi à l'ouverture de RIG.", null);
        try
        {
            return new("serveur", null, await serveur.LireAsync<EtatCertificat>(RoutesCertification.Pour(greffe)));
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return new("refuse", $"Vous n'avez pas accès à la V3 du greffe {greffe} : le certificat ne se lit pas.", null);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return new("erreur", e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.", null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return new("erreur", $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).", null);
        }
    }

    /// <summary>
    /// Sans serveur, aucun certificat ne peut etre lu : l'ecran dit l'etat vrai de ce poste -- aucun certificat enregistre --
    /// avec la regle de la V3, et la version que porte cet ecran. Jamais un certificat d'exemple.
    /// </summary>
    private static VueEcranCertificat Demonstration()
    {
        var aujourdhui = DateOnly.FromDateTime(DateTime.Today);
        var (etat, motifs) = RegleCertificat.Juger(null, aujourdhui, EcranSignalement.Version);
        return new("demo", "Démonstration : sans serveur de RIG, le certificat enregistré par Amitel ne se lit pas ; l'écran n'en montre aucun d'exemple.",
            new EtatCertificat(null, etat, null, EcranSignalement.Version, aujourdhui, motifs, null));
    }
}
