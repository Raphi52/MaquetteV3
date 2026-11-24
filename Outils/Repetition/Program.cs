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
//   RigV3.Repetition retour (--source <chaine V2> --accord <BASE> | --copie <chaine copie anonymisee> --mois N)
//                    --rapport <dossier> [--max-lignes N] [--mois N] [--fenetre 10] [--greffe <code>] [--garder] [--graine <texte>]
//       la repetition du retour a l'ancien RIG (REC-007 CA07, RepetitionRetour.cs) : la repetition du transfert, le greffe
//       mis en beta, un mois d'affaires de la V3 (N demandes ; par defaut le mois le plus charge de la source), le retour
//       chronometre, le controle de nuit ensuite et sa contre-epreuve ; rapport retour-<date>.json et .md.
//   RigV3.Repetition supprimer --base rigv3_rep_<...>
//       supprime une base gardee par --garder, sur le serveur des bancs de ce poste seulement.
//   RigV3.Repetition donnees-dev [--source <chaine V2> --accord <BASE>] [--max-lignes 200] [--greffe 3801] [--refaire]
//                    [--fichier <json>] [--rapport <dossier>]
//       les donnees de developpement de l'app (DonneesDev.cs) : copie anonymisee de RIG_DEV (par defaut), transferee dans une
//       base V3, base commune avec le greffe au catalogue, agent choisi ; ecrit %LOCALAPPDATA%\RigV3\donnees-dev.json, que
//       donnees-dev.ps1 relit pour lancer le serveur. Deja pretes : rien n'est refait (--refaire pour les refaire).
// Code de sortie : repeter 0 toutes les bases au bout, 1 sinon ; feu 0 vert, 4 rouge ; retour 0 vert, 1 rouge ; donnees-dev 0 pretes, 1 sinon ;
// 2 commande mal formee.
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
            DossierMigrations = Opt("migrations") ?? Dossier("Base", "migrations"),
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
    case "retour" when Opt("rapport") is { } dossier && (Opt("source") is not null ^ Opt("copie") is not null)
                       && (Opt("copie") is null || Opt("mois") is not null):
    {
        var rapport = await RepetitionRetour.RepeterAsync(new OptionsRetour
        {
            Source = Opt("source"),
            Copie = Opt("copie"),
            Accord = Opt("accord"),
            DossierRapport = dossier,
            DossierMigrations = Opt("migrations") ?? Dossier("Base", "migrations"),
            DossierScriptsV2 = Opt("scripts") ?? Dossier("Base", "v2"),
            MaxLignes = Opt("max-lignes") is { } m ? long.Parse(m, CultureInfo.InvariantCulture) : null,
            DemandesDuMois = Opt("mois") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : null,
            FenetreHeures = Opt("fenetre") is { } w ? double.Parse(w, CultureInfo.InvariantCulture) : 10,
            Greffe = Opt("greffe"),
            Garder = o.ContainsKey("garder"),
            Graine = Opt("graine"),
            Sortie = Console.Out,
        });
        Console.WriteLine();
        Console.WriteLine(RepetitionRetour.EnMarkdown(rapport));
        return rapport.Vert ? 0 : 1;
    }
    case "donnees-dev":
    {
        // Les donnees de developpement de l'app (DonneesDev.cs) : la source par defaut est RIG_DEV, lue sur SQL-DEV\DEV.
        var source = Opt("source") ?? "Server=SQL-DEV\\DEV;Database=RIG_DEV;Integrated Security=True;TrustServerCertificate=True;Application Name=RigV3.DonneesDev";
        try
        {
            var d = await PreparationDonneesDev.PreparerAsync(new OptionsDonneesDev
            {
                Source = source,
                Accord = Opt("accord") ?? (Opt("source") is null ? "RIG_DEV" : null),
                MaxLignes = Opt("max-lignes") is { } m ? long.Parse(m, CultureInfo.InvariantCulture) : 200,
                Greffe = Opt("greffe") ?? "3801",
                DossierRapport = Opt("rapport") ?? Path.Combine(Path.GetDirectoryName(PreparationDonneesDev.FichierParDefaut)!, "donnees-dev-rapports"),
                DossierMigrations = Opt("migrations") ?? Dossier("Base", "migrations"),
                Fichier = Opt("fichier") ?? PreparationDonneesDev.FichierParDefaut,
                Refaire = o.ContainsKey("refaire"),
                Sortie = Console.Out,
            });
            Console.WriteLine($"Pretes : greffe {d.Greffe} ({d.NomGreffe}), base V3 {d.BaseV3}, base commune {d.Commune}, agent {d.Compte} ({d.NomAgent}).");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or Microsoft.Data.SqlClient.SqlException or IOException or RigV3.Outils.Anonymisation.RefusAnonymisation)
        {
            Console.Error.WriteLine($"Donnees de developpement NON pretes : {e.Message}");
            return 1;
        }
    }
    case "supprimer" when Opt("base") is { } nomBase:
    {
        // Les bases qu'une repetition a gardees (--garder), une fois lues : sur ce poste seulement, et seulement elles.
        if (!nomBase.StartsWith("rigv3_rep_", StringComparison.OrdinalIgnoreCase) || nomBase.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
        {
            Console.Error.WriteLine($"Refuse : « {nomBase} » n'est pas une base de repetition (rigv3_rep_...).");
            return 2;
        }
        await Repetition.SupprimerAsync(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(RigV3.Base.BaseJetable.ServeurDesBancs()) { InitialCatalog = nomBase }.ConnectionString);
        Console.WriteLine($"{nomBase} supprimee (ou deja absente).");
        return 0;
    }
    default:
        Console.Error.WriteLine("Usage : RigV3.Repetition repeter (--source <chaine> | --sources <fichier> | --copie <chaine>) --rapport <dossier> [...]");
        Console.Error.WriteLine("        RigV3.Repetition feu --rapports <dossier> [--jour-j AAAA-MM-JJ] [--attendues 40] [--fenetre 10] [--parallele 1]");
        Console.Error.WriteLine("        RigV3.Repetition retour (--source <chaine> --accord <BASE> | --copie <chaine> --mois N) --rapport <dossier> [--max-lignes N] [--mois N] [--garder]");
        Console.Error.WriteLine("        RigV3.Repetition supprimer --base rigv3_rep_<...>");
        Console.Error.WriteLine("        RigV3.Repetition donnees-dev [--source <chaine> --accord <BASE>] [--max-lignes 200] [--greffe 3801] [--refaire]");
        return 2;
}

// Un dossier du depot (Base\migrations, Base\v2) : en remontant depuis l'outil.
static string Dossier(params string[] chemin)
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine([d.FullName, .. chemin]))) return Path.Combine([d.FullName, .. chemin]);
    throw new DirectoryNotFoundException($"{Path.Combine(chemin)} introuvable au-dessus de l'outil : passer --migrations / --scripts <dossier>.");
}
