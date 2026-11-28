using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Ce que l'ecran « Mon accueil » affiche, et d'ou (Pont/LectureEcranMain.cs) : les reglages RIG v2 de l'agent, par cle
/// (ConsoleAccueil_*), seulement ceux qu'il a enregistres ; ses profils RIG v2 ; <paramref name="AgentV2"/> faux s'il n'a pas de
/// compte RIG v2.
/// </summary>
public sealed record VueParametresAccueil(string Mode, string? Message, IReadOnlyDictionary<string, string> Valeurs, bool EstAdministrateur,
                                          bool EstAmitel, bool AgentV2);

/// <summary>
/// L'entree de l'ecran « Mon accueil » (PROC_ACCUEIL_PARAM). Plan « RIG v3 100 % fonctionnel », lot 20 (27/11/2026), sur la marche
/// du lot 17. L'ecran n'avait pas de pont : ses reglages de demonstration sont dans le .tsx.
///
/// Branche au serveur, l'ecran montre les reglages d'accueil que L'AGENT a enregistres dans RIG v2 (PARAMETRE_UTILISATEUR,
/// Serveur/Socle/EcransMain/LectureParametresAccueil.cs), avec les gardes de profil de RIG v2 (animations : administrateur ;
/// molette et date libre : Amitel). « Enregistrer » les ecrit dans PARAMETRE_UTILISATEUR comme RIG v2 a la fermeture de l'onglet
/// (FORM_SKIN_ACCUEIL_PARAM.cs:97-103, :620-648 ; <see cref="EnregistrerAsync"/>, lot 13 du plan « RIG v3 complet ») : ce sont des
/// reglages du poste WinForms de RIG v2, que la V3 n'applique pas a ses ecrans (la reprise des preferences n'en garde que theme,
/// favoris, ecran de demarrage et imprimante : ReprisePreferences.cs:24-28) ; l'ecran le dit
/// (web/src/ecrans/EcranParametresAccueil.tsx, Enregistrement). NON LUS : « plein ecran »
/// (ParamUtilisateurUltSplitter, hors PARAMETRE_UTILISATEUR) et les imprimantes PaperCut (service externe). SANS serveur : mode
/// « demo » (l'ecran garde ses reglages d'exemple, marques). En panne : le motif.
/// </summary>
public static class EcranParametresAccueil
{
    [JSInvokable("EcranParametresAccueil.Reglages")]
    public static Task<VueParametresAccueil> Reglages() => ReglagesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>« Enregistrer mes réglages d'accueil » : les <paramref name="valeurs"/> par cle ConsoleAccueil_*, ecrites dans
    /// PARAMETRE_UTILISATEUR comme RIG v2 a la fermeture de l'onglet (Serveur/Socle/EcransMain/EcritureParametresAccueil.cs, lot 13 du
    /// plan « RIG v3 complet, mesuré par le banc du parcours complet »).</summary>
    [JSInvokable("EcranParametresAccueil.Enregistrer")]
    public static Task<ResultatGeste> Enregistrer(Dictionary<string, string> valeurs)
        => EnregistrerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, valeurs);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration », rien d'ecrit.</summary>
    public static async Task<ResultatGeste> EnregistrerAsync(ClientServeur? serveur, string? greffe, IReadOnlyDictionary<string, string> valeurs)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesParametresAccueil.Enregistrer, greffe),
            new GesteEcran(TypeGeste.Enregistrer, [], valeurs.ToDictionary(v => v.Key, v => v.Value)));
    }

    /// <summary>Les reglages de l'agent de la session dans le greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueParametresAccueil> ReglagesAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "vos réglages d'accueil", (s, g) =>
            s.LireAsync<ParametresAccueilLus>(RoutesEcransMain.Pour(RoutesParametresAccueil.Parametres, g)));
        return lu.Valeur is { } v
            ? new VueParametresAccueil(lu.Mode, null, v.Valeurs, v.EstAdministrateur, v.EstAmitel, v.AgentV2)
            : new VueParametresAccueil(lu.Mode, lu.Message, new Dictionary<string, string>(), false, false, false);
    }
}
