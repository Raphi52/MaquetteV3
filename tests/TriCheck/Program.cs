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
            var type = assemblage.GetType("RigV3Desktop.Components.Ecrans." + nomEcran);
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
        }

        Verifier("EcranRechAffaires", "Trouvees", "Nom", o => Membre(o, "Nom"));
        Verifier("EcranRequisitions", "CommandesWebVisibles", "Recue", o => Membre(o, "Recue"));

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0 ? "TriCheck : vert." : $"TriCheck : {echecs.Count} echec(s).");
        return echecs.Count;
    }
}
