using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3.Passage;

/// <summary>Une ligne du plan du jour J : un greffe, son heure de passage a SON heure locale, la meme en UTC et a Paris.</summary>
public sealed record LignePlan(string Code, string Nom, string Vague, string Fuseau, string Serveur, DateTime PassageLocal, DateTime PassageUtc,
                               DateTime HeureParis);

/// <summary>
/// LE PLAN DU JOUR J (SOC-024 CA01, T01) : les greffes hors beta passent le meme jour, chacun a l'ouverture de SA
/// journee, a son heure locale (fuseau du catalogue, SOC-007). Les greffes beta sont deja passes (SOC-022) : ils
/// sont listes a part, sans heure. Un greffe sans fuseau n'a pas d'heure : il est refuse (il faut le saisir avant).
/// L'heure d'ouverture est celle des fenetres de livraison (Outils\Livraison\sites.json, « ouverture.debut »,
/// SOC-010 T04, hypothese a valider) : une seule valeur pour les deux.
/// </summary>
public static class PlanJourJ
{
    public const string Metropole = "metropole";
    public const string OutreMer = "outre-mer";
    public static readonly TimeOnly OuvertureParDefaut = new(7, 0);

    public sealed record Plan(DateOnly Jour, TimeOnly Ouverture, IReadOnlyList<LignePlan> Passages, IReadOnlyList<FicheGreffe> DejaPasses,
                              IReadOnlyList<string> SansFuseau);

    public static Plan Calculer(IEnumerable<FicheGreffe> fiches, DateOnly jour, TimeOnly ouverture)
    {
        var liste = fiches.ToList();
        var paris = TimeZoneInfo.FindSystemTimeZoneById(RigV3.Serveur.Socle.Greffes.Fuseaux.Metropole);
        var passages = new List<LignePlan>();
        var sansFuseau = new List<string>();
        foreach (var f in liste.Where(f => !f.EstBeta))
        {
            if (f.Fuseau is null) { sansFuseau.Add($"{f.Code} {f.Nom}"); continue; }
            var local = jour.ToDateTime(ouverture, DateTimeKind.Unspecified);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.FindSystemTimeZoneById(f.Fuseau));
            passages.Add(new LignePlan(f.Code, f.Nom, f.EstOutreMer ? OutreMer : Metropole, f.Fuseau, f.Serveur, local, utc,
                                       TimeZoneInfo.ConvertTimeFromUtc(utc, paris)));
        }
        return new Plan(jour, ouverture, [.. passages.OrderBy(p => p.PassageUtc).ThenBy(p => p.Code, StringComparer.Ordinal)],
                        [.. liste.Where(f => f.EstBeta).OrderBy(f => f.Code, StringComparer.Ordinal)], sansFuseau);
    }

    public static string Texte(Plan p)
    {
        var s = new StringBuilder();
        s.AppendLine($"Jour J {p.Jour:yyyy-MM-dd} : {p.Passages.Count} greffe(s) a l'ouverture de leur journee ({p.Ouverture:HH\\:mm} heure locale), " +
                     $"{p.Passages.Count(l => l.Vague == Metropole)} de metropole, {p.Passages.Count(l => l.Vague == OutreMer)} d'outre-mer ; " +
                     $"{p.DejaPasses.Count} beta deja passe(s).");
        s.AppendLine($"  {"greffe",-6} {"nom",-26} {"vague",-10} {"heure locale",-17} {"UTC",-17} {"heure de Paris",-17} fuseau");
        foreach (var l in p.Passages)
            s.AppendLine($"  {l.Code,-6} {Couper(l.Nom, 26),-26} {l.Vague,-10} {l.PassageLocal:yyyy-MM-dd HH:mm}  {l.PassageUtc:yyyy-MM-dd HH:mm}  " +
                         $"{l.HeureParis:yyyy-MM-dd HH:mm}  {l.Fuseau}");
        foreach (var f in p.DejaPasses) s.AppendLine($"  {f.Code,-6} {Couper(f.Nom, 26),-26} beta       deja passe en beta (SOC-022)");
        foreach (var x in p.SansFuseau) s.AppendLine($"  REFUS : {x} n'a pas de fuseau au catalogue : pas d'heure de passage");
        return s.ToString();
    }

    private static string Couper(string s, int n) => s.Length <= n ? s : s[..n];
}

/// <summary>Une repetition generale : chaque greffe du plan passe sur sa copie, dans l'ordre du jour J (SOC-025).</summary>
public sealed class RapportRepetition
{
    public int Numero { get; set; }
    public DateOnly Jour { get; set; }
    public DateTime DebutUtc { get; set; }
    public DateTime FinUtc { get; set; }
    public long DureeMs { get; set; }
    public List<RapportPassage> Bases { get; set; } = [];

    public string EnJson() => JsonSerializer.Serialize(this, RapportPassage.Json);
    public static RapportRepetition Lire(string fichier) => JsonSerializer.Deserialize<RapportRepetition>(File.ReadAllText(fichier), RapportPassage.Json)
        ?? throw new InvalidDataException($"{fichier} : rapport de repetition vide.");
}

/// <summary>
/// LA REPETITION GENERALE (SOC-025 T02) et LE JOUR J (SOC-024 T02) : chaque greffe du plan passe, l'un apres l'autre,
/// dans l'ordre de leurs heures (UTC). Un echec sur un greffe ne touche pas les autres (CA03) : son passage revient
/// a la V2 et le suivant part. Avec <c>AuxHeures</c>, chaque passage attend l'heure de son greffe moins l'avance
/// donnee (derniere repetition, « aux heures du jour J », SOC-025 CA03) ; sinon ils s'enchainent.
/// Une repetition ne s'ecrit que sur des copies (CA06) : une instance de production est refusee avant tout passage.
/// </summary>
public static class MoteurRepetition
{
    /// <summary>Les instances de production des 40 greffes (COMMUN_RIG.dbo.GREFFE lu le 2026-11-20,
    /// refonte-rig\controles\bases-greffes.json) : une repetition n'y ecrit jamais (SOC-025 CA06).</summary>
    public static readonly IReadOnlySet<string> InstancesDeProduction =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"SQL-PROD\PROD", "RIGBD-ANTILLES", "RIGBD-REUNION", "RIGBD-POLYNESIE" };

    public sealed record OptionsRepetition(int Numero, PlanJourJ.Plan Plan, Func<LignePlan, OptionsPassage> Passage)
    {
        /// <summary>« greffe » -> etape qui echoue expres (SOC-024 T06 : echec simule sur un greffe).</summary>
        public IReadOnlyDictionary<string, string> EchecsSimules { get; init; } = new Dictionary<string, string>();
        public bool AuxHeures { get; init; }
        /// <summary>Avec <see cref="AuxHeures"/> : le passage d'un greffe part a son heure de passage moins cette avance, soit
        /// 20:00 la veille, heure locale, pour une ouverture a 07:00 (fenetre de nuit de REC-001 T04, proposee).</summary>
        // fix-ok: 2 h d'avance (premiere ecriture) ne couvraient pas un transfert estime a 4 h 16 - 4 h 44 sur RIG_DEV
        // (docs/reconstruction/recopie.md, « Mesures ») : le passage part au debut de la nuit du greffe.
        public TimeSpan Avance { get; init; } = TimeSpan.FromHours(11);
        /// <summary>Vrai pour une repetition (copies seulement, CA06) ; faux le jour J, sur les vraies bases.</summary>
        public bool EstRepetition { get; init; } = true;
        public Action<string>? Suivre { get; init; }
        /// <summary>Le dossier ou ecrire le rapport de chaque greffe, au fil des etapes (tableau de bord, SOC-024 CA06).</summary>
        public string? DossierRapports { get; init; }
    }

    public static async Task<RapportRepetition> JouerAsync(OptionsRepetition o, CancellationToken ct = default)
    {
        var chrono = Stopwatch.StartNew();
        var r = new RapportRepetition { Numero = o.Numero, Jour = o.Plan.Jour, DebutUtc = DateTime.UtcNow };
        var production = o.Plan.Passages.Where(l => InstancesDeProduction.Contains(l.Serveur)).Select(l => $"{l.Code} ({l.Serveur})").ToList();
        if (o.EstRepetition && production.Count > 0)
            throw new InvalidOperationException("Repetition refusee avant tout passage : une repetition ne s'ecrit que sur des copies, " +
                                                $"sur des instances de test (SOC-025 CA06) ; le catalogue vise la production pour {string.Join(", ", production)}.");
        foreach (var ligne in o.Plan.Passages)
        {
            ct.ThrowIfCancellationRequested();
            if (o.AuxHeures && ligne.PassageUtc - o.Avance - DateTime.UtcNow is { } attente && attente > TimeSpan.Zero)
            {
                o.Suivre?.Invoke($"{ligne.Code} : attente de {attente:hh\\:mm} (passage a {ligne.PassageLocal:HH:mm} heure locale)");
                await Task.Delay(attente, ct);
            }
            var options = o.Passage(ligne) with
            {
                PassageLocal = ligne.PassageLocal,
                EchecSimule = o.EchecsSimules.GetValueOrDefault(ligne.Code),
                FichierRapport = o.DossierRapports is null ? null : Path.Combine(o.DossierRapports, $"passage-{ligne.Code}.json"),
            };
            o.Suivre?.Invoke($"=== {ligne.Code} {ligne.Nom} ({ligne.Vague}, {ligne.PassageLocal:HH:mm} heure locale)");
            r.Bases.Add(await MoteurPassage.PasserAsync(options, ct));
        }
        r.FinUtc = DateTime.UtcNow;
        r.DureeMs = chrono.ElapsedMilliseconds;
        return r;
    }

    /// <summary>Le tableau de bord (SOC-024 CA06) : pour chaque greffe, l'etape atteinte et son issue.</summary>
    public static string Tableau(IEnumerable<RapportPassage> rapports)
    {
        var s = new StringBuilder();
        s.AppendLine($"  {"greffe",-6} {"nom",-26} {"issue",-18} {"derniere etape faite",-20} {"duree",8}  motif");
        foreach (var p in rapports.OrderBy(p => p.PassageUtc ?? DateTime.MaxValue))
            s.AppendLine($"  {p.Greffe,-6} {(p.Nom.Length > 26 ? p.Nom[..26] : p.Nom),-26} {(p.Issue.Length == 0 ? "en cours" : p.Issue),-18} " +
                         $"{p.DerniereEtapeFaite ?? "-",-20} {p.DureeMs / 1000.0,7:0.0}s  {p.Motif}");
        return s.ToString();
    }

    public static string TexteDuree(long ms) => TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
