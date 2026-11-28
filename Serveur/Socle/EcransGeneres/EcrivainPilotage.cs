using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Pilotage » (PROC_XPILOTAGE) : « Visualiser » genere le tableau de pilotage affiche -- la demande XPILOTAGE de RIG v2 et le classeur,
/// mis dans la FILE DES EDITIONS. Mode d'emploi : EcrivainEcran.cs ; le tableau : LecteurPilotage.cs (Id = « cellule|periode »,
/// EditionsV2.IdSurPeriode). Plan « RIG v3 complet, mesure par le banc du parcours complet », lot 1, 28/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_XPILOTAGE\OPE_GENERER.cs :
///   - « Visualiser » (APERCU renomme, :74 ; :147-149) -> GenererDocument (:156-213) : les controles, au mot pres -- « Vous devez saisir
///     une date de début et une date de fin », « La date de début doit être inférieure à la date de fin », « La date de début et la date de
///     fin doivent être inférieure à la date du jour », « Vous devez saisir au moins un type de statistique\n(RCS ou Judiciaire) »
///     (:157-164) ; la periode vient de la ligne (la forme Etat n'envoie pas ses parametres), le type de la feuille des cellules envoyees
///     (« 1 » RCS, « 2 » judiciaire, :259-260) ;
///   - le nom du document : « Tableau pilotage RCS et Judiciaire du jj-mm-aaaa au jj-mm-aaaa », ou RCS, ou Judiciaire seul (:180-186) ;
///   - le travail de fond (_backgroundWorker_DoWork, :108-121) ECRIT EN BASE : une Demande au nom du document, au code du processus
///     (Demande.CreateDemande(_NomDocument, "", MetierCodeProcessus)), cloturee validee quand le classeur est rempli (:115), supprimee
///     sinon (:117) -- les 27 demandes XPILOTAGE de RIG_DEV (V, T ; lu le 28/11/2026) ;
///   - « Archives » (ARCHIVES, :150-151 -> AfficherArchives, :416-425) ouvre l'explorateur du poste sur le dossier des tableaux generes
///     (CheminRepertoireCommun\XPILOTAGE\…) : rien en base ; RIG v3 n'a pas ce dossier : refuse, dit.
/// LA V3 : la demande XPILOTAGE validee (EditionsV2.DemandeProcessusValideeAsync), et, comme toute edition de RIG v3, le classeur en demande
/// d'edition dans la file (decision du plan, EditionsV2.SansParametre ; REPORT_SQL, le type de RIG v2 d'un tableau tire de requetes),
/// rattachee a cette demande. La forme Etat envoie un geste PAR GROUPE (FormeEtat.tsx) : les deux feuilles a l'ecran donnent deux
/// tableaux, « RCS » et « Judiciaire », la ou RIG v2 en fait un seul. NON REPRIS (nommes) : le classeur lui-meme (le modele XPILOTAGE.XLT,
/// ses feuilles remplies, :215 et suivantes) et le suffixe « _n » d'un nom deja pris dans le dossier des archives (:188-199).
/// Trace : famille « demande », la demande XPILOTAGE creee, « a cree ».
/// </summary>
internal sealed class EcrivainPilotage : IEcrivainEcran
{
    public string Procedure => "PROC_XPILOTAGE";
    public string Ecran => "Pilotage";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Visualiser = "Visualiser", Archives = "Archives";

    /// <summary>GenererDocument, OPE_GENERER.cs:158-164, au mot pres.</summary>
    internal const string SansType = "Vous devez saisir au moins un type de statistique\n(RCS ou Judiciaire)";
    internal const string DebutApresFin = "La date de début doit être inférieure à la date de fin";
    internal const string ApresAujourdhui = "La date de début et la date de fin doivent être inférieure à la date du jour";

    internal const string ArchivesNonReprises =
        "« Archives » ouvre, sur le poste, le dossier des tableaux de pilotage déjà générés (le répertoire commun du greffe) : RIG v3 n'a pas ce dossier, rien n'est ouvert.";
    internal const string LigneDisparue = "Une cellule du tableau n'est plus dans la base du greffe : relancez le calcul.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom == Archives) return IssueGeste.Refuser(ArchivesNonReprises);
        if (geste.Demande.Nom != Visualiser) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansType);
        var lues = geste.Demande.Ids.Select(EditionsV2.LirePeriode).ToArray();
        var cellules = lues.Select(l => l is { } x && int.TryParse(x.Cle, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : -1).ToArray();
        if (cellules.Any(c => c < 0) || lues.Select(l => (l!.Value.Debut, l.Value.Fin)).Distinct().Count() != 1)
            return IssueGeste.Refuser(LigneDisparue);
        var (debut, fin) = (lues[0]!.Value.Debut, lues[0]!.Value.Fin);
        if (debut > fin) return IssueGeste.Refuser(DebutApresFin);
        if (debut > geste.Maintenant || fin > geste.Maintenant) return IssueGeste.Refuser(ApresAujourdhui);

        // Les feuilles des cellules (PILOT_FEUILLE, « 1 » RCS, « 2 » judiciaire) ; des entiers relus ci-dessus dans la liste IN.
        var distinctes = cellules.Distinct().ToArray();
        var feuilles = new HashSet<string>(StringComparer.Ordinal);
        var trouvees = 0;
        await using (var r = await geste.LireAsync(
            $"SELECT LTRIM(RTRIM(PILOT_FEUILLE)) FROM v2.PILOTAGE_REQUETES WHERE PILOT_ID_PILOT IN ({string.Join(", ", distinctes.Select(EditionsV2.Texte))})", ct))
            while (await r.ReadAsync(ct)) { trouvees++; if (!r.IsDBNull(0)) feuilles.Add(r.GetString(0)); }
        if (trouvees != distinctes.Length) return IssueGeste.Refuser(LigneDisparue);
        var (rcs, jud) = (feuilles.Contains("1"), feuilles.Contains("2"));
        if (!rcs && !jud) return IssueGeste.Refuser(SansType);

        // :180-186 (ToShortDateString de RIG v2, « / » remplaces par « - »).
        string Date(DateTime d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        var nom = $"Tableau pilotage {(rcs && jud ? "RCS et Judiciaire" : rcs ? "RCS" : "Judiciaire")} du {Date(debut)} au {Date(fin)}";
        var (idDemande, numero, refus) = await EditionsV2.DemandeProcessusValideeAsync(geste, "XPILOTAGE", nom, ct);
        if (refus is not null) return IssueGeste.Refuser(refus);
        var idEdition = await EditionsV2.DemandeEditionAsync(geste, idDemande, EditionsV2.SansParametre(nom, "REPORT_SQL"), nom, null, null, null, null, ct,
            new Dictionary<string, object?> { ["DMNED_LIBELLE_SOURCE"] = "Tableau de pilotage" });
        var trace = new TraceGeste("demande", $"la demande {numero} « {nom} »", EditionsV2.Texte(idDemande), "a cree", null,
            new { Demande = numero, Processus = "XPILOTAGE", Document = nom, Cellules = distinctes.Length, Edition = idEdition });
        return IssueGeste.Ecrit($"« {nom} » : demande {numero} validée, le tableau est placé dans la file des éditions.",
            [EditionsV2.Texte(idDemande)], trace);
    }
}
