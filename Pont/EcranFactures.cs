using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>La demande RCS d'une affaire, ce qu'une facture en reprend : son numero et l'abrege de son client (colonnes Numero et
/// Client de JeuDemo.Demandes) ; branche sur la base (lot 12), aussi le nom, l'adresse et le solde DU du client de sa facture (le
/// solde de RIG v2 change de signe : positif = ce que le client doit, la convention de l'ecran), et d'ou vient la demande.</summary>
public sealed record DemandeFacturee(string Numero, string Client, string? Nom = null, string? Adresse = null, decimal? Solde = null,
                                     string Mode = LectureEcransMain.Demo, string? Message = null);

/// <summary>
/// Pont de l'ecran PROC_FACTURE (web/src/ecrans/EcranFactures.tsx).
///
/// L'ecran lit deux choses : la date du jour, valeur de depart de la date d'encaissement, et -- ouvert depuis la page d'une affaire --
/// la demande de cette affaire. Ses fiches clients, son tarif et ses tiers payeurs etaient declares dans le .razor lui-meme : ils
/// l'ont suivi dans le .tsx (voir web/README.md, « ce qui passe par le pont »).
///
/// BRANCHE SUR LA BASE (plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026 ; marche du lot 17, Pont/LectureEcranMain.cs) : avec
/// un serveur, la date du jour est celle du poste (RIG v2 : DateTime.Today), et la demande du dossier se lit dans la base du greffe
/// (Serveur/Socle/EcransMain/LectureFactures.cs : la premiere demande du dossier et le client de sa facture). Sans serveur, la
/// demonstration (JeuDemo), inchangee. En panne : une demande vide qui porte le motif, jamais celle de la demonstration.
/// </summary>
public static class EcranFactures
{
    /// <summary>La date du jour au format d'un &lt;input type="date"&gt; (aaaa-mm-jj) : celle du poste avec un serveur, sinon
    /// JeuDemo.Aujourdhui.</summary>
    [JSInvokable("EcranFactures.DateDuJour")]
    public static string DateDuJour() => DateDuJour(ClientServeur.Actif, DateTime.Today);

    /// <summary>La date de depart : <paramref name="aujourdhui"/> avec un serveur, la date de la demonstration sans.</summary>
    public static string DateDuJour(ClientServeur? serveur, DateTime aujourdhui) =>
        (serveur is null ? JeuDemo.Aujourdhui : aujourdhui).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// La demande de l'affaire <paramref name="dossier"/>, null si elle n'en a pas (une affaire purement judiciaire, un dossier de
    /// test) : la facture ouverte pour elle reprend son numero et son client au lieu de ceux de la facture de demonstration. Sans
    /// serveur, meme recherche que l'ouverture d'une affaire par son numero (DossierDeNumero, Pont/PlanTravail.cs) : la premiere
    /// demande portee par ce dossier.
    /// </summary>
    [JSInvokable("EcranFactures.DemandeDe")]
    public static Task<DemandeFacturee?> DemandeDe(string dossier) => DemandeDeAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, dossier);

    /// <summary>La demande du dossier <paramref name="dossier"/> du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<DemandeFacturee?> DemandeDeAsync(ClientServeur? serveur, string? greffe, string dossier)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "la demande du dossier", (s, g) =>
            s.LireAsync<DemandeDuDossierLue>(RoutesEcransMain.Pour(RoutesFactures.Demande, g).Replace("{dossier}", Uri.EscapeDataString(dossier.Trim()), StringComparison.Ordinal)));
        if (lu.Mode == LectureEcransMain.Demo)
            return JeuDemo.Demandes.FirstOrDefault(d => d.Dossier == dossier) is { } d ? new DemandeFacturee(d.Numero, d.Client) : null;
        if (lu.Valeur is not { } v) return new DemandeFacturee("", "", Mode: lu.Mode, Message: lu.Message);
        return v.Demande is { } x
            ? new DemandeFacturee(x.Numero, x.Abrege, x.Nom, x.Adresse, x.Solde is { } s ? -s : null, lu.Mode)
            : null;
    }
}
