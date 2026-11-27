using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Encaissements » (PROC_ENCAISS, web/src/ecrans/EcranEncaissements.tsx) ouvert pour un dossier : l'encaissement de départ de ses
/// factures, lu dans la base V3 du greffe (v2.DEMANDE, v2.FACTURE, v2.ARTICLE_UTILISE, v2.VENTILATION, v2.CLIENT_SOLDE, rec.Client,
/// rec.DossierRcs). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE (ancien RIG) : le constructeur de PROC_ENCAISS, mode « encaissement multi-factures » (PROC_ENCAISS.cs:89-111 ;
/// proc_encaiss.md, Détail OPE, « les modes d'amorçage ») : pour les factures reçues, une ventilation par couple (affaire, client) au
/// montant -Affaire.GetSolde(client) (Affaire.cs:291-316 : ventilations de l'affaire pour ce client moins les factures TTC distinctes de
/// ce client qui ont un article sur l'affaire), un couple au solde nul n'a pas de ventilation (:106) ; le montant de l'encaissement est
/// la somme (:102), le payeur le nom du client de la PREMIÈRE facture (:103). Les factures du dossier : celles des demandes du dossier
/// (FACTU_NUM_DEMANDE = DMND_NUM_DEMANDE ; le dossier par son numéro de gestion RCS, ou le numéro indicatif de la demande), dans
/// l'ordre de leur numéro ; leur affaire : DMND_ID_AFFRE. Les soldes de la grille : Affaire.GetSolde (Affaire.cs:318-337 :
/// ventilations moins articles TTC, la requête de LectureAffaires.cs) et Client.SoldeClient (Client.cs:1181-1195 : le dernier solde
/// validé + ventilations - factures depuis, pour un compte C hors PC ; sinon ventilations - factures).
/// Sans dossier, l'écran part du mode « libre » de RIG v2 (:87 : montant 0, sans client) : rien à lire.
/// </summary>
public static class LectureEncaissements
{
    private const string Requete = """
        SELECT f.FACTU_NUMERO_FACTURE, d.DMND_ID_AFFRE, af.AFFRE_LIBELLE, f.FACTU_ID_CLIEN, c.Nom, c.Abrege,
               ROUND(ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_AFFRE = d.DMND_ID_AFFRE AND v.VENTI_ID_CLIEN = f.FACTU_ID_CLIEN), 0)
                     - ISNULL((SELECT SUM(x.FACTU_MONTANT_TTC) FROM (SELECT DISTINCT ff.FACTU_NUMERO_FACTURE, ISNULL(ff.FACTU_MONTANT_TTC, 0) AS FACTU_MONTANT_TTC
                                                                     FROM v2.FACTURE AS ff JOIN v2.ARTICLE_UTILISE AS u ON u.ARTUT_NUMERO_FACTURE = ff.FACTU_NUMERO_FACTURE
                                                                     WHERE u.ARTUT_ID_AFFRE = d.DMND_ID_AFFRE AND ff.FACTU_ID_CLIEN = f.FACTU_ID_CLIEN) AS x), 0), 2) AS SoldeAffaireClient,
               ROUND(ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_AFFRE = d.DMND_ID_AFFRE), 0)
                     - ISNULL((SELECT SUM(u.ARTUT_MONTANT_TTC) FROM v2.ARTICLE_UTILISE AS u WHERE u.ARTUT_ID_AFFRE = d.DMND_ID_AFFRE), 0), 2) AS SoldeAffaire,
               ROUND(CASE WHEN c.Type = 'C' AND c.EstComptePc = 0 AND s.CLISO_DATE_SOLDE IS NOT NULL
                          THEN s.CLISO_MONTANT
                               + ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = c.Id AND v.VENTI_DATE_VENTILATION >= s.CLISO_DATE_SOLDE AND v.VENTI_DATE_VENTILATION < @maintenant), 0)
                               - ISNULL((SELECT SUM(ff.FACTU_MONTANT_TTC) FROM v2.FACTURE AS ff WHERE ff.FACTU_ID_CLIEN = c.Id AND ff.FACTU_DATE_CREATION >= s.CLISO_DATE_SOLDE AND ff.FACTU_DATE_CREATION < @maintenant), 0)
                          ELSE ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = c.Id AND v.VENTI_DATE_VENTILATION < @maintenant), 0)
                               - ISNULL((SELECT SUM(ff.FACTU_MONTANT_TTC) FROM v2.FACTURE AS ff WHERE ff.FACTU_ID_CLIEN = c.Id AND ff.FACTU_DATE_CREATION < @maintenant), 0) END, 2) AS SoldeClient
        FROM v2.FACTURE AS f
        JOIN v2.DEMANDE AS d ON d.DMND_NUM_DEMANDE = f.FACTU_NUM_DEMANDE
        JOIN rec.Client AS c ON c.Id = f.FACTU_ID_CLIEN
        LEFT JOIN v2.AFFAIRE AS af ON af.AFFRE_ID_AFFRE = d.DMND_ID_AFFRE
        LEFT JOIN rec.DossierRcs AS ds ON ds.Id = d.DMND_ID_DSRCS
        OUTER APPLY (SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
                     WHERE CLISO_ID_CLIEN = c.Id AND CLISO_DATE_SOLDE <= @maintenant ORDER BY CLISO_DATE_SOLDE DESC, CLISO_ID_CLISO DESC) AS s
        WHERE (ds.NumeroGestion = @dossier OR d.DMND_NUM_INDICATIF = @dossier) AND d.DMND_ID_AFFRE IS NOT NULL
        ORDER BY f.FACTU_NUMERO_FACTURE
        """;

    /// <summary>L'encaissement de départ des factures du dossier <paramref name="dossier"/> (aucune facture : payeur vide, montant 0).</summary>
    public static async Task<EncaissementDeDepart> LireAsync(SqlConnection baseGreffe, string dossier, DateTime maintenant, CancellationToken ct)
    {
        var factures = new List<string>();
        var couples = new List<VentilationLue>();
        var payeur = "";
        await using (var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@dossier", dossier.Trim()),
                         new SqlParameter("@maintenant", maintenant)))
            while (await r.ReadAsync(ct))
            {
                factures.Add(FormeLigne.Texte(r, "FACTU_NUMERO_FACTURE").Trim());
                if (payeur.Length == 0) payeur = FormeLigne.Texte(r, "Nom").Trim();
                var affaire = r.GetInt32(1);
                var client = r.GetInt32(3);
                if (couples.Any(c => c.IdAffaire == affaire && c.IdClient == client)) continue;
                couples.Add(new VentilationLue(affaire, FormeLigne.Texte(r, "AFFRE_LIBELLE").Trim(), client, FormeLigne.Texte(r, "Nom").Trim(),
                    FormeLigne.Texte(r, "Abrege").Trim(), -Convert.ToDecimal(r["SoldeAffaireClient"], CultureInfo.InvariantCulture),
                    Convert.ToDecimal(r["SoldeAffaire"], CultureInfo.InvariantCulture), Convert.ToDecimal(r["SoldeClient"], CultureInfo.InvariantCulture)));
            }
        VentilationLue[] ventilations = [.. couples.Where(c => c.Montant != 0m)];
        return new EncaissementDeDepart(payeur, ventilations.Sum(v => v.Montant), [.. factures], ventilations);
    }
}
