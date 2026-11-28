// fix-ok: pas un correctif -- lot 11 du plan-f4fb097f (28/11/2026) : la requête est partagée avec le geste du bordereau
// (EcritureArchivage.cs), qui la joue dans SA transaction ; trois retouches d'un même découpage (Requete / LignesAsync).
using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.EcransMain;
using RigV3.Serveur.Socle.EcransGeneres;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Socle.EcransMain;

/// <summary>
/// « Archivage / radiation » (PROC_ARCHIRAD, web/src/ecrans/EcranArchivage.tsx) : les dossiers RCS radiés dont le bordereau
/// d'archivage n'est pas encore fait, lus dans la base V3 du greffe (rec.DossierRcs, rec.Entreprise, rec.ActionRegistre et
/// v2.ACTION_RCS_GED_ANNEXES). LECTURE SEULE (LectureV2). Plan « RIG v3 100 % fonctionnel », lot 20, 27/11/2026, marche du lot 17.
///
/// SOURCE (ancien RIG) : D:\RigApplication\Source\RIG\DLL\Processus\PROC_ARCHIRAD.
///   - La RECHERCHE (Operations/OPE_ARCHRSEL.cs, Toolbar_ButtonClick, :74-146) ne montre qu'un NOMBRE de dossiers, celui de la
///     requete de comptage : en mode dates (:87-95), DSSRC_DATE_RADIATION de 00 h la date mini a 24 h la date maxi et
///     DSSRC_TYPE_RCS dans InTypeRcs(type) (PROC_ARCHIRAD.cs:64-70 : « AC » tel quel, sinon le type suivi de 1 ou de 2) ; en mode
///     liste (:115-122), DSSRC_NUM_GESTION dans la liste saisie, separee par des espaces (_listeSaisieToSQL, :152-170) ; dans les
///     deux, SAUF les dossiers qui ont deja une annexe GED « Historique des FORMALITES » sur une de leurs actions
///     (EXCEPT ... ACTION_RCS_GED_ANNEXES, ACRGA_LIBELLE_PIECE).
///   - Dates et type sont obligatoires en mode dates (ult_DateMin/ult_DateMax/ult_TypeRcs MetierObligatoire,
///     proc_archirad.md:429-430) : sans eux, rien n'est cherche et le motif le dit.
///   - Le bordereau n'est permis que pour 1 a 149 dossiers (« nbTotal > 0 & nbTotal < 150 », :98-102).
///   - ORDRE : celui du traitement, DSSRC_DATE_RADIATION puis DSSRC_NUM_GESTION (PROC_ARCHIRAD.cs:79).
/// La V2 n'a PAS de grille sur cet ecran (seulement ce nombre) : la liste de la V3 montre les dossiers que ce nombre compte.
/// NON LUS : « dernier depot » et « pieces », colonnes de la maquette sans equivalent V2.
/// </summary>
public static class LectureArchivage
{
    /// <summary>Au plus autant de dossiers (le plafond des écrans faits main).</summary>
    public const int Plafond = RoutesEcransMain.Plafond;

    /// <summary>Le motif quand un critère obligatoire manque (mode dates).</summary>
    public const string CriteresManquants =
        "Saisissez les dates de radiation et le type RCS : RIG v2 les exige pour chercher les dossiers à archiver.";

    /// <summary>Le dossier a deja son annexe « Historique des FORMALITES » (la partie EXCEPT de OPE_ARCHRSEL.cs:91-93).</summary>
    private const string SansHistorique = """
        NOT EXISTS (SELECT 1 FROM rec.ActionRegistre AS a
                    JOIN v2.ACTION_RCS_GED_ANNEXES AS g ON g.ACRGA_ID_ACTRC = a.Id
                    WHERE a.IdDossier = d.Id AND g.ACRGA_LIBELLE_PIECE = 'Historique des FORMALITES')
        """;

    private const string Debut = """
        SELECT TOP (@plafond) d.Id, d.NumeroGestion, d.TypeRcs, d.DateRadiation, e.Denomination, e.Prenom, COUNT(*) OVER () AS Total
        FROM rec.DossierRcs AS d
        LEFT JOIN rec.Entreprise AS e ON e.Id = d.IdEntreprise
        WHERE
        """;

    private const string Ordre = "\nORDER BY d.DateRadiation, d.NumeroGestion";

    /// <summary>InTypeRcs (PROC_ARCHIRAD.cs:64-70) : les valeurs de DSSRC_TYPE_RCS d'un type de la liste.</summary>
    public static string[] ValeursType(string type) => type is "AC" or "" ? [type] : [type + "1", type + "2"];

    /// <summary>Les dossiers radiés de <paramref name="recherche"/>, au plus <paramref name="plafond"/>.</summary>
    public static async Task<DossiersArchivageLus> LireAsync(SqlConnection baseGreffe, RechercheArchivage recherche, CancellationToken ct,
                                                            int plafond = Plafond)
    {
        var (sql, parametres, motif) = Requete(recherche, plafond);
        if (motif is not null) return new DossiersArchivageLus([], 0, motif);
        await using var r = await LectureV2.LireAsync(baseGreffe, sql, ct, parametres);
        return await LignesAsync(r, ct);
    }

    /// <summary>La requête de <paramref name="recherche"/> et ses paramètres, ou le motif d'un critère obligatoire manquant -- aussi celle
    /// du bordereau (EcritureArchivage.cs), jouée dans la transaction du geste.</summary>
    internal static (string Sql, SqlParameter[] Parametres, string? Motif) Requete(RechercheArchivage recherche, int plafond = Plafond)
    {
        var parametres = new List<SqlParameter> { new("@plafond", SqlDbType.Int) { Value = plafond } };
        string filtre;
        var liste = (recherche.Liste ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (liste.Length > 0)
        {
            // Mode liste (OPE_ARCHRSEL.cs:115-122) : au plus le maximum du bordereau, au-dela la V2 refuse le bordereau.
            var numeros = liste.Take(RoutesArchivage.MaxBordereau).ToArray();
            parametres.AddRange(numeros.Select((n, i) => new SqlParameter($"@n{i}", SqlDbType.VarChar, 10) { Value = n }));
            filtre = $"d.NumeroGestion IN ({string.Join(", ", numeros.Select((_, i) => $"@n{i}"))})";
        }
        else
        {
            var type = (recherche.TypeRcs ?? "").Trim().ToUpperInvariant();
            if (recherche.Du is not { } du || recherche.Au is not { } au || !RoutesArchivage.TypesRcs.Contains(type))
                return ("", [], CriteresManquants);
            var valeurs = ValeursType(type);
            parametres.Add(new SqlParameter("@du", SqlDbType.DateTime) { Value = du.ToDateTime(TimeOnly.MinValue) });
            parametres.Add(new SqlParameter("@lendemainAu", SqlDbType.DateTime) { Value = au.ToDateTime(TimeOnly.MinValue).AddDays(1) });
            parametres.AddRange(valeurs.Select((v, i) => new SqlParameter($"@t{i}", SqlDbType.VarChar, 3) { Value = v }));
            filtre = "d.DateRadiation >= @du AND d.DateRadiation < @lendemainAu " +
                     $"AND d.TypeRcs IN ({string.Join(", ", valeurs.Select((_, i) => $"@t{i}"))})";
        }
        return ($"{Debut} {filtre}\n  AND {SansHistorique}{Ordre}", [.. parametres], null);
    }

    /// <summary>Les dossiers que la requête rend, et leur total.</summary>
    internal static async Task<DossiersArchivageLus> LignesAsync(SqlDataReader r, CancellationToken ct)
    {
        var lignes = new List<DossierArchivageLu>();
        var total = 0;
        while (await r.ReadAsync(ct))
        {
            total = Convert.ToInt32(r["Total"], CultureInfo.InvariantCulture);
            var designation = string.Join(" ", new[] { FormeLigne.Texte(r, "Denomination"), FormeLigne.Texte(r, "Prenom") }
                .Select(t => t.Trim()).Where(t => t.Length > 0));
            lignes.Add(new DossierArchivageLu(Convert.ToInt32(r["Id"], CultureInfo.InvariantCulture), FormeLigne.Texte(r, "NumeroGestion").Trim(),
                designation, FormeLigne.Texte(r, "TypeRcs").Trim(), FormeLigne.DateDe(r, "DateRadiation")));
        }
        return new DossiersArchivageLus([.. lignes], total, null);
    }
}
