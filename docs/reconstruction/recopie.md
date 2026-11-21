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
| `Serveur\Recopie\ReglesRec001.cs` | les 9 tables telles quelles de REC-001 |
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
| CA01 à CA03 sur la **recopie continue** (changement rejouable, 3 essais puis mise de côté avec son message, les autres fiches continuent, la même fiche attend, relance unitaire dans l'ordre) | la file `rec.FileRecopie` et l'applicateur sont REC-002 (lot 9, pas encore sur main) ; aujourd'hui l'applicateur s'arrête au premier échec et compte ses essais | lot 9, ou un lot après lui |
| CA05, second cas (alerte d'un changement mis de côté) | la même file ; la source `AlertesRecopie` l'accueille (règle « recopie-incident » déjà admise) | lot 9 |
| Fiche d'incident pour l'exploitation (T04) | relance, lecture du rapport du matin, alerte : écrits ici ; la lecture des changements mis de côté attend la file | lot 9 |

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
   correspondance (hors liens cassés hérités, déjà dans `rec.LienCasse`).
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
| Tables V3 qui ne sont pas la table V2 « telle quelle » | Correspondances seulement (UTILISATEUR → `soc.Compte`) : la conversion colonne par colonne est à décrire par la story de la table (T03). |

**À valider / reste** : l'heure 02:00 et la durée de 90 jours (proposées) ; T03 — les colonnes « propres » que la
recopie du lot 9 ne recopie jamais (état de transmission) devront être exclues du contrôle quand ses règles
(`ReglesRec002`) seront sur main, sinon elles sortiront en écarts ; CA05 sur les 40 bases de production (pas sur ce
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
chez Amitel), soit une copie réduite **fermée sur ses clés étrangères** (comme l'extrait du banc, `ExtraitRigDev`) : à
ajouter à `Outils\Anonymisation` (lot 6). Le banc, lui, répète jusqu'au bout une base complète de 27 tables.

**Ce qui ne se fait pas d'ici** : les 40 bases des greffes (lecture sur accord de chaque base, chez Amitel, l'anonymisation
sur le serveur du greffe : CA05) ; le **retard de recopie** (la recopie continue, lot 9, et le simulateur d'une journée
d'écritures, REC-008 T04, manquent) ; la planification des répétitions avant la bêta (T03, exploitation).

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
     reçoit son identifiant de la V2 et sa correspondance (CA02) ; la première fiche qui échoue arrête le passage et
     reste en file avec ses essais (CA06) ;
   - **la remontée** (`Remontee.cs`) lit le suivi des changements de SQL Server de la base V2 depuis son point de
     reprise (`rec.Remontee`, au départ la version lue par le transfert), dans l'ordre des versions, une fiche visée
     d'abord (CA03), et écrit chaque fiche dans la V3 sous la marque `rigv3.remontee` (le déclencheur de file ne la
     renvoie pas), au journal des gestes au nom de la recopie (SOC-016 CA06) ; le point avance après le lot.
3. **Pas d'aller-retour** (REC-002 CA05, REC-003 CA05) : après chaque fiche, les deux bases ont le même état, noté dans
   `rec.EtatLigne`. Un changement de la V2 dont l'état est exactement celui-là est une écriture de la recopie : ignoré.
4. **Conflit** (REC-004) : si la fiche V2 n'a plus l'état commun au moment d'appliquer un changement de la V3, la V2
   l'a changée entre deux passages ; la V3 gagne sur la fiche entière (CA04) et `rec.Ecart` / `rec.EcartChamp` gardent
   chaque champ que la V2 avait changé (valeur perdue, valeur gardée), les deux dates et les deux auteurs (CA01).
   L'écran **« Fiches en conflit »** (`PROC_FICHES_CONFLIT`, Surveiller › Les envois et les éditions) les montre à
   l'agent qui a la fonction `RECOPIE_ECARTS` ; « Traité » le sort de l'alerte (CA03) ; rouge à 5 jours ouvrés.
   `rec.EcartsParJour` donne le compte par jour, base et table au contrôle quotidien (REC-006, CA05).

### Mettre un greffe en recopie

```powershell
# 1. Base V2, AVANT le transfert : le suivi des changements de la base et de ses tables partagées (aucun programme
#    de la V2 n'est touché, REC-003 CA02). Les tables : celles de Serveur\Recopie\ReglesRecopie.cs.
cd Base\v2
sqlcmd -S <instance> -E -f 65001 -b -i recopie-v2.sql -v BaseV2=RIG_<GREFFE> RetentionJours=7 Tables="ACTION_RCS,ADRESSE_BASE,ARRIVEE,CATEGORIE,CLIENT,DOSSIER_RCS,INSCRIPTION"
# 2. Le transfert (plus haut).
# 3. La mise en service, côté V3 : PontRecopie.MettreEnServiceAsync(connexionV3, "RIG_<GREFFE>") pose les 7 déclencheurs
#    de file et le point de départ (refus nommés : pas de transfert fini, suivi absent, rétention dépassée).
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
| Ce qui est recopié | les 7 tables partagées qui ont une table dans la V3 (`ReglesRec002`) | ENTREPRISE, BODACC, DEPOT_RCS, INSCRIPTION_RCS, INSTANCE, NON_INSCRIT, PASSAGE_CFE, BATEAU, CREANCE_END n'ont pas encore de règle de transfert ; UTILISATEUR → `soc.Compte` demande la correspondance de ses colonnes (story des agents). |

### Ce qui ne se fait pas depuis ce poste, ou reste

| Critère | Ce qui manque | Où |
|---|---|---|
| REC-003 T01 : coût du suivi sur la plus grosse base | la copie anonymisée de la plus grosse base (attend la nouvelle réception du lot 6) | REC-008, lot 8 |
| REC-002 T04 / REC-003 T04 : alerte de retard | le retard au-delà de 5 min va au journal technique en erreur ; une règle d'alerte du Surveillant (lot 2) reste à poser | supervision |
| REC-004 CA02 : l'alerte dans la tuile « À traiter » de l'accueil | l'alerte est servie (`EcranConflits.Alerte`, `GET /recopie/{greffe}/ecarts/alerte`) ; `Pont\Accueil.cs` est en cours dans un autre fil, et la tuile ne montre aujourd'hui que les alertes de la feuille métier (décision du 17/09/2026) : à trancher | chef, lot de l'accueil |
| REC-004 CA02 : « chaque ligne ouvre la fiche » | un dossier ouvre son affaire, un client l'écran des fiches clients ; inscription, adresse, action, arrivée et catégorie n'ont pas encore d'écran de fiche dans la V3 | stories de domaine (lot 10) |
| Une story de domaine (lot 10) qui remplace `v2.<TABLE>` | elle remplace la ligne de sa table dans `ReglesRec002` par une règle qui nomme sa table V3 et la correspondance de ses colonnes (une sorte nouvelle de règle, à écrire avec elle) | lot 10 |

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
| `Serveur\Recopie\ModuleConflits.cs`, `Contrats\Recopie\Conflits.cs` | les routes des écarts (liste, alerte, « Traité »), les jours ouvrés |
| `Pont\EcranConflits.cs`, `web\src\ecrans\EcranConflits.tsx` (+ test) | l'écran « Fiches en conflit » |
| `tests\RecopieDeuxSensCheck\` | le banc : un contrôle par critère des trois stories |

Preuve : `.\tests.ps1 RecopieDeuxSens` (il lui faut `docker compose up -d` et la lecture de RIG_DEV). Défauts réinjectés le
2026-11-21 : sans la remise de l'état de transmission, REC-002 CA04 rougit (dossier « M ») ; sans la détection du conflit,
REC-004 CA01, CA02, CA04 et CA05 rougissent. Le banc fait aussi sa propre contre-épreuve : l'état commun effacé, la valeur
de la V2 disparaît en silence, et l'oracle de CA01 le voit.
