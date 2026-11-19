import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { Application, type GestePlanIle } from './Application'
import { morceaux } from '../test/morceaux'
import { vuePlan } from '../test/accueil'
import { dossier, proc as procPlan, vueApplication } from '../test/plan'
import type { GesteIle } from '../accueil/types'
import type { VueApplication } from '../plan/types'

afterEach(cleanup)

// Un ecran sans pont (l'ecran qui se nomme) : l'aiguillage n'appelle aucun C# ici.
const proc = (partiel: Parameters<typeof procPlan>[0] = {}) => procPlan({ code: 'PROC_CREANCE', ...partiel })

// L'APPLICATION ENTIERE (lot 2 du passage a React) : ce que Home.razor rendait.
// Les regles du plan de travail sont jouees par tests/PlanTravailCheck (C#) ;
// ici, la racine de la page, l'ordre des blocs, le clavier, et la regle propre
// a la page : une vue plus ancienne que le dernier geste n'est jamais montree.
function rendre(vue: VueApplication = vueApplication()) {
  const geste = vi.fn<(g: GesteIle) => void>()
  const plan = vi.fn<(g: GestePlanIle) => Promise<unknown>>(() => Promise.resolve(null))
  const r = render(<Application vue={vue} geste={geste} plan={plan} />)
  return { ...r, geste, plan, gestes: () => plan.mock.calls.map(([g]) => [g.nom, g.valeur, g.numero]) }
}

describe('Application : la racine de la page', () => {
  it('pose le theme et la colonne de la page, puis le filet de marque (style du div racine de Home.razor)', () => {
    const { container } = rendre(vueApplication({ theme: 'sombre' }))
    const racine = container.firstElementChild as HTMLElement
    expect(racine.getAttribute('data-rig-theme')).toBe('sombre')
    expect(racine.style.cssText).toBe('background: var(--rig-fond); color: var(--rig-texte); height: 100vh; overflow: hidden; display: flex; flex-direction: column;')
    expect(morceaux(racine)).toEqual(['<div>'])
    expect(racine.firstElementChild!.className).toBe('rig-filet-marque')
    expect(document.documentElement.getAttribute('data-rig-theme')).toBe('sombre')
  })

  it('la barre (ses onglets DEDANS, « comme Brave »), puis l\'accueil, l\'ecran et l\'affaire', () => {
    const vue = vueApplication({
      barre: true, accueil: vuePlan(),
      onglets: [{ code: 'PROC_FACTURE', libelle: 'Factures', actif: true, detache: false, glisse: false, verbe: null }],
      proc: proc(), dossier: dossier(),
    })
    const { container } = rendre(vue)
    const enfants = [...container.firstElementChild!.children].map((e) => e.tagName.toLowerCase() + '.' + e.className.split(' ')[0])
    expect(enfants).toEqual(['div.rig-filet-marque', 'header.rig-barre', 'div.rig-accueil', 'div.rig-proc', 'div.rig-vged'])
    expect(container.querySelector('header.rig-barre > #rig-vonglets')).not.toBeNull()
  })

  it('dans la fenetre d\'un onglet sorti : ni barre ni accueil, l\'ecran seul', () => {
    const { container } = rendre(vueApplication({ proc: proc() }))
    expect(container.querySelector('.rig-barre')).toBeNull()
    expect(container.querySelector('.rig-accueil')).toBeNull()
    expect(container.querySelector('.rig-proc')).not.toBeNull()
  })

  it('l\'ecran commence par son contenu : ni ligne du titre (etoile, Fermer), ni bandeau des ecrans voisins', () => {
    const { container } = rendre(vueApplication({ proc: proc() }))
    const ecran = container.querySelector('.rig-proc')!
    expect([...ecran.children].map((e) => e.className)).toEqual(['rig-nonmaq'])
    expect(ecran.querySelector('.rig-vged__entete, .rig-sujet, .rig-vue__etoile')).toBeNull()
    expect([...ecran.querySelectorAll('button')].some((b) => b.textContent === 'Fermer')).toBe(false)
  })
})

describe('Application : les gestes du plan de travail', () => {
  it('chaque geste part numerote, avec sa valeur (null si absente)', () => {
    const onglets = [{ code: 'PROC_CREANCE', libelle: 'Creances', actif: true, detache: false, glisse: false }]
    const { container, gestes } = rendre(vueApplication({ barre: true, onglets, proc: proc() }))
    fireEvent.keyDown(document, { key: 'Escape' })
    fireEvent.click(container.querySelector('.rig-vonglet__fermer')!)
    expect(gestes()).toEqual([['echap', null, 1], ['ongletFermer', 'PROC_CREANCE', 2]])
  })

  it('une vue plus ancienne que le dernier geste n\'est pas montree ; celle qui le porte l\'est', () => {
    const { container, rerender, geste, plan } = rendre(vueApplication({ proc: proc({ libelle: 'Factures' }) }))
    const titre = () => container.querySelector('.rig-proc .rig-nonmaq__titre')!.textContent
    fireEvent.keyDown(document, { key: 'Escape' })
    fireEvent.keyDown(document, { key: 'Escape' })
    // La reponse au 1er geste arrive : le 2e est encore en route, rien ne bouge.
    rerender(<Application vue={vueApplication({ geste: 1, proc: proc({ libelle: 'Intermediaire' }) })} geste={geste} plan={plan} />)
    expect(titre()).toBe('Factures')
    // La reponse au dernier geste : elle est montree.
    rerender(<Application vue={vueApplication({ geste: 2, proc: proc({ libelle: 'Encaissements' }) })} geste={geste} plan={plan} />)
    expect(titre()).toBe('Encaissements')
  })

  it('Echap, ou que soit le focus, remonte « echap » ; les autres touches rien', () => {
    const { gestes } = rendre(vueApplication({ proc: proc() }))
    fireEvent.keyDown(document, { key: 'Enter' })
    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(gestes()).toEqual([['echap', null, 1]])
  })

  it('les gestes de l\'accueil passent par le rappel « geste », sans numero', () => {
    const { container, geste, plan } = rendre(vueApplication({ barre: true, accueil: vuePlan() }))
    fireEvent.click(container.querySelector('.rig-menu__replier')!)
    expect(geste).toHaveBeenCalledWith({ nom: 'menu', valeur: null })
    expect(plan).not.toHaveBeenCalled()
  })
})

describe('Application : la barre d\'onglets', () => {
  const onglets = [
    { code: 'PROC_FACTURE', libelle: 'Factures', actif: false, detache: false, glisse: false, verbe: null },
    { code: 'PROC_ENCAISS', libelle: 'Encaissements', actif: true, detache: true, glisse: true, verbe: null },
  ]

  it('un onglet : titre, marque « sorti », croix ; memes classes et titres que Razor', () => {
    const { container } = rendre(vueApplication({ barre: true, onglets }))
    const barre = container.querySelector('#rig-vonglets')!
    expect(barre.className).toBe('rig-vonglets')
    const [a, b] = barre.querySelectorAll('.rig-vonglet')
    expect(a!.className).toBe('rig-vonglet   ')
    expect(b!.className).toBe('rig-vonglet rig-vonglet--actif rig-vonglet--sorti rig-vonglet--glisse')
    expect(a!.getAttribute('title')).toBe('Glissez l onglet hors de la fenetre pour le sortir sur un autre ecran')
    expect(b!.getAttribute('title')).toBe('Ouvert dans sa propre fenetre')
    expect(morceaux(a!)).toEqual(['<span>', '<button>'])
    expect(morceaux(b!)).toEqual(['<span>', '<span>', '<button>'])
    expect(b!.querySelector('.rig-vonglet__sorti')!.textContent).toBe('↗')
    expect(a!.querySelector('.rig-vonglet__fermer')!.getAttribute('aria-label')).toBe('Fermer Factures')
    expect(a!.querySelector('.rig-vonglet__fermer')!.textContent).toBe('×')
  })

  it('appui, glisser et lacher partent au C# avec la mesure de la barre ; la croix ne declenche pas l\'onglet', () => {
    const { container, gestes } = rendre(vueApplication({ barre: true, onglets }))
    const a = container.querySelector('[data-rig-onglet="PROC_FACTURE"]')!
    fireEvent.pointerDown(a, { clientX: 30, clientY: 12 })
    fireEvent.pointerMove(a, { clientX: 31, clientY: 12, buttons: 0 })
    fireEvent.pointerMove(a, { clientX: 80, clientY: 12, buttons: 1 })
    fireEvent.pointerUp(a, { clientX: 80, clientY: 12 })
    fireEvent.click(a)
    fireEvent.pointerDown(a.querySelector('.rig-vonglet__fermer')!)
    fireEvent.click(a.querySelector('.rig-vonglet__fermer')!)
    const g = gestes()
    expect(g.map(([nom]) => nom)).toEqual(['ongletDebut', 'ongletGlisse', 'ongletFin', 'ongletActiver', 'ongletFermer'])
    expect(g[0]![1]).toMatchObject({ code: 'PROC_FACTURE', x: 30, y: 12, mesure: { cases: [[0, 0], [0, 0]] } })
    expect((g[0]![1] as { mesure: { page: number[] } }).mesure.page).toHaveLength(3)
    expect(g[1]![1]).toEqual({ x: 80, y: 12 })
    expect(g[4]![1]).toBe('PROC_FACTURE')
  })
})
