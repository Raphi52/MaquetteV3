# Socle — les courriels du greffe, leurs modèles et leur trace (SOC-006)

Lot 1 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`, **SOC-006** : « Envoyer
les courriels du greffe avec ses modèles et en garder la trace ».

| Couche | Ce qui est fait | Où |
|---|---|---|
| Base (T01) | modèles (`soc.ModeleMessage`), listes de diffusion (`soc.ListeDiffusion`, `soc.ModeleMessageListe`), courriels partis (`soc.CourrielEnvoye`), trace de chaque étape (`soc.TraceEnvoi`) | `Base\migrations\SOC-006_courriels.sql` |
| Serveur (T02) | le service d'envoi : modèle du greffe, adresses, jetons, habillage en base puis fichier, remise ou boîte de dépôt, trace à chaque étape | `Serveur\Socle\Courriels\ServiceCourriels.cs`, `HabillageCourriel.cs`, `ValeursGreffe.cs`, `RemiseCourriel.cs`, `OptionsCourriels.cs`, `TracesCourriels.cs` |
| Exploitation (T03) | le ménage quotidien des traces de plus de 90 jours, durée réglable | `Serveur\Socle\Courriels\MenageCourriels.cs` |
| Écran (T04) | « Messagerie » (gestion des modèles : modèles et listes du greffe, aperçu ; ni modèle ni bascule « Amitel » pour un agent du greffe, CA05 ; **création, modification, désactivation et réactivation** d'un modèle et d'une liste pour un agent qui a la fonction `GESTEMAIL`, lot 15) et « Courriels envoyés » (menu Configurer › Modèles et envois) | `Pont\EcranMessagerie.cs`, `web\src\ecrans\EcranMessagerie.tsx`, `Pont\EcranCourriels.cs`, `web\src\ecrans\EcranCourriels.tsx`, `ModuleCourriels.cs`, `GestionCourriels.cs`, `Base\migrations\SOC-006_gestion.sql` |
| Reprise (T05) | modèles et listes de la V2, comparaison colonne par colonne | `Serveur\Socle\Courriels\RepriseCourriels.cs` |
| Tests (T06) | un contrôle par critère, contre-épreuves faites | `tests\SocleCourrielsCheck` — **`.\tests.ps1 SocleCourriels`** |

**Faux ami** (comme dans la V2) : le **modèle** d'un courriel (`soc.ModeleMessage`, `EMAIL_MODEL` : objet, texte,
expéditeur…) n'est pas son **habillage** (`soc.ModeleCourriel`, `MAIL_TEMPLATE`, repris par SOC-003 : le cadre
graphique autour du texte). Le nom `soc.ModeleCourriel` date de la phase 1 et sa migration est jouée : il reste.

## Les sources V2 relues avant d'écrire

Toutes les références `code_v2` de la story ont été relues à la ligne citée : `EMAIL_MODEL.sql:1-19`,
`MAIL_TEMPLATE.sql:1-9`, `MailTemplateStore.cs:22-28`, `EMAIL_ENVOI_TRACE.sql:3-30`, `EmailEnvoiTrace.cs:14` et
`:171-176`, `envoi-mail-rig-send-email.md:92-97`, `envoi-mail-template.md:34-37`. Elles disent ce que le carnet leur
prête. Lues en plus, parce que le comportement en dépend :

| Source | Ce qu'elle a appris |
|---|---|
| `EmailEnvoye.cs:123-190` (`Send`) | les étapes `EMAIL_SEND_DEBUT` / `_SUCCES` / `_ERREUR`, et ce que chacune garde |
| `EmailEnvoye.cs:346-457` (`_EnvoiLienHttp`, `SetLogoInMail`) | le rendu sans habillage : « \| » en saut de ligne, `$PieceJointe$`, `$LOGO$` (cid `logo`, 400 × 100), `SMTP_APPEL` / `SMTP_IGNORE_DEV` |
| `EmailEnvoye.cs:466-570` + `GreffeMailBuilder.cs` | le rendu avec habillage : `{{MESSAGE_PERSONNALISE}}`, `{{VILLE_TRIBUNAL}}`, `{{NOM_GREFFIER}}`, `{{LISTE_DOCUMENTS_HTML}}`, jeton inconnu = vide, logo `cid:LogoGreffe` ; habillage illisible = rendu historique |
| `SendEmail.cs:520-560`, `:614-646` | destinataire de l'acte + destinataire par défaut du modèle ; expéditeur, réponse, copie du modèle ; les adresses en **étiquettes** `[ADRESSE_MAIL_JUDICIAIRE]` prennent la colonne d'`INFO_GREFFE` |
| `SendEmail.cs:845-860` | le courriel s'enregistre (`EMAIL_ENVOYE`) **après** l'envoi, puis la trace le cite |
| `RigGreffe.cs:299-302`, `:1466-1530`, `:1661-1677` | ville du tribunal = `PARAM_EDITIONS.PRMED_VILLE_MAJ` ; logo = `GRF_PATH_EDITION_LOT1\<code>\Images\logo.*` |
| `OPE_LISTE_MAILS_LOT1.cs` (`PROC_MAIL_LOT1`) | l'écran V2 de consultation : période (aujourd'hui à l'ouverture), destinataire, modèle, 500 lignes au plus, texte à la demande, statut toujours « Envoyé » |
| `TreeviewEditions.cs:701-704` | la liste des modèles proposée à l'envoi masque les modèles réservés à Amitel pour un agent du greffe (CA05) |
| `RIG_DEV` (lu le 2026-11-21, lecture seule) | 40 modèles dont 17 réservés à Amitel, 9 en envoi automatique, 37 en HTML ; 36 utilisent des étiquettes d'adresse ; 16 `$LOGO$`, 16 `$PieceJointe$` ; 0 liste de diffusion ; `EMAIL_MODEL` a 2 colonnes de plus que le projet versionné (`EMAILMOD_SIGNATURE_ACTIVE`, `EMAILMOD_SIGNATURE`) ; fonction `MAIL_LOT1` « Mails envoyés en Lot 1 » ; 88 792 traces, la plus ancienne du 01/07/2026 |

## Comment un courriel part

```csharp
// Dans un acte de la V3 (refus, certificat…) : le service, pas une route.
var resultat = await serviceCourriels.EnvoyerAsync("3801", new DemandeCourriel(
    TypeModele: "CA", Destinataires: "maitre@etude.example", ComplementObjet: " [ref : D-2026-0042]",
    Documents: [new DocumentCourriel(@"\\ged\…\certificat.pdf", "https://…/lien")],
    Mode: "MANUEL", Origine: "certificat d'audience", Agent: @"AMITEL\agent"));
// resultat.Suivi : le numéro de suivi (la trace) ; resultat.Etat : envoye | ignore | echec ; resultat.Erreur
```

1. Le **modèle du greffe** (`soc.ModeleMessage`) donne l'objet, le texte, l'expéditeur, la réponse, la copie
   (cachée, comme la V2) et le destinataire par défaut, ajouté au destinataire de l'acte.
2. Les **étiquettes** des adresses (`[ADRESSE_MAIL_…]`) prennent leur valeur du greffe. Modèle inconnu, ou expéditeur /
   destinataire vide une fois les étiquettes remplacées : **rien n'est remis**, la trace dit pourquoi (`PREPARATION_ERREUR`).
3. Le texte est **rendu comme la V2** : avec l'habillage du greffe (en base, sinon le fichier du réglage
   `SEND_EMAIL_TEMPLATE_PATH`, par `ModeleCourriel.LireAvecRepliFichierAsync` du lot 4 de la phase 1) et ses jetons ; sans
   habillage, ou s'il est illisible, le rendu historique.
4. Le courriel est **remis** au serveur de messagerie, ou **rangé dans la boîte de dépôt** hors production (ci-dessous).
5. **Chaque étape s'écrit dans la trace** du greffe (`soc.TraceEnvoi`), succès ou erreur ; un courriel parti s'enregistre
   dans `soc.CourrielEnvoye` puis la trace le cite. La trace s'écrit hors de toute transaction de l'appelant (un acte
   annulé n'efface pas la trace d'un courriel déjà parti) ; si elle ne s'écrit pas, le courriel part quand même et la
   cause va au journal du serveur (événement 51061), comme la V2 (« best-effort »).

### Où la V3 lit les valeurs du greffe

| Valeur | V2 | V3 aujourd'hui |
|---|---|---|
| habillage | `MAIL_TEMPLATE`, sinon fichier | `soc.ModeleCourriel` (SOC-003), même repli |
| `{{NOM_GREFFIER}}` | `GREFFE_PARAMETRE.GRPAR_TPG_NOM_GREFFIER` | `soc.ParametreGreffe` (SOC-003), base V3 |
| logo | `GRF_PATH_EDITION_LOT1` + `\<code>\Images\logo.*` | `GRF_PATH_EDITION_LOT1` de `soc.GreffeInfoV2` (SOC-003), mêmes règles |
| `{{VILLE_TRIBUNAL}}` | `PARAM_EDITIONS.PRMED_VILLE_MAJ` | **la base V2 du greffe, en lecture** : `PARAM_EDITIONS` n'est pas encore dans la V3 (SED-001) |
| `[ÉTIQUETTE]` des adresses | `INFO_GREFFE` | **la base V2 du greffe, en lecture** : `INFO_GREFFE` n'est pas encore dans la V3 (TRA-025) |

La base V2 s'ouvre par `CatalogueGreffes.OuvrirBaseV2Async` (intention de lecture) et ne se lit que par `LectureV2`
(un SELECT seul). Quand TRA-025 et SED-001 reprendront ces deux tables, seules ces deux lectures changeront de base
(`ValeursGreffe.cs`). Une valeur absente est notée au journal du serveur (événement 51062), comme la V2 notait
« Donnée greffe indisponible ».

## Jamais de vrai courriel hors production (CA06)

Section `Courriels` de la configuration du serveur :

| Clé | Défaut | Rôle |
|---|---|---|
| `Production` | `false` | seul un serveur qui le **dit** remet les courriels au vrai serveur de messagerie |
| `Remise` | `depot` | `depot` (boîte de dépôt) ou `smtp` |
| `Depot` | `%TEMP%\RigV3\courriels` | un fichier `<suivi>.eml` par envoi, lisible par tout logiciel de messagerie |
| `Smtp:Hote`, `Smtp:Port` | —, 25 | le serveur de messagerie (V2 : `srvmail.amitel.fr`) |
| `ConservationJours` | 90 | âge au-delà duquel le ménage efface une trace (CA04) |
| `Menage:Heure` | 03:00 | heure du ménage quotidien (heure du serveur) |

La règle est dans le code (`OptionsCourriels.Decider`), pas dans la mémoire de qui configure : hors production, un
courriel n'est remis qu'à un **serveur jetable du poste** (adresse de bouclage écrite en clair : `127.0.0.1`, `::1`,
`localhost` ; un nom n'est jamais résolu) ; dans tous les autres cas il est rangé dans la boîte de dépôt et la trace
note `SMTP_IGNORE_DEV` (« envoi ignoré »), avec le fichier. Une configuration incohérente arrête le démarrage :
production sans `smtp`, `smtp` sans hôte, durée nulle.

## Les routes

| Verbe | Adresse | Rôle |
|---|---|---|
| GET | `/socle/greffes/{greffe}/courriels` | les envois, un par numéro de suivi, les plus récents d'abord, 500 au plus ; `?du=&au=&destinataire=&modele=&etat=&max=` ; réservé à **MAIL_LOT1** |
| GET | `/socle/greffes/{greffe}/courriels/{suivi}` | un envoi : ses étapes dans l'ordre et le texte du courriel parti (404 inconnu) ; réservé à **MAIL_LOT1** |
| GET | `/socle/greffes/{greffe}/modeles-courriel` | les modèles que l'agent peut choisir : sans ceux réservés à Amitel pour un agent du greffe (CA05) |
| GET | `/socle/greffes/{greffe}/modeles-courriel/{type}` | un modèle : texte, adresses résolues pour le greffe, listes par défaut, aperçu rendu (`ApercuModele`) ; **404** s'il est inconnu **ou** réservé à Amitel pour un agent du greffe (CA05 : il ne le voit pas, même par son nom) |
| GET | `/socle/greffes/{greffe}/listes-diffusion` | les listes de distribution que l'agent voit : publiques (`*`) et les siennes, actives ou non — pas celle d'un collègue (V2 : `GetAllListeDistributionMailByLogin`) |
| POST | `/socle/greffes/{greffe}/modeles-courriel` | crée un modèle (`SaisieModele` → `ModeleMessage`, 201) ; réservé à **GESTEMAIL** |
| PUT | `/socle/greffes/{greffe}/modeles-courriel/{type}` | modifie un modèle ; réservé à **GESTEMAIL** ; **404** pour un agent du greffe sur un modèle réservé à Amitel (il ne le voit pas, CA05) |
| PUT | `/socle/greffes/{greffe}/modeles-courriel/{type}/actif` | désactive ou réactive un modèle (`SaisieActivation`) ; mêmes règles |
| POST | `/socle/greffes/{greffe}/listes-diffusion` | crée une liste (`SaisieListe` → `ListeDiffusion`, 201) ; réservé à **GESTEMAIL** |
| PUT | `/socle/greffes/{greffe}/listes-diffusion/{id}` | modifie une liste que l'agent voit (404 pour la liste privée d'un collègue) ; réservé à **GESTEMAIL** |
| PUT | `/socle/greffes/{greffe}/listes-diffusion/{id}/actif` | désactive ou réactive une liste ; mêmes règles |

Toutes portent `PolitiquesSocle.AgentAdmis` (401 sans session, 403 compte refusé, 404 greffe absent, 503 base muette) et
ouvrent la base du greffe par le catalogue. Les cinq premières ne font que lire ; les six d'écriture exigent la fonction
`GESTEMAIL` (403 sinon, motif nommé, rien n'est écrit) et rendent **400 avec le motif** quand une saisie est refusée.
Pour les lectures : `MAIL_LOT1` est la fonction de l'écran V2 « Mails
envoyés en Lot 1 » ; sans elle, 403 et rien n'est lu. Les trois routes de la Messagerie n'exigent que d'être admis :
ce sont les modèles qu'un agent choisit pour envoyer. La règle CA05 est **une seule requête**
(`ServiceCourriels.ModelesVisiblesAsync`) pour la liste et pour le modèle demandé par son nom. L'**envoi** n'est pas une
route : un acte appelle le service.

## L'écran « Courriels envoyés »

Menu **Configurer › Modèles et envois**, code `PROC_MAIL_LOT1` (celui de l'écran V2 qu'il reprend ; sans fiche dans
`D:\RigApplication\Documentation\reference\proc`, d'où sa place dans `sansFiche` de `tests\MenuGestesCheck`). Comme
l'écran V2 : les courriels du jour à l'ouverture, filtres période, destinataire, modèle, 500 lignes au plus, texte à la
demande. En plus de la V2, qui ne montrait que des « Envoyé » : l'**issue** de chaque envoi (envoyé, refusé et pourquoi,
non remis hors production et son fichier) et ses **étapes**. Lecture seule. Sans `RIGV3_SERVEUR` : trois envois de
démonstration, un par issue. Captures claire et sombre : voir le compte rendu du lot.

## L'écran « Messagerie » (gestion des modèles, T04, CA05)

Menu **Configurer › Modèles et envois**, code `PROC_GESTEMAIL` (la procédure V2 « Modele de mail », code processus
`GESTEMAIL`). Pont `Pont\EcranMessagerie.cs` (`EcranMessagerie.Ouvrir`, `EcranMessagerie.Modele`), écran
`web\src\ecrans\EcranMessagerie.tsx`. Tout vient de la base **du greffe** de la session, par les trois routes ci-dessus.

- **CA05** : un agent du greffe ne reçoit aucun modèle réservé à Amitel (ni dans la liste, ni en demandant son nom) et
  n'a pas la bascule « Amitel ». Cette bascule n'apparaît que si le serveur a rendu un modèle réservé, ce qu'il ne fait
  que pour un agent d'Amitel : l'écran n'a pas d'autre source pour la décider.
- **Aperçu** : le courriel tel qu'il partirait de ce greffe — habillage, ville du tribunal, greffier, logo, adresses
  d'`INFO_GREFFE` — avec deux documents d'exemple, comme l'aperçu de la V2 (`OPE_EDIT_TEMPLATE_MAIL._Apercu`). Rien
  n'est envoyé ni écrit. Il s'affiche dans un cadre sans script. Une étiquette sans valeur dans le greffe est nommée
  (« À compléter »).
- **Démonstration** seulement sans `RIGV3_SERVEUR`, avec le bandeau « Démonstration » : cinq modèles dont les noms,
  objets et cases sont ceux de `RIG_DEV` (lu le 2026-11-21), textes et adresses fictifs, aucun réservé à Amitel.
- Captures du lot 1 (bureau caché, démonstration, lecture) :
  `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\phase2-lot1-courriels\SOC-006-messagerie-clair.png`
  et `…-sombre.png`.

## Modifier les modèles et les listes (T04 suite, lot 15)

Un agent qui a la fonction **`GESTEMAIL`** (« Gestion modèles emails » dans le catalogue de RIG_DEV, code processus de la procédure V2
« Modèle de mail », `proc_gestemail.md:6`)
crée, modifie, désactive et réactive un modèle de courriel et une liste de distribution depuis la Messagerie : boutons
« Nouveau modèle », « Nouvelle liste », « Enregistrer » (« Créer le modèle » pour un modèle neuf), « Annuler »,
« Désactiver » / « Réactiver ». L'écran ne montre ces boutons que si le serveur dit que l'agent a `GESTEMAIL` dans ce
greffe (`RoutesDroits.Verifier`) ; sans elle, **lecture seule**, sans aucun bouton d'écriture, avec la mention
« Lecture seule ». Le serveur reste juge : chaque route d'écriture vérifie `GESTEMAIL` à nouveau. Sans `RIGV3_SERVEUR`,
la démonstration montre le formulaire et **refuse proprement** chaque écriture (« Démonstration : sans serveur, aucun
modèle n'est enregistré… »).

### Les règles de la V2 reprises (`GestionCourriels.cs`, `ReglesCourriels`)

| Règle | Source V2 | V3 |
|---|---|---|
| Nom d'un modèle unique, figé une fois créé | `OPE_EDIT_EMAILMODEL.cs:86-90`, `:251` | 400 « Le nom du modèle de mail existe déjà » (sans tenir compte des majuscules, comme la base) ; 400 si un PUT change le nom |
| Code d'une liste unique **parmi toutes les listes du greffe** (celles des collègues comprises), figé | `OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:59-63`, `:191` | 400 « Le code de la liste de distribution existe déjà » |
| Modèle réservé à Amitel : modifiable par Amitel seul ; case visible pour Amitel seul | `OPE_EDIT_EMAILMODEL.cs:291-300`, `:333` | 404 pour un agent du greffe (modifier, désactiver), comme sa lecture (CA05) ; 403 s'il coche « réservé à Amitel » ; la case n'est à l'écran que pour Amitel |
| Envoi automatique seulement pour un modèle RCS ; RCS décoché → remis à faux | `_SetVisibilityChkEnvoiAuto`, `:349-350` | la case n'apparaît qu'avec « Utilisable RCS » ; le serveur enregistre `Lot1 && EnvoiAutomatique` |
| Désactiver / activer, jamais supprimer | `ActiverDesactiver`, `:202` et `:141-147` ; `OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:103-109` | `UtilisableEnSaisie` / `Actif` ; un second « Désactiver » ne change rien et n'écrit rien |
| `$PieceJointe$` obligatoire dans un modèle HTML sous un habillage réglé | `OPE_EDIT_EMAILMODEL.cs:91-99` | 400, même phrase |
| `$LOGO$` interdit quand le greffe a un habillage (base ou fichier) | `:100-110` | 400, même phrase |
| Le texte devient HTML s'il porte `$LOGO$` | `EmailModel.Sauve`, `EmailModel.cs:307-308` | même règle à l'enregistrement (un modèle texte qui porte `$LOGO$` passe en HTML, comme la V2) |
| Adresses : courriel nu ou nommé, ou boîte du greffe en étiquette (`Greffe <[ADRESSE_MAIL_RCS]>`) | `RIG.TOOLS.RegEx` (`RegEx.cs:16-23`), `OPE_EDIT_EMAILMODEL_ONE_CHOICE.cs:56-73` | mêmes expressions ; une liste se contrôle comme `ULT_MULTITEXT_MULTIMAIL` (« Courriel (x) invalide »), adresses une par ligne ou séparées par « ; », dédoublonnées |
| Liste publique (`*`) ou privée (le compte de l'agent) ; une liste publique ne redevient pas privée | `:64-66`, `:198` | 400 « ne redevient pas privée » ; une liste privée n'existe que pour son propriétaire (404) |
| Listes par défaut d'un modèle : parmi les listes actives que l'agent voit | `OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:99-100` | une liste qu'il ne voit pas lui est refusée (400) |
| Signature du modèle | colonnes `EMAILMOD_SIGNATURE_ACTIVE`, `EMAILMOD_SIGNATURE` (RIG_DEV ; aucun code V2 versionné ne les lit) | « Signature active » et son texte, enregistrés tels quels |
| Dictionnaire des tags réservé à Amitel | `OPE_EDIT_EMAILMODEL.cs:192-193`, texte `OPE_EDIT_TEMPLATE_MAIL.cs:167-182` | le pont ne le rend qu'à un agent d'Amitel (bouton « Dictionnaire de tags ») |
| Une saisie en cours n'est jamais perdue en silence | `OPE_RECHERCHE_EMAILMODEL.LoadEmailModel` (`proc_gestemail.md` § 4) | ouvrir un autre modèle ou une autre liste est refusé, avec un message, tant qu'elle n'est ni enregistrée ni annulée |

Les **40 modèles de RIG_DEV** passent ces règles (contrôle du banc) : aucun ne devient impossible à enregistrer. Le
sujet peut rester vide, comme dans la V2 (`DIVJUD` de RIG_DEV n'en a pas).

Le texte d'un modèle marque ses sauts de ligne par « | » : l'écran les montre en vrais sauts de ligne et les remet en
« | » à l'enregistrement. Un texte qui porte déjà de vrais sauts de ligne en base est montré et renvoyé tel quel.

**Journal** : chaque geste réussi s'écrit au journal des gestes de la base du greffe (SOC-016), **dans sa transaction**,
famille `courriel` (`SOC-006_gestion.sql`) : « a créé », « a modifié » (avec les valeurs avant et après, listes par défaut
comprises), « a désactivé » ; une réactivation est un « a modifié » `{"actif":true}`. Un geste refusé n'écrit rien.
**Après enregistrement**, le pont relit les modèles, les listes et l'aperçu (recalculé par le serveur) : l'écran montre le
courriel tel qu'il partirait désormais.

### Comparaison avec l'écran V2 « Modele de mail »

Aucune capture de la V2 n'existe sur ce poste et aucun exécutable de `PROC_GESTEMAIL` n'y est construit ; la
comparaison est faite sur les formulaires de la V2 (`OPE_*.Designer.cs`, titres `MetierTitre`), lus le 2026-11-21.

| V2 (formulaire, titre) | V3 |
|---|---|
| Recherche : colonnes « Type », « Libellé écran », « Sujet » ; filtre sur le nom au fil de la saisie | liste « nom — libellé écran » (l'objet en infobulle) ; filtre au fil de la saisie sur le nom **ou** le libellé écran |
| Bouton « Afficher / Masquer les éléments inactifs » (modèles `EMAILMOD_UTILISABLE_EN_SAISIE = 0`, listes `LSTDM_ACTIF = 0`) | le même bouton, mêmes libellés, pour les deux |
| « Nom du modèle », « Libellé écran », « Sujet », « Corps » | mêmes libellés ; le corps en « Texte du modèle » (lecture) **et** en aperçu rendu |
| « Expéditeur » (boîte du greffe ou saisie libre), « Répondre à », « Copie cachée » | mêmes libellés ; l'adresse **résolue** pour le greffe, l'étiquette `[ADRESSE_MAIL_…]` en infobulle |
| « Destinataire par défaut (optionnel) » : saisie libre + listes cochées | « Destinataire par défaut » + « Listes par défaut » (seulement celles que l'agent voit) |
| Cases « Utilisable RCS / judiciaire / privilège / comptabilité » | pastilles du modèle, et filtres « Domaine » (RCS, Judiciaire, Privilège, Comptabilité) |
| « Modèle modifiable uniquement par Amitel » (visible pour Amitel seul) | pastille et filtre « Amitel », pour un agent d'Amitel seul |
| « Mail envoyé automatiquement (sans affichage de l'écran de mail) », visible pour un modèle RCS | même libellé, pour un modèle RCS seulement (`_SetVisibilityChkEnvoiAuto`) |
| Listes : « Code », « Libellé », « RCS », « Judiciaire », « Propriétaire », « Actif » | mêmes colonnes, plus les adresses ; « * » s'affiche « Tous les agents » |
| Barre : « Ajouter un modèle », « Désactiver/Activer », Valider / Annuler du formulaire | « Nouveau modèle », « Désactiver » / « Réactiver », « Enregistrer » / « Annuler » (lot 15), pour un agent `GESTEMAIL` |
| « Dictionnaire de tags » (Amitel) | le même texte, pour un agent d'Amitel seul (lot 15) |
| Listes : « Ajouter », Valider, « Désactiver/Activer » ; « Public » / « Privé (login) » | « Nouvelle liste », « Enregistrer », « Désactiver » / « Réactiver » ; « Publique (tous les agents) » / « Privée (compte) » (lot 15) |
| « Signature du greffe », « Ajouter le logo », « Éditer le template courriel » (Amitel) | **non repris** (voir « ce qui reste ») ; `$LOGO$` se tape dans le texte |

## Écarts avec la V2

| Écart | Pourquoi |
|---|---|
| Hors production, **aucun** courriel ne part, même vers des adresses `@amitel` (la V2 les envoyait : `EmailEnvoye.cs:385-387`) | règle du plan : jamais de vrai courriel hors production |
| « Production » est un réglage du **serveur** (`Courriels:Production`), pas la marque `GRF_IS_DEV` du greffe | un greffe de développement tourne sur un serveur de développement (Q4) ; une marque oubliée sur un greffe ne peut pas faire partir de courriel |
| Les colonnes `PWD_*` d'`INFO_GREFFE` ne sont jamais des valeurs de courriel (la V2 aurait rendu `[PWD_SQL]`) | un modèle ne doit pas pouvoir mettre un mot de passe dans un courriel |
| Une étape de plus : `PREPARATION_ERREUR` (modèle inconnu, étiquette sans valeur) | la V2 envoyait avec une adresse vide et échouait plus loin ; la V3 dit la cause |
| Le ménage passe **chaque jour** sur **tous** les greffes et va jusqu'au bout (la V2 effaçait 10 000 traces au lancement de `RIG_Send_Email`) | le serveur n'a pas de lancement par envoi |
| Taille du logo du rendu historique : 400 × 100 toujours (la V2 lisait l'élément de fusion `LOGO_MAIL` / `LOGO`) | ces éléments sont ceux des éditions (SED-001), pas encore dans la V3 |
| L'écran montre aussi les échecs | la V3 les trace (CA03) |
| CA05 : la V2 **affiche** un modèle réservé à Amitel dans la gestion des modèles (GESTEMAIL), en lecture seule (« non modifiable car commun à tous les greffes », `OPE_EDIT_EMAILMODEL.cs:291-300`) ; elle le **masque** dans la liste proposée à l'envoi | la route **et l'écran « Messagerie »** suivent le carnet (« il ne le voit pas ») et la liste d'envoi : l'agent du greffe ne le voit nulle part. **À valider** par le métier : la V2 le lui laissait lire dans GESTEMAIL |
| Messagerie : le filtre porte sur le nom **et** le libellé écran (la V2 : le nom seul) ; des filtres par domaine que la V2 n'avait pas (ses cases sont des champs du modèle) | reprise de la maquette ; aucun modèle n'est caché par là, seulement trié |
| Messagerie : une liste par défaut privée d'un collègue n'est pas montrée dans la fiche d'un modèle | la V2 ne la montrait pas non plus (sa liste cochable ne contient que les listes de l'agent et les publiques) |
| Enregistrer un modèle **garde** ses liens vers les listes que l'agent ne voit pas (la liste privée d'un collègue) | la V2 les **effaçait** (`OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:57-79` : tout lien non coché part, et la liste cochable ne montre pas la liste d'un collègue) : un agent défaisait sans le voir le réglage d'un autre. **À valider** par le métier |
| Modèle réservé à Amitel : un agent du greffe reçoit 404 en le modifiant | suite de CA05 (il ne le voit pas) ; la V2 le lui montrait en lecture seule. **À valider** avec l'écart CA05 ci-dessus |
| L'expéditeur est obligatoire ; une liste a au moins une adresse | un modèle sans expéditeur ne part pas (`PREPARATION_ERREUR`) ; une liste vide ne sert à rien. La V2 laissait enregistrer les deux |
| Une étiquette d'adresse est ancrée des deux côtés (`[ADRESSE_MAIL_RCS]xyz` est refusé) et jamais celle d'un mot de passe (`[PWD_SQL]`) | l'expression de la V2 (`EmailTableInfogreffe`) n'ancrait chaque alternative que d'un côté ; les mots de passe ne sont jamais des adresses (écart déjà pris à l'envoi) |
| La trace d'un geste est une ligne du journal des gestes, dans la transaction du geste | la V2 créait une demande déjà validée (« Désactivation du modèle mail : … »), sur une autre connexion que l'écriture (`proc_gestemail.md` § 9) : une écriture ratée laissait une trace orpheline |
| « Désactiver » ne pose pas de question | la V2 demandait « Voulez-vous vraiment désactiver ce modèle ? » ; le geste est réversible (« Réactiver ») et journalisé |
| Changer de modèle avec une saisie en cours : refusé avec un message | la V2 proposait Oui / Non / Annuler ; ici l'agent enregistre ou annule d'abord : rien n'est perdu ni enregistré sans lui |

## Ce qui ne se fait pas depuis ce poste, et ce qui reste

| Sujet | Pourquoi / où |
|---|---|
| CA01 « 0 écart sur les 40 bases » | prouvé sur `RIG_DEV` (40 modèles, 0 écart) et sur un schéma V2 « versionné » ; les 40 bases des greffes sont sur les serveurs des greffes (lecture SQL-PROD seulement greffe nommé) : à jouer au transfert (`RepriseCourriels.ReprendreAsync` puis `ComparerAsync`, base par base) |
| La durée de 90 jours | à faire valider par le délégué à la protection des données d'Amitel (la trace garde des adresses et des objets de courriers judiciaires) |
| Le chemin de l'habillage en fichier depuis les serveurs de la V3 | `RepriseReglages.RapportAsync` (SOC-003 CA05) le liste s'il est injoignable ; à vérifier sur les serveurs réels |
| Les boutons « Signature du greffe » (insère `RigGreffe.SignatureJudiciaire` dans le texte) et « Ajouter le logo » | la signature judiciaire du greffe vient de ses réglages d'éditions (SED-001), pas encore dans la V3 ; `$LOGO$` se tape dans le texte. À reprendre avec les éditions |
| L'éditeur HTML du texte (`RigHtmlEditor`, quand un habillage est réglé) et « Éditer le template courriel » (Amitel, l'habillage) | le texte se modifie en zone de texte (HTML brut pour un modèle HTML) ; l'habillage relève de SOC-003 (`soc.ModeleCourriel`) et n'a pas encore d'écran |
| La fonction `GESTEMAIL` dans chaque base de greffe | elle vient du catalogue V2 (`PROCESSUS_ET_FONCTIONALITE`) par la reprise des droits (SOC-002) ; les rôles qui l'ont se vérifient greffe par greffe au transfert |
| Les documents « par lien » | la V2 publie la pièce et met son lien (`_getLienHttp`) ; la V3 reçoit le lien tout fait de l'acte : la publication des pièces viendra avec les éditions |
| La reprise de l'historique (`EMAIL_ENVOYE`, `EMAIL_ENVOI_TRACE`) | non faite ici : la trace V2 se purge à 90 jours ; l'historique des courriels relève de la recopie (REC) |

## Les contre-épreuves faites (2026-11-21)

Chaque fichier remis à l'identique après coup, banc vert ensuite :

| Défaut injecté | Contrôles passés au rouge |
|---|---|
| l'échec d'envoi écrit comme un succès (`ServiceCourriels.cs`) | 6, dont « CA03 : le serveur de messagerie REFUSE le destinataire » |
| la garde hors production levée (`OptionsCourriels.Decider`) | « CA06 : hors production, un serveur de messagerie qui n'est pas celui du poste n'est jamais appelé » (+1) |
| le filtre des modèles réservés à Amitel retiré | « CA05 : un agent du greffe ne voit pas les modèles réservés à Amitel » |
| le repli sur le fichier d'habillage retiré (`ValeursGreffe.cs`) | « CA02 : sans habillage en base, celui du fichier… » et son jumeau |
| le filtre « réservé à Amitel » retiré de `ModelesVisiblesAsync` | 4 : CA05 de la route (liste, par le nom) et CA05 de l'écran « Messagerie » (liste, par le nom) |
| le pont annonce la bascule « Amitel » à tout le monde (`EcranMessagerie.cs`) | « Ecran SOC-006 CA05 : la Messagerie d'un agent du greffe … sans la bascule « Amitel » » |
| l'écran montre la bascule « Amitel » à tout le monde (`EcranMessagerie.tsx`, vitest) | 2 tests de `EcranMessagerie.test.tsx` (agent du greffe, démonstration) |
| lot 15 : un agent du greffe peut toucher un modèle réservé à Amitel (`ModeleVisibleAsync` sans la règle) | « SOC-006 T04 / CA05 : un agent du GREFFE ne modifie ni ne désactive un modèle réservé à Amitel » (200 / 200 au lieu de 404) |
| lot 15 : la vérification de `GESTEMAIL` retirée des routes d'écriture (`ModuleCourriels.Gerer`) | « SANS la fonction GESTEMAIL, chacune des 6 écritures … est refusée » (201, 200, 200, 201, 200, 200) et le contrôle de l'écran sans GESTEMAIL |
| lot 15 : la ligne du journal de la création retirée (`GestionCourriels.CreerModeleAsync`) | « la création est écrite au journal des gestes… » et le décompte des gestes du contrôle des saisies |
| lot 15 : l'écran montre ses boutons d'écriture sans `GESTEMAIL` (`EcranMessagerie.tsx`, vitest) | 2 tests de `EcranMessagerie.test.tsx` (« sans GESTEMAIL : … AUCUN bouton d'écriture », agent du greffe) |

L'ancienne maquette de la Messagerie (modèles inventés, bascule « Amitel » pour tous, aucun appel au pont) faisait
échouer les 8 tests de `EcranMessagerie.test.tsx` écrits avant le branchement (2026-11-21).

Lot 15 : les bancs `NumerotationCompteurCheck`, `SocleAiguillageCheck`, `SocleLivraisonCheck`, `SoclePreferencesCheck` et
`SocleSupervisionCheck` arrêtent désormais leur serveur (`await app.StopAsync()`) avant de le détruire, comme le contrôle
« Arrêt » de ce banc : avant, quatre d'entre eux écrivaient « BackgroundService failed » au journal à leur sortie (le
cinquième, Supervision, filtre sa console en dessous de « critique »).

<!-- fix-ok: réception refusée du lot 1 (chef, 2026-11-21) : l'écran de la story (EcranMessagerie.tsx) n'était branché sur aucune route ; sections Messagerie, routes, écarts, reste et contre-épreuves ajoutées. -->

