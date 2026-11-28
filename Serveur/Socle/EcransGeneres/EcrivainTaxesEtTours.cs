using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Taxes et tours de rôle » (PROC_TAXESETTOURS) : « Éditer » met dans la FILE DES EDITIONS l'etat de chaque groupe affiche -- tours de
/// role, acomptes, taxes. Mode d'emploi : EcrivainEcran.cs ; le tableau : LecteurTaxesEtTours.cs (Id = « T|A|X + mandataire |periode »,
/// EditionsV2.IdSurPeriode). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_TAXESETTOURS\OPE_RECHERCHE.cs, EDITER (:41-82) -- une edition
/// pure, rien n'est ecrit en base (proc_taxesettours.md, Detail OPE) :
///   - les controles de la periode, au mot pres : « Vous devez au moins saisir la date de début » (:46-47), « Vous devez saisir la date de
///     début si vous avez saisir la date de fin » (:48-49), « La date de début doit être inférieure à la date de fin » (:50-51) ; la
///     periode vient de la ligne (la forme Etat n'envoie pas ses parametres) : seule la troisieme peut encore refuser ;
///   - EDITER n'est actif qu'avec au moins une case cochee (UpdateBouttonEditer, :85-90) : sans ligne, refus ;
///   - un etat Crystal par case cochee (GenererRapport, :94-123) : 4D_TOURS.rpt (PlumitifPcTours.cs:165) pour les tours,
///     4D_TAXES_ACOMPTES.rpt (PlumitifPcTaxes.cs:166) pour les acomptes et pour les taxes ; un etat sans resultat ne s'affiche pas, et
///     RIG v2 le dit : « Il n'y a pas de résultat pour l'édition Tours dans la période demandée. » (:61-75, au mot pres) -- ici, quand
///     aucun mandataire des lignes envoyees n'est plus dans la base.
/// LA V3 : RIG v2 affiche l'etat sur le poste ; une edition de RIG v3 est une demande dans la file des editions (decision du plan,
/// EditionsV2.SansParametre) : une demande d'edition FUSION_CRYSTAL_NET (le moteur Crystal .NET de RIG.CRYSTAL.JUDICIAIRE) par etat, sa
/// maquette (le nom du .rpt), son titre « Tours de rôle du … au … », sans demande V2 (RIG v2 n'en cree pas). NON REPRIS (nomme) : les
/// parametres DATE_DEBUT / DATE_FIN / TYPE_EDITION de l'etat (:96-99, la liste de generation que le service lirait).
/// Trace : famille « demande », l'edition creee, « a cree ».
/// </summary>
internal sealed class EcrivainTaxesEtTours : IEcrivainEcran
{
    public string Procedure => "PROC_TAXESETTOURS";
    public string Ecran => "Taxes et tours de rôle";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Editer = "Éditer";

    internal const string SansLigne = "« Éditer » n'est actif qu'avec au moins un état coché (tours, acomptes ou taxes) : RIG v2 le grise sinon.";
    internal const string DebutApresFin = "La date de début doit être inférieure à la date de fin";
    internal const string LigneDisparue = "Une ligne du tableau n'est plus celle de la base : relancez la recherche.";

    /// <summary>Un etat de RIG v2 : la lettre de ses lignes, son mot dans le message de RIG v2 (:63, :68, :73), son titre, son .rpt.</summary>
    private sealed record Etat(char Lettre, string Mot, string Titre, string Maquette);

    private static readonly Etat[] Etats =
    [
        new('T', "Tours", "Tours de rôle", "4D_TOURS"),
        new('A', "Acomptes", "Acomptes", "4D_TAXES_ACOMPTES"),
        new('X', "Taxes", "Taxes", "4D_TAXES_ACOMPTES"),
    ];

    internal static string SansResultat(string mot) => $"Il n'y a pas de résultat pour l'édition {mot} dans la période demandée.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansLigne);
        var lues = geste.Demande.Ids.Select(EditionsV2.LirePeriode).ToArray();
        if (lues.Any(l => l is null || Etats.All(e => e.Lettre != l.Value.Cle[0])) || lues.Select(l => (l!.Value.Debut, l.Value.Fin)).Distinct().Count() != 1)
            return IssueGeste.Refuser(LigneDisparue);
        var (debut, fin) = (lues[0]!.Value.Debut, lues[0]!.Value.Fin);
        if (debut > fin) return IssueGeste.Refuser(DebutApresFin);

        var crees = new List<string>();
        var traces = new List<TraceGeste>();
        var refus = new List<string>();
        var titres = new List<string>();
        foreach (var etat in Etats)
        {
            // « T123-ADJ », « A123 », « X123 » : le mandataire (MANDATAIRE_PC) de la ligne.
            var mandataires = lues.Where(l => l!.Value.Cle[0] == etat.Lettre)
                .Select(l => int.TryParse(l!.Value.Cle[1..].Split('-')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : -1)
                .ToArray();
            if (mandataires.Length == 0) continue;
            if (mandataires.Any(m => m < 0)) return IssueGeste.Refuser(LigneDisparue);
            int presents;
            await using (var r = await geste.LireAsync(
                $"SELECT COUNT(*) FROM v2.MANDATAIRE_PC WHERE MNDPC_ID_MNDPC IN ({string.Join(", ", mandataires.Distinct().Select(EditionsV2.Texte))})", ct))
                presents = await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
            if (presents == 0) { refus.Add(SansResultat(etat.Mot)); continue; }

            var titre = $"{etat.Titre} du {debut.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} au {fin.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            var id = await EditionsV2.DemandeEditionAsync(geste, null, EditionsV2.SansParametre(titre, "FUSION_CRYSTAL_NET"), titre, null, null, null, null, ct,
                new Dictionary<string, object?> { ["DMNED_CODE_MAQUETTE"] = etat.Maquette, ["DMNED_LIBELLE_SOURCE"] = "Taxes et tours de rôle" });
            crees.Add(EditionsV2.Texte(id));
            titres.Add(titre);
            traces.Add(new TraceGeste("demande", $"l'édition « {titre} »", EditionsV2.Texte(id), "a cree", null,
                new { Document = titre, Maquette = etat.Maquette + ".rpt", Lignes = mandataires.Length, File = "file des éditions" }));
        }
        // RIG v2 dit chaque etat sans resultat, et n'affiche que les autres (:76-78).
        if (crees.Count == 0) return IssueGeste.Refuser(string.Join(" ", refus));
        var message = crees.Count == 1
            ? $"« {titres[0]} » : placé dans la file des éditions."
            : $"{crees.Count} états placés dans la file des éditions.";
        if (refus.Count > 0) message += " " + string.Join(" ", refus);
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }
}
