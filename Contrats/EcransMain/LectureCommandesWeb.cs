namespace RigV3.Contrats.EcransMain;

// « Commandes web » (PROC_COMMANDES_WEB, web/src/ecrans/EcranCommandesWeb.tsx), écran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur :
// Serveur/Socle/EcransMain/LectureCommandesWeb.cs.

/// <summary>L'adresse de la lecture : GET, les commandes web reçues (<see cref="CommandesWebLues"/>).</summary>
public static class RoutesCommandesWeb
{
    public const string Commandes = "/ecrans-main/{greffe}/commandes-web";
}

/// <summary>
/// Une commande web (table COMMANDE_WEB) : identifiant, référence et numéro de suivi, date, famille (code de
/// FamilleCommandeWeb de NéoRIG), type de produit, origine (SL, IG, MAN), SIREN, dénomination, requérant, état (A, T, V, R,
/// S), motif et date de traitement ; la demande de la commande (CMDWB_ID_DMND) : son numéro et le numéro de gestion du dossier
/// qu'elle porte (DMND_NUM_INDICATIF, posé par « Valider », lot 7 du plan « RIG v3 complet »), vides sans demande.
/// </summary>
public sealed record CommandeWebLue(int Id, string Reference, string NumeroSuivi, DateTime? DateCommande, int Famille, string TypeProduit,
                                    string Origine, string Siren, string Denomination, string Requerant, string Etat, string Motif,
                                    DateTime? DateTraitement, string NumeroDemande = "", string DossierDemande = "");

/// <summary>Les commandes lues (au plus le plafond), de la plus ancienne à la plus récente, et leur total.</summary>
public sealed record CommandesWebLues(CommandeWebLue[] Lignes, int Total);
