using System.Globalization;
using System.Text.Json;
using RigV3.Contrats.EcransGeneres;
using RigV3.Contrats.EcransMain;

namespace RigV3Desktop.Pont;

/// <summary>
/// L'ouverture de l'ecran « Commandes web » sur la base du greffe (PROC_COMMANDES_WEB). Plan « RIG v3 100 % fonctionnel », lot 20
/// (27/11/2026), sur la marche du lot 17 ; gestes ecrits et recherche du debiteur : plan « RIG v3 complet », lot 7 (28/11/2026).
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
        var session = new SessionCommandesWeb([], lu.Mode, lu.Message);
        if (lu.Valeur is { } v) session.Charger(v);
        return session;
    }

    private static Task<LectureEcranMain<CommandesWebLues>> LireAsync(ClientServeur? serveur, string? greffe)
        => LectureEcransMain.LireAsync(serveur, greffe, "les commandes web", (s, g) =>
            s.LireAsync<CommandesWebLues>(RoutesEcransMain.Pour(RoutesCommandesWeb.Commandes, g)));

    /// <summary>
    /// Un geste de l'ecran sur <paramref name="serveur"/> (null : la demonstration). Commandes lues en base : « Decider » Mettre en
    /// attente et Valider s'ECRIVENT par le serveur (Serveur/Socle/EcransMain/EcritureCommandesWeb.cs, regles de NéoRIG) puis la liste
    /// est RELUE de la base ; le rapprochement que demande un geste (choisir une commande, relancer la recherche) est cherche dans la
    /// base (<see cref="RechercherAsync"/>) ; tout autre geste se joue sur la session (Agir), ceux qui ecrivent encore y refusent
    /// (<see cref="SessionCommandesWeb.SansEcriture"/>). Plan « RIG v3 complet, mesuré par le banc du parcours complet », lot 7.
    /// </summary>
    // fix-ok: pas un correctif -- lot 7 (plan-f4fb097f) ajoute le geste ecrit par le serveur ; mesure de depart : banc
    // ParcoursCompletCheck, PROC_COMMANDES_WEB rouge « geste non prouvé » et v2.COMMANDE_WEB absente de la base du banc (28/11/2026).
    // Refus du chef (main bad949c) : Valider n'etait pas ecrit, le debiteur pas cherche en base (capture commandes-web-3).
    public static async Task<VueCommandesWeb> AgirAsync(ClientServeur? serveur, string? greffe, string session, string geste, JsonElement valeur)
    {
        var s = Sessions.Lire(session);
        var branche = serveur is not null && !string.IsNullOrWhiteSpace(greffe);
        var (route, demande) = !branche ? (null, null)
            : s.MiseEnAttenteEnBase(geste, valeur) is { } attente ? (RoutesTraitementCommandesWeb.MettreEnAttente, attente)
            : s.ValidationEnBase(geste, valeur) is { } validation ? (RoutesTraitementCommandesWeb.Valider, validation)
            : ((string?)null, (GesteEcran?)null);
        if (route is not null && demande is not null)
        {
            var r = await EcranGenere.EnvoyerGesteAsync(serveur!, greffe!, RoutesEcransMain.Pour(route, greffe!), demande);
            var relu = r.Fait ? await LireAsync(serveur, greffe) : null;
            s.ApresGeste(r, relu?.Valeur, relu?.Message);
        }
        else s.Agir(geste, valeur);
        if (branche && s.PrendreRechercheDemandee() is { } cherche)
            s.PoserRapprochement(await RechercherAsync(serveur!, greffe!, cherche.Siren, cherche.Raison));
        return s.Vue(session);
    }

    /// <summary>
    /// LA RECHERCHE DU DEBITEUR (ou du dossier concerne) DANS LA BASE DU GREFFE : celle de « Réquisitions », le REQUI de RIG v2
    /// (Serveur/Socle/EcransMain/LectureEntreprisesRequi.cs, OPE_REQUI_RECHERCHE.cs:140-256 de la branche 275), que NéoRIG reprend pour
    /// son rapprochement (CibleRattachementRepository.cs:19-23, « Entonnoir du REQUI V2 (CriteresEntreprise) : SIREN, puis
    /// dénomination »). L'ordre et les messages sont ceux de l'ecran (TraitementCommandes.Rechercher, Rapprochement_* de NéoRIG) : le
    /// SIREN d'abord, puis la raison sociale ; les dossiers radies sont montres (marques), comme NéoRIG (EstRadie). NON REPRIS, nommes
    /// par LectureEntreprisesRequi.cs : les non-inscrits, les etablissements secondaires, la phonetique et les mots directeurs.
    /// </summary>
    internal static async Task<ResultatRapprochement> RechercherAsync(ClientServeur serveur, string greffe, string siren, string raison)
    {
        var brut = siren.Trim();
        var chiffres = TraitementCommandes.Chiffres(brut);
        string? message = null;
        if (brut.Length > 0 && (chiffres.Length != brut.Replace(" ", "").Length || chiffres.Length > 9))
            message = $"« {brut} » n'est pas un SIREN (9 chiffres au plus) : il n'est pas utilisé.";
        else if (chiffres.Length > 0)
        {
            var (parSiren, panne) = await LireEntreprisesAsync(serveur, greffe, "", chiffres);
            if (panne is not null) return new([], panne);
            if (parSiren.Length > 0) return new(parSiren, null);
            message = $"Aucun dossier pour le SIREN {TraitementCommandes.FormaterSiren(chiffres)} : recherche par raison sociale.";
        }
        if (string.IsNullOrWhiteSpace(raison))
            return new([], message ?? "Saisissez un SIREN, une raison sociale ou une adresse (voie ou ville), puis lancez la recherche.");
        var (parNom, panneNom) = await LireEntreprisesAsync(serveur, greffe, raison.Trim(), "");
        if (panneNom is not null) return new([], panneNom);
        return new(parNom, parNom.Length == 0 ? "Aucun dossier RCS ni non-inscrit ne correspond. La création du non-inscrit se fait dans RIG V2." : message);
    }

    private static async Task<(EntiteRapprochee[] Entites, string? Panne)> LireEntreprisesAsync(ClientServeur serveur, string greffe, string raison,
                                                                                               string siren)
    {
        var recherche = new RechercheEntreprises(raison, siren, "", "", "", "", InclureRadies: true, Limite: SessionCommandesWeb.LimiteRapprochement);
        var lu = await LectureEcransMain.LireAsync(serveur, greffe, "le débiteur", (s, g) =>
            s.EnvoyerAsync<RechercheEntreprises, EntreprisesLues>(RoutesEcransMain.Pour(RoutesRequisitions.Entreprises, g), recherche));
        return lu.Valeur is { } v ? ([.. v.Lignes.Select(SessionCommandesWeb.Entite)], null)
                                  : ([], $"La recherche du débiteur n'a pas pu être lue dans la base du greffe : {lu.Message}");
    }
}

/// <summary>
/// Ce que la session fait d'une commande LUE EN BASE (mode « serveur »).
///
/// LA LECTURE : une ligne de COMMANDE_WEB devient une CommandeRecue -- famille, etat et origine traduits comme NeoRIG
/// (FamilleCommandeWeb.cs:12-19, EtatCommandeWeb.cs:35-40, OrigineCommandeWeb.cs:16-28). L'adresse, l'e-mail, les types
/// d'inscription et le sous-type viennent du flux XML de la commande, que la V3 ne lit pas : ils restent vides. Une commande
/// validee montre le dossier auquel sa demande la rattache (<see cref="Rattachements"/>).
///
/// LE RAPPROCHEMENT est cherche dans la base (EcranCommandesWeb.RechercherAsync) : <see cref="Choisir"/> et
/// <see cref="RelancerRapprochement"/> le DEMANDENT (<see cref="PrendreRechercheDemandee"/>), le pont le pose
/// (<see cref="PoserRapprochement"/>). Un dossier trouve est une <see cref="EntiteRapprochee"/> (<see cref="Entite(EntrepriseLue)"/>),
/// retenu par sa cle <see cref="CleDossier"/>.
///
/// METTRE EN ATTENTE et VALIDER s'ecrivent dans la base (lot 7 du plan « RIG v3 complet », 28/11/2026) : le serveur les ecrit
/// (EcritureCommandesWeb.cs), la liste est relue. Valider part quand l'escalier est fait (le dossier retenu, le choix, l'edition) ;
/// sinon la marche qui manque ferme le geste, rien n'est envoye. LES AUTRES GESTES QUI ECRIVENT (<see cref="GestesQuiEcrivent"/>)
/// REFUSENT et disent pourquoi (<see cref="SansEcriture"/>) : le rejet notifie Infogreffe (INotificationRejetCommandeWeb, un service
/// externe) ; la saisie au guichet et l'adresse corrigee s'enregistrent par le traitement des clients, que la V3 n'a pas.
/// </summary>
public sealed partial class SessionCommandesWeb
{
    /// <summary>Les gestes qui modifient une commande (Agir) : refuses quand les commandes viennent de la base, sauf Mettre en
    /// attente et Valider, envoyes au serveur par EcranCommandesWeb.AgirAsync.</summary>
    internal static readonly IReadOnlySet<string> GestesQuiEcrivent = new HashSet<string>(StringComparer.Ordinal)
    {
        "Decider", "EnvoyerSansAttendre", "NouvelleCommande", "EnregistrerSaisie", "AnnulerSaisie", "EnregistrerAdresse", "RevenirAdresseRecue",
    };

    /// <summary>La limite de dossiers d'une recherche du rapprochement : le plancher de OPE_REQUI_RECHERCHE
    /// (LectureEntreprisesRequi.LimiteMin).</summary>
    internal const int LimiteRapprochement = 50;

    /// <summary>
    /// La date de travail montree (recherche, types, certificats) : celle du jeu de demonstration sans serveur
    /// (TraitementCommandes.DateTravail), la date du jour quand les commandes viennent de la base.
    /// </summary>
    private string DateDeTravail
        => (EnBase ? DateOnly.FromDateTime(DateTime.Today) : TraitementCommandes.DateTravail).ToString("dd/MM/yyyy");

    /// <summary>Le refus d'un geste qui ecrit, branche a la base (hors Mettre en attente et Valider).</summary>
    public const string SansEcriture =
        "Rien n'est enregistré : dans NéoRIG, rejeter une commande web la notifie à Infogreffe (service externe), et la saisie au guichet "
        + "passe par le choix des clients. RIG v3 ne les reprend pas encore : seules la mise en attente et la validation s'enregistrent.";

    /// <summary>L'etape « Types d'inscription commandés » en base : les types viennent du flux XML de la commande.</summary>
    public const string SansTypesRecus =
        "Les types d'inscription commandés viennent du flux XML de la commande, que RIG v3 ne lit pas encore : l'état d'endettement ne "
        + "se valide pas ici.";

    /// <summary>Le dossier retenu (sa cle) a valider, en base ; vide sinon.</summary>
    private readonly Dictionary<string, EntiteRapprochee> Retenues = new(StringComparer.Ordinal);

    /// <summary>Le dossier et la demande d'une commande lue en base (le numero de sa demande, le n° de gestion qu'elle porte).</summary>
    private readonly Dictionary<int, (string Demande, string Dossier)> Rattachements = [];

    /// <summary>La recherche du rapprochement qu'un geste demande, a lire en base par le pont ; null sinon.</summary>
    private (string Siren, string Raison)? RechercheDemandee;

    /// <summary>La cle d'un dossier RCS de la base dans le rapprochement.</summary>
    public static string CleDossier(int id) => "D" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Un dossier RCS de la base (LectureEntreprisesRequi) en entite du rapprochement : son siege, ses inscriptions, sa
    /// procedure collective et sa radiation lus par les regles de RIG v2.</summary>
    internal static EntiteRapprochee Entite(EntrepriseLue e)
    {
        var adresse = string.Join(", ", new[] { e.Adresse, $"{e.CodePostal} {e.Ville}".Trim() }.Where(x => x.Length > 0));
        return new EntiteRapprochee(CleDossier(e.Id), e.Gestion, e.Designation, e.Siren, e.Personne, NonInscrit: false, e.Radie,
                                    e.ProcedureCollective, [new LigneEntite(TraitementCommandes.NatureSiege, adresse, e.NbInscriptions)]);
    }

    /// <summary>L'entite de cle <paramref name="cle"/> : en base, celle de la recherche ou deja retenue ; sinon la demonstration.</summary>
    private EntiteRapprochee? Entite(string? cle)
    {
        if (!EnBase) return TraitementCommandes.Entite(cle);
        if (string.IsNullOrEmpty(cle)) return null;
        return Resultat.Entites.FirstOrDefault(e => e.Cle == cle) ?? Retenues.GetValueOrDefault(cle);
    }

    /// <summary>Les commandes lues en base remplacent celles de la session, avec la demande et le dossier de chacune.</summary>
    internal void Charger(CommandesWebLues lues)
    {
        Toutes.Clear();
        Toutes.AddRange(lues.Lignes.Select(Commande));
        Rattachements.Clear();
        foreach (var c in lues.Lignes) Rattachements[c.Id] = (c.NumeroDemande, c.DossierDemande);
        Rafraichir();
    }

    /// <summary>La recherche demandee par le dernier geste, une seule fois.</summary>
    internal (string Siren, string Raison)? PrendreRechercheDemandee()
    {
        var r = RechercheDemandee;
        RechercheDemandee = null;
        return r;
    }

    /// <summary>Le rapprochement lu en base pour la commande ouverte.</summary>
    internal void PoserRapprochement(ResultatRapprochement r) => Resultat = r;

    /// <summary>Le geste du serveur quand <paramref name="geste"/> met en attente la commande ouverte d'une session lue en base ;
    /// null pour tout autre geste (il se joue sur la session).</summary>
    internal GesteEcran? MiseEnAttenteEnBase(string geste, JsonElement valeur)
        => EnBase && Action(geste, valeur) == ActionCommande.MettreEnAttente && Selection is { } c
            ? new GesteEcran(TypeGeste.Decider, [c.Id.ToString(CultureInfo.InvariantCulture)],
                             new Dictionary<string, string> { [RoutesTraitementCommandesWeb.ChampMotif] = MotifSaisi }) { Nom = "Mettre en attente" }
            : null;

    /// <summary>
    /// Le geste du serveur quand <paramref name="geste"/> valide la commande ouverte d'une session lue en base ET que son escalier
    /// est fait (un dossier RCS retenu, le choix, l'edition) ; null sinon -- Agir dit alors la marche qui manque, rien n'est envoye.
    /// </summary>
    internal GesteEcran? ValidationEnBase(string geste, JsonElement valeur)
        => EnBase && Action(geste, valeur) == ActionCommande.Valider && Selection is { } c
           && BlocageValider(c, DocumentsEnCours(c)) is null && Entite(c.CibleRetenue) is { NonInscrit: false } cible
           && cible.Cle.StartsWith('D') && int.TryParse(cible.Cle[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dossier)
            ? new GesteEcran(TypeGeste.Decider, [c.Id.ToString(CultureInfo.InvariantCulture)],
                             new Dictionary<string, string> { [RoutesTraitementCommandesWeb.ChampDossier] = dossier.ToString(CultureInfo.InvariantCulture) })
              { Nom = "Valider" }
            : null;

    private static ActionCommande? Action(string geste, JsonElement valeur)
        => geste == "Decider" && valeur.ValueKind == JsonValueKind.String && Enum.TryParse<ActionCommande>(valeur.GetString(), out var a) ? a : null;

    /// <summary>
    /// Apres un geste ecrit par le serveur : refuse, son message (la bande reste ouverte, le motif saisi aussi) ; fait, les commandes
    /// <paramref name="relues"/> dans la base remplacent celles de la session et la suivante s'ouvre, comme Decider en demonstration.
    /// Fait mais pas relu : le message le dit (<paramref name="motifLecture"/>).
    /// </summary>
    internal void ApresGeste(ResultatGeste r, CommandesWebLues? relues, string? motifLecture)
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
        Charger(relues);
        var texte = r.Message;
        if (suivante is not null && Ordre.FirstOrDefault(x => x.Id == suivante.Id) is { } prochaine) Choisir(prochaine);
        else texte = $"{texte} C'était la dernière commande de la liste.";
        Message = (texte, "succes");
    }

    /// <summary>La ligne « Rattachée au dossier … » d'une commande validee lue en base ; null sinon.</summary>
    private string? RattachementDe(CommandeRecue c)
        => EnBase && c.Etat == EtatCommande.Validee && Rattachements.TryGetValue(c.Id, out var r) && r.Dossier.Length > 0
            ? $"Rattachée au dossier {r.Dossier} · demande n° {r.Demande}" : null;

    /// <summary>L'etape « Type de certificat de non-opposition » et le cadre du controle, tant qu'aucun type n'est coche.</summary>
    public const string SansTypeCoche = "Cochez un type de certificat pour lancer le contrôle de délivrance.";

    /// <summary>L'etape « Type de certificat de non-opposition » en base : le controle de delivrance (NeoRIG,
    /// DelivranceNonOpposition.Evaluer) lit la date de la derniere formalite du dossier ; joue sur la demonstration, il rejetait a
    /// tort. Il n'est pas joue, et l'etape le dit (le serveur refuse aussi : EcritureCommandesWeb.SansControleDelivrance).</summary>
    public const string SansControleDelivrance =
        "Contrôle de délivrance non joué : il lit la date de la dernière formalité du dossier, que RIG v3 ne reprend pas encore. "
        + "Le certificat de non-opposition ne se valide pas ici.";

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
