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
/// nom, adresse par défaut rec.ClientAdresse, solde Client.SoldeClient -- vide et null sans client).
/// </summary>
public sealed record DemandeDuDossier(string Numero, string Abrege, string Nom, string Adresse, decimal? Solde);

/// <summary>La réponse de la route : la demande du dossier, null s'il n'en porte aucune (un corps vide ne se lit pas côté pont).</summary>
public sealed record DemandeDuDossierLue(DemandeDuDossier? Demande);
