using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3Desktop;
using RigV3Desktop.Pont;

// LE PLAN DE TRAVAIL JOUE (lot 2 du plan « finir le passage a React », 19/11/2026).
//
// Home.razor n'est plus qu'une COQUILLE : la page entiere est l'application React
// (web/src/composants/Application.tsx), et l'etat et les regles du plan de
// travail vivent dans la session Pont/PlanTravail.cs (le @code de Home.razor,
// deplace tel quel). Les bancs qui relisaient Home.razor relisent la meme regle a
// son nouvel endroit ; celui-ci JOUE les gestes comme React les remonte
// (SessionPlanTravail.Agir) et lit la VUE rendue (SessionPlanTravail.Vue) :
// ouvrir un dossier, passer ses etapes, voir une saisie refusee puis acceptee,
// valider le dossier, ouvrir deux ecrans en onglets et les reordonner, Echap,
// le mode tache, le filtre de l'arbre, l'impression, la validation hierarchique.
// Arracher un onglet ouvre une fenetre WPF : il est prouve sur le bureau cache
// (parcours avant/apres du lot), pas ici.

internal static class Programme
{
    private static readonly List<string> Echecs = new();

    private static void Exiger(bool ok, string quoi)
    {
        Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
        if (!ok) Echecs.Add(quoi);
    }

    private static JsonElement Valeur(object? v) => JsonSerializer.SerializeToElement(v);

    /// <summary>La ligne de l'arbre dont le texte est celui-ci, ou null.</summary>
    private static LigneArbreVue? Ligne(LigneArbreVue l, string texte)
        => l.Texte == texte ? l : l.Enfants.Select(e => Ligne(e, texte)).FirstOrDefault(x => x is not null);

    private static IEnumerable<LigneArbreVue> Lignes(LigneArbreVue l)
        => new[] { l }.Concat(l.Enfants.SelectMany(Lignes));

    [STAThread]
    private static int Main()
    {
        var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

        // --- 0. Home.razor n'est plus qu'une coquille qui monte l'application.
        var home = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
        // Commentaires retires D'ABORD : l'en-tete cite « le @code de cette page ».
        var sansCommentaires = Regex.Replace(home, @"@\*[\s\S]*?\*@", "");
        var balisageHome = sansCommentaires[..sansCommentaires.IndexOf("@code", StringComparison.Ordinal)];
        Exiger(Regex.Matches(balisageHome, @"<IleReact\b").Count == 1
               && balisageHome.Contains("<IleReact Composant=\"Application\" Proprietes=\"@(new Dictionary<string, object?> { [\"vue\"] = Plan.Vue() })\" Rappels=\"Rappels\" />"),
            "Home.razor monte UNE ile : l'application React entiere, avec la vue du plan de travail");
        Exiger(!Regex.IsMatch(balisageHome, @"class=""rig-|@if|@foreach|@switch|<Ecran"),
            "Home.razor ne rend plus aucun balisage ni aucun ecran lui-meme");
        Exiger(home.Contains("[\"geste\"] = IleReact.Rappel(EventCallback.Factory.Create<Pont.GesteIle>(this, g => Plan.SurGesteAccueil(g)))")
               && home.Contains("[\"plan\"] = IleReact.Rappel(EventCallback.Factory.Create<Pont.GestePlan>(this, g => Plan.Jouer(g)))"),
            "Home.razor renvoie les gestes de l'accueil et du plan de travail a la session");
        Exiger(home.Contains("Plan.Planifier = action => _ = InvokeAsync(() => { action(); StateHasChanged(); });")
               && home.Contains("if (premierRendu) Plan.Demarrer();") && home.Contains("await Plan.DisposeAsync();"),
            "Home.razor rejoue les changements venus d'ailleurs, demarre et libere la session");
        var aiguillage = File.ReadAllText(Path.Combine(racine, "web/src/plan/Aiguillage.tsx"));
        var ecrans = Directory.GetFiles(Path.Combine(racine, "web/src/ecrans"), "Ecran*.tsx")
                              .Where(f => !f.EndsWith(".test.tsx")).Select(Path.GetFileNameWithoutExtension)
                              .Where(n => n != "EcranTableauEditions").ToArray();
        var montes = ecrans.Where(n => Regex.IsMatch(aiguillage, $@"import \{{ {n} \}} from '\.\./ecrans/{n}'")
                                       && Regex.IsMatch(aiguillage, $@"<{n}[\s/>]")).ToArray();
        Exiger(montes.Length == ecrans.Length && ecrans.Length >= 37,
            $"les ecrans React sont montes DIRECTEMENT par l'aiguillage, sans coquille .razor ({montes.Length}/{ecrans.Length})");
        // Sur le CODE de l'aiguillage : ses commentaires citent Home.razor et les coquilles.
        var codeAiguillage = Regex.Replace(aiguillage, @"/\*[\s\S]*?\*/|//[^\n]*", "");
        Exiger(!codeAiguillage.Contains("IleReact") && !Regex.IsMatch(codeAiguillage, @"\.razor|Components/"),
            "l'aiguillage ne repasse par aucune coquille Blazor");

        // Bac a sable : JAMAIS l'etat reel de l'utilisateur.
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-plantravail-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
        try
        {
            Jouer();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatBanc); } catch (IOException) { }
        }

        Console.WriteLine();
        Console.WriteLine(Echecs.Count == 0
            ? "PlanTravailCheck : vert."
            : $"PlanTravailCheck : {Echecs.Count} echec(s).");
        return Echecs.Count == 0 ? 0 : 1;
    }

    private static void Jouer()
    {
        var plan = new SessionPlanTravail(null, null);
        plan.Initialiser();
        void Agir(string geste, object? valeur = null) => plan.Agir(geste, Valeur(valeur));

        // --- 1. La page s'ouvre sur l'accueil, sans ecran ni affaire.
        var vue = plan.Vue();
        Exiger(vue.Accueil is not null && vue.Barre is not null && vue.Proc is null && vue.Dossier is null,
            "la page s'ouvre sur l'accueil (barre et accueil), sans ecran ni affaire");

        // --- 2. Ouvrir un dossier : l'arbre, « Informations du dossier » et le KBIS a jour.
        Agir("ouvrirNumero", "004176");
        vue = plan.Vue();
        var d = vue.Dossier;
        Exiger(d is { Numero: "004176" } && d.Sommaire.Champs.Any(c => c.Libelle == "SIREN"),
            "un ecran qui remonte le numero 004176 ouvre l'affaire, avec sa fiche sommaire");
        Exiger(d?.Fiche is { Branche: "dossier" } && d.Apercu?.Apercu is { Libelle: "Kbis a jour", Kbis: not null },
            "l'affaire s'ouvre sur « Informations du dossier » au milieu et le KBIS a jour a droite");

        // --- 3. Une piece de l'arbre : la saisie guidee va sur SON etape.
        var statuts = Ligne(d!.Arbre, "Statuts signes");
        Exiger(statuts is not null, "la piece « Statuts signes » est dans l'arbre rendu");
        Agir("arbreActiver", statuts!.Id);
        vue = plan.Vue();
        d = vue.Dossier!;
        Exiger(d.Fiche is null && d.Etapes is { Courante: 2, Total: 7 } && d.Etapes.Pages[2].Titre == "Capital",
            "ouvrir les statuts amene la saisie guidee sur SON etape (Capital)");
        Exiger(vue.Defilement is { Index: 2, Doux: false },
            "la page recoit le defilement immediat vers cette etape");
        Exiger(d.Etapes!.Pages.Count(p => p.Rendue) == 3 && !d.Etapes.Pages[6].Rendue,
            "seules l'etape visible et ses deux voisines sont rendues");
        Exiger(d.Apercu?.Apercu is { Libelle: "Statuts signes" } && d.Apercu.Apercu.Feuilles.Length > 0,
            "la piece s'affiche a droite, page par page");

        // --- 4. Passer les etapes (frise et pages).
        Agir("allerEtape", 1);
        vue = plan.Vue();
        d = vue.Dossier!;
        Exiger(d.Etapes!.Courante == 1 && vue.Defilement is { Index: 1, Doux: true },
            "la frise passe a l'etape 2 et fait defiler la page en douceur");
        Agir("etapeVisible", 3);
        Exiger(plan.Vue().Dossier!.Etapes!.Courante == 3, "la page dit quelle etape est visible : la frise suit");
        Agir("allerEtape", 99);
        Exiger(plan.Vue().Dossier!.Etapes!.Courante == 6, "aller au-dela de la derniere etape s'arrete au controle final");
        Agir("allerEtape", 1);

        // --- 5. Une saisie refusee, puis acceptee.
        ChampSaisieVue Siren() => plan.Vue().Dossier!.Etapes!.Pages[1].Champs.Single(c => c.Colonne == "DSSRC_SIREN");
        Agir("ecrire", new { etape = "IDENTITE", colonne = "DSSRC_SIREN", valeur = "402 118 447" });
        Exiger(Siren() is { Refus: not null, Valeur: "402 118 447" },
            "un SIREN a la cle fausse est refuse, avec son motif");
        var pasIdentite = plan.Vue().Dossier!.Etapes!.Pas[1];
        Exiger(pasIdentite is { Etat: "refuse", Pastille: "!", Refuses: 1 },
            "la frise marque l'etape « Identite » comme refusee");
        Exiger(plan.Vue().Dossier!.Bandeau is { Erreur: true } b && b.Liens.Any(l => l.Etape == "Identite" && l.Refuse),
            "le bandeau des defauts nomme l'etape a corriger");
        Agir("allerEtape", 6);
        var recap = plan.Vue().Dossier!.Etapes!.Pages[6].Recap;
        Exiger(recap is { Valide: false } && recap.Nombre > 0 && recap.Titre.Contains("bloque"),
            "le controle final dit ce qui bloque la validation");
        Agir("valider");
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[6].Recap is { Valide: false },
            "un dossier qui a un defaut ne se valide pas, meme si le geste arrive");
        Agir("allerEtape", 1);
        Agir("saisie", new { etape = "IDENTITE", colonne = "DSSRC_SIREN", valeur = "109 678 235" });
        Exiger(Siren() is { Refus: null, Valeur: "109 678 235" }, "la frappe porte la valeur en direct");
        Agir("ecrire", new { etape = "IDENTITE", colonne = "DSSRC_SIREN", valeur = "109 678 235" });
        Exiger(plan.Vue().Dossier!.Bandeau is null, "la saisie corrigee fait disparaitre le bandeau des defauts");

        // --- 6. Valider le dossier, le rouvrir.
        Agir("allerEtape", 6);
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[6].Recap is { Valide: false, Nombre: 0 },
            "plus rien ne bloque la validation");
        Agir("valider");
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[6].Recap is { Valide: true }, "le dossier est valide");
        Agir("rouvrir");
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[6].Recap is { Valide: false, Nombre: 0 },
            "« Rouvrir la saisie » retire la validation");
        Agir("valider");
        // Une seule source de verite : l'ecran des demandes recoit les dossiers valides.
        var attendus = JeuDemo.Demandes.Select(x => x.Dossier).Distinct().Where(x => x == "004176").ToArray();
        Agir("ouvrirCode", "PROC_DEMANDE");
        Exiger(plan.Vue().Proc is { Code: "PROC_DEMANDE" } pd && attendus.All(pd.Ecran.Valides.Contains)
               && pd.Ecran.Valides.All(v => v == "004176"),
            "l'ecran des demandes recoit la liste des dossiers valides");
        Agir("fermerProc");

        // --- 7. Fiches multiples : ajouter un etablissement.
        Agir("ouvrirNumero", "004176");
        Agir("arbreActiver", Ligne(plan.Vue().Dossier!.Arbre, "Statuts signes")!.Id);
        Agir("allerEtape", 3);
        Agir("ajouterFiche", "SIEGE");
        var fiches = plan.Vue().Dossier!.Etapes!.Pages[3].Fiches;
        Exiger(fiches is { Unite: "Etablissement", Nombre: 2, Actif: 1 }
               && plan.Vue().Dossier!.Etapes!.Pas[3].Fiches == 2,
            "« + Ajouter » ouvre un deuxieme etablissement, et la frise le compte");
        Agir("supprimerFiche", "SIEGE");
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[3].Fiches is { Nombre: 1 }, "« Retirer » le supprime");

        // --- 8. Apercu, impression et envoi par mail.
        Agir("imprimer");
        Exiger(plan.Vue().Impression is { Exemplaires: 1 } imp && imp.Imprimantes.Length > 0,
            "le bouton Imprimer ouvre la boite de choix de l'imprimante");
        Agir("exemplaires", "abc");
        Exiger(plan.Vue().Impression!.Exemplaires == 1, "un nombre d'exemplaires illisible ne change rien");
        Agir("exemplaires", "3");
        Agir("rectoVerso");
        Agir("lancerImpression");
        vue = plan.Vue();
        Exiger(vue.Impression is null && vue.Dossier!.Apercu!.Apercu!.Message.Contains("3 ex.")
               && vue.Dossier.Apercu.Apercu.Message.Contains("recto-verso"),
            "Imprimer envoie la piece et le dit sous l'apercu");
        Agir("envoiMail");
        Exiger(plan.Vue().EnvoiMail is { PeutEnvoyer: false, Message: 0 }, "l'envoi par mail attend un destinataire");
        Agir("destinataire", "greffe.civil@exemple.fr");
        Agir("message", "1");
        Exiger(plan.Vue().EnvoiMail is { PeutEnvoyer: true, Message: 1 } m && m.Corps.Contains("convocation"),
            "le destinataire et le message type se choisissent");
        Agir("envoyerMail");
        Exiger(plan.Vue().EnvoiMail is null && plan.Vue().Dossier!.Apercu!.Apercu!.Message.Contains("greffe.civil@exemple.fr"),
            "le mail part et le dit (maquette)");

        // --- 9. L'arbre : filtre, menu du clic droit, nouvel element.
        Agir("filtre", "Kbis");
        d = plan.Vue().Dossier!;
        Exiger(d.Filtre == "Kbis" && Lignes(d.Arbre).Where(l => l.Enfants.Length == 0 && l.Niveau > 0)
                                                     .All(l => l.Texte.Contains("Kbis", StringComparison.OrdinalIgnoreCase)),
            "le filtre de l'arbre ne garde que les elements qui portent le texte");
        Agir("effacerFiltre");
        Exiger(plan.Vue().Dossier!.Filtre == "", "la croix efface le filtre");
        var kbis = Ligne(plan.Vue().Dossier!.Arbre, "Kbis a jour")!;
        Agir("arbreMenu", new { id = kbis.Id, clientX = 300, clientY = 400 });
        Exiger(plan.Vue().MenuNoeud is { Titre: "Kbis a jour", X: 300, Y: 400, EstPiece: true },
            "le menu du clic droit s'ouvre sous le pointeur, sur le bon noeud");
        Agir("fermerMenu");
        Agir("ouvrirNouveau");
        Exiger(plan.Vue().Nouveau is { } nv && nv.Natures.Length == AccesGed.Natures.Length,
            "« + Nouveau » propose toutes les natures");
        Agir("fermerNouveau");
        Exiger(plan.Vue().Nouveau is null, "Annuler ferme la boite");

        // --- 10. Deux ecrans en onglets, reordonnes ; Echap.
        Agir("fermerDossier");
        Agir("ouvrirCode", "PROC_FACTURE");
        Agir("ouvrirCode", "PROC_ENCAISS");
        vue = plan.Vue();
        Exiger(vue.Onglets is { Length: 2 } o && o[0].Code == "PROC_FACTURE" && o[1] is { Code: "PROC_ENCAISS", Actif: true }
               && vue.Proc is { Code: "PROC_ENCAISS", BoutonFermer: true },
            "deux ecrans ouverts prennent deux onglets, le dernier est montre");
        Exiger(vue.Proc!.Sujet is { Titre: "Argent" } s && s.Onglets.Any(x => x.Code == "PROC_FACTURE"),
            "le bandeau du sujet montre les ecrans voisins");
        Agir("ongletDebut", new { code = "PROC_ENCAISS", x = 150.0, y = 20.0,
                                  mesure = new { page = new[] { 1500.0, 900.0, 40.0 }, cases = new[] { new[] { 10.0, 100.0 }, new[] { 110.0, 200.0 } } } });
        Agir("ongletGlisse", new { x = 40.0, y = 20.0 });
        Agir("ongletFin", new { x = 40.0, y = 20.0 });
        Exiger(plan.Vue().Onglets!.Select(x => x.Code).SequenceEqual(new[] { "PROC_ENCAISS", "PROC_FACTURE" }),
            "glisser un onglet le reordonne");
        Agir("ongletActiver", "PROC_FACTURE");
        Exiger(plan.Vue().Proc?.Code == "PROC_FACTURE", "un clic sur un onglet montre son ecran");
        Agir("favori", "PROC_FACTURE");
        Exiger(plan.Vue().Proc?.Favori == true, "l'etoile de l'ecran le met en favori");
        Agir("favori", "PROC_FACTURE");
        Agir("echap");
        Exiger(plan.Vue().Onglets is { Length: 1 } && plan.Vue().Proc?.Code == "PROC_ENCAISS",
            "Echap ferme l'ecran montre (son onglet) ; l'onglet voisin prend la place");
        Agir("ongletFermer", "PROC_ENCAISS");
        Exiger(plan.Vue().Onglets is null, "fermer le dernier onglet retire la barre d'onglets");
        Agir("ouvrirNumero", "004176");
        Agir("ouvrirCode", "PROC_FACTURE");
        Agir("echap");
        Exiger(plan.Vue().Proc is null, "Echap ferme l'ecran ouvert...");
        Agir("ouvrirNumero", "004176");
        Agir("echap");
        Exiger(plan.Vue().Dossier is null, "... et l'affaire ouverte");

        // --- 11. Le mode tache : la tuile « Mes taches du jour » ouvre la TACHE, pas
        // seulement l'affaire (OuvrirAlerte -> OuvrirTache), comme sur l'ecran.
        var accueil = new SessionAccueil(AccueilEtat.Charger());
        accueil.Initialiser();
        var deno = accueil.TachesOuvertes().First(a => a.Tache is { Code: "DENO" });
        plan.SurGesteAccueil(new GesteIle("tache", Valeur(deno.Id)));
        var t = plan.Vue().Dossier?.Tache;
        Exiger(plan.Vue().Dossier?.Numero == deno.Tache!.Value.Dossier && t is { Code: "DENO" } && t.Effets.Length >= 4 && t.Cases.Length >= 1,
            "la tache du jour ouvre son affaire EN MODE TACHE (bandeau, checklists)");
        Agir("fermerDossier");
        Agir("ouvrirNumero", deno.Tache!.Value.Dossier);
        Exiger(plan.Vue().Dossier?.Tache is { Code: "DENO" },
            "rouvrir l'affaire d'une tache en attente, depuis n'importe quel ecran, rouvre sa tache");
        t = plan.Vue().Dossier!.Tache!;
        var inpi = Array.FindIndex(t!.Effets, e => e.Code == "0F");
        Exiger(inpi >= 0 && !t.Effets[inpi].Coche, "l'effet INPI est a faire");
        Agir("basculerEffet", inpi);
        Exiger(plan.Vue().Dossier!.Tache!.Effets[inpi].Coche, "cocher l'effet INPI le note comme traite");
        Agir("basculerTout");
        Exiger(plan.Vue().Dossier!.Tache!.ToutLeDossier && plan.Vue().Dossier!.Tache!.TexteTout == "Revenir a la tache",
            "« Afficher tout le dossier » rouvre la vue entiere sans quitter la tache");
        Agir("fermerTache");
        Exiger(plan.Vue().Dossier!.Tache is null, "« Fermer la tache » quitte le mode tache");

        // --- 12. Validation hierarchique : un stagiaire SOUMET, l'arbre ne bouge pas.
        Agir("fermerDossier");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("astagiaire")));
        Agir("ouvrirNumero", "004176");
        var avant = Lignes(plan.Vue().Dossier!.Arbre).Count();
        var cible = Ligne(plan.Vue().Dossier!.Arbre, "Liste des souscripteurs");
        Agir("arbreMenu", new { id = cible!.Id, clientX = 10, clientY = 10 });
        Agir("menuSupprimer");
        Exiger(Ligne(plan.Vue().Dossier!.Arbre, "Liste des souscripteurs") is not null
               && Lignes(plan.Vue().Dossier!.Arbre).Count() == avant,
            "un stagiaire qui supprime une piece ne la fait pas disparaitre : la demande attend un valideur");
        plan.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Agir("arbreMenu", new { id = Ligne(plan.Vue().Dossier!.Arbre, "Liste des souscripteurs")!.Id, clientX = 10, clientY = 10 });
        Agir("menuSupprimer");
        Exiger(Ligne(plan.Vue().Dossier!.Arbre, "Liste des souscripteurs") is null,
            "le greffier, lui, supprime en direct");

        // --- 13. Les gestes numerotes et le geste inconnu.
        plan.Jouer(new GestePlan("fermerDossier", Valeur(null), 41));
        Exiger(plan.Vue().Geste == 41 && plan.Vue().Dossier is null,
            "la vue dit le dernier geste joue : la page n'affiche jamais une vue plus ancienne");
        var refuse = false;
        try { Agir("gesteQuiNExistePas"); } catch (InvalidOperationException) { refuse = true; }
        Exiger(refuse, "un geste inconnu est une erreur franche, jamais ignore");

        plan.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
