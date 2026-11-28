namespace RigV3.Contrats.EcransMain;

// « Archivage / radiation » (PROC_ARCHIRAD, web/src/ecrans/EcranArchivage.tsx), écran fait main lu dans la base du greffe (plan
// « RIG v3 100 % fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureArchivage.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheArchivage"/>, rend <see cref="DossiersArchivageLus"/>.</summary>
public static class RoutesArchivage
{
    public const string Dossiers = "/ecrans-main/{greffe}/archivage";

    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Agir, Ids vide) : « Préparer le bordereau » de RIG v2,
    /// « Un seul bordereau de numérisation par dossier » (PROC_ARCHIRAD.traitementBordereauUnique, PROC_ARCHIRAD.cs:155-217) sur les
    /// dossiers de la recherche (<see cref="ChampDu"/>, <see cref="ChampAu"/>, <see cref="ChampType"/>, ou <see cref="ChampListe"/>) ;
    /// rend un <c>ResultatGeste</c> dont Ids = les dossiers dotés de leur bordereau (422 : le refus de RIG v2). Écriture :
    /// Serveur/Socle/EcransMain/EcritureArchivage.cs (lot 11 du plan du parcours complet).
    /// </summary>
    public const string Bordereau = "/ecrans-main/{greffe}/archivage/bordereau";

    /// <summary>Les clés des critères dans les Valeurs du geste (dates aaaa-mm-jj ; la liste : des numéros séparés par des espaces).</summary>
    public const string ChampDu = "Date mini", ChampAu = "Date maxi", ChampType = "Type RCS", ChampListe = "Liste";

    /// <summary>Le plafond de RIG v2 : « Nombre de dossiers (max 150) » ; au-delà, VALIDER reste grisé (OPE_ARCHRSEL.cs:98-102).</summary>
    public const int MaxBordereau = 150;

    /// <summary>Les types RCS de la liste de RIG v2 (OPE_ARCHRSEL.cs:63-68), pour le serveur et l'ecran.</summary>
    public static readonly IReadOnlyList<string> TypesRcs = ["A", "B", "C", "D", "AC", "I"];
}

/// <summary>
/// Les critères d'OPE_ARCHRSEL : les dates de radiation (obligatoires) et le type RCS (A, B, C, D, AC ou I, obligatoire), ou la
/// liste de numéros de gestion (mode « liste », séparés par des espaces).
/// </summary>
public sealed record RechercheArchivage(DateOnly? Du, DateOnly? Au, string? TypeRcs, string? Liste);

/// <summary>Un dossier RCS radié, sans « Historique des FORMALITES » en GED : son numéro, sa désignation, son type et sa radiation.</summary>
public sealed record DossierArchivageLu(int Id, string Numero, string Designation, string TypeRcs, DateTime? Radiation);

/// <summary>
/// Les dossiers trouvés (au plus le plafond), dans l'ordre de RIG v2, et leur total ; <paramref name="Motif"/> : pourquoi rien
/// n'est cherché (un critère obligatoire manque), null sinon.
/// </summary>
public sealed record DossiersArchivageLus(DossierArchivageLu[] Lignes, int Total, string? Motif);
