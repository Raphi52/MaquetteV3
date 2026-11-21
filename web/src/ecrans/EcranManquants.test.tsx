import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranManquants, type NumeroManquant, type VueEcranManquants } from './EcranManquants'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// La liste (repris de la V2, declares dans la V3, rendus exclus) est tenue par la vue num.NumeroManquant et la route
// du serveur, prouvees par tests/NumerotationCompteurCheck. Ici : l'ecran montre la liste et la filtre.
function manquant(partiel: Partial<NumeroManquant> = {}): NumeroManquant {
  return {
    serie: 'RC1', annee: 2024, jour: 0, chrono: 8948, numero: 'F24/008948', motif: 'manquant constaté par la V2, motif inconnu',
    logiciel: 'V2', declareLe: '2026-11-20T18:00:00', ...partiel,
  }
}

function vue(partiel: Partial<VueEcranManquants> = {}): VueEcranManquants {
  return {
    mode: 'serveur', message: null, greffe: '3801',
    manquants: [
      manquant(),
      manquant({ annee: 2025, chrono: 14518, numero: 'F25/014518' }),
      manquant({ serie: 'RC5', annee: 2026, chrono: 7, numero: 'F26/000007', motif: 'inscription abandonnée', logiciel: 'V3' }),
    ],
    ...partiel,
  }
}

describe('EcranManquants', () => {
  it('NUM-013 T04, CA02, CA03 : chaque numero manquant avec son motif et son origine', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    expect(await screen.findByText('3 numéro(s) — greffe 3801')).toBeTruthy()
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes[0]!.textContent).toContain('F24/008948')
    expect(lignes[0]!.textContent).toContain('manquant constaté par la V2, motif inconnu')
    expect(lignes[2]!.textContent).toContain('inscription abandonnée')
    expect(lignes[2]!.textContent).toContain('V3')
  })

  it('NUM-013 CA04 : filtre par serie et par annee (RC1, RC5 et RC6 partagent la forme « F »)', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue() })
    render(<EcranManquants />)
    await screen.findByText('3 numéro(s) — greffe 3801')
    fireEvent.change(screen.getByLabelText('Série'), { target: { value: 'RC1' } })
    expect(screen.getByText('2 numéro(s) — greffe 3801')).toBeTruthy()
    fireEvent.change(screen.getByLabelText('Année'), { target: { value: '2025' } })
    const lignes = screen.getAllByRole('row').slice(1)
    expect(lignes).toHaveLength(1)
    expect(lignes[0]!.textContent).toContain('F25/014518')
    fireEvent.change(screen.getByLabelText('Série'), { target: { value: 'RC5' } })
    expect(screen.getByText(/le registre est continu/)).toBeTruthy()
  })

  it('sans la fonction LNUMCM : le refus du serveur, sans liste', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue({ mode: 'refuse', manquants: [], message: 'Refuse : le compte x n\'a pas la fonction LNUMCM.' }) })
    render(<EcranManquants />)
    const alerte = await screen.findByRole('alert')
    expect(alerte.textContent).toContain('Accès refusé')
    expect(alerte.textContent).toContain('LNUMCM')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('sans serveur : la liste de demonstration', async () => {
    installerFauxPont({ 'EcranManquants.Ouvrir': () => vue({ mode: 'demo', greffe: null }) })
    render(<EcranManquants />)
    expect(await screen.findAllByText('Démonstration')).toHaveLength(2)
    expect(screen.getAllByRole('row').slice(1)).toHaveLength(3)
  })
})
