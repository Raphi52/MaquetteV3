import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render } from '@testing-library/react'
import { IconeGed, dessinGed } from './IconeGed'
import { IconeNouveaute, dessinNouveaute } from './IconeNouveaute'

afterEach(cleanup)

// Les natures de AccesGed.Natures (Metier/ArbreGed.cs) qui ont LEUR dessin ;
// « pdf » prend la feuille, le dessin par defaut (tests/GedCheck relit la liste
// sur le C# et verifie chaque nature dans IconeGed.tsx).
const natures = ['dossier', 'sous-dossier', 'jpg', 'tif', 'courrier', 'note', 'ordonnance',
                 'assignation', 'statuts', 'kbis', 'identite', 'facture', 'pv']

describe('IconeGed', () => {
  it('rend le meme <svg> que le .razor : trait currentColor, classe par defaut rig-icone', () => {
    const { container } = render(<IconeGed nature="dossier" />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('class')).toBe('rig-icone')
    expect(svg.getAttribute('viewBox')).toBe('0 0 24 24')
    expect(svg.getAttribute('fill')).toBe('none')
    expect(svg.getAttribute('stroke')).toBe('currentColor')
    expect(svg.getAttribute('stroke-width')).toBe('1.4')
    expect(svg.getAttribute('stroke-linecap')).toBe('round')
    expect(svg.getAttribute('stroke-linejoin')).toBe('round')
    expect(svg.getAttribute('aria-hidden')).toBe('true')
    expect(svg.getAttribute('focusable')).toBe('false')
    expect(svg.querySelectorAll('path')).toHaveLength(1)
  })

  it('chaque nature a SON dessin, distinct des autres ; une nature inconnue prend la feuille', () => {
    const dessins = natures.map(dessinGed)
    expect(new Set(dessins).size).toBe(natures.length)
    expect(dessinGed('pdf')).toBe(dessinGed('n-importe-quoi'))
    expect(natures.map(dessinGed)).not.toContain(dessinGed('pdf'))
  })

  it('pose la classe demandee (l\'arbre : rig-arbre__icone, la fenetre de creation : rig-nature__icone)', () => {
    const { container } = render(<IconeGed nature="kbis" classe="rig-arbre__icone" />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('class')).toBe('rig-arbre__icone')
    expect(svg.querySelectorAll('rect, circle, path')).toHaveLength(5)
  })

  it('aucune ressource externe dans les dessins', () => {
    for (const n of natures) expect(dessinGed(n)).not.toMatch(/http|url\(/)
  })
})

describe('IconeNouveaute', () => {
  it('trace au trait 1.6, classe par defaut rig-nouv__icone', () => {
    const { container } = render(<IconeNouveaute nom="boussole" />)
    const svg = container.querySelector('svg')!
    expect(svg.getAttribute('class')).toBe('rig-nouv__icone')
    expect(svg.getAttribute('stroke')).toBe('currentColor')
    expect(svg.getAttribute('stroke-width')).toBe('1.6')
    expect(svg.querySelectorAll('circle')).toHaveLength(1)
    expect(svg.querySelectorAll('path')).toHaveLength(1)
  })

  it('les quatre icones de l\'accueil sont dessinees ; un nom inconnu ne dessine rien', () => {
    for (const nom of ['boussole', 'profil', 'megaphone', 'clair-sombre']) expect(dessinNouveaute(nom)).not.toBe('')
    expect(dessinNouveaute('inconnu')).toBe('')
    const { container } = render(<IconeNouveaute nom="inconnu" classe="x" />)
    expect(container.querySelector('svg')!.childNodes).toHaveLength(0)
  })

  it('« clair-sombre » remplit sa moitie nuit en currentColor', () => {
    const { container } = render(<IconeNouveaute nom="clair-sombre" />)
    const plein = container.querySelector('path[fill="currentColor"]')!
    expect(plein.getAttribute('stroke')).toBe('none')
  })
})
