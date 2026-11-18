using Microsoft.JSInterop;

namespace RigV3Desktop.Pont;

/// <summary>
/// Pont de l'ecran BODACC (web/src/ecrans/EcranBodacc.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, qui dit si une annonce a envoyer est EN RETARD (date prevue
/// d'envoi depassee). Ses annonces et ses filtres etaient declares dans le
/// .razor lui-meme : ils ont suivi l'ecran dans le .tsx (web/README.md,
/// « ce qui passe par le pont »).
/// </summary>
public static class EcranBodacc
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranBodacc.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
