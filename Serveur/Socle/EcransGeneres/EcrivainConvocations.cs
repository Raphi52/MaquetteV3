using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Citer par huissier » des « Convocations » (PROC_GCONV) sur les instances cochees du cabinet (LecteurConvocations.cs : une
/// ligne = instance-demande d'edition-relance-exercice). Mode d'emploi : EcrivainEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7,
/// 26/11/2026.
///
/// SOURCE (ancien RIG, Source\RIG\DLL\Processus\PROC_GCONV\PROC_GCONV\ETP_CITATIONS\OPE_CITATIONS.cs ; proc_gconv.md, Detail OPE) :
///   - LES LIGNES : une LRAR revenue « RECEPTION » ou « NPAI » est grisee, non cochable (:94-95, TreeViewAudience.cs:286-287) ;
///   - LES CONTROLES (Verifier, :251-320), dans l'ordre : la maquette du code de citation (SURV_CEP_CITATION_HUISS, « CITATION » par
///     defaut, Surveillances.cs:423-425) doit avoir un destinataire de type huissier (CEP_EDITION.CEPED_DESTI_MODE_ENVOI, sauts de
///     ligne lus comme « ; », vide = « DI » ; « H », « H,... » ou « HUI ») ; l'huissier est obligatoire pour chaque instance cochee ;
///     une instance cochee deux fois est refusee (doublons de numero de role). Messages au mot pres ;
///   - L'HUISSIER : saisi une fois pour les lignes cochees -- RIG v2 le recopie de la ligne racine sur les lignes vides
///     (TreeViewAudience.cs:186-201) ; un identifiant numerique est un intervenant (INTERVENANT_PC_REPER), sinon un destinataire
///     institutionnel (DTINS_CODE) (:347-349). La liste de RIG v2 ne propose que ceux de la base : un autre est refuse ;
///   - L'ECRITURE (DoPreparationObjetsBeforeOperation, :241-249 ; Sauve, :323-332) : pour chaque instance cochee, un evenement de
///     procedure SURVENU au code de citation, date du jour (EvenementProcedure.CreateEvenementProcedure(..., true, false,
///     DateTime.Today ...), :245), ajoute a l'instance (numero d'ordre = nombre d'evenements + 1, Instance.cs:594-596).
///
/// CE QUE LA V3 NE REPREND PAS (nomme, rien en silence, avec la raison) :
///   - les EDITIONS (GenererLesEditions, :334-492) : RIG v2 les tire de l'evenement de citation (:359 ->
///     EvenementProcedure.CreateDemandeEdition, EvenementProcedure.cs:2607, puis GetAllEditions, :2801 : maquettes CEP_EDITION du
///     code et destinataires de l'instance, DestinatairesEditionsLot2XML.cs) ; la copie de l'ordonnance (piece VIDIGED, :397-433) et
///     le KBIS (case cochee, :436-457) ne se creent qu'A L'INTERIEUR de la boucle sur ces editions (:365). La V3 sait ecrire une
///     demande d'edition a parametre connu (v2.DEMANDE_EDITION : EcrivainRelancesComptesAnnuels, EcrivainRegistresJudiciaires),
///     pas calculer les editions d'un evenement : aucune demande n'est creee ;
///   - la FACTURE de la diligence (GenererFacture, :214-238 ; :461-489) : elle facture les articles de l'evenement
///     (EvenementProcedure.CreateArticles, EvenementProcedure.cs:2443), que la V3 ne calcule pas ; ses controles (dossier RCS,
///     premier dirigeant, :286-304) ne gardent qu'elle ;
///   - l'avertissement « copie de l'ordonnance » (index VIDIGED, :274-284), qui n'annonce que l'edition de la copie ;
///   - la demande « GCONV instance <role> » creee puis validee (:495-499, :330) : le journal des gestes la remplace, la facture
///     n'etant pas faite.
/// </summary>
internal sealed class EcrivainConvocations : IEcrivainEcran
{
    public string Procedure => "PROC_GCONV";

    public string Ecran => "Convocations";

    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Decider];

    /// <summary>Le geste (le Valider du descripteur, Surveiller.cs) et son parametre, au mot pres.</summary>
    internal const string Citer = "Citer par huissier", ParametreHuissier = "Huissier";

    /// <summary>Ce que RIG v2 produit aussi a la citation (GenererLesEditions, OPE_CITATIONS.cs:334-492 ; GenererFacture, :214-238) et
    /// que la V3 ne fait pas (voir l'en-tete) : le message du geste le dit a l'agent, une citation enregistree n'est pas une citation
    /// envoyee.</summary>
    // fix-ok: controle du lot 7 (26/11) -- la raison donnee (« pas de moteur d'editions ») etait fausse : la V3 ecrit deja des
    // demandes d'edition (EcrivainRelancesComptesAnnuels.cs:284). Ce qui manque est le calcul des editions et des articles de
    // l'evenement de citation (EvenementProcedure.cs:2607, :2801, :2443) ; le Kbis n'est edite que si sa case est cochee (:437).
    internal const string EditionsNonProduites =
        "La citation à l'huissier, la copie de l'ordonnance, le Kbis (s'il est demandé) et la facture ne sont pas produits : RIG v2 les tire "
        + "des éditions et des articles paramétrés pour l'événement de citation, que RIG v3 ne calcule pas encore.";

    private sealed record Ligne(int IdInstance, string Role, string? TypeDate);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var d = geste.Demande;
        if (d.Nom != Citer) return IssueGeste.Refuser($"Le geste « {d.Nom} » n'existe pas aux convocations de RIG v2.");
        if (d.Ids.Length == 0) return IssueGeste.Refuser("Cochez au moins une instance.");
        var lignes = new List<Ligne>();
        foreach (var id in d.Ids)
        {
            if (await LireLigneAsync(geste, id, ct) is not { } l) return IssueGeste.Refuser($"L'instance « {id} » n'est pas une convocation de la base du greffe.");
            if (l.TypeDate is "RECEPTION" or "NPAI")
                return IssueGeste.Refuser($"La LRAR de l'instance {l.Role} est revenue ({l.TypeDate}) : sa ligne est grisée, elle ne se cite pas.");
            lignes.Add(l);
        }

        // Verifier (OPE_CITATIONS.cs:251-320).
        var code = await CodeCitationAsync(geste, ct);
        if (!await MaquetteAvecHuissierAsync(geste, code, ct))
            return IssueGeste.Refuser("Il faut paramétrer un destinataire de type H sur la maquette du code événement CITATION.");
        var saisi = d.Valeurs.TryGetValue(ParametreHuissier, out var h) ? h.Trim() : "";
        if (saisi.Length == 0) return IssueGeste.Refuser("La saisie de l'huissier est obligatoire pour l'instance " + lignes[0].Role);
        var doublons = lignes.GroupBy(l => l.Role).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (doublons.Count > 0)
        {
            var liste = string.Join(";", doublons);
            return IssueGeste.Refuser(doublons.Count > 1 ? $"Les instances suivantes {liste} ont été cochées plusieurs fois."
                                                         : $"L'instance suivante {liste} a été cochée plusieurs fois.");
        }
        if (await NomHuissierAsync(geste, saisi, ct) is not { } huissier)
            return IssueGeste.Refuser($"L'huissier « {saisi} » n'est pas dans la base du greffe.");

        // Sauve (:323-332) : l'evenement de citation, date du jour.
        var traces = new List<TraceGeste>();
        foreach (var l in lignes)
        {
            var idEvenement = (await EvenementsProcedureV2.SurvenuAsync(geste, l.IdInstance, code, geste.Maintenant.Date, avecFutur: false, ct)).IdSurvenu;
            traces.Add(new TraceGeste("affaire", $"la citation du rôle {l.Role}", idEvenement.ToString(CultureInfo.InvariantCulture), "a cree", null,
                new { Instance = l.Role, Evenement = code, Date = geste.Maintenant.Date, Huissier = huissier }));
        }
        var n = lignes.Count;
        return IssueGeste.Ecrit($"{n} citation{(n > 1 ? "s" : "")} par huissier enregistrée{(n > 1 ? "s" : "")} ({huissier}). {EditionsNonProduites}",
                                [.. d.Ids], [.. traces]);
    }

    /// <summary>Le code d'evenement de la citation (Surveillances.GetCodeEveProCitationHuissier, Surveillances.cs:423-425).</summary>
    private static async Task<string> CodeCitationAsync(GesteEnCours geste, CancellationToken ct)
    {
        await using var r = await geste.LireAsync("SELECT Valeur FROM soc.Reglage WHERE Code = 'SURV_CEP_CITATION_HUISS'", ct);
        return await r.ReadAsync(ct) && FormeLigne.Texte(r, "Valeur") is { Length: > 0 } v ? v : "CITATION";
    }

    /// <summary>Un destinataire de type huissier sur la maquette du code (Verifier, OPE_CITATIONS.cs:252-263).</summary>
    private static async Task<bool> MaquetteAvecHuissierAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        var modes = new List<string>();
        await using (var r = await geste.LireAsync("SELECT CEPED_DESTI_MODE_ENVOI FROM v2.CEP_EDITION WHERE CEPED_CODE_EVE_PRO = @c", ct,
                         new SqlParameter("@c", code)))
            while (await r.ReadAsync(ct)) modes.Add(FormeLigne.Texte(r, "CEPED_DESTI_MODE_ENVOI"));
        return modes.Any(m => (m.Length == 0 ? "DI" : m.Replace("\r\n", ";", StringComparison.Ordinal)).Split(';')
                                 .Any(x => x == "H" || x.StartsWith("H,", StringComparison.Ordinal) || x == "HUI"));
    }

    /// <summary>Le nom de l'huissier saisi : un intervenant (id numerique) ou un destinataire institutionnel (OPE_CITATIONS.cs:347-351).</summary>
    private static async Task<string?> NomHuissierAsync(GesteEnCours geste, string saisi, CancellationToken ct)
    {
        var numerique = int.TryParse(saisi, NumberStyles.None, CultureInfo.InvariantCulture, out var id);
        await using var r = numerique
            ? await geste.LireAsync("SELECT INTRPR_NOM AS Nom FROM v2.INTERVENANT_PC_REPER WHERE INTRPR_ID_INTRPR = @id", ct, new SqlParameter("@id", id))
            : await geste.LireAsync("SELECT DTINS_CODE AS Nom FROM v2.DESTINATAIRE_INSTITUTIONNEL WHERE DTINS_CODE = @c", ct, new SqlParameter("@c", saisi));
        return await r.ReadAsync(ct) ? FormeLigne.Texte(r, "Nom") : null;
    }

    /// <summary>Une ligne du lecteur (« instance-demande d'edition-relance-exercice ») : l'instance et le retour de sa LRAR.</summary>
    private static async Task<Ligne?> LireLigneAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        var parts = id.Split('-');
        if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var instance)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var edition)) return null;
        await using var r = await geste.LireAsync("""
            SELECT i.INSTN_NUM_ROLE, l.LRARI_TYPE_DATE
            FROM v2.INSTANCE AS i WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED = @edition
            JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = de.DMNED_NUM_LRAR
            WHERE i.INSTN_ID_INSTN = @instance
            """, ct, new SqlParameter("@instance", instance), new SqlParameter("@edition", edition));
        if (!await r.ReadAsync(ct)) return null;
        return new Ligne(instance, FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "LRARI_TYPE_DATE") is { Length: > 0 } t ? t : null);
    }
}
