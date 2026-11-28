using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'entree de l'ecran « Dépôts judiciaires » (web/src/ecrans/EcranDepotsJud.tsx). Plan « RIG v3 100 % fonctionnel », lot 5
/// (26/11/2026), sur la marche du lot 17 (Pont/LectureEcranMain.cs).
///
/// Branche au serveur, l'ecran montre les instances de la serie JROP de la base du greffe (Serveur/Socle/EcransMain/
/// LectureDepotsJud.cs) : nom courant, nature, date d'enrolement, dernier evenement survenu. « État » = la demande qui tient
/// l'instance (« En cours ») ou « Libre » ; « À facturer » = aucune facture a un client sur ses demandes (l'etape de
/// facturation de RIG v2 s'affiche alors, PROC_DEPOTJUD.cs:58-64). Le dossier ouvert au clic est l'instance elle-meme, par son
/// numero de role. La chambre n'est pas lue (aucune colonne) : vide. SANS serveur : la demonstration (Exemple), marquee
/// « exemple ». En panne : le motif et aucune ligne.
/// </summary>
public static partial class EcranDepotsJud
{
    [JSInvokable("EcranDepotsJud.Vue")]
    public static Task<VueDepotsJud> Vue() => VueAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les instances du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueDepotsJud> VueAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les instances du greffe", (s, g) =>
            s.LireAsync<InstancesDepotLues>(RoutesEcransMain.Pour(RoutesDepotsJud.Instances, g)));
        if (lu.Mode == LectureEcransMain.Demo) return Exemple();
        if (lu.Valeur is not { } v) return new VueDepotsJud([], lu.Mode, lu.Message);
        return new VueDepotsJud([.. v.Lignes.Select(Ligne)], lu.Mode, null, v.Total);
    }

    /// <summary>
    /// Les codes d'evenement avec depot proposes pour l'instance de role <paramref name="role"/> (la liste de l'etape « Evénement
    /// survenu » de RIG v2, Serveur/Socle/EcransMain/EcritureDepotsJud.cs). Sans serveur : les codes de la demonstration. Lot 8 du plan
    /// « parcours complet », 28/11/2026.
    /// </summary>
    [JSInvokable("EcranDepotsJud.Codes")]
    public static Task<VueCodesDepot> Codes(string role) => CodesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, role);

    public static async Task<VueCodesDepot> CodesAsync(ClientServeur? serveur, string? greffe, string? role)
    {
        if (serveur is not null && string.IsNullOrWhiteSpace(role)) return new VueCodesDepot([], "vide", "Choisissez une instance de la liste.");
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les codes d'événement avec dépôt", (s, g) =>
            s.LireAsync<CodesDepotLus>(RoutesDepotsJud.CodesPour(g, role ?? "")));
        if (lu.Mode == LectureEcransMain.Demo) return CodesExemple;
        return lu.Valeur is { } v ? new VueCodesDepot([.. v.Codes.Select(c => new CodeDepot(c.Code, c.Libelle))], lu.Mode,
                                                      v.Codes.Length > 0 ? null : AucunCode(v.Type), v.Type)
                                  : new VueCodesDepot([], lu.Mode, lu.Message);
    }

    /// <summary>
    /// « Nouvel événement survenu » de RIG v2 : l'evenement <paramref name="code"/> du <paramref name="date"/> (aaaa-mm-jj) et son depot
    /// numerote, sur l'instance <paramref name="role"/>, ecrits par le serveur. Sans serveur, ou pour une instance d'exemple : rien
    /// n'est ecrit, « Démonstration ».
    /// </summary>
    [JSInvokable("EcranDepotsJud.Deposer")]
    public static Task<ResultatGeste> Deposer(string role, string code, string date)
        => EnregistrerAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, role, code, date);

    public static async Task<ResultatGeste> EnregistrerAsync(ClientServeur? serveur, string? greffe, string role, string code, string date)
    {
        if (serveur is null || greffe is null)
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        var valeurs = new Dictionary<string, string>
        {
            [GesteEcran.Cle(DepotEvenement.Section, DepotEvenement.Code)] = code,
            [GesteEcran.Cle(DepotEvenement.Section, DepotEvenement.Date)] = date,
        };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesDepotsJud.Deposer, greffe),
            new GesteEcran(TypeGeste.Creer, [role], valeurs));
    }

    /// <summary>Le motif d'une liste vide : la liste de RIG v2 ne propose que les codes avec depot compatibles avec le type de l'instance.</summary>
    private static string AucunCode(string type) => type.Length == 0
        ? "Cette instance n'est pas une instance de la série JROP de la base du greffe : aucun code proposé."
        : $"Aucun code d'événement avec dépôt n'est compatible avec cette instance (type {type}) dans la base du greffe : RIG v2 n'en proposerait aucun.";

    private static LigneDepotJud Ligne(InstanceDepotLue i)
    {
        var dernier = i.DernierEvenement.Length == 0 ? ""
            : i.DateEvenement is { } d ? $"{i.DernierEvenement} ({d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})" : i.DernierEvenement;
        var tenue = i.DemandeEnCours.Length > 0;
        return new LigneDepotJud(i.Numero, i.Denomination, i.Nature, Chambre: "",
            i.Enrolement?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", dernier,
            tenue ? "En cours" : "Libre", tenue ? "attente" : "ok", i.AFacturer, Dossier: i.Numero);
    }
}
