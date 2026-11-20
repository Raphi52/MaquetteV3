import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranTeletransmission, type VueTeletransmission } from './EcranTeletransmission'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'
import { contexteDossier } from '../test/dossier'

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

// Ouvert depuis la page d'une affaire : seuls les flux rattaches a elle ; l'etat
// du canal reste celui de TOUS les flux (plan « Vue du dossier », lot 2).
describe('EcranTeletransmission ouvert depuis une affaire', () => {
  const nombre = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent

  it('ne montre que ses flux ; la bascule rend toute la liste', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const { container } = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '004151' })} />)
    await screen.findByText('Canal au vert')
    expect(nombre(container)).toBe('1 flux')
    expect(screen.getByText('A7')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Dossier 004151 seulement' }))
    expect(nombre(container)).toBe('2 flux')
  })

  it('une affaire sans flux le dit, canal toujours au vert ; sans affaire, ni bascule ni filtre', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const avec = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={contexteDossier()} />)
    expect(await screen.findByText('Aucun flux pour le dossier 004176 dans ce mode.')).toBeTruthy()
    expect(screen.getByText('Canal au vert')).toBeTruthy()
    avec.unmount()

    const sans = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={null} />)
    await screen.findByText('Canal au vert')
    expect(nombre(sans.container)).toBe('2 flux')
    expect(screen.queryByRole('button', { name: /seulement$/ })).toBeNull()
  })
})
