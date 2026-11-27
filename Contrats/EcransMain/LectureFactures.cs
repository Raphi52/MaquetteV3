namespace RigV3.Contrats.EcransMain;

// « Factures » (PROC_FACTURE, web/src/ecrans/EcranFactures.tsx), écran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 12, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureFactures.cs.

/// <summary>L'adresse de la lecture : GET, la demande d'un dossier que sa facture reprend (<see cref="DemandeDuDossierLue"/>).</summary>
public static class RoutesFactures
{
    public const string Demande = "/ecrans-main/{greffe}/factures/{dossier}";
}

/// <summary>
/// La demande d'un dossier, ce que sa facture reprend : le numéro de la demande, et le client de sa facture s'il en a une (code abrégé,
/// nom, adresse par défaut rec.ClientAdresse, solde Client.SoldeClient -- vide et null sans client) ; le numéro de sa facture (vide si
/// elle n'est que tarifée ou sans facture) et ses articles, ceux que la grille de RIG v2 charge (OPE_FACTURE_ARTICLES).
/// </summary>
public sealed record DemandeDuDossier(string Numero, string Abrege, string Nom, string Adresse, decimal? Solde,
                                      string Facture = "", ArticleDeFacture[]? Articles = null);

/// <summary>
/// Un article de la facture, une ligne de v2.ARTICLE_UTILISE telle que la grille de RIG v2 la montre (colonnes colCodeArtut à colTTC,
/// FormFacture/ArticlesDataGridView.cs:1041) : le prix UHT est celui d'UNE unité, les débours (BODACC, INPI, Autre) et le TTC ceux de
/// la LIGNE -- TTC = PUHT × (1 + TVA) × quantité + débours (RecalculTTCLigne, ArticlesDataGridView.cs:597-622). Montants exacts (money).
/// </summary>
public sealed record ArticleDeFacture(int Id, string Code, string Libelle, int Quantite, decimal PrixUht, decimal TauxTva,
                                      decimal Bodacc, decimal Inpi, decimal Autre, decimal Ttc, string Commentaire);

/// <summary>La réponse de la route : la demande du dossier, null s'il n'en porte aucune (un corps vide ne se lit pas côté pont).</summary>
public sealed record DemandeDuDossierLue(DemandeDuDossier? Demande);
