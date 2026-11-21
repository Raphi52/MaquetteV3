using System.Text.RegularExpressions;

// Garde-fou du MENU PAR GESTES.
//
// Le menu de gauche ne range plus les ecrans par MODULE de RIG v2 (RCS,
// Judiciaire, Documents...) mais par GESTE : ce qu'on vient faire. Chaque
// geste se coupe en VERBES, et chaque verbe porte les ecrans qui le servent.
// 154 procedures de RIG v2 y sont rangees.
//
// Deux choses se perdent toutes seules quand on ajoute une entree a la main,
// et ce banc les retient :
//
//  1. LE BRUIT TECHNIQUE. Les sources v2 contiennent des bacs a sable
//     (PROC_DEMO, PROC_DEV*, PROC_TESTNLH), des outils de developpeur
//     (PROC_DEBUG_CONTROLS), des lanceurs (*_EXE), des outils de migration
//     et des procedures explicitement obsoletes (PROC_VGED, remplacee par
//     PROC_VDOSSIER ; PROC_MJUDS). Aucune n'a sa place devant un greffier.
//
//  2. LA TAILLE DES VERBES. Un verbe est fait pour se lire d'un coup d'oeil.
//     En dessous de trois ecrans il ne merite pas son titre (autant remonter
//     les lignes d'un cran) ; au-dela de huit, l'oeil balaye au lieu de lire
//     et on retombe sur la liste a rallonge d'avant.
//
// Trois controles de coherence viennent avec : la teinte de chaque geste
// existe bien dans le CSS, un code n'apparait pas deux fois dans le meme
// verbe, et chaque code est atteste par les sources de RIG v2 quand celles-ci
// sont accessibles depuis ce poste (sinon ce controle-la est simplement
// annonce comme non joue -- il ne doit pas rendre le banc rouge sur une
// machine qui n'a pas le depot v2).

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
// Le catalogue du menu a quitte Home.razor pour Pont/MenuAccueil.cs (lot 1 du
// passage a React, 19/11/2026) : le menu est rendu par web/src/accueil/Menu.tsx,
// son etat vit dans Pont/Accueil.cs. Memes regles, lues a la nouvelle adresse.
// Le plan de travail (lot 2) : le @code de Home.razor vit dans Pont/PlanTravail.cs.
var home = Path.Combine(racine, "Pont", "PlanTravail.cs");
var catalogue = Path.Combine(racine, "Pont", "MenuAccueil.cs");
var session = Path.Combine(racine, "Pont", "Accueil.cs");
var css = Path.Combine(racine, "wwwroot", "rig-accueil.css");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Info(string quoi) => Console.WriteLine("NOTE : " + quoi);

Exiger(File.Exists(home), "Pont/PlanTravail.cs (le plan de travail) est trouve");
Exiger(File.Exists(catalogue), "Pont/MenuAccueil.cs (le catalogue du menu) est trouve");
Exiger(File.Exists(session), "Pont/Accueil.cs (l'etat du menu) est trouve");
Exiger(File.Exists(css), "rig-accueil.css est trouve");
if (echecs.Count > 0) { Bilan(); return 1; }

var texte = File.ReadAllText(catalogue);

// Un SEUL catalogue : le menu de l'accueil (la session) et les aiguillages du
// plan de travail (Home.razor, motif d'un ecran a venir) lisent le meme tableau.
Exiger(File.ReadAllText(home).Contains("Moments = Pont.MenuAccueil.Moments;"),
    "le plan de travail relit le catalogue du menu, sans en tenir une copie");
Exiger(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(session), @"MenuAccueil\.Moments").Count >= 3,
    "la session de l'accueil construit le menu depuis ce catalogue");
Exiger(!File.ReadAllText(home).Contains("private readonly Moment[] Moments ="),
    "le plan de travail ne declare pas son propre tableau des gestes");

// ---- Lecture du tableau Moments ----------------------------------------
var debut = texte.IndexOf("public static readonly MomentMenu[] Moments =", StringComparison.Ordinal);
Exiger(debut >= 0, "le tableau des gestes est trouve dans Pont/MenuAccueil.cs");
if (debut < 0) { Bilan(); return 1; }

var fin = texte.IndexOf("\n    ];", debut, StringComparison.Ordinal);
Exiger(fin > debut, "le tableau des gestes est correctement ferme");
if (fin <= debut) { Bilan(); return 1; }

var bloc = texte[debut..fin];

// Le tableau est ecrit a indentation fixe : un geste a 8 espaces, un verbe
// 12, une entree 16. On lit ligne a ligne plutot que par expression unique :
// le message d'echec peut alors nommer le verbe fautif.
var gestes = new List<(string Titre, string Teinte, List<(string Verbe, List<(string Libelle, string Code)> Entrees)> Verbes)>();

var reGeste = new Regex(@"^        new\(""([^""]+)"", ""([a-z]+)"", ""[^""]*"",\s*$");
var reVerbe = new Regex(@"^            new\(""([^""]*)"",\s*$");
var reEntree = new Regex(@"^                new\(""([^""]+)"",\s*""[^""]*"",\s*""(PROC_[A-Z0-9_]+)""\),?\s*$");

foreach (var ligne in bloc.Split('\n'))
{
    var l = ligne.TrimEnd('\r');

    var g = reGeste.Match(l);
    if (g.Success)
    {
        gestes.Add((g.Groups[1].Value, g.Groups[2].Value, new()));
        continue;
    }

    var v = reVerbe.Match(l);
    if (v.Success && gestes.Count > 0)
    {
        gestes[^1].Verbes.Add((v.Groups[1].Value, new()));
        continue;
    }

    var e = reEntree.Match(l);
    if (e.Success && gestes.Count > 0 && gestes[^1].Verbes.Count > 0)
        gestes[^1].Verbes[^1].Entrees.Add((e.Groups[1].Value, e.Groups[2].Value));
}

Exiger(gestes.Count >= 7, $"le menu porte au moins 7 gestes (lu : {gestes.Count})");

var toutesEntrees = gestes.SelectMany(g => g.Verbes).SelectMany(v => v.Entrees).ToList();
Exiger(toutesEntrees.Count >= 100,
    $"le menu couvre largement le catalogue v2 (lu : {toutesEntrees.Count} entrees, "
    + $"{toutesEntrees.Select(x => x.Code).Distinct().Count()} procedures distinctes)");

// ---- 1. Aucun code technique ni obsolete --------------------------------
// Chaque motif dit POURQUOI le code n'a rien a faire dans le menu : le
// message d'echec se lit sans ouvrir les sources v2.
var interdits = new (string Motif, string Pourquoi)[]
{
    (@"^PROC_DEMO",            "bac a sable de demonstration du moteur"),
    (@"^PROC_DEV(_|$)",        "bac a sable de developpeur"),
    (@"^PROC_DEBUG",           "outil de debogage des controles"),
    (@"^PROC_TESTNLH$",        "canevas de test sans logique metier"),
    (@"_EXE$",                 "lanceur technique, pas un ecran"),
    (@"^PROC_VGED$",           "obsolete : remplacee par PROC_VDOSSIER"),
    (@"^PROC_MJUDS$",          "marquee OBSOLETE dans les sources v2"),
    (@"^PROC_CORRESP",         "outil de migration des correspondances"),
    (@"^PROC_COMPARE_TABLE$",  "outil de comparaison technique"),
    (@"^PROC_BATCH$",          "traitement de fond, sans ecran"),
    (@"^PROC_PILOTE_",         "pilote de service technique"),
    (@"^PROC_4D$",             "passerelle technique"),
    (@"^PROC_MENU$",           "le menu lui-meme"),
    (@"^PROC_SOUS_MENU$",      "le menu lui-meme"),
    (@"^PROC_EXPLOIT$",        "outil d'exploitation, hors poste greffier"),
};

foreach (var (libelle, code) in toutesEntrees.DistinctBy(x => x.Code))
    foreach (var (motif, pourquoi) in interdits)
        if (Regex.IsMatch(code, motif))
            Exiger(false, $"« {libelle} » ({code}) n'entre pas dans le menu : {pourquoi}");

Exiger(true, "aucun code technique ni obsolete dans le menu");

// ---- 2. Chaque verbe garde entre 3 et 8 ecrans --------------------------
foreach (var geste in gestes)
{
    Exiger(geste.Verbes.Count > 0, $"le geste « {geste.Titre} » porte au moins un verbe");

    foreach (var (verbe, entrees) in geste.Verbes)
    {
        var nom = verbe.Length > 0 ? $"{geste.Titre} / {verbe}" : geste.Titre;
        Exiger(entrees.Count is >= 3 and <= 8,
            $"« {nom} » garde entre 3 et 8 ecrans (lu : {entrees.Count})");

        var double_ = entrees.GroupBy(x => x.Code).FirstOrDefault(gr => gr.Count() > 1);
        Exiger(double_ is null,
            $"« {nom} » ne repete pas deux fois le meme ecran"
            + (double_ is null ? "" : $" (repete : {double_.Key})"));
    }
}

// ---- 3. Coherence : teintes, libelles ----------------------------------
var feuille = File.ReadAllText(css);
foreach (var teinte in gestes.Select(g => g.Teinte).Distinct())
    Exiger(feuille.Contains($".rig-menu__groupe--{teinte}", StringComparison.Ordinal),
        $"la couleur du geste « {teinte} » est definie dans rig-accueil.css");

foreach (var (libelle, code) in toutesEntrees)
    Exiger(!libelle.Contains("PROC_", StringComparison.Ordinal),
        $"le libelle de {code} ne montre pas le code interne");

// ---- 4. Chaque code est atteste par RIG v2 (si le depot est la) ---------
// Les fiches de reference de RIG v2 font foi. Trois procedures n'ont pas de
// fiche mais bien un dossier de sources (Source/.../PROC_xxx) : elles sont
// nommees ici, pour que l'exception reste visible au lieu d'affaiblir le
// controle.
// PROC_ACTUALITES est NEUVE : les actualites n'existent pas dans
// RIG v2 (verifie : aucun fichier proc_bulletin*.md dans les fiches), donc
// aucune fiche ne peut l'attester.
// PROC_COMMANDES_WEB est NEUVE aussi : c'est l'ecran « Traitement des
// commandes » de NeoRIG (D:\RIG-V3, CommandesWebViewModel) ; la V2 traitait
// les commandes web depuis PROC_REQUI, sans procedure a elles.
// PROC_UTILISATEUR (« Comptes des agents », lot 3 de la reconstruction) n'a pas
// de fiche mais un dossier de sources : Source\Outils\Exe\Processus\PROC_UTILISATEUR
// (FORM_UTILISATEUR, OPE_EDITION_UTILISATEUR, processus GUTILI de MODELE_PROCESSUS).
// PROC_MAIL_LOT1 (« Courriels envoyes », SOC-006) n'a pas de fiche dans ce dossier mais
// un dossier de sources : Source\RIG\DLL\Processus\PROC_MAIL_LOT1 (OPE_LISTE_MAILS_LOT1,
// fonction MAIL_LOT1 « Mails envoyés en Lot 1 » de PROCESSUS_ET_FONCTIONALITE).
// PROC_COMPTEURS_NUM (« Compteurs de numeros ») est l'ecran de reattribution
// d'un numero de NUM-004 T04. Il reprend XNUMERO de la V2 (« Reattribution de
// numero metier », procedure VB6 du menu), qui n'a ni fiche ni source ici.
// PROC_REGLAGES_GREFFE (« Reglages du greffe », SOC-003 T03) est NEUVE : la V2
// n'a pas d'ecran generique de sa table PARAMETRAGE (chaque ecran ecrit les siens).
// PROC_NUMEROS_MANQUANTS (« Numeros manquants », NUM-013 T04) reprend LNUMCM de
// la V2, procedure VB6 du menu sans fiche ni source ici, comme XNUMERO.
// PROC_TOUCHES (« Ancienne touche, nouvelle touche », SOC-027) est NEUVE : la fiche
// qui fait correspondre les raccourcis de la V2 a ceux de la V3 n'existe pas dans la V2.
// PROC_SUPERVISION (« Supervision », SOC-011 T04 et SOC-017 T04) est NEUVE : la V2
// n'a pas d'ecran ; l'exploitation lit COMMUN_RIG (ETAT_SERVICE, LOG_RIG_ERREUR) a
// la main et relance par l'outil MaintenanceServices, hors de RIG.
// PROC_PREMIERS_PAS (« Premiers pas », SOC-026 CA06) et PROC_CERTIFICAT (« Certificat NF525 de la V3 », SOC-019 CA04) sont
// NEUVES (lot 17 de la reconstruction) : la V2 n'a ni aide a la premiere ouverture ni ecran du certificat (il circule en
// piece jointe, un greffe le demande a l'assistance).
// PROC_CERTIF (« Certification NF525 », SOC-018 T03) n'a pas de fiche mais un dossier de sources, dans la branche
// origin/Development de la V2 seulement : Source\RIG\DLL\Processus\PROC_CERTIF (FORM_CERTIF, journaux JET_NP et JET_P,
// grands totaux des factures et des encaissements).
var sansFiche = new[] { "PROC_LISTE_DELIBERES", "PROC_SUIVI_IMPRESSION", "PROC_REQUI",
                        "PROC_ACTUALITES", "PROC_COMMANDES_WEB", "PROC_UTILISATEUR", "PROC_MAIL_LOT1",
                        "PROC_COMPTEURS_NUM", "PROC_REGLAGES_GREFFE",
                        "PROC_NUMEROS_MANQUANTS", "PROC_TOUCHES", "PROC_SUPERVISION", "PROC_CERTIF",
                        "PROC_FICHES_CONFLIT", "PROC_HISTORIQUE_ANCIEN_RIG", "PROC_PREMIERS_PAS", "PROC_CERTIFICAT" };

var fiches = @"D:\RigApplication\Documentation\reference\proc";

if (Directory.Exists(fiches))
{
    var connus = Directory.GetFiles(fiches, "*.md")
        .Select(f => Path.GetFileNameWithoutExtension(f).ToUpperInvariant())
        .ToHashSet();

    foreach (var (libelle, code) in toutesEntrees.DistinctBy(x => x.Code))
        Exiger(connus.Contains(code) || sansFiche.Contains(code),
            $"« {libelle} » ({code}) existe bien dans RIG v2");
}
else
{
    Info("les sources de RIG v2 ne sont pas accessibles depuis ce poste : "
       + "le controle « la procedure existe en v2 » n'est pas joue.");
}

return Bilan();

int Bilan()
{
    Console.WriteLine();
    if (echecs.Count == 0)
    {
        Console.WriteLine("VERT : le menu par gestes tient ses regles.");
        return 0;
    }
    Console.WriteLine($"ROUGE : {echecs.Count} regle(s) du menu ne sont pas tenues.");
    foreach (var e in echecs) Console.WriteLine("  - " + e);
    return 1;
}
