import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranEncaissements } from './EcranEncaissements'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Valeurs attendues : celles de l'ecran Blazor (EcranEncaissements.razor) : 357,57 encaisses,
// deux ventilations choisies, date de virement = JeuDemo.Aujourdhui (09/09/2026).
function ouvrir() {
  const dotnet = installerFauxDotNet({ 'EcranEncaissements.DateDuJour': () => '2026-09-09' })
  return { dotnet, ...render(<EcranEncaissements />) }
}
const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent!.trim()

describe('EcranEncaissements', () => {
  it('affiche la saisie, la date du pont et une ventilation complete', async () => {
    const { container, dotnet } = ouvrir()
    expect(await screen.findByDisplayValue('2026-09-09')).toBeTruthy()
    expect(dotnet.invokeMethodAsync).toHaveBeenCalledWith('RigV3Desktop', 'EcranEncaissements.DateDuJour')
    expect((screen.getByLabelText('Payeur') as HTMLInputElement).value).toBe('SELARL LEROY & ASSOCIES')
    expect(compte(container)).toBe('357,57 EUR ventile(s) sur 357,57 EUR — solde nul')
    expect(container.querySelectorAll('tbody tr.rig-ligne--valide')).toHaveLength(2)
    expect(screen.getByText('Encaissement entierement ventile').className).toBe('rig-etat rig-etat--ok')
    expect(screen.getByText(/Encaissement repris : E-2026-0447/)).toBeTruthy()
  })

  it('decocher une affaire laisse un reste a ventiler ; le montant saisi le change', async () => {
    const { container } = ouvrir()
    await screen.findByDisplayValue('2026-09-09')
    fireEvent.click(container.querySelectorAll('tbody tr')[1]!)
    expect(compte(container)).toBe('194,85 EUR ventile(s) sur 357,57 EUR — 162,72 EUR a ventiler')
    expect(screen.getByText('Ventilation incomplete').className).toBe('rig-etat rig-etat--attente')

    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '194.85' } })
    expect(compte(container)).toBe('194,85 EUR ventile(s) sur 194,85 EUR — solde nul')
    // Une saisie illisible garde le dernier montant lisible (le @bind d'un decimal).
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '' } })
    expect(compte(container)).toBe('194,85 EUR ventile(s) sur 194,85 EUR — solde nul')
  })

  it('« Emettre un recu » eteint fait disparaitre l\'adresse du recu', () => {
    ouvrir()
    expect(screen.getByLabelText('Adresse du recu')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Emettre un recu' }))
    expect(screen.queryByLabelText('Adresse du recu')).toBeNull()
  })
})
