# Recopie V2 ↔ V3 — le transfert initial (REC-001) et l'architecture pour les lots suivants

Lot 7 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story : **REC-001** de `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\recopie.json` — transférer chaque
base de l'ancien RIG en une nuit, telle qu'elle est. C'est le « script de transfert des données » de la demande de
départ. Décisions du cadrage qui le fixent : Q2 (dossiers, numéros et historique repris tels quels), Q4 (une base V3
par greffe, posée à côté de sa base V2, sur la même instance), Q15 (la V2 ne s'arrête jamais), C5 (données reprises
d'anciens logiciels, liens cassés), C6 (40 bases, 861 tables, une nuit).

Ce fichier dit **ce que fait le transfert**, **comment le lancer**, **pourquoi il est construit ainsi**, et
**comment les lots suivants s'y branchent** (REC-002 à REC-019). Les lots 8, 10 et 11 y **ajoutent** leurs
sections après « Fichiers », sans réécrire les autres.

## Ce que fait le transfert, dans l'ordre

`MoteurTransfert.TransfererAsync` (`Serveur\Recopie\MoteurTransfert.cs`), une base de greffe à la fois :

1. **Journal ouvert** : une ligne dans `rec.Transfert` (état « en cours »). Un transfert **interrompu** de la même
   base est **repris** (REC-005, lot 8 : voir « Reprendre après un incident ») ; refus si elle a déjà un transfert fini
   ou arrêté sur un écart dans cette base V3, ou si la base V2 n'est pas sur l'instance de la base V3 (Q4 ;
   `num.Reprendre` la lit par son nom).
2. **Instantané daté** de la base V2 (`CREATE DATABASE … AS SNAPSHOT OF`, `Serveur\Recopie\Instantane.cs`) : tout
   est lu dans cet état figé, sans verrou sur la V2. Sa date et la version du suivi des changements lue dedans vont
   au journal (CA05).
3. **Catalogue réel** de la base lu dans l'instantané (`Catalogue.cs` : tables, colonnes, clés primaires, clés
   étrangères désactivées comprises, déclencheurs, lignes), comparé aux règles :
   - une colonne absente ou en plus est **imprimée**, le transfert continue (la copie suit le catalogue réel) ;
   - une **table attendue absente**, une **table sans règle**, une table « telle quelle » sans clé primaire ou d'un
     type non porté, une table `v2.X` déjà présente : **refus avant toute écriture de données** (CA01, T03).
4. Les **reprises de la phase 1** placées avant les tables : réglages (SOC-003), puis comptes et droits (SOC-002).
   Elles sont **appelées, jamais refaites** : le transfert lance leur code, compte et relie ce qu'elles portent.
5. Chaque table **« telle quelle »**, dans l'ordre déduit des clés étrangères (`CopieTelleQuelle.cs`) : **une
   transaction par lot** (lignes, correspondance, point de reprise, REC-005), puis la transaction de fin de table
   (liens cassés, rejets, contrôle de fin, ligne du rapport).
6. Les reprises placées **après** les tables : les compteurs (NUM-005, `num.Reprendre`), lus dans la **V2 vivante**
   au dernier moment, puis `num.ReleverHeritage`.
7. **Fini** : totaux, durée, un événement 210 « reprise de données » dans `soc.EvenementTechnique` ; l'instantané
   est supprimé.

**Arrêt propre** : un écart (comptes, correspondances, valeurs, écart d'une reprise) annule la table en cours, rien
de ce qui suit n'est fait, l'état « arrete » et son motif sont au journal, l'instantané est supprimé. Les tables finies
avant restent, avec leur ligne de rapport. Une **erreur** (incident technique) n'annule plus rien : le transfert est
« interrompu » et se reprend (REC-005). **La base V2 n'est jamais écrite** (CA06).

## Le lancer

```powershell
# La base V3 du greffe, à jour des migrations (REC-001_transfert compris), sur l'instance de la base V2
dotnet run --project Base\RigV3.Migrations.csproj -- --base "<chaîne de RIGV3_<GREFFE>>" --creer

# Le transfert (ici : le SQL Server du poste ; chez Amitel : --exploitation)
dotnet run --project Outils\Transfert\RigV3.Transfert.csproj -- transferer `
    --v2 "Server=…;Database=RIG_<GREFFE>;…" --v3 "Server=…;Database=RIGV3_<GREFFE>;…" [--lot 50000] [--exploitation]

# Les colonnes attendues d'une table, relevées dans une base de référence (lecture seule)
dotnet run --project Outils\Transfert\RigV3.Transfert.csproj -- inventaire --v2 "<chaîne de RIG_DEV>" --tables AFFAIRE,DEMANDE
```

Codes de sortie : **0** fini, **1** arrêté (un écart), **2** refusé avant toute écriture, **3** commande mal
formée ou serveur refusé, **4** interrompu par un incident (la même commande reprend, REC-005). Sans `--exploitation`, l'outil refuse toute base qui n'est pas sur ce poste, **sans s'y
connecter** : un agent qui le lance d'ici n'écrit jamais sur SQL-DEV, SQL-PROD ni un serveur de greffe.

L'outil imprime le journal au fil de l'eau, puis **le rapport de fin de nuit** de la base (REC-001 T04) : par table,
la règle, les lignes lues, écrites, rejetées, les liens cassés gardés et la durée. Tout est aussi en base :

| Table (base V3) | Ce qu'elle tient |
|---|---|
| `rec.Transfert` | un transfert : base et serveur V2, instantané et sa date, version de suivi, état, motif, totaux, durée |
| `rec.TransfertTable` | le rapport de fin de nuit : une ligne par table (ordre, règle, story, table V3, lues, écrites, rejetées, liens cassés, correspondances, octets, durée, détail) |
| `rec.TransfertEvenement` | tout ce que le transfert a imprimé (schéma, règles, reprises, refus, arrêt), dans l'ordre |
| `rec.Correspondance` | chaque ligne écrite : base V2, table V2, identifiant V2, table V3, identifiant V3, transfert, date — indexée dans les deux sens (T01) |
| `rec.Rejet` | chaque ligne lue et non écrite : table, identifiant, motif, règle appliquée (CA03, CA04) |
| `rec.LienCasse` | chaque lien gardé tel quel vers une ligne absente : clé de la V2, colonnes, valeur, table visée, cause (CA04) |
| schéma `v2` | les tables de la V2 recopiées telles quelles (`v2.AFFAIRE`, `v2.DEMANDE`…) |

Identifiant d'une ligne, en texte : sa clé primaire (colonnes jointes par `|`, date en ISO 8601, binaire en
hexadécimal). Pour une table telle quelle, l'identifiant V3 est le même que le V2 (`v2.AFFAIRE.AFFRE_ID_AFFRE`
gardé) ; pour une table portée par une reprise, c'est celui de la table V3 (`UTILISATEUR` 101 → `soc.Compte` 7).

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **Le catalogue de chaque base**, jamais le projet de base de données de la V2 | Deux bases du même logiciel n'ont pas le même schéma (RIG_DEV 861 tables / 789 clés ; RIG_RECETTE 838 / 777), et `AFFAIRE.sql` du projet n'a pas la colonne client que RIG_DEV porte avec sa clé. Écart code ↔ métier de la story, règle retenue « à valider ». |
| **Instantané de base** SQL Server, pas l'isolement par instantané | RIG_DEV a `ALLOW_SNAPSHOT_ISOLATION OFF` (lu le 2026-11-21) ; seul `READ_COMMITTED_SNAPSHOT` est actif, qui fige une requête et non toute une nuit de lecture. L'activer changerait un réglage de chaque base V2. L'instantané fige toute la base à une date, sans verrou, en lecture seule par nature (une écriture y est refusée par SQL Server) ; toutes éditions depuis 2016 SP1. Prouvé sur le poste : une ligne écrite après n'y est pas, `CHANGETABLE` depuis sa version la rend. |
| Les **compteurs** lus dans la **V2 vivante**, en dernier | La V2 donne des numéros toute la nuit : `num.Reprendre` (NUM-005) les prend sous blocage à leur dernière valeur (833 ms de blocage mesurés sur les 26 553 lignes de RIG_DEV). Le basculement des trois procédures de la V2 dans la même transaction est le geste du passage (lot 13, SOC-024). |
| **Une règle par table**, déclarée par sa story et **trouvée seule** | Comme les routes (`IModuleServeur`) : un lot ajoute sa classe, aucun fichier commun. Une table déclarée deux fois arrête le transfert en nommant les deux classes. Une table lue sans règle : refus (T03). |
| **« Telle quelle »** = règle d'attente | Tant que la story du domaine n'a pas écrit sa règle colonne par colonne, la table est recopiée dans le schéma `v2` : mêmes colonnes, types, classements, identités, mêmes valeurs, même clé primaire. La V3 a ainsi **toutes** ses données dès le matin, et les domaines les reprennent de là. |
| Ni **clés étrangères** ni **déclencheurs** sur `v2.*` | Un lien cassé hérité (C5) doit être gardé tel quel : une clé posée le refuserait. Les clés de la V2 sont **vérifiées ligne à ligne** par le transfert (désactivées comprises) ; les déclencheurs sont relevés au journal, leur comportement se refait dans les stories de domaine. |
| **Lien cassé : marquer** par défaut, **rejeter** si la règle le dit | « Il ne s'arrête pas et n'invente aucune valeur » (CA04). Marquer garde la ligne et sa valeur et note le lien dans `rec.LienCasse` ; rejeter la range dans `rec.Rejet`. Une ligne qui vise une ligne **rejetée** est écrite et son lien marqué « rejetée par le transfert », jamais rejetée en cascade. Défaut « marquer » et absence de cascade : **à valider** par le référent de chaque domaine. |
| **Contrôle de fin de table** dans sa transaction | lues = écrites + rejetées ; une correspondance par ligne écrite ; même empreinte des valeurs des deux côtés (`CHECKSUM_AGG(BINARY_CHECKSUM(…))` sur l'instantané et sur `v2.X`). Un écart annule la table. Limite mesurée : `BINARY_CHECKSUM` voit les **valeurs**, pas les types (`'ABC123'` donne 72745491 en varchar comme en nvarchar) ; les types viennent du catalogue, et le banc recrée les tables de RIG_DEV pour vérifier qu'ils sont les mêmes. |
| **Classement** posé sur les seuls types texte | RIG_DEV porte un classement sur **538 colonnes non texte** (bit, datetime…, héritage de l'ancien schéma) qu'un `COLLATE` refuse (erreur 447). Mesuré par le banc. |
| `timestamp` → `binary(8)`, colonne calculée → sa valeur | La valeur reste telle quelle ; un horodatage de ligne n'a de sens que dans la V2. Types d'assemblage (geography, hierarchyid, CLR) : refus avant écriture, aucune des 10 tables n'en a. |
| Lecture par **`LectureV2`** | La seule porte de la V3 vers une base V2 : un SELECT seul. Le seul geste du transfert sur le serveur V2 est de créer puis supprimer l'instantané. |
| **Un événement 210** à la fin | Comme la reprise des droits (SOC-005 CA03) : la V2 note « reprise de données d'autres logiciels » (code 210) ; un seul pour toute la base. |

## Mesures (banc, SQL Server 2022 du poste, Docker)

Base V2 jetable tirée de RIG_DEV : **97 941 lignes, 26 tables, 29 clés étrangères vérifiées, 12 déclencheurs**,
construite en 3 à 5 s. Transfert complet (reprises comprises) : **4,1 à 4,7 s** ; 57 397 lignes telles quelles,
7,5 Mo écrits, **3,8 à 4,2 Mo/s, 29 000 à 32 000 lignes/s, contrôle de fin compris**.

Extrapolation à la plus grosse base **connue de ce poste**, RIG_DEV (62,5 Go au catalogue, 129,7 millions de
lignes, 860 tables) : **4 h 16 à 4 h 44 au volume, 1 h 07 à 1 h 14 aux lignes** — dans la fenêtre de 10 h
(20 h → 6 h, proposée, à valider). Méthode : durée fixe du passage (instantané, catalogue, reprises) + volume de la
base ÷ débit mesuré des tables ; on retient le plus long des deux. Le volume compte les index de la V2, que la copie ne
refait pas : l'estimation est prudente. Elle vient d'un poste de développement : **ce n'est pas CA02**.

## La procédure de nuit (REC-001 T04, proposée, à valider)

- **Fenêtre** : de la fermeture du soir à l'ouverture du lendemain, **20 h → 6 h** heure du greffe (fuseau du
  catalogue des greffes, SOC-007 ; les 6 outre-mer ont la leur).
- **Ordre** : les 6 bases d'outre-mer sur leurs serveurs (`RIGBD-ANTILLES`, `RIGBD-POLYNESIE`, `RIGBD-REUNION`) en
  même temps que les 34 de `SQL-PROD\PROD` ; sur `SQL-PROD`, les plus grosses d'abord (la mesure des 40 tailles est
  REC-008), plusieurs bases à la fois tant que le serveur le tient (à mesurer, REC-008). Liste des 40 :
  `refonte-rig\controles\bases-greffes.json`.
- **Par base** : migrations sur la base V3 vide, puis `RigV3.Transfert transferer … --exploitation` ; code 0 = la base
  ouvre le matin ; code 1 ou 2 = elle reste en V2 (Q14 : aucun greffe ne s'arrête), le rapport dit pourquoi.
- **Rapport de fin de nuit** : la sortie de l'outil et `rec.TransfertTable` (par table : lues, écrites, rejetées,
  liens cassés, durée) ; `rec.Rejet` et `rec.LienCasse` vont au référent du domaine de chaque table.
- **Avant** : ouvrir les chaînes signées de la V3 (`ServiceChaines.OuvrirAsync`, SOC-018 — **avant** les reprises,
  décision du lot 5) ; **après** : basculer les trois procédures de numérotation de la V2 (lot 13, SOC-024).

## Pour les lots suivants

### Ajouter ou changer la règle d'une table

Une table V2 entre dans le transfert **par une règle déclarée dans `Serveur\Recopie`**, et par elle seule :

- **telle quelle** : une ligne `RegleTable.TelleQuelle("TABLE", "STORY", colonnes…)` dans une classe
  `IReglesTransfert` (modèle : `ReglesRec001.cs`). Colonnes : `RigV3.Transfert inventaire --tables TABLE` sur RIG_DEV ;
- **colonne par colonne** (REC-012 à REC-019, stories de domaine) : la story **remplace** la ligne « telle quelle » de
  sa table (elle ne s'y ajoute pas : deux déclarations arrêtent le transfert), par une règle qui écrit sa table V3 et
  sa correspondance. Le moteur sait aujourd'hui deux sortes de règles, « telle quelle » et « reprise » ; une règle
  colonne par colonne s'écrit comme une **reprise** (`IRepriseTransfert`, modèle : `ReprisesPhase1.cs`) tant qu'elle
  porte des tables entières, ou comme une sorte nouvelle du moteur si elle doit s'intercaler dans l'ordre des clés ;
- **lien cassé** : `SiLienCasse = Rejeter` sur la règle, si le référent le décide ;
- **table facultative** (`Facultative: true`) : seulement si le code qui la lit sait qu'elle peut manquer.

Une **reprise** (`IRepriseTransfert`) : `Nom`, `Story`, `Rang`, `ApresLesTables`, ses `Regles` (les tables qu'elle
**porte** ; une table qu'elle lit seulement garde sa propre règle, comme CATEGORIE pour les droits) et `ExecuterAsync`.
Elle rend « déjà faite » si la base V3 l'a déjà (elle n'est pas rejouée), ses écarts (le transfert s'arrête), et ses
tables **une ligne pour une ligne** (`TablePortee` : le moteur écrit leur correspondance et exige lues = écrites). Les
reprises des courriels (SOC-006, `RepriseCourriels`) et des préférences (SOC-013, `ReprisePreferences`) n'y sont pas
encore : leurs tables (`EMAIL_MODEL`…, `PARAMETRE_UTILISATEUR`) seront refusées « sans règle » tant qu'elles n'auront
pas leur classe.

### Où se branchent REC-002 à REC-019

| Lot / story | Ce qu'il trouve ici |
|---|---|
| **REC-003** (lot 9) remonter les écritures de la V2 | `rec.Transfert.VersionSuivi` : la version du suivi des changements lue **dans** l'instantané. `CHANGETABLE(CHANGES <table>, VersionSuivi)` rend exactement ce que le transfert n'a pas pris (prouvé par le banc, CA05). Sans suivi activé sur la base V2 (REC-003 T01), la colonne est vide : l'activer **avant** le transfert. |
| **REC-002 / REC-004** (lot 9) recopier vers la V2 | `rec.Correspondance`, index `(TableV3, IdV3)` : de la ligne V3 à sa ligne V2. |
| **REC-005** (lot 8) reprendre après un incident | Fait : section « Reprendre après un incident (REC-005) » ci-dessous. |
| **REC-006** (lot 8) comparer chaque nuit | Fait : section « Comparer chaque nuit (REC-006) » ci-dessous. |
| **REC-008** (lot 8) répéter sur les copies anonymisées | Fait : section « Répéter le transfert (REC-008) » ci-dessous. |
| **REC-009** (lot 11) anciens logiciels | `MIGRATION_ID`, `CORRESP_*` : une règle chacune ; la règle « déjà repris = marqué » de 2017 rejoint `rec.Rejet` / `rec.LienCasse`. |
| **REC-010 à REC-019** fiches partagées | REC-012 à REC-017 et REC-019 : faites au lot 10, chaque fiche dans sa table V3 (`Serveur\Recopie\FichesV3.cs`, section « Les fiches partagées » plus bas) ; REC-010, REC-011, REC-018 (historique, demandes liées, arrivées) : lot 11, leur table est encore « telle quelle » dans `ReglesRec001.cs`. |

## Écarts code ↔ métier et choix à valider

| Sujet | Codé comme | Pourquoi / qui tranche |
|---|---|---|
| Schéma de référence | le catalogue de chaque base (story, « à valider ») | `AFFAIRE.sql` n'a pas la colonne client de RIG_DEV |
| Lien cassé | marquer, valeur gardée ; enfant d'un rejeté marqué, pas rejeté | « n'invente aucune valeur » ; référent de chaque domaine |
| `v2.*` sans clés ni déclencheurs | liens vérifiés par le transfert, déclencheurs relevés | garder les liens cassés hérités (C5) |
| Instantané | droit `CREATE DATABASE` sur le serveur de la V2, fichiers `.ss` à côté de la base | exploitation Amitel |
| Fenêtre de nuit | 20 h → 6 h | dossier d'exploitation, à valider |
| Événement 210 à la fin | un seul par base | comme SOC-005 CA03 ; référent Socle |

## Ce qui ne se fait pas depuis ce poste, et pourquoi

| Critère | Ce qui manque ici | Où |
|---|---|---|
| CA02 sur la copie anonymisée de la **plus grosse** des 40 bases, serveur de la taille de la production | les bases des greffes ne se lisent pas d'ici sans accord ; la copie anonymisée attend la nouvelle réception du lot 6 ; pas de serveur de production | REC-008, lot 8 |
| CA05 « la recopie continue rejoue » | le lecteur de changements | REC-003, lot 9 (le point de départ exact est prouvé ici) |
| Transférer une base **entière** | RIG_DEV a **833 tables sans règle** : le moteur refuse (T03) tant que leur story n'a pas déclaré la sienne ; une **répétition** les porte par des règles provisoires (REC-008, lot 8), pas un vrai passage | stories de domaine |
| Écran du transfert (« à maquetter ») | l'exploitant d'Amitel lance l'outil ; aucun écran n'est dans les fichiers de ce lot | à décider |

## Fichiers

| Fichier | Rôle |
|---|---|
| `Base\migrations\REC-001_transfert.sql` | schéma `v2`, `rec.Transfert`, `rec.TransfertTable`, `rec.TransfertEvenement`, `rec.Correspondance`, `rec.Rejet`, `rec.LienCasse` |
| `Serveur\Recopie\MoteurTransfert.cs` | le moteur, l'ordre d'écriture, le rapport |
| `Serveur\Recopie\Instantane.cs` | l'état figé daté de la base V2 |
| `Serveur\Recopie\Catalogue.cs` | le schéma réel de la base V2, les écritures SQL des noms et identifiants |
| `Serveur\Recopie\CopieTelleQuelle.cs` | la copie d'une table, ses liens cassés, sa correspondance, son contrôle de fin |
| `Serveur\Recopie\JournalTransfert.cs` | le journal et l'événement 210 |
| `Serveur\Recopie\Regles.cs` | règles, reprises, découverte et refus des doublons |
| `Serveur\Recopie\ReglesRec001.cs` | les tables telles quelles de REC-001 (9 au lot 1 ; AFFAIRE, DEMANDE, ARRIVEE depuis le lot 10, les autres dans `FichesV3.cs`) |
| `Serveur\Recopie\ReprisesPhase1.cs` | réglages, comptes et droits, compteurs : appelés, pas refaits |
| `Outils\Transfert\` (`RigV3.Transfert`) | l'outil de l'exploitant : `transferer`, `inventaire` |
| `tests\RecopieTransfertCheck\` | le banc : base V2 jetable tirée de RIG_DEV (`ExtraitRigDev.cs`), un contrôle par critère |

Preuve : `.\tests.ps1 RecopieTransfert` (il lui faut `docker compose up -d` et la lecture de RIG_DEV ; autre serveur :
`RIGV3_RIG_DEV`). Contre-épreuves faites : une valeur changée dans la V3 rougit l'oracle ; une valeur écrite dans la
V2 change son empreinte ; une date arrondie en silence dans la V3 arrête le transfert ; le classement remis sur les
colonnes non texte et le lien vers une mère rejetée non marqué rougissent le banc.

## Reprendre après un incident (REC-005) — lot 8

Story **REC-005** : relancer le transfert après une panne (serveur redémarré, base indisponible, erreur sur une ligne)
et qu'il reparte de lui-même du dernier point sûr, sans perte ni doublon, sans intervention à la main dans les bases.

### Ce que fait le transfert maintenant

- **Par lots** (`CopieTelleQuelle.CopierAsync`, T03) : chaque lot — les lignes suivantes dans l'ordre de la clé
  primaire, leur correspondance et **sa ligne `rec.TransfertLot`** (n° de lot, passage, lignes écrites, dernière clé)
  — est **une transaction**. La ligne de lot existe si et seulement si ses lignes sont dans la base V3 (T01).
- **Verrou de session** (`sp_getapplock`, connexion hors du pool) : un seul transfert d'une base à la fois ; un
  processus tué le rend (SQL Server ferme sa session).
- **Relancer = reprendre** : la même commande `RigV3.Transfert transferer` trouve le dernier transfert de la base
  « interrompu », ou « en cours » sans processus derrière (verrou libre : arrêt brutal), et le **reprend** : même
  identifiant (`rec.Correspondance`, `rec.Rejet`, `rec.LienCasse` lui appartiennent), même **instantané** de la V2
  (gardé à l'interruption : la reprise lit exactement le même état figé et la même version de suivi), une reprise de
  plus (`rec.Transfert.Reprises`). Les tables finies (ligne de rapport) ne sont pas refaites ; la table entamée repart
  **après la dernière clé du dernier lot enregistré** ; les reprises de la phase 1 déjà rapportées ne sont pas rappelées.
- **Le point de reprise est vérifié** avant de repartir : nombre de lignes et dernière clé de la table V3 contre la
  ligne du lot. Incohérent (un lot effacé, une table touchée à la main) : **écart**, le transfert s'arrête au lieu de
  doubler ou de sauter des lignes.
- **Écart ≠ incident.** Un écart (valeurs, comptes, point incohérent) arrête le transfert (« arrete », code 1), annule
  la table entamée et supprime l'instantané : il ne se reprend pas (corriger, repartir d'une base V3 neuve). Toute
  autre erreur, une annulation ou un arrêt brutal : « interrompu » (code 4), ce qui est enregistré reste, motif et
  point de reprise au journal (`rec.Transfert.Motif`, `PointDeReprise`).
- **Rapport du matin** (CA04) : `RigV3.Transfert rapport --v3 <chaîne> | --liste <fichier>` dit pour chaque base
  « TERMINE » ou « INTERROMPU » avec son point de reprise (table, lot, lignes, dernière clé) ; code 0 si toutes sont
  terminées.
- **Alerte** (CA05) : `Serveur\Recopie\AlertesRecopie.cs`, lue par le surveillant de la supervision (SOC-011) à
  chaque passage : un transfert interrompu (ou mort en route) depuis plus de **5 minutes** (`Recopie:DelaiIncidentMinutes`,
  seuil proposé) ouvre l'alerte « recopie-incident » qui nomme la base, la table et l'erreur ; elle se ferme quand le
  transfert est repris et fini.

### Les choix et leur raison

| Choix | Raison |
|---|---|
| Point de reprise = **le lot**, dans la transaction de ses lignes | L'outil de 2017 notait « succès ou échec élément par élément » (`ImportFactureCompte.cs`) et ne reprenait que ce qui n'avait pas de numéro de destination (`MigrationFactureBulk.cs`) : marquer ce qui est fait rend la relance sans doublon. Ici la marque est la ligne `rec.TransfertLot`, écrite avec les lignes : impossible d'avoir l'une sans les autres. Rien n'est écrit dans la V2. |
| Repartir **après la dernière clé** (lecture par clé, paramètres du type exact) | Un `OFFSET` relirait tout ; un `nvarchar` comparé à un `varchar` sous un classement SQL_ ne trie pas comme lui : la lecture sauterait ou relirait des lignes. Les paramètres ont le type, la taille et la précision de la colonne (`Sql.Parametre`). |
| **Garder l'instantané** d'un transfert interrompu | Une reprise sur un nouvel état figé mêlerait deux dates de la V2 et la version de suivi (REC-003) mentirait. L'instantané survit au redémarrage de SQL Server ; disparu, la reprise est refusée (écart). Il occupe la place des pages que la V2 modifie entre-temps : à reprendre dans la nuit. |
| Verrou applicatif de **session**, hors pool | Seul moyen sûr de savoir qu'un « en cours » n'a plus personne derrière : la session meurt avec le processus. |
| Tables **sans clé primaire** (répétition seulement) | Une ligne ne s'y identifie pas : ni correspondance ni lot. Une règle provisoire la copie en une transaction (annulée entière par un incident, recopiée entière à la reprise). Un vrai passage les refuse toujours (T03). |

### Écarts et choix à valider

| Sujet | Codé comme | Qui tranche |
|---|---|---|
| Seuil d'alerte | 5 minutes (`Recopie:DelaiIncidentMinutes`) | exploitation Amitel (valeur proposée par la story) |
| Taille d'un lot | 50 000 lignes (`--lot`) : un lot perdu au plus par incident | exploitation, après REC-008 sur la plus grosse base |
| Instantané gardé | jusqu'à la reprise ; place disque sur le serveur de la V2 | exploitation Amitel |

### Ce qui ne se fait pas encore

| Critère | Ce qui manque | Où |
|---|---|---|
| CA01 à CA03 sur la **recopie continue** (changement rejouable, 3 essais puis mise de côté avec son message, les autres fiches continuent, la même fiche attend, relance unitaire dans l'ordre) | fait au lot 9 : section « Un changement refusé : mis de côté, relancé » plus bas, contrôles REC-005 de `tests\RecopieDeuxSensCheck` | lot 9 (fait) |
| CA05, second cas (alerte d'un changement mis de côté) | fait au lot 9 : `AlertesRecopie` (règle « recopie-incident »), contrôle « REC-005 CA05 (mis de cote) » de `tests\RecopieDeuxSensCheck` | lot 9 (fait) |
| Fiche d'incident pour l'exploitation (T04) | la lecture et la relance des changements mis de côté : section « Un changement refusé : mis de côté, relancé » plus bas | lot 9 (fait) |

## Comparer chaque nuit (REC-006) — lot 8

`ComparaisonBases.ComparerAsync` (`Serveur\Recopie\Comparaison.cs`), une base de greffe à la fois :

1. **Purge** des rapports de plus de **90 jours** (CA04, `Recopie:ConservationRapportsJours`), détails compris.
2. **Deux états figés** (instantanés de base, la V3 puis la V2) : la lecture ne pose aucun verrou sur les tables
   vivantes ; les agents écrivent pendant le contrôle (CA05).
3. Pour chaque **table partagée** (REC-006 `tables_v2` : ACTION_RCS, ADRESSE_BASE, ARRIVEE, CATEGORIE, CLIENT,
   DOSSIER_RCS, INSCRIPTION, UTILISATEUR, ENTREPRISE, AFFAIRE, DEMANDE), **par la table de correspondance** (CA01) :
   le nombre de lignes de chaque côté et une **empreinte SHA-256 de chaque ligne** (texte de ses colonnes, calculé par
   SQL Server de la même façon des deux côtés : date ISO, binaire et horodatage en hexadécimal, flottant à 17 chiffres,
   NULL distinct de toute valeur), lues dans l'ordre binaire de l'identifiant V2 et fusionnées. Chaque ligne est
   comparée, en écart, sans correspondance (une ligne **rejetée** par le transfert est comptée à part), ou
   correspondance sans sa ligne. Les lignes V3 sans correspondance sont comptées aussi.
4. Chaque **écart** est détaillé (CA02) : les deux lignes relues colonne par colonne, **les colonnes qui diffèrent**.
   Puis il est **relu dans les deux bases vivantes** : disparu, c'était une recopie en cours entre les deux états figés
   (« résorbé », compté, non alerté).
5. Les **liens orphelins** (CA03) : une demande ou une affaire de la V3 qui vise une fiche partagée sans
   correspondance (hors liens cassés hérités, déjà dans `rec.LienCasse`). Le lien est lu en **identifiant de la V3** de
   la fiche visée (la remontée le traduit, une ligne de la V3 le porte ; une ligne transférée porte celui de la V2, égal),
   sauf une colonne `NumerosV2` (type, agent : numéro de la V2 dans une ligne qui est dans la V2). Une ligne de la V3 qui
   vise une ligne que la V3 tient seule par règle (affaire ou demande de la V3, type de la V3 seule) n'est pas orpheline ;
   une ligne qui est dans la V2 doit viser une fiche de la V2 (lot 11).
6. Le **rapport** dans la base V3 : `rec.Comparaison` (états figés, durée, totaux), `rec.ComparaisonTable`,
   `rec.ComparaisonEcart` (genre, table, deux identifiants, colonnes) — **aucune valeur** ni donnée personnelle.

**La nuit** (T04) : `ComparaisonNocturne` (ITraitementDeFond + ITachePlanifiee « recopie-comparaison », inscrits par
`ModuleRecopieNuit`) est planifiée par le planificateur des clôtures NF525, sans second planificateur : due pour un
greffe dont la base V3 est créée et a un transfert fini, entre **02:00** (`Recopie:ComparaisonHeure`, après la
sauvegarde, proposé) et **06:00** (`Recopie:OuvertureHeure`) heure du greffe, si aucun contrôle n'a commencé depuis
20 h. **Alerte** (CA04) : `AlertesRecopie`, règle « recopie-ecart » dès que le dernier rapport a un écart, un lien
orphelin ou une ligne sans correspondance (base, tables, comptes) ; elle se ferme au contrôle propre suivant.

**À la main** : `dotnet run --project Outils\Comparaison -- comparer --v2 <chaîne> --v3 <chaîne> [--tables A,B | --toutes]`
(code 0 rien, 5 au moins un écart, 1 contrôle arrêté ; sans `--exploitation`, ce poste seulement).

| Choix | Raison |
|---|---|
| Empreinte **par ligne** (SHA-256), pas `CHECKSUM_AGG` | Le transfert compare une empreinte de table (REC-001) ; ici il faut dire **quelle** ligne diffère. SHA-256 d'un texte défini colonne par colonne : pas de collision pratique, contrairement à `BINARY_CHECKSUM` (32 bits). |
| Fusion de deux lectures triées | 4,5 millions d'arrivées dans RIG_DEV : un dictionnaire en mémoire ne tiendrait pas ; deux curseurs triés, une passe. |
| Relecture des écarts dans les bases vivantes | Les deux instantanés ne sont pas pris au même instant : une fiche recopiée entre les deux serait un faux écart et une fausse alerte chaque nuit. |
| Tables V3 qui ne sont pas la table V2 « telle quelle » | Comparées colonne par colonne par la règle de leur fiche (lot 10 : `rec.*`, UTILISATEUR → `soc.Compte`) ; une table V3 sans règle colonne par colonne : correspondances seulement (T03). |

<!-- fix-ok: phrases fausses relevées par le juge et mesurées le 2026-11-21 par RecopieDeuxSensCheck : le contrôle de nuit voyait 2 faux écarts sur l'état de transmission (exclusion annoncée « à faire »), puis 5 sur l'identifiant et les liens des fiches créées par la recopie (clients, dossiers, action) ; le journal de la remontée écrivait « non noté par la V2 » pour un client dont la V2 note l'agent. -->
**À valider / reste** : l'heure 02:00 et la durée de 90 jours (proposées) ; T03 — une fiche que le lot 9 recopie
(`ReglesRec002`) est comparée sur les colonnes que la recopie recopie (`FichesRecopiees`), et sur elles seules : ni son
identifiant, propre à chaque base et apparié par `rec.Correspondance` (une fiche créée d'un côté reçoit le numéro que
l'autre base lui donne), ni ses colonnes « propres » (état de transmission, `RegleRecopie.ColonnesPropres`) ; un lien
vers une autre fiche recopiée est comparé en identifiant V2, la valeur de la V3 lue par la correspondance comme la
recopie l'écrit. Le détail de la table le dit (lot 9 ; banc `RecopieDeuxSensCheck`, « REC-006 sur les
fiches recopiées ») ; CA05 sur les 40 bases de production (pas sur ce
poste). Mesure du banc : 57 640 lignes de 10 tables en 3,0 s (19 092 lignes/s, relecture comprise), 185 écritures
d'agents pendant le contrôle, aucune bloquée (la plus longue 27 ms) ; extrapolé aux 16,5 millions de lignes des fiches
partagées de RIG_DEV : **14 min 22 s**, dans la fenêtre 02 h → 06 h.

## Répéter le transfert (REC-008) — lot 8

`RigV3.Repetition repeter` (`Outils\Repetition`), base par base :

1. **Anonymisation** (CA01, CA05) par l'outil de SOC-021 : la source est **lue** (SELECT seul) et la copie écrite
   **déjà anonymisée** sur ce poste — aucune copie en clair n'existe ; puis son contrôle (un échantillon de 1 000
   valeurs personnelles de la source cherchées dans la copie, volumes, clés et numéros gardés) doit être vert.
2. **Garde** : le transfert ne lit qu'une copie **marquée** anonymisée (`anonymisation.Copie`) ; une copie en clair est
   refusée avant toute lecture, et le **journal de préparation** le dit (CA05, T09).
3. **Transfert** dans une base V3 neuve, avec des **règles provisoires** pour les tables sans règle (décision du chef,
   lots 7 et 8 : `ReglesProvisoires.Completer`, « telle quelle », marquées « provisoire » au rapport, comptées dans
   `rec.Transfert.Provisoires`). Jamais par défaut : `--provisoires` de `RigV3.Transfert`, ou la répétition.
4. **Contrôle des deux bases** (REC-006).
5. **Rapport** `repetition-<date>.json` et `.md` (CA02) : par base, la durée du transfert, les rejets par table et
   règle (avec un exemple de motif), le retard maximal de recopie, les écarts du contrôle, les durées de chaque étape.

**Le feu vert** (CA03, CA04) : `RigV3.Repetition feu --rapports <dossier> [--jour-j AAAA-MM-JJ]` (code 0 vert, 4 rouge)
juge la dernière répétition : vert **seulement** si les **40 bases** sont allées au bout sur des copies **complètes**
(une copie réduite par `--max-lignes` mesure le schéma entier, pas la durée), tiennent dans la **fenêtre de
nuit** (10 h ; chacune, et toutes au parallélisme dit), si **chaque rejet a sa règle de reprise écrite**
(`RegleTable.RepriseDesRejets` de sa table), si **aucune table n'est passée par une règle provisoire**, si le
contrôle ne trouve **aucun écart**, si le **retard de recopie est mesuré**, et — jour J fixé — si la dernière
répétition **complète** a **moins de 30 jours**.

```powershell
# Une base de greffe (chez Amitel, accord donné pour CETTE base) ; plusieurs : --sources <fichier, une chaîne par ligne>
dotnet run --project Outils\Repetition -- repeter --source "Server=…;Database=RIG_<GREFFE>;…" --accord RIG_<GREFFE> `
    --rapport "<dossier des rapports>" [--max-lignes N] [--echantillon 1000] [--lot 50000] [--garder]
dotnet run --project Outils\Repetition -- feu --rapports "<dossier des rapports>" --jour-j 2027-06-01
```

| Choix | Raison |
|---|---|
| Une règle provisoire est **rouge** au feu | Elle n'est pas une règle de reprise écrite : le jour J ne part pas sur 833 tables que personne n'a validées. Elle sert à mesurer une base entière avant les stories de domaine. |
| Retard de recopie « non mesuré » = **rouge** | CA02 exige la mesure ; un feu vert sans elle serait un faux vert. |
| Copies et bases V3 de la répétition **supprimées** à la fin | Elles ne servent qu'à mesurer (C17) ; `--garder` pour les inspecter. |

**Copie réduite** (`--max-lignes N`) : toutes les tables, au plus N lignes chacune. Son contrôle se juge sur la
réduction (lot 6 : « volumes et lignes gardées diffèrent par construction ») : aucune valeur réelle retrouvée, colonnes
à vider vidées, marque présente, aucun prénom dans une colonne gardée, et chaque volume égal à min(source, N) ; les
numéros gardés ne sont pas jugés, et l'absence de fuite se prouve sur une copie complète (le banc le fait).

**Une base entière, mesurée** (décision du chef, lots 7 et 8 ; RIG_DEV en lecture, 2026-11-21, copie réduite à
200 lignes par table, rapport `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\repetitions\repetition-20261121-105245.md`) :

| Étape | Résultat |
|---|---|
| Copie anonymisée | 860 tables, 1 057 colonnes traitées, 0 anomalie, **131,9 s** |
| Contrôle de la copie | vert sur la réduction : 36 813 valeurs et 109 286 mots tirés de RIG_DEV, **aucun retrouvé**, 396 volumes réduits comme demandé, **222 s** |
| Transfert | le schéma entier est **accepté** : **835 règles provisoires** (dont les 104 tables sans clé primaire de RIG_DEV, copiées en une transaction) — le refus « table sans règle » (T03) ne bloque plus une répétition |
| Arrêt | « interrompu » dans la reprise des **comptes et droits** (SOC-002) : `FK_RoleFonction_Fonction` — les 200 premières lignes de chaque table ne sont pas **fermées sur les clés** (un rôle vise une fonction non copiée) |

Le chronomètre d'une base **entière** demande donc soit une **copie complète** (RIG_DEV : 62,5 Go, un serveur dimensionné
chez Amitel), soit une copie réduite **fermée sur ses clés étrangères** (comme l'extrait du banc, `ExtraitRigDev`). La
seconde est faite au lot 18 : voir « Une base entière sur une copie réduite fermée » ci-dessous.

**Ce qui ne se fait pas d'ici** : les 40 bases des greffes (lecture sur accord de chaque base, chez Amitel, l'anonymisation
sur le serveur du greffe : CA05) ; le **retard de recopie** (la recopie continue, lot 9, et le simulateur d'une journée
d'écritures, REC-008 T04, manquent) ; la planification des répétitions avant la bêta (T03, exploitation).

### Une base entière sur une copie réduite fermée (REC-008) — lot 18

La copie réduite est maintenant **fermée sur ses clés étrangères** (`Outils\Anonymisation\Fermeture.cs`, mode d'emploi dans
`docs\reconstruction\socle-anonymisation.md`, « La copie réduite, fermée sur ses clés ») : les N premières lignes de chaque
table, puis toutes les lignes parentes qu'elles visent, de proche en proche ; ses clés sont posées **avec** vérification.
Répétition jouée sur RIG_DEV (SQL-DEV, lecture seule, accord « RIG_DEV »), copie et base V3 sur le SQL Docker du poste,
supprimées à la fin :

```powershell
RigV3.Repetition repeter --source "Server=SQL-DEV\DEV;Database=RIG_DEV;Integrated Security=True;…" --accord RIG_DEV `
    --rapport C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\repetitions --max-lignes 200
```

Rapport : `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\repetitions\repetition-20261122-003351.md` (et `.json`) ;
rapport de la copie : `RIGDEV-anonymisation.md` à côté. Code de sortie **0** : la base est allée au bout, 17 min en tout.

| Étape | Chronomètre | Résultat |
|---|---|---|
| Copie anonymisée, réduite à 200 lignes par table | **282,8 s**, dont fermeture **180,3 s** | 860 tables, 1 057 colonnes traitées, 0 anomalie ; 109 342 premières lignes, puis **109 025 lignes parentes dans 112 tables, en 7 tours** (les plus grosses : `ADRESSE_BASE` 14 777, `ENTREPRISE` 10 317, `DOSSIER_RCS` 9 641, `AFFAIRE` 8 916 lignes au total) ; les **789 clés étrangères posées avec vérification**, 0 sans (relu dans `sys.foreign_keys` de la copie : 0 non vérifiée) ; 2,0 Go, dont 531 Mo de documents de `GED_RIG` (le plus gros : 510 Mo) |
| Contrôle de la copie | **559,7 s** | **vert** : 1 057 colonnes personnelles, 308 653 valeurs et 881 565 mots tirés de RIG_DEV, **aucun retrouvé** ; 396 volumes conformes à la réduction, dont 112 complétés par la fermeture (min(source, 200) + les lignes ajoutées, pas une de plus). Plus long qu'au lot 8 (222 s) : la comparaison des colonnes gardées relit en entier les petites tables à documents (`DOCUMENT_OCERISE` : 551 lignes, 2,2 Go, 2 min à elle seule) |
| Transfert (règles provisoires du lot 8) | **150,8 s** | **fini** : 861 tables, dont **822 par une règle provisoire** ; 198 613 lignes écrites, 1,14 Go, **0 rejet, 0 lien cassé**. Les reprises passent toutes, dont celle des **comptes et droits (SOC-002)** qui arrêtait le lot 8 : 223 comptes, 5 services, 43 rôles, 412 fonctions, 253 droits. Les trois tables à documents font la moitié du temps : `GED_RIG` 546 Mo en 43,5 s, `DOCUMENT_OCERISE` 191 Mo en 19,8 s, `INPI_REVERSEMENTS` 140 Mo en 13,8 s (10 à 12,5 Mo/s) ; les 858 autres : 73,6 s (0,09 s par table) |
| Contrôle des deux bases (REC-006) | **6,3 s** | **fini** : 67 362 lignes comparées sur les 11 fiches partagées, **0 écart, 0 lien orphelin, 0 ligne sans correspondance** |
| Feu vert (`RigV3.Repetition feu`) | — | **rouge**, comme prévu : copie réduite, 822 tables en règle provisoire, retard de recopie non mesuré ; tenus : la base au bout, la fenêtre de nuit, aucun rejet sans règle, aucun écart |

**Ce que la répétition prouve** : le transfert passe sur **les 860 tables réelles de RIG_DEV** (schéma entier, avec des
données dont tous les liens tiennent) : aucune reprise ne s'arrête — ni SOC-002, ni les compteurs (NUM-005, NUM-013,
NUM-019), ni les réglages (SOC-003, SOC-004), ni les fiches partagées (REC-012 à REC-019) —, rien n'est rejeté, et le
contrôle des deux bases ne trouve aucun écart. L'arrêt du lot 8 venait bien de la copie (les N premières lignes ne
tenaient pas ensemble), pas d'une reprise : aucun défaut de reprise n'est apparu, aucun n'est donc à corriger ici.

**Ce qu'elle ne prouve pas** : la durée d'une base complète (copie réduite : le feu vert la refuse) ; l'absence de fuite
(dans une copie réduite, la plupart des valeurs tirées n'y sont pas : le banc la prouve sur une copie complète de son
extrait) ; le retard de recopie (toujours « non mesuré »).

**Extrapolation au volume réel** (estimation d'un poste de développement, **pas CA02**). RIG_DEV, catalogue lu le
2026-11-22 : 64,0 Go, dont **47,9 Go de données** (tables sans leurs index non groupés, que le transfert ne recopie pas),
dont **20,0 Go de grandes valeurs** (documents, textes longs) ; 129,7 millions de lignes. Méthode : le coût fixe par table
mesuré ici, plus le volume divisé par le débit mesuré de son genre ; on garde le plus long du volume et des lignes.

| Étape | Mesure qui sert | Base entière (RIG_DEV) |
|---|---|---|
| Transfert : coût fixe | 0,09 s par table (858 tables, 73,6 s) | ≈ 1 min 30 |
| Transfert : grandes valeurs | 10 à 12,5 Mo/s (`GED_RIG`, `DOCUMENT_OCERISE`, `INPI_REVERSEMENTS`, ci-dessus) | 20,0 Go → 27 à 33 min |
| Transfert : lignes | 3,8 à 4,2 Mo/s ou 29 000 à 32 000 lignes/s (banc du lot 7, « Mesures ») | 27,8 Go → **1 h 50 à 2 h 02** (aux lignes : 1 h 08 à 1 h 15) |
| **Transfert, total** | | **≈ 2 h 20 à 2 h 40** — dans la fenêtre de nuit de 10 h (le lot 7 estimait 4 h 16 à 4 h 44 en comptant les index) |
| Contrôle des deux bases | 10 700 lignes/s (67 362 en 6,3 s, coût fixe compris) | 16,5 millions de lignes dans les 11 fiches partagées → ≈ 26 min au plus |
| Copie anonymisée complète | 6,7 à 7,1 Mo/s (fermeture : 1,2 Go en 180 s ; copie : 2,0 Go en 283 s) | 47,9 Go → ≈ 2 h, **sans** la reconstruction des index ni le calcul des valeurs fictives sur les textes longs, non mesurés à ce volume |
| Contrôle de la copie | par tirage : peu sensible au volume | ≈ 10 min |

La nuit du passage (transfert puis contrôle) tiendrait donc en **3 h environ** ; une répétition complète (copie
anonymisée comprise) en 5 h environ. Une copie complète reste à faire chez Amitel (serveur dimensionné) pour remplacer
cette estimation par une mesure.

**Ce que la répétition a fait corriger en route** (outil de copie, `Outils\Anonymisation\Copie.cs`, `EcrireAsync`) : la
lecture des lignes est **synchrone** — en asynchrone, SqlClient lit un gros binaire en un temps quadratique
([dotnet/SqlClient#593](https://github.com/dotnet/SqlClient/issues/593)) : la fermeture restait sur 4 documents de
`GED_RIG` (dont un de 510 Mo) plus de 2 min, à 100 % d'un cœur ; ensuite 4 documents en 7 s — et un lot s'écrit dès
64 Mo (au premier passage, lots bornés en lignes seulement, le processus de copie est monté à 4,3 Go pendant la fermeture
de `GED_RIG` ; ensuite 255 à 640 Mo). Et un **défaut relevé, non corrigé** : les documents de
la GED passent en clair dans la copie (`socle-anonymisation.md`, « Défaut relevé au lot 18 »).

**Fichiers du lot 18** : `Outils\Anonymisation\Fermeture.cs` (la fermeture), `Copie.cs` (copie par table réutilisée par
la fermeture, clés vérifiées, lecture synchrone, lots bornés en octets), `Rapport.cs` (lignes ajoutées, tours, durée) ;
`Outils\Repetition\Repetition.cs` (contrôle d'une copie réduite fermée : min(source, N) + ajouts) ; bancs
`tests\SocleAnonymisationCheck` (« copie réduite fermée : aucune clé étrangère orpheline », contre-épreuve sans la
fermeture) et `tests\RecopieRepriseCheck` (la répétition d'une base réduite fermée va au bout).

## Fichiers du lot 8

| Fichier | Rôle |
|---|---|
| `Base\migrations\REC-005_reprise.sql` | état « interrompu », reprises et point de reprise de `rec.Transfert`, `rec.TransfertLot` ; les deux règles d'alerte de la recopie |
| `Base\migrations\REC-006_comparaison.sql` | `rec.Comparaison`, `rec.ComparaisonTable`, `rec.ComparaisonEcart` |
| `Serveur\Recopie\CopieTelleQuelle.cs` | copie par lots et point de reprise, annulation d'une table entamée, copie provisoire sans clé |
| `Serveur\Recopie\MoteurTransfert.cs`, `JournalTransfert.cs`, `Instantane.cs` | reprise, verrou, états, instantané gardé et rattaché, règles provisoires |
| `Serveur\Recopie\RapportDuMatin.cs` | le rapport du matin (REC-005 CA04) |
| `Serveur\Recopie\Regles.cs` | `RegleTable.PourRepetition`, `Provisoire`, `RepriseDesRejets` ; `ReglesProvisoires` |
| `Serveur\Recopie\Comparaison.cs` | le contrôle des deux bases (REC-006) |
| `Serveur\Recopie\ComparaisonNocturne.cs`, `ModuleRecopieNuit.cs` | le contrôle de nuit planifié |
| `Serveur\Recopie\AlertesRecopie.cs`, `Serveur\Socle\Supervision\SourcesAlertes.cs` | les alertes de la recopie, par le surveillant (`ISourceAlertes`) |
| `Outils\Transfert\` | `transferer` (reprise, `--provisoires`), `rapport` |
| `Outils\Comparaison\` (`RigV3.Comparaison`) | `comparer` |
| `Outils\Repetition\` (`RigV3.Repetition`) | `repeter`, `feu` |
| `tests\RecopieRepriseCheck\` | le banc : un contrôle par critère des trois stories |

Preuve : `.\tests.ps1 RecopieReprise` (`docker compose up -d` et la lecture de RIG_DEV). Le processus du transfert y est
**tué** au milieu d'ARRIVEE puis relancé. Contre-épreuves faites : un point de reprise saboté arrête le transfert au
lieu de doubler ; la reprise qui ignore son point (défaut réinjecté à la main) rougit le banc ; les écarts injectés
puis remis : vus puis plus vus ; une copie non anonymisée est refusée ; un rejet sans règle rougit le feu.

## La recopie continue (REC-002, REC-003, REC-004) — lot 9

Après le transfert d'une base, la V2 continue de tourner (Q15) : ce qu'un agent change dans la V3 sur une **fiche
partagée** est recopié dans la V2 (REC-002, la V3 fait foi, Q7) ; ce que la V2 écrit encore sur ces fiches pour ses
anciennes affaires remonte dans la V3 (REC-003) ; une fiche changée **des deux côtés** entre deux passages garde la
version de la V3 dans les deux bases, et l'écart est montré à un agent désigné (REC-004).

### Ce qui se passe, dans l'ordre

1. **La V3 écrit une fiche partagée** (aujourd'hui sa copie `v2.<TABLE>`) : le déclencheur de la table, posé par la mise
   en service, écrit une ligne dans `rec.FileRecopie` **dans la même transaction** (fiche, identifiant V3, colonnes
   changées, ligne d'avant en JSON, agent, date). La V3 n'attend jamais la V2.
2. **Un passage** (`ServiceRecopie.PasserAsync`, lancé toutes les 5 s par `RecopieContinue` quand
   `Recopie:Continue=true`) traite d'abord la file, puis les changements de la V2 :
   - **l'applicateur** (`Applicateur.cs`) recopie chaque fiche dans la V2, sur la même instance (Q4), **une transaction
     par fiche** (fiche V2, correspondance, état commun, lignes de file) ; les fiches passent dans l'ordre de leur
     première ligne, et une fiche **visée** qui attend encore passe avant celle qui la vise (CA03) ; une fiche créée
     reçoit son identifiant de la V2 et sa correspondance (CA02) ; une **panne** de la V2 (hors ligne, injoignable)
     arrête le passage sans compter d'essai, tout reste en file avec le motif (CA06) ; un **refus** de la V2 (contrainte,
     déclencheur) ne coûte qu'à sa fiche : un essai, les autres continuent, mise de côté au 3e (REC-005, plus bas) ;
   - **la remontée** (`Remontee.cs`) lit le suivi des changements de SQL Server de la base V2 depuis son point de
     reprise (`rec.Remontee`, au départ la version lue par le transfert), dans l'ordre des versions, une fiche visée
     d'abord (CA03), et écrit chaque fiche dans la V3 sous la marque `rigv3.remontee` (le déclencheur de file ne la
     renvoie pas), au journal des gestes au nom de la recopie et du compte V2 d'origine (SOC-016 CA06 : pour un client,
     le `UTLST_LOGIN` de l'agent que la V2 a noté dans `CLIEN_ID_UTLST` pour ce changement ; sinon « non noté par la
     V2 ») ; le point avance après le lot. Une fiche que la V3 **refuse** ne le retient pas : elle va dans
     `rec.RemonteeEchec`, rejouée de son état courant à chaque passage, mise de côté au 3e essai (REC-005, plus bas).
3. **Pas d'aller-retour** (REC-002 CA05, REC-003 CA05) : après chaque fiche, les deux bases ont le même état, noté dans
   `rec.EtatLigne`. Un changement de la V2 dont l'état est exactement celui-là est une écriture de la recopie : ignoré.
4. **Conflit** (REC-004) : si la fiche V2 n'a plus l'état commun au moment d'appliquer un changement de la V3, la V2
   l'a changée entre deux passages ; la V3 gagne sur la fiche entière (CA04) et `rec.Ecart` / `rec.EcartChamp` gardent
   chaque champ que la V2 avait changé (valeur perdue, valeur gardée), les deux dates et les deux auteurs (CA01).
   L'écran **« Fiches en conflit »** (`PROC_FICHES_CONFLIT`, Surveiller › Les envois et les éditions) les montre à
   l'agent qui a la fonction `RECOPIE_ECARTS` ; « Traité » le sort de l'alerte (CA03) ; rouge à 5 jours ouvrés.
   `rec.EcartsParJour` donne le compte par jour, base et table au contrôle quotidien (REC-006, CA05).

### Un changement refusé : mis de côté, relancé (REC-005 CA01-CA03, CA05 sur la file continue)

Décision du chef du 21/11 : la file de la recopie continue tient REC-005 comme le transfert (lot 8). Le code :
`Serveur\Recopie\MisesDeCote.cs` (règles communes), `Applicateur.cs`, `Remontee.cs`, `AlertesRecopie.cs` ; la migration
`Base\migrations\REC-005_file-recopie.sql` (REC-002_recopie.sql est déjà jouée, elle ne se modifie plus).

- **Panne ou refus.** Une erreur qui touche la base entière (hors ligne 942, en transition 952, ouverture refusée 4060,
  délai, réseau, droit retiré, interblocage…) est une **panne** : le passage s'arrête, aucun essai n'est compté, tout
  repart au passage suivant. Toute autre erreur de SQL Server est un **refus** du changement par la base d'en face
  (contrainte, clé, déclencheur, conversion) : un essai de plus pour **sa** fiche — la fiche visée, si c'est elle qui est
  refusée —, et les autres fiches continuent.
- **3 essais, puis mis de côté** (seuil proposé, `ServiceRecopie.EssaisAvantMiseDeCote`) : V3 → V2, la ligne de tête de
  la fiche passe « mise de cote » avec son message (`rec.FileRecopie.Erreur`, `MiseDeCoteLe`) ; V2 → V3, la fiche passe
  « mise de cote » dans `rec.RemonteeEchec`. Les changements suivants de **la même fiche** attendent derrière elle, et une
  fiche qui la **vise** attend aussi (la ligne visée doit exister avant elle).
- **Rejouable** (CA01) : un passage coupé au milieu d'une fiche n'en laisse rien (une transaction par fiche) ; rejouer
  un changement déjà appliqué compare avant d'écrire : la ligne de l'autre base n'est pas réécrite (sa version de suivi
  ne bouge pas), aucun doublon.
- **Alerte d'exploitation** (CA05, règle `recopie-incident` de la supervision, `AlertesRecopie`) : un changement mis de
  côté (base, sens, table, première fiche, erreur) ; la recopie arrêtée depuis plus de 5 minutes (les passages échouent :
  base, table où il s'est arrêté, erreur) ; ou plus aucun passage et un changement de la V3 qui attend depuis plus de
  5 minutes (la boucle du serveur arrêtée). L'alerte se ferme d'elle-même quand la cause disparaît.

**Fiche d'incident (exploitation d'Amitel).**

1. Lire l'alerte « recopie-incident » : elle nomme la base, la table et l'erreur.
2. Lire la liste : `GET /recopie/{greffe}/mises-de-cote` (agent marqué Amitel dans la base du greffe, sinon 403) — sens,
   ligne ou fiche, essais, message, nombre de changements qui attendent derrière.
3. Corriger la cause (donnée, contrainte, paramétrage de la V2 ou de la V3). Une panne ne demande rien : la recopie
   repart seule au retour de la base.
4. Relancer **un** changement : `POST /recopie/{greffe}/mises-de-cote/relancer` avec
   `{ "sens": "V3 vers V2", "ligne": <ligne> }` ou `{ "sens": "V2 vers V3", "tableV2": "CLIENT", "idV2": "<id>" }`.
   Il repart à zéro essai ; au passage suivant (5 s), il passe, puis ceux qui attendaient derrière lui, dans l'ordre.
   La relance est au journal des gestes (famille « fiche-partagee ») et sur la ligne (`RelanceLe`, `RelancePar`).
5. S'il est refusé de nouveau, il compte de nouveaux essais et revient dans la liste au 3e.

### Mettre un greffe en recopie

```powershell
# 1. Base V2, AVANT le transfert : le suivi des changements de la base et de ses tables partagées (aucun programme
#    de la V2 n'est touché, REC-003 CA02). Les tables : celles des règles de recopie du serveur (ReglesRecopie.Decouvrir() :
#    ReglesRecopie.cs, et depuis le lot 10 FichesV3.cs) -- 21 tables le 2026-11-21.
cd Base\v2
sqlcmd -S <instance> -E -f 65001 -b -i recopie-v2.sql -v BaseV2=RIG_<GREFFE> RetentionJours=7 Tables="ACTION_RCS,ADRESSE_BASE,ARRIVEE,BATEAU,BODACC,CATEGORIE,CLIENT,CLIENT_ADRESSE,CLIENT_CONTACT,CREANCE_END,DEPOT_RCS,DOSSIER_RCS,ENCAISSEMENT_REPRIS,ENTREPRISE,INSCRIPTION,INSCRIPTION_RCS,INSTANCE,NON_INSCRIT,PASSAGE_CFE,RADIATION_TOTALE,UTILISATEUR"
# 2. Le transfert (plus haut).
# 3. La mise en service, côté V3 : PontRecopie.MettreEnServiceAsync(connexionV3, "RIG_<GREFFE>") pose un déclencheur de file
#    par fiche, le point de départ, et fige les fiches de la base (rec.FicheEnService, lot 10) ; refus nommés : pas de transfert
#    fini, suivi absent, rétention dépassée, fiche écrite par le transfert ailleurs que dans la table V3 de sa règle.
# 4. Le serveur : "Recopie": { "Continue": true, "IntervalleSecondes": 5, "SeuilRetardMinutes": 5 }.
```

### Les choix et leur raison

| Choix | Raison |
|---|---|
| **Une file dans la V3**, écrite par déclencheur ; la V2 écrite après | REC-002 CA06 : la V2 hors ligne ne bloque pas la V3. Le pont de numérotation écrit la V2 dans sa transaction : ici ce serait suspendre la V3 à la V2. Un déclencheur garantit qu'aucun code de domaine n'oublie la file. |
| **Suivi des changements de SQL Server** (CHANGE_TRACKING) côté V2 | REC-003 CA02 : aucun programme de la V2 modifié. Il écrit dans la transaction de l'agent, sans attendre la V3 (mesuré sur le poste : 4,32 ms par modification avec, 3,17 ms sans). La V3 arrêtée ne perd rien tant que la rétention (7 jours, proposée) couvre l'arrêt (CA04). |
| **L'état commun** (`rec.EtatLigne`), pas la marque du suivi, pour reconnaître une recopie | Mesuré le 2026-11-21 : un déclencheur de la V2 qui réécrit la ligne dans la même instruction donne une version **sans** le `CHANGE_TRACKING_CONTEXT` de la recopie. La marque reste posée (trace), l'état commun décide ; le même état sert à détecter le conflit. |
| La V3 reçoit ce que **les déclencheurs de la V2** écrivent pendant une recopie | Sans cela, le changement V3 suivant réécrirait dans la V2 les valeurs que ses déclencheurs venaient de poser, et la fiche ne reviendrait jamais à l'état commun. |
| **Colonnes propres** à chaque logiciel (état de transmission) | REC-002 CA04 : ni recopiées ni remontées, remises à leur valeur d'avant si le déclencheur de la V2 les marque « M » ; une fiche créée par la V3 reçoit « N » dans la V2. Prouvé : sans cette remise, le dossier passe « M », donc envoyé par la vue Infogreffe (défaut réinjecté, banc rouge). |
| Une transaction **par fiche**, d'une base à l'autre | Même instance (Q4) : une transaction locale suffit, sans coordinateur distribué ; une panne au milieu ne laisse ni demi-fiche ni correspondance orpheline. |
| **Les dates de `rec.*` à l'horloge du serveur SQL** | Le SQL Server des bancs (Docker) est à l'heure UTC, le poste à Paris (08:52 contre 09:52, mesuré) : l'âge d'un écart et la durée d'un passage se mesurent avec `SYSDATETIME()`, jamais `DateTime.Now`. |
| Un passage à la fois par base | Verrou applicatif `rigv3.recopie.<base>` : deux serveurs ne recopient pas deux fois la même file. |
| La boucle ne vise que les greffes **dont la base V3 existe** (`BaseV3CreeeLe`), et une même panne ne va au journal technique qu'une fois par 10 minutes | Mesuré par le banc : sans ce filtre, un greffe pas encore passé donnait une erreur à chaque passage (toutes les 5 s) ; chaque passage garde de toute façon son erreur dans `rec.PassageRecopie`. |

### Écarts code ↔ métier et choix à valider

| Sujet | Codé comme | Pourquoi / qui tranche |
|---|---|---|
| Neutraliser la transmission de la V2 | état d'avant remis dans la même transaction ; « N » pour une fiche créée par la V3 | règle retenue de REC-002 (« à valider ») ; seules `*_ETAT_TRANSMISSION` et `*_DATE_MODIF_FLAG_TRANS` (les colonnes que les déclencheurs écrivent) sont propres. Les états INPI et Infogreffe (`DSSRC_INPI_ETAT`…) sont recopiés : référent Échanges. |
| Fiche entière ou champ par champ | fiche entière (la V3 gagne partout), l'écart liste les champs V2 perdus | décision du tour 8 ; la fusion champ par champ est la question ouverte de REC-004 au référent Socle. |
| Auteur côté V2 | pour un **client** : l'agent que la V2 note dans `CLIEN_ID_UTLST`, au nom affiché par sa fiche client (« NOM Prénom (ABRÉGÉ) », table `UTILISATEUR`), quand le changement de la V2 l'a noté (sa date ou son agent a changé) ; pour les 6 autres tables : « non noté par l'ancien RIG » | `Client.Sauve` de la V2 écrit la date et l'agent courant (`RigMetier\Facture\Client.cs:522-523`), et sa fiche client les montre (« Dernière modification le … par … », `PROC_FCLIENT.cs:47`) : règle `ColonneAuteurV2` de `ReglesRec002`. Dossier, inscription, adresse, action, arrivée et catégorie n'ont aucune colonne d'agent dans leur schéma V2 (`RigDatabase\dbo\Tables\*.sql`), et le suivi des changements ne le dit pas. Un champ changé par la V2 sans date ni agent (écriture SQL directe) garde l'auteur « non noté » : l'agent de la fiche est alors celui d'un enregistrement plus ancien. |
| Date côté V2 | la date de mise à jour de la fiche quand elle a changé (`CLIEN_DATE_DERNIERE_MODIF`, `DSSRC_DATE_MAJ`, `INEDT_DATE_MAJ`), sinon l'heure où la recopie l'a vue | REC-004 T04. |
| Jours ouvrés du rouge | lundi-vendredi hors les 11 jours fériés légaux de métropole | outre-mer et Alsace-Moselle à ajouter : référent Socle. |
| Suppression d'une fiche | recopiée dans les deux sens ; supprimée dans la V2 et modifiée dans la V3 = recréée dans la V2 (écart « (fiche) ») | les stories ne parlent que de création et de modification : référent Socle. |
| Seuil de mise de côté | 3 essais refusés (REC-005 CA02, « seuil proposé ») | à valider avec l'exploitation d'Amitel. |
| Panne ou refus | liste fermée des numéros d'erreur de panne (`MisesDeCote.NumerosDePanne`) ; tout le reste est un refus | un numéro qui manque fait compter des essais pendant une panne : la mise de côté apparaîtrait à tort (alerte visible, relance sans dommage). |
| Libellés de l'écran | le titre du champ sur la fiche de la V2 quand elle en a un (32 colonnes, « V2 » dans `Contrats\Recopie\Libelles.cs`) ; sinon un libellé proposé | relevé dans `D:\RigApplication\Source` (`MetierTitre` du contrôle lié à la colonne). La fiche client de la V2 dit « E-mail » (pas « Adresse e-mail »). Le mot de passe d'un compte web (`CLIEN_MDP`) ne s'affiche jamais. Libellés proposés : référent Socle. |
| Ce qui est recopié | au lot 9, les 7 tables partagées qui avaient une table dans la V3 (`ReglesRec002`) | depuis le lot 10 : 21 tables (section « Les fiches partagées » plus bas) ; seule ARRIVEE reste dans `ReglesRec002`. |

### Ce qui ne se fait pas depuis ce poste, ou reste

| Critère | Ce qui manque | Où |
|---|---|---|
| REC-003 T01 : coût du suivi sur la plus grosse base | la copie anonymisée de la plus grosse base (attend la nouvelle réception du lot 6) | REC-008, lot 8 |
| REC-005 CA05 sur le vrai canal d'Amitel | prouvé sur le canal simulé de la supervision (hors production) ; l'envoi réel suit le canal du lot 2 | supervision, production |
| REC-005 T04 : un écran pour la liste et la relance | les deux routes existent (exploitation d'Amitel) ; l'écran de supervision ne les montre pas encore | supervision |
| REC-004 CA02 : « chaque ligne ouvre la fiche » | un dossier ouvre son affaire, un client l'écran des fiches clients ; inscription, adresse, action, arrivée et catégorie n'ont pas encore d'écran de fiche dans la V3 ; la story qui donne son écran à une de ces fiches renseigne `Cible` de sa règle dans `ReglesRec002` (`Serveur\Recopie\ReglesRecopie.cs`, ex. `ecran:PROC_FCLIENT`), sinon son écart reste sans « Ouvrir la fiche » | stories de domaine (lot 10) |
| Une story de domaine (lot 10) qui remplace `v2.<TABLE>` | fait au lot 10 : la règle « colonne par colonne » (`RegleRecopie.Correspondance`, `RegleTable.Cible`), section « Les fiches partagées » | — |

### Fichiers de la recopie continue

| Fichier | Rôle |
|---|---|
| `Base\migrations\REC-002_recopie.sql` | `rec.FileRecopie`, `rec.EtatLigne`, `rec.Remontee`, `rec.PassageRecopie`, `rec.Ecart`, `rec.EcartChamp`, `rec.EcartsParJour`, fonction `RECOPIE_ECARTS`, origine des correspondances |
| `Base\v2\recopie-v2.sql` | le suivi des changements de la base V2 et de ses tables partagées |
| `Serveur\Recopie\ReglesRecopie.cs` | une règle par table partagée (colonnes propres, date, nom, cible de l'écran) |
| `Serveur\Recopie\FichesRecopiees.cs` | les fiches lues dans le catalogue des deux bases, la ligne en JSON |
| `Serveur\Recopie\PontRecopie.cs` | la mise en service : déclencheurs de file, point de départ |
| `Serveur\Recopie\Applicateur.cs`, `Remontee.cs`, `EcrituresRecopie.cs` | V3 → V2 et conflits ; V2 → V3 ; les écritures d'une fiche |
| `Serveur\Recopie\ServiceRecopie.cs` | un passage, et la boucle du serveur `RecopieContinue` |
| `Serveur\Recopie\ModuleConflits.cs`, `Contrats\Recopie\Conflits.cs` | les routes des écarts (liste, alerte, « Traité »), les jours ouvrés, les routes des changements mis de côté (liste, relance) |
| `Base\migrations\REC-005_file-recopie.sql`, `Serveur\Recopie\MisesDeCote.cs`, `Contrats\Recopie\MisesDeCote.cs` | REC-005 sur la file continue : panne ou refus, essais, mise de côté, relance, liste de l'exploitation |
| `Serveur\Recopie\AlertesRecopie.cs` | l'alerte d'exploitation d'un changement mis de côté, d'une recopie arrêtée ou en retard (REC-005 CA05) |
| `Contrats\Recopie\Libelles.cs` | ce que l'agent lit à la place des noms de colonnes (« Client », « E-mail ») |
| `Pont\EcranConflits.cs`, `web\src\ecrans\EcranConflits.tsx` (+ test) | l'écran « Fiches en conflit » |
| `Pont\Accueil.cs` | l'alerte « fiche(s) modifiée(s) des deux côtés » dans la tuile « À traiter » de l'agent désigné (catégorie « Corrections diverses ») ; son clic ouvre la liste (REC-004 CA02) |
| `tests\RecopieDeuxSensCheck\` | le banc : un contrôle par critère des trois stories |

Preuve : `.\tests.ps1 RecopieDeuxSens` (il lui faut `docker compose up -d` et la lecture de RIG_DEV). Défauts réinjectés le
2026-11-21 : sans la remise de l'état de transmission, REC-002 CA04 rougit (dossier « M ») ; sans la détection du conflit,
REC-004 CA01, CA02, CA04 et CA05 rougissent. Le banc fait aussi sa propre contre-épreuve : l'état commun effacé, la valeur
de la V2 disparaît en silence, et l'oracle de CA01 le voit. REC-005 (21/11) : les contrôles de la mise de côté ont été
écrits et joués d'abord sur l'ancien code (main 40624c2) — rouges : le client « REFUSE PAR LA V2 » (contrainte
CK_BANC_REC005) arrêtait chaque passage (0 appliqué, SUIT LE REFUS 1 et 2 jamais recopiés), « REFUSE PAR LA V3 » bloquait le
point de reprise (366 pour une version courante de 370), une V2 hors ligne comptait des essais ; verts après la correction.

## Les fiches partagées (REC-012 à REC-017, REC-019) — lot 10

Lot 10 du plan (2026-11-21). Les fiches que la V2 et la V3 d'un greffe partagent ont désormais **leur table dans la V3**,
avec la correspondance de **chaque colonne** de la V2, et sont transférées (REC-001) puis recopiées dans les deux sens
(REC-002, REC-003) par cette correspondance. Jusqu'ici, leur copie telle quelle (`v2.CLIENT`…) en tenait lieu.

### Les tables V3 et leur correspondance

Migration `Base\migrations\REC-012_fiches-partagees.sql` (schéma `rec`, règle des migrations) ; correspondance colonne par
colonne dans `Serveur\Recopie\FichesV3.cs` (une ligne par colonne : nom V3, nom V2 ; le nom V2 est aussi en commentaire dans
la migration). **Une seule déclaration par fiche** donne sa règle de transfert (`RegleTable.Cible`) et sa règle de recopie
(`RegleRecopie.Correspondance`).

| Table V3 | Table V2 | Story | Colonnes | Particularités |
|---|---|---|---|---|
| `rec.Adresse` | ADRESSE_BASE | REC-016 | 19 + clé | `EstInconnue` : une seule adresse inconnue (CA03) |
| `rec.Entreprise` | ENTREPRISE | REC-012 | 17 + clé | `Denomination` (ENTRP_DESIGNATION) |
| `rec.DossierRcs` | DOSSIER_RCS | REC-012 | 40 + clé | état de transmission propre à la V2 (CA02) |
| `rec.Client` | CLIENT | REC-013 | 33 + clé | agent par compte de session (CA02) ; code abrégé unique, `DoublonHerite` (T01) |
| `rec.ClientAdresse` | CLIENT_ADRESSE | REC-013 | 9 + clé | lien vers l'adresse : sans clé étrangère sur RIG_DEV, avec une clé active sur RIG_RECETTE (lu le 2026-11-21) ; déclaré dans `LiensSansCle`, il vaut pour les deux |
| `rec.ClientContact` | CLIENT_CONTACT | REC-013 | 9 + clé | |
| `rec.ProvisionReprise` | ENCAISSEMENT_REPRIS | REC-013 | 7 + clé | consommée une seule fois (CA06, `ProvisionsReprises.cs`) |
| `rec.Creance`, `rec.Bateau` | CREANCE_END, BATEAU | REC-014 | 18, 25 + clé | agent de l'appel au registre des navires par compte de session |
| `rec.Inscription` | INSCRIPTION | REC-014 | 23 + clé | chaîne des renouvellements ; état de transmission propre à la V2 (CA03) |
| `rec.ActionRegistre` | ACTION_RCS | REC-015 | 14 + clé | ses six objets restent tels quels (`v2.DEPOT_RCS`…, T02) |
| `rec.TypeDemande` | CATEGORIE | REC-019 | 30 + clé | identifiant de la V3 ; le numéro V2 n'est que dans `rec.Correspondance` |
| `soc.Compte` (SOC-001) | UTILISATEUR | REC-017 | 11 + clé | réglages du poste, mot de passe… propres à la V2 (CA03) ; nom de session unique |

Tables restées **telles quelles** et recopiées comme au lot 9 (`ReglesObjetsDuRegistre`, même fichier) : BODACC, DEPOT_RCS,
INSCRIPTION_RCS, INSTANCE, NON_INSCRIT, PASSAGE_CFE (les objets d'une action, dont les colonnes métier sont celles de leurs
stories RCS et Judiciaire, REC-015 T02) et RADIATION_TOTALE (la radiation d'une inscription, REC-014 CA04). Avec ARRIVEE
(lot 11), **21 tables** sont recopiées.

Le banc vérifie la correspondance contre le **catalogue réel de RIG_DEV** : chaque colonne de chaque table V2 est nommée, ou
laissée à la V2 par une raison écrite (clé, état de transmission, déroulement d'un type, réglages d'un poste), au même type.

### Comment le transfert les écrit

- **Même copie par lots** que la copie telle quelle (`CopieTelleQuelle.cs`, `RegleTable.Cible`) : chaque colonne V2 sous son
  nom V3, mêmes identifiants (IDENTITY gardée), même point de reprise (REC-005), mêmes contrôles de fin (lues = écrites,
  une correspondance par ligne, empreinte des valeurs). La table V3 doit exister (migration) et être vide ; annulée sur un
  écart, elle est vidée (pas supprimée). Rapport : règle « colonne par colonne », table V3 de la fiche.
- **Une colonne traduite** (un agent par son compte de session, un service par `soc.Service.IdV2`) reçoit la valeur de la V2
  puis sa traduction dans la transaction du lot ; une valeur sans équivalent est vidée et notée dans `rec.LienCasse`
  (cause « sans equivalent dans la V3 »), jamais inventée ; elle est hors de l'empreinte.
- **Colonnes de la V3 seule calculées** au transfert : `rec.Client.DoublonHerite` (les codes abrégés que la V2 porte deux fois,
  gardés : RIG_DEV en a 15 lignes sur 7 codes) et `rec.Adresse.EstInconnue`.
- **Types de demande** : par la reprise « types de demande par reference » (`TypesDemande.cs`, rang 30, après les comptes) :
  la V3 donne à chaque type SON identifiant, le numéro V2 va dans `rec.Correspondance` ; parents et service traduits ; types
  indiscernables listés par la vue `rec.TypesDemandeIndiscernables`.
- **Adresse inconnue unique** : la reprise « adresse inconnue unique » (après les tables) garde la première, y rattache les
  fiches V3 et les correspondances des autres (origine « fusion »), et les retire.

### Comment la recopie les recopie

- La recopie lit et compare une fiche **sous ses noms V2** : la table V3 est lue « AS » ses noms V2 et écrite par eux
  (`TableRecopiee.SelectJsonV3`, `OpenJsonV3`, `ColonneV3`) ; applicateur, remontée, écarts et contrôle de nuit n'ont qu'une
  écriture pour les deux sortes de règles.
- **Règles figées à la mise en service** (`rec.FicheEnService`) : le serveur recopie une base avec les fiches de sa mise en
  service, pas avec celles que son code déclare au jour le jour. Sans cela, une règle ajoutée au serveur entrait dans la
  recopie d'un greffe déjà en service alors que sa table V2 n'y est pas suivie : la remontée s'arrêtait. Une fiche mise en
  service telle quelle (`v2.X`) garde sa forme telle quelle (`RegleRecopie.EnTelleQuelle`) ; la mise en service refuse une
  fiche que le transfert a écrite dans une autre table que celle de sa règle. Le **contrôle de nuit** prend les mêmes
  règles (`Comparaison.cs`) : comparer avec toutes celles du serveur traduisait des liens que la base ne recopie pas
  (512 faux écarts `CLIENT [CLIEN_ID_UTLST]` mesurés par `RecopieDeuxSensCheck`, où UTILISATEUR n'est pas en service).
- **Les parents d'un type de demande** (domaine, famille, activité) sont d'autres types. La V2 les tient par trois clés
  étrangères vers `CATEGORIE.CTGR_ID_CTGR` (`FK_CATEGORIE_CTGR_ID_CTGR_DOMAINE|FAMILLE|ACTIVITE_…`, actives et vérifiées
  sur RIG_DEV et RIG_RECETTE, lu le 2026-11-21) ; la règle les déclare aussi liens (`LiensSansCle`), pour qu'ils restent
  traduits sur une base où ces clés manqueraient (sans lien, un numéro V2 serait écrit dans la V3). Ils sont lus par la
  correspondance sous leur nom V2 : le contrôle de nuit qui les cherchait sous le nom V3 voyait 873 faux écarts
  (`RecopieRepriseCheck`, corrigé dans `Comparaison.cs`).
- **Une colonne traduite n'est jamais un lien** : depuis que les comptes sont recopiés, `CLIEN_ID_UTLST → UTILISATEUR` est
  aussi une clé vers une fiche partagée ; traduite deux fois, l'agent V3 « azuredevops » devenait l'agent V2 « Moulinette »
  (mesuré, REC-013 CA02 rouge au 4e essai du banc).
- **Le déclencheur de file compare au classement binaire** : sous le classement de la base (insensible à la casse), « Dupont »
  → « DUPONT » n'était pas un changement et n'arrivait jamais dans l'autre base (contre-épreuve dans le banc). Vaut pour
  toutes les fiches, telles quelles comprises.
- **Valeur unique prise par une autre fiche** (`CleMetier`) : la fiche est mise de côté d'emblée (1 essai, pas 3 : rejouer ne
  servirait à rien) avec son motif, et l'écart est ouvert dans « Fiches en conflit » — code abrégé d'un client dans la V3
  (REC-013 CA03), nom de session d'un compte dans les deux (REC-017 CA04).
- **Sans création dans la V2** (`SansCreationV2`) : un type de demande créé dans la V3 seule n'est pas créé dans la V2 ; ses
  lignes de file sont appliquées avec « sans equivalent dans la V2 » et `TypesDemande.SansEquivalentAsync` le liste (REC-019
  CA03, pour REC-007).
- **Provision reprise** (`ProvisionsReprises.ConsommerAsync`) : la V3 la réserve d'abord dans la V2 par l'UPDATE conditionnel
  de la V2 elle-même (`Encaissement.Sauve`), dans sa transaction ; le premier qui réserve consomme. C'est la seule écriture de
  la V3 dans la V2 hors de la file : une file laisserait consommer deux fois.

### Contrôles (banc `tests\RecopieFichesCheck`)

| Story | CA | Ce que le banc fait et vérifie |
|---|---|---|
| — | T01/T02 | 13 fiches, chaque colonne de RIG_DEV nommée ou laissée à la V2 par une raison, mêmes types |
| REC-012 | CA01 | les 345 dossiers transférés : SIREN et numéro de gestion égaux au classement binaire, même longueur, dont 61 hors norme ; contre-épreuve (minuscules) vue |
| | CA02 | dénomination changée dans la V3 (et changement de casse seul) → V2 ; dossier non marqué « à transmettre » (« F » reste « F ») ; contre-épreuve du classement binaire |
| | CA03 | procédure collective portée par la V2 → V3 ; la V2 la marque « M » (c'est elle qui transmet), la V3 ne la renvoie pas |
| | CA04 | entreprise et dossier créés dans la V3 → V2, liés, trouvés par SIREN + numéro de gestion, état « N » |
| REC-013 | CA01 | client et adresse créés dans la V3 → V2, trouvé par code abrégé, fiche identique |
| | CA02 | agent de rattachement dans les deux sens par le compte de session (identifiants V2 et V3 différents) ; au transfert, 312 clients, 0 faux |
| | CA03 | même code abrégé créé des deux côtés : le client V3 le garde, celui de la V2 est mis de côté d'emblée, écart ouvert |
| | CA04 | affaire V3 pour un client V2 : elle vise sa fiche, sans copie |
| | CA05 | adresse et contacts modifiés dans la V3 → V2 ; contact retiré dans la V3 → absent de la V2 |
| | CA06 | provision consommée par la V2 d'abord → la V3 refuse avec le motif ; par la V3 d'abord → l'UPDATE de la V2 ne touche rien ; soldes égaux |
| REC-014 | CA01 | renouvellement V3 d'une inscription V2 : même chaîne dans les deux |
| | CA02 | créance et bateau écrits avant l'inscription, qui garde son numéro |
| | CA03 | mise à jour recopiée : état de transmission de la V2 inchangé |
| | CA04 | radiation (RADIATION_TOTALE) saisie dans la V2 → la V3 voit l'inscription radiée |
| REC-015 | CA01 | une action de chacune des six sortes dans la V3, objet compris → historique V2, objet écrit avant |
| | CA02 | action saisie dans la V2 → même dossier dans la V3 |
| | CA03 | journée simulée (10 actions de chaque côté) : historiques identiques et dans le même ordre sur 100 dossiers tirés au hasard |
| | CA04 | demande V3 portant une action → mène à l'action ; les demandes transférées aussi |
| REC-016 | CA01 | adresse partagée par deux entreprises corrigée dans la V3 → les deux fiches V2 la voient |
| | CA02 | 42 adresses référencées par plusieurs fiches : mêmes références dans la V3 |
| | CA03 | deux adresses inconnues dans la V2 → une seule dans la V3, les trois fiches la visent |
| | CA04 | dix adresses de formes variées saisies dans la V3 → même découpage dans la V2 que celui que la V2 avait produit |
| REC-017 | CA01 | compte créé dans la V3 → V2, même nom de session, actif, même service (ses rôles, et l'ouverture de la V2 : lot 19, `tests\RecopieDroitsCheck`) |
| | CA02 | désactivation dans la V3 → V2 en moins d'une minute, par la vraie boucle du serveur |
| | CA03 | imprimante et scanner du poste gardés ; vides pour un nouveau compte |
| | CA04 | nom de session déjà pris dans la V2 : mis de côté d'emblée, motif, écart |
| REC-019 | CA01 | les 892 types de RIG_DEV (table entière) : retrouvés par référence et niveau, parents traduits, identifiant de la V3, numéro V2 nulle part ailleurs |
| | CA02 | RIG_DEV (demandes du 1417 = MODIFREPER) et RIG_RECETTE (du 2136 = EDS) : chaque demande garde le type de SA référence |
| | CA03 | type créé dans la V3 seule : pas créé dans la V2, sans numéro V2, listé sans équivalent |
| | CA04 | libellé et service changés dans la V3 → V2 ; déroulement inchangé |
| | CA05 | RIG_DEV : références en double distinguées par le niveau, aucune indiscernable ; RIG_RECETTE avec une jumelle posée par le banc : les deux transférées et listées |

Preuve : `.\tests.ps1 RecopieFiches` (il lui faut `docker compose up -d` et la lecture de RIG_DEV et RIG_RECETTE). Les bancs du
moteur (lots 7, 8, 9, 13) gardent la copie et la recopie telles quelles : leur extrait est complété des tables des règles du
serveur (`ExtraitRigDev.Completer`), le témoin de la colonne en plus et de l'arrêt sur conversion silencieuse est passé de
CLIENT / DOSSIER_RCS à ARRIVEE, et le banc du lot 9 met en service ses sept fiches sous leur forme telle quelle.

### Écarts code ↔ métier et choix à valider

| Sujet | Codé comme | Pourquoi / qui tranche |
|---|---|---|
| Noms des colonnes V3 | proposés (français, sans le préfixe V2), types de la V2 à l'identique | la story du domaine (RCS, Facturation…) peut les changer par une migration neuve |
| Schéma `rec` | règle des migrations (préfixe de la story) | les stories de domaine pourront déplacer leur table dans leur schéma |
| Code abrégé unique (REC-013 T01) | unique pour ce que la V3 crée ou reçoit ; les doublons hérités gardés, marqués | RIG_DEV en a 7 ; les fusionner est l'affaire du référent Facturation |
| Même code abrégé créé des deux côtés (CA03) | le client V3 est recopié (la V2 tolère les doublons, `IDX_CLIEN_ABREGE` non unique) ; celui de la V2 n'entre pas dans la V3, mis de côté avec un écart | « le client V3 garde le code » ; l'agent désigné décide du sort du client V2 |
| Adresse inconnue (REC-016 CA03) | le texte exact des reprises de 2017 (`* Adresse inconnu pour le client*`) | RIG_DEV n'en a aucune ; ses deux « ADRESSE INCONNUE » sont des saisies d'agent, non rattachées : référent à consulter |
| Découpage d'une adresse (REC-016 CA04) | la V3 tient les champs découpés de la V2 et la recopie les écrit tels quels | le découpage d'une adresse saisie en texte libre (`AdresseStandardisation.ExtraireAdresse`) est celui de l'écran d'adresse de la V3, à faire avec sa story |
| Radiation d'une inscription (REC-014 CA04) | RADIATION_TOTALE recopiée telle quelle | la story n'en parle pas (tables_v2 : INSCRIPTION, CREANCE_END, BATEAU) ; c'est ainsi que la V2 radie (`Inscription.IsRadieeOuPerimee`) |
| INSTANCE (objet d'action) | `INSTN_TOP_EXPERIAN` propre à la V2 | son déclencheur remet aussi `AUDIENCE_CABINET.AUDNC_TOP_DIFFUSION` à 0 quand la date Infogreffe change : non neutralisable par la recopie, référent Judiciaire |
| REC-019 T08 | deux AMITEL « indiscernables » annoncés | mesuré : niveaux 1, 2 et 3, tous discernables ; la liste est prouvée sur une jumelle posée par le banc |
| Provision : la V3 écrit la V2 hors de la file | réservation directe, même instance | c'est la seule façon qu'une provision ne soit consommée qu'une fois (CA06) ; si la V2 est hors ligne, la V3 refuse (pas de consommation sans réservation) |
| Une adresse V3 qui représente plusieurs adresses V2 | recopie vers la première (correspondance non « fusion ») | l'adresse inconnue ne change pas en pratique ; une modification de la seconde dans la V2 réécrirait la V3 |
| Mot de passe du compte web d'un client (`CLIEN_MDP`) | aucune colonne dans `rec.Client` : il reste dans la V2, jamais recopié ; un client créé par la V3 n'en a pas | SOC-001 CA08 : aucun mot de passe dans la V3 (`SocleIdentiteCheck` l'a vu rouge) ; le compte web des clients est à reprendre par sa story |

### Ce qui ne se fait pas depuis ce poste, ou reste

| Critère | Ce qui manque | Où |
|---|---|---|
| ~~REC-017 CA01 « l'agent ouvre la V2 sans autre geste »~~ | **Fait au lot 19** : les rôles et droits du compte passent avec lui (SOC-002 T05), section « Les droits d'un compte passent avec lui » plus bas | — |
| REC-012 CA02 « un seul envoi à Infogreffe sur l'environnement de test » | l'environnement de test d'Infogreffe est « à obtenir » ; la V3 n'envoie pas encore : prouvé ici que la V2 ne marque pas le dossier | Échanges, partenaire |
| Les 40 bases | mesure sur RIG_DEV et RIG_RECETTE seulement (les bases des greffes ne se lisent pas d'ici sans accord) ; un schéma de greffe sans une colonne de la correspondance la laisse hors de la recopie | répétition REC-008 (lot 18) |
| Écran « à maquetter » des stories | aucun écran de fiche n'est dans ce lot ; les écarts d'une fiche sans écran restent sans « Ouvrir la fiche » | stories de domaine |
| Les demandes recopiées par leur type (REC-019 T03) | la recopie des demandes est au lot 11 (REC-011, REC-007) : `TypesDemande.NumeroV2Async` / `TypeV3Async` / `SansEquivalentAsync` sont là pour elle | lot 11 |
| Un type dont le parent V2 n'existe pas (lien cassé hérité) | mesuré le 2026-11-21 en lecture seule : **0 cas** sur RIG_DEV (892 types, 873 avec un parent) et **0** sur RIG_RECETTE (857 types, 834 avec un parent) ; la V2 l'interdit par ses trois clés étrangères, actives et vérifiées (`is_disabled = 0`, `is_not_trusted = 0`) sur les deux bases. Si le cas existait (clé désactivée sur une base de greffe), le transfert laisserait le parent vide dans la V3 et le noterait (`rec.LienCasse`), et la recopie garderait le lien sans correspondance tel quel (règle commune du lot 9) : un numéro V2 comme parent dans la V3. Reste à lire l'état de ces clés sur les 40 bases | répétition REC-008 (lot 18) |

### Fichiers du lot 10

| Fichier | Rôle |
|---|---|
| `Base\migrations\REC-012_fiches-partagees.sql` | les 12 tables `rec.*`, `rec.TypesDemandeIndiscernables`, `rec.FicheEnService`, origine « fusion », cause « sans equivalent dans la V3 » |
| `Serveur\Recopie\FichesV3.cs` | une déclaration par fiche (correspondance colonne par colonne), les objets du registre tels quels, l'adresse inconnue unique |
| `Serveur\Recopie\TypesDemande.cs` | la reprise des types par référence, le numéro V2 d'un type, les types sans équivalent |
| `Serveur\Recopie\ProvisionsReprises.cs` | la consommation d'une provision reprise, réservée dans la V2 |
| `Serveur\Recopie\ReglesRecopie.cs`, `Regles.cs` | `ColonneFiche`, `Traduction`, `CleMetier`, `RegleRecopie.Correspondance`, `EnTelleQuelle`, `RegleTable.Cible`, `CibleFiche` |
| `Serveur\Recopie\CopieTelleQuelle.cs`, `MoteurTransfert.cs` | la copie colonne par colonne (cible, traductions, colonnes calculées) |
| `Serveur\Recopie\FichesRecopiees.cs`, `EcrituresRecopie.cs`, `PontRecopie.cs`, `Applicateur.cs`, `Remontee.cs`, `MisesDeCote.cs`, `Comparaison.cs` | la recopie sous les noms V2, les règles de la mise en service, le déclencheur binaire, les valeurs uniques, la comparaison de nuit des fiches |
| `tests\RecopieFichesCheck\` | le banc : un contrôle par critère des sept stories |

## Les affaires des deux logiciels, les anciens logiciels et le retour à l'ancien RIG (REC-007, REC-009, REC-010, REC-011, REC-018) — lot 11

Lot 11 du plan (2026-11-21). Une affaire se traite dans **un seul** logiciel (Q5, Q14 ; SOC-014) : créée avant le passage,
dans l'ancien RIG ; après, dans la V3. La base V3 tient pourtant les deux dans `v2.AFFAIRE` et `v2.DEMANDE`, et ce lot dit
comment elles y vivent, comment l'agent les voit, comment elles se lient, et comment un greffe bêta revient à l'ancien RIG.

### Les affaires des deux logiciels dans la base V3 (REC-010, REC-011, REC-018)

- **L'origine d'une ligne est sa correspondance** (REC-010 T01) : une affaire ou une demande qui a une ligne dans
  `rec.Correspondance` est de l'ancien RIG (transférée, ou remontée) ; sans correspondance, elle a été créée dans la V3.
  Aucune colonne en plus : le retour (plus bas) donne la sienne à chaque ligne de la V3 qu'il recopie, origine « retour ».
- **AFFAIRE et DEMANDE ne font que remonter** (`RegleRecopie.RemonteeSeule`, `Serveur\Recopie\GardeAffaires.cs`, classe
  `ReglesAffaires`) : les anciennes affaires et leurs demandes restent à jour dans la V3 par la remontée du lot 9 (une demande
  validée dans l'ancien RIG se relit validée au passage suivant, 5 s : REC-010 CA02) ; une ligne de la V3 n'est jamais mise
  en file vers la V2. `Base\v2\recopie-v2.sql` suit donc aussi AFFAIRE et DEMANDE (les tables viennent de
  `ReglesRecopie.Decouvrir()` : **23 tables** depuis ce lot).
- **Les numéros de la V2 gardés** (`RegleRecopie.NumerosV2`) : dans la copie, `DMND_ID_CTGR` et `DMND_ID_UTLST` des
  anciennes demandes sont des numéros de CATEGORIE et d'UTILISATEUR (le transfert les a copiés tels quels), alors que
  `rec.TypeDemande` et `soc.Compte` ont leurs propres identifiants. Ces colonnes ne sont pas des liens : ni traduites à la
  remontée, ni lues par la correspondance au contrôle de nuit (sinon une ligne remontée et une ligne transférée n'auraient
  pas le même sens). **Une ligne créée dans la V3 y porte l'identifiant de la V3** (type, compte) : le retour le traduit.
- **La garde** (déclencheur posé par la mise en service sur `v2.AFFAIRE` et `v2.DEMANDE`, avec les index qu'il lit) :
  1. une écriture dans une ancienne affaire — l'affaire, une de ses demandes, une demande ajoutée — est refusée, erreur
     51030 « Cette affaire se traite dans l'ancien RIG … », rien n'est écrit (REC-010 CA03) ;
  2. une création pour un greffe revenu à l'ancien RIG est refusée, erreur 51031, qui dit depuis quand (REC-007 CA03) ;
  3. **la règle de blocage de la V2, d'un logiciel à l'autre** (REC-011 CA01) : une demande créée dans la V3 sur un dossier
     où une ancienne demande **encore en cours** bloque déjà les autres demandes du dossier (`DMND_INHIBEE`,
     `DMND_ID_DMND_INHIBITRICE` : `ULT_9ANLWIN.cls:148`) est bloquée par elle ; son lien vise la ligne de l'ancienne dans la
     V3, la trace va dans `rec.LienDemande` ;
  4. sous la marque de la remontée : une ancienne demande **validée ou abandonnée** dans l'ancien RIG lève, au passage qui la
     remonte, les blocages qu'elle posait sur des demandes de la V3 (REC-011 CA02) ; la trace reste lisible.
  Les écritures de la remontée ne sont jamais refusées : c'est l'ancien RIG qui les a faites.
- **Les liens entre demandes** (REC-011) : une demande de la V3 vise une ancienne par ses colonnes de la V2
  (`DMND_ID_DMND_MERE`, `DMND_ID_DMND_INHIBITRICE` → la ligne de l'ancienne dans la V3, son identifiant V2 dans la
  correspondance, T01) ; le lien qu'une **ancienne** demande porte vers une demande de la V3 (bloquée par elle, née d'elle)
  est gardé dans `rec.LienDemande` (`HistoriqueAffaires.LierAncienneAsync`) et écrit dans la V2 au retour, quand la demande de
  la V3 y a une ligne (CA04).
- **L'arrivée** (REC-018) reste la copie telle quelle `v2.ARRIVEE`, recopiée dans les deux sens depuis le lot 9 (même numéro,
  mode, date et heure : CA01) ; l'agent de la V2 la retrouve par son numéro et y rattache une demande d'une ancienne affaire,
  qui remonte (CA02) ; ses demandes des deux logiciels se lisent ensemble (`HistoriqueAffaires.ArriveeAsync`, CA03).
- **Le contrôle de nuit** (REC-006, `Comparaison.cs`) ne compte plus comme « sans correspondance » une affaire ou une demande
  créée dans la V3 : c'est une affaire de la V3 (sinon chaque affaire nouvelle ouvrait l'alerte « recopie-ecart » chaque nuit).

### Les anciens logiciels (REC-009)

- **Les anciens numéros** : `CORRESP_MIGRATION_RIG`, `CORRESP_ORACLE_RIG`, `CORRESP_GAGI_RIG` — deux d'entre elles n'ont pas de
  clé primaire dans la V2 — sont reprises ligne pour ligne dans `rec.AncienNumero` (reprise « anciens numeros »,
  `Serveur\Recopie\AnciensNumeros.cs`), indexée sur l'ancien numéro et sur la fiche RIG (T01). La recherche
  (`AnciensNumeros.ChercherAsync`, route `GET /recopie/{greffe}/anciens-numeros?numero=|rig=`) reprend celle de la V2
  (`PROC_CORRESPMIG`, `OPE_RECHERCHE.cs` ; `PROC_CORRESP_MIGRATION|ORACLE|GAGI`) : la fiche RIG, son numéro, l'ancien numéro,
  le numéro Infogreffe, et la fiche de la V3 par la correspondance (CA01, CA02).
- **Les tables de reprise base à base** (`MIGRATION_REFERENCE`, `MIGRATION_TABLE`, `MIGRATION_CHAMP`, `MIGRATION_ID`) et la
  **correspondance des articles Replay** (`ARTICLE_CORRESP_REPLAY`, dates comprises) : telles quelles (`ReglesRec009`),
  facultatives, **en lecture seule** dans la V3 (déclencheur, erreur 51040), leur nombre de lignes au rapport du transfert,
  même vides (CA04, CA05).
- **Les fiches de remplissage** des reprises de 2017 (client « Fake data … », client « crée automatiquement par la
  migration RIG », adresse « * Adresse inconnu pour le client* » : `MigrationClient.cs:211-215`, `ClientExtended.cs:21,51`)
  sont repérées dans l'état figé et listées dans `rec.FicheAncienneReprise`, telles quelles, aucune supprimée ni corrigée
  (CA03 ; route `GET /recopie/{greffe}/anciennes-reprises`).

### L'écran « Historique de l'ancien RIG » (`PROC_HISTORIQUE_ANCIEN_RIG`)

Surveiller › Les envois et les éditions. `Pont\EcranHistoriqueV2.cs`, `web\src\ecrans\EcranHistoriqueV2.tsx` (+ test) ; sans
`RIGV3_SERVEUR`, une démonstration. Critères : entreprise (SIREN), dossier (numéro de gestion), client (code abrégé), arrivée,
ancien numéro, numéro RIG, fiches des anciennes reprises ; Entrée, F5 et F7 lancent la recherche (`EntreeLanceLaRecherche`).
Les affaires des deux logiciels, les anciennes marquées « traitée dans l'ancien RIG » ; le détail : les demandes et leur état,
le blocage et les liens avec le logiciel de chaque demande liée (un clic l'ouvre en lecture), les échanges avec les partenaires.
Le bloc « Ancien RIG » de la recherche d'affaires propose aussi « Voir son historique (lecture seule) »
(`EcranRechAffaires.HistoriqueAncienne`, SOC-015 CA01 laissé ouvert par le lot 3). Captures clair et sombre (démonstration) :
`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\phase2-lot11-historique\`. En fenêtre étroite (1 080 px,
`etroit-*.png`, 22/11), les tableaux à colonnes fixes donnaient 0 px à leur colonne libre — le nom de l'affaire et les liens
entre demandes disparaissaient (`etroit-avant-correctif-clair.png`) : chaque tableau garde désormais 200 px à cette colonne
(`grille()` de l'écran) et défile au-delà ; contrôle « fenêtre étroite » de `EcranHistoriqueV2.test.tsx`.

Les échanges (REC-010 CA04) se lisent dans la base des échanges (`EDI_GREFFE`, `FLUX_SORTANT` / `FLUX_ENTRANT`, lecture seule)
par code greffe et identifiant V2 de chaque demande ; réglage `Recopie:BaseEchanges` (absent : l'écran le dit).

### Revenir à l'ancien RIG pendant la bêta (REC-007)

Il prolonge le retour de la numérotation (NUM-022, `Base\v2\retour.sql`) : `Serveur\Recopie\RetourBeta.cs`, routes
`GET|POST /recopie/{greffe}/retour`, `PUT …/retour/responsable`, `PUT …/retour/types` (exploitation d'Amitel).

1. **La décision** (CA08) : seule la personne désignée (`rec.ResponsableRetour`) décide ; toute demande, prise ou refusée,
   est enregistrée avec son auteur, sa date et son motif (`rec.DecisionRetour`) et va au journal des gestes.
2. **Les refus avant toute écriture** (`RetourBeta.PreparerAsync`) : greffe hors bêta, ou **jour J des autres greffes passé**
   (la fin de bêta de `rigv3.Pont` de la base V2, celle de NUM-022 ; CA05) ; un **type de demande de la V3 seule** sans
   règle choisie (CA06, `rec.RegleTypeRetour`) ; une table qui pointe vers une affaire ou une demande de la V3 **sans règle
   de retour** (T03 : la règle de chacune des 33 tables est celle de la story de son domaine ; aujourd'hui aucune, donc
   refus — mesuré avec `INSTANCE`) ; un compte de la V3 sans équivalent dans l'ancien RIG.
3. **La V3 se ferme** pour le greffe (`rec.EtatRetour` « en retour », puis « revenu en V2 ») : plus aucune création (CA03).
4. **Une transaction**, sous le verrou de la recopie de la base : chaque affaire de la V3, puis chaque demande **une par une**
   dans l'ordre de création, sa date de validation de la V3 écrite (les déclencheurs de la V2 ne la remplacent pas : CA02),
   ses numéros tels quels, ses liens traduits par la correspondance (client, dossier, arrivée, type, agent…) ; un lien vers une
   demande de la V3 créée après est écrit à la fin ; les liens d'anciennes demandes vers la V3 (REC-011 CA04) ; la
   **facturation de la V3 reste dans la V3** — rien n'entre dans les chaînes signées de la V2 — et la V2 reçoit un renvoi par
   ligne dans `rigv3.RenvoiFactureV3` (`Base\v2\pont-v2.sql`, CA04). Chaque ligne a sa correspondance (« retour ») et son
   état commun : la remontée ne la renvoie pas, le contrôle de nuit la compare (CA07).
5. Une erreur annule tout et rouvre la V3 ; la décision est « arretee » avec sa cause.
6. **La création d'affaires est rouverte dans la V2** (T02) : l'aiguillage (`ModuleAiguillage`, `RegleAiguillage`) lit
   l'état du retour dans la base V3 d'un greffe bêta passé (`RetourBeta.RevenuDepuisAsync`) et donne alors **toute** affaire
   à la V2 — la date de passage, atteinte, ne change plus (SOC-007 CA05), c'est cet état qui décide. Preuve : `REC-007 T02`
   de `tests\SocleAiguillageCheck` (par la route) et de `tests\RecopieRetourCheck` (l'état posé par le vrai retour).

Le contrôle de nuit d'un greffe bêta n'alerte pas pour ses affaires de la V3, ni pendant la bêta ni après un retour : ni
« sans correspondance » (affaires, demandes, type de la V3 seule), ni lien orphelin (identifiants de la V3, voir REC-006 point 5).
Mesuré le 2026-11-22 : avant cette lecture, 497 liens orphelins pendant la bêta et 253 après le retour pour 248 demandes de la V3.

### Écarts code ↔ métier et choix à valider

| Sujet | Codé comme | Pourquoi / qui tranche |
|---|---|---|
| Recopie d'une demande au retour (REC-007, écart de la story) | une par une, date de validation de la V3 écrite | les déclencheurs de la V2 lisent une ligne (`DEMANDE.sql:189-233`) ; référent Socle |
| Factures de la V3 au retour (CA04) | restent dans la V3, renvoi par affaire dans `rigv3.RenvoiFactureV3` | règle proposée par la story ; référent de la Facturation (Q6) |
| Jour J (CA05) | la fin de bêta de `rigv3.Pont` (NUM-022), posée par la bascule | une seule date pour la numérotation et les affaires |
| Lignes qui dépendent d'une demande (33 tables) | refus tant que la story de leur domaine n'a pas écrit sa règle de retour | REC-007 T03 « par renvoi aux stories de domaine » |
| Blocage entre logiciels (REC-011) | une ancienne demande en cours qui bloque déjà une demande de son dossier bloque la demande de la V3 ; levé à « validée » ou « supprimée » | quels types bloquent : stories du domaine RCS (note de la story) |
| Ancienne demande bloquée par une demande de la V3 | gardé dans la V3, écrit dans la V2 au retour seulement | pendant la bêta, l'ancien RIG ne voit pas ce blocage : référent RCS |
| Arrivée (REC-018 T01, T02) | `v2.ARRIVEE`, telle quelle, mêmes noms des deux côtés | une table `rec.Arrivee` changerait le témoin de trois bancs du moteur (lots 7, 8) ; à faire avec l'écran d'arrivée |
| Échanges d'une affaire (REC-010 CA04) | les flux dont l'identifiant RIG est celui d'une demande | les autres (dossier, tables de dématérialisation) : story Échanges de leur flux |
| Personne désignée (CA08) | un compte par greffe, désigné par l'exploitation | à nommer chez Amitel (dossier de retour, REC-007 T04) |

### Ce qui ne se fait pas depuis ce poste, ou reste

| Critère | Ce qui manque | Où |
|---|---|---|
| REC-007 CA07 sur la copie anonymisée d'un greffe bêta (un mois d'affaires simulées) | la nouvelle réception du lot 6 et un greffe bêta (Le Puy, Chartres, Vienne) ; mesuré ici sur RIG_DEV : 370 lignes en 6,5 s, extrapolé au mois le plus chargé de RIG_DEV (35 180 demandes) à 16 min | REC-008 (lot 18) |
| REC-007 dans l'outil du passage | `RigV3.Passage retour` (`Outils\Passage\MoteurPassage.cs`, `RevenirAsync`) joue `retour.sql` ; le retour des affaires se lance par sa route, avant | lot 13 / 17 |
| REC-007 T04 (procédure de retour : préavis, fenêtre, communication) | écrite par l'exploitation d'Amitel | exploitation |
| REC-009 T05 (comptage sur les 40 bases) | les bases des greffes ne se lisent pas d'ici sans accord | répétition REC-008 (lot 18) |
| « Ouvrir dans l'ancien RIG » depuis l'historique | le geste existe dans la recherche d'affaires (lot 3) ; l'historique le dit en texte | écran |

### Fichiers du lot 11

| Fichier | Rôle |
|---|---|
| `Base\migrations\REC-007_retour.sql`, `REC-009_anciens-numeros.sql`, `REC-011_liens-demandes.sql` | décision, personne désignée, règles de type, état du greffe, origine « retour » ; anciens numéros, fiches d'anciennes reprises ; liens entre demandes |
| `Base\v2\pont-v2.sql` | `rigv3.RenvoiFactureV3` (renvoi vers la facturation de la V3) |
| `Serveur\Recopie\GardeAffaires.cs` | les règles AFFAIRE / DEMANDE (remontée seule, numéros V2) et le déclencheur de garde |
| `Serveur\Recopie\HistoriqueAffaires.cs` | l'historique, les demandes et leurs liens, l'arrivée, les échanges |
| `Serveur\Recopie\AnciensNumeros.cs` | REC-009 : règles, reprise, recherche, fiches d'anciennes reprises |
| `Serveur\Recopie\RetourBeta.cs` | REC-007 : préparation, décision, retour |
| `Serveur\Recopie\ModuleHistorique.cs`, `Contrats\Recopie\Historique.cs` | les routes et leurs données |
| `Serveur\Recopie\ReglesRecopie.cs`, `FichesRecopiees.cs`, `PontRecopie.cs`, `Comparaison.cs` | `RemonteeSeule`, `NumerosV2`, la garde posée à la mise en service ; au contrôle de nuit, les lignes que la V3 tient seule hors « sans correspondance » et les liens lus en identifiant de la V3 |
| `Contrats\Socle\Aiguillage.cs`, `Serveur\Socle\Aiguillage\ModuleAiguillage.cs` | REC-007 T02 : un greffe bêta revenu, toute affaire à la V2 |
| `Pont\EcranHistoriqueV2.cs`, `web\src\ecrans\EcranHistoriqueV2.tsx` (+ test), `web\src\ecrans\EcranRechAffaires.tsx` | l'écran, et le bloc « Ancien RIG » de la recherche d'affaires |
| `tests\RecopieRetourCheck\` | le banc : un contrôle par critère des cinq stories |

Preuve : `.\tests.ps1 RecopieRetour` (il lui faut `docker compose up -d` et la lecture de RIG_DEV). Les identifiants de la V2 y
sont décalés de 100 000 de ceux de la V3 avant toute écriture : un identifiant de la V3 écrit tel quel dans la V2 viserait une
autre ligne. Contre-épreuve faite le 2026-11-21 : le retour réécrit sans la date de validation (défaut réinjecté) — REC-007
CA02 rouge (0 date gardée sur 100) et le contrôle de nuit voit 120 écarts `DMND_DATE_VALIDATION` ; vert une fois remis.
Le 2026-11-22, CA07 passe par le contrôle de nuit complet (toutes les fiches partagées) et exige « rien à traiter », liens
orphelins compris, pendant la bêta et après le retour ; contre-épreuve : la correspondance d'une affaire rendue mise de côté,
ses deux demandes sont listées orphelines. L'aiguillage ignorant l'état du retour (défaut réinjecté) : `REC-007 T02` de
`SocleAiguillageCheck` rouge ; vert une fois remis.

## Les droits d'un compte passent avec lui (SOC-002 T05, fin de REC-017 CA01) — lot 19

Un compte créé ou changé dans la V3 arrivait dans la V2 sans ses rôles : la V2 ne l'ouvrait pas (au moins un rôle,
`RigClientAccueil\RigConsoleAccueil.cs:117-131`) et ne lui donnait aucune fonction. Ses rôles et ses droits directs passent
désormais **avec lui**, chacun dans son origine V2 (rôles, ancien système, menus de comptabilité). Le détail métier, les choix
et les écarts à valider sont dans `docs\reconstruction\socle-droits.md`, section « Recopie V3 → V2 des droits ».

**Le mécanisme, pour les lots suivants : la partie jointe d'une fiche** (`IRecopieJointe`, `RegleRecopie.Jointe`,
`Serveur\Recopie\ReglesRecopie.cs`). Ce qu'une fiche porte dans d'autres tables de la V3 et qui doit passer dans la V2 avec elle
se déclare sur SA règle : les tables et la colonne qui désigne la fiche (`TableJointe`), l'écriture dans la V2
(`AppliquerAsync`, dans la transaction de la fiche) et le retrait avant une suppression (`SupprimerAsync`). La mise en service
(`PontRecopie`) pose sur chaque table jointe un déclencheur qui met **la fiche** en file (genre « M », colonnes
« (libellé) », la ligne de la fiche en « avant ») ; l'applicateur appelle la partie jointe à la création de la fiche et pour
chaque ligne marquée (étape 4 de `Applicateur.FicheSansNomAsync`). Rien d'autre à brancher : ordre, panne, refus, mise de côté
au 3e essai, relance et écart sont ceux de la fiche. Aujourd'hui une seule partie jointe : les droits du compte
(`Serveur\Recopie\DroitsRecopies.cs`, tables `soc.CompteRole` et `soc.CompteFonction`).

**Mettre un greffe en recopie** : la mise en service pose maintenant **trois** déclencheurs pour le compte
(`soc.TR_Compte_FileRecopie`, `soc.TR_CompteRole_FileRecopie`, `soc.TR_CompteFonction_FileRecopie`). Un greffe mis en service
avant ce lot rejoue sa mise en service pour les avoir (sans risque).

**Corrigé en route** : `ServiceComptes.CreerAsync` (`OUTPUT INSERTED.Id` sans `INTO`) ne pouvait plus créer de compte dans
la base d'un greffe en recopie : SQL Server refuse ce `OUTPUT` sur une table qui porte un déclencheur (erreur 334). Les autres
`OUTPUT` sans `INTO` du serveur (relevés le 2026-11-21) visent des tables sans déclencheur de recopie (`rec.*`, courriels,
traitements, documents) ; une story qui donnera une fiche partagée à l'une d'elles devra le revoir.

| Fichier | Rôle |
|---|---|
| `Serveur\Recopie\DroitsRecopies.cs` | les droits d'un compte écrits dans leurs trois origines V2, vérifiés avant validation ; l'ancien système (`AncienSysteme.Resoudre`) |
| `Serveur\Recopie\ReglesRecopie.cs`, `FichesV3.cs` | `IRecopieJointe`, `TableJointe`, `RegleRecopie.Jointe` ; la fiche « compte d'agent » porte ses droits |
| `Serveur\Recopie\PontRecopie.cs`, `Applicateur.cs` | les déclencheurs des tables jointes ; l'écriture avec la fiche, le retrait avant la suppression |
| `Serveur\Socle\Comptes\RepriseDroits.cs` | la réponse de la V2 (`ReponsesV2`) et la lecture des droits d'un compte (`DonneesV2.LireCompteAsync`) |
| `Serveur\Socle\Comptes\ServiceComptes.cs` | la création d'un compte sous un déclencheur (`OUTPUT … INTO`) |
| `tests\RecopieDroitsCheck\` | le banc : compte créé avec un rôle (ouverture de la V2), rôle ajouté puis retiré, droits directs « comptabilité » et « ancien », mise de côté, refus au 3e essai, écart, suppression, contre-épreuve |

Preuve : `.\tests.ps1 RecopieDroits` → code 0 (22 contrôles, oracle de la V2 = V3 sur les 876 fonctions de chaque compte).

<!-- fix-ok: lot 19 (2026-11-21), pas un correctif de ce document : deux lignes du lot 10 qui laissaient les roles de REC-017 CA01 au Socle renvoient a leur preuve (banc RecopieDroits vert, code 0, 22 controles lus dans sa sortie) et la section du lot 19 est ajoutee a la fin. -->

## Le contrôle de nuit compare aussi les droits (REC-006 CA01 « pour chaque table partagée », CA02, CA04 ; SOC-002 T06) — lot 20

Depuis le lot 19, les droits d'un compte passent de la V3 à la V2 (rôles, ancien système, menus de comptabilité). Le contrôle de
nuit ne les regardait pas : un rôle donné à la main dans l'ancien RIG, ou une recopie de droits ratée, passait inaperçu jusqu'au
prochain changement du compte dans la V3. Il les compare maintenant, comme une table partagée de plus : **`UTILISATEUR (droits)`**.

**Ce qui est comparé : la réponse, pas les lignes.** Les rôles de la V3 ne correspondent pas ligne à ligne à ceux de la V2 (rôles
« RIGV3 n° », trois origines). Pour chaque compte **actif** de la V3 qui a une correspondance V2 (`rec.Correspondance`
`UTILISATEUR` → `soc.Compte`, actif aussi dans la V2) et chaque fonction du catalogue de la V2 (`PROCESSUS_ET_FONCTIONALITE`), la
**réponse de la V2** est celle de la V3 (`soc.Droit`, toutes origines). La réponse de la V2 est calculée **une seule fois, par le
calcul que la recopie fait déjà avant d'écrire** : `DroitsRecopies.Differences` (qui appelle `RepriseDroits.ReponsesV2`) sert à
la vérification d'avant validation de la recopie (étape 8) **et** au contrôle de nuit. Pas de second calcul.

**Un état figé, comme le reste du contrôle (CA05).** La réponse de la V2 se lit dans l'instantané de la V2 pris pour le contrôle,
celle de la V3 dans l'instantané de la V3 : aucune lecture ne bloque les agents. Comme pour les autres fiches, un écart est **relu dans
les bases vivantes** : disparu entre-temps (une recopie des droits est passée entre les deux états figés), il est « résorbé »,
compté, non alerté.

**Ce que dit le rapport** (`rec.ComparaisonTable`, `rec.ComparaisonEcart`, contrôle de la base) :
- une ligne `UTILISATEUR (droits)` → `soc.Droit` : nombre de comptes (colonnes « lignes »), réponses comparées (comptes × fonctions),
  écarts, résorbés ; son détail dit « N comptes actifs recopiés, M fonctions de la V2 chacun » ;
- un écart (genre « ecart ») par compte et par fonction : `IdV2` = compte V2, `IdV3` = compte V3, `Colonnes` = la fonction,
  `Detail` = « V2 : accordée ; V3 : refusée » (ou l'inverse). Ni login ni valeur : des identifiants et un code de fonction.

**L'alerte d'exploitation est celle qui existe (CA04, `AlertesRecopie`, règle `recopie-ecart`)** : les écarts de droits entrent dans
le total du rapport (`RapportComparaison.Ecarts`, code de sortie 5) ; le message nomme la table (« tables : UTILISATEUR (droits)
(144 ecarts, …) ») et renvoie au contrôle. Le contrôle propre suivant ferme l'alerte.

**Quand il s'exécute.** Seulement quand la recopie de la fiche « compte d'agent » est **en service** pour la base
(`rec.FicheEnService`, règle `UTILISATEUR` avec sa partie jointe `DroitsRecopies`) et que `UTILISATEUR` fait partie des tables
comparées : avant la mise en service, la V3 n'écrit pas les droits dans la V2, il n'y aurait que de faux écarts. Une base V2 sans les
tables de droits n'est pas comparée sur ce point.

### Contrôles (banc `tests\RecopieDroitsCheck`, section 4)

| Critère | Ce que le banc prouve |
|---|---|
| REC-006 CA01 / SOC-002 T06 | après la journée de recopie du banc, le contrôle compte 97 comptes × 876 fonctions = 84 972 réponses, **0 écart**, écrit au rapport de la base |
| REC-006 CA02 | un rôle donné à la main dans la V2 (hors recopie) à un compte est listé : compte V2, compte V3, fonction, « V2 : accordée ; V3 : refusée » — les mêmes 144 fonctions que l'oracle indépendant de la V2 (`tests\SocleDroitsCheck\OracleDroitsV2.cs`) ; code de sortie 5 |
| REC-006 CA04 | le vrai surveillant lève « recopie-ecart » (canal d'alerte), qui nomme `UTILISATEUR (droits)` et le contrôle ; le rôle retiré, le contrôle suivant est propre et ferme l'alerte (jumeau) |
| REC-006 CA01 | une recopie de droits ratée (déclencheur coupé, rôle retiré dans la V3 seulement) est vue : mêmes 142 fonctions que l'oracle ; la recopie passée, plus d'écart (jumeau) |
| Contre-épreuve | l'étape des droits neutralisée dans `ComparaisonBases` (`if (false && …)`) : le banc est **rouge** (rapport sans ligne de droits) ; remise, vert |

### Écarts code ↔ métier et choix à valider

- **Seuls les comptes actifs, des deux côtés.** Un compte désactivé dans la V3 ou dans la V2 n'est pas comparé ici (la comparaison de la
  fiche `UTILISATEUR` voit son état). À valider : faut-il aussi contrôler les droits d'un compte désactivé (il n'ouvre plus la V2) ?
- **Une fonction que la V3 connaît et pas la V2** (catalogue V3 plus large) n'est pas comparée : la V2 n'a pas de réponse.
- **Le contrôle ne fait pas la distinction « par origine »** : un droit direct de la V3 que la V2 donne par un rôle (ou
  l'inverse) n'est pas un écart tant que les réponses concordent — c'est voulu (la recopie écrit chaque droit dans son origine,
  la correspondance n'est pas ligne à ligne).

### Ce qui ne se fait pas depuis ce poste

- La durée sur les **40 bases**, la nuit, sur le serveur de production : le banc mesure une base jetable de RIG_DEV (97 comptes actifs,
  876 fonctions, environ 85 000 réponses par contrôle). Le coût n'a pas été mesuré sur une base de greffe réelle, ni sous la charge de
  la nuit (REC-006 CA05 reste, comme au lot 8, une estimation du poste de développement).

| Fichier | Rôle |
|---|---|
| `Serveur\Recopie\DroitsRecopies.cs` | `Differences` (le calcul partagé), `ComparerAsync` (le contrôle des droits), `TableControle` |
| `Serveur\Recopie\Comparaison.cs` | l'étape « 4 bis » : appelle le contrôle des droits quand la recopie des comptes est en service |
| `Serveur\Recopie\ComparaisonNocturne.cs` | le journal du contrôle de nuit dit combien d'écarts sont des écarts de droits |
| `tests\RecopieDroitsCheck\` | section 4 du banc : contrôle propre, rôle à la main, alerte, recopie ratée, jumeaux |

