import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranDepotsJud } from './EcranDepotsJud'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

function ouvrir(ouvrirDossier = vi.fn()) {
  const pont = installerFauxPont({ 'EcranDepotsJud.DateDuJour': () => '2026-09-09' })
  return { pont, ouvrirDossier, ...render(<EcranDepotsJud ouvrirDossier={ouvrirDossier} />) }
}

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const titre = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__entete .rig-kicker')!.textContent

describe('EcranDepotsJud', () => {
  it('ouvre sur l\'etape de recherche d\'instance, avec les quatre instances', () => {
    const { container } = ouvrir()
    expect(titre(container)).toBe('Etape de recherche d\'instance')
    expect(compte(container)).toBe('4 instance(s)')
    expect(screen.getByRole('button', { name: 'Etape de recherche d\'instance' }).className).toBe('rig-bascule rig-bascule--actif')
    const premiere = container.querySelectorAll('tbody tr')[0]!
    expect([...premiere.querySelectorAll('td')].map((td) => td.textContent)).toEqual(
      ['2026J00121', 'SARL BOULANGERIE DUVAL', 'Redressement judiciaire', '1re', '04/03/2026', 'Jugement d\'ouverture', 'En cours', 'oui'])
    expect(premiere.querySelector('.rig-etat')!.className).toBe('rig-etat rig-etat--action')
  })

  it('l\'etape Facturation ne garde que les instances a facturer', () => {
    const { container } = ouvrir()
    fireEvent.click(screen.getByRole('button', { name: 'Facturation et gestion commerciale' }))
    expect(titre(container)).toBe('Facturation et gestion commerciale')
    expect(compte(container)).toBe('2 instance(s)')
  })

  it('l\'etape « Nouvel evenement survenu » montre l\'evenement, date du jour venue du pont', async () => {
    const { pont } = ouvrir()
    expect(screen.queryByLabelText('Evenement survenu')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Nouvel evenement survenu' }))
    expect((screen.getByLabelText('Evenement survenu') as HTMLSelectElement).value).toBe('JUG')
    expect(await screen.findByDisplayValue('2026-09-09')).toBe(screen.getByLabelText('Date de l\'evenement'))
    expect(pont.appels).toHaveBeenCalledWith('EcranDepotsJud.DateDuJour')
  })

  it('cherche par numero et par denomination, sans casse', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('N d\'instance'), { target: { value: '2026j' } })
    expect(compte(container)).toBe('2 instance(s)')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'marne' } })
    expect(compte(container)).toBe('1 instance(s)')
    fireEvent.change(screen.getByLabelText('Denomination'), { target: { value: 'inconnue' } })
    expect(screen.getByText('Aucune instance ne repond a cette recherche.')).toBeTruthy()
  })

  it('une instance cliquee remonte son dossier a la coquille Blazor', () => {
    const { ouvrirDossier } = ouvrir()
    fireEvent.click(screen.getByText('2025J00088'))
    expect(ouvrirDossier).toHaveBeenCalledWith('2023B01120')
  })
})
