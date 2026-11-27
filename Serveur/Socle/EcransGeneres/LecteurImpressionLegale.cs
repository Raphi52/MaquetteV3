using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Impression légale » (PROC_IMPLEG) : les decisions d'une audience dont la minute est editee, a imprimer en masse. Mode d'emploi :
/// LecteurEcran.cs ; le geste : EcrivainImpressionLegale.cs. Plan « RIG v3 100 % fonctionnel », lot 10, 27/11/2026.
///
/// FORME « Audience » (et non « File ») : le perimetre de RIG v2 est toujours UNE audience, recue en parametre (« IdAudience », depuis
/// le retour d'audience, OPE_INFORMATIONS_AUDIENCE.ult_BtnImpLeg_ButtonClick) ou choisie dans la recherche d'audience
/// (OPE_IMPLEG.OpenOcxRecherche, :127-144) -- proc_impleg.md, Detail OPE, qui fait foi.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_IMPLEG\PROC_IMPLEG\Etapes - Operations\OPE_IMPLEG.cs -- RequeteSQL_DataDJ (:52-82) :
/// DECISION_JUDICIAIRE JOIN APPEL_AFFAIRE (l'appel du prononce, DCJDC_ID_APPAF_PRONONCER) JOIN INSTANCE JOIN TYPE_DECISION, « WHERE
/// APPAF_ID_ADNC = &lt;audience&gt; AND (DCJDC_ETAT_REDACTION = 3 OR DCJDC_ETAT_REDACTION = 4) », triees « ISNULL(DCJDC_DATE,
/// DCJDC_DATE_PREVUE) DESC » ; le libelle de l'etat (« Minute éditée », « Impression légale effectuée ») suivi de « - ATTENTION - NON
/// DIFFUSABLE » si DCJDC_ETAT_DIFFUSION vaut 0, 1, 2 ou 4. L'audience : sans parametre, la recherche s'ouvre sur J-3 a J+3 (:132-133)
/// -- la fenetre de la liste par defaut ; la date et la chambre choisies la remplacent (AudiencesV2.Filtre). RIG v2 n'en traite qu'UNE :
/// la V3 montre les decisions de toutes les audiences du jour choisi, l'audience dans les precisions.
///
/// REGLE D'ETAT (la selection par defaut « Impleg_attente » coche les decisions a l'etat « Révisé à rendre », OPE_PREFERENCES.cs:49-62) :
///   - AFaire « Minute éditée » : DCJDC_ETAT_REDACTION = 3 ;
///   - EnCours « Impression légale effectuée » : DCJDC_ETAT_REDACTION = 4 (RIG v2 la propose encore : elle se reimprime).
/// Id = DCJDC_ID_DCJDC.
///
/// NON REPRIS (nomme) : le nombre d'editions par decision et le bouton « Paramétrage IMPLEG » qui ouvre les destinataires (l'ecran
/// « Destinataires », PROC_DEST_REDA) ; l'alerte rouge « décision non numérisée » (REDA_NUMERISATION, :198-213).
/// </summary>
internal sealed class LecteurImpressionLegale : IRechercheEcran
{
    public string Procedure => "PROC_IMPLEG";

    /// <summary>Le groupe du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string Decisions = "Décisions de l'audience";

    /// <summary>Les parametres de l'audience (le descripteur), que la recherche lit.</summary>
    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre];

    internal const string Requete = $$"""
        SELECT TOP (@plafond) d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, d.DCJDC_ETAT_REDACTION, d.DCJDC_ETAT_DIFFUSION, d.DCJDC_NUM_REPERTOIRE, d.DCJDC_DATE,
               d.DCJDC_DATE_PREVUE, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, td.R_TYPEDECISI_LIBELLE_ECRAN, td.R_TYPEDECISI_TYPE_MESURE_LIE,
               {{AudiencesV2.Libelle}} AS Audience, COUNT(*) OVER () AS Total
        FROM v2.DECISION_JUDICIAIRE AS d
        JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_APPAF = d.DCJDC_ID_APPAF_PRONONCER
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
        JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
        JOIN v2.AUDIENCE_CABINET AS a ON a.AUDNC_ID_ADNC = ap.APPAF_ID_ADNC
        {{AudiencesV2.Jointures}}
        WHERE (d.DCJDC_ETAT_REDACTION = '3' OR d.DCJDC_ETAT_REDACTION = '4') AND /*filtre*/
        ORDER BY a.AUDNC_DATE, a.AUDNC_ID_ADNC, ISNULL(d.DCJDC_DATE, d.DCJDC_DATE_PREVUE) DESC, d.DCJDC_ID_DCJDC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        // La recherche d'audience de RIG v2 : J-3 a J+3, audiences et cabinets confondus (FORM_RECHERCHE_AUDCAB, ParamImpleg) -- le mode
        // n'est pas un critere : AUDNC_AUDIENCE est lu tel quel (« modeParDefaut » n'est pas applique, ci-dessous).
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, null, true, aujourdhui.Date.AddDays(-3), aujourdhui.Date.AddDays(3), parametres)
                                .Replace(" AND a.AUDNC_AUDIENCE = @mode", "", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += " AND (i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)";
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre, StringComparison.Ordinal), Ligne, ct, [.. parametres]);
    }

    /// <summary>Le libelle de l'etat de redaction de RIG v2 (RequeteSQL_DataDJ, :56-66).</summary>
    internal static string LibelleEtat(string etat, string diffusion)
        => (etat == "3" ? "Minute éditée" : "Impression légale effectuée") + (diffusion is "0" or "1" or "2" or "4" ? " - ATTENTION - NON DIFFUSABLE" : "");

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etat = FormeLigne.Texte(r, "DCJDC_ETAT_REDACTION");
        var minute = FormeLigne.Texte(r, "DCJDC_NUM_REPERTOIRE");
        var precisions = FormeLigne.Joindre($"Rôle {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN"),
            minute.Length > 0 ? $"minute {minute}" : "sans numéro de minute", LibelleEtat(etat, FormeLigne.Texte(r, "DCJDC_ETAT_DIFFUSION")),
            FormeLigne.Texte(r, "Audience").TrimEnd(' ', '-'));
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        return new LigneLue(nom.Length > 0 ? nom : FormeLigne.Texte(r, "INSTN_NUM_ROLE"), precisions, Decisions, etat == "3" ? "AFaire" : "EnCours", [])
        {
            Id = FormeLigne.Texte(r, "DCJDC_ID_DCJDC"),
        };
    }
}
