using System.Globalization;
using RigV3.Repetition;

// ============================================================
// LA REPETITION DU TRANSFERT SUR DES COPIES ANONYMISEES (REC-008 ; lot 8 du plan, 2026-11-21).
// Mode d'emploi complet : docs/reconstruction/recopie.md, « Repeter le transfert ».
//
//   RigV3.Repetition repeter (--source <chaine V2> | --sources <fichier> | --copie <chaine copie anonymisee>)
//                    --rapport <dossier> [--accord <BASE>] [--max-lignes N] [--echantillon 1000] [--lot 50000]
//                    [--migrations <dossier>] [--garder] [--graine <texte>]
//       pour chaque base : copie anonymisee sur ce poste (la source est LUE, jamais copiee en clair) et son controle,
//       transfert dans une base V3 neuve (regles provisoires pour les tables sans regle), controle des deux bases,
//       mesures ; rapport repetition-<date>.json et .md dans le dossier. --sources : une chaine par ligne.
//       Une source hors de ce poste exige --accord <son nom de base> (SOC-021 CA07) ; la copie, elle, ne s'ecrit
//       QUE sur ce poste. --copie : une copie deja faite, refusee si elle n'est pas marquee anonymisee (CA05).
//   RigV3.Repetition feu --rapports <dossier> [--jour-j AAAA-MM-JJ] [--attendues 40] [--fenetre 10] [--parallele 1]
//       juge le feu vert de la beta ou du jour J sur la derniere repetition du dossier (CA03, CA04).
// Code de sortie : repeter 0 toutes les bases au bout, 1 sinon ; feu 0 vert, 4 rouge ; 2 commande mal formee.
// ============================================================

var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal)) { Console.Error.WriteLine($"option inattendue : {args[i]}"); return 2; }
    o[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "";
}
string? Opt(string n) => o.GetValueOrDefault(n);

switch (args.FirstOrDefault())
{
    case "repeter" when Opt("rapport") is { } dossier && (Opt("source") ?? Opt("sources") ?? Opt("copie")) is not null:
    {
        var sources = Opt("sources") is { } f ? File.ReadAllLines(f).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
                    : Opt("source") is { } s ? [s] : [];
        var rapport = await Repetition.RepeterAsync(new OptionsRepetition
        {
            Sources = sources,
            Copies = Opt("copie") is { } c ? [c] : [],
            Accord = Opt("accord"),
            DossierRapport = dossier,
            DossierMigrations = Opt("migrations") ?? Migrations(),
            MaxLignes = Opt("max-lignes") is { } m ? long.Parse(m, CultureInfo.InvariantCulture) : null,
            Echantillon = Opt("echantillon") is { } e ? int.Parse(e, CultureInfo.InvariantCulture) : 1000,
            TailleLot = Opt("lot") is { } l ? int.Parse(l, CultureInfo.InvariantCulture) : 50_000,
            Garder = o.ContainsKey("garder"),
            Graine = Opt("graine"),
            Sortie = Console.Out,
        });
        Console.WriteLine();
        Console.WriteLine(Repetition.EnMarkdown(rapport));
        return rapport.Bases.All(b => b.Etat == "fini") ? 0 : 1;
    }
    case "feu" when Opt("rapports") is { } dossier:
    {
        var rapports = Feu.Lire(dossier);
        if (rapports.Count == 0) { Console.Error.WriteLine($"Aucun rapport repetition-*.json dans {dossier}."); return 2; }
        var feu = Feu.Juger(rapports[^1], rapports.Take(rapports.Count - 1), new OptionsFeu(
            BasesAttendues: Opt("attendues") is { } a ? int.Parse(a, CultureInfo.InvariantCulture) : 40,
            FenetreHeures: Opt("fenetre") is { } w ? double.Parse(w, CultureInfo.InvariantCulture) : 10,
            Parallele: Opt("parallele") is { } p ? int.Parse(p, CultureInfo.InvariantCulture) : 1,
            JourJ: Opt("jour-j") is { } j ? DateOnly.ParseExact(j, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null));
        Console.WriteLine(Feu.Texte(feu));
        return feu.Vert ? 0 : 4;
    }
    default:
        Console.Error.WriteLine("Usage : RigV3.Repetition repeter (--source <chaine> | --sources <fichier> | --copie <chaine>) --rapport <dossier> [...]");
        Console.Error.WriteLine("        RigV3.Repetition feu --rapports <dossier> [--jour-j AAAA-MM-JJ] [--attendues 40] [--fenetre 10] [--parallele 1]");
        return 2;
}

// Le dossier Base\migrations du depot : en remontant depuis l'outil.
static string Migrations()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "Base", "migrations"))) return Path.Combine(d.FullName, "Base", "migrations");
    throw new DirectoryNotFoundException("Base\\migrations introuvable au-dessus de l'outil : passer --migrations <dossier>.");
}
