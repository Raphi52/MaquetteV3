import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { ActionsDossier, formeDesVerbes, IDENTITE_MIN, largeurDesPuces, lignesDesPuces } from './ActionsDossier'
import { Procedure } from './Procedure'
import { Visionneuse } from './Visionneuse'
import { actionsJud, actionsRcs, dossier, proc } from '../test/plan'
import type { DossierVue, GestePlan, VerbeActionsVue } from './types'

// LA BARRE D'ACTIONS DU DOSSIER (plan « Vue du dossier = coeur des actions », lot 1,
// 20/11/2026). Ce qui est disponible par type d'affaire est decide par le C#
// (tests/PlanTravailCheck le joue) ; ici, le rendu de ce que la session envoie, le
// geste remonte, la copie jouee par la page, le clavier, et la puce « ← Dossier »
// de l'ecran ouvert depuis l'affaire.

const ecrireDansPressePapiers = vi.fn<(texte: string) => Promise<void>>()

beforeEach(() => {
  ecrireDansPressePapiers.mockReset().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: ecrireDansPressePapiers }, configurable: true })
})
afterEach(cleanup)

function rendre(verbes: VerbeActionsVue[]) {
  const plan = vi.fn<GestePlan>()
  const copier = vi.fn<(texte: string, quoi: string) => void>()
  const r = render(<ActionsDossier verbes={verbes} plan={plan} copier={copier} />)
  return { ...r, plan, copier, gestes: () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur ?? null]) }
}

/** Les menus de la barre : le verbe, et pour chaque action « libelle [lettre] (ecran) ». */
function lire(barre: Element): [string, string[]][] {
  return [...barre.querySelectorAll(':scope > details')].map((d) => [
    d.querySelector('summary')!.textContent!,
    [...d.querySelectorAll('[role="menuitem"]')].map((b) =>
      b.querySelector('.rig-vged__agir-libelle')!.textContent! + ' [' + b.querySelector('kbd')!.textContent + ']'
      + (b.classList.contains('rig-vged__agir-action--ecran') ? ' (ecran)' : '')),
  ])
}

function menu(container: HTMLElement, verbe: string): HTMLDetailsElement {
  return [...container.querySelectorAll<HTMLDetailsElement>('.rig-vged__agir > details')]
    .find((d) => d.querySelector('summary')!.textContent === verbe)!
}

describe('ActionsDossier : sans mise en page mesurable, un menu par verbe', () => {
  it('affaire RCS : les cinq verbes dans l\'ordre, le Kbis et les ecrans du RCS', () => {
    const { container } = rendre(actionsRcs())
    const barre = container.querySelector('.rig-vged__agir')!
    expect(barre.getAttribute('role')).toBe('toolbar')
    expect(lire(barre)).toEqual([
      ['Delivrer', ['Kbis a jour [K]', 'Imprimer le Kbis [I]', "Imprimer l'arborescence [P]",
        'Requisitions - commande de documents [R] (ecran)', 'Commandes web [W] (ecran)']],
      ['Enregistrer', ['Demandes [D] (ecran)', 'BODACC [B] (ecran)', 'Cloture [O] (ecran)', 'Archivage / radiation [A] (ecran)']],
      ['Facturer', ['Factures [F] (ecran)', 'Encaissements [E] (ecran)']],
      ['Ecrire', ['Envoyer le dossier par mail [M]']],
      ['Suivre', ['Copier le n° [N]', 'Copier le SIREN [S]', 'Dossiers verrouilles [V] (ecran)', 'Teletransmissions (EDI) [T] (ecran)']],
    ])
    // Rien a mesurer (pas de mise en page) : la forme la plus compacte, menus fermes.
    expect(container.querySelectorAll('.rig-vged__agir > .rig-vged__agir-groupe')).toHaveLength(0)
    expect(container.querySelectorAll('details[open]')).toHaveLength(0)
  })

  it('affaire judiciaire : ni Kbis ni SIREN ; depots, redaction, role, audiences, GED judiciaire', () => {
    const { container } = rendre(actionsJud())
    const lu = lire(container.querySelector('.rig-vged__agir')!)
    expect(lu.map(([v]) => v)).toEqual(['Delivrer', 'Enregistrer', 'Facturer', 'Ecrire', 'Suivre'])
    const toutes = lu.flatMap(([, a]) => a).join(' | ')
    expect(toutes).not.toContain('Kbis')
    expect(toutes).not.toContain('SIREN')
    expect(lu[1]![1]).toContain('Depots judiciaires [D] (ecran)')
    expect(lu[3]![1]).toEqual(['Notification au mandataire [C]', "Redaction d'une decision [R] (ecran)", 'Envoyer le dossier par mail [M]'])
    expect(lu[4]![1]).toEqual(['Copier le n° [N]', 'Consultation judiciaire [J] (ecran)', 'Audiences [U] (ecran)',
      'GED judiciaire [G] (ecran)', 'Dossiers verrouilles [V] (ecran)'])
  })

  it('sans action disponible, pas de barre du tout', () => {
    const { container } = rendre([])
    expect(container.innerHTML).toBe('')
  })

  it('une action locale ou d\'ecran remonte son code par « actionDossier » et referme le menu', () => {
    const { container, gestes } = rendre(actionsRcs())
    const delivrer = menu(container, 'Delivrer')
    delivrer.open = true
    fireEvent.click(screen.getByRole('menuitem', { name: 'Kbis a jour K', hidden: true }))
    expect(delivrer.open).toBe(false)
    const facturer = menu(container, 'Facturer')
    facturer.open = true
    fireEvent.click(screen.getByRole('menuitem', { name: 'Factures F', hidden: true }))
    expect(gestes()).toEqual([['actionDossier', 'kbis'], ['actionDossier', 'factures']])
    expect(facturer.open).toBe(false)
  })

  it('une copie se joue dans la page, sans geste : le texte et ce que l\'avis annonce', () => {
    const { container, gestes, copier } = rendre(actionsRcs())
    menu(container, 'Suivre').open = true
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copier le SIREN S', hidden: true }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copier le n° N', hidden: true }))
    expect(copier.mock.calls).toEqual([['812 445 901', 'SIREN'], ['004176', 'n°']])
    expect(gestes()).toEqual([])
  })

  it('menu ouvert, sa lettre joue l\'action ; une lettre d\'un autre menu ne fait rien', () => {
    const { container, gestes } = rendre(actionsRcs())
    const enregistrer = menu(container, 'Enregistrer')
    enregistrer.open = true
    fireEvent.keyDown(enregistrer, { key: 'f' })
    expect(gestes()).toEqual([])
    fireEvent.keyDown(enregistrer, { key: 'b' })
    expect(gestes()).toEqual([['actionDossier', 'bodacc']])
    expect(enregistrer.open).toBe(false)
    // Menu ferme, la lettre n'est pas captee par la barre (le raccourci global est un autre lot).
    fireEvent.keyDown(enregistrer, { key: 'd' })
    expect(gestes()).toEqual([['actionDossier', 'bodacc']])
  })

  it('Echap referme le menu SEUL : la touche ne remonte pas au document (qui fermerait l\'affaire)', () => {
    const { container } = rendre(actionsRcs())
    const auDocument = vi.fn()
    document.addEventListener('keydown', auDocument)
    try {
      const ecrire = menu(container, 'Ecrire')
      ecrire.open = true
      fireEvent.keyDown(ecrire, { key: 'Escape' })
      expect(ecrire.open).toBe(false)
      expect(auDocument).not.toHaveBeenCalled()
      // Menu ferme, Echap remonte normalement.
      fireEvent.keyDown(ecrire, { key: 'Escape' })
      expect(auDocument).toHaveBeenCalledTimes(1)
    } finally {
      document.removeEventListener('keydown', auDocument)
    }
  })

  it('un menu qui s\'ouvre referme l\'autre ; un clic ailleurs referme celui qui est ouvert', () => {
    const { container } = rendre(actionsRcs())
    const delivrer = menu(container, 'Delivrer')
    const suivre = menu(container, 'Suivre')
    delivrer.open = true
    fireEvent(delivrer, new Event('toggle'))
    suivre.open = true
    fireEvent(suivre, new Event('toggle'))
    expect(delivrer.open).toBe(false)
    expect(suivre.open).toBe(true)
    fireEvent.pointerDown(suivre.querySelector('[role="menuitem"]')!)
    expect(suivre.open).toBe(true)
    fireEvent.pointerDown(document.body)
    expect(suivre.open).toBe(false)
  })
})

describe('ActionsDossier : des boutons groupes par verbe, un menu seulement si la place manque', () => {
  // La page mesure : la largeur de l'entete, et dans la couche de mesure chaque verbe
  // deplie (boutons) puis replie (menu). Ici, ces largeurs sont posees a la main.
  const DEPLIE: Record<string, number> = { Delivrer: 700, Enregistrer: 420, Facturer: 230, Ecrire: 330, Suivre: 520 }
  const REPLIE = 80
  // La forme « action principale » (lot 6) : le bouton de l'action frequente, et le chevron du
  // reste du verbe. Enregistrer et Suivre n'en ont pas (actionsRcs : principale null).
  const PRINCIPALE: Record<string, number> = { Delivrer: 120, Facturer: 110, Ecrire: 100 }
  let largeurEntete = 0
  // Chaque enfant de la rangee des puces de l'identite (puces, mention, avis de copie).
  let largeurPuce = 0
  let observateurs: (() => void)[] = []

  beforeEach(() => {
    largeurEntete = 0
    largeurPuce = 0
    observateurs = []
    vi.stubGlobal('ResizeObserver', class {
      constructor(rappel: () => void) { observateurs.push(rappel) }
      observe() {}
      disconnect() {}
    })
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
      let largeur = 0
      if (this.matches('[data-entete], .rig-vged__entete')) largeur = largeurEntete
      else if (this.parentElement?.matches('.rig-vged__puces')) largeur = largeurPuce
      else if (this.parentElement?.parentElement?.matches('.rig-vged__agir-mesure')) {
        // Chaque forme mesuree porte son verbe et sa forme (data-verbe, data-forme).
        const { verbe = '', forme } = (this as HTMLElement).dataset
        largeur = forme === 'boutons' ? DEPLIE[verbe]! : forme === 'principale' ? PRINCIPALE[verbe]! : REPLIE
      }
      return { width: largeur, height: 20, x: 0, y: 0, top: 0, left: 0, right: largeur, bottom: 20, toJSON: () => ({}) } as DOMRect
    })
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  /** La forme de chaque verbe dans la barre : « B » = boutons, « P » = son action principale
   *  en bouton et le reste sous un chevron, « M » = menu. */
  function formes(container: HTMLElement): string {
    return [...container.querySelector('.rig-vged__agir')!.children]
      .filter((e) => !e.classList.contains('rig-vged__agir-mesure'))
      .map((e) => (e.tagName === 'DETAILS' ? 'M' : e.classList.contains('rig-vged__agir-principale') ? 'P' : 'B')).join('')
  }
  function rendreDansEntete(largeur: number) {
    largeurEntete = largeur
    const plan = vi.fn<GestePlan>()
    const r = render(<div data-entete=""><ActionsDossier verbes={actionsRcs()} plan={plan} copier={vi.fn()} /></div>)
    return { ...r, plan }
  }
  const redimensionner = (largeur: number) => {
    largeurEntete = largeur
    act(() => observateurs.forEach((o) => o()))
  }

  it('formeDesVerbes : les verbes se deplient du moins couteux au plus couteux, tant que la barre tient', () => {
    const v = (deplie: number) => ({ deplie, replie: 80 })
    expect(formeDesVerbes([v(700), v(420), v(230)], 2000, 0, 0)).toEqual(['boutons', 'boutons', 'boutons'])
    // 240 en menus ; couts 620, 340, 150 : le 3e (390), le 2e (730), pas le 1er (1350 > 800).
    expect(formeDesVerbes([v(700), v(420), v(230)], 800, 0, 0)).toEqual(['menu', 'boutons', 'boutons'])
    // Les ecarts et le cadre comptent : 2 x 2 + 10 de plus, le 2e ne tient plus (744 > 730).
    expect(formeDesVerbes([v(700), v(420), v(230)], 730, 2, 10)).toEqual(['menu', 'menu', 'boutons'])
    // Meme tout en menus, la barre deborde : rien ne se deplie.
    expect(formeDesVerbes([v(700), v(420), v(230)], 200, 0, 0)).toEqual(['menu', 'menu', 'menu'])
    // A cout egal, l'ordre de la barre departage.
    expect(formeDesVerbes([v(300), v(300)], 460, 0, 0)).toEqual(['boutons', 'menu'])
  })

  it('formeDesVerbes : la forme « principale » passe avant les boutons du meme verbe, a son cout', () => {
    // 160 en menus. Le 1er : principale +40, puis boutons +580 ; le 2e n'en a pas (+340).
    const verbes = [{ deplie: 700, replie: 80, principale: 120 }, { deplie: 420, replie: 80 }]
    expect(formeDesVerbes(verbes, 199, 0, 0)).toEqual(['menu', 'menu'])
    expect(formeDesVerbes(verbes, 200, 0, 0)).toEqual(['principale', 'menu'])
    expect(formeDesVerbes(verbes, 539, 0, 0)).toEqual(['principale', 'menu'])
    expect(formeDesVerbes(verbes, 540, 0, 0)).toEqual(['principale', 'boutons'])
    expect(formeDesVerbes(verbes, 1120, 0, 0)).toEqual(['boutons', 'boutons'])
    // Plus etroite que son menu (« Factures » contre « Facturer ») : toujours prise, meme
    // quand la barre deborde -- elle la raccourcit.
    expect(formeDesVerbes([{ deplie: 230, replie: 90, principale: 85 }], 50, 0, 0)).toEqual(['principale'])
  })

  it('formeDesVerbes : en elargissant, un verbe deplie ne se replie jamais', () => {
    // Le premier tri (ordre de la barre, en sautant un verbe trop large) repliait
    // Facturer pour deplier Delivrer en passant de 1500 a 1920 px.
    const verbes = Object.entries(DEPLIE).map(([verbe, deplie]) =>
      ({ deplie, replie: REPLIE, ...(PRINCIPALE[verbe] !== undefined ? { principale: PRINCIPALE[verbe] } : {}) }))
    const rang = { menu: 0, principale: 1, boutons: 2 }
    let avant = formeDesVerbes(verbes, 0, 2, 6)
    for (let largeur = 10; largeur <= 3000; largeur += 10) {
      const apres = formeDesVerbes(verbes, largeur, 2, 6)
      avant.forEach((forme, i) => expect(rang[apres[i]!], `verbe ${i} a ${largeur} px`).toBeGreaterThanOrEqual(rang[forme]))
      avant = apres
    }
    expect(avant).toEqual(['boutons', 'boutons', 'boutons', 'boutons', 'boutons'])
  })

  it('assez de largeur : chaque verbe est un groupe de boutons, et un bouton remonte son geste', () => {
    const { container, plan } = rendreDansEntete(3000)
    expect(formes(container)).toBe('BBBBB')
    expect(container.querySelectorAll('.rig-vged__agir details')).toHaveLength(0)
    const facturer = screen.getByRole('group', { name: 'Facturer' })
    expect([...facturer.querySelectorAll('button')].map((b) => b.textContent)).toEqual(['Factures', 'Encaissements'])
    fireEvent.click(screen.getByRole('button', { name: 'Factures' }))
    fireEvent.click(screen.getByRole('button', { name: 'Kbis a jour' }))
    expect(plan.mock.calls).toEqual([['actionDossier', 'factures'], ['actionDossier', 'kbis']])
    // La couche de mesure double chaque bouton, mais reste hors de l'arbre
    // d'accessibilite : un seul « Factures » s'y trouve.
    expect(screen.getAllByRole('button', { name: 'Factures' })).toHaveLength(1)
  })

  it('chaque bouton, et chaque action d\'un menu, donne sa lettre dans son infobulle (lot 5)', () => {
    const { container } = rendreDansEntete(3000)
    expect(screen.getByRole('button', { name: 'Factures' }).getAttribute('title'))
      .toBe('Factures (infobulle) — touche F')
    const boutons = [...container.querySelectorAll<HTMLButtonElement>('.rig-vged__agir > .rig-vged__agir-groupe > button')]
    expect(boutons.map((b) => / — touche ([A-Z])$/.exec(b.title)?.[1]).join('')).toBe('KIPRWDBOAFEMNSVT')
    redimensionner(300)
    expect(screen.getByRole('menuitem', { name: /^Factures/ }).getAttribute('title')).toBe('Factures (infobulle) — touche F')
    expect(container.querySelector('.rig-vged__agir > details summary')!.getAttribute('title'))
      .toBe("Kbis a jour (K) · Imprimer le Kbis (I) · Imprimer l'arborescence (P) · Requisitions - commande de documents (R) · Commandes web (W)")
  })

  it('la largeur manque : les verbes qui ne tiennent plus se replient en menu, et se redeplient quand elle revient', () => {
    const { container } = rendreDansEntete(3000)
    expect(formes(container)).toBe('BBBBB')
    // 800 : en menus 400 ; les formes principales d'Ecrire (+20), Facturer (+30), Delivrer (+40),
    // puis les boutons de Facturer (+120) tiennent (610) ; ceux d'Ecrire (+230) non.
    redimensionner(800)
    expect(formes(container)).toBe('PMBPM')
    // Un verbe replie garde toutes ses actions, avec leur lettre.
    const enregistrer = container.querySelector<HTMLDetailsElement>('.rig-vged__agir > details')!
    expect(enregistrer.querySelector('summary')!.textContent).toBe('Enregistrer')
    expect(enregistrer.querySelectorAll('[role="menuitem"] kbd')).toHaveLength(4)
    redimensionner(300)
    expect(formes(container)).toBe('MMMMM')
    // Les deux derniers menus deroulent vers la gauche.
    expect([...container.querySelectorAll('.rig-vged__agir-verbe--droite > summary')].map((s) => s.textContent))
      .toEqual(['Ecrire', 'Suivre'])
    redimensionner(3000)
    expect(formes(container)).toBe('BBBBB')
  })

  it('dans la visionneuse, l\'identite de l\'affaire garde IDENTITE_MIN : la barre ne prend que le reste', () => {
    // Les autres voisins mesurent 0 ici : la barre dispose de l'entete moins IDENTITE_MIN.
    largeurEntete = IDENTITE_MIN + 1000
    const { container } = render(<Visionneuse dossier={dossier({ actions: actionsRcs() })} pagination={0} defilement={null}
                                               defilementJoue={{ current: 0 }} plan={vi.fn<GestePlan>()} />)
    // 1000 : en menus 400 ; les trois formes principales (490), Facturer (610) et Ecrire (840)
    // tiennent, Enregistrer (1180) non. Sans la reserve de l'identite (1480), il se serait deplie.
    expect(formes(container)).toBe('PMBBM')
  })

  it('lignesDesPuces / largeurDesPuces : le retour a la ligne des puces, et la largeur la plus etroite pour N lignes', () => {
    expect(lignesDesPuces([], 4, 500)).toBe(0)
    // 3 x 200 + 2 ecarts de 4 = 608 : une ligne a 608, deux a 607 ; trois sous 404.
    expect(lignesDesPuces([200, 200, 200], 4, 608)).toBe(1)
    expect(lignesDesPuces([200, 200, 200], 4, 607)).toBe(2)
    expect(lignesDesPuces([200, 200, 200], 4, 404)).toBe(2)
    expect(lignesDesPuces([200, 200, 200], 4, 403)).toBe(3)
    // Une puce plus large que la rangee reste seule sur sa ligne.
    expect(lignesDesPuces([900, 100], 4, 500)).toBe(2)
    expect(largeurDesPuces([200, 200, 200], 4, 1, 2000)).toBe(608)
    expect(largeurDesPuces([200, 200, 200], 4, 2, 2000)).toBe(404)
    // Jamais sous la plus large puce, qui serait coupee ; jamais au-dela du plafond.
    expect(largeurDesPuces([200, 200, 200], 4, 3, 2000)).toBe(200)
    expect(largeurDesPuces([200, 200, 200], 4, 1, 300)).toBe(300)
  })

  /** La visionneuse d'une affaire a `n` puces de 300 px (la 1re, le SIREN). */
  function rendreAffaire(n: number) {
    largeurPuce = 300
    const plan = vi.fn<GestePlan>()
    const champs = Array.from({ length: n }, (_, i) => ({ libelle: i === 0 ? 'SIREN' : 'Champ ' + i, valeur: 'v' + i, mono: false }))
    const r = render(<Visionneuse dossier={dossier({ actions: actionsRcs(), sommaire: { champs, mention: '' } })} pagination={0}
                                  defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    return { ...r, plan }
  }
  /** La largeur de la barre telle qu'elle s'affiche (verbes en boutons, principale ou menus). */
  function largeurBarre(container: HTMLElement): number {
    const verbes = Object.keys(DEPLIE)
    return [...formes(container)].reduce((s, f, i) =>
      s + (f === 'B' ? DEPLIE[verbes[i]!]! : f === 'P' ? PRINCIPALE[verbes[i]!]! : REPLIE), 0)
  }

  it("deplier un verbe ne fait jamais passer les puces de l'identite sur une ligne de plus (004176 a 1500 px)", () => {
    // Refus du controle final (20/11/2026) : a 1500 px, deplier « Facturer » faisait
    // passer les puces de 004176 de 2 a 3 lignes. Ici 5 puces de 300 px.
    const puces = [300, 300, 300, 300, 300]
    largeurEntete = 1400
    const { container } = rendreAffaire(5)
    // Barre repliee (400), l'identite a 1000 px : 2 lignes de puces, qui tiennent jusqu'a 900.
    // Les trois formes principales (+90) tiennent ; deplier Facturer (+120 de plus) la
    // ramenerait a 790 : 3 lignes. (480 fixes : 'PMBBM'.)
    expect(formes(container)).toBe('PMPPM')
    // 1600 : l'identite a 1200 en menus (2 lignes) ; Facturer deplie, elle garde 990 : 2 lignes.
    redimensionner(1600)
    expect(formes(container)).toBe('PMBPM')
    // A toute largeur, jamais une ligne de plus que barre toute repliee ; et assez
    // large, tout est en boutons.
    for (let largeur = 600; largeur <= 4000; largeur += 25) {
      redimensionner(largeur)
      const repliee = lignesDesPuces(puces, 0, largeur - 5 * REPLIE)
      expect(lignesDesPuces(puces, 0, largeur - largeurBarre(container)), `${largeur} px, ${formes(container)}`)
        .toBeLessThanOrEqual(repliee)
    }
    expect(formes(container)).toBe('BBBBB')
  })

  it("l'avis « SIREN copie » qui passe dans les puces ne change pas la forme de la barre", async () => {
    // 4 puces, 1500 px : l'identite a 1100 en menus, 2 lignes ; elle peut descendre a 600.
    largeurEntete = 1500
    const { container } = rendreAffaire(4)
    expect(formes(container)).toBe('PMBBM')
    fireEvent.click(container.querySelector('.rig-vged__puces > .rig-vged__puce--copie')!)
    expect((await screen.findByRole('status')).textContent).toBe('SIREN copie')
    // L'entete change de hauteur avec l'avis : la mesure repart. Compte comme une
    // puce, il porterait la reserve a 900 et replierait Ecrire le temps de l'avis.
    redimensionner(1500)
    expect(formes(container)).toBe('PMBBM')
  })

  it("004176 a 1516 px : les verbes replies gardent « Kbis a jour » et « Factures » en boutons, sans ligne de puces en plus", () => {
    // Refus du chef (lot 6, 20/11/2026) : a 1516 px, les cinq verbes de 004176 etaient des
    // menus, deux clics pour le Kbis ou les factures. Faux si : la forme principale prenait
    // la place des puces (une ligne de plus), si Enregistrer ou Suivre, sans action frequente,
    // gagnaient un bouton, ou si Ecrire (le seul mail) restait un menu d'un item.
    largeurEntete = 1400
    const { container, plan } = rendreAffaire(5)
    expect(formes(container)).toBe('PMPPM')
    expect(lignesDesPuces([300, 300, 300, 300, 300], 0, 1400 - largeurBarre(container))).toBe(2)
    fireEvent.click(screen.getByRole('button', { name: 'Kbis a jour' }))
    fireEvent.click(screen.getByRole('button', { name: 'Factures' }))
    fireEvent.click(screen.getByRole('button', { name: 'Envoyer le dossier par mail' }))
    // (La zone des etapes de la visionneuse remonte aussi « etapeVisible » : seules les actions comptent.)
    const actions = () => plan.mock.calls.filter(([geste]) => geste === 'actionDossier')
    expect(actions()).toEqual([['actionDossier', 'kbis'], ['actionDossier', 'factures'], ['actionDossier', 'envoyerDossier']])
    expect(screen.getByRole('group', { name: 'Ecrire' }).querySelector('details')).toBeNull()
    // Le reste du verbe sous son chevron, nomme par le verbe, chaque action avec sa lettre.
    const suite = screen.getByRole('group', { name: 'Delivrer' }).querySelector('details')!
    expect(suite.querySelector('summary')!.getAttribute('aria-label')).toBe('Delivrer : toutes les actions')
    expect(suite.querySelector('[role="menu"]')!.getAttribute('aria-label')).toBe('Delivrer')
    expect([...suite.querySelectorAll('[role="menuitem"] kbd')].map((k) => k.textContent).join('')).toBe('KIPRW')
    // Menu ouvert, sa lettre joue l'action, comme dans un verbe replie.
    suite.open = true
    fireEvent.keyDown(suite, { key: 'i' })
    expect(actions().at(-1)).toEqual(['actionDossier', 'imprimerKbis'])
    expect(suite.open).toBe(false)
  })
})

describe('Visionneuse : la barre dans l\'entete, le numero et le SIREN qui se copient', () => {
  function rendreVisionneuse(d: DossierVue) {
    const plan = vi.fn<GestePlan>()
    const r = render(<Visionneuse dossier={d} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    return { ...r, plan }
  }

  it('la barre se place entre l\'identite et « Document »', () => {
    const { container } = rendreVisionneuse(dossier({ actions: actionsRcs(), boutonDocument: true }))
    const enfants = [...container.querySelector('.rig-vged__entete')!.children].map((e) => e.className || e.tagName)
    const identite = enfants.indexOf('rig-vged__identite')
    expect(enfants[identite + 1]).toBe('rig-vged__agir')
    expect(container.querySelector('.rig-vged__agir')!.nextElementSibling!.textContent).toBe('Document')
  })

  it('la copie depuis la barre passe par le presse-papiers et un avis bref suit les puces', async () => {
    const { container, plan } = rendreVisionneuse(dossier({ actions: actionsRcs() }))
    menu(container, 'Suivre').open = true
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copier le SIREN S', hidden: true }))
    expect(ecrireDansPressePapiers).toHaveBeenCalledWith('812 445 901')
    expect((await screen.findByRole('status')).textContent).toBe('SIREN copie')
    expect(container.querySelector('.rig-vged__puces > .rig-vged__copie')).not.toBeNull()
    // « etapeVisible » part des que la zone des etapes est montee (Visionneuse.test.tsx) : rien d'autre.
    expect(plan.mock.calls.filter(([nom]) => nom !== 'etapeVisible')).toEqual([])
  })

  it('un clic sur le numero ou sur la puce SIREN les copie ; les autres puces ne copient rien', async () => {
    const { container } = rendreVisionneuse(dossier())
    fireEvent.click(container.querySelector('.rig-vged__titre > .rig-vged__numero')!)
    expect(ecrireDansPressePapiers).toHaveBeenLastCalledWith('004176')
    expect((await screen.findByRole('status')).textContent).toBe('n° copie')
    const puces = container.querySelectorAll('.rig-vged__puces > .rig-vged__puce')
    expect(puces[0]!.tagName).toBe('BUTTON')
    expect(puces[0]!.getAttribute('title')).toBe('Copier le SIREN')
    expect(puces[1]!.tagName).toBe('SPAN')
    fireEvent.click(puces[0]!)
    expect(ecrireDansPressePapiers).toHaveBeenLastCalledWith('812 445 901')
    expect(ecrireDansPressePapiers).toHaveBeenCalledTimes(2)
  })

  it('un presse-papiers refuse le dit, au lieu d\'annoncer une copie', async () => {
    ecrireDansPressePapiers.mockRejectedValue(new Error('refuse'))
    const { container } = rendreVisionneuse(dossier())
    fireEvent.click(container.querySelector('.rig-vged__titre > .rig-vged__numero')!)
    expect((await screen.findByRole('status')).textContent).toBe('Copie impossible')
  })
})

describe('Procedure : l\'ecran ouvert depuis une affaire porte la puce « ← Dossier »', () => {
  it('la puce nomme l\'affaire et y ramene', () => {
    const plan = vi.fn<GestePlan>()
    const p = proc()
    p.ecran = { ...p.ecran, dossier: { numero: '004176', siren: '812 445 901', objet: 'Modification - SCI DU PARC', type: 'RCS', nom: 'SCI DU PARC' } }
    const { container } = render(<Procedure proc={p} plan={plan} />)
    const puce = container.querySelector('.rig-proc > .rig-proc__dossier > button')!
    expect(container.querySelector('.rig-proc')!.firstElementChild!.className).toBe('rig-proc__dossier')
    expect(puce.textContent).toBe('Dossier 004176 SCI DU PARC')
    fireEvent.click(puce)
    expect(plan.mock.calls).toEqual([['retourDossier']])
  })

  it('un ecran ouvert d\'ailleurs n\'a pas de puce', () => {
    const { container } = render(<Procedure proc={proc()} plan={vi.fn<GestePlan>()} />)
    expect(container.querySelector('.rig-proc__dossier')).toBeNull()
  })
})
