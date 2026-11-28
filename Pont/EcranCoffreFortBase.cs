using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// Un depot, aux champs de l'ecran (web/src/ecrans/EcranCoffreFort.tsx, DepotCoffre) ; dates aaaa-mm-jj, null sans date.
/// <paramref name="ATransmettre"/> : faux pour un depot de l'historique de RIG v2 (DEMAT_CFEC : il est parti) ; null en
/// demonstration (l'ecran le deduit de ses exemples).
/// </summary>
public sealed record DepotCoffre(string Code, string Abrege, string Destinataire, string Coffre, string TypeDecision, string Etat,
                                 string Statut, string? EnvoiInfogreffe, string? ArInfogreffe, string? ArDestinataire,
                                 string? LectureDestinataire, string? TraitementRig, string? AvisDepotCoffre, string CodeDepot,
                                 string Erreur, string[] Historique, string NomDocument, bool? ATransmettre);

/// <summary>Ce que l'ecran Coffre-fort affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
public sealed record VueCoffreFort(string Mode, string? Message, DepotCoffre[] Depots, int Total);

/// <summary>
/// L'entree de l'ecran « Coffre-fort » (PROC_COFFRE_FORT). Plan « RIG v3 100 % fonctionnel », lot 20 (27/11/2026), sur la marche
/// du lot 17. L'ecran n'avait pas de pont : ses depots de demonstration sont dans le .tsx.
///
/// Branche au serveur, « Deja transmis » montre l'HISTORIQUE des depots de RIG v2 (DEMAT_CFEC, Serveur/Socle/EcransMain/
/// LectureCoffreFort.cs : requete, colonnes et couleurs d'OPE_RECHERCHE_HISTORIQUE / OPE_RESULTAT_HISTORIQUE citees).
/// « A transmettre » reste VIDE : RIG v2 le compose de quatre requetes (evenements, editions, decisions numerisees) que la V3
/// ne reprend pas ; « Date envoi infogreffe » et l'historique des envois sont dans la base EDI du greffe, absente. Le coffre-fort
/// electronique est un SERVICE EXTERNE, jamais appele. Le geste « Re-transmettre » de RIG v2 (des depots deja transmis) s'ecrit dans la
/// base du greffe par le serveur (<see cref="RelancerTransmissionAsync"/>, Serveur/Socle/EcransMain/EcritureCoffreFort.cs : etat « 2 -
/// Traite par le greffe », que le service du coffre-fort reprend) ; sans serveur, rien n'est ecrit (plan « RIG v3 complet, mesuré par
/// le banc du parcours complet », lot 10, 28/11/2026). SANS serveur : mode « demo », aucun depot (l'ecran garde ses exemples,
/// marques). En panne : le motif et aucun depot.
/// </summary>
public static class EcranCoffreFort
{
    [JSInvokable("EcranCoffreFort.Depots")]
    public static Task<VueCoffreFort> Depots() => DepotsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe);

    /// <summary>Les depots du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueCoffreFort> DepotsAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les dépôts au coffre-fort", (s, g) =>
            s.LireAsync<DepotsCoffreLus>(RoutesEcransMain.Pour(RoutesCoffreFort.Depots, g)));
        if (lu.Valeur is not { } v) return new VueCoffreFort(lu.Mode, lu.Message, [], 0);
        return new VueCoffreFort(lu.Mode, null, [.. v.Lignes.Select(Depot)], v.Total);
    }

    /// <summary>« Re-transmettre » les depots <paramref name="ids"/> (DepotCoffre.Code) de l'historique.</summary>
    [JSInvokable("EcranCoffreFort.RelancerTransmission")]
    public static Task<ResultatGeste> RelancerTransmission(string[] ids)
        => RelancerTransmissionAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, ids);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> RelancerTransmissionAsync(ClientServeur? serveur, string? greffe, string[] ids)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCoffreFort.Retransmission, greffe),
            new GesteEcran(TypeGeste.Agir, [.. (ids ?? []).Select(i => i?.Trim() ?? "")], new Dictionary<string, string>())
            {
                Nom = RoutesCoffreFort.Retransmettre,
            });
    }

    private static DepotCoffre Depot(DepotCoffreLu d)
    {
        // L'etat comme la grille de RIG v2 : code, puis « - libelle » ou « - erreur » (OPE_RESULTAT_HISTORIQUE.cs:310-314).
        var etat = d.Etat + " - " + (d.Erreur.Length > 0 ? d.Erreur : d.LibelleEtat);
        var destinataire = string.Join(" - ", new[] { d.TypeDestinataire, d.AbregeDestinataire }.Where(t => t.Length > 0));
        return new DepotCoffre(d.Id.ToString(CultureInfo.InvariantCulture), d.Entite,
            d.NomDestinataire.Length > 0 ? d.NomDestinataire : destinataire, Coffre: "", TypeDecision: d.Nature, etat, Statut(d),
            EnvoiInfogreffe: null, Iso(d.ReceptionAr), Iso(d.AccuseReception), Iso(d.Lecture), Iso(d.TraitementRig), Iso(d.Depot),
            d.IdentifiantDepot, d.Erreur, [], d.NomDocument, ATransmettre: false);
    }

    /// <summary>
    /// La couleur de la ligne dans RIG v2 (OPE_RESULTAT_HISTORIQUE.cs:356-358), dans son ordre : 8 ou 9 rouge ; 3 ou 4 vert ; puis,
    /// si elle n'est pas rouge, sans date de depot orange (meme un 3 ou 4). Les autres restent sans couleur (« » : point gris).
    /// </summary>
    internal static string Statut(DepotCoffreLu d)
    {
        var code = d.Etat.Length > 0 ? d.Etat[0] : ' ';
        if (code is '8' or '9') return "alerte";
        if (d.Depot is null) return "attente";
        return code is '3' or '4' ? "ok" : "";
    }

    private static string? Iso(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
