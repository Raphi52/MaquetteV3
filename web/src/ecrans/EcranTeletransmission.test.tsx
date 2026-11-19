import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranTeletransmission, type VueTeletransmission } from './EcranTeletransmission'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les flux sont calcules par Pont/EcranTeletransmission.cs (execute par
// tests/AlertesCheck). Ici : l'etat du canal suit les flux rendus, le mode part
// au pont, et un flux rattache ouvre son affaire.
const vue = (mode: unknown): VueTeletransmission => mode === 'EDI'
  ? { flux: [{ numero: 'A7', horodatage: '09/09/2026 09:12', modeLisible: 'Edi', nom: 'INFOGREFFE', references: 'R1', numDemande: '004151', numerise: false },
             { numero: 'A8', horodatage: '09/09/2026 09:30', modeLisible: 'Edi', nom: 'INPI', references: 'R2', numDemande: '', numerise: true }],
      dernier: 'dernier flux : 09/09/2026 09:30' }
  : { flux: [], dernier: 'Aucun flux' }

describe('EcranTeletransmission', () => {
  it('« Canal au vert » quand des flux EDI sont arrives, et le flux rattache ouvre son affaire', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const ouvrirDossier = vi.fn()
    render(<EcranTeletransmission ouvrirDossier={ouvrirDossier} />)

    await screen.findByText('Canal au vert')
    expect(morceaux(screen.getByText('2 flux'))).toEqual(['2', ' flux'])
    fireEvent.click(screen.getByText('A7'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004151')
    fireEvent.click(screen.getByText('A8'))
    expect(ouvrirDossier).toHaveBeenCalledTimes(1)
  })

  it('un autre mode redemande au pont ; sans flux, le canal le dit', async () => {
    const pont = installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    render(<EcranTeletransmission ouvrirDossier={vi.fn()} />)
    await screen.findByText('Canal au vert')
    fireEvent.change(screen.getByLabelText('Mode'), { target: { value: 'TOUS' } })
    await screen.findByText('Aucun flux recu')
    await waitFor(() => expect(pont.appels).toHaveBeenLastCalledWith('EcranTeletransmission.Flux', 'TOUS'))
    expect(screen.getByText('Aucun flux pour ce mode.')).toBeTruthy()
  })
})
