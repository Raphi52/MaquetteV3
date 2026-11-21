# Numérotation — le compteur central de la V3

Lot 5 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-20. Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\numerotation.json` :

| Story | Ce qu'elle demande | Où c'est fait |
|---|---|---|
| NUM-001 | Un seul compteur par série, dans la base V3 du greffe ; la V2 comme la V3 y prennent leurs numéros | `Base\migrations\NUM-001_compteurs.sql` (tables, `num.Attribuer`, droits) |
| NUM-004 | Rendre un numéro donné par erreur pour qu'il soit redonné | même fichier : `num.Restituer` ; route `POST /numerotation/{greffe}/restitutions` |
| NUM-008 | Prouver qu'aucun numéro n'est donné deux fois quand les deux logiciels numérotent ensemble | `tests\NumerotationCompteurCheck` (charge V2 + V3) |

Décision de départ : **Q16** du cadrage — la V3 distribue tous les numéros ; `GENERER_NUMERO`,
`CHERCHER_NUMERO` et `RESTITUER_NUMERO` des bases V2 lui demandent, sur la même instance, dans leur
transaction, sans réseau (Q4).

**Preuve** : `.\tests.ps1 NumerotationCompteur` rend 0 — 52 contrôles verts (38 du lot 5, 14 du raccord du lot 8), au moins un
par critère, et 2 lignes « RESTE » pour ce qu'aucun banc du poste ne peut faire (voir la fin). Dont
8 sessions (4 V2, 4 V3) qui tirent 10 000 numéros RGB en même temps : 10 000 différents et consécutifs,
compteur +10 000 (≈ 22 s, ≈ 460 numéros/s sur le SQL Server Docker du poste). Contre-épreuves faites :
verrou du compteur retiré → 62 paires en double sur 100 (NUM-001 CA02 rouge) et la clé unique arrête la
création d'un second compteur RGB 2027 ; refus « au-delà du compteur » retiré → 4 contrôles rouges.

## Sources V2 vérifiées

Lues le 2026-11-20 dans `D:\RigApplication\Source\BaseDeDonnées\RigDatabase\dbo\` et sur `RIG_DEV`
(`SQL-DEV\DEV`, lecture seule) :

| Source | Ce qui en est repris |
|---|---|
| `Stored Procedures\GENERER_NUMERO.sql:2-9` | signature : type, année (0 = en cours), quantième (0 = aujourd'hui), code greffe |
| `GENERER_NUMERO.sql:42-55` | redirection d'un type (`ARRC` → `ARRD` dans RIG_DEV) ; `-2` pour un type inconnu |
| `GENERER_NUMERO.sql:76-146` | série « par jour » quand le format porte `Q` : un compteur par (type, année, quantième) |
| `GENERER_NUMERO.sql:85-105, 150-168` | réattribution du plus petit numéro rendu, toutes ses lignes effacées |
| `GENERER_NUMERO.sql:174-177` | « chrono 0 = rien donné, le suivant est la valeur de départ » |
| `GENERER_NUMERO.sql:191-233` | nouvelle année : réglages copiés, départ à `NMMTR_CHRONO_DEBUT` |
| `GENERER_NUMERO.sql:245-288` | écriture du numéro : `A L S C Q G K`, tout autre caractère recopié |
| `CHERCHER_NUMERO.sql` | même règle que `GENERER_NUMERO`, sans transaction et sans caractère recopié. **Aucun appelant** dans `D:\RigApplication\Source` (grep du 2026-11-20) : le lot 7 la fait pointer sur `num.Attribuer` comme `GENERER_NUMERO`. |
| `RESTITUER_NUMERO.sql:43-67` | réglage de restitution (`NMMTR_GESTION_NUM_ERR`) ; inactif : rien n'est rendu |
| `RESTITUER_NUMERO.sql:76-105` | lecture du numéro selon le format |
| `RESTITUER_NUMERO.sql:114-123` | une ligne « attribution erronée » ajoutée ; le compteur n'est ni effacé ni diminué (note Brain `rig-v3-deux-lignes-numero-metier-apres-restitution`) |
| `Tables\NUMERO_METIER.sql:1-36` | colonnes ; index (type, année, quantième) **non unique** |
| `RIG_DEV.dbo.NUMERO_METIER` | réglages réels de JPC, RGB, JGDJ, ARRD/ARRC, RC1/RC5/RC6, des 10 séries sans restitution, LTIA : ce sont les compteurs de départ du banc |
| `Outils\DLL\RigMetier\Numerotation\Numerotation.cs:182-215` | l'appel .NET de la restitution lit `@MonErreur` (`-1` si la procédure n'a pas pu être jouée) |

## Le modèle (base V3 du greffe, schéma `num`)

| Table | Une ligne = | Clé |
|---|---|---|
| `num.Compteur` | une ligne « compteur » de `NUMERO_METIER` | **UNIQUE** (`Type`, `Annee`, `Jour`) |
| `num.NumeroRendu` | une ligne « attribution erronée » (`NMMTR_ATTRIBUTION_ERRONEE = 1`) en attente d'être redonnée | (`Serie`, `Annee`, `Jour`, `Chrono`) |
| `num.Journal` | une attribution, réattribution, restitution ou un refus : série, numéro, logiciel (V2/V3), compte, poste, date, explication entière, code rendu | `Id` |

`Jour` vaut le quantième (1..366) pour une série dont le format porte `Q` (JGDJ, ARRD, DMD…), **0** pour
les autres (un compteur par année). `Chrono` est le **dernier numéro donné** ; `NULL` = aucun encore
(le prochain sera `Debut`).

## L'interface pour les autres lots

### Le lot 6 (reprise de `NUMERO_METIER`, NUM-005)

Une migration `NUM-005_reprise.sql` (`-- apres: NUM-001`) ou l'outil de reprise écrit **directement** dans
les tables (en `dbo`, pas par les programmes). Correspondance :

| `NUMERO_METIER` (V2) | V3 |
|---|---|
| ligne avec `NMMTR_ATTRIBUTION_ERRONEE = 0` | `num.Compteur` |
| `NMMTR_TYPE_METIER`, `NMMTR_ANNEE` | `Type`, `Annee` |
| `NMMTR_QUANTIEME` | `Jour` si `CHARINDEX('Q', NMMTR_STRUCTURE_NUMERO) > 0`, sinon **0** (la V2 y écrit le jour de création, sans effet) |
| `NMMTR_STRUCTURE_NUMERO`, `NMMTR_CODAGE_ANNEE`, `NMMTR_LETTRE_CATEGORIE`, `NMMTR_SEPARATEUR`, `NMMTR_TAILLE_CHRONO`, `NMMTR_CODE_GREFFE`, `NMMTR_LIBELLE` | `Structure`, `CodageAnnee`, `Lettre`, `Separateur`, `TailleChrono`, `CodeGreffe`, `Libelle` |
| `NMMTR_CHRONO_DEBUT` | `Debut` |
| `NMMTR_CHRONO` | `Chrono`, **0 → NULL** (la V2 note « rien donné » par 0) ; contrainte `Chrono >= Debut` |
| `NMMTR_GESTION_NUM_ERR` | `GestionRendus` |
| `NMMTR_FILE_REDIRECTION` | `Redirection` |
| `NMMTR_TABLE_AFFECTEE`, `NMMTR_ATTRIBUT_AFFECTE` | `TableCible`, `ColonneCible` |
| `NMMTR_DATE_ATTRIBUTION`, `NMMTR_ID_NMMTR` | `DerniereAttribution`, `IdV2` |
| ligne avec `NMMTR_ATTRIBUTION_ERRONEE = 1`, lisible, entre `Debut` et `Chrono` de son compteur, série à restitution active | `num.NumeroRendu` (une seule par numéro ; `Numero` = `num.Composer(...)`, `Explication` = `NMMTR_EXPLICATION`, `Logiciel` = `'V2'`, `IdV2`) |
| les autres lignes « attribution erronée » (au-delà du compteur, en double, série inactive) | `num.Journal`, `Evenement = 'reprise'` (réservé à la reprise), avec la raison (NUM-004 T05) |

Cas que la clé unique refuse et que la reprise doit trancher (NUM-005) : deux compteurs ARRD pour un
même jour (2014, 2018) et JGOP 2004 jour 1 dans RIG_DEV ; une série « par jour » dont une ligne n'a pas
de quantième (ARRC 2001).

`num.Composer(@Structure, @CodageAnnee, @Lettre, @Separateur, @TailleChrono, @CodeGreffe, @Annee, @Jour, @Chrono)`
écrit un numéro ; `num.LireNumero(@Numero, @Structure, @CodageAnnee, @Lettre, @Separateur, @TailleChrono, @LongueurGreffe)`
le relit (aucune ligne s'il ne suit pas le format). Les contrôles de continuité (NUM-009 à NUM-020) lisent
`num.Journal`.

### Le lot 7 (les programmes de la V2 qui demandent à la V3, NUM-002, 003, 006)

Le nouveau corps de `GENERER_NUMERO` (et de `CHERCHER_NUMERO`, même règle) appelle `num.Attribuer` de la
base V3 du greffe, sur la même instance :

```sql
DECLARE @rc int, @msg nvarchar(400);
EXEC @rc = RIGV3_<GREFFE>.num.Attribuer
     @Type = @TYPE_NUMERO_METIER, @Numero = @NUMERO_RETOURNE OUTPUT,
     @Annee = @ANNEE, @Jour = @QUANTIEME, @CodeGreffe = @CODE_GREFFE,
     @Logiciel = 'V2', @Compte = <compte V2>, @Poste = <poste V2>, @Message = @msg OUTPUT;
RETURN @rc;      -- 0, -2 (type inconnu) ou -5 (série pleine)
```

| Paramètre de `num.Attribuer` | Sens |
|---|---|
| `@Type varchar(5)` | le type demandé ; la redirection est suivie (`@Serie OUTPUT` dit la série qui a servi) |
| `@Numero varchar(40) OUTPUT` | le numéro écrit au format de la série |
| `@Annee smallint = 0`, `@Jour smallint = 0` | 0 = l'année en cours, aujourd'hui |
| `@CodeGreffe varchar(5) = NULL` | écrit à la place de `G` (seule LIAI l'utilise) ; NULL = celui du compteur. Passer le `@CODE_GREFFE` reçu (défaut V2 `'9999'`) donne le même numéro que la V2. |
| `@Logiciel char(2)`, `@Compte`, `@Poste` | pour le journal ; `@Compte`/`@Poste` NULL = `ORIGINAL_LOGIN()` / `HOST_NAME()` |
| `@DelaiVerrouMs int = 10000` | attente maximale d'un compteur tenu par une autre transaction ; `-1` = sans limite |
| `@Chrono`, `@Reattribue`, `@Message` OUTPUT | le chrono, « c'est un numéro rendu », le motif d'un refus |

`RESTITUER_NUMERO` appelle `num.Restituer` (`@Type`, `@Numero`, `@Annee`, `@Explication`, `@Logiciel`,
`@Compte`, `@Poste`, `@DelaiVerrouMs` ; `@Serie`, `@Issue`, `@Message` OUTPUT) et rend son code dans
`@MonErreur` (l'appelant .NET le lit là, `Numerotation.cs:205`). Codes : `0` rendu / non redonnable /
déjà rendu, `-2` type inconnu, `-3` illisible, `-4` jamais donné. La ligne « attribution erronée » à
recopier dans `NUMERO_METIER` de la V2 (NUM-006) est celle de `num.NumeroRendu` : son `Explication` a
déjà les 50 premiers caractères.

**Erreurs** : les deux programmes **lèvent** (`THROW`) au lieu de rendre un code quand ils ne peuvent pas
répondre : `51001` = compteur tenu au-delà de `@DelaiVerrouMs` (rien n'est consommé), `51000` =
paramètre faux, toute autre erreur SQL telle quelle. Appelés dans une transaction, ils y travaillent
(point de sauvegarde) : annulée, rien n'est consommé ; en erreur, la transaction de l'appelant est
ramenée au point de sauvegarde (ou laissée à annuler si elle est condamnée).

**Droits** : le compte SQL de la V2 devient un utilisateur de la base V3 membre de `num_demandeur`
(EXECUTE sur les deux programmes, lecture des tables, **DENY** INSERT/UPDATE/DELETE). Il ne doit pas être
`db_owner` de la base V3 : `db_owner` passe outre le DENY.

### Les actes de la V3 (domaines, phases suivantes)

Un acte qui numérote reçoit `DistributeurNumeros` (`Serveur\Numerotation\Compteur\DistributeurNumeros.cs`)
en paramètre de sa route et l'appelle **dans la transaction de l'acte** — c'est le seul chemin
d'attribution de la partie serveur (NUM-001 T04) :

```csharp
await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
var n = await numeros.AttribuerAsync(cn, tx, new DemandeNumero("RC1"), compte, poste, ct);
if (n.Code != CodesNumerotation.Ok) return Results.UnprocessableEntity(n);
// ... l'acte, avec n.Numero ...
await tx.CommitAsync(ct);
```

`RestituerAsync(cn, tx, new DemandeRestitution(type, numero, explication), compte, poste, ct)` pour une
annulation (minute, instance, procédure, demande au Trésor : NUM-004 T03). `CompteurOccupeException` =
compteur tenu au-delà de `Numerotation:DelaiVerrouMs` (10 000 ms par défaut).

Les routes (`Contrats\Numerotation\RoutesCompteur.cs`) servent l'écran : `GET /numerotation/{greffe}/compteurs`
(`?type=`), `POST /numerotation/{greffe}/numeros`, `POST /numerotation/{greffe}/restitutions` (explication
obligatoire : 400 sinon). Compteur occupé : HTTP 503, code `-9`. L'adresse d'un greffe :
`RoutesCompteur.Pour(RoutesCompteur.Numeros, code)` ; accès et base du greffe : section « Raccord fait » ci-dessous.

## Raccord fait (lot 8, 2026-11-21) : session Windows et base du greffe

| Point | Ce que fait la V3 | Où |
|---|---|---|
| Session | Les trois routes portent `PolitiquesSocle.AgentAdmis` (lot 2), posée une fois sur leur groupe : 401 sans session Windows, 403 pour un compte refusé ; la route ne tourne pas. Le journal (`num.Journal.Compte`) prend le compte de la session admise ; « (non identifie) » a disparu. | `Serveur\Numerotation\Compteur\ModuleCompteur.cs` |
| Base du greffe | L'adresse porte le code du greffe (`{greffe}`) ; la route ouvre sa base V3 par le catalogue commun (`CatalogueGreffes.OuvrirBaseV3Async`, lot 4). Greffe absent du catalogue : 404 ; base qui ne répond pas : 503, la base nommée, sans repli sur la base commune ni sur un autre greffe. Aucune chaîne de connexion dans le module. | même fichier, `Contrats\Numerotation\RoutesCompteur.cs` |
| Agent du greffe | La politique juge l'agent dans la base du greffe de l'adresse (`Serveur\Socle\Identite`, voir `socle-identite.md`) : un agent connu d'un seul greffe est refusé (403) sur les routes des autres. | `Serveur\Socle\Identite\ServiceIdentite.cs` |
| `DistributeurNumeros` | Inchangé : il reçoit toujours la connexion ouverte de l'acte. | — |

Le banc joue trois bases jetables : la base **commune** (le catalogue, sans aucun compte d'agent), celle du
**greffe A** (les compteurs de RIG_DEV, l'agent `banc.numeros`, un compte désactivé) et celle du **greffe B**
(RGB 2026 à 4242, JPC 2026 à 10, l'agent `banc.greffeb`), plus un greffe au catalogue dont la base n'existe
pas. Contrôles « Raccord (1) à (3) » : les 3 routes répondent 401 sans session, 403 au compte désactivé, 404
pour un greffe inconnu, 503 pour la base muette, sans que rien ne bouge dans les trois bases ; un numéro tiré
pour A avance A et laisse B intact, et l'inverse ; l'agent de A est refusé sur B, celui de B sur A.
Contre-épreuves faites (2026-11-21), chaque fois fichier restauré à l'identique : politique retirée du groupe
→ 5 contrôles rouges ; identité cherchée dans la base commune → banc rouge (403 dès le premier appel) ; réponse
404 / 503 de la politique retirée → 2 rouges (403 à la place) ; route qui ouvre toujours la base de A → 2 rouges.

Pour un banc ou un essai : une ligne `soc.Greffe` dans la base commune (serveur, base V3), la configuration
`Greffes:Authentification` (la façon de se connecter aux instances, sans serveur ni base) et le compte de
l'agent dans `soc.Compte` de la base **du greffe** (modèle : `tests\NumerotationCompteurCheck\Program.cs`).

## L'écran « Compteurs de numéros » (2026-11-21, fil chef conv-2052)

Menu **Configurer › Référentiels › Compteurs de numeros** (code `PROC_COMPTEURS_NUM`), aussi trouvé par la
recherche (« compteurs »). Il reprend la procédure de menu **`XNUMERO`** de la V2, « Reattribution de numero
metier » (PROCVB6, ouverte aux rôles RCSNIV03 et RCSNIV05 sur RIG_DEV : `PROCESSUS_ET_FONCTIONALITE`,
`HABILIT_ROLE_USER_PROCESSUS`, lus le 21/11) ; ses sources VB6 ne sont pas dans le dépôt de la V2.

| Partie | Ce que montre l'écran | Où |
|---|---|---|
| Compteurs (NUM-001 CA01) | une ligne par série, année et jour : libellé, dernier chrono, « Redonnés » / « n en attente » / « Non redonnés », date du dernier numéro ; recherche par série ou libellé | `web\src\ecrans\EcranCompteurs.tsx`, route `GET /numerotation/{greffe}/compteurs` |
| Rendre un numéro (NUM-004 CA10) | série, numéro, explication obligatoire ; année demandée seulement si le format de la série ne la porte pas (CA07) ; au-delà de 50 caractères, l'écran dit que la V2 n'en recevra que le début (CA08) ; la phrase du serveur s'affiche telle quelle (rendu, non redonnable, déjà rendu, refus) | `Pont\EcranCompteurs.cs`, route `POST /numerotation/{greffe}/restitutions` |
| Sans `RIGV3_SERVEUR` | 9 compteurs de démonstration (libellés et chronos de RIG_DEV, 21/11), lecture seule | `Pont\EcranCompteurs.cs` |
| Branchée sans greffe ouvert | « Aucun greffe ouvert », aucun compteur : l'écran ne devine pas de greffe | idem |

Preuves : `web\src\ecrans\EcranCompteurs.test.tsx` (8 tests) ; dans `tests\NumerotationCompteurCheck`, la partie
« Ecran » joue `Pont\EcranCompteurs.cs` par le vrai `ClientServeur` contre le serveur en mémoire : lecture des
51 compteurs du greffe A, numéro rendu puis redonné, numéro jamais donné refusé, agent du greffe B refusé,
greffe absent (404) et base muette (503) en phrase lisible, démonstration. Captures : bureau caché, thèmes clair
et sombre (application de la copie de travail, démonstration).

## Comment la simultanéité est tenue (NUM-001 CA02, NUM-008)

1. Chaque compteur (série, année, jour), **qu'il existe ou non**, a un verrou applicatif
   `num:<série>:<année>:<jour>` (`sp_getapplock`, exclusif, jusqu'à la fin de la transaction), pris par
   `num.Attribuer` **et** `num.Restituer`. Deux demandes du même compteur, V2 ou V3, passent l'une après
   l'autre ; un compteur manquant (nouvelle année, nouveau jour) n'est créé qu'une fois.
2. L'attente est bornée (`@DelaiVerrouMs`) : `SET LOCK_TIMEOUT` n'accepte pas de variable (erreur 102
   mesurée), `sp_getapplock @LockTimeout` si. Le verrou ne bloque que **sa** clé : pas de verrou de plage
   qui ferait attendre le jour d'à côté.
3. Le chrono suivant est lu et écrit dans **une** instruction (`UPDATE … OUTPUT inserted.Chrono`).
4. La clé **unique** (`Type`, `Annee`, `Jour`) est la dernière barrière : un second compteur est refusé
   même par un écrivain qui ne passerait pas par les programmes (contre-épreuve : verrou retiré, la clé a
   arrêté la création en double de RGB 2027).
5. Les réglages (redirection, format) se lisent sans verrou, comme la V2 (`GENERER_NUMERO.sql:42,57,73`) :
   une attribution ne les change jamais.
6. Un acte qui prend des numéros de **deux** compteurs dans des ordres différents peut s'interbloquer
   avec un autre (comme dans la V2) : SQL Server en choisit un, qui reçoit une erreur et ne consomme rien.

## Règles retenues et écarts avec la V2

Lecture de la règle commune 6 du plan (« un écart à valider se code comme la V2 ») retenue par ce lot :
quand la story a **déjà écrit l'écart en critère** (ex. NUM-004 CA04), le critère est codé et l'écart
signalé ici ; un écart **sans critère** est codé comme la V2. Tous restent « à valider » par le référent
Socle (Q12).

| Écart | V2 | V3 | Critère | État |
|---|---|---|---|---|
| Restitution d'une série inactive (FACTU, DMD, RAP, ARRHF, ARRNQ, RA1, EAR, LIAI, SIREN, HF) | sort sans rien faire ni rien noter (`RESTITUER_NUMERO.sql:63-67`) | pas redonné non plus, code 0, **demande journalisée** avec son explication | NUM-004 CA03 | à valider |
| Numéro jamais donné (JPC 2026 : 9999 rendu, compteur à 242) | accepté, redonné avant le suivant : la série saute | **refusé**, `-4`, refus journalisé | NUM-004 CA04 | à valider |
| Numéro rendu plusieurs fois | une ligne de plus à chaque fois (jusqu'à 157) | une seule entrée redonnable, les suivantes journalisées | NUM-004 CA05 | à valider |
| Année d'un numéro rendu sans année | l'année en cours (`RESTITUER_NUMERO.sql:73-74`) | **lue dans le numéro** si le format la porte | NUM-004 CA07 | à valider |
| Erreur en restitution | sort sans code, transaction parfois ouverte | code non nul / erreur levée, transaction annulée | NUM-004 CA09 | à valider |
| Série par jour : réattribution sans regarder le réglage | oui (`GENERER_NUMERO.sql:85-86`) | **comme la V2** (aucun critère ; sans effet dans RIG_DEV) | — | à valider |
| Lecture d'un numéro rendu | retranche des longueurs sans rien vérifier | format vérifié à la lettre ; illisible → `-3`, rien d'écrit | NUM-004 CA06 | à valider |
| Chrono plus long que le format (ex. JPC au-delà de 9999) | boucle sans fin (`GENERER_NUMERO.sql:261-266`) : l'appelant attend jusqu'à son délai | refus `-5` « série pleine », journalisé, rien consommé | — (échouer proprement, Q16) | à valider |
| Valeur de départ 0 (SIREN) | « chrono 0 = rien donné » redonne 0 à chaque demande | `NULL` = rien donné : 0 puis 1, 2… | NUM-001 (aucun doublon) | à valider |
| Lettre ou séparateur absents d'un format qui les demande | le numéro entier devient NULL | écrit sans eux | — | sans effet dans RIG_DEV |
| Deux compteurs pour une même clé (ARRD 2014, 2018) | possible (index non unique, création du jour hors transaction) | impossible (clé unique + verrou) | NUM-001 CA02, NUM-008 CA02 | voulu |

Le carnet écrit le numéro de NUM-004 CA01 « 2026RJ00012 » ; le format de JPC dans RIG_DEV a 4 chiffres :
le numéro réel est `2026RJ0012`, c'est lui que le banc joue.

## Ce qui reste, et pourquoi

| Reste | Raison | Qui |
|---|---|---|
| **L'écran choisit son greffe** : `Pont\EcranCompteurs.cs` passe encore `null` comme greffe (`Ouvrir`, `Rendre`) ; branchée au serveur, l'écran dit donc « Aucun greffe ouvert » | le choix du greffe à l'ouverture (fenêtre de connexion de la V2, `RigFormLogin`) est le lot 10 ; il y passera le greffe de la session | lot 10 |
| NUM-004 T04 / T08 : la réattribution **réservée** aux détenteurs de `XNUMERO` (rôles RCSNIV03, RCSNIV05 de la V2), et son essai avec un agent sans le droit | la route de restitution n'exige encore qu'un agent admis ; le droit se vérifie par `ServiceDroits`, que le lot 9 raccorde en ce moment à la base du greffe | après le lot 9 |
| NUM-001 T05, NUM-004 T05 : reprise des lignes de la V2 | story NUM-005 | lot 6 |
| NUM-004 T02 : nouveau corps de `RESTITUER_NUMERO` ; NUM-008 « sessions V2 par les programmes remplacés » | les programmes V2 remplacés sont le lot 7 ; ici, une session V2 appelle `num.Attribuer`/`num.Restituer` en SQL avec `@Logiciel = 'V2'`, ce que feront ces programmes. Le lot 7 rejoue la charge avec ses vrais programmes (`NumerotationV2Check`). | lot 7 |
| NUM-008 CA07 : rejouer la charge sur une copie anonymisée d'une base de greffe, archiver avec le rapport de la bêta (SOC-022) | aucune copie anonymisée sur ce poste ; le banc écrit déjà son relevé (`numerotation-charge.json` à côté du banc) | avant la bêta |
| NUM-001 CA01 « après la reprise » | le banc part des réglages réels de RIG_DEV recopiés à la main ; la reprise de toutes les lignes est NUM-005 | lot 6 |
| Déployer la base V3 à côté de la base V2 de chaque greffe, donner `num_demandeur` au compte de la V2 et au serveur | hors poste de développement (pas de droit d'écriture sur les serveurs des greffes) | déploiement |
| Les chiffres de RIG_DEV (142 types, 1 365 lignes rendues…) sur les 40 bases | mesure Q11 | lot 6 / exploitation |

## Travailler en développement

```powershell
docker compose up -d                                  # le SQL Server du poste
.\tests.ps1 NumerotationCompteur                      # le banc (≈ 55 s)
```

Sur une base de développement (`architecture.md`, « Travailler en développement ») :

```sql
DECLARE @n varchar(40), @rc int;
EXEC @rc = num.Attribuer @Type = 'RGB', @Numero = @n OUTPUT;           -- 2026B00891…
EXEC @rc = num.Restituer @Type = 'RGB', @Numero = '2026B00891', @Explication = N'essai';
SELECT * FROM num.Journal ORDER BY Id DESC;
```

<!-- fix-ok: chiffres corriges apres mesure -- 38 lignes « OK » et 2 « RESTE » comptees par grep -c sur la sortie du banc (2026-11-20) ; le nombre de bases V2 retire, non verifie ici. Lot 8 (2026-11-21) : pas un correctif, le raccord -- adresses avec {greffe}, section « Raccord fait », deux restes retires ; 52 lignes « OK » comptees par grep -c sur tests.ps1 NumerotationCompteur -Detail, duree mesuree 54-57 s. -->
