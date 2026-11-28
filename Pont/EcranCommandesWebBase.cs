using System.Globalization;
using System.Text.Json;
using RigV3.Contrats.EcransGeneres;
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
        var lu = await LireAsync(serveur, greffe);
        if (lu.Mode == LectureEcransMain.Demo) return new SessionCommandesWeb();
        return new SessionCommandesWeb(lu.Valeur is { } v ? [.. v.Lignes.Select(SessionCommandesWeb.Commande)] : [], lu.Mode, lu.Message);
    }

    private static Task<LectureEcranMain<CommandesWebLues>> LireAsync(ClientServeur? serveur, string? greffe)
        => LectureEcransMain.LireAsync(serveur, greffe, "les commandes web", (s, g) =>
            s.LireAsync<CommandesWebLues>(RoutesEcransMain.Pour(RoutesCommandesWeb.Commandes, g)));

    /// <summary>
    /// Un geste de l'ecran sur <paramref name="serveur"/> (null : la demonstration). Commandes lues en base, « Decider » Mettre en
    /// attente s'ECRIT par le serveur (Serveur/Socle/EcransMain/EcritureCommandesWeb.cs, regles de NéoRIG) puis la liste est RELUE de
    /// la base ; tout autre geste se joue sur la session (Agir), ceux qui ecrivent y refusent (<see cref="SessionCommandesWeb.SansEcriture"/>).
    /// Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7, 28/11/2026.
    /// </summary>
    // fix-ok: pas un correctif -- lot 7 (plan-f4fb097f) ajoute le geste ecrit par le serveur ; mesure de depart : banc
    // ParcoursCompletCheck, PROC_COMMANDES_WEB rouge « geste non prouvé » et v2.COMMANDE_WEB absente de la base du banc (28/11/2026).
    public static async Task<VueCommandesWeb> AgirAsync(ClientServeur? serveur, string? greffe, string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        if (serveur is not null && !string.IsNullOrWhiteSpace(greffe) && s.MiseEnAttenteEnBase(geste, valeur) is { } demande)
        {
            var r = await EcranGenere.EnvoyerGesteAsync(serveur, greffe, RoutesEcransMain.Pour(RoutesTraitementCommandesWeb.MettreEnAttente, greffe), demande);
            var relu = r.Fait ? await LireAsync(serveur, greffe) : null;
            s.ApresMiseEnAttente(r, relu?.Valeur, relu?.Message);
        }
        else s.Agir(geste, valeur);
        return s.Vue(session);
    }
}

/// <summary>
/// Ce que la session fait d'une commande LUE EN BASE (mode « serveur »).
///
/// LA LECTURE : une ligne de COMMANDE_WEB devient une CommandeRecue -- famille, etat et origine traduits comme NeoRIG
/// (FamilleCommandeWeb.cs:12-19, EtatCommandeWeb.cs:35-40, OrigineCommandeWeb.cs:16-28). L'adresse, l'e-mail, les types
/// d'inscription et le sous-type viennent du flux XML de la commande, que la V3 ne lit pas : ils restent vides.
///
/// METTRE EN ATTENTE s'ecrit dans la base (lot 7 du plan « RIG v3 complet », 28/11/2026) : NeoRIG n'y joint ni edition ni
/// notification (TraiterCommandeWebService.CloturerDemandeAsync ne fait rien pour une mise en attente) ; le serveur l'ecrit
/// (<see cref="EcranCommandesWeb.AgirAsync(ClientServeur?, string?, string, string, JsonElement)"/>), la liste est relue.
/// LES AUTRES GESTES QUI ECRIVENT (<see cref="GestesQuiEcrivent"/>) REFUSENT et disent pourquoi (<see cref="SansEcriture"/>) : dans
/// NeoRIG, valider ou rejeter une commande ecrit son etat ET edite les certificats, la facture et la reponse a Infogreffe
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
        "Rien n'est enregistré : dans NéoRIG, valider ou rejeter une commande web écrit son état ET édite les certificats, la facture et " +
        "la réponse à Infogreffe (service externe). La V3 n'a pas encore ces éditions : seule la mise en attente s'enregistre.";

    /// <summary>Le geste du serveur quand <paramref name="geste"/> met en attente la commande ouverte d'une session lue en base ;
    /// null pour tout autre geste (il se joue sur la session).</summary>
    internal GesteEcran? MiseEnAttenteEnBase(string geste, JsonElement valeur)
        => EnBase && geste == "Decider" && Selection is { } c && valeur.ValueKind == JsonValueKind.String
           && Enum.TryParse<ActionCommande>(valeur.GetString(), out var action) && action == ActionCommande.MettreEnAttente
            ? new GesteEcran(TypeGeste.Decider, [c.Id.ToString(CultureInfo.InvariantCulture)],
                             new Dictionary<string, string> { [RoutesTraitementCommandesWeb.ChampMotif] = MotifSaisi }) { Nom = "Mettre en attente" }
            : null;

    /// <summary>
    /// Apres la mise en attente par le serveur : refusee, son message (la bande reste ouverte, le motif saisi aussi) ; faite, les
    /// commandes <paramref name="relues"/> dans la base remplacent celles de la session et la suivante s'ouvre, comme Decider en
    /// demonstration. Faite mais pas relue : le message le dit (<paramref name="motifLecture"/>).
    /// </summary>
    internal void ApresMiseEnAttente(ResultatGeste r, CommandesWebLues? relues, string? motifLecture)
    {
        if (!r.Fait || Selection is not { } c)
        {
            Message = (r.Message, "avertissement");
            return;
        }
        var suivante = TraitementCommandes.Suivante(Ordre, c);
        BandeOuverte = null;
        MotifSaisi = string.Empty;
        Selection = null;
        if (relues is null)
        {
            Message = ($"{r.Message} La liste n'a pas pu être relue : {motifLecture}", "avertissement");
            return;
        }
        Toutes.Clear();
        Toutes.AddRange(relues.Lignes.Select(Commande));
        Rafraichir();
        var texte = r.Message;
        if (suivante is not null && Ordre.FirstOrDefault(x => x.Id == suivante.Id) is { } prochaine) Choisir(prochaine);
        else texte = $"{texte} C'était la dernière commande de la liste.";
        Message = (texte, "succes");
    }

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
