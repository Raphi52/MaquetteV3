namespace RigV3Desktop.Pont;

/// <summary>Une instance de l'ecran (les champs de « Instance » dans web/src/ecrans/EcranDepotsJud.tsx) ; ouverture = aaaa-mm-jj.</summary>
public sealed record LigneDepotJud(string Numero, string Denomination, string Nature, string Chambre, string Ouverture,
                                   string DernierEvenement, string Etat, string Statut, bool AFacturer, string Dossier);

/// <summary>Ce que l'ecran montre : ses instances, d'ou elles viennent (Pont/LectureEcranMain.cs) et leur total.</summary>
public sealed record VueDepotsJud(LigneDepotJud[] Lignes, string Mode = "demo", string? Message = null, int Total = 0);

/// <summary>
/// Pont de l'ecran PROC_DEPOTJUD (web/src/ecrans/EcranDepotsJud.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, valeur de depart de la « Date de l'evenement ». Ses etapes
/// et ses instances etaient declarees dans le .razor lui-meme : elles ont
/// suivi l'ecran dans le .tsx (web/README.md, « ce qui passe par le pont »).
///
/// Lot 5 du plan « RIG v3 100 % fonctionnel » (26/11/2026) : les instances viennent du pont (EcranDepotsJudBase.cs, la base
/// du greffe) ; ces quatre-la sont la DEMONSTRATION, rendue sans serveur et marquee « exemple » a l'ecran.
/// </summary>
public static partial class EcranDepotsJud
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranDepotsJud.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Les instances de demonstration (celles que l'ecran declarait), sans serveur.</summary>
    public static VueDepotsJud Exemple() => new(
    [
        new("2026J00121", "SARL BOULANGERIE DUVAL", "Redressement judiciaire", "1re", "2026-03-04", "Jugement d'ouverture", "En cours", "action", true, "2024B00318"),
        new("2026J00134", "SAS TRANSPORTS MARNE", "Liquidation judiciaire", "2e", "2026-05-19", "Designation d'expert", "En cours", "attente", false, "2024B00407"),
        new("2025J00088", "EURL COIFFURE MARIE", "Sauvegarde", "1re", "2025-11-02", "Ordonnance", "Suspendue", "alerte", true, "2023B01120"),
        new("2024J00507", "SCI DU MOULIN", "Redressement judiciaire", "3e", "2024-06-12", "Cloture", "Close", "ok", false, "2025B00044"),
    ], LectureEcransMain.Demo, null, 4);
}
