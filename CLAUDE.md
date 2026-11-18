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
powershell -NoProfile -File D:\AutoWinOS\scripts\hdesk-lancer.ps1 `
  -Id <ton-id> -Executable "D:\RigV3Desktop\bin\Debug\net10.0-windows\RigV3Desktop.exe" `
  -Travail "<ce que tu vérifies>"

powershell -NoProfile -File D:\AutoWinOS\scripts\hdesk-observe.ps1 `
  -InstanceId <ton-id> -Output <chemin.png>
```

Puis **lis l'image**. Un code de sortie non nul du lanceur (4 = l'app est morte,
3 = aucune fenêtre, 1 = échec) est une erreur de ton tour : lis `erreur` et
`journalWindows`, corrige, relance — ne bascule pas sur l'écran de l'utilisateur.

### Ne demande JAMAIS à l'utilisateur de fermer sa fenêtre

« Ferme ton app et je fais un build complet » est une **faute**, pas une précaution :
tu lui fais payer une interruption pour une preuve que tu peux obtenir sans lui.
Deux raisons mesurées :

1. **Le hot reload tourne déjà.** L'utilisateur lance `dev.ps1` (`dotnet watch`) :
   un `.razor`, un `.cs` et les CSS/JS de `wwwroot` sont **repris à chaud dans
   l'application ouverte**, sans build de ta part et sans redémarrage. Ton édition
   est donc **déjà à l'écran** — dis-le, ne demande rien.
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
