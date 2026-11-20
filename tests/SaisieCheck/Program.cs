using System.Text.RegularExpressions;
using RigV3Desktop;

// Garde-fou de la SAISIE GUIDEE.
//
// Deux promesses a tenir :
//  1. ce qui est saisi survit a la fermeture de l'application ;
//  2. une etape comme Dirigeants ou Etablissements accepte PLUSIEURS fiches.
// Les deux se verifient sur l'etat reellement ecrit sur disque, pas sur
// une intention dans le code.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// Bac a sable : un fichier jetable (RIGV3_ETAT), JAMAIS l'etat reel de l'utilisateur. Jusqu'au
// 20/11/2026, ce banc ecrivait ses fiches dans %APPDATA%\RigV3\accueil.json et remettait une
// sauvegarde a la fin : entre-temps, une application ouverte qui enregistrait ses reglages
// perdait son ecriture, et un arret du banc laissait la saisie de test chez l'utilisateur
// (le fichier changeait de date a chaque .\tests.ps1 -- mesure du parcours du lot 6).
// fix-ok: mesure du 20/11/2026 -- date de %APPDATA%\RigV3\accueil.json changee a 21:29:29,83, pendant
// SaisieCheck seul ; lance seul, ce banc la change (21:29:29 -> 21:30:25), aucun autre ne la touche.
var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
var chemin = Path.Combine(Path.GetTempPath(), "rigv3-saisiecheck-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", chemin);

try
{
    // --- 1. Deux dirigeants et deux etablissements sur l'affaire 0000
    var ecrit = new AccueilEtat();
    ecrit.Saisies["0000"] = new Dictionary<string, List<Dictionary<string, string>>>
    {
        ["DIRIGEANTS"] =
        [
            new() { ["DRGNT_FONCTION_DIRIGEANT"] = "President", ["DRGNT_NUM_ORDRE"] = "1" },
            new() { ["DRGNT_FONCTION_DIRIGEANT"] = "Directeur general", ["DRGNT_NUM_ORDRE"] = "2" }
        ],
        ["SIEGE"] =
        [
            new() { ["ETBLS_TYPE_ADRESSE"] = "Siege social" },
            new() { ["ETBLS_TYPE_ADRESSE"] = "Etablissement secondaire" }
        ],
        ["IDENTITE"] =
        [
            new() { ["DSSRC_SIREN"] = "903 118 776" }
        ]
    };
    ecrit.Enregistrer();

    Exiger(File.Exists(chemin), "l'etat est bien ecrit sur disque");

    // --- 2. Relecture : c'est la seule preuve que la saisie survit
    var relu = AccueilEtat.Charger();

    Exiger(relu.Saisies.ContainsKey("0000"), "l'affaire 0000 est retrouvee apres relecture");

    var dirigeants = relu.Saisies["0000"]["DIRIGEANTS"];
    Exiger(dirigeants.Count == 2, "l'etape Dirigeants garde bien 2 fiches");
    Exiger(dirigeants[0]["DRGNT_FONCTION_DIRIGEANT"] == "President",
        "la 1re fiche dirigeant a conserve sa fonction");
    Exiger(dirigeants[1]["DRGNT_FONCTION_DIRIGEANT"] == "Directeur general",
        "la 2e fiche dirigeant a conserve la sienne, distincte");

    var sieges = relu.Saisies["0000"]["SIEGE"];
    Exiger(sieges.Count == 2, "l'etape Siege garde bien 2 etablissements");
    Exiger(sieges[0]["ETBLS_TYPE_ADRESSE"] != sieges[1]["ETBLS_TYPE_ADRESSE"],
        "les deux etablissements restent distincts");

    Exiger(relu.Saisies["0000"]["IDENTITE"].Count == 1,
        "une etape simple garde exactement une fiche");

    // --- 3. Deux affaires ne se melangent pas
    relu.Saisies["004182"] = new Dictionary<string, List<Dictionary<string, string>>>
    {
        ["IDENTITE"] = [new() { ["DSSRC_SIREN"] = "111 111 111" }]
    };
    relu.Enregistrer();
    var relu2 = AccueilEtat.Charger();
    Exiger(relu2.Saisies["0000"]["IDENTITE"][0]["DSSRC_SIREN"] == "903 118 776"
        && relu2.Saisies["004182"]["IDENTITE"][0]["DSSRC_SIREN"] == "111 111 111",
        "chaque affaire garde SA saisie, sans melange");

    // --- 4. La vue declare bien les deux etapes multiples
    // Lot 2 du passage a React (19/11/2026) : les etapes et leurs regles vivent dans
    // Pont/PlanTravail.cs (le @code de Home.razor, deplace tel quel).
    var vue = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Pont/PlanTravail.cs"));
    var calculVue = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Pont/PlanTravailVue.cs"));
    Exiger(Regex.IsMatch(vue, @"Multiple:\s*true,\s*Unite:\s*""Dirigeant"""),
        "l'etape Dirigeants est declaree multiple dans la vue");
    Exiger(Regex.IsMatch(vue, @"Multiple:\s*true,\s*Unite:\s*""Etablissement"""),
        "l'etape Etablissements est declaree multiple dans la vue");
    // Assertion CIBLEE : Etat.Enregistrer() apparait ailleurs dans la vue,
    // le chercher seul laissait passer une methode Ecrire qui n enregistre rien.
    Exiger(Regex.IsMatch(vue,
            @"void Ecrire\([^)]*\)\s*\{[^}]*FicheVue\(e\)\[c\.Colonne\][^}]*Etat\.Enregistrer\(\);"),
        // fix-ok: le plan de travail intercale PublierDocument() entre l ecriture et Etat.Enregistrer() ; on verifie le corps de Ecrire, pas l adjacence
        "la methode qui ecrit un champ enregistre dans la foulee");

    // --- 4b. Formulaire du milieu (demande du 15/09/2026) : bouton Actions
    // (liste XACTION lue en base) et bouton Historique (qui a change quoi).
    var h = new AccueilEtat();
    h.NoterModification("0000", new ModificationChamp(DateTime.Now, "jean.dupont",
        "Informations des Formalites > Annonce Bodacc", "N° parution", "BODACC.NUM_PARUTION", "12", "13"));
    h.NoterModification("0000", new ModificationChamp(DateTime.Now, "jean.dupont",
        "Informations des Formalites > Annonce Bodacc", "N° parution", "BODACC.NUM_PARUTION", "13", "13"));
    var hl = AccueilEtat.Charger().Modifications["0000"];
    Exiger(hl.Count == 1 && hl[0] is { Auteur: "jean.dupont", Avant: "12", Apres: "13" },
        "une modification est ecrite sur disque avec auteur, avant et apres ; une valeur inchangee n est pas notee");
    Exiger(ActionsXaction.Liste.Length == 20 && ActionsXaction.Liste[0].Code == "1GDMNX"
           && ActionsXaction.Liste[^1].Code == "XVERIF",
        "le menu Actions porte les 20 actions (nature A) du processus XACTION lues en base");
    // PORTE EN REACT (lot 6, 19/11/2026) : Components/FicheInformationsDossier.razor
    // n'est plus qu'une coquille. Le champ se dessine dans
    // web/src/composants/FicheInformationsDossier.tsx et remonte sa valeur a la
    // session de Pont/FicheInformationsDossier.cs, ou vivent Saisir et Modifier :
    // les regles se lisent la, et la session est EXECUTEE juste apres.
    var racineFiche = Path.Combine(AppContext.BaseDirectory, "../../../../../");
    var tsxFiche = File.ReadAllText(Path.Combine(racineFiche, "web/src/composants/FicheInformationsDossier.tsx"));
    var fiche = File.ReadAllText(Path.Combine(racineFiche, "Pont/FicheInformationsDossier.cs"));
    // Verrou KBIS (18/09/2026) : le champ passe par Saisir, qui rend la main
    // a Modifier hors verrou ; une demande en attente n'est PAS une modification.
    Exiger(tsxFiche.Contains("quand=\"change\" changer={(texte) => geste('saisir', { s: bloc.s, c: c.c, valeur: texte })}")
           && Regex.IsMatch(fiche, @"case ""saisir"":[\s\S]{0,300}?Saisir\(bloc, c, CleDuChamp\(bloc, c\),")
           && Regex.IsMatch(fiche, @"void Saisir[\s\S]{0,200}?if \(!Verrou\) \{ Modifier\(bloc, c, cle, apres\)")
           && Regex.IsMatch(fiche, @"(void|async Task) Modifier[\s\S]{0,500}?Etat\?\.NoterModification\("),
        "chaque saisie du formulaire passe par l historique");
    Exiger(Regex.IsMatch(tsxFiche, @"\{vue\.historique \? \([\s\S]{0,2000}?rig-histo__avant[\s\S]{0,1500}?\) : \("),
        "le bouton Historique remplace le formulaire par le journal");

    // La session de la fiche, EXECUTEE comme la coquille et React la jouent.
    // Son etat ecrit dans un fichier jetable (RIGV3_ETAT), jamais dans celui
    // de l'utilisateur.
    var etatAvantFiche = Environment.GetEnvironmentVariable("RIGV3_ETAT");
    var etatFicheBanc = Path.Combine(Path.GetTempPath(), "rigv3-saisiecheck-fiche-" + Guid.NewGuid().ToString("N") + ".json");
    Environment.SetEnvironmentVariable("RIGV3_ETAT", etatFicheBanc);
    try
    {
        var hf = new AccueilEtat();
        var jsonFiche = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        System.Text.Json.JsonElement J(object? o) => System.Text.Json.JsonSerializer.SerializeToElement(o, jsonFiche);
        RigV3Desktop.Pont.ParametresFiche Pf(bool verrou)
            => new("0000", "Dossier RCS", InformationsDossier.LibelleNoeud, InformationsDossier.Sections,
                   InformationsDossier.ProcessusSources, null, 1, hf, verrou);
        var bloc0 = InformationsDossier.Sections[0];
        var cle0 = $"{bloc0.Table}.{bloc0.Champs[0].Colonne}";
        bool Montre(RigV3Desktop.Pont.VueFiche v, string valeur)
            => v.Sections.SelectMany(x => x.Champs).Any(ch => ch.Cle == cle0 && ch.Valeur == valeur);

        var libre = RigV3Desktop.Pont.FicheInformationsDossier.Ouvrir(Pf(false));
        var saisie = RigV3Desktop.Pont.FicheInformationsDossier.Agir(libre, "saisir", J(new { s = 0, c = 0, valeur = "Actif" }));
        Exiger(hf.Modifications["0000"].Last() is { Avant: "", Apres: "Actif" } m0 && m0.Colonne == cle0
               && m0.Element == $"{InformationsDossier.LibelleNoeud} > {bloc0.Titre}"
               && saisie.Rappels.SequenceEqual(["champModifie"]) && Montre(saisie, "Actif"),
            "une saisie hors verrou est ECRITE dans l historique et leve ChampModifie (execute)");

        var ecritures = hf.Modifications["0000"].Count;
        var verrou = RigV3Desktop.Pont.FicheInformationsDossier.Ouvrir(Pf(true));
        var lecture = RigV3Desktop.Pont.FicheInformationsDossier.Agir(verrou, "saisir", J(new { s = 0, c = 0, valeur = "Radie" }));
        Exiger(lecture.LectureSeule && lecture.Verrou is { EnEdition: false, EnAttente: false }
               && hf.Modifications["0000"].Count == ecritures && lecture.Rappels.Length == 0 && Montre(lecture, "Actif"),
            "verrou KBIS : la fiche s ouvre en lecture seule, une frappe n ecrit rien (execute)");
        RigV3Desktop.Pont.FicheInformationsDossier.Agir(verrou, "demanderModification", J(null));
        var brouillon = RigV3Desktop.Pont.FicheInformationsDossier.Agir(verrou, "saisir", J(new { s = 0, c = 0, valeur = "Radie" }));
        var envoi = RigV3Desktop.Pont.FicheInformationsDossier.Agir(verrou, "envoyerDemande", J(null));
        Exiger(brouillon.Verrou is { EnEdition: true, Brouillon: 1 } && !brouillon.LectureSeule && Montre(brouillon, "Radie")
               && envoi.Verrou is { EnAttente: true, Attente: 1 }
               && hf.Modifications["0000"].Count == ecritures && Montre(envoi, "Actif"),
            "une demande en attente n est PAS une modification : la trace et le KBIS restent ceux en vigueur (execute)");

        var histo = RigV3Desktop.Pont.FicheInformationsDossier.Agir(libre, "historique", J(null));
        Exiger(histo.Historique && histo.Entrees.Length == ecritures && histo.Entrees[0].Apres == "Actif"
               && histo.Entrees[0].Avant == "(vide)",
            "le bouton Historique montre le journal, le plus recent en tete (execute)");
        RigV3Desktop.Pont.FicheInformationsDossier.Fermer(libre);
        RigV3Desktop.Pont.FicheInformationsDossier.Fermer(verrou);
    }
    finally
    {
        Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvantFiche);
        try { File.Delete(etatFicheBanc); } catch (IOException) { }
    }
    // Les trois fiches (dossier, formalites, depots) recoivent l'etat vivant : la
    // session du plan de travail les ouvre avec lui (ParametresFiche).
    Exiger(Regex.Matches(calculVue, @"new ParametresFiche\(d\.Numero,[^;]*?\bEtat\b").Count >= 2,
        "la vue transmet l etat au formulaire");
}
finally
{
    Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
    try { File.Delete(chemin); } catch (IOException) { }
}

// --- 5. L etat valide remonte dans TOUS les ecrans, depuis une seule source
// Les VUES sont les ecrans et composants React (web/src, ports du 18/11/2026 au
// 19/11/2026) : Blazor et ses .razor sont retires (lot 3 du plan « finir le
// passage a React », 19/11/2026). Les tests (*.test.tsx) n'en sont pas.
var racineWeb = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../web/src"));
var fichiersTsx = Directory.Exists(racineWeb)
    ? Directory.GetFiles(racineWeb, "*.tsx", SearchOption.AllDirectories)
               .Where(f => !f.EndsWith(".test.tsx", StringComparison.OrdinalIgnoreCase)).ToArray()
    : [];
var fichiersVues = fichiersTsx.Where(f => Regex.IsMatch(f, @"[\\/](ecrans|composants)[\\/]")).ToArray();
// Le plan de travail (lot 2) : le @code de Home.razor est dans Pont/PlanTravail.cs,
// sa vue et ses gestes dans Pont/PlanTravailVue.cs ; l'aiguillage des ecrans est
// web/src/plan/Aiguillage.tsx (lu ci-dessus avec les .tsx, et seul ci-dessous).
var racinePont = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Pont"));
var fichiersPlan = new[] { Path.Combine(racinePont, "PlanTravail.cs"), Path.Combine(racinePont, "PlanTravailVue.cs") };
var aiguillage = File.ReadAllText(Path.Combine(racineWeb, "plan", "Aiguillage.tsx"));
var vue2 = string.Join(Environment.NewLine, fichiersTsx.Concat(fichiersPlan).Select(File.ReadAllText));
Exiger(fichiersVues.Length >= 4, $"les ecrans sont bien decoupes en composants ({fichiersVues.Length} vues)");
Exiger(fichiersTsx.Length >= 3, $"les ecrans React sont lus avec les vues ({fichiersTsx.Length} .tsx)");

// Les listes de reference (modes d arrivee, categories de depot) ont quitte
// les vues pour Metier/JeuDemo.cs : on les cherche donc dans les DEUX, sinon
// le garde-fou passerait au vert sur une liste disparue.
var racineMetier = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Metier"));
var sources = vue2 + Environment.NewLine
              + string.Join(Environment.NewLine,
                  Directory.GetFiles(racineMetier, "*.cs", SearchOption.AllDirectories)
                           .Select(File.ReadAllText));
Exiger(Regex.IsMatch(vue2, @"private bool EstValide\(string numeroDossier\) => Etat\.Valides\.Contains"),
    "une seule source de verite pour l etat valide");
// Les ecrans Demandes, Arrivee et Actes sont PORTES EN REACT (lot 3, 19/11/2026) :
// leur filtre et l etat AFFICHE vivent dans Pont/<Ecran>.cs, leur balisage et
// leurs clics dans web/src/ecrans/<Ecran>.tsx. Chaque regle ci-dessous se lit a
// son nouvel endroit, ET s execute par le pont (meme appel que l ecran React).
var racineDepot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string LireDepot(string relatif) => File.ReadAllText(Path.Combine(racineDepot, relatif));
var pontDemandes = LireDepot("Pont/EcranDemandes.cs");
var pontActes = LireDepot("Pont/EcranActes.cs");
var tsxArrivee = LireDepot("web/src/ecrans/EcranArrivee.tsx");
var tsxActes = LireDepot("web/src/ecrans/EcranActes.tsx");
RigV3Desktop.Pont.CriteresDemandes CritDemandes(string[] etats, bool enCours = false)
    => new("", "", "", null, null, etats, false, enCours, 500);
var demandeEnCours = JeuDemo.Demandes.First(d => d.EnCours);
var grilleValide = RigV3Desktop.Pont.EcranDemandes.Trouver(CritDemandes([]), [demandeEnCours.Dossier]);

Exiger(Regex.IsMatch(pontDemandes, @"valide \? ""Validee""")
       && grilleValide.Lignes.Where(l => l.Dossier == demandeEnCours.Dossier).All(l => l.Etat == "Validee" && l.Statut == "ok"),
    "la grille de resultats affiche Validee pour un dossier valide");
Exiger(Regex.IsMatch(pontDemandes, @"!valide && dem\.EnCours")
       && grilleValide.Lignes.Where(l => l.Dossier == demandeEnCours.Dossier).All(l => !l.EnCours)
       && RigV3Desktop.Pont.EcranDemandes.Trouver(CritDemandes([], enCours: true), [demandeEnCours.Dossier])
              .Lignes.All(l => l.Dossier != demandeEnCours.Dossier),
    "un dossier valide n est plus compte en cours dans la grille");
// La tuile « Ma file » a ete RETIREE de l accueil le 17/09/2026 a la demande de
// l utilisateur : l assertion qui gardait son appel a EstValide n a plus d objet.
// Le meme invariant reste garde ci-dessus sur la grille de resultats.
Exiger(pontDemandes.Contains("CritEtats.Contains(EtatReel(d))")
       && RigV3Desktop.Pont.EcranDemandes.Trouver(CritDemandes(["Validee"]), [demandeEnCours.Dossier])
              .Lignes.Any(l => l.Dossier == demandeEnCours.Dossier),
    "le filtre par etat suit la validation, sinon il raterait les dossiers valides");

// --- 6. L ecran Arrivee du courrier a ses PROPRES criteres et colonnes
Exiger(aiguillage.Contains("case 'PROC_ARRIVEE':") && aiguillage.Contains("<EcranArrivee"),
    "la procedure Arrivee ouvre son propre ecran");
foreach (var colonne in new[] { "ARRV_NUM_ARRIVEE", "ARRV_NOM", "ARRV_REFERENCES",
                                "ARRV_MODE_ARRIVEE", "ARRV_CASE_COURRIER" })
{
    Exiger(sources.Contains(colonne), $"le critere {colonne} vient de la table ARRIVEE");
}
foreach (var mode in new[] { "Accueil", "e-Mail", "Edi", "Fax", "Poste", "Recommande", "Taxi-colis" })
{
    Exiger(sources.Contains(mode), $"le mode d arrivee « {mode} » est celui de dbo.MODE_ARRIVEE");
}
// --- 7. Une arrivee mene a SA demande, sauf quand il n y en a pas
var courrierSaisie = RigV3Desktop.Pont.EcranArrivee.Trouver(
    new("", "", "", "", "", null, null, false, false, 500)).Lignes;
Exiger(tsxArrivee.Contains("ouvrirDossier(a.numDemande)")
       && aiguillage.Contains("<EcranArrivee ouvrirDossier={ouvrirParNumero} />")
       && courrierSaisie.Any(a => a.NumDemande.Length > 0),
    "la ligne d arrivee ouvre la demande liee");
Exiger(Regex.IsMatch(tsxArrivee, @"function ouvrir\(a: LigneArrivee\) \{\s*if \(a\.numDemande\.trim\(\) === ''\) return\s+ouvrirDossier")
       && courrierSaisie.Any(a => a.NumDemande.Trim() == ""),
    "une arrivee sans demande ne mene nulle part");
Exiger(vue2.Contains("rig-table__ligne--inerte"),
    "une ligne sans demande se distingue visuellement");
Exiger(vue2.Contains("non rattachee"),
    "la colonne N Demande dit clairement qu il n y a pas de demande");

// --- 8. L ecran Actes numerises : criteres et colonnes de RIG v2
Exiger(aiguillage.Contains("case 'PROC_ACTES_NUM':") && aiguillage.Contains("<EcranActes"),
    "la procedure Actes numerises ouvre son propre ecran");
foreach (var libelle in new[] { "N Dossier / N Depot", "Abrege piece", "Type de depot",
                                "Lister actes non numerisables", "Date de cloture", "Anomalies" })
{
    Exiger(sources.Contains(libelle), $"le libelle « {libelle} » vient des ecrans de PROC_ACTES_NUM");
}
foreach (var cat in new[] { "Depots actes", "Depots des comptes annuels", "Beneficiaire effectif", "Proces-Verbal" })
{
    Exiger(sources.Contains(cat), $"la categorie « {cat} » est celle de dbo.CATEGORIE_DEPOT");
}
// Les actes non numerisables sont MASQUES par defaut : la case les ajoute.
RigV3Desktop.Pont.CriteresActes CritActes(bool nonNumerisables)
    => new("", "", "", null, null, nonNumerisables, false, 500);
var actesParDefaut = RigV3Desktop.Pont.EcranActes.Trouver(CritActes(false)).Lignes;
var actesAvecCase = RigV3Desktop.Pont.EcranActes.Trouver(CritActes(true)).Lignes;
Exiger(pontActes.Contains("if (!c.CritNonNumerisables) q = q.Where(a => a.Numerisable);")
       && actesParDefaut.All(a => a.Numerisable)
       && actesAvecCase.Length > actesParDefaut.Length && actesAvecCase.Any(a => !a.Numerisable),
    "les actes non numerisables sont masques tant que la case n est pas cochee");
Exiger(Regex.IsMatch(tsxActes, @"function ouvrir\(a: LigneActe\) \{\s*if \(!a\.numerisable\) return\s+ouvrirDossier"),
    "un acte non numerisable n ouvre rien");

// --- 9. Historique VGED : evenements HORODATES a l instant de l action
var etatSrc = File.ReadAllText(Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../../AccueilEtat.cs")));
Exiger(etatSrc.Contains("public Dictionary<string, List<string>> Journal"),
    "le journal VGED est conserve dans l etat, par affaire");
Exiger(Regex.IsMatch(etatSrc, @"\$""\{DateTime\.Now:[^""]+\} · \{evenement\}"""),
    "chaque evenement du journal est horodate a l instant de l action");
Exiger(Regex.IsMatch(etatSrc, @"public void Journaliser[\s\S]{0,900}?Enregistrer\(\);"),
    "le journal est ecrit sur disque, pas garde en memoire");
Exiger(etatSrc.Contains("lignes[^1].EndsWith(evenement"),
    "une frappe repetee ne produit pas un evenement par caractere");
Exiger(vue2.Contains("Etat.Journaliser(CleAffaire, \"Dossier VALIDE au controle final\")"),
    "la validation laisse une trace dans l historique");
Exiger(!vue2.Contains("Historique VGED"),
    "le bouton Historique VGED est retire de la barre de commandes");

// --- 10. Une procedure non portee le DIT, elle n emprunte pas l ecran des demandes
Exiger(aiguillage.Contains("case 'PROC_DEMANDE':"),
    "l ecran des demandes est reserve a PROC_DEMANDE");
Exiger(vue2.Contains("<EcranNonMaquette"),
    "les procedures non portees ouvrent l ecran qui les nomme");
Exiger(!Regex.IsMatch(vue2, @"default:\s*(return\s*)?<EcranDemandes"),
    "aucune procedure ne retombe en silence sur l ecran des demandes");

// --- 11. TOUTE procedure du menu a son ecran, ou dit pourquoi elle n en a pas.
//
// Defaut d origine (10/09/2026) : le menu annoncait 24 procedures et seules
// 3 avaient un ecran ; les 21 autres tombaient sur l ecran « non maquettee ».
// Ce controle relit le menu et exige un case pour CHAQUE code. Le catalogue vit dans
// Pont/CatalogueEcrans.cs depuis le 20/11/2026 (sorti de PlanTravail.cs : l'ecran
// Demandes y lit le nom de ses traitements) ; la session en derive son aiguillage.
var catalogue = File.ReadAllText(Path.Combine(racinePont, "CatalogueEcrans.cs"));
var blocMenu = Regex.Match(catalogue, @"public static readonly FamilleEcrans\[\] Familles =[\s\S]*?\r?\n    \];").Value;
Exiger(blocMenu.Length > 0, "le catalogue des procedures est lisible (Pont/CatalogueEcrans.cs)");
Exiger(vue2.Contains("CatalogueEcrans.Familles"), "le plan de travail aiguille sur ce catalogue, pas sur une copie");
var codesMenu = Regex.Matches(blocMenu, @"new\(""(?<code>[A-Z][A-Z_]{3,})"",\s*""")
    .Select(m => m.Groups["code"].Value).Distinct().ToArray();
Exiger(codesMenu.Length >= 20, $"le menu expose bien ses procedures ({codesMenu.Length} lues)");

// Codes SANS ecran assume : la raison doit etre ecrite dans le code.
var sansEcran = new[] { "PROC_CREANCE" };
foreach (var code in codesMenu)
{
    var porte = aiguillage.Contains($"case '{code}':");
    if (sansEcran.Contains(code))
    {
        Exiger(!porte, $"{code} n a pas d ecran (v2 est une coquille vide)");
        Exiger(Regex.IsMatch(vue2, "\"" + code + "\"" + @"\s*=>"),
            $"{code} explique POURQUOI il n a pas d ecran");
    }
    else
    {
        Exiger(porte, $"la procedure {code} du menu ouvre son propre ecran");
    }
}

// Chaque ecran cite la procedure v2 dont il vient : pas de formulaire generique.
// Les ecrans sont tous en React depuis le retrait de Blazor (19/11/2026) : le .tsx
// garde la trace de sa source v2. Un ecran NEUF (geste que v2 ne portait pas) n a
// pas de procedure a citer : il doit alors le DIRE, faute de quoi la regle
// exigerait un code invente. EcranNonMaquette.tsx en est exempt : il ne vient
// d aucune procedure, il NOMME celle qui n a pas d ecran (porte au lot 5, 18/11/2026).
var ecransPortes = fichiersTsx.Where(f => Regex.IsMatch(Path.GetFileName(f), @"^Ecran\w*\.tsx$")).ToArray();
Exiger(ecransPortes.Length >= 37, $"les ecrans React sont tous lus ({ecransPortes.Length})");
foreach (var fichier in ecransPortes)
{
    var nom = Path.GetFileName(fichier);
    if (nom == "EcranNonMaquette.tsx") continue;
    Exiger(Regex.IsMatch(File.ReadAllText(fichier), @"(PROC_[A-Z_]+|BODACC|Sans origine RIG v2)"),
        $"{nom} nomme la procedure RIG v2 dont il est porte, ou dit qu il n en a pas");
}

// L ecran « non maquettee » est rendu par React depuis le lot 5 : la regle de la
// section 10 (une procedure non portee le DIT) se lit dans son .tsx, et
// l'aiguillage du plan de travail lui transmet le libelle et le motif (sa coquille
// .razor le faisait jusqu'au retrait de Blazor, 19/11/2026).
var nonMaqTsx = fichiersTsx.FirstOrDefault(f => Path.GetFileName(f) == "EcranNonMaquette.tsx");
Exiger(nonMaqTsx is not null, "l ecran des procedures non portees existe en React (EcranNonMaquette.tsx)");
var nonMaq = nonMaqTsx is null ? "" : File.ReadAllText(nonMaqTsx);
Exiger(Regex.IsMatch(nonMaq, @"className=""rig-nonmaq__titre"">\{libelle\}"),
    "l ecran non maquette affiche le libelle de la procedure ouverte");
Exiger(Regex.IsMatch(nonMaq, @"motif[\s\S]{0,120}?<p className=""rig-nonmaq__texte"">\{motif\}</p>"),
    "le motif etabli de l absence d ecran est affiche tel quel quand il est connu");
Exiger(nonMaq.Contains("Cet ecran n'est pas encore disponible."),
    "sans motif connu, l ecran dit qu il n est pas encore disponible");
Exiger(aiguillage.Contains("<EcranNonMaquette code={proc.code} libelle={proc.libelle} motif={ecran.motif} />")
       && Regex.IsMatch(File.ReadAllText(fichiersPlan[1]), @"\bMotif\b"),
    "l'aiguillage transmet a EcranNonMaquette le libelle et le motif calcules par le plan de travail");

// --- 12. Le menu de procedures n est plus le SEUL chemin vers un traitement.
//
// Direction du 11/09/2026 : le systeme de procedures doit disparaitre de
// l interface. Deux chemins le remplacent, et ce controle les verrouille
// pour qu ils ne repartent pas en silence.
// Depuis le 19/11/2026 (conv-2018), la recherche est la palette Ctrl K, que la
// barre de l accueil ouvre : TraitementsPour(terme) lui fournit les ecrans.
Exiger(Regex.IsMatch(vue2, @"private Proc\[\] TraitementsPour\(string terme\)"),
    "la recherche (palette, ouverte par la barre de l accueil) trouve aussi les traitements, pas que les affaires");
var traitements = Regex.Match(vue2, @"TraitementsPour\(string terme\)\s*=>[\s\S]{0,400}?;");
Exiger(traitements.Success && traitements.Value.Contains("Correspond(terme, p.Libelle)")
       && !Regex.IsMatch(traitements.Value, @"Correspond\([^)]*p\.Code|p\.Code\.Contains"),
    "la recherche des traitements porte sur le LIBELLE, jamais sur le code interne");
Exiger(Regex.IsMatch(vue2, @"private Proc\[\] ActionsDe\(Dossier"),
    "l affaire ouverte propose ses propres traitements");
// Demande du 15/09/2026 : la barre du haut de la visionneuse (onglets,
// tableau des editions, actions) est RETIREE — ses boutons faisaient sortir
// de la vue. Ce controle verrouille son absence.
Exiger(!vue2.Contains("rig-vged__outils") && !vue2.Contains("ActionsDe(DossierOuvert)"),
    "la visionneuse n a plus de barre d outils ni de boutons d actions qui font sortir de la vue");
Exiger(Regex.IsMatch(vue2, @"ActionsDe[\s\S]{0,500}?d\.Type == ""JUD"""),
    "les actions suivent le domaine de l affaire (RCS ou judiciaire)");

// Le retour d affaire : sans lui, cliquer une action FERMAIT l affaire.
// Depuis le 20/11/2026, la barre d actions du dossier (rig-vged__agir, Pont/PlanTravailActions.cs)
// ramene ses actions dans la vue : ses ecrans s ouvrent « pour ce dossier », dans un onglet
// qui retient l affaire et y ramene. La memoire est PAR ONGLET (AffaireDesEcrans) : une
// valeur unique n etait consommee que si plus rien n etait montre, jamais depuis que
// l affaire a son propre onglet. Le parcours est joue par tests/PlanTravailCheck (15).
Exiger(Regex.IsMatch(vue2, @"private void OuvrirProcDepuisAffaire\("),
    "le chemin qui garde le contexte d une affaire reste disponible");
Exiger(Regex.IsMatch(vue2, @"OuvrirProcDepuisAffaire[\s\S]{0,300}?AffaireDesEcrans\[proc\.Code\] = depuis;"),
    "l affaire d origine est memorisee, pour l onglet de l ecran, au lancement du traitement");
Exiger(Regex.IsMatch(vue2, @"private void FermerOnglet\(string code\)[\s\S]{0,700}?AffaireDesEcrans\.TryGetValue\(code[\s\S]{0,300}?RevenirAuDossier\(retour")
       && Regex.IsMatch(vue2, @"private void FermerProc\(\)[\s\S]{0,200}?FermerOnglet\(courant\.Code\)"),
    "fermer le traitement (sa croix, Echap) ROUVRE l affaire d ou il a ete lance");
Exiger(Regex.IsMatch(vue2, @"private void FermerOnglet\(string code\)[\s\S]{0,700}?AffaireDesEcrans\.Remove\(code\);"),
    "le retour d affaire est consomme une fois, pas rejoue indefiniment");

// --- 13. La colonne de droite sait afficher le FORMULAIRE, pas seulement le
// document. Defaut du 11/09/2026 : la colonne de droite n avait plus qu un
// locataire (l apercu) et « Editer » ne posait qu un message, sans ouvrir le
// formulaire deja concu ni toucher le document.
Exiger(Regex.IsMatch(vue2, @"private bool DonneesVisible"),
    "les donnees ont leur PROPRE colonne (3 zones, 14/09/2026)");
Exiger(Regex.IsMatch(vue2, @"private void EditerPiece\(\)[\s\S]{0,600}?CodeEtapeGuide = EtapeDuNoeud\(ApercuNoeud\)\.Code;"),
    "Editer ouvre le formulaire de l ETAPE liee a la piece, pas un formulaire vide");
Exiger(Regex.IsMatch(vue2, @"private void EditerPiece\(\)[\s\S]{0,600}?DonneesVisible = true;"),
    "Editer ouvre la colonne des donnees");
Exiger(Regex.IsMatch(vue2, @"private void OuvrirApercu[\s\S]{0,400}?CodeEtapeGuide = EtapeDuNoeud\(n\)\.Code;"),
    "les donnees affichees suivent la piece cliquee dans l arbre");
// Lot 2 (19/11/2026) : le fragment ChampsSaisie du .razor est la fonction Champs
// de web/src/plan/Etapes.tsx ; sa vue est calculee par ChampsVue (Pont/PlanTravailVue.cs).
var etapesTsx = File.ReadAllText(Path.Combine(racineWeb, "plan", "Etapes.tsx"));
// Le pied des etapes reprend la MEME fonction pour les champs de l'etape Controle,
// sortie de la liste le 19/11/2026 (conv-2022) : deux appels, une seule fonction.
Exiger(Regex.IsMatch(etapesTsx, @"rig-vged__donnees[\s\S]{0,16000}?<Champs code=\{page\.code\}")
       && Regex.IsMatch(etapesTsx, @"function Pied\([\s\S]{0,1500}?<Champs code=""CONTROLE""")
       && Regex.Matches(etapesTsx, @"<Champs ").Count == 2
       && Regex.Matches(etapesTsx, @"function Champs\(").Count == 1,
    "la colonne des donnees affiche CELLE de la saisie guidee, pas une copie (le pied l'appelle pour le controle)");
Exiger(Regex.Matches(vue2, @"foreach \(var c in courante\.Champs\)").Count == 1
       && Regex.Matches(etapesTsx, @"className=""rig-champs""").Count == 1,
    "la boucle des champs de la saisie guidee n existe qu UNE fois (pas de copie)");
// Le document montre ce que le formulaire a renseigne. Depuis le 15/09/2026
// ce n est plus un encadre colle sous l apercu (« rig-apercu__saisie ») mais
// l extrait Kbis LUI-MEME, re-rendu a chaque frappe : le controle porte donc
// sur le branchement reel, pas sur le nom d une classe disparue.
// La forme juridique est celle de la fiche sommaire de l'affaire (lot 7, 20/11/2026).
Exiger(Regex.IsMatch(vue2, @"ApercuKbis\.Vue\(d\.Numero, d\.Siren, DenominationAffaire\(\), SommaireDu\(d\)\.FormeJuridique, LireSaisie\)")
       && vue2.Contains("<ApercuKbis vue={a.kbis} />"),
    "le document montre ce que le formulaire a renseigne");
Exiger(Regex.IsMatch(vue2, @"private string LireSaisie\(string codeEtape, string colonne\)[\s\S]{0,400}?FicheVue\(etape\)\.TryGetValue\(colonne"),
    "ce que le document affiche est LU dans la saisie guidee, pas recopie");
Exiger(Regex.IsMatch(vue2, @"private List<\(string Libelle, string Valeur\)> ValeursDocument\(NoeudGed"),
    "les valeurs portees au document viennent de la saisie de la piece");

// --- 14. PREREMPLISSAGE et AFFICHAGE CONDITIONNEL (demande du 16/09/2026).
//
// Defaut d origine : les 51 champs de la saisie guidee etaient tous libres,
// vides, et TOUS affiches quel que soit le type de dossier. Le modele de
// champ ne savait porter ni valeur de depart ni condition d affichage.
Exiger(Regex.IsMatch(vue2, @"record Champ\([\s\S]{0,400}?string Defaut = """""),
    "le modele de champ porte une valeur de depart (Defaut)");
Exiger(Regex.IsMatch(vue2, @"record Champ\([\s\S]{0,400}?string VisibleSi = """""),
    "le modele de champ porte une condition d affichage (VisibleSi)");
// La valeur de depart est ECRITE dans la fiche, pas seulement affichee :
// sinon elle ne partirait ni au document ni dans accueil.json.
Exiger(Regex.IsMatch(vue2, @"NouvelleFiche\(string etape\)[\s\S]{0,600}?c\.Defaut"),
    "une fiche neuve est prereemplie avec les valeurs de depart");
Exiger(!Regex.IsMatch(vue2, @"fiches = \[new Dictionary<string, string>\(\)\]"),
    "la premiere fiche d une etape passe elle aussi par le preremplissage");
Exiger(Regex.IsMatch(vue2, @"AjouterFiche\(Etape e\)[\s\S]{0,300}?fiches\.Add\(NouvelleFiche\("),
    "une fiche ajoutee (2e dirigeant, 2e etablissement) est prereemplie aussi");
// Le conditionnel s ajoute au fragment UNIQUE de rendu, pas dans une copie.
Exiger(Regex.IsMatch(vue2, @"foreach \(var c in courante\.Champs\)[\s\S]{0,200}?if \(!Visible\(courante, c\)\) continue;"),
    "un champ dont la condition n est pas remplie n est pas affiche");
// Un champ cache ne doit jamais bloquer la validation en silence.
Exiger(Regex.IsMatch(vue2, @"bool RequisManquant\(Etape e, Champ c\)\s*=>\s*c\.Obligatoire && Visible\(e, c\)"),
    "un champ cache ne compte pas comme requis manquant");
Exiger(Regex.IsMatch(vue2, @"string\? RefusDe\(Etape e, Champ c\)\s*\{\s*if \(!Visible\(e, c\)\) return null;"),
    "un champ cache ne peut pas etre refuse");
// Couverture reelle sur les etapes : sans seuil, un seul champ prerempli
// ferait passer la regle au vert.
var blocEtapes = Regex.Match(vue2, @"private readonly Etape\[\] Etapes =[\s\S]*?\r?\n    \];").Value;
var nbChamps = Regex.Matches(blocEtapes, @"new\(""[^""]+"", ""[A-Z]").Count;
var nbDefauts = Regex.Matches(blocEtapes, @"Defaut:").Count;
var nbConditions = Regex.Matches(blocEtapes, @"VisibleSi:").Count;
Exiger(nbChamps >= 55, $"les etapes portent les champs metier attendus ({nbChamps} lus)");
Exiger(nbDefauts >= 30, $"au moins 30 champs partent prereemplis ({nbDefauts} lus)");
Exiger(nbConditions >= 8, $"au moins 8 champs sont conditionnels ({nbConditions} lus)");
// Les valeurs de depart ne sont pas inventees : elles viennent de Metier/.
Exiger(vue2.Contains("JeuDemo.Aujourdhui") && vue2.Contains("Greffes.Defaut"),
    "les valeurs de depart sont tirees des listes de Metier/, pas ecrites en dur");

// --- La location-gerance : la condition est-elle ECRITE, et FAIT-elle son travail ?
// Les trois champs de la loi de 1956 (section LOI_1956 de Metier/InformationsDossier.cs)
// n'ont de sens que pour un fonds donne en location-gerance. On ne se contente pas de
// lire la condition dans le source : on l'EXECUTE sur deux fiches, via la regle reelle
// que le formulaire applique (Metier/ConditionChamp.cs).
foreach (var colonne in new[] { "LOI56_DATE_ACTE", "LOI56_DATE_EXPIRATION", "LOI56_TACITE_RECONDUC" })
{
    var champ = Regex.Match(blocEtapes, @"new\(""[^""]+"", """ + colonne + @"""[\s\S]{0,300}?\),?");
    var condition = Regex.Match(champ.Value, @"VisibleSi: ""([^""]+)""");
    if (!condition.Success)
    {
        Exiger(false, $"{colonne} porte une condition d'affichage");
        continue;
    }

    var regle = condition.Groups[1].Value;
    var horsLocation = new Dictionary<string, string> { ["ETBLS_MODE_EXPLOITATION"] = "Exploitation directe" };
    var enLocation = new Dictionary<string, string> { ["ETBLS_MODE_EXPLOITATION"] = "Location-gerance" };

    Exiger(!ConditionChamp.Remplie(horsLocation, regle),
        $"{colonne} reste cache en exploitation directe");
    Exiger(ConditionChamp.Remplie(enLocation, regle),
        $"{colonne} apparait en location-gerance");
    Exiger(!ConditionChamp.Remplie(new Dictionary<string, string>(), regle),
        $"{colonne} reste cache sur une fiche neuve");
}

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("Saisie guidee : tout est vert.");
    return 0;
}
Console.WriteLine($"Saisie guidee : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
