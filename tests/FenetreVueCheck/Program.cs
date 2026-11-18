using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.AspNetCore.Components;
using RigV3Desktop;
using RigV3Desktop.Components;

// Garde-fou du PASSAGE DU RANG a la vue du document (demande du 14/09/2026).
//
// Le juge a note que « Vue.RootComponents[0].Parameters = ... » n'etait prouve
// que par la compilation : si l'index ou le nom du parametre ne correspondait
// pas, TOUTES les fenetres detachees afficheraient le meme document -- le
// defaut meme qu'on corrige. Ici on CONSTRUIT vraiment deux fenetres et on
// relit ce qui a ete pose.

internal static class Programme
{
    /// <summary>Declenche la fermeture reelle (OnClosing est protege).</summary>
    private static void Fermer(FenetreDocument f)
    {
        typeof(FenetreDocument)
            .GetMethod("OnClosing", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(f, new object[] { new System.ComponentModel.CancelEventArgs() });
        f.Close();
    }

    /// <summary>La zone de travail de l'ecran principal, en unites WPF.</summary>
    private static Rect ZoneDeTravailPrincipale() => SystemParameters.WorkArea;

    [STAThread]
    private static int Main()
    {
        var echecs = new List<string>();
        void Exiger(bool ok, string quoi)
        {
            Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
            if (!ok) echecs.Add(quoi);
        }

        if (Application.Current is null) new Application();

        var vue = typeof(DocumentDetacheVue);
        var parametres = vue.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name).ToList();
        Exiger(parametres.Contains("Rang"),
            "la vue du document expose bien un parametre nomme Rang");

        foreach (var rang in new[] { 1, 2, 3 })
        {
            var fenetre = new FenetreDocument(rang);
            var champ = typeof(FenetreDocument)
                .GetField("Vue", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(fenetre);
            var racines = (champ as Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView)?.RootComponents;
            Exiger(racines is { Count: > 0 },
                $"fenetre {rang} : la vue racine declaree en XAML est presente");
            var racine = racines![0];
            Exiger(racine.ComponentType == vue,
                $"fenetre {rang} : la vue racine est bien celle du document");
            var pose = racine.Parameters;
            Exiger(pose is not null && pose.TryGetValue("Rang", out var v) && v is int i && i == rang,
                $"fenetre {rang} : le rang {rang} est bien transmis a la vue");
            fenetre.Close();
        }

        // --- La POSITION, executee par la fenetre elle-meme.
        // Le juge notait que l'aller-retour n'etait prouve que par la fonction
        // pure : ici c'est OnClosing qui enregistre et RestaurerPose qui relit.
        var temporaire = Path.Combine(Path.GetTempPath(),
            "rigv3-pose-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", temporaire);
        try
        {
            var zone = ZoneDeTravailPrincipale();
            var pose = new Rect(zone.X + 60, zone.Y + 40, 820, 640);

            var posee = new FenetreDocument(2);
            posee.Left = pose.X; posee.Top = pose.Y;
            posee.Width = pose.Width; posee.Height = pose.Height;
            Fermer(posee);
            var retenue = AccueilEtat.PoseDocument(2);
            Exiger(retenue is not null && retenue.Gauche == pose.X && retenue.Haut == pose.Y
                   && retenue.Largeur == pose.Width && retenue.Hauteur == pose.Height,
                "fermer la fenetre 2 retient EXACTEMENT ou l'utilisateur l'a laissee");

            var rouverte = new FenetreDocument(2);
            var restauree = (bool)typeof(FenetreDocument)
                .GetMethod("RestaurerPose", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(rouverte, null)!;
            Exiger(restauree, "a la reouverture, la fenetre 2 reprend la position memorisee");
            Exiger(rouverte.Left == pose.X && rouverte.Top == pose.Y
                   && rouverte.Width == pose.Width && rouverte.Height == pose.Height,
                "elle se rouvre au MEME endroit et a la MEME taille (position stable)");
            rouverte.Close();

            // La fenetre 1, jamais posee, ne vole pas la position de la 2.
            var autre = new FenetreDocument(1);
            var restauree1 = (bool)typeof(FenetreDocument)
                .GetMethod("RestaurerPose", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(autre, null)!;
            Exiger(!restauree1, "la fenetre 1 garde SA place : rien de memorise, placement par defaut");
            autre.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", null);
            try { if (File.Exists(temporaire)) File.Delete(temporaire); } catch { }
        }

        // === ONGLET ARRACHE : deux ecrans simules (gauche 0..1920, droite 1920..3840) ===
        {
            var zones = new List<PlacementDocument.Zone>
            {
                new(0, 0, 1920, 1040), new(1920, 0, 1920, 1040)
            };
            bool Dans(PlacementDocument.Zone z, double x, double y) =>
                x >= z.X && x < z.X + z.Largeur && y >= z.Y && y < z.Y + z.Hauteur;

            var (xd, yd) = FenetreVue.PoseSousCurseur(2600, 400);
            Exiger(Dans(zones[1], xd, yd) && !Dans(zones[0], xd, yd),
                $"onglet lache sur l'ecran de droite (curseur 2600,400) : la fenetre y nait ({xd},{yd})");
            var (xg, yg) = FenetreVue.PoseSousCurseur(700, 400);
            Exiger(Dans(zones[0], xg, yg),
                $"onglet lache sur l'ecran de gauche : la fenetre y reste ({xg},{yg})");

            var pose = PlacementDocument.Calculer(zones, 0, 0, 900, 700);
            Exiger(pose is not null && Dans(zones[1], pose.Gauche, pose.Haut),
                "premiere sortie sans curseur : la vue va sur l'ecran qui NE porte PAS la fenetre principale");
            var pose2 = PlacementDocument.Calculer(zones, 1, 0, 900, 700);
            Exiger(pose2 is not null && Dans(zones[0], pose2.Gauche, pose2.Haut),
                "fenetre principale a droite : la vue va a gauche");
        }

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0
            ? "FenetreVueCheck : tout est vert."
            : $"FenetreVueCheck : {echecs.Count} echec(s).");
        return echecs.Count == 0 ? 0 : 1;
    }
}
