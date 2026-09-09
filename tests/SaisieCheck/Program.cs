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

var chemin = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "RigV3", "accueil.json");

// On ne detruit pas la saisie de l'utilisateur : on met son fichier de cote.
var sauvegarde = File.Exists(chemin) ? File.ReadAllText(chemin) : null;

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
    var vue = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../Components/Pages/Home.razor"));
    Exiger(Regex.IsMatch(vue, @"Multiple:\s*true,\s*Unite:\s*""Dirigeant"""),
        "l'etape Dirigeants est declaree multiple dans la vue");
    Exiger(Regex.IsMatch(vue, @"Multiple:\s*true,\s*Unite:\s*""Etablissement"""),
        "l'etape Etablissements est declaree multiple dans la vue");
    // Assertion CIBLEE : Etat.Enregistrer() apparait ailleurs dans la vue,
    // le chercher seul laissait passer une methode Ecrire qui n enregistre rien.
    Exiger(Regex.IsMatch(vue,
            @"FicheVue\(e\)\[c\.Colonne\][^;]*;\s*Etat\.Enregistrer\(\);"),
        "la methode qui ecrit un champ enregistre dans la foulee");
}
finally
{
    if (sauvegarde is null) File.Delete(chemin);
    else File.WriteAllText(chemin, sauvegarde);
}

// --- 5. L etat valide remonte dans TOUS les ecrans, depuis une seule source
var racineVues = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Components"));
var fichiersVues = Directory.GetFiles(racineVues, "*.razor", SearchOption.AllDirectories);
var vue2 = string.Join(Environment.NewLine, fichiersVues.Select(File.ReadAllText));
Exiger(fichiersVues.Length >= 4, $"les ecrans sont bien decoupes en composants ({fichiersVues.Length} vues)");

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
Exiger(Regex.IsMatch(vue2, @"valide \? ""Validee"""),
    "la grille de resultats affiche Validee pour un dossier valide");
Exiger(Regex.IsMatch(vue2, @"!valide && dem\.EnCours"),
    "un dossier valide n est plus compte en cours dans la grille");
Exiger(Regex.IsMatch(vue2, @"var valide = EstValide\(d\.Numero\);"),
    "la tuile Ma file interroge le meme etat");
Exiger(vue2.Contains("CritEtats.Contains(EtatReel(d))"),
    "le filtre par etat suit la validation, sinon il raterait les dossiers valides");

// --- 6. L ecran Arrivee du courrier a ses PROPRES criteres et colonnes
Exiger(vue2.Contains("case \"PROC_ARRIVEE\":") && vue2.Contains("<EcranArrivee"),
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
Exiger(vue2.Contains("OuvrirDossier.InvokeAsync(a.NumDemande)"),
    "la ligne d arrivee ouvre la demande liee");
Exiger(Regex.IsMatch(vue2, @"Ouvrir\(Arrivee a\)\s*=>\s*string\.IsNullOrWhiteSpace\(a\.NumDemande\)"),
    "une arrivee sans demande ne mene nulle part");
Exiger(vue2.Contains("rig-table__ligne--inerte"),
    "une ligne sans demande se distingue visuellement");
Exiger(vue2.Contains("non rattachee"),
    "la colonne N Demande dit clairement qu il n y a pas de demande");

// --- 8. L ecran Actes numerises : criteres et colonnes de RIG v2
Exiger(vue2.Contains("case \"PROC_ACTES_NUM\":") && vue2.Contains("<EcranActes"),
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
Exiger(vue2.Contains("if (!CritNonNumerisables) q = q.Where(a => a.Numerisable);"),
    "les actes non numerisables sont masques tant que la case n est pas cochee");
Exiger(Regex.IsMatch(vue2, @"Ouvrir\(ActeNum a\)\s*=>\s*!a\.Numerisable"),
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
Exiger(Regex.IsMatch(vue2, @"HistoriqueOuvert = !HistoriqueOuvert"),
    "la commande Historique VGED n est plus decorative : elle ouvre le panneau");

// --- 10. Une procedure non portee le DIT, elle n emprunte pas l ecran des demandes
Exiger(vue2.Contains("case \"PROC_DEMANDE\":"),
    "l ecran des demandes est reserve a PROC_DEMANDE");
Exiger(vue2.Contains("<EcranNonMaquette"),
    "les procedures non portees ouvrent l ecran qui les nomme");
Exiger(!Regex.IsMatch(vue2, @"default:\s*<EcranDemandes"),
    "aucune procedure ne retombe en silence sur l ecran des demandes");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("Saisie guidee : tout est vert.");
    return 0;
}
Console.WriteLine($"Saisie guidee : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
