using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3.Contrats.Socle;

namespace RigV3.Passage;

// Le certificat (record Certificat) et sa regle de validite (RegleCertificat) vivent dans Contrats/Socle/Certification.cs
// depuis le lot 17 : la route du certificat de la V3 (SOC-019 CA04) juge avec la MEME regle que le critere de demarrage.
// fix-ok: pas un defaut corrige -- deplacement mesure (grep du 2026-11-21 : la regle de validite n'existait que dans
// CriteresBeta.CertificatValide, l'ecran du certificat en aurait fait une seconde copie) ; les signalements de la V3 lus par
// « beta defauts » (lot 17) sont l'autre ajout de ce fichier.

/// <summary>Une version de la V3 soumise au suivi de certification (SOC-019 CA03) : l'organisme a-t-il donne son accord ?</summary>
public sealed record SuiviVersion(string Version, bool Accord, string Organisme, DateOnly Le, string? Reference = null);

/// <summary>Les agents a former et formes d'un metier dans un greffe (SOC-026 ; comptes, jamais de noms).</summary>
public sealed record FormationMetier(string Metier, int AFormer, int Formes);
public sealed record FormationGreffe(string Code, IReadOnlyList<FormationMetier> Metiers);
public sealed record PlanFormation(IReadOnlyList<FormationGreffe> Greffes);

/// <summary>La validation d'une story par un greffe beta (SOC-022 CA05).</summary>
public sealed record ValidationBeta(string Story, string Greffe, string Par, DateOnly Le);

/// <summary>
/// Un defaut trouve pendant la beta (SOC-022 CA03, CA06). Les cases decrivent ce qu'il fait, pas sa gravite : c'est la
/// regle (<see cref="RegleDefauts"/>) qui dit s'il est bloquant, pour que deux personnes classent pareil.
/// </summary>
public sealed record DefautBeta(string Id, string Suivi, string Greffe, string Titre, string Etat,
                                bool DonneesPerduesOuFaussees = false, bool ActeOuRegistreFaux = false, bool ChainesOuFacturation = false,
                                bool DroitsOuConfidentialite = false, bool GesteImpossible = false, bool Contournement = false);

/// <summary>
/// LA REGLE ECRITE DE CLASSEMENT DES DEFAUTS DE LA BETA (SOC-022 CA03, proposee, a valider par le directeur de projet).
/// Bloquant si le defaut, une seule fois suffit :
///   1. perd ou fausse une donnee (transfert, recopie, saisie) ;
///   2. fausse un acte ou une inscription au registre (numero, Kbis, mention, radiation, depot des comptes) ;
///   3. touche une chaine signee, une facture ou un encaissement (NF525, SOC-018/019) ;
///   4. ouvre un droit ou une donnee a qui ne doit pas la voir ;
///   5. empeche un geste de l'agent SANS contournement connu.
/// Sinon non bloquant (gene, lenteur, libelle, geste empeche avec un contournement ecrit).
/// Un defaut sans identifiant de suivi n'est pas recevable (CA06 : il se signale avec son identifiant).
/// </summary>
public static class RegleDefauts
{
    public const string Ouvert = "ouvert";
    public const string Clos = "clos";

    public static (bool Bloquant, string Raison) Classer(DefautBeta d)
    {
        if (d.DonneesPerduesOuFaussees) return (true, "perd ou fausse une donnee");
        if (d.ActeOuRegistreFaux) return (true, "fausse un acte ou le registre");
        if (d.ChainesOuFacturation) return (true, "touche une chaine signee, une facture ou un encaissement");
        if (d.DroitsOuConfidentialite) return (true, "ouvre un droit ou une donnee a qui ne doit pas la voir");
        if (d.GesteImpossible && !d.Contournement) return (true, "empeche un geste sans contournement");
        return (false, d.GesteImpossible ? "geste empeche avec un contournement ecrit" : "gene sans perte ni geste empeche");
    }

    /// <summary>
    /// La liste des defauts ouverts, bloquants d'abord, avec leur identifiant de suivi ; puis les defauts SIGNALES DEPUIS
    /// LA V3 (SOC-022 CA06, ecran de l'agent, base commune) qui ne sont pas encore classes : aucun defauts-beta.json ne
    /// porte leur identifiant de suivi. Ils sont « A CLASSER » : la regle ne classe que ce qu'une personne a decrit
    /// (les cases de <see cref="DefautBeta"/>), jamais une phrase d'agent devinee.
    /// </summary>
    public static string Liste(IEnumerable<DefautBeta> defauts, IReadOnlyList<DefautSignale>? signales = null)
    {
        var tous = defauts.ToList();
        var s = new StringBuilder();
        foreach (var d in tous.Where(d => d.Etat == Ouvert).OrderByDescending(d => Classer(d).Bloquant).ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            var (b, raison) = Classer(d);
            s.AppendLine($"  {(b ? "BLOQUANT" : "non bloquant"),-12} {d.Id,-10} suivi {d.Suivi,-10} greffe {d.Greffe,-5} {d.Titre} ({raison})");
        }
        var aClasser = AClasser(tous, signales ?? []);
        foreach (var x in aClasser)
            s.AppendLine($"  {"A CLASSER",-12} {"(V3)",-10} suivi {x.Suivi,-10} greffe {x.CodeGreffe ?? "?",-5} {x.Phrase} " +
                         $"(signale depuis la V3 le {x.Instant:yyyy-MM-dd HH:mm} UTC par {x.Compte ?? "?"}, ecran « {x.Ecran} », version {x.Version ?? "?"}" +
                         $"{(x.DerniereErreur is { } e ? $", derniere erreur {e}" : "")})");
        if (aClasser.Count > 0)
            s.AppendLine($"  -> {aClasser.Count} defaut(s) signale(s) depuis la V3 a classer : les decrire dans defauts-beta.json sous leur identifiant de suivi (regle de SOC-022 CA03).");
        return s.Length == 0 ? "  aucun defaut ouvert\n" : s.ToString();
    }

    /// <summary>Les defauts signales depuis la V3 qu'aucun defaut du dossier ne decrit (meme identifiant de suivi).</summary>
    public static IReadOnlyList<DefautSignale> AClasser(IReadOnlyList<DefautBeta> defauts, IReadOnlyList<DefautSignale> signales)
    {
        var decrits = defauts.Select(d => (d.Suivi ?? string.Empty).Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        return [.. signales.Where(x => !decrits.Contains(x.Suivi.Trim().ToUpperInvariant())).OrderBy(x => x.Instant)];
    }
}

/// <summary>Ce qu'il faut savoir d'une story du carnet pour la beta.</summary>
public sealed record StoryCarnet(string Id, string Titre, string ValidePar, bool ValidationGreffeBeta);

/// <summary>
/// LE DOSSIER DE LA BETA : ce que l'outil lit pour juger les criteres (SOC-022 T01). Tout y est un fichier tenu par
/// une personne nommee, sauf le carnet (les stories), les bancs du depot et le catalogue des greffes.
///   certificat-v3.json, certificat-v2.json     les certificats (SOC-019 CA01, CA05)
///   repetitions\*.json                          les rapports de repetition generale (RigV3.Passage repeter)
///   explications.json, defauts-repetitions.json les ecarts expliques et les defauts des repetitions (SOC-025 CA02, CA05)
///   formation.json                              agents a former et formes, par greffe et par metier (SOC-026)
///   defauts-beta.json                           les defauts de la beta (SOC-022 CA03)
///   validations-beta.json                       les validations des greffes beta (SOC-022 CA05)
/// </summary>
public sealed record DossierBeta(IReadOnlyList<StoryCarnet> Stories, IReadOnlySet<string> StoriesCitees, Certificat? CertificatV3, Certificat? CertificatV2,
                                 IReadOnlyList<RapportRepetition> Repetitions, IReadOnlyList<Explication> Explications,
                                 IReadOnlyList<DefautRepetition> DefautsRepetitions, PlanFormation? Formation, IReadOnlyList<DefautBeta> Defauts,
                                 IReadOnlyList<ValidationBeta> Validations, IReadOnlyList<FicheGreffe> Catalogue)
{
    /// <summary>Les defauts signales par les agents depuis la V3 (SOC-022 CA06), lus dans la base commune
    /// (soc.DefautSignale, <see cref="LireSignalesAsync"/>) : la commande « beta » les ajoute au dossier.</summary>
    public IReadOnlyList<DefautSignale> Signales { get; init; } = [];

    /// <summary>Les defauts signales depuis la V3, dans la base commune <paramref name="commune"/> (la meme lecture que l'ecran
    /// Supervision). Une base commune sans la table (migration SOC-022_defauts pas livree) est un refus nomme, pas une liste vide.</summary>
    public static async Task<IReadOnlyList<DefautSignale>> LireSignalesAsync(string commune, CancellationToken ct)
    {
        await using var cn = new Microsoft.Data.SqlClient.SqlConnection(commune);
        await cn.OpenAsync(ct);
        await using (var existe = new Microsoft.Data.SqlClient.SqlCommand("SELECT OBJECT_ID('soc.DefautSignale', 'U');", cn))
            if (await existe.ExecuteScalarAsync(ct) is null or DBNull)
                throw new InvalidOperationException($"la base commune {cn.Database} n'a pas la table des defauts signales depuis la V3 " +
                                                    "(migration SOC-022_defauts) : la livrer avant de lire les defauts de la beta.");
        return await RigV3.Serveur.Socle.Supervision.DefautsSignales.LireAsync(cn, null, null, ct);
    }

    public static DossierBeta Lire(string carnet, string dossier, string dossierBancs, IReadOnlyList<FicheGreffe> catalogue)
    {
        T? Fichier<T>(string nom) where T : class
            => File.Exists(Path.Combine(dossier, nom)) ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dossier, nom)), RapportPassage.Json) : null;
        var stories = LireCarnet(carnet);
        var repetitions = Directory.Exists(Path.Combine(dossier, "repetitions"))
            ? Directory.GetFiles(Path.Combine(dossier, "repetitions"), "*.json").Select(RapportRepetition.Lire).ToList()
            : [];
        return new DossierBeta(stories, StoriesCiteesParLesBancs(dossierBancs, stories), Fichier<Certificat>("certificat-v3.json"),
            Fichier<Certificat>("certificat-v2.json"), repetitions,
            ComparaisonRepetitions.LireExplications(Path.Combine(dossier, "explications.json")),
            ComparaisonRepetitions.LireDefauts(Path.Combine(dossier, "defauts-repetitions.json")),
            Fichier<PlanFormation>("formation.json"), Fichier<List<DefautBeta>>("defauts-beta.json") ?? [],
            Fichier<List<ValidationBeta>>("validations-beta.json") ?? [], catalogue);
    }

    /// <summary>Les stories du carnet : chaque *.json qui est une liste de stories, sauf les non reprises et le schema.</summary>
    public static IReadOnlyList<StoryCarnet> LireCarnet(string carnet)
    {
        var stories = new List<StoryCarnet>();
        foreach (var f in Directory.GetFiles(carnet, "*.json").Order(StringComparer.Ordinal))
        {
            var nom = Path.GetFileName(f);
            if (nom.StartsWith("non-repris", StringComparison.OrdinalIgnoreCase) || nom.Equals("schema.json", StringComparison.OrdinalIgnoreCase)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
            foreach (var s in doc.RootElement.EnumerateArray())
                if (s.TryGetProperty("id", out var id))
                    stories.Add(new StoryCarnet(id.GetString()!, s.TryGetProperty("titre", out var t) ? t.GetString() ?? "" : "",
                        s.TryGetProperty("valide_par", out var v) ? v.GetString() ?? "" : "",
                        s.TryGetProperty("validation_greffe_beta", out var b) && b.ValueKind == JsonValueKind.True));
        }
        return stories;
    }

    /// <summary>Les stories qu'au moins un banc cite (regle 3 du plan : chaque critere est un controle de banc qui cite son id).</summary>
    public static IReadOnlySet<string> StoriesCiteesParLesBancs(string dossierBancs, IReadOnlyList<StoryCarnet> stories)
    {
        var texte = new StringBuilder();
        if (Directory.Exists(dossierBancs))
            foreach (var f in Directory.EnumerateFiles(dossierBancs, "*.cs", SearchOption.AllDirectories))
            {
                var morceaux = f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (morceaux.Any(m => m is "bin" or "obj" || m.StartsWith("binverif", StringComparison.OrdinalIgnoreCase))) continue;
                texte.Append(File.ReadAllText(f)).Append('\n');
            }
        var tout = texte.ToString();
        return stories.Select(s => s.Id).Where(id => Regex.IsMatch(tout, $@"(?<![A-Z0-9-]){Regex.Escape(id)}(?![0-9])")).ToHashSet(StringComparer.Ordinal);
    }
}

/// <summary>
/// LES CRITERES ECRITS DE LA BETA (SOC-022 CA01, CA02, CA04 ; SOC-019 CA01, CA05). L'outil juge, il ne decide pas a la
/// place des personnes : chaque critere lit un fichier tenu par quelqu'un, ou le depot, ou le catalogue. Aucun critere
/// n'est une date : une date seule ne demarre ni ne termine jamais la beta (CA04).
/// </summary>
public static class CriteresBeta
{
    /// <summary>Les greffes beta decides (cadrage.md:42, decision Q8) : ceux que la V2 marque beta, et aucun autre (CA02).</summary>
    public static readonly IReadOnlyList<string> GreffesBeta = ["Le Puy", "Chartres", "Vienne"];

    public const string AValider = "à valider";

    public static IReadOnlyList<Constat> Demarrage(DossierBeta d, string versionV3, DateOnly aujourdhui)
    {
        var c = new List<Constat>
        {
            V3Complete(d),
            StoriesValidees(d, "SOC-022 CA01"),
            CertificatValide(d.CertificatV3, "SOC-019 CA01 / SOC-022 CA01 : certificat de la V3", aujourdhui, versionV3),
            CertificatValide(d.CertificatV2, "SOC-019 CA05 : certificat de la V2 a jour", aujourdhui, null),
            Repetitions(d, "SOC-022 CA01 : repetitions generales sans ecart"),
            Formation(d, aujourdhui),
            GreffesBetaDuCatalogue(d),
        };
        return c;
    }

    public static IReadOnlyList<Constat> Fin(DossierBeta d)
    {
        var bloquants = d.Defauts.Where(x => x.Etat == RegleDefauts.Ouvert && RegleDefauts.Classer(x).Bloquant).ToList();
        var sansSuivi = d.Defauts.Where(x => string.IsNullOrWhiteSpace(x.Suivi)).Select(x => x.Id).ToList();
        // Un defaut signale depuis la V3 et pas encore classe peut etre bloquant : la beta ne se termine pas sur lui.
        var aClasser = RegleDefauts.AClasser(d.Defauts, d.Signales);
        var c = new List<Constat>
        {
            new("SOC-022 CA04 : aucun defaut bloquant ouvert", bloquants.Count == 0 && sansSuivi.Count == 0 && aClasser.Count == 0,
                sansSuivi.Count > 0 ? $"defaut(s) sans identifiant de suivi, non recevable(s) : {string.Join(", ", sansSuivi)}"
                : aClasser.Count > 0 ? $"{aClasser.Count} defaut(s) signale(s) depuis la V3 pas encore classe(s) dans defauts-beta.json : {string.Join(", ", aClasser.Select(x => x.Suivi))}"
                : bloquants.Count == 0 ? $"{d.Defauts.Count(x => x.Etat == RegleDefauts.Ouvert)} defaut(s) ouvert(s), aucun bloquant (regle de SOC-022 CA03)"
                : $"{bloquants.Count} defaut(s) bloquant(s) ouvert(s) : {string.Join(", ", bloquants.Select(x => $"{x.Id} [{x.Suivi}] {RegleDefauts.Classer(x).Raison}"))}"),
            Repetitions(d, "SOC-022 CA04 : transferts repetes sans ecart"),
            StoriesValidees(d, "SOC-022 CA04 : validation des referents"),
            ValidationsBeta(d),
            GreffesBetaDuCatalogue(d),
        };
        return c;
    }

    private static Constat V3Complete(DossierBeta d)
    {
        var manque = d.Stories.Where(s => !d.StoriesCitees.Contains(s.Id)).Select(s => s.Id).ToList();
        return new("SOC-022 CA01 : V3 complete (chaque story citee par un banc)", manque.Count == 0 && d.Stories.Count > 0,
            manque.Count == 0 ? $"les {d.Stories.Count} stories du carnet ont un controle de banc qui les cite"
            : $"{manque.Count} story(s) sur {d.Stories.Count} sans controle de banc : {Extrait(manque)}");
    }

    private static Constat StoriesValidees(DossierBeta d, string critere)
    {
        var manque = d.Stories.Where(s => string.IsNullOrWhiteSpace(s.ValidePar) || s.ValidePar.Trim() == AValider).Select(s => s.Id).ToList();
        return new(critere.Contains("referents") ? critere : $"{critere} : toutes les stories validees par leur referent",
            manque.Count == 0 && d.Stories.Count > 0,
            manque.Count == 0 ? $"les {d.Stories.Count} stories ont leur referent (valide_par)"
            : $"{manque.Count} story(s) sur {d.Stories.Count} encore « {AValider} » : {Extrait(manque)}");
    }

    private static Constat CertificatValide(Certificat? cert, string critere, DateOnly aujourdhui, string? versionV3)
    {
        if (cert is null) return new(critere, false, "aucun certificat au dossier (certificat-v3.json / certificat-v2.json)");
        var trous = RegleCertificat.Manques(cert, aujourdhui, versionV3);
        return new(critere, trous.Count == 0, trous.Count == 0
            ? $"{cert.Logiciel} : certificat {cert.Numero} de {cert.Organisme} ({cert.Accreditation}), {cert.Referentiel}, version {cert.VersionCouverte}, valide jusqu'au {cert.ValideJusquau:yyyy-MM-dd}"
            : string.Join(" ; ", trous));
    }

    private static Constat Repetitions(DossierBeta d, string critere)
    {
        var constats = ComparaisonRepetitions.Comparer(d.Repetitions, new ComparaisonRepetitions.Regles
        {
            Explications = d.Explications,
            Defauts = d.DefautsRepetitions,
        });
        var faux = constats.Where(x => !x.Ok).ToList();
        return new(critere, d.Repetitions.Count > 0 && faux.Count == 0, d.Repetitions.Count == 0
            ? "aucun rapport de repetition au dossier (repetitions\\*.json)"
            : faux.Count == 0 ? $"{d.Repetitions.Count} repetition(s) : toutes les regles de SOC-025 tenues"
            : string.Join(" | ", faux.Select(x => $"{x.Critere} {x.Detail}")));
    }

    private static Constat Formation(DossierBeta d, DateOnly aujourdhui)
    {
        const string critere = "SOC-022 CA01 / SOC-026 CA03 : formation des greffes beta faite";
        if (d.Formation is null) return new(critere, false, "aucun plan de formation au dossier (formation.json, tire de RigV3.Passage formation)");
        var codes = d.Catalogue.Where(f => f.EstBeta).Select(f => f.Code).ToList();
        var trous = new List<string>();
        foreach (var code in codes)
        {
            var g = d.Formation.Greffes.FirstOrDefault(x => x.Code == code);
            if (g is null || g.Metiers.Sum(m => m.AFormer) == 0) { trous.Add($"{code} : aucun agent compte"); continue; }
            foreach (var m in g.Metiers.Where(m => m.Formes < m.AFormer))
                trous.Add($"{code} {m.Metier} : {m.Formes} forme(s) sur {m.AFormer}");
        }
        return new(critere, codes.Count > 0 && trous.Count == 0, trous.Count == 0
            ? $"les {codes.Count} greffes beta : {string.Join(", ", codes.Select(c => $"{c} {d.Formation.Greffes.First(g => g.Code == c).Metiers.Sum(m => m.Formes)} agent(s) forme(s)"))}"
            : string.Join(" ; ", trous));
    }

    private static Constat GreffesBetaDuCatalogue(DossierBeta d)
    {
        static string Norme(string s) => new string(s.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                                                      .ToArray()).Replace('-', ' ').Trim().ToUpperInvariant();
        var beta = d.Catalogue.Where(f => f.EstBeta).ToList();
        var attendus = GreffesBeta.Select(Norme).ToHashSet();
        var trouves = beta.Select(f => Norme(f.Nom)).ToHashSet();
        var ok = trouves.SetEquals(attendus);
        return new("SOC-022 CA02 : greffes beta = ceux que la V2 marque beta", ok, ok
            ? $"{string.Join(", ", beta.Select(f => $"{f.Nom} ({f.Code})"))}, et aucun autre"
            : $"le catalogue marque beta {(beta.Count == 0 ? "aucun greffe" : string.Join(", ", beta.Select(f => $"{f.Nom} ({f.Code})")))} ; decision Q8 : {string.Join(", ", GreffesBeta)}");
    }

    private static Constat ValidationsBeta(DossierBeta d)
    {
        var beta = d.Catalogue.Where(f => f.EstBeta).Select(f => f.Code).ToList();
        var manque = new List<string>();
        foreach (var s in d.Stories.Where(s => s.ValidationGreffeBeta))
        {
            var absents = beta.Where(g => !d.Validations.Any(v => v.Story == s.Id && v.Greffe == g)).ToList();
            if (absents.Count > 0) manque.Add($"{s.Id} ({string.Join("/", absents)})");
        }
        var nombre = d.Stories.Count(s => s.ValidationGreffeBeta);
        return new("SOC-022 CA05 : validations des greffes beta enregistrees", beta.Count > 0 && manque.Count == 0, manque.Count == 0
            ? $"les {nombre} stories qui changent le geste de l'agent sont validees par les {beta.Count} greffes beta"
            : $"{manque.Count} story(s) sur {nombre} sans la validation de chaque greffe beta : {Extrait(manque)}");
    }

    private static string Extrait(IReadOnlyList<string> ids) => ids.Count <= 12 ? string.Join(", ", ids) : string.Join(", ", ids.Take(12)) + $", ... (+{ids.Count - 12})";
}

/// <summary>
/// LE SUIVI DE CERTIFICATION D'UNE VERSION (SOC-019 CA03) : une version qui touche une fonction certifiee ne se
/// livre qu'avec l'accord ecrit de l'organisme. Le perimetre certifie est celui que decrit le dossier de
/// certification (docs\certification\dossier-certification-v3.md) ; un fichier qui en releve et change rend le suivi
/// obligatoire. L'evenement 250 / 255 de l'installation est ecrit par la livraison (Outils\Livraison, SOC-009 CA06).
/// </summary>
public static class SuiviCertification
{
    /// <summary>Les fichiers des fonctions certifiees (chemins du depot, « / ») : chaines, signature, journal,
    /// clotures, archive, purge ; et les futurs domaines qui signent (facturation, encaissements).</summary>
    public static readonly IReadOnlyList<string> Perimetre =
    [
        "Serveur/Socle/Chaines/",
        "Base/migrations/SOC-018",
        "Contrats/Socle/Chaines.cs",
        "Pont/EcranChaines.cs",
        "web/src/ecrans/EcranChaines.tsx",
        "Serveur/Facturation/",
        "Base/migrations/FAC-",
    ];

    public static IReadOnlyList<string> Touches(IEnumerable<string> fichiers)
        => [.. fichiers.Select(f => f.Trim().Replace('\\', '/')).Where(f => f.Length > 0 && Perimetre.Any(p => f.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                       .Distinct(StringComparer.OrdinalIgnoreCase)];

    public static Constat Juger(string version, IEnumerable<string> fichiers, IReadOnlyList<SuiviVersion> registre)
    {
        var touches = Touches(fichiers);
        if (touches.Count == 0)
            return new("SOC-019 CA03", true, $"version {version} : aucune fonction certifiee touchee, livraison sans suivi");
        var accord = registre.FirstOrDefault(s => s.Version == version && s.Accord);
        return new("SOC-019 CA03", accord is not null, accord is not null
            ? $"version {version} : {touches.Count} fichier(s) certifie(s) touche(s), accord de {accord.Organisme} du {accord.Le:yyyy-MM-dd} ({accord.Reference})"
            : $"version {version} : {touches.Count} fichier(s) certifie(s) touche(s) ({string.Join(", ", touches.Take(5))}) sans accord au suivi de certification : livraison refusee");
    }
}
