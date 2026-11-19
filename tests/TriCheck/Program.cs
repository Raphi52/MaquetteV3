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
        // dans toute la maquette.
        var entete = assemblage.GetType("RigV3Desktop.Components.EnteteTriable");
        Exiger(entete is not null, "le composant d'en-tete triable partage existe");

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
            // (BasculerTri, la liste). C'est elle qu'on construit ; sinon le
            // composant Blazor, comme avant.
            var session = nomEcran.StartsWith("Ecran", StringComparison.Ordinal)
                ? assemblage.GetType("RigV3Desktop.Pont.Session" + nomEcran["Ecran".Length..])
                : null;
            var type = session ?? assemblage.GetType("RigV3Desktop.Components.Ecrans." + nomEcran);
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
        }
        Verifier("EcranRequisitions", "CommandesWebVisibles", "Recue", o => Membre(o, "Recue"));

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0 ? "TriCheck : vert." : $"TriCheck : {echecs.Count} echec(s).");
        return echecs.Count;
    }
}
