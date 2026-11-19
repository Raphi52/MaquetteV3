using System.Text.RegularExpressions;
using RigV3Desktop;

// Banc de la MISE EN SITUATION : un flux EDI arrive au greffe, et on verifie
// qu on a bien tout ce qu il faut LA OU IL FAUT -- dans les ecrans reels, pas
// dans une vue de demonstration.
//
//   Courrier du jour  : la ligne EDI, rapprochee de la demande
//   Demandes recues   : la demande de modification, en cours
//   Accueil / Alertes : la tache a faire, qui mene droit a la zone d edition
//   Fiche du dossier  : les seuls champs declares, deja porteurs du flux
//
// Ce que ce banc NE fait pas : ouvrir des fenetres. Il verifie les DONNEES que
// ces ecrans lisent, et le cablage des ecrans vers elles.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var dossier = SimulationEdi.Dossier;
var alertes = new AlertesGreffe();

// --- 1. AUCUNE VUE DEDIEE : le cas se rencontre dans les ecrans du greffe.
Exiger(!File.Exists(Path.Combine(racine, "Components/Ecrans/EcranSimulationEdi.razor")),
    "aucun ecran de demonstration : la mise en situation se joue dans les vrais ecrans");
var home = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
Exiger(!home.Contains("DEMO_CHAINE_EDI") && !home.Contains("EcranSimulationEdi"),
    "le menu n expose aucune entree de demonstration");

// --- 2. COURRIER DU JOUR : le flux y est, par le mode EDI, rapproche.
var arrivee = JeuDemo.Arrivees.FirstOrDefault(a => a.References == SimulationEdi.Message.Fichier);
Exiger(arrivee is not null, "le fichier EDI est dans le courrier du jour");
Exiger(arrivee?.Mode == "EDI" && arrivee.ModeLisible == "Edi",
    "il y entre par le mode EDI, lisible tel quel a l ecran");
Exiger(arrivee?.NumDemande == dossier,
    "le courrier est RAPPROCHE de la demande qu il a ouverte");
Exiger(JeuDemo.Arrivees[0].References == SimulationEdi.Message.Fichier,
    "il est en tete du courrier : c est l arrivee la plus recente");
// L ecran du courrier est PORTE EN REACT (lot 3, 19/11/2026) : son filtre vit
// dans Pont/EcranArrivee.cs. On lit le pont, et on l EXECUTE comme l ecran.
var ecranArrivee = File.ReadAllText(Path.Combine(racine, "Pont/EcranArrivee.cs"));
Exiger(ecranArrivee.Contains("JeuDemo.Arrivees"),
    "l ecran du courrier lit bien cette liste, il n a pas sa copie");
var courrier = RigV3Desktop.Pont.EcranArrivee.Trouver(
    new("", "", "", "", "", null, null, false, false, 50));
Exiger(courrier.Lignes.Length > 0 && courrier.Lignes[0].References == SimulationEdi.Message.Fichier
       && courrier.Lignes[0].ModeLisible == "Edi" && courrier.Lignes[0].NumDemande == dossier,
    "a l ouverture de l ecran du courrier, le flux EDI est la premiere ligne, rapprochee de sa demande");

// --- 3. DEMANDES RECUES : la demande existe et elle est en cours.
var demande = JeuDemo.Demandes.FirstOrDefault(d => d.Dossier == dossier);
Exiger(demande is not null, "la demande de modification est dans la file");
Exiger(demande?.EnCours == true, "elle est en cours, donc visible dans la file du jour");
Exiger(demande?.Siren == SimulationEdi.Message.Siren,
    "elle porte le SIREN annonce par le flux : pas deux verites");
// L ecran des demandes est PORTE EN REACT (lot 3) : son filtre vit dans
// Pont/EcranDemandes.cs. On lit le pont, et on l EXECUTE comme l ecran.
var ecranDemandes = File.ReadAllText(Path.Combine(racine, "Pont/EcranDemandes.cs"));
Exiger(ecranDemandes.Contains("JeuDemo.Demandes"),
    "l ecran des demandes lit bien cette liste");
var fileDuJour = RigV3Desktop.Pont.EcranDemandes.Trouver(
    new("", "", "", null, null, [], false, true, 50), []);
Exiger(fileDuJour.Lignes.Any(l => l.Dossier == dossier && l.EnCours && l.Siren == SimulationEdi.Message.Siren),
    "l ecran des demandes, filtre « en cours », montre la demande ouverte par le flux");

// --- 4. ACCUEIL : l alerte est CALCULEE sur l evenement, et elle mene a la tache.
var evenement = JeuDemo.EvenementsAttendus.FirstOrDefault(e => e.Dossier == dossier);
Exiger(evenement is not null, "l evenement RCS attend d etre traite");
Exiger(evenement?.Code == SimulationEdi.Message.CodeEvenement,
    "il porte le code lu dans le flux");

// fix-ok: depuis le 17/09 Relever() ne rend QUE le catalogue metier de la feuille 2 ; les constats calcules (lots, taches) sont dans ReleverCalculees().
var relevees = alertes.ReleverCalculees();
var alerte = relevees.FirstOrDefault(a => a.Tache is { } t && t.Dossier == dossier);
Exiger(alerte is not null, "l accueil leve une alerte pour ce dossier");
Exiger(alerte?.Tache?.Code == SimulationEdi.Message.CodeEvenement,
    "l alerte mene a la TACHE, pas a une simple affaire");
Exiger(alerte?.Lot is null,
    "l alerte du jour ne part PAS en lot : elle mene droit a la zone d edition");
Exiger(relevees.Any(a => a.Lot is not null),
    "les evenements plus anciens restent regroupes en lot");

// --- 5. FICHE : les champs declares, et seulement eux.
var recette = RecettesTaches.ParCode(alerte!.Tache!.Value.Code)!;
var colonnes = recette.Champs.Select(c => c.Table + "." + c.Colonne).ToArray();
Exiger(colonnes.Contains("INSCRIT_PM.DENOMINATION_USAGE"), "la fiche ouvre la denomination");
Exiger(colonnes.Contains("ETABLISSEMENT.NOM_DOMAINE"), "la fiche ouvre le nom de domaine");
Exiger(recette.Champs.Length <= 4,
    $"la vue reste repliee : {recette.Champs.Length} champs, pas la fiche entiere");

// --- 6. LE FLUX PORTE les valeurs, et elles sont proposees dans la fiche.
Exiger(SimulationEdi.ValeurProposee("INSCRIT_PM", "DENOMINATION_USAGE")
        == SimulationEdi.NouvelleDenomination,
    "la denomination proposee est celle du flux");
Exiger(SimulationEdi.ValeurProposee("ETABLISSEMENT", "NOM_DOMAINE")
        == SimulationEdi.NouveauNomDomaine,
    "le nom de domaine propose est celui du flux");
Exiger(SimulationEdi.ValeurProposee("INSCRIT_PM", "SIGLE") is null,
    "un champ dont le flux ne dit rien ne recoit AUCUNE valeur inventee");

var contenu = string.Join("\n", SimulationEdi.Message.Lignes);
Exiger(contenu.Contains(SimulationEdi.Message.CodeEvenement)
        && contenu.Contains(SimulationEdi.NouvelleDenomination)
        && contenu.Contains(SimulationEdi.NouveauNomDomaine),
    "le fichier EDI porte lui-meme le code et les deux valeurs");

// --- 7. Le prereemplissage passe par la TRACE, sur le chemin REEL de la tache.
Exiger(Regex.IsMatch(home, @"private void OuvrirTache\([\s\S]{0,700}?ProposerLesValeursDuFlux\("),
    "l ouverture d une tache propose les valeurs du flux, quel que soit le chemin suivi");
Exiger(Regex.IsMatch(home, @"ProposerLesValeursDuFlux[\s\S]{0,900}?Etat\.NoterModification\("),
    "elles sont ecrites dans l historique, comme une saisie a la main");

var fiche = File.ReadAllText(Path.Combine(racine, "Components/FicheInformationsDossier.razor"));
Exiger(Regex.IsMatch(fiche, @"ValeurAffichee\(string cle\)[\s\S]{0,300}?DerniereValeur\("),
    "le formulaire AFFICHE ce que la trace porte deja");
// Verrou KBIS (18/09/2026) : il ne joue QUE hors tache ; en tache, Saisir
// rend la main a Modifier et le champ reste saisissable.
Exiger(fiche.Contains("@onchange=\"ev => Saisir(bloc, c, cle,")
       && Regex.IsMatch(fiche, @"Task Saisir[\s\S]{0,200}?if \(!Verrou\) \{ await Modifier\(bloc, c, cle, apres\)")
       && File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor")).Contains("Verrou=\"@(RecetteActive is null)\""),
    "le champ reste modifiable a la main, la proposition ne le verrouille pas");

// --- 8. LA MEME PORTE POUR TOUS LES ECRANS. Entrer par le courrier ou par la
// file des demandes doit poser le dossier REPLIE sur sa tache, pas les 521
// champs. Le repli est branche au point d entree COMMUN (OuvrirParNumero),
// donc il vaut pour tout ecran qui ouvre un dossier par son numero.
Exiger(Regex.IsMatch(home, @"private void OuvrirParNumero\([\s\S]{0,1800}?TacheEnAttente\(numeroDossier\) is \{ \} attendue[\s\S]{0,200}?OuvrirTache\("),
    "ouvrir un dossier par son numero ouvre sa tache en attente, d ou qu on vienne");
Exiger(Regex.IsMatch(home, @"private \(RecetteTache Recette, string Code\)\? TacheEnAttente\([\s\S]{0,600}?AlertesOuvertes\(\)"),
    "la tache en attente est lue dans le MEME releve que les alertes : une tache close ne replie rien");
Exiger(Regex.IsMatch(home, @"RecetteActive is not null && AffaireDeLaTache != numeroDossier"),
    "changer d affaire referme le mode replie : l arbre d un dossier n est jamais filtre par la recette d un autre");
Exiger(home.Contains("Afficher tout le dossier"),
    "le dossier reste ENTIER : le bandeau porte le bouton qui rouvre tout sans quitter la tache");
Exiger(Regex.IsMatch(home, @"BasculerToutLeDossier\(\)[\s\S]{0,200}?ToutLeDossier = !ToutLeDossier"),
    "ce bouton bascule dans les deux sens, sans fermer la tache");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0
    ? "SimuEdiCheck : vert."
    : $"SimuEdiCheck : {echecs.Count} echec(s).");
return echecs.Count == 0 ? 0 : 1;
