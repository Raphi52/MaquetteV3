using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

/// <summary>Ce que l'ecran sait du logiciel d'une affaire de ses resultats (SOC-015 CA01).</summary>
/// <param name="Logiciel">« V2 » (affaire creee avant le passage du greffe : traitee dans l'ancien RIG) ou « V3 ».</param>
public sealed record LigneAiguillee(string Numero, string Logiciel, string Motif);

/// <summary>La reponse de l'aiguillage pour les lignes d'une recherche.</summary>
/// <param name="Mode">« serveur », « demo » (sans RIGV3_SERVEUR), « sans-greffe », « refuse » (403) ou « erreur ».
/// Hors « serveur » et « demo », aucune ligne n'est aiguillee : l'ecran se comporte comme avant ce lot.</param>
/// <param name="Passage">La date de passage du greffe a la V3 (« 01/08/2026 08:00 », heure locale) ; null si non fixee.</param>
/// <param name="Message">La raison, quand l'aiguillage n'a pas pu repondre ; null sinon.</param>
public sealed record VueAiguillage(string Mode, string? Greffe, string? Passage, string? Message, LigneAiguillee[] Affaires);

/// <summary>Le compte rendu du geste « Ouvrir dans l'ancien RIG ».</summary>
/// <param name="Geste">« lance » (la V2 demarre), « ramene » (la V2 du greffe etait ouverte : remise au premier plan)
/// ou « refuse » (rien n'a ete fait, <paramref name="Message"/> dit pourquoi).</param>
/// <param name="Message">La phrase montree a l'agent : le numero copie, a rechercher dans l'ancien RIG (SOC-015 CA03).</param>
/// <param name="LigneDeCommande">Le lancement de la V2 tel qu'il a ete fait (« "C:\RIG\exe\RigClientAccueil.exe" /g8002 ») ; null sans lancement.</param>
public sealed record ResultatAncienRig(bool Fait, string Geste, string Numero, string Message, string? LigneDeCommande);

/// <summary>
/// Les gestes Windows du poste RDS dont le pont a besoin pour l'ancien RIG. L'implementation reelle est
/// <see cref="PosteWindows"/> ; le banc tests/SocleAiguillageCheck en substitue une partie pour ne pas voler
/// le premier plan ni le presse-papiers de la personne qui le lance.
/// </summary>
public interface IPosteWindows
{
    /// <summary>La fenetre principale de la V2 de ce greffe dans la session Windows courante, ou null.</summary>
    IntPtr? FenetreV2(string nomProcessus, string? greffe);
    bool ExisteExe(string chemin);
    Process? Lancer(string exe, string arguments);
    void RamenerAuPremierPlan(IntPtr fenetre);
    /// <summary>Envoie <paramref name="message"/> a la fenetre comme la V2 parle a sa console (WM_COPYDATA) ; false si refuse ou sans reponse.</summary>
    bool Envoyer(IntPtr fenetre, string message);
    /// <summary>Copie <paramref name="texte"/> dans le presse-papiers ; false si Windows l'a refuse.</summary>
    bool Copier(string texte);
}

/// <summary>
/// L'ANCIEN RIG vu de la V3 (SOC-014 T04, SOC-015 T01-T03 ; lot 3 de la phase 2).
///
/// 1. QUEL LOGICIEL pour chaque affaire d'une recherche (<see cref="AiguillerAsync"/>) : la regle de SOC-014
///    (<see cref="RegleAiguillage"/>) avec la date de passage du greffe, demandee au serveur
///    (<see cref="RoutesAiguillage.Aiguiller"/>) ; sans RIGV3_SERVEUR, la demonstration fixe un passage au
///    <see cref="PassageDemo"/> et joue LA MEME regle.
///
/// 2. OUVRIR UNE ANCIENNE AFFAIRE DANS LA V2 (<see cref="OuvrirAsync"/>), comme la V2 le fait elle-meme :
///    - la V2 du greffe est deja ouverte dans la session (processus RigClientAccueil dont la fenetre s'intitule
///      « Console d'accueil de RIG (&lt;code&gt; - ... », la regle d'instance unique de la V2,
///      RigConsoleAccueil.cs:85 et le titre de FormRigClientAccueil.cs:561 / RigGreffe.cs:594) : elle est
///      ramenee au premier plan (CA02) ;
///    - sinon elle est lancee comme son raccourci Windows : « RigClientAccueil.exe /g&lt;code&gt; »
///      (RigFormLogin.cs:311, lu par ListeParametre.cs:243-246) dans &lt;RigPath&gt;exe\, RigPath etant la cle
///      HKLM\SOFTWARE\Wow6432Node\RIG\RigPath de la V2 (Common.cs:32, C:\RIG\ par defaut). Le code du greffe
///      saute la fenetre de choix du greffe ; AUCUN « utilisateur= » : la V2 reconnait l'agent a sa session
///      Windows (RigConsoleAccueil.cs:76-78, Utilisateur.GetUtilisateurCourant ; CA05) ;
///    - puis, des que sa fenetre est prete, la V3 lui demande sa recherche d'affaires par SON canal de
///      messages, celui de ses propres raccourcis « Envoyer vers » : WM_COPYDATA
///      « EXECUTE_PROCESSUS|RECHAFF|NoAffaire=&lt;numero&gt; » (AmiCommunication.cs:36-47 et :92-95, lu par
///      FormRigClientAccueil.cs:96-100 et :427-433). La V2 y applique ses propres droits (« Vous n'etes pas
///      autorise a executer le processus », FormRigClientAccueil.cs:1844) ;
///    - le numero est copie dans le presse-papiers et affiche (CA03) : la V2 livree ouvre sa recherche VIDE --
///      le parametre NoAffaire, qui la pre-remplit, n'existe que dans une modification NON livree de
///      D:\RigApplication (PROC_RECHAFF.cs, non commitee) ; la V2 livree l'ignore sans erreur. Ouvrir la V2
///      directement sur l'affaire est un chantier V2 a decider par le referent Socle (note de SOC-015).
///
/// Une affaire que l'aiguillage donne a la V3 n'ouvre pas la V2 : refus, avec sa raison.
/// </summary>
public static class AncienRig
{
    /// <summary>Variable d'environnement : le chemin complet de l'exe de la V2, si elle n'est pas ou la V2 la range.</summary>
    public const string VariableExe = "RIGV3_V2_EXE";

    /// <summary>Le titre de la console de la V2 (RigConsoleAccueil.TITLE_FORM_PRINCIPAL, RigConsoleAccueil.cs:43).</summary>
    public const string TitreConsoleV2 = "Console d'accueil de RIG";

    /// <summary>Le processus de la V2 qui recherche une affaire (PROCESSUS_ET_FONCTIONALITE, lu sur RIG_DEV le
    /// 21/11/2026 : RECHAFF, DLL PROC_RECHAFF.dll, « Recherche Affaire », execution unique).</summary>
    public const string ProcessusRechercheV2 = "RECHAFF";

    /// <summary>La demonstration (sans serveur) : le greffe est passe a la V3 le 1er aout 2026 a 8 h, heure locale.
    /// Les affaires du jeu creees avant (fin juillet et plus tot) se traitent donc dans l'ancien RIG.</summary>
    public static readonly DateTime PassageDemo = new(2026, 8, 1, 8, 0, 0);

    /// <summary>Combien de temps la V3 attend que la V2 lancee ouvre sa fenetre avant de lui parler (son
    /// demarrage charge le greffe et ses habilitations).</summary>
    public static readonly TimeSpan DelaiDemarrageV2 = TimeSpan.FromMinutes(2);

    /// <summary>Le poste reel ; remplacable par un banc.</summary>
    public static IPosteWindows Poste { get; set; } = new PosteWindows();

    [JSInvokable("EcranRechAffaires.Aiguiller")]
    public static Task<VueAiguillage> Aiguiller(string[] numeros)
        => AiguillerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, numeros, EcranRechAffaires.CreationDe);

    [JSInvokable("EcranRechAffaires.OuvrirDansAncienRig")]
    public static Task<ResultatAncienRig> Ouvrir(string numero)
        => OuvrirAsync(numero, ClientServeur.Actif, SessionAgent.CodeGreffe, EcranRechAffaires.CreationDe,
                       Poste, CheminExe(), DelaiDemarrageV2, attendreLaV2: false);

    /// <summary>
    /// Le logiciel de chaque affaire de <paramref name="numeros"/>, d'apres sa date de creation
    /// (<paramref name="creationDe"/>) et la date de passage du greffe. Un numero dont la date est inconnue n'est
    /// pas aiguille : le contrat prendrait une date absente pour une NOUVELLE affaire.
    /// </summary>
    public static async Task<VueAiguillage> AiguillerAsync(ClientServeur? serveur, string? greffe, IEnumerable<string> numeros,
                                                           Func<string, DateTime?> creationDe)
    {
        var affaires = numeros.Distinct()
            .Select(n => (Numero: n, CreeeLe: creationDe(n)))
            .Where(a => a.CreeeLe is not null)
            .Select(a => new AffaireAAiguiller(a.Numero, a.CreeeLe))
            .ToList();
        if (serveur is null)
        {
            var lignes = affaires.Select(a => RegleAiguillage.Aiguiller(a.Numero, PassageDemo, a.CreeeLe, DateTime.Now)).ToArray();
            return new("demo", null, Date(PassageDemo), null, lignes.Select(Ligne).ToArray());
        }
        if (string.IsNullOrWhiteSpace(greffe))
            return Vide("sans-greffe", null,
                "Aucun greffe n'est ouvert dans cette session : l'ancien RIG ne peut pas etre distingue de la V3.");
        if (affaires.Count == 0) return new("serveur", greffe, null, null, []);
        try
        {
            var reponse = await serveur.EnvoyerAsync<QuestionAiguillage, ReponseAiguillage>(
                RoutesAiguillage.Pour(RoutesAiguillage.Aiguiller, greffe), new QuestionAiguillage(affaires));
            return new("serveur", greffe, reponse.PassageLocal is { } p ? Date(p) : null, null,
                       reponse.Affaires.Select(Ligne).ToArray());
        }
        catch (ErreurServeur e) when (e.CodeHttp == 403)
        {
            return Vide("refuse", greffe, $"Vous n'avez pas acces au catalogue du greffe {greffe}.");
        }
        catch (Exception e) when (e is ErreurServeur or HttpRequestException or TaskCanceledException)
        {
            var code = (e as ErreurServeur)?.CodeHttp.ToString(CultureInfo.InvariantCulture) ?? e.GetType().Name;
            return Vide("erreur", greffe, $"L'aiguillage du greffe {greffe} n'a pas repondu ({code}) : l'ancien RIG n'est pas signale.");
        }
    }

    /// <summary>
    /// Ouvre l'ancienne affaire <paramref name="numero"/> dans la V2 du greffe (SOC-015 CA02, CA03, CA05) : la ramene
    /// si elle est ouverte, sinon la lance ; copie le numero ; demande a la V2 sa recherche d'affaires.
    /// </summary>
    /// <param name="attendreLaV2">true (banc) : attendre que la V2 lancee ait recu le message avant de rendre ;
    /// false (ecran) : rendre aussitot, l'attente continue sans bloquer l'agent.</param>
    public static async Task<ResultatAncienRig> OuvrirAsync(string numero, ClientServeur? serveur, string? greffe,
                                                            Func<string, DateTime?> creationDe, IPosteWindows poste,
                                                            string cheminExe, TimeSpan delai, bool attendreLaV2)
    {
        numero = (numero ?? "").Trim();
        // Le message de la V2 se decoupe sur « | », puis ses parametres sur « ; » et « = » (ListeParametre.cs:216-224) :
        // un numero qui en porte un ne lui arriverait pas entier.
        if (numero.Length == 0 || numero.IndexOfAny(['|', ';', '=', '\r', '\n']) >= 0)
            return Refus(numero, $"Numero d'affaire illisible pour l'ancien RIG : « {numero} ».");

        var vue = await AiguillerAsync(serveur, greffe, [numero], creationDe);
        if (vue.Affaires.FirstOrDefault() is not { } ligne)
            return Refus(numero, vue.Message ?? $"L'affaire {numero} est inconnue : son logiciel ne peut pas etre determine.");
        if (ligne.Logiciel != Logiciels.V2)
            return Refus(numero, $"L'affaire {numero} se traite dans la V3 ({ligne.Motif}) : l'ancien RIG ne l'ouvre pas.");

        var message = MessageRechercheV2(numero);
        var processus = Path.GetFileNameWithoutExtension(cheminExe);
        // CA03 : le numero est deja copie quand la V2 s'affiche.
        var aRechercher = poste.Copier(numero)
            ? $"Le numero {numero} est copie : collez-le dans « N° affaire » de la recherche d'affaires de l'ancien RIG."
            : $"Recherchez le numero {numero} dans « N° affaire » de la recherche d'affaires de l'ancien RIG (le presse-papiers a refuse la copie).";

        if (poste.FenetreV2(processus, greffe) is { } ouverte)
        {
            poste.RamenerAuPremierPlan(ouverte);
            // La V2 traite le message DANS sa boucle de fenetre (FormRigClientAccueil.cs:96-100) : elle ouvre sa
            // recherche, ou affiche un refus modal. L'ecran n'attend pas qu'elle ait fini.
            var envoi = Task.Run(() => poste.Envoyer(ouverte, message));
            var recue = !attendreLaV2 || await envoi;
            return new(true, "ramene", numero,
                $"L'ancien RIG{DuGreffe(greffe)} etait deja ouvert : il est ramene au premier plan"
                + (recue ? " et ouvre sa recherche d'affaires. " : " (il n'a pas repondu a la demande de recherche). ") + aRechercher,
                null);
        }
        if (!poste.ExisteExe(cheminExe))
            return Refus(numero, $"L'ancien RIG n'est pas installe sur ce poste ({cheminExe} introuvable).");

        var arguments = ArgumentsLancement(greffe);
        var lance = poste.Lancer(cheminExe, arguments);
        if (lance is null)
            return Refus(numero, $"L'ancien RIG n'a pas pu etre lance ({cheminExe}).");
        var suite = EnvoyerQuandPreteAsync(poste, lance, processus, greffe, message, delai);
        if (attendreLaV2) await suite;
        else _ = suite.ContinueWith(t => Debug.WriteLine($"AncienRig : {t.Exception?.GetBaseException().Message}"),
                                    TaskContinuationOptions.OnlyOnFaulted);
        return new(true, "lance", numero,
            $"L'ancien RIG{DuGreffe(greffe)} demarre ; sa recherche d'affaires s'ouvrira des qu'il sera pret. "
            + (greffe is null ? "Aucun greffe n'est ouvert dans la V3 : il demandera lequel ouvrir. " : "")
            + aRechercher,
            LigneDeCommande(cheminExe, arguments));
    }

    /// <summary>Les arguments du lancement, ceux du raccourci Windows de la V2 : « /g&lt;code&gt; », rien d'autre ;
    /// vide sans greffe (la V2 montre alors sa fenetre de choix du greffe).</summary>
    public static string ArgumentsLancement(string? greffe) => string.IsNullOrWhiteSpace(greffe) ? "" : "/g" + greffe.Trim();

    /// <summary>Le message qui demande a la console de la V2 sa recherche d'affaires (format de
    /// AmiCommunication.RigAccueil.ExecuteProcessus : « EXECUTE_PROCESSUS|&lt;code&gt;|&lt;parametres&gt; »).
    /// Le troisieme champ est toujours present : la V2 le lit sans verifier (FormRigClientAccueil.cs:432).</summary>
    public static string MessageRechercheV2(string numero) => $"EXECUTE_PROCESSUS|{ProcessusRechercheV2}|NoAffaire={numero}";

    /// <summary>Le debut du titre de la console de la V2 d'un greffe (« Console d'accueil de RIG (8002 - ») ; sans
    /// greffe, celui de toute console de la V2.</summary>
    public static string DebutTitreV2(string? greffe)
        => string.IsNullOrWhiteSpace(greffe) ? TitreConsoleV2 + " (" : $"{TitreConsoleV2} ({greffe.Trim()} - ";

    public static string LigneDeCommande(string exe, string arguments) => $"\"{exe}\"" + (arguments.Length > 0 ? " " + arguments : "");

    /// <summary>L'exe de la V2 : <see cref="VariableExe"/> s'il est pose, sinon &lt;RigPath&gt;exe\RigClientAccueil.exe comme
    /// la V2 se trouve elle-meme (AmiSystem.getExe, AmiSystem.cs:836-848).</summary>
    public static string CheminExe()
    {
        if (Environment.GetEnvironmentVariable(VariableExe) is { Length: > 0 } force) return force;
        string? rigPath = null;
        if (OperatingSystem.IsWindows())
        {
            // HKLM\SOFTWARE\Wow6432Node\RIG vue d'un processus 64 bits = SOFTWARE\RIG de la vue 32 bits, celle de la V2.
            using var cle = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\RIG");
            rigPath = cle?.GetValue("RigPath") as string;
        }
        return Path.Combine(string.IsNullOrWhiteSpace(rigPath) ? @"C:\RIG\" : rigPath, "exe", "RigClientAccueil.exe");
    }

    /// <summary>Attend que la V2 lancee ait sa fenetre, puis qu'elle ait fini de demarrer (file de messages au repos,
    /// WaitForInputIdle), et lui envoie <paramref name="message"/>. false si elle s'arrete ou n'apparait pas a temps :
    /// le numero reste copie, l'agent le recherche lui-meme.</summary>
    private static async Task<bool> EnvoyerQuandPreteAsync(IPosteWindows poste, Process lance, string processus, string? greffe,
                                                           string message, TimeSpan delai)
    {
        var limite = DateTime.UtcNow + delai;
        while (DateTime.UtcNow < limite)
        {
            if (lance.HasExited) return false;
            if (poste.FenetreV2(processus, greffe) is { } fenetre)
            {
                var reste = (int)Math.Max(0, (limite - DateTime.UtcNow).TotalMilliseconds);
                try { lance.WaitForInputIdle(reste); }
                catch (InvalidOperationException) { /* processus sans interface ou deja termine : on tente l'envoi */ }
                return poste.Envoyer(fenetre, message);
            }
            await Task.Delay(250);
        }
        return false;
    }

    private static LigneAiguillee Ligne(AffaireAiguillee a) => new(a.Numero, a.Logiciel, a.Motif);

    private static VueAiguillage Vide(string mode, string? greffe, string message) => new(mode, greffe, null, message, []);

    private static ResultatAncienRig Refus(string numero, string message) => new(false, "refuse", numero, message, null);

    private static string DuGreffe(string? greffe) => string.IsNullOrWhiteSpace(greffe) ? "" : $" du greffe {greffe}";

    private static string Date(DateTime d) => d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>
/// Les gestes reels du poste : processus et fenetres de la session Windows, lancement, premier plan, message
/// WM_COPYDATA et presse-papiers, par l'API Windows (user32 / kernel32) pour rester utilisables hors de WPF.
/// </summary>
// fix-ok: relecture avant le premier lancement -- la V2 execute EXECUTE_PROCESSUS dans sa WndProc (FormRigClientAccueil.cs:96-100, refus modal possible :1844), d'ou l'envoi hors du fil de l'ecran ; et la copie disait reussir sans verifier OpenClipboard/SetClipboardData.
public class PosteWindows : IPosteWindows
{
    /// <summary>La V2 d'un greffe dans CETTE session (un serveur RDS porte les sessions de plusieurs agents) : un
    /// processus du nom de l'exe dont la fenetre principale porte le titre de la console de ce greffe, comme la V2
    /// se cherche elle-meme (RigConsoleAccueil.cs:85).</summary>
    public virtual IntPtr? FenetreV2(string nomProcessus, string? greffe)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var debut = AncienRig.DebutTitreV2(greffe);
        foreach (var p in Process.GetProcessesByName(nomProcessus))
        {
            using (p)
            {
                try
                {
                    if (p.SessionId == session && p.MainWindowHandle != IntPtr.Zero
                        && p.MainWindowTitle.StartsWith(debut, StringComparison.Ordinal))
                        return p.MainWindowHandle;
                }
                catch (InvalidOperationException) { /* termine entre l'enumeration et la lecture */ }
            }
        }
        return null;
    }

    public virtual bool ExisteExe(string chemin) => File.Exists(chemin);

    public virtual Process? Lancer(string exe, string arguments)
        => Process.Start(new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            // Le dossier de l'exe, comme le « Demarrer dans » de ses raccourcis : la V2 y trouve ses DLL.
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        });

    public virtual void RamenerAuPremierPlan(IntPtr fenetre)
    {
        if (IsIconic(fenetre)) ShowWindow(fenetre, SW_RESTORE);
        SetForegroundWindow(fenetre);
    }

    /// <summary>WM_COPYDATA comme AmiCommunication.SendMessage (AmiCommunication.cs:36-47) : dwData = 1, le texte en
    /// UTF-16 termine par un zero, cbData = (longueur + 1) * 2. SendMessageTimeout plutot que SendMessage : une V2
    /// figee ne bloque pas la V3.</summary>
    public virtual bool Envoyer(IntPtr fenetre, string message)
    {
        var texte = Marshal.StringToHGlobalUni(message);
        try
        {
            var cds = new CopyDataStruct { dwData = 1, cbData = (message.Length + 1) * 2, lpData = texte };
            return SendMessageTimeout(fenetre, WM_COPYDATA, IntPtr.Zero, ref cds, SMTO_ABORTIFHUNG, 30_000, out _) != IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(texte); }
    }

    /// <summary>Le presse-papiers par l'API Windows. Il s'ouvre au nom d'une fenetre de la V3 : ouvert sans fenetre,
    /// EmptyClipboard laisse le presse-papiers sans proprietaire et SetClipboardData echoue (documentation de
    /// EmptyClipboard).</summary>
    public virtual bool Copier(string texte)
    {
        var fenetre = Process.GetCurrentProcess().MainWindowHandle;
        if (fenetre == IntPtr.Zero) return false;
        var ouvert = false;
        // Une autre application peut tenir le presse-papiers un instant (historique, bureau a distance).
        for (var essai = 0; essai < 10 && !(ouvert = OpenClipboard(fenetre)); essai++) Thread.Sleep(50);
        if (!ouvert) return false;
        try
        {
            if (!EmptyClipboard()) return false;
            var memoire = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)((texte.Length + 1) * 2));
            if (memoire == IntPtr.Zero) return false;
            var dest = GlobalLock(memoire);
            Marshal.Copy(texte.ToCharArray(), 0, dest, texte.Length);
            Marshal.WriteInt16(dest, texte.Length * 2, 0);
            GlobalUnlock(memoire);
            if (SetClipboardData(CF_UNICODETEXT, memoire) != IntPtr.Zero) return true;
            GlobalFree(memoire);
            return false;
        }
        finally { CloseClipboard(); }
    }

    private const int WM_COPYDATA = 0x004A, SW_RESTORE = 9, SMTO_ABORTIFHUNG = 0x0002, GMEM_MOVEABLE = 0x0002;
    private const uint CF_UNICODETEXT = 13;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyDataStruct
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam,
                                                    int flags, int timeoutMs, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(int flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr hMem);
}
