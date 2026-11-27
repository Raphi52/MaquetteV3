using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Éditions différées » (PROC_EDT_DIFF) : « Clôturer le lot » ferme le groupe d'impression d'un lot ouvert et le rend pret a
/// imprimer, dans la base du greffe ; le service d'impression de RIG v2 le prend ensuite. Mode d'emploi : EcrivainEcran.cs ; la
/// liste : LecteurEditionsDifferees.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_EDT_DIFF\ (proc_edt_diff.md, Detail OPE) -- le bouton « Clôturer » de chaque
/// lot (OPE_EDT_DIFF_LOT._Toolbar_ButtonClick, :64-73) : Processus.Verifier(), SauvegardeFinale (chaque DemandeEdition du lot sauvee
/// telle que la liste la tient : sans case changee, inchangee), puis Cloturer -- MetierEdtDiff.Cloturer (:31-35) : phase
/// PRET_A_IMPRIMER (« P », DemandeEditionPourServices.cs:16-33) et GPIMP_DATE_FIN = maintenant, le groupe sauve.
/// CONTROLES, avant toute ecriture :
///   1. un geste connu, au moins un lot coche ;
///   2. le lot est ouvert dans la liste (la requete de GetLotEdtActifs) ;
///   3. une imprimante : « Vous devez choisir une imprimante pour pouvoir clôturer » (DoVerifier, :52-54), au mot pres.
/// Traces : famille « file », « a modifie », la phase et la date de fin avant et apres. Ecrivant le lot, la V3 ecrit aussi ce que
/// RIG v2 trace par une Demande « seule » validee « Clôture LOT {nom} ({n} document(s)) » (OPE_LISTE_LOT._CreerDemande, :140-143) :
/// cette demande n'est qu'une trace, remplacee par le journal des gestes (decision du chef du plan) ; son libelle est l'objet de la
/// trace.
///
/// NON REPRIS (nomme) : l'avertissement « l'imprimante choisie est l'imprimante d'Amitel » (IMPED_ONLY_AMITEL, :56-58), un
/// avertissement non bloquant que la V3 donne dans son message ; le choix de l'imprimante (la liste de la synthese).
/// </summary>
internal sealed class EcrivainEditionsDifferees : IEcrivainEcran
{
    public string Procedure => "PROC_EDT_DIFF";
    public string Ecran => "Éditions différées";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Le geste du descripteur, au mot pres (le bouton VALIDER relibelle « Clôturer », OPE_EDT_DIFF_LOT.cs:26-27).</summary>
    internal const string Cloturer = "Clôturer le lot";

    /// <summary>Le refus de RIG v2 sans imprimante (OPE_EDT_DIFF_LOT.DoVerifier, :53-54).</summary>
    internal const string SansImprimante = "Vous devez choisir une imprimante pour pouvoir clôturer";

    private sealed record Lot(string Groupe, string Nom, string Imprimante, int Documents);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Cloturer) return IssueGeste.Refuser($"Geste inconnu de l'écran « {Ecran} » : {geste.Demande.Nom}.");
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser("Aucun lot coché : rien n'est clôturé.");

        var lots = new List<Lot>();
        foreach (var id in ids)
        {
            Lot? lot = null;
            await using (var r = await geste.LireAsync(LecteurEditionsDifferees.Requete.Replace("/*groupe*/", "AND g.GPIMP_NUM_GROUPE = @g", StringComparison.Ordinal),
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
        foreach (var l in lots)
        {
            await geste.EcrireAsync("UPDATE v2.GROUPE_IMPRESSION SET GPIMP_PHASE_TRAITEMENT = @p, GPIMP_DATE_FIN = @fin WHERE GPIMP_NUM_GROUPE = @g AND GPIMP_DATE_FIN IS NULL",
                ct, new SqlParameter("@p", LecteurGroupesImpression.PhasePret), new SqlParameter("@fin", geste.Maintenant), new SqlParameter("@g", l.Groupe));
            if (await ImprimanteAmitelAsync(geste, l.Imprimante, ct)) amitel.Add(l.Imprimante);
            traces.Add(new TraceGeste("demande", $"Clôture LOT {l.Nom} ({l.Documents} document(s))", l.Groupe, "a modifie",
                new Dictionary<string, string> { ["Phase"] = "en préparation", ["Date de fin"] = "" },
                new Dictionary<string, string> { ["Phase"] = LecteurGroupesImpression.LibellePhase(LecteurGroupesImpression.PhasePret), ["Date de fin"] = FormeLigne.Date(geste.Maintenant) }));
        }
        var n = lots.Count;
        var message = (n > 1 ? $"{n} lots clôturés" : $"Lot {lots[0].Nom} clôturé") + " : prêt à imprimer, le service d'impression de RIG v2 le prend."
                    + (amitel.Count == 0 ? "" : $" Attention, l'imprimante choisie est l'imprimante d'Amitel ({string.Join(", ", amitel.Distinct())}).");
        return IssueGeste.Ecrit(message, [.. lots.Select(l => l.Groupe)], [.. traces]);
    }

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
