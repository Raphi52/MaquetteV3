using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Remise en banque » (PROC_REMBANQ) : le bordereau DEFINITIF et l'annulation d'un remis, sur les lignes cochees. Mode d'emploi :
/// EcrivainEcran.cs ; la liste : LecteurRemiseEnBanque.cs. Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026.
///
/// « Générer un bordereau » = le bouton Définitif de la V2 (OPE_ACTIONS.cs:80-104, _getData("D") :142-166), puis
/// RemiseEnBanque.PreparerDonnees (D:\RigApplication\Source\DllRapportCrystal\RigCrystalFactu\RemiseEnBanque.cs:89-235) :
///   1. aucun encaissement coche -> « Attenttion, aucun encaissement n'a été coché ! » (OPE_ACTIONS.cs:102, au mot pres, faute
///      comprise) ; un encaissement deja remis depuis la lecture ne compte pas (la V2 ne montre que ENCMT_REMIS IS NULL) ;
///   2. des mois differents -> le refus de la comptabilite de tresorerie (OPE_ACTIONS.cs:170). La V2 compare les mois des deux
///      dates de la periode ; la liste de la V3 est celle du jour, et la regle porte sur ce que le remis contient : les mois
///      (annee comprise, point 6 de la synthese du Detail OPE) des encaissements coches ;
///   3. les encaissements coches qui ont une banque par leur mode (INNER JOIN MODE_PAIEMENT_BANQUE, BANQUE, MODE_PAIEMENT,
///      RemiseEnBanque.cs:101-105), tries par BANQU_CODE, mode, ENCMT_REMIS DESC, date, montant (:130) ; le numero de remis part de
///      MAX(ENCMT_REMIS) de toute la table (_GetLastNumRemis, :318-323) et augmente de 1 a chaque changement de mode (:171-174) ;
///   4. ecrit, pour chacun : UPDATE ENCAISSEMENT SET ENCMT_REMIS = &lt;remis&gt;, ENCMT_ID_BANQU = &lt;banque&gt; (:221-226) -- les
///      seules lignes que la V2 ecrit. Un encaissement dont le mode n'a pas de banque est laisse a remettre, comme la V2 (le
///      INNER JOIN l'ecarte ; la garde « mode sans banque » de OPE_MODEPAIEMENT.cs:74-82 n'est jamais appelee, Detail OPE) ; le
///      message le nomme. Aucun encaissement coche avec banque : refus (rien a remettre), message de la V3.
/// « Annuler le remis » = le bouton Annuler (OPE_ACTIONS._controlRemis(true), :192-213, :262-283) pour le numero de remis des lignes
/// cochees : une ligne sans remis -> « Veuillez entrer un numéro de remis » (:283) ; aucun encaissement non comptabilise sur ce
/// remis -> « Description du problème : le remis n°N est déjà comptabilisé » (:279) ; sinon UPDATE ENCAISSEMENT SET ENCMT_REMIS =
/// NULL, ENCMT_ID_BANQU = NULL WHERE ENCMT_REMIS = N AND ENCMT_DATE_COMPTABILISATION IS NULL (:203-204) -- tout le remis, pas
/// seulement les lignes cochees : la V2 annule un NUMERO.
/// NON REPRIS (nomme) : le bordereau imprime (etat Crystal Expresso_Remise_Banque.rpt, :233, et sa table ##REMISE_BANQUE), le
/// brouillon et la reedition (des editions sans ecriture), le filtre par utilisateur (la V3 prend tous les utilisateurs, le defaut
/// de la V2) et le INNER JOIN UTILISATEUR (:106 ; l'agent V2 est soc.Compte dans la V3 : un encaissement sans agent repris n'est pas
/// ecarte). La confirmation « Voulez vous lancer l'édition définitive ? » (:95) est celle de l'ecran (geste explicite).
/// </summary>
internal sealed class EcrivainRemiseEnBanque : IEcrivainEcran
{
    public string Procedure => "PROC_REMBANQ";
    public string Ecran => "Remise en banque";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Generer = "Générer un bordereau", Annuler = "Annuler le remis";

    // Les messages de la V2, mot pour mot ; ses retours a la ligne (« \n\n », « \n ») deviennent une espace ou un point : l'ecran
    // affiche une ligne, et le banc compare une ligne. fix-ok: mesure a la lecture du banc (Program.cs:833, chaque ligne de .gestes
    // est Trim() et lue seule) -- un message a retour a la ligne ne pouvait pas y etre ecrit, donc pas controle.
    internal const string AucunCoche = "Attenttion, aucun encaissement n'a été coché !";
    internal const string MoisDifferents = "L'utilisation de la comptabilité de trésorerie ne vous permet pas de mettre des encaissements de mois " +
                                           "différents sur le même remis. Veuillez faire un remis par mois.";

    public Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct) => geste.Demande.Nom switch
    {
        Generer => GenererAsync(geste, ct),
        Annuler => AnnulerAsync(geste, ct),
        var autre => Task.FromResult(IssueGeste.Refuser($"Geste « {autre} » inconnu de la remise en banque.")),
    };

    private static int[] Ids(GesteEnCours geste)
        => [.. geste.Demande.Ids.Select(i => int.TryParse(i, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Where(n => n > 0).Distinct()];

    private static string ListeIds(int[] ids) => string.Join(", ", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    private sealed record ARemettre(int Id, DateTime Date, string Mode, int? Banque, string? CodeBanque);

    private static async Task<IssueGeste> GenererAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = Ids(geste);
        if (ids.Length == 0) return IssueGeste.Refuser(AucunCoche);
        // Les encaissements coches encore a remettre, sous verrou, avec la banque de leur mode (RemiseEnBanque.cs:101-105, :130).
        var lus = new List<ARemettre>();
        await using (var r = await geste.LireAsync($"""
            SELECT e.ENCMT_ID_ENCMT, e.ENCMT_DATE_ENCAISSEMENT, e.ENCMT_MODE_PAIEMENT, b.BANQU_ID_BANQU, b.BANQU_CODE
            FROM v2.ENCAISSEMENT AS e WITH (UPDLOCK, HOLDLOCK)
            LEFT JOIN v2.MODE_PAIEMENT_BANQUE AS mb ON mb.MODBQ_MODE_PAIEMENT = e.ENCMT_MODE_PAIEMENT
            LEFT JOIN v2.BANQUE AS b ON b.BANQU_ID_BANQU = mb.MODBQ_ID_BANQU
            WHERE e.ENCMT_ID_ENCMT IN ({ListeIds(ids)}) AND e.ENCMT_REMIS IS NULL
            ORDER BY CASE WHEN b.BANQU_ID_BANQU IS NULL THEN 1 ELSE 0 END, b.BANQU_CODE, e.ENCMT_MODE_PAIEMENT, e.ENCMT_DATE_ENCAISSEMENT,
                     e.ENCMT_MONTANT, e.ENCMT_ID_ENCMT
            """, ct))
            while (await r.ReadAsync(ct))
                lus.Add(new ARemettre(r.GetInt32(0), r.GetDateTime(1), FormeLigne.Texte(r, "ENCMT_MODE_PAIEMENT"),
                    CriteresConsultation.EntierDe(r, "BANQU_ID_BANQU"), r["BANQU_CODE"] as string));
        if (lus.Count == 0) return IssueGeste.Refuser(AucunCoche);
        if (lus.Select(l => (l.Date.Year, l.Date.Month)).Distinct().Count() > 1) return IssueGeste.Refuser(MoisDifferents);

        var avecBanque = lus.Where(l => l.Banque is not null).ToList();
        var sansBanque = lus.Where(l => l.Banque is null).ToList();
        if (avecBanque.Count == 0)
            return IssueGeste.Refuser($"Rien n'est remis : le mode de paiement des encaissements cochés ({string.Join(", ", sansBanque.Select(l => l.Mode).Distinct())}) " +
                                      "n'a pas de banque associée dans les paramètres globaux.");

        int compteur;
        await using (var r = await geste.LireAsync("SELECT ISNULL(MAX(ENCMT_REMIS), 0) FROM v2.ENCAISSEMENT WITH (UPDLOCK, HOLDLOCK)", ct))
        {
            await r.ReadAsync(ct);
            compteur = r.GetInt32(0);
        }
        var traces = new List<TraceGeste>();
        var remis = new SortedSet<int>();
        string? modePrecedent = null;
        foreach (var l in avecBanque)
        {
            if (l.Mode != modePrecedent) { compteur++; modePrecedent = l.Mode; }
            await geste.EcrireAsync("UPDATE v2.ENCAISSEMENT SET ENCMT_REMIS = @remis, ENCMT_ID_BANQU = @banque WHERE ENCMT_ID_ENCMT = @id", ct,
                new SqlParameter("@remis", compteur), new SqlParameter("@banque", l.Banque!.Value), new SqlParameter("@id", l.Id));
            remis.Add(compteur);
            traces.Add(new TraceGeste("encaissement", $"l'encaissement n° {l.Id}", l.Id.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Remis = (int?)null, Banque = (int?)null }, new { Remis = compteur, Banque = l.Banque, l.CodeBanque }));
        }
        var numeros = string.Join(", ", remis.Select(n => "n° " + n.ToString(CultureInfo.InvariantCulture)));
        var message = $"{avecBanque.Count} encaissement{(avecBanque.Count > 1 ? "s" : "")} remis en banque dans la base du greffe : remis {numeros} ; " +
                      "le bordereau imprimé (état de la V2) n'est pas produit par la V3.";
        if (sansBanque.Count > 0)
            message += sansBanque.Count == 1
                ? $" Laissé à remettre, faute de banque pour son mode de paiement : n° {sansBanque[0].Id}."
                : $" Laissés à remettre, faute de banque pour leur mode de paiement : n° {string.Join(", ", sansBanque.Select(l => l.Id))}.";
        return IssueGeste.Ecrit(message, [.. avecBanque.Select(l => l.Id.ToString(CultureInfo.InvariantCulture))], [.. traces]);
    }

    private static async Task<IssueGeste> AnnulerAsync(GesteEnCours geste, CancellationToken ct)
    {
        var ids = Ids(geste);
        var numeros = new SortedSet<int>();
        await using (var r = await geste.LireAsync($"SELECT ENCMT_REMIS FROM v2.ENCAISSEMENT WHERE ENCMT_ID_ENCMT IN ({(ids.Length == 0 ? "0" : ListeIds(ids))})", ct))
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(0) || r.GetInt32(0) <= 0) return IssueGeste.Refuser("Veuillez entrer un numéro de remis");
                numeros.Add(r.GetInt32(0));
            }
        if (numeros.Count == 0) return IssueGeste.Refuser("Veuillez entrer un numéro de remis");

        var traces = new List<TraceGeste>();
        var detaches = new List<string>();
        foreach (var n in numeros)
        {
            var aDetacher = new List<int>();
            await using (var r = await geste.LireAsync(
                "SELECT ENCMT_ID_ENCMT FROM v2.ENCAISSEMENT WITH (UPDLOCK, HOLDLOCK) WHERE ENCMT_REMIS = @n AND ENCMT_DATE_COMPTABILISATION IS NULL ORDER BY ENCMT_ID_ENCMT",
                ct, new SqlParameter("@n", n)))
                while (await r.ReadAsync(ct)) aDetacher.Add(r.GetInt32(0));
            if (aDetacher.Count == 0)
                return IssueGeste.Refuser($"Description du problème : le remis n°{n} est déjà comptabilisé");
            await geste.EcrireAsync("""
                UPDATE v2.ENCAISSEMENT SET ENCMT_REMIS = NULL, ENCMT_ID_BANQU = NULL
                WHERE ENCMT_REMIS = @n AND ENCMT_DATE_COMPTABILISATION IS NULL
                """, ct, new SqlParameter("@n", n));
            traces.Add(new TraceGeste("encaissement", $"le remis n° {n}", n.ToString(CultureInfo.InvariantCulture), "a modifie",
                new { Remis = n, Encaissements = aDetacher }, new { Remis = (int?)null }));
            detaches.AddRange(aDetacher.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }
        var message = numeros.Count == 1
            ? $"Traitement terminé. Le remis n°{numeros.Min} a été annulé."
            : $"Traitement terminé. Les remis n°{string.Join(", n°", numeros)} ont été annulés.";
        return IssueGeste.Ecrit(message + $" Encaissement{(detaches.Count > 1 ? "s" : "")} de nouveau à remettre : n° {string.Join(", ", detaches)}.",
            [.. detaches], [.. traces]);
    }
}
