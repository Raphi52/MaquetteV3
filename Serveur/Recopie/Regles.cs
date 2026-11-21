using System.Reflection;

namespace RigV3.Serveur.Recopie;

// ============================================================
// LES REGLES DU TRANSFERT, TABLE PAR TABLE (REC-001 T03).
//
// Chaque table d'une base V2 a UNE regle, declaree par la story qui en repond :
//   - « telle quelle » : la table est recopiee dans le schema v2 de la base V3 (memes colonnes, memes types, memes
//     valeurs, meme cle primaire) ; c'est la regle d'attente tant que la story du domaine n'a pas ecrit la sienne ;
//   - « reprise »      : une reprise deja ecrite (phase 1 : comptes et droits, reglages, compteurs) la porte dans les
//     tables de la V3 ; le transfert l'APPELLE, il ne la refait pas (IRepriseTransfert).
// Le moteur refuse de demarrer si une table lue n'a pas de regle, ou si une table attendue manque (CA01).
//
// Comme les routes (IModuleServeur), les regles sont TROUVEES SEULES : un lot ajoute sa classe (IReglesTransfert ou
// IRepriseTransfert) et aucun fichier commun. Une table declaree deux fois arrete le transfert en nommant les deux
// classes : changer la regle d'une table, c'est modifier sa declaration, pas en ajouter une seconde.
// Mode d'emploi : docs/reconstruction/recopie.md.
// ============================================================

public enum SorteRegle
{
    TelleQuelle,
    Reprise,
}

/// <summary>Ce que le transfert fait d'une ligne dont un lien vise une ligne absente de la V2 (lien casse herite, C5).
/// Dans les deux cas il ne s'arrete pas et n'invente aucune valeur (CA04).</summary>
public enum SiLienCasse
{
    /// <summary>La ligne est ecrite, la valeur du lien gardee telle quelle, le lien note dans rec.LienCasse.</summary>
    Marquer,
    /// <summary>La ligne n'est pas ecrite : elle va dans rec.Rejet avec la cle et la valeur en motif.</summary>
    Rejeter,
}

/// <summary>
/// La regle d'une table V2.
/// </summary>
/// <param name="Table">Nom de la table dans la base V2 : nom seul pour dbo (« DEMANDE »), « schema.NOM » sinon.</param>
/// <param name="Story">La story qui en repond (et qui ecrira sa regle colonne par colonne, s'il y a lieu).</param>
/// <param name="Colonnes">Les colonnes attendues, relevees dans la base de reference (RIG_DEV) par
/// « RigV3.Transfert inventaire » : une colonne absente ou en plus est imprimee (CA01), la copie telle quelle suit
/// toujours le catalogue reel de la base.</param>
/// <param name="Facultative">La table peut manquer dans une base (la reprise qui la lit le prevoit).</param>
/// <param name="Reprise">Pour une regle « reprise » : le <see cref="IRepriseTransfert.Nom"/> qui la porte.</param>
public sealed record RegleTable(string Table, SorteRegle Sorte, string Story, IReadOnlyList<string> Colonnes,
                                bool Facultative = false, SiLienCasse SiLienCasse = SiLienCasse.Marquer, string? Reprise = null)
{
    public static RegleTable TelleQuelle(string table, string story, params string[] colonnes)
        => new(table, SorteRegle.TelleQuelle, story, colonnes);

    /// <summary>
    /// La regle PROVISOIRE d'une table qui n'a pas encore la sienne (REC-008, decision du chef lots 7 et 8) : « telle
    /// quelle », colonnes du catalogue de la base, MARQUEE comme telle au rapport (« provisoire ») et comptee. Elle ne se
    /// declare jamais dans le code : seul <see cref="ReglesProvisoires.Completer"/> la fabrique, a la demande expresse de
    /// qui repete (option --provisoires de l'outil). Le feu vert du jour J la refuse (REC-008 CA03).
    /// </summary>
    public static RegleTable PourRepetition(string table, IEnumerable<string> colonnes)
        => new(table, SorteRegle.TelleQuelle, ReglesProvisoires.Story, [.. colonnes]) { Provisoire = true };

    /// <summary>Une regle fabriquee pour une repetition, non ecrite par une story (<see cref="PourRepetition"/>).</summary>
    public bool Provisoire { get; init; }

    /// <summary>
    /// Ce que deviennent les lignes REJETEES de la table (SiLienCasse = Rejeter) : la regle de reprise ecrite par le
    /// referent du domaine (« ressaisies par le greffe », « reprises par REC-009 »...). Le feu vert d'une repetition
    /// exige qu'un rejet ait la sienne (REC-008 CA03) ; null : aucune n'est ecrite.
    /// </summary>
    public string? RepriseDesRejets { get; init; }

    public string Libelle => Sorte == SorteRegle.TelleQuelle
        ? $"{(Provisoire ? "PROVISOIRE, " : "")}telle quelle ({Story}, lien casse : {(SiLienCasse == SiLienCasse.Marquer ? "marquer" : "rejeter")})"
        : $"reprise « {Reprise} » ({Story})";
}

/// <summary>
/// LES REGLES PROVISOIRES D'UNE REPETITION (REC-008 ; decision du chef, lots 7 et 8). RIG_DEV a 833 tables sans regle :
/// le transfert refuse une base entiere (REC-001 T03) tant que leur story n'a pas declare la sienne. Pour repeter et
/// chronometrer une base ENTIERE avant les stories de domaine, l'outil fabrique, a la demande, une regle « telle quelle »
/// provisoire pour chaque table du catalogue qui n'en a pas. Elle est marquee (« provisoire » au rapport, PROVISOIRE au
/// journal), comptee (rec.Transfert.Provisoires) et le feu vert la refuse ; une story de domaine la remplace en declarant
/// la regle de sa table, sans rien retirer ici.
/// </summary>
public static class ReglesProvisoires
{
    public const string Story = "REC-008";

    /// <summary>Les regles du serveur, completees d'une regle provisoire pour chaque table du catalogue qui n'en a pas.</summary>
    public static IReadOnlyList<RegleTable> Completer(IReadOnlyList<RegleTable> regles, CatalogueV2 catalogue)
    {
        var connues = new HashSet<string>(regles.Select(r => r.Table), StringComparer.OrdinalIgnoreCase);
        return [.. regles, .. catalogue.Tables.Values.Where(t => !connues.Contains(t.Nom)).OrderBy(t => t.Nom, StringComparer.Ordinal)
                                 .Select(t => RegleTable.PourRepetition(t.Nom, t.Colonnes.Select(c => c.Nom)))];
    }
}

/// <summary>Une classe qui declare des regles « telle quelle » (constructeur sans parametre, trouvee seule).</summary>
public interface IReglesTransfert
{
    IEnumerable<RegleTable> Regles { get; }
}

/// <summary>
/// Une reprise deja ecrite, appelee par le transfert (constructeur sans parametre, trouvee seule). Elle declare les
/// tables qu'elle porte (ses <see cref="RegleTable"/>, de sorte <see cref="SorteRegle.Reprise"/>) ; une table qu'elle
/// lit sans la porter (CATEGORIE pour les droits) garde sa propre regle.
/// </summary>
public interface IRepriseTransfert
{
    /// <summary>Nom court, cite par les regles et le journal (« comptes et droits »).</summary>
    string Nom { get; }
    string Story { get; }
    /// <summary>Ordre d'appel ; <see cref="ApresLesTables"/> la place apres les tables copiees telles quelles.</summary>
    int Rang { get; }
    bool ApresLesTables { get; }
    IEnumerable<RegleTable> Regles { get; }

    /// <summary>Appelle la reprise. Rend <see cref="BilanRepriseTransfert.DejaFaite"/> si la base V3 l'a deja (elle n'est
    /// alors pas rejouee) ; un ecart rendu dans <see cref="BilanRepriseTransfert.Ecarts"/> arrete le transfert.</summary>
    Task<BilanRepriseTransfert> ExecuterAsync(ContexteReprise contexte, CancellationToken ct);
}

/// <summary>Ce qu'une reprise recoit : l'instantane (la V2 figee, en lecture seule), la base V2 vivante (par son nom,
/// sur la meme instance : num.Reprendre la bloque le temps de sa copie), la base V3.</summary>
public sealed class ContexteReprise(string connexionInstantane, string connexionV3, string baseV2, int idTransfert)
{
    public string ConnexionInstantane { get; } = connexionInstantane;
    public string ConnexionV3 { get; } = connexionV3;
    public string BaseV2 { get; } = baseV2;
    public int IdTransfert { get; } = idTransfert;
}

/// <summary>Une table portee une ligne pour une ligne par la reprise : le moteur compte ses lignes dans l'instantane,
/// ecrit sa correspondance (<paramref name="SelectionV2V3"/> : un SELECT de deux colonnes, identifiant V2 puis
/// identifiant V3, en texte) et exige lues = ecrites (CA03).</summary>
public sealed record TablePortee(string TableV2, string TableV3, string SelectionV2V3);

public sealed record BilanRepriseTransfert(bool DejaFaite, string Detail, IReadOnlyList<TablePortee> Tables, IReadOnlyList<string> Ecarts);

/// <summary>La decouverte des regles et des reprises du serveur, et le refus des doublons.</summary>
public static class ReglesTransfert
{
    public static (IReadOnlyList<RegleTable> Regles, IReadOnlyList<IRepriseTransfert> Reprises) Decouvrir()
        => Decouvrir(typeof(ReglesTransfert).Assembly);

    public static (IReadOnlyList<RegleTable> Regles, IReadOnlyList<IRepriseTransfert> Reprises) Decouvrir(Assembly assemblage)
    {
        var types = assemblage.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        var reprises = types.Where(t => typeof(IRepriseTransfert).IsAssignableFrom(t))
            .Select(t => (IRepriseTransfert)Activator.CreateInstance(t)!).ToList();
        var sources = new List<(string Source, RegleTable Regle)>();
        foreach (var r in reprises)
            sources.AddRange(r.Regles.Select(g => (r.GetType().Name, g with { Sorte = SorteRegle.Reprise, Reprise = r.Nom })));
        foreach (var t in types.Where(t => typeof(IReglesTransfert).IsAssignableFrom(t) && !typeof(IRepriseTransfert).IsAssignableFrom(t)))
            sources.AddRange(((IReglesTransfert)Activator.CreateInstance(t)!).Regles.Select(g => (t.Name, g)));

        var doublons = sources.GroupBy(s => s.Regle.Table, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (doublons.Count > 0)
            throw new InvalidOperationException("Regles de transfert en double : " + string.Join(" ; ",
                doublons.Select(g => $"{g.Key} declaree par {string.Join(" et ", g.Select(s => s.Source))}")) +
                ". Une table a une seule regle : modifier la declaration existante.");
        var noms = reprises.GroupBy(r => r.Nom, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (noms.Count > 0)
            throw new InvalidOperationException("Reprises de meme nom : " + string.Join(", ", noms));
        return (sources.Select(s => s.Regle).ToList(), reprises.OrderBy(r => r.Rang).ToList());
    }
}
