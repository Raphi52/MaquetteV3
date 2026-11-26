using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Registres judiciaires » (PROC_REGJUD) : « Impression » met chaque registre imprime dans la FILE DES EDITIONS
/// (v2.DEMANDE_EDITION), comme VALIDER de RIG v2. Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 6,
/// 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_REGJUD n'ecrit RIEN d'autre que des demandes d'edition (proc_regjud.md:756-757, « aucune
/// persistance SQL »). A la validation (DoGenererLesEditions, REGISTRE\OPE_REGISTRE_ABSTRACT.cs:113-126), chaque registre qui a des
/// lignes cree UNE DemandeEdition (GenererEditionRegistre), avec DMNED_IMPRESSION = 1, DMNED_ENVOI_PAR_MAIL = 0,
/// DMNED_AFF_AVANT_IMPRESSION = 0 et DMNED_LIBELLE_SOURCE = le titre de l'etape (PROC_REGJUD.Designer.cs:36, :47, :59) ; un
/// registre dont GenererEditionRegistre rend null n'en cree AUCUNE (« if (DERegistre != null) », ABSTRACT.cs:120). Les valeurs
/// de creation sont celles de DemandeEdition.CreateDemandeEdition (Outils\DLL\RigMetier\Edition\DemandeEdition.cs:536-560) :
/// date de creation, papier NORMAL, media IMPR, etat d'emission N, phase C (DemandeEditionPourServices.cs:21), utilisateur et
/// interlocuteur = le compte Windows de l'agent, 1 exemplaire, sans trame, agrafage ni recto-verso, groupage automatique, ni resume
/// ni lecture seule. Par registre (le groupe de la ligne) :
///   - repertoire general (OPE_REGISTRE_GENERAL.cs:218-236) : PAR LE REGLAGE D'EDITION « REP_GENERAL » du greffe
///     (CreateDemandeEditionByParametreGeneration, DemandeEdition.cs:2579-2597). Sans ce reglage dans v2.PARAMETRE_EDITION, la V2
///     rend null : AUCUNE demande, le geste est refuse et le dit (controle final du lot 6, 26/11/2026 : la V3 en inventait une).
///     Avec lui, la demande reprend ses colonnes, que la V2 serialise dans sa liste de parametres (ParametreEdition.cs:270-300,
///     :435-468) puis recopie dans la demande : CODE_MAQUETTE et CODE_PROCESSUS (CorrigeParametre, « a recuperer »,
///     DemandeEdition.cs:1825-1826), NOMBRE_EXEMPLAIRES, NUM_GROUPE_IMPRESSION, READ_ONLY et CODE_ENTITE_SOURCE (:586-631),
///     CODE_CFECP et TRIBUNAL_DIGITAL_NATURE_DOC (:2597-2598) ; puis FUSION_CRYSTAL_NET, « Répertoire général des N instances
///     enrôlées » ;
///   - decisions des IP (OPE_REGISTRE_DECISIONS_IP.cs:193-208) : FUSION_CRYSTAL, maquette LOT2_REG_DECISION_IP, groupe
///     RegDecisionIP, « Registre des N décisions des IP » ;
///   - ordonnances du president (OPE_REGISTRE_ORDO_PRESIDENT.cs:236-258) : FUSION_CRYSTAL, maquette LOT2_REG_INSTANCES, groupe
///     RegInstances, « Liste des N Ordonnances ».
/// N est le nombre de lignes envoyees (une par instance ou par decision : la V2 compte les lignes de sa requete).
/// NON REPRIS, nommes : la demande V2 du processus (DMNED_ID_DMND vide -- elle n'est qu'une trace, creee et validee d'un coup,
/// sans facture : le journal des gestes la remplace, decision du plan) ; le XML de generation (DMNED_PARAMETRE_GENERATION vide :
/// la liste du reglage nettoyee par CorrigeParametre, plus TitreEtat, GreffeNom et SQL de l'etat Crystal -- RIG v3 n'a pas le
/// generateur Crystal) ; le lot du reglage (LOT, que la V2 marque « erreur », DemandeEdition.cs:1823) ; le « - n° » d'ordre du
/// titre. « Aperçu » n'a pas d'ecriture dans la V2 (l'etat Crystal affiche) : refuse, dit.
/// CONTROLE (proc_regjud.md:271) : VALIDER n'est actif qu'avec au moins une ligne -- sans ligne, refus, rien d'ecrit.
/// </summary>
internal sealed class EcrivainRegistresJudiciaires : IEcrivainEcran
{
    public string Procedure => "PROC_REGJUD";

    public string Ecran => "Registres judiciaires";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Impression = "Impression";
    internal const string SansLigne = "Aucune ligne du registre à imprimer : RIG v2 n'active VALIDER qu'avec au moins une ligne.";
    internal const string LigneDisparue = "Une ligne du registre n'existe plus dans la base du greffe : relisez le registre.";
    internal const string ApercuNonRepris = "« Aperçu » affiche l'état Crystal du registre, que RIG v3 ne génère pas : « Impression » le place dans la file des éditions.";
    internal const string CodeReglageRepertoire = "REP_GENERAL";
    internal const string SansReglageRepertoire = "Le répertoire général n'est pas imprimé : le réglage d'édition REP_GENERAL n'existe pas dans ce greffe, et RIG v2 ne crée alors aucune demande.";

    /// <summary>Un registre : la lettre de ses lignes (LecteurRegistresJudiciaires), la table de son id, ce que la V2 ecrit.</summary>
    private sealed record Registre(char Lettre, string Table, string ColonneId, string Etape, string TypeGeneration, string? Maquette,
                                   string? Groupe, Func<int, string> Titre, string Objet);

    private static readonly Registre[] Registres =
    [
        new('G', "v2.INSTANCE", "INSTN_ID_INSTN", "Registre Général", "FUSION_CRYSTAL_NET", null, null,
            n => $"Répertoire général des {n} instances enrôlées", "le répertoire général"),
        new('I', "v2.DECISION_JUDICIAIRE", "DCJDC_ID_DCJDC", "Registre des Décisions des IP", "FUSION_CRYSTAL", "LOT2_REG_DECISION_IP", "RegDecisionIP",
            n => $"Registre des {n} décisions des IP", "le registre des décisions des IP"),
        new('O', "v2.INSTANCE", "INSTN_ID_INSTN", "Registre des Ordonnances du Président", "FUSION_CRYSTAL", "LOT2_REG_INSTANCES", "RegInstances",
            n => $"Liste des {n} Ordonnances", "le registre des ordonnances du président"),
    ];

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Impression) return IssueGeste.Refuser(ApercuNonRepris);
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(SansLigne);

        // Les lignes par registre (« G-123 », « I-456 », « O-789 » : LecteurRegistresJudiciaires), chacune relue en base.
        var parRegistre = new Dictionary<Registre, List<int>>();
        foreach (var id in geste.Demande.Ids)
        {
            var registre = Registres.FirstOrDefault(r => id.Length > 2 && id[0] == r.Lettre && id[1] == '-');
            if (registre is null || !int.TryParse(id[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return IssueGeste.Refuser(LigneDisparue);
            (parRegistre.TryGetValue(registre, out var l) ? l : parRegistre[registre] = []).Add(n);
        }
        foreach (var (registre, ids) in parRegistre)
        {
            // Les ids sont des entiers relus ci-dessus : la liste IN ne porte aucun texte saisi.
            int trouves;
            await using (var r = await geste.LireAsync(
                $"SELECT COUNT(*) FROM {registre.Table} WHERE {registre.ColonneId} IN ({string.Join(", ", ids.Distinct())})", ct))
                trouves = await r.ReadAsync(ct) ? r.GetInt32(0) : 0;
            if (trouves != ids.Distinct().Count()) return IssueGeste.Refuser(LigneDisparue);
        }

        // Le repertoire general passe par le reglage d'edition REP_GENERAL du greffe ; sans lui, la V2 ne cree rien (en tete).
        // fix-ok: mesure au controle final du lot 6 (26/11/2026) -- la V3 ecrivait toujours une demande sans maquette, d'ou la
        // demande n° 4445222 sur la copie de dev, qui n'a pas REP_GENERAL (209 reglages dans v2.PARAMETRE_EDITION, aucun
        // REP_GENERAL) ; la V2 rend null sans ce reglage (DemandeEdition.cs:2582-2584) et n'ajoute rien (ABSTRACT.cs:120).
        var general = Registres[0];
        Dictionary<string, object?>? reglage = null;
        if (parRegistre.ContainsKey(general))
        {
            reglage = await ReglageRepertoireAsync(geste, ct);
            if (reglage is null) parRegistre.Remove(general);
            if (parRegistre.Count == 0) return IssueGeste.Refuser(SansReglageRepertoire);
        }

        var compte = geste.Agent.CompteWindows.Length > 32 ? geste.Agent.CompteWindows[..32] : geste.Agent.CompteWindows;
        var crees = new List<string>();
        var traces = new List<TraceGeste>();
        foreach (var registre in Registres.Where(parRegistre.ContainsKey))
        {
            var n = parRegistre[registre].Count;
            var titre = registre.Titre(n);
            var valeurs = new Dictionary<string, object?>
            {
                ["DMNED_DATE_CREATION"] = geste.Maintenant, ["DMNED_TYPE_PAPIER"] = "NORMAL", ["DMNED_MEDIA"] = "IMPR",
                ["DMNED_ETAT_EMISSION"] = "N", ["DMNED_PHASE_TRAITEMENT"] = "C", ["DMNED_UTILISATEUR"] = compte,
                ["DMNED_NOMBRE_EXEMPLAIRE"] = (short)1, ["DMNED_IMPRESSION"] = true, ["DMNED_PAPIER_TRAME"] = false,
                ["DMNED_AGRAFAGE"] = false, ["DMNED_RECTO_VERSO"] = false, ["DMNED_TYPE_GENERATION"] = registre.TypeGeneration,
                ["DMNED_AFF_AVANT_IMPRESSION"] = false, ["DMNED_INTERLOCUTEUR"] = compte, ["DMNED_LIBELLE_DOCUMENT"] = titre,
                ["DMNED_GROUPAGE_AUTOMATIQUE"] = true, ["DMNED_RESUME"] = false, ["DMNED_READ_ONLY"] = false,
                ["DMNED_ENVOI_PAR_MAIL"] = false, ["DMNED_LIBELLE_SOURCE"] = registre.Etape,
                ["DMNED_CODE_MAQUETTE"] = registre.Maquette, ["DMNED_NUM_GROUPE"] = registre.Groupe,
                // fix-ok: erreur 515 mesuree sur la copie de dev le 26/11/2026 (« Cannot insert the value NULL into column
                // 'DMNED_HAS_NUMLRAR' ») : la base V3 garde ces colonnes NOT NULL sans leur defaut ((0)) de RIG_DEV
                // (INFORMATION_SCHEMA de SQL-DEV\DEV) ; la V2 les laisse a ce defaut, ecrit ici en clair.
                ["DMNED_HAS_NUMLRAR"] = false, ["DMNED_IS_NON_SUPPRESSIBLE"] = false, ["DMNED_HAS_NUMLR"] = false,
                ["DMNED_MASQUER_COFFRE_FORT"] = false,
            };
            // Le repertoire general : les colonnes de son reglage d'edition remplacent les valeurs de creation (en tete).
            if (registre == general && reglage is not null)
                foreach (var (colonne, valeur) in reglage) valeurs[colonne] = valeur;
            var id = await geste.InsererAsync("v2.DEMANDE_EDITION", "DMNED_ID_DMNED", valeurs, ct);
            var idTexte = id.ToString(CultureInfo.InvariantCulture);
            crees.Add(idTexte);
            traces.Add(new TraceGeste("affaire", $"l'impression de {registre.Objet}", idTexte, "a cree", null,
                new { Document = titre, Lignes = n, File = "file des éditions" }));
        }
        var message = crees.Count == 1
            ? $"{Registres.First(parRegistre.ContainsKey).Titre(parRegistre.Values.First().Count)} : placé dans la file des éditions."
            : $"{crees.Count} registres placés dans la file des éditions.";
        if (reglage is null && geste.Demande.Ids.Any(i => i.StartsWith("G-", StringComparison.Ordinal)))
            message += " " + SansReglageRepertoire;
        return IssueGeste.Ecrit(message, [.. crees], [.. traces]);
    }

    /// <summary>
    /// Les colonnes de la demande que la V2 tire du reglage d'edition REP_GENERAL (en tete : CreateDemandeEditionByParametreGeneration) ;
    /// null si le greffe n'a pas ce reglage. Une colonne vide du reglage n'entre pas dans sa liste (DelParametre,
    /// ParametreEdition.cs:460-461) : la demande garde alors sa valeur de creation, ici NULL ou celle du dictionnaire de base.
    /// </summary>
    private static async Task<Dictionary<string, object?>?> ReglageRepertoireAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync(
            "SELECT PRMED_CODE_MAQUETTE, PRMED_CODE_PROCESSUS, PRMED_NOMBRE_EXEMPLAIRES, PRMED_NUM_GROUPE_IMPRESSION, PRMED_READ_ONLY, "
            + "PRMED_CODE_ENTITE_SOURCE, PRMED_CODE_CFECP, PRMED_TRIBUNAL_DIGITAL_NATURE_DOC "
            + "FROM v2.PARAMETRE_EDITION WHERE PRMED_CODE = @code", ct, new SqlParameter("@code", CodeReglageRepertoire));
        if (!await r.ReadAsync(ct)) return null;
        object? Lu(string colonne) => r[colonne] is DBNull ? null : r[colonne];
        var reglage = new Dictionary<string, object?>
        {
            ["DMNED_CODE_MAQUETTE"] = Lu("PRMED_CODE_MAQUETTE"), ["DMNED_CODE_PROCESSUS"] = Lu("PRMED_CODE_PROCESSUS"),
            ["DMNED_NUM_GROUPE"] = Lu("PRMED_NUM_GROUPE_IMPRESSION"), ["DMNED_CODE_ENTITE_SOURCE"] = Lu("PRMED_CODE_ENTITE_SOURCE"),
            ["DMNED_CODE_CFECP"] = Lu("PRMED_CODE_CFECP"), ["DMNED_TRIBUNAL_DIGITAL_NATURE_DOC"] = Lu("PRMED_TRIBUNAL_DIGITAL_NATURE_DOC"),
        };
        // NOT NULL dans la V2 (PARAMETRE_EDITION.sql) : toujours dans la liste, donc toujours recopiees.
        if (Lu("PRMED_NOMBRE_EXEMPLAIRES") is { } exemplaires)
            reglage["DMNED_NOMBRE_EXEMPLAIRE"] = System.Convert.ToInt16(exemplaires, CultureInfo.InvariantCulture);
        if (Lu("PRMED_READ_ONLY") is bool lectureSeule) reglage["DMNED_READ_ONLY"] = lectureSeule;
        return reglage;
    }
}
