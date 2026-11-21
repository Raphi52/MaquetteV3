using System.Text.Json;
using System.Text.Json.Nodes;
using RigV3.Contrats.Socle;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Livraison;

// ============================================================
// LES SITES DE LA PARTIE SERVEUR (SOC-010, lot 12 du plan, 2026-11-21).
//
// sites.json (a cote de l'outil) dit ou tourne la partie serveur : Amitel pour la metropole,
// un serveur sur place aux Antilles, a la Reunion et en Polynesie (CA01, CA02). Un greffe est
// servi par LE site qui porte l'instance SQL de sa base (colonne Serveur du catalogue) : la
// regle se lit dans les donnees, pas dans une liste de greffes a tenir a la main, et un greffe
// dont l'instance n'est a aucun site (ou a deux) est signale.
//
// Ce fichier produit aussi, par site, la configuration de la partie serveur
// (appsettings.Production.json du paquet, commande « site ») et la fenetre de mise a jour :
// hors des heures d'ouverture de TOUS les greffes du site, a leur heure locale (CA04).
// ============================================================

/// <summary>Les heures d'ouverture des greffes, heure locale de chaque greffe.</summary>
public sealed record Ouverture(IReadOnlyList<DayOfWeek> Jours, TimeOnly Debut, TimeOnly Fin)
{
    /// <summary>Le greffe est-il ouvert a cette heure locale ?</summary>
    public bool Ouvert(DateTime local) => Jours.Contains(local.DayOfWeek) && TimeOnly.FromDateTime(local) is var h && h >= Debut && h < Fin;
}

/// <summary>La base commune qu'un site ouvre (ConnectionStrings:RigV3 de sa partie serveur).</summary>
public sealed record BaseCommuneSite(string Instance, string Base);

/// <summary>Un site d'hebergement de la partie serveur.</summary>
/// <param name="Instances">Les instances SQL que ce site atteint sans traverser l'ocean.</param>
/// <param name="Adresse">L'adresse de sa partie serveur (RIGV3_SERVEUR des serveurs RDS du site) ; null tant
/// qu'elle n'est pas posee au deploiement.</param>
public sealed record Site(string Nom, string Lieu, IReadOnlyList<string> Instances, BaseCommuneSite BaseCommune, string? Adresse);

/// <summary>Un greffe et le site qui le sert.</summary>
public sealed record AffectationSite(string Greffe, string Nom, string Instance, bool EstOutreMer, string? Site);

/// <summary>Le contenu de sites.json.</summary>
public sealed record Sites(Ouverture Ouverture, IReadOnlyList<Site> Liste)
{
    public const string NomFichier = "sites.json";

    /// <summary>Nom du service Windows de la partie serveur, le meme sur chaque site.</summary>
    public const string NomService = "RigV3Serveur";

    public static Sites Lire(string chemin)
    {
        var racine = JsonNode.Parse(File.ReadAllText(chemin))
                     ?? throw new InvalidOperationException($"{chemin} illisible.");
        var o = racine["ouverture"]!;
        var ouverture = new Ouverture(
            [.. o["jours"]!.AsArray().Select(j => Enum.Parse<DayOfWeek>(j!.GetValue<string>()))],
            TimeOnly.Parse(o["debut"]!.GetValue<string>()), TimeOnly.Parse(o["fin"]!.GetValue<string>()));
        var sites = racine["sites"]!.AsArray().Select(s => new Site(
            s!["nom"]!.GetValue<string>(),
            s["lieu"]!.GetValue<string>(),
            [.. s["instances"]!.AsArray().Select(i => i!.GetValue<string>())],
            new BaseCommuneSite(s["baseCommune"]!["instance"]!.GetValue<string>(), s["baseCommune"]!["base"]!.GetValue<string>()),
            s["adresse"]?.GetValue<string>())).ToList();
        var doublons = sites.SelectMany(s => s.Instances.Select(i => (Instance: i, s.Nom)))
            .GroupBy(x => x.Instance, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (doublons.Count > 0)
            throw new InvalidOperationException("Instance servie par deux sites : " +
                string.Join(" ; ", doublons.Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.Nom))})")));
        return new Sites(ouverture, sites);
    }

    public Site Site(string nom)
        => Liste.FirstOrDefault(s => s.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException($"Site inconnu « {nom} » : {string.Join(", ", Liste.Select(s => s.Nom))}.");

    /// <summary>Le site qui sert un greffe : celui qui porte l'instance de sa base ; null si aucun (CA01, CA02).</summary>
    public Site? SitePour(FicheGreffe greffe)
        => Liste.FirstOrDefault(s => s.Instances.Contains(greffe.Serveur, StringComparer.OrdinalIgnoreCase));

    /// <summary>Chaque greffe du catalogue et son site, par code.</summary>
    public IReadOnlyList<AffectationSite> Repartir(IEnumerable<FicheGreffe> catalogue)
        => [.. catalogue.OrderBy(g => g.Code, StringComparer.Ordinal)
                .Select(g => new AffectationSite(g.Code, g.Nom, g.Serveur, g.EstOutreMer, SitePour(g)?.Nom))];

    /// <summary>
    /// Les incoherences de la repartition : greffe sans site, greffe d'outre-mer servi par Amitel (sa base
    /// serait atteinte a travers l'ocean, CA02), greffe de metropole servi par un site d'outre-mer.
    /// </summary>
    public IReadOnlyList<string> Incoherences(IEnumerable<FicheGreffe> catalogue, string siteMetropole = "Amitel")
    {
        var problemes = new List<string>();
        foreach (var g in catalogue.OrderBy(g => g.Code, StringComparer.Ordinal))
        {
            var site = SitePour(g);
            if (site is null)
                problemes.Add($"{g.Code} {g.Nom} : aucune partie serveur ne sert l'instance {g.Serveur}.");
            else if (g.EstOutreMer && site.Nom.Equals(siteMetropole, StringComparison.OrdinalIgnoreCase))
                problemes.Add($"{g.Code} {g.Nom} : greffe d'outre-mer servi par {site.Nom} (sa base {g.Serveur} serait atteinte a travers l'ocean).");
            else if (!g.EstOutreMer && !site.Nom.Equals(siteMetropole, StringComparison.OrdinalIgnoreCase))
                problemes.Add($"{g.Code} {g.Nom} : greffe de metropole servi par le site {site.Nom}.");
        }
        return problemes;
    }

    // --- CA04 : la mise a jour hors des heures d'ouverture, heure locale ---------------------------

    /// <summary>Les fuseaux des greffes servis par le site (fuseau du catalogue, sinon celui de reprise).</summary>
    public IReadOnlyList<string> Fuseaux(Site site, IEnumerable<FicheGreffe> catalogue)
        => [.. catalogue.Where(g => SitePour(g)?.Nom == site.Nom)
                .Select(g => g.Fuseau ?? RigV3.Serveur.Socle.Greffes.Fuseaux.ParDefaut(g.Code, g.EstOutreMer)
                             ?? throw new InvalidOperationException($"{g.Code} {g.Nom} : fuseau inconnu, a saisir au catalogue (SOC-007 T04)."))
                .Distinct(StringComparer.Ordinal)];

    /// <summary>Une mise a jour du site est-elle permise a cet instant : tous ses greffes fermes, a leur heure locale ?</summary>
    public bool MiseAJourPermise(IReadOnlyList<string> fuseaux, DateTime instantUtc)
        => fuseaux.All(f => !Ouverture.Ouvert(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc),
                                                                               TimeZoneInfo.FindSystemTimeZoneById(f))));

    /// <summary>Le debut et la fin de la prochaine fenetre de mise a jour (pas d'un quart d'heure), a partir de
    /// <paramref name="depuisUtc"/> ; null s'il n'y en a aucune dans les 8 jours (heures d'ouverture sans trou commun).</summary>
    public (DateTime DebutUtc, DateTime FinUtc)? ProchaineFenetre(IReadOnlyList<string> fuseaux, DateTime depuisUtc)
    {
        var pas = TimeSpan.FromMinutes(15);
        var t = new DateTime(depuisUtc.Ticks - depuisUtc.Ticks % pas.Ticks, DateTimeKind.Utc);
        var limite = t.AddDays(8);
        while (t < limite && !MiseAJourPermise(fuseaux, t)) t += pas;
        if (t >= limite) return null;
        var fin = t;
        while (fin < limite && MiseAJourPermise(fuseaux, fin)) fin += pas;
        return (t, fin);
    }

    // --- T02 : la configuration de la partie serveur d'un site --------------------------------------

    /// <summary>
    /// appsettings.Production.json de la partie serveur du site : son nom et ses instances (GET /socle/version),
    /// sa base commune (session Windows du compte de service, comme la V2 : bases-et-roles.md:18) et son adresse
    /// d'ecoute. Aucun mot de passe : l'acces aux bases est celui du compte du service Windows.
    /// </summary>
    public static string ConfigurationServeur(Site site)
    {
        var config = new JsonObject
        {
            ["Site"] = new JsonObject
            {
                ["Nom"] = site.Nom,
                ["Instances"] = new JsonArray([.. site.Instances.Select(i => (JsonNode)JsonValue.Create(i)!)]),
            },
            ["ConnectionStrings"] = new JsonObject
            {
                ["RigV3"] = $"Server={site.BaseCommune.Instance};Database={site.BaseCommune.Base};Integrated Security=True;TrustServerCertificate=True",
            },
            ["Greffes"] = new JsonObject { ["Authentification"] = CatalogueGreffes.AuthentificationParDefaut },
        };
        if (site.Adresse is not null) config["Urls"] = site.Adresse;
        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
