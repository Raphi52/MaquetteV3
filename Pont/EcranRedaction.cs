namespace RigV3Desktop.Pont;

/// <summary>
/// Une décision judiciaire comme l'écran « Rédaction d'une décision » la montre (web/src/ecrans/EcranRedaction.tsx, interface
/// Decision) : <paramref name="Numero"/> est le numéro de rôle de son instance ; <paramref name="Delibere"/> range la décision sous
/// l'onglet « Délibéré » tant qu'elle n'a pas d'appel d'affaire prononcé, sous « Prononcé » ensuite. Avant dire droit,
/// contradictoire et exécution provisoire sont des LISTES dans RIG v2 (ULT_COMBO_*) : leur libellé.
/// </summary>
public sealed record DecisionRedaction(int Id, string Numero, string Affaire, bool Delibere, string TypeDecision, string SensGeneral,
                                       string Ressort, string Date, string DatePrevue, string MiseEnDelibere, string Prononce,
                                       string AvantDireDroit, string Contradictoire, string ExecutionProvisoire, bool DebatsPublics,
                                       bool DecisionPublique, bool MesureInstruction, int Documents, string[] Mesures);

/// <summary>
/// Ce que l'écran affiche : les décisions, d'où elles viennent (Pont/LectureEcranMain.cs) et, branché, combien la base en compte
/// (<paramref name="Total"/>) : la lecture s'arrête au plafond (LectureRedaction.Plafond), l'écran dit alors « n affichées sur
/// total » au lieu de laisser croire que la liste est complète. 0 : la démonstration ou une panne, rien à dire.
/// </summary>
public sealed record VueRedaction(DecisionRedaction[] Decisions, string Mode = "demo", string? Message = null, int Total = 0);

/// <summary>
/// Pont de l'écran PROC_REDA (web/src/ecrans/EcranRedaction.tsx). Jusqu'au lot 10 du plan « RIG v3 100 % fonctionnel »
/// (27/11/2026), ses quatre décisions de démonstration étaient écrites dans l'écran, sans pont ; elles vivent ici et ne
/// valent plus que SANS serveur (marquées « exemple » par l'écran). Branché : Pont/EcranRedactionBase.cs.
/// </summary>
public static partial class EcranRedaction
{
    /// <summary>La démonstration, marquée « exemple » par l'écran.</summary>
    public static VueRedaction Exemple() => new(
    [
        new(1, "2026J00121", "AMITEL SAS - ouverture", true, "Jugement d'ouverture", "Favorable", "Premier ressort",
            "06/03/2026", "05/03/2026", "20/02/2026", "", "Non", "Oui", "Oui", true, true, true, 4,
            ["MES-2026-0114 — expertise comptable", "MES-2026-0121 — inventaire"]),
        new(2, "2026J00134", "SCI DU PARC - designation", true, "Ordonnance de designation", "Favorable", "Premier ressort",
            "02/06/2026", "01/06/2026", "18/05/2026", "", "Oui", "Oui", "Non", false, true, true, 2,
            ["MES-2026-0128 — evaluation immobiliere"]),
        new(3, "2025J00088", "MARTIN EURL - cloture", false, "Jugement de cloture", "Defavorable", "Dernier ressort",
            "20/09/2026", "20/09/2026", "04/09/2026", "20/09/2026", "Non", "Oui", "Non", true, true, false, 0, []),
        new(4, "2024J00507", "DUPONT SARL - copie conforme", false, "Copie conforme", "Sans objet", "Premier ressort",
            "08/11/2024", "08/11/2024", "", "08/11/2024", "Non", "Non", "Non", false, false, false, 1, []),
    ]);
}
