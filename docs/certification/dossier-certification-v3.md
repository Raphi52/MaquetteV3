# Dossier de certification NF525 de RIG V3 — à remettre à l'organisme (SOC-019)

Éditeur : **Amitel**. Logiciel : **RIG V3** (refonte de RIG, logiciel des greffes des tribunaux de commerce), version
**2026.1** (`Directory.Build.props`, une seule version pour l'écran, la partie serveur et les bases). Rédigé le
2026-11-21 (lot 13 du plan « Reconstruction de RIG — phase 2 »), à partir du code du dépôt `D:\RigV3Desktop` : chaque
affirmation renvoie au fichier qui la tient. Le banc `tests\SoclePassageCheck` vérifie à chaque passage que ce dossier
décrit les 7 chaînes du code et **chaque événement que la V3 écrit** : une chaîne ou un code ajouté au code sans être
décrit ici le fait passer au rouge.

| Pièce | Où |
|---|---|
| Ce dossier (description fonctionnelle et technique) | `docs\certification\dossier-certification-v3.md` |
| Description détaillée des chaînes, écarts avec la V2 | `docs\reconstruction\socle-chaines.md` |
| Schéma des tables signées et du journal | `Base\migrations\SOC-018_chaines.sql` |
| Code de la signature, du contrôle, des clôtures, de l'archive, de la purge | `Serveur\Socle\Chaines\` |
| Preuves rejouables (bancs, contre-épreuves) | `tests\SocleChainesCheck`, `tests\SoclePassageCheck` |

## Le certificat demandé, et ce qu'il couvre

- **Référentiel** : NF525 (inaltérabilité, sécurisation, conservation, archivage des données de règlement), au titre
  du 3° bis du I de l'article 286 du CGI. La RIG V3 émet des factures et enregistre des encaissements : elle y est
  soumise.
- **Organisme** : un organisme accrédité par le COFRAC. À ce jour, deux sont reconnus par l'administration : AFNOR
  Certification (référentiel NF525, secrétariat technique INFOCERT) et le LNE (référentiel de certification des
  systèmes de caisse). Le choix revient au responsable de la conformité d'Amitel.
- **Point de droit à confirmer** : le carnet (SOC-019) cite le BOFiP « seul le certificat d'un organisme accrédité est
  admis, l'attestation de l'éditeur ne l'est plus depuis le 16 février 2025 ». Depuis, l'**article 125 de la loi
  n° 2026-103 du 19 février 2026** (finances pour 2026) a **rétabli** l'attestation individuelle de l'éditeur, à partir
  du 21 février 2026 : le greffe peut justifier par l'un **ou** l'autre (BOFiP, actualité ACTU-2026-00073 du
  25 mars 2026). La story garde le certificat d'un organisme accrédité, plus exigeant et toujours valable ; c'est ce
  que l'outil de la bêta exige (`RigV3.Passage beta demarrer`, critère « SOC-019 CA01 »). **À valider** par le
  responsable de la conformité.
- **Périmètre** : la partie serveur de la V3 (une base V3 par greffe, Q4). **La V2 garde ses propres chaînes et sa
  propre certification** (décisions Q6 et Q15 du cadrage : chaque logiciel a la sienne ; la V2 facture à vie) : aucune
  ligne signée de la V2 n'est copiée, modifiée ni signée de nouveau par la V3 (SOC-019 CA05, SOC-018 CA02).
- **Version couverte** : le certificat couvre une version majeure (année.version, `2026.1`) ; une révision seule est
  une version mineure (événement 255), un changement d'année ou de version une version majeure (événement 250). Voir
  « Suivi de certification à chaque version ».

## Les 7 chaînes signées

Chaque chaîne est une suite de maillons ; chaque maillon reprend la signature de celui qui le précède. Elles naissent
vides le jour du passage du greffe à la V3 (ouverture, événement 260) ; rien n'est signé avant.

| Chaîne | Ce qu'elle tient | Table (base V3 du greffe) |
|---|---|---|
| `FAC` | les factures et avoirs : numéro, nature, date, montant TTC, client, ventilation par taux de TVA | `soc.FactureSignee` |
| `ENC` | les encaissements : numéro, date, montant | `soc.EncaissementSigne` |
| `GTF` | les grands totaux des factures : jour (GTP), mois (GTM), exercice (GTA/E), cumul perpétuel réel et absolu, total par taux | `soc.GrandTotalFactures` |
| `GTE` | les grands totaux des encaissements, mêmes périodes | `soc.GrandTotalEncaissements` |
| `DUP` | les duplicatas imprimés d'une facture déjà signée : date, motif, compte | `soc.DuplicataSigne` |
| `JNP` | le journal des événements **non purgeable** | `soc.EvenementTechnique` (Chaine = JNP) |
| `JP` | le journal des événements **purgeable**, une suite par exercice | `soc.EvenementTechnique` (Chaine = JP) |

L'ordre : le rang d'un maillon vient de `soc.EtatChaine`, pris sous verrou applicatif dans la **transaction de la
pièce** (`ServiceChaines.SignerFactureAsync`, `SignerEncaissementAsync`, `SignerDuplicataAsync`). Une transaction
annulée ne laisse aucun trou ; huit signatures simultanées prennent huit rangs consécutifs (banc). Tout trou est donc
une rupture (événement 95), et non un « trou légitime » comme avec la séquence SQL de la V2.

## La signature

- **Algorithme** : ECDSA P-256 sur SHA-256 ; signature notée `E1:` + Base64Url de (r‖s), comme la V2
  (`Serveur\Socle\Chaines\Signatures.cs`).
- **Pré-image** : la chaîne, le rang, « O »/« N », la signature du maillon précédent, puis les champs de la pièce
  (ceux de la V2 : `SP_HashFactureR12`, `SP_HashEncaissement13`, `SP_HashDuplicata`, `SP_HashGrandTotaux_*`). Chaque
  champ est codé « longueur:valeur; » : une virgule dans un libellé ne peut pas déplacer une frontière de champ
  (la V2 joignait les champs par des virgules). Même fonction pour signer et pour vérifier.
- **Clé** : une clé par greffe, dans un conteneur CNG **non exportable** du profil du compte de service de la partie
  serveur, nommé `RIGV3_NF525_P256_<greffe>` (`CoffreClesCng`). La **clé publique** est en base
  (`soc.CleSignature`, empreinte SHA-256) : un auditeur vérifie sans accès au serveur ni à la clé privée.
- **Changement de clé** : la suite signe avec la nouvelle clé, l'événement 450 le trace ; les maillons d'avant restent
  vérifiables par l'ancienne clé publique.
- **Pas d'interrupteur** : la V3 n'a aucun réglage d'activation de la certification ; effacer l'ouverture est refusé
  par la base (SOC-018 CA06).

## Le journal des événements

Le journal est `soc.EvenementTechnique` (SOC-005). N'importe quel programme y écrit un événement **sans** signature
(`ServiceChaines.EcrireEvenementAsync`, un `INSERT`, un déclencheur SQL) ; la V3 le signe dans l'ordre, à chaque geste
des chaînes et par le traitement de fond `SignatureDeFond` (toutes les 5 minutes, comme `Job_Exec_Hash_JET` de la V2).
Le code de l'événement dit sa chaîne (`soc.CodeEvenement.Purgeable`, les 59 codes de `CODE_JET` de la V2 repris tels
quels). Les événements que la V3 écrit elle-même :

| Code | Libellé (catalogue CODE_JET) | Chaîne | Qui l'écrit |
|---|---|---|---|
| 10 | Changement du mode de gestion de la séquence continue | `JNP` | déclencheur `num.TR_Compteur_ModeSequence` : un changement de format, de codage de l'année ou de restitution d'une série de numéros |
| 30 | Archivage fiscal d'année ou d'exercice | `JNP` | `CloturerAsync`, clôture d'exercice : porte l'empreinte SHA-256 de l'archive |
| 50 | Clôture de période (Jour/Mois) | `JP` | `CloturerAsync` (jour, mois), par une personne ou la clôture automatique |
| 60 | Clôture annuelle ou d'exercice | `JNP` | `CloturerAsync`, exercice |
| 90 | Détection d'un défaut d'intégrité dans les données sécurisées ou dans une archive fiscale | `JNP` | `ControleIntegrite` : une signature qui ne correspond plus |
| 95 | Détection d'une rupture de la séquence continue | `JNP` | `ControleIntegrite` : un trou, des premiers ou derniers maillons disparus |
| 155 | Duplicata d'une note | `JP` | `SignerDuplicataAsync` |
| 205 | Purge partielle du JET | `JNP` | `PurgerAsync` |
| 210 | Reprise de données d'autres logiciels suite à changement de systèmes | `JNP` | le transfert initial de la base V2 du greffe au passage (REC-001, `Serveur\Recopie\JournalTransfert.cs`) et la reprise des droits (SOC-005) |
| 250 | Installation d'une nouvelle version majeure du logiciel | `JNP` | la livraison d'une version à la base du greffe (`Outils\Livraison\Livreur.cs`, SOC-009 CA06) |
| 255 | Installation d'une nouvelle version mineure du logiciel | `JP` | la livraison, version mineure |
| 260 | Initialisation des données | `JNP` | `ServiceChaines.OuvrirAsync`, au passage du greffe : **premier maillon** du journal non purgeable |
| 450 | Traçabilité du changement du trousseau de clés utilisé pour signer | `JNP` | `ServiceChaines`, à l'inscription d'une nouvelle clé (après la première) |

Les autres codes du catalogue (droits 130, etc.) sont écrits par les domaines qui en répondent, et signés de même.

## Inaltérabilité et contrôle d'intégrité

- **Inaltérabilité** (`SOC-018_chaines.sql`) : les déclencheurs des tables signées refusent toute modification, tout
  effacement et toute insertion hors de la signature (contexte de session `rigv3.signature`). Dans le journal, seule la
  signature d'un événement non signé passe, et seule la purge (contexte `rigv3.purge-jet`) efface. C'est une garde
  contre l'erreur ; une altération faite malgré elle est **détectée** par le contrôle.
- **Contrôle** (`ControleIntegrite.cs`, écran « Certification NF525 », route `POST …/chaines/controle`) : chaque maillon
  est vérifié avec la clé publique ; une signature qui ne correspond plus écrit un **90**, un trou un **95** ; les
  archives sont vérifiées (empreinte refaite, sceau, événement 30). Sur une copie anonymisée d'une base (tests,
  répétitions), le contrôle n'écrit rien et dit « sans valeur probante ».

## Les clôtures

- **Périodes** : jour (`GTP`, aaaammjj), mois (`GTM`, aaaamm), exercice (`GTA/E`, aaaa), à **l'heure du greffe** (fuseau
  posé à l'ouverture : les greffes d'outre-mer ont le leur). Pour les factures et les encaissements : total de la
  période, cumul perpétuel réel et absolu ; pour les factures, total par taux de TVA. Une période se clôt une fois, et
  pas avant sa fin.
- **Automatiques** (`ClotureNocturne`, planificateur de la partie serveur) : chaque jour à 22 h heure du greffe
  (`Chaines:ClotureHeure`), le mois à la suite du jour qui le termine, l'exercice le 31/12 ; **un jour oublié**
  (serveur arrêté, base injoignable) reste dû et passe avant les suivants : rien n'est sauté, contrairement au job de la
  V2 qui n'est jamais rattrapé. Une copie anonymisée n'est jamais close automatiquement.
- **Événements** : 50 pour le jour et le mois, 60 pour l'exercice, signés.

## L'archivage fiscal

À la clôture de l'exercice, la V3 produit un **document JSON** de ses pièces, grands totaux et événements signés, avec
les clés publiques (`soc.ArchiveFiscale`) ; son empreinte SHA-256 est écrite dans l'**événement 30** du journal non
purgeable, et son **sceau** signe l'empreinte, ce qu'elle couvre et cet événement. Le banc vérifie une facture à partir
de l'archive seule. Le nom d'export du document est dans la colonne `Fichier`.

## La purge

Seul le journal **purgeable** (`JP`) d'un exercice **archivé** se purge, dans la limite de ce que l'archive couvre ; la
purge écrit l'événement 205. Le journal non purgeable, les pièces et les grands totaux ne se purgent jamais. La purge
est un geste humain (comptable du greffe ou Amitel), jamais automatique.

## Questions soumises à l'organisme

| # | Question | Ce que fait la V3 aujourd'hui | Si l'organisme refuse |
|---|---|---|---|
| 1 | **Archive fiscale scellée le 31/12 avec le total annuel** | la clôture automatique du 31/12 à 22 h écrit l'événement 60, le total `GTA/E` **et** l'archive scellée dans une même transaction (`CloturerAsync`) ; une pièce émise ce soir-là après 22 h n'est pas dans l'archive de l'exercice. La V2 produit son archive à part, sur un exercice clos (mode 11) | séparer l'archive dans `CloturerAsync` : le total `GTA/E` le 31/12, l'archive en un geste à part sur exercice clos (décision du chef du plan, lot 14) |
| 2 | Format de l'archive | document JSON en base, scellé, lié à l'événement 30 ; export par son nom de fichier | produire en plus le format demandé, depuis le même document |
| 3 | Rang des maillons sans trou | compteur transactionnel : tout trou est une rupture (95) | — |
| 4 | Codage de la pré-image « longueur:valeur; » au lieu de la virgule de la V2 | retenu pour qu'un libellé ne déplace pas une frontière de champ | revenir au codage de la V2 |
| 5 | Vérification par la clé publique en base | l'auditeur vérifie sans la clé privée | — |
| 6 | Deux logiciels pour un greffe pendant la transition | les affaires d'avant le passage restent facturées par la V2 (ses chaînes, sa certification), les nouvelles par la V3 (Q6, Q15, SOC-014) | — |

## Suivi de certification à chaque version (SOC-019 CA03)

- **Périmètre certifié** : les fichiers des fonctions décrites ici — `Serveur\Socle\Chaines\`,
  `Base\migrations\SOC-018*`, `Contrats\Socle\Chaines.cs`, `Pont\EcranChaines.cs`, `web\src\ecrans\EcranChaines.tsx`,
  et, quand ils existeront, ceux des domaines qui signent (`Serveur\Facturation\`, `Base\migrations\FAC-*`). La liste
  est `SuiviCertification.Perimetre` (`Outils\Passage\Beta.cs`) ; le banc vérifie qu'elle vise des fichiers réels.
- **Avant la livraison** d'une version : `RigV3.Passage certification suivi --version <v> --fichiers @changes.txt
  --registre suivi-certification.json` (la liste vient de `git diff --name-only <version livrée>..HEAD`). Si un fichier
  du périmètre a changé, la livraison est **refusée** tant que le registre ne porte pas l'accord de l'organisme pour
  cette version (organisme, date, référence de son avis).
- **À l'installation** : la livraison écrit l'événement **250** (majeure) ou **255** (mineure) dans chaque base
  (`Outils\Livraison\Livreur.cs`, SOC-009 CA06), signé ensuite comme tout événement.

## Le certificat chez le greffe (SOC-019 CA04)

Le certificat obtenu, avec sa date de validité, est remis à chaque greffe par Amitel et rangé dans le dossier de la
bêta (`certificat-v3.json` : organisme, accréditation, numéro, référentiel, version couverte, dates). Son affichage
dans la V3, pour qu'un greffe le retrouve sans passer par l'assistance, est un écran « à maquetter » qui n'est pas
construit (voir `docs\reconstruction\socle-exploitation.md`, « Ce qui ne se fait pas depuis ce poste »).
