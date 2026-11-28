using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Créer une audience » (web/src/ecrans/EcranAudiences.tsx). Plan « RIG v3 100 % fonctionnel », lot 7
/// (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les listes de saisie de la creation (types d'audience, chambres, sections, types de
/// classement de la base du greffe) et les audiences a venir avec leurs affaires au role (Serveur/Socle/EcransMain/
/// LectureAudiences.cs). « Publiée sur le web » = non « non disponible dans les choix d'audience web ». SANS serveur : la
/// demonstration (Exemple), marquee « exemple ». En panne : le motif, aucune ligne ni aucune liste.
///
/// « Créer l'audience » (<see cref="CreerAsync"/>) : la saisie OPE_CREATION_AUDIENCE de RIG v2, ecrite par le serveur
/// (Serveur/Socle/EcransMain/EcritureAudiences.cs, ses controles V2 cites) ; les listes envoient leur CODE (Options). Sans serveur,
/// rien n'est ecrit (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 9, 28/11/2026).
/// </summary>
public static partial class EcranAudiences
{
    [JSInvokable("EcranAudiences.Vue")]
    public static Task<VueAudiences> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>« Créer l'audience » : la saisie de l'ecran (codes des listes, date aaaa-mm-jj, heure HH:MM). Le resultat porte
    /// l'identifiant de l'audience creee (Ids), ou le refus de RIG v2.</summary>
    [JSInvokable("EcranAudiences.Creer")]
    public static Task<ResultatGeste> Creer(SaisieAudience saisie) => CreerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, saisie);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> CreerAsync(ClientServeur? serveur, string? greffe, SaisieAudience s)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCreationAudience.Creer, greffe),
            new GesteEcran(TypeGeste.Creer, [], new Dictionary<string, string>
            {
                [RoutesCreationAudience.ChampDate] = s.Date?.Trim() ?? "",
                [RoutesCreationAudience.ChampHeure] = s.Heure?.Trim() ?? "",
                [RoutesCreationAudience.ChampType] = s.Type?.Trim() ?? "",
                [RoutesCreationAudience.ChampChambre] = s.Chambre?.Trim() ?? "",
                [RoutesCreationAudience.ChampSection] = s.Section?.Trim() ?? "",
                [RoutesCreationAudience.ChampClassements] = string.Join(";", s.Classements ?? []),
                [RoutesCreationAudience.ChampPublique] = s.Publique ? "oui" : "non",
                [RoutesCreationAudience.ChampNonDisponibleWeb] = s.NonDisponibleWeb ? "oui" : "non",
            }));
    }

    /// <summary>Les listes et les audiences a venir du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueAudiences> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les audiences", (s, g) =>
            s.LireAsync<AudiencesProgrammeesLues>(RoutesEcransMain.Pour(RoutesAudiences.Programmees, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueAudiences([], [], [], [], [], lu.Mode, lu.Message);
        return new VueAudiences([.. v.Lignes.Select(a => new LigneAudience(a.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                                    a.Heure, a.Type, a.Chambre, a.Section, a.Publique, !a.NonDisponibleWeb, a.Affaires))],
                                v.Types, v.Chambres, v.Sections, v.Classements, lu.Mode, null, v.Total)
        {
            Options = [.. v.Options.Select(o => new OptionAudienceVue(o.Liste, o.Code, o.Libelle))],
        };
    }
}

/// <summary>Ce que l'ecran saisit pour creer une audience : date aaaa-mm-jj, heure HH:MM, et les CODES du type, de la chambre, de la
/// section et des types de classement admis (VueAudiences.Options).</summary>
public sealed record SaisieAudience(string? Date, string? Heure, string? Type, string? Chambre, string? Section, string[]? Classements,
                                    bool Publique = true, bool NonDisponibleWeb = false);
