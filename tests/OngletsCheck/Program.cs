using System.IO;
using System.Reflection;
using System.Windows;
using RigV3Desktop;

// Garde-fou de la BARRE D'ONGLETS (demande du 17/09/2026 : « un systeme
// d'onglet comme un navigateur web, ou je peux sortir l'onglet sur un 2e
// ecran, et qui se rappelle de mon agencement »).
//
// Les trois choses que la compilation ne verrait PAS :
//   1. ouvrir une 2e vue ne doit plus EFFACER la premiere (c'etait le defaut) ;
//   2. lache hors de la fenetre = onglet sorti ; lache dedans = simple ordre ;
//   3. l'agencement doit revenir a l'identique apres redemarrage.

internal static class Programme
{
    [STAThread]
    private static int Main()
    {
        var echecs = new List<string>();
        void Exiger(bool ok, string quoi)
        {
            Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
            if (!ok) echecs.Add(quoi);
        }

        // ---------- 1. La barre se comporte comme celle d'un navigateur.
        var barre = new EtatOnglets();
        barre.Ouvrir("PROC_XAFF", "Affaires");
        barre.Ouvrir("PROC_RECHAFF", "Recherche d'affaires");
        Exiger(barre.Onglets.Count == 2,
            "ouvrir une 2e vue AJOUTE un onglet au lieu de remplacer la 1re");
        Exiger(barre.Actuel?.Code == "PROC_RECHAFF",
            "la vue qu'on vient d'ouvrir est celle qui est montree");

        barre.Ouvrir("PROC_XAFF", "Affaires");
        Exiger(barre.Onglets.Count == 2,
            "rouvrir une vue deja ouverte y revient, sans creer de doublon");
        Exiger(barre.Actuel?.Code == "PROC_XAFF", "... et elle redevient la vue montree");

        barre.Masquer();
        Exiger(barre.Actuel is null && barre.Onglets.Count == 2,
            "revenir a l'accueil garde les onglets ouverts");

        // Fermer celui du milieu : on tombe sur un VOISIN, pas sur l'accueil.
        var trois = new EtatOnglets();
        trois.Ouvrir("A", "A"); trois.Ouvrir("B", "B"); trois.Ouvrir("C", "C");
        trois.Activer("B");
        trois.Fermer("B");
        Exiger(trois.Onglets.Count == 2 && trois.Actuel?.Code == "C",
            "fermer l'onglet courant bascule sur son voisin, l'ecran ne se vide pas");
        trois.Fermer("A");
        trois.Fermer("C");
        Exiger(trois.Onglets.Count == 0 && trois.Actuel is null,
            "le dernier onglet ferme ramene a l'accueil");

        // ---------- 2. Reordonner par glisser.
        var ordre = new EtatOnglets();
        ordre.Ouvrir("A", "A"); ordre.Ouvrir("B", "B"); ordre.Ouvrir("C", "C");
        ordre.Activer("A");
        ordre.Deplacer(0, 2);
        Exiger(string.Join(",", ordre.Codes()) == "B,C,A",
            "glisser un onglet a la fin le range bien a la fin");
        Exiger(ordre.Actuel?.Code == "A",
            "l'onglet deplace reste celui qui est montre (on suit l'onglet, pas la place)");

        var cases = new List<(double, double)> { (0, 100), (100, 220), (220, 300) };
        Exiger(EtatOnglets.IndexCible(cases, 10) == 0, "lache a gauche : place 1");
        Exiger(EtatOnglets.IndexCible(cases, 170) == 1, "lache au milieu du 2e : place 2");
        Exiger(EtatOnglets.IndexCible(cases, 290) == 2, "lache a droite : derniere place");
        // Les vrais onglets sont separes par un espace (gap de 2 px) : le
        // survoler ne doit PAS envoyer l'onglet au bout de la barre.
        var espaces = new List<(double, double)> { (0, 100), (102, 220), (222, 300) };
        Exiger(EtatOnglets.IndexCible(espaces, 100.5) == 0, "espace entre le 1er et le 2e, cote 1er : place 1, pas la derniere");
        Exiger(EtatOnglets.IndexCible(espaces, 101.5) == 1, "espace entre le 1er et le 2e, cote 2e : place 2");

        // ---------- 3. Sortir l'onglet de la fenetre.
        // La fenetre visible mesure 1200 x 800 ; la marge evite qu'un geste
        // rasant le bord n'arrache l'onglet par accident.
        Exiger(!ArrachageOnglet.EstSorti(600, 400, 0, 0, 1200, 800),
            "lache DANS la fenetre : l'onglet ne sort pas");
        Exiger(!ArrachageOnglet.EstSorti(1210, 400, 0, 0, 1200, 800),
            "lache a 10 px du bord : encore dedans, l'onglet ne sort pas");
        Exiger(ArrachageOnglet.EstSorti(1400, 400, 0, 0, 1200, 800),
            "lache a DROITE hors de la fenetre : l'onglet sort dans sa fenetre");
        Exiger(ArrachageOnglet.EstSorti(600, -60, 0, 0, 1200, 800),
            "lache AU-DESSUS de la fenetre : l'onglet sort aussi");
        Exiger(!ArrachageOnglet.EstSorti(9999, 9999, 0, 0, 0, 0),
            "fenetre de taille inconnue : on n'arrache pas au hasard");

        var sortie = new EtatOnglets();
        sortie.Ouvrir("A", "A"); sortie.Ouvrir("B", "B");
        Exiger(sortie.Detacher("B"), "l'onglet B part dans sa propre fenetre");
        Exiger(sortie.Onglets.Count == 2 && sortie.Onglets[1].Detache,
            "l'onglet sorti RESTE dans la barre (on voit ou la vue est partie)");
        Exiger(sortie.Montre is null,
            "la vue sortie n'est plus dessinee dans la fenetre principale");
        Exiger(!sortie.Detacher("B"), "on ne sort pas deux fois la meme vue");
        sortie.Rattacher("B");
        Exiger(sortie.Montre?.Code == "B",
            "fermer la fenetre ramene la vue dans son onglet");

        // ---------- 3 bis. La pastille du verbe (19/11/2026, « Onglet fondu » n°3).
        // L'onglet porte la pastille du verbe du menu qui range sa vue. Ce que la
        // compilation ne verrait pas : une teinte de verbe sans regle CSS donnerait
        // une pastille grise, sans erreur nulle part.
        var arrivee = RigV3Desktop.Pont.MenuAccueil.VerbeDe("PROC_ARRIVEE");
        Exiger(arrivee is { Titre: "Ouvrir", Teinte: "rcs", Signe: "+" },
            "le courrier du jour porte la pastille « + » d'Ouvrir");
        Exiger(RigV3Desktop.Pont.MenuAccueil.VerbeDe("PROC_AUDIENCE") is { Titre: "Surveiller", Signe: "!" },
            "les audiences portent la pastille « ! » de Surveiller");
        Exiger(RigV3Desktop.Pont.MenuAccueil.VerbeDe("PROC_TACHES_LOT") is null,
            "une vue hors du menu (taches en lot, ouverte par une alerte) n'a pas de pastille");
        var css = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../wwwroot/rig-accueil.css")));
        foreach (var verbe in RigV3Desktop.Pont.MenuAccueil.Moments)
            Exiger(css.Contains(".rig-vonglet__verbe--" + verbe.Teinte),
                $"la pastille d'onglet de « {verbe.Titre} » a sa couleur (.rig-vonglet__verbe--{verbe.Teinte})");

        // ---------- 4. L'agencement survit au redemarrage.
        var temporaire = Path.Combine(Path.GetTempPath(),
            "rigv3-onglets-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", temporaire);
        try
        {
            AccueilEtat.MemoriserOnglets(
                new[] { "PROC_XAFF", "PROC_RECHAFF", "PROC_FACTURE" },
                "PROC_RECHAFF",
                new[] { "PROC_FACTURE" });

            var relu = AccueilEtat.Charger();
            Exiger(string.Join(",", relu.OngletsVue) == "PROC_XAFF,PROC_RECHAFF,PROC_FACTURE",
                "au redemarrage, les memes onglets reviennent DANS LE MEME ORDRE");
            Exiger(relu.OngletActif == "PROC_RECHAFF",
                "l'onglet qui etait devant revient devant");
            Exiger(relu.VuesDetachees.SequenceEqual(new[] { "PROC_FACTURE" }),
                "la vue sortie sur l'autre ecran se rouvre en fenetre");

            var pose = new PoseFenetre { Gauche = 1930, Haut = 40, Largeur = 900, Hauteur = 700 };
            AccueilEtat.MemoriserFenetreVue("PROC_FACTURE", pose);
            var revenue = AccueilEtat.PoseVue("PROC_FACTURE");
            Exiger(revenue is not null
                   && Math.Abs(revenue.Gauche - 1930) < 0.5
                   && Math.Abs(revenue.Largeur - 900) < 0.5,
                "la fenetre d'une vue sortie retrouve SA place (clef = la vue, pas le rang)");
            Exiger(AccueilEtat.PoseVue("PROC_XAFF") is null,
                "une vue jamais sortie n'a pas de place memorisee");

            // Une SESSION garde l'etat lu a son demarrage (SessionAccueil.Etat) et le reecrit
            // EN ENTIER a chaque geste (Consulter, l'arbre GED...). Ouvrir une affaire retient
            // son onglet (MemoriserOnglets) PUIS la consulte : l'etat de la session, lu avant,
            // remettait la barre du demarrage, et les onglets ne revenaient plus (parcours du
            // lot 6, 20/11/2026 : 004176 et 004151 ouverts, app fermee, accueil seul au retour).
            var session = AccueilEtat.Charger();
            AccueilEtat.MemoriserOnglets(new[] { "AFFAIRE:004176", "AFFAIRE:004151", "PROC_FACTURE" },
                "AFFAIRE:004151", Array.Empty<string>(),
                new Dictionary<string, string> { ["PROC_FACTURE"] = "004151" });
            AccueilEtat.MemoriserFenetreVue("AFFAIRE:004151", pose);
            AccueilEtat.MemoriserTriClasseur(AccueilEtat.TriClasseurParDossier);
            session.Consulter("004151", "Depot de bilan - TRANSNORD SA", "318552007", "RCS", "retard");
            var apresSession = AccueilEtat.Charger();
            Exiger(string.Join(",", apresSession.OngletsVue) == "AFFAIRE:004176,AFFAIRE:004151,PROC_FACTURE"
                   && apresSession.OngletActif == "AFFAIRE:004151",
                "une session qui ecrit apres l'ouverture d'une affaire garde les onglets retenus (et l'actif)");
            Exiger(apresSession.AffairesDesOnglets.TryGetValue("PROC_FACTURE", out var affaireFacture) && affaireFacture == "004151",
                "... et l'affaire de l'onglet d'ecran ouvert depuis elle");
            Exiger(AccueilEtat.PoseVue("AFFAIRE:004151") is not null,
                "... et la place retenue entre-temps pour une fenetre sortie");
            Exiger(apresSession.TriClasseur == AccueilEtat.TriClasseurParDossier,
                "... et le classement du classeur choisi entre-temps (une autre fenetre)");
            // Le jumeau : ce que la session ecrit, ELLE, est bien ecrit.
            Exiger(apresSession.Consultations.FirstOrDefault()?.Numero == "004151",
                "ce que la session ecrit (la consultation de l'affaire) est bien ecrit");

            // Un accueil.json ecrit AVANT cette version n'a aucun de ces champs :
            // il doit se charger quand meme, listes vides.
            File.WriteAllText(temporaire, "{\"ThemeSombre\":true}");
            var ancien = AccueilEtat.Charger();
            Exiger(ancien.OngletsVue.Count == 0 && ancien.VuesDetachees.Count == 0
                   && ancien.FenetresVue.Count == 0 && ancien.OngletActif == string.Empty,
                "un fichier de reglages ecrit avant cette version se charge sans erreur");

            // ---------- 5. La fenetre d'onglet porte bien SA vue.
            // Sans ce passage de parametre, toutes les fenetres sorties
            // montreraient l'accueil -- defaut invisible a la compilation.
            if (Application.Current is null) new Application();
            var fenetre = new FenetreVue("PROC_RECHAFF", "Recherche d'affaires", 2);
            var champ = typeof(FenetreVue)
                .GetField("Vue", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(fenetre);
            // Depuis le retrait de Blazor (19/11/2026), ces reglages vont a la RACINE
            // de la fenetre (Pont/Racines.cs, RacinePlanTravail), qui les donne a la
            // session du plan de travail.
            Exiger(champ is Microsoft.Web.WebView2.Wpf.WebView2, "la fenetre sortie declare bien sa vue WebView2");
            var racine = fenetre.Racine;
            Exiger(racine.VueInitiale == "PROC_RECHAFF",
                "la fenetre sortie recoit le CODE de l'ecran arrache");
            Exiger(racine.ModeFenetreVue && !racine.Bords,
                "la fenetre sortie s'ouvre sans le menu ni la barre (ni les bandes de bord) de la fenetre principale");
            Exiger(racine.Composant == "Application" && racine.Rappels.SequenceEqual(new[] { "geste", "plan" }),
                "la fenetre sortie porte bien la page des ecrans");
            Exiger(racine.Plan.VueInitiale == "PROC_RECHAFF" && racine.Plan.ModeFenetreVue,
                "la session du plan de travail recoit ces deux reglages");
            fenetre.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", null);
            try { File.Delete(temporaire); } catch (IOException) { }
        }

        Console.WriteLine();
        Console.WriteLine(echecs.Count == 0
            ? "Onglets de vues : tout est vert."
            : $"Onglets de vues : {echecs.Count} ECHEC(S).");
        return echecs.Count == 0 ? 0 : 1;
    }
}
