import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { EcranComptabilisation } from './EcranComptabilisation'

afterEach(cleanup)

// Valeurs attendues : celles de l'ecran Blazor (EcranComptabilisation.razor), journee du 09/09/2026 choisie.
// Milliers : euros() (format.ts) les separe par U+202F comme .NET ; montant() (ListeModerne.tsx) par U+00A0.
const lignes = (c: HTMLElement) =>
  [...c.querySelectorAll('tbody tr')].map((tr) => [...tr.querySelectorAll('td')].map((td) => td.textContent))
const valeurs = (c: HTMLElement) => [...c.querySelectorAll('.rig-lm-carte__valeur')].map((v) => v.textContent)

describe('EcranComptabilisation', () => {
  it('affiche les journees, ce qui reste et les remis de la premiere journee', () => {
    const { container } = render(<EcranComptabilisation />)
    expect(screen.getByRole('button', { name: '09/09/2026 — 14 facture(s), 15 encaissement(s)' }).className)
      .toBe('rig-bascule rig-bascule--actif')
    expect([...container.querySelectorAll('.rig-chip')].map((p) => p.textContent)).toEqual([
      '3 jour(s) a comptabiliser', '23 facture(s)', '5 017,20 € encaisses',
    ])
    expect(container.querySelector('.rig-lm-synthese')!.textContent)
      .toBe('14 facture(s) · 3\u00a0812,40 € TTC · 15 encaissement(s) · 3\u00a0812,40 € encaisses')
    // L'agregat des factures de la journee (v2 : FactureJour) est la premiere carte.
    expect(valeurs(container)).toEqual([
      '3\u00a0812,40 € TTC · 14 facture(s) · 11 affaire(s) en cours · 1 sans demande',
      '3\u00a0812,40 €',
      '222,00 € sur 3\u00a0812,40 €',
    ])
    expect(lignes(container)).toEqual([
      ['4821', 'Cheque', '9', '2\u00a0140,00 €', '09/09/2026'],
      ['4822', 'Virement', '4', '1\u00a0450,40 €', '09/09/2026'],
      ['non remis', 'Carte bancaire', '2', '222,00 €', '09/09/2026'],
    ])
    // Remis en point (vert, orange s'il n'est pas remis), mode en pastille.
    expect([...container.querySelectorAll('tbody .rig-lm-etat')].map((e) => e.className.split(' ')[1]))
      .toEqual(['rig-lm-t-vert', 'rig-lm-t-vert', 'rig-lm-t-orange'])
    expect(container.querySelector('tbody .rig-lm-pastille')!.className).toBe('rig-lm-pastille rig-lm-t-bleu')
    expect(container.querySelector('.rig-lm-pied')!.textContent)
      .toBe('3 remis affiche(s)Encaissements 15Total 3\u00a0812,40 €Non remis 222,00 €')
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

  it('les cartes filtrent les remis : un mode, ou les non remis ; une autre journee enleve le filtre', () => {
    const { container } = render(<EcranComptabilisation />)
    fireEvent.click(screen.getByRole('button', { name: 'Virement 1\u00a0450,40 €', pressed: false }))
    expect(lignes(container).map((l) => l[0])).toEqual(['4822'])
    fireEvent.click(screen.getByRole('button', { name: /^Encaissements par mode/ }))
    expect(lignes(container)).toHaveLength(3)

    const nonRemis = container.querySelectorAll<HTMLElement>('.rig-lm-carte')[2]!
    fireEvent.click(nonRemis)
    expect(nonRemis.getAttribute('aria-pressed')).toBe('true')
    expect(lignes(container).map((l) => l[0])).toEqual(['non remis'])
    expect(container.querySelector('.rig-lm-pied')!.textContent).toContain('1 remis affiche(s)')

    // La journee du 08/09 n'a que des remis : le filtre ne la vide pas, il est enleve.
    fireEvent.click(screen.getByRole('button', { name: '08/09/2026 — 6 facture(s), 6 encaissement(s)' }))
    expect(lignes(container).map((l) => l[0])).toEqual(['4818', '4819'])
  })
})
