# Former les agents des greffes à la V3 (SOC-026)

Lot 13 du plan « Reconstruction de RIG — phase 2 », 2026-11-21. Ce dossier porte le **plan de formation** et les
**supports tirés des écrans de la V3**. Tout ce qui est marqué « proposé » est **à valider** par le directeur de projet
d'Amitel et les greffes bêta (la story demande la validation des greffes bêta).

| Fichier | Ce qu'il donne | Comment il est fait |
|---|---|---|
| `README.md` (ce fichier) | le plan : qui, quand, sur quoi, l'assistance du jour J | écrit à la main |
| `ecrans-par-metier.md` | pour chaque métier (RCS, judiciaire, endettement) et pour tous : chaque entrée du menu de la V3, ce qu'on y trouve, la procédure de la V2 qu'elle remplace, et si l'écran est déjà construit | **généré** des écrans de la V3 (`Pont\MenuAccueil.cs`, `Metier\PerimetreProfil.cs`, `Pont\CatalogueEcrans.cs`) |
| `libelles-qui-changent.md` | les écrans dont le nom change de la V2 à la V3 | **généré** (`Pont\InventaireTouchesV2.cs` contre le nom de l'écran dans la V3) |
| écran « Ancienne touche, nouvelle touche » de la V3 (menu Administrer, SOC-027) | les raccourcis clavier de la V2 et leur équivalent | écran de la V3 (`Pont\EcranCorrespondances.cs`) |

Les deux fichiers générés se recalculent à chaque passage du banc `tests\SoclePassageCheck` : une ligne qui décrit un
écran ou un libellé qui n'existe plus le fait passer au rouge ; un écran ajouté depuis leur génération est signalé
(« RESTE ») et se reprend par `RIGV3_ECRIRE_FORMATION=1 .\tests.ps1 SoclePassage`. Le banc vérifie aussi qu'aucun écran
renommé ne perd un mot imposé par les textes (CA05).

## Qui former : un comptage, pas une estimation (CA01)

```powershell
# Une base V2 : comptes ACTIFS, par service et par métier, sans aucun nom ni login (chez Amitel : --exploitation)
dotnet run --project Outils\Passage\RigV3.Passage.csproj -- formation --v2 "Server=…;Database=RIG_<GREFFE>;…" --greffe <code> --plan formation.json
```

`RigV3.Passage formation` lit `UTILISATEUR` et `SERVICE` de la base du greffe en lecture seule et ne sort que des
nombres : agents à former par métier, puis par service ; les comptes d'Amitel et les comptes désactivés sont exclus et
comptés à part. Le métier d'un service vient de son abrégé : `RCS` → RCS, `JUD` → judiciaire, `INS` → endettement,
`CDG` (cabinet des greffiers) → tous métiers ; un abrégé inconnu reste « à classer » et le greffe dit de quel métier il
relève. `--plan` écrit le squelette de `formation.json` (à former par métier, formés à 0) que l'équipe de formation
tient ensuite.

Mesure sur RIG_DEV (base de développement, 2026-11-21) : 46 agents à former (34 RCS, 10 judiciaire, 2 au cabinet des
greffiers) ; 50 comptes Amitel actifs et 147 comptes désactivés exclus. **La comptabilité n'est pas un service dans
RIG_DEV** : ses agents sont comptés dans leur service (écart avec la story, qui cite « RCS, judiciaire,
comptabilité » : à valider avec les greffes). Les vrais nombres viennent de la commande jouée sur les 40 bases, chez
Amitel.

## Quand : deux vagues (CA03)

| Vague | Qui | Avant quoi | Contrôlé par |
|---|---|---|---|
| 1 | les 3 greffes **bêta** : Le Puy, Chartres, Vienne | le **début de la bêta** | `RigV3.Passage beta demarrer` refuse tant qu'un métier d'un greffe bêta a moins de formés que d'agents à former (`formation.json`) |
| 2 | les **37 autres** greffes, outre-mer compris | le **jour J** | `formation.json` tenu à jour ; revue avant de fixer le jour J |

Chaque agent suit la formation **de son métier** avant d'ouvrir la V3 en production. Le dépôt ne tient que des
nombres (à former, formés) : la liste nominative des présences est une donnée personnelle, elle reste chez l'équipe de
formation.

## Sur quoi : une V3 d'essai par greffe, sur sa copie anonymisée (CA02)

- **Environnement** : pour chaque greffe, une V3 d'essai alimentée par la **copie anonymisée** de sa base
  (`Outils\Anonymisation`, SOC-021), passée à la V3 par le même outil que le jour J (`RigV3.Passage passer`, sur un
  catalogue et une instance **de test**). Jamais la production : une répétition ou un environnement d'essai qui vise une
  instance de production est refusé avant tout passage (`SOC-025 CA06`).
- **Réserve** : aucune copie anonymisée ne sert tant que le lot 6 (copie anonymisée) n'est pas reçu à nouveau (décision
  du chef du plan : les copies faites avant gardent des prénoms en clair).

## Le déroulé proposé (à valider)

| Module | Pour qui | Durée proposée | Support |
|---|---|---|---|
| Tronc commun : ouvrir la V3, l'accueil, le menu par geste et par verbe, les onglets, la recherche (Entrée, F5, F7), l'ancien RIG ouvert depuis la V3 pour une affaire d'avant le passage | tous | 1/2 journée | `ecrans-par-metier.md` (« Tous les métiers »), fiche des touches |
| Gestes du métier | RCS / judiciaire / endettement | 1/2 journée par métier | `ecrans-par-metier.md` (section du métier) |
| Ce qui change de nom | tous | inclus | `libelles-qui-changent.md` |
| Exercices sur la V3 d'essai du greffe | chaque agent | 1 h | la copie anonymisée de son greffe |

Le vocabulaire imposé par les textes — immatriculation, radiation, Kbis, dépôt des comptes, mentions — est **le même**
que dans la V2 (CA05) ; seuls changent les noms propres à la V2, listés dans `libelles-qui-changent.md`.

## L'assistance du jour J (CA04, proposé)

- **Dimensionnée pour tous à la fois** : les 37 greffes passent le même jour ; l'effectif se calcule sur le comptage
  (`formation.json`), proposé à **une personne d'assistance pour 25 agents** ouvrant la V3 ce matin-là, à valider.
- **Horaires élargis pour l'outre-mer** : chaque greffe passe à l'ouverture de **sa** journée (07:00 heure locale,
  `RigV3.Passage jour-j`). Heure de Paris en hiver : La Réunion ouvre à 04:00, Mayotte à 05:00, la métropole à 07:00,
  les Antilles à 12:00, Papeete à 18:00 ; Papeete ferme à 20:00 heure locale, soit 07:00 le lendemain à Paris.
  L'assistance tient donc de 04:00 (J) à 07:00 (J+1), heure de Paris, avec le gros de l'effectif de 07:00 à 12:00.
- Le tableau de bord du jour J (`RigV3.Passage tableau`) dit à l'assistance où en est chaque greffe.

## Ce qui reste

| Reste | Pourquoi | Qui |
|---|---|---|
| Aide intégrée à la première ouverture, avec l'accès à la fiche des touches (CA06) | écran « à maquetter », hors des fichiers de ce lot | lot de l'accueil |
| Comptage des 40 bases et `formation.json` réel | les bases des greffes ne se lisent pas depuis ce poste | exploitation, Amitel |
| Durées, ratio d'assistance, place de la comptabilité | décisions du directeur de projet et des greffes bêta | directeur de projet |
