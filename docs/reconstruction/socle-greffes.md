# Socle — les greffes, leurs bases et leurs réglages (SOC-003, SOC-007, SOC-008)

Lot 4 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-20. Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json` :

| Story | Ce qu'elle donne | Où |
|---|---|---|
| **SOC-007** Déclarer chaque greffe et ses deux bases dans un catalogue commun | `soc.Greffe` (+ son historique) dans la base commune ; la partie serveur ne trouve la base d'un greffe que là | `Base\migrations\SOC-007_catalogue.sql`, `Serveur\Socle\Greffes\CatalogueGreffes.cs`, `RepriseCatalogue.cs` |
| **SOC-008** Créer la base de chaque greffe à côté de sa base actuelle | création d'une base V3 sur l'instance de sa base V2, ses accès, et les contrôles d'exploitation (schéma, sauvegardes, disque, SQL Server 2017) | `Base\migrations\SOC-008_acces.sql`, `Serveur\Socle\Greffes\BaseGreffe.cs` |
| **SOC-003** Retrouver les réglages de son greffe à l'identique | `soc.Reglage`, `soc.ParametreGreffe`, `soc.ModeleCourriel`, `soc.GreffeInfoV2` ; reprise, comparaison valeur par valeur, lecture « comme la V2 » | `Base\migrations\SOC-003_reglages.sql`, `Serveur\Socle\Greffes\LectureReglage.cs`, `RepriseReglages.cs`, `ModeleCourriel.cs`, `LectureV2.cs`, `AuteurReglage.cs` |

Routes : `Serveur\Socle\Greffes\ModuleGreffes.cs` (types échangés : `Contrats\Socle\Greffes.cs`).
Banc : `tests\SocleGreffesCheck` — **`.\tests.ps1 SocleGreffes`**, 94 contrôles, un par critère au moins,
chacun cite son id (`SOC-007 CA03`…), chaque refus a son jumeau accepté.

## Les sources V2 vérifiées avant d'écrire

Toutes les références `code_v2` des trois stories ont été relues à la ligne citée (2026-11-20) :
`PARAMETRAGE.sql:1-12`, `Parametrage.cs:65-71, 98-102, 103-121, 163-166`, `GREFFE_PARAMETRE.sql:1-60`,
`rigparametre-catalogue.md:216-217`, `envoi-mail-template.md:39-46`, `config-registre.md:31-32`,
`RigGreffe.cs:180-200, 201-209, 349-351`, `drom.md:183-185`, `bases-et-roles.md:6-11, 18, 25, 28-32`,
`02-perimetre-volumetrie.md:114`, `drift-base-vs-ssdt.md:11-27`. Toutes disent ce que le carnet leur prête.
Lues en plus, parce que le comportement en dépend :

| Source | Ce qu'elle a appris |
|---|---|
| `RigTools\SQL\DBObject.cs:113-115` | un réglage présent mais NULL vaut `""` (DBString), pas la valeur par défaut |
| `RigTools\SQL\DBObject.cs:444-475` | la lecture d'une case à cocher : `bool.TryParse`, sinon la 1re lettre (`1`, `Y`, `O` = oui ; tout le reste = non). **« Vrai » y vaut non.** |
| `Message_Regent_V2016-02_Xml.cs:649`, `ListeLots.vb:634` | d'autres fonctions comparent au texte exact `"OUI"` |
| `MailTemplateStore.cs:16-43`, `EmailEnvoye.cs:475-478` | modèle `SHELL` en base d'abord, sinon fichier de `SEND_EMAIL_TEMPLATE_PATH`, sinon pas d'habillage |
| `RigGreffe.cs:72-76` + `GREFFE.cs` | `GRF_IS_GREFFE_BETA` est `bit NULL` ; vide = faux (`IsGreffeBeta`) |
| `RIG_DEV` (lu le 2026-11-20, lecture seule) | 966 réglages, 24 NULL, 33 oui / non écrits autrement que 0 / 1 (comme le carnet), 9 chemins réseau ; `GREFFE_PARAMETRE` 61 colonnes, 1 ligne ; `MAIL_TEMPLATE` 1 ligne `SHELL` (3 310 car.) ; base en `SQL_Latin1_General_CP1_CI_AS`, `FULL`, niveau 140, SQL Server 14.0.3485.1 |
| `controles\bases-greffes.json` (COMMUN_RIG, 2026-11-20, conv-2057) | code, nom, base et serveur des 40 greffes exploités |

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **Le catalogue vit dans la base commune** (celle de `ConnectionStrings:RigV3`) ; la table existe, vide, dans chaque base de greffe | Une seule source pour « où est la base du greffe » (CA04). Toutes les migrations se jouent partout : les 40 bases gardent un schéma identique (SOC-008 CA02). |
| **Une seule colonne `Serveur`** pour les deux bases | « Même serveur » (Q4) tenu par la structure, pas par une vérification. |
| **`CatalogueGreffes.OuvrirBaseV3Async(code)`** est la seule façon d'ouvrir la base d'un greffe | La configuration ne porte que la façon de s'authentifier (`Greffes:Authentification`, par défaut la session Windows du compte de service, comme la V2). Base injoignable = `BaseGreffeInjoignable` → 503, jamais de repli. Décision notée au plan pour les lots 5 à 7. |
| **Nom V3 déduit** : `RIG_AMIENS` → `RIGV3_AMIENS` (autre nom : suffixe `_V3`) | Le nom de la V3 se lit à partir de celui de la V2, pour les 40. |
| **Les règles du catalogue sont dans la base** (déclencheur `soc.TR_Greffe_Regles`) : auteur et date obligatoires, fuseau connu de SQL Server, passage figé une fois fait, aucun greffe retiré | Une requête SQL écrite à la main par l'exploitation ne peut pas les contourner (le banc l'essaie). |
| **Fuseaux en noms Windows** (`SA Western Standard Time`…) | Les seuls que SQL Server comprend (`sys.time_zone_info`, `AT TIME ZONE`, dès 2016) et que .NET lit aussi. Le passage figé se calcule dans la base, à l'heure locale du greffe. |
| **Valeur des réglages gardée en texte**, lue par la règle de la fonction V2 (`LectureReglage`, `RegleOuiNon`) | Décision Q2 et valeur hors norme `REGLAGE-OUI-NON` : aucune normalisation sans le référent Socle. |
| **Qui lit quel réglage** : un champ `static readonly LectureReglage` dans la classe qui le lit, trouvé par réflexion | Comme les modules : aucune liste commune à tenir. Un réglage repris que personne ne déclare va dans la liste remise au référent (CA04). |
| **`GREFFE_PARAMETRE` et la ligne de `COMMUN_RIG.GREFFE` en lignes (colonne, valeur `sql_variant`, type)** | Une base dont la table a dérivé (le carnet signale des écarts de schéma entre greffes) se reprend telle qu'elle est ; la valeur garde son type (money, bit). Les `(max)` vont dans `Texte` (un `sql_variant` ne les tient pas). |
| **`LectureV2`** : seule porte vers une base V2, n'accepte qu'un SELECT seul (ni INTO, ni `;`, ni mot qui écrit) | La reprise lit RIG_DEV et les bases des greffes sans jamais pouvoir les écrire ; le banc vérifie le refus. |
| **La base V3 prend le classement et le mode de récupération de SA base V2** ; le relevé de schéma note le classement **réel** de chaque colonne et celui de la base ; une base se compare à la version livrée **au même classement** | La recopie compare les textes comme la V2 du greffe. SQL Server ne garde pas la trace d'un `COLLATE` écrit dans une migration (`SOC-001_comptes.sql` en a un) : on ne peut pas séparer un classement voulu d'un classement hérité. Mesuré sur `main` + ce lot : l'ancienne règle « noter seulement s'il diffère de la base » rendait CA02 faux. Si les 40 bases V2 ont le même classement (à relever), les 40 bases V3 sont identiques à la lettre ; sinon, seul le classement diffère, et la vérification le nomme. |
| **Rôles `rigv3_serveur`, `rigv3_recopie`, `rigv3_exploitation` dans les migrations** ; aucun compte d'agent déclaré | Accès identique sur les 40 bases ; un agent ne peut même pas ouvrir la base V3 (erreur 4060, essayée). |
| **Contrôle « SQL Server 2017 » du texte des migrations**, en plus d'un essai au niveau 140 | Mesuré le 2026-11-20 sur SQL Server 2022 au niveau 140 : `GENERATE_SERIES` est refusé mais `GREATEST` et `IS DISTINCT FROM` passent. Liste tirée de Microsoft : [What's new in SQL Server 2022](https://learn.microsoft.com/en-us/sql/sql-server/what-s-new-in-sql-server-2022), [… 2019](https://learn.microsoft.com/en-us/sql/sql-server/what-s-new-in-sql-server-2019). |
| **Reprise et création ne sont pas des routes** | Ce sont des gestes d'exploitation sur les instances des greffes ; une route qui efface des réglages n'a pas sa place avant les droits du lot 3. |

## Les routes

| Verbe | Adresse | Rôle |
|---|---|---|
| GET | `/socle/greffes` | toutes les fiches |
| GET | `/socle/greffes/{code}` | une fiche : bases, bêta, outre-mer, fuseau et décalage, passage (404 si inconnu) |
| GET | `/socle/greffes/{code}/historique` | les modifications de la fiche, auteur et date |
| PUT | `/socle/greffes/{code}/passage` | `{"passageLocal":"2027-03-01T08:00"}`, heure locale ; 409 une fois le passage fait |
| PUT | `/socle/greffes/{code}/fuseau` | `{"fuseau":"SA Western Standard Time"}` ; 409 une fois le passage fait |
| GET | `/socle/greffes/{code}/base` | la base V3 répond-elle, à quelle version (503 sinon, la base nommée) |
| GET | `/socle/greffes/{code}/reglages` et `/reglages/{reglage}` | réglages lus dans la base du greffe |
| PUT | `/socle/greffes/{code}/reglages/{reglage}` | `{"valeur":"1"}` ; pose auteur et date ; 404 si le réglage n'existe pas (comme la V2) |
| GET | `/socle/greffes/{code}/reprise` | la liste remise au référent : réglages non lus par la V3, chemins réseau injoignables |

**Accès** : toutes ces routes portent `PolitiquesSocle.AgentAdmis` (lot 2, SOC-001), posée une fois sur
leur groupe dans `ModuleGreffes.cs` : sans session Windows, 401 ; compte refusé (inconnu, sans rôle,
désactivé, sans service), 403 ; la route ne s'exécute pas. L'auteur d'une modification est donc toujours
un agent admis ; pour un réglage, c'est son nom, comme la V2 (SOC-003 CA07, tableau des écarts). Le banc appelle les 10 routes sans session (401), avec un compte désactivé (403), vérifie
que rien n'a changé, et que l'agent admis lit par les 7 routes de lecture (200). Il vérifie aussi, dans le
serveur lui-même, que chaque route du module porte la politique : une route ajoutée hors du groupe fait
tomber le banc. Contre-épreuve faite (2026-11-21) : politique retirée du groupe → 3 contrôles rouges.

## Les gestes d'exploitation (code prêt, à lancer par l'exploitation)

```csharp
// 1. Remplir le catalogue depuis COMMUN_RIG (lecture seule de la V2)
await RepriseCatalogue.ReprendreAsync(communRigV2, communeV3, auteur);
await RepriseCatalogue.ComparerAsync(communRigV2, communeV3);           // 0 écart attendu

// 2. Créer la base V3 d'un greffe à côté de sa base V2, puis l'inscrire
await BaseGreffe.CreerPourGreffeAsync(catalogue, "8002", @"Base\migrations",
    new AccesBaseV3(["AMITEL\\svc-rigv3"], ["AMITEL\\svc-recopie"], ["AMITEL\\exploitation-sql"]),
    new TaillesBaseV3(), auteur);
await BaseGreffe.VerifierAsync(chaineBaseV3, schemaLivre, derniereMigration); // avant chaque répétition

// 3. Reprendre les réglages, comparer
await RepriseReglages.ReprendreAsync(baseV2, baseV3);
await RepriseReglages.ComparerAsync(baseV2, baseV3);                     // 0 écart attendu

// 4. Surveiller
await BaseGreffe.ControlerSauvegardesAsync(instance, baseV2, baseV3);
await BaseGreffe.RestaurerEssaiAsync(instance, baseV3, autreInstance, nomEssai, fichier);
await BaseGreffe.PlaceDisqueAsync(instance, [baseV2, baseV3], seuilOctets);
```

## Écarts et valeurs hors norme (codés comme la V2, à valider par le référent Socle)

| Sujet | Ce que fait la V3 | Pourquoi le signaler |
|---|---|---|
| « Vrai » lu par une case à cocher .NET de la V2 | **non**, comme `DBObject.cs:444-475` (seule la 1re lettre compte : V ≠ O). « Oui » donne oui, « oui » non. | Règle reproduite et testée. Elle ne vaut que pour les lectures .NET. |
| Les réglages écrits « Vrai » / « Faux » (`CMANDM_TOUJOURS_OP`, `CMANDM_FORCER_DIFFERE`, `CJCM_FORCER_DIFFERE`…) | gardés tels quels ; **aucune règle de lecture encore** | Ils sont écrits et relus par des écrans **VB6** : `RIG_CLIENT_CHGT_MAND_MASSE\frmParamétrage.frm:1590` écrit `CBool(case)`, que VB6 traduit dans la langue de Windows ; `frmParamétrage.frm:1284` et `frmChgtMANDMasse.frm:3523` le relisent dans un booléen. Microsoft documente que VB6 ne relisait pas ce mot traduit avant le Service Pack 3 de Visual Studio 6 ([KB Q216379](https://github.com/jeffpar/kbarchive/blob/master/kb/216/Q216379)) ; la conversion exacte ne figure dans aucune documentation trouvée ([Wine, VarBoolFromStr](https://dyn.manpages.debian.org/wheezy/libwine-dev/VarBoolFromStr.3w)). À mesurer sur un poste RDS de la V2 avant d'écrire la fonction V3 qui les lira ; surtout pas avec la règle « case à cocher » .NET, où « Vrai » vaut non. |
| Réglage présent mais vide | `""`, pas la valeur par défaut de la fonction | Comportement V2 (DBString) ; 24 réglages NULL dans RIG_DEV. |
| Papeete marqué outre-mer | repris tel quel (`GREFFE-PAPEETE-OUTRE-MER`) | Sens « vague de livraison, fuseau, serveur local », pas juridique. |
| `MAIL_TEMPLATE.MAILT_CONTENU` | `nvarchar(max)` | `nvarchar(max)` dans RIG_DEV, `varchar(max)` dans le projet versionné (dérive). |
| `COMMUN_RIG.GREFFE` | toutes les colonnes de la base, quelles qu'elles soient | 54 dans la classe V2, 55 relevées en base : la reprise ne dépend pas de la classe. Mots de passe compris, chiffrés comme en V2, servis par aucune route. |
| Auteur d'un réglage modifié | le **nom** de l'agent (`soc.Compte.Nom`), sinon son compte Windows sans domaine — comme la V2 (`Parametrage.cs:104-108` : `UTLST_NOM`, sinon `AmiEnvironment.UserName`) ; règle dans `AuteurReglage.cs` | Les auteurs repris de la V2 sont des noms : la colonne ne mélange pas noms et comptes. Le second repli de la V2 (l'utilisateur de la borne, `Utilisateur.GetUtilisateurWEB`, `Utilisateur.cs:428-430`) n'a pas d'équivalent : la V3 refuse avant (403) tout appelant qui n'est pas un agent admis. Corrigé le 2026-11-21 sur objection du contrôle final (la V3 écrivait d'abord le compte) ; contre-épreuve : repli écrit avec son domaine → 1 contrôle rouge. |
| `GREFFE_PARAMETRE` à plusieurs lignes | la reprise s'arrête sans rien copier | La V2 n'en prévoit qu'une ; à trancher si une base en a plusieurs. |
| `SEND_EMAIL_TEMPLATE_PATH` de RIG_DEV | `\\ged2\PartageEdition$\Commun\templateMail\template-shell.txt`, **injoignable** depuis ce poste | Sans conséquence tant que le modèle est en base (il l'est) ; listé au référent (CA05). 8 des 9 chemins réseau de RIG_DEV répondent. |
| Classement des bases | celui de la base V2 du greffe | À relever sur les 40 bases : s'ils diffèrent, la recopie compare comme chaque V2. |
| Fuseau à la reprise | par département du code greffe (971, 972 → UTC−4 ; 974 → UTC+4 ; 976 → UTC+3 ; 987 → UTC−10 ; métropole → Paris) | Valeurs de SOC-007 CA03 ; un outre-mer non prévu (Guyane, 973) reste vide, à saisir. |

## Ce qui ne peut pas se faire ici, et pourquoi

| Tâche / critère | Fait ici | Reste, et pourquoi |
|---|---|---|
| SOC-003 CA01 « 0 écart sur les 40 bases » | reprise et comparaison sur RIG_DEV (966 réglages, 61 paramètres, 1 modèle, 8 694 valeurs, 0 écart) | Les 40 bases sont sur SQL-PROD et les serveurs d'outre-mer : lecture réservée, à lancer par l'exploitation avec `RepriseReglages.ComparerAsync`. Un outil en ligne de commande pour enchaîner les 40 est à écrire (hors des fichiers de ce lot). |
| SOC-007 CA01 sur la vraie `COMMUN_RIG` | prouvé sur une copie d'essai : les 40 greffes relus le 2026-11-20, 54 colonnes | La lecture de COMMUN_RIG m'est refusée depuis le poste (base non déclarée hors production). Lancer `RepriseCatalogue` sur la vraie base au déploiement. |
| SOC-008 T02 : créer les 40 bases | création prouvée sur le serveur local, à côté de bases V2 jetables | Écriture sur les instances de production : exploitation, avec autorisation. |
| SOC-008 T03 : comptes d'accès | rôles posés, comptes déclarés à la création | Noms des comptes de service (serveur, recopie, exploitation) à fournir par Amitel. |
| SOC-008 CA04 : plan de sauvegarde, restauration trimestrielle **sur une autre instance** | contrôle « V3 au moins aussi récente que V2 » et restauration d'essai (même instance, autre nom) | Les travaux de sauvegarde planifiés et une seconde instance sont de l'exploitation ; `RestaurerEssaiAsync` accepte une instance cible différente (le fichier doit lui être lisible). |
| SOC-008 CA05 : version de chaque instance | `CreerAsync` lit la version et refuse en dessous de 2017 ; migrations jouées au niveau 140 ; texte contrôlé | Relever la version réelle des 4 instances de production (l'outil de requête n'y a pas accès). |
| SOC-008 CA06 : alerte de place disque | `PlaceDisqueAsync` dit quel volume passe sous le seuil | Brancher sur la supervision d'Amitel et fixer le seuil. |
| Écrans « à maquetter » (SOC-003 T03, SOC-007 T05, écran d'exploitation SOC-008) | — | Aucun fichier d'écran n'est confié à ce lot ; à confier à un lot d'écrans (thèmes clair et sombre). |
| Accès aux routes | toutes les routes réservées aux agents admis (`PolitiquesSocle.AgentAdmis`) | Droits fins (seuls l'exploitation d'Amitel et l'agent chargé du paramétrage modifient) : avec les droits du lot 3 (SOC-002). |
| Envoi du courriel habillé | le modèle choisi est celui de la V2 (base, puis fichier, puis rien) | Le rendu (`GreffeMailBuilder`) appartient à la story d'envoi des courriels. |
| Catalogue « lisible depuis chaque site » | — | Où poser la base commune et comment l'outre-mer la joint : déploiement. |

## Lancer le banc

```powershell
docker compose up -d                 # le SQL Server local
.\tests.ps1 SocleGreffes             # 94 contrôles, code 0 = vert
```

Le banc passe par l'identité du lot 2 : l'en-tête `X-RigV3-Compte` (lu en développement seulement) et deux
comptes inscrits dans `soc.Compte` de sa base jetable, `banc.socle` (admis) et `banc.refuse` (désactivé).

Il lit RIG_DEV sur `SQL-DEV\DEV` avec la session Windows (`RIGV3_V2_REGLAGES` pour une autre chaîne ; RIG_DEV
ou RIG_RECETTE seulement), crée ses bases jetables, deux bases V3, quatre comptes SQL et quelques sauvegardes
sur le serveur local, et supprime tout en sortie. La copie d'essai de COMMUN_RIG
(`tests\SocleGreffesCheck\commun_rig_greffe.sql`) se régénère par `generer_commun_rig_greffe.py`.
Contre-épreuve faite : « Vrai » lu oui en case à cocher et marque bêta vide lue « bêta » → 3 contrôles rouges.

<!-- fix-ok: trois retouches mesurees -- (1) les reglages ecrits « Vrai » sont lus par VB6 (frmParametrage.frm:1284), pas par la case a cocher .NET : l'ancienne ligne « probable defaut de la V2 » etait fausse ; (2) CA02 rouge sur main + ce lot (COLLATE ecrit dans SOC-001) : regle du classement reecrite ; (3) lot 2 fusionne pendant le lot, nombre de controles passe a 88 ; (4) objection du controle final : routes ouvertes sans AgentAdmis, politique posee, 92 controles ; (5) objection du controle final : l'auteur d'un reglage etait le compte (« AMITEL\banc.socle », banc rouge du 2026-11-21) au lieu du nom UTLST_NOM de la V2, corrige, 94 controles. -->
