using System.Globalization;
using System.Text;
using RigV3Desktop;
using RigV3Desktop.Pont;

namespace SoclePassageCheck;

/// <summary>
/// LES SUPPORTS DE FORMATION TIRES DES ECRANS DE LA V3 (SOC-026 T03, CA05) : ce que voit chaque metier dans le menu
/// (Pont\MenuAccueil.cs filtre par Metier\PerimetreProfil.cs), si l'ecran existe deja dans la V3
/// (Pont\CatalogueEcrans.cs), et les libelles de la V2 qui changent (Pont\InventaireTouchesV2.cs, nom de l'ecran dans
/// la V2, contre le nom que la V3 lui donne). Le banc les recalcule a chaque passage : une ligne des supports qui ne
/// correspond plus a un ecran est un support qui ment (rouge) ; un ecran nouveau pas encore dans les supports est a
/// regenerer (RESTE). Regenerer : RIGV3_ECRIRE_FORMATION=1 .\tests.ps1 SoclePassage.
/// </summary>
internal static class SupportsFormation
{
    public const string FichierEcrans = "ecrans-par-metier.md";
    public const string FichierLibelles = "libelles-qui-changent.md";

    private const string Genere =
        "<!-- FICHIER GENERE par tests/SoclePassageCheck (SupportsFormation.cs) a partir des ecrans de la V3 : ne pas modifier a la main. " +
        "Regenerer : RIGV3_ECRIRE_FORMATION=1 .\\tests.ps1 SoclePassage -->";

    /// <summary>Les metiers du greffe (Metier\JeuDemo.cs, DomaineGed) : un agent n'en pratique qu'un a la fois.</summary>
    public static readonly (string Domaine, string Nom)[] Metiers =
        [(DomaineGed.Rcs, "RCS"), (DomaineGed.Judiciaire, "Judiciaire"), (DomaineGed.Endettement, "Endettement (injonctions, creances)")];

    /// <summary>Le vocabulaire impose par les textes (SOC-026 CA05) : il ne change pas d'un logiciel a l'autre.</summary>
    public static readonly string[] VocabulaireDesTextes = ["immatriculation", "radiation", "kbis", "depot", "mention"];

    private static readonly HashSet<string> Portes = CatalogueEcrans.Familles.SelectMany(f => f.Ecrans).Select(e => e.Code).ToHashSet(StringComparer.Ordinal);

    private sealed record Ligne(string Chemin, EntreeMenu Entree);

    private static IEnumerable<Ligne> Menu() =>
        MenuAccueil.Moments.SelectMany(m => m.Sous.SelectMany(s => s.Entrees.Select(e => new Ligne($"{m.Titre} > {s.Titre}", e))));

    private static string LigneTableau(Ligne l)
        => $"| {l.Chemin} | {l.Entree.Libelle} | {l.Entree.Detail} | `{l.Entree.Code}` | {(Portes.Contains(l.Entree.Code) ? "dans la V3" : "a venir")} |";

    public static string EcransParMetier()
    {
        var s = new StringBuilder();
        s.AppendLine(Genere).AppendLine();
        s.AppendLine("# Les écrans de la V3, métier par métier (SOC-026)").AppendLine();
        s.AppendLine("Ce que l'agent trouve dans le menu de gauche de la V3, rangé comme le menu (geste › verbe), pour son métier.");
        s.AppendLine("« dans la V3 » : l'écran est construit ; « a venir » : l'entrée est au menu, son écran arrive avec son domaine.");
        s.AppendLine("Le code entre guillemets inversés est celui de la procédure de RIG V2 : c'est le même écran, l'agent le reconnaît.").AppendLine();
        foreach (var (domaine, nom) in Metiers)
        {
            var propres = Menu().Where(l => PerimetreProfil.Reserve(l.Entree.Code).Contains(domaine)).ToList();
            s.AppendLine($"## {nom} — {propres.Count} entrée(s) propres au métier").AppendLine();
            s.AppendLine("| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |");
            s.AppendLine("|---|---|---|---|---|");
            foreach (var l in propres) s.AppendLine(LigneTableau(l));
            s.AppendLine();
        }
        var communes = Menu().Where(l => PerimetreProfil.Reserve(l.Entree.Code).Length == 0).ToList();
        s.AppendLine($"## Tous les métiers — {communes.Count} entrée(s) communes (courrier, documents, comptabilité, réglages)").AppendLine();
        s.AppendLine("| Geste › verbe | Entrée du menu | Ce qu'on y trouve | Procédure V2 | Écran |");
        s.AppendLine("|---|---|---|---|---|");
        foreach (var l in communes) s.AppendLine(LigneTableau(l));
        return s.ToString();
    }

    /// <summary>Les ecrans dont le nom change de la V2 a la V3 : (procedure, nom V2, nom V3).</summary>
    public static IReadOnlyList<(string Code, string V2, string V3)> Changements()
        => [.. InventaireTouchesV2.Lignes.GroupBy(t => t.Procedure).Select(g => (Code: g.Key, V2: g.First().EcranV2, V3: CatalogueEcrans.Nom(g.Key)))
               .Where(x => x.V3.Length > 0 && Norme(x.V2) != Norme(x.V3)).OrderBy(x => x.Code, StringComparer.Ordinal)];

    public static string LibellesQuiChangent()
    {
        var s = new StringBuilder();
        s.AppendLine(Genere).AppendLine();
        s.AppendLine("# Les libellés propres à la V2 qui changent dans la V3 (SOC-026 CA05)").AppendLine();
        s.AppendLine("Le vocabulaire imposé par les textes — immatriculation, radiation, Kbis, dépôt des comptes, mentions — ne change pas.");
        s.AppendLine("Seuls changent les noms que la V2 donnait à ses écrans. À gauche le nom de l'écran dans RIG V2, à droite celui de la V3 ;");
        s.AppendLine("les gestes au clavier sont dans la fiche « Ancienne touche, nouvelle touche » (SOC-027, menu Administrer).").AppendLine();
        s.AppendLine("| Procédure V2 | Nom dans RIG V2 | Nom dans la V3 |");
        s.AppendLine("|---|---|---|");
        foreach (var (code, v2, v3) in Changements()) s.AppendLine($"| `{code}` | {v2} | {v3} |");
        return s.ToString();
    }

    /// <summary>Les changements qui feraient perdre un mot impose par les textes (SOC-026 CA05).</summary>
    public static IReadOnlyList<string> VocabulairePerdu()
        => [.. Changements().SelectMany(c => VocabulaireDesTextes.Where(m => Norme(c.V2).Contains(m) && !Norme(c.V3).Contains(m))
                                                                  .Select(m => $"{c.Code} : « {c.V2} » -> « {c.V3} » perd « {m} »"))];

    /// <summary>Les lignes de tableau d'un support (hors en-tetes) : ce qui se compare au recalcul.</summary>
    public static IReadOnlyList<string> LignesDeTableau(string texte)
        => [.. texte.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Geste", StringComparison.Ordinal)
                                                                  && !l.StartsWith("| Procédure V2 | Nom", StringComparison.Ordinal))];

    public static string Norme(string s)
        => new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
           .ToLowerInvariant().Replace("'", " ").Replace("-", " ").Replace("  ", " ").Trim();
}
