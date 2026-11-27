namespace RigV3.Contrats.EcransMain;

// « Statistiques » (PROC_STAT, web/src/ecrans/EcranStatistiques.tsx), écran fait main CALCULÉ dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 9, 27/11/2026 ; marche du lot 17). Le calcul : Serveur/Socle/EcransMain/LectureStatistiques.cs.

/// <summary>L'adresse du calcul : POST, un volet et sa période (<see cref="RechercheStatistiques"/>) -> <see cref="StatistiquesLues"/>.</summary>
public static class RoutesStatistiques
{
    public const string Statistiques = "/ecrans-main/{greffe}/statistiques";
}

/// <summary>Les six volets de PROC_STAT (OPE_STATS_INTERNES.Designer.cs, OPE_STATS_SEMESTRIELLES), au mot pres ceux de l'ecran.</summary>
public static class VoletsStatistiques
{
    public const string Internes = "Internes", Web = "Web", RepartitionsJcOp = "RepartitionsJcOp", ActivitesCa = "ActivitesCa",
                        ActivitesSalaries = "ActivitesSalaries", Semestrielles = "Semestrielles";
}

/// <summary>
/// Ce que l'écran demande : le volet de RIG v2 (« Internes », « Web », « RepartitionsJcOp », « ActivitesCa »,
/// « ActivitesSalaries », « Semestrielles »), la période (OPE_DATES : du, au) et, pour les deux volets d'activités, le niveau
/// NAF (OPE_NIVEAU : « 1 » à « 4 »).
/// </summary>
public sealed record RechercheStatistiques(string Volet, DateTime Debut, DateTime Fin, string? Niveau);

/// <summary>Une ligne d'un tableau : son libellé, ses cellules dans l'ordre des colonnes (après la première).</summary>
public sealed record LigneStatistique(string Libelle, string[] Valeurs);

/// <summary>
/// Un tableau du volet, celui d'une partie du classeur de RIG v2 : son titre, ses colonnes (la première est celle du libellé),
/// ses lignes ; <paramref name="Cause"/> dit pourquoi il n'est pas calculé (une table absente de la base V3...), sans ligne.
/// </summary>
public sealed record TableauStatistique(string Titre, string[] Colonnes, LigneStatistique[] Lignes, string? Cause = null);

/// <summary>
/// Le volet calculé : ses tableaux ; <paramref name="Refus"/>, le message de RIG v2 quand la période est refusée (rien n'est
/// calculé) ; <paramref name="NombrePc"/>, le nombre de PC de la période que le volet semestriel affiche (ult_LblNbPc).
/// </summary>
public sealed record StatistiquesLues(string Volet, TableauStatistique[] Tableaux, string? Refus = null, string? NombrePc = null);
