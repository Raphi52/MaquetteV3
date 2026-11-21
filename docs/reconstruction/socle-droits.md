# Socle — comptes des agents, droits et journaux (SOC-002, SOC-004, SOC-005, SOC-016)

Lot 3 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-20/21. Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json` :

- **SOC-002** « Retrouver exactement ses droits le jour du passage » (7 critères) ;
- **SOC-004** « Créer, modifier et désactiver le compte d'un agent » (8 critères) ;
- **SOC-005** « Garder la trace de chaque changement de droits » (5 critères) ;
- **SOC-016** « Savoir qui a modifié quoi, quand et depuis quel écran » (6 critères).

Construites sur l'architecture du lot 1 (`docs\reconstruction\architecture.md`) et les tables du lot 2
(`Base\migrations\SOC-001_comptes.sql`, `docs\reconstruction\socle-identite.md`).

## Ce que fait la V3

- **Un seul modèle de droits.** La V2 a trois origines de droits, consultées dans un ordre précis :
  les rôles du compte ; pour une fonction de type `FONCVB6` seulement, l'ancien système (degré > 2) ;
  pour toute autre fonction, les menus de comptabilité, y compris sous six anciens noms. La V3 les
  réunit : rôles (`soc.Role`, `soc.CompteRole`), fonctions de chaque rôle (`soc.RoleFonction`) et
  droits **directs** d'un compte (`soc.CompteFonction`), qui gardent leur origine V2 (`ancien`,
  `comptabilite`). La vue `soc.Droit` est l'union. La traduction (degré, type, anciens noms) est faite
  **une fois, à la reprise** ; vérifier un droit n'est plus qu'une lecture.
- **La reprise** (`RepriseDroits.ReprendreAsync(base V2, base V3)`) lit la base V2 en lecture seule et
  écrit, en une transaction : services (avec leur distribution), comptes (désactivés compris, sans mot
  de passe), rôles, fonctions, liens, droits directs, **un** événement 210, et la ligne qui ouvre le
  journal de la V3.
- **L'écran « Comptes des agents »** (menu Configurer › Mon poste) : rechercher, créer, corriger,
  rattacher à un service, désactiver, supprimer (si le compte n'a jamais agi), donner ou retirer un
  rôle ; deux onglets de lecture, « Journal des gestes » et « Journal des droits ».
- **Deux journaux en ajout seul**, écrits dans la transaction de chaque geste : le journal des droits
  (qui a donné ou retiré quel rôle à qui) et le journal des gestes (agent, heure, écran, objet, avant
  et après). Plus un journal des événements techniques (codes de la V2 : 130, 210).

## Sources V2 vérifiées avant de coder (lecture du 2026-11-20)

Chaque source `code_v2` des quatre stories a été rouverte à la ligne citée.

| Source citée par le carnet | Constat |
|---|---|
| `Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:298-347` | Conforme : rôles, puis ancien système (FONCVB6 seulement), puis menus de comptabilité et anciens noms (`:327-341`). |
| `Utilisateur.cs:300-316` | Conforme : une fonction inconnue est créée en silence au catalogue. Aussi `:317-320` : une fonction `OLD` ou `VB6` est **réécrite** en `FONCVB6` pendant la vérification (deuxième écriture cachée). |
| `Outils\DLL\RigOldHabilitation\Habilitation.cs:8-16` | Conforme : `> 2`, toute erreur avalée = refus sans trace. |
| `Utilisateur.cs:364-366` | Conforme : `IsHabilit` = rôle `GestionDesHabilitations`. |
| `PROC_UTILISATEUR\FORM_UTILISATEUR.cs:20-24`, `PROC_ROLE\FORM_ROLE.cs:20-24` | Conforme : le contrôle « écran interdit sans `GestionDesHabilitations` » est en commentaire. |
| `HABILIT_ROLE_USER.sql:1-7`, `COMPOSER_HABILITATION.sql:1-7`, `EXPRESSO_DROITS.sql:1-6` | Conformes. |
| `Documentation\06-criteres-equivalence.md:38` | Conforme : « Matrice rôles × actions = identique à l'original ». |
| `RIG_DEV.dbo.*` (comptes du carnet) | Recomptés : 43 rôles, 2 900 rôles attribués, 1 097 liens rôle-fonction, 876 fonctions ; ancien système 24 rôles, 524 attributions, 211 degrés ; 93 menus, 6 863 droits de comptabilité ; 5 services, 243 comptes (96 actifs). |
| `PROC_UTILISATEUR\Edition\OPE_EDITION_UTILISATEUR.cs:156-159` | Conforme, mais le texte exact de la V2 est **« Ce login existe déjà : X »** ; le critère SOC-004 CA02 le paraphrase en « ce compte existe déjà ». La V3 reprend le texte de la V2. |
| `OPE_EDITION_UTILISATEUR.cs:163-172` | Conforme : `:165` code greffe 9999 si le créateur est d'Amitel, sinon celui du greffe ; `:166-167` abrégé = 16 premiers caractères au-delà de 15 ; `:169-170` une demande « Création/Modification de l'utilisateur ». |
| `OPE_SUPPRESSION_UTILISATEUR.cs:38-58` | Conforme : suppression avec transfert des demandes à un autre compte. |
| `UTILISATEUR.sql`, `SERVICE.sql:1-11`, `RIG_DEV.dbo.MODELE_PROCESSUS` | Conformes (GUTILI « gestion des données et paramètres utilisateurs », XSERVICE « Mise à jour des services du greffe »). |
| `HABILIT_UTILISATEUR_ROLE_USER.sql:1-6` | Conforme : ni auteur ni date (la colonne `HUROL_LOGIN_UTLST` est le compte **visé**). |
| `RIG_DEV.dbo.AUDIT`, `AUDIT.sql:1-11` | Conformes : 822 599 lignes sur FACTURE, ENCAISSEMENT, VENTILATION, ARTICLE_UTILISE, depuis le 2025-02-03. |
| `FACTURE.sql:81-94` | Conforme : l'auteur noté est `PROGRAM_NAME + ' - ' + LOGIN_NAME`. |
| `RIG_DEV.dbo.CODE_JET` | Conforme : code 130 « Habilitation d'utilisateur/Gestion des droits », 210 « Reprise de données… ». À noter : chaque ligne porte aussi une **valeur** (21 pour le 130, 32 pour le 210) ; la V3 écrit le code (130, 210). Lequel des deux part dans le journal signé est à trancher par la certification (SOC-018). |

Sources V2 trouvées en route, non citées par le carnet mais nécessaires :
`VB6\Bin Client\RIG_Client_HabilitationV2\GestionDroitRIG.cls:248-482` (le calcul du degré de l'ancien
système, avec les degrés du domaine et de l'activité qui priment) ; `ProcessusEtFonctionalite.cs:186-192`
(type lu par `Enum.TryParse`, sensible à la casse), `:326-327` (fonction cherchée sans majuscules),
`:42-43` (domaine Comptabilité = bit 16) ; `Utilisateur.cs:75-77` (administrateur = Amitel **et** marque
administrateur) ; `PROC_PROCESSUS\Recherche\OPE_RECHERCHE_PROCESSUS.cs:319-320` (qui voit les fonctions
réservées) ; `PROC_UTILISATEUR\Recherche\OPE_RECHERCHE_UTILISATEUR.cs:276` et
`OPE_EDITION_UTILISATEUR.cs:301` (un responsable hors Amitel ne voit pas les comptes administrateurs et
ne touche pas un compte Amitel) ; `RigMetier\Admin\ExpressoMenu.cs:26-39`.

## Où est le code

| Couche | Fichier | Rôle |
|---|---|---|
| Base | `Base\migrations\SOC-002_droits.sql` | `soc.Fonction` (code sans majuscules, domaine Comptabilité), `soc.RoleFonction`, `soc.CompteFonction`, vue `soc.Droit` ; rôle `GestionDesHabilitations` |
| Base | `Base\migrations\SOC-004_services.sql` | distribution des services (direction, distribution spécifique et automatique, heure), date de création du compte |
| Base | `Base\migrations\SOC-005_journal.sql` | `soc.JournalDroits`, `soc.EvenementTechnique`, déclencheurs « ajout seul » |
| Base | `Base\migrations\SOC-016_gestes.sql` | `soc.FamilleJournal` (durées), `soc.Journal`, déclencheurs, `soc.PurgerJournal`, fonction `JOURNAL_GESTES` |
| Contrat | `Contrats\Socle\Comptes.cs`, `Droits.cs`, `JournalGestes.cs` | `RoutesComptes`, `RoutesDroits`, `RoutesJournal` et leurs records |
| Serveur | `Serveur\Socle\Comptes\ModuleComptes.cs` | les routes, la politique `PolitiquesComptes.GestionDesDroits` |
| Serveur | `Serveur\Socle\Comptes\ServiceDroits.cs` | l'appelant (`Appelant`, règles Amitel/administrateur), la vérification d'un droit |
| Serveur | `Serveur\Socle\Comptes\ServiceComptes.cs` | créer, modifier, désactiver, supprimer, rôles ; journaux dans la transaction |
| Serveur | `Serveur\Socle\Comptes\JournalGestes.cs` | écrire et lire les journaux (gestes, droits, événements), y compris pour la recopie |
| Serveur | `Serveur\Socle\Comptes\RepriseDroits.cs` | la reprise au passage, et la lecture d'une base V2 (`DonneesV2`) |
| Écran | `Pont\EcranComptes.cs`, `web\src\ecrans\EcranComptes.tsx` (+ `.test.tsx`) | l'écran ; sans `RIGV3_SERVEUR`, les comptes de `JeuDemo` en lecture seule |
| Menu | `Pont\MenuAccueil.cs`, `Pont\CatalogueEcrans.cs`, `web\src\plan\Aiguillage.tsx` | une entrée chacun (`PROC_UTILISATEUR`, « Comptes des agents ») |
| Banc | `tests\SocleDroitsCheck\` (+ `OracleDroitsV2.cs`, `BaseV2Jetable.cs`) | un contrôle par critère ; comparaison avec RIG_DEV |
| Banc voisin | `tests\MenuGestesCheck\Program.cs` | `PROC_UTILISATEUR` ajouté aux procédures sans fiche (elle a un dossier de sources V2) |

Routes (toutes derrière `PolitiquesSocle.AgentAdmis` ; `{greffe}` = code du greffe au catalogue, adresse
par `RoutesComptes.Pour`, `RoutesDroits.Pour`, `RoutesJournal.Pour`) :

| Route | Qui | Rôle |
|---|---|---|
| `GET /socle/{greffe}/comptes` | gère les droits | comptes, services, rôles **visibles** |
| `POST /socle/{greffe}/comptes/creer`, `/modifier`, `/desactiver`, `/supprimer`, `/role` | gère les droits | les gestes ; un refus métier = 200 avec `Fait = false` et le message |
| `GET /socle/{greffe}/droits/journal?compte=&role=&du=&au=` | gère les droits | journal des droits filtré, du plus récent au plus ancien |
| `GET /socle/{greffe}/droits/verifier?fonction=` | tout agent admis | ses propres droits |
| `GET /socle/{greffe}/journal?agent=&objet=&du=&au=` | tout agent admis | tout le greffe avec `JOURNAL_GESTES`, ses seuls gestes sinon |

Chacune lit et écrit dans la base V3 de **ce** greffe (voir « Raccord fait », plus bas) : 404 si le greffe
n'est pas au catalogue, 503 si sa base ne répond pas, sans repli.

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **Traduire les trois origines à la reprise**, pas à chaque vérification | La V2 recalcule à chaque clic le degré de l'ancien système (plusieurs requêtes) et écrit dans le catalogue pendant une lecture. Traduite une fois, la vérification est une lecture d'une vue ; la V3 ne garde pas l'ancien système (24 rôles, 211 degrés) comme modèle vivant. |
| **La traduction suit le partage de la V2** : ancien système seulement pour une fonction `FONCVB6` (ou `OLD`, `VB6`), menus seulement pour les autres | Sinon la V3 accorderait un droit que la V2 refuse : un menu de comptabilité posé sur une fonction VB6 n'ouvre rien dans la V2. Contre-épreuve : `OLD` oublié dans la traduction → 42 écarts sur RIG_DEV, banc rouge. |
| **Un comparateur indépendant** (`tests\SocleDroitsCheck\OracleDroitsV2.cs`) : portage instruction par instruction de `ALaFonctionnalite` et du VB6, sur ses propres lectures de la base V2 | La V3 est jugée contre l'algorithme de la V2, pas contre la reprise elle-même. Contre-épreuve : la règle « le degré du domaine prime » retirée de la reprise → 326 écarts sur RIG_DEV, banc rouge. Limite : c'est l'algorithme, pas l'exécution de la DLL V2 (elle demande tout l'environnement RIG). |
| **Fonction inconnue : refus, rien écrit** ; **panne : refus et trace** (`EventId` 51006, compte, fonction, heure UTC) dans le journal du serveur | CA05 et CA06. La trace va dans le journal du serveur et non en base : une panne de la base ne doit pas empêcher de tracer la panne. |
| **Droit de gérer les droits = rôle `GestionDesHabilitations`**, tenu côté serveur (politique) | Celui de la V2 (`IsHabilit`), créé par la migration pour qu'une base neuve l'ait avant toute reprise. |
| **Rôles réservés** : hors Amitel, ni Amitel ni administrateur ; Amitel non administrateur, pas les rôles administrateur ; administrateur (= Amitel **et** marque admin), tout | Règle de la V2 pour les fonctions réservées (`OPE_RECHERCHE_PROCESSUS.cs:319-320`), appliquée aux rôles ; « administrateur » au sens de `IsUserAdministrateur`. |
| **Un compte « a agi »** s'il est l'auteur d'une ligne d'un journal de la V3, **ou s'il vient de la V2** | Ses demandes et factures de la V2 ne sont pas dans la V3 : le supprimer effacerait leur auteur côté V2. Prudent ; à revoir quand la recopie dira ce que le compte a fait dans la V2. |
| **Les journaux citent l'auteur par clé étrangère**, le compte visé par son nom | La base elle-même refuse de supprimer un auteur (SOC-004 T01) ; un compte jamais actif peut être supprimé sans perdre la trace des rôles qu'il a eus. |
| **Ajout seul par déclencheurs** (`THROW 51010`, `51011`) | Refusé à tout compte SQL, même `sa`. Seule `soc.PurgerJournal` efface, et seulement une ligne échue : même la purge ne peut pas retirer une ligne récente. |
| **Durées de conservation par famille** (`soc.FamilleJournal`), contrainte « au moins 6 ans » pour la facturation | CA04 de SOC-016. Les autres durées valent 6 ans **en attendant** le référent Socle et le délégué à la protection des données. |
| **Le nom de l'écran part dans le corps de chaque demande** (`Ecran`), posé par `Pont\EcranComptes.cs` | Sans toucher `Pont\ClientServeur.cs` (fichier commun du lot 1). Un en-tête commun à tous les écrans serait plus général : à décider quand les domaines journaliseront. |
| **Code du greffe d'un compte créé par le greffe** : celui de l'adresse (`{greffe}`) | Le `RIG.Common.CodeGreffeRig` de la V2, le greffe ouvert. Les services de RIG_DEV portent `9999`, pas le code du greffe : on ne peut pas le déduire du service. (Jusqu'au lot 9 : la configuration `Greffe:Code`, retirée.) |
| **`JOURNAL_GESTES`** : une fonction de la V3, donnée d'office au rôle `GestionDesHabilitations` | Le « chef de greffe » n'existe pas comme tel dans la V2 (la maquette le décide par `PeutValider`). Une fonction du modèle de droits laisse le greffe l'attribuer à qui il veut (au rôle `GREFFIER`, par exemple). À valider. |
| **Écran dans `Configurer › Mon poste`** | Le seul sous-menu de réglages du greffe qui avait de la place (6 entrées sur 8 permises par `MenuGestesCheck`) ; un sous-menu « Les agents » aurait demandé trois écrans. |

## Écarts code ↔ métier « à valider » (règle 6 du plan)

1. **SOC-004 CA04 — suppression.** La V2 supprime un compte qui a agi en réattribuant ses demandes à un
   autre (`OPE_SUPPRESSION_UTILISATEUR.cs:38-58`). Critère écrit par la story, donc codé selon lui
   (décision du lot 5) : la V3 refuse et propose la désactivation ; seul un compte qui n'a jamais agi
   se supprime.
2. **SOC-005 — journal des droits.** La V2 ne garde ni auteur ni date d'un rôle attribué. La V3
   journalise chaque ajout et retrait (CA01) et écrit l'événement 130 quand le rôle ouvre une fonction
   du domaine Comptabilité de la V2 (CA02).
3. **SOC-002 CA04 — accès à la gestion des droits.** La V2 a mis le contrôle en commentaire : tout agent
   qui ouvre GESTUTIL ou GESTROLE gère les comptes. La V3 exige le rôle `GestionDesHabilitations`.
   Des agents qui géraient les comptes sans ce rôle ne le pourront plus le jour du passage.
4. **SOC-002 CA05 — fonction inconnue.** La V2 l'ajoute au catalogue en `FONCVB6` ; la V3 refuse sans
   rien écrire. Une fonction que la V2 crée ainsi après le passage n'existera pas dans la V3.
5. **Ce que la V2 fait sans le dire** (codé comme la V2, signalé) :
   - le doublon de compte n'est cherché que si le compte change (`OPE_EDITION_UTILISATEUR.cs:156`) ;
   - une catégorie de l'ancien système partagée par plusieurs lignes (RIG_DEV : `AMITEL` ×3,
     `EDIT_PRV` ×2, `PREVENTION` ×2) : la V2 prend la première ligne rendue par une requête **sans
     ORDER BY** ; la V3 prend la plus petite clé, celle de l'ordre physique habituel. Si SQL Server
     rendait une autre ligne, la V2 elle-même changerait de réponse ;
   - un menu de comptabilité se compare à la majuscule près au nom passé par l'appelant (C#, `==`) ; la
     V3 cherche la fonction sans majuscules, comme le rôle. Seul un appel de la V2 avec un nom mal écrit
     diffère ;
   - un compte sans rôle refusé à l'ouverture : écart déjà signalé par SOC-001 (lot 2).

## Ce qui ne peut pas se faire ici, et pourquoi

| Tâche | Raison | Qui / quand |
|---|---|---|
| **SOC-002 CA07, SOC-004 CA06 et la moitié V2 de SOC-004 CA03** : la V2 applique un droit, un compte, une désactivation faits dans la V3 | C'est la recopie V3 → V2 (REC-002, REC-017), en phase 2 ; aucune écriture dans une base V2 n'est permise ici. Ce que la recopie lira est prêt : chaque geste est journalisé dans sa transaction, avec le compte Windows, le nom du rôle et l'identifiant V2 du service. Le banc les marque **REPORTÉ**, pas OK. | Phase 2, thème REC |
| **SOC-002 CA01 « sur les 40 bases »** et la reprise base par base (T04) | Seule RIG_DEV est lisible depuis ce poste. Le banc se rejoue tel quel sur une autre base V2 : `$env:RIGV3_V2 = "<chaîne de la copie anonymisée>"; .\tests.ps1 SocleDroits`. | Copies anonymisées des 40 greffes (REC-008) |
| **SOC-016 CA01-CA02 pour les familles des domaines** (dossier, demande, facture, encaissement…) | Ces domaines n'existent pas encore dans la V3. Le journal est prêt : `JournalGestes.EcrireAsync(cn, tx, appelant, écran, famille, objet, id, verbe, avant, après)` dans la transaction du geste ; les quatre familles de la V2 sont déclarées. | Lots des domaines |
| **SOC-016 T03, la tuile d'activité de l'accueil** (`web\src\accueil\Tuiles.tsx`) | Hors des fichiers de ce lot ; l'écran « Comptes des agents » porte déjà le journal (onglet « Journal des gestes »). La tuile lira `GET /socle/{greffe}/journal`. | Lot de l'accueil |
| **Le greffe de l'écran choisi à l'ouverture** | En attendant, `Pont\EcranComptes.cs` lit le code du greffe dans la variable `RIGV3_GREFFE` (sans elle, avec un serveur, l'écran le dit au lieu d'appeler une adresse sans greffe). | Lot 10 (choix du greffe comme la V2, `RigFormLogin.cs:282-319`) |
| **SOC-016 T05, planifier la purge** et fixer les durées autres que la facturation | Geste d'exploitation (agent SQL), durées à fixer par le référent Socle et le DPO. `soc.PurgerJournal` est prête. | Exploitation |
| **Signature du journal des événements** (C19, NF525) | Certification de la V3 (SOC-018). `soc.EvenementTechnique` en est le contenu, en ajout seul. | SOC-018 |
| **Interdire `TRUNCATE` et la désactivation des déclencheurs** | Les déclencheurs arrêtent tout compte SQL sur `UPDATE`/`DELETE`, mais un propriétaire de la base peut les désactiver. Le compte SQL du serveur ne doit avoir que lecture et ajout sur les journaux. | Déploiement (droits SQL) |

## Raccord fait (lot 9, 2026-11-21) : comptes, droits et journaux dans la base DU greffe

| Qui | Ce qui a changé |
|---|---|
| Les adresses (`Contrats\Socle\Comptes.cs`, `Droits.cs`, `JournalGestes.cs`) | Les 9 routes portent `{greffe}` : `/socle/{greffe}/comptes…`, `/socle/{greffe}/droits/…`, `/socle/{greffe}/journal`, adresse par `Pour(route, greffe)`. |
| `ServiceDroits` | `OuvrirBaseAsync(http, b, ct)` : la base du greffe de l'adresse par `CatalogueGreffes.OuvrirBaseV3Async`, la base commune pour une route sans greffe (le catalogue). `AppelantAsync` et `VerifierAsync(http, b, moi, fonction, ct)` y lisent ; une base muette pendant la vérification reste un refus tracé (CA06). |
| Les routes (`ModuleComptes.cs`) | Chacune ouvre la base du greffe une fois et la passe à `ServiceComptes` et `JournalGestes` (qui ne reçoivent plus `BaseRig`). La politique `AgentAdmis` y juge l'agent (lot 8) : 404 greffe inconnu, 503 base muette, sans repli ; la politique de gestion des droits laisse alors la réponse de `AgentAdmis`. |
| Le code greffe d'un compte créé | Celui de l'adresse ; la clé `Greffe:Code` n'existe plus. |
| L'écran (`Pont\EcranComptes.cs`) | Appelle les adresses du greffe de `RIGV3_GREFFE`, en attendant le choix du greffe à l'ouverture (lot 10). |

Prouvé par le banc (contrôles « Raccord (1) ») : deux bases de greffe jetables et un greffe sans base au
catalogue d'une base commune ; 404 et 503 sur chacune des 9 routes sans qu'aucune base ne change ; un compte
du greffe A inconnu du greffe B (ouverture, droits, gestion des comptes) ; un geste sur A journalisé dans A
seulement, le même sur B dans B seulement, chacun avec le code greffe de son adresse. Contre-épreuve
(2026-11-21) : `OuvrirBaseAsync` ramené à la base commune → 9 contrôles rouges, fichier restauré à l'identique.

## Preuves

- `.\tests.ps1 SocleDroits` → **code 0** : 60 contrôles OK (dont 8 du raccord et la suppression des 4 bases
  jetables) et 4 parties **REPORTÉES**, nommées (recopie de la phase 2, 40 bases). Chaque critère des quatre
  stories est cité.
- **Comparaison avec la V2 sur RIG_DEV** (lue en lecture seule, reprise dans une base jetable) :
  96 comptes actifs × 876 fonctions = **84 096 comparaisons, 0 écart** (59 714 autorisations) ; par la
  route de l'agent, 3 comptes × 876 fonctions = 2 628 appels, 0 écart. Reprise en 0,5 s : 243 comptes,
  5 services, 2 900 rôles attribués, 4 408 droits de l'ancien système, 6 720 de la comptabilité.
- **Contre-épreuves** (2026-11-21), fichier restauré à l'identique après chacune : règle du domaine
  retirée de la reprise → 5 échecs dont 326 écarts sur RIG_DEV ; type `OLD` oublié → 5 échecs dont
  42 écarts ; un droit retiré de la V3 → le comparateur voit exactement 1 écart (contrôle permanent du
  banc).
- `web\src\ecrans\EcranComptes.test.tsx` (vitest, 9 tests) : refus sans le droit, création avec rôles,
  suppression refusée puis désactivation, rôle donné, journal en phrases avec l'écran et l'avant/après,
  le compte rendu d'un geste qui s'efface au changement d'onglet ou d'agent, le détail du geste choisi
  lu en entier sous la grille, et une liste de rôles vide lue « — » (chacun rouge avant sa correction).
- Grilles de l'écran en colonnes réparties (relecture du 2026-11-21) : la grille commune (une ligne par
  cellule, largeur automatique) poussait « Rôles », « État », « Écran » et « Avant → après » hors du
  cadre dans une fenêtre étroite. Chaque colonne garde maintenant sa part ; un texte trop long finit
  par « … » et se lit entier au survol. Le service s'y lit par son abrégé (RCS, JUD), nom complet au
  survol et dans la fiche. Le compte rendu d'un geste (et son bouton « Désactiver ce compte ») s'efface
  dès qu'on change d'onglet ou d'agent.
- Détail du geste (2e relecture du 2026-11-21) : la grille garde une ligne par geste (règle du
  2026-09-17), donc à 540 px la phrase et l'avant/après y finissent par « … ». Sous la grille, le geste
  choisi — le dernier d'office, un clic sur une ligne pour un autre, repéré par un filet — se lit en
  entier, lignes repliées : la phrase, l'heure, l'écran et l'avant → après. Un compte sans rôle avant
  le geste se lit « roles : — → GREFFIER » (la liste vide s'affichait vide).
- Captures sur bureau caché (2026-11-21, refaites après la relecture), app de cette copie de travail
  branchée à un serveur de développement local, sous le compte d'un responsable (`RIGV3_COMPTE`), base
  préparée par la vraie reprise d'une petite base V2 : `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\` —
  `SOC-004-01-ecran-comptes.png` (5 colonnes dans le cadre ; le compte administrateur reste caché à un
  responsable hors Amitel), `SOC-004-02-creation-saisie.png`, `SOC-004-03-compte-cree.png` (« Compte
  créé. », lbernard, abrégé, code greffe 3801), `SOC-004-04-compte-modifie.png` (« M. Louis
  Bernard-Petit »), `SOC-004-05-compte-desactive.png`, `SOC-016-06-journal-des-gestes.png` (le compte
  rendu a disparu ; créé, rôles, modifié, désactivé, colonne « Écran » entière),
  `SOC-005-07-journal-des-droits.png`, `SOC-016-08-detail-du-geste.png` (ligne « a modifié les rôles »
  choisie, sa phrase entière et « roles : — → GREFFIER » sous la grille). L'écran virtuel de la session
  d'essai fait 540 px de large (200 %) : c'est la fenêtre la plus étroite qu'on puisse avoir ; les
  phrases longues y finissent par « … » dans la grille et se lisent entières dans le détail.
  <!-- fix-ok: la doc décrivait les captures d'avant la relecture (colonnes hors cadre, message resté affiché) ; mise à jour sur les captures refaites le 21/11 à 00:46-00:48. -->
  Le thème sombre n'a pas été capturé : l'écran n'emploie que les classes communes `rig-*` des autres
  écrans, qui portent les deux thèmes.

## Utiliser en développement

```powershell
# Base V3 de développement, migrations jouées (architecture.md), puis la reprise d'une base V2 :
#   RepriseDroits.ReprendreAsync("<chaîne V2>", "<chaîne V3>")  -- base V3 neuve seulement
# Elle se déclare au catalogue de la base commune (ici la même), sous le code du greffe :
#   INSERT INTO soc.Greffe (CodeGreffe, Nom, Serveur, BaseV2, BaseV3, EstBeta, EstOutreMer, Fuseau, ModifiePar, ModifieLe)
#   VALUES ('3801', 'Greffe de dev', '127.0.0.1,14330', 'RIG_DEV', 'RIGV3_DEV', 0, 0, 'Romance Standard Time', 'dev', SYSDATETIME());
# Serveur en développement (changement de compte par l'en-tête) :
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__RigV3 = "Server=127.0.0.1,14330;Database=RIGV3_DEV;User ID=sa;Password=RigV3_dev_2026!;TrustServerCertificate=True"
$env:Greffes__Authentification = "User ID=sa;Password=RigV3_dev_2026!;TrustServerCertificate=True"
dotnet run --project Serveur\RigV3.Serveur.csproj --urls http://127.0.0.1:5080
# L'app, sous un compte qui a le rôle GestionDesHabilitations, sur le greffe 3801 :
$env:RIGV3_SERVEUR = "http://127.0.0.1:5080"; $env:RIGV3_COMPTE = "<compte>"; $env:RIGV3_GREFFE = "3801"
```

Écrire au journal depuis une autre route (lots des domaines) — une route DU greffe (`{greffe}` dans l'adresse) :

```csharp
await using var cn = await droits.OuvrirBaseAsync(http, b, ct);   // la base du greffe de l'adresse
var moi = (await droits.AppelantAsync(http, b, cn, ct))!;         // ServiceDroits, en paramètre de la route
await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
// ... le geste ...
await JournalGestes.EcrireAsync(cn, tx, moi, demande.Ecran, "facture", $"la facture {num}", num,
                                "a modifie", avant, apres, ct);   // dans la transaction du geste
await tx.CommitAsync(ct);
```

Vérifier un droit : `await droits.VerifierAsync(http, b, moi, "FACT", ct)`, dans la base du greffe de
l'adresse (jamais d'exception : une panne est un refus tracé).
<!-- fix-ok: la doc décrivait les routes sans greffe, la clé Greffe:Code et « Juger dans la base DU GREFFE » en reste à faire ; le lot 9 a fait ce raccord (banc SocleDroitsCheck, contrôles « Raccord (1) », code 0 le 2026-11-21). -->
