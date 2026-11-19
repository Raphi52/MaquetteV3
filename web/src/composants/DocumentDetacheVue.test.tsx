import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { DocumentDetacheVue, zoomMoins, zoomPlus, type PieceDetacheeVue } from './DocumentDetacheVue'
import { morceaux } from '../test/morceaux'

afterEach(cleanup)

// La piece et la decoupe en pages viennent de Pont/DocumentDetacheVue.cs
// (DocumentPartage.TextePage, une seule decoupe). Ici : l'apercu de la fenetre
// detachee et son objectif PROPRE (zoom), tenu par ce composant seul.
const piece: PieceDetacheeVue = {
  extension: 'PDF', pages: 2, date: '19/11/2026', libelle: 'Kbis a jour', aPdf: false, fichier: '',
  valeurs: [{ libelle: 'SIREN', valeur: '402 118 447' }, { libelle: 'Civilite', valeur: 'M.' }],
  feuilles: [{ numero: 1, texte: 'Kbis a jour\n\n(page 1)' }, { numero: 2, texte: '(page 2 - suite du document)' }],
}

describe('DocumentDetacheVue', () => {
  it('sans piece : le guide « Aucune piece ouverte », dans le theme sombre plein ecran', () => {
    const { container } = render(<DocumentDetacheVue piece={null} />)
    const fond = container.firstElementChild as HTMLElement
    expect(fond.getAttribute('data-rig-theme')).toBe('sombre')
    expect(fond.getAttribute('style')).toBe('background: var(--rig-fond); color: var(--rig-texte); height: 100vh; overflow: auto;')
    expect(screen.getByText('Aucune piece ouverte').className).toBe('rig-guide__titre')
    expect(container.querySelector('.rig-guide__source')!.textContent)
      .toBe("Cliquez une piece dans l'arbre de la fenetre principale.")
  })

  it('une piece : entete en morceaux Razor, feuilles, valeurs sur la 1re seulement, folios', () => {
    const { container } = render(<DocumentDetacheVue piece={piece} />)
    expect(morceaux(container.querySelector('.rig-apercu__entete .rig-kicker')))
      .toEqual(['PDF', ' · ', '2', ' page(s) · ', '19/11/2026'])
    expect(container.querySelector('.rig-apercu__titre')!.textContent).toBe('Kbis a jour')
    const feuilles = container.querySelectorAll('.rig-apercu__feuille')
    expect(feuilles).toHaveLength(2)
    expect(morceaux(feuilles[0]!.querySelector('dl.rig-apercu__saisie'))).toEqual(['<dt>', '<dd>', '<dt>', '<dd>'])
    expect(feuilles[1]!.querySelector('dl')).toBeNull()
    expect(feuilles[1]!.querySelector('.rig-apercu__feuille-texte')!.textContent).toBe('(page 2 - suite du document)')
    expect(morceaux(feuilles[1]!.querySelector('.rig-apercu__folio'))).toEqual(['2', ' / ', '2'])
  })

  it('l\'objectif : + et - par pas de 0,25 entre 0,5 et 4, applique aux pages, 100 % remet a 1', () => {
    const { container } = render(<DocumentDetacheVue piece={piece} />)
    const pages = container.querySelector('.rig-apercu__pages') as HTMLElement
    const taux = container.querySelector('.rig-zoom__taux')!
    expect(morceaux(taux)).toEqual(['100', ' %'])
    expect(pages.style.zoom).toBe('1')
    fireEvent.click(screen.getByText('+'))
    expect(taux.textContent).toBe('125 %')
    expect(pages.style.zoom).toBe('1.25')
    fireEvent.click(screen.getByText('−'))
    fireEvent.click(screen.getByText('−'))
    fireEvent.click(screen.getByText('−'))
    expect(taux.textContent).toBe('50 %')
    fireEvent.click(screen.getByText('100 %'))
    expect(pages.style.zoom).toBe('1')
    expect(zoomPlus(4)).toBe(4)
    expect(zoomMoins(0.5)).toBe(0.5)
    expect(zoomPlus(3.9)).toBe(4)
  })

  it('un vrai PDF s\'affiche dans un cadre, sans feuilles ni objectif applique', () => {
    const { container } = render(<DocumentDetacheVue piece={{ ...piece, aPdf: true, fichier: 'documents/kbis.pdf' }} />)
    const cadre = container.querySelector('.rig-apercu__pages--pdf iframe.rig-apercu__pdf')!
    expect(cadre.getAttribute('src')).toBe('documents/kbis.pdf#view=FitH')
    expect(cadre.getAttribute('title')).toBe('Kbis a jour')
    expect(container.querySelector('.rig-apercu__feuille')).toBeNull()
  })
})
