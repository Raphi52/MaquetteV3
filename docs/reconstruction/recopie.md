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

1. **Journal ouvert** : une ligne dans `rec.Transfert` (état « en cours »). Refus si la base V2 a déjà un transfert
   dans cette base V3 (reprendre après un incident, c'est REC-005), ou si la base V2 n'est pas sur l'instance de la
   base V3 (Q4 ; `num.Reprendre` la lit par son nom).
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
5. Chaque table **« telle quelle »**, dans l'ordre déduit des clés étrangères (`CopieTelleQuelle.cs`), **une
   transaction par table** : table `v2.<NOM>`, liens cassés, copie par lots, correspondance, contrôle de fin.
6. Les reprises placées **après** les tables : les compteurs (NUM-005, `num.Reprendre`), lus dans la **V2 vivante**
   au dernier moment, puis `num.ReleverHeritage`.
7. **Fini** : totaux, durée, un événement 210 « reprise de données » dans `soc.EvenementTechnique` ; l'instantané
   est supprimé.

**Arrêt propre** : un écart (comptes, correspondances, valeurs, écart d'une reprise) ou une erreur annule la table en
cours, rien de ce qui suit n'est fait, l'état « arrete » et son motif sont au journal, l'instantané est supprimé.
Les tables finies avant restent, avec leur ligne de rapport. **La base V2 n'est jamais écrite** (CA06).

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

Codes de sortie : **0** fini, **1** arrêté (écart, erreur), **2** refusé avant toute écriture, **3** commande mal
formée ou serveur refusé. Sans `--exploitation`, l'outil refuse toute base qui n'est pas sur ce poste, **sans s'y
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
| **REC-005** (lot 8) reprendre après un incident | Une transaction par table, une ligne `rec.TransfertTable` par table finie, l'état « arrete » et son motif. Aujourd'hui un second transfert de la même base est **refusé** : la reprise au lot suivant est à écrire là (`MoteurTransfert`, contrôle « déjà un transfert »). |
| **REC-006** (lot 8) comparer chaque nuit | `rec.Correspondance` et l'empreinte de `CopieTelleQuelle` (même calcul des deux côtés, sur un état figé). |
| **REC-008** (lot 8) répéter sur les copies anonymisées | `RigV3.Transfert transferer` sur une copie de `Outils\Anonymisation` (lot 6) : le contrôle de la copie (lot 6, `MesureGardee.Toutes`) passe de 12 s à 32-44 s sur RIG_DEV entier, à compter dans la durée des répétitions. La copie anonymisée ne sert de jeu de répétition qu'une fois le lot 6 reçu à nouveau (décision du chef). |
| **REC-009** (lot 11) anciens logiciels | `MIGRATION_ID`, `CORRESP_*` : une règle chacune ; la règle « déjà repris = marqué » de 2017 rejoint `rec.Rejet` / `rec.LienCasse`. |
| **REC-010 à REC-019** fiches partagées | Leur table est aujourd'hui « telle quelle » dans `ReglesRec001.cs` (commentaire : la story qui la reprendra) ; `UTILISATEUR` est portée par la reprise des comptes. |

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
| Transférer une base **entière** | RIG_DEV a **833 tables sans règle** : le moteur refuse (T03) tant que leur story n'a pas déclaré la sienne | stories de domaine |
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
| `Serveur\Recopie\ReglesRec001.cs` | les 9 tables telles quelles de REC-001 |
| `Serveur\Recopie\ReprisesPhase1.cs` | réglages, comptes et droits, compteurs : appelés, pas refaits |
| `Outils\Transfert\` (`RigV3.Transfert`) | l'outil de l'exploitant : `transferer`, `inventaire` |
| `tests\RecopieTransfertCheck\` | le banc : base V2 jetable tirée de RIG_DEV (`ExtraitRigDev.cs`), un contrôle par critère |

Preuve : `.\tests.ps1 RecopieTransfert` (il lui faut `docker compose up -d` et la lecture de RIG_DEV ; autre serveur :
`RIGV3_RIG_DEV`). Contre-épreuves faites : une valeur changée dans la V3 rougit l'oracle ; une valeur écrite dans la
V2 change son empreinte ; une date arrondie en silence dans la V3 arrête le transfert ; le classement remis sur les
colonnes non texte et le lien vers une mère rejetée non marqué rougissent le banc.
