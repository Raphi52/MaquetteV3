namespace RigV3.Contrats.EcransMain;

// « Actes » (PROC_ACTES_NUM ; web/src/ecrans/EcranActes.tsx), ecran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 6, 26/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureActes.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheActes"/>, rend <see cref="ActesLus"/>.</summary>
public static class RoutesActes
{
    public const string Actes = "/ecrans-main/{greffe}/actes";
}

/// <summary>
/// Les criteres de PROC_ACTES_NUM (OPE_RESULTAT.ChargerDonnees) : le numero de dossier ou de depot, les abreges de piece
/// (synonymes), la categorie de depot, la periode de depot, « sans document » (ACTDP_SANS_DOCUMENT = 1 au lieu de 0) et le
/// nombre maximal de lignes.
/// </summary>
public sealed record RechercheActes(string Numero, string[] Abreges, string Categorie, DateTime? Du, DateTime? Au, bool SansDocument, int Plafond);

/// <summary>Une categorie de depot hors comptes annuels (CATEGORIE_DEPOT) : son code et son libelle d'ecran.</summary>
public sealed record CategorieDepotLue(string Code, string Libelle);

/// <summary>Un acte depose non encore range dans la GED (ACTE_DEPOSE), avec son depot, son dossier et sa piece.</summary>
public sealed record ActeLu(int Id, string NumeroGestion, string NumeroDepot, string Designation, string Categorie, string CategorieLibelle,
                            string Abrege, string Description, DateTime? DateDepot, DateTime? DateActe, bool Demat, bool SansDocument);

/// <summary>Les actes lus (au plus le plafond), leur total et les categories du critere.</summary>
public sealed record ActesLus(ActeLu[] Lignes, int Total, CategorieDepotLue[] Categories);
