using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Les entrees de l'ecran « Ouvrir un dossier » (web/src/ecrans/EcranOuvertureDossier.tsx). Plan « RIG v3 100 % fonctionnel »,
/// lot 17 (26/11/2026).
///
/// Branche au serveur, le tableau montre les derniers dossiers RCS de la base du greffe (Serveur/Socle/EcransMain/
/// LectureDossiersOuverts.cs) et le geste « Ouvrir » N'ECRIT PAS : cet ecran n'a pas d'origine RIG v2 (aucun ecran de l'ancienne
/// application ne cree un dossier hors d'une formalite), donc aucune ecriture V2 a reproduire ; il le dit, la fiche saisie
/// reste a l'ecran. SANS serveur, la demonstration (VueExemple, OuvrirExemple) ouvre le dossier dans le registre partage de
/// la demonstration, comme avant, et l'ecran la marque « exemple ».
/// </summary>
public static partial class EcranOuvertureDossier
{
    /// <summary>Le refus du geste quand l'ecran lit la base : rien n'est ecrit, et pourquoi.</summary>
    public const string SansEcritureV2 =
        "Rien n'est enregistré : dans RIG v2, un dossier RCS naît d'une formalité d'immatriculation, pas de cet écran. " +
        "L'ouverture directe n'écrit donc pas dans la base du greffe.";

    [JSInvokable("EcranOuvertureDossier.Vue")]
    public static Task<VueOuverture> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    [JSInvokable("EcranOuvertureDossier.Ouvrir")]
    public static Task<ResultatOuverture> Ouvrir(FicheOuverture fiche) => OuvrirAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, fiche);

    /// <summary>La date proposee et les dossiers du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueOuverture> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les dossiers du greffe", (s, g) =>
            s.LireAsync<DossiersOuvertsLus>(RoutesEcransMain.Pour(RoutesEcransMain.DossiersOuverts, g)));
        if (lu.Mode == LectureEcransMain.Demo) return VueExemple();
        var aujourdhui = DateTime.Today.ToString("yyyy-MM-dd");
        if (lu.Valeur is not { } v) return new VueOuverture(aujourdhui, [], lu.Mode, lu.Message);
        return new VueOuverture(aujourdhui,
            [.. v.Lignes.Select(d => new LigneDossierOuvert(d.Numero, d.Siren, d.Denomination, d.Forme,
                                                           d.Immatriculation?.ToString("dd/MM/yyyy") ?? "", Capital: "", Monnaie: "", Origine: ""))],
            lu.Mode, null, v.Total);
    }

    /// <summary>Le geste « Ouvrir » : la demonstration sans serveur ; avec, un refus qui dit pourquoi, et le tableau de la base.</summary>
    public static async Task<ResultatOuverture> OuvrirAsync(ClientServeur? serveur, string? greffe, FicheOuverture fiche)
    {
        if (serveur is null) return OuvrirExemple(fiche);
        var vue = await VueAsync(serveur, greffe);
        return new ResultatOuverture(vue.Message ?? SansEcritureV2, Bloquant: true, vue.Ouverts);
    }
}
