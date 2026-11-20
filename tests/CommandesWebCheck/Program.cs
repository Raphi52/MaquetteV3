using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Banc de l'ecran « Traitement des commandes » (PROC_COMMANDES_WEB), porte de
// NeoRIG (D:\RIG-V3) dans la maquette le 18/11/2026.
//
// Ce que la compilation ne dit pas, et que ce banc verifie :
//   1. les REGLES portees (filtre, groupes, rapprochement, delivrance,
//      transitions, enveloppe, saisie au guichet, adresse) rendent ce que
//      NeoRIG rend sur le jeu d'exemple ;
//   2. l'ecran est ATTEIGNABLE (aiguillage, menu, catalogue, perimetre) ;
//   3. il SUIT LES THEMES : aucune couleur en dur dans ses styles, et chaque
//      classe qu'il pose a sa regle ;
//   4. ses libelles sont ceux de NeoRIG (si D:\RIG-V3 est lisible d'ici) ;
//   5. depuis le portage React (19/11/2026), ses GESTES joues par le pont
//      (Pont/EcranCommandesWeb.cs, comme React les envoie) rendent ce que
//      TraitementCommandes calcule sur un jeu temoin : le pont delegue, il ne
//      recopie pas.

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
// Les marques de la « Correspondance » du debiteur : les memes comparaisons que les ecarts.
Exiger(TraitementCommandes.RaisonConcorde(jeu.Single(c => c.Id == 9), martin) == false
       && TraitementCommandes.SirenConcorde(jeu.Single(c => c.Id == 9), martin) is null
       && TraitementCommandes.SirenConcorde(jeu.Single(c => c.Id == 1), entiteBillards) == true
       && TraitementCommandes.RaisonConcorde(jeu.Single(c => c.Id == 1), entiteBillards) == true,
    "concordances : raison sociale differente (MARTIN), rien a comparer sans SIREN connu, tout concorde (AMIENS BILLARDS)");
// Le n° de gestion reçu (certificat de non-faillite SCI DU PARC, n° 6) face a celui du dossier.
var failliteParc = jeu.Single(c => c.Id == 6);
var dossierParc = TraitementCommandes.Entites.Single(e => e.Gestion == "004176");
Exiger(TraitementCommandes.GestionConcorde(failliteParc, dossierParc) == true
       && TraitementCommandes.GestionConcorde(failliteParc, entiteBillards) == false
       && TraitementCommandes.GestionConcorde(jeu.Single(c => c.Id == 1), entiteBillards) is null
       && TraitementCommandes.Ecarts(failliteParc, dossierParc).Count == 0
       && TraitementCommandes.Ecarts(failliteParc, entiteBillards).Last() == "N° de gestion reçu : 004176 ≠ 2026B01067",
    "n° de gestion : 004176 concorde avec son dossier, ecart « N° de gestion reçu : 004176 ≠ 2026B01067 » sinon, rien a comparer sans n° reçu");

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

// PORTAGE EN REACT (lot 2, 19/11/2026). L'ecran tenait en UN fichier .razor
// (balisage + @code). Il a ete coupe en trois : la coquille .razor qui montait
// l'ile React (retiree avec Blazor le 19/11/2026), web/src/ecrans/EcranCommandesWeb.tsx (le BALISAGE) et
// Pont/EcranCommandesWeb.cs (l'ETAT et les appels aux regles, l'ancien @code
// deplace tel quel dans SessionCommandesWeb). « ecran » designe donc desormais
// le .tsx ET le pont : ce que le .razor contenait a lui seul. Une regle de mise
// en page se lit dans le .tsx, un appel de regle dans le pont. La section 5 est
// neuve : elle JOUE les gestes par le pont et compare a TraitementCommandes.
// Lot 2 du passage a React (19/11/2026) : l'aiguillage est web/src/plan/Aiguillage.tsx,
// le catalogue vit dans Pont/PlanTravail.cs (le @code de Home.razor).
var home = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
var aiguillage = File.ReadAllText(Path.Combine(racine, "web/src/plan/Aiguillage.tsx"));
var ecranChemin = Path.Combine(racine, "Components/Ecrans/EcranCommandesWeb.razor");
var tsxChemin = Path.Combine(racine, "web/src/ecrans/EcranCommandesWeb.tsx");
var pontChemin = Path.Combine(racine, "Pont/EcranCommandesWeb.cs");
// Blazor est retire depuis le 19/11/2026 : plus de coquille .razor, l'ecran est React.
Exiger(!File.Exists(ecranChemin), "plus de coquille EcranCommandesWeb.razor : l'ecran est le .tsx");
Exiger(File.Exists(tsxChemin) && File.Exists(pontChemin), "l'ecran React EcranCommandesWeb.tsx et son pont Pont/EcranCommandesWeb.cs existent");
var tsx = File.Exists(tsxChemin) ? File.ReadAllText(tsxChemin) : string.Empty;
var pont = File.Exists(pontChemin) ? File.ReadAllText(pontChemin) : string.Empty;
var ecran = tsx + "\n" + pont;
// Le CODE du .tsx, sans ses commentaires (son en-tete cite Metier/TraitementCommandes.cs).
var codeTsx = Regex.Replace(tsx, @"/\*[\s\S]*?\*/|//[^\n]*", "");
// Depuis le lot 2 du plan « Vue du dossier » (20/11/2026), l'ecran recoit en plus,
// FACULTATIF, le contexte de l'affaire d'ou on l'ouvre : il se monte toujours sans
// parametre obligatoire, et l'aiguillage le monte a la cle de cette affaire.
Exiger(Regex.IsMatch(tsx, @"export function EcranCommandesWeb\(\{ dossier = null \}: ProprietesEcranCommandesWeb\)")
       && Regex.IsMatch(tsx, @"export interface ProprietesEcranCommandesWeb \{\s*(?:/\*\*[^\n]*\*/\s*)?dossier\?: ContexteDossierVue \| null\s*\}"),
    "l'ecran React se monte sans parametre obligatoire, comme sa coquille le faisait (seul dossier?, l'affaire d'ou on l'ouvre)");
Exiger(Regex.IsMatch(aiguillage, @"case\s+'PROC_COMMANDES_WEB'\s*:(?:\s*//[^\n]*)*\s*return <EcranCommandesWeb key=\{cle\} dossier=\{dossier\} />"),
    "le plan de travail aiguille PROC_COMMANDES_WEB vers <EcranCommandesWeb />, avec le contexte de l'affaire");
// Le catalogue du menu de gauche vit dans Pont/MenuAccueil.cs depuis le lot 1 du
// passage a React (19/11/2026) : l'entree s'y lit, au meme format.
var menuAccueil = File.ReadAllText(Path.Combine(racine, "Pont/MenuAccueil.cs"));
Exiger(Regex.IsMatch(menuAccueil, @"new\(""Commandes web"",\s*""[^""]+"",\s*""PROC_COMMANDES_WEB""\)"), "le menu « Recevoir » porte l'entree « Commandes web »");
Exiger(!Regex.IsMatch(home, @"new\(""Commandes web"",\s*""[^""]+"",\s*""PROC_COMMANDES_WEB""\)"), "le plan de travail ne tient pas de copie de l'entree de menu");
Exiger(Regex.IsMatch(home, @"new\(""PROC_COMMANDES_WEB"",\s*""Commandes web""\)"), "le catalogue connait l'ecran");
// (Le « sujet Affaires » qui le rangeait dans le bandeau des ecrans voisins a ete
// retire avec ce bandeau, de tous les ecrans, le 19/11/2026 — conv-2010.)
Exiger(pont.Contains("TraitementCommandes.JeuDemoCommandes()") && pont.Contains("TraitementCommandes.Appliquer(")
        && pont.Contains("TraitementCommandes.ControlerNonOpposition(")
        && !codeTsx.Contains("TraitementCommandes") && !Regex.IsMatch(codeTsx, @"\bMath\.|new Date\(|\.sort\("),
    "l'ecran joue les regles de Metier/TraitementCommandes.cs, il n'en a pas de copie");
Exiger(tsx.Contains("'EcranCommandesWeb.Ouvrir'") && tsx.Contains("'EcranCommandesWeb.Agir'") && tsx.Contains("'EcranCommandesWeb.Fermer'")
        && pont.Contains("[JSInvokable(\"EcranCommandesWeb.Ouvrir\")]") && pont.Contains("[JSInvokable(\"EcranCommandesWeb.Agir\")]")
        && pont.Contains("[JSInvokable(\"EcranCommandesWeb.Fermer\")]"),
    "l'ecran React appelle les trois methodes de SON pont, et le pont les expose");
// La mise en page choisie au /draft du 19/11/2026 (« Proue », qui remplace la
// « Boite de tri » du 18/11) : la recherche en pilule EN HAUT, une tuile chiffree
// (.rig-chiffre, grand rond et anneau) par onglet d'etat, puis la liste et le
// panneau A COTE ; decisions dans l'ordre de la maquette. En React, le panneau est
// le composant <Panneau>, pose dans la scene APRES la liste ; c'est lui qui porte
// rig-tcw__panneau. L'en-tete date + compte « à traiter » (.rig-proue) qui
// surmontait la pilule a ete retire (conv-2010) : il prenait de la place.
Exiger(!codeTsx.Contains("rig-proue") && !codeTsx.Contains("rig-tcw__barre") && !pont.Contains("Proue(")
        && pont.Contains("TraitementCommandes.Onglets(") && tsx.Contains("rig-tcw__chercher")
        // ({} : ce qui reste d'un commentaire JSX une fois les commentaires otes)
        && Regex.IsMatch(codeTsx, @"<div className=""rig-tcw"">\s*(\{\}\s*)?<div className=""rig-tcw__cherche"">")
        && Regex.IsMatch(tsx, @"rig-tcw__pilule[\s\S]*rig-tcw__tuiles[\s\S]*rig-tcw__scene[\s\S]*rig-tcw__liste[\s\S]*<Panneau\b")
        && Regex.IsMatch(tsx, @"function\s+Tuile\b[\s\S]*?rig-chiffre rig-chiffre--[\s\S]*?rig-chiffre__anneau")
        && Regex.IsMatch(tsx, @"function\s+Panneau\b[\s\S]*?className=""rig-tcw__panneau"""),
    "mise en page « Proue » : recherche en pilule en haut (sans en-tete au-dessus), une tuile par etat, liste et panneau a cote");
// Une tuile est un FILTRE : son clic remonte « ChoisirOnglet » avec son etat ; la
// pilule remonte chaque frappe (« FiltrerRapide »). Le filtre est dans le pont.
Exiger(codeTsx.Contains("geste('ChoisirOnglet', t.etat)") && codeTsx.Contains("geste('FiltrerRapide', texte)")
        && pont.Contains("case \"FiltrerRapide\":") && !codeTsx.Contains("Deposer") && !pont.Contains("Deposer"),
    "les tuiles filtrent par « ChoisirOnglet », la pilule par « FiltrerRapide » ; plus aucun glisser-deposer");
var decisions = new[] { ">Rejeter…<", ">Mettre en attente…<", ">Valider<" }.Select(l => tsx.IndexOf(l, StringComparison.Ordinal)).ToList();
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
        // « Tout replier » n'y est pas : l'accordeon (conv-2024) replie ses groupes un a un.
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

// ============================================================ 5. GESTES JOUES PAR LE PONT
// EcranCommandesWeb.Agir(session, geste, valeur) est l'appel que fait React a
// chaque clic ou frappe. Chaque vue rendue est comparee a ce que
// TraitementCommandes calcule lui-meme sur un jeu TEMOIN, traite en parallele.

var temoin = TraitementCommandes.JeuDemoCommandes();
var defaut = TraitementCommandes.CriteresParDefaut();
List<CommandeRecue> OrdreTemoin(CriteresCommandes cr, bool directs) =>
    TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(temoin, cr), directs).SelectMany(g => g.Commandes).ToList();
List<int> Ids(CriteresCommandes cr, bool directs) => OrdreTemoin(cr, directs).Select(c => c.Id).ToList();
string OngletsTemoin(EtatCommande actif) => string.Join(", ", TraitementCommandes.Onglets(temoin, defaut)
    .Select(o => $"{TraitementCommandes.Libelle(o.Etat)} {o.Nombre}{(o.Etat == actif ? "*" : "")}"));

var vue = EcranCommandesWeb.Ouvrir();
VueCommandesWeb Geste(string geste, object? valeur = null) =>
    vue = EcranCommandesWeb.Agir(vue.Session, geste, JsonSerializer.SerializeToElement(valeur));
// Proue : un onglet d'etat = une tuile, qui filtre la liste sur son etat.
string OngletsLus() => string.Join(", ", vue.Tuiles.Select(o => $"{o.Libelle} {o.Nombre}{(o.Actif ? "*" : "")}"));
LigneCw[] LignesLues() => vue.Lignes;
IEnumerable<int> IdsLus() => LignesLues().Select(l => l.Id);
string? OngletActif() => vue.Tuiles.SingleOrDefault(o => o.Actif)?.Etat;

var aTraiterOuverture = vue.Tuiles.Single(o => o.Etat == "ATraiter");
Exiger(vue.Compteur == "8 commande(s) · 3 groupe(s)" && aTraiterOuverture is { Nombre: 8, Detail: "3 groupe(s)" }
       && OngletsLus() == OngletsTemoin(EtatCommande.ATraiter),
    $"joue : a l'ouverture, 8 commandes en 3 groupes, tuile « À traiter » active (lu : {vue.Compteur} | {OngletsLus()})");
Exiger(IdsLus().SequenceEqual(Ids(defaut, false))
       && LignesLues().All(l => l.Precisions == TraitementCommandes.Precisions(temoin.Single(c => c.Id == l.Id))
                           && l.Direct == TraitementCommandes.EstRapprochementDirect(temoin.Single(c => c.Id == l.Id))),
    "joue : la liste suit l'ordre de Regrouper, ses precisions et pastilles sont celles de TraitementCommandes");
// Accordeon (conv-2024) : l'ecran range les lignes sous un titre par groupe, par
// le rang. Les groupes de la vue sont ceux de Regrouper, dans son ordre, et
// chaque ligne trouve le sien.
var groupesTemoin = TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(temoin, defaut), false);
Exiger(vue.Groupes.Select(g => (g.Rang, g.Titre, g.Nombre))
           .SequenceEqual(groupesTemoin.Select(g => (g.Rang, g.Titre, g.Commandes.Count)))
       && vue.Groupes.Select(g => g.Rang).Distinct().Count() == vue.Groupes.Length
       && LignesLues().All(l => vue.Groupes.Any(g => g.Rang == l.Rang)),
    $"joue : les titres de groupe sont ceux de Regrouper, chacun avec son nombre (lu : {string.Join(" | ", vue.Groupes.Select(g => $"{g.Titre} {g.Nombre}"))})");
// Chaque tuile, CLIQUEE, montre dans la liste les commandes de son etat, dans
// l'ordre de Regrouper ; son nombre est celui de la liste, ses groupes son detail,
// et les parts des anneaux font le tout.
var tuilesFausses = new List<string>();
foreach (var t in vue.Tuiles.ToList())
{
    Geste("ChoisirOnglet", t.Etat);
    var surEtat = TraitementCommandes.SurOnglet(defaut, Enum.Parse<EtatCommande>(t.Etat));
    var groupesEtat = TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(temoin, surEtat), false).Count;
    if (OngletActif() != t.Etat || !IdsLus().SequenceEqual(Ids(surEtat, false)) || LignesLues().Length != t.Nombre
        || t.Detail != $"{groupesEtat} groupe(s)" || vue.Panneau is not null)
        tuilesFausses.Add(t.Etat);
}
Geste("ChoisirOnglet", "ATraiter");
Exiger(tuilesFausses.Count == 0 && vue.Tuiles.Sum(o => o.Part) is >= 99 and <= 101,
    $"joue : chaque tuile filtre la liste sur son etat (Regrouper), compte ses groupes, et les anneaux font 100 % (fausses : "
    + string.Join(", ", tuilesFausses) + ")");

Geste("ChoisirOnglet", "Rejetee");
Exiger(OngletActif() == "Rejetee" && IdsLus().SequenceEqual(new[] { 11 }), "joue : l'onglet « Rejetée » ne montre que IG-90601");
Geste("ChoisirOnglet", "ATraiter");
// Choisir une commande d'un AUTRE etat (un membre d'enveloppe) active sa tuile.
Geste("Choisir", 11);
Exiger(OngletActif() == "Rejetee" && vue.Panneau?.Id == 11 && vue.Panneau.Traitee is { Rejetee: true }
       && LignesLues().Single(l => l.Active).Id == 11,
    "joue : choisir la commande rejetee IG-90601 ouvre son panneau et active la tuile « Rejetée », ou elle est en surbrillance");
Geste("ChoisirOnglet", "ATraiter");

Geste("Basculer", "RechercheOuverte");
Geste("Texte", new { champ = "SirenCherche", valeur = "512 004" });
Geste("Rechercher");
var critSiren = defaut with { Siren = "512 004" };
Exiger(vue.RechercheOuverte && vue.RechercheAffinee && vue.ResumeCriteres == TraitementCommandes.Resumer(critSiren)
       && IdsLus().SequenceEqual(Ids(critSiren, false)) && LignesLues().Length > 0,
    $"joue : la saisie d'un SIREN puis « Rechercher » filtre comme Filtrer (lu : {LignesLues().Length} ligne(s), « {vue.ResumeCriteres} »)");
Geste("Texte", new { champ = "SirenCherche", valeur = "" });
Geste("Rechercher");
Exiger(!vue.RechercheAffinee && IdsLus().SequenceEqual(Ids(defaut, false)), "joue : SIREN efface, la liste revient entiere");

var c1t = temoin.Single(c => c.Id == 1);
Geste("Choisir", 1);
Exiger(vue.Panneau is { } p1 && p1.Titre == TraitementCommandes.Titre(c1t) && p1.Reference == TraitementCommandes.LigneReference(c1t)
       && p1.Rapprochement is { Entites.Length: 1 } && !vue.RechercheOuverte,
    "joue : choisir une commande ouvre son panneau (titre, reference, rapprochement de TraitementCommandes) et replie la recherche");
// « Correspondance » du debiteur (/draft du 19/11/2026) : la commande face a chaque
// dossier trouve ; la recherche reste repliee tant qu'un dossier est trouve.
Exiger(vue.Panneau!.Rapprochement is { Debiteur: true, RechercheOuverte: false, Message: null } r1
       && r1.CommandeSiren == TraitementCommandes.FormaterSiren(c1t.Siren) && r1.CommandeRaison == c1t.Denomination
       && r1.Entites.Single() is { NbAdresses: 2, NbInscriptions: 17, SirenConcorde: true, RaisonConcorde: true,
                                    Ecarts.Length: 0, Lignes.Length: 1 },
    "correspondance : commande face au dossier (SIREN et raison sociale concordent, 2 adresses, 17 inscriptions), recherche repliee");
Geste("Basculer", "RechercheRapprochementOuverte");
Exiger(vue.Panneau!.Rapprochement!.RechercheOuverte, "correspondance : « Chercher un autre dossier » deplie la recherche");
Geste("Basculer", "RechercheRapprochementOuverte");
// La meme Correspondance pour le dossier concerne (non-opposition, faillite) : la
// faillite SCI DU PARC (n° 6) face a son dossier, recherche repliee ; et CHAQUE
// dossier trouve porte ses ecarts, meme non retenu (avant : seul le retenu).
var c6t = temoin.Single(c => c.Id == 6);
Geste("Choisir", 6);
Exiger(vue.Panneau!.Rapprochement is { Debiteur: false, Faillite: true, RechercheOuverte: false, Message: null } r6
       && r6.CommandeSiren == TraitementCommandes.FormaterSiren(c6t.Siren) && r6.CommandeRaison == c6t.Denomination
       && r6.CommandeGestion == "004176"
       && r6.Entites.Single() is { Gestion: "004176", SirenConcorde: true, RaisonConcorde: true, GestionConcorde: true,
                                    Ecarts.Length: 0, Retenue: false, Distinctions.Length: 0 },
    "correspondance : la faillite SCI DU PARC face au dossier 004176 (n° de gestion, SIREN et raison sociale concordent), recherche repliee");
Geste("Basculer", "RechercheRapprochementOuverte");
Exiger(vue.Panneau!.Rapprochement!.RechercheOuverte, "correspondance : le dossier concerne deplie aussi sa recherche");
Geste("Texte", new { champ = "RechSiren", valeur = "" });
Geste("Texte", new { champ = "RechRaison", valeur = "AMIENS BILLARDS" });
Geste("RelancerRapprochement");
var autres = vue.Panneau!.Rapprochement!.Entites;
Exiger(autres.Length == 1 && autres.All(e => !e.Retenue && e.RaisonConcorde == false && e.SirenConcorde == false
                                            && e.GestionConcorde == false
                                            && e.Ecarts.SequenceEqual(TraitementCommandes.Ecarts(c6t, TraitementCommandes.Entite(e.Cle)!))
                                            && e.Ecarts.Contains("Raison sociale reçu : SCI DU PARC ≠ AMIENS BILLARDS")
                                            && e.Ecarts.Contains("N° de gestion reçu : 004176 ≠ 2026B01067")),
    "correspondance : le dossier AMIENS BILLARDS trouve pour la faillite SCI DU PARC porte ses ecarts sans etre retenu (lu : "
    + string.Join(" ; ", autres.Select(e => $"{e.Cle}={string.Join("|", e.Ecarts)}")) + ")");
// Ce qui departage des dossiers trouves ne se montre que s'ils concordent TOUS :
// un dossier seul (SCI DU PARC, AMIENS BILLARDS) ou des dossiers en ecart (les deux
// TRANSNORD SA cherches pour la faillite SCI DU PARC) n'ont rien a departager.
Geste("Texte", new { champ = "RechRaison", valeur = "TRANSNORD" });
Geste("RelancerRapprochement");
var enEcart = vue.Panneau!.Rapprochement!.Entites;
Exiger(autres.Single().Distinctions.Length == 0
       && enEcart.Length == 2 && enEcart.All(e => e.Ecarts.Length > 0 && e.Distinctions.Length == 0 && e.Lignes.All(l => !l.Distingue)),
    "distinctions : rien a departager pour un dossier seul ni pour des dossiers en ecart (lu : "
    + string.Join(" ; ", enEcart.Select(e => $"{e.Cle}={string.Join("|", e.Distinctions)}")) + ")");
// La faillite TRANSNORD SA (n° 2) trouve 004151 (siege, BOVES) et 004155
// (etablissement, ABBEVILLE), sans ecart : chaque carte met en avant son n° de
// gestion et son adresse, pas le SIREN ni la raison sociale qu'ils partagent.
Geste("Choisir", 2);
var concordants = vue.Panneau!.Rapprochement!.Entites;
Exiger(concordants.Length == 2
       && concordants.All(e => e.Ecarts.Length == 0 && e.Distinctions.SequenceEqual(new[] { "gestion", "adresse" })
                               && e.Lignes.Length == 1 && e.Lignes.All(l => l.Distingue)),
    "distinctions : les deux dossiers TRANSNORD SA concordants se departagent par n° de gestion et adresse (lu : "
    + string.Join(" ; ", concordants.Select(e => $"{e.Cle}={string.Join("|", e.Distinctions)}")) + ")");
Geste("Choisir", 1);
Geste("OuvrirBande", "Rejeter");
Exiger(vue.Panneau?.Pied?.Bande is { Rejet: true, ConfirmerRefuse: true }, "joue : la bande de rejet s'ouvre, confirmation fermee sans motif");
Geste("Decider", "Rejeter");
Exiger(vue.Message is { Texte: "Motif obligatoire pour un rejet.", Severite: "avertissement" } && vue.Panneau?.Id == 1,
    "joue : rejeter sans motif est refuse, la commande reste ouverte");
Geste("Texte", new { champ = "MotifSaisi", valeur = " Doublon " });
Exiger(vue.Panneau?.Pied?.Bande is { ConfirmerRefuse: false, MotifSaisi: " Doublon " }, "joue : le motif saisi ouvre la confirmation");
var suivante = TraitementCommandes.Suivante(OrdreTemoin(defaut, false), c1t);
var attenduRejet = TraitementCommandes.Appliquer(temoin, c1t, ActionCommande.Rejeter, " Doublon ", []);
Geste("Decider", "Rejeter");
Exiger(vue.Message is { Severite: "succes" } m1 && m1.Texte == attenduRejet && suivante is not null && vue.Panneau?.Id == suivante.Id,
    $"joue : avec un motif, le rejet passe (« {attenduRejet} ») et la suivante s'ouvre");
Exiger(OngletsLus() == OngletsTemoin(EtatCommande.ATraiter) && IdsLus().SequenceEqual(Ids(defaut, false)),
    $"joue : onglets et liste suivent l'etat rejete (lu : {OngletsLus()})");

Geste("NouvelleCommande");
Exiger(vue.Panneau?.Saisie is not null && OngletActif() == "SaisieManuelle"
       && vue.Message?.Texte.StartsWith("Commande MAN-0001 créée") == true,
    "joue : « Nouvelle commande saisie au guichet » ouvre la saisie MAN-0001 dans son onglet");
Geste("EnregistrerSaisie");
Exiger(vue.Message is { Texte: "Saisissez une raison sociale ou un SIREN.", Severite: "avertissement" },
    "joue : enregistrer une saisie vide est refuse");
Geste("ChoisirTypeSaisi", "Faillite");
Geste("Texte", new { champ = "Denomination", valeur = "BATIPRO SARL" });
Geste("Texte", new { champ = "Requerant", valeur = "M. DURAND" });
Exiger(vue.Panneau?.Saisie is { Famille: "Faillite", Denomination: "BATIPRO SARL", Requerant: "M. DURAND" },
    "joue : la saisie au guichet (type choisi, raison sociale, requerant) est retenue");
Geste("EnregistrerSaisie");
Exiger(vue.Message is { Texte: "Commande MAN-0001 enregistrée.", Severite: "succes" } && OngletActif() == "ATraiter"
       && vue.Panneau is { Saisie: null, Nom: "BATIPRO SARL" },
    "joue : la saisie complete est enregistree et passe « À traiter »");
// Le temoin suit la meme saisie, par Metier directement.
var saisieTemoin = TraitementCommandes.NouvelleSaisie(temoin);
saisieTemoin.Famille = FamilleCommande.Faillite;
saisieTemoin.Denomination = "BATIPRO SARL";
saisieTemoin.Requerant = "M. DURAND";
Exiger(TraitementCommandes.EnregistrerSaisie(saisieTemoin) is null && IdsLus().SequenceEqual(Ids(defaut, false)),
    $"joue : la commande du guichet prend sa place dans la liste, comme dans Regrouper (lu : {string.Join(",", IdsLus())})");

// Le tri de cet ecran : « Rapprochements directs en tête ». Sur le jeu d'exemple
// a l'ouverture, les directs sont DEJA en tete de leur groupe : la bascule n'y
// change rien a voir. La commande du guichet (type non-faillite, BATIPRO SARL,
// un seul dossier) est plus recente que TRANSNORD (deux dossiers) : elle doit
// remonter devant lui.
Geste("BasculerDirects");
Exiger(vue.DirectsEnTete && IdsLus().SequenceEqual(Ids(defaut, true)) && !Ids(defaut, true).SequenceEqual(Ids(defaut, false)),
    $"joue : « Rapprochements directs en tête » reordonne la liste comme Regrouper(…, true) (lu : {string.Join(",", IdsLus())} ; sans : {string.Join(",", Ids(defaut, false))})");
Geste("BasculerDirects");
Exiger(!vue.DirectsEnTete && IdsLus().SequenceEqual(Ids(defaut, false)), "joue : … et la rebascule rend l'ordre d'origine");

Geste("OuvrirCoordonnees");
Geste("Adresse", new { cle = "Voie", valeur = new string('X', 40) });
Exiger(vue.Coordonnees is { } co1 && co1.Alertes.Any(a => a.StartsWith("Ligne de plus de 34 caractères")),
    "joue : une ligne d'adresse trop longue est signalee dans « Coordonnées du destinataire »");
Geste("FermerCoordonnees");
Exiger(vue.Coordonnees is null, "joue : « Fermer » referme les coordonnees");

// ---- L'ESCALIER (/draft du 19/11/2026, conv-1993, direction « Escalier ») : trois
// marches (dossier, choix, edition) qui sont les morceaux de la regle qui ferme
// Valider. Une session NEUVE : le jeu d'exemple tel qu'a l'ouverture.
EcranCommandesWeb.Fermer(vue.Session);
vue = EcranCommandesWeb.Ouvrir();
// a) Pour chaque commande de la liste : le compte affiche AVANT de l'ouvrir est celui
//    de son escalier A L'OUVERTURE, et Valider se ferme sur le manque de la premiere
//    marche non faite, mot pour mot (une seule regle, pas deux qui divergent).
foreach (var ligne in LignesLues().ToList())
{
    var avant = ligne.Avancement;
    Geste("Choisir", ligne.Id);
    var pe = vue.Panneau!;
    var faites = pe.Etapes.Count(e => e.Faite);
    var manque = pe.Etapes.FirstOrDefault(e => !e.Faite)?.Manque;
    Exiger(pe.Etapes.Length == 3 && avant == $"{faites}/3" && ligne.Prete == (faites == 3)
           && pe.Etapes.All(e => e.Faite == (e.Manque is null))
           && pe.Pied!.Blocage == manque && pe.Pied.Avancement == (manque is null ? "" : $"{faites} / 3"),
        $"escalier : {ligne.Nom} n° {ligne.Id} — liste « {avant} », marches {faites}/3, Valider fermé sur « {pe.Pied?.Blocage} »");
}
// b) SCI DU PARC (etat d'endettement, IG-90812) : le debiteur manque, types et edition sont prets.
Geste("Choisir", 5);
Exiger(vue.Panneau!.Etapes.Select(e => $"{e.Nom}|{e.Faite}|{e.Resume}").SequenceEqual(new[]
       {
           "Débiteur|False|", "Types d'inscription commandés|True|NP, Z · Qté 1", "Édition|True|Édition : 2 document(s)",
       })
       && vue.Panneau.Etapes[0].Manque == "Retenez le débiteur dans le rapprochement pour rattacher la commande."
       && LignesLues().Single(l => l.Id == 5).Avancement == "2/3" && !vue.Panneau.ChoixDeplie,
    "escalier : SCI DU PARC — Débiteur à faire, Types « NP, Z · Qté 1 » et Édition prêts, liste « 2/3 » (lu : "
    + string.Join(" ; ", vue.Panneau.Etapes.Select(e => $"{e.Nom}|{e.Faite}|{e.Resume}")) + ")");
// c) Une marche faite se replie ; « Modifier » la rouvre, et choisir une commande la replie.
Geste("Basculer", "ChoixDeplie");
Exiger(vue.Panneau!.ChoixDeplie, "escalier : « Modifier » rouvre la marche des types");
// d) Decocher tous les types : la marche du choix ET l'edition tombent, avec le motif de toujours.
Geste("CocherTous", false);
Exiger(vue.Panneau!.Etapes.Select(e => e.Faite).SequenceEqual(new[] { false, false, false })
       && vue.Panneau.Pied!.Blocage == "Retenez le débiteur dans le rapprochement pour rattacher la commande."
       && vue.Panneau.Etapes[1].Manque == "Cochez au moins un type d'inscription, avec une quantité."
       && LignesLues().Single(l => l.Id == 5).Avancement == "0/3",
    "escalier : sans type coché, Types et Édition passent à faire, et la liste le suit (« 0/3 »)");
Geste("CocherTous", true);
// e) Types coches mais quantite a zero : le choix est fait, il n'y a rien a editer.
Geste("Compteur", new { cible = "QuantiteInscriptions", sens = "valeur" });
Exiger(vue.Panneau!.Etapes.Select(e => e.Faite).SequenceEqual(new[] { false, true, false })
       && vue.Panneau.Etapes[2].Manque == "Cochez au moins un type d'inscription, avec une quantité.",
    "escalier : quantité à zéro — Types reste fait, l'Édition manque");
Geste("Compteur", new { cible = "QuantiteInscriptions", sens = "valeur" });
// f) Retenir le seul dossier trouve : les trois marches sont faites, Valider s'ouvre.
Geste("Retenir", vue.Panneau!.Rapprochement!.Entites.Single().Cle);
Exiger(vue.Panneau!.Etapes.All(e => e.Faite) && vue.Panneau.Etapes[0].Resume == "SCI DU PARC"
       && vue.Panneau.Pied!.Blocage is null && vue.Panneau.Pied.Avancement == ""
       && LignesLues().Single(l => l.Id == 5) is { Avancement: "3/3", Prete: true },
    "escalier : débiteur retenu — 3/3, Valider ouvert, la ligne passe « prête »");
Geste("Choisir", 6);
Exiger(!vue.Panneau!.ChoixDeplie && LignesLues().Single(l => l.Id == 5).Avancement == "3/3",
    "escalier : une autre commande s'ouvre repliée ; SCI DU PARC garde son dossier retenu dans la liste");
// g) Une commande traitee n'a ni marches ni compte.
Geste("ChoisirOnglet", "Validee");
Exiger(LignesLues().Length > 0 && LignesLues().All(l => l.Avancement is null && !l.Prete),
    "escalier : une commande traitée n'affiche aucun compte");

// ---- RECHERCHE EN PILULE (« Proue », /draft du 19/11/2026) : un seul champ,
// applique a chaque frappe, qui cherche dans la raison sociale, le n° de commande
// et le SIREN. Une session NEUVE, comparee a un jeu NEUF filtre par Metier
// directement (le temoin du haut a deja rejete IG-90601 et saisi MAN-0001).
EcranCommandesWeb.Fermer(vue.Session);
vue = EcranCommandesWeb.Ouvrir();
var neuf = TraitementCommandes.JeuDemoCommandes();
List<int> IdsNeufs(CriteresCommandes cr) =>
    TraitementCommandes.Regrouper(TraitementCommandes.Filtrer(neuf, cr), false).SelectMany(g => g.Commandes).Select(c => c.Id).ToList();
string TuilesNeuves(CriteresCommandes cr) => string.Join(", ", TraitementCommandes.Onglets(neuf, cr)
    .Select(o => $"{TraitementCommandes.Libelle(o.Etat)} {o.Nombre}{(o.Etat == EtatCommande.ATraiter ? "*" : "")}"));
// a) Une raison sociale, sans la casse : la liste ET les tuiles se recomptent ; la
//    commande ouverte reste ouverte.
Geste("Choisir", 1);
Geste("FiltrerRapide", "parc");
var surParc = defaut with { Texte = "parc" };
Exiger(vue.Rapide == "parc" && IdsLus().SequenceEqual(IdsNeufs(surParc)) && LignesLues().Length > 0
       && LignesLues().All(l => l.Nom.Contains("PARC", StringComparison.Ordinal))
       && OngletsLus() == TuilesNeuves(surParc) && vue.Panneau?.Id == 1,
    $"pilule : « parc » ne garde que les PARC, recompte les tuiles et laisse la commande ouverte (lu : {string.Join(",", IdsLus())} | {OngletsLus()})");
// b) Un n° de commande, en minuscules.
Geste("FiltrerRapide", "ig-90812");
Exiger(IdsLus().SequenceEqual(IdsNeufs(defaut with { Texte = "ig-90812" })) && IdsLus().Contains(5),
    $"pilule : « ig-90812 » trouve la commande IG-90812 (lu : {string.Join(",", IdsLus())})");
// c) Un SIREN tape avec ses espaces.
var avecSiren = neuf.First(c => c.Etat == EtatCommande.ATraiter && c.Siren.Length == 9);
var sirenTape = $"{avecSiren.Siren[..3]} {avecSiren.Siren[3..6]}";
Geste("FiltrerRapide", sirenTape);
Exiger(IdsLus().SequenceEqual(IdsNeufs(defaut with { Texte = sirenTape })) && IdsLus().Contains(avecSiren.Id),
    $"pilule : « {sirenTape} » trouve {avecSiren.Denomination} par son SIREN (lu : {string.Join(",", IdsLus())})");
// d) La recherche detaillee garde le texte de la pilule ; l'effacer rend tout.
Geste("Rechercher");
var garde = vue.Rapide == sirenTape && IdsLus().SequenceEqual(IdsNeufs(defaut with { Texte = sirenTape }));
Geste("FiltrerRapide", "");
Exiger(garde && vue.Rapide == "" && IdsLus().SequenceEqual(IdsNeufs(defaut)) && OngletsLus() == TuilesNeuves(defaut),
    "pilule : « Rechercher » garde le texte de la pilule ; l'effacer rend la liste et les tuiles entieres");
// e) La regle seule : accents ignores ; un texte qui n'est pas fait de chiffres ne
//    cherche pas dans le SIREN.
var accent = new CommandeRecue { Denomination = "SOCIÉTÉ GÉNÉRALE", Reference = "IG-91004", Siren = "552120222" };
Exiger(TraitementCommandes.Correspond(accent, "societe gen") && TraitementCommandes.Correspond(accent, "552 120")
       && TraitementCommandes.Correspond(accent, "ig-910") && !TraitementCommandes.Correspond(accent, "X-55212")
       && TraitementCommandes.Correspond(accent, "  "),
    "pilule : « societe gen » trouve SOCIÉTÉ GÉNÉRALE, « 552 120 » son SIREN, et « X-55212 » ne le cherche pas dans le SIREN");

var gesteInconnu = false;
try { Geste("GesteQuiNExistePas"); }
catch (ArgumentException) { gesteInconnu = true; }
Exiger(gesteInconnu, "un geste inconnu est une erreur franche, jamais ignore en silence");
var sessionCw = vue.Session;
EcranCommandesWeb.Fermer(sessionCw);
var fermee = false;
try { EcranCommandesWeb.Agir(sessionCw, "Rechercher", JsonSerializer.SerializeToElement<object?>(null)); }
catch (InvalidOperationException) { fermee = true; }
Exiger(fermee, "une session fermee ne repond plus");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("VERT : l'ecran des commandes web tient ses regles.");
    return 0;
}
Console.WriteLine($"ROUGE : {echecs.Count} regle(s) non tenue(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
