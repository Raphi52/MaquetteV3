using System.Net.Http;
using RigV3.Contrats.Recopie;

namespace RigV3Desktop.Pont;

/// <summary>
/// Ce que l'ecran « Historique de l'ancien RIG » affiche.
/// </summary>
/// <param name="Mode">« serveur » (base V3 du greffe), « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403), « vide »
/// (critere ou valeur manquants) ou « erreur » (serveur, greffe ou base injoignable ; recopie pas en service).</param>
/// <param name="Critere">« siren », « gestion », « client », « arrivee » (historique), « ancien », « rig » (anciens numeros) ou
/// « reprises » (fiches des anciennes reprises a verifier).</param>
public sealed record VueEcranHistorique(
    string Mode,
    string? Message,
    string? Greffe,
    string Critere,
    string? Valeur,
    VueHistorique? Historique,
    ArriveeDeuxLogiciels? Arrivee,
    AncienNumeroVue[] Anciens,
    FicheAncienneRepriseVue[] Reprises);

/// <summary>
/// Pont de l'ecran « Historique de l'ancien RIG » (web/src/ecrans/EcranHistoriqueV2.tsx) : ecran NEUF de la V3, « a maquetter »
/// dans le carnet (REC-010 T04, REC-011 T04, REC-009 T04, REC-018 T04). La V2 n'a pas d'ecran qui montre les affaires de
/// l'autre logiciel ; sa recherche par ancien numero (PROC_CORRESPMIG, PROC_CORRESP_MIGRATION / ORACLE / GAGI) est reprise ici
/// comme critere. Tout se lit dans la base V3 du greffe, en lecture seule :
///   - l'historique d'une entreprise (SIREN), d'un dossier (numero de gestion), d'un client (code abrege) ou d'une arrivee
///     (numero) : les affaires des deux logiciels, les anciennes marquees « traitee dans l'ancien RIG », leurs demandes, leur
///     etat, leurs liens et leurs echanges (REC-010) ; une demande liee s'ouvre en lecture (REC-011 CA03) ;
///   - une arrivee et ses demandes des deux logiciels (REC-018 CA03) ;
///   - les anciens numeros (REC-009 CA01, CA02) et les fiches creees par les anciennes reprises (CA03).
/// Le greffe vient de la session (<see cref="SessionAgent.CodeGreffe"/>) ; sans RIGV3_SERVEUR, une demonstration.
/// </summary>
public static class EcranHistoriqueV2
{
    public const string NomEcran = "Historique de l'ancien RIG";
    public const string Code = "PROC_HISTORIQUE_ANCIEN_RIG";

    /// <summary>Les criteres de l'ecran, dans l'ordre de sa liste.</summary>
    public static readonly IReadOnlyList<string> Criteres = ["siren", "gestion", "client", "arrivee", "ancien", "rig", "reprises"];

    [JSInvokable("EcranHistoriqueV2.Chercher")]
    public static Task<VueEcranHistorique> Chercher(string critere, string? valeur)
        => ChercherAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, critere, valeur);

    [JSInvokable("EcranHistoriqueV2.Demande")]
    public static Task<DemandeHistorique?> Demande(int id) => DemandeAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id);

    /// <summary>SOC-015 CA01 (laisse ouvert par le lot 3, decision du chef) : l'historique d'une ancienne affaire, en lecture,
    /// depuis le bloc « Ancien RIG » de la recherche d'affaires -- par son numero de dossier.</summary>
    [JSInvokable("EcranRechAffaires.HistoriqueAncienne")]
    public static Task<VueEcranHistorique> HistoriqueAncienne(string numero)
        => ChercherAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, "gestion", numero);

    public static async Task<VueEcranHistorique> ChercherAsync(ClientServeur? serveur, string? greffe, string critere, string? valeur)
    {
        critere = Criteres.Contains(critere) ? critere : "siren";
        if (critere != "reprises" && string.IsNullOrWhiteSpace(valeur))
            return Vide("vide", greffe, critere, valeur, "Saisissez le numéro à chercher.");
        if (serveur is null) return Demonstration(critere, valeur);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null, critere, valeur, "Aucun greffe n'est ouvert dans cette session : l'historique se lit dans la base du greffe choisi à l'ouverture de RIG.");
        var v = valeur?.Trim() ?? "";
        try
        {
            return critere switch
            {
                "arrivee" => await ArriveeAsync(serveur, greffe, v),
                "ancien" or "rig" => Vide("serveur", greffe, critere, v, null) with
                {
                    Anciens = await serveur.LireAsync<AncienNumeroVue[]>(RoutesHistorique.Pour(RoutesHistorique.AnciensNumeros, greffe) +
                                                                          (critere == "ancien" ? "?numero=" : "?rig=") + Uri.EscapeDataString(v)),
                },
                "reprises" => Vide("serveur", greffe, critere, null, null) with
                {
                    Reprises = await serveur.LireAsync<FicheAncienneRepriseVue[]>(RoutesHistorique.Pour(RoutesHistorique.AnciennesReprises, greffe)),
                },
                _ => Vide("serveur", greffe, critere, v, null) with
                {
                    Historique = await serveur.LireAsync<VueHistorique>(RoutesHistorique.Pour(RoutesHistorique.Historique, greffe) +
                                                                         $"?critere={critere}&valeur={Uri.EscapeDataString(v)}"),
                },
            };
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse", greffe, critere, v, $"L'historique du greffe {greffe} est réservé à ses agents.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 409 or 503)
        {
            return Vide("erreur", greffe, critere, v, e.CodeHttp switch
            {
                404 => $"Le greffe {greffe} n'est pas au catalogue des greffes.",
                409 => $"La recopie entre l'ancien RIG et la V3 n'est pas en service pour le greffe {greffe} : rien à montrer de l'ancien RIG.",
                _ => $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.",
            });
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return Vide("erreur", greffe, critere, v, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).");
        }
    }

    private static async Task<VueEcranHistorique> ArriveeAsync(ClientServeur serveur, string greffe, string numero)
    {
        try
        {
            var a = await serveur.LireAsync<ArriveeDeuxLogiciels>(RoutesHistorique.Pour(RoutesHistorique.Arrivee, greffe).Replace("{numero}", Uri.EscapeDataString(numero)));
            var h = await serveur.LireAsync<VueHistorique>(RoutesHistorique.Pour(RoutesHistorique.Historique, greffe) + $"?critere=arrivee&valeur={Uri.EscapeDataString(numero)}");
            return Vide("serveur", greffe, "arrivee", numero, null) with { Arrivee = a, Historique = h };
        }
        catch (ErreurServeur e) when (e.CodeHttp == 404 && e.Corps?.Contains("arrivée", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Vide("serveur", greffe, "arrivee", numero, $"Aucune arrivée n° {numero} dans ce greffe.");
        }
    }

    /// <summary>Une demande liee, ouverte en lecture (REC-011 CA03) ; null si elle n'existe pas ou sans serveur ni greffe.</summary>
    public static async Task<DemandeHistorique?> DemandeAsync(ClientServeur? serveur, string? greffe, int id)
    {
        if (serveur is null) return DemandesDemo.FirstOrDefault(d => d.Id == id);
        if (string.IsNullOrWhiteSpace(greffe)) return null;
        try
        {
            return await serveur.LireAsync<DemandeHistorique>(RoutesHistorique.Pour(RoutesHistorique.Demande, greffe).Replace("{id}", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static VueEcranHistorique Vide(string mode, string? greffe, string critere, string? valeur, string? message)
        => new(mode, message, greffe, critere, valeur, null, null, [], []);

    // ---------------------------------------------------------------------------------------------------------------
    // La demonstration (sans RIGV3_SERVEUR) : une entreprise qui a deux anciennes affaires et une nouvelle.
    // ---------------------------------------------------------------------------------------------------------------

    private static readonly DateTime Jour = new(2026, 11, 18, 9, 30, 0);

    private static readonly DemandeHistorique[] DemandesDemo =
    [
        new(9001, "702611", Origines.V2, "D2607903046", "MODIFICATION DIRIGEANT", "Q", "qualifiée", Jour.AddDays(-40), null, "Modification", "2026A004512",
            false, null, [new LienDemandeVue("bloque", 9301, "V300000412", Origines.V3, "Q", "qualifiée", false, null)]),
        new(9002, "702612", Origines.V2, "D2607903047", "DEPOT DES COMPTES 2025", "V", "validée", Jour.AddDays(-38), Jour.AddDays(-37).AddHours(2), "Dépôt des comptes",
            "2026A004512", false, null, []),
        new(9101, "698004", Origines.V2, "D2507800112", "IMMATRICULATION", "V", "validée", Jour.AddDays(-400), Jour.AddDays(-398), "Immatriculation", "2025A001877",
            false, null, []),
        new(9301, null, Origines.V3, "V300000412", "TRANSFERT DE SIEGE", "Q", "qualifiée", Jour, null, "Transfert de siège", "2026V300042", true,
            "Bloquée par la demande D2607903046 de l'ancien RIG",
            [new LienDemandeVue("bloquée par", 9001, "D2607903046", Origines.V2, "Q", "qualifiée", false, null),
             new LienDemandeVue("née de", 9002, "D2607903047", Origines.V2, "V", "validée", false, null)]),
    ];

    private static VueEcranHistorique Demonstration(string critere, string? valeur)
    {
        const string Message = "Démonstration : sans serveur, une entreprise inventée s'affiche, en lecture seule.";
        if (critere is "ancien" or "rig")
            return Vide("demo", null, critere, valeur, Message) with
            {
                Anciens =
                [
                    new("migration", "DOSSIER_RCS", "dossier RCS", "NO-DED-2004-007", "DED-2004-007", 177021, "2004B00117", "IG2004B00117", "rec.DossierRcs", "177021"),
                    new("GAGI", "PROCEDURE_COLLECTIVE", "procédure collective", "77012", "GAGI-77012", 5512, "2009J00042", "IG2009J00042", null, null),
                ],
            };
        if (critere == "reprises")
            return Vide("demo", null, critere, null, Message) with
            {
                Reprises =
                [
                    new("CLIENT", "578727", "rec.Client", "578727", "client « Fake data » d'une ancienne reprise", "Fake data PC-0001"),
                    new("CLIENT", "578729", "rec.Client", "578729", "client créé automatiquement par une ancienne reprise", "SCP MARTIN"),
                    new("ADRESSE_BASE", "889076", "rec.Adresse", "889075", "adresse inconnue d'une ancienne reprise", "* Adresse inconnu pour le client* 42000 SAINT ETIENNE"),
                ],
            };
        var echanges = new[]
        {
            // Les libelles sont ceux de la base des echanges (FLUX_SORTANT_ETAT, FLUX_ENTRANT_ETAT d'EDI_GREFFE_2025, SQL-DEV, lus le 22/11/2026).
            new EchangeHistorique("envoi", 41207, "INPI_FORMALITE", 7, "Erreur d'intégration", EchangeHistorique.EtatEnErreur(7), Jour.AddDays(-37).AddHours(3),
                                  "Dépôt des comptes 2025", "Pièce jointe absente : le fichier des comptes annuels n'a pas été trouvé.", "D2607903047"),
            new EchangeHistorique("envoi", 41231, "INPI_FORMALITE", 2, "Intégré avec succès", EchangeHistorique.EtatEnErreur(2), Jour.AddDays(-37).AddHours(5),
                                  "Dépôt des comptes 2025", null, "D2607903047"),
            new EchangeHistorique("réception", 52119, "INPI_RETOUR", 2, "Intégré avec succès", EchangeHistorique.EtatEnErreur(2), Jour.AddDays(-36),
                                  "Accusé de réception INPI", null, "D2607903047"),
        };
        var affaires = new[]
        {
            new AffaireHistorique(5502427, null, Origines.V3, Origines.MarqueNouvelle, "en cours", "TRANSFERT DE SIEGE", Jour, "BOULANGERIE DUPONT", "DUPONT01",
                ["2004B00117"], [DemandesDemo[3]], [], null),
            new AffaireHistorique(5495750, "5495750", Origines.V2, Origines.MarqueAncienne, "en cours", "MODIFICATIONS 2026", Jour.AddDays(-40), "BOULANGERIE DUPONT",
                "DUPONT01", ["2004B00117"], [DemandesDemo[0], DemandesDemo[1]], echanges, null),
            new AffaireHistorique(5480011, "5480011", Origines.V2, Origines.MarqueAncienne, "terminée", "IMMATRICULATION", Jour.AddDays(-400), "BOULANGERIE DUPONT",
                "DUPONT01", ["2004B00117"], [DemandesDemo[2]], [], null),
        };
        if (critere == "arrivee")
            return Vide("demo", null, critere, valeur, Message) with
            {
                Arrivee = new ArriveeDeuxLogiciels(7092482, "7192482", "2026V300042", "C", Jour.AddHours(-1), "PLI MIXTE", "REF CABINET 12", [DemandesDemo[3], DemandesDemo[0]], 1),
                Historique = new VueHistorique("arrivee", "2026V300042", "Arrivée 2026V300042 du 18/11/2026", [affaires[0], affaires[1]], 1, 1, null),
            };
        return Vide("demo", null, critere, valeur, Message) with
        {
            Historique = new VueHistorique(critere, valeur ?? "", "BOULANGERIE DUPONT — SIREN 512345678", affaires, 2, 1, null),
        };
    }
}
