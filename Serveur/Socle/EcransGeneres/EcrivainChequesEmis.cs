using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Cheques emis » (PROC_EDCHQEMIS) : « Voir » pose l'etat des remboursements emis affiches dans la FILE DES EDITIONS. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurChequesEmis.cs (Id = l'encaissement de remboursement) ; l'etat et le droit : EditionsEtatsV2.cs
/// (en tete). Plan-f4fb097f, lot 4, 28/11/2026.
///
/// SOURCE (ancien RIG) : VOIR, seul bouton (OPE_EDCHQEMIS.cs:54-58) -> Visualiser (:83-92) -> EmisTropPercus.LancerEmisTropPercus puis
/// Afficher (D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu\EmisTropPercus.cs:80-196).
/// CONTROLES, avant toute ecriture :
///   1. le droit d'executer EDCHQEMIS (EditionsEtatsV2.ProcessusAutoriseAsync ; FORM_ACCUEIL.cs:1624, au mot pres) -- dans la copie de
///      RIG_DEV, il vient des menus de comptabilite (soc.Droit, origine « comptabilite ») : aucun role ne le porte ;
///   2. au moins un remboursement : VOIR n'est actif qu'avec un element, sinon « Il y a 0 élément à visualiser » (nbElements, :65-68 ;
///      deux cases decochees, :117-119), au mot pres ;
///   3. chaque ligne est un remboursement de l'etat : l'encaissement existe, son mode est de type RBT, et il a une ventilation sur un
///      client, une affaire et une demande de cette affaire (les INNER JOIN de LancerEmisTropPercus, :93-101, comme le lecteur) -- sinon
///      refus, message sans source V2.
/// L'ETAT : Expresso_EMIS_TROPPERCU.rpt (:172) ; le titre selon les cases (:162-170) : les deux « Chèques émis entre le jj/mm/aaaa et le
/// jj/mm/aaaa », notifications seules « Chèques émis pour notifications entre le ... », trop-perçus seuls « Chèques émis pour trop perçus
/// entre le ... » (la V2 y ecrit la date ET l'heure, DateTime par defaut, :169 : la V3 ecrit la date, comme les deux autres titres).
/// REQUETE : celle de LancerEmisTropPercus (:93-101), sur les remboursements edites. TOTAL : la somme des ENCMT_MONTANT, un remboursement
/// une fois (la V2 dedoublonne par encaissement, :148-157).
/// LES CASES ET LA PERIODE : les saisies « Trop-perçus », « Notifications » (« oui ») et « Période » (« du|au ») si l'ecran les envoie ;
/// l'Etat de la V3 les envoie avec toutes les lignes affichees, en un geste (FormeEtat.tsx, lot 16 du plan-f4fb097f). Cases non saisies :
/// l'origine des remboursements edites (toutes notifications, aucune, ou les deux) ; periode non saisie : du premier au dernier.
/// TRACE : famille « demande », « a cree », l'etat pose (titre, nombre de remboursements, total).
/// </summary>
internal sealed class EcrivainChequesEmis : IEcrivainEcran
{
    public string Procedure => "PROC_EDCHQEMIS";
    public string Ecran => "Chèques émis";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Voir = "Voir", Processus = "EDCHQEMIS", NomRpt = "Expresso_EMIS_TROPPERCU.rpt";
    internal const string ZeroElement = "Il y a 0 élément à visualiser";
    internal const string HorsEtat = "Un remboursement n'est plus à l'état des chèques émis (supprimé, mode de paiement autre qu'un remboursement, ou sans ventilation sur un client, une affaire et sa demande) : relisez la liste.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Voir) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu des chèques émis : RIG v2 n'a que « {Voir} ».");
        // 1. Le droit d'executer le processus.
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Processus, ct)) return IssueGeste.Refuser(EditionsEtatsV2.NonAutorise(Processus));
        // 2. Au moins un remboursement.
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(ZeroElement);
        if (EditionsEtatsV2.Entiers(geste.Demande.Ids) is not { } ids) return IssueGeste.Refuser(HorsEtat);

        // 3. Chaque ligne a l'etat (les jointures de LancerEmisTropPercus) ; les ids sont des entiers relus ci-dessus.
        var liste = string.Join(", ", ids);
        var dates = new List<DateTime>();
        var total = 0m;
        int notifications = 0;
        await using (var r = await geste.LireAsync($"""
            SELECT e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MONTANT, CASE WHEN e.ENCMT_ORIGINE = 'NTF' THEN 1 ELSE 0 END AS Notification
            FROM v2.ENCAISSEMENT AS e
            INNER JOIN v2.MODE_PAIEMENT AS mp ON mp.R_MODEPAIEME_VALEUR = e.ENCMT_MODE_PAIEMENT
            WHERE e.ENCMT_ID_ENCMT IN ({liste}) AND mp.R_MODEPAIEME_TYPE = 'RBT'
              AND EXISTS (SELECT 1 FROM v2.VENTILATION AS v INNER JOIN rec.Client AS c ON c.Id = v.VENTI_ID_CLIEN
                          INNER JOIN v2.AFFAIRE AS a ON a.AFFRE_ID_AFFRE = v.VENTI_ID_AFFRE INNER JOIN v2.DEMANDE AS d ON d.DMND_ID_AFFRE = a.AFFRE_ID_AFFRE
                          WHERE v.VENTI_ID_ENCMT = e.ENCMT_ID_ENCMT)
            """, ct))
        {
            while (await r.ReadAsync(ct))
            {
                dates.Add(r.GetDateTime(0));
                total += r.GetDecimal(1);
                notifications += r.GetInt32(2);
            }
        }
        if (dates.Count != ids.Length) return IssueGeste.Refuser(HorsEtat);

        var valeurs = geste.Demande.Valeurs;
        bool? Case(string libelle) => CriteresConsultation.OuiNon(valeurs.GetValueOrDefault(libelle));
        var (trop, notif) = Case(LecteurChequesEmis.TropPercus) is null && Case(LecteurChequesEmis.Notifications) is null
            ? (notifications < ids.Length, notifications > 0)
            : (Case(LecteurChequesEmis.TropPercus) == true, Case(LecteurChequesEmis.Notifications) == true);
        if (!trop && !notif) return IssueGeste.Refuser(ZeroElement);
        var (du, au) = valeurs.GetValueOrDefault(LecteurChequesEmis.CriterePeriode) is { Length: > 0 } periode
            ? LecteurJournalCaisse.Periode(periode, geste.Maintenant.Date)
            : (dates.Min().Date, dates.Max().Date);
        var entre = $"entre le {EditionsEtatsV2.Jour(du)} et le {EditionsEtatsV2.Jour(au)}";
        var titre = notif ? (trop ? $"Chèques émis {entre}" : $"Chèques émis pour notifications {entre}") : $"Chèques émis pour trop perçus {entre}";
        var requete = "SELECT DISTINCT ENCMT_MONTANT, ENCMT_DATE_ENCAISSEMENT, ENCMT_NUMERO_TITRE, VENTI_ID_AFFRE, VENTI_MONTANT, ENCMT_COMMENTAIRE, CLIEN_ABREGE, "
            + "CLIEN_NUMERO_COMPTE, CLIEN_NOM, AFFRE_DATE_CREATION, R_MODEPAIEME_TYPE, ISNULL(DSSRC_NUM_GESTION, CLIEN_ABREGE) AS DSSRC_NUM_GESTION, ENCMT_ORIGINE, "
            + "ENCMT_ID_ENCMT FROM ENCAISSEMENT INNER JOIN VENTILATION ON ENCMT_ID_ENCMT = VENTI_ID_ENCMT INNER JOIN CLIENT ON VENTI_ID_CLIEN = CLIEN_ID_CLIEN "
            + "INNER JOIN AFFAIRE ON VENTI_ID_AFFRE = AFFRE_ID_AFFRE INNER JOIN DEMANDE ON AFFRE_ID_AFFRE = DMND_ID_AFFRE "
            + "LEFT OUTER JOIN DOSSIER_RCS ON DMND_ID_DSRCS = DSSRC_ID_DSRCS INNER JOIN MODE_PAIEMENT ON ENCMT_MODE_PAIEMENT = R_MODEPAIEME_VALEUR "
            + $"WHERE R_MODEPAIEME_TYPE = 'RBT' AND ENCMT_ID_ENCMT IN ({liste}) ORDER BY ENCMT_ID_ENCMT, ENCMT_NUMERO_TITRE, DSSRC_NUM_GESTION DESC";
        var etat = new EditionsEtatsV2.Etat(Processus, titre, NomRpt, requete, ids.Length, total);
        var id = await EditionsEtatsV2.PoserAsync(geste, etat, ct);

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        var remboursements = $"{ids.Length} remboursement{(ids.Length > 1 ? "s" : "")}";
        var trace = new TraceGeste("demande", $"l'édition « {titre} »", idTexte, "a cree", null,
            new { Etat = NomRpt, Remboursements = ids.Length, Total = total, File = "file des éditions" });
        return IssueGeste.Ecrit($"{titre} : {remboursements}, {CriteresConsultation.Euros(total)}, placé dans la file des éditions.", [idTexte], trace);
    }
}
