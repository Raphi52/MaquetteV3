namespace RigV3.Contrats.EcransMain;

// « Clôture » (PROC_CLOT, web/src/ecrans/EcranCloture.tsx), écran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureCloture.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheCloture"/>, rend <see cref="AffairesClotureLues"/>.</summary>
public static class RoutesCloture
{
    public const string Affaires = "/ecrans-main/{greffe}/cloture";
}

/// <summary>
/// Les dates de la recherche de RIG v2 (ultDateDeb, ultDateFin d'OPE_SELECT_PC) : la limite de l'examen de clôture entre
/// <paramref name="Du"/> et <paramref name="Au"/>, ou avant <paramref name="Au"/> sans <paramref name="Du"/> ; sans
/// <paramref name="Au"/>, aucune borne (RIG v2 grise alors la recherche).
/// </summary>
public sealed record RechercheCloture(DateOnly? Du, DateOnly? Au);

/// <summary>
/// Une procédure collective à examiner pour la clôture (grille « Sélection PC » de RIG v2) : son numéro, le nom de l'affaire,
/// le dossier RCS, la limite de l'examen (l'évènement 4CLO futur), « au forfait » (PRCLL_FORFAIT_FACTURE : jamais facturée)
/// et ses liquidateurs en mission.
/// </summary>
public sealed record AffaireClotureLue(int Id, string NumPc, string NomAffaire, string Dossier, DateTime? LimiteExamen,
                                       bool AuForfait, string Liquidateurs);

/// <summary>Les procédures trouvées (au plus le plafond), par numéro de PC, et leur total.</summary>
public sealed record AffairesClotureLues(AffaireClotureLue[] Lignes, int Total);
