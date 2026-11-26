using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// LES AUDIENCES COMME RIG v2 : ce que les ecrans d'audience partagent (plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026) --
/// le libelle d'une audience ou d'un cabinet, la fenetre de dates de la recherche d'audience, une date saisie a l'ecran.
/// Lecteurs : LecteurRetourAudience.cs (PROC_RETAUD), LecteurComposition.cs (PROC_COMPO), LecteurPreparationAudience.cs
/// (PROC_PREAUD), LecteurConvocations.cs (PROC_GCONV), LecteurMiseAuPointChambres.cs (PROC_MPCS).
///
/// SOURCE (ancien RIG) :
///   - une audience est une ligne de AUDIENCE_CABINET ; AUDNC_AUDIENCE = 1 pour une audience, 0 pour un cabinet
///     (proc_retaud.md:223, PROC_RETAUD.cs:80-82) ;
///   - son libelle : AudienceCabinet.GetInfosAudience (AudienceCabinet.cs:1105-1129) -- « Audience du jj/mm/aaaa à hh:mm - » puis la
///     section « Section libelle (code) », sinon la chambre « Chambre libelle (code) » ; « Cabinet du jj/mm/aaaa à hh:mm - » puis le
///     juge du cabinet (JUGE, AUDNC_ID_JUG). Le bureau du juge (PARAMETRAGE PLUM_CAB_BUREAU, :1131-1135) et le greffier du cabinet
///     ne sont pas repris : la V3 n'en a pas l'ecran ;
///   - la fenetre de la recherche d'audience (OPE_INFORMATIONS_AUDIENCE.cs:107-114, proc_retaud.md:450-454) : du jour moins
///     PARAMETRAGE INTERV_AUD_DATE_MIN_RETAUD (14 par defaut) au jour plus INTERV_AUD_DATE_MAX_RETAUD (0 par defaut) ; pour un
///     cabinet, INTERV_CAB_DATE_MIN_RETAUD / INTERV_CAB_DATE_MAX_RETAUD (memes defauts). GetIntParameter (:144-149) prend la valeur
///     du PARAMETRAGE si elle est un entier, sinon le defaut. La table PARAMETRAGE de RIG v2 est reprise dans soc.Reglage
///     (Base\migrations\SOC-003_reglages.sql, RepriseReglages.cs:57) : la base V3 du greffe n'a pas de v2.PARAMETRAGE (mesure le
///     26/11/2026 sur la copie de dev : « Invalid object name », erreur 208).
/// </summary>
internal static class AudiencesV2
{
    /// <summary>Le libelle d'une audience (GetInfosAudience, AudienceCabinet.cs:1105-1129), en SQL : les alias a (AUDIENCE_CABINET),
    /// ch (CHAMBRE), se (SECTION), jc (JUGE du cabinet) doivent etre joints par <see cref="Jointures"/>.</summary>
    internal const string Libelle = """
        CASE WHEN a.AUDNC_AUDIENCE = 0
             THEN 'Cabinet du ' + ISNULL(CONVERT(varchar(10), a.AUDNC_DATE, 103), '') + ' à ' + ISNULL(a.AUDNC_HEURE, '') + ' - '
                  + ISNULL(LTRIM(RTRIM(jc.JUG_NOM + ISNULL(' ' + jc.JUG_PRENOM, ''))), '')
             ELSE 'Audience du ' + ISNULL(CONVERT(varchar(10), a.AUDNC_DATE, 103), '') + ' à ' + ISNULL(a.AUDNC_HEURE, '') + ' - '
                  + CASE WHEN se.R_SECTION_VALEUR IS NOT NULL THEN 'Section ' + se.R_SECTION_LIBELLE_ECRAN + ' (' + a.AUDNC_SECTION + ')'
                         WHEN ch.R_CHAMBRE_VALEUR IS NOT NULL THEN 'Chambre ' + ch.R_CHAMBRE_LIBELLE_ECRAN + ' (' + a.AUDNC_CHAMBRE + ')'
                         ELSE '' END
        END
        """;

    /// <summary>Les jointures du libelle (alias a = AUDIENCE_CABINET).</summary>
    internal const string Jointures = """
        LEFT JOIN v2.CHAMBRE AS ch ON ch.R_CHAMBRE_VALEUR = a.AUDNC_CHAMBRE
        LEFT JOIN v2.SECTION AS se ON se.R_SECTION_VALEUR = a.AUDNC_SECTION
        LEFT JOIN v2.JUGE AS jc ON jc.JUG_ID_JUG = a.AUDNC_ID_JUG
        """;

    /// <summary>Le mode « Audience » (AUDNC_AUDIENCE = 1) ou « Cabinet » (0) d'un critere saisi ; null s'il n'est ni l'un ni l'autre.</summary>
    internal static bool? Mode(string? saisi) => saisi?.Trim().ToLowerInvariant() switch
    {
        "audience" or "audiences" => true,
        "cabinet" or "cabinets" => false,
        _ => null,
    };

    /// <summary>Une date saisie a l'ecran (« 2026-02-20 » d'un champ date, ou « 20/02/2026 ») ; null sinon.</summary>
    internal static DateTime? Date(string? saisie)
    {
        var s = saisie?.Trim() ?? "";
        return DateTime.TryParseExact(s, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Date : null;
    }

    /// <summary>
    /// La fenetre de la recherche d'audience de RIG v2 (OPE_INFORMATIONS_AUDIENCE.cs:107-114) : [jour - min ; jour + max], min et
    /// max lus dans les reglages du greffe (soc.Reglage, reprise de PARAMETRAGE : <paramref name="cleMin"/>, <paramref name="cleMax"/>),
    /// 14 et 0 par defaut (GetIntParameter, :144-149).
    /// </summary>
    internal static async Task<(DateTime Du, DateTime Au)> FenetreAsync(SqlConnection cn, DateTime aujourdhui, string cleMin, string cleMax,
                                                                       int defautMin, int defautMax, CancellationToken ct)
    {
        var min = defautMin;
        var max = defautMax;
        await using (var r = await LectureV2.LireAsync(cn, "SELECT Code, Valeur FROM soc.Reglage WHERE Code IN (@min, @max)", ct,
                         new SqlParameter("@min", cleMin), new SqlParameter("@max", cleMax)))
            while (await r.ReadAsync(ct))
                if (int.TryParse(FormeLigne.Texte(r, "Valeur"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    if (string.Equals(FormeLigne.Texte(r, "Code"), cleMin, StringComparison.Ordinal)) min = n;
                    else max = n;
                }
        return (aujourdhui.Date.AddDays(-min), aujourdhui.Date.AddDays(max));
    }

    /// <summary>
    /// Le filtre SQL d'une recherche d'audience : la date choisie (le jour entier), la chambre ou la section (code ou libelle), le
    /// mode. Sans date, <paramref name="du"/> et <paramref name="au"/> bornent. Ajoute ses parametres a <paramref name="parametres"/>.
    /// </summary>
    internal static string Filtre(RechercheEcran? recherche, string critereDate, string? critereChambre, string? critereMode, bool modeParDefaut,
                                  DateTime du, DateTime au, List<SqlParameter> parametres)
    {
        var filtres = new List<string>();
        var jour = Date(recherche?.Critere(critereDate));
        parametres.Add(new SqlParameter("@du", jour ?? du));
        parametres.Add(new SqlParameter("@au", (jour ?? au).AddDays(1)));
        filtres.Add("a.AUDNC_DATE >= @du AND a.AUDNC_DATE < @au");
        var mode = critereMode is null ? null : Mode(recherche?.Critere(critereMode));
        parametres.Add(new SqlParameter("@mode", mode ?? modeParDefaut));
        filtres.Add("a.AUDNC_AUDIENCE = @mode");
        if (critereChambre is not null && recherche?.Critere(critereChambre) is { } chambre)
        {
            filtres.Add("(a.AUDNC_CHAMBRE = @chambre OR a.AUDNC_SECTION = @chambre OR ch.R_CHAMBRE_LIBELLE_ECRAN LIKE @chambreLike OR se.R_SECTION_LIBELLE_ECRAN LIKE @chambreLike)");
            parametres.Add(new SqlParameter("@chambre", chambre));
            parametres.Add(new SqlParameter("@chambreLike", FormeLigne.Contient(chambre)));
        }
        return string.Join(" AND ", filtres);
    }
}
