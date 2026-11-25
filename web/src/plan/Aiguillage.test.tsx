import { describe, expect, it, vi } from 'vitest'
import type { ReactElement } from 'react'
import { Aiguillage } from './Aiguillage'
import { paletteVue, proc } from '../test/plan'
import { contexteDossier } from '../test/dossier'
import type { GestePlan } from './types'
import { EcranActes } from '../ecrans/EcranActes'
import { EcranArrivee } from '../ecrans/EcranArrivee'
import { EcranBodacc } from '../ecrans/EcranBodacc'
import { EcranClients } from '../ecrans/EcranClients'
import { EcranCommandesWeb } from '../ecrans/EcranCommandesWeb'
import { EcranDemandes } from '../ecrans/EcranDemandes'
import { EcranGenere } from '../ecrans/EcranGenere'
import { EcranNonMaquette } from '../ecrans/EcranNonMaquette'
import { EcranOuvertureDossier } from '../ecrans/EcranOuvertureDossier'
import { EcranRechAffaires } from '../ecrans/EcranRechAffaires'
import { EcranRecherche } from '../ecrans/EcranRecherche'
import { EcranRequisitions } from '../ecrans/EcranRequisitions'
import { EcranTachesLot } from '../ecrans/EcranTachesLot'
import { EcranVerrous } from '../ecrans/EcranVerrous'

// L'AIGUILLAGE (le @switch de Home.razor) : chaque code mene a SON ecran React,
// monte directement, avec les parametres que sa coquille .razor lui passait.
// Lu sur l'element rendu par la fonction (sans monter l'ecran, qui appellerait
// son pont) ; tests/MotifCheck et NomMetierCheck tiennent le reste du catalogue.
function aiguiller(code: string, ecran = proc().ecran) {
  const plan = vi.fn<GestePlan>()
  const el = Aiguillage({ proc: proc({ code, libelle: 'Libelle ' + code, ecran }), plan }) as ReactElement<Record<string, unknown>> | null
  return { el, plan }
}

describe('Aiguillage des ecrans de procedure', () => {
  it.each([
    ['PROC_ACTES_NUM', EcranActes], ['PROC_ARRIVEE', EcranArrivee], ['BODACC', EcranBodacc],
    ['PROC_REQUI', EcranRequisitions], ['PROC_COMMANDES_WEB', EcranCommandesWeb], ['PROC_VERROUS', EcranVerrous],
    ['PROC_GCLIENT', EcranClients],
  ])('%s monte son ecran', (code, ecran) => {
    expect(aiguiller(code).el!.type).toBe(ecran)
  })

  // Un ecran generique (Pont/EcransGeneres/) : la procedure sans ecran a part porte son descripteur.
  it('une procedure dont le descripteur est porte monte EcranGenere ; sans descripteur, elle reste a venir', () => {
    const descripteur = { code: 'ECRAN_TEST', titre: 'Ecran test', forme: 'File' as const, sujet: 'dossiers', recherche: '', criteres: [],
      valider: 'Valider', rang: 1, etats: [], groupes: [], marches: [], sections: [], colonnes: [], parametres: [], gestes: [],
      lignes: [], membres: [], enOnglets: false }
    expect(aiguiller('ECRAN_TEST', { ...proc().ecran, descripteur }).el!.type).toBe(EcranGenere)
    expect(aiguiller('ECRAN_TEST').el!.type).toBe(EcranNonMaquette)
  })

  it('un ecran qui ouvre un dossier remonte son NUMERO au plan de travail', () => {
    for (const code of ['PROC_ACTES_NUM', 'PROC_ARRIVEE', 'PROC_VERROUS', 'BODACC']) {
      const { el, plan } = aiguiller(code)
      ;(el!.props.ouvrirDossier as (n: string) => void)('004176')
      expect(plan).toHaveBeenCalledWith('ouvrirNumero', '004176')
    }
  })

  it('la recherche d\'affaires : ouvrir une affaire, ou un dossier pour le SIREN cherche', () => {
    const { el, plan } = aiguiller('PROC_RECHAFF')
    expect(el!.type).toBe(EcranRechAffaires)
    ;(el!.props.ouvrir as (n: string) => void)('2026B01067')
    ;(el!.props.ouvrirUnDossier as (s: string) => void)('903118776')
    expect(plan.mock.calls).toEqual([['ouvrirNumero', '2026B01067'], ['ouvrirUnDossier', '903118776']])
  })

  it('les demandes recoivent les dossiers valides ; l\'ouverture, son SIREN et sa cle', () => {
    const ecran = { ...proc().ecran, valides: ['004176'], sirenDemande: '903118776', cleOuverture: '903118776' }
    const demandes = aiguiller('PROC_DEMANDE', ecran).el!
    expect(demandes.type).toBe(EcranDemandes)
    expect(demandes.props.valides).toEqual(['004176'])
    const ouverture = aiguiller('OUVERTURE_DOSSIER', ecran).el!
    expect(ouverture.type).toBe(EcranOuvertureDossier)
    expect(ouverture.key).toBe('903118776')
    expect(ouverture.props.sirenDemande).toBe('903118776')
  })

  it('les taches en lot : la session ouverte par le C#, recreee quand elle change', () => {
    const lot = aiguiller('PROC_TACHES_LOT', { ...proc().ecran, lot: { session: 's1', version: 3 } }).el!
    expect(lot.type).toBe(EcranTachesLot)
    expect(lot.key).toBe('s1')
    expect(lot.props).toEqual({ session: 's1', version: 3 })
    expect(aiguiller('PROC_TACHES_LOT').el).toBeNull()
  })

  it('la page « Recherche » : la palette etalee en page, avec la vue calculee par le plan de travail', () => {
    const recherche = paletteVue({ texte: 'dupont' })
    const { el, plan } = aiguiller('RECHERCHE', { ...proc().ecran, recherche })
    expect(el!.type).toBe(EcranRecherche)
    expect(el!.props).toEqual({ vue: recherche, plan })
  })

  it('un ecran ouvert depuis une affaire en recoit le contexte, monte a sa cle ; sans affaire, ni l\'un ni l\'autre', () => {
    const dossier = contexteDossier()
    const pourLAffaire = { ...proc().ecran, dossier }
    for (const code of ['PROC_DEMANDE', 'PROC_REQUI', 'PROC_COMMANDES_WEB', 'PROC_CLOT', 'BODACC', 'PROC_DEPOTJUD',
                        'PROC_CJUD', 'PROC_GEDJUD', 'PROC_FACTURE', 'PROC_ENCAISS',
                        'PROC_VERROUS', 'PROC_TELETRANS']) {
      const avec = aiguiller(code, pourLAffaire).el!
      expect(avec.props.dossier, code).toBe(dossier)
      // La cle : rouvert pour une autre affaire, l'ecran repart de ses valeurs de depart.
      expect(avec.key, code).toBe('004176')
      const sans = aiguiller(code).el!
      expect(sans.props.dossier, code).toBeNull()
      expect(sans.key, code).toBeNull()
    }
    // Ceux-ci ne peuvent rien faire d'une affaire (aucun critere ni liste a son nom) : rien ne leur est passe.
    // PROC_COURRIER_DIVERS est le CATALOGUE des modeles (parametrage) : son bloc « Pour le dossier » ne
    // servait a rien (constat du chef, 004182, 20/11/2026) ; on ecrit depuis l'affaire par « Ecrire » (lot 9).
    for (const code of ['PROC_ARCHIRAD', 'PROC_REDA', 'PROC_AUDIENCE', 'PROC_COURRIER_DIVERS']) {
      const el = aiguiller(code, pourLAffaire).el!
      expect(el.props.dossier, code).toBeUndefined()
      expect(el.key, code).toBeNull()
    }
  })

  it('une procedure sans ecran tombe sur l\'ecran qui se nomme, avec son motif', () => {
    for (const code of ['PROC_CREANCE', 'PROC_INCONNUE']) {
      const el = aiguiller(code).el!
      expect(el.type).toBe(EcranNonMaquette)
      expect(el.props).toEqual({ code, libelle: 'Libelle ' + code, motif: 'Motif lu dans le menu.' })
    }
  })
})
