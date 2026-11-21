import { describe, expect, it } from 'vitest'
import { TOUCHES_RECHERCHE, toucheDeRecherche } from './clavier'

// Les touches qui lancent la recherche dans un critere (SOC-027, decision du chef du plan, 21/11/2026) : Entree, F5 et F7,
// les touches de l'ancien RIG. La fenetre ne les prend plus pour elle (Pont/PageWeb.cs) ; la page les traite ici.

const touche = (key: string) => new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })

describe('clavier.ts : les touches de la recherche', () => {
  it('Entree, F5 et F7 lancent la recherche', () => {
    expect([...TOUCHES_RECHERCHE].sort()).toEqual(['Enter', 'F5', 'F7'])
    for (const key of ['Enter', 'F5', 'F7']) expect(toucheDeRecherche(touche(key))).toBe(true)
  })

  it('F5 et F7 sont marquees traitees (rien d\'autre ne les reprend) ; Entree reste libre, comme avant', () => {
    const f5 = touche('F5')
    const f7 = touche('F7')
    const entree = touche('Enter')
    toucheDeRecherche(f5)
    toucheDeRecherche(f7)
    toucheDeRecherche(entree)
    expect([f5.defaultPrevented, f7.defaultPrevented, entree.defaultPrevented]).toEqual([true, true, false])
  })

  it('les autres touches ne lancent rien et ne sont pas touchees (jumeau : F6 et F8 a cote de F5 et F7)', () => {
    for (const key of ['F6', 'F8', 'F4', 'a', 'Escape', 'Tab']) {
      const e = touche(key)
      expect(toucheDeRecherche(e)).toBe(false)
      expect(e.defaultPrevented).toBe(false)
    }
  })
})
