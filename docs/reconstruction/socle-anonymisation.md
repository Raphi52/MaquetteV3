# Socle — copie anonymisée de la base d'un greffe pour les tests (SOC-021)

Lot 6 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json` : **SOC-021**
(contraintes C17 « toute copie de production servant aux tests est anonymisée » et C10).

| Ce qu'elle donne | Où |
|---|---|
| L'outil qui copie une base V2 dans le SQL Server local et y remplace les données personnelles **pendant le transfert** (aucune valeur réelle n'est écrite dans la copie) | `Outils\Anonymisation\` (`RigV3.Anonymisation`) |
| Les règles par table et colonne, d'où elles viennent, et l'inventaire des colonnes à relire | `Outils\Anonymisation\Regles.cs` ; commande `inventaire` |
| Les valeurs fictives (permutation à clé, mot par mot) | `Outils\Anonymisation\Pseudonymes.cs`, `Anonymiseur.cs` |
| La copie : schéma, données, index, clés, contraintes, vues et procédures, marque, rapport | `Outils\Anonymisation\Copie.cs`, `Schema.cs`, `Rapport.cs`, `PorteSource.cs` |
| Le contrôle automatique d'une copie (CA06) | `Outils\Anonymisation\Controle.cs` ; commande `controler` |
| Les bancs | `tests\SocleAnonymisationCheck` (la copie et son contrôle) et `tests\SocleAnonymisationTirageCheck` (le tirage du contrôle) — **`.\tests.ps1 SocleAnonymisation`** lance les deux ; chaque contrôle cite son CA |

Pour la suite : REC-008 (répétitions du transfert) part d'une copie faite par cet outil. Elle se reconnaît à
`anonymisation.Copie` et à la propriété de base `RIGV3_ANONYMISATION` (décision notée au plan).

## Mode d'emploi

```
# la copie (source lue seulement ; copie sur CE poste : docker compose up -d)
dotnet run --project Outils\Anonymisation -- copier --source "Server=SQL-PROD\…;Database=RIG_AMIENS;Integrated Security=True;TrustServerCertificate=True" `
    --accord RIG_AMIENS --cible "Server=127.0.0.1,14330;User ID=sa;Password=…;TrustServerCertificate=True;Database=RIG_AMIENS_ANONYME" `
    --rapport D:\copies\RIG_AMIENS_ANONYME.md

# le contrôle de la copie (CA06) : code 0 = rien trouvé, 3 = une valeur réelle, un volume ou un numéro différent
dotnet run --project Outils\Anonymisation -- controler --source "<même source>" --accord RIG_AMIENS --cible "<même copie>"

# l'inventaire (schéma seulement, aucune donnée lue) : ce que la copie ferait de chaque colonne
dotnet run --project Outils\Anonymisation -- inventaire --source "<source>" --accord RIG_AMIENS --sortie D:\copies\inventaire.md
```

Le compte qui lit la source doit avoir, en plus de la lecture, le droit **`VIEW DEFINITION`** sur la base : sans lui,
SQL Server cache les définitions (valeurs par défaut, contraintes, filtres d'index, modules) et la copie n'aurait pas la forme
de la source ; l'outil refuse alors avant de créer la copie (mesuré sur RIG_LE_PUY le 2026-11-23 : 1 251 valeurs par défaut,
24 contraintes, 190 filtres d'index et 305 modules cachés ; la copie, avant ce refus, plantait après 36 min de données).

Options : `--tables A,B` (sous-ensemble), `--max-lignes N` (copie réduite **fermée sur ses clés étrangères**, voir
« La copie réduite, fermée sur ses clés »), `--regles <fichier>` (relecture du
délégué, une ligne `TABLE.COLONNE=Garder|Mots|Courriel|Fichier|Telephone|DateNaissance|Vider|Xml|Document`), `--graine <texte>`
(valeurs reproductibles, **refusée** pour une source hors de ce poste).

## Correction du 2026-11-21 (refus du chef à la première réception)

<!-- fix-ok: refus du chef rejoué sur main 2e28acc puis mesuré sur RIG_DEV par la porte de lecture : « ETAT » levait les
     prénoms d'état civil (80-96 % de valeurs à prénom) et CA06 ne tirait que les colonnes traitées. -->

**Le défaut.** La règle par le nom prenait « ETAT » pour un segment technique : les colonnes
`*_PRENOM(S)_ETAT_CIVIL(_ANC)` restaient en clair. Compté par le chef sur RIG_DEV : 173 812 prénoms remplis dans
`CASIER_JUDICIAIRE`, 77 635 dans `INSCRIT_PP`, 74 535 dans `BENEFICIAIRE_EFFECTIF`, 6 183 dans `REPRESENTANT_PM`,
et les mêmes colonnes dans `BENEFICIAIRE_EFFECTIF_HISTO`, `BODACC_RCS_XML`, `NOTIFICATION_RCS_XML_DETAIL`. Le banc
restait vert : son extrait n'avait pas ces tables, et le contrôle CA06 ne tirait que les colonnes déjà traitées —
il suit les mêmes règles que la copie, donc il ne peut pas voir une règle fausse.

**Une copie faite avant cette correction garde ces prénoms en clair** (commit `2f1d970`) : elle est à refaire, elle
ne sort pas du poste et ne sert pas de jeu de répétition (décision du chef).

**Mesuré sur RIG_DEV avant de corriger** (porte de lecture, empreintes salées : aucune valeur sortie). Pour chacune
des 174 colonnes « gardées, à relire », la part de ses valeurs qui portent un prénom fréquent de la base :

| Colonnes | Part de valeurs à prénom | Décision |
|---|---:|---|
| les 11 `*_PRENOM(S)_ETAT_CIVIL(_ANC)` | 80 à 96 % (les 6 plus remplies, RIG_DEV entier) ; 52 à 100 % sur l'extrait du banc (9) | **traitées** : « ETAT » suivi de « CIVIL » n'est plus technique (composé `ETAT_CIVIL`) ; `NTXDT_PRENOMS_ETAT_CIVIL_DT`, une date de changement, reste gardée |
| les 38 `*NOM*FICHIER*` | 7 portent des noms de personnes ou d'affaires (jusqu'à 24 % de prénoms, 37 % de noms : `CRTRA_NOM_FICHIER`, `DMCOPJ_NOM_FICHIER`, `PRCLL_NOM_AFFAIRE_FICHIER`, `GDRG_NOM_FICHIER`…), 19 des noms structurés (`a9999a9999….pdf`) ou des libellés fixes, 12 sont vides | **toutes traitées**, extension connue gardée (`Fichier`) : le contenu change d'un greffe à l'autre, et un nom structuré garde sa forme |
| `GPIMP/TAGPS/LOT_NOM_IMPRIMANTE`, `IMPED_NOM_LOGIQUE` | 66 à 73 % — 4 noms d'imprimantes sur 26 portent le nom d'un agent | **traitées** (composé `NOM_IMPRIMANTE`), la même règle des quatre côtés : le lien entre elles tient |
| `INSPM/DRGPM_DENOMINATION_STATUT` | 25 % de prénoms, 61 % de noms | **traitées** (composé `DENOMINATION_STATUT` : « statutaire ») |
| `ETBLS_NOM_DOMAINE` | sites des établissements (le nom de la société s'y lit) | **traitée** (composé `NOM_DOMAINE`) ; `ETBLS_ETAT_NOM_DOMAINE` (3 codes) reste gardée |
| `INFO_GREFFE.PWD_SQL` | vide sur RIG_DEV ; c'est un mot de passe | **vidée** : un secret n'est jamais levé par un segment technique (« SQL ») |
| 20 entiers et indicateurs (`*_ID_ADRESSE_*`, `*_MAIL_FORMAT_*`…) | ne portent pas de texte | sortis de la liste « à relire » (une clé, un bit ne portent pas de nom) ; un téléphone rangé dans un nombre y reste |
| les 98 autres (`TYPE_VOIE`, `NOM_LOGIQUE`, `TEXTE_PARAMETRE`, `CODE_*`…) | 15 % au plus (`LGEDT_TEXTE_PARAMETRE`) | gardées, toujours « à relire » ; le contrôle les mesure à chaque copie |

**Le contrôle voit désormais une règle fausse.** CA06 tire aussi chaque colonne **gardée** dont le nom a un segment
personnel (voir « Le contrôle automatique ») ; une colonne au-delà de 35 % de prénoms le rend rouge, même sans
« fuite » par les colonnes traitées. Réinjectée dans le code, l'ancienne règle fait rougir le banc (14 échecs, dont
le contrôle de la copie et la mesure de RIG_DEV entier).

**Deuxième correction : le tirage d'une grande table** (objection de la relecture, même jour). Le banc rougissait au
hasard : `.\tests.ps1 SocleAnonymisation` a rendu 1 sur un passage, `DEMANDE_EDITION.DMNED_CODE_ENTITE_DESTINATAIRE`
signalée à 50 %. Cause mesurée : cette table a 4 300 504 lignes, mais la colonne n'a que 82 valeurs distinctes ; des
pages tirées au hasard n'en rendent que les plus fréquentes (4 à 9 par passage), et un seul mot à prénom y faisait
12 à 50 %. Le seuil s'appliquait sans nombre minimum de valeurs. Désormais, une colonne qui sort de ses pages avec
moins de 200 valeurs est **relue sur toute la table** : soit elle rend 200 valeurs tirées parmi toutes ses
distinctes, soit elle les rend toutes, et la mesure devient exacte, sans hasard (`MesureGardee.Toutes`). Sur
RIG_DEV, 19 colonnes de grandes tables sont ainsi lues en entier ; les autres relues rendent 200 valeurs.
`DMNED_CODE_ENTITE_DESTINATAIRE` est lue en entier : 82 sur 82, 4 % à chaque passage. Ce que ça coûte : la mesure de RIG_DEV entier passe de 12 s à 32-44 s (6 s au
plus par colonne relue). Une relecture parcourt toute la table : sur une base dix fois plus grosse que RIG_DEV,
compter environ dix fois plus (estimation, non mesurée). Le banc `tests\SocleAnonymisationTirageCheck` le garde :
une colonne de grande table jugée sur moins de 200 valeurs doit l'être sur toutes ses distinctes, qu'il compte à
part. L'entrée qui le fait rougir si la correction est fausse : `DMNED_CODE_ENTITE_DESTINATAIRE`. Sans la
relecture en entier, il rend 1 (`DMNED_CODE_ENTITE_DESTINATAIRE 7 sur 82`, 19 colonnes jugées sur un tirage
maigre) ; avec, il rend 0 (82 sur 82). Son jumeau : une colonne qui a 200 valeurs ou plus reste jugée sur 200
valeurs tirées, sans relire la table.

## Les sources V2 vérifiées avant d'écrire

| Source | Ce qu'elle a appris |
|---|---|
| `CLIENT.sql:4-5,20-22,33` | nom, prénom, courriel, téléphones, date de naissance, mot de passe web (`CLIEN_MDP`), bloc-notes |
| `UTILISATEUR.sql:10-11` | nom, prénom des agents ; aussi `UTLST_LOGIN` (index unique `IFK_UTLST_LOGIN`), `UTLST_PASSWORD`, téléphone |
| `AUDIT.sql:8-9` + RIG_DEV | `AUDIT_XML` (`NOT NULL`) et `AUDIT_XML_NEW` : `<deleted COL="…" …/>`, **un attribut par colonne** de la table journalisée (`AUDIT_TABLE`) ; 822 599 lignes, surtout FACTURE (521 804) |
| `EMAIL_ENVOI_TRACE.sql:14-18` | expéditeur, destinataires, copie cachée, réponse, objet ; aussi ligne de commande et message d'erreur |
| `ADRESSE_BASE.sql`, `PARTIE.sql`, `DIRIGEANT_PP.sql` | les colonnes de la story, plus pièce d'identité, lieu de naissance, textes libres |
| RIG_DEV `FACTURE`, `ENCAISSEMENT` | la facture recopie le client (`FACTU_DESIGNATION_FACTURATION`, adresses) et elle est **signée** (`FACTU_SIGN_SEQ`, `FACTU_SIGN_ELEC`) : 3 000 factures récentes sur 3 000 signées ; le nom du client y figure mot pour mot (3 000 sur 3 000) |
| RIG_DEV `sys.foreign_key_columns` | 155 clés étrangères en texte ; une seule traitée d'un seul côté par les règles : `DEMANDE_EDITION.DMNED_LOT → LOT.LOT_NOM` |
| RIG_DEV, schéma | 860 tables, 15 590 colonnes, SQL Server 14.0.3485 (2017), niveau 140, `SQL_Latin1_General_CP1_CI_AS` ; des `tinyint`, `datetime` et `bit` y portent un classement (`TYPE_BIEN.R_TYPEBIEN_REC_NIV`…) |

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **Remplacement pendant le transfert** (lecture, remplacement en mémoire, écriture par lots), jamais « copie puis mise à jour » | Une valeur réelle de production n'arrive jamais sur l'instance de test, même un instant (C17, RGPD art. 32). |
| **Permutation à clé, mot par mot** (Feistel à 10 tours, HMAC-SHA256, sur l'alphabet de chaque position) | Même mot → même faux mot dans toutes les tables : les liens et les recherches restent justes (CA02). Deux mots différents → deux faux mots différents : deux personnes ne fusionnent pas, l'index unique des comptes tient. **Aucun mot ne reste lui-même** (un seul cycle). Même longueur, même forme : majuscule, minuscule, accent, chiffre, ponctuation à leur place (CA01). |
| **Insensible à la casse, sensible aux accents** | Comme le classement de la V2 : « Dupont » et « DUPONT » donnent les mêmes lettres. |
| **Clé aléatoire, effacée en fin de copie** ; pas de table de correspondance | La clé *est* la correspondance (T01 : « détruite après la copie ») : sans elle, personne ne refait le lien. Une graine fixe rendrait la copie réversible : refusée hors de ce poste. |
| **Date de naissance : autre jour de la même année**, heure gardée | L'âge reste vraisemblable pour les tests ; même date → même fausse date. |
| **Téléphone : chiffres permutés ensemble, deux premiers gardés** | « 06 12 34 56 78 » et « 0612345678 » donnent les mêmes chiffres ; fixe et portable restent reconnaissables. |
| **Courriel : extension gardée si elle a 2 à 4 lettres** (fr, com, info) | L'adresse reste une adresse. Une extension plus longue (`.avocat`, `.paris`, fautes de saisie : 10 adresses sur 2 000 dans RIG_DEV) peut désigner quelqu'un : remplacée. |
| **Numéros, dates des affaires, montants, identifiants : gardés** ; identités reprises à la dernière valeur de la source | Les répétitions mesurent les vraies durées et les vrais compteurs (CA03). |
| **Commune, code postal, pays, codes : gardés** | Seuls, ils ne désignent personne ; le routage et la géographie des tests restent vrais. Numéro et nom de voie sont remplacés. |
| **Champs libres : anonymisés mot à mot** (longueur gardée) ; **secrets vidés** (mots de passe) | Le volume des textes reste (CA03). Le rapport dit pour chacun : « anonymisé » ou « vidé » (CA04). |
| **XML du journal : chaque attribut prend la règle de sa colonne** dans la table que nomme `AUDIT_TABLE` | Le journal garde sa forme et ses numéros ; la désignation du client y reçoit la même valeur fictive que dans la facture (CA02, CA04). |
| **Règles : un catalogue colonne par colonne, puis le nom de la colonne** (segments NOM, PRENOM, MAIL, TEL, NAISSANCE, ADRESSE, TEXTE… ; levés par CODE, TYPE, ROLE… **sauf dans un composé** : `ETAT_CIVIL`, `DENOMINATION_STATUT`, `NOM_DOMAINE`, `NOM_IMPRIMANTE` ; un secret — PWD, MDP, PASSWORD — n'est **jamais** levé) | 1 057 colonnes traitées sur les 15 590 de RIG_DEV ; ce qui ressemble à une donnée personnelle est **traité par défaut** (on préfère abîmer un libellé que laisser passer un nom), et listé « à relire ». |
| **Nom de fichier** (un segment de nom avec « FICHIER ») : mots remplacés, **extension connue gardée** (`.pdf`, `.doc(x)`, `.xml`, `.txt`, `.zip`, `.sql`, `.html`, `.dat`, `.tif`…, liste mesurée sur RIG_DEV) | Le fichier reste du même genre. Liste fermée : une fin « .JEAN » n'est pas une extension, elle est remplacée. |
| **Les deux côtés d'une clé étrangère prennent la même règle** | Sinon le lien casse (mesuré : `DMNED_LOT` gardé, `LOT_NOM` remplacé, la clé ne se vérifiait plus). Le côté anonymisé l'emporte. |
| **La copie a la forme de la source** : colonnes, classements, identités, défauts, index, clés étrangères (vérifiées quand la source les vérifie), contraintes, vues, fonctions, procédures, déclencheurs | Les tests et les répétitions tournent sur la vraie structure. Un module qui ne se crée pas (il dépend d'un serveur lié, d'une colonne disparue) est listé, sans arrêter la copie. |
| **Marque « chaînes invalidées par l'anonymisation »** dès qu'une ligne signée change (CA05) | La signature couvre le nom du client (fiche team #1316). La marque est dans `anonymisation.Copie`, dans la propriété de base `RIGV3_ANONYMISATION` et en tête du rapport. |
| **Lecture seule de la source** : accord par base, `ApplicationIntent=ReadOnly`, chaque requête par `LectureV2.ExigerLecture` (la porte de la reprise, liée et non recopiée) ; copie **sur ce poste** seulement (`BaseJetable.ExigerServeurLocal`), base neuve ou vide, jamais la source | CA07. Les refus tombent **avant** toute connexion. Le rapport note les écritures recensées par le serveur source avant et après. |
| **Document (colonne binaire) : document fictif du même format, à la même taille** ; NULL reste NULL ; la source ne rend que le format et la taille (2026-11-23) | Un Kbis, un acte, un flux EDI portent des noms : aucun octet réel ne doit arriver dans la copie, même un instant. Même format et même taille gardent les volumes (CA03) et ce que les tests ouvrent (une archive reste une archive, un PDF s'ouvre). Voir « Les documents ». |
| **Copie réduite fermée sur ses clés étrangères** (lot 18), clés posées **avec** vérification | Les N premières lignes de chaque table ne tiennent pas ensemble : au lot 8, la répétition sur RIG_DEV s'arrêtait dans la reprise des droits (un rôle visait une fonction non copiée). Fermée, une copie réduite a des liens justes comme une copie complète, et la vérification des clés par SQL Server le prouve à chaque copie. |

## La copie réduite, fermée sur ses clés (lot 18, `Outils\Anonymisation\Fermeture.cs`)

`--max-lignes N` copie les N premières lignes de chaque table (dans l'ordre de la clé primaire ; une table sans clé
primaire — 104 dans RIG_DEV — dans l'ordre du serveur, comme au lot 8), puis **toutes les lignes parentes qu'elles
visent, de proche en proche**, jusqu'à ce qu'aucune clé étrangère ne pointe dans le vide (le modèle est l'extrait du
banc du transfert, `tests\RecopieTransfertCheck\ExtraitRigDev.cs`) :

1. Pendant la copie, chaque ligne écrite est notée par ses valeurs **source** (avant anonymisation) : ce qui l'identifie
   (clé primaire, colonnes visées par une clé) et ce qu'elle vise.
2. Tour par tour, pour chaque clé étrangère entre tables copiées (789 dans RIG_DEV, dont 2 à deux colonnes et 18 vers
   leur propre table), les valeurs visées absentes du parent copié sont relues dans la source (SELECT seul, paquets de
   2 000 paramètres au type de la colonne : l'index de la source sert), et ces lignes parentes sont copiées
   **anonymisées** comme les autres ; une ligne déjà copiée n'est jamais réécrite.
3. Un tour sans ajout arrête : chaque tour ajoute une ligne de la source ou demande une valeur jamais demandée, les deux
   sont en nombre fini. Les valeurs se comparent comme SQL Server les compare (blancs finaux ignorés, majuscules et
   accents selon le classement de la colonne visée).
4. Les clés de la copie sont alors posées **avec vérification** : une fermeture incomplète serait une anomalie de la
   copie, et la répétition s'arrêterait à l'anonymisation. Le rapport dit les lignes ajoutées par table, les tours et
   la durée.

Le contrôle de la copie (`controler`) reste exigé avant usage. Sur une copie réduite, ses volumes diffèrent par
construction ; la répétition les juge sur la réduction : chaque table a min(source, N) lignes **plus** celles que la
fermeture lui a ajoutées (`Repetition.JugerControle`), pas une de plus.

## Le contrôle automatique (CA06)

Il tire au hasard des lignes de la **source** qui portent des données personnelles (le catalogue appliqué au
schéma de la source, **pas** la liste que la copie tient d'elle-même : une colonne oubliée par la copie est
contrôlée quand même) et cherche leurs valeurs réelles **à leur place** dans la copie (même clé, même colonne ;
dans un XML, même attribut personnel) : valeur entière, puis chaque mot de 5 caractères et plus.

- **Sans sortir les valeurs** : chaque serveur calcule `HASHBYTES('SHA2_256', sel + valeur)` avec la même
  expression ; la source ne rend que des empreintes de 32 octets, des clés et des nombres (le banc le vérifie).
  Le sel est tiré au hasard, puis oublié.
- **Pourquoi « à leur place » et pas « partout »** : mesuré sur RIG_DEV, la recherche partout donnait
  6 faux positifs — une fausse date de naissance tombe forcément sur la vraie date d'un autre, et un mot de
  banque ou de commune gardé exprès partage un mot avec un nom de client. À sa place, une valeur restée en
  clair est forcément trouvée ; une coïncidence ne peut pas y tomber.
- **Tirage** : toute la table jusqu'à 50 000 lignes, au-delà des pages au hasard (`TABLESAMPLE`) ; une requête
  par table. Avant ce choix, le contrôle ne finissait pas en 10 min sur RIG_DEV. Pour les colonnes gardées
  (ci-dessous), une colonne dont les pages rendent moins de 200 valeurs distinctes est relue sur toute la table.
- **Les colonnes gardées à segment personnel** (refus du 2026-11-21) : le contrôle suit les mêmes règles que la
  copie, donc une règle qui garde à tort une colonne de prénoms lui échappait. Il tire aussi chaque colonne de texte
  **gardée** dont le nom a un segment personnel (segment technique, décision du catalogue ou remplacement
  `--regles`), 200 valeurs distinctes tirées au hasard, ou **toutes** si elle en a moins, et compte celles qui
  portent un **prénom fréquent de la base**. Le
  lexique vient des colonnes dont le nom porte PRENOM(S), **quelle que soit leur règle** ; un mot n'y compte que
  s'il est vu 3 fois dans les prénoms d'au moins **2 autres tables** (mesuré : « SUPPLEANT », « DIRECTEUR »,
  « GRENOBLE » sont saisis dans des prénoms de RIG_DEV, chacun dans une seule ou peu de tables). Au-delà de **35 %**
  la colonne est rouge : mesuré le 2026-11-21 (8 passages sur RIG_DEV entier, 5 sur l'extrait), les colonnes
  gardées de RIG_DEV montent à 15 % au plus, les prénoms d'état civil gardés par l'ancienne règle sont à 80-96 %
  (52 à 100 % sur l'extrait du banc, dont le lexique est plus petit). D'un passage à l'autre, une colonne jugée sur
  200 valeurs bouge de 6 points au plus ; une colonne lue en entier ne bouge pas. Une
  colonne rouge se **traite** (`--regles TABLE.COLONNE=Mots`) : la garder ne passe pas le contrôle.
  **Limite** : le lexique voit les prénoms ; une colonne gardée qui ne porterait que des noms de famille
  n'atteindrait pas le seuil.
- **Les documents** (2026-11-23) : dans chaque colonne « document », des documents de la source sont tirés (par
  leur clé, puis leur empreinte `HASHBYTES` salée, calculée sur la source) ; aucun ne doit se retrouver dans la copie,
  ni à sa place ni ailleurs dans la colonne, et à sa place la copie doit avoir le même format et la même taille. Une
  colonne de grande table dont les pages au hasard rendent moins de 20 documents est relue en entier
  (`KBIS_XML.KBIS_PDF` : 304 documents sur 1,3 million de lignes). Et comme pour les textes, une règle fausse se voit :
  chaque binaire que les règles **gardent** est tiré, et une seule valeur qui a la signature d'un document (PDF, ZIP,
  TIFF…) ou plus de 1 024 octets le rend rouge. De la source ne sortent que des clés, des empreintes et des
  signatures (« Pdf:16864 »).
- Il vérifie aussi : mêmes volumes, colonnes gardées identiques sur des lignes tirées, secrets vides, marque.

## Les documents (règle des binaires, décidée au lot 18, faite le 2026-11-23)

Une colonne `binary`, `varbinary` ou `image` porte un **document** — GED, Kbis, flux EDI, pièce numérisée — sauf un
**binaire technique** : colonne de clé (primaire, unique, étrangère), ou nom à segment technique (`EMPREINTE`, `HASH`,
`CLE`, `SIGNATURE`, `TOKEN`, `GUID`…) **et** taille déclarée bornée (une empreinte ou une clé a une taille fixe, un
document non). Un document est remplacé ; un binaire technique est gardé, sa raison écrite dans l'inventaire et le
rapport, et le contrôle vérifie qu'il ne porte pas de document (`Regles.cs`, `CatalogueRegles.Binaire`).

1. La source ne rend **que la signature** de chaque document : son format, reconnu à ses premiers octets **par le
   serveur**, et sa taille (`DocumentsFictifs.ExpressionSignature`, `Outils\Anonymisation\Documents.cs`). Aucun octet
   d'un document réel ne sort de la source, même en mémoire. Effet de bord : la copie ne lit plus les gros binaires
   (la lecture quadratique de SqlClient#593 ne les touche plus).
2. La copie reçoit un document **fictif** du même format, complété à la **même taille** : PDF d'une page A4 qui écrit
   « Document anonymisé — copie de travail » (ouvert par le lecteur PDF de Windows, `Windows.Data.Pdf`, le 2026-11-23),
   PostScript qui l'imprime, image blanche (TIFF, PNG, JPEG, GIF, BMP), archive ZIP d'un fichier texte qui le dit
   (intègre au contrôle de `zipfile` de Python), XML, JSON, RTF ou texte qui le disent ; sinon des octets neutres
   derrière la signature du format (OLE2, GZIP), ou des zéros. NULL reste NULL, un document vide reste vide.
3. Le document fictif ne dépend que de la signature : deux documents de même format et de même taille reçoivent le
   même. Il ne contient rien de la source.
4. Le rapport de copie a une section « Documents remplacés » (colonnes, nombre, formats, octets) et la liste des
   binaires gardés avec leur raison (CA04).

Inventaire des colonnes binaires de RIG_DEV (tables utilisateur, `is_ms_shipped = 0`) — formats et tailles mesurés le
2026-11-23 en lecture seule (signatures calculées par le serveur, rien d'autre n'en est sorti) :

| Colonne | Type | Documents (non NULL) | Formats | Dans la copie |
|---|---|---:|---|---|
| `COMMANDE_WEB.CMDWB_FLUX` | varbinary(max) | 3 258 | XML (6 Mo) — commandes web | remplacée |
| `DEMAT_RAPTURE.RAPTU_FLUX` | varbinary(max) | 77 | JSON (6,2 Mo, au plus 0,12 Mo) | remplacée |
| `DEMAT_RPJC_ENR.RPJCE_FLUX` | varbinary(max) | 3 261 | XML (12,8 Mo) | remplacée |
| `DEMAT_RPJC_INS.RPJCI_FLUX` | varbinary(max) | 2 824 | XML (2,6 Mo) | remplacée |
| `DEMAT_RPJC_REN.RPJCR_FLUX` | varbinary(max) | 938 | XML (0,9 Mo) | remplacée |
| `DEMAT_RPJC_TRCNC.RPJCC_FLUX` | varbinary(max) | 1 073 | XML (1,2 Mo) | remplacée |
| `DOCUMENT_OCERISE.DOCOC_FICHIER` | varbinary(max) | 551 | PDF 471 (528 Mo), TIFF 80 (1,6 Go, au plus 96 Mo) — pièces numérisées | remplacée |
| `GED_RIG.GDRG_FICHIER` | image | 134 420 | ZIP 122 367 (3,2 Go, au plus 510 Mo), XML 11 181 (276 Mo), PDF 869 (353 Mo), autre 3 — la GED : actes, Kbis | remplacée |
| `GED_RIG.GDRG_FICHIER_PDF` | varbinary(max) | 0 | — | remplacée |
| `GED_RIG.GDRG_FICHIER_PS` | varbinary(max) | 0 | — | remplacée |
| `KBIS_XML.KBIS_PDF` | varbinary(max) | 304 | PDF (4,3 Mo, au plus 21 Ko) — Kbis | remplacée |
| `sysdiagrams.definition` | varbinary(max) | 0 | — (diagrammes de SSMS) | remplacée |

Les 12 sont des documents ; aucune ne remplit la règle du binaire technique. L'inventaire de l'outil
(`inventaire`, section « Colonnes binaires ») les liste avec leur raison, et le banc vérifie que chaque colonne
binaire de la base y est classée.

## Mesures sur RIG_DEV (lecture seule, accord « RIG_DEV », 2026-11-21)

| Mesure | Résultat |
|---|---|
| Inventaire (`inventaire`) | 15 590 colonnes, 1 057 traitées, 1 029 à relire dont 98 gardées (avant la correction du 2026-11-21 : 1 000 traitées, 174 gardées à relire) |
| Colonnes gardées à segment personnel, mesurées (banc, `MesurerGardeesAsync`) | 32 à 44 s : 69 colonnes remplies, 5 254 valeurs, lexique de 2 487 à 2 501 prénoms tirés de 42 colonnes ; 19 colonnes de grandes tables lues en entier ; la plus haute à 15 % ; avec l'ancienne règle, les prénoms d'état civil à 80-96 % |
| Copie réduite des 860 tables (`--max-lignes 20`) | 83 s, aucune anomalie : 1 893 index, 789 clés étrangères (posées sans vérification : copie réduite), 468 vues, fonctions, procédures et déclencheurs ; 8 modules non refaits parce qu'ils sont cassés dans RIG_DEV même (serveur lié `RIGBD4`, colonnes `*_ID_SSADR` disparues) |
| Contrôle de cette copie (`controler --echantillon 100`) | 180 s pour les 860 tables : 1 000 colonnes personnelles, 34 236 valeurs et 104 733 mots réels tirés (empreintes), aucun retrouvé ; 29 tables sans clé primaire d'une colonne non tirées (listées). **Limite** : dans une copie réduite à 20 lignes par table, la plupart des lignes tirées n'y sont pas — ce passage prouve la durée et la tenue du contrôle sur le vrai schéma, pas l'absence de fuite (c'est le banc qui la prouve, sur une copie complète de son extrait). Volumes et lignes gardées diffèrent par construction (code 3). |
| Écritures du serveur sur RIG_DEV pendant la copie | 6 861 285 avant, 6 861 295 après : d'autres sessions écrivent sur RIG_DEV ; l'outil, lui, n'y joue que des SELECT (869 requêtes, toutes par la porte) |
| Copie réduite **fermée** des 860 tables (`--max-lignes 200`, lot 18, 2026-11-22) | **282,8 s**, aucune anomalie : 109 342 premières lignes, puis **109 025 lignes parentes** ajoutées dans 112 tables en **7 tours (180,3 s)** ; les **789 clés étrangères posées avec vérification**, 0 sans ; 2,0 Go sur le SQL du poste (les documents de `GED_RIG` visés par les éditions) |

Deux défauts de la copie, vus en fermant RIG_DEV et corrigés dans `EcrireAsync` (`Copie.cs`) : la lecture **asynchrone**
d'une ligne à gros binaire est quadratique dans SqlClient ([dotnet/SqlClient#593](https://github.com/dotnet/SqlClient/issues/593),
ouvert) — 4 lignes de `GED_RIG` (`GDRG_FICHIER`, `image`) en 137 s, client à 100 % d'un cœur ; lecture synchrone : 4 lignes
en 7 s. Et un lot n'était borné qu'en lignes : au premier passage, le processus de copie est monté à **4,3 Go** pendant
la fermeture de `GED_RIG` (relevé du processus, arrêté là ; une lecture de fermeture y rapporte au plus 2 000 lignes) ;
un lot s'écrit maintenant aussi dès 64 Mo, et les passages suivants sont restés entre 255 et 640 Mo. Le plafond est
arrivé au deuxième passage, la lecture synchrone au troisième : ces relevés ne séparent pas l'effet de l'un et de l'autre.

**Défaut relevé au lot 18, corrigé le 2026-11-23** : les colonnes **binaires** n'étaient jamais traitées
(`CatalogueRegles.Pour` ne traitait que les textes et le XML) ; les Kbis et les actes passaient **en clair** — 1 367
documents (531 Mo) dans la copie réduite fermée de RIG_DEV. Règle décidée par le chef et faite : chaque document
devient un document fictif du même format et de la même taille (« Les documents », plus haut). Une copie faite
avant le 2026-11-23 porte des documents réels : elle ne sert plus, on la refait.

## Le banc (`tests\SocleAnonymisationCheck`)

Il lit un extrait **cohérent** de RIG_DEV (440 factures, récentes ou commentées, avec leurs clients et leur
journal de 2 217 lignes, 684 parties avec leurs clients et adresses, 300 dirigeants, les 243 agents, 800 courriels
et traces, les 61 lots d'éditions et 300 demandes qui les citent ; depuis le 2026-11-21, 1 500 lignes à prénoms
d'état civil de chacune des 7 tables du refus et 1 500 noms de fichiers de `CREANCE_TRANSITOIRE` ; depuis le
2026-11-23, 40 documents de la GED et 20 Kbis en PDF), en fait une base
V2 jetable avec les vraies clés étrangères de RIG_DEV (11, vérifiées), la copie avec l'outil, puis compare source
et copie ligne à ligne. Il mesure aussi RIG_DEV entier (colonnes gardées). Environ 2 min 30.

Ses commentaires gardent les mesures de son premier passage, faites avant la relecture des colonnes maigres
(par exemple « 79-100 % ») : ce fichier est figé tel que la relecture l'a relu. Les chiffres qui font foi sont
ceux de cette page.

Le second banc, `tests\SocleAnonymisationTirageCheck`, lit RIG_DEV en lecture seule et n'écrit nulle part. Il
mesure les colonnes gardées des 151 tables de plus de 50 000 lignes, puis compte à part les valeurs distinctes de
chaque colonne jugée sur moins de 200 valeurs (27 colonnes, vides comprises). Environ 50 s.

| Critère | Contrôle |
|---|---|
| CA01 | 33 colonnes (noms, prénoms — dont les 8 prénoms d'état civil du refus —, naissances, adresses, courriels, téléphones, noms de fichiers) : aucune valeur restée, même longueur, même forme, @ et extension, indicatif, extension de fichier ; mots de passe vides ; 29 règles mesurées sur RIG_DEV vérifiées au point d'appel, avec leurs jumeaux gardés ; sur RIG_DEV entier, aucune colonne gardée ne porte de prénoms ; et les valeurs fictives elles-mêmes (bijection sans point fixe sur tous les mots de 2 lettres, casse, accents, dates, téléphones, fichiers) |
| CA02 | nom du client dans la désignation de ses 440 factures ; désignation identique dans le journal XML (2 113 lignes) ; compte de l'agent dans le journal ; aucun nom fusionné ; les 8 clés étrangères refaites et vérifiées, dont la clé en texte `DMNED_LOT → LOT_NOM` |
| CA03 | mêmes volumes ; 278 colonnes gardées identiques ligne à ligne ; compteurs d'identité à la même valeur |
| CA04 | 16 champs libres nommés (XML du journal, destinataires, copie cachée, objet et ligne de commande des traces, corps de courriels, notes, textes de partie, commentaire de facture) : aucune valeur intacte, chacun listé au rapport ; le XML garde sa forme. L'extrait prend exprès des lignes où ils sont remplis ; `EMTRC_REPLY` et `DRGPP_TEXTE_RPPP` restent vides (le second dans tout RIG_DEV) : le banc le dit |
| CA05 | 406 factures signées au client anonymisé → marque « chaînes invalidées par l'anonymisation » (table, propriété, rapport) ; jumeau : une copie sans ligne signée porte « chaînes intactes » |
| CA06 | le contrôle ne trouve rien, et ne sort de la source que des empreintes, des clés, des nombres et des signatures de documents (format et taille) ; il tire aussi les colonnes gardées à segment personnel (aucune à 35 % de prénoms) ; **contre-épreuves rouges** : `CLIEN_NOM` laissé en clair → le contrôle le trouve ; copie **et** contrôle avec l'ancienne règle sur les 9 prénoms d'état civil → aucune fuite par les colonnes traitées, mais le contrôle les trouve gardées (52 à 100 %) ; même chose sur RIG_DEV entier (80-96 %) ; même graine → mêmes valeurs. Second banc : une colonne de grande table jugée sur moins de 200 valeurs l'est sur toutes ses distinctes (`DMNED_CODE_ENTITE_DESTINATAIRE` 82 sur 82), et une colonne qui en a 200 ou plus reste tirée |
| Documents (CA01, CA03, CA04, CA06 ; 2026-11-23) | « **documents remplacés : aucun document de la source retrouvé, même format, même taille** » : le banc compte **lui-même**, source et copie étant sur le même serveur, 60 documents (38 ZIP, 2 XML, 20 PDF) : empreinte SHA-256 à sa place 0, ailleurs dans la copie 0 ; taille différente 0 ; chaque document de la copie **ouvert** dans le format de la source (archive ZIP, XML chargé, table xref du PDF suivie) ; 80 NULL restés NULL ; colonnes listées au rapport. Le contrôle de l'outil tire les 60 et n'en retrouve aucun. **Contre-épreuve rouge** : règle des binaires retirée (`Garder` sur `GDRG_FICHIER` et `KBIS_PDF`) → le banc retrouve 60 documents sur 60, le contrôle de l'outil aussi, et avec la règle fausse il voit les documents dans les binaires gardés. L'inventaire de RIG_DEV classe ses 12 colonnes binaires, chacune avec sa raison |
| CA07 | aucune écriture sur la source pendant la copie (journal du serveur et empreinte du contenu) ; refus avant connexion d'une copie vers SQL-DEV ; refus de RIG_DEV sans accord, avec l'accord d'une autre base, avec une graine ; jumeau accepté : l'inventaire avec l'accord ; la porte refuse un UPDATE ; refus d'écrire sur la source ou sur une base déjà remplie ; une source qui **cache ses définitions** (compte de lecture sans `VIEW DEFINITION`) est refusée avant toute base copie — contre-épreuve : refus retiré du code, la copie passe en code 0 en perdant la vue (rouge) |
| REC-008 (lot 18) | « **copie réduite fermée : aucune clé étrangère orpheline** » : 20 premières lignes par table, puis les lignes parentes (26 : `ADRESSE_BASE` +17, `UTILISATEUR` +9, en 2 tours) ; le banc compte **lui-même**, clé par clé de la copie, les lignes dont la clé est remplie sans parent : 0 sur les 11 clés, toutes vérifiées, volumes = min(source, 20) + ajouts ; **contre-épreuve** : la réduction non fermée laisse 74 lignes orphelines sur 4 clés ; la copie fermée passe le contrôle de l'outil (aucune valeur réelle retrouvée). Fermeture retirée du code (défaut réinjecté) : ce contrôle rougit |

Les corrections faites en route sont gardées par le banc : réinjectées, elles le font rougir
(alignement des clés retiré : 4 rouges ; extensions longues gardées : 2 rouges ; composé `ETAT_CIVIL` retiré, le
2026-11-21 : 14 rouges ; tirage par pages sans relecture, le même jour : 2 rouges au second banc, 19 colonnes
jugées sur un tirage maigre ; règle des binaires retirée du code — `CatalogueRegles.Binaire` qui garde tout —, le
2026-11-23 : 4 rouges, code 1).

## Ce qui ne se fait pas depuis ce poste

| Reste | Pourquoi | Quand |
|---|---|---|
| T03 : copie de RIG_LE_PUY (accord donné le 2026-11-23, puis **remise à plus tard** le même jour : « on touche pas à la prod pour le moment », tout se fait sur RIG_DEV en attendant le changement de base) | Le compte Windows de ce poste (`INTRANET\raphael.vilain`, lecteur de la base, pas propriétaire) lit RIG_LE_PUY (SQL-PROD\PROD) mais n'y a pas `VIEW DEFINITION` : l'outil refuse. Le droit se donne par un compte d'administration SQL : `USE RIG_LE_PUY; GRANT VIEW DEFINITION TO [INTRANET\raphael.vilain];` (lecture des définitions seulement ; `REVOKE` après la copie). | au changement de base |
| T01 : relecture de l'inventaire par le délégué à la protection des données | Décision humaine. L'inventaire est produit (`inventaire`), ses corrections s'écrivent dans un fichier `--regles` sans toucher au code. | avant la première copie de production |
| T03, CA07 : lancer l'outil base par base sur les 40 greffes | Aucun accord pour une base de production sur ce poste ; l'outil exige `--accord <BASE>` pour chacune. | avant REC-008 |
| T05 sur un serveur de production | D'autres sessions écrivent : le compteur du serveur ne prouve plus seul l'absence d'écriture ; la garantie reste la porte de lecture (le rapport donne les deux). | à chaque copie |
| Écran « à maquetter » du carnet | Le lot livre l'outil en ligne de commande (fichiers prévus par le chef) ; aucun écran V2 n'existe. | si REC-008 ou SOC-025 en demandent un |
| Valeurs en alphabet non latin (cyrillique, grec…) | Gardées telles quelles (hors des alphabets permutés) ; le contrôle ne les tire pas. Aucune dans l'extrait de RIG_DEV. | à relire avec l'inventaire |
