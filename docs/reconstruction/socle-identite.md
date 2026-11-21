# Socle — identité de l'agent (SOC-001)

Lot 2 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-20. Story **SOC-001 « Ouvrir RIG avec sa session Windows, sans mot de passe »**
(`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`), construite sur
l'architecture du lot 1 (`docs\reconstruction\architecture.md`).

## Ce que fait la V3

L'agent ouvre RIG depuis le serveur RDS : l'écran transmet sa **session Windows** au serveur
(Kerberos ou NTLM, aucune saisie). Le serveur cherche le compte dans la base du greffe, sans tenir
compte des majuscules, puis applique **les quatre contrôles de la V2, dans son ordre** :

| Ordre | Contrôle (V2 : `CheckUser`) | Statut V3 | Message (texte de la V2) | Critère |
|---|---|---|---|---|
| 1 | compte absent de la base | `inconnu` | L'utilisateur '<compte>' n'a pas été défini dans RIG. | CA03 |
| 2 | aucun rôle | `sans-droit` | Aucune habilitation n'a été définie pour l'utilisateur '<compte>'. | CA05 |
| 3 | compte désactivé | `desactive` | L'utilisateur '<compte>' a été désactivé. | CA04 |
| 4 | aucun service rattaché | `sans-service` | Aucun service n'est rattaché à l'utilisateur '<compte>'. | CA06 |

Le premier contrôle qui échoue donne le refus : un compte désactivé **et** sans rôle est refusé pour
« aucune habilitation », comme dans la V2. Admis, l'accueil s'ouvre et son entête salue l'agent par
« Prénom Nom » (CA01). Refusé, l'écran de refus remplace l'application entière : le compte, la raison,
la hotline d'Amitel et un seul bouton, « Fermer RIG ». L'accueil n'est jamais ouvert, rien n'est lu
ni écrit (CA03).

Sources V2 relues avant de coder (lecture du 2026-11-20) :
`RIG\EXE\Accueil\RigClientAccueil\RigConsoleAccueil.cs:76-78` (paramètre `utilisateur=`) et `:113-133`
(les quatre refus, ordre et messages) ; `RIG\DLL\Processus\PROC_ACCUEIL\RigConsoleAccueil.cs:76` (même
contrôle) ; `Outils\AmiDLL\AmiSystem\AmiEnvironment.cs:14` (`System.Environment.UserName`, sans domaine) ;
`Outils\DLL\RigMetier\Habilitation\Utilisateur.cs:250-253` (`RoleUsers`, jamais null), `:89-96`
(`ServiceAssocie`), `:421-432` et `:530` (recherche par `ToUpper`) ;
`BaseDeDonnées\RigDatabase\dbo\Tables\UTILISATEUR.sql:8-9,14,58-59`, `SERVICE.sql`,
`HABILIT_ROLE_USER.sql`, `HABILIT_UTILISATEUR_ROLE_USER.sql`.

## Où est le code

| Couche | Fichier | Rôle |
|---|---|---|
| Base | `Base\migrations\SOC-001_comptes.sql` | `soc.Service`, `soc.Compte`, `soc.Role`, `soc.CompteRole` — **aucune colonne de mot de passe** |
| Contrat | `Contrats\Socle\IdentiteAgent.cs` | `RoutesIdentite` (`GET /socle/identite`, `GET /socle/identite/{greffe}`, en-tête `X-RigV3-Compte`), `StatutsIdentite`, `IdentiteAgent`, `AgentReconnu` |
| Serveur | `Serveur\Socle\Identite\ModuleIdentite.cs` | l'authentification du serveur, les deux routes, la politique `PolitiquesSocle.AgentAdmis` et sa réponse 404 / 503 quand la base du greffe ne s'ouvre pas |
| Serveur | `Serveur\Socle\Identite\ServiceIdentite.cs` | le compte de l'appelant, la base où le chercher (celle du greffe de l'adresse), la requête SQL, le jugement dans l'ordre de la V2 |
| Écran | `Pont\ClientServeur.cs` (2 retouches) | chaque appel porte la session Windows ; `RIGV3_COMPTE` part dans l'en-tête |
| Écran | `Pont\SessionAgent.cs` | demande au serveur qui est l'agent, à l'ouverture de la fenêtre principale |
| Écran | `MainWindow.xaml.cs` (Loaded) | agent refusé → `RacineRefus` au lieu de l'application |
| Écran | `Pont\EcranRefus.cs`, `web\src\composants\EcranRefus.tsx` | l'écran de refus (racine de fenêtre, thèmes clair et sombre) |
| Écran | `Pont\Accueil.cs` (2 retouches) | l'entête affiche l'agent ; le rond de profil (sélecteur de logins de démonstration) disparaît |
| Banc | `tests\SocleIdentiteCheck\` | un contrôle par critère CA01 à CA08 |

Sans `RIGV3_SERVEUR`, rien ne change : aucun appel, l'accueil reste sur `Metier\JeuDemo.cs` et son
sélecteur de logins. `web\src\accueil\Entete.tsx` n'a pas eu à changer : il affiche déjà
`entete.utilisateur`, que `Pont\Accueil.cs` remplit maintenant avec l'agent du serveur.

## Les choix et leur raison

| Choix | Raison |
|---|---|
| **Negotiate** (`Microsoft.AspNetCore.Authentication.Negotiate` 10.0.13) sur Kestrel, client `SocketsHttpHandler { Credentials = CredentialCache.DefaultCredentials }` | L'authentification intégrée standard d'ASP.NET Core : Kerberos dans le domaine, NTLM sinon. Le serveur reçoit `DOMAINE\compte` ; il garde le compte seul, comme `Environment.UserName` dans la V2. Prouvé de bout en bout par le banc, avec la vraie session de qui le lance. |
| **Le serveur décide du compte**, jamais l'écran | Un écran modifié ne peut pas se faire passer pour un autre agent : en production, seul le compte de la poignée de main Windows compte (CA07). |
| **Changement de compte en développement et en recette seulement** : en-tête `X-RigV3-Compte`, que l'app envoie si `RIGV3_COMPTE` est posée | C'est le `utilisateur=` de la V2, borné par l'écart 2 ci-dessous. Le serveur ne l'écoute que si `ASPNETCORE_ENVIRONMENT` vaut `Development` ou `Recette` ; sans variable, ASP.NET Core est en `Production` et l'ignore. Deux verrous : le sélecteur de schéma et le gestionnaire lui-même. Les bancs s'en servent pour jouer chaque compte. |
| **Aucune route n'exige l'agent d'office** ; une route de données pose `.RequireAuthorization(PolitiquesSocle.AgentAdmis)` | `/sante` reste ouverte (exploitant, répartiteur de charge). La politique rend 401 sans session, 403 pour un compte refusé, et la route ne s'exécute pas. Les lots suivants n'ont qu'une ligne à écrire. |
| **`GestionnaireSessionWindows`** : Negotiate, sauf sur un serveur sans connexion | Negotiate lève `NotSupportedException` sur le serveur en mémoire des bancs (`TestServer`), à **chaque** requête : `ServeurCheck` et tous les bancs futurs seraient rouges. Sans connexion, il n'y a pas de poignée de main possible : la requête reste anonyme. Contre-épreuve : gestionnaire retiré, `/sante` échoue dans le banc sur cette exception. |
| **`GET /socle/identite` répond 200** avec le statut, admis ou refusé | L'écran lit la raison du refus pour l'afficher ; un 403 l'aurait obligé à décoder un corps d'erreur. La protection des données, elle, est un vrai 403 (politique). |
| **Collation `SQL_Latin1_General_CP1_CI_AS` posée sur la colonne** `CompteWindows` | La recherche **et** l'unicité ignorent les majuscules (CA02), quelle que soit la collation de la base. |
| **Compte plus long que 35 caractères = inconnu**, sans requête | Le paramètre SQL `varchar(35)` tronquerait le nom et pourrait reconnaître un autre compte. |
| **Les quatre tables dans la migration de SOC-001** | Les quatre contrôles en ont besoin, et `tables_v2` de la story les cite (`UTILISATEUR`, `HABILIT_UTILISATEUR_ROLE_USER`, `SERVICE`). `soc.Role` reprend `HABILIT_ROLE_USER` (rôles réservés à Amitel ou aux administrateurs). Le lot 3 les **complète** par ses migrations (`-- apres: SOC-001`) : fonctions et origine des droits (SOC-002), distribution des services et création des comptes (SOC-004). |
| **Écran de refus dans `web\src\composants\`**, pas `ecrans\` | C'est une racine de fenêtre (comme `Application.tsx`), pas un écran du plan de travail : `PlanTravailCheck` exige que tout `ecrans\Ecran*.tsx` passe par l'aiguillage. Le chemin `ecrans\` prévu par le plan était une estimation. |

## Écarts code ↔ métier « à valider » (règle 6 du plan)

1. **Compte sans aucun rôle.** La V2 veut le refuser mais teste `RoleUsers == null`, jamais vrai
   (`Utilisateur.cs:250-253` rend une liste, vide au pire) : elle le laisse entrer. Dans `RIG_DEV`,
   1 compte actif sur 96 est dans ce cas (relevé du carnet, 2026-11-20). **La V3 le refuse** (CA05),
   comme la V2 le prévoit, à sa place dans l'ordre (2e contrôle). À valider par le référent Socle :
   ce compte ne pourra plus ouvrir RIG le jour du passage.
2. **Paramètre `utilisateur=`.** La V2 l'accepte au lancement, même en production, et ouvre RIG sous
   n'importe quel compte du greffe. **La V3 l'ignore en production** (CA07) et ne l'accepte qu'en
   développement et en recette (`RIGV3_COMPTE`). À valider.

Une différence de forme, pas de règle : la V2 montre une boîte puis ferme RIG ; la V3 montre un écran
qui ne propose que « Fermer RIG ». Les messages sont ceux de la V2, mot pour mot (« hotline d'Amitel »,
et « hotline d'AMITEL » pour le service manquant).

Le repli de la V2 sur le compte de la **borne web** quand le compte n'est pas trouvé
(`Utilisateur.cs:428-430`) n'est pas repris ici : il relève des échanges et des services (note de la
story).

## Ce qui ne peut pas se faire ici, et pourquoi

| Tâche | Raison | Qui / quand |
|---|---|---|
| **T04 reprise** : recopier les comptes des 40 bases V2 (même compte, même état, même service), sans `UTLST_PASSWORD` | Les comptes sont partagés avec la V2 (Q7) : leur recopie suit le thème Recopie V2-V3 (phase 2). Les colonnes `IdV2` de `soc.Compte` et `soc.Service` sont prêtes pour elle. Règle de reprise de la story : la colonne de mot de passe n'est ni lue, ni copiée, ni journalisée. | Phase 2, thème REC |
| **T05 exploitation** : vérifier, sur chaque serveur RDS (outre-mer compris), que le serveur V3 reconnaît les comptes Windows de tous les greffes servis (domaines, approbations) | Hors poste de développement : pas d'accès aux RDS ni aux domaines des greffes. À noter pour ce contrôle : le serveur garde le compte **sans son domaine**, comme la V2 ; deux domaines qui portent le même nom de compte désigneraient le même agent. | Exploitation, avant la 1re répétition générale |
| **T06, seconde moitié** : rejouer les tests sur les copies anonymisées des 3 greffes bêta | Les copies anonymisées n'existent pas encore. Le banc les jouera tel quel : il crée sa base, y inscrit ses comptes et ne lit rien d'autre. | Quand les copies seront livrées |
| **L'écran choisit son greffe** : `Pont\SessionAgent.cs` appelle encore `GET /socle/identite`, jugé dans la base commune | Le serveur sait juger l'agent dans la base d'un greffe (section « Raccord fait ») ; l'écran ne sait pas encore QUEL greffe il ouvre. La V2 le prend au lancement (`RigFormLogin.cs:282-319` : paramètre `greffe=` ou `CodeGreffe=`, sinon sa fenêtre de connexion, qui choisit d'office le seul greffe du serveur avec `ungreffeselectionauto=1`), puis y cherche le compte. Le raccord : l'écran appelle `RoutesIdentite.Pour(code)` puis les routes de ce greffe. En production, la base commune n'a pas de compte d'agent : sans ce choix, l'écran branché au serveur refusera tout agent de greffe. | Phase des écrans (fichiers `Pont\` hors de ce lot) |
| **HTTPS** entre les RDS et le serveur | Comme au lot 1 : au déploiement, avec le certificat d'Amitel. Negotiate fonctionne en HTTP sur `127.0.0.1` pour le développement. | Déploiement |

## Raccord fait (lot 8, 2026-11-21) : le compte se cherche dans la base du greffe

Comme la V2 (`Utilisateur.GetUtilisateurCourant(CodeGreffeRig)`, `Utilisateur.cs:424-432`), le compte de
l'agent se cherche dans la base **du greffe choisi**, plus dans la base unique du serveur.

| Règle | Ce que fait le serveur |
|---|---|
| **Le greffe choisi est dans l'adresse** : `{greffe}` (`PolitiquesSocle.ParametreGreffe`) | `GET /socle/identite/{greffe}` (`RoutesIdentite.Pour(code)`), `/numerotation/{greffe}/…` : `ServiceIdentite` ouvre la base V3 de ce greffe par le catalogue commun (`CatalogueGreffes.OuvrirBaseV3Async`, lot 4) et y applique les quatre contrôles de la V2. Un agent déclaré dans un seul greffe est **inconnu** des autres. |
| **Greffe absent du catalogue : 404 ; base qui ne répond pas : 503** | La cause est dite (le code, la base nommée), sans repli sur la base commune ni sur un autre greffe. Sur une route de données, la politique `AgentAdmis` rend elle-même ces 404 / 503 au lieu d'un 403 (`ReponseGreffeNonOuvert`, le traitement des refus d'ASP.NET Core, [documentation Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/customizingauthorizationmiddlewareresponse)) ; sans session, 401 d'abord, sans lire le catalogue. |
| **Une route sans `{greffe}` est une route commune** : le compte s'y cherche dans la base commune (`ConnectionStrings:RigV3`) | Le catalogue des greffes (`/socle/greffes/{code}…`, lot 4) en est une : son `{code}` désigne la fiche gérée, pas le greffe où l'agent travaille, et une fiche existe **avant** la base V3 de son greffe (SOC-008 ; dans `SocleGreffesCheck`, la fiche T004 n'a pas de base V3 et sa route de passage doit pourtant répondre). `GET /socle/identite` sans greffe reste l'appel de l'écran d'aujourd'hui (reste ci-dessus). |

Le banc ajoute deux bases de greffe jetables, A et B, au catalogue de sa base commune ; l'agent `agent.a`
n'est inscrit que dans A, `agent.b` que dans B, `JDUPONT` que dans la base commune. Contrôles « Raccord » :
`agent.a` est admis sur A (nom lu dans la base de A) et **refusé sur B** (statut `inconnu`, message de la V2) ;
`agent.b` l'inverse ; `JDUPONT` est inconnu de A ; un compte désactivé dans A y est refusé pour ce motif ;
404 / 503 / 401 sur un greffe absent, muet, sans session ; une route de données `/banc/{greffe}/donnees`
refuse l'agent de A sur B et le compte commun sur A (403), rend 404 / 503 sans tourner.
Contre-épreuves (2026-11-21, fichier restauré à l'identique chaque fois) : identité cherchée dans la base
commune → 8 contrôles rouges ; réponse 404 / 503 de la politique retirée → 1 rouge (403 à la place).

## Preuves

- `.\tests.ps1 SocleIdentite` → code 0 : 37 contrôles (28 du lot 2, 9 du raccord du lot 8), dont chaque critère CA01 à CA08 nommé. CA01 et
  CA07 passent par la **vraie** session Windows (Kestrel sur `127.0.0.1`, client de l'écran avec la
  session) ; CA02 à CA06 et l'ordre des refus par le changement de compte de développement ; CA08 sur
  toute la base (toutes les migrations du dépôt) et sur les sources des écrans (`.tsx`, `.ts`, `.xaml`,
  `.html`), chacun avec son jumeau qui prouve que le contrôle voit un champ ou une colonne de mot de passe.
- Contre-épreuves faites le 2026-11-20, chaque fois banc rouge puis fichier restauré à l'identique :
  gestionnaire `GestionnaireSessionWindows` retiré (`/sante` en échec, `NotSupportedException`) ;
  contrôles « désactivé » et « sans droit » inversés (l'ordre de la V2 est rouge) ; en-tête de
  changement de compte écouté en production (les deux contrôles CA07 rouges).
- `web\src\composants\EcranRefus.test.tsx` (vitest) : compte, raison, assistance, aucun champ, un seul
  bouton.
- Captures sur bureau caché, app branchée à un serveur de production local : entête « Bonjour Raphaël
  Vilain » lu dans la base ; écran de refus pour la même session sur une base qui ne la porte pas
  (`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\SOC-001-*.png`).

## Utiliser en développement

```powershell
# Le serveur sur une base qui porte le compte de la session (voir architecture.md)
$env:ConnectionStrings__RigV3 = "Server=127.0.0.1,14330;Database=RIGV3_DEV;User ID=sa;Password=RigV3_dev_2026!;TrustServerCertificate=True"
dotnet run --project Serveur\RigV3.Serveur.csproj --urls http://127.0.0.1:5080

# Ouvrir RIG sous un autre compte : serveur en développement + compte demandé par l'app
$env:ASPNETCORE_ENVIRONMENT = "Development"   # côté serveur
$env:RIGV3_COMPTE = "jdupont"                  # côté app (ignoré par un serveur de production)
```

Protéger une route de données d'un greffe (lots suivants) : le greffe dans l'adresse, la base par le catalogue.

```csharp
routes.MapGet("/<theme>/{greffe}/<sujet>", async (string greffe, CatalogueGreffes c, CancellationToken ct) =>
      {
          await using var cn = await c.OuvrirBaseV3Async(greffe, ct);   // l'agent vient d'y etre juge
          …
      })
      .RequireAuthorization(PolitiquesSocle.AgentAdmis);   // using RigV3.Serveur.Socle.Identite;
```

Dans un banc : `ServeurRig.Construire(["--environment", "Development"], …)` puis l'en-tête
`X-RigV3-Compte: <compte>` sur chaque requête ; pour une route d'un greffe, une ligne `soc.Greffe` dans la
base commune, `Greffes:Authentification` et le compte dans `soc.Compte` de la base **du greffe** (modèle :
`tests\SocleIdentiteCheck\Program.cs`).

<!-- fix-ok: retouches de redaction, pas de correctif -- (1) « /sante repond 500 » remplace par ce que la contre-epreuve a mesure (NotSupportedException, controle /sante en echec) ; (2) raccord avec le lot 4 ajoute apres son message msg-e5208977 (CatalogueGreffes). Lot 8 (2026-11-21) : pas un correctif -- section « Raccord fait », reste « base du greffe » remplace par le choix du greffe a l'ecran (citation RigFormLogin.cs:282-319 relue a la ligne), 37 controles comptes par grep sur tests.ps1 SocleIdentite -Detail. -->

