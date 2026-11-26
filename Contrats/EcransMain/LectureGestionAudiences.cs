namespace RigV3.Contrats.EcransMain;

// « Gestion des audiences » (PROC_GESTAUDS, web/src/ecrans/EcranGestionAudiences.tsx), ecran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/
// LectureGestionAudiences.cs.

/// <summary>L'adresse de la lecture : GET, les audiences et cabinets supprimes (<see cref="AudiencesSupprimeesLues"/>).</summary>
public static class RoutesGestionAudiences
{
    public const string Supprimees = "/ecrans-main/{greffe}/audiences-supprimees";
}

/// <summary>
/// Une ligne de la liste de PROC_GESTAUDS (une audience ou un cabinet SUPPRIME, AUDIENCE_CABINET_SUPPRIMEE) : le mode, la date et
/// l'heure, le type, le code cabinet (« code - libelle »), le juge et le greffier d'un cabinet, la chambre (« code - libelle ») et
/// la section d'une audience, les types de classement admis, publique, l'auteur et la date de la suppression, la date de sa
/// diffusion (envoi a Infogreffe), « non disponible sur le web ».
/// </summary>
public sealed record AudienceSupprimeeLue(bool Cabinet, DateTime? Date, string Heure, string Type, string CodeCabinet, string Juge, string Greffier,
                                          string Chambre, string Section, string TypeClassement, bool Publique, string AuteurSuppression,
                                          DateTime? DateSuppression, DateTime? DateDiffusion, bool NonDisponibleWeb);

/// <summary>Les lignes lues (au plus le plafond) et leur total.</summary>
public sealed record AudiencesSupprimeesLues(AudienceSupprimeeLue[] Lignes, int Total);
