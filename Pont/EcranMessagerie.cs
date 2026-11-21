using System.Net;
using System.Net.Http;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Ce que l'ecran « Messagerie » affiche.</summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » ou « erreur ».</param>
/// <param name="Message">La raison d'un refus ou d'une panne ; null sinon.</param>
/// <param name="Greffe">Le greffe dont les modeles sont montres ; null en demonstration.</param>
/// <param name="VoitReservesAmitel">Le serveur a rendu au moins un modele reserve a Amitel, ce qu'il ne fait que pour un agent
/// d'Amitel (CA05) : l'ecran montre alors la bascule « Amitel » (la V2 : CheckBoxAmitel visible pour IsUserAmitel,
/// OPE_EDIT_EMAILMODEL.cs:333). Deduit des modeles recus, jamais d'une autre source : un agent du greffe ne peut pas l'avoir.</param>
/// <param name="Modeles">Les modeles du greffe, inactifs compris (l'ecran les cache, comme la V2, sauf a la demande).</param>
/// <param name="Listes">Les listes de distribution publiques et celles de l'agent, inactives comprises.</param>
public sealed record VueEcranMessagerie(
    string Mode,
    string? Message,
    string? Greffe,
    bool VoitReservesAmitel,
    ModeleMessage[] Modeles,
    ListeDiffusion[] Listes);

/// <summary>Un modele ouvert : <paramref name="Apercu"/>, ou la raison de son absence dans <paramref name="Message"/>.</summary>
public sealed record VueModeleMessagerie(string Mode, string? Message, ApercuModele? Apercu);

/// <summary>
/// Pont de l'ecran « Messagerie » (web/src/ecrans/EcranMessagerie.tsx), SOC-006 T04 : les modeles de courriel du greffe et
/// ses listes de distribution, lus dans SA base par le serveur, et l'apercu d'un modele tel qu'il partirait (habillage,
/// valeurs et adresses du greffe). Reprend la procedure V2 PROC_GESTEMAIL (« Modele de mail ») en lecture seule. Un agent
/// du greffe n'y voit ni les modeles reserves a Amitel ni la bascule « Amitel » (CA05) : le serveur ne les lui rend pas
/// (RoutesCourriels.Modeles et RoutesCourriels.Modele, prouve par tests/SocleCourrielsCheck). Le greffe : celui de la
/// session (<see cref="SessionAgent.CodeGreffe"/>). Sans RIGV3_SERVEUR : la demonstration, ici et nulle part ailleurs.
/// </summary>
public static class EcranMessagerie
{
    public const string NomEcran = "Messagerie";

    [JSInvokable("EcranMessagerie.Ouvrir")]
    public static Task<VueEcranMessagerie> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranMessagerie.Modele")]
    public static Task<VueModeleMessagerie> Modele(string type) => ModeleAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, type);

    /// <summary>Les modeles et les listes de <paramref name="greffe"/>, lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranMessagerie> OuvrirAsync(ClientServeur? serveur, string? greffe)
    {
        if (serveur is null) return new("demo", null, null, false, DemoModeles, DemoListes);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : les modèles se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var modeles = await serveur.LireAsync<ModeleMessage[]>(RoutesCourriels.Pour(RoutesCourriels.Modeles, greffe));
            var listes = await serveur.LireAsync<ListeDiffusion[]>(RoutesCourriels.Pour(RoutesCourriels.ListesDiffusion, greffe));
            return new("serveur", null, greffe, modeles.Any(m => m.ReserveAmitel), modeles, listes);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return Vide("refuse", greffe, Motif(e) ?? "Vous n'avez pas accès aux modèles de courriel de ce greffe.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Vide("erreur", greffe, e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Vide("erreur", greffe, Injoignable(serveur, e));
        }
    }

    /// <summary>Le modele <paramref name="type"/> : texte, adresses resolues, listes par defaut et apercu. Un modele que le
    /// serveur ne rend pas (inconnu, ou reserve a Amitel pour un agent du greffe : 404) donne sa raison, jamais d'apercu.</summary>
    public static async Task<VueModeleMessagerie> ModeleAsync(ClientServeur? serveur, string? greffe, string type)
    {
        if (serveur is null)
            return DemoApercu(type) is { } demo ? new("demo", null, demo) : new("erreur", $"Le modèle « {type} » n'est pas dans la démonstration.", null);
        if (string.IsNullOrWhiteSpace(greffe)) return new("sans-greffe", "Aucun greffe n'est ouvert dans cette session.", null);
        try
        {
            return new("serveur", null, await serveur.LireAsync<ApercuModele>(RoutesCourriels.PourModele(greffe, type)));
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403 or 404)
        {
            return new(e.CodeHttp == 404 ? "erreur" : "refuse", Motif(e) ?? $"Le modèle « {type} » n'est pas lisible ({e.CodeHttp}).", null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new("erreur", Injoignable(serveur, e), null);
        }
    }

    private static string Injoignable(ClientServeur serveur, Exception e)
        => $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).";

    /// <summary>Le motif d'un refus du serveur (RefusGreffe) ; null si le corps n'en porte pas.</summary>
    private static string? Motif(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }

    private static VueEcranMessagerie Vide(string mode, string? greffe, string message) => new(mode, message, greffe, false, [], []);

    // --- Demonstration (sans RIGV3_SERVEUR) : cinq modeles dont les noms, libelles, objets et cases sont ceux de
    // RIG_DEV.dbo.EMAIL_MODEL (lu le 2026-11-21, lecture seule) ; textes et adresses fictifs. Aucun modele reserve a
    // Amitel : la demonstration est celle d'un agent du greffe. Ils vivent ici : seul cet ecran les montre.
    private static readonly ModeleMessage[] DemoModeles =
    [
        new("ASS", "Envoi DCP", "Communication DCP", false, "Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]>",
            "[ADRESSE_MAIL_NO_REPLY]", "[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]", null, false, false, true, false, true, false, false),
        new("Copie CEP - demande de provision", "Copie dde provision", "Duplicata - demande de règlement/provision", true,
            "Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]>", "[ADRESSE_MAIL_JUDICIAIRE]", "[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]", null,
            false, false, false, false, true, false, false),
        new("CREJET", "Lettre de rejet d'encaissement", "Rejet de votre encaissement", true,
            "Greffe du tribunal de commerce <[ADRESSE_MAIL_COMPTABILITE]>", "[ADRESSE_MAIL_NO_REPLY]", "[ADRESSE_MAIL_ACCUSE_ENVOI_RCS]", null,
            false, false, true, true, false, false, true),
        new("INPI_IMR_REFUS", "Refus RCS Formalités", "RCS Formalités - Demande rejetée", true,
            "Greffe du tribunal de commerce. Service RCS. <[ADRESSE_MAIL_RCS]>", "[ADRESSE_MAIL_NO_REPLY]", "[ADRESSE_MAIL_ACCUSE_ENVOI_RCS]", null,
            false, true, true, true, true, false, false),
        new("Jugement", "Jugement", "Jugement", true, "Greffe du tribunal de commerce <[ADRESSE_MAIL_JUDICIAIRE]>",
            "[ADRESSE_MAIL_JUDICIAIRE]", "[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]", null, false, false, true, false, true, false, false),
    ];

    private static readonly ListeDiffusion[] DemoListes =
    [
        new(1, "MANDATAIRES", "Mandataires judiciaires du ressort", "mandataire.un@etude-demo.fr;mandataire.deux@etude-demo.fr", false, true,
            RoutesCourriels.ListePublique, true),
        new(2, "GUICHET_RCS", "Guichet du registre", "rcs@greffe-demo.fr", true, false, RoutesCourriels.ListePublique, true),
        new(3, "MES_CONTACTS", "Mes contacts", "contact@client-demo.fr", true, true, "agent.demo", true),
        new(4, "ANCIENS_JUGES", "Juges (ancienne liste)", "juge@tribunal-demo.fr", false, true, RoutesCourriels.ListePublique, false),
    ];

    private static readonly Dictionary<string, string> DemoAdresses = new(StringComparer.Ordinal)
    {
        ["[ADRESSE_MAIL_JUDICIAIRE]"] = "judiciaire@greffe-demo.fr",
        ["[ADRESSE_MAIL_NO_REPLY]"] = "ne-pas-repondre@greffe-demo.fr",
        ["[ADRESSE_MAIL_ACCUSE_ENVOI_JUD]"] = "accuse-jud@greffe-demo.fr",
        ["[ADRESSE_MAIL_ACCUSE_ENVOI_RCS]"] = "accuse-rcs@greffe-demo.fr",
        ["[ADRESSE_MAIL_COMPTABILITE]"] = "comptabilite@greffe-demo.fr",
        ["[ADRESSE_MAIL_RCS]"] = "rcs@greffe-demo.fr",
    };

    private static string DemoAdresse(string? liste)
        => DemoAdresses.Aggregate(liste ?? "", (s, p) => s.Replace(p.Key, p.Value, StringComparison.Ordinal));

    private static ApercuModele? DemoApercu(string type)
    {
        if (DemoModeles.FirstOrDefault(m => m.Type == type) is not { } m) return null;
        var corps = $"Madame, Monsieur,|Vous trouverez ci-dessous le lien vers le document « {m.LibelleEcran} ».|$PieceJointe$|Le greffe";
        var texte = WebUtility.HtmlEncode(corps).Replace("|", "<br/>").Replace("$PieceJointe$",
            "<a href=\"#\">Lien n°1</a><br/><a href=\"#\">Lien n°2</a>", StringComparison.Ordinal);
        var rendu = "<html><body style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1f2937\">"
                    + "<h2 style=\"color:#0055a4;margin:0 0 12px\">Tribunal de commerce de DÉMONSTRATION</h2>"
                    + $"<div>{texte}</div><p style=\"color:#6b7280\">Le greffier : Maître Démonstration</p></body></html>";
        var listes = m.Type == "ASS" ? DemoListes[..1] : [];
        return new ApercuModele(m, corps, DemoAdresse(m.Expediteur), DemoAdresse(m.Reponse), DemoAdresse(m.Copie), "",
            rendu, true, [], listes);
    }
}
