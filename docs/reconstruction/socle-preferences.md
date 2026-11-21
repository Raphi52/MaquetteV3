# Socle — préférences de l'agent sur tout serveur RDS, et fiche « ancienne touche, nouvelle touche » (SOC-013, SOC-027)

Lot 4 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json` :

- **SOC-013** « Retrouver ses préférences et ses documents sur n'importe quel serveur RDS » (5 critères) ;
- **SOC-027** « Retrouver ses gestes grâce à la fiche « ancienne touche, nouvelle touche » » (5 critères).

Banc : `.\tests.ps1 SoclePreferences` (`tests\SoclePreferencesCheck`), un contrôle par critère, qui cite son id.

## SOC-013 — ce que fait la V3

**Le problème.** La maquette rangeait les réglages de l'accueil dans `%APPDATA%\RigV3\accueil.json`
(`AccueilEtat.Chemin`), donc sur le serveur RDS où l'app tourne. Sur une ferme RDS sans profil itinérant, l'agent
les perd dès que la ferme l'envoie sur un autre serveur. La V2 les garde en base (`dbo.PARAMETRE_UTILISATEUR`,
40 470 lignes sur RIG_DEV ; l'imprimante, le dernier serveur et la dernière session dans `dbo.UTILISATEUR`).

| Couche | Fichier | Rôle |
|---|---|---|
| Base | `Base\migrations\SOC-013_preferences.sql` | `soc.Preference` (agent, nom, valeur JSON, date, serveur RDS qui l'a écrite) ; `soc.SessionAgent` (la dernière session ouverte : serveur + session) ; `soc.DocumentAgent` (documents préparés pour un agent, remis une seule fois) |
| Contrats | `Contrats\Socle\Preferences.cs` | `RoutesPreferences`, `PreferencesAgent`, `OuvertureSessionAgent`, `DocumentRecu`, `NomsPreferences` |
| Serveur | `Serveur\Socle\Preferences\` | `ModulePreferences` (5 routes, greffe dans l'adresse, `AgentAdmis`) ; `ServicePreferences` ; `DocumentsAgents` ; `ReprisePreferences` |
| App | `AccueilEtat.cs`, `Pont\PreferencesServeur.cs`, `MainWindow.xaml.cs` | la liste des préférences, la reprise à l'ouverture, l'envoi de ce qui change |
| App | `Pont\DocumentsRecus.cs`, `FenetreDocument.xaml.cs` (`AfficheurFenetres`, `Ouvrir`) | chaque document reçu s'ouvre dans une fenêtre de document (CA03, CA04) |
| App | `Pont\PlanTravail.cs`, `Pont\PlanTravailVue.cs` | la boîte d'impression s'ouvre sur l'imprimante de l'agent ; la dernière choisie devient la sienne (CA05) |

- **Routes** (toutes `/socle/{greffe}/…`, l'agent est celui de la session Windows, aucune adresse ne nomme un autre
  agent) : `GET`/`PUT preferences` ; `POST session` (la V3 s'ouvre ici, les documents en attente sont remis) ;
  `GET documents?serveur=…&session=…` (la relève de cette session seulement) ; `GET documents/{id}` (le contenu d'un
  de SES documents : 404 pour celui d'un autre agent, ou si le fichier n'est plus sur le serveur).
- **Ouverture** (`MainWindow`, avant `PageWeb.Brancher`, donc avant que l'accueil lise `accueil.json`) : une fois
  l'agent admis dans son greffe — greffe du lancement ou écran de choix —, `PreferencesServeur.BrancherSessionAsync`
  lit ses préférences, les pose dans `accueil.json` (`AccueilEtat.ReprendrePreferences`), branche le dépôt
  (`AccueilEtat.Distant`), déclare la session, reçoit les documents qui attendaient, puis relève toutes les 30 s.
  À la fermeture, ce qui a changé part (`FermerAsync`).
- **Envoi** : chaque écriture d'`accueil.json` passe la liste des préférences au dépôt, qui n'envoie que ce qui
  diffère de ce que le serveur tient, regroupé (400 ms), un envoi à la fois. Une panne ne relance rien en boucle :
  le réglage est dans le fichier, il repart au geste suivant.
- **Sans `RIGV3_SERVEUR`** : `ClientServeur.Actif` est null, `BrancherSessionAsync` ne fait rien, `Distant` reste
  null. Le fichier seul, comme avant (contrôlé par le banc).

**Ce qui voyage** (`AccueilEtat.Preferences`) : thème, imprimante, favoris, onglets ouverts et onglet actif, vues
sorties et leur place, affaire de chaque onglet, places des fenêtres de document, classement du classeur, tuiles et
disposition, mesures, alertes, catégories d'alertes et leurs réglages. **Ce qui ne voyage pas** : le reste du fichier
(saisies, journal, fil, tâches, arbre GED, historique de consultation). C'est la démonstration des affaires ; ces
données iront en base avec leur domaine, pas dans les préférences.

### La reprise des préférences de la V2 (CA02)

`ReprisePreferences.ReprendreAsync(base V2, base V3, écrans du menu V3)` lit la V2 par `LectureV2` (un SELECT seul)
et écrit `soc.Preference`. Noms relevés sur RIG_DEV le 2026-11-21 : 1 150 noms, 40 833 lignes.

| V2 | V3 | Règle |
|---|---|---|
| `ConsoleAccueil_Theme` | `Theme` | MetroBlue → clair, MetroDark → sombre, MetroPurple → violet, MetroOrange → orange, MetroMaroon → argile. **À valider** ; un autre thème (MetroGreen sur RIG_DEV) est listé. |
| `ConsoleAccueil_Fav_NN` | `VuesFavorites` | « P;FACT;FACT » → la procédure de la DLL du processus (`PROCESSUS_ET_FONCTIONALITE.PROCF_DLL` : FACT → PROC_FACTURE, VGED → PROC_VDOSSIER), sinon le code lui-même ; gardé seulement s'il est au menu de la V3 ; ordre NN conservé. |
| `ConsoleAccueil_StartProcessus` | `OngletActif`, `OngletsVue` | vide → l'accueil. |
| `UTILISATEUR.UTLST_IMPRIMANTE_DEFAUT` | `Imprimante` | telle quelle (« Production » pour 240 agents sur RIG_DEV). |
| tout le reste | — | listé au référent Socle : nom, nombre d'agents, raison par famille (aspect de l'interface `AmiConstantes.*`, places des fenêtres `Proc*Loc*`, colonnes de listes, disposition de l'ancien accueil). |

Un agent V2 sans compte V3 (`soc.Compte.IdV2`) est listé à part, sans bloquer. Reprendre deux fois donne le même
résultat (contrôlé). Le banc joue la reprise sur une copie d'essai relue sur RIG_DEV
(`tests\SoclePreferencesCheck\preferences_v2_rig_dev.sql` : les 575 lignes des agents 5 et 209, leur imprimante,
la table des processus ; ni login, ni nom).

### Les documents préparés pour l'agent (CA03, CA04)

Règle de la V2 (`LigneEdition.AfficheDocument`, `LigneEdition.cs:810-820`) : un document s'affiche sur le serveur et
dans la session notés à l'ouverture (`ThreadAccueil.cs:89-94`). La V3 garde cette règle — une seule session par
agent, la dernière ouverte — et y ajoute ce que demande CA04 : le document attend dans `soc.DocumentAgent` tant que
la session notée ne l'a pas relevé, ou part à la prochaine ouverture. Il est marqué remis (serveur, session, date)
dans la même instruction qui le lit : jamais deux fois. Le dépôt n'est pas une route : c'est la partie serveur
(`DocumentsAgents.DeposerAsync`) ; aucun agent ne pose de document chez un autre.

**À l'écran (T03).** Chaque document remis — à l'ouverture de l'application (CA04) comme à la relève (CA03) — lève
`PreferencesServeur.DocumentsArrives`, un événement de l'application auquel `MainWindow` s'abonne **avant** la session
(`DocumentsRecus.Brancher(new AfficheurFenetres(), …)`), sinon les documents qui attendaient arriveraient sans personne
pour les ouvrir. `Pont\DocumentsRecus.cs` lit le contenu par `GET documents/{id}` — le fichier est sur le serveur de la V3,
chez Amitel, que le serveur RDS ne lit pas —, le range à côté des réglages de l'agent (`accueil-pieces\documents-recus`,
servi à la page par l'hôte des pièces jointes, `Pont\PageWeb.cs`) et l'ouvre dans une **fenêtre de document**
(`FenetreDocument.Ouvrir`, la fenêtre de « Détacher »), titre du document en barre de titre, avec sa nature, qui l'a
préparé et quand. Les documents s'ouvrent un par un, dans l'ordre d'arrivée. Un document qui ne s'affiche pas (fichier
disparu du serveur, serveur muet, type autre que PDF, JPG ou PNG) s'ouvre **quand même**, avec la raison à la place du
contenu : l'agent sait qu'il est arrivé. Le rang d'une fenêtre se prend désormais sur **toutes** les fenêtres de document
ouvertes (`FenetreDocument.RangsOuverts`, aussi pour « Détacher » dans `Pont\PlanTravail.cs`) : sur les seules siennes,
une affaire détachée et un document reçu liraient le même emplacement.

## SOC-027 — la fiche « ancienne touche, nouvelle touche »

Écran **neuf** (« à maquetter »), menu **Configurer › Mon poste › Ancienne touche, nouvelle touche**
(`PROC_TOUCHES`, rangé dans les écrans sans fiche V2 de `MenuGestesCheck`). `Pont\EcranCorrespondances.cs` +
`web\src\ecrans\EcranCorrespondances.tsx` (+ test). Une ligne par raccourci : écran de l'ancien RIG, ancienne
touche, ce qu'elle faisait, statut dans la V3, nouvelle touche, écran de la V3 (un clic l'ouvre pour essayer),
explication. Elle **se cherche** (un mot sur tous les textes de la ligne, et un filtre par statut) et **s'imprime**
(bouton « Imprimer la fiche »). Aucun code de procédure n'est montré.

**D'où vient chaque ligne.**
1. **L'ancienne touche : un inventaire outillé** (T01). `tests\SoclePreferencesCheck\inventorier_touches_v2.py`
   lit `D:\RigApplication\Source` (lecture seule) : dans chaque `.cs` d'un dossier `PROC_*`, chaque comparaison
   d'une touche de fonction, d'Entrée ou d'Échap, coupée à la fin de sa branche, et classe ce qu'elle déclenche.
   Il écrit `Pont\InventaireTouchesV2.cs` (fichier généré) : **185 lignes, 77 écrans de la V2**, chacune avec son
   `fichier:ligne`. Six branches mal coupées par la lecture automatique sont lues à la main (`LUES_A_LA_MAIN`).
   Constat au passage : sur la gestion des audiences et l'EDI RPJC (mandataire), F7 et Entrée ne lancent plus la
   recherche dans la V2 (l'appel est en commentaire) ; elles valident seulement le champ.
2. **L'écran de la V3** : le menu de la maquette, par le code de la procédure ; sauf les fenêtres que la V3
   remplace (`EcransRemplaces`) : l'accueil, la page du dossier (VGED, VDOSSIER), la palette Ctrl K (les fenêtres de
   recherche OCX de la V2), et quatre écrans au menu sous un autre code (experts, juges, divergences BEN,
   habilitations — **à valider**).
3. **La nouvelle touche** : les règles du clavier de la V3 (`EcranCorrespondances.Regle`) — Entrée, **F5 et F7** lancent
   la recherche là où l'écran le fait vraiment (`EntreeLanceLaRecherche`, vérifié dans son `.tsx`), Échap ferme, la
   palette Ctrl K remplace les fenêtres de recherche, « ? » montre les touches, une ligne s'ouvre au clic.

**F5 et F7 (décision du chef du plan, 2026-11-21).** La fenêtre ne recharge plus jamais la V3 : `Pont\PageWeb.cs` coupe
les touches du navigateur (`AreBrowserAcceleratorKeysEnabled = false`). Avant, F5 rechargeait toute la fenêtre — la
saisie en cours était perdue — et F7 basculait la navigation au curseur. Les touches arrivent à la page : dans un critère
d'un écran de recherche, `toucheDeRecherche` (`web\src\plan\clavier.ts`) lance la recherche sur Entrée, F5 et F7, comme
la V2 (`OPE_SEARCH_*`, `frmREQUI.cs:424-425`). Sur les réquisitions, F5 efface les critères et la liste, comme `RAZ` de la
V2 (`frmREQUI.cs:421`, `F5EffaceLesCriteres`). Tout écran de recherche construit ensuite passe par `toucheDeRecherche` :
le banc refuse un écran de `EntreeLanceLaRecherche` qui teste Entrée lui-même. Le contrôle WPF ne donne pas le réglage
touche par touche (`IsBrowserAcceleratorKeyEnabled` vit sur `CoreWebView2Controller`, qu'il garde pour lui) : le réglage
d'ensemble coupe aussi Ctrl R, Ctrl F et F3 (chercher dans la page), Ctrl P (chaque écran a son bouton Imprimer), Ctrl +
et Ctrl − (Ctrl + molette zoome toujours) et Alt + flèche. Copier, coller, tout sélectionner, annuler et les touches de
déplacement restent. En Debug, F12 ouvre toujours les outils de développement.

**La fiche se lit en entier** : chaque texte revient à la ligne, pastilles et boutons compris (`RETOUR_A_LA_LIGNE`,
propre à cette table ; les grilles de la charte restent sur une ligne). Sous 900 px, la table garde sa largeur et le
cadre défile.
4. **Trois lignes globales** : Alt + lettre soulignée (V2, `RigConsoleAccueil.cs:97`) → la lettre de l'action sans
   Alt sur la page du dossier ; Ctrl K et « ? », nouveaux.

**L'aide d'un écran (CA03)** : sur tout écran de procédure visible, « ? » (hors d'un champ) ouvre la liste des
touches de cet écran avec l'ancienne touche à côté (`AideTouchesEcran`, monté par `web\src\plan\Procedure.tsx`) ;
Échap ou « ? » la ferment.

**Répartition au 2026-11-21** (188 lignes, sortie du banc après F5/F7) : identique 24 ; nouvelle touche 31 ; remplacée
par un geste 28 ; supprimée 19 ; nouveau 2 ; écran à venir 84. Le banc imprime le compte à jour à chaque passage.

## Écarts à valider (règle 4 du plan)

| Écart | Ce que fait la V3 | Pourquoi / qui tranche |
|---|---|---|
| **Les touches du navigateur sont coupées** (décision du chef : F5 et F7 ne rechargent plus). | Ctrl F, F3, Ctrl P, Ctrl +/−, Ctrl R, Alt + flèche ne font plus rien ; copier-coller, annuler, déplacement restent ; Ctrl + molette zoome. | Si un greffe se servait de Ctrl F (chercher dans la page) ou de Ctrl + (zoom au clavier), le dire au référent : chaque touche se rend par la page, écran par écran. |
| **Un document « à relire » s'ouvre en lecture.** La V2 l'ouvrait dans Word, visible, pour le relire (`LGEDT_WORD_VISIBLE`, `LigneEdition.cs:806-823`). | La fenêtre de document affiche les PDF et les images ; un autre type (Word) s'ouvre avec la raison à la place du contenu. | Le moteur d'éditions de la V3 (pas encore construit) dira ce qu'il produit ; si c'est du Word à corriger, décider avec le référent comment l'agent le relit. |
| **Recherche sans touche** sur des écrans déjà construits : Actes numérisés, Demandes, GED judiciaire, Coffre-fort, Gestion des clients filtrent leur liste au fil de la saisie (ni bouton, ni Entrée) ; Actualités n'a aucun critère. | La fiche dit « la liste se filtre au fil de la saisie » ou « pas de critère (écart à valider) ». Le banc vérifie chaque affirmation dans le `.tsx` (Entrée → `rechercher()`, champs sans bouton, aucun champ) et repère un écran changé sans que la fiche suive. | Filtrer à chaque frappe est un choix de la maquette ; sur une vraie base, une requête coûteuse se lance plutôt sur validation (la V2 l'a voulu : `proc_demande.md:194`). À décider écran par écran (autres lots). |
| **Choisir une ligne au clavier** (Entrée, F10, F11, F12 dans une liste de la V2). | Un clic sur la ligne. | Écart de vitesse au clavier, à décider pour toutes les listes de la V3. |
| Fonctions d'écran absentes de la V3 construite : F10 de la facture (encaissements repris), F2-F4 des réquisitions (dernier DCA, dernier statut), F7 adresse d'une nouvelle entreprise, F5 du tableau des lots. | « Supprimée : l'écran de la V3 n'a pas cette fonction ». | Référent Socle : reprendre la fonction ou la supprimer. |
| 84 lignes « écran à venir » : l'écran de la V3 n'est pas encore construit, ou la procédure de la V2 n'a pas encore de place au menu (file des éditions, envoi au BODACC, surveillances…). | Statut « Écran à venir ». | Leur touche se fixe avec l'écran ; la fiche se met à jour seule (le banc la recalcule). |
| Correspondance des thèmes V2 → V3. | MetroBlue → clair, etc. | Référent Socle. |
| Les préférences ne vont pas au journal des gestes. | `soc.Preference` garde la date et le serveur RDS de la dernière écriture. | Un réglage personnel n'est pas un geste sur les données du greffe (SOC-016) ; à confirmer. |
| L'imprimante par défaut suit la dernière choisie. | La boîte d'impression la retient. | Dans la V2, elle se réglait dans les paramètres de l'agent ; à confirmer. |

## Ce qui ne se fait pas depuis ce poste

| Sujet | Pourquoi |
|---|---|
| Deux vrais serveurs RDS de recette (SOC-013 T05) | Pas de ferme RDS ici ; le banc simule deux postes (deux `accueil.json`, deux noms de serveur) sur le même serveur de la V3. |
| La reprise des 40 bases de greffe (SOC-013 T04) | Elle se joue au passage, base par base, avec l'outil de la recopie ; aucune écriture sur SQL-PROD depuis ce poste. |
| La fenêtre de document **à l'écran** d'un vrai serveur RDS | Le banc joue la chaîne entière contre le vrai serveur (document déposé, relevé, contenu lu, rangé, montré) et vérifie le branchement WPF dans le code (`MainWindow`, `AfficheurFenetres`, `FenetreDocument.Ouvrir`) ; l'ouverture de la fenêtre elle-même se voit avec un serveur branché (`RIGV3_SERVEUR`), qu'aucun bureau caché de ce poste n'a. |
| Les retours des greffes bêta sur la fiche (SOC-027 CA05, T03, T05) | Ils se recueillent chez Le Puy, Chartres et Vienne. La fiche porte son état (`EtatFiche = "avant-beta"`) et ses retours (`RetoursBeta`) ; `RefusLivraison` refuse « livrable » tant qu'un retour n'est pas intégré (contrôlé, avec son jumeau). |

## Relancer l'inventaire des touches

Quand le banc dit qu'une touche du code de la V2 manque à l'inventaire (ou qu'il en a une que la V2 n'a plus) :

```powershell
python -I tests\SoclePreferencesCheck\inventorier_touches_v2.py Pont\InventaireTouchesV2.cs
.\tests.ps1 SoclePreferences
```

Le script s'arrête sur une touche qu'il ne sait pas classer : ajouter un motif à `ACTIONS`, ou la ligne à
`LUES_A_LA_MAIN` après l'avoir lue dans la V2.

## Preuves

- `.\tests.ps1 SoclePreferences` : vert ; un contrôle par critère de SOC-013 (CA01 à CA05) et de SOC-027 (CA01 à CA05).
- Contre-épreuve du contrôle clé (SOC-013 CA01) : l'envoi au serveur coupé dans `AccueilEtat.Ecrire`, le banc sort
  rouge (7 contrôles en échec, code 1) ; rétabli, il repasse vert. SOC-027 CA01 porte sa contre-épreuve dans le banc
  (une touche retirée de l'inventaire est vue manquante).
- Contre-épreuve des corrections du 2026-11-21 : `DocumentsArrives` jamais levé, `AreBrowserAcceleratorKeysEnabled`
  retiré et la recherche d'affaires revenue à « Entrée seule » → le banc sort rouge (5 échecs, code 1) ; rétablis, vert.
  Le banc porte aussi la sienne (sans abonné, un document reçu ne s'ouvre pas ; une page qui garde les touches du
  navigateur est vue).
- `web\src\ecrans\EcranCorrespondances.test.tsx` (vitest) : affichage, recherche, ouverture d'un écran, aide « ? »,
  texte entier sans coupure. `web\src\plan\clavier.test.ts`, `EcranRequisitions.test.tsx`, `EcranRechAffaires.test.tsx` :
  Entrée, F5 et F7 lancent la recherche, F5 efface les critères des réquisitions (contre-épreuve : F5 et F7 retirés de
  `TOUCHES_RECHERCHE`, 4 tests rouges).

<!-- fix-ok: comptes de la fiche relus dans la sortie du banc du 2026-11-21 (INFO : a-venir 84, geste 29, identique 22, nouveau 2, nouvelle-touche 32, supprimee 19) apres les regles « valider le champ » et « filtre au fil de la saisie ». -->
