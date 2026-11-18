import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranRedaction } from './EcranRedaction'

afterEach(cleanup)

const compte = (c: HTMLElement) => c.querySelector('.rig-resultats-proc__compte')!.textContent
const valeur = (libelle: string) => (screen.getByLabelText(libelle) as HTMLInputElement).value

describe('EcranRedaction', () => {
  it('ouvre sur la premiere decision deliberee, ses champs, ses caracteres et ses mesures', () => {
    const { container } = render(<EcranRedaction />)
    expect(compte(container)).toBe('Decision judiciaire liee a 4 documents')
    expect(screen.getByRole('button', { name: '2026J00121 — AMITEL SAS - ouverture' }).className).toBe('rig-bascule rig-bascule--actif')
    expect(valeur('Type de decision')).toBe('Jugement d\'ouverture')
    expect(valeur('Mise en delibere le')).toBe('20/02/2026')
    // Une date absente s'affiche « — ».
    expect(valeur('Prononce le')).toBe('—')
    expect([...container.querySelectorAll('.rig-etat')].map((e) => `${e.textContent}:${e.className}`)).toEqual([
      'Avant dire droit:rig-etat rig-etat--attente', 'Contradictoire:rig-etat rig-etat--ok',
      'Execution provisoire:rig-etat rig-etat--ok', 'Debats publics:rig-etat rig-etat--ok',
      'Decision publique:rig-etat rig-etat--ok', 'Mesure d\'instruction:rig-etat rig-etat--ok',
    ])
    expect(screen.getByText('MES-2026-0114 — expertise comptable').className).toBe('rig-table__mono')
  })

  it('l\'onglet Prononce liste les decisions prononcees ; une decision sans mesure le dit', () => {
    const { container } = render(<EcranRedaction />)
    fireEvent.click(screen.getByRole('button', { name: 'Prononce' }))
    expect(screen.getByRole('button', { name: 'Prononce' }).className).toBe('rig-onglet rig-onglet--actif')
    expect(screen.queryByRole('button', { name: '2026J00121 — AMITEL SAS - ouverture' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '2025J00088 — MARTIN EURL - cloture' }))
    expect(compte(container)).toBe('Decision judiciaire liee a 0 documents')
    expect(valeur('Prononce le')).toBe('20/09/2026')
    expect(screen.getByText('Aucune mesure liee a cette decision.')).toBeTruthy()
  })
})
