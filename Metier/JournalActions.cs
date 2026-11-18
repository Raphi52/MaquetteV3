using System;
using System.Collections.Generic;
using System.Linq;

namespace RigV3Desktop;

/// <summary>
/// UN GESTE fait par un agent du greffe : qui, quand, quoi, sur quoi, et
/// depuis quel ecran. Le verbe est ecrit en clair (« a modifie ») et non
/// code : le chef de greffe lit une phrase, il n a pas a traduire un
/// identifiant technique pour savoir ce qui s est passe.
/// </summary>
/// <param name="Login">Le compte qui a agi, tel qu il existe dans JeuDemo.</param>
/// <param name="Ecran">Le code PROC_ de l ecran d ou le geste est parti : c est
/// lui qui permettra plus tard d ouvrir l ecran concerne depuis le journal.</param>
public sealed record ActionAgent(string Login, DateTime Quand, string Verbe,
                                 string Objet, string Ecran)
{
    /// <summary>Le nom de l agent, lu sur son compte. Un login inconnu
    /// s affiche tel quel plutot que de disparaitre : un geste sans auteur
    /// lisible reste un geste a montrer.</summary>
    public string Agent
        => JeuDemo.Comptes.FirstOrDefault(c => c.Login == Login)?.Nom ?? Login;

    /// <summary>« il y a 4 min », « hier 15:20 ». Meme lecture du temps que
    /// les affaires recentes : deux tuiles cote a cote ne doivent pas compter
    /// les minutes de deux facons differentes.</summary>
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

/// <summary>
/// LE JOURNAL DES GESTES DU GREFFE. Le chef de greffe est responsable de
/// ce que son equipe inscrit au registre : il doit pouvoir dire, sans
/// ouvrir dix ecrans, qui a touche quel dossier ce matin. Rien ici n est
/// branche a la base : la maquette rejoue un jeu de gestes coherent avec
/// les comptes et les dossiers deja connus, pour que la tuile montre des
/// lignes qu on retrouve ailleurs dans l application.
///
/// Le journal est une LECTURE : il ne se trie pas a l ecran, il rend deja
/// ses lignes du plus recent au plus ancien. Une tuile qui devrait trier
/// elle-meme finirait par trier autrement qu un ecran voisin.
/// </summary>
public static class JournalActions
{
    /// <summary>Les verbes metier retenus. Liste FERMEE : un geste qui
    /// n entre dans aucun de ces quatre verbes est un geste qu on ne sait pas
    /// encore nommer, et il vaut mieux s en apercevoir ici qu a l ecran.</summary>
    public static readonly string[] Verbes =
        ["a ouvert", "a modifie", "a valide", "a depose"];

    /// <summary>
    /// Les gestes de la journee, ecrits du plus ancien au plus recent (la
    /// lecture les retournera). Les dossiers sont ceux de JeuDemo : le chef
    /// qui clique sur une ligne doit tomber sur une affaire qui existe.
    /// </summary>
    private static readonly (string Login, int MinutesAvant, string Verbe, string Objet, string Ecran)[] Gestes =
    [
        ("cmartin",    312, "a ouvert",  "dossier 004176",            "OUVERTURE_DOSSIER"),
        ("pjudic",     287, "a ouvert",  "affaire 2026B01067",        "PROC_XAFF"),
        ("mdupuis",    241, "a depose",  "creance sur 004155",        "PROC_CREANCE"),
        ("astagiaire", 198, "a modifie", "dirigeant du dossier 004182", "PROC_MDIL"),
        ("cmartin",    174, "a modifie", "denomination du dossier 004176", "PROC_MMA"),
        ("rvilain",    166, "a valide",  "modification du dossier 004182", "PROC_VDOSSIER"),
        ("pjudic",     120, "a depose",  "piece au dossier 2026B01067", "PROC_DEPOTJUD"),
        ("cmartin",     95, "a ouvert",  "dossier 004151",            "OUVERTURE_DOSSIER"),
        ("mdupuis",     61, "a modifie", "creance sur 004151",        "PROC_MODCREANCE"),
        ("astagiaire",  38, "a ouvert",  "dossier 004168",            "OUVERTURE_DOSSIER"),
        ("cmartin",     22, "a depose",  "acte au dossier 004151",    "PROC_ACTRET"),
        ("rvilain",      7, "a valide",  "depot du dossier 004151",   "PROC_VDOSSIER")
    ];

    /// <summary>
    /// L instant d ou partent les gestes de demonstration, fixe UNE fois au
    /// demarrage. Sans cette ancre, chaque lecture recalculerait « maintenant »
    /// et deux lectures separees de quelques millisecondes rendraient des
    /// gestes differents : la tuile ne pourrait plus se comparer a elle-meme,
    /// ni rafraichir sa liste sans la voir bouger. Le temps qui passe reste
    /// visible, puisque « il y a N min » se calcule, lui, a l affichage.
    /// </summary>
    private static readonly DateTime Origine = DateTime.Now;

    /// <summary>TOUTES les actions connues, du plus recent au plus ancien.</summary>
    public static ActionAgent[] Toutes
        => [.. Gestes
            .Select(g => new ActionAgent(g.Login, Origine.AddMinutes(-g.MinutesAvant),
                                         g.Verbe, g.Objet, g.Ecran))
            .OrderByDescending(a => a.Quand)];

    /// <summary>Les dernieres actions, tous agents confondus.</summary>
    public static ActionAgent[] Dernieres(int combien)
        => [.. Toutes.Take(Math.Max(combien, 0))];

    /// <summary>Les actions d UN agent. Un login vide ou inconnu ne rend pas
    /// tout le journal par surprise : vide = pas de filtre, inconnu = rien.</summary>
    public static ActionAgent[] De(string login)
        => string.IsNullOrWhiteSpace(login)
            ? Toutes
            : [.. Toutes.Where(a => a.Login == login)];

    /// <summary>Combien de gestes chaque agent a pose depuis minuit, du plus
    /// actif au moins actif. C est le chiffre que le chef lit en premier.</summary>
    public static (string Login, string Agent, int Nombre)[] ParAgentAujourdhui()
        => [.. Toutes
            .Where(a => a.Quand.Date == DateTime.Now.Date)
            .GroupBy(a => a.Login)
            .Select(g => (Login: g.Key, Agent: g.First().Agent, Nombre: g.Count()))
            .OrderByDescending(x => x.Nombre)
            .ThenBy(x => x.Agent, StringComparer.Ordinal)];

    /// <summary>Les logins presents dans le journal, pour garnir le filtre.</summary>
    public static string[] Agents
        => [.. Toutes.Select(a => a.Login).Distinct().OrderBy(l => l, StringComparer.Ordinal)];

    /// <summary>
    /// QUI A LE DROIT DE VOIR CE JOURNAL. Seul le compte qui VALIDE les
    /// modifications est le superieur hierarchique du greffe : lui seul a
    /// besoin -- et le droit -- de voir le travail des autres. Un redacteur
    /// ne surveille pas ses collegues.
    /// </summary>
    public static bool VisiblePour(CompteRig compte) => compte.PeutValider;
}
