using System.Reflection;
using System.Text.Json;

namespace RigV3Desktop.Pont;

// ============================================================
// LE REPARTITEUR : le cote C# du pont, depuis le retrait de Blazor (lot 3 du
// plan « finir le passage a React », 19/11/2026).
//
// La page parle au C# par la messagerie de WebView2, et rien d'autre :
//   page -> C#  { appel: n, nom: "EcranX.Methode", args: [...] }
//   C# -> page  { reponse: n, valeur: ... }  ou  { reponse: n, erreur: "..." }
//   C# -> page  { evenement: "racine", valeur: EtatRacine }   (sans demande)
// Le cote React est web/src/pont.ts (appeler<T>, ecouter, rappel).
//
// Deux sortes de destinataires :
//   - les methodes STATIQUES marquees [JSInvokable("<NomEcran>.<Methode>")] de
//     Pont/<NomEcran>.cs, trouvees par reflexion (aucune liste a tenir : deux
//     lots qui ajoutent chacun leur methode ne se marchent pas dessus) ;
//   - la RACINE de la fenetre (RacineWeb) : « Racine.Ouvrir » rend ce que la
//     page doit monter, « Racine.Rappeler » lui remonte un geste. Apres un
//     rappel, et a chaque changement venu d'ailleurs (minuteur, autre fenetre),
//     la racine est renvoyee a la page -- seulement si elle a change.
//
// Le repartiteur ne connait pas WebView2 : il recoit du JSON et poste du JSON
// par les deux fonctions qu'on lui donne. La fenetre le branche (PageWeb), un
// banc le joue directement (tests/FenetresCheck).
// ============================================================

/// <summary>Le pont d'UNE fenetre : recoit les appels de sa page, lui renvoie sa racine.</summary>
public sealed class Repartiteur
{
    /// <summary>Les memes regles que l'interop Blazor qu'il remplace : proprietes
    /// en camelCase a l'aller, sans casse au retour.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> Recensement = new(Recenser);

    /// <summary>Toutes les methodes appelables par la page, par identifiant.</summary>
    public static IReadOnlyDictionary<string, MethodInfo> Methodes => Recensement.Value;

    private readonly RacineWeb Racine;
    private readonly Action<string> Poster;
    private readonly Action<Action> SurLeFil;
    private string? DernierEnvoi;
    private bool Ouverte;

    /// <param name="racine">Ce que la page de cette fenetre monte.</param>
    /// <param name="poster">Envoie un message JSON a la page (PostWebMessageAsJson).</param>
    /// <param name="surLeFil">Rejoue une action sur le fil de la fenetre (Dispatcher) :
    /// les minuteurs de l'accueil levent leurs changements depuis un autre fil.</param>
    public Repartiteur(RacineWeb racine, Action<string> poster, Action<Action> surLeFil)
    {
        Racine = racine;
        Poster = poster;
        SurLeFil = surLeFil;
        Racine.Change += () => SurLeFil(Pousser);
        Racine.Brancher(action => SurLeFil(() => { action(); Pousser(); }));
    }

    /// <summary>
    /// Un message de la page. Rend la main une fois la reponse postee ; une
    /// erreur du C# part a la page (sa promesse est rejetee avec ce message),
    /// jamais avalee.
    /// </summary>
    public async Task Recevoir(string message)
    {
        long numero;
        string nom;
        JsonElement[] args;
        try
        {
            using var document = JsonDocument.Parse(message);
            var racine = document.RootElement;
            numero = racine.GetProperty("appel").GetInt64();
            nom = racine.GetProperty("nom").GetString() ?? string.Empty;
            args = racine.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(e => e.Clone()).ToArray()
                : [];
        }
        catch (Exception erreur) when (erreur is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Sans numero d'appel, aucune promesse a rejeter : le message est
            // illisible, on le dit dans la sortie de debogage et on s'arrete.
            System.Diagnostics.Debug.WriteLine($"[pont] message illisible : {erreur.Message}");
            return;
        }

        object? valeur;
        try
        {
            valeur = await Appeler(nom, args);
        }
        catch (Exception erreur)
        {
            var cause = erreur is TargetInvocationException { InnerException: { } interieure } ? interieure : erreur;
            System.Diagnostics.Debug.WriteLine($"[pont] {nom} : {cause}");
            Envoyer(JsonSerializer.Serialize(new { reponse = numero, erreur = cause.Message }, Json));
            return;
        }
        Envoyer(JsonSerializer.Serialize(new { reponse = numero, valeur }, Json));
    }

    /// <summary>Poste a la page, tant que sa fenetre est ouverte : une fois la
    /// fenetre fermee, sa vue WebView2 est detruite et refuserait le message
    /// (un minuteur ou une reponse peut arriver juste apres la fermeture).</summary>
    private void Envoyer(string message)
    {
        if (Racine.Fermee) return;
        Poster(message);
    }

    private async Task<object?> Appeler(string nom, JsonElement[] args)
    {
        switch (nom)
        {
            case "Racine.Ouvrir":
                Exiger(nom, args, 0);
                return Ouvrir();
            case "Racine.Rappeler":
                Exiger(nom, args, 2);
                await Racine.Rappeler(args[0].GetString() ?? string.Empty, args[1]);
                // Comme Blazor apres un EventCallback : la page redessine.
                Pousser();
                return null;
        }

        if (!Methodes.TryGetValue(nom, out var methode))
            throw new InvalidOperationException($"Aucune methode [JSInvokable(\"{nom}\")] dans le pont.");

        var parametres = methode.GetParameters();
        Exiger(nom, args, parametres.Length);
        var valeurs = new object?[parametres.Length];
        for (var i = 0; i < parametres.Length; i++)
        {
            var type = parametres[i].ParameterType;
            valeurs[i] = type == typeof(JsonElement) ? args[i] : args[i].Deserialize(type, Json);
        }

        var resultat = methode.Invoke(null, valeurs);
        if (resultat is Task tache)
        {
            await tache;
            return methode.ReturnType.IsGenericType
                ? methode.ReturnType.GetProperty(nameof(Task<int>.Result))!.GetValue(tache)
                : null;
        }
        return resultat;
    }

    private static void Exiger(string nom, JsonElement[] args, int attendus)
    {
        // Meme regle que l'interop Blazor : le compte d'arguments est exact.
        if (args.Length != attendus)
            throw new ArgumentException($"« {nom} » attend {attendus} argument(s), {args.Length} recu(s).");
    }

    /// <summary>
    /// La page demande sa racine : a son chargement, et de nouveau apres un
    /// rechargement (F5, reprise a chaud). La racine n'est ouverte qu'UNE fois :
    /// un rechargement retrouve la session telle qu'elle est.
    /// </summary>
    private EtatRacine Ouvrir()
    {
        if (!Ouverte)
        {
            Ouverte = true;
            Racine.Ouvrir();
        }
        var etat = Racine.Etat();
        DernierEnvoi = Message(etat);
        if (!Racine.Demarree)
        {
            // Apres le premier rendu, comme OnAfterRender(premierRendu) de
            // Home.razor : la page a deja sa racine, un changement qui suit
            // lui arrive par Pousser.
            SurLeFil(() => { Racine.Demarrer(); Pousser(); });
        }
        return etat;
    }

    /// <summary>Renvoie la racine a la page, si elle a change depuis le dernier envoi.</summary>
    public void Pousser()
    {
        if (!Ouverte || Racine.Fermee) return;
        var message = Message(Racine.Etat());
        if (message == DernierEnvoi) return;
        DernierEnvoi = message;
        Envoyer(message);
    }

    private static string Message(EtatRacine etat)
        => JsonSerializer.Serialize(new { evenement = "racine", valeur = etat }, Json);

    private static IReadOnlyDictionary<string, MethodInfo> Recenser()
    {
        var methodes = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var type in typeof(Repartiteur).Assembly.GetTypes())
        {
            foreach (var methode in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (methode.GetCustomAttribute<JSInvokableAttribute>() is not { } marque) continue;
                if (!methodes.TryAdd(marque.Identifiant, methode))
                    throw new InvalidOperationException(
                        $"Deux methodes portent l'identifiant « {marque.Identifiant} » : {methodes[marque.Identifiant].DeclaringType}.{methodes[marque.Identifiant].Name} et {type}.{methode.Name}.");
            }
        }
        return methodes;
    }
}

/// <summary>
/// Ce que la page d'une fenetre monte : le composant React (web/src/composants/),
/// ses proprietes, les noms des gestes qu'il remonte, et les bandes de
/// redimensionnement (fenetre principale seule).
/// </summary>
public sealed record EtatRacine(string Composant, string[] Rappels, bool Bords,
                                IReadOnlyDictionary<string, object?> Proprietes);
