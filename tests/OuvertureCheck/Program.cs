using System.Text.RegularExpressions;
using RigV3Desktop;

// Garde-fou de la JONCTION entre l'ecran « Ouvrir un dossier » et la
// recherche d'affaires.
//
// La promesse tenue ici : un dossier qu'on vient d'ouvrir pour une societe
// REJOINT le fonds d'affaires et se retrouve par son SIREN, y compris quand
// le SIREN est retape avec des espaces. Sans cela, l'ouverture ne serait
// visible que dans l'ecran qui l'a creee.

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

var JeuDemoDate = new DateTime(2026, 9, 9);

// On n'ecrit JAMAIS dans l'etat reel de l'utilisateur : le banc detourne
// accueil.json vers un fichier a lui, efface a la fin.
var etatBanc = Path.Combine(Path.GetTempPath(), "rigv3-ouverture-check.json");
Environment.SetEnvironmentVariable("RIGV3_ETAT", etatBanc);
if (File.Exists(etatBanc)) File.Delete(etatBanc);
AffairesOuvertes.Relire();

AffairesOuvertes.Oublier();

// --- 1. Le fonds de depart ne contient pas la societe X
Exiger(AffairesOuvertes.ParSiren("903 118 776") is null,
    "avant ouverture, aucun dossier pour ce SIREN");

// --- 2. Ouverture
var ne = AffairesOuvertes.Ouvrir("903 118 776", "SOCIETE X", new DateTime(2026, 9, 9));
Exiger(ne is not null, "l'ouverture rend bien un dossier");
Exiger(ne!.Numero == "2026B00001", "le numero de gestion est attribue : 2026B00001");
Exiger(ne.Siren == "903118776", "le SIREN est normalise a neuf chiffres");
Exiger(ne.Type == "RCS", "le dossier ouvert est une affaire RCS");
Exiger(ne.Objet.Contains("SOCIETE X"), "l'objet porte la denomination saisie");

// --- 3. La jonction : il est retrouvable dans le fonds partage
Exiger(AffairesOuvertes.Toutes.Count == 1, "le dossier a rejoint le fonds d'affaires");
Exiger(AffairesOuvertes.ParSiren("903118776")?.Numero == ne.Numero,
    "il se retrouve par son SIREN colle");
Exiger(AffairesOuvertes.ParSiren("903 118 776")?.Numero == ne.Numero,
    "il se retrouve par son SIREN espace");

// La recherche de l'accueil filtre sur numero / objet / SIREN sans espaces :
// on rejoue EXACTEMENT ce filtre sur le fonds partage.
bool Correspond(Dossier d, string terme)
    => d.Numero.Contains(terme, StringComparison.OrdinalIgnoreCase)
    || d.Objet.Contains(terme, StringComparison.OrdinalIgnoreCase)
    || d.Siren.Replace(" ", "").Contains(terme.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

Exiger(AffairesOuvertes.Toutes.Any(d => Correspond(d, "903118776")),
    "la recherche de l'accueil le trouve par SIREN");
Exiger(AffairesOuvertes.Toutes.Any(d => Correspond(d, "SOCIETE X")),
    "la recherche de l'accueil le trouve par denomination");
Exiger(AffairesOuvertes.Toutes.Any(d => Correspond(d, ne.Numero)),
    "la recherche de l'accueil le trouve par numero de gestion");

// --- 4. Deux fois le meme SIREN : refus, pas de doublon
Exiger(AffairesOuvertes.Ouvrir("903118776", "SOCIETE X BIS", new DateTime(2026, 9, 9)) is null,
    "un SIREN deja ouvert est refuse");
Exiger(AffairesOuvertes.Toutes.Count == 1, "aucun doublon n'a ete cree");

// --- 5. Saisies invalides
Exiger(AffairesOuvertes.Ouvrir("12345", "TROP COURT", JeuDemoDate) is null,
    "un SIREN de moins de neuf chiffres est refuse");
Exiger(AffairesOuvertes.Ouvrir("440927115", "   ", JeuDemoDate) is null,
    "une denomination vide est refusee");

// --- 6. Le compteur avance
var second = AffairesOuvertes.Ouvrir("440 927 116", "SOCIETE Y", new DateTime(2026, 9, 9));
Exiger(second?.Numero == "2026B00002", "le deuxieme dossier prend le numero suivant");

// --- 7. SURVIE A LA FERMETURE : le dossier doit etre ECRIT sur disque et
// relu au demarrage suivant. On simule la fermeture en vidant la memoire.
Exiger(File.Exists(etatBanc), "l'ouverture a bien ete ecrite sur disque");

AffairesOuvertes.Relire();   // <- equivaut a rouvrir l'application

var apres = AffairesOuvertes.ParSiren("903118776");
Exiger(apres is not null, "apres relecture, le dossier de SOCIETE X est toujours la");
Exiger(apres?.Numero == "2026B00001", "il a garde son numero de gestion");
Exiger(apres?.Objet.Contains("SOCIETE X") == true, "il a garde sa denomination");
Exiger(AffairesOuvertes.Toutes.Count == 2, "les deux dossiers ouverts ont survecu");

var detail = AffairesOuvertes.Details.First(d => d.Siren == "903118776");
Exiger(detail.Immatriculation == new DateTime(2026, 9, 9),
    "la date d'immatriculation a survecu");

// Le compteur repart de l'existant, pas de zero : sinon deux dossiers
// porteraient le meme numero apres un redemarrage.
var troisieme = AffairesOuvertes.Ouvrir("512 345 678", "SOCIETE Z", new DateTime(2026, 9, 9));
Exiger(troisieme?.Numero == "2026B00003",
    "apres redemarrage, le numero suivant ne recycle pas un numero deja pris");

AffairesOuvertes.Oublier();
AffairesOuvertes.Relire();
Exiger(AffairesOuvertes.Toutes.Count == 0, "la remise a zero est elle aussi ecrite sur disque");

// --- 8. Le RATTRAPAGE : depuis une recherche restee vide, on peut ouvrir le
// dossier sans retaper le SIREN. Le cablage se lit dans les sources : les
// trois maillons doivent etre presents ensemble, sinon le bouton ne mene
// nulle part.
var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string Lire(string relatif) => File.ReadAllText(Path.Combine(racine, relatif));

// PORTES EN REACT (lot 3, 19/11/2026) : les deux .razor ne sont plus que des
// coquilles. Le maillon « recherche » se lit donc sur la chaine reelle :
// coquille (parametre + rappel) -> ecran React (bouton) -> pont C# (SIREN
// cherche, recherche restee vide), et le pont est EXECUTE plus bas.
var rech = Lire("Components/Ecrans/EcranRechAffaires.razor");
var rechTsx = Lire("web/src/ecrans/EcranRechAffaires.tsx");
// Lot 2 du passage a React (19/11/2026) : l'aiguillage des ecrans est
// web/src/plan/Aiguillage.tsx, les gestes du plan de travail Pont/PlanTravailVue.cs.
var accueil = Lire("web/src/plan/Aiguillage.tsx");
var gestesPlan = Lire("Pont/PlanTravailVue.cs");
var ouverture = Lire("Components/Ecrans/EcranOuvertureDossier.razor");
var ouvertureTsx = Lire("web/src/ecrans/EcranOuvertureDossier.tsx");

Exiger(rech.Contains("EventCallback<string> OuvrirUnDossier")
       && rech.Contains("[\"ouvrirUnDossier\"] = IleReact.Rappel(OuvrirUnDossier)"),
    "« Mes affaires » sait remonter une demande d'ouverture");
Exiger(rechTsx.Contains("onClick={() => ouvrirUnDossier(vue.sirenCherche)}"),
    "le bouton « Ouvrir un dossier » emporte le SIREN cherche");
Exiger(Regex.IsMatch(rechTsx, @"vue\.aucunCritere \?[\s\S]{0,200}?Aucune affaire ne repond a ces criteres[\s\S]{0,300}?Ouvrir un dossier"),
    "le bouton n'apparait qu'apres une recherche restee vide");
Exiger(accueil.Contains("ouvrirUnDossier={(siren) => plan('ouvrirUnDossier', siren)}")
       && gestesPlan.Contains("case \"ouvrirUnDossier\": OuvrirUnDossierPour(Texte(valeur)); break;"),
    "l'accueil ecoute cette demande");
Exiger(accueil.Contains("<EcranOuvertureDossier key={ecran.cleOuverture} sirenDemande={ecran.sirenDemande} />")
       && gestesPlan.Contains("SirenAImmatriculer, CleOuverture.ToString()"),
    "l'accueil transmet le SIREN a l'ecran d'ouverture");
Exiger(ouverture.Contains("[Parameter] public string? SirenDemande")
       && ouverture.Contains("[\"sirenDemande\"] = SirenDemande"),
    "l'ecran d'ouverture accepte un SIREN pre-rempli");
Exiger(ouvertureTsx.Contains("useState(() => (sirenDemande ?? '').trim())"),
    "l'ecran React d'ouverture pose ce SIREN dans la fiche");

// La jonction JOUEE par les ponts des deux ecrans : rechercher un SIREN
// inconnu, ouvrir le dossier depuis l'ecran d'ouverture, le retrouver.
static RigV3Desktop.Pont.CriteresAffaires Criteres(string libelle = "")
    => new("", libelle, "", "", "", "", "", "", "", null, null);

var vide = RigV3Desktop.Pont.EcranRechAffaires.Trouver(Criteres(), null, true, null);
Exiger(vide.AucunCritere && vide.SirenCherche == "",
    "sans critere, la grille ne propose pas d'ouvrir un dossier");
var inconnu = RigV3Desktop.Pont.EcranRechAffaires.Trouver(Criteres("512 345 678"), null, true, null);
Exiger(inconnu.Lignes.Length == 0 && !inconnu.AucunCritere && inconnu.SirenCherche == "512345678",
    "un SIREN inconnu laisse la recherche vide et le SIREN cherche est retenu, sans espaces");
Exiger(RigV3Desktop.Pont.EcranRechAffaires.Trouver(Criteres("SOCIETE 512"), null, true, null).SirenCherche == "",
    "un libelle qui n'est pas un SIREN n'est pas propose a l'immatriculation");
var ouvertPont = RigV3Desktop.Pont.EcranOuvertureDossier.Ouvrir(new RigV3Desktop.Pont.FicheOuverture(
    "Guichet unique (INPI)", inconnu.SirenCherche, new DateOnly(2026, 9, 9), "SOCIETE Z", "SAS", 1000m, "EUR"));
Exiger(!ouvertPont.Bloquant && ouvertPont.Ouverts.Any(o => o.Siren == "512345678"),
    "l'ecran d'ouverture ouvre le dossier pour ce SIREN");
var retrouve = RigV3Desktop.Pont.EcranRechAffaires.Trouver(Criteres("512 345 678"), null, true, null);
Exiger(retrouve.Lignes.Length == 1 && retrouve.Lignes[0].Nom == "SOCIETE Z",
    "la recherche d'affaires retrouve aussitot le dossier ouvert");
Exiger(RigV3Desktop.Pont.EcranOuvertureDossier.Ouvrir(new RigV3Desktop.Pont.FicheOuverture(
           "Guichet unique (INPI)", "512345678", null, "SOCIETE Z BIS", "SAS", 1000m, "EUR")) is { Bloquant: true } refus
       && refus.Message.StartsWith("Ce SIREN a deja un dossier ouvert"),
    "l'ecran d'ouverture refuse un second dossier pour le meme SIREN, et le dit");

if (File.Exists(etatBanc)) File.Delete(etatBanc);

Console.WriteLine();
if (echecs.Count == 0) { Console.WriteLine("Jonction ouverture -> recherche : VERT."); return 0; }
Console.WriteLine(echecs.Count + " echec(s) : " + string.Join(" | ", echecs));
return 1;
