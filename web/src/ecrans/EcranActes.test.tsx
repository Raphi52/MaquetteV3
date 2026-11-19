import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranActes, type LigneActe, type VueActes } from './EcranActes'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

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

describe('EcranActes', () => {
  it('la case « Lister actes non numerisables » redemande au pont', async () => {
    const dotnet = installerFauxDotNet({ 'EcranActes.Trouver': vue })
    const { container } = render(<EcranActes ouvrirDossier={vi.fn()} />)
    await screen.findByText('D1')
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: 'Lister actes non numerisables' }))
    await screen.findByText('D2')
    expect((dotnet.invokeMethodAsync.mock.lastCall![2] as { critNonNumerisables: boolean }).critNonNumerisables).toBe(true)
    const inerte = container.querySelectorAll('tbody tr')[1]!
    expect(inerte.className).toBe('rig-table__ligne--alerte rig-table__ligne--inerte')
    expect(inerte.getAttribute('title')).toBe('Acte non numerisable : Illisible')
  })

  it('un acte numerise ouvre son dossier, un acte non numerisable n\'ouvre rien', async () => {
    installerFauxDotNet({ 'EcranActes.Trouver': vue })
    const ouvrirDossier = vi.fn()
    render(<EcranActes ouvrirDossier={ouvrirDossier} />)
    fireEvent.click(await screen.findByText('D1'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004171')

    fireEvent.click(screen.getByRole('button', { name: 'Lister actes non numerisables' }))
    fireEvent.click(await screen.findByText('D2'))
    await waitFor(() => expect(ouvrirDossier).toHaveBeenCalledTimes(1))
  })
})
