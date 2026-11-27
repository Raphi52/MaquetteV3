using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

/// <summary>
/// « Analyse IA » (PROC_IA) : UNE liasse INPI, designee par son numero, et la reponse du service d'analyse deja enregistree dans la base
/// du greffe. Mode d'emploi : LecteurEcran.cs. Plan « RIG v3 100 % fonctionnel », lot 15, 27/11/2026.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_IA (Detail OPE de proc_ia.md, qui fait foi).
///   - la liasse : DematLiasseCfe.GetDematLiasseCfeByNumeroNational (OPE_IA.cs:73, PROC_IA.cs:53-69) -- la table DEMAT_LIASSE_CFE, par
///     DMTLCF_NUMERO_NATIONAL_INPI ; la V2 la recoit de la visionneuse des pieces (OPE_DOC_DEMAT.cs:803-825), l'ecran V3 la cherche par ce
///     numero, son seul critere ;
///   - la reponse : DMTLCF_REPONSE_IA, le JSON brut du service (PROC_IA.cs:60-64) ; vide = pas encore analysee (cas nominal), remplie =
///     la liasse s'ouvre sur son resultat (ETP_ENTETE.cs:36-37) ;
///   - le resultat : verificationsChamps (champ, resultat, explication, indiceConfiance) et verificationGlobale (indiceConfiance,
///     commentaire, compteurs) -- IAHelper.cs:310-345 ; le resultat brut normalise en enum (« PAS DE CORRESPONDANCE » ->
///     PAS_DE_CORRESPONDANCE, IAHelper.cs:435-445) et affiche par sa Description (Correspond, Pas de correspondance, Proche, Non
///     disponible) ; l'indice en bandes Faible &lt; 0,5, Moyen &lt; 0,8, Bon &lt; 0,9, Très bon &lt; 1, Excellent (FormatIndiceConfiance,
///     IAHelper.cs:418-432).
/// LA LISTE (LireAsync) n'est pas montree par l'ecran (une Operation sur UN objet) : ce sont les liasses qui portent un numero national
/// INPI (celles que la V2 sait analyser), les plus recentes d'abord ; elle sert au banc et a la recherche.
/// GROUPES : DMTLCF_ORIGINE « INPI_FORMA » -> « Formalités INPI », « INPI_FORMA_MAJ » -> « Régularisations INPI » (la V2 prend la
/// derniere regularisation quand il y en a une, OPE_IA.cs:134-139).
/// ETAT : reponse vide -> AFaire « À analyser » ; remplie -> Validee « Analysée ». « En cours » n'existe que pendant l'appel du service.
/// LE LANCEMENT N'EST PAS BRANCHE : il poste les PDF et le JSON de la formalite au service d'analyse externe (IA.cs:130-193, POST
/// {IA_API_BASE_URL}/outil/rcs/verification_complete) -- un appel reseau externe que la V3 ne fait pas (regle du plan) : le geste reste
/// une demonstration etiquetee, rien n'est ecrit.
/// </summary>
internal sealed class LecteurAnalyseIa : IRechercheEcran, IFicheEcran
{
    public string Procedure => "PROC_IA";

    internal const string Formalites = "Formalités INPI", Regularisations = "Régularisations INPI";
    internal const string CritereNumero = "Numéro de liasse";

    public IReadOnlyCollection<string> Criteres { get; } = [CritereNumero];

    private const string Requete = """
        SELECT TOP (@plafond) l.DMTLCF_ID_DMTLCF, l.DMTLCF_NUMERO_NATIONAL_INPI, l.DMTLCF_ORIGINE, l.DMTLCF_DATE_CREATION, l.DMTLCF_NUM_GESTION,
               CASE WHEN l.DMTLCF_REPONSE_IA IS NULL OR LTRIM(l.DMTLCF_REPONSE_IA) = '' THEN 0 ELSE 1 END AS Analysee, COUNT(*) OVER () AS Total
        FROM v2.DEMAT_LIASSE_CFE AS l
        WHERE l.DMTLCF_NUMERO_NATIONAL_INPI IS NOT NULL AND l.DMTLCF_NUMERO_NATIONAL_INPI <> '' /*filtres*/
        ORDER BY l.DMTLCF_DATE_CREATION DESC, l.DMTLCF_ID_DMTLCF DESC
        """;

    public Task<LignesLues> LireAsync(SqlConnection baseGreffe, DateTime aujourdhui, int plafond, CancellationToken ct)
        => Lire(baseGreffe, new FiltresRecherche(), plafond, ct);

    public Task<LignesLues> ChercherAsync(SqlConnection baseGreffe, RechercheEcran recherche, DateTime aujourdhui, int plafond, CancellationToken ct)
    {
        var f = new FiltresRecherche();
        // Le numero national, EGAL (GetDematLiasseCfeByNumeroNational) : le texte ou le critere.
        if ((recherche.Critere(CritereNumero) ?? recherche.Texte) is { } numero && numero.Trim().Length > 0)
            f.Ajouter("l.DMTLCF_NUMERO_NATIONAL_INPI = @numero", ("@numero", numero.Trim()));
        return Lire(baseGreffe, f, plafond, ct);
    }

    private static Task<LignesLues> Lire(SqlConnection baseGreffe, FiltresRecherche f, int plafond, CancellationToken ct)
        => LectureEcran.LireAsync(baseGreffe, Requete.Replace("/*filtres*/", f.EnSuite, StringComparison.Ordinal), Ligne, ct,
            f.Parametres(new SqlParameter("@plafond", plafond)));

    private static LigneLue Ligne(SqlDataReader r)
    {
        var analysee = Convert.ToInt32(r["Analysee"], CultureInfo.InvariantCulture) == 1;
        var regularisation = FormeLigne.Texte(r, "DMTLCF_ORIGINE") == "INPI_FORMA_MAJ";
        var precisions = FormeLigne.Joindre(regularisation ? "régularisation INPI" : "formalité INPI",
            FormeLigne.Texte(r, "DMTLCF_NUM_GESTION") is { Length: > 0 } g ? $"dossier {FormeLigne.NumeroGestion(g)}" : null,
            $"reçue le {FormeLigne.Date(FormeLigne.DateDe(r, "DMTLCF_DATE_CREATION"))}", analysee ? "analysée" : "à analyser");
        return new LigneLue(FormeLigne.Texte(r, "DMTLCF_NUMERO_NATIONAL_INPI"), precisions, regularisation ? Regularisations : Formalites,
            analysee ? "Validee" : "AFaire", [])
        {
            Id = FormeLigne.Texte(r, "DMTLCF_NUMERO_NATIONAL_INPI"),
        };
    }

    public async Task<FicheEcran?> LireFicheAsync(SqlConnection baseGreffe, string id, DateTime maintenant, CancellationToken ct)
    {
        string numero, json;
        await using (var r = await LectureV2.LireAsync(baseGreffe, """
                         SELECT TOP (1) l.DMTLCF_NUMERO_NATIONAL_INPI, l.DMTLCF_REPONSE_IA, l.DMTLCF_ID_DMTLCF
                         FROM v2.DEMAT_LIASSE_CFE AS l WHERE l.DMTLCF_NUMERO_NATIONAL_INPI = @id ORDER BY l.DMTLCF_ID_DMTLCF DESC
                         """, ct, new SqlParameter("@id", id)))
        {
            if (!await r.ReadAsync(ct)) return null;
            numero = FormeLigne.Texte(r, "DMTLCF_NUMERO_NATIONAL_INPI");
            json = FormeLigne.Texte(r, "DMTLCF_REPONSE_IA");
        }
        // Ce que le lancement emporte (proc_ia.md:153, regle de source :223-231 ; IA.cs:147-150) : le champ est une sous-liste (Tableau).
        var pieces = new ChampFiche("Pièces de la formalité", "envoyées au service d'analyse externe au lancement : jamais appelé ici")
        {
            Colonnes = ["Pièce", "Source"],
            Lignes =
            [
                ["Données de la formalité (JSON)", "la dernière régularisation, sinon la liasse, sinon le flux reçu"],
                ["Pièces PDF", "tous les PDF du dossier de la liasse"],
            ],
        };
        if (json.Length == 0)
            return new FicheEcran(Procedure, numero, $"Liasse {numero}",
                [new("Pièces envoyées", [pieces]), new("Analyse", [])]);
        var (champs, synthese) = Lire(json);
        return new FicheEcran(Procedure, numero, $"Liasse {numero}",
        [
            new("Pièces envoyées", [pieces]),
            new("Champs vérifiés", [new ChampFiche("Champs vérifiés", $"{champs.Length} champ(s)")
                { Colonnes = ["Champ", "Résultat", "Explication", "Indice de confiance"], Lignes = champs }]),
            new("Synthèse", [new ChampFiche("Indice de confiance et commentaire", synthese.Resume)
                { Colonnes = ["Indice de confiance", "Commentaire"], Lignes = [[synthese.Indice, synthese.Commentaire]] }]),
        ]);
    }

    /// <summary>Le resultat de la V2 (IAHelper.GetTypeResultat, :435-445) en mots (Description de eTypeResultat).</summary>
    internal static string Resultat(string? brut) => (brut ?? "").Trim().Replace(' ', '_').ToUpperInvariant() switch
    {
        "CORRESPOND" => "Correspond",
        "PAS_DE_CORRESPONDANCE" => "Pas de correspondance",
        "PROCHE" => "Proche",
        "NON_DISPONIBLE" => "Non disponible",
        _ => "Aucun",
    };

    /// <summary>FormatIndiceConfiance (IAHelper.cs:418-432).</summary>
    internal static string Indice(string? brut)
    {
        if (string.IsNullOrWhiteSpace(brut)) return "<Aucun>";
        if (!double.TryParse(brut.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var i)) return brut;
        var bande = i < 0.5 ? "Faible" : i < 0.8 ? "Moyen" : i < 0.9 ? "Bon" : i < 1.0 ? "Très bon" : "Excellent";
        return $"{bande} ({i.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"))})";
    }

    private static (string[][] Champs, (string Indice, string Commentaire, string Resume) Synthese) Lire(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var racine = doc.RootElement;
            static string T(JsonElement e, string nom) => e.TryGetProperty(nom, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
            var champs = racine.TryGetProperty("verificationsChamps", out var vc) && vc.ValueKind == JsonValueKind.Array
                ? vc.EnumerateArray().Select(c => new[] { T(c, "champ"), Resultat(T(c, "resultat")), T(c, "explication"), Indice(T(c, "indiceConfiance")) }).ToArray()
                : [];
            if (!racine.TryGetProperty("verificationGlobale", out var g) || g.ValueKind != JsonValueKind.Object)
                return (champs, ("<Aucun>", "", "pas de synthèse dans la réponse"));
            var indice = Indice(T(g, "indiceConfiance"));
            return (champs, (indice, T(g, "commentaire"),
                $"{T(g, "nombreChampsVerifies")} champ(s) vérifié(s) : {T(g, "nombreCorrespondances")} correspondance(s), {T(g, "nombrePasCorrespondances")} sans correspondance, {T(g, "nombreProches")} proche(s), {T(g, "nombreNonDisponibles")} non disponible(s)"));
        }
        catch (JsonException e)
        {
            // La V2 dit l'echec de la deserialisation (PROC_IA.cs:65-68) : la fiche le dit aussi, sans rien inventer.
            return ([], ("<Aucun>", "", $"réponse illisible : {e.Message}"));
        }
    }
}
