import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ATTENTE_BARRE, Palette } from './Palette'
import type { ActionPaletteVue, PaletteVue, TravailPaletteVue } from '../plan/types'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { DelaiRecherche } from '../ecrans/annoncesBodacc'

afterEach(() => { cleanup(); retirerFauxPont() })

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
      texte: 'LISTE · Nom / Actions / Montant · TRANSNORD Jean 600', positions: [34], longueurs: [9], fichier: '',
      pagesDesPassages: [1], nbPages: 1,
    }],
    dossierDesPieces: '2026B01067 AMIENS BILLARDS', numeroDesPieces: '2026B01067',
    horsGreffe: [{ code: 'FIBEN', source: 'FIBEN', objet: 'cotation et incidents de paiement, Banque de France', sujet: 'TRANSNORD SA · 318 552 004' }],
    bilan: { jour: 'mercredi 9 septembre', horsDelai: 2, arriveesDuJour: 3, mAttendent: 4, plusAncienne: 4 },
    sirenInconnu: null,
    ecrans: [],
    elements: [],
    ancree: false,
    depots: [],
    avis: null,
    actions: [],
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

  it('avec une saisie : le plus urgent d abord, puis les pieces, les annonces et hors du greffe', async () => {
    const p = rendre(vue({ texte: 'redressement transnord' }))
    expect(p.titres()).toEqual(['Le plus urgent d abord2', 'Dans le texte des pieces1', 'Hors du greffe1'])
    cleanup()
    // Sans serveur, le pont rend « demo » : la palette cherche les annonces d'exemple de l'ecran BODACC, marquees.
    const pont = installerFauxPont({ 'EcranBodacc.Chercher': () => ({ mode: 'demo', message: null, annonces: [], total: 0 }) })
    const p2 = rendre(vue({ texte: 'redressement', travail: [], horsGreffe: [], pieces: [] }))
    await waitFor(() => expect(p2.titres()).toEqual(['Annonces BODACC1']))
    expect(p2.choisie()).toContain('A2026-1190')
    expect(p2.choisie()).toContain('Ouverture de redressement')
    expect(p2.container.querySelector('.rig-palette__ligne--choisie .rig-tcw__exemple')!.textContent).toBe('exemple')
    expect(pont.appels).toHaveBeenCalledWith('EcranBodacc.Chercher', 'redressement')
  })

  it('branchee au serveur : les annonces BODACC de la base, sans « exemple » ; jamais l exemple a la place (lot 20)', async () => {
    const base = {
      numAnnonce: '7', numPc: '2001RJ0004', typeAnnonce: 'Création', codeEvePro: '1RJ', dateEvePro: '2001-03-02', prevueEnvoi: '2001-03-05',
      envoi: '2001-03-05', numParution: '', parution: null, numBord: 'B1', numFact: '', texte: 'Jugement d ouverture de redressement judiciaire',
      dossier: '2001B00012', transmission: null,
    }
    installerFauxPont({ 'EcranBodacc.Chercher': () => ({ mode: 'serveur', message: null, annonces: [base], total: 1 }) })
    const p = rendre(vue({ texte: 'redressement', travail: [], horsGreffe: [], pieces: [] }))
    await waitFor(() => expect(p.titres()).toEqual(['Annonces BODACC1']))
    expect(p.choisie()).toContain('Jugement d ouverture de redressement judiciaire')
    expect(p.choisie()).not.toContain('A2026-1190')
    expect(p.container.querySelector('.rig-tcw__exemple')).toBeNull()
    cleanup()
    // Base injoignable : aucune annonce, pas l'exemple.
    installerFauxPont({ 'EcranBodacc.Chercher': () => ({ mode: 'erreur', message: 'La base ne répond pas.', annonces: [], total: 0 }) })
    const panne = rendre(vue({ texte: 'redressement', travail: [], horsGreffe: [], pieces: [] }))
    await new Promise((r) => setTimeout(r, DelaiRecherche + 50))
    expect(panne.titres()).toEqual([])
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

  it("→ ouvre les gestes du resultat ; M'attribuer ; Echap revient aux resultats sans fermer", () => {
    const p = rendre()
    p.touche('ArrowRight')
    expect(p.container.querySelector('.rig-palette__fil')!.textContent).toBe('004151 · Depot de bilan›')
    const libelles = [...p.container.querySelectorAll('.rig-palette__liste .rig-palette__t1')].map((t) => t.textContent)
    // Les memes mots que la barre du dossier (« Copier le n° », Metier/ActionsDossier.cs) et que
    // la colonne de suivi (« M'attribuer », Suivi.tsx) : la palette disait « Copier le numero »
    // et « Me l attribuer » pour les memes gestes (parcours du lot 6, 20/11/2026).
    expect(libelles).toEqual(['Ouvrir la demande', 'Ouvrir l ecran Demandes', "M'attribuer", 'Attribuer a Claire',
                              'Copier le n°', 'Copier le SIREN'])
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
    expect(p.container.querySelector('.rig-palette__pied')!.textContent).toContain("Ctrl Mm'attribuer")
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

  it('les ecrans du menu et les elements de l arbre : ce que la barre de l accueil trouvait', () => {
    const p = rendre(vue({
      texte: 'trans', pieces: [], horsGreffe: [],
      ecrans: [{ code: 'CODE_TELETRANS', libelle: 'Teletransmissions (EDI)' }],
      elements: [{ libelle: 'Depot n 2024/0141', chemin: ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141'], lieu: 'Depots d\'actes', piece: false }],
    }))
    expect(p.titres()).toEqual(['Le plus urgent d abord2', 'Ecrans1', 'Dans l arbre du dossier1'])
    p.touche('ArrowDown'); p.touche('ArrowDown')
    expect(p.choisie()).toContain('Teletransmissions (EDI)')
    expect([...p.container.querySelector('.rig-palette__ligne--choisie')!.querySelectorAll('mark')].map((m) => m.textContent)).toEqual(['trans'])
    p.touche('Enter')
    p.touche('ArrowDown')
    expect(p.choisie()).toContain('Depot n 2024/0141')
    expect(p.apercu()).toContain('Depots d\'actes · 2026B01067 AMIENS BILLARDS')
    p.touche('Enter')
    expect(p.appels()).toEqual([
      ['paletteAller', { nature: 'ecran', cle: 'CODE_TELETRANS' }],
      ['palettePiece', ['Dossier RCS', 'Depots d\'actes', 'Depot n 2024/0141']],
    ])
  })

  it('accrochee a la barre de l accueil : la saisie dans la pilule, la liste pendue dessous, sans voile', () => {
    const plan = vi.fn<(nom: string, valeur?: unknown) => void>()
    const dehors = document.createElement('div')
    document.body.appendChild(dehors)
    const { container } = render(<Palette vue={vue({ ancree: true })} plan={plan} ancree />)
    const barre = container.firstElementChild!
    expect(barre.className).toBe('rig-recherche rig-recherche--fixe rig-recherche--palette')
    const champ = barre.querySelector<HTMLInputElement>('input.rig-recherche__saisie')!
    expect(champ.placeholder).toBe(ATTENTE_BARRE)
    expect(document.activeElement).toBe(champ)
    // La liste est DANS la barre (elle pend dessous en CSS), et rien ne voile l'accueil.
    expect(barre.querySelector('.rig-palette.rig-palette--ancree .rig-palette__liste')).not.toBeNull()
    expect(container.querySelector('.rig-palette__voile')).toBeNull()
    expect(container.querySelector('.rig-palette__saisie')).toBeNull()
    // Un clic dans la liste la garde ouverte ; un clic ailleurs dans la page la ferme.
    fireEvent.mouseDown(barre.querySelector('.rig-palette__liste')!)
    expect(plan).not.toHaveBeenCalled()
    fireEvent.mouseDown(dehors)
    expect(plan.mock.calls).toEqual([['paletteFermer']])
    // Le clavier est le meme : Echap ferme, la frappe cherche.
    fireEvent.change(champ, { target: { value: 'transnord sa' } })
    fireEvent.keyDown(champ, { key: 'Escape' })
    expect(plan.mock.calls.slice(1)).toEqual([['paletteTexte', 'transnord sa'], ['paletteFermer']])
    dehors.remove()
  })

  it('« Ouvrir en page » etale la meme recherche dans un onglet', () => {
    const p = rendre()
    fireEvent.click(p.getByText('Ouvrir en page'))
    expect(p.appels()).toEqual([['paletteEnPage', undefined]])
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

// « SUR CE DOSSIER » (plan « Vue du dossier = coeur des actions », lot 5, 20/11/2026) :
// sur une affaire ouverte, les actions de sa barre viennent en tete, filtrees par le C#
// (Metier/RecherchePalette.cs : Actions, joue par tests/PaletteCheck) ; Entree en joue une
// par le geste de la barre. Hors affaire, la vue n'en porte aucune : la palette d'avant.
describe('Palette : « Sur ce dossier »', () => {
  const action = (code: string, verbe: string, libelle: string, touche: string, ecran: boolean, copie: string | null = null): ActionPaletteVue =>
    ({ code, verbe, libelle, touche, detail: libelle + ' (infobulle)', ecran, copie })
  const factures = action('factures', 'Facturer', 'Factures', 'F', true)
  const encaissements = action('encaissements', 'Facturer', 'Encaissements', 'E', true)
  const surDossier = { dossierDesPieces: '004176 SCI DU PARC', numeroDesPieces: '004176' }

  it('« fac » sur l affaire : ses actions EN TETE, Factures choisie, Entree la joue (« actionDossier »)', () => {
    const p = rendre(vue({ texte: 'fac', actions: [factures, encaissements], ...surDossier }))
    expect(p.titres()).toEqual(['Sur ce dossier2', 'Le plus urgent d abord2', 'Dans le texte des pieces1', 'Hors du greffe1'])
    const ligne = p.container.querySelector('.rig-palette__ligne--choisie')!
    expect(ligne.querySelector('.rig-palette__t1')!.innerHTML).toBe('<mark>Fac</mark>tures')
    expect(ligne.querySelector('.rig-palette__t2')!.textContent).toBe('Facturer · Factures (infobulle)')
    // La lettre de l'action, qui la joue aussi hors de la palette.
    expect(ligne.querySelector('.rig-palette__touche')!.textContent).toBe('F')
    expect(p.apercu()).toContain('Factures')
    expect(p.apercu()).toContain('004176 SCI DU PARC')
    p.touche('Enter')
    expect(p.appels()).toEqual([['actionDossier', 'factures']])
  })

  it('les fleches passent d une action a l autre, puis au travail', () => {
    const p = rendre(vue({ texte: 'fac', actions: [factures, encaissements], ...surDossier }))
    p.touche('ArrowDown')
    p.touche('Enter')
    p.touche('ArrowDown')
    p.touche('Enter')
    expect(p.appels()).toEqual([['actionDossier', 'encaissements'], ['paletteAller', { nature: 'dem', cle: '004151' }]])
  })

  it('avant de taper : toutes les actions de l affaire, puis le travail ; l apercu suit l action choisie', () => {
    const p = rendre(vue({ texte: '', pieces: [], horsGreffe: [], actions: [factures, encaissements], ...surDossier }))
    expect(p.titres()).toEqual(['Sur ce dossier2', 'A traiter maintenant2'])
    expect(p.apercu()).toContain('Apercu · action sur le dossier')
    p.touche('ArrowDown')
    p.touche('ArrowDown')
    expect(p.apercu()).toContain('Mon travail · mercredi 9 septembre')
  })

  it('une action de copie copie dans la page, sans remonter au C#', () => {
    const ecrire = vi.fn<(texte: string) => Promise<void>>().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: ecrire }, configurable: true })
    const p = rendre(vue({ texte: 'siren', travail: [], pieces: [], horsGreffe: [],
                           actions: [action('copierSiren', 'Suivre', 'Copier le SIREN', 'S', false, '812 445 901')], ...surDossier }))
    p.touche('Enter')
    expect(ecrire).toHaveBeenCalledWith('812 445 901')
    expect(p.appels()).toEqual([])
    expect(p.container.querySelector('.rig-palette__copie')!.textContent).toBe('SIREN copie')
  })

  it('seules des actions repondent : pas de « Aucun resultat »', () => {
    const p = rendre(vue({ texte: 'fac', travail: [], pieces: [], horsGreffe: [], actions: [factures] }))
    expect(p.titres()).toEqual(['Sur ce dossier1'])
    expect(p.container.querySelector('.rig-palette__rien')).toBeNull()
  })

  it('hors affaire (aucune action dans la vue) : la palette est inchangee', () => {
    const p = rendre(vue({ texte: 'fac', actions: [] }))
    expect(p.titres()).toEqual(['Le plus urgent d abord2', 'Dans le texte des pieces1', 'Hors du greffe1'])
    expect(p.container.textContent).not.toContain('Sur ce dossier')
  })
})
