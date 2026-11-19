namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran PROC_CLOT (web/src/ecrans/EcranCloture.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, a laquelle il comparait la « Limite Examen » de chaque
/// affaire (ligne en alerte, critere « Limite d'examen depassee »). Ses
/// affaires de demonstration etaient declarees dans le .razor lui-meme : elles
/// ont suivi l'ecran dans le .tsx (web/README.md, « ce qui passe par le pont »).
/// </summary>
public static class EcranCloture
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranCloture.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
