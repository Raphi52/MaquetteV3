using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Les criteres du journal : partie (« np », « p » ou null), code d'evenement, du et au (aaaa-mm-jj, heure du greffe).</summary>
public sealed record FiltreJournal(string? Partie, short? Code, string? Du, string? Au);

/// <summary>
/// Ce que l'ecran « Certification NF525 » affiche.
/// </summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403) ou « erreur ».</param>
/// <param name="Message">Le compte rendu du dernier geste (controle, purge) ou la raison d'un refus ; null sinon.</param>
/// <param name="Echec">Le message est un refus, une erreur ou un defaut trouve (filet rouge).</param>
public sealed record VueEcranChaines(
    string Mode,
    string? Message,
    bool Echec,
    string? Greffe,
    EtatCertification? Etat,
    FiltreJournal Filtre,
    EvenementSigne[] Journal,
    GrandTotal[] Totaux,
    CodeEvenement[] Codes,
    BilanIntegrite? Controle);

/// <summary>
/// Pont de l'ecran « Certification NF525 » (web/src/ecrans/EcranChaines.tsx), SOC-018 T03, « a maquetter » : la
/// consultation des chaines signees de la V3. Il reprend PROC_CERTIF de la V2 (FORM_CERTIF : onglets « Journal non
/// purgeable », « Journal purgeable », « Grands totaux Factures », « Grands totaux Encaissements », bouton « Purger
/// journal periodique », etat « Integrite » ; sources dans D:\RigApplication, branche origin/Development,
/// Source\RIG\DLL\Processus\PROC_CERTIF, sans fiche de reference). Le comptable y lit le journal des evenements
/// filtre par type et par date et les grands totaux, en lecture seule ; il lance le controle d'integrite ; il purge la
/// seule partie purgeable d'un exercice archive (CA07). Les regles sont au serveur (Serveur/Socle/Chaines, prouvees par
/// tests/SocleChainesCheck) : l'ecran montre sa reponse. Sans RIGV3_SERVEUR : une demonstration en lecture seule.
/// </summary>
public static class EcranChaines
{
    public const string NomEcran = "Certification NF525";

    private static readonly FiltreJournal ToutLeJournal = new(null, null, null, null);

    [JSInvokable("EcranChaines.Ouvrir")]
    public static Task<VueEcranChaines> Ouvrir() => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, ToutLeJournal);

    [JSInvokable("EcranChaines.Rechercher")]
    public static Task<VueEcranChaines> Rechercher(FiltreJournal filtre) => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, filtre);

    [JSInvokable("EcranChaines.Controler")]
    public static Task<VueEcranChaines> Controler(FiltreJournal filtre) => ControlerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, filtre);

    [JSInvokable("EcranChaines.Purger")]
    public static Task<VueEcranChaines> Purger(DemandePurge demande) => PurgerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, demande);

    /// <summary>L'etat, le journal filtre, les codes et les grands totaux du greffe, lus sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueEcranChaines> OuvrirAsync(ClientServeur? serveur, string? greffe, FiltreJournal filtre,
                                                          string? message = null, bool echec = false, BilanIntegrite? controle = null)
    {
        if (serveur is null) return Demonstration(filtre);
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null, filtre,
                "Aucun greffe n'est ouvert dans cette session : les chaînes signées se lisent dans la base du greffe choisi à l'ouverture de RIG.");
        try
        {
            var etat = await serveur.LireAsync<EtatCertification>(RoutesChaines.Pour(RoutesChaines.Etat, greffe));
            var codes = await serveur.LireAsync<CodeEvenement[]>(RoutesChaines.Pour(RoutesChaines.Codes, greffe));
            var journal = await serveur.LireAsync<EvenementSigne[]>(RoutesChaines.Pour(RoutesChaines.Journal, greffe) + Requete(filtre));
            var totaux = await serveur.LireAsync<GrandTotal[]>(RoutesChaines.Pour(RoutesChaines.Totaux, greffe));
            return new VueEcranChaines("serveur", message, echec, greffe, etat, filtre, journal, totaux, codes, controle);
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse", greffe, filtre, EcranReglages.Motif(e) ?? $"Vous n'avez pas accès aux chaînes signées du greffe {greffe}.");
        }
        catch (ErreurServeur e) when (e.CodeHttp is 404 or 503)
        {
            return Vide("erreur", greffe, filtre, e.CodeHttp == 404
                ? $"Le greffe {greffe} n'est pas au catalogue des greffes."
                : $"La base du greffe {greffe} ne répond pas. Réessayez dans un instant ou appelez l'assistance d'Amitel.");
        }
        catch (ErreurServeur e) when (e.CodeHttp == 400)
        {
            return Vide("serveur", greffe, filtre, EcranReglages.Motif(e) ?? "Critères de recherche illisibles.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name;
            return Vide("erreur", greffe, filtre, $"Le serveur de RIG ne répond pas ({serveur.Adresse}, {code}).");
        }
    }

    /// <summary>Le controle d'integrite (CA04) puis la relecture : le bilan s'affiche, et les evenements 90 / 95 qu'il a ecrits.</summary>
    public static async Task<VueEcranChaines> ControlerAsync(ClientServeur? serveur, string? greffe, FiltreJournal filtre)
    {
        if (serveur is null)
            return Demonstration(filtre) with { Message = "Démonstration : sans serveur, aucun contrôle n'est lancé.", Echec = true };
        if (string.IsNullOrWhiteSpace(greffe)) return await OuvrirAsync(serveur, greffe, filtre);
        BilanIntegrite bilan;
        try
        {
            bilan = await serveur.EnvoyerAsync<object, BilanIntegrite>(RoutesChaines.Pour(RoutesChaines.Controle, greffe), new { });
        }
        catch (ErreurServeur e) when (e.CodeHttp is 403 or 409)
        {
            return await OuvrirAsync(serveur, greffe, filtre, EcranReglages.Motif(e) ?? "Le contrôle a été refusé.", echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return await OuvrirAsync(serveur, greffe, filtre,
                $"Le contrôle n'a pas pu se faire : le serveur de RIG a répondu en erreur ({(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).", echec: true);
        }
        return await OuvrirAsync(serveur, greffe, filtre, CompteRendu(bilan), echec: !bilan.Integre || !bilan.ValeurProbante, controle: bilan);
    }

    /// <summary>La purge du journal purgeable d'un exercice archive (CA07) ; un refus (pas d'archive) s'affiche tel quel.</summary>
    public static async Task<VueEcranChaines> PurgerAsync(ClientServeur? serveur, string? greffe, DemandePurge demande)
    {
        var purgeable = new FiltreJournal("p", null, null, null);
        if (serveur is null)
            return Demonstration(purgeable) with { Message = "Démonstration : sans serveur, rien n'est purgé.", Echec = true };
        if (string.IsNullOrWhiteSpace(greffe)) return await OuvrirAsync(serveur, greffe, purgeable);
        ResultatPurge resultat;
        try
        {
            resultat = await serveur.EnvoyerAsync<DemandePurge, ResultatPurge>(RoutesChaines.Pour(RoutesChaines.Purge, greffe), demande);
        }
        catch (ErreurServeur e) when (e.CodeHttp is 400 or 403 or 409)
        {
            return await OuvrirAsync(serveur, greffe, purgeable, EcranReglages.Motif(e) ?? "La purge a été refusée.", echec: true);
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            return await OuvrirAsync(serveur, greffe, purgeable,
                $"La purge n'a pas pu se faire : le serveur de RIG a répondu en erreur ({(e as ErreurServeur)?.CodeHttp.ToString() ?? e.GetType().Name}).", echec: true);
        }
        return await OuvrirAsync(serveur, greffe, purgeable, resultat.Message);
    }

    /// <summary>La phrase du bilan d'un controle.</summary>
    public static string CompteRendu(BilanIntegrite b)
    {
        if (!b.ValeurProbante) return b.Reserve ?? "Contrôle sans valeur probante.";
        var maillons = b.Chaines.Sum(c => c.Maillons);
        if (b.Integre) return $"Intégrité vérifiée : {maillons} maillon(s) dans les 7 chaînes, aucun défaut ni rupture.";
        var defauts = b.Chaines.Sum(c => c.Defauts.Length) + b.Archives.Length;
        var ruptures = b.Chaines.Sum(c => c.Ruptures.Length);
        return $"Contrôle : {defauts} défaut(s) d'intégrité et {ruptures} rupture(s) de séquence, écrits au journal non purgeable " +
               $"(événements 90 et 95). Premier : {b.Chaines.SelectMany(c => c.Defauts.Concat(c.Ruptures).Select(x => $"{c.Libelle}, {x}")).Concat(b.Archives).First()}.";
    }

    private static string Requete(FiltreJournal f)
    {
        var p = new List<string>();
        if (!string.IsNullOrWhiteSpace(f.Partie)) p.Add("partie=" + Uri.EscapeDataString(f.Partie.Trim()));
        if (f.Code is { } c) p.Add("code=" + c);
        if (!string.IsNullOrWhiteSpace(f.Du)) p.Add("du=" + Uri.EscapeDataString(f.Du.Trim()));
        if (!string.IsNullOrWhiteSpace(f.Au)) p.Add("au=" + Uri.EscapeDataString(f.Au.Trim()));
        return p.Count == 0 ? "" : "?" + string.Join("&", p);
    }

    private static VueEcranChaines Vide(string mode, string? greffe, FiltreJournal filtre, string message)
        => new(mode, message, true, greffe, null, filtre, [], [], [], null);

    /// <summary>
    /// Sans serveur : un greffe fictif dont les chaines sont ouvertes le 01/09/2026 -- quelques evenements et grands
    /// totaux FABRIQUES pour montrer la forme de l'ecran (aucune donnee de la V2 : ses chaines restent les siennes).
    /// Le filtre s'applique comme le ferait le serveur.
    /// </summary>
    private static VueEcranChaines Demonstration(FiltreJournal filtre)
    {
        static DateTime U(int mois, int jour, int h, int m) => new(2026, mois, jour, h, m, 0, DateTimeKind.Utc);
        static string Jour(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        EvenementSigne[] tous =
        [
            new(9, U(11, 20, 19, 2), 50, "Clôture de période (Jour/Mois)", "Clôture de la journée du 20/11/2026 : 14 facture(s) pour 1 284,60 € (cumul perpétuel 61 902,35 €), 12 encaissement(s) pour 1 104,10 €.", "Partie serveur de la V3", true, "JP", 2026, 64),
            new(8, U(11, 20, 15, 41), 155, "Duplicata d'une note", "Duplicata n° 1 de la facture V3-26-000412 imprimé par AMITEL\\agent.compta : demande du client", "AMITEL\\agent.compta", true, "JP", 2026, 63),
            new(7, U(11, 18, 9, 5), 10, "Changement du mode de gestion de la séquence continue [1]", "Changement du mode de la série FACV3 2026 : format LA-C -> LA-C, codage de l'année 2 -> 2, numéros rendus redonnés non -> non", "AMITEL\\agent.compta", false, "JNP", 2026, 6),
            new(6, U(10, 31, 20, 0), 50, "Clôture de période (Jour/Mois)", "Clôture du mois d'octobre 2026 : 268 facture(s) pour 24 410,00 €.", "Partie serveur de la V3", true, "JP", 2026, 41),
            new(5, U(9, 1, 6, 12), 130, "Habilitation d'utilisateur/Gestion des droits", "Ajout du rôle COMPTA au compte AMITEL\\agent.compta ; fonctions de facturation ou d'encaissement : FACTURE", "Agent Compta", true, "JP", 2026, 1),
            new(4, U(9, 1, 6, 0), 210, "Reprise de données d'autres logiciels suite à changement de systèmes", "Reprise des comptes et des droits de la V2 : 42 comptes, 18 rôles.", "Reprise de la V2", false, "JNP", 2026, 3),
            new(3, U(8, 28, 22, 0), 250, "Installation d'une nouvelle version majeure du logiciel", "Version 2026.1.0 livrée (vague base).", "Livraison", false, "JNP", 2026, 2),
            new(2, U(9, 1, 5, 59), 260, "Initialisation des données", "Initialisation des données : ouverture des 7 chaînes signées de la V3 au passage du greffe, vides. Les chaînes de la V2 restent dans la V2.", "Partie serveur de la V3", false, "JNP", 2026, 1),
        ];
        var journal = tous.Where(e => filtre.Partie switch { "np" => !e.Purgeable, "p" => e.Purgeable, _ => true })
                          .Where(e => filtre.Code is null || e.Code == filtre.Code)
                          .Where(e => string.IsNullOrWhiteSpace(filtre.Du) || string.CompareOrdinal(Jour(e.Instant), filtre.Du.Trim()) >= 0)
                          .Where(e => string.IsNullOrWhiteSpace(filtre.Au) || string.CompareOrdinal(Jour(e.Instant), filtre.Au.Trim()) <= 0)
                          .ToArray();
        var etat = new EtatCertification("demo", true, true, U(9, 1, 5, 59), "Romance Standard Time", "3f1c…", 0,
        [
            new("FAC", ChainesSignees.Libelle("FAC"), 2210, 2210, U(11, 20, 18, 55)),
            new("ENC", ChainesSignees.Libelle("ENC"), 1873, 1873, U(11, 20, 18, 40)),
            new("GTF", ChainesSignees.Libelle("GTF"), 83, 83, U(11, 20, 19, 2)),
            new("GTE", ChainesSignees.Libelle("GTE"), 83, 83, U(11, 20, 19, 2)),
            new("DUP", ChainesSignees.Libelle("DUP"), 4, 4, U(11, 20, 15, 41)),
            new("JNP", ChainesSignees.Libelle("JNP"), 6, 6, U(11, 18, 9, 5)),
            new("JP", ChainesSignees.Libelle("JP"), 64, 64, U(11, 20, 19, 2)),
        ], []);
        GrandTotal[] totaux =
        [
            new("GTF", "GTP", "20261120", 1284.60m, 61902.35m, 62310.35m, 14, U(11, 20, 19, 2), "Total du 20 NOVEMBRE 2026", 83, [new(20.00m, 1001.10m), new(0.00m, 283.50m)]),
            new("GTF", "GTP", "20261119", 1702.20m, 60617.75m, 61025.75m, 19, U(11, 19, 19, 1), "Total du 19 NOVEMBRE 2026", 82, [new(20.00m, 1400.20m), new(0.00m, 302.00m)]),
            new("GTF", "GTM", "202610", 24410.00m, 24410.00m, 24818.00m, 268, U(10, 31, 20, 0), "Total de OCTOBRE 2026", 52, [new(20.00m, 20120.00m), new(0.00m, 4290.00m)]),
            new("GTE", "GTP", "20261120", 1104.10m, 55410.90m, 55410.90m, 12, U(11, 20, 19, 2), "Total du 20 NOVEMBRE 2026", 83, []),
            new("GTE", "GTP", "20261119", 1580.00m, 54306.80m, 54306.80m, 17, U(11, 19, 19, 1), "Total du 19 NOVEMBRE 2026", 82, []),
        ];
        CodeEvenement[] codes =
        [
            new(10, "Changement du mode de gestion de la séquence continue [1]", false, "C"),
            new(30, "Archivage fiscal d'année ou d'exercice", false, "C"),
            new(50, "Clôture de période (Jour/Mois)", true, "X"),
            new(60, "Clôture annuelle ou d'exercice", false, "C"),
            new(90, "Détection d'un défaut d'intégrité dans les données sécurisées ou dans une archive fiscale", false, "C"),
            new(95, "Détection d'une rupture de la séquence continue", false, "C"),
            new(130, "Habilitation d'utilisateur/Gestion des droits", true, "C"),
            new(155, "Duplicata d'une note", true, "C"),
            new(205, "Purge Partielle du JET", false, "C"),
            new(210, "Reprise de données d'autres logiciels suite à changement de systèmes", false, "C"),
            new(250, "Installation d'une nouvelle version majeure du logiciel", false, "X"),
            new(260, "Initialisation des données", false, "C"),
        ];
        return new VueEcranChaines("demo", null, false, null, etat, filtre, journal, totaux, codes, null);
    }
}
