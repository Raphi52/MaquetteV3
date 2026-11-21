using System.Net.Http;
using RigV3.Contrats.Recopie;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>
/// Ce que l'ecran « Fiches en conflit » affiche.
/// </summary>
/// <param name="Mode">« serveur » (base V3 du greffe), « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (l'agent
/// n'est pas designe : 403) ou « erreur » (serveur, greffe ou base injoignable).</param>
/// <param name="PeutTraiter">L'agent a la fonction de suivi (RoutesDroits.Verifier) : le bouton « Traité » est montre.</param>
/// <param name="Traites">La liste montre les ecarts deja traites (sinon ceux qui attendent).</param>
/// <param name="Table">Le filtre par table ; null = toutes.</param>
public sealed record VueEcranConflits(
    string Mode,
    string? Message,
    bool Echec,
    string? Greffe,
    bool PeutTraiter,
    bool Traites,
    string? Table,
    EcartRecopie[] Ecarts,
    string[] Tables,
    AlerteEcarts? Alerte);

/// <summary>L'alerte « fiche modifiée des deux côtés » de l'accueil (REC-004 CA02, CA03) ; null pour un agent non designe.</summary>
/// <param name="Statut">« alerte » (rouge : un ecart attend depuis 5 jours ouvres ou plus), « action », ou « ok » (rien a traiter).</param>
/// <param name="Cible">Le code de l'ecran que son clic ouvre.</param>
public sealed record AlerteFichesEnConflit(int Nombre, int EnRouge, string Statut, string Titre, string Detail, string Cible);

/// <summary>
/// Pont de l'ecran « Fiches en conflit » (web/src/ecrans/EcranConflits.tsx) : ecran NEUF de la V3, « a maquetter » dans le
/// carnet (REC-004 T03) -- la V2 n'a pas de recopie, donc pas d'ecran d'ecarts. Il montre les fiches partagees modifiees
/// dans la V2 ET dans la V3 entre deux passages de la recopie : la V3 a gagne dans les deux bases, chaque champ que la V2
/// avait change y est avec sa valeur perdue et la valeur gardee, les deux dates et les deux auteurs (REC-004 CA01). Le
/// clic sur une fiche l'ouvre (son affaire, ou l'ecran de sa fiche) ; « Traité » la sort de l'alerte, avec l'agent et la
/// date (CA03). Reserve a l'agent designe : la fonction RECOPIE_ECARTS du greffe (RoutesConflits.FonctionSuivi).
/// Le greffe vient de la session (<see cref="SessionAgent.CodeGreffe"/>) ; sans RIGV3_SERVEUR, une demonstration.
/// </summary>
public static class EcranConflits
{
    public const string NomEcran = "Fiches en conflit";
    public const string Code = "PROC_FICHES_CONFLIT";

    [JSInvokable("EcranConflits.Ouvrir")]
    public static Task<VueEcranConflits> Ouvrir(bool traites, string? table)
        => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, traites, table);

    [JSInvokable("EcranConflits.Traiter")]
    public static Task<VueEcranConflits> Traiter(long id, bool traites, string? table)
        => TraiterAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, traites, table);

    [JSInvokable("EcranConflits.Alerte")]
    public static Task<AlerteFichesEnConflit?> Alerte() => AlerteAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    public static async Task<VueEcranConflits> OuvrirAsync(ClientServeur? serveur, string? greffe, bool traites = false, string? table = null,
                                                           string? message = null, bool echec = false)
    {
        if (serveur is null) return Demonstration(traites, table);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null, "Aucun greffe n'est ouvert dans cette session : les fiches en conflit se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var adresse = RoutesConflits.Pour(RoutesConflits.Ecarts, greffe) + $"?traites={(traites ? "true" : "false")}" +
                          (string.IsNullOrWhiteSpace(table) ? "" : "&table=" + Uri.EscapeDataString(table));
            var vue = await serveur.LireAsync<VueEcarts>(adresse);
            var alerte = await serveur.LireAsync<AlerteEcarts>(RoutesConflits.Pour(RoutesConflits.Alerte, greffe));
            // Le bouton suit le droit du serveur, relu ici (le serveur le reverifie a chaque « Traité »).
            var droit = await serveur.LireAsync<VerificationDroit>(RoutesDroits.Pour(RoutesDroits.Verifier, greffe) + "?fonction=" + RoutesConflits.FonctionSuivi);
            return new VueEcranConflits("serveur", message, echec, greffe, droit.Autorise, traites, string.IsNullOrWhiteSpace(table) ? null : table,
                                        vue.Ecarts, vue.Tables, alerte);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse", greffe, $"Les fiches en conflit sont réservées à l'agent que le greffe {greffe} a désigné pour les suivre " +
                                          $"(fonction {RoutesConflits.FonctionSuivi}, donnée par la gestion des droits).");
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

    /// <summary>Marque l'ecart traite (REC-004 CA03), puis relit la liste : il en sort, et se retrouve dans « Traités ».</summary>
    public static async Task<VueEcranConflits> TraiterAsync(ClientServeur? serveur, string? greffe, long id, bool traites, string? table)
    {
        if (serveur is null)
            return Demonstration(traites, table) with { Message = "Démonstration : sans serveur, aucun écart n'est marqué traité.", Echec = true };
        if (string.IsNullOrWhiteSpace(greffe)) return await OuvrirAsync(serveur, greffe);
        EcartRecopie ecart;
        try
        {
            ecart = await serveur.EnvoyerAsync<object, EcartRecopie>(RoutesConflits.PourEcart(greffe, id), new { });
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return await OuvrirAsync(serveur, greffe, traites, table,
                $"L'écart {id} n'a pas été marqué traité : c'est réservé à l'agent désigné (fonction {RoutesConflits.FonctionSuivi}).", echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return await OuvrirAsync(serveur, greffe, traites, table,
                $"L'écart {id} n'a pas été marqué traité : le serveur de RIG a répondu en erreur ({(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).", echec: true);
        }
        return await OuvrirAsync(serveur, greffe, traites, table,
            $"{ecart.Fiche} : marqué traité par {ecart.TraitePar} le {ecart.TraiteLe:dd/MM/yyyy à HH:mm}.");
    }

    /// <summary>L'alerte de l'accueil pour l'agent de la session ; null s'il n'est pas designe (403) ou sans greffe.</summary>
    public static async Task<AlerteFichesEnConflit?> AlerteAsync(ClientServeur? serveur, string? greffe)
    {
        AlerteEcarts a;
        if (serveur is null) a = Demonstration(false, null).Alerte!;
        else if (string.IsNullOrWhiteSpace(greffe)) return null;
        else
            try { a = await serveur.LireAsync<AlerteEcarts>(RoutesConflits.Pour(RoutesConflits.Alerte, greffe)); }
            catch (ErreurServeur e) when (e.CodeHttp is 403 or 404 or 503) { return null; }
        return Ligne(a);
    }

    /// <summary>La ligne d'alerte de l'accueil tiree du compte du serveur (CA02 : le nombre ; CA03 : rouge a 5 jours ouvres).</summary>
    public static AlerteFichesEnConflit Ligne(AlerteEcarts a)
        => new(a.NonTraites, a.EnRouge, a.EnRouge > 0 ? "alerte" : a.NonTraites > 0 ? "action" : "ok",
               a.NonTraites == 0 ? "Aucune fiche modifiée des deux côtés" : $"{a.NonTraites} fiche(s) modifiée(s) des deux côtés",
               a.EnRouge > 0 ? $"dont {a.EnRouge} en attente depuis {RoutesConflits.JoursOuvresAvantRouge} jours ouvrés ou plus"
                             : a.NonTraites > 0 ? "la V3 a gagné : ce que l'ancien RIG avait écrit est à revoir" : "rien à revoir",
               Code);

    private static VueEcranConflits Vide(string mode, string? greffe, string message)
        => new(mode, message, true, greffe, false, false, null, [], [], null);

    /// <summary>
    /// Sans serveur : trois ecarts inventes sur les colonnes reelles des fiches partagees (noms fictifs), dont un en rouge.
    /// Ils vivent ici plutot que dans Metier/JeuDemo.cs : seul cet ecran les montre.
    /// </summary>
    private static VueEcranConflits Demonstration(bool traites, string? table)
    {
        var maintenant = DateTime.Now;
        EcartRecopie[] tous =
        [
            new(3, "CLIENT", "41877", "41877", "client 41877 — SARL BOULANGERIE DES REMPARTS", "ecran:PROC_FCLIENT", maintenant.AddMinutes(-12),
                maintenant.AddMinutes(-20), null, maintenant.AddMinutes(-14), "Claire MARTIN",
                [new("CLIEN_EMAIL", "contact@remparts-boulangerie.fr", "compta@remparts-boulangerie.fr"),
                 new("CLIEN_TELEPHONE", "03 22 91 40 12", "03 22 91 40 10")], null, null, false),
            new(2, "DOSSIER_RCS", "250918", "250918", "dossier RCS 250918 — 2019B00412", "dossier:2019B00412", maintenant.AddHours(-3),
                maintenant.AddHours(-3).AddMinutes(-5), null, maintenant.AddHours(-3).AddMinutes(-2), "Julien PETIT",
                [new("DSSRC_DATE_RADIATION", "2026-11-18T00:00:00", null)], null, null, false),
            new(1, "ADRESSE_BASE", "1180522", "1180522", "adresse 1180522 — RUE DES JACOBINS", null, maintenant.AddDays(-9),
                maintenant.AddDays(-9).AddMinutes(-30), null, maintenant.AddDays(-9).AddMinutes(-10), "Claire MARTIN",
                [new("ADRBS_NUM_VOIE", "12", "14"), new("ADRBS_CODE_POSTAL", "80000", "80080")], null, null, true),
        ];
        var choisis = tous.Where(e => (table is null || e.TableV2 == table) && !traites).ToArray();
        return new VueEcranConflits("demo", null, false, null, false, traites, table, choisis, [.. tous.Select(e => e.TableV2).Distinct().Order()],
                                    new AlerteEcarts(tous.Length, tous.Count(e => e.EnRouge), tous.Min(e => e.DetecteLe)));
    }
}
