using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using RigV3.Contrats.Socle;
using RigV3Desktop.Pont;

namespace RigV3.Lanceur;

// ============================================================
// LE LANCEUR DE RIG V3 SUR LES SERVEURS RDS (SOC-012, lot 12 du plan, 2026-11-21).
//
// L'icone que l'agent clique, a cote de celle de la V2 : RigV3Lanceur.exe, a la racine de
// l'installation (Installation.cs). Il :
//   1. juge le lancement (Verification.JugerAsync) et, s'il refuse, le dit a l'agent
//      (FenetreMessage) et a l'exploitation (POST /socle/version/erreurs-demarrage, sinon
//      %LOCALAPPDATA%\RigV3Lanceur\lanceur.log) ;
//   2. ramene au premier plan la V3 deja ouverte pour ce greffe dans la session (CA07) ;
//   3. ouvre versions\<la plus recente>\RigV3Desktop.exe avec les arguments recus ;
//   4. reste en veille, sans fenetre, tant que l'ecran est ouvert : toutes les 5 minutes il
//      redemande la version du serveur et avertit l'agent si une plus recente l'attend (CA03).
//
// Ses options, retirees avant de passer le reste a l'ecran :
//   --lanceur-racine <dossier>   racine de l'installation (defaut : le dossier du lanceur)
//   --lanceur-theme clair|sombre theme de la fenetre (defaut : celui des reglages de l'agent)
// Variable RIGV3_VEILLE_SECONDES : periode de la veille (defaut 300 s).
// Code de sortie : 0 ecran ouvert (ou fenetre deja ouverte ramenee), 2 lancement refuse.
// ============================================================

public static class Programme
{
    [STAThread]
    public static int Main(string[] arguments)
    {
        // Un second lancement passe la main au premier : il lui permet de prendre le premier plan.
        AllowSetForegroundWindow(-1);

        var (racine, theme, pourEcran) = LireOptions(arguments);
        var palette = PaletteLanceur.DeLAgent(theme);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        ClientServeur? serveur;
        try { serveur = ClientServeur.Actif; }
        catch (InvalidOperationException e)
        {
            new FenetreMessage(new Verdict(false, "RIG V3 est mal configuré sur ce serveur", e.Message,
                null, null, null, null, Remonter: false, Reessayer: false, Ecart: "serveur"), palette).MontrerRefus();
            Journal($"configuration : {e.Message}");
            return 2;
        }
        var greffesDuPoste = Environment.GetEnvironmentVariable(SessionAgent.VariableGreffesDuPoste);

        Verdict verdict;
        while (true)
        {
            verdict = Task.Run(() => Verification.JugerAsync(racine, Verification.VersionWebView2, serveur, pourEcran, greffesDuPoste))
                          .GetAwaiter().GetResult();
            if (verdict.Ouvrir) break;
            Journal($"refus : {verdict.Titre} -- {verdict.Message}");
            if (verdict.Remonter && Task.Run(() => Verification.RemonterAsync(serveur, verdict)).GetAwaiter().GetResult() is { } echec)
                Journal($"remontee a l'exploitation impossible : {echec}");
            if (!new FenetreMessage(verdict, palette).MontrerRefus()) return 2;
        }

        var installee = Installation.Courante(racine)!;
        Process? ecran = null;
        using var instance = InstanceUnique.Prendre(verdict.Greffe,
            () => application.Dispatcher.BeginInvoke(() => PremierPlan(ecran)));
        if (instance is { Premier: false }) return 0;

        var demarrage = new ProcessStartInfo(installee.Ecran) { UseShellExecute = false, WorkingDirectory = installee.Dossier };
        foreach (var a in pourEcran) demarrage.ArgumentList.Add(a);
        ecran = Process.Start(demarrage)!;
        ecran.EnableRaisingEvents = true;
        ecran.Exited += (_, _) => application.Dispatcher.BeginInvoke(application.Shutdown);

        if (serveur is not null) Veiller(application, serveur, installee.Version, verdict.Greffe, palette);
        application.Run();
        return 0;
    }

    /// <summary>CA03 : toutes les N secondes, la version du serveur ; plus recente que l'ecran ouvert -> un avertissement,
    /// une fois par version annoncee. Un serveur muet le temps d'un redemarrage (SOC-010 CA04) ne dit rien : on reessaie.</summary>
    private static void Veiller(Application application, ClientServeur serveur, string enCours, string? greffe, PaletteLanceur palette)
    {
        var periode = int.TryParse(Environment.GetEnvironmentVariable("RIGV3_VEILLE_SECONDES"), out var s) && s > 0 ? s : 300;
        string? dejaAnnoncee = null;
        var minuterie = new DispatcherTimer { Interval = TimeSpan.FromSeconds(periode) };
        minuterie.Tick += async (_, _) =>
        {
            try
            {
                var version = await serveur.LireAsync<VersionServeur>(RoutesVersions.Serveur);
                if (Verification.Veiller(enCours, version, greffe) is { } avertissement && avertissement.VersionServeur != dejaAnnoncee)
                {
                    dejaAnnoncee = avertissement.VersionServeur;
                    new FenetreMessage(avertissement, palette, veille: true).Show();
                }
            }
            catch (Exception e) when (e is HttpRequestException or ErreurServeur or TaskCanceledException)
            {
                Journal($"veille : serveur muet ({e.Message}), nouvel essai dans {periode} s");
            }
        };
        minuterie.Start();
    }

    private static void PremierPlan(Process? ecran)
    {
        if (ecran is null || ecran.HasExited) return;
        ecran.Refresh();
        var fenetre = ecran.MainWindowHandle;
        if (fenetre == IntPtr.Zero) return;
        if (IsIconic(fenetre)) ShowWindow(fenetre, 9 /* SW_RESTORE */);
        SetForegroundWindow(fenetre);
    }

    internal static (string Racine, string? Theme, string[] PourEcran) LireOptions(string[] arguments)
    {
        var racine = AppContext.BaseDirectory;
        string? theme = null;
        var reste = new List<string>();
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--lanceur-racine" && i + 1 < arguments.Length) racine = arguments[++i];
            else if (arguments[i] == "--lanceur-theme" && i + 1 < arguments.Length) theme = arguments[++i];
            else reste.Add(arguments[i]);
        }
        return (racine, theme, [.. reste]);
    }

    /// <summary>Le journal du lanceur, dans le profil de l'agent (aucun droit d'ecriture ailleurs, CA01).</summary>
    private static void Journal(string ligne)
    {
        try
        {
            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RigV3Lanceur");
            Directory.CreateDirectory(dossier);
            File.AppendAllText(Path.Combine(dossier, "lanceur.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ligne}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Le journal local est un secours : son echec ne doit pas empecher le message a l'agent.
        }
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr fenetre);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr fenetre, int commande);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr fenetre);
}
