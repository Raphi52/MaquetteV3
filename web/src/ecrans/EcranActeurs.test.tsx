import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranActeurs } from './EcranActeurs'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranActeurs.razor) : 5 acteurs dont 4 actifs,
// LEROY (mandataire) choisi au depart.
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value
const puces = (c: HTMLElement) => [...c.querySelectorAll('.rig-chip')].map((p) => p.textContent)

describe('EcranActeurs', () => {
  it('affiche les acteurs actifs et la fiche du mandataire LEROY', () => {
    const { container } = render(<EcranActeurs />)
    expect(screen.getByText('Resultats (4)')).toBeTruthy()
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('LEROY')
    expect(valeur('Debut de fonction')).toBe('08/01/2018')
    expect(valeur('Fin de fonction')).toBe('')
    expect(valeur('SIREN')).toBe('402 118 553')
    expect(puces(container)).toEqual([
      'Administrateur judiciaire', 'Commissaire a l\'execution du plan',
      'Commissaire priseur', 'Mandataire conciliation', 'RDC liquidation MDJ',
    ])
    // Statuts : Distinct().Order() apres « Tous ».
    expect([...(screen.getByLabelText('Statut', { selector: 'select' }) as HTMLSelectElement).options].map((o) => o.text))
      .toEqual(['Tous', 'En fonction', 'Retiree'])
  })

  it('le type commande les champs : juge, greffier, expert inactif', () => {
    const { container } = render(<EcranActeurs />)
    fireEvent.click(screen.getByRole('button', { name: 'PETIT — PETIT' }))
    expect(screen.queryByLabelText('SIREN')).toBeNull()
    expect(puces(container)).toHaveLength(12)

    fireEvent.click(screen.getByRole('button', { name: 'DUVAL — DUVAL' }))
    expect(valeur('Compte utilisateur lie')).toBe('hduval')

    fireEvent.click(screen.getByRole('button', { name: 'Acteurs actifs uniquement' }))
    expect(screen.getByText('Resultats (5)')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'MOREAU — MOREAU' }))
    expect(valeur('Fin de fonction')).toBe('31/12/2024')
    expect(screen.getByText('Inactif').className).toBe('rig-etat rig-etat--attente')
  })
})
