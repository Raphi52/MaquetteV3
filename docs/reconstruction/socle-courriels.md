# Socle — les courriels du greffe, leurs modèles et leur trace (SOC-006)

Lot 1 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`, **SOC-006** : « Envoyer
les courriels du greffe avec ses modèles et en garder la trace ».

| Couche | Ce qui est fait | Où |
|---|---|---|
| Base (T01) | modèles (`soc.ModeleMessage`), listes de diffusion (`soc.ListeDiffusion`, `soc.ModeleMessageListe`), courriels partis (`soc.CourrielEnvoye`), trace de chaque étape (`soc.TraceEnvoi`) | `Base\migrations\SOC-006_courriels.sql` |
| Serveur (T02) | le service d'envoi : modèle du greffe, adresses, jetons, habillage en base puis fichier, remise ou boîte de dépôt, trace à chaque étape | `Serveur\Socle\Courriels\ServiceCourriels.cs`, `HabillageCourriel.cs`, `ValeursGreffe.cs`, `RemiseCourriel.cs`, `OptionsCourriels.cs`, `TracesCourriels.cs` |
| Exploitation (T03) | le ménage quotidien des traces de plus de 90 jours, durée réglable | `Serveur\Socle\Courriels\MenageCourriels.cs` |
| Écran (T04) | « Messagerie » (gestion des modèles : modèles et listes du greffe, aperçu ; ni modèle ni bascule « Amitel » pour un agent du greffe, CA05 ; **création, modification, désactivation et réactivation** d'un modèle et d'une liste pour un agent qui a la fonction `GESTEMAIL`, lot 15 ; **les gestes de l'écran V2** — éditeur mis en forme du texte HTML, contenu actif retiré à l'enregistrement, boîtes du greffe choisies par leur adresse, « Ajouter le logo », question au changement de modèle, lot 16) et « Courriels envoyés » (menu Configurer › Modèles et envois) | `Pont\EcranMessagerie.cs`, `web\src\ecrans\EcranMessagerie.tsx`, `Pont\EcranCourriels.cs`, `web\src\ecrans\EcranCourriels.tsx`, `ModuleCourriels.cs`, `GestionCourriels.cs`, `Base\migrations\SOC-006_gestion.sql` |
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
| GET | `/socle/greffes/{greffe}/boites-courriel` | ce que le formulaire d'un modèle propose pour ce greffe (`BoitesDuGreffe`, lot 16) : ses 9 boîtes par leur **adresse** (`INFO_GREFFE` de sa base V2, lue comme l'envoi) et l'état de son habillage (réglé : éditeur mis en forme ; présent : `$LOGO$` refusé) |

Toutes portent `PolitiquesSocle.AgentAdmis` (401 sans session, 403 compte refusé, 404 greffe absent, 503 base muette) et
ouvrent la base du greffe par le catalogue. Les six de lecture ne font que lire ; les six d'écriture exigent la fonction
`GESTEMAIL` (403 sinon, motif nommé, rien n'est écrit) et rendent **400 avec le motif** quand une saisie est refusée.
Pour les lectures : `MAIL_LOT1` est la fonction de l'écran V2 « Mails
envoyés en Lot 1 » ; sans elle, 403 et rien n'est lu. Les quatre routes de lecture de la Messagerie n'exigent que d'être
admis : ce sont les modèles qu'un agent choisit pour envoyer, et les adresses de son greffe. La règle CA05 est **une seule requête**
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
| Champs obligatoires — modèle : nom, libellé écran, sujet, texte, expéditeur ; liste : code, libellé, adresses | `MetierObligatoire` : `OPE_EDIT_EMAILMODEL.Designer.cs:103` (sujet), `:115` (texte), `:172` (nom), `:191` (libellé écran), `OPE_EDIT_EMAILMODEL_ONE_CHOICE.Designer.cs:90` (expéditeur), `OPE_EDIT_LISTEDISTRIBUTIONMAIL.Designer.cs:81`, `:91`, `:135` ; refusés à la validation par `Ult.moteur.cs:516` | 400 « … est obligatoire. » ; l'écran marque « requis » exactement ces champs |
| Modèle réservé à Amitel : modifiable par Amitel seul ; case visible pour Amitel seul | `OPE_EDIT_EMAILMODEL.cs:291-300`, `:333` | 404 pour un agent du greffe (modifier, désactiver), comme sa lecture (CA05) ; 403 s'il coche « réservé à Amitel » ; la case n'est à l'écran que pour Amitel |
| Envoi automatique seulement pour un modèle RCS ; RCS décoché → remis à faux | `_SetVisibilityChkEnvoiAuto`, `:349-350` | la case n'apparaît qu'avec « Utilisable RCS » ; le serveur enregistre `Lot1 && EnvoiAutomatique` |
| Désactiver / activer, jamais supprimer | `ActiverDesactiver`, `:202` et `:141-147` ; `OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:103-109` | `UtilisableEnSaisie` / `Actif` ; un second « Désactiver » ne change rien et n'écrit rien |
| Désactiver / activer demande d'abord « Voulez-vous vraiment désactiver (activer) ce modèle / cette liste ? », Oui / Non | `OPE_EDIT_EMAILMODEL.cs:63`, `:68` ; `OPE_EDIT_LISTEDISTRIBUTIONMAIL.cs:39`, `:44` | la même question sous les boutons de la fiche ; « Non » ne fait rien |
| `$PieceJointe$` obligatoire dans un modèle HTML sous un habillage réglé | `OPE_EDIT_EMAILMODEL.cs:91-99` | 400, même phrase |
| `$LOGO$` interdit quand le greffe a un habillage (base ou fichier) | `:100-110` | 400, même phrase |
| Le texte devient HTML s'il porte `$LOGO$` | `EmailModel.Sauve`, `EmailModel.cs:307-308` | même règle à l'enregistrement (un modèle texte qui porte `$LOGO$` passe en HTML, comme la V2) |
| Adresses : courriel nu ou nommé, ou boîte du greffe en étiquette (`Greffe <[ADRESSE_MAIL_RCS]>`) | `RIG.TOOLS.RegEx` (`RegEx.cs:16-23`), `OPE_EDIT_EMAILMODEL_ONE_CHOICE.cs:56-73` | mêmes expressions ; une liste se contrôle comme `ULT_MULTITEXT_MULTIMAIL` (« Courriel (x) invalide »), adresses une par ligne ou séparées par « ; », dédoublonnées |
| Liste publique (`*`) ou privée (le compte de l'agent) ; une liste publique ne redevient pas privée | `:64-66`, `:198` | 400 « ne redevient pas privée » ; une liste privée n'existe que pour son propriétaire (404) |
| Listes par défaut d'un modèle : parmi les listes **actives** que l'agent voit | `OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:98-99` | l'écran ne propose que celles-là ; la liste privée d'un collègue ou une liste désactivée est refusée (400) |
| À l'enregistrement, le modèle n'a plus **que** les listes cochées | `OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:58-61` (tous les liens marqués à supprimer, les cochés gardés) | même règle : un lien vers la liste privée d'un collègue ou vers une liste désactivée part aussi (à valider, voir « Écarts ») |
| Signature du modèle | colonnes `EMAILMOD_SIGNATURE_ACTIVE`, `EMAILMOD_SIGNATURE` (RIG_DEV ; aucun code V2 versionné ne les lit) | « Signature active » et son texte, enregistrés tels quels |
| Dictionnaire des tags réservé à Amitel | `OPE_EDIT_EMAILMODEL.cs:192-193`, texte `OPE_EDIT_TEMPLATE_MAIL.cs:167-182` | le pont ne le rend qu'à un agent d'Amitel (bouton « Dictionnaire de tags ») |
| Une saisie en cours n'est jamais perdue en silence | `OPE_RECHERCHE_EMAILMODEL.LoadEmailModel` (`proc_gestemail.md` § 4) | **modèle** (lot 16) : la question de la V2, voir plus bas ; **liste** : ouvrir autre chose est refusé, avec un message, tant qu'elle n'est ni enregistrée ni annulée (la V2 a mis sa question en commentaire, `OPE_RECHERCHE_LISTEDISTRIBUTIONMAIL.cs:90`) |

Sur les **40 modèles de RIG_DEV** (contrôle du banc), **38** passent ces règles tels quels et **2** sont à compléter :
incomplets en base, la V2 les refuserait aussi à l'enregistrement — `DIVJUD` (sans sujet) et « Envoi document
privilège » (sans libellé écran). L'agent complète le champ à son prochain enregistrement, comme dans la V2. Aucun ne
devient impossible à enregistrer.

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
| « Nom du modèle », « Libellé écran », « Sujet », « Corps » | mêmes libellés ; le corps en « Texte du modèle » (lecture) **et** en aperçu rendu ; en modification, l'éditeur mis en forme pour un modèle HTML sous un habillage réglé (lot 16) |
| « Expéditeur » (boîte du greffe ou saisie libre), « Répondre à », « Copie cachée » | lecture : l'adresse **résolue** pour le greffe, l'étiquette en infobulle ; modification (lot 16) : « Saisie libre » et les boîtes du greffe **par leur adresse**, « Expéditeur du courrier (libellé / adresse du mail) », « Répondre à (saisie libre) », « Copie cachée à (saisie libre) » |
| « Destinataire par défaut (optionnel) » : saisie libre + listes cochées | « Destinataire par défaut » + « Listes par défaut » (les actives que l'agent voit, comme la V2) |
| Cases « Utilisable RCS / judiciaire / privilège / comptabilité » | pastilles du modèle, et filtres « Domaine » (RCS, Judiciaire, Privilège, Comptabilité) |
| « Modèle modifiable uniquement par Amitel » (visible pour Amitel seul) | pastille et filtre « Amitel », pour un agent d'Amitel seul |
| « Mail envoyé automatiquement (sans affichage de l'écran de mail) », visible pour un modèle RCS | même libellé, pour un modèle RCS seulement (`_SetVisibilityChkEnvoiAuto`) |
| Listes : « Code », « Libellé », « RCS », « Judiciaire », « Propriétaire », « Actif » | mêmes colonnes, plus les adresses ; « * » s'affiche « Tous les agents » |
| Barre : « Ajouter un modèle », « Désactiver/Activer », Valider / Annuler du formulaire | « Nouveau modèle », « Désactiver » / « Réactiver », « Enregistrer » / « Annuler » (lot 15), pour un agent `GESTEMAIL` |
| « Dictionnaire de tags » (Amitel) | le même texte, pour un agent d'Amitel seul (lot 15) |
| Listes : « Ajouter », Valider, « Désactiver/Activer » ; « Public » / « Privé (login) » | « Nouvelle liste », « Enregistrer », « Désactiver » / « Réactiver » ; « Publique (tous les agents) » / « Privée (compte) » (lot 15) |
| « Ajouter le logo » | le même bouton, sous le texte (lot 16) |
| « Signature du greffe », « Éditer le template courriel » (Amitel) | **non repris** (voir « ce qui reste ») |

## Les gestes de l'écran V2 repris (T04 fin, lot 16)

Écarts relevés par le chef en comparant la Messagerie du lot 15 à l'écran V2 « Modele de mail », repris un par un. Sources
relues : `OPE_EDIT_EMAILMODEL.cs` (`LinkDonnees`, `Toolbar_ButtonClick`, `_TemplateActif`), `OPE_EDIT_EMAILMODEL_ONE_CHOICE.cs`,
`OPE_EDIT_EMAILMODEL_MANY_CHOICES.cs` (`RigOpeOutilsMetier\EMAIL`), `OPE_RECHERCHE_EMAILMODEL.cs` (`LoadEmailModel`),
`RigHtmlEditor.cs`, `RigHtmlSanitizer.cs`, `Ult_HtmlEditor.cs` (`Rig_Ult_Metier`), `RegEx.cs` (`DecoupeMailInfoGreffe`).

| Geste | V2 | V3 |
|---|---|---|
| Texte d'un modèle HTML | éditeur mis en forme si le modèle est HTML **et** que le greffe a un habillage réglé (`EMAILMOD_CORPS_HTML && _TemplateActif`, `LinkDonnees`) ; sinon zone de texte | même condition, décidée à l'ouverture du modèle (`FicheModele.editeur`). Barre de `RigHtmlEditor`, dans son ordre et avec ses textes : **G** (Gras), **I** (Italique), **S** (Souligné), **• Liste** (Liste à puces), **1. Liste** (Liste numérotée), **Lien** (Insérer un lien, adresse pré-remplie « https:// »), **Effacer mise en forme**. Chaque bouton est la commande d'édition du navigateur que la V2 passait à `ExecCommand` (`bold`, `italic`, `underline`, `insertUnorderedList`, `insertOrderedList`, `createLink`, `removeFormat`) : **aucun paquet npm de plus**. La sélection de l'agent est gardée (la V2 agissait au `MouseDown`) |
| Texte écrit en lignes dans un modèle HTML (25 des 37 de `RIG_DEV`) | `Ult_HtmlEditor._CorpsToHtml` : texte sans balise échappé, sauts de ligne en `<br />` | même règle ; et comme `_userEdited` de la V2, le texte de la base **repart tel quel** tant que l'agent n'a pas tapé dans l'éditeur (entrer et sortir ne modifie rien) |
| Nettoyage du texte HTML | aucun dans GESTEMAIL : `Ult_HtmlEditor` enregistre `InnerHtml` **tel quel** (`Ult_HtmlEditor.cs:228-231`) ; `RigHtmlSanitizer` ne sert qu'à l'écran d'envoi | au **serveur**, à **chaque** enregistrement d'un modèle HTML (déjà HTML, ou qui le devient par `$LOGO$`), le **contenu actif seul** part (décision du chef, 2026-11-21) : blocs `<script>` (fin `</script >` comprise, jusqu'à ce qu'il n'en reste aucun), attributs `on…` d'une balise (`onclick`, `onerror`, même collés après un guillemet), adresses `javascript:` d'un `href`, `src`, `action`, `formaction` ou `xlink:href` (même écrites avec des entités, `jav&#x61;script:`), remplacées par `"#"` comme `RigHtmlSanitizer` (`ReglesCourriels.Assainir`). **Tout le reste est gardé octet pour octet**, comme la V2 l'enregistrait : `<font>`, `<style>`, commentaires, `&nbsp;` répétés, blancs des bords, et un « on = » ou un « javascript: » du texte, hors d'une balise. La base **et le journal** gardent ce texte ; les contrôles (vide, `$PieceJointe$`) le jugent. La **taille** (4 000 caractères) se juge sur la **saisie**, avant le nettoyage : la V2 écrivait le texte brut dans `EMAILMOD_CORPS` (4 000), et le nettoyage ne lit ainsi jamais plus (sur 1 Mo de « <a », il passait plus de 5 minutes ; borné, 62 ms pour refuser). Un modèle texte reste tel que saisi, comme la V2 |
| Expéditeur | `ONE_CHOICE` : « Saisie libre » + 5 boîtes montrées par leur **adresse** (`INFO_GREFFE`) ; libellé et adresse (lecture pour une boîte) ; enregistre « Libellé <[ADRESSE_MAIL_X]> » | même choix, mêmes libellés ; l'adresse vient du serveur (`GET …/boites-courriel`) ; à côté de l'adresse, le nom court de la boîte (« RCS », « judiciaire »…) ; l'étiquette en infobulle ; la base garde l'**étiquette** |
| Répondre à | `MANY_CHOICES « RA »` : 5 boîtes à cocher + saisie libre ; « ne pas répondre » exclut les autres et la saisie libre, et inversement | même chose ; enregistre « saisie libre;[ÉTIQUETTE];… » dans l'ordre de la V2 |
| Copie cachée | `MANY_CHOICES « CC »` : 4 boîtes (sans « ne pas répondre ») + 4 accusés d'envoi + saisie libre ; à l'ajout, accusé d'envoi judiciaire coché ; cocher « Utilisable X » coche l'accusé d'envoi X (`CheckUtilisableEn`) | même chose |
| « Ajouter le logo » | ajoute `$LOGO$` à la fin du texte, le modèle passe en HTML ; refusé à la validation sous un habillage | même geste ; sous l'habillage du greffe, l'écran le refuse **tout de suite**, avec la phrase du serveur, sans rien ajouter ; le serveur le refuse aussi (400) |
| Ouvrir un autre modèle (ou « Nouveau modèle ») avec une saisie en cours | « Des changements ont été saisis, voulez-vous les enregistrer ? » Oui (enregistre puis ouvre) / Non (abandonne) | la même question, dans la forme de celle de « Désactiver » (pastille « Attention », Oui / Non) |

Les adresses des boîtes sont lues **où la V3 les lit aujourd'hui** : `INFO_GREFFE` de la base V2 du greffe, par
`LectureValeursGreffe.LireAsync` (lecture seule) ; jamais une colonne de mot de passe. Une boîte que le greffe n'a pas
s'affiche « aucune adresse dans ce greffe » (la V2 montrait un bouton sans libellé). Quand TRA-025 reprendra `INFO_GREFFE`,
seule cette lecture changera de base.

Sur `RIG_DEV` (lu dans la V2 à chaque passage du banc) : le nettoyage change **0** des 37 modèles HTML, et `CREJET` et
`MPCS_INFO_OUV_JUGE` (balises `<font>`) re-enregistrés tels quels par un agent gardent leur texte octet pour octet. L'essai
refusé, qui reprenait toutes les règles de `RigHtmlSanitizer`, en changeait **15** : `CREJET`, `INPI_DCA_RECLA`,
`MPCS_INFO_OUV_JUGE` par leurs `<font>`, douze autres par leurs blancs de bord — l'ancien contrôle comparait au texte rogné et
n'en montrait que 3.

Captures (bureau caché, démonstration, adresses fictives) : voir le compte rendu du lot 16 —
`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\phase2-lot16-messagerie\`.

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
| Enregistrer un modèle **efface** ses liens vers les listes que l'agent ne peut pas cocher (la liste privée d'un collègue, une liste désactivée) | **codé comme la V2** (`OPE_EDIT_EMAILMODEL_LISTE_DISTRIB.cs:58-61` : tout lien non coché part, et la liste cochable ne montre que les listes actives de l'agent et les publiques, `:98-99`). Mais un agent défait ainsi, sans le voir, le réglage d'un collègue. **À valider** par le métier : garder ces liens serait plus sûr ; c'est une ligne de `GestionCourriels.LierListesAsync` |
| Modèle réservé à Amitel : un agent du greffe reçoit 404 en le modifiant | suite de CA05 (il ne le voit pas) ; la V2 le lui montrait en lecture seule. **À valider** avec l'écart CA05 ci-dessus |
| Une étiquette d'adresse est ancrée des deux côtés (`[ADRESSE_MAIL_RCS]xyz` est refusé) et jamais celle d'un mot de passe (`[PWD_SQL]`) | l'expression de la V2 (`EmailTableInfogreffe`) n'ancrait chaque alternative que d'un côté ; les mots de passe ne sont jamais des adresses (écart déjà pris à l'envoi) |
| La trace d'un geste est une ligne du journal des gestes, dans la transaction du geste | la V2 créait une demande déjà validée (« Désactivation du modèle mail : … »), sur une autre connexion que l'écriture (`proc_gestemail.md` § 9) : une écriture ratée laissait une trace orpheline |
| Changer de modèle avec une saisie en cours : « Oui » refusé par le serveur garde la saisie et n'ouvre rien (lot 16) | la V2 appelait `Processus.Valider()` puis chargeait l'autre modèle sans lire son résultat (`OPE_RECHERCHE_EMAILMODEL.cs:107-124`) : une saisie refusée était remplacée. Ici le motif s'affiche et la saisie reste |
| Le texte HTML perd son contenu actif (`<script>`, `on…`, `javascript:`) à chaque enregistrement, même hors de l'éditeur (lot 16) | dans GESTEMAIL, la V2 enregistrait le HTML **brut** : son éditeur branché est `Ult_HtmlEditor` (`HtmlCorps`, G/I/S et deux listes, `InnerHtml` tel quel) ; `RigHtmlEditor` et son assainisseur servent à l'écran d'envoi (`RIG_Send_Email`, `frmEditerMail`), et `_InitHtmlEditor` d'`OPE_EDIT_EMAILMODEL` n'est jamais appelé. La V3 garde les 7 boutons de `RigHtmlEditor` et ne retire que le contenu actif, qui s'exécuterait dans l'éditeur de l'écran (décision du chef, 2026-11-21) : aucun modèle de `RIG_DEV` n'en porte, aucun ne change (ci-dessus). Le reste des règles de `RigHtmlSanitizer` (`<font>`, `<style>`, commentaires, `&nbsp;`, blancs) n'est **pas** repris |
| Expéditeur : libellé sans blancs de bord (lot 16) | la V2 recollait « libellé + " <" » sans couper l'espace lu (`DecoupeMailInfoGreffe` le garde) : 22 des 32 expéditeurs nommés de `RIG_DEV` ont deux espaces avant « < ». La V3 en remet un seul, au prochain enregistrement du modèle |
| « Répondre à » / « Copie cachée » : une étiquette que le formulaire ne propose pas reste en saisie libre (lot 16) | la V2 la laissait tomber sans le dire (`GetValueOfPropertyForCheckbox` : ni case cochée, ni texte libre) |
| Le lien de l'éditeur se saisit dans la fiche (champ « Adresse du lien », OK / Annuler) | la V2 ouvrait une petite fenêtre (`DemanderUrl`) ; même pré-remplissage « https:// », même refus d'une adresse vide |

## Ce qui ne se fait pas depuis ce poste, et ce qui reste

| Sujet | Pourquoi / où |
|---|---|
| CA01 « 0 écart sur les 40 bases » | prouvé sur `RIG_DEV` (40 modèles, 0 écart) et sur un schéma V2 « versionné » ; les 40 bases des greffes sont sur les serveurs des greffes (lecture SQL-PROD seulement greffe nommé) : à jouer au transfert (`RepriseCourriels.ReprendreAsync` puis `ComparerAsync`, base par base) |
| La durée de 90 jours | à faire valider par le délégué à la protection des données d'Amitel (la trace garde des adresses et des objets de courriers judiciaires) |
| Le chemin de l'habillage en fichier depuis les serveurs de la V3 | `RepriseReglages.RapportAsync` (SOC-003 CA05) le liste s'il est injoignable ; à vérifier sur les serveurs réels |
| Le bouton « Signature du greffe » (insère `RigGreffe.SignatureJudiciaire` dans le texte) | la signature judiciaire du greffe vient de ses réglages d'éditions (SED-001), pas encore dans la V3. À reprendre avec les éditions (« Ajouter le logo » : fait, lot 16) |
| « Éditer le template courriel » (Amitel, l'habillage) | l'habillage relève de SOC-003 (`soc.ModeleCourriel`) et n'a pas encore d'écran (l'éditeur mis en forme du texte : fait, lot 16) |
| « Répondre à » obligatoire | la V2 le refuse vide (`OPE_EDIT_EMAILMODEL_MANY_CHOICES.Verifier`) ; le serveur de la V3 ne l'exige pas (lot 15). 1 modèle de `RIG_DEV` n'a pas de réponse : la règle le ferait compléter à son prochain enregistrement. À reprendre dans `ReglesCourriels.ControlerModele` après accord |
| Le libellé de l'expéditeur d'un modèle neuf | la V2 le pré-remplit du nom du greffe (`ParametresEdition.TypeEtNomGreffeMin`, `PARAM_EDITIONS`) ; la V3 le laisse vide : `PARAM_EDITIONS` n'est pas encore dans la V3 (SED-001) |
| Les captures de la V2 | aucun exécutable de `PROC_GESTEMAIL` n'est construit sur ce poste : la comparaison des captures V3 se fait sur les formulaires de la V2 (`*.Designer.cs`), pas sur une image |
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
| lot 16 : le nettoyage retiré de `ReglesCourriels.ControlerModele` (le texte reçu écrit tel quel) | « le texte d'un modèle HTML est ASSAINI à l'enregistrement » — le `<script>`, le `onclick` et le lien `javascript:` passaient en base (rejoué le 2026-11-21 sur la version « contenu actif seul » : 1 échec, ce contrôle) |
| lot 16 : les règles entières de `RigHtmlSanitizer` (l'essai refusé : `<style>`, commentaires, `<font>`, `&nbsp;` répétés, blancs des bords retirés) | 6 : le contrôle du contenu actif (`onerror` collé après un guillemet, `</SCRIPT >`, `jav&#x61;script:` passaient), le jumeau « le reste du HTML s'enregistre tel que la V2 l'écrivait », l'enregistrement de la saisie piégée (`<font>` et commentaire perdus), le jumeau « sans contenu actif, tel quel », « sur les 37 modèles HTML de RIG_DEV … change 15 texte(s) », « CREJET / MPCS_INFO_OUV_JUGE re-enregistrés : TEXTE CHANGE » |
| lot 16 : la taille jugée après le nettoyage (l'ordre de l'essai refusé) | « la taille du texte se juge sur la saisie » — 4 001 caractères dont un `<script>` enregistrés (200 au lieu de 400) |
| lot 16 : `$LOGO$` ne fait plus passer le modèle en HTML | 3 : « Ajouter le logo », le `$LOGO$` sans habillage du lot 15, les listes par défaut (BANC_DOC reste texte) |
| lot 16 : le pont ne transmet pas les boîtes du greffe (`EcranMessagerie.OuvrirAsync`) | « Ecran SOC-006 T04 : la Messagerie propose les boîtes par leur adresse… » |
| lot 16 (vitest, ensemble) : éditeur jamais montré, étiquette envoyée sans crochets, pas de question au changement de modèle, logo accepté sous habillage (`EcranMessagerie.tsx`) | 9 des 29 tests de `EcranMessagerie.test.tsx` : 7 des 9 tests du lot 16 (éditeur ×2, boîtes, logo, question ×3 ; les jumeaux « zone de texte » et « saisie libre » restent verts, comme ils le doivent) et 2 tests du lot 15 qui composent l'expéditeur |

L'ancienne maquette de la Messagerie (modèles inventés, bascule « Amitel » pour tous, aucun appel au pont) faisait
échouer les 8 tests de `EcranMessagerie.test.tsx` écrits avant le branchement (2026-11-21).

Lot 15 : les bancs `NumerotationCompteurCheck`, `SocleAiguillageCheck`, `SocleLivraisonCheck`, `SoclePreferencesCheck` et
`SocleSupervisionCheck` arrêtent désormais leur serveur (`await app.StopAsync()`) avant de le détruire, comme le contrôle
« Arrêt » de ce banc : avant, quatre d'entre eux écrivaient « BackgroundService failed » au journal à leur sortie (le
cinquième, Supervision, filtre sa console en dessous de « critique »).
Lot 16 : `NumerotationV2Check` aussi (`await app.StopAsync()` avant le `DisposeAsync` de NUM-007 CA04) : « BackgroundService
failed » à sa sortie passe de 1 à 0, ses 58 contrôles restent verts (mesuré le 2026-11-21).

<!-- fix-ok: lot 16 -- écarts V2 relevés par le chef et lus dans cette page avant d'écrire : « non repris » pour l'éditeur HTML et « Ajouter le logo », adresses en étiquettes tapées, changement de modèle refusé par un message. -->
<!-- fix-ok: réception refusée du lot 1 (chef, 2026-11-21) : l'écran de la story (EcranMessagerie.tsx) n'était branché sur aucune route ; sections Messagerie, routes, écarts, reste et contre-épreuves ajoutées. -->

