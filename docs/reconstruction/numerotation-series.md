# Numérotation — la reprise des compteurs et les séries du greffe

Lot 6 du plan « Reconstruction de RIG — phase 1 » (plan `plan-da1bba7f`, fil chef `conv-2052`),
2026-11-21. Stories du carnet `C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\numerotation.json`,
construites sur le compteur central du lot 5 (`numerotation-compteur.md`) :

| Story | Ce qu'elle demande | Où c'est fait |
|---|---|---|
| NUM-005 | Reprendre chaque compteur de la V2 à sa valeur du jour du passage | `Base\migrations\NUM-005_reprise.sql` (`num.Reprendre`, `num.ReleverHeritage`, rapport, archive) ; `Serveur\Numerotation\Series\RepriseNumerotation.cs` |
| NUM-009 à NUM-020 | Continuer chaque série (142 types de RIG_DEV) en V2 comme en V3 | le compteur du lot 5, nourri par la reprise ; `num.Attribuer` redéfini (année future, fin de plage) ; `num.FormePartagee`, `num.ValeurHeritee`, `num.NumeroManquant`, `num.DeclarerManquant`, `num.CodeLiaison` ; `Serveur\Numerotation\Series\FinitionV2.cs`, `NumerosSpeciaux.cs` |
| NUM-021 | Confier à la V3 les numéros que la V2 calcule hors du compteur commun | relevé ci-dessous ; `num.OrdreFiche` + `num.AttribuerOrdre` ; compteur `BDFNI` |

**Preuve** : `.\tests.ps1 NumerotationSeries` rend 0 (≈ 30 s). Le banc lit **RIG_DEV en lecture seule**
(`SELECT` seul, garde-fou dans le banc), recopie ses tables de numérotation sur une base jetable locale
« V2 » avec le **vrai `GENERER_NUMERO` de RIG_DEV** (son texte, identique à la source `code_v2` au caractère
près — contrôlé), reprend cette base dans une base V3 jetable par `num.Reprendre`, puis, pour **chacun des
142 types**, compare le numéro suivant : `GENERER_NUMERO` dit ce que la V2 aurait donné, `num.Attribuer`
demandé par la V3 puis par la V2 doit donner la même chose, au même format. Résultat du 2026-11-21 :
**142 séries sur 142 continuent à l'identique** (2 écarts voulus, écrits ci-dessous), plus un contrôle par
critère CAxx : 186 lignes « OK » en tout (142 séries + 44 autres), 0 « ECHEC », 5 « RESTE » (voir la fin).

Contre-épreuves faites :
- un déclencheur du banc fausse un chrono pendant la copie → la reprise rend `-1`, 1 écart, **tout est
  annulé** (aucun compteur, aucun numéro rendu dans la V3, table V2 intacte) : c'est un contrôle du banc
  (NUM-005 CA02) ;
- la règle « rendu au-delà du compteur → journal seulement » retirée de la migration → 2 contrôles rouges
  (NUM-005 CA05, et NUM-011 CA01 JPC : la V3 redonnerait `2026RJ9999`, jamais donné), puis migration
  restaurée à l'identique.

## Sources V2 vérifiées

Lues le 2026-11-20 et le 2026-11-21 dans `D:\RigApplication\Source` et sur `RIG_DEV` (`SQL-DEV\DEV`, lecture seule) :

| Source | Ce qui en est repris |
|---|---|
| `BaseDeDonnées\RigDatabase\dbo\Stored Procedures\GENERER_NUMERO.sql` (= `OBJECT_DEFINITION` dans RIG_DEV) | l'oracle du banc, exécuté tel quel ; règles déjà portées par le lot 5 |
| `GENERER_NUMERO.sql:79-80` | une ligne du jour à chrono 0 ne compte pas comme compteur du jour : la reprise la traduit en `Chrono NULL` (le prochain est `Debut`) |
| `GENERER_NUMERO.sql:85-86, 151-152` | un numéro rendu n'est redonné que pour son jour (série « Q ») ou son année, et seulement si la série redonne |
| `RIG_DEV.dbo.NUMERO_METIER` | 26 553 lignes, 142 types, 1 365 lignes « attribution erronée » (= carnet) ; 3 clés en double ; 5 lignes d'années au-delà de 2027 |
| `RIG_DEV.dbo.NUMERO_METIER_SAVE_CDU` | 153 lignes, mêmes colonnes que `NUMERO_METIER` → `num.Archive` |
| `RIG_DEV.dbo.NUMERO_CHRONO_MANQUANT` | 7 numéros RC1 sans motif → journal « manquant » |
| `RIG_DEV.dbo.GREFFE_NUMERO_LIAISON` | 8 codes (C3801, C3802, G3801, G3802, M3801, M3802, X3801, X3802) → `num.CodeLiaison` |
| `Outils\DLL\RigMetier\Numerotation\Numerotation.cs:44-75` | la DLL appelle `GENERER_NUMERO` avec des entiers ; une étoile dans le numéro est remplacée par la clé de liaison |
| `Numerotation.cs:124-145` | lettres recommandées : `LRARN` / `LRN`, **année 2000** (file unique), clé, format national `CP NNN NNN NNNN C` |
| `Numerotation.cs:147-180` | clé des lettres recommandées (somme pondérée modulo 10) → `FinitionV2.CleLettreRecommandee` |
| `Numerotation.cs:439-537` | clé du numéro de liaison (lettre → chiffre, Corse A/B, Luhn) → `FinitionV2.CleLiaison` |
| `ActionRcs\PassageCfe.cs:161-172` | le numéro de liaison reçoit le code INSEE du greffe, jamais le `9999` par défaut |
| `RIG_DEV.dbo.PASSAGE_CFE`, `LETTRE_RECOMMANDEE` | 3 000 numéros réels chacun : la clé recalculée est la leur (0 fausse) |
| `RIG_DEV.dbo.INSTANCE`, `DOSSIER_RCS`, `FACTURE` | rôles en double (2004IP00330, 2009JC13469), gestions sans année (8 « AP… »), factures hors format (4 « ASTRA-AF… ») |
| `RIG_DEV.dbo.ARRIVEE` | A1428200001 et A1813700001 portés deux fois : donnés par les deux compteurs ARRD en double |
| `Edi\DLL\EdiMetier\EdiBanqueFrance\EdiBanqueFranceAvisJUDEdiSortant.cs:105-115` | « <année>F » + 5 chiffres = plus grand de l'année + 1 → compteur `BDFNI` |
| `_Dossier\DossierRCS.cs:2136-2143` | l'ordre des mentions repart à 1 à chaque enregistrement (fiche #2440) → `num.OrdreFiche` |
| `RCS\Exe\PROC_REQUI\frmREQUI.cs:3140-3145` | ordre des établissements NQ : `ISNULL(MAX, 1) + 1` → `num.OrdreFiche`, premier = 2 |

## La reprise (NUM-005)

### Le geste du passage

Les deux bases sont sur la même instance (Q4). Le passage d'une base (SOC-024, lot 7 pour les programmes) :

```sql
BEGIN TRANSACTION;
DECLARE @rc int, @id int, @msg nvarchar(400);
EXEC @rc = RIGV3_<GREFFE>.num.Reprendre @BaseV2 = N'RIG_<GREFFE>', @IdReprise = @id OUTPUT, @Message = @msg OUTPUT;
IF @rc <> 0 BEGIN ROLLBACK; RETURN; END;      -- -1 écart, -10 déjà reprise, -11 base V2 introuvable
-- lot 7 : remplacement de GENERER_NUMERO, CHERCHER_NUMERO, RESTITUER_NUMERO dans RIG_<GREFFE>, contrôles de bascule
COMMIT;                                          -- la table NUMERO_METIER de la V2 reste bloquée jusqu'ici
EXEC RIGV3_<GREFFE>.num.ReleverHeritage @BaseV2 = N'RIG_<GREFFE>', @IdReprise = @id;   -- en lecture, hors blocage
```

En C# : `RepriseNumerotation.ReprendreAsync(cn, tx, baseV2)` rend le rapport (`RapportReprise`, écarts compris) ;
`ReleverHeritageAsync(cn, null, baseV2, idReprise)`.

`num.Reprendre` travaille dans la transaction de l'appelant (point de sauvegarde) ou ouvre la sienne. Sa
première écriture est `SELECT … FROM <base V2>.dbo.NUMERO_METIER WITH (TABLOCKX, HOLDLOCK)` : jusqu'au
commit, `GENERER_NUMERO` et `RESTITUER_NUMERO` attendent (prouvé : `GENERER_NUMERO` d'une session V2 sort sur
l'erreur 1222 pendant la reprise, table inchangée). **Mesure** : 1,3 s de blocage pour les 26 553 lignes de
RIG_DEV, rapprochement compris (CA01 : moins d'une minute).

### Les règles de la copie

| Ligne de la V2 | Dans la V3 | Critère |
|---|---|---|
| ligne de compteur (`NMMTR_ATTRIBUTION_ERRONEE = 0`) | `num.Compteur`, clé (type, année, jour si le format porte « Q », sinon 0) ; tous les réglages ; chrono `0` → `NULL` | CA02 |
| deux lignes pour une même clé (RIG_DEV : ARRD 2014 j282 1196/1, ARRD 2018 j137 1403/1, JGOP 2004 j1 0/0) | le plus grand chrono ; l'autre ligne au journal (`reprise`) et dans `num.Signalement` ; `num.ReleverHeritage` liste les numéros de ce jour portés deux fois dans leur table (ARRIVEE : A1428200001, A1813700001) | CA03 |
| ligne d'une année au-delà de l'année suivante (RA1 2044, RA1 2188, JDCIA 2048 ×2, JDRFM 2109) | copiée telle quelle, chrono compris, signalée ; ensuite `num.Attribuer` refuse toute année au-delà de l'année suivante, code **-6** | CA04, T05 |
| ligne « attribution erronée » lisible, sous le compteur, série qui redonne (ou série par jour) | `num.NumeroRendu`, une seule fois par numéro, explication de la V2 ou « sans explication (reprise de la V2) » : 1 175 dans RIG_DEV, dont 457 sans explication | CA05 |
| ligne « attribution erronée » illisible (chrono 0, jour 0 ou au-delà de 366), sans compteur, au-delà du compteur, série qui ne redonne pas, ou en double | journal seulement (`reprise`, motif) : 190 dans RIG_DEV, dont 181 illisibles (le carnet en compte 178 « à 0 » ; 3 JGDJ 2004 ont un jour 602, 801, 900) | CA05 |
| `NUMERO_METIER_SAVE_CDU` | `num.Archive` (153 lignes), lecture seule (déclencheur, erreur 51002), jamais lue par `num.Attribuer` | CA06 |
| `NUMERO_CHRONO_MANQUANT` | journal, événement `manquant`, motif « manquant constaté par la V2, motif inconnu », jamais redonné | NUM-013 CA02 |
| `GREFFE_NUMERO_LIAISON` | `num.CodeLiaison` | NUM-019 T03 |
| `MENTION_OBSERVATION`, `ETABLISSEMENT` (NQ, non supprimés) | `num.OrdreFiche` : le plus grand ordre de chaque dossier (200 171 dossiers pour les mentions) | NUM-021 CA01 |
| `EDI_BDF` | compteur `BDFNI` de l'année, au plus grand « <année>F » de l'année (2026F00032 dans RIG_DEV) | NUM-021 CA04 |
| — | compteur `FACV3` de l'année : la série des factures de la V3, préfixe `V3-` (provisoire), départ 1 | NUM-020 CA02 |

La table de la V2 n'est jamais modifiée.

### Le rapprochement (CA02)

Relu dans les tables de la V3, avant le commit : pour chaque (type, année, jour) de la V2, un compteur au même
chrono (le plus grand), même format, même réglage (comparaison binaire) ; même nombre de lignes de compteur
(1 + doublons journalisés) et de lignes rendues (redonnables + journalisées) ; aucun compteur sans ligne V2 ;
totaux ; archive et manquants comptés. **Un écart : tout est annulé** (`-1`), la base V2 garde ses compteurs
et ses programmes, les écarts restent dans `num.RepriseEcart` (rapport `num.Reprise`, NUM-005 T03). Le banc
le vérifie aussi par un second calcul en C#, indépendant du SQL (25 185 clés, 0 différente).

## Les séries (NUM-009 à NUM-020)

Chaque série reste celle de la V2 : même compteur, même format, mêmes numéros rendus. La V2 y prendra ses
numéros par ses programmes remplacés (lot 7), la V3 par `DistributeurNumeros`. Les **142 types** de RIG_DEV
sont exactement ceux des 12 stories (contrôlé). Le tableau complet, série par série (format exact, valeur
reprise, numéros donnés), est à la fin de ce document.

| Story | Critère propre | Comment c'est tenu | Contrôle |
|---|---|---|---|
| NUM-009 | CA02 : JGDJ, JGJC, JGOP le même jour | chacun son compteur du jour ; un numéro = (série, valeur) dans le journal ; `num.FormePartagee` les liste | les trois à 1, 2 le même jour |
| NUM-009 | CA03 : rôles en double | `num.ValeurHeritee` (INSTANCE / NUM_ROLE : 2004IP00330, 2009JC13469), `num.EstHeritee` ; leur compteur est déjà au-delà | 1 pour un hérité, 0 pour un rôle neuf |
| NUM-010 | CA02 : JDETM et JDSAN écrivent « SANC… » | `num.FormePartagee` compte les numéros portés par les deux (2 en 2026 après le banc) ; `num.ReleverHeritage` les signale au passage | |
| NUM-011 | CA02, CA03 | PREV reprise telle quelle, JPRV pour les préventions ; un numéro TPG rendu (demande au Trésor annulée) est redonné | |
| NUM-012 | CA02, CA03 | AP… : valeurs héritées, aucun format RG* ne les lit ; aucun compteur ne vise le numéro RCS | |
| NUM-013 | CA02, CA03, CA04 | `num.NumeroManquant` (lit le journal) ; `num.DeclarerManquant` (motif obligatoire, `-4` si jamais donné) ; un numéro rendu sort de la liste | |
| NUM-016 | CA02, CA03 | ARRC → ARRD comme la V2 ; le jour change : compteur du jour créé une fois, départ à 1 | |
| NUM-017 | CA02 : fin de plage | `num.Compteur.FinPlage` (à saisir, donnée de La Poste) et `AlertePlage` (défaut : un dixième de la plage) ; `num.PlageAEpuiser` ; au-delà, `-5` « Plage épuisée » | |
| NUM-017 | CA03 : clé | `NumerosSpeciaux.LettreRecommandeeAsync` : année 2000, `FinitionV2.LettreRecommandee` | clé = celle de 3 000 lettres réelles |
| NUM-019 | CA02 : numéro de liaison | `NumerosSpeciaux.LiaisonAsync` : le code du compteur LIAI doit être un code de `num.CodeLiaison`, sinon refus ; clé de la V2 | clé = celle de 3 000 numéros « G » réels |
| NUM-020 | CA01 à CA05 | FACTU continue ; FACV3 ouverte au passage (`V3-26-000001`) ; les deux formats ne se lisent pas l'un l'autre ; restitution inactive (avoir = numéro suivant) ; ASTRA-AF… héritées | |

Côté V3, un acte qui numérote une lettre recommandée ou un passage au CFE appelle `NumerosSpeciaux` (même
transaction que l'acte) : le numéro brut du compteur reçoit la finition que la DLL de la V2 lui donnait.

## NUM-021 : les numéros calculés hors du compteur commun

Recherche du 2026-11-21 dans `D:\RigApplication\Source` (`MAX(…NUM|ORDRE|CHRONO…)` suivi de `+ 1`, code C#,
VB6 et SQL, hors migrations) ; chaque source est relue à sa ligne par le banc (CA02) :

| Numéro | Source | Partagé V2/V3 | Règle (CA03) |
|---|---|---|---|
| ordre des mentions d'un dossier (`MENTION_OBSERVATION.MNTOB_NUM_ORDRE`) | `Outils\DLL\RigMetier\_Dossier\DossierRCS.cs:2136-2143` | oui (dossier RCS, Q7) | `num.OrdreFiche` « MENTION_OBSERVATION », repris au plus grand ; `num.AttribuerOrdre` |
| ordre des établissements NQ (`ETABLISSEMENT.ETBLS_NUM_ORDRE`) | `RCS\Exe\PROC_REQUI\frmREQUI.cs:3140-3145` (et `RCS\Exe\REQUI\frmREQUI.cs:2995`) | oui | `num.OrdreFiche` « ETABLISSEMENT », premier = 2 comme la V2 |
| « <année>F » des non-inscrits (`EDI_BDF.EDIBDF_NUM_GESTION`) | `Edi\DLL\EdiMetier\EdiBanqueFrance\EdiBanqueFranceAvisJUDEdiSortant.cs:105-115` | oui | compteur `BDFNI` (V2 et V3) |
| numéro de transmission (`TRANSMISSION_JUD.TRJUD_NUM_TRANS`, base des transmissions) | `Edi\DLL\TransInfogreffeMetier\TransmissionJud.cs:28` | si la V3 transmet | compteur de la V3 dans la base des échanges : domaine Échanges |
| ordre des événements / actes d'un dépôt | `VB6\Bin Ult Cliente\CLI_VALLIN\frmVisuDepotActe.frm:5994, 7877, 8311` | non (traitement d'un seul client) | reste local ; usage à mesurer (Q11) |
| ordre de groupe d'une demande d'édition | `Judiciaire\Exe\PROC_INJONCTION_DCA\OLD\frmInjonctionsDCA.cs:588` | non (dossier OLD) | aucune : code hors service |
| `RIG_DEV.dbo.LOT_INPI_NUMEROS` (plages min/max par lot) | carnet | non (propre au lot) | reste local ; les lots eux-mêmes sont numérotés par LTIA/LTIC/LTIF |
| `EDI_GREFFE.dbo.NUM_TRANSMISSION` | carnet | à relever | **non lu** : « Cannot open database EDI_GREFFE » pour le compte de ce poste |

CA01 : 200 171 dossiers repris ; au dossier le plus rempli (281 mentions), une mention V3 puis une V2 reçoivent
282 et 283. CA04 : le premier avis de la V2 reçoit `2026F00033` (ce que la V2 aurait calculé), celui de la V3
`2026F00034`.

## Règles retenues et écarts avec la V2

Même lecture de la règle commune 6 que le lot 5 : un écart que la story a écrit en critère est codé selon le
critère ; un écart sans critère est codé comme la V2. Tous restent « à valider » par le référent nommé.

| Écart | V2 | V3 | Critère | Référent |
|---|---|---|---|---|
| Année au-delà de l'année suivante | acceptée (RA1 2188, JDRFM 2109…) | refusée, code `-6`, journalisée | NUM-005 CA04 | Socle |
| Compteurs en double pour une clé | une ligne prise au hasard du parcours | le plus grand chrono, l'autre journalisée | NUM-005 CA03 | Socle |
| Rendu au-delà du compteur (JPC 2026 : 9999 pour un compteur à 242) | redonné avant le suivant : la série saute | journal seulement ; la V3 donne 243 | NUM-004 CA04, NUM-005 CA05 | Socle |
| Fin de la plage de La Poste | aucune | `FinPlage`, alerte, refus `-5` au-delà | NUM-017 CA02 | Socle |
| Numéro de liaison sans code | `9999` par défaut de `GENERER_NUMERO` | refusé : un code de `num.CodeLiaison` est exigé | NUM-019 CA02 | RCS |
| Ordre des mentions | repart à 1 à chaque enregistrement (doublons, #2440 ; 2 468 paires en double dans RIG_DEV) | continue au plus grand du dossier | NUM-021 CA01 | RCS |
| Ordre des établissements NQ | `ISNULL(MAX, 1) + 1` : le premier vaut 2 | comme la V2 (premier = 2) | — | RCS |
| Non-inscrits BDF | plus grand de l'année + 1, calculé par chaque envoi | compteur `BDFNI`, mêmes numéros | NUM-021 CA04 | Échanges |
| Factures de la V3 | une seule série | série `FACV3`, préfixe **provisoire** `V3-` (paramètre de `num.Reprendre`, refusé s'il n'a que des chiffres) | NUM-020 CA02 | Facturation |
| Valeur de départ 0 (SIREN) | redonne `000000000` sans fin | `000000000` puis `000000001` (lot 5) | — | Socle |
| Lettres recommandées LRR, LRARR, LRI, LRARI, JLR, JLRAR | aucun appelant .NET (grep du 2026-11-21) ; demandées pour une année nouvelle, elles repartent au début de leur plage (numéros déjà utilisés en 2000 / 2008) | comme la V2 | — | Socle (à confirmer : séries mortes ?) |
| Numéros de liaison d'autres lettres (J, B, M, U) | leur clé ne suit pas `CleNumLiaison` (J : 34 091 sur 117 166 ; B : 139 sur 139) | jamais composés par la V3 | — | constat, sans effet |
| JDETM et JDSAN écrivent « SANC… » ; ECN et ENP « …E… » ; RC1/RC5/RC6 « F… » ; LTIA/LTIC/LTIF ; PREV/RGCH | mêmes numéros possibles | idem, signalé (`num.FormePartagee`, `num.Signalement`) ; un numéro = (série, valeur) | NUM-010 CA02, NUM-015 CA02… | Judiciaire, RCS |

## Pour le lot 7 (les programmes de la V2)

- `num.Attribuer` a deux refus de plus : **`-6`** (année au-delà de la suivante) et `-5` « plage épuisée »
  (`CodesNumerotation.AnneeHorsLimite`, `SeriePleine`). Même signature.
- La reprise garde une ligne de compteur ARRC 2001 sans quantième (série par jour) à `Jour = 0` : c'est la ligne
  de réglage qui porte la redirection vers ARRD ; elle ne sert jamais de compteur d'un jour.
- La reprise ajoute deux compteurs sans ligne V2 : `FACV3` et `BDFNI` de l'année du passage.
- `num.AttribuerOrdre @Fiche, @Cle, @Ordre OUTPUT, @Premier` remplace le calcul des mentions
  (`DossierRCS.cs:2136-2143`) et des établissements (`frmREQUI.cs:3140`, `@Premier = 2`) ; `num.Attribuer
  'BDFNI'` celui des non-inscrits (`EdiBanqueFranceAvisJUDEdiSortant.cs:105-115`). Ces trois appelants sont
  du code .NET / VB6 de la V2, hors de ce dépôt.

## Ce qui reste, et pourquoi

| Reste | Raison | Qui |
|---|---|---|
| NUM-005 CA07, T04, T08 : rejouer la reprise sur les 40 copies anonymisées, outre-mer compris, et le jour J | aucune copie anonymisée sur ce poste ; le banc la joue sur RIG_DEV | avant le jour J (SOC-025) |
| NUM-005 CA08 : la reprise inverse | story NUM-022 | plan suivant |
| NUM-005 CA01 : remplacer les trois programmes dans la même transaction | lot 7 (`Base\v2\bascule.sql`) ; `num.Reprendre` travaille déjà dans la transaction de l'appelant | lot 7 |
| Faire appeler `num.AttribuerOrdre` et `num.Attribuer 'BDFNI'` par la V2 (mentions, établissements, avis BDF) | code de la V2 (`D:\RigApplication`), hors de ce dépôt | lot 7 / passage |
| Écrans « à maquetter » : rapport de reprise (NUM-005), liste des numéros manquants (NUM-013 T04), saisie de la fin de plage (NUM-017 CA02) | hors de la liste de fichiers de ce lot (aucun `web\src\ecrans`) ; les vues et programmes sont prêts | phase des écrans |
| Vérification de saisie du numéro de gestion (NUM-012 CA02) et unicité du rôle avec exceptions (NUM-009 CA03) | domaines RCS et Judiciaire ; ils appellent `num.EstHeritee` | phases des domaines |
| NUM-020 CA06 : le numéro dans la facture électronique | SOC-020 | phase suivante |
| NUM-020 T04 : contrôle de continuité chaque nuit | exploitation ; le journal (`num.Journal`, série par série) en est la source | exploitation |
| NUM-021 : `EDI_GREFFE.dbo.NUM_TRANSMISSION`, base des transmissions | lecture refusée au compte de ce poste (mesuré le 2026-11-21) | avec un droit de lecture |
| Choix du préfixe des factures de la V3 | décision du référent Facturation (écart du carnet NUM-020) | référent |
| Chiffres de RIG_DEV sur les 40 bases (types, doublons, années futures, hors format) | mesure Q11 | exploitation |

## Travailler en développement

```powershell
docker compose up -d                       # le SQL Server du poste
.\tests.ps1 NumerotationSeries             # le banc (≈ 30 s ; lit RIG_DEV en lecture seule)
```

Il faut l'accès en lecture à `SQL-DEV\DEV` (compte Windows) ; un autre serveur V2 : variable `RIGV3_RIG_DEV`
(chaîne de connexion). Le banc écrit son relevé série par série dans
`tests\NumerotationSeriesCheck\binverif\Debug\net10.0-windows\releve-series.md`.

## Les 142 séries, une par une

Relevé du banc, 2026-11-21 (RIG_DEV lu le jour même ; le jour est celui du serveur SQL du poste, à l'heure UTC : 324 = le 20 novembre). « La V2
aurait donné » : les numéros que `GENERER_NUMERO` de RIG_DEV donne sur la copie, dans l'ordre ; la V3 donne
le premier à la V3, le second à la V2.

_Relevé du 2026-11-21 00:31, RIG_DEV (SQL-DEV\DEV / RIG_DEV), jour 324._

| Story | Type | Format (structure, annee, lettre, separateur, chiffres) | Repris de RIG_DEV (annee, jour : chrono) | GENERER_NUMERO de la V2 aurait donne | La V3 donne (V3 puis V2) |
|---|---|---|---|---|---|
| NUM-009 | 6 | AC, 4, «», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026000001, 2026000002 | 2026000001, 2026000002 |
| NUM-009 | JGAFF | AC, 4, «», «», 6 | 2026 : 4374 | 2026002017, 2026003946 | 2026002017, 2026003946 |
| NUM-009 | JGDJ | AQC, 2, «», «», 5 | 2026 j324 : pas de compteur (nouveau jour, depart 1) | 2632400001, 2632400002 | 2632400001, 2632400002 |
| NUM-009 | JGJC | AQC, 2, «», «», 5 | 2026 j324 : pas de compteur (nouveau jour, depart 1) | 2632400001, 2632400002 | 2632400001, 2632400002 |
| NUM-009 | JGOP | AQC, 2, «», «», 5 | 2026 j324 : pas de compteur (nouveau jour, depart 1) | 2632400001, 2632400002 | 2632400001, 2632400002 |
| NUM-009 | JRCT | ALC, 4, «J», «», 5 | 2026 : 98 | 2026J00098, 2026J00099 | 2026J00098, 2026J00099 |
| NUM-009 | JRDD | ALC, 4, «DD», «», 5 | 2026 : 2 | 2026DD00003, 2026DD00004 | 2026DD00003, 2026DD00004 |
| NUM-009 | JRDSJ | ALC, 4, «OR», «», 5 | 2026 : 69 | 2026OR00068, 2026OR00070 | 2026OR00068, 2026OR00070 |
| NUM-009 | JRIP | ALC, 4, «IP», «», 5 | 2026 : 306 | 2026IP00307, 2026IP00308 | 2026IP00307, 2026IP00308 |
| NUM-009 | JRJC | ALC, 4, «JC», «», 5 | 2026 : 1356 | 2026JC01272, 2026JC01284 | 2026JC01272, 2026JC01284 |
| NUM-009 | JRMC | ALC, 4, «LS», «», 4 | 2026 : 85 | 2026LS0086, 2026LS0087 | 2026LS0086, 2026LS0087 |
| NUM-009 | JRMDE | ALC, 4, «MD», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026MD00001, 2026MD00002 | 2026MD00001, 2026MD00002 |
| NUM-009 | JROP | ALC, 4, «OP», «», 5 | 2026 : 1721 | 2026OP00626, 2026OP01717 | 2026OP00626, 2026OP01717 |
| NUM-009 | JRORC | ALC, 4, «RC», «», 5 | 2026 : 24 | 2026RC00005, 2026RC00024 | 2026RC00005, 2026RC00024 |
| NUM-009 | JRPC | ALC, 4, «F», «», 5 | 2026 : 688 | 2026F00071, 2026F00078 | 2026F00071, 2026F00078 |
| NUM-009 | JRPR | ALC, 4, «PP», «», 4 | 2026 : 9 | 2026PP0009, 2026PP0010 | 2026PP0009, 2026PP0010 |
| NUM-009 | JRREF | ALC, 4, «R», «», 5 | 2026 : 110 | 2026R00111, 2026R00112 | 2026R00111, 2026R00112 |
| NUM-010 | JD13 | LASC, 4, «D13», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | D132026/00001, D132026/00002 | D132026/00001, D132026/00002 |
| NUM-010 | JD31 | LASC, 4, «D31», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | D312026/00001, D312026/00002 | D312026/00001, D312026/00002 |
| NUM-010 | JD40 | LASC, 4, «D40», «/», 5 | 2026 : 194 | D402026/00195, D402026/00196 | D402026/00195, D402026/00196 |
| NUM-010 | JD69 | LASC, 4, «D69», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | D692026/00001, D692026/00002 | D692026/00001, D692026/00002 |
| NUM-010 | JDCAS | LASC, 4, «CAS», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | CAS2026/00001, CAS2026/00002 | CAS2026/00001, CAS2026/00002 |
| NUM-010 | JDCCC | LASC, 4, «CCC», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | CCC2026/00001, CCC2026/00002 | CCC2026/00001, CCC2026/00002 |
| NUM-010 | JDCCO | LASC, 4, «CCO», «/», 5 | 2026 : 3 | CCO2026/00003, CCO2026/00004 | CCO2026/00003, CCO2026/00004 |
| NUM-010 | JDCIA | LASC, 4, «CIA», «/», 5 | 2026 : 387 | CIA2026/00388, CIA2026/00389 | CIA2026/00388, CIA2026/00389 |
| NUM-010 | JDCOL | LASC, 4, «COL», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | COL2026/00001, COL2026/00002 | COL2026/00001, COL2026/00002 |
| NUM-010 | JDCS | LASC, 4, «DCS», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | DCS2026/00001, DCS2026/00002 | DCS2026/00001, DCS2026/00002 |
| NUM-010 | JDDCP | LASC, 4, «DCP», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | DCP2026/00001, DCP2026/00002 | DCP2026/00001, DCP2026/00002 |
| NUM-010 | JDDIL | LASC, 4, «DIL», «/», 5 | 2026 : 1 | DIL2026/00002, DIL2026/00003 | DIL2026/00002, DIL2026/00003 |
| NUM-010 | JDDIV | LASC, 4, «DIV», «/», 5 | 2026 : 866 | DIV2026/00867, DIV2026/00868 | DIV2026/00867, DIV2026/00868 |
| NUM-010 | JDEC | LASC, 4, «DEC», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | DEC2026/00001, DEC2026/00002 | DEC2026/00001, DEC2026/00002 |
| NUM-010 | JDETA | LASC, 4, «ETA», «/», 5 | 2026 : 525 | ETA2026/00526, ETA2026/00527 | ETA2026/00526, ETA2026/00527 |
| NUM-010 | JDETM | LASC, 4, «SANC», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | SANC2026/00001, SANC2026/00002 | SANC2026/00001, SANC2026/00002 |
| NUM-010 | JDEX | LASC, 4, «DEX», «/», 5 | 2026 : 3 | DEX2026/00004, DEX2026/00005 | DEX2026/00004, DEX2026/00005 |
| NUM-010 | JDEXT | LASC, 4, «EXT», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | EXT2026/00001, EXT2026/00002 | EXT2026/00001, EXT2026/00002 |
| NUM-010 | JDHUI | LASC, 4, «HUI», «/», 5 | 2026 : 114 | HUI2026/00115, HUI2026/00116 | HUI2026/00115, HUI2026/00116 |
| NUM-010 | JDIMP | LASC, 4, «IMP», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | IMP2026/00001, IMP2026/00002 | IMP2026/00001, IMP2026/00002 |
| NUM-010 | JDINV | LASC, 4, «INV», «/», 5 | 2026 : 137 | INV2026/00138, INV2026/00139 | INV2026/00138, INV2026/00139 |
| NUM-010 | JDOCP | LASC, 4, «OCP», «/», 5 | 2026 : 203 | OCP2026/00204, OCP2026/00205 | OCP2026/00204, OCP2026/00205 |
| NUM-010 | JDOFF | LASC, 4, «OFF», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | OFF2026/00001, OFF2026/00002 | OFF2026/00001, OFF2026/00002 |
| NUM-010 | JDPLA | LASC, 4, «PLA», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | PLA2026/00001, PLA2026/00002 | PLA2026/00001, PLA2026/00002 |
| NUM-010 | JDPLI | LASC, 4, «PLI», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | PLI2026/00001, PLI2026/00002 | PLI2026/00001, PLI2026/00002 |
| NUM-010 | JDPVC | LASC, 4, «PVC», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | PVC2026/00001, PVC2026/00002 | PVC2026/00001, PVC2026/00002 |
| NUM-010 | JDPVR | LASC, 4, «PVR», «/», 5 | 2026 : 99 | PVR2026/00100, PVR2026/00101 | PVR2026/00100, PVR2026/00101 |
| NUM-010 | JDRAN | LASC, 4, «RAN», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | RAN2026/00001, RAN2026/00002 | RAN2026/00001, RAN2026/00002 |
| NUM-010 | JDRAP | LASC, 4, «RAP», «/», 5 | 2026 : 231 | RAP2026/00232, RAP2026/00233 | RAP2026/00232, RAP2026/00233 |
| NUM-010 | JDRCP | LASC, 4, «RCP», «/», 5 | 2026 : 36 | RCP2026/00037, RCP2026/00038 | RCP2026/00037, RCP2026/00038 |
| NUM-010 | JDRED | LASC, 4, «RED», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | RED2026/00001, RED2026/00002 | RED2026/00001, RED2026/00002 |
| NUM-010 | JDREP | LASC, 4, «REP», «/», 5 | 2026 : 4 | REP2026/00005, REP2026/00006 | REP2026/00005, REP2026/00006 |
| NUM-010 | JDRFM | LASC, 4, «FMS», «/», 5 | 2026 : 164 | FMS2026/00162, FMS2026/00163 | FMS2026/00162, FMS2026/00163 |
| NUM-010 | JDRIP | LASC, 4, «RIP», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | RIP2026/00001, RIP2026/00002 | RIP2026/00001, RIP2026/00002 |
| NUM-010 | JDSAI | LASC, 4, «SAI», «/», 5 | 2026 : 339 | SAI2026/00340, SAI2026/00341 | SAI2026/00340, SAI2026/00341 |
| NUM-010 | JDSAN | LASC, 4, «SANC», «/», 5 | 2026 : 14 | SANC2026/00015, SANC2026/00016 | SANC2026/00015, SANC2026/00016 |
| NUM-011 | JMEXP | ALC, 4, «E», «», 5 | 2026 : 2 | 2026E00003, 2026E00004 | 2026E00003, 2026E00004 |
| NUM-011 | JPC | ALC, 4, «RJ», «», 4 | 2026 : 242 | 2026RJ9999, 2026RJ0243, 2026RJ0244 | 2026RJ0243, 2026RJ0244 (ecart voulu NUM-004 CA04 : la V2 aurait d'abord redonne 2026RJ9999, jamais donne) |
| NUM-011 | JPCEX | ALC, 4, «EX», «», 4 | 2026 : 8 | 2026EX0009, 2026EX0010 | 2026EX0009, 2026EX0010 |
| NUM-011 | JPRV | ALC, 4, «PR», «», 4 | 2026 : 56 | 2026PR0057, 2026PR0058 | 2026PR0057, 2026PR0058 |
| NUM-011 | OTIJ | ALC, 4, «OTJ», «», 4 | 2026 : 7 | 2026OTJ0008, 2026OTJ0009 | 2026OTJ0008, 2026OTJ0009 |
| NUM-011 | PREV | ASC, 4, «», «/», 4 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026/0001, 2026/0002 | 2026/0001, 2026/0002 |
| NUM-011 | TPG | ALC, 4, «TPG», «», 4 | 2026 : 102 | 2026TPG0102, 2026TPG0103 | 2026TPG0102, 2026TPG0103 |
| NUM-012 | RGA | ALC, 4, «A», «», 5 | 2026 : 932 | 2026A00933, 2026A00934 | 2026A00933, 2026A00934 |
| NUM-012 | RGAC | ALC, 4, «AC», «», 4 | 2026 : 98 | 2026AC0099, 2026AC0100 | 2026AC0099, 2026AC0100 |
| NUM-012 | RGB | ALC, 4, «B», «», 5 | 2026 : 890 | 2026B00891, 2026B00892 | 2026B00891, 2026B00892 |
| NUM-012 | RGC | ALC, 4, «C», «», 5 | 2026 : 2 | 2026C00003, 2026C00004 | 2026C00003, 2026C00004 |
| NUM-012 | RGD | ALC, 4, «D», «», 5 | 2026 : 324 | 2026D00325, 2026D00326 | 2026D00325, 2026D00326 |
| NUM-012 | RGI | ALC, 4, «I», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026I00001, 2026I00002 | 2026I00001, 2026I00002 |
| NUM-012 | RGNI | ALC, 4, «NI», «», 5 | 2026 : 262 | 2026NI00263, 2026NI00264 | 2026NI00263, 2026NI00264 |
| NUM-013 | RC1 | LASC, 2, «F», «/», 6 | 2026 : 9935 | F26/009936, F26/009937 | F26/009936, F26/009937 |
| NUM-013 | RC5 | LASC, 2, «F», «/», 6 | 2026 : 3 | F26/000004, F26/000005 | F26/000004, F26/000005 |
| NUM-013 | RC6 | LASC, 2, «F», «/», 6 | 2026 : 179 | F26/000180, F26/000181 | F26/000180, F26/000181 |
| NUM-013 | RGCH | ASC, 4, «», «/», 4 | 2026 : 56 | 2026/0057, 2026/0058 | 2026/0057, 2026/0058 |
| NUM-014 | RA4 | LASC, 4, «P», «/», 6 | 2026 : 12 | P2026/000013, P2026/000014 | P2026/000013, P2026/000014 |
| NUM-014 | RD1 | LASC, 4, «A», «/», 6 | 2026 : 4030 | A2026/004031, A2026/004032 | A2026/004031, A2026/004032 |
| NUM-014 | RD2 | LASC, 4, «D», «/», 6 | 2026 : 85 | D2026/000086, D2026/000087 | D2026/000086, D2026/000087 |
| NUM-014 | RD3 | LASC, 4, «F», «/», 6 | 2026 : 3 | F2026/000004, F2026/000005 | F2026/000004, F2026/000005 |
| NUM-014 | RD4 | LASC, 4, «E», «/», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | E2026/000001, E2026/000002 | E2026/000001, E2026/000002 |
| NUM-014 | RD5 | LASC, 4, «K», «/», 6 | 2026 : 2 | K2026/000003, K2026/000004 | K2026/000003, K2026/000004 |
| NUM-014 | RD6 | LASC, 4, «G», «/», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | G2026/000001, G2026/000002 | G2026/000001, G2026/000002 |
| NUM-014 | RD7 | LASC, 4, «J», «/», 6 | 2026 : 1 | J2026/000002, J2026/000003 | J2026/000002, J2026/000003 |
| NUM-014 | RDB | LASC, 4, «N», «/», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | N2026/000001, N2026/000002 | N2026/000001, N2026/000002 |
| NUM-014 | RDC | LASC, 4, «B», «/», 6 | 2026 : 4538 | B2026/004539, B2026/004540 | B2026/004539, B2026/004540 |
| NUM-015 | EAG | ALC, 4, «B», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026B000001, 2026B000002 | 2026B000001, 2026B000002 |
| NUM-015 | EAN | ALC, 4, «M», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026M000001, 2026M000002 | 2026M000001, 2026M000002 |
| NUM-015 | ECB | ALC, 4, «C», «», 6 | 2026 : 815 | 2026C000816, 2026C000817 | 2026C000816, 2026C000817 |
| NUM-015 | ECN | ALC, 4, «E», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026E000001, 2026E000002 | 2026E000001, 2026E000002 |
| NUM-015 | EDC | ALC, 4, «D», «», 6 | 2026 : 1 | 2026D000002, 2026D000003 | 2026D000002, 2026D000003 |
| NUM-015 | EFA | ALC, 4, «A», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026A000001, 2026A000002 | 2026A000001, 2026A000002 |
| NUM-015 | EFC | ALC, 4, «N», «», 6 | 2026 : 222 | 2026N000223, 2026N000224 | 2026N000223, 2026N000224 |
| NUM-015 | EGD | ALC, 4, «G», «», 6 | 2026 : 30 | 2026G000031, 2026G000032 | 2026G000031, 2026G000032 |
| NUM-015 | EGS | ALC, 4, «K», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026K000001, 2026K000002 | 2026K000001, 2026K000002 |
| NUM-015 | EHF | ALC, 4, «H», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026H000001, 2026H000002 | 2026H000001, 2026H000002 |
| NUM-015 | EHM | ALC, 4, «Q», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026Q000001, 2026Q000002 | 2026Q000001, 2026Q000002 |
| NUM-015 | EIN | ALC, 4, «I», «», 6 | 2026 : 14 | 2026I000015, 2026I000016 | 2026I000015, 2026I000016 |
| NUM-015 | EIS | ALC, 4, «U», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026U000001, 2026U000002 | 2026U000001, 2026U000002 |
| NUM-015 | EJF | ALC, 4, «JF», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026JF00001, 2026JF00002 | 2026JF00001, 2026JF00002 |
| NUM-015 | ELM | ALC, 4, «L», «», 6 | 2026 : 155 | 2026L000156, 2026L000157 | 2026L000156, 2026L000157 |
| NUM-015 | EMO | ALC, 4, «O», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026O000001, 2026O000002 | 2026O000001, 2026O000002 |
| NUM-015 | ENJ | ALC, 4, «J», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026J000001, 2026J000002 | 2026J000001, 2026J000002 |
| NUM-015 | ENJSC | ALC, 4, «Z», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026Z000001, 2026Z000002 | 2026Z000001, 2026Z000002 |
| NUM-015 | ENP | ALC, 4, «E», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026E000001, 2026E000002 | 2026E000001, 2026E000002 |
| NUM-015 | ENPS | ALC, 4, «NP», «», 5 | 2026 : 32 | 2026NP00033, 2026NP00034 | 2026NP00033, 2026NP00034 |
| NUM-015 | EPD | ALC, 4, «F», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026F000001, 2026F000002 | 2026F000001, 2026F000002 |
| NUM-015 | EPE | ALC, 4, «P», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026P000001, 2026P000002 | 2026P000001, 2026P000002 |
| NUM-015 | EPT | ALC, 4, «T», «», 6 | 2026 : 6 | 2026T000007, 2026T000008 | 2026T000007, 2026T000008 |
| NUM-015 | EPX | ALC, 4, «X», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026X000001, 2026X000002 | 2026X000001, 2026X000002 |
| NUM-015 | ERP | ALC, 4, «R», «», 6 | 2026 : 23 | 2026R000024, 2026R000025 | 2026R000024, 2026R000025 |
| NUM-015 | ESC | ALC, 4, «Y», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026Y000001, 2026Y000002 | 2026Y000001, 2026Y000002 |
| NUM-015 | ESF | ALC, 4, «SF», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026SF00001, 2026SF00002 | 2026SF00001, 2026SF00002 |
| NUM-015 | ESM | ALC, 4, «SM», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026SM00001, 2026SM00002 | 2026SM00001, 2026SM00002 |
| NUM-015 | ESP | ALC, 4, «SP», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026SP00001, 2026SP00002 | 2026SP00001, 2026SP00002 |
| NUM-015 | ESS | ALC, 4, «S», «», 6 | 2026 : 16 | 2026S000017, 2026S000018 | 2026S000017, 2026S000018 |
| NUM-015 | EVF | ALC, 4, «V», «», 6 | 2026 : 8 | 2026V000009, 2026V000010 | 2026V000009, 2026V000010 |
| NUM-015 | EWA | ALC, 4, «WA», «», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026WA00001, 2026WA00002 | 2026WA00001, 2026WA00002 |
| NUM-015 | EWW | ALC, 4, «W», «», 6 | 2026 : pas de compteur (nouvelle annee, depart 1) | 2026W000001, 2026W000002 | 2026W000001, 2026W000002 |
| NUM-016 | ARRC → ARRD | LAQC, 2, «A», «», 5 | 2026 j324 : 115 | A2632400116, A2632400117 | A2632400116, A2632400117 |
| NUM-016 | ARRD | LAQC, 2, «A», «», 5 | 2026 j324 : 115 | A2632400118, A2632400119 | A2632400118, A2632400119 |
| NUM-016 | ARRHF | LAC, 2, «HF», «», 7 | 2026 : pas de compteur (nouvelle annee, depart 1) | HF260000001, HF260000002 | HF260000001, HF260000002 |
| NUM-016 | ARRNQ | LAC, 2, «NQ», «», 7 | 2026 : pas de compteur (nouvelle annee, depart 1) | NQ260000001, NQ260000002 | NQ260000001, NQ260000002 |
| NUM-016 | ARRR | LAC, 2, «RE», «», 7 | 2026 : pas de compteur (nouvelle annee, depart 1) | RE260000001, RE260000002 | RE260000001, RE260000002 |
| NUM-016 | DMD | LAQC, 2, «D», «», 5 | 2026 j324 : 122 | D2632400123, D2632400124 | D2632400123, D2632400124 |
| NUM-016 | EAR | ALC, 2, «e», «», 7 | 2026 : 1985 | 26e0001986, 26e0001987 | 26e0001986, 26e0001987 |
| NUM-016 | HF | LAC, 1, «HF», «», 7 | 2026 : pas de compteur (nouvelle annee, depart 1) | HF60000001, HF60000002 | HF60000001, HF60000002 |
| NUM-016 | RA1 | ALC, 2, «r», «», 6 | 2026 : 19637 | 26r019638, 26r019639 | 26r019638, 26r019639 |
| NUM-017 | JLR | LC, 4, «RB», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 1) | RB00000001, RB00000002 | RB00000001, RB00000002 |
| NUM-017 | JLRAR | LC, 4, «RA», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 1) | RA00000001, RA00000002 | RA00000001, RA00000002 |
| NUM-017 | LRARI | LC, 4, «RW», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 28676980) | RW28676980, RW28676981 | RW28676980, RW28676981 |
| NUM-017 | LRARN | LC, 4, «2C», «», 10 | 2000 : 347399241 | 2C0347399242, 2C0347399243 | 2C0347399242, 2C0347399243 |
| NUM-017 | LRARR | LC, 4, «RA», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 15000000) | RA15000000, RA15000001 | RA15000000, RA15000001 |
| NUM-017 | LRI | LC, 4, «RZ», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 26476001) | RZ26476001, RZ26476002 | RZ26476001, RZ26476002 |
| NUM-017 | LRN | LC, 4, «2D», «», 10 | 2000 : 111014305 | 2D0111014306, 2D0111014307 | 2D0111014306, 2D0111014307 |
| NUM-017 | LRR | LC, 4, «RB», «», 8 | 2026 : pas de compteur (nouvelle annee, depart 15000000) | RB15000000, RB15000001 | RB15000000, RB15000001 |
| NUM-018 | LTIA | C, 4, «ACT», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 00001, 00002 | 00001, 00002 |
| NUM-018 | LTIC | C, 4, «CPT», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 00001, 00002 | 00001, 00002 |
| NUM-018 | LTIF | C, 4, «IMR», «/», 5 | 2026 : pas de compteur (nouvelle annee, depart 1) | 00001, 00002 | 00001, 00002 |
| NUM-018 | RAP | ALC, 2, «AP», «», 5 | 2026 : 43 | 26AP00044, 26AP00045 | 26AP00044, 26AP00045 |
| NUM-018 | RBOE | ALSC, 2, «BB», «/», 4 | 2026 : 321 | 26BB/0322, 26BB/0323 | 26BB/0322, 26BB/0323 |
| NUM-019 | IBREQ | LASC, 4, «BR», «/», 4 | 2026 : pas de compteur (nouvelle annee, depart 1) | BR2026/0001, BR2026/0002 | BR2026/0001, BR2026/0002 |
| NUM-019 | LIAI | LGACK, 1, «G», «», 5 | 2026 : 1782 | G3802601783*, G3802601784* | G3802601783*, G3802601784* |
| NUM-019 | PARA | AC, 2, «», «», 5 | 2026 : 436 | 2600437, 2600438 | 2600437, 2600438 |
| NUM-019 | RCJN | ALC, 4, «CJ», «», 5 | 2026 : 2699 | 2026CJ02700, 2026CJ02701 | 2026CJ02700, 2026CJ02701 |
| NUM-019 | SIREN | C, 0, «», «», 9 | 2026 : pas de compteur (nouvelle annee, depart 0) | 000000000, 000000000 | 000000000, 000000001 (ecart voulu (lot 5) : la V2 redonne 000000000 deux fois, la V3 donne 000000001) |
| NUM-020 | FACTU | A-C, 2, «», «», 6 | 2026 : 22768 | 26-022769, 26-022770 | 26-022769, 26-022770 |
