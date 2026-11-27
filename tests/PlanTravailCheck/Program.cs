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

    /// <summary>
    /// La valeur que la feuille donne a cette <paramref name="propriete"/> de ce selecteur pour
    /// une fenetre de <paramref name="largeur"/> pixels : celle de la DERNIERE regle, dans l'ordre
    /// du fichier, qui le nomme tel quel et la pose, hors des @media qui ne s'appliquent pas
    /// (seuls « (max-width: N) » avec N >= largeur comptent). Une cascade reduite aux regles
    /// d'egale force et aux proprietes ecrites telles quelles (« overflow » n'est pas lu dans
    /// « overflow-y »), ce qui suffit pour les cartes du corps du dossier.
    /// fix-ok: a 1100 px, la regle des cartes (« min-height: 0 », rig-accueil.css ~4398) venait
    /// apres la mise en page etroite (~930) et l'annulait -- mesure sur capture, arbre ~110 px.
    /// </summary>
    private static string? ProprieteA(string css, string selecteur, string propriete, int largeur)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        string? valeur = null;
        void Lire(string bloc, bool applique)
        {
            var i = 0;
            while (i < bloc.Length)
            {
                var ouvre = bloc.IndexOf('{', i);
                if (ouvre < 0) return;
                var tete = Regex.Replace(bloc[i..ouvre].Trim(), @"\s+", " ");
                var (fin, profondeur) = (ouvre + 1, 1);
                while (fin < bloc.Length && profondeur > 0)
                    profondeur += bloc[fin++] switch { '{' => 1, '}' => -1, _ => 0 };
                var corps = bloc[(ouvre + 1)..(fin - 1)];
                if (tete.StartsWith("@media", StringComparison.Ordinal))
                {
                    var m = Regex.Match(tete, @"^@media \(max-width: (\d+)px\)$");
                    Lire(corps, applique && m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) >= largeur);
                }
                else if (applique && !tete.StartsWith('@') && tete.Split(',').Select(s => s.Trim()).Contains(selecteur)
                         && Regex.Match(corps, @"(?:^|[;\s])" + Regex.Escape(propriete) + @":\s*([^;]+);") is { Success: true } lu)
                    valeur = lu.Groups[1].Value.Trim();
                i = fin;
            }
        }
        Lire(css, applique: true);
        return valeur;
    }

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

        // --- 0 ter. LE CATALOGUE DE LA BARRE D'ACTIONS DU DOSSIER (Metier/ActionsDossier.cs, plan
        // « Vue du dossier = coeur des actions », lot 1, 20/11/2026) : un ecran n'y entre que s'il
        // est REELLEMENT monte par l'aiguillage -- un code qui tomberait sur « ecran a venir »
        // (EcranNonMaquette, le cas par defaut) ferait une action qui n'agit pas.
        bool Monte(string code) => Regex.IsMatch(codeAiguillage, $@"case '{Regex.Escape(code)}':\s*return\s*<(?!EcranNonMaquette\b)\w+");
        var catalogue = ActionsDossier.Catalogue;
        var ecransDesActions = catalogue.Where(a => a.Ecran is not null).Select(a => a.Ecran!).ToList();
        var nonMontes = ecransDesActions.Where(c => !Monte(c)).ToList();
        Exiger(ecransDesActions.Count >= 10 && nonMontes.Count == 0,
            $"barre du dossier : ses {ecransDesActions.Count} ecrans sont tous montes par l'aiguillage ({string.Join(", ", nonMontes)})");
        Exiger(Monte("PROC_FACTURE") && !Monte("PROC_PARAM_LISTEEXCEL") && !Monte("PROC_STAT_INCONNU"),
            "jumeau : le controle reconnait un ecran qui tomberait sur « ecran a venir » (PROC_PARAM_LISTEEXCEL)");
        Exiger(catalogue.Select(a => a.Code).Distinct().Count() == catalogue.Length
               && catalogue.All(a => ActionsDossier.Verbes.Contains(a.Verbe) && char.IsAsciiLetterUpper(a.Touche)
                                     && a.Types.Length > 0 && a.Types.All(t => t is DomaineGed.Rcs or DomaineGed.Judiciaire)
                                     && !a.Code.Contains("PROC_") && a.Libelle.Length > 0 && a.Detail.Length > 0)
               && ActionsDossier.Verbes.All(v => catalogue.Any(a => a.Verbe == v)),
            "barre du dossier : codes neutres et uniques, chaque action sous un des cinq verbes, pour le RCS ou le judiciaire, une lettre");
        // Lot 9 : « Ecrire › Courriers divers » ouvrait le CATALOGUE des modeles, un ecran de
        // parametrage (constate par le chef le 20/11/2026 depuis 004182). Il reste a l'accueil.
        Exiger(catalogue.All(a => a.Ecran != "PROC_COURRIER_DIVERS") && Monte("PROC_COURRIER_DIVERS"),
            "aucune action du dossier n'ouvre le CATALOGUE des courriers divers (ecran de parametrage, toujours monte pour l'accueil)");
        var ecranCatalogue = File.ReadAllText(Path.Combine(racine, "web/src/ecrans/EcranCourriersDivers.tsx"));
        var catalogueCourriers = EcranCourriersDivers.Catalogue();
        Exiger(ecranCatalogue.Contains("appeler<CatalogueCourriersVue>('EcranCourriersDivers.Catalogue')")
               && CourriersDivers.Modeles.All(m => !ecranCatalogue.Contains(m.ParamEdition))
               && catalogueCourriers.Courriers.Select(c => c.Code).SequenceEqual(CourriersDivers.Modeles.Select(m => m.Code))
               && catalogueCourriers.Courriers.Count(c => c.Actif) == 4
               && catalogueCourriers.Types.SequenceEqual(CourriersDivers.TypesEntite),
            "une seule liste des modeles : l'ecran du catalogue la lit par son pont, il n'en garde aucune copie");
        // Le courrier produit sorti dans une fenetre detachee (elle s'ouvrirait a l'ecran ici) : ses
        // valeurs viennent de ValeursDocument, qui ecarte le courrier par la meme regle que l'apercu.
        var sourcePlan = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
        Exiger(Regex.IsMatch(sourcePlan, @"ValeursDocument\(NoeudGed n\)\s*\{\s*(//[^\n]*\s*)*if \(HorsSaisie\(n\)\) return \[\];")
               && Regex.Matches(sourcePlan, @"ValeursDocument\(ApercuNoeud\)").Count == 2,
            "le courrier sorti dans une fenetre n'y porte aucune valeur de formulaire (DetacherDocument, PublierDocument : ValeursDocument l'ecarte)");
        foreach (var type in new[] { DomaineGed.Rcs, DomaineGed.Judiciaire })
        {
            var lettres = ActionsDossier.Pour(type).Select(a => a.Touche).ToList();
            Exiger(lettres.Distinct().Count() == lettres.Count,
                $"barre du dossier : une lettre par action sur une affaire {type} ({new string(lettres.ToArray())})");
        }

        // Bac a sable : JAMAIS l'etat reel de l'utilisateur.
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatPont = Path.Combine(Path.GetTempPath(), "rigv3-pont-" + Guid.NewGuid().ToString("N") + ".json");
        var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-plantravail-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Le pont ouvre 004176 : son onglet est RETENU et revient au demarrage suivant. Chaque
            // partie a donc son etat -- Jouer part d'une page neuve.
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatPont);
            JouerPont(racine);
            // REDEMARRAGE : l'onglet de l'affaire ouverte revient. La session l'ecrit en ouvrant
            // l'affaire (MemoriserOnglets) PUIS consulte l'affaire sur l'etat lu a son demarrage :
            // cet etat reecrivait la barre vide, et l'app redemarrait sur l'accueil seul (parcours
            // du lot 6, 20/11/2026 : 004176 et 004151 ouverts, aucun onglet au retour).
            var redemarree = new SessionPlanTravail(null, null);
            redemarree.Initialiser();
            Exiger(redemarree.Vue().Onglets?.Any(o => o.Code == "AFFAIRE:004176") == true,
                $"redemarrage : l'onglet de l'affaire ouverte (004176) revient dans la barre (onglets : {string.Join(", ", redemarree.Vue().Onglets?.Select(o => o.Code) ?? [])})");
            redemarree.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
            Jouer();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatPont); } catch (IOException) { }
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
               == EcranVerrous.Exemple("R. VILAIN", false).Lignes.Length,
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
        // Le jeu ne connait pas le capital de SCI DU PARC : la fiche ne prend plus celui d'AMIENS
        // BILLARDS (lot 7, 20/11/2026), le montant est a saisir -- le redacteur le saisit.
        Exiger(plan.Vue().Dossier!.Etapes!.Recap.Groupes.Any(g => g.Points.Any(x => x is { Colonne: "CPTL_MONTANT", Manquant: true })),
            "le capital de 004176, inconnu du jeu, est a saisir");
        Agir("ecrire", new { etape = "CAPITAL", colonne = "CPTL_MONTANT", valeur = "1500" });

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
        JouerActionsDossier();
        JouerEnCoursDossier();
        JouerIdentiteDeLAffaire();
        JouerParcoursDuDossier();
        JouerOeilDuDocument();
    }

    /// <summary>
    /// 19. L'OEIL DU PIED DES ETAPES rouvre LE DOCUMENT, pas une colonne vide (capture de
    /// l'utilisateur, 22/11/2026 : « le document s'ouvre pas mais le panel oui »). La croix du
    /// document oubliait la piece ; l'oeil, offert des que la colonne etait fermee, la rouvrait
    /// sans rien dedans -- ni piece, ni croix pour la refermer. Etat a part, comme 17 et 18.
    /// </summary>
    private static void JouerOeilDuDocument()
    {
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatIci = Path.Combine(Path.GetTempPath(), "rigv3-oeil-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatIci);
        try
        {
            var s = new SessionPlanTravail(null, null);
            s.Initialiser();
            void Faire(string geste, object? valeur = null) => s.Agir(geste, Valeur(valeur));
            DossierVue D() => s.Vue().Dossier!;

            Faire("ouvrirNumero", "004176");
            Faire("historiqueOuvrir", D().Historique.Single(p => p.Libelle == "Statuts signes").Id);
            Exiger(D() is { Etapes.Oeil: false, BoutonDocument: false, Apercu.Apercu.Libelle: "Statuts signes" },
                "precondition : les statuts a droite, les etapes au milieu ; rien a reafficher, ni oeil ni « Document »");

            Faire("fermerApercu");
            Exiger(D() is { Apercu: null, Etapes.Oeil: true, BoutonDocument: true },
                "la croix ferme la colonne du document : l'oeil des etapes et « Document » proposent de la rouvrir");
            Faire("afficherDocument");
            Exiger(D() is { Apercu.Apercu.Libelle: "Statuts signes", Etapes.Oeil: false, BoutonDocument: false },
                "l'oeil rouvre la colonne SUR la piece fermee (Statuts signes), pas vide");

            // Jumeau : la piece fermee reste a SON affaire. Sans Kbis, 2026J00087 n'a rien a montrer :
            // ni oeil ni « Document », et le geste ne rouvre pas les statuts de 004176 a cote d'elle.
            Faire("fermerApercu");
            Faire("ouvrirNumero", "2026J00087");
            Exiger(D() is { Numero: "2026J00087", Apercu: null, BoutonDocument: false } d && d.Etapes is null or { Oeil: false },
                "jumeau : une affaire sans document n'offre ni l'oeil ni « Document » (ils ouvriraient une colonne vide)");
            Faire("afficherDocument");
            Exiger(D().Apercu is null,
                "jumeau : le geste n'y ouvre rien -- ni colonne vide, ni la piece d'une autre affaire");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatIci); } catch (IOException) { }
            try { Directory.Delete(PiecesJointes.DossierPour(etatIci), recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// 17. LE KBIS ET LA FICHE D'UNE AFFAIRE SONT LES SIENS (plan « Vue du dossier = coeur des
    /// actions », lot 7, 20/11/2026). Constate par le chef sur main e34fea0 : « Kbis a jour » de
    /// 004151 (TRANSNORD SA) et de 004176 (SCI DU PARC) montrait AMIENS BILLARDS, SIREN 402 118 447
    /// refuse (cle de controle), gestion 2026B01067, 12 rue des Trois Cailloux : une fiche neuve
    /// prenait les valeurs de la societe de demonstration, quelle que soit l'affaire. Une fiche
    /// neuve part de SON affaire ; une valeur deja enregistree qui n'est que l'ancien defaut
    /// d'AMIENS BILLARDS est reprise ; une vraie saisie, elle, ecrase toujours.
    /// Etat a part : les sections precedentes ont saisi dans 004176.
    /// </summary>
    private static void JouerIdentiteDeLAffaire()
    {
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatIci = Path.Combine(Path.GetTempPath(), "rigv3-identite-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatIci);
        try
        {
            // Ce qu'un poste a deja enregistre (constate dans accueil.json le 20/11/2026, onze
            // affaires) : les anciens defauts d'AMIENS BILLARDS dans 004176, et dans 004168 une
            // VRAIE saisie de l'utilisateur, qui doit rester.
            var seme = AccueilEtat.Charger();
            seme.Saisies["004176"] = new()
            {
                ["DEMANDE"] = [new() { ["DMND_NUM_DEMANDE"] = "004188", ["DMND_NUM_INDICATIF"] = "2026B01067",
                                       ["DMND_LIBELLE_ECRAN"] = "Modification - NORD MARINE SAS", ["DMND_NOM"] = "DUPONT Jean" }],
                ["IDENTITE"] = [new() { ["DSSRC_SIREN"] = "402 118 447", ["INSPM_DENOMINATION_STATUT"] = "AMIENS BILLARDS",
                                        ["DSSRC_NUM_GESTION"] = "2026B01067", ["DSSRC_TYPE_RCS"] = "Personne morale",
                                        ["DSSRC_DATE_IMMAT"] = JeuDemo.Aujourdhui.ToString("yyyy-MM-dd") }],
                ["SIEGE"] = [new() { ["ETBLS_ID_ADRBS"] = "12 rue des Trois Cailloux, 80000 AMIENS" }],
                ["ACTIVITE"] = [new() { ["ACTVT_ACTIVITE_DECLAREE"] = "Commerce de detail" }],
                ["CAPITAL"] = [new() { ["CPTL_MONTANT"] = "10000", ["CPTL_MONNAIE"] = "EUR",
                                       ["CPTL_TEXTE_CAPITAL"] = "Capital de 10 000 EUR divise en 1 000 actions de 10 EUR" }],
            };
            seme.Saisies["004168"] = new()
            {
                ["IDENTITE"] = [new() { ["DSSRC_SIREN"] = "552 100 554", ["INSPM_DENOMINATION_STATUT"] = "BATIPRO CONSTRUCTION" }],
            };
            // La forme juridique se change par une tache (« Modification de la forme juridique »,
            // RecetteTache.FormeJuridique) : la trace des modifications fait foi pour le bandeau.
            seme.Modifications["004168"] =
            [
                new(DateTime.Now, "Raphael", "INSCRIT_PM", "Forme juridique", "INSCRIT_PM.FORME_JURIDIQUE_UTI",
                    "Societe par actions simplifiee", "Societe anonyme"),
            ];
            seme.Enregistrer();

            var s = new SessionPlanTravail(null, null);
            s.Initialiser();
            void Faire(string geste, object? valeur = null) => s.Agir(geste, Valeur(valeur));
            foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);
            static string LigneKbis(VueKbis k, string titre)
                => k.Sections.SelectMany(x => x.Lignes).FirstOrDefault(l => l.Titre == titre)?.Libelle ?? "";
            static string TexteKbis(VueKbis k)
                => k.Gestion + " | " + string.Join(" | ", k.Sections.SelectMany(x => x.Lignes).Select(l => l.Libelle));
            string[] amiens = ["AMIENS BILLARDS", "402 118 447", "2026B01067", "Trois Cailloux", "DUPONT Jean", "Commerce de detail"];
            bool Montre(string texte, string[] traces) => traces.Any(t => texte.Contains(t, StringComparison.OrdinalIgnoreCase));
            bool SirenValide(string siren) => ValiditeSaisie.LuhnValide(siren.Replace(" ", ""));
            // La forme juridique que la puce du bandeau montre pour cette affaire (fiche sommaire).
            static string FormeBandeau(DossierVue x)
                => x.Sommaire.Champs.FirstOrDefault(c => c.Libelle == "Forme juridique")?.Valeur ?? "";

            // --- 004151, TRANSNORD SA : « Delivrer › Kbis a jour ».
            Faire("ouvrirNumero", "004151");
            Faire("actionDossier", "kbis");
            var d = s.Vue().Dossier!;
            var k = d.Apercu?.Apercu?.Kbis;
            Exiger(k is not null && LigneKbis(k, "Denomination ou raison sociale") == "TRANSNORD SA"
                   && LigneKbis(k, "Immatriculation au RCS, numero").StartsWith(d.Siren + " R.C.S. ", StringComparison.Ordinal)
                   && k.Gestion == "004151" && LigneKbis(k, "Adresse du siege") == "ZI Nord, 80440 BOVES",
                $"004151 : le Kbis est celui de TRANSNORD SA, au SIREN du bandeau, gestion 004151, siege a BOVES (lu : {(k is null ? "aucun Kbis" : TexteKbis(k))})");
            Exiger(SirenValide(d.Siren) && !(d.Bandeau?.Liens.Any(l => l.Etape == "Identite" && l.Refuse) ?? false),
                $"004151 : le SIREN du bandeau ({d.Siren}) passe la cle de Luhn, la fiche Identite n'est pas refusee");
            Exiger(k is not null && !Montre(TexteKbis(k), amiens), "004151 : rien d'AMIENS BILLARDS sur le Kbis");
            // Le capital du jeu (10 000 EUR, « 1 000 actions de 10 EUR ») est celui d'AMIENS BILLARDS
            // (Metier/ArbreGed.cs : son Kbis, son BODACC « SARL au capital de 10 000 EUR ») ; le jeu ne
            // connait pas celui de TRANSNORD SA : la ligne reste vide.
            Exiger(k is not null && LigneKbis(k, "Capital social") == "",
                $"004151 : capital inconnu de l'affaire, pas celui d'AMIENS BILLARDS (lu : « {(k is null ? "aucun Kbis" : LigneKbis(k, "Capital social"))} »)");
            // 004151 n'a pas de fiche sommaire a elle, mais sa SOCIETE en a une : J-00931, l'autre
            // affaire de TRANSNORD SA, meme SIREN 318 552 007 (Societe anonyme, immatriculee le
            // 18/02/2008). Ce n'est pas « emprunter a une autre societe » : c'est la meme. Le Kbis
            // les imprimait vides, et la puce du bandeau ne montrait pas la forme.
            // fix-ok: SommaireDu/ValeursDeLAffaire ne lisaient que SommairesDemo[numero], absent pour 004151 --
            // mesure : PlanTravailCheck rouge sur 4db0ca7, Kbis de 004151 lu forme « » et date d'immatriculation « ».
            Exiger(k is not null && LigneKbis(k, "Forme juridique") == "Societe anonyme" && FormeBandeau(d) == "Societe anonyme",
                $"004151 : la forme juridique est celle de TRANSNORD SA (fiche sommaire de J-00931, meme SIREN), au Kbis comme au bandeau, jamais le type de dossier (lu : Kbis « {(k is null ? "aucun Kbis" : LigneKbis(k, "Forme juridique"))} », bandeau « {FormeBandeau(d)} »)");
            Exiger(k is not null && LigneKbis(k, "Date d'immatriculation") == "18/02/2008",
                $"004151 : la date d'immatriculation est celle de TRANSNORD SA (J-00931, meme SIREN), pas la date du jour (lu : « {(k is null ? "aucun Kbis" : LigneKbis(k, "Date d'immatriculation"))} »)");
            // Seule l'identite de la societe passe d'une affaire a l'autre : le siege de 004151 reste
            // celui que le jeu range sous 004151 (BOVES, controle plus haut), pas « 41 Route de Paris »
            // de J-00931, et la mention de la procedure de J-00931 (redressement judiciaire) n'est pas la sienne.
            Exiger(d.Sommaire.Mention == "" && !d.Sommaire.Champs.Any(c => c.Libelle == "Adresse" && c.Valeur.Contains("Route de Paris")),
                $"004151 : ni l'adresse ni la mention de J-00931 ne passent a 004151 (lu : mention « {d.Sommaire.Mention} »)");

            // --- Jumeau : 004144 (LES TROIS CHENES) n'a de fiche sommaire ni a elle ni a sa societe.
            // Forme et date restent VIDES : ni la date du jour, ni la forme d'une autre societe.
            Faire("ouvrirNumero", "004144");
            Faire("actionDossier", "kbis");
            var d44 = s.Vue().Dossier!;
            var k44 = d44.Apercu?.Apercu?.Kbis;
            Exiger(k44 is not null && LigneKbis(k44, "Denomination ou raison sociale").Contains("TROIS CHENES")
                   && LigneKbis(k44, "Forme juridique") == "" && FormeBandeau(d44) == ""
                   && LigneKbis(k44, "Date d'immatriculation") == "",
                $"jumeau : 004144, dont le jeu ne connait ni la forme ni la date, les laisse vides (lu : {(k44 is null ? "aucun Kbis" : TexteKbis(k44))} ; bandeau « {FormeBandeau(d44)} »)");

            // --- 004176, SCI DU PARC : des anciens defauts d'AMIENS BILLARDS deja enregistres.
            Faire("ouvrirNumero", "004176");
            Faire("actionDossier", "kbis");
            d = s.Vue().Dossier!;
            k = d.Apercu?.Apercu?.Kbis;
            var copieSiren = d.Actions.SelectMany(g => g.Actions).Single(a => a.Code == "copierSiren").Copie;
            Exiger(k is not null && LigneKbis(k, "Denomination ou raison sociale") == "SCI DU PARC" && k.Gestion == "004176"
                   && LigneKbis(k, "Date d'immatriculation") == "12/03/2014" && LigneKbis(k, "Adresse du siege") == "8 Avenue du Parc, 80000 AMIENS"
                   && LigneKbis(k, "Immatriculation au RCS, numero").StartsWith(d.Siren + " R.C.S. ", StringComparison.Ordinal)
                   && copieSiren == d.Siren && SirenValide(d.Siren) && !Montre(TexteKbis(k), amiens),
                $"004176 : le Kbis est celui de SCI DU PARC, au SIREN du bandeau, valide ; l'ancien defaut enregistre est repris (lu : {(k is null ? "aucun Kbis" : TexteKbis(k))})");
            Exiger(!(d.Bandeau?.Liens.Any(l => l.Etape == "Identite" && l.Refuse) ?? false),
                "004176 : l'ancien SIREN 402 118 447 enregistre n'est plus refuse, il a laisse la place a celui de l'affaire");
            // Le Kbis lisait le « Type de dossier » (Personne morale) comme forme juridique, et la
            // puce du bandeau disait « Societe civile immobiliere » pour la meme affaire.
            Exiger(k is not null && LigneKbis(k, "Forme juridique") == "Societe civile immobiliere"
                   && FormeBandeau(d) == "Societe civile immobiliere",
                $"004176 : le Kbis dit la forme juridique du bandeau, pas le type de dossier (lu : Kbis « {(k is null ? "aucun Kbis" : LigneKbis(k, "Forme juridique"))} », bandeau « {FormeBandeau(d)} »)");

            // --- Jumeau : une VRAIE saisie de l'utilisateur ecrase toujours.
            Faire("ouvrirNumero", "004168");
            Faire("actionDossier", "kbis");
            k = s.Vue().Dossier!.Apercu?.Apercu?.Kbis;
            Exiger(k is not null && LigneKbis(k, "Denomination ou raison sociale") == "BATIPRO CONSTRUCTION",
                "jumeau : la denomination saisie par l'utilisateur sur 004168 reste, le defaut de l'affaire ne l'ecrase pas");
            Exiger(k is not null && LigneKbis(k, "Forme juridique") == "Societe anonyme" && FormeBandeau(s.Vue().Dossier!) == "Societe anonyme",
                $"jumeau : la forme changee par une tache sur 004168 passe au Kbis comme au bandeau (lu : « {(k is null ? "aucun Kbis" : LigneKbis(k, "Forme juridique"))} »)");

            // --- Toutes les affaires : aucune fiche ne montre AMIENS BILLARDS hors des siennes,
            // ni la demande 004188 (NORD MARINE SAS) hors de la sienne.
            var numeros = JeuDemo.Demandes.Select(x => x.Dossier).Where(n => n.Length > 0)
                                 .Append("J-00931").Distinct().ToArray();
            foreach (var n in numeros)
            {
                Faire("ouvrirNumero", n);
                if (s.Vue().Dossier!.Actions.SelectMany(g => g.Actions).Any(a => a.Code == "kbis"))
                    Faire("actionDossier", "kbis");
                s.Vue();
            }
            Faire("ouvrirNumero", "004151");   // enregistre ce que la derniere affaire a cree
            var saisies = AccueilEtat.Charger().Saisies;
            string Valeurs(string n) => saisies.TryGetValue(n, out var e)
                ? string.Join(" | ", e.Values.SelectMany(f => f).SelectMany(f => f.Values)) : "";
            string Champ(string n, string etape, string colonne)
                => saisies.TryGetValue(n, out var e) && e.TryGetValue(etape, out var f) && f.Count > 0
                   && f[0].TryGetValue(colonne, out var v) ? v : "";
            var avecFiche = numeros.Where(n => Champ(n, "IDENTITE", "DSSRC_TYPE_RCS").Length > 0).ToArray();
            var fuites = numeros.Where(n => n is not "2026B01067" and not "004182" && Montre(Valeurs(n), amiens)).ToArray();
            var fuitesDemande = numeros.Where(n => n != "004188" && Montre(Valeurs(n), ["NORD MARINE", "004188"])).ToArray();
            Exiger(avecFiche.Length >= 8 && fuites.Length == 0 && fuitesDemande.Length == 0,
                $"aucune fiche ne montre AMIENS BILLARDS hors de 2026B01067/004182, ni la demande 004188 hors de la sienne "
                + $"({avecFiche.Length} affaires lues ; AMIENS : {string.Join(", ", fuites)} ; 004188 : {string.Join(", ", fuitesDemande)})");
            Exiger(Champ("004151", "IDENTITE", "DSSRC_SIREN") == "318 552 007"
                   && Champ("004151", "IDENTITE", "INSPM_DENOMINATION_STATUT") == "TRANSNORD SA"
                   && Champ("004151", "DEMANDE", "DMND_NUM_DEMANDE") == "004151"
                   && Champ("004151", "DEMANDE", "DMND_LIBELLE_ECRAN") == "Depot de bilan - TRANSNORD SA"
                   && Champ("004151", "DEMANDE", "DMND_NOM") == "MARCHAND Paul"
                   && Champ("004151", "ACTIVITE", "ACTVT_ACTIVITE_DECLAREE") == "Transport routier",
                $"la fiche neuve de 004151 part de 004151 : SIREN, denomination, sa demande, son dirigeant, son activite "
                + $"(lu : {Champ("004151", "IDENTITE", "DSSRC_SIREN")} · {Champ("004151", "DEMANDE", "DMND_NUM_DEMANDE")} · {Champ("004151", "DEMANDE", "DMND_NOM")})");
            Exiger(Champ("004182", "IDENTITE", "INSPM_DENOMINATION_STATUT") == "AMIENS BILLARDS"
                   && Champ("004182", "IDENTITE", "DSSRC_SIREN") == "109 678 235"
                   && Champ("004182", "DEMANDE", "DMND_NUM_DEMANDE") == "004182",
                "jumeau : l'affaire d'AMIENS BILLARDS (004182) garde sa societe, au SIREN de son bandeau (cle valide), et sa demande");
            Exiger(Champ("004176", "CAPITAL", "CPTL_MONTANT") == "" && Champ("004176", "CAPITAL", "CPTL_TEXTE_CAPITAL") == ""
                   && Champ("004151", "CAPITAL", "CPTL_MONTANT") == "" && Champ("004151", "CAPITAL", "CPTL_TEXTE_CAPITAL") == "",
                $"le capital d'AMIENS BILLARDS ne part plus dans 004151 (fiche neuve) ni ne reste dans 004176 (ancien defaut enregistre) "
                + $"(lu : 004151 « {Champ("004151", "CAPITAL", "CPTL_MONTANT")} », 004176 « {Champ("004176", "CAPITAL", "CPTL_MONTANT")} »)");
            Exiger(Champ("004168", "IDENTITE", "DSSRC_SIREN") == "552 100 554"
                   && Champ("004176", "IDENTITE", "DSSRC_TYPE_RCS") == "Personne morale"
                   && Champ("004176", "CAPITAL", "CPTL_MONNAIE") == "EUR"
                   && Champ("J-00931", "CAPITAL", "CPTL_MONNAIE") == "EUR",
                "les vraies saisies et les defauts neutres (type de dossier, monnaie) restent");
            // Repris de la copie du run run-26218cd842c9-1 (20/11/2026), perdu quand une version
            // suivante du meme travail est entree dans main sans lui.
            Exiger(Champ("004138", "IDENTITE", "DSSRC_TYPE_RCS").Length > 0
                   && Champ("004138", "IDENTITE", "INSPM_DENOMINATION_STATUT") == "",
                $"une affaire sans societe (004138, arrivee du courrier, sans SIREN) garde sa denomination vide : "
                + $"« lot 2026-36 », la fin de son titre, n'en est pas une (lu : « {Champ("004138", "IDENTITE", "INSPM_DENOMINATION_STATUT")} »)");

            // --- Jumeau de la reprise : elle ne joue qu'UNE fois. L'adresse « 12 rue des Trois
            // Cailloux » (une adresse de la liste) choisie ENSUITE sur 2026B01067 reste au redemarrage.
            Faire("ouvrirNumero", "2026B01067");
            Faire("ecrire", new { etape = "SIEGE", colonne = "ETBLS_ID_ADRBS", valeur = "12 rue des Trois Cailloux, 80000 AMIENS" });
            foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);
            s.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var redemarre = new SessionPlanTravail(null, null);
            redemarre.Initialiser();
            redemarre.Agir("ouvrirNumero", Valeur("2026B01067"));
            redemarre.Agir("actionDossier", Valeur("kbis"));
            k = redemarre.Vue().Dossier!.Apercu?.Apercu?.Kbis;
            Exiger(k is not null && LigneKbis(k, "Adresse du siege") == "12 rue des Trois Cailloux, 80000 AMIENS",
                $"jumeau : une valeur choisie apres la reprise, meme egale a l'ancien defaut, n'est plus reprise (lu : {(k is null ? "aucun Kbis" : LigneKbis(k, "Adresse du siege"))})");
            foreach (var o in redemarre.Vue().Onglets ?? []) redemarre.Agir("ongletFermer", Valeur(o.Code));
            redemarre.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatIci); } catch (IOException) { }
        }
    }

    /// <summary>
    /// 15. LA BARRE D'ACTIONS DU DOSSIER (plan « Vue du dossier = coeur des actions », lot 1,
    /// 20/11/2026), sur une session neuve : les actions locales jouees dans la page de
    /// l'affaire, l'ecran ouvert « pour ce dossier » (son contexte, la puce de retour, sa
    /// fermeture qui ramene a l'affaire), le tri par type d'affaire, par login, et la
    /// fenetre d'un onglet sorti.
    /// </summary>
    private static void JouerActionsDossier()
    {
        var s = new SessionPlanTravail(null, null);
        s.Initialiser();
        void Faire(string geste, object? valeur = null) => s.Agir(geste, Valeur(valeur));
        string[] Codes(VerbeActionsVue[] verbes) => verbes.SelectMany(g => g.Actions).Select(a => a.Code).ToArray();
        string[] EcrireDe(SessionPlanTravail session)
            => session.Vue().Dossier!.Actions.SingleOrDefault(g => g.Verbe == ActionsDossier.Ecrire)?.Actions.Select(a => a.Code).ToArray() ?? [];
        bool Refuse(SessionPlanTravail session, string code)
        {
            try { session.Agir("actionDossier", Valeur(code)); return false; }
            catch (InvalidOperationException) { return true; }
        }
        // Les onglets que l'etat du banc a retenus ne comptent pas ici.
        foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);

        Faire("actionDossier", "factures");
        Exiger(s.Vue() is { Dossier: null, Proc: null, Onglets: null },
            "sans affaire ouverte (fermee entre le clic et son traitement), une action ne fait rien");

        // --- La barre d'une affaire RCS.
        Faire("ouvrirNumero", "004176");
        var v = s.Vue();
        var barre = v.Dossier!.Actions;
        var codes = Codes(barre);
        Exiger(barre.Select(g => g.Verbe).SequenceEqual(ActionsDossier.Verbes),
            "affaire RCS : la barre montre les cinq verbes, dans l'ordre");
        Exiger(new[] { "kbis", "imprimerKbis", "imprimerArborescence", "factures", "demandes", "commandesWeb", "bodacc", "envoyerDossier" }.All(codes.Contains)
               && !codes.Contains("depotsJudiciaires") && !codes.Contains("redaction"),
            "affaire RCS : le Kbis, les ecrans du RCS et ceux de tous ; aucun ecran reserve au judiciaire");
        var actions = barre.SelectMany(g => g.Actions).ToDictionary(a => a.Code);
        Exiger(actions["copierNumero"].Copie == "004176" && actions["copierSiren"].Copie == "812 445 906"
               && actions.Values.Count(a => a.Copie is not null) == 2
               && actions["factures"] is { Ecran: true, Libelle: "Factures", Touche: "F" } && actions["kbis"] is { Ecran: false },
            "les copies portent le numero et le SIREN ; une action d'ecran le dit ; chaque action a sa lettre");

        // --- Les gestes LOCAUX : sans changer d'ecran.
        var autre = v.Dossier.Historique.First(p => p.Libelle != ArbreDemo.LibelleKbisAJour);
        Faire("historiqueOuvrir", autre.Id);
        Faire("fermerApercu");
        Exiger(s.Vue().Dossier!.Apercu is null, "precondition : une autre piece choisie, la colonne du document fermee");
        Faire("actionDossier", "kbis");
        v = s.Vue();
        Exiger(v.Proc is null && v.Dossier!.Apercu?.Apercu is { Libelle: "Kbis a jour", Kbis: not null }
               && Ligne(v.Dossier.Arbre, "Kbis a jour") is { Actif: true }
               && v.Dossier.Historique.Single(p => p.Actif).Libelle == "Kbis a jour",
            "« Kbis a jour » choisit le Kbis dans l'arbre et le montre dans la colonne du document, rouverte");
        Faire("historiqueOuvrir", autre.Id);
        Faire("actionDossier", "imprimerKbis");
        v = s.Vue();
        Exiger(v.Impression is { Libelle: "Kbis a jour" } && v.Dossier!.Apercu?.Apercu?.Libelle == "Kbis a jour",
            "« Imprimer le Kbis » montre le Kbis et ouvre le choix de l'imprimante sur lui");
        Faire("fermerImpression");
        Faire("actionDossier", "imprimerArborescence");
        Exiger(s.Vue().Impression?.Libelle == "Arborescence du dossier 004176",
            "« Imprimer l'arborescence » ouvre la modale d'impression sur l'arborescence du dossier");
        Faire("lancerImpression");
        Exiger(s.Vue().Impression is null && s.Vue().Dossier!.Apercu!.Apercu!.Message.StartsWith("« Arborescence du dossier 004176 » envoye a "),
            "l'impression de l'arborescence part et le dit, en la nommant (la confirmation s'affiche sous la piece ouverte)");
        Faire("imprimer");
        Exiger(s.Vue().Impression?.Libelle == "Kbis a jour",
            "jumeau : le bouton Imprimer du document reprend la piece ouverte, pas l'arborescence");
        Faire("fermerImpression");
        Faire("actionDossier", "envoyerDossier");
        var pieces = s.Vue().Dossier!.Pieces;
        Exiger(s.Vue().EnvoiMail is { PeutEnvoyer: false } envoi && envoi.Libelle == $"Dossier entier 004176 ({pieces} pieces)",
            $"« Envoyer le dossier par mail » ouvre la modale d'envoi sur le dossier entier ({pieces} pieces)");
        Faire("destinataire", "greffe.civil@exemple.fr");
        Faire("envoyerMail");
        Exiger(s.Vue().EnvoiMail is null && s.Vue().Dossier!.Apercu!.Apercu!.Message.Contains("greffe.civil@exemple.fr"),
            "le dossier entier part par mail et le dit (maquette)");
        Faire("envoiMail");
        Exiger(s.Vue().EnvoiMail?.Libelle == "Kbis a jour", "jumeau : le mail du document reprend la piece ouverte");
        Faire("fermerMail");
        Exiger(Refuse(s, "copierSiren") && Refuse(s, "depotsJudiciaires") && Refuse(s, "inconnue")
               && s.Vue() is { Proc: null, Dossier.Numero: "004176" },
            "une copie se joue dans la page ; une action d'un autre type d'affaire, ou inconnue, est refusee franchement");

        // --- Un ECRAN ouvert pour ce dossier : son contexte, la puce, la fermeture.
        Faire("ouvrirCode", "PROC_ENCAISS");
        Exiger(s.Vue().Proc is { Code: "PROC_ENCAISS", Ecran.Dossier: null },
            "un ecran ouvert depuis le menu ne porte aucune affaire");
        Faire("ongletActiver", "AFFAIRE:004176");
        Faire("actionDossier", "factures");
        v = s.Vue();
        Exiger(v.Dossier is null && v.Proc is { Code: "PROC_FACTURE", Libelle: "Factures" } pf
               && pf.Ecran.Dossier is { Numero: "004176", Siren: "812 445 906", Objet: "Modification - SCI DU PARC", Type: "RCS", Nom: "SCI DU PARC" }
               && v.Onglets!.Select(o => o.Code).SequenceEqual(new[] { "AFFAIRE:004176", "PROC_ENCAISS", "PROC_FACTURE" })
               && v.Onglets[2] is { Actif: true, Libelle: "Factures" },
            "« Factures » ouvre l'ecran dans son onglet, au nom de l'action, avec le contexte de l'affaire (n°, SIREN, objet, type, nom)");
        Faire("retourDossier");
        v = s.Vue();
        Exiger(v.Proc is null && v.Dossier is { Numero: "004176" } && v.Dossier.Apercu?.Apercu?.Libelle == "Kbis a jour"
               && v.Onglets is { Length: 3 } ot && ot[0].Actif,
            "la puce « ← Dossier » ramene a l'affaire telle qu'on l'a laissee ; l'onglet de l'ecran reste");
        Faire("ongletActiver", "PROC_FACTURE");
        Exiger(s.Vue().Proc?.Ecran.Dossier?.Numero == "004176", "revenu sur son onglet, l'ecran garde son affaire");
        Faire("ongletFermer", "PROC_FACTURE");
        v = s.Vue();
        Exiger(v.Proc is null && v.Dossier?.Numero == "004176"
               && v.Onglets!.Select(o => o.Code).SequenceEqual(new[] { "AFFAIRE:004176", "PROC_ENCAISS" }) && v.Onglets[0].Actif,
            "fermer l'onglet de l'ecran ramene a SON affaire, pas a l'onglet voisin (Encaissements)");
        Faire("ouvrirCode", "PROC_FACTURE");
        Faire("ongletFermer", "PROC_FACTURE");
        Exiger(s.Vue() is { Dossier: null, Proc.Code: "PROC_ENCAISS" },
            "jumeau : le meme ecran ouvert depuis le menu, ferme, laisse la place a l'onglet voisin");
        Faire("ongletActiver", "AFFAIRE:004176");
        // Lot 9 : les courriers divers sont judiciaires, et le code ne connait aucune lettre type
        // RCS (Metier/CourriersDivers.cs) : un dossier RCS n'en propose aucun, ni le catalogue.
        Exiger(EcrireDe(s).SequenceEqual(new[] { "envoyerDossier" }) && Refuse(s, "courriers") && Refuse(s, "courrier:CD_CONV")
               && s.Vue() is { Proc: null, Dossier.Numero: "004176" },
            "dossier RCS : « Ecrire » ne propose ni courrier type (aucun n'existe pour le RCS) ni le catalogue des modeles ; le geste est refuse");
        Faire("actionDossier", "archivage");
        Exiger(s.Vue().Proc is { Code: "PROC_ARCHIRAD", Ecran.Dossier.Numero: "004176" }, "« Archivage / radiation » s'ouvre pour l'affaire");
        Faire("echap");
        Exiger(s.Vue() is { Proc: null, Dossier.Numero: "004176" } && s.Vue().Onglets!.All(o => o.Code != "PROC_ARCHIRAD"),
            "Echap sur l'ecran ouvert depuis l'affaire le ferme et y ramene aussi");
        Faire("actionDossier", "bodacc");
        Faire("ongletActiver", "PROC_ENCAISS");
        Faire("ongletFermer", "BODACC");
        Exiger(s.Vue() is { Dossier: null, Proc.Code: "PROC_ENCAISS" },
            "un ecran de l'affaire ferme en arriere-plan ne deplace pas la vue");
        Faire("ongletActiver", "AFFAIRE:004176");
        Faire("actionDossier", "demandes");
        Faire("ongletFermer", "AFFAIRE:004176");
        Exiger(s.Vue().Proc is { Code: "PROC_DEMANDE", Ecran.Dossier.Numero: "004176" },
            "l'affaire fermee, l'ecran ouvert pour elle garde sa puce");
        Faire("retourDossier");
        Exiger(s.Vue().Dossier?.Numero == "004176" && s.Vue().Onglets!.Any(o => o is { Code: "AFFAIRE:004176", Actif: true }),
            "la puce rouvre l'affaire fermee, dans son onglet");
        Faire("ongletActiver", "PROC_DEMANDE");
        Faire("ongletFermer", "AFFAIRE:004176");
        Faire("ongletFermer", "PROC_DEMANDE");
        Exiger(s.Vue() is { Dossier: null, Proc.Code: "PROC_ENCAISS" },
            "fermer l'ecran ne rouvre pas une affaire qu'on a fermee : l'onglet voisin prend la place");

        // --- Une affaire purement JUDICIAIRE : ni Kbis ni SIREN, les ecrans du judiciaire.
        Faire("ouvrirNumero", "2026P00031");
        var jud = Codes(s.Vue().Dossier!.Actions);
        Exiger(new[] { "depotsJudiciaires", "redaction", "consultationJudiciaire", "audiences", "gedJudiciaire", "factures", "imprimerArborescence" }.All(jud.Contains)
               && !jud.Contains("kbis") && !jud.Contains("imprimerKbis") && !jud.Contains("copierSiren")
               && !jud.Contains("demandes") && !jud.Contains("commandesWeb"),
            "affaire judiciaire : depots, redaction, role, audiences, GED ; ni Kbis (pas de dossier RCS), ni SIREN, ni ecran du RCS");
        Faire("actionDossier", "depotsJudiciaires");
        Exiger(s.Vue().Proc is { Code: "PROC_DEPOTJUD", Ecran.Dossier: { Numero: "2026P00031", Type: "JUD" } },
            "jumeau : sur l'affaire judiciaire, « Depots judiciaires » ouvre son ecran pour elle");

        // --- ECRIRE sur une affaire judiciaire (lot 9) : les courriers divers de SA nature (type
        // d'entite du modele), produits remplis avec elle dans la colonne du document.
        string TexteDuDocument(SessionPlanTravail session)
            => string.Join("\n", session.Vue().Dossier?.Apercu?.Apercu?.Feuilles.Select(f => f.Texte) ?? []);
        Faire("ouvrirNumero", "J-00931");
        var barreJ = s.Vue().Dossier!.Actions.SelectMany(g => g.Actions).ToList();
        Exiger(EcrireDe(s).SequenceEqual(new[] { "courrier:CD_NOTIF", "redaction", "envoyerDossier" })
               && barreJ.Single(a => a.Code == "courrier:CD_NOTIF") is { Libelle: "Notification au mandataire", Ecran: false, Copie: null, Touche: "C" }
               && barreJ.Select(a => a.Touche).Distinct().Count() == barreJ.Count,
            "J-00931, procedure collective : « Ecrire » propose la notification au mandataire, seul modele actif de sa nature, sans ecran, par la lettre C");
        Exiger(Refuse(s, "courriers") && Refuse(s, "courrier:CD_CONV") && Refuse(s, "courrier:CD_MESURE") && Refuse(s, "courrier:INCONNU"),
            "jumeau : un modele d'une autre nature (convocation d'instance), inactif (suivi de mesure), inconnu, ou la famille elle-meme, est refuse");
        var ongletsAvant = s.Vue().Onglets!.Length;
        Faire("actionDossier", "courrier:CD_NOTIF");
        v = s.Vue();
        var lettre = v.Dossier?.Apercu?.Apercu;
        var texte = TexteDuDocument(s);
        Exiger(v.Proc is null && v.Onglets!.Length == ongletsAvant && v.Dossier?.Numero == "J-00931"
               && lettre is { Libelle: "Notification au mandataire - J-00931", Pdf: false, Kbis: null }
               && lettre.Feuilles.All(f => f.Valeurs is null),
            "le courrier s'affiche dans la colonne du document de l'affaire, seul (aucune valeur de formulaire), sans ouvrir d'ecran ni d'onglet");
        Exiger(new[] { "Greffe du Tribunal de Commerce d'Amiens", "Affaire n° J-00931 : TRANSNORD SA", "41 Route de Paris, 80000 AMIENS",
                       "Maitre SERRANO Philippe", "Mandataire judiciaire", "Objet : Notification au mandataire",
                       "a l'egard de TRANSNORD SA", "Amiens, le " + DateTime.Today.ToString("dd/MM/yyyy") }.All(texte.Contains)
               && !texte.Contains('{') && !texte.Contains('}'),
            "le courrier est rempli avec l'affaire : n°, denomination, adresse, et le mandataire judiciaire qu'il demande");
        // « Editer » ouvrait le formulaire de l'etape que le libelle evoque (« mandataire » :
        // CONTROLE, EtapeDuNoeud) : un formulaire de l'affaire, pas le courrier affiche.
        // fix-ok: mesure rouge sur main 71f90e9 (20/11/2026) -- EditerPiece suivait EtapeDuNoeud(courrier).
        // Sans Fiche.Version : ce compteur avance a chaque lecture de la vue, geste ou non.
        string EtatDeSaisie(SessionPlanTravail session)
            => JsonSerializer.Serialize(new { session.Vue().Dossier!.Etapes, session.Vue().Dossier!.Fiche?.Branche });
        Exiger(lettre is { Editable: false }, "la colonne du document ne montre pas « Editer » sur le courrier : il n'a aucun formulaire");
        var saisieAvant = EtatDeSaisie(s);
        Faire("editer");
        Exiger(EtatDeSaisie(s) == saisieAvant && s.Vue().Dossier?.Apercu?.Apercu?.Libelle == "Notification au mandataire - J-00931",
            "« Editer » sur le courrier n'ouvre aucun formulaire de l'affaire : l'etape et la fiche ne bougent pas, le courrier reste affiche");
        Faire("imprimer");
        Exiger(s.Vue().Impression?.Libelle == "Notification au mandataire - J-00931", "le courrier s'imprime par le bouton du document");
        Faire("lancerImpression");
        Exiger(s.Vue().Dossier?.Apercu?.Apercu?.Message.StartsWith("Envoye a ") == true, "l'impression du courrier part et le dit");
        Faire("envoiMail");
        Exiger(s.Vue().EnvoiMail?.Libelle == "Notification au mandataire - J-00931", "le courrier part par mail par le bouton du document");
        Faire("fermerMail");
        Faire("actionDossier", "kbis");
        Exiger(s.Vue().Dossier?.Apercu?.Apercu is { Libelle: "Kbis a jour", Kbis: not null },
            "jumeau : une autre piece ouverte remplace le courrier dans la colonne du document");
        Exiger(s.Vue().Dossier?.Apercu?.Apercu is { Editable: true }, "jumeau : une piece du classeur (le Kbis) garde « Editer »");
        Faire("actionDossier", "courrier:CD_NOTIF");

        // Une autre affaire, sans Kbis (purement judiciaire) : le courrier ecrit pour TRANSNORD
        // ne la suit pas -- il partirait a l'impression ou par mail sous le nom d'une autre affaire.
        Faire("ouvrirNumero", "2026J00087");
        Exiger(s.Vue().Dossier is { Numero: "2026J00087", Apercu: null },
            "le courrier d'une affaire ne reste pas affiche sur la suivante : sans Kbis, la colonne du document se ferme");
        Exiger(EcrireDe(s).SequenceEqual(new[] { "courrier:CD_CONV", "courrier:CD_RELAN", "redaction", "envoyerDossier" }),
            "2026J00087, instance : la convocation et la relance de piece manquante");
        Faire("actionDossier", "courrier:CD_CONV");
        texte = TexteDuDocument(s);
        Exiger(texte.Contains("Affaire n° 2026J00087 : DUPONT Jean") && texte.Contains("DUPONT Jean\n" + CourriersDivers.AdresseInconnue)
               && !texte.Contains("SERRANO"),
            "la convocation va a la partie, DUPONT Jean ; son adresse, inconnue au dossier, se dit au lieu de s'inventer");

        Faire("ouvrirNumero", "2026C00059");
        Exiger(EcrireDe(s).SequenceEqual(new[] { "courrier:CD_INFO", "redaction", "envoyerDossier" }),
            "2026C00059, prevention : le courrier d'information");
        Faire("actionDossier", "courrier:CD_INFO");
        var enTete = TexteDuDocument(s).Split('\n').TakeWhile(l => !l.StartsWith("Amiens, le ")).Where(l => l.Trim().Length > 0).ToArray();
        Exiger(enTete.Length == 2 && enTete[0] == "Greffe du Tribunal de Commerce d'Amiens" && TexteDuDocument(s).Contains("ATELIER DU MEUBLE"),
            "un modele sans destinataire n'en imprime aucun : avant la date, le seul en-tete du greffe");

        Faire("ouvrirNumero", "2026M00045");
        Exiger(EcrireDe(s).SequenceEqual(new[] { "redaction", "envoyerDossier" }),
            "2026M00045, mesure : son seul modele est inactif -- aucune entree de courrier, pas d'entree vide");

        // --- Le LOGIN : sans le RCS, ni Kbis ni ecran reserve au RCS, et le geste est refuse.
        s.SurGesteAccueil(new GesteIle("login", Valeur("pjudic")));
        Faire("ouvrirNumero", "004176");
        var pj = Codes(s.Vue().Dossier!.Actions);
        Exiger(!pj.Contains("kbis") && !pj.Contains("imprimerKbis") && !pj.Contains("commandesWeb") && pj.Contains("factures") && Refuse(s, "kbis"),
            "un login sans le RCS ne se voit proposer ni le Kbis ni un ecran du RCS ; le geste est refuse");
        s.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Exiger(Codes(s.Vue().Dossier!.Actions).Contains("kbis"), "jumeau : revenu au greffier, le Kbis est propose");

        // --- La fenetre d'un onglet SORTI : pas d'onglets ou ouvrir un ecran.
        var sortie = new SessionPlanTravail(null, null, modeFenetreVue: true, vueInitiale: "AFFAIRE:004176");
        sortie.Initialiser();
        var so = sortie.Vue().Dossier!.Actions;
        Exiger(so.SelectMany(g => g.Actions).All(a => !a.Ecran) && Codes(so).Contains("kbis") && Codes(so).Contains("envoyerDossier")
               && so.All(g => g.Verbe != ActionsDossier.Facturer) && Refuse(sortie, "factures"),
            "dans la fenetre d'un onglet sorti, la barre garde les gestes locaux, sans aucun ecran (« Facturer » disparait)");
        sortie.DisposeAsync().AsTask().GetAwaiter().GetResult();

        foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);
        s.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 16. EN COURS SUR L'AFFAIRE (plan « Vue du dossier = coeur des actions », lot 3,
    /// 20/11/2026), en tete de la colonne de suivi : le travail filtre sur le numero de
    /// l'affaire (RecherchePalette.TravailDuDossier), « M'attribuer » sur la regle de la
    /// palette, l'ecran de chaque ligne ouvert pour ce dossier, les derniers gestes.
    /// </summary>
    private static void JouerEnCoursDossier()
    {
        // --- Le FILTRE par affaire, sur les donnees de JeuDemo.
        var t151 = RecherchePalette.TravailDuDossier("004151", _ => false);
        Exiger(t151.Select(t => (t.Nature, t.Numero, t.Etat)).SequenceEqual(new[]
               {
                   ("dem", "004151", "J+11"), ("cmd", "CW-2026-000413", "A traiter"),
                   ("doc", "D-2026-1170", "A numeriser"), ("cou", "A-2026-0909", "A numeriser"),
               })
               && t151.All(t => t.Cle == "004151" && t.Urgence < RecherchePalette.Termine),
            "004151 : sa demande hors delai, sa commande web (rangee sous son SIREN par les requisitions), "
            + "l'acte et le courrier attendus -- le plus urgent d'abord, rien d'une autre affaire, rien de termine");
        Exiger(t151[1].Titre == "Faillite - TRANSNORD SA" && t151[2].Titre == "Compte de resultat 2025 - TRANSNORD SA"
               && !t151.Any(t => t.Titre.StartsWith("Comptes annuels 2025", StringComparison.Ordinal)),
            "jumeau : l'acte deja numerise de la meme affaire (Comptes annuels 2025) n'est plus en cours");
        var t176 = RecherchePalette.TravailDuDossier("004176", _ => false);
        Exiger(t176.Select(t => (t.Nature, t.Numero)).SequenceEqual(new[] { ("dem", "004176"), ("doc", "D-2026-1181") })
               && t176[0] is { Ton: "al", Qui: "M. DURAND" } && t176[1] is { Etat: "page 3 illisible", Ton: "al", Urgence: 1 },
            "004176 : sa demande hors delai (M. DURAND) et son acte en anomalie ; son courrier deja rattache n'y est plus");
        Exiger(RecherchePalette.TravailDuDossier("004151", d => d == "004151").All(t => t.Nature != "dem")
               && RecherchePalette.TravailDuDossier("", _ => false).Length == 0,
            "une demande validee au controle final n'est plus en cours ; sans numero, rien");

        // --- La VUE de l'affaire 004151 pour le greffier.
        var s = new SessionPlanTravail(null, null);
        s.Initialiser();
        void Faire(string geste, object? valeur = null) => s.Agir(geste, Valeur(valeur));
        foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);
        s.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));
        Faire("ouvrirNumero", "004151");
        var e = s.Vue().Dossier!.EnCours;
        Exiger(e.Travail.Length == 4 && e.HorsDelai == 1
               && e.Travail[0] is { Nature: "dem", Numero: "004151", Titre: "Depot de bilan - TRANSNORD SA", Etat: "J+11", Ton: "al",
                                    Filet: "alerte", Qui.Signature: "C. BENIER", AMoi: false, Attribuable: true, EcranLibelle: "Demandes" }
               && e.Travail[0].Delai == "Delai legal de 7 jours depasse de 4 jours",
            "la colonne de 004151 liste « Depot de bilan - TRANSNORD SA » hors delai, en alerte, avec son agent (C. BENIER)");
        Exiger(e.Travail.Skip(1).All(t => !t.Attribuable) && e.Travail[1] is { Qui: null, EcranLibelle: "Commandes web" }
               && e.Travail[2].EcranLibelle == "Actes numerises" && e.Travail[3].EcranLibelle == "Arrivee du courrier",
            $"jumeau : seule la demande s'attribue ; chaque ligne nomme l'ecran qu'elle ouvre ({string.Join(", ", e.Travail.Select(t => t.EcranLibelle))})");
        Exiger(e.Activite.Length is > 0 and <= 4 && e.Activite.All(a => a.Objet.Contains("004151"))
               && e.Activite[0] is { Agent: "Raphael", Verbe: "a valide", Objet: "depot du dossier 004151" },
            "les derniers gestes sur 004151, le plus recent en tete (le greffier voit ceux de toute l'equipe)");

        // --- « M'attribuer » : le geste de la palette, la meme regle des deux cotes.
        Faire("paletteOuvrir");
        Faire("paletteTexte", "transnord bilan");
        bool PaletteAttribuable() => s.Vue().Palette!.Travail.Single(t => t.Numero == "004151").Attribuable;
        Exiger(PaletteAttribuable(), "precondition : la palette propose aussi d'attribuer la demande 004151");
        Faire("paletteFermer");
        Faire("paletteAttribuer", new { numero = "004151", signature = (string?)null });
        e = s.Vue().Dossier!.EnCours;
        Exiger(e.Travail[0] is { Qui.Signature: "R. VILAIN", Qui.Initiales: "RV", AMoi: true, Attribuable: false }
               && JeuDemo.Demandes.Single(d => d.Numero == "004151").Utilisateur == "R. VILAIN",
            "« M'attribuer » passe la demande 004151 au nom du greffier : la ligne le dit et ne le propose plus");
        Faire("paletteOuvrir");
        Faire("paletteTexte", "transnord bilan");
        Exiger(!PaletteAttribuable(), "jumeau : la palette lit la meme attribution et la meme regle");
        Faire("paletteFermer");

        // --- Un clic sur la ligne : son ecran, ouvert POUR ce dossier (contexte du lot 1).
        Faire("enCoursOuvrir", new { nature = "dem", numero = "004151" });
        var v = s.Vue();
        Exiger(v.Dossier is null && v.Proc is { Code: "PROC_DEMANDE", Libelle: "Demandes" } pd
               && pd.Ecran.Dossier is { Numero: "004151", Objet: "Depot de bilan - TRANSNORD SA" }
               && v.Onglets!.Any(o => o.Code == "AFFAIRE:004151") && v.Onglets.Single(o => o.Actif).Code == "PROC_DEMANDE",
            "un clic sur la demande ouvre l'ecran Demandes dans son onglet, avec le contexte de l'affaire 004151");
        Faire("retourDossier");
        Exiger(s.Vue() is { Proc: null, Dossier.Numero: "004151" }, "la puce « ← Dossier » ramene a l'affaire");
        Faire("enCoursOuvrir", new { nature = "cou", numero = "A-2026-0909" });
        Exiger(s.Vue().Proc is { Code: "PROC_ARRIVEE", Ecran.Dossier.Numero: "004151" },
            "jumeau : le courrier attendu ouvre l'Arrivee du courrier, pour la meme affaire");
        Faire("retourDossier");
        var onglets = s.Vue().Onglets!.Length;
        Faire("enCoursOuvrir", new { nature = "dem", numero = "004176" });
        Faire("enCoursOuvrir", new { nature = "cou", numero = "A-2026-0912" });
        Exiger(s.Vue() is { Proc: null, Dossier.Numero: "004151" } && s.Vue().Onglets!.Length == onglets,
            "une ligne d'une autre affaire, ou terminee, n'ouvre rien depuis 004151");

        // --- Un redacteur RCS : le journal des autres lui est cache, la demande reste attribuable.
        s.SurGesteAccueil(new GesteIle("login", Valeur("cmartin")));
        e = s.Vue().Dossier!.EnCours;
        Exiger(e.Travail[0] is { Qui.Signature: "R. VILAIN", AMoi: false, Attribuable: true }
               && e.Activite.Length > 0 && e.Activite.All(a => a.Agent == "Claire"),
            "un redacteur ne voit que ses propres gestes ; la demande d'un autre lui reste attribuable");

        // --- Un login sans le RCS : la commande web (ecran du RCS) se lit sans s'ouvrir.
        s.SurGesteAccueil(new GesteIle("login", Valeur("pjudic")));
        e = s.Vue().Dossier!.EnCours;
        var refuse = false;
        try { Faire("enCoursOuvrir", new { nature = "cmd", numero = "CW-2026-000413" }); }
        catch (InvalidOperationException) { refuse = true; }
        Exiger(e.Travail.Single(t => t.Nature == "cmd").EcranLibelle == "" && refuse
               && e.Travail.Single(t => t.Nature == "dem").EcranLibelle == "Demandes",
            "login judiciaire : la commande web n'offre pas son ecran (hors perimetre) et le geste est refuse ; la demande, si");
        s.SurGesteAccueil(new GesteIle("login", Valeur("rvilain")));

        // --- La fenetre d'un onglet SORTI : aucune ligne n'ouvre d'ecran.
        var sortie = new SessionPlanTravail(null, null, modeFenetreVue: true, vueInitiale: "AFFAIRE:004151");
        sortie.Initialiser();
        Exiger(sortie.Vue().Dossier!.EnCours.Travail is { Length: 4 } ts && ts.All(t => t.EcranLibelle == ""),
            "dans la fenetre d'un onglet sorti, les lignes se lisent sans ouvrir d'ecran");
        sortie.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // L'attribution vit le temps du processus (JeuDemo) : elle est rendue a son agent.
        JeuDemo.Attribuer("004151", "C. BENIER");
        foreach (var o in s.Vue().Onglets ?? []) Faire("ongletFermer", o.Code);
        s.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 18. LE PARCOURS D'UN AGENT SUR LA PAGE DU DOSSIER (plan « Vue du dossier = coeur des
    /// actions », lot 6, 20/11/2026), joue sur l'app au bureau cache : 004176 puis 004151, le
    /// Kbis, facturer et revenir, une demande en cours, joindre un PDF, imprimer, envoyer le
    /// dossier, sortir l'onglet, redemarrer. Chaque defaut releve a son controle ici, avec
    /// l'entree qui le ferait echouer si la correction etait fausse. Etat a part : le
    /// redemarrage relit ce que la premiere session a retenu.
    /// </summary>
    private static void JouerParcoursDuDossier()
    {
        var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
        var etatIci = Path.Combine(Path.GetTempPath(), "rigv3-parcours-" + Guid.NewGuid().ToString("N") + ".json");
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatIci);
        try
        {
            var s = new SessionPlanTravail(null, null);
            s.Initialiser();
            void Faire(string geste, object? valeur = null) => s.Agir(geste, Valeur(valeur));

            // --- LA BARRE, 004176 a 1516 px : ses cinq verbes replies en menus, il fallait deux
            // clics pour le Kbis ou les factures. Chaque verbe nomme son action FREQUENTE, qui
            // reste un bouton quand il se replie ; un verbe d'une seule action est cette action.
            // Faux si : Enregistrer ou Suivre en nommaient une (la barre s'allongerait sans
            // raison), ou si Ecrire (le seul mail, sur un dossier RCS) restait un menu d'un item.
            Faire("ouvrirNumero", "004176");
            string? Principale(SessionPlanTravail x, string verbe)
                => x.Vue().Dossier!.Actions.Single(g => g.Verbe == verbe).Principale;
            Exiger(Principale(s, ActionsDossier.Delivrer) == "kbis" && Principale(s, ActionsDossier.Facturer) == "factures"
                   && Principale(s, ActionsDossier.Ecrire) == "envoyerDossier"
                   && Principale(s, ActionsDossier.Enregistrer) is null && Principale(s, ActionsDossier.Suivre) is null,
                "004176 : Delivrer nomme « Kbis a jour », Facturer « Factures », Ecrire son seul mail ; Enregistrer et Suivre, rien");

            // --- FACTURER ET REVENIR : l'affaire revient sur SON etape. La page remonte la zone
            // des etapes a neuf, en haut : sans defilement demande, elle rapportait l'etape du
            // haut (« etape 1 / 6 ») et le C# la prenait pour la courante. Faux si : le retour
            // par l'onglet de l'affaire (et pas seulement par la puce) repartait encore en haut.
            Faire("actionDossier", "kbis");
            Faire("allerEtape", 2);
            var avant = s.Vue();
            Exiger(avant.Dossier!.Etapes is { Courante: 2 }, "precondition : 004176 montre ses etapes, sur la 3 / 6");
            var defileAvant = avant.Defilement?.Numero ?? 0;
            Faire("actionDossier", "factures");
            Exiger(s.Vue().Proc?.Code == "PROC_FACTURE", "precondition : Factures ouvert pour 004176");
            Faire("retourDossier");
            var retour = s.Vue();
            Exiger(retour.Dossier?.Etapes is { Courante: 2 } && retour.Defilement is { Index: 2, Doux: false } d1 && d1.Numero > defileAvant,
                $"revenue par la puce « ← Dossier », l'affaire ramene la frise sur son etape 3 / 6 (defilement : {retour.Defilement?.ToString() ?? "aucun"})");
            Faire("ongletActiver", "PROC_FACTURE");
            var defileEcran = s.Vue().Defilement?.Numero ?? 0;
            Faire("ongletActiver", "AFFAIRE:004176");
            Exiger(s.Vue().Defilement is { Index: 2 } d2 && d2.Numero > defileEcran,
                "jumeau : revenue par son onglet, meme retour sur l'etape 3 / 6");

            // --- 004151 S'OUVRE EN MODE TACHE (« Dissolution ») : l'arbre replie ce que la tache
            // ne nomme pas. Le DOSSIER ENTIER part par mail : toutes ses pieces, pas les 2 que la
            // tache montre (la modale disait « Dossier entier 004151 (2 pieces) »). Faux si : on
            // comptait les pieces montrees -- egal hors mode tache, d'ou 004151 ici et pas 004176.
            Faire("ouvrirNumero", "004151");
            var v = s.Vue();
            Exiger(v.Dossier!.Tache is not null, "precondition : 004151 s'ouvre en mode tache");
            static int Masquees(DossierVue x)
                => int.Parse(Regex.Match(x.Tache!.TexteTout, @"(\d+) pieces? masquee").Groups[1].Value, CultureInfo.InvariantCulture);
            var montrees = v.Dossier.Pieces;
            var masquees = Masquees(v.Dossier);
            Faire("actionDossier", "envoyerDossier");
            Exiger(s.Vue().EnvoiMail?.Libelle == $"Dossier entier 004151 ({montrees + masquees} pieces)",
                $"« Envoyer le dossier par mail » compte le dossier entier : {montrees} montrees + {masquees} masquees (lu : {s.Vue().EnvoiMail?.Libelle})");
            Faire("fermerMail");

            // --- JOINDRE UN PDF EN MODE TACHE : la piece reste dans sa carte. Elle passait le
            // compteur de la carte a 5 et partait dans les pieces masquees (42 -> 43). Faux si :
            // la tache ne repliait plus rien (le compte des masquees doit rester le meme).
            var carte = Lignes(v.Dossier.Arbre).First(l => l.Texte == "Pieces jointes");
            var pdf = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\n%%EOF\n"));
            Faire("arbreJoindre", new { id = carte.Id, nom = "attestation-parcours.pdf", contenu = pdf });
            v = s.Vue();
            var jointe = Lignes(v.Dossier!.Arbre).FirstOrDefault(l => l.Texte.StartsWith("attestation-parcours", StringComparison.Ordinal));
            Exiger(jointe is not null && v.Dossier.Pieces == montrees + 1 && Masquees(v.Dossier) == masquees,
                $"le PDF joint en mode tache se montre dans sa carte ({v.Dossier.Pieces} pieces montrees au lieu de {montrees + 1}, {Masquees(v.Dossier)} masquees au lieu de {masquees})");

            // --- IMPRIMER UN DOSSIER ENTIER : la confirmation s'affiche sous la piece ouverte (le PDF
            // joint) ; elle disait « Envoye a \\srv-print... » sans dire QUOI -- on croyait n'avoir
            // imprime que la piece. Elle nomme le dossier imprime. Faux si : la piece ouverte,
            // imprimee par son propre bouton, etait renommee (jumeau : sa confirmation reste courte).
            Faire("arbreMenu", new { id = carte.Id, clientX = 10, clientY = 10 });
            Faire("menuImprimer");
            Exiger(s.Vue().Impression?.Libelle == "Pieces jointes (5 pieces)", $"precondition : la modale imprime les 5 pieces jointes (lu : {s.Vue().Impression?.Libelle})");
            Faire("lancerImpression");
            var message = s.Vue().Dossier!.Apercu!.Apercu!.Message;
            Exiger(message.StartsWith("« Pieces jointes (5 pieces) » envoye a ", StringComparison.Ordinal),
                $"la confirmation d'impression d'un dossier entier dit ce qui est parti (lu : {message})");
            Faire("imprimer");
            Faire("lancerImpression");
            message = s.Vue().Dossier!.Apercu!.Apercu!.Message;
            Exiger(message.StartsWith("Envoye a ", StringComparison.Ordinal), $"jumeau : la piece ouverte, imprimee par son bouton, garde sa confirmation (lu : {message})");
            Faire("actionDossier", "envoyerDossier");
            Faire("destinataire", "greffe.civil@exemple.fr");
            Faire("envoyerMail");
            message = s.Vue().Dossier!.Apercu!.Apercu!.Message;
            Exiger(message.StartsWith($"« Dossier entier 004151 ({montrees + 1 + masquees} pieces) » : mail ", StringComparison.Ordinal),
                $"la confirmation de l'envoi du dossier entier dit ce qui est parti (lu : {message})");

            // --- FACTURER DEPUIS 004151, PUIS REDEMARRER : l'onglet Factures garde son affaire.
            // Il revenait au demarrage sans elle : la facture generale (SELARL LEROY, comptes
            // annuels), ni bloc « Pour le dossier » ni puce de retour.
            Faire("actionDossier", "factures");
            Exiger(s.Vue().Proc is { Code: "PROC_FACTURE", Ecran.Dossier.Numero: "004151" }, "precondition : Factures pour 004151");
            s.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var r = new SessionPlanTravail(null, null);
            r.Initialiser();
            void FaireR(string geste, object? valeur = null) => r.Agir(geste, Valeur(valeur));
            FaireR("ongletActiver", "PROC_FACTURE");
            Exiger(r.Vue().Proc is { Code: "PROC_FACTURE", Ecran.Dossier.Numero: "004151" },
                $"apres un redemarrage, l'onglet Factures ouvert depuis 004151 est toujours le sien (lu : {r.Vue().Proc?.Ecran.Dossier?.Numero ?? "aucune affaire"})");
            FaireR("retourDossier");
            Exiger(r.Vue().Dossier?.Numero == "004151", "apres un redemarrage, sa puce « ← Dossier » ramene a 004151");

            // --- L'ACCUEIL OUVRE L'ECRAN GENERAL : « Facturer › Factures » du menu reprenait
            // l'onglet de 004151, sa facture et son bloc « Pour le dossier ». Faux si : l'action
            // de l'affaire, rejouee ensuite, ne le rendait pas a l'affaire.
            FaireR("accueil");
            FaireR("ouvrirCode", "PROC_FACTURE");
            Exiger(r.Vue().Proc is { Code: "PROC_FACTURE", Ecran.Dossier: null },
                "depuis l'accueil, « Factures » ouvre l'ecran general, pas la facture de 004151");
            FaireR("ongletActiver", "AFFAIRE:004151");
            FaireR("actionDossier", "factures");
            Exiger(r.Vue().Proc is { Code: "PROC_FACTURE", Ecran.Dossier.Numero: "004151" },
                "jumeau : depuis l'affaire, le meme onglet repart pour elle");
            FaireR("accueil");
            FaireR("ouvrirCode", "PROC_FACTURE");
            r.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var r2 = new SessionPlanTravail(null, null);
            r2.Initialiser();
            r2.Agir("ongletActiver", Valeur("PROC_FACTURE"));
            Exiger(r2.Vue().Proc is { Code: "PROC_FACTURE", Ecran.Dossier: null },
                "jumeau : l'ecran ouvert depuis l'accueil reste general au redemarrage");
            r2.DisposeAsync().AsTask().GetAwaiter().GetResult();

            // --- L'ECRAN DEMANDES (ouvert depuis le dossier) nommait le traitement par son code
            // interne (PROC_DEPOTJUD). Il le nomme par l'ecran, comme son onglet. Faux si : le
            // filtre « Processus », qui garde le code pour valeur, ne trouvait plus 004151.
            var tout = new CriteresDemandes("", "", "", null, null, [], false, false, 50);
            var demandes = EcranDemandes.Exemple(tout, []);
            Exiger(demandes.Lignes.Length > 5
                   && demandes.Lignes.All(l => l.TraitementLibelle.Length > 0 && !l.TraitementLibelle.Contains("PROC_"))
                   && demandes.LibellesTraitements.Length == demandes.Traitements.Length
                   && demandes.LibellesTraitements.All(t => t.Length > 0 && !t.Contains("PROC_"))
                   && demandes.Lignes.First(l => l.Numero == "004151").TraitementLibelle == "Depots judiciaires"
                   && demandes.Lignes.First(l => l.Numero == "004176").TraitementLibelle == "Demandes",
                $"Demandes : la colonne Traitement et la liste Processus nomment l'ecran, jamais son code ({string.Join(", ", demandes.Lignes.Select(l => l.TraitementLibelle).Distinct())})");
            var parCode = EcranDemandes.Exemple(tout with { CritProcessus = "PROC_DEPOTJUD" }, []);
            Exiger(parCode.Lignes.Select(l => l.Numero).SequenceEqual(new[] { "004151" }),
                "jumeau : le filtre « Processus » garde le code pour valeur et trouve 004151");

            // --- L'ENTETE A 1100 PX : sur 004151 (mode tache, bouton « Document »), la page sortait
            // de la fenetre -- « 2 points a cor... » coupe, la croix de l'affaire hors champ, le corps
            // empile coupe a droite. La colonne de la page (grille .rig-vged, colonne implicite
            // « auto ») et celle du corps empile (« 1fr ») grandissaient jusqu'a la largeur minimale
            // de leur contenu. Plafonnees a la fenetre (minmax(0, 1fr)), l'identite cede la place et
            // son titre s'abrege. Faux si : une seule des deux colonnes restait libre de grandir.
            var css = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")),
                                                    "wwwroot/rig-accueil.css"));
            var page = Regex.Match(css, @"\n\.rig-vged \{[^}]*grid-template-rows: auto minmax\(0, 1fr\);[^}]*\}").Value;
            var etroit = Regex.Matches(css, @"@media \(max-width: 1100px\) \{(?:[^{}]|\{[^{}]*\})*\}")
                              .Select(m => Regex.Match(m.Value, @"\.rig-vged__corps \{[^}]*\}").Value).FirstOrDefault(c => c.Length > 0) ?? "";
            Exiger(page.Contains("grid-template-columns: minmax(0, 1fr);") && etroit.Contains("grid-template-columns: minmax(0, 1fr) !important;"),
                $"a 1100 px, ni la page du dossier ni son corps empile ne depassent la fenetre (colonnes plafonnees : page « {Regex.Match(page, @"grid-template-columns:[^;]*").Value} », corps « {Regex.Match(etroit, @"grid-template-columns:[^;]*").Value} »)");

            // --- LE CORPS EMPILE A 1100 PX : l'arbre ne montrait que son filtre et ses onglets, le
            // document une bande de quelques pixels, le suivi ses seuls onglets. La regle des cartes
            // (« min-height: 0 », plus bas dans la feuille, meme force) effacait les hauteurs minimales
            // de la mise en page etroite, ecrite plus haut : la grille partageait la hauteur de la
            // fenetre a parts egales (~100 px chacune) au lieu de faire defiler le corps. La cascade est
            // rejouee ici telle qu'elle s'applique a 1100 px. Une hauteur minimale compte si c'est une
            // longueur non nulle, ou « auto » sur une carte qui ne coupe pas son contenu (overflow
            // visible : minimum fonde sur le contenu) ; « max-content » ne compte pas, Chrome l'ignore
            // en dimensionnant les rangees (mesure : rangee de l'arbre a 20 px, carte posee sur la
            // fiche). Faux si : une regle d'egale force placee apres la mise en page etroite remettait
            // « min-height: 0 », ou l'arbre repassait en « overflow: hidden ».
            foreach (var zone in new[] { ".rig-vged__arbre", ".rig-vged__corps > .rig-vged__apercu", ".rig-vged__suivi" })
            {
                var mini = ProprieteA(css, zone, "min-height", 1100);
                var coupe = ProprieteA(css, zone, "overflow", 1100);
                var tient = mini switch
                {
                    null or "0" or "0px" or "max-content" or "min-content" or "fit-content" => false,
                    "auto" => coupe == "visible",
                    _ => true,
                };
                Exiger(tient, $"a 1100 px, « {zone} » garde une hauteur minimale dans le corps empile, qui defile (lu en fin de cascade : min-height {mini ?? "aucune"}, overflow {coupe ?? "aucun"})");
                // Jumeau : en large, les colonnes cote a cote remplissent la hauteur et defilent
                // chacune ; une hauteur minimale y ferait deborder la page.
                Exiger(ProprieteA(css, zone, "min-height", 1500) == "0",
                    $"jumeau : a 1500 px, « {zone} » reste a « min-height: 0 » (lu : {ProprieteA(css, zone, "min-height", 1500) ?? "aucune"})");
            }

            // --- L'ENTETE DE L'AFFAIRE : la barre d'actions mesure ses VOISINS (retour, filet,
            // Document, points a corriger, fermer) en les supposant fixes, seule l'identite cedant la
            // place (ActionsDossier.tsx, mesurer). Les boutons retour et fermer se tassaient de ~8 px
            // chacun quand la barre se depliait : mesures barre depliee, les voisins laissaient 17 px de
            // plus a l'identite, ses puces « tenaient sur une ligne », la barre se repliait ; mesures
            // barre repliee, elle se redepliait. A 1516 px, 004151 restait tout en menus, puces sur deux
            // lignes (releve dans la page du 20/11/2026 : voisins 292,0 px barre repliee, 274,9 depliee).
            // Faux si : un voisin de la barre peut retrecir.
            var voisins = ProprieteA(css, ".rig-vged__entete > :not(.rig-vged__identite)", "flex-shrink", 1516);
            Exiger(voisins == "0",
                $"a 1516 px, les voisins de la barre d'actions ne retrecissent pas : sa mesure ne depend plus de sa forme (lu : flex-shrink {voisins ?? "aucun"})");
            // Jumeau : l'identite, elle, cede la place (sans quoi l'entete deborderait de la fenetre).
            Exiger(ProprieteA(css, ".rig-vged__identite", "flex", 1516) == "1 1 auto"
                   && ProprieteA(css, ".rig-vged__identite", "min-width", 1516) == "0",
                "jumeau : l'identite de l'affaire reste la seule a ceder la place (flex 1 1 auto, min-width 0)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
            try { File.Delete(etatIci); } catch (IOException) { }
            try { Directory.Delete(PiecesJointes.DossierPour(etatIci), recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
