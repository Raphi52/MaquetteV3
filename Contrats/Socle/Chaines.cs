namespace RigV3.Contrats.Socle;

// SOC-018 : les chaines signees de la V3 (certification NF525), ouvertes au passage du greffe.
// Mode d'emploi : docs/reconstruction/socle-chaines.md.

/// <summary>Les adresses des chaines signees d'un greffe (le greffe dans l'adresse, comme le compteur).</summary>
public static class RoutesChaines
{
    /// <summary>GET : l'etat de la certification et des 7 chaines.</summary>
    public const string Etat = "/socle/greffes/{greffe}/chaines";

    /// <summary>POST : ouvrir les 7 chaines, vides, avec l'evenement 260 (le passage du greffe, CA01). Amitel seul.</summary>
    public const string Ouverture = "/socle/greffes/{greffe}/chaines/ouverture";

    /// <summary>GET : le journal des evenements, filtre par partie (np, p), code, du, au (yyyy-MM-dd, heure du greffe).</summary>
    public const string Journal = "/socle/greffes/{greffe}/chaines/journal";

    /// <summary>GET : le catalogue des 59 codes d'evenement.</summary>
    public const string Codes = "/socle/greffes/{greffe}/chaines/codes";

    /// <summary>GET : les grands totaux, en lecture seule (?chaine=GTF ou GTE).</summary>
    public const string Totaux = "/socle/greffes/{greffe}/chaines/totaux";

    /// <summary>POST : le controle d'integrite et de sequence des 7 chaines et des archives (evenements 90 et 95, CA04).</summary>
    public const string Controle = "/socle/greffes/{greffe}/chaines/controle";

    /// <summary>POST : la cloture d'un jour, d'un mois ou d'un exercice (grands totaux signes ; archive pour l'exercice, CA05).</summary>
    public const string Clotures = "/socle/greffes/{greffe}/chaines/clotures";

    /// <summary>POST : la purge du journal PURGEABLE d'un exercice archive (CA07).</summary>
    public const string Purge = "/socle/greffes/{greffe}/chaines/purge";

    public static string Pour(string route, string greffe) => route.Replace("{greffe}", Uri.EscapeDataString(greffe));
}

/// <summary>Les 7 chaines (C19 du cadrage) : leur code et leur nom.</summary>
public static class ChainesSignees
{
    public const string Factures = "FAC";
    public const string Encaissements = "ENC";
    public const string TotauxFactures = "GTF";
    public const string TotauxEncaissements = "GTE";
    public const string Duplicatas = "DUP";
    public const string JournalNonPurgeable = "JNP";
    public const string JournalPurgeable = "JP";

    /// <summary>Les 7, dans l'ordre de la story (CA01).</summary>
    public static readonly IReadOnlyList<(string Code, string Libelle)> Toutes =
    [
        (Factures, "Factures"),
        (Encaissements, "Encaissements"),
        (TotauxFactures, "Totaux des factures"),
        (TotauxEncaissements, "Totaux des encaissements"),
        (Duplicatas, "Duplicatas"),
        (JournalNonPurgeable, "Journal des événements non purgeables"),
        (JournalPurgeable, "Journal des événements purgeables"),
    ];

    public static string Libelle(string code) => Toutes.FirstOrDefault(c => c.Code == code).Libelle ?? code;
}

/// <summary>Les types de grand total de la V2 (GRAND_TOTAUX_PERIODIQUES_*.GT_TYPE) et les periodes qu'on cloture.</summary>
public static class PeriodesCloture
{
    public const string Jour = "jour";
    public const string Mois = "mois";
    public const string Exercice = "exercice";

    public const string TypeJour = "GTP";
    public const string TypeMois = "GTM";
    public const string TypeExercice = "GTA/E";
}

/// <summary>Une chaine : combien de maillons, le dernier emis.</summary>
public sealed record EtatChaine(string Code, string Libelle, long Maillons, long DerniereSeq, DateTime? DernierMaillonLe);

/// <summary>
/// La certification de la base V3 du greffe. <paramref name="Active"/> vaut vrai des l'ouverture, sans reglage
/// (CA06) ; <paramref name="EnAttente"/> : les evenements ecrits mais pas encore signes.
/// </summary>
public sealed record EtatCertification(
    string Greffe,
    bool Ouverte,
    bool Active,
    DateTime? OuverteLe,
    string? Fuseau,
    string? EmpreinteCle,
    int EnAttente,
    EtatChaine[] Chaines,
    int[] ExercicesArchives);

/// <summary>Un code du catalogue (V2 : CODE_JET).</summary>
public sealed record CodeEvenement(short Code, string Libelle, bool Purgeable, string Exigibilite);

/// <summary>
/// Un evenement du journal. <paramref name="Chaine"/> null et <paramref name="Seq"/> null : ecrit, en attente de sa
/// signature (la partie serveur le signe au plus tard au passage suivant).
/// </summary>
public sealed record EvenementSigne(
    long Id,
    DateTime Instant,
    short Code,
    string Libelle,
    string Detail,
    string Auteur,
    bool Purgeable,
    string? Chaine,
    int? Exercice,
    long? Seq);

/// <summary>Un montant d'un grand total par taux de TVA.</summary>
public sealed record TotalParTaux(decimal Taux, decimal MontantTtc);

/// <summary>Un grand total signe (lecture seule, CA07).</summary>
/// <param name="Chaine">GTF (factures) ou GTE (encaissements).</param>
/// <param name="Type">GTP (jour), GTM (mois), GTA/E (exercice).</param>
public sealed record GrandTotal(
    string Chaine,
    string Type,
    string Periode,
    decimal Total,
    decimal CumulReel,
    decimal CumulAbsolu,
    int NbPieces,
    DateTime CalculeLe,
    string? Information,
    long Seq,
    TotalParTaux[] ParTaux);

/// <summary>Clore un jour, un mois ou un exercice. <paramref name="Date"/> : un jour de la periode, yyyy-MM-dd.</summary>
public sealed record DemandeCloture(string Periode, string Date);

/// <summary>Ce qu'une cloture a ecrit.</summary>
public sealed record ResultatCloture(string Periode, string IdPeriode, GrandTotal[] Totaux, ArchiveProduite? Archive, string Message);

/// <summary>L'archive fiscale d'un exercice, scellee et enregistree par l'evenement 30 du journal non purgeable.</summary>
public sealed record ArchiveProduite(int Exercice, string Fichier, string Empreinte, int NbPieces, long DerniereSeqJnp,
                                     long DerniereSeqJp, long IdEvenement, long SeqEvenement);

/// <summary>Purger le journal purgeable d'un exercice.</summary>
public sealed record DemandePurge(int Exercice);

/// <summary>Ce que la purge a efface, et l'evenement 205 qui l'enregistre.</summary>
public sealed record ResultatPurge(int Exercice, int Purges, long IdEvenement, string Message);

/// <summary>Le bilan d'une chaine au controle.</summary>
/// <param name="Defauts">Les maillons dont la signature ne correspond plus (un evenement 90 chacun).</param>
/// <param name="Ruptures">Les trous de la sequence (un evenement 95 chacun).</param>
public sealed record BilanChaine(string Code, string Libelle, long Maillons, long Valides, string[] Defauts, string[] Ruptures);

/// <summary>
/// Le bilan du controle d'integrite (CA04). <paramref name="ValeurProbante"/> faux sur une copie anonymisee
/// (SOC-021) : ses chaines sont cassees par construction, le controle ne prouve rien et n'ecrit aucun evenement.
/// </summary>
public sealed record BilanIntegrite(
    DateTime Le,
    bool ValeurProbante,
    string? Reserve,
    int SigneesAvant,
    BilanChaine[] Chaines,
    string[] Archives,
    long[] EvenementsEcrits)
{
    public bool Integre => Chaines.All(c => c.Defauts.Length == 0 && c.Ruptures.Length == 0) && Archives.Length == 0;
}
