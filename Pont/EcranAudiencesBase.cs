using System.Globalization;
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
/// </summary>
public static partial class EcranAudiences
{
    [JSInvokable("EcranAudiences.Vue")]
    public static Task<VueAudiences> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les listes et les audiences a venir du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueAudiences> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les audiences", (s, g) =>
            s.LireAsync<AudiencesProgrammeesLues>(RoutesEcransMain.Pour(RoutesAudiences.Programmees, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueAudiences([], [], [], [], [], lu.Mode, lu.Message);
        return new VueAudiences([.. v.Lignes.Select(a => new LigneAudience(a.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                                    a.Heure, a.Type, a.Chambre, a.Section, a.Publique, !a.NonDisponibleWeb, a.Affaires))],
                                v.Types, v.Chambres, v.Sections, v.Classements, lu.Mode, null, v.Total);
    }
}
