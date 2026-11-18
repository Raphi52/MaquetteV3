using System.Text.RegularExpressions;
using RigV3Desktop;

// Banc de l'ecran « Traitement des commandes » (PROC_COMMANDES_WEB), porte de
// NeoRIG (D:\RIG-V3) dans la maquette le 18/11/2026.
//
// Ce que la compilation ne dit pas, et que ce banc verifie :
//   1. les REGLES portees (filtre, groupes, rapprochement, delivrance,
//      transitions, enveloppe, saisie au guichet, adresse) rendent ce que
//      NeoRIG rend sur le jeu d'exemple ;
//   2. l'ecran est ATTEIGNABLE (aiguillage, menu, sujet, catalogue, perimetre) ;
//   3. il SUIT LES THEMES : aucune couleur en dur dans ses styles, et chaque
//      classe qu'il pose a sa regle ;
//   4. ses libelles sont ceux de NeoRIG (si D:\RIG-V3 est lisible d'ici).

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}
void Note(string quoi) => Console.WriteLine("NOTE : " + quoi);

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

// ============================================================ 1. REGLES

// --- Le jeu et la liste a l'ouverture : « À traiter », groupe par type.
var jeu = TraitementCommandes.JeuDemoCommandes();
Exiger(jeu.Count == 11, $"le jeu d'exemple porte 11 commandes (lu : {jeu.Count})");
var v2 = JeuDemo.CommandesWeb();
Exiger(Enumerable.Range(0, 4).All(i => jeu[i].Reference == v2[i].Numero
        && jeu[i].Siren == TraitementCommandes.Chiffres(v2[i].Siren)),
    "les 4 premieres sont les commandes des requisitions : memes numeros, memes SIREN (une seule verite)");

var groupes = TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(jeu, TraitementCommandes.CriteresParDefaut()), false);
Exiger(TraitementCommandes.Compteur(groupes) == "8 commande(s) · 3 groupe(s)",
    $"a l'ouverture : 8 commandes a traiter en 3 groupes (lu : {TraitementCommandes.Compteur(groupes)})");
Exiger(groupes.Select(g => g.Famille).SequenceEqual(new[] { FamilleCommande.EtatEndettement, FamilleCommande.Faillite, FamilleCommande.NonOpposition }),
    "ordre des groupes : endettement, non-faillite, non-opposition (OrdreGroupe de NeoRIG)");
Exiger(groupes[0].Commandes.Count == 5 && groupes[0].Commandes.Any(c => c.Famille == FamilleCommande.EtatEndettementNonInscrit)
        && groupes[0].Commandes.Any(c => c.Famille == FamilleCommande.EtatEndettementRequisition),
    "les variantes non-inscrit et requisition se rangent sous l'etat d'endettement");
Exiger(groupes[0].Commandes.Select(c => c.Reference).First() == "IG-91004",
    "dans un groupe, la plus ancienne d'abord");
Exiger(groupes.Select(g => g.Rang).SequenceEqual(new[] { 1, 4, 5 }), "rangs de couleur 1, 4, 5 comme NeoRIG");

var tous = TraitementCommandes.CriteresParDefaut() with { Etats = new HashSet<EtatCommande>() };
Exiger(TraitementCommandes.Filtrer(jeu, tous).Count == 11, "sans etat coche, toutes les commandes");
Exiger(TraitementCommandes.Filtrer(jeu, tous with { Siren = "512 004" }).Count == 2, "filtre SIREN par chiffres, espaces ignores");
Exiger(TraitementCommandes.Filtrer(jeu, tous with { Reference = "ig-908" }).Count == 2, "filtre n° de commande, casse ignoree");
Exiger(TraitementCommandes.Filtrer(jeu, tous with { Famille = FamilleCommande.Faillite }).Count == 3, "filtre par type");
Exiger(TraitementCommandes.Filtrer(jeu, tous with { Du = new DateTime(2026, 9, 7), Au = new DateTime(2026, 9, 8) }).Count == 4,
    "filtre par periode, bornes comprises");
Exiger(TraitementCommandes.Resumer(TraitementCommandes.CriteresParDefaut()) == "Toutes · À traiter",
    "la loupe resume les criteres : « Toutes · À traiter »");

// --- Onglets d'etat de la Boite de tri (direction choisie au /draft du 18/11/2026)
var onglets = TraitementCommandes.Onglets(jeu, TraitementCommandes.CriteresParDefaut());
var lusOnglets = string.Join(", ", onglets.Select(o => $"{TraitementCommandes.Libelle(o.Etat)} {o.Nombre}"));
Exiger(lusOnglets == "À traiter 8, Mise en attente 1, Validée 1, Rejetée 1",
    $"onglets a l'ouverture : quatre etats et leur compte, tous etats confondus (lu : {lusOnglets})");
var ongletsFiltres = TraitementCommandes.Onglets(jeu, TraitementCommandes.CriteresParDefaut() with { Famille = FamilleCommande.NonOpposition });
Exiger(ongletsFiltres.Select(o => o.Nombre).SequenceEqual(new[] { 1, 1, 0, 1 }),
    "les comptes des onglets suivent les autres criteres (type non-opposition : 1, 1, 0, 1)");
var avecSaisie = TraitementCommandes.JeuDemoCommandes();
TraitementCommandes.NouvelleSaisie(avecSaisie);
Exiger(TraitementCommandes.Onglets(avecSaisie, TraitementCommandes.CriteresParDefaut()).LastOrDefault() == (EtatCommande.SaisieManuelle, 1),
    "« Saisie manuelle » n'a son onglet que pendant une saisie au guichet");
var surRejetees = TraitementCommandes.SurOnglet(TraitementCommandes.CriteresParDefaut() with { Siren = "999" }, EtatCommande.Rejetee);
Exiger(surRejetees.Etats.SetEquals(new[] { EtatCommande.Rejetee }) && surRejetees.Siren == "999"
        && TraitementCommandes.Filtrer(jeu, surRejetees).Single().Reference == "IG-90601",
    "un onglet remplace l'etat et garde les autres criteres");
Exiger(!TraitementCommandes.RechercheAffinee(TraitementCommandes.CriteresParDefaut())
        && TraitementCommandes.RechercheAffinee(TraitementCommandes.CriteresParDefaut() with { Reference = "IG" }),
    "la pastille « Rechercher » ne s'allume que pour un critere au-dela de l'onglet");

var c5 = jeu.Single(c => c.Id == 5);
Exiger(TraitementCommandes.LigneReference(c5) == "N° IG-90812 · 07/09/2026 · Infogreffe · CABINET FIDAL AMIENS",
    $"ligne sous le nom dans le panneau (lu : {TraitementCommandes.LigneReference(c5)})");
Exiger(TraitementCommandes.Precisions(c5) == "IG-90812 · 07/09 · 512 004 331", $"seconde ligne de la liste (lu : {TraitementCommandes.Precisions(c5)})");
Exiger(TraitementCommandes.Titre(c5) == "État d'endettement · N° IG-90812", "titre de l'en-tete : type · N°");
Exiger(TraitementCommandes.SousTitre(c5) == "07/09/2026 · Infogreffe", "sous-titre : date · origine");

// --- Rapprochement
var transnord = TraitementCommandes.RechercherPour(jeu.Single(c => c.Id == 2));
Exiger(transnord.Entites.Count == 2, "TRANSNORD : deux dossiers pour un SIREN, donc pas de rapprochement direct");
Exiger(!TraitementCommandes.EstRapprochementDirect(jeu.Single(c => c.Id == 2)), "… et pas de pastille verte");
var billards = TraitementCommandes.RechercherPour(jeu.Single(c => c.Id == 1));
Exiger(billards.Entites.Count == 1 && billards.Entites[0].Lignes.Count == 2,
    "AMIENS BILLARDS : un seul dossier, avec son siege et son etablissement");
Exiger(TraitementCommandes.EstRapprochementDirect(jeu.Single(c => c.Id == 1)), "… donc rapprochement direct");
Exiger(TraitementCommandes.Rechercher("", "eric").Entites.Select(e => e.Designation).SequenceEqual(new[] { "MARTIN Eric (EURL)" }),
    "recherche par raison sociale, accents et casse ignores");
Exiger(TraitementCommandes.Rechercher("", "RIC").Entites.Count == 0, "un mot cherche doit COMMENCER un mot de la designation");
var pasSiren = TraitementCommandes.Rechercher("12AB", "");
Exiger(pasSiren.Entites.Count == 0 && pasSiren.Message == "« 12AB » n'est pas un SIREN (9 chiffres au plus) : il n'est pas utilisé.",
    "un SIREN non numerique est refuse avec le message de NeoRIG");
var inconnu = TraitementCommandes.Rechercher("999888777", "DUVAL TRANSPORTS");
Exiger(inconnu.Entites.Count == 0 && inconnu.Message!.StartsWith("Aucun dossier RCS ni non-inscrit ne correspond"),
    "rien trouve : la creation du non-inscrit se fait dans RIG V2");

var entiteBillards = billards.Entites[0];
Exiger(TraitementCommandes.LignesVisibles(entiteBillards, false, false).Select(l => l.Nature).SequenceEqual(new[] { TraitementCommandes.NatureSiege }),
    "R1/R4 : par defaut, le siege seul");
Exiger(TraitementCommandes.LignesVisibles(entiteBillards, false, true).Count == 2, "R3 : « Tous les établissements » montre l'etablissement");
var b2 = TraitementCommandes.Entites.Single(e => e.Gestion == "004155");
Exiger(TraitementCommandes.LignesVisibles(b2, false, false).Count == 1, "R3 bis : un dossier sans siege montre ses etablissements d'office");
var ni = TraitementCommandes.Entites.Single(e => e.NonInscrit);
Exiger(TraitementCommandes.LignesVisibles(ni, false, false).Count == 0 && TraitementCommandes.LignesVisibles(ni, true, false).Count == 1,
    "R2 : les lignes debiteur n'apparaissent qu'avec « Tous les débiteurs »");
Exiger(TraitementCommandes.ResumeRapprochement(transnord.Entites, 2) == "2 ligne(s) · 2 PM · 0 NI", "resume du rapprochement (R7)");

var martin = TraitementCommandes.Entites.Single(e => e.Designation == "MARTIN Eric (EURL)");
Exiger(TraitementCommandes.Ecarts(jeu.Single(c => c.Id == 9), martin).SequenceEqual(new[] { "Raison sociale reçu : MARTIN ERIC ≠ MARTIN Eric (EURL)" }),
    "l'ecart de raison sociale est signale sur la cible retenue");
Exiger(TraitementCommandes.Ecarts(jeu.Single(c => c.Id == 1), entiteBillards).Count == 0, "aucun ecart quand tout concorde");

// --- Controle de delivrance (DelivranceNonOpposition.Evaluer)
var batipro = TraitementCommandes.ControlerNonOpposition(jeu.Single(c => c.Id == 3));
Exiger(batipro.Issue == IssueDelivrance.Delivrance && batipro.Jours == 56 && TraitementCommandes.Delai(batipro) == "56 j ≥ 31 j",
    $"BATIPRO : formalite du 15/07, 56 j ≥ 31 j, delivrance (lu : {batipro.Issue} {batipro.Jours})");
var chenes = TraitementCommandes.ControlerNonOpposition(jeu.Single(c => c.Id == 7));
Exiger(chenes.Issue == IssueDelivrance.Reserve && TraitementCommandes.Delai(chenes) == "15 j < 31 j"
        && chenes.Motif == "Le délai depuis la dernière formalité n'est pas écoulé.",
    "LES TROIS CHENES : 15 j < 31 j, reserve « délai pas écoulé »");
var dt = TraitementCommandes.DateTravail;
Exiger(TraitementCommandes.ControlerNonOpposition(dt, dt, "", "", false, null).Motif == "Certificat non délivré : la commande ne porte ni SIREN ni n° d'affaire.",
    "ni SIREN ni n° d'affaire : refus, evalue en premier");
Exiger(TraitementCommandes.ControlerNonOpposition(dt, dt.AddDays(1), "1", null, true, dt).Issue == IssueDelivrance.Rejet,
    "commande datee apres la date de travail : refus");
Exiger(TraitementCommandes.ControlerNonOpposition(dt, dt, "123456789", null, false, null).Motif
        == "Certificat non délivré : aucune entreprise ne correspond au SIREN 123456789 de la commande.",
    "entreprise introuvable : refus qui cite le SIREN");
Exiger(TraitementCommandes.ControlerNonOpposition(dt, dt, "1", null, true, null).Issue == IssueDelivrance.Reserve,
    "aucune formalite : reserve, pas refus");
var posterieure = TraitementCommandes.ControlerNonOpposition(dt, dt, "1", null, true, dt.AddDays(3));
Exiger(posterieure.Issue == IssueDelivrance.Reserve && TraitementCommandes.Delai(posterieure) == "formalité postérieure",
    "formalite posterieure a la date de travail : reserve, sans delai negatif");

// --- Certificats de la famille faillite
var transnordSiege = TraitementCommandes.Entites.Single(e => e.Gestion == "004151");
var motifPc = TraitementCommandes.MotifBlocage(CertificatFaillite.NonFaillite, transnordSiege);
Exiger(motifPc == "Bloqué — procédure collective en cours et diffusable" && TraitementCommandes.OuvreLaBasculeFaillite(motifPc),
    "procedure collective : non-faillite bloquee, et la bascule « certificat de faillite » s'ouvre");
var motifRadie = TraitementCommandes.MotifBlocage(CertificatFaillite.NonFaillite, martin);
Exiger(motifRadie == "Bloqué — dossier radié" && !TraitementCommandes.OuvreLaBasculeFaillite(motifRadie),
    "dossier radie : refus, mais pas de bascule (le motif n'etablit pas la procedure)");
Exiger(TraitementCommandes.MotifBlocage(CertificatFaillite.NonRcs, entiteBillards) is not null
        && TraitementCommandes.MotifBlocage(CertificatFaillite.NonRcs, ni) is null,
    "non RCS : refuse a un dossier RCS, permis a un non-inscrit");
Exiger(TraitementCommandes.MotifBlocage(CertificatFaillite.NonFaillite, TraitementCommandes.Entites.Single(e => e.Gestion == "004176")) is null,
    "SCI DU PARC : non-faillite delivrable");

// --- Documents edites
var aucun = new Dictionary<CertificatFaillite, int>();
var c1 = jeu.Single(c => c.Id == 1);
Exiger(TraitementCommandes.Documents(c1, 0, 1, aucun, false, [], 0, false).Select(d => d.Document)
        .SequenceEqual(new[] { "Lettre d'accompagnement" }), "endettement sans type coche : la lettre seule");
Exiger(TraitementCommandes.Documents(c1, 4, 2, aucun, false, [], 0, false)[0] == new DocumentEdite("État d'endettement", "Sécurisé", 2, "types cochés"),
    "endettement : un etat securise, quantite commune aux types coches");
var faillite = TraitementCommandes.Documents(jeu.Single(c => c.Id == 2), 0, 0,
    new Dictionary<CertificatFaillite, int> { [CertificatFaillite.NonFaillite] = 1 }, true, [], 0, false);
Exiger(faillite[0] == new DocumentEdite("Certificat de faillite", "Sécurisé", 1, "délivré à la place du certificat de non-faillite"),
    "bascule cochee : le certificat de faillite remplace la non-faillite");
var cno = TraitementCommandes.Documents(jeu.Single(c => c.Id == 3), 0, 0, aucun, false,
    [SousTypeCno.ReductionCapital, SousTypeCno.InjonctionDePayer], 1, false);
Exiger(cno.Count == 2 && cno[0].Document == "Certificat de non-opposition (réduction de capital)",
    "non-opposition : CNO-IP n'est jamais edite ici");
Exiger(TraitementCommandes.Documents(jeu.Single(c => c.Id == 3), 0, 0, aucun, false, [SousTypeCno.ReductionCapital], 1, true)[0].Document
        == "Certificat d'opposition", "opposition formee : le certificat d'opposition remplace les non-oppositions");

// --- Transitions
Exiger(TraitementCommandes.Refus(c1, ActionCommande.Rejeter, "  ") == "Motif obligatoire pour un rejet.", "rejet sans motif : refuse");
Exiger(TraitementCommandes.Refus(c1, ActionCommande.Rejeter, new string('x', 501)) is not null, "motif de plus de 500 caracteres : refuse, jamais tronque");
Exiger(TraitementCommandes.Refus(c1, ActionCommande.MettreEnAttente, "") is null, "mise en attente : motif facultatif");
Exiger(TraitementCommandes.Refus(jeu.Single(c => c.Id == 7), ActionCommande.MettreEnAttente, "") is not null
        && TraitementCommandes.Refus(jeu.Single(c => c.Id == 7), ActionCommande.Valider, null) is null,
    "en attente : se valide ou se rejette, ne se remet pas en attente");
Exiger(TraitementCommandes.Refus(jeu.Single(c => c.Id == 10), ActionCommande.Valider, null) == "Cette commande est déjà traitée : aucune action possible.",
    "commande validee : consultation seule");

var ordre = groupes.SelectMany(g => g.Commandes).ToList();
Exiger(TraitementCommandes.Suivante(ordre, ordre[0])?.Id == ordre[1].Id, "apres un traitement, la suivante de la liste s'ouvre");
Exiger(TraitementCommandes.Suivante(ordre, ordre[^1]) is null, "apres la derniere, plus rien : « C'était la dernière commande de la liste. »");

var rejet = TraitementCommandes.Appliquer(jeu, c1, ActionCommande.Rejeter, " Doublon ", []);
Exiger(c1.Etat == EtatCommande.Rejetee && c1.Motif == "Doublon" && rejet == "Commande CW-2026-000412 rejetée.",
    "rejet : etat, motif nettoye, message de NeoRIG");

// --- Enveloppe : deux commandes du meme numero partent ensemble.
var c6 = jeu.Single(c => c.Id == 6);
var docs5 = TraitementCommandes.Documents(c5, 2, 1, aucun, false, [], 0, false);
var m5 = TraitementCommandes.Appliquer(jeu, c5, ActionCommande.Valider, null, docs5, "immobilier@fidal-amiens.fr");
Exiger(c5.Etat == EtatCommande.Validee && c5.EnvoiGroupeEnAttente
        && m5.Contains("Ils partiront dans la même enveloppe qu'une autre commande de ce numéro, encore à traiter.")
        && m5.EndsWith("Les documents partiront aussi par mail à immobilier@fidal-amiens.fr."),
    "valider la 1re de l'enveloppe : documents retenus, en attente de l'autre, mail annonce");
Exiger(TraitementCommandes.Filtrer(jeu, tous with { EnAttenteEnvoi = true }).Single().Id == 5, "« Validées en attente d'envoi » la retrouve");
Exiger(TraitementCommandes.EtatEnveloppe(TraitementCommandes.Enveloppe(jeu, c5)) == "1 à traiter avant l'envoi.", "l'enveloppe dit ce qui la retient");
var m6 = TraitementCommandes.Appliquer(jeu, c6, ActionCommande.Valider, null,
    TraitementCommandes.Documents(c6, 0, 0, new Dictionary<CertificatFaillite, int> { [CertificatFaillite.NonFaillite] = 1 }, false, [], 0, false));
Exiger(m6.EndsWith("Enveloppe envoyée : 2 commandes éditées ensemble.") && !c5.EnvoiGroupeEnAttente,
    "valider la derniere fait partir toute l'enveloppe");
Exiger(TraitementCommandes.EtatEnveloppe(TraitementCommandes.Enveloppe(jeu, c5)).StartsWith("Enveloppe envoyée"), "l'enveloppe se dit envoyee");

var jeu2 = TraitementCommandes.JeuDemoCommandes();
var c5b = jeu2.Single(c => c.Id == 5);
TraitementCommandes.Appliquer(jeu2, c5b, ActionCommande.Valider, null, docs5);
Exiger(TraitementCommandes.EnvoyerSansAttendre(jeu2, c5b) == "Enveloppe envoyée : 1 commande éditée." && !c5b.EnvoiGroupeEnAttente,
    "« Envoyer sans attendre » fait partir les validees sans attendre les autres");
Exiger(jeu2.Single(c => c.Id == 1).Etat == EtatCommande.ATraiter, "un jeu neuf a chaque ouverture : rien ne survit a la fermeture");

// --- Saisie au guichet
var saisie = TraitementCommandes.NouvelleSaisie(jeu2);
Exiger(saisie.Reference == "MAN-0001" && saisie.Etat == EtatCommande.SaisieManuelle && saisie.Origine == OrigineCommande.Guichet,
    "nouvelle commande au guichet : MAN-0001, en saisie");
Exiger(TraitementCommandes.Refus(saisie, ActionCommande.Valider, null) is not null, "une saisie non enregistree ne se valide pas");
Exiger(TraitementCommandes.EnregistrerSaisie(saisie) == "Saisissez une raison sociale ou un SIREN.", "enregistrer a vide : refuse");
saisie.Denomination = "BATIPRO SARL";
Exiger(TraitementCommandes.EnregistrerSaisie(saisie) == "Saisissez le nom du requérant.", "sans requerant : refuse");
saisie.Requerant = "M. DURAND";
Exiger(TraitementCommandes.EnregistrerSaisie(saisie) is null && saisie.Etat == EtatCommande.ATraiter, "saisie complete : la commande passe a traiter");
Exiger(TraitementCommandes.Documents(saisie, 1, 1, aucun, false, [], 0, false).Any(d => d.Document == "Facture"),
    "une commande du guichet porte sa facture");
Exiger(TraitementCommandes.NouvelleSaisie(jeu2).Reference == "MAN-0002", "la suivante prend MAN-0002");

// --- Coordonnees du destinataire
Exiger(TraitementCommandes.ControlerAdresse(jeu.Single(c => c.Id == 9).AdresseLivraison, "").Any(a => a.StartsWith("Ligne de plus de 34 caractères")),
    "une ligne de plus de 34 caracteres est signalee (la plieuse la couperait)");
Exiger(TraitementCommandes.ControlerAdresse(["a", "b", "c", "d", "e", "f", "g"], "").Any(a => a.Contains("7 lignes")), "plus de 6 lignes : signale");
Exiger(TraitementCommandes.ControlerAdresse(["a"], "Suisse").Any(a => a.Contains("hors de l'Union européenne")), "hors UE : signale");
Exiger(TraitementCommandes.ControlerAdresse(["BANQUE DU NORD", "80000 AMIENS"], "France").Count == 0, "une adresse correcte passe");

// --- Perimetre
Exiger(PerimetreProfil.Reserve(TraitementCommandes.CodeEcran).OrderBy(x => x).SequenceEqual(new[] { DomaineGed.Endettement, DomaineGed.Rcs }.OrderBy(x => x)),
    "perimetre : le RCS et l'endettement");

// ============================================================ 2. ATTEIGNABLE

var home = File.ReadAllText(Path.Combine(racine, "Components/Pages/Home.razor"));
var ecranChemin = Path.Combine(racine, "Components/Ecrans/EcranCommandesWeb.razor");
Exiger(File.Exists(ecranChemin), "l'ecran EcranCommandesWeb.razor existe");
var ecran = File.Exists(ecranChemin) ? File.ReadAllText(ecranChemin) : string.Empty;
Exiger(Regex.IsMatch(home, @"case\s+""PROC_COMMANDES_WEB""\s*:(?:\s*@\*[\s\S]*?\*@)?\s*<EcranCommandesWeb\s*/>"),
    "Home.razor aiguille PROC_COMMANDES_WEB vers <EcranCommandesWeb />");
Exiger(Regex.IsMatch(home, @"new\(""Commandes web"",\s*""[^""]+"",\s*""PROC_COMMANDES_WEB""\)"), "le menu « Recevoir » porte l'entree « Commandes web »");
Exiger(Regex.IsMatch(home, @"new\(""PROC_COMMANDES_WEB"",\s*""Commandes web""\)"), "le catalogue connait l'ecran");
Exiger(Regex.IsMatch(home, @"new\(""SUJ_AFFAIRES""[^\]]*""PROC_REQUI"",\s*""PROC_COMMANDES_WEB"""),
    "le sujet « Affaires » le range a cote des requisitions");
Exiger(ecran.Contains("TraitementCommandes.JeuDemoCommandes()") && ecran.Contains("TraitementCommandes.Appliquer(")
        && ecran.Contains("TraitementCommandes.ControlerNonOpposition("),
    "l'ecran joue les regles de Metier/TraitementCommandes.cs, il n'en a pas de copie");
// La mise en page choisie au /draft du 18/11/2026 (« Boite de tri ») : onglets
// d'etat, pastille de recherche, liste et panneau dans la meme case, decisions
// dans l'ordre de la maquette.
Exiger(ecran.Contains("rig-tcw__barre") && ecran.Contains("TraitementCommandes.Onglets(") && ecran.Contains("rig-tcw__chercher")
        && Regex.IsMatch(ecran, @"rig-tcw__scene[\s\S]*rig-tcw__liste[\s\S]*rig-tcw__panneau"),
    "mise en page « Boite de tri » : onglets d'etat, pastille Rechercher, panneau par-dessus la liste");
var decisions = new[] { ">Rejeter…<", ">Mettre en attente…<", ">Valider<" }.Select(l => ecran.IndexOf(l, StringComparison.Ordinal)).ToList();
Exiger(decisions.All(i => i > 0) && decisions.SequenceEqual(decisions.Order()),
    "decisions dans l'ordre de la maquette : Rejeter…, Mettre en attente…, Valider");

// ============================================================ 3. THEMES

var css = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-accueil.css"));
var reglesTcw = Regex.Matches(css, @"([^{}]*rig-tcw[^{}]*)\{([^{}]*)\}").Select(m => (Selecteur: m.Groups[1].Value.Trim(), Corps: m.Groups[2].Value)).ToList();
Exiger(reglesTcw.Count > 50, $"les styles de l'ecran sont dans rig-accueil.css ({reglesTcw.Count} regles)");
var enDur = reglesTcw.Where(r => Regex.IsMatch(r.Corps, @"#[0-9A-Fa-f]{3,8}\b|\brgba?\(|\bhsla?\(")).Select(r => r.Selecteur).ToList();
Exiger(enDur.Count == 0, "aucune couleur en dur : l'ecran suit les cinq themes"
    + (enDur.Count == 0 ? "" : " — en dur : " + string.Join(", ", enDur.Take(5))));
var jetons = reglesTcw.SelectMany(r => Regex.Matches(r.Corps, @"var\((--rig-[a-z0-9-]+)\)").Select(m => m.Groups[1].Value)).Distinct().ToList();
var charte = File.ReadAllText(Path.Combine(racine, "wwwroot/rig-charte.css"));
var inconnus = jetons.Where(j => !Regex.IsMatch(charte, Regex.Escape(j) + @"\s*:")).ToList();
Exiger(inconnus.Count == 0, "chaque jeton employe est defini par la charte"
    + (inconnus.Count == 0 ? "" : " — inconnus : " + string.Join(", ", inconnus)));

var classes = Regex.Matches(ecran, @"rig-tcw(?:__[a-z0-9-]+)?").Select(m => m.Value)
    .Where(c => !c.EndsWith('-')).Distinct().ToList();
var sansRegle = classes.Where(c => !Regex.IsMatch(css, @"\." + Regex.Escape(c) + @"(?![a-z0-9_-])")).ToList();
Exiger(sansRegle.Count == 0, $"chaque classe posee par l'ecran a sa regle ({classes.Count} classes)"
    + (sansRegle.Count == 0 ? "" : " — sans regle : " + string.Join(", ", sansRegle)));
var variantes = Enum.GetValues<EtatCommande>().Select(e => "rig-tcw__icone--" + TraitementCommandes.IconeEtat(e))
    .Concat(new[] { 1, 4, 5 }.Select(r => "rig-tcw__rang-" + r))
    .Concat(new[] { "succes", "info", "avertissement", "erreur" }.Select(s => "rig-tcw__message--" + s));
var variantesSansRegle = variantes.Where(v => !css.Contains("." + v + " ", StringComparison.Ordinal)).ToList();
Exiger(variantesSansRegle.Count == 0, "les classes composees (etat, rang, message) ont toutes leur regle"
    + (variantesSansRegle.Count == 0 ? "" : " — manque : " + string.Join(", ", variantesSansRegle)));

// ============================================================ 4. LIBELLES DE NeoRIG

var resx = @"D:\RIG-V3\src\RIG.Presentation.ViewModels\Resources\Rcs\CommandesWeb\Libelles.resx";
if (File.Exists(resx))
{
    var valeurs = Regex.Matches(File.ReadAllText(resx), @"<value>(.*?)</value>", RegexOptions.Singleline)
        .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToHashSet();
    var libelles = new[]
    {
        "Nouvelle commande saisie au guichet", "Rapprochements directs en tête", "Rapprochement direct",
        "Coordonnées du destinataire", "Kbis", "État de synthèse", "Kbis hors ressort", "Synthèse RNE",
        "Mettre en attente…", "Rejeter…", "Valider", "Confirmer le rejet", "Confirmer la mise en attente",
        "Motif du rejet", "Motif de la mise en attente (facultatif)", "Saisir le motif…", "Date de travail",
        "Validées en attente d'envoi", "Tous les débiteurs", "Tous les établissements", "Types d'inscription commandés",
        "Contrôle de délivrance", "Type de certificat de non-opposition", "Envoyer aussi par mail", "Données reçues",
        "Ce qui a été envoyé", "Sélectionnez une commande dans la liste pour la traiter.", "Aucune commande ne correspond aux critères.",
        "Délivrer le certificat de faillite", "Délivrer le certificat d'opposition", "Enregistrer la saisie", "Annuler la commande",
        // « Tout replier » n'y est plus : la Boite de tri montre une liste a plat, sans groupes a replier.
        "Envoyer sans attendre", "Aperçu de l'enveloppe", "Enregistrer l'adresse", "Revenir à l'adresse reçue",
        "Informations saisies", "Certificats", "Dossier concerné", "Retenir", "Tout cocher",
    };
    var absentsResx = libelles.Where(l => !valeurs.Contains(l)).ToList();
    Exiger(absentsResx.Count == 0, $"les {libelles.Length} libelles de l'ecran sont ceux de Libelles.resx de NeoRIG"
        + (absentsResx.Count == 0 ? "" : " — absents de NeoRIG : " + string.Join(" | ", absentsResx)));
    var absentsEcran = libelles.Where(l => !ecran.Contains(l, StringComparison.Ordinal)).ToList();
    Exiger(absentsEcran.Count == 0, "… et l'ecran les affiche tels quels"
        + (absentsEcran.Count == 0 ? "" : " — absents de l'ecran : " + string.Join(" | ", absentsEcran)));
    var enums = Enum.GetValues<EtatCommande>().Select(TraitementCommandes.Libelle)
        .Concat(Enum.GetValues<FamilleCommande>().Select(TraitementCommandes.Libelle))
        .Concat(Enum.GetValues<SousTypeCno>().Select(TraitementCommandes.Libelle))
        .Concat(Enum.GetValues<CertificatFaillite>().Select(TraitementCommandes.Libelle)).ToList();
    var enumsAbsents = enums.Where(l => !valeurs.Contains(l)).ToList();
    Exiger(enumsAbsents.Count == 0, "les libelles d'etat, de type, de sous-type et de certificat sont ceux de NeoRIG"
        + (enumsAbsents.Count == 0 ? "" : " — absents : " + string.Join(" | ", enumsAbsents)));
}
else
{
    Note("D:\\RIG-V3 n'est pas lisible depuis ce poste : la fidelite des libelles a NeoRIG n'est pas jouee.");
}

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("VERT : l'ecran des commandes web tient ses regles.");
    return 0;
}
Console.WriteLine($"ROUGE : {echecs.Count} regle(s) non tenue(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
