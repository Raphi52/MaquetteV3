# web/ — les écrans React de RigV3Desktop

Migration Blazor → React, commencée le 18/11/2026 (plan `plan-4021ed92`, fil chef
`conv-1985`). Le métier reste en C# (`Metier/`, non modifié). Ce dossier contient
les écrans réécrits en React 19 + TypeScript strict, construits par Vite dans
`wwwroot/app/rig-iles.js` (ignoré par git, reconstruit à chaque `dotnet build`).

Ce fichier est le **mode d'emploi d'un portage d'écran** : les lots 2 à 6 le suivent.

## Comment ça tient ensemble

```
Home.razor ──> Components/Ecrans/EcranX.razor (COQUILLE, mêmes paramètres)
                     │  <IleReact Composant="EcranX" Proprietes=… Rappels=… />
                     ▼
               Components/IleReact.razor ──import──> wwwroot/app/rig-iles.js
                     │  monter / mettreAJour / demonter      (web/src/iles.ts)
                     ▼
               web/src/ecrans/EcranX.tsx  (le rendu, mêmes classes CSS)
                     │  appeler<T>('EcranX.Methode', …)       (web/src/pont.ts)
                     ▼
               Pont/EcranX.cs  [JSInvokable("EcranX.Methode")] ──> Metier/
```

- **Coquille** : le `.razor` d'origine garde son nom et ses `[Parameter]` ;
  `Home.razor` ne change pas (jusqu'aux lots 7-8).
- **Île** : `IleReact` monte le composant React dans un `div` en
  `display:contents` (aucune boîte : la mise en page CSS est inchangée).
  Une propriété qui change re-rend le composant sans perdre son état.
- **Pont** : `web/src/pont.ts` est le **seul** fichier de `web/src` qui parle
  au C# (`src/regles.test.ts` le vérifie). Il reste générique : aucun nom d'écran.
  Au lot 9 (retrait de Blazor), c'est le seul fichier à changer côté React.
- **Registre** : `iles.ts` rend montable tout `src/ecrans/<Nom>.tsx` et
  `src/composants/<Nom>.tsx` qui exporte `export function <Nom>`. Aucune
  liste à tenir : deux lots en parallèle ne se marchent pas dessus.

## Porter un écran, pas à pas

1. **Capture AVANT.** Compile l'appli dans ta copie de travail
   (`dotnet build RigV3Desktop.csproj`, jamais dans `D:\RigV3Desktop` — voir
   `CLAUDE.md`), lance cet exe sur ton bureau caché, ouvre l'écran, capture.
   Capture aussi chaque état visible (bascule allumée, onglet choisi…).
2. **Le `.tsx`** : `web/src/ecrans/EcranX.tsx`, `export function EcranX(props)`.
   Recopie le balisage **à l'identique** : mêmes balises, mêmes classes
   (`class` → `className`), même ordre. Garde en tête le commentaire qui cite
   la procédure RIG v2 (`PROC_XXX`) : `SaisieCheck` l'exige.
3. **Le pont** : `Pont/EcranX.cs`, `public static class EcranX` dans
   `namespace RigV3Desktop.Pont`, méthodes `[JSInvokable("EcranX.Methode")]`
   statiques qui **délèguent** à `Metier/` sans le modifier. Un fichier par
   écran : ne touche jamais au pont d'un autre lot.
4. **La coquille** : le `.razor` ne garde que son commentaire d'origine (+ une
   ligne « porté en React ») et
   `<IleReact Composant="EcranX" Proprietes="…" Rappels="…" />`. Exemple
   complet avec rappel : `Components/Ecrans/EcranVerrous.razor`.
5. **Les tests** : `web/src/ecrans/EcranX.test.tsx` (vitest), avec le faux
   DotNet de `src/test/faux-dotnet.ts`. Un banc C# de `tests/` qui relisait
   le `.razor` tient la **même règle** sur le `.tsx` ou l'exécute par le pont
   (exemple : `tests/AlertesCheck`, section « 2 ter »). Jamais supprimé, jamais
   desserré : autant de vérifications ou plus.
6. **Capture APRÈS et comparaison**, même état, même taille de fenêtre :

   ```
   cd web
   npm run comparer -- avant.png apres.png --haut 80
   ```

   `--haut 80` ignore la barre d'onglets du haut (elle varie d'un lancement à
   l'autre). Le résultat attendu est **0 pixel différent** ; sinon ouvre
   l'image `apres-diff.png`, les pixels qui diffèrent y sont en rouge.
7. **Preuves de fin** : `.\compiler.ps1` = 0 et `.\tests.ps1` = 0.

## Ce qui passe par le pont, et ce qui reste dans le `.tsx`

| L'écran Blazor… | Côté React |
|---|---|
| lisait `Metier/` (`JeuDemo`, `AlertesGreffe`…) | une méthode de `Pont/EcranX.cs` |
| calculait sur `Metier/` dans son `@code` (filtre, tri, regroupement) | **dans le pont**, à l'identique : même résultat, même ordre (le tri de chaînes .NET n'est pas celui de JS) |
| déclarait ses propres listes de démo dans `@code` | constantes du `.tsx` |
| tenait un état d'écran (saisie, bascule, onglet) | `useState` |
| recevait un `[Parameter]` valeur | `Proprietes` de l'île |
| levait un `EventCallback` | `Rappels` de l'île → fonction en props |

## Pièges déjà rencontrés (lot 1)

- **Les blancs entre éléments en ligne.** Razor garde l'espace entre
  `</button>` et `<span>` écrits sur deux lignes ; JSX le supprime. Dans un
  bloc qui n'est pas en colonne souple (flex), ça décale ce qui suit de la
  largeur d'une espace. Écris `{' '}` là où l'original avait ce blanc
  (Factures : la pastille « Solde client », vue à la comparaison des captures).
- **Les nombres.** `ToString("N2")` sous fr-FR → `nombre(x, 2)` de
  `src/format.ts` (virgule décimale, milliers séparés par U+202F comme .NET).
  Les montants se calculent en **centimes entiers** (`centimes`, `euros`) :
  le C# calculait en `decimal`, exact.
- **`@bind` d'un nombre** : une saisie illisible ne remet pas la valeur à
  zéro. Garde le texte saisi ET la dernière valeur lisible (Factures,
  « Montant encaissé »).
- **Réponse du pont** : elle arrive après le premier rendu. Tant qu'elle n'est
  pas là, n'affiche pas « aucun résultat » (ce serait faux) ; et ignore une
  réponse périmée (voir le `useEffect` d'`EcranVerrous.tsx`).
- **NODE_ENV du poste.** La construction fixe elle-même le mode production
  (`vite.config.ts`). Ne la fais pas dépendre de l'environnement : avec
  `NODE_ENV=development` en variable globale, l'écran restait vide
  (`o.jsxDEV is not a function`).
- **Rechargement de la vue** (F5, reprise à chaud) : la page JS disparaît
  avant que Blazor détruise les composants. `IleReact` ignore alors la
  `JSException` du nettoyage, comme `Home.razor` ; sans ça l'appli se fermait.
- **Voir la console JS** sur le bureau caché : F12 dans la fenêtre RIG
  (outils de développement activés en Debug).

## Ajouts du lot 5 (écrans juridiction et éditions)

- **`src/criteres.ts`** : les gestes des écrans de recherche, à reprendre
  plutôt qu'à réécrire — `dateCourte` (`ToString("dd/MM/yyyy")` d'une date
  tenue en `aaaa-mm-jj`), `contientSansCasse` (`Contains(…, OrdinalIgnoreCase)`),
  `estBlanc` (`IsNullOrWhiteSpace`), `distinctsTries` (`Distinct().Order()`,
  ordre fr-FR) et `entierLisible` (le `@bind` d'un `int`).
- **Les boutons de `.rig-criteres__actions`** : ce bloc n'est PAS en colonne
  souple ; les boutons y sont en ligne et Razor gardait un blanc entre eux.
  Mets `{' '}` entre deux boutons écrits sur deux lignes dans le `.razor`
  (sinon ils se collent). Pas de blanc à recopier autour d'un `@foreach` /
  `@if` : Razor les supprimait déjà.
- **`iles.test.tsx`** lit la liste des écrans montables sur le disque : un
  écran ajouté n'y demande aucune ligne.
- **Captures du bureau entier** (`hdesk-observe.ps1`, 1920×1080) : la fenêtre
  commence à y = 66, sa barre d'onglets finit vers y = 137 — compare avec
  `--haut 140`, pas 80. L'appli met quelques secondes à accepter un geste
  après son lancement : capture d'abord l'accueil, sinon la première capture
  montre l'accueil au lieu de l'écran voulu.

## Commandes

| Commande (dans `web/`) | Effet |
|---|---|
| `npm run build` | construit `wwwroot/app/rig-iles.js` (fait aussi par `dotnet build`) |
| `npm run watch` | reconstruit à chaque enregistrement ; l'appli ouverte se recharge seule (elle surveille `wwwroot`) |
| `npm run typecheck` | `tsc --noEmit`, mode strict (fait aussi par `compiler.ps1`) |
| `npm test` | vitest (fait aussi par `tests.ps1`, banc `WebVitest`) |
| `npm run comparer -- a.png b.png [--haut px]` | compare deux captures pixel par pixel |

Avec `dev.ps1`, un `.tsx` modifié n'est repris qu'au prochain build : lance
`npm run watch` à côté pour le voir tout de suite.
