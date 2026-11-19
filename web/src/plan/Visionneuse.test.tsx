import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { styleDepuisTexte, Visionneuse } from './Visionneuse'
import { morceaux } from '../test/morceaux'
import { apercu, dossier, etapes, historique, ligneArbre, tache } from '../test/plan'
import type { DossierVue, GestePlan } from './types'

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

describe('Visionneuse : l\'historique des pieces sous les donnees', () => {
  it('les 7 plus recentes : date, point or ou bleu, libelle ; la piece ouverte marquee', () => {
    const { container } = rendre()
    const milieu = container.querySelector('.rig-vged__corps > .rig-vged__milieu')!
    expect([...milieu.children].map((e) => e.className)).toEqual(['rig-vged__guide rig-vged__donnees', 'rig-chrono'])
    expect(milieu.querySelector('.rig-chrono__titre')!.textContent).toBe('Historique des pieces · 9')
    const pieces = milieu.querySelectorAll('.rig-chrono__piece')
    expect(pieces).toHaveLength(7)
    expect(morceaux(pieces[0]!)).toEqual(['<span>', '<span>', '<span>'])
    expect(pieces[0]!.textContent).toBe('19/11/2026Kbis a jour')
    expect(pieces[0]!.className).toBe('rig-chrono__piece rig-chrono__piece--actif')
    expect(pieces[0]!.getAttribute('aria-current')).toBe('true')
    expect(pieces[0]!.getAttribute('title')).toBe('Dossier RCS · Edition greffe · 1 p.')
    expect(pieces[0]!.querySelector('.rig-chrono__point')!.className).toBe('rig-chrono__point rig-chrono__point--rcs')
    expect(pieces[2]!.querySelector('.rig-chrono__point')!.className).toBe('rig-chrono__point rig-chrono__point--jud')
    expect(pieces[1]!.className).toBe('rig-chrono__piece')
  })

  it('« tout voir » deroule les 9 pieces, puis revient aux 7 dernieres', () => {
    const { container } = rendre()
    const tout = container.querySelector('.rig-chrono__tout')!
    expect(tout.textContent).toBe('tout voir')
    expect(tout.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(tout)
    expect(container.querySelectorAll('.rig-chrono__piece')).toHaveLength(9)
    expect(tout.textContent).toBe('les 7 dernieres')
    fireEvent.click(tout)
    expect(container.querySelectorAll('.rig-chrono__piece')).toHaveLength(7)
  })

  it('un clic sur une piece remonte « historiqueOuvrir » avec son identifiant', () => {
    const { container, gestes } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-chrono__piece')[3]!)
    expect(gestes()).toEqual([['historiqueOuvrir', 'n13']])
  })

  it('7 pieces ou moins : pas de « tout voir » ; aucune piece datee : pas d\'historique', () => {
    const { container, rerender, plan } = rendre(dossier({ historique: dossier().historique.slice(0, 7) }))
    expect(container.querySelector('.rig-chrono__tout')).toBeNull()
    rerender(<Visionneuse dossier={dossier({ historique: [] })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-chrono')).toBeNull()
  })

  it('colonne des donnees fermee : l\'historique part avec elle', () => {
    const { container } = rendre(dossier({ etapes: null, fiche: null, poignee2: false }))
    expect(container.querySelector('.rig-vged__milieu')).toBeNull()
    expect(container.querySelector('.rig-chrono')).toBeNull()
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

  it('arbre, poignee, donnees (et historique), poignee, document : dans cet ordre', () => {
    const { container } = rendre()
    expect([...container.querySelector('.rig-vged__corps')!.children].map((e) => e.className)).toEqual([
      'rig-vged__arbre', 'rig-vged__poignee', 'rig-vged__milieu', 'rig-vged__poignee',
      'rig-vged__guide rig-vged__apercu',
    ])
  })

  it('les poignees remontent leur colonne et l\'abscisse ; le voile suit le pointeur puis lache', () => {
    const { container, gestes } = rendre(dossier({ voile: true }))
    const [p1, p2] = container.querySelectorAll('.rig-vged__poignee')
    fireEvent.pointerDown(p1!, { clientX: 400 })
    fireEvent.pointerDown(p2!, { clientX: 820 })
    const voile = container.querySelector('.rig-vged__voile')!
    fireEvent.pointerMove(voile, { clientX: 450, clientY: 10 })
    fireEvent.pointerUp(voile)
    expect(gestes()).toEqual([
      ['redimDebut', { colonne: 1, x: 400 }], ['redimDebut', { colonne: 2, x: 820 }],
      ['redim', { x: 450, y: 10 }], ['redimFin', null],
    ])
  })

  it('l\'entete de l\'arbre, le filtre et sa croix', () => {
    const { container, gestes, rerender, plan } = rendre()
    expect(morceaux(container.querySelector('.rig-arbre__entete')!)).toEqual(['<span>', ' ', '<button>'])
    const filtre = container.querySelector('.rig-arbre__filtre') as HTMLInputElement
    expect(filtre.getAttribute('placeholder')).toBe("Filtrer l'arbre...")
    expect(container.querySelector('.rig-arbre__filtre-effacer')).toBeNull()
    fireEvent.input(filtre, { target: { value: 'Kb' } })
    rerender(<Visionneuse dossier={dossier({ filtre: 'Kb' })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    fireEvent.click(container.querySelector('.rig-arbre__filtre-effacer')!)
    expect(gestes()).toEqual([['filtre', 'Kb'], ['effacerFiltre', null]])
  })

  it('l\'arbre est rendu sous .rig-arbre et ses gestes partent au plan de travail', () => {
    const { container, gestes } = rendre()
    const lignes = container.querySelectorAll('.rig-arbre .rig-arbre__ligne')
    expect(lignes.length).toBeGreaterThanOrEqual(2)
    fireEvent.click(lignes[1]!)
    expect(gestes()[0]).toEqual(['arbreActiver', 'n2'])
  })
})

describe('Visionneuse : la saisie guidee', () => {
  it('la zone des etapes dit au C# quelle etape est visible des qu\'elle apparait, puis joue le defilement demande', () => {
    const plan = vi.fn<GestePlan>()
    const joue = { current: 0 }
    const scrollTo = vi.fn()
    const original = HTMLElement.prototype.scrollTo
    HTMLElement.prototype.scrollTo = scrollTo as unknown as typeof original
    try {
      const { rerender } = render(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 4, index: 2, doux: false }}
                                               defilementJoue={joue} plan={plan} />)
      expect(plan).toHaveBeenCalledWith('etapeVisible', 0)
      expect(scrollTo).toHaveBeenCalledWith({ left: 0, behavior: 'auto' })
      expect(joue.current).toBe(4)
      // Le meme numero n'est pas rejoue ; un numero neuf l'est, en douceur.
      rerender(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 4, index: 2, doux: false }} defilementJoue={joue} plan={plan} />)
      rerender(<Visionneuse dossier={dossier()} pagination={0} defilement={{ numero: 5, index: 1, doux: true }} defilementJoue={joue} plan={plan} />)
      expect(scrollTo).toHaveBeenCalledTimes(2)
      expect(scrollTo).toHaveBeenLastCalledWith({ left: 0, behavior: 'smooth' })
    } finally {
      HTMLElement.prototype.scrollTo = original
    }
  })

  it('l\'entete des etapes en morceaux, sans oeil quand le document est a droite', () => {
    const { container, gestes } = rendre()
    const donnees = container.querySelector('.rig-vged__donnees')!
    expect(morceaux(donnees.querySelector('.rig-kicker')!)).toEqual(['Controle du dossier · etape ', '2', ' / ', '7'])
    expect(donnees.querySelector('.rig-guide__titre')!.textContent).toBe('Statuts signes')
    expect(donnees.querySelector('[aria-label="Afficher le panneau de droite"]')).toBeNull()
    fireEvent.click(donnees.querySelector('.rig-guide__fermer')!)
    expect(gestes()).toEqual([['fermerEtapes', null]])
  })

  it('la frise : fleches, etat de chaque etape, pastille, defauts et compte en morceaux', () => {
    const { container, gestes } = rendre()
    const pas = container.querySelectorAll('.rig-frise__pas')
    expect(pas[1]!.className).toBe('rig-frise__pas rig-frise__pas--refuse rig-frise__pas--courant')
    expect(pas[0]!.className).toBe('rig-frise__pas rig-frise__pas--complet ')
    const bouton = pas[1]!.querySelector('.rig-frise__bouton')!
    expect(morceaux(bouton)).toEqual(['<span>', ' ', '<span>', ' ', '<span>'])
    expect(morceaux(bouton.querySelector('.rig-frise__libelle')!)).toEqual(['Identite', '<span>'])
    expect(morceaux(bouton.querySelector('.rig-frise__defaut')!)).toEqual(['1', ' valeur(s) refusee(s)'])
    expect(morceaux(bouton.querySelector('.rig-frise__compte')!)).toEqual(['11', ' / ', '14'])
    const siege = pas[3]!.querySelector('.rig-frise__libelle')!
    expect(morceaux(siege.querySelector('.rig-frise__fiches')!)).toEqual(['2', ' ', 'etablissement', 's'])
    expect(morceaux(siege.querySelector('.rig-frise__requis')!)).toEqual(['2', ' requis manquant(s)'])
    const [precedente, suivante] = container.querySelectorAll('.rig-frise__fleche-bouton')
    expect((precedente as HTMLButtonElement).disabled).toBe(false)
    fireEvent.click(precedente!)
    fireEvent.click(suivante!)
    fireEvent.click(bouton)
    expect(gestes()).toEqual([['allerEtape', 0], ['allerEtape', 2], ['allerEtape', 1]])
  })

  it('les pages : la courante et ses voisines sont rendues, les autres attendent', () => {
    const { container } = rendre()
    const pages = container.querySelectorAll('.rig-saisie__page')
    expect(pages[1]!.className).toBe('rig-saisie__page rig-saisie__page--courante')
    expect(pages[1]!.id).toBe('rig-etape-1')
    expect(pages[1]!.getAttribute('data-etape')).toBe('1')
    expect(morceaux(pages[1]!.querySelector('.rig-kicker')!)).toEqual(['Etape ', '2', ' sur ', '7'])
    expect(pages[3]!.querySelector('.rig-saisie__attente')!.textContent).toBe('Siege et etablissements')
  })

  it('un champ : libelle et « requis », saisie, refus ou manque, colonne ; frappe et sortie remontent', () => {
    const { container, gestes } = rendre()
    const labels = container.querySelectorAll('.rig-saisie__page--courante .rig-saisie-champ')
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
    e.pages[3] = { ...e.pages[3]!, rendue: true }
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

  it('le controle final : ce qui bloque, le bouton Valider eteint, puis le dossier valide', () => {
    const recapBloque = { valide: false, nombre: 2, titre: '2 points bloquent la validation', aManquer: 1, refusees: 1, groupes: [
      { indexEtape: 1, fiche: 0, etape: 'Identite', multi: false, unite: 'Fiche', nombre: 2, points: [
        { champ: 'SIREN', motif: 'cle incorrecte', colonne: 'DSSRC_SIREN', manquant: false },
        { champ: 'Patronyme', motif: 'a renseigner', colonne: 'INSPP_PATRONYME', manquant: true },
      ] }] }
    const e = etapes()
    e.pages[1] = { ...e.pages[1]!, recap: recapBloque }
    const { container, gestes, rerender, plan } = rendre(dossier({ etapes: e }))
    const entete = container.querySelector('.rig-recap__entete')!
    expect(morceaux(entete)).toEqual(['<span>', ' ', '<div>', ' ', '<button>'])
    expect(entete.querySelector('.rig-recap__pastille')!.className).toBe('rig-recap__pastille rig-recap__pastille--bloque')
    expect(morceaux(entete.querySelector('.rig-recap__detail')!)).toEqual(['1', ' a renseigner · ', '1', ' valeur(s) refusee(s) '])
    const valider = [...entete.querySelectorAll('button')].find((b) => b.textContent === 'Valider le dossier') as HTMLButtonElement
    expect(valider.disabled).toBe(true)
    const aller = container.querySelector('.rig-recap__aller')!
    expect(morceaux(aller)).toEqual(['<span>', ' ', '<span>', ' ', '<span>'])
    expect(container.querySelectorAll('.rig-recap__point')[0]!.className).toBe('rig-recap__point rig-recap__point--refuse')
    fireEvent.click(aller)
    const ok = etapes()
    ok.pages[1] = { ...ok.pages[1]!, recap: { valide: true, nombre: 0, titre: '', aManquer: 0, refusees: 0, groupes: [] } }
    rerender(<Visionneuse dossier={dossier({ etapes: ok })} pagination={0} defilement={null} defilementJoue={{ current: 0 }} plan={plan} />)
    expect(container.querySelector('.rig-recap__entete')!.className).toBe('rig-recap__entete rig-recap__entete--valide')
    fireEvent.click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Rouvrir la saisie')!)
    expect(gestes()).toEqual([['allerAuDefaut', { etape: 1, fiche: 0 }], ['rouvrir', null]])
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
