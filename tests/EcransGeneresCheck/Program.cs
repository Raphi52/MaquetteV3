using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3Desktop;
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
//   3. le verbe OUVRIR a ses onze ecrans (PROC_CJUDINFOG retire du menu, voir la section 3) ;
//   4. STRICT : aucune entree du menu n'est sans ecran NI descripteur, hors les exceptions
//      nommees en section 3 (chacune porte sa raison ; une exception perimee est rouge) ;
//   5. STRICT : un descripteur VIDE est rouge (ni colonne, ni ligne de demonstration, ni action) ;
//   6. les ECRANS REUNIS (24/11/2026, conv-2142 : « on groupe ce qui peut l'etre ») : chaque
//      entree du menu qui reunit des procedures a un descripteur fabrique de LEURS descripteurs
//      (un groupe chacune, leurs lignes, leurs marches), et ne melange jamais des procedures que
//      des metiers differents voient (Metier/PerimetreProfil.cs).

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
// Une procedure reunie garde son nom et son code : le menu la porte, sous l'ecran qui la reunit.
var libelleDuCode = MenuAccueil.Toutes.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.First().Libelle);
var reunions = entrees.Where(e => e.Membres.Length > 0).DistinctBy(e => e.Code).ToList();
var casAiguillage = Regex.Matches(aiguillage, @"case '([A-Z_0-9]+)':").Select(m => m.Groups[1].Value).ToHashSet();
Exiger(libelleDuCode.Count >= 150,
    $"le menu expose ses procedures ({libelleDuCode.Count} codes atteignables, dont {libelleDuCode.Count - codesMenu.Count} reunis ; {entrees.Count} entrees affichees)");
// Mesure du 24/11/2026 (conv-2142) : 170 entrees affichees avant, 107 apres la premiere passe, 99 apres
// la seconde. Ce qui reste a part est un ecran porte en React (sa propre logique), ou une procedure vue
// par d'autres metiers que ses voisines. Le plafond tient le resserrement : un ecran de plus au menu se
// reunit d'abord a ses voisins, ou ce plafond se discute.
Exiger(reunions.Count >= 30 && entrees.Count <= 100,
    $"le menu est resserre : {reunions.Count} ecrans reunissent plusieurs procedures, {entrees.Count} entrees affichees (170 avant le 24/11/2026)");

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

foreach (var d in descripteurs.Where(d => d.Membres.Length > 0))
{
    // Un ecran REUNI : fabrique a partir de ses procedures (CatalogueGenere.Reunir), rien a la main.
    var defauts = new List<string>();
    var entree = reunions.FirstOrDefault(e => e.Code == d.Code);
    if (entree is null) { Exiger(false, $"{d.Code} : un descripteur a membres sans entree du menu qui reunisse"); continue; }
    if (d.Titre != entree.Libelle) defauts.Add($"son titre « {d.Titre} » n'est pas le libelle du menu « {entree.Libelle} »");
    if (!d.Membres.Select(m => m.Code).SequenceEqual(entree.Membres.Select(m => m.Code)))
        defauts.Add("ses procedures ne sont pas celles que le menu reunit, dans le meme ordre");
    if (!d.Groupes.SequenceEqual(d.Membres.Select(m => m.Titre))) defauts.Add("un groupe par procedure, a son titre, est attendu");
    if (d.Marches.Length != 0) defauts.Add("un ecran reuni n'a pas de marches propres : chaque ligne suit celles de sa procedure");
    if (d.Lignes.Length != d.Membres.Sum(m => m.Lignes.Length)) defauts.Add("ses lignes ne sont pas exactement celles de ses procedures");
    if (casAiguillage.Contains(d.Code)) defauts.Add("un cas de l'aiguillage le doublonne : le descripteur serait mort");
    foreach (var etat in new[] { "AFaire", "EnCours", "Validee" })
        if (d.Etats.All(e => e.Code != etat)) defauts.Add($"l'etat « {etat} » manque (il porte les gestes)");
    foreach (var l in d.Lignes)
    {
        var parcours = d.Membres.FirstOrDefault(m => m.Titre == l.Groupe);
        if (parcours is null) { defauts.Add($"la ligne « {l.Nom} » n'appartient a aucune procedure reunie"); continue; }
        if (l.Faites.Any(k => k < 0 || k >= parcours.Marches.Length))
            defauts.Add($"la ligne « {l.Nom} » franchit une marche hors de l'escalier de sa procedure");
        // L'ancien groupe passe en tete de la precision UNE fois (CatalogueGenere.AvecAncienGroupe).
        var morceaux = l.Precisions.Split(" · ");
        if (morceaux.Length > 1 && morceaux[0] == morceaux[1])
            defauts.Add($"la ligne « {l.Nom} » repete son groupe : « {l.Precisions} »");
    }
    // Le meme metier pour l'ecran et pour chacune de ses procedures : un agent ne voit jamais, dans un
    // ecran reuni, la liste d'une procedure que son profil lui cache.
    string Metiers(string code) => string.Join(",", PerimetreProfil.Reserve(code).OrderBy(x => x, StringComparer.Ordinal));
    foreach (var m in d.Membres.Where(m => Metiers(m.Code) != Metiers(d.Code)))
        defauts.Add($"{m.Code} est vu par [{Metiers(m.Code)}], l'ecran qui le reunit par [{Metiers(d.Code)}]");
    if (defauts.Count == 0)
        Exiger(true, $"{d.Code} ({d.Titre}) : reunit {d.Membres.Length} procedures ({string.Join(", ", d.Membres.Select(m => m.Titre))}), {d.Lignes.Length} lignes, metier [{Metiers(d.Code)}]");
    foreach (var defaut in defauts) Exiger(false, $"{d.Code} : {defaut}");
}

// Chaque procedure reunie a son descripteur, et n'est reunie qu'une fois (sinon deux ecrans se la disputent).
var reunies = reunions.SelectMany(e => e.Membres.Select(m => (Reunion: e.Code, m.Code))).ToList();
var sansDescripteur = reunies.Where(r => !parCode.TryGetValue(r.Code, out var x) || x.Membres.Length > 0).ToList();
Exiger(sansDescripteur.Count == 0,
    $"chaque procedure reunie a son propre descripteur ({reunies.Count} procedures dans {reunions.Count} ecrans)"
    + (sansDescripteur.Count == 0 ? "" : $" ; sans : {string.Join(", ", sansDescripteur.Select(r => $"{r.Code} dans {r.Reunion}"))}"));
var deuxFois = reunies.GroupBy(r => r.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
Exiger(deuxFois.Count == 0 && !reunies.Any(r => codesMenu.Contains(r.Code)),
    "une procedure reunie l'est une seule fois, et n'a plus d'entree a elle au menu"
    + (deuxFois.Count == 0 ? "" : $" (deux fois : {string.Join(", ", deuxFois)})"));
// La fabrique est la seule source : un ecran reuni ne se decrit pas aussi a la main.
Exiger(MenuAccueil.SyntheseDe("PROC_MMA")?.Code == "SYNTHESE_CORRIGER_RCS" && MenuAccueil.SyntheseDe("PROC_FACTURE") is null
       && MenuAccueil.VerbeDe("PROC_MMA")?.Titre == "Modifier",
    "une procedure reunie mene a l'ecran qui la reunit, et prend son verbe (Mentions en marge -> Corriger un dossier RCS, Modifier)");

foreach (var d in descripteurs.Where(d => d.Membres.Length == 0))
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

    // Un descripteur VIDE ne se rend pas : il lui faut des colonnes (les criteres affiches en tete
    // de grille, cf. Surveiller.cs), des lignes de demonstration et des actions (etapes + bouton final).
    if (d.Criteres.Length == 0 || d.Criteres.Any(c => string.IsNullOrWhiteSpace(c)))
        defauts.Add("aucune colonne : la grille n'affiche aucun critere");
    if (d.Marches.Length == 0 || string.IsNullOrWhiteSpace(d.Valider))
        defauts.Add("aucune action : pas d'etape a franchir, ou pas de bouton final");
    if (d.Groupes.Length == 0) defauts.Add("aucun groupe");
    if (d.Lignes.Length == 0) defauts.Add("aucune ligne de demonstration");
    else if (d.Lignes.Length < 6 || d.Lignes.Length > 15)
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
                                "groupes", "marches", "lignes", "icone", "obligatoire", "faites", "membres" })
        Exiger(json.Contains($"\"{cle}\":"), $"le descripteur sort en JSON avec la cle « {cle} »");
}

// ============================================================ 3. ZERO ENTREE SANS ECRAN (strict)
// fix-ok: cause mesuree (main, 23/11) — le banc etait VERT avec 2 entrees du menu sans ecran
// (PROC_CJUDINFOG, PROC_PARAM_LISTEEXCEL), seulement NOTEES, et ne controlait aucun descripteur vide.
// La regle est une assertion maintenant.

// Les entrees du menu qu'on ne porte PAS encore, a dessein. Chacune garde sa raison : tant qu'elle
// est au menu sans ecran, elle est toleree. Une exception qui ne sert plus (le code n'est plus au
// menu, ou il a enfin son ecran) est ROUGE : il faut la retirer de cette liste.
var exceptions = new Dictionary<string, string>
{
    ["PROC_PARAM_LISTEEXCEL"] = "fiche RIG v2 vide, « une coquille, pas une couche OPE » (decision du chef)",
};
foreach (var (code, raison) in exceptions)
    Exiger(codesMenu.Contains(code) && !casAiguillage.Contains(code) && !parCode.ContainsKey(code),
        $"exception {code} perimee : elle n'est plus au menu sans ecran, la retirer de la liste ({raison})");

// Retire du menu le 2026-11-23 par decision de l'utilisateur (fiche RIG v2 « OBSOLETE », supprimee le
// 22/11/2018, factorisee dans PROC_CJUD) : ni au menu, ni en descripteur. S'il revenait, c'est rouge.
const string retire = "PROC_CJUDINFOG";
Exiger(!codesMenu.Contains(retire), $"{retire} (obsolete, factorise dans PROC_CJUD) n'est plus au menu");
Exiger(!parCode.ContainsKey(retire), $"{retire} n'a pas de descripteur : il ne revient pas");

// PROC_CREANCE etait toleree sans ecran (« rien dans la fiche RIG v2 ») jusqu'au 24/11/2026 : FORM_CREANCE n'etait
// qu'un conteneur, et c'est le role qu'il garde -- l'ecran qui reunit les quatre procedures des creances.
Exiger(reunions.FirstOrDefault(e => e.Code == "PROC_CREANCE") is { } creances
       && creances.Membres.Select(m => m.Code).SequenceEqual(["PROC_ADDCREANCE", "PROC_MODCREANCE", "PROC_INTEGCREANCE", "PROC_CREANREP"]),
    "PROC_CREANCE, conteneur vide de RIG v2, reunit les quatre procedures des creances");

var sansEcran = codesMenu.Where(c => !casAiguillage.Contains(c) && !parCode.ContainsKey(c)).ToList();
var tolerees = 0;
foreach (var code in sansEcran)
{
    if (exceptions.ContainsKey(code))
    {
        tolerees++;
        Note($"{code} sans ecran, tolere : {exceptions[code]}");
    }
    else Exiger(false, $"{code} ({libelleDuCode[code]}) : entree du menu sans ecran ni descripteur");
}

// Le compte par verbe du menu : ce que l'utilisateur voit, verbe par verbe.
foreach (var verbe in MenuAccueil.Moments)
    Note($"verbe {verbe.Titre} : {descripteurs.Count(d => MenuAccueil.VerbeDe(d.Code)?.Titre == verbe.Titre)} ecran(s) generique(s)");

var bilan = $"{descripteurs.Count} ecrans generiques, {codesMenu.Count(c => casAiguillage.Contains(c))} entrees portees par un ecran, {tolerees} tolerees (exceptions nommees)";
if (echecs.Count == 0)
{
    Console.WriteLine("VERT : aucune entree du menu sans ecran hors exceptions, descripteurs sains. " + bilan);
    return 0;
}
Console.WriteLine($"ROUGE : {echecs.Count} regle(s) non tenue(s). " + bilan);
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
