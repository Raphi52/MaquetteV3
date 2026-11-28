namespace RigV3Desktop.Pont;

/// <summary>Une audience a venir de l'ecran (« AudienceLigne » dans web/src/ecrans/EcranAudiences.tsx) ; date = aaaa-mm-jj.</summary>
public sealed record LigneAudience(string Date, string Heure, string Type, string Chambre, string Section, bool Publique, bool Web, int NbAffaires);

/// <summary>Ce que l'ecran montre : les audiences a venir, les listes de la creation, d'ou elles viennent (Pont/LectureEcranMain.cs)
/// et le total des audiences.</summary>
public sealed record VueAudiences(LigneAudience[] Lignes, string[] Types, string[] Chambres, string[] Sections, string[] Classements,
                                  string Mode = "demo", string? Message = null, int Total = 0)
{
    /// <summary>Les valeurs des listes avec leur code, celui que « Créer l'audience » envoie (EcranAudiencesBase.cs) ; vide dans la
    /// demonstration, qui n'ecrit rien.</summary>
    public OptionAudienceVue[] Options { get; init; } = [];
}

/// <summary>Une valeur d'une liste de la creation : sa liste (« type », « chambre », « section », « classement »), son code, son libelle.</summary>
public sealed record OptionAudienceVue(string Liste, string Code, string Libelle);

/// <summary>
/// Pont de l'ecran PROC_AUDIENCE (web/src/ecrans/EcranAudiences.tsx).
///
/// L'ecran ne lisait qu'UNE chose dans Metier/ : la date du jour du jeu de
/// demonstration, d'ou il tirait la date proposee pour une audience neuve
/// (une semaine plus tard). Ses types d'audience, ses classements et ses
/// audiences programmees etaient declares dans le .razor lui-meme : ils ont
/// suivi l'ecran dans le .tsx (web/README.md, « ce qui passe par le pont »).
///
/// Lot 7 du plan « RIG v3 100 % fonctionnel » (26/11/2026) : listes et audiences viennent du pont (EcranAudiencesBase.cs, la base
/// du greffe) ; celles-ci sont la DEMONSTRATION, rendue sans serveur et marquee « exemple » a l'ecran.
/// </summary>
public static partial class EcranAudiences
{
    /// <summary>Le jour + 7, au format d'un &lt;input type="date"&gt; (aaaa-mm-jj) : celui du jeu de demonstration sans serveur, le vrai
    /// jour branche a la base -- la date de demonstration (septembre 2026) etait deja passee, et une audience creee a cette date
    /// n'entrait pas dans les audiences a venir que l'ecran relit (lot 9 du parcours complet, 28/11/2026).</summary>
    [JSInvokable("EcranAudiences.DateProposee")]
    public static string DateProposee() =>
        (ClientServeur.Actif is null ? JeuDemo.Aujourdhui : DateTime.Today).AddDays(7)
            .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Les listes et les trois audiences de demonstration (celles que l'ecran declarait), sans serveur.</summary>
    public static VueAudiences Exemple() => new(
    [
        new("2026-09-15", "09:00", "Audience de procedure collective", "1re", "A", true, true, 14),
        new("2026-09-22", "10:15", "Audience du juge-commissaire", "1re", "A", false, false, 6),
        new("2026-09-29", "14:00", "Audience de plaidoirie", "2e", "B", true, true, 9),
    ],
    ["Audience de procedure collective", "Audience de reference", "Audience de plaidoirie", "Audience de conciliation", "Audience du juge-commissaire"],
    ["1re", "2e", "3e"], ["A", "B", "C"],
    ["Insuffisance d'actif", "Extinction du passif", "Conversion en liquidation", "Report"],
    LectureEcransMain.Demo, null, 3);
}
