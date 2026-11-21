using System.Net;

namespace RigV3.Serveur.Socle.Courriels;

/// <summary>
/// Ou part un courriel (SOC-006 CA06), lu dans la section « Courriels » de la configuration du serveur.
///
/// LA REGLE QUI TIENT « JAMAIS DE VRAI COURRIEL HORS PRODUCTION » est ici, dans le code, et pas dans la memoire de
/// celui qui configure : un serveur n'est de production que s'il le DIT (<c>Courriels:Production=true</c>) ; sans cela,
/// un courriel n'est remis qu'a un serveur de messagerie JETABLE du poste (adresse de bouclage : 127.0.0.1, ::1,
/// localhost), et dans tous les autres cas il est range dans la boite de depot (un fichier .eml par envoi) et la
/// trace le note « envoi ignore » (SMTP_IGNORE_DEV), comme la V2 hors production (EmailEnvoye.cs:385-420).
///
/// Ecart avec la V2, voulu : la V2 decidait par la marque « developpement » du GREFFE (GRF_IS_DEV de COMMUN_RIG) et
/// envoyait quand meme, hors production, un courriel dont toutes les adresses etaient chez Amitel
/// (EmailEnvoye.cs:385-387). La V3 decide par le SERVEUR (un greffe de developpement tourne sur un serveur de
/// developpement, decision Q4) et n'a aucune exception : regle du plan, aucun vrai courriel hors production.
/// </summary>
public sealed class OptionsCourriels
{
    public const string Section = "Courriels";
    public const string RemiseDepot = "depot";
    public const string RemiseSmtp = "smtp";

    /// <summary>La V2 garde ses traces 90 jours (EmailEnvoiTrace.cs:14) ; duree a valider par le delegue a la protection des donnees.</summary>
    public const int ConservationParDefaut = 90;

    /// <summary><c>Courriels:Production</c> : ce serveur remet les courriels au vrai serveur de messagerie. Faux par defaut.</summary>
    public bool Production { get; init; }

    /// <summary><c>Courriels:Remise</c> : « depot » (par defaut) ou « smtp ».</summary>
    public string Remise { get; init; } = RemiseDepot;

    /// <summary><c>Courriels:Depot</c> : le dossier de la boite de depot (un .eml par envoi).</summary>
    public string Depot { get; init; } = Path.Combine(Path.GetTempPath(), "RigV3", "courriels");

    /// <summary><c>Courriels:Smtp:Hote</c> : le serveur de messagerie (la V2 : registre Mail\SMTP, srvmail.amitel.fr, RigGreffe.cs:629).</summary>
    public string? SmtpHote { get; init; }

    /// <summary><c>Courriels:Smtp:Port</c> (25 par defaut).</summary>
    public int SmtpPort { get; init; } = 25;

    /// <summary><c>Courriels:ConservationJours</c> : l'age au-dela duquel le menage quotidien efface une trace (CA04).</summary>
    public int ConservationJours { get; init; } = ConservationParDefaut;

    /// <summary><c>Courriels:Menage:Heure</c> : l'heure (locale du serveur) du menage quotidien, 03:00 par defaut.</summary>
    public TimeSpan HeureMenage { get; init; } = new(3, 0, 0);

    /// <summary>Lit la section et refuse une configuration qui enverrait sans le savoir, ou n'enverrait rien en production.</summary>
    public static OptionsCourriels Lire(IConfiguration configuration)
    {
        var s = configuration.GetSection(Section);
        var defaut = new OptionsCourriels();
        var options = new OptionsCourriels
        {
            Production = s.GetValue("Production", false),
            Remise = (s["Remise"] ?? RemiseDepot).Trim().ToLowerInvariant(),
            Depot = string.IsNullOrWhiteSpace(s["Depot"]) ? defaut.Depot : s["Depot"]!.Trim(),
            SmtpHote = string.IsNullOrWhiteSpace(s["Smtp:Hote"]) ? null : s["Smtp:Hote"]!.Trim(),
            SmtpPort = s.GetValue("Smtp:Port", 25),
            ConservationJours = s.GetValue("ConservationJours", ConservationParDefaut),
            HeureMenage = s.GetValue("Menage:Heure", defaut.HeureMenage),
        };
        options.Verifier();
        return options;
    }

    /// <summary>Leve <see cref="InvalidOperationException"/> si la configuration est incoherente : le serveur ne demarre pas.</summary>
    public void Verifier()
    {
        if (Remise is not (RemiseDepot or RemiseSmtp))
            throw new InvalidOperationException($"Courriels:Remise vaut « {Remise} » : « {RemiseDepot} » ou « {RemiseSmtp} » seulement.");
        if (Remise == RemiseSmtp && SmtpHote is null)
            throw new InvalidOperationException("Courriels:Remise vaut « smtp » sans Courriels:Smtp:Hote : aucun serveur de messagerie n'est nomme.");
        // Un serveur de production qui rangerait tout dans un dossier n'enverrait rien, sans erreur : refuse au demarrage.
        if (Production && Remise != RemiseSmtp)
            throw new InvalidOperationException("Courriels:Production vaut vrai mais Courriels:Remise n'est pas « smtp » : aucun courriel ne partirait.");
        if (ConservationJours <= 0)
            throw new InvalidOperationException($"Courriels:ConservationJours vaut {ConservationJours} : une duree en jours, positive, est attendue.");
    }

    /// <summary>
    /// Ce qui sera fait d'un courriel : remis au serveur <see cref="DecisionRemise.Hote"/>, ou range dans la boite de depot
    /// (« envoi ignore »). Remis seulement en production, ou a un serveur jetable du poste.
    /// </summary>
    public DecisionRemise Decider()
    {
        if (Remise == RemiseSmtp && SmtpHote is { } hote)
        {
            if (Production) return new(true, hote, SmtpPort, "serveur de production");
            if (EstBoucleLocale(hote)) return new(true, hote, SmtpPort, "serveur de messagerie jetable du poste");
            return new(false, null, null, $"hors production : {hote} n'est pas un serveur du poste, le courriel n'y est pas remis");
        }
        return new(false, null, null, "hors production : boite de depot");
    }

    /// <summary>Une adresse de bouclage ecrite en clair (127.x, ::1, localhost) ; un nom n'est jamais resolu.</summary>
    public static bool EstBoucleLocale(string hote)
        => string.Equals(hote, "localhost", StringComparison.OrdinalIgnoreCase)
           || (IPAddress.TryParse(hote.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));
}

/// <summary>Ce que <see cref="OptionsCourriels.Decider"/> a decide pour un envoi.</summary>
/// <param name="Remettre">Vrai : le courriel est remis au serveur de messagerie <paramref name="Hote"/>.</param>
/// <param name="Raison">Pourquoi, en clair (ecrit dans la trace d'un envoi ignore).</param>
public sealed record DecisionRemise(bool Remettre, string? Hote, int? Port, string Raison);
