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

/// <summary>Ce que rend la recherche de la palette : le mode (Pont/LectureEcranMain.cs), les annonces trouvees en base et leur
/// total (au-dela du plafond de la palette).</summary>
public sealed record VueRechercheBodacc(string Mode, string? Message, AnnonceBodacc[] Annonces, int Total);

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
        return new VueBodacc(lu.Mode, null, [.. v.Lignes.Select(AlEcran)], v.Total, Iso(v.Au), $"du {v.Du:dd/MM/yyyy} au {v.Au:dd/MM/yyyy}");
    }

    /// <summary>
    /// La recherche de la palette Ctrl K et de la page Recherche (web/src/ecrans/annoncesBodacc.ts ; plan « RIG v3 100 %
    /// fonctionnel », lot 20) : branchee, les annonces de la base dont chaque mot tape se retrouve dans l'annonce, sans la
    /// periode de l'ecran (Serveur/Socle/EcransMain/LectureBodacc.cs, ChercherAsync). SANS serveur : mode « demo », aucune
    /// annonce -- la page cherche alors dans ses annonces d'exemple, qu'elle marque « exemple ». En panne : le motif, aucune.
    /// </summary>
    [JSInvokable("EcranBodacc.Chercher")]
    public static Task<VueRechercheBodacc> Chercher(string texte) => ChercherAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, texte);

    /// <summary>Les annonces du greffe <paramref name="greffe"/> qui repondent a <paramref name="texte"/> (null serveur : « demo »).</summary>
    public static async Task<VueRechercheBodacc> ChercherAsync(ClientServeur? serveur, string? greffe, string texte)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les annonces BODACC", (s, g) =>
            s.EnvoyerAsync<RechercheBodacc, AnnoncesBodaccTrouvees>(RoutesEcransMain.Pour(RoutesBodaccRecherche.Recherche, g),
                new RechercheBodacc(texte ?? "")));
        return lu.Valeur is { } v
            ? new VueRechercheBodacc(lu.Mode, null, [.. v.Lignes.Select(AlEcran)], v.Total)
            : new VueRechercheBodacc(lu.Mode, lu.Message, [], 0);
    }

    /// <summary>Une annonce lue, aux champs de l'ecran (dates aaaa-mm-jj).</summary>
    private static AnnonceBodacc AlEcran(AnnonceBodaccLue a)
        => new(a.NumAnnonce, a.NumPc, a.TypeAnnonce, a.CodeEvePro, Iso(a.DateEvePro) ?? "", Iso(a.PrevueEnvoi) ?? "", Iso(a.Envoi),
               a.NumParution, Iso(a.Parution), a.NumBord, a.NumFact, a.Texte, a.Dossier, Iso(a.Transmission));

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Iso(DateTime? d) => d is { } v ? Iso(v) : null;
}
