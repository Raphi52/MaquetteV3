# Numérotation — la V2 demande ses numéros à la V3

Lot 7 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-21. Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\numerotation.json` :

| Story | Ce qu'elle demande | Où c'est fait |
|---|---|---|
| NUM-002 | La V2 obtient ses numéros comme avant, mais du compteur de la V3, sans recompiler la V2 | `Base\v2\GENERER_NUMERO.sql`, `RESTITUER_NUMERO.sql`, `pont-v2.sql` (`rigv3.DemanderNumero`, `rigv3.RendreNumero`) |
| NUM-003 | Le second programme (`CHERCHER_NUMERO`) passe aussi par la V3, chaque appel journalisé | `Base\v2\CHERCHER_NUMERO.sql`, `rigv3.AppelChercher` ; relevé des appelants `Base\v2\releve-appelants.sql` |
| NUM-006 | La table des compteurs de la V2 reste juste pour les écrans et traitements qui la lisent | `Base\v2\pont-v3.sql` (recopie V3 → V2 dans la même transaction) ; garde `dbo.TR_NUMERO_METIER_RIGV3` (`pont-v2.sql`) |
| NUM-007 | Compteur injoignable : message clair, code non nul, rien de consommé | `rigv3.BaseV3Injoignable`, `rigv3.MessageIndisponible` (`pont-v2.sql`) ; échecs comptés : `bascule.sql` (session d'événements), `Base\v2\echecs.sql` |
| NUM-022 | Pendant la bêta, rendre à la V2 ses trois programmes et ses compteurs à jour | `Base\v2\retour.sql`, `Base\v2\fin-de-beta.sql` |

Le compteur lui-même est celui du lot 5 (`num.Attribuer`, `num.Restituer`,
`docs\reconstruction\numerotation-compteur.md`). Ce lot n'a modifié aucun de ses fichiers.

**Preuve** : `.\tests.ps1 NumerotationV2` rend 0 — 58 contrôles verts, au moins un par critère des
5 stories (26 critères : NUM-002 CA01 à CA08, NUM-003 CA01 à CA05, NUM-006 CA01 à CA04, NUM-007 CA01 à
CA06, NUM-022 CA01 à CA03) et pour NUM-002 T03 (la reprise du lot 6 jouée dans la transaction de la
bascule), et 6 lignes « RESTE » pour ce qu'aucun banc du poste ne peut faire (fin de ce document).
Durée ≈ 40 s. Contre-épreuves faites, chacune rejouée puis le fichier remis à l'identique :

| Défaut réinjecté | Le banc passe au rouge sur |
|---|---|
| la reprise validée avant la suite de la bascule (`COMMIT` après `num.Reprendre`) | NUM-002 T03 : la bascule qui échoue au dernier programme laisse la V3 avec ses compteurs (14/3/1 au lieu de 0/0/0) |
| la recopie écrit aussi les séries de la V3 seule | NUM-006 CA01 (une ligne FACV3 dans la V2), NUM-022 CA01 (2 lignes FACV3/BDFNI après le retour) |
| `GENERER_NUMERO` n'envoie plus son message « 1 ligne affectée » | NUM-002 CA01 : l'appel VB6 par lot SQL (`modGeneral.bas`) ne reçoit plus rien |
| la recopie V3 → V2 du chrono coupée | NUM-006 CA01 (4 contrôles), NUM-006 CA02 |
| la garde de `NUMERO_METIER` désarmée | NUM-006 CA04 ; et le retour arrière refuse ensuite (« compteur V2 absent de la V3 ») |
| un programme d'origine gardé qui ne compile plus | NUM-022 CA01 : tout le retour est annulé (contrôle permanent du banc) |
| le script lu dans une autre page de code que UTF-8 | refus de la bascule (contrôle permanent du banc) |
| l'agent du banc appelle le serveur sans en-tête de session | NUM-006 CA01 : arrêt sur « POST /numerotation/8002/numeros : 401 Unauthorized » |

## Comment c'est fait

```
  base V2 du greffe (RIG_X)                                   base V3 du greffe (RIGV3_X), même instance
  ─────────────────────────                                   ─────────────────────────────────────────
  GENERER_NUMERO ┐                                            num.Attribuer ── num.Compteur ──┐
  CHERCHER_NUMERO┴─ rigv3.DemanderNumero ─ rigv3.Attribuer ─►              num.NumeroRendu ─┤ déclencheurs
  RESTITUER_NUMERO ─ rigv3.RendreNumero ── rigv3.Restituer ─► num.Restituer                  │ num.TR_*_RecopieV2
                                                                                             │
  NUMERO_METIER ◄───────────── num.NumeroMetierV2 (synonyme) ◄──────────────────────────────┘
      └ dbo.TR_NUMERO_METIER_RIGV3 : refuse toute autre écriture (51006)
      └ dbo.TR_NUMERO_METIER_JET (NF525, s'il est là) : voit les changements de mode recopiés
```

- **Même contrat.** Les trois programmes gardent leur nom, leurs paramètres, leurs types, leurs tailles
  et leurs valeurs par défaut (contrôlé sur `sys.parameters` contre ceux d'origine) : aucun appelant n'est
  recompilé. Ils gardent aussi la **forme de leur réponse** : un message « 1 ligne affectée » quand le
  numéro est donné, aucun sinon (mesuré sur l'ancien corps le 2026-11-21). L'appel VB6 par lot SQL en
  dépend : il saute ce premier résultat avant de lire le numéro (`RIG_CliSerL_Inscription\modGeneral.bas:85`,
  « Set rst = rst.NextRecordset », après l'appel de la ligne 83).
- **Même transaction, sans réseau** (Q4, NUM-002 CA06) : `rigv3.Attribuer` est un synonyme vers
  `[RIGV3_X].num.Attribuer`, qui travaille dans un point de sauvegarde de la transaction de l'appelant.
  Une transaction de la V2 annulée annule le numéro, la ligne de la V2 et le journal de la V3.
- **Le corps est le même dans les 40 bases** (NUM-002 T02) : seuls les synonymes et la fiche `rigv3.Pont`
  portent le nom de la base V3 et le code du greffe.
- **La recopie V3 → V2** (NUM-006 CA01) est faite par deux déclencheurs posés dans la base V3, sur
  `num.Compteur` et `num.NumeroRendu`. Elle suit **toute** écriture du compteur — attribution pour la V2,
  attribution de la V3 pour elle-même, restitution, nouvelle année, nouveau jour — dans la même
  transaction. Correspondance : un compteur `(Type, Annee, Jour)` = les lignes « compteur » de même type et
  année (et de même quantième pour une série par jour) ; `Chrono NULL` = `NMMTR_CHRONO 0` ; un numéro rendu
  = une ligne « attribution erronée », quantième NULL hors série par jour, comme l'écrivait
  `RESTITUER_NUMERO.sql:114-123`. Un simple avancement du chrono ne touche pas aux colonnes de mode : le
  déclencheur NF525 de la V2 n'émet rien ; un changement de mode les touche et il émet son événement 10.
  Une **série de la V3 seule** — un type dont la V2 n'a aucune ligne « compteur », comme FACV3 et BDFNI que
  la reprise du lot 6 ouvre au passage — n'a pas de « ligne correspondante » (NUM-006 CA01) : elle n'est ni
  recopiée ni comptée comme écart. Sinon la V2 porterait ces types, et `num.Reprendre` refuserait de
  reprendre de nouveau sa base (`NUM-005_reprise.sql:631-632` ; mesuré : 1 ligne FACV3 à la première facture
  de la V3, 2 lignes FACV3/BDFNI au retour, avant la correction).
- **La garde** (NUM-006 CA04) refuse toute écriture dans `NUMERO_METIER` qui ne vient pas de la recopie,
  même du propriétaire de la base. La recopie se signale par `SESSION_CONTEXT('rigv3.recopie') = 1` : c'est
  une garde contre l'erreur (un ancien outil de reprise, un ancien programme resté en cours pendant la
  bascule), pas contre un programme qui voudrait la tromper.

### Ce que rendent les programmes remplacés

| Code | `GENERER_NUMERO` / `CHERCHER_NUMERO` | `RESTITUER_NUMERO` (code aussi dans `@MonErreur`) |
|---|---|---|
| 0 | numéro donné | rendu, série sans restitution (journalisé), ou déjà rendu |
| -2 | type inconnu, rien d'écrit, **sans message** (comme l'ancien) | type inconnu |
| -3, -4 | — | numéro illisible, jamais donné (NUM-004) |
| -5, -6 | refus du compteur, **erreur levée** avec son message : série pleine ou plage épuisée, année au-delà de la suivante (lot 6) | — |
| -9 | **numérotation indisponible**, erreur levée, rien consommé | indisponible, erreur levée, rien écrit |

Jamais 0 avec un numéro vide (NUM-002 CA07). L'erreur est levée par `RAISERROR` (gravité 16), pas par
`THROW` : elle ne coupe pas le lot de l'appelant, qui lit donc aussi le code (NUM-007 CA01 : une erreur
**et** un code non nul). Son **état** dit la cause : 1 base V3 hors service, 2 compteur tenu au-delà de
5 s, 3 erreur pendant l'appel, 5 refus du compteur.

Le message de NUM-007, mot pour mot :
« Numérotation indisponible : la base de la V3 du greffe <code> ne répond pas. Aucun numéro n'a été
attribué. Réessayez ou appelez l'assistance d'Amitel. » (« rendu » pour une restitution).

Avant l'appel, `rigv3.BaseV3Injoignable` regarde l'état de la base V3 (hors ligne, en restauration, en
utilisateur unique ou restreint, accès ou droit `EXECUTE` absent) : prouvé de ces quatre façons. Un
compteur tenu par une autre transaction est attendu **5 s** puis donne la même erreur. La partie serveur et
l'écran de la V3 n'y sont pour rien : arrêtés, la V2 numérote (NUM-007 CA04).

## Le script de bascule

À jouer pour un greffe au moment de son passage, **depuis `Base\v2`** (`:r` lit les fichiers du dossier
courant), la V2 du greffe fermée aux saisies :

```powershell
cd D:\RigV3Desktop\Base\v2
sqlcmd -S SQL-PROD\PROD -E -f 65001 -b -i bascule.sql `
       -v BaseV2=RIG_AMIENS BaseV3=RIGV3_AMIENS CodeGreffe=8002 Reprise=oui FinBeta=2027-06-30 `
          SessionEchecs=rigv3_numerotation_echecs
```

| Variable | Sens |
|---|---|
| `BaseV2`, `BaseV3`, `CodeGreffe` | du catalogue des greffes (`soc.Greffe`, SOC-007 : `BaseV2`, `BaseV3`, `CodeGreffe`) ; les deux bases sur la même instance |
| `Reprise` | `oui` : la reprise des compteurs (`num.Reprendre`, NUM-005, lot 6) est jouée **dans la transaction de la bascule** — personne ne numérote entre les deux (NUM-002 T03) ; `faite` : jouée avant, la bascule vérifie que rien n'a bougé |
| `FinBeta` | `AAAA-MM-JJ` : jusque-là le retour arrière est possible, le corps d'origine est gardé ; `non` : greffe hors bêta, rien n'est gardé, pas de retour |
| `SessionEchecs` | la session d'événements de l'instance qui compte les échecs (une par instance) |

`-f 65001` : le script et ses messages sont en UTF-8. Sans cette option, `sqlcmd` lit un fichier non
Unicode dans la page de code du poste ([sqlcmd, option -f](https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility)) :
les trois scripts qui écrivent des messages **refusent alors de tourner** (contrôle `UNICODE(N'é') = 233`,
prouvé par le banc sur `bascule.sql` ; le même contrôle ouvre `retour.sql` et `fin-de-beta.sql`). Le `sqlcmd` en Go installé sur ce poste refuse l'option `-f` (« 'f': Unknown Option »,
2026-11-21) mais lit l'UTF-8 de lui-même : l'y retirer.

Ce qu'il fait, dans **une** transaction, `NUMERO_METIER` et les compteurs de la V3 verrouillés en exclusif :

1. contrôles : les deux bases sur l'instance, même collation, script lu en UTF-8, compteur V3 en place,
   programmes de la V2 d'origine (pas déjà basculés) ;
2. `Reprise=oui` : la reprise des compteurs, `num.Reprendre @BaseV2` du lot 6, **dans cette transaction**
   (NUM-002 T03) ; un code non nul (-1 écart, -10 base V3 déjà reprise, -11 base V2 introuvable) refuse la
   bascule. `Reprise=faite` : constat qu'elle a été jouée avant ;
3. côté V3 : synonyme, vue `num.EcartsV2`, déclencheurs de recopie ; puis **rapprochement** : chaque
   compteur de la V2 a son compteur V3 au même chrono, chaque numéro rendu de la V3 a sa ligne dans la V2 —
   sinon **refus 51020** et tout est défait (prouvé : avant la reprise, et quand la V2 a numéroté seule
   après un retour). Les séries de la V3 seule (FACV3, BDFNI) ne sont pas des écarts ;
4. côté V2 : schéma `rigv3`, fiche `rigv3.Pont`, corps d'origine des trois programmes avec leurs réglages
   (`QUOTED_IDENTIFIER OFF` : ils écrivent `"9999"`) et leurs droits (`GRANT EXECUTE … TO public` sur RIG_DEV),
   puis les trois programmes remplacés ;
5. après la transaction : la session d'événements des échecs, si l'instance ne l'a pas encore ; puis, avec
   `Reprise=oui`, le relevé des valeurs héritées (`num.ReleverHeritage`, lot 6 : rôles en double, numéros
   hors format, numéros portés deux fois), en lecture, hors du blocage. S'il échoue, la bascule est déjà
   validée : le rejouer seul.

**Prouvé avec la vraie reprise** (`Reprise=oui`, banc) : la V2 jetable porte, en plus de `NUMERO_METIER`,
les tables que lisent `num.Reprendre` et `num.ReleverHeritage` (`NUMERO_METIER_SAVE_CDU`,
`NUMERO_CHRONO_MANQUANT`, `GREFFE_NUMERO_LIAISON`, `MENTION_OBSERVATION`, `ETABLISSEMENT`, `EDI_BDF`,
`INSTANCE`, `DOSSIER_RCS`, `FACTURE`, voir `tests\NumerotationV2Check\v2\reprise.sql`), un cas de chaque.
La bascule rend la reprise validée à zéro écart (12 lignes, 14 compteurs dont FACV3 et BDFNI à 32), les
ordres par fiche, le code de liaison, l'archive, le numéro manquant, puis les trois valeurs héritées. Une
bascule qui échoue au dernier programme (`RESTITUER_NUMERO` refusé) **annule aussi la reprise** : la V3
reste sans compteur, la V2 garde ses programmes et sa table — la reprise et le remplacement sont bien dans
la même transaction.

`NUMERO_METIER` n'est pas touchée : en place et intacte (NUM-006 T03, contrôlé ligne à ligne) ; les huit
lectures de la V2 rendent la même chose avant et après (NUM-006 CA02).

## Le retour arrière (bêta seulement)

```powershell
cd D:\RigV3Desktop\Base\v2
sqlcmd -S SQL-PROD\PROD -E -f 65001 -b -i retour.sql -v BaseV2=RIG_AMIENS BaseV3=RIGV3_AMIENS CodeGreffe=8002
```

Dans **une seule transaction** (NUM-022 CA01), les deux tables verrouillées :

1. **refus 51022** si le pont n'est pas en service, si le greffe est hors bêta, si la bêta est finie
   (`FinBeta` passée) ou si le corps d'origine a été archivé (NUM-022 CA03, prouvé des trois façons) ;
2. la V2 reçoit le chrono courant de chaque compteur et les numéros rendus de la V3 : chaque ligne repasse
   par la recopie, puis `num.EcartsV2` doit être vide, sinon refus ;
3. la V3 est **fermée** pour ce greffe : recopie retirée, déclencheurs `num.TR_Compteur_RenduALaV2` et
   `num.TR_NumeroRendu_RenduALaV2` qui refusent toute écriture (51023). Un numéro que la V3 donnerait encore
   serait un doublon de la V2 ;
4. les trois programmes d'origine sont recréés tels qu'ils étaient : texte, `QUOTED_IDENTIFIER`,
   `ANSI_NULLS`, droits (contrôlé) ;
5. le pont quitte la V2 (garde, synonymes, procédures) ; ses tables restent, la fiche dit le retour.

Toute erreur annule tout (prouvé : un corps d'origine qui ne compile plus → rien n'est rendu, la V3 n'est pas
fermée). Après le retour, la V2 continue au chrono où la V3 s'était arrêtée, redonne d'abord les numéros que
la V3 tenait pour rendus ; sur la série RGB 2026 du banc, les numéros donnés par la V2 basculée, la V3 et la
V2 revenue se suivent sans trou ni doublon. Les actes de la V3 gardent leurs numéros, sous le compteur de la
V2 (NUM-022 CA02) ; leur recopie dans la V2 est REC-007.

Une **nouvelle bascule** du même greffe après un retour demande que la V3 rattrape d'abord la V2, qui a
numéroté seule. **Ce rattrapage n'existe pas encore** : `num.Reprendre` ne reprend qu'une base V3 sans
compteur (-10), et vider `num.Compteur` ferait perdre le chrono des séries de la V3 seule (FACV3 : les
factures déjà numérotées par la V3 seraient redonnées) et heurterait `num.OrdreFiche` et `num.CodeLiaison`
déjà repris. Le retour, lui, la rend possible : il ne laisse dans la V2 aucune ligne FACV3 ni BDFNI
(contrôlé). Le banc fait ce rattrapage à la main pour atteindre l'état de NUM-022 CA03, puis rebascule avec
`Reprise=faite` (voir « Ce qui reste »).

**Fin de la bêta** (le jour J des autres greffes, pour chaque greffe bêta) :

```powershell
sqlcmd -S SQL-PROD\PROD -E -f 65001 -b -i fin-de-beta.sql -v BaseV2=RIG_AMIENS
```

Le corps d'origine passe dans `rigv3.CorpsOrigineArchive` ; le retour refuse ensuite de tourner, même si
quelqu'un repousse `FinBeta` (prouvé).

## Les droits

| Compte | Base V3 | Base V2 |
|---|---|---|
| celui de la V2 (applications) | utilisateur membre de `num_demandeur` (EXECUTE sur les deux programmes) — jamais `db_owner` | ce qu'il a déjà : lire, écrire, exécuter (la recopie écrit `NUMERO_METIER` sous son identité) |
| celui du serveur V3 | `num_demandeur` (lot 5) | **utilisateur avec INSERT, UPDATE, DELETE sur `dbo.NUMERO_METIER`** : quand la V3 numérote pour elle-même, la recopie écrit dans la V2 sous son identité (la chaîne de propriété ne passe pas d'une base à l'autre) |
| l'exploitation qui joue les scripts | `db_owner` | `db_owner` ; `ALTER ANY EVENT SESSION` sur l'instance (session des échecs), `VIEW SERVER STATE` pour `echecs.sql` |

Prouvé par le banc sous un compte SQL d'agent qui n'a que ces droits : les sept appels reçoivent leur numéro,
et le retrait de `num_demandeur` donne l'erreur prévue.

Tant que le pont est en service, la V3 ne numérote que si la base V2 de son greffe répond (la recopie est
dans la transaction) : les deux bases suivent le même plan de sauvegarde et de disponibilité (NUM-007 T03).

## Les échecs comptés (NUM-007 CA05)

Chaque « Numérotation indisponible » levé par un programme remplacé est gardé par la session d'événements
de l'instance (`error_reported`, message `Num_rotation indisponible :%`, avec la base, le poste, le compte,
l'état), **même si l'appelant annule sa transaction** — une table de la V2 l'aurait perdu avec elle. En
mémoire : les 10 000 derniers, jusqu'au redémarrage de l'instance. Lecture :

```powershell
sqlcmd -S SQL-PROD\PROD -E -f 65001 -i echecs.sql -v SessionEchecs=rigv3_numerotation_echecs -W -s ";"
```

Une ligne par base V2 : combien, le premier, le dernier, la cause du dernier. Le banc en provoque 14 et en
lit 14. L'alerte vers l'exploitation est la supervision SOC-011, qui lira cette requête.

## Les sept appels de la V2 et leur geste en cas d'échec (NUM-007 CA03, pour le référent)

Le banc rejoue chacun tel qu'il est écrit (types, tailles, sens des paramètres, lecture du résultat), sur la
V2 basculée et sur une V2 d'origine : même numéro des deux côtés. Base V3 hors service, **chacun reçoit
l'erreur avec son message, aucun ne reçoit de numéro**. Ce qu'il en fait ensuite est dans son code :

| Appel | Source | Code greffe, sortie | Geste après l'erreur (lu dans le code) |
|---|---|---|---|
| .NET central | `Outils\DLL\RigMetier\Numerotation\Numerotation.cs:44-75` | absent, 40 | **avale l'erreur et rend un numéro vide** (`:70-74`) ; l'intégration des mentions d'office enregistre alors l'acte **sans numéro** (`Edi\DLL\EdiMetier\EdiMofRofPc\EdiMofRofEdiEntrant.cs:101-106`) |
| VB6 central | `VB6\Bin Client Serveur Local\RIG_CliSerL_Numerotation\Numero.cls:278-314` | vide, 40 | rend `enuErrorConnexion` et écrit le journal ; l'appelant décide |
| VB6 par lot SQL (inscriptions) | `VB6\Bin Client Serveur Local\RIG_CliSerL_Inscription\modGeneral.bas:64-97` | vide, 20 | **avale l'erreur et rend une chaîne vide** : acte sans numéro possible |
| injecteur d'avis (DMD) | `EdiOld\VB6\InjecteurEdi_AVISIR\TraitementAvis.cls:165-208` | fourni, 20 | relance l'erreur : s'arrête |
| surveillance des inscriptions | `RCS\Exe\Surveillance des inscriptions\frmDebiteurs.vb:665-695` | fourni, 20 | l'exception remonte (Try/Finally sans Catch) : s'arrête |
| inscriptions en masse | `RCS\Exe\InscriptionsMasse\DetailsDebiteur.vb:1598-1615` | fourni, 20 | journalise et relance : s'arrête |
| inscriptions en masse, OutilRIG | `RCS\Exe\InscriptionsMasse\OutilRIG.vb:129-166` | fourni, 20 | journalise et relance : s'arrête |

**À remettre au référent** : trois appelants peuvent enregistrer un acte sans numéro (.NET central et ses
appelants, VB6 par lot SQL). La décision Q16 exclut de corriger la V2 aujourd'hui ; c'est à lui de trancher.

## Le relevé des appelants de CHERCHER_NUMERO (NUM-003 CA02)

`Base\v2\releve-appelants.sql` liste, pour une base, les objets SQL qui citent `CHERCHER_NUMERO` (hors le
programme lui-même et le pont) et les étapes de tâches planifiées de l'instance qui le citent. Prouvé sur
une base sans appelant (aucune ligne) puis avec un objet et une tâche posés exprès (les deux trouvés).

| Où | Résultat | Quand |
|---|---|---|
| code de la V2 (`D:\RigApplication\Source`) | aucun appel ; seule référence : la liste de livraison `RigDatabase.sqlproj:4515` | carnet, 2026-11-20 |
| RIG_DEV, objets SQL (`sys.sql_modules`) | aucun appelant | carnet, 2026-11-20 |
| RIG_DEV, tâches planifiées de SQL-DEV | **non relevé** : le garde-fou SQL des agents refuse désormais `sqlcmd` vers une base non déclarée non-production (2026-11-21) | à faire |
| les 40 bases (34 sur SQL-PROD, 6 outre-mer) | **à faire** avant le passage, résultat à écrire ici | — |

## Écarts avec la V2 (tous « à valider » par le référent Socle)

Règle du plan (décision du lot 5) : un écart que la story a écrit en critère est codé selon le critère ; un
écart sans critère est codé comme la V2.

| Écart | V2 | V3 | Critère | État |
|---|---|---|---|---|
| Code greffe d'un format qui le porte (LIAI) | celui de l'appelant, `9999` par défaut | celui de l'appelant s'il n'est ni vide ni `9999`, sinon celui du compteur ; jamais `9999` dans un numéro (prouvé : vide, absent, `9999` → `G3802…` ; `3801` gardé) | NUM-002 CA08 | à valider |
| Code greffe écrit dans la ligne d'un **jour** créé (DMD, JGDJ) | celui de l'appelant | celui du compteur. Seule différence des huit lectures de la V2 avec la V2 d'origine, mesurée par le banc sur 2 lignes (lecture `clsNumerotation.cls:46`) | NUM-002 CA08 | à valider |
| Création d'une année : code greffe | celui du compteur, même si l'appelant en donne un (`GENERER_NUMERO.sql:202`) | idem (`num.Attribuer` redéfini par le lot 6 : la nouvelle année copie le `CodeGreffe` du compteur précédent, `NUM-005_reprise.sql:357-361`, lu le 2026-11-21) : la règle de CA08 « celui de l'appelant » n'y est pas appliquée | NUM-002 CA08 | à valider |
| `CHERCHER_NUMERO` décrit comme une lecture | il donne un numéro | il donne un numéro, par la V3 | NUM-003 | à valider |
| `CHERCHER_NUMERO` et la lettre de 4 caractères | `SAN2026/00013` (lettre tronquée à 3, caractères fixes non recopiés) | `SANC2026/00013`, comme `GENERER_NUMERO` | NUM-003 CA04 | à valider |
| `CHERCHER_NUMERO` en erreur | sort sans valeur : l'appelant lit 0 | code non nul et message | NUM-003 CA05 | à valider |
| Restitution d'un type inconnu | `@MonErreur` = 0, `RETURN -2` | `@MonErreur` = -2 | NUM-004 CA09 | à valider |
| Explication vide d'une restitution venue de la V2 | ligne V2 avec une explication vide | « sans explication (demande de la V2) » (lot 5), 50 premiers caractères dans la ligne V2 | NUM-004 CA08 | à valider |
| Ligne V2 d'une série par année créée par la V3 | quantième = jour demandé (ou du jour) | quantième du jour de création, sur l'horloge du serveur | — | sans effet : aucune lecture ne s'en sert |
| Série que seule la V3 connaît (FACV3, BDFNI) | — | jamais écrite dans `NUMERO_METIER` : la V2 n'en a pas de ligne correspondante ; ses numéros restent dans la V3 | NUM-006 CA01 | sans effet sur la V2 |

## Ce qui reste, et pourquoi

| Reste | Raison | Qui |
|---|---|---|
| Rattraper la V3 après un retour, pour rebasculer le même greffe | `num.Reprendre` (lot 6) ne reprend qu'une base V3 sans compteur ; il faut une reprise qui remet les séries de la V2 au niveau de la V2 et garde les séries de la V3 seule, `num.OrdreFiche` et `num.CodeLiaison`. Le banc le fait à la main | lot 6 (reprise), avant la bêta |
| NUM-002 T04, NUM-003 T03, NUM-022 T02 : jouer `bascule.sql` sur les 40 bases au passage de chacune, `fin-de-beta.sql` sur les bases bêta le jour J | hors poste (pas de droit d'écriture sur les serveurs des greffes) | passage (SOC-024) |
| NUM-003 CA02 / T05 : relevé des appelants sur les 40 bases et les tâches de SQL-DEV | pas d'accès aux serveurs des greffes ; SQL-DEV refusé par le garde-fou SQL des agents | exploitation |
| NUM-006 CA03 : « la V3 l'écrit dans le sien » | aucun programme de la V3 ne change encore le mode d'une série ; sa chaîne signée est SOC-018. La recopie vers la V2 et l'événement NF525 de la V2 sont prouvés | l'écran des séries, SOC-018 |
| NUM-006 T04 : relever sur les 40 bases le déclencheur NF525 de `NUMERO_METIER` et l'activation de la certification | il est dans RIG_DEV mais pas dans le projet de base de la V2 | exploitation |
| NUM-007 CA03 : rejouer l'erreur dans les écrans VB6 et .NET et remettre la liste au référent | la liste ci-dessus vient du code ; le banc ne lance pas les applications de la V2 | référent, recette |
| NUM-007 CA05 : alerte vers l'exploitation dès le premier échec | la supervision (SOC-011) n'existe pas encore ; elle lira `echecs.sql` | SOC-011 |
| NUM-007 T03 : même plan de sauvegarde et de disponibilité pour la base V3 que pour la base V2 | exploitation des serveurs | exploitation |
| NUM-022 CA02 : recopier dans la V2 les actes de la V3 avec leurs numéros | story REC-007 | phase de la recopie |
| NUM-022 T03 : répéter le retour sur une copie anonymisée d'une base bêta | aucune copie anonymisée sur ce poste ; le banc le joue sur une V2 jetable avec les réglages de RIG_DEV | avant la bêta |
| Écrans « à maquetter » des cinq stories (état du pont, échecs, relevé) | hors de la liste de fichiers de ce lot (aucun `web\src\ecrans`) ; les données sont dans `rigv3.Pont`, `rigv3.AppelChercher`, `echecs.sql` | plan suivant |
| Jouer les scripts par `sqlcmd.exe` lui-même sur un serveur de recette | le banc les joue par son lecteur de scripts (variables, `:r`, `GO`, une connexion), pas par `sqlcmd.exe` : le garde-fou SQL des agents le refuse vers les bases jetables | exploitation, première bascule de recette |

## Travailler en développement

```powershell
docker compose up -d                         # le SQL Server du poste
.\tests.ps1 NumerotationV2                   # le banc (≈ 40 s)
```

Le banc monte cinq bases jetables : la V3 (toutes les migrations), la V2 du greffe et un témoin V2 jamais
basculé (les seuls objets utiles, copiés de la V2 dans `tests\NumerotationV2Check\v2`, voir son
`LISEZMOI.md` ; `v2\reprise.sql` y ajoute les tables que lit la reprise du lot 6), une V2 d'origine pour composer les 142 types de `compteurs-d8.json`,
et la base commune du serveur. Les mêmes gestes
sont faits sur la V2 basculée et sur le témoin, et comparés. Il crée un compte SQL d'agent, une session
d'événements et une tâche planifiée, et les supprime à la fin (contrôlé).

La partie « la V3 pour elle-même » (NUM-006 CA01) passe par le vrai serveur, comme en production depuis le
lot 8 : les routes portent le greffe (`POST /numerotation/8002/numeros`, `RoutesCompteur.Pour`) et
demandent la session. Le catalogue de la base commune (`soc.Greffe`) mène le greffe 8002 à la base V3,
l'agent du banc est un compte de cette base V3 (`soc.Compte`), et il se désigne par l'en-tête de
développement `X-RigV3-Compte` (serveur démarré en `Development`). Sans cet en-tête, le banc s'arrête sur
« 401 Unauthorized » (contre-épreuve faite).

<!-- fix-ok: reprise du 2026-11-21 apres relecture du controle final -- le lot 6 est reuni sur main (8251460) : Reprise=oui joue par le banc, la recopie ecrivait FACV3/BDFNI dans la V2 (1 puis 2 lignes mesurees, banc rouge), le saut VB6 est a modGeneral.bas:85 (lu) ; les chiffres (58 OK, 6 RESTE, 14 echecs, 40 s) sont ceux lus dans la sortie de tests.ps1 NumerotationV2. -->
<!-- fix-ok: 2e reprise -- main b3111fe (lot 8 reuni) : le banc postait sur /numerotation/numeros sans greffe ni session, rouge mesure (JsonException apres 35 OK) ; raccorde a /numerotation/{greffe}/… , la ligne « a la reunion du lot 8 » des restes retiree (reunion faite). -->

