import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { BarreRig } from './BarreRig'
import { ThemeRig } from './ThemeRig'
import { morceaux } from '../test/morceaux'
import { vueBarre } from '../test/accueil'
import type { GesteIle, VueBarre } from '../accueil/types'

afterEach(cleanup)

function rendre(vue: VueBarre = vueBarre()) {
  const geste = vi.fn<(g: GesteIle) => void>()
  const r = render(<BarreRig vue={vue} geste={geste} />)
  return { ...r, geste, gestes: () => geste.mock.calls.map(([g]) => [g.nom, g.valeur]) }
}

describe('BarreRig (la barre du haut de la fenetre)', () => {
  it('marque, version, reglages et boutons de fenetre, avec les blancs de Razor', () => {
    const { container } = rendre()
    const barre = container.querySelector('header.rig-barre')!
    expect(morceaux(barre)).toEqual(['<span>', ' ', '<span>', ' ', '<div>'])
    expect(morceaux(barre.querySelector('.rig-barre__marque'))).toEqual(['RIG ', '<b>'])
    expect(barre.querySelector('.rig-barre__version')!.textContent).toBe('3.0.14')
    const droite = barre.querySelector('.rig-barre__droite')!
    expect(morceaux(droite)).toEqual(['<div>', ' ', '<div>'])
    expect(morceaux(droite.querySelector('.rig-fenetre'))).toEqual(['<button>', ' ', '<button>', ' ', '<button>'])
    expect([...droite.querySelectorAll('.rig-fenetre__bouton')].map((b) => [b.getAttribute('title'), b.textContent]))
      .toEqual([['Reduire', '–'], ['Agrandir', '□'], ['Fermer', '✕']])
    expect(droite.querySelector('.rig-barre__action')!.className).toBe('rig-barre__action ')
    expect(droite.querySelector('.rig-barre__action')!.textContent).toBe('⚙')
  })

  it('un appui sur la barre DEPLACE la fenetre ; les boutons, eux, ne la deplacent pas', () => {
    const { container, gestes } = rendre()
    fireEvent.mouseDown(container.querySelector('.rig-barre__marque')!)
    for (const b of container.querySelectorAll('.rig-fenetre__bouton')) { fireEvent.mouseDown(b); fireEvent.click(b) }
    fireEvent.mouseDown(container.querySelector('.rig-barre__action')!)
    expect(gestes()).toEqual([['deplacer', null], ['reduire', null], ['agrandir', null], ['fermer', null]])
  })

  it('Reglages > Affichage : la liste des themes ; le choix part a la session', () => {
    const { container, gestes } = rendre(vueBarre({ reglages: true, theme: 'clair' }))
    expect(container.querySelector('.rig-barre__action')!.className).toBe('rig-barre__action rig-barre__action--ouvert')
    const panneau = container.querySelector('.rig-tuiles-panneau')!
    expect(morceaux(panneau)).toEqual(['<div>', ' ', '<label>'])
    const liste = panneau.querySelector<HTMLSelectElement>('select#rig-choix-theme')!
    expect(liste.className).toBe('rig-saisie-champ__saisie')
    expect([...liste.options].map((o) => [o.value, o.textContent])).toEqual([['clair', 'Clair'], ['sombre', 'Sombre']])
    expect(liste.value).toBe('clair')
    expect(container.textContent).not.toContain('Mode sombre')
    fireEvent.change(liste, { target: { value: 'sombre' } })
    fireEvent.click(container.querySelector('.rig-voile-clic')!)
    expect(gestes()).toEqual([['theme', 'sombre'], ['fermerPanneauBarre', null]])
  })

  it('la liste garde le choix de l\'utilisateur jusqu\'a la reponse, puis suit la session', () => {
    const geste = vi.fn()
    const { container, rerender } = render(<BarreRig vue={vueBarre({ reglages: true })} geste={geste} />)
    const liste = container.querySelector<HTMLSelectElement>('#rig-choix-theme')!
    fireEvent.change(liste, { target: { value: 'sombre' } })
    rerender(<BarreRig vue={vueBarre({ reglages: true })} geste={geste} />)
    expect(liste.value).toBe('sombre')
    rerender(<BarreRig vue={vueBarre({ reglages: true, theme: 'sombre' })} geste={geste} />)
    rerender(<BarreRig vue={vueBarre({ reglages: true, theme: 'clair' })} geste={geste} />)
    expect(liste.value).toBe('clair')
  })

  it('un ecran ouvert recouvre l\'accueil : le rond de profil passe DANS la barre', () => {
    const { container, gestes } = rendre(vueBarre({
      profil: { dansLaBarre: true, ouvert: false, initiales: 'RV', titre: 'rvilain · Greffier (RCS) : changer de login', login: 'rvilain', comptes: [] },
    }))
    const droite = container.querySelector('.rig-barre__droite')!
    expect(droite.firstElementChild!.className).toBe('rig-profil-ancre rig-profil-ancre--barre')
    expect(morceaux(droite)).toEqual(['<div>', '<div>', ' ', '<div>'])
    const rond = droite.querySelector('.rig-profil')!
    expect(rond.textContent).toBe('RV')
    expect(rond.getAttribute('aria-expanded')).toBe('false')
    fireEvent.mouseDown(rond)
    fireEvent.click(rond)
    expect(gestes()).toEqual([['profil', null]])
  })
})

describe('ThemeRig (data-rig-theme sur <html>)', () => {
  it('pose le theme choisi sur la page entiere, et le suit', () => {
    const { rerender, container } = render(<ThemeRig theme="sombre" />)
    expect(container.innerHTML).toBe('')
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('sombre')
    rerender(<ThemeRig theme="argile" />)
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('argile')
  })
})
