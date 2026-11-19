import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { BarreRig } from './BarreRig'
import { ThemeRig } from './ThemeRig'
import { morceaux } from '../test/morceaux'
import type { GesteIle } from '../accueil/types'
import type { OngletBarreVue } from '../plan/types'

afterEach(cleanup)

const onglets: OngletBarreVue[] = [
  { code: 'PROC_FACTURE', libelle: 'Factures', actif: false, detache: false, glisse: false, verbe: null },
  { code: 'PROC_ENCAISS', libelle: 'Encaissements', actif: true, detache: false, glisse: false, verbe: null },
]

function rendre(ouverts: OngletBarreVue[] | null = onglets, accueil = false) {
  const geste = vi.fn<(g: GesteIle) => void>()
  const plan = vi.fn()
  const r = render(<BarreRig onglets={ouverts} finsDeGlisse={0} accueil={accueil} geste={geste} plan={plan} />)
  return {
    ...r, geste, plan,
    gestes: () => geste.mock.calls.map(([g]) => [g.nom, g.valeur]),
    plans: () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur]),
  }
}

describe('BarreRig (la barre du haut, « comme Brave »)', () => {
  it('la maison, les onglets, le « + » puis les boutons de la fenetre : rien d\'autre', () => {
    const { container } = rendre()
    const barre = container.querySelector('header.rig-barre')!
    expect(morceaux(barre)).toEqual(['<button>', '<div>', '<button>', '<div>'])
    const maison = barre.firstElementChild!
    expect(maison.getAttribute('title')).toBe('Accueil')
    expect(maison.className).toBe('rig-vonglet rig-vonglet--maison')
    expect(maison.querySelector('svg')).not.toBeNull()
    expect(maison.hasAttribute('data-rig-onglet')).toBe(false)
    expect(maison.nextElementSibling!.id).toBe('rig-vonglets')
    expect([...barre.querySelectorAll('.rig-vonglet__titre')].map((t) => t.textContent)).toEqual(['Factures', 'Encaissements'])
    const fenetre = barre.querySelector('.rig-fenetre')!
    expect(barre.lastElementChild).toBe(fenetre)
    expect([...fenetre.querySelectorAll('.rig-fenetre__bouton')].map((b) => [b.getAttribute('title'), b.textContent]))
      .toEqual([['Reduire', '–'], ['Agrandir', '□'], ['Fermer', '✕']])
    // Ce que la barre portait avant le 19/11/2026 n'y est plus.
    expect(barre.textContent).not.toContain('RIG')
    expect(barre.querySelector('select, .rig-profil, .rig-tuiles-panneau')).toBeNull()
  })

  it('sans vue ouverte : la barre ne garde que la maison, le « + » et les boutons de la fenetre', () => {
    const { container } = rendre(null)
    const barre = container.querySelector('header.rig-barre')!
    expect(morceaux(barre)).toEqual(['<button>', '<button>', '<div>'])
    expect(barre.querySelector('#rig-vonglets')).toBeNull()
  })

  it('le « + » au bout des onglets, comme dans Brave : il ouvre l\'accueil sans deplacer la fenetre', () => {
    const { container, gestes, plans } = rendre()
    const plus = container.querySelector('.rig-vonglet--plus')!
    expect(plus.previousElementSibling!.id).toBe('rig-vonglets')
    // La forme d'un onglet (comme la maison), pas un rond ; mais il ne se glisse pas.
    expect(plus.className).toBe('rig-vonglet rig-vonglet--plus')
    expect(plus.hasAttribute('data-rig-onglet')).toBe(false)
    expect(plus.getAttribute('aria-label')).toBe('Nouvel onglet')
    expect(plus.querySelector('svg')).not.toBeNull()
    fireEvent.mouseDown(plus)
    fireEvent.click(plus)
    expect(gestes()).toEqual([])
    expect(plans()).toEqual([['accueil', undefined]])
  })

  it('la maison est l\'onglet actif quand l\'accueil se voit ; son clic y ramene sans deplacer la fenetre', () => {
    const actif = rendre(onglets, true)
    expect(actif.container.querySelector('.rig-vonglet--maison')!.className).toBe('rig-vonglet rig-vonglet--maison rig-vonglet--actif')
    expect(actif.container.querySelector('.rig-vonglet--maison')!.getAttribute('aria-current')).toBe('page')
    cleanup()
    const { container, gestes, plans } = rendre()
    const maison = container.querySelector('.rig-vonglet--maison')!
    expect(maison.hasAttribute('aria-current')).toBe(false)
    fireEvent.mouseDown(maison)
    fireEvent.click(maison)
    expect(gestes()).toEqual([])
    expect(plans()).toEqual([['accueil', undefined]])
  })

  it('un appui HORS d\'un onglet ou d\'un bouton deplace la fenetre ; onglets et boutons, non', () => {
    const { container, gestes, plans } = rendre()
    fireEvent.mouseDown(container.querySelector('header.rig-barre')!)
    fireEvent.mouseDown(container.querySelector('#rig-vonglets')!)
    fireEvent.mouseDown(container.querySelector('.rig-fenetre')!)
    const onglet = container.querySelector('[data-rig-onglet="PROC_FACTURE"]')!
    fireEvent.mouseDown(onglet)
    fireEvent.click(onglet)
    for (const b of container.querySelectorAll('.rig-fenetre__bouton')) { fireEvent.mouseDown(b); fireEvent.click(b) }
    expect(gestes()).toEqual([['deplacer', null], ['deplacer', null], ['deplacer', null],
                              ['reduire', null], ['agrandir', null], ['fermer', null]])
    expect(plans()).toEqual([['ongletActiver', 'PROC_FACTURE']])
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
