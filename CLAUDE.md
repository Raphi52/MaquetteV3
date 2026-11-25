# RigV3Desktop — consignes de travail

## Compiler : `.\compiler.ps1` — JAMAIS `dotnet build`

Pour savoir si le code compile, lance **`.\compiler.ps1`** (c'est-à-dire
`dotnet msbuild RigV3Desktop.csproj -t:Compile`). Exit 0 = ça compile, exit 1 =
les `error CS` sont dans la sortie.

`dotnet build` et `dotnet run` recopient `bin\Debug\net10.0-windows\RigV3Desktop.exe`.
Quand l'application de l'utilisateur tourne, ce fichier est **verrouillé par Windows** :
la compilation échoue sur **MSB3021 / MSB3027**, et le réflexe qui suit — fermer
l'application pour débloquer — fait **disparaître sa fenêtre en plein travail**.

La cible `Compile` s'arrête **avant** l'édition de liens et la copie : le compilateur
tourne, toutes les erreurs remontent, l'exe n'est jamais touché. Mesuré le 2026-09-17,
application ouverte : `-t:Compile` rend 0 sans que la date de l'exe bouge ; une erreur
de type introduite rend 1 avec son `error CS`.

`compiler.ps1` vérifie aussi les écrans React (`web/`) : `npm run typecheck` (TypeScript
strict), `error TS` dans la sortie. Il ne construit pas `wwwroot/app/` : la construction
Vite est accrochée à la copie vers `bin\` (cibles `ConstruireWeb` / `CopierWeb` du
`.csproj`), que seul un vrai build fait.

Paquets npm : une seule règle, la cible `InstallerWeb` du `.csproj` (`npm ci` quand
`web/package.json` ou `package-lock.json` est plus récent que
`web/node_modules/.rig-install-ok`). `compiler.ps1`, `tests.ps1` et `dev.ps1` (avant de
lancer Vite) l'appellent d'abord. Si un serveur Vite ou vitest de ce dossier tient
`node_modules`, elle **refuse** au lieu d'effacer les paquets sous lui, et nomme le pid :
ne le tue pas s'il n'est pas à toi — compile dans ta propre copie de travail.

## L'interface : WPF + WebView2 + React (Blazor retiré le 2026-11-19)

Chaque fenêtre (`MainWindow`, `FenetreVue`, `FenetreDocument`) porte un contrôle
**WebView2 direct** (paquet `Microsoft.Web.WebView2`) branché par `Pont/PageWeb.cs` :
`wwwroot/` servi sous `https://rigv3.example/`, page React montée par
`web/src/racine.tsx` sur la racine de la fenêtre (`Pont/Racines.cs`). Tout appel au
C# passe par `web/src/pont.ts` (messagerie WebView2) et `Pont/Repartiteur.cs`
(méthodes `[JSInvokable("<NomEcran>.<Methode>")]` de `Pont/`). Il n'y a plus aucun
`.razor` ni paquet `Components.WebView`. Mode d'emploi des écrans : `web/README.md`.

## Ne ferme jamais l'application de l'utilisateur

Pas de `taskkill /IM RigV3Desktop.exe`, pas de `Stop-Process -Name RigV3Desktop`.
Un binaire verrouillé n'est **pas** une raison de fermer : c'est la raison d'utiliser
`compiler.ps1`. Si tu as vraiment besoin d'arrêter un process, ce doit être **un pid
que tu as lancé toi-même**.

`dev.ps1` et `lancer.ps1` ferment bien l'instance en cours — c'est **voulu** : ce sont
les scripts que l'**utilisateur** déclenche lui-même, et ils filtrent sur le chemin
exact de l'exe. Ce ne sont pas tes commandes par défaut.

## Lancer l'application pour la voir : bureau caché, jamais au premier plan

Pour vérifier un rendu, ne lance pas l'app sur l'écran de l'utilisateur — elle
s'afficherait par-dessus son travail et se disputerait le verrou d'instance.
Utilise le bureau caché d'Autowin, avec l'identifiant `-Id` que tes consignes
t'ont donné (`chat-<id du fil>` ou `run-<id du run>`, jamais un nom inventé) :

```
# 1. Une COPIE des réglages de l'utilisateur, pour ne jamais écrire dans les siens
#    (onglets ouverts, cartes du classeur, notes du fil, thème) :
Copy-Item "$env:APPDATA\RigV3\accueil.json" "<dossier temporaire du fil>\accueil.json"
$env:RIGV3_ETAT = "<dossier temporaire du fil>\accueil.json"

# 2. Les données de dev, comme chez l'utilisateur (mode par défaut de dev.ps1 et
#    lancer.ps1 depuis le 2026-11-24) : serveur V3 local, RIGV3_SERVEUR / _COMPTE /
#    _GREFFES posées dans CE PowerShell. Sans cette ligne, l'app montre la démonstration.
& D:\RigV3Desktop\donnees-dev.ps1

# 3. Le lanceur transmet cet environnement à l'app :
powershell -NoProfile -File D:\AutoWinOS\scripts\hdesk-lancer.ps1 `
  -Id <ton-id> -Executable "D:\RigV3Desktop\bin\Debug\net10.0-windows\RigV3Desktop.exe" `
  -Travail "<ce que tu vérifies>"

powershell -NoProfile -File D:\AutoWinOS\scripts\hdesk-observe.ps1 `
  -InstanceId <ton-id> -Output <chemin.png>
```

**Sans `RIGV3_ETAT`, l'app de ton bureau caché écrit dans les réglages de
l'utilisateur** (`%APPDATA%\RigV3\accueil.json`, choisi par `AccueilEtat.Chemin`) :
un onglet que tu ouvres pour vérifier réapparaît chez lui au prochain démarrage.
Mesuré le 2026-11-20 (conv-2041) : avec la variable, l'affaire 004151 ouverte sur
le bureau caché est écrite dans la copie, et le fichier de l'utilisateur ne bouge pas.

**Avec les données de dev, `RIGV3_ETAT` ne suffit plus.** Branchée au serveur
(`RIGV3_SERVEUR`), l'app relit les préférences de l'agent sur le serveur et y renvoie
chaque changement d'onglet (`Pont/PreferencesServeur.cs`). Les onglets que tu ouvres
reviennent donc chez l'utilisateur, puisque son app utilise le même agent. Après
`donnees-dev.ps1`, lance l'app au nom d'un AUTRE agent de la copie :
`$env:RIGV3_COMPTE = "<compteWindows>"`, choisi dans `GET /socle/3801/comptes`
(en-tête `X-RigV3-Compte`). Mesuré le 2026-11-25 (plan-03880acb, lot 3) : les
préférences de l'agent de l'utilisateur sont restées identiques. Sinon, referme
l'onglet ouvert et remets l'onglet actif d'origine avant de fermer.

Puis **lis l'image**. Un code de sortie non nul du lanceur (4 = l'app est morte,
3 = aucune fenêtre, 1 = échec) est une erreur de ton tour : lis `erreur` et
`journalWindows`, corrige, relance — ne bascule pas sur l'écran de l'utilisateur.

### Ne demande JAMAIS à l'utilisateur de fermer sa fenêtre

« Ferme ton app et je fais un build complet » est une **faute**, pas une précaution :
tu lui fais payer une interruption pour une preuve que tu peux obtenir sans lui.
Deux raisons mesurées :

1. **Le hot reload tourne déjà.** L'utilisateur lance `dev.ps1` : il démarre le
   **serveur Vite** (port libre, `RIGV3_VITE`) puis `dotnet watch`, et la fenêtre
   charge sa page depuis Vite. Un `.tsx`/`.ts` de `web/src` est repris à chaud par
   Vite, un `.cs` par `dotnet watch`, les CSS/images de `wwwroot` par la fenêtre
   elle-même : **dans l'application ouverte**, sans build de ta part et sans
   redémarrage. Ton édition est donc **déjà à l'écran** — dis-le, ne demande rien.
   (Prouvé le 2026-11-19 : `dev.ps1` lancé sur un bureau caché, une retouche de
   `web/src/accueil/Entete.tsx` est apparue dans la fenêtre, même pid.)
2. **Le bureau caché n'a pas besoin d'un build neuf.** Aucun verrou d'instance
   mono n'existe dans ce projet (vérifié le 2026-09-17 : aucun `Mutex` dans le
   code). Tu peux donc lancer **l'exe déjà présent** dans
   `bin\Debug\net10.0-windows\` sur ton bureau caché pendant que la fenêtre de
   l'utilisateur tourne, et capturer là.

Donc l'ordre est : `compiler.ps1` (vert) → bureau caché sur l'exe existant →
capture. Si cet exe est **antérieur** à ton édition, ne rebuild pas et ne ferme
rien : dis en une ligne que la capture montre l'état d'avant, ou appuie-toi sur le
hot reload de l'utilisateur.

Un `dotnet build` complet (exe libre) reste **son** geste, via `dev.ps1` /
`lancer.ps1` — jamais une demande que tu lui adresses. Et un build dans un dossier
de sortie séparé **ne marche pas** ici (essayé le 2026-09-17 :
`-p:BaseOutputPath=…` casse sur les `*_wpftmp` de `obj\Debug`, 3 483 erreurs
CS0579) : ce n'est pas un contournement à retenter.

## Tests

`.\tests.ps1`
