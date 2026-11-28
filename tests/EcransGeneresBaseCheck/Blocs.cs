using System.Text.RegularExpressions;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.EcransGeneres;

// La lecture des jeux .gestes et .recherches de tests/EcransGeneresBaseCheck/Jeux/ (format : en-tete de Program.cs, 6).
// Fichier PARTAGE (28/11/2026) : EcransGeneresBaseCheck joue tous les blocs, tests/ParcoursCompletCheck rejoue le premier
// geste fait de chaque ecran du menu par le pont. Un seul analyseur, pour que les deux bancs lisent les memes jeux de la
// meme facon (il vivait au bas de Program.cs).

/// <summary>Un bloc d'un jeu .gestes ou .recherches (format : en-tete de Program.cs, 6).</summary>
sealed class Bloc
{
    public GesteEcran? Geste { get; set; }
    public string? Resultat { get; set; }
    public string? Message { get; set; }
    public List<string>? Lignes { get; set; }
    public string? Fiche { get; set; }
    public List<(string Cle, string Valeur)> ValeursFiche { get; } = [];
    public List<(string Sql, string Attendu)> Controles { get; } = [];
    public string? Agent { get; set; }
    public string? Texte { get; set; }
    public Dictionary<string, string> Criteres { get; } = new(StringComparer.Ordinal);
}

/// <summary>La lecture des jeux .gestes et .recherches : des blocs separes par « --- », des lignes « cle: valeur ».</summary>
static class Blocs
{
    private static readonly string[] Cles = ["geste:", "ids:", "valeur:", "resultat:", "message:", "lignes:", "fiche:", "controle:", "agent:", "texte:", "critere:"];

    /// <summary>« cle = valeur » (le premier « = » precede d'un blanc ; une valeur vide est permise).</summary>
    private static (string, string) Paire(string texte)
    {
        var i = texte.IndexOf(" =", StringComparison.Ordinal);
        return i < 0 ? (texte.Trim(), "") : (texte[..i].Trim(), texte[(i + 2)..].Trim());
    }

    public static List<Bloc> Lire(string[] lignes)
    {
        var blocs = new List<Bloc>();
        var b = new Bloc();
        string? suite = null;   // « lignes » ou « fiche » : les lignes qui suivent leur appartiennent
        TypeGeste type = default;
        string? nom = null;
        var ids = new List<string>();
        var valeurs = new Dictionary<string, string>(StringComparer.Ordinal);
        void Clore()
        {
            if (b.Resultat is not null) b.Geste = new GesteEcran(type, [.. ids], new Dictionary<string, string>(valeurs)) { Nom = nom };
            if (b.Resultat is not null || b.Lignes is not null || b.Texte is not null || b.Criteres.Count > 0) blocs.Add(b);
            b = new Bloc(); suite = null; nom = null; ids.Clear(); valeurs.Clear();
        }
        foreach (var brute in lignes)
        {
            var l = brute.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            if (l == "---") { Clore(); continue; }
            var cle = Cles.FirstOrDefault(c => l.StartsWith(c, StringComparison.Ordinal));
            if (cle is null)
            {
                if (suite == "lignes") b.Lignes!.Add(l);
                else if (suite == "fiche") b.ValeursFiche.Add(Paire(l));
                else throw new InvalidOperationException($"Ligne hors bloc : « {l} »");
                continue;
            }
            var v = l[cle.Length..].Trim();
            suite = null;
            switch (cle)
            {
                case "geste:":
                    var mots = v.Split(' ', 2);
                    type = Enum.Parse<TypeGeste>(mots[0]);
                    nom = mots.Length > 1 ? mots[1] : null;
                    break;
                case "ids:": ids.AddRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "valeur:": var (k, x) = Paire(v); valeurs[k] = x; break;
                case "resultat:": b.Resultat = v; break;
                case "message:": b.Message = v; break;
                case "lignes:": b.Lignes = []; suite = "lignes"; break;
                case "fiche:": b.Fiche = v; suite = "fiche"; break;
                case "controle:":
                    var i = v.LastIndexOf(" => ", StringComparison.Ordinal);
                    b.Controles.Add((v[..i].Trim(), v[(i + 4)..].Trim()));
                    break;
                case "agent:": b.Agent = v; break;
                case "texte:": b.Texte = v; break;
                case "critere:": var (c, y) = Paire(v); b.Criteres[c] = y; break;
            }
        }
        Clore();
        return blocs;
    }

    /// <summary>Une ligne au format des .attendu et des blocs « lignes: » : « Nom | Precisions | Groupe | Etat | [faites] ».</summary>
    public static string Texte(string nom, string precisions, string groupe, string etat, IEnumerable<int> faites)
        => $"{nom} | {precisions} | {groupe} | {etat} | [{string.Join(",", faites)}]";

    /// <summary>Une date relative au jour du banc dans une ligne attendue : {J-5}, {M-16}, {A+1} (jours, mois, annees), le premier
    /// ou le dernier jour d'un mois {D-1}, {F-1} (lot 9 : « le mois precedent complet » de PROC_STATCA, OPE_RECHERCHE.cs:37-58).</summary>
    public static string Dates(string ligne, DateTime jour) => Regex.Replace(ligne, @"\{([JMADF])([+-]\d+)\}", m =>
    {
        var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var mois = new DateTime(jour.Year, jour.Month, 1).AddMonths(n);
        return FormeLigne.Date(m.Groups[1].Value switch
        {
            "J" => jour.AddDays(n), "M" => jour.AddMonths(n), "D" => mois, "F" => mois.AddMonths(1).AddDays(-1), _ => jour.AddYears(n),
        });
    });
}
