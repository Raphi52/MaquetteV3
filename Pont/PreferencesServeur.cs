// fix-ok: CS0246 HttpRequestException -- un projet WPF n'importe pas System.Net.Http d'office (meme cause que SessionAgent.cs).
using System.Diagnostics;
using System.Net.Http;
using RigV3.Contrats.Socle;

namespace RigV3Desktop.Pont;

// ============================================================
// LES PREFERENCES DE L'AGENT ET SES DOCUMENTS, BRANCHES SUR LE SERVEUR (SOC-013, couche ecran T03 ;
// lot 4 du plan « Reconstruction de RIG — phase 2 », 2026-11-21).
//
// A l'ouverture, une fois l'agent admis dans son greffe (MainWindow, Pont/SessionAgent.cs) et AVANT
// l'accueil :
//   1. les preferences de l'agent sont lues dans la base du greffe et posees dans accueil.json
//      (AccueilEtat.ReprendrePreferences) : sur un autre serveur RDS, l'accueil s'ouvre avec SES
//      reglages (CA01), et l'impression sur SON imprimante (CA05) ;
//   2. le depot se branche (AccueilEtat.Distant) : chaque reglage change part au serveur, en
//      arriere-plan, seulement ce qui a change ;
//   3. la session est declaree (serveur RDS + session) : les documents qui attendaient l'agent lui
//      sont remis (CA04), puis la releve ne rend que ceux de CETTE session (CA03). Chaque document
//      remis leve DocumentsArrives ; Pont/DocumentsRecus.cs l'ouvre dans une fenetre de document.
// Sans RIGV3_SERVEUR, rien de tout cela : accueil.json seul, comme avant.
//
// Ce fichier ne depend que de ClientServeur, de Contrats/ et d'AccueilEtat : le banc
// tests/SoclePreferencesCheck le compile seul, contre le vrai serveur en memoire.
// ============================================================

/// <summary>Ce que l'ouverture a fait : preferences reprises du serveur, documents remis, ou la panne qui l'a empechee.</summary>
/// <param name="Reprises">Le nombre de preferences lues sur le serveur et posees dans accueil.json (0 : premier passage).</param>
/// <param name="Documents">Les documents qui attendaient l'agent (CA04).</param>
/// <param name="Panne">Null si tout a repondu ; sinon pourquoi les preferences restent celles de ce poste.</param>
public sealed record OuverturePreferences(int Reprises, DocumentRecu[] Documents, string? Panne);

public sealed class PreferencesServeur : IDepotPreferences
{
    /// <summary>Le depot de la session ouverte ; null hors serveur ou avant l'ouverture.</summary>
    public static PreferencesServeur? Actif { get; private set; }

    /// <summary>Delai entre un reglage change et son envoi : plusieurs gestes de suite partent ensemble.</summary>
    public static readonly TimeSpan Regroupement = TimeSpan.FromMilliseconds(400);

    private readonly ClientServeur client;
    private readonly string greffe;
    private readonly object verrou = new();

    /// <summary>Ce que le serveur tient : lu a l'ouverture, puis mis a jour a chaque envoi reussi.</summary>
    private readonly Dictionary<string, string> tenues = new(StringComparer.Ordinal);

    /// <summary>Ce qui a change depuis et attend de partir (le dernier etat de chaque nom gagne).</summary>
    private readonly Dictionary<string, string> aEnvoyer = new(StringComparer.Ordinal);

    private Task envoi = Task.CompletedTask;
    private readonly List<DocumentRecu> recus = [];

    private PreferencesServeur(ClientServeur client, string greffe, string serveur, string session)
    {
        this.client = client;
        this.greffe = greffe;
        Serveur = serveur;
        Session = session;
    }

    /// <summary>Le serveur RDS de cette V3 (V2 : AmiEnvironment.MachineName).</summary>
    public string Serveur { get; }

    /// <summary>La session Windows et l'instance de cette V3 (« 3:7f1c… »).</summary>
    public string Session { get; }

    /// <summary>Le dernier envoi en echec, ou null : le reglage reste dans accueil.json et repartira au prochain envoi.</summary>
    public string? DernierEchec { get; private set; }

    /// <summary>Les documents recus par cette session, du plus ancien au plus recent.</summary>
    public IReadOnlyList<DocumentRecu> Recus { get { lock (verrou) return [.. recus]; } }

    /// <summary>
    /// Des documents viennent d'arriver pour la session ouverte : a l'ouverture (CA04) ou a une releve (CA03). L'evenement
    /// est celui de l'application, pas d'un depot : l'app s'y abonne UNE fois, avant l'ouverture, sinon les documents qui
    /// attendaient l'agent arriveraient sans personne pour les ouvrir (Pont/DocumentsRecus.cs). Leve sur le fil de
    /// l'ouverture, ou sur un fil du pool pour la releve.
    /// </summary>
    public static event Action<PreferencesServeur, DocumentRecu[]>? DocumentsArrives;

    /// <summary>
    /// L'ouverture (voir l'en-tete). <paramref name="serveur"/> et <paramref name="session"/> : null = ce poste
    /// (le banc y passe deux serveurs RDS differents). Une panne ne bloque pas l'ouverture : l'accueil garde les
    /// reglages de ce poste, et la raison est rendue.
    /// </summary>
    public static async Task<OuverturePreferences> OuvrirAsync(ClientServeur client, string greffe, string? serveur = null,
                                                               string? session = null, CancellationToken ct = default)
    {
        Fermer();
        var depot = new PreferencesServeur(client, greffe, serveur ?? Environment.MachineName, session ?? SessionDeCetteV3());
        PreferencesAgent lues;
        try { lues = await client.LireAsync<PreferencesAgent>(RoutesPreferences.Pour(RoutesPreferences.Preferences, greffe), ct); }
        catch (Exception e) when (EstPanne(e))
        {
            // Sans relecture, le depot ne se branche pas : il ecraserait au retour du serveur des reglages plus recents.
            return new OuverturePreferences(0, [], $"Préférences non relues sur le serveur ({e.Message}) : celles de ce poste restent.");
        }

        var reprises = 0;
        if (lues.Valeurs.Count > 0)
        {
            reprises = AccueilEtat.ReprendrePreferences(lues.Valeurs);
            foreach (var (nom, valeur) in lues.Valeurs) depot.tenues[nom] = valeur;
        }
        Actif = depot;
        AccueilEtat.Distant = depot;
        // Le serveur n'a rien (premiere ouverture branchee) ou pas tout : ce poste lui donne ce qui manque ou differe.
        if (reprises < AccueilEtat.Preferences.Count) depot.Envoyer(AccueilEtat.Charger().PreferencesPartagees());

        DocumentRecu[] documents = [];
        string? panne = null;
        try
        {
            documents = await client.EnvoyerAsync<OuvertureSessionAgent, DocumentRecu[]>(
                RoutesPreferences.Pour(RoutesPreferences.Session, greffe), new OuvertureSessionAgent(depot.Serveur, depot.Session), ct);
        }
        catch (Exception e) when (EstPanne(e))
        {
            // Les documents restent au serveur : ils partiront a la prochaine ouverture (CA04).
            panne = $"Session non declaree au serveur ({e.Message}) : les documents en attente partiront a la prochaine ouverture.";
        }
        depot.Recevoir(documents);
        return new OuverturePreferences(reprises, documents, panne);
    }

    /// <summary>
    /// L'ouverture de l'application (MainWindow) : branchee au serveur et l'agent admis dans un greffe, les preferences et
    /// les documents de la session ; sinon rien (null). Puis la releve des documents, toutes les <paramref name="releve"/>.
    /// </summary>
    public static async Task<OuverturePreferences?> BrancherSessionAsync(TimeSpan releve)
    {
        if (ClientServeur.Actif is not { } client || SessionAgent.CodeGreffe is not { } greffe) return null;
        var ouverture = await OuvrirAsync(client, greffe);
        if (ouverture.Panne is not null) Trace.WriteLine("RigV3 SOC-013 : " + ouverture.Panne);
        Actif?.DemarrerReleve(releve);
        return ouverture;
    }

    private Timer? minuteur;

    /// <summary>
    /// Releve les documents de la session a intervalle regulier ; une panne attend la releve suivante. Le rappel du
    /// minuteur est « async void » : une exception qui en sortirait arreterait l'application entiere pour une releve
    /// de fond manquee. Toute erreur y est donc journalisee (Trace), et la releve suivante repart. DocumentsArrives est
    /// leve sur un fil du pool : un abonne d'interface repasse sur le sien.
    /// </summary>
    public void DemarrerReleve(TimeSpan intervalle)
        => minuteur = new Timer(async _ =>
        {
            try { await ReleverAsync(); }
            catch (Exception e) { Trace.WriteLine($"RigV3 SOC-013 : releve des documents en echec ({e.GetType().Name} : {e.Message})."); }
        }, null, intervalle, intervalle);

    /// <summary>Fin de session (fermeture de la fenetre) : les reglages changes partent, la releve s'arrete.</summary>
    public static async Task FermerAsync()
    {
        if (Actif is not { } depot) return;
        depot.minuteur?.Dispose();
        await depot.ViderAsync();
        Fermer();
    }

    /// <summary>Debranche le depot (fin de session, ou nouvelle ouverture).</summary>
    public static void Fermer()
    {
        if (AccueilEtat.Distant == Actif) AccueilEtat.Distant = null;
        Actif?.minuteur?.Dispose();
        Actif = null;
    }

    /// <summary>Recoit les preferences du fichier qui vient d'etre ecrit ; n'envoie que ce qui differe du serveur.</summary>
    public void Envoyer(IReadOnlyDictionary<string, string> preferences)
    {
        lock (verrou)
        {
            foreach (var (nom, valeur) in preferences)
            {
                if (tenues.TryGetValue(nom, out var tenue) && tenue == valeur) aEnvoyer.Remove(nom);
                else aEnvoyer[nom] = valeur;
            }
            if (aEnvoyer.Count == 0 || planifie) return;
            // A la suite de l'envoi en cours, jamais en meme temps : deux PUT croises pourraient arriver dans le desordre.
            planifie = true;
            envoi = envoi.ContinueWith(async _ => { await Task.Delay(Regroupement); await EnvoyerEnAttenteAsync(); },
                                       TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>Un envoi est prevu et n'a pas encore pris sa liste : un nouveau changement partira avec lui.</summary>
    private bool planifie;

    /// <summary>Attend que tout ce qui a change soit parti (fermeture de la fenetre, banc).</summary>
    public async Task ViderAsync()
    {
        Task enCours;
        lock (verrou) enCours = envoi;
        await enCours;
        await EnvoyerEnAttenteAsync();
    }

    private async Task EnvoyerEnAttenteAsync()
    {
        while (true)
        {
            Dictionary<string, string> lot;
            lock (verrou)
            {
                planifie = false;
                if (aEnvoyer.Count == 0) return;
                lot = new Dictionary<string, string>(aEnvoyer, StringComparer.Ordinal);
            }
            try
            {
                var tenu = await client.ModifierAsync<PreferencesAgent, PreferencesAgent>(
                    RoutesPreferences.Pour(RoutesPreferences.Preferences, greffe), new PreferencesAgent(lot, Serveur));
                lock (verrou)
                {
                    foreach (var (nom, valeur) in tenu.Valeurs) tenues[nom] = valeur;
                    // Ce qui a encore change pendant l'envoi reste a envoyer ; le reste est parti.
                    foreach (var (nom, valeur) in lot)
                        if (aEnvoyer.TryGetValue(nom, out var encore) && encore == valeur) aEnvoyer.Remove(nom);
                    DernierEchec = null;
                }
            }
            catch (Exception e) when (EstPanne(e))
            {
                // Pas de nouvel essai en boucle : le reglage est dans accueil.json, il repart au prochain geste.
                DernierEchec = e.Message;
                Trace.WriteLine($"RigV3 SOC-013 : preferences non envoyees au serveur ({e.Message}).");
                return;
            }
        }
    }

    /// <summary>Releve les documents prepares pour l'agent depuis la derniere fois, s'il travaille toujours ICI (CA03).</summary>
    public async Task<DocumentRecu[]> ReleverAsync(CancellationToken ct = default)
    {
        var adresse = RoutesPreferences.Pour(RoutesPreferences.Documents, greffe)
                      + $"?serveur={Uri.EscapeDataString(Serveur)}&session={Uri.EscapeDataString(Session)}";
        var documents = await client.LireAsync<DocumentRecu[]>(adresse, ct);
        Recevoir(documents);
        return documents;
    }

    /// <summary>Le contenu d'un document de l'agent, pour l'ouvrir a l'ecran (le fichier est sur le serveur, pas ici).</summary>
    public Task<ContenuDocument> LireContenuAsync(int id, CancellationToken ct = default)
        => client.LireAsync<ContenuDocument>(RoutesPreferences.PourDocument(greffe, id), ct);

    private void Recevoir(DocumentRecu[] documents)
    {
        if (documents.Length == 0) return;
        lock (verrou) recus.AddRange(documents);
        DocumentsArrives?.Invoke(this, documents);
    }

    /// <summary>La session Windows de ce processus et une marque propre a cette V3.</summary>
    private static string SessionDeCetteV3()
        => $"{Process.GetCurrentProcess().SessionId}:{Guid.NewGuid():N}";

    private static bool EstPanne(Exception e) => e is ErreurServeur or HttpRequestException or TaskCanceledException;
}
