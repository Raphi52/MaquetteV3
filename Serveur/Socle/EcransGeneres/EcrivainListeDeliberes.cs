using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Délibérés » (PROC_LISTE_DELIBERES) : « Editer le résultat » met la liste des delibéres non rendus a l'ecran dans la FILE DES EDITIONS.
/// Mode d'emploi : EcrivainEcran.cs ; la liste : LecteurListeDeliberes.cs (Id = « instance|reference|duree|types »). Plan « RIG v3 complet,
/// mesure par le banc du parcours complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\Judiciaire\Exe\PROC_LISTE_DELIBERES\frmListeDelibere.cs -- un programme sans demande ni
/// ecriture en base :
///   - « Editer le résultat » (EDITER, :51-54 ; tbListeDelib_ButtonClick, :593-597) ouvre un document Word des lignes du resultat
///     (AfficherWord, :263-327), titre « Liste affaires (&lt;types&gt;) en cours au &lt;reference&gt; et en délibéré depuis plus de &lt;n&gt;
///     jours » (:277, TypeInstanceGenererTitreWord :375-396). Le bouton n'est actif qu'avec au moins une ligne (:135-139) : sans ligne, refus ;
///   - « Délibérés repoussés non rendus » (EXCEL, :60-61 ; _GenererExcelDeliberer, :568-590) lance EXECUTE_EXCEL sur sa propre requete
///     (Resources\SQL_DeliberesRepoussesNonRendus.txt, sans rapport avec les lignes affichees) : un classeur sur le poste. RIG v3 ne
///     produit pas de classeur : refuse, dit -- comme « Éditer les détails » du suivi de dematerialisation (EcrivainSuiviDematerialisation.cs).
/// LA V3 : une edition de RIG v3 est une demande dans la file des editions (decision du plan, EditionsV2.SansParametre) -- le document
/// Word de RIG v2 devient une demande d'edition FUSION_ASPOSE au titre de RIG v2, sans demande V2 (RIG v2 n'en cree pas), le titre de la
/// fenetre en libelle source (« Liste des délibérés », :32). NON REPRIS (nommes) : le juge et la date d'enrolement du sous-titre (:278-286 :
/// ils ne voyagent pas avec la ligne) ; le choix des colonnes (lstEdit) et le tableau du document, que la V3 n'engendre pas.
/// Trace : famille « demande », l'edition creee, « a cree ».
/// </summary>
internal sealed class EcrivainListeDeliberes : IEcrivainEcran
{
    public string Procedure => "PROC_LISTE_DELIBERES";
    public string Ecran => "Délibérés";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres (frmListeDelibere.cs:53, :61).</summary>
    internal const string Editer = "Editer le résultat", Repousses = "Délibérés repoussés non rendus";

    internal const string SansLigne = "« Editer le résultat » n'est actif qu'avec au moins un délibéré à l'écran : RIG v2 le grise sans résultat.";
    internal const string ClasseurNonRepris =
        "« Délibérés repoussés non rendus » ouvre un classeur Excel sur le poste (EXECUTE_EXCEL de RIG v2) : RIG v3 ne produit pas de classeur, rien n'est édité.";
    internal const string LigneDisparue = "Une affaire de la liste n'est plus dans la base du greffe : relancez la recherche.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom == Repousses) return IssueGeste.Refuser(ClasseurNonRepris);
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        // Les lignes : « instance|reference|duree|types » (LecteurListeDeliberes.IdLigne), toutes de la meme recherche.
        var lues = geste.Demande.Ids.Select(Lire).ToArray();
        if (lues.Any(l => l is null) || lues.Select(l => (l!.Value.Reference, l.Value.Duree, l.Value.Types)).Distinct().Count() != 1)
            return IssueGeste.Refuser(LigneDisparue);
        var instances = lues.Select(l => l!.Value.Instance).Distinct().ToArray();
        int trouvees;
        // Des entiers relus ci-dessus : la liste IN ne porte aucun texte saisi.
        await using (var r = await geste.LireAsync(
            $"SELECT COUNT(*) FROM v2.INSTANCE WHERE INSTN_ID_INSTN IN ({string.Join(", ", instances.Select(EditionsV2.Texte))})", ct))
            trouvees = await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
        if (trouvees != instances.Length) return IssueGeste.Refuser(LigneDisparue);

        var (_, reference, duree, types) = lues[0]!.Value;
        var titre = $"Liste affaires ({string.Join(",", types.Split(',').Select(LecteurListeDeliberes.LibelleType))}) en cours au " +
                    $"{reference.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} et en délibéré depuis plus de {duree} jours";
        var id = await EditionsV2.DemandeEditionAsync(geste, null, EditionsV2.SansParametre(titre, "FUSION_ASPOSE"), titre, null, null, null, null, ct,
            new Dictionary<string, object?> { ["DMNED_LIBELLE_SOURCE"] = "Liste des délibérés" });
        var n = instances.Length;
        var trace = new TraceGeste("demande", $"l'édition « {titre} »", EditionsV2.Texte(id), "a cree", null,
            new { Document = titre, Affaires = n, File = "file des éditions" });
        return IssueGeste.Ecrit($"« {titre} » ({n} affaire{(n > 1 ? "s" : "")}) : placé dans la file des éditions.", [EditionsV2.Texte(id)], trace);
    }

    private static (int Instance, DateTime Reference, int Duree, string Types)? Lire(string id)
    {
        var parts = id.Split('|');
        return parts.Length == 4
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var instance)
               && DateTime.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reference)
               && int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var duree)
               && parts[3].Length > 0
            ? (instance, reference, duree, parts[3]) : null;
    }
}
