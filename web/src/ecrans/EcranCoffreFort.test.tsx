import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranCoffreFort } from './EcranCoffreFort'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranCoffreFort.razor) : 4 depots, dont 2
// « A transmettre » (jamais envoye, ou en erreur) et 2 « Deja transmis ».
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranCoffreFort', () => {
  it('affiche les depots a transmettre, le depot en erreur en alerte', () => {
    const { container } = render(<EcranCoffreFort />)
    expect(compte(container)).toBe('2 depot(s)')
    expect(container.querySelectorAll('thead th')).toHaveLength(12)
    expect(lignes(container).map((l) => l[0])).toEqual(['CF-2026-0603', 'CF-2026-0610'])
    expect(container.querySelectorAll('tbody tr')[1]!.className).toBe('rig-table__ligne--alerte')
    expect(lignes(container)[1]!.slice(3, 5)).toEqual(['En erreur', '04/09/2026'])
    // Distinct().Order() des coffres, apres « Tous ».
    expect([...(screen.getByLabelText('Nom du coffre') as HTMLSelectElement).options].map((o) => o.text))
      .toEqual(['Tous', 'COFFRE_BLANC', 'COFFRE_LEROY', 'COFFRE_PETIT'])
  })

  it('l\'onglet « Deja transmis » et les criteres filtrent la liste', () => {
    const { container } = render(<EcranCoffreFort />)
    fireEvent.click(screen.getByRole('button', { name: 'Deja transmis' }))
    expect(screen.getByRole('button', { name: 'Deja transmis' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(lignes(container).map((l) => l[0])).toEqual(['CF-2026-0412', 'CF-2026-0518'])
    fireEvent.change(screen.getByLabelText('Nom du coffre'), { target: { value: 'COFFRE_BLANC' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['CF-2026-0518'])
    fireEvent.change(screen.getByLabelText('Code ou destinataire'), { target: { value: 'leroy' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun depot ne repond a ces criteres.')
  })

  it('un clic sur un depot montre son historique, borne par le nombre de lignes', () => {
    const { container } = render(<EcranCoffreFort />)
    fireEvent.click(screen.getByRole('button', { name: 'Deja transmis' }))
    fireEvent.click(container.querySelectorAll('tbody tr')[0]!)
    expect(screen.getByText('Historique de CF-2026-0412 (20 ligne(s) au plus)')).toBeTruthy()
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(3)
    fireEvent.change(screen.getByLabelText('Nombre de lignes d\'historique'), { target: { value: '2' } })
    expect(container.querySelectorAll('.rig-historique__liste li')).toHaveLength(2)
  })
})
