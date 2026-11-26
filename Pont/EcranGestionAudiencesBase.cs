using System.Globalization;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Gestion des audiences » (web/src/ecrans/EcranGestionAudiences.tsx). Plan « RIG v3 100 % fonctionnel »,
/// lot 7 (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les audiences et cabinets SUPPRIMES de la base du greffe, comme PROC_GESTAUDS
/// (Serveur/Socle/EcransMain/LectureGestionAudiences.cs) : en mode cabinet le juge, le greffier et le code cabinet ; en mode
/// audience la chambre et la section. Le jour se dit en francais (« dddd », OPE_RECHERCHE_GESTAUDS.cs:301) ; « Type de cabinet »
/// est le type du cabinet. SANS serveur : la demonstration (Exemple), marquee « exemple ». En panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranGestionAudiences
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    [JSInvokable("EcranGestionAudiences.Vue")]
    public static Task<VueGestionAudiences> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les audiences et cabinets supprimes du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueGestionAudiences> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les audiences supprimées", (s, g) =>
            s.LireAsync<AudiencesSupprimeesLues>(RoutesEcransMain.Pour(RoutesGestionAudiences.Supprimees, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueGestionAudiences([], lu.Mode, lu.Message);
        return new VueGestionAudiences([.. v.Lignes.Select(Ligne)], lu.Mode, null, v.Total);
    }

    private static LigneGestionAudience Ligne(AudienceSupprimeeLue a)
    {
        string Jour(DateTime? d) => d is { } j ? Francais.TextInfo.ToTitleCase(j.ToString("dddd", Francais)) : "";
        string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        return new LigneGestionAudience(Jour(a.Date), Date(a.Date), a.Heure, a.Type, a.CodeCabinet, a.Cabinet ? a.Type : "",
            a.Juge, a.Section, a.Chambre, a.Greffier, a.TypeClassement, a.Publique, a.AuteurSuppression, Date(a.DateSuppression),
            Date(a.DateDiffusion), a.Cabinet);
    }
}
