import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranGestionAudiences } from './EcranGestionAudiences'

afterEach(cleanup)

const entetes = (c: HTMLElement) => [...c.querySelectorAll('thead th')].map((th) => th.textContent)
const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const options = (libelle: string) =>
  [...(screen.getByLabelText(libelle) as HTMLSelectElement).options].map((o) => o.textContent)

describe('EcranGestionAudiences', () => {
  it('affiche les cinq audiences en mode cabinets, avec la colonne Greffier', () => {
    const { container } = render(<EcranGestionAudiences />)
    expect(compte(container)).toBe('5 sur 100 maximum')
    expect(entetes(container)).toHaveLength(13)
    expect(entetes(container).slice(5, 7)).toEqual(['Juge', 'Greffier'])
    const premiere = [...container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')].map((td) => td.textContent)
    expect(premiere).toEqual(['Lundi', '14/09/2026', '09h00', 'Audience de procedure', 'CAB-01', 'Mme LEROY', 'M. PETIT',
      'Renvoi', 'Oui', '—', '—', '—', 'non dispo.'])
  })

  it('hors mode cabinets, la colonne unique devient Section et Greffier disparait', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.click(screen.getByRole('button', { name: 'Recherche par cabinets' }))
    expect(entetes(container)).toHaveLength(12)
    expect(entetes(container)[5]).toBe('Section')
    expect(entetes(container)).not.toContain('Greffier')
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')[5]!.textContent).toBe('1re section')
  })

  it('les listes de choix sont les valeurs distinctes, triees comme Order()', () => {
    render(<EcranGestionAudiences />)
    expect(options('Juge')).toEqual(['Tous', 'M. DUVAL', 'Mme LEROY'])
    expect(options('Type de classement')).toEqual(['Tous', 'Clos', 'Mise en delibere', 'Radiation', 'Renvoi'])
    // Un cabinet vide n'est pas propose.
    expect(options('Code de cabinet')).toEqual(['Tous', 'CAB-01', 'CAB-02', 'CAB-03'])
  })

  it('filtre par juge et par dates, et borne au nombre maximum', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.change(screen.getByLabelText('Juge'), { target: { value: 'M. DUVAL' } })
    expect(compte(container)).toBe('2 sur 100 maximum')
    fireEvent.change(screen.getByLabelText('Juge'), { target: { value: '' } })

    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2026-09-15' } })
    fireEvent.change(screen.getByLabelText('Au'), { target: { value: '2026-09-17' } })
    expect(compte(container)).toBe('2 sur 100 maximum')

    fireEvent.change(screen.getByLabelText('Nombre de resultats maximum'), { target: { value: '1' } })
    expect(compte(container)).toBe('1 sur 1 maximum')
    // Une saisie illisible garde la derniere valeur lisible.
    fireEvent.change(screen.getByLabelText('Nombre de resultats maximum'), { target: { value: '' } })
    expect(compte(container)).toBe('1 sur 1 maximum')
  })

  it('dit qu\'aucune audience ne repond, sur toute la largeur', () => {
    const { container } = render(<EcranGestionAudiences />)
    fireEvent.change(screen.getByLabelText('Du'), { target: { value: '2027-01-01' } })
    const vide = screen.getByText('Aucune audience ne repond a ces criteres.')
    expect(vide.getAttribute('colspan')).toBe('13')
    expect(vide.parentElement!.className).toBe('rig-table__ligne--inerte')
    expect(compte(container)).toBe('0 sur 100 maximum')
  })
})
