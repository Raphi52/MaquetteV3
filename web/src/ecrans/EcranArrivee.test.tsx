import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranArrivee, type LigneArrivee, type VueArrivee } from './EcranArrivee'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le filtre vit dans Pont/EcranArrivee.cs (execute par tests/SaisieCheck et
// SimuEdiCheck). Ici : une arrivee mene a SA demande, une arrivee sans demande
// ne mene nulle part et se distingue.
const ligne = (numero: string, numDemande: string, numerise: boolean): LigneArrivee => ({
  numero, horodatage: '09/09/2026 08:12', modeLisible: 'Poste', nom: 'SCI DU PARC', references: 'REF',
  case: '12', paiementJoint: true, numDemande, typeDemande: 'IMMAT', utilisateur: 'R. VILAIN', numerise, attestation: false,
})

const vue = (): VueArrivee => ({
  modes: [{ code: 'POSTE', libelle: 'Poste' }, { code: 'EDI', libelle: 'Edi' }],
  lignes: [ligne('A1', '004176', true), ligne('A2', '', false)], total: 9, aNumeriser: 1,
})

describe('EcranArrivee', () => {
  it('une arrivee rattachee ouvre sa demande, une arrivee sans demande reste inerte', async () => {
    installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranArrivee ouvrirDossier={ouvrirDossier} />)

    fireEvent.click(await screen.findByText('A1'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')

    const sans = container.querySelectorAll('tbody tr')[1]!
    expect(sans.className).toBe('rig-table__ligne--alerte rig-table__ligne--inerte')
    expect(sans.getAttribute('title')).toBe('Aucune demande rattachee a cette arrivee')
    expect(sans.querySelector('.rig-table__sans')!.textContent).toBe('non rattachee')
    fireEvent.click(sans)
    expect(ouvrirDossier).toHaveBeenCalledTimes(1)
  })

  it('les modes viennent du pont, et le mode choisi lui est renvoye', async () => {
    const pont = installerFauxPont({ 'EcranArrivee.Trouver': vue })
    render(<EcranArrivee ouvrirDossier={vi.fn()} />)
    await screen.findByText('A1')
    fireEvent.change(screen.getByLabelText(/Mode d/), { target: { value: 'EDI' } })
    await waitFor(() => expect((pont.appels.mock.lastCall![1] as { critMode: string }).critMode).toBe('EDI'))
  })
})
