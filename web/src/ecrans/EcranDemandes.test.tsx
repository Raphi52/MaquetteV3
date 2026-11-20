import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranDemandes, type LigneDemande, type VueDemandes } from './EcranDemandes'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'
import { contexteDossier } from '../test/dossier'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le filtre et l'etat AFFICHE (la validation au controle final prime) sont
// calcules par Pont/EcranDemandes.cs, execute par tests/SaisieCheck. Ici : la
// grille montre ce que le pont rend, et la liste des valides lui est transmise.
const ligne = (numero: string, dossier: string, valide: boolean): LigneDemande => ({
  numero, nom: `Demande ${numero}`, dossier, siren: '109 678 235', client: 'CLIENT', traitement: 'PROC_DEMANDE',
  etat: valide ? 'Validee' : 'En controle', statut: valide ? 'ok' : 'alerte', enCours: !valide,
  creation: '01/09/2026', validation: valide ? '09/09/2026' : '', utilisateur: 'R. VILAIN', alerte: !valide,
  traitementLibelle: 'Demandes',
})

const vue = (_c: unknown, valides: unknown): VueDemandes => ({
  traitements: ['PROC_DEMANDE', 'PROC_DEPOTJUD'], etats: ['En controle', 'Validee'], total: 12,
  lignes: [ligne('D1', '004151', (valides as string[]).includes('004151')), ligne('D2', '004176', false)],
  libellesTraitements: ['Demandes', 'Depots judiciaires'],
})

describe('EcranDemandes', () => {
  it('montre l\'etat rendu par le pont pour les dossiers valides', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={['004151']} ouvrir={vi.fn()} />)

    expect(morceaux(await screen.findByText('2 affichee(s) sur 12'))).toEqual(['2', ' affichee(s) sur ', '12'])
    expect(pont.appels.mock.calls[0]![2]).toEqual(['004151'])
    const rangees = container.querySelectorAll('tbody tr')
    expect(rangees[0]!.querySelector('.rig-etat')!.className).toBe('rig-etat rig-etat--ok')
    expect(rangees[0]!.querySelector('.rig-etat')!.textContent).toBe('Validee')
    expect(rangees[0]!.className).toBe('')
    expect(rangees[1]!.className).toBe('rig-table__ligne--alerte')
    expect(rangees[1]!.querySelectorAll('td')[7]!.textContent).toBe('oui')
  })

  it('le traitement se lit par le nom de son ecran ; la liste Processus garde le code pour valeur', async () => {
    // Parcours du lot 6 (20/11/2026) : ouvert depuis 004151, l'ecran montrait « PROC_DEPOTJUD ».
    // Faux si : le code repartait a l'ecran, ou si le choix d'un processus remontait le libelle
    // au pont (son filtre compare le code).
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    await screen.findByText('2 affichee(s) sur 12')
    expect(container.querySelector('tbody')!.textContent).not.toContain('PROC_')
    expect(container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')[5]!.textContent).toBe('Demandes')
    const liste = screen.getByRole('combobox', { name: 'Processus' })
    expect([...liste.querySelectorAll('option')].map((o) => [o.value, o.textContent]))
      .toEqual([['', 'Tous'], ['PROC_DEMANDE', 'Demandes'], ['PROC_DEPOTJUD', 'Depots judiciaires']])
    fireEvent.change(liste, { target: { value: 'PROC_DEPOTJUD' } })
    await waitFor(() => expect((pont.appels.mock.calls.at(-1)![1] as { critProcessus: string }).critProcessus).toBe('PROC_DEPOTJUD'))
  })

  it('une bascule d\'etat part au pont, « Effacer » remet les criteres', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': vue })
    render(<EcranDemandes valides={[]} ouvrir={vi.fn()} />)
    const bascule = await screen.findByRole('button', { name: 'Validee' })
    fireEvent.click(bascule)
    await waitFor(() => expect((pont.appels.mock.lastCall![1] as { critEtats: string[] }).critEtats).toEqual(['Validee']))
    expect(bascule.className).toBe('rig-bascule rig-bascule--actif')

    fireEvent.change(screen.getByLabelText('Nombre maxi. de demandes affichees'), { target: { value: '12a' } })
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    await waitFor(() => expect(pont.appels.mock.lastCall![1]).toMatchObject({ critEtats: [], critTopMax: 50 }))
  })

  it('un clic sur une demande remonte son dossier', async () => {
    installerFauxPont({ 'EcranDemandes.Trouver': vue })
    const ouvrir = vi.fn()
    render(<EcranDemandes valides={[]} ouvrir={ouvrir} />)
    fireEvent.click(await screen.findByText('Demande D2'))
    expect(ouvrir).toHaveBeenCalledWith('004176')
  })
})

// Ouvert depuis la page d'une affaire (barre d'actions, plan « Vue du dossier »,
// lot 2) : ses demandes, sans saisie. Le faux pont filtre sur « Demande » comme
// Pont/EcranDemandes.cs (le numero ou le nom contient le texte).
describe('EcranDemandes ouvert depuis une affaire', () => {
  const filtrer = (c: unknown): VueDemandes => {
    const t = (c as { critDemande: string }).critDemande
    const toutes = [{ ...ligne('004176', '004176', false), nom: 'Modification - SCI DU PARC' }, ligne('004151', '004151', false)]
    return { traitements: [], etats: [], total: toutes.length, lignes: toutes.filter((l) => l.numero.includes(t) || l.nom.includes(t)), libellesTraitements: [] }
  }

  it('cherche ses demandes par son numero des le premier appel, sans saisie', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': filtrer })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} dossier={contexteDossier()} />)
    expect(await screen.findByText('Modification - SCI DU PARC')).toBeTruthy()
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
    expect(pont.appels.mock.calls[0]![1]).toMatchObject({ critDemande: '004176' })
    expect((screen.getByLabelText('Demande') as HTMLInputElement).value).toBe('004176')
  })

  it('sans affaire (ouvert depuis l\'accueil) : aucun critere, toutes les demandes', async () => {
    const pont = installerFauxPont({ 'EcranDemandes.Trouver': filtrer })
    const { container } = render(<EcranDemandes valides={[]} ouvrir={vi.fn()} dossier={null} />)
    await screen.findByText('Modification - SCI DU PARC')
    expect(container.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(pont.appels.mock.calls[0]![1]).toMatchObject({ critDemande: '' })
    expect((screen.getByLabelText('Demande') as HTMLInputElement).value).toBe('')
  })
})
