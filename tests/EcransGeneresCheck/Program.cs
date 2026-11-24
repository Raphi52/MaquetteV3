using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3Desktop.Pont;
using RigV3Desktop.Pont.EcransGeneres;

// Banc des ECRANS GENERIQUES (lot 1 du plan « les 125 ecrans manquants du menu, au style
// Commandes web »). Ce que la compilation ne dit pas, et que ce banc verifie :
//   1. chaque descripteur est FONDE et sain : son code est une entree du menu, son titre le
//      libelle de cette entree, il porte les trois etats qui recoivent les gestes, 6 a 15 lignes
//      anonymes coherentes avec leurs marches, et aucun code interne (PROC_) n'est affiche ;
//   2. il est ATTEIGNABLE : l'aiguillage le monte (branche par defaut), aucun cas de l'aiguillage
//      ne le doublonne, sa teinte et ses icones ont leur classe dans rig-accueil.css, et il sort
//      en JSON aux noms que web/src lit ;
//   3. le verbe OUVRIR a ses onze ecrans (PROC_CJUDINFOG retire du menu : sa fiche RIG v2 dit la
//      procedure supprimee le 22/11/2018) ;
//   4. le compte des entrees du menu SANS ecran NI descripteur est RAPPORTE sans rougir : les
//      autres lots tournent encore. PROC_CREANCE est exclu (rien dans RIG v2, motif etabli).

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Note(string quoi) => Console.WriteLine("NOTE : " + quoi);

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string Lire(string relatif) => File.ReadAllText(Path.Combine(racine, relatif));
var aiguillage = Lire("web/src/plan/Aiguillage.tsx");
var ecranGenere = Lire("web/src/ecrans/EcranGenere.tsx");
var planTravailVue = Lire("Pont/PlanTravailVue.cs");
var css = Lire("wwwroot/rig-accueil.css");
// Les teintes 2, 3, 6, 7 vivent dans rig-ecrans-generes.css (la reference ne se modifie pas) ;
// elles ne comptent que si index.html lie ce fichier.
var cssGenere = Lire("wwwroot/rig-ecrans-generes.css");
var cssGenereLie = Regex.IsMatch(Lire("wwwroot/index.html"), @"href=""rig-ecrans-generes\.css""");

// ============================================================ 1. LE MENU ET L'AIGUILLAGE

var entrees = MenuAccueil.Moments.SelectMany(m => m.Sous).SelectMany(s => s.Entrees).ToList();
var codesMenu = entrees.Select(e => e.Code).Distinct().ToList();
var libelleDuCode = entrees.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().Libelle);
var casAiguillage = Regex.Matches(aiguillage, @"case '([A-Z_0-9]+)':").Select(m => m.Groups[1].Value).ToHashSet();
Exiger(codesMenu.Count >= 150, $"le menu expose ses procedures ({codesMenu.Count} codes distincts, {entrees.Count} entrees)");

// L'aiguillage monte le descripteur par sa branche par defaut, et seulement la.
Exiger(aiguillage.Contains("import { EcranGenere } from '../ecrans/EcranGenere'")
       && aiguillage.Contains("if (ecran.descripteur) return <EcranGenere key={proc.code} descripteur={ecran.descripteur} />"),
    "l'aiguillage monte EcranGenere avec le descripteur porte par la procedure");
// Les trois etats qui portent les gestes ont le meme nom cote ecran et cote descripteur.
foreach (var etat in new[] { "AFaire", "EnCours", "Validee" })
    Exiger(ecranGenere.Contains($"'{etat}'"), $"EcranGenere.tsx connait l'etat « {etat} » que portent les descripteurs");
// La vue de procedure porte le descripteur.
Exiger(planTravailVue.Contains("CatalogueGenere.Descripteur(p.Code)"),
    "la vue de procedure (Pont/PlanTravailVue.cs) porte le descripteur de la procedure ouverte");

// ============================================================ 2. LES DESCRIPTEURS

var descripteurs = CatalogueGenere.Tous.OrderBy(d => d.Code, StringComparer.Ordinal).ToList();
var parCode = descripteurs.ToDictionary(d => d.Code);
Note($"{descripteurs.Count} descripteur(s) decouvert(s) par reflexion : {string.Join(", ", descripteurs.Select(d => d.Code))}");

foreach (var d in descripteurs)
{
    var defauts = new List<string>();
    if (!libelleDuCode.TryGetValue(d.Code, out var libelle)) defauts.Add("son code n'est pas une entree du menu");
    else if (d.Titre != libelle) defauts.Add($"son titre « {d.Titre} » n'est pas le libelle du menu « {libelle} »");
    if (casAiguillage.Contains(d.Code)) defauts.Add("un cas de l'aiguillage le doublonne : le descripteur serait mort");

    var etats = d.Etats.Select(e => e.Code).ToHashSet();
    foreach (var etat in new[] { "AFaire", "EnCours", "Validee" })
        if (!etats.Contains(etat)) defauts.Add($"l'etat « {etat} » manque (il porte les gestes)");
    foreach (var etat in d.Etats)
        if (!Regex.IsMatch(css, $@"\.rig-tcw__icone--{Regex.Escape(etat.Icone)}\b"))
            defauts.Add($"l'icone « {etat.Icone} » de « {etat.Libelle} » n'a pas de classe dans rig-accueil.css");
    var classeRang = $@"\.rig-tcw__rang-{d.Rang}\s*\{{";
    if (!Regex.IsMatch(css, classeRang) && !(cssGenereLie && Regex.IsMatch(cssGenere, classeRang)))
        defauts.Add($"la teinte {d.Rang} n'a pas de classe rig-tcw__rang-{d.Rang} dans rig-accueil.css ni dans rig-ecrans-generes.css (lie par index.html)");

    if (d.Marches.Length == 0) defauts.Add("aucune marche");
    if (d.Groupes.Length == 0) defauts.Add("aucun groupe");
    if (d.Lignes.Length < 6 || d.Lignes.Length > 15)
        defauts.Add($"{d.Lignes.Length} lignes de demonstration (6 a 15 attendues)");
    var obligatoires = Enumerable.Range(0, d.Marches.Length).Where(k => d.Marches[k].Obligatoire).ToList();
    foreach (var l in d.Lignes)
    {
        if (!d.Groupes.Contains(l.Groupe)) defauts.Add($"la ligne « {l.Nom} » est dans le groupe absent « {l.Groupe} »");
        if (!etats.Contains(l.Etat)) defauts.Add($"la ligne « {l.Nom} » est dans l'etat absent « {l.Etat} »");
        if (l.Faites.Any(k => k < 0 || k >= d.Marches.Length) || l.Faites.Distinct().Count() != l.Faites.Length)
            defauts.Add($"la ligne « {l.Nom} » franchit une marche hors de l'escalier, ou deux fois la meme");
        // Une ligne est coherente avec ses marches : c'est ce que le geste Valider suppose.
        if (l.Etat == "AFaire" && l.Faites.Length > 0)
            defauts.Add($"la ligne « {l.Nom} » est a faire et a pourtant des marches franchies");
        if (l.Etat == "EnCours" && l.Faites.Length == 0)
            defauts.Add($"la ligne « {l.Nom} » est en cours sans aucune marche franchie");
        if (l.Etat == "Validee" && !obligatoires.All(k => l.Faites.Contains(k)))
            defauts.Add($"la ligne « {l.Nom} » est validee sans toutes ses marches obligatoires");
    }

    // Rien d'interne n'atteint l'ecran : ni titre, ni libelle, ni texte de ligne ou de marche.
    var textes = new List<string> { d.Titre, d.Sujet, d.Recherche, d.Valider }
        .Concat(d.Criteres).Concat(d.Groupes).Concat(d.Etats.Select(e => e.Libelle))
        .Concat(d.Marches.SelectMany(m => new[] { m.Titre, m.Consigne }.Concat(m.Champs)))
        .Concat(d.Lignes.SelectMany(l => new[] { l.Nom, l.Precisions }));
    if (textes.Any(t => t.Contains("PROC_"))) defauts.Add("un code interne (PROC_) est affiche a l'ecran");

    if (defauts.Count == 0)
        Exiger(true, $"{d.Code} ({d.Titre}) : sain, {d.Lignes.Length} lignes, {d.Marches.Length} marches, {d.Groupes.Length} groupes");
    foreach (var defaut in defauts) Exiger(false, $"{d.Code} : {defaut}");
}

// Le verbe OUVRIR : ses onze ecrans. PROC_CJUDINFOG a quitte le menu (voir DomaineOuvrir).
var ouvrir = new[] { "PROC_XAFF", "PROC_NF", "PROC_NPC", "PROC_IPE", "PROC_MESIN", "PROC_JOIN",
                     "PROC_CORDJRCS", "PROC_DAS", "PROC_DEP", "PROC_DEPPCM", "PROC_DASBEN" };
Exiger(ouvrir.All(parCode.ContainsKey),
    $"le verbe Ouvrir a ses onze ecrans generiques ({ouvrir.Count(parCode.ContainsKey)}/{ouvrir.Length})");

// Ce que web/src lit : les noms camelCase du descripteur, tels que la vue les envoie.
if (descripteurs.Count > 0)
{
    var json = JsonSerializer.Serialize(descripteurs[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
    foreach (var cle in new[] { "code", "titre", "sujet", "recherche", "criteres", "valider", "rang", "etats",
                                "groupes", "marches", "lignes", "icone", "obligatoire", "faites" })
        Exiger(json.Contains($"\"{cle}\":"), $"le descripteur sort en JSON avec la cle « {cle} »");
}

// ============================================================ 3. LE RESTE A FAIRE (rapporte, sans bloquer)

var sansEcran = codesMenu
    .Where(c => c != "PROC_CREANCE" && !casAiguillage.Contains(c) && !parCode.ContainsKey(c))
    .ToList();
Note($"sans ecran ni descripteur : {string.Join(", ", sansEcran)}");
// PROC_CJUDINFOG (fiche RIG v2 « OBSOLETE », factorisee dans PROC_CJUD) a quitte le menu le
// 2026-11-23 : s'il y revenait sans ecran, il compterait ici comme un reste a faire.
Exiger(!codesMenu.Contains("PROC_CJUDINFOG"), "PROC_CJUDINFOG (obsolete, factorise dans PROC_CJUD) n'est plus au menu");

var bilan = $"RESTANTS : {sansEcran.Count} entree(s) du menu sans ecran ni descripteur (PROC_CREANCE exclu, rapporte sans bloquer)";
if (echecs.Count == 0)
{
    Console.WriteLine("VERT : les descripteurs generiques tiennent leurs regles. " + bilan);
    return 0;
}
Console.WriteLine($"ROUGE : {echecs.Count} regle(s) non tenue(s). " + bilan);
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
