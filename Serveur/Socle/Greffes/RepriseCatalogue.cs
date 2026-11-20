using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Socle.Greffes;

// ============================================================
// REMPLIR LE CATALOGUE DEPUIS LA FICHE COMMUNE DE LA V2 (SOC-007 T03, SOC-003 T04 ; lot 4).
//
// Source : COMMUN_RIG.dbo.GREFFE, les greffes exploites (GRF_IS_EXPLOIT = 1 : 40 le
// 2026-11-20). Pour chacun :
//  * sa fiche du catalogue (soc.Greffe) : code, nom, serveur et base V2, base V3 deduite
//    (CatalogueGreffes.NomBaseV3, meme serveur), beta (VIDE = non beta, CA02, comme
//    RigGreffe.IsGreffeBeta, RigGreffe.cs:72-76), outre-mer (GRF_IS_DROM), fuseau de
//    reprise (Fuseaux.ParDefaut) ;
//  * « les informations du greffe tenues en commun » (soc.GreffeInfoV2) : CHAQUE colonne
//    de sa ligne, valeur et type gardes, a cote de sa fiche.
// Une fiche deja la garde sa base V3, son fuseau et sa date de passage : la reprise se
// rejoue a chaque repetition sans defaire ce que l'exploitation a saisi.
// ============================================================

public static class RepriseCatalogue
{
    private const string LectureGreffes = "SELECT * FROM dbo.GREFFE WHERE GRF_IS_EXPLOIT = 1 ORDER BY GRF_CODE_GREFFE";

    private sealed record LigneV2(string Code, string Nom, string? Serveur, string? Base, bool EstBeta, bool EstOutreMer,
                                  IReadOnlyList<(string Colonne, object? Valeur, string Type)> Colonnes);

    /// <summary>Remplit (ou met a jour) le catalogue de la base commune V3. Rend le nombre de greffes repris.</summary>
    public static async Task<int> ReprendreAsync(SqlConnection communV2, SqlConnection communeV3, string auteur,
                                                 CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auteur);
        var lignes = await LireAsync(communV2, ct);
        var sansBase = lignes.Where(l => string.IsNullOrWhiteSpace(l.Serveur) || string.IsNullOrWhiteSpace(l.Base)).ToList();
        if (sansBase.Count > 0)
            throw new InvalidOperationException(
                "Greffe(s) exploite(s) sans base ou sans serveur dans COMMUN_RIG.dbo.GREFFE : " +
                string.Join(", ", sansBase.Select(l => l.Code)) + ". Rien n'est repris : la fiche V2 est a completer.");

        await using var tx = (SqlTransaction)await communeV3.BeginTransactionAsync(ct);
        foreach (var l in lignes)
        {
            await using (var cmd = new SqlCommand(
                "IF EXISTS (SELECT 1 FROM soc.Greffe WHERE CodeGreffe = @c) " +
                "  UPDATE soc.Greffe SET Nom = @nom, Serveur = @srv, BaseV2 = @b2, EstBeta = @beta, EstOutreMer = @om, " +
                "         ModifiePar = @a, ModifieLe = SYSDATETIME() WHERE CodeGreffe = @c; " +
                "ELSE " +
                "  INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe) " +
                "  VALUES (@c, @nom, @srv, @b2, @b3, @beta, @om, @fuseau, @a, SYSDATETIME());", communeV3, tx))
            {
                cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = l.Code });
                cmd.Parameters.Add(new SqlParameter("@nom", SqlDbType.VarChar, 80) { Value = l.Nom });
                cmd.Parameters.Add(new SqlParameter("@srv", SqlDbType.VarChar, 128) { Value = l.Serveur });
                cmd.Parameters.Add(new SqlParameter("@b2", SqlDbType.VarChar, 128) { Value = l.Base });
                cmd.Parameters.Add(new SqlParameter("@b3", SqlDbType.VarChar, 128) { Value = CatalogueGreffes.NomBaseV3(l.Base!) });
                cmd.Parameters.Add(new SqlParameter("@beta", SqlDbType.Bit) { Value = l.EstBeta });
                cmd.Parameters.Add(new SqlParameter("@om", SqlDbType.Bit) { Value = l.EstOutreMer });
                cmd.Parameters.Add(new SqlParameter("@fuseau", SqlDbType.VarChar, 64)
                    { Value = (object?)Fuseaux.ParDefaut(l.Code, l.EstOutreMer) ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@a", SqlDbType.VarChar, 128) { Value = auteur });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await using (var del = new SqlCommand("DELETE FROM soc.GreffeInfoV2 WHERE CodeGreffe = @c;", communeV3, tx))
            {
                del.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = l.Code });
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var (colonne, valeur, type) in l.Colonnes)
            {
                // Un sql_variant ne tient ni les (max) ni plus de 8 000 octets : ceux-la vont dans Texte.
                var texteLong = type.EndsWith("(max)", StringComparison.Ordinal) || type is "text" or "ntext";
                await using var ins = new SqlCommand(
                    "INSERT INTO soc.GreffeInfoV2 (CodeGreffe, Colonne, Valeur, Texte, TypeV2, ReprisLe) " +
                    "VALUES (@c, @col, @v, @t, @type, SYSDATETIME());", communeV3, tx);
                ins.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = l.Code });
                ins.Parameters.Add(new SqlParameter("@col", SqlDbType.VarChar, 128) { Value = colonne });
                ins.Parameters.Add(new SqlParameter("@v", SqlDbType.Variant) { Value = texteLong || valeur is null ? DBNull.Value : valeur });
                ins.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, -1) { Value = texteLong && valeur is not null ? Convert.ToString(valeur)! : DBNull.Value });
                ins.Parameters.Add(new SqlParameter("@type", SqlDbType.VarChar, 40) { Value = type });
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        return lignes.Count;
    }

    /// <summary>
    /// Compare la fiche commune V2 et le catalogue V3 (SOC-007 CA01, SOC-003 CA01) : chaque
    /// greffe exploite present, ses marques, et chaque colonne de sa ligne gardee a l'identique.
    /// </summary>
    public static async Task<IReadOnlyList<EcartReprise>> ComparerAsync(SqlConnection communV2, SqlConnection communeV3,
                                                                       CancellationToken ct = default)
    {
        var ecarts = new List<EcartReprise>();
        var lignes = await LireAsync(communV2, ct);
        var codesV2 = lignes.Select(l => l.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var l in lignes)
        {
            var attendu = new Dictionary<string, object?>
            {
                ["Nom"] = l.Nom, ["Serveur"] = l.Serveur, ["BaseV2"] = l.Base,
                ["EstBeta"] = l.EstBeta, ["EstOutreMer"] = l.EstOutreMer,
            };
            await using (var cmd = new SqlCommand(
                "SELECT Nom, Serveur, BaseV2, EstBeta, EstOutreMer FROM soc.Greffe WHERE CodeGreffe = @c;", communeV3))
            {
                cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = l.Code });
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct))
                {
                    ecarts.Add(new("GREFFE", l.Code, "(fiche)", "exploite", "(absente du catalogue)"));
                    continue;
                }
                foreach (var (nom, valeur) in attendu)
                {
                    var v3 = r.GetValue(r.GetOrdinal(nom));
                    if (!RepriseReglages.MemeValeur(valeur, v3))
                        ecarts.Add(new("GREFFE", l.Code, nom, RepriseReglages.Texte(valeur), RepriseReglages.Texte(v3)));
                }
            }

            var repris = new Dictionary<string, (object? Valeur, string Type)>(StringComparer.Ordinal);
            await using (var cmd = new SqlCommand(
                "SELECT Colonne, Valeur, Texte, TypeV2 FROM soc.GreffeInfoV2 WHERE CodeGreffe = @c;", communeV3))
            {
                cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.VarChar, 20) { Value = l.Code });
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    repris[r.GetString(0)] = (!r.IsDBNull(1) ? r.GetValue(1) : r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3));
            }
            foreach (var (colonne, valeur, type) in l.Colonnes)
            {
                if (!repris.TryGetValue(colonne, out var v3))
                    ecarts.Add(new("GREFFE", l.Code, colonne, RepriseReglages.Texte(valeur), "(absente en V3)"));
                else if (!RepriseReglages.MemeValeur(valeur, v3.Valeur) || v3.Type != type)
                    ecarts.Add(new("GREFFE", l.Code, colonne, RepriseReglages.Texte(valeur), RepriseReglages.Texte(v3.Valeur)));
            }
        }
        await using (var cmd = new SqlCommand("SELECT CodeGreffe FROM soc.Greffe;", communeV3))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (!codesV2.Contains(r.GetString(0)))
                    ecarts.Add(new("GREFFE", r.GetString(0), "(fiche)", "(non exploite ou absent en V2)", "au catalogue"));
        return ecarts;
    }

    private static async Task<List<LigneV2>> LireAsync(SqlConnection communV2, CancellationToken ct)
    {
        var lignes = new List<LigneV2>();
        await using var r = await LectureV2.LireAsync(communV2, LectureGreffes, ct);
        var schema = r.GetColumnSchema();
        while (await r.ReadAsync(ct))
        {
            var colonnes = new List<(string, object?, string)>();
            for (var i = 0; i < r.FieldCount; i++)
                colonnes.Add((r.GetName(i), r.IsDBNull(i) ? null : r.GetValue(i), RepriseReglages.TypeSql(schema[i])));
            string? Texte(string nom) => r.IsDBNull(r.GetOrdinal(nom)) ? null : r.GetString(r.GetOrdinal(nom));
            // Marque beta vide = non beta (CA02) ; GRF_IS_DROM est NOT NULL en V2 (GREFFE.cs).
            var beta = r.IsDBNull(r.GetOrdinal("GRF_IS_GREFFE_BETA")) ? false : r.GetBoolean(r.GetOrdinal("GRF_IS_GREFFE_BETA"));
            lignes.Add(new LigneV2(Texte("GRF_CODE_GREFFE")!, Texte("GRF_NOM")!, Texte("GRF_SERVEUR_BD"), Texte("GRF_NOMBASE_BD"),
                beta, r.GetBoolean(r.GetOrdinal("GRF_IS_DROM")), colonnes));
        }
        return lignes;
    }
}
