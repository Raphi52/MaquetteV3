using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Préparation d'audience » (PROC_PREAUD) : les audiences et cabinets proches, a preparer (numeros d'appel, plumitif). Mode
/// d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_preaud.md, le Detail OPE fait foi (code Source\RIG\DLL\Processus\PROC_PREAUD\).
///   - LE MODE : « Mode » A (audience, PREAUD), P (procedures collectives, PREAUDPC), C (cabinet, PRECAB) (proc_preaud.md:21-23,
///     :284-294) ; A et P cherchent des AUDIENCES, C des CABINETS (ETP.cs:54-62) ;
///   - LA FENETRE : [maintenant - INTERV_AUD_DATE_MIN_PREAUD ; maintenant + INTERV_AUD_DATE_MAX_PREAUD] jours, INTERV_CAB_*_PREAUD
///     pour un cabinet, 3 par defaut (ETP.cs:54-71, proc_preaud.md:335) : sans recherche, les audiences de cette fenetre ; « Date de
///     l'audience » choisit un jour, « Chambre » une chambre ou une section. ORDRE : date, heure ;
///   - REGLE D'ETAT : numeros d'appel non attribues (AUDNC_NUM_APPEL_ATTRIB = 0 : le recalcul est force, OPE_REC.cs:103-106) =
///     « À préparer » (AFaire) ; attribues = « En préparation » (EnCours). Le plumitif genere ne laisse pas de trace dans les tables
///     V2 de la base V3 (c'est un document numerise, VIDIGED) : l'etat « Plumitif prêt » n'est pas lu.
///
/// LE GESTE : « Valider » attribue les numeros d'appel (EcrivainPreparationAudience.cs). CE QUE LA V3 NE REPREND PAS : la
/// generation du plumitif (un etat Crystal, LancerPlumitif), son impression et son envoi par courriel ou au coffre.
/// </summary>
internal sealed class LecteurPreparationAudience : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_PREAUD";

    /// <summary>La fiche de l'audience ou du cabinet deplie (FichesAudienceV2.cs : ses sections, ses sources V2, ce qu'elle ne lit pas).</summary>
    public Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
        => FichesAudienceV2.PreparationAsync(baseGreffe, Procedure, id, ct);

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Audiences = "Audiences", Cabinets = "Cabinets";

    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre", CritereMode = "Mode";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre, CritereMode];

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) a.AUDNC_ID_ADNC AS Id, a.AUDNC_AUDIENCE AS EstAudience, {{AudiencesV2.Libelle}} AS Audience,
               CONVERT(bit, ISNULL(a.AUDNC_NUM_APPEL_ATTRIB, 0)) AS NumerosAttribues,
               (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE AS aa WHERE aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC) AS Affaires,
               COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET AS a
        {{AudiencesV2.Jointures}}
        WHERE /*filtre*/
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        // Mode C = cabinet ; A et P (« Procédures collectives ») = audience (ETP.cs:54-62).
        var cabinet = AudiencesV2.Mode(recherche.Critere(CritereMode)) == false;
        var (du, au) = cabinet
            ? await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_CAB_DATE_MIN_PREAUD", "INTERV_CAB_DATE_MAX_PREAUD", 3, 3, ct)
            : await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_PREAUD", "INTERV_AUD_DATE_MAX_PREAUD", 3, 3, ct);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, null, !cabinet, du, au, parametres);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += $" AND ({AudiencesV2.Libelle}) LIKE @texte";
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre), Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var affaires = Convert.ToInt32(r["Affaires"], CultureInfo.InvariantCulture);
        var attribues = r["NumerosAttribues"] is true;
        var precisions = FormeLigne.Joindre($"{affaires} affaire{(affaires > 1 ? "s" : "")} au rôle",
                                            attribues ? "numéros d'appel attribués" : "numéros d'appel à attribuer");
        return new LigneLue(FormeLigne.Texte(r, "Audience"), precisions, r["EstAudience"] is true ? Audiences : Cabinets,
                            attribues ? "EnCours" : "AFaire", []) { Id = FormeLigne.Texte(r, "Id") };
    }
}
