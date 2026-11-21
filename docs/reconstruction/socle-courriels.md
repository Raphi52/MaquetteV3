# Socle — les courriels du greffe, leurs modèles et leur trace (SOC-006)

Lot 1 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`, **SOC-006** : « Envoyer
les courriels du greffe avec ses modèles et en garder la trace ».

| Couche | Ce qui est fait | Où |
|---|---|---|
| Base (T01) | modèles (`soc.ModeleMessage`), listes de diffusion (`soc.ListeDiffusion`, `soc.ModeleMessageListe`), courriels partis (`soc.CourrielEnvoye`), trace de chaque étape (`soc.TraceEnvoi`) | `Base\migrations\SOC-006_courriels.sql` |
| Serveur (T02) | le service d'envoi : modèle du greffe, adresses, jetons, habillage en base puis fichier, remise ou boîte de dépôt, trace à chaque étape | `Serveur\Socle\Courriels\ServiceCourriels.cs`, `HabillageCourriel.cs`, `ValeursGreffe.cs`, `RemiseCourriel.cs`, `OptionsCourriels.cs`, `TracesCourriels.cs` |
| Exploitation (T03) | le ménage quotidien des traces de plus de 90 jours, durée réglable | `Serveur\Socle\Courriels\MenageCourriels.cs` |
| Écran (T04, en partie) | « Courriels envoyés » (menu Configurer › Modèles et envois) ; la route des modèles masque les modèles réservés à Amitel (CA05) | `Pont\EcranCourriels.cs`, `web\src\ecrans\EcranCourriels.tsx`, `ModuleCourriels.cs` |
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

Toutes portent `PolitiquesSocle.AgentAdmis` (401 sans session, 403 compte refusé, 404 greffe absent, 503 base muette),
ouvrent la base du greffe par le catalogue, et ne font que lire. `MAIL_LOT1` est la fonction de l'écran V2 « Mails
envoyés en Lot 1 » ; sans elle, 403 et rien n'est lu. L'**envoi** n'est pas une route : un acte appelle le service.

## L'écran « Courriels envoyés »

Menu **Configurer › Modèles et envois**, code `PROC_MAIL_LOT1` (celui de l'écran V2 qu'il reprend ; sans fiche dans
`D:\RigApplication\Documentation\reference\proc`, d'où sa place dans `sansFiche` de `tests\MenuGestesCheck`). Comme
l'écran V2 : les courriels du jour à l'ouverture, filtres période, destinataire, modèle, 500 lignes au plus, texte à la
demande. En plus de la V2, qui ne montrait que des « Envoyé » : l'**issue** de chaque envoi (envoyé, refusé et pourquoi,
non remis hors production et son fichier) et ses **étapes**. Lecture seule. Sans `RIGV3_SERVEUR` : trois envois de
démonstration, un par issue. Captures claire et sombre : voir le compte rendu du lot.

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
| CA05 : la V2 **affiche** un modèle réservé à Amitel dans la gestion des modèles (GESTEMAIL), en lecture seule (« non modifiable car commun à tous les greffes », `OPE_EDIT_EMAILMODEL.cs:291-300`) ; elle le **masque** dans la liste proposée à l'envoi | la route suit le carnet (« il ne le voit pas ») et la liste d'envoi ; **à valider** pour l'écran de gestion des modèles |

## Ce qui ne se fait pas depuis ce poste, et ce qui reste

| Sujet | Pourquoi / où |
|---|---|
| CA01 « 0 écart sur les 40 bases » | prouvé sur `RIG_DEV` (40 modèles, 0 écart) et sur un schéma V2 « versionné » ; les 40 bases des greffes sont sur les serveurs des greffes (lecture SQL-PROD seulement greffe nommé) : à jouer au transfert (`RepriseCourriels.ReprendreAsync` puis `ComparerAsync`, base par base) |
| La durée de 90 jours | à faire valider par le délégué à la protection des données d'Amitel (la trace garde des adresses et des objets de courriers judiciaires) |
| Le chemin de l'habillage en fichier depuis les serveurs de la V3 | `RepriseReglages.RapportAsync` (SOC-003 CA05) le liste s'il est injoignable ; à vérifier sur les serveurs réels |
| La gestion des modèles (écran `EcranMessagerie`, T04) | la maquette existe ; la brancher sur `soc.ModeleMessage` (lecture par la route des modèles, modification à écrire) n'était pas dans ce lot |
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
