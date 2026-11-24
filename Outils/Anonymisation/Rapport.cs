using System.Globalization;
using System.Text;

namespace RigV3.Outils.Anonymisation;

/// <summary>Une colonne traitee : sa regle, d'ou elle vient, combien de valeurs, combien changees.</summary>
public sealed class ColonneTraitee(string table, string colonne, string type, int longueur, Regle regle)
{
    public string Table { get; } = table;
    public string Colonne { get; } = colonne;
    public string Type { get; } = type;
    public int Longueur { get; } = longueur;
    public Regle Regle { get; } = regle;
    public long Valeurs { get; set; }
    public long Modifiees { get; set; }
    public bool Document => Regle.Traitement == Traitement.Document;
    /// <summary>Document : nombre de documents par format (« Pdf », « Zip »…) et octets ecrits (les memes que la source).</summary>
    public SortedDictionary<string, long> Formats { get; } = new(StringComparer.Ordinal);
    public long Octets { get; set; }
    /// <summary>Champ libre au sens de CA04 : texte long (plus de 100 caracteres ou max) ou XML.</summary>
    public bool ChampLibre => !Document && (Type == "xml" || Longueur < 0 || Longueur > 100 * (Type.StartsWith('n') ? 2 : 1));
}

/// <summary>
/// LE RAPPORT DE LA COPIE (SOC-021 T02, CA04, CA05, CA07) : ce qui a ete lu, comment, ce qui a ete
/// traite et pourquoi. Il ne contient AUCUNE valeur des bases : des noms de tables et de colonnes,
/// des nombres, des messages d'erreur du serveur.
/// </summary>
public sealed class Rapport
{
    public string Source { get; set; } = "";
    public string Copie { get; set; } = "";
    public DateTime Debut { get; set; }
    public DateTime Fin { get; set; }
    public bool Reproductible { get; set; }
    public string? Accord { get; set; }
    public int RequetesSource { get; set; }
    public long? EcrituresSourceAvant { get; set; }
    public long? EcrituresSourceApres { get; set; }
    public long? MaxLignes { get; set; }
    /// <summary>Copie reduite fermee sur ses cles etrangeres (Fermeture.cs) : ses cles sont posees avec verification.</summary>
    public bool Fermee { get; set; }
    public int ToursFermeture { get; set; }
    public long DureeFermetureMs { get; set; }
    /// <summary>Cles etrangeres entre tables copiees que la fermeture a suivies.</summary>
    public int LiensFermes { get; set; }
    /// <summary>Lignes parentes ajoutees par la fermeture, par table (en plus des N premieres).</summary>
    public List<(string Table, long Lignes)> AjoutsFermeture { get; } = [];
    /// <summary>Copie reduite : les tables copiees en entier (OptionsCopie.TablesEntieres).</summary>
    public List<string> TablesEntieres { get; } = [];
    public List<(string Table, long Lignes)> Volumes { get; } = [];
    public List<ColonneTraitee> Colonnes { get; } = [];
    /// <summary>Les colonnes binaires GARDEES (binaire technique : cle, empreinte, signature de chaine) et pourquoi.</summary>
    public List<(string Colonne, string Type, string Raison)> BinairesGardes { get; } = [];
    public List<(string Colonne, string Raison)> ARelire { get; } = [];
    public List<(string Table, long Lignes)> TablesSigneesTouchees { get; } = [];
    public List<string> IndexCrees { get; } = [];
    public List<string> ClesVerifiees { get; } = [];
    public List<string> ClesNonVerifiees { get; } = [];
    public List<string> ModulesCrees { get; } = [];
    public List<string> Ignores { get; } = [];
    public List<string> Anomalies { get; } = [];

    public bool ChainesInvalidees => TablesSigneesTouchees.Count > 0;
    public long LignesSigneesTouchees => TablesSigneesTouchees.Sum(t => t.Lignes);
    public const string MotifChaines = "chaînes invalidées par l'anonymisation";

    public string EnMarkdown()
    {
        var sb = new StringBuilder();
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        string N(long n) => n.ToString("N0", fr);
        sb.AppendLine("# Copie anonymisée d'une base V2 — rapport (SOC-021)").AppendLine();
        sb.AppendLine($"- Source : `{Source}` (lue en lecture seule)");
        sb.AppendLine($"- Copie : `{Copie}`");
        sb.AppendLine($"- Début : {Debut:yyyy-MM-dd HH:mm:ss}, fin : {Fin:yyyy-MM-dd HH:mm:ss} ({(Fin - Debut).TotalSeconds:0} s)");
        sb.AppendLine($"- Clé des valeurs fictives : {(Reproductible ? "tirée d'une GRAINE (copie reproductible ; refusée pour une source hors de ce poste)" : "aléatoire, effacée de la mémoire en fin de copie : la correspondance est détruite")}");
        sb.AppendLine($"- Accord : {(Accord is null ? "source sur ce poste, pas d'accord requis" : $"donné pour `{Accord}`")}");
        if (MaxLignes is { } m)
            sb.AppendLine(Fermee
                ? $"- Copie RÉDUITE : les {N(m)} premières lignes de chaque table, puis FERMÉE sur ses clés étrangères ({LiensFermes} clés suivies) : " +
                  $"{N(AjoutsFermeture.Sum(a => a.Lignes))} lignes parentes ajoutées dans {AjoutsFermeture.Count} tables, en {ToursFermeture} tours, " +
                  $"{DureeFermetureMs / 1000.0:0.0} s ; clés étrangères posées AVEC vérification"
                : $"- Copie RÉDUITE : au plus {N(m)} lignes par table, NON fermée sur ses clés (clés étrangères posées sans vérification)");
        if (TablesEntieres.Count > 0)
            sb.AppendLine($"- Tables copiées EN ENTIER malgré la réduction ({TablesEntieres.Count}) : {string.Join(", ", TablesEntieres)}");
        sb.AppendLine($"- **Chaînes signées : {(ChainesInvalidees ? $"INVALIDÉES par l'anonymisation ({N(LignesSigneesTouchees)} lignes signées touchées)" : "aucune ligne signée touchée")}**");
        sb.AppendLine();

        sb.AppendLine("## Lecture seule de la source (CA07)").AppendLine();
        sb.AppendLine($"- {RequetesSource} requêtes jouées sur la source, toutes passées par la porte de lecture V2 (`LectureV2.ExigerLecture` : un SELECT seul, ni INTO ni point-virgule), connexion `ApplicationIntent=ReadOnly`.");
        sb.AppendLine(EcrituresSourceAvant is { } av && EcrituresSourceApres is { } ap
            ? $"- Écritures recensées par le serveur sur la base source (`sys.dm_db_index_usage_stats`, `user_updates`) : {N(av)} avant, {N(ap)} après{(av == ap ? " — aucune pendant la copie." : " — DES ÉCRITURES ONT EU LIEU pendant la copie (d'autres sessions écrivent-elles sur cette base ?).")}"
            : "- Écritures du serveur source : non lisibles (droit `VIEW SERVER STATE` absent) ; la garantie reste la porte de lecture.");
        sb.AppendLine();

        sb.AppendLine("## Volumes").AppendLine();
        sb.AppendLine("| Table | Lignes copiées |").AppendLine("|---|---:|");
        foreach (var (t, n) in Volumes) sb.AppendLine($"| {t} | {N(n)} |");
        sb.AppendLine();
        if (Fermee)
        {
            sb.AppendLine("Dont lignes parentes ajoutées par la fermeture sur les clés étrangères :").AppendLine();
            sb.AppendLine("| Table | Lignes ajoutées |").AppendLine("|---|---:|");
            foreach (var (t, n) in AjoutsFermeture) sb.AppendLine($"| {t} | {N(n)} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Colonnes anonymisées (CA01, CA02)").AppendLine();
        sb.AppendLine("| Colonne | Traitement | Origine de la règle | Valeurs | Modifiées |").AppendLine("|---|---|---|---:|---:|");
        foreach (var c in Colonnes.Where(c => !c.ChampLibre && !c.Document))
            sb.AppendLine($"| {c.Table}.{c.Colonne} | {c.Regle.Traitement} | {c.Regle.Origine} | {N(c.Valeurs)} | {N(c.Modifiees)} |");
        sb.AppendLine();

        sb.AppendLine("## Documents remplacés (CA01, CA03, CA04)").AppendLine();
        sb.AppendLine("Colonnes binaires qui portent un document (GED, Kbis, flux, pièces numérisées) : chaque document est remplacé par un " +
                      $"document FICTIF du même format, complété à la même taille (« {DocumentsFictifs.TexteFictif} »). La source n'a rendu " +
                      "que le format et la taille de chacun : aucun octet d'un document réel n'est sorti de la source. NULL reste NULL.").AppendLine();
        sb.AppendLine("| Colonne | Type | Documents | Remplacés | Formats | Octets |").AppendLine("|---|---|---:|---:|---|---:|");
        foreach (var c in Colonnes.Where(c => c.Document))
            sb.AppendLine($"| {c.Table}.{c.Colonne} | {c.Type} | {N(c.Valeurs)} | {N(c.Modifiees)} | " +
                          $"{(c.Formats.Count == 0 ? "—" : string.Join(", ", c.Formats.Select(f => $"{f.Key} {N(f.Value)}")))} | {N(c.Octets)} |");
        if (!Colonnes.Any(c => c.Document)) sb.AppendLine("| aucune | | | | | |");
        sb.AppendLine();
        sb.AppendLine("Binaires gardés (binaire technique, sans personne) :").AppendLine();
        if (BinairesGardes.Count == 0) sb.AppendLine("- aucun.");
        foreach (var (c, t, r) in BinairesGardes) sb.AppendLine($"- `{c}` ({t}) : {r}");
        sb.AppendLine();

        sb.AppendLine("## Champs libres (CA04)").AppendLine();
        sb.AppendLine("Textes longs et XML : vidés, ou anonymisés mot à mot (longueur gardée ; un XML garde ses noms d'éléments et d'attributs, chaque valeur prend la règle de sa colonne).").AppendLine();
        sb.AppendLine("| Champ libre | Traitement | Origine de la règle | Valeurs | Modifiées |").AppendLine("|---|---|---|---:|---:|");
        foreach (var c in Colonnes.Where(c => c.ChampLibre))
            sb.AppendLine($"| {c.Table}.{c.Colonne} | {(c.Regle.Traitement == Traitement.Vider ? "vidé" : $"anonymisé ({c.Regle.Traitement})")} | {c.Regle.Origine} | {N(c.Valeurs)} | {N(c.Modifiees)} |");
        sb.AppendLine();

        sb.AppendLine("## Chaînes signées (CA05)").AppendLine();
        if (ChainesInvalidees)
        {
            sb.AppendLine($"La copie porte la marque **« {MotifChaines} »** (table `anonymisation.Copie`, propriété de base `RIGV3_ANONYMISATION`) : aucun test ne doit utiliser ses chaînes comme preuve.").AppendLine();
            sb.AppendLine("| Table signée | Lignes signées dont le contenu a changé |").AppendLine("|---|---:|");
            foreach (var (t, n) in TablesSigneesTouchees) sb.AppendLine($"| {t} | {N(n)} |");
        }
        else sb.AppendLine("Aucune ligne signée n'a changé : la marque porte « chaînes intactes ».");
        sb.AppendLine();

        sb.AppendLine("## À relire (inventaire, SOC-021 T01)").AppendLine();
        sb.AppendLine("Colonnes dont le nom évoque une donnée personnelle mais qui sont GARDÉES (segment technique, ou choix du catalogue), et colonnes traitées sur leur seul nom : à relire par le délégué à la protection des données.").AppendLine();
        foreach (var (c, r) in ARelire) sb.AppendLine($"- `{c}` : {r}");
        foreach (var c in Colonnes.Where(c => c.Regle.Origine.Contains("a relire")))
            sb.AppendLine($"- `{c.Table}.{c.Colonne}` : {c.Regle.Origine}, traitée {c.Regle.Traitement}");
        sb.AppendLine();

        sb.AppendLine("## Forme de la base").AppendLine();
        sb.AppendLine($"- {IndexCrees.Count} index et clés primaires refaits.");
        sb.AppendLine($"- {ClesVerifiees.Count} clés étrangères refaites ET vérifiées sur les données copiées (les liens tiennent) ; {ClesNonVerifiees.Count} posées sans vérification.");
        foreach (var c in ClesNonVerifiees) sb.AppendLine($"  - {c}");
        sb.AppendLine($"- {ModulesCrees.Count} vues, fonctions, procédures et déclencheurs refaits.");
        foreach (var i in Ignores) sb.AppendLine($"- Non refait : {i}");
        sb.AppendLine();

        sb.AppendLine("## Anomalies").AppendLine();
        if (Anomalies.Count == 0) sb.AppendLine("Aucune.");
        foreach (var a in Anomalies) sb.AppendLine($"- {a}");
        return sb.ToString();
    }
}
