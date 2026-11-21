using System.Text.Json;

namespace RigV3.Passage;

/// <summary>Un constat d'une regle ecrite : le critere qu'il juge (« SOC-025 CA04 »), vrai ou faux, et pourquoi.</summary>
public sealed record Constat(string Critere, bool Ok, string Detail);

/// <summary>L'explication d'un ecart ou d'une erreur d'une repetition (SOC-025 CA02 : tout ecart doit etre explique pour continuer).</summary>
public sealed record Explication(int Repetition, string Greffe, string Motif);

/// <summary>Un defaut trouve pendant une repetition (SOC-025 CA05) : le cas qui l'a revele (greffe, etape) et s'il est corrige.</summary>
public sealed record DefautRepetition(string Id, int TrouveDans, string Greffe, string Etape, bool Corrige, string? Suivi = null);

/// <summary>
/// LES REGLES DES REPETITIONS GENERALES (SOC-025), jouees sur les rapports que rend <see cref="MoteurRepetition"/> :
///   CA01  chaque repetition traite toutes les bases attendues ;
///   CA02  chaque base a un rapport (duree de chaque etape, ecarts, erreurs) ; tout ecart et toute erreur sont expliques ;
///   CA03  la derniere tient dans la nuit : sa duree totale sous la fenetre, chaque base finie avant son heure de passage ;
///   CA04  au moins trois repetitions ; la derniere sans ecart ni erreur, tous ses greffes passes ; ses durees stables ;
///   CA05  un defaut corrige est rejoue par chaque repetition suivante (meme greffe, meme etape faite) ;
///   CA06  aucune base sur une instance de production.
/// Une regle fausse arrete : la repetition suivante (ou le jour J) ne se decide pas sur un « presque ».
/// </summary>
public static class ComparaisonRepetitions
{
    public sealed record Regles
    {
        /// <summary>Les greffes que chaque repetition doit traiter ; vide = ceux de la derniere repetition.</summary>
        public IReadOnlyCollection<string> BasesAttendues { get; init; } = [];
        public IReadOnlyList<Explication> Explications { get; init; } = [];
        public IReadOnlyList<DefautRepetition> Defauts { get; init; } = [];
        /// <summary>La fenetre de nuit (REC-001 T04 : 20 h -> 6 h, proposee, a valider).</summary>
        public TimeSpan Nuit { get; init; } = TimeSpan.FromHours(10);
        /// <summary>Stable : la derniere duree d'une base ne s'ecarte pas de la moyenne des precedentes de plus de cette part...</summary>
        public double EcartRelatifStable { get; init; } = 0.25;
        /// <summary>... ou de plus de cette duree (une base de quelques secondes varie de plus de 25 % sans rien dire).</summary>
        public TimeSpan EcartAbsoluStable { get; init; } = TimeSpan.FromMinutes(1);
        public int Minimum { get; init; } = 3;
    }

    public static IReadOnlyList<Constat> Comparer(IReadOnlyList<RapportRepetition> repetitions, Regles regles)
    {
        var c = new List<Constat>();
        var ordre = repetitions.OrderBy(r => r.Numero).ToList();
        if (ordre.Count == 0)
            return [new Constat("SOC-025 CA04", false, "aucune repetition jouee")];
        var derniere = ordre[^1];
        var attendues = (regles.BasesAttendues.Count > 0 ? regles.BasesAttendues : derniere.Bases.Select(b => b.Greffe)).ToHashSet(StringComparer.Ordinal);

        // CA01 : toutes les bases, a chaque repetition.
        foreach (var r in ordre)
        {
            var manque = attendues.Except(r.Bases.Select(b => b.Greffe)).Order(StringComparer.Ordinal).ToList();
            c.Add(new Constat("SOC-025 CA01", manque.Count == 0, manque.Count == 0
                ? $"repetition {r.Numero} : les {attendues.Count} base(s) attendue(s) traitees"
                : $"repetition {r.Numero} : {manque.Count} base(s) non traitee(s) : {string.Join(", ", manque)}"));
        }

        // CA02 : un rapport par base (duree de chaque etape) ; ecarts et erreurs expliques.
        foreach (var r in ordre)
        {
            var sansDuree = r.Bases.Where(b => b.Etapes.Count == 0).Select(b => b.Greffe).ToList();
            var aExpliquer = r.Bases.Where(b => b.Issue != IssuesPassage.Passe || b.Ecarts.Count > 0 || b.Erreurs.Count > 0).ToList();
            var nonExpliques = aExpliquer.Where(b => !regles.Explications.Any(e => e.Repetition == r.Numero && e.Greffe == b.Greffe))
                                         .Select(b => $"{b.Greffe} ({b.Issue}{(b.Motif is null ? "" : " : " + b.Motif)}{string.Concat(b.Ecarts.Select(e => $" ; ecart {e.Comparateur} : {e.Detail}"))})")
                                         .ToList();
            c.Add(new Constat("SOC-025 CA02", sansDuree.Count == 0 && nonExpliques.Count == 0,
                sansDuree.Count > 0 ? $"repetition {r.Numero} : base(s) sans duree d'etape : {string.Join(", ", sansDuree)}"
                : nonExpliques.Count > 0 ? $"repetition {r.Numero} : ecart(s) ou erreur(s) sans explication : {string.Join(" | ", nonExpliques)}"
                : $"repetition {r.Numero} : {aExpliquer.Count} ecart(s) ou erreur(s), tous expliques ; durees par etape au rapport"));
        }

        // CA03 : la derniere tient dans la nuit, chaque base finie avant l'ouverture de sa journee.
        var duree = TimeSpan.FromMilliseconds(derniere.DureeMs);
        var enRetard = derniere.Bases.Where(b => b.PassageUtc is { } p && b.FinUtc is { } f && f > p).Select(b => $"{b.Greffe} (fini {b.FinUtc:HH:mm} UTC, passage {b.PassageUtc:HH:mm} UTC)").ToList();
        c.Add(new Constat("SOC-025 CA03", duree <= regles.Nuit && enRetard.Count == 0,
            enRetard.Count > 0 ? $"repetition {derniere.Numero} : base(s) finie(s) apres leur heure de passage : {string.Join(", ", enRetard)}"
            : duree > regles.Nuit ? $"repetition {derniere.Numero} : {MoteurRepetition.TexteDuree(derniere.DureeMs)}, plus que la nuit ({regles.Nuit:hh\\:mm})"
            : $"repetition {derniere.Numero} : {MoteurRepetition.TexteDuree(derniere.DureeMs)} pour {derniere.Bases.Count} base(s), sous la nuit ({regles.Nuit:hh\\:mm}), chaque base finie avant son heure de passage"));

        // CA04 : au moins trois ; la derniere propre ; durees stables.
        c.Add(new Constat("SOC-025 CA04", ordre.Count >= regles.Minimum,
            $"{ordre.Count} repetition(s) jouee(s) ({regles.Minimum} au moins)"));
        var sales = derniere.Bases.Where(b => b.Issue != IssuesPassage.Passe || b.Ecarts.Count > 0 || b.Erreurs.Count > 0).Select(b => b.Greffe).ToList();
        c.Add(new Constat("SOC-025 CA04", sales.Count == 0, sales.Count == 0
            ? $"repetition {derniere.Numero} : aucun ecart, aucune erreur, {derniere.Bases.Count} greffe(s) passe(s)"
            : $"repetition {derniere.Numero} : ecart, erreur ou greffe non passe pour {string.Join(", ", sales)} (une explication n'y suffit pas)"));
        if (ordre.Count >= 2)
        {
            var instables = new List<string>();
            foreach (var b in derniere.Bases)
            {
                var avant = ordre.Take(ordre.Count - 1).SelectMany(r => r.Bases.Where(x => x.Greffe == b.Greffe && x.Issue == IssuesPassage.Passe)).Select(x => (double)x.DureeMs).ToList();
                if (avant.Count == 0) { instables.Add($"{b.Greffe} (aucune duree d'avant pour comparer)"); continue; }
                var moyenne = avant.Average();
                var tolere = Math.Max(regles.EcartRelatifStable * moyenne, regles.EcartAbsoluStable.TotalMilliseconds);
                if (Math.Abs(b.DureeMs - moyenne) > tolere)
                    instables.Add($"{b.Greffe} ({b.DureeMs / 1000.0:0.0} s contre {moyenne / 1000.0:0.0} s en moyenne)");
            }
            c.Add(new Constat("SOC-025 CA04", instables.Count == 0, instables.Count == 0
                ? $"durees stables : chaque base de la repetition {derniere.Numero} a moins de {regles.EcartRelatifStable:P0} (ou {regles.EcartAbsoluStable.TotalSeconds:0} s) de sa moyenne"
                : $"durees instables : {string.Join(", ", instables)}"));
        }

        // CA05 : un defaut corrige est rejoue par chaque repetition suivante.
        foreach (var d in regles.Defauts.Where(d => d.Corrige))
        {
            var suivantes = ordre.Where(r => r.Numero > d.TrouveDans).ToList();
            var rejoue = suivantes.Count > 0 && suivantes.All(r => r.Bases.Any(b => b.Greffe == d.Greffe &&
                b.Etapes.Any(e => e.Nom == d.Etape && e.Etat == EtatsEtape.Faite)));
            c.Add(new Constat("SOC-025 CA05", rejoue, rejoue
                ? $"defaut {d.Id} (repetition {d.TrouveDans}, {d.Greffe}, etape {d.Etape}) : corrige et rejoue par {suivantes.Count} repetition(s) suivante(s)"
                : $"defaut {d.Id} (repetition {d.TrouveDans}, {d.Greffe}, etape {d.Etape}) : corrige mais son cas n'est pas rejoue avec succes ensuite"));
        }

        // CA06 : seules les copies sont ecrites.
        var production = ordre.SelectMany(r => r.Bases.Where(b => b.Serveur is { } s && MoteurRepetition.InstancesDeProduction.Contains(s))
                                                       .Select(b => $"repetition {r.Numero} : {b.Greffe} sur {b.Serveur}")).ToList();
        c.Add(new Constat("SOC-025 CA06", production.Count == 0, production.Count == 0
            ? "aucune base d'une repetition sur une instance de production"
            : $"bases ecrites sur la production : {string.Join(", ", production)}"));
        return c;
    }

    public static IReadOnlyList<Explication> LireExplications(string? fichier) => fichier is null || !File.Exists(fichier) ? []
        : JsonSerializer.Deserialize<List<Explication>>(File.ReadAllText(fichier), RapportPassage.Json) ?? [];

    public static IReadOnlyList<DefautRepetition> LireDefauts(string? fichier) => fichier is null || !File.Exists(fichier) ? []
        : JsonSerializer.Deserialize<List<DefautRepetition>>(File.ReadAllText(fichier), RapportPassage.Json) ?? [];
}
