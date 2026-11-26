namespace RigV3.Contrats.EcransMain;

// « Dossiers verrouillés » (PROC_VERROUS, web/src/ecrans/EcranVerrous.tsx), écran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 9, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureVerrous.cs.

/// <summary>L'adresse de la lecture : GET, les demandes qui verrouillent (<see cref="VerrousLus"/>).</summary>
public static class RoutesVerrous
{
    public const string Verrous = "/ecrans-main/{greffe}/verrous";
}

/// <summary>
/// Une demande de RIG v2 qui verrouille ses objets : son dossier (numéro indicatif), son nom, son numéro, son traitement, son
/// état (« En cours » ou le libellé de ETAT_DEMANDE), qui la tient (DMND_NOM_UTILISATEUR) et sa date de création.
/// </summary>
public sealed record VerrouLu(string Dossier, string Nom, string Demande, string Traitement, string Etat, string Utilisateur, DateTime? Creation);

/// <summary>Les demandes lues (au plus le plafond), de la plus ancienne à la plus récente, et leur total.</summary>
public sealed record VerrousLus(VerrouLu[] Lignes, int Total);
