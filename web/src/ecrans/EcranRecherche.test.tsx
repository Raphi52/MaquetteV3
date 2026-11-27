import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EcranRecherche, morceauxDeLaPage } from './EcranRecherche'
import { paletteVue } from '../test/plan'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import type { PaletteVue, PiecePaletteVue } from '../plan/types'

afterEach(() => { cleanup(); retirerFauxPont() })

const TEXTE = 'DECLARATION SUR L\'HONNEUR\n\nJe soussigne DUPONT Jean, ne le 4 avril 1978. DUPONT declare n\'avoir fait l\'objet d\'aucune condamnation.'
const premier = TEXTE.indexOf('DUPONT')
const second = TEXTE.indexOf('DUPONT', premier + 6)

function piece(partiel: Partial<PiecePaletteVue> = {}): PiecePaletteVue {
  return {
    libelle: 'Declaration de non-condamnation', chemin: ['Dossier RCS', 'Formalites', 'Formalite n 2024/00218', 'Declaration de non-condamnation'],
    lieu: 'Formalites > Formalite n 2024/00218', avant: '… Je soussigne ', trouve: 'DUPONT', apres: ' Jean, ne le 4 avril 1978…',
    passages: 2, pages: 1, date: '12/03/2024', origine: 'Teletransmission', annonce: false,
    texte: TEXTE, positions: [premier, second], longueurs: [6, 6], fichier: '', pagesDesPassages: [1, 1], nbPages: 1,
    ...partiel,
  }
}

function rendre(v: Partial<PaletteVue> = {}) {
  const plan = vi.fn<(nom: string, valeur?: unknown) => void>()
  const vue = paletteVue({
    texte: 'dupont', travailTrouve: 0,
    pieces: [piece(), piece({ libelle: 'Liste des souscripteurs', chemin: ['Dossier RCS', 'Liste des souscripteurs'], lieu: 'Dossier RCS',
      texte: 'LISTE DES SOUSCRIPTEURS\nDUPONT Jean 600', positions: [24], longueurs: [6], pagesDesPassages: [1], passages: 1 })],
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
      ['rechercheOuvrir', { chemin: ['Dossier RCS', 'Liste des souscripteurs'], page: 1 }],
      ['rechercheOuvrir', { chemin: ['Dossier RCS', 'Liste des souscripteurs'], page: 1 }],
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

  it('un vrai document se lit par page : la page du passage courant, en texte ou en PDF', () => {
    // Trois pages ; « commandite » en page 1 et en page 3.
    const texte = 'Synthese de depot\nSociete en commandite par actions\fPage deux sans rien\fGerant de la commandite'
    const pdf = piece({
      libelle: 'Synthese de depot au Guichet unique', fichier: 'documents/synthese.pdf', nbPages: 3, pages: 3,
      texte, positions: [texte.indexOf('commandite'), texte.lastIndexOf('commandite')], longueurs: [10, 10], pagesDesPassages: [1, 3],
    })
    const p = rendre({ texte: 'commandite', pieces: [pdf] })
    expect(p.container.querySelector('.rig-rp__page')!.textContent).toBe('page 1 sur 3')
    expect(p.container.querySelector('.rig-rp__feuille')!.textContent).toBe('Synthese de depot\nSociete en commandite par actions')
    fireEvent.keyDown(p.container.querySelector('.rig-rp')!, { key: 'n' })
    expect(p.container.querySelector('.rig-rp__page')!.textContent).toBe('page 3 sur 3')
    expect(p.container.querySelector('.rig-rp__feuille')!.textContent).toBe('Gerant de la commandite')
    expect(p.courant()!.textContent).toBe('commandite')
    fireEvent.click(p.getByText('PDF'))
    expect(p.container.querySelector('.rig-rp__feuille')).toBeNull()
    expect(p.container.querySelector('iframe.rig-rp__pdf')!.getAttribute('src')).toBe('documents/synthese.pdf#page=3&view=FitH')
    fireEvent.click(p.getByText('Texte'))
    expect(p.container.querySelector('iframe')).toBeNull()
    // « Ouvrir la piece » : la visionneuse a la page du passage lu (3), pas a la premiere.
    fireEvent.click(p.getByText('Ouvrir la piece'))
    expect(p.appels()).toEqual([['rechercheOuvrir', { chemin: pdf.chemin, page: 3 }]])
  })

  it('les morceaux d une page ne prennent que ses passages, a leur place', () => {
    const texte = 'aaa X bbb\fccc X\fX'
    const pc = piece({ texte, positions: [4, 14, 16], longueurs: [1, 1, 1], pagesDesPassages: [1, 2, 3], nbPages: 3 })
    expect(morceauxDeLaPage(pc, 1)).toEqual([{ t: 'aaa ', k: null }, { t: 'X', k: 0 }, { t: ' bbb', k: null }])
    expect(morceauxDeLaPage(pc, 2)).toEqual([{ t: 'ccc ', k: null }, { t: 'X', k: 1 }])
    expect(morceauxDeLaPage(pc, 3)).toEqual([{ t: 'X', k: 2 }])
  })

  it('« Joindre a ce depot » : le depot choisi, la piece jointe, l avis ; deja dedans, le bouton le dit', () => {
    const depots = [
      { libelle: 'Depot n 2024/0141', chemin: ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141'], lieu: 'Depots d\'actes', pieces: ['Bail commercial'], choisi: false },
      { libelle: 'Depot n B2024/061077', chemin: ['Dossier RCS', 'Depots des comptes annuels', 'Depot n B2024/061077'], lieu: 'Depots des comptes annuels', pieces: ['Declaration de non-condamnation'], choisi: false },
    ]
    const p = rendre({ depots, avis: null })
    const choix = p.container.querySelector<HTMLSelectElement>('.rig-rp__depots')!
    expect([...choix.options].map((o) => o.textContent)).toEqual(['Depot n 2024/0141 · Depots d\'actes', 'Depot n B2024/061077 · Depots des comptes annuels'])
    fireEvent.click(p.getByText('Joindre a ce depot'))
    expect(p.appels()).toEqual([['rechercheJoindre', { piece: piece().chemin, depot: depots[0]!.chemin }]])
    fireEvent.change(choix, { target: { value: depots[1]!.chemin.join('/') } })
    expect((p.getByText('Deja dans ce depot') as HTMLButtonElement).disabled).toBe(true)
    cleanup()
    const p2 = rendre({ depots, avis: '« Declaration de non-condamnation » jointe a Depot n 2024/0141.' })
    expect(p2.getByRole('status').textContent).toBe('« Declaration de non-condamnation » jointe a Depot n 2024/0141.')
  })

  it('la copie jointe arrive en tete de liste : la liseuse reste sur la piece lue, au meme passage', () => {
    const souscripteurs = piece({ libelle: 'Liste des souscripteurs', chemin: ['Dossier RCS', 'Liste des souscripteurs'], lieu: 'Dossier RCS',
      texte: 'LISTE DES SOUSCRIPTEURS\nDUPONT Jean 600', positions: [24], longueurs: [6], pagesDesPassages: [1], passages: 1 })
    const plan = vi.fn<(nom: string, valeur?: unknown) => void>()
    const avant = paletteVue({ texte: 'dupont', travailTrouve: 0, pieces: [souscripteurs, piece()] })
    const r = render(<EcranRecherche vue={avant} plan={plan} />)
    fireEvent.click(r.getAllByRole('option')[1]!)
    fireEvent.keyDown(r.container.querySelector('.rig-rp')!, { key: 'n' })
    expect(r.container.querySelector('.rig-rp__nav')!.textContent).toContain('passage 2 sur 2')
    // Le plan de travail renvoie la liste avec la copie jointe en tete.
    const copie = piece({ chemin: ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141', 'Declaration de non-condamnation'] })
    r.rerender(<EcranRecherche vue={{ ...avant, pieces: [copie, souscripteurs, piece()], avis: 'jointe' }} plan={plan} />)
    expect(r.container.querySelector('.rig-palette__ligne--choisie')!.textContent).toContain('Declaration de non-condamnation')
    expect(r.getAllByRole('option')[2]!.getAttribute('aria-selected')).toBe('true')
    expect(r.container.querySelector('.rig-rp__nav')!.textContent).toContain('passage 2 sur 2')
  })

  // Le garde-fou de la page, comme celui de la palette (Palette.test.tsx) : avant le lot 20 du plan « RIG v3 100 %
  // fonctionnel », la page filtrait la constante d'exemple AnnoncesBodacc meme branchee au serveur.
  it('annonces BODACC : celles de la base branchee au serveur, sans « exemple » ; sans serveur, l exemple marque (lot 20)', async () => {
    const base = {
      numAnnonce: '7', numPc: '2001RJ0004', typeAnnonce: 'Création', codeEvePro: '1RJ', dateEvePro: '2001-03-02', prevueEnvoi: '2001-03-05',
      envoi: '2001-03-05', numParution: '', parution: null, numBord: 'B1', numFact: '', texte: 'Jugement d ouverture de redressement judiciaire',
      dossier: '2001B00012', transmission: null,
    }
    const pont = installerFauxPont({ 'EcranBodacc.Chercher': () => ({ mode: 'serveur', message: null, annonces: [base], total: 1 }) })
    const r = render(<EcranRecherche vue={paletteVue({ texte: 'redressement', pieces: [] })} plan={vi.fn()} />)
    await waitFor(() => expect(r.container.textContent).toContain('Jugement d ouverture de redressement judiciaire'))
    expect(pont.appels).toHaveBeenCalledWith('EcranBodacc.Chercher', 'redressement')
    expect(r.container.textContent).not.toContain('A2026-1190')
    expect(r.container.querySelector('.rig-tcw__exemple')).toBeNull()
    cleanup()
    // Sans serveur, le pont rend « demo » : l'annonce d'exemple de l'ecran BODACC, marquee.
    installerFauxPont({ 'EcranBodacc.Chercher': () => ({ mode: 'demo', message: null, annonces: [], total: 0 }) })
    const demo = render(<EcranRecherche vue={paletteVue({ texte: 'redressement', pieces: [] })} plan={vi.fn()} />)
    await waitFor(() => expect(demo.container.textContent).toContain('A2026-1190'))
    expect(demo.container.querySelector('.rig-tcw__exemple')!.textContent).toBe('exemple')
  })

  it('avant la reponse du plan de travail, le cadre seul', () => {
    const plan = vi.fn()
    const { container } = render(<EcranRecherche vue={null} plan={plan} />)
    expect(container.innerHTML).toBe('<div class="rig-rp"></div>')
  })
})
