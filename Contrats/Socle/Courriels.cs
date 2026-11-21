namespace RigV3.Contrats.Socle;

/// <summary>
/// Les adresses des courriels du greffe (SOC-006) : la liste des envois et leur trace, le detail d'un envoi, les
/// modeles qu'un agent peut choisir. Le serveur les publie (Serveur/Socle/Courriels/ModuleCourriels.cs), l'ecran
/// « Courriels envoyés » les appelle (Pont/EcranCourriels.cs). L'ENVOI n'est pas une route : un acte de la V3 qui
/// envoie un courriel appelle le service du serveur (ServiceCourriels.EnvoyerAsync), comme la V2 lancait RIG_Send_Email.
///
/// Chaque adresse porte le code du greffe (<c>{greffe}</c>) : l'agent est juge dans la base V3 de CE greffe (401 sans
/// session, 403 compte refuse, 404 greffe absent du catalogue, 503 base muette). L'adresse a appeler : <see cref="Pour"/>.
/// </summary>
public static class RoutesCourriels
{
    /// <summary>GET : les envois du greffe, un par numero de suivi, les plus recents d'abord (<see cref="CourrielSuivi"/>[]).
    /// Filtres facultatifs : <c>?du=2026-11-21&amp;au=2026-11-21&amp;destinataire=…&amp;modele=…&amp;etat=echec&amp;max=500</c>.
    /// Reserve a la fonction <see cref="FonctionCourrielsEnvoyes"/>.</summary>
    public const string Envois = "/socle/greffes/{greffe}/courriels";

    /// <summary>GET : un envoi, son texte et chacune de ses etapes (<see cref="DetailCourriel"/>) ; 404 s'il est inconnu.
    /// Reserve a la fonction <see cref="FonctionCourrielsEnvoyes"/>.</summary>
    public const string Envoi = "/socle/greffes/{greffe}/courriels/{suivi:guid}";

    /// <summary>GET : les modeles de courriel que l'agent peut choisir (<see cref="ModeleMessage"/>[]). Un modele
    /// reserve a Amitel n'y est pas pour un agent du greffe (SOC-006 CA05).</summary>
    public const string Modeles = "/socle/greffes/{greffe}/modeles-courriel";

    /// <summary>
    /// La fonction (droit) qu'exigent <see cref="Envois"/> et <see cref="Envoi"/> : MAIL_LOT1, la procedure de la V2
    /// « Mails envoyés en Lot 1 » (PROCESSUS_ET_FONCTIONALITE de RIG_DEV, DLL PROC_MAIL_LOT1, lu le 2026-11-21), l'ecran
    /// de consultation des courriels partis. Sans elle : 403.
    /// </summary>
    public const string FonctionCourrielsEnvoyes = "MAIL_LOT1";

    /// <summary>Le nombre d'envois rendus au plus, comme le TOP 500 de la V2 (OPE_LISTE_MAILS_LOT1.cs:16), jamais illimite.</summary>
    public const int MaximumParDefaut = 500;

    /// <summary>L'adresse d'une de ces routes pour le greffe <paramref name="greffe"/> (et l'envoi <paramref name="suivi"/>).</summary>
    public static string Pour(string route, string greffe, Guid? suivi = null)
        => route.Replace("{greffe}", Uri.EscapeDataString(greffe))
                .Replace("{suivi:guid}", suivi?.ToString("D") ?? "");

    /// <summary>L'adresse de <see cref="Envois"/> avec ses filtres (vides omis).</summary>
    public static string Filtrer(string greffe, DateOnly? du = null, DateOnly? au = null, string? destinataire = null,
                                 string? modele = null, string? etat = null, int? max = null)
    {
        var filtres = new List<string>();
        if (du is { } d) filtres.Add("du=" + d.ToString("yyyy-MM-dd"));
        if (au is { } a) filtres.Add("au=" + a.ToString("yyyy-MM-dd"));
        if (!string.IsNullOrWhiteSpace(destinataire)) filtres.Add("destinataire=" + Uri.EscapeDataString(destinataire.Trim()));
        if (!string.IsNullOrWhiteSpace(modele)) filtres.Add("modele=" + Uri.EscapeDataString(modele.Trim()));
        if (!string.IsNullOrWhiteSpace(etat)) filtres.Add("etat=" + Uri.EscapeDataString(etat.Trim()));
        if (max is { } m) filtres.Add("max=" + m);
        var adresse = Pour(Envois, greffe);
        return filtres.Count == 0 ? adresse : adresse + "?" + string.Join("&", filtres);
    }
}

/// <summary>
/// Ce qu'est devenu un envoi, deduit de ses etapes (<see cref="EtapesCourriel"/>).
/// </summary>
public static class EtatsCourriel
{
    /// <summary>Le serveur de messagerie l'a accepte (EMAIL_SEND_SUCCES sans SMTP_IGNORE_DEV).</summary>
    public const string Envoye = "envoye";

    /// <summary>Hors production : il n'a PAS ete remis au serveur de messagerie, il est dans la boite de depot (SMTP_IGNORE_DEV, CA06).</summary>
    public const string Ignore = "ignore";

    /// <summary>Le serveur de messagerie l'a refuse, ou il n'a pas pu etre prepare (EMAIL_SEND_ERREUR, PREPARATION_ERREUR).</summary>
    public const string Echec = "echec";

    /// <summary>Commence sans issue ecrite (l'envoi a ete interrompu).</summary>
    public const string EnCours = "en-cours";

    public static readonly string[] Tous = [Envoye, Ignore, Echec, EnCours];
}

/// <summary>
/// Les etapes ecrites dans la trace, celles de la V2 (envoi-mail-rig-send-email.md:92-97, EmailEnvoye.cs:130-175,
/// 391-408) plus une de la V3 : PREPARATION_ERREUR (modele inconnu, adresse introuvable : rien n'est remis).
/// </summary>
public static class EtapesCourriel
{
    public const string PreparationEmail = "PREPARATION_EMAIL";
    public const string PreparationErreur = "PREPARATION_ERREUR";
    public const string EnvoiDebut = "EMAIL_SEND_DEBUT";
    public const string AppelSmtp = "SMTP_APPEL";
    public const string IgnoreHorsProduction = "SMTP_IGNORE_DEV";
    public const string EnvoiSucces = "EMAIL_SEND_SUCCES";
    public const string EnvoiErreur = "EMAIL_SEND_ERREUR";
    public const string CourrielEnregistre = "EMAIL_ENVOYE";
}

/// <summary>
/// Un envoi, tel que la liste « Courriels envoyés » le montre : une ligne par numero de suivi (toutes ses etapes reunies).
/// </summary>
/// <param name="Suivi">Le numero de suivi de l'envoi (EMTRC_CORRELATION_ID de la V2).</param>
/// <param name="Debut">L'heure de sa premiere etape (heure du serveur de la base).</param>
/// <param name="Etat">Un de <see cref="EtatsCourriel"/>.</param>
/// <param name="TypeModele">Le modele de courriel (EMAILMOD_TYPE).</param>
/// <param name="NbPieces">Le nombre de documents (liens) joints.</param>
/// <param name="IdCourriel">Le courriel enregistre (soc.CourrielEnvoye) une fois parti ; null sinon.</param>
/// <param name="Erreur">Le message de l'erreur d'un echec ; null sinon.</param>
/// <param name="Agent">Le compte de l'agent qui l'a demande, s'il est connu.</param>
/// <param name="FichierDepot">Hors production : le fichier de la boite de depot ou il a ete range.</param>
public sealed record CourrielSuivi(
    Guid Suivi,
    DateTime Debut,
    string Etat,
    string? TypeModele,
    string? Expediteur,
    string? Destinataires,
    string? Copie,
    string? Sujet,
    int? NbPieces,
    int? IdCourriel,
    string? Erreur,
    string? Agent,
    string? FichierDepot);

/// <summary>Une etape de la trace d'un envoi (soc.TraceEnvoi).</summary>
public sealed record EtapeCourriel(
    DateTime Instant,
    string Etape,
    string? Mode,
    bool? Succes,
    string? SmtpHote,
    int? SmtpPort,
    string? ErreurType,
    string? ErreurMessage,
    string? FichierDepot);

/// <summary>Un envoi en detail : sa ligne, le texte du courriel parti (null s'il n'est pas parti) et ses etapes dans l'ordre.</summary>
public sealed record DetailCourriel(
    CourrielSuivi Envoi,
    string? Corps,
    bool? CorpsHtml,
    EtapeCourriel[] Etapes);

/// <summary>
/// Un modele de courriel du greffe (soc.ModeleMessage, EMAIL_MODEL de la V2), sans son texte : ce que montre une liste de choix.
/// </summary>
public sealed record ModeleMessage(
    string Type,
    string? LibelleEcran,
    string Sujet,
    bool CorpsHtml,
    string? Expediteur,
    string? Reponse,
    string? Copie,
    string? DestinataireDefaut,
    bool ReserveAmitel,
    bool EnvoiAutomatique,
    bool? UtilisableEnSaisie,
    bool Lot1,
    bool Lot2);
