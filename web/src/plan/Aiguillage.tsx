// L'AIGUILLAGE des ecrans de procedure : le @switch (ProcOuverte.Code) de
// Components/Pages/Home.razor, porte au lot 2 du plan « finir le passage a
// React » (19/11/2026). C'est le SEUL fichier de l'application qui aiguille par
// code de procedure (tests/NomMetierCheck le tient) : chaque ecran est un
// composant React monte ICI, directement, avec les memes parametres que sa
// coquille .razor lui passait -- les ecrans s'appellent entre eux sans plus
// repasser par Blazor.
//
// Un ecran qui « ouvre un dossier » remonte le NUMERO : la page
// (SessionPlanTravail.OuvrirParNumero) retrouve l'affaire et ouvre la
// visionneuse ; les ecrans n'ont rien a savoir de la visionneuse.

import { EcranActes } from '../ecrans/EcranActes'
import { EcranActeurs } from '../ecrans/EcranActeurs'
import { EcranActualites } from '../ecrans/EcranActualites'
import { EcranArchivage } from '../ecrans/EcranArchivage'
import { EcranArrivee } from '../ecrans/EcranArrivee'
import { EcranAudiences } from '../ecrans/EcranAudiences'
import { EcranBanques } from '../ecrans/EcranBanques'
import { EcranBodacc } from '../ecrans/EcranBodacc'
import { EcranClients } from '../ecrans/EcranClients'
import { EcranCloture } from '../ecrans/EcranCloture'
import { EcranCoffreFort } from '../ecrans/EcranCoffreFort'
import { EcranCommandesWeb } from '../ecrans/EcranCommandesWeb'
import { EcranComptabilisation } from '../ecrans/EcranComptabilisation'
import { EcranConsultationJud } from '../ecrans/EcranConsultationJud'
import { EcranControleVidiged } from '../ecrans/EcranControleVidiged'
import { EcranCourriersDivers } from '../ecrans/EcranCourriersDivers'
import { EcranDemandes } from '../ecrans/EcranDemandes'
import { EcranDepotsJud } from '../ecrans/EcranDepotsJud'
import { EcranDocDemat } from '../ecrans/EcranDocDemat'
import { EcranEncaissements } from '../ecrans/EcranEncaissements'
import { EcranExperts } from '../ecrans/EcranExperts'
import { EcranFactures } from '../ecrans/EcranFactures'
import { EcranFicheClient } from '../ecrans/EcranFicheClient'
import { EcranGedJud } from '../ecrans/EcranGedJud'
import { EcranGestionAudiences } from '../ecrans/EcranGestionAudiences'
import { EcranMaquettesEdition } from '../ecrans/EcranMaquettesEdition'
import { EcranMessagerie } from '../ecrans/EcranMessagerie'
import { EcranNonMaquette } from '../ecrans/EcranNonMaquette'
import { EcranOuvertureDossier } from '../ecrans/EcranOuvertureDossier'
import { EcranParametresAccueil } from '../ecrans/EcranParametresAccueil'
import { EcranRechAffaires } from '../ecrans/EcranRechAffaires'
import { EcranRedaction } from '../ecrans/EcranRedaction'
import { EcranRequisitions } from '../ecrans/EcranRequisitions'
import { EcranStatistiques } from '../ecrans/EcranStatistiques'
import { EcranTachesLot } from '../ecrans/EcranTachesLot'
import { EcranTeletransmission } from '../ecrans/EcranTeletransmission'
import { EcranVerrous } from '../ecrans/EcranVerrous'
import type { GestePlan, ProcVue } from './types'

/** Le choix d'un client (EcranClients) ne remontait a rien : Home.razor ne branchait pas Selectionner. */
function aucunChoix(): void { /* rien, comme l'EventCallback vide du .razor */ }

export interface ProprietesAiguillage {
  proc: ProcVue
  plan: GestePlan
}

export function Aiguillage({ proc, plan }: ProprietesAiguillage) {
  const ouvrirParNumero = (numero: string) => plan('ouvrirNumero', numero)
  const ecran = proc.ecran
  switch (proc.code) {
    case 'PROC_ACTES_NUM':
      return <EcranActes ouvrirDossier={ouvrirParNumero} />

    case 'PROC_ARRIVEE':
      return <EcranArrivee ouvrirDossier={ouvrirParNumero} />

    case 'OUVERTURE_DOSSIER':
      // La cle change avec le SIREN : l'ecran est RECONSTRUIT (le @key du .razor).
      return <EcranOuvertureDossier key={ecran.cleOuverture} sirenDemande={ecran.sirenDemande} />

    case 'PROC_RECHAFF':
      return <EcranRechAffaires ouvrir={ouvrirParNumero}
                                ouvrirUnDossier={(siren) => plan('ouvrirUnDossier', siren)} />

    case 'PROC_DEMANDE':
      return <EcranDemandes valides={ecran.valides} ouvrir={ouvrirParNumero} />

    case 'PROC_REQUI':
      return <EcranRequisitions />

    case 'PROC_COMMANDES_WEB':
      // Ecran NEUF de NeoRIG (« Traitement des commandes ») : la V2 traitait
      // les commandes web depuis PROC_REQUI.
      return <EcranCommandesWeb />

    case 'PROC_CLOT':
      return <EcranCloture ouvrirDossier={ouvrirParNumero} />

    case 'PROC_ARCHIRAD':
      return <EcranArchivage ouvrirDossier={ouvrirParNumero} />

    case 'BODACC':
      return <EcranBodacc ouvrirDossier={ouvrirParNumero} />

    case 'PROC_DEPOTJUD':
      return <EcranDepotsJud ouvrirDossier={ouvrirParNumero} />

    case 'PROC_ACTEUR_JUD':
      return <EcranActeurs />

    case 'PROC_CJUD':
      return <EcranConsultationJud ouvrirDossier={ouvrirParNumero} />

    case 'PROC_AUDIENCE':
      return <EcranAudiences />

    case 'PROC_GESTAUDS':
      return <EcranGestionAudiences />

    case 'PROC_EXPERT':
      return <EcranExperts />

    case 'PROC_GEDJUD':
      return <EcranGedJud ouvrirDossier={ouvrirParNumero} />

    case 'PROC_REDA':
      return <EcranRedaction />

    case 'PROC_DOC_DEMAT':
      return <EcranDocDemat ouvrirDossier={ouvrirParNumero} />

    case 'PROC_CONTROLE_VIDIGED':
      return <EcranControleVidiged />

    case 'PROC_COFFRE_FORT':
      return <EcranCoffreFort />

    case 'PROC_FACTURE':
      return <EcranFactures />

    case 'PROC_ENCAISS':
      return <EcranEncaissements />

    case 'PROC_FCLIENT':
      return <EcranFicheClient />

    case 'PROC_GCLIENT':
      return <EcranClients selectionner={aucunChoix} />

    case 'PROC_STAT':
      return <EcranStatistiques />

    case 'PROC_ACCUEIL_PARAM':
      return <EcranParametresAccueil />

    case 'PROC_ACTUALITES':
      return <EcranActualites />

    case 'PROC_GESTEMAIL':
      return <EcranMessagerie />

    case 'PROC_GESTBANQ':
      return <EcranBanques />

    case 'PROC_EDIT_MAQUETTE':
      return <EcranMaquettesEdition />

    case 'PROC_COMPTABILISATION':
      return <EcranComptabilisation />

    case 'PROC_COURRIER_DIVERS':
      return <EcranCourriersDivers />

    case 'PROC_TACHES_LOT':
      // Traitement EN LOT : la meme recette sur plusieurs affaires. La session
      // est ouverte par le C# avec l'etat vivant de l'accueil (Pont/PlanTravailVue.cs).
      return ecran.lot
        ? <EcranTachesLot key={ecran.lot.session} session={ecran.lot.session} version={ecran.lot.version} />
        : null

    case 'PROC_VERROUS':
      return <EcranVerrous ouvrirDossier={ouvrirParNumero} />

    case 'PROC_TELETRANS':
      return <EcranTeletransmission ouvrirDossier={ouvrirParNumero} />

    // PROC_CREANCE n'a PAS d'ecran porte, et ce n'est pas un oubli : dans
    // RIG v2, PROC_CREANCE.cs ne contient qu'un constructeur et
    // FORM_CREANCE.Designer.cs aucun controle (32 lignes, un simple conteneur).
    // Il n'y a donc rien a porter ; inventer un ecran de creances serait
    // inventer RIG v2.
    default:
      return <EcranNonMaquette code={proc.code} libelle={proc.libelle} motif={ecran.motif} />
  }
}
