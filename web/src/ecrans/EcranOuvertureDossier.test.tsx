import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { EcranOuvertureDossier, type ResultatOuverture, type VueOuverture } from './EcranOuvertureDossier'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'
import { morceaux } from '../test/morceaux'

afterEach(() => { cleanup(); retirerFauxPont() })

// Les controles et l'ouverture elle-meme sont faits par
// Pont/EcranOuvertureDossier.cs, execute par tests/OuvertureCheck. Ici : le
// SIREN demande est pre-rempli, la fiche part au pont, un refus garde la fiche.
const ouvert = { numero: '2026B00001', siren: '512345678', denomination: 'SOCIETE Z', forme: 'SAS',
                 immatriculation: '09/09/2026', capital: '1 000', monnaie: 'EUR', origine: 'Guichet unique (INPI)' }

describe('EcranOuvertureDossier', () => {
  it('pre-remplit le SIREN recu d\'une recherche restee vide, et la date du jour du pont', async () => {
    installerFauxPont({ 'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: [] }) })
    render(<EcranOuvertureDossier sirenDemande=" 512345678 " />)
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('512345678')
    await waitFor(() => expect((screen.getByLabelText('Date d\'immatriculation') as HTMLInputElement).value).toBe('2026-09-09'))
    expect(morceaux(screen.getByText('0 dossier(s)'))).toEqual(['0', ' dossier(s)'])
  })

  it('ouvre le dossier par le pont, puis vide la fiche ; un refus la garde', async () => {
    const reponses: ResultatOuverture[] = [
      { message: 'La denomination est obligatoire pour ouvrir un dossier.', bloquant: true, ouverts: [] },
      { message: 'Dossier 2026B00001 ouvert pour SOCIETE Z', bloquant: false, ouverts: [ouvert] },
    ]
    const pont = installerFauxPont({
      'EcranOuvertureDossier.Vue': (): VueOuverture => ({ aujourdhui: '2026-09-09', ouverts: [] }),
      'EcranOuvertureDossier.Ouvrir': () => reponses.shift(),
    })
    render(<EcranOuvertureDossier sirenDemande="512345678" />)
    await waitFor(() => expect(pont.appels).toHaveBeenCalledWith('EcranOuvertureDossier.Vue'))
    const bouton = screen.getByRole('button', { name: /Ouvrir le dossier/ })

    fireEvent.click(bouton)
    await screen.findByText('La denomination est obligatoire pour ouvrir un dossier.')
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('512345678')

    fireEvent.click(bouton)
    await screen.findByText('Dossier 2026B00001 ouvert pour SOCIETE Z')
    expect(pont.appels.mock.lastCall![1]).toMatchObject({ siren: '512345678', dateImmat: '2026-09-09', capital: 1000 })
    expect((screen.getByLabelText('N° SIREN') as HTMLInputElement).value).toBe('')
    expect(screen.getByText('2026B00001')).toBeTruthy()
  })
})
