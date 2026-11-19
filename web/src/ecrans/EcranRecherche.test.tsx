import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { EcranRecherche } from './EcranRecherche'
import { paletteVue } from '../test/plan'
import type { PaletteVue, PiecePaletteVue } from '../plan/types'

afterEach(cleanup)

const TEXTE = 'DECLARATION SUR L\'HONNEUR\n\nJe soussigne DUPONT Jean, ne le 4 avril 1978. DUPONT declare n\'avoir fait l\'objet d\'aucune condamnation.'
const premier = TEXTE.indexOf('DUPONT')
const second = TEXTE.indexOf('DUPONT', premier + 6)

function piece(partiel: Partial<PiecePaletteVue> = {}): PiecePaletteVue {
  return {
    libelle: 'Declaration de non-condamnation', chemin: ['Dossier RCS', 'Formalites', 'Formalite n 2024/00218', 'Declaration de non-condamnation'],
    lieu: 'Formalites > Formalite n 2024/00218', avant: '… Je soussigne ', trouve: 'DUPONT', apres: ' Jean, ne le 4 avril 1978…',
    passages: 2, pages: 1, date: '12/03/2024', origine: 'Teletransmission', annonce: false,
    texte: TEXTE, positions: [premier, second], longueur: 6,
    ...partiel,
  }
}

function rendre(v: Partial<PaletteVue> = {}) {
  const plan = vi.fn<(nom: string, valeur?: unknown) => void>()
  const vue = paletteVue({
    texte: 'dupont', travailTrouve: 0,
    pieces: [piece(), piece({ libelle: 'Liste des souscripteurs', chemin: ['Dossier RCS', 'Liste des souscripteurs'], lieu: 'Dossier RCS',
      texte: 'LISTE DES SOUSCRIPTEURS\nDUPONT Jean 600', positions: [24], longueur: 6, passages: 1 })],
    ...v,
  })
  const r = render(<EcranRecherche vue={vue} plan={plan} />)
  const courant = () => r.container.querySelector('.rig-rp__m--courant')
  const marques = () => [...r.container.querySelectorAll('.rig-rp__feuille mark')]
  const appels = () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur])
  return { ...r, plan, courant, marques, appels }
}

describe('EcranRecherche (la palette en page, liseuse des pieces)', () => {
  it('la piece choisie s affiche en entier, chaque passage surligne, le premier courant', () => {
    const p = rendre()
    expect(p.container.querySelector('.rig-rp__som')!.textContent).toBe('2 resultats pour « dupont » · 0 a traiter · 2 pieces')
    expect(p.container.querySelector('.rig-palette__ligne--choisie')!.textContent).toContain('Declaration de non-condamnation')
    expect(p.container.querySelector('.rig-rp__feuille')!.textContent).toBe(TEXTE)
    expect(p.marques().map((m) => m.textContent)).toEqual(['DUPONT', 'DUPONT'])
    expect(p.courant()).toBe(p.marques()[0])
    expect(p.container.querySelector('.rig-rp__nav')!.textContent).toContain('passage 1 sur 2')
  })

  it('N, Maj N, F3 et les fleches passent d un passage a l autre, en boucle', () => {
    const p = rendre()
    const racine = p.container.querySelector('.rig-rp')!
    fireEvent.keyDown(racine, { key: 'n' })
    expect(p.courant()).toBe(p.marques()[1])
    fireEvent.keyDown(racine, { key: 'n' })
    expect(p.courant()).toBe(p.marques()[0])
    fireEvent.keyDown(racine, { key: 'N', shiftKey: true })
    expect(p.courant()).toBe(p.marques()[1])
    // Dans la saisie, « n » s'ecrit ; F3 passe quand meme au passage suivant.
    const champ = p.container.querySelector<HTMLInputElement>('.rig-recherche__saisie')!
    fireEvent.keyDown(champ, { key: 'n' })
    expect(p.courant()).toBe(p.marques()[1])
    fireEvent.keyDown(champ, { key: 'F3' })
    expect(p.courant()).toBe(p.marques()[0])
    fireEvent.click(p.getByTitle('Passage precedent (Maj N)'))
    expect(p.courant()).toBe(p.marques()[1])
  })

  it('une autre piece de la liste s affiche a son tour ; Entree ou le bouton l ouvre dans son affaire', () => {
    const p = rendre()
    fireEvent.click(p.getAllByRole('option')[1]!)
    expect(p.container.querySelector('.rig-rp__feuille')!.textContent).toBe('LISTE DES SOUSCRIPTEURS\nDUPONT Jean 600')
    expect(p.container.querySelector('.rig-rp__nav')!.textContent).toContain('passage 1 sur 1')
    fireEvent.keyDown(p.container.querySelector('.rig-rp')!, { key: 'Enter' })
    fireEvent.click(p.getByText('Ouvrir la piece'))
    expect(p.appels()).toEqual([
      ['palettePiece', ['Dossier RCS', 'Liste des souscripteurs']],
      ['palettePiece', ['Dossier RCS', 'Liste des souscripteurs']],
    ])
  })

  it('la barre cherche a chaque frappe ; le travail, les ecrans et l arbre s ouvrent d un clic', () => {
    const p = rendre({
      travail: [{
        nature: 'dem', numero: '004151', cle: '004151', ecran: 'CODE_DEMANDES', ecranLibelle: 'Demandes', titre: 'Depot de bilan - TRANSNORD SA',
        detail: 'Demande du 29/08/2026', etat: 'J+11', ton: 'al', filet: 'alerte', qui: null, aMoi: false, enCours: true, attribuable: true,
        delai: null, siren: '318 552 004', activite: [],
      }],
      travailTrouve: 1,
      ecrans: [{ code: 'CODE_TELETRANS', libelle: 'Teletransmissions (EDI)' }],
      elements: [{ libelle: 'Depot n 2024/0141', chemin: ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141'], lieu: 'Depots d\'actes', piece: false }],
    })
    fireEvent.change(p.container.querySelector('.rig-recherche__saisie')!, { target: { value: 'dupont jean' } })
    fireEvent.click(p.getByText('Depot de bilan - TRANSNORD SA'))
    fireEvent.click(p.getByText('Teletransmissions (EDI)'))
    fireEvent.click(p.getByText('Depot n 2024/0141'))
    expect(p.appels()).toEqual([
      ['rechercheTexte', 'dupont jean'],
      ['paletteAller', { nature: 'dem', cle: '004151' }],
      ['paletteAller', { nature: 'ecran', cle: 'CODE_TELETRANS' }],
      ['palettePiece', ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141']],
    ])
  })

  it('rien dans les pieces : la liseuse le dit ; un SIREN inconnu propose d ouvrir un dossier', () => {
    const p = rendre({ texte: '552 100 554', pieces: [], sirenInconnu: '552 100 554' })
    expect(p.container.querySelector('.rig-rp__som')!.textContent).toBe('Aucun resultat pour « 552 100 554 »')
    expect(p.container.querySelector('.rig-rp__vide')!.textContent).toBe('Aucune piece ne contient « 552 100 554 ».')
    fireEvent.click(p.getByText('Ouvrir un dossier pour ce SIREN'))
    expect(p.appels()).toEqual([['paletteAller', { nature: 'siren', cle: '552 100 554' }]])
  })

  it('avant la reponse du plan de travail, le cadre seul', () => {
    const plan = vi.fn()
    const { container } = render(<EcranRecherche vue={null} plan={plan} />)
    expect(container.innerHTML).toBe('<div class="rig-rp"></div>')
  })
})
