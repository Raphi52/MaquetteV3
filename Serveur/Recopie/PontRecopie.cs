// fix-ok: conflit de fusion du lot 11 avec main (lot 19, dba63f3) tranche en gardant les deux ajouts : une table en remontee
// seule (REC-010) recoit la garde des affaires, les autres la file et les declencheurs de leur partie jointe (SOC-002 T05).
using System.Text;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>Le compte rendu de la mise en service de la recopie continue d'une base.</summary>
/// <param name="Faite">La recopie tourne pour cette base (deja en service compris).</param>
/// <param name="Refus">Ce qui l'empeche, avant toute ecriture ; vide si <paramref name="Faite"/>.</param>
/// <param name="Declencheurs">Les declencheurs de file poses sur les tables V3.</param>
/// <param name="Version">Le point de depart de la remontee : la version du suivi des changements lue par le transfert.</param>
public sealed record MiseEnServiceRecopie(bool Faite, IReadOnlyList<string> Refus, IReadOnlyList<string> Declencheurs, long? Version, int? IdTransfert);

/// <summary>
/// LA MISE EN SERVICE DE LA RECOPIE CONTINUE D'UN GREFFE, cote V3 (REC-002 T01, REC-003 T02), sur le modele du pont de
/// numerotation (Base\v2\pont-v3.sql) : des declencheurs sur les tables de la V3, la base V2 nommee sur la meme instance
/// (Q4), une marque de session pour qu'une ecriture de la recopie ne soit pas renvoyee.
///
/// A la difference du pont de numerotation, la V3 n'ecrit PAS la V2 dans sa transaction : la V2 indisponible ne doit pas
/// bloquer la V3 (REC-002 CA06). Le declencheur de chaque table partagee ecrit une ligne dans rec.FileRecopie, dans la
/// transaction du changement ; l'applicateur la recopie ensuite, dans l'ordre (Applicateur.cs).
///
/// Ordre de la mise en service, pour une base deja transferee (REC-001) :
///   1. cote V2, AVANT le transfert : le suivi des changements de SQL Server sur la base et ses tables partagees
///      (Base\v2\recopie-v2.sql, REC-003 T01) -- l'instantane du transfert lit alors la version de depart ;
///   2. le transfert (RigV3.Transfert transferer) ;
///   3. ici : les declencheurs de file sur les tables V3, puis le point de depart de la remontee (rec.Remontee) ;
///   4. la recopie continue tourne (RecopieContinue, ou ServiceRecopie.PasserAsync).
/// Rejouer la mise en service est sans risque : les declencheurs sont reecrits, le point de reprise n'est pas touche.
/// </summary>
public static class PontRecopie
{
    /// <summary>La cle de SESSION_CONTEXT qui marque une ecriture de la remontee dans la V3 : le declencheur de file ne
    /// la met pas en file (REC-002 CA05, REC-003 CA05) -- la meme idee que « rigv3.recopie » du pont de numerotation.</summary>
    public const string CleRemontee = "rigv3.remontee";

    /// <summary>La cle de SESSION_CONTEXT ou le serveur pose l'agent de la V3 qui ecrit (auteur de la ligne de file).</summary>
    public const string CleAgent = "rigv3.agent";

    /// <summary>Le contexte du suivi des changements que porte chaque ecriture de la recopie dans la V2 (WITH
    /// CHANGE_TRACKING_CONTEXT) : une trace, pas la preuve. Mesure le 2026-11-21 : un declencheur de la V2 qui reecrit la
    /// ligne dans la meme instruction donne une version SANS ce contexte ; la remontee reconnait donc une ecriture de la
    /// recopie a l'etat de la ligne apres son passage (rec.EtatLigne), et pas seulement a cette marque.</summary>
    public static readonly byte[] MarqueRecopie = "RIGV3-RECOPIE"u8.ToArray();

    public static string NomDeclencheur(TableRecopiee t) => $"TR_{t.V3.NomSeul}_FileRecopie";

    public static string NomDeclencheur(TableJointe j) => $"TR_{j.TableV3.Split('.')[^1]}_FileRecopie";

    /// <summary>
    /// Le declencheur d'une table de la partie jointe d'une fiche (soc.CompteRole, soc.CompteFonction : les droits d'un compte,
    /// SOC-002 T05) : un ajout, un retrait ou un changement REEL y met LA FICHE en file -- genre « M », colonnes « (droits) »,
    /// la ligne de la fiche telle qu'elle est (l'etat commun tant qu'elle n'a pas ete recopiee, REC-004), l'agent de la V3 --,
    /// dans la transaction du changement. L'applicateur ecrit alors la partie jointe avec la fiche (Applicateur.cs, etape 4).
    /// </summary>
    internal static string DeclencheurJoint(TableRecopiee t, TableJointe j)
    {
        var parties = j.TableV3.Split('.');
        var table = $"{Sql.Q(parties[0])}.{Sql.Q(parties[1])}";
        var f = Sql.Q(j.ColonneFiche);
        var cols = t.ColonnesDeclencheur.ToList();
        var fiche = cols.Count == 0 ? "NULL"
            : $"(SELECT {string.Join(", ", cols.Select(c => $"d.{Sql.Q(t.ColonneV3(c).Nom)} AS {Sql.Q(c.Nom)}"))} FROM {t.NomV3Sql} AS d " +
              $"WHERE d.{Sql.Q(t.CleV3.Nom)} = x.{f} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)";
        return $"""
            CREATE OR ALTER TRIGGER {Sql.Q(parties[0])}.{Sql.Q(NomDeclencheur(j))} ON {table}
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                -- Pose par Serveur\Recopie\PontRecopie.cs (SOC-002 T05) : un changement de {j.TableV3} met la fiche {t.NomV3} en file.
                SET NOCOUNT ON;
                IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;
                IF ISNULL(CONVERT(int, SESSION_CONTEXT(N'{CleRemontee}')), 0) = 1 RETURN;
                INSERT INTO rec.FileRecopie (TableV3, IdV3, Genre, Colonnes, Avant, Auteur)
                SELECT {Sql.Texte(t.NomV3)}, CONVERT(nvarchar(200), x.{f}), 'M', {Sql.Texte(t.Regle.MarqueJointe!)}, {fiche},
                       ISNULL(CONVERT(nvarchar(128), SESSION_CONTEXT(N'{CleAgent}')), ORIGINAL_LOGIN())
                FROM (SELECT {f} FROM inserted UNION SELECT {f} FROM deleted) AS x
                WHERE EXISTS (SELECT * FROM inserted AS i WHERE i.{f} = x.{f} EXCEPT SELECT * FROM deleted AS d WHERE d.{f} = x.{f})
                   OR EXISTS (SELECT * FROM deleted AS d WHERE d.{f} = x.{f} EXCEPT SELECT * FROM inserted AS i WHERE i.{f} = x.{f});
            END;
            """;
    }

    public static async Task<MiseEnServiceRecopie> MettreEnServiceAsync(string connexionV3, string baseV2, FichesRecopiees? fiches = null,
                                                                         CancellationToken ct = default)
    {
        fiches ??= await FichesRecopiees.ChargerAsync(connexionV3, baseV2, null, ct);
        var refus = new List<string>(fiches.Problemes);
        await using var cn = new SqlConnection(connexionV3);
        await cn.OpenAsync(ct);

        int? idTransfert = null;
        long? version = null;
        await using (var cmd = new SqlCommand(
            "SELECT TOP (1) Id, VersionSuivi FROM rec.Transfert WHERE BaseV2 = @b AND Etat = 'fini' ORDER BY Id DESC;", cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            if (await l.ReadAsync(ct)) { idTransfert = l.GetInt32(0); version = l.IsDBNull(1) ? null : l.GetInt64(1); }
        }
        if (idTransfert is null) refus.Add($"la base {baseV2} n'a pas de transfert fini dans cette base V3 (REC-001 d'abord)");
        else if (version is null)
            refus.Add($"le transfert {idTransfert} n'a pas lu de version du suivi des changements : le suivi n'etait pas active sur {baseV2} " +
                      "avant le transfert (Base\\v2\\recopie-v2.sql) ; la remontee ne saurait pas d'ou repartir");

        // Le suivi des changements de la base V2 et de chaque table partagee, et sa retention : ce que la remontee doit
        // lire depuis le transfert doit encore y etre (REC-003 CA04).
        var q = Sql.Q(baseV2);
        await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.change_tracking_databases WHERE database_id = DB_ID(@b);", cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            if ((int)(await cmd.ExecuteScalarAsync(ct))! == 0)
                refus.Add($"le suivi des changements n'est pas active sur la base {baseV2} (Base\\v2\\recopie-v2.sql)");
        }
        var suivies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand(
            $"SELECT t.name FROM {q}.sys.change_tracking_tables AS c JOIN {q}.sys.tables AS t ON t.object_id = c.object_id;", cn))
        await using (var l = await cmd.ExecuteReaderAsync(ct))
            while (await l.ReadAsync(ct)) suivies.Add(l.GetString(0));
        foreach (var t in fiches.Tables.Where(t => !suivies.Contains(t.V2.NomSeul)))
            refus.Add($"{t.NomV2} : le suivi des changements n'est pas active sur la table de la V2 (Base\\v2\\recopie-v2.sql)");
        if (version is { } v && refus.Count == 0)
            foreach (var t in fiches.Tables)
            {
                var min = await VersionMinimaleAsync(cn, null, baseV2, t, ct);
                if (min is null || min > v)
                    refus.Add($"{t.NomV2} : le suivi des changements ne remonte plus a la version {v} du transfert (minimum {min?.ToString() ?? "inconnu"}) : " +
                              "des changements de la V2 sont perdus, la base est a comparer (REC-006) et a retransferer");
            }
        // Chaque fiche dans la table V3 ou le transfert l'a ecrite (son rapport, rec.TransfertTable) : une regle qui viserait
        // une autre table recopierait une table vide pendant que la vraie fiche reste figee (REC-012 a REC-019 : v2.CLIENT
        // d'une base transferee avant le lot 10, rec.Client apres).
        if (idTransfert is not null)
        {
            var ecrites = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = new SqlCommand("SELECT TableV2, TableV3 FROM rec.TransfertTable WHERE IdTransfert = @id;", cn))
            {
                cmd.Parameters.AddWithValue("@id", idTransfert.Value);
                await using var l = await cmd.ExecuteReaderAsync(ct);
                while (await l.ReadAsync(ct)) ecrites[l.GetString(0)] = l.IsDBNull(1) ? null : l.GetString(1);
            }
            foreach (var t in fiches.Tables)
                if (!ecrites.TryGetValue(t.NomV2, out var ecrite))
                    refus.Add($"{t.NomV2} : le transfert {idTransfert} ne l'a pas portee dans la V3 (pas de ligne a son rapport)");
                else if (!string.Equals(ecrite, t.NomV3, StringComparison.OrdinalIgnoreCase))
                    refus.Add($"{t.NomV2} : le transfert l'a ecrite dans {ecrite ?? "aucune table"}, sa regle de recopie vise {t.NomV3}");
        }
        if (refus.Count > 0) return new MiseEnServiceRecopie(false, refus, [], version, idTransfert);

        var poses = new List<string>();
        foreach (var t in fiches.Tables)
        {
            // Une table qui ne fait que remonter (les affaires et leurs demandes, REC-010) n'a pas de file vers la V2 : son
            // declencheur est la garde des anciennes affaires, avec les index qu'il lit (GardeAffaires.cs).
            if (t.Regle.RemonteeSeule)
            {
                foreach (var lot in GardeAffaires.Lots(t, baseV2))
                {
                    await using var garde = new SqlCommand(lot, cn) { CommandTimeout = 3600 };
                    await garde.ExecuteNonQueryAsync(ct);
                }
                poses.Add($"{t.V3.Schema}.{GardeAffaires.NomDeclencheur(t)}");
                continue;
            }
            await using var cmd = new SqlCommand(Declencheur(t), cn);
            await cmd.ExecuteNonQueryAsync(ct);
            poses.Add($"{t.V3.Schema}.{NomDeclencheur(t)}");
            // La partie jointe d'une fiche (les droits d'un compte, SOC-002 T05) : un changement de ses tables met la FICHE en file.
            foreach (var j in t.Regle.Jointe?.Tables ?? [])
            {
                await using var joint = new SqlCommand(DeclencheurJoint(t, j), cn);
                await joint.ExecuteNonQueryAsync(ct);
                poses.Add($"{j.TableV3.Split('.')[0]}.{NomDeclencheur(j)}");
            }
        }
        // Les fiches de cette mise en service, et elles seules, sont celles que le serveur recopie pour cette base
        // (FichesRecopiees.ReglesEnServiceAsync).
        await using (var cmd = new SqlCommand("DELETE FROM rec.FicheEnService WHERE BaseV2 = @b;" + string.Concat(fiches.Tables.Select((t, i) =>
            $" INSERT INTO rec.FicheEnService (BaseV2, TableV2, TableV3) VALUES (@b, @t2_{i}, @t3_{i});")), cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            for (var i = 0; i < fiches.Tables.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@t2_{i}", fiches.Tables[i].NomV2);
                cmd.Parameters.AddWithValue($"@t3_{i}", fiches.Tables[i].NomV3);
            }
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM rec.Remontee WHERE BaseV2 = @b)
                INSERT INTO rec.Remontee (BaseV2, Version, IdTransfert) VALUES (@b, @v, @id);
            """, cn))
        {
            cmd.Parameters.AddWithValue("@b", baseV2);
            cmd.Parameters.AddWithValue("@v", version!.Value);
            cmd.Parameters.AddWithValue("@id", idTransfert!.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return new MiseEnServiceRecopie(true, [], poses, version, idTransfert);
    }

    /// <summary>CHANGE_TRACKING_MIN_VALID_VERSION de la table V2, lue dans le contexte de sa base ; null si non suivie.</summary>
    internal static async Task<long?> VersionMinimaleAsync(SqlConnection cn, SqlTransaction? tx, string baseV2, TableRecopiee t, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"EXEC {Sql.Q(baseV2)}.sys.sp_executesql N'SELECT @m = CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(@t));', " +
            "N'@t nvarchar(300), @m bigint OUTPUT', @t = @table, @m = @min OUTPUT;", cn, tx);
        cmd.Parameters.AddWithValue("@table", t.V2.NomSql);
        var min = cmd.Parameters.Add(new SqlParameter("@min", System.Data.SqlDbType.BigInt) { Direction = System.Data.ParameterDirection.Output });
        await cmd.ExecuteNonQueryAsync(ct);
        return min.Value is DBNull ? null : (long)min.Value;
    }

    /// <summary>CHANGE_TRACKING_CURRENT_VERSION de la base V2, lue dans son contexte.</summary>
    internal static async Task<long?> VersionCouranteAsync(SqlConnection cn, string baseV2, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            $"EXEC {Sql.Q(baseV2)}.sys.sp_executesql N'SELECT @v = CHANGE_TRACKING_CURRENT_VERSION();', N'@v bigint OUTPUT', @v = @version OUTPUT;", cn);
        var v = cmd.Parameters.Add(new SqlParameter("@version", System.Data.SqlDbType.BigInt) { Direction = System.Data.ParameterDirection.Output });
        await cmd.ExecuteNonQueryAsync(ct);
        return v.Value is DBNull ? null : (long)v.Value;
    }

    /// <summary>
    /// Le declencheur de file d'une table V3 (REC-002 T01) : chaque creation, modification REELLE (une colonne recopiee qui
    /// change) ou suppression ecrit sa ligne dans rec.FileRecopie, dans la transaction du changement -- une transaction
    /// annulee n'en laisse aucune. La ligne d'avant (JSON) est l'etat commun des deux bases tant que la fiche n'a pas
    /// ete recopiee (REC-004). Une ecriture de la remontee (SESSION_CONTEXT rigv3.remontee) n'est pas mise en file.
    /// </summary>
    internal static string Declencheur(TableRecopiee t)
    {
        var k = Sql.Q(t.CleV3.Nom);
        var cols = t.ColonnesDeclencheur.ToList();
        // La colonne V3 d'une colonne recopiee ; un texte compare au classement binaire : sous le classement de la base
        // (insensible a la casse), « Dupont » -> « DUPONT » n'etait pas un changement et n'etait jamais recopie
        // (mesure par RecopieFichesCheck, REC-012 CA02, sur la denomination).
        string V3(string alias, ColonneV2 c) => $"{alias}.{Sql.Q(t.ColonneV3(c).Nom)}" + (t.ColonneV3(c).PorteClassement ? " COLLATE Latin1_General_BIN2" : "");
        string Changees()
        {
            if (cols.Count == 0) return "NULL";
            // CONCAT_WS prend 254 arguments au plus : par paquets, puis les paquets ensemble. Les noms sont ceux de la V2.
            var paquets = cols.Chunk(200).Select(p => "CONCAT_WS(N','" + string.Concat(p.Select(c =>
                $", CASE WHEN NOT EXISTS (SELECT {V3("i", c)} INTERSECT SELECT {V3("d", c)}) THEN {Sql.Texte(c.Nom)} END")) + ")").ToList();
            var tout = paquets.Count == 1 ? paquets[0] : "CONCAT_WS(N','," + string.Join(", ", paquets.Select(p => $"NULLIF({p}, N'')")) + ")";
            return $"NULLIF({tout}, N'')";
        }
        // La ligne d'avant, sous les noms de la V2 : c'est l'etat commun quand la fiche n'a jamais ete recopiee (REC-004).
        var avant = cols.Count == 0 ? "NULL"
            : $"(SELECT {string.Join(", ", cols.Select(c => $"d.{Sql.Q(t.ColonneV3(c).Nom)} AS {Sql.Q(c.Nom)}"))} FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES)";
        var change = cols.Count == 0 ? "1 = 0"
            : $"EXISTS (SELECT {string.Join(", ", cols.Select(c => V3("i", c)))} EXCEPT SELECT {string.Join(", ", cols.Select(c => V3("d", c)))})";
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE OR ALTER TRIGGER {Sql.Q(t.V3.Schema)}.{Sql.Q(NomDeclencheur(t))} ON {t.NomV3Sql}");
        sb.AppendLine("AFTER INSERT, UPDATE, DELETE");
        sb.AppendLine("AS");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    -- Pose par Serveur\\Recopie\\PontRecopie.cs (REC-002 T01) : la file de recopie V3 -> V2, ecrite dans la transaction du changement.");
        sb.AppendLine("    SET NOCOUNT ON;");
        sb.AppendLine("    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;");
        sb.AppendLine($"    IF ISNULL(CONVERT(int, SESSION_CONTEXT(N'{CleRemontee}')), 0) = 1 RETURN;   -- une ecriture de la remontee (REC-003) n'est pas renvoyee");
        sb.AppendLine("    INSERT INTO rec.FileRecopie (TableV3, IdV3, Genre, Colonnes, Avant, Auteur)");
        sb.AppendLine($"    SELECT {Sql.Texte(t.NomV3)}, COALESCE({Sql.EnTexte("i", t.CleV3)}, {Sql.EnTexte("d", t.CleV3)}),");
        sb.AppendLine($"           CASE WHEN d.{k} IS NULL THEN 'C' WHEN i.{k} IS NULL THEN 'S' ELSE 'M' END,");
        sb.AppendLine($"           CASE WHEN i.{k} IS NOT NULL AND d.{k} IS NOT NULL THEN {Changees()} END,");
        sb.AppendLine($"           CASE WHEN d.{k} IS NOT NULL THEN {avant} END,");
        sb.AppendLine($"           ISNULL(CONVERT(nvarchar(128), SESSION_CONTEXT(N'{CleAgent}')), ORIGINAL_LOGIN())");
        sb.AppendLine($"    FROM inserted AS i FULL JOIN deleted AS d ON d.{k} = i.{k}");
        sb.AppendLine($"    WHERE i.{k} IS NULL OR d.{k} IS NULL OR {change};");
        sb.AppendLine("END;");
        return sb.ToString();
    }
}
