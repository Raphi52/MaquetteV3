namespace RigV3.Contrats.EcransMain;

// « Encaissements » (PROC_ENCAISS, web/src/ecrans/EcranEncaissements.tsx), écran fait main lu dans la base du greffe (plan « RIG v3
// 100 % fonctionnel », lot 12, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureEncaissements.cs.

/// <summary>L'adresse de la lecture : GET, l'encaissement de départ pour les factures d'un dossier (<see cref="EncaissementDeDepart"/>).</summary>
public static class RoutesEncaissements
{
    public const string Depart = "/ecrans-main/{greffe}/encaissements/{dossier}";
}

/// <summary>
/// Une ventilation de départ (Ventilation.CreateVentilation de PROC_ENCAISS) : l'affaire et son libellé, le client (nom, code
/// abrégé), le montant à ventiler (l'opposé du solde de l'affaire pour ce client, Affaire.GetSolde(client)), et les soldes que montre la
/// grille : celui de l'affaire (Affaire.GetSolde) et celui du client (Client.SoldeClient) -- signés comme dans RIG v2 (négatif = dû).
/// </summary>
public sealed record VentilationLue(int IdAffaire, string Affaire, int IdClient, string Client, string Abrege, decimal Montant,
    decimal SoldeAffaire, decimal SoldeClient);

/// <summary>L'encaissement de départ : le payeur (le nom du client de la première facture), le montant (la somme des ventilations), les
/// factures du dossier retenues, et les ventilations, une par couple (affaire, client) au solde non nul.</summary>
public sealed record EncaissementDeDepart(string Payeur, decimal Montant, string[] Factures, VentilationLue[] Ventilations);
