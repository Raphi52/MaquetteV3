using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using static RigV3.Serveur.Socle.EcransGeneres.LecteurSurveillanceIp;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Injonctions de payer » (PROC_SURVIP) : « Appliquer les traitements » traite les echeances cochees (Id = l'evenement FUTUR
/// echu, la cellule TrtIdEvePro de RIG v2), chacune selon son groupe. Mode d'emploi : EcrivainEcran.cs ; la liste :
/// LecteurSurveillanceIp.cs. Plan « RIG v3 100 % fonctionnel », lot 8, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_SURVIP\Etape\OPE_TRAITEMENTS.cs -- PreparationTraitements
/// (l. 585-905), ValiderInstancesDemandes (:907-962), MajListeDemande et demandeValidee (:964-1027), OnProcessusEventAfterChild
/// (:498-558) ; et le moteur des evenements de RigMetier (Outils\DLL\RigMetier\JUDICIAIRE\EvenementProcedure.cs :
/// PasserEnSurvenuEveProFutur :1968-1997, PasserEnExpirerEveProFutur :2007-2036, ...AvecEveProSurvenuSpecifique :2040-2087,
/// creationEvenementSurvenuDepuisExpire :1816-1846 ; Instance.CreateInstanceOPSurIP, JUDICIAIRE\Instance.cs:3399-3530).
///
/// LES SAISIES (Parametres du descripteur, l'operation de recherche OPE_RECHERCHE de RIG v2, que PreparationTraitements relit,
/// :630-636) : « Juge » (cboJuge, MetierObligatoire, OPE_RECHERCHE.Designer.cs:133 ; abrege d'un juge actif), « Greffier » et
/// « Date prévue » (facultatifs) -- ils ne servent qu'a l'opposition (OP) creee ; et les cases « LRAR » et « LR » de la grille de
/// RIG v2 (cellules LRAR / LR, une par ligne ; la case d'en-tete les pose sur toutes, :262-283) : ici une fois pour le lot, LRAR
/// l'emportant sur LR comme dans le code (:744-760). POURQUOI une fois : la liste a cocher de la V3 (FormeFile.tsx) envoie UNE
/// saisie par geste pour toutes les lignes cochees (GesteEcran.Valeurs, un dictionnaire libelle -> valeur), pas une cellule par
/// ligne. Le meme resultat que la V2 s'obtient en deux passages : cocher les IP a envoyer en LRAR, cocher « LRAR », appliquer ;
/// puis les autres. RIG v2 grise ces cases sur une ligne qui enrole une OP (:151-166) : elles y sont sans effet.
///
/// CONTROLES, avant toute ecriture :
///   1. « « Juge » est obligatoire. » (l'etape de recherche) ; un juge inconnu, inactif ou en double est refuse ;
///   2. « Vous devez sélectionner au moins une IP à traiter » (DoVerifier, :506-508), au mot pres ;
///   3. par ligne, comme PreparationTraitements, sans arreter les autres (RIG v2 affiche la liste des erreurs, :862-875, et traite
///      le reste) : l'instance introuvable -- « Impossible de traiter l'instance N. Problème de récupération » (:850) ;
///      l'evenement plus futur (survenu ou expire entre-temps) -- « L'évènement futur n'existe plus. Impossible de traiter
///      l'instance N. Problème de récupération » (:847) ; l'instance verrouillee par une autre demande -- « Impossible de traiter
///      l'instance N » suivi du motif du verrou (CreateAndLockDemande, :842 ; VerrouDemandeV2) ; l'OP impossible a creer (code
///      affaire absent ou mal parametre, juge sans statut : Instance.cs:3403-3420, :3446-3450) -- « Problème à la création de l'OP
///      pour l'instance N. » (le message enveloppe que RIG v2 affiche, :3527). Une ligne en erreur n'ecrit rien. Si AUCUNE ligne
///      n'est traitee, le geste est refuse avec ces erreurs (RIG v2 reste en saisie quand toutes sont verrouillees, :524-527).
///
/// CE QUI S'ECRIT, par ligne :
///   - v2.DEMANDE « SURVIP » de l'IP (CreateAndLockDemande(false, "SURVIP"), :839), validee a la fin (demandeValidee) : son affaire
///     (celle de l'IP), son nom (le nom courant), son indicatif et ses numeros (le role de l'IP, puis celui de l'OP), son lien 2J ;
///   - selon le groupe (:690-720) :
///       Titre exécutoire       : l'opposition attendue (IOIP / IPOPOS) passe EXPIREE et l'evenement survenu de son code
///                                « expire » (R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE : IPEXECUT dans RIG_DEV) est cree du jour ; avec LRAR,
///                                le code + « LR » s'il existe (IPEXECUTLR) ; avec LR, le code + « R » (IPEXECUTR) (:740-816) ;
///       IP Non avenue          : si le reglage IP_NONAVENUE_EVT_SI_PAS_ENROLER_OP est donne (IPCNAV dans RIG_DEV), la signification
///                                attendue expire avec CET evenement survenu (+ « LR » / + « R » selon la case :
///                                CODE_EVE_PRO.GetCodeEveProNonAvenueSiPasEnrolerOp, CODE_EVE_PRO.cs:216-236) ; sinon une OP
///                                « IPNAVSO » est enrolee et la signification expire avec son code « expire » (IPNAV) ;
///       Caducité               : idem, reglage IP_CADUCITE_EVT_SI_PAS_ENROLER_OP (vide dans RIG_DEV : OP « IPCADSO »), code
///                                « expire » de la consignation (IPCAD) ;
///       Certif. non opposition : l'evenement IPCERTNOPP SURVIENT du jour (dates mini/maxi/fixe effacees), et la derniere
///                                opposition encore attendue (IOIP / IPOPOS future, la plus tardive) passe expiree, sans
///                                evenement survenu (:724-736).
///     Un evenement survenu cree depuis un expire porte le numero source de l'expire (EVNPR_NUMERO_SOURCE_EP_LIE), son texte est
///     efface pour etre reconstruit (EVNPR_TEXTE NULL) ; sans code « expire », l'evenement expire sans evenement survenu ;
///   - l'OP (CreateInstanceOPSurIP puis ValiderInstancesDemandes) : son affaire et sa demande « OPE » validee (role de l'OP, numeros
///     « role IP role OP », lien 2J) ; v2.INSTANCE du code affaire (type, nature, nature particuliere, acte de saisine du code),
///     saisie, enrolement et acte du jour, etat « 0 », etat info « 1 », numero source neuf, role de la serie du code (annee en
///     cours) et repertoire general JGAFF, JOINTE a l'IP (INSTN_ID_INSTN_INSTANCE_PRINCIPA, date de jonction du jour) ; les
///     parties : le JUGE en demandeur (designation = libelle de son statut, civilite « PM »), les demandeurs de l'IP en defendeurs,
///     ses defendeurs en intervenants (Partie.DupliquerPartie, Partie.cs:1357-1395, les colonnes qu'elle recopie), « copie de la
///     decision » -- posee aussi sur les parties de l'IP (:3459-3468, puis instanceIP.Sauve) ; le dossier judiciaire JRDSJ d'une
///     OP de type RP sans PC (:927-944 : nom = le 1er defendeur, sinon « Inconnu ») ; les evenements de creation du code affaire,
///     survenus du jour (:3478-3490) ; la decision du type du code affaire (date prevue, juge, greffier, ressort, contradictoire,
///     avant dire droit, archivage « 0 », :3494-3505) et son ordonnance en file d'edition si le type a un parametre d'edition
///     (« <libelle> <role IP> », entite DECI_JUDICIAIRE, :3509-3520 ; RelancesRsurv.EditionAsync).
/// Une IP traitee sort de la liste : son evenement futur n'est plus futur (la regle du lecteur).
///
/// LES EDITIONS des evenements survenus (eveProSurvenu.CreateDemandeEdition, :822-835), sur la demande SURVIP, et celles des
/// evenements de creation de l'OP, sur sa demande OPE (Instance.cs:3471-3476) : EditionsEvenement.cs (maquettes CEP_EDITION du
/// code, une ligne v2.DEMANDE_EDITION par destinataire) ; ce qu'il ne reprend pas est dit dans le message.
///
/// NON REPRIS (nommes, le message du geste le dit) : la reedition de l'ordonnance en TITRE
/// EXECUTOIRE (TrtTitreExecu, :1029-1051 : fichier PDF de la GED Vidiged, service externe) ; les pieces numerisees
/// (GererPiecesNum) ; la ligne de rapport general ; les MOTS DIRECTEURS de l'OP (GenererMotDirecteur, :552-554) ; l'impression
/// demat du juge signataire (JUG_SIGNATURE_DEMAT_TYPE_INSTANCES, :3513-3515). La facture : RIG v2 ne facture pas en SURVIP
/// (l'avertissement de DoVerifier, :509-511).
/// </summary>
internal sealed class EcrivainSurveillanceIp : IEcrivainEcran
{
    public string Procedure => "PROC_SURVIP";
    public string Ecran => "Injonctions de payer";
    public IReadOnlyCollection<TypeGeste> Gestes { get; } = [TypeGeste.Agir];

    /// <summary>Les Parametres du descripteur PROC_SURVIP (Pont/EcransGeneres/Surveiller.cs), au mot pres.</summary>
    internal const string ParametreJuge = "Juge", ParametreGreffier = "Greffier", ParametreDatePrevue = "Date prévue",
                          ParametreLrar = "LRAR", ParametreLr = "LR";

    /// <summary>Le refus de DoVerifier sans ligne cochee (OPE_TRAITEMENTS.cs:507), au mot pres.</summary>
    internal const string AucuneIp = "Vous devez sélectionner au moins une IP à traiter";

    /// <summary>Le groupe (le TypeTrt de RIG v2) d'un evenement futur, par son code (les requetes du lecteur).</summary>
    internal static string? TypeDe(string code) => code switch
    {
        "IOIP" or "IPOPOS" => TitreExecutoire,
        "ISIGNI" or "IPSIGNIF" => NonAvenue,
        "ICONSOIP" or "IPCONSOPOS" => Caducite,
        "IPCERTNOPP" => CertifNonOpposition,
        _ => null,
    };

    private sealed record Echeance(int IdEvenement, string Code, bool Futur, string? NumeroSource, int IdInstance, string Role, string NomCourant,
                                   int? IdAffaire);

    private sealed record CodeOp(string Valeur, string TypeNumRole, string TypeInstance, string ActeSaisine, string Nature, string NatAffPart,
                                 string TypeDecision, string? EvenementsCreation, string? ParametreEdition);

    private sealed record Juge(int Id, string Statut);

    public async Task<IssueGeste> JouerAsync(GesteEnCours geste, CancellationToken ct)
    {
        string S(string libelle) => geste.Demande.Valeurs.TryGetValue(libelle, out var v) ? v.Trim() : "";
        static bool Coche(string v) => v.ToLowerInvariant() is "oui" or "true" or "1";

        // 1. L'etape de recherche : le juge (obligatoire), le greffier et la date prevue (facultatifs).
        if (S(ParametreJuge).Length == 0) return IssueGeste.Refuser(CreationV2.Obligatoire(ParametreJuge));
        var idJuge = await InjonctionsIsurv.PersonneAsync(geste, "v2.JUGE", "JUG_ID_JUG", "JUG_ABREGE", "JUG_ACTIF", S(ParametreJuge), ct);
        if (idJuge is null)
            return IssueGeste.Refuser($"Le juge « {S(ParametreJuge)} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
        int? idGreffier = null;
        if (S(ParametreGreffier) is { Length: > 0 } abregeGreffier)
        {
            idGreffier = await InjonctionsIsurv.PersonneAsync(geste, "v2.GREFFIER_ASSERMENTE", "GRASS_ID_GRASS", "GRASS_ABREGE", "GRASS_ACTIF", abregeGreffier, ct);
            if (idGreffier is null)
                return IssueGeste.Refuser($"Le greffier « {abregeGreffier} » n'est pas utilisable en saisie dans la base du greffe (abrégé inconnu, en double ou inactif).");
        }
        DateTime? datePrevue = null;
        if (S(ParametreDatePrevue) is { Length: > 0 } dp)
        {
            if (AudiencesV2.Date(dp) is not { } d) return IssueGeste.Refuser($"« {ParametreDatePrevue} » : date illisible ({dp}).");
            datePrevue = d;
        }
        var lrar = Coche(S(ParametreLrar));
        var lr = !lrar && Coche(S(ParametreLr));

        // 2. Au moins une IP cochee.
        var ids = geste.Demande.Ids.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return IssueGeste.Refuser(AucuneIp);

        var juge = await JugeAsync(geste, idJuge.Value, ct);
        var reglages = await ReglagesAsync(geste, ct);
        var colonnesEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var faits = new List<string>();
        var erreurs = new List<string>();
        var traces = new List<TraceGeste>();
        var objets = new List<string>();
        var editions = new List<string>();
        var bilans = new List<EditionsEvenement.Bilan>();

        // 3. Ligne par ligne (PreparationTraitements, :641-861).
        foreach (var id in ids)
        {
            var e = int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? await EcheanceAsync(geste, n, ct) : null;
            if (e is null) { erreurs.Add($"Impossible de traiter l'instance {id}. Problème de récupération"); continue; }
            var type = TypeDe(e.Code);
            if (type is null) { erreurs.Add($"Impossible de traiter l'instance {e.Role}. Problème de récupération"); continue; }
            if (!e.Futur)
            {
                erreurs.Add($"L'évènement futur n'existe plus. Impossible de traiter l'instance {e.Role}. Problème de récupération");
                continue;
            }
            var idInstance = e.IdInstance.ToString(CultureInfo.InvariantCulture);
            if (await VerrouDemandeV2.MotifAsync(geste.Base, geste.Transaction, "Instance", VerrouDemandeV2.SequenceurInstance, idInstance, ct) is { } verrou)
            {
                erreurs.Add($"Impossible de traiter l'instance {e.Role} {verrou}");
                continue;
            }

            // L'OP a enroler (Caducite ou Non avenue sans reglage « si pas enroler OP ») : lue et controlee AVANT d'ecrire.
            var reglage = type == Caducite ? reglages.Caducite : type == NonAvenue ? reglages.NonAvenue : null;
            CodeOp? codeOp = null;
            if ((type == Caducite || type == NonAvenue) && reglage is null)
            {
                codeOp = await CodeOpAsync(geste, type == Caducite ? "IPCADSO" : "IPNAVSO", ct);
                if (codeOp is null || juge is null) { erreurs.Add($"Problème à la création de l'OP pour l'instance {e.Role}."); continue; }
            }

            // La demande SURVIP de l'IP.
            var (idDemande, numeroDemande, refusDemande) = await DemandeInstanceAsync(geste, "SURVIP", e.IdInstance, e.IdAffaire, e.NomCourant, e.Role, ct);
            if (idDemande is null) return IssueGeste.Refuser(refusDemande!);

            // L'OP.
            (int Id, string Role, string? Ordonnance)? op = null;
            if (codeOp is not null)
            {
                var (creee, refusOp) = await CreerOpAsync(geste, e, codeOp, juge!, idGreffier, datePrevue, bilans, ct);
                if (creee is null) return IssueGeste.Refuser(refusOp!);
                op = creee;
                if (creee.Value.Ordonnance is { } o) editions.Add(o);
                await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_LES_NUMEROS = LEFT(@n, 128) WHERE DMND_ID_DMND = @d", ct,
                    new SqlParameter("@n", $"{e.Role} {creee.Value.Role}"), new SqlParameter("@d", idDemande.Value));
            }

            // L'evenement futur : expire (avec son survenu) ou survient.
            string? survenu;
            if (type == CertifNonOpposition)
            {
                await geste.EcrireAsync(
                    $"UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_SURVENU = 1, EVNPR_DATE = @jour, EVNPR_DATE_MAXI = NULL, EVNPR_DATE_MINI = NULL, EVNPR_DATE_FIXE = NULL{Texte(colonnesEve)} WHERE EVNPR_ID_EVNPR = @id",
                    ct, new SqlParameter("@jour", geste.Maintenant.Date), new SqlParameter("@id", e.IdEvenement));
                survenu = e.Code;
                // Ses editions (eveProSurvenu.CreateDemandeEdition, :822-835), sur la demande SURVIP.
                bilans.Add(await EditionsEvenement.CreerAsync(geste, idDemande.Value, numeroDemande, e.IdEvenement, ct));
                // La derniere opposition encore attendue passe expiree (:727-735), sans evenement survenu.
                await geste.EcrireAsync($"""
                    UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DELAI_EXPIRE = 1{Texte(colonnesEve)}
                    WHERE EVNPR_ID_EVNPR = (SELECT TOP (1) o.EVNPR_ID_EVNPR FROM v2.EVENEMENT_PROCEDURE AS o
                                            WHERE o.EVNPR_ID_INSTN = @i AND o.EVNPR_SURVENU = 0 AND o.EVNPR_DELAI_EXPIRE = 0
                                              AND o.EVNPR_CODE_EVE_PRO IN ('IOIP', 'IPOPOS')
                                            ORDER BY COALESCE(o.EVNPR_DATE_MAXI, o.EVNPR_DATE_MINI, o.EVNPR_DATE_FIXE) DESC, o.EVNPR_ID_EVNPR DESC)
                    """, ct, new SqlParameter("@i", e.IdInstance));
            }
            else
            {
                string? code;
                if (reglage is { } r)
                    code = lrar ? r.Lrar : lr ? r.Lr : r.Code;   // GetCodeEvePro...SiPasEnrolerOp : « LR » / « R » s'ils existent
                else
                {
                    code = await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_CODE_EVE_PRO_EXPIRE FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                        new SqlParameter("@c", e.Code)) as string is { Length: > 0 } x ? x.Trim() : null;
                    // Une ligne qui enrole une OP a ses cases grisees (:151-166).
                    if (code is not null && codeOp is null && (lrar || lr)) code = await SuffixeAsync(geste, code, lrar ? "LR" : "R", ct);
                }
                int? idSurvenu;
                (survenu, idSurvenu) = await ExpirerAsync(geste, e, code, colonnesEve, ct);
                // Les editions de l'evenement survenu (:822-835) ; un expire sans survenu n'en a pas.
                if (idSurvenu is { } ids2) bilans.Add(await EditionsEvenement.CreerAsync(geste, idDemande.Value, numeroDemande, ids2, ct));
            }

            var quoi = FormeLigne.Joindre($"IP {e.Role} {Libelle(type)}", survenu is null ? null : $"événement {survenu}",
                                          op is { } o2 ? $"opposition {o2.Role} enrôlée" : null);
            faits.Add(quoi);
            objets.Add(e.IdEvenement.ToString(CultureInfo.InvariantCulture));
            traces.Add(new TraceGeste("affaire", $"l'injonction de payer {e.Role}", idInstance, "a modifie",
                new Dictionary<string, string?> { ["Evenement"] = $"{e.Code} attendu" },
                new Dictionary<string, string?>
                {
                    ["Traitement"] = type, ["Evenement"] = type == CertifNonOpposition ? $"{e.Code} survenu" : $"{e.Code} expiré",
                    ["Survenu"] = survenu, ["Demande"] = numeroDemande, ["Opposition"] = op?.Role,
                    ["Envoi"] = codeOp is null && type != CertifNonOpposition ? lrar ? "LRAR" : lr ? "LR" : null : null,
                }));
        }

        if (faits.Count == 0) return IssueGeste.Refuser(string.Join(Environment.NewLine, erreurs));
        var nb = faits.Count;
        var message = $"Traitements appliqués à {nb} injonction{(nb > 1 ? "s" : "")} de payer : {string.Join(" ; ", faits)}"
                    + (editions.Count == 0 ? "" : $" ; ordonnance{(editions.Count > 1 ? "s" : "")} « {string.Join(" », « ", editions.Distinct())} » en file d'édition")
                    + "." + EditionsEvenement.Message(bilans)
                    + " Non repris par la V3 : la réédition de l'ordonnance en titre exécutoire (fichier de la GED) et les mots directeurs des oppositions."
                    + (erreurs.Count == 0 ? "" : " Erreurs : " + string.Join(" ; ", erreurs));
        return IssueGeste.Ecrit(message, [.. objets], [.. traces]);
    }

    /// <summary>
    /// La demande validee d'un processus ouvert sur une instance existante (CreateAndLockDemande puis demandeValidee,
    /// OPE_TRAITEMENTS.cs:839 et :1000-1027 ; la demande du processus IPSUIVI, MajListeDemande) : de l'affaire de l'instance, a son
    /// nom courant, son role en indicatif et en numeros, liee a l'instance (2J). RelancesRsurv.DemandeAsync l'ecrit validee.
    /// </summary>
    internal static async Task<(int? Id, string? Numero, string? Refus)> DemandeInstanceAsync(GesteEnCours geste, string processus, int idInstance,
        int? idAffaire, string nom, string role, CancellationToken ct)
    {
        var (id, numero, refus) = await RelancesRsurv.DemandeAsync(geste, null, nom, role, null, null, null, ct, processus);
        if (id is null) return (null, null, refus);
        if (idAffaire is { } affaire)
            await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_ID_AFFRE = @a WHERE DMND_ID_DMND = @d", ct, new SqlParameter("@a", affaire), new SqlParameter("@d", id.Value));
        await geste.InsererAsync("v2.DOSSIER_DEMANDE", "DSSDM_ID_DSSDM", new Dictionary<string, object?>
        {
            ["DSSDM_ID_DMND"] = id.Value, ["DSSDM_CODE_SEQUENCEUR"] = VerrouDemandeV2.SequenceurInstance,
            ["DSSDM_VALEUR_SEQUENCEUR"] = idInstance.ToString(CultureInfo.InvariantCulture),
        }, ct);
        return (id, numero, null);
    }

    /// <summary>« , EVNPR_TEXTE = NULL » si la table porte la colonne (le texte se reconstruit a la sauvegarde dans RIG v2).</summary>
    private static string Texte(HashSet<string> colonnes) => colonnes.Contains("EVNPR_TEXTE") ? ", EVNPR_TEXTE = NULL" : "";

    private static string Libelle(string type) => type switch
    {
        TitreExecutoire => "en titre exécutoire",
        NonAvenue => "non avenue",
        Caducite => "caduque",
        _ => "certificat de non-opposition",
    };

    /// <summary>L'evenement <paramref name="code"/> + <paramref name="suffixe"/> s'il existe dans CODE_EVE_PRO, sinon <paramref name="code"/>
    /// (OPE_TRAITEMENTS.cs:766-772 pour LRAR, :793-799 pour LR).</summary>
    private static async Task<string> SuffixeAsync(GesteEnCours geste, string code, string suffixe, CancellationToken ct)
        => await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_VALEUR FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                        new SqlParameter("@c", code + suffixe)) is string s ? s.Trim() : code;

    /// <summary>L'evenement futur passe expire ; si <paramref name="code"/> est donne, l'evenement survenu de ce code est cree du jour,
    /// lie a l'expire par son numero source (creationEvenementSurvenuDepuisExpire, EvenementProcedure.cs:1816-1846). Rend le code et l'id crees.</summary>
    private static async Task<(string? Code, int? Id)> ExpirerAsync(GesteEnCours geste, Echeance e, string? code, HashSet<string> colonnes, CancellationToken ct)
    {
        var source = e.NumeroSource;
        if (source is null && colonnes.Contains("EVNPR_NUMERO_SOURCE")) source = Guid.NewGuid().ToString().ToUpperInvariant();
        await geste.EcrireAsync(
            $"UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_DELAI_EXPIRE = 1{Texte(colonnes)}{(source is not null && e.NumeroSource is null ? ", EVNPR_NUMERO_SOURCE = @s" : "")} WHERE EVNPR_ID_EVNPR = @id",
            ct, new SqlParameter("@id", e.IdEvenement), new SqlParameter("@s", (object?)source ?? DBNull.Value));
        if (code is null) return (null, null);
        var idSurvenu = await EcrivainRetourAudience.EvenementSurvenuAsync(geste, e.IdInstance, code, geste.Maintenant.Date, ct);
        if (source is not null && colonnes.Contains("EVNPR_NUMERO_SOURCE_EP_LIE"))
            await geste.EcrireAsync("UPDATE v2.EVENEMENT_PROCEDURE SET EVNPR_NUMERO_SOURCE_EP_LIE = @s WHERE EVNPR_ID_EVNPR = @id", ct,
                new SqlParameter("@s", source), new SqlParameter("@id", idSurvenu));
        return (code, idSurvenu);
    }

    /// <summary>Les reglages « si pas enroler OP » (PARAMETRAGE de RIG v2, repris dans soc.Reglage) : le code et ses variantes LRAR / LR
    /// (CODE_EVE_PRO.cs:193-236) ; null si le reglage est vide ou absent (RIG v2 : GetValeurParametrage(code, "") = « »).</summary>
    private sealed record Reglage(string Code, string Lrar, string Lr);

    private static async Task<(Reglage? Caducite, Reglage? NonAvenue)> ReglagesAsync(GesteEnCours geste, CancellationToken ct)
    {
        if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'soc.Reglage')", ct) is DBNull or null) return (null, null);
        async Task<Reglage?> Lire(string cle)
        {
            if (await RelancesRsurv.Scalaire(geste, "SELECT Valeur FROM soc.Reglage WHERE Code = @c", ct, new SqlParameter("@c", cle)) is not string { Length: > 0 } v
                || v.Trim().Length == 0)
                return null;
            var code = v.Trim();
            var lrar = await SuffixeAsync(geste, code, "LR", ct);
            // Sans code « R », RIG v2 laisse la variante LR vide (le ref n'est pas pose, :226-230) : l'expire se fait alors sans
            // evenement survenu -- hypothese sur AddEvenementProcedureSurvenu(""), aucun code « IPCNAV » sans « R » dans RIG_DEV.
            var r = await RelancesRsurv.Scalaire(geste, "SELECT R_CODEEVEPRO_VALEUR FROM v2.CODE_EVE_PRO WHERE R_CODEEVEPRO_VALEUR = @c", ct,
                                                 new SqlParameter("@c", code + "R")) as string;
            return new Reglage(code, lrar, r?.Trim() ?? "");
        }
        return (await Lire("IP_CADUCITE_EVT_SI_PAS_ENROLER_OP"), await Lire("IP_NONAVENUE_EVT_SI_PAS_ENROLER_OP"));
    }

    private static async Task<Echeance?> EcheanceAsync(GesteEnCours geste, int id, CancellationToken ct)
    {
        var colonnesEve = await RelancesRsurv.ColonnesAsync(geste, "v2.EVENEMENT_PROCEDURE", ct);
        var colonnesIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        await using var r = await geste.LireAsync($"""
            SELECT ev.EVNPR_ID_EVNPR, ev.EVNPR_CODE_EVE_PRO,
                   CAST(CASE WHEN ISNULL(ev.EVNPR_SURVENU, 0) = 0 AND ISNULL(ev.EVNPR_DELAI_EXPIRE, 0) = 0 THEN 1 ELSE 0 END AS bit) AS Futur,
                   {(colonnesEve.Contains("EVNPR_NUMERO_SOURCE") ? "ev.EVNPR_NUMERO_SOURCE" : "CAST(NULL AS varchar(40))")} AS Source,
                   i.INSTN_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
                   {(colonnesIns.Contains("INSTN_ID_AFFRE") ? "i.INSTN_ID_AFFRE" : "CAST(NULL AS int)")} AS Affaire
            FROM v2.EVENEMENT_PROCEDURE AS ev WITH (UPDLOCK, HOLDLOCK)
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = ev.EVNPR_ID_INSTN AND i.INSTN_TYPE_INSTANCE = 'IP'
            WHERE ev.EVNPR_ID_EVNPR = @id
            """, ct, new SqlParameter("@id", id));
        if (!await r.ReadAsync(ct)) return null;
        return new Echeance(id, FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO").Trim(), r["Futur"] is true,
            FormeLigne.Texte(r, "Source") is { Length: > 0 } s ? s : null, Convert.ToInt32(r["INSTN_ID_INSTN"], CultureInfo.InvariantCulture),
            FormeLigne.Texte(r, "INSTN_NUM_ROLE"), FormeLigne.Texte(r, "INSTN_NOM_COURANT"),
            r["Affaire"] is DBNull ? null : Convert.ToInt32(r["Affaire"], CultureInfo.InvariantCulture));
    }

    /// <summary>Le juge saisi et le libelle de son statut (Juge.Statut, Juge.cs:238-242 ; STATUT.R_STATUT_LIBELLE_ECRAN) ; null s'il n'a
    /// pas de statut (RIG v2 ne peut alors pas creer le demandeur de l'OP, Instance.cs:3446-3450).</summary>
    private static async Task<Juge?> JugeAsync(GesteEnCours geste, int id, CancellationToken ct)
        => await RelancesRsurv.Scalaire(geste, """
               SELECT s.R_STATUT_LIBELLE_ECRAN FROM v2.JUGE AS j JOIN v2.STATUT AS s ON s.R_STATUT_VALEUR = j.JUG_STATUT WHERE j.JUG_ID_JUG = @id
               """, ct, new SqlParameter("@id", id)) is string { Length: > 0 } statut ? new Juge(id, statut.Trim()) : null;

    /// <summary>Le code affaire de l'OP et ce que CreateInstanceOPSurIP exige (Instance.cs:3403-3420) ; null s'il manque ou est mal
    /// parametre (RIG v2 leve et la ligne n'est pas traitee).</summary>
    private static async Task<CodeOp?> CodeOpAsync(GesteEnCours geste, string code, CancellationToken ct)
    {
        CodeOp? c = null;
        await using (var r = await geste.LireAsync("""
            SELECT R_CODEAFFAIR_VALEUR, R_CODEAFFAIR_TYPE_NUM_ROLE, R_CODEAFFAIR_TYPE_INSTANCE, R_CODEAFFAIR_CODE_ACTE_PRO, R_CODEAFFAIR_NATURE_AFF,
                   R_CODEAFFAIR_NAT_AFF_PART, R_CODEAFFAIR_TYPE_DECISION, R_CODEAFFAIR_CODE_EVE_PRO_CREA
            FROM v2.CODE_AFFAIRE WHERE R_CODEAFFAIR_VALEUR = @c
            """, ct, new SqlParameter("@c", code)))
            if (await r.ReadAsync(ct))
            {
                string? O(string col) => FormeLigne.Texte(r, col).Trim() is { Length: > 0 } s ? s : null;
                if (O("R_CODEAFFAIR_CODE_ACTE_PRO") is { } acte && O("R_CODEAFFAIR_NATURE_AFF") is { } nature && O("R_CODEAFFAIR_NAT_AFF_PART") is { } part
                    && O("R_CODEAFFAIR_TYPE_DECISION") is { } type && O("R_CODEAFFAIR_TYPE_NUM_ROLE") is { } num && O("R_CODEAFFAIR_TYPE_INSTANCE") is { } ti)
                    c = new CodeOp(O("R_CODEAFFAIR_VALEUR")!, num, ti, acte, nature, part, type, O("R_CODEAFFAIR_CODE_EVE_PRO_CREA"), null);
            }
        if (c is null) return null;
        var parametre = await RelancesRsurv.Scalaire(geste, "SELECT R_TYPEDECISI_PARAMETRE_EDITION FROM v2.TYPE_DECISION WHERE R_TYPEDECISI_VALEUR = @t", ct,
                                                     new SqlParameter("@t", c.TypeDecision)) as string;
        return c with { ParametreEdition = parametre is { Length: > 0 } p && p.Trim().Length > 0 ? p.Trim() : null };
    }

    // Les colonnes que Partie.DupliquerPartie recopie (Partie.cs:1357-1395), dans son ordre.
    private static readonly string[] ColonnesPartie =
    [
        "PRT_ID_CLIEN", "PRT_NUM_ORDRE", "PRT_ID_MNDTR_POS", "PRT_ID_MNDTR_PLAI", "PRT_ID_RPRTR", "PRT_ID_PRTRP", "PRT_ID_ADRBS", "PRT_ABREGE",
        "PRT_CIVILITE", "PRT_DESIGNATION", "PRT_NOM", "PRT_PROFESSION", "PRT_FORME_JURIDIQUE_UTI", "PRT_CODE_GREFFE", "PRT_SIREN",
        "PRT_DATE_NAISSANCE", "PRT_NATIONALITE", "PRT_ORGANE", "PRT_MODE_PRESENTATION", "PRT_TEXTE_ASSISTANCE", "PRT_IDEM_PRESENTATION",
        "PRT_TEXTE_ESQUALITE", "PRT_TEXTE_COMPLEMENTAIRE", "PRT_ADRESSE_MAIL", "PRT_REFERENCES", "PRT_QUALIF_MANDATAIRE_POS",
        "PRT_QUALIF_MANDATAIRE_PLAI", "PRT_DATE_EFFET", "PRT_DATE_FIN", "PRT_EDITION_JUGEMENT", "PRT_MANDATAIRE_EDITION_JUGEMENT",
        "PRT_TELEPHONE", "PRT_FAX", "PRT_PORTABLE", "PRT_DATE_ACTE", "PRT_MANDATAIRE_PLAI_EDITION_JUGEMENT", "PRT_ID_DSRCS",
        "PRT_ID_NINSC", "PRT_AIDE_JURIDIC_TOTALE", "PRT_AIDE_JURIDIC_PARTIELLE", "PRT_AIDE_JURIDIC_NUM",
    ];

    /// <summary>CreateInstanceOPSurIP puis ValiderInstancesDemandes (en tete du fichier) : l'OP, ses parties, son dossier judiciaire,
    /// ses evenements, sa decision et son ordonnance, et sa demande OPE validee.</summary>
    private static async Task<((int Id, string Role, string? Ordonnance)? Op, string? Refus)> CreerOpAsync(GesteEnCours geste, Echeance ip, CodeOp code,
        Juge juge, int? idGreffier, DateTime? datePrevue, List<EditionsEvenement.Bilan> bilans, CancellationToken ct)
    {
        var jour = geste.Maintenant.Date;
        var annee = geste.Maintenant.Year;
        var (role, refusRole) = await CreationV2.NumeroAsync(geste, code.TypeNumRole, annee, ct);
        if (role is null) return (null, refusRole);
        var (repertoire, refusRep) = await CreationV2.NumeroAsync(geste, "JGAFF", annee, ct);
        if (repertoire is null) return (null, refusRep);

        // Les parties de l'IP, lues avant d'ecrire : demandeurs (D%) et defendeurs (F%), par ordre.
        var presentesPartie = await RelancesRsurv.ColonnesAsync(geste, "v2.PARTIE", ct);
        var copiees = ColonnesPartie.Where(presentesPartie.Contains).ToArray();
        var partiesIp = new List<(string Qualif, Dictionary<string, object?> Valeurs)>();
        await using (var r = await geste.LireAsync($"""
            SELECT PRT_QUALIF_PARTIE, {string.Join(", ", copiees)} FROM v2.PARTIE
            WHERE PRT_ID_INSTN = @i AND (PRT_QUALIF_PARTIE LIKE 'D%' OR PRT_QUALIF_PARTIE LIKE 'F%') ORDER BY PRT_NUM_ORDRE, PRT_ID_PRT
            """, ct, new SqlParameter("@i", ip.IdInstance)))
            while (await r.ReadAsync(ct))
            {
                var v = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in copiees) v[c] = r[c] is DBNull ? null : r[c];
                partiesIp.Add((FormeLigne.Texte(r, "PRT_QUALIF_PARTIE"), v));
            }
        var demandeursIp = partiesIp.Where(p => p.Qualif.StartsWith('D')).Select(p => p.Valeurs).ToList();
        var defendeursIp = partiesIp.Where(p => p.Qualif.StartsWith('F')).Select(p => p.Valeurs).ToList();

        // La demande OPE et son affaire.
        var (demande, refusDemande) = await CreationV2.CreerDemandeAsync(geste, "OPE", "V", ct);
        if (demande is null) return (null, refusDemande);
        var premierDefendeur = demandeursIp.FirstOrDefault();   // le 1er defendeur de l'OP = le 1er demandeur de l'IP
        var nomCourant = CreationV2.NomCourant(juge.Statut, premierDefendeur?["PRT_DESIGNATION"] as string);
        var colonnesIns = await RelancesRsurv.ColonnesAsync(geste, "v2.INSTANCE", ct);
        var instance = new Dictionary<string, object?>
        {
            ["INSTN_ID_AFFRE"] = demande.IdAffaire, ["INSTN_CODE_AFFAIRE"] = code.Valeur, ["INSTN_NATURE_AFFAIRE"] = code.Nature,
            ["INSTN_NAT_AFF_PART"] = code.NatAffPart, ["INSTN_CODE_ACTE_PROCE"] = code.ActeSaisine, ["INSTN_ETAT_INSTANCE"] = "0", ["INSTN_ETAT_INFO"] = "1",
            ["INSTN_DATE_SAISINE_GREFFE"] = jour, ["INSTN_DATE_ENROLEMENT"] = jour, ["INSTN_DATE_ACTE"] = jour, ["INSTN_TYPE_INSTANCE"] = code.TypeInstance,
            ["INSTN_NUMERO_SOURCE"] = Guid.NewGuid().ToString().ToUpperInvariant(), ["INSTN_NUM_ROLE"] = role, ["INSTN_NUM_REP_GENERAL"] = repertoire,
            ["INSTN_NOM_COURANT"] = nomCourant, ["INSTN_ID_INSTN_INSTANCE_PRINCIPA"] = ip.IdInstance, ["INSTN_DATE_JONCTION"] = jour,
        };
        var idOp = await CreationV2.InsererAsync(geste, "v2.INSTANCE", "INSTN_ID_INSTN",
            instance.Where(v => colonnesIns.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value), ct);

        // Les parties : le juge en demandeur, puis les doublons des parties de l'IP ; « copie de la decision » sur celles de l'IP aussi.
        await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", new Dictionary<string, object?>
        {
            ["PRT_ID_INSTN"] = idOp, ["PRT_NUM_ORDRE"] = (short)1, ["PRT_CIVILITE"] = "PM", ["PRT_DESIGNATION"] = juge.Statut, ["PRT_NOM"] = juge.Statut,
            ["PRT_QUALIF_PARTIE"] = "D",
        }, ct);
        foreach (var (liste, qualif) in new[] { (demandeursIp, "F"), (defendeursIp, "I") })
            foreach (var p in liste)
            {
                var v = new Dictionary<string, object?>(p, StringComparer.OrdinalIgnoreCase)
                {
                    ["PRT_ID_INSTN"] = idOp, ["PRT_QUALIF_PARTIE"] = qualif,
                };
                if (presentesPartie.Contains("PRT_COPIE_DECISION")) v["PRT_COPIE_DECISION"] = true;
                await CreationV2.InsererAsync(geste, "v2.PARTIE", "PRT_ID_PRT", v.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value), ct);
            }
        if (presentesPartie.Contains("PRT_COPIE_DECISION"))
            await geste.EcrireAsync("UPDATE v2.PARTIE SET PRT_COPIE_DECISION = 1 WHERE PRT_ID_INSTN = @i AND (PRT_QUALIF_PARTIE LIKE 'D%' OR PRT_QUALIF_PARTIE LIKE 'F%')",
                ct, new SqlParameter("@i", ip.IdInstance));

        // Le dossier judiciaire d'une OP RP sans PC (ValiderInstancesDemandes, :927-944).
        if (code.TypeInstance == "RP")
        {
            if (await RelancesRsurv.Scalaire(geste, "SELECT OBJECT_ID(N'v2.DOSSIER_JUDICIAIRE')", ct) is DBNull or null)
                return (null, "La table DOSSIER_JUDICIAIRE est absente de la base du greffe : RIG v2 y crée le dossier judiciaire de l'opposition, rien n'est traité.");
            var (numeroDossier, refusDossier) = await CreationV2.NumeroAsync(geste, "JRDSJ", jour.Year, ct);
            if (numeroDossier is null) return (null, refusDossier);
            var nomDossier = premierDefendeur?["PRT_DESIGNATION"] is string { Length: > 0 } && premierDefendeur["PRT_NOM"] is string nd ? nd : "Inconnu";
            var idDossier = await geste.InsererAsync("v2.DOSSIER_JUDICIAIRE", "DSJUD_ID_DSJUD", new Dictionary<string, object?>
            {
                ["DSJUD_NUM_DOSSIER"] = numeroDossier, ["DSJUD_NOM_DOSSIER"] = nomDossier,
            }, ct);
            if (colonnesIns.Contains("INSTN_ID_DSJUD"))
                await geste.EcrireAsync("UPDATE v2.INSTANCE SET INSTN_ID_DSJUD = @d WHERE INSTN_ID_INSTN = @i", ct,
                    new SqlParameter("@d", idDossier), new SqlParameter("@i", idOp));
        }

        // Les evenements de creation du code affaire, survenus du jour.
        // Leurs editions, sur la demande OPE (eveCA.CreateDemandeEdition(ddeOPE), Instance.cs:3471-3476).
        foreach (var eve in (code.EvenementsCreation ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idEve = await EcrivainRetourAudience.EvenementSurvenuAsync(geste, idOp, eve, jour, ct);
            bilans.Add(await EditionsEvenement.CreerAsync(geste, demande.Id, demande.Numero, idEve, ct));
        }

        // La decision du type du code affaire, et son ordonnance.
        var decision = (await EcrivainMiseAuPointChambres.CreerDecisionAsync(geste, idOp, role, code.TypeDecision, datePrevue, null, ct))[0];
        var idDecision = int.Parse(decision.IdObjet!, CultureInfo.InvariantCulture);
        await geste.EcrireAsync("""
            UPDATE v2.DECISION_JUDICIAIRE SET DCJDC_ID_JUG = @juge, DCJDC_ID_GRASS = @greffier, DCJDC_RESSORT = '0', DCJDC_CONTRADICTOIRE = '0',
                   DCJDC_AVANT_DIRE_DROIT = '0', DCJDC_ETAT_ARCHIVAGE = '0', DCJDC_MODE_ARCHIVAGE = '0'
            WHERE DCJDC_ID_DCJDC = @id
            """, ct, new SqlParameter("@juge", juge.Id), new SqlParameter("@greffier", (object?)idGreffier ?? DBNull.Value), new SqlParameter("@id", idDecision));
        string? ordonnance = null;
        if (code.ParametreEdition is { } parametre)
        {
            var source = await RelancesRsurv.Scalaire(geste, "SELECT DCJDC_NUMERO_SOURCE FROM v2.DECISION_JUDICIAIRE WHERE DCJDC_ID_DCJDC = @id", ct,
                                                      new SqlParameter("@id", idDecision)) as string ?? "";
            ordonnance = await RelancesRsurv.EditionAsync(geste, demande.Id, parametre, " " + ip.Role, "DECI_JUDICIAIRE", source, null, null, ct);
        }

        // La demande OPE validee (MajListeDemande, :969-976).
        await CreationV2.RattacherAsync(geste, demande, nomCourant, role, $"{ip.Role} {role}", VerrouDemandeV2.SequenceurInstance,
                                        idOp.ToString(CultureInfo.InvariantCulture), ct);
        await geste.EcrireAsync("UPDATE v2.DEMANDE SET DMND_DATE_VALIDATION = @m WHERE DMND_ID_DMND = @d", ct,
            new SqlParameter("@m", geste.Maintenant), new SqlParameter("@d", demande.Id));
        return ((idOp, role, ordonnance), null);
    }
}
