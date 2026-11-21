using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Serveur.Recopie;

/// <summary>Un ecart constate pendant le transfert : la table en cours est annulee et le transfert s'arrete.</summary>
public sealed class EcartTransfert(string message) : Exception(message);

/// <summary>
/// LA COPIE D'UNE TABLE « TELLE QUELLE » (REC-001 T02, CA03, CA04 ; REC-005 T03) :
///  1. la table v2.NOM, aux colonnes du catalogue REEL de la base (memes types, classements, identite) et a sa cle
///     primaire ; ni ses cles etrangeres (un lien casse herite y est garde tel quel), ni ses declencheurs (le
///     comportement de la V2 se refait dans les stories de domaine) ;
///  2. les liens casses, lus dans l'instantane : chaque cle etrangere de la V2, desactivee ou non, est verifiee ligne a
///     ligne ; la regle de la table dit marquer (ligne ecrite, lien dans rec.LienCasse) ou rejeter (rec.Rejet) ;
///  3. la copie par LOTS depuis l'instantane, valeurs et identifiants gardes : chaque lot (les lignes suivantes dans
///     l'ordre de la cle), sa correspondance (4) et son point de reprise (rec.TransfertLot) sont UNE transaction ;
///     une reprise repart apres le dernier lot enregistre, sans reecrire une ligne (REC-005 CA04) ;
///  4. la correspondance : chaque ligne ecrite, son identifiant V2 et son identifiant V3 (le meme ici) ;
///  puis, dans la transaction de fin de table :
///  5. les lignes dont le lien vise une ligne rejetee par le transfert : marquees « rejetee par le transfert » ;
///  6. le controle de fin : lues = ecrites + rejetees, une correspondance par ligne ecrite, et la meme empreinte
///     (CHECKSUM_AGG de BINARY_CHECKSUM des colonnes) des deux cotes. Un ecart annule la table et arrete tout.
/// </summary>
public static class CopieTelleQuelle
{
    public static string NomV3(TableV2 t) => $"v2.{Sql.Q(t.Nom)}";

    /// <summary>Le nom de la table V3 tel qu'il s'ecrit dans le journal et la correspondance (« v2.AFFAIRE »).</summary>
    public static string NomV3Texte(TableV2 t) => $"v2.{t.Nom}";

    /// <summary>Ce qui empeche de copier la table telle quelle (verifie avant toute ecriture), ou null. Une table sans cle
    /// primaire n'a ni correspondance ni reprise par lot : seule une regle PROVISOIRE (repetition, REC-008) la copie, en une
    /// transaction (<see cref="CopierSansCleAsync"/>) ; sa story dira comment identifier une ligne.</summary>
    public static string? Empechement(TableV2 t, bool provisoire = false)
    {
        var non = t.Colonnes.Where(c => c.TypeSql is null).Select(c => $"{c.Nom} ({c.TypeNom})").ToList();
        if (non.Count > 0) return $"{t.Nom} a des colonnes d'un type que la copie telle quelle ne porte pas : {string.Join(", ", non)}";
        if (t.ClePrimaire.Count == 0)
            return provisoire ? null : $"{t.Nom} n'a pas de cle primaire : sa regle doit dire comment identifier une ligne (rec.Correspondance)";
        // La lecture par lots (REC-005 T03) repart apres la derniere cle ecrite : chaque colonne de la cle doit se passer
        // en parametre de son type exact.
        var cles = t.ClePrimaire.Select(t.Colonne).Where(c => Sql.Parametre(c, "@k", DBNull.Value) is null).Select(c => $"{c.Nom} ({c.TypeBase})").ToList();
        return cles.Count == 0 ? null : $"{t.Nom} a une cle primaire d'un type que la lecture par lots ne porte pas : {string.Join(", ", cles)}";
    }

    public static string Ddl(TableV2 t)
    {
        var colonnes = t.Colonnes.Select(c =>
            $"{Sql.Q(c.Nom)} {c.TypeSql}" +
            (c.Collation is { } col && c.PorteClassement ? $" COLLATE {col}" : "") +
            (c.Identite ? $" IDENTITY({c.Graine ?? 1}, {c.Pas ?? 1})" : "") +
            (c.AccepteNull ? " NULL" : " NOT NULL"));
        var pk = $"PK_v2_{t.Nom.Replace('.', '_')}";
        if (pk.Length > 128) pk = pk[..128];
        return t.ClePrimaire.Count == 0
            ? $"CREATE TABLE {NomV3(t)} ({string.Join(", ", colonnes)});"
            : $"CREATE TABLE {NomV3(t)} ({string.Join(", ", colonnes)}, CONSTRAINT {Sql.Q(pk)} PRIMARY KEY CLUSTERED " +
              $"({string.Join(", ", t.ClePrimaire.Select(Sql.Q))}));";
    }

    /// <summary>« La ligne c a un lien casse par cette cle » : toutes ses colonnes renseignees, aucune ligne visee.</summary>
    private static string Casse(CleEtrangereV2 k, TableV2 visee)
        => "(" + string.Join(" AND ", k.Colonnes.Select(c => $"c.{Sql.Q(c)} IS NOT NULL")) +
           $" AND NOT EXISTS (SELECT 1 FROM {visee.NomSql} AS p WHERE " +
           string.Join(" AND ", k.Colonnes.Zip(k.ColonnesVisees, (c, v) => $"p.{Sql.Q(v)} = c.{Sql.Q(c)}")) + "))";

    private static string Valeur(TableV2 t, CleEtrangereV2 k) => Sql.Identifiant("c", k.Colonnes.Select(t.Colonne));

    /// <summary>Le point de reprise d'une table : son dernier lot ecrit (rec.TransfertLot), ou null si aucun.</summary>
    public sealed record PointLot(int Lot, long Lignes, string? DerniereCle, long DureeMs, DateTime PremierLe);

    public static async Task<PointLot?> PointAsync(SqlConnection v3, int idTransfert, string tableV2, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(
            "SELECT TOP (1) Lot, Lignes, DerniereCle, (SELECT SUM(DureeMs) FROM rec.TransfertLot WHERE IdTransfert = @id AND TableV2 = @t), " +
            "(SELECT MIN(Le) FROM rec.TransfertLot WHERE IdTransfert = @id AND TableV2 = @t) " +
            "FROM rec.TransfertLot WHERE IdTransfert = @id AND TableV2 = @t ORDER BY Lot DESC;", v3);
        cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idTransfert });
        cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, 128) { Value = tableV2 });
        await using var l = await cmd.ExecuteReaderAsync(ct);
        return await l.ReadAsync(ct)
            ? new PointLot(l.GetInt32(0), l.GetInt64(1), l.IsDBNull(2) ? null : l.GetString(2), l.GetInt64(3), l.GetDateTime(4))
            : null;
    }

    /// <summary>Ce qui se passe apres chaque lot ecrit (et enregistre) : le moteur le dit au journal ; un banc peut y
    /// provoquer l'incident qu'il veut prouver.</summary>
    public delegate Task ApresLot(string table, int lot, long lignes, CancellationToken ct);

    /// <summary>
    /// La copie d'une table PAR LOTS (REC-005 T03) : chaque lot -- les lignes suivantes dans l'ordre de la cle primaire,
    /// leur correspondance et sa ligne rec.TransfertLot -- est UNE transaction. Un arret, brutal ou non, laisse donc la
    /// table a la fin d'un lot enregistre ; la reprise repart apres la derniere cle de ce lot, sans reecrire une seule
    /// ligne, apres avoir verifie que la table V3 dit la meme chose que le point (nombre de lignes, derniere cle) : un
    /// point incoherent est un ECART, le transfert s'arrete au lieu de doubler ou de sauter des lignes. Les liens casses,
    /// les rejets et le controle de fin de table (sur la table entiere) sont la derniere transaction, avec la ligne du
    /// rapport (rec.TransfertTable) : une table est finie si et seulement si elle a sa ligne de rapport.
    /// </summary>
    public static async Task<LigneRapport> CopierAsync(SqlConnection instantane, SqlConnection v3, CatalogueV2 catalogue, TableV2 t,
                                                       RegleTable regle, int ordre, int idTransfert, string baseV2, int tailleLot,
                                                       IReadOnlySet<string> tablesAvecRejets, CancellationToken ct,
                                                       int passage = 0, ApresLot? apresLot = null)
    {
        if (t.ClePrimaire.Count == 0) return await CopierSansCleAsync(instantane, v3, t, regle, ordre, idTransfert, ct);
        var cle = t.ClePrimaire.Select(t.Colonne).ToList();
        var identifiantV2 = Sql.Identifiant("c", cle);
        var rejeter = regle.SiLienCasse == SiLienCasse.Rejeter;
        var nomRegle = regle.Provisoire ? "provisoire" : regle.Cible is null ? "telle quelle" : "colonne par colonne";

        // La table V3 : v2.NOM, creee ici a l'image de la V2 ; ou la table de la fiche (REC-012 a REC-019), creee par sa
        // migration, ou chaque colonne V2 est ecrite sous son nom V3 (sa cle recoit l'identifiant de la V2, comme v2.NOM).
        var cible = regle.Cible;
        if (cible is not null && cle.Count != 1) throw new EcartTransfert($"{t.Nom} : une fiche colonne par colonne demande une cle d'une colonne");
        var tableV3 = cible?.NomSql ?? NomV3(t);
        var tableV3Texte = cible?.TableV3 ?? NomV3Texte(t);
        string NomV3De(string nomV2) => cible is null ? nomV2
            : string.Equals(nomV2, cle[0].Nom, StringComparison.OrdinalIgnoreCase) ? cible.CleV3
            : cible.Colonnes.First(m => string.Equals(m.V2, nomV2, StringComparison.OrdinalIgnoreCase)).V3;
        var copiees = cible is null ? t.Colonnes
            : [.. t.Colonnes.Where(c => cle.Contains(c) || cible.Colonnes.Any(m => string.Equals(m.V2, c.Nom, StringComparison.OrdinalIgnoreCase)))];
        var traduites = cible?.Colonnes.Where(m => m.Traduction is not null).ToList() ?? [];
        var cleV3 = cle.Select(c => c with { Nom = NomV3De(c.Nom) }).ToList();
        var identifiantV3 = Sql.Identifiant("c", cleV3);

        // Le point de reprise de la table, verifie contre la table V3 elle-meme (REC-005 CA04).
        var point = await PointAsync(v3, idTransfert, t.Nom, ct);
        var debut = point?.PremierLe ?? DateTime.Now;
        object[]? derniere = null;
        if (point is not null)
        {
            if (await ScalaireAsync(v3, null, $"SELECT OBJECT_ID({Sql.Texte(tableV3)});", ct) is null)
                throw new EcartTransfert($"{t.Nom} : le point de reprise dit {point.Lignes} lignes ecrites (lot {point.Lot}), mais {tableV3Texte} n'existe pas");
            var deja = Convert.ToInt64(await ScalaireAsync(v3, null, $"SELECT COUNT_BIG(*) FROM {tableV3};", ct));
            string? cleEcrite = null;
            await using (var cmd = new SqlCommand($"SELECT TOP (1) {identifiantV3}, {string.Join(", ", cleV3.Select(c => $"c.{Sql.Q(c.Nom)}"))} " +
                                                  $"FROM {tableV3} AS c ORDER BY {string.Join(", ", cleV3.Select(c => $"c.{Sql.Q(c.Nom)} DESC"))};", v3) { CommandTimeout = 0 })
            await using (var l = await cmd.ExecuteReaderAsync(ct))
                if (await l.ReadAsync(ct))
                {
                    cleEcrite = l.GetString(0);
                    derniere = [.. Enumerable.Range(1, cle.Count).Select(l.GetValue)];
                }
            if (deja != point.Lignes || !string.Equals(cleEcrite, point.DerniereCle, StringComparison.Ordinal))
                throw new EcartTransfert($"{t.Nom} : point de reprise incoherent : lot {point.Lot} enregistre avec {point.Lignes} lignes et la derniere cle " +
                                         $"« {point.DerniereCle ?? "aucune"} », mais {tableV3Texte} a {deja} lignes et sa derniere cle est « {cleEcrite ?? "aucune"} »");
        }

        // 2. Les liens casses de la V2, avant d'ecrire quoi que ce soit (lecture de l'instantane).
        var casses = new List<(string IdV2, CleEtrangereV2 Cle, string Valeur)>();
        foreach (var k in t.Cles)
        {
            var visee = catalogue.Tables[k.TableVisee];
            var sql = $"SELECT {identifiantV2}, {Valeur(t, k)} FROM {t.NomSql} AS c WHERE {Casse(k, visee)}";
            await using var cmd = Lecture(instantane, sql);
            await using var l = await cmd.ExecuteReaderAsync(ct);
            while (await l.ReadAsync(ct)) casses.Add((l.GetString(0), k, l.GetString(1)));
        }
        var filtre = rejeter && t.Cles.Count > 0
            ? " WHERE NOT (" + string.Join(" OR ", t.Cles.Select(k => Casse(k, catalogue.Tables[k.TableVisee]))) + ")"
            : "";
        var rejetes = rejeter ? casses.GroupBy(c => c.IdV2, StringComparer.Ordinal).ToList() : [];

        // 1, 3, 4. La table, puis la copie lot par lot depuis l'instantane, chaque lot avec sa correspondance (meme
        // identifiant des deux cotes) et son point de reprise, dans SA transaction.
        // Une colonne calculee de la V3 seule (doublon herite...) est lue sous un nom a elle, qu'aucune colonne V2 ne porte.
        var calculees = cible?.Calculees.Select((k, i) => (k.V3, k.Expression, Alias: $"rigv3_calculee_{i}")).ToList() ?? [];
        var colonnes = string.Join(", ", copiees.Select(c => $"c.{Sql.Q(c.Nom)}").Concat(calculees.Select(k => $"{k.Expression} AS {Sql.Q(k.Alias)}")));
        var ordreCle = string.Join(", ", cle.Select(c => $"c.{Sql.Q(c.Nom)}"));
        var lot = point?.Lot ?? 0;
        var ecritesLots = point?.Lignes ?? 0;
        var dureeLots = point?.DureeMs ?? 0;
        // Une reprise dont le dernier lot enregistre etait le dernier de la table n'a plus que la transaction de fin a faire.
        var finie = point is not null && await ToutesLuesAsync();
        async Task<bool> ToutesLuesAsync()
        {
            // Plus aucune ligne apres la derniere cle ecrite, dans l'instantane ? (lot 1 vide : la table l'est)
            if (derniere is null) return true;
            await using var cmd = Lecture(instantane, $"SELECT COUNT_BIG(*) FROM (SELECT TOP (1) 1 AS x FROM {t.NomSql} AS c WHERE {Condition()}) AS s");
            AjouterCle(cmd, derniere);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) == 0;
        }
        string Condition() => (filtre.Length > 0 ? filtre[" WHERE ".Length..] + " AND " : "") + Sql.Apres("c", cle, "k");
        void AjouterCle(SqlCommand cmd, object[] valeurs)
        {
            for (var i = 0; i < cle.Count; i++) cmd.Parameters.Add(Sql.Parametre(cle[i], $"@k{i}", valeurs[i])!);
        }
        while (!finie)
        {
            var chronoLot = Stopwatch.StartNew();
            await using var txLot = (SqlTransaction)await v3.BeginTransactionAsync(ct);
            if (lot == 0 && cible is null) await ExecuterAsync(v3, txLot, Ddl(t), ct);
            else if (lot == 0 && Convert.ToInt64(await ScalaireAsync(v3, txLot, $"SELECT COUNT_BIG(*) FROM {tableV3};", ct)) > 0)
                throw new EcartTransfert($"{t.Nom} : la table {tableV3Texte} a deja des lignes avant le transfert (elle doit etre vide)");
            var depuis = derniere;
            await using (var cmd = Lecture(instantane, $"SELECT TOP ({tailleLot}) {colonnes} FROM {t.NomSql} AS c" +
                                                       (depuis is null ? filtre : " WHERE " + Condition()) + $" ORDER BY {ordreCle}"))
            {
                if (depuis is not null) AjouterCle(cmd, depuis);
                // fix-ok: « Tentative non valide de lecture du numero de colonne '0'. Avec CommandBehavior.SequentialAccess, vous ne
                // pouvez lire qu'a partir du numero de colonne '10' » (RecopieFichesCheck, 2e essai, ADRESSE_BASE -> rec.Adresse) :
                // SqlBulkCopy lit la source dans l'ordre des colonnes de la DESTINATION ; v2.NOM a l'ordre de la V2, rec.Adresse a sa
                // cle en premier. Lecture sequentielle pour la copie telle quelle seulement.
                await using var lecteur = await cmd.ExecuteReaderAsync(cible is null ? CommandBehavior.SequentialAccess : CommandBehavior.Default, ct);
                using var copie = new SqlBulkCopy(v3, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, txLot)
                {
                    DestinationTableName = tableV3,
                    BatchSize = tailleLot,
                    BulkCopyTimeout = 0,
                    EnableStreaming = true,
                };
                foreach (var c in copiees) copie.ColumnMappings.Add(c.Nom, NomV3De(c.Nom));
                foreach (var k in calculees) copie.ColumnMappings.Add(k.Alias, k.V3);
                foreach (var c in cleV3) copie.ColumnOrderHints.Add(c.Nom, SortOrder.Ascending);
                await copie.WriteToServerAsync(lecteur, ct);
            }
            var duLot = depuis is null ? "" : " WHERE " + Sql.Apres("c", cleV3, "k");
            // Les colonnes traduites du lot (un agent par son compte de session...) : la valeur de la V2 sans traduction est
            // notee lien casse, puis videe ; les autres recoivent leur valeur V3 (CibleFiche).
            foreach (var m in traduites)
            {
                var colonne = $"c.{Sql.Q(m.V3)}";
                var traduite = "(" + m.Traduction!.SqlVersV3(instantane.Database).Replace("@v", colonne, StringComparison.Ordinal) + ")";
                await using (var cmd = new SqlCommand(
                    "INSERT INTO rec.LienCasse (IdTransfert, BaseV2, TableV2, IdV2, Cle, Colonnes, Valeur, TableVisee, Cause) " +
                    $"SELECT {idTransfert}, {Sql.Texte(baseV2)}, {Sql.Texte(t.Nom)}, {identifiantV3}, {Sql.Texte("traduction " + m.V2)}, {Sql.Texte(m.V2)}, " +
                    $"CONVERT(nvarchar(400), {colonne}), {Sql.Texte(m.Traduction.Par)}, N'sans equivalent dans la V3' FROM {tableV3} AS c " +
                    $"{(duLot.Length == 0 ? " WHERE " : duLot + " AND ")}{colonne} IS NOT NULL AND {traduite} IS NULL;", v3, txLot) { CommandTimeout = 0 })
                {
                    if (depuis is not null) AjouterCle(cmd, depuis);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await using (var cmd = new SqlCommand($"UPDATE c SET {colonne} = {traduite} FROM {tableV3} AS c{(duLot.Length == 0 ? " WHERE " : duLot + " AND ")}{colonne} IS NOT NULL;",
                                                      v3, txLot) { CommandTimeout = 0 })
                {
                    if (depuis is not null) AjouterCle(cmd, depuis);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }
            long n;
            await using (var cmd = new SqlCommand(
                $"INSERT INTO rec.Correspondance (BaseV2, TableV2, IdV2, TableV3, IdV3, IdTransfert) " +
                $"SELECT {Sql.Texte(baseV2)}, {Sql.Texte(t.Nom)}, {identifiantV3}, {Sql.Texte(tableV3Texte)}, {identifiantV3}, {idTransfert} " +
                $"FROM {tableV3} AS c{duLot};", v3, txLot) { CommandTimeout = 0 })
            {
                if (depuis is not null) AjouterCle(cmd, depuis);
                n = await cmd.ExecuteNonQueryAsync(ct);
            }
            string? texteCle = null;
            if (n > 0)
                await using (var cmd = new SqlCommand($"SELECT TOP (1) {identifiantV3}, {string.Join(", ", cleV3.Select(c => $"c.{Sql.Q(c.Nom)}"))} " +
                                                      $"FROM {tableV3} AS c ORDER BY {string.Join(", ", cleV3.Select(c => $"c.{Sql.Q(c.Nom)} DESC"))};", v3, txLot) { CommandTimeout = 0 })
                await using (var l = await cmd.ExecuteReaderAsync(ct))
                {
                    await l.ReadAsync(ct);
                    texteCle = l.GetString(0);
                    derniere = [.. Enumerable.Range(1, cle.Count).Select(l.GetValue)];
                }
            if (n > 0 || lot == 0)
            {
                lot++;
                ecritesLots += n;
                dureeLots += chronoLot.ElapsedMilliseconds;
                await using var cmd = new SqlCommand("INSERT INTO rec.TransfertLot (IdTransfert, TableV2, Lot, Passage, Lignes, DerniereCle, DureeMs) " +
                                                     "VALUES (@id, @t, @lot, @p, @l, @c, @ms);", v3, txLot);
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = idTransfert });
                cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, 128) { Value = t.Nom });
                cmd.Parameters.Add(new SqlParameter("@lot", SqlDbType.Int) { Value = lot });
                cmd.Parameters.Add(new SqlParameter("@p", SqlDbType.Int) { Value = passage });
                cmd.Parameters.Add(new SqlParameter("@l", SqlDbType.BigInt) { Value = ecritesLots });
                cmd.Parameters.Add(new SqlParameter("@c", SqlDbType.NVarChar, 200) { Value = (object?)texteCle ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@ms", SqlDbType.BigInt) { Value = chronoLot.ElapsedMilliseconds });
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await txLot.CommitAsync(ct);
            if (n > 0 && apresLot is not null) await apresLot(t.Nom, lot, ecritesLots, ct);
            finie = n < tailleLot;
        }
        var lots = lot;

        // La transaction de fin : liens casses, rejets, controle de la table entiere, ligne du rapport.
        var chronoFin = Stopwatch.StartNew();
        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(ct);

        // 2 (suite). Rejets et liens marques.
        if (rejeter)
        {
            var table = new DataTable();
            foreach (var (nom, type) in new[] { ("IdTransfert", typeof(int)), ("BaseV2", typeof(string)), ("TableV2", typeof(string)), ("IdV2", typeof(string)), ("Motif", typeof(string)), ("Regle", typeof(string)) })
                table.Columns.Add(nom, type);
            foreach (var g in rejetes)
                table.Rows.Add(idTransfert, baseV2, t.Nom, g.Key, Borne("lien casse : " + string.Join(" ; ",
                    g.Select(c => $"{c.Cle.Nom} ({string.Join(", ", c.Cle.Colonnes)} = {c.Valeur}) vise une ligne absente de {c.Cle.TableVisee}")), 1000),
                    "lien casse : rejeter");
            await InsererAsync(v3, tx, "rec.Rejet", table, ct);
        }
        else
        {
            var table = TableLiens();
            foreach (var c in casses)
                table.Rows.Add(idTransfert, baseV2, t.Nom, c.IdV2, c.Cle.Nom, string.Join(", ", c.Cle.Colonnes), Borne(c.Valeur, 400), c.Cle.TableVisee, "absente de la V2");
            await InsererAsync(v3, tx, "rec.LienCasse", table, ct);
        }

        // 5. Les lignes ecrites dont le lien vise une ligne que le transfert a rejetee : dans une table deja copiee, ou dans
        // celle-ci (une demande dont la demande mere est rejetee) ; ses rejets sont deja dans rec.Rejet, meme transaction.
        // fix-ok: le lien d'une table sur elle-meme (DMND_ID_DMND_MERE) n'etait pas vu, la table n'entrant dans
        // tablesAvecRejets qu'apres sa copie (relecture du cas « demande mere rejetee » de REC-001 CA04).
        foreach (var k in t.Cles.Where(k => tablesAvecRejets.Contains(k.TableVisee) ||
                                            rejetes.Count > 0 && string.Equals(k.TableVisee, t.Nom, StringComparison.OrdinalIgnoreCase)))
        {
            var visee = catalogue.Tables[k.TableVisee];
            if (!k.ColonnesVisees.SequenceEqual(visee.ClePrimaire, StringComparer.OrdinalIgnoreCase)) continue;
            if (!k.Colonnes.All(n => copiees.Any(c => string.Equals(c.Nom, n, StringComparison.OrdinalIgnoreCase)))) continue;
            // Le lien lu dans la table V3, sous ses noms V3 (les memes pour v2.NOM).
            var valeurV3 = Sql.Identifiant("c", k.Colonnes.Select(n => t.Colonne(n) with { Nom = NomV3De(n) }));
            await ExecuterAsync(v3, tx,
                "INSERT INTO rec.LienCasse (IdTransfert, BaseV2, TableV2, IdV2, Cle, Colonnes, Valeur, TableVisee, Cause) " +
                $"SELECT {idTransfert}, {Sql.Texte(baseV2)}, {Sql.Texte(t.Nom)}, {identifiantV3}, {Sql.Texte(k.Nom)}, " +
                $"{Sql.Texte(string.Join(", ", k.Colonnes))}, {valeurV3}, {Sql.Texte(k.TableVisee)}, 'rejetee par le transfert' " +
                $"FROM {tableV3} AS c WHERE EXISTS (SELECT 1 FROM rec.Rejet r WHERE r.IdTransfert = {idTransfert} " +
                $"AND r.BaseV2 = {Sql.Texte(baseV2)} AND r.TableV2 = {Sql.Texte(k.TableVisee)} AND r.IdV2 = {valeurV3});", ct);
        }

        // 6. Le controle de fin de table.
        var lues = Convert.ToInt64(await ScalaireLectureAsync(instantane, $"SELECT COUNT_BIG(*) FROM {t.NomSql} AS c", ct));
        var ecrites = Convert.ToInt64(await ScalaireAsync(v3, tx, $"SELECT COUNT_BIG(*) FROM {tableV3};", ct));
        long rejetees = rejetes.Count;
        var correspondances = Convert.ToInt64(await ScalaireAsync(v3, tx,
            $"SELECT COUNT_BIG(*) FROM rec.Correspondance WHERE BaseV2 = {Sql.Texte(baseV2)} AND TableV2 = {Sql.Texte(t.Nom)} AND IdTransfert = {idTransfert};", ct));
        var liens = Convert.ToInt64(await ScalaireAsync(v3, tx,
            $"SELECT COUNT_BIG(*) FROM rec.LienCasse WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)};", ct));
        var sansTraduction = traduites.Count == 0 ? 0 : Convert.ToInt64(await ScalaireAsync(v3, tx,
            $"SELECT COUNT_BIG(*) FROM rec.LienCasse WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)} AND Cause = N'sans equivalent dans la V3';", ct));
        if (lues != ecrites + rejetees)
            throw new EcartTransfert($"{t.Nom} : {lues} lignes lues dans la V2, {ecrites} ecrites et {rejetees} rejetees (lues = ecrites + rejetees attendu)");
        if (correspondances != ecrites)
            throw new EcartTransfert($"{t.Nom} : {ecrites} lignes ecrites mais {correspondances} correspondances");
        // L'empreinte : toutes les colonnes ecrites telles quelles (une colonne traduite n'a plus la valeur de la V2), chacune
        // sous son nom de chaque cote.
        var empreinte = copiees.Where(c => !c.HorsEmpreinte && !traduites.Any(m => string.Equals(m.V2, c.Nom, StringComparison.OrdinalIgnoreCase))).ToList();
        if (empreinte.Count > 0)
        {
            string Liste(bool cote2) => string.Join(", ", empreinte.Select(c => c.TypeBase == "timestamp" && cote2
                ? $"CONVERT(binary(8), c.{Sql.Q(c.Nom)})" : $"c.{Sql.Q(cote2 ? c.Nom : NomV3De(c.Nom))}"));
            var e2 = await ScalaireLectureAsync(instantane, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(true)})) FROM {t.NomSql} AS c{filtre}", ct);
            var e3 = await ScalaireAsync(v3, tx, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(false)})) FROM {tableV3} AS c;", ct);
            if (!Equals(e2, e3))
                throw new EcartTransfert($"{t.Nom} : les valeurs ecrites ne sont pas celles de la V2 (empreinte V2 {e2 ?? "vide"}, V3 {e3 ?? "vide"})");
        }
        var octets = Convert.ToInt64(await ScalaireAsync(v3, tx,
            "SELECT ISNULL(SUM(a.total_pages), 0) * 8192 FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id = p.partition_id " +
            $"WHERE p.object_id = OBJECT_ID({Sql.Texte(tableV3)});", ct));

        var cassesHerites = rejeter ? 0 : casses.Count;
        var detail = $"{t.Cles.Count} cles etrangeres verifiees ({t.Cles.Count(k => k.Desactivee || k.NonVerifiee)} desactivees ou non verifiees dans la V2), " +
                     $"{cassesHerites} liens casses herites marques, {liens - cassesHerites - sansTraduction} liens vers une ligne rejetee, " +
                     $"{t.Declencheurs.Count} declencheurs releves (non recopies), {lots} lots de {tailleLot}" +
                     (cible is null ? "" : $" ; {copiees.Count} colonnes ecrites sous leur nom V3 dans {cible.TableV3}, " +
                                           $"{t.Colonnes.Count - copiees.Count} propres a la V2 non reprises" +
                                           (traduites.Count == 0 ? "" : $", {traduites.Count} traduite(s) ({string.Join(", ", traduites.Select(m => $"{m.V2} par {m.Traduction!.Par}"))}) dont {sansTraduction} valeur(s) sans equivalent")) +
                     (point is null ? "" : $" (repris apres le lot {point.Lot} : ses {point.Lignes} lignes, deja ecrites, ne sont pas reecrites)") +
                     (regle.Provisoire ? " ; REGLE PROVISOIRE (REC-008) : la story du domaine doit declarer la sienne" : "");
        // Duree de la table : ses lots, tous passages confondus, et cette transaction de fin.
        var ligne = new LigneRapport(t.Nom, ordre, nomRegle, regle.Story, tableV3Texte, lues, ecrites, rejetees, liens,
                                     correspondances, octets, dureeLots + chronoFin.ElapsedMilliseconds, detail);
        await JournalTransfert.InsererTableAsync(v3, tx, idTransfert, ligne, debut, ct);
        await tx.CommitAsync(ct);
        return ligne;
    }

    /// <summary>
    /// La copie PROVISOIRE d'une table SANS CLE PRIMAIRE (REC-008 : RIG_DEV en a 104 sur 859, 4,2 millions de lignes, lu le
    /// 2026-11-21) : une ligne ne s'y identifie pas, donc ni correspondance, ni lot, ni liens casses verifies ligne a ligne.
    /// La table est copiee en UNE transaction avec sa ligne de rapport : un incident l'annule entiere et la reprise la
    /// recopie entiere -- ni perte ni doublon. Controle de fin : lues = ecrites, meme empreinte des deux cotes.
    /// </summary>
    private static async Task<LigneRapport> CopierSansCleAsync(SqlConnection instantane, SqlConnection v3, TableV2 t, RegleTable regle, int ordre,
                                                              int idTransfert, CancellationToken ct)
    {
        if (!regle.Provisoire) throw new EcartTransfert($"{t.Nom} n'a pas de cle primaire et sa regle n'est pas provisoire");
        var chrono = Stopwatch.StartNew();
        var debut = DateTime.Now;
        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(ct);
        await ExecuterAsync(v3, tx, Ddl(t), ct);
        await using (var cmd = Lecture(instantane, $"SELECT {string.Join(", ", t.Colonnes.Select(c => $"c.{Sql.Q(c.Nom)}"))} FROM {t.NomSql} AS c"))
        await using (var lecteur = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct))
        {
            using var copie = new SqlBulkCopy(v3, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, tx)
            {
                DestinationTableName = NomV3(t),
                BulkCopyTimeout = 0,
                EnableStreaming = true,
            };
            foreach (var c in t.Colonnes) copie.ColumnMappings.Add(c.Nom, c.Nom);
            await copie.WriteToServerAsync(lecteur, ct);
        }
        var lues = Convert.ToInt64(await ScalaireLectureAsync(instantane, $"SELECT COUNT_BIG(*) FROM {t.NomSql} AS c", ct));
        var ecrites = Convert.ToInt64(await ScalaireAsync(v3, tx, $"SELECT COUNT_BIG(*) FROM {NomV3(t)};", ct));
        if (lues != ecrites) throw new EcartTransfert($"{t.Nom} : {lues} lignes lues dans la V2, {ecrites} ecrites (table sans cle, copie provisoire)");
        var empreinte = t.Colonnes.Where(c => !c.HorsEmpreinte).ToList();
        if (empreinte.Count > 0)
        {
            string Liste(bool cote2) => string.Join(", ", empreinte.Select(c => c.TypeBase == "timestamp" && cote2
                ? $"CONVERT(binary(8), c.{Sql.Q(c.Nom)})" : $"c.{Sql.Q(c.Nom)}"));
            var e2 = await ScalaireLectureAsync(instantane, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(true)})) FROM {t.NomSql} AS c", ct);
            var e3 = await ScalaireAsync(v3, tx, $"SELECT CHECKSUM_AGG(BINARY_CHECKSUM({Liste(false)})) FROM {NomV3(t)} AS c;", ct);
            if (!Equals(e2, e3))
                throw new EcartTransfert($"{t.Nom} : les valeurs ecrites ne sont pas celles de la V2 (empreinte V2 {e2 ?? "vide"}, V3 {e3 ?? "vide"})");
        }
        var octets = Convert.ToInt64(await ScalaireAsync(v3, tx,
            "SELECT ISNULL(SUM(a.total_pages), 0) * 8192 FROM sys.partitions p JOIN sys.allocation_units a ON a.container_id = p.partition_id " +
            $"WHERE p.object_id = OBJECT_ID({Sql.Texte(NomV3(t))});", ct));
        var ligne = new LigneRapport(t.Nom, ordre, "provisoire", regle.Story, NomV3Texte(t), lues, ecrites, 0, 0, null, octets, chrono.ElapsedMilliseconds,
            $"SANS CLE PRIMAIRE : copiee en une transaction, sans correspondance ni reprise par lot, {t.Cles.Count} cles etrangeres non verifiees ; " +
            "REGLE PROVISOIRE (REC-008) : la story du domaine doit dire comment identifier une ligne");
        await JournalTransfert.InsererTableAsync(v3, tx, idTransfert, ligne, debut, ct);
        await tx.CommitAsync(ct);
        return ligne;
    }

    /// <summary>
    /// Annule une table entamee et non finie (pas de ligne rec.TransfertTable) : sa table V3, ses correspondances, ses
    /// lots. C'est l'ARRET PROPRE d'un ecart (REC-001) : un ecart ne se reprend pas, la table ne garde rien de ses lots
    /// deja ecrits. Un incident technique, lui, garde les lots : la reprise repart du dernier (REC-005).
    /// </summary>
    public static async Task AnnulerAsync(string connexionV3, int idTransfert, TableV2 t, RegleTable? regle = null)
    {
        await using var v3 = new SqlConnection(connexionV3);
        await v3.OpenAsync(CancellationToken.None);
        await using var tx = (SqlTransaction)await v3.BeginTransactionAsync(CancellationToken.None);
        if (Convert.ToInt32(await ScalaireAsync(v3, tx, $"SELECT COUNT(*) FROM rec.TransfertTable WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)};",
                                                CancellationToken.None)) > 0) return;
        // La table d'une fiche est celle de sa migration : elle est videe, pas supprimee.
        await ExecuterAsync(v3, tx, (regle?.Cible is { } c ? $"DELETE FROM {c.NomSql}; " : $"DROP TABLE IF EXISTS {NomV3(t)}; ") +
            $"DELETE FROM rec.Correspondance WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)}; " +
            $"DELETE FROM rec.TransfertLot WHERE IdTransfert = {idTransfert} AND TableV2 = {Sql.Texte(t.Nom)};", CancellationToken.None);
        await tx.CommitAsync(CancellationToken.None);
    }

    internal static DataTable TableLiens()
    {
        var table = new DataTable();
        foreach (var (nom, type) in new[] { ("IdTransfert", typeof(int)), ("BaseV2", typeof(string)), ("TableV2", typeof(string)), ("IdV2", typeof(string)),
                                            ("Cle", typeof(string)), ("Colonnes", typeof(string)), ("Valeur", typeof(string)), ("TableVisee", typeof(string)), ("Cause", typeof(string)) })
            table.Columns.Add(nom, type);
        return table;
    }

    private static string Borne(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    /// <summary>Une lecture de l'instantane : par la porte de la V2 (un SELECT seul), sans delai (une table de la V2
    /// peut prendre des minutes : DEMANDE fait 5 millions de lignes dans RIG_DEV).</summary>
    private static SqlCommand Lecture(SqlConnection instantane, string sql)
    {
        LectureV2.ExigerLecture(sql);
        return new SqlCommand(sql, instantane) { CommandTimeout = 0 };
    }

    private static async Task<object?> ScalaireLectureAsync(SqlConnection instantane, string sql, CancellationToken ct)
    {
        await using var cmd = Lecture(instantane, sql);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task ExecuterAsync(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalaireAsync(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 0 };
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task InsererAsync(SqlConnection cn, SqlTransaction tx, string destination, DataTable lignes, CancellationToken ct)
    {
        if (lignes.Rows.Count == 0) return;
        using var copie = new SqlBulkCopy(cn, SqlBulkCopyOptions.Default, tx) { DestinationTableName = destination, BulkCopyTimeout = 0 };
        foreach (DataColumn c in lignes.Columns) copie.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copie.WriteToServerAsync(lignes, ct);
    }
}
