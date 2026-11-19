namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran PROC_AUDIENCE (web/src/ecrans/EcranAudiences.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, d'ou il tirait la date proposee pour une audience neuve
/// (une semaine plus tard). Ses types d'audience, ses classements et ses
/// audiences programmees etaient declares dans le .razor lui-meme : ils ont
/// suivi l'ecran dans le .tsx (web/README.md, « ce qui passe par le pont »).
/// </summary>
public static class EcranAudiences
{
    /// <summary>JeuDemo.Aujourdhui + 7 jours, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranAudiences.DateProposee")]
    public static string DateProposee() =>
        JeuDemo.Aujourdhui.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
