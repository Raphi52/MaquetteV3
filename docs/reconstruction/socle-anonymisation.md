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
délégué, une ligne `TABLE.COLONNE=Garder|Mots|Courriel|Telephone|DateNaissance|Vider|Xml`), `--graine <texte>`
(valeurs reproductibles, **refusée** pour une source hors de ce poste).

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
| **Règles : un catalogue colonne par colonne, puis le nom de la colonne** (segments NOM, PRENOM, MAIL, TEL, NAISSANCE, ADRESSE, TEXTE… ; levés par CODE, TYPE, FICHIER, ROLE…) | 1 000 colonnes traitées sur les 15 590 de RIG_DEV ; ce qui ressemble à une donnée personnelle est **traité par défaut** (on préfère abîmer un libellé que laisser passer un nom), et listé « à relire ». |
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
  par table. Avant ce choix, le contrôle ne finissait pas en 10 min sur RIG_DEV.
- Il vérifie aussi : mêmes volumes, colonnes gardées identiques sur des lignes tirées, secrets vides, marque.

## Mesures sur RIG_DEV (lecture seule, accord « RIG_DEV », 2026-11-21)

| Mesure | Résultat |
|---|---|
| Inventaire (`inventaire`) | 15 590 colonnes, 1 000 traitées (888 mots, 51 téléphones, 32 courriels, 19 dates de naissance, 6 vidées, 2 XML), 1 094 à relire |
| Copie réduite des 860 tables (`--max-lignes 20`) | 83 s, aucune anomalie : 1 893 index, 789 clés étrangères (posées sans vérification : copie réduite), 468 vues, fonctions, procédures et déclencheurs ; 8 modules non refaits parce qu'ils sont cassés dans RIG_DEV même (serveur lié `RIGBD4`, colonnes `*_ID_SSADR` disparues) |
| Contrôle de cette copie (`controler --echantillon 100`) | 180 s pour les 860 tables : 1 000 colonnes personnelles, 34 236 valeurs et 104 733 mots réels tirés (empreintes), aucun retrouvé ; 29 tables sans clé primaire d'une colonne non tirées (listées). **Limite** : dans une copie réduite à 20 lignes par table, la plupart des lignes tirées n'y sont pas — ce passage prouve la durée et la tenue du contrôle sur le vrai schéma, pas l'absence de fuite (c'est le banc qui la prouve, sur une copie complète de son extrait). Volumes et lignes gardées diffèrent par construction (code 3). |
| Écritures du serveur sur RIG_DEV pendant la copie | 6 861 285 avant, 6 861 295 après : d'autres sessions écrivent sur RIG_DEV ; l'outil, lui, n'y joue que des SELECT (869 requêtes, toutes par la porte) |

## Le banc (`tests\SocleAnonymisationCheck`)

Il lit un extrait **cohérent** de RIG_DEV (440 factures, récentes ou commentées, avec leurs clients et leur
journal de 2 217 lignes, 684 parties avec leurs clients et adresses, 300 dirigeants, les 243 agents, 800 courriels
et traces, les 61 lots d'éditions et 300 demandes qui les citent), en fait une base V2 jetable avec les vraies
clés étrangères de RIG_DEV (8, vérifiées), la copie avec l'outil, puis compare source et copie ligne à ligne.
71 contrôles, en 36 s.

| Critère | Contrôle |
|---|---|
| CA01 | 24 colonnes (noms, prénoms, naissances, adresses, courriels, téléphones de clients, dirigeants, parties, agents) : aucune valeur restée, même longueur, même forme, @ et extension, indicatif ; mots de passe vides ; et les valeurs fictives elles-mêmes (bijection sans point fixe sur tous les mots de 2 lettres, casse, accents, dates, téléphones) |
| CA02 | nom du client dans la désignation de ses 440 factures ; désignation identique dans le journal XML (2 113 lignes) ; compte de l'agent dans le journal ; aucun nom fusionné ; les 8 clés étrangères refaites et vérifiées, dont la clé en texte `DMNED_LOT → LOT_NOM` |
| CA03 | mêmes volumes ; 278 colonnes gardées identiques ligne à ligne ; compteurs d'identité à la même valeur |
| CA04 | 16 champs libres nommés (XML du journal, destinataires, copie cachée, objet et ligne de commande des traces, corps de courriels, notes, textes de partie, commentaire de facture) : aucune valeur intacte, chacun listé au rapport ; le XML garde sa forme. L'extrait prend exprès des lignes où ils sont remplis ; `EMTRC_REPLY` et `DRGPP_TEXTE_RPPP` restent vides (le second dans tout RIG_DEV) : le banc le dit |
| CA05 | 406 factures signées au client anonymisé → marque « chaînes invalidées par l'anonymisation » (table, propriété, rapport) ; jumeau : une copie sans ligne signée porte « chaînes intactes » |
| CA06 | le contrôle ne trouve rien, et ne sort de la source que des empreintes, des clés et des nombres ; **contre-épreuve rouge** : `CLIEN_NOM` laissé en clair → le contrôle le trouve ; même graine → mêmes valeurs |
| CA07 | aucune écriture sur la source pendant la copie (journal du serveur et empreinte du contenu) ; refus avant connexion d'une copie vers SQL-DEV ; refus de RIG_DEV sans accord, avec l'accord d'une autre base, avec une graine ; jumeau accepté : l'inventaire avec l'accord ; la porte refuse un UPDATE ; refus d'écrire sur la source ou sur une base déjà remplie |

Les deux corrections faites en route sont gardées par le banc : réinjectées, elles le font rougir
(alignement des clés retiré : 4 rouges ; extensions longues gardées : 2 rouges).

## Ce qui ne se fait pas depuis ce poste

| Reste | Pourquoi | Quand |
|---|---|---|
| T01 : relecture de l'inventaire par le délégué à la protection des données | Décision humaine. L'inventaire est produit (`inventaire`), ses corrections s'écrivent dans un fichier `--regles` sans toucher au code. | avant la première copie de production |
| T03, CA07 : lancer l'outil base par base sur les 40 greffes | Aucun accord pour une base de production sur ce poste ; l'outil exige `--accord <BASE>` pour chacune. | avant REC-008 |
| T05 sur un serveur de production | D'autres sessions écrivent : le compteur du serveur ne prouve plus seul l'absence d'écriture ; la garantie reste la porte de lecture (le rapport donne les deux). | à chaque copie |
| Écran « à maquetter » du carnet | Le lot livre l'outil en ligne de commande (fichiers prévus par le chef) ; aucun écran V2 n'existe. | si REC-008 ou SOC-025 en demandent un |
| Valeurs en alphabet non latin (cyrillique, grec…) | Gardées telles quelles (hors des alphabets permutés) ; le contrôle ne les tire pas. Aucune dans l'extrait de RIG_DEV. | à relire avec l'inventaire |
