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
                                          DateTime? DateSuppression, DateTime? DateDiffusion, bool NonDisponibleWeb)
{
    /// <summary>Les CODES des criteres de la recherche (<see cref="RechercheAudiencesSupprimees"/>, plan « parcours complet », lot 9) :
    /// la chambre (AUDNCS_CHAMBRE), le code cabinet (AUDNCS_CODE_CABINET) et le juge (AUDNCS_ID_JUG) de la ligne.</summary>
    public string CodeChambre { get; init; } = "";
    public string CodeCabinetSeul { get; init; } = "";
    public int? IdJuge { get; init; }
}

/// <summary>Les lignes lues (au plus le plafond) et leur total ; <see cref="Message"/> : pourquoi la recherche n'a rien cherche.</summary>
public sealed record AudiencesSupprimeesLues(AudienceSupprimeeLue[] Lignes, int Total)
{
    public string? Message { get; init; }
}

/// <summary>
/// La RECHERCHE de PROC_GESTAUDS (OPE_RECHERCHE_GESTAUDS._Rechercher), appliquee par le serveur (POST
/// <see cref="RoutesRechercheAudiencesSupprimees.Rechercher"/>, plan « parcours complet », lot 9, 28/11/2026) : le mode (cabinets ou
/// audiences), les dates d'audience et de suppression (aaaa-mm-jj), le type, la chambre, la section, le juge (son numero), le code
/// cabinet, UN type de classement (code), et le nombre maximum de lignes.
/// </summary>
public sealed record RechercheAudiencesSupprimees(bool Cabinets, string? AudienceDu = null, string? AudienceAu = null, string? SuppressionDu = null,
                                                  string? SuppressionAu = null, string? Type = null, string? Chambre = null, string? Section = null,
                                                  int? Juge = null, string? CodeCabinet = null, string? TypeClassement = null, int Max = 200);

/// <summary>L'adresse de la recherche : POST <see cref="RechercheAudiencesSupprimees"/> -> <see cref="AudiencesSupprimeesLues"/>.</summary>
public static class RoutesRechercheAudiencesSupprimees
{
    public const string Rechercher = "/ecrans-main/{greffe}/audiences-supprimees/recherche";
}
