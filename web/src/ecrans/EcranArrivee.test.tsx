import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranArrivee, oublierVuesDeLaSession, type LigneArrivee, type VueArrivee } from './EcranArrivee'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont(); oublierVuesDeLaSession() })

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

const rangees = (c: HTMLElement) => [...c.querySelectorAll('tbody tr:not(.rig-lm-detail)')]

describe('EcranArrivee', () => {
  it('une arrivee rattachee ouvre sa demande, une arrivee sans demande reste inerte', async () => {
    installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranArrivee ouvrirDossier={ouvrirDossier} />)

    fireEvent.click(await screen.findByText('A1'))
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')

    const sans = rangees(container)[1]!
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

describe('EcranArrivee en liste moderne', () => {
  it('une ligne par arrivee : expediteur, mode en pastille, numerisation en point ; la tete en fait la synthese', async () => {
    installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const { container } = render(<EcranArrivee ouvrirDossier={vi.fn()} />)
    await screen.findByText('A1')
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('2 affichee(s) sur 9 · 1 a numeriser · 2 avec paiement')
    const [rattachee, sans] = rangees(container)
    expect(rattachee!.querySelector('.rig-lm-identite__nom')!.textContent).toBe('SCI DU PARC')
    expect(rattachee!.querySelector('.rig-lm-identite__objet')!.textContent).toBe('IMMAT · A1')
    expect(rattachee!.querySelector('.rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    // Une pastille coupee se lit entiere au survol (capture orange du 25/11 : « Recoi », « Taxi-(… »).
    // Faux si : la cellule du mode n'a pas d'infobulle, ou porte autre chose que le libelle affiche.
    expect(rattachee!.querySelector('.rig-lm-pastille')!.closest('td')!.getAttribute('title')).toBe('Poste')
    expect(rattachee!.querySelector('.rig-lm-etat')!.textContent).toBe('faite')
    expect(sans!.querySelector('.rig-lm-etat')!.className).toBe('rig-lm-etat rig-lm-t-orange')
    expect(sans!.querySelector('.rig-lm-etat')!.textContent).toBe('a faire')
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('2 arrivees afficheesA numeriser 1Paiement joint 2Clic sur une arrivee pour ouvrir sa demande.')
  })

  it('le chevron deplie la ligne sans l\'ouvrir ; « Ouvrir la demande » est grise pour une arrivee sans demande', async () => {
    installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranArrivee ouvrirDossier={ouvrirDossier} />)
    await screen.findByText('A1')
    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[1]!)
    expect(ouvrirDossier).not.toHaveBeenCalled()
    expect([...container.querySelectorAll('tr.rig-lm-detail .rig-lm-champ')].map((c) => c.textContent)).toEqual([
      'N ArriveeA2', 'ReferencesREF', 'Case12', 'Paiement jointoui', 'Type de demandeIMMAT', 'Utilisateur designeR. VILAIN', 'Attestationnon'])
    expect((screen.getByRole('button', { name: 'Ouvrir la demande' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.click(screen.getAllByRole('button', { name: 'Deplier la ligne' })[0]!)
    fireEvent.click(screen.getByRole('button', { name: 'Ouvrir la demande' }))
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')
  })

  it('« Aller a une arrivee… » filtre la liste affichee ; Entree ouvre la demande de la seule restante', async () => {
    const pont = installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const ouvrirDossier = vi.fn()
    const { container } = render(<EcranArrivee ouvrirDossier={ouvrirDossier} />)
    await screen.findByText('A1')
    const appelsAvant = pont.appels.mock.calls.length
    const champ = screen.getByRole('textbox', { name: 'Aller a une arrivee…' })
    fireEvent.change(champ, { target: { value: '004176' } })
    expect(rangees(container)).toHaveLength(1)
    expect(pont.appels.mock.calls.length).toBe(appelsAvant)
    fireEvent.keyDown(champ, { key: 'Enter' })
    expect(ouvrirDossier).toHaveBeenCalledWith('004176')
    // Sans accents, comme l'ecran modele : « réf » trouve les references « REF ».
    fireEvent.change(champ, { target: { value: 'réf' } })
    expect(rangees(container)).toHaveLength(2)
    fireEvent.change(champ, { target: { value: 'zzz' } })
    expect(screen.getByText('Aucune des 2 arrivees ne contient « zzz ».')).toBeTruthy()
  })

  it('les vues et les cartes posent les criteres existants (mode, reste a numeriser, paiement)', async () => {
    const pont = installerFauxPont({ 'EcranArrivee.Trouver': vue })
    const { container } = render(<EcranArrivee ouvrirDossier={vi.fn()} />)
    await screen.findByText('A1')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Toutes 2' }).getAttribute('aria-pressed')).toBe('true'))

    fireEvent.click(screen.getByRole('button', { name: 'Teletransmises 2' }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critMode: 'EDI', critNonNumerise: false }))
    expect((screen.getByLabelText(/Mode d/) as HTMLSelectElement).value).toBe('EDI')

    const carte = (titre: string) => [...container.querySelectorAll('.rig-lm-carte')]
      .find((c) => c.querySelector('.rig-lm-carte__titre')!.textContent === titre) as HTMLElement
    fireEvent.click(carte('Reste a numeriser'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critMode: 'EDI', critNonNumerise: true }))
    expect(screen.getByRole('button', { name: 'Reste a numeriser' }).className).toBe('rig-bascule rig-bascule--actif')
    // La carte des modes, filtree sur EDI, se vide d'un clic.
    fireEvent.click(carte('Modes d arrivee'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critMode: '' }))
    fireEvent.click(carte('Modes d arrivee').querySelector('.rig-lm-legende__entree')!)
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critMode: 'POSTE' }))
    fireEvent.click(carte('Paiement joint'))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critPaiement: true }))
  })

  it('chaque mode courant de dbo.MODE_ARRIVEE a sa propre teinte de pastille ; « Autre » reste gris', async () => {
    // Libelles lus dans dbo.MODE_ARRIVEE (Metier/JeuDemo.cs, ModesArrivee), tels que le pont les rend
    // en modeLisible. Faux si : deux modes partagent une teinte (« Recommande » vert comme « Accueil »),
    // ou un libelle n'est plus reconnu et tombe au gris (« e-Mail » ecrit « E-mail »).
    const courants = ['Accueil', 'Poste', 'Recommande', 'Edi', 'e-Mail']
    installerFauxPont({ 'EcranArrivee.Trouver': (): VueArrivee => ({
      modes: [], total: courants.length + 1, aNumeriser: 0,
      lignes: [...courants, 'Autre'].map((m, i) => ({ ...ligne(`A${i}`, '', true), modeLisible: m })),
    }) })
    const { container } = render(<EcranArrivee ouvrirDossier={vi.fn()} />)
    await screen.findByText('A5')
    const teintes = rangees(container).map((r) => r.querySelector('.rig-lm-pastille')!.className)
    const [autre] = teintes.splice(courants.length)
    expect(teintes).not.toContain('rig-lm-pastille rig-lm-t-gris')
    expect(new Set(teintes).size).toBe(courants.length)
    expect(autre).toBe('rig-lm-pastille rig-lm-t-gris')
  })
})
