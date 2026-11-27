using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Composition » (PROC_COMPO) : les audiences a venir et leur composition (juges, greffier, parquetier). Mode d'emploi :
/// LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : proc_compo.md, le Detail OPE fait foi (code Source\RIG\DLL\Processus\PROC_COMPO\PROC_COMPO\).
///   - L'AUDIENCE : la recherche s'ouvre en type AUDIENCE (PROC.cs:134 ; un cabinet n'a pas de composition, BtnCompoAudience.Visible
///     = AUDNC_AUDIENCE, proc_retaud.md:406), fenetre [J+1 ; J+15] en dur (PROC.cs:139-140) : sans recherche, ces audiences ;
///     « Date de l'audience » choisit un jour, « Chambre » une chambre ou une section. ORDRE : date, heure ;
///   - LA COMPOSITION : COMPOSITION (CMPST_ID_ADNC) et ses siegeants SIEGER (SGR_ID_CMAUD, les juges) ; une composition = au moins
///     un juge, un greffier, zero ou un parquetier (ETP_COMPO.cs:240-257) ;
///   - REGLE D'ETAT : aucune composition = « À composer » (AFaire ; RIG v2 en propose une vide, PROC.cs:216-217) ; une composition
///     sans juge = « En cours » (EnCours : la regle « un juge au minimum », ETP_COMPO.cs:121, n'est pas tenue) ; sinon « Composée »
///     (Validee).
///
/// LE GESTE : « Valider » enregistre la composition de l'audience cochee (juges, greffier, parquetier) : EcrivainComposition.cs,
/// qui nomme ce que la V3 ne reprend pas (plusieurs compositions, invitations calendrier envoyees par courriel).
/// </summary>
internal sealed class LecteurComposition : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_COMPO";

    /// <summary>La fiche de l'audience depliee (FichesAudienceV2.cs : ses sections, ses sources V2, ce qu'elle ne lit pas).</summary>
    public Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
        => FichesAudienceV2.CompositionAsync(baseGreffe, Procedure, id, ct);

    /// <summary>Le groupe du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Audiences = "Audiences";

    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre];

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) a.AUDNC_ID_ADNC AS Id, {{AudiencesV2.Libelle}} AS Audience,
               (SELECT COUNT(*) FROM v2.APPEL_AFFAIRE AS aa WHERE aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC) AS Affaires,
               (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Compositions,
               (SELECT COUNT(*) FROM v2.COMPOSITION AS c WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC
                  AND NOT EXISTS (SELECT 1 FROM v2.SIEGER AS s WHERE s.SGR_ID_CMAUD = c.CMPST_ID_CMAUD)) AS SansJuge,
               (SELECT COUNT(*) FROM v2.COMPOSITION AS c JOIN v2.SIEGER AS s ON s.SGR_ID_CMAUD = c.CMPST_ID_CMAUD
                WHERE c.CMPST_ID_ADNC = a.AUDNC_ID_ADNC) AS Juges,
               COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET AS a
        {{AudiencesV2.Jointures}}
        WHERE /*filtre*/
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        // [J+1 ; J+15] en dur (PROC_COMPO.cs:139-140) ; audiences seulement (PROC_COMPO.cs:134).
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, null, true, aujourdhui.Date.AddDays(1), aujourdhui.Date.AddDays(15), parametres);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += $" AND ({AudiencesV2.Libelle}) LIKE @texte";
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre), Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        int N(string c) => Convert.ToInt32(r[c], CultureInfo.InvariantCulture);
        var compositions = N("Compositions");
        var affaires = N("Affaires");
        var juges = N("Juges");
        var etat = compositions == 0 ? "AFaire" : N("SansJuge") > 0 ? "EnCours" : "Validee";
        var precisions = FormeLigne.Joindre(
            $"{affaires} affaire{(affaires > 1 ? "s" : "")} au rôle",
            compositions == 0 ? "aucune composition" : $"{compositions} composition{(compositions > 1 ? "s" : "")} · {juges} juge{(juges > 1 ? "s" : "")}");
        return new LigneLue(FormeLigne.Texte(r, "Audience"), precisions, Audiences, etat, []) { Id = FormeLigne.Texte(r, "Id") };
    }
}
