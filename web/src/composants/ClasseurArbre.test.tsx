import { cleanup, fireEvent, render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ClasseurArbre, frise, type LigneArbreVue, type ProprietesClasseurArbre } from './ClasseurArbre'

afterEach(cleanup)

// Une ligne de l'arbre ; `enfants` non vide = dossier.
function l(id: string, texte: string, o: Partial<LigneArbreVue> = {}): LigneArbreVue {
  const enfants = o.enfants ?? []
  return {
    id, niveau: 0, texte, icone: o.icone ?? (enfants.length || o.compte !== undefined ? 'dossier' : 'pdf'),
    actif: false, chemin: false, depliable: enfants.length > 0, ouvert: enfants.length > 0, renomme: false,
    compte: null, enfants, vide: false, groupe: 0, niveauGroupe: 0, date: '', pages: 0, derniere: '', ...o,
  }
}

// Le dossier 004176, comme le pont le rend : Dossier RCS (Kbis, fiche, Depots d'actes
// ouverte avec deux depots) et Judiciaire (Instances ouverte, Ordonnances fermee).
function affaire(o: { choisi?: string, filtre?: boolean } = {}) {
  const choisi = o.choisi ?? 'cert'
  const g2 = { groupe: 2, niveauGroupe: 2 }
  const g6 = { groupe: 6, niveauGroupe: 2 }
  const p = (id: string, texte: string, date: string, pages: number, g: object, niveau: number) =>
    l(id, texte, { ...g, niveau, date, pages, actif: id === choisi })
  const actes = l('da', "Depots d'actes", { ...g2, niveau: 2, compte: 3, derniere: '15/10/2024', chemin: choisi === 'cert', enfants: [
    l('info', 'Informations des depots', { ...g2, niveau: 3, icone: 'note' }),
    l('d1', 'Depot n 2024/0141', { ...g2, niveau: 3, compte: 1, enfants: [p('bail', 'Bail commercial', '12/03/2024', 9, g2, 4)] }),
    l('d2', 'Depot n A2024/038654', { ...g2, niveau: 3, compte: 2, chemin: choisi === 'cert', enfants: [
      p('pv', 'Extrait de proces-verbal du conseil de surveillance du 03/10/2024', '15/10/2024', 3, g2, 4),
      l('gen', 'Documents generes lors de la formalite', { ...g2, niveau: 4, compte: 1, chemin: choisi === 'cert',
        enfants: [p('cert', "Certificat de depot d'actes", '15/10/2024', 1, g2, 5)] }),
    ] }),
  ] })
  const rcs = l('rcs', 'Dossier RCS', { niveau: 1, compte: 21, chemin: choisi !== 'rapport', enfants: [
    p('kbis', 'Kbis a jour', '19/11/2026', 1, {}, 2),
    l('fiche', 'Informations du dossier', { niveau: 2, icone: 'note' }),
    actes,
    l('pj', 'Pieces jointes', { groupe: 1, niveauGroupe: 2, niveau: 2, compte: 4, derniere: '15/09/2026', ouvert: false }),
  ] })
  const instances = l('in', 'Instances', { ...g6, niveau: 2, compte: 7, derniere: '11/07/2023', chemin: choisi === 'rapport', enfants: [
    l('i1', 'Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC', { ...g6, niveau: 3, compte: 3, enfants: [
      p('assig', 'Assignation en paiement', '14/02/2022', 8, g6, 4),
      l('mes', 'Mesures d instruction', { ...g6, niveau: 4, compte: 1, enfants: [p('rapport', 'Rapport d expertise', '03/03/2023', 42, g6, 5)] }),
      l('dec', 'Decisions judiciaires', { ...g6, niveau: 4, compte: 1, enfants: [p('jug', '2022J00114-JUG-GONON-11/07/2023 17:50:58', '11/07/2023', 2, g6, 5)] }),
    ] }),
  ] })
  const jud = l('jud', 'Judiciaire', { niveau: 1, compte: 23, chemin: choisi === 'rapport', enfants: [
    instances,
    l('ord', 'Ordonnances', { groupe: 1, niveauGroupe: 2, niveau: 2, compte: 6, derniere: '02/05/2024', ouvert: false }),
  ] })
  const arbre = l('racine', '004176 Modification - SCI DU PARC', { compte: 44, enfants: [rcs, jud] })
  const chemin = choisi === 'rapport'
    ? ['004176', 'Judiciaire', 'Instances', 'Instance n 2022J00114 - AMIENS BILLARDS / SCI DU PARC', 'Mesures d instruction', 'Rapport d expertise']
    : ['004176', 'Dossier RCS', "Depots d'actes", 'Depot n A2024/038654', 'Documents generes lors de la formalite', "Certificat de depot d'actes"]
  return { arbre, chemin, filtre: o.filtre ? 'certif' : '' }
}

function rendre(donnees = affaire()) {
  const gestes: [string, unknown][] = []
  const p: ProprietesClasseurArbre = {
    ...donnees,
    activer: (id) => gestes.push(['activer', id]),
    rubrique: (id) => gestes.push(['rubrique', id]),
    renommer: (r) => gestes.push(['renommer', r]),
    menuContextuel: vi.fn((s) => gestes.push(['menu', s.id])),
  }
  const r = render(<ClasseurArbre {...p} />)
  return { ...r, gestes, p }
}

// Les icones (traces SVG en texte) ajoutent des blancs : le texte lu est nettoye.
const textes = (c: Element, sel: string) => [...c.querySelectorAll(sel)].map((e) => e.textContent!.trim())

describe('ClasseurArbre : la bascule et le rappel', () => {
  it('une bascule par domaine, avec son nombre de pieces ; le classeur de la piece choisie est montre', () => {
    const { container } = rendre()
    expect(textes(container, '.rig-classeur__domaine')).toEqual(['Dossier RCS 21', 'Judiciaire 23'])
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
    expect(textes(container, '.rig-classeur__carte .rig-classeur__tete .rig-arbre__texte')).toEqual(["Depots d'actes", 'Pieces jointes'])
    // La piece ouverte est de ce cote : pas de rappel.
    expect(container.querySelector('.rig-classeur__rappel')).toBeNull()
  })

  it('l\'autre classeur, et le rappel or qui ramene a la piece ouverte', () => {
    const { container } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-classeur__domaine')[1]!)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Judiciaire 23')
    expect(textes(container, '.rig-classeur__tete .rig-arbre__texte')).toEqual(['Instances', 'Ordonnances'])
    // Judiciaire n'a aucune piece hors rubrique : pas de carte « hors rubrique ».
    expect(container.querySelector('.rig-classeur__hors')).toBeNull()
    const rappel = container.querySelector('.rig-classeur__rappel')!
    expect(rappel.textContent).toBe("Certificat de depot d'actes est ouvertDossier RCS ›")
    fireEvent.click(rappel)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
    expect(container.querySelector('.rig-classeur__rappel')).toBeNull()
  })

  it('choisir une autre piece (historique, palette) montre SON classeur, meme apres une bascule', () => {
    const { container, rerender, p } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-classeur__domaine')[1]!)
    rerender(<ClasseurArbre {...p} {...affaire({ choisi: 'cert' })} chemin={['004176', 'Dossier RCS', 'Kbis a jour']} />)
    expect(container.querySelector('.rig-classeur__domaine--on')!.textContent).toBe('Dossier RCS 21')
  })

  it('pendant une recherche, la bascule compte les pieces trouvees', () => {
    const { container } = rendre(affaire({ filtre: true }))
    // Dossier RCS rend 4 pieces dans ce jeu (Kbis, Bail, PV, Certificat ; la fiche ne compte pas), Judiciaire 3.
    expect(textes(container, '.rig-classeur__domaine')).toEqual(['Dossier RCS 4', 'Judiciaire 3'])
  })
})

describe('ClasseurArbre : les cartes', () => {
  it('hors rubrique : une carte au nom du domaine ; la fiche ne compte pas comme piece', () => {
    const { container, gestes } = rendre()
    const hors = container.querySelector('.rig-classeur__hors')!
    expect(hors.querySelector('.rig-classeur__hors-titre')!.textContent).toBe('Dossier RCS 1 piece hors rubrique')
    expect(textes(hors, '.rig-arbre__ligne .rig-arbre__texte')).toEqual(['Kbis a jour', 'Informations du dossier'])
    expect(textes(hors, '.rig-classeur__date')).toEqual(['19/11/2026'])
    fireEvent.click(hors.querySelectorAll('.rig-arbre__ligne')[1]!)
    expect(gestes).toEqual([['activer', 'fiche']])
  })

  it('une carte fermee : filet de sa teinte, nombre de pieces et date de la plus recente ; un clic l\'ouvre', () => {
    const { container, gestes } = rendre()
    const pj = [...container.querySelectorAll('.rig-classeur__carte')].find((c) => c.getAttribute('aria-label') === 'Pieces jointes')!
    expect(pj.className).toBe('rig-classeur__carte')
    expect((pj as HTMLElement).style.getPropertyValue('--g')).toBe('var(--rig-groupe-1)')
    expect(pj.querySelector('.rig-classeur__meta')!.textContent).toBe('4 pieces · 15/09/2026')
    expect(pj.querySelector('.rig-classeur__corps')).toBeNull()
    fireEvent.click(pj.querySelector('.rig-classeur__tete')!)
    expect(gestes).toEqual([['rubrique', 'pj']])
  })

  it('les trois points ouvrent le menu sans ouvrir la carte', () => {
    const { container, gestes } = rendre()
    fireEvent.click(container.querySelector('.rig-classeur__carte .rig-classeur__tete .rig-arbre__actions')!)
    expect(gestes).toEqual([['menu', 'da']])
  })
})

describe('ClasseurArbre : la carte ouverte, par date et par dossier', () => {
  it('« Par date » d\'office : la frise par annee, de la plus ancienne a la plus recente, la fiche a la fin', () => {
    const { container } = rendre()
    const carte = container.querySelector('.rig-classeur__carte--ouverte')!
    expect(carte.querySelector('.rig-classeur__periode')!.textContent).toBe('du 12/03/2024 au 15/10/2024')
    expect(carte.querySelector('.rig-classeur__tri--on')!.textContent).toBe('Par date')
    const frise = carte.querySelector('.rig-classeur__frise')!
    expect([...frise.children].map((e) => e.className.includes('rig-classeur__an') ? e.textContent : e.querySelector('.rig-classeur__jour')!.textContent))
      .toEqual(['2024', '12/03', '15/10', '15/10', 'sans date', ''])
    const ev = [...frise.querySelectorAll('.rig-classeur__ev')]
    expect(ev.map((e) => e.querySelector('em')!.textContent)).toEqual([
      'Depot n 2024/0141 · 9 p.', 'Depot n A2024/038654 · 3 p.', 'Documents generes lors de la formalite · 1 p.', "Depots d'actes"])
    // La piece choisie y est marquee, comme dans l'arbre.
    expect(ev[2]!.className).toContain('rig-arbre__ligne--actif')
  })

  it('les DECISIONS (rangees sous « Decisions judiciaires ») passent en or', () => {
    const { container } = rendre(affaire({ choisi: 'rapport' }))
    const ev = [...container.querySelectorAll('.rig-classeur__carte--ouverte .rig-classeur__ev')]
    expect(ev.map((e) => e.querySelector('.rig-arbre__texte')!.textContent))
      .toEqual(['Assignation en paiement', 'Rapport d expertise', '2022J00114-JUG-GONON-11/07/2023 17:50:58'])
    expect(ev.map((e) => e.className.includes('rig-classeur__ev--decision'))).toEqual([false, false, true])
    expect(textes(container, '.rig-classeur__carte--ouverte .rig-classeur__an')).toEqual(['2022', '2023'])
  })

  it('« Par dossier » : chaque sous-dossier en intertitre, avec ce qu\'il range un cran plus loin', () => {
    const { container, gestes } = rendre()
    fireEvent.click([...container.querySelectorAll('.rig-classeur__tri button')].find((b) => b.textContent === 'Par dossier')!)
    const corps = container.querySelector('.rig-classeur__carte--ouverte .rig-classeur__corps')!
    expect(textes(corps, '.rig-classeur__it')).toEqual(['Depot n 2024/01411', 'Depot n A2024/0386542', 'Documents generes lors de la formalite1'].map((t) => t + '⋮'))
    expect([...corps.querySelectorAll('.rig-classeur__it')].map((e) => (e as HTMLElement).style.getPropertyValue('--p'))).toEqual(['0', '0', '1'])
    expect([...corps.querySelectorAll('.rig-classeur__it')].map((e) => e.className.includes('--chemin'))).toEqual([false, true, true])
    const cert = [...corps.querySelectorAll('.rig-arbre__ligne')].find((e) => e.textContent!.trim().startsWith('Certificat'))!
    expect((cert as HTMLElement).style.getPropertyValue('--p')).toBe('2')
    expect(cert.querySelector('.rig-classeur__pages')!.textContent).toBe('1 p.')
    fireEvent.click(cert)
    expect(gestes).toEqual([['activer', 'cert']])
  })

  it('un element en cours de renommage montre sa saisie, Echap l\'abandonne', () => {
    const d = affaire()
    d.arbre.enfants[0]!.enfants[0] = { ...d.arbre.enfants[0]!.enfants[0]!, renomme: true }
    const { container, gestes } = rendre(d)
    const saisie = container.querySelector('.rig-classeur__hors .rig-arbre__saisie') as HTMLInputElement
    expect(saisie.value).toBe('Kbis a jour')
    fireEvent.keyDown(saisie, { key: 'Escape' })
    expect(gestes).toEqual([['renommer', { id: 'kbis', texte: '' }]])
  })
})

describe('frise : les pieces d\'une rubrique pour « Par date »', () => {
  it('toutes, sous-dossiers compris, triees par date ; les non datees a part', () => {
    const rubrique = affaire().arbre.enfants[0]!.enfants[2]!
    const { datees, sansDate } = frise(rubrique)
    expect(datees.map((x) => x.ligne.id)).toEqual(['bail', 'pv', 'cert'])
    expect(sansDate.map((x) => [x.ligne.id, x.dossier])).toEqual([['info', "Depots d'actes"]])
  })
})
