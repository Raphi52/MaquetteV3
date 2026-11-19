import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranExperts } from './EcranExperts'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranExperts.razor) : 3 experts dont 2 actifs,
// GIRARD choisi au depart.
const lignes = (c: HTMLElement, table = 0) =>
  [...c.querySelectorAll('table')[table]!.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

describe('EcranExperts', () => {
  it('affiche les experts actifs, la fiche et les mesures en cours de GIRARD', () => {
    const { container } = render(<EcranExperts />)
    expect(container.querySelectorAll('.rig-resultats-proc__compte')[0]!.textContent).toBe('2 expert(s)')
    expect(lignes(container)).toEqual([
      ['GIRARD', 'M.', 'GIRARD Paul', 'p.girard@exemple.fr', 'Actif'],
      ['NOEL', 'Mme', 'NOEL Julie', 'j.noel@exemple.fr', 'Actif'],
    ])
    // La ligne choisie porte le trait de focus.
    expect((container.querySelector('tbody tr') as HTMLElement).style.boxShadow).toBe('inset 3px 0 0 0 var(--rig-focus)')
    expect((screen.getByLabelText('Complement de nom') as HTMLInputElement).value).toBe('Cabinet Girard & Fils')
    expect(lignes(container, 1)).toEqual([
      ['M-2026-0114', 'M.', 'GIRARD Paul', 'Cabinet Girard & Fils', 'Expert-comptable', '9 rue Gambetta, 51100 Reims'],
    ])
  })

  it('les mesures terminees, les inactifs et le filtre de qualite', () => {
    const { container } = render(<EcranExperts />)
    fireEvent.click(screen.getByRole('button', { name: 'Mesures terminees' }))
    expect(lignes(container, 1).map((l) => l[0])).toEqual(['M-2024-0503'])

    fireEvent.click(screen.getByRole('button', { name: 'Experts actifs uniquement' }))
    expect(container.querySelectorAll('.rig-resultats-proc__compte')[0]!.textContent).toBe('3 expert(s)')
    fireEvent.change(screen.getByLabelText('Qualite / profession'), { target: { value: 'Expert immobilier' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['ROUX'])
    fireEvent.click(container.querySelector('tbody tr')!)
    expect(container.querySelectorAll('.rig-resultats-proc__compte')[1]!.textContent).toBe('ROUX')
    expect(screen.getByText('Inactif', { selector: '.rig-criteres__bloc > .rig-etat' }).className).toBe('rig-etat rig-etat--attente')
  })
})
