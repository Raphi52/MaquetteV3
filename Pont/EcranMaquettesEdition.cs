using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Un paramètre d'édition, tel que le panneau « Parametres d'edition » de web/src/ecrans/EcranMaquettesEdition.tsx le remplit
/// (les champs d'OPE_PARAM_EDT_MAJ de RIG v2) ; le nombre d'exemplaires en texte, vide s'il n'est pas renseigné.
/// </summary>
public sealed record ParametreEditionVue(string Code, string Libelle, string Processus, string Nature, string Imprimante, string TypePapier,
    string Lot, string LotLrar, string LotIlrar, string LotSubstitution, string NbExemplaires, bool RectoVerso, bool Agrafage,
    bool AfficherAvantImpression, bool DocNonModifiable, bool Impression, bool GarderEditionLot1, string TypeGeneration, string RequeteSql);

/// <summary>
/// Une maquette, telle que l'affiche l'écran (Maquette) : « Ecran d'origine » = les codes processus de ses paramètres d'édition,
/// « Type de document » = le format de son document (MQTT_EXTENSION), « Type de maquette » = la colonne Type de la liste de RIG v2
/// (MQTT_TYPE_MAQUETTE), aucune date (MAQUETTE n'en porte pas), ses tables liées (« TYPE · code », sans doublon) et ses paramètres.
/// </summary>
public sealed record MaquetteVue(string Code, string Libelle, string Process, string TypeDoc, string TypeMaquette, string Maj,
    string[] Tables, bool EntetePlieuse, ParametreEditionVue[] Parametres);

/// <summary>Ce que l'écran affiche, et d'où viennent les maquettes (Pont/LectureEcranMain.cs). Mode « demo » : aucune maquette, l'écran
/// garde sa démonstration, marquée « exemple ». <see cref="AgentAmitel"/> : RIG v2 n'ouvre qu'à Amitel « Supprimer » un paramètre
/// d'édition et la case « Garder edition lot 1 » (MaquettesLues.AgentAmitel).</summary>
public sealed record VueMaquettes(MaquetteVue[] Maquettes, int Total, string Mode, string? Message)
{
    public bool AgentAmitel { get; init; }
}

/// <summary>
/// L'entrée de l'écran « Edition des maquettes » (PROC_EDIT_MAQUETTE, web/src/ecrans/EcranMaquettesEdition.tsx). Plan « RIG v3 100 %
/// fonctionnel », lot 15 (27/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs). L'écran ne lisait rien dans Metier/ : sa
/// démonstration vit dans le .tsx.
///
/// Branché au serveur, l'écran montre les maquettes du greffe (Serveur/Socle/EcransMain/LectureMaquettesEdition.cs : MAQUETTE, ses
/// paramètres d'édition et leurs tables liées). SANS serveur : aucune maquette, mode « demo » -- la démonstration reste celle de
/// l'écran, marquée « exemple ». En panne : le motif et aucune maquette. Les gestes d'un PARAMÈTRE d'édition, Enregistrer et Supprimer,
/// s'écrivent dans la base du greffe (<see cref="Parametre"/>, Serveur/Socle/EcransMain/EcritureParametresEdition.cs) ; ceux d'une
/// MAQUETTE (Modifier, Dupliquer, Supprimer, Valider) passent par son document Word en GED dans RIG v2 et ne sont pas repris.
/// </summary>
public static class EcranMaquettesEdition
{
    [JSInvokable("EcranMaquettesEdition.Maquettes")]
    public static Task<VueMaquettes> Maquettes() => MaquettesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les maquettes du greffe <paramref name="greffe"/> (null serveur : la démonstration de l'écran).</summary>
    public static async Task<VueMaquettes> MaquettesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les maquettes", (s, g) =>
            s.LireAsync<MaquettesLues>(RoutesEcransMain.Pour(RoutesMaquettesEdition.Maquettes, g)));
        if (lu.Valeur is not { } v) return new VueMaquettes([], 0, lu.Mode, lu.Message);
        return new VueMaquettes([.. v.Lignes.Select(Vue)], v.Total, lu.Mode, null) { AgentAmitel = v.AgentAmitel };
    }

    /// <summary>
    /// Un geste sur le paramètre d'édition <paramref name="code"/> : Enregistrer ses <paramref name="valeurs"/> changées (clés = les titres
    /// des champs de RIG v2, EcritureParametresEdition.Champ*) ou Supprimer. Sans serveur : rien n'est écrit, « Démonstration ».
    /// </summary>
    [JSInvokable("EcranMaquettesEdition.Parametre")]
    public static Task<ResultatGeste> Parametre(TypeGeste type, string code, Dictionary<string, string> valeurs)
        => ParametreAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, new GesteEcran(type, [code], valeurs));

    public static async Task<ResultatGeste> ParametreAsync(ClientServeur? serveur, string? greffe, GesteEcran geste)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        if (geste.Ids.Length != 1 || string.IsNullOrWhiteSpace(geste.Ids[0]))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : un paramètre d'exemple n'existe pas dans la base du greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesMaquettesEdition.Parametre, greffe), geste);
    }

    private static MaquetteVue Vue(MaquetteLue m) => new(m.Code, m.Libelle,
        string.Join(", ", m.Parametres.Select(p => p.CodeProcessus).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)),
        m.Extension, m.Type, "",
        [.. m.Parametres.SelectMany(p => p.TablesLiees).Select(t => $"{t.Type} · {t.Code}").Distinct(StringComparer.Ordinal)],
        m.EnteteFormatPlieuse, [.. m.Parametres.Select(Vue)]);

    private static ParametreEditionVue Vue(ParametreEditionLu p) => new(p.Code, p.Libelle, p.CodeProcessus, p.NatureEdition, p.Imprimante,
        p.TypePapier, p.Lot, p.LotLrar, p.LotIlrar, p.LotSubstitution,
        p.NombreExemplaires?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "", p.RectoVerso, p.Agrafage,
        p.AfficherAvantImpression, p.LectureSeule, p.Impression, p.GarderEditionLot1, p.TypeGeneration, p.RequeteSql);
}
