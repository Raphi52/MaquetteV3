import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Palette } from './Palette'
import type { PaletteVue, TravailPaletteVue } from '../plan/types'

afterEach(cleanup)

const moi = { signature: 'R. VILAIN', initiales: 'RV', nom: 'Raphael' }
const benier = { signature: 'C. BENIER', initiales: 'CB', nom: 'C. BENIER' }

function ligne(partiel: Partial<TravailPaletteVue>): TravailPaletteVue {
  return {
    nature: 'dem', numero: '004151', cle: '004151', ecran: 'PROC_DEMANDE', ecranLibelle: 'Demandes', titre: 'Depot de bilan - TRANSNORD SA',
    detail: 'Demande du 29/08/2026 · TRANSNORD', etat: 'J+11', ton: 'al', filet: 'alerte', qui: benier,
    aMoi: false, enCours: true, attribuable: true, delai: 'Delai legal de 7 jours depasse de 4 jours', siren: '318 552 004',
    activite: [{ agent: 'Raphael', verbe: 'a valide', objet: 'depot du dossier 004151', depuis: 'il y a 7 min' }],
    ...partiel,
  }
}

function vue(partiel: Partial<PaletteVue> = {}): PaletteVue {
  return {
    texte: 'transnord', filtre: 'tout',
    filtres: [
      { code: 'tout', libelle: 'Tout', nombre: 2 }, { code: 'moi', libelle: 'A moi', nombre: 0 },
      { code: 'horsDelai', libelle: 'Hors delai', nombre: 1 }, { code: 'semaine', libelle: 'Cette semaine', nombre: 0 },
    ],
    moi,
    agents: [moi, { signature: 'C. MARTIN', initiales: 'CM', nom: 'Claire' }],
    travail: [
      ligne({}),
      ligne({ nature: 'aff', numero: 'J-00931', cle: 'J-00931', ecran: '', ecranLibelle: '', etat: 'JUD', ton: 'al', qui: null, attribuable: false, enCours: false, delai: null, activite: [] }),
    ],
    travailTrouve: 2,
    pieces: [{
      libelle: 'Liste des souscripteurs', chemin: ['Dossier RCS', 'Liste des souscripteurs'], lieu: 'Dossier RCS',
      avant: 'Nom / Actions / Montant · ', trouve: 'TRANSNORD', apres: ' Jean 600', passages: 2, pages: 2,
      date: '12/03/2024', origine: 'Depot guichet', annonce: false,
    }],
    dossierDesPieces: '2026B01067 AMIENS BILLARDS', numeroDesPieces: '2026B01067',
    horsGreffe: [{ code: 'FIBEN', source: 'FIBEN', objet: 'cotation et incidents de paiement, Banque de France', sujet: 'TRANSNORD SA · 318 552 004' }],
    bilan: { jour: 'mercredi 9 septembre', horsDelai: 2, arriveesDuJour: 3, mAttendent: 4, plusAncienne: 4 },
    sirenInconnu: null,
    ...partiel,
  }
}

function rendre(v: PaletteVue = vue()) {
  const plan = vi.fn<(nom: string, valeur?: unknown) => void>()
  const r = render(<Palette vue={v} plan={plan} />)
  const champ = () => r.container.querySelector<HTMLInputElement>('.rig-palette__champ')!
  const touche = (key: string, plus: object = {}) => fireEvent.keyDown(champ(), { key, ...plus })
  const appels = () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur])
  const titres = () => [...r.container.querySelectorAll('.rig-palette__liste > .rig-palette__groupe > .rig-palette__titre')].map((t) => t.textContent)
  const choisie = () => r.container.querySelector('.rig-palette__ligne--choisie')?.textContent ?? ''
  const apercu = () => r.container.querySelector('.rig-palette__apercu')!.textContent ?? ''
  return { ...r, plan, champ, touche, appels, titres, choisie, apercu }
}

describe('Palette Ctrl K (/draft « Page Recherche », propositions 3 et 6)', () => {
  it('avant de taper : le travail a traiter, les filtres comptes et le bilan du jour', () => {
    const p = rendre(vue({ texte: '', pieces: [], horsGreffe: [] }))
    expect(p.titres()).toEqual(['A traiter maintenant2'])
    expect([...p.container.querySelectorAll('.rig-palette__filtre')].map((f) => f.textContent))
      .toEqual(['Tout2', 'A moi0', 'Hors delai1', 'Cette semaine0'])
    expect(p.container.querySelector('.rig-palette__filtre--actif')!.textContent).toBe('Tout2')
    expect(p.container.querySelector('.rig-palette__moi')!.textContent).toBe('RVRaphael')
    expect(p.apercu()).toContain('Mon travail · mercredi 9 septembre')
    expect(p.apercu()).toContain('4 demandes t attendentla plus ancienne depuis 4 jours')
    expect(p.apercu()).toContain('Le texte des pieces · 2026B01067 AMIENS BILLARDS')
    expect(document.activeElement).toBe(p.champ())
  })

  it('avec une saisie : le plus urgent d abord, puis les pieces, les annonces et hors du greffe', () => {
    const p = rendre(vue({ texte: 'redressement transnord' }))
    expect(p.titres()).toEqual(['Le plus urgent d abord2', 'Dans le texte des pieces1', 'Hors du greffe1'])
    const p2 = rendre(vue({ texte: 'redressement', travail: [], horsGreffe: [], pieces: [] }))
    // Les annonces de l'ecran BODACC se cherchent aussi.
    expect(p2.titres()).toEqual(['Annonces BODACC1'])
    expect(p2.choisie()).toContain('A2026-1190')
    expect(p2.choisie()).toContain('Ouverture de redressement')
  })

  it('la ligne de travail montre son filet, son etat, qui s en occupe, et surligne ce qui est tape', () => {
    const p = rendre()
    const l = p.container.querySelector('.rig-palette__ligne--choisie')!
    expect(l.querySelector('.rig-palette__filet')!.className).toBe('rig-palette__filet rig-filet-v--alerte')
    expect(l.querySelector('.rig-palette__etat')!.className).toBe('rig-palette__etat rig-palette__etat--al')
    expect(l.querySelector('.rig-palette__etat')!.textContent).toBe('J+11')
    expect(l.querySelector('.rig-palette__avatar')!.textContent).toBe('CB')
    expect([...l.querySelectorAll('mark')].map((m) => m.textContent)).toEqual(['TRANSNORD'])
    expect(p.apercu()).toContain('C. BENIERs en occupe')
    expect(p.apercu()).toContain('Delai legal de 7 jours depasse de 4 jours')
    expect(p.apercu()).toContain('Raphael a valide depot du dossier 004151il y a 7 min')
  })

  it('taper remonte le texte au plan de travail ; les fleches et Entree ouvrent la ligne choisie', () => {
    const p = rendre()
    fireEvent.change(p.champ(), { target: { value: 'transnord sa' } })
    expect(p.appels()).toEqual([['paletteTexte', 'transnord sa']])
    p.touche('ArrowDown')
    expect(p.choisie()).toContain('J-00931')
    p.touche('Enter')
    expect(p.appels().at(-1)).toEqual(['paletteAller', { nature: 'aff', cle: 'J-00931' }])
  })

  it('→ ouvre les gestes du resultat ; Me l attribuer ; Echap revient aux resultats sans fermer', () => {
    const p = rendre()
    p.touche('ArrowRight')
    expect(p.container.querySelector('.rig-palette__fil')!.textContent).toBe('004151 · Depot de bilan›')
    const libelles = [...p.container.querySelectorAll('.rig-palette__liste .rig-palette__t1')].map((t) => t.textContent)
    expect(libelles).toEqual(['Ouvrir la demande', 'Ouvrir l ecran Demandes', 'Me l attribuer', 'Attribuer a Claire',
                              'Copier le numero', 'Copier le SIREN'])
    p.touche('ArrowDown'); p.touche('ArrowDown')
    p.touche('Enter')
    expect(p.appels()).toEqual([['paletteAttribuer', { numero: '004151', signature: null }]])
    // Apres une attribution, on revient aux resultats.
    expect(p.container.querySelector('.rig-palette__fil')).toBeNull()
    p.touche('ArrowRight')
    const doc = vi.fn()
    document.addEventListener('keydown', doc)
    p.touche('Escape')
    document.removeEventListener('keydown', doc)
    expect(p.container.querySelector('.rig-palette__fil')).toBeNull()
    expect(doc).not.toHaveBeenCalled()
    expect(p.appels().filter(([n]) => n === 'paletteFermer')).toEqual([])
    p.touche('Escape')
    expect(p.appels().at(-1)).toEqual(['paletteFermer', undefined])
  })

  it('les gestes se filtrent en tapant', () => {
    const p = rendre()
    p.touche('ArrowRight')
    fireEvent.change(p.champ(), { target: { value: 'claire' } })
    expect([...p.container.querySelectorAll('.rig-palette__liste .rig-palette__t1')].map((t) => t.textContent)).toEqual(['Attribuer a Claire'])
    p.touche('Enter')
    expect(p.appels()).toEqual([['paletteAttribuer', { numero: '004151', signature: 'C. MARTIN' }]])
  })

  it('Ctrl M attribue sans ouvrir les gestes ; Tab passe au filtre suivant', () => {
    const p = rendre()
    p.touche('m', { ctrlKey: true })
    p.touche('Tab')
    p.touche('Tab', { shiftKey: true })
    expect(p.appels()).toEqual([
      ['paletteAttribuer', { numero: '004151', signature: null }],
      ['paletteFiltre', 'moi'],
      ['paletteFiltre', 'semaine'],
    ])
    expect(p.container.querySelector('.rig-palette__pied')!.textContent).toContain('Ctrl Mme l attribuer')
  })

  it('une piece montre son passage et s ouvre depliee dans son affaire', () => {
    const p = rendre()
    p.touche('ArrowDown'); p.touche('ArrowDown')
    expect(p.choisie()).toContain('« Nom / Actions / Montant · TRANSNORD Jean 600 »')
    expect(p.apercu()).toContain('Dossier RCS · 2026B01067 AMIENS BILLARDS2 pages · 12/03/2024 · Depot guichet')
    expect(p.apercu()).toContain('2 passages trouves')
    p.touche('Enter')
    expect(p.appels()).toEqual([['palettePiece', ['Dossier RCS', 'Liste des souscripteurs']]])
  })

  it('hors du greffe le dit : rien n est encore branche', () => {
    const p = rendre()
    p.touche('ArrowDown'); p.touche('ArrowDown'); p.touche('ArrowDown')
    expect(p.choisie()).toContain('FIBEN · TRANSNORD SA · 318 552 004')
    expect(p.choisie()).toContain('a brancher')
    expect(p.apercu()).toContain('Pas encore branche')
  })

  it('un SIREN inconnu : aucun resultat, et Entree ouvre un dossier pour lui', () => {
    const p = rendre(vue({
      texte: '552 100 554', travail: [], travailTrouve: 0, pieces: [], sirenInconnu: '552 100 554',
      horsGreffe: [{ code: 'FIBEN', source: 'FIBEN', objet: 'cotation', sujet: '552 100 554' }],
    }))
    expect(p.container.querySelector('.rig-palette__rien')!.textContent).toBe('Aucun resultat pour « 552 100 554 »')
    expect(p.titres()).toEqual(['Que faire de ce SIREN', 'Hors du greffe1'])
    expect(p.apercu()).toContain('SIREN552 100 554')
    p.touche('Enter')
    expect(p.appels()).toEqual([['paletteAller', { nature: 'siren', cle: '552 100 554' }]])
  })

  it('un clic sur le voile ferme la palette ; un clic dedans, non', () => {
    const p = rendre()
    fireEvent.mouseDown(p.container.querySelector('.rig-palette')!)
    expect(p.appels()).toEqual([])
    fireEvent.mouseDown(p.container.querySelector('.rig-palette__voile')!)
    expect(p.appels()).toEqual([['paletteFermer', undefined]])
  })

  it('un clic sur une ligne l ouvre ; son chevron montre ses gestes', () => {
    const p = rendre()
    const lignes = p.container.querySelectorAll('.rig-palette__liste .rig-palette__ligne')
    fireEvent.click(lignes[1]!.querySelector('.rig-palette__vers')!)
    expect(p.container.querySelector('.rig-palette__fil')!.textContent).toBe('J-00931 · Depot de bilan›')
    expect(p.appels()).toEqual([])
    p.touche('ArrowLeft')
    fireEvent.click(p.container.querySelectorAll('.rig-palette__liste .rig-palette__ligne')[0]!)
    expect(p.appels()).toEqual([['paletteAller', { nature: 'dem', cle: '004151' }]])
    expect(screen.getByRole('dialog', { name: 'Rechercher partout' })).toBeTruthy()
  })
})
