using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace RigV3.Outils.Anonymisation;

// L'OUTIL D'ANONYMISATION, en ligne de commande (SOC-021 ; mode d'emploi :
// docs/reconstruction/socle-anonymisation.md).
//
//   dotnet run --project Outils/Anonymisation -- copier    --source "<chaine V2>" --cible "<chaine copie>" --rapport <fichier.md>
//                                                          [--accord <BASE>] [--tables A,B] [--max-lignes N] [--regles <fichier>] [--graine <texte>]
//   dotnet run --project Outils/Anonymisation -- controler --source "<chaine V2>" --cible "<chaine copie>" [--accord <BASE>] [--echantillon 200]
//   dotnet run --project Outils/Anonymisation -- inventaire --source "<chaine V2>" --sortie <fichier.md> [--accord <BASE>] [--regles <fichier>]
//
//   --source   la base V2 (Database=… obligatoire), LUE SEULEMENT
//   --cible    la copie : serveur de CE poste seulement (docker compose up -d) ; base absente ou vide
//   --accord   source hors de ce poste : son nom de base, l'accord donne pour CETTE base (CA07)
//   --regles   remplacements relus « TABLE.COLONNE=Garder|Mots|Courriel|Fichier|Telephone|DateNaissance|Vider|Xml »
//   --graine   valeurs fictives reproductibles (source sur ce poste seulement)
//
// Code de sortie : 0 = fait (controle : rien trouve), 1 = refus ou echec, 2 = mauvais usage,
// 3 = le controle a trouve une valeur reelle, un volume ou un numero different, ou une marque absente.
public static class Programme
{
    public static Task<int> Main(string[] args) => ExecuterAsync(args, Console.Out, Console.Error);

    public static async Task<int> ExecuterAsync(string[] args, TextWriter sortie, TextWriter erreurs)
    {
        if (args.Length == 0 || args[0] is not ("copier" or "controler" or "inventaire"))
        {
            erreurs.WriteLine("Commande attendue : copier | controler | inventaire (voir l'en-tete de Program.cs).");
            return 2;
        }
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                erreurs.WriteLine($"Argument inattendu : {args[i]}");
                return 2;
            }
            o[args[i][2..]] = args[++i];
        }
        string? Opt(string n) => o.GetValueOrDefault(n);
        if (Opt("source") is not { } source || (args[0] != "inventaire" && Opt("cible") is null))
        {
            erreurs.WriteLine("--source et --cible sont obligatoires (inventaire : --source et --sortie).");
            return 2;
        }

        try
        {
            var remplacements = Opt("regles") is { } f ? CatalogueRegles.LireRemplacements(f).ToList() : null;
            switch (args[0])
            {
                case "copier":
                {
                    if (Opt("rapport") is null) { erreurs.WriteLine("--rapport <fichier.md> est obligatoire : une copie sans rapport ne se livre pas (CA04)."); return 2; }
                    var rapport = await CopieAnonymisee.CopierAsync(new OptionsCopie
                    {
                        Source = source,
                        Cible = Opt("cible")!,
                        Accord = Opt("accord"),
                        Graine = Opt("graine"),
                        Tables = Opt("tables")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        MaxLignes = Opt("max-lignes") is { } m ? long.Parse(m, CultureInfo.InvariantCulture) : null,
                        Remplacements = remplacements,
                        FichierRapport = Opt("rapport"),
                        Journal = sortie,
                    });
                    sortie.WriteLine($"Copie faite : {rapport.Volumes.Count} tables, {rapport.Colonnes.Count} colonnes traitees, " +
                                     $"chaines {(rapport.ChainesInvalidees ? "INVALIDEES" : "intactes")}, {rapport.Anomalies.Count} anomalie(s). Rapport : {Opt("rapport")}");
                    return rapport.Anomalies.Count == 0 ? 0 : 1;
                }
                case "controler":
                {
                    var n = Opt("echantillon") is { } e ? int.Parse(e, CultureInfo.InvariantCulture) : 200;
                    var r = await ControleCopie.ControlerAsync(source, Opt("cible")!, Opt("accord"), n, journal: sortie, remplacements: remplacements);
                    sortie.WriteLine($"Marque : {(r.Marquee ? "presente" : "ABSENTE")} ; chaines {(r.ChainesInvalidees ? "invalidees" : "intactes")}");
                    sortie.WriteLine($"Echantillon : {r.ColonnesControlees} colonnes personnelles, {r.EchantillonValeurs} valeurs et {r.EchantillonMots} mots reels (empreintes) ; retrouves : {(r.Fuites.Count == 0 ? "aucun" : string.Join(", ", r.Fuites))}");
                    sortie.WriteLine($"Volumes : {r.TablesComparees} tables, differents : {(r.VolumesDifferents.Count == 0 ? "aucun" : string.Join(", ", r.VolumesDifferents))}");
                    sortie.WriteLine($"Colonnes gardees : {r.LignesComparees} lignes tirees, changees : {(r.NumerosChanges.Count == 0 ? "aucune" : string.Join(", ", r.NumerosChanges))}");
                    sortie.WriteLine($"Tables sans cle primaire simple, non tirees : {(r.TablesSansCle.Count == 0 ? "aucune" : string.Join(", ", r.TablesSansCle))}");
                    sortie.WriteLine($"A vider et non vides : {(r.NonVidees.Count == 0 ? "aucune" : string.Join(", ", r.NonVidees))}");
                    var enClair = r.Gardees.Where(g => g.Prenoms).Select(g => $"{g.Colonne} ({g.Part:P0} de {g.Valeurs} valeurs ; gardee : {g.Raison})").ToList();
                    sortie.WriteLine($"Colonnes gardees a segment personnel : {r.Gardees.Count(g => g.Valeurs > 0)} tirees (lexique : {r.LexiquePrenoms} prenoms de {r.ColonnesLexique} colonnes) ; " +
                                     $"prenoms en clair : {(enClair.Count == 0 ? "aucune" : string.Join(", ", enClair))}" +
                                     (r.GardeesNonJugees ? " -- NON JUGEES : la base n'a donne aucun prenom" : ""));
                    return r.Vert ? 0 : 3;
                }
                default:
                {
                    if (Opt("sortie") is not { } fichier) { erreurs.WriteLine("--sortie <fichier.md> est obligatoire."); return 2; }
                    var (texte, traitees, aRelire, total) = await InventaireAsync(source, Opt("accord"), remplacements);
                    await File.WriteAllTextAsync(fichier, texte);
                    sortie.WriteLine($"Inventaire : {total} colonnes, {traitees} traitees, {aRelire} a relire. Fichier : {fichier}");
                    return 0;
                }
            }
        }
        catch (Exception e) when (e is RefusAnonymisation or InvalidOperationException or ArgumentException or SqlException or IOException)
        {
            erreurs.WriteLine($"Refus ou echec : {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// L'INVENTAIRE (SOC-021 T01) : la regle que la copie appliquerait a chaque colonne de la source, et
    /// les colonnes a relire. Lecture du schema seulement : aucune donnee n'est lue.
    /// </summary>
    public static async Task<(string Texte, int Traitees, int ARelire, int Total)> InventaireAsync(
        string source, string? accord, IEnumerable<KeyValuePair<string, Traitement>>? remplacements)
    {
        await using var src = await PorteSource.OuvrirAsync(source, accord, CancellationToken.None);
        var schema = await SchemaSource.LireAsync(src, CancellationToken.None);
        var catalogue = new CatalogueRegles(remplacements);
        catalogue.AlignerSurLesCles(schema);
        var sb = new StringBuilder();
        sb.AppendLine($"# Inventaire des colonnes personnelles — {src.Serveur} / {src.Base} ({DateTime.Now:yyyy-MM-dd})").AppendLine();
        sb.AppendLine("Schéma seulement, aucune donnée lue. À relire par le délégué à la protection des données (SOC-021 T01) ;")
          .AppendLine("une correction s'écrit dans un fichier `--regles` (TABLE.COLONNE=traitement), sans toucher au code.").AppendLine();
        sb.AppendLine("| Colonne | Type | Traitement | Origine |").AppendLine("|---|---|---|---|");
        int traitees = 0, aRelire = 0, total = 0;
        var relire = new List<string>();
        foreach (var t in schema.Tables)
            foreach (var c in t.Colonnes)
            {
                total++;
                var r = catalogue.Pour(t.Nom, c.Nom, c.Type);
                if (r.Anonymise)
                {
                    traitees++;
                    sb.AppendLine($"| {t.Libelle}.{c.Nom} | {c.Type} | {r.Traitement} | {r.Origine} |");
                    if (r.Origine.Contains("a relire")) aRelire++;
                }
                else if (catalogue.Suspicion(t.Nom, c.Nom, c.Type) is { } raison)
                {
                    aRelire++;
                    relire.Add($"- `{t.Libelle}.{c.Nom}` ({c.Type}) : {raison}");
                }
            }
        sb.AppendLine().AppendLine("## Gardées, à relire").AppendLine();
        foreach (var l in relire) sb.AppendLine(l);
        return (sb.ToString(), traitees, aRelire, total);
    }
}
