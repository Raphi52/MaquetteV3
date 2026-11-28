using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Une ligne du catalogue des courriers divers : les colonnes que l'ecran affiche
/// (v2 : OPE_RECHERCHE_COURRIER_DIVERS.cs, colonnes COUJU_*) ; <paramref name="Id"/> vide pour un modele d'exemple.</summary>
public sealed record CourrierDiversVue(string Code, string Libelle, string TypeEntite, string ParamEdition,
                                       bool AvecDestinataire, bool Actif, string Id = "");

/// <summary>Un parametre d'edition que la fiche propose pour un type d'entite (PRMED_CODE_PROCESSUS EDIT_*).</summary>
public sealed record ParamEditionVue(string Code, string Libelle, string TypeEntite);

/// <summary>Le catalogue : les types d'entite du filtre, tous les modeles, actifs ou non, les parametres d'edition de la fiche, d'ou
/// ils viennent (Pont/LectureEcranMain.cs) et le motif d'une panne.</summary>
public sealed record CatalogueCourriersVue(string[] Types, CourrierDiversVue[] Courriers, ParamEditionVue[] ParamsEdition,
                                           string Mode = "demo", string? Message = null);

/// <summary>
/// Pont de l'ecran PROC_COURRIER_DIVERS (web/src/ecrans/EcranCourriersDivers.tsx) : le CATALOGUE des modeles, ouvert par l'accueil.
///
/// Lot 8 du plan « RIG v3 complet, mesure par le banc du parcours complet » (28/11/2026) : branche au serveur, le catalogue est celui de
/// la base du greffe (COURRIER_JUDICIAIRE, Serveur/Socle/EcransMain/LectureCourriersDivers.cs, qui cite RIG v2), et la fiche ecrit
/// ses gestes -- valider (creer ou modifier) et supprimer, comme OPE_EDITION_COURRIER_DIVERS -- par le serveur
/// (EcritureCourriersDivers.cs). SANS serveur : la demonstration, la liste de Metier/CourriersDivers.cs (celle que la page du dossier
/// propose encore), marquee « exemple » ; en panne : le motif et aucun modele.
/// </summary>
public static class EcranCourriersDivers
{
    [JSInvokable("EcranCourriersDivers.Catalogue")]
    public static Task<CatalogueCourriersVue> Catalogue() => CatalogueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Le catalogue du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<CatalogueCourriersVue> CatalogueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les courriers divers", (s, g) =>
            s.LireAsync<CourriersDiversLus>(RoutesEcransMain.Pour(RoutesCourriersDivers.Catalogue, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new CatalogueCourriersVue(CourriersDivers.TypesEntite, [], [], lu.Mode, lu.Message);
        return new CatalogueCourriersVue(CourriersDivers.TypesEntite,
            [.. v.Courriers.Select(c => new CourrierDiversVue(c.Code, c.Libelle, c.TypeEntite, c.ParamEdition, c.AvecDestinataire, c.Actif,
                                                              c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)))],
            [.. v.ParamsEdition.Select(p => new ParamEditionVue(p.Code, p.Libelle, p.TypeEntite))], lu.Mode);
    }

    /// <summary>La demonstration : la liste de Metier/CourriersDivers.cs, sans serveur.</summary>
    public static CatalogueCourriersVue Exemple()
        => new(CourriersDivers.TypesEntite,
               [.. CourriersDivers.Modeles.Select(m => new CourrierDiversVue(m.Code, m.Libelle, m.TypeEntite, m.ParamEdition, m.AvecDestinataire, m.Actif))],
               [.. CourriersDivers.Modeles.Select(m => new ParamEditionVue(m.ParamEdition, m.ParamEdition, m.TypeEntite)).Distinct()],
               LectureEcransMain.Demo);

    /// <summary>
    /// « Valider » de la fiche (OPE_EDITION_COURRIER_DIVERS.Valider) : cree le modele (<paramref name="id"/> vide) ou enregistre ses champs
    /// changes. <paramref name="champs"/> porte, sous leurs titres de RIG v2 (SaisieCourrierDivers : « Libellé », « Type entité »...), les
    /// champs saisis ; cases « 1 » / « 0 ». Sans serveur : rien n'est ecrit, « Démonstration ».
    /// </summary>
    [JSInvokable("EcranCourriersDivers.Enregistrer")]
    public static Task<ResultatGeste> Enregistrer(string? id, Dictionary<string, string> champs)
        => EnregistrerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id, champs);

    public static Task<ResultatGeste> EnregistrerAsync(ClientServeur? serveur, string? greffe, string? id, IReadOnlyDictionary<string, string> champs)
        => EnvoyerAsync(serveur, greffe, string.IsNullOrEmpty(id) ? new GesteEcran(TypeGeste.Creer, [],
                            champs.ToDictionary(c => GesteEcran.Cle(SaisieCourrierDivers.Section, c.Key), c => c.Value))
                        : new GesteEcran(TypeGeste.Enregistrer, [id], champs.ToDictionary(c => GesteEcran.Cle(SaisieCourrierDivers.Section, c.Key), c => c.Value)));

    /// <summary>« Supprimer » de la fiche (OPE_EDITION_COURRIER_DIVERS.Supprimer). Sans serveur : rien n'est ecrit, « Démonstration ».</summary>
    [JSInvokable("EcranCourriersDivers.Supprimer")]
    public static Task<ResultatGeste> Supprimer(string id) => SupprimerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, id);

    public static Task<ResultatGeste> SupprimerAsync(ClientServeur? serveur, string? greffe, string id)
        => EnvoyerAsync(serveur, greffe, new GesteEcran(TypeGeste.Supprimer, [id], new Dictionary<string, string>()));

    private static async Task<ResultatGeste> EnvoyerAsync(ClientServeur? serveur, string? greffe, GesteEcran geste)
    {
        if (serveur is null || greffe is null)
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        if (geste.Type != TypeGeste.Creer && geste.Ids is not [{ Length: > 0 }])
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : un courrier divers d'exemple n'existe pas dans la base du greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCourriersDivers.Geste, greffe), geste);
    }
}
