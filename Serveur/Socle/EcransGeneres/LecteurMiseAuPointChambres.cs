using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Mise au point chambres » (PROC_MPCS) : les affaires au role qui portent une procedure collective et ne sont pas encore
/// traitees -- celles sur lesquelles RIG v2 ouvre PROC_MPCS. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel »,
/// lot 7, 26/11/2026.
///
/// SOURCE (ancien RIG) : PROC_MPCS ne s'ouvre pas du menu sur une liste a lui : le retour d'audience le lance sur les appels
/// d'affaire coches (parametre IdAppelsAffaires, proc_mpcs.md:22) quand TOUTES leurs instances ont une procedure collective
/// (INSTN_ID_PRCLL) et qu'aucune n'est traitee (OPE_LIST_APPEL_AFFAIRE.cs:1409-1412, DetermineProcessusSelonTypeInstance ; une PC
/// seule : PROC_MPC, :1420-1425). Le role est donc celui du retour d'audience (LecteurRetourAudience.cs : la fenetre
/// INTERV_AUD_DATE_*_RETAUD, « Date de l'audience », « Chambre »), restreint a ces affaires, avec la colonne « Compo. » de cette
/// grille (LecteurRetourAudience.Composition) : la composition que « Valider la décision » pose s'y relit. ORDRE : audience, numero
/// d'appel, role.
/// Une ligne, un etat « À juger » (AFaire).
///
/// LE GESTE : « Valider la décision » en lot (résultat et décision judiciaire), EcrivainMiseAuPointChambres.cs. Le nouvel etat de
/// la PC (loi, modalites, forfait, OPE_NOUV_ETAT_PC...) ne se saisit qu'en affaire unique dans RIG v2 : une PC seule se met a
/// jour par « Procédures collectives » (PROC_MPC).
/// </summary>
internal sealed class LecteurMiseAuPointChambres : IRechercheEcran
{
    public string Procedure => "PROC_MPCS";

    /// <summary>Le groupe du descripteur (Surveiller.cs), au mot pres.</summary>
    internal const string Groupe = "Procédures collectives à l'audience";

    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre];

    private static readonly string Requete = $$"""
        SELECT TOP (@plafond) aa.APPAF_ID_APPAF AS Id, i.INSTN_NUM_ROLE AS Role, i.INSTN_NOM_COURANT AS NomCourant,
               aa.APPAF_NUM_APPEL AS NumAppel, aa.APPAF_TYPE_CLASSEMENT AS TypeClassement, pc.PRCLL_NUM_PC AS NumeroPc,
               {{AudiencesV2.Libelle}} AS Audience, {{LecteurRetourAudience.Composition}} AS Compo, COUNT(*) OVER () AS Total
        FROM v2.AUDIENCE_CABINET AS a
        JOIN v2.APPEL_AFFAIRE AS aa ON aa.APPAF_ID_ADNC = a.AUDNC_ID_ADNC
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = aa.APPAF_ID_INSTN
        JOIN v2.TYPE_CLASSEMENT AS tc ON tc.R_TYPECLASSE_VALEUR = aa.APPAF_TYPE_CLASSEMENT
        LEFT JOIN v2.PROCEDURE_COLLECTIVE AS pc ON pc.PRCLL_ID_PRCLL = i.INSTN_ID_PRCLL
        {{AudiencesV2.Jointures}}
        WHERE i.INSTN_ID_PRCLL IS NOT NULL AND aa.APPAF_CODE_PLUMITIF IS NULL AND /*filtre*/
        ORDER BY a.AUDNC_DATE, a.AUDNC_HEURE, a.AUDNC_ID_ADNC, aa.APPAF_NUM_APPEL, i.INSTN_NUM_ROLE, aa.APPAF_ID_APPAF
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (du, au) = await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_RETAUD", "INTERV_AUD_DATE_MAX_RETAUD", 14, 0, ct);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var filtre = AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, null, true, du, au, parametres);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtre += " AND (REPLACE(i.INSTN_NUM_ROLE, ' ', '') LIKE @role OR i.INSTN_NOM_COURANT LIKE @texte)";
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(recherche.Texte.Replace(" ", "", StringComparison.Ordinal))));
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", filtre), Ligne, ct, [.. parametres]);
    }

    // fix-ok: mesure du 27/11/2026 (copie de dev) -- la composition posee par « Valider la décision » ne se relisait sur aucune
    // ligne : la colonne « Compo. » de la grille de RIG v2 (OPE_LIST_APPEL_AFFAIRE.cs:583) manquait (en-tete, requete, ligne).
    private static LigneLue Ligne(SqlDataReader r)
    {
        var role = FormeLigne.Texte(r, "Role");
        var precisions = FormeLigne.Joindre(
            $"Rôle {role}",
            LecteurRetourAudience.NumeroAppel(FormeLigne.Texte(r, "NumAppel"), FormeLigne.Texte(r, "TypeClassement")) is { Length: > 0 } n ? $"appel n° {n}" : null,
            FormeLigne.Texte(r, "NumeroPc") is { Length: > 0 } pc ? $"PC {pc}" : null,
            FormeLigne.Texte(r, "Audience"),
            LecteurRetourAudience.TexteComposition(r));
        var nom = FormeLigne.Texte(r, "NomCourant") is { Length: > 0 } nc ? nc : $"Instance n° {role}";
        return new LigneLue(nom, precisions, Groupe, "AFaire", []) { Id = FormeLigne.Texte(r, "Id") };
    }
}
