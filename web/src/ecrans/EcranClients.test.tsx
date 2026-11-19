import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranClients } from './EcranClients'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranClients.razor, 6 clients de demonstration).
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

describe('EcranClients', () => {
  it('affiche les six clients, avec leur statut en pastille', () => {
    const { container } = render(<EcranClients selectionner={vi.fn()} />)
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('6 client(s) sur 6')
    expect(lignes(container)[0]).toEqual(['1042', 'C-000142', 'LEROY', 'SELARL LEROY & ASSOCIES', 'Mandataires', 'Actif', 'En compte'])
    expect(screen.getByText('Bloque', { selector: 'span.rig-etat' }).className).toBe('rig-etat rig-etat--attente')
  })

  it('filtre par mot cle (sans casse) et par statut, et « Effacer » remet tout', () => {
    const { container } = render(<EcranClients selectionner={vi.fn()} />)
    fireEvent.change(screen.getByLabelText('Mot cle'), { target: { value: 'dup' } })
    expect(lignes(container).map((l) => l[2])).toEqual(['DUPONT'])

    fireEvent.change(screen.getByLabelText('Mot cle'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'En sommeil' }))
    expect(screen.getByRole('button', { name: 'En sommeil' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(lignes(container).map((l) => l[2])).toEqual(['PREV51'])

    fireEvent.click(screen.getByRole('button', { name: 'Greffe' }))
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun client ne repond a ces criteres.')

    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('6 client(s) sur 6')
  })

  it('un clic sur une ligne remonte l\'abrege du client a Blazor', () => {
    const selectionner = vi.fn()
    const { container } = render(<EcranClients selectionner={selectionner} />)
    fireEvent.click(container.querySelectorAll('tbody tr')[2]!)
    expect(selectionner).toHaveBeenCalledWith('DUPONT')
  })
})
