import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { EnteteTriable } from './EnteteTriable'

afterEach(cleanup)

function rendre(props: Partial<Parameters<typeof EnteteTriable>[0]> = {}) {
  const trier = vi.fn()
  const { container } = render(
    <table><thead><tr>
      <EnteteTriable libelle="N° Affaire" cle="Numero" ascendant={true} trier={trier} {...props} />
    </tr></thead></table>,
  )
  return { th: container.querySelector('th')!, trier }
}

// Memes regles que Components/EnteteTriable.razor (TriCheck le garde cote Blazor).
describe('EnteteTriable', () => {
  it('porte le sens par aria-sort : none tant que la colonne n\'est pas active', () => {
    const { th } = rendre({ cleActive: 'Autre' })
    expect(th.getAttribute('aria-sort')).toBe('none')
    expect(th.querySelector('.rig-th__fleche')!.textContent).toBe('')
  })

  it('ascending et ▲ sur la colonne active montante', () => {
    const { th } = rendre({ cleActive: 'Numero', ascendant: true })
    expect(th.getAttribute('aria-sort')).toBe('ascending')
    expect(th.querySelector('.rig-th__fleche')!.textContent).toBe('▲')
  })

  it('descending et ▼ sur la colonne active descendante', () => {
    const { th } = rendre({ cleActive: 'Numero', ascendant: false })
    expect(th.getAttribute('aria-sort')).toBe('descending')
    expect(th.querySelector('.rig-th__fleche')!.textContent).toBe('▼')
  })

  it('garde les classes du .razor, la classe passee en tete', () => {
    expect(rendre().th.className).toBe('rig-th--triable')
    expect(rendre({ classe: 'rig-table__centre' }).th.className).toBe('rig-table__centre rig-th--triable')
  })

  it('nomme le tri dans l\'infobulle, libelle en clair', () => {
    const { th } = rendre()
    expect(th.title).toBe('Trier par N° Affaire')
    expect(th.firstChild?.textContent).toBe('N° Affaire')
  })

  it('remonte la CLE de la colonne au clic', () => {
    const { th, trier } = rendre()
    fireEvent.click(th)
    expect(trier).toHaveBeenCalledWith('Numero')
  })
})
