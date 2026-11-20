namespace RigV3Desktop.Pont;

/// <summary>La demande RCS d'une affaire, ce qu'une facture en reprend : son numero et
/// l'abrege de son client (colonnes Numero et Client de JeuDemo.Demandes).</summary>
public sealed record DemandeFacturee(string Numero, string Client);

/// <summary>
/// Pont de l'ecran PROC_FACTURE (web/src/ecrans/EcranFactures.tsx).
///
/// L'ecran lit deux choses dans Metier/ : la date du jour du jeu de
/// demonstration, valeur de depart de la date d'encaissement, et -- ouvert
/// depuis la page d'une affaire -- la demande de cette affaire. Ses fiches
/// clients, son tarif et ses tiers payeurs etaient declares dans le .razor
/// lui-meme : ils ont suivi l'ecran dans le .tsx (voir web/README.md, « ce qui
/// passe par le pont »).
/// </summary>
public static class EcranFactures
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranFactures.DateDuJour")]
    public static string DateDuJour() =>
        JeuDemo.Aujourdhui.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// La demande de l'affaire <paramref name="dossier"/>, null si elle n'en a pas (une
    /// affaire purement judiciaire, un dossier de test) : la facture ouverte pour elle
    /// reprend son numero et son client au lieu de ceux de la facture de demonstration.
    /// Meme recherche que l'ouverture d'une affaire par son numero (DossierDeNumero,
    /// Pont/PlanTravail.cs) : la premiere demande portee par ce dossier.
    /// </summary>
    [JSInvokable("EcranFactures.DemandeDe")]
    public static DemandeFacturee? DemandeDe(string dossier)
        => JeuDemo.Demandes.FirstOrDefault(d => d.Dossier == dossier) is { } d
            ? new DemandeFacturee(d.Numero, d.Client)
            : null;
}
