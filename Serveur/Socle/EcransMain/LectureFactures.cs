using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Factures » (PROC_FACTURE, web/src/ecrans/EcranFactures.tsx) ouvert pour un dossier : la demande de ce dossier que sa facture
/// reprend, et le client de sa facture, lus dans la base V3 du greffe (v2.DEMANDE, v2.FACTURE, rec.Client, rec.ClientAdresse, v2.CLIENT_SOLDE,
/// v2.VENTILATION). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 12, 27/11/2026, sur la marche du lot 17.
///
/// SOURCE : la règle de l'écran (Pont/EcranFactures.cs, DemandeDe) -- la PREMIÈRE demande portée par le dossier ; dans la base, par le
/// numéro de gestion de son dossier RCS ou le numéro indicatif de la demande, la plus ancienne (DMND_ID_DMND). Le client : celui de la
/// facture de cette demande (FACTU_ID_CLIEN, comme LectureDemandes.cs) ; le bloc client de RIG v2 (OPE_FACTURE_CLIENT : abrégé, nom,
/// adresse, ult_lblSolde) -- l'adresse par défaut du client (rec.ClientAdresse, CLIENT_ADRESSE de RIG v2, ParDefaut), le solde Client.SoldeClient (Client.cs:1181-1195,
/// la formule de LectureEncaissements.cs).
/// </summary>
public static class LectureFactures
{
    private const string Requete = """
        SELECT TOP (1) d.DMND_NUM_DEMANDE, c.Id, c.Abrege, c.Nom,
               (SELECT TOP (1) CONCAT_WS(' ', NULLIF(a.LigneAdresse, ''), NULLIF(CONCAT_WS(' ', a.CodePostal, a.Localite), ''))
                FROM rec.ClientAdresse AS a WHERE a.IdClient = c.Id ORDER BY a.ParDefaut DESC, a.Id) AS Adresse,
               ROUND(CASE WHEN c.Type = 'C' AND c.EstComptePc = 0 AND s.CLISO_DATE_SOLDE IS NOT NULL
                          THEN s.CLISO_MONTANT
                               + ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = c.Id AND v.VENTI_DATE_VENTILATION >= s.CLISO_DATE_SOLDE AND v.VENTI_DATE_VENTILATION < @maintenant), 0)
                               - ISNULL((SELECT SUM(ff.FACTU_MONTANT_TTC) FROM v2.FACTURE AS ff WHERE ff.FACTU_ID_CLIEN = c.Id AND ff.FACTU_DATE_CREATION >= s.CLISO_DATE_SOLDE AND ff.FACTU_DATE_CREATION < @maintenant), 0)
                          ELSE ISNULL((SELECT SUM(v.VENTI_MONTANT) FROM v2.VENTILATION AS v WHERE v.VENTI_ID_CLIEN = c.Id AND v.VENTI_DATE_VENTILATION < @maintenant), 0)
                               - ISNULL((SELECT SUM(ff.FACTU_MONTANT_TTC) FROM v2.FACTURE AS ff WHERE ff.FACTU_ID_CLIEN = c.Id AND ff.FACTU_DATE_CREATION < @maintenant), 0) END, 2) AS Solde
        FROM v2.DEMANDE AS d
        LEFT JOIN rec.DossierRcs AS ds ON ds.Id = d.DMND_ID_DSRCS
        OUTER APPLY (SELECT TOP (1) f.FACTU_ID_CLIEN FROM v2.FACTURE AS f WHERE f.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE AND f.FACTU_ID_CLIEN IS NOT NULL
                     ORDER BY f.FACTU_NUMERO_FACTURE) AS fa
        LEFT JOIN rec.Client AS c ON c.Id = fa.FACTU_ID_CLIEN
        OUTER APPLY (SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
                     WHERE CLISO_ID_CLIEN = c.Id AND CLISO_DATE_SOLDE <= @maintenant ORDER BY CLISO_DATE_SOLDE DESC, CLISO_ID_CLISO DESC) AS s
        WHERE ds.NumeroGestion = @dossier OR d.DMND_NUM_INDICATIF = @dossier
        ORDER BY d.DMND_ID_DMND
        """;

    /// <summary>La demande du dossier <paramref name="dossier"/>, null s'il n'en porte aucune.</summary>
    public static async Task<DemandeDuDossier?> LireAsync(SqlConnection baseGreffe, string dossier, DateTime maintenant, CancellationToken ct)
    {
        await using var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@dossier", dossier.Trim()),
            new SqlParameter("@maintenant", maintenant));
        if (!await r.ReadAsync(ct)) return null;
        var avecClient = !r.IsDBNull(1);
        return new DemandeDuDossier(FormeLigne.Texte(r, "DMND_NUM_DEMANDE").Trim(), FormeLigne.Texte(r, "Abrege").Trim(), FormeLigne.Texte(r, "Nom").Trim(),
            FormeLigne.Texte(r, "Adresse").Trim(), avecClient ? CriteresConsultation.Decimal(r, "Solde") : null);
    }
}
