using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurEditionsDifferees;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Éditions différées » (PROC_EDT_DIFF) : les gestes du detail d'un lot (« Ne pas imprimer », « Remettre à l'impression ») et
/// « Clôturer le lot », dans la base du greffe ; le service d'impression de RIG v2 prend ensuite le lot clos. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurEditionsDifferees.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_EDT_DIFF\EDT_DIFF_LOT\ (proc_edt_diff.md, Detail OPE).
///   - « Ne pas imprimer » / « Remettre à l'impression » : la case « Ne pas imprimer » de la grille d'un lot (DataGridViewListeLot,
///     :54-60), puis TRAITER (OPE_LISTE_LOT._TraiterEditionsCochees, :53-78). Case cochee : DMNED_IMPRESSION = faux, la phase « P »
///     (pret a imprimer) redevient « E » (emis) ; case decochee sur un document non imprime : DMNED_IMPRESSION = vrai, le document
///     rejoint le groupe du lot (DMNED_NUM_GROUPE), « E » redevient « P ». RIG v2 tient ce choix en memoire et ne l'enregistre qu'a la
///     cloture (SauvegardeFinale, :83-88) ; la V3 n'a pas de memoire d'ecran : elle l'ecrit tout de suite, le document RESTANT dans son
///     groupe, barre -- l'etat que la grille de RIG v2 relit et sait defaire (_FillRow, :132-139 ; TRAITER, :66-73). Le retrait du
///     groupe (DMNED_NUM_GROUPE vide, :60) se fait a la cloture, comme la sauvegarde de RIG v2.
///   - « Clôturer le lot » (OPE_EDT_DIFF_LOT._Toolbar_ButtonClick, :64-73) : Processus.Verifier(), SauvegardeFinale (les documents
///     barres quittent le groupe, ci-dessus), puis MetierEdtDiff.Cloturer (Metier\MetierEdtDiff.cs:31-35) : phase PRET_A_IMPRIMER
///     (« P », DemandeEditionPourServices.cs:16-33) et GPIMP_DATE_FIN = maintenant, le groupe sauve.
/// CONTROLES, avant toute ecriture :
///   1. un geste connu, au moins une ligne cochee ;
///   2. le geste vise sa sorte de ligne : un document (« Ne pas imprimer », « Remettre à l'impression ») ou un lot (« Clôturer le
///      lot ») -- RIG v2 porte la case sur la ligne du document et le bouton sur la synthese du lot ;
///   3. la ligne est dans un lot ouvert (les requetes de LecteurEditionsDifferees) ;
///   4. la case change (un document deja barre ne se barre pas deux fois) ;
///   5. la cloture : une imprimante -- « Vous devez choisir une imprimante pour pouvoir clôturer » (DoVerifier, :52-54), au mot pres.
/// Traces : famille « demande », « a modifie », l'impression du document, ou la phase et la date de fin du lot. Ecrivant
/// le lot, la V3 ecrit aussi ce que RIG v2 trace par une Demande « seule » validee « Clôture LOT {nom} ({n} document(s)) »
/// (OPE_LISTE_LOT._CreerDemande, :140-143) : cette demande n'est qu'une trace, remplacee par le journal des gestes (decision du chef du
/// plan) ; son libelle est l'objet de la trace.
///
/// NON REPRIS (nomme) : l'avertissement « l'imprimante choisie est l'imprimante d'Amitel » (IMPED_ONLY_AMITEL, :56-58), un
/// avertissement non bloquant que la V3 donne dans son message ; le choix de l'imprimante (la liste de la synthese).
/// </summary>
internal sealed class EcrivainEditionsDifferees : IEcrivainEcran
{
    public string Procedure => "PROC_EDT_DIFF";
    public string Ecran => "Éditions différées";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les gestes du descripteur, au mot pres : le bouton VALIDER relibelle « Clôturer » (OPE_EDT_DIFF_LOT.cs:26-27) et la
    /// case « Ne pas imprimer » de la grille (DataGridViewListeLot.cs:54-58), cochee ou decochee.</summary>
    internal const string Cloturer = "Clôturer le lot", NePasImprimer = "Ne pas imprimer", Remettre = "Remettre à l'impression";

    /// <summary>Le refus de RIG v2 sans imprimante (OPE_EDT_DIFF_LOT.DoVerifier, :53-54).</summary>
    internal const string SansImprimante = "Vous devez choisir une imprimante pour pouvoir clôturer";

    private sealed record Lot(string Groupe, string Nom, string Imprimante, int Documents);

    private sealed record Document(int Id, string Libelle, string Lot, string Groupe, bool Imprime, string Phase);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var nom = geste.Demande.Nom;
        if (nom is not (Cloturer or NePasImprimer or Remettre)) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(nom == Cloturer ? "Aucun lot coché : rien n'est clôturé." : "Aucun document coché : rien n'est modifié.");
        return nom == Cloturer ? await CloturerAsync(geste, ids, ct) : await ImpressionAsync(geste, ids, nom == Remettre, ct);
    }

    /// <summary>« Ne pas imprimer » (<paramref name="imprimer"/> faux) ou « Remettre à l'impression » sur des documents (TRAITER).</summary>
    private static async Task<IssueGeste> ImpressionAsync(GesteEnCours geste, string[] ids, bool imprimer, CancellationToken ct)
    {
        var nom = imprimer ? Remettre : NePasImprimer;
        var documents = new List<Document>();
        foreach (var id in ids)
        {
            if (!id.StartsWith("E-", StringComparison.Ordinal))
                return IssueGeste.Refuser($"« {nom} » vaut pour un document du lot, pas pour le lot lui-même ({id}) : dépliez le lot dans « {Documents} ».");
            if (await DocumentAsync(geste, id, ct) is not { } d) return IssueGeste.Refuser($"Document introuvable parmi ceux des lots ouverts ({id}).");
            if (d.Imprime == imprimer)
                return IssueGeste.Refuser(imprimer ? $"Le document « {d.Libelle} » du lot {d.Lot} est déjà à imprimer."
                                                   : $"Le document « {d.Libelle} » du lot {d.Lot} est déjà marqué « Ne pas imprimer ».");
            documents.Add(d);
        }

        var traces = new List<TraceGeste>();
        foreach (var d in documents)
        {
            // _TraiterEditionsCochees (:58-73) : la phase P <-> E, l'impression ; le groupe reste (voir l'en-tete).
            var phase = imprimer ? (d.Phase == "E" ? "P" : d.Phase) : (d.Phase == "P" ? "E" : d.Phase);
            await geste.EcrireAsync("UPDATE v2.DEMANDE_EDITION SET DMNED_IMPRESSION = @i, DMNED_PHASE_TRAITEMENT = @p, DMNED_NUM_GROUPE = @g WHERE DMNED_ID_DMNED = @id",
                ct, new SqlParameter("@i", imprimer), new SqlParameter("@p", (object?)NullSiVide(phase) ?? DBNull.Value), new SqlParameter("@g", d.Groupe),
                new SqlParameter("@id", d.Id));
            traces.Add(new TraceGeste("demande", $"le document « {d.Libelle} » du lot {d.Lot}", d.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new Dictionary<string, string> { ["Impression"] = d.Imprime ? "à imprimer" : "ne pas imprimer", ["Phase"] = d.Phase },
                new Dictionary<string, string> { ["Impression"] = imprimer ? "à imprimer" : "ne pas imprimer", ["Phase"] = phase }));
        }
        var n = documents.Count;
        var message = imprimer
            ? (n > 1 ? $"{n} documents remis à l'impression" : $"Document « {documents[0].Libelle} » remis à l'impression") + "."
            : n > 1 ? $"{n} documents marqués « Ne pas imprimer » : ils quitteront leur lot à sa clôture."
                    : $"Document « {documents[0].Libelle} » marqué « Ne pas imprimer » : il quittera le lot à sa clôture.";
        return IssueGeste.Ecrit(message, [.. documents.Select(d => "E-" + d.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static async Task<IssueGeste> CloturerAsync(GesteEnCours geste, string[] ids, CancellationToken ct)
    {
        var lots = new List<Lot>();
        foreach (var id in ids)
        {
            if (id.StartsWith("E-", StringComparison.Ordinal))
                return IssueGeste.Refuser($"« {Cloturer} » se joue sur la ligne du lot (« {LotsOuverts} »), pas sur un de ses documents ({id}).");
            Lot? lot = null;
            await using (var r = await geste.LireAsync(Requete.Replace("/*groupe*/", "AND g.GPIMP_NUM_GROUPE = @g", StringComparison.Ordinal),
                             ct, new SqlParameter("@plafond", 1), new SqlParameter("@g", id)))
                if (await r.ReadAsync(ct))
                    lot = new Lot(FormeLigne.Texte(r, "GPIMP_NUM_GROUPE"), FormeLigne.Texte(r, "LOT_NOM"), FormeLigne.Texte(r, "GPIMP_NOM_IMPRIMANTE"),
                                  Convert.ToInt32(r["NB"], CultureInfo.InvariantCulture));
            if (lot is null) return IssueGeste.Refuser($"Lot introuvable parmi les lots d'éditions différées ouverts ({id}).");
            if (lot.Imprimante.Length == 0) return IssueGeste.Refuser(SansImprimante);
            lots.Add(lot);
        }

        var amitel = new List<string>();
        var traces = new List<TraceGeste>();
        var retires = 0;
        foreach (var l in lots)
        {
            // SauvegardeFinale (:83-88) : un document barre quitte le groupe (_TraiterEditionsCochees, :59-60).
            retires += await geste.EcrireAsync("UPDATE v2.DEMANDE_EDITION SET DMNED_NUM_GROUPE = NULL WHERE DMNED_NUM_GROUPE = @g AND DMNED_IMPRESSION = 0",
                ct, new SqlParameter("@g", l.Groupe));
            await geste.EcrireAsync("UPDATE v2.GROUPE_IMPRESSION SET GPIMP_PHASE_TRAITEMENT = @p, GPIMP_DATE_FIN = @fin WHERE GPIMP_NUM_GROUPE = @g AND GPIMP_DATE_FIN IS NULL",
                ct, new SqlParameter("@p", LecteurGroupesImpression.PhasePret), new SqlParameter("@fin", geste.Maintenant), new SqlParameter("@g", l.Groupe));
            if (await ImprimanteAmitelAsync(geste, l.Imprimante, ct)) amitel.Add(l.Imprimante);
            traces.Add(new TraceGeste("demande", $"Clôture LOT {l.Nom} ({l.Documents} document(s))", l.Groupe, "a modifie",
                new Dictionary<string, string> { ["Phase"] = "en préparation", ["Date de fin"] = "" },
                new Dictionary<string, string> { ["Phase"] = LecteurGroupesImpression.LibellePhase(LecteurGroupesImpression.PhasePret), ["Date de fin"] = FormeLigne.Date(geste.Maintenant) }));
        }
        var n = lots.Count;
        var message = (n > 1 ? $"{n} lots clôturés" : $"Lot {lots[0].Nom} clôturé") + " : prêt à imprimer, le service d'impression de RIG v2 le prend."
                    + (retires == 0 ? "" : retires > 1 ? $" {retires} documents marqués « Ne pas imprimer » quittent le lot." : " 1 document marqué « Ne pas imprimer » quitte le lot.")
                    + (amitel.Count == 0 ? "" : $" Attention, l'imprimante choisie est l'imprimante d'Amitel ({string.Join(", ", amitel.Distinct())}).");
        return IssueGeste.Ecrit(message, [.. lots.Select(l => l.Groupe)], [.. traces]);
    }

    /// <summary>Le document « E-&lt;id&gt; » d'un lot ouvert ; null sinon.</summary>
    private static async Task<Document?> DocumentAsync(GesteEnCours geste, string id, CancellationToken ct)
    {
        if (!int.TryParse(id.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        await using var r = await geste.LireAsync(RequeteDocuments.Replace("/*document*/", "AND e.DMNED_ID_DMNED = @id", StringComparison.Ordinal), ct,
                                                  new SqlParameter("@plafond", 1), new SqlParameter("@id", n));
        if (!await r.ReadAsync(ct)) return null;
        var libelle = FormeLigne.Texte(r, "DMNED_LIBELLE_DOCUMENT");
        return new Document(n, libelle.Length > 0 ? libelle : $"Document {n}", FormeLigne.Texte(r, "LOT_NOM"), FormeLigne.Texte(r, "GPIMP_NUM_GROUPE"),
                            r["DMNED_IMPRESSION"] is not bool b || b, FormeLigne.Texte(r, "DMNED_PHASE_TRAITEMENT"));
    }

    private static string? NullSiVide(string s) => s.Length == 0 ? null : s;

    /// <summary>Vrai si <paramref name="imprimante"/> est une imprimante reservee a Amitel (ImprimanteLot2.IMPED_ONLY_AMITEL, :56-58).</summary>
    private static async Task<bool> ImprimanteAmitelAsync(GesteEnCours geste, string imprimante, CancellationToken ct)
    {
        // Une base sans la table (un banc) n'a pas d'imprimante d'Amitel : la table se teste avant d'etre lue (un SELECT qui la nomme
        // echoue a la compilation quand elle manque).
        await using (var existe = await geste.LireAsync("SELECT CONVERT(bit, CASE WHEN OBJECT_ID(N'v2.IMPRIMANTE_EDITION') IS NULL THEN 0 ELSE 1 END)", ct))
            if (!await existe.ReadAsync(ct) || !existe.GetBoolean(0)) return false;
        await using var r = await geste.LireAsync("SELECT COUNT(*) FROM v2.IMPRIMANTE_EDITION WHERE IMPED_NOM_LOGIQUE = @i AND IMPED_ONLY_AMITEL = 1",
                                                  ct, new SqlParameter("@i", imprimante));
        return await r.ReadAsync(ct) && r.GetInt32(0) > 0;
    }
}
