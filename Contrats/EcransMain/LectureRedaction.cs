namespace RigV3.Contrats.EcransMain;

// « Rédaction d'une décision » (PROC_REDA, web/src/ecrans/EcranRedaction.tsx), écran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 10, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureRedaction.cs.

/// <summary>L'adresse de la lecture : GET, les décisions judiciaires à rédiger (<see cref="DecisionsRedactionLues"/>).</summary>
public static class RoutesRedaction
{
    public const string Decisions = "/ecrans-main/{greffe}/redaction";
}

/// <summary>
/// Une décision judiciaire comme OPE_DECISION_JUDICIAIRE de RIG v2 la montre : son instance (numéro de rôle, nom courant), les
/// libellés de ses listes (type, sens général, ressort, avant dire droit, contradictoire, exécution provisoire), ses dates, le
/// titre des appels d'affaire délibéré et prononcé (vide : aucun), ses deux cases (débats publics, décision publique), si son
/// type porte une mesure d'instruction, le nombre de documents numérisés en GED et les mesures qui lui sont liées.
/// </summary>
public sealed record DecisionRedactionLue(int Id, string Role, string Affaire, string TypeDecision, string SensGeneral, string Ressort,
                                          DateTime? Date, DateTime? DatePrevue, string MiseEnDelibere, string Prononce,
                                          string AvantDireDroit, string Contradictoire, string ExecutionProvisoire,
                                          bool DebatsPublics, bool DecisionPublique, bool MesureInstruction, int Documents, string[] Mesures);

/// <summary>Les décisions lues (au plus le plafond), dans l'ordre de RIG v2, et leur total.</summary>
public sealed record DecisionsRedactionLues(DecisionRedactionLue[] Lignes, int Total);

/// <summary>
/// Le geste « valider la décision » de PROC_REDA (OPE_DECISION_JUDICIAIRE, bouton Valider : POST <see cref="Valider"/>, GesteEcran Ids =
/// [l'id de la decision], valeurs sous les titres de RIG v2 de l'operation « Décision judiciaire », OPE_DECISION_JUDICIAIRE.Designer.cs:165-297 ;
/// un champ absent garde la valeur de la base). Dates aaaa-mm-jj (vide : aucune), cases « 1 » / « 0 ». Ecrit par
/// Serveur/Socle/EcransMain/EcritureRedaction.cs (lot 8 du plan « parcours complet », 28/11/2026).
/// </summary>
/// <summary>Une valeur d'une liste de la decision : sa valeur et son libelle.</summary>
public sealed record ValeurListeLue(string Valeur, string Libelle);

/// <summary>Une liste de la decision : le titre de son champ (<see cref="ValidationDecision"/>), obligatoire ou non, et ses valeurs.</summary>
public sealed record ListeDecisionLue(string Champ, bool Obligatoire, ValeurListeLue[] Valeurs);

public static class ValidationDecision
{
    public const string Valider = "/ecrans-main/{greffe}/redaction/valider";

    /// <summary>GET, les valeurs des cinq listes de la decision (<see cref="ListeDecisionLue"/>[], une par champ, dans l'ordre de la fiche).</summary>
    public const string Listes = "/ecrans-main/{greffe}/redaction/listes";

    public const string Section = "Décision judiciaire";
    public const string SensGeneral = "Sens général";
    public const string Ressort = "Ressort";
    public const string AvantDireDroit = "Avant dire droit";
    public const string Contradictoire = "Contradictoire";
    public const string ExecutionProvisoire = "Exécution provisoire";
    public const string DatePrevue = "Date prévue";
    public const string Date = "Date";
    public const string DebatsPublics = "Débats publics";
    public const string DecisionPublique = "Décision publique";
}
