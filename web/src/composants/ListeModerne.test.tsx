import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { createRoot } from 'react-dom/client'
import {
  CartesChiffrees, CelluleChevron, CelluleIdentite, ChoixTri, EnteteListe, LigneDetail, ListeModerne, MenuLigne, MontantBarre,
  Pastille, PiedListe, PointEtat, TeintesAvatar, VuesListe, initiales, montant, normaliser, oublierVuesDeSession, rangAvatar,
  teinteStatut, texteCsv, useVuesDeSession, type VuesDeSession,
} from './ListeModerne'
import { EcranVisible } from '../plan/visibilite'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

// La feuille du socle, lue sur le disque : wwwroot/ est hors de la racine de Vite, qui refuse de l'importer en ?raw
// (et sous jsdom, URL est celle de jsdom, que readFileSync ne prend pas : on passe un chemin).
const feuille = readFileSync(join(import.meta.dirname, '../../../wwwroot/rig-listes.css'), 'utf8')
// La feuille des ecrans, ou vit le panneau des criteres qui se pose a cote de la liste.
const feuilleEcrans = readFileSync(join(import.meta.dirname, '../../../wwwroot/rig-accueil.css'), 'utf8')

// fix-ok: les manques de ce socle ont ete MESURES par les lots 2 a 7 du plan des listes modernes (decisions notees du
// plan-9386cb20) : chaque test ci-dessous nomme son lot, sa mesure et l'entree qui le ferait echouer sur l'ancien code.
afterEach(cleanup)

/** Une regle de rig-listes.css : media = le texte de la regle @media qui l'entoure, null hors de toute regle @media. */
interface Regle { media: string | null; selecteurs: string[]; declarations: Record<string, string> }

/** Les regles d'une feuille (rig-listes.css par defaut), dans l'ordre de la feuille, commentaires retires. */
function regles(texteFeuille: string = feuille): Regle[] {
  const sortie: Regle[] = []
  const parcourir = (texte: string, dans: string | null) => {
    let i = 0
    for (;;) {
      const ouvre = texte.indexOf('{', i)
      if (ouvre < 0) return
      let profondeur = 1
      let j = ouvre + 1
      for (; j < texte.length && profondeur > 0; j++) profondeur += texte[j] === '{' ? 1 : texte[j] === '}' ? -1 : 0
      const tete = texte.slice(i, ouvre).trim()
      const corps = texte.slice(ouvre + 1, j - 1)
      if (tete.startsWith('@media')) parcourir(corps, tete)
      else {
        const declarations: Record<string, string> = {}
        for (const d of corps.split(';')) {
          const deux = d.indexOf(':')
          if (deux > 0) declarations[d.slice(0, deux).trim()] = d.slice(deux + 1).trim()
        }
        sortie.push({ media: dans, selecteurs: tete.split(',').map((s) => s.trim().replace(/\s+/g, ' ')), declarations })
      }
      i = j
    }
  }
  parcourir(texteFeuille.replace(/\/\*[\s\S]*?\*\//g, ''), null)
  return sortie
}

/**
 * Les declarations d'un selecteur de rig-listes.css, dans l'ordre de la feuille (la derniere l'emporte, comme la
 * cascade a poids egal). media = le texte de la regle @media qui l'entoure, null hors de toute regle @media.
 * Ce que jsdom ne dessine pas (une hauteur, un alignement) se verifie ainsi sur la feuille elle-meme.
 */
function declarations(selecteur: string, media: string | null = null, texteFeuille: string = feuille): Record<string, string> {
  const sortie: Record<string, string> = {}
  for (const r of regles(texteFeuille)) if (r.media === media && r.selecteurs.includes(selecteur)) Object.assign(sortie, r.declarations)
  return sortie
}

const ETROIT = '@media (max-width: 1100px)'

/** Une cellule du socle se pose dans une vraie ligne de tableau ; la ligne note ses clics. */
function dansUneLigne(cellules: ReactNode, surClic = vi.fn(), surDouble = vi.fn()) {
  return render(<table><tbody><tr onClick={surClic} onDoubleClick={surDouble}>{cellules}</tr></tbody></table>)
}

describe('ListeModerne', () => {
  it('le cadre : la colonne des resultats en carte de surface, nommee', () => {
    render(<ListeModerne etiquette="Affaires"><p>contenu</p></ListeModerne>)
    const cadre = screen.getByRole('region', { name: 'Affaires' })
    expect(cadre.tagName).toBe('SECTION')
    expect(cadre.className).toBe('rig-resultats-proc rig-lm')
    expect(cadre.textContent).toBe('contenu')
  })

  // Lot 7 du plan, mesure a 900x700 : overflow:hidden coupait tout ce qui passait sous la carte (pied a 1236 px,
  // carte finie a 688, rien ne defilait). Entree qui ferait echouer ce test si la correction etait fausse : une
  // feuille ou .rig-lm reste en overflow:hidden, ou ou la carte garde la hauteur plancher de 300 px en fenetre etroite.
  it('le cadre ne coupe rien : coins arrondis gardes, contenu atteignable ; en fenetre etroite, la carte prend sa hauteur', () => {
    const cadre = declarations('.rig-lm')
    expect(cadre['border-radius']).toBe('12px')
    // auto coupe aux coins arrondis comme hidden, mais ce qui depasse se fait defiler au lieu de disparaitre.
    expect(cadre.overflow).toBe('auto')
    // Sous 1100 px le corps empile et defile : la carte doit faire la hauteur de son contenu (pied compris),
    // pas les 300 px de .rig-proc__corps > .rig-resultats-proc.
    expect(declarations('.rig-proc__corps > .rig-resultats-proc.rig-lm', ETROIT).flex).toBe('1 0 auto')
  })

  // Capture du 25/11/2026 (Recherche d'affaires) : les criteres de gauche restaient a meme le fond gris, champs gris
  // sur gris, a cote de la liste blanche. Entree qui ferait echouer ce test : une feuille ou .rig-criteres n'a pas le
  // fond, les coins ou le filet de .rig-lm (l'ancien code n'avait qu'un filet a droite, sans fond ni coins).
  it('les criteres de gauche sont la meme carte que la liste : fond, coins et filet identiques', () => {
    const liste = declarations('.rig-lm')
    const criteres = declarations('.rig-criteres', null, feuilleEcrans)
    for (const propriete of ['background', 'border-radius', 'box-shadow']) {
      expect({ propriete, valeur: criteres[propriete] }).toEqual({ propriete, valeur: liste[propriete] })
    }
    // Leurs champs sont blancs sur blanc, pas le gris du fond de l'ecran.
    expect(declarations('.rig-criteres .rig-saisie-champ__saisie', null, feuilleEcrans).background).toBe('var(--rig-surface)')
  })

  // Relecture du lot 8, capture a 900x700 (d2-rechaff-bas.png) : la grille bornee a 70vh defilait DANS la colonne
  // qui defile. Colonne descendue, l'en-tete collant de la grille restait colle au haut de SA zone : coupe a moitie
  // par le bord de la colonne, pose sur la premiere ligne. Entree qui ferait echouer ce test si la correction etait
  // fausse : une regle de fenetre etroite qui rend a .rig-table-cadre une hauteur bornee (max-height: 70vh, l'ancien
  // code) ou fixe (height).
  it('en fenetre etroite, une seule zone defile : la grille ne borne pas sa hauteur dans la colonne', () => {
    const bornees = regles()
      .filter((r) => r.media === ETROIT && r.selecteurs.some((s) => s.includes('.rig-table-cadre')))
      .filter((r) => 'max-height' in r.declarations || 'height' in r.declarations)
      .map((r) => `${r.selecteurs.join(', ')} { max-height: ${r.declarations['max-height']}; height: ${r.declarations.height} }`)
    expect(bornees).toEqual([])
  })
})

describe('outils de texte', () => {
  it('initiales : les deux premiers mots qui comptent, sans forme juridique ni mot de liaison (avatars de la maquette)', () => {
    expect(initiales('AMIENS BILLARDS')).toBe('AB')
    expect(initiales('BATIPRO SARL')).toBe('B')
    expect(initiales('SCI DU PARC')).toBe('P')
    expect(initiales('MENUISERIE PICARDE')).toBe('MP')
    expect(initiales('MARTIN EURL')).toBe('M')
    expect(initiales('COMPTOIR DU NORD')).toBe('CN')
    expect(initiales('SOMME LOGISTIQUE')).toBe('SL')
    expect(initiales('LEROY Michel')).toBe('LM')
    expect(initiales('Société Générale')).toBe('SG')
    // Un nom fait SEULEMENT de mots sans initiale en garde quand meme une ; un nom vide, un « ? ».
    expect(initiales('SA')).toBe('S')
    expect(initiales('')).toBe('?')
  })

  // Lot 2 (capture Gestion des audiences) : « Mme LEROY » donnait ML, « M. DUVAL » MD. Entree qui ferait echouer
  // ce test si la correction etait fausse : « Mme LEROY » (ou « Maître DUPONT », accent compris).
  it('initiales : une civilite ne donne pas d\'initiale (juges, experts)', () => {
    expect(initiales('Mme LEROY')).toBe('L')
    expect(initiales('M. DUVAL')).toBe('D')
    expect(initiales('MLLE PETIT Anne')).toBe('PA')
    expect(initiales('Me MARTIN')).toBe('M')
    expect(initiales('Maître DUPONT Paul')).toBe('DP')
    expect(initiales('Dr BERNARD')).toBe('B')
    // Une civilite seule garde son initiale ; un mot qui COMMENCE comme une civilite reste un mot.
    expect(initiales('M.')).toBe('M')
    expect(initiales('MERCIER DRUON')).toBe('MD')
  })

  // Lot 6 : chaque ecran recopiait le normaliser() de la Recherche d'affaires. Entree qui ferait echouer ce test :
  // « Clôturée » (accent et casse) contre « cloturee ».
  it('normaliser : sans casse ni accents, la cle du filtre « Aller a… »', () => {
    expect(normaliser('Clôturée ÉLAN Dépôt')).toBe('cloturee elan depot')
    expect(normaliser('Clôturée').includes(normaliser('CLOTUREE'))).toBe(true)
    expect(normaliser('')).toBe('')
  })

  // Lot 3 : le capital EUR/CHF/USD de l'Ouverture de dossier se formatait a part. Entree qui ferait echouer ce
  // test : 50 000 CHF, que l'ancien montant() ecrivait « 50 000,00 € ».
  it('montant : une autre devise et un nombre de decimales, l\'euro a deux decimales par defaut', () => {
    expect(montant(50000, { devise: 'CHF', decimales: 0 })).toBe('50 000 CHF')
    expect(montant(11000, { devise: 'EUR', decimales: 0 })).toBe('11 000 EUR')
    expect(montant(0.5, { devise: 'USD' })).toBe('0,50 USD')
    expect(montant(1379.3, {})).toBe('1 379,30 €')
  })

  // Lot 6 : CoffreFort, DepotsJud, GedJud, Supervision et Verrous recopiaient la meme table. Entree qui ferait
  // echouer ce test : « attente », qui doit prendre l'orange, et un statut inconnu, qui doit prendre le gris.
  it('teinteStatut : les statuts de la charte (rig-etat--*) en teinte de liste, gris pour le reste', () => {
    expect(['ok', 'alerte', 'action', 'attente'].map(teinteStatut)).toEqual(['vert', 'rouge', 'bleu', 'orange'])
    expect(teinteStatut('')).toBe('gris')
    expect(teinteStatut(null)).toBe('gris')
    expect(teinteStatut(undefined)).toBe('gris')
    expect(teinteStatut('inconnu')).toBe('gris')
  })

  it('rangAvatar : une teinte stable par nom, toujours dans la palette declaree', () => {
    const noms = ['AMIENS BILLARDS', 'BATIPRO SARL', 'SCI DU PARC', 'MARTIN EURL', 'COMPTOIR DU NORD', 'X', '']
    for (const nom of noms) {
      const rang = rangAvatar(nom)
      expect(rang).toBeGreaterThanOrEqual(1)
      expect(rang).toBeLessThanOrEqual(TeintesAvatar)
      expect(rangAvatar(nom)).toBe(rang)
    }
    expect(rangAvatar('amiens billards ')).toBe(rangAvatar('AMIENS BILLARDS'))
    expect(new Set(noms.map(rangAvatar)).size).toBeGreaterThan(1)
  })

  it('montant : a la francaise, deux decimales, euro', () => {
    expect(montant(1379.3).replace(/\s/g, ' ')).toBe('1 379,30 €')
    expect(montant(0)).toBe('0,00 €')
  })

  it('montant : milliers separes par une espace insecable normale, pas l\'espace fine quasi invisible a 12 px', () => {
    expect(montant(1379.3)).toBe('1\u00a0379,30 €')
    expect(montant(1379.3)).not.toContain('\u202f')
  })

  it('texteCsv : point-virgule, guillemets doubles seulement quand il le faut', () => {
    expect(texteCsv(['N°', 'Libelle'], [['004176', 'Modification - SCI "DU" PARC'], ['1', 'a;b']]))
      .toBe('N°;Libelle\r\n004176;"Modification - SCI ""DU"" PARC"\r\n1;"a;b"')
  })
})

describe('EnteteListe', () => {
  it('titre, synthese et champ « Aller a… » qui filtre ; Echap vide le champ sans aller plus loin', () => {
    const changer = vi.fn()
    const valider = vi.fn()
    render(<EnteteListe titre="Affaires" synthese={<>15 resultats</>} detail="7 instance"
                        filtre={{ valeur: 'sci', changer, indication: 'Aller a une affaire…', valider }} />)
    expect(screen.getByText('Affaires').className).toBe('rig-lm-titre')
    expect(screen.getByText('15 resultats').title).toBe('7 instance')
    const champ = screen.getByLabelText('Aller a une affaire…')
    fireEvent.change(champ, { target: { value: 'scib' } })
    expect(changer).toHaveBeenLastCalledWith('scib')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(valider).toHaveBeenCalledTimes(1)
    expect(fireEvent.keyDown(champ, { key: 'Escape' })).toBe(false)
    expect(changer).toHaveBeenLastCalledWith('')
  })

  it('Echap dans un champ deja vide n\'est pas retenu : l\'application le recoit', () => {
    render(<EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />)
    expect(fireEvent.keyDown(screen.getByLabelText('Aller a…'), { key: 'Escape' })).toBe(true)
  })

  it('Ctrl K pose le curseur dans le champ et garde la touche (la palette ne s\'ouvre pas)', () => {
    render(<EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />)
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(screen.getByLabelText('Aller a…'))
  })

  // Rouge intermittent d'EcranSupervision.test.tsx dans la suite complete, sous la charge de tests.ps1 (25/11/2026) :
  // le champ etait affiche, mais l'ecoute de Ctrl K, posee par un effet DIFFERE, ne l'etait pas encore. Ici, Ctrl K
  // part dans la micro-tache ou le champ apparait, avant tout effet differe. Entree qui ferait echouer ce test :
  // l'ecoute posee par useEffect (touche non prise).
  it('Ctrl K est pris des que le champ est affiche, pas un effet differe plus tard', async () => {
    const drapeau = globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean | undefined }
    const avant = drapeau.IS_REACT_ACT_ENVIRONMENT
    drapeau.IS_REACT_ACT_ENVIRONMENT = false
    const hote = document.body.appendChild(document.createElement('div'))
    const racine = createRoot(hote)
    try {
      const pris = await new Promise<boolean>((fini) => {
        const veille = new MutationObserver(() => {
          if (!hote.querySelector('input')) return
          veille.disconnect()
          const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
          window.dispatchEvent(touche)
          fini(touche.defaultPrevented)
        })
        veille.observe(hote, { childList: true, subtree: true })
        racine.render(<EnteteListe titre="Affaires" filtre={{ valeur: '', changer: () => {}, indication: 'Aller a…' }} />)
      })
      expect(pris).toBe(true)
    } finally {
      racine.unmount()
      hote.remove()
      drapeau.IS_REACT_ACT_ENVIRONMENT = avant
    }
  })

  it('ecran cache (onglet non montre) : Ctrl K n\'est ni pris ni detourne', () => {
    render(<EcranVisible.Provider value={false}>
      <EnteteListe titre="Affaires" filtre={{ valeur: '', changer: vi.fn(), indication: 'Aller a…' }} />
    </EcranVisible.Provider>)
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(false)
    expect(document.activeElement).not.toBe(screen.getByLabelText('Aller a…'))
  })

  it('sans filtre, ni champ ni Ctrl K', () => {
    render(<EnteteListe titre="Factures" />)
    expect(screen.queryByRole('textbox')).toBeNull()
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(false)
  })
})

describe('CartesChiffrees', () => {
  it('clic carte, segment ou legende : chacun son filtre, le segment ne declenche pas la carte', () => {
    const carte = vi.fn()
    const instance = vi.fn()
    const mesure = vi.fn()
    const { container } = render(<CartesChiffrees cartes={[
      { cle: 'solde', titre: 'Solde total', valeur: '1 379,30 €', legende: true, choisir: carte, actif: false, segments: [
        { cle: 'instance', libelle: 'Instance', valeur: 334.3, teinte: 'bleu', texte: '334,30 €', actif: true, choisir: instance },
        { cle: 'mesure', libelle: 'Mesure', valeur: 0, teinte: 'vert', texte: '0,00 €', choisir: mesure },
      ] },
      { cle: 'en-cours', titre: 'En cours', valeur: 12, complement: 'sur 15', anneau: { part: 12, total: 15 } },
    ]} />)

    // Un segment a zero ne se dessine pas (largeur nulle), sa legende reste et filtre.
    const segments = container.querySelectorAll('.rig-lm-segment')
    expect(segments).toHaveLength(1)
    expect(segments[0]!.className).toContain('rig-lm-t-bleu')
    expect(segments[0]!.className).toContain('rig-lm-segment--actif')
    fireEvent.click(segments[0]!)
    expect(instance).toHaveBeenCalledTimes(1)
    expect(carte).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Mesure 0,00 €' }))
    expect(mesure).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Instance 334,30 €', pressed: true })).toBeTruthy()

    fireEvent.click(screen.getByText('Solde total'))
    expect(carte).toHaveBeenCalledTimes(1)
    fireEvent.keyDown(screen.getByText('Solde total').parentElement!, { key: 'Enter' })
    expect(carte).toHaveBeenCalledTimes(2)

    // La carte sans filtre n'est pas un bouton ; son anneau dit 12 sur 15.
    const enCours = screen.getByText('En cours').parentElement!
    expect(enCours.getAttribute('role')).toBeNull()
    expect(enCours.textContent).toBe('En cours12 sur 15')
    const plein = enCours.querySelector('.rig-lm-anneau__plein')!.getAttribute('stroke-dasharray')!
    expect(Number(plein.split(' ')[0]) / Number(plein.split(' ')[1])).toBeCloseTo(12 / 15, 2)
  })

  // Vu par le chef en fenetre etroite (Encaissements) : une carte retrecie coupait son montant (« 200,00 … »).
  // Entree qui ferait echouer ce test : une carte restee en min-width:0.
  it('une carte ne retrecit pas sous son contenu : elle passe a la ligne au lieu de couper son chiffre', () => {
    expect(declarations('.rig-lm-carte')['min-width']).toBe('min-content')
    expect(declarations('.rig-lm-cartes')['flex-wrap']).toBe('wrap')
  })
})

describe('VuesListe et ChoixTri', () => {
  it('puces avec compte, puce active, choix d\'une vue', () => {
    const choisir = vi.fn()
    render(<VuesListe vues={[{ cle: 'toutes', libelle: 'Toutes', compte: 15 }, { cle: 'clo', libelle: 'Cloturees', compte: 2 },
                             { cle: 'neuve', libelle: 'Sans compte' }]} active="toutes" choisir={choisir} />)
    expect(screen.getByRole('button', { name: 'Toutes 15', pressed: true })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Cloturees 2', pressed: false })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sans compte' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Cloturees 2' }))
    expect(choisir).toHaveBeenCalledWith('clo')
    // Sans « enregistrer », pas de « + Nouvelle vue ».
    expect(screen.queryByText('+ Nouvelle vue')).toBeNull()
  })

  it('« + Nouvelle vue » demande un nom : Entree enregistre, Echap ou un nom vide n\'enregistrent rien', () => {
    const enregistrer = vi.fn()
    render(<VuesListe vues={[]} active={null} choisir={vi.fn()} enregistrer={enregistrer}>
      <button>Exporter</button>
    </VuesListe>)
    expect(screen.getByText('Exporter').parentElement!.className).toBe('rig-lm-outils')

    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Escape' })
    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(enregistrer).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: '+ Nouvelle vue' }))
    fireEvent.change(screen.getByLabelText('Nom de la nouvelle vue'), { target: { value: '  Mes RCS ' } })
    fireEvent.keyDown(screen.getByLabelText('Nom de la nouvelle vue'), { key: 'Enter' })
    expect(enregistrer).toHaveBeenCalledWith('Mes RCS')
    expect(screen.queryByLabelText('Nom de la nouvelle vue')).toBeNull()
  })

  // Lot 2 (Experts) : deux groupes de puces dans une liste s'appelaient tous deux « Vues ». Entree qui ferait
  // echouer ce test : etiquette="Vues des mesures", rendue « Vues » par l'ancien composant.
  it('l\'etiquette nomme le groupe de puces (« Vues » par defaut) : deux groupes d\'une liste se distinguent', () => {
    render(<>
      <VuesListe vues={[{ cle: 'a', libelle: 'Actifs' }]} active="a" choisir={vi.fn()} />
      <VuesListe etiquette="Vues des mesures" vues={[{ cle: 'm', libelle: 'Mesures en cours' }]} active="m" choisir={vi.fn()} />
    </>)
    expect(screen.getByRole('group', { name: 'Vues' }).textContent).toBe('Actifs')
    expect(screen.getByRole('group', { name: 'Vues des mesures' }).textContent).toBe('Mesures en cours')
  })

  it('« Trier » : l\'ordre d\'origine puis les cles de l\'ecran ; le sens s\'inverse seulement sur une cle', () => {
    const choisir = vi.fn()
    const inverser = vi.fn()
    const { rerender } = render(<ChoixTri cles={[['Nom', 'Nom'], ['Solde', 'Solde']]} cleActive={null} ascendant
                                          choisir={choisir} inverser={inverser} origine="Plus recentes" />)
    const liste = screen.getByLabelText('Trier') as HTMLSelectElement
    expect([...liste.options].map((o) => o.text)).toEqual(['Plus recentes', 'Nom', 'Solde'])
    expect((screen.getByRole('button', { name: /inverser/ }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(liste, { target: { value: 'Solde' } })
    expect(choisir).toHaveBeenLastCalledWith('Solde')

    rerender(<ChoixTri cles={[['Nom', 'Nom'], ['Solde', 'Solde']]} cleActive="Solde" ascendant={false}
                       choisir={choisir} inverser={inverser} origine="Plus recentes" />)
    fireEvent.click(screen.getByRole('button', { name: 'Ordre decroissant, inverser' }))
    expect(inverser).toHaveBeenCalledTimes(1)
    fireEvent.change(screen.getByLabelText('Trier'), { target: { value: '' } })
    expect(choisir).toHaveBeenLastCalledWith(null)
  })
})

describe('useVuesDeSession', () => {
  afterEach(() => oublierVuesDeSession())
  interface Criteres { nature: string; soldeMin: string }
  const memes = (a: Criteres, b: Criteres) => a.nature === b.nature && a.soldeMin === b.soldeMin
  const prereglees = [{ cle: 'toutes', libelle: 'Toutes', criteres: { nature: '', soldeMin: '' } }]
  let dernier: VuesDeSession<Criteres> | null = null
  function Ecran({ ecran, criteres }: { ecran: string; criteres: Criteres }) {
    dernier = useVuesDeSession(ecran, prereglees, criteres, memes)
    return <ul>{dernier.vues.map((v) => <li key={v.cle}>{v.libelle}</li>)}</ul>
  }

  // Lot 3 : six ecrans recopiaient leur tableau de module, son « enregistrer » et son « oublier ». Entree qui
  // ferait echouer ce test : fermer puis rouvrir l'ecran (la vue doit rester), ou un autre ecran (il ne la voit pas).
  it('enregistre les criteres courants pour la session : la vue survit a la fermeture de l\'ecran, pas aux autres ecrans', () => {
    const vue = render(<Ecran ecran="RechAffaires" criteres={{ nature: 'collective', soldeMin: '' }} />)
    expect(dernier!.active).toBeNull()
    act(() => dernier!.enregistrer('Mes collectives'))
    expect(dernier!.vues.map((v) => v.cle)).toEqual(['toutes', 'session-1'])
    expect(dernier!.vues[1]!.criteres).toEqual({ nature: 'collective', soldeMin: '' })
    // Les criteres courants sont ceux de la vue enregistree : elle est la vue active.
    expect(dernier!.active).toBe('session-1')

    // Fermer l'onglet puis le rouvrir : la vue est toujours la (tableau de module), avec sa cle.
    vue.unmount()
    render(<Ecran ecran="RechAffaires" criteres={{ nature: '', soldeMin: '' }} />)
    expect(screen.getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Toutes', 'Mes collectives'])
    expect(dernier!.active).toBe('toutes')
    act(() => dernier!.enregistrer('Deuxieme'))
    expect(dernier!.vues.map((v) => v.cle)).toEqual(['toutes', 'session-1', 'session-2'])

    // Un autre ecran a ses propres vues.
    cleanup()
    render(<Ecran ecran="Demandes" criteres={{ nature: '', soldeMin: '' }} />)
    expect(dernier!.vues.map((v) => v.cle)).toEqual(['toutes'])
  })

  it('oublierVuesDeSession repart d\'une session sans vue ajoutee (les tests des ecrans s\'en servent)', () => {
    const premier = render(<Ecran ecran="Clients" criteres={{ nature: 'x', soldeMin: '' }} />)
    act(() => dernier!.enregistrer('X'))
    premier.unmount()
    oublierVuesDeSession()
    render(<Ecran ecran="Clients" criteres={{ nature: 'x', soldeMin: '' }} />)
    expect(dernier!.vues.map((v) => v.cle)).toEqual(['toutes'])
  })

})

describe('cellules du tableau', () => {
  it('identite, pastille, point d\'etat : avatar d\'initiales a teinte stable, nom gras, objet gris', () => {
    const { container } = dansUneLigne(<>
      <CelluleIdentite nom="SCI DU PARC" objet="Modification · 004176" titre="tout" apres={<span className="rig-badge">V2</span>} />
      <td><Pastille teinte="orange">Procedure collective</Pastille></td>
      <td><PointEtat teinte="rouge">Radiee</PointEtat></td>
    </>)
    const avatar = container.querySelector('.rig-lm-avatar')!
    expect(avatar.textContent).toBe('P')
    expect(avatar.className).toContain(`rig-lm-avatar--${rangAvatar('SCI DU PARC')}`)
    expect(screen.getByText('SCI DU PARC').tagName).toBe('B')
    expect(screen.getByText('Modification · 004176').className).toBe('rig-lm-identite__objet')
    expect(container.querySelector('td')!.title).toBe('tout')
    expect(screen.getByText('Procedure collective').className).toBe('rig-lm-pastille rig-lm-t-orange')
    expect(screen.getByText('Radiee').className).toBe('rig-lm-etat rig-lm-t-rouge')
  })

  // Lot 4 (Factures) : un article de facture n'est ni un client ni un dossier, il n'a pas d'avatar ; l'ecran
  // ecrivait la cellule a la main. Entree qui ferait echouer ce test : avatar={false}, ignore par l'ancien composant.
  it('CelluleIdentite sans avatar : nom en gras et objet en gris, sans rond d\'initiales', () => {
    const { container } = dansUneLigne(<CelluleIdentite avatar={false} nom="Frais de greffe" objet="FG01" titre="FG01 Frais de greffe" />)
    expect(container.querySelector('.rig-lm-avatar')).toBeNull()
    expect(screen.getByText('Frais de greffe').className).toBe('rig-lm-identite__nom')
    expect(screen.getByText('FG01').className).toBe('rig-lm-identite__objet')
    expect(container.querySelector('td')!.title).toBe('FG01 Frais de greffe')
  })

  // Lot 5 (Correspondances, capture orange) : un etat revenu a la ligne centrait son point ENTRE les deux lignes.
  // Entree qui ferait echouer ce test : une feuille ou .rig-lm-etat reste en align-items:center.
  it('PointEtat : le point face a la PREMIERE ligne quand le texte revient a la ligne', () => {
    expect(declarations('.rig-lm-etat')['align-items']).toBe('flex-start')
    // Le point (7 px) centre dans la hauteur d'une ligne : (1lh - 7px) / 2 au-dessus de lui.
    expect(declarations('.rig-lm-etat i')['margin-top']).toBe('calc((1lh - 7px) / 2)')
    expect(declarations('.rig-lm-etat i').height).toBe('7px')
  })

  // Lot 3 (captures orange) : en inline-flex, text-overflow n'a aucun effet, « Recommande » sortait « Recommanc ».
  // Entree qui ferait echouer ce test : une pastille restee en display:inline-flex.
  it('Pastille : un bloc en ligne qui coupe son texte par « … »', () => {
    const pastille = declarations('.rig-lm-pastille')
    expect(pastille.display).toBe('inline-block')
    expect([pastille.overflow, pastille['white-space'], pastille['text-overflow']]).toEqual(['hidden', 'nowrap', 'ellipsis'])
    // Le point garde son milieu sur celui du texte, sans conteneur flex.
    expect(declarations('.rig-lm-pastille i')['vertical-align']).toBe('middle')
  })

  it('MontantBarre : barre proportionnelle au plus grand, zero gris sans barre', () => {
    const { container } = render(<>
      <MontantBarre valeur={142.5} max={415} texte="142,50 €" />
      <MontantBarre valeur={0} max={415} texte="0,00 €" />
      <MontantBarre valeur={415} max={415} texte="415,00 €" />
    </>)
    const barres = [...container.querySelectorAll<HTMLElement>('.rig-lm-montant__barre b')].map((b) => b.style.width)
    expect(barres).toEqual(['34%', '0%', '100%'])
    expect(screen.getByText('0,00 €').className).toContain('rig-lm-montant__texte--zero')
    expect(screen.getByText('142,50 €').className).not.toContain('--zero')
  })

  it('le chevron deplie sans choisir la ligne ni l\'ouvrir', () => {
    const basculer = vi.fn()
    const surClic = vi.fn()
    const surDouble = vi.fn()
    dansUneLigne(<CelluleChevron ouverte={false} basculer={basculer} />, surClic, surDouble)
    const chevron = screen.getByRole('button', { name: 'Deplier la ligne' })
    expect(chevron.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(chevron)
    fireEvent.doubleClick(chevron)
    expect(basculer).toHaveBeenCalledTimes(1)
    expect(surClic).not.toHaveBeenCalled()
    expect(surDouble).not.toHaveBeenCalled()
  })

  it('LigneDetail couvre toutes les colonnes : champs (vide = tiret), actions', () => {
    const ouvrir = vi.fn()
    const { container } = render(<table><tbody>
      <LigneDetail colonnes={7} champs={[{ libelle: 'Id instance', valeur: '55210' }, { libelle: 'SIREN', valeur: '' }]}
                   actions={[{ libelle: 'Ouvrir', fort: true, agir: ouvrir }, { libelle: 'Historique', agir: vi.fn(), desactive: true }]}>
        <span>compte rendu</span>
      </LigneDetail>
    </tbody></table>)
    expect(container.querySelector('tr.rig-lm-detail > td')!.getAttribute('colspan')).toBe('7')
    expect([...container.querySelectorAll('.rig-lm-champ')].map((c) => c.textContent)).toEqual(['Id instance55210', 'SIREN—'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('button', { name: 'Ouvrir' }).className).toContain('rig-lm-outil--fort')
    expect((screen.getByRole('button', { name: 'Historique' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('compte rendu')).toBeTruthy()
  })

  it('MenuLigne : « … » ouvre les actions sans toucher a la ligne ; une action ferme le menu ; Echap ne va pas plus loin', () => {
    const ouvrir = vi.fn()
    const surClic = vi.fn()
    const surDouble = vi.fn()
    dansUneLigne(<td><MenuLigne libelle="Actions de 004176" actions={[{ libelle: 'Ouvrir', agir: ouvrir }, { libelle: 'Historique', agir: vi.fn() }]} /></td>,
                 surClic, surDouble)
    const bouton = screen.getByRole('button', { name: 'Actions de 004176' })
    expect(screen.queryByRole('menu')).toBeNull()
    fireEvent.click(bouton)
    fireEvent.doubleClick(bouton)
    expect(bouton.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Ouvrir', 'Historique'])
    expect(document.activeElement).toBe(screen.getByRole('menuitem', { name: 'Ouvrir' }))
    expect(fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Ouvrir' }), { key: 'Escape' })).toBe(false)
    expect(screen.queryByRole('menu')).toBeNull()

    fireEvent.click(bouton)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ouvrir' }))
    expect(ouvrir).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).toBeNull()
    expect(surClic).not.toHaveBeenCalled()
    expect(surDouble).not.toHaveBeenCalled()

    // Un clic ailleurs referme le menu.
    fireEvent.click(bouton)
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('PiedListe : les totaux puis l\'aide', () => {
    const { container } = render(<PiedListe aide="Double-clic…"><span><b>15</b> affaires</span></PiedListe>)
    expect(container.querySelector('.rig-lm-pied')!.textContent).toBe('15 affairesDouble-clic…')
    expect(screen.getByText('Double-clic…').className).toBe('rig-lm-pied__aide')
  })
})
