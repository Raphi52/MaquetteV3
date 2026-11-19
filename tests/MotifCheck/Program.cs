using System.IO;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

// Garde-fou du MENU QUI NE MENE NULLE PART (16/09/2026).
//
// Constat mesure : 166 codes distincts dans le menu de gauche, 33 branches
// dans l'aiguillage de Home.razor. Toutes les autres entrees tombaient sur
// l'ecran d'attente avec le meme texte generique « Cet ecran n'est pas encore
// disponible » -- en demonstration, quatre clics sur cinq ne disaient RIEN.
// Ce banc exige que CHAQUE entree de menu sans ecran porte rende un motif
// non vide, dit avec les mots du metier.

internal static class Programme
{
    private const BindingFlags Prive =
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [STAThread]
    private static int Main()
    {
        var echecs = new List<string>();
        void Exiger(bool ok, string quoi)
        {
            Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
            if (!ok) echecs.Add(quoi);
        }

        var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        // Depuis le lot 2 du passage a React (19/11/2026), l'aiguillage des ecrans
        // est web/src/plan/Aiguillage.tsx (le @switch de Home.razor) et le motif
        // d'un ecran a venir est calcule par la session du plan de travail
        // (Pont/PlanTravail.cs, le @code de Home.razor deplace tel quel).
        var source = File.ReadAllText(Path.Combine(racine, "web/src/plan/Aiguillage.tsx"));

        // Les codes reellement aiguilles vers un ecran porte.
        var portes = Regex.Matches(source, "case '(?<c>[A-Z_0-9]+)'")
            .Select(m => m.Groups["c"].Value).ToHashSet();
        Exiger(portes.Count > 20, $"l'aiguillage expose bien ses branches ({portes.Count} lues)");
        // Le cas par defaut de l'aiguillage montre bien l'ecran qui se nomme, avec son motif.
        Exiger(Regex.IsMatch(source, @"default:\s*return <EcranNonMaquette code=\{proc\.code\} libelle=\{proc\.libelle\} motif=\{ecran\.motif\} />"),
            "une entree sans ecran tombe sur l'ecran qui se nomme, avec son motif");

        // Le menu, lu dans l'objet lui-meme et non dans le texte.
        var plan = typeof(RigV3Desktop.Pont.SessionPlanTravail);
        var instance = new RigV3Desktop.Pont.SessionPlanTravail(null, null);
        var moments = (IEnumerable)plan.GetField("Moments", Prive)!.GetValue(instance)!;
        var motif = plan.GetMethod("MotifNonMaquette", Prive)!;

        var codes = new List<(string Code, string Libelle)>();
        foreach (var moment in moments)
        {
            var sous = (IEnumerable)moment.GetType().GetProperty("Sous")!.GetValue(moment)!;
            foreach (var sm in sous)
            {
                var entrees = (IEnumerable)sm.GetType().GetProperty("Entrees")!.GetValue(sm)!;
                foreach (var e in entrees)
                {
                    var t = e.GetType();
                    codes.Add(((string)t.GetProperty("Code")!.GetValue(e)!,
                               (string)t.GetProperty("Libelle")!.GetValue(e)!));
                }
            }
        }

        Exiger(codes.Count > 100, $"le menu de gauche est bien parcouru ({codes.Count} entrees)");

        var muettes = new List<string>();
        foreach (var (code, libelle) in codes.DistinctBy(c => c.Code))
        {
            if (portes.Contains(code)) continue;
            var cible = motif.IsStatic ? null : instance;
            var texte = motif.Invoke(cible, new object?[] { code }) as string;
            if (string.IsNullOrWhiteSpace(texte) || texte.Trim().Length < 20)
                muettes.Add($"{code} ({libelle})");
        }

        Exiger(muettes.Count == 0,
            "aucune entree de menu ne tombe sur un ecran d'attente muet"
            + (muettes.Count == 0 ? "" : $" -- {muettes.Count} muettes : "
               + string.Join(", ", muettes.Take(8)) + (muettes.Count > 8 ? ", ..." : "")));

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0
            ? "MotifCheck : vert."
            : $"MotifCheck : {echecs.Count} echec(s).");
        return echecs.Count;
    }
}
