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
