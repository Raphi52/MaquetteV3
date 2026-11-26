using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Une annonce, aux champs de l'ecran (web/src/ecrans/EcranBodacc.tsx, Annonce) : dates au format aaaa-mm-jj, null
/// quand elles manquent.</summary>
public sealed record AnnonceBodacc(string NumAnnonce, string NumPc, string TypeAnnonce, string CodeEvePro, string DateEvePro,
                                   string PrevueEnvoi, string? Envoi, string NumParution, string? Parution, string NumBord,
                                   string NumFact, string Texte, string Dossier, string? Transmission);

/// <summary>Ce que l'ecran BODACC affiche.</summary>
/// <param name="Mode">« serveur » (annonces de la base du greffe), « demo » (sans serveur : l'ecran garde ses annonces
/// d'exemple, marquees « exemple »), ou « sans-greffe », « refuse », « erreur » avec leur <paramref name="Message"/>.</param>
/// <param name="Aujourdhui">La date du jour (aaaa-mm-jj) qui dit si une annonce a envoyer est en retard : la vraie en base,
/// celle du jeu de demonstration sinon.</param>
/// <param name="Periode">La periode de recherche de RIG v2 (« du 01/01/2026 au 26/11/2026 ») ; vide en demonstration.</param>
public sealed record VueBodacc(string Mode, string? Message, AnnonceBodacc[] Annonces, int Total, string Aujourdhui, string Periode);

/// <summary>
/// Pont de l'ecran BODACC (web/src/ecrans/EcranBodacc.tsx).
///
/// Branche au serveur (plan « RIG v3 100 % fonctionnel », lot 17, 26/11/2026), l'ecran montre les annonces de la base du
/// greffe de la session : celles de la periode de recherche de RIG v2, lues par Serveur/Socle/EcransMain/LectureBodacc.cs
/// (requete, colonnes et partage « a envoyer / deja envoye » de PROC_ENV_BOD cites) ; ses criteres les filtrent sur place,
/// comme avant. SANS serveur, il garde ses annonces d'exemple, declarees dans le .tsx, et les marque « exemple » ; la date
/// du jour est alors celle du jeu de demonstration, qui dit si une annonce a envoyer est EN RETARD (date prevue depassee).
/// </summary>
public static class EcranBodacc
{
    /// <summary>JeuDemo.Aujourdhui, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj).</summary>
    [JSInvokable("EcranBodacc.DateDuJour")]
    public static string DateDuJour() => Iso(JeuDemo.Aujourdhui);

    [JSInvokable("EcranBodacc.Annonces")]
    public static Task<VueBodacc> Annonces() => AnnoncesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les annonces du greffe <paramref name="greffe"/> lues sur <paramref name="serveur"/> (null : la demonstration).</summary>
    public static async Task<VueBodacc> AnnoncesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les annonces BODACC", (s, g) =>
            s.LireAsync<AnnoncesBodaccLues>(RoutesEcransMain.Pour(RoutesEcransMain.Bodacc, g)));
        if (lu.Mode == LectureEcransMain.Demo) return new VueBodacc(lu.Mode, null, [], 0, DateDuJour(), "");
        if (lu.Valeur is not { } v) return new VueBodacc(lu.Mode, lu.Message, [], 0, Iso(DateTime.Today), "");
        return new VueBodacc(lu.Mode, null,
            [.. v.Lignes.Select(a => new AnnonceBodacc(a.NumAnnonce, a.NumPc, a.TypeAnnonce, a.CodeEvePro, Iso(a.DateEvePro) ?? "",
                                                       Iso(a.PrevueEnvoi) ?? "", Iso(a.Envoi), a.NumParution, Iso(a.Parution), a.NumBord,
                                                       a.NumFact, a.Texte, a.Dossier, Iso(a.Transmission)))],
            v.Total, Iso(v.Au), $"du {v.Du:dd/MM/yyyy} au {v.Au:dd/MM/yyyy}");
    }

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Iso(DateTime? d) => d is { } v ? Iso(v) : null;
}
