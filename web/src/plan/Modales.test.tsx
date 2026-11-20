import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { EnvoiMail, Impression, MenuNoeud, Nouveau } from './Modales'
import { morceaux } from '../test/morceaux'
import type { GestePlan } from './types'

afterEach(cleanup)

function geste() {
  const plan = vi.fn<GestePlan>()
  return { plan, gestes: () => plan.mock.calls.map(([nom, valeur]) => [nom, valeur ?? null]) }
}

describe('Modales du plan de travail', () => {
  it('Imprimer : libelle en morceaux, trois listes, exemplaires, recto-verso ; le voile ferme, le cadre non', () => {
    const { plan, gestes } = geste()
    const { container } = render(<Impression plan={plan} vue={{
      libelle: 'Kbis a jour', imprimantes: ['FILE1', 'FILE2'], files: ['Aucune'], papiers: ['NORMAL'],
      imprimante: 'FILE1', file: 'Aucune', papier: 'NORMAL', exemplaires: 1, rectoVerso: false,
    }} />)
    const cadre = container.querySelector('.rig-impression')!
    expect(morceaux(cadre.querySelector('.rig-kicker')!)).toEqual(['Imprimer « ', 'Kbis a jour', ' »'])
    expect(morceaux(cadre)).toEqual(['<div>', ' ', '<label>', ' ', '<label>', ' ', '<label>', ' ', '<label>', ' ', '<button>', ' ', '<div>'])
    expect(morceaux(cadre.querySelector('label')!)).toEqual(['<span>', ' ', '<select>'])
    expect(cadre.querySelector('.rig-bascule')!.className).toBe('rig-bascule ')
    fireEvent.change(cadre.querySelector('select')!, { target: { value: 'FILE2' } })
    const nombre = cadre.querySelector('input[type="number"]') as HTMLInputElement
    expect(nombre.value).toBe('1')
    expect(nombre.getAttribute('min')).toBe('1')
    fireEvent.change(nombre, { target: { value: '3' } })
    fireEvent.click(cadre.querySelector('.rig-bascule')!)
    fireEvent.click(cadre)
    fireEvent.click(container.querySelector('.rig-voile')!)
    expect(gestes()).toEqual([['imprimante', 'FILE2'], ['exemplaires', '3'], ['rectoVerso', null], ['fermerImpression', null]])
  })

  it('Envoyer par mail : destinataire en direct, message type, Envoyer eteint sans destinataire', () => {
    const { plan, gestes } = geste()
    const { container } = render(<EnvoiMail plan={plan} vue={{
      libelle: 'Kbis a jour', destinataires: ['a@exemple.fr'], messages: ['Transmission de piece', 'Convocation'],
      message: 0, corps: 'Madame, Monsieur', destinataire: '', peutEnvoyer: false,
    }} />)
    const cadre = container.querySelector('.rig-impression')!
    expect(morceaux(cadre.querySelector('.rig-kicker')!)).toEqual(['Envoyer par mail « ', 'Kbis a jour', ' »'])
    const champ = cadre.querySelector('input[type="email"]') as HTMLInputElement
    expect(champ.getAttribute('list')).toBe('rig-mail-destinataires')
    expect(morceaux(champ.parentElement!)).toEqual(['<span>', ' ', '<input>', ' ', '<datalist>'])
    const envoyer = [...cadre.querySelectorAll('button')].find((b) => b.textContent === 'Envoyer') as HTMLButtonElement
    expect(envoyer.disabled).toBe(true)
    fireEvent.input(champ, { target: { value: 'g' } })
    fireEvent.change(cadre.querySelector('select')!, { target: { value: '1' } })
    expect(gestes()).toEqual([['destinataire', 'g'], ['message', '1']])
  })

  it('Nouvel element : la cible, une tuile par nature avec son icone, Annuler', () => {
    const { plan, gestes } = geste()
    const { container } = render(<Nouveau plan={plan} vue={{
      cible: 'Dossier RCS', natures: [{ code: 'dossier', titre: 'Nouveau dossier', extension: '' }, { code: 'pdf', titre: 'Document PDF', extension: 'PDF' }],
    }} />)
    expect(morceaux(container.querySelector('.rig-kicker')!)).toEqual(['Nouvel element dans « ', 'Dossier RCS', ' »'])
    const [dossierN, pdf] = container.querySelectorAll('.rig-tuile-nature')
    expect(morceaux(dossierN!)).toEqual(['<svg>', ' ', '<span>'])
    expect(morceaux(pdf!)).toEqual(['<svg>', ' ', '<span>', '<span>'])
    expect(pdf!.querySelector('svg')!.getAttribute('class')).toBe('rig-nature__icone')
    fireEvent.click(pdf!)
    fireEvent.click([...container.querySelectorAll('button')].find((b) => b.textContent === 'Annuler')!)
    expect(gestes()).toEqual([['ajouterElement', 'pdf'], ['fermerNouveau', null]])
  })

  // Les vues du menu, comme Pont/PlanTravail.cs (VueMenuNoeud) les rend.
  const dossier = { titre: 'Depot n A2024/038654', x: 300, y: 400.5, estPiece: false, pieces: 2, copie: 'A2024/038654', copieNumero: true }
  const piece = { titre: 'Kbis a jour', x: 1, y: 1, estPiece: true, pieces: 0, copie: 'Kbis a jour', copieNumero: false }
  const items = (c: Element) => [...c.querySelectorAll('.rig-ctx button')].map((b) => b.textContent)

  it('Menu du clic droit : pose sous le pointeur ; un dossier s\'imprime et s\'envoie ENTIER, ses pieces annoncees', () => {
    const { plan, gestes } = geste()
    const { container } = render(<MenuNoeud plan={plan} vue={dossier} />)
    const menu = container.querySelector('.rig-ctx') as HTMLElement
    expect(menu.style.left).toBe('300px')
    expect(menu.style.top).toBe('400.5px')
    expect(morceaux(menu)).toEqual(['<div>', ' ', '<button>', '<button>', '<button>', '<div>', ' ', '<button>', '<button>', ' ', '<button>', ' ', '<button>', '<button>', ' ', '<div>', ' ', '<button>'])
    expect(items(menu)).toEqual(['Imprimer (2 pieces)', 'Envoyer par mail (2 pieces)', 'Copier le n° A2024/038654',
      'Renommer', 'Nouvel element ici…', 'Tout deplier', 'Tout replier', 'Dupliquer', 'Supprimer'])
    fireEvent.click(menu.querySelectorAll('button')[0]!)
    fireEvent.click(menu.querySelectorAll('button')[1]!)
    expect(gestes()).toEqual([['menuImprimer', null], ['menuEnvoyer', null]])
  })

  it('une piece s\'ouvre dans une fenetre, s\'imprime, s\'envoie, se copie par son libelle ; pas d\'operation de dossier', () => {
    const { plan, gestes } = geste()
    const { container } = render(<MenuNoeud plan={plan} vue={piece} />)
    expect(items(container)).toEqual(['Ouvrir dans une fenetre', 'Imprimer', 'Envoyer par mail', 'Copier le libelle', 'Renommer', 'Dupliquer', 'Supprimer'])
    fireEvent.click(container.querySelectorAll('.rig-ctx button')[0]!)
    fireEvent.contextMenu(container.querySelector('.rig-voile')!)
    fireEvent.click(container.querySelector('.rig-ctx__item--danger')!)
    expect(gestes()).toEqual([['menuFenetre', null], ['fermerMenu', null], ['menuSupprimer', null]])
  })

  it('ouvert pres du bord (carte du bas), le menu remonte et recule pour rester entier dans la fenetre', () => {
    // jsdom ne mesure rien : le menu fait ici 180 x 300 px, la fenetre 1024 x 768.
    const mesure = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
      .mockReturnValue({ width: 180, height: 300, x: 0, y: 0, top: 0, left: 0, right: 180, bottom: 300, toJSON: () => ({}) })
    const { plan } = geste()
    const { container } = render(<MenuNoeud plan={plan} vue={{ ...dossier, x: 1000, y: 700 }} />)
    const menu = container.querySelector('.rig-ctx') as HTMLElement
    expect([menu.style.left, menu.style.top]).toEqual([`${window.innerWidth - 180 - 8}px`, `${window.innerHeight - 300 - 8}px`])
    mesure.mockRestore()
  })

  it('une fiche ou un dossier vide : rien a imprimer ni a envoyer ; un dossier d\'une piece le dit au singulier', () => {
    const { plan } = geste()
    const { container, rerender } = render(<MenuNoeud plan={plan} vue={{ ...dossier, titre: 'Informations des depots', pieces: 0, copie: 'Informations des depots', copieNumero: false }} />)
    expect(items(container)).toEqual(['Copier le libelle', 'Renommer', 'Nouvel element ici…', 'Tout deplier', 'Tout replier', 'Dupliquer', 'Supprimer'])
    rerender(<MenuNoeud plan={plan} vue={{ ...dossier, titre: 'Depot n 2024/0141', pieces: 1, copie: '2024/0141' }} />)
    expect(items(container).slice(0, 3)).toEqual(['Imprimer (1 piece)', 'Envoyer par mail (1 piece)', 'Copier le n° 2024/0141'])
  })

  it('Copier : le n° du dossier part au presse-papiers et le menu se ferme ; un presse-papiers refuse se dit', async () => {
    const ecrire = vi.fn<(t: string) => Promise<void>>().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('refuse'))
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: ecrire }, configurable: true })
    const { plan, gestes } = geste()
    const { container } = render(<MenuNoeud plan={plan} vue={dossier} />)
    const copier = () => [...container.querySelectorAll('.rig-ctx button')].find((b) => b.textContent!.startsWith('Cop'))!
    fireEvent.click(copier())
    await waitFor(() => expect(gestes()).toEqual([['fermerMenu', null]]))
    expect(ecrire).toHaveBeenCalledWith('A2024/038654')
    fireEvent.click(copier())
    await waitFor(() => expect(copier().textContent).toBe('Copie impossible'))
    expect(gestes()).toEqual([['fermerMenu', null]])
  })
})
