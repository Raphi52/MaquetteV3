using System.Net.Http.Json;
using System.Text.Json;
using RigV3.Contrats.Socle;
using RigV3.Lanceur;

namespace RigV3.Livraison;

// ============================================================
// LE RELEVE DES VERSIONS (SOC-012 CA06, T06 ; lot 12, 2026-11-21).
//
// Pour chaque site : la version de sa partie serveur (GET /socle/version). Pour chaque serveur
// RDS : la version que son prochain lancement ouvrira (la plus recente complete de son dossier
// versions\, regle du lanceur, Installation.Courante). Un RDS qui ne porte pas la version de la
// partie serveur de son site est en ecart ; un site muet aussi. Lance apres chaque livraison
// (et chaque nuit), il rend 0 si tout concorde, 1 sinon.
// La liste des serveurs RDS (nom, site, dossier d'installation vu du poste d'exploitation, ex.
// \\RDS-01\c$\Program Files\RigV3) est un fichier de l'exploitation : leur nombre n'est pas
// tranche (une dizaine ou une quarantaine, Brain : rig-v3-nombre-de-serveurs-rds-en-production).
// ============================================================

/// <summary>Un serveur RDS a relever.</summary>
public sealed record ServeurRds(string Nom, string Site, string Racine);

/// <summary>Une ligne du releve.</summary>
/// <param name="Piece">« serveur » (partie serveur d'un site) ou « rds ».</param>
/// <param name="Attendue">La version de la partie serveur du site ; null si elle n'a pas repondu.</param>
public sealed record LigneReleve(string Piece, string Nom, string Site, string? Version, string? Attendue, bool Concorde, string? Detail);

public static class Releve
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lit rds.json : [ { "nom": "RDS-01", "site": "Amitel", "racine": "\\\\RDS-01\\c$\\Program Files\\RigV3" } ].</summary>
    public static IReadOnlyList<ServeurRds> LireRds(string chemin)
        => JsonSerializer.Deserialize<List<ServeurRds>>(File.ReadAllText(chemin), Json)
           ?? throw new InvalidOperationException($"{chemin} illisible.");

    /// <param name="client">Le transport vers les parties serveur ; un banc y met le serveur en memoire.</param>
    public static async Task<IReadOnlyList<LigneReleve>> FaireAsync(Sites sites, IReadOnlyList<ServeurRds> rds,
                                                                    HttpMessageHandler? client = null, CancellationToken ct = default)
    {
        using var http = client is null ? new HttpClient() : new HttpClient(client, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(15);
        var lignes = new List<LigneReleve>();
        var parSite = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites.Liste.Where(s => s.Adresse is not null || rds.Any(r => r.Site.Equals(s.Nom, StringComparison.OrdinalIgnoreCase))))
        {
            string? version = null, detail = null;
            if (site.Adresse is null) detail = "adresse de la partie serveur non posee dans sites.json";
            else
            {
                try
                {
                    var v = await http.GetFromJsonAsync<VersionServeur>(new Uri(new Uri(site.Adresse.TrimEnd('/') + "/"), RoutesVersions.Serveur.TrimStart('/')), Json, ct);
                    version = v?.Version;
                    if (v is not null && !v.Site.Equals(site.Nom, StringComparison.OrdinalIgnoreCase))
                        detail = $"le serveur se dit du site « {v.Site} »";
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
                {
                    detail = $"partie serveur muette : {e.Message}";
                }
            }
            parSite[site.Nom] = version;
            lignes.Add(new LigneReleve("serveur", site.Adresse ?? "(sans adresse)", site.Nom, version, version, version is not null && detail is null, detail));
        }
        foreach (var r in rds.OrderBy(r => r.Site, StringComparer.Ordinal).ThenBy(r => r.Nom, StringComparer.Ordinal))
        {
            var attendue = parSite.GetValueOrDefault(r.Site);
            VersionInstallee? courante = null;
            string? detail = null;
            try { courante = Installation.Courante(r.Racine); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { detail = $"dossier illisible : {e.Message}"; }
            if (courante is null && detail is null) detail = "aucune version complete installee";
            var concorde = courante is not null && attendue is not null && VersionV3.Egales(courante.Version, attendue);
            if (detail is null && !concorde)
                detail = attendue is null ? "partie serveur du site non relevee" : $"ecran {courante!.Version}, partie serveur {attendue}";
            lignes.Add(new LigneReleve("rds", r.Nom, r.Site, courante?.Version, attendue, concorde, detail));
        }
        return lignes;
    }

    public static string Texte(IReadOnlyList<LigneReleve> lignes)
        => string.Join(Environment.NewLine, lignes.Select(l =>
               $"{(l.Concorde ? "OK   " : "ECART")} {l.Piece,-8} {l.Site,-10} {l.Nom,-40} {l.Version ?? "?",-10}" +
               (l.Detail is null ? "" : $"  {l.Detail}")));
}
