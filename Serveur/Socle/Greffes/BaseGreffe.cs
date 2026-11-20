using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using RigV3.Base;

namespace RigV3.Serveur.Socle.Greffes;

// ============================================================
// LA BASE V3 DE CHAQUE GREFFE, A COTE DE SA BASE V2 (SOC-008, lot 4).
//
// CreerAsync, sur l'instance qui porte la base V2 (decision Q4) :
//  1. refuse une instance anterieure a SQL Server 2017 (CA05) ;
//  2. exige la base V2 sur CETTE instance, en lit la collation et le mode de
//     recuperation : la base V3 prend les memes (T01) ;
//  3. refuse d'ecraser une base qui existe deja ;
//  4. cree la base, ses fichiers et leur taille initiale dans les dossiers par defaut
//     de l'instance ;
//  5. y joue les migrations du depot : la version de schema est inscrite au journal
//     rigv3.Migration (lot 1) -- 40 bases, un seul schema (CA02) ;
//  6. y declare les comptes de la partie serveur, de la recopie et de l'exploitation
//     dans leurs roles (SOC-008_acces.sql) ; aucun compte d'agent (CA03).
// Un echec apres la creation supprime la base QUE CET APPEL vient de creer, vide : il
// ne reste pas de demi-base.
//
// Et les controles d'exploitation : schema compare a la version livree (CA02, T05),
// sauvegardes au moins aussi recentes que celles de la V2 et restauration d'essai
// (CA04), place disque sous un seuil (CA06), texte des migrations sans fonction
// absente de SQL Server 2017 (CA05).
// ============================================================

/// <summary>Les comptes (connexions SQL Server, ex. « AMITEL\svc-rigv3 ») qui ecrivent dans la base V3.</summary>
public sealed record AccesBaseV3(IReadOnlyList<string> Serveur, IReadOnlyList<string> Recopie, IReadOnlyList<string> Exploitation)
{
    public static readonly AccesBaseV3 Aucun = new([], [], []);
}

/// <summary>Taille initiale et croissance des fichiers, en Mo.</summary>
public sealed record TaillesBaseV3(int DonneesMo = 256, int JournalMo = 64, int CroissanceDonneesMo = 256, int CroissanceJournalMo = 64);

/// <summary>Ce que la creation a fait.</summary>
public sealed record BaseV3Creee(string Base, string BaseV2, string Collation, string Recuperation, int VersionInstance,
                                 int MigrationsJouees, string? DerniereMigration);

/// <summary>L'etat d'une base V3 avant une repetition generale (SOC-008 T05).</summary>
public sealed record VerificationBaseV3(bool Existe, bool Vide, int MigrationsJouees, string? DerniereMigration,
                                        IReadOnlyList<string> Problemes)
{
    public bool Prete => Problemes.Count == 0;
}

/// <summary>Derniere sauvegarde de chaque type (D complete, I differentielle, L journal) de la V2 et de la V3 (CA04).</summary>
public sealed record ControleSauvegarde(IReadOnlyDictionary<string, DateTime> V2, IReadOnlyDictionary<string, DateTime> V3,
                                        IReadOnlyList<string> Retards)
{
    public bool Conforme => Retards.Count == 0;
}

/// <summary>Une restauration d'essai : reussie, et le schema restaure est-il celui de la source ?</summary>
public sealed record RestaurationEssai(string Base, string BaseEssai, bool SchemaIdentique, TimeSpan Duree);

/// <summary>Un volume disque qui porte des fichiers des bases V2 / V3 d'un greffe (CA06).</summary>
public sealed record VolumeDisque(string Volume, long TotalOctets, long LibreOctets, IReadOnlyList<string> Bases, bool Alerte);

public static partial class BaseGreffe
{
    /// <summary>SQL Server 2017 = version 14 (bases-et-roles.md:25 ; SQL-DEV en 14.0.3485.1, lu le 2026-11-20).</summary>
    public const int VersionMinimale = 14;

    /// <summary>Cree la base V3 du greffe sur l'instance de sa base V2, puis l'inscrit au catalogue (CA01).</summary>
    public static async Task<BaseV3Creee> CreerPourGreffeAsync(CatalogueGreffes catalogue, string code, string dossierMigrations,
                                                               AccesBaseV3 acces, TaillesBaseV3 tailles, string auteur,
                                                               CancellationToken ct = default)
    {
        var fiche = await catalogue.LireAsync(code, ct) ?? throw new GreffeInconnu(code);
        var creee = await CreerAsync(catalogue.ConnexionBase(fiche.Serveur, "master"), fiche.BaseV2, dossierMigrations,
                                     acces, tailles, ct);
        await catalogue.InscrireBaseV3Async(code, creee.Base, auteur, ct);
        return creee;
    }

    /// <summary>Cree la base V3 a cote de <paramref name="baseV2"/>, sur l'instance de <paramref name="connexionInstance"/>.</summary>
    public static async Task<BaseV3Creee> CreerAsync(string connexionInstance, string baseV2, string dossierMigrations,
                                                     AccesBaseV3 acces, TaillesBaseV3 tailles, CancellationToken ct = default)
    {
        var nom = CatalogueGreffes.NomBaseV3(baseV2);
        await using var master = await OuvrirAsync(connexionInstance, "master", ct);

        var version = Convert.ToInt32(await ScalaireAsync(master, "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion'));", ct));
        if (version < VersionMinimale)
            throw new InvalidOperationException(
                $"Instance en version {version} : la V3 demande SQL Server 2017 (14) au moins. Monter l'instance de version avant (SOC-008 CA05).");

        string collation, recuperation;
        await using (var cmd = new SqlCommand("SELECT collation_name, recovery_model_desc FROM sys.databases WHERE name = @n;", master))
        {
            cmd.Parameters.Add(new SqlParameter("@n", SqlDbType.NVarChar, 128) { Value = baseV2 });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                throw new InvalidOperationException(
                    $"La base V2 {baseV2} n'est pas sur cette instance : la base V3 se cree A COTE d'elle, et nulle part ailleurs (SOC-008 CA01).");
            collation = r.GetString(0);
            recuperation = r.GetString(1);
        }
        if (!CollationValide().IsMatch(collation) || recuperation is not ("FULL" or "SIMPLE" or "BULK_LOGGED"))
            throw new InvalidOperationException($"Collation ou mode de recuperation inattendu pour {baseV2} : {collation} / {recuperation}.");

        if (await ScalaireAsync(master, $"SELECT DB_ID(N'{Litteral(nom)}');", ct) is not null)
            throw new InvalidOperationException(
                $"La base {nom} existe deja : elle ne se recree pas par-dessus (verifier son etat avec VerifierAsync).");

        var donnees = (string)(await ScalaireAsync(master, "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultDataPath'));", ct))!;
        var journal = (string)(await ScalaireAsync(master, "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultLogPath'));", ct))!;
        await ExecuterAsync(master,
            $"CREATE DATABASE {Crochets(nom)} ON PRIMARY " +
            $"(NAME = N'{Litteral(nom)}', FILENAME = N'{Litteral(donnees + nom)}.mdf', SIZE = {tailles.DonneesMo}MB, FILEGROWTH = {tailles.CroissanceDonneesMo}MB) " +
            $"LOG ON (NAME = N'{Litteral(nom)}_log', FILENAME = N'{Litteral(journal + nom)}_log.ldf', SIZE = {tailles.JournalMo}MB, FILEGROWTH = {tailles.CroissanceJournalMo}MB) " +
            $"COLLATE {collation};", ct);
        try
        {
            await ExecuterAsync(master, $"ALTER DATABASE {Crochets(nom)} SET RECOVERY {recuperation};", ct);
            var connexionBase = new SqlConnectionStringBuilder(connexionInstance) { InitialCatalog = nom }.ConnectionString;
            await Migrateur.AppliquerAsync(connexionBase, dossierMigrations, null, ct);
            await using (var cn = await OuvrirAsync(connexionInstance, nom, ct))
            {
                foreach (var (role, comptes) in new[] { ("rigv3_serveur", acces.Serveur), ("rigv3_recopie", acces.Recopie),
                                                        ("rigv3_exploitation", acces.Exploitation) })
                    foreach (var compte in comptes)
                        await ExecuterAsync(cn,
                            $"IF USER_ID(N'{Litteral(compte)}') IS NULL CREATE USER {Crochets(compte)} FROM LOGIN {Crochets(compte)}; " +
                            $"ALTER ROLE {role} ADD MEMBER {Crochets(compte)};", ct);
            }
            var (jouees, derniere) = await EtatAsync(connexionBase, ct);
            return new BaseV3Creee(nom, baseV2, collation, recuperation, version, jouees, derniere);
        }
        catch
        {
            // La base est celle que CET appel vient de creer, encore vide : on ne laisse pas de demi-base.
            SqlConnection.ClearAllPools();
            await ExecuterAsync(master,
                $"IF DB_ID(N'{Litteral(nom)}') IS NOT NULL BEGIN ALTER DATABASE {Crochets(nom)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE {Crochets(nom)}; END", CancellationToken.None);
            throw;
        }
    }

    // --- CA02 / T05 : le schema ------------------------------------------------

    /// <summary>
    /// Le schema d'une base, une ligne par objet : classement de la base, schemas, tables,
    /// colonnes (type, taille, nullite, identite, classement REEL, defaut), index, cles,
    /// contraintes, definitions des procedures, vues, fonctions et declencheurs, roles et
    /// leurs droits. Les noms donnes par SQL Server lui-meme sont neutralises. Trie : deux
    /// bases au meme schema donnent la meme liste.
    /// Le classement d'une colonne est note tel quel : SQL Server ne garde pas la trace d'un
    /// COLLATE ecrit dans la migration, on ne peut donc pas le separer de celui herite de la
    /// base. Une base V3 se compare a la version livree AU MEME classement de base (une base V3
    /// prend celui de sa base V2, SOC-008 T01).
    /// </summary>
    public static async Task<IReadOnlyList<string>> SchemaAsync(string connexionBase, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connexionBase);
        await cn.OpenAsync(ct);
        var lignes = new List<string>();
        foreach (var requete in RequetesSchema)
        {
            await using var cmd = new SqlCommand(requete, cn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) lignes.Add(Regex.Replace(r.GetString(0), @"\s+", " ").Trim());
        }
        lignes.Sort(StringComparer.Ordinal);
        return lignes;
    }

    /// <summary>L'empreinte SHA-256 du schema (<see cref="SchemaAsync"/>).</summary>
    public static async Task<string> EmpreinteSchemaAsync(string connexionBase, CancellationToken ct = default)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", await SchemaAsync(connexionBase, ct)))));

    // fix-ok: erreur 451 mesuree au premier banc -- les colonnes *_desc et permission_name des vues systeme
    // sont en Latin1_General_CI_AS_KS_WS ; collees a un nom (classement de la base) sans COLLATE DATABASE_DEFAULT,
    // SQL Server refuse la concatenation.
    // fix-ok: CA02 rouge mesure sur main + ce lot (2026-11-20) -- SOC-001_comptes.sql declare
    // CompteWindows COLLATE SQL_Latin1_General_CP1_CI_AS en clair ; noter le classement « seulement s'il
    // differe de celui de la base » le faisait apparaitre dans une base French_CI_AS et pas dans la
    // reference : la normalisation etait fausse par construction, le classement est maintenant note tel quel.
    private static readonly string[] RequetesSchema =
    [
        "SELECT 'classement de la base ' + CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'))",
        "SELECT 'schema ' + name FROM sys.schemas WHERE schema_id > 4 AND schema_id < 16384",
        "SELECT 'table ' + SCHEMA_NAME(schema_id) + '.' + name FROM sys.tables WHERE is_ms_shipped = 0",
        """
        SELECT 'colonne ' + SCHEMA_NAME(t.schema_id) + '.' + t.name + '.' + c.name + ' ' + ty.name
             + '(' + CONVERT(varchar(10), c.max_length) + ',' + CONVERT(varchar(10), c.precision) + ',' + CONVERT(varchar(10), c.scale) + ')'
             + CASE WHEN c.is_nullable = 1 THEN ' null' ELSE ' not null' END
             + CASE WHEN c.is_identity = 1 THEN ' identity' ELSE '' END
             + ISNULL(' collate ' + c.collation_name, '')
             + ISNULL(' default ' + dc.definition, '')
             + ISNULL(' calcul ' + cc.definition, '')
             + ' rang ' + CONVERT(varchar(10), c.column_id)
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id AND t.is_ms_shipped = 0
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
        LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
        """,
        """
        SELECT 'index ' + SCHEMA_NAME(t.schema_id) + '.' + t.name + ' '
             + CASE WHEN kc.is_system_named = 1 THEN '(nom auto)' ELSE i.name END + ' ' + i.type_desc COLLATE DATABASE_DEFAULT
             + CASE WHEN i.is_unique = 1 THEN ' unique' ELSE '' END + CASE WHEN i.is_primary_key = 1 THEN ' cle' ELSE '' END
             + ' (' + STUFF((SELECT ',' + col.name + CASE WHEN ic.is_descending_key = 1 THEN ' desc' ELSE '' END
                                  + CASE WHEN ic.is_included_column = 1 THEN ' inclus' ELSE '' END
                             FROM sys.index_columns ic JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                             WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                             ORDER BY ic.is_included_column, ic.key_ordinal, col.name FOR XML PATH('')), 1, 1, '') + ')'
             + ISNULL(' filtre ' + i.filter_definition, '')
        FROM sys.indexes i
        JOIN sys.tables t ON t.object_id = i.object_id AND t.is_ms_shipped = 0
        LEFT JOIN sys.key_constraints kc ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
        WHERE i.type > 0
        """,
        """
        SELECT 'verification ' + SCHEMA_NAME(t.schema_id) + '.' + t.name + ' '
             + CASE WHEN ck.is_system_named = 1 THEN '(nom auto)' ELSE ck.name END + ' ' + ck.definition
        FROM sys.check_constraints ck JOIN sys.tables t ON t.object_id = ck.parent_object_id
        """,
        """
        SELECT 'reference ' + SCHEMA_NAME(fk.schema_id) + '.' + OBJECT_NAME(fk.parent_object_id) + ' '
             + CASE WHEN fk.is_system_named = 1 THEN '(nom auto)' ELSE fk.name END + ' -> '
             + OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) + ' ('
             + STUFF((SELECT ',' + COL_NAME(fkc.parent_object_id, fkc.parent_column_id) + '=' + COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id)
                      FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id = fk.object_id
                      ORDER BY fkc.constraint_column_id FOR XML PATH('')), 1, 1, '') + ')'
             + ' ' + fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT + ' ' + fk.update_referential_action_desc COLLATE DATABASE_DEFAULT
        FROM sys.foreign_keys fk
        """,
        """
        SELECT 'module ' + o.type_desc COLLATE DATABASE_DEFAULT + ' ' + SCHEMA_NAME(o.schema_id) + '.' + o.name + ' ' + m.definition
        FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id WHERE o.is_ms_shipped = 0
        """,
        """
        SELECT 'role ' + name FROM sys.database_principals WHERE type = 'R' AND is_fixed_role = 0 AND name <> 'public'
        """,
        """
        SELECT 'membre ' + r.name + ' <- ' + m.name
        FROM sys.database_role_members rm
        JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
        JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
        WHERE m.type = 'R'
        """,
        """
        SELECT 'droit ' + p.state_desc COLLATE DATABASE_DEFAULT + ' ' + p.permission_name COLLATE DATABASE_DEFAULT
             + ' ' + p.class_desc COLLATE DATABASE_DEFAULT
             + ISNULL(' ' + OBJECT_SCHEMA_NAME(p.major_id) + '.' + OBJECT_NAME(p.major_id), '') + ' -> ' + g.name
        FROM sys.database_permissions p
        JOIN sys.database_principals g ON g.principal_id = p.grantee_principal_id
        WHERE g.type = 'R' AND g.is_fixed_role = 0 AND g.name <> 'public'
        """,
    ];

    /// <summary>
    /// Avant une repetition generale (T05) : la base existe, elle est vide de donnees (hors
    /// journal des migrations), elle porte la derniere migration attendue et le schema livre.
    /// </summary>
    public static async Task<VerificationBaseV3> VerifierAsync(string connexionBase, IReadOnlyList<string> schemaLivre,
                                                               string? derniereMigrationAttendue, CancellationToken ct = default)
    {
        var nom = new SqlConnectionStringBuilder(connexionBase).InitialCatalog;
        await using (var master = await OuvrirAsync(connexionBase, "master", ct))
            if (await ScalaireAsync(master, $"SELECT DB_ID(N'{Litteral(nom)}');", ct) is null)
                return new VerificationBaseV3(false, false, 0, null, [$"la base {nom} n'existe pas"]);

        var problemes = new List<string>();
        var (jouees, derniere) = await EtatAsync(connexionBase, ct);
        if (derniere != derniereMigrationAttendue)
            problemes.Add($"version de schema : derniere migration {derniere ?? "(aucune)"}, attendue {derniereMigrationAttendue ?? "(aucune)"}");

        long lignes;
        await using (var cn = new SqlConnection(connexionBase))
        {
            await cn.OpenAsync(ct);
            lignes = Convert.ToInt64(await ScalaireAsync(cn,
                "SELECT ISNULL(SUM(p.rows), 0) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id " +
                "WHERE p.index_id IN (0, 1) AND t.is_ms_shipped = 0 AND NOT (SCHEMA_NAME(t.schema_id) = 'rigv3' AND t.name = 'Migration');", ct));
        }
        if (lignes > 0) problemes.Add($"la base n'est pas vide : {lignes} ligne(s) de donnees");

        var schema = await SchemaAsync(connexionBase, ct);
        var enTrop = schema.Except(schemaLivre, StringComparer.Ordinal).ToList();
        var manquants = schemaLivre.Except(schema, StringComparer.Ordinal).ToList();
        if (enTrop.Count + manquants.Count > 0)
            problemes.Add($"schema different de la version livree : {enTrop.Count} en trop, {manquants.Count} manquant(s)" +
                          (enTrop.Count > 0 ? $" ; ex. en trop « {enTrop[0]} »" : "") +
                          (manquants.Count > 0 ? $" ; ex. manquant « {manquants[0]} »" : ""));
        return new VerificationBaseV3(true, lignes == 0, jouees, derniere, problemes);
    }

    // --- CA04 : sauvegardes et restauration d'essai ------------------------------

    /// <summary>
    /// Pour chaque type de sauvegarde que la V2 recoit (complete, differentielle, journal),
    /// la V3 en a-t-elle une au moins aussi recente ? (historique msdb de l'instance ; les
    /// copies « COPY_ONLY », hors plan, ne comptent pas.)
    /// </summary>
    public static async Task<ControleSauvegarde> ControlerSauvegardesAsync(string connexionInstance, string baseV2, string baseV3,
                                                                          CancellationToken ct = default)
    {
        await using var cn = await OuvrirAsync(connexionInstance, "msdb", ct);
        async Task<Dictionary<string, DateTime>> Dernieres(string nomBase)
        {
            await using var cmd = new SqlCommand(
                "SELECT type, MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = @b AND is_copy_only = 0 GROUP BY type;", cn);
            cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.NVarChar, 128) { Value = nomBase });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var d = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            while (await r.ReadAsync(ct)) d[r.GetString(0)] = r.GetDateTime(1);
            return d;
        }
        var v2 = await Dernieres(baseV2);
        var v3 = await Dernieres(baseV3);
        var retards = new List<string>();
        foreach (var (type, date) in v2)
        {
            var libelle = type switch { "D" => "complete", "I" => "differentielle", "L" => "du journal", _ => type };
            if (!v3.TryGetValue(type, out var d3))
                retards.Add($"sauvegarde {libelle} : la V2 en a une ({date:dd/MM/yyyy HH:mm}), la V3 aucune");
            else if (d3 < date)
                retards.Add($"sauvegarde {libelle} : V3 du {d3:dd/MM/yyyy HH:mm}, plus ancienne que la V2 ({date:dd/MM/yyyy HH:mm})");
        }
        return new ControleSauvegarde(v2, v3, retards);
    }

    /// <summary>
    /// Restauration d'essai : sauvegarde « COPY_ONLY » (hors plan) de <paramref name="nomBase"/>
    /// dans <paramref name="fichier"/>, restauree sous <paramref name="nomEssai"/> sur l'instance
    /// cible (le fichier doit lui etre lisible), schema compare, puis base d'essai supprimee.
    /// </summary>
    public static async Task<RestaurationEssai> RestaurerEssaiAsync(string connexionSource, string nomBase, string connexionCible,
                                                                    string nomEssai, string fichier, CancellationToken ct = default)
    {
        var debut = DateTime.UtcNow;
        await using (var source = await OuvrirAsync(connexionSource, "master", ct))
        await using (var cmd = new SqlCommand("BACKUP DATABASE @b TO DISK = @f WITH COPY_ONLY, INIT, CHECKSUM;", source) { CommandTimeout = 0 })
        {
            cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.NVarChar, 128) { Value = nomBase });
            cmd.Parameters.Add(new SqlParameter("@f", SqlDbType.NVarChar, 260) { Value = fichier });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using var cible = await OuvrirAsync(connexionCible, "master", ct);
        if (await ScalaireAsync(cible, $"SELECT DB_ID(N'{Litteral(nomEssai)}');", ct) is not null)
            throw new InvalidOperationException($"La base d'essai {nomEssai} existe deja sur l'instance cible : choisir un autre nom.");
        var donnees = (string)(await ScalaireAsync(cible, "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultDataPath'));", ct))!;
        var journal = (string)(await ScalaireAsync(cible, "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultLogPath'));", ct))!;
        var deplacements = new List<string>();
        await using (var cmd = new SqlCommand("RESTORE FILELISTONLY FROM DISK = @f;", cible))
        {
            cmd.Parameters.Add(new SqlParameter("@f", SqlDbType.NVarChar, 260) { Value = fichier });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var n = 0;
            while (await r.ReadAsync(ct))
            {
                var logique = r.GetString(r.GetOrdinal("LogicalName"));
                var estJournal = r.GetString(r.GetOrdinal("Type")) == "L";
                var physique = (estJournal ? journal : donnees) + nomEssai + "_" + (n++) + (estJournal ? ".ldf" : ".mdf");
                deplacements.Add($"MOVE N'{Litteral(logique)}' TO N'{Litteral(physique)}'");
            }
        }
        await using (var cmd = new SqlCommand(
            $"RESTORE DATABASE {Crochets(nomEssai)} FROM DISK = @f WITH {string.Join(", ", deplacements)}, CHECKSUM, RECOVERY;", cible)
            { CommandTimeout = 0 })
        {
            cmd.Parameters.Add(new SqlParameter("@f", SqlDbType.NVarChar, 260) { Value = fichier });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        try
        {
            var avant = await SchemaAsync(new SqlConnectionStringBuilder(connexionSource) { InitialCatalog = nomBase }.ConnectionString, ct);
            var apres = await SchemaAsync(new SqlConnectionStringBuilder(connexionCible) { InitialCatalog = nomEssai }.ConnectionString, ct);
            return new RestaurationEssai(nomBase, nomEssai, avant.SequenceEqual(apres, StringComparer.Ordinal), DateTime.UtcNow - debut);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await ExecuterAsync(cible,
                $"IF DB_ID(N'{Litteral(nomEssai)}') IS NOT NULL BEGIN ALTER DATABASE {Crochets(nomEssai)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE {Crochets(nomEssai)}; END", CancellationToken.None);
        }
    }

    // --- CA06 : place disque ------------------------------------------------------

    /// <summary>Les volumes qui portent les fichiers des bases donnees ; alerte si la place libre
    /// descend sous <paramref name="seuilOctets"/> (la V3 s'ajoute a la V2 sur les memes disques).</summary>
    public static async Task<IReadOnlyList<VolumeDisque>> PlaceDisqueAsync(string connexionInstance, IReadOnlyList<string> bases,
                                                                          long seuilOctets, CancellationToken ct = default)
    {
        await using var cn = await OuvrirAsync(connexionInstance, "master", ct);
        var parVolume = new Dictionary<string, (long Total, long Libre, SortedSet<string> Bases)>(StringComparer.OrdinalIgnoreCase);
        foreach (var nomBase in bases)
        {
            await using var cmd = new SqlCommand(
                // SQL Server sous Linux (le conteneur de developpement) ne donne ni point de montage
                // ni identifiant de volume, seulement les tailles (mesure le 2026-11-20) : le volume
                // prend alors le nom du dossier du fichier. Sous Windows (les instances des greffes),
                // c'est la lettre du disque (« D:\ »).
                "SELECT COALESCE(vs.volume_mount_point, vs.volume_id, " +
                "LEFT(f.physical_name, LEN(f.physical_name) - CHARINDEX('/', REVERSE(REPLACE(f.physical_name, '\\', '/'))) + 1)), " +
                "vs.total_bytes, vs.available_bytes FROM sys.master_files f " +
                "CROSS APPLY sys.dm_os_volume_stats(f.database_id, f.file_id) vs WHERE f.database_id = DB_ID(@b);", cn);
            cmd.Parameters.Add(new SqlParameter("@b", SqlDbType.NVarChar, 128) { Value = nomBase });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var trouve = false;
            while (await r.ReadAsync(ct))
            {
                trouve = true;
                var volume = r.GetString(0);
                if (!parVolume.TryGetValue(volume, out var v)) parVolume[volume] = v = (r.GetInt64(1), r.GetInt64(2), new SortedSet<string>(StringComparer.Ordinal));
                v.Bases.Add(nomBase);
            }
            if (!trouve) throw new InvalidOperationException($"La base {nomBase} n'est pas sur cette instance.");
        }
        return [.. parVolume.Select(p => new VolumeDisque(p.Key, p.Value.Total, p.Value.Libre, [.. p.Value.Bases], p.Value.Libre < seuilOctets))];
    }

    // --- CA05 : rien d'absent de SQL Server 2017 ------------------------------------

    /// <summary>
    /// Les fonctions et syntaxes d'un script SQL absentes de SQL Server 2017 (14.x), avec
    /// leur version d'apparition. Un niveau de compatibilite 140 sur un moteur recent NE les
    /// refuse PAS toutes (mesure le 2026-11-20 sur SQL Server 2022, niveau 140 : GREATEST et
    /// IS DISTINCT FROM passent, GENERATE_SERIES est refuse) : d'ou ce controle du texte.
    /// Sources : learn.microsoft.com, « What's new in SQL Server 2019 » et « ... 2022 ».
    /// </summary>
    public static IReadOnlyList<string> AbsentDeSqlServer2017(string texteSql)
    {
        var sansCommentaires = Regex.Replace(Regex.Replace(texteSql, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"--[^\n]*", " ");
        return [.. Absents.Where(a => a.Motif.IsMatch(sansCommentaires)).Select(a => a.Libelle)];
    }

    private static readonly (Regex Motif, string Libelle)[] Absents =
    [
        (Mot(@"GREATEST\s*\("), "GREATEST (SQL Server 2022)"),
        (Mot(@"LEAST\s*\("), "LEAST (SQL Server 2022)"),
        (Mot(@"GENERATE_SERIES\s*\("), "GENERATE_SERIES (SQL Server 2022)"),
        (Mot(@"DATETRUNC\s*\("), "DATETRUNC (SQL Server 2022)"),
        (Mot(@"DATE_BUCKET\s*\("), "DATE_BUCKET (SQL Server 2022)"),
        (Mot(@"IS\s+(NOT\s+)?DISTINCT\s+FROM"), "IS [NOT] DISTINCT FROM (SQL Server 2022)"),
        (Mot(@"JSON_(OBJECT|ARRAY|PATH_EXISTS)\s*\("), "JSON_OBJECT / JSON_ARRAY / JSON_PATH_EXISTS (SQL Server 2022)"),
        (Mot(@"APPROX_PERCENTILE_(CONT|DISC)"), "APPROX_PERCENTILE_CONT / _DISC (SQL Server 2022)"),
        (Mot(@"(LEFT_SHIFT|RIGHT_SHIFT|BIT_COUNT|GET_BIT|SET_BIT)\s*\("), "fonctions de bits (SQL Server 2022)"),
        (Mot(@"WINDOW\s+\w+\s+AS\s*\("), "clause WINDOW (SQL Server 2022)"),
        (Mot(@"LEDGER\s*=\s*ON"), "tables LEDGER (SQL Server 2022)"),
        (Mot(@"TRIM\s*\(\s*(LEADING|TRAILING|BOTH)"), "TRIM LEADING / TRAILING / BOTH (SQL Server 2022)"),
        (Mot(@"[LR]TRIM\s*\([^()]*,"), "LTRIM / RTRIM a deux arguments (SQL Server 2022)"),
        (Mot(@"STRING_SPLIT\s*\([^()]*,[^()]*,"), "STRING_SPLIT avec rang (SQL Server 2022)"),
        (Mot(@"APPROX_COUNT_DISTINCT\s*\("), "APPROX_COUNT_DISTINCT (SQL Server 2019)"),
        (Mot(@"OPTIMIZE_FOR_SEQUENTIAL_KEY"), "OPTIMIZE_FOR_SEQUENTIAL_KEY (SQL Server 2019)"),
        (new Regex(@"_UTF8\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "collation UTF-8 (SQL Server 2019)"),
    ];

    private static Regex Mot(string motif) => new(@"\b" + motif, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // --- outils -----------------------------------------------------------------

    [GeneratedRegex(@"^[A-Za-z0-9_]+$")]
    private static partial Regex CollationValide();

    private static string Crochets(string nom) => "[" + nom.Replace("]", "]]") + "]";

    private static string Litteral(string texte) => texte.Replace("'", "''");

    private static async Task<(int Jouees, string? Derniere)> EtatAsync(string connexionBase, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connexionBase);
        await cn.OpenAsync(ct);
        return await Migrateur.EtatJournalAsync(cn, ct);
    }

    private static async Task<SqlConnection> OuvrirAsync(string connexionInstance, string nomBase, CancellationToken ct)
    {
        var cn = new SqlConnection(new SqlConnectionStringBuilder(connexionInstance) { InitialCatalog = nomBase }.ConnectionString);
        try
        {
            await cn.OpenAsync(ct);
            return cn;
        }
        catch
        {
            await cn.DisposeAsync();
            throw;
        }
    }

    private static async Task<object?> ScalaireAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn);
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is DBNull ? null : v;
    }

    private static async Task ExecuterAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 300 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
