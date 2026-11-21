using System.Net.Http;
using RigV3.Contrats.Numerotation;

namespace RigV3Desktop.Pont;

/// <summary>
/// Ce que l'ecran « Compteurs de numéros » affiche.
/// </summary>
/// <param name="Mode">« serveur » (base V3 du greffe), « demo » (sans RIGV3_SERVEUR), « sans-greffe »
/// (branchee au serveur, mais aucun greffe ouvert par la session), « refuse » (403) ou « erreur »
/// (serveur, greffe ou base injoignable).</param>
/// <param name="Message">Le compte rendu de la derniere restitution, ou la raison d'un refus ; null sinon.</param>
/// <param name="Echec">Le message est un refus ou une erreur (filet rouge), pas un numero rendu.</param>
/// <param name="Greffe">Le code du greffe dont les compteurs sont montres ; null en demonstration.</param>
public sealed record VueEcranCompteurs(
    string Mode,
    string? Message,
    bool Echec,
    string? Greffe,
    CompteurNumeros[] Compteurs);

/// <summary>
/// Pont de l'ecran « Compteurs de numéros » (web/src/ecrans/EcranCompteurs.tsx) : ecran NEUF de la V3,
/// « a maquetter » dans le carnet (NUM-004 T04, « écran de réattribution d'un numéro »), qui reprend la
/// procedure de menu XNUMERO de la V2 (« Reattribution de numero metier », PROCVB6, ouverte aux roles
/// RCSNIV03 et RCSNIV05 sur RIG_DEV, lu le 21/11/2026 dans PROCESSUS_ET_FONCTIONALITE et
/// HABILIT_ROLE_USER_PROCESSUS ; ses sources VB6 ne sont pas dans le depot). Il montre les compteurs de la
/// base V3 du greffe (NUM-001 CA01 : une ligne par serie, annee et jour) et permet au responsable de
/// rendre un numero donne par erreur, avec son explication (NUM-004 CA10 : memes regles qu'une
/// restitution venue d'un programme, tenues par le serveur, Serveur/Numerotation/Compteur).
///
/// Le greffe : les routes du compteur portent le code du greffe (lot 8). Il vient du choix du greffe a
/// l'ouverture (lot 10, <see cref="SessionAgent.CodeGreffe"/>) ; si la session n'en porte pas, l'ecran
/// branche au serveur dit qu'aucun greffe n'est ouvert, sans en deviner un.
/// Sans RIGV3_SERVEUR, l'ecran montre des compteurs de demonstration en lecture seule.
/// </summary>
public static class EcranCompteurs
{
    /// <summary>Le nom de l'ecran (menu, catalogue des ecrans).</summary>
    public const string NomEcran = "Compteurs de numéros";

    [JSInvokable("EcranCompteurs.Ouvrir")]
    public static Task<VueEcranCompteurs> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranCompteurs.Rendre")]
    public static Task<VueEcranCompteurs> Rendre(DemandeRestitution demande)
        => RendreAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, demande);

    /// <summary>Les compteurs du greffe <paramref name="greffe"/> lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranCompteurs> OuvrirAsync(ClientServeur? serveur, string? greffe,
                                                            string? message = null, bool echec = false)
    {
        if (serveur is null) return Demonstration();
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : les compteurs se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var compteurs = await serveur.LireAsync<CompteurNumeros[]>(RoutesCompteur.Pour(RoutesCompteur.Compteurs, greffe));
            return new VueEcranCompteurs("serveur", message, echec, greffe, compteurs);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse", greffe, $"Vous n'avez pas accès aux compteurs du greffe {greffe}.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Vide("erreur", greffe, e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            // Le detail technique (corps JSON de la reponse) n'aide pas l'agent : l'adresse et le code suffisent.
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return Vide("erreur", greffe, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).");
        }
    }

    /// <summary>
    /// Rend un numero donne par erreur (NUM-004 CA10), puis relit les compteurs. La reponse du serveur dit
    /// ce qui s'est passe (rendu, non redonnable, deja rendu, ou un refus a code negatif, CA09) : l'ecran
    /// la montre telle quelle.
    /// </summary>
    public static async Task<VueEcranCompteurs> RendreAsync(ClientServeur? serveur, string? greffe, DemandeRestitution demande)
    {
        if (serveur is null)
            return Demonstration() with { Message = "Démonstration : sans serveur, aucun numéro n'est rendu.", Echec = true };
        if (string.IsNullOrWhiteSpace(greffe)) return await OuvrirAsync(serveur, greffe);
        ResultatRestitution resultat;
        try
        {
            resultat = await serveur.EnvoyerAsync<DemandeRestitution, ResultatRestitution>(
                RoutesCompteur.Pour(RoutesCompteur.Restitutions, greffe), demande);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            // Reattribution reservee a la fonction XNUMERO, comme la procedure de la V2 (NUM-004 T04, T08) ; si
            // l'agent n'est meme plus admis, la relecture des compteurs le dira (mode « refuse »).
            return await OuvrirAsync(serveur, greffe,
                $"Le numéro {demande.Numero} n'a pas été rendu : rendre un numéro est réservé aux agents qui ont la fonction " +
                $"{RoutesCompteur.FonctionReattribution} (« Réattribution de numéro métier »), comme dans l'ancien RIG.",
                echec: true);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 503)
        {
            // Compteur tenu par un autre poste au-dela du delai (NUM-008), ou base du greffe muette.
            return await OuvrirAsync(serveur, greffe,
                $"Le numéro {demande.Numero} n'a pas été rendu : le compteur {demande.Type} est occupé ou la base ne répond pas. Réessayez dans un instant.",
                echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return await OuvrirAsync(serveur, greffe,
                $"Le numéro {demande.Numero} n'a pas été rendu : le serveur de RIG a répondu en erreur ({(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).",
                echec: true);
        }
        return await OuvrirAsync(serveur, greffe, CompteRendu(demande, resultat), echec: resultat.Code != CodesNumerotation.Ok);
    }

    /// <summary>
    /// La phrase montree apres une restitution : celle du serveur, qui nomme deja le numero et ce qu'il en
    /// a fait (« 2026RJ0266 » sera redonné…, Restitution refusée : …). La phrase de l'ecran ne sert que si
    /// le serveur n'en rend pas : la mettre devant la sienne disait deux fois la meme chose (banc du 21/11).
    /// </summary>
    public static string CompteRendu(DemandeRestitution demande, ResultatRestitution resultat)
    {
        if (!string.IsNullOrWhiteSpace(resultat.Message)) return resultat.Message.Trim();
        var quoi = resultat.Issue switch
        {
            IssuesRestitution.Rendu => "rendu : il sera redonné au prochain acte de sa série",
            IssuesRestitution.NonRedonnable => "noté au journal : sa série ne redonne pas les numéros rendus, le trou est expliqué",
            IssuesRestitution.DejaRendu => "déjà rendu : il attend d'être redonné, la demande est notée au journal",
            _ => $"refusé (code {resultat.Code})",
        };
        return $"{demande.Numero} ({demande.Type}) {quoi}.";
    }

    private static VueEcranCompteurs Vide(string mode, string? greffe, string message)
        => new(mode, message, true, greffe, []);

    /// <summary>
    /// Sans serveur : quelques compteurs, libelles et derniers chronos lus sur RIG_DEV le 21/11/2026
    /// (NUMERO_METIER, annee 2026), formats de tests/NumerotationV2Check/compteurs-d8.json. Ils vivent ici
    /// plutot que dans Metier/JeuDemo.cs : seul cet ecran les montre.
    /// </summary>
    private static VueEcranCompteurs Demonstration()
        => new("demo", null, false, null,
        [
            Demo("RGB",   2026, 0,   "RCS Numéro de gestion du dossier RCS, catégorie B", "ALC",  4, "B",    "",  5, 890,   true,  2),
            Demo("JPC",   2026, 0,   "n° de PC du ressort du greffe",                     "ALC",  4, "RJ",   "",  4, 242,   true,  1),
            Demo("JDSAN", 2026, 0,   "n° de dépôt saisine en sanction",                   "LASC", 4, "SANC", "/", 5, 14,    true,  0),
            Demo("PARA",  2026, 0,   "Numéro de paraphe",                                 "AC",   2, "",     "",  5, 436,   true,  0),
            Demo("RC5",   2026, 0,   "Numéro chronologique des EIRL",                     "LASC", 2, "F",    "/", 6, 3,     true,  0),
            Demo("ARRD",  2026, 325, "Numéro des arrivées, type Demande",                 "LAQC", 2, "A",    "",  5, 22,    true,  0),
            Demo("DMD",   2026, 325, "Numéro des Demandes",                               "LAQC", 2, "D",    "",  5, 31,    false, 0),
            Demo("FACTU", 2026, 0,   "Dernier numéro de facture affecté",                 "A-C",  2, "",     "",  6, 22768, false, 0),
            Demo("LIAI",  2026, 0,   "RCS Numéro de liaison",                             "LGACK",1, "G",    "",  5, 1782,  false, 0),
        ]);

    private static CompteurNumeros Demo(string type, int annee, int jour, string libelle, string structure, int codageAnnee,
                                        string lettre, string separateur, int taille, int chrono, bool rendus, int enAttente)
        => new(type, annee, jour, libelle, structure, codageAnnee, lettre, separateur, taille, null, 1, chrono, rendus,
               null, null, null, new DateTime(2026, 11, 20, 17, 42, 0, DateTimeKind.Utc), enAttente);
}
