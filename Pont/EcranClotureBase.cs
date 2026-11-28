// fix-ok: pas un correctif -- lot 11 du plan-f4fb097f (28/11/2026) : le geste « Courrier aux débiteurs » (mesuré au 28/11 : PROC_CLOT
// « geste non prouvé ») et ce qu'il relit (Id de la PC, dernier courrier) ; puis, refus du chef du 28/11 (« le courrier est secondaire »),
// le geste principal « Enrôlement sur saisine d'office » et sa relecture (records, doc, méthodes) : trois retouches d'un même ajout.
using System.Globalization;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>Une ligne de l'ecran (web/src/ecrans/EcranCloture.tsx, LigneClot) ; dates aaaa-mm-jj ; vide = non lu.</summary>
/// <param name="AuForfait">PRCLL_FORFAIT_FACTURE ; null en demonstration (l'ecran garde ses forfaits d'exemple).</param>
/// <param name="Id">L'identifiant de la PC en base, que le courrier aux debiteurs envoie ; null en demonstration.</param>
public sealed record LigneCloture(string Role, string Affaire, string Demandeur, string Defendeur, string Chambre, string Audience,
                                  string HeureAppel, string Classement, string LimiteExamen, bool AFacturer, string DernierCourrier,
                                  string Dossier, string NumPc, string HeureDebut, string Dirigeants, string Liquidateurs, string Forfait,
                                  string Huissier, string CodePostalVille, string ActeSaisine, string Depot, string Facturation,
                                  bool? AuForfait, int? Id = null);

/// <summary>Une instance de clôture enrôlée (onglet « Enrolement / requete », web/src/ecrans/EcranCloture.tsx, LigneEnrol) ; dates
/// aaaa-mm-jj, vide = non lu. Les colonnes de l'étape « Résultats » de RIG v2 (OPE_RESULTATS.cs:267-299) et ce que l'enrôlement a posé.</summary>
public sealed record LigneEnrolementCloture(int IdInstance, string Role, string Repertoire, int IdPc, string NumPc, string Demandeur,
                                            string Defendeur, string ActeSaisine, string Enrolement, string Audience, string HeureAudience,
                                            string Chambre, string Classement, string HeureAppel, string Convocation, string DateConvocation,
                                            string ExamenSurvenu);

/// <summary>Les instances de clôture enrôlées lues, et d'où (Pont/LectureEcranMain.cs) ; vide en démonstration.</summary>
public sealed record VueEnrolementsCloture(string Mode, string? Message, LigneEnrolementCloture[] Lignes, int Total);

/// <summary>Ce que l'ecran Cloture affiche, et d'ou (Pont/LectureEcranMain.cs).</summary>
/// <param name="Aujourdhui">La date qui dit si une limite d'examen est depassee : la vraie en base, celle du jeu sinon.</param>
public sealed record VueCloture(string Mode, string? Message, LigneCloture[] Lignes, int Total, string Aujourdhui);

/// <summary>
/// L'entree de l'ecran « Cloture » (PROC_CLOT). Plan « RIG v3 100 % fonctionnel », lot 20 (27/11/2026), sur la marche du lot 17.
///
/// Branche au serveur, l'ecran montre les procedures collectives dont l'examen de cloture approche, lues dans la base du greffe
/// (Serveur/Socle/EcransMain/LectureCloture.cs : la requete de la grille « Selection PC » de RIG v2, ses filtres cites) ; les
/// dates Du / Au sont celles de RIG v2, sur la LIMITE d'examen. Ce que la base ne porte pas reste vide : chambre, audience,
/// heure, classement et huissier sont choisis par l'agent a l'enrolement dans RIG v2. « Dernier courrier » est celui du courrier
/// aux debiteurs (CLOT_EVE_PRO_INCITR, LectureCloture.cs). SANS serveur : mode « demo », aucune ligne (l'ecran garde ses
/// affaires d'exemple, marquees). En panne : le motif et aucune ligne.
///
/// LES GESTES (plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 11, 28/11/2026), écrits par le serveur
/// (Serveur/Socle/EcransMain/EcritureCloture.cs) sur les PC cochees :
///   - le PRINCIPAL, l'« Enrôlement sur saisine d'office » (mode 1, celui que RIG v2 présélectionne depuis l'alerte, OPE_EXAMEN_CLOT.cs:
///     57-59 ; OPE_SELECT_PC.cs:1776-1899) -- <see cref="CreerEnrolementsAsync"/> : une instance de clôture par PC, son appel à l'audience
///     choisie pour le liquidateur, sa convocation, le 4CLO de la PC survenu ; relu par <see cref="EnrolementsAsync"/> (l'onglet
///     « Enrolement / requete ») ;
///   - le second, « Courrier aux debiteurs » (mode 5, OPE_SELECT_PC.cs:1761-1775) -- <see cref="EnvoyerCourrierDebiteursAsync"/>.
/// Sans serveur, rien n'est ecrit (« Démonstration »).
/// </summary>
public static partial class EcranCloture
{
    /// <summary>La phrase de RIG v2 pour une PC sans liquidateur, montree bloquee (OPE_SELECT_PC.cs:534).</summary>
    public const string SansLiquidateur = "Pas de liquidateur présent dans la procédure collective.";

    [JSInvokable("EcranCloture.Affaires")]
    public static Task<VueCloture> Affaires(string? du, string? au)
        => AffairesAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, Jour(du), Jour(au));

    /// <summary>Les procedures du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueCloture> AffairesAsync(ClientServeur? serveur, string? greffe, DateOnly? du, DateOnly? au)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les procédures à clôturer", (s, g) =>
            s.EnvoyerAsync<RechercheCloture, AffairesClotureLues>(RoutesEcransMain.Pour(RoutesCloture.Affaires, g), new RechercheCloture(du, au)));
        if (lu.Mode == LectureEcransMain.Demo) return new VueCloture(lu.Mode, null, [], 0, DateDuJour());
        var aujourdhui = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (lu.Valeur is not { } v) return new VueCloture(lu.Mode, lu.Message, [], 0, aujourdhui);
        return new VueCloture(lu.Mode, null, [.. v.Lignes.Select(Ligne)], v.Total, aujourdhui);
    }

    /// <summary>« Courrier aux débiteurs » sur les PC <paramref name="ids"/> (les lignes cochees) ; <paramref name="facturer"/> : le bouton
    /// FACTURATION de RIG v2. Le resultat porte les PC traitees (Ids).</summary>
    [JSInvokable("EcranCloture.CourrierDebiteurs")]
    public static Task<ResultatGeste> CourrierDebiteurs(int[] ids, bool facturer)
        => EnvoyerCourrierDebiteursAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, ids, facturer);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> EnvoyerCourrierDebiteursAsync(ClientServeur? serveur, string? greffe, int[] ids, bool facturer)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCloture.CourrierDebiteurs, greffe),
            new GesteEcran(TypeGeste.Agir, [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))],
                           new Dictionary<string, string> { [RoutesCloture.ChampFacturer] = facturer ? "oui" : "non" }) { Nom = "Courrier aux débiteurs" });
    }

    /// <summary>« Enrôlement sur saisine d'office » des PC <paramref name="ids"/> (les lignes cochees), a l'audience du
    /// <paramref name="dateAudience"/> (aaaa-mm-jj) et <paramref name="heureAudience"/> (vide : la seule de ce jour), au
    /// <paramref name="classement"/> (vide : le réglage CLOT_SO_TYPECLA) et a <paramref name="heureAppel"/> ; <paramref name="facturer"/> :
    /// le bouton FACTURATION de RIG v2. Le resultat porte les instances creees (Ids).</summary>
    [JSInvokable("EcranCloture.EnrolerSurSaisineOffice")]
    public static Task<ResultatGeste> EnrolerSurSaisineOffice(int[] ids, string? dateAudience, string? heureAudience, string? classement,
                                                              string? heureAppel, bool facturer)
        => CreerEnrolementsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, ids, dateAudience, heureAudience, classement, heureAppel, facturer);

    /// <summary>Le geste sur <paramref name="serveur"/> et le greffe <paramref name="greffe"/> : sans eux, « Démonstration ».</summary>
    public static async Task<ResultatGeste> CreerEnrolementsAsync(ClientServeur? serveur, string? greffe, int[] ids, string? dateAudience,
                                                                 string? heureAudience, string? classement, string? heureAppel, bool facturer)
    {
        if (serveur is null || string.IsNullOrWhiteSpace(greffe))
            return new ResultatGeste(false, $"{EcranGenere.Demonstration} : RIG n'est pas branché sur la base d'un greffe.") { SansEcrivain = true };
        return await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesCloture.EnrolementSo, greffe),
            new GesteEcran(TypeGeste.Creer, [.. ids.Select(i => i.ToString(CultureInfo.InvariantCulture))], new Dictionary<string, string>
            {
                [RoutesCloture.ChampDateAudience] = dateAudience?.Trim() ?? "", [RoutesCloture.ChampHeureAudience] = heureAudience?.Trim() ?? "",
                [RoutesCloture.ChampClassement] = classement?.Trim() ?? "", [RoutesCloture.ChampHeureAppel] = heureAppel?.Trim() ?? "",
                [RoutesCloture.ChampFacturer] = facturer ? "oui" : "non",
            }) { Nom = "Enrôlement sur saisine d'office" });
    }

    /// <summary>Les instances de clôture enrôlées (onglet « Enrolement / requete ») : <paramref name="du"/> et <paramref name="au"/> bornent
    /// la date d'enrôlement (aaaa-mm-jj ; vides : toutes).</summary>
    [JSInvokable("EcranCloture.Enrolements")]
    public static Task<VueEnrolementsCloture> Enrolements(string? du, string? au)
        => EnrolementsAsync(ClientServeur.Actif, SessionAgent.CodeGreffe, Jour(du), Jour(au));

    /// <summary>Les instances de clôture du greffe <paramref name="greffe"/> (null serveur : la demonstration, aucune ligne).</summary>
    public static async Task<VueEnrolementsCloture> EnrolementsAsync(ClientServeur? serveur, string? greffe, DateOnly? du, DateOnly? au)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les instances de clôture enrôlées", (s, g) =>
            s.EnvoyerAsync<RechercheCloture, EnrolementsClotureLus>(RoutesEcransMain.Pour(RoutesCloture.Enrolements, g), new RechercheCloture(du, au)));
        if (lu.Valeur is not { } v) return new VueEnrolementsCloture(lu.Mode, lu.Mode == LectureEcransMain.Demo ? null : lu.Message, [], 0);
        static string J(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        return new VueEnrolementsCloture(lu.Mode, null, [.. v.Lignes.Select(e => new LigneEnrolementCloture(e.IdInstance, e.NumRole,
            e.NumRepGeneral, e.IdPc, e.NumPc, e.Demandeur, e.Defendeur, e.ActeSaisine, J(e.Enrolement), J(e.Audience), e.HeureAudience,
            e.Chambre, e.TypeClassement, e.HeureAppel, e.Convocation, J(e.DateConvocation), J(e.ExamenSurvenu)))], v.Total);
    }

    private static LigneCloture Ligne(AffaireClotureLue a) => new(
        Role: a.NumPc, Affaire: a.NomAffaire, Demandeur: "", Defendeur: a.NomAffaire, Chambre: "", Audience: "", HeureAppel: "",
        Classement: "", LimiteExamen: a.LimiteExamen?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        // Jamais de facture pour une PC au forfait (OPE_SELECT_PC.cs:2006). Le dernier courrier : jj/mm/aaaa, comme la grille V2 (:680).
        AFacturer: !a.AuForfait, DernierCourrier: a.DernierCourrier?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
        Dossier: a.Dossier, NumPc: a.NumPc, HeureDebut: "", Dirigeants: "",
        Liquidateurs: a.Liquidateurs.Length > 0 ? a.Liquidateurs : SansLiquidateur, Forfait: "", Huissier: "", CodePostalVille: "",
        ActeSaisine: "", Depot: "", Facturation: "", AuForfait: a.AuForfait, Id: a.Id);

    private static DateOnly? Jour(string? iso)
        => DateOnly.TryParseExact(iso ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
