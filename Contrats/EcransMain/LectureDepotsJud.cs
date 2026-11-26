namespace RigV3.Contrats.EcransMain;

// « Dépôts judiciaires » (PROC_DEPOTJUD, web/src/ecrans/EcranDepotsJud.tsx), ecran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 5, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureDepotsJud.cs.

/// <summary>L'adresse de la lecture : GET, les instances ou deposer (<see cref="InstancesDepotLues"/>).</summary>
public static class RoutesDepotsJud
{
    public const string Instances = "/ecrans-main/{greffe}/depots-jud";
}

/// <summary>
/// Une instance de la recherche de PROC_DEPOTJUD : son numero de role, son nom courant, sa nature d'affaire, sa date
/// d'enrolement, son dernier evenement survenu (libelle et date), la demande qui la tient (vide si libre) et si la
/// facturation s'affiche (aucun client sur ses demandes).
/// </summary>
public sealed record InstanceDepotLue(string Numero, string Denomination, string Nature, DateTime? Enrolement, string DernierEvenement,
                                      DateTime? DateEvenement, string DemandeEnCours, bool AFacturer);

/// <summary>Les instances lues (au plus le plafond) et leur total.</summary>
public sealed record InstancesDepotLues(InstanceDepotLue[] Lignes, int Total);
