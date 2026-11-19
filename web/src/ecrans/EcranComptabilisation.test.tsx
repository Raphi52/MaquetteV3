import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranComptabilisation } from './EcranComptabilisation'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranComptabilisation.razor), journee du 09/09/2026 choisie.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))

describe('EcranComptabilisation', () => {
  it('affiche les journees, ce qui reste et les remis de la premiere journee', () => {
    const { container } = render(<EcranComptabilisation />)
    expect(screen.getByRole('button', { name: '09/09/2026 — 14 facture(s), 15 encaissement(s)' }).className)
      .toBe('rig-bascule rig-bascule--actif')
    expect([...container.querySelectorAll('.rig-chip')].map((p) => p.textContent)).toEqual([
      '3 jour(s) a comptabiliser', '23 facture(s)', '5 017,20 € encaisses',
      '14 facture(s)', '3 812,40 € TTC', '11 affaire(s) en cours', '1 sans demande',
    ])
    expect(container.querySelector('.rig-resultats-proc__compte')!.textContent).toBe('14 facture(s) — 3 812,40 € TTC')
    expect(lignes(container)).toEqual([
      ['4821', 'Cheque', '9', '2 140,00 €', '09/09/2026'],
      ['4822', 'Virement', '4', '1 450,40 €', '09/09/2026'],
      ['non remis', 'Carte bancaire', '2', '222,00 €', '09/09/2026'],
    ])
  })

  it('choisir une autre journee change l\'en-tete et la grille', () => {
    const { container } = render(<EcranComptabilisation />)
    fireEvent.click(screen.getByRole('button', { name: '07/09/2026 — 3 facture(s), 0 encaissement(s)' }))
    expect(screen.getByText('Journee du 07/09/2026')).toBeTruthy()
    expect(container.querySelector('.rig-table__vide')!.textContent).toBe('Aucun encaissement pour cette journee.')
    // Les boutons de .rig-criteres__actions gardent le blanc que Razor laissait entre eux.
    expect(container.querySelector('.rig-criteres__actions')!.textContent)
      .toBe('Comptabiliser les factures Comptabiliser les encaissements Remettre en banque Lettrage des PV et REG')
  })
})
