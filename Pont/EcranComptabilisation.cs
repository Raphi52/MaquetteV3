namespace RigV3Desktop.Pont;

/// <summary>
/// Un nœud d'encaissements d'une journée, tel que l'écran « Comptabilisation » le montre (web/src/ecrans/EcranComptabilisation.tsx,
/// interface Remis) : <paramref name="NumeroRemis"/> 0 = encaissements non remis du mode ; <paramref name="ModePaiement"/> le libellé
/// du mode, <paramref name="TypeMode"/> son type (CH, VI, CB, ES…, la teinte de l'écran) ; montant en CENTIMES entiers (la convention
/// de l'écran) ; date « aaaa-mm-jj ».
/// </summary>
public sealed record RemisCompta(int NumeroRemis, string ModePaiement, string TypeMode, int NbEncaissement, long SommeEncaissement,
                                 string DateEncaissement, string Observation = "");

/// <summary>Une journée à comptabiliser (interface Journee de l'écran) : l'agrégat de ses factures (montant en centimes) et ses remis.</summary>
public sealed record JourneeCompta(string Date, int NbFactures, long MontantTotalTTC, int NbAffairesEnCours, int NbSansDemande, RemisCompta[] Remis);

/// <summary>
/// Ce que l'écran affiche : les journées, leur total, le message bloquant de RIG v2 (factures sans demande) s'il y en a, si le greffe
/// est paramétré pour ne cocher que les encaissements (EXPRESSO_FIGERFACTAUTO), et d'où elles viennent (Pont/LectureEcranMain.cs).
/// </summary>
public sealed record VueComptabilisation(JourneeCompta[] Journees, int Total, string? Anomalie, bool EncaissementsSeuls,
                                         string Mode = "demo", string? Message = null);

/// <summary>
/// Pont de l'écran PROC_COMPTABILISATION (web/src/ecrans/EcranComptabilisation.tsx). Jusqu'au lot 13 du plan « RIG v3 100 %
/// fonctionnel » (27/11/2026), ses trois journées de démonstration étaient écrites dans l'écran, sans pont ; elles vivent ici et ne
/// valent plus que SANS serveur (marquées « exemple » par l'écran). Branché : Pont/EcranComptabilisationBase.cs.
/// </summary>
public static partial class EcranComptabilisation
{
    /// <summary>La démonstration, marquée « exemple » par l'écran ; le libellé du paramètre affiché, comme avant le branchement.</summary>
    public static VueComptabilisation Exemple() => new(
    [
        new("2026-09-09", 14, 381240, 11, 1,
        [
            new(4821, "Cheque", "CH", 9, 214000, "2026-09-09"),
            new(4822, "Virement", "VI", 4, 145040, "2026-09-09"),
            new(0, "Carte bancaire", "CB", 2, 22200, "2026-09-09"),
        ]),
        new("2026-09-08", 6, 120480, 6, 0,
        [
            new(4818, "Cheque", "CH", 5, 98080, "2026-09-08"),
            new(4819, "Especes", "ES", 1, 22400, "2026-09-08"),
        ]),
        new("2026-09-07", 3, 45600, 2, 1, []),
    ], 3, null, true);
}
