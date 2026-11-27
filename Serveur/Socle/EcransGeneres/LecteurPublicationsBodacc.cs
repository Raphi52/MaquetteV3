using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Publications BODACC » (PROC_GESTBOD, forme Operation) : les annonces BODACC des procedures et des preventions, dans les trois
/// listes de RIG v2 -- les rejets, les certificats de parution, les controles non conformes. Mode d'emploi des lecteurs :
/// LecteurEcran.cs ; le geste : EcrivainPublicationsBodacc.cs (plan « RIG v3 100 % fonctionnel », lot 11, 27/11/2026 ; reprise : forme
/// Operation, comme « Envoi JAL / BODACC », pour porter les criteres que la V2 exige).
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_GESTBOD (proc_gestbod.md, Detail OPE, qui fait foi : la seule table est BODACC,
/// jointe a EVENEMENT_PROCEDURE, PROCEDURE_COLLECTIVE et PREVENTION). L'affaire est celle de la procedure collective, sinon de la
/// prevention de l'evenement (RejetsBod.cs:41-71, CertifBod.cs:42-73) ; dans la base V3, BODACC est la table V2 telle quelle.
///
/// SANS CRITERE (LireAsync) : la seule liste que la V2 ouvre sans date, LES REJETS (Entree\OPE_LST_REJETS.MajSelectionBodacc, :55-90) :
///   BODACC LEFT JOIN EVENEMENT_PROCEDURE, BDCCR_ETAT_BODACC = '9' (« rejet Bodacc, a rectifier », ETAT_BODACC de RIG_DEV) et
///   BDCCR_TYPE_BODACC LIKE 'P%'. L'etat 3 (« rectif. (rejet) a envoyer ») est ce que la mise a jour des rejets ecrit (OPE_LST_LISTE_REJ
///   .DoSauver, :51-57) : la V2 les retire de sa liste au rechargement, la V3 les garde, figees, etat « A renvoyer » -- la relecture du
///   geste, jusqu'au prochain envoi BODACC (Envoi JAL / BODACC, qui les passe a 4, OPE_A_ENVOYER.cs:1342-1358).
/// AVEC CRITERES (ChercherAsync) : « Critere de date » (les boutons radio GestionBodacc, OPE_BASE_BOD.cs:12, :44-47 : « Date d'EP » --
///   le premier, pris quand rien n'est dit --, « Date de parution », « Date d'envoi »), « Date du », « Date au » ; ou « Numero de
///   parution ». Une date « au » sans « du » ne cherche rien (OPE_LST_REJETS :52-55, OPE_CERTIF_BOD :70-73 : return) ; « au » avant
///   « du » non plus (« La date 'au' doit être plus grande que la date 'du' », puis return, OPE_CERTIF_BOD.cs:75-80) ; sans « au », la
///   date est une egalite (= du), sinon BETWEEN, en jours (CONVERT(datetime, ..., 103)). Les trois listes :
///   - REJETS (OPE_LST_REJETS, :63-85) : par date d'EP (EVNPR_DATE ou BDCCR_DATE_EP_SUPPR, l'evenement supprime) ou d'envoi ; la V2
///     n'offre ni date de parution ni numero a ce mode : ils n'y cherchent rien ;
///   - CERTIFICATS DE PARUTION (OPE_CERTIF_BOD, :86-117) : par date d'EP (ou d'EP supprime), de parution (BDCCR_DATE_PUBLICITE), d'envoi,
///     ou par numero de parution (BDCCR_NUM_PARUTION = ...) ; BDCCR_ETAT_BODACC 5, 6 ou 10 et BDCCR_TYPE_BODACC LIKE 'P%' ; ordre
///     BDCCR_NUM_PARUTION, BDCCR_NUMERO_ANNONCE ;
///   - CONTROLES NON CONFORMES (OPE_LST_CTRLNC, :50-81) : BODACC INNER JOIN EVENEMENT_PROCEDURE, par date d'EP (EVNPR_DATE seule), de
///     parution ou d'envoi (pas de numero a ce mode), etats 5, 6 ou 10, BDCCR_CONTROLE_QUALITE renseigne (sans filtre de type).
/// L'Id d'une ligne est son groupe et l'annonce (« R-<id> », « C-<id> », « Q-<id> ») : une annonce certifiee et non conforme est dans
/// deux listes, et le geste n'y fait pas la meme chose.
/// REGLE D'ETAT : rejets -- 9 -> AFaire « A traiter », 3 -> EnAttente « A renvoyer » ; certificats et controles -- 5 (« publicite faite,
/// a verifier ») et 10 (« rectif. (rejet) fait, a verifier ») -> AFaire « A traiter », 6 (« publicite faite et verifiee ») -> EnCours
/// « Verifiee ». « Certifiee » (Validee) n'a pas de ligne : l'edition du certificat ne change pas l'etat (OPE_LST_CERTIF_BOD.cs:61-89).
/// </summary>
internal sealed class LecteurPublicationsBodacc : IRechercheEcran
{
    public string Procedure => "PROC_GESTBOD";

    /// <summary>Les groupes du descripteur, au mot pres.</summary>
    internal const string Certificats = "Certificats de parution", Rejets = "Rejets", Controles = "Contrôles non conformes";

    /// <summary>Les criteres du descripteur, au mot pres.</summary>
    internal const string CritereDate = "Critère de date", CritereDu = "Date du", CritereAu = "Date au", CritereNumero = "Numéro de parution";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereDu, CritereAu, CritereNumero];

    private const string Colonnes = """
        b.BDCCR_ID_BDCRC, b.BDCCR_ETAT_BODACC, b.BDCCR_DATE_ENVOI, b.BDCCR_DATE_PUBLICITE, b.BDCCR_NUM_PARUTION, b.BDCCR_NUMERO_ANNONCE,
        b.BDCCR_MOTIF_REJET_BODACC_1, b.BDCCR_CONTROLE_QUALITE, ev.EVNPR_CODE_EVE_PRO, ev.EVNPR_DATE, pc.PRCLL_NUM_PC, pc.PRCLL_NOM_AFFAIRE,
        pr.PREVE_NUM_PRV, pr.PREVE_NOM_AFFAIRE
        """;

    private const string Jointures = """
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON ev.EVNPR_ID_PRCLL = pc.PRCLL_ID_PRCLL
        LEFT JOIN v2.PREVENTION AS pr ON ev.EVNPR_ID_PREVE = pr.PREVE_ID_PREVE
        """;

    /// <summary>Les rejets (en tete) ; « /*filtre*/ » recoit la date ou une annonce (l'ecrivain).</summary>
    internal const string RequeteRejets = $"""
        SELECT TOP (@plafond) {Colonnes}, 'R' AS Liste, COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b LEFT JOIN v2.EVENEMENT_PROCEDURE AS ev ON b.BDCCR_ID_EVNPR = ev.EVNPR_ID_EVNPR
        {Jointures}
        WHERE b.BDCCR_ETAT_BODACC IN ('9', '3') AND b.BDCCR_TYPE_BODACC LIKE 'P%' /*filtre*/
        ORDER BY b.BDCCR_ID_BDCRC
        """;

    /// <summary>Les certificats de parution (en tete) ; « /*filtre*/ » recoit la date, le numero ou une annonce.</summary>
    internal const string RequeteCertificats = $"""
        SELECT TOP (@plafond) {Colonnes}, 'C' AS Liste, COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b LEFT JOIN v2.EVENEMENT_PROCEDURE AS ev ON b.BDCCR_ID_EVNPR = ev.EVNPR_ID_EVNPR
        {Jointures}
        WHERE b.BDCCR_ETAT_BODACC IN ('5', '6', '10') AND b.BDCCR_TYPE_BODACC LIKE 'P%' /*filtre*/
        ORDER BY b.BDCCR_NUM_PARUTION, b.BDCCR_NUMERO_ANNONCE, b.BDCCR_ID_BDCRC
        """;

    /// <summary>Les controles non conformes (en tete) ; « /*filtre*/ » recoit la date ou une annonce.</summary>
    internal const string RequeteControles = $"""
        SELECT TOP (@plafond) {Colonnes}, 'Q' AS Liste, COUNT(*) OVER () AS Total
        FROM v2.BODACC AS b INNER JOIN v2.EVENEMENT_PROCEDURE AS ev ON b.BDCCR_ID_EVNPR = ev.EVNPR_ID_EVNPR
        {Jointures}
        WHERE b.BDCCR_ETAT_BODACC IN ('5', '6', '10') AND b.BDCCR_CONTROLE_QUALITE IS NOT NULL /*filtre*/
        ORDER BY b.BDCCR_ID_BDCRC
        """;

    /// <summary>La requete de la liste d'un Id (« R- », « C- », « Q- ») ; null pour un autre prefixe.</summary>
    internal static string? RequeteDe(char liste) => liste switch
    {
        'R' => RequeteRejets, 'C' => RequeteCertificats, 'Q' => RequeteControles, _ => null,
    };

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, RequeteRejets.Replace("/*filtre*/", "", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    /// <summary>Les trois listes de la V2 sur les criteres saisis (en tete du fichier).</summary>
    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var numero = recherche.Critere(CritereNumero);
        var du = CriteresConsultation.Date(recherche.Critere(CritereDu));
        var au = CriteresConsultation.Date(recherche.Critere(CritereAu));
        if (numero is null && du is null && au is null) return await LireAsync(baseGreffe, aujourdhui, plafond, ct);

        var lignes = new List<LigneLue>();
        var total = 0;
        async Task Ajouter(string requete, string filtre, params SqlParameter[] p)
        {
            var lues = await LectureEcran.LireAsync(baseGreffe, requete.Replace("/*filtre*/", filtre, StringComparison.Ordinal), Ligne, ct,
                                                    [new SqlParameter("@plafond", plafond), .. p]);
            lignes.AddRange(lues.Lignes);
            total += lues.Total;
        }
        if (numero is not null)
        {
            // Le numero de parution : la liste des certificats seule (le seul mode qui l'offre).
            await Ajouter(RequeteCertificats, "AND b.BDCCR_NUM_PARUTION = @numero", new SqlParameter("@numero", numero));
            return new LignesLues(lignes, total);
        }
        if (du is null || (au is { } fin && fin < du)) return new LignesLues([], 0);

        var critere = (recherche.Critere(CritereDate) ?? "").ToLowerInvariant();
        var parution = critere.Contains("parution", StringComparison.Ordinal);
        var envoi = critere.Contains("envoi", StringComparison.Ordinal);
        string Borne(string colonne) => au is null ? $"{colonne} = @du" : $"{colonne} BETWEEN @du AND @au";
        SqlParameter[] Bornes() => au is null ? [new SqlParameter("@du", du.Value.Date)]
                                              : [new SqlParameter("@du", du.Value.Date), new SqlParameter("@au", au.Value.Date)];
        var parEp = $"AND ({Borne("ev.EVNPR_DATE")} OR {Borne("b.BDCCR_DATE_EP_SUPPR")})";
        var filtre = parution ? $"AND {Borne("b.BDCCR_DATE_PUBLICITE")}" : envoi ? $"AND {Borne("b.BDCCR_DATE_ENVOI")}" : parEp;

        await Ajouter(RequeteCertificats, filtre, Bornes());
        if (!parution) await Ajouter(RequeteRejets, filtre, Bornes());
        await Ajouter(RequeteControles, parution || envoi ? filtre : $"AND {Borne("ev.EVNPR_DATE")}", Bornes());
        return new LignesLues(lignes, total);
    }

    internal static LigneLue Ligne(SqlDataReader r)
    {
        var id = FormeLigne.Texte(r, "BDCCR_ID_BDCRC");
        var liste = FormeLigne.Texte(r, "Liste");
        var etat = FormeLigne.Texte(r, "BDCCR_ETAT_BODACC");
        var pc = FormeLigne.Texte(r, "PRCLL_NUM_PC");
        var prv = FormeLigne.Texte(r, "PREVE_NUM_PRV");
        // La prevention l'emporte quand les deux sont la (« le dernier renseigne gagne », CertifBod.cs:61-71).
        var affaire = prv.Length > 0 ? FormeLigne.Texte(r, "PREVE_NOM_AFFAIRE") : FormeLigne.Texte(r, "PRCLL_NOM_AFFAIRE");
        var ep = FormeLigne.Texte(r, "EVNPR_CODE_EVE_PRO");
        var envoi = FormeLigne.DateDe(r, "BDCCR_DATE_ENVOI");
        var parue = FormeLigne.DateDe(r, "BDCCR_DATE_PUBLICITE");
        var (groupe, code, quoi) = liste switch
        {
            "C" => (Certificats, etat == "6" ? "EnCours" : "AFaire", etat == "6" ? "parution vérifiée" : "parution à vérifier"),
            "Q" => (Controles, etat == "6" ? "EnCours" : "AFaire", $"contrôle qualité : {FormeLigne.Texte(r, "BDCCR_CONTROLE_QUALITE")}"),
            _ => (Rejets, etat == "3" ? "EnAttente" : "AFaire", etat == "3" ? "à renvoyer au prochain envoi" : "rejetée, à rectifier"),
        };
        var precisions = FormeLigne.Joindre(
            prv.Length > 0 ? $"prévention {prv}" : pc.Length > 0 ? $"procédure {pc}" : null,
            ep.Length > 0 ? FormeLigne.Joindre($"EP {ep}", FormeLigne.DateDe(r, "EVNPR_DATE") is { } d ? $"du {FormeLigne.Date(d)}" : null) : null,
            envoi is null ? null : $"envoyée le {FormeLigne.Date(envoi)}",
            liste == "R" || parue is null ? null : FormeLigne.Joindre($"parue le {FormeLigne.Date(parue)}",
                FormeLigne.Texte(r, "BDCCR_NUM_PARUTION") is { Length: > 0 } p ? $"parution n° {p}" : null),
            FormeLigne.Texte(r, "BDCCR_NUMERO_ANNONCE") is { Length: > 0 } n ? $"annonce n° {n}" : null,
            liste == "R" && FormeLigne.Texte(r, "BDCCR_MOTIF_REJET_BODACC_1") is { Length: > 0 } m ? $"rejet : {m}" : null,
            quoi);
        var nom = affaire.Length > 0 ? affaire : $"Annonce BODACC n° {id}";
        return new LigneLue(nom, precisions, groupe, code, []) { Id = $"{(liste.Length > 0 ? liste : "R")}-{id}" };
    }
}
