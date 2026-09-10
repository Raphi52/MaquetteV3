using System.IO;
using System.Linq;
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

    /// <summary>
    /// Rang d'empilement : la DERNIERE tuile manipulee passe devant les
    /// autres. Sans lui, deux tuiles qui se chevauchent s'empilent dans
    /// l'ordre du code et celle qu'on vient de bouger peut se retrouver
    /// dessous.
    /// </summary>
    public int Ordre { get; set; }
}

/// <summary>
/// Ce que l'accueil doit retenir d'une session a l'autre : ou sont posees
/// les tuiles, lesquelles sont allumees, et quelles alertes sont ecoutees.
/// Un fichier JSON dans le profil utilisateur suffit : rien ici n'a besoin
/// de la base.
/// </summary>
/// <summary>Une ouverture d affaire, horodatee a l instant du clic.</summary>
public sealed class ConsultationAffaire
{
    public string Numero { get; set; } = string.Empty;
    public string Objet { get; set; } = string.Empty;
    public string Siren { get; set; } = string.Empty;
    public string Type { get; set; } = "RCS";
    public string Statut { get; set; } = "neutre";
    public DateTime Quand { get; set; }

    /// <summary>« il y a 4 min », « hier 15:20 » — lu, pas calcule a l ecran.</summary>
    public string Depuis
    {
        get
        {
            var ecart = DateTime.Now - Quand;
            if (ecart.TotalMinutes < 1) return "a l instant";
            if (ecart.TotalMinutes < 60) return $"il y a {(int)ecart.TotalMinutes} min";
            if (ecart.TotalHours < 24) return $"il y a {(int)ecart.TotalHours} h";
            if (ecart.TotalDays < 2) return $"hier {Quand:HH:mm}";
            return Quand.ToString("dd/MM/yyyy HH:mm");
        }
    }
}

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
    /// Historique de consultation : les affaires REELLEMENT ouvertes par
    /// l utilisateur, la plus recente en tete. RIG v2 n a pas cette
    /// information (la table AFFAIRE ne porte qu une date de creation) :
    /// elle est donc produite ici, a l instant du clic, et conservee dans
    /// accueil.json.
    /// </summary>
    public List<ConsultationAffaire> Consultations { get; set; } = new();

    /// <summary>Nombre d affaires retenues dans l historique.</summary>
    public const int PlafondConsultations = 20;

    /// <summary>
    /// Note qu une affaire vient d etre ouverte. Une affaire deja presente
    /// remonte en tete au lieu d etre dupliquee, et l historique est borne :
    /// sans cela le fichier grossirait sans fin.
    /// </summary>
    /// <summary>
    /// Fait passer une tuile DEVANT toutes les autres. Appele quand on la
    /// deplace, la redimensionne ou l'allume : le geste le plus recent
    /// gagne. Les rangs sont renumerotes a partir de 1 pour qu'ils ne
    /// grimpent pas indefiniment dans le fichier.
    /// </summary>
    public void Avancer(string id)
    {
        if (!Tuiles.TryGetValue(id, out var cible)) return;
        var rang = 1;
        foreach (var pose in Tuiles.Where(t => t.Key != id)
                                   .OrderBy(t => t.Value.Ordre)
                                   .Select(t => t.Value))
            pose.Ordre = rang++;
        cible.Ordre = rang;
    }

    public void Consulter(string numero, string objet, string siren, string type, string statut)
    {
        if (string.IsNullOrWhiteSpace(numero)) return;
        Consultations.RemoveAll(c => c.Numero == numero);
        Consultations.Insert(0, new ConsultationAffaire
        {
            Numero = numero, Objet = objet, Siren = siren,
            Type = type, Statut = statut, Quand = DateTime.Now
        });
        if (Consultations.Count > PlafondConsultations)
            Consultations.RemoveRange(PlafondConsultations,
                Consultations.Count - PlafondConsultations);
        Enregistrer();
    }

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
