import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranDemandes, type LigneDemande, type VueDemandes } from './EcranDemandes'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Le filtre et l'etat AFFICHE (la validation au controle final prime) sont
// calcules par Pont/EcranDemandes.cs, execute par tests/SaisieCheck. Ici : la
// grille montre ce que le pont rend, et la liste des valides lui est transmise.
const ligne = (numero: string, dossier: string, valide: boolean): LigneDemande => ({
  numero, nom: `Demande ${numero}`, dossier, siren: '109 678 235', client: 'CLIENT', traitement: 'IMMAT',
  etat: valide ? 'Validee' : 'En controle', statut: valide ? 'ok' : 'alerte', enCours: !valide,
  creation: '01/09/2026', validation: valide ? '09/09/2026' : '', utilisateur: 'R. VILAIN', alerte: !valide,
})

const vue = (_c: unknown, valides: unknown): VueDemandes => ({
  traitements: ['IMMAT', 'MODIF'], etats: ['En controle', 'Validee'], total: 12,
  lignes: [ligne('D1', '004151', (valides as string[]).includes('004151')), ligne('D2', '004176', false)],
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
