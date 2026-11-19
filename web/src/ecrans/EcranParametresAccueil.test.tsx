import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranParametresAccueil } from './EcranParametresAccueil'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranParametresAccueil.razor) : 3 imprimantes
// cochees sur 4, une ligne sur deux coloriee, tri « Date de creation (Descendant) ».
const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

describe('EcranParametresAccueil', () => {
  it('affiche les reglages de depart et l\'apercu', () => {
    const { container } = render(<EcranParametresAccueil />)
    expect(compte(container)).toBe('3 imprimante(s) cochee(s)')
    expect(screen.getByRole('button', { name: 'Animations' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(screen.getByRole('button', { name: 'Ouvrir en plein ecran' }).className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Greffe - Accueil (recommande)' })).toBeTruthy()
    expect((screen.getByLabelText('Interval de defilement (secondes)') as HTMLInputElement).value).toBe('8')
    expect(container.querySelector('thead th')!.textContent).toBe('Date de creation (Descendant)')
    const lignes = [...container.querySelectorAll('tbody tr')] as HTMLElement[]
    expect(lignes).toHaveLength(4)
    expect(lignes.map((l) => l.style.background !== '')).toEqual([false, true, false, true])
  })

  it('cocher une imprimante, changer le tri, eteindre le coloriage', () => {
    const { container } = render(<EcranParametresAccueil />)
    fireEvent.click(screen.getByRole('button', { name: /^Greffe - Archives/ }))
    expect(compte(container)).toBe('4 imprimante(s) cochee(s)')
    fireEvent.change(screen.getByLabelText('Sens du tri'), { target: { value: 'Ascendant' } })
    fireEvent.change(screen.getByLabelText('Colonne de tri des alertes'), { target: { value: 'Client' } })
    expect(container.querySelector('thead th')!.textContent).toBe('Client (Ascendant)')
    fireEvent.click(screen.getByRole('button', { name: 'Colorier une ligne sur deux' }))
    expect(([...container.querySelectorAll('tbody tr')] as HTMLElement[]).every((l) => l.style.background === '')).toBe(true)
  })
})
