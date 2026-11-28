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

/// <summary>
/// Le geste « modifier un document » de la GED judiciaire (PROC_GED_MODIF, ETP_GED_MODIF : POST <see cref="Modifier"/>, GesteEcran Ids =
/// [son id], valeurs sous les titres de RIG v2 -- operations « Etat » (champs « Etat », « Motif rejet », OPE_GED_MODIF_ETAT.Designer.cs:71-90)
/// et « Commentaire » (champ « Libellé », OPE_GED_MODIF_COMMENTAIRE.Designer.cs:67-75)). Ecrit par
/// Serveur/Socle/EcransMain/EcritureGedJud.cs (lot 8 du plan « parcours complet », 28/11/2026).
/// </summary>
/// <summary>Un etat de la liste « Etat » du document : son code et son libelle.</summary>
public sealed record EtatGedLu(string Code, string Libelle);

public static class ModificationDocumentGed
{
    public const string Modifier = "/ecrans-main/{greffe}/ged-judiciaire/modifier";

    /// <summary>GET, les etats de la liste « Etat » (VIDIGED_PARAMETRAGE de type ETAT, ULT_COMBO_ETAT.cs:17-26) : <see cref="EtatGedLu"/>[].</summary>
    public const string Etats = "/ecrans-main/{greffe}/ged-judiciaire/etats";

    public const string SectionEtat = "Etat";
    public const string Etat = "Etat";
    public const string MotifRejet = "Motif rejet";
    public const string SectionCommentaire = "Commentaire";
    public const string Commentaire = "Libellé";
}
