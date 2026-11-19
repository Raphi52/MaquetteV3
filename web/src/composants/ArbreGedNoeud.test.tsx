import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
import { ArbreGedNoeud, type LigneArbreVue } from './ArbreGedNoeud'
import { morceaux } from '../test/morceaux'

afterEach(cleanup)

// Les regles (enfants visibles, filtre, ouverture, chemin, repli) vivent dans
// Pont/ArbreGedNoeud.cs, executees par tests/GedCheck et tests/RecetteArbreCheck.
// Ici : le balisage de chaque ligne, la recursion, et les gestes remontes.
function ligne(id: string, texte: string, plus: Partial<LigneArbreVue> = {}): LigneArbreVue {
  return {
    id, niveau: 0, texte, icone: 'dossier', actif: false, chemin: false, depliable: false, ouvert: false,
    renomme: false, compte: null, enfants: [], vide: false, groupe: 0, niveauGroupe: 0, ...plus,
  }
}

const piece = ligne('n3', 'Kbis a jour', { niveau: 2, icone: 'kbis', actif: true })
const rcs = ligne('n2', 'Dossier RCS', { niveau: 1, depliable: true, ouvert: true, compte: 21, chemin: true, enfants: [piece] })
const vide = ligne('n4', 'Pieces jointes', { niveau: 1, ouvert: true, vide: true })
const racine = ligne('n1', '004176 Modification', { depliable: true, ouvert: true, compte: 44, chemin: true, enfants: [rcs, vide] })

function rendre(l = racine) {
  const gestes = { activer: vi.fn(), renommer: vi.fn(), menuContextuel: vi.fn() }
  const r = render(<ArbreGedNoeud ligne={l} {...gestes} />)
  return { ...r, ...gestes }
}

describe('ArbreGedNoeud', () => {
  it('rend les lignes A PLAT, dans l\'ordre de l\'arbre, en s\'appelant pour chaque enfant ouvert', () => {
    const { container } = rendre()
    const lignes = [...container.children].map((e) => e.className.trim())
    expect(lignes).toEqual([
      'rig-arbre__ligne  rig-arbre__ligne--chemin',
      'rig-arbre__ligne  rig-arbre__ligne--chemin',
      'rig-arbre__ligne rig-arbre__ligne--actif',
      'rig-arbre__ligne',
      'rig-arbre__vide',
    ])
    expect([...container.querySelectorAll('.rig-arbre__texte')].map((e) => e.textContent))
      .toEqual(['004176 Modification', 'Dossier RCS', 'Kbis a jour', 'Pieces jointes'])
  })

  it('porte le niveau en variable CSS, le chevron, l\'icone et le compte en trois morceaux', () => {
    const { container } = rendre()
    const [l0, , l2, l3] = [...container.querySelectorAll('.rig-arbre__ligne')] as HTMLElement[]
    expect(l0!.getAttribute('style')).toBe('--rig-arbre-niveau: 0;')
    expect(l2!.getAttribute('style')).toBe('--rig-arbre-niveau: 2;')
    expect(l0!.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron rig-arbre__chevron--ouvert')
    expect(l0!.querySelector('.rig-arbre__chevron')!.textContent).toBe('▸')
    expect(l2!.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron rig-arbre__chevron--vide')
    expect(l2!.querySelector('svg')!.getAttribute('class')).toBe('rig-arbre__icone')
    expect(morceaux(l0!.querySelector('.rig-arbre__compte'))).toEqual(['(', '44', ')'])
    expect(l3!.querySelector('.rig-arbre__compte')).toBeNull()
    const libelle = l0!.querySelector('.rig-arbre__libelle')!
    expect(morceaux(libelle)).toEqual(['<span>', '<svg>', '<span>', '<span>'])
  })

  it('un dossier ouvert sans enfant montre « aucune piece » un niveau plus bas', () => {
    const { container } = rendre()
    const v = container.querySelector('.rig-arbre__vide')!
    expect(v.textContent).toBe('aucune piece')
    expect(v.getAttribute('style')).toBe('--rig-arbre-niveau: 2;')
  })

  it('une branche fermee ne rend pas ses enfants', () => {
    const { container } = rendre({ ...racine, ouvert: false })
    expect(container.querySelectorAll('.rig-arbre__ligne')).toHaveLength(1)
    expect(container.querySelector('.rig-arbre__chevron')!.className).toBe('rig-arbre__chevron ')
  })

  it('un clic sur une ligne remonte SON identifiant (le repli se decide dans le pont)', () => {
    const { container, activer } = rendre()
    fireEvent.click(container.querySelectorAll('.rig-arbre__ligne')[2]!)
    expect(activer).toHaveBeenCalledWith('n3')
  })

  it('les trois points ouvrent le menu a la souris, SANS activer la ligne', () => {
    const { container, activer, menuContextuel } = rendre()
    const bouton = container.querySelectorAll('.rig-arbre__actions')[1] as HTMLButtonElement
    expect(bouton.getAttribute('aria-label')).toBe('Actions sur Dossier RCS')
    expect(bouton.getAttribute('title')).toBe('Actions')
    expect(bouton.textContent).toBe('⋮')
    fireEvent.click(bouton, { clientX: 120, clientY: 340 })
    expect(menuContextuel).toHaveBeenCalledWith(expect.objectContaining({ id: 'n2', clientX: 120, clientY: 340 }))
    expect(activer).not.toHaveBeenCalled()
  })

  it('le renommage SUR PLACE : champ prerempli, valide au « change », Echap abandonne', () => {
    const { container, renommer, activer } = rendre({ ...piece, renomme: true })
    const champ = container.querySelector('input.rig-arbre__saisie') as HTMLInputElement
    expect(champ.value).toBe('Kbis a jour')
    expect(document.activeElement).toBe(champ)
    fireEvent.click(champ)
    expect(activer).not.toHaveBeenCalled()
    champ.value = 'Kbis 2026'
    fireEvent.change(champ)
    expect(renommer).toHaveBeenLastCalledWith({ id: 'n3', texte: 'Kbis 2026' })
    fireEvent.keyDown(champ, { key: 'Escape' })
    expect(renommer).toHaveBeenLastCalledWith({ id: 'n3', texte: '' })
    expect(container.querySelector('.rig-arbre__texte')).toBeNull()
  })

  it('le LISERE : la tete d\'une rubrique et tout ce qu\'elle range portent sa teinte et le niveau de la tete', () => {
    const fille = ligne('n6', 'Bail commercial', { niveau: 3, icone: 'pdf', groupe: 2, niveauGroupe: 2 })
    const rubrique = ligne('n5', "Depots d'actes", { niveau: 2, depliable: true, ouvert: true, groupe: 2, niveauGroupe: 2, enfants: [fille] })
    const { container } = rendre(ligne('n4', 'Dossier RCS', { niveau: 1, depliable: true, ouvert: true, enfants: [rubrique] }))
    const [domaine, tete, piece] = [...container.querySelectorAll('.rig-arbre__ligne')] as HTMLElement[]
    expect(domaine!.className).not.toContain('rig-arbre__ligne--groupe')
    expect(domaine!.style.getPropertyValue('--rig-arbre-teinte')).toBe('')
    expect(tete!.className).toContain('rig-arbre__ligne--groupe rig-arbre__ligne--tete')
    expect(piece!.className).toContain('rig-arbre__ligne--groupe')
    expect(piece!.className).not.toContain('rig-arbre__ligne--tete')
    for (const l of [tete!, piece!]) {
      expect(l.style.getPropertyValue('--rig-arbre-teinte')).toBe('var(--rig-groupe-2)')
      expect(l.style.getPropertyValue('--rig-arbre-niveau-groupe')).toBe('2')
    }
    expect(piece!.style.getPropertyValue('--rig-arbre-niveau')).toBe('3')
  })

  // Depots d'actes (tete, niveau 2) > Depot A2024 (3) > Documents generes (4) > Certificat (5),
  // et a cote : Depot 2024/0141 (3, ferme) puis Depot B2024 (3, ouvert, vide).
  function rubriqueEmboitee(choisi: string) {
    const g = { groupe: 2, niveauGroupe: 2 }
    const certificat = ligne('n9', 'Certificat de depot', { ...g, niveau: 5, icone: 'pdf', actif: choisi === 'n9' })
    const generes = ligne('n8', 'Documents generes', { ...g, niveau: 4, depliable: true, ouvert: true, enfants: [certificat] })
    const extrait = ligne('n10', 'Extrait de PV', { ...g, niveau: 4, icone: 'pdf', actif: choisi === 'n10' })
    const depotA = ligne('n7', 'Depot A2024', { ...g, niveau: 3, depliable: true, ouvert: true, enfants: [extrait, generes] })
    const ferme = ligne('n11', 'Depot 2024/0141', { ...g, niveau: 3, depliable: true })
    const videB = ligne('n12', 'Depot B2024', { ...g, niveau: 3, ouvert: true, vide: true })
    return ligne('n5', "Depots d'actes", { ...g, niveau: 2, depliable: true, ouvert: true, enfants: [depotA, ferme, videB] })
  }
  const sous = (l: Element) => (l as HTMLElement).style.getPropertyValue('--rig-arbre-sous')
  const sousActif = (l: Element) => (l as HTMLElement).style.getPropertyValue('--rig-arbre-sous-actif')

  it('les SOUS-LISERES : un par sous-dossier ouvert qui contient la ligne, plus le sien', () => {
    const { container } = rendre(rubriqueEmboitee(''))
    const [tete, depotA, extrait, generes, certificat, ferme, videB] = [...container.querySelectorAll('.rig-arbre__ligne')]
    // Sous-dossiers OUVERTS : leur propre filet. La tete a le lisere, un dossier ferme rien.
    expect(depotA!.className).toContain('rig-arbre__ligne--sous-tete')
    expect(generes!.className).toContain('rig-arbre__ligne--sous-tete')
    expect(videB!.className).toContain('rig-arbre__ligne--sous-tete')
    for (const l of [tete!, extrait!, certificat!, ferme!]) expect(l.className).not.toContain('--sous-tete')
    // Les dossiers qui CONTIENNENT la ligne, entre la tete et elle.
    expect([tete, depotA, extrait, generes, certificat, ferme].map((l) => sous(l!))).toEqual(['', '', '1', '1', '2', ''])
    // « aucune piece » sous un sous-dossier ouvert : son filet continue dessous.
    const vide = container.querySelector('.rig-arbre__vide')!
    expect(vide.className).toBe('rig-arbre__vide rig-arbre__vide--groupe')
    expect(sous(vide)).toBe('1')
    expect((vide as HTMLElement).style.getPropertyValue('--rig-arbre-teinte')).toBe('var(--rig-groupe-2)')
    // Rien n'est choisi : aucun sous-lisere en teinte pleine.
    expect([...container.querySelectorAll('.rig-arbre__ligne')].map(sousActif).every((v) => v === '')).toBe(true)
  })

  it('le sous-lisere du dossier qui contient la ligne CHOISIE passe en teinte pleine, sur lui et ses voisines', () => {
    // Le Certificat est choisi : c'est « Documents generes » (niveau 4, 2e sous-lisere) qui s'allume.
    const { container } = rendre(rubriqueEmboitee('n9'))
    const [tete, depotA, extrait, generes, certificat, ferme] = [...container.querySelectorAll('.rig-arbre__ligne')]
    expect(sousActif(generes!)).toBe('1')
    expect((generes as HTMLElement).style.getPropertyValue('--rig-arbre-sous-actif-haut')).toBe('4px')
    expect(sousActif(certificat!)).toBe('1')
    expect((certificat as HTMLElement).style.getPropertyValue('--rig-arbre-sous-actif-haut')).toBe('')
    // Hors de « Documents generes » : rien ne s'allume.
    for (const l of [tete!, depotA!, extrait!, ferme!]) expect(sousActif(l)).toBe('')

    // L'Extrait est choisi : c'est Depot A2024 (1er sous-lisere) qui s'allume, jusque dans
    // « Documents generes » qu'il contient aussi.
    cleanup()
    const autre = rendre(rubriqueEmboitee('n10')).container
    const lignes = [...autre.querySelectorAll('.rig-arbre__ligne')]
    expect(lignes.slice(1, 5).map(sousActif)).toEqual(['0', '0', '0', '0'])
    expect(sousActif(lignes[0]!)).toBe('')
    expect(sousActif(lignes[5]!)).toBe('')
  })

  // Les traits neutres d'une ligne : [nombre de dossiers neutres qui la contiennent, le sien ?], null sans trait.
  const neutres = (l: Element) => {
    const t = l.querySelector(':scope > .rig-arbre__niveaux') as HTMLElement | null
    return t && [t.style.getPropertyValue('--rig-arbre-neutres'), t.className.includes('rig-arbre__niveaux--propre')]
  }

  it('les TRAITS DE NIVEAU neutres : hors rubrique, chaque dossier ouvert a le sien ; la racine n\'en porte pas', () => {
    const { container } = rendre()
    const [racineL, rcsL, kbis, pj] = [...container.querySelectorAll('.rig-arbre__ligne')]
    // 004176 (niveau 0) : rien. Dossier RCS (1, ouvert) : son trait. Kbis (2) : celui de Dossier RCS.
    // Pieces jointes (1, ouvert, vide) : son trait, qui continue sous « aucune piece ».
    expect([racineL, rcsL, kbis, pj].map((l) => neutres(l!))).toEqual([null, ['0', true], ['1', false], ['0', true]])
    expect(neutres(container.querySelector('.rig-arbre__vide')!)).toEqual(['1', false])
    expect(container.querySelector('.rig-arbre__niveaux')!.getAttribute('aria-hidden')).toBe('true')
    // L'element est pose sous la ligne, hors de son libelle : le decoupage du texte ne bouge pas.
    expect(container.querySelector('.rig-arbre__vide')!.textContent).toBe('aucune piece')
  })

  it('dans une rubrique, les traits neutres s\'arretent a sa tete : ses propres filets prennent le relais', () => {
    // Depots d'actes (tete au niveau 2) dans Dossier RCS (1) : le trait de Dossier RCS longe
    // la rubrique ; les sous-dossiers n'en ajoutent aucun (ils ont leur sous-lisere).
    const { container } = rendre(ligne('n4', 'Dossier RCS', { niveau: 1, depliable: true, ouvert: true, enfants: [rubriqueEmboitee('')] }))
    const lignes = [...container.querySelectorAll('.rig-arbre__ligne')]
    expect(lignes.map(neutres)).toEqual([['0', true], ...Array(lignes.length - 1).fill(['1', false])])
    // Une rubrique posee directement sous la racine (niveau 1) : aucun trait neutre.
    cleanup()
    const g = { groupe: 3, niveauGroupe: 1 }
    const haut = rendre(ligne('n1', 'Procedures collectives', { ...g, niveau: 1, depliable: true, ouvert: true,
      enfants: [ligne('n2', 'Procedure 2024P00042', { ...g, niveau: 2, depliable: true, ouvert: true, enfants: [ligne('n3', 'Jugement', { ...g, niveau: 3 })] })] })).container
    expect([...haut.querySelectorAll('.rig-arbre__ligne')].map(neutres)).toEqual([null, null, null])
  })

  it('choisie directement sous la tete : le lisere de la rubrique suffit, aucun sous-lisere ne s\'allume', () => {
    const g = { groupe: 1, niveauGroupe: 2 }
    const kbis = ligne('n2', 'Extrait Kbis', { ...g, niveau: 3, actif: true })
    const { container } = rendre(ligne('n1', 'Pieces jointes', { ...g, niveau: 2, depliable: true, ouvert: true, enfants: [kbis] }))
    expect([...container.querySelectorAll('.rig-arbre__ligne')].map(sousActif)).toEqual(['', ''])
  })
})
