using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using RigV3.Base;
using RigV3.Serveur;
using RigV3.Serveur.Socle.Chaines;
using RigV3.Serveur.Socle.Greffes;

namespace RigV3.Passage;

/// <summary>La ligne de commande de l'outil (en tete de Program.cs), appelable par le banc sans processus a part.</summary>
public static class LigneDeCommande
{
    /// <param name="coffre">La cle de signature des chaines ; null = le magasin de cles Windows du compte qui lance
    /// l'outil (CoffreClesCng) : lancer le passage sous le compte de service de la partie serveur (SOC-018).</param>
    public static async Task<int> ExecuterAsync(string[] args, TextWriter sortie, ICoffreCles? coffre = null, CancellationToken ct = default)
    {
        Dictionary<string, string> o;
        try { o = Options(args.Skip(args.FirstOrDefault() is "beta" or "certification" ? 2 : 1)); }
        catch (ArgumentException e) { sortie.WriteLine(e.Message); return 3; }
        string? Option(string cle) => o.GetValueOrDefault(cle);
        bool Drapeau(string cle) => o.ContainsKey(cle);

        try
        {
            switch (args.FirstOrDefault())
            {
                case "passer" or "retour" when Option("commune") is { } commune && Option("greffe") is { } greffe:
                {
                    var options = OptionsDePassage(o, commune, greffe, coffre, sortie);
                    var r = args[0] == "passer" ? await MoteurPassage.PasserAsync(options, ct) : await MoteurPassage.RevenirAsync(options, ct);
                    sortie.WriteLine();
                    sortie.WriteLine(Texte(r));
                    // « retour » : revenir a la V2 est ce qui etait demande (0) ; « passer » : c'est un echec rattrape (1).
                    return args[0] == "retour" && r.Issue == IssuesPassage.RevenuV2 ? 0 : IssuesPassage.Code(r.Issue);
                }
                case "jour-j" when Option("commune") is { } commune && Option("jour") is { } jour:
                {
                    var plan = await PlanAsync(o, commune, jour, ct);
                    sortie.WriteLine(PlanJourJ.Texte(plan));
                    if (Option("plan") is { } f) File.WriteAllText(f, JsonSerializer.Serialize(plan, RapportPassage.Json));
                    if (plan.SansFuseau.Count > 0) return 1;
                    if (!Drapeau("jouer")) return 0;
                    if (!Drapeau("exploitation")) { sortie.WriteLine("Refuse : le jour J se joue chez Amitel, avec --exploitation."); return 3; }
                    var jourJ = await MoteurRepetition.JouerAsync(new MoteurRepetition.OptionsRepetition(0, plan, l => OptionsDePassage(o, commune, l.Code, coffre, sortie))
                    {
                        EstRepetition = false, AuxHeures = true, DossierRapports = Option("dossier-passages"), Suivre = sortie.WriteLine,
                    }, ct);
                    sortie.WriteLine(MoteurRepetition.Tableau(jourJ.Bases));
                    return jourJ.Bases.All(b => b.Issue == IssuesPassage.Passe) ? 0 : 1;
                }
                case "repeter" when Option("commune") is { } commune && Option("jour") is { } jour && Option("rapport") is { } fichier:
                {
                    var plan = await PlanAsync(o, commune, jour, ct);
                    if (Option("greffes") is { } liste)
                    {
                        var codes = liste.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                        plan = plan with { Passages = [.. plan.Passages.Where(p => codes.Contains(p.Code))] };
                    }
                    var echecs = (Option("simuler-echec") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => x.Split(':')).ToDictionary(x => x[0], x => x.Length > 1 ? x[1] : EtapesPassage.Bascule, StringComparer.Ordinal);
                    var rep = await MoteurRepetition.JouerAsync(new MoteurRepetition.OptionsRepetition(
                        int.Parse(Option("numero") ?? "1", CultureInfo.InvariantCulture), plan, l => OptionsDePassage(o, commune, l.Code, coffre, sortie))
                    {
                        EchecsSimules = echecs, AuxHeures = Drapeau("aux-heures"), DossierRapports = Option("dossier-passages"), Suivre = sortie.WriteLine,
                    }, ct);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fichier))!);
                    File.WriteAllText(fichier, rep.EnJson());
                    sortie.WriteLine($"Repetition {rep.Numero} : {rep.Bases.Count} base(s) en {MoteurRepetition.TexteDuree(rep.DureeMs)}, rapport {fichier}");
                    sortie.WriteLine(MoteurRepetition.Tableau(rep.Bases));
                    return 0;
                }
                case "tableau" when Option("dossier") is { } dossier:
                    sortie.WriteLine(MoteurRepetition.Tableau(Directory.GetFiles(dossier, "passage-*.json").Select(RapportPassage.Lire)));
                    return 0;
                case "comparer" when Option("repetitions") is { } chemin:
                {
                    var fichiers = Directory.Exists(chemin) ? Directory.GetFiles(chemin, "*.json") : chemin.Split(',', StringSplitOptions.TrimEntries);
                    var constats = ComparaisonRepetitions.Comparer([.. fichiers.Select(RapportRepetition.Lire)], new ComparaisonRepetitions.Regles
                    {
                        Explications = ComparaisonRepetitions.LireExplications(Option("explications")),
                        Defauts = ComparaisonRepetitions.LireDefauts(Option("defauts")),
                        BasesAttendues = Option("bases")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
                        Nuit = Option("nuit") is { } n ? TimeSpan.FromHours(double.Parse(n, CultureInfo.InvariantCulture)) : TimeSpan.FromHours(10),
                    });
                    return Constats(constats, sortie, "Repetitions conformes : le jour J peut se fixer sur elles.", "Repetitions NON conformes.");
                }
                case "beta" when args.Length > 1 && Option("carnet") is { } carnet && Option("dossier") is { } dossier && Option("commune") is { } commune:
                {
                    var fiches = await CatalogueDe(o, commune).ListerAsync(ct);
                    // Les defauts signales par les agents depuis la V3 (SOC-022 CA06) sont dans la base commune, a cote du catalogue.
                    var d = DossierBeta.Lire(carnet, dossier, Option("bancs") ?? Path.Combine(Directory.GetCurrentDirectory(), "tests"), fiches)
                        with { Signales = await DossierBeta.LireSignalesAsync(commune, ct) };
                    var aujourdhui = Option("aujourdhui") is { } a ? DateOnly.Parse(a, CultureInfo.InvariantCulture) : DateOnly.FromDateTime(DateTime.Today);
                    switch (args[1])
                    {
                        case "demarrer":
                            return Constats(CriteresBeta.Demarrage(d, VersionV3(), aujourdhui), sortie,
                                "Criteres de demarrage remplis : la beta peut demarrer.", "La beta ne demarre pas : un critere manque (SOC-022 CA01).");
                        case "terminer":
                            return Constats(CriteresBeta.Fin(d), sortie,
                                "Criteres de fin remplis : la beta se termine, le jour J des 37 autres peut se fixer.",
                                "La beta ne se termine pas : un critere de fin manque (SOC-022 CA04).");
                        case "defauts":
                            sortie.Write(RegleDefauts.Liste(d.Defauts, d.Signales));
                            return 0;
                    }
                    break;
                }
                case "certification" when args.Length > 1 && args[1] == "suivi" && Option("version") is { } version && Option("fichiers") is { } liste:
                {
                    var registre = Option("registre") is { } f && File.Exists(f)
                        ? JsonSerializer.Deserialize<List<SuiviVersion>>(File.ReadAllText(f), RapportPassage.Json) ?? [] : [];
                    // « @fichier » : la liste dans un fichier, une ligne par chemin (sortie de git diff --name-only) ; sinon a,b,c.
                    // fix-ok: « le fichier s'il existe » lisait Base/migrations/SOC-018_chaines.sql, donne seul depuis la racine du depot, comme
                    // une liste de chemins : suivi accorde a tort (banc SoclePassageCheck, 3e lancement, 2026-11-21) ; d'ou le « @ » explicite.
                    var fichiers = liste.StartsWith('@') ? File.ReadAllLines(liste[1..]) : liste.Split(',', StringSplitOptions.TrimEntries);
                    return Constats([SuiviCertification.Juger(version, fichiers, registre)], sortie, "Livraison permise.", "Livraison refusee.");
                }
                case "formation" when Option("v2") is { } v2 && Option("greffe") is { } greffe:
                {
                    var b = new SqlConnectionStringBuilder(v2) { ApplicationIntent = ApplicationIntent.ReadOnly };
                    if (!Drapeau("exploitation") && !BaseJetable.EstServeurLocal(v2) && b.InitialCatalog is not ("RIG_DEV" or "RIG_RECETTE"))
                    {
                        sortie.WriteLine($"Refuse : {b.DataSource}/{b.InitialCatalog} n'est ni sur ce poste ni RIG_DEV / RIG_RECETTE ; " +
                                         "les bases des greffes se comptent chez Amitel, avec --exploitation.");
                        return 3;
                    }
                    await using var cn = new SqlConnection(b.ConnectionString);
                    await cn.OpenAsync(ct);
                    var g = await ComptageAgents.CompterAsync(cn, greffe, ct);
                    sortie.Write(ComptageAgents.Texte([g]));
                    if (Option("plan") is { } f) File.WriteAllText(f, JsonSerializer.Serialize(ComptageAgents.Plan([g]), RapportPassage.Json));
                    return 0;
                }
            }
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException or IOException or JsonException or FormatException or GreffeInconnu or BaseGreffeInjoignable)
        {
            sortie.WriteLine($"Refuse : {e.Message}");
            return 2;
        }
        sortie.WriteLine("""
            Usage : RigV3.Passage passer   --commune <chaine> --greffe <code> [--passage aaaa-mm-jjThh:mm] [--fin-beta aaaa-mm-jjThh:mm:ss]
                                           [--rapport <fichier.json>] [--authentification <chaine>] [--scripts <Base\v2>] [--exploitation]
                    RigV3.Passage retour   --commune <chaine> --greffe <code> [...]
                    RigV3.Passage jour-j   --commune <chaine> --jour aaaa-mm-jj [--ouverture 07:00] [--plan <fichier.json>] [--jouer --exploitation]
                    RigV3.Passage repeter  --commune <chaine> --jour aaaa-mm-jj --numero <n> --rapport <fichier.json> [--greffes a,b]
                                           [--simuler-echec greffe:etape] [--aux-heures] [--dossier-passages <dossier>]
                    RigV3.Passage tableau  --dossier <dossier des passages>
                    RigV3.Passage comparer --repetitions <dossier|f1,f2,f3> [--explications f] [--defauts f] [--bases a,b] [--nuit 10]
                    RigV3.Passage beta demarrer|terminer|defauts --carnet <dossier> --dossier <dossier de la beta> --commune <chaine> [--bancs tests]
                    RigV3.Passage certification suivi --version <v> --fichiers a,b|@liste.txt [--registre suivi-certification.json]
                    RigV3.Passage formation --v2 <chaine> --greffe <code> [--plan formation.json]
            """);
        return 3;
    }

    /// <summary>La version de la V3 que porte cet outil (Directory.Build.props, SOC-009 CA01).</summary>
    public static string VersionV3()
        => typeof(LigneDeCommande).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static OptionsPassage OptionsDePassage(Dictionary<string, string> o, string commune, string greffe, ICoffreCles? coffre, TextWriter sortie)
    {
        static DateTime Date(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return new OptionsPassage(commune, greffe)
        {
            Coffre = coffre ?? (OperatingSystem.IsWindows() ? new CoffreClesCng() : throw new PlatformNotSupportedException("Coffre de cles Windows requis.")),
            Authentification = o.GetValueOrDefault("authentification") ?? CatalogueGreffes.AuthentificationParDefaut,
            DossierScripts = o.GetValueOrDefault("scripts") ?? Path.Combine(AppContext.BaseDirectory, "scripts-v2"),
            PassageLocal = o.GetValueOrDefault("passage") is { } p ? Date(p) : null,
            FinBeta = o.GetValueOrDefault("fin-beta") is { } f ? Date(f) : null,
            SessionEchecs = o.GetValueOrDefault("session-echecs") ?? "rigv3_numerotation_echecs",
            Exploitation = o.ContainsKey("exploitation"),
            FichierRapport = o.GetValueOrDefault("rapport"),
            Suivre = sortie.WriteLine,
        };
    }

    private static CatalogueGreffes CatalogueDe(Dictionary<string, string> o, string commune) => new(new BaseRig(commune),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CatalogueGreffes.CleAuthentification] = o.GetValueOrDefault("authentification") ?? CatalogueGreffes.AuthentificationParDefaut,
        }).Build());

    private static async Task<PlanJourJ.Plan> PlanAsync(Dictionary<string, string> o, string commune, string jour, CancellationToken ct)
        => PlanJourJ.Calculer(await CatalogueDe(o, commune).ListerAsync(ct), DateOnly.Parse(jour, CultureInfo.InvariantCulture),
                              o.GetValueOrDefault("ouverture") is { } h ? TimeOnly.Parse(h, CultureInfo.InvariantCulture) : PlanJourJ.OuvertureParDefaut);

    private static int Constats(IReadOnlyList<Constat> constats, TextWriter sortie, string vert, string rouge)
    {
        foreach (var c in constats) sortie.WriteLine($"{(c.Ok ? "OK      " : "MANQUE  ")} {c.Critere} — {c.Detail}");
        var ok = constats.All(c => c.Ok);
        sortie.WriteLine(ok ? vert : rouge);
        return ok ? 0 : 1;
    }

    /// <summary>Le compte rendu d'un passage : etape par etape, duree, ce qu'elle a constate.</summary>
    public static string Texte(RapportPassage r)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"Passage du greffe {r.Greffe} {r.Nom} : {r.Issue.ToUpperInvariant()} en {r.DureeMs / 1000.0:0.0} s" +
                     (r.PassageLocal is { } p ? $", passage a {p:yyyy-MM-dd HH:mm} heure locale" : ""));
        if (r.Motif is not null) s.AppendLine("  " + r.Motif);
        foreach (var e in r.Etapes) s.AppendLine($"  {e.Nom,-16} {e.Etat,-10} {e.DureeMs,7} ms  {e.Detail}");
        foreach (var e in r.Ecarts) s.AppendLine($"  ECART {e.Comparateur} : {e.Detail}");
        foreach (var e in r.Reserves) s.AppendLine($"  RESERVE : {e}");
        if (r.BaseV3ARecreer) s.AppendLine($"  La base V3 {r.BaseV3} a ete ecrite : la recreer avant de repasser.");
        return s.ToString();
    }

    private static Dictionary<string, string> Options(IEnumerable<string> args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var liste = args.ToList();
        for (var i = 0; i < liste.Count; i++)
        {
            if (!liste[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"option inattendue : {liste[i]}");
            var cle = liste[i][2..];
            o[cle] = i + 1 < liste.Count && !liste[i + 1].StartsWith("--", StringComparison.Ordinal) ? liste[++i] : "";
        }
        return o;
    }
}
