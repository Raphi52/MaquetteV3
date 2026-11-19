using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using RigV3Desktop;
using RigV3Desktop.Pont;

// Garde-fou de l'ecran des REQUISITIONS (fiche 275).
//
// Defaut a l'origine de ce controle (11/09/2026) : l'ecran a ete porte depuis
// OPE_REQUI_RECHERCHE / RESULTATS / PANIER de la branche 275, mais AUCUN
// controle du depot ne le regardait. Une compilation verte ne dit rien des
// regles metier portees : elles pouvaient disparaitre sans que rien ne rougisse.
// Chaque regle ci-dessous vient d'une ligne lue dans la source v2.
//
// PORTAGE EN REACT (lot 2, 19/11/2026). L'ecran tenait en UN fichier,
// Components/Ecrans/EcranRequisitions.razor. Il est maintenant en trois :
//   - la coquille .razor, qui monte l'ile React ;
//   - web/src/ecrans/EcranRequisitions.tsx, le BALISAGE (libelles, champs, gestes) ;
//   - Pont/EcranRequisitions.cs, l'ETAT et les REGLES (l'ancien @code, deplace
//     tel quel dans SessionRequisitions).
// Chaque regle d'avant est donc relue la ou elle vit desormais : une regle de
// code dans le pont, un libelle ou un champ dans le .tsx, un champ lie dans les
// DEUX (le .tsx l'envoie, le pont le range). Aucune n'est retiree. La section 9
// est neuve : elle JOUE les gestes de l'ecran par le pont, comme React les envoie.

var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var coquille = Path.Combine(racine, "Components", "Ecrans", "EcranRequisitions.razor");
var cheminTsx = Path.Combine(racine, "web", "src", "ecrans", "EcranRequisitions.tsx");
var cheminPont = Path.Combine(racine, "Pont", "EcranRequisitions.cs");
var home = Path.Combine(racine, "Components", "Pages", "Home.razor");

var echecs = new List<string>();
void Exiger(bool ok, string quoi)
{
    Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
    if (!ok) echecs.Add(quoi);
}

Exiger(File.Exists(coquille), "l'ecran EcranRequisitions.razor existe");
Exiger(File.Exists(cheminTsx), "l'ecran React EcranRequisitions.tsx existe");
Exiger(File.Exists(cheminPont), "le pont Pont/EcranRequisitions.cs existe");
if (!File.Exists(coquille) || !File.Exists(cheminTsx) || !File.Exists(cheminPont))
{
    Console.WriteLine(echecs.Count + " echec(s).");
    return 1;
}

var tsx = File.ReadAllText(cheminTsx);
// « texte » : les regles de code. Elles etaient dans le @code du .razor, elles
// sont dans la classe de session du pont.
var texte = File.ReadAllText(cheminPont);
var maison = File.ReadAllText(home);

// --- 1. L'ecran est ATTEIGNABLE : sans ce branchement il n'existe pour personne.
Exiger(Regex.IsMatch(maison, @"case\s+""PROC_REQUI""\s*:\s*\r?\n\s*<EcranRequisitions\s*/>"),
    "Home.razor route PROC_REQUI vers <EcranRequisitions />");
Exiger(maison.Contains("\"PROC_REQUI\")") || Regex.IsMatch(maison, @"new\(""Requisitions"".*""PROC_REQUI""\)"),
    "le menu propose bien l'entree Requisitions (PROC_REQUI)");
Exiger(Regex.IsMatch(File.ReadAllText(coquille), @"<IleReact\s+Composant=""EcranRequisitions""\s*/>"),
    "la coquille monte l'ecran React (<IleReact Composant=\"EcranRequisitions\" />)");
Exiger(tsx.Contains("'EcranRequisitions.Ouvrir'") && tsx.Contains("'EcranRequisitions.Agir'")
       && tsx.Contains("'EcranRequisitions.Fermer'")
       && texte.Contains("[JSInvokable(\"EcranRequisitions.Ouvrir\")]")
       && texte.Contains("[JSInvokable(\"EcranRequisitions.Agir\")]")
       && texte.Contains("[JSInvokable(\"EcranRequisitions.Fermer\")]"),
    "l'ecran React appelle les trois methodes de SON pont, et le pont les expose");

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

Exiger(tsx.Contains("Commandes web a traiter"),
    "l'ecran affiche la liste des commandes web a traiter");
// Avant : @ondblclick="() => RapprocherCommandeWeb(web)". Maintenant le .tsx
// remonte le geste au double-clic, et le pont l'aiguille vers la meme methode.
Exiger(Regex.IsMatch(tsx, @"onDoubleClick=\{\(\)\s*=>\s*geste\('RapprocherCommandeWeb'")
       && Regex.IsMatch(texte, @"case\s+""RapprocherCommandeWeb""\s*:\s*RapprocherCommandeWeb\("),
    "un double-clic sur une commande web lance le rapprochement");

var corpsRappr = Regex.Match(texte, @"void\s+RapprocherCommandeWeb\(.*?\r?\n\s*\}\s*\r?\n", RegexOptions.Singleline).Value;
Exiger(corpsRappr.Contains("CritSiren = web.Siren") && corpsRappr.Contains("CritRaisonSociale = web.Societe"),
    "le rapprochement remplit les criteres avec le siren, sinon la raison sociale");
Exiger(corpsRappr.Contains("Rechercher()"),
    "le rapprochement lance la recherche de dossiers");

var corpsTraitee = Regex.Match(texte, @"void\s+MarquerTraitee\(\).*?\r?\n\s*\}\s*\r?\n", RegexOptions.Singleline).Value;
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

// Un champ lie (@bind="CommandeEnCours.X" dans le .razor) : le .tsx l'affiche et
// remonte la saisie sous le nom X, et le pont range cette saisie dans
// CommandeEnCours.X (PoserArticle, ou c == CommandeEnCours).
bool ChampLie(string champ, string helper) =>
    Regex.IsMatch(tsx, helper + @"\('" + champ + @"',\s*a\." + char.ToLowerInvariant(champ[0]) + champ[1..] + @"\)")
    && Regex.IsMatch(texte, @"case\s+nameof\(c\." + champ + @"\)\s*:[^\n]*\bc\." + champ + @"\s*=");
var corpsPoser = Regex.Match(texte, @"void\s+PoserArticle\(.*?\n    \}", RegexOptions.Singleline).Value;
Exiger(corpsPoser.Contains("CommandeEnCours is not { } c"),
    "les champs de la commande en cours sont ranges dans CommandeEnCours (PoserArticle)");

Exiger(tsx.Contains("Certif. Non Opposition") && ChampLie("NbCertifNonOppo", "nombre"),
    "l ecran fait saisir la quantite de certificats Non Opposition");
Exiger(Regex.IsMatch(tsx, @"const\s+date\s*=.*?<Saisie[^>]*type=""date""", RegexOptions.Singleline)
       && ChampLie("DateCnOppo", "date"),
    "l ecran fait saisir la date du certificat Non Opposition");

//        Quantites PAR type d inscription : une quantite globale ne disait pas
//        combien de chaque type. NbInscr devient la SOMME, la regle v2 tient.
Exiger(Regex.IsMatch(metier, @"Dictionary<string,\s*int>\s+QteParInscription"),
    "CommandeRequi porte une quantite par type d inscription");
Exiger(tsx.Contains("geste('ChangerQteInscription'")
       && Regex.IsMatch(texte, @"case\s+""ChangerQteInscription""\s*:\s*\r?\n?\s*ChangerQteInscription\(")
       && texte.Contains("QteInscription("),
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
// Avant : texte.Contains("CommandeEnCours.TravailFrais"). La fraicheur affichee
// est celle de Metier (CommandeRequi.TravailFrais), lue par le pont sur
// CommandeEnCours et montree telle quelle par le .tsx.
Exiger(ChampLie("DateTravail", "date")
       && Regex.IsMatch(texte, @"CommandeEnCours\s+is\s+\{\s*\}\s+a\b") && texte.Contains("a.TravailFrais")
       && tsx.Contains("a.travailFrais"),
    "l ecran fait saisir la date de travail et affiche sa fraicheur");
Exiger(tsx.Contains("Travail perime"),
    "un travail au-dela du seuil est signale comme perime");

//        Nom du requi : le requerant pour le compte de qui la commande est passee.
Exiger(Regex.IsMatch(metier, @"class\s+CommandeRequi\b.*?\bNomRequi\b", RegexOptions.Singleline),
    "CommandeRequi porte le nom du requi");
Exiger(tsx.Contains("Nom du requi") && tsx.Contains("article('NomRequi')")
       && Regex.IsMatch(texte, @"case\s+nameof\(c\.NomRequi\)\s*:\s*c\.NomRequi\s*=\s*texte"),
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
// declare autrement. On exige la COLONNE, pas sa forme d'ecriture. En React,
// les colonnes triables sont une liste [libelle, cle] du .tsx.
Exiger(Regex.IsMatch(tsx, @"\[\s*'Origine'\s*,\s*'Origine'\s*\]") && tsx.Contains("{web.origine}")
       && texte.Contains("web.Origine"),
    "la liste des commandes web affiche leur origine");

//        Le panier doit MONTRER ces nouveaux points, sinon ils sont invisibles.
var corpsResume = Regex.Match(texte, @"string\s+Resume\(CommandeRequi\s+c\).*?\n    \}", RegexOptions.Singleline).Value;
Exiger(corpsResume.Contains("NbCertifNonOppo"), "le panier resume le certificat Non Opposition");
Exiger(corpsResume.Contains("QteParInscription"), "le panier resume la quantite par type d inscription");
Exiger(corpsResume.Contains("NomRequi"), "le panier resume le nom du requi");
Exiger(corpsResume.Contains("DateTravail") && corpsResume.Contains("perime"),
    "le panier resume la date de travail et signale un travail perime");
Exiger(tsx.Contains("{cmd.resume}") && Regex.IsMatch(texte, @"Resume\(c\)\)\)\.ToArray\(\)"),
    "la colonne « Articles commandes » du panier affiche ce resume");

// --- 8 bis. Le pont DELEGUE a Metier, le .tsx n'a AUCUNE regle.
//        Ce qui vient de Metier/JeuDemo.cs (fraicheur, KBIS et certificat permis,
//        nom de la demande) est LU, jamais recalcule ; le .tsx n'a que des
//        valeurs a afficher et des gestes a remonter.
Exiger(texte.Contains("a.KBisPossible") && texte.Contains("a.CnfPossible") && texte.Contains("a.AgeTravailJours")
       && texte.Contains("CommandeRequi.JoursFraicheur") && texte.Contains("JeuDemo.CommandesWeb()")
       && texte.Contains("JeuDemo.EntreprisesRequi") && texte.Contains("JeuDemo.CasesInscription"),
    "le pont lit les regles et le jeu de Metier (KBisPossible, CnfPossible, AgeTravailJours, JoursFraicheur, JeuDemo)");
Exiger(!texte.Contains("TotalDays") && !Regex.IsMatch(texte, @"JoursFraicheur\s*=") && !Regex.IsMatch(texte, @"Origine\s*\+\s*"" - """),
    "le pont ne recalcule ni l'age du travail, ni le seuil, ni le nom de la demande");
Exiger(!Regex.IsMatch(tsx, @"\bMath\.|new Date\(|\.sort\(|\bparseInt\(|\bNumber\("),
    "le .tsx ne calcule rien : ni limite, ni date, ni tri, ni conversion de nombre (le pont le fait)");

// --- 9. Les GESTES de l'ecran, joues par le pont comme React les envoie
//        (EcranRequisitions.Agir(session, geste, valeur), valeurs en JSON).
var jeu = JeuDemo.CommandesWeb();
var vue = EcranRequisitions.Ouvrir();
VueRequisitions Geste(string geste, object? valeur = null) =>
    vue = EcranRequisitions.Agir(vue.Session, geste, JsonSerializer.SerializeToElement(valeur));
int[] Rangs() => vue.CommandesWeb.Select(l => l.Rang).ToArray();

Exiger(vue.CommandesWeb.Length == jeu.Length && vue.NbATraiter == jeu.Count(c => c.Etat != "Traitee")
       && vue.CommandesWeb.Select(l => l.Numero).SequenceEqual(jeu.Select(c => c.Numero)),
    $"a l'ouverture, la zone haute montre les {jeu.Length} commandes web du jeu de Metier, dans l'ordre d'arrivee");

Geste("Rechercher");
Exiger(vue.Message == "Veuillez saisir un critere de recherche." && !vue.ARecherche && vue.Trouvees.Length == 0,
    "joue : Rechercher sans critere est refuse");
Geste("Critere", new { champ = "CritRaisonSociale", valeur = "1234567" });
Exiger(vue.RaisonSocialeInvalide && vue.Criteres.RaisonSociale == "1234567",
    "joue : une raison sociale de 7 chiffres est signalee des la frappe");
Geste("Rechercher");
Exiger(vue.Message.StartsWith("Designation invalide") && vue.Trouvees.Length == 0,
    "joue : ... et la recherche la refuse");

Geste("Effacer");
Geste("LimiteDossiers", "20");
Geste("Critere", new { champ = "CritVille", valeur = "AMIENS" });
Geste("Rechercher");
Exiger(vue.LimiteRetenue == 50 && vue.ARecherche && vue.Trouvees.Length > 0,
    $"joue : une limite de 20 est ramenee a 50 (lu : {vue.LimiteRetenue}, {vue.Trouvees.Length} ligne(s))");
Geste("LimiteDossiers", "9000");
Geste("LimiteDossiers", "abc");
Geste("Rechercher");
Exiger(vue.Criteres.LimiteDossiers == 9000 && vue.LimiteRetenue == 500,
    "joue : une limite illisible garde la precedente, et 9000 est ramene a 500");
Exiger(vue.Trouvees.All(t => t.TypeAdresse != "Etablissement"),
    "joue : « Tous les etablissements » decoche, seuls les sieges remontent");
Geste("BasculerCritere", "CritTousEtb");
Geste("Rechercher");
Exiger(vue.Criteres.TousEtb && vue.Trouvees.Any(t => t.TypeAdresse == "Etablissement"),
    "joue : la bascule « Tous les etablissements » ramene les etablissements");

// Le tri de la zone haute : premier clic croissant, second clic inverse.
Geste("BasculerTri", "Demandeur");
var attenduDemandeur = jeu.Select((c, i) => (c, i)).OrderBy(x => x.c.Demandeur).Select(x => x.i).ToArray();
Exiger(vue.CleTri == "Demandeur" && vue.TriAscendant && Rangs().SequenceEqual(attenduDemandeur),
    $"joue : un clic sur « Demandeur » range la zone haute (lu : {string.Join(",", Rangs())})");
Geste("BasculerTri", "Demandeur");
var attenduDemandeurInverse = jeu.Select((c, i) => (c, i)).OrderByDescending(x => x.c.Demandeur).Select(x => x.i).ToArray();
Exiger(!vue.TriAscendant && Rangs().SequenceEqual(attenduDemandeurInverse),
    "joue : un second clic sur « Demandeur » l'inverse");
Geste("BasculerTri", "Recue");
Exiger(vue.TriAscendant && Rangs().SequenceEqual(jeu.Select((c, i) => (c, i)).OrderBy(x => x.c.Recue).Select(x => x.i)),
    "joue : changer de colonne repart en ordre croissant");

// Le rapprochement d'une commande web, puis la saisie de sa commande.
var web0 = jeu[0];
Geste("RapprocherCommandeWeb", 0);
Exiger(vue.Criteres.Siren == web0.Siren && vue.Criteres.RaisonSociale == "" && vue.ARecherche
       && vue.Trouvees.Length == 1 && vue.Trouvees[0].Designation == web0.Societe,
    $"joue : le double-clic sur {web0.Numero} cherche par son siren et trouve {web0.Societe}");
Geste("AjouterAuPanier", 0);
var art = vue.Articles;
Exiger(vue.Volet == "articles" && vue.NbPanier == 1 && art is not null && art.NumCommande == 1
       && art.NomRequi == web0.NomDemande,
    $"joue : ajouter au panier ouvre la commande 1, nom du requi prerempli (lu : {art?.NomRequi})");

Geste("Article", new { champ = "NbCertifNonOppo", valeur = "3" });
Exiger(vue.Articles?.NbCertifNonOppo == 3, "joue : saisir 3 certificats Non Opposition");
Geste("Article", new { champ = "NbCertifNonOppo", valeur = "" });
Exiger(vue.Articles?.NbCertifNonOppo == 3, "joue : une quantite effacee garde la valeur d'avant (@bind d'un int)");
Geste("Article", new { champ = "DateCnOppo", valeur = "2026-09-01" });
Exiger(vue.Articles?.DateCnOppo == "2026-09-01", "joue : saisir la date du certificat Non Opposition");

var stricte = JeuDemo.CasesInscription.First(c => c.Stricte).Libelle;
var nonStricte = JeuDemo.CasesInscription.First(c => !c.Stricte).Libelle;
Geste("BasculerInscription", stricte);
Exiger(vue.Articles is { NbInscr: 1 } a1 && a1.Cases.Single(c => c.Libelle == stricte) is { Cochee: true, Qte: 1 },
    $"joue : cocher {stricte} (stricte) met sa quantite et le total a 1");
Geste("ChangerQteInscription", new { libelle = stricte, saisie = "4" });
Exiger(vue.Articles?.NbInscr == 4, "joue : sa quantite passee a 4, le total suit");
Geste("BasculerInscription", nonStricte);
Exiger(vue.Articles?.NbInscr == 5, $"joue : cocher {nonStricte} ajoute sa quantite au total (somme par type)");
Geste("BasculerInscription", stricte);
Exiger(vue.Articles is { NbInscr: 0 } a0 && a0.Cases.Single(c => c.Libelle == stricte) is { Cochee: false, Qte: 0 },
    "joue : plus aucune case stricte, le total retombe a 0 et la quantite decochee disparait");

Geste("Article", new { champ = "NomRequi", valeur = "MAITRE DURAND" });
var perime = JeuDemo.Aujourdhui.AddDays(-(CommandeRequi.JoursFraicheur + 10)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
Geste("Article", new { champ = "DateTravail", valeur = perime });
Exiger(vue.Articles is { TravailFrais: false } a9 && a9.AgeTravailJours == CommandeRequi.JoursFraicheur + 10,
    $"joue : une date de travail a {CommandeRequi.JoursFraicheur + 10} jours est perimee");
Geste("Volet", "panier");
Exiger(vue.Panier.Length == 1 && vue.Panier[0].Resume.Contains("3 Certif. Non Opposition")
       && vue.Panier[0].Resume.Contains("requi MAITRE DURAND") && vue.Panier[0].Resume.Contains("(perime)"),
    $"joue : le panier resume la saisie (lu : {vue.Panier.FirstOrDefault()?.Resume})");

Geste("Valider");
Exiger(vue.NbPanier == 1 && vue.Criteres.Siren == "" && !vue.ARecherche,
    "joue : « Valider » vide les criteres et garde le panier");
var web3 = jeu[3];
Geste("RapprocherCommandeWeb", 3);
Geste("AjouterAuPanier", 0);
Geste("Volet", "panier");
Exiger(vue.NbPanier == 2 && vue.Panier[1].NumCommande == 2 && vue.Panier[1].Designation == web3.Societe,
    "joue : une autre societe ouvre la commande 2");
Geste("Supprimer", 0);
Exiger(vue.NbPanier == 1 && vue.Panier[0].NumCommande == 1 && vue.Panier[0].Designation == web3.Societe,
    "joue : supprimer la commande 1 renumerote la suivante en 1");

Geste("ChoisirCommandeWeb", 1);
Geste("MarquerTraitee");
Exiger(vue.NbATraiter == jeu.Length - 1 && vue.CommandesWeb.Single(l => l.Rang == 1).Etat == "Traitee"
       && vue.Message.Contains("TODO #275"),
    "joue : « Marquer traitee » passe la commande web a Traitee et le compteur baisse");
Geste("MasquerTraitees");
Exiger(vue.MasquerTraitees && vue.CommandesWeb.Length == jeu.Length - 1 && vue.CommandesWeb.All(l => l.Rang != 1),
    "joue : « Masquer les traitees » la retire de la liste");

var gesteInconnu = false;
try { Geste("GesteQuiNExistePas"); }
catch (ArgumentException) { gesteInconnu = true; }
Exiger(gesteInconnu, "un geste inconnu est une erreur franche, jamais ignore en silence");

var session = vue.Session;
EcranRequisitions.Fermer(session);
var fermee = false;
try { EcranRequisitions.Agir(session, "Rechercher", JsonSerializer.SerializeToElement<object?>(null)); }
catch (InvalidOperationException) { fermee = true; }
Exiger(fermee, "une session fermee ne repond plus");

Console.WriteLine();
Console.WriteLine(echecs.Count == 0 ? "Tout est vert." : echecs.Count + " echec(s).");
return echecs.Count == 0 ? 0 : 1;
