import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranStatistiques } from './EcranStatistiques'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranStatistiques.razor), volet « Statistiques internes ».
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

describe('EcranStatistiques', () => {
  it('affiche le volet des statistiques internes, ecarts signes', () => {
    const { container } = render(<EcranStatistiques />)
    expect(container.querySelector('.rig-resultats-proc__entete .rig-kicker')!.textContent).toBe('Statistiques internes')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('5 ligne(s)')
    expect(lignes(container)[0]).toEqual(['Immatriculations', '184', '171', '+13'])
    expect(lignes(container)[1]).toEqual(['Modifications', '412', '438', '-26'])
    // Lot 9 : ces chiffres ne viennent pas de la base du greffe, l'ecran le dit (mention « exemple »).
    expect(container.querySelector('[role="note"]')!.textContent).toContain('Chiffres de démonstration')
    // Periode : du 1er janvier a aujourd'hui, date du POSTE (DateTime.Today).
    const annee = new Date().getFullYear()
    expect((screen.getByLabelText('Date de debut') as HTMLInputElement).value).toBe(`${annee}-01-01`)
    expect(screen.getByLabelText('Niveau')).toBeTruthy()
  })

  it('chaque volet a son en-tete ; « Nombre de PC » n\'existe que dans les semestrielles', () => {
    const { container } = render(<EcranStatistiques />)
    fireEvent.click(screen.getByRole('button', { name: 'Ouverture par activites : par chiffre d\'affaire' }))
    expect(container.querySelector('thead th')!.textContent).toBe('Tranche de chiffre d\'affaire')
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('5 ligne(s)')

    fireEvent.click(screen.getByRole('button', { name: 'Statistiques semestrielles' }))
    expect(screen.queryByLabelText('Niveau')).toBeNull()
    expect((screen.getByLabelText('Nombre de PC') as HTMLInputElement).value).toBe('46')
    expect(container.querySelector('thead th')!.textContent).toBe('Indicateur')
    expect(lignes(container)[1]).toEqual(['Delai moyen en jours', '3', '4', '-1'])
  })
})
