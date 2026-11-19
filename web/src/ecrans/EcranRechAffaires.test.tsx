import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranRechAffaires, type LigneAffaire, type VueAffaires } from './EcranRechAffaires'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Le fonds d'affaires, le filtre et le tri sont faits par Pont/EcranRechAffaires.cs,
// executes par tests/TriCheck, OuvertureCheck et GedCheck. Ici : l'ecran affiche
// ce que le pont rend, lui transmet les criteres saisis, et remonte les gestes.
const ligne = (numero: string, nature = 'instance'): LigneAffaire => ({
  numero, idInstance: '55339', role: '2026F00301', codeAffaire: '010', nature, nomNature: 'Instance',
  libelle: `Affaire ${numero}`, nom: 'AMIENS BILLARDS', etat: 'En cours', siren: '109 678 235', type: 'RCS',
  saisine: '30/08/2026', creation: '01/09/2026', solde: '142,50',
})

function vue(c: unknown, _cle: unknown, _asc: unknown, selection: unknown): VueAffaires {
  const criteres = c as Record<string, unknown>
  const numero = String(criteres.critNumero ?? '')
  const lignes = numero === 'ZZZ' ? [] : [ligne('2026B01067'), ligne('004176')]
  return {
    natures: [{ code: 'instance', nom: 'Instance' }], etats: ['En cours', 'Radiee'],
    lignes, resume: lignes.length ? `${lignes.length} instance` : '',
    aucunCritere: Object.values(criteres).every((v) => v === '' || v === null),
    sirenCherche: String(criteres.critLibelle ?? '').replace(/\D/g, '').length === 9 ? String(criteres.critLibelle).replace(/\D/g, '') : '',
    choisie: selection ? { numero: String(selection), idInstance: '55339', role: '2026F00301' } : null,
  }
}

describe('EcranRechAffaires', () => {
  it('affiche les affaires rendues par le pont, colonnes et resume compris', async () => {
    const dotnet = installerFauxDotNet({ 'EcranRechAffaires.Trouver': vue })
    const { container } = render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)

    await screen.findByText('Resultats (2) — 2 instance')
    expect(dotnet.invokeMethodAsync.mock.calls[0]![1]).toBe('EcranRechAffaires.Trouver')
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees).toHaveLength(2)
    expect(rangees[0]!.querySelectorAll('td')).toHaveLength(13)
    expect(rangees[0]!.lastElementChild!.textContent).toBe('142,50 €')
    expect(container.querySelectorAll('th.rig-th--triable')).toHaveLength(13)
  })

  it('pre-remplit le numero d\'affaire recu de l\'accueil', async () => {
    const dotnet = installerFauxDotNet({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires noAffaire=" 004176 " ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/Resultats/)
    expect((dotnet.invokeMethodAsync.mock.calls[0]![2] as { critNumero: string }).critNumero).toBe('004176')
  })

  it('double-clic : remonte le numero de l\'affaire a ouvrir', async () => {
    installerFauxDotNet({ 'EcranRechAffaires.Trouver': vue })
    const ouvrir = vi.fn()
    render(<EcranRechAffaires ouvrir={ouvrir} ouvrirUnDossier={vi.fn()} />)
    fireEvent.doubleClick(await screen.findByText('004176'))
    expect(ouvrir).toHaveBeenCalledWith('004176')
  })

  it('un clic sur un en-tete demande le tri au pont, un second l\'inverse', async () => {
    const dotnet = installerFauxDotNet({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    await screen.findByText(/Resultats/)
    fireEvent.click(screen.getByText('Nom'))
    await waitFor(() => expect(dotnet.invokeMethodAsync.mock.lastCall!.slice(3, 5)).toEqual(['Nom', true]))
    fireEvent.click(screen.getByText('Nom'))
    await waitFor(() => expect(dotnet.invokeMethodAsync.mock.lastCall!.slice(3, 5)).toEqual(['Nom', false]))
  })

  it('recherche restee vide : « Ouvrir un dossier » emporte le SIREN cherche', async () => {
    installerFauxDotNet({
      'EcranRechAffaires.Trouver': (c: unknown, ...r: unknown[]) =>
        ({ ...vue(c, ...(r as [unknown, unknown, unknown])), lignes: [], resume: '' }),
    })
    const ouvrirUnDossier = vi.fn()
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={ouvrirUnDossier} />)
    expect(await screen.findByText('Saisissez au moins un critere, puis lancez la recherche.')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Libelle, SIREN'), { target: { value: '512 345 678' } })
    await screen.findByText('Aucune affaire ne repond a ces criteres.')
    const pourLeSiren = await screen.findByText('pour le SIREN 512345678')
    expect(morceaux(pourLeSiren)).toEqual([' pour le SIREN ', '512345678'])
    expect(morceaux(pourLeSiren.parentElement)).toEqual(['<button>', '<span>'])
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir un dossier' }))
    expect(ouvrirUnDossier).toHaveBeenCalledWith('512345678')
  })

  it('Entree dans les criteres et suppression simulee de l\'instance choisie', async () => {
    installerFauxDotNet({ 'EcranRechAffaires.Trouver': vue })
    render(<EcranRechAffaires ouvrir={vi.fn()} ouvrirUnDossier={vi.fn()} />)
    fireEvent.click(await screen.findByText('2026B01067'))
    expect(morceaux(await screen.findByText('2026B01067 · instance 55339 · role 2026F00301')))
      .toEqual(['2026B01067', ' · instance ', '55339', ' · role ', '2026F00301'])
    fireEvent.click(screen.getByRole('button', { name: 'Supprimer l\'instance' }))
    expect(screen.getByText(/suppression simulee — numeros metiers liberes\./)).toBeTruthy()
    fireEvent.keyDown(screen.getByLabelText('N° Affaire'), { key: 'Enter' })
    expect(screen.queryByText(/suppression simulee/)).toBeNull()
  })
})
