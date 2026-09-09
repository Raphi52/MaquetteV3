using System.IO;
using System.Text.Json;

namespace RigV3Desktop;

/// <summary>Pose d'une tuile sur la grille de l'accueil (12 colonnes).</summary>
public sealed class PoseTuile
{
    public int Colonne { get; set; } = 1;
    public int Ligne { get; set; } = 1;
    public int Largeur { get; set; } = 4;
    public int Hauteur { get; set; } = 2;
    public bool Visible { get; set; } = true;
}

/// <summary>
/// Ce que l'accueil doit retenir d'une session a l'autre : ou sont posees
/// les tuiles, lesquelles sont allumees, et quelles alertes sont ecoutees.
/// Un fichier JSON dans le profil utilisateur suffit : rien ici n'a besoin
/// de la base.
/// </summary>
public sealed class AccueilEtat
{
    public Dictionary<string, PoseTuile> Tuiles { get; set; } = new();
    public Dictionary<string, bool> Alertes { get; set; } = new();

    /// <summary>Mesures du widget performance retenues, par libelle.</summary>
    public Dictionary<string, bool> Mesures { get; set; } = new();

    /// <summary>
    /// Saisie guidee conservee d une session a l autre : par affaire, puis
    /// par etape, puis une LISTE de fiches — une etape comme Dirigeants ou
    /// Etablissements en accepte plusieurs.
    /// </summary>
    public Dictionary<string, Dictionary<string, List<Dictionary<string, string>>>> Saisies { get; set; } = new();

    /// <summary>Affaires dont la saisie a passe le controle final.</summary>
    public HashSet<string> Valides { get; set; } = new();

    /// <summary>
    /// Journal VGED par affaire : la commande « Historique VGED » de RIG v2
    /// (ETP_VGED/OPE_VGED.cs) affiche les evenements du dossier. Ici les
    /// lignes sont HORODATEES a l instant de l action reelle, jamais
    /// reconstituees a l affichage.
    /// </summary>
    public Dictionary<string, List<string>> Journal { get; set; } = new();

    /// <summary>
    /// Ajoute une ligne horodatee au journal d une affaire. Une ligne
    /// identique a la precedente n est pas repetee : sinon chaque frappe
    /// dans un champ produirait un evenement et le journal deviendrait
    /// illisible.
    /// </summary>
    public void Journaliser(string affaire, string evenement)
    {
        if (string.IsNullOrWhiteSpace(affaire)) return;
        if (!Journal.TryGetValue(affaire, out var lignes))
        {
            lignes = new List<string>();
            Journal[affaire] = lignes;
        }
        var ligne = $"{DateTime.Now:dd/MM/yyyy HH:mm} · {evenement}";
        if (lignes.Count > 0 && lignes[^1].EndsWith(evenement, StringComparison.Ordinal)) return;
        lignes.Add(ligne);
        Enregistrer();
    }

    private static string Chemin => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RigV3", "accueil.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AccueilEtat Charger()
    {
        try
        {
            if (File.Exists(Chemin))
            {
                var lu = JsonSerializer.Deserialize<AccueilEtat>(File.ReadAllText(Chemin));
                if (lu is not null) return lu;
            }
        }
        catch (Exception)
        {
            // Un etat illisible ne doit jamais empecher l'accueil de s'ouvrir :
            // on repart de la disposition par defaut.
        }
        return new AccueilEtat();
    }

    public void Enregistrer()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
            File.WriteAllText(Chemin, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception)
        {
            // Idem : ne pas faire tomber l'interface pour un disque en lecture seule.
        }
    }
}
