import { afterEach, describe, expect, it, vi } from 'vitest'
import { ecouterEchap } from './clavier'
import { ecouterGlisse, finirSuivi, mesurerOnglets } from './glisse'
import { allerEtape, etapeEnHaut, etapeSuivie, suivreEtapeVisible } from './pagination'

// Ce que seul le navigateur sait faire (ex-wwwroot/rig-accueil.js, retire au
// lot 2) : le clavier, le glisser d'un onglet, le defilement des etapes.

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

describe('accordeon des etapes : defilement vertical', () => {
  // Trois etapes de 100 px l'une sous l'autre, dans une zone de 150 px de haut.
  function zone(scrollTop: number) {
    document.body.innerHTML = '<div id="z"><section data-etape="0"></section><section data-etape="1"></section><section data-etape="2"></section></div>'
    const z = document.getElementById('z')!
    Object.defineProperty(z, 'clientHeight', { value: 150 })
    z.querySelectorAll<HTMLElement>('[data-etape]').forEach((s, i) => {
      Object.defineProperty(s, 'offsetTop', { value: i * 100 })
      Object.defineProperty(s, 'offsetHeight', { value: 100 })
    })
    z.scrollTop = scrollTop
    z.scrollTo = vi.fn() as unknown as HTMLElement['scrollTo']
    return z
  }

  it('l\'etape en haut est la derniere dont la ligne a passe le bord (a 8 px pres)', () => {
    expect(etapeEnHaut(zone(0))).toBe(0)
    expect(etapeEnHaut(zone(92))).toBe(1)
    expect(etapeEnHaut(zone(91))).toBe(0)
    expect(etapeEnHaut(zone(250))).toBe(2)
  })

  it('dit l\'etape en haut des le branchement, puis a chaque changement seulement', () => {
    const z = zone(120)
    const vue = vi.fn()
    const rafs: FrameRequestCallback[] = []
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((f) => { rafs.push(f); return rafs.length })
    try {
      const arreter = suivreEtapeVisible(z, vue)
      expect(vue).toHaveBeenCalledWith(1)
      z.scrollTop = 150
      z.dispatchEvent(new Event('scroll'))
      rafs.shift()!(0)
      z.scrollTop = 210
      z.dispatchEvent(new Event('scroll'))
      rafs.shift()!(0)
      expect(vue.mock.calls).toEqual([[1], [2]])
      arreter()
      z.dispatchEvent(new Event('scroll'))
      expect(rafs).toHaveLength(0)
    } finally {
      raf.mockRestore()
    }
  })

  it('aller a une etape amene sa ligne en haut, en douceur ou d\'un coup ; deja entiere a l\'ecran, rien ne bouge', () => {
    const z = zone(0)
    allerEtape(z, 2)
    allerEtape(z, 1, false)
    expect(z.scrollTo).toHaveBeenNthCalledWith(1, { top: 200, behavior: 'smooth' })
    expect(z.scrollTo).toHaveBeenNthCalledWith(2, { top: 100, behavior: 'auto' })
    allerEtape(z, 0)
    expect(z.scrollTo).toHaveBeenCalledTimes(2)
    allerEtape(z, 9)
    allerEtape(null, 1)
    expect(z.scrollTo).toHaveBeenCalledTimes(2)
  })

  it('colonne large : l\'etape demandee reste la courante quand le defilement bute en bas, jusqu\'a ce que l\'utilisateur fasse defiler', () => {
    // Vu en capture le 19/11/2026 : un clic sur le bilan du pied ouvrait l'etape
    // Controle (la derniere), la zone ne pouvait pas la monter en haut, et le
    // pied disait « etape 3 / 7 ». Ici, 3 etapes de 100 px, zone de 150 px,
    // scrollTop plafonne a 150 : la ligne de l'etape 2 (y = 200) ne passe jamais le bord.
    const z = zone(0)
    const vue = vi.fn()
    const rafs: FrameRequestCallback[] = []
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((f) => { rafs.push(f); return rafs.length })
    try {
      const arreter = suivreEtapeVisible(z, vue)
      allerEtape(z, 2)
      z.scrollTop = 150
      z.dispatchEvent(new Event('scroll'))
      rafs.shift()!(0)
      expect(etapeEnHaut(z)).toBe(1)
      expect(etapeSuivie(z)).toBe(2)
      expect(vue.mock.calls).toEqual([[0], [2]])

      // Un clic dans un champ de l'etape ne la lache pas ; la molette, si.
      z.querySelector('section')!.dispatchEvent(new Event('pointerdown', { bubbles: true }))
      expect(etapeSuivie(z)).toBe(2)
      z.dispatchEvent(new Event('wheel'))
      z.scrollTop = 120
      z.dispatchEvent(new Event('scroll'))
      rafs.shift()!(0)
      expect(vue.mock.calls).toEqual([[0], [2], [1]])
      arreter()
    } finally {
      raf.mockRestore()
    }
  })

  it('la barre de defilement et les touches de defilement lachent l\'etape demandee ; une frappe dans un champ, non', () => {
    const z = zone(0)
    const champ = document.createElement('input')
    champ.id = 'champ'
    z.appendChild(champ)
    const arreter = suivreEtapeVisible(z, vi.fn())
    try {
      allerEtape(z, 2)
      document.getElementById('champ')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
      expect(etapeSuivie(z)).toBe(2)
      z.querySelector('section')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageDown', bubbles: true }))
      expect(etapeSuivie(z)).toBe(0)
      allerEtape(z, 2)
      z.dispatchEvent(new Event('pointerdown'))
      expect(etapeSuivie(z)).toBe(0)
    } finally {
      arreter()
    }
  })
})
