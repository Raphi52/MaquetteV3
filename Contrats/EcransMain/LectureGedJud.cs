namespace RigV3.Contrats.EcransMain;

// « GED judiciaire » (PROC_GEDJUD, web/src/ecrans/EcranGedJud.tsx), écran fait main lu dans la base du greffe (plan « RIG v3
// 100 % fonctionnel », lot 11, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureGedJud.cs.

/// <summary>L'adresse de la lecture : GET, les documents de la GED judiciaire (<see cref="DocumentsGedLus"/>).</summary>
public static class RoutesGedJud
{
    public const string Documents = "/ecrans-main/{greffe}/ged-judiciaire";
}

/// <summary>
/// Un document de la GED judiciaire de RIG v2 (une ligne de VIDIGED_INDEX) : son libellé, l'entité qui le porte (type et
/// numéro), sa nature et son état (libellés de VIDIGED_PARAMETRAGE), sa date de réception, son commentaire et son motif de
/// rejet ; le nombre de décisions judiciaires liées (VIDIGED_DJ) et la date rendue et prévue de la première.
/// </summary>
public sealed record DocumentGedLu(int Id, string Libelle, string TypeEntite, string Entite, string Nature, string CodeEtat, string Etat,
    DateTime? Reception, string Commentaire, string MotifRejet, int DecisionsLiees, DateTime? DateDecision, DateTime? DatePrevue);

/// <summary>Les documents lus (au plus le plafond), les plus récemment reçus d'abord, et leur total.</summary>
public sealed record DocumentsGedLus(DocumentGedLu[] Lignes, int Total);
