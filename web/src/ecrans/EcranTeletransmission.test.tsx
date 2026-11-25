import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranTeletransmission, type VueTeletransmission } from './EcranTeletransmission'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
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

const synthese = (c: HTMLElement) => c.querySelector('.rig-lm-synthese')!.textContent

describe('EcranTeletransmission', () => {
  it('« Canal au vert » quand des flux EDI sont arrives, et le flux rattache ouvre son affaire', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranTeletransmission ouvrirDossier={ouvrirDossier} />)

    await screen.findByText('Canal au vert')
    expect(synthese(container)).toBe('2 flux · 1 a numeriser · 1 sans demande')
    fireEvent.click(screen.getByText('A7'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004151')
    fireEvent.click(screen.getByText('A8'))
    expect(ouvrirDossier).toHaveBeenCalledTimes(1)
  })

  it('un autre mode redemande au pont ; sans flux, le canal le dit', async () => {
    const pont = installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    render(<EcranTeletransmission ouvrirDossier={vi.fn()} />)
    await screen.findByText('Canal au vert')
    const tousAvant = pont.appels.mock.calls.filter((a) => a[1] === 'TOUS').length
    fireEvent.change(screen.getByLabelText('Mode'), { target: { value: 'TOUS' } })
    await screen.findByText('Aucun flux recu')
    await waitFor(() => expect(pont.appels).toHaveBeenLastCalledWith('EcranTeletransmission.Flux', 'TOUS'))
    // La liste du mode choisi est bien redemandee (en plus des comptes des vues).
    expect(pont.appels.mock.calls.filter((a) => a[1] === 'TOUS').length).toBeGreaterThan(tousAvant + 1)
    expect(screen.getByText('Aucun flux pour ce mode.')).toBeTruthy()
  })
})

describe('EcranTeletransmission en liste moderne', () => {
  it('une ligne par flux : emetteur, reference et numero ; mode en pastille ; numerise en point d\'etat', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const { container } = render(<EcranTeletransmission ouvrirDossier={vi.fn()} />)
    await screen.findByText('Canal au vert')
    const premiere = container.querySelector('tbody tr')!
    expect(premiere.querySelector('.rig-lm-identite__nom')!.textContent).toBe('INFOGREFFE')
    expect(premiere.querySelector('.rig-lm-identite__objet')!.textContent).toBe('R1 · A7')
    expect(premiere.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-violet')
    // Une pastille coupee se lit entiere au survol (capture orange du 25/11 sur le Courrier arrive, memes modes).
    // Faux si : la cellule du mode n'a pas d'infobulle, ou porte autre chose que le libelle affiche.
    expect(premiere.querySelector('.rig-lm-pastille')!.closest('td')!.getAttribute('title')).toBe('Edi')
    expect(premiere.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(premiere.querySelector('.rig-lm-etat')!.textContent).toBe('non')
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('2 flux affichesA numeriser 1Sans demande 1Clic sur un flux pour ouvrir son affaire.')
  })

  it('les vues sont les deux modes, avec leur compte ; en choisir une change le critere', async () => {
    const pont = installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    render(<EcranTeletransmission ouvrirDossier={vi.fn()} />)
    await screen.findByText('Canal au vert')
    const edi = await screen.findByRole('button', { name: 'Teletransmis (EDI) 2' })
    expect(edi.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(screen.getByRole('button', { name: 'Tous les modes 0' }))
    await screen.findByText('Aucun flux recu')
    expect((screen.getByLabelText('Mode') as HTMLSelectElement).value).toBe('TOUS')
    expect(pont.appels).toHaveBeenCalledWith('EcranTeletransmission.Flux', 'TOUS')
  })

  it('« Aller a un flux… » filtre la liste affichee ; Entree ouvre l\'affaire du seul flux restant', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranTeletransmission ouvrirDossier={ouvrirDossier} />)
    await screen.findByText('Canal au vert')
    const champ = screen.getByRole('textbox', { name: 'Aller a un flux…' })
    fireEvent.change(champ, { target: { value: 'infogreffe' } })
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
    expect(synthese(container)).toBe('1 flux · 1 a numeriser · 0 sans demande')
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('004151')
    // Sans accents, comme l'ecran modele : « infogréffe » trouve l'emetteur « INFOGREFFE ».
    fireEvent.change(champ, { target: { value: 'infogréffe' } })
    expect([...container.querySelectorAll('tbody .rig-lm-identite__nom')].map((n) => n.textContent)).toEqual(['INFOGREFFE'])
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucun des 2 flux ne contient « zzz ».')).toBeTruthy()
  })
})

// Ouvert depuis la page d'une affaire : seuls les flux rattaches a elle ; l'etat
// du canal reste celui de TOUS les flux (plan « Vue du dossier », lot 2).
describe('EcranTeletransmission ouvert depuis une affaire', () => {
  const nombre = (c: HTMLElement) => c.querySelectorAll('tbody tr:not(:has(.rig-table__vide))').length

  it('ne montre que ses flux ; la bascule rend toute la liste', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const { container } = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={contexteDossier({ numero: '004151' })} />)
    await screen.findByText('Canal au vert')
    expect(nombre(container)).toBe(1)
    expect(synthese(container)).toBe('1 flux · 1 a numeriser · 0 sans demande')
    // Le compte de la vue suit le meme filtre que la liste.
    expect((await screen.findByRole('button', { name: 'Teletransmis (EDI) 1' })).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByText('A7')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Dossier 004151 seulement' }))
    expect(nombre(container)).toBe(2)
    expect(synthese(container)).toBe('2 flux · 1 a numeriser · 1 sans demande')
  })

  it('une affaire sans flux le dit, canal toujours au vert ; sans affaire, ni bascule ni filtre', async () => {
    installerFauxPont({ 'EcranTeletransmission.Flux': vue })
    const avec = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={contexteDossier()} />)
    expect(await screen.findByText('Aucun flux pour le dossier 004176 dans ce mode.')).toBeTruthy()
    expect(screen.getByText('Canal au vert')).toBeTruthy()
    avec.unmount()

    const sans = render(<EcranTeletransmission ouvrirDossier={vi.fn()} dossier={null} />)
    await screen.findByText('Canal au vert')
    expect(nombre(sans.container)).toBe(2)
    expect(screen.queryByRole('button', { name: /seulement$/ })).toBeNull()
  })
})
