namespace RigV3.Contrats.EcransMain;

// « Documents dématérialisés » (PROC_DOC_DEMAT, web/src/ecrans/EcranDocDemat.tsx), écran fait main lu dans la base du greffe
// (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureDocDemat.cs.

/// <summary>L'adresse de la lecture : GET, les pièces dématérialisées (<see cref="PiecesDematLues"/>).</summary>
public static class RoutesDocDemat
{
    public const string Pieces = "/ecrans-main/{greffe}/pieces-dematerialisees";
}

/// <summary>
/// Une pièce dématérialisée de RIG v2 (une ligne de DEMAT_CREFO_PJ) : le dossier de sa demande (numéro indicatif, sinon le
/// numéro de demande), le nom de la demande, la démat qui la porte (« Liasse CFE » ou « DAS »), l'abrégé et le libellé de son
/// type (TYPE_PJ_CREFO), sa date de pièce, son nom de fichier, si elle est supprimée et l'état Infogreffe.
/// </summary>
public sealed record PieceDematLue(int Id, string Dossier, string Demande, string Demat, string Abrege, string Libelle, DateTime? DatePiece,
    string Fichier, bool Supprimee, string EtatInfogreffe);

/// <summary>Les pièces lues (au plus le plafond), par dossier puis dans l'ordre de la démat, et leur total.</summary>
public sealed record PiecesDematLues(PieceDematLue[] Lignes, int Total);
