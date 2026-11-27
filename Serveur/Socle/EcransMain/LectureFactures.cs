using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Factures » (PROC_FACTURE, web/src/ecrans/EcranFactures.tsx) ouvert pour un dossier : la demande de ce dossier que sa facture
/// reprend, le client de sa facture et ses articles, lus dans la base V3 du greffe (v2.DEMANDE, v2.FACTURE, v2.ARTICLE_UTILISE,
/// rec.Client, rec.ClientAdresse, v2.CLIENT_SOLDE, v2.VENTILATION). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 12,
/// 27/11/2026, sur la marche du lot 17.
///
/// SOURCE : la règle de l'écran (Pont/EcranFactures.cs, DemandeDe) -- la PREMIÈRE demande portée par le dossier ; dans la base, par le
/// numéro de gestion de son dossier RCS ou le numéro indicatif de la demande, la plus ancienne (DMND_ID_DMND). Puis la facture comme
/// RIG v2 l'ouvre sur cette demande (PROC_FACTURE.cs:352-398, DoInitObject) :
///   - la demande a une facture (Facture.GetFactureByNumDemande, Facture.cs:468-471, la première -- ici par numéro) : son client
///     (FACTU_ID_CLIEN) et SES articles, ARTUT_NUMERO_FACTURE = la facture, par ARTUT_ID_ARTUT (Facture.cs:988-990) ;
///   - sinon, la demande est tarifée : le client du premier article tarifé de cette demande (PROC_FACTURE.cs:393-397), et TOUS les
///     articles non facturés de l'affaire pour ce client (CreateFactureFromTarification, Facture.cs:154-158 ;
///     GetArticleUtiliseTarifesByIdAffaireClient, ArticleUtilise.cs:194-226 : par ARTUT_NUM_DEMANDE) ;
///   - sinon, une facture vide (CreateEmptyFacture, PROC_FACTURE.cs:400-424) : aucun client, aucun article.
/// Le bloc client de RIG v2 (OPE_FACTURE_CLIENT : abrégé, nom, adresse, ult_lblSolde) -- l'adresse par défaut du client
/// (rec.ClientAdresse, CLIENT_ADRESSE de RIG v2, ParDefaut), le solde Client.SoldeClient (Client.cs:1181-1195, la formule de
/// LectureEncaissements.cs).
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
                               - ISNULL((SELECT SUM(ff.FACTU_MONTANT_TTC) FROM v2.FACTURE AS ff WHERE ff.FACTU_ID_CLIEN = c.Id AND ff.FACTU_DATE_CREATION < @maintenant), 0) END, 2) AS Solde,
               fa.FACTU_NUMERO_FACTURE, d.DMND_ID_AFFRE
        FROM v2.DEMANDE AS d
        LEFT JOIN rec.DossierRcs AS ds ON ds.Id = d.DMND_ID_DSRCS
        OUTER APPLY (SELECT TOP (1) f.FACTU_NUMERO_FACTURE, f.FACTU_ID_CLIEN FROM v2.FACTURE AS f WHERE f.FACTU_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                     ORDER BY f.FACTU_NUMERO_FACTURE) AS fa
        OUTER APPLY (SELECT TOP (1) t.ARTUT_ID_CLIEN FROM v2.ARTICLE_UTILISE AS t
                     WHERE fa.FACTU_NUMERO_FACTURE IS NULL AND t.ARTUT_ID_AFFRE = d.DMND_ID_AFFRE AND t.ARTUT_NUM_DEMANDE = d.DMND_NUM_DEMANDE
                       AND NULLIF(t.ARTUT_NUMERO_FACTURE, '') IS NULL AND t.ARTUT_ID_CLIEN IS NOT NULL
                     ORDER BY t.ARTUT_ID_ARTUT) AS ta
        LEFT JOIN rec.Client AS c ON c.Id = COALESCE(fa.FACTU_ID_CLIEN, ta.ARTUT_ID_CLIEN)
        OUTER APPLY (SELECT TOP (1) CLISO_DATE_SOLDE, CLISO_MONTANT FROM v2.CLIENT_SOLDE
                     WHERE CLISO_ID_CLIEN = c.Id AND CLISO_DATE_SOLDE <= @maintenant ORDER BY CLISO_DATE_SOLDE DESC, CLISO_ID_CLISO DESC) AS s
        WHERE ds.NumeroGestion = @dossier OR d.DMND_NUM_INDICATIF = @dossier
        ORDER BY d.DMND_ID_DMND
        """;

    // Les articles : ceux de la facture (@facture non vide), sinon les articles tarifés de l'affaire pour le client (@client). Le taux
    // de TVA est un REAL dans RIG v2 : converti en décimal par SQL, jamais par un flottant côté C#.
    private const string RequeteArticles = """
        SELECT a.ARTUT_ID_ARTUT, a.ARTUT_CODE_ARTICLE, a.ARTUT_LIBELLE, a.ARTUT_QUANTITE, ISNULL(a.ARTUT_PRIX_UHT, 0) AS PrixUht,
               CAST(ROUND(a.ARTUT_TAUX_TVA, 2) AS decimal(9, 2)) AS TauxTva, ISNULL(a.ARTUT_MONTANT_BODACC, 0) AS Bodacc,
               ISNULL(a.ARTUT_MONTANT_INPI, 0) AS Inpi, ISNULL(a.ARTUT_MONTANT_AUTRE, 0) AS Autre, a.ARTUT_MONTANT_TTC, a.ARTUT_COMMENTAIRE
        FROM v2.ARTICLE_UTILISE AS a
        WHERE (@facture <> '' AND a.ARTUT_NUMERO_FACTURE = @facture)
           OR (@facture = '' AND a.ARTUT_ID_AFFRE = @affaire AND a.ARTUT_ID_CLIEN = @client AND NULLIF(a.ARTUT_NUMERO_FACTURE, '') IS NULL)
        ORDER BY CASE WHEN @facture = '' THEN a.ARTUT_NUM_DEMANDE END, a.ARTUT_ID_ARTUT
        """;

    /// <summary>La demande du dossier <paramref name="dossier"/>, null s'il n'en porte aucune.</summary>
    public static async Task<DemandeDuDossier?> LireAsync(SqlConnection baseGreffe, string dossier, DateTime maintenant, CancellationToken ct)
    {
        DemandeDuDossier demande;
        int? client, affaire;
        await using (var r = await LectureV2.LireAsync(baseGreffe, Requete, ct, new SqlParameter("@dossier", dossier.Trim()),
            new SqlParameter("@maintenant", maintenant)))
        {
            if (!await r.ReadAsync(ct)) return null;
            client = r.IsDBNull(1) ? null : r.GetInt32(1);
            affaire = r.IsDBNull(r.GetOrdinal("DMND_ID_AFFRE")) ? null : r.GetInt32(r.GetOrdinal("DMND_ID_AFFRE"));
            demande = new DemandeDuDossier(FormeLigne.Texte(r, "DMND_NUM_DEMANDE").Trim(), FormeLigne.Texte(r, "Abrege").Trim(),
                FormeLigne.Texte(r, "Nom").Trim(), FormeLigne.Texte(r, "Adresse").Trim(), client is null ? null : CriteresConsultation.Decimal(r, "Solde"),
                FormeLigne.Texte(r, "FACTU_NUMERO_FACTURE").Trim());
        }
        // Ni facture ni tarification : la facture vide de RIG v2, sans article.
        if (demande.Facture.Length == 0 && (client is null || affaire is null)) return demande with { Articles = [] };

        var articles = new List<ArticleDeFacture>();
        await using (var r = await LectureV2.LireAsync(baseGreffe, RequeteArticles, ct, new SqlParameter("@facture", demande.Facture),
            new SqlParameter("@affaire", (object?)affaire ?? DBNull.Value), new SqlParameter("@client", (object?)client ?? DBNull.Value)))
        {
            while (await r.ReadAsync(ct))
                articles.Add(new ArticleDeFacture(r.GetInt32(0), FormeLigne.Texte(r, "ARTUT_CODE_ARTICLE").Trim(), FormeLigne.Texte(r, "ARTUT_LIBELLE").Trim(),
                    Convert.ToInt32(r.GetValue(3), System.Globalization.CultureInfo.InvariantCulture), r.GetDecimal(4), r.GetDecimal(5),
                    r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9), FormeLigne.Texte(r, "ARTUT_COMMENTAIRE")));
        }
        return demande with { Articles = [.. articles] };
    }
}
