namespace RigV3.Contrats.EcransMain;

// « Coffre-fort » (PROC_COFFRE_FORT, web/src/ecrans/EcranCoffreFort.tsx), écran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureCoffreFort.cs.

/// <summary>L'adresse de la lecture : GET, l'historique des dépôts au coffre-fort (<see cref="DepotsCoffreLus"/>).</summary>
public static class RoutesCoffreFort
{
    public const string Depots = "/ecrans-main/{greffe}/coffre-fort";
}

/// <summary>
/// Un dépôt au coffre-fort électronique (DEMAT_CFEC de RIG v2), aux colonnes de l'historique (OPE_RESULTAT_HISTORIQUE) : son
/// identifiant, l'entité (numéro d'instance), la nature du document, le destinataire (type, abrégé et nom du mandataire),
/// l'état (code et libellé de DEMAT_ETAT, ou l'erreur), le nom du document dans le coffre, l'identifiant du dépôt et les dates
/// du suivi.
/// </summary>
public sealed record DepotCoffreLu(int Id, string Entite, string TypeEntite, string Nature, string TypeDestinataire, string AbregeDestinataire,
                                   string NomDestinataire, string Etat, string LibelleEtat, string Erreur, string NomDocument,
                                   string IdentifiantDepot, DateTime? TraitementRig, DateTime? ReceptionAr, DateTime? Depot,
                                   DateTime? AccuseReception, DateTime? Lecture);

/// <summary>Les dépôts lus (au plus le plafond), dans l'ordre de RIG v2, et leur total.</summary>
public sealed record DepotsCoffreLus(DepotCoffreLu[] Lignes, int Total);
