using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Les criteres de recherche du journal technique (dates au format yyyy-MM-dd, UTC).</summary>
public sealed record RechercheErreurs(string? Suivi, string? Greffe, string? Compte, string? Du, string? Au);

/// <summary>
/// Ce que l'ecran « Supervision » affiche.
/// </summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « refuse » (403 : pas un compte d'Amitel) ou « erreur ».</param>
/// <param name="Message">Le compte rendu du dernier geste (relance, recherche), ou la raison d'un refus ; null sinon.</param>
/// <param name="Echec">Le message est un refus ou une erreur.</param>
/// <param name="Erreurs">Le resultat de la derniere recherche dans le journal technique (les plus recentes a l'ouverture).</param>
public sealed record VueEcranSupervision(
    string Mode,
    string? Message,
    bool Echec,
    EtatSupervision? Etat,
    ErreurTechnique[] Erreurs);

/// <summary>
/// Pont de l'ecran « Supervision » (web/src/ecrans/EcranSupervision.tsx) : ecran NEUF de la V3, « a maquetter » dans le
/// carnet (SOC-011 T04, SOC-017 T04), reserve a l'exploitation et a l'assistance d'Amitel. Il remplace, en un endroit,
/// les outils de la V2 : les tables COMMUN_RIG ETAT_SERVICE et LOG_RIG_ERREUR lues a la main (supervision.md), les
/// fichiers de journal du serveur de l'agent (C:\rig\data\log, \\rigapp-territoire pour l'outre-mer) et l'outil
/// MaintenanceServices pour relancer un service. Il montre les 40 greffes (base V3, migrations, echecs de numerotation
/// de l'ancien RIG), les traitements publies par chaque serveur, les alertes, et cherche une erreur par son identifiant
/// de suivi, son greffe, son compte ou sa periode. Tout vient du serveur (base commune) ; sans RIGV3_SERVEUR, une
/// demonstration en lecture seule.
///
/// Il porte aussi l'envoi des erreurs de l'ecran au serveur (SOC-017 T03) : <see cref="SignalerAsync"/>, appele par
/// le repartiteur du pont quand une methode echoue, rend l'identifiant de suivi a montrer a l'agent.
/// </summary>
public static class EcranSupervision
{
    /// <summary>Le nom de l'ecran (menu, catalogue des ecrans).</summary>
    public const string NomEcran = "Supervision";

    [JSInvokable("EcranSupervision.Ouvrir")]
    public static Task<VueEcranSupervision> Ouvrir() => OuvrirAsync(ClientServeur.Actif);

    [JSInvokable("EcranSupervision.Rechercher")]
    public static Task<VueEcranSupervision> Rechercher(RechercheErreurs criteres) => RechercherAsync(ClientServeur.Actif, criteres);

    [JSInvokable("EcranSupervision.Relancer")]
    public static Task<VueEcranSupervision> Relancer(string traitement) => RelancerAsync(ClientServeur.Actif, traitement);

    /// <summary>L'etat des greffes et des traitements, et les erreurs les plus recentes, lus sur <paramref name="serveur"/>.</summary>
    public static async Task<VueEcranSupervision> OuvrirAsync(ClientServeur? serveur, string? message = null, bool echec = false,
                                                              ErreurTechnique[]? erreurs = null)
    {
        if (serveur is null) return Demonstration();
        try
        {
            var etat = await serveur.LireAsync<EtatSupervision>(RoutesSupervision.Etat);
            erreurs ??= await serveur.LireAsync<ErreurTechnique[]>(RoutesSupervision.Erreurs);
            return new VueEcranSupervision("serveur", message, echec, etat, erreurs);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Refus(serveur, e);
        }
    }

    /// <summary>
    /// Cherche dans le journal technique. Un identifiant de suivi donne rend tout son parcours, dans l'ordre (SOC-017 CA02) ;
    /// sinon greffe, compte et periode, les plus recentes en premier.
    /// </summary>
    public static async Task<VueEcranSupervision> RechercherAsync(ClientServeur? serveur, RechercheErreurs criteres)
    {
        if (serveur is null) return Demonstration() with { Message = "Démonstration : la recherche demande le serveur de RIG.", Echec = true };
        ErreurTechnique[] erreurs;
        try
        {
            erreurs = !string.IsNullOrWhiteSpace(criteres.Suivi)
                ? await serveur.LireAsync<ErreurTechnique[]>(RoutesSupervision.PourParcours(criteres.Suivi.Trim()))
                : await serveur.LireAsync<ErreurTechnique[]>(RoutesSupervision.Erreurs + Requete(criteres));
        }
        catch (ErreurServeur e) when (e.CodeHttp == 400)
        {
            return await OuvrirAsync(serveur, Motif(e) ?? "Critères de recherche illisibles.", echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Refus(serveur, e);
        }
        var message = erreurs.Length == 0
            ? "Aucune erreur ne répond à cette recherche."
            : !string.IsNullOrWhiteSpace(criteres.Suivi)
                ? $"Parcours {criteres.Suivi.Trim().ToUpperInvariant()} : {erreurs.Length} message(s), du premier au dernier."
                : $"{erreurs.Length} erreur(s) trouvée(s), la plus récente en premier.";
        return await OuvrirAsync(serveur, message, echec: false, erreurs);
    }

    /// <summary>Relance un traitement arrete (SOC-011 CA04) : le serveur trace le geste avec son auteur.</summary>
    public static async Task<VueEcranSupervision> RelancerAsync(ClientServeur? serveur, string traitement)
    {
        if (serveur is null) return Demonstration() with { Message = "Démonstration : sans serveur, aucun traitement n'est relancé.", Echec = true };
        try
        {
            var r = await serveur.EnvoyerAsync<DemandeRelance, ResultatRelance>(RoutesSupervision.Relancer, new DemandeRelance(traitement));
            return await OuvrirAsync(serveur,
                $"Traitement {r.Traitement} relancé ({r.EtatAvant} → {r.EtatApres}) : {r.EnFile} élément(s) en file repris, rien n'est perdu.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 409)
        {
            return await OuvrirAsync(serveur, Motif(e) ?? $"Le traitement {traitement} n'a pas été relancé.", echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Refus(serveur, e);
        }
    }

    /// <summary>
    /// SOC-017 T03 : une erreur non prevue de l'ecran (une methode du pont qui leve) part au journal technique ; rend ce
    /// que l'agent doit voir (l'identifiant de suivi). Sans serveur, ou s'il ne repond pas, null : l'erreur reste dans
    /// la sortie de debogage du poste, comme avant. Ne leve jamais : signaler une erreur ne doit pas en creer une autre.
    /// Si l'erreur vient du serveur (500 avec un identifiant), elle rejoint le MEME parcours.
    /// </summary>
    public static async Task<SuiviErreur?> SignalerAsync(ClientServeur? serveur, string programme, Exception cause, string? greffe)
    {
        if (serveur is null) return null;
        try
        {
            using var delai = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await serveur.EnvoyerAsync<ErreurEcran, SuiviErreur>(RoutesSupervision.ErreursEcran,
                new ErreurEcran(programme, cause.Message, cause.GetType().FullName, cause.ToString(), greffe,
                    (cause as ErreurServeur)?.Suivi, Environment.MachineName), delai.Token);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[supervision] erreur de l'ecran non transmise ({programme}) : {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// <see cref="SignalerAsync"/> pour le repartiteur du pont (Pont/Repartiteur.cs) : le serveur et le greffe de la
    /// session. Une adresse RIGV3_SERVEUR illisible n'est pas une seconde panne : l'erreur d'origine la dit deja.
    /// </summary>
    public static Task<SuiviErreur?> SignalerDepuisLePontAsync(string programme, Exception cause)
    {
        ClientServeur? serveur;
        try { serveur = ClientServeur.Actif; }
        catch (InvalidOperationException) { return Task.FromResult<SuiviErreur?>(null); }
        return SignalerAsync(serveur, programme, cause, SessionAgent.CodeGreffe);
    }

    private static string Requete(RechercheErreurs c)
    {
        var parties = new List<string>();
        void Ajouter(string cle, string? valeur)
        {
            if (!string.IsNullOrWhiteSpace(valeur)) parties.Add($"{cle}={Uri.EscapeDataString(valeur.Trim())}");
        }
        Ajouter("greffe", c.Greffe);
        Ajouter("compte", c.Compte);
        Ajouter("du", c.Du);
        Ajouter("au", c.Au);
        return parties.Count == 0 ? "" : "?" + string.Join("&", parties);
    }

    /// <summary>Le motif que le serveur donne d'un refus (RefusGreffe), s'il en donne un.</summary>
    private static string? Motif(ErreurServeur e)
    {
        if (e.Corps is null) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<RefusGreffe>(e.Corps, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))?.Motif; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static VueEcranSupervision Refus(ClientServeur serveur, Exception e)
        => e is ErreurServeur { CodeHttp: 401 or 403 }
            ? new("refuse", "La supervision est réservée à l'exploitation et à l'assistance d'Amitel.", true, null, [])
            : new("erreur", $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).",
                  true, null, []);

    // --- La demonstration ---------------------------------------------------------------------------------

    /// <summary>
    /// Sans serveur : les 40 greffes exploites (code, nom, instance), relus le 2026-11-20 dans COMMUN_RIG.dbo.GREFFE
    /// (C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\controles\bases-greffes.json). Les etats, traitements,
    /// alertes et erreurs sont INVENTES pour montrer l'ecran : la base V3 n'existe encore pour aucun greffe.
    /// </summary>
    private static readonly (string Code, string Nom, string Serveur)[] GreffesExploites =
    [
        ("0101", "Bourg en Bresse", @"SQL-PROD\PROD"), ("0501", "Gap", @"SQL-PROD\PROD"), ("0601", "Antibes", @"SQL-PROD\PROD"),
        ("0603", "Grasse", @"SQL-PROD\PROD"), ("0901", "Foix", @"SQL-PROD\PROD"), ("1304", "Salon de Provence", @"SQL-PROD\PROD"),
        ("1501", "Aurillac", @"SQL-PROD\PROD"), ("1801", "Bourges", @"SQL-PROD\PROD"), ("2002", "Bastia", @"SQL-PROD\PROD"),
        ("2602", "Romans", @"SQL-PROD\PROD"), ("2701", "Bernay", @"SQL-PROD\PROD"), ("2801", "Chartres", @"SQL-PROD\PROD"),
        ("2903", "Quimper", @"SQL-PROD\PROD"), ("3003", "Nimes", @"SQL-PROD\PROD"), ("3801", "Grenoble", @"SQL-PROD\PROD"),
        ("3802", "Vienne", @"SQL-PROD\PROD"), ("3902", "Lons", @"SQL-PROD\PROD"), ("4202", "Saint-Etienne", @"SQL-PROD\PROD"),
        ("4302", "Le Puy", @"SQL-PROD\PROD"), ("5103", "Reims", @"SQL-PROD\PROD"), ("5401", "Briey", @"SQL-PROD\PROD"),
        ("5501", "Bar-le-Duc", @"SQL-PROD\PROD"), ("5601", "Lorient", @"SQL-PROD\PROD"), ("5802", "Nevers", @"SQL-PROD\PROD"),
        ("5902", "Dunkerque", @"SQL-PROD\PROD"), ("6601", "Perpignan", @"SQL-PROD\PROD"), ("6901", "Lyon", @"SQL-PROD\PROD"),
        ("6903", "Villefranche", @"SQL-PROD\PROD"), ("7106", "Macon", @"SQL-PROD\PROD"), ("7401", "Annecy", @"SQL-PROD\PROD"),
        ("7402", "Thonon", @"SQL-PROD\PROD"), ("7606", "Le Havre", @"SQL-PROD\PROD"), ("8002", "Amiens", @"SQL-PROD\PROD"),
        ("8305", "Toulon", @"SQL-PROD\PROD"), ("9711", "Basse-Terre", "RIGBD-ANTILLES"), ("9712", "Pointe-à-Pitre", "RIGBD-ANTILLES"),
        ("9721", "Fort-de-France", "RIGBD-ANTILLES"), ("9741", "St Denis de la Réunion", "RIGBD-REUNION"),
        ("9761", "Mamoudzou", "RIGBD-REUNION"), ("9871", "Papeete", "RIGBD-POLYNESIE"),
    ];

    private static VueEcranSupervision Demonstration()
    {
        var passage = new DateTime(2026, 11, 21, 8, 30, 0, DateTimeKind.Utc);
        EtatGreffe Greffe((string Code, string Nom, string Serveur) g)
        {
            var outreMer = g.Code.StartsWith("97", StringComparison.Ordinal) || g.Code.StartsWith("98", StringComparison.Ordinal);
            return g.Code switch
            {
                // Les trois greffes beta (COMMUN_RIG, SOC-007) : leur base V3 existerait la premiere.
                "4302" => new(g.Code, g.Nom, outreMer, g.Serveur, SantesGreffe.Ok, null, 21, "SOC-011_supervision", 2,
                              passage.AddHours(-3), passage.AddMinutes(-12), @"état 1, RDS-PUY-03, AMITEL\jdupont", passage),
                "2801" => new(g.Code, g.Nom, outreMer, g.Serveur, SantesGreffe.Ok, null, 21, "SOC-011_supervision", 0, null, null, null, passage),
                "3802" => new(g.Code, g.Nom, outreMer, g.Serveur, SantesGreffe.Injoignable,
                              @"Base RIGV3_VIENNE du greffe 3802 injoignable sur SQL-PROD\PROD : délai de connexion dépassé.",
                              null, null, 0, null, null, null, passage),
                "9741" => new(g.Code, g.Nom, outreMer, g.Serveur, SantesGreffe.MigrationsEnRetard,
                              "19 migration(s) jouée(s) sur 21 (dernière : SOC-016_gestes).", 19, "SOC-016_gestes", 0, null, null, null, passage),
                _ => new(g.Code, g.Nom, outreMer, g.Serveur, SantesGreffe.PasCreee, "Base V3 pas encore créée (catalogue, SOC-008).",
                         null, null, null, null, null, "Échecs non relevés : Base\\v2\\bascule.sql n'est pas joué sur l'instance.", passage),
            };
        }
        var etat = new EtatSupervision(passage, [.. GreffesExploites.Select(Greffe)],
        [
            new("RIGV3-APP-01", TraitementsSupervision.PartieServeur, EtatsTraitement.EnMarche, 0, 0, 0, null, "Version 2026.1.0, démarrée le 20/11/2026 06:00 (UTC).", passage, passage),
            new("RIGV3-APP-01", "Courriels du greffe", EtatsTraitement.EnAttente, 0, 3, 0, null, null, passage, passage.AddMinutes(-2)),
            new("RIGV3-APP-01", "Recopie V2 → V3", EtatsTraitement.EnErreur, 0, 14, 1, "4302",
                "Délai d'attente dépassé sur la base RIG_LE_PUY (suivi 7KQ2-M9XP)", passage, passage.AddMinutes(-41)),
        ],
        [
            new(3, ReglesAlerte.TraitementEnErreur, "4302", "Recopie V2 → V3 (RIGV3-APP-01)",
                "Le traitement Recopie V2 → V3 est arrêté en erreur sur un élément du greffe 4302 : Délai d'attente dépassé (suivi 7KQ2-M9XP)",
                passage.AddMinutes(-41), passage.AddMinutes(-41), "simule", null),
            new(2, ReglesAlerte.BaseInjoignable, "3802", TraitementsSupervision.BaseDuGreffe,
                @"La base V3 du greffe 3802 ne répond pas : délai de connexion dépassé.", passage.AddMinutes(-30), passage.AddMinutes(-30), "simule", null),
            new(1, ReglesAlerte.EchecNumerotation, "4302", TraitementsSupervision.Numerotation,
                "2 échec(s) de numérotation dans l'ancien RIG depuis le 21/11/2026 05:30:00 (UTC), le dernier le 21/11/2026 08:18:00.",
                passage.AddHours(-3), passage.AddHours(-3), "simule", null),
        ]);
        ErreurTechnique[] erreurs =
        [
            new(3, "7KQ2-M9XP", passage.AddMinutes(-41), NiveauxErreur.Erreur, OriginesErreur.Traitement, "4302", null, "RIGV3-APP-01",
                "traitement Recopie V2 → V3", "Microsoft.Data.SqlClient.SqlException", "Délai d'attente dépassé sur la base RIG_LE_PUY.", null),
            new(2, "Q3VZ-81HD", passage.AddHours(-2), NiveauxErreur.Grave, OriginesErreur.Ecran, "9741", "mlebon", "RDS-REUNION-02",
                "EcranCompteurs.Rendre", "System.NullReferenceException", "Object reference not set to an instance of an object.", null),
            new(1, "4TNM-2C7R", passage.AddHours(-5), NiveauxErreur.Grave, OriginesErreur.Serveur, "8002", "jdupont", "RIGV3-APP-01",
                "POST /numerotation/{greffe}/restitutions", "System.InvalidOperationException", "Sequence contains no elements.", null),
        ];
        return new VueEcranSupervision("demo", null, false, etat, erreurs);
    }
}
