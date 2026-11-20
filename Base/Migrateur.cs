using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace RigV3.Base;

// ============================================================
// LE MIGRATEUR de la base V3 (lot 1 du plan de reconstruction, 2026-11-20).
//
// Une migration = UN fichier .sql de Base/migrations, nomme par la story du
// carnet qui l'exige : « SOC-001_comptes.sql » (story SOC-001, libelle
// « comptes »). Pourquoi un fichier par story, sans liste a tenir : plusieurs
// lots ecrivent leurs migrations en meme temps ; une liste commune serait le
// fichier ou ils se marchent dessus.
//
// L'ORDRE. Le nom ne donne pas l'ordre (« NUM-001 » passe avant « SOC-001 »
// dans l'alphabet, alors que la numerotation s'appuie sur le socle). Une
// migration qui a besoin d'une autre le DIT dans son en-tete :
//     -- apres: SOC-001, SOC-007_catalogue
// (un identifiant de story vise toutes ses migrations ; un nom complet, une
// seule). L'outil range le tout (tri topologique) et departage les ex aequo
// par le nom : le meme dossier donne toujours le meme ordre. Une dependance
// inconnue ou circulaire arrete tout, avant d'avoir rien joue.
//
// LE JOURNAL. Chaque migration jouee est inscrite dans rigv3.Migration (son
// nom, l'empreinte SHA-256 de son texte, son rang, sa date, sa duree), dans la
// MEME transaction que son script : jouee et inscrite, ou ni l'un ni l'autre.
// Une migration deja jouee ne se modifie plus : si son texte a change, l'outil
// refuse de continuer (on ecrit une nouvelle migration a la place).
//
// LE SCHEMA DU THEME. Les tables d'un theme vivent dans le schema SQL de son
// prefixe en minuscules (SOC -> soc.Compte, NUM -> num.Compteur). L'outil cree
// ce schema s'il manque, avant le script : aucun lot n'a a se demander lequel
// des deux le cree.
//
// LES LOTS « GO ». Un script peut couper ses lots par une ligne « GO » (seule
// sur sa ligne), comme sous SSMS : CREATE PROCEDURE doit ouvrir son lot.
// ============================================================

/// <summary>Une migration lue sur le disque.</summary>
/// <param name="Id">Nom du fichier sans « .sql » : « SOC-001_comptes ».</param>
/// <param name="Story">Story du carnet : « SOC-001 ».</param>
/// <param name="Schema">Schema SQL du theme : « soc ».</param>
/// <param name="Apres">References de l'en-tete « -- apres: », telles qu'ecrites.</param>
public sealed record Migration(string Id, string Story, string Schema, string Chemin,
                               string Texte, string Empreinte, IReadOnlyList<string> Apres);

/// <summary>Ce qu'un passage du migrateur a fait.</summary>
/// <param name="Jouees">Migrations jouees par CE passage, dans leur ordre.</param>
/// <param name="DejaJouees">Migrations du dossier deja inscrites au journal.</param>
/// <param name="Absentes">Migrations du journal dont le fichier n'est plus dans le dossier.</param>
public sealed record RapportMigrations(IReadOnlyList<string> Jouees, IReadOnlyList<string> DejaJouees,
                                       IReadOnlyList<string> Absentes);

/// <summary>Une migration refusee ou en echec : le message nomme le fichier et la cause.</summary>
public sealed class ErreurMigration(string message, Exception? cause = null) : Exception(message, cause);

public static class Migrateur
{
    /// <summary>Le journal des migrations jouees, dans chaque base V3.</summary>
    public const string TableJournal = "rigv3.Migration";

    /// <summary>« SOC-001_comptes » : story (2 a 5 majuscules, tiret, 3 chiffres), « _ », libelle en minuscules.</summary>
    private static readonly Regex NomValide =
        new(@"^(?<story>(?<theme>[A-Z]{2,5})-[0-9]{3})_[a-z0-9][a-z0-9_-]*$", RegexOptions.CultureInvariant);

    private static readonly Regex LigneApres =
        new(@"^\s*--\s*apres\s*:(?<refs>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LigneGo =
        new(@"^[ \t]*GO[ \t]*(--[^\n]*)?$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Lit les migrations du dossier et les rend dans l'ordre ou elles se jouent.</summary>
    /// <exception cref="ErreurMigration">Nom invalide, dependance inconnue ou circulaire.</exception>
    public static IReadOnlyList<Migration> Lire(string dossier)
    {
        if (!Directory.Exists(dossier))
            throw new ErreurMigration($"Dossier des migrations introuvable : {dossier}");

        var migrations = new List<Migration>();
        foreach (var chemin in Directory.GetFiles(dossier, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(chemin);
            var nom = NomValide.Match(id);
            if (!nom.Success)
                throw new ErreurMigration(
                    $"{Path.GetFileName(chemin)} : nom refuse. Forme attendue « <STORY>_<libelle>.sql », " +
                    "ex. « SOC-001_comptes.sql » (story du carnet, puis un libelle en minuscules, chiffres, - ou _).");
            var texte = Normaliser(File.ReadAllText(chemin, Encoding.UTF8));
            migrations.Add(new Migration(id, nom.Groups["story"].Value, nom.Groups["theme"].Value.ToLowerInvariant(),
                                         chemin, texte, Empreinte(texte), LireApres(texte)));
        }
        return Ordonner(migrations);
    }

    /// <summary>
    /// Joue sur la base <paramref name="connexion"/> les migrations du dossier qui
    /// ne sont pas au journal, dans l'ordre. S'arrete a la premiere en echec
    /// (elle est annulee en entier) en levant <see cref="ErreurMigration"/>.
    /// </summary>
    public static async Task<RapportMigrations> AppliquerAsync(string connexion, string dossier,
                                                               Action<string>? journal = null,
                                                               CancellationToken ct = default)
    {
        var migrations = Lire(dossier);
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync(ct);

        // Deux outils lances en meme temps sur la meme base (deux deploiements) :
        // le second attend que le premier ait fini, au lieu de jouer les memes scripts.
        await Executer(cn, null, "EXEC sp_getapplock @Resource = N'rigv3-migrations', @LockMode = N'Exclusive', " +
                                 "@LockOwner = N'Session', @LockTimeout = -1;", ct);
        await CreerJournal(cn, ct);

        var jouees = await LireJournal(cn, null, ct);
        var parId = migrations.ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var (id, empreinte) in jouees)
        {
            if (parId.TryGetValue(id, out var m) && !string.Equals(m.Empreinte, empreinte, StringComparison.OrdinalIgnoreCase))
                throw new ErreurMigration(
                    $"{id}.sql a change depuis qu'il a ete joue sur cette base (empreinte {empreinte[..12]}… -> " +
                    $"{m.Empreinte[..12]}…). Une migration jouee ne se modifie plus : ecrire une nouvelle migration.");
        }

        var faites = new List<string>();
        foreach (var m in migrations.Where(m => !jouees.ContainsKey(m.Id)))
        {
            ct.ThrowIfCancellationRequested();
            var chrono = Stopwatch.StartNew();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            try
            {
                await Executer(cn, tx, $"IF SCHEMA_ID(N'{m.Schema}') IS NULL EXEC(N'CREATE SCHEMA [{m.Schema}]');", ct);
                var lots = Lots(m.Texte);
                for (var i = 0; i < lots.Count; i++)
                {
                    try { await Executer(cn, tx, lots[i].Texte, ct); }
                    catch (SqlException e)
                    {
                        throw new ErreurMigration(
                            $"{m.Id}.sql, lot {i + 1}/{lots.Count} (ligne {lots[i].Ligne + e.LineNumber - 1}) : {e.Message}", e);
                    }
                }
                await using (var ins = new SqlCommand(
                    $"INSERT INTO {TableJournal} (Id, Empreinte, DureeMs) VALUES (@id, @empreinte, @duree);", cn, tx))
                {
                    ins.Parameters.AddWithValue("@id", m.Id);
                    ins.Parameters.AddWithValue("@empreinte", m.Empreinte);
                    ins.Parameters.AddWithValue("@duree", (int)chrono.ElapsedMilliseconds);
                    await ins.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
            }
            catch
            {
                // Une erreur grave peut avoir deja annule la transaction cote serveur.
                if (tx.Connection is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
            faites.Add(m.Id);
            journal?.Invoke($"jouee : {m.Id} ({chrono.ElapsedMilliseconds} ms)");
        }

        return new RapportMigrations(
            faites,
            migrations.Where(m => jouees.ContainsKey(m.Id)).Select(m => m.Id).ToList(),
            jouees.Keys.Where(id => !parId.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Ou en est le journal de la base ouverte : nombre de migrations jouees et
    /// la derniere (par rang). Une base jamais migree rend (0, null).
    /// </summary>
    public static async Task<(int Nombre, string? Derniere)> EtatJournalAsync(SqlConnection cn, CancellationToken ct = default)
    {
        await using var cmd = new SqlCommand(
            $"IF OBJECT_ID(N'{TableJournal}', N'U') IS NULL SELECT 0, CAST(NULL AS nvarchar(128)) " +
            $"ELSE SELECT (SELECT COUNT(*) FROM {TableJournal}), (SELECT TOP (1) Id FROM {TableJournal} ORDER BY Rang DESC);", cn);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        await lecteur.ReadAsync(ct);
        return (lecteur.GetInt32(0), lecteur.IsDBNull(1) ? null : lecteur.GetString(1));
    }

    /// <summary>Les migrations du journal, dans l'ordre ou elles ont ete jouees.</summary>
    public static async Task<IReadOnlyList<string>> JoueesAsync(string connexion, CancellationToken ct = default)
    {
        await using var cn = new SqlConnection(connexion);
        await cn.OpenAsync(ct);
        return [.. (await LireJournal(cn, null, ct)).Keys];
    }

    // --- Lecture d'un fichier ---------------------------------------------

    /// <summary>Sans BOM, fins de ligne en LF, sans blanc final : la meme empreinte
    /// quel que soit le reglage git (autocrlf) du poste qui a extrait le fichier.</summary>
    private static string Normaliser(string texte)
        => texte.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

    private static string Empreinte(string texte)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texte))).ToLowerInvariant();

    /// <summary>Les references « -- apres: » de l'EN-TETE (lignes de commentaire ou vides du debut).</summary>
    private static IReadOnlyList<string> LireApres(string texte)
    {
        var refs = new List<string>();
        foreach (var ligne in texte.Split('\n'))
        {
            var t = ligne.Trim();
            if (t.Length > 0 && !t.StartsWith("--", StringComparison.Ordinal)) break;
            var m = LigneApres.Match(t);
            if (!m.Success) continue;
            refs.AddRange(m.Groups["refs"].Value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries));
        }
        return refs;
    }

    /// <summary>Le script coupe a chaque ligne « GO » ; chaque lot garde sa ligne de depart (1 = premiere ligne du fichier).</summary>
    private static List<(string Texte, int Ligne)> Lots(string texte)
    {
        var lots = new List<(string, int)>();
        var debut = 0;
        foreach (Match go in LigneGo.Matches(texte))
        {
            Ajouter(texte[debut..go.Index], debut);
            debut = go.Index + go.Length;
        }
        Ajouter(texte[debut..], debut);
        return lots;

        void Ajouter(string morceau, int position)
        {
            if (morceau.Trim().Length == 0) return;
            lots.Add((morceau, texte.AsSpan(0, position).Count('\n') + 1));
        }
    }

    // --- Ordre ----------------------------------------------------------------

    /// <summary>Tri topologique sur « -- apres: », ex aequo departages par le nom (ordinal).</summary>
    private static IReadOnlyList<Migration> Ordonner(List<Migration> migrations)
    {
        var avant = migrations.ToDictionary(m => m.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var m in migrations)
        {
            foreach (var reference in m.Apres)
            {
                var visees = migrations.Where(x => x.Id == reference || x.Story == reference).Select(x => x.Id).ToList();
                if (visees.Count == 0)
                    throw new ErreurMigration(
                        $"{m.Id}.sql : « -- apres: {reference} » ne designe aucune migration du dossier " +
                        "(identifiant de story, ex. SOC-001, ou nom complet, ex. SOC-001_comptes).");
                foreach (var v in visees.Where(v => v != m.Id)) avant[m.Id].Add(v);
            }
        }

        var restantes = new SortedSet<string>(avant.Keys, StringComparer.Ordinal);
        var parId = migrations.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var ordre = new List<Migration>();
        var placees = new HashSet<string>(StringComparer.Ordinal);
        while (restantes.Count > 0)
        {
            var prete = restantes.FirstOrDefault(id => avant[id].All(placees.Contains));
            if (prete is null)
                throw new ErreurMigration(
                    "Dependances circulaires entre migrations (« -- apres: ») : " + string.Join(", ", restantes) + ".");
            restantes.Remove(prete);
            placees.Add(prete);
            ordre.Add(parId[prete]);
        }
        return ordre;
    }

    // --- Base -------------------------------------------------------------------

    private static async Task CreerJournal(SqlConnection cn, CancellationToken ct)
        => await Executer(cn, null,
            "IF SCHEMA_ID(N'rigv3') IS NULL EXEC(N'CREATE SCHEMA [rigv3]');\n" +
            $"IF OBJECT_ID(N'{TableJournal}', N'U') IS NULL\n" +
            $"CREATE TABLE {TableJournal} (\n" +
            "    Id        nvarchar(128) NOT NULL CONSTRAINT PK_Migration PRIMARY KEY,\n" +
            "    Empreinte char(64)      NOT NULL,\n" +
            "    Rang      int           IDENTITY(1,1) NOT NULL,\n" +
            "    JoueeLe   datetime2(3)  NOT NULL CONSTRAINT DF_Migration_JoueeLe DEFAULT SYSUTCDATETIME(),\n" +
            "    DureeMs   int           NOT NULL);", ct);

    private static async Task<Dictionary<string, string>> LireJournal(SqlConnection cn, SqlTransaction? tx, CancellationToken ct)
    {
        var jouees = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var cmd = new SqlCommand($"SELECT Id, Empreinte FROM {TableJournal} ORDER BY Rang;", cn, tx);
        await using var lecteur = await cmd.ExecuteReaderAsync(ct);
        while (await lecteur.ReadAsync(ct)) jouees[lecteur.GetString(0)] = lecteur.GetString(1);
        return jouees;
    }

    private static async Task Executer(SqlConnection cn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        // Pas de limite de duree : une migration de reprise peut copier des millions
        // de lignes ; c'est l'operateur qui l'arrete (Ctrl+C), pas un delai arbitraire.
        await using var cmd = new SqlCommand(sql, cn, tx) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
