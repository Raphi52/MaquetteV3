using System.Globalization;
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
