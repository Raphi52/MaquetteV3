using System.Text.RegularExpressions;

// Garde-fou de l'ecran des REQUISITIONS (fiche 275).
//
// Defaut a l'origine de ce controle (11/09/2026) : l'ecran a ete porte depuis
// OPE_REQUI_RECHERCHE / RESULTATS / PANIER de la branche 275, mais AUCUN
// controle du depot ne le regardait. Une compilation verte ne dit rien des
// regles metier portees : elles pouvaient disparaitre sans que rien ne rougisse.
// Chaque regle ci-dessous vient d'une ligne lue dans la source v2.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var ecran = Path.Combine(racine, "Components", "Ecrans", "EcranRequisitions.razor");
var home = Path.Combine(racine, "Components", "Pages", "Home.razor");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

Exiger(File.Exists(ecran), "l'ecran EcranRequisitions.razor existe");
if (!File.Exists(ecran)) { Console.WriteLine("1 echec"); return 1; }

var texte = File.ReadAllText(ecran);
var maison = File.ReadAllText(home);

// --- 1. L'ecran est ATTEIGNABLE : sans ce branchement il n'existe pour personne.
Exiger(Regex.IsMatch(maison, @"case\s+""PROC_REQUI""\s*:\s*\r?\n\s*<EcranRequisitions\s*/>"),
    "Home.razor route PROC_REQUI vers <EcranRequisitions />");
Exiger(maison.Contains("\"PROC_REQUI\")") || Regex.IsMatch(maison, @"new\(""Requisitions"".*""PROC_REQUI""\)"),
    "le menu propose bien l'entree Requisitions (PROC_REQUI)");

// --- 2. LimiteDossiers de OPE_REQUI_RECHERCHE : la limite est ramenee dans 50..500.
Exiger(Regex.IsMatch(texte, @"Math\.Clamp\(\s*CritLimiteDossiers\s*,\s*50\s*,\s*500\s*\)"),
    "la limite de dossiers est ramenee dans 50..500 (LimiteDossiers)");

// --- 3. AuMoinsUnCritere : une recherche a vide est refusee, et les cases a
//        cocher ne comptent PAS comme critere (elles filtrent, elles ne cherchent pas).
Exiger(Regex.IsMatch(texte, @"bool\s+AuMoinsUnCritere\s*=>"),
    "la regle AuMoinsUnCritere existe");
var corpsCritere = Regex.Match(texte, @"bool\s+AuMoinsUnCritere\s*=>(.*?);", RegexOptions.Singleline).Groups[1].Value;
foreach (var champ in new[] { "CritRaisonSociale", "CritSiren", "CritDirigeant", "CritActivite",
                              "CritNumVoie", "CritAdresse", "CritCodePostal", "CritVille" })
    Exiger(corpsCritere.Contains(champ), $"AuMoinsUnCritere compte le champ {champ}");
foreach (var caseACocher in new[] { "CritPhonetique", "CritEtendre", "CritTousEtb", "CritGuichet" })
    Exiger(!corpsCritere.Contains(caseACocher),
        $"AuMoinsUnCritere ne compte PAS la case {caseACocher}");
Exiger(texte.Contains("Veuillez saisir un critere de recherche."),
    "une recherche sans critere affiche le refus");

// --- 4. _ValiderRaisonSociale : au-dela de 6 caracteres, une designation faite
//        QUE de chiffres est rejetee.
Exiger(Regex.IsMatch(texte, @"CritRaisonSociale\.Length\s*>\s*6\s*&&\s*int\.TryParse"),
    "une raison sociale de plus de 6 chiffres est refusee");

// --- 5. OPE_REQUI_PANIER : regroupement d'une meme societe a la MEME adresse
//        sous une seule commande, et renumerotation apres suppression.
Exiger(Regex.IsMatch(texte, @"c\.Societe\.Siren\s*==\s*societe\.Siren", RegexOptions.Singleline)
    && Regex.IsMatch(texte, @"c\.Societe\.Adresse\s*==\s*societe\.Adresse", RegexOptions.Singleline),
    "les lignes d'une meme societe a la meme adresse vont dans la meme commande");
var corpsSuppr = Regex.Match(texte, @"void\s+Supprimer\(.*?\n\s*\}", RegexOptions.Singleline).Value;
Exiger(corpsSuppr.Contains("Panier.Remove(cmd)")
    && Regex.IsMatch(corpsSuppr, @"Panier\[i\]\.NumCommande\s*=\s*i\s*\+\s*1"),
    "supprimer une commande renumerote celles qui suivent");

// --- 6. btnValider_Click : « Valider » vide les criteres SANS vider le panier.
var corpsValider = Regex.Match(texte, @"void\s+Valider\(\).*?\n\s*\}", RegexOptions.Singleline).Value;
Exiger(corpsValider.Length > 0 && !corpsValider.Contains("Panier.Clear()"),
    "« Valider » ne vide jamais le panier");
Exiger(corpsValider.Contains("Effacer()"), "« Valider » vide bien les criteres");

// --- 7. Fiche 275 : la ZONE HAUTE, les commandes web arrivees par EDI.
//        C'est le raccordement que la fiche demande et que personne n'avait fait :
//        sans elle, l'ecran est un REQUI porte, pas un traitement des commandes web.
var metier = File.ReadAllText(Path.Combine(racine, "Metier", "JeuDemo.cs"));
Exiger(Regex.IsMatch(metier, @"class\s+CommandeWeb\b"),
    "le modele CommandeWeb existe dans Metier/JeuDemo.cs");
foreach (var champ in new[] { "Numero", "Recue", "Demandeur", "Famille", "CodeEdi", "Etat" })
    Exiger(Regex.IsMatch(metier, @"class\s+CommandeWeb\b.*?\b" + champ + @"\b", RegexOptions.Singleline),
        $"CommandeWeb porte le champ {champ}");
foreach (var famille in new[] { "Endettement", "Faillite", "Non opposition" })
    Exiger(metier.Contains("\"" + famille + "\""),
        $"le jeu de commandes web couvre la famille {famille}");
foreach (var code in new[] { "ET-CREQ", "ET-PREQ" })
    Exiger(metier.Contains(code), $"le jeu de commandes web porte le code EDI {code}");

Exiger(texte.Contains("Commandes web a traiter"),
    "l'ecran affiche la liste des commandes web a traiter");
Exiger(Regex.IsMatch(texte, @"ondblclick=""\(\)\s*=>\s*RapprocherCommandeWeb\("),
    "un double-clic sur une commande web lance le rapprochement");

var corpsRappr = Regex.Match(texte, @"void\s+RapprocherCommandeWeb\(.*?
\s*\}\s*
", RegexOptions.Singleline).Value;
Exiger(corpsRappr.Contains("CritSiren = web.Siren") && corpsRappr.Contains("CritRaisonSociale = web.Societe"),
    "le rapprochement remplit les criteres avec le siren, sinon la raison sociale");
Exiger(corpsRappr.Contains("Rechercher()"),
    "le rapprochement lance la recherche de dossiers");

var corpsTraitee = Regex.Match(texte, @"void\s+MarquerTraitee\(\).*?
\s*\}\s*
", RegexOptions.Singleline).Value;
Exiger(corpsTraitee.Contains("Etat = \"Traitee\""),
    "« Marquer traitee » fait passer la commande web a l'etat Traitee");
Exiger(corpsTraitee.Contains("TODO #275"),
    "le sens exact de la validation reste marque TODO #275 (non defini par la fiche)");

Exiger(Regex.IsMatch(texte, @"NbATraiter\s*=>\s*CommandesWeb\.Count\(c\s*=>\s*c\.Etat\s*!=\s*""Traitee""\)"),
    "le compteur ne compte que les commandes encore a traiter");

// --- 8. Fiche 275 : les points de la commande qui manquaient a l ecran.
//        Certificat de NON OPPOSITION (la 3e famille EDI de la fiche : l ecran
//        commandait Non Faillite / Non RCS / Non RSAC mais pas celui-la).
Exiger(Regex.IsMatch(metier, @"class\s+CommandeRequi\b.*?\bNbCertifNonOppo\b", RegexOptions.Singleline),
    "CommandeRequi porte la quantite de certificats Non Opposition");
Exiger(Regex.IsMatch(metier, @"class\s+CommandeRequi\b.*?\bDateCnOppo\b", RegexOptions.Singleline),
    "CommandeRequi porte la date du certificat Non Opposition");
Exiger(texte.Contains("Certif. Non Opposition")
    && texte.Contains("@bind=\"CommandeEnCours.NbCertifNonOppo\""),
    "l ecran fait saisir la quantite de certificats Non Opposition");
Exiger(Regex.IsMatch(texte, @"type=""date""\s+@bind=""CommandeEnCours\.DateCnOppo"""),
    "l ecran fait saisir la date du certificat Non Opposition");

//        Quantites PAR type d inscription : une quantite globale ne disait pas
//        combien de chaque type. NbInscr devient la SOMME, la regle v2 tient.
Exiger(Regex.IsMatch(metier, @"Dictionary<string,\s*int>\s+QteParInscription"),
    "CommandeRequi porte une quantite par type d inscription");
Exiger(texte.Contains("ChangerQteInscription") && texte.Contains("QteInscription("),
    "l ecran fait saisir une quantite par type d inscription");
var corpsGestion = Regex.Match(texte, @"void\s+GestionQteInscr\(\).*?\n    \}", RegexOptions.Singleline).Value;
Exiger(corpsGestion.Contains("QteInscription(c.Libelle)") && corpsGestion.Contains(".Sum("),
    "la quantite globale d inscriptions est la somme des quantites par type");
Exiger(corpsGestion.Contains("somme > 0 ? somme : 1"),
    "cocher une case stricte laisse la quantite a 1 au minimum (GestionQteInscr)");
Exiger(corpsGestion.Contains("CommandeEnCours.NbInscr = 0;"),
    "plus aucune case stricte cochee remet la quantite a 0 (GestionQteInscr)");
var corpsBascule = Regex.Match(texte, @"void\s+BasculerInscription\(.*?\n    \}", RegexOptions.Singleline).Value;
Exiger(corpsBascule.Contains("QteParInscription[cas.Libelle] = 1")
    && corpsBascule.Contains("QteParInscription.Remove(cas.Libelle)"),
    "cocher un type met sa quantite a 1, le decocher la retire");

//        Date de travail et fraicheur : le greffier doit voir si les pieces
//        etablies sont encore valables.
Exiger(Regex.IsMatch(metier, @"class\s+CommandeRequi\b.*?\bDateTravail\b", RegexOptions.Singleline),
    "CommandeRequi porte la date de travail");
Exiger(Regex.IsMatch(metier, @"JoursFraicheur\s*=\s*\d+"),
    "le seuil de fraicheur est une constante nommee");
Exiger(Regex.IsMatch(metier, @"bool\s+TravailFrais\s*=>") && Regex.IsMatch(metier, @"int\s+AgeTravailJours\s*=>"),
    "la fraicheur du travail se calcule a partir de son age en jours");
Exiger(Regex.IsMatch(texte, @"type=""date""\s+@bind=""CommandeEnCours\.DateTravail""")
    && texte.Contains("CommandeEnCours.TravailFrais"),
    "l ecran fait saisir la date de travail et affiche sa fraicheur");
Exiger(texte.Contains("Travail perime"),
    "un travail au-dela du seuil est signale comme perime");

//        Nom du requi : le requerant pour le compte de qui la commande est passee.
Exiger(Regex.IsMatch(metier, @"class\s+CommandeRequi\b.*?\bNomRequi\b", RegexOptions.Singleline),
    "CommandeRequi porte le nom du requi");
Exiger(texte.Contains("Nom du requi") && texte.Contains("@bind=\"CommandeEnCours.NomRequi\""),
    "l ecran fait saisir le nom du requi");

//        « Nom de la demande » (fiche 275) : « Site local ou Infogreffe - N de
//        commande et date ». C est lui qui prerempli le nom du requi.
Exiger(Regex.IsMatch(metier, @"class\s+CommandeWeb.*?Origine", RegexOptions.Singleline),
    "CommandeWeb porte l origine de la commande (Site local / Infogreffe)");
Exiger(metier.Contains("\"Site local\"") && metier.Contains("\"Infogreffe\""),
    "les deux origines de la fiche 275 existent dans le jeu d exemple");
Exiger(Regex.IsMatch(metier, @"NomDemande\s*=>\s*Origine\s*\+\s*"" - ""\s*\+\s*Numero\s*\+\s*"" - ""\s*\+\s*Recue\.ToString\(""dd/MM/yyyy""\)"),
    "le nom de la demande suit le format origine - numero - date de la fiche 275");
Exiger(texte.Contains("NomRequi = CommandeWebEnCours?.NomDemande"),
    "rapprocher une commande web prerempli le nom du requi");
// L'en-tete est passe au composant partage EnteteTriable le 16/09/2026 (les
// colonnes se trient au clic) : la colonne Origine existe toujours, elle se
// declare autrement. On exige la COLONNE, pas sa forme d'ecriture.
Exiger((texte.Contains("<th>Origine</th>") || texte.Contains("Libelle=\"Origine\""))
       && texte.Contains("@web.Origine"),
    "la liste des commandes web affiche leur origine");

//        Le panier doit MONTRER ces nouveaux points, sinon ils sont invisibles.
var corpsResume = Regex.Match(texte, @"string\s+Resume\(CommandeRequi\s+c\).*?\n    \}", RegexOptions.Singleline).Value;
Exiger(corpsResume.Contains("NbCertifNonOppo"), "le panier resume le certificat Non Opposition");
Exiger(corpsResume.Contains("QteParInscription"), "le panier resume la quantite par type d inscription");
Exiger(corpsResume.Contains("NomRequi"), "le panier resume le nom du requi");
Exiger(corpsResume.Contains("DateTravail") && corpsResume.Contains("perime"),
    "le panier resume la date de travail et signale un travail perime");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "Tout est vert." : echecs.Count + " echec(s).");
return echecs.Count == 0 ? 0 : 1;
