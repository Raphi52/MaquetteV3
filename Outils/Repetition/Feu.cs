using System.Text.Json;

namespace RigV3.Repetition;

/// <param name="BasesAttendues">Les bases a passer ensemble (Q9 du cadrage : 40 ; liste dans refonte-rig\controles\bases-greffes.json).</param>
/// <param name="FenetreHeures">La fenetre de nuit (20 h -> 6 h : 10 h, proposee par REC-001, a valider).</param>
/// <param name="Parallele">Bases transferees en meme temps sur un serveur (a mesurer, REC-008 ; 1 par defaut : en serie).</param>
/// <param name="JourJ">Le jour J fixe (CA04) ; null : non juge.</param>
/// <param name="DelaiJours">Age maximal de la derniere repetition complete au jour J (30 jours, propose, a valider).</param>
public sealed record OptionsFeu(int BasesAttendues = 40, double FenetreHeures = 10, int Parallele = 1, DateOnly? JourJ = null, int DelaiJours = 30);

/// <summary>Le feu de la beta ou du jour J : vert seulement sans aucune raison rouge.</summary>
public sealed record FeuVert(bool Vert, IReadOnlyList<string> Rouges, IReadOnlyList<string> Tenus);

/// <summary>
/// LE FEU VERT DE LA BETA ET DU JOUR J (REC-008 CA03, CA04), juge sur le rapport de la derniere repetition. Vert
/// seulement si :
///  - les bases attendues ont toutes ete repetees jusqu'au bout, sur des copies COMPLETES (une copie reduite mesure le
///    schema entier, pas la duree) ;
///  - elles tiennent dans la fenetre de nuit (chacune, et toutes ensemble au parallelisme dit) ;
///  - chaque rejet a une regle de reprise ecrite (RegleTable.RepriseDesRejets de sa table) ;
///  - aucune table n'est passee par une regle PROVISOIRE (une regle provisoire n'est pas une regle de reprise ecrite) ;
///  - le controle des deux bases ne trouve aucun ecart, lien orphelin ni ligne sans correspondance (inexplique : aucun
///    n'a encore d'explication ecrite) ;
///  - le retard de recopie est mesure (CA02 : une mesure manquante n'est pas un feu vert) ;
///  - jour J fixe : la derniere repetition COMPLETE a moins de <see cref="OptionsFeu.DelaiJours"/> jours.
/// </summary>
public static class Feu
{
    public static FeuVert Juger(RapportRepetition dernier, IEnumerable<RapportRepetition> historique, OptionsFeu o)
    {
        var rouges = new List<string>();
        var tenus = new List<string>();
        void Regle(bool tenu, string vrai, string faux) => (tenu ? tenus : rouges).Add(tenu ? vrai : faux);

        var finies = dernier.Bases.Where(b => b.Etat == "fini").ToList();
        Regle(finies.Count >= o.BasesAttendues && dernier.Bases.All(b => b.Etat == "fini"),
              $"{finies.Count} bases repetees jusqu'au bout sur {o.BasesAttendues} attendues",
              $"{finies.Count} base(s) repetee(s) jusqu'au bout sur {o.BasesAttendues} attendues" +
              (dernier.Bases.Any(b => b.Etat != "fini") ? " ; arretees : " + string.Join(", ", dernier.Bases.Where(b => b.Etat != "fini").Select(b => $"{b.Base} ({b.Etat})")) : ""));

        var reduites = dernier.Bases.Where(b => b.MaxLignes is not null).Select(b => $"{b.Base} ({b.MaxLignes} lignes par table)").ToList();
        Regle(reduites.Count == 0, "copies completes (aucune reduite)",
              $"copie(s) REDUITE(S) : la duree mesuree n'est pas celle de la base : {string.Join(", ", reduites)}");

        var fenetre = TimeSpan.FromHours(o.FenetreHeures);
        var plusLongue = finies.Select(b => b.DureeTransfertMs ?? 0).DefaultIfEmpty(0).Max();
        var ensemble = TimeSpan.FromMilliseconds(finies.Sum(b => b.DureeTransfertMs ?? 0) / (double)Math.Max(1, o.Parallele));
        Regle(TimeSpan.FromMilliseconds(plusLongue) <= fenetre && ensemble <= fenetre,
              $"fenetre de nuit tenue : la plus longue {TimeSpan.FromMilliseconds(plusLongue):hh\\:mm\\:ss}, toutes ensemble {ensemble:hh\\:mm\\:ss} ({o.Parallele} a la fois), fenetre {o.FenetreHeures} h",
              $"fenetre de nuit depassee : la plus longue {TimeSpan.FromMilliseconds(plusLongue):hh\\:mm\\:ss}, toutes ensemble {ensemble:hh\\:mm\\:ss} ({o.Parallele} a la fois), fenetre {o.FenetreHeures} h");

        var sansRegle = finies.SelectMany(b => b.Rejets.Where(r => string.IsNullOrWhiteSpace(r.RepriseDesRejets)).Select(r => $"{b.Base} {r.Table} ({r.Regle}, {r.Lignes} lignes)")).ToList();
        Regle(sansRegle.Count == 0, "chaque rejet a sa regle de reprise ecrite",
              $"rejet(s) sans regle de reprise ecrite : {string.Join(", ", sansRegle)}");

        var provisoires = finies.Where(b => b.Provisoires > 0).Select(b => $"{b.Base} ({b.Provisoires} tables)").ToList();
        Regle(provisoires.Count == 0, "aucune table par une regle provisoire",
              $"tables transferees par une regle PROVISOIRE, sans regle de reprise ecrite : {string.Join(", ", provisoires)}");

        var ecarts = finies.Where(b => (b.Ecarts ?? 0) + (b.Orphelins ?? 0) + (b.SansCorrespondance ?? 0) > 0)
                           .Select(b => $"{b.Base} ({b.Ecarts} ecarts, {b.Orphelins} orphelins, {b.SansCorrespondance} sans correspondance)").ToList();
        Regle(ecarts.Count == 0, "le controle des deux bases ne trouve aucun ecart", $"ecarts du controle sans explication ecrite : {string.Join(", ", ecarts)}");

        var nonMesure = finies.Where(b => b.RetardMaxRecopieMs is null).Select(b => b.Base).ToList();
        Regle(nonMesure.Count == 0, $"retard de recopie mesure (au plus {finies.Select(b => b.RetardMaxRecopieMs ?? 0).DefaultIfEmpty(0).Max()} ms)",
              $"retard de recopie non mesure : {string.Join(", ", nonMesure)}");

        if (o.JourJ is { } jourJ)
        {
            var complete = historique.Append(dernier)
                .Where(r => r.Bases.Count(b => b.Etat == "fini") >= o.BasesAttendues && r.Bases.All(b => b.Etat == "fini"))
                .Where(r => DateOnly.FromDateTime(r.Debut) <= jourJ)
                .OrderByDescending(r => r.Debut).FirstOrDefault();
            if (complete is null)
                rouges.Add($"jour J {jourJ:yyyy-MM-dd} : aucune repetition complete des {o.BasesAttendues} bases avant lui");
            else
            {
                var age = jourJ.DayNumber - DateOnly.FromDateTime(complete.Debut).DayNumber;
                Regle(age < o.DelaiJours,
                      $"jour J {jourJ:yyyy-MM-dd} : derniere repetition complete le {complete.Debut:yyyy-MM-dd}, {age} jour(s) avant (moins de {o.DelaiJours})",
                      $"jour J {jourJ:yyyy-MM-dd} : derniere repetition complete le {complete.Debut:yyyy-MM-dd}, {age} jours avant ({o.DelaiJours} au plus) : la refaire");
            }
        }
        return new FeuVert(rouges.Count == 0, rouges, tenus);
    }

    /// <summary>Les rapports de repetition d'un dossier (repetition-*.json), du plus ancien au plus recent.</summary>
    public static IReadOnlyList<RapportRepetition> Lire(string dossier)
        => [.. Directory.EnumerateFiles(dossier, "repetition-*.json")
                .Select(f => JsonSerializer.Deserialize<RapportRepetition>(File.ReadAllText(f), RapportRepetition.Json)!)
                .OrderBy(r => r.Debut)];

    public static string Texte(FeuVert f)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine(f.Vert ? "FEU VERT" : "FEU ROUGE");
        foreach (var r in f.Rouges) s.AppendLine($"  ROUGE : {r}");
        foreach (var t in f.Tenus) s.AppendLine($"  tenu  : {t}");
        return s.ToString();
    }
}
