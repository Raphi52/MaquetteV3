using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RigV3.Livraison;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Greffes;

// ============================================================
// L'OUTIL DE LIVRAISON DE LA V3 (SOC-009, SOC-010, SOC-012 ; lot 12 du plan, 2026-11-21).
// Mode d'emploi complet : docs/reconstruction/socle-exploitation.md.
//
//   RigV3.Livraison paquet  --sortie <dossier> [--version 2026.1.1] [--migrations <dossier>]
//       construit le paquet d'une version (livraison.json + migrations/), schema mesure sur une
//       base jetable du SQL Server LOCAL ; version par defaut : celle de Directory.Build.props.
//   RigV3.Livraison livrer  --paquet <dossier> --commune <chaine> --vague beta|metropole|outremer
//                           [--authentification <chaine sans serveur>] [--rapport <fichier.json>]
//   RigV3.Livraison ecart   (memes options que livrer) : le controle d'ecart seul, rien n'est ecrit.
//       --commune : la base commune V3 (catalogue des greffes) ; --authentification : comment
//       ouvrir les instances des greffes (defaut : session Windows, Greffes:Authentification).
//   RigV3.Livraison sites   --commune <chaine> [--sites <sites.json>]
//       quel site sert chaque greffe, incoherences, et la prochaine fenetre de mise a jour de
//       chaque site (hors heures d'ouverture, heure locale de ses greffes).
//   RigV3.Livraison site    --site <nom> --sortie <dossier> [--sites <sites.json>]
//       ecrit appsettings.Production.json de la partie serveur du site (paquet-serveur.ps1).
//   RigV3.Livraison rds     --ecran <publication de RigV3Desktop> --racine <dossier RDS> [--lanceur <publication de Lanceur>]
//       pose une version de l'ecran a cote des autres sur un serveur RDS (et met le lanceur a jour).
//   RigV3.Livraison releve  --rds <rds.json> [--sites <sites.json>]
//       versions de chaque partie serveur et de chaque serveur RDS ; 1 si l'une s'ecarte.
// Code de sortie : 0 tout est fait et conforme ; 1 arret (ecart, echec) ; 2 commande mal formee.
// ============================================================

try
{
    if (args.Length == 0) return Aide();
    var options = Options(args[1..]);
    string Exiger(string cle) => options.TryGetValue(cle, out var v) ? v
        : throw new ArgumentException($"option --{cle} manquante (voir l'en-tete de Program.cs).");
    var sitesJson = options.GetValueOrDefault("sites") ?? Path.Combine(AppContext.BaseDirectory, Sites.NomFichier);

    switch (args[0])
    {
        case "paquet":
        {
            var version = options.GetValueOrDefault("version") ?? VersionDeLOutil();
            var migrations = options.GetValueOrDefault("migrations") ?? Path.Combine(AppContext.BaseDirectory, "migrations");
            var paquet = await PaquetLivraison.ConstruireAsync(migrations, Exiger("sortie"), version);
            Console.WriteLine($"Paquet {paquet.Version} : {paquet.Manifeste.Migrations.Count} migration(s), {paquet.Manifeste.Schema.Count} objet(s) " +
                              $"de schema, empreinte {paquet.Empreinte[..12]}, dans {paquet.Dossier}.");
            return 0;
        }
        case "livrer" or "ecart":
        {
            var paquet = PaquetLivraison.Lire(Exiger("paquet"));
            var livreur = new Livreur(Catalogue(Exiger("commune"), options.GetValueOrDefault("authentification")), Console.WriteLine);
            var rapport = await livreur.LivrerAsync(paquet, Vagues.Lire(Exiger("vague")), controleSeul: args[0] == "ecart");
            Console.WriteLine(rapport.Texte());
            if (options.TryGetValue("rapport", out var fichier))
                await File.WriteAllTextAsync(fichier, JsonSerializer.Serialize(rapport, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return rapport.Reussie ? 0 : 1;
        }
        case "sites":
        {
            var sites = Sites.Lire(sitesJson);
            var catalogue = await Catalogue(Exiger("commune"), options.GetValueOrDefault("authentification")).ListerAsync();
            foreach (var a in sites.Repartir(catalogue))
                Console.WriteLine($"{a.Greffe,-6} {a.Nom,-28} {a.Instance,-18} {(a.EstOutreMer ? "outre-mer" : "metropole"),-10} -> {a.Site ?? "AUCUN SITE"}");
            foreach (var site in sites.Liste)
            {
                var fuseaux = sites.Fuseaux(site, catalogue);
                if (fuseaux.Count == 0) continue;
                var f = sites.ProchaineFenetre(fuseaux, DateTime.UtcNow);
                Console.WriteLine(f is { } fenetre
                    ? $"{site.Nom} : prochaine mise a jour possible du {fenetre.DebutUtc:dd/MM HH:mm} au {fenetre.FinUtc:dd/MM HH:mm} UTC ({string.Join(", ", fuseaux)})"
                    : $"{site.Nom} : AUCUNE fenetre hors heures d'ouverture dans les 8 jours ({string.Join(", ", fuseaux)})");
            }
            var incoherences = sites.Incoherences(catalogue);
            foreach (var i in incoherences) Console.WriteLine("INCOHERENCE " + i);
            return incoherences.Count == 0 ? 0 : 1;
        }
        case "site":
        {
            var site = Sites.Lire(sitesJson).Site(Exiger("site"));
            var sortie = Exiger("sortie");
            Directory.CreateDirectory(sortie);
            await File.WriteAllTextAsync(Path.Combine(sortie, "appsettings.Production.json"), Sites.ConfigurationServeur(site));
            Console.WriteLine($"appsettings.Production.json du site {site.Nom} ecrit dans {sortie}.");
            return 0;
        }
        case "rds":
        {
            // SOC-012 T01 : pose l'ecran (et le lanceur) sur un serveur RDS, depuis le poste d'exploitation
            // (--racine en \\RDS-01\c$\Program Files\RigV3), sans fermer les sessions ouvertes.
            var racine = Exiger("racine");
            var posee = RigV3.Lanceur.Installation.Poser(Exiger("ecran"), racine);
            Console.WriteLine($"Ecran {posee.Version} pose dans {posee.Dossier} : il sert au prochain lancement.");
            if (options.TryGetValue("lanceur", out var lanceur))
                foreach (var ancien in RigV3.Lanceur.Installation.PoserLanceur(lanceur, racine))
                    Console.WriteLine($"Lanceur tenu par une session, mis de cote : {ancien} (a effacer quand plus personne ne l'utilise).");
            return 0;
        }
        case "releve":
        {
            var lignes = await Releve.FaireAsync(Sites.Lire(sitesJson), Releve.LireRds(Exiger("rds")));
            Console.WriteLine(Releve.Texte(lignes));
            return lignes.All(l => l.Concorde) ? 0 : 1;
        }
        default:
            return Aide();
    }
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
catch (Exception e) when (e is InvalidOperationException or RigV3.Base.ErreurMigration)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

static int Aide()
{
    Console.Error.WriteLine("RigV3.Livraison paquet|livrer|ecart|sites|site|rds|releve [--option valeur ...] : voir l'en-tete de Program.cs.");
    return 2;
}

static Dictionary<string, string> Options(string[] morceaux)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < morceaux.Length; i++)
    {
        if (!morceaux[i].StartsWith("--") || i + 1 >= morceaux.Length)
            throw new ArgumentException($"option mal formee : « {morceaux[i]} » (attendu : --nom valeur).");
        options[morceaux[i][2..]] = morceaux[++i];
    }
    return options;
}

static CatalogueGreffes Catalogue(string commune, string? authentification)
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        [CatalogueGreffes.CleAuthentification] = authentification,
    }).Build();
    return new CatalogueGreffes(new BaseRig(commune), configuration);
}

static string VersionDeLOutil()
    => RigV3.Contrats.Socle.VersionV3.Normaliser(
        typeof(Livreur).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0");
