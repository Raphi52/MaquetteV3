import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranBodacc } from './EcranBodacc'
import { installerFauxPont, retirerFauxPont } from '../test/faux-pont'

afterEach(() => { cleanup(); retirerFauxPont() })

// JeuDemo.Aujourdhui = 09/09/2026 : les annonces prevues le 08/09 et le 04/09
// sont en retard, celle du 09/09 ne l'est pas (capture Blazor du 18/11/2026).
function ouvrir(ouvrirDossier = vi.fn()) {
  installerFauxPont({ 'EcranBodacc.DateDuJour': () => '2026-09-09' })
  return { ouvrirDossier, ...render(<EcranBodacc ouvrirDossier={ouvrirDossier} />) }
}

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const entetes = (c: HTMLElement) => [...c.querySelectorAll('thead th')].map((th) => th.textContent)

describe('EcranBodacc', () => {
  it('« a envoyer » : six colonnes, les annonces en retard surlignees', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    expect(compte(container)).toBe('3 annonce(s)')
    expect(entetes(container)).toHaveLength(6)
    const rangees = [...container.querySelectorAll('tbody tr')]
    expect(rangees.map((r) => r.className)).toEqual(['rig-table__ligne--alerte', '', 'rig-table__ligne--alerte'])
    expect([...rangees[0]!.querySelectorAll('td')].map((td) => td.textContent))
      .toEqual(['PC2026-041', 'IMM', '02/09/2026', 'Immatriculation SARL LES TILLEULS', '08/09/2026', 'Immatriculation'])
  })

  it('n\'affiche aucune ligne tant que la date du jour n\'est pas connue', () => {
    installerFauxPont({ 'EcranBodacc.DateDuJour': () => new Promise<string>(() => {}) })
    const { container } = render(<EcranBodacc ouvrirDossier={vi.fn()} />)
    expect(container.querySelectorAll('tbody tr')).toHaveLength(0)
    expect(screen.queryByText('Aucune annonce ne repond a ces criteres.')).toBeNull()
  })

  it('« deja envoye » : douze colonnes et les dates d\'envoi, de transmission, de parution', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    fireEvent.click(screen.getByRole('button', { name: 'BODACC deja envoye' }))
    expect(screen.getByRole('button', { name: 'BODACC deja envoye' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(entetes(container)).toHaveLength(12)
    expect(compte(container)).toBe('2 annonce(s)')
    const premiere = container.querySelectorAll('tbody tr')[0]!
    expect(premiere.className).toBe('')
    expect([...premiere.querySelectorAll('td')].map((td) => td.textContent)).toEqual(
      ['', 'RAD', '12/08/2026', '18/08/2026', '19/08/2026', 'BD-2610', 'FA-45120', '24/08/2026', '2026-171', 'A2026-1150',
       'Radiation', 'Radiation EURL LE PETIT CAFE'])
  })

  it('filtre par texte et par type ; « Effacer » remet ces criteres a vide', async () => {
    const { container } = ouvrir()
    await screen.findByText('Immatriculation SARL LES TILLEULS')
    expect([...(screen.getByLabelText('Type d\'annonce') as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Tous', 'Depot des comptes', 'Immatriculation', 'Modification', 'Procedure collective', 'Radiation'])
    fireEvent.change(screen.getByLabelText('Num annonce / dossier'), { target: { value: '2019b' } })
    expect(compte(container)).toBe('1 annonce(s)')
    fireEvent.change(screen.getByLabelText('Num annonce / dossier'), { target: { value: 'rien' } })
    const vide = screen.getByText('Aucune annonce ne repond a ces criteres.')
    expect(vide.getAttribute('colspan')).toBe('6')

    fireEvent.change(screen.getByLabelText('Num bordereau'), { target: { value: 'BD' } })
    fireEvent.click(screen.getByRole('button', { name: 'Effacer' }))
    expect(compte(container)).toBe('3 annonce(s)')
    expect((screen.getByLabelText('Num annonce / dossier') as HTMLInputElement).value).toBe('')
    // Comme dans Blazor, « Effacer » ne touche pas aux numeros de bordereau et de facture.
    expect((screen.getByLabelText('Num bordereau') as HTMLInputElement).value).toBe('BD')
  })

  it('une annonce cliquee remonte son dossier', async () => {
    const { ouvrirDossier } = ouvrir()
    fireEvent.click(await screen.findByText('Changement de gerant'))
    expect(ouvrirDossier).toHaveBeenCalledWith('2019B00233')
  })
})
