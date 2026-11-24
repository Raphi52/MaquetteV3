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
    expect(container.querySelector('.rig-lm-synthese')!.textContent).toBe('4 banques')
    expect(screen.getByText('Liste (4)')).toBeTruthy()
    // Avatar « BP » (LA ne donne pas d'initiale), designation, code ; compte ; modes en pastilles.
    expect(lignes(container)[2]).toEqual(['BPLA BANQUE POSTALELBP', '512300', 'Cheque Virement Carte bancaire'])
    expect([...container.querySelectorAll('tbody tr')[2]!.querySelectorAll('.rig-lm-pastille')].map((p) => p.className))
      .toEqual(['rig-lm-pastille rig-lm-t-bleu', 'rig-lm-pastille rig-lm-t-violet', 'rig-lm-pastille rig-lm-t-orange'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('4 banques affichees')
    expect(screen.getByRole('button', { name: 'CA — CREDIT AGRICOLE' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(valeur('Designation')).toBe('CREDIT AGRICOLE')
    expect(valeur('Adresse de l\'agence')).toBe('25 rue de Vesle\n51100 Reims')
    expect(screen.getByText('Fournisseur : oui')).toBeTruthy()
  })

  it('la recherche filtre sans casse ; un clic sur une ligne change la fiche', () => {
    const { container } = render(<EcranBanques />)
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'post' } })
    expect(lignes(container).map((l) => l[0])).toEqual(['BPLA BANQUE POSTALELBP'])
    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: 'zzz' } })
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucune banque ne repond a cette recherche.')

    fireEvent.change(screen.getByLabelText('Code ou designation'), { target: { value: '' } })
    fireEvent.click(container.querySelectorAll('tbody tr')[1]!)
    expect(valeur('Designation')).toBe('BANQUE POPULAIRE')
    expect(valeur('Modes de paiement')).toBe('Cheque')
    expect(screen.getByText('Notification : non')).toBeTruthy()
  })
})
