# Architecture de la V3 de RIG

Lot 1 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-20. Décision de départ : **Q4** de
`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\cadrage.md` — une base SQL Server V3 par greffe,
posée à côté de sa base V2 ; une partie serveur .NET 10 hébergée chez Amitel ; l'écran RigV3Desktop
sur les serveurs RDS.

Ce fichier dit **ce qui a été choisi et pourquoi**, puis **comment ajouter** une table, une route, un
écran et un banc. La règle qui tient tout : **un ajout = des fichiers à soi**. Aucune liste commune à
compléter pour une table, une route ou un banc ; les lots travaillent en parallèle sans se marcher dessus.

## Les quatre couches

```
 Écran (poste RDS)                     Serveur (chez Amitel)                Base (à côté de la V2)
 ───────────────────                   ─────────────────────                ──────────────────────
 web/src/ecrans/EcranX.tsx             Serveur/<Thème>/<Sujet>/             RIGV3_<GREFFE> (SQL Server)
   │ appeler('EcranX.Methode')           Module<Sujet>.cs  ── SQL ──────▶     schémas soc, num, rec…
 Pont/EcranX.cs                          (IModuleServeur, trouvé seul)        journal rigv3.Migration
   │ ClientServeur.Actif ? ── HTTP JSON ──▶  routes MapGet / MapPost         ▲
   │                  : JeuDemo          │                                    │ Base/RigV3.Migrations
 Pont/ClientServeur.cs                  BaseRig (chaîne de connexion)          │ (scripts Base/migrations)
          └──────── types partagés : Contrats/<Thème>/<Échange>.cs ────────┘
```

| Dossier | Projet | Rôle |
|---|---|---|
| `Serveur\` | `RigV3.Serveur.csproj` (ASP.NET Core, .NET 10) | les routes, un module par fichier |
| `Base\` | `RigV3.Migrations.csproj` (outil console) | joue `Base\migrations\*.sql`, tient le journal ; porte `BaseJetable` pour les bancs |
| `Contrats\` | `RigV3.Contrats.csproj` (bibliothèque) | les types échangés entre l'écran et le serveur, et l'adresse de leurs routes |
| `Pont\ClientServeur.cs` | dans l'app (`RigV3Desktop.csproj`) | appelle le serveur si `RIGV3_SERVEUR` est posée ; sinon rien ne change |
| `docker-compose.yml` | — | SQL Server 2022 Developer du poste de développement |
| `tests\ServeurCheck\` | banc | prouve le tout de bout en bout (base jetable, migrations, `/sante`, client) |

`RigV3Desktop.csproj` exclut `Serveur\`, `Base\`, `Contrats\` et `docs\` comme `tests\` : rien de ces
dossiers (ni leurs `obj\`) n'entre dans l'application. L'app **référence** seulement
`Contrats\RigV3.Contrats.csproj`.

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **ASP.NET Core, API minimales** (`MapGet`, `MapPost`), pas de contrôleurs MVC | Le serveur est une couche d'accès aux données et de règles, sans pages. Une route tient en quelques lignes dans son module. |
| **Modules découverts par réflexion** (`IModuleServeur`, `ServeurRig.Decouvrir`) | Un lot ajoute `Module<Sujet>.cs` et c'est tout : aucune liste à compléter, donc aucun conflit entre lots. Même principe que le pont de l'app (`Pont/Repartiteur.cs`, méthodes `[JSInvokable]` trouvées seules) et le registre des écrans (`web/src/iles.ts`). |
| **Une route en double arrête le démarrage** en nommant les deux modules | C'est le prix de la découverte automatique : deux lots peuvent publier la même adresse sans le savoir. Sans ce contrôle, ASP.NET Core ne le dit qu'à la première requête (« correspondance ambiguë »). |
| **`ServeurRig.Construire`** monte le serveur ; `Program.cs` ne fait que `Run()` | Le banc démarre **le même** serveur, en mémoire (`TestServer`), sur sa base jetable : ce qu'il prouve est ce qui tourne en vrai. |
| **`Contrats\`** partagé par l'app et le serveur | Un champ renommé d'un côté casse la compilation de l'autre, au lieu d'arriver vide à l'écran. JSON en camelCase des deux côtés (`JsonSerializerDefaults.Web`, comme `Repartiteur.Json`). |
| **Migrations SQL écrites à la main**, un fichier par story (`SOC-001_comptes.sql`), pas d'Entity Framework | Le carnet raisonne en tables et en procédures (`tables_v2`, `GENERER_NUMERO`…), la V2 aussi ; la recopie V2 ↔ V3 et la numérotation (Q16) ont besoin de SQL précis (verrous, transactions). Le nom par story relie chaque table à son exigence. |
| **L'ordre vient de l'en-tête `-- apres: SOC-001`**, pas du nom | Les noms suivent les stories, pas l'ordre de construction : `NUM-001` passerait avant `SOC-001` dans l'alphabet. Un numéro d'ordre global obligerait les lots à se le disputer. Tri topologique ; ex æquo départagés par le nom ; dépendance inconnue ou circulaire = arrêt avant de rien jouer. |
| **Journal `rigv3.Migration`** (nom, empreinte SHA-256, rang, date, durée), inscrit dans la **même transaction** que le script | Une migration est jouée et inscrite, ou ni l'un ni l'autre. Une migration jouée puis modifiée est refusée : on en écrit une nouvelle. L'empreinte ignore BOM et fins de ligne (réglage git du poste). |
| **Un schéma SQL par thème** (`soc`, `num`, `rec`… : le préfixe de la story en minuscules), créé par l'outil | Deux thèmes ne peuvent pas se disputer un nom de table, et aucun lot n'a à savoir lequel crée le schéma. |
| **Verrou `sp_getapplock`** pendant un passage de l'outil | Deux déploiements lancés en même temps sur la même base : le second attend au lieu de rejouer les mêmes scripts. |
| **SQL Server 2022 Developer dans Docker** (`docker-compose.yml`), écoute sur `127.0.0.1:14330` | Même moteur que la cible, gratuit, identique sur chaque poste, jetable (`docker compose down -v`). Port 14330 : un SQL Server installé sur le poste tient souvent 1433. Jamais exposé au réseau. |
| **Collations** : serveur `French_CI_AS` (celle de `SQL-DEV\DEV`), bases `SQL_Latin1_General_CP1_CI_AS` (celle de `RIG_DEV`) — lues le 2026-11-20 | La recopie V2 ↔ V3 doit comparer les textes comme la V2. À revérifier sur les serveurs des greffes au déploiement (`SQL-DEV` est en SQL Server 2017, 14.0.3485.1). |
| **Une base jetable par banc** (`BaseJetable`), nommée `rigv3_banc_<Banc>_<instant>_<hasard>` | Plusieurs lots lancent leurs bancs en même temps sur le même serveur : chacun a sa base, la supprime à la fin. |
| **Garde-fou : une base ne se crée que sur le poste** (`BaseJetable.EstServeurLocal`) | Règle commune du plan : jamais d'écriture sur SQL-DEV ni SQL-PROD. C'est le code qui la tient (bancs et `--creer` de l'outil), vérifié par `ServeurCheck` — contre-épreuve faite : garde-fou retiré, le banc passe au rouge. |
| **`RIGV3_SERVEUR`** décide de la source des données de l'écran | Absente (l'exe seul, les bancs, `dev.ps1 -Demo`) : `ClientServeur.Actif` vaut `null`, l'écran reste sur `Metier/JeuDemo.cs`, les 33 bancs existants ne voient aucune différence. Posée mais illisible : erreur nommée, pas de retour discret à la démonstration. |
| **`Microsoft.Data.SqlClient` 7.1.2** | La 6.1.6 tire `System.Security.Cryptography.Pkcs` 9.0.11, signalé vulnérable à la restauration (NU1902 / NU1903). |
| **`/sante`** : 200 si la base répond, 503 sinon, avec la cause | Première question d'un exploitant et d'un répartiteur de charge ; elle dit aussi où en sont les migrations de la base. |

## Travailler en développement

```powershell
# 1. Le SQL Server du poste (une fois ; il redémarre avec Docker)
docker compose up -d

# 2. Une base de développement, à jour de toutes les migrations
$cs = "Server=127.0.0.1,14330;Database=RIGV3_DEV;User ID=sa;Password=RigV3_dev_2026!;TrustServerCertificate=True"
dotnet run --project Base\RigV3.Migrations.csproj -- --base $cs --creer
dotnet run --project Base\RigV3.Migrations.csproj -- --base $cs --liste     # ce qui est joué / à jouer

# 3. Le serveur sur cette base
$env:ConnectionStrings__RigV3 = $cs
dotnet run --project Serveur\RigV3.Serveur.csproj --urls http://127.0.0.1:5080
#    http://127.0.0.1:5080/sante -> {"etat":"ok","base":"RIGV3_DEV","migrationsJouees":…}

# 4. L'app branchée sur ce serveur (sans cette variable, elle reste sur JeuDemo)
$env:RIGV3_SERVEUR = "http://127.0.0.1:5080"
```

Le mot de passe `RigV3_dev_2026!` ne protège qu'un serveur local sans donnée réelle. Pour le changer :
`RIGV3_SQL_MDP` (et `RIGV3_SQL_PORT`) avant `docker compose up`, et la chaîne complète aux bancs par
`RIGV3_SQL`. Repartir de zéro : `docker compose down -v`.

### Travailler sur les données de développement (copie anonymisée de RIG_DEV)

**C'est le mode par défaut des lanceurs depuis le 2026-11-24** (décision de l'utilisateur : « on n'est plus une
démo, on est le vrai projet ») : `dev.ps1` — que lance aussi le bouton « Lancer » d'Autowin
(`.autowin\scripts.json`) — et `lancer.ps1` (raccourci du bureau) ouvrent l'app sur une copie anonymisée de
RIG_DEV, sans jamais avoir une donnée en clair sur le poste. Si ces données ne sont pas disponibles (Docker arrêté,
préparation en échec), le lanceur s'arrête et le dit : il ne retombe jamais en silence sur la démonstration. Il les
prépare **avant** de fermer l'instance ouverte : Docker arrêté, l'application déjà à l'écran reste ouverte. Banc :
`.\tests.ps1 Lanceurs` (l'ordre dans les deux scripts, et `dev.ps1` lancé avec un Docker injoignable : code 1, la
cause, le recours `-Demo`, rien de lancé ; contre-épreuves faites : ordre d'avant remis, rouge ; message de Docker
d'avant remis, rouge).

```powershell
.\dev.ps1                      # mode développement (reprise à chaud) sur les données de dev
.\lancer.ps1                   # compile puis lance l'exe, sur les données de dev
.\dev.ps1 -Demo                # (ou .\lancer.ps1 -Demo) la démonstration, Metier\JeuDemo.cs, sans serveur
.\donnees-dev.ps1 -Refaire     # refaire la copie (RIG_DEV a changé, migration nouvelle)
.\donnees-dev.ps1 -Arreter     # arrêter le serveur lancé par le script
```

L'exe lancé seul, sans `RIGV3_SERVEUR` (les bancs, un bureau caché sans le script), reste sur la démonstration :
c'est le comportement de l'app elle-même, que les bancs exigent. Pour voir l'app comme l'utilisateur la voit,
appeler `& .\donnees-dev.ps1` dans le même PowerShell avant de la lancer.

| Étape | Qui la fait | Ce qui se passe |
|---|---|---|
| Bases | `RigV3.Repetition donnees-dev` (`Outils\Repetition\DonneesDev.cs`) | RIG_DEV est **lu** sur `SQL-DEV\DEV` (SELECT seul) ; la copie s'écrit **déjà anonymisée** sur le SQL Docker du poste (SOC-021 : noms, adresses, documents remplacés), réduite à 200 lignes par table et fermée sur ses clés, **sauf** les tables que lisent les reprises du transfert (21 au 24/11 : comptes, droits, réglages, compteurs, catégories, correspondances des anciens numéros), copiées en entier ; elle est transférée dans une base V3 (comptes, droits, réglages, compteurs : les reprises de la phase 1) ; une base commune porte le greffe **3801** au catalogue, base V2 = la copie, base V3 = la base transférée. ~19 min la première fois ; ensuite, quelques secondes (bases retrouvées). |
| Agent | la même commande | un compte **actif, administrateur d'Amitel**, avec un service et des rôles (les quatre contrôles de SOC-001), qui gère les droits s'il y en a un. Son identifiant est un pseudonyme : ta session Windows n'est pas dans la copie, l'app ouvre donc le greffe par `RIGV3_COMPTE`, que seul un serveur en `Development` écoute. Il est aussi inscrit dans la base commune (routes communes). |
| Serveur | `donnees-dev.ps1` | compilé dans un dossier neuf, `%LOCALAPPDATA%\RigV3\serveur-dev-<date>` (il ne verrouille ni `Serveur\bin` ni `bin\Debug`, et la dll du serveur en cours ne bloque pas la compilation suivante), lancé sur `http://127.0.0.1:5080` avec la base commune, l'accès aux bases des greffes par le compte `sa` du Docker, en `Development` ; au lancement suivant, **repris tel quel si son code est à jour, relancé sinon** (paragraphe suivant) ; `-Arreter` l'arrête, lui seul (pid et dll notés dans `serveur-dev.json`, un fichier par port : `serveur-dev-<port>.json` hors 5080). |
| App | `donnees-dev.ps1` | `RIGV3_SERVEUR`, `RIGV3_COMPTE`, `RIGV3_GREFFES=3801` posées dans le processus PowerShell : `dotnet watch` (via `dev.ps1`) ou l'exe (`-Lancer`) en hérite. |

**Le serveur suit le code du dépôt** (2026-11-25). Avant, un serveur déjà lancé était repris tel quel, même construit
sur un code d'avant les derniers lots : le 25/11 à 10h25, `/sante` rendait `0b39f4f`, sans le lot 2 (`7feba2c`), et
les écrans branchés par les lots 2 et 3 restaient sur leurs exemples dans l'app de l'utilisateur. Désormais, à chaque
appel, le script lit le commit qui suit « + » dans la version de `/sante` (le SDK .NET y inscrit la tête du dépôt à la
compilation) et le compare au dépôt : `git diff --quiet <commit> HEAD -- Serveur Contrats Base Directory.Build.props …`
— `Serveur\`, les projets qu'il référence de proche en proche (lus dans les `ProjectReference`), et les fichiers que
MSBuild lit à la racine. Un écart : il compile dans un dossier neuf, **puis** arrête l'ancien (le sien seul), **puis**
démarre le neuf, et le dit en une ligne : `Serveur de dev perime (0b39f4f) : relance sur ef44766 (pid 44128, coupure
1.5 s)` (mesuré le 25/11 : 13 s en tout, 1,5 s sans serveur). Un changement de `web\` (React) ou de l'écran seul ne
relance rien. Restent tels quels, avec un avertissement : un serveur lancé hors du script (jamais arrêté : l'arrêter
soi-même, ou `-Port <autre>`) ; une version sans commit, un commit inconnu du dépôt, git absent (serveur repris sans
vérification) ; une compilation en échec (l'ancien reste en service). La comparaison porte sur `HEAD` : une
modification non commitée de `Serveur\` ne relance rien (`-Arreter`, puis le script, pour la prendre). Une fois le neuf
en service, les dossiers des compilations précédentes sont effacés, sauf celui d'un serveur encore lancé (autre port).
Banc : `.\tests.ps1 Lanceurs` (les deux fonctions du script jouées sur un dépôt git jetable : React et écran seuls →
à jour ; `Serveur`, `Contrats`, une référence de `Contrats`, `Directory.Build.props` → périmé ; version sans commit,
commit inconnu, git absent → illisible ; contre-épreuves faites : comparaison sur tout le dépôt, références non
suivies, fichiers de la racine oubliés, ancien serveur arrêté avant la compilation → rouge à chaque fois).

**Le serveur survit à la session qui l'a lancé.** Il est lancé par WMI (`Win32_Process.Create`, fonction
`Start-HorsSession` du script) : son parent est le service WMI, pas le PowerShell appelant. Lancé par `Start-Process`,
il était l'enfant de l'appelant, donc dans son Job Windows. Or une session d'agent Autowin est un Job qui tue ses
processus à sa fermeture : le 25/11, le serveur relancé à 10h39 depuis une session d'agent est mort à 10h48 avec elle,
l'ancien déjà arrêté, et le port 5080 est resté vide. L'environnement du script est recopié en entier, avec les
variables du serveur en plus : `Win32_ProcessStartup.EnvironmentVariables` remplace tout le bloc (mesuré : sans
recopie, 4 variables, ni `Path` ni `dotnet`). Banc : `.\tests.ps1 Lanceurs` met l'appelant dans un Job, le termine,
puis exige que le processus lancé vive encore et ait reçu ses variables (rouge par `Start-Process`, rouge par WMI sans
recopie de l'environnement).

**Branchée, l'app renvoie les préférences de l'agent au serveur** (SOC-013, `Pont/PreferencesServeur.cs:12-18`) : à
l'ouverture, elle relit ses onglets dans la base et les pose dans `accueil.json`, et chaque réglage changé y repart.
Sur un bureau caché, `RIGV3_ETAT` protège donc le fichier de l'utilisateur, **pas** ses onglets : ceux de l'agent de
la copie sont les siens. Pour une capture, lancer l'app avec le `RIGV3_COMPTE` d'un **autre** agent admis et sans
préférences (liste : `GET /socle/3801/comptes` ; parade du lot 3, 25/11). À défaut, refermer l'onglet ouvert et
reprendre l'onglet actif d'origine avant de fermer l'app (vérifié le 25/11 : `GET /socle/3801/preferences` identique
avant et après).

Les noms des trois bases et de l'agent : `%LOCALAPPDATA%\RigV3\donnees-dev.json` ; journal du serveur :
`serveur-dev.log` à côté. Une préparation refaite supprime l'ancien jeu **après** sa réussite (un échec laisse
l'ancien en service). Les bases sont des bases de répétition (`rigv3_rep_…`) : `RigV3.Repetition supprimer`
les accepte.

Ce que ces données ne donnent pas : les écrans qui n'ont pas encore de partie serveur (actes, audiences,
facturation… : phase 3) restent sur leur démonstration ; hors socle, la copie est réduite (200 premières lignes par
table et leurs parents), pas le volume de RIG_DEV.

Le serveur est lancé par `cmd`, qui écrit son journal, sans redirection côté PowerShell : redirigé, il héritait des
canaux du script, et un appelant qui lisait sa sortie (pipe, `Tee-Object`, un lanceur) attendait la fin du serveur,
c'est-à-dire jamais (mesuré le 2026-11-24).

Les bancs : `.\tests.ps1 ServeurCheck` (ou `.\tests.ps1` pour tous). Sans `docker compose up -d`, un banc
qui a besoin de la base est **rouge** et le dit (il attend 90 s que SQL Server démarre, puis abandonne).

## Recette 1 — ajouter une table

1. Créer `Base\migrations\<STORY>_<libelle>.sql` : la story du carnet qui exige la table, un libellé en
   minuscules (`SOC-002_droits.sql`). Plusieurs fichiers par story si besoin (`NUM-005_reprise.sql`,
   `NUM-005_reprise-index.sql`).
2. La table va dans le **schéma du thème** : `CREATE TABLE soc.Droit (…)`. Noms en français, en PascalCase,
   au singulier (`soc.Compte`, `num.Compteur`) ; l'outil crée le schéma s'il manque.
3. Si elle a besoin d'une autre migration, le dire dans l'en-tête (lignes de commentaire du début) :
   ```sql
   -- SOC-002 : les droits d'un compte (carnet\socle.json, SOC-002).
   -- apres: SOC-001
   CREATE TABLE soc.Droit (…);
   GO
   CREATE PROCEDURE soc.DonnerDroit … ;
   ```
   `-- apres: SOC-001` vise toutes les migrations de la story ; `-- apres: SOC-001_comptes`, une seule.
   `GO` seul sur sa ligne coupe les lots (obligatoire avant `CREATE PROCEDURE`, `CREATE VIEW`…).
4. Pas de `BEGIN TRAN` / `COMMIT` dans le script : l'outil enveloppe chaque migration dans sa
   transaction. Pas de `USE` : l'outil est déjà sur la bonne base.
5. Une migration **déjà jouée** ne se modifie plus (l'outil refuse) : on en ajoute une nouvelle.
6. Preuve : le banc de la story joue `Base\migrations` sur sa base jetable (recette 4). `ServeurCheck`
   rejoue aussi **toutes** les migrations du dépôt sur une base neuve : une migration cassée le rend rouge.

Aucun fichier commun à toucher.

## Recette 2 — ajouter une route

1. Le type échangé : `Contrats\<Thème>\<Échange>.cs` (namespace `RigV3.Contrats.<Thème>`), un `record`
   et la classe des adresses du thème :
   ```csharp
   namespace RigV3.Contrats.Socle;
   public static class RoutesComptes { public const string Liste = "/socle/comptes"; }
   public sealed record CompteAgent(string Identifiant, string Nom, bool Actif);
   ```
   Adresse : `/<thème>/<sujet>` en minuscules (`/socle/comptes`, `/numerotation/compteurs`).
2. Le module : `Serveur\<Thème>\<Sujet>\Module<Sujet>.cs`, une classe qui implémente `IModuleServeur`
   (constructeur sans paramètre) :
   ```csharp
   namespace RigV3.Serveur.Socle.Comptes;
   internal sealed class ModuleComptes : IModuleServeur
   {
       public void Brancher(IEndpointRouteBuilder routes)
           => routes.MapGet(RoutesComptes.Liste, async (BaseRig b, CancellationToken ct) =>
           {
               await using var cn = await b.OuvrirAsync(ct);
               // … SqlCommand paramétrée sur soc.Compte …
               return Results.Ok(comptes);
           });
   }
   ```
   La base arrive en paramètre (`BaseRig`) ; un service propre au module s'inscrit dans
   `Inscrire(IServiceCollection, IConfiguration)` (méthode facultative de l'interface). Modèle complet :
   `Serveur\Sante\ModuleSante.cs`.
3. Il est trouvé au démarrage, sans rien d'autre. Si un autre module publie déjà la même adresse avec le
   même verbe, le serveur refuse de démarrer en nommant les deux.
4. Authentification (lot 2, session Windows) : elle s'inscrit dans `Inscrire` de son module
   (`services.AddAuthentication(…)`, `AddAuthorization()`). ASP.NET Core ajoute alors lui-même les
   middlewares d'authentification et d'autorisation
   ([documentation Microsoft](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/security)) :
   `ServeurRig.cs` n'a pas à changer.
5. Plusieurs bases (une par greffe, lot 4, SOC-008) : `BaseRig` sert **une** chaîne
   (`ConnectionStrings:RigV3`). Le choix de la base du greffe s'ajoute comme un service du module du
   lot 4 (inscrit dans son `Inscrire`), que ses routes reçoivent en paramètre — sans modifier `BaseRig`.

Aucun fichier commun à toucher.

## Recette 3 — ajouter un écran

Ce qui fait **exister** un écran ne touche aucun fichier commun ; le **placer dans le menu**, si (voir 5).

1. `web\src\ecrans\EcranX.tsx`, `export function EcranX(props)` : monté par son nom, sans liste
   (`web/src/iles.ts`). Style de la maquette, thèmes clair et sombre (mode d'emploi : `web/README.md`).
2. `Pont\EcranX.cs`, `public static class EcranX`, méthodes `[JSInvokable("EcranX.Methode")]` statiques :
   trouvées par réflexion (`Pont/Repartiteur.cs`). Les données :
   ```csharp
   [JSInvokable("EcranComptes.Lister")]
   public static async Task<CompteAgent[]> Lister()
       => ClientServeur.Actif is { } serveur
           ? await serveur.LireAsync<CompteAgent[]>(RoutesComptes.Liste)
           : JeuDemo.Comptes;          // sans RIGV3_SERVEUR : la démonstration, comme aujourd'hui
   ```
3. `web\src\ecrans\EcranX.test.tsx` (vitest, faux pont de `web/src/test/faux-pont.ts`) : joué par
   `.\tests.ps1` sous le nom `WebVitest`.
4. Les types TypeScript de l'écran reprennent ceux de `Contrats\` en camelCase, dans le `.tsx` ou à côté.
5. **Le placer dans le menu** est le seul geste partagé : une entrée dans `Pont/MenuAccueil.cs`, son nom
   dans `Pont/CatalogueEcrans.cs`, son `case` dans `web/src/plan/Aiguillage.tsx`. Ce sont des listes
   **éditoriales** voulues (l'ordre et le regroupement du menu sont un choix de produit, tenus par
   `MenuGestesCheck` et `NomMetierCheck`), pas de la plomberie. Remplacements ciblés, jamais de
   réécriture. En phase 1, le lot 3 (écran des comptes) est le seul prévu au menu. Sa liste de fichiers
   ne contient que `Pont/CatalogueEcrans.cs` : `Pont/MenuAccueil.cs` et `web/src/plan/Aiguillage.tsx`
   n'y sont pas. Ils devront s'y ajouter. Relire leur propriétaire avant d'écrire : d'autres fils
   travaillent dans ce dossier.


## Recette 4 — ajouter un banc

1. `tests\<Nom>Check\<Nom>Check.csproj` + `Program.cs`. `.\tests.ps1` le trouve seul (tout `*.csproj` de
   `tests\`) et le lance par `.\tests.ps1 <Nom>`. Code de sortie : 0 = vert.
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net10.0</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
     </PropertyGroup>
     <ItemGroup>
       <Compile Include="Program.cs" />
     </ItemGroup>
     <ItemGroup>
       <ProjectReference Include="../../Serveur/RigV3.Serveur.csproj" />
       <ProjectReference Include="../../Base/RigV3.Migrations.csproj" />
       <ProjectReference Include="../../Contrats/RigV3.Contrats.csproj" />
       <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.13" />
     </ItemGroup>
   </Project>
   ```
2. Le squelette (le modèle complet est `tests\ServeurCheck\Program.cs`) :
   ```csharp
   using Microsoft.AspNetCore.TestHost;   // UseTestServer, GetTestClient
   using RigV3.Base;                      // BaseJetable, Migrateur
   using RigV3.Serveur;                   // ServeurRig

   var racine = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
   var echecs = new List<string>();
   void Exiger(bool ok, string quoi)
   {
       Console.WriteLine((ok ? "OK   " : "ECHEC") + " : " + quoi);
       if (!ok) echecs.Add(quoi);
   }

   await using (var b = await BaseJetable.CreerAsync("SocleDroitsCheck"))      // SA base, supprimée en sortie
   {
       await Migrateur.AppliquerAsync(b.Connexion, Path.Combine(racine, "Base", "migrations"));
       await using var app = ServeurRig.Construire([], s =>
       {
           s.WebHost.UseTestServer();                                           // le vrai serveur, en mémoire
           s.Configuration["ConnectionStrings:RigV3"] = b.Connexion;
       });
       await app.StartAsync();
       var http = app.GetTestClient();
       // … un contrôle par critère, qui cite son id :
       Exiger(/* … */, "SOC-002 CA03 : un agent sans droit ne voit pas l'écran des comptes");
   }
   return echecs.Count == 0 ? 0 : 1;
   ```
3. Un contrôle par critère `CAxx` de la story, qui **cite l'id** (règle 5 du plan). Tout refus a son
   jumeau accepté (même borne, autre côté).
4. Lancer `docker compose up -d` avant. Le banc ne vise jamais SQL-DEV ni SQL-PROD : `BaseJetable` refuse
   tout serveur qui n'est pas le poste. Les données de la V2 pour la reprise se **lisent** sur `RIG_DEV`
   (`SQL-DEV\DEV`), jamais par `BaseJetable`.

Aucun fichier commun à toucher.

## Ce que ce lot ne fait pas, et pourquoi

| Sujet | Où il sera traité |
|---|---|
| Déployer le serveur chez Amitel, l'app sur les RDS, une base V3 à côté de chacune des 40 bases V2 | Hors poste de développement : pas de droit d'écriture sur les serveurs des greffes. À outiller au déploiement ; l'outil des migrations (`--base`) est déjà celui qu'on lancera, base par base. |
| Le choix de la base du greffe par le serveur | Lot 4 (SOC-008), sans modifier `BaseRig` (recette 2, point 5). |
| L'identité de l'agent (session Windows) côté serveur | Lot 2 (SOC-001), par `Inscrire` de son module (recette 2, point 4). |
| HTTPS entre les RDS et le serveur | Au déploiement (certificat d'Amitel) ; en développement le serveur écoute en HTTP sur `127.0.0.1`. |
| Générer les types TypeScript depuis `Contrats\` | Non fait : les écrans recopient les types (recette 3, point 4). À reconsidérer si les écarts deviennent fréquents. |
