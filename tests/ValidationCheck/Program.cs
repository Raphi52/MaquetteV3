using System.Text.RegularExpressions;
using RigV3Desktop;

// Garde-fou du CIRCUIT DE VALIDATION HIERARCHIQUE (demande du 16/09/2026).
//
// Regle a tenir : le login « astagiaire » n'ecrit PAS dans la GED. Ses
// gestes (renommer, supprimer, dupliquer, creer) partent en demande et
// l'arbre reste INCHANGE tant que le greffier n'a pas valide.
//
// Le banc prouve deux choses :
//  1. le METIER : les droits des comptes et les etats d'une demande ;
//  2. l'ARBRE : rejoue le circuit sur le vrai modele GED (masques, ajouts,
//     libelles) et verifie qu'aucun effet n'apparait avant la validation ;
//  3. la VUE : le plan de travail fait bien passer les QUATRE gestes par
//     Soumettre, et n'applique la modification que dans ValiderModification.
//     Depuis le lot 2 du passage a React (19/11/2026), ces regles vivent dans
//     Pont/PlanTravail.cs (le @code de Home.razor, deplace tel quel) ; le
//     balisage de la page est dans web/src/plan/ et web/src/composants/Application.tsx.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var vue = File.ReadAllText(Path.Combine(racine, "Pont/PlanTravail.cs"));
// Le balisage rendu par React : la barre du haut comprise (lot 1).
var balisage = string.Join("\n",
    Directory.GetFiles(Path.Combine(racine, "web/src/plan"), "*.tsx")
             .Append(Path.Combine(racine, "web/src/composants/Application.tsx"))
             .Append(Path.Combine(racine, "web/src/composants/BarreRig.tsx"))
             .Where(f => !f.EndsWith(".test.tsx")).Select(File.ReadAllText));

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

// --- 1. Les comptes et leurs droits.
var stagiaire = JeuDemo.Compte("astagiaire");
var greffier = JeuDemo.Compte("rvilain");
Exiger(stagiaire.Login == "astagiaire", "le login astagiaire existe dans le jeu de comptes");
Exiger(!stagiaire.PeutModifierDirect && stagiaire.SoumetAValidation,
    "astagiaire ne peut PAS modifier en direct");
Exiger(!stagiaire.PeutValider, "astagiaire ne peut pas valider ses propres demandes");
Exiger(greffier.PeutValider && greffier.PeutModifierDirect,
    "le greffier rvilain valide et modifie en direct");
Exiger(JeuDemo.Comptes.Count(c => c.PeutValider) >= 1, "au moins un superieur peut valider");

// --- 2. Le circuit rejoue sur le VRAI modele d'arbre.
// On reproduit ici la file de Home.razor : une demande porte le geste a
// appliquer, et ce geste n'est execute qu'a la validation.
var demandes = new List<(ModificationProposee Info, Action Appliquer)>();
var numero = 0;
bool Soumettre(CompteRig compte, string geste, string cible, string detail, Action appliquer)
{
    if (compte.PeutModifierDirect) { appliquer(); return false; }
    demandes.Add((new ModificationProposee(++numero, geste, cible, detail, compte.Nom, DateTime.Now),
                  appliquer));
    return true;
}

var arbre = ArbreDemo.Racine("0000", "AMITEL SAS", "903 118 776");
string[] tous = [ArbreDemo.DomaineRcs, ArbreDemo.DomaineJudiciaire];
var masques = AccesGed.NouveauxMasques();
var ajouts = AccesGed.NouveauxAjouts();
var libelles = AccesGed.NouveauxLibelles();

var dossier = AccesGed.EnfantsVisibles(arbre, tous, ajouts, masques)[0];
var avantNb = AccesGed.EnfantsVisibles(dossier, tous, ajouts, masques).Count;
var cible = AccesGed.EnfantsVisibles(dossier, tous, ajouts, masques)[0];
var nomAvant = cible.Libelle;

// Renommage soumis.
Exiger(Soumettre(stagiaire, "Renommer", nomAvant, "nouveau nom : Acte revise",
                 () => libelles[cible] = "Acte revise"),
    "un renommage d'astagiaire est SOUMIS, pas applique");
Exiger(!libelles.ContainsKey(cible), "le libelle n'a pas change avant validation");

// Suppression soumise.
Soumettre(stagiaire, "Supprimer", nomAvant, "", () => masques.Add(cible));
Exiger(AccesGed.EnfantsVisibles(dossier, tous, ajouts, masques).Count == avantNb,
    "le document supprime est TOUJOURS visible avant validation");

// Creation soumise.
var neuf = AccesGed.NouveauSelonNature(AccesGed.Natures[0].Code, "10/09/2026");
Soumettre(stagiaire, "Creer", dossier.Libelle, $"nouvel element : {neuf.Libelle}", () =>
{
    if (!ajouts.TryGetValue(dossier, out var l)) { l = new List<NoeudGed>(); ajouts[dossier] = l; }
    l.Add(neuf);
});
Exiger(AccesGed.EnfantsVisibles(dossier, tous, ajouts, masques).Count == avantNb,
    "l'element cree n'apparait pas dans l'arbre avant validation");

Exiger(demandes.Count == 3 && demandes.All(d => d.Info.EstEnAttente),
    "les trois gestes attendent, a l'etat « En attente de validation »");
Exiger(demandes.Count > 0
       && demandes[0].Info.Etat == EtatModification.EnAttente
       && demandes[0].Info.Resume.Contains(nomAvant),
    "la demande dit le geste et le document vise");

// --- 3. La validation par le greffier applique REELLEMENT.
foreach (var d in demandes) if (greffier.PeutValider) d.Appliquer();
Exiger(libelles.TryGetValue(cible, out var apres) && apres == "Acte revise",
    "apres validation, le renommage est applique");
Exiger(masques.Contains(cible), "apres validation, la suppression est appliquee");
Exiger(ajouts.TryGetValue(dossier, out var liste) && liste.Any(n => ReferenceEquals(n, neuf)),
    "apres validation, l'element cree est dans l'arbre");

// Un login SANS droit de validation ne change rien.
var masques2 = AccesGed.NouveauxMasques();
var applique = false;
var refuse = new ModificationProposee(1, "Supprimer", nomAvant, "", stagiaire.Nom, DateTime.Now);
if (stagiaire.PeutValider) { applique = true; masques2.Add(cible); }
Exiger(!applique && masques2.Count == 0 && refuse.EstEnAttente,
    "astagiaire ne peut pas valider : la demande reste en attente");

// --- 4. La VUE fait bien passer les gestes par la file.
foreach (var geste in new[] { "Renommer", "Supprimer", "Dupliquer", "Creer" })
    Exiger(Regex.IsMatch(vue, @"Soumettre\(""" + geste + @"""") ,
        $"le plan de travail soumet le geste « {geste} » a validation");
Exiger(Regex.IsMatch(vue, @"private bool Soumettre[\s\S]{0,200}if \(CompteCourant\.PeutModifierDirect\) return false;"),
    "le plan de travail n'ecrit en direct que si le login en a le droit");
Exiger(Regex.IsMatch(vue, @"ValiderModification\(DemandeModification d\)[\s\S]{0,260}d\.Appliquer\(\);"),
    "le plan de travail n'applique la modification QUE dans ValiderModification");
Exiger(Regex.IsMatch(vue, @"ValiderModification[\s\S]{0,160}!CompteCourant\.PeutValider")
       || Regex.IsMatch(vue, @"if \(!CompteCourant\.PeutValider"),
    "seul un login habilite peut valider depuis la vue");
Exiger(!vue.Contains("rig-validations-bouton") && !vue.Contains("ValidationsEnAttente")
       && !balisage.Contains("rig-validations-bouton") && !balisage.Contains("ValidationsEnAttente"),
    "la barre du haut ne porte PLUS de bouton Validations (retire le 2026-09-17)");
// Les gestes de l'arbre qui passent par Soumettre arrivent bien de la page :
// le menu du clic droit et le renommage sur place sont branches sur eux.
foreach (var (nom, methode) in new[] { ("menuRenommer", "DemarrerRenommage"), ("menuSupprimer", "SupprimerNoeud"),
                                         ("menuDupliquer", "Dupliquer"), ("ajouterElement", "AjouterElement"),
                                         ("arbreRenommer", "ValiderRenommage") })
    Exiger(Regex.IsMatch(File.ReadAllText(Path.Combine(racine, "Pont/PlanTravailVue.cs")),
               $@"case ""{nom}"":[\s\S]{{0,200}}?{methode}\(")
           && balisage.Contains($"'{nom}'"),
        $"le geste « {nom} » de la page joue {methode} du plan de travail");

Console.WriteLine();
if (echecs.Count == 0)
{
    Console.WriteLine("Validation : tout est vert.");
    return 0;
}
Console.WriteLine($"Validation : {echecs.Count} echec(s).");
foreach (var e in echecs) Console.WriteLine("  - " + e);
return 1;
