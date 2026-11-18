import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranFactures } from './EcranFactures'
import { installerFauxDotNet, retirerFauxDotNet } from '../test/faux-dotnet'

afterEach(() => { cleanup(); retirerFauxDotNet() })

// Les montants attendus sont ceux de la capture de l'ecran Blazor (18/11/2026) :
// Total TTC 458,63 EUR, reste a encaisser 258,63 EUR pour 200 encaisses.
function ouvrir() {
  installerFauxDotNet({ 'EcranFactures.DateDuJour': () => '2026-09-09' })
  return render(<EcranFactures />)
}

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const solde = (c: HTMLElement) => c.querySelector('section > .rig-criteres__bloc > .rig-etat')!

describe('EcranFactures', () => {
  it('affiche le total et le reste comme l\'ecran Blazor', async () => {
    const { container } = ouvrir()
    expect(compte(container)).toBe('Total TTC 458,63 EUR — reste a encaisser 258,63 EUR')
    expect(solde(container).textContent).toBe('Solde restant 258,63 EUR')
    expect(solde(container).className).toBe('rig-etat rig-etat--attente')
    expect(await screen.findByDisplayValue('2026-09-09')).toBeTruthy()
  })

  it('ecrit les colonnes de la grille a la francaise', () => {
    const { container } = ouvrir()
    const cellules = [...container.querySelectorAll('tbody tr')[0]!.querySelectorAll('td')].map((td) => td.textContent)
    expect(cellules).toEqual(['IMM01', 'Immatriculation societe', '1', '37,45', '20,0 %', '105,90', '0,00', '0,00', '194,85', ''])
  })

  it('un clic sur un article le retire du total, un second l\'y remet', () => {
    const { container } = ouvrir()
    const ligne = screen.getByText('IMM01').closest('tr')!
    fireEvent.click(ligne)
    expect(ligne.className).toBe('rig-table__ligne--inerte')
    expect(compte(container)).toBe('Total TTC 263,78 EUR — reste a encaisser 63,78 EUR')
    fireEvent.click(ligne)
    expect(ligne.hasAttribute('class')).toBe(false)
    expect(compte(container)).toBe('Total TTC 458,63 EUR — reste a encaisser 258,63 EUR')
  })

  it('une facture couverte est soldee (calcul exact, pas en virgule flottante)', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '458.63' } })
    expect(solde(container).textContent).toBe('Facture soldee')
    expect(solde(container).className).toBe('rig-etat rig-etat--ok')
  })

  it('une saisie illisible garde le dernier montant lisible, comme le @bind d\'un decimal', () => {
    const { container } = ouvrir()
    fireEvent.change(screen.getByLabelText('Montant encaisse'), { target: { value: '' } })
    expect(compte(container)).toBe('Total TTC 458,63 EUR — reste a encaisser 258,63 EUR')
  })

  it('« Changer de client » fait defiler les fiches et met la fiche a jour', () => {
    ouvrir()
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('LEROY')
    expect(screen.getByText(/Solde client/).textContent).toBe('Solde client 812,40 EUR')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('BLANC')
    expect((screen.getByLabelText('Nom du client') as HTMLSelectElement).value).toBe('CABINET BLANC')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('DIVERS')
    expect(screen.getByText(/Solde client/).className).toBe('rig-etat rig-etat--ok')

    fireEvent.click(screen.getByRole('button', { name: 'Changer de client' }))
    expect((screen.getByLabelText('Abrege') as HTMLInputElement).value).toBe('LEROY')
  })

  it('Facturer / Tarifer : un choix exclusif qui dit ce qu\'il fera', () => {
    ouvrir()
    const facturer = screen.getByRole('button', { name: 'Facturer' })
    const tarifer = screen.getByRole('button', { name: 'Tarifer' })
    expect(facturer.className).toBe('rig-onglet rig-onglet--actif')
    expect(screen.getByText('Facturer : une facture numerotee sera emise.')).toBeTruthy()

    fireEvent.click(tarifer)
    expect(tarifer.className).toBe('rig-onglet rig-onglet--actif')
    expect(facturer.className).toBe('rig-onglet')
    expect(screen.getByText('Tarifer : le montant est calcule sans emettre de facture.')).toBeTruthy()
  })

  it('garde l\'espace entre « Changer de client » et la pastille (bloc en ligne)', () => {
    const { container } = ouvrir()
    const actions = container.querySelector('.rig-criteres__actions')!
    expect(actions.childNodes[1]?.textContent).toBe(' ')
  })
})
