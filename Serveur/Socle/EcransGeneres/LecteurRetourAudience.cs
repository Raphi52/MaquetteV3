using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Retour d'audience » (PROC_RETAUD) : les affaires au role d'une audience (les appels d'affaire), pour en saisir le resultat.
/// Mode d'emploi : LecteurEcran.cs ; les gestes : EcrivainRetourAudience.cs. Plan « RIG v3 100 % fonctionnel », lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_RETAUD (proc_retaud.md, le Detail OPE fait foi ; code Source\RIG\DLL\Processus\PROC_RETAUD\).
///   - LE ROLE : la requete de la grille, OPE_LIST_APPEL_AFFAIRE.cs:561-592 -- AUDIENCE_CABINET JOIN APPEL_AFFAIRE (APPAF_ID_ADNC)
///     JOIN INSTANCE (APPAF_ID_INSTN) JOIN TYPE_CLASSEMENT (APPAF_TYPE_CLASSEMENT) ; colonnes reprises : n° de role, n° d'appel,
///     resultat (APPAF_CODE_PLUMITIF), prochain appel (le premier appel de l'instance a une audience posterieure), composition (la
///     colonne « Compo. » : le numero d'ordre de la composition de l'appel, CMPST_NUM_ORDRE de APPAF_ID_CMAUD, montree pour une
///     audience seulement : OPE_LIST_APPEL_AFFAIRE.cs:343-346, :543, :583, :787-791). Les parties
///     (demandeur, defendeur) ne sont pas reprises : la ligne porte le nom courant de l'instance, qui les nomme ;
///   - L'AUDIENCE : RIG v2 ouvre la recherche d'audience bornee a [jour - 14 ; jour] (PARAMETRAGE INTERV_AUD_DATE_MIN_RETAUD /
///     _MAX_RETAUD ; INTERV_CAB_* pour un cabinet ; OPE_INFORMATIONS_AUDIENCE.cs:107-114, AudiencesV2.FenetreAsync) : sans
///     recherche, les affaires des audiences de cette fenetre ; « Date de l'audience » choisit un jour (la recherche de RIG v2 ou
///     l'on change les dates), « Chambre » une chambre ou une section, « Mode » audience ou cabinet (PROC_RETAUD.cs:85-87, « C » =
///     cabinet). ORDRE : audience (date, heure), puis numero d'appel (NumAppelRowComparer), puis role ;
///   - GROUPES, les filtres de la grille (FiltreAffichage, OPE_LIST_APPEL_AFFAIRE.cs:553-598) : « Publicités en attente » (un
///     evenement de procedure en attente porte l'appel : EVNPA_ID_APPAF_DELIBERER ou _PRONONCER, :598) ; sinon, non traite
///     (APPAF_CODE_PLUMITIF NULL, IsTraite, AppelAffaire.cs:736-739) et prononce (R_TYPECLASSE_PRONONCE = 1, :596) : « Prononcés à
///     traiter » ; non traite : « Affaires à traiter » (:594) ; traite : « Affaires traitées » (le filtre « tous »).
///   - REGLE D'ETAT : non traite = « À saisir » (AFaire) ; traite = « Résultat saisi » (Validee) ; publicite en attente =
///     « Publicité en attente » (EnAttente).
/// </summary>
internal sealed class LecteurRetourAudience : IRechercheEcran
{
    public string Procedure => "PROC_RETAUD";

    /// <summary>Les groupes du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string ATraiter = "Affaires à traiter", PrononcesATraiter = "Prononcés à traiter", Traitees = "Affaires traitées",
                          Publicites = "Publicités en attente";

    /// <summary>Les parametres de l'audience (descripteur), que l'ecran envoie comme criteres.</summary>
    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre", CritereMode = "Mode";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre, CritereMode];

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) aa.APPAF_ID_APPAF AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant,
               aa.APPAF_NUM_APPEL AS NumAppel, aa.APPAF_TYPE_CLASSEMENT AS TypeClassement, aa.APPAF_CODE_PLUMITIF AS Resultat,
               CONVERT(bit, ISNULL(tc.R_TYPECLASSE_PRONONCE, 0)) AS Prononce, {{AudiencesV2.Libelle}} AS Audience,
               (SELECT TOP (1) a2.AUDNC_DATE FROM v2.APPEL_AFFAIRE AS aa2
                JOIN v2.AUDIENCE_CABINET AS a2 ON a2.AUDNC_ID_ADNC = aa2.APPAF_ID_ADNC AND a2.AUDNC_DATE > a.AUDNC_DATE
                WHERE aa2.APPAF_ID_INSTN = i.INSTN_ID_INSTN ORDER BY a2.AUDNC_DATE) AS ProchainAppel,
               {{Composition}} AS Compo,
               CASE WHEN EXISTS (SELECT 1 FROM v2.EVENEMENT_PROCEDURE_EN_ATTENTE AS e
                                 WHERE e.EVNPA_ID_APPAF_DELIBERER = aa.APPAF_ID_APPAF OR e.EVNPA_ID_APPAF_PRONONCER = aa.APPAF_ID_APPAF)
                    THEN 1 ELSE 0 END AS Publicite,
               COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET AS a
        JOIN v2.APPEL_AFFAIRE AS aa ON aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
        JOIN v2.TYPE_CLASSEMENT AS tc ON tc.R_TYPECLASSE_VALEUR = aa.APPAF_TYPE_CLASSEMENT
        {{AudiencesV2.Jointures}}
        WHERE /*filtre*/
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC, aa.APPAF_NUM_APPEL, i.INSTN_NUM_ROLE, aa.APPAF_ID_APPAF
        """;

    // fix-ok: mesure du 27/11/2026 (copie de dev, serveur 5221) -- apres « Valider la décision » avec « Composition » = 3 sur
    // 2000F00104, aucune ligne relue (PROC_MPCS, PROC_RETAUD) ne montrait la composition posee : la grille de RIG v2 a une colonne
    // « Compo. » (OPE_LIST_APPEL_AFFAIRE.cs:343-346, :583, :787-791) que ce lecteur n'avait pas reprise. Une seule cause, plusieurs
    // remplacements (en-tete, requete, ligne).
    /// <summary>La colonne « Compo. » de la grille de RIG v2 (OPE_LIST_APPEL_AFFAIRE.cs:583) : le numero d'ordre de la composition
    /// de l'appel, pour une audience seulement (la colonne est masquee pour un cabinet, :543). Lue aussi par la mise au point des
    /// chambres, dont les lignes sont celles de la meme grille.</summary>
    internal const string Composition =
        "CASE WHEN a.AUDNC_AUDIENCE = 1 THEN (SELECT TOP (1) cmp.CMPST_NUM_ORDRE FROM v2.COMPOSITION AS cmp WHERE cmp.CMPST_ID_CMAUD = aa.APPAF_ID_CMAUD) END";

    /// <summary>« composition n° 2 » quand l'appel en a une (la colonne <see cref="Composition"/>, lue sous le nom Compo).</summary>
    internal static string? TexteComposition(SqlDataReader r) => r["Compo"] is DBNull ? null : $"composition n° {r["Compo"]}";

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var audience = AudiencesV2.Mode(recherche.Critere(CritereMode)) ?? true;
        var (du, au) = audience
            ? await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_RETAUD", "INTERV_AUD_DATE_MAX_RETAUD", 14, 0, ct)
            : await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_CAB_DATE_MIN_RETAUD", "INTERV_CAB_DATE_MAX_RETAUD", 14, 0, ct);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, CritereMode, true, du, au, parametres);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += " AND (REPLACE(i.INSTN_NUM_ROLE, ' ', '') LIKE @role OR i.INSTN_NOM_COURANT LIKE @texte)";
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre), Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "Role");
        var resultat = FormeLigne.Texte(r, "Resultat");
        var traite = resultat.Length > 0;
        var publicite = Convert.ToInt32(r["Publicite"], System.Globalization.CultureInfo.InvariantCulture) == 1;
        var prochain = FormeLigne.DateDe(r, "ProchainAppel");
        var precisions = FormeLigne.Joindre(
            role.Length > 0 ? $"Rôle {role}" : null,
            NumeroAppel(FormeLigne.Texte(r, "NumAppel"), FormeLigne.Texte(r, "TypeClassement")) is { Length: > 0 } n ? $"appel n° {n}" : null,
            FormeLigne.Texte(r, "Audience"),
            traite ? $"résultat {resultat}" : "résultat à saisir",
            prochain is not null ? $"prochain appel le {FormeLigne.Date(prochain)}" : null,
            TexteComposition(r));
        var groupe = publicite ? Publicites : traite ? Traitees : r["Prononce"] is true ? PrononcesATraiter : ATraiter;
        var etat = publicite ? "EnAttente" : traite ? "Validee" : "AFaire";
        var nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } nc ? nc : $"Instance n° {role}";
        return new LigneLue(nom, precisions, groupe, etat, []) { Id = FormeLigne.Texte(r, "Id") };
    }

    /// <summary>Le numero d'appel comme RIG v2 l'affiche (AppelAffaire.NumAppelFormate, AppelAffaire.cs:741-749) : « J_12 » devient
    /// « <type de classement> 12 ».</summary>
    internal static string NumeroAppel(string numAppel, string typeClassement)
    {
        var morceaux = numAppel.Split('_');
        return morceaux.Length == 2 ? $"{typeClassement} {morceaux[1]}" : numAppel;
    }
}
