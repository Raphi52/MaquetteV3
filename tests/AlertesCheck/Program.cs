using System.Text.RegularExpressions;
using RigV3Desktop;

// Controle du widget ALERTES de l'accueil : les lignes sont CALCULEES sur
// les dossiers connus, chaque alerte MENE quelque part, la destination
// existe reellement dans l'application, et les reglages d'affichage font
// ce qu'ils annoncent.
var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
var vue = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));

// --- 1. Le releve
var sonde = new AlertesGreffe { Utilisateur = "R. VILAIN" };
// La TUILE ne montre que la feuille metier (« Alertes accueil v2 Rig.xlsx ») ;
// les constats calcules vivent a part. Les controles de destination et de
// recette portent sur LES DEUX, mais l'accueil, lui, n'affiche que le catalogue.
var catalogue = sonde.Relever();
var calculees = sonde.ReleverCalculees();
var a = catalogue.Concat(calculees).ToArray();

Exiger(catalogue.Length == CatalogueAlertes.Types.Length,
    "la tuile « A traiter » montre exactement les types de la feuille metier");
Exiger(catalogue.All(x => x.Id.StartsWith("alerte-")),
    "aucune alerte hors feuille metier dans la tuile « A traiter »");

// LE COMBIEN remplace la barre de couleur a gauche (17/09/2026). Le chiffre
// doit etre STABLE : deux affichages qui donnent deux nombres feraient douter
// de la charge affichee.
Exiger(catalogue.All(x => x.Nombre >= 0), "aucune ligne ne montre un compte negatif");
Exiger(catalogue.Where(x => x.Nombre == 0)
                .All(x => CatalogueAlertes.Types.First(t => CatalogueAlertes.IdentifiantDe(t) == x.Id).Code is null),
    "seules les alertes « a creer » affichent zero");
Exiger(catalogue.Zip(sonde.Relever()).All(c => c.First.Nombre == c.Second.Nombre),
    "le compte d'une ligne ne change pas d'un affichage a l'autre");
Exiger(!vue.Contains("rig-filet-v--@a.Statut"),
    "la barre verticale a disparu des lignes de « A traiter »");
// Depuis le 17/09/2026 la pastille est coloree par le PALIER de charge
// (PanneauMetier.Palier) et non plus par la gravite : une seule grammaire de
// couleur pour toutes les pastilles de l'accueil.
Exiger(vue.Contains("rig-alerte__compte--@PanneauMetier.Palier(a.Nombre)") && vue.Contains("@a.Nombre"),
    "chaque ligne de « A traiter » montre son nombre d'elements a traiter, colore par palier");
Exiger(PanneauMetier.Palier(0) == "vide" && PanneauMetier.Palier(10) == "bas"
    && PanneauMetier.Palier(11) == "moyen" && PanneauMetier.Palier(20) == "moyen"
    && PanneauMetier.Palier(21) == "haut",
    "les paliers de charge : 0 vide, 1-10 bas, 11-20 moyen, 21+ haut");

// Le compte d'une CATEGORIE est la CHARGE, pas le nombre de lignes.
Exiger(vue.Contains("groupe.Lignes.Sum(x => x.Nombre)"),
    "le compte d'une categorie somme les nombres de ses lignes");
Exiger(!vue.Contains("rig-groupe-tete__compte\">@groupe.Lignes.Length"),
    "le compte d'une categorie n'est plus son nombre de lignes");

Exiger(a.Length > 0, "la sonde rend au moins une alerte");
Exiger(a.All(x => !string.IsNullOrWhiteSpace(x.Titre) && !string.IsNullOrWhiteSpace(x.Detail)),
    "aucune alerte vide");
Exiger(a.Select(x => x.Id).Distinct().Count() == a.Length, "les identifiants d'alerte sont uniques");
Exiger(a.All(x => x.Statut is "ok" or "attente" or "action" or "alerte"),
    "chaque statut correspond a un filet de couleur existant");

var b = sonde.Relever().Concat(sonde.ReleverCalculees()).ToArray();
Exiger(a.Zip(b).All(c => c.First == c.Second), "deux releves consecutifs donnent le meme etat");

// Le hors-delai est CALCULE sur les demandes, pas ecrit en dur.
var attenduHorsDelai = JeuDemo.Demandes.Count(d => d.EnCours
    && (JeuDemo.Aujourdhui - d.Creation2).TotalDays > AlertesGreffe.DelaiLegalJours);
var delai = a.FirstOrDefault(x => x.Id == "delai");
Exiger(attenduHorsDelai == 0 || delai is not null, "une demande hors delai produit l'alerte correspondante");
Exiger(delai is null || delai.Titre.StartsWith(attenduHorsDelai.ToString()),
    "le compte de l'alerte hors delai suit les demandes connues");

// Le verrou ne compte QUE les dossiers d'un autre poste.
var verrou = a.FirstOrDefault(x => x.Id == "verrou");
var attenduVerrous = JeuDemo.Demandes.Count(d => d.EnCours && d.Utilisateur != "R. VILAIN"
                                                 && !string.IsNullOrWhiteSpace(d.Utilisateur));
Exiger(verrou is null || verrou.Titre.StartsWith(attenduVerrous.ToString()),
    "l'alerte de verrou ignore mes propres dossiers");

// --- 2. Chaque alerte EMMENE quelque part, et la destination EXISTE
Exiger(a.All(x => !string.IsNullOrWhiteSpace(x.Cible)), "chaque alerte porte une destination");
Exiger(a.All(x => !string.IsNullOrWhiteSpace(x.LibelleCible)),
    "chaque destination est nommee en clair pour l'infobulle");

foreach (var alerte in a)
{
    if (alerte.Affaire is { Length: > 0 } numero)
    {
        Exiger(JeuDemo.Demandes.Any(d => d.Dossier == numero),
            $"l'affaire visee par « {alerte.Id} » existe ({numero})");
        continue;
    }

    // Une alerte de LOT (« lot:CODE ») vise l ecran de traitement groupe,
    // avec sa recette deja choisie. Ce n est pas une cible d affaire.
    if (alerte.Lot is { Length: > 0 } codeLot)
    {
        Exiger(RecettesTaches.ParCode(codeLot) is not null,
            $"la recette du lot « {alerte.Id} » existe ({codeLot})");
        Exiger(vue.Contains("case \"PROC_TACHES_LOT\":"),
            "la vue aiguille l ecran de traitement en lot");
        Exiger(vue.Contains("LotDemande = codeLot;"),
            "l alerte de lot ouvre l ecran SUR SA recette, pas sur un choix a refaire");
        continue;
    }

    // Une alerte TASK DRIVEN (« tache:CODE:dossier ») ne vise pas un ecran :
    // elle ouvre une RECETTE sur une affaire. Les deux doivent exister, sinon
    // le clic ouvrirait une tache vide.
    if (alerte.Tache is { } t)
    {
        Exiger(RecettesTaches.ParCode(t.Code) is not null,
            $"la recette visee par « {alerte.Id} » existe ({t.Code})");
        Exiger(JeuDemo.Demandes.Any(d => d.Dossier == t.Dossier),
            $"l'affaire visee par la tache « {alerte.Id} » existe ({t.Dossier})");
        Exiger(vue.Contains("OuvrirTache("),
            "la vue sait ouvrir une tache depuis une alerte");
        continue;
    }

    // Une cible d'ecran doit etre aiguillee dans Home.razor ET figurer au
    // catalogue, sinon le clic tomberait sur « ecran non maquette ».
    Exiger(vue.Contains($"case \"{alerte.Cible}\":"),
        $"l'ecran de « {alerte.Id} » est aiguille ({alerte.Cible})");
    Exiger(Regex.IsMatch(vue, $@"new\(""{alerte.Cible}"",\s*"""),
        $"la cible de « {alerte.Id} » est au catalogue des traitements");
}

// --- 2 bis. Les alertes TASK DRIVEN suivent les EVENEMENTS, pas la file
// Une alerte de tache accrochee au « premier dossier en cours » enverrait le
// greffier renommer une societe que personne n a demande de renommer.
var taches = a.Where(x => x.Tache is not null).ToArray();
var attendues = JeuDemo.EvenementsAttendus
    .Where(e => RecettesTaches.ParCode(e.Code) is not null)
    .Where(e => JeuDemo.Demandes.Any(d => d.Dossier == e.Dossier))
    .ToArray();

// REGROUPEMENT (16/09/2026) : plusieurs affaires qui attendent la MEME
// recette font UNE entree de lot, pas une alerte par dossier -- sinon
// quarante denominations produisent quarante lignes pour une seule
// decision. La regle d origine tient toujours, mais au niveau de la
// COUVERTURE : aucun evenement en attente ne doit disparaitre.
var lots = a.Where(x => x.Lot is not null).ToArray();
bool Couvert(EvenementAttendu e)
{
    var r = RecettesTaches.ParCode(e.Code)!;
    return taches.Any(t => t.Tache!.Value.Code == e.Code && t.Tache!.Value.Dossier == e.Dossier)
           || lots.Any(l => l.Lot == r.Code);
}
Exiger(attendues.All(Couvert),
    "chaque evenement en attente est couvert : par son alerte ou par son lot");
Exiger(taches.Length + lots.Length <= attendues.Length,
    "le regroupement REDUIT la file, il ne l allonge pas");

// Un lot ne se forme qu a partir de DEUX dossiers, et jamais pour une
// recette qui se saisit ligne par ligne (le tableau la refuse).
foreach (var l in lots)
{
    var recette = RecettesTaches.ParCode(l.Lot!);
    Exiger(recette is not null, $"le lot « {l.Id} » nomme une recette connue");
    Exiger(recette is not null && !recette.ARepetition,
        $"le lot « {l.Id} » ne groupe pas une tache qui se saisit ligne par ligne");
    var combien = attendues.Count(e => RecettesTaches.ParCode(e.Code)!.Code == recette!.Code);
    Exiger(combien >= 2, $"le lot « {l.Id} » porte au moins deux dossiers ({combien})");
}
Exiger(lots.Length > 0, "au moins un lot se forme sur le jeu de demonstration");
Exiger(taches.All(t => JeuDemo.EvenementsAttendus.Any(e => e.Code == t.Tache!.Value.Code
                                                        && e.Dossier == t.Tache!.Value.Dossier)),
    "aucune alerte de tache sans evenement qui la justifie");

// Un evenement dont le code n a AUCUNE recette ne doit produire aucune
// alerte : le clic ouvrirait une tache sans champ.
Exiger(taches.All(t => RecettesTaches.ParCode(t.Tache!.Value.Code) is not null),
    "aucune alerte de tache ne vise une recette inconnue");

// Le meme dossier peut porter deux evenements distincts : les identifiants
// doivent rester uniques, sinon une alerte en masquerait une autre.
Exiger(taches.Select(t => t.Id).Distinct().Count() == taches.Length,
    "deux evenements ne produisent pas deux alertes de meme identifiant");

// CAS LIMITE, verifie sur une donnee reelle : le jeu declare au moins un
// evenement SANS recette (COLL, conjoint collaborateur). Il doit etre ecarte
// en silence -- ni alerte, ni plantage. Sans cet echantillon, le filtre
// « recette inconnue » serait un controle qui ne teste rien.
var sansRecette = JeuDemo.EvenementsAttendus
    .Where(e => RecettesTaches.ParCode(e.Code) is null)
    .ToArray();
Exiger(sansRecette.Length > 0,
    "le jeu contient au moins un evenement sans recette, pour eprouver le filtre");
Exiger(sansRecette.All(e => !taches.Any(t => t.Tache!.Value.Code == e.Code)),
    $"l'evenement sans recette ne produit aucune alerte ({string.Join(", ", sansRecette.Select(e => e.Code))})");

// Chaque recette declaree doit porter au moins un champ : une tache sans
// champ ouvrirait un formulaire vide.
Exiger(RecettesTaches.Liste.All(r => r.Champs.Length > 0),
    "aucune recette n'est vide de champ");
Exiger(RecettesTaches.Liste.Select(r => r.Code).Distinct().Count() == RecettesTaches.Liste.Length,
    "deux recettes ne portent pas le meme code");

// Chaque EFFET doit citer la colonne du referentiel d ou sort sa valeur.
// Sans cette exigence, rien n empecherait d ecrire un code de publication
// plausible mais faux -- et le greffier cocherait une diffusion qui n existe
// pas. Les valeurs elles-memes ont ete lues sur RIG_AMIENS@SQL-PROD\PROD.
Exiger(RecettesTaches.Liste.SelectMany(r => r.Effets)
        .All(e => e.Source.StartsWith("CODE_EVENEMENT_RCS.")),
    "chaque effet cite la colonne du referentiel dont il sort");
Exiger(RecettesTaches.Liste.SelectMany(r => r.Effets)
        .All(e => !string.IsNullOrWhiteSpace(e.Code) && !string.IsNullOrWhiteSpace(e.Libelle)),
    "aucun effet sans code ni libelle");

// Toute recette BRANCHEE sur un evenement en attente doit porter ses effets :
// une tache qu on peut clore sans rien publier est une tache a moitie faite.
var branchees = JeuDemo.EvenementsAttendus
    .Select(e => RecettesTaches.ParCode(e.Code))
    .Where(r => r is not null)
    .Distinct()
    .ToArray();
Exiger(branchees.All(r => r!.Effets.Length > 0),
    $"les {branchees.Length} recettes branchees portent toutes leurs effets");

// Cas LU en base : NUSA n a aucune publication BODACC. C est une valeur du
// referentiel, pas un oubli -- le controle fige ce fait.
var nusa = RecettesTaches.ParCode("NUSA");
Exiger(nusa is not null && !nusa.Effets.Any(e => e.Canal == "bodacc"),
    "le nom d'usage ne declare aucune publication BODACC (valeur lue)");
Exiger(nusa is not null && nusa.Effets.Any(e => e.Canal == "infogreffe" && e.Code == "4035"),
    "le nom d'usage porte son code Infogreffe lu en base (4035)");

// LES CODES DE DIFFUSION DIFFERENT D UN EVENEMENT A L AUTRE, et c est le
// piege : recopier ceux du voisin « par coherence » transmettrait sous une
// mauvaise reference. Valeurs LUES sur RIG_AMIENS@SQL-PROD\PROD le
// 16/09/2026 (R_CODEEVENEM_CODE_INPI, R_CODEEVENEM_INFOGREFFE).
var attenduInpi = new Dictionary<string, string>
{
    ["NOPA"] = "10P", ["PSEU"] = "15P", ["COLL"] = "30P", ["PRENO"] = "0F",
    ["DENO"] = "0F", ["SIGL"] = "0F", ["FORM"] = "0F", ["NUSA"] = "0F",
};
foreach (var (code, valeur) in attenduInpi)
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null && r.Effets.Any(e => e.Canal == "inpi" && e.Code == valeur),
        $"« {code} » transmet a l'INPI sous SON code lu en base ({valeur})");
}

var attenduInfogreffe = new Dictionary<string, string>
{
    ["DENO"] = "4028", ["SIGL"] = "4011", ["FORM"] = "4012",
    ["NUSA"] = "4035", ["NOPA"] = "4035", ["PRENO"] = "4035",
    ["PSEU"] = "4035", ["COLL"] = "4500",
};
foreach (var (code, valeur) in attenduInfogreffe)
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null && r.Effets.Any(e => e.Canal == "infogreffe" && e.Code == valeur),
        $"« {code} » se diffuse sous SON code Infogreffe lu en base ({valeur})");
}

// L ETAT CIVIL ne s annonce PAS au BODACC : les 3 colonnes B1/C1/D1 sont
// vides en base pour ces cinq codes. Fait LU, jamais un oubli de saisie.
foreach (var code in new[] { "NUSA", "NOPA", "PRENO", "PSEU", "COLL" })
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null && !r.Effets.Any(e => e.Canal == "bodacc"),
        $"« {code} » ne publie pas au BODACC (valeur lue)");
}

// COLL est le SEUL a ne declencher aucune notification : les deux colonnes
// sont a 0. Aucun de ses effets de notification ne doit etre exige.
var coll = RecettesTaches.ParCode("COLL");
Exiger(coll is not null && !coll.Effets.Any(e => e.Canal.StartsWith("notif") && e.Obligatoire),
    "le conjoint collaborateur n'exige aucune notification (valeur lue)");

// A l'inverse, l'etat civil des personnes notifie bien les secondaires.
foreach (var code in new[] { "NOPA", "PRENO", "PSEU" })
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null && r.Effets.Any(e => e.Canal == "notif-secondaire" && e.Obligatoire),
        $"« {code} » notifie les etablissements secondaires (valeur lue)");
}

// LES RECETTES LES PLUS FREQUENTES. Comptage sur douze mois glissants dans
// EVENEMENT_RCS (RIG_AMIENS@SQL-PROD\PROD, 16/09/2026). Ces codes ne sont
// pas choisis au hasard : ce sont ceux que le greffe traite le plus et dont
// le referentiel DECLARE les champs (ULT_OP_ET_EVENEMENT_RCS).
var frequents = new Dictionary<string, string>
{
    ["RADI"] = "4004", ["DISS"] = "4024", ["CESM"] = "4026",
};
foreach (var (code, infogreffe) in frequents)
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null, $"« {code} », l'un des plus frequents, a sa recette");
    Exiger(r is not null && r.Effets.Any(e => e.Canal == "infogreffe" && e.Code == infogreffe),
        $"« {code} » se diffuse sous SON code Infogreffe lu en base ({infogreffe})");
}

// RADI ne publie PAS au BODACC, contrairement a DISS et CESM : valeur lue,
// et c'est la difference qu'une recopie aurait effacee.
var radi = RecettesTaches.ParCode("RADI");
Exiger(radi is not null && !radi.Effets.Any(e => e.Canal == "bodacc"),
    "la radiation de l'exploitant ne publie pas au BODACC (valeur lue)");
Exiger(radi is not null && radi.Effets.Any(e => e.Canal == "inpi" && e.Code == "41P"),
    "la radiation transmet a l'INPI sous 41P (valeur lue)");
foreach (var code in new[] { "DISS", "CESM" })
{
    var r = RecettesTaches.ParCode(code);
    Exiger(r is not null && r.Effets.Any(e => e.Canal == "bodacc"),
        $"« {code} » publie bien au BODACC (valeur lue)");
}

// CESM est le seul des trois sans aucune notification (les deux colonnes a 0).
var cesm = RecettesTaches.ParCode("CESM");
Exiger(cesm is not null && !cesm.Effets.Any(e => e.Canal.StartsWith("notif") && e.Obligatoire),
    "la cessation d'activite n'exige aucune notification (valeur lue)");

// La radiation porte des champs FACULTATIFS : une radiation pour simple arret
// d'activite n'a ni deces ni fin d'emploi salarie. Si les trois etaient
// exiges, la tache ne pourrait jamais se clore dans le cas le plus courant.
Exiger(radi is not null && radi.Champs.Any(c => !c.Obligatoire),
    "la radiation propose des champs sans les exiger");
Exiger(radi is not null && radi.ChampsObligatoires.Length == 1,
    "seule la date de cessation est exigee pour clore une radiation");

// Le releve ne doit plus contenir de dossier choisi au hasard dans la file.
var source = File.ReadAllText(Path.Combine(racine, "Metier/AlertesGreffe.cs"));
Exiger(!source.Contains("var aRenommer = demandes.FirstOrDefault(d => d.EnCours)"),
    "l'alerte de tache n'est plus accrochee au premier dossier venu");
Exiger(source.Contains("JeuDemo.EvenementsAttendus"),
    "le releve part des evenements en attente");

// Les deux ecrans crees pour les alertes existent bien sur le disque.
Exiger(File.Exists(Path.Combine(racine, "Components/Ecrans/EcranVerrous.razor")),
    "l'ecran des dossiers verrouilles existe");
Exiger(File.Exists(Path.Combine(racine, "Components/Ecrans/EcranTeletransmission.razor")),
    "l'ecran des teletransmissions existe");

// --- 2 ter. L'ecran des verrous, PORTE EN REACT (lot 1, 18/11/2026).
// Ce que ce banc tenait sur EcranVerrous.razor -- l'ecran existe, ses lignes
// sont CALCULEES sur les demandes connues, jamais ecrites en dur -- il le tient
// maintenant sur la chaine reelle : coquille .razor -> ile React -> pont C#.
// Le calcul vit dans Pont/EcranVerrous.cs : il est EXECUTE ici, pas relu.
var coquilleVerrous = File.ReadAllText(Path.Combine(racine, "Components/Ecrans/EcranVerrous.razor"));
var cheminTsxVerrous = Path.Combine(racine, "web/src/ecrans/EcranVerrous.tsx");
Exiger(File.Exists(cheminTsxVerrous), "l'ecran React des dossiers verrouilles existe (web/src/ecrans/EcranVerrous.tsx)");
var tsxVerrous = File.Exists(cheminTsxVerrous) ? File.ReadAllText(cheminTsxVerrous) : string.Empty;
var pontVerrous = File.ReadAllText(Path.Combine(racine, "Pont/EcranVerrous.cs"));
Exiger(coquilleVerrous.Contains("<IleReact Composant=\"EcranVerrous\""),
    "la coquille EcranVerrous.razor monte l'ecran React du meme nom");
Exiger(Regex.IsMatch(coquilleVerrous, @"\[Parameter\] public EventCallback<string> OuvrirDossier")
       && coquilleVerrous.Contains("[\"ouvrirDossier\"] = IleReact.Rappel(OuvrirDossier)"),
    "la coquille garde le parametre OuvrirDossier et le remonte depuis React");
Exiger(vue.Contains("<EcranVerrous OuvrirDossier=\"OuvrirParNumero\" />"),
    "Home.razor ouvre toujours l'affaire cliquee dans l'ecran des verrous");
Exiger(tsxVerrous.Contains("appeler<VueVerrous>('EcranVerrous.Verrouilles', utilisateur, moiCompris)"),
    "l'ecran React demande ses lignes au pont, avec le login et la bascule saisis");
Exiger(pontVerrous.Contains("[JSInvokable(\"EcranVerrous.Verrouilles\")]"),
    "le pont expose ce calcul au React sous le meme identifiant");
Exiger(!Regex.IsMatch(tsxVerrous, @"['""`]\d{6}['""`]"),
    "l'ecran React n'ecrit aucun numero de dossier en dur : il affiche ce que le pont calcule");

var vueVerrous = RigV3Desktop.Pont.EcranVerrous.Verrouilles("R. VILAIN", moiCompris: false);
Exiger(vueVerrous.Lignes.Length == attenduVerrous,
    $"l'ecran montre autant de verrous que les demandes en cours d'un autre poste ({vueVerrous.Lignes.Length} / {attenduVerrous})");
Exiger(verrou is null || verrou.Titre.StartsWith(vueVerrous.Lignes.Length + " "),
    "l'alerte « ouvert(s) par un tiers » et l'ecran ou elle mene annoncent le meme nombre");
Exiger(vueVerrous.Lignes.All(l => l.Utilisateur != "R. VILAIN"
                                  && JeuDemo.Demandes.Any(d => d.Dossier == l.Dossier && d.EnCours && d.Utilisateur == l.Utilisateur)),
    "chaque ligne est une demande connue, en cours, portee par un autre login");
Exiger(vueVerrous.Lignes.Zip(vueVerrous.Lignes.Skip(1)).All(p => p.First.Age >= p.Second.Age),
    "les verrous les plus anciens viennent en tete");
var avecMoi = RigV3Desktop.Pont.EcranVerrous.Verrouilles("R. VILAIN", moiCompris: true);
Exiger(avecMoi.Lignes.Length == JeuDemo.Demandes.Count(d => d.EnCours && !string.IsNullOrWhiteSpace(d.Utilisateur)),
    "« Inclure mes propres dossiers » ajoute mes demandes en cours, et elles seules");
Exiger(avecMoi.Detenteurs.Sum(d => d.Nombre) == avecMoi.Lignes.Length
       && avecMoi.Detenteurs.Select(d => d.Utilisateur).SequenceEqual(avecMoi.Detenteurs.Select(d => d.Utilisateur).Order()),
    "les detenteurs, ranges par nom, comptent toutes les lignes");

// La vue branche le clic, et ne garde plus de liste d'alertes en dur.
Exiger(vue.Contains("OuvrirAlerte("), "Home.razor branche le clic d'une alerte");
Exiger(vue.Contains("SondeAlertes.Relever()"), "Home.razor releve les alertes par la sonde");
Exiger(!Regex.IsMatch(vue, @"new\(""delai"",\s*""3 demandes"),
    "plus d'alertes ecrites en dur dans la vue");

// --- 3. Les reglages font ce qu'ils annoncent
bool Tout(string _) => true;

var toutes = new ReglagesAlertes().Appliquer(a, Tout);
Exiger(toutes.Length == a.Length, "sans reglage, toutes les alertes surveillees sont affichees");
Exiger(toutes.Zip(toutes.Skip(1)).All(c => c.First.Gravite >= c.Second.Gravite),
    "par defaut, le plus grave est en tete");

var graves = new ReglagesAlertes { NiveauMini = 3 }.Appliquer(a, Tout);
Exiger(graves.All(x => x.Statut == "alerte"), "le niveau minimal ecarte ce qui est moins grave");
Exiger(graves.Length == a.Count(x => x.Statut == "alerte"), "et n'en ecarte pas davantage");

var deux = new ReglagesAlertes { Limite = 2 }.Appliquer(a, Tout);
Exiger(deux.Length == Math.Min(2, a.Length), "le plafond de lignes est respecte");

var parTitre = new ReglagesAlertes { Tri = "titre" }.Appliquer(a, Tout);
Exiger(parTitre.Zip(parTitre.Skip(1))
              .All(c => string.Compare(c.First.Titre, c.Second.Titre, StringComparison.OrdinalIgnoreCase) <= 0),
    "le tri par libelle est alphabetique");

var premier = a[0].Id;
var sansPremier = new ReglagesAlertes().Appliquer(a, id => id != premier);
Exiger(sansPremier.All(x => x.Id != premier), "une alerte eteinte n'est plus affichee");


// --- 5. Le catalogue metier (feuille « Alertes accueil v2 Rig ») et ses categories
Exiger(CatalogueAlertes.Types.Length >= 35, "le catalogue porte les types declares par le metier");
Exiger(CatalogueAlertes.Types.All(t => !string.IsNullOrWhiteSpace(t.Cible)),
    "chaque type d'alerte mene a un ecran de traitement");
Exiger(CatalogueAlertes.Types.All(t => t.Cible.StartsWith("PROC_")),
    "chaque cible est un code de procedure");
Exiger(CatalogueAlertes.Types.All(t => vue.Contains("\"" + t.Cible + "\"")),
    "chaque destination du catalogue existe reellement dans l'accueil");
Exiger(CatalogueAlertes.Types.All(t => CatalogueAlertes.Categories.Contains(t.Categorie)),
    "chaque type appartient a une categorie connue");
Exiger(CatalogueAlertes.Types.Select(CatalogueAlertes.IdentifiantDe).Distinct().Count()
       == CatalogueAlertes.Types.Length,
    "les identifiants du catalogue sont uniques");
Exiger(CatalogueAlertes.Types.Any(t => t.Code is null),
    "les alertes « a creer » (sans code RIG) sont presentes");

// Chaque type releve est bien affiche, et porte sa categorie.
Exiger(CatalogueAlertes.Types.All(t => a.Any(x => x.Id == CatalogueAlertes.IdentifiantDe(t))),
    "le releve contient toutes les alertes du catalogue");
Exiger(a.All(x => CatalogueAlertes.Categories.Contains(x.Categorie)),
    "aucune alerte relevee n'est hors categorie");

// Eteindre une CATEGORIE retire toutes ses lignes, et elles seules.
var sansDca = new ReglagesAlertes().Appliquer(a, Tout, c => c != CatalogueAlertes.Dca);
Exiger(sansDca.All(x => x.Categorie != CatalogueAlertes.Dca),
    "une categorie eteinte retire toutes ses alertes");
Exiger(sansDca.Length == a.Count(x => x.Categorie != CatalogueAlertes.Dca),
    "et n'en retire aucune autre");

// --- 6. Le nom de la tuile
Exiger(vue.Contains("\"alertes\",     \"A traiter\""),
    "la tuile des alertes s'appelle « A traiter »");
Exiger(vue.Contains("AlertesParCategorie") && vue.Contains("BasculerCategorie"),
    "la tuile groupe par categorie et sait en eteindre une");

// --- 7. LES WIDGETS METIER : un par categorie de la feuille (17/09/2026)
// Le widget de la maquette metier (deux panneaux : alertes a gauche, demandes
// a droite) est decline en UNE tuile PAR CATEGORIE du catalogue. Ce banc tient
// les deux bouts : la tuile existe et est aiguillee dans l'accueil, et ce
// qu'elle montre a droite est borne et stable.
foreach (var categorie in CatalogueAlertes.Categories)
{
    var idTuile = "metier-" + categorie.Split(' ')[0].ToLowerInvariant()
        .Replace("é", "e").Replace("è", "e");
    Exiger(vue.Contains($"new(\"{idTuile}\","),
        $"la categorie « {categorie} » a son widget metier dans la grille ({idTuile})");
    Exiger(PanneauMetier.ActionsDe(categorie).Length > 0,
        $"la categorie « {categorie} » propose au moins un geste metier");
}
Exiger(vue.Contains("StartsWith(\"metier-\")") && vue.Contains("CorpsMetier(def)"),
    "l'accueil aiguille les tuiles metier vers leur corps");
Exiger(vue.Contains("rig-metier__gauche") && vue.Contains("rig-metier__droite"),
    "le widget metier a bien ses DEUX panneaux");

// Le bouton de deploiement de la maquette : sans lui, la liste de droite reste
// coincee dans une tuile de 2 lignes et on ne peut pas la lire en entier.
Exiger(vue.Contains("rig-metier__deployer") && vue.Contains("BasculerDeploiementMetier"),
    "le widget metier porte son bouton de deploiement");
Exiger(vue.Contains("rig-metier--deploye") && vue.Contains("MetierEstDeploye"),
    "le deploiement change la classe du widget (etat lisible a l'ecran)");
var cssAccueil = File.ReadAllText(Path.Combine(racine, "wwwroot", "rig-accueil.css"));
Exiger(cssAccueil.Contains(".rig-metier--deploye") && cssAccueil.Contains("position: fixed"),
    "le widget deploye sort de la grille (position fixe)");

// Le panneau de droite : borne, stable, et muet sur une alerte « a creer ».
var grosse = a.OrderByDescending(x => x.Nombre).First();
var vide = a.FirstOrDefault(x => x.Nombre == 0);
// Plafonne DEUX fois : par LignesMax, et par le nombre de dossiers de demo --
// au-dela, le modulo de PanneauMetier.Lignes reboucle et un dossier ressort en
// double (regle ajoutee dans AlertesGreffe.cs apres le 21/09, banc aligne le 18/11).
Exiger(PanneauMetier.Lignes(grosse).Length
        == Math.Min(Math.Min(grosse.Nombre, PanneauMetier.LignesMax), JeuDemo.Demandes.Length),
    "le panneau de droite montre autant de demandes que l'alerte en annonce, plafonne");
Exiger(PanneauMetier.Lignes(grosse).Select(l => l.Chrono).Distinct().Count()
        == PanneauMetier.Lignes(grosse).Length,
    "le panneau de droite ne montre jamais deux fois le meme dossier");
Exiger(PanneauMetier.Lignes(grosse).Select(l => l.Chrono)
        .SequenceEqual(PanneauMetier.Lignes(grosse).Select(l => l.Chrono)),
    "deux affichages de suite donnent les MEMES dossiers");
Exiger(vide is null || PanneauMetier.Lignes(vide).Length == 0,
    "une alerte « a creer » n'invente aucune demande");
Exiger(PanneauMetier.Lignes(grosse).All(l => l.Chrono.Length > 0 && l.Type.Length > 0),
    "chaque ligne du panneau porte un chrono et un type");

Console.WriteLine();
if (echecs.Count > 0)
{
    Console.WriteLine($"{echecs.Count} controle(s) en echec.");
    return 1;
}
Console.WriteLine("Widget alertes : vert.");
return 0;
