namespace RigV3.Contrats.EcransMain;

// « Clients » (PROC_GCLIENT, web/src/ecrans/EcranClients.tsx) et « Fiche client » (PROC_FCLIENT, web/src/ecrans/EcranFicheClient.tsx),
// écrans faits main lus dans la base du greffe (plan « RIG v3 100 % fonctionnel », lot 14, 27/11/2026 ; marche du lot 17). Les
// lecteurs : Serveur/Socle/EcransMain/LectureClients.cs.

/// <summary>Les adresses : GET la liste des clients ; GET la fiche d'un client (<c>{id}</c>).</summary>
public static class RoutesClients
{
    public const string Clients = "/ecrans-main/{greffe}/clients";
    public const string Fiche = "/ecrans-main/{greffe}/clients/{id}";
}

/// <summary>Un client en compte (la grille d'OPE_RESULTAT de PROC_GCLIENT) : id, compte, abrégé, nom, groupe, statut (A, B, S) et type.</summary>
public sealed record ClientLu(int Id, string Compte, string Abrege, string Nom, string Groupe, string Statut, string Type);

/// <summary>Les clients lus (au plus le plafond), par abrégé, et leur total.</summary>
public sealed record ClientsLus(ClientLu[] Lignes, int Total);

/// <summary>Un solde du compte (CLIENT_SOLDE : la grille _ultDataGridViewLatestBalance, date et solde) ; date aaaa-mm-jj, montant en centimes.</summary>
public sealed record SoldeClientLu(string Date, long Solde);

/// <summary>Un contact du client (CLIENT_CONTACT, OPE_CONTACT).</summary>
public sealed record ContactClientLu(string Nom, string Fonction, string Telephone, string Mail);

/// <summary>
/// La fiche d'un client (PROC_FCLIENT : identification, communication, paramétrage, informations). Dates jj/mm/aaaa ; provision en
/// centimes ; la synchronisation Infogreffe comme OPE_INFORMATIONS la dit (« En attente de transmission », une date, « Non transmis »).
/// </summary>
public sealed record FicheClientLue(int Id, string Abrege, string Nom, string Compte, string Siren, string Adresse, string Type, string EtatCompte,
                                   string Groupe, string Naissance, string Mail, string TelPrincipal, string TelPortable, string ModePaiement,
                                   long Provision, bool ReleveFacture, string FactuPremiere, string FactuDerniere, string EncaissPremier,
                                   string EncaissDernier, int NbFactures, int NbEncaissements, string DerniereModif, string SynchroInfogreffe,
                                   string Notes, SoldeClientLu[] Soldes, ContactClientLu[] Contacts);
