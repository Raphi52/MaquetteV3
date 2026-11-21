using System.Net.Http;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Ce que l'ecran « Réglages du greffe » affiche.</summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403 a la lecture) ou « erreur ».</param>
/// <param name="Message">Le compte rendu de la derniere modification, ou la raison d'un refus ; null sinon.</param>
/// <param name="Echec">Le message est un refus ou une erreur, pas une modification faite.</param>
/// <param name="Greffe">Le code du greffe dont les reglages sont montres ; null en demonstration.</param>
/// <param name="Selection">Le code du reglage a garder choisi apres un geste ; null sinon.</param>
public sealed record VueEcranReglages(
    string Mode,
    string? Message,
    bool Echec,
    string? Greffe,
    ReglageGreffe[] Reglages,
    string? Selection);

/// <summary>
/// Pont de l'ecran « Réglages du greffe » (web/src/ecrans/EcranReglages.tsx), SOC-003 T03 « à maquetter » :
/// consulter et modifier les reglages generaux du greffe (table PARAMETRAGE de la V2), avec l'auteur et la date
/// de la derniere modification (CA07). La V2 n'a pas d'ecran generique de ces reglages : chaque ecran V2 ecrit les
/// siens sous sa fonction (socle-greffes.md) ; cet ecran montre les colonnes que la V2 garde (code, libelle, valeur
/// en texte, utilisation, auteur et date). Routes du lot 9 : lecture par tout agent admis dans le greffe, modification
/// reservee a la regle V2 de la fiche du greffe (ServiceDroits.PeutModifierLeGreffe) -- le motif d'un 403 est celui
/// du serveur, lu dans le corps de sa reponse, jamais recopie ici.
/// Le greffe : celui de la session (<see cref="SessionAgent.CodeGreffe"/>, lot 10).
/// </summary>
public static class EcranReglages
{
    /// <summary>Le nom de l'ecran tel que le journal des gestes l'ecrit (SOC-016 CA01).</summary>
    public const string NomEcran = "Réglages du greffe";

    [JSInvokable("EcranReglages.Ouvrir")]
    public static Task<VueEcranReglages> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranReglages.Modifier")]
    public static Task<VueEcranReglages> Modifier(string code, string? valeur)
        => ModifierAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, code, valeur);

    /// <summary>Les reglages du greffe <paramref name="greffe"/> lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranReglages> OuvrirAsync(ClientServeur? serveur, string? greffe,
                                                           string? message = null, bool echec = false, string? selection = null)
    {
        if (serveur is null) return Demonstration();
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : les réglages se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var reglages = await serveur.LireAsync<ReglageGreffe[]>(RoutesGreffes.Pour(RoutesGreffes.Reglages, greffe));
            return new VueEcranReglages("serveur", message, echec, greffe, reglages, selection);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403)
        {
            return Vide("refuse", greffe, Motif(e) ?? $"Vous n'avez pas accès aux réglages du greffe {greffe}.");
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

    /// <summary>
    /// Remplace la valeur du reglage <paramref name="code"/> (texte, comme la V2 ; vide = null) puis relit la liste.
    /// Un refus (403 sans le droit, 404 reglage absent) s'affiche avec le motif du serveur ; rien n'a change.
    /// </summary>
    public static async Task<VueEcranReglages> ModifierAsync(ClientServeur? serveur, string? greffe, string code, string? valeur)
    {
        if (serveur is null)
            return Demonstration() with { Message = "Démonstration : sans serveur, les réglages ne se modifient pas.", Echec = true, Selection = code };
        if (string.IsNullOrWhiteSpace(greffe)) return await OuvrirAsync(serveur, greffe);
        ReglageGreffe apres;
        try
        {
            apres = await serveur.ModifierAsync<SaisieReglage, ReglageGreffe>(
                RoutesGreffes.Pour(RoutesGreffes.Reglage, greffe, code), new SaisieReglage(valeur, NomEcran));
        }
        catch (ErreurServeur e) when (e.CodeHttp is 401 or 403 or 404 or 409)
        {
            return await OuvrirAsync(serveur, greffe, Motif(e) ?? $"Le réglage {code} n'a pas été modifié ({e.CodeHttp}).", echec: true, selection: code);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var etat = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return await OuvrirAsync(serveur, greffe, $"Le réglage {code} n'a pas été modifié : le serveur de RIG a répondu en erreur ({etat}).",
                echec: true, selection: code);
        }
        return await OuvrirAsync(serveur, greffe,
            $"Réglage {apres.Code} modifié : « {apres.Valeur ?? "(vide)"} », par {apres.AuteurModif}.", selection: apres.Code);
    }

    /// <summary>Le motif d'un refus du serveur (RefusGreffe en JSON dans le corps) ; null si le corps n'en porte pas.</summary>
    public static string? Motif(ErreurServeur e)
    {
        if (string.IsNullOrWhiteSpace(e.Corps)) return null;
        try { return JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Motif; }
        catch (JsonException) { return null; }
    }

    private static VueEcranReglages Vide(string mode, string? greffe, string message) => new(mode, message, true, greffe, [], null);

    /// <summary>
    /// Sans serveur : 8 reglages lus sur RIG_DEV (PARAMETRAGE, 21/11/2026), codes, libelles et valeurs tels quels ; les
    /// auteurs de modification (comptes de collegues) sont remplaces par des comptes neutres. Ici plutot que dans
    /// Metier/JeuDemo.cs : seul cet ecran les montre.
    /// </summary>
    private static VueEcranReglages Demonstration()
        => new("demo", null, false, null,
        [
            Demo("CMDWEB_MAQUETTE_CNO_FS_SANS_OPERATION", "Commande web : maquette du certificat CNO-FS quand aucune fusion ni scission n'est détecté", "7CNO_8_IMP", null, "INTRANET\\responsable", 16),
            Demo("EXPRESSO_LC_MODEPAIEMENT_CH", "Mode de paiement par défaut pour les LC", "RBT", "RIG_FACTU_EXPRESSO : Lettres_chèques", "rig", 19),
            Demo("EXPRESSO_LC_ORDRE_NUM", "Ordre d'attribution des n° de chèques pour les LC (C=Croissant;D=Décroissant)", "C", "RIG_FACTU_EXPRESSO : Lettres_chèques", "rig", 19),
            Demo("PROC_RELCLI_ultCheckBoxExclureCrediteurs", "Réglage par défaut dans PROC_RELCLI : Exclure les clients créditeurs et non mouvementés", "True", null, "rig", 13),
            Demo("PROC_RELCLI_ultCheckBoxLettre", "Réglage par défaut dans PROC_RELCLI : Avec lettre", "True", null, "rig", 13),
            Demo("PROC_RELCLI_ultCheckBoxSendMail", "Réglage par défaut dans PROC_RELCLI : Envoi par mail si possible", "False", null, "rig", 13),
            Demo("RIGV3_FONCTIONNALITE_CMDWEB_RAPPROCHEMENT_PRECALCULE", "Rapprochement précalculé dans la liste des commandes web", "1", null, "INTRANET\\responsable", 17),
            Demo("RIGV3_FONCTIONNALITE_JUD_MODIFICATION_INSTANCE", "Modification d'une instance judiciaire", "1", null, "INTRANET\\responsable", 16),
        ], null);

    private static ReglageGreffe Demo(string code, string libelle, string valeur, string? utilisation, string auteur, int jourDeNovembre)
        => new(code, libelle, valeur, utilisation, "rig", new DateTime(2024, 3, 1, 9, 0, 0), auteur, new DateTime(2026, 11, jourDeNovembre, 10, 0, 0));
}
