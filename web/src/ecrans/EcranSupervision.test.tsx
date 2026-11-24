import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranSupervision, pastilleTraitement, type DefautSignale, type EtatGreffe, type ErreurTechnique, type VueEcranSupervision } from './EcranSupervision'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })
// fix-ok: tests suivis sur la liste moderne (lot 6 des listes) : puces de vues, points d'etat, ligne depliee, filtre Ctrl K, pied -- pas un correctif.

/** Les onglets sont devenus des puces de vues : « Greffes » et son compte (« Greffes 40 »). */
const puce = (nom: string) => screen.getByRole('button', { name: nom })

// Les regles (journal, alertes, relance, purge) vivent dans le serveur et sont prouvees par
// tests/SocleSupervisionCheck. Ici : l'ecran montre ce que le pont lui rend et lui remonte chaque geste.
function greffe(code: string, partiel: Partial<EtatGreffe> = {}): EtatGreffe {
  return {
    code, nom: `Greffe ${code}`, estOutreMer: code.startsWith('97') || code.startsWith('98'), serveur: 'SQL-PROD\\PROD',
    sante: 'pas creee', detail: null, migrationsJouees: null, derniereMigration: null, echecs: 0, premierEchec: null,
    dernierEchec: null, echecsDetail: null, verifieLe: '2026-11-21T08:30:00Z', ...partiel,
  }
}

function erreur(partiel: Partial<ErreurTechnique> = {}): ErreurTechnique {
  return {
    id: 1, suivi: '7KQ2-M9XP', instant: '2026-11-21T08:00:00Z', niveau: 'grave', origine: 'serveur', codeGreffe: '9741',
    compte: 'mlebon', serveur: 'RIGV3-APP-01', programme: 'POST /numerotation/{greffe}/restitutions',
    type: 'System.InvalidOperationException', message: 'Sequence contains no elements.', pile: 'at Rig.Truc()', ...partiel,
  }
}

const codes = ['0101', '0501', '0601', '0603', '0901', '1304', '1501', '1801', '2002', '2602', '2701', '2801', '2903', '3003',
  '3801', '3802', '3902', '4202', '4302', '5103', '5401', '5501', '5601', '5802', '5902', '6601', '6901', '6903', '7106',
  '7401', '7402', '7606', '8002', '8305', '9711', '9712', '9721', '9741', '9761', '9871']

function vue(partiel: Partial<VueEcranSupervision> = {}): VueEcranSupervision {
  return {
    mode: 'serveur', message: null, echec: false,
    etat: {
      dernierPassage: '2026-11-21T08:30:00Z',
      greffes: codes.map((c) => c === '3802'
        ? greffe(c, { sante: 'injoignable', detail: 'Base RIGV3_VIENNE injoignable' })
        : greffe(c)),
      traitements: [
        { serveur: 'APP-01', traitement: 'Partie serveur', etat: 'en marche', enCours: 0, enAttente: 0, enErreur: 0, codeGreffe: null,
          message: 'Version 2026.1.0', publieLe: '2026-11-21T08:30:00Z', fileBaisseLe: '2026-11-21T08:30:00Z' },
        { serveur: 'APP-01', traitement: 'Recopie', etat: 'en erreur', enCours: 0, enAttente: 3, enErreur: 1, codeGreffe: '4302',
          message: 'Délai dépassé (suivi 7KQ2-M9XP)', publieLe: '2026-11-21T08:30:00Z', fileBaisseLe: '2026-11-21T08:00:00Z' },
      ],
      alertes: [
        { id: 1, regle: 'base-injoignable', codeGreffe: '3802', traitement: 'Base V3 du greffe', erreur: 'La base V3 du greffe 3802 ne répond pas',
          ouverteLe: '2026-11-21T08:30:00Z', envoyeeLe: '2026-11-21T08:30:00Z', canal: 'simule', fermeeLe: null },
      ],
    },
    erreurs: [erreur()],
    ...partiel,
  }
}

describe('EcranSupervision', () => {
  it('SOC-011 CA03 : montre les 40 greffes, outre-mer compris, et la base en panne', async () => {
    installerFauxPont({ 'EcranSupervision.Ouvrir': () => vue() })
    render(<EcranSupervision />)
    expect(await screen.findByRole('button', { name: 'Greffes 40' })).toBeTruthy()
    expect(screen.getByText(/40 greffe\(s\), dont 6 d'outre-mer — 1 à surveiller/)).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(40)
    expect(lignes.find((l) => l.textContent?.includes('3802'))!.textContent).toContain('Injoignable')
    expect(lignes.find((l) => l.textContent?.includes('9871'))!.textContent).toContain('outre-mer')
    // La base en point colore : injoignable rouge ; le greffe en avatar + nom.
    const vienne = lignes.find((l) => l.textContent?.includes('3802'))!
    expect(vienne.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    expect(vienne.querySelector('.rig-lm-identite__nom')!.textContent).toBe('Greffe 3802')
    expect(document.querySelector('.rig-lm-titre')!.textContent).toBe('Greffes')
    expect(document.querySelector('.rig-lm-pied')!.textContent).toBe('40 greffe(s) affiché(s)Survol d\'un état : son détail (base, migrations, échecs).')
  })

  it('SOC-011 CA02 : rappelle l\'alerte ouverte, qui nomme le greffe', async () => {
    installerFauxPont({ 'EcranSupervision.Ouvrir': () => vue() })
    render(<EcranSupervision />)
    expect(await screen.findByText('1 alerte(s)')).toBeTruthy()
    fireEvent.click(puce('Alertes 1'))
    const ligne = screen.getAllByRole('row')[1]!
    expect(ligne.textContent).toContain('Base injoignable')
    expect(ligne.textContent).toContain('3802')
    expect(ligne.textContent).toContain('Envoyée')
    expect(ligne.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-violet')
    expect(ligne.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
  })

  it('SOC-011 CA04 : relance un traitement en erreur par son nom', async () => {
    const pont = installerFauxPont({
      'EcranSupervision.Ouvrir': () => vue(),
      'EcranSupervision.Relancer': () => vue({ message: 'Traitement Recopie relancé (en erreur → en marche) : 3 élément(s) en file repris, rien n\'est perdu.' }),
    })
    render(<EcranSupervision />)
    fireEvent.click(await screen.findByRole('button', { name: 'Traitements 2' }))
    const boutons = screen.getAllByRole('button', { name: 'Relancer' })
    expect(boutons).toHaveLength(1)
    fireEvent.click(boutons[0]!)
    expect((await screen.findByRole('status')).textContent).toContain('rien n\'est perdu')
    expect(pont.appels).toHaveBeenCalledWith('EcranSupervision.Relancer', 'Recopie')
  })

  it('SOC-011 CA04 : huit colonnes d\'avant -- « En cours » et le dernier message sont dans la ligne depliee, avec « Relancer »', async () => {
    const pont = installerFauxPont({
      'EcranSupervision.Ouvrir': () => vue(),
      'EcranSupervision.Relancer': () => vue({ message: 'Traitement Recopie relancé.' }),
    })
    render(<EcranSupervision />)
    fireEvent.click(await screen.findByRole('button', { name: 'Traitements 2' }))
    const recopie = screen.getAllByRole('row').find((l) => l.textContent?.includes('Recopie'))!
    expect(recopie.textContent).not.toContain('Délai dépassé')
    expect(recopie.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-rouge')
    fireEvent.click(within(recopie).getByRole('button', { name: 'Deplier la ligne' }))
    const detail = document.querySelector('tr.rig-lm-detail')!
    expect(detail.textContent).toContain('Greffe 4302 — Délai dépassé (suivi 7KQ2-M9XP)')
    // Depliee, la ligne n'offre « Relancer » qu'une fois : dans son detail, pas aussi dans la rangee.
    expect(screen.getAllByRole('button', { name: 'Relancer' })).toHaveLength(1)
    expect(within(recopie).queryByRole('button', { name: 'Relancer' })).toBeNull()
    fireEvent.click(within(detail as HTMLElement).getByRole('button', { name: 'Relancer' }))
    expect((await screen.findByRole('status')).textContent).toContain('relancé')
    expect(pont.appels).toHaveBeenCalledWith('EcranSupervision.Relancer', 'Recopie')
  })

  it('le filtre rapide (Ctrl K) filtre la liste montree ; changer de liste le vide', async () => {
    installerFauxPont({ 'EcranSupervision.Ouvrir': () => vue() })
    render(<EcranSupervision />)
    await screen.findByRole('button', { name: 'Greffes 40' })
    const champ = screen.getByLabelText('Greffe ou serveur…')
    expect(fireEvent.keyDown(window, { key: 'k', ctrlKey: true })).toBe(false)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: '97' } })
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(5)
    expect(screen.getByText(/5 greffe\(s\), dont 5 d'outre-mer — 0 à surveiller/)).toBeTruthy()
    fireEvent.click(puce('Erreurs 1'))
    expect((screen.getByLabelText('Aller à une erreur…') as HTMLInputElement).value).toBe('')
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(1)
  })

  it('SOC-017 CA02 : cherche par identifiant de suivi et montre le detail de l\'erreur', async () => {
    const pont = installerFauxPont({
      'EcranSupervision.Ouvrir': () => vue({ erreurs: [] }),
      'EcranSupervision.Rechercher': () => vue({
        message: 'Parcours 7KQ2-M9XP : 2 message(s), du premier au dernier.',
        erreurs: [erreur(), erreur({ id: 2, origine: 'ecran', programme: 'EcranCompteurs.Rendre', pile: 'pile de l\'ecran' })],
      }),
    })
    render(<EcranSupervision />)
    await screen.findByRole('button', { name: 'Greffes 40' })
    fireEvent.change(screen.getByLabelText('Identifiant de suivi'), { target: { value: ' 7kq2-m9xp ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Chercher' }))
    expect(await screen.findByRole('button', { name: 'Erreurs 2' })).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranSupervision.Rechercher',
      { suivi: '7kq2-m9xp', greffe: null, compte: null, du: null, au: null })
    fireEvent.click(screen.getByText('EcranCompteurs.Rendre'))
    expect(screen.getByLabelText('Détail de l\'erreur').textContent).toContain('pile de l\'ecran')
  })

  it("SOC-022 CA06 : montre les defauts signales depuis la V3 ; l'identifiant d'un defaut et sa derniere erreur se cherchent", async () => {
    const defaut: DefautSignale = {
      id: 7, suivi: 'H8RZ-4K2Q', instant: '2026-11-21T09:05:00Z', codeGreffe: '4302', compte: 'jdupont', poste: 'RDS-PUY-03',
      ecran: 'Compteurs de numéros', codeEcran: 'PROC_COMPTEURS_NUM', version: '2026.1.0', heurePoste: '2026-11-21T10:05:00+01:00',
      derniereErreur: '7KQ2-M9XP', phrase: 'Le numéro rendu ne réapparaît pas.',
    }
    const pont = installerFauxPont({
      'EcranSupervision.Ouvrir': () => vue({ defauts: [defaut] }),
      'EcranSupervision.Rechercher': (c) => (c as { suivi: string }).suivi === 'H8RZ-4K2Q'
        ? vue({ erreurs: [], defauts: [defaut], message: "H8RZ-4K2Q est un défaut signalé depuis la V3 (jdupont, greffe 4302) : voir l'onglet Défauts." })
        : vue({ defauts: [defaut], message: 'Parcours 7KQ2-M9XP : 1 message(s), du premier au dernier.' }),
    })
    render(<EcranSupervision />)
    fireEvent.click(await screen.findByRole('button', { name: 'Défauts 1' }))
    const table = screen.getByRole('table', { name: 'Défauts signalés' })
    expect(table.textContent).toContain('H8RZ-4K2Q')
    expect(table.textContent).toContain('Le numéro rendu ne réapparaît pas.')
    // L'identifiant que l'agent dicte est celui d'un defaut : la recherche mene a l'onglet des defauts.
    fireEvent.click(puce('Erreurs 1'))
    fireEvent.change(screen.getByLabelText('Identifiant de suivi'), { target: { value: 'H8RZ-4K2Q' } })
    fireEvent.click(screen.getByRole('button', { name: 'Chercher' }))
    expect((await screen.findByRole('status')).textContent).toContain('est un défaut signalé')
    expect(screen.getByRole('table', { name: 'Défauts signalés' })).toBeTruthy()
    // La derniere erreur que le defaut emporte ramene son parcours.
    fireEvent.click(screen.getByRole('button', { name: '7KQ2-M9XP' }))
    expect(await screen.findByRole('button', { name: 'Erreurs 1' })).toBeTruthy()
    expect(pont.appels).toHaveBeenLastCalledWith('EcranSupervision.Rechercher', { suivi: '7KQ2-M9XP', greffe: null, compte: null, du: null, au: null })
    fireEvent.click(puce('Défauts 1'))
    fireEvent.click(screen.getByText('Le numéro rendu ne réapparaît pas.'))
    expect(screen.getByLabelText('Détail du défaut').textContent).toContain('RDS-PUY-03')
  })

  it('refuse un compte hors Amitel avec le motif du pont', async () => {
    installerFauxPont({ 'EcranSupervision.Ouvrir': () => ({ mode: 'refuse', message: 'La supervision est réservée à l\'exploitation et à l\'assistance d\'Amitel.', echec: true, etat: null, erreurs: [] }) })
    render(<EcranSupervision />)
    expect((await screen.findByRole('alert')).textContent).toContain('réservée à l\'exploitation')
  })

  it('nomme chaque etat de traitement de SOC-011 CA01', () => {
    expect((['en marche', 'travaux en cours', 'en attente', 'en erreur', 'arrete'] as const).map((e) => pastilleTraitement(e).libelle))
      .toEqual(['En marche', 'Travaux en cours', 'En attente', 'En erreur', 'Arrêté'])
  })
})
