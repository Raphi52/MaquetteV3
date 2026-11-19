using System.Globalization;
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

        // --- 0. Plus de Home.razor (Blazor retire, lot 3 du plan « finir le passage
        // a React », 19/11/2026) : la fenetre monte l'application React par SA racine,
        // Pont/Racines.cs (RacinePlanTravail), qui tient la session comme Home le faisait.
        var racines = File.ReadAllText(Path.Combine(racine, "Pont/Racines.cs"));
        Exiger(!File.Exists(Path.Combine(racine, "Components/Pages/Home.razor"))
               && racines.Contains("public override string Composant => \"Application\";")
               && racines.Contains("new Dictionary<string, object?> { [\"vue\"] = Plan.Vue() }"),
            "la racine de la fenetre monte UNE composante : l'application React entiere, avec la vue du plan de travail");
        var razor = Directory.EnumerateFiles(racine, "*.razor", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"[\\/](bin|obj|node_modules|binverif)[\\/]")).ToList();
        var projet = File.ReadAllText(Path.Combine(racine, "RigV3Desktop.csproj"));
        Exiger(razor.Count == 0 && projet.Contains("<Project Sdk=\"Microsoft.NET.Sdk\">")
               && !projet.Contains("Components.WebView") && !projet.Contains("Sdk.Razor"),
            $"plus aucun .razor ni paquet Blazor dans l'application ({razor.Count} .razor)");
        Exiger(racines.Contains("case \"geste\": Plan.SurGesteAccueil(valeur.Deserialize<GesteIle>(Repartiteur.Json)!); break;")
               && racines.Contains("case \"plan\": Plan.Jouer(valeur.Deserialize<GestePlan>(Repartiteur.Json)!); break;"),
            "la racine renvoie les gestes de l'accueil et du plan de travail a la session");
        Exiger(racines.Contains("Plan.Planifier = action => Planifier(action);")
               && racines.Contains("protected override void SurDemarrage() => Plan.Demarrer();")
               && racines.Contains("await Plan.DisposeAsync();"),
            "la racine rejoue les changements venus d'ailleurs, demarre et libere la session");
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
            JouerPont(racine);
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

    /// <summary>
    /// 0 bis. LE PONT JOUE de bout en bout (Pont/Repartiteur.cs), comme la page le
    /// fait par la messagerie de WebView2 : ouvrir la racine, remonter un geste,
    /// appeler une methode d'ecran, recevoir les erreurs. Les messages sont ceux
    /// que web/src/pont.ts poste et lit.
    /// </summary>
    private static void JouerPont(string racineDepot)
    {
        var postes = new List<JsonElement>();
        var verrou = new object();
        var racinePage = new RacinePlanTravail(null, null);
        var pont = new Repartiteur(racinePage,
            json => { lock (verrou) postes.Add(JsonDocument.Parse(json).RootElement.Clone()); },
            action => { lock (verrou) action(); });
        JsonElement Reponse(int numero)
        {
            lock (verrou) return postes.Last(m => m.TryGetProperty("reponse", out var r) && r.GetInt32() == numero);
        }
        JsonElement[] Racines()
        {
            lock (verrou) return postes.Where(m => m.TryGetProperty("evenement", out var e) && e.GetString() == "racine").ToArray();
        }
        void Envoyer(int numero, string nom, params object?[] args)
            => pont.Recevoir(JsonSerializer.Serialize(new { appel = numero, nom, args }, Repartiteur.Json)).GetAwaiter().GetResult();

        Envoyer(1, "Racine.Ouvrir");
        var ouverte = Reponse(1).GetProperty("valeur");
        Exiger(ouverte.GetProperty("composant").GetString() == "Application"
               && ouverte.GetProperty("rappels").EnumerateArray().Select(r => r.GetString()).SequenceEqual(new[] { "geste", "plan" })
               && !ouverte.GetProperty("bords").GetBoolean()
               && ouverte.GetProperty("proprietes").GetProperty("vue").GetProperty("accueil").ValueKind == JsonValueKind.Object,
            "pont : Racine.Ouvrir rend l'application, ses deux gestes et la vue de l'accueil (sans bandes de bord hors fenetre)");
        Exiger(racinePage.Demarree,
            "pont : la session demarre apres l'ouverture (minuteurs de l'accueil), comme apres le premier rendu");

        var envoisAvant = Racines().Length;
        Envoyer(2, "Racine.Rappeler", "plan", new { nom = "ouvrirNumero", valeur = "004176", numero = 1 });
        var apres = Racines().Skip(envoisAvant).LastOrDefault();
        Exiger(Reponse(2).TryGetProperty("valeur", out _) && !Reponse(2).TryGetProperty("erreur", out _)
               && apres.ValueKind == JsonValueKind.Object
               && apres.GetProperty("valeur").GetProperty("proprietes").GetProperty("vue").GetProperty("dossier").GetProperty("numero").GetString() == "004176"
               && apres.GetProperty("valeur").GetProperty("proprietes").GetProperty("vue").GetProperty("geste").GetInt32() == 1,
            "pont : un geste du plan remonte a la session, et la racine redessinee (affaire 004176, geste 1) part a la page");

        Envoyer(3, "EcranVerrous.Verrouilles", "R. VILAIN", false);
        Exiger(Reponse(3).GetProperty("valeur").GetProperty("lignes").GetArrayLength()
               == EcranVerrous.Verrouilles("R. VILAIN", false).Lignes.Length,
            "pont : une methode [JSInvokable] d'ecran est jointe par son identifiant, arguments convertis du JSON");

        Envoyer(4, "Ecran.Inconnu");
        Envoyer(5, "EcranVerrous.Verrouilles", "R. VILAIN");
        Exiger(Reponse(4).GetProperty("erreur").GetString()!.Contains("Aucune methode [JSInvokable(\"Ecran.Inconnu\")]")
               && Reponse(5).GetProperty("erreur").GetString()!.Contains("attend 2 argument(s), 1 recu(s)"),
            "pont : un identifiant inconnu ou un mauvais compte d'arguments est une erreur rendue a la page, jamais avalee");

        // Chaque identifiant appele par la page existe cote C# : sous Blazor, une
        // faute de frappe ne se voyait qu'a l'execution ; ici elle se voit au banc.
        var appels = Directory.GetFiles(Path.Combine(racineDepot, "web/src"), "*.ts*", SearchOption.AllDirectories)
            .Where(f => !Regex.IsMatch(f, @"\.test\.tsx?$|[\\/]test[\\/]"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"appeler<[^>]*>\(\s*'([^']+)'").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        // « Racine.Ouvrir » et « Racine.Rappeler » sont les deux appels que le repartiteur
        // traite lui-meme (la racine de la fenetre), sans methode [JSInvokable].
        var absents = appels.Where(id => id is not ("Racine.Ouvrir" or "Racine.Rappeler")
                                         && !Repartiteur.Methodes.ContainsKey(id)).ToList();
        Exiger(appels.Count >= 20 && absents.Count == 0,
            $"pont : les {appels.Count} identifiants appeles par web/src existent tous en [JSInvokable] ({string.Join(", ", absents)})");

        racinePage.DisposeAsync().AsTask().GetAwaiter().GetResult();
        int envoisFin;
        lock (verrou) envoisFin = postes.Count;
        pont.Pousser();
        Envoyer(6, "Racine.Ouvrir");
        lock (verrou) Exiger(racinePage.Fermee && postes.Count == envoisFin,
            "pont : une fois la fenetre fermee, plus rien n'est poste a sa page");
    }

    private static void Jouer()
    {
        var plan = new SessionPlanTravail(null, null);
        plan.Initialiser();
        void Agir(string geste, object? valeur = null) => plan.Agir(geste, Valeur(valeur));

        // --- 1. La page s'ouvre sur l'accueil, sans ecran ni affaire.
        var vue = plan.Vue();
        Exiger(vue.Accueil is not null && vue.Barre && vue.Proc is null && vue.Dossier is null,
            "la page s'ouvre sur l'accueil (barre et accueil), sans ecran ni affaire");

        // --- 2. Ouvrir un dossier : l'arbre, « Informations du dossier » et le KBIS a jour.
        Agir("ouvrirNumero", "004176");
        vue = plan.Vue();
        var d = vue.Dossier;
        Exiger(d is { Numero: "004176" } && d.Sommaire.Champs.Any(c => c.Libelle == "SIREN"),
            "un ecran qui remonte le numero 004176 ouvre l'affaire, avec sa fiche sommaire");
        Exiger(d?.Fiche is { Branche: "dossier" } && d.Apercu?.Apercu is { Libelle: "Kbis a jour", Kbis: not null },
            "l'affaire s'ouvre sur « Informations du dossier » au milieu et le KBIS a jour a droite");
        Exiger(!d!.Sommaire.Champs.Any(c => c.Libelle == "N° de gestion"),
            "le numero de gestion titre l'entete : il ne se repete pas dans les puces de la fiche sommaire");

        // --- 2 bis. L'historique sous les donnees (proposition 5 du /draft du 19/11/2026) :
        // les pieces datees de l'arbre, la plus recente en tete ; un clic ouvre la piece et
        // deplie sa branche, meme repliee dans l'arbre.
        var histo = d.Historique;
        var dates = histo.Select(p => DateTime.ParseExact(p.Date, "dd/MM/yyyy", CultureInfo.InvariantCulture)).ToList();
        Exiger(histo.Length > 7 && dates.Zip(dates.Skip(1)).All(x => x.First >= x.Second),
            $"l'historique liste les pieces datees de l'arbre, la plus recente en tete ({histo.Length} pieces)");
        Exiger(histo[0] is { Libelle: "Kbis a jour", Rubrique: "Dossier RCS", Judiciaire: false, Actif: true }
               && histo.Count(p => p.Actif) == 1,
            "en tete : le KBIS a jour, seul marque comme la piece ouverte a droite");
        var rapport = histo.FirstOrDefault(p => p.Libelle == "Rapport d expertise");
        Exiger(rapport is { Judiciaire: true, Rubrique: "Mesures d instruction", Date: "03/03/2023", Pages: 42 },
            "une piece judiciaire porte sa rubrique, sa date et son nombre de pages");
        // CLASSEUR (/draft du 19/11/2026, « Chronologie ») : l'affaire s'ouvre sur ses
        // cartes FERMEES hors de l'element choisi (« Informations du dossier », cote RCS) ;
        // la carte Instances l'est donc, et le rapport qu'elle range n'est pas rendu.
        Exiger(Ligne(d.Arbre, "Instances") is { Ouvert: false } && Ligne(d.Arbre, "Rapport d expertise") is null,
            "le classeur s'ouvre cartes fermees hors de l'element choisi : le rapport n'est pas rendu");
        Agir("historiqueOuvrir", rapport!.Id);
        vue = plan.Vue();
        Exiger(vue.Dossier!.Apercu?.Apercu is { Libelle: "Rapport d expertise" }
               && Ligne(vue.Dossier.Arbre, "Rapport d expertise") is { Actif: true }
               && Ligne(vue.Dossier.Arbre, "Instances") is { Ouvert: true }
               && vue.Dossier.Historique.Single(p => p.Actif).Libelle == "Rapport d expertise",
            "un clic dans l'historique ouvre la piece a droite et la montre choisie, sa carte ouverte");
        // L'affaire rouverte (un arbre neuf) retrouve la CARTE qu'on y a laissee ouverte, quoi
        // qui l'ait ouverte -- ici l'historique (demande du 19/11/2026, ArbreGedEnregistre.Cartes).
        Agir("ouvrirNumero", "004176");
        Exiger(Ligne(plan.Vue().Dossier!.Arbre, "Instances") is { Ouvert: true }
               && Ligne(plan.Vue().Dossier!.Arbre, "Rapport d expertise") is { Actif: false },
            "rouverte, l'affaire retrouve la carte laissee ouverte (Instances) ; l'element choisi est celui d'entree");
        Agir("arbreRubrique", Ligne(plan.Vue().Dossier!.Arbre, "Instances")!.Id);
        Agir("ouvrirNumero", "004176");
        Exiger(Lignes(plan.Vue().Dossier!.Arbre).Where(l => l.Groupe > 0 && l.Niveau == l.NiveauGroupe).All(l => !l.Ouvert),
            "une carte refermee le reste a la reouverture : l'affaire repart cartes fermees");
        d = plan.Vue().Dossier;

        // --- 2 ter. La vue du dossier, proposition 3 « Onglets Suivi / Fil » du /draft du
        // 19/11/2026 (conv-2009) : le LISERE des rubriques, l'arbre qui se replie, le fil.
        var rubriques = new[] { "Pieces jointes", "Depots d'actes", "Depots des comptes annuels", "Formalites",
                                "Inscriptions privileges", "Instances", "Procedures collectives" }
            .Select(r => Ligne(d!.Arbre, r)).ToArray();
        Exiger(rubriques.All(r => r is not null)
               && rubriques.Select(r => r!.Groupe).SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7 })
               && rubriques.All(r => r!.NiveauGroupe == 2 && r.Niveau == 2),
            "chaque rubrique de l'arbre porte sa teinte de lisere, dans l'ordre de l'arbre ; elle en est la tete");
        Exiger(Ligne(d!.Arbre, "Dossier RCS") is { Groupe: 0 } && Ligne(d.Arbre, "Kbis a jour") is { Groupe: 0 }
               && Ligne(d.Arbre, "Judiciaire") is { Groupe: 0 },
            "les domaines et les pieces posees a meme le domaine n'ont pas de lisere");
        // --- Le CLASSEUR (proposition 5 « Chronologie ») : au plus une carte ouverte par
        // domaine, qui rend TOUT ce qu'elle range ; la date, les pages, la derniere date.
        Exiger(Lignes(d.Arbre).Where(l => l.Groupe > 0 && l.Niveau == l.NiveauGroupe).All(l => !l.Ouvert)
               && Ligne(d.Arbre, "Dossier RCS") is { Ouvert: true } && Ligne(d.Arbre, "Judiciaire") is { Ouvert: true },
            "a l'ouverture, les deux domaines sont rendus et aucune carte n'est ouverte (l'element choisi est hors rubrique)");
        Exiger(Ligne(d.Arbre, "Instances") is { Ouvert: false, Compte: 7, Derniere: "11/07/2023", Enfants.Length: 0 }
               && Ligne(d.Arbre, "Depots d'actes") is { Derniere: "15/10/2024" },
            "une carte fermee annonce son nombre de pieces et la date de la plus recente, sans rien rendre dessous");
        Agir("arbreRubrique", Ligne(d.Arbre, "Depots d'actes")!.Id);
        Agir("arbreRubrique", Ligne(plan.Vue().Dossier!.Arbre, "Instances")!.Id);
        d = plan.Vue().Dossier;
        Exiger(Ligne(d!.Arbre, "Depots d'actes") is { Ouvert: true } && Ligne(d.Arbre, "Instances") is { Ouvert: true }
               && Ligne(d.Arbre, "Bail commercial") is { Date: "12/03/2024", Pages: 9 }
               && Ligne(d.Arbre, "Rapport d expertise") is { Date: "03/03/2023", Pages: 42 },
            "une carte ouverte rend tout ce qu'elle range, sous-dossiers compris ; chaque piece porte sa date et ses pages");
        Exiger(Ligne(d.Arbre, "Bail commercial") is { Groupe: 2, NiveauGroupe: 2, Niveau: 4 }
               && Ligne(d.Arbre, "Rapport d expertise") is { Groupe: 6, NiveauGroupe: 2 },
            "tout ce qu'une rubrique range, a toute profondeur, porte sa teinte et le niveau de sa tete");
        Agir("arbreRubrique", Ligne(d.Arbre, "Pieces jointes")!.Id);
        d = plan.Vue().Dossier;
        Exiger(Ligne(d!.Arbre, "Pieces jointes") is { Ouvert: true } && Ligne(d.Arbre, "Depots d'actes") is { Ouvert: false }
               && Ligne(d.Arbre, "Instances") is { Ouvert: true },
            "ouvrir une carte referme les autres de SON domaine, pas celles de l'autre classeur");
        Agir("arbreRubrique", Ligne(d.Arbre, "Pieces jointes")!.Id);
        Exiger(Ligne(plan.Vue().Dossier!.Arbre, "Pieces jointes") is { Ouvert: false },
            "un second clic referme la carte");
        Agir("arbreRubrique", Ligne(plan.Vue().Dossier!.Arbre, "Formalites")!.Id);
        Exiger(Lignes(plan.Vue().Dossier!.Arbre).Single(l => l.Actif).Texte == "Informations des Formalites",
            "la carte Formalites, qui commence par sa fiche, l'ouvre au milieu (comme son depliage dans l'arbre)");
        // Le CLASSEMENT de la carte ouverte (« Par dossier » / « Par date ») est retenu d'une
        // affaire a l'autre et au redemarrage (demande du 19/11/2026, AccueilEtat.TriClasseur).
        Exiger(plan.Vue().Dossier!.TriClasseur == "date", "la carte ouverte se classe « Par date » tant qu'on n'a rien choisi");
        Agir("arbreTri", "dossier");
        Exiger(plan.Vue().Dossier!.TriClasseur == "dossier" && AccueilEtat.Charger().TriClasseur == "dossier",
            "« Par dossier » clique : la vue le rend, et le fichier d'etat l'a deja");
        Agir("arbreTri", "n'importe quoi");
        Exiger(plan.Vue().Dossier!.TriClasseur == "dossier", "une valeur inconnue est ignoree");
        Agir("ouvrirNumero", "004176");
        Exiger(plan.Vue().Dossier!.TriClasseur == "dossier", "le classement reste d'une affaire a l'autre");
        var redemarre = new SessionPlanTravail(null, null);
        redemarre.Initialiser();
        redemarre.Agir("ouvrirNumero", Valeur("004176"));
        Exiger(redemarre.Vue().Dossier?.TriClasseur == "dossier" && AccueilEtat.Charger().TriClasseur == "dossier",
            "le classement survit au redemarrage : une session neuve le relit dans le fichier d'etat");
        redemarre.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Agir("arbreTri", "date");
        Exiger(plan.Vue().Dossier!.TriClasseur == "date" && AccueilEtat.Charger().TriClasseur == "date",
            "revenir « Par date » s'enregistre aussi");
        // Les SOUS-DOSSIERS REPLIES du classeur (« Par dossier ») : retenus PAR AFFAIRE, au
        // redemarrage ; une piece choisie ailleurs rouvre celui qui la range (demande du 19/11/2026).
        const string depotA = "Depot n A2024/038654";
        LigneArbreVue Arbre() => plan.Vue().Dossier!.Arbre;
        List<string>? Retenus() => AccueilEtat.Charger().ArbreGedDe("004176")?.Replies;
        // Ouvre la carte si elle est fermee (elle a pu rester ouverte : l'affaire s'en souvient).
        void OuvrirCarte(SessionPlanTravail s, string carte)
        {
            if (Ligne(s.Vue().Dossier!.Arbre, carte) is { Ouvert: false } l) s.Agir("arbreRubrique", Valeur(l.Id));
        }
        Agir("ouvrirNumero", "004176");
        OuvrirCarte(plan, "Depots d'actes");
        Exiger(Ligne(Arbre(), depotA) is { Replie: false }, "un sous-dossier est deplie tant qu'on ne l'a pas replie");
        Agir("arbreReplierDossier", Ligne(Arbre(), depotA)!.Id);
        Exiger(Ligne(Arbre(), depotA) is { Replie: true, Enfants.Length: 2 } && Retenus() is { Count: 1 },
            "replie : la vue le dit, ses pieces sont rendues quand meme (« Par date » les montre), et le fichier d'etat le retient");
        Agir("arbreReplierDossier", Ligne(Arbre(), "Depots d'actes")!.Id);
        Agir("arbreReplierDossier", Ligne(Arbre(), "Bail commercial")!.Id);
        Exiger(Ligne(Arbre(), "Depots d'actes") is { Replie: false, Ouvert: true } && Retenus() is { Count: 1 },
            "ce geste ne replie ni une carte de rubrique ni une piece");
        Agir("filtre", "certificat");
        Exiger(Ligne(Arbre(), depotA) is { Replie: false }, "pendant une recherche, rien n'est replie : elle montre ce qu'elle trouve");
        Agir("effacerFiltre");
        // Une session NEUVE (le redemarrage) ; l'autre affaire y est ouverte, pas dans celle
        // du banc : chaque affaire ouverte prend un onglet, et la suite compte les onglets.
        var relance = new SessionPlanTravail(null, null);
        relance.Initialiser();
        relance.Agir("ouvrirNumero", Valeur("004151"));
        // 004151 peut avoir une tache en attente (selon la date) : l'ouvrir ouvre sa tache, et
        // l'arbre se replie sur ce qu'elle nomme. « Afficher tout le dossier » le rend entier.
        if (relance.Vue().Dossier!.Tache is not null) relance.Agir("basculerTout", Valeur(null));
        OuvrirCarte(relance, "Depots d'actes");
        Exiger(Ligne(relance.Vue().Dossier!.Arbre, depotA) is { Replie: false },
            "le repli vaut pour SON affaire : la meme branche d'une autre affaire reste depliee");
        relance.Agir("ouvrirNumero", Valeur("004176"));
        Exiger(Ligne(relance.Vue().Dossier!.Arbre, "Depots d'actes") is { Ouvert: true },
            "la carte laissee ouverte survit au redemarrage : l'affaire rouverte par une session neuve la rouvre");
        Exiger(Ligne(relance.Vue().Dossier!.Arbre, depotA) is { Replie: true },
            "le repli survit au redemarrage : une session neuve le relit pour l'affaire rouverte");
        var certificat = relance.Vue().Dossier!.Historique.First(p => p.Libelle == "Certificat de depot d'actes");
        relance.Agir("historiqueOuvrir", Valeur(certificat.Id));
        var apresHistorique = relance.Vue().Dossier!.Arbre;
        Exiger(Ligne(apresHistorique, depotA) is { Replie: false }
               && Ligne(apresHistorique, "Certificat de depot d'actes") is { Actif: true } && Retenus() is null or { Count: 0 },
            "ouvrir depuis l'historique une piece d'un sous-dossier replie le rouvre, et le fichier l'oublie");
        relance.Agir("arbreReplierDossier", Valeur(Ligne(apresHistorique, depotA)!.Id));
        Exiger(Ligne(relance.Vue().Dossier!.Arbre, depotA) is { Replie: true },
            "replier celui qui range la piece DEJA ouverte reste possible : rien ne le rouvre tant que le choix ne change pas");
        relance.Agir("arbreReplierDossier", Valeur(Ligne(relance.Vue().Dossier!.Arbre, depotA)!.Id));
        Exiger(Ligne(relance.Vue().Dossier!.Arbre, depotA) is { Replie: false } && Retenus() is null or { Count: 0 },
            "un second clic le rouvre, et le fichier l'oublie");
        relance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // La suite part de l'etat d'entree de l'affaire (la fiche Formalites a change la mise en page).
        Agir("ouvrirNumero", "004176");
        d = plan.Vue().Dossier;
        Exiger(d.Historique.Single(p => p.Libelle == "Rapport d expertise").Groupe == 6
               && d.Historique.Single(p => p.Libelle == "Kbis a jour").Groupe == 0,
            "dans l'historique, le point d'une piece prend la teinte de sa rubrique dans l'arbre");

        Exiger(!d.ArbreReplie && d.Poignee
               && d.StyleCorps.StartsWith("grid-template-columns: ") && d.StyleCorps.Contains(" minmax(240px, 20%); grid-template-rows: minmax(0, 1fr)"),
            "la colonne de suivi est la derniere colonne du corps, sur une seule rangee");
        var colonnesDeplie = d.StyleCorps.Split(';')[0].Split(' ').Length;
        var choisie = Lignes(d.Arbre).Single(l => l.Actif).Texte;
        Agir("arbreReplier");
        var replie = plan.Vue().Dossier!;
        Exiger(replie.ArbreReplie && !replie.Poignee
               && replie.StyleCorps.Split(';')[0].Split(' ').Length == colonnesDeplie - 2
               && replie.StyleCorps.Contains("grid-template-rows: auto minmax(0, 1fr)"),
            "arbre replie : sa colonne et sa poignee partent, une rangee s'ouvre pour le chemin");
        Exiger(replie.Chemin.Length >= 2 && replie.Chemin[0] == "004176" && replie.Chemin[^1] == choisie,
            $"le chemin va du numero de l'affaire a l'element choisi dans l'arbre ({string.Join(" > ", replie.Chemin)})");
        Agir("ouvrirNumero", "004176");
        Exiger(plan.Vue().Dossier!.ArbreReplie, "l'arbre reste replie d'une affaire a l'autre, comme sa largeur");
        Agir("arbreDeplier");
        d = plan.Vue().Dossier!;
        Exiger(!d.ArbreReplie && d.Poignee && d.StyleCorps.Split(';')[0].Split(' ').Length == colonnesDeplie,
            "l'arbre se rouvre a sa place");

        // LA COLONNE DE SUIVI se replie a droite et se glisse comme les autres (demande du 19/11/2026).
        Exiger(!d.SuiviReplie && d.StyleCorps.Contains(" 10px minmax(240px, 20%); "),
            "la colonne de suivi a sa gouttiere, ou glisse sa poignee, et garde sa largeur souple tant qu'on ne la glisse pas");
        Agir("suiviReplier");
        var suiviReplie = plan.Vue().Dossier!;
        Exiger(suiviReplie.SuiviReplie && suiviReplie.StyleCorps.Contains(" 10px 38px; ")
               // « minmax(240px, 20%) » compte deux morceaux, « 38px » un seul.
               && suiviReplie.StyleCorps.Split(';')[0].Split(' ').Length == colonnesDeplie - 1,
            $"repliee, la colonne de suivi n'est plus qu'un rail de 38 px ({suiviReplie.StyleCorps})");
        Agir("ouvrirNumero", "004176");
        Exiger(plan.Vue().Dossier!.SuiviReplie, "la colonne de suivi reste repliee d'une affaire a l'autre, comme l'arbre");
        Agir("suiviDeplier");
        Exiger(!plan.Vue().Dossier!.SuiviReplie && plan.Vue().Dossier!.StyleCorps.Contains(" 10px minmax(240px, 20%); "),
            "la colonne de suivi se rouvre a sa largeur");
        // Glisser a GAUCHE elargit le suivi, depuis la largeur rendue que l'ecran envoie.
        Agir("redimDebut", new { colonne = 3, x = 1500.0, largeur = 300.0 });
        Agir("redim", new { x = 1400.0, y = 10.0 });
        Exiger(plan.Vue().Dossier!.StyleCorps.Contains(" 10px 400px; "),
            $"glisse de 100 px vers la gauche, le suivi passe de 300 a 400 px ({plan.Vue().Dossier!.StyleCorps})");
        Agir("redim", new { x = 2400.0, y = 10.0 });
        Exiger(plan.Vue().Dossier!.StyleCorps.Contains(" 10px 240px; "), "le suivi ne descend pas sous 240 px");
        Agir("redim", new { x = -2000.0, y = 10.0 });
        Exiger(plan.Vue().Dossier!.StyleCorps.Contains(" 10px 900px; "), "le suivi ne depasse pas 900 px");
        Agir("redim", new { x = 1450.0, y = 10.0 });
        Agir("redimFin");
        Agir("ouvrirNumero", "004176");
        d = plan.Vue().Dossier!;
        Exiger(!d.Voile && d.StyleCorps.Contains(" 10px 350px; ") && !d.StyleCorps.Contains("user-select"),
            "la largeur glissee du suivi tient, d'une affaire a l'autre");
        // Ce login voit les deux domaines, sans ajout ni masque : toutes les pieces de l'arbre.
        Exiger(d.Pieces > 0 && d.Pieces == d.Arbre.Compte,
            $"l'entete de l'arbre compte les pieces visibles ({d.Pieces})");

        var filAvant = d.Fil.Length;
        Agir("filEcrire", "   ");
        Exiger(plan.Vue().Dossier!.Fil.Length == filAvant, "une note vide n'entre pas au fil");
        Agir("filEcrire", "  Date de naissance du gerant a demander  ");
        var fil = plan.Vue().Dossier!.Fil;
        Exiger(fil.Length == filAvant + 1
               && fil[^1] is { Modification: false, Texte: "Date de naissance du gerant a demander" }
               && fil[^1].Quand == DateTime.Now.ToString("HH:mm") && fil[^1].Initiales.Length > 0,
            "une note ecrite entre au fil, a la fin, datee de l'heure du jour et signee");
        Exiger(AccueilEtat.Charger().Fil.TryGetValue("004176", out var enregistre)
               && enregistre[^1].Texte == "Date de naissance du gerant a demander",
            "le fil est enregistre avec l'etat de l'application : il survit a sa fermeture");

        var midi = new DateTime(2026, 11, 19, 12, 0, 0);
        var fondu = SessionPlanTravail.FilDeLAffaire(
            [new MessageFil(midi.AddHours(-1), "cedric.benier", "PV recu"), new MessageFil(midi, "raphael.vilain", "Merci")],
            [new ModificationChamp(midi.AddMinutes(-30), "raphael.vilain", "Dossier > Capital", "Montant du capital",
                                   "CAPITAL", "15000", "40000"),
             new ModificationChamp(midi.AddDays(-1), "raphael.vilain", "Dossier > Siege", "Enseigne", "ENSEIGNE", "", "Billards")],
            midi);
        Exiger(fondu.Select(m => m.Modification ? "M:" + m.Champ : m.Texte)
                    .SequenceEqual(new[] { "M:Enseigne", "PV recu", "M:Montant du capital", "Merci" }),
            "le fil fond notes et valeurs changees, du plus ancien au plus recent");
        Exiger(fondu[0] is { Quand: "hier 12:00", Avant: "(vide)", Apres: "Billards" }
               && fondu[1] is { Initiales: "CB", Auteur: "C. BENIER", Quand: "11:00" }
               && fondu[2] is { Auteur: "R. VILAIN", Avant: "15000", Apres: "40000" },
            "chaque message dit qui (« C. BENIER », « CB »), quand (« 11:00 », « hier 12:00 ») et, pour une valeur, avant et apres");

        // --- 3. Une piece de l'arbre : la saisie guidee va sur SON etape.
        var statuts = Ligne(d!.Arbre, "Statuts signes");
        Exiger(statuts is not null, "la piece « Statuts signes » est dans l'arbre rendu");
        Agir("arbreActiver", statuts!.Id);
        vue = plan.Vue();
        d = vue.Dossier!;
        Exiger(d.Fiche is null && d.Etapes is { Courante: 2, Total: 6 } && d.Etapes.Pages[2].Titre == "Capital",
            "ouvrir les statuts amene la saisie guidee sur SON etape (Capital)");
        Exiger(vue.Defilement is { Index: 2, Doux: false },
            "la page recoit le defilement immediat vers cette etape");
        Exiger(d.Etapes!.Pages[2] is { Ouverte: true } && d.Etapes.Pages[2].Champs.Length > 0
               && d.Etapes.Pages[5] is { Ouverte: false, Champs.Length: 0 },
            "l'etape de la piece se deplie dans l'accordeon ; une etape repliee ne porte aucun champ");
        // Controle n'est plus une ligne de la liste (19/11/2026, conv-2022) : ses
        // champs partent dans le PIED, a cote de « Valider le dossier ».
        Exiger(d.Etapes.Pages.Length == 6 && d.Etapes.Pas.Length == 6
               && d.Etapes.Pages.All(p => p.Code != "CONTROLE") && d.Etapes.Pages[^1].Code == "DIRIGEANTS",
            "la liste montre les 6 etapes de saisie, sans le controle");
        Exiger(d.Etapes.Controle.Select(c => c.Colonne).SequenceEqual(new[] { "OBSRC_TEXTE_MENTION", "DSSRC_ETAT_DOSSIER" })
               && d.Etapes.Controle[1] is { Obligatoire: true, Valeur: "A controler", Manque: false,
                                            Refus: "a passer a « Conforme » pour valider le dossier" },
            "le pied porte les champs du controle : la mention et l'etat du dossier, refuse tant qu'il n'est pas « Conforme »");
        Exiger(d.Apercu?.Apercu is { Libelle: "Statuts signes" } && d.Apercu.Apercu.Feuilles.Length > 0,
            "la piece s'affiche a droite, page par page");

        // --- 4. Passer les etapes (accordeon, l'en-tete en bas).
        Agir("allerEtape", 1);
        vue = plan.Vue();
        d = vue.Dossier!;
        Exiger(d.Etapes!.Courante == 1 && d.Etapes.Pages[1].Ouverte && d.Etapes.Pages[2].Ouverte
               && vue.Defilement is { Index: 1, Doux: true },
            "aller a l'etape 2 la deplie sans replier les autres, et la page la fait defiler en douceur");
        Agir("basculerEtape", 4);
        vue = plan.Vue();
        Exiger(vue.Dossier!.Etapes! is { Courante: 4 } e4 && e4.Pages[4].Ouverte && e4.Pages[4].Champs.Length > 0
               && vue.Defilement is { Index: 4, Doux: true },
            "un clic sur la ligne d'une etape repliee la deplie, en fait la courante et l'amene en vue");
        Agir("basculerEtape", 4);
        Exiger(plan.Vue().Dossier!.Etapes!.Pages[4] is { Ouverte: false, Champs.Length: 0 },
            "un second clic sur la ligne la replie");
        Agir("etapeVisible", 3);
        Exiger(plan.Vue().Dossier!.Etapes!.Courante == 3, "la page dit quelle etape est en haut : le pied suit (« etape 4 / 6 »)");
        Agir("allerEtape", 99);
        Exiger(plan.Vue().Dossier!.Etapes!.Courante == 5, "aller au-dela de la derniere etape s'arrete a la derniere de la liste");
        Agir("allerEtape", 1);
        // Aller au controle (son index du modele, 6) : rien ne se deplie ni ne
        // defile, ses champs sont deja dans le pied.
        var numeroDefilement = plan.Vue().Defilement?.Numero;
        Agir("allerEtape", 6);
        Agir("basculerEtape", 6);
        Exiger(plan.Vue().Dossier!.Etapes! is { Courante: 1, Pages.Length: 6 }
               && plan.Vue().Defilement?.Numero == numeroDefilement,
            "aller au controle ne change ni l'etape courante ni le defilement");
        // Une piece rangee au controle (inscription), ouverte depuis l'historique
        // des pieces : elle s'affiche, la liste ne bouge pas.
        var inscriptions = plan.Vue().Dossier!.Historique.FirstOrDefault(p => p.Libelle == "Etat des inscriptions");
        Exiger(inscriptions is not null, "la piece « Etat des inscriptions » est dans l'historique des pieces");
        Agir("historiqueOuvrir", inscriptions!.Id);
        Exiger(plan.Vue().Dossier! is { Etapes.Courante: 1, Apercu.Apercu.Libelle: "Etat des inscriptions" },
            "ouvrir une piece du controle l'affiche sans changer l'etape courante");
        // Le pied ecrit dans l'etape CONTROLE : vider l'etat du dossier bloque, la
        // mention fait paraitre sa date et sa diffusion.
        Agir("ecrire", new { etape = "CONTROLE", colonne = "DSSRC_ETAT_DOSSIER", valeur = "" });
        Exiger(plan.Vue().Dossier!.Etapes! is { } ec && ec.Controle[^1] is { Colonne: "DSSRC_ETAT_DOSSIER", Manque: true }
               && ec.Recap.Groupes.Any(g => g.IndexEtape == 6 && g.Points.Any(x => x.Colonne == "DSSRC_ETAT_DOSSIER")),
            "un etat du dossier vide est a renseigner, dans le pied et dans le controle final");
        Agir("ecrire", new { etape = "CONTROLE", colonne = "DSSRC_ETAT_DOSSIER", valeur = "A controler" });
        Agir("ecrire", new { etape = "CONTROLE", colonne = "OBSRC_TEXTE_MENTION", valeur = "Radiation d'office" });
        Exiger(plan.Vue().Dossier!.Etapes!.Controle.Select(c => c.Colonne)
                   .SequenceEqual(new[] { "OBSRC_TEXTE_MENTION", "MNTOB_DATE_MENTION", "MNTOB_DIFFUSABLE", "DSSRC_ETAT_DOSSIER" }),
            "une mention redigee fait paraitre sa date et sa diffusion dans le pied");
        Agir("ecrire", new { etape = "CONTROLE", colonne = "OBSRC_TEXTE_MENTION", valeur = "" });
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
        var recap = plan.Vue().Dossier!.Etapes!.Recap;
        Exiger(recap is { Valide: false } && recap.Nombre > 0 && recap.Titre.Contains("bloque")
               && recap.Groupes[0] is { IndexEtape: 1, Etape: "Identite" },
            "le pied dit ce qui bloque la validation, et ou (Identite)");
        Agir("valider");
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: false },
            "un dossier qui a un defaut ne se valide pas, meme si le geste arrive");
        Agir("allerEtape", 1);
        Agir("saisie", new { etape = "IDENTITE", colonne = "DSSRC_SIREN", valeur = "109 678 235" });
        Exiger(Siren() is { Refus: null, Valeur: "109 678 235" }, "la frappe porte la valeur en direct");
        Agir("ecrire", new { etape = "IDENTITE", colonne = "DSSRC_SIREN", valeur = "109 678 235" });
        Exiger(plan.Vue().Dossier!.Bandeau is { } bc && bc.Liens.All(l => l.Etape != "Identite"),
            "la saisie corrigee retire Identite du bandeau des defauts");

        // --- 5 bis. L'etat du dossier : seul « Conforme » se valide (19/11/2026, conv-2022).
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: false, Nombre: 1, Refusees: 1 } rc
               && rc.Groupes.Single() is { IndexEtape: 6, Etape: "Controle" } gc
               && gc.Points.Single() is { Colonne: "DSSRC_ETAT_DOSSIER", Manquant: false }
               && plan.Vue().Dossier!.Bandeau!.Liens.Single().Etape == "Controle",
            "un dossier « A controler » bloque encore la validation, et le dit (pied, bandeau)");
        Agir("valider");
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: false },
            "« Valider » est refuse tant que l'etat n'est pas « Conforme », meme si le geste arrive");
        Agir("ecrire", new { etape = "CONTROLE", colonne = "DSSRC_ETAT_DOSSIER", valeur = "En anomalie" });
        Exiger(plan.Vue().Dossier!.Etapes!.Controle[^1] is { Refus: not null },
            "un dossier « En anomalie » ne se valide pas non plus");
        Agir("ecrire", new { etape = "CONTROLE", colonne = "DSSRC_ETAT_DOSSIER", valeur = "Conforme" });
        Exiger(plan.Vue().Dossier!.Etapes!.Controle[^1] is { Refus: null } && plan.Vue().Dossier!.Bandeau is null,
            "« Conforme » leve le refus et le bandeau des defauts");

        // --- 6. Valider le dossier, le rouvrir.
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: false, Nombre: 0 },
            "plus rien ne bloque la validation");
        Agir("valider");
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: true }, "le dossier est valide");
        Agir("rouvrir");
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: false, Nombre: 0 },
            "« Rouvrir la saisie » retire la validation");
        Agir("valider");
        // Une seule source de verite : l'ecran des demandes recoit les dossiers valides.
        var attendus = JeuDemo.Demandes.Select(x => x.Dossier).Distinct().Where(x => x == "004176").ToArray();
        Agir("ouvrirCode", "PROC_DEMANDE");
        Exiger(plan.Vue().Proc is { Code: "PROC_DEMANDE" } pd && attendus.All(pd.Ecran.Valides.Contains)
               && pd.Ecran.Valides.All(v => v == "004176"),
            "l'ecran des demandes recoit la liste des dossiers valides");
        // L'ecran n'a plus de bouton « Fermer » (conv-2010) : c'est la croix de son onglet.
        // L'affaire a SON onglet (demande du 19/11/2026), au nom du dossier : ouvrir l'ecran
        // l'a quittee sans la fermer ; fermer l'ecran rend la place a l'onglet voisin, le sien.
        Exiger(plan.Vue().Dossier is null && plan.Vue().Onglets is { Length: 2 } oa
               && oa[0] is { Code: "AFFAIRE:004176", Libelle: "Modification - SCI DU PARC", Actif: false }
               && oa[1] is { Code: "PROC_DEMANDE", Actif: true },
            "l'affaire ouverte a son onglet, au nom du dossier, et un ecran ouvert la quitte sans le fermer");
        Agir("ongletFermer", "PROC_DEMANDE");
        Exiger(plan.Vue().Proc is null && plan.Vue().Dossier?.Numero == "004176"
               && plan.Vue().Onglets is { Length: 1 } ob && ob[0] is { Code: "AFFAIRE:004176", Actif: true },
            "la croix de l'onglet ferme l'ecran ; l'onglet voisin, celui de l'affaire, la remontre");
        Exiger(plan.Vue().Dossier!.Etapes!.Recap is { Valide: true },
            "revenir sur l'onglet de l'affaire la retrouve telle qu'on l'a laissee (sa saisie validee)");

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
               && vue.Proc is { Code: "PROC_ENCAISS" },
            "deux ecrans ouverts prennent deux onglets, le dernier est montre");
        Agir("ongletDebut", new { code = "PROC_ENCAISS", x = 150.0, y = 20.0,
                                  mesure = new { page = new[] { 1500.0, 900.0, 40.0 }, cases = new[] { new[] { 10.0, 100.0 }, new[] { 110.0, 200.0 } } } });
        Agir("ongletGlisse", new { x = 40.0, y = 20.0 });
        Agir("ongletFin", new { x = 40.0, y = 20.0 });
        Exiger(plan.Vue().Onglets!.Select(x => x.Code).SequenceEqual(new[] { "PROC_ENCAISS", "PROC_FACTURE" }),
            "glisser un onglet le reordonne");
        Agir("ongletActiver", "PROC_FACTURE");
        Exiger(plan.Vue().Proc?.Code == "PROC_FACTURE", "un clic sur un onglet montre son ecran");
        Agir("echap");
        Exiger(plan.Vue().Onglets is { Length: 1 } && plan.Vue().Proc?.Code == "PROC_ENCAISS",
            "Echap ferme l'ecran montre (son onglet) ; l'onglet voisin prend la place");
        // La maison, premier onglet de la barre : retour a l'accueil, onglets gardes.
        Agir("accueil");
        vue = plan.Vue();
        Exiger(vue.Proc is null && vue.Dossier is null && vue.Accueil is not null
               && vue.Onglets is { Length: 1 } om && !om[0].Actif,
            "la maison ramene a l'accueil sans fermer les onglets");
        Agir("ongletActiver", "PROC_ENCAISS");
        Exiger(plan.Vue().Proc?.Code == "PROC_ENCAISS", "un clic sur l'onglet y revient");
        Agir("ouvrirNumero", "004176");
        vue = plan.Vue();
        Exiger(vue.Dossier is not null && vue.Proc is null
               && vue.Onglets is { Length: 2 } oc && oc[1] is { Code: "AFFAIRE:004176", Actif: true } && !oc[0].Actif,
            "une affaire ouverte recouvre l'accueil et prend l'onglet actif");
        Agir("ongletActiver", "PROC_ENCAISS");
        Exiger(plan.Vue().Proc?.Code == "PROC_ENCAISS" && plan.Vue().Dossier is null,
            "un clic sur l'onglet d'un ecran quitte l'affaire sans la fermer (l'ecran seul est montre)");
        Agir("ongletDebut", new { code = "AFFAIRE:004176", x = 150.0, y = 20.0,
                                  mesure = new { page = new[] { 1500.0, 900.0, 40.0 }, cases = new[] { new[] { 10.0, 100.0 }, new[] { 110.0, 200.0 } } } });
        Agir("ongletFin", new { x = 150.0, y = 20.0 });
        Exiger(plan.Vue().Dossier?.Numero == "004176" && plan.Vue().Proc is null,
            "un clic sur l'onglet de l'affaire la remontre");
        Agir("ouvrirNumero", "004176");
        Exiger(plan.Vue().Onglets is { Length: 2 }, "rouvrir l'affaire reprend son onglet, sans doublon");
        Agir("accueil");
        vue = plan.Vue();
        Exiger(vue.Proc is null && vue.Dossier is null && vue.Onglets is { Length: 2 } od && od.All(x => !x.Actif),
            "depuis une affaire, la maison ramene a l'accueil ; les onglets restent, le sien compris");
        Agir("ongletActiver", "AFFAIRE:004176");
        Exiger(plan.Vue().Dossier?.Numero == "004176", "l'onglet de l'affaire la rouvre depuis l'accueil");
        Agir("fermerDossier");
        vue = plan.Vue();
        Exiger(vue.Dossier is null && vue.Proc?.Code == "PROC_ENCAISS" && vue.Onglets is { Length: 1 },
            "la croix de l'affaire ferme son onglet ; l'onglet voisin prend la place");
        Agir("ongletFermer", "PROC_ENCAISS");
        Exiger(plan.Vue().Onglets is null, "fermer le dernier onglet retire la barre d'onglets");
        Agir("ouvrirNumero", "004176");
        Agir("ouvrirCode", "PROC_FACTURE");
        Agir("echap");
        Exiger(plan.Vue().Proc is null, "Echap ferme l'ecran ouvert...");
        Agir("ouvrirNumero", "004176");
        Agir("echap");
        Exiger(plan.Vue().Dossier is null, "... et l'affaire ouverte");

        // --- 10 bis. L'onglet d'une affaire SORTI dans sa propre fenetre (demande du
        // 19/11/2026). La fenetre WPF elle-meme est prouvee sur le bureau cache ; ici, la
        // session qu'elle monte : ouverte sur l'affaire de son onglet, sans barre ni accueil
        // ni fleche « Retour » ; sa croix et Echap demandent la fermeture de la fenetre et
        // disent a la principale de fermer l'onglet (AffaireFermee).
        var sortie = new SessionPlanTravail(null, null, modeFenetreVue: true, vueInitiale: "AFFAIRE:004176");
        sortie.Initialiser();
        var fermetures = 0;
        sortie.FermerFenetre += () => fermetures++;
        vue = sortie.Vue();
        Exiger(vue.Dossier is { Numero: "004176", Sortie: true } && vue.Onglets is null && vue.Accueil is null && !vue.Barre,
            "la fenetre d'un onglet d'affaire sorti s'ouvre sur cette affaire, seule, sans fleche « Retour »");
        Agir("ouvrirNumero", "004176");
        Exiger(plan.Vue().Dossier is { Sortie: false }, "dans la fenetre principale, l'affaire garde sa fleche « Retour »");
        Agir("fermerDossier");
        Exiger(!sortie.AffaireFermee && fermetures == 0, "rien n'est ferme tant qu'on ne touche pas la fenetre sortie");
        sortie.Agir("echap", Valeur(null));
        Exiger(sortie.AffaireFermee && fermetures == 1,
            "Echap, dans la fenetre sortie, ferme l'affaire : la fenetre doit partir, son onglet avec");
        sortie.Agir("fermerDossier", Valeur(null));
        Exiger(fermetures == 2, "la croix de l'affaire, dans la fenetre sortie, fait de meme");

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
        // Sortie dans sa fenetre, elle s'y montre comme dans son onglet : sur sa tache.
        var sortieTache = new SessionPlanTravail(null, null, modeFenetreVue: true,
                                                 vueInitiale: "AFFAIRE:" + deno.Tache!.Value.Dossier);
        sortieTache.Initialiser();
        Exiger(sortieTache.Vue().Dossier is { Tache.Code: "DENO", Sortie: true },
            "l'onglet d'une affaire a tache, sorti dans sa fenetre, y rouvre sa tache (pas le dossier entier)");
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

        // --- 14. Une tache CLOSE ne se rouvre plus (TacheEnAttente lit TachesOuvertes,
        // filtree comme la tuile « Mes taches du jour »). En dernier : la cloture reste
        // dans l'etat du banc.
        var etatClos = AccueilEtat.Charger();
        etatClos.CloreTache("DENO", deno.Tache!.Value.Dossier, "banc");
        var apresCloture = new SessionPlanTravail(null, null, modeFenetreVue: true,
                                                  vueInitiale: "AFFAIRE:" + deno.Tache!.Value.Dossier);
        apresCloture.Initialiser();
        Exiger(apresCloture.Vue().Dossier is { Tache: null } dc && dc.Numero == deno.Tache!.Value.Dossier,
            "une tache close ne se rouvre plus : l'affaire s'ouvre entiere");

        plan.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
