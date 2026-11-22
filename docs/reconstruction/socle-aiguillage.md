# Socle — chaque affaire au bon logiciel, et l'ancien RIG depuis la V3

Lot 3 du plan « Reconstruction de RIG — phase 2 » (plan `plan-a85ebf33`, fil chef `conv-2052`), 2026-11-21.
Stories **SOC-014** (envoyer chaque affaire au bon logiciel selon sa date de création) et **SOC-015** (ouvrir
une ancienne affaire dans la V2 depuis la V3) du carnet
`C:\Users\raphael.vilain\Documents\Autowin\refonte-rig\carnet\socle.json`. Banc : `.\tests.ps1 SocleAiguillage`.

## La règle (SOC-014 CA06)

Une affaire se traite dans la **V3** si et seulement si elle est créée **à l'heure de passage de son greffe ou
après** ; sinon, et tant que le passage n'est pas fixé, dans la **V2**. Rien d'autre : ni domaine, ni nature,
ni entreprise (Q18). La date de passage est celle du catalogue commun (`soc.Greffe.PassageLocal`, SOC-007,
heure **locale** du greffe) ; la borne est celle de `FicheGreffe.PassageFait` (heure atteinte = passage fait).

| Cas | Date comparée | Exemple (passage le 01/08/2026 08:00) |
|---|---|---|
| affaire existante (CA02) | sa date de création (`AFFAIRE.AFFRE_DATE_CREATION`) | 30/07 → V2 ; 01/08 07:59 → V2 ; 01/08 08:00 → V3 |
| nouvelle affaire (CA01) | maintenant, **à l'heure du greffe** | après le passage → V3 ; greffe au passage à venir ou sans date → V2 |
| nouvelle formalité sur une entreprise déjà immatriculée (CA03) | maintenant (c'est une nouvelle affaire) | V3, même si l'affaire d'immatriculation de 2019 reste V2 |
| arrivée (échange partenaire, dépôt) (CA05) | celle de l'affaire visée ; aucune visée = nouvelle | affaire de juillet → V2 ; sans affaire → V3 |
| greffe bêta **revenu à l'ancien RIG** (REC-007 T02, lot 11) | aucune : l'état du retour, lu dans sa base V3 (`rec.EtatRetour`) | toute affaire → V2 (la date de passage, atteinte, ne change plus) |

La règle est écrite **une fois**, dans `Contrats\Socle\Aiguillage.cs` (`RegleAiguillage.Aiguiller`) : le serveur
la joue avec la date du catalogue, la démonstration de l'écran (sans serveur) avec un passage fixé au
01/08/2026 08:00 (`AncienRig.PassageDemo`). Ils ne peuvent pas juger différemment.

## La route (SOC-014 T01)

`POST /socle/greffes/{code}/aiguillage` (`RoutesAiguillage.Aiguiller`, `Serveur\Socle\Aiguillage\ModuleAiguillage.cs`)
— corps `QuestionAiguillage { affaires: [{ numero, creeeLe | null }] }`, réponse `ReponseAiguillage`
(`passageLocal`, `maintenantLocal`, et pour chaque affaire `logiciel` « V2 »/« V3 » et `motif` en clair).

- Une question porte plusieurs affaires (une page de recherche pose **une** question), 1 000 au plus (400 au-delà).
- `creeeLe: null` = aucune affaire visée : une nouvelle affaire, datée de **maintenant à l'heure du greffe**
  (`Fuseaux.MaintenantLocal`). Un greffe d'outre-mer passe à son heure, pas à celle du serveur.
- Lecture seule : seule la fiche du catalogue est lue (base commune), aucune base de greffe ; rien n'est écrit,
  donc pas de journal des gestes.
- Accès : `PolitiquesSocle.AgentAdmis`, agent jugé dans la base commune comme pour la fiche du greffe — 401 sans
  session, 403 compte refusé, 404 greffe hors catalogue.

## L'écran (SOC-014 T04, SOC-015 T01–T03)

Dans la **recherche d'affaires** (`web\src\ecrans\EcranRechAffaires.tsx`, pont `Pont\AncienRig.cs`) :

- **CA01** — une affaire créée avant le passage porte la pastille « traitée dans la V2 » (le motif en info-bulle).
  L'écran demande l'aiguillage quand la *liste* des numéros change (pas à chaque tri). S'il ne répond pas (403,
  greffe inconnu, aucun greffe ouvert), aucune pastille : l'écran reste celui d'avant ce lot.
- **CA02** — l'ancienne affaire choisie montre un bloc « Ancien RIG » et le bouton **« Ouvrir dans l'ancien RIG »**.
  Pour une affaire de la V3, ni bloc ni bouton : la démonstration est inchangée.
- **CA04** — « Supprimer l'instance » (XXAFF) est refusé sur une ancienne affaire, avec le renvoi vers l'ancien RIG ;
  sur une affaire de la V3, la suppression reste simulée comme avant.
- Le double-clic ouvre toujours l'affaire dans la visionneuse : la consulter dans la V3 reste permis.

### Comment la V3 ouvre la V2 (SOC-015 CA02, CA03, CA05)

Exactement comme la V2 le fait elle-même (sources relues dans `D:\RigApplication\Source`) :

1. **Déjà ouverte ?** Un processus `RigClientAccueil` de la **même session Windows** dont la fenêtre principale
   s'intitule `Console d'accueil de RIG (<code> - <serveur> / <base>)` — la règle d'instance unique de la V2
   (`RigConsoleAccueil.cs:85`), le titre de `FormRigClientAccueil.cs:561` et `RigGreffe.cs:594`. Elle est ramenée
   au premier plan.
2. **Sinon, lancée comme son raccourci Windows** : `<RigPath>exe\RigClientAccueil.exe /g<code>`
   (`RigFormLogin.cs:311`, lu par `ListeParametre.cs:243-246`). `RigPath` est la clé de la V2
   `HKLM\SOFTWARE\Wow6432Node\RIG\RigPath` (`Common.cs:32`, `C:\RIG\` par défaut) ; la variable `RIGV3_V2_EXE`
   la remplace. Le code du greffe saute la fenêtre de choix du greffe. **Aucun** `utilisateur=` : la V2 reconnaît
   l'agent à sa session Windows (CA05).
3. **Le numéro est copié** dans le presse-papiers et dit à l'agent avant que la V2 s'affiche (CA03).
4. **La V2 ouvre sa recherche d'affaires** : dès que sa fenêtre est prête (titre présent, puis `WaitForInputIdle`),
   la V3 lui envoie par `WM_COPYDATA` le message de ses propres raccourcis « Envoyer vers » :
   `EXECUTE_PROCESSUS|RECHAFF|NoAffaire=<numéro>` (`AmiCommunication.cs:36-47` et `:92-95`, lu par
   `FormRigClientAccueil.cs:96-100` et `:427-433`). La V2 y applique ses propres droits (« Vous n'êtes pas autorisé
   à exécuter le processus », `FormRigClientAccueil.cs:1844`). `RECHAFF` = « Recherche Affaire », DLL, exécution
   unique (`PROCESSUS_ET_FONCTIONALITE`, lu sur RIG_DEV le 21/11/2026).

Une affaire que l'aiguillage donne à la V3 n'ouvre pas la V2 (refus, avec la raison). L'écran n'attend pas la V2 :
le démarrage peut prendre du temps, l'attente (2 min au plus) continue sans bloquer l'agent.

## Écarts code ↔ métier « à valider »

| Écart | Ce qui est codé | À trancher par |
|---|---|---|
| **La V2 livrée n'ouvre pas une affaire donnée.** Le paramètre `NoAffaire` de `PROC_RECHAFF` (pré-remplit le n° et lance la recherche) n'existe que dans une modification **non commitée** de `D:\RigApplication` (`PROC_RECHAFF.cs`, `OPE_CRITERES.cs`, photos des 17/11). | La V3 envoie `NoAffaire=` quand même : la V2 livrée l'ignore sans erreur (`NumDemande` est ancré, `RegEx.cs:28`) et ouvre sa recherche **vide** ; le numéro est dans le presse-papiers. Si la modification V2 est livrée, la recherche s'ouvre pré-remplie sans rien changer à la V3. | référent Socle (chantier V2, note de SOC-015) |
| **Coller le numéro d'office** (`DataToCopyPaste`, comme les « Envoyer vers » de la V2) | Non fait : la V2 colle dans le champ qui a le focus, et dans `OPE_CRITERES` deux champs portent le même `TabIndex` (4 : n° affaire et n° de titre). Coller au mauvais endroit lancerait une mauvaise recherche. | référent Socle |
| **SOC-014 CA04 — fermer la création d'affaire dans la V2 « par les droits »** (règle retenue du carnet). L'inventaire ci-dessous compte **49 fichiers, 69 appels** de `Affaire.CreateAffaire` : **15 services EDI, 5 de facturation externe et 1 traitement de nuit** tournent sans agent, donc **aucun droit d'agent ne les ferme**. | Rien n'est écrit dans la V2 depuis ce poste (règle du plan). L'aiguillage donne à la V3 toute création postérieure au passage ; l'inventaire est tenu par le banc contre le code V2. | référent Socle : fermer les chemins sans agent est un chantier V2 (règle retenue de SOC-014) ; le retrait des droits au jour du passage relève du passage (lot 13) |
| **Heure de création d'une affaire V2** : `DateTime.Now` du serveur qui la crée (`Affaire.cs:126`). | Comparée comme une heure **locale du greffe**. Vrai en métropole ; pour l'outre-mer, à vérifier : les serveurs RDS des greffes d'outre-mer sont-ils à l'heure du greffe ? | exploitation |

## Ce qui ne se fait pas depuis ce poste

- **La vraie V2 n'est jamais lancée par le banc** (elle est installée sur ce poste, `C:\rig\exe`, et se connecterait
  aux bases) : le banc se relance lui-même en « fausse V2 », une fenêtre titrée comme la console, hors écran, sans
  activation ; elle note les arguments reçus et les messages `WM_COPYDATA`. Le premier plan et le presse-papiers de
  la personne qui lance le banc ne sont pas touchés. Ce qui reste à jouer sur un **serveur RDS de recette** (SOC-015
  T05) : la vraie V2 lancée puis ramenée, sa recherche ouverte, un agent sans droit `RECHAFF`.
- **CA04 côté V2** (la V2 d'un greffe passé refuse de créer, sans s'arrêter en erreur) : demande une V2 en recette
  sur une base au passage fait, et les chantiers ci-dessus.
- **SOC-014 T02** (créer l'affaire dans la V3 en lisant la fiche reprise) et **SOC-015 T03/T04** (historique recopié
  d'une ancienne affaire, en lecture seule) attendent les tables d'affaires de la recopie (lots 7 à 11).
- **40 greffes** : la règle ne dépend que du catalogue ; chaque greffe passe à sa date, saisie au catalogue (SOC-007).

## Chemins de création d'affaire de la V2 (SOC-014 CA04, T03)

Appels de `Affaire.CreateAffaire(` ou `Affaire.CreateAffaireSansSauver(` dans `D:\RigApplication\Source`, hors
outils de reprise joués une fois (`Migration*`, `Moulinettes`, `BatchOneShot`). Le banc relit le code et compare :
un chemin ajouté ou retiré dans la V2 le fait passer au rouge.

| Fichier | Appels | Nature |
|---|---|---|
| `Edi/DLL/EdiMetier/EdiAJMJ/AjmjEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiBeDivergence/BeDivergenceEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiBris/EdiBrisEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiDematDCA/DematDCADemande.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiDematIP/DematIPEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiFluxGenerique/Das/FluxGeneriqueDas.cs` | 2 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiFluxGenerique/Dca/FluxGeneriqueDca.cs` | 2 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiFluxGenerique/LiasseCfeEdiV1.cs` | 2 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiFluxGenerique/LiasseCfeEdiV2.cs` | 7 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiFluxGeneriqueJud/FluxGeneriques/FluxGeneriqueJud.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiLiasseCfe/Obsolete/LiasseCfeEdi.cs` | 2 | service EDI (automatique : aucun droit d'agent ne le ferme) — dossier ancien, livré ? à confirmer |
| `Edi/DLL/EdiMetier/EdiNotification/Judiciaire/NotificationSESJUD.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiNotification/RCS/NotificationSESRCS.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiRPJCEnr/RPJCEnrEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Edi/DLL/EdiMetier/EdiRbeHisto/RBE_HISTOEdiEntrant.cs` | 1 | service EDI (automatique : aucun droit d'agent ne le ferme) |
| `Judiciaire/DLL/RigMetierJudiciaireForm/Retour site local/frmSiteLocalRetaud.cs` | 1 | processus (droit d'agent) — dossier ancien, livré ? à confirmer |
| `Judiciaire/Exe/PROC_INJONCTION_DCA/OLD/frmInjonctionsDCA.cs` | 1 | processus (droit d'agent) — dossier ancien, livré ? à confirmer |
| `Judiciaire/Exe/PROC_RAPPRO_ACTEURS/frmRapproActeurs.cs` | 2 | processus (droit d'agent) |
| `Outils/DLL/RigMetier/Demande/Demande.cs` | 3 | métier commun (appelé par processus et EDI) |
| `Outils/DLL/RigMetier/Facture/TpgDemandeTresor.cs` | 1 | métier commun (appelé par processus et EDI) |
| `Outils/DLL/RigMetier/JUDICIAIRE/Instance.cs` | 1 | métier commun (appelé par processus et EDI) |
| `Outils/DLL/RigMoteurCPP/ClientXML/EdiFichier.cs` | 1 | facturation externe (service) |
| `Outils/DLL/RigMoteurCPP/RIG_FactExterne/FactService.cs` | 1 | facturation externe (service) |
| `Outils/DLL/RigMoteurCPP/RIG_FactExterne/Facture.cs` | 1 | facturation externe (service) |
| `Outils/DLL/RigMoteurCPP/RIG_FactExterne/FactureBorne.cs` | 1 | facturation externe (service) |
| `Outils/Exe/Batch/FactureXML/FactureXML.cs` | 1 | traitement de nuit |
| `RCS/DLL/RIG_FactExterne/RIG_FactExterne/FactService.cs` | 1 | facturation externe (service) |
| `RCS/Exe/PROC_REQUI/frmREQUI.cs` | 2 | processus (droit d'agent) |
| `RCS/Exe/REQUI/frmREQUI.cs` | 1 | processus (droit d'agent) |
| `RCS/Exe/RIG_RECH_LET/frmRechLET.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/Inscriptions/PROC_CINSM/PROC_CINSM.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_ARRIVEE/OPE_ARRIVEE.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_CHM/PROC_CHJCM/Metier/Ordonnance/Ordonnance.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_CLOT/PROC_CLOT/PROC_CLOT.cs` | 2 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_DEPPCM/OPE_FACTU_MASSE.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_ENCAISS/Saisie/VentilationLine.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_FACTURE/PROC_FACTURE_RECETTE/Preview/OPE_PREVIEW.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_GCOM/GcomTreeViewEngine/ActionMenus/MenuDemande.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_INTEGCREANCE/INTCREA/1 Integration des Creances Transitoires/IntegrationCreances.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_IPSUIVI/Etape/OPE_IPSUIVI.cs` | 2 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_REJETS_BILANS/ETP_REJETS_BILANS/OPE_REJETS_BILANS.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_RETOURLRAR/ETP_RETOURLRAR_TREEVIEW/OPE_RETOURLRAR_TREEVIEW.cs` | 3 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_SRBE_RAPPEL/PROC_SRBE/PROC_SRBE_RAPPEL.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/PROC_VNOTIF/NOTIFS/OPE_NOTIFS_RECUES.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/SURVEILLANCES/PROC_ISURV/PROC_ISURV.cs` | 3 | processus (droit d'agent) |
| `RIG/DLL/Processus/SURVEILLANCES/PROC_SRBE/PROC_SRBE/PROC_DBE_INJ/PROC_IDBE.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/Processus/SURVEILLANCES/PROC_SRBE/PROC_SRBE/PROC_DBE_RELANCE/PROC_RDBE.cs` | 1 | processus (droit d'agent) |
| `RIG/DLL/RigAutomate/AutomateVB6/RigFormAutomateVB6.cs` | 1 | automate (tout processus) |
| `RIG/DLL/RigAutomate/Processus.Tableaux.cs` | 1 | automate (tout processus) |

`Demande.cs` est le chemin commun : toute création de demande crée l'affaire au besoin (`Demande.cs:562-568`) — y
compris depuis les services EDI. Le code du processus qui ouvre chaque chemin (pour le retrait des droits) reste à
établir fonction par fonction au moment du passage.

## Le banc

`.\tests.ps1 SocleAiguillage` (`tests\SocleAiguillageCheck`) : serveur réel en mémoire sur une base jetable du SQL
Server du poste (`docker compose up -d`), un contrôle par critère (SOC-014 CA01–CA06, SOC-015 CA01–CA05), chacun
avec son jumeau. Il joue aussi les tests « SOC-015 » de l'écran (vitest) et relit les sources de la V2
(`RIGV3_V2_SOURCES` pour un autre dossier).
