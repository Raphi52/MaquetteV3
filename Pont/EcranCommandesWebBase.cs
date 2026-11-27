using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'ouverture de l'ecran « Commandes web » sur la base du greffe (PROC_COMMANDES_WEB). Plan « RIG v3 100 % fonctionnel », lot 20
/// (27/11/2026), sur la marche du lot 17.
///
/// Branche au serveur, la session part des commandes de la base (v2.COMMANDE_WEB, Serveur/Socle/EcransMain/LectureCommandesWeb.cs)
/// au lieu de TraitementCommandes.JeuDemoCommandes() ; les regles de l'ecran (onglets, recherche, escalier) ne changent pas.
/// SANS serveur : la demonstration, comme avant (tests/CommandesWebCheck la joue). En panne : AUCUNE commande et le motif.
/// </summary>
public static partial class EcranCommandesWeb
{
    /// <summary>Ouvre une session sur les commandes du greffe <paramref name="greffe"/> (null serveur : la demonstration).</summary>
    public static async Task<VueCommandesWeb> OuvrirAsync(ClientServeur? serveur, string? greffe)
    {
        var session = await SessionAsync(serveur, greffe);
        return session.Vue(Sessions.Ouvrir(session));
    }

    /// <summary>La session de l'ecran : les commandes du greffe <paramref name="greffe"/>, ou la demonstration sans serveur.</summary>
    private static async Task<SessionCommandesWeb> SessionAsync(ClientServeur? serveur, string? greffe)
    {
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "les commandes web", (s, g) =>
            s.LireAsync<CommandesWebLues>(RoutesEcransMain.Pour(RoutesCommandesWeb.Commandes, g)));
        if (lu.Mode == LectureEcransMain.Demo) return new SessionCommandesWeb();
        return new SessionCommandesWeb(lu.Valeur is { } v ? [.. v.Lignes.Select(SessionCommandesWeb.Commande)] : [], lu.Mode, lu.Message);
    }
}

/// <summary>
/// Ce que la session fait d'une commande LUE EN BASE (mode « serveur »).
///
/// LA LECTURE : une ligne de COMMANDE_WEB devient une CommandeRecue -- famille, etat et origine traduits comme NeoRIG
/// (FamilleCommandeWeb.cs:12-19, EtatCommandeWeb.cs:35-40, OrigineCommandeWeb.cs:16-28). L'adresse, l'e-mail, les types
/// d'inscription et le sous-type viennent du flux XML de la commande, que la V3 ne lit pas : ils restent vides.
///
/// LES GESTES QUI ECRIVENT (<see cref="GestesQuiEcrivent"/>) REFUSENT et disent pourquoi (<see cref="SansEcriture"/>) : dans
/// NeoRIG, decider d'une commande ecrit son etat ET edite les certificats, la facture et la reponse a Infogreffe
/// (TraiterCommandeWebService, INotificationRejetCommandeWeb), un service externe ; la V3 n'a pas encore ces editions et ne
/// change pas l'etat seul. La saisie au guichet et l'adresse corrigee s'enregistrent aussi par ce traitement. Le rapprochement
/// (chercher le debiteur) cherchait dans la DEMONSTRATION (JeuDemo.EntitesRequi) : en base, il ne montre rien
/// (<see cref="SansRapprochement"/>).
/// </summary>
public sealed partial class SessionCommandesWeb
{
    /// <summary>Les gestes qui modifient une commande (Agir) : refuses quand les commandes viennent de la base.</summary>
    internal static readonly IReadOnlySet<string> GestesQuiEcrivent = new HashSet<string>(StringComparer.Ordinal)
    {
        "Decider", "EnvoyerSansAttendre", "NouvelleCommande", "EnregistrerSaisie", "AnnulerSaisie", "EnregistrerAdresse", "RevenirAdresseRecue",
    };

    /// <summary>
    /// La date de travail montree (recherche, types, certificats) : celle du jeu de demonstration sans serveur
    /// (TraitementCommandes.DateTravail), la date du jour quand les commandes viennent de la base.
    /// </summary>
    private string DateDeTravail
        => (EnBase ? DateOnly.FromDateTime(DateTime.Today) : TraitementCommandes.DateTravail).ToString("dd/MM/yyyy");

    /// <summary>Le refus d'un geste qui ecrit, branche a la base.</summary>
    public const string SansEcriture =
        "Rien n'est enregistré : dans NéoRIG, traiter une commande web écrit son état ET édite les certificats, la facture et la " +
        "réponse à Infogreffe (service externe). La V3 n'a pas encore ces éditions : elle ne modifie pas la commande.";

    /// <summary>L'etape « Type de certificat de non-opposition » et le cadre du controle, tant qu'aucun type n'est coche.</summary>
    public const string SansTypeCoche = "Cochez un type de certificat pour lancer le contrôle de délivrance.";

    /// <summary>L'etape « Type de certificat de non-opposition » en base : le controle de delivrance (NeoRIG,
    /// DelivranceNonOpposition.Evaluer) cherche l'entreprise du SIREN ; joue sur les entites de demonstration, il
    /// rejetait a tort (« aucune entreprise ne correspond au SIREN… »). Il n'est donc pas joue, et l'etape le dit.</summary>
    public const string SansControleDelivrance =
        "Contrôle de délivrance non joué : il cherche l'entreprise du SIREN de la commande, et la recherche du débiteur " +
        "dans la base du greffe n'est pas encore reprise.";

    /// <summary>Le rapprochement en base : aucune entite d'exemple, et pourquoi.</summary>
    internal static readonly ResultatRapprochement SansRapprochement = new([],
        "La recherche du débiteur dans la base du greffe n'est pas encore reprise : aucun dossier n'est proposé.");

    /// <summary>Une commande de la base, en CommandeRecue (les regles de l'ecran).</summary>
    internal static CommandeRecue Commande(CommandeWebLue c) => new()
    {
        Id = c.Id,
        Reference = c.Reference,
        Date = c.DateCommande ?? DateTime.MinValue,
        Famille = c.Famille switch
        {
            5 => FamilleCommande.Faillite,
            11 => FamilleCommande.EtatEndettementNonInscrit,
            12 => FamilleCommande.EtatEndettementRequisition,
            13 => FamilleCommande.NonOpposition,
            _ => FamilleCommande.EtatEndettement,
        },
        Origine = c.Origine switch { "IG" => OrigineCommande.Infogreffe, "MAN" => OrigineCommande.Guichet, _ => OrigineCommande.SiteLocal },
        Etat = c.Etat switch
        {
            "T" => EtatCommande.EnAttente,
            "V" => EtatCommande.Validee,
            "R" => EtatCommande.Rejetee,
            "S" => EtatCommande.SaisieManuelle,
            _ => EtatCommande.ATraiter,
        },
        Denomination = c.Denomination,
        Siren = c.Siren,
        Requerant = c.Requerant,
        Motif = c.Motif,
    };
}
