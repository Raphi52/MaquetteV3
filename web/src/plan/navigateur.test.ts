import { afterEach, describe, expect, it, vi } from 'vitest'
import { ecouterEchap } from './clavier'
import { ecouterGlisse, finirSuivi, mesurerOnglets } from './glisse'
import { allerEtape, suivrePagination } from './pagination'

// Ce que seul le navigateur sait faire (ex-wwwroot/rig-accueil.js, retire au
// lot 2) : le clavier, le glisser d'un onglet, la pagination des etapes.

afterEach(() => { document.body.innerHTML = '' })

function rect(left: number, right: number, top = 0, bottom = 30): DOMRect {
  return { left, right, top, bottom, width: right - left, height: bottom - top, x: left, y: top, toJSON: () => ({}) } as DOMRect
}

describe('clavier : Echap', () => {
  it('Echap, et lui seul, est remonte ; l\'ecoute se retire', () => {
    const echap = vi.fn()
    const retirer = ecouterEchap(echap)
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }))
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    retirer()
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(echap).toHaveBeenCalledTimes(1)
  })
})

describe('glisser d\'un onglet', () => {
  function barre() {
    document.body.innerHTML = '<div id="rig-vonglets"><div data-rig-onglet="A"><span>A</span><button class="rig-vonglet__fermer">x</button></div><div data-rig-onglet="B">B</div></div>'
    const [a, b] = document.querySelectorAll<HTMLElement>('[data-rig-onglet]')
    a!.getBoundingClientRect = () => rect(10, 100)
    b!.getBoundingClientRect = () => rect(110, 200)
    document.getElementById('rig-vonglets')!.getBoundingClientRect = () => rect(0, 1500, 0, 36)
    return { a: a!, b: b! }
  }

  it('mesure la zone visible, le bas de la barre et la place de chaque onglet', () => {
    barre()
    const m = mesurerOnglets()
    expect(m.cases).toEqual([[10, 100], [110, 200]])
    expect(m.page).toHaveLength(3)
    expect(m.page[2]).toBe(36)
  })

  it('l\'onglet suit le pointeur au-dela de 6 px, et reprend sa place au lacher ou a l\'arrachage', () => {
    const { a } = barre()
    a.setPointerCapture = vi.fn()
    const retirer = ecouterGlisse()
    a.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, clientX: 20, clientY: 10 }))
    expect(a.setPointerCapture).toHaveBeenCalled()
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 23, clientY: 10 }))
    expect(a.style.transform).toBe('')
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 60, clientY: 10 }))
    expect(a.style.transform).toBe('translateX(40px)')
    expect(a.classList.contains('rig-vonglet--suit')).toBe(true)
    finirSuivi()
    expect(a.style.transform).toBe('')
    expect(a.classList.contains('rig-vonglet--suit')).toBe(false)
    retirer()
  })

  it('la croix n\'est pas un debut de glisser', () => {
    const { a } = barre()
    a.setPointerCapture = vi.fn()
    const retirer = ecouterGlisse()
    a.querySelector('button')!.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, clientX: 90, clientY: 10 }))
    document.dispatchEvent(new MouseEvent('pointermove', { clientX: 200, clientY: 10 }))
    expect(a.setPointerCapture).not.toHaveBeenCalled()
    expect(a.style.transform).toBe('')
    retirer()
  })
})

describe('pagination des etapes', () => {
  function zone(scrollLeft: number) {
    document.body.innerHTML = '<div id="z"><section data-etape="0"></section><section data-etape="1"></section><section data-etape="2"></section></div>'
    const z = document.getElementById('z')!
    Object.defineProperty(z, 'clientWidth', { value: 500 })
    z.scrollLeft = scrollLeft
    z.scrollTo = vi.fn() as unknown as HTMLElement['scrollTo']
    return z
  }

  it('dit l\'etape visible des le branchement (arrondie a la page la plus proche)', () => {
    const z = zone(760)
    const vue = vi.fn()
    const arreter = suivrePagination(z, vue)
    expect(vue).toHaveBeenCalledWith(2)
    arreter()
  })

  it('aller a une etape fait defiler d\'une largeur de page par etape, en douceur ou d\'un coup', () => {
    const z = zone(0)
    allerEtape(z, 2)
    allerEtape(z, 1, false)
    expect(z.scrollTo).toHaveBeenNthCalledWith(1, { left: 1000, behavior: 'smooth' })
    expect(z.scrollTo).toHaveBeenNthCalledWith(2, { left: 500, behavior: 'auto' })
    allerEtape(null, 1)
  })

  it('la molette tourne UNE page par geste ; Ctrl+molette reste au zoom', () => {
    const z = zone(500)
    const arreter = suivrePagination(z, vi.fn())
    z.dispatchEvent(new WheelEvent('wheel', { deltaY: 100, ctrlKey: true, cancelable: true }))
    expect(z.scrollTo).not.toHaveBeenCalled()
    const ev = new WheelEvent('wheel', { deltaY: 100, cancelable: true })
    z.dispatchEvent(ev)
    z.dispatchEvent(new WheelEvent('wheel', { deltaY: 100, cancelable: true }))
    expect(ev.defaultPrevented).toBe(true)
    expect(z.scrollTo).toHaveBeenCalledTimes(1)
    expect(z.scrollTo).toHaveBeenCalledWith({ left: 1000, behavior: 'smooth' })
    arreter()
  })
})
