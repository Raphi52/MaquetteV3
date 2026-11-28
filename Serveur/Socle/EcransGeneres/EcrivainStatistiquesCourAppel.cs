using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Statistiques cour d'appel » (PROC_STATCA) : « Word » met le tableau de bord de l'activite commerciale affiche dans la FILE DES
/// EDITIONS. Mode d'emploi : EcrivainEcran.cs ; le tableau : LecteurStatistiquesCourAppel.cs (Id = « rang de la categorie|periode »,
/// EditionsV2.IdSurPeriode). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_STATCA\ -- rien n'est ecrit en base :
///   - « Word » (WORD, OPE_RECHERCHE.cs:73-75 -> GenererWord, :97-105 -> OPE_GENERER.PrintWord, :610 et suivantes) ecrit un document
///     Word sur le poste, titre « Tableau de bord de l'activité commerciale / &lt;ville&gt; / du &lt;debut&gt; au &lt;fin&gt; »
///     (_TitreDoc, OPE_GENERER.cs:590), les quatre compteurs de procedures collectives et le tableau des quatre categories. Le bouton n'est
///     actif qu'apres la generation du tableau (EnableWord(true), OPE_GENERER.cs:588) : sans ligne, refus ;
///   - les controles de la periode (LoadData, OPE_RECHERCHE.cs:81-92), au mot pres : « La date de début doit être inférieure à la date de
///     fin », « La date de début et la date de fin doivent être inférieure à la date du jour » -- la periode vient de la ligne (la forme
///     Etat n'envoie pas ses parametres).
/// LA V3 : une edition de RIG v3 est une demande dans la file des editions (decision du plan, EditionsV2.SansParametre) : une demande
/// d'edition FUSION_ASPOSE au titre de RIG v2, le titre de l'etape en libelle source (« Activité commerciale du … au … »,
/// OPE_GENERER.cs:586), sans demande V2 (aucune demande STATCA dans RIG_DEV, lu le 28/11/2026). NON REPRIS (nommes) : la ville du titre
/// (ParametresEdition.VilleMin du greffe, que la V3 ne lit pas ici) ; le contenu du document, que la V3 n'engendre pas.
/// Trace : famille « demande », l'edition creee, « a cree ».
/// </summary>
internal sealed class EcrivainStatistiquesCourAppel : IEcrivainEcran
{
    public string Procedure => "PROC_STATCA";
    public string Ecran => "Statistiques cour d'appel";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Word = "Word";

    internal const string SansTableau = "« Word » n'est actif qu'après la génération du tableau : RIG v2 le grise sans résultat.";
    internal const string DebutApresFin = "La date de début doit être inférieure à la date de fin";
    internal const string ApresAujourdhui = "La date de début et la date de fin doivent être inférieure à la date du jour";
    internal const string LigneDisparue = "Une ligne du tableau n'est plus celle de la base : relancez la génération.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Word) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansTableau);
        // Les quatre categories du tableau (LecteurStatistiquesCourAppel.cs : rangs 0 a 3), toutes de la meme periode.
        var lues = geste.Demande.Ids.Select(EditionsV2.LirePeriode).ToArray();
        if (lues.Any(l => l is null || l.Value.Cle is not ("0" or "1" or "2" or "3")) || lues.Select(l => (l!.Value.Debut, l.Value.Fin)).Distinct().Count() != 1)
            return IssueGeste.Refuser(LigneDisparue);
        var (debut, fin) = (lues[0]!.Value.Debut, lues[0]!.Value.Fin);
        if (debut > fin) return IssueGeste.Refuser(DebutApresFin);
        if (debut > geste.Maintenant || fin > geste.Maintenant) return IssueGeste.Refuser(ApresAujourdhui);

        string Date(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var titre = $"Tableau de bord de l'activité commerciale du {Date(debut)} au {Date(fin)}";
        var id = await EditionsV2.DemandeEditionAsync(geste, null, EditionsV2.SansParametre(titre, "FUSION_ASPOSE"), titre, null, null, null, null, ct,
            new Dictionary<string, object?> { ["DMNED_LIBELLE_SOURCE"] = $"Activité commerciale du {Date(debut)} au {Date(fin)}" });
        var trace = new TraceGeste("demande", $"l'édition « {titre} »", EditionsV2.Texte(id), "a cree", null,
            new { Document = titre, Categories = lues.Length, File = "file des éditions" });
        return IssueGeste.Ecrit($"« {titre} » : placé dans la file des éditions.", [EditionsV2.Texte(id)], trace);
    }
}
