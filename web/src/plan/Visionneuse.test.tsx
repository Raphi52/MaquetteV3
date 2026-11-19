import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { styleDepuisTexte, Visionneuse } from './Visionneuse'
import { morceaux } from '../test/morceaux'
import { apercu, dossier, etapes, historique, ligneArbre, tache } from '../test/plan'
import type { DossierVue, GestePlan, RecapVue } from './types'

afterEach(cleanup)

// LA VISIONNEUSE D'AFFAIRE (lot 2 du passage a React). Ses regles (defauts,
// etapes, tache, colonnes) sont jouees par tests/PlanTravailCheck ; ici, le
// balisage rendu -- memes classes, MEMES MORCEAUX DE TEXTE que Razor (lus dans
// le code genere de Home.razor) -- et les gestes remontes au plan de travail.
function rendre(d: DossierVue = dossier()) {
  const plan = vi.fn<GestePlan>()
  const r = render(<Visionneuse dossier={d} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
  // « etapeVisible » part des que la zone des etapes est montee (teste a part).
  return { ...r, plan, gestes: () => plan.mock.calls.filter(([nom]) => nom !== 'etapeVisible').map(([nom, valeur]) => [nom, valeur ?? null]) }
}

describe('Visionneuse : entete, fiche sommaire en puces, points a corriger', () => {
  it('l\'entete : retour, filet du statut, identite, pastille des defauts, croix', () => {
    const { container } = rendre()
    const entete = container.querySelector('.rig-vged > .rig-vged__entete')!
    expect(morceaux(entete)).toEqual(['<button>', ' ', '<span>', ' ', '<div>', '<div>', '<button>'])
    expect(entete.children[1]!.className).toBe('rig-filet-v rig-filet-v--alerte')
    expect(morceaux(entete.querySelector('.rig-vged__titre')!)).toEqual(['<b>', ' ', '<span>'])
    expect(entete.querySelector('.rig-vged__titre')!.textContent).toBe('004176 Modification - SCI DU PARC')
    expect(entete.lastElementChild!.getAttribute('aria-label')).toBe("Fermer l'affaire")
  })

  it('dans la fenetre d\'un onglet sorti, pas de fleche « Retour » (il n\'y a pas d\'accueil) ; la croix reste', () => {
    const { container, gestes } = rendre(dossier({ sortie: true }))
    const entete = container.querySelector('.rig-vged > .rig-vged__entete')!
    expect(morceaux(entete)).toEqual([' ', '<span>', ' ', '<div>', '<div>', '<button>'])
    expect(container.querySelector('[aria-label="Retour a l\'accueil"]')).toBeNull()
    fireEvent.click(entete.lastElementChild!)
    expect(gestes()).toEqual([['fermerDossier', null]])
  })

  it('« Document » n\'apparait que si la piece est ouverte et la colonne fermee', () => {
    const { container, gestes } = rendre(dossier({ boutonDocument: true }))
    const doc = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Document')!
    fireEvent.click(doc)
    expect(gestes()).toEqual([['afficherDocument', null]])
  })

  it('la fiche sommaire : une puce par champ, sous le titre ; la mention en rouge si elle existe', () => {
    const { container, rerender, plan } = rendre()
    const puces = container.querySelectorAll('.rig-vged__entete .rig-vged__puces > .rig-vged__puce')
    expect(puces).toHaveLength(2)
    expect(morceaux(puces[0]!)).toEqual(['<span>', ' ', '<span>'])
    expect(puces[0]!.textContent).toBe('SIREN 812 445 901')
    expect(puces[0]!.lastElementChild!.className).toBe('rig-vged__puce-valeur rig-vged__puce-valeur--mono')
    expect(puces[1]!.lastElementChild!.className).toBe('rig-vged__puce-valeur')
    expect(container.querySelector('.rig-vged__mention')).toBeNull()
    const radiee = dossier()
    radiee.sommaire = { ...radiee.sommaire, mention: 'Radiee' }
    rerender(<Visionneuse dossier={radiee} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-vged__puces > .rig-vged__mention')!.textContent).toBe('Radiee')
  })

  it('les points a corriger : une pastille dans l\'entete, qui deroule les liens vers l\'etape a corriger', () => {
    const { container, gestes } = rendre()
    const defauts = container.querySelector('.rig-vged__entete > .rig-vged__defauts')!
    expect(defauts.className).toBe('rig-vged__defauts rig-vged__defauts--erreur')
    expect(defauts.getAttribute('role')).toBe('alert')
    expect(morceaux(defauts.querySelector('summary')!)).toEqual(['<svg>', '<b>', ' ', 'point a corriger'])
    const lien = defauts.querySelector('.rig-vged__defauts-lien')!
    expect(lien.className).toBe('rig-vged__defauts-lien rig-vged__defauts-lien--refuse')
    expect(morceaux(lien)).toEqual(['Identite', ' ', '<span>'])
    fireEvent.click(lien)
    expect(gestes()).toEqual([['allerAuDefautBandeau', { etape: 1, fiche: 0 }]])
  })

  it('la liste des points a corriger se referme au clic ailleurs et apres le choix d\'un lien', () => {
    const { container } = rendre()
    const liste = container.querySelector('.rig-vged__defauts details') as HTMLDetailsElement
    liste.open = true
    fireEvent.pointerDown(liste.querySelector('summary')!)
    expect(liste.open).toBe(true)
    fireEvent.pointerDown(container.querySelector('.rig-vged__arbre')!)
    expect(liste.open).toBe(false)
    liste.open = true
    fireEvent.click(liste.querySelector('.rig-vged__defauts-lien')!)
    expect(liste.open).toBe(false)
  })

  it('sans defaut, pas de pastille', () => {
    const { container } = rendre(dossier({ bandeau: null }))
    expect(container.querySelector('.rig-vged__defauts')).toBeNull()
  })
})

describe('Visionneuse : l\'historique des pieces, dans l\'onglet Suivi', () => {
  it('TOUTES les pieces : date, point, puis libelle et origine sur deux lignes ; la piece ouverte marquee', () => {
    const { container } = rendre()
    const milieu = container.querySelector('.rig-vged__corps > .rig-vged__milieu')!
    expect([...milieu.children].map((e) => e.className)).toEqual(['rig-vged__guide rig-vged__donnees rig-accordeon'])
    const suivi = container.querySelector('.rig-vged__corps > .rig-vged__suivi')!
    expect(suivi.querySelector('.rig-chrono__titre')!.textContent).toBe('Historique des pieces · 9')
    const pieces = suivi.querySelectorAll('.rig-chrono__piece')
    expect(pieces).toHaveLength(9)
    // Plus de « tout voir » : la liste defile dans sa carte.
    expect(suivi.querySelector('.rig-chrono__tout')).toBeNull()
    expect(morceaux(pieces[0]!)).toEqual(['<span>', '<span>', '<span>'])
    expect(morceaux(pieces[0]!.querySelector('.rig-chrono__texte'))).toEqual(['<span>', '<span>'])
    expect(pieces[0]!.querySelector('.rig-chrono__libelle')!.textContent).toBe('Kbis a jour')
    expect(pieces[0]!.querySelector('.rig-chrono__origine')!.textContent).toBe('Edition greffe')
    expect(pieces[0]!.className).toBe('rig-chrono__piece rig-chrono__piece--actif')
    expect(pieces[0]!.getAttribute('aria-current')).toBe('true')
    expect(pieces[0]!.getAttribute('title')).toBe('19/11/2026 · Dossier RCS · Edition greffe · 1 p.')
    // L'annee en tete de ses pieces, comme dans le draft : 2026 (2 pieces), 2024 (6), puis 2023.
    expect([...suivi.querySelectorAll('.rig-chrono__liste > li')].map((l) => l.className || l.querySelector('.rig-chrono__date')!.textContent))
      .toEqual(['rig-chrono__annee', '19/11', '15/09', 'rig-chrono__annee', '12/12', '15/11', '12/11', '15/10', '08/10', '18/06',
                'rig-chrono__annee', '03/03'])
    expect([...suivi.querySelectorAll('.rig-chrono__annee')].map((a) => a.textContent)).toEqual(['2026', '2024', '2023'])
    expect(pieces[0]!.querySelector('.rig-chrono__point')!.className).toBe('rig-chrono__point rig-chrono__point--rcs')
    expect(pieces[1]!.className).toBe('rig-chrono__piece')
  })

  it('le point d\'une piece rangee dans une rubrique prend la couleur du lisere de l\'arbre', () => {
    const { container } = rendre()
    const pieces = container.querySelectorAll('.rig-chrono__piece')
    // n12 : « Depots sur la procedure », sous la rubrique Procedures collectives (teinte 7).
    const point = pieces[2]!.querySelector('.rig-chrono__point') as HTMLElement
    expect(point.className).toBe('rig-chrono__point rig-chrono__point--jud rig-chrono__point--groupe')
    expect(point.style.getPropertyValue('--rig-chrono-teinte')).toBe('var(--rig-groupe-7)')
    // Le Kbis a jour, hors rubrique, garde l'or du dossier RCS.
    expect((pieces[0]!.querySelector('.rig-chrono__point') as HTMLElement).style.getPropertyValue('--rig-chrono-teinte')).toBe('')
  })

  it('un clic sur une piece remonte « historiqueOuvrir » avec son identifiant', () => {
    const { container, gestes } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-chrono__piece')[3]!)
    expect(gestes()).toEqual([['historiqueOuvrir', 'n13']])
  })

  it('aucune piece datee : pas d\'historique', () => {
    const { container, rerender, plan } = rendre(dossier({ historique: dossier().historique.slice(0, 2) }))
    expect(container.querySelectorAll('.rig-chrono__piece')).toHaveLength(2)
    rerender(<Visionneuse dossier={dossier({ historique: [] })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-chrono')).toBeNull()
  })

  it('colonne des donnees fermee : l\'historique reste dans la colonne de suivi', () => {
    const { container } = rendre(dossier({ etapes: null, fiche: null, poignee2: false }))
    expect(container.querySelector('.rig-vged__milieu')).toBeNull()
    expect(container.querySelector('.rig-vged__suivi .rig-chrono')).not.toBeNull()
  })

  it('la piece ouverte change : l\'arbre defile jusqu\'a sa ligne choisie, une seule fois', () => {
    const defiler = vi.fn()
    const original = Element.prototype.scrollIntoView
    Element.prototype.scrollIntoView = defiler
    try {
      const { container, rerender, plan } = rendre()
      defiler.mockClear()
      const rapport = dossier({
        arbre: ligneArbre('n1', '004176 Modification - SCI DU PARC', [{ ...ligneArbre('n13', 'Depot du rapport du mandataire', [], 1), actif: true }]),
        historique: historique().map((p) => ({ ...p, actif: p.id === 'n13' })),
      })
      const vue = (d: DossierVue) => <Visionneuse dossier={d} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />
      rerender(vue(rapport))
      expect(defiler).toHaveBeenCalledTimes(1)
      expect(defiler.mock.contexts[0]).toBe(container.querySelector('.rig-vged__arbre .rig-arbre__ligne--actif'))
      expect(defiler).toHaveBeenCalledWith({ block: 'nearest' })
      rerender(vue({ ...rapport }))
      expect(defiler).toHaveBeenCalledTimes(1)
    } finally {
      Element.prototype.scrollIntoView = original
    }
  })
})

describe('Visionneuse : les colonnes', () => {
  it('le style des colonnes calcule par le C# devient le style du corps', () => {
    expect(styleDepuisTexte('grid-template-columns: 400px 6px minmax(0, 1fr); user-select: none'))
      .toEqual({ gridTemplateColumns: '400px 6px minmax(0, 1fr)', userSelect: 'none' })
    const { container } = rendre()
    expect((container.querySelector('.rig-vged__corps') as HTMLElement).style.gridTemplateColumns)
      .toBe('400px 6px 420px 6px minmax(0px, 1fr)')
  })

  it('arbre, poignee, donnees, poignee, document, poignee, suivi : dans cet ordre', () => {
    const { container } = rendre()
    expect([...container.querySelector('.rig-vged__corps')!.children].map((e) => e.className)).toEqual([
      'rig-vged__arbre', 'rig-vged__poignee', 'rig-vged__milieu', 'rig-vged__poignee',
      'rig-vged__guide rig-vged__apercu', 'rig-vged__poignee rig-vged__poignee--suivi', 'rig-vged__suivi',
    ])
  })

  it('les poignees remontent leur colonne et l\'abscisse ; le voile suit le pointeur puis lache', () => {
    const { container, gestes } = rendre(dossier({ voile: true }))
    const [p1, p2, p3] = container.querySelectorAll('.rig-vged__poignee')
    // La troisieme envoie aussi la largeur RENDUE du suivi : sa largeur souple
    // (un cinquieme de la fenetre) n'est pas connue du C#.
    const suivi = container.querySelector('.rig-vged__suivi')!
    suivi.getBoundingClientRect = () => ({ width: 312 }) as DOMRect
    fireEvent.pointerDown(p1!, { clientX: 400 })
    fireEvent.pointerDown(p2!, { clientX: 820 })
    fireEvent.pointerDown(p3!, { clientX: 1500 })
    const voile = container.querySelector('.rig-vged__voile')!
    fireEvent.pointerMove(voile, { clientX: 450, clientY: 10 })
    fireEvent.pointerUp(voile)
    expect(gestes()).toEqual([
      ['redimDebut', { colonne: 1, x: 400 }], ['redimDebut', { colonne: 2, x: 820 }],
      ['redimDebut', { colonne: 3, x: 1500, largeur: 312 }],
      ['redim', { x: 450, y: 10 }], ['redimFin', null],
    ])
  })

  it('l\'entete de l\'arbre, le filtre et sa croix', () => {
    const { container, gestes, rerender, plan } = rendre()
    expect(morceaux(container.querySelector('.rig-arbre__entete')!)).toEqual(['<span>', ' ', '<button>', ' ', '<button>'])
    expect(container.querySelector('.rig-arbre__entete .rig-arbre__libelle')!.textContent).toBe('Visualisation du dossier · 42 pieces')
    const filtre = container.querySelector('.rig-arbre__filtre') as HTMLInputElement
    expect(filtre.getAttribute('placeholder')).toBe("Filtrer l'arbre...")
    expect(container.querySelector('.rig-arbre__filtre-effacer')).toBeNull()
    fireEvent.input(filtre, { target: { value: 'Kb' } })
    rerender(<Visionneuse dossier={dossier({ filtre: 'Kb' })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    fireEvent.click(container.querySelector('.rig-arbre__filtre-effacer')!)
    expect(gestes()).toEqual([['filtre', 'Kb'], ['effacerFiltre', null]])
  })

  it('le classeur est rendu sous .rig-arbre et ses gestes partent au plan de travail', () => {
    const { container, gestes } = rendre()
    // Le classeur ne montre plus la ligne racine (l'affaire, deja dans l'entete) : la piece seule.
    const lignes = container.querySelectorAll('.rig-arbre .rig-arbre__ligne')
    expect([...lignes].map((l) => l.textContent!.trim())).toEqual(['Statuts signes⋮'])
    fireEvent.click(lignes[0]!)
    expect(gestes()[0]).toEqual(['arbreActiver', 'n2'])
  })
})

describe('Visionneuse : la saisie guidee en accordeon, l\'en-tete en bas', () => {
  // jsdom ne mesure rien : chaque ligne d'etape est posee a 40 px de la precedente.
  function poserLesEtapes() {
    const original = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetTop')!
    Object.defineProperty(HTMLElement.prototype, 'offsetTop', {
      configurable: true,
      get(this: HTMLElement) { return this.dataset.etape ? Number(this.dataset.etape) * 40 : 0 },
    })
    return () => Object.defineProperty(HTMLElement.prototype, 'offsetTop', original)
  }

  it('la zone des etapes dit au C# son etape des qu\'elle apparait (celle du haut, ou celle demandee), puis joue le defilement demande', () => {
    const plan = vi.fn<GestePlan>()
    const joue = { current: 0 }
    const scrollTo = vi.fn()
    const original = HTMLElement.prototype.scrollTo
    HTMLElement.prototype.scrollTo = scrollTo as unknown as typeof original
    const retirer = poserLesEtapes()
    try {
      // Sans defilement demande : l'etape du haut.
      const sans = render(<Visionneuse dossier={dossier()} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
      expect(plan.mock.calls).toEqual([['etapeVisible', 0]])
      sans.unmount()
      plan.mockClear()
      // Une piece ouverte sur l'etape 3 : c'est elle que le pied annonce, meme
      // si la zone ne peut pas la monter jusqu'en haut (src/plan/pagination.ts).
      const { rerender } = render(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 4, index: 2, doux: false }}
                                               defilementJoue={joue} plan={plan} />)
      expect(plan.mock.calls).toEqual([['etapeVisible', 2]])
      expect(scrollTo).toHaveBeenCalledWith({ top: 80, behavior: 'auto' })
      expect(joue.current).toBe(4)
      // Le meme numero n'est pas rejoue ; un numero neuf l'est, en douceur.
      rerender(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 4, index: 2, doux: false }} defilementJoue={joue} plan={plan} />)
      rerender(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 5, index: 1, doux: true }} defilementJoue={joue} plan={plan} />)
      expect(scrollTo).toHaveBeenCalledTimes(2)
      expect(scrollTo).toHaveBeenLastCalledWith({ top: 40, behavior: 'smooth' })
    } finally {
      HTMLElement.prototype.scrollTo = original
      retirer()
    }
  })

  it('plus d\'en-tete en haut : les etapes d\'abord, le pied ensuite (titre, bilan, champs du controle et Valider, croix), sans oeil quand le document est a droite', () => {
    const { container, gestes } = rendre()
    const donnees = container.querySelector('.rig-vged__donnees')!
    expect(donnees.className).toBe('rig-vged__guide rig-vged__donnees rig-accordeon')
    expect(donnees.querySelector('.rig-guide__entete')).toBeNull()
    expect(donnees.firstElementChild!.className).toBe('rig-accordeon__etapes')
    const pied = donnees.lastElementChild!
    expect(pied.className).toBe('rig-guide__controle rig-guide__controle--bloque')
    // Ordre du balisage = ordre de tabulation : titres, bilan, validation, outils.
    // Valider est un voisin direct : la grille du CSS le pose sur la ligne du
    // bilan en colonne etroite, au bout des champs en colonne large.
    expect([...pied.children].map((e) => e.className)).toEqual(
      ['rig-guide__titres', 'rig-bilan', 'rig-guide__validation', 'rig-bouton rig-bouton--fort rig-guide__valider', 'rig-guide__outils'])
    expect(morceaux(pied)).toEqual(['<div>', ' ', '<div>', ' ', '<div>', ' ', '<button>', ' ', '<div>'])
    // Le controle n'est plus une etape de la liste : 6 etapes.
    expect(morceaux(pied.querySelector('.rig-kicker')!)).toEqual(['Controle du dossier · etape ', '2', ' / ', '6'])
    expect(pied.querySelector('.rig-guide__titre')!.textContent).toBe('Statuts signes')
    expect(pied.querySelector('[aria-label="Afficher le panneau de droite"]')).toBeNull()
    fireEvent.click(pied.querySelector('.rig-guide__fermer')!)
    expect(gestes()).toEqual([['fermerEtapes', null]])
  })

  it('l\'oeil descend dans le pied, a cote de la croix', () => {
    const { container, gestes } = rendre(dossier({ etapes: etapes({ oeil: true }) }))
    const outils = container.querySelector('.rig-guide__controle .rig-guide__outils')!
    expect(morceaux(outils)).toEqual(['<button>', '<button>'])
    fireEvent.click(outils.querySelector('[aria-label="Afficher le panneau de droite"]')!)
    expect(gestes()).toEqual([['afficherDocument', null]])
  })

  it('une ligne par etape : pastille, nom, resume et compte en morceaux ; un clic la deplie ou la replie', () => {
    const { container, gestes } = rendre()
    const sections = container.querySelectorAll('.rig-accordeon__etape')
    expect(sections).toHaveLength(4)
    expect(sections[1]!.className).toBe('rig-accordeon__etape rig-accordeon__etape--refuse rig-accordeon__etape--ouverte rig-accordeon__etape--courante')
    expect(sections[1]!.id).toBe('rig-etape-1')
    expect(sections[1]!.getAttribute('data-etape')).toBe('1')
    expect(sections[3]!.className).toBe('rig-accordeon__etape rig-accordeon__etape--partiel')
    const identite = sections[1]!.querySelector('.rig-accordeon__ligne')!
    expect(morceaux(identite)).toEqual(['<span>', ' ', '<span>', ' ', '<span>', ' ', '<span>', '<svg>'])
    expect(identite.getAttribute('aria-expanded')).toBe('true')
    expect(identite.getAttribute('title')).toBe('Identite')
    expect(identite.querySelector('.rig-accordeon__pastille')!.textContent).toBe('!')
    expect(identite.querySelector('.rig-accordeon__nom')!.textContent).toBe('Identite')
    expect(morceaux(identite.querySelector('.rig-accordeon__defaut')!)).toEqual(['1', ' valeur(s) refusee(s)'])
    expect(morceaux(identite.querySelector('.rig-accordeon__compte')!)).toEqual(['11', ' / ', '14'])
    // Repliee : les fiches et les requis manquants restent dans le resume.
    const siege = sections[3]!.querySelector('.rig-accordeon__ligne')!
    expect(siege.getAttribute('aria-expanded')).toBe('false')
    expect(morceaux(siege.querySelector('.rig-accordeon__fiches')!)).toEqual(['2', ' ', 'etablissement', 's'])
    expect(morceaux(siege.querySelector('.rig-accordeon__requis')!)).toEqual(['2', ' requis manquant(s)'])
    // Depliee et sans defaut : le resume nomme sa table.
    expect(sections[0]!.querySelector('.rig-accordeon__source')!.textContent).toBe('table DEMANDE')
    expect(sections[2]!.querySelector('.rig-accordeon__source')).toBeNull()
    fireEvent.click(identite)
    fireEvent.click(siege)
    expect(gestes()).toEqual([['basculerEtape', 1], ['basculerEtape', 3]])
  })

  it('seules les etapes depliees portent leurs champs', () => {
    const { container } = rendre()
    const sections = container.querySelectorAll('.rig-accordeon__etape')
    expect(sections[1]!.querySelector('.rig-accordeon__corps')).not.toBeNull()
    expect(sections[2]!.querySelector('.rig-accordeon__corps')).toBeNull()
    expect(sections[3]!.querySelector('.rig-accordeon__corps')).toBeNull()
  })

  it('un champ : libelle et « requis », saisie, refus ou manque, colonne ; frappe et sortie remontent', () => {
    const { container, gestes } = rendre()
    const labels = container.querySelectorAll('.rig-accordeon__etape--courante .rig-saisie-champ')
    expect(labels[0]!.className).toBe('rig-saisie-champ rig-saisie-champ--moitie rig-saisie-champ--refuse')
    expect(labels[2]!.className).toBe('rig-saisie-champ rig-saisie-champ--pleine rig-saisie-champ--manque')
    expect(morceaux(labels[0]!)).toEqual(['<span>', '<input>', '<span>', '<span>'])
    expect(morceaux(labels[0]!.firstElementChild!)).toEqual(['SIREN', '<span>'])
    expect(labels[0]!.querySelector('.rig-saisie-champ__refus')!.textContent).toBe('cle de controle du SIREN incorrecte')
    expect(labels[2]!.querySelector('.rig-saisie-champ__manque')!.textContent).toBe('a renseigner')
    const saisie = labels[0]!.querySelector('input') as HTMLInputElement
    expect(saisie.value).toBe('402 118 447')
    fireEvent.input(saisie, { target: { value: '109 678 235' } })
    fireEvent.change(saisie, { target: { value: '109 678 235' } })
    const liste = labels[1]!.querySelector('select') as HTMLSelectElement
    expect([...liste.options].map((o) => o.value)).toEqual(['', 'Personne morale', 'Personne physique'])
    expect(liste.value).toBe('Personne morale')
    fireEvent.change(liste, { target: { value: 'Personne physique' } })
    expect(gestes()).toEqual([
      ['saisie', { etape: 'IDENTITE', colonne: 'DSSRC_SIREN', valeur: '109 678 235' }],
      ['ecrire', { etape: 'IDENTITE', colonne: 'DSSRC_SIREN', valeur: '109 678 235' }],
      ['ecrire', { etape: 'IDENTITE', colonne: 'DSSRC_TYPE_RCS', valeur: 'Personne physique' }],
    ])
  })

  it('une etape multiple : ses fiches en onglets, ajout et retrait', () => {
    const e = etapes()
    e.pages[3] = { ...e.pages[3]!, ouverte: true }
    const { container, gestes } = rendre(dossier({ etapes: e }))
    const fiches = container.querySelector('.rig-fiches')!
    expect(morceaux(fiches)).toEqual(['<button>', '<button>', '<button>', '<button>'])
    const [f1, f2, ajout, retrait] = fiches.querySelectorAll('button')
    expect(f2!.className).toBe('rig-fiches__onglet rig-fiches__onglet--actif')
    expect(morceaux(f1!)).toEqual(['Etablissement', ' ', '1'])
    fireEvent.click(f1!)
    fireEvent.click(ajout!)
    fireEvent.click(retrait!)
    expect(gestes()).toEqual([['choisirFiche', { etape: 'SIEGE', index: 0 }], ['ajouterFiche', 'SIEGE'], ['supprimerFiche', 'SIEGE']])
  })

  it('le controle dans le pied : ce qui bloque, puis les champs de l\'etape Controle a cote de Valider, eteint tant qu\'un point bloque', () => {
    const recapBloque: RecapVue = { valide: false, nombre: 2, titre: '2 points bloquent la validation', aManquer: 1, refusees: 1, groupes: [
      { indexEtape: 1, fiche: 0, etape: 'Identite', multi: false, unite: 'Fiche', nombre: 1, points: [
        { champ: 'SIREN', motif: 'cle incorrecte', colonne: 'DSSRC_SIREN', manquant: false }] },
      { indexEtape: 6, fiche: 0, etape: 'Controle', multi: false, unite: 'Fiche', nombre: 1, points: [
        { champ: 'Etat du dossier', motif: 'a renseigner', colonne: 'DSSRC_ETAT_DOSSIER', manquant: true }] }] }
    const { container, gestes, rerender, plan } = rendre(dossier({ etapes: etapes({ recap: recapBloque }) }))
    // Aucune ligne « Controle » dans la liste : ses champs sont dans le pied.
    expect([...container.querySelectorAll('.rig-accordeon__nom')].map((n) => n.textContent)).not.toContain('Controle')
    const bilan = container.querySelector('.rig-guide__controle .rig-bilan')!
    expect(morceaux(bilan)).toEqual(['<span>', ' ', '<button>'])
    expect(bilan.querySelector('.rig-recap__pastille')!.className).toBe('rig-recap__pastille rig-recap__pastille--bloque')
    expect(bilan.querySelector('.rig-bilan__titre')!.textContent).toBe('2 points bloquent la validation')
    // Deux morceaux insecables, separes par « · » : la ligne ne se coupe qu'entre eux.
    const detail = bilan.querySelector('.rig-bilan__detail')!
    expect(morceaux(detail)).toEqual(['<span>', ' · ', '<span>'])
    expect([...detail.children].map((s) => morceaux(s))).toEqual([['1', ' a renseigner'], ['1', ' valeur(s) refusee(s)']])

    // La rangee de validation : les champs de Controle ; l'etat du dossier dit
    // pourquoi il bloque. Valider, a part, reste eteint.
    const validation = container.querySelector('.rig-guide__controle .rig-guide__validation')!
    expect(morceaux(validation)).toEqual(['<div>'])
    expect([...validation.querySelectorAll('.rig-saisie-champ__libelle')].map((l) => l.firstChild!.textContent))
      .toEqual(['Texte de la mention', 'Etat du dossier'])
    expect(validation.querySelector('.rig-saisie-champ--refuse .rig-saisie-champ__refus')!.textContent)
      .toBe('a passer a « Conforme » pour valider le dossier')
    const valider = container.querySelector('.rig-guide__controle > .rig-guide__valider') as HTMLButtonElement
    expect(valider.textContent).toBe('Valider le dossier')
    expect(valider.className).toBe('rig-bouton rig-bouton--fort rig-guide__valider')
    expect(valider.disabled).toBe(true)
    // Les champs ecrivent dans l'etape CONTROLE, comme dans la liste.
    fireEvent.input(validation.querySelector('input')!, { target: { value: 'Radiation d\'office' } })
    fireEvent.change(validation.querySelector('select')!, { target: { value: 'Conforme' } })
    // Le texte du bilan mene au premier point DE LA LISTE (Identite), pas au controle deja a l'ecran.
    fireEvent.click(bilan.querySelector('.rig-bilan__texte--lien')!)

    // Seul un point du controle bloque : il est sous les yeux, le bilan n'est plus un lien.
    const seulControle: RecapVue = { ...recapBloque, nombre: 1, refusees: 0, groupes: [recapBloque.groupes[1]!] }
    rerender(<Visionneuse dossier={dossier({ etapes: etapes({ recap: seulControle }) })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-bilan__texte--lien')).toBeNull()

    // Plus rien ne bloque : Valider s'allume, le filet passe au vert (classe --pret).
    const pret: RecapVue = { valide: false, nombre: 0, titre: 'Rien ne bloque la validation', aManquer: 0, refusees: 0, groupes: [] }
    rerender(<Visionneuse dossier={dossier({ etapes: etapes({ recap: pret }) })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-guide__controle')!.className).toBe('rig-guide__controle rig-guide__controle--pret')
    expect((container.querySelector('.rig-guide__valider') as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(container.querySelector('.rig-guide__valider')!)

    // Dossier valide : « Rouvrir la saisie » prend la place de Valider, a cote des memes champs.
    const ok: RecapVue = { valide: true, nombre: 0, titre: '', aManquer: 0, refusees: 0, groupes: [] }
    rerender(<Visionneuse dossier={dossier({ etapes: etapes({ recap: ok }) })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-guide__controle')!.className).toBe('rig-guide__controle rig-guide__controle--valide')
    expect(container.querySelector('.rig-bilan')!.className).toBe('rig-bilan rig-bilan--valide')
    expect(container.querySelector('.rig-guide__valider')!.textContent).toBe('Rouvrir la saisie')
    expect(container.querySelector('.rig-guide__validation .rig-champs')).not.toBeNull()
    fireEvent.click(container.querySelector('.rig-guide__valider')!)
    expect(gestes()).toEqual([
      ['saisie', { etape: 'CONTROLE', colonne: 'OBSRC_TEXTE_MENTION', valeur: 'Radiation d\'office' }],
      ['ecrire', { etape: 'CONTROLE', colonne: 'DSSRC_ETAT_DOSSIER', valeur: 'Conforme' }],
      ['allerAuDefaut', { etape: 1, fiche: 0 }], ['valider', null], ['rouvrir', null]])
  })
})

describe('Visionneuse : le document et le mode tache', () => {
  it('l\'apercu : kicker en morceaux, six actions nommees, valeurs saisies sur la 1re feuille, folio', () => {
    const { container, gestes } = rendre()
    const a = container.querySelector('.rig-apercu')!
    expect(morceaux(a.querySelector('.rig-kicker')!)).toEqual(['PDF', ' · ', '2', ' page(s) · ', '12/03/2024'])
    const actions = [...a.querySelectorAll('.rig-apercu__actions button')]
    expect(actions.map((b) => b.getAttribute('aria-label'))).toEqual(['Imprimer', 'Editer', 'Detacher', 'Sauvegarder', 'Envoyer par mail', 'Fermer'])
    actions.forEach((b) => fireEvent.click(b))
    expect(gestes().map(([n]) => n)).toEqual(['imprimer', 'editer', 'detacher', 'sauvegarder', 'envoiMail', 'fermerApercu'])
    const feuilles = a.querySelectorAll('.rig-apercu__feuille')
    expect(morceaux(feuilles[0]!)).toEqual(['<dl>', '<div>', ' ', '<div>'])
    expect(morceaux(feuilles[0]!.querySelector('dl')!)).toEqual(['<dt>', '<dd>'])
    expect(morceaux(feuilles[1]!)).toEqual(['<div>', ' ', '<div>'])
    expect(morceaux(feuilles[1]!.querySelector('.rig-apercu__folio')!)).toEqual(['2', ' / ', '2'])
  })

  it('sans valeur saisie, la liste reste VIDE (rig-apercu__saisie:empty la masque)', () => {
    const { container } = rendre(dossier({ apercu: { detachees: 0, apercu: apercu({ feuilles: [{ numero: 1, total: 1, texte: 'x', valeurs: [] }] }) } }))
    expect(container.querySelector('.rig-apercu__saisie')!.childNodes).toHaveLength(0)
  })

  it('les documents sortis : leur nombre et « Tout rattacher » ; un message sous l\'entete', () => {
    const { container, gestes } = rendre(dossier({ apercu: { detachees: 2, apercu: apercu({ message: 'Envoye a FILE1' }) } }))
    expect(morceaux(container.querySelector('.rig-vged__detachees .rig-guide__source')!))
      .toEqual(['2', ' fenetre(s) ouverte(s) — chacune revient toujours a la meme place.'])
    expect(container.querySelector('.rig-apercu__message')!.textContent).toBe('Envoye a FILE1')
    fireEvent.click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Tout rattacher')!)
    expect(gestes()).toEqual([['rattacher', null]])
  })

  it('le bandeau de la tache : comptes en morceaux, Terminer eteint avec sa raison', () => {
    const { container, gestes } = rendre(dossier({ tache: tache() }))
    const bandeau = container.querySelector('.rig-tache__bandeau')!
    expect(morceaux(bandeau)).toEqual(['<span>', ' ', '<span>', ' ', '<button>', '<span>', ' ', '<button>', ' ', '<button>'])
    const etat = bandeau.querySelector('.rig-tache__etat')!
    expect(etat.className).toBe('rig-tache__etat rig-tache__etat--ok')
    expect(morceaux(etat)).toEqual(['1', ' / ', '1', ' champs renseignes\n', '<span>'])
    expect(morceaux(etat.querySelector('.rig-tache__reste')!)).toEqual(['+ ', '1', ' facultatif(s)'])
    const terminer = bandeau.querySelector('.rig-tache__terminer') as HTMLButtonElement
    expect(terminer.disabled).toBe(true)
    expect(terminer.getAttribute('title')).toBe('Reste a traiter : Transmission INPI (registre national)')
    fireEvent.click(bandeau.querySelector('.rig-tache__tout')!)
    expect(gestes()).toEqual([['basculerTout', null]])
  })

  it('les checklists en pied, APRES l\'arbre : la preuve coche, l\'effet sans preuve se coche a la main', () => {
    const { container, gestes } = rendre(dossier({ tache: tache() }))
    const colonne = container.querySelector('.rig-vged__arbre')!
    const blocs = [...colonne.children].map((e) => e.className)
    expect(blocs.indexOf('rig-arbre')).toBeLessThan(blocs.indexOf('rig-tache__effets rig-tache__effets--pied'))
    const [saisir, entraine] = colonne.querySelectorAll('.rig-tache__effets--pied')
    expect(morceaux(saisir!.querySelector('.rig-tache__effets-compte')!)).toEqual(['1', '\n / ', '1', ' renseignes\n'])
    const [bodacc, inpi] = entraine!.querySelectorAll('.rig-tache__effet')
    expect(bodacc!.className).toBe('rig-tache__effet  rig-tache__effet--prouve ')
    expect((bodacc!.querySelector('input') as HTMLInputElement).disabled).toBe(true)
    expect(morceaux(bodacc!)).toEqual(['<input>', ' ', '<button>', ' ', '<span>', '<span>'])
    fireEvent.click(inpi!.querySelector('input')!)
    fireEvent.click(inpi!.querySelector('.rig-tache__effet-libelle')!)
    expect(gestes()).toEqual([['basculerEffet', 1], ['viserEffet', 1]])
  })
})
