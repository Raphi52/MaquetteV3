using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Occultations » (PROC_DJ_OCCULTATIONS) : les consignes d'occultation d'une decision judiciaire pour l'open data (les categories a
/// occulter, les mots a masquer et a conserver). Mode d'emploi : LecteurEcran.cs ; le geste : EcrivainOccultations.cs. Plan « RIG v3
/// 100 % fonctionnel », lot 10, 27/11/2026.
///
/// SOURCE (ancien RIG) : Source\RIG\DLL\Processus\PROC_DJ_OCCULTATIONS\ (proc_dj_occultations.md, Detail OPE, qui fait foi). RIG v2
/// ouvre UNE decision, recue de l'ecran de redaction (ETP_REDA.ShowsOccultationsDecision, PROC_REDA\ETP_REDA\ETP_REDA.cs:218-224,
/// « idDecisionJudiciaire ») ; sans decision, il n'affiche qu'un message (ETP_OCCULTATIONS.DoChargerDonnees, :48-61). Les
/// consignes sont DECISION_JUDICIAIRE_OCCULTATIONS (12 cases, OPE_OCCULTATIONS.cs:33-48) et DECISION_JUDICIAIRE_OCCULTATIONS_MOTS
/// (DJOM_A_MASQUER : a masquer, sinon a conserver, OPE_OCCULTATIONS_MOTS.cs:51-57). Une decision sans consignes en recoit en memoire,
/// pre-cochees par le reglage DJ_OCCULTATION_PAR_DEFAUT (« 1;0;1;... », une valeur par case dans l'ordre des colonnes,
/// DecisionJudiciaireOccultations._InitialisationChamps, :59-78 ; reglage absent : aucune case).
///
/// LA LISTE : la V3 n'a pas l'ecran de redaction qui donne la decision. Sans recherche : les decisions dont les consignes sont deja
/// saisies ; la recherche (le numero de role, le nom de l'affaire) retrouve toute decision, avec ou sans consignes.
/// REGLE D'ETAT : AFaire « À occulter » (aucune consigne enregistree), EnCours « Occultations saisies » (la ligne existe ; RIG v2 les
/// rouvre et les modifie a volonte : la ligne reste modifiable). Id = DCJDC_ID_DCJDC.
/// </summary>
internal sealed class LecteurOccultations : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_DJ_OCCULTATIONS";

    /// <summary>Les groupes du descripteur (ModifierEnvois.cs), au mot pres : la categorie de la decision (DCJDC_CAT_DECISION).</summary>
    internal const string Jugements = "Jugements", Ordonnances = "Ordonnances";

    /// <summary>Les sections et champs du descripteur, au mot pres.</summary>
    internal const string SectionDecision = "Décision", ChampRole = "Numéro de rôle", ChampType = "Type de décision",
                          SectionCategories = "Catégories d'occultation", SectionMasquer = "Mots à masquer", ChampMasquer = "Mots-clés à masquer",
                          SectionConserver = "Mots à conserver", ChampConserver = "Mots-clés à conserver", CritereRole = "Numéro de rôle";

    /// <summary>Les 12 cases de RIG v2 (OPE_OCCULTATIONS.Designer.cs:92-193, MetierTitre) et leur colonne, dans l'ordre des colonnes de
    /// la table (celui du reglage DJ_OCCULTATION_PAR_DEFAUT).</summary>
    internal static readonly (string Libelle, string Colonne)[] Cases =
    [
        ("Intérêt particulier", "DJOC_INTERET_PARTICULIER"),
        ("Personne Morale", "DJOC_OCCULTER_PM"),
        ("Personne physico-morale/géo-morale", "DJOC_OCCULTER_PPM"),
        ("Adresse", "DJOC_OCCULTER_ADRESSE"),
        ("Dates civiles (naissance , mariage, décès)", "DJOC_OCCULTER_DATE_CIVILES"),
        ("Plaque d'immatriculation", "DJOC_OCCULTER_PLAQUE_IMMATRICULATION"),
        ("Cadastre", "DJOC_OCCULTER_CADASTRE"),
        ("Chaine de numéro identifiante", "DJOC_OCCULTER_NUMERO_IDENTIFICATION"),
        ("Coordonnées électroniques", "DJOC_OCCULTER_COORDONNEES_ELECTRONIQUES"),
        ("Occultations spéciales soumises à décision du président du tribunal de commerce (Nom Prénom des juges et des Greffiers)", "DJOC_OCCULTER_NOM_JUGE_GREFFIER"),
        ("Occultations des motifs (débats en chambre du conseil protection de la vie privé et ou sécurité physique des parties)", "DJOC_OCCULTER_MOTIFS"),
        ("Occultations des motifs (Secret des affaires)", "DJOC_OCCULTER_MOTIFS_SECRET_AFFAIRES"),
    ];

    public IReadOnlyCollection<string> Criteres { get; } = [CritereRole];

    // Le groupe : « O » (ordonnance) dans la categorie de la decision, sinon jugement (CAT_DECISION de RIG_DEV : J, O, ...).
    private const string Requete = """
        SELECT TOP (@plafond) d.DCJDC_ID_DCJDC, d.DCJDC_DATE, d.DCJDC_CAT_DECISION, d.DCJDC_TYPE_DECISION, td.R_TYPEDECISI_LIBELLE_ECRAN,
               i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT, o.DJOC_ID_DJOC,
               (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS AS m WHERE m.DJOM_ID_DJOC = o.DJOC_ID_DJOC AND m.DJOM_A_MASQUER = 1) AS Masquer,
               (SELECT COUNT(*) FROM v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS AS m WHERE m.DJOM_ID_DJOC = o.DJOC_ID_DJOC AND m.DJOM_A_MASQUER = 0) AS Conserver,
               COUNT(*) OVER () AS Total
        FROM v2.DECISION_JUDICIAIRE AS d
        JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
        LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
        LEFT JOIN v2.DECISION_JUDICIAIRE_OCCULTATIONS AS o ON o.DJOC_ID_DCJDC = d.DCJDC_ID_DCJDC
        WHERE /*filtre*/
        ORDER BY i.INSTN_NUM_ROLE DESC, d.DCJDC_NUM_ORDRE, d.DCJDC_ID_DCJDC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", "o.DJOC_ID_DJOC IS NOT NULL", StringComparison.Ordinal), Ligne, ct,
                                  new SqlParameter("@plafond", plafond));

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var filtres = new List<string> { "1 = 1" };
        var parametres = new List<SqlParameter> { new("@plafond", plafond) };
        if (!string.IsNullOrWhiteSpace(recherche.Texte))
        {
            filtres.Add("(i.INSTN_NUM_ROLE LIKE @texte OR i.INSTN_NOM_COURANT LIKE @texte)");
            parametres.Add(new SqlParameter("@texte", FormeLigne.Contient(recherche.Texte)));
        }
        if (recherche.Critere(CritereRole) is { } role)
        {
            filtres.Add("i.INSTN_NUM_ROLE LIKE @role");
            parametres.Add(new SqlParameter("@role", FormeLigne.Contient(role.Replace(" ", "", StringComparison.Ordinal))));
        }
        return LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtre*/", string.Join(" AND ", filtres), StringComparison.Ordinal), Ligne, ct, [.. parametres]);
    }

    private static LigneLue Ligne(SqlDataReader r)
    {
        var saisies = r["DJOC_ID_DJOC"] is not DBNull;
        var masquer = Convert.ToInt32(r["Masquer"], CultureInfo.InvariantCulture);
        var conserver = Convert.ToInt32(r["Conserver"], CultureInfo.InvariantCulture);
        var date = FormeLigne.DateDe(r, "DCJDC_DATE");
        var type = FormeLigne.Texte(r, "R_TYPEDECISI_LIBELLE_ECRAN") is { Length: > 0 } t ? t : FormeLigne.Texte(r, "DCJDC_TYPE_DECISION");
        var precisions = FormeLigne.Joindre($"Rôle {FormeLigne.Texte(r, "INSTN_NUM_ROLE")}", type, date is null ? "non rendue" : $"du {FormeLigne.Date(date)}",
            !saisies ? "occultations non saisies"
                : masquer + conserver == 0 ? "cases enregistrées, aucun mot"
                : FormeLigne.Joindre(masquer == 0 ? null : $"{masquer} mot{(masquer > 1 ? "s" : "")} à masquer",
                                     conserver == 0 ? null : $"{conserver} mot{(conserver > 1 ? "s" : "")} à conserver"));
        var nom = FormeLigne.Texte(r, "INSTN_NOM_COURANT");
        return new LigneLue(nom.Length > 0 ? nom : FormeLigne.Texte(r, "INSTN_NUM_ROLE"), precisions,
            FormeLigne.Texte(r, "DCJDC_CAT_DECISION") == "O" ? Ordonnances : Jugements, saisies ? "EnCours" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "DCJDC_ID_DCJDC"),
        };
    }

    /// <summary>Les consignes d'une decision : ses cases (celles de la base, ou celles du reglage par defaut) et ses mots.</summary>
    internal sealed record Consignes(int IdDecision, int IdInstance, string Role, string Nom, string Type, int? IdOccultations, bool[] Cases,
                                     List<string> Masquer, List<string> Conserver);

    /// <summary>Les consignes de la decision <paramref name="id"/> ; null si elle n'existe pas. <paramref name="lire"/> execute un SELECT
    /// (celui du lecteur, ou celui du geste dans sa transaction).</summary>
    internal static async Task<Consignes?> ConsignesAsync(Func<string, SqlParameter[], Task<SqlDataReader>> lire, int id, CancellationToken ct)
    {
        Consignes? c = null;
        await using (var r = await lire($"""
            SELECT d.DCJDC_ID_DCJDC, d.DCJDC_ID_INSTN, i.INSTN_NUM_ROLE, i.INSTN_NOM_COURANT,
                   ISNULL(td.R_TYPEDECISI_LIBELLE_ECRAN, ISNULL(d.DCJDC_TYPE_DECISION, '')) AS TypeDecision, o.DJOC_ID_DJOC,
                   {string.Join(", ", Cases.Select(k => "o." + k.Colonne))}
            FROM v2.DECISION_JUDICIAIRE AS d
            JOIN v2.INSTANCE AS i ON i.INSTN_ID_INSTN = d.DCJDC_ID_INSTN
            LEFT JOIN v2.TYPE_DECISION AS td ON td.R_TYPEDECISI_VALEUR = d.DCJDC_TYPE_DECISION
            LEFT JOIN v2.DECISION_JUDICIAIRE_OCCULTATIONS AS o ON o.DJOC_ID_DCJDC = d.DCJDC_ID_DCJDC
            WHERE d.DCJDC_ID_DCJDC = @id
            """, [new SqlParameter("@id", id)]))
        {
            if (!await r.ReadAsync(ct)) return null;
            var idOccultations = r["DJOC_ID_DJOC"] is int n ? n : (int?)null;
            c = new Consignes(id, Convert.ToInt32(r["DCJDC_ID_INSTN"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "INSTN_NUM_ROLE"),
                FormeLigne.Texte(r, "INSTN_NOM_COURANT"), FormeLigne.Texte(r, "TypeDecision"), idOccultations,
                [.. Cases.Select(k => r[k.Colonne] is true)], [], []);
        }
        if (c.IdOccultations is { } idOcc)
        {
            await using var m = await lire("SELECT DJOM_MOTS, DJOM_A_MASQUER FROM v2.DECISION_JUDICIAIRE_OCCULTATIONS_MOTS WHERE DJOM_ID_DJOC = @o ORDER BY DJOM_ID_DJOM",
                                           [new SqlParameter("@o", idOcc)]);
            while (await m.ReadAsync(ct)) (m["DJOM_A_MASQUER"] is true ? c.Masquer : c.Conserver).Add(FormeLigne.Texte(m, "DJOM_MOTS"));
        }
        else
        {
            // Les cases par defaut du greffe (DJ_OCCULTATION_PAR_DEFAUT), une valeur par case ; absent : aucune case cochee.
            await using var p = await lire("SELECT Valeur FROM soc.Reglage WHERE Code = 'DJ_OCCULTATION_PAR_DEFAUT'", []);
            if (await p.ReadAsync(ct))
            {
                var valeurs = FormeLigne.Texte(p, "Valeur").Split(';');
                for (var k = 0; k < Cases.Length && k < valeurs.Length; k++) c.Cases[k] = valeurs[k].Trim() == "1";
            }
        }
        return c;
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        if (await ConsignesAsync((sql, p) => LectureV2.LireAsync(baseGreffe, sql, ct, p), n, ct) is not { } c) return null;
        return new FicheEcran(Procedure, id, c.Nom.Length > 0 ? c.Nom : c.Role,
        [
            new SectionFiche(SectionDecision, [new(ChampRole, c.Role), new(ChampType, c.Type)]),
            new SectionFiche(SectionCategories, [.. Cases.Select((k, i) => new ChampFiche(k.Libelle, FormeLigne.Case(c.Cases[i])) { Modifiable = true })]),
            new SectionFiche(SectionMasquer, [new(ChampMasquer, string.Join("\n", c.Masquer)) { Modifiable = true }]),
            new SectionFiche(SectionConserver, [new(ChampConserver, string.Join("\n", c.Conserver)) { Modifiable = true }]),
        ]);
    }
}
