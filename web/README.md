# web/ — les écrans React de RigV3Desktop

Migration Blazor → React, commencée le 18/11/2026 (plan `plan-4021ed92`, fil chef
`conv-1985`). Le métier reste en C# (`Metier/`, non modifié). Ce dossier contient
les écrans réécrits en React 19 + TypeScript strict, construits par Vite dans
`wwwroot/app/rig-iles.js` (ignoré par git, reconstruit à chaque `dotnet build`).

Ce fichier est le **mode d'emploi d'un portage d'écran** : les lots 2 à 6 le suivent.
**Blazor est retiré depuis le 19/11/2026** (plan `plan-2a906eb0`, lot 3) : les
sections plus bas qui parlent de coquilles `.razor` et d'`IleReact` racontent
l'historique du portage ; l'état actuel est ci-dessous et dans « Ajouts du plan
« finir le passage à React », lot 3 ».

## Comment ça tient ensemble

```
MainWindow / FenetreVue / FenetreDocument (WPF, controle WebView2 direct)
                     │  Pont/PageWeb.cs : https://rigv3.example/ = wwwroot/
                     │  (sous dev.ps1 : la page vient du serveur Vite)
                     ▼
               wwwroot/index.html ──> app/rig-iles.js   (web/src/main.ts)
                     │  demarrer(#app) : appeler('Racine.Ouvrir')   (web/src/racine.tsx)
                     ▼
               Pont/Racines.cs  RacinePlanTravail / RacineDocumentDetache
                     │  { composant, proprietes, rappels, bords }
                     ▼
               web/src/composants/Application.tsx (ou DocumentDetacheVue.tsx)
                     │  -> src/plan/Aiguillage.tsx -> web/src/ecrans/EcranX.tsx
                     │  appeler<T>('EcranX.Methode', …)       (web/src/pont.ts)
                     ▼
               Pont/Repartiteur.cs ──> Pont/EcranX.cs [JSInvokable("EcranX.Methode")] ──> Metier/
```

- **Racine** : chaque fenêtre WPF a sa racine C# (`Pont/Racines.cs`) ; la page la
  demande au chargement (`Racine.Ouvrir`) et monte le composant nommé dans un
  `div[data-ile]` en `display:contents` (aucune boîte). Le C# renvoie la racine de
  lui-même quand elle change (évènement `racine`) : React re-rend sans perdre son état.
- **Pont** : `web/src/pont.ts` est le **seul** fichier de `web/src` qui parle
  au C# (`src/regles.test.ts` le vérifie). Il reste générique : aucun nom d'écran.
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
   pont de `src/test/faux-pont.ts` (ex-`faux-dotnet.ts`). Un banc C# de `tests/` qui relisait
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

## Ajouts du lot 2 (Réquisitions, Commandes web) : l'écran à session

- **Quand** : l'état de l'écran est fait d'objets de `Metier/` que `Metier/`
  modifie lui-même (`TraitementCommandes.Appliquer`, `CommandeRequi.TravailFrais`…)
  et presque chaque valeur affichée sort d'une de ses règles. Recopier cet état
  dans React recopierait les règles. Le `@code` est alors **déplacé tel quel**
  dans `Session<Nom>` de `Pont/Ecran<Nom>.cs` (mêmes membres, mêmes noms) ; le
  `.tsx` ne tient que la vue rendue et remonte chaque geste (`src/session.tsx` :
  `useSession`, `Saisie`).
- **Contrat** : `Ecran<Nom>.Ouvrir()` → vue ; `Ecran<Nom>.Agir(session, geste,
  valeur)` → vue après le geste ; `Ecran<Nom>.Fermer(session)`. Un geste inconnu
  lève une exception (jamais ignoré). Sessions rangées par `Pont/SessionsEcran.cs`.
- **Bancs** : un banc qui relisait le `.razor` relit les règles dans le pont et
  le balisage dans le `.tsx`, puis **joue les gestes** en appelant
  `Ecran<Nom>.Agir` comme React (`tests/RequiCheck` section 9,
  `tests/CommandesWebCheck` section 5 : chaque vue comparée à ce que `Metier/`
  calcule sur un jeu témoin). Le `.csproj` du banc passe alors en
  `net10.0-windows` + `UseWPF` + `ProjectReference` vers `RigV3Desktop.csproj`,
  et le code ajoute `using System.IO;` (les usings implicites de WPF l'omettent).
  `tests/TriCheck` trouve seul la classe `Session<Nom>` d'un écran à session.
- **Blancs autour d'un `@foreach`** (précision à la note du lot 5 ci-dessous) :
  Razor ne retire que les blancs **seuls**. Un texte qui *finit* par un blanc
  juste avant le `@foreach` (« — avec ») ou qui *commence* par un blanc juste
  après (« · 2 à traiter ») le garde : écris `{' '}` des deux côtés du `.map`
  (Commandes web, ligne « Enveloppe », vu à la comparaison des captures :
  998 pixels décalés, puis 0 après correction).
- **Pièges** : une regex de banc qui contenait un vrai saut de ligne doit
  écrire `\r?\n` (un `.cs` neuf peut être en LF, l'ancien `.razor` était en
  CRLF) ; un contrôle « le `.tsx` ne recopie aucune règle » se fait sur son code
  **sans les commentaires** (l'en-tête cite le fichier de règles). Avant d'exiger
  qu'un geste « change l'ordre », vérifie que le jeu d'exemple le permet : sur
  l'onglet « À traiter », les rapprochements directs sont déjà en tête.
- **Copie « avant » exportée** (`git archive <commit>` dans `%TEMP%`) : ne relie
  **jamais** son `web/node_modules` à celui de ta copie par une jonction. Sa
  première compilation lance `npm ci` (cible `InstallerWeb`), qui vide
  `node_modules` à travers le lien, donc le tien aussi (vu au lot 2, réparé par
  le `npm ci` suivant). Laisse la compilation l'installer.
- **État de l'appli lancée pour une capture** : pose `RIGV3_ETAT=<fichier jetable>`
  avant `hdesk-lancer.ps1` (le lanceur transmet son environnement), sinon l'appli
  écrit dans `%APPDATA%\RigV3\accueil.json`, les réglages de l'utilisateur.
  Partir du même fichier pour « avant » et « après » rend aussi les captures comparables.

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

## Ajouts du lot 3 (recherche d'affaires, dossiers, courrier) : les morceaux de texte

- **Un morceau par `@expression`.** Razor crée un nœud de texte pour chaque
  `@expression` et un pour chaque texte écrit entre elles :
  `@n dossier(s)` donne « 3 » puis « dossier(s) ». Chromium mesure chaque nœud
  à part et arrondit sa largeur : une seule chaîne assemblée (`` {`${n} dossier(s)`} ``,
  ou une concaténation) se dessine décalée d'une fraction de pixel, et tout ce
  qui suit glisse avec. Vu au lot 3 : « 0 / 4 » de Tâches en lot élargissait une
  colonne du tableau (134 pixels différents), « 10 affiche(s) · 4 sans image »
  d'Actes (25 px), « role 2026F00087 » de la recherche (20 px) ; tous à 0 après
  correction. **Règle : chaque `@x` du `.razor` = un `{x}` du `.tsx`**, le texte
  entre deux en morceau séparé : `{n}{' affiche(s) · '}{m}{' sans image'}`
  (`{n} dossier(s)` écrit sur une ligne convient aussi). Jamais de gabarit
  `` `${…}` `` pour un texte affiché. Seul le découpage compte : un blanc qui se
  replie (`"\n      "` contre `" "`) se dessine pareil.
- **Le blanc garde sa place.** « pour le SIREN » : Razor retirait le blanc entre
  le bouton et le `@if`, mais gardait celui du DÉBUT du `span`
  (`<span>\n  pour le SIREN @x`). Ce blanc se dessine dans la police du `span`
  (6,7 px en chasse fixe), pas dans celle du parent (3,3 px) : écris
  `<span>{' pour le SIREN '}{x}</span>`, pas `{' '}` avant le `span` (bouton
  décalé de 2 px sinon). De même « N dossier(s) » suivi d'un `@if` : le blanc
  reste à la fin du morceau (`{' dossier(s) '}`), la puce suivante n'en a pas.
- **Le test** : `morceaux(el)` (`src/test/morceaux.ts`) rend les nœuds enfants
  d'un élément ; les tests d'écran du lot 3 vérifient le découpage exact
  (`expect(morceaux(compte)).toEqual(['2', ' affichee(s) sur ', '12'])`), rouge
  sur une chaîne assemblée.
- **Trouver le morceau qui diffère** quand une capture n'est pas à 0 : relève,
  avant et après, chaque nœud de texte rendu avec ses boîtes
  (`Range.getClientRects()`, au 1/100 px) et compare les deux listes ; le premier
  écart nomme le morceau fautif. Le port `--remote-debugging-port` passé par
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` n'est pas ouvert par ce WebView2
  (157, constaté le 19/11) : un script ajouté à l'`index.html` d'une copie
  jetable, qui envoie l'arbre à un petit serveur local, fait l'affaire — à
  retirer avant tout commit.

## Ajouts du lot 6 (composants de l'accueil : arbre, fiche, Kbis, icônes, fenêtre détachée)

- **Composants, pas écrans** : `src/composants/<Nom>.tsx`, montés par leur
  coquille `Components/<Nom>.razor` (mêmes `[Parameter]`). Home.razor et
  `FenetreDocument` (racine `DocumentDetacheVue`) n'ont pas changé.
- **Paramètres vivants** (HashSet des nœuds ouverts, `AccueilEtat`, la fonction
  `Lire` du Kbis, l'emplacement de `DocumentPartage`) : ils ne traversent pas.
  La coquille appelle elle-même `Pont/<Nom>.Vue(...)` (C# simple, sans
  `[JSInvokable]`) et passe la vue calculée en `Proprietes` ; `IleReact` ne
  renvoie que si le JSON change. C'est le cas d'`ArbreGedNoeud`, `ApercuKbis`,
  `DocumentDetacheVue` (abonnée à `Emplacement.Change`).
- **Composant récursif** (`ArbreGedNoeud`) : UNE île à la racine, la récursion
  est dans le `.tsx`. Chaque ligne porte un identifiant d'IDENTITÉ
  (`ConditionalWeakTable`, car `NoeudGed` est un record à égalité de valeur) ;
  la coquille retrouve le nœud d'un geste par l'index rempli au dernier rendu,
  puis lève l'`EventCallback` d'origine (tuple `(NoeudGed, MouseEventArgs)` refait
  côté C#).
- **Session ouverte par la coquille** (`FicheInformationsDossier`, comme
  `EcranTachesLot`) : `Parametrer` rend vrai seulement si ce que la vue lit HORS
  session a changé (paramètres par identité + trace du dossier) — la coquille
  incrémente alors `version`. Sans ça, chaque rendu de l'accueil (minuteries
  comprises) ferait relire la vue. Les `EventCallback` levés par un geste
  (`ChampModifie`, `ToutAffiche`) reviennent dans `vue.rappels` ; React les
  remonte à la coquille, même si la vue de ce geste est dépassée.
- **`@onchange`** d'un champ : `Saisie` avec `quand="change"` (`src/session.tsx`).
- **Même `<input>` réutilisé par Razor** : en saisie guidée, le texte tapé
  restait affiché dans le champ suivant sans y être écrit (capture « avant » du
  19/11). Le `.tsx` recrée le champ par `key={cle}` : c'est le seul écart voulu
  avec la capture d'avant.
- **Icônes SVG** : les mêmes chaînes que la `MarkupString` du `.razor`, posées par
  `dangerouslySetInnerHTML` (constantes du fichier, jamais une saisie).
- **Table sans `<tbody>`** dans le `.razor` : le `.tsx` l'écrit (le navigateur
  crée de toute façon ce groupe de lignes ; React refuse `<tr>` sous `<table>`).
- **Parcours de captures** rejoué à l'identique avant/après : 24 états (fiche,
  menu, historique, verrou, saisie guidée, arbre replié/filtré, renommage,
  menu « ⋮ », fenêtre détachée et zoom). Deux passages : 20 états à 0 pixel à
  chaque fois ; le reste = l'écart voulu ci-dessus, 3 pixels de teinte ≤ 2/255 au
  bord de l'arbre, et selon le passage un curseur de saisie qui clignote ou
  l'anneau animé d'une tuile de l'accueil (teinte 1/255) — hors de ce lot.

## Ajouts du plan « finir le passage à React », lot 1 : l'accueil

- **La coupe avec le plan de travail (lot 2).** L'accueil = `header.rig-barre` et
  `div.rig-accueil` entiers (menu, entête, recherche, chiffres du jour, « Proue »,
  grille et corps des tuiles), rendus par trois îles : `ThemeRig` (pose
  `data-rig-theme` sur `<html>`, montée dans TOUTES les fenêtres, ne dessine rien),
  `BarreRig` et `Accueil` (`src/composants/`). Leurs morceaux sont dans
  `src/accueil/`. Le plan de travail (onglets, `rig-proc`, `rig-vged`, modales,
  Échap, pagination des étapes) et le ROUTAGE (`OuvrirCode`, `OuvrirAlerte`,
  `OuvrirParNumero`, `OuvrirDossier`, fonds d'affaires, `Groupes`, `Sujets`)
  restent dans `Home.razor` pour le lot 2.
- **Session tenue par Home, pas par React.** L'état de l'accueil est PARTAGÉ avec
  le plan de travail (`AccueilEtat`, login, thème) : `Home.razor` garde un champ
  `Accueil` (`Pont.SessionAccueil`, le `@code` déplacé tel quel) et s'en sert
  (`Etat => Accueil.Etat`, `Domaines => Accueil.Domaines`…). À chaque rendu, Home
  calcule la vue UNE fois (`Accueil.Vue(ContexteDeLAccueil())`) et la passe en
  `Proprietes` : les minuteurs (performance chaque seconde, actualités chaque
  minute) arrivent ainsi sans code de plus ; `IleReact` ne renvoie que si le JSON
  change. Le contexte porte ce que seul le plan de travail sait (accueil
  recouvert, écran ouvert, résultats de recherche calculés sur son fonds).
- **Un seul rappel, `geste`** : `{ nom, valeur }` → `SessionAccueil.Agir`, qui rend
  une `DemandeAccueil` pour ce qui sort de l'accueil (ouvrir un écran, une
  alerte, un numéro, un résultat de recherche par son RANG ; login ou thème
  changé ; déplacer, réduire, agrandir, fermer la fenêtre). Un geste inconnu lève
  une exception.
- **Catalogue du menu** : `Pont/MenuAccueil.cs` (même indentation, lue par
  `tests/MenuGestesCheck`) ; `Home.razor` le relit par
  `Moments = Pont.MenuAccueil.Moments` (aiguillages, `tests/MotifCheck`).
- **Glisser-déposer** (ex-`activerPose` de `rig-accueil.js`) : `src/accueil/pose.ts`
  (géométrie, mêmes bornes que `PoserTuile` / `DimensionnerTuile`) et
  `Grille.tsx`. Pendant le geste la tuile suit le pointeur par son `style` React
  (z-index 60, `rig-widget--saisi`, `rig-grille--pose`) ; au lâcher, la pose part
  au C# et la tuile GARDE la pose lâchée jusqu'à ce que la vue la porte (pas de
  retour en arrière d'un aller-retour). `rig-accueil.js` ne garde que la
  pagination, le clavier et la barre d'onglets.
- **Listes déroulantes à valeur C#** : `src/accueil/Liste.tsx`, le pendant de
  `Saisie` — un `<select value>` contrôlé reviendrait sur l'ancien thème entre
  le choix et la réponse.
- **Les blancs, lus dans le code que Razor génère.** Plutôt que de deviner ce que
  Razor garde : `dotnet msbuild -t:Compile -p:EmitCompilerGeneratedFiles=true`
  (créer d'abord les dossiers `obj/Debug/net10.0-windows/generated/…/Components/Pages`,
  sinon CS0016) écrit `Home_razor.g.cs`, où chaque `AddContent` est un nœud de
  texte et chaque `AddMarkupContent("\r\n   ")` un blanc. Exemple : le blanc entre
  `</b>` et `@(…)` est RETIRÉ, celui entre `</b>` et `<span>` sur une même ligne
  est GARDÉ.
- **Captures** : parcours rejoué sur l'exe d'avant et d'après (clair : accueil,
  réglages de la barre, panneau des tuiles, profil, chiffres 1 et 3 dépliés, menu
  réduit, disposition « Par métier », widget déployé ; sombre : 5 états). Tous à
  0 pixel, sauf trois écarts qui changent aussi entre deux captures du MÊME exe :
  l'anneau « Hors délai légal » (animation infinie `rig-anneau-pouls`), une bande
  de 5 px au bord du panneau de profil, et un survol en transition ; le widget
  déployé diffère de 1/255 de teinte au plus, dans l'ombre derrière lui.
  Un glisser se joue sur le bureau caché par `D:\AutoWinOS\scripts\hdesk-glisser.ps1`.

## Ajouts du plan « finir le passage à React », lot 2 : le plan de travail

- **Home.razor n'est plus qu'une coquille.** Il monte UNE île, `Application`
  (`src/composants/Application.tsx`), avec `Plan.Vue()` en `Proprietes` et deux
  rappels : `geste` (l'accueil, `SessionAccueil.Agir`, inchangé) et `plan`
  (`SessionPlanTravail.Jouer`). Les noms des gestes de l'accueil et du plan se
  recouvrent (`favori`, `fermer`…) : deux rappels, jamais un seul.
- **Le `@code` de Home.razor est dans `Pont/PlanTravail.cs`** (`SessionPlanTravail`,
  déplacé TEL QUEL : mêmes membres, mêmes noms ; seules les attaches à Blazor ont
  changé). `Pont/PlanTravailVue.cs` porte les records de la vue (un par bloc du
  balisage, mêmes noms que `src/plan/types.ts`), `Vue()` et `Agir(geste, valeur)` :
  chaque `@onclick`/`@oninput`/`@onpointer…` du `.razor` est un geste nommé qui
  appelle la MÊME méthode. Un geste inconnu lève une exception.
- **Le balisage** est dans `src/plan/` : `Onglets.tsx`, `Procedure.tsx`,
  `Aiguillage.tsx` (le `@switch` : le SEUL fichier qui aiguille par code,
  `tests/NomMetierCheck`), `Visionneuse.tsx`, `Tache.tsx`, `Etapes.tsx`,
  `Apercu.tsx`, `Modales.tsx`. Les 37 écrans de `src/ecrans/` y sont montés
  DIRECTEMENT, avec les paramètres de leur coquille (les coquilles `.razor` ne
  sont plus montées par personne : le lot 3 les retire avec Blazor).
- **Écrans à session ouverts par le C#** : la fiche d'informations (trois branches
  = trois sessions, comme les trois composants du `.razor`) et les tâches en lot
  reçoivent l'état VIVANT de l'accueil ; `SessionPlanTravail` les ouvre, les
  `Parametrer` à chaque `Vue()` (version +1 si la vue a pu changer) et les ferme
  quand ils quittent la page. L'arbre (`ArbreGedNoeud.Vue`, index des nœuds du
  dernier calcul) et le Kbis (`ApercuKbis.Vue(…, LireSaisie)`) sont calculés dans
  `Vue()`.
- **Une vue périmée n'est jamais montrée.** Chaque geste du plan part numéroté
  (`GestePlan.Numero`), la vue dit le dernier numéro joué (`VueApplication.Geste`) :
  `Application` garde la vue montrée tant que la réponse au DERNIER geste n'est
  pas là. Sans ça, deux frappes rapides dans un champ faisaient revenir le texte
  intermédiaire (même parti que `useSession`).
- **`rig-accueil.js` est retiré** : clavier → `src/plan/clavier.ts`, glisser
  d'onglet (capture du pointeur, retour visuel) → `src/plan/glisse.ts`, pagination
  des étapes → `src/plan/pagination.ts`. Ce qui se faisait APRÈS le rendu en
  Blazor (amener la frise sur l'étape d'une pièce) part dans la vue
  (`Defilement { numero, index, doux }`) : la page le joue UNE fois, après avoir
  rendu la zone ; le numéro joué est tenu par l'application (il survit à la
  fermeture des étapes). `Pagination` avance quand une affaire s'ouvre (le
  `PaginationSuivie = false` du `.razor`).
- **Changements venus d'ailleurs** (minuteurs de l'accueil, fenêtre WPF refermée,
  étoile allumée dans une autre fenêtre) : `SessionPlanTravail.Planifier`, que la
  coquille branche sur `InvokeAsync(…; StateHasChanged)`, et l'événement `Change`.
- **Saisie à deux évènements** : `Saisie` (`src/session.tsx`) accepte `finir` en
  plus de `changer` (le `@oninput` + `@onchange` de la saisie guidée).
- **Écart de comportement, voulu** : ouvrir une pièce quand les étapes
  APPARAISSENT amène la frise sur l'étape de la pièce (« Statuts signés » →
  Capital), comme le code le demande. L'exe Blazor y arrivait une fois sur deux :
  `suivrePagination` mesurait l'étape visible (0) pendant que le défilement
  attendait, et la frise restait sur l'étape 1.
- **Bancs** : chaque banc qui relisait Home.razor relit la même règle dans
  `Pont/PlanTravail*.cs` ou `src/plan/*.tsx` ; `tests/PlanTravailCheck` (neuf)
  JOUE le parcours par `SessionPlanTravail.Agir` (dossier, étapes, saisie refusée
  puis acceptée, validation, onglets, Échap, tâche, filtre, impression, mail,
  validation hiérarchique) et tient que Home.razor n'est qu'une coquille.
- **Parcours avant/après** (bureau caché, 30 états, `%TEMP%\lot2-outils\parcours.sh`) :
  tous à 0 pixel sauf l'anneau animé « Hors délai légal », 2 pixels de teinte
  ≤ 7/255 au bord de l'arbre (déjà vus au lot 6) et l'étape ci-dessus. La fenêtre
  d'un onglet arraché naît sous le curseur RÉEL : le parcours la pose à une place
  fixe avant la capture, et la ferme par `WM_CLOSE` (sa croix), un clic tombant
  sur la fenêtre principale passée devant.

## Ajouts du plan « finir le passage à React », lot 3 : Blazor retiré

- **Plus de Blazor.** Plus aucun `.razor` (`Main.razor`, `_Imports.razor`,
  `Components/` : les coquilles et `IleReact` sont supprimées) ; le projet est en
  `Microsoft.NET.Sdk` et le paquet `Microsoft.AspNetCore.Components.WebView.Wpf`
  (figé sur 9.0.120) est remplacé par `Microsoft.Web.WebView2` 1.0.3179.45, la
  version que ce paquet tirait déjà. Contrôle **classique** `WebView2`, pas
  `WebView2CompositionControl` (la cause de la page blanche du BlazorWebView 10).
- **Les fenêtres** (`MainWindow`, `FenetreVue`, `FenetreDocument`) portent un
  `<wv2:WebView2 x:Name="Vue" />` et appellent `Pont.PageWeb.Brancher(Vue, Racine)`
  au chargement : un environnement WebView2 partagé (même dossier de données que
  BlazorWebView, `%LOCALAPPDATA%\RigV3Desktop.WebView2`), thème sombre des listes
  déroulantes, `wwwroot` sous `https://rigv3.example/`, aucune autre origine
  chargée ni écoutée. Leur racine (`Racine`) est fermée avec elles.
- **Racines** (`Pont/Racines.cs`) : `RacinePlanTravail` (ex-`Main.razor` + `Home.razor` :
  session du plan de travail, rappels `geste` et `plan`, bandes de bord dans la
  fenêtre principale non agrandie) et `RacineDocumentDetache` (ex-`DocumentDetacheVue.razor`).
  Même cycle de vie : ouverte à la première demande de la page (un rechargement
  la retrouve telle quelle), démarrée après le premier rendu, fermée avec la fenêtre.
- **Répartiteur** (`Pont/Repartiteur.cs`) : reçoit `{ appel, nom, args }`, joint la
  méthode statique `[JSInvokable("<NomEcran>.<Methode>")]` trouvée par réflexion
  (attribut du projet, même nom que celui de Blazor : les ponts n'ont pas changé),
  convertit les arguments en JSON (mêmes règles que Blazor, compte exact), répond
  `{ reponse, valeur | erreur }`. Après un rappel et à chaque changement venu
  d'ailleurs, il renvoie la racine **si son JSON a changé**.
- **Côté React** : `src/main.ts` (entrée, `wwwroot/app/rig-iles.js`),
  `src/racine.tsx` (`demarrer` : monte la racine, la re-rend à chaque envoi, écrit
  toute erreur dans la page au lieu de la laisser blanche), `src/fenetre/Bords.tsx`
  (les 8 bandes de `Main.razor`, `onMouseDown` → rappel `bord` →
  `FenetreInterop.Redimensionner`), `src/pont.ts` (`appeler`, `ecouter`, `rappel`).
  Tests : `src/test/faux-pont.ts` joue le vrai transport (aller-retour JSON,
  numéros d'appel) ; `racine.test.tsx`, `pont.test.ts`, `iles.test.tsx`.
- **Développement** : `dev.ps1` lance `npm run dev` (Vite) sur un port libre, attend
  sa réponse, pose `RIGV3_VITE` et lance `dotnet watch` : la fenêtre (Debug,
  localhost seulement) charge la page depuis Vite, qui reprend un `.tsx` à chaud.
  `vite.config.ts` sert alors **le même** `wwwroot/index.html`, module construit
  remplacé par `src/main.ts`, et `wwwroot` en dossier public. Fermer l'appli arrête
  `dotnet watch` et Vite.
- **Construction** : `ConstruireWeb` / `CopierWeb` sont accrochées à la copie vers
  `bin\` (plus aux fichiers statiques de Razor) ; `wwwroot` est copié dans la
  sortie et servi de là.
- **Preuves** (bureau caché, `%TEMP%\lot3-blazor-outils\`) : le parcours du lot 2
  (30 états, onglet arraché compris) et un parcours de redimensionnement des trois
  fenêtres, rejoués sur l'exe Blazor (main `099d280`) et sur celui-ci : identiques
  au pixel, sauf l'anneau animé « Hors délai légal » et un survol en transition —
  écarts qu'on retrouve, aux mêmes endroits, entre deux lancements du même exe
  Blazor. Le glisser d'une bande de bord n'est pas pilotable sur un bureau caché
  (boucle native de Windows) : même résultat avant/après ; la taille est donc
  changée par `SetWindowPos` pour la preuve.

## Commandes

| Commande (dans `web/`) | Effet |
|---|---|
| `npm run dev -- --port <n>` | serveur Vite à chaud (lancé par `dev.ps1`, qui choisit le port et le passe à la fenêtre) |
| `npm run build` | construit `wwwroot/app/rig-iles.js` (fait aussi par `dotnet build`) |
| `npm run watch` | reconstruit à chaque enregistrement ; une appli lancée HORS `dev.ps1` se recharge seule (elle surveille `wwwroot`) |
| `npm run typecheck` | `tsc --noEmit`, mode strict (fait aussi par `compiler.ps1`) |
| `npm test` | vitest (fait aussi par `tests.ps1`, banc `WebVitest`) |
| `npm run comparer -- a.png b.png [--haut px]` | compare deux captures pixel par pixel |

Avec `dev.ps1`, un `.tsx` modifié est repris **à chaud** par le serveur Vite dans
la fenêtre ouverte : rien à lancer à côté.
