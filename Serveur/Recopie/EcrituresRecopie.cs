using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>Une fiche attend une autre fiche qui n'est pas encore passee : la ligne reste en file, rien n'est ecrit.</summary>
public sealed class RecopieEnAttente(string message) : Exception(message);

/// <summary>
/// Les gestes elementaires de la recopie, dans les deux sens, sur UNE connexion a la base V3 : la base V2 est nommee sur
/// la meme instance (Q4), si bien qu'une fiche recopiee, sa correspondance, son etat commun et sa ligne de file tiennent
/// dans UNE transaction -- une panne au milieu n'en laisse rien.
/// </summary>
internal static class EcrituresRecopie
{
    // --- lire -------------------------------------------------------------------------------------------------

    public static async Task<LigneJson?> LireV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV2, bool verrou, CancellationToken ct)
        => LigneJson.Lire(await ScalaireAsync(cn, tx, t.SelectJson(t.NomV2Sql, t.Colonnes, "@id", verrou ? "WITH (UPDLOCK, HOLDLOCK)" : null), ct, ("@id", idV2)));

    public static async Task<LigneJson?> LireV3Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, bool verrou, CancellationToken ct,
                                                     bool avecPropres = false)
        => LigneJson.Lire(await ScalaireAsync(cn, tx, t.SelectJson(t.NomV3Sql, avecPropres ? t.Colonnes.Concat(t.Propres) : t.Colonnes, "@id",
                                                                    verrou ? "WITH (UPDLOCK, HOLDLOCK)" : null), ct, ("@id", idV3)));

    public static async Task<bool> ExisteV3Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, CancellationToken ct)
        => await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(1), 1) FROM {t.NomV3Sql} AS x WHERE {t.CleEgale("x", "@id")};", ct, ("@id", idV3)) is not null;

    public static async Task<bool> ExisteV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV2, CancellationToken ct)
        => await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(1), 1) FROM {t.NomV2Sql} AS x WHERE {t.CleEgale("x", "@id")};", ct, ("@id", idV2)) is not null;

    // --- la correspondance (rec.Correspondance, REC-001 T01) ----------------------------------------------------

    public static Task<string?> IdV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, CancellationToken ct)
        => ScalaireAsync(cn, tx, "SELECT TOP (1) IdV2 FROM rec.Correspondance WHERE TableV3 = @t3 AND IdV3 = @id AND BaseV2 = @b AND TableV2 = @t2;", ct,
                         ("@t3", t.NomV3), ("@id", idV3), ("@b", t.BaseV2), ("@t2", t.NomV2));

    public static Task<string?> IdV3Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV2, CancellationToken ct)
        => ScalaireAsync(cn, tx, "SELECT IdV3 FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id AND TableV3 = @t3;", ct,
                         ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id", idV2), ("@t3", t.NomV3));

    public static Task CorrespondreAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, string idV3, int idTransfert, string origine,
                                         CancellationToken ct)
        => ExecuterAsync(cn, tx, """
            DELETE FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id2;
            INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert, Origine) VALUES (@b, @t2, @id2, @t3, @id3, @tr, @o);
            """, ct, ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id2", idV2), ("@t3", t.NomV3), ("@id3", idV3), ("@tr", idTransfert), ("@o", origine));

    public static Task OublierAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, CancellationToken ct)
        => ExecuterAsync(cn, tx, """
            DELETE FROM rec.Correspondance WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id2;
            DELETE FROM rec.EtatLigne WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id2;
            """, ct, ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id2", idV2));

    /// <summary>Traduit les liens d'une ligne vers les identifiants de l'autre base. Un lien sans correspondance garde sa
    /// valeur (lien casse herite, ou fiche visee hors de la recopie) : l'appelant a deja fait passer avant elle toute
    /// fiche visee qui attendait (REC-002 CA03, REC-003 CA03).</summary>
    public static async Task<LigneJson> TraduireAsync(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, LigneJson ligne, bool versV2, CancellationToken ct)
    {
        var r = ligne.Copie();
        foreach (var (colonne, visee) in t.Liens)
        {
            if (!r.TryGetValue(colonne, out var brut) || TableRecopiee.TexteDe(brut) is not { } valeur) continue;
            var autre = versV2 ? await IdV2Async(cn, tx, visee, valeur, ct) : await IdV3Async(cn, tx, visee, valeur, ct);
            if (autre is not null) r[colonne] = TableRecopiee.Brut(autre, t.V2.Colonne(colonne));
        }
        return r;
    }

    // --- l'etat commun (rec.EtatLigne) ---------------------------------------------------------------------------

    public static async Task<LigneJson?> EtatAsync(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV2, CancellationToken ct)
        => LigneJson.Lire(await ScalaireAsync(cn, tx, "SELECT Ligne FROM rec.EtatLigne WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id;", ct,
                                              ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id", idV2)));

    public static Task NoterEtatAsync(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, LigneJson ligneV2, string sens, CancellationToken ct)
        => ExecuterAsync(cn, tx, """
            UPDATE rec.EtatLigne SET Ligne = @l, Le = SYSDATETIME(), Sens = @s WHERE BaseV2 = @b AND TableV2 = @t2 AND IdV2 = @id;
            IF @@ROWCOUNT = 0 INSERT INTO rec.EtatLigne (BaseV2, TableV2, IdV2, Ligne, Sens) VALUES (@b, @t2, @id, @l, @s);
            """, ct, ("@b", t.BaseV2), ("@t2", t.NomV2), ("@id", idV2), ("@l", ligneV2.Ecrire()), ("@s", sens));

    // --- ecrire dans la V2 (REC-002) ------------------------------------------------------------------------------

    /// <summary>Met a jour les colonnes <paramref name="colonnes"/> de la fiche V2 avec les valeurs de <paramref name="ligne"/>
    /// (identifiants de la V2), sous la marque de la recopie ; puis remet les colonnes propres (etat de transmission) a
    /// leur valeur d'avant si un declencheur de la V2 les a changees (REC-002 CA04).</summary>
    public static Task ModifierV2Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, LigneJson ligne, IReadOnlyList<string> colonnes,
                                       CancellationToken ct)
    {
        var cols = t.Colonnes.Where(c => colonnes.Contains(c.Nom, StringComparer.OrdinalIgnoreCase)).ToList();
        var sql = new System.Text.StringBuilder();
        if (t.Propres.Count > 0)
        {
            sql.AppendLine($"DECLARE @avant TABLE ({string.Join(", ", t.Propres.Select(p => $"{Sql.Q(p.Nom)} {p.TypeSql}"))});");
            sql.AppendLine($"INSERT INTO @avant SELECT {string.Join(", ", t.Propres.Select(p => $"x.{Sql.Q(p.Nom)}"))} FROM {t.NomV2Sql} AS x WHERE {t.CleEgale("x", "@id")};");
        }
        sql.AppendLine($"WITH CHANGE_TRACKING_CONTEXT (@marque) UPDATE x SET {string.Join(", ", cols.Select(c => $"x.{Sql.Q(c.Nom)} = j.{Sql.Q(c.Nom)}"))} " +
                       $"FROM {t.NomV2Sql} AS x CROSS JOIN OPENJSON(@json) {TableRecopiee.OpenJson(cols)} AS j WHERE {t.CleEgale("x", "@id")};");
        if (t.Propres.Count > 0)
            sql.AppendLine($"WITH CHANGE_TRACKING_CONTEXT (@marque) UPDATE x SET {string.Join(", ", t.Propres.Select(p => $"x.{Sql.Q(p.Nom)} = a.{Sql.Q(p.Nom)}"))} " +
                           $"FROM {t.NomV2Sql} AS x CROSS JOIN @avant AS a WHERE {t.CleEgale("x", "@id")} AND EXISTS (SELECT " +
                           $"{string.Join(", ", t.Propres.Select(p => $"x.{Sql.Q(p.Nom)}"))} EXCEPT SELECT {string.Join(", ", t.Propres.Select(p => $"a.{Sql.Q(p.Nom)}"))});");
        return ExecuterAsync(cn, tx, sql.ToString(), ct, ("@id", idV2), ("@json", ligne.Ecrire()), ("@marque", PontRecopie.MarqueRecopie));
    }

    /// <summary>Cree la fiche dans la V2 ; la V2 lui donne son identifiant quand sa cle est une identite (REC-002 CA02).
    /// Une colonne propre recoit la valeur de creation de la regle (l'etat « N » qui bloque la transmission), sinon celle
    /// de la V3.</summary>
    public static async Task<string> CreerV2Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, LigneJson ligneAvecPropres, CancellationToken ct)
    {
        var lues = t.Colonnes.Concat(t.Propres).ToList();
        var cibles = lues.Select(c => Sql.Q(c.Nom)).ToList();
        var valeurs = lues.Select(c => t.Regle.CreationV2.TryGetValue(c.Nom, out var e) ? e : $"j.{Sql.Q(c.Nom)}").ToList();
        if (!t.CleIdentiteV2) { cibles.Add(Sql.Q(t.Cle.Nom)); valeurs.Add($"CONVERT({t.Cle.TypeSql}, @id3)"); }
        var sql = $"""
            DECLARE @ids TABLE (Id {t.Cle.TypeSql});
            WITH CHANGE_TRACKING_CONTEXT (@marque) INSERT INTO {t.NomV2Sql} ({string.Join(", ", cibles)}) OUTPUT inserted.{Sql.Q(t.Cle.Nom)} INTO @ids
            SELECT {string.Join(", ", valeurs)} FROM OPENJSON(@json) {TableRecopiee.OpenJson(lues)} AS j;
            SELECT {Sql.EnTexte("x", t.Cle with { Nom = "Id" })} FROM @ids AS x;
            """;
        return await ScalaireAsync(cn, tx, sql, ct, ("@id3", idV3), ("@json", ligneAvecPropres.Ecrire()), ("@marque", PontRecopie.MarqueRecopie))
               ?? throw new InvalidOperationException($"{t.NomV2} : la V2 n'a rendu aucun identifiant pour la fiche V3 {idV3}");
    }

    public static Task SupprimerV2Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, CancellationToken ct)
        => ExecuterAsync(cn, tx, $"WITH CHANGE_TRACKING_CONTEXT (@marque) DELETE x FROM {t.NomV2Sql} AS x WHERE {t.CleEgale("x", "@id")};", ct,
                         ("@id", idV2), ("@marque", PontRecopie.MarqueRecopie));

    // --- ecrire dans la V3 (REC-003), sous la marque de la remontee : le declencheur de file ne la renvoie pas -------

    public static Task ModifierV3Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, LigneJson ligne, IReadOnlyList<string> colonnes,
                                       CancellationToken ct)
    {
        var cols = t.Colonnes.Where(c => colonnes.Contains(c.Nom, StringComparer.OrdinalIgnoreCase)).ToList();
        return SousMarqueAsync(cn, tx, $"UPDATE x SET {string.Join(", ", cols.Select(c => $"x.{Sql.Q(c.Nom)} = j.{Sql.Q(c.Nom)}"))} " +
                                       $"FROM {t.NomV3Sql} AS x CROSS JOIN OPENJSON(@json) {TableRecopiee.OpenJson(cols)} AS j WHERE {t.CleEgale("x", "@id")};",
                               ct, ("@id", idV3), ("@json", ligne.Ecrire()));
    }

    /// <summary>Cree dans la V3 une fiche venue de la V2 ; la V3 lui donne son identifiant quand sa cle est une identite
    /// (REC-003 CA03). Les colonnes propres recoivent la valeur de la V2.</summary>
    public static async Task<string> CreerV3Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, LigneJson ligneAvecPropres, CancellationToken ct)
    {
        var lues = t.Colonnes.Concat(t.Propres).ToList();
        var cibles = lues.Select(c => Sql.Q(c.Nom)).ToList();
        var valeurs = lues.Select(c => $"j.{Sql.Q(c.Nom)}").ToList();
        if (!t.CleIdentiteV3) { cibles.Add(Sql.Q(t.Cle.Nom)); valeurs.Add($"CONVERT({t.Cle.TypeSql}, @id2)"); }
        string? id = null;
        await SousMarqueAsync(cn, tx, $"""
            DECLARE @ids TABLE (Id {t.Cle.TypeSql});
            INSERT INTO {t.NomV3Sql} ({string.Join(", ", cibles)}) OUTPUT inserted.{Sql.Q(t.Cle.Nom)} INTO @ids
            SELECT {string.Join(", ", valeurs)} FROM OPENJSON(@json) {TableRecopiee.OpenJson(lues)} AS j;
            SELECT {Sql.EnTexte("x", t.Cle with { Nom = "Id" })} FROM @ids AS x;
            """, ct, v => id = v, ("@id2", idV2), ("@json", ligneAvecPropres.Ecrire()));
        return id ?? throw new InvalidOperationException($"{t.NomV3} : la V3 n'a rendu aucun identifiant pour la fiche V2 {idV2}");
    }

    public static Task SupprimerV3Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, CancellationToken ct)
        => SousMarqueAsync(cn, tx, $"DELETE x FROM {t.NomV3Sql} AS x WHERE {t.CleEgale("x", "@id")};", ct, ("@id", idV3));

    private static Task SousMarqueAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct, params (string, object)[] p)
        => SousMarqueAsync(cn, tx, sql, ct, null, p);

    /// <summary>Joue <paramref name="sql"/> marque comme une ecriture de la remontee, et retire TOUJOURS la marque apres
    /// (elle n'est pas transactionnelle : une erreur ne doit pas la laisser sur une connexion rendue au pool).</summary>
    private static async Task SousMarqueAsync(SqlConnection cn, SqlTransaction tx, string sql, CancellationToken ct, Action<string?>? resultat,
                                              params (string, object)[] p)
    {
        await ExecuterAsync(cn, tx, $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = 1;", ct);
        try
        {
            var v = await ScalaireAsync(cn, tx, sql, ct, p);
            resultat?.Invoke(v);
        }
        finally
        {
            // Transaction encore vivante : la marque se retire dedans. Transaction condamnee par l'erreur : l'appelant
            // l'annule puis appelle RetirerMarqueAsync (Applicateur, Remontee) -- l'erreur d'origine, elle, remonte.
            if (cn.State == ConnectionState.Open && tx.Connection is not null)
                try { await ExecuterAsync(cn, tx, RetraitMarque, CancellationToken.None); }
                catch (SqlException) { }
        }
    }

    private static readonly string RetraitMarque = $"EXEC sys.sp_set_session_context @key = N'{PontRecopie.CleRemontee}', @value = NULL;";

    /// <summary>Retire la marque de la remontee de la connexion, hors transaction (apres une annulation).</summary>
    public static async Task RetirerMarqueAsync(SqlConnection cn)
    {
        if (cn.State == ConnectionState.Open) await ExecuterAsync(cn, null, RetraitMarque, CancellationToken.None);
    }

    // --- bas niveau ------------------------------------------------------------------------------------------------

    public static async Task<string?> ScalaireAsync(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct, params (string Nom, object Valeur)[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (n, v) in p) cmd.Parameters.Add(Parametre(n, v));
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : Convert.ToString(r, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task ExecuterAsync(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct, params (string Nom, object Valeur)[] p)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 120 };
        foreach (var (n, v) in p) cmd.Parameters.Add(Parametre(n, v));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter Parametre(string nom, object valeur) => valeur switch
    {
        string s => new SqlParameter(nom, SqlDbType.NVarChar, s.Length <= 4000 ? 4000 : -1) { Value = s },
        byte[] b => new SqlParameter(nom, SqlDbType.VarBinary, 128) { Value = b },
        null => new SqlParameter(nom, DBNull.Value),
        _ => new SqlParameter(nom, valeur),
    };
}
