namespace RigV3Desktop.Pont;

/// <summary>
/// Les sessions ouvertes d'un ECRAN A SESSION (web/src/session.tsx) : l'etat de
/// l'ecran vit cote C#, une session par ecran monte, retrouvee par son
/// identifiant a chaque geste.
///
/// Pourquoi un plafond : la page JS peut disparaitre sans fermer ses sessions
/// (F5, reprise a chaud de wwwroot) ; sans lui, chaque rechargement laisserait
/// un ecran entier en memoire jusqu'a la fermeture de l'application. Au-dela de
/// <see cref="Plafond"/>, la plus ancienne est oubliee : aucun poste n'a trente
/// ecrans du meme traitement ouverts a la fois.
/// </summary>
public sealed class SessionsEcran<T> where T : class
{
    public const int Plafond = 32;

    private readonly Dictionary<string, T> ouvertes = new(StringComparer.Ordinal);
    private readonly Queue<string> ordre = new();
    private readonly object verrou = new();

    /// <summary>Range une session neuve et rend son identifiant.</summary>
    public string Ouvrir(T session)
    {
        var id = Guid.NewGuid().ToString("N");
        lock (verrou)
        {
            while (ouvertes.Count >= Plafond && ordre.TryDequeue(out var ancienne)) ouvertes.Remove(ancienne);
            ouvertes[id] = session;
            ordre.Enqueue(id);
        }
        return id;
    }

    /// <summary>La session de cet identifiant ; une session oubliee est une erreur franche.</summary>
    public T Lire(string id)
    {
        lock (verrou)
        {
            return ouvertes.TryGetValue(id, out var s)
                ? s
                : throw new InvalidOperationException($"Session d'ecran inconnue ou deja fermee : {id}.");
        }
    }

    public void Fermer(string id)
    {
        lock (verrou) { ouvertes.Remove(id); }
    }

    /// <summary>Nombre de sessions ouvertes (diagnostic et bancs).</summary>
    public int Nombre
    {
        get { lock (verrou) { return ouvertes.Count; } }
    }
}
