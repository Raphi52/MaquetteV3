# Supervision et journal technique — SOC-017 et SOC-011

Lot 2 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json` :

- **SOC-017** — retrouver une erreur technique sans aller sur les serveurs (technicien d'assistance d'Amitel) ;
- **SOC-011** — surveiller la partie serveur et ses traitements depuis Amitel (exploitant d'Amitel).

Banc : `.\tests.ps1 SocleSupervision` (`tests\SocleSupervisionCheck`), 31 contrôles, un par critère au moins.

## Ce que la V2 fait, et ce que la V3 fait à la place

| Besoin | V2 (outils cités par les stories) | V3 (ce lot) |
|---|---|---|
| Lire une erreur d'agent | Fichiers `C:\rig\data\log\<greffe>\…` sur le serveur où tourne l'agent (`logs-application.md:25-29`), purgés en quelques jours (`:60-63`) ; outre-mer : partage `\\rigapp-<territoire>\RIG\data\log\` (`:48-58`) | Table `soc.ErreurTechnique` de la base **commune** : les 40 greffes au même endroit, outre-mer compris ; recherche par identifiant, greffe, compte, période (écran « Supervision », route `GET /socle/supervision/erreurs`) |
| Erreurs graves en base | `COMMUN_RIG.dbo.LOG_RIG_ERREUR` (plantages `eLog9Crash` seulement, 2 482 lignes le 2026-11-20), sans rien que l'agent puisse citer | Toute erreur non prévue (serveur, écran, traitement) **et** toute erreur interceptée puis journalisée (`ILogger`, avertissement et au-delà) ; chacune porte l'**identifiant de suivi** du parcours (« 7KQ2-M9XP »), montré à l'agent |
| Erreurs avalées | Plusieurs `catch` qui avalent (`proc_accueil.md:298-299`, `Affaire.cs:140-143`) | Recherche statique dans le banc : aucun `catch` qui attrape tout sans relancer ni journaliser dans `Serveur\` ni dans `Pont\Repartiteur.cs` |
| État des services | `COMMUN_RIG.dbo.ETAT_SERVICE`, une ligne `ETSVC_LAST = 1` par programme et serveur **sans le greffe**, lue à la main ; 11 550 694 lignes jamais purgées | `soc.EtatTraitement` (état courant, une ligne par serveur et traitement, greffe de l'élément en erreur) + `soc.EtatTraitementHistorique` purgé ; publié à chaque passage (`Supervision:IntervalleSecondes`, 60 s) |
| Inventaire des services | `ServiceExploit` / `ClientExploit` interroge chaque serveur (`catalogue-services.md:240-246`) | Chaque serveur publie lui-même dans la base commune ; l'écran ne se connecte à aucun serveur |
| Démarrer / arrêter | `MaintenanceServices`, outil d'administration sur le serveur (`catalogue-services.md:323`) | Bouton « Relancer » de l'écran (route `POST /socle/supervision/traitements/relancer`), tracé au journal des gestes avec son auteur |
| File d'attente | `COMMUN_RIG.dbo.AMIMESSAGE` (18 201 617 lignes) | `soc.FileTraitement` : un élément reste en file tant qu'il n'est pas fait ; un traitement arrêté la garde |
| Alerte | Aucune | Règles d'alerte à chaque passage, canal `ICanalAlerte` (simulé en développement) |
| Bases des greffes, migrations | — | `soc.EtatGreffe` : la base V3 répond-elle, a-t-elle toutes les migrations de la base commune |
| Échecs de numérotation (NUM-007 CA05) | `Base\v2\echecs.sql` lancé à la main avec sqlcmd | Le **même** script, embarqué dans le serveur (`RigV3.Serveur.csproj`), joué sur l'instance de chaque greffe à chaque passage ; alerte dès le premier échec |

## Où c'est tenu

| Couche | Fichier |
|---|---|
| Base | `Base\migrations\SOC-017_erreurs.sql` (`soc.ErreurTechnique`, `soc.ConservationTechnique`), `Base\migrations\SOC-011_supervision.sql` (`soc.FileTraitement`, `soc.EtatTraitement` et son historique, `soc.EtatGreffe`, `soc.AlerteExploitation`, famille `supervision` du journal des gestes, durées « etats », « alertes », « file ») |
| Contrats | `Contrats\Socle\Supervision.cs` (`RoutesSupervision`, états, règles, `ErreurTechnique`, `EtatSupervision`…) |
| Serveur | `Serveur\Socle\Supervision\` : `JournalErreurs.cs` (écriture nettoyée, recherche, intercepteur des routes, filtre de la page d'exception de développement, journal `ILogger` → base), `Traitements.cs` (`ITraitementDeFond`, `MoteurTraitements`), `Surveillant.cs` (passage : greffes, échecs, publication, alertes, purge ; `LectureEchecs`), `CanalAlerte.cs`, `ModuleSupervision.cs` (routes, politique `ExploitationAmitel`) |
| Écran | `Pont\EcranSupervision.cs`, `web\src\ecrans\EcranSupervision.tsx` (+ test), menu **Configurer › Outils › Supervision** (`PROC_SUPERVISION`) |
| Erreurs de l'écran | `Pont\Repartiteur.cs` : une méthode du pont qui lève part au journal (`EcranSupervision.SignalerDepuisLePontAsync`) ; l'agent lit « Erreur technique dans RIG. Donnez l'identifiant de suivi XXXX-XXXX à l'assistance d'Amitel. » ; `Pont\ClientServeur.cs` : `ErreurServeur.Suivi` (en-tête `X-Rig-Suivi`) rattache l'erreur de l'écran au parcours de l'erreur du serveur |

## Les critères, un par un

| Critère | Tenu par | Contrôle du banc |
|---|---|---|
| SOC-017 CA01 | `IntercepteurErreurs` / `FiltreErreursDeveloppement` (500 avec le seul identifiant), route `POST /socle/erreurs` (écran), compte pris de la session | l'agent reçoit 500 + identifiant (aussi en en-tête), ni message ni pile ; ligne en base avec date, greffe, compte, serveur, programme, message, pile ; erreur d'écran avec le compte de la session ; en production aussi (serveur sans page de développement) |
| SOC-017 CA02 | en-tête `X-Rig-Suivi`, `Parcours.Courant`, route `GET /socle/supervision/erreurs/{suivi}` | l'identifiant ramène, dans l'ordre, l'erreur interceptée, la panne du serveur et l'erreur de l'écran ; l'écran retrouve le parcours ; **détail refusé hors Amitel** (403, 401 sans session ; jumeau 200) |
| SOC-017 CA03 | `FournisseurJournalTechnique` (catégories `RigV3.*`) ; erreurs des traitements écrites par le moteur | erreur interceptée retrouvée dans le parcours ; recherche statique (avec son témoin rouge et ses jumeaux) |
| SOC-017 CA04 | `soc.ConservationTechnique`, `Surveillant.PurgerAsync` (lots de 5 000) | graves 730 j > erreurs 180 j > traces 30 j ; la purge retire et garde exactement ce qu'il faut |
| SOC-017 CA05 | `Nettoyage.Texte` (mots de passe, jetons, blocs base64 ≥ 160 signes), aucune colonne de corps de requête | ni mot de passe, ni jeton, ni pièce dans le message et la pile ; jumeau : le reste du message est gardé |
| SOC-017 CA06 (+T05) | une seule base commune | une erreur de chacun des 40 greffes se retrouve par son greffe ; les 6 d'outre-mer dans la même liste |
| SOC-011 CA01 | `Surveillant.PublierAsync`, `MoteurTraitements.EtatsAsync` | états publiés + historique ; en marche → en attente → travaux en cours → en marche ; serveur en production laissé seul : 3 publications à 1 s d'écart |
| SOC-011 CA02 | `Surveillant.EvaluerAsync` / `EnvoyerAsync`, `ICanalAlerte` | base de Vienne arrêtée : vue et alertée **une** fois, fermée au retour ; base sans migration alertée ; premier échec de numérotation (session posée par le bloc même de `bascule.sql`, lue par `echecs.sql`) alerté ; traitement en erreur alerté avec le greffe de l'élément ; file qui ne baisse plus depuis le délai réglé alertée ; jumeaux sans alerte |
| SOC-011 CA03 | route `GET /socle/supervision/etat` (base commune seule) | 40 greffes, 6 d'outre-mer, tous vérifiés, lus en 0,3 s pendant qu'une base est arrêtée ; 403 hors Amitel ; écran Amitel en mode serveur |
| SOC-011 CA04 | `MoteurTraitements.RelancerAsync` + journal des gestes dans la même transaction | agent refusé (rien ne bouge) ; relance : 2 éléments repris, chacun fait une fois, rien perdu ; trace avec l'auteur ; 409 si déjà en marche, 404 inconnu ; relance d'un traitement « arrêté » depuis l'écran |
| SOC-011 CA05 | `Surveillant.PurgerAsync` (« etats » 31 j, « alertes » 365 j, « file » 31 j) | historique de 40 j purgé, d'hier gardé ; alerte fermée il y a 400 j purgée, alerte ouverte jamais |

**Contre-épreuve du contrôle clé** (règle 3 du plan), 2026-11-21 : la route du parcours protégée seulement par
`AgentAdmis` (sans `ExploitationAmitel`) → banc **rouge**, 2 contrôles en échec (« Accès : 3 routes réservées » et
« SOC-017 CA02 : le détail d'une erreur est refusé hors Amitel (agent de greffe 200 …) ») ; protection remise → vert.

## Se servir de la supervision

```powershell
# Serveur (voir architecture.md) : la supervision tourne seule. Réglages facultatifs :
$env:Supervision__IntervalleSecondes = "60"          # publication et vérification des greffes
$env:Supervision__DelaiFileBloqueeSecondes = "1800"  # une file qui ne baisse plus depuis 30 min alerte
$env:Supervision__SessionEchecs = "rigv3_numerotation_echecs"   # celle de Base\v2\bascule.sql
$env:Supervision__DossierAlertes = "D:\RigV3\alertes"            # canal simulé : une ligne JSON par alerte
$env:Supervision__Site = "RIGV3-APP-01"                          # nom du serveur dans les états (défaut : la machine)
```

- L'écran : **Configurer › Outils › Supervision**, réservé aux comptes marqués Amitel (`soc.Compte.EstAmitel`) de
  la base commune. Sans `RIGV3_SERVEUR`, une démonstration en lecture seule : les 40 vrais greffes, des états
  inventés.
- Écrire un traitement de fond : une classe `ITraitementDeFond` (un élément à la fois ; une exception l'arrête,
  l'élément reste en file), inscrite par son module (`services.AddSingleton<ITraitementDeFond, MonTraitement>()`),
  alimentée par `MoteurTraitements.DeposerAsync`. Elle apparaît seule dans la supervision.
- Brancher le vrai canal d'alerte : une classe `ICanalAlerte` inscrite par son module ; elle remplace le canal
  simulé (`TryAddSingleton`). Hors développement et recette, le canal simulé écrit chaque alerte en **erreur** au
  journal : un serveur de production sans canal réel ne se tait pas.

## Choix à valider (règle 4 du plan)

| Sujet | Choix fait | À valider par |
|---|---|---|
| Durées de conservation | erreurs graves 2 ans, erreurs 6 mois, traces 30 jours ; états 31 jours, alertes fermées 1 an, éléments faits 31 jours (`soc.ConservationTechnique`, modifiables sans code) | référent Socle et délégué à la protection des données (C17) |
| Qui voit la supervision | tout compte admis de la base commune marqué Amitel (`UTLST_ISAMITEL` de la V2) ; ni administrateur ni assistance exigés | exploitation d'Amitel |
| Une alerte par cause | la première panne alerte ; tant que la cause dure, son texte se met à jour sans renvoi ; la cause disparue, l'alerte se ferme. Les échecs de numérotation restent comptés jusqu'au redémarrage de l'instance (session en mémoire) : l'alerte reste ouverte jusque-là | exploitation (faut-il un acquittement ?) |
| File bloquée | alerte même si le traitement est arrêté par l'exploitant | exploitation |
| « en attente » | traitement démarré dont des éléments attendent (la V2 n'a pas laissé de définition écrite de ses états) | exploitation |
| Erreurs d'écran | la route `POST /socle/erreurs` est ouverte à toute session Windows (une erreur peut précéder l'admission), sans limite de débit | exploitation / sécurité |

## Ce qui ne se fait pas depuis ce poste, et pourquoi

- **Les 40 vraies bases** (instances `SQL-PROD\PROD`, `RIGBD-ANTILLES`, `RIGBD-REUNION`, `RIGBD-POLYNESIE`) : aucune
  base V3 n'y existe et ce poste n'a pas le droit d'y écrire ; le banc pose les 40 greffes réels sur l'instance
  locale. À vérifier au déploiement du serveur chez Amitel.
- **Le réseau des territoires** : qu'un écran sur un RDS d'outre-mer joigne le serveur d'Amitel (SOC-017 CA06).
- **Le canal réel d'alerte** : à choisir par l'exploitation (courriel via SOC-006, outil d'astreinte).
- **Plusieurs serveurs** : chacun publie ses traitements et peut évaluer les règles (index unique des alertes
  ouvertes) ; une relance doit atteindre le serveur qui fait tourner le traitement (aujourd'hui : celui qui reçoit la
  demande). Aucun traitement de fond du socle n'existe encore (courriels, recopie : lots suivants).
- **La V2 n'est pas reprise** (T05 des deux stories) : `ETAT_SERVICE` et `LOG_RIG_ERREUR` restent dans `COMMUN_RIG`.
  `ETAT_SERVICE` n'existe pas dans le `COMMUN_RIG` de `SQL-DEV\DEV` (lu le 2026-11-21) : les chiffres viennent du
  carnet (lecture de production du 2026-11-20).

## Fichiers touchés hors de la liste du lot, et pourquoi

| Fichier | Pourquoi |
|---|---|
| `Base\migrations\SOC-017_erreurs.sql`, `SOC-011_supervision.sql` | les tâches T01 « base » des deux stories (fichiers neufs, aucun fichier commun modifié) |
| `Serveur\RigV3.Serveur.csproj` | embarquer `Base\v2\echecs.sql` (une ligne) : le serveur joue le script de l'exploitation, pas une copie |
| `Pont\ClientServeur.cs` | `ErreurServeur.Suivi` : l'identifiant de la panne du serveur, que l'erreur de l'écran reprend |
| `Pont\Repartiteur.cs` | trois lignes : une méthode du pont qui lève part au journal, l'agent lit l'identifiant (SOC-017 T03) |
| `Pont\MenuAccueil.cs`, `Pont\CatalogueEcrans.cs`, `web\src\plan\Aiguillage.tsx`, `tests\MenuGestesCheck\Program.cs` | placer l'écran au menu (Configurer › Outils) et le déclarer sans fiche V2 |
