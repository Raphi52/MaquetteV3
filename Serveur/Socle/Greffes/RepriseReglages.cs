using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Contrats.Socle;

namespace RigV3.Serveur.Socle.Greffes;

// ============================================================
// LA REPRISE DES REGLAGES D'UN GREFFE, V2 -> V3 (SOC-003 T04, T05, T06 ; lot 4).
//
// Elle COPIE, sans rien interpreter (decision Q2, valeur hors norme REGLAGE-OUI-NON) :
//   dbo.PARAMETRAGE      -> soc.Reglage          (chaque colonne, texte tel quel)
//   dbo.GREFFE_PARAMETRE -> soc.ParametreGreffe  (une ligne par colonne, type garde)
//   dbo.MAIL_TEMPLATE    -> soc.ModeleCourriel   (s'il existe dans la base du greffe)
// Elle lit la V2 par LectureV2 (un SELECT seul : jamais d'ecriture) et remplace, dans
// UNE transaction, ce que la base V3 tenait : chaque repetition du transfert repart de
// la V2. La comparaison (CA01) relit les deux cotes et compare chaque valeur, typee,
// au caractere pres. Le rapport (CA04, CA05) liste ce que le referent Socle tranche.
// ============================================================

/// <summary>Un ecart entre la V2 et la V3 : la table V2, la cle, la colonne et les deux valeurs.</summary>
public sealed record EcartReprise(string Table, string Cle, string Colonne, string? ValeurV2, string? ValeurV3);

/// <summary>Ce qu'une reprise a copie.</summary>
public sealed record BilanReprise(int Reglages, int Parametres, int Modeles);

public static class RepriseReglages
{
    private static readonly (string V2, string V3)[] ColonnesReglage =
    [
        ("PARAM_CODE", "Code"), ("PARAM_LIBELLE", "Libelle"), ("PARAM_VALEUR", "Valeur"),
        ("PARAM_UTILISATION", "Utilisation"), ("PARAM_AUTEUR_CREATION", "AuteurCreation"),
        ("PARAM_DATE_CREATION", "DateCreation"), ("PARAM_AUTEUR_MODIF", "AuteurModif"),
        ("PARAM_DATE_MODIF", "DateModif"), ("PARAM_ID_PARAM", "IdV2"),
    ];

    private static readonly (string V2, string V3)[] ColonnesModele =
    [
        ("MAILT_CODE", "Code"), ("MAILT_CONTENU", "Contenu"), ("MAILT_DATE_MODIF", "DateModif"),
        ("MAILT_AUTEUR_MODIF", "AuteurModif"), ("MAILT_ID_MAILT", "IdV2"),
    ];

    private const string ModeleExiste = "SELECT CASE WHEN OBJECT_ID(N'dbo.MAIL_TEMPLATE') IS NULL THEN 0 ELSE 1 END";

    /// <summary>Copie les reglages de la base V2 du greffe dans sa base V3 (remplace l'existant).</summary>
    public static async Task<BilanReprise> ReprendreAsync(SqlConnection v2, SqlConnection v3, CancellationToken ct = default)
    {
        // La ligne de GREFFE_PARAMETRE est lue AVANT la transaction : une base qui en
        // aurait plusieurs arrete la reprise sans avoir rien touche.
        var parametres = await LireParametresV2Async(v2, ct);
        var avecModele = Convert.ToInt32(await LectureV2.ValeurAsync(v2, ModeleExiste, ct)) == 1;

        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(ct);
        await ExecuterAsync(v3, tx, "DELETE FROM soc.Reglage; DELETE FROM soc.ParametreGreffe; DELETE FROM soc.ModeleCourriel;", ct);

        await CopierAsync(v2, v3, tx, "dbo.PARAMETRAGE", "soc.Reglage", ColonnesReglage, ct);
        if (avecModele) await CopierAsync(v2, v3, tx, "dbo.MAIL_TEMPLATE", "soc.ModeleCourriel", ColonnesModele, ct);

        foreach (var (colonne, (valeur, type)) in parametres)
        {
            await using var ins = new SqlCommand(
                "INSERT INTO soc.ParametreGreffe (Colonne, Valeur, TypeV2) VALUES (@c, @v, @t);", v3, tx);
            ins.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 128) { Value = colonne });
            ins.Parameters.Add(new SqlParameter("@v", SqlDbType.Variant) { Value = valeur ?? DBNull.Value });
            ins.Parameters.Add(new SqlParameter("@t", SqlDbType.VarChar, 40) { Value = type });
            await ins.ExecuteNonQueryAsync(ct);
        }

        var bilan = new BilanReprise(
            await CompterAsync(v3, tx, "soc.Reglage", ct),
            await CompterAsync(v3, tx, "soc.ParametreGreffe", ct),
            await CompterAsync(v3, tx, "soc.ModeleCourriel", ct));
        await tx.CommitAsync(ct);
        return bilan;
    }

    /// <summary>
    /// Compare la V2 et la V3, valeur par valeur (CA01) : chaque reglage, chaque colonne
    /// de GREFFE_PARAMETRE, chaque modele de courriel. Texte compare au caractere pres
    /// (casse et espaces compris), nombre a sa valeur, date a la milliseconde.
    /// </summary>
    public static async Task<IReadOnlyList<EcartReprise>> ComparerAsync(SqlConnection v2, SqlConnection v3, CancellationToken ct = default)
    {
        var ecarts = new List<EcartReprise>();
        await ComparerLignesAsync(v2, v3, "PARAMETRAGE", "dbo.PARAMETRAGE", "soc.Reglage", ColonnesReglage, ecarts, ct);
        if (Convert.ToInt32(await LectureV2.ValeurAsync(v2, ModeleExiste, ct)) == 1)
            await ComparerLignesAsync(v2, v3, "MAIL_TEMPLATE", "dbo.MAIL_TEMPLATE", "soc.ModeleCourriel", ColonnesModele, ecarts, ct);

        var attendus = await LireParametresV2Async(v2, ct);
        var repris = new Dictionary<string, (object? Valeur, string Type)>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand("SELECT Colonne, Valeur, TypeV2 FROM soc.ParametreGreffe;", v3))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                repris[r.GetString(0)] = (r.IsDBNull(1) ? null : r.GetValue(1), r.GetString(2));
        foreach (var (colonne, (valeur, type)) in attendus)
        {
            if (!repris.TryGetValue(colonne, out var v3Valeur))
                ecarts.Add(new("GREFFE_PARAMETRE", colonne, "(colonne)", Texte(valeur), "(absente en V3)"));
            else
            {
                if (!MemeValeur(valeur, v3Valeur.Valeur))
                    ecarts.Add(new("GREFFE_PARAMETRE", colonne, "Valeur", Texte(valeur), Texte(v3Valeur.Valeur)));
                if (!string.Equals(type, v3Valeur.Type, StringComparison.Ordinal))
                    ecarts.Add(new("GREFFE_PARAMETRE", colonne, "TypeV2", type, v3Valeur.Type));
            }
        }
        foreach (var colonne in repris.Keys.Where(c => !attendus.ContainsKey(c)))
            ecarts.Add(new("GREFFE_PARAMETRE", colonne, "(colonne)", "(absente en V2)", Texte(repris[colonne].Valeur)));
        return ecarts;
    }

    /// <summary>
    /// La liste remise au referent Socle (T05) : les reglages gardes qu'aucune fonction de
    /// la V3 ne lit (CA04) et les chemins reseau que ce serveur n'atteint pas (CA05). Lecture
    /// seule : aucun reglage n'est modifie.
    /// </summary>
    public static async Task<RapportReprise> RapportAsync(string code, SqlConnection v3, TimeSpan delaiChemin,
                                                          CancellationToken ct = default)
    {
        var lus = LectureReglage.Toutes().Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reglages = new List<(string Code, string? Valeur)>();
        await using (var cmd = new SqlCommand("SELECT Code, Valeur FROM soc.Reglage ORDER BY Code;", v3))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                reglages.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));

        var nonLus = reglages.Where(x => !lus.Contains(x.Code)).Select(x => x.Code).ToList();
        var chemins = reglages.Where(x => x.Valeur is { } v && EstCheminReseau(v)).ToList();
        var verdicts = await Task.WhenAll(chemins.Select(x => CauseInjoignableAsync(x.Valeur!, delaiChemin)));
        var injoignables = chemins.Zip(verdicts)
            .Where(p => p.Second is not null)
            .Select(p => new CheminInjoignable(p.First.Code, p.First.Valeur!, p.Second!))
            .ToList();
        return new RapportReprise(code, reglages.Count, nonLus, injoignables);
    }

    /// <summary>Un chemin reseau : un chemin UNC (\\serveur\partage...).</summary>
    public static bool EstCheminReseau(string valeur) => valeur.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>Pourquoi ce chemin ne repond pas depuis ce serveur ; null s'il repond.
    /// Un serveur absent peut faire attendre longtemps : au-dela du delai, il est declare injoignable.</summary>
    public static async Task<string?> CauseInjoignableAsync(string chemin, TimeSpan delai)
    {
        var essai = Task.Run(() => File.Exists(chemin) || Directory.Exists(chemin));
        if (await Task.WhenAny(essai, Task.Delay(delai)) != essai)
            return $"pas de reponse en {delai.TotalSeconds:0} s";
        return await essai ? null : "introuvable depuis ce serveur";
    }

    // --- outils ---------------------------------------------------------------

    private static async Task CopierAsync(SqlConnection v2, SqlConnection v3, SqlTransaction tx, string source, string cible,
                                          (string V2, string V3)[] colonnes, CancellationToken ct)
    {
        var sql = $"SELECT {string.Join(", ", colonnes.Select(c => c.V2))} FROM {source}";
        await using var r = await LectureV2.LireAsync(v2, sql, ct);
        using var bulk = new SqlBulkCopy(v3, SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = cible,
            BulkCopyTimeout = 600,
        };
        foreach (var (a, b) in colonnes) bulk.ColumnMappings.Add(a, b);
        await bulk.WriteToServerAsync(r, ct);
    }

    private static async Task<Dictionary<string, (object? Valeur, string Type)>> LireParametresV2Async(SqlConnection v2, CancellationToken ct)
    {
        var resultat = new Dictionary<string, (object? Valeur, string Type)>(StringComparer.Ordinal);
        await using var r = await LectureV2.LireAsync(v2, "SELECT * FROM dbo.GREFFE_PARAMETRE", ct);
        var schema = r.GetColumnSchema();
        var lignes = 0;
        while (await r.ReadAsync(ct))
        {
            if (++lignes > 1)
                throw new InvalidOperationException(
                    "GREFFE_PARAMETRE porte plus d'une ligne dans cette base V2 : reprise arretee, rien n'est copie (a trancher par le referent Socle).");
            for (var i = 0; i < r.FieldCount; i++)
                resultat[r.GetName(i)] = (r.IsDBNull(i) ? null : r.GetValue(i), TypeSql(schema[i]));
        }
        return resultat;
    }

    private static async Task ComparerLignesAsync(SqlConnection v2, SqlConnection v3, string table, string source, string cible,
                                                  (string V2, string V3)[] colonnes, List<EcartReprise> ecarts, CancellationToken ct)
    {
        var lignesV2 = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        await using (var r = await LectureV2.LireAsync(v2, $"SELECT {string.Join(", ", colonnes.Select(c => c.V2))} FROM {source}", ct))
            while (await r.ReadAsync(ct)) lignesV2[r.GetString(0)] = Valeurs(r);
        var lignesV3 = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand($"SELECT {string.Join(", ", colonnes.Select(c => c.V3))} FROM {cible};", v3))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) lignesV3[r.GetString(0)] = Valeurs(r);

        foreach (var (cle, a) in lignesV2)
        {
            if (!lignesV3.TryGetValue(cle, out var b))
            {
                ecarts.Add(new(table, cle, "(ligne)", "presente", "(absente en V3)"));
                continue;
            }
            for (var i = 1; i < colonnes.Length; i++)
                if (!MemeValeur(a[i], b[i]))
                    ecarts.Add(new(table, cle, colonnes[i].V2, Texte(a[i]), Texte(b[i])));
        }
        foreach (var cle in lignesV3.Keys.Where(k => !lignesV2.ContainsKey(k)))
            ecarts.Add(new(table, cle, "(ligne)", "(absente en V2)", "presente"));
    }

    private static object?[] Valeurs(DbDataReader r)
    {
        var v = new object?[r.FieldCount];
        for (var i = 0; i < v.Length; i++) v[i] = r.IsDBNull(i) ? null : r.GetValue(i);
        return v;
    }

    /// <summary>Deux valeurs SQL egales ? Texte au caractere pres ; nombres a leur valeur
    /// (money 1.52 = numeric 1.5200) ; dates exactes ; octets un a un.</summary>
    public static bool MemeValeur(object? a, object? b)
    {
        if (a is null || a is DBNull) return b is null || b is DBNull;
        if (b is null || b is DBNull) return false;
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
        if (a is byte[] ba && b is byte[] bb) return ba.AsSpan().SequenceEqual(bb);
        if (EstNombre(a) && EstNombre(b)) return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
        return a.Equals(b);
    }

    private static bool EstNombre(object o) => o is byte or short or int or long or decimal;

    /// <summary>Une valeur lisible dans un rapport.</summary>
    public static string? Texte(object? v) => v switch
    {
        null or DBNull => null,
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
        byte[] o => Convert.ToHexString(o.Length > 64 ? o[..64] : o),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() is { Length: > 200 } s ? s[..200] + "…" : v.ToString(),
    };

    /// <summary>Le type SQL d'une colonne lue, en clair : « varchar(8) », « money », « varchar(max) ».</summary>
    public static string TypeSql(DbColumn c)
    {
        var nom = (c.DataTypeName ?? "?").ToLowerInvariant();
        return nom switch
        {
            "varchar" or "nvarchar" or "char" or "nchar" or "varbinary" or "binary" =>
                // (max) : SqlClient donne une taille de -1 ou de 2^31-1 selon le type.
                $"{nom}({(c.ColumnSize is null or <= 0 or >= int.MaxValue / 2 ? "max" : c.ColumnSize.Value.ToString(CultureInfo.InvariantCulture))})",
            "decimal" or "numeric" => $"{nom}({c.NumericPrecision},{c.NumericScale})",
            _ => nom,
        };
    }

    private static async Task ExecuterAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> CompterAsync(SqlConnection cn, SqlTransaction tx, string table, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {table};", cn, tx);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
