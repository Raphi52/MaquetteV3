import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { EcranAudiences } from './EcranAudiences'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Valeurs attendues : celles de la capture de l'ecran Blazor (18/11/2026) :
// date proposee 16/09/2026 (JeuDemo.Aujourdhui + 7), 3 audiences programmees.
function ouvrir() {
  const pont = installerFauxPont({ 'EcranAudiences.DateProposee': () => '2026-09-16' })
  return { pont, ...render(<EcranAudiences />) }
}

const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')
/** La phrase de synthese : le compte, les publiques, les affaires. */
const synthese = (c: HTMLElement) => espaces(c.querySelector('.rig-lm-synthese')!.textContent)
const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]
const lignes = (c: HTMLElement) => rangees(c).map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
/** Section et « Publiee sur le web » d'une rangee : dans sa ligne depliee. */
function deplier(rangee: HTMLTableRowElement): string[][] {
  fireEvent.click(within(rangee).getByRole('button', { name: 'Deplier la ligne' }))
  return [...rangee.nextElementSibling!.querySelectorAll('.rig-lm-champ')].map((c) => [
    c.querySelector('.rig-lm-champ__libelle')!.textContent!, c.querySelector('.rig-lm-champ__valeur')!.textContent!])
}

describe('EcranAudiences', () => {
  it('affiche les audiences programmees et la date proposee par le pont', async () => {
    const { container, pont } = ouvrir()
    expect(await screen.findByDisplayValue('2026-09-16')).toBeTruthy()
    expect(pont.appels).toHaveBeenCalledWith('EcranAudiences.DateProposee')
    expect(container.querySelector('.rig-lm-titre')!.textContent).toBe('Audiences programmees')
    expect(synthese(container)).toBe('3 audiences · 2 publiques · 29 affaires')
    expect(lignes(container)[0]).toEqual(['', '15/09/2026', '09:00', 'Audience de procedure collective', '1re', 'oui', '14'])
    expect(lignes(container)[1]).toEqual(['', '22/09/2026', '10:15', 'Audience du juge-commissaire', '1re', 'non', '6'])
    const premiere = rangees(container)[0]!
    expect(premiere.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-orange')
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-vert')
    expect(rangees(container)[1]!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-gris')
    expect(deplier(premiere)).toEqual([['Section', 'A'], ['Publiee sur le web', 'oui']])
    expect(premiere.nextElementSibling!.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect(deplier(rangees(container)[1]!)).toEqual([['Section', 'A'], ['Publiee sur le web', '—']])
    // Une ligne depliee a la fois.
    expect(container.querySelectorAll('.rig-lm-detail')).toHaveLength(1)
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['3 audiences affichees', 'Publiques 2', 'Affaires 29'])
    // Le @bind d'un TimeOnly ecrivait « HH:mm:ss ».
    expect((container.querySelector('input[type="time"]') as HTMLInputElement).value).toBe('09:00:00')
    expect(screen.getByText('Envoi de mail actif').className).toBe('rig-etat rig-etat--ok')
  })

  it('les bascules s\'allument et s\'eteignent : publique allumee, classement admis par defaut', () => {
    ouvrir()
    const publique = screen.getByRole('button', { name: 'Audience publique' })
    const web = screen.getByRole('button', { name: 'Publiee sur le web' })
    expect(publique.className).toBe('rig-bascule rig-bascule--actif')
    expect(web.className).toBe('rig-bascule')
    expect(screen.getByRole('button', { name: 'Insuffisance d\'actif' }).className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.click(web)
    fireEvent.click(screen.getByRole('button', { name: 'Report' }))
    expect(web.className).toBe('rig-bascule rig-bascule--actif')
    expect(screen.getByRole('button', { name: 'Report' }).className).toBe('rig-bascule rig-bascule--actif')
  })

  it('« Creer l\'audience » ajoute la saisie, rangee par date, sans affaire', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-16')
    fireEvent.change(container.querySelector('input[type="time"]')!, { target: { value: '14:30' } })
    fireEvent.change(screen.getByLabelText('Chambre'), { target: { value: '3e' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))

    expect(synthese(container)).toBe('4 audiences · 3 publiques · 29 affaires')
    expect(lignes(container)[1]).toEqual(['', '16/09/2026', '14:30', 'Audience de procedure collective', '3e', 'oui', '0'])
    expect(deplier(rangees(container)[1]!)).toEqual([['Section', 'A'], ['Publiee sur le web', '—']])
  })

  it('sans date d\'audience, rien n\'est cree', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-16')
    fireEvent.change(container.querySelector('input[type="date"]')!, { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Creer l\'audience' }))
    expect(synthese(container)).toBe('3 audiences · 2 publiques · 29 affaires')
  })

  it('« Aller a une audience… » (Ctrl K) filtre la liste affichee', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-16')
    const champ = screen.getByLabelText('Aller a une audience…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    fireEvent.change(champ, { target: { value: 'plaidoirie' } })
    expect(lignes(container).map((l) => l[1])).toEqual(['29/09/2026'])
    expect(synthese(container)).toBe('1 audience · 1 publique · 9 affaires')
    // Sans casse ni accents, comme la Recherche d'affaires : « PROCÉDURE » trouve « Audience de procedure collective ».
    fireEvent.change(champ, { target: { value: 'PROCÉDURE' } })
    expect(lignes(container).map((l) => l[1])).toEqual(['15/09/2026'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 3 audiences ne contient « zzz ».')).toBeTruthy()
  })
})
