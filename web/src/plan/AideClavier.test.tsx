import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Application, type GestePlanIle } from '../composants/Application'
import { actionsRcs, dossier, paletteVue, vueApplication } from '../test/plan'
import type { VueApplication } from './types'
import { dansUneSaisie, toucheDeRaccourci } from './clavier'

// LES RACCOURCIS DE LA PAGE DU DOSSIER (plan « Vue du dossier = coeur des actions »,
// lot 5, 20/11/2026), joues a travers l'APPLICATION entiere : la touche part du
// document comme dans la fenetre, passe par l'ecoute de la page (clavier.ts) et
// remonte le geste de la barre. Ce que joue chaque geste est au C#
// (tests/PlanTravailCheck, tests/PaletteCheck).

const ecrireDansPressePapiers = vi.fn<(texte: string) => Promise<void>>()

beforeEach(() => {
  ecrireDansPressePapiers.mockReset().mockResolvedValue(undefined)
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: ecrireDansPressePapiers }, configurable: true })
})
afterEach(cleanup)

function rendre(vue: VueApplication = vueApplication({ dossier: dossier({ actions: actionsRcs() }) })) {
  const plan = vi.fn<(g: GestePlanIle) => Promise<unknown>>(() => Promise.resolve(null))
  const r = render(<Application vue={vue} geste={vi.fn()} plan={plan} />)
  // Les etapes remontent leur etape visible au montage : seuls les gestes du clavier comptent ici.
  const gestes = () => plan.mock.calls.map(([g]) => [g.nom, g.valeur]).filter(([nom]) => nom !== 'etapeVisible')
  const aide = () => screen.queryByRole('dialog', { name: 'Raccourcis clavier' })
  return { ...r, plan, gestes, aide }
}

describe('Raccourcis d\'une lettre sur la page du dossier', () => {
  it('la lettre d\'une action la joue, comme son bouton (minuscule ou capitale)', () => {
    const { gestes } = rendre()
    fireEvent.keyDown(document.body, { key: 'f' })
    fireEvent.keyDown(document.body, { key: 'K' })
    expect(gestes()).toEqual([['actionDossier', 'factures'], ['actionDossier', 'kbis']])
  })

  it('une lettre sans action, une touche tenue, ou avec Ctrl, Alt ou Cmd : rien', () => {
    const { gestes } = rendre()
    fireEvent.keyDown(document.body, { key: 'x' })
    fireEvent.keyDown(document.body, { key: 'f', repeat: true })
    fireEvent.keyDown(document.body, { key: 'f', ctrlKey: true })
    fireEvent.keyDown(document.body, { key: 'f', altKey: true })
    fireEvent.keyDown(document.body, { key: 'f', metaKey: true })
    expect(gestes()).toEqual([])
  })

  it('tapee dans le filtre de l\'arbre (ou tout champ), la lettre s\'ecrit et ne joue rien', () => {
    const { container, gestes } = rendre()
    const filtre = container.querySelector<HTMLInputElement>('.rig-arbre__filtre')!
    filtre.focus()
    fireEvent.keyDown(filtre, { key: 'f' })
    fireEvent.keyDown(filtre, { key: '?' })
    expect(gestes()).toEqual([])
    // Hors du champ, la meme lettre joue.
    fireEvent.keyDown(document.body, { key: 'f' })
    expect(gestes()).toEqual([['actionDossier', 'factures']])
  })

  it('« N » copie le numero dans la page, avec l\'avis, sans remonter au C#', async () => {
    const { container, gestes } = rendre()
    fireEvent.keyDown(document.body, { key: 'n' })
    expect(ecrireDansPressePapiers).toHaveBeenCalledWith('004176')
    expect(gestes()).toEqual([])
    expect((await screen.findByRole('status')).textContent).toBe('n° copie')
    expect(container.querySelector('.rig-vged__copie')).not.toBeNull()
  })

  it('une fenetre ouverte par-dessus (palette, impression) : les lettres ne jouent rien', () => {
    const impression = { libelle: 'Kbis a jour', imprimantes: ['A'], files: ['B'], papiers: ['C'], imprimante: 'A',
                         file: 'B', papier: 'C', exemplaires: 1, rectoVerso: false }
    for (const autre of [{ palette: paletteVue() }, { impression }]) {
      const { gestes, aide, unmount } = rendre(vueApplication({ dossier: dossier({ actions: actionsRcs() }), ...autre }))
      fireEvent.keyDown(document.body, { key: 'f' })
      fireEvent.keyDown(document.body, { key: '?' })
      expect(gestes()).toEqual([])
      expect(aide()).toBeNull()
      unmount()
    }
  })

  it('un menu de la barre ouvert joue SA lettre une seule fois, et se referme', () => {
    const { container, gestes } = rendre()
    const facturer = [...container.querySelectorAll<HTMLDetailsElement>('.rig-vged__agir > details')]
      .find((d) => d.querySelector('summary')!.textContent === 'Facturer')!
    facturer.open = true
    fireEvent.keyDown(facturer.querySelector('summary')!, { key: 'e' })
    expect(gestes()).toEqual([['actionDossier', 'encaissements']])
    expect(facturer.open).toBe(false)
  })
})

describe('L\'aide des raccourcis (« ? »)', () => {
  it('« ? » ouvre l\'aide : les lettres de la page par verbe, puis les touches de toute la page', () => {
    const { aide } = rendre()
    expect(aide()).toBeNull()
    fireEvent.keyDown(document.body, { key: '?' })
    const boite = aide()!
    expect(boite.getAttribute('aria-modal')).toBe('true')
    expect(boite.querySelector('.rig-kicker')!.textContent).toBe('Raccourcis clavier · dossier 004176')
    const sections = [...boite.querySelectorAll('section')].map((s) => [
      s.querySelector('h3')!.textContent,
      [...s.querySelectorAll('li')].map((l) => l.querySelector('kbd')!.textContent + ' ' + l.querySelector('span')!.textContent),
    ])
    expect(sections).toEqual([
      ['Delivrer', ['K Kbis a jour', 'I Imprimer le Kbis', "P Imprimer l'arborescence", 'R Requisitions - commande de documents', 'W Commandes web']],
      ['Enregistrer', ['D Demandes', 'B BODACC', 'O Cloture', 'A Archivage / radiation']],
      ['Facturer', ['F Factures', 'E Encaissements']],
      ['Ecrire', ['M Envoyer le dossier par mail']],
      ['Suivre', ['N Copier le n°', 'S Copier le SIREN', 'V Dossiers verrouilles', 'T Teletransmissions (EDI)']],
      ['Toute la page', ['Ctrl K Rechercher partout, et agir sur ce dossier', '? Afficher ou fermer cette aide',
                         'Echap Fermer : cette aide, un menu, la liste des points, puis l affaire']],
    ])
    expect(document.activeElement).toBe(boite)
  })

  it('Echap ferme l\'aide SEULE, ou que soit le focus ; un second Echap ferme l\'affaire', () => {
    const { aide, gestes } = rendre()
    fireEvent.keyDown(document.body, { key: '?' })
    expect(aide()).not.toBeNull()
    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(aide()).toBeNull()
    expect(gestes()).toEqual([])
    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(gestes()).toEqual([['echap', null]])
  })

  it('aide ouverte, les lettres ne jouent rien ; « ? » ou le voile la ferment, le focus revient', () => {
    const { container, aide, gestes } = rendre()
    const filtre = container.querySelector<HTMLInputElement>('.rig-arbre__filtre')!
    const bouton = container.querySelector<HTMLButtonElement>('.rig-vged__numero')!
    bouton.focus()
    fireEvent.keyDown(document.activeElement!, { key: '?' })
    fireEvent.keyDown(aide()!, { key: 'f' })
    expect(gestes()).toEqual([])
    fireEvent.keyDown(aide()!, { key: '?' })
    expect(aide()).toBeNull()
    expect(document.activeElement).toBe(bouton)
    fireEvent.keyDown(document.body, { key: '?' })
    fireEvent.mouseDown(aide()!)
    expect(aide()).not.toBeNull()
    fireEvent.mouseDown(aide()!.parentElement!)
    expect(aide()).toBeNull()
    expect(filtre).toBeTruthy()
  })

  it('la palette qui s\'ouvre par-dessus ferme l\'aide (Echap ira a la palette)', () => {
    const plan = vi.fn<(g: GestePlanIle) => Promise<unknown>>(() => Promise.resolve(null))
    const vue = vueApplication({ dossier: dossier({ actions: actionsRcs() }) })
    const { rerender } = render(<Application vue={vue} geste={vi.fn()} plan={plan} />)
    fireEvent.keyDown(document.body, { key: '?' })
    expect(screen.queryByRole('dialog', { name: 'Raccourcis clavier' })).not.toBeNull()
    // La vue du C# repond au dernier geste parti (les etapes en remontent un au montage).
    rerender(<Application vue={{ ...vue, geste: plan.mock.calls.length, palette: paletteVue() }} geste={vi.fn()} plan={plan} />)
    expect(screen.queryByRole('dialog', { name: 'Raccourcis clavier' })).toBeNull()
  })
})

describe('clavier.ts : ce qui est un raccourci', () => {
  const evenement = (key: string, cible: EventTarget, plus: KeyboardEventInit = {}) => {
    const e = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...plus })
    Object.defineProperty(e, 'target', { value: cible })
    return e
  }

  it('un champ, une zone de texte, une liste ou un texte editable ecrivent : jamais de raccourci', () => {
    for (const balise of ['input', 'textarea', 'select']) {
      const champ = document.createElement(balise)
      expect(dansUneSaisie(champ)).toBe(true)
      expect(toucheDeRaccourci(evenement('f', champ))).toBeNull()
    }
    const editable = document.createElement('div')
    editable.setAttribute('contenteditable', 'true')
    const dedans = document.createElement('span')
    editable.append(dedans)
    expect(dansUneSaisie(dedans)).toBe(true)
    expect(dansUneSaisie(document.createElement('button'))).toBe(false)
  })

  it('une lettre seule, en capitale ; « ? » ; rien pour les autres touches ou une touche deja traitee', () => {
    const bouton = document.createElement('button')
    expect(toucheDeRaccourci(evenement('f', bouton))).toBe('F')
    expect(toucheDeRaccourci(evenement('?', bouton, { shiftKey: true }))).toBe('?')
    expect(toucheDeRaccourci(evenement('Enter', bouton))).toBeNull()
    expect(toucheDeRaccourci(evenement('1', bouton))).toBeNull()
    expect(toucheDeRaccourci(evenement('é', bouton))).toBeNull()
    const traitee = evenement('f', bouton)
    traitee.preventDefault()
    expect(toucheDeRaccourci(traitee)).toBeNull()
  })
})
