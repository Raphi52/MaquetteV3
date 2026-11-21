using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using RigV3.Base;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Livraison;

// ============================================================
// LE PAQUET D'UNE VERSION DE LA BASE (SOC-009 CA01, T02 ; lot 12 du plan, 2026-11-21).
//
// « Une seule source versionnee » : un paquet est un DOSSIER fige, construit une fois
// depuis Base/migrations (le depot git), puis livre tel quel aux trois vagues :
//
//   <paquet>/livraison.json   version, empreinte, migrations (nom + empreinte de chacune),
//                             schema attendu apres livraison (une ligne par objet) ;
//   <paquet>/migrations/*.sql les scripts, recopies a l'octet.
//
// Le schema attendu se MESURE au moment de construire : les migrations sont jouees sur une
// base jetable du SQL Server local (BaseJetable, garde-fou « serveur local seulement »), puis
// relevees par BaseGreffe.SchemaAsync, le meme releve que la verification SOC-008. Le controle
// d'ecart de chaque base (Livreur) compare a CE releve : une base qui porte un objet en trop
// ou a qui il en manque un est nommee, objet par objet.
//
// A la lecture, le paquet est reverifie : un script modifie apres coup (empreinte differente
// de livraison.json) le fait refuser. Changer un script = construire un nouveau paquet, avec
// un nouveau numero.
// ============================================================

/// <summary>Une migration du paquet.</summary>
public sealed record MigrationPaquet(string Id, string Empreinte);

/// <summary>Le contenu de livraison.json.</summary>
/// <param name="Empreinte">SHA-256 de la liste des migrations (nom et empreinte, dans l'ordre ou elles se jouent) :
/// deux paquets de meme contenu ont la meme empreinte, quel que soit leur numero.</param>
/// <param name="Collation">Classement de la base de reference : une base se compare au schema livre A CE classement
/// (docs/reconstruction/socle-greffes.md, relevé de schéma).</param>
public sealed record ManifestePaquet(string Version, string Empreinte, DateTime ConstruitLe, string Collation,
                                     IReadOnlyList<MigrationPaquet> Migrations, IReadOnlyList<string> Schema);

/// <summary>Un paquet lu et verifie, pret a livrer.</summary>
public sealed record PaquetLivraison(string Dossier, ManifestePaquet Manifeste)
{
    public const string NomManifeste = "livraison.json";
    public const string DossierScripts = "migrations";

    public string Version => Manifeste.Version;
    public string Empreinte => Manifeste.Empreinte;
    public string Migrations => Path.Combine(Dossier, DossierScripts);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Construit le paquet de <paramref name="version"/> dans <paramref name="sortie"/> (qui doit etre vide ou absent),
    /// a partir des scripts de <paramref name="dossierMigrations"/>.
    /// </summary>
    public static async Task<PaquetLivraison> ConstruireAsync(string dossierMigrations, string sortie, string version,
                                                              CancellationToken ct = default)
    {
        version = Contrats.Socle.VersionV3.Normaliser(version);
        if (Directory.Exists(sortie) && Directory.EnumerateFileSystemEntries(sortie).Any())
            throw new InvalidOperationException($"Le dossier du paquet {sortie} n'est pas vide : un paquet ne s'ecrit pas par-dessus un autre.");
        var migrations = Migrateur.Lire(dossierMigrations);   // noms et ordre valides, sinon ErreurMigration
        var scripts = Path.Combine(sortie, DossierScripts);
        Directory.CreateDirectory(scripts);
        foreach (var m in migrations)
            File.Copy(m.Chemin, Path.Combine(scripts, Path.GetFileName(m.Chemin)));

        // Le schema attendu, mesure sur une base neuve a jour du paquet.
        IReadOnlyList<string> schema;
        await using (var reference = await BaseJetable.CreerAsync("LivraisonReference", ct))
        {
            await Migrateur.AppliquerAsync(reference.Connexion, scripts, null, ct);
            await using (var cn = new SqlConnection(reference.Connexion))
            {
                await cn.OpenAsync(ct);
                await JournalLivraisons.CreerAsync(cn, ct);
            }
            schema = await BaseGreffe.SchemaAsync(reference.Connexion, ct);
        }

        var liste = migrations.Select(m => new MigrationPaquet(m.Id, m.Empreinte)).ToList();
        var manifeste = new ManifestePaquet(version, EmpreinteDe(liste), DateTime.UtcNow, BaseJetable.Collation, liste, schema);
        await File.WriteAllTextAsync(Path.Combine(sortie, NomManifeste), JsonSerializer.Serialize(manifeste, Json), Encoding.UTF8, ct);
        return new PaquetLivraison(Path.GetFullPath(sortie), manifeste);
    }

    /// <summary>Lit un paquet et verifie que ses scripts sont ceux de son manifeste (meme liste, memes empreintes).</summary>
    public static PaquetLivraison Lire(string dossier)
    {
        var chemin = Path.Combine(dossier, NomManifeste);
        if (!File.Exists(chemin))
            throw new InvalidOperationException($"{chemin} introuvable : ce dossier n'est pas un paquet de livraison (commande « paquet »).");
        var manifeste = JsonSerializer.Deserialize<ManifestePaquet>(File.ReadAllText(chemin, Encoding.UTF8), Json)
                        ?? throw new InvalidOperationException($"{chemin} illisible.");
        var surDisque = Migrateur.Lire(Path.Combine(dossier, DossierScripts))
            .Select(m => new MigrationPaquet(m.Id, m.Empreinte)).ToList();
        if (!surDisque.SequenceEqual(manifeste.Migrations) || EmpreinteDe(surDisque) != manifeste.Empreinte)
        {
            var differences = surDisque.Except(manifeste.Migrations).Select(m => m.Id)
                .Concat(manifeste.Migrations.Except(surDisque).Select(m => m.Id)).Distinct().ToList();
            throw new InvalidOperationException(
                $"Paquet {manifeste.Version} altere : les scripts de {DossierScripts}\\ ne sont plus ceux de {NomManifeste} " +
                $"({string.Join(", ", differences)}). Un paquet ne se retouche pas : en construire un nouveau.");
        }
        return new PaquetLivraison(Path.GetFullPath(dossier), manifeste);
    }

    private static string EmpreinteDe(IEnumerable<MigrationPaquet> migrations)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", migrations.Select(m => $"{m.Id} {m.Empreinte.ToUpperInvariant()}"))))).ToLowerInvariant();
}
