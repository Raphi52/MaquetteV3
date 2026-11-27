import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranRedaction, type Decision, type VueRedaction } from './EcranRedaction'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

// Les decisions que le pont rend (Pont/EcranRedaction.cs) : deux deliberees (sans appel prononce), deux prononcees.
const decision = (d: Partial<Decision> & Pick<Decision, 'id' | 'numero' | 'affaire' | 'delibere'>): Decision => ({
  typeDecision: '', sensGeneral: '', ressort: '', date: '', datePrevue: '', miseEnDelibere: '', prononce: '',
  avantDireDroit: '', contradictoire: '', executionProvisoire: '', debatsPublics: false, decisionPublique: false,
  mesureInstruction: false, documents: 0, mesures: [], ...d,
})
const decisions: Decision[] = [
  decision({ id: 1, numero: '2026J00121', affaire: 'AMITEL SAS - ouverture', delibere: true, typeDecision: 'Jugement d\'ouverture',
             ressort: 'Premier ressort', avantDireDroit: 'Non', contradictoire: 'Oui', executionProvisoire: 'Oui',
             miseEnDelibere: '20/02/2026 14:00 - Chambre 2', debatsPublics: true, decisionPublique: true, mesureInstruction: true,
             documents: 4, mesures: ['Expertise n°26EXP00114'] }),
  decision({ id: 2, numero: '2026J00121', affaire: 'AMITEL SAS - ouverture', delibere: true, typeDecision: 'Ordonnance' }),
  decision({ id: 3, numero: '2025J00088', affaire: 'MARTIN EURL - cloture', delibere: false, prononce: '20/09/2026', documents: 0 }),
]
const vue = (v: Partial<VueRedaction> = {}): VueRedaction => ({ decisions, mode: 'serveur', message: null, ...v })

describe('EcranRedaction', () => {
  it('ouvre sur la premiere decision deliberee, ses champs, ses listes, ses caracteres et ses mesures', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => vue() })
    const { container } = render(<EcranRedaction />)
    await screen.findByText('Expertise n°26EXP00114')
    expect(compte(container)).toBe('Decision judiciaire liee a 4 documents')
    expect(screen.getAllByRole('button', { name: '2026J00121 — AMITEL SAS - ouverture' })[0]!.className).toBe('rig-bascule rig-bascule--actif')
    expect(valeur('Type de decision')).toBe('Jugement d\'ouverture')
    expect(valeur('Mise en delibere le')).toBe('20/02/2026 14:00 - Chambre 2')
    // Une date absente s'affiche « — » ; avant dire droit, contradictoire, execution provisoire : le libelle de leur liste.
    expect(valeur('Prononce le')).toBe('—')
    expect([valeur('Avant dire droit'), valeur('Contradictoire'), valeur('Execution provisoire')]).toEqual(['Non', 'Oui', 'Oui'])
    expect([...container.querySelectorAll('.rig-etat')].map((e) => `${e.textContent}:${e.className}`)).toEqual([
      'Debats publics:rig-etat rig-etat--ok', 'Decision publique:rig-etat rig-etat--ok', 'Mesure d\'instruction:rig-etat rig-etat--ok',
    ])
    expect(screen.getByText('Expertise n°26EXP00114').className).toBe('rig-table__mono')
  })

  it('deux decisions de la meme instance restent deux lignes ; la seconde se choisit seule', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => vue() })
    const { container } = render(<EcranRedaction />)
    const lignes = await screen.findAllByRole('button', { name: '2026J00121 — AMITEL SAS - ouverture' })
    expect(lignes).toHaveLength(2)
    fireEvent.click(lignes[1]!)
    expect(lignes.map((b) => b.className)).toEqual(['rig-bascule', 'rig-bascule rig-bascule--actif'])
    expect(valeur('Type de decision')).toBe('Ordonnance')
    expect(compte(container)).toBe('Decision judiciaire liee a 0 documents')
  })

  it('l\'onglet Prononce liste les decisions prononcees ; une decision sans mesure le dit', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => vue() })
    const { container } = render(<EcranRedaction />)
    await screen.findByText('Expertise n°26EXP00114')
    fireEvent.click(screen.getByRole('button', { name: 'Prononce' }))
    expect(screen.getByRole('button', { name: 'Prononce' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(screen.queryByRole('button', { name: '2026J00121 — AMITEL SAS - ouverture' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '2025J00088 — MARTIN EURL - cloture' }))
    expect(compte(container)).toBe('Decision judiciaire liee a 0 documents')
    expect(valeur('Prononce le')).toBe('20/09/2026')
    expect(screen.getByText('Aucune mesure liee a cette decision.')).toBeTruthy()
  })
})

// D'ou viennent les decisions (plan « RIG v3 100 % fonctionnel », lot 10) : la demonstration est marquee « exemple », la base
// ne l'est pas, une panne montre son motif et AUCUNE decision (Pont/EcranRedactionBase.cs).
describe('EcranRedaction : provenance des decisions', () => {
  it('sans serveur, les decisions de demonstration sont marquees « exemple »', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => vue({ mode: 'demo' }) })
    const { container } = render(<EcranRedaction />)
    await screen.findByText('Expertise n°26EXP00114')
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Décisions de démonstration')
  })

  it('branche au serveur, les decisions de la base, sans mention', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => vue() })
    const { container } = render(<EcranRedaction />)
    await screen.findByText('Expertise n°26EXP00114')
    expect(container.querySelector('[role="note"]')).toBeNull()
  })

  it('en panne, le motif et aucune decision (jamais la demonstration a la place)', async () => {
    installerFauxPont({ 'EcranRedaction.Decisions': () => ({ decisions: [], mode: 'erreur', message: 'La base du greffe 3801 ne répond pas.' }) })
    const { container } = render(<EcranRedaction />)
    expect((await screen.findByRole('alert')).textContent).toContain('La base du greffe 3801 ne répond pas.')
    expect(container.querySelectorAll('.rig-bascule')).toHaveLength(0)
    expect(screen.getByText('Aucun appel d\'affaire delibere')).toBeTruthy()
  })
})
