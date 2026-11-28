using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Consultation judiciaire » (web/src/ecrans/EcranConsultationJud.tsx). Plan « RIG v3 100 % fonctionnel »,
/// lot 6 (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les enrolements RPJC de la base du greffe (Serveur/Socle/EcransMain/
/// LectureConsultationJud.cs) : l'affaire est le role de l'instance creee a l'acceptation, ou la reference RPVA de la demande
/// tant qu'il n'y en a pas ; le dossier ouvert au clic est ce role. Le XML se lit a la demande, un enrolement a la fois
/// (« Visualiser le XML » : XmlAsync). Le nombre de pieces et les anomalies ne sont pas lus (null : l'ecran le dit). SANS
/// serveur : la demonstration (Exemple), marquee « exemple ». En panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranConsultationJud
{
    [JSInvokable("EcranConsultationJud.Vue")]
    public static Task<VueConsultationJud> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les enrolements du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueConsultationJud> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les enrôlements dématérialisés", (s, g) =>
            s.LireAsync<EnrolementsLus>(RoutesEcransMain.Pour(RoutesConsultationJud.Enrolements, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueConsultationJud([], lu.Mode, lu.Message);
        return new VueConsultationJud([.. v.Lignes.Select(Ligne)], lu.Mode, null, v.Total);
    }

    /// <summary>Ce que dit « Visualiser le XML » quand la base ne porte pas de flux pour l'enrolement.</summary>
    public const string SansFlux = "Cet enrôlement n'a pas de XML dans la base du greffe (RPJCE_FLUX vide).";

    /// <summary>Ce qu'il dit quand l'enrolement n'est plus dans la base.</summary>
    public const string Disparu = "Cet enrôlement n'est plus dans la base du greffe : relisez la liste.";

    [JSInvokable("EcranConsultationJud.Xml")]
    public static Task<XmlConsultationJud> Xml(string id) => XmlAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id);

    /// <summary>Le XML de l'enrolement <paramref name="id"/> du greffe (« Visualiser le XML », RPJCE_FLUX).</summary>
    public static async Task<XmlConsultationJud> XmlAsync(ClientServeur? serveur, string? greffe, string id)
    {
        if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return new("erreur", null, Disparu);
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "le XML de l'enrôlement", (s, g) =>
            s.LireAsync<FluxEnrolementLu>(RoutesConsultationJud.XmlPour(g, n)));
        if (lu.Valeur is not { } v) return new(lu.Mode, null, lu.Message ?? "Sans serveur : le XML ne se lit pas.");
        return new(lu.Mode, v.Xml, !v.Existe ? Disparu : v.Xml is null ? SansFlux : null);
    }

    /// <summary>
    /// « Saisie de la demande » de RIG v2 (OPE_CJUDINFOG) : traiter l'enrolement <paramref name="id"/> -- <paramref name="etat"/> « R »
    /// (refuse) avec son type et son motif de rejet ; « A » (accepter) est refuse par le serveur, que RIG v3 ne reprend pas
    /// (Serveur/Socle/EcransMain/EcritureConsultationJud.cs). Sans serveur, ou pour un enrolement d'exemple (sans Id) : rien n'est
    /// ecrit, « Démonstration ». Lot 8 du plan « parcours complet », 28/11/2026.
    /// </summary>
    [JSInvokable("EcranConsultationJud.Traiter")]
    public static Task<ResultatGeste> Traiter(string id, string etat, string typeRejet, string motif)
        => TraiterAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, etat, typeRejet, motif);

    public static async Task<ResultatGeste> TraiterAsync(ClientServeur? serveur, string? greffe, string? id, string etat, string typeRejet, string motif)
    {
        if (serveur is null || greffe is null)
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        if (string.IsNullOrEmpty(id))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : un enrôlement d'exemple n'existe pas dans la base du greffe.") { SansEcrivain = true };
        var valeurs = new Dictionary<string, string>
        {
            [GesteEcran.Cle(TraitementEnrolement.Section, TraitementEnrolement.Etat)] = etat,
            [GesteEcran.Cle(TraitementEnrolement.Section, TraitementEnrolement.TypeRejet)] = typeRejet,
            [GesteEcran.Cle(TraitementEnrolement.Section, TraitementEnrolement.MotifRejet)] = motif,
        };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesConsultationJud.Traiter, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [id], valeurs));
    }

    private static string Jour(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static LigneConsultationJud Ligne(EnrolementLu e)
    {
        var affaire = e.Role.Length > 0 ? e.Role : e.Reference.Length > 0 ? e.Reference : $"n° {e.Id}";
        var nom = e.Denomination.Length > 0 ? e.Denomination : e.Demande.Length > 0 ? $"Demande {e.Demande}" : $"Référence RPVA {e.Reference}";
        return new LigneConsultationJud(affaire, nom, e.EtatDemat, e.EtatTraitement, e.TypeRejet, Jour(e.Creation),
            e.Traitement is null ? null : Jour(e.Traitement), NbPieces: null, Anomalies: null, e.MotifRejet, Xml: null, Dossier: e.Role,
            Id: e.Id.ToString(CultureInfo.InvariantCulture));
    }
}
