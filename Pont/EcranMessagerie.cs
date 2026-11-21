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
/// <param name="PeutModifier">L'agent a la fonction GESTEMAIL dans ce greffe (le serveur le dit, RoutesDroits.Verifier) : l'ecran
/// montre ses boutons d'ecriture. Sans elle, la lecture seule. Vrai en demonstration, ou chaque ecriture est refusee par ce pont.
/// Le serveur reste juge : chaque route d'ecriture verifie GESTEMAIL a nouveau (403).</param>
/// <param name="EstAmitel">L'agent est un agent d'Amitel (son identite dans ce greffe) : la case « Modèle modifiable uniquement par
/// Amitel » et le dictionnaire des tags ne sont qu'a lui (OPE_EDIT_EMAILMODEL.cs:192-193, :333).</param>
/// <param name="Compte">Le compte de l'agent : le proprietaire d'une liste privee qu'il cree (« Privé (compte) » de la V2).</param>
/// <param name="Dictionnaire">Le dictionnaire des tags de la V2, pour un agent d'Amitel seulement ; null pour les autres.</param>
public sealed record VueEcranMessagerie(
    string Mode,
    string? Message,
    string? Greffe,
    bool VoitReservesAmitel,
    ModeleMessage[] Modeles,
    ListeDiffusion[] Listes,
    bool PeutModifier,
    bool EstAmitel,
    string? Compte,
    string? Dictionnaire);

/// <summary>Un modele ouvert : <paramref name="Apercu"/>, ou la raison de son absence dans <paramref name="Message"/>.</summary>
public sealed record VueModeleMessagerie(string Mode, string? Message, ApercuModele? Apercu);

/// <summary>Ce qu'est devenu un geste d'ecriture de la Messagerie.</summary>
/// <param name="Ok">Le serveur a enregistre le geste ; faux : <paramref name="Message"/> dit pourquoi (le motif du serveur), rien n'a
/// change et l'ecran garde la saisie de l'agent.</param>
/// <param name="Vue">Apres un geste reussi : les modeles et les listes relus.</param>
/// <param name="Fiche">Apres un geste reussi sur un modele : son apercu recalcule par le serveur.</param>
public sealed record ResultatMessagerie(bool Ok, string Message, VueEcranMessagerie? Vue, VueModeleMessagerie? Fiche);

/// <summary>
/// Pont de l'ecran « Messagerie » (web/src/ecrans/EcranMessagerie.tsx), SOC-006 T04 : les modeles de courriel du greffe et
/// ses listes de distribution, lus dans SA base par le serveur, et l'apercu d'un modele tel qu'il partirait (habillage,
/// valeurs et adresses du greffe). Reprend la procedure V2 PROC_GESTEMAIL (« Modele de mail ») en lecture seule. Un agent
/// du greffe n'y voit ni les modeles reserves a Amitel ni la bascule « Amitel » (CA05) : le serveur ne les lui rend pas
/// (RoutesCourriels.Modeles et RoutesCourriels.Modele, prouve par tests/SocleCourrielsCheck). Le greffe : celui de la
/// session (<see cref="SessionAgent.CodeGreffe"/>). Sans RIGV3_SERVEUR : la demonstration, ici et nulle part ailleurs.
///
/// L'ECRITURE (lot 15) : un agent qui a la fonction GESTEMAIL cree, modifie, desactive et reactive un modele et une liste,
/// par les routes d'ecriture du serveur (Serveur/Socle/Courriels/GestionCourriels.cs), qui controlent chaque saisie et
/// ecrivent le geste au journal. Un refus du serveur rend son motif, tel quel ; apres un geste reussi, les modeles, les
/// listes et l'apercu sont relus. En demonstration, rien ne s'ecrit : le geste est refuse et le dit.
/// </summary>
public static class EcranMessagerie
{
    public const string NomEcran = "Messagerie";

    [JSInvokable("EcranMessagerie.Ouvrir")]
    public static Task<VueEcranMessagerie> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranMessagerie.Modele")]
    public static Task<VueModeleMessagerie> Modele(string type) => ModeleAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, type);

    /// <summary>Enregistre un modele : <paramref name="type"/> null le cree, sinon modifie le modele de ce nom.</summary>
    [JSInvokable("EcranMessagerie.EnregistrerModele")]
    public static Task<ResultatMessagerie> EnregistrerModele(SaisieModele saisie, string? type)
        => EnregistrerModeleAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, saisie, type);

    [JSInvokable("EcranMessagerie.ActiverModele")]
    public static Task<ResultatMessagerie> ActiverModele(string type, bool actif)
        => ActiverModeleAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, type, actif);

    /// <summary>Enregistre une liste de distribution : <paramref name="id"/> null la cree, sinon modifie la liste de ce numero.</summary>
    [JSInvokable("EcranMessagerie.EnregistrerListe")]
    public static Task<ResultatMessagerie> EnregistrerListe(SaisieListe saisie, int? id)
        => EnregistrerListeAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, saisie, id);

    [JSInvokable("EcranMessagerie.ActiverListe")]
    public static Task<ResultatMessagerie> ActiverListe(int id, bool actif)
        => ActiverListeAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, actif);

    /// <summary>Les modeles et les listes de <paramref name="greffe"/>, lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranMessagerie> OuvrirAsync(ClientServeur? serveur, string? greffe)
    {
        if (serveur is null) return new("demo", null, null, false, DemoModeles, DemoListes, true, false, DemoCompte, null);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : les modèles se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var modeles = await serveur.LireAsync<ModeleMessage[]>(RoutesCourriels.Pour(RoutesCourriels.Modeles, greffe));
            var listes = await serveur.LireAsync<ListeDiffusion[]>(RoutesCourriels.Pour(RoutesCourriels.ListesDiffusion, greffe));
            // Les boutons d'ecriture : seulement si le serveur dit que l'agent a GESTEMAIL dans CE greffe ; Amitel : son identite.
            var droit = await serveur.LireAsync<VerificationDroit>(
                RoutesDroits.Pour(RoutesDroits.Verifier, greffe) + "?fonction=" + Uri.EscapeDataString(RoutesCourriels.FonctionGestion));
            var agent = (await serveur.LireAsync<IdentiteAgent>(RoutesIdentite.Pour(greffe))).Agent;
            var amitel = agent?.EstAmitel == true;
            return new("serveur", null, greffe, modeles.Any(m => m.ReserveAmitel), modeles, listes, droit.Autorise, amitel,
                agent?.CompteWindows, amitel ? DictionnaireDesTags : null);
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

    /// <summary>Cree (<paramref name="type"/> null) ou modifie le modele, puis relit les modeles, les listes et son apercu.</summary>
    public static async Task<ResultatMessagerie> EnregistrerModeleAsync(ClientServeur? serveur, string? greffe, SaisieModele saisie, string? type)
    {
        if (serveur is null) return Demonstration("aucun modèle n'est enregistré");
        if (string.IsNullOrWhiteSpace(greffe)) return SansGreffe;
        saisie = saisie with { Ecran = NomEcran };
        return await Ecrire(serveur, greffe, "Le modèle n'a pas été enregistré", async () =>
        {
            var m = type is null
                ? await serveur.EnvoyerAsync<SaisieModele, ModeleMessage>(RoutesCourriels.Pour(RoutesCourriels.Modeles, greffe), saisie)
                : await serveur.ModifierAsync<SaisieModele, ModeleMessage>(RoutesCourriels.PourModele(greffe, type), saisie);
            return (type is null ? $"Modèle « {m.Type} » créé." : $"Modèle « {m.Type} » enregistré.", m.Type);
        });
    }

    /// <summary>Desactive (<paramref name="actif"/> faux) ou reactive le modele, comme les boutons de la V2.</summary>
    public static async Task<ResultatMessagerie> ActiverModeleAsync(ClientServeur? serveur, string? greffe, string type, bool actif)
    {
        if (serveur is null) return Demonstration(actif ? "aucun modèle n'est réactivé" : "aucun modèle n'est désactivé");
        if (string.IsNullOrWhiteSpace(greffe)) return SansGreffe;
        return await Ecrire(serveur, greffe, actif ? "Le modèle n'a pas été réactivé" : "Le modèle n'a pas été désactivé", async () =>
        {
            var m = await serveur.ModifierAsync<SaisieActivation, ModeleMessage>(
                RoutesCourriels.PourModele(greffe, type, RoutesCourriels.ActivationModele), new SaisieActivation(actif, NomEcran));
            return ($"Modèle « {m.Type} » {(actif ? "réactivé" : "désactivé")}.", m.Type);
        });
    }

    /// <summary>Cree (<paramref name="id"/> null) ou modifie la liste, puis relit les modeles et les listes.</summary>
    public static async Task<ResultatMessagerie> EnregistrerListeAsync(ClientServeur? serveur, string? greffe, SaisieListe saisie, int? id)
    {
        if (serveur is null) return Demonstration("aucune liste n'est enregistrée");
        if (string.IsNullOrWhiteSpace(greffe)) return SansGreffe;
        saisie = saisie with { Ecran = NomEcran };
        return await Ecrire(serveur, greffe, "La liste n'a pas été enregistrée", async () =>
        {
            var l = id is { } n
                ? await serveur.ModifierAsync<SaisieListe, ListeDiffusion>(RoutesCourriels.PourListe(greffe, n), saisie)
                : await serveur.EnvoyerAsync<SaisieListe, ListeDiffusion>(RoutesCourriels.Pour(RoutesCourriels.ListesDiffusion, greffe), saisie);
            return (id is null ? $"Liste « {l.Code} » créée." : $"Liste « {l.Code} » enregistrée.", null);
        });
    }

    /// <summary>Desactive (<paramref name="actif"/> faux) ou reactive la liste.</summary>
    public static async Task<ResultatMessagerie> ActiverListeAsync(ClientServeur? serveur, string? greffe, int id, bool actif)
    {
        if (serveur is null) return Demonstration(actif ? "aucune liste n'est réactivée" : "aucune liste n'est désactivée");
        if (string.IsNullOrWhiteSpace(greffe)) return SansGreffe;
        return await Ecrire(serveur, greffe, actif ? "La liste n'a pas été réactivée" : "La liste n'a pas été désactivée", async () =>
        {
            var l = await serveur.ModifierAsync<SaisieActivation, ListeDiffusion>(
                RoutesCourriels.PourListe(greffe, id, RoutesCourriels.ActivationListe), new SaisieActivation(actif, NomEcran));
            return ($"Liste « {l.Code} » {(actif ? "réactivée" : "désactivée")}.", null);
        });
    }

    /// <summary>Un geste d'ecriture : son refus rend le motif du serveur (rien n'a change) ; reussi, l'ecran est relu, et l'apercu
    /// du modele <c>Type</c> recalcule par le serveur.</summary>
    private static async Task<ResultatMessagerie> Ecrire(ClientServeur serveur, string greffe, string echec,
                                                          Func<Task<(string Message, string? Type)>> geste)
    {
        (string Message, string? Type) fait;
        try
        {
            fait = await geste();
        }
        catch (ErreurServeur e) when (e.CodeHttp is 400 or 401 or 403 or 404 or 409)
        {
            return new(false, Motif(e) ?? $"{echec} ({e.CodeHttp}).", null, null);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return new(false, $"{echec} : {Injoignable(serveur, e)}", null, null);
        }
        return new(true, fait.Message, await OuvrirAsync(serveur, greffe),
            fait.Type is null ? null : await ModeleAsync(serveur, greffe, fait.Type));
    }

    private static ResultatMessagerie Demonstration(string quoi)
        => new(false, $"Démonstration : sans serveur, {quoi}. Ouvrez RIG branché sur son serveur pour modifier les modèles du greffe.", null, null);

    private static readonly ResultatMessagerie SansGreffe =
        new(false, "Aucun greffe n'est ouvert dans cette session : rien n'est enregistré.", null, null);

    /// <summary>
    /// Le dictionnaire des tags de la V2, tel quel (OPE_EDIT_TEMPLATE_MAIL.GetMessageDictionnaireTags, OPE_EDIT_TEMPLATE_MAIL.cs:167-182),
    /// reserve a un agent d'Amitel comme son bouton (OPE_EDIT_EMAILMODEL.cs:192-193).
    /// </summary>
    public const string DictionnaireDesTags =
        "TOKENS DU TEMPLATE (habillage — à placer dans le HtmlBody) :\n" +
        "  {{MESSAGE_PERSONNALISE}}  : corps du mail (modèle) injecté ici\n" +
        "  {{LISTE_DOCUMENTS_HTML}}  : liste des liens / documents (lignes <tr>)\n" +
        "  {{VILLE_TRIBUNAL}}        : ville du tribunal (PARAM_EDITIONS)\n" +
        "  {{NOM_GREFFIER}}          : nom du greffier (greffe_parametre)\n" +
        "  cid:LogoGreffe            : logo du greffe (attribut src d'une image)\n" +
        "\n" +
        "TAGS DU CORPS DU MODÈLE (résolus à l'envoi — à saisir dans le modèle, pas dans le template) :\n" +
        "  $$NumeroAffaireJud        : numéro de rôle de l'affaire\n" +
        "  $$NomAffaireJud           : nom de l'affaire\n" +
        "  $$NumeroEtNomAffaireJud   : numéro + nom de l'affaire\n" +
        "  $$LibelleDocument         : libellé du document édité\n" +
        "  $LOGO$ / $PieceJointe$    : repositionnent le logo / les liens dans le corps";

    private static string Injoignable(ClientServeur serveur, Exception e)
        => $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).";

    /// <summary>Le motif d'un refus du serveur (RefusGreffe) ; null si le corps n'en porte pas.</summary>
    private static string? Motif(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }

    private static VueEcranMessagerie Vide(string mode, string? greffe, string message)
        => new(mode, message, greffe, false, [], [], false, false, null, null);

    // --- Demonstration (sans RIGV3_SERVEUR) : cinq modeles dont les noms, libelles, objets et cases sont ceux de
    // RIG_DEV.dbo.EMAIL_MODEL (lu le 2026-11-21, lecture seule) ; textes et adresses fictifs. Aucun modele reserve a
    // Amitel : la demonstration est celle d'un agent du greffe, qui a GESTEMAIL (le formulaire se montre, chaque
    // enregistrement est refuse par ce pont). Ils vivent ici : seul cet ecran les montre.
    private const string DemoCompte = "agent.demo";

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
        new(3, "MES_CONTACTS", "Mes contacts", "contact@client-demo.fr", true, true, DemoCompte, true),
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
