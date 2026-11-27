using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Rédaction en masse » (PROC_REDA_MASSE) : les decisions d'une audience a rediger en lot (generer le document, valider au greffe
/// = attribuer la minute, imprimer la minute). Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainRedactionEnMasse.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 10, 27/11/2026.
///
/// FORME « Audience » (et non « File ») : RIG v2 lance la redaction en masse depuis le retour d'UNE audience, avec la liste des
/// decisions de cette audience (« ListeIdDJ », PROC_RETAUD\Operation\OPE_INFORMATIONS_AUDIENCE.ult_BtnRedaEnMasse_ButtonClick, :574-584 ;
/// PROC_REDA_MASSE.DoInitObject, :58-62) -- proc_reda_masse.md, Detail OPE, qui fait foi.
///
/// SOURCE (ancien RIG) : la selection de l'audience, OPE_INFORMATIONS_AUDIENCE.GetDJForRedaMasse (:586-608) -- pour chaque appel de
/// l'audience (tries « prononcé » d'abord, puis par numero d'appel) : la decision PRONONCEE a cet appel si elle est seule, a l'etat de
/// redaction 0, 1, 2 ou 3, et d'un type de decision renseigne ; et les decisions de l'instance SANS prononce dont le type est « avec
/// redaction » (R_TYPEDECISI_DECISION_AVEC_REDACTION). L'audience : la fenetre du retour d'audience (INTERV_AUD_DATE_MIN/MAX_RETAUD, 14 et
/// 0 jours par defaut, OPE_INFORMATIONS_AUDIENCE.cs:107-114 -- AudiencesV2.FenetreAsync), ou la date et la chambre choisies.
///
/// GROUPES : la categorie de la decision (DCJDC_CAT_DECISION « O » ordonnance, sinon jugement).
/// REGLE D'ETAT (les predicats de la grille, OPE_LISTE_DJ_REDA_MASSE.cs:853-875) :
///   - AFaire « À générer » : etat 0, le document reste a generer (CanCheckGeneration) ;
///   - EnCours « Brouillon ou révision » : etat 1 ou 2, la minute se valide sans regenerer (CanCheckValidation) ;
///   - Validee « Minute validée » : etat 3 « révisé à rendre » (la minute est attribuee ; la suite est l'impression legale).
/// Id = DCJDC_ID_DCJDC.
///
/// NON REPRIS (nomme) : les dates de delibere et de prononce modifiables dans la grille (colonnes boutons, FormAppelAffaire) ; le modele de
/// document (ULT_COMBO_MODELE_DOCUMENT) : la forme Audience ne les saisit pas.
/// </summary>
internal sealed class LecteurRedactionEnMasse : IRechercheEcran
{
    public string Procedure => "PROC_REDA_MASSE";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres.</summary>
    internal const string Jugements = "Jugements", Ordonnances = "Ordonnances";

    /// <summary>Les parametres de l'audience (le descripteur), que la recherche lit.</summary>
    internal const string CritereDate = "Date de l'audience", CritereChambre = "Chambre";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereDate, CritereChambre];

    // Les appels de l'audience choisie (le filtre se pose a la place de /*filtre*/, dans les deux branches). Pas de WITH : la porte de
    // lecture V2 n'admet qu'une requete qui commence par SELECT (LectureV2.ExigerLecture).
    private const string Appels = $$"""
        (SELECT a.AUDNC_ID_ADNC, a.AUDNC_DATE, {{AudiencesV2.Libelle}} AS Audience, ap.APPAF_ID_APPAF, ap.APPAF_ID_INSTN, ap.APPAF_NUM_APPEL,
                CONVERT(bit, ISNULL(tc.R_TYPECLASSE_PRONONCE, 0)) AS Prononce
         FROM v2.AUDIENCE_CABINET AS a
         {{AudiencesV2.Jointures}}
         JOIN v2.APPEL_AFFAIRE AS ap ON ap.APPAF_ID_ADNC = a.AUDNC_ID_ADNC
         LEFT JOIN v2.TYPE_CLASSEMENT AS tc ON tc.R_TYPECLASSE_VALEUR = ap.APPAF_TYPE_CLASSEMENT
         WHERE /*filtre*/)
        """;

    private const string Requete = $$"""
        SELECT TOP (@plafond) u.DCJDC_ID_DCJDC, u.Audience, u.APPAF_NUM_APPEL, d.DCJDC_ETAT_REDACTION, d.DCJDC_CAT_DECISION, d.DCJDC_NUM_REPERTOIRE,
               d.DCJDC_DATE, d.DCJDC_DATE_PREVUE, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, td.R_TYPEDECISI_LIBELLE_ECRAN, er.R_ETATREDACT_LIBELLE_ECRAN,
               COUNT(*) OVER () AS Total
        FROM (
            SELECT dj.*, ROW_NUMBER() OVER (PARTITION BY dj.DCJDC_ID_DCJDC ORDER BY dj.AUDNC_DATE, dj.Prononce DESC, dj.APPAF_NUM_APPEL) AS Rang
            FROM (
                SELECT ap.*, d.DCJDC_ID_DCJDC FROM {{Appels}} AS ap
                JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_APPAF_PRONONCER = ap.APPAF_ID_APPAF
                WHERE d.DCJDC_ETAT_REDACTION IN ('0', '1', '2', '3') AND d.DCJDC_TYPE_DECISION IS NOT NULL
                  AND (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE AS x WHERE x.DCJDC_ID_APPAF_PRONONCER = ap.APPAF_ID_APPAF) = 1
                UNION ALL
                SELECT ap.*, d.DCJDC_ID_DCJDC FROM {{Appels}} AS ap
                JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_INSTN = ap.APPAF_ID_INSTN AND d.DCJDC_ID_APPAF_PRONONCER IS NULL
                JOIN v2.TYPE_DECISION AS t ON t.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION AND t.R_TYPEDECISI_DECISION_AVEC_REDACTION = 1
            ) AS dj
        ) AS u
        JOIN v2.DECISION_JUDICIAIRE AS d ON d.DCJDC_ID_DCJDC = u.DCJDC_ID_DCJDC
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
        LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
        LEFT JOIN v2.ETAT_REDACTION AS er ON er.R_ETATREDACT_VALEUR = d.DCJDC_ETAT_REDACTION
        WHERE u.Rang = 1 /*texte*/
        ORDER BY u.AUDNC_DATE, u.AUDNC_ID_ADNC, u.Prononce DESC, u.APPAF_NUM_APPEL, d.DCJDC_ID_DCJDC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => ChercherAsync(baseGreffe, new RechercheEcran(null, new Dictionary<string, string>()), aujourdhui, plafond, ct);

    public async Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var (du, au) = await AudiencesV2.FenetreAsync(baseGreffe, aujourdhui, "INTERV_AUD_DATE_MIN_RETAUD", "INTERV_AUD_DATE_MAX_RETAUD", 14, 0, ct);
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        var sql = Requete.Replace("/*filtre*/", AudiencesV2.Filtre(recherche, CritereDate, CritereChambre, null, true, du, au, parametres), StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            sql = sql.Replace("/*texte*/", "AND (i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)", StringComparison.Ordinal);
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        return await LectureEcran.LireAsync(baseGreffe, sql, Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var etat = FormeLigne.Texte(r, "DCJDC_ETAT_REDACTION");
        var minute = FormeLigne.Texte(r, "DCJDC_NUM_REPERTOIRE");
        var appel = FormeLigne.Texte(r, "APPAF_NUM_APPEL");
        var precisions = FormeLigne.Joindre($"Rôle {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN"),
            FormeLigne.Texte(r, "R_ETATREDACT_LIBELLE_ECRAN") is { Length: > 0 } e ? e : $"état de rédaction {etat}",
            minute.Length > 0 ? $"minute {minute}" : null, FormeLigne.Texte(r, "Audience").TrimEnd(' ', '-'), appel.Length > 0 ? $"appel {appel}" : null);
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        return new LigneLue(nom.Length > 0 ? nom : FormeLigne.Texte(r, "INSTN_NUM_ROLE"), precisions,
            FormeLigne.Texte(r, "DCJDC_CAT_DECISION") == "O" ? Ordonnances : Jugements, etat switch { "0" => "AFaire", "3" => "Validee", _ => "EnCours" }, [])
        {
            Id = FormeLigne.Texte(r, "DCJDC_ID_DCJDC"),
        };
    }
}
