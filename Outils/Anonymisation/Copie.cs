using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using RigV3.Base;

namespace RigV3.Outils.Anonymisation;

/// <summary>Ce que demande l'exploitant (ligne de commande : Program.cs).</summary>
public sealed record OptionsCopie
{
    /// <summary>Chaine de la base V2 source (Database=… obligatoire). Lue en lecture seule.</summary>
    public required string Source { get; init; }
    /// <summary>Chaine de la base copie : serveur de CE poste seulement ; base absente (creee) ou vide.</summary>
    public required string Cible { get; init; }
    /// <summary>Hors de ce poste : le nom de la base source, donne comme accord pour CETTE base (CA07).</summary>
    public string? Accord { get; init; }
    /// <summary>Graine des valeurs fictives (copie reproductible) : refusee pour une source hors poste.</summary>
    public string? Graine { get; init; }
    /// <summary>Tables a copier (« CLIENT » ou « dbo.CLIENT ») ; vide = toutes.</summary>
    public IReadOnlyList<string>? Tables { get; init; }
    /// <summary>Copie reduite : au plus N lignes par table (les premieres dans l'ordre de la cle).</summary>
    public long? MaxLignes { get; init; }
    /// <summary>Regles relues (« TABLE.COLONNE » -> traitement), prioritaires sur le catalogue.</summary>
    public IEnumerable<KeyValuePair<string, Traitement>>? Remplacements { get; init; }
    /// <summary>Fichier Markdown du rapport (aucune valeur des bases n'y est ecrite).</summary>
    public string? FichierRapport { get; init; }
    public int TailleLot { get; init; } = 5000;
    public TextWriter? Journal { get; init; }
}

/// <summary>
/// LA COPIE ANONYMISEE D'UNE BASE V2 (SOC-021 T02).
///
///  1. Garde-fous AVANT toute connexion : copie sur ce poste seulement (BaseJetable.ExigerServeurLocal),
///     source hors poste seulement avec l'accord donne pour elle, graine refusee hors poste.
///  2. Schema lu dans la source (lecture seule), base copie creee au classement de la source.
///  3. Table par table : lecture en continu, REMPLACEMENT des valeurs personnelles en memoire, ecriture
///     par lots (SqlBulkCopy) -- les valeurs reelles ne sont jamais ecrites sur l'instance de test.
///     Identifiants, numeros, dates des affaires, montants : recopies tels quels (CA03), identites
///     reprises a la derniere valeur de la source (les vrais compteurs).
///  4. Index, cles etrangeres (verifiees sur les donnees copiees quand la source les verifie),
///     contraintes, vues, fonctions, procedures, declencheurs.
///  5. Marque de la copie (schema « anonymisation ») : regle de chaque colonne, et « chaines
///     invalidees par l'anonymisation » des qu'une ligne signee a change (CA05). Rapport.
/// </summary>
public static class CopieAnonymisee
{
    /// <summary>Le schema de la marque, dans la copie ; jamais dans une base V2 reelle.</summary>
    public const string SchemaMarque = "anonymisation";
    public const string ProprieteBase = "RIGV3_ANONYMISATION";

    public static async Task<Rapport> CopierAsync(OptionsCopie o, CancellationToken ct = default)
    {
        // ---- 1. garde-fous, sans se connecter
        BaseJetable.ExigerServeurLocal(o.Cible);
        var cible = new SqlConnectionStringBuilder(o.Cible);
        if (string.IsNullOrWhiteSpace(cible.InitialCatalog))
            throw new RefusAnonymisation("La chaine de la copie doit nommer sa base (Database=…).");
        var source = PorteSource.Verifier(o.Source, o.Accord);
        if (o.Graine is not null && !PorteSource.EstSurCePoste(o.Source))
            throw new RefusAnonymisation(
                "Graine refusee : une source hors de ce poste se copie avec une cle aleatoire, detruite en fin de copie " +
                "(une graine connue rendrait la copie reversible).");
        if (PorteSource.EstSurCePoste(o.Source) && MemeServeur(source.DataSource, cible.DataSource)
            && string.Equals(source.InitialCatalog, cible.InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new RefusAnonymisation("La copie et la source sont la meme base.");

        var rapport = new Rapport
        {
            Source = $"{source.DataSource} / {source.InitialCatalog}",
            Copie = $"{cible.DataSource} / {cible.InitialCatalog}",
            Debut = DateTime.Now,
            Accord = PorteSource.EstSurCePoste(o.Source) ? null : o.Accord,
            MaxLignes = o.MaxLignes,
        };
        var catalogue = new CatalogueRegles(o.Remplacements);
        using var pseudonymes = o.Graine is null ? Pseudonymes.Aleatoire() : Pseudonymes.DepuisGraine(o.Graine);
        rapport.Reproductible = pseudonymes.Reproductible;
        var anonymiseur = new Anonymiseur(pseudonymes, catalogue);
        var journal = o.Journal ?? TextWriter.Null;

        await using var src = await PorteSource.OuvrirAsync(o.Source, o.Accord, ct);
        rapport.EcrituresSourceAvant = await src.EcrituresServeurAsync(ct);
        var schema = await SchemaSource.LireAsync(src, ct);
        catalogue.AlignerSurLesCles(schema);
        var tables = Choisir(schema.Tables, o.Tables);
        journal.WriteLine($"Source {rapport.Source} : {schema.Tables.Count} tables, {tables.Count} a copier.");

        // ---- 2. la base copie
        await CreerBaseAsync(cible, schema.Collation, ct);
        await using var cn = new SqlConnection(cible.ConnectionString);
        await cn.OpenAsync(ct);
        foreach (var s in tables.Select(t => t.Schema).Concat(schema.Modules.Select(m => m.Schema)).Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(s => !s.Equals("dbo", StringComparison.OrdinalIgnoreCase)))
            await ExecuterAsync(cn, $"IF SCHEMA_ID(N'{s.Replace("'", "''")}') IS NULL EXEC(N'CREATE SCHEMA {Ddl.Q(s).Replace("'", "''")}');", ct);

        var copiees = tables.ToDictionary(t => t.Id);
        foreach (var t in tables)
        {
            ct.ThrowIfCancellationRequested();
            long lignes;
            try
            {
                await ExecuterAsync(cn, Ddl.CreerTable(t), ct);
                // Index groupe et cle primaire AVANT les donnees : la table se remplit dans son ordre.
                foreach (var i in schema.Index.Where(i => i.Table == t.Id && (i.Type == 1 || i.Primaire)))
                {
                    await ExecuterAsync(cn, Ddl.CreerIndex(t, i), ct);
                    rapport.IndexCrees.Add($"{t.Libelle}.{i.Nom}");
                }
                lignes = await CopierTableAsync(src, cn, t, schema, catalogue, anonymiseur, o, rapport, ct);
            }
            catch (Exception e) when (e is SqlException or InvalidOperationException)
            {
                // Une copie incomplete n'est pas une copie : on s'arrete, en nommant la table.
                throw new InvalidOperationException($"Table {t.Libelle} : {e.Message}", e);
            }
            rapport.Volumes.Add((t.Libelle, lignes));
            journal.WriteLine($"  {t.Libelle} : {lignes} lignes");
            if (t.DerniereIdentite is { } derniere)
                await ExecuterAsync(cn, $"DBCC CHECKIDENT ('{t.Complet.Replace("'", "''")}', RESEED, {derniere.ToString(CultureInfo.InvariantCulture)}) WITH NO_INFOMSGS;", ct);
        }

        // ---- 4. la forme : index, cles, contraintes, modules
        foreach (var i in schema.Index.Where(i => copiees.ContainsKey(i.Table) && !(i.Type == 1 || i.Primaire)))
        {
            try
            {
                await ExecuterAsync(cn, Ddl.CreerIndex(copiees[i.Table], i), ct);
                rapport.IndexCrees.Add($"{copiees[i.Table].Libelle}.{i.Nom}");
            }
            catch (SqlException e)
            {
                // Un index UNIQUE sur une colonne anonymisee qui ne tiendrait plus = deux valeurs fusionnees : grave.
                rapport.Anomalies.Add($"index {copiees[i.Table].Libelle}.{i.Nom} non refait : {e.Message}");
            }
        }
        rapport.Ignores.AddRange(schema.IndexIgnores.Where(i => tables.Any(t => i.StartsWith(t.Libelle + ".", StringComparison.Ordinal))));

        foreach (var k in schema.Cles)
        {
            if (!copiees.TryGetValue(k.Table, out var t)) continue;
            if (!copiees.TryGetValue(k.Reference, out var r))
            {
                rapport.Ignores.Add($"cle {t.Libelle}.{k.Nom} : la table referencee n'est pas copiee");
                continue;
            }
            var verifier = !k.NonVerifiee && !k.Desactivee && o.MaxLignes is null;
            try
            {
                await ExecuterAsync(cn, Ddl.CreerCle(t, r, k, verifier), ct);
                (verifier ? rapport.ClesVerifiees : rapport.ClesNonVerifiees).Add(
                    $"{t.Libelle}.{k.Nom}" + (verifier ? "" : k.NonVerifiee ? " (deja non verifiee dans la source)" : o.MaxLignes is null ? " (desactivee dans la source)" : " (copie reduite)"));
            }
            catch (SqlException e) when (verifier)
            {
                rapport.Anomalies.Add($"cle {t.Libelle}.{k.Nom} : les donnees copiees ne la verifient pas ({e.Message}) ; posee sans verification");
                await ExecuterAsync(cn, Ddl.CreerCle(t, r, k, false), ct);
                rapport.ClesNonVerifiees.Add($"{t.Libelle}.{k.Nom} (ECHEC de la verification)");
            }
            if (k.Desactivee) await ExecuterAsync(cn, $"ALTER TABLE {t.Complet} NOCHECK CONSTRAINT {Ddl.Q(k.Nom)};", ct);
        }

        foreach (var c in schema.Contraintes)
        {
            if (!copiees.TryGetValue(c.Table, out var t)) continue;
            var verifier = !c.NonVerifiee && !c.Desactivee;
            try { await ExecuterAsync(cn, Ddl.CreerContrainte(t, c, verifier), ct); }
            catch (SqlException e) when (verifier)
            {
                // Une contrainte de format (CHECK … LIKE '%@%') que la valeur fictive ne tient plus : la regle casse la forme.
                rapport.Anomalies.Add($"contrainte {t.Libelle}.{c.Nom} : les donnees copiees ne la verifient pas ({e.Message}) ; posee sans verification");
                await ExecuterAsync(cn, Ddl.CreerContrainte(t, c, false), ct);
            }
            if (c.Desactivee) await ExecuterAsync(cn, $"ALTER TABLE {t.Complet} NOCHECK CONSTRAINT {Ddl.Q(c.Nom)};", ct);
        }

        await CreerModulesAsync(cn, schema, copiees.Values.ToList(), rapport, ct);

        // ---- 5. la marque et le rapport
        rapport.EcrituresSourceApres = await src.EcrituresServeurAsync(ct);
        rapport.RequetesSource = src.Requetes;
        rapport.Fin = DateTime.Now;
        foreach (var t in tables)
            foreach (var c in t.Colonnes)
                if (catalogue.Suspicion(t.Nom, c.Nom) is { } raison) rapport.ARelire.Add(($"{t.Libelle}.{c.Nom}", raison));
        await MarquerAsync(cn, rapport, ct);
        if (o.FichierRapport is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.FichierRapport))!);
            await File.WriteAllTextAsync(o.FichierRapport, rapport.EnMarkdown(), ct);
        }
        return rapport;
    }

    private static List<TableSql> Choisir(List<TableSql> toutes, IReadOnlyList<string>? demandees)
    {
        if (demandees is null || demandees.Count == 0) return toutes;
        var choisies = new List<TableSql>();
        foreach (var d in demandees)
        {
            var t = toutes.FirstOrDefault(t => string.Equals(t.Libelle, d, StringComparison.OrdinalIgnoreCase))
                    ?? toutes.FirstOrDefault(t => string.Equals(t.Nom, d, StringComparison.OrdinalIgnoreCase))
                    ?? throw new RefusAnonymisation($"Table « {d} » absente de la source.");
            if (!choisies.Contains(t)) choisies.Add(t);
        }
        return choisies;
    }

    private static bool MemeServeur(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Base absente : creee au classement de la source. Presente : doit etre VIDE (jamais ecrasee).</summary>
    private static async Task CreerBaseAsync(SqlConnectionStringBuilder cible, string collation, CancellationToken ct)
    {
        var master = new SqlConnectionStringBuilder(cible.ConnectionString) { InitialCatalog = "master" };
        await using var cn = new SqlConnection(master.ConnectionString);
        await cn.OpenAsync(ct);
        var nom = cible.InitialCatalog;
        await using (var existe = new SqlCommand("SELECT DB_ID(@n)", cn))
        {
            existe.Parameters.AddWithValue("@n", nom);
            if (await existe.ExecuteScalarAsync(ct) is DBNull or null)
            {
                var classement = collation.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && collation.Length > 0 ? $" COLLATE {collation}" : "";
                await ExecuterAsync(cn, $"CREATE DATABASE {Ddl.Q(nom)}{classement};", ct);
                return;
            }
        }
        await using var tables = new SqlCommand($"SELECT COUNT(*) FROM {Ddl.Q(nom)}.sys.tables WHERE is_ms_shipped = 0", cn);
        if ((int)(await tables.ExecuteScalarAsync(ct))! > 0)
            throw new RefusAnonymisation($"La base copie « {nom} » existe et n'est pas vide : l'outil n'ecrase jamais une base.");
    }

    private static async Task<long> CopierTableAsync(PorteSource src, SqlConnection cn, TableSql t, SchemaSource schema,
        CatalogueRegles catalogue, Anonymiseur anonymiseur, OptionsCopie o, Rapport rapport, CancellationToken ct)
    {
        var colonnes = t.Colonnes.Where(c => !c.Calculee).ToList();
        if (colonnes.Count == 0) return 0;
        var regles = colonnes.Select(c => catalogue.Pour(t.Nom, c.Nom, c.Type)).ToArray();
        var traitees = new ColonneTraitee?[colonnes.Count];
        for (var i = 0; i < colonnes.Count; i++)
            if (regles[i].Anonymise)
            {
                traitees[i] = new ColonneTraitee(t.Libelle, colonnes[i].Nom, colonnes[i].Type, colonnes[i].Longueur, regles[i]);
                rapport.Colonnes.Add(traitees[i]!);
            }
        // Les colonnes d'une signature (FACTU_SIGN_SEQ, GTPF_SIGNATURE…) : une ligne signee dont le contenu
        // change n'a plus une signature valable (CA05).
        var signatures = colonnes.Select((c, i) => (c, i))
            .Where(x => CatalogueRegles.Segments(x.c.Nom).Any(s => s is "SIGN" or "SIGNATURE"))
            .Select(x => x.i).ToArray();
        long signeesTouchees = 0;

        var cle = schema.Index.FirstOrDefault(i => i.Table == t.Id && i.Primaire)?.Colonnes.Where(c => !c.Inclus).Select(c => Ddl.Q(c.Colonne)).ToList();
        var sql = $"SELECT {(o.MaxLignes is { } m ? $"TOP ({m}) " : "")}{string.Join(", ", colonnes.Select(c => Ddl.Q(c.Nom)))} " +
                  $"FROM {t.Complet} WITH (NOLOCK)" +
                  (o.MaxLignes is not null && cle is { Count: > 0 } ? $" ORDER BY {string.Join(", ", cle)}" : "");

        var index = colonnes.Select((c, i) => (c.Nom, i)).ToDictionary(x => x.Nom, x => x.i, StringComparer.OrdinalIgnoreCase);
        long lignes = 0;
        await using var lecteur = await src.LireAsync(sql, ct);
        using var lot = new DataTable();
        for (var i = 0; i < lecteur.FieldCount; i++) lot.Columns.Add(colonnes[i].Nom, lecteur.GetFieldType(i));
        using var bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, null)
        {
            DestinationTableName = t.Complet,
            BulkCopyTimeout = 0,
            BatchSize = o.TailleLot,
        };
        foreach (var c in colonnes) bulk.ColumnMappings.Add(c.Nom, c.Nom);

        var valeurs = new object[colonnes.Count];
        while (await lecteur.ReadAsync(ct))
        {
            lecteur.GetValues(valeurs);
            var avant = (object[])valeurs.Clone();
            var change = false;
            for (var i = 0; i < valeurs.Length; i++)
            {
                if (traitees[i] is not { } traitee || avant[i] is DBNull) continue;
                traitee.Valeurs++;
                valeurs[i] = anonymiseur.Appliquer(regles[i], avant[i], colonnes[i].AccepteNull, n => index.TryGetValue(n, out var k) ? avant[k] : null);
                if (!Equals(valeurs[i], avant[i])) { traitee.Modifiees++; change = true; }
            }
            if (change && signatures.Any(s => avant[s] is not DBNull)) signeesTouchees++;
            lot.Rows.Add(valeurs);
            lignes++;
            if (lot.Rows.Count >= o.TailleLot)
            {
                await bulk.WriteToServerAsync(lot, ct);
                lot.Clear();
            }
        }
        if (lot.Rows.Count > 0) await bulk.WriteToServerAsync(lot, ct);
        if (signeesTouchees > 0) rapport.TablesSigneesTouchees.Add((t.Libelle, signeesTouchees));
        if (colonnes.Any(c => c.Type is "timestamp" or "rowversion"))
            rapport.Ignores.Add($"{t.Libelle} : colonne rowversion recopiee en binary(8) (une rowversion ne s'ecrit pas)");
        return lignes;
    }

    /// <summary>
    /// Vues, fonctions, procedures, declencheurs : rejoues avec leurs reglages de creation, en plusieurs
    /// passes (une vue peut dependre d'une autre). Ce qui depend d'une autre base (COMMUN_RIG) ou d'une
    /// table non copiee echoue : liste au rapport, sans arreter la copie.
    /// </summary>
    private static async Task CreerModulesAsync(SqlConnection cn, SchemaSource schema, List<TableSql> copiees, Rapport rapport, CancellationToken ct)
    {
        var ordre = new[] { "FN", "IF", "TF", "V", "P", "TR" };
        var restants = schema.Modules.OrderBy(m => Array.IndexOf(ordre, m.Type)).ToList();
        var erreurs = new Dictionary<string, string>();
        for (var passe = 0; passe < 5 && restants.Count > 0; passe++)
        {
            var encore = new List<ModuleSql>();
            foreach (var m in restants)
            {
                try
                {
                    await ExecuterAsync(cn, $"SET QUOTED_IDENTIFIER {(m.GuillemetsIdentifiants ? "ON" : "OFF")}; SET ANSI_NULLS {(m.AnsiNulls ? "ON" : "OFF")};", ct);
                    await ExecuterAsync(cn, m.Definition, ct);
                    rapport.ModulesCrees.Add(m.Libelle);
                }
                catch (SqlException e)
                {
                    erreurs[m.Libelle] = e.Message;
                    encore.Add(m);
                }
            }
            if (encore.Count == restants.Count) break;
            restants = encore;
        }
        await ExecuterAsync(cn, "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", ct);
        foreach (var m in restants) rapport.Ignores.Add($"{m.Libelle} ({m.Type}) : {erreurs[m.Libelle]}");
    }

    /// <summary>
    /// LA MARQUE (CA05) : schema « anonymisation » de la copie. Copie : une ligne (source, dates, chaines
    /// invalidees ou non) ; Colonne : la regle de chaque colonne traitee. Plus une propriete de la base,
    /// lisible sans connaitre ce schema. Un controle de chaines qui la trouve ne compte pas comme preuve.
    /// </summary>
    private static async Task MarquerAsync(SqlConnection cn, Rapport r, CancellationToken ct)
    {
        await ExecuterAsync(cn, $"IF SCHEMA_ID(N'{SchemaMarque}') IS NULL EXEC(N'CREATE SCHEMA {SchemaMarque}');", ct);
        await ExecuterAsync(cn, $"""
            CREATE TABLE {SchemaMarque}.Copie (
                Id int NOT NULL CONSTRAINT PK_anonymisation_Copie PRIMARY KEY CONSTRAINT CK_anonymisation_Copie_Une CHECK (Id = 1),
                Source nvarchar(300) NOT NULL, Debut datetime2(0) NOT NULL, Fin datetime2(0) NOT NULL,
                Reproductible bit NOT NULL, ChainesInvalidees bit NOT NULL, Motif nvarchar(200) NOT NULL,
                LignesSigneesTouchees bigint NOT NULL, TablesSigneesTouchees nvarchar(max) NOT NULL);
            CREATE TABLE {SchemaMarque}.Colonne (
                TableV2 nvarchar(300) NOT NULL, Colonne sysname NOT NULL, Traitement varchar(20) NOT NULL,
                Origine nvarchar(400) NOT NULL, Valeurs bigint NOT NULL, Modifiees bigint NOT NULL,
                CONSTRAINT PK_anonymisation_Colonne PRIMARY KEY (TableV2, Colonne));
            """, ct);
        await using (var cmd = new SqlCommand(
            $"INSERT {SchemaMarque}.Copie VALUES (1, @source, @debut, @fin, @repro, @inval, @motif, @lignes, @tables);", cn))
        {
            cmd.Parameters.AddWithValue("@source", r.Source);
            cmd.Parameters.AddWithValue("@debut", r.Debut);
            cmd.Parameters.AddWithValue("@fin", r.Fin);
            cmd.Parameters.AddWithValue("@repro", r.Reproductible);
            cmd.Parameters.AddWithValue("@inval", r.ChainesInvalidees);
            cmd.Parameters.AddWithValue("@motif", r.ChainesInvalidees ? Rapport.MotifChaines : "chaînes intactes : aucune ligne signée n'a changé");
            cmd.Parameters.AddWithValue("@lignes", r.LignesSigneesTouchees);
            cmd.Parameters.AddWithValue("@tables", string.Join(", ", r.TablesSigneesTouchees.Select(t => $"{t.Table} ({t.Lignes})")));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        foreach (var c in r.Colonnes)
        {
            await using var cmd = new SqlCommand($"INSERT {SchemaMarque}.Colonne VALUES (@t, @c, @tr, @o, @v, @m);", cn);
            cmd.Parameters.AddWithValue("@t", c.Table);
            cmd.Parameters.AddWithValue("@c", c.Colonne);
            cmd.Parameters.AddWithValue("@tr", c.Regle.Traitement.ToString());
            cmd.Parameters.AddWithValue("@o", c.Regle.Origine.Length > 400 ? c.Regle.Origine[..400] : c.Regle.Origine);
            cmd.Parameters.AddWithValue("@v", c.Valeurs);
            cmd.Parameters.AddWithValue("@m", c.Modifiees);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using (var p = new SqlCommand("EXEC sys.sp_addextendedproperty @name = @n, @value = @v;", cn))
        {
            p.Parameters.AddWithValue("@n", ProprieteBase);
            p.Parameters.AddWithValue("@v", r.ChainesInvalidees ? Rapport.MotifChaines : "copie anonymisée, chaînes intactes");
            await p.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task ExecuterAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
