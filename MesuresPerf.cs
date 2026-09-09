using System.Diagnostics;

namespace RigV3Desktop;

/// <summary>Une ligne du widget performance, telle qu'affichee.</summary>
public sealed record MesurePerf(string Libelle, string Valeur, int Part, string Statut);

/// <summary>
/// Releve les mesures REELLES du processus en cours : rien n'est ecrit en dur.
/// Reprend les grandeurs du compteur de RigApplication
/// (Source/Outils/AmiDLL/SmartThreadPool/STPPerformanceCounter.cs) :
/// threads, file d'attente, elements traites, temps d'attente.
/// </summary>
public sealed class MesuresPerf
{
    private readonly long _completesDepart = ThreadPool.CompletedWorkItemCount;
    private TimeSpan _cpuPrecedent;
    private DateTime _instantPrecedent;

    public MesuresPerf()
    {
        using var p = Process.GetCurrentProcess();
        _cpuPrecedent = p.TotalProcessorTime;
        _instantPrecedent = DateTime.UtcNow;
    }

    /// <summary>Attente reelle avant execution d'une tache remise au pool, en ms.</summary>
    private static double AttentePool()
    {
        var chrono = Stopwatch.StartNew();
        using var fait = new ManualResetEventSlim(false);
        double attente = -1;
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            attente = chrono.Elapsed.TotalMilliseconds;
            fait.Set();
        }, null);
        fait.Wait(500);
        return attente < 0 ? chrono.Elapsed.TotalMilliseconds : attente;
    }

    private static int Part(double valeur, double plafond)
        => (int)Math.Clamp(valeur / plafond * 100.0, 0, 100);

    private static string Statut(int part)
        => part >= 85 ? "alerte" : part >= 60 ? "action" : part >= 30 ? "attente" : "ok";

    private static MesurePerf Ligne(string libelle, string valeur, double brut, double plafond)
    {
        var part = Part(brut, plafond);
        return new MesurePerf(libelle, valeur, part, Statut(part));
    }

    public MesurePerf[] Relever()
    {
        using var p = Process.GetCurrentProcess();

        var maintenant = DateTime.UtcNow;
        var cpu = p.TotalProcessorTime;
        var ecoule = (maintenant - _instantPrecedent).TotalMilliseconds;
        var charge = ecoule > 0
            ? (cpu - _cpuPrecedent).TotalMilliseconds / (ecoule * Environment.ProcessorCount) * 100.0
            : 0;
        _cpuPrecedent = cpu;
        _instantPrecedent = maintenant;

        var threads = p.Threads.Count;
        var poolEnUsage = ThreadPool.ThreadCount;
        var enFile = ThreadPool.PendingWorkItemCount;
        var traites = ThreadPool.CompletedWorkItemCount - _completesDepart;
        var attente = AttentePool();
        var memoire = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

        return
        [
            Ligne("Threads du process",   threads.ToString("N0"),                 threads,     64),
            Ligne("Threads du pool",      poolEnUsage.ToString("N0"),             poolEnUsage, 32),
            Ligne("Elements en file",     enFile.ToString("N0"),                  enFile,      16),
            Ligne("Traites (session)",    traites.ToString("N0"),                 traites,     50_000),
            Ligne("Attente du pool",      attente.ToString("0.00") + " ms",       attente,     20),
            Ligne("Memoire geree",        memoire.ToString("0.0") + " Mo",        memoire,     512)
        ];
    }
}
