using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Statistiques » (PROC_STAT, écran fait main web/src/ecrans/EcranStatistiques.tsx) : le geste EXCEL de RIG v2, le classeur du volet sur
/// sa période. RIG v2 le génère SUR LE POSTE (RapportStatMandataire / MetierStats, la maquette du paramètre d'édition du volet) et l'ouvre ;
/// décision du plan (lot 1, plan-f4fb097f) : une édition faite sur le poste est une DEMANDE DANS LA FILE DES ÉDITIONS
/// (EditionsV2.DemandeEditionAsync), ici avec le paramètre d'édition du volet. Joué par la route POST RoutesStatistiques.Excel
/// (ModuleStatistiques.cs) dans la transaction et le journal des gestes du socle (GesteFaitMain). Lot 11 du plan « RIG v3 complet, mesuré
/// par le banc du parcours complet » (28/11/2026) ; le calcul affiché : LectureStatistiques.cs.
///
/// SOURCE (ancien RIG, D:\RigApplication\Source\RIG\DLL\Processus\PROC_STAT) :
///   - le paramètre et le nom du classeur de chaque volet : STATS_INTERNES\Details Stats\OPE_STATISTIQUES_INTERNES.cs:17-18, :32
///     (STAT_INTERNES, « Statistiques internes du jj-mm-aaaa au jj-mm-aaaa ») ; OPE_STATISTIQUES_WEB.cs:17-18, :32 (STAT_WEB) ;
///     OPE_REPARTITIONS_JC_OP.cs:16-17, :31 (STAT_REP_JCOP) ; OPE_OUVERTURE_ACTIVITES_CA.cs:16-17, :31 (STAT_OUV_CA) ;
///     OPE_OUVERTURE_ACTIVITES_SALARIES.cs:16-17, :31 (STAT_OUV_SAL) ; STATS_SEMESTRIELLES\OPE_STATS_SEMESTRIELLES.cs:21, :117
///     (STAT_SEMESTRIELLES, « Stats Semestrielles du … au … ») -- les six paramètres existent sur RIG_DEV (FUSION_RIG, maquettes LOT2_STAT_*,
///     lus le 28/11/2026) ;
///   - les contrôles, au mot près : « La date de début ne peut pas être supérieur à la date de fin » (STATS_INTERNES\Commun\OPE_DATES.cs:44,
///     OPE_STATS_SEMESTRIELLES.cs:96) ; « Impossible de trouver la maquette » quand le paramètre ou sa maquette manque
///     (OPE_STATISTIQUES_BASE._InitMaquette, :50-59 ; OPE_STATS_SEMESTRIELLES.cs:54).
/// NON REPRIS (nommés) : le classeur lui-même (le service d'édition le produit de sa maquette) ; le suffixe « _2 » quand un classeur du même
/// nom existe déjà dans le répertoire commun (:34-37) ; le niveau NAF des volets d'activités, que la V2 passe au calcul, pas à l'édition.
/// TRACE : famille « demande », « a cree » (comme les écrivains qui posent un état de RIG v2 dans la file par EditionsEtatsV2.PoserAsync :
/// EcrivainChequesEmis.cs:96, EcrivainJournalCaisse.cs:96, EcrivainJournalVente.cs:88).
/// </summary>
public static class EcritureStatistiques
{
    /// <summary>Le nom de l'écran au journal des gestes.</summary>
    public const string Ecran = "Statistiques";

    /// <summary>Le code processus de RIG v2 (PROCESSUS_ET_FONCTIONALITE de RIG_DEV : STAT).</summary>
    public const string CodeProcessus = "STAT";

    /// <summary>OPE_DATES.cs:44, au mot près.</summary>
    public const string DatesInversees = "La date de début ne peut pas être supérieur à la date de fin";

    /// <summary>OPE_STATISTIQUES_BASE.cs:56, au mot près.</summary>
    public const string SansMaquette = "Impossible de trouver la maquette";

    /// <summary>Les gestes écrits : tout autre type est refusé par la route (rien d'écrit).</summary>
    public static readonly IReadOnlyCollection<TypeGeste> Gestes = [TypeGeste.Agir];

    /// <summary>Le paramètre d'édition et le nom du classeur de chaque volet (voir l'en-tête).</summary>
    public static readonly IReadOnlyDictionary<string, (string Parametre, string Nom)> Classeurs = new Dictionary<string, (string, string)>
    {
        [VoletsStatistiques.Internes] = ("STAT_INTERNES", "Statistiques internes"),
        [VoletsStatistiques.Web] = ("STAT_WEB", "Statistiques Web"),
        [VoletsStatistiques.RepartitionsJcOp] = ("STAT_REP_JCOP", "Répartitions des JC et OP"),
        [VoletsStatistiques.ActivitesCa] = ("STAT_OUV_CA", "Ouvertures par chiffre d'affaire"),
        [VoletsStatistiques.ActivitesSalaries] = ("STAT_OUV_SAL", "Ouvertures par activité salariés"),
        [VoletsStatistiques.Semestrielles] = ("STAT_SEMESTRIELLES", "Stats Semestrielles"),
    };

    /// <summary>Le classeur du volet et de la période des Valeurs du geste, posé dans la file des éditions.</summary>
    public static async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string V(string cle) => geste.Demande.Valeurs.TryGetValue(cle, out var v) ? v?.Trim() ?? "" : "";
        var volet = V(RoutesStatistiques.ChampVolet);
        if (!Classeurs.TryGetValue(volet, out var classeur))
            return IssueGeste.Refuser($"Volet « {volet} » inconnu : {string.Join(", ", Classeurs.Keys)}.");
        static DateTime? Jour(string t) => DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        if (Jour(V(RoutesStatistiques.ChampDebut)) is not { } debut || Jour(V(RoutesStatistiques.ChampFin)) is not { } fin)
            return IssueGeste.Refuser("Saisissez la date de début et la date de fin.");
        if (debut > fin) return IssueGeste.Refuser(DatesInversees);
        if (await EditionsV2.ParametreAsync(geste, classeur.Parametre, ct) is not { } parametre
            || await EcritureCloture.MaquetteAsync(geste, classeur.Parametre, ct) is not { Length: > 0 } maquette)
            return IssueGeste.Refuser(SansMaquette);

        var libelle = string.Format(CultureInfo.InvariantCulture, "{0} du {1:dd-MM-yyyy} au {2:dd-MM-yyyy}", classeur.Nom, debut, fin);
        var id = await EditionsV2.DemandeEditionAsync(geste, null, parametre, libelle, null, null, null, null, ct,
            new Dictionary<string, object?> { ["DMNED_CODE_MAQUETTE"] = maquette, ["DMNED_CODE_PROCESSUS"] = CodeProcessus });
        return IssueGeste.Ecrit($"Classeur « {libelle} » posé dans la file des éditions.", [EditionsV2.Texte(id)],
            new TraceGeste("demande", $"le classeur « {libelle} »", EditionsV2.Texte(id), "a cree", null,
                new { Volet = volet, Parametre = classeur.Parametre, Maquette = maquette, Du = debut.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                      Au = fin.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
    }
}
