using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Un document de la GED judiciaire, tel que l'affiche web/src/ecrans/EcranGedJud.tsx (DocGed) : les dates en aaaa-mm-jj.
/// Decision = le libelle du document ; Instance = le numero de l'entite qui le porte ; DateDecision / DatePrevue = celles de la
/// premiere decision liee ; NbLies = les decisions liees (VIDIGED_DJ) ; Statut = la teinte de son etat.
/// </summary>
public sealed record DocumentGedVue(string Id, string Decision, string Minute, string Instance, string TypeEntite, string Nature,
    string? DateDecision, string DatePrevue, int NbLies, string Etat, string CodeEtat, string Statut, string Dossier,
    string? Reception, string Commentaire, string MotifRejet, string[] Historiques);

/// <summary>Ce que l'ecran affiche, et d'ou viennent les lignes (Pont/LectureEcranMain.cs). Mode « demo » : aucune ligne, l'ecran
/// garde sa demonstration, marquee « exemple ».</summary>
public sealed record VueGedJud(DocumentGedVue[] Lignes, int Total, string Mode, string? Message);

/// <summary>Un etat de la liste « Etat » du document (code, libelle).</summary>
public sealed record EtatGed(string Code, string Libelle);

/// <summary>Les etats proposes, d'ou ils viennent et le motif d'une panne.</summary>
public sealed record VueEtatsGed(EtatGed[] Etats, string Mode, string? Message);

/// <summary>
/// L'entree de l'ecran « GED judiciaire » (web/src/ecrans/EcranGedJud.tsx). Plan « RIG v3 100 % fonctionnel », lot 11
/// (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les documents de la GED judiciaire du greffe (Serveur/Socle/EcransMain/LectureGedJud.cs :
/// VIDIGED_INDEX, la recherche de OPE_RECHERCHE_DOC de RIG v2). SANS serveur : aucune ligne, mode « demo » -- la demonstration
/// reste celle de l'ecran (ses documents etaient deja dans EcranGedJud.tsx, sans pont), marquee « exemple ». En panne : le
/// motif et aucune ligne.
/// L'etat a la teinte de RIG v2 : un rejete en rouge (OPE_RESULTAT_TREEVIEW.cs:60-61), en attente en orange, valide en vert.
/// L'historique (PROC_GED_HISTORIQUE, VIDIGED_HISTO) n'est pas relu : la liste est vide.
/// </summary>
public static class EcranGedJud
{
    [JSInvokable("EcranGedJud.Documents")]
    public static Task<VueGedJud> Documents() => DocumentsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les documents du greffe <paramref name="greffe"/> (null serveur : la demonstration de l'ecran).</summary>
    public static async Task<VueGedJud> DocumentsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les documents de la GED judiciaire", (s, g) =>
            s.LireAsync<DocumentsGedLus>(RoutesEcransMain.Pour(RoutesGedJud.Documents, g)));
        if (lu.Valeur is not { } v) return new VueGedJud([], 0, lu.Mode, lu.Message);
        return new VueGedJud([.. v.Lignes.Select(Vue)], v.Total, lu.Mode, null);
    }

    /// <summary>
    /// « Modifier le document » de RIG v2 (PROC_GED_MODIF) : l'etat <paramref name="etat"/> (code de la liste ETAT), le motif de rejet
    /// (exige pour « R ») et le commentaire du document <paramref name="id"/>, ecrits par le serveur avec la trace VIDIGED_HISTO
    /// (Serveur/Socle/EcransMain/EcritureGedJud.cs). Sans serveur : rien n'est ecrit, « Démonstration ». Lot 8 du plan « parcours
    /// complet », 28/11/2026.
    /// </summary>
    [JSInvokable("EcranGedJud.Modifier")]
    public static Task<ResultatGeste> Modifier(string id, string etat, string motif, string commentaire)
        => ModifierAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, etat, motif, commentaire);

    public static async Task<ResultatGeste> ModifierAsync(ClientServeur? serveur, string? greffe, string id, string etat, string motif, string commentaire)
    {
        if (serveur is null || greffe is null)
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        var valeurs = new Dictionary<string, string>
        {
            [GesteEcran.Cle(ModificationDocumentGed.SectionEtat, ModificationDocumentGed.Etat)] = etat,
            [GesteEcran.Cle(ModificationDocumentGed.SectionEtat, ModificationDocumentGed.MotifRejet)] = motif,
            [GesteEcran.Cle(ModificationDocumentGed.SectionCommentaire, ModificationDocumentGed.Commentaire)] = commentaire,
        };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(ModificationDocumentGed.Modifier, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [id], valeurs));
    }

    /// <summary>Les etats de la liste « Etat » de « Modifier le document » (VIDIGED_PARAMETRAGE, type ETAT). Sans serveur : aucun (la
    /// demonstration n'a pas de geste).</summary>
    [JSInvokable("EcranGedJud.Etats")]
    public static Task<VueEtatsGed> Etats() => EtatsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    public static async Task<VueEtatsGed> EtatsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les états des documents", (s, g) =>
            s.LireAsync<EtatGedLu[]>(RoutesEcransMain.Pour(ModificationDocumentGed.Etats, g)));
        return new VueEtatsGed([.. (lu.Valeur ?? []).Select(e => new EtatGed(e.Code, e.Libelle))], lu.Mode, lu.Message);
    }

    /// <summary>La teinte d'un etat de VIDIGED_INDEX (V, VR, A, R) ; un autre code reste gris.</summary>
    internal static string Statut(string codeEtat) => codeEtat.Trim().ToUpperInvariant() switch
    {
        "V" or "VR" => "ok",
        "A" => "attente",
        "R" => "alerte",
        _ => "",
    };

    private static string? Jour(DateTime? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DocumentGedVue Vue(DocumentGedLu d) => new(
        d.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), d.Libelle.Length > 0 ? d.Libelle : "(sans libellé)", "",
        d.Entite, d.TypeEntite, d.Nature, Jour(d.DateDecision), Jour(d.DatePrevue) ?? "", d.DecisionsLiees, d.Etat, d.CodeEtat,
        Statut(d.CodeEtat), d.Entite, Jour(d.Reception), d.Commentaire, d.MotifRejet, []);
}
