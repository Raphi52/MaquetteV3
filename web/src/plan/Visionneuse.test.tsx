import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { styleDepuisTexte, Visionneuse } from './Visionneuse'
import { morceaux } from '../test/morceaux'
import { apercu, dossier, etapes, tache } from '../test/plan'
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

describe('Visionneuse : entete, fiche sommaire, bandeau des defauts', () => {
  it('l\'entete : retour, filet du statut, numero, objet, SIREN et type en morceaux, croix', () => {
    const { container } = rendre()
    const entete = container.querySelector('.rig-vged > .rig-vged__entete')!
    expect(morceaux(entete)).toEqual(['<button>', ' ', '<span>', ' ', '<b>', ' ', '<span>', ' ', '<span>', ' ', '<span>', '<button>'])
    expect(entete.children[1]!.className).toBe('rig-filet-v rig-filet-v--alerte')
    expect(morceaux(entete.children[4]!)).toEqual([' · SIREN ', '812 445 901', ' · ', 'RCS'])
    expect(entete.querySelectorAll('button')[1]!.getAttribute('aria-label')).toBe("Fermer l'affaire")
  })

  it('« Document » n\'apparait que si la piece est ouverte et la colonne fermee', () => {
    const { container, gestes } = rendre(dossier({ boutonDocument: true }))
    const doc = [...container.querySelectorAll('button')].find((b) => b.textContent === 'Document')!
    fireEvent.click(doc)
    expect(gestes()).toEqual([['afficherDocument', null]])
  })

  it('la fiche sommaire : un seul flux de champs, le resume en morceaux', () => {
    const { container } = rendre()
    const resume = container.querySelector('.rig-sommaire__titre')!
    expect(morceaux(resume)).toEqual([' Fiche sommaire ', '<span>'])
    expect(morceaux(resume.querySelector('span')!)).toEqual(['004176', ' · ', 'SCI DU PARC'])
    const champs = container.querySelectorAll('.rig-sommaire__champ')
    expect(champs).toHaveLength(2)
    expect(morceaux(champs[0]!)).toEqual(['<span>', ' ', '<span>'])
    expect(champs[0]!.lastElementChild!.className).toBe('rig-sommaire__valeur rig-sommaire__valeur--mono')
    expect(champs[1]!.lastElementChild!.className).toBe('rig-sommaire__valeur ')
    expect(container.querySelector('.rig-sommaire__mention')).toBeNull()
  })

  it('le bandeau des defauts : compte, liens vers l\'etape a corriger', () => {
    const { container, gestes } = rendre()
    const bandeau = container.querySelector('.rig-bandeau-defauts')!
    expect(bandeau.className).toBe('rig-bandeau-defauts rig-bandeau-defauts--erreur')
    expect(morceaux(container.querySelector('.rig-bandeau-defauts__titre')!)).toEqual(['<b>', ' ', 'point a corriger'])
    const lien = container.querySelector('.rig-bandeau-defauts__lien')!
    expect(lien.className).toBe('rig-bandeau-defauts__lien rig-bandeau-defauts__lien--refuse')
    expect(morceaux(lien)).toEqual(['Identite', ' ', '<span>'])
    fireEvent.click(lien)
    expect(gestes()).toEqual([['allerAuDefautBandeau', { etape: 1, fiche: 0 }]])
  })

  it('sans defaut, pas de bandeau', () => {
    const { container } = rendre(dossier({ bandeau: null }))
    expect(container.querySelector('.rig-bandeau-defauts')).toBeNull()
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

  it('arbre, poignee, donnees, poignee, document : dans cet ordre', () => {
    const { container } = rendre()
    expect([...container.querySelector('.rig-vged__corps')!.children].map((e) => e.className)).toEqual([
      'rig-vged__arbre', 'rig-vged__poignee', 'rig-vged__guide rig-vged__donnees', 'rig-vged__poignee',
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
