using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Convocations » (PROC_GCONV) : les instances d'un CABINET convoquees par lettre recommandee (LRAR), et ou en est leur citation
/// par huissier. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_GCONV\PROC_GCONV\ETP_CITATIONS\OPE_CITATIONS.cs (proc_gconv.md, le Detail
/// OPE fait foi).
///   - LE CABINET : la recherche s'ouvre en type CABINET, un seul a la fois (OPE_CITATIONS.cs:157-162), fenetre [maintenant -
///     INTERV_AUD_DATE_MIN_PREAUD ; maintenant + INTERV_AUD_DATE_MAX_PREAUD], 3 et 3 par defaut (:164-167 ; PARAMETRAGE repris dans
///     soc.Reglage, AudiencesV2.cs) : sans recherche, les cabinets de cette fenetre ; « Date de l'audience » choisit un jour ;
///   - LES INSTANCES (la requete de ChangementMetierBase, :46-67, GROUP BY de tous ses champs) : les appels d'affaire du cabinet,
///     leur instance, un evenement de procedure de l'instance et SA demande d'edition LRAR (la premiere dont la source est
///     l'evenement, DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC'), la LRAR (LRAR_INFOS), la relance RCS de l'instance (RELANCE_RCS,
///     RLCRC_ID_INSTN) et sa diligence (DILIGENCE_RCS) ; l'exercice de la diligence et le dossier de son action en plus (LEFT JOIN).
///     ACTION_RCS et DOSSIER_RCS sont repris en fiches (rec.ActionRegistre, rec.DossierRcs : Serveur/Recopie/FichesV3.cs) ;
///   - LA CITATION (:83-89) : le premier evenement de l'instance au code SURV_CEP_CITATION_HUISS (« CITATION » par defaut,
///     Surveillances.cs:423-425), la signification au code SURV_CEP_SIGNIFICATION (« SSIGNI », :441-443), son texte ;
///   - REGLE D'ETAT (les noeuds de l'arbre, :91-95) : LRAR « RECEPTION » ou « NPAI » = grisee, non cochee -> « Reçue ou NPAI »
///     (Rejetee) ; sinon une citation existe -> « Citée » (Validee) ; sinon la LRAR porte une solution de citation
///     (LRARI_SLN_CITATION) -> cochee d'office, « À citer » (AFaire) ; sinon -> « Sans retour » (EnCours : pas de retour de LRAR).
///   ORDRE : cabinet (date, heure), numero de role, date de la LRAR.
///
/// LE GESTE : « Citer par huissier » (l'evenement CITATION), EcrivainConvocations.cs ; ses editions et sa facture n'y sont pas.
/// </summary>
internal sealed class LecteurConvocations : IRechercheEcran
{
    public string Procedure => "PROC_GCONV";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string ACiter = "Citations à envoyer", Citees = "Citées", SansRetour = "LRAR sans retour", RecuesNpai = "Reçues ou NPAI";

    internal const string CritereDate = "Date de l'audience";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate];

    // Une sous-requete dans le FROM (la garde de LectureEcran n'admet qu'une requete qui commence par SELECT : pas de WITH) :
    // les lignes distinctes de RIG v2 (son GROUP BY de tous les champs), puis la LRAR, la citation et la signification.
    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) v.INSTN_ID_INSTN, v.DMNED_ID_DMNED, v.RLCRC_ID_RLCRC, v.EXRCC_ID_EXRCC, v.INSTN_NUM_ROLE, v.INSTN_NOM_COURANT,
               v.DSSRC_NUM_GESTION, v.LRARI_NUM_LRAR, v.DMNED_DATE_CREATION, l.LRARI_SLN_CITATION, l.LRARI_TYPE_DATE, pl.PRMLI_LIBELLE,
               cit.EVNPR_DATE AS DateCitation, sig.EVNPR_TEXTE AS Signification, {{AudiencesV2.Libelle}} AS Audience, COUNT(*) OVER () AS Total
        FROM (
            SELECT DISTINCT a.AUDNC_ID_ADNC, i.INSTN_ID_INSTN, s.Id AS DSSRC_ID_DSRCS, ar.Id AS ACTRC_ID_ACTRC,
                   g.DLGRC_ID_DLGRC, r.RLCRC_ID_RLCRC, e.EVNPR_ID_EVNPR, de.DMNED_ID_DMNED, ex.EXRCC_ID_EXRCC, ex.EXRCC_DATE_CLOTURE,
                   g.DLGRC_NATURE_DILIGENCE, i.INSTN_NUM_ROLE, s.NumeroGestion AS DSSRC_NUM_GESTION, s.Siren AS DSSRC_SIREN,
                   l.LRARI_NUM_LRAR, de.DMNED_DATE_CREATION, i.INSTN_NOM_COURANT
            FROM v2.AUDIENCE_CABINET AS a
            JOIN v2.APPEL_AFFAIRE AS aa ON aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
            JOIN v2.EVENEMENT_PROCEDURE AS e ON e.EVNPR_ID_INSTN = i.INSTN_ID_INSTN
            JOIN v2.DEMANDE_EDITION AS de ON de.DMNED_ID_DMNED =
                (SELECT TOP (1) dd.DMNED_ID_DMNED FROM v2.DEMANDE_EDITION AS dd JOIN v2.LRAR_INFOS AS ll ON dd.DMNED_NUM_LRAR = ll.LRARI_NUM_LRAR
                 WHERE dd.DMNED_VALEUR_ID_SOURCE = e.EVNPR_NUMERO_SOURCE AND dd.DMNED_CODE_ENTITE_SOURCE = 'EVT_PROC')
            JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = de.DMNED_NUM_LRAR
            JOIN v2.RELANCE_RCS AS r ON r.RLCRC_ID_INSTN = i.INSTN_ID_INSTN
            JOIN v2.DILIGENCE_RCS AS g ON g.DLGRC_ID_DLGRC = r.RLCRC_ID_DLGRC
            LEFT JOIN v2.EXERCICE AS ex ON ex.EXRCC_ID_DLGRC = g.DLGRC_ID_DLGRC
            LEFT JOIN rec.ActionRegistre AS ar ON ar.Id = g.DLGRC_ID_ACTRC
            LEFT JOIN rec.DossierRcs AS s ON s.Id = ar.IdDossier
            {{AudiencesV2.Jointures}}
            WHERE /*filtre*/) AS v
        JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = v.AUDNC_ID_ADNC
        JOIN v2.LRAR_INFOS AS l ON l.LRARI_NUM_LRAR = v.LRARI_NUM_LRAR
        LEFT JOIN v2.PARAM_LRAR_INFOS AS pl ON pl.PRMLI_CODE = l.LRARI_TYPE_DATE
        OUTER APPLY (SELECT TOP (1) c.EVNPR_DATE FROM v2.EVENEMENT_PROCEDURE AS c WHERE c.EVNPR_ID_INSTN = v.INSTN_ID_INSTN AND c.EVNPR_CODE_EVE_PRO = @citation
                     ORDER BY c.EVNPR_NUM_ORDRE, c.EVNPR_ID_EVNPR) AS cit
        OUTER APPLY (SELECT TOP (1) c.EVNPR_TEXTE FROM v2.EVENEMENT_PROCEDURE AS c WHERE c.EVNPR_ID_INSTN = v.INSTN_ID_INSTN AND c.EVNPR_CODE_EVE_PRO = @signification
                     ORDER BY c.EVNPR_NUM_ORDRE, c.EVNPR_ID_EVNPR) AS sig
        {{AudiencesV2.Jointures}}
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC, v.INSTN_NUM_ROLE, v.DMNED_DATE_CREATION, v.DMNED_ID_DMNED, v.RLCRC_ID_RLCRC, v.EXRCC_ID_EXRCC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // La fenetre de l'OCX de recherche (OPE_CITATIONS.cs:164-167) ; cabinets seulement (:159).
        var (du, au) = await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_PREAUD", "INTERV_AUD_DATE_MAX_PREAUD", 3, 3, ct);
        var codes = await CodesAsync(baseGreffe, ct);
        var parametres = new List<SqlParameter>
        {
            new("@plafond", plafond), new("@citation", codes.Citation), new("@signification", codes.Signification),
        };
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, null, null, false, du, au, parametres);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += " AND (REPLACE(i.INSTN_NUM_ROLE, ' ', '') LIKE @role OR i.INSTN_NOM_COURANT LIKE @texte OR s.NumeroGestion LIKE @texte OR l.LRARI_NUM_LRAR LIKE @texte)";
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre), Ligne, ct, [.. parametres]);
    }

    /// <summary>Les codes d'evenement de la citation et de la signification par huissier (Surveillances.cs:423-425, :441-443).</summary>
    private static async Task<(string Citation, string Signification)> CodesAsync(SqlConnection cn, CancellationToken ct)
    {
        var citation = "CITATION";
        var signification = "SSIGNI";
        await using var r = await LectureV2.LireAsync(cn,
            "SELECT Code, Valeur FROM soc.Reglage WHERE Code IN ('SURV_CEP_CITATION_HUISS', 'SURV_CEP_SIGNIFICATION')", ct);
        while (await r.ReadAsync(ct))
            if (FormeLigne.Texte(r, "Valeur") is { Length: > 0 } v)
            {
                if (FormeLigne.Texte(r, "Code") == "SURV_CEP_CITATION_HUISS") citation = v;
                else signification = v;
            }
        return (citation, signification);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var typeDate = FormeLigne.Texte(r, "LRARI_TYPE_DATE");
        var citee = FormeLigne.DateDe(r, "DateCitation");
        var (groupe, etat) = typeDate is "RECEPTION" or "NPAI" ? (RecuesNpai, "Rejetee")
            : citee is not null ? (Citees, "Validee")
            : r["LRARI_SLN_CITATION"] is true ? (ACiter, "AFaire")
            : (SansRetour, "EnCours");
        var role = FormeLigne.Texte(r, "INSTN_NUM_ROLE");
        var retour = FormeLigne.Texte(r, "PRMLI_LIBELLE") is { Length: > 0 } lib ? lib : typeDate;
        var precisions = FormeLigne.Joindre(
            $"Rôle {role}",
            FormeLigne.Texte(r, "DSSRC_NUM_GESTION") is { Length: > 0 } g ? $"dossier {g}" : null,
            $"LRAR {FormeLigne.Texte(r, "LRARI_NUM_LRAR")}" + (FormeLigne.DateDe(r, "DMNED_DATE_CREATION") is { } envoi ? $" du {FormeLigne.Date(envoi)}" : ""),
            retour is { Length: > 0 } ? $"retour : {retour}" : null,
            citee is not null ? $"citée le {FormeLigne.Date(citee)}" : null,
            FormeLigne.Texte(r, "Signification") is { Length: > 0 } sig ? $"signification : {sig}" : null,
            FormeLigne.Texte(r, "Audience"));
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT") is { Length: > 0 } nc ? nc : $"Instance n° {role}";
        var id = string.Join("-", FormeLigne.Texte(r, "INSTN_ID_INSTN"), FormeLigne.Texte(r, "DMNED_ID_DMNED"), FormeLigne.Texte(r, "RLCRC_ID_RLCRC"),
                                  FormeLigne.Texte(r, "EXRCC_ID_EXRCC"));
        return new LigneLue(nom, precisions, groupe, etat, []) { Id = id };
    }
}
