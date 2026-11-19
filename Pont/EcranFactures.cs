namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran PROC_FACTURE (web/src/ecrans/EcranFactures.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, valeur de depart de la date d'encaissement. Ses fiches
/// clients, ses articles et ses tiers payeurs etaient declares dans le .razor
/// lui-meme : ils ont suivi l'ecran dans le .tsx (voir web/README.md, « ce qui
/// passe par le pont »).
/// </summary>
public static class EcranFactures
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranFactures.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
