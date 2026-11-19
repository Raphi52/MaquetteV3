namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran PROC_DEPOTJUD (web/src/ecrans/EcranDepotsJud.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, valeur de depart de la « Date de l'evenement ». Ses etapes
/// et ses instances etaient declarees dans le .razor lui-meme : elles ont
/// suivi l'ecran dans le .tsx (web/README.md, « ce qui passe par le pont »).
/// </summary>
public static class EcranDepotsJud
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranDepotsJud.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
