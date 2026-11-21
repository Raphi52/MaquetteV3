# Socle — les chaînes signées de la V3, ouvertes le jour du passage (SOC-018)

Lot 5 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Story **SOC-018** du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`. Décision de
départ : **Q6** du cadrage (chaque logiciel garde ses chaînes signées, la V3 ouvre les siennes au passage), contrainte
**C19** (7 chaînes, chaque signature reprend la précédente).

| Couche | Ce qui est fait | Où |
|---|---|---|
| Base (T01) | le catalogue des 59 codes d'événement ; les 7 chaînes ; les clés publiques ; l'état des chaînes ; l'archive fiscale par exercice ; l'inaltérabilité ; l'événement 10 des séries de numéros | `Base\migrations\SOC-018_chaines.sql` |
| Serveur (T02) | la signature chaînée (ECDSA P-256), le contrôle d'intégrité et de séquence (90, 95), les clôtures et leurs grands totaux, l'archive scellée, la purge, la signature de fond | `Serveur\Socle\Chaines\` |
| Écran (T03) | « Certification NF525 » : le journal filtrable, les grands totaux en lecture seule, le contrôle, la purge | `Pont\EcranChaines.cs`, `web\src\ecrans\EcranChaines.tsx` (+ test) |
| Reprise (T04) | l'ouverture des chaînes vides avec l'événement 260 ; rien lu ni écrit dans la V2 | `ServiceChaines.OuvrirAsync`, route `POST /socle/greffes/{greffe}/chaines/ouverture` |
| Test (T05) | un contrôle par critère, contre-épreuves | `tests\SocleChainesCheck` |
| Contrats | les types échangés et les adresses | `Contrats\Socle\Chaines.cs` |

**Preuve** : `.\tests.ps1 SocleChaines` rend 0 — 41 contrôles verts, au moins un par critère (CA01 à CA07), plus T01,
T02, l'événement des séries de la V3, l'écran et la copie anonymisée ; 3 lignes « RESTE » (fin de ce document). Durée
≈ 12 s. Contre-épreuves faites, chaque fichier remis à l'identique (comparaison octet à octet) :

| Défaut réinjecté | Le banc passe au rouge sur |
|---|---|
| le contrôle ne vérifie plus la signature (`ControleIntegrite.cs`, `&&` devenu `\|\|`) | CA04 : la facture modifiée en base n'est plus détectée, aucun 90 |
| la pré-image ne porte plus la signature du maillon précédent (`Signatures.cs`, `Canon`) | CA03 : la facture 2 vérifie « sans la précédente » |

Écran : `web\src\ecrans\EcranChaines.test.tsx` (8 tests). Captures sur le bureau caché, application de la copie de
travail, démonstration, thèmes clair et sombre :
`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\captures\SOC-018-1-journal-clair.png`,
`SOC-018-2-journal-sombre.png`, `SOC-018-3-grands-totaux-sombre.png`.

## Les sources de la V2 relues

Le code de la certification de la V2 n'est **pas** dans le projet de base (`drift-base-vs-ssdt.md` : « serveur-only,
délibéré ») ni dans la branche courante de `D:\RigApplication` : il est dans sa branche **`origin/Development`**
(commit `2f7d6c0` du 2026-11-13 pour le script SQL) :

| Source | Ce qui en est repris |
|---|---|
| `Source\Outils\DLL\ExpressoCertification\CertifExpressoApp48\SQL_Sources\SQLQuery_Deploy_Certification_FULL.sql` | les 59 codes de `CODE_JET` (lignes 129 à 187, copiées telles quelles), les pré-images de `SP_HashFactureR12`, `SP_HashEncaissement13`, `SP_HashDuplicata`, `SP_HashGrandTotaux_*`, les grands totaux de `SP_INSERT_Totaux_*_Periodiques` (GTP, GTM, GTA/E, cumuls perpétuels), `SP_VERIF_RUPTURE_SEQUENCE`, `ARCHIVE_FISCALE_EXERCICE` |
| `...\RigDatabase\dbo\Stored Procedures\SP_INSERT_JET.sql`, `SP_HashJETR19.sql`, `SP_HashJETR19_P.sql`, `Tables\JET_NP.sql` | le rangement d'un événement par son code, la chaîne purgeable qui repart à chaque exercice |
| `CertifExpressoApp48\SignatureEcdsa.cs`, `Codec.cs`, `CalculHash.cs`, `Docs\Note_Methodologie_Verif_Integrite_90.md` | ECDSA P-256 / SHA-256, préfixe `E1:` + Base64Url, clé CNG non exportable par greffe ; le contrôle 90 par recalcul |
| `Source\RIG\DLL\Processus\PROC_CERTIF` (FORM_CERTIF, onglets, purge) | l'écran de consultation |
| `tests\NumerotationV2Check\v2\TR_NUMERO_METIER_JET.rigdev.sql`, `fn_CertifNF525Active.rigdev.sql` (lot 7 de la phase 1) | l'événement 10 d'un changement de mode de série ; le réglage d'activation que la V3 n'a pas |

## Comment c'est fait

```
  un geste de la V3                     Serveur\Socle\Chaines\ServiceChaines            base V3 du greffe
  ─────────────────                     ────────────────────────────────────            ─────────────────
  facture, encaissement, duplicata ──►  SignerFactureAsync (dans SA transaction) ──►   soc.FactureSignee …  (Seq, Signature, IdCle)
  droits 130, reprise 210, livraison    écrivent l'événement SANS signature      ──►   soc.EvenementTechnique (SOC-005)
  250, série 10 (déclencheur SQL)       SignerEnAttenteAsync : à chaque geste             └ Chaine JNP / JP, Exercice, Seq, Signature
                                        des chaînes + SignatureDeFond (5 min)
  clôture jour / mois / exercice   ──►  CloturerAsync : grands totaux signés      ──►   soc.GrandTotalFactures / …Encaissements
                                        + archive (exercice) + événement 30            soc.ArchiveFiscale ─► événement 30 (JNP)
  contrôle                         ──►  ControleIntegrite : refait chaque          ──►   événements 90 (altération), 95 (rupture)
                                        signature avec la CLÉ PUBLIQUE en base
```

- **Les 7 chaînes** (C19) : `FAC` factures, `ENC` encaissements, `GTF` / `GTE` grands totaux, `DUP` duplicatas, `JNP`
  journal non purgeable, `JP` journal purgeable. Le journal est la table de SOC-005, `soc.EvenementTechnique`, posée
  « en attendant sa signature (SOC-018) » : elle reçoit ici sa signature, pas une copie. Le code d'un événement dit sa
  chaîne (`soc.CodeEvenement.Purgeable`, comme `SP_INSERT_JET`).
- **La signature** : ECDSA P-256 sur SHA-256, `E1:` + Base64Url, comme la V2. La pré-image de chaque maillon porte la
  chaîne, le rang, « O »/« N » et la signature du maillon présent juste en dessous, puis les champs de la pièce — ceux
  de la V2. Même fonction pour signer et vérifier (`Preimages`).
- **La clé** : un conteneur CNG non exportable par greffe, `RIGV3_NF525_P256_<greffe>`, dans le profil du compte de
  service de la partie serveur (`CoffreClesCng`). La clé **publique** est en base (`soc.CleSignature`) : la vérification
  ne demande pas la clé privée. Un changement de clé écrit l'événement 450.
- **L'ordre** : le rang d'un maillon vient de `soc.EtatChaine`, sous verrou applicatif, dans la transaction de la pièce.
  Une transaction annulée ne laisse aucun trou (banc : rang repris) ; huit signatures simultanées prennent huit rangs
  consécutifs. Le journal purgeable a une suite par exercice (`SP_HashJETR19_P`).
- **Inaltérable** (T01) : les déclencheurs refusent toute modification, tout effacement et toute insertion hors de la
  signature (`SESSION_CONTEXT('rigv3.signature')`) ; dans le journal, seule la signature d'un événement non signé
  passe, et seule la purge (`rigv3.purge-jet`) efface — du journal **purgeable**, d'un exercice **archivé**, dans la
  limite de ce que l'archive couvre. SOC-005 CA04 tient toujours (51010). C'est une garde contre l'erreur : une
  altération faite malgré elle est **détectée** par le contrôle.
- **Le contrôle** (CA04) : chaque maillon est vérifié ; une signature qui ne correspond plus est un **90** ; un trou, des
  premiers ou des derniers maillons disparus sont un **95** (un maillon dont le précédent manque est compté avec la
  rupture, pas comme une altération). Les archives : empreinte refaite, sceau, événement 30. Les événements sont écrits
  au journal non purgeable et signés. Sur une copie anonymisée (SOC-021, propriété `RIGV3_ANONYMISATION` que le lot 6
  pose sur toute copie, ou `anonymisation.Copie.ChainesInvalidees`), le contrôle n'écrit ni 90 ni 95 et son bilan dit
  « sans valeur probante » ; l'ouverture et la signature y restent possibles, pour répéter le passage (SOC-025).
- **Les clôtures** (CA05) : jour (`GTP`, aaaammjj), mois (`GTM`, aaaamm), exercice (`GTA/E`, aaaa), à l'heure du
  greffe (fuseau du catalogue, posé à l'ouverture). Factures et encaissements : le total de la période, le cumul
  perpétuel réel et absolu (le précédent du même type plus la période), et pour les factures le total par taux de TVA.
  Événement 50 (jour, mois) ou 60 (exercice). Une période se clôt une fois et pas avant sa fin.
- **L'archive fiscale** (CA05) : à la clôture de l'exercice, un document JSON de ses pièces, grands totaux et
  événements signés, avec les clés publiques ; son empreinte SHA-256 est écrite dans l'**événement 30 du journal non
  purgeable**, et son sceau signe l'empreinte, ce qu'elle couvre et cet événement. Le banc vérifie une facture à partir
  de l'archive seule.
- **La purge** (CA07) : le journal purgeable d'un exercice dont l'archive est faite ; l'événement 205 l'écrit.
- **L'ouverture** (CA01, T04) : au passage, `OuvrirAsync` (route `POST …/chaines/ouverture`, Amitel seul) refuse une base
  déjà ouverte ou déjà signée, pose la certification et l'état des chaînes, inscrit la clé, écrit l'**événement 260**
  « Initialisation des données » et le signe **en premier** maillon du journal non purgeable. Un événement écrit avant
  (livraison 250, reprise 210) est signé après lui. Rien ne se signe avant l'ouverture.
- **La V2** (CA02) : aucun code des chaînes n'ouvre la base V2 ni ne nomme une de ses tables signées (le banc le vérifie
  dans le code et par l'empreinte des chaînes d'une base V2, identique avant et après).
- **Pas d'interrupteur** (CA06) : la V3 n'a aucun réglage d'activation ; effacer l'ouverture est refusé par la base. Le
  banc signe avec une base V2 dont `fn_CertifNF525Active()` rend 0.
- **Les séries de numéros** (NUM-006 CA03) : un changement de mode d'une série de `num.Compteur` (format, codage de
  l'année, restitution) écrit l'événement **10** (déclencheur `num.TR_Compteur_ModeSequence`) ; l'avancement du
  chrono n'écrit rien. Pour FACV3 et BDFNI, que la V3 tient seule, c'est le seul journal où il s'écrit.

## Mode d'emploi

| Geste | Comment | Qui |
|---|---|---|
| Ouvrir les chaînes au passage | `POST /socle/greffes/{greffe}/chaines/ouverture`, ou `ServiceChaines.OuvrirAsync(cn, greffe, fuseau, auteur, trace)` — **avant** les reprises du passage | Amitel (outil du passage, lot 13) |
| Signer une facture, un encaissement, un duplicata | `ServiceChaines.SignerFactureAsync(cn, tx, new PieceFacture(…))` (… `Encaissement`, `Duplicata`) dans la transaction qui enregistre la pièce | le domaine Facturation |
| Écrire un événement NF525 | `ServiceChaines.EcrireEvenementAsync(cn, tx, code, detail, auteur, idAuteur)` ou un `INSERT` dans `soc.EvenementTechnique` ; la V3 le signe ensuite | tout programme |
| Lire l'état, le journal, les codes, les totaux | `GET /socle/greffes/{greffe}/chaines`, `…/journal?partie=np\|p&code=&du=&au=`, `…/codes`, `…/totaux?chaine=GTF\|GTE` | comptable du greffe, Amitel |
| Contrôler | `POST …/chaines/controle` | comptable, Amitel |
| Clore | `POST …/chaines/clotures` `{ "periode": "jour\|mois\|exercice", "date": "aaaa-mm-jj" }` | comptable, Amitel |
| Purger | `POST …/chaines/purge` `{ "exercice": 2025 }` | comptable, Amitel |

Réglage : `Chaines:SignatureDeFondSecondes` (300 par défaut, 0 l'arrête). Écran : menu **Facturer › Tenir les
comptes › Certification NF525** (`PROC_CERTIF`) ; Entrée, F5 et F7 dans un critère lancent la recherche, par la touche
commune `toucheDeRecherche` de `web\src\plan\clavier.ts` (`Pont\EcranCorrespondances.cs`).

## Écarts avec la V2, à valider (référent Facturation, co-valideur de la story)

| Écart | V2 | V3 | Pourquoi |
|---|---|---|---|
| Qui consulte | écrans `PROC_CERTIF` au seul rôle `ADMINAMITEL` (commentaire de `PROC_CERTIF.cs`) | les comptables du greffe (une fonction du domaine Comptabilité, `soc.Fonction.Comptabilite`) et Amitel | CA07 dit « un comptable » ; Q17 : le besoin métier fait la règle |
| Rang des maillons | `SEQUENCE` SQL : un trou « légitime » à chaque transaction annulée | compteur transactionnel : tout trou est une rupture | un 95 de la V2 était « à investiguer » ; ici il est certain |
| Codage de la pré-image | champs joints par des virgules | « longueur:valeur; » | une virgule dans un motif ou un libellé pouvait déplacer une frontière de champ sans changer la signature |
| Précédent d'un grand total | le précédent du même type | le maillon précédent de la table | une seule suite par chaîne, contrôlée comme les autres |
| Précédent d'un duplicata | la facture pour le premier, puis le duplicata précédent de la même facture | la signature de la facture **et** celle du duplicata précédent de la chaîne | les deux liens à la fois |
| Vérification | demande le conteneur de la clé privée du greffe | demande la clé publique en base | un auditeur vérifie sans accès au serveur |
| Séries sans greffe | ignorées par `TR_NUMERO_METIER_JET` | toutes tracées | une base V3 n'a qu'un greffe |
| Purge | les lignes affichées de `JET_P` | l'exercice archivé, dans la limite de son archive | la story : « seule la partie purgeable, après archivage » |
| Archive | fichier produit par `ArchiveFiscaleExercice.cs` | document JSON en base (`Fichier` = son nom d'export), scellé, lié à l'événement 30 | format à valider avec le certificateur (SOC-019) |
| Clôture quotidienne | job planifié à 22 h, un `.bat` par greffe, jamais rattrapé | un traitement de fond planifié, à l’heure du greffe, avec rattrapage (voir « Clôtures automatiques ») | une journée oubliée ne reste pas ouverte |
| Clôture de l’exercice | la V2 insère le total `GTA/E` le 31/12 au soir ; son archive est un geste à part (mode 11, exercice clos) | geste humain (la route) : le total et l’archive scellée vont ensemble | **à valider** : voir « Clôtures automatiques » |

## Ce qui reste, et pourquoi

| Reste | Raison | Qui |
|---|---|---|
| CA02 « elles restent vérifiables dans la V2 » | se prouve par `CertifExpressoApp` (mode 10) sur les vraies bases V2, avec leur clé : ni l'une ni l'autre sur ce poste. La V3 n'y touche pas (empreinte, banc) | exploitation, au passage |
| Ouvrir les chaînes des 40 greffes | l'outil du passage appelle l'ouverture, base par base | lot 13 (SOC-024) |
| Clôtures automatiques (jour, mois) | **faites** (lot 14) : voir « Clôtures automatiques » | — |
| Clôture automatique de l’exercice | à décider : la V2 le fait pour le total, pas pour l’archive | référent Facturation |
| Heure réelle de 22 h sur les 40 greffes, nuit réelle chez Amitel | le banc joue l’horloge par instants choisis ; la vraie nuit ne se prouve pas depuis ce poste | exploitation, au passage |
| Sauvegarde de la clé de chaque greffe | une clé CNG non exportable disparaît avec le serveur : les chaînes restent vérifiables (clé publique en base), la suite signe avec une nouvelle clé (événement 450). La politique (clé dans un HSM ou coffre) est une décision d'exploitation | Amitel, exploitation |
| Signer les vraies factures et encaissements | les tables du domaine Facturation n'existent pas encore ; elles appelleront `SignerFactureAsync` / `SignerEncaissementAsync` dans leur transaction | lots Facturation |
| La certification de la V3 (auditeur, attestation) | hors banc | SOC-019, lot 13 |

## Clôtures automatiques (SOC-018 CA05 T02, lot 14)

**La V2.** `Job_Exec_Hash_Totaux_FactEnc` (`CertifExpressoApp48Jobs`) : tous les jours à 22 h, `SP_INSERT_Totaux_Factures_Periodiques`
puis `..._Encaissements_Periodiques` (le jour, `GTP` ; le dernier jour du mois, `GTM` ; le 31/12, `GTA/E`), puis la signature (mode 2).
Un soir où le job ne tourne pas n’est jamais rattrapé. La section 6 du script de déploiement (`Job_Traitement_File`, toutes les
10 secondes) ne sert que la file `FILE_TRAITEMENT` des factures et encaissements.

**La V3.** Un seul planificateur pour toute la partie serveur (`ServeurSocleSupervisionPlanificateurNocturne.cs`), qui
**ne connaît rien des chaînes** : il interroge chaque tâche inscrite (`ITachePlanifiee`) pour chaque greffe du catalogue et dépose
ce qui est dû dans la file des traitements de fond (SOC-011). Il ne redépose ni ce qui attend déjà, ni ce qui a été tenté depuis
moins de la pause (30 min), et l’échec d’un greffe va au journal technique avec son code (une fois par pause). La clôture est la
tâche `ClotureNocturne` (traitement `chaines-clotures`, `ServeurSocleChainesClotureNocturne.cs`) :

- **dû** : dans un greffe dont les chaînes sont ouvertes, chaque jour depuis l’ouverture jusqu’au jour en cours si l’heure du
  greffe (fuseau de `soc.Certification`) a passé l’heure réglée, sinon jusqu’à la veille ; le mois, à la suite du jour qui le
  termine ; rien pour l’exercice ;
- **ordre** : par date, le jour avant le mois ; une période déjà close (par une personne, un autre serveur, un passage précédent) n’est plus due,
  et `CloturerAsync` refuse de toute façon la seconde (409) ;
- **rattrapage** : un jour oublié (serveur arrêté, greffe injoignable) reste dû et passe avant les suivants au passage suivant ;
- **panne** : une erreur dans un greffe arrête SES clôtures et va au journal technique (SOC-017) avec son code ; le traitement ne passe
  pas « en erreur » (il arrêterait les autres greffes) ; un greffe dont la base ne répond pas est de plus signalé par la supervision
  (alerte « base injoignable ») ;
- **copie anonymisée** (`RIGV3_ANONYMISATION` ou `anonymisation.Copie.ChainesInvalidees`) : jamais close automatiquement, même si
  l’élément était déjà en file ;
- **qui signe** : l’événement 50 porte l’auteur « Clôture automatique (partie serveur de la V3) ». Pas de ligne au journal des
  gestes : il est celui des agents (une personne, un identifiant) ; l’événement 50, signé, est la trace.

**Réglages** (configuration du serveur) : `Chaines:ClotureHeure` (« hh:mm », 22:00), `Planificateur:IntervalleSecondes` (60 ; 0 arrête le
planificateur), `Planificateur:PauseMinutes` (30).

**Reprise par un autre traitement de nuit (REC-006, lot 8).** Écrire une classe qui implémente `ITraitementDeFond` **et** `ITachePlanifiee`
(même nom), l’inscrire par son module (les deux `AddSingleton` ; le planificateur est déjà là, ne pas en inscrire un second). Modèle :
`ModuleChaines.Inscrire`.

**Écart à valider.** La V2 insère aussi le total d’exercice `GTA/E` le 31/12 à 22 h ; la V3 laisse la clôture de l’exercice à une personne :
elle produit l’archive fiscale scellée (empreinte au journal non purgeable) et ouvre la purge du journal, alors que la V2 ne produit son
archive que sur un exercice clos (mode 11). Si le référent Facturation veut le total automatique, il suffit d’ajouter `exercice` aux clôtures dues de
`ClotureNocturne.ClotureesDuesAsync` ; l’archive serait alors produite le soir du 31/12, avant les dernières pièces de la soirée.

## Fichiers

`Base\migrations\SOC-018_chaines.sql` · `Contrats\Socle\Chaines.cs` · `Serveur\Socle\Chaines\` (`ModuleChaines.cs`,
`ServiceChaines.cs`, `ServiceChaines.Clotures.cs`, `ControleIntegrite.cs`, `LectureChaines.cs`, `Signatures.cs`,
`SignatureDeFond.cs`, `ClotureNocturne.cs`) · `ServeurSocleSupervisionPlanificateurNocturne.cs` · `Pont\EcranChaines.cs` · `web\src\ecrans\EcranChaines.tsx` (+ `.test.tsx`) · `tests\SocleChainesCheck` ·
listes partagées : `Pont\MenuAccueil.cs`, `Pont\CatalogueEcrans.cs`, `web\src\plan\Aiguillage.tsx`,
`Pont\EcranCorrespondances.cs` (Entrée, F5 et F7 lancent la recherche), `tests\MenuGestesCheck\Program.cs` (`PROC_CERTIF` sans fiche).
