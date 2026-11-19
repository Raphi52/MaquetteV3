import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render } from '@testing-library/react'
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

  it('Menu du clic droit : pose sous le pointeur ; un dossier a trois operations de plus qu\'une piece', () => {
    const { plan, gestes } = geste()
    const { container, rerender } = render(<MenuNoeud plan={plan} vue={{ titre: 'Dossier RCS', x: 300, y: 400.5, estPiece: false }} />)
    const menu = container.querySelector('.rig-ctx') as HTMLElement
    expect(menu.style.left).toBe('300px')
    expect(menu.style.top).toBe('400.5px')
    expect(morceaux(menu)).toEqual(['<div>', ' ', '<button>', '<button>', ' ', '<button>', ' ', '<button>', '<button>', ' ', '<div>', ' ', '<button>'])
    expect([...menu.querySelectorAll('button')].map((b) => b.textContent))
      .toEqual(['Renommer', 'Nouvel element ici…', 'Tout deplier', 'Tout replier', 'Dupliquer', 'Supprimer'])
    rerender(<MenuNoeud plan={plan} vue={{ titre: 'Kbis a jour', x: 1, y: 1, estPiece: true }} />)
    expect([...container.querySelectorAll('.rig-ctx button')].map((b) => b.textContent)).toEqual(['Renommer', 'Dupliquer', 'Supprimer'])
    fireEvent.contextMenu(container.querySelector('.rig-voile')!)
    fireEvent.click(container.querySelector('.rig-ctx__item--danger')!)
    expect(gestes()).toEqual([['fermerMenu', null], ['menuSupprimer', null]])
  })
})
