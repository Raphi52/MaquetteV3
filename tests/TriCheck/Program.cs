using System.Collections;
using System.IO;
using System.Reflection;

// Garde-fou du TRI DES GRILLES (16/09/2026).
//
// Constat mesure avant ce banc : 32 des 35 ecrans ne contenaient ni Trier ni
// OrderBy ; aucune grille ne se triait en cliquant son en-tete. Une grille de
// greffe qu'on ne peut pas ranger par date ou par nom passe pour une image.
//
// Ce banc ne lit pas le texte des fichiers : il CONSTRUIT l'ecran, clique le
// tri par reflexion et relit l'ordre reellement rendu.

internal static class Programme
{
    private const BindingFlags Prive = BindingFlags.NonPublic | BindingFlags.Instance;

    [STAThread]
    private static int Main()
    {
        var echecs = new List<string>();
        void Exiger(bool ok, string quoi)
        {
            Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
            if (!ok) echecs.Add(quoi);
        }

        var assemblage = typeof(RigV3Desktop.App).Assembly;

        // L'en-tete triable est un composant PARTAGE : une seule facon de trier
        // dans toute la maquette. Il est en React depuis le lot 6 (19/11/2026),
        // et seul depuis le retrait de Blazor : web/src/composants/EnteteTriable.tsx,
        // importe par les ecrans qui trient.
        var racineDepot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var cheminEntete = Path.Combine(racineDepot, "web/src/composants/EnteteTriable.tsx");
        var importeurs = Directory.GetFiles(Path.Combine(racineDepot, "web/src/ecrans"), "*.tsx")
            .Where(f => !f.EndsWith(".test.tsx") && File.ReadAllText(f).Contains("from '../composants/EnteteTriable'"))
            .ToList();
        Exiger(File.Exists(cheminEntete) && File.ReadAllText(cheminEntete).Contains("export function EnteteTriable(")
               && importeurs.Count >= 2,
            $"le composant d'en-tete triable partage existe, et les ecrans qui trient s'en servent ({importeurs.Count})");

        // Les objets de grille exposent tantot des proprietes, tantot des champs
        // publics : lire l'un ou l'autre, sinon le banc mesurerait sa propre
        // maladresse et non le tri.
        static IComparable Membre(object o, string nom)
        {
            var t = o.GetType();
            var valeur = t.GetProperty(nom)?.GetValue(o) ?? t.GetField(nom)?.GetValue(o);
            return (IComparable)valeur!;
        }

        void Verifier(string nomEcran, string proprieteListe, string cle, Func<object, IComparable> lecture)
        {
            // Ecran PORTE EN REACT A SESSION (web/src/session.tsx, 19/11/2026) :
            // son etat et son tri ont quitte le .razor (devenu une coquille) pour
            // la classe Session<Nom> de Pont/<Ecran>.cs, avec les MEMES membres
            // (BasculerTri, la liste). C'est elle qu'on construit (Blazor, et ses
            // composants d'ecran, sont retires depuis le 19/11/2026).
            var session = nomEcran.StartsWith("Ecran", StringComparison.Ordinal)
                ? assemblage.GetType("RigV3Desktop.Pont.Session" + nomEcran["Ecran".Length..])
                : null;
            var type = session;
            if (type is null) { Exiger(false, $"{nomEcran} : ecran introuvable"); return; }
            var instance = Activator.CreateInstance(type, nonPublic: true)!;

            var basculer = type.GetMethod("BasculerTri", Prive);
            if (basculer is null) { Exiger(false, $"{nomEcran} : pas d'action de tri (BasculerTri)"); return; }

            var liste = type.GetProperty(proprieteListe, Prive | BindingFlags.Public);
            if (liste is null) { Exiger(false, $"{nomEcran} : liste {proprieteListe} introuvable"); return; }

            List<IComparable> Lire()
                => ((IEnumerable)liste.GetValue(instance)!).Cast<object>().Select(lecture).ToList();

            basculer.Invoke(instance, new object?[] { cle });
            var croissant = Lire();
            basculer.Invoke(instance, new object?[] { cle });   // deuxieme clic = sens inverse
            var decroissant = Lire();

            Exiger(croissant.Count > 2, $"{nomEcran} : la grille a des lignes ({croissant.Count})");
            Exiger(croissant.SequenceEqual(croissant.OrderBy(x => x)),
                $"{nomEcran} : un clic sur « {cle} » range la grille dans l'ordre");
            Exiger(decroissant.SequenceEqual(croissant.AsEnumerable().Reverse()),
                $"{nomEcran} : un second clic inverse l'ordre");

            // Ecran a session : le meme tri par le GESTE que le .tsx envoie au
            // clic sur l'en-tete, Agir(« BasculerTri », cle) — la colonne cliquee
            // arrive bien jusqu'a BasculerTri.
            if (session is not null)
            {
                var agir = type.GetMethod("Agir", Prive);
                Exiger(agir is not null, $"{nomEcran} : la session recoit les gestes de React (Agir)");
                if (agir is null) return;
                var parGeste = Activator.CreateInstance(type, nonPublic: true)!;
                var valeur = System.Text.Json.JsonSerializer.SerializeToElement(cle);
                List<IComparable> LireGeste()
                    => ((IEnumerable)liste.GetValue(parGeste)!).Cast<object>().Select(lecture).ToList();
                agir.Invoke(parGeste, new object?[] { "BasculerTri", valeur });
                var croissantGeste = LireGeste();
                agir.Invoke(parGeste, new object?[] { "BasculerTri", valeur });
                Exiger(croissantGeste.SequenceEqual(croissant) && LireGeste().SequenceEqual(decroissant),
                    $"{nomEcran} : le geste « BasculerTri » envoye par React trie de meme, puis inverse");
            }
        }

        // EcranRechAffaires est PORTE EN REACT (lot 3, 19/11/2026) : la grille est
        // triee par Pont/EcranRechAffaires.cs (ordre des chaines .NET), l'ecran
        // React ne lui passe que la colonne et le sens. Meme mesure, sur le pont :
        // un clic range la grille, un second clic sur la meme colonne l'inverse.
        {
            var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            var tsxRech = File.ReadAllText(Path.Combine(racine, "web/src/ecrans/EcranRechAffaires.tsx"));
            Exiger(System.Text.RegularExpressions.Regex.IsMatch(tsxRech,
                       @"function basculerTri\(cle: string\) \{\s*if \(cleTri === cle\) setTriAscendant\(!triAscendant\)\s*else \{ setCleTri\(cle\); setTriAscendant\(true\) \}")
                   && tsxRech.Contains("<EnteteTriable "),
                "EcranRechAffaires : les en-tetes partages basculent le tri (basculerTri)");
            var sansCritere = new RigV3Desktop.Pont.CriteresAffaires("", "", "", "", "", "", "", "", "", null, null);
            List<IComparable> Noms(bool ascendant) => RigV3Desktop.Pont.EcranRechAffaires
                .Trouver(sansCritere, "Nom", ascendant, null).Lignes.Select(l => (IComparable)l.Nom).ToList();
            var croissant = Noms(true);
            var decroissant = Noms(false);
            Exiger(croissant.Count > 2, $"EcranRechAffaires : la grille a des lignes ({croissant.Count})");
            Exiger(croissant.SequenceEqual(croissant.OrderBy(x => x)),
                "EcranRechAffaires : un clic sur « Nom » range la grille dans l'ordre");
            Exiger(decroissant.SequenceEqual(croissant.AsEnumerable().Reverse()),
                "EcranRechAffaires : un second clic inverse l'ordre");

            // Listes modernes, lot 1 (24/11/2026) : « Trier » propose les memes cles que les en-tetes, et
            // les vues de la maquette sont comptees par le pont sur le fonds de demonstration (15 affaires :
            // Toutes 15, Proc. collectives en cours 3, Solde > 100 € 4, Cloturees 2). Le solde minimum se
            // lit a la francaise et filtre STRICTEMENT au-dela ; illisible, il ne filtre rien.
            var cles = System.Text.RegularExpressions.Regex.Matches(tsxRech, @"\['[^']+', '([A-Za-z]+)'\]").Select(m => m.Groups[1].Value).ToList();
            Exiger(cles.SequenceEqual(new[] { "Numero", "IdInstance", "Role", "CodeAffaire", "Nature", "Libelle", "Nom", "Etat",
                                              "Siren", "Type", "Saisine", "Creation", "Solde" })
                   && tsxRech.Contains("<ChoixTri cles={Colonnes}"),
                $"EcranRechAffaires : « Trier » propose les 13 cles de tri du pont ({string.Join(", ", cles)})");
            RigV3Desktop.Pont.CriteresAffaires Vue(string nature = "", string etat = "", string? soldeMin = null)
                => sansCritere with { CritNature = nature, CritEtat = etat, CritSoldeMin = soldeMin };
            var comptes = RigV3Desktop.Pont.EcranRechAffaires.Compter(
                [Vue(), Vue("collective", "En cours"), Vue(soldeMin: "100"), Vue(etat: "Cloturee")]);
            Exiger(comptes.SequenceEqual(new[] { 15, 3, 4, 2 }),
                $"EcranRechAffaires : les vues comptent 15, 3, 4, 2 comme la maquette ({string.Join(", ", comptes)})");
            var auDela = RigV3Desktop.Pont.EcranRechAffaires.Trouver(Vue(soldeMin: "142,50"), null, true, null);
            Exiger(auDela.Lignes.Length == 3 && auDela.Lignes.All(l => l.SoldeValeur > 142.50m) && !auDela.AucunCritere,
                $"EcranRechAffaires : « Solde superieur a 142,50 » garde les soldes au-dela, pas l'egal ({auDela.Lignes.Length})");
            var illisible = RigV3Desktop.Pont.EcranRechAffaires.Trouver(Vue(soldeMin: "abc"), null, true, null);
            Exiger(illisible.Lignes.Length == 15 && illisible.AucunCritere,
                "EcranRechAffaires : un solde minimum illisible ne filtre rien et ne compte pas comme critere");
            var tous = RigV3Desktop.Pont.EcranRechAffaires.Trouver(sansCritere, null, true, null).Lignes;
            Exiger(tous.Sum(l => l.SoldeValeur) == 1379.30m && tous.All(l => l.Solde == l.SoldeValeur.ToString("N2")),
                "EcranRechAffaires : le solde en nombre est celui du texte, total 1 379,30 comme la maquette");
        }
        Verifier("EcranRequisitions", "CommandesWebVisibles", "Recue", o => Membre(o, "Recue"));

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0 ? "TriCheck : vert." : $"TriCheck : {echecs.Count} echec(s).");
        return echecs.Count;
    }
}
