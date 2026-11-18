import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranControleVidiged } from './EcranControleVidiged'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__entete .rig-resultats-proc__compte')!.textContent
const page = () => screen.getByText(/^Page /).textContent
const guids = (c: HTMLElement) => [...c.querySelectorAll('tbody tr')].map((tr) => tr.querySelector('td')!.textContent)

describe('EcranControleVidiged', () => {
  it('pagine les cinq archives par trois, celles sans CD surlignees', () => {
    const { container } = render(<EcranControleVidiged />)
    expect(compte(container)).toBe('5 archive(s) — 3 affichee(s)')
    expect(page()).toBe('Page 1 / 2')
    expect(guids(container)).toEqual(['a41f-2026-0091', 'a41f-2026-0092', 'b77c-2026-0103'])
    expect([...container.querySelectorAll('tbody tr')].map((tr) => tr.className))
      .toEqual(['', '', 'rig-table__ligne--alerte'])
  })

  it('« Suivant » et « Precedent » restent dans les bornes', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    expect(page()).toBe('Page 2 / 2')
    expect(guids(container)).toEqual(['b77c-2026-0104', 'c12d-2026-0110'])
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    expect(page()).toBe('Page 2 / 2')
    fireEvent.click(screen.getByRole('button', { name: 'Precedent' }))
    fireEvent.click(screen.getByRole('button', { name: 'Precedent' }))
    expect(page()).toBe('Page 1 / 2')
  })

  it('filtre « Sans support CD » et par type ; les lignes par page se changent', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Sans support CD uniquement' }))
    expect(compte(container)).toBe('2 archive(s) — 2 affichee(s)')
    expect(page()).toBe('Page 1 / 1')
    fireEvent.click(screen.getByRole('button', { name: 'Sans support CD uniquement' }))

    fireEvent.change(screen.getByLabelText('Type d\'archive'), { target: { value: 'Comptes annuels' } })
    expect(compte(container)).toBe('2 archive(s) — 2 affichee(s)')
    fireEvent.change(screen.getByLabelText('Type d\'archive'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Lignes par page'), { target: { value: '10' } })
    expect(compte(container)).toBe('5 archive(s) — 5 affichee(s)')
    expect(page()).toBe('Page 1 / 1')
  })

  it('le numero affiche est celui des boutons, la page montree reste dans les bornes', () => {
    const { container } = render(<EcranControleVidiged />)
    fireEvent.click(screen.getByRole('button', { name: 'Suivant' }))
    fireEvent.change(screen.getByLabelText('Lignes par page'), { target: { value: '5' } })
    // Comme dans Blazor : « Page 2 / 1 », mais la seule page existante est montree.
    expect(page()).toBe('Page 2 / 1')
    expect(compte(container)).toBe('5 archive(s) — 5 affichee(s)')
  })
})
