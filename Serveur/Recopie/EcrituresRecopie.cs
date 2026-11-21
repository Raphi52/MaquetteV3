// fix-ok: le journal de la remontee ecrivait « non note par la V2 » pour un client dont la V2 note l'agent (CLIEN_ID_UTLST) --
// mesure le 2026-11-21 par RecopieDeuxSensCheck (SOC-016 CA06 rouge : client 578508 enregistre par l'agent n° 1, CompteV2
// « non note par la V2 ») ; l'agent se lit desormais ici (AgentV2Async), pour l'ecart (Applicateur) comme pour le journal (Remontee).
using System.Data;
using Microsoft.Data.SqlClient;

namespace RigV3.Serveur.Recopie;

/// <summary>Une fiche attend une autre fiche qui n'est pas encore passee : la ligne reste en file, rien n'est ecrit.</summary>
public sealed class RecopieEnAttente(string message) : Exception(message);

/// <summary>La base d'en face ne peut pas recevoir la fiche telle qu'elle est (une valeur sans equivalent, REC-013 CA02) : un
/// REFUS, compte comme celui d'une contrainte de la base (un essai, mise de cote au 3e), avec ce motif lisible.</summary>
public sealed class RecopieRefusee(string message) : Exception(message);

/// <summary>Un refus qu'aucun nouvel essai ne levera (une valeur unique deja prise par une autre fiche, REC-013 CA03,
/// REC-017 CA04) : la fiche est mise de cote D'EMBLEE, avec son motif, et l'ecart est ecrit pour l'agent designe.</summary>
/// <param name="IdV2">La fiche de la V2 en cause (celle qui tient la valeur, ou celle qui ne passe pas).</param>
/// <param name="IdV3">La fiche de la V3 en cause (celle qui ne passe pas, ou celle qui tient la valeur).</param>
/// <param name="Champ">La colonne (nom V2) et ses deux valeurs (texte JSON).</param>
public sealed class RecopieImpossible(string message, string idV2, string idV3, string champ, string? valeurV2, string? valeurV3) : Exception(message)
{
    public string IdV2 { get; } = idV2;
    public string IdV3 { get; } = idV3;
    public string Champ { get; } = champ;
    public string? ValeurV2 { get; } = valeurV2;
    public string? ValeurV3 { get; } = valeurV3;
}

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

    /// <summary>La fiche V3, lue sous les noms de la V2 (une table colonne par colonne : « x.[Nom] AS [CLIEN_NOM] »).</summary>
    public static async Task<LigneJson?> LireV3Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, bool verrou, CancellationToken ct,
                                                     bool avecPropres = false)
        => LigneJson.Lire(await ScalaireAsync(cn, tx, t.SelectJsonV3(avecPropres ? t.Colonnes.Concat(t.PropresV3) : t.Colonnes, "@id",
                                                                      verrou ? "WITH (UPDLOCK, HOLDLOCK)" : null), ct, ("@id", idV3)));

    public static async Task<bool> ExisteV3Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, CancellationToken ct)
        => await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(1), 1) FROM {t.NomV3Sql} AS x WHERE {t.CleEgaleV3("x", "@id")};", ct, ("@id", idV3)) is not null;

    public static async Task<bool> ExisteV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV2, CancellationToken ct)
        => await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(1), 1) FROM {t.NomV2Sql} AS x WHERE {t.CleEgale("x", "@id")};", ct, ("@id", idV2)) is not null;

    // --- la correspondance (rec.Correspondance, REC-001 T01) ----------------------------------------------------

    /// <summary>La ligne V2 d'une fiche V3. Une fiche V3 qui en represente plusieurs de la V2 (l'adresse inconnue, REC-016
    /// CA03) n'a qu'une correspondance qui n'est pas « fusion » : c'est elle que la recopie V3 -> V2 suit.</summary>
    public static Task<string?> IdV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, string idV3, CancellationToken ct)
        => ScalaireAsync(cn, tx, "SELECT TOP (1) IdV2 FROM rec.Correspondance WHERE TableV3 = @t3 AND IdV3 = @id AND BaseV2 = @b AND TableV2 = @t2 " +
                                 "ORDER BY CASE WHEN Origine = 'fusion' THEN 1 ELSE 0 END, Le;", ct,
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
        // Les colonnes traduites (un agent par son compte de session, REC-013 CA02 ; un service) : une valeur renseignee qui
        // n'a pas d'equivalent de l'autre cote est un REFUS -- la fiche attend, l'exploitant le voit --, jamais une valeur
        // recopiee brute ni inventee.
        foreach (var c in t.Colonnes)
        {
            if (t.TraductionDe(c.Nom) is not { } traduction || !r.TryGetValue(c.Nom, out var brut) || TableRecopiee.TexteDe(brut) is not { } valeur) continue;
            var sql = versV2 ? traduction.SqlVersV2(t.BaseV2) : traduction.SqlVersV3(t.BaseV2);
            var source = versV2 ? t.ColonneV3(c) : c;
            var autre = await ScalaireAsync(cn, tx, $"SELECT CONVERT(nvarchar(400), ({sql.Replace("@v", $"CONVERT({source.TypeSql}, @valeur)", StringComparison.Ordinal)}));",
                                            ct, ("@valeur", valeur))
                        ?? throw new RecopieRefusee($"{t.NomV2}.{c.Nom} = {valeur} : sans equivalent dans la {(versV2 ? "V2" : "V3")} par {traduction.Par}");
            r[c.Nom] = TableRecopiee.Brut(autre, versV2 ? c : t.ColonneV3(c));
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
        // Une fiche colonne par colonne n'a pas ses colonnes propres dans la V3 : seules celles qui ont une valeur de creation
        // sont ecrites (l'etat « N »), les autres prennent la valeur par defaut de la V2 (une imprimante reste vide, REC-017 CA03).
        var lues = t.Colonnes.Concat(t.Regle.ColonneParColonne ? t.Propres.Where(p => t.Regle.CreationV2.ContainsKey(p.Nom)) : t.Propres).ToList();
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
        return SousMarqueAsync(cn, tx, $"UPDATE x SET {string.Join(", ", cols.Select(c => $"x.{Sql.Q(t.ColonneV3(c).Nom)} = j.{Sql.Q(c.Nom)}"))} " +
                                       $"FROM {t.NomV3Sql} AS x CROSS JOIN OPENJSON(@json) {t.OpenJsonV3(cols)} AS j WHERE {t.CleEgaleV3("x", "@id")};",
                               ct, ("@id", idV3), ("@json", ligne.Ecrire()));
    }

    /// <summary>Cree dans la V3 une fiche venue de la V2 ; la V3 lui donne son identifiant quand sa cle est une identite
    /// (REC-003 CA03). Les colonnes propres recoivent la valeur de la V2.</summary>
    public static async Task<string> CreerV3Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV2, LigneJson ligneAvecPropres, CancellationToken ct)
    {
        var lues = t.Colonnes.Concat(t.PropresV3).ToList();
        var cibles = lues.Select(c => Sql.Q(t.ColonneV3(c).Nom)).ToList();
        var valeurs = lues.Select(c => $"j.{Sql.Q(c.Nom)}").ToList();
        if (!t.CleIdentiteV3) { cibles.Add(Sql.Q(t.CleV3.Nom)); valeurs.Add($"CONVERT({t.CleV3.TypeSql}, @id2)"); }
        string? id = null;
        await SousMarqueAsync(cn, tx, $"""
            DECLARE @ids TABLE (Id {t.CleV3.TypeSql});
            INSERT INTO {t.NomV3Sql} ({string.Join(", ", cibles)}) OUTPUT inserted.{Sql.Q(t.CleV3.Nom)} INTO @ids
            SELECT {string.Join(", ", valeurs)} FROM OPENJSON(@json) {t.OpenJsonV3(lues)} AS j;
            SELECT {Sql.EnTexte("x", t.CleV3 with { Nom = "Id" })} FROM @ids AS x;
            """, ct, v => id = v, ("@id2", idV2), ("@json", ligneAvecPropres.Ecrire()));
        return id ?? throw new InvalidOperationException($"{t.NomV3} : la V3 n'a rendu aucun identifiant pour la fiche V2 {idV2}");
    }

    public static Task SupprimerV3Async(SqlConnection cn, SqlTransaction tx, TableRecopiee t, string idV3, CancellationToken ct)
        => SousMarqueAsync(cn, tx, $"DELETE x FROM {t.NomV3Sql} AS x WHERE {t.CleEgaleV3("x", "@id")};", ct, ("@id", idV3));

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

    // --- l'agent de la V2 --------------------------------------------------------------------------------------------

    /// <summary>L'agent V2 qui a enregistre le changement <paramref name="v2"/> d'une fiche (REC-004 CA01, SOC-016 CA06) :
    /// celui que la V2 note sur la fiche (<see cref="RegleRecopie.ColonneAuteurV2"/> : CLIEN_ID_UTLST d'un client, ecrit avec
    /// la date par Client.Sauve), seulement si ce changement l'a note -- sa date ou son agent differe de <paramref name="avant"/>
    /// (null : fiche neuve). Sinon l'agent de la fiche est celui d'un enregistrement plus ancien : null, « non note ».
    /// Nom : tel que la fiche client de la V2 l'affiche (Utilisateur.NomPrenomAbrege, « NOM Prenom (ABREGE) ») ; Compte : son
    /// compte de connexion (UTLST_LOGIN, varchar(35) comme soc.Journal.CompteV2).</summary>
    public static async Task<(string Nom, string Compte)?> AgentV2Async(SqlConnection cn, SqlTransaction? tx, TableRecopiee t, LigneJson? v2, LigneJson? avant,
                                                                        CancellationToken ct)
    {
        static string? Valeur(string? colonne, LigneJson? l) => colonne is not null && l is not null && l.TryGetValue(colonne, out var b) ? LigneJson.Lisible(b)?.Trim() : null;
        if (Valeur(t.Regle.ColonneAuteurV2, v2) is not { Length: > 0 } agent) return null;
        if (avant is not null && Valeur(t.Regle.ColonneDateV2, v2) == Valeur(t.Regle.ColonneDateV2, avant) && Valeur(t.Regle.ColonneAuteurV2, avant) == agent) return null;
        await using var cmd = new SqlCommand("SELECT CONCAT(UTLST_NOM, ' ', UTLST_PRENOM, ' (', UTLST_ABREGE, ')'), UTLST_LOGIN " +
                                             $"FROM {Sql.Q(t.BaseV2)}.dbo.UTILISATEUR WHERE UTLST_ID_UTLST = @u;", cn, tx) { CommandTimeout = 120 };
        cmd.Parameters.Add(Parametre("@u", agent));
        await using var l = await cmd.ExecuteReaderAsync(ct);
        return await l.ReadAsync(ct) ? (l.GetString(0), l.GetString(1)) : null;
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
