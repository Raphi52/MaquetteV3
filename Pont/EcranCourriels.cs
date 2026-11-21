using System.Net.Http;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Les filtres de la liste, comme ceux de l'ecran V2 (OPE_LISTE_MAILS_LOT1) : periode, destinataire, modele, etat.</summary>
/// <param name="Du">Premier jour (« 2026-11-21 ») ; vide = sans borne.</param>
/// <param name="Au">Dernier jour, inclus ; vide = sans borne.</param>
/// <param name="Etat">Un des etats de <see cref="EtatsCourriel"/> ; vide = tous.</param>
public sealed record FiltreCourriels(string? Du, string? Au, string? Destinataire, string? Modele, string? Etat);

/// <summary>Ce que l'ecran « Courriels envoyés » affiche.</summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403 : pas MAIL_LOT1) ou « erreur ».</param>
/// <param name="Message">La raison d'un refus ou d'une panne ; null sinon.</param>
/// <param name="Greffe">Le greffe dont les envois sont montres ; null en demonstration.</param>
/// <param name="Filtre">Les filtres appliques (l'ecran les remontre tels quels).</param>
public sealed record VueEcranCourriels(
    string Mode,
    string? Message,
    string? Greffe,
    FiltreCourriels Filtre,
    CourrielSuivi[] Envois);

/// <summary>Le detail d'un envoi : <paramref name="Detail"/>, ou la raison de son absence dans <paramref name="Message"/>.</summary>
public sealed record VueDetailCourriel(string Mode, string? Message, DetailCourriel? Detail);

/// <summary>
/// Pont de l'ecran « Courriels envoyés » (web/src/ecrans/EcranCourriels.tsx), SOC-006 : la liste des courriels que la V3
/// a envoyes pour le greffe, avec leur trace -- envoye, non remis hors production (boite de depot), ou en echec et
/// pourquoi. Reprend l'ecran V2 « Mails envoyés en Lot 1 » (PROC_MAIL_LOT1, fonction MAIL_LOT1 : filtres periode,
/// destinataire, modele, au plus 500 lignes, aujourd'hui par defaut, texte du courriel a la demande), qui ne montrait que
/// les courriels partis (« le Lot 1 ne stocke pas d'erreur », OPE_LISTE_MAILS_LOT1.cs:13) : la V3 montre aussi les
/// echecs, puisqu'elle les trace (CA03). Lecture seule. Le greffe : celui de la session (<see cref="SessionAgent.CodeGreffe"/>).
/// </summary>
public static class EcranCourriels
{
    public const string NomEcran = "Courriels envoyés";

    [JSInvokable("EcranCourriels.Ouvrir")]
    public static Task<VueEcranCourriels> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, null);

    [JSInvokable("EcranCourriels.Chercher")]
    public static Task<VueEcranCourriels> Chercher(FiltreCourriels filtre) => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, filtre);

    [JSInvokable("EcranCourriels.Detail")]
    public static Task<VueDetailCourriel> Detail(string suivi) => DetailAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, suivi);

    /// <summary>Les envois de <paramref name="greffe"/> lus sur <paramref name="serveur"/> (null : la demonstration). Sans
    /// filtre : ceux d'aujourd'hui, comme l'ecran de la V2 a son ouverture.</summary>
    public static async Task<VueEcranCourriels> OuvrirAsync(ClientServeur? serveur, string? greffe, FiltreCourriels? filtre)
    {
        var f = filtre ?? new FiltreCourriels(DateTime.Today.ToString("yyyy-MM-dd"), null, null, null, null);
        if (serveur is null) return Demonstration(f);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null, f,
                "Aucun greffe n'est ouvert dans cette session : les courriels se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var envois = await serveur.LireAsync<CourrielSuivi[]>(RoutesCourriels.Filtrer(greffe, Jour(f.Du), Jour(f.Au),
                f.Destinataire, f.Modele, f.Etat));
            return new VueEcranCourriels("serveur", null, greffe, f, envois);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return Vide("refuse", greffe, f, Motif(e) ??
                $"Vous n'avez pas accès aux courriels envoyés : il faut la fonction {RoutesCourriels.FonctionCourrielsEnvoyes}.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Vide("erreur", greffe, f, e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.");
        }
        catch (ErreurServeur e) when (e.CodeHttp == 400)
        {
            return Vide("erreur", greffe, f, Motif(e) ?? "La recherche est mal formée.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return Vide("erreur", greffe, f, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).");
        }
    }

    /// <summary>Un envoi en detail : ses etapes et le texte du courriel parti.</summary>
    public static async Task<VueDetailCourriel> DetailAsync(ClientServeur? serveur, string? greffe, string suivi)
    {
        if (!Guid.TryParse(suivi, out var id)) return new("erreur", $"Numéro de suivi illisible : {suivi}.", null);
        if (serveur is null)
            return DemoDetail(id) is { } demo ? new("demo", null, demo) : new("erreur", "Ce courriel n'est pas dans la démonstration.", null);
        if (string.IsNullOrWhiteSpace(greffe)) return new("sans-greffe", "Aucun greffe n'est ouvert dans cette session.", null);
        try
        {
            return new("serveur", null, await serveur.LireAsync<DetailCourriel>(RoutesCourriels.Pour(RoutesCourriels.Envoi, greffe, id)));
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403 or 404)
        {
            return new(e.CodeHttp == 404 ? "erreur" : "refuse", Motif(e) ?? $"Le courriel {id} n'est pas lisible ({e.CodeHttp}).", null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return new("erreur", $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).", null);
        }
    }

    private static DateOnly? Jour(string? texte) => DateOnly.TryParse(texte, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Le motif d'un refus du serveur (RefusGreffe) ; null si le corps n'en porte pas.</summary>
    private static string? Motif(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }

    private static VueEcranCourriels Vide(string mode, string? greffe, FiltreCourriels f, string message) => new(mode, message, greffe, f, []);

    // --- Demonstration (sans RIGV3_SERVEUR) : trois envois d'aujourd'hui, un par issue. Les types de modele sont ceux de
    // RIG_DEV.dbo.EMAIL_MODEL (lu le 2026-11-21) ; les adresses sont fictives. Ils vivent ici : seul cet ecran les montre.
    private static readonly Guid DemoEnvoye = new("6b1e0d5c-0000-4000-8000-000000000001");
    private static readonly Guid DemoIgnore = new("6b1e0d5c-0000-4000-8000-000000000002");
    private static readonly Guid DemoEchec = new("6b1e0d5c-0000-4000-8000-000000000003");

    private static CourrielSuivi[] DemoEnvois()
    {
        var jour = DateTime.Today;
        return
        [
            new(DemoEchec, jour.AddHours(11).AddMinutes(2), EtatsCourriel.Echec, "Convocation", "judiciaire@greffe-demo.fr",
                "maitre.martin@avocat-demo.fr", "accuse-jud@greffe-demo.fr", "Convocation à l'audience du 4 décembre", 1, null,
                "Mailbox unavailable. The server response was: 5.1.1 adresse inconnue", @"AMITEL\agent.demo", null),
            new(DemoIgnore, jour.AddHours(10).AddMinutes(41), EtatsCourriel.Ignore, "CA", "judiciaire@greffe-demo.fr",
                "mandataire@etude-demo.fr", "accuse-jud@greffe-demo.fr", "Certificat d'audience", 2, 2, null, @"AMITEL\agent.demo",
                @"C:\RigV3\courriels\6b1e0d5c-0000-4000-8000-000000000002.eml"),
            new(DemoEnvoye, jour.AddHours(9).AddMinutes(15), EtatsCourriel.Envoye, "ASS", "judiciaire@greffe-demo.fr",
                "societe.exemple@client-demo.fr", null, "Assignation enregistrée", 1, 1, null, @"AMITEL\agent.demo", null),
        ];
    }

    private static VueEcranCourriels Demonstration(FiltreCourriels f)
    {
        var du = Jour(f.Du);
        var au = Jour(f.Au);
        var envois = DemoEnvois().Where(e =>
            (du is null || DateOnly.FromDateTime(e.Debut) >= du) && (au is null || DateOnly.FromDateTime(e.Debut) <= au)
            && (string.IsNullOrWhiteSpace(f.Destinataire) || (e.Destinataires ?? "").Contains(f.Destinataire.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(f.Modele) || (e.TypeModele ?? "").Contains(f.Modele.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(f.Etat) || e.Etat == f.Etat)).ToArray();
        return new("demo", null, null, f, envois);
    }

    private static DetailCourriel? DemoDetail(Guid suivi)
    {
        if (DemoEnvois().FirstOrDefault(e => e.Suivi == suivi) is not { } envoi) return null;
        var t = envoi.Debut;
        EtapeCourriel E(int secondes, string etape, bool? succes, string? hote = null, string? type = null, string? message = null, string? depot = null)
            => new(t.AddSeconds(secondes), etape, "MANUEL", succes, hote, hote is null ? null : 25, type, message, depot);
        EtapeCourriel[] etapes = envoi.Etat switch
        {
            EtatsCourriel.Echec =>
            [
                E(0, EtapesCourriel.PreparationEmail, null), E(0, EtapesCourriel.EnvoiDebut, null),
                E(1, EtapesCourriel.AppelSmtp, null, "srvmail.amitel.fr"),
                E(2, EtapesCourriel.EnvoiErreur, false, "srvmail.amitel.fr", "System.Net.Mail.SmtpFailedRecipientException", envoi.Erreur),
            ],
            EtatsCourriel.Ignore =>
            [
                E(0, EtapesCourriel.PreparationEmail, null), E(0, EtapesCourriel.EnvoiDebut, null),
                E(1, EtapesCourriel.IgnoreHorsProduction, true, depot: envoi.FichierDepot),
                E(1, EtapesCourriel.EnvoiSucces, true, depot: envoi.FichierDepot), E(1, EtapesCourriel.CourrielEnregistre, true),
            ],
            _ =>
            [
                E(0, EtapesCourriel.PreparationEmail, null), E(0, EtapesCourriel.EnvoiDebut, null),
                E(1, EtapesCourriel.AppelSmtp, null, "srvmail.amitel.fr"), E(2, EtapesCourriel.EnvoiSucces, true, "srvmail.amitel.fr"),
                E(2, EtapesCourriel.CourrielEnregistre, true),
            ],
        };
        var corps = envoi.IdCourriel is null ? null
            : "Maître,|Vous trouverez ci-dessous le lien vers le document de votre affaire.|$PieceJointe$|Le greffe";
        return new DetailCourriel(envoi, corps, envoi.IdCourriel is null ? null : false, etapes);
    }
}
