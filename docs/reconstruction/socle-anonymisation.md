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
| Le banc | `tests\SocleAnonymisationCheck` — **`.\tests.ps1 SocleAnonymisation`** (71 contrôles, chacun cite son CA) |

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

Options : `--tables A,B` (sous-ensemble), `--max-lignes N` (copie réduite), `--regles <fichier>` (relecture du
délégué, une ligne `TABLE.COLONNE=Garder|Mots|Courriel|Fichier|Telephone|DateNaissance|Vider|Xml`), `--graine <texte>`
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
| les 11 `*_PRENOM(S)_ETAT_CIVIL(_ANC)` | 80 à 96 % (les 6 plus remplies, RIG_DEV entier) ; 55 à 100 % sur l'extrait du banc (9) | **traitées** : « ETAT » suivi de « CIVIL » n'est plus technique (composé `ETAT_CIVIL`) ; `NTXDT_PRENOMS_ETAT_CIVIL_DT`, une date de changement, reste gardée |
| les 38 `*NOM*FICHIER*` | 7 portent des noms de personnes ou d'affaires (jusqu'à 24 % de prénoms, 37 % de noms : `CRTRA_NOM_FICHIER`, `DMCOPJ_NOM_FICHIER`, `PRCLL_NOM_AFFAIRE_FICHIER`, `GDRG_NOM_FICHIER`…), 19 des noms structurés (`a9999a9999….pdf`) ou des libellés fixes, 12 sont vides | **toutes traitées**, extension connue gardée (`Fichier`) : le contenu change d'un greffe à l'autre, et un nom structuré garde sa forme |
| `GPIMP/TAGPS/LOT_NOM_IMPRIMANTE`, `IMPED_NOM_LOGIQUE` | 66 à 73 % — 4 noms d'imprimantes sur 26 portent le nom d'un agent | **traitées** (composé `NOM_IMPRIMANTE`), la même règle des quatre côtés : le lien entre elles tient |
| `INSPM/DRGPM_DENOMINATION_STATUT` | 25 % de prénoms, 61 % de noms | **traitées** (composé `DENOMINATION_STATUT` : « statutaire ») |
| `ETBLS_NOM_DOMAINE` | sites des établissements (le nom de la société s'y lit) | **traitée** (composé `NOM_DOMAINE`) ; `ETBLS_ETAT_NOM_DOMAINE` (3 codes) reste gardée |
| `INFO_GREFFE.PWD_SQL` | vide sur RIG_DEV ; c'est un mot de passe | **vidée** : un secret n'est jamais levé par un segment technique (« SQL ») |
| 20 entiers et indicateurs (`*_ID_ADRESSE_*`, `*_MAIL_FORMAT_*`…) | ne portent pas de texte | sortis de la liste « à relire » (une clé, un bit ne portent pas de nom) ; un téléphone rangé dans un nombre y reste |
| les 98 autres (`TYPE_VOIE`, `NOM_LOGIQUE`, `TEXTE_PARAMETRE`, `CODE_*`…) | 14 % au plus (`ENCMT_DESIGNATION_BANQUE`) | gardées, toujours « à relire » ; le contrôle les mesure à chaque copie |

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
`DMNED_CODE_ENTITE_DESTINATAIRE` est lue en entier : 82 sur 82, 4 % à chaque passage. Ce que ça coûte : la mesure de RIG_DEV entier passe de 12 s à 32-40 s (6 s au
plus par colonne relue). Une relecture parcourt toute la table : sur une base dix fois plus grosse que RIG_DEV,
compter environ dix fois plus (estimation, non mesurée). Le banc le garde : une colonne de grande table jugée sur moins de 200 valeurs doit l'être
sur toutes ses distinctes, qu'il compte à part (rouge avant la correction : `DMNED_CODE_ENTITE_DESTINATAIRE 9 sur
82`, et 20 autres colonnes). Six passages de suite du banc sont verts.

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
  la colonne est rouge : mesuré le 2026-11-21 (6 passages sur RIG_DEV entier, 3 sur l'extrait), les colonnes
  gardées de RIG_DEV montent à 14 % au plus, les prénoms d'état civil gardés par l'ancienne règle sont à 80-96 %
  (55 à 100 % sur l'extrait du banc, dont le lexique est plus petit). D'un passage à l'autre, une colonne jugée sur
  200 valeurs bouge de 5 points au plus ; une colonne lue en entier ne bouge pas. Une
  colonne rouge se **traite** (`--regles TABLE.COLONNE=Mots`) : la garder ne passe pas le contrôle.
  **Limite** : le lexique voit les prénoms ; une colonne gardée qui ne porterait que des noms de famille
  n'atteindrait pas le seuil.
- Il vérifie aussi : mêmes volumes, colonnes gardées identiques sur des lignes tirées, secrets vides, marque.

## Mesures sur RIG_DEV (lecture seule, accord « RIG_DEV », 2026-11-21)

| Mesure | Résultat |
|---|---|
| Inventaire (`inventaire`) | 15 590 colonnes, 1 057 traitées, 1 029 à relire dont 98 gardées (avant la correction du 2026-11-21 : 1 000 traitées, 174 gardées à relire) |
| Colonnes gardées à segment personnel, mesurées (banc, `MesurerGardeesAsync`) | 32 à 40 s : 69 colonnes remplies, 5 254 valeurs, lexique de 2 487 à 2 501 prénoms tirés de 42 colonnes ; 19 colonnes de grandes tables lues en entier ; la plus haute à 14 % ; avec l'ancienne règle, les prénoms d'état civil à 80-96 % |
| Copie réduite des 860 tables (`--max-lignes 20`) | 83 s, aucune anomalie : 1 893 index, 789 clés étrangères (posées sans vérification : copie réduite), 468 vues, fonctions, procédures et déclencheurs ; 8 modules non refaits parce qu'ils sont cassés dans RIG_DEV même (serveur lié `RIGBD4`, colonnes `*_ID_SSADR` disparues) |
| Contrôle de cette copie (`controler --echantillon 100`) | 180 s pour les 860 tables : 1 000 colonnes personnelles, 34 236 valeurs et 104 733 mots réels tirés (empreintes), aucun retrouvé ; 29 tables sans clé primaire d'une colonne non tirées (listées). **Limite** : dans une copie réduite à 20 lignes par table, la plupart des lignes tirées n'y sont pas — ce passage prouve la durée et la tenue du contrôle sur le vrai schéma, pas l'absence de fuite (c'est le banc qui la prouve, sur une copie complète de son extrait). Volumes et lignes gardées diffèrent par construction (code 3). |
| Écritures du serveur sur RIG_DEV pendant la copie | 6 861 285 avant, 6 861 295 après : d'autres sessions écrivent sur RIG_DEV ; l'outil, lui, n'y joue que des SELECT (869 requêtes, toutes par la porte) |

## Le banc (`tests\SocleAnonymisationCheck`)

Il lit un extrait **cohérent** de RIG_DEV (440 factures, récentes ou commentées, avec leurs clients et leur
journal de 2 217 lignes, 684 parties avec leurs clients et adresses, 300 dirigeants, les 243 agents, 800 courriels
et traces, les 61 lots d'éditions et 300 demandes qui les citent ; depuis le 2026-11-21, 1 500 lignes à prénoms
d'état civil de chacune des 7 tables du refus et 1 500 noms de fichiers de `CREANCE_TRANSITOIRE`), en fait une base
V2 jetable avec les vraies clés étrangères de RIG_DEV (11, vérifiées), la copie avec l'outil, puis compare source
et copie ligne à ligne. Il mesure aussi RIG_DEV entier (colonnes gardées). Environ 2 min 30.

| Critère | Contrôle |
|---|---|
| CA01 | 33 colonnes (noms, prénoms — dont les 8 prénoms d'état civil du refus —, naissances, adresses, courriels, téléphones, noms de fichiers) : aucune valeur restée, même longueur, même forme, @ et extension, indicatif, extension de fichier ; mots de passe vides ; 29 règles mesurées sur RIG_DEV vérifiées au point d'appel, avec leurs jumeaux gardés ; sur RIG_DEV entier, aucune colonne gardée ne porte de prénoms ; et les valeurs fictives elles-mêmes (bijection sans point fixe sur tous les mots de 2 lettres, casse, accents, dates, téléphones, fichiers) |
| CA02 | nom du client dans la désignation de ses 440 factures ; désignation identique dans le journal XML (2 113 lignes) ; compte de l'agent dans le journal ; aucun nom fusionné ; les 8 clés étrangères refaites et vérifiées, dont la clé en texte `DMNED_LOT → LOT_NOM` |
| CA03 | mêmes volumes ; 278 colonnes gardées identiques ligne à ligne ; compteurs d'identité à la même valeur |
| CA04 | 16 champs libres nommés (XML du journal, destinataires, copie cachée, objet et ligne de commande des traces, corps de courriels, notes, textes de partie, commentaire de facture) : aucune valeur intacte, chacun listé au rapport ; le XML garde sa forme. L'extrait prend exprès des lignes où ils sont remplis ; `EMTRC_REPLY` et `DRGPP_TEXTE_RPPP` restent vides (le second dans tout RIG_DEV) : le banc le dit |
| CA05 | 406 factures signées au client anonymisé → marque « chaînes invalidées par l'anonymisation » (table, propriété, rapport) ; jumeau : une copie sans ligne signée porte « chaînes intactes » |
| CA06 | le contrôle ne trouve rien, et ne sort de la source que des empreintes, des clés et des nombres ; il tire aussi les colonnes gardées à segment personnel (aucune à 35 % de prénoms) ; **contre-épreuves rouges** : `CLIEN_NOM` laissé en clair → le contrôle le trouve ; copie **et** contrôle avec l'ancienne règle sur les 9 prénoms d'état civil → aucune fuite par les colonnes traitées, mais le contrôle les trouve gardées (55 à 100 %) ; même chose sur RIG_DEV entier (80-96 %) ; une colonne de grande table jugée sur moins de 200 valeurs l'est sur toutes ses distinctes (`DMNED_CODE_ENTITE_DESTINATAIRE` 82 sur 82) ; même graine → mêmes valeurs |
| CA07 | aucune écriture sur la source pendant la copie (journal du serveur et empreinte du contenu) ; refus avant connexion d'une copie vers SQL-DEV ; refus de RIG_DEV sans accord, avec l'accord d'une autre base, avec une graine ; jumeau accepté : l'inventaire avec l'accord ; la porte refuse un UPDATE ; refus d'écrire sur la source ou sur une base déjà remplie |

Les corrections faites en route sont gardées par le banc : réinjectées, elles le font rougir
(alignement des clés retiré : 4 rouges ; extensions longues gardées : 2 rouges ; composé `ETAT_CIVIL` retiré, le
2026-11-21 : 14 rouges ; tirage par pages sans relecture, le même jour : 1 rouge, 21 colonnes jugées sur un tirage
maigre).

## Ce qui ne se fait pas depuis ce poste

| Reste | Pourquoi | Quand |
|---|---|---|
| T01 : relecture de l'inventaire par le délégué à la protection des données | Décision humaine. L'inventaire est produit (`inventaire`), ses corrections s'écrivent dans un fichier `--regles` sans toucher au code. | avant la première copie de production |
| T03, CA07 : lancer l'outil base par base sur les 40 greffes | Aucun accord pour une base de production sur ce poste ; l'outil exige `--accord <BASE>` pour chacune. | avant REC-008 |
| T05 sur un serveur de production | D'autres sessions écrivent : le compteur du serveur ne prouve plus seul l'absence d'écriture ; la garantie reste la porte de lecture (le rapport donne les deux). | à chaque copie |
| Écran « à maquetter » du carnet | Le lot livre l'outil en ligne de commande (fichiers prévus par le chef) ; aucun écran V2 n'existe. | si REC-008 ou SOC-025 en demandent un |
| Valeurs en alphabet non latin (cyrillique, grec…) | Gardées telles quelles (hors des alphabets permutés) ; le contrôle ne les tire pas. Aucune dans l'extrait de RIG_DEV. | à relire avec l'inventaire |
