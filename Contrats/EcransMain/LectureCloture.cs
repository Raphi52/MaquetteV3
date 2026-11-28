namespace RigV3.Contrats.EcransMain;

// « Clôture » (PROC_CLOT, web/src/ecrans/EcranCloture.tsx), écran fait main lu dans la base du greffe (plan « RIG v3 100 %
// fonctionnel », lot 20, 27/11/2026 ; marche du lot 17). Le lecteur : Serveur/Socle/EcransMain/LectureCloture.cs.

/// <summary>L'adresse de la lecture : POST <see cref="RechercheCloture"/>, rend <see cref="AffairesClotureLues"/>.</summary>
public static class RoutesCloture
{
    public const string Affaires = "/ecrans-main/{greffe}/cloture";

    /// <summary>
    /// POST un geste (<c>RigV3.Contrats.EcransGeneres.GesteEcran</c>, Type = Agir, Ids = les PC cochées) : le mode « Courrier aux
    /// débiteurs » de RIG v2 (eModeCloture.COURRIER_DEBITEURS, OPE_SELECT_PC.cs:1761-1775) -- l'évènement survenu du réglage
    /// CLOT_EVE_PRO_INCITR sur chaque PC, sa demande et son courrier ; rend un <c>ResultatGeste</c> dont Ids = les demandes créées
    /// (422 : le refus de RIG v2). Écriture : Serveur/Socle/EcransMain/EcritureCloture.cs (lot 11 du plan du parcours complet).
    /// </summary>
    public const string CourrierDebiteurs = "/ecrans-main/{greffe}/cloture/courrier-debiteurs";

    /// <summary>La clé de la facturation dans les Valeurs du geste (« oui » : le bouton FACTURATION de RIG v2 enfoncé).</summary>
    public const string ChampFacturer = "Facturation";

    /// <summary>
    /// POST un geste (<c>GesteEcran</c>, Type = Creer, Ids = les PC cochées) : le mode « Enrôlement sur saisine d'office » de RIG v2
    /// (eModeCloture.ENROLEMENT_SUR_SAISINE_OFFICE, le mode que RIG v2 présélectionne depuis l'alerte : OPE_EXAMEN_CLOT.cs:57-59 ;
    /// traitements OPE_SELECT_PC.cs:1776-1899) -- par PC, une instance de clôture numérotée, sa demande « Enrôl sur SO », son appel à
    /// l'audience choisie pour le liquidateur, l'évènement de convocation et ses éditions, le 4CLO de la PC survenu. Valeurs : l'audience
    /// (<see cref="ChampDateAudience"/>, <see cref="ChampHeureAudience"/>), <see cref="ChampClassement"/>, <see cref="ChampHeureAppel"/>,
    /// <see cref="ChampFacturer"/>. Rend un <c>ResultatGeste</c> dont Ids = les instances créées (422 : le refus de RIG v2). Écriture :
    /// Serveur/Socle/EcransMain/EcritureCloture.cs (lot 11 du plan du parcours complet, refus du chef du 28/11/2026).
    /// </summary>
    public const string EnrolementSo = "/ecrans-main/{greffe}/cloture/enrolement-so";

    /// <summary>POST <see cref="RechercheCloture"/> : les instances de clôture enrôlées (<see cref="EnrolementsClotureLus"/>), l'onglet
    /// « Enrôlement / requête » de l'écran -- les colonnes de l'étape « Résultats » de RIG v2 (OPE_RESULTATS.cs:267-299).</summary>
    public const string Enrolements = "/ecrans-main/{greffe}/cloture/enrolements";

    /// <summary>Les champs de la ligne du liquidateur dans la grille d'enrôlement de RIG v2 (colDateAudience, colHeureAudience,
    /// colTypeClassement, colHeureAppel : OPE_SELECT_PC.cs:345-397).</summary>
    public const string ChampDateAudience = "Date d'audience", ChampHeureAudience = "Heure d'audience", ChampClassement = "Type de classement",
                        ChampHeureAppel = "Heure d'appel";
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
/// et ses liquidateurs en mission ; <paramref name="DernierCourrier"/>, la date du dernier évènement du courrier aux débiteurs
/// (réglage CLOT_EVE_PRO_INCITR : la colonne « Dernier courrier envoyé » de RIG v2, OPE_SELECT_PC.cs:678-682), null sans courrier.
/// </summary>
public sealed record AffaireClotureLue(int Id, string NumPc, string NomAffaire, string Dossier, DateTime? LimiteExamen,
                                       bool AuForfait, string Liquidateurs, DateTime? DernierCourrier = null);

/// <summary>Les procédures trouvées (au plus le plafond), par numéro de PC, et leur total.</summary>
public sealed record AffairesClotureLues(AffaireClotureLue[] Lignes, int Total);

/// <summary>
/// Une instance de clôture enrôlée (étape « Résultats » de RIG v2, OPE_RESULTATS.cs:267-299) : son n° de rôle et de répertoire, la PC,
/// le premier demandeur et le premier défendeur (« [n] » devant s'il y en a plusieurs, :278), l'acte de saisine (son libellé), la date
/// d'enrôlement, l'audience de son premier appel (date, heure, chambre) et son type de classement et heure d'appel ; la date du dernier
/// évènement de convocation de l'instance (<paramref name="Convocation"/>, son code) ; la date à laquelle le 4CLO de la PC est survenu.
/// </summary>
public sealed record EnrolementClotureLu(int IdInstance, string NumRole, string NumRepGeneral, int IdPc, string NumPc, string Demandeur,
                                         string Defendeur, string ActeSaisine, DateTime? Enrolement, DateTime? Audience, string HeureAudience,
                                         string Chambre, string TypeClassement, string HeureAppel, string Convocation, DateTime? DateConvocation,
                                         DateTime? ExamenSurvenu);

/// <summary>Les instances de clôture enrôlées (au plus le plafond), les plus récentes d'abord, et leur total.</summary>
public sealed record EnrolementsClotureLus(EnrolementClotureLu[] Lignes, int Total);
