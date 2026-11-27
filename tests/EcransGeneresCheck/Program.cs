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
//      des metiers differents voient (Metier/PerimetreProfil.cs) ;
//   7. les ECRANS REUNIS EN ONGLETS (25/11/2026, conv-2147 : les ecrans faits a la main en React,
//      EntreeMenu.EnOnglets) : chaque procedure y garde SON ecran, un onglet chacune, sous la meme
//      regle du metier ; ouverte par son code (la recherche), elle mene a l'ecran reuni, sur SON onglet ;
//   8. LES FORMES (25/11/2026, plan « RIG v3 100 % fonctionnel », lot 1) : tout descripteur a une forme ; seules Saisie
//      et Operation ont des etapes ; une Fiche a des champs editables ; un Referentiel a des colonnes ; un Etat, des
//      parametres et des colonnes ; un ecran reuni prend la forme de ses procedures ou passe en onglets.

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
// Les formes (25/11/2026) : EcranGenere.tsx aiguille, formes/commun.tsx porte les etats et le refus des gestes.
var formesCommun = Lire("web/src/ecrans/formes/commun.tsx");
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
// Les ecrans reunis en LISTE (un descripteur fabrique, sections 2 et 6) et ceux EN ONGLETS (section 7).
var enListe = reunions.Where(e => !e.EnOnglets).ToList();
var enOnglets = reunions.Where(e => e.EnOnglets).ToList();
var casAiguillage = Regex.Matches(aiguillage, @"case '([A-Z_0-9]+)':").Select(m => m.Groups[1].Value).ToHashSet();
Exiger(libelleDuCode.Count >= 150,
    $"le menu expose ses procedures ({libelleDuCode.Count} codes atteignables, dont {libelleDuCode.Count - codesMenu.Count} reunis ; {entrees.Count} entrees affichees)");
// Mesure du 24/11/2026 (conv-2142) : 170 entrees affichees avant, 107 apres la premiere passe, 99 apres
// la seconde. Le 25/11/2026 (conv-2147), les ecrans portes en React d'un meme metier sont reunis en
// onglets : 94. Ce qui reste a part est une procedure vue par d'autres metiers que ses voisines. Le
// plafond tient le resserrement : un ecran de plus au menu se reunit d'abord a ses voisins, ou ce
// plafond se discute.
Exiger(reunions.Count >= 33 && entrees.Count <= 95,
    $"le menu est resserre : {reunions.Count} ecrans reunissent plusieurs procedures ({enOnglets.Count} en onglets), {entrees.Count} entrees affichees (170 avant le 24/11/2026)");

// L'aiguillage monte le descripteur par sa branche par defaut, et seulement la.
Exiger(aiguillage.Contains("import { EcranGenere } from '../ecrans/EcranGenere'")
       && aiguillage.Contains("if (ecran.descripteur) return <EcranGenere key={proc.code} descripteur={ecran.descripteur} />"),
    "l'aiguillage monte EcranGenere avec le descripteur porte par la procedure");
// Les trois etats qui portent les gestes ont le meme nom cote ecran et cote descripteur.
foreach (var etat in new[] { "AFaire", "EnCours", "Validee" })
    Exiger(formesCommun.Contains($"'{etat}'"), $"les formes (formes/commun.tsx) connaissent l'etat « {etat} » que portent les descripteurs");
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
    if (d.Parties.Length != 0) defauts.Add("un ecran reuni n'a ni etapes ni sections propres : chaque ligne suit celles de sa procedure");
    if (d.Lignes.Length != d.Membres.Sum(m => m.Lignes.Length)) defauts.Add("ses lignes ne sont pas exactement celles de ses procedures");
    if (casAiguillage.Contains(d.Code)) defauts.Add("un cas de l'aiguillage le doublonne : le descripteur serait mort");
    foreach (var etat in new[] { "AFaire", "EnCours", "Validee" })
        if (d.Etats.All(e => e.Code != etat)) defauts.Add($"l'etat « {etat} » manque (il porte les gestes)");
    foreach (var l in d.Lignes)
    {
        var parcours = d.Membres.FirstOrDefault(m => m.Titre == l.Groupe);
        if (parcours is null) { defauts.Add($"la ligne « {l.Nom} » n'appartient a aucune procedure reunie"); continue; }
        if (l.Faites.Any(k => k < 0 || k >= parcours.Parties.Length))
            defauts.Add($"la ligne « {l.Nom} » franchit une partie hors de celles de sa procedure");
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

// Chaque procedure reunie EN LISTE a son descripteur, et toute procedure n'est reunie qu'une fois (sinon deux
// ecrans se la disputent). Celles reunies en onglets gardent leur ecran : section 7.
var reunies = reunions.SelectMany(e => e.Membres.Select(m => (Reunion: e.Code, m.Code))).ToList();
var reuniesEnListe = enListe.SelectMany(e => e.Membres.Select(m => (Reunion: e.Code, m.Code))).ToList();
var sansDescripteur = reuniesEnListe.Where(r => !parCode.TryGetValue(r.Code, out var x) || x.Membres.Length > 0).ToList();
Exiger(sansDescripteur.Count == 0,
    $"chaque procedure reunie en liste a son propre descripteur ({reuniesEnListe.Count} procedures dans {enListe.Count} ecrans)"
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
    // fix-ok: cause mesuree (25/11/2026, capture de Raphael sur « Corriger une affaire ») -- cette regle exigeait des
    // marches de TOUT descripteur (« aucune action : pas d'etape a franchir ») : le banc imposait l'escalier partout.
    // Un descripteur a maintenant des parties (etapes d'une Saisie, sections des autres formes) ; la forme, section 8.
    if (d.Parties.Length == 0 || string.IsNullOrWhiteSpace(d.Valider))
        defauts.Add("aucune partie (ni etape ni section), ou pas de bouton final");
    if (d.Groupes.Length == 0) defauts.Add("aucun groupe");
    if (d.Lignes.Length == 0) defauts.Add("aucune ligne de demonstration");
    else if (d.Lignes.Length < 6 || d.Lignes.Length > 15)
        defauts.Add($"{d.Lignes.Length} lignes de demonstration (6 a 15 attendues)");
    var obligatoires = Enumerable.Range(0, d.Parties.Length).Where(k => d.Parties[k].Obligatoire).ToList();
    foreach (var l in d.Lignes)
    {
        if (!d.Groupes.Contains(l.Groupe)) defauts.Add($"la ligne « {l.Nom} » est dans le groupe absent « {l.Groupe} »");
        if (!etats.Contains(l.Etat)) defauts.Add($"la ligne « {l.Nom} » est dans l'etat absent « {l.Etat} »");
        if (l.Faites.Any(k => k < 0 || k >= d.Parties.Length) || l.Faites.Distinct().Count() != l.Faites.Length)
            defauts.Add($"la ligne « {l.Nom} » franchit une partie hors de l'ecran, ou deux fois la meme");
        if (l.Valeurs.Length > d.Colonnes.Length)
            defauts.Add($"la ligne « {l.Nom} » a {l.Valeurs.Length} cellules pour {d.Colonnes.Length} colonnes");
        // Une ligne est coherente avec ses ETAPES : c'est ce que le bouton final d'une Saisie suppose. Les sections d'une
        // autre forme ne se franchissent pas -- la fiche s'ouvre entiere, aucune forme ne lit Faites (lot 1, 25/11/2026) :
        // retirer la recherche d'une fiche laissait « en cours sans marche » des lignes dont elle etait la seule faite.
        if (d.Marches.Length == 0) continue;
        if (l.Etat == "AFaire" && l.Faites.Length > 0)
            defauts.Add($"la ligne « {l.Nom} » est a faire et a pourtant des marches franchies");
        if (l.Etat == "EnCours" && l.Faites.Length == 0)
            defauts.Add($"la ligne « {l.Nom} » est en cours sans aucune marche franchie");
        if (l.Etat == "Validee" && !obligatoires.All(k => l.Faites.Contains(k)))
            defauts.Add($"la ligne « {l.Nom} » est validee sans toutes ses marches obligatoires");
    }

    // Rien d'interne n'atteint l'ecran : ni titre, ni libelle, ni texte de ligne ou de marche.
    var textes = new List<string> { d.Titre, d.Sujet, d.Recherche, d.Valider }
        .Concat(d.Criteres).Concat(d.Groupes).Concat(d.Etats.Select(e => e.Libelle)).Concat(d.Gestes)
        .Concat(d.Parties.Concat(d.Resultat).SelectMany(m => new[] { m.Titre, m.Consigne }.Concat(m.Champs.SelectMany(c => c.Options.Prepend(c.Libelle)))))
        .Concat(d.Colonnes.Concat(d.Parametres).SelectMany(c => c.Options.Prepend(c.Libelle)))
        .Concat(d.Lignes.SelectMany(l => new[] { l.Nom, l.Precisions }.Concat(l.Valeurs)));
    if (textes.Any(t => t.Contains("PROC_"))) defauts.Add("un code interne (PROC_) est affiche a l'ecran");

    if (defauts.Count == 0)
        Exiger(true, $"{d.Code} ({d.Titre}) : sain, forme {d.Forme}, {d.Lignes.Length} lignes, {d.Marches.Length} etapes, {d.Sections.Length} sections, {d.Groupes.Length} groupes");
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
                                "groupes", "marches", "lignes", "icone", "obligatoire", "faites", "membres",
                                "forme", "sections", "colonnes", "parametres", "gestes", "enOnglets", "objetUnique", "resultat", "libelle", "type", "options", "valeurs" })
        Exiger(json.Contains($"\"{cle}\":"), $"le descripteur sort en JSON avec la cle « {cle} »");
    // La forme et le type d'un champ sortent en TEXTE (les noms que web/src/plan/types.ts attend), pas en nombre.
    Exiger(Regex.IsMatch(json, "\"forme\":\"" + descripteurs[0].Forme + "\"") && Regex.IsMatch(json, "\"type\":\"[A-Z][a-z]+\""),
        $"la forme et le type des champs sortent en texte (« forme »:« {descripteurs[0].Forme} »)");
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

// Un ecran reuni en onglets n'a ni cas ni descripteur : l'ecran a onglets le porte (section 7 le verifie).
var sansEcran = codesMenu.Where(c => !casAiguillage.Contains(c) && !parCode.ContainsKey(c) && enOnglets.All(e => e.Code != c)).ToList();
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

// ============================================================ 7. LES ECRANS REUNIS EN ONGLETS
// fix-ok: cause mesuree (25/11/2026, conv-2147) -- les sections 2 et 6 exigeaient un descripteur de chaque
// procedure reunie : un ecran fait a la main n'en a pas, les ecrans reunis en onglets ont donc leurs propres regles ici.

// L'aiguillage les monte AVANT son switch (un ecran reuni n'a pas de cas a lui), la vue de procedure porte leurs onglets.
var avantSwitch = aiguillage.IndexOf("if (ecran.reunis)", StringComparison.Ordinal);
Exiger(aiguillage.Contains("import { EcranOnglets } from '../ecrans/EcranOnglets'") && avantSwitch >= 0
       && avantSwitch < aiguillage.IndexOf("switch (proc.code)", StringComparison.Ordinal) && aiguillage.Contains("<EcranOnglets "),
    "l'aiguillage monte EcranOnglets pour un ecran reuni en onglets, avant d'aiguiller par code");
Exiger(planTravailVue.Contains("VueOngletsReunis(p.Code)"), "la vue de procedure (Pont/PlanTravailVue.cs) porte les onglets de l'ecran reuni");

// Le metier d'un ecran : ses domaines (Metier/PerimetreProfil.cs), vide s'il est transverse.
string MetiersDe(string code) => string.Join(",", PerimetreProfil.Reserve(code).OrderBy(x => x, StringComparer.Ordinal));
foreach (var e in enOnglets)
{
    var defauts = new List<string>();
    if (e.Membres.Length < 2) defauts.Add("un ecran reuni a au moins deux onglets");
    if (parCode.ContainsKey(e.Code)) defauts.Add("il a un descripteur de liste : ses onglets seraient morts");
    if (casAiguillage.Contains(e.Code)) defauts.Add("un cas de l'aiguillage le doublonne : ses onglets seraient morts");
    // Chaque onglet monte l'ecran de SA procedure : fait a la main (un cas de l'aiguillage), ou generique (son descripteur).
    foreach (var m in e.Membres.Where(m => !casAiguillage.Contains(m.Code) && !(parCode.TryGetValue(m.Code, out var d) && d.Membres.Length == 0)))
        defauts.Add($"l'onglet {m.Code} ({m.Libelle}) n'a pas d'ecran a monter");
    // Des ecrans tous generiques se reunissent en une liste commune (section 6) : les onglets sont pour les ecrans
    // faits a la main, qui ont leur propre logique.
    if (!e.Membres.Any(m => casAiguillage.Contains(m.Code)))
        defauts.Add("aucun onglet n'est un ecran fait a la main : une liste commune les reunit (EnOnglets a retirer)");
    // Meme regle du metier que les ecrans reunis en liste : aucun onglet qu'un agent ne verrait pas seul.
    foreach (var m in e.Membres.Where(m => MetiersDe(m.Code) != MetiersDe(e.Code)))
        defauts.Add($"{m.Code} est vu par [{MetiersDe(m.Code)}], l'ecran qui le reunit par [{MetiersDe(e.Code)}]");
    if (defauts.Count == 0)
        Exiger(true, $"{e.Code} ({e.Libelle}) : {e.Membres.Length} onglets ({string.Join(", ", e.Membres.Select(m => m.Libelle))}), metier [{MetiersDe(e.Code)}]");
    foreach (var defaut in defauts) Exiger(false, $"{e.Code} : {defaut}");
}

// Les trois familles du 25/11/2026 (conv-2147). Leurs voisines reservees a un metier restent a part : les documents
// judiciaires et les statistiques de la cour d'appel (judiciaire), les non-inscrits (RCS).
var familles = new Dictionary<string, string[]>
{
    ["SYNTHESE_DOCUMENTS_NUMERISES"] = ["PROC_DOC_DEMAT", "PROC_ACTES_NUM", "PROC_CONTROLE_VIDIGED", "PROC_COFFRE_FORT"],
    ["SYNTHESE_CLIENTS"] = ["PROC_GCLIENT", "PROC_FCLIENT"],
    ["SYNTHESE_STATISTIQUES"] = ["PROC_STAT", "PROC_XPILOTAGE"],
};
foreach (var (code, membres) in familles)
    Exiger(enOnglets.FirstOrDefault(e => e.Code == code) is { } r && r.Membres.Select(m => m.Code).SequenceEqual(membres),
        $"{code} reunit en onglets {string.Join(", ", membres)}");
Exiger(new[] { "PROC_GEDJUD", "PROC_STATCA", "PROC_CNI" }.All(c => codesMenu.Contains(c) && MenuAccueil.SyntheseDe(c) is null),
    "les documents judiciaires, les statistiques de la cour d'appel et les non-inscrits gardent leur entree au menu");

// Le chemin de l'utilisateur, joue sur la session comme la page le remonte (SessionPlanTravail.Agir) : « Fiche client »
// cherchee ouvre l'ecran reuni des clients, sur SON onglet. Bac a sable : JAMAIS l'etat reel de l'utilisateur.
var etatAvant = Environment.GetEnvironmentVariable("RIGV3_ETAT");
var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-ecransgeneres-" + Guid.NewGuid().ToString("N") + ".json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
try
{
    var session = new SessionPlanTravail(null, null);
    session.Initialiser();
    void Agir(string geste, object? valeur = null) => session.Agir(geste, JsonSerializer.SerializeToElement(valeur));
    ProcVue? Proc() => session.Vue().Proc;

    Agir("paletteOuvrir");
    Agir("paletteTexte", "Fiche client");
    Exiger(session.Vue().Palette?.Ecrans.FirstOrDefault() is { Code: "PROC_FCLIENT" },
        "la recherche trouve « Fiche client » par son nom, en tete des ecrans");
    Agir("paletteAller", new { nature = "ecran", cle = "PROC_FCLIENT" });
    Exiger(Proc() is { Code: "SYNTHESE_CLIENTS", Libelle: "Comptes clients" } p && p.Ecran.Descripteur is null
           && p.Ecran.Reunis is { Actif: "PROC_FCLIENT" } r && r.Onglets.Select(o => o.Code).SequenceEqual(["PROC_GCLIENT", "PROC_FCLIENT"]),
        $"« Fiche client » cherchee ouvre l'ecran reuni des clients, onglet « Fiche client » actif ({Proc()?.Code}, {Proc()?.Ecran.Reunis?.Actif})");
    Exiger(session.Vue().Onglets?.Count(o => o.Code is "SYNTHESE_CLIENTS" or "PROC_GCLIENT" or "PROC_FCLIENT") == 1,
        "un seul onglet du haut pour l'ecran reuni, pas un par procedure");

    // Un onglet choisi dans l'ecran est retenu ; un code qui n'est pas un onglet (une procedure reunie en LISTE) est ignore.
    Agir("ongletReuni", "PROC_GCLIENT");
    Exiger(Proc()?.Ecran.Reunis?.Actif == "PROC_GCLIENT", "un onglet choisi dans l'ecran reste montre quand il est redessine");
    Agir("ongletReuni", "PROC_MMA");
    Exiger(Proc()?.Ecran.Reunis?.Actif == "PROC_GCLIENT", "un code hors des onglets ne change pas l'onglet montre");

    // Par son code d'ailleurs (favori, onglet retenu, alerte : le geste ouvrirCode) : son onglet aussi.
    Agir("ouvrirCode", "PROC_FCLIENT");
    Exiger(Proc() is { Code: "SYNTHESE_CLIENTS", Ecran.Reunis.Actif: "PROC_FCLIENT" }, "« Fiche client » ouverte par son code active son onglet");

    // Par l'entree du menu : le premier onglet ; un onglet generique porte SON descripteur, l'ecran fait a la main aucun.
    Agir("ouvrirCode", "SYNTHESE_STATISTIQUES");
    Exiger(Proc() is { Code: "SYNTHESE_STATISTIQUES", Ecran.Reunis: { Actif: "PROC_STAT" } s }
           && s.Onglets[0].Descripteur is null && s.Onglets[1].Descripteur?.Code == "PROC_XPILOTAGE",
        "l'entree « Statistiques et pilotage » ouvre le premier onglet ; Pilotage porte son descripteur, Statistiques aucun");
    // Un ecran qui n'est pas reuni en onglets n'en porte pas.
    Agir("ouvrirCode", "PROC_MMA");
    Exiger(Proc() is { Code: "SYNTHESE_CORRIGER_RCS", Ecran.Reunis: null }, "un ecran reuni en liste ne porte pas d'onglets");

    // Un ecran reuni en onglets PAR SES FORMES (CatalogueGenere.Reunir, EnOnglets du descripteur : Journaux et lettrage
    // y met des etats, une saisie et un lettrage) suit la meme regle. Avant le 25/11/2026, son onglet se tenait dans la
    // page et partait toujours du premier : « Livre de repertoire » cherche montrait le journal de caisse (objection
    // du controle final, lot 1 du plan « RIG v3 100 % fonctionnel »). Sa vue porte les onglets -- aucun descripteur de
    // liste, qui laisserait la page choisir --, chacun le descripteur de sa procedure.
    Agir("ouvrirCode", "PROC_JLR");
    Exiger(Proc() is { Code: "SYNTHESE_JOURNAUX", Ecran: { Descripteur: null, Reunis: { Actif: "PROC_JLR" } j } }
           && j.Onglets.Select(o => o.Code).SequenceEqual(["PROC_JCAISSE", "PROC_JVENTE", "PROC_JLR", "PROC_VLETTRAGE"])
           && j.Onglets.All(o => o.Descripteur?.Code == o.Code),
        $"« Livre de repertoire » ouvert par son code montre SON onglet dans « Journaux et lettrage » ({Proc()?.Code}, onglet {Proc()?.Ecran.Reunis?.Actif ?? "tenu par la page"})");
    Agir("ongletReuni", "PROC_VLETTRAGE");
    Exiger(Proc()?.Ecran.Reunis?.Actif == "PROC_VLETTRAGE", "un onglet choisi dans un ecran reuni par ses formes reste montre");
    Agir("paletteOuvrir");
    Agir("paletteTexte", "Tiers payeurs");
    Agir("paletteAller", new { nature = "ecran", cle = "PROC_GESTTIERSPAYEURS" });
    Exiger(Proc() is { Code: "SYNTHESE_PAIEMENTS", Ecran.Reunis.Actif: "PROC_GESTTIERSPAYEURS" },
        $"« Tiers payeurs » cherche ouvre « Paiements » sur son onglet ({Proc()?.Ecran.Reunis?.Actif ?? "tenu par la page"})");

    // La fiche des touches (Pont/EcranCorrespondances.cs, EcranV3De) suit la meme regle. Elle ne lisait que le drapeau du
    // menu : la touche Entree du journal de caisse s'essayait dans « Journaux et lettrage » entier (l'onglet deja montre),
    // et l'aide « ? » de l'onglet, qui demande les touches de l'onglet actif (Procedure.tsx), ne la montrait plus.
    // Reunie en LISTE, une procedure s'essaie toujours dans l'ecran qui la reunit (le jumeau, Actes bloques).
    var toucheCaisse = EcranCorrespondances.Fiche.FirstOrDefault(l => l.EcranV2 == "Journal de caisse");
    Exiger(toucheCaisse is { EcranV3: "PROC_JCAISSE" } && EcranCorrespondances.DeLEcran("PROC_JCAISSE").Contains(toucheCaisse),
        $"la touche du « Journal de caisse » s'essaie dans SON onglet, et l'aide « ? » de l'onglet la montre (ecran {toucheCaisse?.EcranV3 ?? "aucun"})");
    Agir("ongletReuni", "PROC_JVENTE");
    Agir("ouvrirCode", toucheCaisse?.EcranV3);
    Exiger(Proc() is { Code: "SYNTHESE_JOURNAUX", Ecran.Reunis.Actif: "PROC_JCAISSE" },
        $"« Essayer » la touche du journal de caisse ouvre « Journaux et lettrage » sur son onglet ({Proc()?.Code}, onglet {Proc()?.Ecran.Reunis?.Actif})");
    Exiger(EcranCorrespondances.EcranV3De("PROC_ACTRET") == "SYNTHESE_ACTES_BLOQUES",
        $"une procedure reunie en liste s'essaie dans l'ecran qui la reunit (PROC_ACTRET : {EcranCorrespondances.EcranV3De("PROC_ACTRET")})");
}
finally
{
    Environment.SetEnvironmentVariable("RIGV3_ETAT", etatAvant);
    try { File.Delete(etatBanc); } catch (IOException) { }
}

// ============================================================ 8. LES FORMES (plan « RIG v3 100 % fonctionnel », lot 1, 25/11/2026)
// Chaque ecran a l'interface de sa fonction (Descripteur.cs, FormeEcran) au lieu d'un escalier unique. Ce que la
// compilation ne dit pas : la forme est la bonne pour ce que l'ecran porte, et l'ecran sait la monter.

var formesReact = Enum.GetValues<FormeEcran>();
foreach (var f in formesReact)
{
    var fichier = $"web/src/ecrans/formes/Forme{f}.tsx";
    Exiger(File.Exists(Path.Combine(racine, fichier)) && Lire(fichier).Contains($"export function Forme{f}(")
           && ecranGenere.Contains($"case '{f}': return <Forme{f} "),
        $"la forme {f} a son composant ({fichier}) et l'aiguilleur EcranGenere.tsx le monte");
}
// L'escalier n'existe plus : ni « Terminer l'etape », ni compteur n/m sur un bouton final, dans aucune forme.
var sourcesFormes = Directory.GetFiles(Path.Combine(racine, "web/src/ecrans/formes"), "*.tsx").Where(p => !p.EndsWith(".test.tsx"))
    .Select(File.ReadAllText).Append(ecranGenere).ToList();
Exiger(sourcesFormes.All(s => !s.Contains("Terminer l’étape") && !s.Contains("Terminer l'étape") && !s.Contains("rig-tcw__valider-compte")),
    "aucune forme ne pose l'escalier (« Terminer l'etape », Valider n/m)");
// Tout geste sans ecrivain le dit : la phrase exacte, posee une fois, que chaque forme reprend.
Exiger(formesCommun.Contains("export const DEMONSTRATION = 'Démonstration : rien n’est enregistré'"),
    "les gestes disent « Demonstration : rien n'est enregistre » tant qu'aucun ecrivain n'existe (formes/commun.tsx)");

// La section qui refait la recherche de RIG v2 : titree comme elle, ou OBLIGATOIRE et faite des seuls criteres de l'ecran
// (l'escalier ne passait pas sans avoir retrouve l'objet). fix-ok: mesure (banc rouge, 11 echecs) -- sans « obligatoire »,
// la regle prenait pour une recherche « Banques et modes » de PROC_PARAMEXPRESSO, facultative : l'association des modes de
// paiement a leur banque, qu'on modifie (proc_paramexpresso.md:6, MODE_PAIEMENT_BANQUE), dont l'ecran a tire ses criteres.
static bool RefaitLaRecherche(DescripteurEcran d, MarcheEcran s)
{
    var saisis = s.Champs.Where(c => c.Type != TypeChamp.Tableau).Select(c => c.Libelle).ToList();
    return s.Titre is "Critères" or "Recherche" || (s.Obligatoire && saisis.Count > 0 && saisis.All(d.Criteres.Contains));
}

var ParForme = descripteurs.Where(d => d.Membres.Length == 0).GroupBy(d => d.Forme).ToDictionary(g => g.Key, g => g.Count());
Note("formes des ecrans d'une procedure : " + string.Join(", ", ParForme.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));
foreach (var d in descripteurs)
{
    var defauts = new List<string>();
    var reuni = d.Membres.Length > 0;
    if (!Enum.IsDefined(d.Forme)) defauts.Add($"forme inconnue ({(int)d.Forme})");
    // Seule une Saisie (et une Operation, en trois temps au plus) a des ETAPES : les autres posent des sections.
    if (d.Marches.Length > 0 && d.Forme is not (FormeEcran.Saisie or FormeEcran.Operation))
        defauts.Add($"forme {d.Forme} avec {d.Marches.Length} etapes : seules Saisie et Operation ont des etapes");
    if (d.Forme == FormeEcran.Operation && d.Marches.Length > 3) defauts.Add($"une Operation a {d.Marches.Length} etapes (3 au plus)");
    if (d.Marches.Length > 0 && d.Sections.Length > 0) defauts.Add("des etapes ET des sections : l'un ou l'autre");
    // Un seul objet, RECU par la fiche RIG v2, ne vaut que pour une Operation (PROC_IA et sa liasse) : une file se traite
    // en lot. Il se designe par son numero, dans le SEUL critere de l'ecran -- aucune liste ou le choisir (controle final
    // du lot 1, 25/11/2026 : la liasse de PROC_IA se cochait dans une liste de sept).
    if (d.ObjetUnique && d.Forme != FormeEcran.Operation) defauts.Add($"objet unique sur une forme {d.Forme} : seule une Operation en a");
    if (d.ObjetUnique && d.Criteres.Length != 1)
        defauts.Add($"objet unique designe par {d.Criteres.Length} criteres : un seul, son numero (la fiche RIG v2 le recoit)");
    // Le resultat du geste (2e temps, apres lui) n'existe que pour une operation sur un objet unique.
    if (d.Resultat.Length > 0 && !d.ObjetUnique) defauts.Add("un resultat apres le geste hors d'une Operation sur un objet unique");
    // Sans ajout ou sans suppression V2 (lot 14 : Dates de travail, Cabinets) : seule la grille d'un Referentiel les offre.
    if ((!d.Ajout || !d.Suppression) && d.Forme != FormeEcran.Referentiel)
        defauts.Add($"ajout ou suppression retire sur une forme {d.Forme} : seule la grille d'un Referentiel les offre");
    // « Dupliquer » et les sections saisies sous une ligne neuve (lot 14 : Tables de reference) : la grille d'un Referentiel seule ;
    // chaque groupe nomme est un groupe de l'ecran, chaque section une de ses sections (sinon la ligne neuve n'en montre aucune).
    if ((d.Duplication || d.SectionsAjout.Count > 0) && d.Forme != FormeEcran.Referentiel)
        defauts.Add($"duplication ou sections d'ajout sur une forme {d.Forme} : seule la grille d'un Referentiel les offre");
    foreach (var (g, titres) in d.SectionsAjout)
    {
        if (!d.Groupes.Contains(g)) defauts.Add($"sections d'ajout du groupe « {g} », absent des groupes de l'ecran");
        foreach (var t in titres.Where(t => d.Sections.All(s => s.Titre != t)))
            defauts.Add($"section d'ajout « {t} » ({g}) absente des sections de l'ecran");
    }
    // Les sections d'une ligne par groupe et les groupes en modification seule (lot 14, 2e reprise : Tables de reference) : la
    // grille d'un Referentiel seule ; chaque groupe et chaque section nommes existent (sinon la ligne ouverte n'en montre aucune).
    if ((d.SectionsParGroupe.Count > 0 || d.GroupesModificationSeule.Length > 0) && d.Forme != FormeEcran.Referentiel)
        defauts.Add($"sections par groupe ou groupes en modification seule sur une forme {d.Forme} : seule la grille d'un Referentiel les offre");
    foreach (var (g, titres) in d.SectionsParGroupe)
    {
        if (!d.Groupes.Contains(g)) defauts.Add($"sections du groupe « {g} », absent des groupes de l'ecran");
        foreach (var t in titres.Where(t => d.Sections.All(s => s.Titre != t)))
            defauts.Add($"section « {t} » du groupe « {g} » absente des sections de l'ecran");
    }
    foreach (var g in d.GroupesModificationSeule.Where(g => !d.Groupes.Contains(g)))
        defauts.Add($"groupe en modification seule « {g} », absent des groupes de l'ecran");
    // Les regles d'affichage d'un champ (lot 14, 4e reprise : un etat de PC dans Tables de reference) : chacune nomme une CASE de sa
    // section, autre que lui (sinon la grille ne lit aucune case : le champ resterait masque, ou jamais bloque). fix-ok: une regle
    // qui nomme autre chose qu'une case de sa section est un champ masque pour toujours -- banc rouge sur une regle « Régime » injectee.
    foreach (var s in d.Marches.Concat(d.Sections))
        foreach (var c in s.Champs)
            foreach (var (regle, cible) in new[] { ("visible si", c.VisibleSi), ("bloque si", c.BloqueSi) })
                if (cible is not null && (cible == c.Libelle || !s.Champs.Any(x => x.Libelle == cible && x.Type == TypeChamp.Case)))
                    defauts.Add($"champ « {c.Libelle} » ({s.Titre}) {regle} « {cible} » : pas une case de sa section");
    if (!reuni)
    {
        switch (d.Forme)
        {
            case FormeEcran.Saisie when d.Marches.Length == 0:
                defauts.Add("une Saisie sans etape"); break;
            case FormeEcran.Fiche when !d.Sections.SelectMany(s => s.Champs).Any(c => c.Type != TypeChamp.Tableau):
                defauts.Add("une Fiche sans champ editable (texte, date, liste, case...)"); break;
            case FormeEcran.Referentiel when d.Colonnes.Length == 0:
                defauts.Add("un Referentiel sans colonne"); break;
            case FormeEcran.Etat when d.Colonnes.Length == 0 || d.Parametres.Length == 0:
                defauts.Add("un Etat sans parametre ou sans colonne"); break;
            case FormeEcran.Audience when d.Parametres.Length == 0:
                defauts.Add("une Audience sans choix de l'audience (parametres)"); break;
            case FormeEcran.File or FormeEcran.Consultation or FormeEcran.Fiche or FormeEcran.Document or FormeEcran.Operation
                when d.Sections.Length == 0:
                defauts.Add($"une forme {d.Forme} sans section"); break;
        }
        // Une Fiche ou une Consultation s'ouvre sur un objet que la liste a DEJA retrouve (panneau des criteres, resultats) :
        // aucune section n'y refait la recherche de RIG v2 -- titree « Criteres » ou « Recherche », ou dont tous les champs
        // saisis sont des criteres de l'ecran. Sinon la fiche redemande l'objet ouvert (« Corriger une affaire » montrait
        // une section « Instance » : numero de role et nom courant, vides ; controle final du lot 1, 25/11/2026).
        if (d.Forme is FormeEcran.Fiche or FormeEcran.Consultation)
            foreach (var s in d.Sections.Where(s => RefaitLaRecherche(d, s)))
                defauts.Add($"la section « {s.Titre} » refait la recherche que la liste a deja faite (criteres : {string.Join(", ", d.Criteres)})");
        // Une liste n'a ses valeurs que pour les colonnes : jamais plus.
        foreach (var c in d.Colonnes.Concat(d.Parametres).Concat(d.Parties.Concat(d.Resultat).SelectMany(s => s.Champs)))
            if ((c.Type == TypeChamp.Liste) != (c.Options.Length > 0))
                defauts.Add($"le champ « {c.Libelle} » : une liste a ses valeurs, et seule une liste en a");
    }
    else
    {
        // Un ecran reuni prend la forme de ses procedures quand elles se reunissent en une liste ; sinon, des onglets.
        var formes = d.Membres.Select(m => m.Forme).Distinct().ToArray();
        var listeCommune = formes.Length == 1 && CatalogueGenere.FormesEnListe.Contains(formes[0]);
        if (d.EnOnglets == listeCommune) defauts.Add($"reunit des formes [{string.Join(", ", formes)}] : EnOnglets devrait valoir {!listeCommune}");
        if (listeCommune && d.Forme != formes[0]) defauts.Add($"forme {d.Forme} au lieu de celle de ses procedures ({formes[0]})");
        // Un objet unique se designe par son numero, sans liste : il ne partage pas celle d'un ecran reuni.
        if (!d.EnOnglets && d.Membres.FirstOrDefault(m => m.ObjetUnique) is { } unique)
            defauts.Add($"{unique.Code} vise un objet unique, sans liste : son ecran reuni passe en onglets");
    }
    if (defauts.Count == 0)
        Exiger(true, $"{d.Code} : forme {d.Forme}{(d.EnOnglets ? $" en onglets ({string.Join(", ", d.Membres.Select(m => m.Forme))})" : "")}");
    foreach (var defaut in defauts) Exiger(false, $"{d.Code} : {defaut}");
}

// Les ecrans de la reception du lot (critere du chef) : chacun a la forme de sa fonction.
Exiger(parCode.GetValueOrDefault("SYNTHESE_CORRIGER_JUD") is { Forme: FormeEcran.Fiche, EnOnglets: false } cj
       && cj.Membres.All(m => m.Sections.SelectMany(s => s.Champs).Any(c => c.Type is TypeChamp.Texte or TypeChamp.Date or TypeChamp.Liste or TypeChamp.Case)),
    "« Corriger une affaire judiciaire » est une Fiche (liste commune), chaque procedure a des champs editables");
Exiger(parCode.GetValueOrDefault("SYNTHESE_RELANCES") is { Forme: FormeEcran.File, EnOnglets: false },
    "« Relances et injonctions » est une File de travail");
Exiger(parCode.GetValueOrDefault("SYNTHESE_PAIEMENTS") is { EnOnglets: true } pai
       && pai.Membres.FirstOrDefault(m => m.Code == "PROC_GESTMODEPAIEMENT") is { Forme: FormeEcran.Referentiel, Colonnes.Length: > 0 },
    "« Modes de paiement » est un Referentiel (grille), dans l'ecran Paiements en onglets");
Exiger(parCode.GetValueOrDefault("PROC_JCAISSE") is { Forme: FormeEcran.Etat } jc
       && jc.Parametres.Any(p => p.Type == TypeChamp.Periode) && jc.Colonnes.Any(c => c.Type == TypeChamp.Montant)
       && jc.Lignes.All(l => l.Valeurs.Length == jc.Colonnes.Length),
    "« Journal de caisse » est un Etat : une periode, une colonne montant a totaliser, ses cellules d'exemple");
// Les deux formes tranchees par le chef apres le controle final du lot 1 (25/11/2026), d'apres leur fiche RIG v2 :
// « Mesure provisoire » modifie une mesure qui EXISTE (proc_mesin.md:19, :177, :211) ; « Analyse IA » lance l'analyse
// d'UNE liasse recue en parametre (proc_ia.md:192), en deux temps (:151-156) -- ni file, ni geste en lot.
Exiger(parCode.GetValueOrDefault("PROC_MESIN") is { Forme: FormeEcran.Fiche, Marches.Length: 0 } mesin
       && mesin.Sections.SelectMany(s => s.Champs).Any(c => c.Type != TypeChamp.Tableau),
    "« Mesure provisoire » est une Fiche (modifier une mesure existante) : des champs editables, aucune etape");
// Analyse IA : la liasse est RECUE (OPE_DOC_DEMAT.cs:818), d'abord le lancement -- rien a saisir avant, la V2 ne fait que
// confirmer (OPE_IA.cs:66) --, ensuite la restitution (Resultat), ou passent les champs verifies, la synthese et l'avis.
Exiger(parCode.GetValueOrDefault("PROC_IA") is { Forme: FormeEcran.Operation, ObjetUnique: true, Gestes.Length: 0, Criteres.Length: 1, Resultat.Length: > 0 } ia
       && ia.Sections.SelectMany(s => s.Champs).All(c => c.Type == TypeChamp.Tableau)
       && parCode.GetValueOrDefault("SYNTHESE_FORMALITES_DEMAT") is { EnOnglets: true },
    "« Analyse IA » est une Operation sur UNE liasse recue par son numero : lancement (rien a saisir), puis resultat ; son ecran reuni passe en onglets");
// Lot 12 (27/11/2026) : a l'ecran, Creer exige un champ rempli dans chaque etape obligatoire (web/src/ecrans/formes/commun.tsx,
// manquantes) -- le banc des gestes, qui poste au serveur, ne passe pas par ce controle. « Ajouter une creance » declarait
// obligatoire l'etape « Integration » et son champ « Erreur d'integration » : Creer refusait toujours (mesure sur le bureau cache).
// Les etapes obligatoires sont les controles d'OPE_CREANCE (PC, creancier, au moins un montant) ; l'integration se lit, sans champ.
Exiger(parCode.GetValueOrDefault("PROC_ADDCREANCE") is { Forme: FormeEcran.Saisie } ajout
       && ajout.Marches.Where(m => m.Obligatoire).Select(m => m.Titre).SequenceEqual(new[] { "Procédure collective", "Créancier", "Montants" })
       && ajout.Marches.Any(m => m.Titre == "Intégration" && !m.Obligatoire && !m.Champs.Any()),
    "« Ajouter une creance » : etapes obligatoires PC, creancier et montants (OPE_CREANCE) ; l'integration se lit, sans champ a saisir");
// Le jumeau, une Operation : le rejet ne fait saisir que ce que son ecrivain lit (EcrivainRejetsEncaissement : Destinataire, Motif).
Exiger(parCode.GetValueOrDefault("PROC_REJET") is { Forme: FormeEcran.Operation } rejet
       && rejet.Sections.Where(s => s.Champs.Any()).Select(s => s.Titre).SequenceEqual(new[] { "Destinataire", "Motif" }),
    "« Rejets d'encaissement » : seuls le destinataire et le motif se saisissent, le cheque choisi se lit sur sa ligne");
// Une section est une PARTIE de l'objet ouvert, jamais la liste qui l'a trouve : chaque forme montre deja ses lignes (liste,
// tableau, file). Une section titree « Resultat(s) » repetait cette liste dans la fiche, ou dans le detail d'une ligne
// (controle final du lot 1, 25/11/2026 : Trouver.cs PROC_REGJUD, PROC_RECHENC, PROC_TARIFS, PROC_BEN, Surveiller.cs PROC_ACTREJ).
// Le jumeau accepte : une section qui nomme ce qu'elle porte (« Resultat par affaire » d'une audience, Surveiller.cs).
// fix-ok: mesure (banc rouge avant correctif, .autowin/tmp/eg-rouge.log) -- 8 sections titrees « Resultat(s) » dans
// 8 descripteurs (ACTREJ, BEN, CFENET_RET, RECHENC, REGJUD, REQUETES, TARIFS, VLETTRAGE) ; ce fichier n'est edite ici
// que pour cette regle (les controles MESIN et IA viennent du commit repris a54c0e4).
foreach (var d in descripteurs.Where(d => d.Membres.Length == 0))
    foreach (var s in d.Parties.Where(s => s.Titre is "Résultat" or "Résultats"))
        Exiger(false, $"{d.Code} : la section « {s.Titre} » repete la liste que la forme {d.Forme} affiche deja");
Exiger(descripteurs.All(d => d.Parties.All(s => s.Titre is not ("Résultat" or "Résultats"))),
    "aucune section ne repete la liste de resultats de sa forme (titree « Resultat » ou « Resultats »)");
Exiger(descripteurs.Any(d => d.Parties.Any(s => s.Titre == "Résultat par affaire")),
    "une section qui nomme ce qu'elle porte (« Resultat par affaire ») reste admise");
// Deux files de traitement sont rangees sous Trouver : les bilans rejetes a renvoyer, les demandes FNIG a envoyer.
string[] filesSousTrouver = ["PROC_REJETS_BILANS", "PROC_FNIG"];
Exiger(descripteurs.Where(d => d.Membres.Length == 0 && MenuAccueil.VerbeDe(d.Code)?.Titre == "Trouver")
                   .All(d => d.Forme is not FormeEcran.File || filesSousTrouver.Contains(d.Code)),
    "les ecrans « Trouver » sont des consultations, etats ou visionneuses (hors " + string.Join(", ", filesSousTrouver) + ")");

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
