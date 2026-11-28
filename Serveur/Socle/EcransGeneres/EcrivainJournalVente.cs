using System.Globalization;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Journal de vente » (PROC_JVENTE) : « Éditer » pose le journal des articles vendus affiches dans la FILE DES EDITIONS. Mode d'emploi :
/// EcrivainEcran.cs ; les lignes : LecteurJournalVente.cs (Id = l'article utilise) ; l'etat et le droit : EditionsEtatsV2.cs (en tete).
/// Plan-f4fb097f, lot 4, 28/11/2026.
///
/// SOURCE (ancien RIG) : EDITER (le Detail OPE de proc_jvente.md : « uniquement sur EDITER ») -> OPE_TRI (D:\RigApplication\Source\RIG\
/// DLL\Processus\PROC_JVENTE\PROC_JVENTE\OPE_TRI.cs) -> JournalVente.GenererDonneesRapport puis l'etat Crystal (DllRapportCrystal\
/// RigCrystalFactu\JournalVente.cs:106-590).
/// CONTROLES, avant toute ecriture :
///   1. le droit d'executer JVENTE (EditionsEtatsV2.ProcessusAutoriseAsync ; FORM_ACCUEIL.cs:1624, au mot pres) ;
///   2. au moins un article : sinon « Aucun élément trouvé » (JournalVente.cs:578-579, au mot pres). Les gardes des cases a cocher
///      (« Vous devez sélectionner au moins un utilisateur », OPE_CRITERESGENERAUX.cs:146 ; types de client, d'article, taux, factures,
///      OPE_FILTRES.cs:131-137) n'ont pas d'objet : la V3 n'a pas ces cases, un filtre vide vaut « tous » (LecteurJournalVente, en tete) ;
///   3. chaque ligne est au journal : l'article utilise existe et son client est dans la base (INNER JOIN CLIENT, JournalVente.cs:284) --
///      sinon refus, message sans source V2.
/// L'ETAT : l'edition « Détaillée » que la V3 affiche (JVENTE_TYPE_EDITION par defaut, OPE_EDITION.cs:34-37) : titre « Journal de vente de
/// la période du jj/mm/aaaa[ au jj/mm/aaaa] - Détaillé » (JournalVente.cs:512-513, :522 ; « au » seulement si la fin differe), etat
/// Expresso_JVente.rpt (:523-524, hors « PC seuls », filtre que la V3 n'a pas). REQUETE : les colonnes de l'edition detaillee
/// (:254-262), sur les articles edites, dans l'ordre de la V2 (:407). TOTAL : la somme des montants TTC (ARTUT_MONTANT_TTC) ; TOTAL_HT :
/// celle des HT (prix unitaire x quantite, :258), comme les colonnes du lecteur.
/// LA PERIODE : la saisie « Période » (« du|au ») si l'ecran l'envoie ; l'Etat de la V3 l'envoie avec toutes les lignes affichees
/// (FormeEtat.tsx, un geste, lot 16 du plan-f4fb097f) ; non saisie, la periode est celle des articles edites, a la date de leur facture, ou a la leur pour un article tarife
/// (JournalVente.cs:297-303, comme le lecteur). NON REPRIS (nommes) : les editions « Par titre », « Résumée », « Par facture » et
/// « Analytique » (le lecteur ne les affiche pas) ; l'affichage commentaires / libelles (JVENTE_AFF_ARTICLE).
/// TRACE : famille « demande », « a cree », l'etat pose (titre, etat Crystal, nombre d'articles, totaux).
/// </summary>
internal sealed class EcrivainJournalVente : IEcrivainEcran
{
    public string Procedure => "PROC_JVENTE";
    public string Ecran => "Journal de vente";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    internal const string Editer = "Éditer", Processus = "JVENTE", NomRpt = "Expresso_JVente.rpt";
    internal const string HorsJournal = "Un article n'est plus au journal de vente (supprimé, ou son client n'est pas dans la base) : relisez le journal.";

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (geste.Demande.Nom != Editer) return IssueGeste.Refuser($"Geste « {geste.Demande.Nom} » inconnu du journal de vente : RIG v2 n'a que « {Editer} ».");
        // 1. Le droit d'executer le processus.
        if (!await EditionsEtatsV2.ProcessusAutoriseAsync(geste, Processus, ct)) return IssueGeste.Refuser(EditionsEtatsV2.NonAutorise(Processus));
        // 2. Au moins un article.
        if (geste.Demande.Ids.Length == 0) return IssueGeste.Refuser(EditionsEtatsV2.AucunElement);
        if (EditionsEtatsV2.Entiers(geste.Demande.Ids) is not { } ids) return IssueGeste.Refuser(HorsJournal);

        // 3. Chaque ligne au journal ; les ids sont des entiers relus ci-dessus.
        var liste = string.Join(", ", ids);
        var dates = new List<DateTime>();
        decimal ttc = 0m, ht = 0m;
        await using (var r = await geste.LireAsync($"""
            SELECT CASE WHEN au.ARTUT_NUMERO_FACTURE IS NULL THEN au.ARTUT_DATE_CREATION ELSE f.FACTU_DATE_CREATION END AS Quand,
                   ISNULL(au.ARTUT_MONTANT_TTC, 0) AS Ttc, ISNULL(au.ARTUT_PRIX_UHT, 0) * ISNULL(au.ARTUT_QUANTITE, 0) AS Ht
            FROM v2.ARTICLE_UTILISE AS au
            INNER JOIN rec.Client AS c ON c.Id = au.ARTUT_ID_CLIEN
            LEFT JOIN v2.FACTURE AS f ON f.FACTU_NUMERO_FACTURE = au.ARTUT_NUMERO_FACTURE
            WHERE au.ARTUT_ID_ARTUT IN ({liste})
            """, ct))
        {
            while (await r.ReadAsync(ct))
            {
                if (r["Quand"] is DateTime quand) dates.Add(quand);
                ttc += Convert.ToDecimal(r["Ttc"], CultureInfo.InvariantCulture);
                ht += Convert.ToDecimal(r["Ht"], CultureInfo.InvariantCulture);
            }
        }
        // Un article lu sans date (facture sans date de creation) n'est pas au journal : sa periode ne le retient jamais.
        if (dates.Count != ids.Length) return IssueGeste.Refuser(HorsJournal);

        var (du, au) = geste.Demande.Valeurs.GetValueOrDefault(LecteurJournalVente.CriterePeriode) is { Length: > 0 } periode
            ? LecteurJournalCaisse.Periode(periode, geste.Maintenant.Date)
            : (dates.Min().Date, dates.Max().Date);
        var titre = $"Journal de vente de la période du {EditionsEtatsV2.Jour(du)}{(du != au ? $" au {EditionsEtatsV2.Jour(au)}" : "")} - Détaillé";
        var requete = "SELECT ARTUT_ID_AFFRE, ARTUT_MONTANT_TTC, ARTUT_PRIX_UHT, ISNULL((SELECT TOP 1 ARTCL_CODE_ARTICLE_GREFFE FROM ARTICLE "
            + "WHERE ARTCL_CODE_ARTICLE_RIG = ARTUT_CODE_ARTICLE ORDER BY ARTCL_DATE_APPLICATION DESC, ARTCL_ID_ARTCL DESC), ARTUT_CODE_ARTICLE) AS ARTUT_CODE_ARTICLE, "
            + "ARTUT_DATE_CREATION, ARTUT_NUMERO_FACTURE, ARTUT_MONTANT_INPI, ARTUT_MONTANT_BODACC, ARTUT_MONTANT_AUTRE, ARTUT_ID_CLIEN, ARTUT_ID_UTLST, "
            + "ARTUT_QUANTITE, ARTUT_TAUX_TVA, CLIEN_ID_CLIEN, CLIEN_NOM, ARTUT_ID_ARTUT, ARTUT_LIBELLE FROM ARTICLE_UTILISE "
            + "INNER JOIN CLIENT ON ARTUT_ID_CLIEN = CLIEN_ID_CLIEN LEFT OUTER JOIN FACTURE ON ARTUT_NUMERO_FACTURE = FACTU_NUMERO_FACTURE "
            + $"WHERE ARTUT_ID_ARTUT IN ({liste}) ORDER BY ARTUT_DATE_CREATION, ARTUT_NUMERO_FACTURE, ARTUT_ID_ARTUT";
        var etat = new EditionsEtatsV2.Etat(Processus, titre, NomRpt, requete, ids.Length, ttc, new Dictionary<string, decimal> { ["TOTAL_HT"] = ht });
        var id = await EditionsEtatsV2.PoserAsync(geste, etat, ct);

        var idTexte = id.ToString(CultureInfo.InvariantCulture);
        var articles = $"{ids.Length} article{(ids.Length > 1 ? "s" : "")}";
        var trace = new TraceGeste("demande", $"l'édition « {titre} »", idTexte, "a cree", null,
            new { Etat = NomRpt, Articles = ids.Length, TotalHt = ht, TotalTtc = ttc, File = "file des éditions" });
        return IssueGeste.Ecrit($"{titre} : {articles}, {CriteresConsultation.Euros(ht)} HT, {CriteresConsultation.Euros(ttc)} TTC, placé dans la file des éditions.",
            [idTexte], trace);
    }
}
