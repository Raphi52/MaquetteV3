using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Identite;

namespace RigV3.Serveur.Socle.Supervision;

// ============================================================
// LE JOURNAL TECHNIQUE (SOC-017, lot 2 de la phase 2 du plan de reconstruction, 2026-11-21).
//
// La V2 ecrit ses erreurs dans des FICHIERS, sur le serveur ou tourne l'agent (C:\rig\data\log\<greffe>\,
// logs-application.md:25-29), purges en quelques jours ; outre-mer, il faut passer par le partage du
// serveur du territoire (\\rigapp-<territoire>\RIG\data\log\, :48-58). Seuls les plantages (eLog9Crash) vont
// en base commune (COMMUN_RIG.dbo.LOG_RIG_ERREUR), sans rien que l'agent puisse donner a l'assistance.
//
// La V3 ecrit TOUTE erreur dans soc.ErreurTechnique de la base COMMUNE (Base\migrations\SOC-017_erreurs.sql),
// pour les 40 greffes au meme endroit (CA06), par trois entrees :
//   1. une exception non interceptee d'une route : IntercepteurErreurs (et, en developpement, la page
//      d'exception d'ASP.NET Core, par FiltreErreursDeveloppement) -> niveau « grave » ; l'appelant recoit
//      un 500 qui ne porte QUE l'identifiant de suivi (SuiviErreur), jamais le message ni la pile ;
//   2. une erreur interceptee puis journalisee par le code (ILogger, Warning et au-dela, categories RigV3.*) :
//      FournisseurJournalTechnique -> « erreur » ou « avertissement » (CA03 : jamais avalee en silence) ;
//   3. une erreur de l'ecran : la route POST /socle/erreurs (ModuleSupervision) -> « grave », origine ecran.
// Toutes les lignes d'une requete partagent l'identifiant de suivi de son parcours (en-tete X-Rig-Suivi,
// envoye par l'ecran ou cree ici) : l'assistance retrouve tout le parcours par lui (CA02).
// ============================================================

/// <summary>Les reglages de la supervision (section « Supervision » de la configuration du serveur).</summary>
public sealed class ReglagesSupervision
{
    /// <summary>Le nom de ce serveur dans les etats publies et le journal (Supervision:Site ; defaut : nom de la machine).</summary>
    public string Site { get; init; } = Environment.MachineName;

    /// <summary>Les passages de la supervision et des traitements tournent seuls (Supervision:Automatique, defaut vrai).
    /// Un banc le coupe pour les jouer lui-meme, pas a pas.</summary>
    public bool Automatique { get; init; } = true;

    /// <summary>Intervalle de publication des etats et de verification des greffes (CA01).</summary>
    public TimeSpan Intervalle { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Delai regle au-dela duquel une file qui ne baisse plus leve une alerte (CA02).</summary>
    public TimeSpan DelaiFileBloquee { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Attente entre deux regards sur la file d'un traitement sans travail.</summary>
    public TimeSpan AttenteFile { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>La session d'evenements des echecs de numerotation, posee par Base\v2\bascule.sql (NUM-007 CA05).</summary>
    public string SessionEchecs { get; init; } = "rigv3_numerotation_echecs";

    /// <summary>Le dossier ou le canal simule ecrit les alertes (une ligne JSON par alerte) ; null : en memoire seulement.</summary>
    public string? DossierAlertes { get; init; }

    private static readonly Regex NomSession = new("^[A-Za-z0-9_]{1,100}$", RegexOptions.CultureInvariant);

    /// <summary>Lit la section « Supervision » ; une valeur illisible arrete le demarrage en se nommant.</summary>
    public static ReglagesSupervision Depuis(IConfiguration configuration)
    {
        var s = configuration.GetSection("Supervision");
        var defaut = new ReglagesSupervision();
        TimeSpan Secondes(string cle, TimeSpan valeur)
        {
            var brut = s[cle];
            if (string.IsNullOrWhiteSpace(brut)) return valeur;
            if (!double.TryParse(brut, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
                throw new InvalidOperationException($"Supervision:{cle} = « {brut} » : un nombre de secondes positif est attendu.");
            return TimeSpan.FromSeconds(n);
        }
        var session = string.IsNullOrWhiteSpace(s["SessionEchecs"]) ? defaut.SessionEchecs : s["SessionEchecs"]!.Trim();
        if (!NomSession.IsMatch(session))
            throw new InvalidOperationException($"Supervision:SessionEchecs = « {session} » : lettres, chiffres et _ seulement.");
        return new ReglagesSupervision
        {
            Site = string.IsNullOrWhiteSpace(s["Site"]) ? defaut.Site : s["Site"]!.Trim(),
            Automatique = !bool.TryParse(s["Automatique"], out var auto) || auto,
            Intervalle = Secondes("IntervalleSecondes", defaut.Intervalle),
            DelaiFileBloquee = Secondes("DelaiFileBloqueeSecondes", defaut.DelaiFileBloquee),
            AttenteFile = Secondes("AttenteFileSecondes", defaut.AttenteFile),
            SessionEchecs = session,
            DossierAlertes = string.IsNullOrWhiteSpace(s["DossierAlertes"]) ? null : s["DossierAlertes"],
        };
    }
}

/// <summary>Les identifiants de suivi : 8 signes sans I, L, O ni U (Crockford), en deux groupes (« 7KQ2-M9XP »),
/// pour etre lus a l'ecran et dictes au telephone sans confusion.</summary>
public static class Suivis
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly Regex Forme = new("^[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", RegexOptions.CultureInvariant);

    /// <summary>Un identifiant neuf (32^8 possibles, tire au hasard cryptographique).</summary>
    public static string Nouveau()
    {
        Span<byte> octets = stackalloc byte[8];
        RandomNumberGenerator.Fill(octets);
        var signes = new char[9];
        for (int i = 0, j = 0; i < 8; i++, j++)
        {
            if (i == 4) signes[j++] = '-';
            signes[j] = Alphabet[octets[i] % 32];   // 256 est un multiple de 32 : aucun signe n'est favorise
        }
        return new string(signes);
    }

    /// <summary>L'identifiant tel qu'il est stocke (majuscules, sans blanc) ; null s'il n'a pas la forme attendue.</summary>
    public static string? Normaliser(string? suivi)
    {
        var s = (suivi ?? string.Empty).Trim().ToUpperInvariant();
        return Forme.IsMatch(s) ? s : null;
    }
}

/// <summary>
/// CA05 : une trace technique ne contient ni mot de passe ni contenu de piece. Le message et la pile passent ici avant
/// d'etre ecrits : les secrets (« Password=… », « pwd: … », jeton d'en-tete) sont masques, un long bloc base64 (le
/// contenu d'une piece, d'une image) est retire, et la longueur est bornee. Le corps d'une requete n'est jamais ecrit.
/// </summary>
public static class Nettoyage
{
    private static readonly Regex Secret = new(
        @"(?i)\b(password|passwd|pwd|mot\s*de\s*passe|mdp|secret|token|jeton|api[_-]?key|cle\s*api)(\s*[=:]\s*)(""[^""]*""|'[^']*'|[^;\s,&]+)",
        RegexOptions.CultureInvariant);
    private static readonly Regex Autorisation = new(@"(?i)\b(bearer|basic|negotiate|ntlm)\s+[A-Za-z0-9+/=._~-]{8,}", RegexOptions.CultureInvariant);
    private static readonly Regex Bloc = new(@"[A-Za-z0-9+/]{160,}={0,2}", RegexOptions.CultureInvariant);

    /// <summary>Le texte sans secret ni contenu de piece, borne a <paramref name="max"/> caracteres ; null reste null.</summary>
    public static string? Texte(string? texte, int max)
    {
        if (texte is null) return null;
        var t = Secret.Replace(texte, m => m.Groups[1].Value + m.Groups[2].Value + "***");
        t = Autorisation.Replace(t, m => m.Groups[1].Value + " ***");
        t = Bloc.Replace(t, m => $"[contenu retiré : {m.Length} caractères]");
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }
}

/// <summary>Une ligne a ecrire au journal technique.</summary>
public sealed record ErreurAEcrire(
    string Suivi, string Niveau, string Origine, string? CodeGreffe, string? Compte, string Serveur,
    string Programme, string? Type, string Message, string? Pile, DateTime Instant)
{
    /// <summary>Une exception : son type, son message (et ceux des causes internes) et sa pile entiere.</summary>
    public static ErreurAEcrire Depuis(Exception e, string suivi, string niveau, string origine, string? greffe, string? compte,
                                       string serveur, string programme)
    {
        var messages = new StringBuilder(e.Message);
        for (var interne = e.InnerException; interne is not null; interne = interne.InnerException)
            messages.Append(" -> ").Append(interne.Message);
        return new(suivi, niveau, origine, greffe, compte, serveur, programme, e.GetType().FullName, messages.ToString(),
                   e.ToString(), DateTime.UtcNow);
    }
}

/// <summary>Le parcours de la requete en cours (identifiant de suivi, contexte HTTP), pose par <see cref="IntercepteurErreurs"/> :
/// une erreur journalisee pendant la requete, a quelque profondeur que ce soit, en prend l'identifiant.</summary>
public static class Parcours
{
    private static readonly AsyncLocal<ParcoursCourant?> Local = new();

    public static ParcoursCourant? Courant
    {
        get => Local.Value;
        internal set => Local.Value = value;
    }
}

/// <summary>L'identifiant de suivi d'une requete et la requete elle-meme (le compte et le greffe s'y lisent apres
/// l'authentification et le routage).</summary>
public sealed record ParcoursCourant(string Suivi, HttpContext Http)
{
    public string? Compte => ServiceIdentite.CompteDe(Http.User) is { } c ? (c.Length > 35 ? c[..35] : c) : null;

    public string? Greffe => ServiceIdentite.GreffeDe(Http);

    /// <summary>« GET /numerotation/{greffe}/numeros » : la route, telle que le serveur la publie.</summary>
    public string Programme
        => $"{Http.Request.Method} {(Http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? Http.Request.Path.Value}";
}

/// <summary>
/// Ecrit et lit le journal technique de la base commune. L'ecriture ne leve jamais : si la base commune ne repond pas,
/// la ligne part dans le journal du processus (console, journal Windows), avec son identifiant -- elle n'est pas perdue
/// en silence ; cette categorie n'est pas recopiee en base (<see cref="FournisseurJournalTechnique"/>), sans boucle.
/// </summary>
public sealed class JournalErreurs(BaseRig commune, ILogger<JournalErreurs> repli)
{
    /// <summary>La phrase montree a l'agent : l'identifiant, et a qui le donner ; rien du detail.</summary>
    public static string PhraseAgent(string suivi)
        => $"Erreur technique dans RIG. Donnez l'identifiant de suivi {suivi} à l'assistance d'Amitel.";

    /// <summary>Ecrit la ligne (nettoyee, CA05) ; rend son identifiant de suivi.</summary>
    public async Task<string> EcrireAsync(ErreurAEcrire e, CancellationToken ct = default)
    {
        var message = Nettoyage.Texte(e.Message, 2000) ?? string.Empty;
        var pile = Nettoyage.Texte(e.Pile, 16000);
        try
        {
            await using var cn = await commune.OuvrirAsync(ct);
            await using var cmd = new SqlCommand("""
                INSERT INTO soc.ErreurTechnique (Suivi, Instant, Niveau, Origine, CodeGreffe, Compte, Serveur, Programme, Type, Message, Pile)
                VALUES (@suivi, @instant, @niveau, @origine, @greffe, @compte, @serveur, @programme, @type, @message, @pile);
                """, cn);
            cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.Char, 9) { Value = e.Suivi });
            cmd.Parameters.Add(new SqlParameter("@instant", SqlDbType.DateTime2) { Value = e.Instant });
            cmd.Parameters.Add(new SqlParameter("@niveau", SqlDbType.VarChar, 13) { Value = e.Niveau });
            cmd.Parameters.Add(new SqlParameter("@origine", SqlDbType.VarChar, 10) { Value = e.Origine });
            cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)Borne(e.CodeGreffe, 20) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = (object?)Borne(e.Compte, 35) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@serveur", SqlDbType.VarChar, 120) { Value = Borne(e.Serveur, 120) ?? "?" });
            cmd.Parameters.Add(new SqlParameter("@programme", SqlDbType.VarChar, 200) { Value = Borne(e.Programme, 200) ?? "?" });
            cmd.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 200) { Value = (object?)Borne(e.Type, 200) ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@message", SqlDbType.NVarChar, 2000) { Value = message });
            cmd.Parameters.Add(new SqlParameter("@pile", SqlDbType.NVarChar, -1) { Value = (object?)pile ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception panne) when (panne is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            repli.LogCritical(panne,
                "Journal technique injoignable ; erreur {Suivi} gardee ici : {Niveau} {Origine} {Greffe} {Programme} : {Message}",
                e.Suivi, e.Niveau, e.Origine, e.CodeGreffe, e.Programme, message);
        }
        return e.Suivi;
    }

    /// <summary>La recherche de l'assistance (SOC-017 T04) : par identifiant, greffe, compte, programme et periode
    /// (dates en UTC, fin comprise), la plus recente en premier, <see cref="RoutesSupervision.ErreursAuPlus"/> au plus.</summary>
    public async Task<IReadOnlyList<ErreurTechnique>> RechercherAsync(string? suivi, string? greffe, string? compte, string? programme,
                                                                       DateOnly? du, DateOnly? au, CancellationToken ct)
    {
        await using var cn = await commune.OuvrirAsync(ct);
        await using var cmd = new SqlCommand($"""
            SELECT TOP ({RoutesSupervision.ErreursAuPlus}) Id, Suivi, Instant, Niveau, Origine, CodeGreffe, Compte, Serveur, Programme, Type, Message, Pile
            FROM soc.ErreurTechnique
            WHERE (@suivi IS NULL OR Suivi = @suivi)
              AND (@greffe IS NULL OR CodeGreffe = @greffe)
              AND (@compte IS NULL OR Compte = @compte)
              AND (@programme IS NULL OR Programme LIKE '%' + @programme + '%')
              AND (@du IS NULL OR Instant >= @du)
              AND (@au IS NULL OR Instant < DATEADD(day, 1, @au))
            ORDER BY Instant DESC, Id DESC;
            """, cn);
        cmd.Parameters.Add(new SqlParameter("@suivi", SqlDbType.Char, 9) { Value = (object?)Suivis.Normaliser(suivi) ?? (string.IsNullOrWhiteSpace(suivi) ? DBNull.Value : "?????????") });
        cmd.Parameters.Add(new SqlParameter("@greffe", SqlDbType.VarChar, 20) { Value = (object?)Borne(Vide(greffe), 20) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@compte", SqlDbType.VarChar, 35) { Value = (object?)Borne(Vide(compte), 35) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@programme", SqlDbType.VarChar, 200) { Value = (object?)Borne(Vide(programme), 200) ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@du", SqlDbType.DateTime2) { Value = du is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@au", SqlDbType.DateTime2) { Value = au is { } a ? a.ToDateTime(TimeOnly.MinValue) : DBNull.Value });
        var lignes = new List<ErreurTechnique>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            lignes.Add(new ErreurTechnique(r.GetInt64(0), r.GetString(1), DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc),
                r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.GetString(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11)));
        return lignes;
    }

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Borne(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>
/// Les erreurs journalisees par le code (ILogger) vont aussi au journal technique (CA03) : categories du serveur
/// (RigV3.*), avertissement et au-dela. L'ecriture passe par une file en memoire, videe par <see cref="EcrivainJournal"/> :
/// un appel de journal ne bloque jamais la requete sur la base. Les categories d'ASP.NET Core n'y vont pas (la page
/// d'exception de developpement redirait chaque erreur deja ecrite par l'intercepteur), ni celle de
/// <see cref="JournalErreurs"/> (son repli quand la base ne repond pas).
/// </summary>
public sealed class FournisseurJournalTechnique(FileJournal file, ReglagesSupervision reglages) : ILoggerProvider
{
    private static readonly string CategorieRepli = typeof(JournalErreurs).FullName!;

    public ILogger CreateLogger(string categorie)
        => categorie.StartsWith("RigV3.", StringComparison.Ordinal) && categorie != CategorieRepli
            ? new Ecrivain(categorie, file, reglages)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose() { }

    private sealed class Ecrivain(string categorie, FileJournal file, ReglagesSupervision reglages) : ILogger
    {
        private readonly string Court = categorie[(categorie.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel niveau) => niveau >= LogLevel.Warning && niveau != LogLevel.None;

        public void Log<TState>(LogLevel niveau, EventId evenement, TState state, Exception? exception, Func<TState, Exception?, string> formater)
        {
            if (!IsEnabled(niveau)) return;
            var parcours = Parcours.Courant;
            var origine = parcours is null ? OriginesErreur.Traitement : OriginesErreur.Serveur;
            file.Deposer(new ErreurAEcrire(
                parcours?.Suivi ?? Suivis.Nouveau(),
                niveau switch { LogLevel.Critical => NiveauxErreur.Grave, LogLevel.Error => NiveauxErreur.Erreur, _ => NiveauxErreur.Avertissement },
                origine, parcours?.Greffe, parcours?.Compte, reglages.Site,
                parcours is null ? Court : $"{parcours.Programme} ({Court})",
                exception?.GetType().FullName, formater(state, exception), exception?.ToString(), DateTime.UtcNow));
        }
    }
}

/// <summary>La file en memoire entre <see cref="FournisseurJournalTechnique"/> et <see cref="EcrivainJournal"/>.
/// Bornee : au-dela de 10 000 lignes en attente (base commune muette longtemps), la plus ancienne cede la place, et
/// le compte des lignes perdues est ecrit des que la base repond.</summary>
public sealed class FileJournal
{
    private readonly Channel<ErreurAEcrire> canal;
    private long perdues;

    public FileJournal()
        => canal = Channel.CreateBounded<ErreurAEcrire>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref perdues));

    internal void Deposer(ErreurAEcrire e) => canal.Writer.TryWrite(e);

    internal ChannelReader<ErreurAEcrire> Lecteur => canal.Reader;

    internal long PrendrePerdues() => Interlocked.Exchange(ref perdues, 0);
}

/// <summary>Vide <see cref="FileJournal"/> dans la base commune, ligne a ligne.</summary>
public sealed class EcrivainJournal(FileJournal file, JournalErreurs journal, ReglagesSupervision reglages) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken arret)
    {
        try
        {
            await foreach (var e in file.Lecteur.ReadAllAsync(arret))
            {
                await journal.EcrireAsync(e, CancellationToken.None);
                if (file.PrendrePerdues() is > 0 and var n)
                    await journal.EcrireAsync(new ErreurAEcrire(Suivis.Nouveau(), NiveauxErreur.Erreur, OriginesErreur.Serveur, null, null,
                        reglages.Site, nameof(EcrivainJournal), null, $"{n} ligne(s) du journal technique perdues : file en memoire pleine.",
                        null, DateTime.UtcNow), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (arret.IsCancellationRequested)
        {
            // Arret du serveur : ce qui reste en file s'ecrit encore, sans attendre de nouvelle ligne.
            while (file.Lecteur.TryRead(out var reste)) await journal.EcrireAsync(reste, CancellationToken.None);
        }
    }
}

/// <summary>
/// Le premier maillon de chaque requete : il donne son identifiant de suivi au parcours (repris de l'en-tete
/// <see cref="RoutesSupervision.EnTeteSuivi"/> s'il a la bonne forme, sinon neuf), le rend sur la reponse, et ecrit
/// au journal toute exception non interceptee (CA01) avant de repondre 500 avec ce seul identifiant.
/// </summary>
public sealed class IntercepteurErreurs(RequestDelegate suite, JournalErreurs journal, ReglagesSupervision reglages)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var suivi = Suivis.Normaliser(http.Request.Headers[RoutesSupervision.EnTeteSuivi].ToString()) ?? Suivis.Nouveau();
        http.Response.Headers[RoutesSupervision.EnTeteSuivi] = suivi;
        Parcours.Courant = new ParcoursCourant(suivi, http);
        try
        {
            await suite(http);
        }
        catch (Exception e) when (e is not OperationCanceledException || !http.RequestAborted.IsCancellationRequested)
        {
            if (!await RepondreAsync(http, e, journal, reglages)) throw;
        }
        finally
        {
            Parcours.Courant = null;
        }
    }

    /// <summary>Ecrit l'erreur au journal puis la reponse de l'agent ; faux si la reponse est deja partie (elle ne peut
    /// plus changer : l'exception continue, deja journalisee).</summary>
    internal static async Task<bool> RepondreAsync(HttpContext http, Exception e, JournalErreurs journal, ReglagesSupervision reglages)
    {
        var parcours = Parcours.Courant ?? new ParcoursCourant(Suivis.Nouveau(), http);
        // Une demande mal formee (corps JSON illisible...) est la faute de l'appelant : 400, trace detaillee seulement.
        var demande = e as BadHttpRequestException;
        await journal.EcrireAsync(ErreurAEcrire.Depuis(e, parcours.Suivi, demande is null ? NiveauxErreur.Grave : NiveauxErreur.Avertissement,
            OriginesErreur.Serveur, parcours.Greffe, parcours.Compte, reglages.Site, parcours.Programme));
        if (http.Response.HasStarted) return false;
        http.Response.Clear();
        http.Response.Headers[RoutesSupervision.EnTeteSuivi] = parcours.Suivi;
        http.Response.StatusCode = demande?.StatusCode ?? StatusCodes.Status500InternalServerError;
        await http.Response.WriteAsJsonAsync(new SuiviErreur(parcours.Suivi, demande is null
            ? JournalErreurs.PhraseAgent(parcours.Suivi)
            : $"Demande illisible. Identifiant de suivi : {parcours.Suivi}."));
        return true;
    }
}

/// <summary>Place <see cref="IntercepteurErreurs"/> en tete du serveur, sans toucher ServeurRig.cs (recette 2 de l'architecture).</summary>
public sealed class FiltreErreurs : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> suite)
        => app =>
        {
            app.UseMiddleware<IntercepteurErreurs>();
            suite(app);
        };
}

/// <summary>
/// En DEVELOPPEMENT, ASP.NET Core pose lui-meme sa page d'exception au debut du pipeline de l'application, donc APRES
/// <see cref="IntercepteurErreurs"/> : elle attraperait l'exception la premiere et l'afficherait en entier. Ce filtre de
/// la page (IDeveloperPageExceptionFilter, https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.diagnostics.ideveloperpageexceptionfilter)
/// fait la meme chose que l'intercepteur : le developpement se comporte comme la production.
/// </summary>
public sealed class FiltreErreursDeveloppement(JournalErreurs journal, ReglagesSupervision reglages) : IDeveloperPageExceptionFilter
{
    public async Task HandleExceptionAsync(ErrorContext contexte, Func<ErrorContext, Task> suite)
    {
        if (!await IntercepteurErreurs.RepondreAsync(contexte.HttpContext, contexte.Exception, journal, reglages))
            await suite(contexte);
    }
}
