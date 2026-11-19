import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { ArbreGedNoeud, type LigneArbreVue } from './ArbreGedNoeud'
import { morceaux } from '../test/morceaux'

afterEach(cleanup)

// Les regles (enfants visibles, filtre, ouverture, chemin, repli) vivent dans
// Pont/ArbreGedNoeud.cs, executees par tests/GedCheck et tests/RecetteArbreCheck.
// Ici : le balisage de chaque ligne, la recursion, et les gestes remontes.
function ligne(id: string, texte: string, plus: Partial<LigneArbreVue> = {}): LigneArbreVue {
  return {
    id, niveau: 0, texte, icone: 'dossier', actif: false, chemin: false, depliable: false, ouvert: false,
    renomme: false, compte: null, enfants: [], vide: false, ...plus,
  }
}

const piece = ligne('n3', 'Kbis a jour', { niveau: 2, icone: 'kbis', actif: true })
const rcs = ligne('n2', 'Dossier RCS', { niveau: 1, depliable: true, ouvert: true, compte: 21, chemin: true, enfants: [piece] })
const vide = ligne('n4', 'Pieces jointes', { niveau: 1, ouvert: true, vide: true })
const racine = ligne('n1', '004176 Modification', { depliable: true, ouvert: true, compte: 44, chemin: true, enfants: [rcs, vide] })

function rendre(l = racine) {
  const gestes = { activer: vi.fn(), renommer: vi.fn(), menuContextuel: vi.fn() }
  const r = render(<ArbreGedNoeud ligne={l} {...gestes} />)
  return { ...r, ...gestes }
}

describe('ArbreGedNoeud', () => {
  it('rend les lignes A PLAT, dans l\'ordre de l\'arbre, en s\'appelant pour chaque enfant ouvert', () => {
    const { container } = rendre()
    const lignes = [...container.children].map((e) => e.className.trim())
    expect(lignes).toEqual([
      'rig-arbre__ligne  rig-arbre__ligne--chemin',
      'rig-arbre__ligne  rig-arbre__ligne--chemin',
      'rig-arbre__ligne rig-arbre__ligne--actif',
      'rig-arbre__ligne',
      'rig-arbre__vide',
    ])
    expect([...container.querySelectorAll('.rig-arbre__texte')].map((e) => e.textContent))
      .toEqual(['004176 Modification', 'Dossier RCS', 'Kbis a jour', 'Pieces jointes'])
  })

  it('porte le niveau en variable CSS, le chevron, l\'icone et le compte en trois morceaux', () => {
    const { container } = rendre()
    const [l0, , l2, l3] = [...container.querySelectorAll('.rig-arbre__ligne')] as HTMLElement[]
    expect(l0!.getAttribute('style')).toBe('--rig-arbre-niveau: 0;')
    expect(l2!.getAttribute('style')).toBe('--rig-arbre-niveau: 2;')
    expect(l0!.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron rig-arbre__chevron--ouvert')
    expect(l0!.querySelector('.rig-arbre__chevron')!.textContent).toBe('▸')
    expect(l2!.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron rig-arbre__chevron--vide')
    expect(l2!.querySelector('svg')!.getAttribute('class')).toBe('rig-arbre__icone')
    expect(morceaux(l0!.querySelector('.rig-arbre__compte'))).toEqual(['(', '44', ')'])
    expect(l3!.querySelector('.rig-arbre__compte')).toBeNull()
    const libelle = l0!.querySelector('.rig-arbre__libelle')!
    expect(morceaux(libelle)).toEqual(['<span>', '<svg>', '<span>', '<span>'])
  })

  it('un dossier ouvert sans enfant montre « aucune piece » un niveau plus bas', () => {
    const { container } = rendre()
    const v = container.querySelector('.rig-arbre__vide')!
    expect(v.textContent).toBe('aucune piece')
    expect(v.getAttribute('style')).toBe('--rig-arbre-niveau: 2;')
  })

  it('une branche fermee ne rend pas ses enfants', () => {
    const { container } = rendre({ ...racine, ouvert: false })
    expect(container.querySelectorAll('.rig-arbre__ligne')).toHaveLength(1)
    expect(container.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron ')
  })

  it('un clic sur une ligne remonte SON identifiant (le repli se decide dans le pont)', () => {
    const { container, activer } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-arbre__ligne')[2]!)
    expect(activer).toHaveBeenCalledWith('n3')
  })

  it('les trois points ouvrent le menu a la souris, SANS activer la ligne', () => {
    const { container, activer, menuContextuel } = rendre()
    const bouton = container.querySelectorAll('.rig-arbre__actions')[1] as HTMLButtonElement
    expect(bouton.getAttribute('aria-label')).toBe('Actions sur Dossier RCS')
    expect(bouton.getAttribute('title')).toBe('Actions')
    expect(bouton.textContent).toBe('⋮')
    fireEvent.click(bouton, { clientX: 120, clientY: 340 })
    expect(menuContextuel).toHaveBeenCalledWith(expect.objectContaining({ id: 'n2', clientX: 120, clientY: 340 }))
    expect(activer).not.toHaveBeenCalled()
  })

  it('le renommage SUR PLACE : champ prerempli, valide au « change », Echap abandonne', () => {
    const { container, renommer, activer } = rendre({ ...piece, renomme: true })
    const champ = container.querySelector('input.rig-arbre__saisie') as HTMLInputElement
    expect(champ.value).toBe('Kbis a jour')
    expect(document.activeElement).toBe(champ)
    fireEvent.click(champ)
    expect(activer).not.toHaveBeenCalled()
    champ.value = 'Kbis 2026'
    fireEvent.change(champ)
    expect(renommer).toHaveBeenLastCalledWith({ id: 'n3', texte: 'Kbis 2026' })
    fireEvent.keyDown(champ, { key: 'Escape' })
    expect(renommer).toHaveBeenLastCalledWith({ id: 'n3', texte: '' })
    expect(container.querySelector('.rig-arbre__texte')).toBeNull()
  })
})
