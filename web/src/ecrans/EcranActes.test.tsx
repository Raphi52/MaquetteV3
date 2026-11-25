import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { EcranActes, teinteCategorie, type LigneActe, type VueActes } from './EcranActes'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le filtre (non numerisables masques tant que la case n'est pas cochee) vit
// dans Pont/EcranActes.cs, execute par tests/SaisieCheck. Ici : la case part au
// pont, et un acte non numerisable n'ouvre rien.
const ligne = (rang: number, numerisable: boolean, anomalie = ''): LigneActe => ({
  rang, dossier: `00417${rang}`, depot: `D${rang}`, denomination: 'SCI DU PARC', categorieLisible: 'Depots actes',
  abrege: 'PV', description: 'Proces-verbal', depose: '01/09/2026', acte: '30/08/2026', cloture: '',
  confidentiel: false, demat: true, anomalie, numerisable,
})

const vue = (c: unknown): VueActes => ({
  categories: [{ code: 'A', libelle: 'Depots actes' }],
  lignes: (c as { critNonNumerisables: boolean }).critNonNumerisables
    ? [ligne(1, true), ligne(2, false, 'Illisible')]
    : [ligne(1, true)],
})

const rangees = (c: HTMLElement) => [...c.querySelectorAll<HTMLTableRowElement>('tbody tr:not(.rig-lm-detail)')]
const espaces = (t: string | null | undefined) => (t ?? '').replace(/\s/g, ' ')

describe('EcranActes', () => {
  it('la case « Lister actes non numerisables » redemande au pont', async () => {
    const pont = installerFauxPont({ 'EcranActes.Trouver': vue })
    const { container } = render(<EcranActes ouvrirDossier={vi.fn()} />)
    await screen.findByText('D1')
    expect(rangees(container)).toHaveLength(1)
    expect(morceaux(container.querySelector('.rig-lm-synthese')))
      .toEqual(['1', ' acte · ', '<b>', ' sans image · ', '<b>', ' anomalie'])
    expect(espaces(container.querySelector('.rig-lm-synthese')!.textContent)).toBe('1 acte · 0 sans image · 0 anomalie')

    fireEvent.click(screen.getByRole('button', { name: 'Lister actes non numerisables' }))
    await screen.findByText('D2')
    expect((pont.appels.mock.lastCall![1] as { critNonNumerisables: boolean }).critNonNumerisables).toBe(true)
    const inerte = rangees(container)[1]!
    expect(inerte.className).toBe('rig-table__ligne--alerte rig-table__ligne--inerte')
    expect(inerte.getAttribute('title')).toBe('Acte non numerisable : Illisible')
    expect(inerte.querySelector('.rig-lm-etat.rig-lm-t-rouge')!.textContent).toBe('Illisible')
    expect(espaces(container.querySelector('.rig-lm-synthese')!.textContent)).toBe('2 actes · 0 sans image · 1 anomalie')
  })

  it('un acte numerise ouvre son dossier, un acte non numerisable n\'ouvre rien', async () => {
    installerFauxPont({ 'EcranActes.Trouver': vue })
    const ouvrirDossier = vi.fn()
    render(<EcranActes ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(await screen.findByText('D1'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004171')

    fireEvent.click(screen.getByRole('button', { name: 'Lister actes non numerisables' }))
    fireEvent.click(await screen.findByText('D2'))
    await waitFor(() => expect(ouvrirDossier).toHaveBeenCalledTimes(1))
  })

  it('une ligne par acte ; les colonnes secondaires dans la ligne depliee, « Ouvrir le dossier » eteint pour un acte inerte', async () => {
    installerFauxPont({ 'EcranActes.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranActes ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Lister actes non numerisables' }))
    await screen.findByText('D2')
    const [premiere, inerte] = rangees(container)
    expect(premiere!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('SCI DU PARC')
    expect(premiere!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('PV · 004171 · D1')
    const categorie = premiere!.querySelector('.rig-lm-pastille')!
    expect([categorie.textContent, categorie.className]).toEqual(['Depots actes', 'rig-lm-pastille rig-lm-t-bleu'])
    expect([...premiere!.querySelectorAll('td')].slice(3, 6).map((td) => td.textContent)).toEqual(['01/09/2026', 'oui', 'aucune'])

    fireEvent.click(within(premiere!).getByRole('button', { name: 'Deplier la ligne' }))
    expect(ouvrirDossier).not.toHaveBeenCalled()
    const detail = premiere!.nextElementSibling as HTMLElement
    expect(detail.querySelector('td')!.getAttribute('colspan')).toBe('7')
    expect([...detail.querySelectorAll('.rig-lm-champ')].map((c) => [
      c.querySelector('.rig-lm-champ__libelle')!.textContent, c.querySelector('.rig-lm-champ__valeur')!.textContent]))
      .toEqual([['N', '1'], ['N Dossier', '004171'], ['N Depot', 'D1'], ['Abrege', 'PV'], ['Description', 'Proces-verbal'],
                ['Date de l acte', '30/08/2026'], ['Date de cloture', '—'], ['Confid.', '—']])
    fireEvent.click(within(detail).getByRole('button', { name: 'Ouvrir le dossier' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('004171')

    fireEvent.click(within(inerte!).getByRole('button', { name: 'Deplier la ligne' }))
    expect((within(inerte!.nextElementSibling as HTMLElement).getByRole('button', { name: 'Ouvrir le dossier' }) as HTMLButtonElement).disabled)
      .toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Actions de l\'acte D2' }))
    expect((screen.getByRole('menuitem', { name: 'Ouvrir le dossier' }) as HTMLButtonElement).disabled).toBe(true)
    expect(ouvrirDossier).toHaveBeenCalledTimes(1)
  })

  it('les vues posent les bascules ; « Aller a un acte… » (Ctrl K) filtre sans redemander au pont', async () => {
    const pont = installerFauxPont({ 'EcranActes.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranActes ouvrirDossier={ouvrirDossier} />)
    await screen.findByText('D1')
    const groupe = screen.getByRole('group', { name: 'Vues' })
    // La vue de depart ne montre que les numerises (le pont masque les autres) : elle ne se dit pas « Tous ».
    expect([...groupe.querySelectorAll('.rig-lm-vue')].map((v) => [v.textContent, v.getAttribute('aria-pressed')]))
      .toEqual([['Numerises', 'true'], ['Avec anomalie', 'false'], ['Avec non numerisables', 'false']])
    fireEvent.click(within(groupe).getByRole('button', { name: 'Avec non numerisables' }))
    await screen.findByText('D2')
    expect(screen.getByRole('button', { name: 'Lister actes non numerisables' }).className).toBe('rig-bascule rig-bascule--actif')
    fireEvent.click(within(groupe).getByRole('button', { name: 'Avec anomalie' }))
    expect((pont.appels.mock.lastCall![1] as { critAnomalies: boolean; critNonNumerisables: boolean }))
      .toMatchObject({ critAnomalies: true, critNonNumerisables: false })
    fireEvent.click(within(groupe).getByRole('button', { name: 'Avec non numerisables' }))
    await screen.findByText('D2')

    const appels = pont.appels.mock.calls.length
    const champ = screen.getByLabelText('Aller a un acte…')
    const touche = new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true, cancelable: true })
    window.dispatchEvent(touche)
    expect(touche.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(champ)
    // Les actes trouves se comptent par leur objet, pas par les rangees : la ligne « Aucun des n actes ne
    // contient… » est aussi une rangee, un filtre qui ne trouve rien passerait sinon pour un filtre qui trouve un.
    const objets = () => [...container.querySelectorAll('.rig-lm-identite__objet')].map((o) => o.textContent)
    // Sans casse ni accents, comme la Recherche d'affaires : « PROCÈS » trouve « Proces-verbal ».
    fireEvent.change(champ, { target: { value: 'PROCÈS' } })
    expect(objets()).toEqual(['PV · 004171 · D1', 'PV · 004172 · D2'])
    fireEvent.change(champ, { target: { value: '004171' } })
    expect(objets()).toEqual(['PV · 004171 · D1'])
    expect([...container.querySelector('.rig-lm-pied')!.children].map((e) => espaces(e.textContent)))
      .toEqual(['1 acte affiche', 'Sans image 0', 'Anomalies 0'])
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('004171')
    expect(pont.appels.mock.calls).toHaveLength(appels)
  })

  it('chaque famille de categorie a sa teinte', () => {
    expect(teinteCategorie('Depots actes PP')).toBe('bleu')
    expect(teinteCategorie('Depots des comptes annuels')).toBe('vert')
    expect(teinteCategorie('Beneficiaire effectif')).toBe('violet')
    expect(teinteCategorie('Ordonnance de prorogation')).toBe('ardoise')
    expect(teinteCategorie('Autre')).toBe('gris')
  })
})
