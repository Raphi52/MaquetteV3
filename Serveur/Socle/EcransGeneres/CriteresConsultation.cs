using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransGeneres;

// LES CRITERES ET LES FICHES DES CONSULTATIONS « TROUVER » (plan « RIG v3 100 % fonctionnel », lot 6, 26/11/2026) : ce que
// les seize lecteurs du verbe Trouver (Pont/EcransGeneres/Trouver.cs) partagent, et que FormeLigne (LecteurEcran.cs) n'a
// pas. Le panneau des criteres de l'ecran (web/src/ecrans/formes/commun.tsx, PanneauCriteres) ne pose que des zones de
// texte : une date s'y tape « 31/12/2025 » ou « 2025-12-31 », un montant « 1 200,50 » ou « 1200.50 », une case « oui ».

/// <summary>La lecture des criteres saisis, et l'assemblage des filtres d'une recherche, pour les consultations.</summary>
internal static class CriteresConsultation
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Une date saisie (« 31/12/2025 », « 31/12/25 », « 2025-12-31 ») ; null si elle ne se lit pas.</summary>
    public static DateTime? Date(string? texte)
        => DateTime.TryParseExact(texte?.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd"], CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var d) ? d.Date : null;

    /// <summary>Un montant saisi (« 1 200,50 », « 1200.50 », « 95 € ») ; null s'il ne se lit pas.</summary>
    public static decimal? Montant(string? texte)
    {
        var t = (texte ?? "").Replace("€", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
                             .Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(t, NumberStyles.Number, Francais, out var m) || decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out m)
            ? m : null;
    }

    /// <summary>Un entier saisi ; null s'il ne se lit pas.</summary>
    public static int? Entier(string? texte)
        => int.TryParse(texte?.Replace(" ", "", StringComparison.Ordinal).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>Un numero sans ses blancs (« 2001 B 00411 » -> « 2001B00411 », « 900 300 001 » -> « 900300001 »).</summary>
    public static string Numero(string texte) => texte.Replace(" ", "", StringComparison.Ordinal).Trim().ToUpperInvariant();

    /// <summary>Une case saisie : « oui », « o », « x », « 1 », « vrai » -> vrai ; « non », « n », « 0 », « faux » -> faux ; sinon null.</summary>
    public static bool? OuiNon(string? texte) => (texte ?? "").Trim().ToLowerInvariant() switch
    {
        "oui" or "o" or "x" or "1" or "vrai" or "true" => true,
        "non" or "n" or "0" or "faux" or "false" => false,
        _ => null,
    };

    // Le format des montants de la V2 : milliers separes par une ESPACE ordinaire, virgule decimale (OPE_RESULTAT de PROC_RECHENC,
    // OPE_RESULTAT.cs:130-132) -- la culture fr-FR de .NET met une espace fine insecable, invisible dans un banc ou un journal.
    private static readonly NumberFormatInfo FormatMontant = new() { NumberGroupSeparator = " ", NumberDecimalSeparator = ",", NumberGroupSizes = [3] };

    /// <summary>Un montant d'affichage (« 1 200,50 € »).</summary>
    public static string Euros(decimal? m) => m is { } v ? v.ToString("N2", FormatMontant) + " €" : "";

    /// <summary>Un montant de fiche, au format des champs (« 1200.50 », FicheEcran.cs).</summary>
    public static string MontantFiche(decimal? m) => m is { } v ? v.ToString("0.00", CultureInfo.InvariantCulture) : "";

    /// <summary>Le decimal d'une colonne (null si NULL).</summary>
    public static decimal? Decimal(SqlDataReader r, string colonne)
        => r[colonne] is DBNull ? null : Convert.ToDecimal(r[colonne], CultureInfo.InvariantCulture);

    /// <summary>L'entier d'une colonne (null si NULL).</summary>
    public static int? EntierDe(SqlDataReader r, string colonne)
        => r[colonne] is DBNull ? null : Convert.ToInt32(r[colonne], CultureInfo.InvariantCulture);

    /// <summary>Le booleen d'une colonne (faux si NULL).</summary>
    public static bool Vrai(SqlDataReader r, string colonne) => r[colonne] is bool b && b;

    /// <summary>Les lignes d'une sous-liste (Tableau de fiche) : une requete SELECT seule, une cellule par colonne lue.</summary>
    public static async Task<string[][]> SousListeAsync(SqlConnection baseGreffe, string sql, Func<SqlDataReader, string[]> ligne,
                                                        CancellationToken ct, params SqlParameter[] parametres)
    {
        var lignes = new List<string[]>();
        await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, parametres);
        while (await r.ReadAsync(ct)) lignes.Add(ligne(r));
        return [.. lignes];
    }

    /// <summary>Un champ de sous-liste d'une fiche (lu seulement).</summary>
    public static ChampFiche Tableau(string libelle, string[] colonnes, string[][] lignes) => new(libelle, "") { Colonnes = colonnes, Lignes = lignes };
}

/// <summary>
/// Les filtres d'UNE recherche, assembles en clauses jointes par AND (jamais concatenes : chaque valeur est un parametre),
/// la regle de la V2 reconstruite sans son idiome « firstPassage » (proc_rechenc.md:268, « preferer une liste de clauses »).
/// </summary>
internal sealed class FiltresRecherche
{
    private readonly List<string> _clauses = [];
    private readonly List<SqlParameter> _parametres = [];

    /// <summary>Aucun filtre ajoute.</summary>
    public bool Vide => _clauses.Count == 0;

    /// <summary>Ajoute une clause et ses parametres (noms uniques, « @x »).</summary>
    public void Ajouter(string clause, params (string Nom, object? Valeur)[] parametres)
    {
        _clauses.Add("(" + clause + ")");
        foreach (var (nom, valeur) in parametres) _parametres.Add(new SqlParameter(nom, valeur ?? DBNull.Value));
    }

    /// <summary>« AND (c1) AND (c2) » a placer derriere un WHERE existant ; vide sans filtre.</summary>
    public string EnSuite => _clauses.Count == 0 ? "" : " AND " + string.Join(" AND ", _clauses);

    /// <summary>Les parametres des clauses, suivis de <paramref name="autres"/>.</summary>
    public SqlParameter[] Parametres(params SqlParameter[] autres) => [.. autres, .. _parametres];
}
