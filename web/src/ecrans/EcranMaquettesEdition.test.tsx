import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranMaquettesEdition } from './EcranMaquettesEdition'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const edition = (c: HTMLElement) => c.querySelector('.rig-nonmaq__texte--sourd')!.textContent!.replace(/\s+/g, ' ').trim()

describe('EcranMaquettesEdition', () => {
  it('liste les cinq maquettes et les tables liees de la premiere', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect(compte(container)).toBe('5 maquette(s)')
    expect(screen.getByText('Tables liees a MQ_KBIS')).toBeTruthy()
    expect([...container.querySelectorAll('.rig-chip')].map((c) => c.textContent)).toEqual(['DOSSIER', 'DEPOT', 'CLIENT'])
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 1 - courrier simple — recto-verso')
  })

  it('les quatre listes de lot ont le meme catalogue et leur propre choix', () => {
    render(<EcranMaquettesEdition />)
    const lots = ['Lot standard', 'Lot LRAR', 'Lot iLRAR', 'Lot de substitution']
      .map((l) => screen.getByLabelText(l) as HTMLSelectElement)
    expect(lots.map((s) => s.value)).toEqual(['Lot 1 - courrier simple', 'Lot 3 - recommande', 'Lot 2 - courrier suivi', '(aucun)'])
    for (const s of lots) expect(s.options).toHaveLength(5)
  })

  it('les parametres d\'edition suivent les choix : lot, recto-verso, agrafage', () => {
    const { container } = render(<EcranMaquettesEdition />)
    fireEvent.change(screen.getByLabelText('Lot standard'), { target: { value: 'Lot 4 - depot guichet' } })
    fireEvent.click(screen.getByRole('button', { name: 'Recto-verso' }))
    fireEvent.click(screen.getByRole('button', { name: 'Agrafage' }))
    expect(edition(container)).toBe('Edition : Greffe - Accueil — A4 en-tete — Lot 4 - depot guichet — agrafe')
  })

  it('filtre par ecran d\'origine (liste triee) et ouvre une maquette', () => {
    const { container } = render(<EcranMaquettesEdition />)
    expect([...(screen.getByLabelText('Ecran d\'origine') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Tous', 'Archivage', 'Audiences', 'Courrier du jour', 'Demandes recues', 'Factures'])
    fireEvent.change(screen.getByLabelText('Type de document'), { target: { value: 'Courrier' } })
    expect(compte(container)).toBe('2 maquette(s)')
    fireEvent.click(screen.getByText('MQ_CONVOC'))
    expect(screen.getByText('Tables liees a MQ_CONVOC')).toBeTruthy()
    expect(container.querySelectorAll('tbody tr')[1]!.querySelectorAll('td')[5]!.textContent).toBe('22/07/2026')
  })
})
