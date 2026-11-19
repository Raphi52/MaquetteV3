import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { useRef } from 'react'
import { PoigneeColonne } from './PoigneeColonne'

afterEach(cleanup)

/** jsdom ne mesure rien : chaque boite rend la largeur qu'on lui donne. */
function largeur(el: Element, px: number) {
  el.getBoundingClientRect = () => ({ width: px, height: 0, top: 0, left: 0, right: px, bottom: 0, x: 0, y: 0, toJSON: () => ({}) })
}

function Scene({ changer }: { changer: (l: number | null) => void }) {
  const colonne = useRef<HTMLDivElement>(null)
  return (
    <div data-testid="scene">
      <div data-testid="colonne" ref={colonne}></div>
      <PoigneeColonne className="p" libelle="Largeur de la liste" colonne={colonne} min={210} resteMin={420} changer={changer} />
      <div></div>
    </div>
  )
}

/** Scene de 1000 px : colonne de 260 px, poignee de 8 px ; la colonne peut aller de 210 a 1000 - 8 - 420 = 572 px. */
function rendre() {
  const changer = vi.fn<(l: number | null) => void>()
  const r = render(<Scene changer={changer} />)
  largeur(r.getByTestId('scene'), 1000)
  largeur(r.getByTestId('colonne'), 260)
  const poignee = r.getByRole('separator')
  largeur(poignee, 8)
  return { changer, poignee }
}

describe('PoigneeColonne', () => {
  it('se presente comme un separateur vertical, focusable, qui dit son double-clic', () => {
    const { poignee } = rendre()
    expect(poignee.getAttribute('aria-orientation')).toBe('vertical')
    expect(poignee.getAttribute('aria-label')).toBe('Largeur de la liste')
    expect(poignee.tabIndex).toBe(0)
    expect(poignee.title).toBe("Largeur de la liste — double-clic : largeur d'origine")
  })

  it('glisser : la largeur de depart plus le deplacement, bornee entre le minimum et la place laissee a la suite', () => {
    const { changer, poignee } = rendre()
    fireEvent.pointerDown(poignee, { clientX: 300, button: 0, pointerId: 1 })
    fireEvent.pointerMove(poignee, { clientX: 380 })
    fireEvent.pointerMove(poignee, { clientX: 0 })
    fireEvent.pointerMove(poignee, { clientX: 2000 })
    fireEvent.pointerUp(poignee)
    fireEvent.pointerMove(poignee, { clientX: 500 })
    expect(changer.mock.calls).toEqual([[340], [210], [572]])
  })

  it('sans appui, ou avec un autre bouton que le gauche, le pointeur ne deplace rien', () => {
    const { changer, poignee } = rendre()
    fireEvent.pointerMove(poignee, { clientX: 400 })
    fireEvent.pointerDown(poignee, { clientX: 300, button: 2, pointerId: 1 })
    fireEvent.pointerMove(poignee, { clientX: 400 })
    expect(changer).not.toHaveBeenCalled()
  })

  it('double-clic : retour a la largeur d\'origine ; fleches : 16 px par appui', () => {
    const { changer, poignee } = rendre()
    fireEvent.doubleClick(poignee)
    fireEvent.keyDown(poignee, { key: 'ArrowRight' })
    fireEvent.keyDown(poignee, { key: 'ArrowLeft' })
    fireEvent.keyDown(poignee, { key: 'Enter' })
    expect(changer.mock.calls).toEqual([[null], [276], [244]])
  })
})
