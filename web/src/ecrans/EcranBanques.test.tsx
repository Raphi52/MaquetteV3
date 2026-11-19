import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranBanques } from './EcranBanques'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranBanques.razor), 4 banques, CA choisie.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

describe('EcranBanques', () => {
  it('affiche les quatre banques et la fiche de la premiere', () => {
    const { container } = render(<EcranBanques />)
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('4 banque(s)')
    expect(screen.getByText('Liste (4)')).toBeTruthy()
    expect(lignes(container)[2]).toEqual(['LBP', 'LA BANQUE POSTALE', '512300', 'Cheque, Virement, Carte bancaire'])
    expect(screen.getByRole('button', { name: 'CA — CREDIT AGRICOLE' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(valeur('Designation')).toBe('CREDIT AGRICOLE')
    expect(valeur('Adresse de l\'agence')).toBe('25 rue de Vesle\n51100 Reims')
    expect(screen.getByText('Fournisseur : oui')).toBeTruthy()
  })

  it('la recherche filtre sans casse ; un clic sur une ligne change la fiche', () => {
    const { container } = render(<EcranBanques />)
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'post' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['LBP'])
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune banque ne repond a cette recherche.')

    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: '' } })
    fireEvent.click(container.querySelectorAll('tbody tr')[1]!)
    expect(valeur('Designation')).toBe('BANQUE POPULAIRE')
    expect(valeur('Modes de paiement')).toBe('Cheque')
    expect(screen.getByText('Notification : non')).toBeTruthy()
  })
})
