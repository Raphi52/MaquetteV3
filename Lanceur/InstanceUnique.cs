namespace RigV3.Lanceur;

/// <summary>
/// SOC-012 CA07 : UNE V3 par greffe et par session Windows, comme la V2 (RigConsoleAccueil.cs:79-91 : « L'application
/// RIG est deja en cours d'execution », puis la fenetre ouverte passe au premier plan). Le lanceur qui ouvre l'ecran
/// d'un greffe tient un verrou nomme « Local\RigV3Lanceur-&lt;greffe&gt; » : « Local\ » = la session de l'agent, donc
/// deux agents du meme serveur RDS ne se genent pas (CA02). Un second lancement trouve le verrou pris, fait signe au
/// premier (evenement nomme) et s'arrete ; le premier ramene SA fenetre au premier plan.
/// Sans greffe connu au lancement (plusieurs greffes declares pour le poste, choix dans l'ecran), pas de verrou :
/// l'agent peut ouvrir deux greffes, comme la V2 qui ne verifie qu'apres le choix du greffe.
/// </summary>
public sealed class InstanceUnique : IDisposable
{
    private readonly Mutex Verrou;
    private readonly EventWaitHandle Signal;
    private readonly RegisteredWaitHandle? Attente;

    /// <summary>Vrai si ce lancement est le premier pour ce greffe dans la session.</summary>
    public bool Premier { get; }

    private InstanceUnique(string nom, Action? auSignal)
    {
        Verrou = new Mutex(initiallyOwned: true, $@"Local\{nom}", out var cree);
        Premier = cree;
        Signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{nom}-premier-plan");
        if (Premier && auSignal is not null)
            Attente = ThreadPool.RegisterWaitForSingleObject(Signal, (_, _) => auSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
        else if (!Premier)
            Signal.Set();   // le premier ramene sa fenetre
    }

    /// <summary>Le nom du verrou d'un greffe ; <paramref name="prefixe"/> isole un banc des vrais lancements.</summary>
    public static string NomPour(string greffe, string prefixe = "RigV3Lanceur")
        => $"{prefixe}-{new string([.. greffe.Where(char.IsAsciiLetterOrDigit)])}";

    /// <summary>Prend le verrou du greffe ; <paramref name="auSignal"/> est appele (sur un fil du pool) quand un
    /// second lancement du meme greffe fait signe. Null si aucun greffe n'est connu : pas de verrou.</summary>
    public static InstanceUnique? Prendre(string? greffe, Action? auSignal, string prefixe = "RigV3Lanceur")
        => string.IsNullOrWhiteSpace(greffe) ? null : new InstanceUnique(NomPour(greffe, prefixe), auSignal);

    public void Dispose()
    {
        Attente?.Unregister(null);
        if (Premier) Verrou.ReleaseMutex();
        Verrou.Dispose();
        Signal.Dispose();
    }
}
