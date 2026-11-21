using System.Diagnostics;
using System.IO;
using System.Text.Json;
using RigV3.Contrats.Socle;

namespace RigV3.Lanceur;

// ============================================================
// L'INSTALLATION DE L'ECRAN SUR UN SERVEUR RDS (SOC-012 T01, CA01, CA05 ; lot 12, 2026-11-21).
//
//   <racine>\RigV3Lanceur.exe              l'icone des agents, a cote de celle de la V2
//   <racine>\versions\2026.1.0\            une version de l'ecran (RigV3Desktop.exe, wwwroot\...)
//   <racine>\versions\2026.1.0\installation.json   ecrite EN DERNIER : sans elle, la version
//                                          est incomplete et le lanceur l'ignore
//
// Une nouvelle version se POSE A COTE des autres, sans toucher aux dossiers que les
// sessions ouvertes executent (la V2, elle, doit tuer ses processus avant de copier :
// deploy-runtime.md:11-17). Elle sert au prochain lancement : le lanceur ouvre toujours
// la plus recente des versions completes. Rien ne s'ecrit dans le dossier d'installation
// a l'usage : l'agent n'a besoin d'aucun droit d'administrateur, ses donnees vont dans
// son profil (navigateur integre : %LOCALAPPDATA%\RigV3Desktop.WebView2, Pont/PageWeb.cs).
//
// Ce fichier ne depend ni de WPF ni du reseau : le banc SocleLivraisonCheck et le releve
// des versions (Outils/Livraison) le compilent tel quel.
// ============================================================

/// <summary>Une version de l'ecran installee sur le serveur RDS.</summary>
public sealed record VersionInstallee(string Version, string Dossier, DateTime PoseeLe)
{
    public string Ecran => Path.Combine(Dossier, Installation.NomEcran);
}

/// <summary>Le contenu de installation.json.</summary>
public sealed record MarqueInstallation(string Version, DateTime PoseeLe, string PoseePar, string Poste);

public static class Installation
{
    public const string DossierVersions = "versions";
    public const string NomMarque = "installation.json";
    public const string NomEcran = "RigV3Desktop.exe";
    private const string SuffixePartiel = ".partiel";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Les versions COMPLETES (marque presente et coherente avec le dossier), la plus recente d'abord.</summary>
    public static IReadOnlyList<VersionInstallee> Lister(string racine)
    {
        var dossier = Path.Combine(racine, DossierVersions);
        if (!Directory.Exists(dossier)) return [];
        var liste = new List<VersionInstallee>();
        foreach (var d in Directory.GetDirectories(dossier))
        {
            var nom = Path.GetFileName(d);
            if (nom.EndsWith(SuffixePartiel, StringComparison.OrdinalIgnoreCase)) continue;
            var marque = Path.Combine(d, NomMarque);
            if (!File.Exists(marque)) continue;
            MarqueInstallation? m;
            try { m = JsonSerializer.Deserialize<MarqueInstallation>(File.ReadAllText(marque), Json); }
            catch (JsonException) { continue; }
            if (m is null || !VersionV3.Egales(m.Version, nom)) continue;
            liste.Add(new VersionInstallee(VersionV3.Normaliser(m.Version), d, m.PoseeLe));
        }
        liste.Sort((a, b) => VersionV3.Comparer(b.Version, a.Version));
        return liste;
    }

    /// <summary>La version que le prochain lancement ouvrira ; null si aucune n'est complete.</summary>
    public static VersionInstallee? Courante(string racine) => Lister(racine).FirstOrDefault();

    /// <summary>La version d'un ecran construit : celle de son exe (Directory.Build.props), sans suffixe.</summary>
    public static string VersionDe(string exe)
    {
        var info = FileVersionInfo.GetVersionInfo(exe);
        var v = info.ProductVersion ?? info.FileVersion
                ?? throw new InvalidOperationException($"{exe} ne porte pas de version.");
        return VersionV3.Normaliser(v);
    }

    /// <summary>
    /// Pose la version de l'ecran construit dans <paramref name="source"/> (sortie de publication de
    /// RigV3Desktop) A COTE des versions deja installees : copie dans un dossier « .partiel », renommage,
    /// puis marque ecrite en dernier. Aucun fichier d'une autre version n'est touche (CA05).
    /// </summary>
    public static VersionInstallee Poser(string source, string racine)
    {
        var exe = Path.Combine(source, NomEcran);
        if (!File.Exists(exe)) throw new InvalidOperationException($"{exe} introuvable : ce dossier n'est pas un ecran publie.");
        var version = VersionDe(exe);
        var versions = Path.Combine(racine, DossierVersions);
        var cible = Path.Combine(versions, version);
        if (Directory.Exists(cible))
            throw new InvalidOperationException($"La version {version} est deja posee ({cible}) : une version posee ne se recopie pas.");
        var partiel = cible + SuffixePartiel;
        if (Directory.Exists(partiel)) Directory.Delete(partiel, recursive: true);   // reste d'une pose interrompue
        Copier(source, partiel);
        Directory.Move(partiel, cible);
        var marque = new MarqueInstallation(version, DateTime.UtcNow, Environment.UserName, Environment.MachineName);
        File.WriteAllText(Path.Combine(cible, NomMarque), JsonSerializer.Serialize(marque, Json));
        return new VersionInstallee(version, cible, marque.PoseeLe);
    }

    /// <summary>
    /// Met a jour le lanceur lui-meme a la racine (fichiers de <paramref name="source"/>, sortie de publication de
    /// Lanceur/). Les lanceurs en veille des sessions ouvertes tiennent leurs fichiers : Windows refuse de les
    /// ecraser mais accepte de les RENOMMER. Un fichier tenu est donc renomme en « .ancien-&lt;horodatage&gt; » (il
    /// sert jusqu'a la fin de la session qui l'utilise), puis le nouveau est copie a sa place (CA05 : aucune
    /// session fermee). Rend les fichiers ainsi mis de cote, a effacer apres coup.
    /// </summary>
    public static IReadOnlyList<string> PoserLanceur(string source, string racine)
    {
        Directory.CreateDirectory(racine);
        var misDeCote = new List<string>();
        var suffixe = $".ancien-{DateTime.UtcNow:yyyyMMddHHmmss}";
        foreach (var f in Directory.GetFiles(source))
        {
            var cible = Path.Combine(racine, Path.GetFileName(f));
            if (File.Exists(cible))
            {
                try { File.Copy(f, cible, overwrite: true); continue; }
                catch (IOException) { }   // tenu par une session : on le renomme, ci-dessous
                File.Move(cible, cible + suffixe);
                misDeCote.Add(cible + suffixe);
            }
            File.Copy(f, cible);
        }
        return misDeCote;
    }

    private static void Copier(string source, string cible)
    {
        Directory.CreateDirectory(cible);
        foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(cible, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(source)) Copier(d, Path.Combine(cible, Path.GetFileName(d)));
    }
}
